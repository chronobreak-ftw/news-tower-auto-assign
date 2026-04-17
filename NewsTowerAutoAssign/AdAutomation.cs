using System;
using System.Collections.Generic;
using System.Linq;
using _Game._Common;
using Assigner;
using Employees;
using GlobalNews;
using Persons;
using Reportables;
using Reportables.News;
using Skills;

namespace NewsTowerAutoAssign
{
    // Auto-assigns idle staff to ads on the Ads tab.
    //
    // Ads are NOT news items, but mechanically they share the same
    // NewsItemStoryFile / IAssignable<...> / AssignTo machinery. The Ads tab
    // (UI.AdsMenu) lists every Ad with ReportableFlags.IsOnGlobe; that's the
    // same set we iterate from LiveReportableManager.OnAdBoard.
    //
    // We do not implement risk / weekend / dead-end discard logic for ads:
    //   - Ads don't carry the same risk components reporter stories do.
    //   - Ads can expire on their own (ReportableExpireData) - the game handles
    //     removing them from the board. We simply re-scan every tick.
    //   - Boycotted ads (Ad.HasBoycott) are skipped because the ad's own
    //     setter unassigns from the newspaper slot when boycott flips on.
    //
    // The reporter-count gate (MinReportersToActivate) is intentionally NOT
    // applied here. Ads are worked by salespeople / copy editors / typesetters
    // / assemblers - the count of *reporters* is irrelevant. The user's
    // AutoAssignAds toggle is the only kill switch.
    internal static class AdAutomation
    {
        // Reentrancy guard. Main-thread-only: Harmony patches fire on the
        // Unity main thread, so a non-volatile bool is sufficient. Do not
        // touch from a background thread. Mirrors AssignmentEvaluator.
        private static bool _isAssigning;

        internal static bool ProgressDoneEventFieldAvailable =>
            GameReflection.ProgressDoneEventFieldAvailable;

        internal static void TryAssignAds()
        {
            if (_isAssigning)
                return;
            if (!AutoAssignPlugin.AutoAssignAds.Value)
                return;
            if (LiveReportableManager.Instance == null)
                return;
            if (!SafetyGate.IsOpen)
                return;

            _isAssigning = true;
            try
            {
                foreach (var ad in LiveReportableManager.Instance.OnAdBoard.ToList())
                    TryAssignAdInternal(ad);
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("AdAutomation.TryAssignAds: " + ex);
            }
            finally
            {
                _isAssigning = false;
            }
        }

        // Called from Patch_AddReportable when a new Ad is added so we react
        // immediately rather than waiting for the next periodic scan tick.
        internal static void TryAssignAd(Ad ad)
        {
            if (_isAssigning)
                return;
            if (!AutoAssignPlugin.AutoAssignAds.Value)
                return;
            if (ad == null || ad.Data == null)
                return;
            if (!SafetyGate.IsOpen)
                return;
            // OnCreatedLive sets IsOnGlobe via EvaluateFlags; if it hasn't
            // landed yet, defer to the next periodic scan rather than try to
            // assign an ad that isn't actually on the board.
            if ((ad.Flags & ReportableFlags.IsOnGlobe) == ReportableFlags.None)
                return;

            _isAssigning = true;
            try
            {
                TryAssignAdInternal(ad);
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("AdAutomation.TryAssignAd: " + ex);
            }
            finally
            {
                _isAssigning = false;
            }
        }

        private static void TryAssignAdInternal(Ad ad)
        {
            if (ad == null || ad.Data == null)
                return;
            // A boycotted ad cannot earn money and the game forcibly unassigns
            // it from the newspaper slot. Don't waste an employee starting
            // work that will immediately be undone.
            if (ad.HasBoycott)
                return;

            // Ad story files are exposed the same way news items expose theirs.
            // Use the same "unlocked + assignable" filter the evaluator uses
            // for news, then run each open slot through the assignment loop.
            var storyFiles = new List<NewsItemStoryFile>();
            ad.GetUnlockedAndAssignableStoryFiles(storyFiles);
            if (storyFiles.Count == 0)
                return;

            foreach (var storyFile in storyFiles)
                TryAssignAdStoryFile(ad, storyFile);
        }

        // Orchestrator for a single ad slot. Mirrors the news path
        // (TryAssignSingleSlot) but with ad-specific reason strings and no
        // story-level context (goal tags / in-progress sets are news-only).
        private static void TryAssignAdStoryFile(Ad ad, NewsItemStoryFile storyFile)
        {
            if (!IsSlotAssignable(storyFile))
                return;

            var skill = storyFile.AssignSkill;
            if (!SkillIsAvailableForAd(ad, skill))
                return;

            // Same filter the evaluator uses for news. See
            // ReporterLookup.PickBestAvailable for the clause-by-clause
            // explanation of why each gate is necessary.
            var employee = ReporterLookup.PickBestAvailable(skill);
            if (employee == null)
            {
                LogNoAdEmployeeAvailable(ad, skill);
                return;
            }

            // Same visibility flip the evaluator does for news. Without this
            // OnAssigned's CanAssign check fails silently and the assignment
            // ghosts (employee marked busy, no progressDoneEvent created).
            storyFile.OnVisibilityChanged(true);

            if (!PassesAdPreFlight(storyFile, employee, skill, ad))
                return;

            CommitAdAssignment(ad, storyFile, employee, skill);
        }

