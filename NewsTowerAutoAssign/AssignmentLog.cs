using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Reportables;
using Tower_Stats;

namespace NewsTowerAutoAssign
{
    internal static class AssignmentLog
    {
        private static readonly HashSet<string> _suppressedDecisions = new HashSet<string>();

        [Conditional("DEBUG")]
        internal static void Info(string area, string message)
        {
            AutoAssignPlugin.Log.LogInfo("[" + area + "] " + message);
        }

        [Conditional("DEBUG")]
        internal static void Decision(string message)
        {
            AutoAssignPlugin.Log.LogInfo("[DECISION] " + message);
        }

        [Conditional("DEBUG")]
        internal static void Discard(string message)
        {
            AutoAssignPlugin.Log.LogWarning("[DISCARD] " + message);
        }

        [Conditional("DEBUG")]
        internal static void Warn(string area, string message)
        {
            AutoAssignPlugin.Log.LogWarning("[" + area + "] " + message);
        }

        internal static void Error(string message) => AutoAssignPlugin.Log.LogError(message);

        [Conditional("DEBUG")]
        internal static void Verbose(string area, string message)
        {
#if DEBUG
            if (AutoAssignPlugin.VerboseLogs != null && AutoAssignPlugin.VerboseLogs.Value)
                AutoAssignPlugin.Log.LogInfo("[VERBOSE:" + area + "] " + message);
#endif
        }

        [Conditional("DEBUG")]
        internal static void DecisionOnce(Reportable reportable, string decisionKey, string message)
        {
            if (!_suppressedDecisions.Add(StoryId(reportable) + ":" + decisionKey))
                return;
            AutoAssignPlugin.Log.LogInfo("[DECISION] " + message);
        }

        internal static void ClearSuppression(Reportable reportable)
        {
            var prefix = StoryId(reportable) + ":";
            _suppressedDecisions.RemoveWhere(key => key.StartsWith(prefix));
        }

        internal static void ResetForNewSave()
        {
            _suppressedDecisions.Clear();
        }

        private static string StoryId(Reportable reportable) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(reportable).ToString();

        internal static string StoryName(NewsItem newsItem)
        {
            var name = newsItem?.Data?.name ?? "Unknown story";
            var paren = name.LastIndexOf(" (");
            return paren > 0 ? name.Substring(0, paren) : name;
        }

        internal static string StoryTagList(NewsItem newsItem)
        {
            if (newsItem?.Data == null)
                return "[]";
            var tags = newsItem
                .Data.DistinctStatTypes.OfType<PlayerStatDataTag>()
                .Where(tag => tag != null)
                .Select(tag => tag.name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return tags.Length == 0 ? "[]" : "[" + string.Join(", ", tags) + "]";
        }

        internal static string GoalSnapshot(
            HashSet<PlayerStatDataTag> quantityGoalTags,
            HashSet<PlayerStatDataTag> binaryGoalTags,
            HashSet<PlayerStatDataTag> inProgressTags
        )
        {
            var quantity =
                quantityGoalTags == null || quantityGoalTags.Count == 0
                    ? "none"
                    : string.Join(
                        ", ",
                        quantityGoalTags
                            .Where(tag => tag != null)
                            .Select(tag => tag.name)
                            .OrderBy(name => name, StringComparer.Ordinal)
                    );
            var binary =
                binaryGoalTags == null || binaryGoalTags.Count == 0
                    ? "none"
                    : string.Join(
                        ", ",
                        binaryGoalTags
                            .Where(tag => tag != null)
                            .OrderBy(tag => tag.name, StringComparer.Ordinal)
                            .Select(tag =>
                                tag.name
                                + (
                                    inProgressTags != null && inProgressTags.Contains(tag)
                                        ? "(covered)"
                                        : "(uncovered)"
                                )
                            )
                    );
            return "binary={" + binary + "} quantity={" + quantity + "}";
        }
    }
}
