using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalNews;
using Reportables;

namespace NewsTowerAutoAssign
{
    // Resolves the "new item unlocked" suitcase reward without opening the UI popup.
    //
    // Why this exists: when a story's chain reaches a NewsItemSuitcaseBuildable node,
    // the node unlocks but the game does NOT run the unlock side-effect until the
    // player makes the story visible (opens the newsbook page). See
    // NewsItemSuitcase.OnVisibilityChanged - it is the only call site that sets DidAct,
    // fires the popup Play event, and invokes UnlockItem. Players who rely on the mod
    // to handle the board without opening stories never trigger visibility, so the
    // node sits at NodeState.Unlocked forever, IsCompleted stays false, and the chain
    // stalls - blocking the reporter from picking up new stories.
    //
    // Fix: scan each story on every evaluation cycle. For any suitcase whose node is
    // Unlocked and DidAct==false, replicate the game's own "act" sequence:
    //   1. call UnlockItem() via reflection (protected abstract) - identical unlock
    //      side-effect to the normal flow, including the DRNG draw from
    //      BuildUnlockListManager.TryUnlockFromList.
    //   2. set DidAct=true via reflection (protected set) - so if the player later
    //      opens the story, OnVisibilityChanged takes the short-circuit branch
    //      (DidAct already true -> just set IsCompleted=true, no double-fire).
    //   3. set IsCompleted=true (public set) - flips the NewsItemNodeCompleter flag
    //      that gates the chain, freeing the story to progress.
    //
    // The SuitcasePopup never opens. Player sees the unlocked building in the build
    // menu and a mod log line naming the item. Patch_SuitcasePopupAutoSkip remains
    // as a belt-and-braces fallback for any popup that still manages to open (e.g.
    // a popup that armed itself before the mod got a chance to scan).
    internal static class SuitcaseAutomation
    {
        // Cached per runtime type - NewsItemSuitcase<TData> is generic, so we can't
        // resolve the inherited DidAct / UnlockItem members at compile time without
        // knowing TData. Caching per GetType() is sufficient because there are only
        // a couple of concrete subclasses in the game.
        private static readonly Dictionary<Type, MethodInfo> _unlockItemMethodCache =
            new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, PropertyInfo> _didActPropertyCache =
            new Dictionary<Type, PropertyInfo>();

        // Types we've already reported a reflection miss for. AssignmentLog.Error
        // is NOT [Conditional("DEBUG")], so without this set a game update that
        // renames UnlockItem / DidAct would spam the player's BepInEx log once
        // per idle-state scan, forever. Reported once per (type, member) pair.
        private static readonly HashSet<string> _reportedReflectionMisses = new HashSet<string>();

        // Binding flags are shared between the startup probe and the per-type
        // resolution below so they can't drift apart. `FlattenHierarchy` is
        // required for DidAct because the property is declared on the generic
        // base NewsItemSuitcase<TData>.
        private const BindingFlags UnlockItemFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags DidActFlags =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.FlattenHierarchy;

        private const string UnlockItemMethodName = "UnlockItem";
        private const string DidActPropertyName = "DidAct";

        // Used by AutoAssignPlugin.VerifyReflection to surface "a game update
        // renamed the abstract NewsItemSuitcase.UnlockItem / DidAct members"
        // at plugin load time rather than at first-suitcase scan. Returns the
        // (first known subclass, missing member names) so the log line can
        // point the user at the specific game version mismatch.
        internal static (string typeName, string[] missing) ProbeReflectionTargets()
        {
            // Use typeof over a string-keyed Type.GetType lookup: this file
            // already references NewsItemSuitcaseBuildable statically, so a
            // rename in the game would fail the compile (loud) rather than
            // silently returning "<not-found>" at runtime (quiet).
            var suitcaseType = typeof(NewsItemSuitcaseBuildable);
            var missing = new List<string>();
            if (suitcaseType.GetMethod(UnlockItemMethodName, UnlockItemFlags) == null)
                missing.Add(UnlockItemMethodName);
            if (suitcaseType.GetProperty(DidActPropertyName, DidActFlags) == null)
                missing.Add(DidActPropertyName);
            return (suitcaseType.FullName, missing.ToArray());
        }

        internal static void TryResolveSuitcases(NewsItem newsItem)
        {
            if (!AutoAssignPlugin.AutoSkipSuitcasePopups.Value || newsItem == null)
                return;
            // Defer to the universal safety gate - before save restoration has
            // completed, calling UnlockItem -> TryUnlockFromList -> GetOrCreateList
            // seeds a fresh entry in BuildUnlockListManager.lists that
            // AddFromLoadGame later collides with (Dictionary.Add throws on
            // duplicate keys - the "Load Error: An item with the same key has
            // already been added" the player sees). See SafetyGate for the
            // full rationale and the open/close event map.
            if (!SafetyGate.IsOpen)
                return;

            try
            {
                foreach (
                    var suitcase in newsItem.GetComponentsInChildren<NewsItemSuitcaseBuildable>(
                        true
                    )
                )
                    TryResolveSuitcase(newsItem, suitcase);
            }
            catch (Exception ex)
            {
                // Reflection / Unity component walks on an in-flight story can
                // surface transient nulls - log once and move on rather than
                // let the exception climb into the evaluator or Harmony patch.
                AssignmentLog.Error(
                    "SuitcaseAutomation.TryResolveSuitcases("
                        + AssignmentLog.StoryName(newsItem)
                        + "): "
                        + ex
                );
            }
        }

