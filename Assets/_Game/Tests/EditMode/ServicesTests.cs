using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PotionPop.Services;
using UnityEngine;

namespace PotionPop.Tests
{
    /// <summary>
    /// Pure-logic tests of the Services module (no network, no Play Mode). They run on the in-memory save (Loc and the
    /// leaderboard read SaveSystem.Data) in UTC with a fixed clock; global state is restored in TearDown.
    /// </summary>
    public class ServicesTests
    {
        long _savedOffset;
        TimeZoneInfo _savedZone;

        /// <summary>Thursday 2026-10-01 12:00:00 UTC.</summary>
        static readonly long BaseTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        [SetUp]
        public void SetUp()
        {
            _savedOffset = TimeUtil.DebugOffsetSeconds;
            _savedZone = TimeUtil.TimeZoneOverride;
            TimeUtil.TimeZoneOverride = TimeZoneInfo.Utc;
            TimeUtil.ClearZoneCache();
            TimeUtil.DebugOffsetSeconds = BaseTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SaveSystem.UseMemoryStorage(true);
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.UseMemoryStorage(false);
            TimeUtil.DebugOffsetSeconds = _savedOffset;
            TimeUtil.TimeZoneOverride = _savedZone;
            TimeUtil.ClearZoneCache();
        }

        // ======================================================================================== Firebase Auth JSON

        [Serializable]
        class IdpRequestProbe
        {
            public string postBody;
            public string requestUri;
            public bool returnSecureToken;
            public bool returnIdpCredential;
        }

        [Test]
        public void SignInWithIdp_Response_IsParsed()
        {
            const string json = @"{
              ""federatedId"": ""https://accounts.google.com/1234"",
              ""providerId"": ""google.com"",
              ""localId"": ""uid_ABC123"",
              ""emailVerified"": true,
              ""email"": ""mimi@example.com"",
              ""fullName"": ""Mimi Kitten"",
              ""displayName"": ""Mimi K."",
              ""photoUrl"": ""https://lh3.googleusercontent.com/a/photo"",
              ""idToken"": ""eyJhbGciOi.payload.sig"",
              ""refreshToken"": ""AMf-refresh-token"",
              ""expiresIn"": ""3600"",
              ""rawUserInfo"": ""{\""sub\"":\""1234\""}""
            }";
            Assert.IsTrue(FirebaseAuthApi.TryParseSignIn(json, out FirebaseSession s, out string error), error);
            Assert.AreEqual("uid_ABC123", s.uid);
            Assert.AreEqual("eyJhbGciOi.payload.sig", s.idToken);
            Assert.AreEqual("AMf-refresh-token", s.refreshToken);
            Assert.AreEqual(3600, s.expiresInSeconds);
            Assert.AreEqual("Mimi K.", s.displayName);
            Assert.AreEqual("mimi@example.com", s.email);
            Assert.AreEqual("google.com", s.providerId);
            Assert.AreEqual(AuthProvider.Google, FirebaseAuthApi.ProviderFromId(s.providerId));
        }

        [Test]
        public void SignInWithIdp_NameFallsBackToFirstAndLastName()
        {
            const string json = @"{""localId"":""u1"",""idToken"":""t"",""refreshToken"":""r"",""expiresIn"":""60"",""firstName"":""Ana"",""lastName"":""Clara""}";
            Assert.IsTrue(FirebaseAuthApi.TryParseSignIn(json, out FirebaseSession s, out _));
            Assert.AreEqual("Ana Clara", s.displayName);
        }

        [Test]
        public void SignInWithIdp_WithoutTokens_FailsWithFirebaseMessage()
        {
            const string json = @"{""needConfirmation"":true,""errorMessage"":""FEDERATED_USER_ID_ALREADY_LINKED"",""localId"":""u1""}";
            Assert.IsFalse(FirebaseAuthApi.TryParseSignIn(json, out FirebaseSession s, out string error));
            Assert.IsNull(s);
            Assert.AreEqual("FEDERATED_USER_ID_ALREADY_LINKED", error);
            Assert.IsFalse(FirebaseAuthApi.TryParseSignIn("not json", out _, out _));
            Assert.IsFalse(FirebaseAuthApi.TryParseSignIn("", out _, out _));
        }

