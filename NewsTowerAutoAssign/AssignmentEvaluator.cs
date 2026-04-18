using System;
using System.Collections.Generic;
using System.Linq;
using _Game._Common;
using Assigner;
using Employees;
using GameState;
using GlobalNews;
using Persons;
using Reportables;
using Risks;
using Skills;
using Tower_Stats;

namespace NewsTowerAutoAssign
{
    internal static partial class AssignmentEvaluator
    {
        private static bool _isAssigning;

        internal static bool ProgressDoneEventFieldAvailable =>
            GameReflection.ProgressDoneEventFieldAvailable;

        internal static void TryAutoAssignAll()
        {
            if (_isAssigning)
                return;
            if (!AutoAssignPlugin.AutoAssignEnabled.Value)
                return;
            if (LiveReportableManager.Instance == null)
                return;
            if (!SafetyGate.IsOpen)
                return;

            _isAssigning = true;
            try
            {
                var (quantityGoalTags, scoopGoalTags, binaryGoalTags, inProgressTags) =
                    LoadGoalContext();
                foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems().ToList())
                    ProcessScannedNewsItem(
                        newsItem,
                        quantityGoalTags,
                        scoopGoalTags,
                        binaryGoalTags,
                        inProgressTags
                    );
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("TryAutoAssignAll: " + ex);
            }
            finally
            {
                _isAssigning = false;
            }
        }

        private static void ProcessScannedNewsItem(
            NewsItem newsItem,
            HashSet<PlayerStatDataTag> quantityGoalTags,
            HashSet<PlayerStatDataTag> scoopGoalTags,
            HashSet<PlayerStatDataTag> binaryGoalTags,
            HashSet<PlayerStatDataTag> inProgressTags
        )
        {
            if (newsItem?.Data == null)
                return;
            if (BribeAutomation.StoryIsPlayerBribeControlled(newsItem))
                return;
            BribeAutomation.TryPayBribes(newsItem);
            SuitcaseAutomation.TryResolveSuitcases(newsItem);
            AssignNewsItemCore(
                newsItem,
                quantityGoalTags,
                scoopGoalTags,
                binaryGoalTags,
                inProgressTags
            );
            MergeInProgressTags(newsItem, inProgressTags);
        }

        private static void MergeInProgressTags(
            NewsItem newsItem,
            HashSet<PlayerStatDataTag> inProgressTags
        )
        {
            if (!AutoAssignPlugin.ChaseGoalsEnabled.Value)
                return;
            if (newsItem == null)
                return;
            if (!IsAnySlotInProgress(newsItem))
                return;
            foreach (var tag in newsItem.Data.DistinctStatTypes.OfType<PlayerStatDataTag>())
                inProgressTags.Add(tag);
        }

        internal static bool IsAnySlotInProgress(NewsItem newsItem)
        {
            foreach (var storyFile in newsItem.GetComponentsInChildren<NewsItemStoryFile>(true))
            {
                if (storyFile != null && (storyFile.IsCompleted || storyFile.Assignee != null))
                    return true;
            }
            return false;
        }

        internal static void TryAssignNewsItem(NewsItem newsItem)
        {
            if (_isAssigning)
                return;
            if (!AutoAssignPlugin.AutoAssignEnabled.Value)
                return;
            if (!SafetyGate.IsOpen)
                return;

            _isAssigning = true;
            try
            {
                var (quantityGoalTags, scoopGoalTags, binaryGoalTags, inProgressTags) =
                    LoadGoalContext();
                AssignNewsItemCore(
                    newsItem,
                    quantityGoalTags,
                    scoopGoalTags,
                    binaryGoalTags,
                    inProgressTags
                );
            }
            catch (Exception ex)
            {
                AssignmentLog.Error("TryAssignNewsItem: " + ex);
            }
            finally
            {
                _isAssigning = false;
            }
        }

