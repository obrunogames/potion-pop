using System;
using UnityEngine;

namespace PotionPop
{
    public enum BoosterType { Undo, Wand, Bottle, Shuffle, Crystal, Rainbow }

    /// <summary>
    /// Coins, stars and booster inventory (GDD §4–§5). Every mutation marks the save dirty and raises its event.
    /// </summary>
    public static class Economy
    {
        public static event Action<int, int> OnCoinsChanged;        // (old, new)
        public static event Action<long, long> OnStarsChanged;      // (old, new) total stars
        public static event Action<BoosterType, int> OnBoosterChanged; // (type, new count)

        /// <summary>Boosters usable on the board (bottom bar, in this order).</summary>
        public static readonly BoosterType[] InGameBoosters = { BoosterType.Undo, BoosterType.Shuffle, BoosterType.Bottle, BoosterType.Wand };
        /// <summary>Boosters picked in the Level Start popup (in this order).</summary>
        public static readonly BoosterType[] PreLevelBoosters = { BoosterType.Rainbow, BoosterType.Crystal };
        /// <summary>Every booster, in enum order.</summary>
        public static readonly BoosterType[] AllBoosters =
            { BoosterType.Undo, BoosterType.Wand, BoosterType.Bottle, BoosterType.Shuffle, BoosterType.Crystal, BoosterType.Rainbow };

        /// <summary>Most Extra Bottles one level can receive (booster + continues).</summary>
        public const int MaxExtraBottles = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnCoinsChanged = null;
            OnStarsChanged = null;
            OnBoosterChanged = null;
        }

        // ------------------------------------------------------------------ coins

        public static int Coins => SaveSystem.Data.coins;

        /// <summary>Adds (or, with a negative amount, removes down to 0) coins. reason is for logs/analytics.</summary>
        public static void AddCoins(int amount, string reason)
        {
            if (amount == 0) return;
            var d = SaveSystem.Data;
            int old = d.coins;
            long next = (long)old + amount;
            d.coins = (int)Math.Max(0, Math.Min(int.MaxValue, next));
            if (d.coins == old) return;
            SaveSystem.MarkDirty();
            OnCoinsChanged?.Invoke(old, d.coins);
        }

        public static bool TrySpendCoins(int amount, string reason)
        {
            if (amount <= 0) return true;
            var d = SaveSystem.Data;
            if (d.coins < amount) return false;
            int old = d.coins;
            d.coins -= amount;
            SaveSystem.MarkDirty();
            OnCoinsChanged?.Invoke(old, d.coins);
            return true;
        }

        public static bool CanAfford(int amount) => SaveSystem.Data.coins >= amount;

        // ------------------------------------------------------------------ stars

        public static long TotalStars => SaveSystem.Data.totalStars;

        /// <summary>Stars earned this week (Monday-based, local time); resets when the week changes.</summary>
        public static long WeeklyStars
        {
            get
            {
                EnsureWeek();
                return SaveSystem.Data.weeklyStars;
            }
        }

        /// <summary>Adds to total stars, weekly stars and star-chest progress.</summary>
        public static void AddStars(int amount)
        {
            if (amount <= 0) return;
            EnsureWeek();
            var d = SaveSystem.Data;
            long old = d.totalStars;
            d.totalStars += amount;
            d.weeklyStars += amount;
            d.starChestProgress = (int)Math.Min(int.MaxValue, (long)d.starChestProgress + amount);
            SaveSystem.MarkDirty();
            OnStarsChanged?.Invoke(old, d.totalStars);
            StarChest.RaiseChanged();
        }

        /// <summary>Resets the weekly star counter when TimeUtil.WeekIndex moved on.</summary>
        /// <remarks>Forward-only: a time zone change that moves the local week back (travel across a Monday boundary)
        /// must not wipe the stars earned this week.</remarks>
        public static void EnsureWeek()
        {
            var d = SaveSystem.Data;
            int week = TimeUtil.WeekIndex;
            if (week <= d.weekId) return;
            d.weekId = week;
            d.weeklyStars = 0;
            SaveSystem.MarkDirtyHousekeeping();   // automatic reset: must not count as fresh progress for the cloud merge
        }

