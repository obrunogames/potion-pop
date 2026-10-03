using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PotionPop.Services
{
    public enum SyncState { Disabled, Idle, Syncing, Synced, Error }

    /// <summary>
    /// Firestore REST: document players/{uid} = { save (JSON string), level, stars, name, avatar, updatedAt }.
    ///
    /// State machine: Disabled (signed out) → Idle (signed in, waiting) → Syncing → Synced | Error (retried with
    /// backoff 30 s → 10 min). The first operation for an account is always a MERGE (GET → SaveSystem.ChooseMerge →
    /// Replace if the cloud wins → PATCH); the local save is never uploaded before the cloud copy was compared in this
    /// session (the merge runs again after the app spent ≥ 60 s in the background, and a local save owned by another
    /// account — PlayerData.cloudUid — never wins against this account's cloud copy). After that, uploads happen every 60 s while the save changed, ~2 s after each level (won: Progress
    /// .OnLevelChanged; won or lost: AdsService.OnLevelEnded), on SyncNow and when the app goes to the background. Nothing blocks the main thread; offline just means State = Error and a
    /// retry later. In the Editor (mock sign-in) the "cloud" is persistentDataPath/mock_cloud.json.
    /// </summary>
    public static class CloudSave
    {
        public const float SyncIntervalSeconds = 60f;
        const float DirtyBatchSeconds = 5f;
        const float AfterLevelSeconds = 2f;
        const float AutoMergeDelaySeconds = 0.5f;
        const float RetryMinSeconds = 30f;
        const float RetryMaxSeconds = 600f;
        /// <summary>Back from the background after at least this long: compare with the cloud again before the next
        /// upload (another device may have progressed meanwhile; iOS/Android keep suspended apps alive for hours).</summary>
        const long RemergeAfterBackgroundSeconds = 60;
        const string LastSyncPrefsPrefix = "sp.cloud.lastSync.";
        /// <summary>Loc key: the cloud save was written by a newer save format (update the app).</summary>
        public const string ErrUpdateApp = "cloud.error.update_app";

        public static event Action OnStateChanged;

        /// <summary>
        /// Raised after the cloud copy replaced the local save (sign-in or automatic merge at startup). SaveSystem.OnReplaced
        /// was raised just before; use this one for the "progress restored" toast.
        /// </summary>
        public static event Action OnRestored;

        static SyncState _state;
        static long _lastSyncAt;
        static bool _attached;
        static bool _busy;
        static bool _dirty;
        static bool _merged;
        static bool _lastMergeRestored;
        static bool _suspended;
        static bool _ignoreChanges;
        static bool _syncRequested;
        static bool _blocked;
        static string _uid;
        static int _op;
        static int _failures;
        static float _autoMergeAt;
        /// <summary>Wall clock (unix s) when the app went to the background, 0 while in the foreground.</summary>
        static long _pausedAt;
        static float _nextAutoSyncAt;
        static float _retryAt;
        static int _lastTickFrame = -1;
        /// <summary>Cloud requests started and not answered yet (any account; superseded ones included).</summary>
        static int _inFlight;
        static readonly List<Action<bool>> MergeWaiters = new List<Action<bool>>();
        static readonly List<Action<bool>> SyncWaiters = new List<Action<bool>>();
        static readonly List<Action> IdleWaiters = new List<Action>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnStateChanged = null;
            OnRestored = null;
            _state = SyncState.Disabled;
            _lastSyncAt = 0;
            _attached = _busy = _dirty = _merged = _lastMergeRestored = _suspended = _ignoreChanges = _syncRequested = _blocked = false;
            _uid = null;
            _op = 0;
            _failures = 0;
            _autoMergeAt = _nextAutoSyncAt = _retryAt = 0f;
            _pausedAt = 0;
            _lastTickFrame = -1;
            _inFlight = 0;
            MergeWaiters.Clear();
            SyncWaiters.Clear();
            IdleWaiters.Clear();
            LastErrorKey = null;
        }

        public static SyncState State => _state;

        /// <summary>Unix seconds of the last successful sync (0 = never).</summary>
        public static long LastSyncAt => _lastSyncAt;

        /// <summary>Loc key of the last failure (cloud.error.*), null after a success.</summary>
        public static string LastErrorKey { get; private set; }

        /// <summary>Local changes not uploaded yet.</summary>
        public static bool HasPendingChanges => _dirty;

        /// <summary>Loc key describing the state: "cloud.state.disabled" / idle / syncing / synced / error.</summary>
        public static string StateLocKey => "cloud.state." + _state.ToString().ToLowerInvariant();

        static float RealNow => Time.realtimeSinceStartup;

        // ---------------------------------------------------------------------------------------- wiring

        /// <summary>Subscribes to the save/auth/progress events (called by AuthService.Init; idempotent).</summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            // "-=" first: events owned by other modules may survive a play session without domain reload.
            SaveSystem.OnChanged -= HandleSaveChanged;
            SaveSystem.OnChanged += HandleSaveChanged;
            AuthService.OnAuthChanged -= HandleAuthChanged;
            AuthService.OnAuthChanged += HandleAuthChanged;
            Progress.OnLevelChanged -= HandleLevelChanged;
            Progress.OnLevelChanged += HandleLevelChanged;
            ServicesRunner.OnUpdate -= InternalTick;
            ServicesRunner.OnUpdate += InternalTick;
            ServicesRunner.OnPause -= HandlePause;
            ServicesRunner.OnPause += HandlePause;
            ServicesRunner.Ensure();
            HandleAuthChanged();
        }

        static void HandleAuthChanged()
        {
            string uid = AuthService.IsSignedIn ? AuthService.User.uid : null;
            if (uid == _uid) return;
            _uid = uid;
            _op++;                      // results of operations for the previous account are discarded
            _busy = false;
            _merged = false;
            _lastMergeRestored = false;
            _syncRequested = false;
            _blocked = false;
            _failures = 0;
            _retryAt = 0f;
            FlushWaiters(MergeWaiters, false);
            FlushWaiters(SyncWaiters, false);
            if (uid == null)
            {
                _dirty = false;
                _lastSyncAt = 0;
                SetState(SyncState.Disabled);
                return;
            }
            _lastSyncAt = FirebaseAuthApi.ParseLong(PlayerPrefs.GetString(LastSyncPrefsPrefix + uid, "0"), 0);
            _dirty = true;              // local progress not compared with this account yet
            _autoMergeAt = RealNow + AutoMergeDelaySeconds;
            SetState(SyncState.Idle);
        }

        static void HandleSaveChanged()
        {
            if (_ignoreChanges || _uid == null) return;
            if (_dirty) return;
            _dirty = true;
            _nextAutoSyncAt = Mathf.Max(_nextAutoSyncAt, RealNow + DirtyBatchSeconds);
        }

        static void HandleLevelChanged(int level) => NotifyLevelEnded();

        /// <summary>
        /// A level ended (win → Progress.OnLevelChanged, win or loss → AdsService.OnLevelEnded): upload ~2 s later
        /// (GDD §7 "sync after each level").
        /// </summary>
        internal static void NotifyLevelEnded()
        {
            if (_uid == null) return;
            _dirty = true;
            _nextAutoSyncAt = Mathf.Min(_nextAutoSyncAt, RealNow + AfterLevelSeconds);
        }

        static void HandlePause(bool paused)
        {
            if (paused)
            {
                _pausedAt = TimeUtil.Now;
                OnApplicationPause();
                return;
            }
            // Resumed. Wall clock on purpose: realtimeSinceStartup does not reliably advance while the device sleeps.
            long away = _pausedAt > 0 ? TimeUtil.Now - _pausedAt : 0;
            _pausedAt = 0;
            if (_uid == null || !_merged || _blocked || away < RemergeAfterBackgroundSeconds) return;
            // A long background: the cloud may hold newer progress from another device. Merge again (GET → ChooseMerge →
            // Replace if the cloud wins → PATCH) instead of blindly uploading this possibly stale save. An upload already
            // started by the pause flush finishes normally; InternalTick runs the merge once it is done.
            _merged = false;
            _autoMergeAt = RealNow + AutoMergeDelaySeconds;
        }

        /// <summary>Called by AuthService right before signing out: uploads pending changes with the current token.</summary>
        internal static void BeforeSignOut()
        {
            // Only when the request can start right now (cached token): the session is cleared right after this call.
            if (_uid != null && _merged && _dirty && !_busy && !_suspended && !_blocked && AuthService.HasFreshIdToken) Upload();
        }

        /// <summary>Pauses every upload (account deletion in progress). Pending work resumes on the next tick.</summary>
        internal static void SetSuspended(bool suspended) => _suspended = suspended;

        /// <summary>
        /// Runs <paramref name="next"/> once no cloud request is in flight any more (immediately when idle). Account
        /// deletion uses it so an upload that is already on the wire cannot recreate the document after the delete.
        /// Requests always answer (HTTP timeout 20 s); a 30 s safety net covers a runner destroyed mid-request.
        /// </summary>
        internal static void WhenIdle(Action next)
        {
            if (next == null) return;
            if (_inFlight <= 0)
            {
                ServicesRunner.SafeInvoke(next);
                return;
            }
            IdleWaiters.Add(next);
            ServicesRunner.Delay(30f, () =>
            {
                if (!IdleWaiters.Remove(next)) return;
                Debug.LogWarning("[CloudSave] a cloud request never answered; continuing anyway.");
                ServicesRunner.SafeInvoke(next);
            });
        }

        // ---------------------------------------------------------------------------------------- public API

        /// <summary>Called right after sign-in: downloads, merges (SaveSystem.ChooseMerge), uploads. done(restoredFromCloud).</summary>
        public static void OnSignedIn(Action<bool> done)
        {
            Attach();
            HandleAuthChanged();
            if (_uid == null || _blocked) { ServicesRunner.SafeInvoke(done, false); return; }
            if (_merged) { ServicesRunner.SafeInvoke(done, _lastMergeRestored); return; }
            if (done != null) MergeWaiters.Add(done);
            if (!_busy && !_suspended) RunMerge();
        }

        /// <summary>Uploads now (merging first if this account was not merged yet). done(success).</summary>
        public static void SyncNow(Action<bool> done = null)
        {
            Attach();
            HandleAuthChanged();
            if (_uid == null || _blocked) { ServicesRunner.SafeInvoke(done, false); return; }
            if (done != null) SyncWaiters.Add(done);
            _syncRequested = true;
            if (_busy || _suspended) return;   // picked up when the running operation ends
            if (!_merged) RunMerge();
            else Upload();
        }

        /// <summary>Periodic sync while dirty (every 60 s) + retries. Call from GameRoot.Update (also self-ticked).</summary>
        public static void Tick()
        {
            Attach();
            InternalTick();
        }

        /// <summary>Flushes pending changes (app going to the background).</summary>
        public static void OnApplicationPause()
        {
            if (_uid != null && _merged && _dirty && !_busy && !_suspended && !_blocked) Upload();
        }

        // ---------------------------------------------------------------------------------------- state machine

        static void InternalTick()
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            if (_uid == null || _busy || _suspended || _blocked) return;
            float now = RealNow;
            if (_state == SyncState.Error && now < _retryAt) return;
            if (!_merged)
            {
                if (now >= _autoMergeAt) RunMerge();
                return;
            }
            if (_syncRequested || (_dirty && now >= _nextAutoSyncAt)) Upload();
        }

        static void RunMerge()
        {
            if (_uid == null) return;
            _busy = true;
            int op = ++_op;
            string uid = _uid;
            SetState(SyncState.Syncing);
            GetDoc(uid, r =>
            {
                if (op != _op) return;
                _busy = false;
                if (!r.ok && !r.notFound)
                {
                    Fail(r.errorKey);
                    FlushWaiters(MergeWaiters, false);
                    FlushWaiters(SyncWaiters, false);
                    return;
                }

                if (r.ok && r.doc != null && r.doc.data != null && r.doc.data.version > PlayerData.CurrentVersion)
                {
                    // Written by a newer build: never overwrite it (fields this build does not know would be lost).
                    Debug.LogWarning("[CloudSave] cloud save version " + r.doc.data.version + " > " + PlayerData.CurrentVersion +
                                     "; sync disabled until the app is updated.");
                    _blocked = true;
                    LastErrorKey = ErrUpdateApp;
                    SetState(SyncState.Error);
                    FlushWaiters(MergeWaiters, false);
                    FlushWaiters(SyncWaiters, false);
                    return;
                }

                bool restored = false;
                bool blockedMerged = false;
                if (r.ok && r.doc != null && r.doc.data != null)
                {
                    PlayerData local = SaveSystem.Data;
                    // Account switch on a shared device: the local save belongs to another account, so this account's
                    // cloud copy wins outright (never merged with / overwritten by someone else's progress).
                    bool foreign = SaveSystem.IsForeignSave(local, uid);
                    if (foreign)
                    {
                        Debug.Log("[CloudSave] local save belongs to another account; restoring this account's cloud save.");
                        KeepRecoveryCopy(local);
                    }
                    PlayerData chosen = SaveSystem.ChooseMergeForAccount(uid, local, r.doc.data, out bool tookCloud);
                    // Players blocked in the ranking stay blocked whichever copy wins (never across accounts).
                    if (!foreign && chosen != null)
                        blockedMerged = SaveSystem.MergeBlockedUids(chosen, ReferenceEquals(chosen, local) ? r.doc.data : local);
                    if (chosen != null && !ReferenceEquals(chosen, local))
                    {
                        _ignoreChanges = true;
                        try { SaveSystem.Replace(chosen); }
                        catch (Exception e) { Debug.LogException(e); }
                        finally { _ignoreChanges = false; }
                        restored = tookCloud;
                    }
                    Debug.Log("[CloudSave] merged with cloud (cloud level " + r.doc.level + ", stars " + r.doc.stars + "): " +
                              (restored ? "cloud save restored" : "local save kept"));
                }
                else if (r.ok)
                {
                    Debug.LogWarning("[CloudSave] cloud document has no readable save; it will be overwritten by the local save.");
                }

                // The local save is now this account's (restored, kept or first upload to a new document): remember the
                // owner so a later sign-in with another account never merges into it. Housekeeping write: no stamp (it
                // also writes the cloud copy's blocked players merged into a kept local save).
                if (SaveSystem.Data.cloudUid != uid || (blockedMerged && !restored))
                {
                    _ignoreChanges = true;
                    try
                    {
                        SaveSystem.Data.cloudUid = uid;
                        SaveSystem.MarkDirtyHousekeeping();
                    }
                    catch (Exception e) { Debug.LogException(e); }
                    finally { _ignoreChanges = false; }
                }

                _merged = true;
                _lastMergeRestored = restored;
                FlushWaiters(MergeWaiters, restored);
                if (restored) RaiseRestored();
                Upload();
            });
        }

        static void Upload()
        {
            if (_uid == null || !_merged) return;
            if (_suspended) { _dirty = true; return; }   // retried by InternalTick once the suspension ends
            PlayerData data = SaveSystem.Data;
            string body;
            try
            {
                long stamp = data.updatedAt > 0 ? data.updatedAt : TimeUtil.Now;
                body = FirestoreCodec.EncodePlayerDoc(data, stamp);
                _leaderboardBody = FirestoreCodec.EncodeLeaderboardDoc(data, stamp);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(FirestoreClient.ErrFailed);
                FlushWaiters(SyncWaiters, false);
                return;
            }

            _busy = true;
            int op = ++_op;
            string uid = _uid;
            _dirty = false;
            _syncRequested = false;
            Action<bool>[] waiters = SyncWaiters.ToArray();
            SyncWaiters.Clear();
            SetState(SyncState.Syncing);

            PutDoc(uid, body, r =>
            {
                if (op != _op)
                {
                    InvokeAll(waiters, false);
                    return;
                }
                _busy = false;
                if (r.ok)
                {
                    _failures = 0;
                    LastErrorKey = null;
                    _lastSyncAt = TimeUtil.Now;
                    PlayerPrefs.SetString(LastSyncPrefsPrefix + uid, _lastSyncAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _nextAutoSyncAt = RealNow + SyncIntervalSeconds;
                    SetState(SyncState.Synced);
                }
                else
                {
                    _dirty = true;
                    Fail(r.errorKey);
                    // SyncNow callers that queued during this upload would otherwise wait for the next retry
                    // (30 s .. 10 min); the retry itself still happens (_syncRequested stays set).
                    FlushWaiters(SyncWaiters, false);
                }
                InvokeAll(waiters, r.ok);
                // A SyncNow that arrived during the upload runs right away.
                if (_syncRequested && !_busy && !_suspended && _uid == uid && _state != SyncState.Error) Upload();
            });
        }

        /// <summary>Best-effort copy of a save that is about to be replaced by another account's cloud save
        /// (persistentDataPath/save.prev_{uid}.json), for support / manual recovery of progress made since its last sync.</summary>
        static void KeepRecoveryCopy(PlayerData local)
        {
            if (local == null || SaveSystem.IsUsingMemoryStorage) return;
            try
            {
                string owner = string.IsNullOrEmpty(local.cloudUid) ? "guest" : local.cloudUid;
                foreach (char c in Path.GetInvalidFileNameChars()) owner = owner.Replace(c, '_');
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "save.prev_" + owner + ".json"), SaveSystem.ToJson(local));
            }
            catch (Exception e) { Debug.LogWarning("[CloudSave] could not keep a recovery copy: " + e.Message); }
        }

        static void Fail(string errorKey)
        {
            _failures++;
            LastErrorKey = string.IsNullOrEmpty(errorKey) ? FirestoreClient.ErrFailed : errorKey;
            float wait = Mathf.Min(RetryMaxSeconds, RetryMinSeconds * Mathf.Pow(2f, Mathf.Min(_failures - 1, 8)));
            _retryAt = RealNow + wait;
            Debug.LogWarning("[CloudSave] sync failed (" + LastErrorKey + "); retrying in " + Mathf.RoundToInt(wait) + " s.");
            SetState(SyncState.Error);
        }

        // ---------------------------------------------------------------------------------------- backend

        static bool UseMock => AuthService.User != null && AuthService.User.isMock;

        static void GetDoc(string uid, Action<CloudResult> done)
        {
            Action<CloudResult> tracked = Track(done);
            if (UseMock) MockCloudStore.GetPlayer(uid, tracked);
            else FirestoreClient.GetPlayer(uid, tracked);
        }

        /// <summary>Public ranking row matching the last encoded save (written right after it).</summary>
        static string _leaderboardBody;

        static void PutDoc(string uid, string body, Action<CloudResult> done)
        {
            Action<CloudResult> tracked = Track(done);
            if (UseMock) MockCloudStore.PutPlayer(uid, body, tracked);
            else FirestoreClient.PutPlayer(uid, body, tracked, _leaderboardBody);
        }

        /// <summary>Counts the request as in flight until it answers, then serves <see cref="WhenIdle"/> waiters.</summary>
        static Action<CloudResult> Track(Action<CloudResult> done)
        {
            _inFlight++;
            bool answered = false;
            return r =>
            {
                if (answered) return;
                answered = true;
                _inFlight = Math.Max(0, _inFlight - 1);
                ServicesRunner.SafeInvoke(done, r);
                if (_inFlight == 0 && IdleWaiters.Count > 0)
                {
                    Action[] waiters = IdleWaiters.ToArray();
                    IdleWaiters.Clear();
                    foreach (Action w in waiters) ServicesRunner.SafeInvoke(w);
                }
            };
        }

        // ---------------------------------------------------------------------------------------- helpers

        static void SetState(SyncState state)
        {
            if (_state == state) return;
            _state = state;
            Action handler = OnStateChanged;
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList()) ServicesRunner.SafeInvoke((Action)d);
        }

        static void RaiseRestored()
        {
            Action handler = OnRestored;
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList()) ServicesRunner.SafeInvoke((Action)d);
        }

        static void FlushWaiters(List<Action<bool>> list, bool value)
        {
            if (list.Count == 0) return;
            Action<bool>[] copy = list.ToArray();
            list.Clear();
            InvokeAll(copy, value);
        }

        static void InvokeAll(Action<bool>[] callbacks, bool value)
        {
            foreach (Action<bool> cb in callbacks) ServicesRunner.SafeInvoke(cb, value);
        }
    }
}
