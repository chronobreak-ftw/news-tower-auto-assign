namespace NewsTowerAutoAssign.InGameTests
{
    internal static class DiscardPredicateTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("DiscardPredicates");
            RiskTests(ctx);
            WeekendTests(ctx);
            AvailabilityTests(ctx);
            ctx.PrintSummary();
        }

        private static void RiskTests(TestContext ctx)
        {
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(false, true, false, true, true, false),
                "feature-off → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(true, true, true, true, true, false),
                "invested → keep regardless"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(true, true, false, false, true, false),
                "chase on + goals not loaded → keep (can't judge)"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForRisk(true, false, false, false, true, false),
                "chase off + fresh risky → discard (no goal exception)"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(true, false, true, true, true, false),
                "chase off but invested → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(true, true, false, true, false, false),
                "no risk component → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForRisk(true, true, false, true, true, true),
                "matches uncovered goal → keep"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForRisk(true, true, false, true, true, false),
                "chase on + risky + no goal + not invested → discard"
            );
        }

        private static void WeekendTests(TestContext ctx)
        {
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForWeekend(false, false, true, false),
                "feature-off → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForWeekend(true, true, true, false),
                "invested on weekend → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForWeekend(true, false, false, false),
                "weekday → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForWeekend(true, false, true, true),
                "matches uncovered goal → keep (faction tag beats weekend timer)"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForWeekend(true, false, true, false),
                "fresh story on weekend → discard"
            );
        }

        private static void AvailabilityTests(TestContext ctx)
        {
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForAvailability(true, false, 4f, false),
                "invested → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForAvailability(false, true, 4f, false),
                "matches goal → keep (wait however long)"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForAvailability(false, false, 0f, false),
                "threshold = 0 (feature off) → keep"
            );
            ctx.Assert(
                !DiscardPredicates.ShouldDiscardForAvailability(false, false, 4f, true),
                "reporter available soon → keep"
            );
            ctx.Assert(
                DiscardPredicates.ShouldDiscardForAvailability(false, false, 4f, false),
                "fresh + no goal + no reporter soon → discard"
            );
        }
    }
}
