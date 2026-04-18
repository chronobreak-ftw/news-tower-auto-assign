using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Reportables;

namespace NewsTowerAutoAssign
{
    internal static class AutoAssignOwnershipRegistry
    {
        private static readonly HashSet<int> AutoAssignedIds = new HashSet<int>();

        internal static void ResetForNewSave() => AutoAssignedIds.Clear();

        internal static bool IsModAutoAssigned(NewsItem newsItem)
        {
            if (newsItem == null)
                return false;
            return AutoAssignedIds.Contains(RuntimeHelpers.GetHashCode(newsItem));
        }

        internal static void MarkModAutoAssigned(NewsItem newsItem)
        {
            if (newsItem == null)
                return;
            int id = RuntimeHelpers.GetHashCode(newsItem);
            if (!AutoAssignedIds.Add(id))
                return;
            newsItem.Removed += () => AutoAssignedIds.Remove(id);
        }
    }
}
