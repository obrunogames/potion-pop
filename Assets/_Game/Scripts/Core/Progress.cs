using System;
using UnityEngine;

namespace PotionPop
{
    public sealed class LevelResult
    {
        public int level;
        public bool hard;
        /// <summary>Replay of an already-won level (Worlds screen): no streak, no card, coins only for new stars.</summary>
        public bool replay;
        public int moves;
        public int coins;              // coins granted (before optional x2 ad)
        /// <summary>Star rating of this win (1..3).</summary>
        public int stars;
        /// <summary>Best rating before this win (0 = first win).</summary>
        public int previousBest;
        /// <summary>Stars added to the totals (new best − previous best, ≥ 0).</summary>
        public int starsGained;
        public string cardId;          // collection card granted (first wins only)
        public bool cardDuplicate;
        public int duplicateCoins;
        public bool areaChanged;       // the next level starts a new world
        public int newAreaNumber;
        public bool starChestReady;
        public int winStreak;
    }

    /// <summary>Player progression; gameplay reports events here (it updates quests, stats, save).</summary>
    public static class Progress
    {
        public static event Action<int> OnLevelChanged;
        /// <summary>A level's best star rating changed (level, new best).</summary>
        public static event Action<int, int> OnLevelStarsChanged;

        public const int WinCoins = 20;
        public const int WinCoinsHard = 40;
        /// <summary>Coins per newly earned star when replaying a level.</summary>
        public const int ReplayCoinsPerStar = 10;
        public const int DuplicateCardCoins = 10;

        static readonly BoosterType[] NoBonus = new BoosterType[0];
        static readonly BoosterType[] BonusRainbow = { BoosterType.Rainbow };
        static readonly BoosterType[] BonusRainbowCrystal = { BoosterType.Rainbow, BoosterType.Crystal };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnLevelChanged = null;
            OnLevelStarsChanged = null;
        }

        /// <summary>Next level to play (1-based).</summary>
        public static int CurrentLevel => SaveSystem.Data.level;
        public static int WinStreak => SaveSystem.Data.winStreak;
        public static int BestStreak => SaveSystem.Data.bestStreak;

        /// <summary>
        /// Free pre-boosters granted by the current win streak (GDD §4): streak ≥ 3 → Rainbow, ≥ 6 → Rainbow + Crystal.
        /// Boosters the player has not unlocked yet are left out. They do not consume the inventory.
        /// </summary>
        public static BoosterType[] StreakBonuses
        {
            get
            {
                int streak = WinStreak;
                var set = streak >= 6 ? BonusRainbowCrystal : streak >= 3 ? BonusRainbow : NoBonus;
                int unlocked = 0;
                for (int i = 0; i < set.Length; i++)
                    if (Economy.IsBoosterUnlocked(set[i])) unlocked++;
                if (unlocked == set.Length) return (BoosterType[])set.Clone();
                var result = new BoosterType[unlocked];
                for (int i = 0, n = 0; i < set.Length; i++)
                    if (Economy.IsBoosterUnlocked(set[i])) result[n++] = set[i];
                return result;
            }
        }

        // ------------------------------------------------------------------ stars per level

        /// <summary>Best star rating of a level (0 = never won).</summary>
        public static int BestStars(int level)
        {
            var s = SaveSystem.Data.levelStars;
            int i = level - 1;
            if (string.IsNullOrEmpty(s) || i < 0 || i >= s.Length) return 0;
            int v = s[i] - '0';
            return v < 0 || v > 3 ? 0 : v;
        }

        /// <summary>Whether a level was won at least once.</summary>
        public static bool IsLevelWon(int level) => BestStars(level) > 0 || level < CurrentLevel;

        /// <summary>Sum of the best stars of levels [first, last].</summary>
        public static int StarsInRange(int first, int last)
        {
            int n = 0;
            for (int l = Math.Max(1, first); l <= last; l++) n += BestStars(l);
            return n;
        }

