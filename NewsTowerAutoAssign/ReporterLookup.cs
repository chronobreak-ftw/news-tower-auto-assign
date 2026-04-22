using System;
using System.Collections.Generic;
using System.Linq;
using _Game.Quests;
using _Game.Quests.Composed;
using _Game.Quests.Composed.Components;
using _Game.Quests.Composed.Data.Components;
using AreaMaps;
using Employees;
using GameState;
using GlobalNews;
using Persons;
using Reportables;
using Skills;
using Tower_Stats;

namespace NewsTowerAutoAssign
{
    internal static class ReporterLookup
    {
        private const string ReporterJobName = "Reporter";

        private static bool IsPlayableReporter(Employee employee) =>
            IsPlayableEmployee(employee) && employee.JobHandler.JobData.name == ReporterJobName;

        private static bool IsPlayableEmployee(Employee employee)
        {
            if (employee == null)
                return false;
            if (!employee.IsGlobetrotter)
                return false;
            if (!employee.IsInTower)
                return false;
            var job = employee.JobHandler?.JobData;
            if (job == null)
                return false;
            if (job.hideFromDrawer)
                return false;
            return true;
        }

        internal static int CountPlayableReporters()
        {
            var matched = Employee.Employees.Where(IsPlayableReporter).ToList();

            return matched.Count;
        }

        internal static bool AnyReporterEverHasSkill(SkillData skill) =>
            AnyMatchingEmployee(skill, IsPlayableReporter);

        internal static bool AnyEmployeeEverHasSkill(SkillData skill) =>
            AnyMatchingEmployee(skill, IsPlayableEmployee);

        private static bool AnyMatchingEmployee(SkillData skill, Func<Employee, bool> isPlayable)
        {
            if (skill == null)
                return true;
            foreach (var employee in Employee.Employees)
            {
                if (!isPlayable(employee))
                    continue;
                if (employee.SkillHandler == null)
                    continue;
                if (employee.SkillHandler.HasSkillAndIsAssigned(skill))
                    return true;
            }
            return false;
        }

        internal static string GetAvailabilitySummary(float thresholdHours)
        {
            int free = 0,
                soon = 0,
                busy = 0;
            int minutes = (int)Math.Round(thresholdHours * 60f);
            var deadline = TowerTime.CurrentTime + TowerTimeDuration.FromMinutes(minutes);
            foreach (var emp in Employee.Employees)
            {
                if (!IsPlayableReporter(emp) || emp.TimeoutHandler == null)
                    continue;
                if (!emp.TimeoutHandler.IsTimedOut)
                    free++;
                else if (emp.TimeoutHandler.GetReleaseTime() <= deadline)
                    soon++;
                else
                    busy++;
            }
            return "free=" + free + " soon=" + soon + " busy=" + busy;
        }

        internal static bool AnyReporterAvailableSoon(SkillData skill, float thresholdHours)
        {
            if (thresholdHours <= 0f)
                return true;

            int minutes = (int)Math.Round(thresholdHours * 60f);
            var deadline = TowerTime.CurrentTime + TowerTimeDuration.FromMinutes(minutes);

            foreach (var employee in Employee.Employees)
            {
                if (!IsPlayableReporter(employee))
                    continue;

                if (employee.SkillHandler == null || employee.TimeoutHandler == null)
                    continue;
                if (skill != null && !employee.SkillHandler.HasSkillAndIsAssigned(skill))
                    continue;
                if (!employee.TimeoutHandler.IsTimedOut)
                    return true;
                if (employee.TimeoutHandler.GetReleaseTime() <= deadline)
                    return true;
            }
            return false;
        }

        internal static Employee PickBestAvailable(SkillData skill)
        {
            return Employee
                .Employees.Where(employee =>
                    employee != null
                    && employee.IsAvailableForGlobeAssignment
                    && employee.AssignableToReportable != null
                    && employee.AssignableToReportable.Assignment == null
                    && employee.SkillHandler != null
                    && (skill == null || employee.SkillHandler.HasSkillAndIsAssigned(skill))
                    && employee.JobHandler?.JobData?.hideFromDrawer == false
                )
                .OrderByDescending(employee => GetSkillLevel(employee, skill))
                .FirstOrDefault();
        }

        internal static int GetSkillLevel(Employee employee, SkillData skill)
        {
            if (skill == null)
                return 0;

            if (employee?.SkillHandler == null)
                return 0;
            return employee.SkillHandler.TryGetSkill(skill, out Skill trainedSkill)
                ? (int)trainedSkill
                : 0;
        }

