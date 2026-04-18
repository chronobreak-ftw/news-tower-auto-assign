namespace NewsTowerAutoAssign
{
    internal static class DiscardPredicates
    {
        internal static bool ShouldDiscardForRisk(
            bool avoidRisksEnabled,
            bool chaseGoalsEnabled,
            bool isInvested,
            bool goalsLoaded,
            bool hasRisk,
            bool matchesUncoveredGoal
        )
        {
            if (!avoidRisksEnabled || isInvested || !hasRisk)
                return false;
            if (!chaseGoalsEnabled)
                return true;
            return goalsLoaded && !matchesUncoveredGoal;
        }

        internal static bool ShouldDiscardForWeekend(
            bool featureEnabled,
            bool isInvested,
            bool isWeekend,
            bool matchesUncoveredGoal
        )
        {
            return featureEnabled && !isInvested && isWeekend && !matchesUncoveredGoal;
        }

        internal static bool ShouldHandleBribeManually(bool autoResolveBribes, bool hasPendingBribe) =>
            !autoResolveBribes && hasPendingBribe;

        internal static bool ShouldDiscardForAvailability(
            bool isInvested,
            bool matchesGoal,
            float thresholdHours,
            bool anyReporterSoon
        )
        {
            return !isInvested && !matchesGoal && thresholdHours > 0f && !anyReporterSoon;
        }
    }
}