        [Test]
        public void SecureToken_Refresh_IsParsed()
        {
            const string json = @"{""access_token"":""at"",""expires_in"":""3600"",""token_type"":""Bearer"",""refresh_token"":""new-refresh"",
                ""id_token"":""new-id"",""user_id"":""uid_ABC123"",""project_id"":""1234""}";
            Assert.IsTrue(FirebaseAuthApi.TryParseRefresh(json, out FirebaseSession s));
            Assert.AreEqual("new-id", s.idToken);
            Assert.AreEqual("new-refresh", s.refreshToken);
            Assert.AreEqual("uid_ABC123", s.uid);
            Assert.AreEqual(3600, s.expiresInSeconds);
            Assert.IsFalse(FirebaseAuthApi.TryParseRefresh(@"{""error"":{""code"":400,""message"":""TOKEN_EXPIRED""}}", out _));
        }

        [Test]
        public void FirebaseErrors_MapToLocKeys()
        {
            const string expired = @"{""error"":{""code"":400,""message"":""TOKEN_EXPIRED"",""errors"":[{""message"":""TOKEN_EXPIRED"",""domain"":""global"",""reason"":""invalid""}]}}";
            Assert.AreEqual("TOKEN_EXPIRED", FirebaseAuthApi.ParseErrorMessage(expired));
            Assert.AreEqual(FirebaseAuthApi.ErrSessionExpired, FirebaseAuthApi.ErrorLocKey(new HttpResult { status = 400, text = expired }));
            Assert.IsTrue(FirebaseAuthApi.IsSessionInvalid(new HttpResult { status = 400, text = expired }));

            Assert.AreEqual("INVALID_IDP_RESPONSE", FirebaseAuthApi.ErrorCode("INVALID_IDP_RESPONSE : The supplied auth credential is malformed"));
            Assert.AreEqual(FirebaseAuthApi.ErrFailed, FirebaseAuthApi.ErrorLocKey("INVALID_IDP_RESPONSE : bad"));
            Assert.AreEqual(FirebaseAuthApi.ErrDisabled, FirebaseAuthApi.ErrorLocKey("USER_DISABLED"));
            Assert.AreEqual(FirebaseAuthApi.ErrRelogin, FirebaseAuthApi.ErrorLocKey("CREDENTIAL_TOO_OLD_LOGIN_AGAIN"));
            Assert.AreEqual(FirebaseAuthApi.ErrTooMany, FirebaseAuthApi.ErrorLocKey("TOO_MANY_ATTEMPTS_TRY_LATER : Try again later."));
            Assert.AreEqual(FirebaseAuthApi.ErrUnavailable, FirebaseAuthApi.ErrorLocKey("OPERATION_NOT_ALLOWED : The identity provider configuration is disabled."));
            Assert.AreEqual(FirebaseAuthApi.ErrUnavailable, FirebaseAuthApi.ErrorLocKey("API key not valid. Please pass a valid API key."));
            Assert.AreEqual(FirebaseAuthApi.ErrFailed, FirebaseAuthApi.ErrorLocKey((string)null));

            // Transport problems are network errors and never end the session.
            var offline = HttpResult.Offline("timeout");
            Assert.AreEqual(FirebaseAuthApi.ErrNetwork, FirebaseAuthApi.ErrorLocKey(offline));
            Assert.IsFalse(FirebaseAuthApi.IsSessionInvalid(offline));
            Assert.AreEqual(FirebaseAuthApi.ErrNetwork, FirebaseAuthApi.ErrorLocKey(new HttpResult { status = 503, text = "<html>" }));
            Assert.IsFalse(FirebaseAuthApi.IsSessionInvalid(new HttpResult { status = 503, text = expired }));
        }

        [Test]
        public void SignInWithIdp_RequestBodies()
        {
            var google = JsonUtility.FromJson<IdpRequestProbe>(FirebaseAuthApi.BuildSignInWithIdpBody(AuthProvider.Google, "tok.en-1_2"));
            Assert.AreEqual("id_token=tok.en-1_2&providerId=google.com", google.postBody);
            Assert.AreEqual("http://localhost", google.requestUri);
            Assert.IsTrue(google.returnSecureToken);
            Assert.IsTrue(google.returnIdpCredential);

            var apple = JsonUtility.FromJson<IdpRequestProbe>(FirebaseAuthApi.BuildSignInWithIdpBody(AuthProvider.Apple, "a.b.c", "raw-NONCE_1"));
            Assert.AreEqual("id_token=a.b.c&providerId=apple.com&nonce=raw-NONCE_1", apple.postBody);

            Assert.AreEqual("grant_type=refresh_token&refresh_token=a%2Bb%2Fc%3D", FirebaseAuthApi.BuildRefreshForm("a+b/c="));
            StringAssert.Contains("accounts:signInWithIdp?key=KEY", FirebaseAuthApi.SignInWithIdpUrl("KEY"));
            StringAssert.StartsWith("https://securetoken.googleapis.com/v1/token?key=", FirebaseAuthApi.RefreshUrl("KEY"));
        }

        // ======================================================================================== native payloads

        [Test]
        public void GoogleNativePayload_IsParsed()
        {
            NativeSignInResult ok = NativeSignIn.ParseGooglePayload("OK|eyJ.token.sig");
            Assert.IsTrue(ok.ok);
            Assert.AreEqual("eyJ.token.sig", ok.idToken);

            NativeSignInResult cancelled = NativeSignIn.ParseGooglePayload("ERR|cancelled|User closed the sheet");
            Assert.IsFalse(cancelled.ok);
            Assert.IsTrue(cancelled.cancelled);
            Assert.AreEqual("auth.error.cancelled", cancelled.errorKey);

            NativeSignInResult noAccount = NativeSignIn.ParseGooglePayload("ERR|nocredential|No credentials | available");
            Assert.AreEqual("auth.error.no_account", noAccount.errorKey);
            Assert.AreEqual("No credentials | available", noAccount.detail);

            Assert.AreEqual("auth.error.network", NativeSignIn.ParseGooglePayload("ERR|network|offline").errorKey);
            Assert.AreEqual("auth.error.unavailable", NativeSignIn.ParseGooglePayload("ERR|config|no url scheme").errorKey);
            Assert.AreEqual("auth.error.failed", NativeSignIn.ParseGooglePayload("ERR|weird|x").errorKey);
            Assert.IsFalse(NativeSignIn.ParseGooglePayload("OK|").ok);
            Assert.IsFalse(NativeSignIn.ParseGooglePayload(null).ok);
        }

        // ======================================================================================== Firestore codec

        static PlayerData SamplePlayer()
        {
            var data = new PlayerData
            {
                playerId = "1234567",
                playerName = "Zoë \"Q\" \\ B\nação, ok",
                avatar = "panda",
                level = 42,
                totalStars = 98765432109L,
                coins = 777,
                updatedAt = 1759300000,
            };
            data.cards.Add("cola");
            data.cards.Add("donut");
            return data;
        }

        [Test]
        public void PlayerDoc_EncodeDecode_RoundTrip()
        {
            PlayerData data = SamplePlayer();
            string json = FirestoreCodec.EncodePlayerDoc(data, 1759300123);
            StringAssert.Contains("\"integerValue\":\"42\"", json);              // 64-bit ints travel as strings
            StringAssert.Contains("\"integerValue\":\"98765432109\"", json);

            Assert.IsTrue(FirestoreCodec.TryDecodePlayerDoc(json, out CloudPlayerDoc doc));
            Assert.AreEqual(42, doc.level);
            Assert.AreEqual(98765432109L, doc.stars);
            Assert.AreEqual("panda", doc.avatar);
            Assert.AreEqual(1759300123, doc.updatedAt);
            Assert.AreEqual("Zoë \"Q\" \\ B\nação, ok", doc.name);   // ≤ 24 chars: kept as is (quotes, backslash, newline, accents)
            Assert.IsNotNull(doc.data);
            Assert.AreEqual("1234567", doc.data.playerId);
            Assert.AreEqual(data.playerName, doc.data.playerName);
            Assert.AreEqual(777, doc.data.coins);
            CollectionAssert.AreEqual(new[] { "cola", "donut" }, doc.data.cards);
        }

        [Test]
        public void PlayerDoc_NameIsClippedForTheRules()
        {
            var data = new PlayerData { playerName = new string('x', 40), avatar = "fox" };
            Assert.IsTrue(FirestoreCodec.TryDecodePlayerDoc(FirestoreCodec.EncodePlayerDoc(data, 1), out CloudPlayerDoc doc));
            Assert.AreEqual(FirestoreCodec.MaxNameLength, doc.name.Length);
            Assert.AreEqual(new string('x', 40), doc.data.playerName);   // the save itself is untouched
        }

        [Test]
        public void PlayerDoc_DecodesFirestoreGetResponse()
        {
            string save = JsonUtility.ToJson(new PlayerData { level = 7, totalStars = 321, playerName = "Bia" });
            string escaped = save.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string json = "{\n  \"name\": \"projects/potion-pop/databases/(default)/documents/players/uid_XYZ\",\n" +
                          "  \"fields\": {\n" +
                          "    \"save\": { \"stringValue\": \"" + escaped + "\" },\n" +
                          "    \"level\": { \"integerValue\": \"7\" },\n" +
                          "    \"stars\": { \"integerValue\": \"321\" },\n" +
                          "    \"name\": { \"stringValue\": \"Bia\" },\n" +
                          "    \"avatar\": { \"stringValue\": \"bunny\" },\n" +
                          "    \"updatedAt\": { \"integerValue\": \"1759300000\" }\n" +
                          "  },\n  \"createTime\": \"2026-10-01T12:00:00.000000Z\",\n  \"updateTime\": \"2026-10-02T12:00:00.000000Z\"\n}";
            Assert.IsTrue(FirestoreCodec.TryDecodePlayerDoc(json, out CloudPlayerDoc doc));
            Assert.AreEqual("uid_XYZ", doc.uid);
            Assert.AreEqual(7, doc.level);
            Assert.AreEqual(321, doc.stars);
            Assert.AreEqual("bunny", doc.avatar);
            Assert.AreEqual(7, doc.data.level);
            Assert.AreEqual("Bia", doc.data.playerName);
        }

        [Test]
        public void PlayerDoc_CorruptSaveDecodesWithoutData()
        {
            const string json = @"{""name"":""projects/p/databases/(default)/documents/players/u"",""fields"":{""save"":{""stringValue"":""oops""},""level"":{""integerValue"":""3""}}}";
            Assert.IsTrue(FirestoreCodec.TryDecodePlayerDoc(json, out CloudPlayerDoc doc));
            Assert.AreEqual(3, doc.level);
            Assert.IsNull(doc.data);
            Assert.IsFalse(FirestoreCodec.TryDecodePlayerDoc("[]", out _));
            Assert.IsFalse(FirestoreCodec.TryDecodePlayerDoc(@"{""error"":{""code"":404}}", out _));
        }

        [Test]
        public void RunQuery_And_Count_Responses_AreDecoded()
        {
            const string query = @"[
              {""document"":{""name"":""projects/p/databases/(default)/documents/players/a"",""fields"":{""name"":{""stringValue"":""Ana""},""avatar"":{""stringValue"":""fox""},""stars"":{""integerValue"":""900""},""level"":{""integerValue"":""30""}}},""readTime"":""2026-10-02T00:00:00Z""},
              {""document"":{""name"":""projects/p/databases/(default)/documents/players/b"",""fields"":{""name"":{""stringValue"":""Bo""},""stars"":{""integerValue"":""12""}}},""readTime"":""2026-10-02T00:00:00Z""},
              {""readTime"":""2026-10-02T00:00:00Z""}
            ]";
            List<CloudPlayerDoc> docs = FirestoreCodec.DecodeQueryResponse(query);
            Assert.AreEqual(2, docs.Count);
            Assert.AreEqual("a", docs[0].uid);
            Assert.AreEqual("Ana", docs[0].name);
            Assert.AreEqual(900, docs[0].stars);
            Assert.AreEqual(30, docs[0].level);
            Assert.AreEqual("b", docs[1].uid);
            Assert.AreEqual(12, docs[1].stars);
            Assert.AreEqual(0, FirestoreCodec.DecodeQueryResponse(@"[{""readTime"":""x""}]").Count);
            Assert.AreEqual(0, FirestoreCodec.DecodeQueryResponse("garbage").Count);

            Assert.AreEqual(1234, FirestoreCodec.DecodeCountResponse(@"[{""result"":{""aggregateFields"":{""n"":{""integerValue"":""1234""}}},""readTime"":""x""}]"));
            Assert.AreEqual(-1, FirestoreCodec.DecodeCountResponse(@"{""error"":{}}"));

            StringAssert.Contains("\"limit\":50", FirestoreCodec.BuildTopPlayersQuery(50));
            StringAssert.Contains("\"direction\":\"DESCENDING\"", FirestoreCodec.BuildTopPlayersQuery(50));
            StringAssert.Contains("\"integerValue\":\"99\"", FirestoreCodec.BuildCountAboveQuery(99));
            Assert.AreEqual("https://firestore.googleapis.com/v1/projects/potion-pop/databases/(default)/documents/players/u1",
                FirestoreCodec.PlayerDocUrl("potion-pop", "u1"));
        }

        // ======================================================================================== leaderboards

        [Test]
        public void Weekly_IsDeterministic_SortedAndRanked()
        {
            List<LeaderboardEntry> a = LeaderboardService.BuildWeekly(2960, 0.5, 1200, "Me", "avatar_fox");
            List<LeaderboardEntry> b = LeaderboardService.BuildWeekly(2960, 0.5, 1200, "Me", "avatar_fox");
            Assert.AreEqual(LeaderboardSim.BotCount + 1, a.Count);
            int players = 0;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(i + 1, a[i].rank);
                Assert.AreEqual(a[i].name, b[i].name);
                Assert.AreEqual(a[i].score, b[i].score);
                Assert.AreEqual(a[i].avatar, b[i].avatar);
                StringAssert.StartsWith("avatar_", a[i].avatar);
                if (i > 0) Assert.GreaterOrEqual(a[i - 1].score, a[i].score);
                if (a[i].isPlayer)
                {
                    players++;
                    Assert.AreEqual(1200, a[i].score);
                    Assert.AreEqual("Me", a[i].name);
                }
            }
            Assert.AreEqual(1, players);

            // Another week brings other rivals/scores.
            List<LeaderboardEntry> other = LeaderboardService.BuildWeekly(2961, 0.5, 1200, "Me", "avatar_fox");
            bool different = false;
            for (int i = 0; i < other.Count && !different; i++) different = other[i].name != a[i].name || other[i].score != a[i].score;
            Assert.IsTrue(different);
        }

        [Test]
        public void Weekly_BotsGrowDuringTheWeek()
        {
            for (int slot = 0; slot < LeaderboardSim.BotCount; slot++)
            {
                Assert.AreEqual(0, LeaderboardSim.WeeklyBotScore(77, slot, 0.0), "everyone starts the week at 0");
                long previous = 0;
                for (int step = 1; step <= 20; step++)
                {
                    long score = LeaderboardSim.WeeklyBotScore(77, slot, step / 20.0);
                    Assert.GreaterOrEqual(score, previous, "scores never go down");
                    previous = score;
                }
                Assert.Greater(previous, 0, "every bot plays by the end of the week");
            }
            // Same hour → same score (hourly steps).
            Assert.AreEqual(LeaderboardSim.WeeklyBotScore(77, 3, 0.5001), LeaderboardSim.WeeklyBotScore(77, 3, 0.5002));
        }

        [Test]
        public void Weekly_PlayerWinsTiesAndRanksFirstWithHugeScore()
        {
            List<LeaderboardEntry> top = LeaderboardService.BuildWeekly(5, 1.0, long.MaxValue / 2, "", "");
            Assert.IsTrue(top[0].isPlayer);
            Assert.AreEqual(1, top[0].rank);
            Assert.AreEqual("avatar_puppy", top[0].avatar);

            List<LeaderboardEntry> zero = LeaderboardService.BuildWeekly(5, 0.0, 0, "Me", "avatar_duck");
            // Week start: all bots at 0, the player wins the tie.
            Assert.IsTrue(zero[0].isPlayer);
        }

        [Test]
        public void SimulatedGlobal_IncludesThePlayerAndGrowsDaily()
        {
            List<LeaderboardEntry> day0 = LeaderboardService.BuildSimulatedGlobal(LeaderboardSim.GlobalEpochDay, 5000, "Me", "avatar_bear");
            List<LeaderboardEntry> day30 = LeaderboardService.BuildSimulatedGlobal(LeaderboardSim.GlobalEpochDay + 30, 5000, "Me", "avatar_bear");
            Assert.AreEqual(50, day0.Count);
            Assert.AreEqual(1, day0.FindAll(e => e.isPlayer).Count);
            long total0 = 0, total30 = 0;
            foreach (LeaderboardEntry e in day0) if (!e.isPlayer) total0 += e.score;
            foreach (LeaderboardEntry e in day30) if (!e.isPlayer) total30 += e.score;
            Assert.Greater(total30, total0);
            for (int i = 1; i < day0.Count; i++) Assert.GreaterOrEqual(day0[i - 1].score, day0[i].score);
        }

        [Test]
        public void WeekClock_MondayBased()
        {
            // 1970-01-05 00:00 UTC was a Monday: start of week 0.
            LeaderboardSim.WeekClock(4 * 86400, 0, out int week, out double fraction, out long left);
            Assert.AreEqual(0, week);
            Assert.AreEqual(0.0, fraction, 1e-9);
            Assert.AreEqual(7 * 86400, left);

            // 2026-10-01 is a Thursday (12:00 UTC): 3.5 days into its week.
            long thursdayNoon = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            LeaderboardSim.WeekClock(thursdayNoon, 0, out int w, out double f, out long l);
            Assert.AreEqual(3.5 / 7.0, f, 1e-9);
            Assert.AreEqual((long)(3.5 * 86400), l);
            LeaderboardSim.WeekClock(thursdayNoon + l, 0, out int next, out double f2, out _);
            Assert.AreEqual(w + 1, next);
            Assert.AreEqual(0.0, f2, 1e-9);

            // A UTC-3 player is still on Sunday when UTC is already Monday 01:00.
            long mondayOneUtc = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            LeaderboardSim.WeekClock(mondayOneUtc, -3 * 3600, out int brWeek, out _, out long brLeft);
            Assert.AreEqual(w, brWeek);
            Assert.AreEqual(2 * 3600, brLeft);
        }

        [Test]
        public void Weekly_UsesTheGameClock()
        {
            // Same week definition as Economy.EnsureWeek: the player's weekly stars count only for the current week.
            PlayerData data = SaveSystem.Data;
            data.weekId = TimeUtil.WeekIndex;
            data.weeklyStars = 4321;
            data.playerName = "Bia";
            List<LeaderboardEntry> list = null;
            LeaderboardService.GetWeekly(l => list = l);
            Assert.IsNotNull(list);
            LeaderboardEntry me = list.Find(e => e.isPlayer);
            Assert.AreEqual(4321, me.score);
            Assert.AreEqual("Bia", me.name);

            // Thursday noon (UTC): 3.5 days left, rivals sampled at mid-week (the real clock may tick meanwhile).
            Assert.AreEqual(3.5 * 86400, LeaderboardService.WeeklyResetSeconds, 5.0);
            Assert.AreEqual(0.5, LeaderboardSim.WeekFraction(LeaderboardService.WeeklyResetSeconds), 1e-4);
            List<LeaderboardEntry> expected = LeaderboardService.BuildWeekly(TimeUtil.WeekIndex, 0.5, 4321, "Bia", "avatar_puppy");
            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual(expected[i].name, list[i].name);
                Assert.AreEqual(expected[i].score, list[i].score);
            }

            data.weekId = TimeUtil.WeekIndex - 1;   // stale counter from last week
            LeaderboardService.GetWeekly(l => list = l);
            Assert.AreEqual(0, list.Find(e => e.isPlayer).score);

            Assert.AreEqual(0.0, LeaderboardSim.WeekFraction(7 * 86400), 1e-9);
            Assert.AreEqual(1.0, LeaderboardSim.WeekFraction(0), 1e-9);
            Assert.AreEqual(0.0, LeaderboardSim.WeekFraction(169 * 3600), 1e-9, "DST week (169 h) clamps");
        }

        [Test]
        public void AvatarSprite_Normalizes()
        {
            Assert.AreEqual("avatar_fox", LeaderboardService.AvatarSprite("fox"));
            Assert.AreEqual("avatar_fox", LeaderboardService.AvatarSprite("avatar_fox"));
            Assert.AreEqual("avatar_puppy", LeaderboardService.AvatarSprite("dragon"));
            Assert.AreEqual("avatar_puppy", LeaderboardService.AvatarSprite(null));
        }

        // ======================================================================================== ranking moderation

        [Test]
        public void Global_OtherPlayersNamesAreSanitizedAndFiltered()
        {
            Assert.AreEqual(Loc.T("lb.player"), LeaderboardService.OtherPlayerName("Hijo de puta"));
            Assert.AreEqual(Loc.T("lb.player"), LeaderboardService.OtherPlayerName(""));
            Assert.AreEqual(Loc.T("lb.player"), LeaderboardService.OtherPlayerName(null));
            Assert.AreEqual("Bia", LeaderboardService.OtherPlayerName("  Bia "));
            string tagged = LeaderboardService.OtherPlayerName("<size=300>Bia</size>");
            Assert.IsFalse(tagged.Contains("<") || tagged.Contains(">"), "no rich-text tags from other players: " + tagged);
            Assert.LessOrEqual(LeaderboardService.OtherPlayerName(new string('x', 24)).Length, PlayerProfile.MaxNameLength);
        }

        [Test]
        public void Global_BlockedPlayersAreHiddenAndRanksClose()
        {
            var list = new List<LeaderboardEntry>
            {
                new LeaderboardEntry { rank = 1, name = "A", uid = "a" },
                new LeaderboardEntry { rank = 2, name = "B", uid = "b" },
                new LeaderboardEntry { rank = 3, name = "Me", isPlayer = true },
                new LeaderboardEntry { rank = 4, name = "C", uid = "c" },
            };
            LeaderboardService.RemoveBlocked(list, new HashSet<string> { "b", "zz" });
            CollectionAssert.AreEqual(new[] { "A", "Me", "C" }, list.ConvertAll(e => e.name));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, list.ConvertAll(e => e.rank));

            // The player outside the top keeps the count-query rank minus the hidden rows ahead; unknown stays 0.
            var outside = new List<LeaderboardEntry>
            {
                new LeaderboardEntry { rank = 1, name = "A", uid = "a" },
                new LeaderboardEntry { rank = 2, name = "B", uid = "b" },
                new LeaderboardEntry { rank = 87, name = "Me", isPlayer = true },
                new LeaderboardEntry { rank = 0, name = "?", uid = "q" },
            };
            LeaderboardService.RemoveBlocked(outside, new[] { "a", "b" });
            CollectionAssert.AreEqual(new[] { 85, 0 }, outside.ConvertAll(e => e.rank));

            // Only other real players can be moderated: never the player's row nor simulated rivals (no uid).
            Assert.IsTrue(LeaderboardService.CanModerate(new LeaderboardEntry { uid = "x" }));
            Assert.IsFalse(LeaderboardService.CanModerate(new LeaderboardEntry { uid = "x", isPlayer = true }));
            Assert.IsFalse(LeaderboardService.CanModerate(new LeaderboardEntry { name = "Bot" }));

            LeaderboardService.Block("u1");
            LeaderboardService.Block("u1");
            Assert.IsTrue(LeaderboardService.IsBlocked("u1"));
            Assert.IsFalse(LeaderboardService.IsBlocked("u2"));
            CollectionAssert.AreEqual(new[] { "u1" }, SaveSystem.Data.blockedUids);
        }

        [Test]
        public void ReportDoc_And_PublicRankingName_AreEncoded()
        {
            string json = FirestoreCodec.EncodeReportDoc("me", "them", new string('n', 40), 1759300000);
            StringAssert.Contains("\"reporter\":{\"stringValue\":\"me\"}", json);
            StringAssert.Contains("\"target\":{\"stringValue\":\"them\"}", json);
            StringAssert.Contains("\"name\":{\"stringValue\":\"" + new string('n', FirestoreCodec.MaxNameLength) + "\"}", json);
            StringAssert.Contains("\"createdAt\":{\"integerValue\":\"1759300000\"}", json);
            Assert.AreEqual("https://firestore.googleapis.com/v1/projects/potion-pop/databases/(default)/documents/reports",
                FirestoreCodec.ReportsUrl("potion-pop"));

            // The public ranking row never carries a refused name (one saved before the filter existed).
            StringAssert.Contains("\"name\":{\"stringValue\":\"\"}", FirestoreCodec.EncodeLeaderboardDoc(new PlayerData { playerName = "M3rd4" }, 1));
            StringAssert.Contains("\"name\":{\"stringValue\":\"Bia\"}", FirestoreCodec.EncodeLeaderboardDoc(new PlayerData { playerName = "Bia" }, 1));
        }

        // ======================================================================================== interstitial rules

        [Test]
        public void Interstitial_FrequencyRules()
        {
            const long now = 1_000_000;
            // Allowed: level ≥ 6, 2 levels since the last one, > 90 s, no recent rewarded.
            Assert.IsTrue(AdRules.ShouldShowInterstitial(6, 2, 0, 0, now));
            Assert.IsTrue(AdRules.ShouldShowInterstitial(20, 5, now - 91, now - 31, now));
            Assert.IsTrue(AdRules.ShouldShowInterstitial(6, 2, now - 90, now - 30, now), "boundaries are inclusive");

            Assert.IsFalse(AdRules.ShouldShowInterstitial(5, 10, 0, 0, now), "never before level 6");
            Assert.IsFalse(AdRules.ShouldShowInterstitial(30, 1, 0, 0, now), "at most every 2 levels");
            Assert.IsFalse(AdRules.ShouldShowInterstitial(30, 2, now - 89, 0, now), "at least 90 s apart");
            Assert.IsFalse(AdRules.ShouldShowInterstitial(30, 2, 0, now - 29, now), "not right after a rewarded ad");

            // A clock moved backwards (timestamps in the future) must not block ads for hours.
            Assert.IsTrue(AdRules.ShouldShowInterstitial(30, 2, now + 5000, now + 5000, now));
            Assert.IsNull(AdRules.WhyNoInterstitial(30, 2, 0, 0, now));
            Assert.IsNotNull(AdRules.WhyNoInterstitial(1, 2, 0, 0, now));
        }

        [Test]
        public void Interstitial_SimulatedSessionCadence()
        {
            // Play levels 1..20, 3 minutes each, every level counted: interstitials after 6, 8, 10, ...
            int since = 0;
            long last = 0, t = 10_000;
            var shownAfter = new List<int>();
            for (int level = 1; level <= 14; level++)
            {
                t += 180;
                since++;
                if (AdRules.ShouldShowInterstitial(level, since, last, 0, t))
                {
                    shownAfter.Add(level);
                    since = 0;
                    last = t;
                }
            }
            CollectionAssert.AreEqual(new[] { 6, 8, 10, 12, 14 }, shownAfter);
        }

        [Test]
        public void Backoff_Grows_And_Caps()
        {
            Assert.AreEqual(0f, AdRules.BackoffSeconds(0, 4, 120));
            Assert.AreEqual(4f, AdRules.BackoffSeconds(1, 4, 120));
            Assert.AreEqual(8f, AdRules.BackoffSeconds(2, 4, 120));
            Assert.AreEqual(120f, AdRules.BackoffSeconds(30, 4, 120));
        }

        // ======================================================================================== nonce / JWT

        [Test]
        public void Nonce_Sha256Hex_MatchesKnownVectors()
        {
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Nonce.Sha256Hex("abc"));
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Nonce.Sha256Hex(""));
        }

        [Test]
        public void Nonce_Generate_IsRandomAndUrlSafe()
        {
            string a = Nonce.Generate(32), b = Nonce.Generate(32);
            Assert.AreEqual(32, a.Length);
            Assert.AreNotEqual(a, b);
            foreach (char c in a) Assert.IsTrue(char.IsLetterOrDigit(c) || c == '-' || c == '_', "unexpected char " + c);
            Assert.AreEqual(64, Nonce.Sha256Hex(a).Length);
            Assert.AreEqual("", Nonce.Generate(0));
        }

        [Test]
        public void Jwt_ClaimsAreDecoded()
        {
            string header = Base64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
            string payload = Base64Url("{\"auth_time\":1759300000,\"iat\":1759300100,\"exp\":1759303700,\"user_id\":\"uid_1\",\"email\":\"m@x.com\"}");
            Assert.IsTrue(Jwt.TryReadClaims(header + "." + payload + ".signature", out JwtClaims claims));
            Assert.AreEqual(1759300000, claims.auth_time);
            Assert.AreEqual(1759303700, claims.exp);
            Assert.AreEqual("uid_1", claims.user_id);
            Assert.IsFalse(Jwt.TryReadClaims("not-a-jwt", out _));
            Assert.IsFalse(Jwt.TryReadClaims(null, out _));
        }

        static string Base64Url(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // ======================================================================================== misc

        [Test]
        public void MockUser_IsStableAndMarked()
        {
            AuthUser a = MockAuthProvider.CreateUser(AuthProvider.Apple);
            AuthUser b = MockAuthProvider.CreateUser(AuthProvider.Google);
            Assert.AreEqual(a.uid, b.uid);
            StringAssert.StartsWith("mock-", a.uid);
            Assert.AreEqual("Mimi Fan", a.displayName);
            Assert.IsTrue(a.isMock);
            Assert.AreEqual(AuthProvider.Apple, a.provider);
            Assert.AreEqual(AuthProvider.Mock, MockAuthProvider.CreateUser(AuthProvider.None).provider);
        }

        [Test]
        public void ServicesConfig_DefaultsAreOfflineWithTestAds()
        {
            ServicesConfig config = ScriptableObject.CreateInstance<ServicesConfig>();
            try
            {
                Assert.IsFalse(config.FirebaseConfigured);
                StringAssert.StartsWith("ca-app-pub-3940256099942544/", config.RewardedUnit);
                StringAssert.StartsWith("ca-app-pub-3940256099942544/", config.InterstitialUnit);
                StringAssert.StartsWith("ca-app-pub-3940256099942544/", config.BannerUnit);
                config.firebaseApiKey = "k";
                config.firebaseProjectId = "p";
                Assert.IsTrue(config.FirebaseConfigured);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PlacementKeys_AreDottedLowerCase()
        {
            foreach (AdPlacement p in Enum.GetValues(typeof(AdPlacement)))
            {
                string key = AdsService.PlacementLocKey(p);
                StringAssert.StartsWith("ads.placement.", key);
                Assert.AreEqual(key.ToLowerInvariant(), key);
            }
        }
    }
}