        /// <summary>Stores a better rating; returns the stars gained (0 when not better).</summary>
        static int RecordStars(int level, int stars)
        {
            stars = Math.Max(1, Math.Min(3, stars));
            int old = BestStars(level);
            if (stars <= old) return 0;
            var d = SaveSystem.Data;
            var chars = (d.levelStars ?? "").ToCharArray();
            int i = level - 1;
            if (chars.Length <= i)
            {
                var grown = new char[i + 1];
                for (int k = 0; k < grown.Length; k++) grown[k] = k < chars.Length ? chars[k] : '0';
                chars = grown;
            }
            chars[i] = (char)('0' + stars);
            d.levelStars = new string(chars);
            OnLevelStarsChanged?.Invoke(level, stars);
            return stars - old;
        }

        // ------------------------------------------------------------------ attempts

        /// <summary>
        /// A level attempt starts (after the Level Start popup). Resets the per-level continue pricing and remembers
        /// the attempt (written immediately) so a level abandoned by killing the app can be counted as a loss on the
        /// next launch, see <see cref="ResolveAbandonedLevel"/>.
        /// </summary>
        public static void ReportLevelStart(int level)
        {
            var d = SaveSystem.Data;
            d.continuesThisLevel = 0;
            d.levelInProgress = Math.Max(1, level);
            SaveSystem.MarkDirty();
            SaveSystem.Flush();   // must survive the app being killed mid-level
        }

        /// <summary>Level that was started but never reported as won or lost (app killed mid-level); 0 when none.</summary>
        public static int AbandonedLevel => SaveSystem.Data.levelInProgress;

        /// <summary>
        /// GDD §5 "lose 1 heart on fail/quit": counts an abandoned attempt as a loss (heart + streak). Call once at
        /// startup (e.g. when Home first shows), never while a level is being played. Returns true if one was resolved.
        /// </summary>
        public static bool ResolveAbandonedLevel()
        {
            int level = SaveSystem.Data.levelInProgress;
            if (level <= 0) return false;
            ReportFail(level);
            return true;
        }

        /// <summary>A bottle was completed (corked). combo = bottles completed in a row (1 = no chain).</summary>
        public static void ReportBottleCompleted(int combo)
        {
            var d = SaveSystem.Data;
            d.totalBottles++;
            if (combo > d.maxCombo) d.maxCombo = combo;
            SaveSystem.MarkDirty();
            Quests.Report(QuestKind.CompleteBottles, 1);
            if (combo > 1) Quests.Report(QuestKind.ReachCombo, 1, combo);
        }

        /// <summary>A pour was made (stats).</summary>
        public static void ReportPour()
        {
            SaveSystem.Data.totalPours++;
            SaveSystem.MarkDirtyHousekeeping();
        }

        public static void ReportBoosterUsed(BoosterType t)
        {
            Quests.Report(QuestKind.UseBoosters, 1);
        }

        /// <summary>
        /// Level won with a star rating (1..3) after <paramref name="moves"/> pours. First wins advance the level,
        /// streak, coins, card and quests; replays (Worlds screen) only pay for newly earned stars. Saves.
        /// </summary>
        public static LevelResult ReportWin(int level, bool hard, int stars, int moves, int maxCombo, bool replay = false)
        {
            var d = SaveSystem.Data;
            level = Math.Max(1, level);
            stars = Math.Max(1, Math.Min(3, stars));
            replay = replay && level < d.level;   // only a level already passed can be a replay
            var result = new LevelResult { level = level, hard = hard, stars = stars, moves = moves, replay = replay };

            int areaBefore = Areas.AreaNumberForLevel(level);
            int oldLevel = d.level;
            result.previousBest = BestStars(level);
            result.starsGained = RecordStars(level, stars);
            if (maxCombo > d.maxCombo) d.maxCombo = maxCombo;
            d.continuesThisLevel = 0;
            d.levelInProgress = 0;
            d.levelsWon++;

            if (replay)
            {
                d.replaysWon++;
                result.coins = result.starsGained * ReplayCoinsPerStar;
                if (result.coins > 0) Economy.AddCoins(result.coins, "level_replay");
                result.winStreak = d.winStreak;
            }
            else
            {
                if (level + 1 > d.level) d.level = level + 1;
                d.winStreak++;
                if (d.winStreak > d.bestStreak) d.bestStreak = d.winStreak;
                if (hard) d.hardLevelsWon++;
                result.winStreak = d.winStreak;
                result.coins = hard ? WinCoinsHard : WinCoins;
                Economy.AddCoins(result.coins, "level_win");
                // collection card (unlocked worlds already include the world of the next level)
                result.cardId = Collection.GrantRandomCard(out result.cardDuplicate);
                result.duplicateCoins = result.cardDuplicate ? DuplicateCardCoins : 0;
                if (hard) Quests.Report(QuestKind.WinHard, 1);
                if (d.winStreak >= 2) Quests.Report(QuestKind.WinStreak2, 1, d.winStreak);
            }
            Economy.AddStars(result.starsGained);

            // daily quests
            Quests.Report(QuestKind.WinLevels, 1);
            if (stars >= 3) Quests.Report(QuestKind.WinThreeStars, 1);
            if (result.starsGained > 0) Quests.Report(QuestKind.CollectStars, result.starsGained);
            if (maxCombo > 1) Quests.Report(QuestKind.ReachCombo, 1, maxCombo);

            // world & chest
            int areaAfter = Areas.AreaNumberForLevel(d.level);
            result.areaChanged = !replay && Areas.AreaNumberForLevel(level + 1) != areaBefore;
            result.newAreaNumber = Areas.AreaNumberForLevel(level + 1);
            result.starChestReady = StarChest.CanOpen;

            SaveSystem.MarkDirty();
            SaveSystem.Flush();   // level end: write now, not debounced
            if (d.level != oldLevel) OnLevelChanged?.Invoke(d.level);
            return result;
        }

