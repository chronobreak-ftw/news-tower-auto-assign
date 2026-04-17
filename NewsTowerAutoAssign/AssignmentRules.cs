using System;
using System.Collections.Generic;
using System.Linq;

namespace NewsTowerAutoAssign
{
    // Priority score for a story-file path given the current goal landscape.
    // Declared as an int-backed enum so the numeric ordering is meaningful
    // (`>` / `<` comparisons are preserved) AND the name reads in log output.
    internal enum PathPriority
    {
        // No goal match - the path yields nothing the mod is chasing.
        None = -1,

        // Path matches a binary goal that an in-progress story already covers.
        // Still surfaced so multi-path stories can pick it over None, but it's
        // effectively wasted work from a goal-chasing POV.
        CoveredBinary = 0,

        // Path advances a scaling-reward (quantity) goal - more tagged copies
        // keep producing more reward, so always worth chasing even if another
        // in-progress story already matches.
        Quantity = 1,

        // Path advances a binary (threshold) goal that nothing on the board
        // has yet covered. Standard "go grab that uncovered check mark".
        UncoveredBinary = 2,

        // Path advances an uncovered binary goal AND the tag is scoop-required
        // AND this path's yield qualifies as a scoop - the highest-value
        // assignment we can make.
        UncoveredScoop = 3,
    }

    // Pure rules used by the assignment evaluator. Generic on the tag type so the
    // NUnit test project can exercise the logic without referencing game DLLs -
    // tests use `string` as TTag, runtime callers use PlayerStatDataTag.
    internal static class AssignmentRules
    {
        // True iff any of the story's stat tags matches an active goal that
        // still deserves chasing right now:
        //   * quantity goal (reward scales per-copy, so always "uncovered")
        //   * binary goal not yet covered by any in-progress story
        internal static bool StoryMatchesUncoveredGoal<TTag>(
            IEnumerable<TTag> storyTags,
            HashSet<TTag> quantityGoalTags,
            HashSet<TTag> binaryGoalTags,
            HashSet<TTag> inProgressTags
        )
        {
            return storyTags.Any(tag =>
                quantityGoalTags.Contains(tag)
                || (binaryGoalTags.Contains(tag) && !inProgressTags.Contains(tag))
            );
        }

        // Scoop priority requires the tag to still be uncovered - once we've
        // already assigned a story that matches the scoop tag, the district
        // goal is on the rails and further scoop-matching paths just duplicate
        // effort.
        //
        // isScoop is a delegate so this class doesn't need a reference to
        // NewsItemStoryFile; callers bind it to storyFile.IsScoop(tag) at runtime.
        internal static PathPriority GetPathGoalPriority<TTag>(
            IEnumerable<TTag> yieldTags,
            HashSet<TTag> quantityGoalTags,
            HashSet<TTag> scoopGoalTags,
            HashSet<TTag> binaryGoalTags,
            HashSet<TTag> inProgressTags,
            Func<TTag, bool> isScoop
        ) =>
            GetPathGoalPriorityDetail(
                yieldTags,
                quantityGoalTags,
                scoopGoalTags,
                binaryGoalTags,
                inProgressTags,
                isScoop,
                _ => ""
            ).priority;

        // Same scoring as GetPathGoalPriority; labels are the distinct yield tags that
        // achieved the winning score (for log lines). formatTag maps each tag to a name.
        internal static (PathPriority priority, string[] labels) GetPathGoalPriorityDetail<TTag>(
            IEnumerable<TTag> yieldTags,
            HashSet<TTag> quantityGoalTags,
            HashSet<TTag> scoopGoalTags,
            HashSet<TTag> binaryGoalTags,
            HashSet<TTag> inProgressTags,
            Func<TTag, bool> isScoop,
            Func<TTag, string> formatTag
        )
        {
            var best = PathPriority.None;
            var atBest = new List<TTag>();
            foreach (var tag in yieldTags)
            {
                if (tag == null)
                    continue;
                PathPriority score;
                bool binaryUncovered =
                    binaryGoalTags.Contains(tag) && !inProgressTags.Contains(tag);
                if (binaryUncovered && scoopGoalTags.Contains(tag) && isScoop(tag))
                    score = PathPriority.UncoveredScoop;
                else if (binaryUncovered)
                    score = PathPriority.UncoveredBinary;
                else if (quantityGoalTags.Contains(tag))
                    score = PathPriority.Quantity;
                else if (binaryGoalTags.Contains(tag))
                    score = PathPriority.CoveredBinary;
                else
                    continue;

                if (score > best)
                {
                    best = score;
                    atBest.Clear();
                    atBest.Add(tag);
                }
                else if (score == best)
                    atBest.Add(tag);

                if (best == PathPriority.UncoveredScoop)
                    break;
            }

            if (best == PathPriority.None)
                return (PathPriority.None, Array.Empty<string>());

            var labels = atBest
                .Select(tag => formatTag(tag))
                .Where(label => !string.IsNullOrEmpty(label))
                .Distinct()
                .OrderBy(label => label, StringComparer.Ordinal)
                .ToArray();
            return (best, labels);
        }
    }
}
