using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Hidden persistent object (DontDestroyOnLoad) shared by every service: runs coroutines that must survive scene
    /// loads (HTTP requests, delays), executes callbacks coming from SDK threads on the main thread (<see cref="Post"/>),
    /// ticks the services every frame and flushes the cloud save when the app goes to the background.
    /// Only exists in Play Mode; in Edit Mode (tests) <see cref="Ensure"/> returns null and async work is skipped.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServicesRunner : MonoBehaviour
    {
        const string ObjectName = "SPServices";

        static ServicesRunner _instance;
        static int _mainThreadId = -1;
        static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        /// <summary>Called every frame (unscaled), even while Time.timeScale is 0.</summary>
        public static event Action OnUpdate;

        /// <summary>Called when the app goes to the background (true) or comes back (false).</summary>
        public static event Action<bool> OnPause;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            OnUpdate = null;
            OnPause = null;
            while (Queue.TryDequeue(out _)) { }
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CaptureMainThread() => _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>True when called from Unity's main thread.</summary>
        public static bool IsMainThread => _mainThreadId < 0 || Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>The runner, created on first use (main thread, Play Mode only). Null in Edit Mode.</summary>
        public static ServicesRunner Ensure()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying || !IsMainThread) return null;
            if (_mainThreadId < 0) _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var go = new GameObject(ObjectName) { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ServicesRunner>();
            return _instance;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the main thread: immediately when already on it, otherwise on the next
        /// frame. Exceptions are logged, never propagated.
        /// </summary>
        public static void Post(Action action)
        {
            if (action == null) return;
            if (IsMainThread) SafeInvoke(action);
            else Queue.Enqueue(action);
        }

        /// <summary>Like <see cref="Post"/> but always deferred to the next frame (even on the main thread).</summary>
        public static void NextFrame(Action action)
        {
            if (action != null) Queue.Enqueue(action);
            if (IsMainThread) Ensure();
        }

        /// <summary>Starts a coroutine on the persistent runner. Returns null (and does nothing) in Edit Mode.</summary>
        public static Coroutine Run(IEnumerator routine)
        {
            ServicesRunner runner = Ensure();
            return runner != null && routine != null ? runner.StartCoroutine(routine) : null;
        }

        /// <summary>Calls <paramref name="action"/> after <paramref name="seconds"/> of real time (ignores timeScale).</summary>
        public static Coroutine Delay(float seconds, Action action) => Run(DelayRoutine(seconds, action));

        static IEnumerator DelayRoutine(float seconds, Action action)
        {
            float until = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
            while (Time.realtimeSinceStartup < until) yield return null;
            SafeInvoke(action);
        }

        /// <summary>Invokes an action, logging (not throwing) any exception — used for user callbacks and events.</summary>
        public static void SafeInvoke(Action action)
        {
            if (action == null) return;
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T>(Action<T> action, T arg)
        {
            if (action == null) return;
            try { action(arg); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T1, T2>(Action<T1, T2> action, T1 a, T2 b)
        {
            if (action == null) return;
            try { action(a, b); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            while (Queue.TryDequeue(out Action action)) SafeInvoke(action);
            Action tick = OnUpdate;
            if (tick != null) SafeInvoke(tick);
        }

        void OnApplicationPause(bool paused)
        {
            SafeInvoke(OnPause, paused);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