        // ------------------------------------------------------------------ boosters

        public static int GetBooster(BoosterType t)
        {
            var d = SaveSystem.Data;
            switch (t)
            {
                case BoosterType.Undo: return d.undo;
                case BoosterType.Wand: return d.wand;
                case BoosterType.Bottle: return d.bottle;
                case BoosterType.Shuffle: return d.shuffle;
                case BoosterType.Crystal: return d.crystal;
                case BoosterType.Rainbow: return d.rainbow;
                default: return 0;
            }
        }

        public static void AddBooster(BoosterType t, int n)
        {
            if (n == 0) return;
            int count = Math.Max(0, GetBooster(t) + n);
            SetBooster(t, count);
            SaveSystem.MarkDirty();
            OnBoosterChanged?.Invoke(t, count);
        }

        /// <summary>Consumes one booster if available.</summary>
        public static bool TryUseBooster(BoosterType t)
        {
            int count = GetBooster(t);
            if (count <= 0) return false;
            SetBooster(t, count - 1);
            SaveSystem.MarkDirty();
            OnBoosterChanged?.Invoke(t, count - 1);
            return true;
        }

        public static int BoosterUnlockLevel(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.Undo: return 3;
                case BoosterType.Shuffle: return 6;
                case BoosterType.Bottle: return 8;
                case BoosterType.Rainbow: return 10;
                case BoosterType.Wand: return 12;
                case BoosterType.Crystal: return Levels.Difficulty.FirstHiddenLevel;   // 15: hidden colors start there
                default: return 1;
            }
        }

        /// <summary>Unlocked when the player's current level ≥ unlock level.</summary>
        public static bool IsBoosterUnlocked(BoosterType t) => Progress.CurrentLevel >= BoosterUnlockLevel(t);

        public static bool IsPreLevel(BoosterType t) => t == BoosterType.Crystal || t == BoosterType.Rainbow;

        /// <summary>Shop pack for a booster: (count, coin price), see GDD §4.</summary>
        public static (int count, int price) BoosterPack(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.Undo: return (3, 200);
                case BoosterType.Shuffle: return (3, 250);
                case BoosterType.Bottle: return (3, 350);
                case BoosterType.Wand: return (3, 400);
                case BoosterType.Rainbow: return (3, 300);
                case BoosterType.Crystal: return (3, 250);
                default: return (3, 300);
            }
        }

        public static bool TryBuyBoosterPack(BoosterType t)
        {
            var pack = BoosterPack(t);
            if (!TrySpendCoins(pack.price, "booster_pack_" + BoosterId(t))) return false;
            AddBooster(t, pack.count);
            return true;
        }

        public static string BoosterSprite(BoosterType t) => "booster_" + BoosterId(t);
        public static string BoosterNameKey(BoosterType t) => "booster." + BoosterId(t);
        public static string BoosterDescKey(BoosterType t) => "booster." + BoosterId(t) + ".desc";

        static readonly string[] Ids = { "undo", "wand", "bottle", "shuffle", "crystal", "rainbow" };

        /// <summary>Lower-case id ("undo") used in sprite/loc keys and analytics.</summary>
        public static string BoosterId(BoosterType t)
        {
            int i = (int)t;
            return i >= 0 && i < Ids.Length ? Ids[i] : t.ToString().ToLowerInvariant();
        }

        static void SetBooster(BoosterType t, int v)
        {
            var d = SaveSystem.Data;
            switch (t)
            {
                case BoosterType.Undo: d.undo = v; break;
                case BoosterType.Wand: d.wand = v; break;
                case BoosterType.Bottle: d.bottle = v; break;
                case BoosterType.Shuffle: d.shuffle = v; break;
                case BoosterType.Crystal: d.crystal = v; break;
                case BoosterType.Rainbow: d.rainbow = v; break;
            }
        }
    }
}
