using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>Star chest (GDD §5): every 30 stars (levels give 1–3) → 100 coins + 2 random (unlocked) boosters.</summary>
    public static class StarChest
    {
        public const int Goal = 30;
        public const int Coins = 100;
        public const int BoosterCount = 2;
        public static event Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        /// <summary>Progress towards the next chest, 0..Goal.</summary>
        public static int Progress => Math.Min(Goal, SaveSystem.Data.starChestProgress);

        public static float Progress01 => Progress / (float)Goal;

        public static bool CanOpen => SaveSystem.Data.starChestProgress >= Goal;

        /// <summary>How many chests are waiting (stars keep counting past the goal).</summary>
        public static int ChestsReady => SaveSystem.Data.starChestProgress / Goal;

        public static Reward[] Open()
        {
            if (!CanOpen) return Array.Empty<Reward>();
            var d = SaveSystem.Data;
            d.starChestProgress -= Goal;

            // Random boosters among the unlocked ones (all of them if none is unlocked yet).
            var pool = new System.Collections.Generic.List<BoosterType>(Economy.AllBoosters.Length);
            foreach (var b in Economy.AllBoosters)
                if (Economy.IsBoosterUnlocked(b)) pool.Add(b);
            if (pool.Count == 0) pool.AddRange(Economy.AllBoosters);

            BoosterType first = pool[CoreRandom.Range(0, pool.Count)];
            BoosterType second = pool[CoreRandom.Range(0, pool.Count)];
            Reward[] rewards = first == second
                ? new[] { Reward.Coins(Coins), Reward.Booster(first, BoosterCount) }
                : new[] { Reward.Coins(Coins), Reward.Booster(first, 1), Reward.Booster(second, 1) };

            for (int i = 0; i < rewards.Length; i++) rewards[i].Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return rewards;
        }

        internal static void RaiseChanged() => OnChanged?.Invoke();
    }
}
