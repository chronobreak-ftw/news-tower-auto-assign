using Reportables;

namespace NewsTowerAutoAssign
{
    internal static class GlobeAttentionSync
    {
        internal static void PromoteFullySeen(NewsItem newsItem)
        {
            if (newsItem == null)
                return;
            if (newsItem.UnseenState == UnseenState.FullSeen)
                return;
            newsItem.UnseenState = UnseenState.FullSeen;
        }
    }
}
