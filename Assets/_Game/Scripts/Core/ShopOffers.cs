using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Daily shop allowances (GDD §5): free gift (50 coins once per local day) and coins for rewarded ads
    /// (+60 coins, 5 per day). The UI shows the ad itself and calls <see cref="GrantAdCoins"/> on success.
    /// </summary>
    public static class ShopOffers
    {
        public const int FreeGiftCoins = 50;
        public const int AdCoins = 60;
        public const int AdCoinsPerDay = 5;
        public static event Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        // Daily allowances only reset when a NEW local day starts (forward-only, like DailyRewards): flipping the device
        // time zone back and forth must not hand out a gift / ad slots on every flip.
        public static bool FreeGiftAvailable => TimeUtil.Today > SaveSystem.Data.freeGiftDay;

        /// <summary>Grants the daily free gift. Returns it, or an empty reward (amount 0) if already claimed today.</summary>
        public static Reward ClaimFreeGift()
        {
            if (!FreeGiftAvailable) return default;
            SaveSystem.Data.freeGiftDay = TimeUtil.Today;
            var reward = Reward.Coins(FreeGiftCoins);
            reward.Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return reward;
        }

        public static int AdCoinsLeft
        {
            get
            {
                var d = SaveSystem.Data;
                return TimeUtil.Today <= d.shopAdsDay ? Math.Max(0, AdCoinsPerDay - d.shopAdsUsed) : AdCoinsPerDay;
            }
        }

        /// <summary>Call after a successful rewarded ad. Returns the reward, or an empty one when the daily cap is reached.</summary>
        public static Reward GrantAdCoins()
        {
            if (AdCoinsLeft <= 0) return default;
            var d = SaveSystem.Data;
            int today = TimeUtil.Today;
            if (today > d.shopAdsDay)
            {
                d.shopAdsDay = today;
                d.shopAdsUsed = 0;
            }
            d.shopAdsUsed++;
            var reward = Reward.Coins(AdCoins);
            reward.Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return reward;
        }

        /// <summary>Claimables for the Shop tab badge.</summary>
        public static int ClaimableCount => FreeGiftAvailable ? 1 : 0;
    }
}
