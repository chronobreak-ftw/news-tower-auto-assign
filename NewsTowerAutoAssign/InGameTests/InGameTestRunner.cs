using System.Linq;
using _Game.Quests;
using _Game.Quests.Composed;
using BepInEx.Logging;
using GlobalNews;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class InGameTestRunner
    {
        private static bool _pureRan = false;
        private static bool _liveRan = false;

        internal static void RunOnceWhenReady()
        {
            if (!_pureRan && LiveReportableManager.Instance != null)
            {
                _pureRan = true;
                TestRunAggregator.Reset();
                AutoAssignPlugin.Log.LogInfo("===== AutoAssign Pure Tests =====");
                DiscardPredicateTests.Run();
                KillSwitchTests.Run();
                AutoAssignPlugin.Log.LogInfo("===== Pure Tests complete =====");
            }

            if (_liveRan || !IsGameStateReady())
                return;

            _liveRan = true;
            AutoAssignPlugin.Log.LogInfo("========== AutoAssign In-Game Tests ==========");
            AssignmentRuleTests.Run();
            GoalExtractionTests.Run();
            LiveStateInvariantTests.Run();
            FactionTagDecisionTests.Run();
            SaveLoadSafetyTests.Run();
            GlobePinOwnershipTests.Run();
            PipelineInvariantTests.Run();
            PrintRunSummary();
            AutoAssignPlugin.Log.LogInfo("========== Tests complete ==========");
        }

        private static bool IsGameStateReady()
        {
            if (LiveReportableManager.Instance == null)
                return false;
            if (QuestManager.Instance == null)
                return false;
            return QuestManager
                .Instance.AllRunningQuests.OfType<ComposedQuest>()
                .Any(cq => cq != null && !cq.IsDummy);
        }

        private static void PrintRunSummary()
        {
            int passed = TestRunAggregator.TotalPassed;
            int failed = TestRunAggregator.TotalFailed;
            int skipped = TestRunAggregator.TotalSkipped;

            string badge;
            LogLevel level;
            if (failed > 0)
            {
                badge = "[FAIL]";
                level = LogLevel.Error;
            }
            else if (skipped > 0)
            {
                badge = "[WARN]";
                level = LogLevel.Warning;
            }
            else
            {
                badge = "[OK]";
                level = LogLevel.Message;
            }

            AutoAssignPlugin.Log.Log(
                level,
                badge
                    + " [RUN] In-Game Tests: "
                    + passed
                    + " passed, "
                    + failed
                    + " failed, "
                    + skipped
                    + " skipped"
            );
        }
    }
}
