using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalNews;
using Reportables;

namespace NewsTowerAutoAssign
{
    internal static class SuitcaseAutomation
    {
        private static readonly Dictionary<Type, MethodInfo> _unlockItemMethodCache =
            new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, PropertyInfo> _didActPropertyCache =
            new Dictionary<Type, PropertyInfo>();

        private static readonly HashSet<string> _reportedReflectionMisses = new HashSet<string>();

        private const BindingFlags UnlockItemFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags DidActFlags =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.FlattenHierarchy;

        private const string UnlockItemMethodName = "UnlockItem";
        private const string DidActPropertyName = "DidAct";

        internal static (string typeName, string[] missing) ProbeReflectionTargets()
        {
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
            if (BribeAutomation.StoryIsPlayerBribeControlled(newsItem))
                return;

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
                AssignmentLog.Error(
                    "SuitcaseAutomation.TryResolveSuitcases("
                        + AssignmentLog.StoryName(newsItem)
                        + "): "
                        + ex
                );
            }
        }

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