        // Cheap gates for skipping slots that are finished, null, or already
        // running. Kept separate from TryAssignAdStoryFile so the top-level
        // flow reads as a sequence of question-shaped helpers.
        private static bool IsSlotAssignable(NewsItemStoryFile storyFile) =>
            storyFile != null
            && !storyFile.IsCompleted
            && !GameReflection.IsSlotAlreadyRunning(storyFile);

        // Mirror the evaluator's preflight: don't try to assign work whose
        // required building isn't built yet, and don't waste a scan trying
        // to find a skill nobody on the roster has. Ads use the job-agnostic
        // employee check (ads are worked by salespeople / editors /
        // typesetters / assemblers, not reporters; the Reporter-only check
        // would silently reject every ad skill).
        private static bool SkillIsAvailableForAd(Ad ad, SkillData skill)
        {
            if (skill == null)
                return true;
            if (!AssetUnlocker.IsUnlockedSafe(skill))
            {
                AssignmentLog.DecisionOnce(
                    ad,
                    "ad_building_missing_" + skill.skillName,
                    "Ad '"
                        + AdName(ad)
                        + "' → WAIT (ad): required building for '"
                        + skill.skillName
                        + "' not built yet (ad kept, will retry when unlocked)."
                );
                return false;
            }
            if (!ReporterLookup.AnyEmployeeEverHasSkill(skill))
            {
                AssignmentLog.DecisionOnce(
                    ad,
                    "ad_no_skill_" + skill.skillName,
                    "Ad '"
                        + AdName(ad)
                        + "' → WAIT (ad): no employee on the roster has '"
                        + skill.skillName
                        + "' trained (ad kept, will retry after hiring)."
                );
                return false;
            }
            return true;
        }

        private static void LogNoAdEmployeeAvailable(Ad ad, SkillData skill)
        {
            AssignmentLog.DecisionOnce(
                ad,
                "ad_no_employee_" + (skill?.skillName ?? "any"),
                "Ad '"
                    + AdName(ad)
                    + "' → WAIT (no employee): all "
                    + (skill != null ? "'" + skill.skillName + "'" : "eligible")
                    + " staff busy right now (ad kept, will retry)."
            );
        }

        // Ad-flavoured equivalent of AssignmentEvaluator.PassesPreFlight.
        private static bool PassesAdPreFlight(
            NewsItemStoryFile storyFile,
            Employee employee,
            SkillData skill,
            Ad ad
        )
        {
            if (storyFile.CanAssignHandlers.All(handler => handler.CanAssign(employee)))
                return true;
            if (storyFile.Node?.NodeState == NewsItemNodeState.Locked)
            {
                AssignmentLog.Verbose(
                    "AD",
                    "Ad branch locked (sibling chosen) ["
                        + (skill?.skillName ?? "any")
                        + "] for "
                        + AdName(ad)
                        + "."
                );
                return false;
            }
            AssignmentLog.Warn(
                "AD",
                "  -> AD PRE-FLIGHT FAIL for "
                    + employee.name
                    + " ["
                    + (skill?.skillName ?? "any")
                    + "]"
                    + " | NodeState="
                    + storyFile.Node?.NodeState
                    + " IsCompleted="
                    + storyFile.IsCompleted
                    + " HasSkill="
                    + (skill == null || employee.SkillHandler.HasSkillAndIsAssigned(skill))
                    + " AvailableForGlobe="
                    + employee.IsAvailableForGlobeAssignment
            );
            return false;
        }

        private static void CommitAdAssignment(
            Ad ad,
            NewsItemStoryFile storyFile,
            Employee employee,
            SkillData skill
        )
        {
            AssignmentLog.ClearSuppression(ad);
            AssignmentLog.Decision(
                "Ad '"
                    + AdName(ad)
                    + "' → ASSIGNED: path="
                    + (skill?.skillName ?? "any")
                    + " employee="
                    + employee.name
                    + "."
            );
            employee.AssignableToReportable.AssignTo(storyFile);
        }

        // Best-effort display name. Ad.Title can format mutable content that
        // isn't yet resolved during early load; fall back to the AdData asset
        // name so logs stay readable even when titles are blank.
        private static string AdName(Ad ad)
        {
            if (ad == null)
                return "?";
            try
            {
                var title = ad.Title;
                if (!string.IsNullOrEmpty(title))
                    return title;
            }
            catch (Exception)
            {
                // Title can throw on partially-initialised ads (e.g. mid-load
                // before company / mutable refs are resolved). Falling back
                // to the AdData asset name is fine for log purposes and we
                // don't need the exception detail here.
            }
            return ad.Data != null ? ad.Data.name : "?";
        }
    }
}
