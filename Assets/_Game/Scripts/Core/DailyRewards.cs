using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// 7-day login calendar (GDD §5): one claim per local day; missing a day resets the cycle to day 1;
    /// after day 7 (the chest) it starts over.
    /// </summary>
    public static class DailyRewards
    {
        public const int CycleLength = 7;
        public static event Action OnChanged;

        static readonly Reward[][] Days =
        {
            new[] { Reward.Coins(50) },
            new[] { Reward.Booster(BoosterType.Undo, 1) },
            new[] { Reward.Coins(100) },
            new[] { Reward.Booster(BoosterType.Bottle, 1), Reward.Booster(BoosterType.Shuffle, 1) },
            new[] { Reward.Coins(150) },
            new[] { Reward.Booster(BoosterType.Wand, 2) },
            new[]
            {
                Reward.Coins(300), Reward.Booster(BoosterType.Undo, 1), Reward.Booster(BoosterType.Wand, 1),
                Reward.Booster(BoosterType.Bottle, 1), Reward.Booster(BoosterType.Shuffle, 1),
            },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        /// <summary>0..6, the day that can be (or was last) claimed.</summary>
        public static int CurrentDay => CanClaim ? NextClaimDay : (SaveSystem.Data.dailyDay + CycleLength - 1) % CycleLength;

        public static bool CanClaim => TimeUtil.Today > SaveSystem.Data.dailyLastClaimDay;

        /// <summary>Was this day of the current cycle already claimed? (calendar check marks)</summary>
        public static bool IsDayClaimed(int day) => CanClaim ? day < NextClaimDay : day <= CurrentDay;

        /// <summary>Day 7 is the chest (big reward).</summary>
        public static bool IsChestDay(int day) => day == CycleLength - 1;

        /// <summary>Seconds until the next claim becomes available (0 when claimable now).</summary>
        public static long SecondsToNextClaim => CanClaim ? 0 : TimeUtil.SecondsToMidnight;

        public static Reward[] DayRewards(int day)
        {
            if (day < 0 || day >= Days.Length) return Array.Empty<Reward>();
            return (Reward[])Days[day].Clone();
        }

        /// <summary>Grants today's rewards and advances the cycle. Returns what was granted.</summary>
        public static Reward[] Claim()
        {
            if (!CanClaim) return Array.Empty<Reward>();
            int day = NextClaimDay;
            var rewards = DayRewards(day);
            var d = SaveSystem.Data;
            d.dailyDay = (day + 1) % CycleLength;
            d.dailyLastClaimDay = TimeUtil.Today;
            for (int i = 0; i < rewards.Length; i++) rewards[i].Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return rewards;
        }

        /// <summary>Day that a claim today would grant: the saved next day, or day 0 when a day was missed.</summary>
        static int NextClaimDay
        {
            get
            {
                var d = SaveSystem.Data;
                int last = d.dailyLastClaimDay;
                bool missed = last >= 0 && TimeUtil.Today - last > 1;
                int day = missed ? 0 : d.dailyDay;
                return day < 0 || day >= CycleLength ? 0 : day;
            }
        }
    }
}
