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
    // Pays newsbook bribe nodes directly without opening the UI popup.
    //
    // Why bypass the popup: the bribe node lives in a story's newsbook page. The
    // MinigamePopup only opens when the player clicks the node while the newsbook is
    // open. Calling OnClicked() from a background patch is unsafe - if the event
    // timeline hasn't armed its recorder yet, Play == null and OnClicked() silently
    // sets IsChosen = true, permanently blocking future manual clicks.
    //
    // Instead we replicate BribeMinigame.Initialize()'s exact cost calculation:
    //   cost = DRNG.Range(bribeComponent, minMoney, maxMoney)
    // The draw is cached per-bribe so re-scans don't advance DRNG again. That
    // matches manual play, which only draws once (in BribeMinigame.Initialize).
    //
    // Why the cache matters: DRNG.Range mutates state on one of 96 slots keyed
    // off the save-component reference, and that state is persisted via
    // DRNG.ComponentData. Drawing on every scan while the player is broke would
    // advance the slot's state N times instead of once, silently desyncing
    // every downstream draw that hashes into the same slot. See
    // GameState/DRNG.cs for the slot pool.
    //
    // If we cannot afford the cost we leave the bribe untouched so the player
    // can handle it manually; we do NOT discard the story for an unpaid bribe.
    internal static class BribeAutomation
    {
        // Static event backing field on BribeMinigame - invoked when we pay directly
        // so finance tracking and quest requirements still fire.
        private static readonly FieldInfo _bribedEventField = typeof(BribeMinigame).GetField(
            BribedEventFieldName,
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Per-component cached DRNG roll. Main-thread-only; cleared on every
        // save load via ResetForNewSave so stale entries from a previous save
        // can't leak into a new one. Lookup is keyed by component identity
        // rather than save-component reference string - cheaper and immune to
        // the save-ref rebuild that happens around OnAfterLoadStart.
        private static readonly Dictionary<NewsItemBribeComponent, int> _pendingCosts =
            new Dictionary<NewsItemBribeComponent, int>();

        private const string BribedEventFieldName = "Bribed";

        // Called by Patch_LRMAwake alongside the decision-log reset. The bribe
        // cache stores raw DRNG draws that are only meaningful for a specific
        // save's component graph; loading a different save would otherwise
        // reuse numbers drawn against the old save's DRNG state.
        internal static void ResetForNewSave() => _pendingCosts.Clear();

        // When bribe auto-pay is disabled, any news item that still has an incomplete
        // bribe node is left entirely to the player: no auto-assign, discards,
        // suitcase unlock, or bribe payment until every bribe on the item is
        // completed or destroyed.
        internal static bool StoryIsPlayerBribeControlled(NewsItem newsItem)
        {
            if (newsItem == null || AutoAssignPlugin.AutoResolveBribes.Value)
                return false;
            foreach (var bribe in newsItem.GetComponentsInChildren<NewsItemBribeComponent>(true))
            {
                if (bribe != null && !bribe.IsCompleted && !bribe.IsDestroyed)
                    return true;
            }
            return false;
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
                // Reflection / Unity component walks on an in-flight story can
                // surface transient nulls - log once and move on rather than
                // let the exception climb into the evaluator or Harmony patch.
                AssignmentLog.Error(
                    "BribeAutomation.TryPayBribes(" + AssignmentLog.StoryName(newsItem) + "): " + ex
                );
            }
        }

        // Cheap gate that checks config, save state, global references, and
        // difficulty settings before any per-bribe work runs. Mid-save-load,
        // TowerStats.Money may not yet reflect the restored balance and the
        // bribe component's IsCompleted flag may be about to be reset by
        // SetComponentData. Paying now can either charge against a transient
        // zero balance (free bribe) or double-charge a bribe the save had
        // already completed. Defer to the central gate - see SafetyGate for
        // the open/close event map.
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

        // Per-bribe flow: resolve, skip if not in the "unresolved + unlocked"
        // state, draw/reuse the cost, and pay when affordable. Logs are
        // suppressed via DecisionOnce so a permanently-unaffordable bribe
        // emits at most one unaffordability line.
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
                // A bribe that has exited the "unresolved" state no longer
                // needs a pending cost - drop it so the cache can't leak
                // across a component lifetime.
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

        // Draw once per bribe; reuse on subsequent scans until either paid
        // (cleared in PayBribe) or the component resolves some other way
        // (cleared in TryPayBribe's not-unresolved branch). This matches
        // BribeMinigame.Initialize's behaviour, which draws exactly once.
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
