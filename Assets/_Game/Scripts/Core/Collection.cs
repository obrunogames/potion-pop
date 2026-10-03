using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Card collection (GDD §5): one album of 9 magical cards per world. Each first win of a level grants a card from
    /// the unlocked worlds (70% chance of a missing one while any is missing); a duplicate gives +10 coins. A complete
    /// album can be claimed once for 500 coins + 1 of each booster.
    /// </summary>
    public static class Collection
    {
        public const int DuplicateCoins = 10;
        public const int AlbumCoins = 500;
        public const float MissingCardChance = 0.7f;
        public static event Action OnChanged;

        static readonly List<string> _candidates = new List<string>(64);
        static readonly List<string> _missing = new List<string>(64);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        public static bool Has(string cardId) => !string.IsNullOrEmpty(cardId) && SaveSystem.Data.cards.Contains(cardId);

        public static int OwnedCount(string areaId)
        {
            var area = Catalog.GetArea(areaId);
            if (area == null || area.cardIds == null) return 0;
            int n = 0;
            foreach (var p in area.cardIds)
                if (Has(p)) n++;
            return n;
        }

        /// <summary>Cards in an album (9 per world).</summary>
        public static int AlbumSize(string areaId)
        {
            var area = Catalog.GetArea(areaId);
            return area != null && area.cardIds != null ? area.cardIds.Length : 0;
        }

        public static int TotalOwned => SaveSystem.Data.cards.Count;

        /// <summary>Total cards in all albums.</summary>
        public static int TotalCards
        {
            get
            {
                int n = 0;
                foreach (var a in Catalog.Areas) n += a.cardIds != null ? a.cardIds.Length : 0;
                return n;
            }
        }

        /// <summary>Gives one random card from unlocked areas (GDD §5). Duplicate adds coins.</summary>
        public static string GrantRandomCard(out bool duplicate)
        {
            duplicate = false;
            _candidates.Clear();
            _missing.Clear();
            foreach (var area in Catalog.Areas)
            {
                if (!IsAreaUnlocked(area) || area.cardIds == null) continue;
                foreach (var p in area.cardIds)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    _candidates.Add(p);
                    if (!Has(p)) _missing.Add(p);
                }
            }
            if (_candidates.Count == 0) return null;   // catalog not available

            var pool = _missing.Count > 0 && CoreRandom.Value() < MissingCardChance ? _missing : _candidates;
            string card = pool[CoreRandom.Range(0, pool.Count)];
            Grant(card, out duplicate);
            return card;
        }

        /// <summary>Adds a specific card (reward). A duplicate gives <see cref="DuplicateCoins"/> coins instead.</summary>
        public static void Grant(string cardId, out bool duplicate)
        {
            duplicate = Has(cardId);
            if (string.IsNullOrEmpty(cardId)) return;
            if (duplicate) Economy.AddCoins(DuplicateCoins, "card_duplicate");
            else
            {
                SaveSystem.Data.cards.Add(cardId);
                SaveSystem.MarkDirty();
            }
            OnChanged?.Invoke();
        }

        /// <summary>An area's album is unlocked once the player reached that area (all after the first cycle).</summary>
        public static bool IsAreaUnlocked(string areaId) => IsAreaUnlocked(Catalog.GetArea(areaId));

        static bool IsAreaUnlocked(AreaInfo area)
        {
            if (area == null) return false;
            return area.index <= Areas.AreaNumberForLevel(Progress.CurrentLevel);
        }

        public static bool AlbumComplete(string areaId)
        {
            var area = Catalog.GetArea(areaId);
            if (area == null || area.cardIds == null || area.cardIds.Length == 0) return false;
            foreach (var p in area.cardIds)
                if (!Has(p)) return false;
            return true;
        }

        public static bool AlbumClaimed(string areaId) => !string.IsNullOrEmpty(areaId) && SaveSystem.Data.albumsClaimed.Contains(areaId);

        /// <summary>The rewards of a complete album (shown before claiming).</summary>
        public static Reward[] AlbumRewards()
        {
            var rewards = new Reward[1 + Economy.AllBoosters.Length];
            rewards[0] = Reward.Coins(AlbumCoins);
            for (int i = 0; i < Economy.AllBoosters.Length; i++) rewards[i + 1] = Reward.Booster(Economy.AllBoosters[i], 1);
            return rewards;
        }

        public static Reward[] ClaimAlbum(string areaId)
        {
            if (!AlbumComplete(areaId) || AlbumClaimed(areaId)) return Array.Empty<Reward>();
            SaveSystem.Data.albumsClaimed.Add(areaId);
            var rewards = AlbumRewards();
            for (int i = 0; i < rewards.Length; i++) rewards[i].Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return rewards;
        }

        public static int ClaimableAlbums
        {
            get
            {
                int n = 0;
                foreach (var a in Catalog.Areas)
                    if (AlbumComplete(a.id) && !AlbumClaimed(a.id)) n++;
                return n;
            }
        }
    }
}