        private static (
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> scoop,
            HashSet<PlayerStatDataTag> binary,
            HashSet<PlayerStatDataTag> inProgress
        ) LoadGoalContext()
        {
            var empty = new HashSet<PlayerStatDataTag>();
            if (!AutoAssignPlugin.ChaseGoalsEnabled.Value)
            {
                GoalChaseSnapshotLog.MaybeLog(false, empty, empty, empty);
                return (empty, empty, empty, empty);
            }

            var (quantity, scoop, binary) = ReporterLookup.GetCurrentGoalTagSets();
            GoalChaseSnapshotLog.MaybeLog(true, quantity, scoop, binary);
            var inProgress = ReporterLookup.GetInProgressTags();
            return (quantity, scoop, binary, inProgress);
        }

        private sealed class EvalContext
        {
            internal NewsItem NewsItem;
            internal HashSet<PlayerStatDataTag> Quantity;
            internal HashSet<PlayerStatDataTag> Scoop;
            internal HashSet<PlayerStatDataTag> Binary;
            internal HashSet<PlayerStatDataTag> InProgress;
            internal List<NewsItemStoryFile> AllStoryFiles;
            internal List<INewsItemRisk> Risks;
            internal bool AlreadyInvested;
            internal bool GoalsLoaded;
            internal bool StoryMatchesGoal;
            internal bool HasRisk;
        }

        private static void AssignNewsItemCore(
            NewsItem newsItem,
            HashSet<PlayerStatDataTag> quantityGoalTags,
            HashSet<PlayerStatDataTag> scoopGoalTags,
            HashSet<PlayerStatDataTag> binaryGoalTags,
            HashSet<PlayerStatDataTag> inProgressTags
        )
        {
            if (IsPassivelyBelowReporterThreshold(newsItem))
                return;
            if (BribeAutomation.StoryIsPlayerBribeControlled(newsItem))
                return;

            var ctx = BuildContext(
                newsItem,
                quantityGoalTags,
                scoopGoalTags,
                binaryGoalTags,
                inProgressTags
            );

            var slots = CollectOrderedAssignableSlots(ctx);
            bool isAmbiguous = HasAmbiguousTopPathPriority(ctx, slots);
            bool deferDiscardsForManualPath =
                AutoAssignPlugin.AutoAssignOnlyObviousPath.Value && isAmbiguous;

            if (!deferDiscardsForManualPath && TryDiscardForRisk(ctx))
                return;
            LogRiskKeptIfApplicable(ctx);

            if (!deferDiscardsForManualPath && TryDiscardForDeadEnd(ctx))
                return;

            if (!deferDiscardsForManualPath && TryDiscardForWeekend(ctx))
                return;
            LogWeekendKeptIfApplicable(ctx);

            if (slots.Count == 0)
                return;

            if (!deferDiscardsForManualPath && TryDiscardForAvailability(ctx, slots))
                return;

            if (AutoAssignPlugin.AutoAssignOnlyObviousPath.Value && isAmbiguous)
            {
                bool withChase = AutoAssignPlugin.ChaseGoalsEnabled.Value;
                AssignmentLog.DecisionOnce(
                    ctx.NewsItem,
                    withChase ? "path_goal_tie" : "path_tie_no_chase",
                    AssignmentLog.StoryName(ctx.NewsItem)
                        + " "
                        + AssignmentLog.StoryTagList(ctx.NewsItem)
                        + " → WAIT (path): multiple assignable paths tie"
                        + (withChase ? " on goal priority" : " with ChaseGoals off")
                        + " - assign manually."
                );
                return;
            }

            AutoAssignOwnershipRegistry.MarkModAutoAssigned(ctx.NewsItem);

            foreach (var storyFile in slots)
                TryAssignSingleSlot(
                    ctx.NewsItem,
                    storyFile,
                    slots,
                    ctx.Quantity,
                    ctx.Scoop,
                    ctx.Binary,
                    ctx.InProgress
                );
        }

        private static bool IsPassivelyBelowReporterThreshold(NewsItem newsItem)
        {
            int reporterCount = ReporterLookup.CountPlayableReporters();
            if (reporterCount >= AutoAssignPlugin.MinReportersToActivate.Value)
                return false;
            AssignmentLog.Verbose(
                "ASSIGN",
                "Skipped "
                    + AssignmentLog.StoryName(newsItem)
                    + " because only "
                    + reporterCount
                    + " reporter(s), need "
                    + AutoAssignPlugin.MinReportersToActivate.Value
                    + " for automation."
            );
            return true;
        }