        internal static (
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> scoopQuantity,
            HashSet<PlayerStatDataTag> binary
        ) GetCurrentGoalTagSets()
        {
            var quantity = new HashSet<PlayerStatDataTag>();
            var scoopQuantity = new HashSet<PlayerStatDataTag>();
            var binary = new HashSet<PlayerStatDataTag>();

            foreach (var quest in EnumerateAllActiveQuests())
            {
                if (quest == null)
                    continue;

                ExtractQuestTags(quest, quantity, scoopQuantity, binary);
            }

            binary.ExceptWith(quantity);

            AssignmentLog.Verbose(
                "GOALS",
                "Goal tags - quantity: ["
                    + string.Join(", ", quantity)
                    + "]  scoopRequired: ["
                    + string.Join(", ", scoopQuantity)
                    + "]  binary: ["
                    + string.Join(", ", binary)
                    + "]"
            );
            return (quantity, scoopQuantity, binary);
        }

        private static IEnumerable<Quest> EnumerateAllActiveQuests()
        {
            if (QuestManager.Instance != null)
            {
                foreach (var quest in QuestManager.Instance.AllRunningQuests)
                {
                    if (quest != null)
                        yield return quest;
                }
            }

            var areaMapRoot = AreaMapRoot.Instance;
            if (areaMapRoot != null)
            {
                foreach (var hub in areaMapRoot.GetComponentsInChildren<AreaMapQuestHub>(true))
                {
                    if (hub?.Quest != null)
                        yield return hub.Quest;
                }
            }
        }

        private static void ExtractQuestTags(
            Quest quest,
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> scoopQuantity,
            HashSet<PlayerStatDataTag> binary
        )
        {
            var added = new HashSet<PlayerStatDataTag>();

            if (quest is HotTopicsQuest hotTopics && hotTopics.TargetSet != null)
            {
                foreach (var tag in hotTopics.TargetSet.DistinctTagTypes)
                {
                    if (tag == null)
                        continue;
                    binary.Add(tag);
                    if (hotTopics.TargetSet.Scoop)
                        scoopQuantity.Add(tag);
                    added.Add(tag);
                }
            }

            if (quest is ComposedQuest composed)
            {
                ExtractComposedQuestTags(composed, quantity, binary, added);

                if (added.Count == 0 && !composed.IsDummy)
                    DumpComposedQuestStructure(composed, "no tags extracted");
            }

            AssignmentLog.Verbose(
                "GOALS",
                "Scanned "
                    + quest.GetType().Name
                    + " ("
                    + QuestIdentityLabel(quest)
                    + ") → tags: ["
                    + string.Join(", ", added.Select(tag => tag?.name ?? "null"))
                    + "]"
            );
        }

        internal static void ExtractComposedQuestTags(
            ComposedQuest composed,
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> binary,
            HashSet<PlayerStatDataTag> addedOut
        )
        {
            int runtimeHits = 0;

            foreach (var req in composed.GetComponentsInChildren<QuestCollectingReward>(true))
            {
                runtimeHits++;
                AddTag(req.RequirementData?.tagToCollect, quantity, addedOut);
            }

            foreach (var req in composed.GetComponentsInChildren<QuestCollectingCombo>(true))
            {
                runtimeHits++;
                AddTag(req.RequirementData?.tagToCollect, quantity, addedOut);
            }

            foreach (var req in composed.GetComponentsInChildren<QuestTargetSetRequirement>(true))
            {
                runtimeHits++;
                var liveSet = req.TargetSet;
                if (liveSet != null && !liveSet.IsEmpty())
                {
                    foreach (var tag in liveSet.DistinctTagTypes)
                        AddBinaryTag(tag, quantity, binary, addedOut);
                    continue;
                }
                var protos = req.RequirementData?.targets?.targets;
                if (protos == null)
                    continue;
                foreach (var pt in protos)
                    AddBinaryTag(pt?.tag, quantity, binary, addedOut);
            }

            foreach (var req in composed.GetComponentsInChildren<QuestTargetComboRequirement>(true))
            {
                runtimeHits++;
                AddBinaryTag(req.Tag ?? req.RequirementData?.tag, quantity, binary, addedOut);
            }

            foreach (var req in composed.GetComponentsInChildren<QuestTopTagRequirement>(true))
            {
                runtimeHits++;
                AddBinaryTag(req.RequirementData?.tag, quantity, binary, addedOut);
            }

            foreach (
                var req in composed.GetComponentsInChildren<QuestAboveTheFoldTagRequirement>(true)
            )
            {
                runtimeHits++;
                AddBinaryTag(req.RequirementData?.tag, quantity, binary, addedOut);
            }

            if (runtimeHits == 0 && composed.Data != null)
            {
                foreach (
                    var rewardData in composed.Data.GetAllChildren<QuestCollectingRewardData>(true)
                )
                    AddTag(rewardData.tagToCollect, quantity, addedOut);
                foreach (
                    var comboData in composed.Data.GetAllChildren<QuestCollectingComboData>(true)
                )
                    AddTag(comboData.tagToCollect, quantity, addedOut);
                foreach (
                    var targetSetData in composed.Data.GetAllChildren<QuestTargetSetRequirementData>(
                        true
                    )
                )
                {
                    var protos = targetSetData.targets?.targets;
                    if (protos == null)
                        continue;
                    foreach (var proto in protos)
                        AddBinaryTag(proto?.tag, quantity, binary, addedOut);
                }
                foreach (
                    var comboReqData in composed.Data.GetAllChildren<QuestTargetComboRequirementData>(
                        true
                    )
                )
                    AddBinaryTag(comboReqData.tag, quantity, binary, addedOut);
                foreach (
                    var topTagData in composed.Data.GetAllChildren<QuestTopTagRequirementData>(true)
                )
                    AddBinaryTag(topTagData.tag, quantity, binary, addedOut);
                foreach (
                    var aboveTheFoldData in composed.Data.GetAllChildren<QuestAboveTheFoldTagRequirementData>(
                        true
                    )
                )
                    AddBinaryTag(aboveTheFoldData.tag, quantity, binary, addedOut);
            }
        }

