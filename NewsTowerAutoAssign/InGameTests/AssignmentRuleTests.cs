using System.Collections.Generic;
using System.Linq;
using Employees;
using GlobalNews;
using Reportables;
using Tower_Stats;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class AssignmentRuleTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("AssignmentRules");
            PriorityOrderingTests(ctx);
            StoryMatchTests(ctx);
            PriorityTests(ctx);
            ReporterSelectionTests(ctx);
            ctx.PrintSummary();
        }

        private static void PriorityOrderingTests(TestContext ctx)
        {
            ctx.Assert(
                PathPriority.UncoveredScoop > PathPriority.UncoveredBinary,
                "UncoveredScoop > UncoveredBinary"
            );
            ctx.Assert(
                PathPriority.UncoveredBinary > PathPriority.Quantity,
                "UncoveredBinary > Quantity"
            );
            ctx.Assert(
                PathPriority.Quantity > PathPriority.CoveredBinary,
                "Quantity > CoveredBinary"
            );
            ctx.Assert(PathPriority.CoveredBinary > PathPriority.None, "CoveredBinary > None");

            var qty = new HashSet<string> { "Economy" };
            var scoop = new HashSet<string> { "Crime" };
            var bin = new HashSet<string> { "Society", "Crime" };
            var empty = new HashSet<string>();
            var covered = new HashSet<string> { "Society" };

            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Sports" },
                    qty,
                    scoop,
                    bin,
                    empty,
                    _ => false
                ) == PathPriority.None,
                "unrelated tag → None"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Economy" },
                    qty,
                    scoop,
                    bin,
                    empty,
                    _ => false
                ) == PathPriority.Quantity,
                "quantity tag → Quantity"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Society" },
                    qty,
                    scoop,
                    bin,
                    covered,
                    _ => false
                ) == PathPriority.CoveredBinary,
                "binary tag already in-progress → CoveredBinary"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Crime" },
                    qty,
                    scoop,
                    bin,
                    empty,
                    _ => false
                ) == PathPriority.UncoveredBinary,
                "uncovered binary tag on non-scoop path → UncoveredBinary"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Crime" },
                    qty,
                    scoop,
                    bin,
                    empty,
                    t => t == "Crime"
                ) == PathPriority.UncoveredScoop,
                "uncovered scoop tag on scoop-qualified path → UncoveredScoop"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Sports", "Economy" },
                    qty,
                    scoop,
                    bin,
                    empty,
                    _ => false
                ) == PathPriority.Quantity,
                "multi-tag path: best priority wins (Sports+Economy → Quantity)"
            );
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Society", "Economy" },
                    qty,
                    scoop,
                    bin,
                    covered,
                    _ => false
                ) == PathPriority.Quantity,
                "Quantity beats CoveredBinary when both tags present on same path"
            );
            var crimeCovered = new HashSet<string> { "Crime" };
            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    new[] { "Crime" },
                    qty,
                    scoop,
                    bin,
                    crimeCovered,
                    t => t == "Crime"
                ) == PathPriority.CoveredBinary,
                "scoop tag already in-progress → CoveredBinary (not UncoveredScoop)"
            );
        }

        private static void StoryMatchTests(TestContext ctx)
        {
            var empty = new HashSet<PlayerStatDataTag>();

            ctx.Assert(
                !AssignmentRules.StoryMatchesUncoveredGoal(empty, empty, empty, empty),
                "all-empty → false"
            );

            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip(
                    "live-tag tests",
                    "LiveReportableManager not available - load a game first"
                );
                return;
            }

            var story = LiveReportableManager
                .Instance.GetNewsItems()
                .FirstOrDefault(n => n?.Data != null);
            if (story == null)
            {
                ctx.Skip("live-tag tests", "no stories on board");
                return;
            }

            var tags = story.Data.DistinctStatTypes.OfType<PlayerStatDataTag>().ToList();
            if (tags.Count == 0)
            {
                ctx.Skip("live-tag tests", "first story has no PlayerStatDataTags");
                return;
            }

            var tag0 = tags[0];
            var setWithTag = new HashSet<PlayerStatDataTag> { tag0 };

            ctx.Assert(
                AssignmentRules.StoryMatchesUncoveredGoal(tags, setWithTag, empty, empty),
                "quantity match → true"
            );
            ctx.Assert(
                AssignmentRules.StoryMatchesUncoveredGoal(tags, setWithTag, empty, setWithTag),
                "quantity match even when already in progress → true (quantity goals stack)"
            );

            ctx.Assert(
                AssignmentRules.StoryMatchesUncoveredGoal(tags, empty, setWithTag, empty),
                "binary uncovered → true"
            );

            ctx.Assert(
                !AssignmentRules.StoryMatchesUncoveredGoal(tags, empty, setWithTag, setWithTag),
                "binary covered by in-progress story → false"
            );
        }

        private static void PriorityTests(TestContext ctx)
        {
            var empty = new HashSet<PlayerStatDataTag>();

            ctx.Assert(
                AssignmentRules.GetPathGoalPriority(
                    Enumerable.Empty<PlayerStatDataTag>(),
                    empty,
                    empty,
                    empty,
                    empty,
                    _ => false
                ) == PathPriority.None,
                "no tags → PathPriority.None"
            );

            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("live priority tests", "load a game first");
                return;
            }

            var (quantity, scoop, binary) = ReporterLookup.GetCurrentGoalTagSets();
            var inProgress = ReporterLookup.GetInProgressTags();

            int tested = 0;
            foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems())
            {
                if (newsItem?.Data == null)
                    continue;
                foreach (var sf in newsItem.GetComponentsInChildren<NewsItemStoryFile>(true))
                {
                    var pri = AssignmentRules.GetPathGoalPriority(
                        sf.BaseYieldDistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantity,
                        scoop,
                        binary,
                        inProgress,
                        tag => sf.IsScoop(tag)
                    );
                    ctx.Assert(
                        pri >= PathPriority.None && pri <= PathPriority.UncoveredScoop,
                        "priority in valid PathPriority range for path "
                            + (sf.AssignSkill?.skillName ?? "any"),
                        "got " + pri
                    );
                    tested++;
                }
            }

            if (tested == 0)
                ctx.Skip("live priority range", "no story files found on board");
        }

        private static void ReporterSelectionTests(TestContext ctx)
        {
            ctx.Assert(
                ReporterLookup.GetSkillLevel(null, null) == 0,
                "GetSkillLevel(null, null) == 0"
            );

            var result = ReporterLookup.PickBestAvailable(null);

            if (result != null)
            {
                ctx.Assert(
                    result.IsAvailableForGlobeAssignment,
                    "PickBestAvailable result: IsAvailableForGlobeAssignment"
                );
                ctx.Assert(
                    result.AssignableToReportable != null,
                    "PickBestAvailable result: AssignableToReportable != null"
                );
                ctx.Assert(
                    result.AssignableToReportable?.Assignment == null,
                    "PickBestAvailable result: not currently assigned"
                );
                ctx.Assert(
                    result.SkillHandler != null,
                    "PickBestAvailable result: SkillHandler != null"
                );
                ctx.Assert(
                    result.JobHandler?.JobData?.hideFromDrawer == false,
                    "PickBestAvailable result: not hidden from drawer"
                );

                ctx.Assert(
                    ReporterLookup.GetSkillLevel(result, null) == 0,
                    "GetSkillLevel(employee, null) == 0"
                );
            }
            else
            {
                bool anyFree = Employee.Employees.Any(e =>
                    e != null
                    && e.IsAvailableForGlobeAssignment
                    && e.AssignableToReportable != null
                    && e.AssignableToReportable.Assignment == null
                    && e.SkillHandler != null
                    && e.JobHandler?.JobData?.hideFromDrawer == false
                );
                ctx.Assert(
                    !anyFree,
                    "PickBestAvailable(null) returns null only when all employees are busy",
                    "at least one free employee exists but PickBestAvailable returned null"
                );
            }

            int checkedCount = 0;
            foreach (var employee in Employee.Employees.Take(5))
            {
                if (employee == null)
                    continue;
                ctx.Assert(
                    ReporterLookup.GetSkillLevel(employee, null) == 0,
                    "GetSkillLevel(employee, nullSkill) == 0 for " + (employee.name ?? "?")
                );
                checkedCount++;
            }

            if (checkedCount == 0)
                ctx.NotApplicable(
                    "GetSkillLevel null-skill checks",
                    "no employees found on roster"
                );
        }
    }
}