        private static EvalContext BuildContext(
            NewsItem newsItem,
            HashSet<PlayerStatDataTag> quantityGoalTags,
            HashSet<PlayerStatDataTag> scoopGoalTags,
            HashSet<PlayerStatDataTag> binaryGoalTags,
            HashSet<PlayerStatDataTag> inProgressTags
        )
        {
            var allStoryFiles = newsItem.GetComponentsInChildren<NewsItemStoryFile>(true).ToList();
            var risks = newsItem.GetComponentsInChildren<INewsItemRisk>(true).ToList();
            bool goalsLoaded = quantityGoalTags.Count > 0 || binaryGoalTags.Count > 0;
            return new EvalContext
            {
                NewsItem = newsItem,
                Quantity = quantityGoalTags,
                Scoop = scoopGoalTags,
                Binary = binaryGoalTags,
                InProgress = inProgressTags,
                AllStoryFiles = allStoryFiles,
                Risks = risks,
                AlreadyInvested = allStoryFiles.Any(sf => sf.IsCompleted || sf.Assignee != null),
                GoalsLoaded = goalsLoaded,
                StoryMatchesGoal =
                    goalsLoaded
                    && AssignmentRules.StoryMatchesUncoveredGoal(
                        newsItem.Data.DistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantityGoalTags,
                        binaryGoalTags,
                        inProgressTags
                    ),
                HasRisk = risks.Count > 0,
            };
        }

        private static bool TryDiscardForRisk(EvalContext ctx)
        {
            if (
                !DiscardPredicates.ShouldDiscardForRisk(
                    AutoAssignPlugin.AvoidRisksEnabled.Value,
                    AutoAssignPlugin.ChaseGoalsEnabled.Value,
                    ctx.AlreadyInvested,
                    ctx.GoalsLoaded,
                    ctx.HasRisk,
                    ctx.StoryMatchesGoal
                )
            )
                return false;
            var riskTypes = string.Join(
                ", ",
                ctx.Risks.Select(risk => risk.GetType().Name).Distinct()
            );
            DiscardStory(
                ctx.NewsItem,
                "risk",
                "risky ("
                    + riskTypes
                    + ") and no story tag matches an uncovered goal."
                    + GoalSnapshotSuffix(ctx.Quantity, ctx.Binary, ctx.InProgress)
            );
            return true;
        }

        private static void LogRiskKeptIfApplicable(EvalContext ctx)
        {
            if (
                !AutoAssignPlugin.AvoidRisksEnabled.Value
                || !ctx.HasRisk
                || !ctx.StoryMatchesGoal
                || ctx.AlreadyInvested
            )
                return;
            AssignmentLog.DecisionOnce(
                ctx.NewsItem,
                "risk_kept",
                AssignmentLog.StoryName(ctx.NewsItem)
                    + " "
                    + AssignmentLog.StoryTagList(ctx.NewsItem)
                    + " → KEPT (risk): risky but "
                    + DescribeGoalMatch(ctx.NewsItem, ctx.Quantity, ctx.Binary, ctx.InProgress)
            );
        }

        private static bool TryDiscardForDeadEnd(EvalContext ctx)
        {
            if (!HasDeadEndNode(ctx.AllStoryFiles))
                return false;
            DiscardStory(
                ctx.NewsItem,
                "dead-end",
                "at least one node has no viable path "
                    + "(missing building or no reporter with required skill)."
            );
            return true;
        }

        private static bool TryDiscardForWeekend(EvalContext ctx)
        {
            bool isWeekend = TowerTimes.IsWeekend(TowerTime.CurrentTime);
            if (
                !DiscardPredicates.ShouldDiscardForWeekend(
                    AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value,
                    ctx.AlreadyInvested,
                    isWeekend,
                    ctx.StoryMatchesGoal
                )
            )
                return false;
            DiscardStory(
                ctx.NewsItem,
                "weekend",
                "arrived "
                    + TowerTime.CurrentTime.Day
                    + ", fresh, and no story tag matches an uncovered goal."
                    + GoalSnapshotSuffix(ctx.Quantity, ctx.Binary, ctx.InProgress)
            );
            return true;
        }

