using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.Services
{
    public sealed class LeaderboardEntry
    {
        public int rank;          // 1-based; 0 = unknown (player outside the online top and the count query failed)
        public string name;
        public string avatar;     // avatar_<name>
        public string team;       // subtitle (area name / country), may be empty
        public long score;
        public bool isPlayer;
        /// <summary>Firebase uid of another real player (online global ranking): the row can be reported / blocked.
        /// Null for simulated rivals and for the player.</summary>
        public string uid;
        /// <summary>That player's name as stored in leaderboard/{uid}, before sanitizing / filtering (sent with a report).</summary>
        public string rawName;
    }

    /// <summary>
    /// Weekly contest (49 simulated players seeded by week, growing during the week, plus the player's weekly stars)
    /// and global (Firestore top 50 by total stars when online, otherwise simulated). Callbacks run on the main thread.
    /// Real players' names are sanitized and filtered (<see cref="NameFilter"/>), and their rows can be reported or
    /// blocked (App Store guideline 1.2): blocked players never show up in the global ranking again.
    /// </summary>
    public static class LeaderboardService
    {
        public const int GlobalLimit = 50;
        const float GlobalCacheSeconds = 60f;

        static List<LeaderboardEntry> _globalCache;
        static float _globalCacheAt = -1000f;
        /// <summary>uid|stars|name|avatar|language the cache was built for: any change of the player's own row (or of the
        /// language) refetches.</summary>
        static string _globalCacheKey;
        static bool _globalLoading;
        static readonly List<Action<List<LeaderboardEntry>, bool>> GlobalWaiters = new List<Action<List<LeaderboardEntry>, bool>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _globalCache = null;
            _globalCacheAt = -1000f;
            _globalCacheKey = null;
            _globalLoading = false;
            GlobalWaiters.Clear();
        }

        // ---------------------------------------------------------------------------------------- weekly

        public static void GetWeekly(Action<List<LeaderboardEntry>> done)
        {
            // Same clock as Economy.EnsureWeek (TimeUtil: local Monday-based weeks, debug offset, cached zone offset),
            // so the rivals and the player's weeklyStars always belong to the same week.
            int weekId = TimeUtil.WeekIndex;
            double fraction = LeaderboardSim.WeekFraction(TimeUtil.SecondsToNextWeek);
            PlayerData data = SaveSystem.Data;
            // ">=": while the local week is temporarily behind the save's (time zone moved back) the stars still count.
            long playerScore = data.weekId >= weekId ? Math.Max(0, data.weeklyStars) : 0;
            List<LeaderboardEntry> list = BuildWeekly(weekId, fraction, playerScore, PlayerName(data), AvatarSprite(data.avatar), PlayerTeam(data));
            ServicesRunner.SafeInvoke(done, list);
        }

        /// <summary>Seconds until the weekly contest resets (next Monday 00:00, local time). Cheap: fine every frame.</summary>
        public static long WeeklyResetSeconds => TimeUtil.SecondsToNextWeek;

        /// <summary>Weekly ranking: the week's 49 rivals + the player, sorted by score and ranked 1..50.</summary>
        public static List<LeaderboardEntry> BuildWeekly(int weekId, double weekFraction, long playerScore, string playerName,
            string playerAvatar, string playerTeam = "")
        {
            List<LeaderboardSim.Bot> bots = LeaderboardSim.WeeklyBots(weekId, weekFraction);
            return BuildFromBots(bots, playerScore, playerName, playerAvatar, playerTeam);
        }

        // ---------------------------------------------------------------------------------------- global

        /// <summary>done(entries, online)</summary>
        public static void GetGlobal(Action<List<LeaderboardEntry>, bool> done)
        {
            if (!AuthService.CloudAvailable || !AuthService.IsSignedIn)
            {
                ServicesRunner.SafeInvoke(done, SimulatedGlobal(), false);
                return;
            }
            string uid = AuthService.User.uid;
            string key = CacheKey(uid);
            if (_globalCache != null && _globalCacheKey == key && Time.realtimeSinceStartup - _globalCacheAt < GlobalCacheSeconds)
            {
                ServicesRunner.SafeInvoke(done, Visible(_globalCache), true);
                return;
            }
            if (done != null) GlobalWaiters.Add(done);
            if (_globalLoading) return;
            _globalLoading = true;
            FetchOnlineGlobal(uid, (list, online) =>
            {
                _globalLoading = false;
                if (online)
                {
                    _globalCache = list;
                    _globalCacheAt = Time.realtimeSinceStartup;
                    _globalCacheKey = key;
                }
                Action<List<LeaderboardEntry>, bool>[] waiters = GlobalWaiters.ToArray();
                GlobalWaiters.Clear();
                foreach (Action<List<LeaderboardEntry>, bool> w in waiters) ServicesRunner.SafeInvoke(w, Visible(list), online);
            });
        }

        /// <summary>Drops the cached online ranking (it is also refetched when the player's stars/name/avatar change).</summary>
        public static void InvalidateGlobalCache() => _globalCache = null;

        static string CacheKey(string uid)
        {
            PlayerData data = SaveSystem.Data;
            // The language is part of the key: rows carry localized area names / fallback names baked in at fetch time.
            return uid + "|" + data.totalStars + "|" + PlayerName(data) + "|" + data.avatar + "|" + Loc.Language;
        }

        static void FetchOnlineGlobal(string uid, Action<List<LeaderboardEntry>, bool> done)
        {
            FirestoreClient.TopPlayers(GlobalLimit, (ok, docs) =>
            {
                if (!ok || docs == null)
                {
                    done(SimulatedGlobal(), false);
                    return;
                }
                PlayerData data = SaveSystem.Data;
                long localStars = Math.Max(0, data.totalStars);
                var list = new List<LeaderboardEntry>(docs.Count + 1);
                LeaderboardEntry player = null;
                foreach (CloudPlayerDoc doc in docs)
                {
                    bool isPlayer = doc.uid == uid;
                    var e = new LeaderboardEntry
                    {
                        name = isPlayer ? PlayerName(data) : OtherPlayerName(doc.name),
                        avatar = AvatarSprite(isPlayer ? data.avatar : doc.avatar),
                        team = AreaName(isPlayer ? data.level : doc.level),
                        score = isPlayer ? Math.Max(doc.stars, localStars) : doc.stars,
                        isPlayer = isPlayer,
                        uid = isPlayer || string.IsNullOrEmpty(doc.uid) ? null : doc.uid,
                        rawName = isPlayer ? null : doc.name ?? "",
                    };
                    if (isPlayer) player = e;
                    list.Add(e);
                }

                if (player != null)
                {
                    SortAndRank(list);
                    done(list, true);
                    return;
                }

                // The player is not in the top: append them and ask Firestore how many players are ahead.
                player = new LeaderboardEntry
                {
                    name = PlayerName(data),
                    avatar = AvatarSprite(data.avatar),
                    team = PlayerTeam(data),
                    score = localStars,
                    isPlayer = true,
                };
                list.Add(player);
                SortAndRank(list);
                if (player.rank <= GlobalLimit)
                {
                    // Local stars (not uploaded yet) already place the player inside the top: keep exactly the top 50.
                    if (list.Count > GlobalLimit) list.RemoveRange(GlobalLimit, list.Count - GlobalLimit);
                    done(list, true);
                    return;
                }
                // Top 50 + the player in 51st position, with the real rank from a count query.
                FirestoreClient.CountAbove(localStars, ahead =>
                {
                    player.rank = ahead >= 0 ? (int)Math.Max(GlobalLimit + 1, Math.Min(int.MaxValue, ahead + 1)) : 0;
                    done(list, true);
                });
            });
        }

        static List<LeaderboardEntry> SimulatedGlobal()
        {
            PlayerData data = SaveSystem.Data;
            return BuildSimulatedGlobal(TimeUtil.Today, Math.Max(0, data.totalStars), PlayerName(data), AvatarSprite(data.avatar), PlayerTeam(data));
        }

        /// <summary>Offline global ranking: 49 simulated all-time totals + the player's total stars, ranked.</summary>
        public static List<LeaderboardEntry> BuildSimulatedGlobal(int dayIndex, long playerStars, string playerName, string playerAvatar,
            string playerTeam = "")
        {
            return BuildFromBots(LeaderboardSim.GlobalBots(dayIndex), playerStars, playerName, playerAvatar, playerTeam);
        }

        /// <summary>
        /// Another player's name from leaderboard/{uid}, as shown: control characters, rich-text tags and glyphs our font
        /// can't draw (emoji, CJK) removed, length capped; empty or offensive → the localized default name.
        /// </summary>
        public static string OtherPlayerName(string raw)
        {
            string name = PlayerProfile.Sanitize(raw);
            return string.IsNullOrEmpty(name) || NameFilter.IsOffensive(name) ? Loc.T("lb.player") : name;
        }

        // ---------------------------------------------------------------------------------------- moderation

        /// <summary>Most blocked players kept in the save (the oldest are dropped first).</summary>
        public const int MaxBlocked = 200;

        /// <summary>A real player of the online ranking other than the player: the row can be reported / blocked.</summary>
        public static bool CanModerate(LeaderboardEntry e) => e != null && !e.isPlayer && !string.IsNullOrEmpty(e.uid);

        public static bool IsBlocked(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            List<string> blocked = SaveSystem.Data.blockedUids;
            return blocked != null && blocked.Contains(uid);
        }

        /// <summary>Hides a player from the global ranking for good (saved, and synced with the cloud save).</summary>
        public static void Block(string uid)
        {
            if (string.IsNullOrEmpty(uid) || IsBlocked(uid)) return;
            PlayerData data = SaveSystem.Data;
            data.blockedUids ??= new List<string>();
            data.blockedUids.Add(uid);
            if (data.blockedUids.Count > MaxBlocked) data.blockedUids.RemoveRange(0, data.blockedUids.Count - MaxBlocked);
            SaveSystem.MarkDirty();
        }

        /// <summary>
        /// Reports a row's name for review — reports/{autoId} = { reporter, target, name, createdAt } — and blocks the
        /// player right away. done(sent): false when the report could not be written (offline, signed out...).
        /// </summary>
        public static void Report(LeaderboardEntry e, Action<bool> done)
        {
            if (!CanModerate(e))
            {
                ServicesRunner.SafeInvoke(done, false);
                return;
            }
            Block(e.uid);
            if (!AuthService.CloudAvailable || !AuthService.IsSignedIn)
            {
                ServicesRunner.SafeInvoke(done, false);
                return;
            }
            FirestoreClient.CreateReport(AuthService.User.uid, e.uid, e.rawName ?? e.name, done);
        }

        /// <summary>Copy of a ranking without the players the player blocked.</summary>
        static List<LeaderboardEntry> Visible(List<LeaderboardEntry> source)
        {
            List<LeaderboardEntry> copy = Copy(source);
            List<string> blocked = SaveSystem.Data.blockedUids;
            return blocked == null || blocked.Count == 0 ? copy : RemoveBlocked(copy, blocked);
        }

        /// <summary>
        /// Drops (in place) the rows of the <paramref name="blocked"/> uids and closes the gaps: every rank below a hidden
        /// row moves up by one (unknown ranks stay 0). Returns the list.
        /// </summary>
        public static List<LeaderboardEntry> RemoveBlocked(List<LeaderboardEntry> list, ICollection<string> blocked)
        {
            if (list == null || blocked == null || blocked.Count == 0) return list;
            var hiddenRanks = new List<int>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                LeaderboardEntry e = list[i];
                if (!CanModerate(e) || !blocked.Contains(e.uid)) continue;
                if (e.rank > 0) hiddenRanks.Add(e.rank);
                list.RemoveAt(i);
            }
            if (hiddenRanks.Count == 0) return list;
            foreach (LeaderboardEntry e in list)
            {
                if (e.rank <= 0) continue;
                int above = 0;
                foreach (int r in hiddenRanks)
                    if (r < e.rank) above++;
                e.rank -= above;
            }
            return list;
        }

        // ---------------------------------------------------------------------------------------- shared

        static List<LeaderboardEntry> BuildFromBots(List<LeaderboardSim.Bot> bots, long playerScore, string playerName, string playerAvatar, string playerTeam)
        {
            var list = new List<LeaderboardEntry>(bots.Count + 1);
            foreach (LeaderboardSim.Bot b in bots)
            {
                list.Add(new LeaderboardEntry
                {
                    name = b.name,
                    avatar = "avatar_" + b.avatarId,
                    team = Loc.T("lb.country." + b.country),
                    score = b.score,
                });
            }
            list.Add(new LeaderboardEntry
            {
                name = string.IsNullOrWhiteSpace(playerName) ? Loc.T("lb.you") : playerName,
                avatar = string.IsNullOrEmpty(playerAvatar) ? "avatar_puppy" : playerAvatar,
                team = playerTeam ?? "",
                score = Math.Max(0, playerScore),
                isPlayer = true,
            });
            SortAndRank(list);
            return list;
        }

        /// <summary>Sorts by score (desc; the player wins ties, then name) and assigns ranks 1..N.</summary>
        public static void SortAndRank(List<LeaderboardEntry> list)
        {
            list.Sort(CompareEntries);
            for (int i = 0; i < list.Count; i++) list[i].rank = i + 1;
        }

        static int CompareEntries(LeaderboardEntry a, LeaderboardEntry b)
        {
            int c = b.score.CompareTo(a.score);
            if (c != 0) return c;
            if (a.isPlayer != b.isPlayer) return a.isPlayer ? -1 : 1;
            return string.CompareOrdinal(a.name, b.name);
        }

        static List<LeaderboardEntry> Copy(List<LeaderboardEntry> source)
        {
            var copy = new List<LeaderboardEntry>(source.Count);
            foreach (LeaderboardEntry e in source)
                copy.Add(new LeaderboardEntry
                {
                    rank = e.rank, name = e.name, avatar = e.avatar, team = e.team, score = e.score, isPlayer = e.isPlayer,
                    uid = e.uid, rawName = e.rawName,
                });
            return copy;
        }

        /// <summary>"avatar_puppy" for "puppy" (unknown ids fall back to the puppy).</summary>
        public static string AvatarSprite(string avatarId)
        {
            if (!string.IsNullOrEmpty(avatarId))
            {
                string id = avatarId.StartsWith("avatar_", StringComparison.Ordinal) ? avatarId.Substring(7) : avatarId;
                foreach (string known in LeaderboardSim.AvatarIds)
                    if (known == id) return "avatar_" + id;
            }
            return "avatar_puppy";
        }

        static string PlayerName(PlayerData data)
        {
            string own = PlayerProfile.Displayable(data.playerName);
            if (!string.IsNullOrEmpty(own) && !NameFilter.IsOffensive(own)) return own;
            if (AuthService.IsSignedIn && !string.IsNullOrWhiteSpace(AuthService.User.displayName))
            {
                string google = PlayerProfile.Displayable(AuthService.User.displayName);   // e.g. a CJK-only Google name
                if (!string.IsNullOrEmpty(google) && !NameFilter.IsOffensive(google)) return google;
            }
            return Loc.T("lb.you");
        }

        static string PlayerTeam(PlayerData data) => AreaName(data.level);

        static string AreaName(int level)
        {
            if (level <= 0) return "";
            try
            {
                AreaInfo area = Areas.AreaForLevel(level);
                return area != null ? Loc.T(area.NameKey) : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
