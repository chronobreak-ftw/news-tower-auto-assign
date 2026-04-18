using System.Collections.Generic;
using System.Linq;
using Tower_Stats;

namespace NewsTowerAutoAssign
{
    internal static class GoalChaseSnapshotLog
    {
        private static int _lastPeriodLogged = int.MinValue;
        private static string _lastFingerprint = "";

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void MaybeLog(
            bool chaseGoalsEnabled,
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> scoop,
            HashSet<PlayerStatDataTag> binary
        )
        {
            if (TowerTime.Instance == null)
                return;

            int period = TowerTime.ZeroBasedPeriodNumber;
            string fp = BuildFingerprint(chaseGoalsEnabled, quantity, scoop, binary);
            bool periodChanged = period != _lastPeriodLogged;
            bool goalsChanged = fp != _lastFingerprint;
            if (!periodChanged && !goalsChanged)
                return;

            _lastPeriodLogged = period;
            _lastFingerprint = fp;

            string date = TowerTime.CurrentTime.ToString();
            if (!chaseGoalsEnabled)
            {
                AssignmentLog.Info(
                    "GOALS",
                    "Period "
                        + period
                        + " ("
                        + date
                        + "): ChaseGoals is off - no goal-based prioritization."
                );
                return;
            }

            string quantityNames = FormatTagNames(quantity);
            string scoopNames = FormatTagNames(scoop);
            string binaryNames = FormatTagNames(binary);
            AssignmentLog.Info(
                "GOALS",
                "Period "
                    + period
                    + " ("
                    + date
                    + "): chasing quantity (scaling reward) ["
                    + (string.IsNullOrEmpty(quantityNames) ? "none" : quantityNames)
                    + "]; scoop-required ["
                    + (string.IsNullOrEmpty(scoopNames) ? "none" : scoopNames)
                    + "]; binary (threshold) ["
                    + (string.IsNullOrEmpty(binaryNames) ? "none" : binaryNames)
                    + "]."
            );
        }

        private static string BuildFingerprint(
            bool chaseGoalsEnabled,
            HashSet<PlayerStatDataTag> quantity,
            HashSet<PlayerStatDataTag> scoop,
            HashSet<PlayerStatDataTag> binary
        )
        {
            return chaseGoalsEnabled
                + "|"
                + FormatTagNames(quantity)
                + "|"
                + FormatTagNames(scoop)
                + "|"
                + FormatTagNames(binary);
        }

        private static string FormatTagNames(HashSet<PlayerStatDataTag> tags)
        {
            if (tags == null || tags.Count == 0)
                return "";
            return string.Join(
                ", ",
                tags.Where(tag => tag != null)
                    .Select(tag => tag.name)
                    .Distinct()
                    .OrderBy(name => name)
            );
        }
    }
}
