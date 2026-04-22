using System;
using System.Linq;
using GlobalNews;
using HarmonyLib;
using Persons;
using Reportables;
using Reportables.News;
using Risks;
using Risks.UI;
using Tower_Stats;
using UI;

namespace NewsTowerAutoAssign
{
    [HarmonyPatch(typeof(LiveReportableManager), "Awake")]
    static class Patch_LRMAwake
    {
        static void Postfix()
        {
            try
            {
                SafetyGate.Close();
                AssignmentLog.ResetForNewSave();
                AutoAssignOwnershipRegistry.ResetForNewSave();
                BribeAutomation.ResetForNewSave();
                AssignmentEvaluator.ClearPendingHolderRelease();
                AssignmentLog.Verbose(
                    "PATCH",
                    "LiveReportableManager.Awake - SafetyGate closed, decision log + bribe cache reset"
                );
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_LRMAwake.Postfix: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(ReportableGlobeHolderComponent), "AssignHolder")]
    static class Patch_AssignHolder
    {
        static void Postfix(
            ReportableGlobeHolderComponent __instance,
            IReportableHolder reportableHolder
        )
        {
            try
            {
                if (reportableHolder == null)
                    return;
                if (reportableHolder.CurrentHolding != __instance)
                    return;
                if (!AssignmentEvaluator._pendingHolderRelease.Remove(__instance.Reportable))
                    return;

                reportableHolder.CurrentHolding = null;
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_AssignHolder.Postfix: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(LiveReportableManager), "AddReportable")]
    static class Patch_AddReportable
    {
        static void Postfix(Reportable reportable)
        {
            try
            {
                AssignmentLog.Verbose("PATCH", "AddReportable: " + reportable?.GetType().Name);

                if (reportable is Ad ad)
                {
                    AdAutomation.TryAssignAd(ad);
                    return;
                }
                if (reportable is NewsItem newsItem && newsItem.Data != null)
                    HandleAddedNewsItem(newsItem);
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_AddReportable.Postfix: " + ex);
            }
        }

        private static void HandleAddedNewsItem(NewsItem newsItem)
        {
            LogStoryTags(newsItem);
            if (BribeAutomation.StoryIsPlayerBribeControlled(newsItem))
                return;
            BribeAutomation.TryPayBribes(newsItem);
            AssignmentEvaluator.TryAssignNewsItem(newsItem);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogStoryTags(NewsItem newsItem)
        {
#if DEBUG
            if (AutoAssignPlugin.VerboseLogs == null || !AutoAssignPlugin.VerboseLogs.Value)
                return;
            var tagNames = newsItem
                .Data.DistinctStatTypes.OfType<PlayerStatDataTag>()
                .Select(statTag => statTag.name)
                .ToList();
            AssignmentLog.Verbose(
                "TAGS",
                AssignmentLog.StoryName(newsItem)
                    + " → ["
                    + (tagNames.Count > 0 ? string.Join(", ", tagNames) : "none")
                    + "]"
            );
#endif
        }
    }

    [HarmonyPatch(typeof(IdleWorkplaceState), "DoState")]
    static class Patch_IdleWorkplaceDoState
    {
        private const float MinScanIntervalSeconds = 1f;

        private static float _lastScanTime = 0f;

        static void Prefix()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastScanTime < MinScanIntervalSeconds)
                    return;
                _lastScanTime = now;
                SafetyGate.Open();
                AssignmentLog.Verbose("PATCH", "IdleWorkplaceState.DoState - rescanning");
                AssignmentEvaluator.TryAutoAssignAll();
                AdAutomation.TryAssignAds();
#if DEBUG
                InGameTests.InGameTestRunner.RunOnceWhenReady();
#endif
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_IdleWorkplaceDoState.Prefix: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(LiveReportableManager), "OnAfterLoadStart")]
    static class Patch_AfterLoad
    {
        static void Postfix()
        {
            try
            {
                AssignmentLog.Verbose("PATCH", "OnAfterLoadStart - scanning existing news");
                SafetyGate.Open();
                if (LiveReportableManager.Instance != null)
                    foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems().ToList())
                    {
                        if (newsItem?.Data == null)
                            continue;
                        if (BribeAutomation.StoryIsPlayerBribeControlled(newsItem))
                            continue;
                        BribeAutomation.TryPayBribes(newsItem);
                        SuitcaseAutomation.TryResolveSuitcases(newsItem);
                        if (AssignmentEvaluator.IsAnySlotInProgress(newsItem))
                        {
                            AutoAssignOwnershipRegistry.MarkModAutoAssigned(newsItem);
                            GlobeAttentionSync.PromoteFullySeen(newsItem);
                        }
                    }
                AssignmentEvaluator.TryAutoAssignAll();
                AdAutomation.TryAssignAds();
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_AfterLoad.Postfix: " + ex);
            }
        }
    }

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
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_SuitcasePopupAutoSkip.Postfix: " + ex);
            }
        }
    }

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
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_RiskPopupAutoSkip.Postfix: " + ex);
            }
        }
    }

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
            catch (Exception ex)
            {
                AssignmentLog.Error("Patch_RiskPopupPlayLog.Prefix: " + ex);
            }
        }
    }
}
