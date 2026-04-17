using System;
using System.Linq;
using GlobalNews;
using HarmonyLib;
using Persons;
using Reportables;
using Risks;
using Risks.UI;
using Tower_Stats;
using UI;

namespace NewsTowerAutoAssign
{
    // All Harmony patch bodies are wrapped in try/catch so a bug in our mod
    // can never propagate into the game's own frame loop. Exceptions from a
    // Prefix/Postfix are otherwise logged by Harmony as warnings and - for
    // some patch sites - can genuinely mis-sequence game state. We'd rather
    // the player keep playing with a one-off Error in the BepInEx log than
    // have the game misbehave because of us.
    [HarmonyPatch(typeof(LiveReportableManager), "AddReportable")]
    static class Patch_AddReportable
    {
        static void Postfix(Reportable reportable)
        {
            try
            {
                AssignmentLog.Verbose("PATCH", "AddReportable: " + reportable?.GetType().Name);
                var newsItem = reportable as NewsItem;
                if (newsItem?.Data == null)
                    return;

#if DEBUG
                // Dump the story's PlayerStatDataTags so faction / composed-quest
                // injections (e.g. Red Herring on "SUSPICIOUS SHIPMENT STOPPED")
                // are visible the moment they arrive.
                if (AutoAssignPlugin.VerboseLogs != null && AutoAssignPlugin.VerboseLogs.Value)
                {
                    var tagNames = newsItem
                        .Data.DistinctStatTypes.OfType<PlayerStatDataTag>()
                        .Select(t => t.name)
                        .ToList();
                    AssignmentLog.Verbose(
                        "TAGS",
                        AssignmentLog.StoryName(newsItem)
                            + " → ["
                            + (tagNames.Count > 0 ? string.Join(", ", tagNames) : "none")
                            + "]"
                    );
                }
#endif
                BribeAutomation.TryPayBribes(newsItem);
                SuitcaseAutomation.TryResolveSuitcases(newsItem);
                AssignmentEvaluator.TryAssignNewsItem(newsItem);
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_AddReportable.Postfix: " + e);
            }
        }
    }

    // Fires when a reporter physically returns to the tower and the state machine
    // transitions them to their desk-idle state - AFTER the completed story has been
    // deposited.
    [HarmonyPatch(typeof(IdleWorkplaceState), "DoState")]
    static class Patch_IdleWorkplaceDoState
    {
        private static float _lastScanTime = 0f;

        static void Prefix()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastScanTime < 1f)
                    return;
                _lastScanTime = now;
                AssignmentLog.Verbose("PATCH", "IdleWorkplaceState.DoState - rescanning");
                AssignmentEvaluator.TryAutoAssignAll();
#if DEBUG
                // In-game tests are developer-only and are compiled out of
                // Release builds. See NewsTowerAutoAssign.csproj for the
                // Configuration-conditional Compile Remove that excludes
                // InGameTests/** in Release.
                InGameTests.InGameTestRunner.RunOnceWhenReady();
#endif
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_IdleWorkplaceDoState.Prefix: " + e);
            }
        }
    }

    // Fires when a save game is loaded - pay any outstanding bribes and scan all existing news items.
    [HarmonyPatch(typeof(LiveReportableManager), "OnAfterLoadStart")]
    static class Patch_AfterLoad
    {
        static void Postfix()
        {
            try
            {
                AssignmentLog.Verbose("PATCH", "OnAfterLoadStart - scanning existing news");
                if (LiveReportableManager.Instance != null)
                    foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems().ToList())
                        if (newsItem?.Data != null)
                        {
                            BribeAutomation.TryPayBribes(newsItem);
                            SuitcaseAutomation.TryResolveSuitcases(newsItem);
                        }
                AssignmentEvaluator.TryAutoAssignAll();
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_AfterLoad.Postfix: " + e);
            }
        }
    }

    // Auto-dismisses the new-story suitcase popup every frame so the player never has to click.
    // SuitcasePopup.Flush() locks the game (GameLockFlags.FullButAllowPause) and runs a 1s
    // open animation + 3s wait per reward card. Setting didSkip=true causes both loops to
    // exit immediately, completing the popup within one frame.
    [HarmonyPatch(typeof(SuitcasePopup), "Update")]
    static class Patch_SuitcasePopupAutoSkip
    {
        static void Postfix(SuitcasePopup __instance)
        {
            try
            {
                if (
                    __instance != null
                    && AutoAssignPlugin.AutoSkipSuitcasePopups.Value
                    && __instance.IsBusy
                )
                    Traverse.Create(__instance).Field("didSkip").SetValue(true);
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_SuitcasePopupAutoSkip.Postfix: " + e);
            }
        }
    }

    // Auto-dismisses risk spinner popups every frame so the player never has to click.
    // The risk outcome (None / Medium / Severe) is already decided and applied by the game
    // before the popup opens - the spinner is purely cosmetic.
    [HarmonyPatch(typeof(RiskPopup), "Update")]
    static class Patch_RiskPopupAutoSkip
    {
        static void Postfix(RiskPopup __instance)
        {
            try
            {
                if (__instance != null && AutoAssignPlugin.AutoSkipRiskPopups.Value)
                    Traverse.Create(__instance).Field("shouldSkip").SetValue(true);
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_RiskPopupAutoSkip.Postfix: " + e);
            }
        }
    }

    // Logs the resolved outcome of each risk popup exactly once.
    // AssignmentLog.Decision is [Conditional("DEBUG")] so this is a no-op in
    // Release builds - the patch itself remains installed but emits nothing.
    [HarmonyPatch(typeof(RiskPopup), "Play")]
    static class Patch_RiskPopupPlayLog
    {
        static void Prefix(RiskPopupArgs args)
        {
            try
            {
                var riskName = args.riskType != null ? args.riskType.name : "?";
                var employeeName = args.context != null ? args.context.name : "unassigned";
                AssignmentLog.Decision(
                    "Risk resolution ("
                        + riskName
                        + ") for "
                        + employeeName
                        + ": severity="
                        + args.severity
                        + (args.isLucky ? " (lucky)" : "")
                        + "."
                );
            }
            catch (Exception e)
            {
                AssignmentLog.Error("Patch_RiskPopupPlayLog.Prefix: " + e);
            }
        }
    }
}
