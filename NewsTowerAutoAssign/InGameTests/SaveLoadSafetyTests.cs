using System.Linq;
using System.Reflection;
using GlobalNews;
using Reportables;
using Risks.UI;
using UI;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class SaveLoadSafetyTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("SaveLoadSafety");
            GateIsOpenPostLoad(ctx);
            ReflectionTargetsResolvable(ctx);
            PopupReflectionTargetsResolvable(ctx);
            NoStuckSuitcases(ctx);
            ctx.PrintSummary();
        }

        private static void GateIsOpenPostLoad(TestContext ctx)
        {
            ctx.Assert(
                SafetyGate.IsOpen,
                "SafetyGate is open once the game is ready",
                "gate still closed - Patch_AfterLoad Postfix likely failed; check earlier log for Patch_AfterLoad.Postfix error"
            );
        }

        private static void ReflectionTargetsResolvable(TestContext ctx)
        {
            ctx.Assert(
                AssignmentEvaluator.ProgressDoneEventFieldAvailable,
                "NewsItemStoryFile.progressDoneEvent resolvable",
                "reflection field not found - game update likely"
            );

            var (typeName, missing) = SuitcaseAutomation.ProbeReflectionTargets();
            ctx.Assert(
                missing.Length == 0,
                "SuitcaseAutomation reflection targets resolvable on " + typeName,
                "missing=[" + string.Join(", ", missing) + "]"
            );
        }

        private static void PopupReflectionTargetsResolvable(TestContext ctx)
        {
            var didSkipField = typeof(SuitcasePopup).GetField(
                "didSkip",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            ctx.Assert(
                didSkipField != null,
                "SuitcasePopup.didSkip field resolvable",
                "field not found - Patch_SuitcasePopupAutoSkip will silently fail to skip"
            );

            var shouldSkipField = typeof(RiskPopup).GetField(
                "shouldSkip",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            ctx.Assert(
                shouldSkipField != null,
                "RiskPopup.shouldSkip field resolvable",
                "field not found - Patch_RiskPopupAutoSkip will silently fail to skip"
            );
        }

        private static void NoStuckSuitcases(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("stuck suitcase", "LiveReportableManager not available");
                return;
            }

            int checkedCount = 0;
            int stuckCount = 0;
            foreach (var newsItem in LiveReportableManager.Instance.GetNewsItems())
            {
                if (newsItem?.Data == null)
                    continue;

                foreach (
                    var suitcase in newsItem.GetComponentsInChildren<NewsItemSuitcaseBuildable>(
                        true
                    )
                )
                {
                    if (suitcase == null || suitcase.IsCompleted)
                        continue;
                    var node = suitcase.Node;
                    if (node == null || node.NodeState != NewsItemNodeState.Unlocked)
                        continue;

                    checkedCount++;

                    var didActProp = suitcase
                        .GetType()
                        .GetProperty(
                            "DidAct",
                            BindingFlags.Instance
                                | BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.FlattenHierarchy
                        );
                    if (didActProp == null)
                    {
                        ctx.Skip(
                            "stuck suitcase: " + AssignmentLog.StoryName(newsItem),
                            "DidAct property not found via reflection"
                        );
                        continue;
                    }

                    bool didAct = (bool)didActProp.GetValue(suitcase, null);
                    bool stuck = !didAct;
                    if (stuck)
                        stuckCount++;
                    ctx.Assert(
                        !stuck,
                        "suitcase resolved: "
                            + AssignmentLog.StoryName(newsItem)
                            + " / "
                            + suitcase.GetType().Name,
                        "node state Unlocked + DidAct=false - auto-resolver didn't fire"
                    );
                }
            }

            if (checkedCount == 0)
                ctx.NotApplicable(
                    "stuck suitcase",
                    "no unlocked suitcases on the board to check right now"
                );
            else
                AutoAssignPlugin.Log.LogInfo(
                    "[INFO] SaveLoadSafety / checked "
                        + checkedCount
                        + " suitcase(s), "
                        + stuckCount
                        + " stuck"
                );
        }
    }
}