        private static void AddTag(
            PlayerStatDataTag tag,
            HashSet<PlayerStatDataTag> bucket,
            HashSet<PlayerStatDataTag> addedOut
        )
        {
            if (tag != null && bucket.Add(tag))
                addedOut.Add(tag);
        }

        private static void AddBinaryTag(
            PlayerStatDataTag tag,
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> binary,
            HashSet<PlayerStatDataTag> addedOut
        )
        {
            if (tag == null || quantity.Contains(tag))
                return;
            if (binary.Add(tag))
                addedOut.Add(tag);
        }

        internal static void DumpComposedQuestStructure(ComposedQuest composed, string reason)
        {
            if (composed == null)
                return;

            AssignmentLog.Verbose(
                "GOALS",
                "[DUMP] "
                    + reason
                    + " for "
                    + QuestIdentityLabel(composed)
                    + " (isDummy="
                    + composed.IsDummy
                    + ")"
            );

            var runtimeTypes = composed
                .GetComponentsInChildren<object>(true)
                .Select(child => child.GetType().Name)
                .Where(typeName => typeName != "ComposedQuest")
                .ToList();
            AssignmentLog.Verbose(
                "GOALS",
                "[DUMP]   runtime tree ("
                    + runtimeTypes.Count
                    + "): ["
                    + string.Join(", ", runtimeTypes)
                    + "]"
            );

            if (composed.Data != null)
            {
                var dataTypes = composed
                    .Data.GetAllChildren<UnityEngine.Object>(false)
                    .Select(child => child.GetType().Name)
                    .ToList();
                AssignmentLog.Verbose(
                    "GOALS",
                    "[DUMP]   data tree ("
                        + dataTypes.Count
                        + "): ["
                        + string.Join(", ", dataTypes)
                        + "]"
                );
            }
        }

        private static string QuestIdentityLabel(Quest quest)
        {
            if (quest is ComposedQuest cq && cq.Data != null)
            {
                var idName =
                    cq.Data.identity != null ? cq.Data.identity.UnlocalizedIdentityName : "?";
                return idName + ": " + (cq.Data.UnlocalizedTitle ?? "?");
            }
            if (quest is HotTopicsQuest)
                return "district";
            return "quest";
        }

        internal static HashSet<PlayerStatDataTag> GetInProgressTags()
        {
            var tags = new HashSet<PlayerStatDataTag>();
            if (LiveReportableManager.Instance == null)
                return tags;

            foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems())
            {
                if (newsItem?.Data == null)
                    continue;

                bool hasProgress = false;
                foreach (var sf in newsItem.GetComponentsInChildren<NewsItemStoryFile>(true))
                {
                    if (sf.IsCompleted || sf.Assignee != null)
                    {
                        hasProgress = true;
                        break;
                    }
                }

                if (!hasProgress)
                    continue;

                foreach (var tag in newsItem.Data.DistinctStatTypes.OfType<PlayerStatDataTag>())
                    tags.Add(tag);
            }
            return tags;
        }
    }
}
