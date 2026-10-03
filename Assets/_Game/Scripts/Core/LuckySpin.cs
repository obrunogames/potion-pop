using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Lucky spin (GDD §5): 1 free spin per local day + 2 more via rewarded ads. 8 weighted segments, listed clockwise
    /// in wheel order starting at the pointer.
    /// </summary>
    public static class LuckySpin
    {
        public const int SegmentCount = 8;
        public const int AdSpinsPerDay = 2;
        public static event Action OnChanged;

        static readonly Reward[] Rewards =
        {
            Reward.Coins(25),
            Reward.Booster(BoosterType.Undo, 1),
            Reward.Coins(50),
            Reward.Booster(BoosterType.Bottle, 1),
            Reward.Coins(100),
            Reward.Booster(BoosterType.Shuffle, 1),
            Reward.Booster(BoosterType.Wand, 1),
            Reward.Coins(250),
        };

        static readonly int[] Weights = { 30, 12, 22, 12, 10, 8, 5, 1 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        public static bool HasFreeSpin
        {
            get
            {
                EnsureDay();
                return !SaveSystem.Data.freeSpinUsed;
            }
        }

        public static int AdSpinsLeft
        {
            get
            {
                EnsureDay();
                return Math.Max(0, AdSpinsPerDay - SaveSystem.Data.adSpinsUsed);
            }
        }

        /// <summary>Any spin left today (free or via ad)?</summary>
        public static bool CanSpin => HasFreeSpin || AdSpinsLeft > 0;

        public static Reward SegmentReward(int index) => index >= 0 && index < Rewards.Length ? Rewards[index] : Reward.Coins(25);

        public static int SegmentWeight(int index) => index >= 0 && index < Weights.Length ? Weights[index] : 0;

        public static int TotalWeight
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Weights.Length; i++) sum += Weights[i];
                return sum;
            }
        }

        /// <summary>Deterministic weighted pick: maps roll01 ∈ [0,1) onto the cumulative weights.</summary>
        public static int PickSegment(float roll01)
        {
            int total = TotalWeight;
            int target = (int)Math.Floor(Mathf.Clamp01(roll01) * total);
            if (target >= total) target = total - 1;
            int acc = 0;
            for (int i = 0; i < Weights.Length; i++)
            {
                acc += Weights[i];
                if (target < acc) return i;
            }
            return Weights.Length - 1;
        }

        /// <summary>
        /// Picks a weighted segment, grants its reward, consumes the spin. Returns the segment index, or -1 when no
        /// spin of that kind is left today.
        /// </summary>
        public static int Spin(bool viaAd)
        {
            EnsureDay();
            var d = SaveSystem.Data;
            if (viaAd)
            {
                if (d.adSpinsUsed >= AdSpinsPerDay) return -1;
                d.adSpinsUsed++;
            }
            else
            {
                if (d.freeSpinUsed) return -1;
                d.freeSpinUsed = true;
            }
            int index = PickSegment(CoreRandom.Value());
            Rewards[index].Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return index;
        }

        /// <summary>Resets the daily allowance when a new local day starts.</summary>
        /// <remarks>Forward-only (like DailyRewards): switching the device time zone back and forth flips the local day,
        /// and resetting on any change would hand out free spins on every flip.</remarks>
        static void EnsureDay()
        {
            var d = SaveSystem.Data;
            int today = TimeUtil.Today;
            if (today <= d.spinDay) return;
            d.spinDay = today;
            d.freeSpinUsed = false;
            d.adSpinsUsed = 0;
            SaveSystem.MarkDirtyHousekeeping();   // automatic reset: must not count as fresh progress for the cloud merge
        }
    }
}
