using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>The players/{uid} document, decoded.</summary>
    public sealed class CloudPlayerDoc
    {
        public string uid;          // last path segment of the document name (may be empty)
        public string saveJson;     // PlayerData as JSON (SaveSystem.ToJson)
        public int level;
        public long stars;
        public string name;
        public string avatar;
        public long updatedAt;      // unix seconds
        /// <summary>saveJson parsed (null when absent or corrupt).</summary>
        public PlayerData data;
    }

    /// <summary>
    /// Firestore REST v1 encoding of the player document and of the leaderboard queries. Firestore wraps every value
    /// in a typed object ({"stringValue": "..."}, {"integerValue": "123"} — 64-bit integers travel as strings).
    /// Pure functions (JsonUtility-based, AOT safe) so they can be unit tested.
    /// </summary>
    public static class FirestoreCodec
    {
        /// <summary>Private saves: players/{uid} — readable and writable by the owner only (see firebase/firestore.rules).</summary>
        public const string Collection = "players";
        /// <summary>Public ranking rows: leaderboard/{uid} = { name, avatar, stars, level, updatedAt } — readable by any
        /// signed-in player, so the global ranking never exposes anyone's full save.</summary>
        public const string LeaderboardCollection = "leaderboard";
        /// <summary>Name reports: reports/{autoId} = { reporter, target, name, createdAt } — create-only for signed-in
        /// players (reporter = their own uid), never readable from the app (reviewed in the Firebase console).</summary>
        public const string ReportsCollection = "reports";
        public const int MaxNameLength = 24;
        public const int MaxAvatarLength = 32;
        public const int MaxUidLength = 128;

        public static string DatabaseRoot(string projectId) =>
            "https://firestore.googleapis.com/v1/projects/" + Uri.EscapeDataString(projectId ?? "") + "/databases/(default)/documents";

        public static string PlayerDocUrl(string projectId, string uid) =>
            DatabaseRoot(projectId) + "/" + Collection + "/" + Uri.EscapeDataString(uid ?? "");

        public static string LeaderboardDocUrl(string projectId, string uid) =>
            DatabaseRoot(projectId) + "/" + LeaderboardCollection + "/" + Uri.EscapeDataString(uid ?? "");

        /// <summary>Collection URL: a POST there creates a document with an id picked by Firestore.</summary>
        public static string ReportsUrl(string projectId) => DatabaseRoot(projectId) + "/" + ReportsCollection;

        public static string RunQueryUrl(string projectId) => DatabaseRoot(projectId) + ":runQuery";

        public static string RunAggregationQueryUrl(string projectId) => DatabaseRoot(projectId) + ":runAggregationQuery";

        // ---------------------------------------------------------------------------------------- DTOs

        [Serializable]
        public sealed class StringValue
        {
            public string stringValue;
        }

        [Serializable]
        public sealed class IntegerValue
        {
            public string integerValue;
        }

        [Serializable]
        public sealed class PlayerFields
        {
            public StringValue save;
            public IntegerValue level;
            public IntegerValue stars;
            public StringValue name;
            public StringValue avatar;
            public IntegerValue updatedAt;
        }

        [Serializable]
        public sealed class PlayerDocument
        {
            public string name;
            public PlayerFields fields;
        }

        [Serializable]
        sealed class LeaderboardFields
        {
            public IntegerValue level;
            public IntegerValue stars;
            public StringValue name;
            public StringValue avatar;
            public IntegerValue updatedAt;
        }

        [Serializable]
        sealed class LeaderboardDocumentWrite
        {
            public LeaderboardFields fields;
        }

        [Serializable]
        sealed class PlayerDocumentWrite
        {
            public PlayerFields fields;
        }

        [Serializable]
        sealed class ReportFields
        {
            public StringValue reporter;
            public StringValue target;
            public StringValue name;
            public IntegerValue createdAt;
        }

        [Serializable]
        sealed class ReportDocumentWrite
        {
            public ReportFields fields;
        }

        [Serializable]
        sealed class QueryItem
        {
            public PlayerDocument document;
        }

        [Serializable]
        sealed class QueryResponse
        {
            public QueryItem[] items;
        }

        [Serializable]
        sealed class AggregateFields
        {
            public IntegerValue n;
        }

        [Serializable]
        sealed class AggregateResult
        {
            public AggregateFields aggregateFields;
        }

        [Serializable]
        sealed class AggregateItem
        {
            public AggregateResult result;
        }

        [Serializable]
        sealed class AggregateResponse
        {
            public AggregateItem[] items;
        }

        // ---------------------------------------------------------------------------------------- encode

        static StringValue Str(string v) => new StringValue { stringValue = v ?? "" };
        static IntegerValue Int(long v) => new IntegerValue { integerValue = v.ToString(CultureInfo.InvariantCulture) };

        /// <summary>
        /// PATCH body of players/{uid}: { save, level, stars, name, avatar, updatedAt }. Name/avatar are trimmed to the
        /// lengths accepted by firebase/firestore.rules.
        /// </summary>
        public static string EncodePlayerDoc(PlayerData data, long updatedAt)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var write = new PlayerDocumentWrite
            {
                fields = new PlayerFields
                {
                    save = Str(SaveSystem.ToJson(data)),
                    level = Int(Math.Max(1, data.level)),
                    stars = Int(Math.Max(0, data.totalStars)),
                    name = Str(Clip(data.playerName, MaxNameLength)),
                    avatar = Str(Clip(data.avatar, MaxAvatarLength)),
                    updatedAt = Int(updatedAt),
                },
            };
            return JsonUtility.ToJson(write);
        }

        /// <summary>PATCH body of leaderboard/{uid}: the public subset of the player (no save). A name refused by
        /// <see cref="NameFilter"/> (saved before the filter existed) goes out empty: others see the default name.</summary>
        public static string EncodeLeaderboardDoc(PlayerData data, long updatedAt)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string name = Clip(data.playerName, MaxNameLength);
            var write = new LeaderboardDocumentWrite
            {
                fields = new LeaderboardFields
                {
                    level = Int(Math.Max(1, data.level)),
                    stars = Int(Math.Max(0, data.totalStars)),
                    name = Str(NameFilter.IsOffensive(name) ? "" : name),
                    avatar = Str(Clip(data.avatar, MaxAvatarLength)),
                    updatedAt = Int(updatedAt),
                },
            };
            return JsonUtility.ToJson(write);
        }

        /// <summary>POST body of a name report (reports collection): who reported whom, the reported name and when.
        /// Lengths match firebase/firestore.rules.</summary>
        public static string EncodeReportDoc(string reporterUid, string targetUid, string name, long createdAt)
        {
            var write = new ReportDocumentWrite
            {
                fields = new ReportFields
                {
                    reporter = Str(Clip(reporterUid, MaxUidLength)),
                    target = Str(Clip(targetUid, MaxUidLength)),
                    name = Str(Clip(name, MaxNameLength)),
                    createdAt = Int(Math.Max(0, createdAt)),
                },
            };
            return JsonUtility.ToJson(write);
        }

        // ---------------------------------------------------------------------------------------- decode

        /// <summary>Decodes a Firestore document JSON (GET response or a runQuery "document").</summary>
        public static bool TryDecodePlayerDoc(string json, out CloudPlayerDoc doc)
        {
            doc = null;
            PlayerDocument raw = FirebaseAuthApi.FromJson<PlayerDocument>(json);
            if (raw == null || !HasAnyValue(raw.fields)) return false;
            doc = Decode(raw);
            return true;
        }

        /// <summary>True when the fields object carries at least one value (JsonUtility may create empty objects).</summary>
        static bool HasAnyValue(PlayerFields f)
        {
            if (f == null) return false;
            return (f.save != null && f.save.stringValue != null) ||
                   (f.level != null && !string.IsNullOrEmpty(f.level.integerValue)) ||
                   (f.stars != null && !string.IsNullOrEmpty(f.stars.integerValue)) ||
                   (f.updatedAt != null && !string.IsNullOrEmpty(f.updatedAt.integerValue)) ||
                   (f.name != null && f.name.stringValue != null) ||
                   (f.avatar != null && f.avatar.stringValue != null);
        }

        static CloudPlayerDoc Decode(PlayerDocument raw)
        {
            PlayerFields f = raw.fields;
            var doc = new CloudPlayerDoc
            {
                uid = LastSegment(raw.name),
                saveJson = f.save != null ? f.save.stringValue : null,
                level = (int)Math.Min(int.MaxValue, Math.Max(0, ReadInt(f.level))),
                stars = Math.Max(0, ReadInt(f.stars)),
                name = f.name != null ? f.name.stringValue : null,
                avatar = f.avatar != null ? f.avatar.stringValue : null,
                updatedAt = ReadInt(f.updatedAt),
            };
            doc.data = ParseSave(doc.saveJson);
            return doc;
        }

        /// <summary>PlayerData from its JSON, or null when empty/corrupt.</summary>
        public static PlayerData ParseSave(string saveJson)
        {
            if (string.IsNullOrEmpty(saveJson) || saveJson.TrimStart().Length == 0 || saveJson.TrimStart()[0] != '{') return null;
            try
            {
                return SaveSystem.FromJson(saveJson);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CloudSave] corrupt cloud save: " + e.Message);
                return null;
            }
        }

        static long ReadInt(IntegerValue v) => v == null ? 0 : FirebaseAuthApi.ParseLong(v.integerValue, 0);

        static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length <= max) return s;
            // Never cut a surrogate pair in half.
            int cut = max;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut);
        }

        // ---------------------------------------------------------------------------------------- queries

        /// <summary>runQuery body: top <paramref name="limit"/> players by stars (only name/avatar/stars/level).</summary>
        public static string BuildTopPlayersQuery(int limit)
        {
            return "{\"structuredQuery\":{" +
                   "\"from\":[{\"collectionId\":\"" + LeaderboardCollection + "\"}]," +
                   "\"select\":{\"fields\":[{\"fieldPath\":\"name\"},{\"fieldPath\":\"avatar\"},{\"fieldPath\":\"stars\"},{\"fieldPath\":\"level\"}]}," +
                   "\"orderBy\":[{\"field\":{\"fieldPath\":\"stars\"},\"direction\":\"DESCENDING\"}]," +
                   "\"limit\":" + Math.Max(1, limit).ToString(CultureInfo.InvariantCulture) + "}}";
        }

        /// <summary>runAggregationQuery body: number of players with more than <paramref name="stars"/> stars.</summary>
        public static string BuildCountAboveQuery(long stars)
        {
            return "{\"structuredAggregationQuery\":{\"structuredQuery\":{" +
                   "\"from\":[{\"collectionId\":\"" + LeaderboardCollection + "\"}]," +
                   "\"where\":{\"fieldFilter\":{\"field\":{\"fieldPath\":\"stars\"},\"op\":\"GREATER_THAN\"," +
                   "\"value\":{\"integerValue\":\"" + Math.Max(0, stars).ToString(CultureInfo.InvariantCulture) + "\"}}}}," +
                   "\"aggregations\":[{\"alias\":\"n\",\"count\":{}}]}}";
        }

        /// <summary>Decodes a runQuery response (a JSON array of {document, readTime}) into player docs, in order.</summary>
        public static List<CloudPlayerDoc> DecodeQueryResponse(string json)
        {
            var list = new List<CloudPlayerDoc>();
            if (string.IsNullOrEmpty(json) || json.TrimStart().Length == 0 || json.TrimStart()[0] != '[') return list;
            QueryResponse response;
            try { response = JsonUtility.FromJson<QueryResponse>("{\"items\":" + json + "}"); }
            catch (Exception) { return list; }
            if (response == null || response.items == null) return list;
            foreach (QueryItem item in response.items)
            {
                if (item == null || item.document == null || string.IsNullOrEmpty(item.document.name) || !HasAnyValue(item.document.fields)) continue;
                list.Add(Decode(item.document));
            }
            return list;
        }

        /// <summary>Decodes a runAggregationQuery count response; -1 when unreadable.</summary>
        public static long DecodeCountResponse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.TrimStart().Length == 0 || json.TrimStart()[0] != '[') return -1;
            try
            {
                AggregateResponse response = JsonUtility.FromJson<AggregateResponse>("{\"items\":" + json + "}");
                if (response == null || response.items == null) return -1;
                foreach (AggregateItem item in response.items)
                {
                    if (item?.result?.aggregateFields?.n == null || string.IsNullOrEmpty(item.result.aggregateFields.n.integerValue)) continue;
                    return FirebaseAuthApi.ParseLong(item.result.aggregateFields.n.integerValue, -1);
                }
            }
            catch (Exception) { }
            return -1;
        }
    }
}