        /// <summary>Level lost or abandoned: consumes a heart, resets streak; saves.</summary>
        public static void ReportFail(int level)
        {
            var d = SaveSystem.Data;
            Lives.TryConsume();
            d.winStreak = 0;
            d.levelsLost++;
            d.continuesThisLevel = 0;
            d.levelInProgress = 0;
            SaveSystem.MarkDirty();
            SaveSystem.Flush();   // level end: write now, not debounced
        }

        /// <summary>Debug: jump to a level.</summary>
        public static void SetLevel(int level)
        {
            var d = SaveSystem.Data;
            level = Math.Max(1, level);
            if (d.level == level) return;
            d.level = level;
            SaveSystem.MarkDirty();
            OnLevelChanged?.Invoke(level);
        }

        // ------------------------------------------------------------------ continues (GDD §5)

        /// <summary>Coin price of the next continue (+1 empty bottle when stuck) in the current level: 300, 600, then 900.</summary>
        public static int ContinuePrice => 300 * Mathf.Clamp(SaveSystem.Data.continuesThisLevel + 1, 1, 3);

        /// <summary>Pays <see cref="ContinuePrice"/> and counts the continue. False when the player can't afford it.</summary>
        public static bool TryBuyContinue()
        {
            if (!Economy.TrySpendCoins(ContinuePrice, "continue")) return false;
            SaveSystem.Data.continuesThisLevel++;
            SaveSystem.MarkDirty();
            return true;
        }

        // ------------------------------------------------------------------ areas

        /// <summary>Area number of the next level to play.</summary>
        public static int CurrentAreaNumber => Areas.AreaNumberForLevel(CurrentLevel);

        /// <summary>True when the player reached an area whose "Area Unlocked" popup wasn't shown yet.</summary>
        public static bool HasUnseenArea => CurrentAreaNumber > SaveSystem.Data.lastSeenArea;

        /// <summary>Marks the current area as presented (call after the "Area Unlocked" popup).</summary>
        public static void MarkAreaSeen()
        {
            var d = SaveSystem.Data;
            int area = CurrentAreaNumber;
            if (d.lastSeenArea == area) return;
            d.lastSeenArea = area;
            SaveSystem.MarkDirty();
        }

        // ------------------------------------------------------------------ tutorials

        public static bool HasSeenTutorial(string id) => !string.IsNullOrEmpty(id) && SaveSystem.Data.seenTutorials.Contains(id);

        public static void MarkTutorialSeen(string id)
        {
            if (string.IsNullOrEmpty(id) || HasSeenTutorial(id)) return;
            SaveSystem.Data.seenTutorials.Add(id);
            SaveSystem.MarkDirty();
        }
    }
}
