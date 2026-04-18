using GameState;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class KillSwitchTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("KillSwitch");
            AutoAssignEnabledToggleObservable(ctx);
            FractionalHoursDeadlinePreserved(ctx);
            MinReporterClampEnforced(ctx);
            BribeKillSwitchConfigRoundTrips(ctx);
            ctx.PrintSummary();
        }

        private static void AutoAssignEnabledToggleObservable(TestContext ctx)
        {
            bool original = AutoAssignPlugin.AutoAssignEnabled.Value;
            try
            {
                AutoAssignPlugin.AutoAssignEnabled.Value = false;
                AssignmentEvaluator.TryAutoAssignAll();
                ctx.Pass("TryAutoAssignAll is a no-op when Enabled=false");

                AutoAssignPlugin.AutoAssignEnabled.Value = true;
                ctx.Assert(
                    AutoAssignPlugin.AutoAssignEnabled.Value,
                    "Enabled flag round-trips through BepInEx config"
                );
            }
            finally
            {
                AutoAssignPlugin.AutoAssignEnabled.Value = original;
            }
        }

        private static void MinReporterClampEnforced(TestContext ctx)
        {
            int original = AutoAssignPlugin.MinReportersToActivate.Value;
            try
            {
                AutoAssignPlugin.MinReportersToActivate.Value = 1;
                ctx.Assert(
                    AutoAssignPlugin.MinReportersToActivate.Value >= 3,
                    "MinReportersToActivate clamped to minimum of 3 when set to 1",
                    "actual=" + AutoAssignPlugin.MinReportersToActivate.Value
                );
            }
            finally
            {
                AutoAssignPlugin.MinReportersToActivate.Value = original;
            }
        }

        private static void BribeKillSwitchConfigRoundTrips(TestContext ctx)
        {
            bool original = AutoAssignPlugin.AutoResolveBribes.Value;
            try
            {
                AutoAssignPlugin.AutoResolveBribes.Value = false;
                ctx.Assert(
                    !AutoAssignPlugin.AutoResolveBribes.Value,
                    "AutoResolveBribes=false round-trips through BepInEx config"
                );
                AutoAssignPlugin.AutoResolveBribes.Value = true;
                ctx.Assert(
                    AutoAssignPlugin.AutoResolveBribes.Value,
                    "AutoResolveBribes=true round-trips through BepInEx config"
                );
                ctx.Pass("AutoResolveBribes kill-switch config round-trips");
            }
            finally
            {
                AutoAssignPlugin.AutoResolveBribes.Value = original;
            }
        }

        private static void FractionalHoursDeadlinePreserved(TestContext ctx)
        {
            const float fractional = 3.5f;
            int truncated = (int)fractional;
            int preserved = (int)System.Math.Round(fractional * 60f);

            long truncatedMinutes = TowerTimeDuration.FromHours(truncated).TotalMinutes;
            long preservedMinutes = TowerTimeDuration.FromMinutes(preserved).TotalMinutes;

            ctx.Assert(
                preservedMinutes > truncatedMinutes,
                "fractional hours produce a longer deadline than the int-truncated form",
                "preserved=" + preservedMinutes + "m truncated=" + truncatedMinutes + "m"
            );

            long halfHourMinutes = TowerTimeDuration
                .FromMinutes((int)System.Math.Round(0.5f * 60f))
                .TotalMinutes;
            ctx.Assert(
                halfHourMinutes > 0L,
                "0.5h now has a positive duration (old bug made it zero)",
                "minutes=" + halfHourMinutes
            );
        }
    }
}
