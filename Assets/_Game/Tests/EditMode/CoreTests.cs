using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PotionPop.Tests
{
    /// <summary>
    /// EditMode tests of the Core module. Every test runs on the in-memory save (SaveSystem.UseMemoryStorage), in UTC,
    /// with a controlled clock and a seeded random source; global state is restored in TearDown.
    /// </summary>
    public class CoreTests
    {
        long _savedOffset;
        TimeZoneInfo _savedZone;

        /// <summary>Wednesday 2026-03-04 12:00:00 UTC (noon keeps day boundaries far from real-clock drift).</summary>
        static readonly long BaseTime = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        [SetUp]
        public void SetUp()
        {
            _savedOffset = TimeUtil.DebugOffsetSeconds;
            _savedZone = TimeUtil.TimeZoneOverride;
            TimeUtil.TimeZoneOverride = TimeZoneInfo.Utc;
            SetNow(BaseTime);
            SaveSystem.UseMemoryStorage(true);
            CoreRandom.Seed(12345);
            Loc.Language = "en";
        }

        [TearDown]
        public void TearDown()
        {
            CoreRandom.Seed(null);
            SaveSystem.UseMemoryStorage(false);
            TimeUtil.DebugOffsetSeconds = _savedOffset;
            TimeUtil.TimeZoneOverride = _savedZone;
            Loc.Reload();   // drops tables added by tests
        }

        static void SetNow(long unix) => TimeUtil.DebugOffsetSeconds = unix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static void Advance(long seconds) => TimeUtil.DebugOffsetSeconds += seconds;
        const long Day = 86400;

        // ============================================================== TimeUtil

        [Test]
        public void TimeUtil_DayAndWeekIndices()
        {
            Assert.AreEqual(0, TimeUtil.DayIndex(0));
            Assert.AreEqual(0, TimeUtil.DayIndex(Day - 1));
            Assert.AreEqual(1, TimeUtil.DayIndex(Day));
            Assert.AreEqual(-1, TimeUtil.DayIndex(-1));

            // 1970-01-05 (day 4) is the first Monday: week 0 starts there.
            Assert.AreEqual(-1, TimeUtil.WeekIndexOf(4 * Day - 1));
            Assert.AreEqual(0, TimeUtil.WeekIndexOf(4 * Day));
            Assert.AreEqual(0, TimeUtil.WeekIndexOf(11 * Day - 1));
            Assert.AreEqual(1, TimeUtil.WeekIndexOf(11 * Day));
            Assert.AreEqual(0, TimeUtil.DayOfWeek(4 * Day));      // Monday
            Assert.AreEqual(2, TimeUtil.DayOfWeek(BaseTime));     // Wednesday

            Assert.AreEqual(TimeUtil.DayIndex(TimeUtil.Now), TimeUtil.Today);
            long toMidnight = TimeUtil.SecondsToMidnight;
            Assert.That(toMidnight, Is.InRange(12 * 3600 - 5, 12 * 3600));
            // Wednesday noon → next Monday 00:00 = 4.5 days.
            Assert.That(TimeUtil.SecondsToNextWeek, Is.InRange(4 * Day + 12 * 3600 - 5, 4 * Day + 12 * 3600));
        }

        [Test]
        public void TimeUtil_DayIndexFollowsTimeZone()
        {
            TimeUtil.TimeZoneOverride = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
            // 01:00 UTC on day 1 is still 22:00 of day 0 at UTC-3.
            Assert.AreEqual(0, TimeUtil.DayIndex(Day + 3600));
            Assert.AreEqual(1, TimeUtil.DayIndex(Day + 3 * 3600));
        }

        [Test]
        public void TimeUtil_DayIndexFollowsDaylightSaving()
        {
            // UTC+0 in winter, UTC+1 from April 1st to October 1st (fixed-date rules).
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 4, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 1));
            TimeUtil.TimeZoneOverride = TimeZoneInfo.CreateCustomTimeZone("TestDST", TimeSpan.Zero, "TestDST", "TestSTD",
                "TestDST", new[] { rule });

            long summer = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            long winter = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            int summerUtcDay = (int)(summer / Day), winterUtcDay = (int)(winter / Day);
            Assert.AreEqual(summerUtcDay + 1, TimeUtil.DayIndex(summer), "23:30 UTC is already 00:30 tomorrow in summer");
            Assert.AreEqual(winterUtcDay, TimeUtil.DayIndex(winter), "but still today in winter");
            Assert.AreEqual(summerUtcDay + 1, TimeUtil.DayIndex(summer), "cached offset is per time bucket");
            Assert.AreEqual(Day - 1800, TimeUtil.SecondsToMidnightFrom(summer));
            Assert.AreEqual(1800, TimeUtil.SecondsToMidnightFrom(winter));
        }

        [Test]
        public void TimeUtil_Formats()
        {
            Assert.AreEqual("05:50", TimeUtil.FormatMMSS(350f));
            Assert.AreEqual("00:00", TimeUtil.FormatMMSS(0f));
            Assert.AreEqual("00:00", TimeUtil.FormatMMSS(-3f));
            Assert.AreEqual("00:01", TimeUtil.FormatMMSS(0.2f));   // countdowns round up
            Assert.AreEqual("62:05", TimeUtil.FormatMMSS(3725f));
            Assert.AreEqual("2d 1h", TimeUtil.FormatDuration(2 * Day + 3600 + 59));
            Assert.AreEqual("10h 4m", TimeUtil.FormatDuration(10 * 3600 + 4 * 60 + 5));
            Assert.AreEqual("22:34", TimeUtil.FormatDuration(22 * 60 + 34));
            Assert.AreEqual("00:00", TimeUtil.FormatDuration(-10));
        }

        // ============================================================== SaveSystem

        [Test]
        public void Save_JsonRoundtrip()
        {
            var d = new PlayerData
            {
                playerId = "1234567", playerName = "Luna", level = 42, coins = 999, totalStars = 123456789012L,
                hearts = 2, nextHeartAt = 1700000000, undo = 7, rainbow = 0, language = "pt", musicOn = false,
                dailyDay = 5, dailyLastClaimDay = 20000, questDay = 20001, weekId = 2900, weeklyStars = 77,
                levelStars = "3213", totalBottles = 345, totalPours = 6789, replaysWon = 4, avatar = "dragon",
            };
            d.cards.Add("glow_mushroom");
            d.cards.Add("firefly_jar");
            d.albumsClaimed.Add("forest");
            d.seenTutorials.Add("undo");
            d.quests.Add(new QuestState { kind = (int)QuestKind.ReachCombo, target = 5, progress = 3, claimed = false });

            var back = SaveSystem.FromJson(SaveSystem.ToJson(d));
            Assert.NotNull(back);
            Assert.AreEqual("1234567", back.playerId);
            Assert.AreEqual("Luna", back.playerName);
            Assert.AreEqual("3213", back.levelStars);
            Assert.AreEqual(345, back.totalBottles);
            Assert.AreEqual(6789, back.totalPours);
            Assert.AreEqual(4, back.replaysWon);
            Assert.AreEqual("dragon", back.avatar);
            Assert.AreEqual(42, back.level);
            Assert.AreEqual(999, back.coins);
            Assert.AreEqual(123456789012L, back.totalStars);
            Assert.AreEqual(2, back.hearts);
            Assert.AreEqual(1700000000, back.nextHeartAt);
            Assert.AreEqual(7, back.undo);
            Assert.AreEqual(0, back.rainbow);
            Assert.AreEqual("pt", back.language);
            Assert.IsFalse(back.musicOn);
            Assert.AreEqual(5, back.dailyDay);
            Assert.AreEqual(20000, back.dailyLastClaimDay);
            CollectionAssert.AreEqual(new[] { "glow_mushroom", "firefly_jar" }, back.cards);
            CollectionAssert.AreEqual(new[] { "forest" }, back.albumsClaimed);
            CollectionAssert.AreEqual(new[] { "undo" }, back.seenTutorials);
            Assert.AreEqual(1, back.quests.Count);
            Assert.AreEqual((int)QuestKind.ReachCombo, back.quests[0].kind);
            Assert.AreEqual(3, back.quests[0].progress);
            Assert.AreEqual(PlayerData.CurrentVersion, back.version);
        }

        [Test]
        public void Save_FromJsonRejectsGarbage()
        {
            Assert.IsNull(SaveSystem.FromJson(null));
            Assert.IsNull(SaveSystem.FromJson(""));
            Assert.IsNull(SaveSystem.FromJson("{not json"));
        }

        [Test]
        public void Save_MigrationAndSanitize()
        {
            var d = SaveSystem.FromJson("{\"version\":0,\"level\":0,\"coins\":-50,\"hearts\":9,\"playerId\":\"12\",\"dailyDay\":11}");
            Assert.NotNull(d);
            Assert.AreEqual(PlayerData.CurrentVersion, d.version);
            Assert.AreEqual(1, d.level);
            Assert.AreEqual(0, d.coins);
            Assert.AreEqual(Lives.Max, d.hearts);
            Assert.AreEqual(0, d.dailyDay);
            Assert.AreEqual("12", d.playerId, "FromJson never invents a player id");
            Assert.NotNull(d.cards);
            Assert.NotNull(d.quests);

            // Restoring such a save keeps the local id instead of a random new one.
            string localId = SaveSystem.Data.playerId;
            SaveSystem.Replace(d);
            Assert.AreEqual(localId, SaveSystem.Data.playerId);
        }

        [Test]
        public void Save_UseMemoryStorageTwiceStartsFresh()
        {
            Economy.AddCoins(100, "test");
            SaveSystem.UseMemoryStorage(true);   // already enabled by SetUp
            Assert.AreEqual(300, Economy.Coins, "re-enabling memory storage starts from an empty save");
            Assert.IsTrue(SaveSystem.IsUsingMemoryStorage);
        }

        [Test]
        public void Save_FirstLaunchGeneratesPlayerIdAndPersists()
        {
            var d = SaveSystem.Data;
            Assert.AreEqual(7, d.playerId.Length);
            Assert.IsTrue(long.TryParse(d.playerId, out _));
            Assert.AreEqual(300, d.coins);
            Assert.AreEqual(5, d.hearts);
            Assert.AreEqual(3, d.undo);
            Assert.AreEqual(1, d.wand);
            Assert.AreEqual(1, d.bottle);
            Assert.AreEqual(2, d.shuffle);
            Assert.AreEqual(1, d.crystal);
            Assert.AreEqual(1, d.rainbow);
            Assert.AreEqual(PlayerProfile.DefaultAvatar, d.avatar, "new players start as Luna");
            Assert.AreEqual("", d.levelStars);
            Assert.IsNotEmpty(SaveSystem.MemoryPrimaryJson, "fresh save is written");

            string id = d.playerId;
            Economy.AddCoins(123, "test");
            SaveSystem.Load();
            Assert.AreEqual(id, SaveSystem.Data.playerId);
            Assert.AreEqual(423, Economy.Coins);
            Assert.Greater(SaveSystem.Data.updatedAt, 0);
        }

        [Test]
        public void Save_CorruptFileFallsBackToBackup()
        {
            Economy.AddCoins(477, "test");
            Assert.AreEqual(777, Economy.Coins);
            SaveSystem.MemoryPrimaryJson = "{\"coins\": 12, broken";
            SaveSystem.Load();
            Assert.AreEqual(777, Economy.Coins);
            Assert.IsNotNull(SaveSystem.FromJson(SaveSystem.MemoryPrimaryJson), "primary is repaired from the backup");
        }

        [Test]
        public void Save_NewerBackupWinsOverStaleFile()
        {
            Economy.AddCoins(100, "test");                       // file + backup: 400 coins
            string stale = SaveSystem.MemoryPrimaryJson;
            Advance(60);
            Economy.AddCoins(100, "test");                       // 500 coins, written later
            SaveSystem.MemoryPrimaryJson = stale;                // simulate a failed file write
            SaveSystem.Load();
            Assert.AreEqual(500, Economy.Coins, "the newer backup is used");
            Assert.AreEqual(SaveSystem.MemoryBackupJson, SaveSystem.MemoryPrimaryJson, "and the file is repaired");
        }

        [Test]
        public void Save_CorruptFileAndBackupStartFresh()
        {
            Economy.AddCoins(477, "test");
            SaveSystem.MemoryPrimaryJson = "garbage";
            SaveSystem.MemoryBackupJson = "more garbage";
            SaveSystem.Load();
            Assert.AreEqual(300, Economy.Coins);
            Assert.AreEqual(7, SaveSystem.Data.playerId.Length);
        }

        [Test]
        public void Save_ChooseMergeRule()
        {
            PlayerData Make(int level, long stars, long updated) => new PlayerData { level = level, totalStars = stars, updatedAt = updated };

            var local = Make(10, 100, 500);
            var cloud = Make(12, 50, 100);
            Assert.AreSame(cloud, SaveSystem.ChooseMerge(local, cloud, out bool tookCloud));
            Assert.IsTrue(tookCloud, "higher level wins");

            cloud = Make(10, 101, 100);
            Assert.AreSame(cloud, SaveSystem.ChooseMerge(local, cloud, out tookCloud));
            Assert.IsTrue(tookCloud, "same level → more stars wins");

            cloud = Make(10, 100, 400);
            Assert.AreSame(local, SaveSystem.ChooseMerge(local, cloud, out tookCloud));
            Assert.IsFalse(tookCloud, "same level & stars → most recent wins (local)");

            cloud = Make(10, 100, 600);
            Assert.AreSame(cloud, SaveSystem.ChooseMerge(local, cloud, out tookCloud));
            Assert.IsTrue(tookCloud, "same level & stars → most recent wins (cloud)");

            cloud = Make(10, 100, 500);
            Assert.AreSame(local, SaveSystem.ChooseMerge(local, cloud, out tookCloud));
            Assert.IsFalse(tookCloud, "full tie keeps local");

            cloud = Make(9, 9999, 9999);
            Assert.AreSame(local, SaveSystem.ChooseMerge(local, cloud, out tookCloud));
            Assert.IsFalse(tookCloud, "lower level loses whatever the stars");

            Assert.AreSame(local, SaveSystem.ChooseMerge(local, null, out tookCloud));
            Assert.IsFalse(tookCloud);
            Assert.AreSame(cloud, SaveSystem.ChooseMerge(null, cloud, out tookCloud));
            Assert.IsTrue(tookCloud);
        }

        [Test]
        public void Save_MergeNeverMixesAccounts()
        {
            // Shared device: account A (level 80) signs out, account B (level 12 in the cloud) signs in.
            var local = new PlayerData { level = 80, totalStars = 900, updatedAt = 900, cloudUid = "A" };
            var cloud = new PlayerData { level = 12, totalStars = 30, updatedAt = 100, cloudUid = "B" };
            Assert.IsTrue(SaveSystem.IsForeignSave(local, "B"));
            Assert.AreSame(cloud, SaveSystem.ChooseMergeForAccount("B", local, cloud, out bool tookCloud));
            Assert.IsTrue(tookCloud, "another account's save never overwrites this account's cloud save");

            // Brand-new account (no cloud document): nothing in the cloud to destroy, the local save is kept.
            Assert.AreSame(local, SaveSystem.ChooseMergeForAccount("B", local, null, out tookCloud));
            Assert.IsFalse(tookCloud);

            // Same account, or a guest save never synced: the GDD §7 rule (higher level wins).
            local.cloudUid = "B";
            Assert.IsFalse(SaveSystem.IsForeignSave(local, "B"));
            Assert.AreSame(local, SaveSystem.ChooseMergeForAccount("B", local, cloud, out tookCloud));
            Assert.IsFalse(tookCloud);
            local.cloudUid = "";
            Assert.AreSame(local, SaveSystem.ChooseMergeForAccount("B", local, cloud, out tookCloud));
            Assert.IsFalse(tookCloud);

            // Old saves without the field load as guest saves.
            var parsed = SaveSystem.FromJson("{\"level\":3}");
            Assert.AreEqual("", parsed.cloudUid);
        }

        [Test]
        public void Save_ReplaceSavesAndNotifies()
        {
            string id = SaveSystem.Data.playerId;
            int replaced = 0;
            Action handler = () => replaced++;
            SaveSystem.OnReplaced += handler;
            try
            {
                var cloud = new PlayerData { level = 42, playerId = "" };
                SaveSystem.Replace(cloud);
                Assert.AreEqual(1, replaced);
                Assert.AreEqual(42, Progress.CurrentLevel);
                Assert.AreEqual(id, SaveSystem.Data.playerId, "a cloud save without id keeps the local id");
                SaveSystem.Load();
                Assert.AreEqual(42, Progress.CurrentLevel, "replacement was written");
            }
            finally { SaveSystem.OnReplaced -= handler; }
        }

        // ============================================================== Economy

        [Test]
        public void Economy_CoinsBoostersAndPacks()
        {
            int lastOld = -1, lastNew = -1;
            Action<int, int> onCoins = (o, n) => { lastOld = o; lastNew = n; };
            Economy.OnCoinsChanged += onCoins;
            try
            {
                Economy.AddCoins(200, "test");
                Assert.AreEqual(300, lastOld);
                Assert.AreEqual(500, lastNew);
                Assert.IsFalse(Economy.TrySpendCoins(501, "test"));
                Assert.AreEqual(500, Economy.Coins);

                Assert.IsTrue(Economy.TryBuyBoosterPack(BoosterType.Wand));   // 3 for 400
                Assert.AreEqual(100, Economy.Coins);
                Assert.AreEqual(4, Economy.GetBooster(BoosterType.Wand));
                Assert.IsFalse(Economy.TryBuyBoosterPack(BoosterType.Undo)); // 200 > 100
                Assert.AreEqual(3, Economy.GetBooster(BoosterType.Undo));
            }
            finally { Economy.OnCoinsChanged -= onCoins; }

            Assert.IsTrue(Economy.TryUseBooster(BoosterType.Rainbow));
            Assert.IsFalse(Economy.TryUseBooster(BoosterType.Rainbow));

            // GDD §5: in-game Undo 3 · Shuffle 6 · Extra Bottle 8 · Magic Wand 12; pre-level Rainbow 10 · Crystal 15.
            Assert.AreEqual(3, Economy.BoosterUnlockLevel(BoosterType.Undo));
            Assert.AreEqual(6, Economy.BoosterUnlockLevel(BoosterType.Shuffle));
            Assert.AreEqual(8, Economy.BoosterUnlockLevel(BoosterType.Bottle));
            Assert.AreEqual(12, Economy.BoosterUnlockLevel(BoosterType.Wand));
            Assert.AreEqual(10, Economy.BoosterUnlockLevel(BoosterType.Rainbow));
            Assert.AreEqual(15, Economy.BoosterUnlockLevel(BoosterType.Crystal));
            Assert.AreEqual((3, 200), Economy.BoosterPack(BoosterType.Undo));
            Assert.AreEqual((3, 250), Economy.BoosterPack(BoosterType.Shuffle));
            Assert.AreEqual((3, 350), Economy.BoosterPack(BoosterType.Bottle));
            Assert.AreEqual((3, 400), Economy.BoosterPack(BoosterType.Wand));
            Assert.AreEqual((3, 300), Economy.BoosterPack(BoosterType.Rainbow));
            Assert.AreEqual((3, 250), Economy.BoosterPack(BoosterType.Crystal));

            CollectionAssert.AreEqual(new[] { BoosterType.Undo, BoosterType.Shuffle, BoosterType.Bottle, BoosterType.Wand }, Economy.InGameBoosters);
            CollectionAssert.AreEqual(new[] { BoosterType.Rainbow, BoosterType.Crystal }, Economy.PreLevelBoosters);
            Assert.IsTrue(Economy.IsPreLevel(BoosterType.Crystal));
            Assert.IsFalse(Economy.IsPreLevel(BoosterType.Undo));
            Assert.AreEqual("booster_bottle", Economy.BoosterSprite(BoosterType.Bottle));
            Assert.AreEqual("booster.rainbow.desc", Economy.BoosterDescKey(BoosterType.Rainbow));

            Assert.IsFalse(Economy.IsBoosterUnlocked(BoosterType.Undo));
            Progress.SetLevel(3);
            Assert.IsTrue(Economy.IsBoosterUnlocked(BoosterType.Undo));
            Assert.IsFalse(Economy.IsBoosterUnlocked(BoosterType.Shuffle));
        }

        [Test]
        public void Economy_WeeklyStarsResetOnNewWeek()
        {
            Economy.AddStars(50);
            Assert.AreEqual(50, Economy.WeeklyStars);
            Assert.AreEqual(50, Economy.TotalStars);
            Advance(7 * Day);
            Assert.AreEqual(0, Economy.WeeklyStars);
            Assert.AreEqual(50, Economy.TotalStars);
            Economy.AddStars(5);
            Assert.AreEqual(5, Economy.WeeklyStars);
        }

        [Test]
        public void StarChest_OpensEvery30Stars()
        {
            Assert.AreEqual(30, StarChest.Goal);
            Progress.SetLevel(20);   // every booster unlocked
            Economy.AddStars(29);
            Assert.IsFalse(StarChest.CanOpen);
            Assert.AreEqual(29f / 30f, StarChest.Progress01, 1e-5);
            Assert.AreEqual(0, StarChest.Open().Length);
            Economy.AddStars(31);
            Assert.AreEqual(2, StarChest.ChestsReady);

            int coins = Economy.Coins;
            int boosters = 0;
            foreach (var b in Economy.AllBoosters) boosters += Economy.GetBooster(b);
            var rewards = StarChest.Open();
            Assert.AreEqual(coins + 100, Economy.Coins);
            int after = 0;
            foreach (var b in Economy.AllBoosters) after += Economy.GetBooster(b);
            Assert.AreEqual(boosters + 2, after);
            Assert.AreEqual(RewardType.Coins, rewards[0].type);
            Assert.AreEqual(30, StarChest.Progress);
            Assert.IsTrue(StarChest.CanOpen);
        }

        // ============================================================== Lives

        [Test]
        public void Lives_RegenerationMath()
        {
            Assert.AreEqual(5, Lives.Hearts);
            Assert.IsTrue(Lives.IsFull);
            Assert.AreEqual(0, Lives.SecondsToNext);

            Assert.IsTrue(Lives.TryConsume());
            Assert.IsTrue(Lives.TryConsume());
            Assert.AreEqual(3, Lives.Hearts);
            Assert.That(Lives.SecondsToNext, Is.InRange(Lives.RegenSeconds - 5, Lives.RegenSeconds));

            Advance(Lives.RegenSeconds - 10);
            Assert.AreEqual(3, Lives.Hearts);
            Advance(20);
            Assert.AreEqual(4, Lives.Hearts, "one heart after 30 min");
            Assert.That(Lives.SecondsToNext, Is.InRange(Lives.RegenSeconds - 15, Lives.RegenSeconds - 5));

            Advance(10 * Lives.RegenSeconds);
            Assert.AreEqual(5, Lives.Hearts, "capped at max");
            Assert.AreEqual(0, Lives.SecondsToNext);
            Assert.AreEqual(0, SaveSystem.Data.nextHeartAt);

            // Several hearts regenerate at once while the app was closed.
            for (int i = 0; i < 5; i++) Assert.IsTrue(Lives.TryConsume());
            Assert.IsFalse(Lives.TryConsume());
            Assert.IsFalse(Lives.CanPlay);
            Advance(2 * Lives.RegenSeconds + 30);
            Assert.AreEqual(2, Lives.Hearts);
        }

        [Test]
        public void Lives_RefillAndInfinite()
        {
            for (int i = 0; i < 3; i++) Lives.TryConsume();
            Economy.AddCoins(500, "test");   // 800
            Assert.IsTrue(Lives.TryBuyRefill());
            Assert.AreEqual(300, Economy.Coins);
            Assert.AreEqual(5, Lives.Hearts);
            Assert.IsFalse(Lives.TryBuyRefill(), "can't buy when full");

            Lives.AddInfinite(600);
            Assert.IsTrue(Lives.HasInfinite);
            Assert.IsTrue(Lives.TryConsume());
            Assert.AreEqual(5, Lives.Hearts, "no heart loss with infinite hearts");
            Lives.AddInfinite(600);
            Assert.That(Lives.InfiniteRemainingSeconds, Is.InRange(1195, 1200), "infinite time stacks");
            Advance(1300);
            Assert.IsFalse(Lives.HasInfinite);
            Assert.IsTrue(Lives.TryConsume());
            Assert.AreEqual(4, Lives.Hearts);

            Lives.Add(10);
            Assert.AreEqual(Lives.Max, Lives.Hearts);
        }

        [Test]
        public void Lives_ClockMovedBackRestartsTimer()
        {
            Assert.IsTrue(Lives.TryConsume());
            Advance(-5 * Day);   // device clock set back by days
            Assert.AreEqual(4, Lives.Hearts);
            Assert.That(Lives.SecondsToNext, Is.InRange(Lives.RegenSeconds - 5, Lives.RegenSeconds), "timer restarts, never waits days");
        }

        // ============================================================== Daily rewards

        [Test]
        public void Daily_CycleAndMissedDayReset()
        {
            Assert.IsTrue(DailyRewards.CanClaim);
            Assert.AreEqual(0, DailyRewards.CurrentDay);
            var r = DailyRewards.Claim();
            Assert.AreEqual(1, r.Length);
            Assert.AreEqual(RewardType.Coins, r[0].type);
            Assert.AreEqual(50, r[0].amount);
            Assert.AreEqual(350, Economy.Coins);
            Assert.IsFalse(DailyRewards.CanClaim);
            Assert.AreEqual(0, DailyRewards.Claim().Length, "only once per day");
            Assert.AreEqual(0, DailyRewards.CurrentDay, "shows the day just claimed");
            Assert.IsTrue(DailyRewards.IsDayClaimed(0));

            Advance(Day);
            Assert.IsTrue(DailyRewards.CanClaim);
            Assert.AreEqual(1, DailyRewards.CurrentDay);
            int undos = Economy.GetBooster(BoosterType.Undo);
            DailyRewards.Claim();
            Assert.AreEqual(undos + 1, Economy.GetBooster(BoosterType.Undo));

            // Missing a day resets the cycle to day 1.
            Advance(2 * Day);
            Assert.IsTrue(DailyRewards.CanClaim);
            Assert.AreEqual(0, DailyRewards.CurrentDay);
            Assert.IsFalse(DailyRewards.IsDayClaimed(0));
            DailyRewards.Claim();
            Assert.AreEqual(1, SaveSystem.Data.dailyDay);

            // Full week then wrap around.
            for (int day = 1; day < 7; day++)
            {
                Advance(Day);
                Assert.AreEqual(day, DailyRewards.CurrentDay);
                DailyRewards.Claim();
            }
            Advance(Day);
            Assert.AreEqual(0, DailyRewards.CurrentDay, "after the chest the cycle restarts");

            var chest = DailyRewards.DayRewards(6);
            Assert.AreEqual(5, chest.Length);
            Assert.AreEqual(300, chest[0].amount);
            Assert.IsTrue(DailyRewards.IsChestDay(6));
            var day4 = DailyRewards.DayRewards(3);
            Assert.AreEqual(2, day4.Length, "Extra Bottle + Shuffle");
            Assert.AreEqual(BoosterType.Bottle, day4[0].booster);
            Assert.AreEqual(BoosterType.Shuffle, day4[1].booster);
            Assert.AreEqual(BoosterType.Wand, DailyRewards.DayRewards(5)[0].booster);
            Assert.AreEqual(2, DailyRewards.DayRewards(5)[0].amount, "Wand x2");
        }

        // ============================================================== Lucky spin

        [Test]
        public void Spin_WeightsAndDeterminism()
        {
            int[] expected = { 30, 12, 22, 12, 10, 8, 5, 1 };
            Assert.AreEqual(LuckySpin.SegmentCount, expected.Length);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], LuckySpin.SegmentWeight(i));
            Assert.AreEqual(100, LuckySpin.TotalWeight);
            Assert.AreEqual(250, LuckySpin.SegmentReward(7).amount);
            Assert.AreEqual(BoosterType.Wand, LuckySpin.SegmentReward(6).booster);

            Assert.AreEqual(0, LuckySpin.PickSegment(0f));
            Assert.AreEqual(0, LuckySpin.PickSegment(0.2999f));
            Assert.AreEqual(1, LuckySpin.PickSegment(0.30f));
            Assert.AreEqual(6, LuckySpin.PickSegment(0.985f));
            Assert.AreEqual(7, LuckySpin.PickSegment(0.995f));
            Assert.AreEqual(7, LuckySpin.PickSegment(1f));

            // A uniform sweep of rolls lands on each segment exactly proportionally to its weight.
            var counts = new int[LuckySpin.SegmentCount];
            const int N = 100000;
            for (int i = 0; i < N; i++) counts[LuckySpin.PickSegment((i + 0.5f) / N)]++;
            for (int i = 0; i < counts.Length; i++) Assert.That(counts[i], Is.InRange(expected[i] * 1000 - 2, expected[i] * 1000 + 2));

            // Same seed → same sequence.
            var a = new List<int>();
            var b = new List<int>();
            CoreRandom.Seed(7);
            for (int i = 0; i < 50; i++) a.Add(LuckySpin.PickSegment(CoreRandom.Value()));
            CoreRandom.Seed(7);
            for (int i = 0; i < 50; i++) b.Add(LuckySpin.PickSegment(CoreRandom.Value()));
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Spin_DailyAllowance()
        {
            Assert.IsTrue(LuckySpin.HasFreeSpin);
            Assert.AreEqual(2, LuckySpin.AdSpinsLeft);
            int index = LuckySpin.Spin(false);
            Assert.That(index, Is.InRange(0, 7));
            Assert.IsFalse(LuckySpin.HasFreeSpin);
            Assert.AreEqual(-1, LuckySpin.Spin(false));
            Assert.That(LuckySpin.Spin(true), Is.InRange(0, 7));
            Assert.That(LuckySpin.Spin(true), Is.InRange(0, 7));
            Assert.AreEqual(-1, LuckySpin.Spin(true));
            Assert.IsFalse(LuckySpin.CanSpin);
            Advance(Day);
            Assert.IsTrue(LuckySpin.HasFreeSpin);
            Assert.AreEqual(2, LuckySpin.AdSpinsLeft);
        }

        [Test]
        public void DailyAllowances_DoNotResetWhenTheLocalDayMovesBack()
        {
            // Wednesday 12:00 UTC is Wednesday 09:00 at UTC-3 but already Thursday 02:00 at UTC+14. Flipping the device
            // zone moves the local day forward (a legit new day) and back; only a NEW day may reset the allowances.
            var west = TimeZoneInfo.CreateCustomTimeZone("test-3", TimeSpan.FromHours(-3), "test-3", "test-3");
            var east = TimeZoneInfo.CreateCustomTimeZone("test+14", TimeSpan.FromHours(14), "test+14", "test+14");
            void UseEverything()
            {
                Assert.AreEqual(ShopOffers.FreeGiftCoins, ShopOffers.ClaimFreeGift().amount);
                for (int i = 0; i < ShopOffers.AdCoinsPerDay; i++) ShopOffers.GrantAdCoins();
                Assert.That(LuckySpin.Spin(false), Is.InRange(0, 7));
                LuckySpin.Spin(true);
                LuckySpin.Spin(true);
                ForceQuests(QuestKind.WinLevels, QuestKind.CompleteBottles, QuestKind.CollectStars);
                foreach (var k in new[] { QuestKind.WinLevels, QuestKind.CompleteBottles, QuestKind.CollectStars }) Quests.Report(k, 999);
                for (int i = 0; i < 3; i++) Assert.IsFalse(Quests.Claim(i).IsEmpty);
            }
            void AssertNothingNew(string at)
            {
                Assert.IsFalse(ShopOffers.FreeGiftAvailable, "gift " + at);
                Assert.AreEqual(0, ShopOffers.AdCoinsLeft, "ad coins " + at);
                Assert.IsFalse(LuckySpin.CanSpin, "spins " + at);
                foreach (var q in Quests.Today()) Assert.IsTrue(q.claimed, "quests regenerated " + at);
            }

            TimeUtil.TimeZoneOverride = west;
            UseEverything();
            TimeUtil.TimeZoneOverride = east;           // Thursday: a real new local day
            Assert.IsTrue(ShopOffers.FreeGiftAvailable);
            Assert.IsTrue(LuckySpin.HasFreeSpin);
            UseEverything();
            TimeUtil.TimeZoneOverride = west;           // back to Wednesday
            AssertNothingNew("after moving back");
            TimeUtil.TimeZoneOverride = east;           // Thursday again: already used
            AssertNothingNew("after moving forward again");

            // Weekly stars: Monday 05:00 UTC is still Sunday at UTC-10; moving back must not wipe this week's stars.
            SetNow(new DateTimeOffset(2026, 3, 9, 5, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());
            TimeUtil.TimeZoneOverride = TimeZoneInfo.Utc;
            Economy.AddStars(40);
            Assert.AreEqual(40, Economy.WeeklyStars);
            TimeUtil.TimeZoneOverride = TimeZoneInfo.CreateCustomTimeZone("test-10", TimeSpan.FromHours(-10), "test-10", "test-10");
            Assert.AreEqual(40, Economy.WeeklyStars, "moving back across Monday wiped the weekly stars");
            TimeUtil.TimeZoneOverride = TimeZoneInfo.Utc;
            Assert.AreEqual(40, Economy.WeeklyStars);
            Advance(7 * Day);
            Assert.AreEqual(0, Economy.WeeklyStars, "a real new week still resets");
        }

        [Test]
        public void Save_HousekeepingWritesKeepUpdatedAt()
        {
            Economy.AddCoins(5, "test");
            SaveSystem.Save();
            long stamped = SaveSystem.Data.updatedAt;
            Assert.Greater(stamped, 0);

            // Daily resets / heart regen are not player progress: they must not win the cloud merge's recency tie-break.
            Advance(Day);
            Quests.Today();
            SaveSystem.Flush();
            Assert.AreEqual(stamped, SaveSystem.Data.updatedAt, "a quest regeneration re-stamped the save");

            Economy.AddCoins(5, "test");   // a real change stamps again
            SaveSystem.Flush();
            Assert.Greater(SaveSystem.Data.updatedAt, stamped);
        }

        // ============================================================== Quests

        [Test]
        public void Quests_DeterministicPerDay()
        {
            var seen = new HashSet<QuestKind>();
            var distinctSets = new HashSet<string>();
            for (int day = 20000; day < 20060; day++)
            {
                var k1 = Quests.KindsForDay(day, 50);
                var k2 = Quests.KindsForDay(day, 50);
                CollectionAssert.AreEqual(k1, k2);
                Assert.AreEqual(3, k1.Length);
                CollectionAssert.AllItemsAreUnique(k1);
                foreach (var k in k1) seen.Add(k);
                distinctSets.Add(string.Join(",", k1));

                var low = Quests.KindsForDay(day, 1);
                CollectionAssert.AllItemsAreUnique(low);
                CollectionAssert.DoesNotContain(low, QuestKind.WinHard, "no hard levels before level 10");
                CollectionAssert.DoesNotContain(low, QuestKind.UseBoosters, "no boosters before level 3");
            }
            Assert.AreEqual(8, seen.Count, "every quest kind shows up");
            Assert.Greater(distinctSets.Count, 10, "sets vary between days");

            var today = Quests.Today();
            Assert.AreEqual(3, today.Count);
            var expected = Quests.KindsForDay(TimeUtil.Today, Progress.CurrentLevel);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected[i], today[i].kind);
                Assert.AreEqual(Quests.TargetOf(today[i].kind), today[i].target);
                Assert.AreEqual(0, today[i].progress);
            }
            // GDD §6 daily quests.
            Assert.AreEqual(3, Quests.TargetOf(QuestKind.WinLevels));
            Assert.AreEqual(80, Quests.RewardOf(QuestKind.WinLevels).amount);
            Assert.AreEqual(25, Quests.TargetOf(QuestKind.CompleteBottles));
            Assert.AreEqual(60, Quests.RewardOf(QuestKind.CompleteBottles).amount);
            Assert.AreEqual(3, Quests.TargetOf(QuestKind.ReachCombo));
            Assert.AreEqual(BoosterType.Bottle, Quests.RewardOf(QuestKind.ReachCombo).booster);
            Assert.AreEqual(2, Quests.TargetOf(QuestKind.UseBoosters));
            Assert.AreEqual(9, Quests.TargetOf(QuestKind.CollectStars));
            Assert.AreEqual(BoosterType.Undo, Quests.RewardOf(QuestKind.CollectStars).booster);
            Assert.AreEqual(100, Quests.RewardOf(QuestKind.WinHard).amount);
            Assert.AreEqual(BoosterType.Shuffle, Quests.RewardOf(QuestKind.WinStreak2).booster);
            Assert.AreEqual(2, Quests.TargetOf(QuestKind.WinThreeStars));
            Assert.AreEqual(BoosterType.Wand, Quests.RewardOf(QuestKind.WinThreeStars).booster);
            Assert.AreEqual("Win 3 levels", new QuestView { kind = QuestKind.WinLevels, target = 3 }.Title);
            Assert.AreEqual("Win 2 levels in a row", new QuestView { kind = QuestKind.WinStreak2, target = 1 }.Title);
            Assert.AreEqual("Fill 25 bottles", new QuestView { kind = QuestKind.CompleteBottles, target = 25 }.Title);
            Assert.AreEqual("Win 2 levels with 3 stars", new QuestView { kind = QuestKind.WinThreeStars, target = 2 }.Title);
        }

        [Test]
        public void Quests_ProgressRulesAndClaim()
        {
            ForceQuests(QuestKind.ReachCombo, QuestKind.WinThreeStars, QuestKind.CompleteBottles);

            Quests.Report(QuestKind.ReachCombo, 1, 2);
            Assert.AreEqual(2, Quests.Today()[0].progress);
            Quests.Report(QuestKind.ReachCombo, 1, 1);
            Assert.AreEqual(2, Quests.Today()[0].progress, "combo progress keeps the best value");
            Quests.Report(QuestKind.ReachCombo, 1, 5);
            Assert.AreEqual(3, Quests.Today()[0].progress, "capped at the target");
            Assert.IsTrue(Quests.Today()[0].IsComplete);

            Quests.Report(QuestKind.WinThreeStars);
            Assert.AreEqual(1, Quests.Today()[1].progress);
            Quests.Report(QuestKind.WinThreeStars);
            Assert.IsTrue(Quests.Today()[1].IsComplete);

            for (int i = 0; i < 24; i++) Progress.ReportBottleCompleted(1);
            Assert.AreEqual(24, Quests.Today()[2].progress);
            Assert.AreEqual(24, SaveSystem.Data.totalBottles);
            Assert.AreEqual(2, Quests.ClaimableCount);

            int bottles = Economy.GetBooster(BoosterType.Bottle);
            var reward = Quests.Claim(0);
            Assert.AreEqual(RewardType.Booster, reward.type);
            Assert.AreEqual(bottles + 1, Economy.GetBooster(BoosterType.Bottle));
            Assert.IsTrue(Quests.Claim(0).IsEmpty, "can't claim twice");
            Assert.IsTrue(Quests.Claim(2).IsEmpty, "can't claim incomplete");
            Assert.IsTrue(Quests.Claim(99).IsEmpty);
            Assert.AreEqual(1, Quests.ClaimableCount);

            // Combos also count from bottles completed in a chain.
            ForceQuests(QuestKind.ReachCombo, QuestKind.WinLevels, QuestKind.UseBoosters);
            Progress.ReportBottleCompleted(1);
            Progress.ReportBottleCompleted(2);
            Assert.AreEqual(2, Quests.Today()[0].progress);
            Progress.ReportBottleCompleted(3);
            Assert.IsTrue(Quests.Today()[0].IsComplete);
            Assert.AreEqual(3, SaveSystem.Data.maxCombo);
            Progress.ReportBoosterUsed(BoosterType.Undo);
            Progress.ReportBoosterUsed(BoosterType.Shuffle);
            Assert.IsTrue(Quests.Today()[2].IsComplete);
            Progress.ReportPour();
            Assert.AreEqual(1, SaveSystem.Data.totalPours);

            // A new day brings new quests.
            Advance(Day);
            var next = Quests.Today();
            Assert.AreEqual(3, next.Count);
            foreach (var q in next) Assert.AreEqual(0, q.progress);
        }

        static void ForceQuests(params QuestKind[] kinds)
        {
            var d = SaveSystem.Data;
            d.questDay = TimeUtil.Today;
            d.quests.Clear();
            foreach (var k in kinds) d.quests.Add(new QuestState { kind = (int)k, target = Quests.TargetOf(k) });
            SaveSystem.MarkDirty();
        }

        // ============================================================== Progress

        [Test]
        public void Progress_ReportWinAndFail()
        {
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));   // deterministic cards (no duplicate coins)
            try
            {
                ForceQuests(QuestKind.WinLevels, QuestKind.WinStreak2, QuestKind.CollectStars);
                var r = Progress.ReportWin(1, false, 3, 6, 1);
                Assert.AreEqual(2, Progress.CurrentLevel);
                Assert.AreEqual(20, r.coins);
                Assert.AreEqual(1, r.winStreak);
                Assert.AreEqual(3, r.stars);
                Assert.AreEqual(0, r.previousBest);
                Assert.AreEqual(3, r.starsGained);
                Assert.IsFalse(r.replay);
                Assert.IsNotNull(r.cardId, "first wins grant a card");
                Assert.AreEqual(3, Economy.TotalStars);
                Assert.AreEqual(3, Progress.BestStars(1));
                Assert.AreEqual(1, SaveSystem.Data.levelsWon);
                Assert.AreEqual(1, Quests.Today()[0].progress);
                Assert.AreEqual(0, Quests.Today()[1].progress, "streak of 1 doesn't count");
                Assert.AreEqual(3, Quests.Today()[2].progress);

                r = Progress.ReportWin(2, true, 2, 30, 2);
                Assert.AreEqual(40, r.coins, "hard levels pay double");
                Assert.AreEqual(2, r.winStreak);
                Assert.AreEqual(2, r.starsGained);
                Assert.AreEqual(5, Economy.TotalStars);
                Assert.AreEqual(1, Quests.Today()[1].progress, "two wins in a row");
                Assert.AreEqual(5, Quests.Today()[2].progress);
                Assert.AreEqual(1, SaveSystem.Data.hardLevelsWon);
                Assert.AreEqual(2, SaveSystem.Data.maxCombo);

                int hearts = Lives.Hearts;
                Progress.ReportFail(3);
                Assert.AreEqual(0, Progress.WinStreak);
                Assert.AreEqual(2, Progress.BestStreak);
                Assert.AreEqual(hearts - 1, Lives.Hearts);
                Assert.AreEqual(3, Progress.CurrentLevel, "a loss doesn't change the level");
                Assert.AreEqual(1, SaveSystem.Data.levelsLost);

                Progress.SetLevel(20);
                r = Progress.ReportWin(20, true, 1, 50, 1);
                Assert.IsTrue(r.areaChanged);
                Assert.AreEqual(1, r.newAreaNumber);
                Assert.IsTrue(Progress.HasUnseenArea);
                Progress.MarkAreaSeen();
                Assert.IsFalse(Progress.HasUnseenArea);
                Assert.AreEqual(1, Progress.BestStars(20));
                Assert.AreEqual(0, Progress.BestStars(21), "not won yet");
                Assert.IsTrue(Progress.IsLevelWon(10), "levels below the current one count as won");
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Progress_ReplaysOnlyPayNewStars()
        {
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));
            try
            {
                Progress.ReportWin(1, false, 1, 20, 1);
                Progress.ReportWin(2, false, 3, 20, 1);
                Assert.AreEqual(3, Progress.CurrentLevel);
                Assert.AreEqual(2, Progress.WinStreak);
                Assert.AreEqual(4, Economy.TotalStars);
                int coins = Economy.Coins, cards = Collection.TotalOwned;

                int starEvents = 0;
                Action<int, int> onStars = (level, stars) => starEvents++;
                Progress.OnLevelStarsChanged += onStars;
                LevelResult r;
                try { r = Progress.ReportWin(1, false, 3, 12, 4, true); }
                finally { Progress.OnLevelStarsChanged -= onStars; }
                Assert.IsTrue(r.replay);
                Assert.AreEqual(1, r.previousBest);
                Assert.AreEqual(2, r.starsGained);
                Assert.AreEqual(2 * Progress.ReplayCoinsPerStar, r.coins);
                Assert.AreEqual(coins + 2 * Progress.ReplayCoinsPerStar, Economy.Coins, "+10 coins per new star");
                Assert.IsNull(r.cardId, "no card on replays");
                Assert.AreEqual(cards, Collection.TotalOwned);
                Assert.AreEqual(2, Progress.WinStreak, "no streak change");
                Assert.AreEqual(2, r.winStreak);
                Assert.AreEqual(3, Progress.CurrentLevel, "the level does not move");
                Assert.AreEqual(2, SaveSystem.Data.levelsWon, "levels won counts first wins only");
                Assert.AreEqual(1, SaveSystem.Data.replaysWon);
                Assert.AreEqual(3, Progress.BestStars(1));
                Assert.AreEqual(6, Economy.TotalStars);
                Assert.AreEqual(1, starEvents);
                Assert.IsFalse(r.areaChanged);

                // No better: nothing to pay.
                coins = Economy.Coins;
                r = Progress.ReportWin(1, false, 2, 30, 1, true);
                Assert.AreEqual(0, r.starsGained);
                Assert.AreEqual(0, r.coins);
                Assert.AreEqual(coins, Economy.Coins);
                Assert.AreEqual(3, Progress.BestStars(1), "a worse result never lowers the best");

                // A failed replay costs a heart but keeps the streak; failing the current level resets it.
                int hearts = Lives.Hearts;
                Progress.ReportFail(1);
                Assert.AreEqual(hearts - 1, Lives.Hearts);
                Assert.AreEqual(2, Progress.WinStreak);
                Progress.ReportFail(3);
                Assert.AreEqual(0, Progress.WinStreak);

                // "Replaying" the current level is a normal first win.
                r = Progress.ReportWin(3, false, 2, 30, 1, true);
                Assert.IsFalse(r.replay);
                Assert.AreEqual(4, Progress.CurrentLevel);
                Assert.IsNotNull(r.cardId);
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Progress_WorldHelpers()
        {
            Assert.AreEqual(1, Areas.FirstLevelOfArea(0));
            Assert.AreEqual(20, Areas.LastLevelOfArea(0));
            Assert.AreEqual(21, Areas.FirstLevelOfArea(1));
            Assert.AreEqual(40, Areas.LastLevelOfArea(1));
            Assert.AreEqual(60, Areas.MaxStarsPerArea);
            Assert.AreEqual(1, Areas.AreaNumberForLevel(21));
            Assert.AreEqual(20, Areas.LevelInArea(40));

            Assert.AreEqual(WorldState.Current, Progress.StateOfArea(0));
            Assert.AreEqual(WorldState.Locked, Progress.StateOfArea(1));
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));
            try
            {
                int expected = 0;
                for (int level = 1; level <= 20; level++)
                {
                    int stars = level % 3 + 1;
                    expected += stars;
                    Progress.ReportWin(level, false, stars, 10, 1);
                }
                Assert.AreEqual(WorldState.Completed, Progress.StateOfArea(0));
                Assert.AreEqual(WorldState.Current, Progress.StateOfArea(1));
                Assert.AreEqual(WorldState.Locked, Progress.StateOfArea(2));
                Assert.AreEqual(expected, Progress.StarsInArea(0));
                Assert.AreEqual(expected, Progress.StarsInRange(1, 20));
                Assert.AreEqual(20, Progress.LevelsWonInArea(0));
                Assert.AreEqual(0, Progress.LevelsWonInArea(1));
                Assert.AreEqual(0, Progress.StarsInArea(1));
                int perfect = 0;
                for (int level = 1; level <= 20; level++) if (level % 3 == 2) perfect++;
                Assert.AreEqual(perfect, Progress.PerfectLevelsInArea(0));
                Assert.AreEqual(perfect, Progress.PerfectLevels);
                Assert.AreEqual(20, SaveSystem.Data.levelStars.Length);
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Progress_StreakBonusesAndContinues()
        {
            SaveSystem.Data.winStreak = 3;
            Assert.AreEqual(0, Progress.StreakBonuses.Length, "the Rainbow Potion is locked before level 10");
            Progress.SetLevel(12);
            CollectionAssert.AreEqual(new[] { BoosterType.Rainbow }, Progress.StreakBonuses);
            SaveSystem.Data.winStreak = 6;
            CollectionAssert.AreEqual(new[] { BoosterType.Rainbow }, Progress.StreakBonuses, "the Crystal Ball unlocks at 15");
            Progress.SetLevel(16);
            CollectionAssert.AreEqual(new[] { BoosterType.Rainbow, BoosterType.Crystal }, Progress.StreakBonuses);
            SaveSystem.Data.winStreak = 2;
            Assert.AreEqual(0, Progress.StreakBonuses.Length);

            Economy.AddCoins(2000, "test");
            Assert.AreEqual(300, Progress.ContinuePrice);
            Assert.IsTrue(Progress.TryBuyContinue());
            Assert.AreEqual(600, Progress.ContinuePrice);
            Assert.IsTrue(Progress.TryBuyContinue());
            Assert.AreEqual(900, Progress.ContinuePrice);
            Assert.IsTrue(Progress.TryBuyContinue());
            Assert.AreEqual(900, Progress.ContinuePrice, "capped");
            Progress.ReportFail(12);
            Assert.AreEqual(300, Progress.ContinuePrice, "resets every level");

            Assert.IsTrue(Progress.TryBuyContinue());
            Assert.AreEqual(600, Progress.ContinuePrice);
            Progress.ReportLevelStart(12);   // e.g. the app was killed mid-level and the level starts again
            Assert.AreEqual(300, Progress.ContinuePrice, "a new attempt starts at the base price");
        }

        [Test]
        public void Progress_AbandonedLevelCountsAsLoss()
        {
            Assert.AreEqual(0, Progress.AbandonedLevel);
            Assert.IsFalse(Progress.ResolveAbandonedLevel());

            Progress.ReportLevelStart(1);
            Progress.ReportWin(1, false, 3, 20, 1);
            Assert.AreEqual(0, Progress.AbandonedLevel, "a finished level is not abandoned");

            Progress.ReportLevelStart(2);
            Assert.AreEqual(2, Progress.AbandonedLevel);
            SaveSystem.Load();   // "app killed" → next launch
            Assert.AreEqual(2, Progress.AbandonedLevel, "the attempt was written immediately");
            int hearts = Lives.Hearts;
            Assert.IsTrue(Progress.ResolveAbandonedLevel());
            Assert.AreEqual(hearts - 1, Lives.Hearts);
            Assert.AreEqual(0, Progress.WinStreak);
            Assert.AreEqual(0, Progress.AbandonedLevel);
            Assert.IsFalse(Progress.ResolveAbandonedLevel(), "resolved once");
        }

        // ============================================================== Collection

        const string TestCatalog =
            "{\"areas\":[{\"id\":\"a0\",\"index\":0,\"accent\":\"#2ED6A1\",\"cards\":[\"p1\",\"p2\"]}," +
            "{\"id\":\"a1\",\"index\":1,\"accent\":\"#FF7EB6\",\"cards\":[\"p3\",\"p4\"]}]}";

        [Test]
        public void Collection_DuplicatesAndAlbums()
        {
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));
            try
            {
                Assert.AreEqual(2, Catalog.Areas.Count);
                Assert.AreEqual("a1", Catalog.AreaOfCard("p4"));
                Assert.AreEqual("a0", Areas.AreaForLevel(1).id);
                Assert.AreEqual("a1", Areas.AreaForLevel(21).id);
                Assert.AreEqual("a0", Areas.AreaForLevel(41).id, "areas cycle");
                Assert.IsTrue(Collection.IsAreaUnlocked("a0"));
                Assert.IsFalse(Collection.IsAreaUnlocked("a1"));

                for (int i = 0; i < 100 && Collection.OwnedCount("a0") < 2; i++)
                {
                    string card = Collection.GrantRandomCard(out _);
                    Assert.AreEqual("a0", Catalog.AreaOfCard(card), "only unlocked areas");
                }
                Assert.AreEqual(2, Collection.OwnedCount("a0"));
                Assert.IsTrue(Collection.AlbumComplete("a0"));
                Assert.AreEqual(1, Collection.ClaimableAlbums);

                int coins = Economy.Coins;
                string dup = Collection.GrantRandomCard(out bool duplicate);
                Assert.IsTrue(duplicate);
                Assert.IsNotNull(dup);
                Assert.AreEqual(coins + Collection.DuplicateCoins, Economy.Coins);
                Assert.AreEqual(2, Collection.TotalOwned, "duplicates are not stored");

                coins = Economy.Coins;
                int undos = Economy.GetBooster(BoosterType.Undo);
                int rainbows = Economy.GetBooster(BoosterType.Rainbow);
                var rewards = Collection.ClaimAlbum("a0");
                Assert.AreEqual(1 + Economy.AllBoosters.Length, rewards.Length, "500 coins + one of each booster");
                Assert.AreEqual(coins + 500, Economy.Coins);
                Assert.AreEqual(undos + 1, Economy.GetBooster(BoosterType.Undo));
                Assert.AreEqual(rainbows + 1, Economy.GetBooster(BoosterType.Rainbow));
                Assert.IsTrue(Collection.AlbumClaimed("a0"));
                Assert.AreEqual(0, Collection.ClaimAlbum("a0").Length, "album claimed once");
                Assert.AreEqual(0, Collection.ClaimableAlbums);

                Progress.SetLevel(21);
                Assert.IsTrue(Collection.IsAreaUnlocked("a1"));
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Collection_PrefersMissingCards()
        {
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));
            try
            {
                // With p1 owned, P(p2) = 0.7 + 0.3 * 0.5 = 0.85.
                int missingPicks = 0;
                const int Trials = 2000;
                for (int i = 0; i < Trials; i++)
                {
                    var cards = SaveSystem.Data.cards;
                    cards.Clear();
                    cards.Add("p1");
                    if (Collection.GrantRandomCard(out _) == "p2") missingPicks++;
                }
                Assert.That(missingPicks / (float)Trials, Is.InRange(0.80f, 0.90f));
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Catalog_ParsesWorldsAndCards()
        {
            const string json =
                "{\"areas\":[{\"id\":\"moon\",\"index\":5,\"accent\":\"#FFA94D\",\"cards\":[\"happy_pumpkin\",\"moon_potion\",\"happy_pumpkin\",\"\"]}," +
                "{\"id\":\"forest\",\"index\":0,\"accent\":\"2ED6A1\",\"cards\":[\"glow_mushroom\"]}," +
                "{\"id\":\"old\",\"index\":1,\"accent\":\"nope\",\"products\":[\"cupcake\"]}]}";
            try
            {
                Assert.IsTrue(Catalog.LoadFromJson(json));
                Assert.AreEqual(3, Catalog.Areas.Count);
                Assert.AreEqual("forest", Catalog.GetArea(0).id, "sorted by declared index");
                Assert.AreEqual("old", Catalog.GetArea(1).id);
                Assert.AreEqual("moon", Catalog.GetArea(2).id);
                Assert.AreEqual(2, Catalog.GetArea("moon").index, "re-indexed 0..n-1");
                CollectionAssert.AreEqual(new[] { "happy_pumpkin", "moon_potion" }, Catalog.GetArea("moon").cardIds, "duplicates / empty ids dropped");
                Assert.AreEqual(0, Catalog.GetArea("old").CardCount, "the old \"products\" field is not read");
                Assert.AreEqual(3, Catalog.CardCount);
                Assert.AreEqual("forest", Catalog.AreaOfCard("glow_mushroom"));
                Assert.IsNull(Catalog.AreaOfCard("cupcake"));
                Assert.AreEqual("card_moon_potion", Catalog.CardSprite("moon_potion"));
                Assert.AreEqual("card.moon_potion", Catalog.CardNameKey("moon_potion"));
                var moon = Catalog.GetArea("moon");
                Assert.AreEqual("area.moon", moon.NameKey);
                Assert.AreEqual("home_moon", moon.HomeBackground);
                Assert.AreEqual("gamebg_moon", moon.GameBackground);
                Assert.AreEqual(new Color(1f, 169f / 255f, 77f / 255f), moon.accent);
                Assert.AreEqual(new Color(46f / 255f, 214f / 255f, 161f / 255f), Catalog.GetArea("forest").accent, "accent without #");
                Assert.AreEqual(new Color(0.482f, 0.302f, 1f), Catalog.GetArea("old").accent, "bad accent → brand purple");

                // World themes cycle; numbering does not.
                Assert.AreEqual("forest", Areas.AreaForNumber(3).id);
                Assert.AreEqual(1, Areas.CycleOfArea(3));
                Assert.AreEqual(0, Areas.CycleOfArea(2));
                Assert.AreEqual("moon", Areas.AreaForLevel(60).id);
            }
            finally { Catalog.Reload(); }
        }

        [Test]
        public void Catalog_EmptyOrBrokenJsonDoesNotThrow()
        {
            try
            {
                Assert.IsFalse(Catalog.LoadFromJson("{broken"));
                Assert.AreEqual(0, Catalog.Areas.Count);
                Assert.IsNull(Areas.AreaForLevel(5));
                Assert.IsNull(Collection.GrantRandomCard(out bool dup));
                Assert.IsFalse(dup);
            }
            finally { Catalog.Reload(); }
        }

        // ============================================================== Rewards / Art / Audio names

        [Test]
        public void Reward_IconAndAmountText()
        {
            Assert.AreEqual("icon_coin", Reward.Coins(250).IconSprite);
            Assert.AreEqual("250", Reward.Coins(250).AmountText);
            Assert.AreEqual("1,500", Reward.Coins(1500).AmountText);
            Assert.AreEqual("booster_undo", Reward.Booster(BoosterType.Undo, 3).IconSprite);
            Assert.AreEqual("x3", Reward.Booster(BoosterType.Undo, 3).AmountText);
            Assert.AreEqual("icon_heart", Reward.Hearts(1).IconSprite);
            Assert.AreEqual("30m", Reward.InfiniteHearts(30).AmountText);
            Assert.AreEqual("1h", Reward.InfiniteHearts(60).AmountText);
            Assert.AreEqual("1h 30m", Reward.InfiniteHearts(90).AmountText);
            Assert.AreEqual("card_glow_mushroom", Reward.Card("glow_mushroom").IconSprite);
            Assert.AreEqual("card.glow_mushroom", Reward.Card("glow_mushroom").NameKey);
            Assert.AreEqual("booster.wand", Reward.Booster(BoosterType.Wand, 1).NameKey);

            Reward.InfiniteHearts(15).Grant();
            Assert.IsTrue(Lives.HasInfinite);
        }

        [Test]
        public void Art_MissingSpriteIsPlaceholderAndInnerFallback()
        {
            try
            {
                var s1 = Art.Get("zz_missing_sprite_for_tests");
                Assert.NotNull(s1);
                Assert.AreSame(s1, Art.Get("zz_missing_sprite_for_tests"), "placeholder is cached");
                Assert.IsFalse(Art.Exists("zz_missing_sprite_for_tests"));
                Assert.AreEqual(new Rect(0.06f, 0.12f, 0.88f, 0.78f), Art.InnerRect("zz_not_listed"));

                Art.LoadIndexJson("{\"sprites\":[{\"name\":\"zz_test_sign\",\"w\":100,\"h\":50,\"inner\":[0.1,0.2,0.3,0.4]}]}");
                Assert.AreEqual(new Rect(0.1f, 0.2f, 0.3f, 0.4f), Art.InnerRect("zz_test_sign"));
                Assert.AreEqual(2f, Art.Info("zz_test_sign").Aspect);
            }
            finally { Art.ClearCache(); }
        }

        [Test]
        public void Audio_ClipNames()
        {
            Assert.AreEqual("layer_reveal", AudioManager.ToSnakeCase("LayerReveal"));
            Assert.AreEqual("click", AudioManager.ToSnakeCase("Click"));
            Assert.AreEqual("sfx_spin_win", AudioManager.ClipName(Sfx.SpinWin));
            Assert.AreEqual("sfx_popup_open", AudioManager.ClipName(Sfx.PopupOpen));
            Assert.AreEqual("music_home", AudioManager.ClipName(Music.Home));
            Assert.IsNull(AudioManager.ClipName(Music.None));
            // Outside play mode everything is a silent no-op.
            Assert.DoesNotThrow(() => { AudioManager.Play(Sfx.Click); AudioManager.PlayCombo(4); AudioManager.PlayMusic(Music.Game); });
            AudioManager.MusicOn = false;
            Assert.IsFalse(SaveSystem.Data.musicOn);
            Haptics.Enabled = false;
            Assert.IsFalse(SaveSystem.Data.vibrationOn);
            Assert.DoesNotThrow(() => Haptics.Play(HapticType.Success));
        }

        // ============================================================== Loc

        [Test]
        public void Loc_CsvParserEdgeCases()
        {
            string csv = "\uFEFFkey,en,pt,es\r\n" +
                         "simple,Hello,Olá,Hola\r\n" +
                         "\"quoted,key\",\"a, b\",\"say \"\"hi\"\"\",\"multi\r\nline\"\n" +
                         "\n" +
                         "empty,,,\r" +
                         "stray,5\" box,x,y\n" +
                         "last,1,2,3";   // no trailing newline
            var rows = Loc.ParseCsv(csv);
            Assert.AreEqual(6, rows.Count, "blank line skipped");
            CollectionAssert.AreEqual(new[] { "key", "en", "pt", "es" }, rows[0], "BOM stripped");
            CollectionAssert.AreEqual(new[] { "simple", "Hello", "Olá", "Hola" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "quoted,key", "a, b", "say \"hi\"", "multi\nline" }, rows[2]);
            CollectionAssert.AreEqual(new[] { "empty", "", "", "" }, rows[3]);
            CollectionAssert.AreEqual(new[] { "stray", "5\" box", "x", "y" }, rows[4], "a quote inside an unquoted cell is literal");
            CollectionAssert.AreEqual(new[] { "last", "1", "2", "3" }, rows[5]);

            Assert.AreEqual(0, Loc.ParseCsv("").Count);
            Assert.AreEqual(1, Loc.ParseCsv("\"\"").Count, "a lone quoted empty cell is a row");
        }

        [Test]
        public void Loc_FallbackChainAndFormatting()
        {
            Loc.AddCsv("key,en,pt,es\n" +
                       "test.all,EN,PT,ES\n" +
                       "test.only_en,English only,,\n" +
                       "test.newline,Line1\\nLine2,L1\\nL2,\n" +
                       "test.fmt,\"{0} of {1}\",\"{0} de {1}\",\"{0} de {1}\"\n" +
                       "test.bad_fmt,{0} {oops},,\n", "tests");
            Loc.AddCsv("es,key,en\nHola,test.order,Hello", "tests-order");

            Loc.Language = "pt";
            Assert.AreEqual("pt", SaveSystem.Data.language, "choice persisted");
            Assert.AreEqual("PT", Loc.T("test.all"));
            Assert.AreEqual("English only", Loc.T("test.only_en"), "missing cell → English");
            Assert.AreEqual("test.missing.key", Loc.T("test.missing.key"), "missing key → key");
            Assert.AreEqual("L1\nL2", Loc.T("test.newline"));
            Assert.AreEqual("1 de 2", Loc.T("test.fmt", 1, 2));
            Assert.AreEqual("Hello", Loc.T("test.order"), "pt missing → en");
            Assert.AreEqual("1.234.567", Loc.Number(1234567));
            Assert.DoesNotThrow(() => Loc.T("test.bad_fmt", 1));

            Loc.Language = "es";
            Assert.AreEqual("ES", Loc.T("test.all"));
            Assert.AreEqual("Line1\nLine2", Loc.T("test.newline"), "es missing → en (unescaped)");
            Assert.AreEqual("Hola", Loc.T("test.order"), "columns are matched by header name");
            Assert.AreEqual("1.234.567", Loc.Number(1234567));

            Loc.Language = "fr";   // unsupported: ignored
            Assert.AreEqual("es", Loc.Language);

            Loc.Language = "en-US";
            Assert.AreEqual("en", Loc.Language);
            Assert.AreEqual("1,234,567", Loc.Number(1234567));
            Assert.AreEqual("-5", Loc.Number(-5));
            Assert.IsTrue(Loc.Has("test.all"));
            Assert.IsFalse(Loc.Has("test.nope"));
            Assert.AreEqual("", Loc.T(null));

            Assert.AreEqual("Português", Loc.LanguageDisplayName("pt"));
            Assert.AreEqual("Español", Loc.LanguageDisplayName("es"));
            Assert.AreEqual("English", Loc.LanguageDisplayName("en"));
            Assert.AreEqual(3, Loc.Languages.Count);

            int changes = 0;
            Action onChange = () => changes++;
            Loc.OnLanguageChanged += onChange;
            try
            {
                Loc.Language = "pt";
                Loc.Language = "pt";
                Assert.AreEqual(1, changes, "event only on actual change");
            }
            finally { Loc.OnLanguageChanged -= onChange; }
        }

        [Test]
        public void Loc_MetaTablesHaveAllLanguages()
        {
            // Every row of the meta tables must exist in en, pt and es.
            string[] tables = { "core", "home", "tabs", "ui", "root", "services", "worlds" };
            int checkedTables = 0;
            foreach (string table in tables)
            {
                var asset = Resources.Load<TextAsset>("Loc/" + table);
                if (asset == null) continue;   // not imported yet (fresh clone before the first Unity import)
                checkedTables++;
                var rows = Loc.ParseCsv(asset.text);
                CollectionAssert.AreEqual(new[] { "key", "en", "pt", "es" }, rows[0], table);
                for (int r = 1; r < rows.Count; r++)
                {
                    Assert.AreEqual(4, rows[r].Count, table + " row " + r);
                    for (int c = 0; c < 4; c++) Assert.IsNotEmpty(rows[r][c], table + ": " + rows[r][0] + " column " + c);
                }
            }
            if (checkedTables == 0)
            {
                Assert.Ignore("Resources/Loc tables not imported yet");
                return;
            }
            foreach (QuestKind kind in Enum.GetValues(typeof(QuestKind)))
                Assert.IsTrue(Loc.Has(new QuestView { kind = kind }.TitleKey), kind.ToString());
            foreach (BoosterType b in Enum.GetValues(typeof(BoosterType)))
            {
                Assert.IsTrue(Loc.Has(Economy.BoosterNameKey(b)), b.ToString());
                Assert.IsTrue(Loc.Has(Economy.BoosterDescKey(b)), b + " desc");
            }
            for (int i = 0; i < Liquids.Count; i++) Assert.IsTrue(Loc.Has(Liquids.NameKey(i)), Liquids.NameKey(i));
            for (int i = 1; i <= UI.HomeMascot.TipCount; i++) Assert.IsTrue(Loc.Has("home.tip." + i), "home.tip." + i);
            foreach (string key in new[] { "worlds.title", "worlds.world_number", "worlds.cycle_name", "worlds.levels_range",
                         "worlds.reach_level", "level.goal_3stars", "level.replay_rule", "booster.crystal_not_needed" })
                Assert.IsTrue(Loc.Has(key), key);
            Assert.AreEqual("Undo", Loc.T("booster.undo"));
            Loc.Language = "pt";
            Assert.AreEqual("Desfazer", Loc.T("booster.undo"));
            Assert.AreEqual("Garrafa Extra", Loc.T("booster.bottle"));
            Assert.AreEqual("Poção Arco-íris", Loc.T("booster.rainbow"));
            Loc.Language = "es";
            Assert.AreEqual("Varita Mágica", Loc.T("booster.wand"));
            Assert.AreEqual("Bola de Cristal", Loc.T("booster.crystal"));
        }

        // ============================================================== Profile & world names

        [Test]
        public void PlayerProfile_AvatarsAndDefault()
        {
            Assert.AreEqual(12, PlayerProfile.Avatars.Length);
            CollectionAssert.AllItemsAreUnique(PlayerProfile.Avatars);
            foreach (string magic in new[] { "luna", "owl", "dragon", "unicorn" })
            {
                CollectionAssert.Contains(PlayerProfile.Avatars, magic);
                Assert.IsTrue(PlayerProfile.IsMagicAvatar(magic), magic);
            }
            Assert.IsFalse(PlayerProfile.IsMagicAvatar("puppy"));
            Assert.AreEqual("luna", PlayerProfile.Avatar);
            Assert.AreEqual("avatar_luna", PlayerProfile.AvatarSprite);
            PlayerProfile.SetAvatar("dragon");
            Assert.AreEqual("dragon", SaveSystem.Data.avatar);
            PlayerProfile.SetAvatar("zebra");
            Assert.AreEqual("dragon", SaveSystem.Data.avatar, "unknown avatars are ignored");
            Assert.AreEqual("fox", PlayerProfile.NormalizeAvatar("avatar_fox"));
            Assert.AreEqual("luna", PlayerProfile.NormalizeAvatar("zebra"));
            Assert.AreEqual("luna", PlayerProfile.NormalizeAvatar(null));
            SaveSystem.Data.avatar = "zebra";   // e.g. a save from another build
            Assert.AreEqual("luna", PlayerProfile.Avatar);
            Assert.AreEqual("luna", SaveSystem.FromJson("{\"level\":3,\"avatar\":\"\"}").avatar);
        }

        [Test]
        public void WorldNames_CycleWithRomanNumerals()
        {
            Assert.AreEqual("II", UI.MetaUI.Roman(2));
            Assert.AreEqual("IV", UI.MetaUI.Roman(4));
            Assert.AreEqual("IX", UI.MetaUI.Roman(9));
            Assert.AreEqual("XIV", UI.MetaUI.Roman(14));
            Assert.AreEqual("MMXXVI", UI.MetaUI.Roman(2026));
            Assert.AreEqual("0", UI.MetaUI.Roman(0));
            Assert.IsTrue(Catalog.LoadFromJson(TestCatalog));
            try
            {
                Loc.AddCsv("key,en,pt,es\narea.a0,Forest,Floresta,Bosque\narea.a1,Caves,Cavernas,Cuevas", "tests");
                Assert.AreEqual("Forest", UI.MetaUI.WorldName(0));
                Assert.AreEqual("Caves", UI.MetaUI.WorldName(1));
                Assert.AreEqual("Forest II", UI.MetaUI.WorldName(2), "themes cycle with a numeral");
                Assert.AreEqual("Caves III", UI.MetaUI.WorldName(5));
                Loc.Language = "pt";
                Assert.AreEqual("Floresta II", UI.MetaUI.WorldName(2));
            }
            finally { Catalog.Reload(); }
        }

        // ============================================================== NameFilter (ranking names, App Store 1.2)

        [Test]
        public void NameFilter_FlagsOffensiveNames()
        {
            string[] offensive =
            {
                // en: case, stretched letters, leetspeak, spaced / dotted letters, split words, camelCase
                "fuck", "FUCK YOU", "Fuuuuck", "f u c k", "F.U.C.K", "fu ck", "Sh1t", "B1tch", "BigAss", "4$$h0l3", "N1gg4",
                "xXFuckXx", "Porn Star",
                // pt (accents too)
                "puta", "PUTA", "p-u-t-a", "P u t 4", "Caralho", "Cárálhô", "cara lho", "P0rr4", "Buceta", "MÉRDA", "M3rd4",
                "Viado", "Filho da puta", "Vai tomar no cu", "SuperPuta", "Cuzão",
                // es
                "Mierda", "M13rd4", "mier da", "Hijo de puta", "Pendejo", "Cabrón", "C@br0n", "Maricón", "Gilipollas",
                "Chingada", "Hijueputa",
            };
            foreach (string name in offensive) Assert.IsTrue(NameFilter.IsOffensive(name), name);
        }

        [Test]
        public void NameFilter_KeepsNormalNames()
        {
            string[] normal =
            {
                "Ana", "Classic", "Assis", "Luna", "Cocada", "Pedro", "Potion Queen", "Analu", "Pupi",
                // roots hidden inside (or across) innocent words
                "Scunthorpe", "Yamashita", "Deputado", "Computador", "Therapist", "Hitchcock", "Cassandra", "Essex", "Sexta",
                "Zora", "Porras", "Carvalho", "Esmeralda", "Kike", "Putin", "Bo Stark", "Mer Dalva", "Ana L", "Cuscuz",
                "Conceição", "Pedro2010", "xXSniperXx", "", null,
            };
            foreach (string name in normal) Assert.IsFalse(NameFilter.IsOffensive(name), name ?? "null");
        }

        [Test]
        public void PlayerProfile_RefusesOffensiveNames()
        {
            Assert.IsTrue(PlayerProfile.SetName("Bia"));
            Assert.IsFalse(PlayerProfile.SetName("P0rr4 Bia"));
            Assert.AreEqual("Bia", SaveSystem.Data.playerName, "a refused name changes nothing");
            Assert.AreEqual("Bia", PlayerProfile.DisplayName);
            // Saved before the filter existed (or restored from the cloud): shown as the default name.
            SaveSystem.Data.playerName = "Fuck";
            Assert.AreEqual(Loc.T("common.player"), PlayerProfile.DisplayName);
        }

        [Test]
        public void Save_BlockedPlayersSurviveTheCloudMerge()
        {
            var local = new PlayerData { blockedUids = new List<string> { "a", "b" } };
            var cloud = new PlayerData { blockedUids = new List<string> { "b", "c" } };
            Assert.IsTrue(SaveSystem.MergeBlockedUids(cloud, local));
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, cloud.blockedUids);
            Assert.IsFalse(SaveSystem.MergeBlockedUids(cloud, local), "nothing new");
            Assert.IsFalse(SaveSystem.MergeBlockedUids(cloud, null));
            PlayerData old = SaveSystem.FromJson("{\"level\":3,\"blockedUids\":null}");
            Assert.IsNotNull(old.blockedUids, "older saves get an empty list");
        }
    }
}
