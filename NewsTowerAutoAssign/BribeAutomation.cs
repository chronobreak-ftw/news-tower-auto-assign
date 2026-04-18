using System;
using System.Collections.Generic;
using System.Reflection;
using GameState;
using Global_News_System.News_Items.Newsbook.Minigames.Bribe;
using GlobalNews;
using Reportables;
using Tower_Stats;
using UnityEngine;

namespace NewsTowerAutoAssign
{
    internal static class BribeAutomation
    {
        private static readonly FieldInfo _bribedEventField = typeof(BribeMinigame).GetField(
            BribedEventFieldName,
            BindingFlags.NonPublic | BindingFlags.Static
        );

        private static readonly Dictionary<NewsItemBribeComponent, int> _pendingCosts =
            new Dictionary<NewsItemBribeComponent, int>();

        private const string BribedEventFieldName = "Bribed";

        internal static void ResetForNewSave() => _pendingCosts.Clear();

        internal static bool StoryIsPlayerBribeControlled(NewsItem newsItem)
        {
            if (newsItem == null)
                return false;
            bool hasPendingBribe = false;
            foreach (var bribe in newsItem.GetComponentsInChildren<NewsItemBribeComponent>(true))
            {
                if (bribe != null && !bribe.IsCompleted && !bribe.IsDestroyed)
                {
                    hasPendingBribe = true;
                    break;
                }
            }
            return DiscardPredicates.ShouldHandleBribeManually(
                AutoAssignPlugin.AutoResolveBribes.Value,
                hasPendingBribe
            );
        }

        internal static void TryPayBribes(NewsItem newsItem)
        {
            if (!IsBribeAutomationReady(newsItem, out var difficulty))
                return;

            try
            {
                int minMoney = difficulty.bribeSettings.minMoney;
                var newspaper = NewspaperManager.Instance?.CurrentNewspaper;

                foreach (
                    var bribe in newsItem.GetComponentsInChildren<NewsItemBribeComponent>(true)
                )
                    TryPayBribe(newsItem, bribe, difficulty, newspaper, minMoney);
            }
            catch (Exception ex)
            {
                AssignmentLog.Error(
                    "BribeAutomation.TryPayBribes(" + AssignmentLog.StoryName(newsItem) + "): " + ex
                );
            }
        }

        private static bool IsBribeAutomationReady(
            NewsItem newsItem,
            out DifficultySettings difficulty
        )
        {
            difficulty = null;
            if (!AutoAssignPlugin.AutoResolveBribes.Value || newsItem == null)
                return false;
            if (!SafetyGate.IsOpen)
                return false;
            if (TowerStats.Instance == null)
                return false;
            difficulty = GameModeSettings.GetCurrentDifficultySettings();
            return difficulty?.bribeSettings != null;
        }

        private static void TryPayBribe(
            NewsItem newsItem,
            NewsItemBribeComponent bribe,
            DifficultySettings difficulty,
            Newspaper newspaper,
            int minMoney
        )
        {
            if (!IsBribeUnresolved(bribe))
            {
                if (bribe != null)
                    _pendingCosts.Remove(bribe);
                return;
            }

            var node = bribe.GetComponentInParent<NewsItemNode>(true);
            if (node?.NodeState != NewsItemNodeState.Unlocked)
                return;

            int cost = GetOrDrawBribeCost(bribe, difficulty, newspaper, minMoney);

            if ((float)cost > TowerStats.Instance.Money)
            {
                LogUnaffordable(newsItem, cost);
                return;
            }

            PayBribe(newsItem, bribe, cost);
        }

        private static bool IsBribeUnresolved(NewsItemBribeComponent bribe) =>
            bribe != null && !bribe.IsCompleted && !bribe.IsDestroyed && !bribe.IsChosen;

        private static int GetOrDrawBribeCost(
            NewsItemBribeComponent bribe,
            DifficultySettings difficulty,
            Newspaper newspaper,
            int minMoney
        )
        {
            if (_pendingCosts.TryGetValue(bribe, out int cached))
                return cached;
            int maxMoney = Mathf.Max(bribe.GetMaxMoney(difficulty, newspaper), minMoney);
            int cost = DRNG.Range(bribe, minMoney, maxMoney);
            _pendingCosts[bribe] = cost;
            return cost;
        }

        private static void LogUnaffordable(NewsItem newsItem, int cost)
        {
            AssignmentLog.DecisionOnce(
                newsItem,
                "bribe_unaffordable",
                AssignmentLog.StoryName(newsItem)
                    + " "
                    + AssignmentLog.StoryTagList(newsItem)
                    + " → WAIT (bribe): cost "
                    + cost
                    + " exceeds available money "
                    + (int)TowerStats.Instance.Money
                    + " (will retry next scan)."
            );
        }

        private static void PayBribe(NewsItem newsItem, NewsItemBribeComponent bribe, int cost)
        {
            TowerStats.Instance.AddMoney(-(float)cost, false);
            var handler = _bribedEventField?.GetValue(null) as Action<float>;
            handler?.Invoke(-(float)cost);
            bribe.IsCompleted = true;
            _pendingCosts.Remove(bribe);
            AssignmentLog.ClearSuppression(newsItem);
            AssignmentLog.Decision(
                AssignmentLog.StoryName(newsItem)
                    + " "
                    + AssignmentLog.StoryTagList(newsItem)
                    + " → BRIBE PAID: cost "
                    + cost
                    + "."
            );
        }
    }
}
