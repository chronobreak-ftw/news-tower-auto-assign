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
    internal static class AdAutomation
    {
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

            if (ad.HasBoycott)
                return;

            var storyFiles = new List<NewsItemStoryFile>();
            ad.GetUnlockedAndAssignableStoryFiles(storyFiles);
            if (storyFiles.Count == 0)
                return;

            foreach (var storyFile in storyFiles)
                TryAssignAdStoryFile(ad, storyFile);
        }

        private static void TryAssignAdStoryFile(Ad ad, NewsItemStoryFile storyFile)
        {
            if (!IsSlotAssignable(storyFile))
                return;

            var skill = storyFile.AssignSkill;
            if (!SkillIsAvailableForAd(ad, skill))
                return;

            var employee = ReporterLookup.PickBestAvailable(skill);
            if (employee == null)
            {
                LogNoAdEmployeeAvailable(ad, skill);
                return;
            }

            storyFile.OnVisibilityChanged(true);

            if (!PassesAdPreFlight(storyFile, employee, skill, ad))
                return;

            CommitAdAssignment(ad, storyFile, employee, skill);
        }

        private static bool IsSlotAssignable(NewsItemStoryFile storyFile) =>
            storyFile != null
            && !storyFile.IsCompleted
            && !GameReflection.IsSlotAlreadyRunning(storyFile);

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
                // Ad.Title can throw on partially-initialised ads mid-load; Data.name is a safe fallback.
            }
            return ad.Data != null ? ad.Data.name : "?";
        }
    }
}
