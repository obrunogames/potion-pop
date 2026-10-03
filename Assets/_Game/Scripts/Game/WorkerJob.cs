// A pure computation (solver queries on a BoardState clone) run on a worker thread; the main thread polls it from
// GameSession.Tick and picks the result up there, so no Unity API is ever touched off the main thread. Falls back to
// running inline when no thread pool is available. Results are tagged with the board version they were computed for.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PotionPop.Game
{
    sealed class WorkerJob<T>
    {
        readonly Task<T> _task;
        readonly bool _inline;
        readonly bool _inlineOk;
        readonly T _inlineResult;

        /// <summary>Board version (GameSession) the job was started for.</summary>
        public readonly int Version;
        /// <summary>Time.unscaledTime at start (timeouts).</summary>
        public readonly float StartedAt;

        WorkerJob(Task<T> task, int version)
        {
            _task = task;
            Version = version;
            StartedAt = Time.unscaledTime;
        }

        WorkerJob(bool ok, T result, int version)
        {
            _inline = true;
            _inlineOk = ok;
            _inlineResult = result;
            Version = version;
            StartedAt = Time.unscaledTime;
        }

        public static WorkerJob<T> Run(Func<T> work, int version)
        {
            if (work == null) return new WorkerJob<T>(false, default, version);
            try
            {
                return new WorkerJob<T>(Task.Run(work), version);
            }
            catch (Exception)
            {
                // No thread pool (exotic platform): compute right now.
                try { return new WorkerJob<T>(true, work(), version); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return new WorkerJob<T>(false, default, version);
                }
            }
        }

        public bool IsDone => _inline || _task == null || _task.IsCompleted;

        /// <summary>The result once done; false while running or when the work threw (logged).</summary>
        public bool TryGet(out T result)
        {
            result = default;
            if (_inline)
            {
                result = _inlineResult;
                return _inlineOk;
            }
            if (_task == null || !_task.IsCompleted) return false;
            if (_task.Status != TaskStatus.RanToCompletion)
            {
                if (_task.Exception != null) Debug.LogException(_task.Exception.GetBaseException());
                return false;
            }
            result = _task.Result;
            return true;
        }
    }
}
