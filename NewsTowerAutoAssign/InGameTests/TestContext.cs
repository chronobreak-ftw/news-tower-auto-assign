using BepInEx.Logging;

namespace NewsTowerAutoAssign.InGameTests
{
    internal class TestContext
    {
        private readonly string _suite;
        private int _passed,
            _failed,
            _skipped;

        private static ManualLogSource Log => AutoAssignPlugin.Log;

        internal TestContext(string suite) => _suite = suite;

        internal int Passed => _passed;
        internal int Failed => _failed;
        internal int Skipped => _skipped;

        internal void Assert(bool condition, string name, string failReason = "")
        {
            if (condition)
                Pass(name);
            else
                Fail(name, failReason);
        }

        internal void Pass(string name)
        {
            _passed++;
            Log.LogInfo("[PASS] " + _suite + " / " + name);
        }

        internal void Fail(string name, string reason = "")
        {
            _failed++;
            Log.LogError(
                "[FAIL] "
                    + _suite
                    + " / "
                    + name
                    + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)
            );
        }

        internal void Skip(string name, string reason = "")
        {
            _skipped++;
            Log.LogInfo(
                "[SKIP] "
                    + _suite
                    + " / "
                    + name
                    + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)
            );
        }

        internal void NotApplicable(string name, string reason = "")
        {
            Log.LogInfo(
                "[N/A]  "
                    + _suite
                    + " / "
                    + name
                    + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)
            );
        }

        internal bool HasFailures => _failed > 0;

        internal void PrintSummary()
        {
            TestRunAggregator.Accumulate(_passed, _failed, _skipped);

            string badge;
            LogLevel level;
            if (_failed > 0)
            {
                badge = "[FAIL]";
                level = LogLevel.Error;
            }
            else if (_skipped > 0)
            {
                badge = "[WARN]";
                level = LogLevel.Warning;
            }
            else
            {
                badge = "[OK]";
                level = LogLevel.Message;
            }

            Log.Log(
                level,
                badge
                    + " [TESTS] "
                    + _suite
                    + ": "
                    + _passed
                    + " passed, "
                    + _failed
                    + " failed, "
                    + _skipped
                    + " skipped"
            );
        }
    }
}
