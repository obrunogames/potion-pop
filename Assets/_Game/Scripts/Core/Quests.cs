using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop
{
    public enum QuestKind { WinLevels, CompleteBottles, ReachCombo, UseBoosters, CollectStars, WinHard, WinStreak2, WinThreeStars }

    public sealed class QuestView
    {
        public int index;
        public QuestKind kind;
        public int target;
        public int progress;
        public bool claimed;
        public Reward reward;
        public bool IsComplete => progress >= target;
        public string TitleKey => "quest." + kind.ToString().ToLowerInvariant();   // e.g. "Win {0} levels"
        /// <summary>Value for {0} in the title (usually the target; 2 for "win 2 levels in a row").</summary>
        public int TitleArg => Quests.TitleArgOf(kind, target);
        /// <summary>Localized title, e.g. "Win 3 levels".</summary>
        public string Title => Loc.T(TitleKey, TitleArg);
        /// <summary>Claimable now (complete and not claimed yet).</summary>
        public bool CanClaim => IsComplete && !claimed;
        public float Progress01 => target <= 0 ? 1f : Mathf.Clamp01(progress / (float)target);
    }

    /// <summary>
    /// Daily quests (GDD §5): 3 per local day, picked deterministically from the day index (same set on every device
    /// for players at a similar stage), progress reported by <see cref="Progress"/>, claimable when complete.
    /// </summary>
    public static class Quests
    {
        public const int PerDay = 3;
        public static event Action OnChanged;
        /// <summary>Raised with the quest index when a quest becomes complete (for toasts/badges).</summary>
        public static event Action<int> OnQuestCompleted;

        struct Def
        {
            public int target;
            public Reward reward;
            public Def(int target, Reward reward) { this.target = target; this.reward = reward; }
        }

        // Indexed by (int)QuestKind.
        static readonly Def[] Defs =
        {
            new Def(3, Reward.Coins(80)),                         // WinLevels
            new Def(25, Reward.Coins(60)),                        // CompleteBottles
            new Def(3, Reward.Booster(BoosterType.Bottle, 1)),    // ReachCombo (x3: 3 bottles corked in a row)
            new Def(2, Reward.Coins(60)),                         // UseBoosters
            new Def(9, Reward.Booster(BoosterType.Undo, 1)),      // CollectStars
            new Def(1, Reward.Coins(100)),                        // WinHard
            new Def(1, Reward.Booster(BoosterType.Shuffle, 1)),   // WinStreak2
            new Def(2, Reward.Booster(BoosterType.Wand, 1)),      // WinThreeStars
        };

        const int KindCount = 8;
        /// <summary>First level that can be hard (GDD §3).</summary>
        const int FirstHardLevel = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnChanged = null;
            OnQuestCompleted = null;
        }

        public static IReadOnlyList<QuestView> Today()
        {
            Ensure();
            var list = SaveSystem.Data.quests;
            var views = new List<QuestView>(list.Count);
            for (int i = 0; i < list.Count; i++) views.Add(ToView(i, list[i]));
            return views;
        }

        /// <summary>value is used by ReachCombo (combo reached).</summary>
        public static void Report(QuestKind kind, int amount = 1, int value = 0)
        {
            Ensure();
            var list = SaveSystem.Data.quests;
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var q = list[i];
                if (q.kind != (int)kind || q.claimed || q.progress >= q.target) continue;
                int p = q.progress;
                switch (kind)
                {
                    case QuestKind.ReachCombo:
                        p = Math.Max(p, value);
                        break;
                    default:
                        if (amount > 0) p += amount;
                        break;
                }
                p = Math.Min(p, q.target);
                if (p == q.progress) continue;
                q.progress = p;
                changed = true;
                if (p >= q.target) OnQuestCompleted?.Invoke(i);
            }
            if (!changed) return;
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
        }

        public static int ClaimableCount
        {
            get
            {
                Ensure();
                int n = 0;
                foreach (var q in SaveSystem.Data.quests)
                    if (!q.claimed && q.progress >= q.target) n++;
                return n;
            }
        }

        /// <summary>Grants the quest reward. Returns it, or an empty reward (amount 0) if not claimable.</summary>
        public static Reward Claim(int index)
        {
            Ensure();
            var list = SaveSystem.Data.quests;
            if (index < 0 || index >= list.Count) return default;
            var q = list[index];
            if (q.claimed || q.progress < q.target) return default;
            q.claimed = true;
            var reward = RewardOf((QuestKind)q.kind);
            reward.Grant();
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return reward;
        }

        /// <summary>Seconds until today's quests are replaced.</summary>
        public static long SecondsToReset => TimeUtil.SecondsToMidnight;

        // ------------------------------------------------------------------ definitions

        public static int TargetOf(QuestKind kind) => Defs[Mathf.Clamp((int)kind, 0, KindCount - 1)].target;
        public static Reward RewardOf(QuestKind kind) => Defs[Mathf.Clamp((int)kind, 0, KindCount - 1)].reward;

        public static int TitleArgOf(QuestKind kind, int target)
        {
            switch (kind)
            {
                case QuestKind.WinStreak2: return 2;
                default: return target;
            }
        }

        /// <summary>
        /// The 3 quest kinds of a day: a date-seeded shuffle of the 8 kinds, skipping kinds the player cannot do yet
        /// (no boosters before level 3, no hard levels before level 10). Pure function of (day, playerLevel).
        /// </summary>
        public static QuestKind[] KindsForDay(int day, int playerLevel)
        {
            var order = new int[KindCount];
            for (int i = 0; i < KindCount; i++) order[i] = i;
            uint h = CoreRandom.Hash(unchecked((uint)day * 2654435761u) ^ 0x51F15EEDu);
            for (int i = KindCount - 1; i > 0; i--)
            {
                h = CoreRandom.Hash(unchecked(h + (uint)i));
                int j = (int)(h % (uint)(i + 1));
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            var result = new QuestKind[PerDay];
            int n = 0;
            for (int i = 0; i < KindCount && n < PerDay; i++)
                if (IsEligible((QuestKind)order[i], playerLevel)) result[n++] = (QuestKind)order[i];
            for (int i = 0; i < KindCount && n < PerDay; i++)      // safety net (cannot happen with 6+ eligible kinds)
                if (Array.IndexOf(result, (QuestKind)order[i], 0, n) < 0) result[n++] = (QuestKind)order[i];
            return result;
        }

        static bool IsEligible(QuestKind kind, int playerLevel)
        {
            switch (kind)
            {
                case QuestKind.UseBoosters: return playerLevel >= Economy.BoosterUnlockLevel(BoosterType.Undo);
                case QuestKind.WinHard: return playerLevel >= FirstHardLevel;
                default: return true;
            }
        }

        // ------------------------------------------------------------------ internals

        /// <summary>Generates today's quests when a new local day starts (or the set is missing / invalid).</summary>
        /// <remarks>Forward-only (like DailyRewards): a device time zone flipped back and forth must not regenerate a
        /// fresh claimable set on every flip.</remarks>
        static void Ensure()
        {
            var d = SaveSystem.Data;
            int today = TimeUtil.Today;
            if (d.quests.Count == PerDay && today <= d.questDay) return;
            d.questDay = Math.Max(d.questDay, today);
            d.quests.Clear();
            foreach (var kind in KindsForDay(today, d.level))
                d.quests.Add(new QuestState { kind = (int)kind, target = TargetOf(kind), progress = 0, claimed = false });
            SaveSystem.MarkDirtyHousekeeping();   // automatic reset: must not count as fresh progress for the cloud merge
            OnChanged?.Invoke();
        }

        static QuestView ToView(int index, QuestState q) => new QuestView
        {
            index = index,
            kind = (QuestKind)q.kind,
            target = q.target,
            progress = Math.Min(q.progress, q.target),
            claimed = q.claimed,
            reward = RewardOf((QuestKind)q.kind),
        };
    }
}
