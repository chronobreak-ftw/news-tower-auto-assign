using System;
using System.Collections.Generic;
using System.Linq;

namespace NewsTowerAutoAssign
{
    internal enum PathPriority
    {
        None = -1,
        CoveredBinary = 0,
        Quantity = 1,
        UncoveredBinary = 2,
        UncoveredScoop = 3,
    }

    internal static class AssignmentRules
    {
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
