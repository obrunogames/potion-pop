using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Local persistence: JSON file at Application.persistentDataPath/save.json (+ PlayerPrefs backup).
    /// CONTRACT (owner: Core agent).
    /// <list type="bullet">
    /// <item>Writes are atomic (save.json.tmp then replace) and mirrored to PlayerPrefs "sp_save_backup".</item>
    /// <item>Load order: file → backup → fresh save (first launch generates the 7-digit player id).</item>
    /// <item><see cref="MarkDirty"/> debounces writes (~0.5 s, at most ~3 s late) through a hidden DontDestroyOnLoad
    /// runner that also flushes on pause/focus loss/quit. Outside play mode writes happen immediately.</item>
    /// <item>EditMode tests call <see cref="UseMemoryStorage"/> so they never touch the real save.</item>
    /// </list>
    /// </summary>
    public static class SaveSystem
    {
        public const string FileName = "save.json";
        public const string BackupKey = "sp_save_backup";
        const float DebounceSeconds = 0.5f;
        const float MaxDelaySeconds = 3f;
        const float RetrySeconds = 5f;

        static PlayerData _data;
        static bool _dirty;
        static float _firstDirtyAt, _dueAt;
        /// <summary>After a failed write, no retry before this realtime (avoids a write attempt + warning per change).</summary>
        static float _retryNotBefore;
        static SaveRunner _runner;
        /// <summary>Set by Application.quitting: no new GameObjects may be created any more, so writes go straight to disk.</summary>
        static bool _quitting;
        /// <summary>A player-driven change is waiting to be written: the next Save stamps updatedAt. Housekeeping writes
        /// (<see cref="MarkDirtyHousekeeping"/>: daily resets, heart regen...) keep the old stamp, so a stale device that
        /// only regenerated its quests at startup can't win the cloud merge's "most recent" tie-break.</summary>
        static bool _stampPending;

        // memory storage (tests)
        static bool _memory;
        static string _memPrimary, _memBackup;
        static PlayerData _stashedData;
        static bool _stashedDirty;

        public static event Action OnChanged;
        /// <summary>Raised after the whole save was replaced (cloud restore / reset) — UI should rebuild.</summary>
        public static event Action OnReplaced;
        /// <summary>Raised after every successful write (cloud sync can hook here).</summary>
        public static event Action OnSaved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _data = null;
            _dirty = false;
            _retryNotBefore = 0f;
            _runner = null;
            _quitting = false;
            _stampPending = false;
            _memory = false;
            _memPrimary = _memBackup = null;
            _stashedData = null;
            _stashedDirty = false;
            OnChanged = null;
            OnReplaced = null;
            OnSaved = null;
        }

        /// <summary>Loaded lazily on first access.</summary>
        public static PlayerData Data => _data ?? LoadInternal();

        /// <summary>True when there are changes not written yet.</summary>
        public static bool IsDirty => _dirty;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>(Re)loads the save from storage (pending changes are written first).</summary>
        public static void Load()
        {
            if (_data != null && _dirty) Save();
            _data = null;
            LoadInternal();
        }

        /// <summary>Writes immediately. Stamps updatedAt when a player-driven change (MarkDirty / Replace / ResetAll) is
        /// pending or the save was never stamped; housekeeping-only writes keep the previous stamp.</summary>
        public static void Save()
        {
            var data = Data;
            if (_stampPending || data.updatedAt <= 0) data.updatedAt = TimeUtil.Now;
            if (Write(ToJson(data)))
            {
                _dirty = false;
                _stampPending = false;
                _retryNotBefore = 0f;
                OnSaved?.Invoke();
            }
            else
            {
                // Keep the changes in memory and retry later (disk full, permissions...).
                _dirty = true;
                _retryNotBefore = Time.realtimeSinceStartup + RetrySeconds;
                _dueAt = _retryNotBefore;
            }
        }

        /// <summary>Writes now if there are pending changes.</summary>
        public static void Flush()
        {
            if (_dirty && _data != null) Save();
        }

        /// <summary>Fires OnChanged and schedules a save (debounced ~0.5 s; also flushed on pause/quit). The change counts
        /// as player progress: the write stamps updatedAt (used by the cloud merge tie-break).</summary>
        public static void MarkDirty() => MarkDirtyInternal(true);

        /// <summary>Like <see cref="MarkDirty"/> (OnChanged + scheduled write) for automatic housekeeping changes — daily /
        /// weekly resets, heart regeneration, cloud bookkeeping — that must not make this device's save look newer than
        /// a copy with real progress.</summary>
        public static void MarkDirtyHousekeeping() => MarkDirtyInternal(false);

        static void MarkDirtyInternal(bool stamp)
        {
            if (stamp) _stampPending = true;
            if (_data == null) LoadInternal();
            if (Application.isPlaying && !_quitting)
            {
                float now = Time.realtimeSinceStartup;
                if (!_dirty) _firstDirtyAt = now;
                _dirty = true;
                _dueAt = Mathf.Max(Mathf.Min(now + DebounceSeconds, _firstDirtyAt + MaxDelaySeconds), _retryNotBefore);
                EnsureRunner();
                OnChanged?.Invoke();
            }
            else
            {
                // Edit mode (editor tools, EditMode tests): no player loop to debounce on. Quitting (OnDisable/OnDestroy
                // during teardown): the runner is gone and must not be re-created. Either way, write right away.
                _dirty = true;
                OnChanged?.Invoke();
                Save();
            }
        }

        /// <summary>Starts over with a brand-new save (new player id), writes it and fires OnReplaced.</summary>
        public static void ResetAll()
        {
            var fresh = new PlayerData();
            Prepare(fresh, true);
            _data = fresh;
            _stampPending = true;
            Save();
            NotifyReplaced();
        }

        public static string ToJson(PlayerData data) => JsonUtility.ToJson(data);

        /// <summary>
        /// Parses, migrates and sanitizes a save. Returns null when the JSON is invalid. An invalid/missing player id is
        /// left as is (not invented here): <see cref="Replace"/> then keeps the local id, loading generates a new one.
        /// </summary>
        public static PlayerData FromJson(string json)
        {
            var data = Parse(json);
            if (data != null) Prepare(data, false);
            return data;
        }

        /// <summary>GDD §7 conflict rule: higher level, then more stars, then most recent updatedAt (tie → local).</summary>
        public static PlayerData ChooseMerge(PlayerData local, PlayerData cloud, out bool tookCloud)
        {
            tookCloud = false;
            if (cloud == null) return local;
            if (local == null)
            {
                tookCloud = true;
                return cloud;
            }
            int cmp = local.level.CompareTo(cloud.level);
            if (cmp == 0) cmp = local.totalStars.CompareTo(cloud.totalStars);
            if (cmp == 0) cmp = local.updatedAt.CompareTo(cloud.updatedAt);
            tookCloud = cmp < 0;
            return tookCloud ? cloud : local;
        }

        /// <summary>The save belongs to another Firebase account than <paramref name="uid"/> (it was merged with it
        /// before). Guest saves (cloudUid "") belong to nobody.</summary>
        public static bool IsForeignSave(PlayerData local, string uid) =>
            local != null && !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(local.cloudUid) && local.cloudUid != uid;

        /// <summary>
        /// Sign-in merge for account <paramref name="uid"/>: a local save owned by ANOTHER account (account switch on a
        /// shared device) never competes with this account's cloud copy — the cloud copy wins, so one player's progress
        /// is never written over another's. Otherwise (guest save or same account) the GDD §7 <see cref="ChooseMerge"/>
        /// rule applies.
        /// </summary>
        public static PlayerData ChooseMergeForAccount(string uid, PlayerData local, PlayerData cloud, out bool tookCloud)
        {
            if (cloud != null && IsForeignSave(local, uid))
            {
                tookCloud = true;
                return cloud;
            }
            return ChooseMerge(local, cloud, out tookCloud);
        }

        /// <summary>
        /// Adds the blocked ranking players of <paramref name="from"/> that <paramref name="into"/> lacks, so whichever copy
        /// wins a cloud merge, nobody the player blocked on either device shows up again. True when something was added.
        /// </summary>
        public static bool MergeBlockedUids(PlayerData into, PlayerData from)
        {
            if (into == null || from == null || from.blockedUids == null || ReferenceEquals(into, from)) return false;
            if (into.blockedUids == null) into.blockedUids = new System.Collections.Generic.List<string>();
            bool added = false;
            foreach (string uid in from.blockedUids)
            {
                if (string.IsNullOrEmpty(uid) || into.blockedUids.Contains(uid)) continue;
                into.blockedUids.Add(uid);
                added = true;
            }
            return added;
        }

        /// <summary>Replaces the current save (cloud restore), saves and fires OnReplaced.</summary>
        public static void Replace(PlayerData data)
        {
            if (data == null) return;
            string keepId = _data != null ? _data.playerId : null;
            if (!IsValidPlayerId(data.playerId) && IsValidPlayerId(keepId)) data.playerId = keepId;
            Prepare(data, true);
            _data = data;
            _stampPending = true;
            Save();
            NotifyReplaced();
        }

        /// <summary>
        /// Version migration hook: upgrades older saves in place, step by step. Add a step whenever
        /// <see cref="PlayerData.CurrentVersion"/> is bumped. Returns true when something changed.
        /// </summary>
        public static bool Migrate(PlayerData data)
        {
            if (data == null) return false;
            int from = data.version;
            if (from < 1) data.version = 1;          // pre-release saves: same layout as v1
            // if (data.version < 2) { /* convert v1 → v2 */ data.version = 2; }
            if (data.version < PlayerData.CurrentVersion) data.version = PlayerData.CurrentVersion;
            // Saves written by a newer build keep their version (unknown fields were dropped by JsonUtility).
            return data.version != from;
        }

        // ------------------------------------------------------------------ test hooks

        /// <summary>
        /// Test hook: true swaps the real storage (file + PlayerPrefs) for an in-memory one and starts from an empty
        /// save; false restores the real storage and the previously loaded data. Never touches disk while enabled.
        /// </summary>
        public static void UseMemoryStorage(bool enable)
        {
            if (enable && _memory)
            {
                // Already in memory mode (e.g. a previous test did not tear down): start over from an empty save.
                _memPrimary = _memBackup = null;
                _data = null;
                _dirty = false;
                Loc.InvalidateLanguage();
                return;
            }
            if (enable == _memory) return;
            if (enable)
            {
                _stashedData = _data;
                _stashedDirty = _dirty;
                _memory = true;
                _memPrimary = _memBackup = null;
                _data = null;
                _dirty = false;
                _stampPending = false;
            }
            else
            {
                _memory = false;
                _memPrimary = _memBackup = null;
                _data = _stashedData;
                _dirty = _stashedDirty;
                _stashedData = null;
            }
            Loc.InvalidateLanguage();
        }

        public static bool IsUsingMemoryStorage => _memory;

        /// <summary>Test hook: raw primary save JSON of the memory storage (set it to simulate corruption).</summary>
        public static string MemoryPrimaryJson
        {
            get => _memPrimary;
            set => _memPrimary = value;
        }

        /// <summary>Test hook: raw backup JSON of the memory storage.</summary>
        public static string MemoryBackupJson
        {
            get => _memBackup;
            set => _memBackup = value;
        }

        // ------------------------------------------------------------------ internals

        static PlayerData LoadInternal()
        {
            bool corrupt = false, fromBackup = false;
            PlayerData data = null;

            string primary = ReadPrimary();
            if (!string.IsNullOrEmpty(primary))
            {
                data = Parse(primary);
                corrupt = data == null;
            }
            if (data != null)
            {
                // Normally the backup is byte-identical. It is newer only when the last file write failed (disk full,
                // app killed before the retry): then it holds the latest progress.
                string backup = ReadBackup();
                if (!string.IsNullOrEmpty(backup) && backup != primary)
                {
                    var b = Parse(backup);
                    if (b != null && b.updatedAt > data.updatedAt)
                    {
                        Debug.LogWarning("[SaveSystem] The PlayerPrefs backup is newer than " + FileName + "; using it.");
                        data = b;
                        fromBackup = true;
                    }
                }
            }
            if (data == null)
            {
                // A crash during the non-atomic fallback copy can leave a broken save next to a complete temp file.
                string tmp = ReadTemp();
                if (!string.IsNullOrEmpty(tmp)) data = Parse(tmp);
                fromBackup = data != null;
            }
            if (data == null)
            {
                string backup = ReadBackup();
                if (!string.IsNullOrEmpty(backup))
                {
                    data = Parse(backup);
                    fromBackup = data != null;
                }
            }

            bool fresh = data == null;
            if (fresh)
            {
                if (corrupt)
                {
                    Debug.LogWarning("[SaveSystem] Save file and backup unreadable; starting a fresh save.");
                    KeepCorruptCopy();
                }
                data = new PlayerData();
            }
            else if (fromBackup && corrupt)
            {
                Debug.LogWarning("[SaveSystem] Save file corrupt; restored from the last good copy (temp file / PlayerPrefs backup).");
            }

            bool changed = Prepare(data, true);
            _data = data;
            _dirty = false;

            // Persist repairs / first launch (never from edit-mode tools unless memory storage is active).
            if ((fresh || fromBackup || changed) && (_memory || Application.isPlaying)) Save();
            return data;
        }

        static PlayerData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<PlayerData>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Migration + sanity clamps (+ player id when <paramref name="ensureId"/>). True when modified.</summary>
        static bool Prepare(PlayerData d, bool ensureId)
        {
            bool changed = Migrate(d);

            if (ensureId && !IsValidPlayerId(d.playerId)) { d.playerId = GeneratePlayerId(); changed = true; }
            if (d.playerId == null) d.playerId = "";
            if (d.playerName == null) d.playerName = "";
            if (string.IsNullOrEmpty(d.avatar)) d.avatar = "puppy";
            if (d.language == null) d.language = "";
            if (d.cloudUid == null) d.cloudUid = "";
            if (d.seenTutorials == null) d.seenTutorials = new System.Collections.Generic.List<string>();
            if (d.cards == null) d.cards = new System.Collections.Generic.List<string>();
            if (d.albumsClaimed == null) d.albumsClaimed = new System.Collections.Generic.List<string>();
            if (d.quests == null) d.quests = new System.Collections.Generic.List<QuestState>();
            if (d.blockedUids == null) d.blockedUids = new System.Collections.Generic.List<string>();

            if (d.level < 1) { d.level = 1; changed = true; }
            if (d.coins < 0) { d.coins = 0; changed = true; }
            if (d.totalStars < 0) d.totalStars = 0;
            if (d.weeklyStars < 0) d.weeklyStars = 0;
            if (d.starChestProgress < 0) d.starChestProgress = 0;
            if (d.hearts < 0) d.hearts = 0;
            if (d.hearts > Lives.Max) d.hearts = Lives.Max;
            if (d.undo < 0) d.undo = 0;
            if (d.wand < 0) d.wand = 0;
            if (d.bottle < 0) d.bottle = 0;
            if (d.shuffle < 0) d.shuffle = 0;
            if (d.crystal < 0) d.crystal = 0;
            if (d.rainbow < 0) d.rainbow = 0;
            if (d.dailyDay < 0 || d.dailyDay > 6) d.dailyDay = 0;
            if (d.winStreak < 0) d.winStreak = 0;
            if (d.bestStreak < d.winStreak) d.bestStreak = d.winStreak;
            if (d.continuesThisLevel < 0) d.continuesThisLevel = 0;
            if (d.levelInProgress < 0) d.levelInProgress = 0;

            // Drop quests with an unknown kind (written by a newer build): today's set is regenerated.
            for (int i = 0; i < d.quests.Count; i++)
            {
                var q = d.quests[i];
                if (q == null || q.kind < 0 || q.kind > (int)QuestKind.WinThreeStars || q.target <= 0)
                {
                    d.quests.Clear();
                    d.questDay = -1;
                    break;
                }
            }
            return changed;
        }

        static bool IsValidPlayerId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 7) return false;
            for (int i = 0; i < id.Length; i++)
                if (id[i] < '0' || id[i] > '9') return false;
            return id[0] != '0';
        }

        static string GeneratePlayerId()
        {
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            return rng.Next(1000000, 10000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static void NotifyReplaced()
        {
            Loc.OnSaveReplaced();
            AudioManager.OnSaveReplaced();
            OnReplaced?.Invoke();
            OnChanged?.Invoke();
        }

        static void EnsureRunner()
        {
            if (_runner != null || !Application.isPlaying || _quitting) return;
            // "-=" first: without a domain reload the handler of the previous play session may still be attached.
            Application.quitting -= HandleQuitting;
            Application.quitting += HandleQuitting;
            var go = new GameObject("[SaveSystem]") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<SaveRunner>();
        }

        static void HandleQuitting()
        {
            _quitting = true;
            Flush();
        }

        internal static void RunnerUpdate()
        {
            if (_dirty && Time.realtimeSinceStartup >= _dueAt) Save();
        }

        internal static void RunnerDestroyed(SaveRunner runner)
        {
            if (_runner == runner) _runner = null;
        }

        // ------------------------------------------------------------------ storage

        static string ReadPrimary()
        {
            if (_memory) return _memPrimary;
            try
            {
                string path = FilePath;
                if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] Could not read " + FileName + ": " + e.Message);
            }
            return null;
        }

        /// <summary>The temp file of an interrupted write (only ever the newest attempted save, possibly partial).</summary>
        static string ReadTemp()
        {
            if (_memory) return null;
            try
            {
                string tmp = FilePath + ".tmp";
                if (File.Exists(tmp)) return File.ReadAllText(tmp, Encoding.UTF8);
            }
            catch (Exception) { /* unreadable: ignored */ }
            return null;
        }

        /// <summary>Keeps an unreadable save as save.json.bad (support/diagnosis) before it gets overwritten.</summary>
        static void KeepCorruptCopy()
        {
            if (_memory) return;
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Copy(path, path + ".bad", true);
            }
            catch (Exception) { /* best effort */ }
        }

        static string ReadBackup()
        {
            if (_memory) return _memBackup;
            try { return PlayerPrefs.GetString(BackupKey, null); }
            catch (Exception) { return null; }
        }

        static bool Write(string json)
        {
            if (_memory)
            {
                _memPrimary = json;
                _memBackup = json;
                return true;
            }

            bool ok = true;
            try
            {
                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null); }
                    catch (Exception)
                    {
                        // Some file systems do not support Replace: overwrite instead (the backup still covers us).
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                ok = false;
                Debug.LogWarning("[SaveSystem] Could not write " + FileName + ": " + e.Message);
            }

            try
            {
                PlayerPrefs.SetString(BackupKey, json);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] Could not write the PlayerPrefs backup: " + e.Message);
            }
            return ok;
        }
    }

    /// <summary>Hidden runner that drives the debounced save and flushes on pause/quit.</summary>
    [AddComponentMenu("")]
    internal sealed class SaveRunner : MonoBehaviour
    {
        void Update() => SaveSystem.RunnerUpdate();
        void OnApplicationPause(bool paused)
        {
            if (paused) SaveSystem.Flush();
            else TimeUtil.ClearZoneCache();   // the device time zone may have changed while in background
        }
        void OnApplicationFocus(bool focused) { if (!focused) SaveSystem.Flush(); }
        void OnApplicationQuit() => SaveSystem.Flush();
        void OnDestroy() => SaveSystem.RunnerDestroyed(this);
    }
}