        // Per-suitcase flow: skip when already resolved or not unlocked,
        // resolve reflection handles (cached), apply the "act" sequence in
        // the exact order the game's own OnVisibilityChanged uses, then log.
        private static void TryResolveSuitcase(
            NewsItem newsItem,
            NewsItemSuitcaseBuildable suitcase
        )
        {
            if (!IsSuitcaseResolvable(suitcase))
                return;

            var type = suitcase.GetType();
            var unlockItem = ResolveUnlockItem(type);
            if (unlockItem == null)
                return;
            var didActProp = ResolveDidAct(type);
            if (didActProp == null)
                return;

            ApplyActSequence(suitcase, unlockItem, didActProp);
            LogItemUnlocked(newsItem, suitcase);
        }

        private static bool IsSuitcaseResolvable(NewsItemSuitcaseBuildable suitcase)
        {
            if (suitcase == null || suitcase.IsCompleted || suitcase.DidAct)
                return false;
            var node = suitcase.Node;
            return node != null && node.NodeState == NewsItemNodeState.Unlocked;
        }

        // Order mirrors the game's own flow in OnVisibilityChanged:
        // DidAct -> UnlockItem -> IsCompleted. Setting DidAct first is
        // important because some subscribers to the visibility-triggered
        // state check it mid-unlock and would otherwise re-enter.
        private static void ApplyActSequence(
            NewsItemSuitcaseBuildable suitcase,
            MethodInfo unlockItem,
            PropertyInfo didActProp
        )
        {
            didActProp.SetValue(suitcase, true, null);
            unlockItem.Invoke(suitcase, null);
            suitcase.IsCompleted = true;
        }

        private static void LogItemUnlocked(NewsItem newsItem, NewsItemSuitcaseBuildable suitcase)
        {
            AssignmentLog.ClearSuppression(newsItem);
            AssignmentLog.Decision(
                AssignmentLog.StoryName(newsItem)
                    + " "
                    + AssignmentLog.StoryTagList(newsItem)
                    + " → ITEM UNLOCKED: "
                    + GetUnlockedItemName(suitcase)
                    + " (suitcase node auto-resolved, chain unblocked)."
            );
        }

        // Best-effort name for the log line. UnlockedItem accessors can throw
        // on partially-initialised assets during early load; we fall back to
        // a placeholder rather than let the exception abort the log.
        private static string GetUnlockedItemName(NewsItemSuitcaseBuildable suitcase)
        {
            try
            {
                if (suitcase.UnlockedItem == null)
                    return "<list empty>";
                return suitcase.UnlockedItem.UnlocalizedBuildname_Safe
                    ?? suitcase.UnlockedItem.name;
            }
            catch (Exception)
            {
                return "<unnamed>";
            }
        }

        // Looks up the private UnlockItem method for a concrete suitcase type
        // and caches the result (including a null cache-entry on miss so we
        // don't repeat the reflection lookup every scan).
        //
        // Emits an Error log exactly once per type so a game update that
        // rename/removes the member is visible at the first scan and then
        // stays quiet - Error is NOT stripped in Release.
        private static MethodInfo ResolveUnlockItem(Type type)
        {
            if (!_unlockItemMethodCache.TryGetValue(type, out var method))
            {
                method = type.GetMethod(UnlockItemMethodName, UnlockItemFlags);
                _unlockItemMethodCache[type] = method;
            }
            if (method == null)
                ReportReflectionMissOnce(type, UnlockItemMethodName);
            return method;
        }

        // Same contract as ResolveUnlockItem, but for the DidAct property on
        // the generic base NewsItemSuitcase<TData>.
        private static PropertyInfo ResolveDidAct(Type type)
        {
            if (!_didActPropertyCache.TryGetValue(type, out var property))
            {
                property = type.GetProperty(DidActPropertyName, DidActFlags);
                _didActPropertyCache[type] = property;
            }
            if (property == null)
                ReportReflectionMissOnce(type, DidActPropertyName);
            return property;
        }

        private static void ReportReflectionMissOnce(Type type, string memberName)
        {
            string key = type.FullName + "." + memberName;
            if (!_reportedReflectionMisses.Add(key))
                return;
            AssignmentLog.Error(
                "SuitcaseAutomation: "
                    + memberName
                    + " not found on "
                    + type.FullName
                    + " - suitcase auto-resolve disabled for this type this session."
            );
        }
    }
}