        private static void LogWeekendKeptIfApplicable(EvalContext ctx)
        {
            bool isWeekend = TowerTimes.IsWeekend(TowerTime.CurrentTime);
            if (
                !AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value
                || !isWeekend
                || !ctx.StoryMatchesGoal
                || ctx.AlreadyInvested
            )
                return;
            AssignmentLog.DecisionOnce(
                ctx.NewsItem,
                "weekend_kept",
                AssignmentLog.StoryName(ctx.NewsItem)
                    + " "
                    + AssignmentLog.StoryTagList(ctx.NewsItem)
                    + " → KEPT (weekend): arrived "
                    + TowerTime.CurrentTime.Day
                    + " but "
                    + DescribeGoalMatch(ctx.NewsItem, ctx.Quantity, ctx.Binary, ctx.InProgress)
            );
        }

        private static List<NewsItemStoryFile> CollectOrderedAssignableSlots(EvalContext ctx)
        {
            var storyFiles = new List<NewsItemStoryFile>();
            ctx.NewsItem.GetUnlockedAndAssignableStoryFiles(storyFiles);
            if (storyFiles.Count == 0)
                return storyFiles;

            AssignmentLog.Verbose(
                "SLOTS",
                AssignmentLog.StoryName(ctx.NewsItem) + ": " + storyFiles.Count + " open slot(s)"
            );

            storyFiles = storyFiles.Where(sf => PathIsAssignableNow(sf)).ToList();
            if (storyFiles.Count == 0)
                return storyFiles;

            if (AutoAssignPlugin.ChaseGoalsEnabled.Value)
            {
                storyFiles = storyFiles
                    .OrderByDescending(sf =>
                        GetPathGoalPriority(sf, ctx.Quantity, ctx.Scoop, ctx.Binary, ctx.InProgress)
                    )
                    .ToList();
                LogPathOrder(storyFiles, ctx.Quantity, ctx.Scoop, ctx.Binary, ctx.InProgress);
            }
            return storyFiles;
        }

        private static bool HasAmbiguousTopPathPriority(
            EvalContext ctx,
            List<NewsItemStoryFile> slots
        )
        {
            if (slots == null || slots.Count <= 1)
                return false;
            if (!AutoAssignPlugin.ChaseGoalsEnabled.Value)
                return SlotsContainXorExclusivePair(slots);

            PathPriority best = PathPriority.None;
            foreach (var sf in slots)
            {
                var p = GetPathGoalPriority(
                    sf,
                    ctx.Quantity,
                    ctx.Scoop,
                    ctx.Binary,
                    ctx.InProgress
                );
                if (p > best)
                    best = p;
            }

            var tiedAtBest = slots
                .Where(sf =>
                    GetPathGoalPriority(sf, ctx.Quantity, ctx.Scoop, ctx.Binary, ctx.InProgress)
                    == best
                )
                .ToList();
            return tiedAtBest.Count > 1 && SlotsContainXorExclusivePair(tiedAtBest);
        }

        private static bool TryDiscardForAvailability(
            EvalContext ctx,
            List<NewsItemStoryFile> slots
        )
        {
            float thresholdHours = AutoAssignPlugin.DiscardIfNoReporterForHours.Value;
            if (
                !DiscardPredicates.ShouldDiscardForAvailability(
                    ctx.AlreadyInvested,
                    ctx.StoryMatchesGoal,
                    thresholdHours,
                    anyReporterSoon: slots.Any(sf =>
                        ReporterLookup.AnyReporterAvailableSoon(sf.AssignSkill, thresholdHours)
                    )
                )
            )
                return false;
            DiscardStory(
                ctx.NewsItem,
                "availability",
                "no reporter free within "
                    + thresholdHours
                    + "h for any slot, fresh, and no story tag matches an uncovered goal."
                    + GoalSnapshotSuffix(ctx.Quantity, ctx.Binary, ctx.InProgress)
            );
            return true;
        }
    }
}
