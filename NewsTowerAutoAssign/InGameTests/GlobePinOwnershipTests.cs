using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameState;
using GlobalNews;
using Reportables;
using Risks;
using Tower_Stats;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewsTowerAutoAssign.InGameTests
{
    internal static class GlobePinOwnershipTests
    {
        internal static void Run()
        {
            var ctx = new TestContext("GlobePinOwnership");
            NullGuards(ctx);
            PinColorReflectionTargetResolvable(ctx);
            InProgressStoriesAreInRegistry(ctx);
            WaitStoriesAreInRegistry(ctx);
            WaitPathStoriesNotInRegistry(ctx);
            PinColorsMatchOwnershipState(ctx);
            RegistryRoundTrip(ctx);
            ResetClearsRegistry(ctx);
            ModAssignedStoriesAreFullySeen(ctx);
            ctx.PrintSummary();
        }

        private static void NullGuards(TestContext ctx)
        {
            bool threw = false;
            bool result = true;
            try
            {
                result = AutoAssignOwnershipRegistry.IsModAutoAssigned(null);
            }
            catch
            {
                threw = true;
            }
            ctx.Assert(!threw, "IsModAutoAssigned(null) does not throw");
            ctx.Assert(!result, "IsModAutoAssigned(null) returns false");
        }

        private static void InProgressStoriesAreInRegistry(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("in-progress stories in registry", "no LiveReportableManager");
                return;
            }

            int checkedCount = 0;
            int missedCount = 0;
            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null || !AssignmentEvaluator.IsAnySlotInProgress(ni))
                    continue;
                checkedCount++;
                if (!AutoAssignOwnershipRegistry.IsModAutoAssigned(ni))
                    missedCount++;
            }

            if (checkedCount == 0)
            {
                ctx.NotApplicable(
                    "in-progress stories in registry",
                    "no in-progress stories on the board right now"
                );
                return;
            }

            ctx.Assert(
                missedCount == 0,
                "every in-progress story is in the ownership registry",
                missedCount + " of " + checkedCount + " in-progress stories not registered"
            );
        }

        private static void WaitStoriesAreInRegistry(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("wait-story registry", "no LiveReportableManager");
                return;
            }

            if (!AutoAssignPlugin.AutoAssignEnabled.Value)
            {
                ctx.NotApplicable("wait-story registry", "AutoAssign is disabled");
                return;
            }

            int reporterCount = ReporterLookup.CountPlayableReporters();
            if (reporterCount < AutoAssignPlugin.MinReportersToActivate.Value)
            {
                ctx.NotApplicable(
                    "wait-story registry",
                    "below reporter threshold ("
                        + reporterCount
                        + " < "
                        + AutoAssignPlugin.MinReportersToActivate.Value
                        + ") - mod is passive"
                );
                return;
            }

            var (quantity, _, binary) = ReporterLookup.GetCurrentGoalTagSets();
            var inProgress = ReporterLookup.GetInProgressTags();
            bool goalsLoaded = quantity.Count > 0 || binary.Count > 0;
            bool isWeekend = TowerTimes.IsWeekend(TowerTime.CurrentTime);

            int checkedCount = 0;
            int missedCount = 0;
            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null)
                    continue;
                if (AssignmentEvaluator.IsAnySlotInProgress(ni))
                    continue;

                var slots = OpenAssignableSlots(ni);
                if (slots.Count == 0)
                    continue;

                bool hasRisk = ni.GetComponentsInChildren<INewsItemRisk>(true).Any();
                bool matchesGoal =
                    goalsLoaded
                    && AssignmentRules.StoryMatchesUncoveredGoal(
                        ni.Data.DistinctStatTypes.OfType<PlayerStatDataTag>(),
                        quantity,
                        binary,
                        inProgress
                    );

                if (
                    DiscardPredicates.ShouldDiscardForRisk(
                        AutoAssignPlugin.AvoidRisksEnabled.Value,
                        AutoAssignPlugin.ChaseGoalsEnabled.Value,
                        isInvested: false,
                        goalsLoaded,
                        hasRisk,
                        matchesGoal
                    )
                )
                    continue;
                if (
                    DiscardPredicates.ShouldDiscardForWeekend(
                        AutoAssignPlugin.DiscardFreshStoriesOnWeekend.Value,
                        isInvested: false,
                        isWeekend,
                        matchesGoal
                    )
                )
                    continue;

                if (
                    AutoAssignPlugin.AutoAssignOnlyObviousPath.Value
                    && AssignmentEvaluator.HasXorAmbiguousSlots(slots)
                )
                    continue;

                checkedCount++;
                bool registered = AutoAssignOwnershipRegistry.IsModAutoAssigned(ni);
                if (!registered)
                    missedCount++;
                ctx.Assert(
                    registered,
                    "wait-story claimed: " + ShortName(ni),
                    "open slots + passed all discard gates but not in ownership registry"
                );
            }

            if (checkedCount == 0)
                ctx.NotApplicable(
                    "wait-story registry",
                    "no non-in-progress stories with open slots survived all discard gates"
                );
        }

        private static void WaitPathStoriesNotInRegistry(TestContext ctx)
        {
            if (!AutoAssignPlugin.AutoAssignOnlyObviousPath.Value)
            {
                ctx.NotApplicable(
                    "wait-path stories not in registry",
                    "AutoAssignOnlyObviousPath is off - WAIT(path) never fires"
                );
                return;
            }
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("wait-path stories not in registry", "no LiveReportableManager");
                return;
            }

            int checkedCount = 0;
            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null)
                    continue;
                if (AssignmentEvaluator.IsAnySlotInProgress(ni))
                    continue;

                var slots = OpenAssignableSlots(ni);
                if (!AssignmentEvaluator.HasXorAmbiguousSlots(slots))
                    continue;

                checkedCount++;
                ctx.Assert(
                    !AutoAssignOwnershipRegistry.IsModAutoAssigned(ni),
                    "XOR-ambiguous story not in registry (player decides): " + ShortName(ni),
                    "story has XOR-ambiguous open paths but is marked as mod-owned"
                );
            }

            if (checkedCount == 0)
                ctx.NotApplicable(
                    "wait-path stories not in registry",
                    "no XOR-ambiguous fresh stories on board right now"
                );
        }

        private static void RegistryRoundTrip(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("registry round-trip", "no LiveReportableManager");
                return;
            }

            var target = LiveReportableManager
                .Instance.GetNewsItems()
                .FirstOrDefault(ni => ni?.Data != null);

            if (target == null)
            {
                ctx.NotApplicable("registry round-trip", "no news items on the board right now");
                return;
            }

            AutoAssignOwnershipRegistry.MarkModAutoAssigned(target);
            ctx.Assert(
                AutoAssignOwnershipRegistry.IsModAutoAssigned(target),
                "MarkModAutoAssigned makes IsModAutoAssigned return true"
            );
        }

        private static void ResetClearsRegistry(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("reset clears registry", "no LiveReportableManager");
                return;
            }

            var items = LiveReportableManager.Instance.GetNewsItems().ToList();
            AutoAssignOwnershipRegistry.ResetForNewSave();
            try
            {
                int wrong = 0;
                foreach (var ni in items)
                    if (ni?.Data != null && AutoAssignOwnershipRegistry.IsModAutoAssigned(ni))
                        wrong++;
                ctx.Assert(
                    wrong == 0,
                    "after ResetForNewSave no story is marked auto-assigned",
                    wrong + " of " + items.Count + " stories still show as auto-assigned"
                );
            }
            finally
            {
                foreach (var ni in items)
                {
                    if (ni?.Data == null)
                        continue;
                    if (AssignmentEvaluator.IsAnySlotInProgress(ni))
                    {
                        AutoAssignOwnershipRegistry.MarkModAutoAssigned(ni);
                        GlobeAttentionSync.PromoteFullySeen(ni);
                    }
                }
            }
        }

        private static void ModAssignedStoriesAreFullySeen(TestContext ctx)
        {
            if (LiveReportableManager.Instance == null)
            {
                ctx.Skip("assigned stories are FullSeen", "no LiveReportableManager");
                return;
            }

            int checkedCount = 0;
            int wrongCount = 0;
            foreach (var ni in LiveReportableManager.Instance.GetNewsItems())
            {
                if (ni?.Data == null || !AutoAssignOwnershipRegistry.IsModAutoAssigned(ni))
                    continue;
                checkedCount++;
                if (ni.UnseenState != UnseenState.FullSeen)
                    wrongCount++;
            }

            if (checkedCount == 0)
            {
                ctx.NotApplicable(
                    "assigned stories are FullSeen",
                    "no mod-auto-assigned stories in registry after reset"
                );
                return;
            }

            ctx.Assert(
                wrongCount == 0,
                "every mod-assigned story has UnseenState=FullSeen",
                wrongCount + " of " + checkedCount + " mod-assigned stories not FullSeen"
            );
        }

        private static void PinColorReflectionTargetResolvable(TestContext ctx)
        {
            ctx.Assert(
                Patch_LocationDisplayRefreshOwnership.InnerImageFieldResolvable,
                "LocationStatusLabel.innerImage field resolvable",
                "field not found - pin tinting falls back to GetComponents<Image>() which may return wrong images"
            );
        }

        private static void PinColorsMatchOwnershipState(TestContext ctx)
        {
            if (!AutoAssignPlugin.GlobePinOwnershipEnabled.Value)
            {
                ctx.NotApplicable(
                    "pin colors match ownership state",
                    "GlobePinOwnershipEnabled is off"
                );
                return;
            }

            var displays = Object.FindObjectsOfType<LocationDisplay>();
            if (displays == null || displays.Length == 0)
            {
                ctx.NotApplicable(
                    "pin colors match ownership state",
                    "no active LocationDisplay instances - open the globe to run this test"
                );
                return;
            }

            int checkedCount = 0;
            int violations = 0;
            foreach (var display in displays)
            {
                if (display == null)
                    continue;
                var statusLabel = display.GetComponentInChildren<LocationStatusLabel>(true);
                if (statusLabel == null)
                    continue;
                var images = Patch_LocationDisplayRefreshOwnership.GetPinImagesForLabel(
                    statusLabel
                );
                if (images == null || images.Length == 0)
                    continue;

                checkedCount++;
                var expectedColor = Patch_LocationDisplayRefreshOwnership.GetExpectedTintForDisplay(
                    display
                );

                Color? wrongColor = null;
                foreach (var image in images)
                {
                    if (image != null && image.color != expectedColor)
                    {
                        wrongColor = image.color;
                        break;
                    }
                }

                if (wrongColor.HasValue)
                {
                    violations++;
                    ctx.Fail(
                        "pin color: " + (display.name ?? "?"),
                        "expected=" + expectedColor + " actual=" + wrongColor.Value
                    );
                }
            }

            if (violations == 0)
            {
                if (checkedCount == 0)
                    ctx.NotApplicable(
                        "pin colors match ownership state",
                        "no LocationDisplay found with a StatusLabel and images"
                    );
                else
                    ctx.Pass(
                        "pin colors match ownership state: all " + checkedCount + " pins correct"
                    );
            }
        }

        private static List<NewsItemStoryFile> OpenAssignableSlots(NewsItem newsItem)
        {
            var slots = new List<NewsItemStoryFile>();
            newsItem.GetUnlockedAndAssignableStoryFiles(slots);
            return slots
                .Where(sf =>
                    sf != null
                    && sf.Assignee == null
                    && !sf.IsCompleted
                    && !GameReflection.IsSlotAlreadyRunning(sf)
                    && sf.Node?.NodeState != NewsItemNodeState.Locked
                    && sf.Node?.NodeState != NewsItemNodeState.Destroyed
                )
                .ToList();
        }

        private static string ShortName(NewsItem ni)
        {
            var name = ni.Data.name ?? "?";
            var paren = name.LastIndexOf(" (");
            return paren > 0 ? name.Substring(0, paren) : name;
        }
    }
}
