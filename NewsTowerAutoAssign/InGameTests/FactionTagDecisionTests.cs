using System.Collections.Generic;
using System.Linq;
using GameState;
using GlobalNews;
using Reportables;
using Risks;
using Tower_Stats;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class FactionTagDecisionTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("FactionTagDecision");
            PureScenarios(ctx);
            LiveBoardScenarios(ctx);
            ctx.PrintSummary();
        }

        private static void PureScenarios(TestContext ctx)
        {
            var storyTags = new[] { "RedHerring" };
            var quantity = new HashSet<string>();
            var binary = new HashSet<string> { "RedHerring" };
            var inProgressUncovered = new HashSet<string>();
            var inProgressCovered = new HashSet<string> { "RedHerring" };

            bool matchesUncovered = AssignmentRules.StoryMatchesUncoveredGoal(
                storyTags,
                quantity,
                binary,
                inProgressUncovered
            );
            ctx.Assert(matchesUncovered, "binary-only tag registers as uncovered goal match");

            bool matchesCovered = AssignmentRules.StoryMatchesUncoveredGoal(
                storyTags,
                quantity,
                binary,
                inProgressCovered
            );
            ctx.Assert(
                !matchesCovered,
                "binary tag already in progress → no longer a match (prevents doubling up)"
            );

            var priority = AssignmentRules.GetPathGoalPriority(
                storyTags,
                quantity,
                new HashSet<string>(),
                binary,
                inProgressUncovered,
                _ => false
            );
            ctx.Assert(
                priority == PathPriority.UncoveredBinary,
                "binary-only path priority = UncoveredBinary",
                "got " + priority
            );

            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(
                    avoidRisksEnabled: true,
                    chaseGoalsEnabled: true,
                    isInvested: false,
                    goalsLoaded: true,
                    hasRisk: true,
                    matchesUncoveredGoal: matchesUncovered
                ),
                "risky + fresh + binary-only goal match → kept"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForWeekend(
                    featureEnabled: true,
                    isInvested: false,
                    isWeekend: true,
                    matchesUncoveredGoal: matchesUncovered
                ),
                "weekend + fresh + binary-only goal match → kept"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForAvailability(
                    isInvested: false,
                    matchesGoal: matchesUncovered,
                    thresholdHours: 4f,
                    anyReporterSoon: false
                ),
                "no reporter soon + fresh + binary-only goal match → kept"
            );

            ctx.Assert(
                DiscardPredicates.ShouldDiscardForRisk(
                    true,
                    true,
                    false,
                    true,
                    true,
                    matchesCovered
                ),
                "risky + fresh + binary already covered → discard"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForWeekend(true, false, true, matchesCovered),
                "weekend + fresh + binary already covered → discard"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForAvailability(false, matchesCovered, 4f, false),
                "no reporter soon + fresh + binary already covered → discard"
            );
        }

        private static void LiveBoardScenarios(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("live board", "LiveReportableManager not available - runner bug");
                return;
            }

            var (_, _, binary) = ReporterLookup.GetCurrentGoalTagSets();
            if (binary.Count == 0)
            {
                ctx.NotApplicable(
                    "live board",
                    "no binary / composed-quest goal tags active in this save right now"
                );
                return;
            }

            if (!AutoAssignPlugin.ChaseGoalsEnabled.Value)
            {
                ctx.NotApplicable(
                    "live board",
                    "ChaseGoals is off - goal-match preservation checks do not apply"
                );
                return;
            }

            var inProgress = ReporterLookup.GetInProgressTags();
            bool isWeekend = TowerTimes.IsWeekend(TowerTime.CurrentTime);

            int checkedCount = 0;
            foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems())
            {
                if (newsItem?.Data == null)
                    continue;

                bool isInvested = newsItem
                    .GetComponentsInChildren<NewsItemStoryFile>(true)
                    .Any(sf => sf.IsCompleted || sf.Assignee != null);
                if (isInvested)
                    continue;

                var storyBinaryHits = newsItem
                    .Data.DistinctStatTypes.OfType<PlayerStatDataTag>()
                    .Where(t => binary.Contains(t) && !inProgress.Contains(t))
                    .Select(t => t.name)
                    .ToList();
                if (storyBinaryHits.Count == 0)
                    continue;

                checkedCount++;
                string label = ShortName(newsItem) + " [" + string.Join(",", storyBinaryHits) + "]";

                const bool matchesGoal = true;

                ctx.Assert(
                    !DiscardPredicates.ShouldDiscardForRisk(
                        AutoAssignPlugin.AvoidRisksEnabled.Value,
                        AutoAssignPlugin.ChaseGoalsEnabled.Value,
                        isInvested: false,
                        goalsLoaded: true,
                        hasRisk: newsItem.GetComponentsInChildren<INewsItemRisk>(true).Any(),
                        matchesUncoveredGoal: matchesGoal
                    ),
                    "binary-match fresh story not risk-discarded: " + label
                );

                ctx.Assert(
                    !DiscardPredicates.ShouldDiscardForWeekend(
                        AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value,
                        isInvested: false,
                        isWeekend: isWeekend,
                        matchesUncoveredGoal: matchesGoal
                    ),
                    "binary-match fresh story not weekend-discarded: " + label
                );

                ctx.Assert(
                    !DiscardPredicates.ShouldDiscardForAvailability(
                        isInvested: false,
                        matchesGoal: matchesGoal,
                        thresholdHours: AutoAssignPlugin.DiscardIfNoReporterForHours.Value,
                        anyReporterSoon: false
                    ),
                    "binary-match fresh story not availability-discarded: " + label
                );
            }

            if (checkedCount == 0)
                ctx.NotApplicable(
                    "live board",
                    "no fresh story on board currently carries an uncovered binary goal tag ("
                        + string.Join(",", binary.Select(t => t.name))
                        + ") - transient state, nothing to assert right now"
                );
        }

        private static string ShortName(NewsItem newsItem)
        {
            var name = newsItem.Data.name ?? "?";
            var paren = name.LastIndexOf(" (");
            return paren > 0 ? name.Substring(0, paren) : name;
        }
    }
}
