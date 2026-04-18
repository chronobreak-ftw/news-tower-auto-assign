using System.Collections.Generic;
using System.Linq;
using GameState;
using GlobalNews;
using Reportables;
using Risks;
using Tower_Stats;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class PipelineInvariantTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("PipelineInvariants");
            DiscardedStoriesAreGone(ctx);
            ChosenPathIsHighestPriority(ctx);
            ctx.PrintSummary();
        }

        private static void DiscardedStoriesAreGone(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("discarded stories are gone", "no LiveReportableManager");
                return;
            }

            if (!AutoAssignPlugin.AutoAssignEnabled.Value)
            {
                ctx.NotApplicable("discarded stories are gone", "AutoAssign is disabled");
                return;
            }

            int reporterCount = ReporterLookup.CountPlayableReporters();
            if (reporterCount < AutoAssignPlugin.MinReportersToActivate.Value)
            {
                ctx.NotApplicable(
                    "discarded stories are gone",
                    "below reporter threshold ("
                        + reporterCount
                        + " < "
                        + AutoAssignPlugin.MinReportersToActivate.Value
                        + ") - mod is passive"
                );
                return;
            }

            var (quantity, _, binary) = ReporterLookup.GetCurrentGoalTagSets();
            var inProgress = ReporterLookup.GetInProgressTags();
            bool goalsLoaded = quantity.Count > 0 || binary.Count > 0;
            bool isWeekend = TowerTimes.IsWeekend(TowerTime.CurrentTime);

            int riskViolations = 0;
            int weekendViolations = 0;

            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null)
                    continue;

                bool isInvested = AssignmentEvaluator.IsAnySlotInProgress(ni);

                if (AutoAssignPlugin.AutoAssignOnlyObviousPath.Value)
                {
                    var slots = new List<NewsItemStoryFile>();
                    ni.GetUnlockedAndAssignableStoryFiles(slots);
                    if (AssignmentEvaluator.HasXorAmbiguousSlots(slots))
                        continue;
                }

                bool hasRisk = ni.GetComponentsInChildren<INewsItemRisk>(true).Any();
                bool matchesGoal =
                    goalsLoaded
                    && AssignmentRules.StoryMatchesUncoveredGoal(
                        ni.Data.DistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantity,
                        binary,
                        inProgress
                    );

                if (
                    DiscardPredicates.ShouldDiscardForRisk(
                        AutoAssignPlugin.AvoidRisksEnabled.Value,
                        AutoAssignPlugin.ChaseGoalsEnabled.Value,
                        isInvested,
                        goalsLoaded,
                        hasRisk,
                        matchesGoal
                    )
                )
                {
                    riskViolations++;
                    ctx.Fail(
                        "discarded stories are gone / risk: " + ShortName(ni),
                        "non-invested, risky, no goal match - should have been discarded"
                    );
                }

                if (
                    DiscardPredicates.ShouldDiscardForWeekend(
                        AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value,
                        isInvested,
                        isWeekend,
                        matchesGoal
                    )
                )
                {
                    weekendViolations++;
                    ctx.Fail(
                        "discarded stories are gone / weekend: " + ShortName(ni),
                        "fresh, weekend, no goal match - should have been discarded"
                    );
                }
            }

            bool riskCheckActive =
                AutoAssignPlugin.AvoidRisksEnabled.Value
                && (AutoAssignPlugin.ChaseGoalsEnabled.Value ? goalsLoaded : true);
            bool weekendCheckActive =
                AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value && isWeekend;

            if (!riskCheckActive)
                ctx.NotApplicable(
                    "discarded stories are gone / risk",
                    !AutoAssignPlugin.AvoidRisksEnabled.Value
                        ? "AvoidRisks is off"
                        : "goal context not loaded yet (ChaseGoals requires it)"
                );
            else if (riskViolations == 0)
                ctx.Pass("discarded stories are gone / risk: no surviving risk-eligible stories");

            if (!weekendCheckActive)
                ctx.NotApplicable(
                    "discarded stories are gone / weekend",
                    !AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value
                        ? "DiscardFreshStoriesOnWeekend is off"
                        : "not a weekend day"
                );
            else if (weekendViolations == 0)
                ctx.Pass(
                    "discarded stories are gone / weekend: no surviving weekend-eligible stories"
                );
        }

        private static void ChosenPathIsHighestPriority(TestContext ctx)
        {
            if (!AutoAssignPlugin.ChaseGoalsEnabled.Value)
            {
                ctx.NotApplicable(
                    "chosen path is highest priority",
                    "ChaseGoals is off - path ordering is not applied"
                );
                return;
            }

            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("chosen path is highest priority", "no LiveReportableManager");
                return;
            }

            var (quantity, scoop, binary) = ReporterLookup.GetCurrentGoalTagSets();
            var inProgress = ReporterLookup.GetInProgressTags();

            int checkedStories = 0;

            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null)
                    continue;
                if (!AutoAssignOwnershipRegistry.IsModAutoAssigned(ni))
                    continue;

                var allFiles = ni.GetComponentsInChildren<NewsItemStoryFile>(true)
                    .Where(sf => sf != null)
                    .ToList();
                if (allFiles.Count <= 1)
                    continue;

                var assignedFiles = allFiles.Where(sf => sf.Assignee != null).ToList();
                if (assignedFiles.Count == 0)
                    continue;

                var openFiles = allFiles
                    .Where(sf =>
                        sf.Assignee == null
                        && !sf.IsCompleted
                        && !GameReflection.IsSlotAlreadyRunning(sf)
                        && sf.Node?.NodeState != NewsItemNodeState.Locked
                        && sf.Node?.NodeState != NewsItemNodeState.Destroyed
                    )
                    .ToList();
                if (openFiles.Count == 0)
                    continue;

                checkedStories++;

                PathPriority bestAssigned = PathPriority.None;
                foreach (var sf in assignedFiles)
                {
                    var p = AssignmentRules.GetPathGoalPriority(
                        sf.BaseYieldDistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantity,
                        scoop,
                        binary,
                        inProgress,
                        tag => sf.IsScoop(tag)
                    );
                    if (p > bestAssigned)
                        bestAssigned = p;
                }

                bool storyViolated = false;
                foreach (var openSf in openFiles)
                {
                    var openPriority = AssignmentRules.GetPathGoalPriority(
                        openSf.BaseYieldDistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantity,
                        scoop,
                        binary,
                        inProgress,
                        tag => openSf.IsScoop(tag)
                    );

                    if (openPriority <= bestAssigned)
                        continue;

                    var available = ReporterLookup.PickBestAvailable(openSf.AssignSkill);
                    if (available == null)
                        continue;

                    if (!storyViolated)
                        storyViolated = true;

                    ctx.Fail(
                        "chosen path is highest priority / " + ShortName(ni),
                        "open path '"
                            + (openSf.AssignSkill?.skillName ?? "any")
                            + "' has priority="
                            + openPriority
                            + " > best assigned="
                            + bestAssigned
                            + " and reporter '"
                            + available.name
                            + "' is free"
                    );
                }

                if (!storyViolated)
                    ctx.Pass("chosen path is highest priority / " + ShortName(ni));
            }

            if (checkedStories == 0)
                ctx.NotApplicable(
                    "chosen path is highest priority",
                    "no mod-assigned multi-path stories with both assigned and open slots on board"
                );
        }

        private static string ShortName(NewsItem ni)
        {
            var name = ni.Data.name ?? "?";
            var paren = name.LastIndexOf(" (");
            return paren > 0 ? name.Substring(0, paren) : name;
        }
    }
}
