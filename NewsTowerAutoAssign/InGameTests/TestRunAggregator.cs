namespace NewsTowerAutoAssign.InGameTests
{
    internal static class TestRunAggregator
    {
        internal static int TotalPassed { get; private set; }
        internal static int TotalFailed { get; private set; }
        internal static int TotalSkipped { get; private set; }

        internal static void Accumulate(int passed, int failed, int skipped)
        {
            TotalPassed += passed;
            TotalFailed += failed;
            TotalSkipped += skipped;
        }

        internal static void Reset()
        {
            TotalPassed = 0;
            TotalFailed = 0;
            TotalSkipped = 0;
        }
    }
}
