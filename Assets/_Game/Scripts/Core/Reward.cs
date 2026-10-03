using System;

namespace PotionPop
{
    public enum RewardType { Coins, Booster, Hearts, InfiniteHeartsMinutes, Stars, Card }

    /// <summary>A grantable reward (daily calendar, spin, chest, quests, albums...).</summary>
    [Serializable]
    public struct Reward
    {
        public RewardType type;
        public int amount;
        public BoosterType booster;
        public string cardId;

        public static Reward Coins(int n) => new Reward { type = RewardType.Coins, amount = n };
        public static Reward Booster(BoosterType b, int n) => new Reward { type = RewardType.Booster, booster = b, amount = n };
        public static Reward Hearts(int n) => new Reward { type = RewardType.Hearts, amount = n };
        public static Reward InfiniteHearts(int minutes) => new Reward { type = RewardType.InfiniteHeartsMinutes, amount = minutes };
        public static Reward Card(string productId) => new Reward { type = RewardType.Card, cardId = productId, amount = 1 };
        public static Reward Stars(int n) => new Reward { type = RewardType.Stars, amount = n };

        /// <summary>True for a default/empty reward (e.g. a failed claim).</summary>
        public bool IsEmpty => amount <= 0;

        /// <summary>Applies the reward to the save (coins, boosters, hearts...). Marks the save dirty.</summary>
        public void Grant()
        {
            if (amount <= 0) return;
            switch (type)
            {
                case RewardType.Coins: Economy.AddCoins(amount, "reward"); break;
                case RewardType.Booster: Economy.AddBooster(booster, amount); break;
                case RewardType.Hearts: Lives.Add(amount); break;
                case RewardType.InfiniteHeartsMinutes: Lives.AddInfinite(amount * 60); break;
                case RewardType.Stars: Economy.AddStars(amount); break;
                case RewardType.Card:
                    if (!string.IsNullOrEmpty(cardId)) Collection.Grant(cardId, out _);
                    break;
            }
        }

        /// <summary>Sprite name for UI: icon_coin, booster_undo, icon_heart, p_&lt;card&gt;...</summary>
        public string IconSprite
        {
            get
            {
                switch (type)
                {
                    case RewardType.Coins: return "icon_coin";
                    case RewardType.Booster: return Economy.BoosterSprite(booster);
                    case RewardType.Hearts: return "icon_heart";
                    case RewardType.InfiniteHeartsMinutes: return "icon_heart";
                    case RewardType.Stars: return "icon_star";
                    case RewardType.Card: return "p_" + cardId;
                    default: return "icon_gift";
                }
            }
        }

        /// <summary>"x3", "250", "30m"...</summary>
        public string AmountText
        {
            get
            {
                switch (type)
                {
                    case RewardType.Coins:
                    case RewardType.Stars:
                        return Loc.Number(amount);
                    case RewardType.InfiniteHeartsMinutes:
                        return FormatMinutes(amount);
                    default:
                        return Loc.T("reward.amount.count", amount);
                }
            }
        }

        /// <summary>Localization key of the reward's name ("Coins", "Hammer", product name...).</summary>
        public string NameKey
        {
            get
            {
                switch (type)
                {
                    case RewardType.Coins: return "reward.coins";
                    case RewardType.Booster: return Economy.BoosterNameKey(booster);
                    case RewardType.Hearts: return "reward.hearts";
                    case RewardType.InfiniteHeartsMinutes: return "reward.infinite_hearts";
                    case RewardType.Stars: return "reward.stars";
                    case RewardType.Card: return Catalog.ProductNameKey(cardId);
                    default: return "reward.coins";
                }
            }
        }

        public override string ToString() => type + ":" + (type == RewardType.Booster ? booster + "x" : "") + amount +
                                             (type == RewardType.Card ? "(" + cardId + ")" : "");

        static string FormatMinutes(int minutes)
        {
            if (minutes < 60) return Loc.T("reward.amount.minutes", minutes);
            int h = minutes / 60, m = minutes % 60;
            return m == 0 ? Loc.T("reward.amount.hours", h) : Loc.T("reward.amount.hours_minutes", h, m);
        }
    }
}
