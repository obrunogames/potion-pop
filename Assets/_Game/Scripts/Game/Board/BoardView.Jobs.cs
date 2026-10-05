// ============================================================================================================
// Board module: animation job scheduler.
//  * Every Play* call becomes a BoardJob that claims ("holds") the bottles it animates. A job starts as soon as all
//    of its bottles are free and no earlier queued job wants one of them (per-bottle call order is preserved);
//    jobs on different bottles run at the same time, so the player can chain pours quickly.
//  * A job owns its timeline (t in seconds, cues = one-shot actions at given times, Tick = continuous motion).
//    It may release a bottle before it ends (the target of a pour is free while the source flies back): releasing
//    applies the model snapshot taken when the job was requested, so a bottle always rests on a model state.
//  * When jobs end: End() → remaining bottles released → queued jobs started → onDone callbacks → if nothing is
//    left, the view reconciles with the model (drift repair) and raises OnIdle.
//  * RefreshAll / Clear abort every job (Abort() frees external resources only) and fire their onDone.
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using UnityEngine;

namespace PotionPop.Game.Board
{
    /// <summary>Base class of a board animation (see BoardView.Jobs.cs).</summary>
    internal abstract class BoardJob
    {
        public BoardView board;
        public Action onDone;
        public int seq;
        public float t;
        public float duration = 0.5f;
        public bool running, finished;

        readonly List<int> _held = new List<int>(2);
        readonly List<int> _snapBottles = new List<int>(2);
        readonly List<BottleData> _snaps = new List<BottleData>(2);

        struct Cue
        {
            public float time;
            public Action action;
        }

        readonly List<Cue> _cues = new List<Cue>(8);
        int _nextCue;

        public IReadOnlyList<int> Held => _held;
        public bool Holds(int bottle) => _held.Contains(bottle);

        /// <summary>Claims a bottle (call before the job is scheduled).</summary>
        public void Hold(int bottle)
        {
            if (bottle >= 0 && !_held.Contains(bottle)) _held.Add(bottle);
        }

        /// <summary>Remembers the model state of a bottle (applied when the job releases it).</summary>
        public void Snapshot(int bottle, BoardState model)
        {
            if (model == null || bottle < 0 || bottle >= model.Count) return;
            int k = _snapBottles.IndexOf(bottle);
            var d = BottleData.Of(model[bottle]);
            if (k >= 0) _snaps[k] = d;
            else
            {
                _snapBottles.Add(bottle);
                _snaps.Add(d);
            }
        }

        public BottleData SnapshotOf(int bottle)
        {
            int k = _snapBottles.IndexOf(bottle);
            return k >= 0 ? _snaps[k] : null;
        }

        /// <summary>Schedules an action at job time <paramref name="time"/> (cues fire in time order).</summary>
        public void At(float time, Action action)
        {
            if (action == null) return;
            int i = _cues.Count;
            while (i > _nextCue && _cues[i - 1].time > time) i--;
            _cues.Insert(i, new Cue { time = time, action = action });
        }

        /// <summary>Extends the job so it lasts at least until <paramref name="time"/>.</summary>
        public void LastUntil(float time)
        {
            if (time > duration) duration = time;
        }

        /// <summary>Releases a bottle now (applies its snapshot, lets taps / queued jobs use it).</summary>
        public void Release(int bottle)
        {
            if (!_held.Contains(bottle)) return;
            _held.Remove(bottle);
            board.OnJobReleased(this, bottle, SnapshotOf(bottle));
        }

        public void ReleaseAll()
        {
            for (int i = _held.Count - 1; i >= 0; i--) Release(_held[i]);
        }

        /// <summary>Forgets the held bottles without applying snapshots (abort).</summary>
        public void DropAll() => _held.Clear();

        internal void Step(float dt)
        {
            t += dt;
            while (_nextCue < _cues.Count && _cues[_nextCue].time <= t)
            {
                var a = _cues[_nextCue++].action;
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
                if (finished) return;
            }
            Tick(dt);
            if (t >= duration && _nextCue >= _cues.Count) finished = true;
        }

        /// <summary>Called once when the job starts (its bottles are free and now held).</summary>
        public virtual void Begin() { }

        /// <summary>Continuous motion, every frame after the due cues.</summary>
        public virtual void Tick(float dt) { }

        /// <summary>Natural end: leave every visual in its final pose (snapshots are applied right after).</summary>
        public virtual void End() { }

        /// <summary>Aborted (RefreshAll / Clear): release external resources only (the view resyncs everything).</summary>
        public virtual void Abort() { }
    }

    public sealed partial class BoardView
    {
        readonly List<BoardJob> _active = new List<BoardJob>(8);
        readonly List<BoardJob> _queue = new List<BoardJob>(4);
        readonly List<BoardJob> _ending = new List<BoardJob>(4);
        int _seq;
        bool _hadJobs;
        int _answerStamp;

        /// <summary>Last real timeline progress, so a long queue is not mistaken for a stalled animation.</summary>
        public float LastAnimationProgressTime { get; private set; }

        /// <summary>Schedules a job (it starts at once when its bottles are free).</summary>
        void Run(BoardJob job, Action onDone)
        {
            job.board = this;
            job.onDone = onDone;
            job.seq = ++_seq;
            _queue.Add(job);
            _hadJobs = true;
            LastAnimationProgressTime = Time.unscaledTime;
            StartReadyJobs();
        }

        void StartReadyJobs()
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                var job = _queue[i];
                if (!CanStart(job, i)) continue;
                _queue.RemoveAt(i);
                i--;
                var held = job.Held;
                for (int k = 0; k < held.Count; k++)
                {
                    var v = View(held[k]);
                    if (v != null) v.holder = job;
                }
                job.running = true;
                _active.Add(job);
                try { job.Begin(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    job.finished = true;
                }
            }
        }

        bool CanStart(BoardJob job, int queueIndex)
        {
            var held = job.Held;
            for (int k = 0; k < held.Count; k++)
            {
                int b = held[k];
                var v = View(b);
                if (v != null && v.holder != null) return false;
                for (int q = 0; q < queueIndex; q++) if (_queue[q].Holds(b)) return false;
            }
            return true;
        }

        void TickJobs(float dt)
        {
            StartReadyJobs();
            for (int i = 0; i < _active.Count; i++)
            {
                var job = _active[i];
                if (job.finished) continue;
                try
                {
                    float before = job.t;
                    job.Step(dt);
                    if (job.t > before) LastAnimationProgressTime = Time.unscaledTime;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    job.finished = true;
                }
            }
            FinishJobs();
        }

        void FinishJobs()
        {
            _ending.Clear();
            for (int i = 0; i < _active.Count; i++) if (_active[i].finished) _ending.Add(_active[i]);
            if (_ending.Count == 0) return;
            for (int i = 0; i < _ending.Count; i++) _active.Remove(_ending[i]);

            List<Action> callbacks = null;
            for (int i = 0; i < _ending.Count; i++)
            {
                var job = _ending[i];
                try { job.End(); }
                catch (Exception e) { Debug.LogException(e); }
                job.ReleaseAll();
                job.running = false;
                if (job.onDone != null) (callbacks ??= new List<Action>(2)).Add(job.onDone);
            }
            _ending.Clear();
            StartReadyJobs();
            InvokeAll(callbacks);
            CheckIdle();
        }

        void CheckIdle()
        {
            if (!_hadJobs || _active.Count > 0 || _queue.Count > 0) return;
            _hadJobs = false;
            Reconcile();
            RaiseIdle();
        }

        /// <summary>Called by a job releasing a bottle: applies the snapshot and frees the bottle.</summary>
        internal void OnJobReleased(BoardJob job, int bottle, BottleData snapshot)
        {
            var v = View(bottle);
            if (v == null) return;
            if (v.holder == job) v.holder = null;
            if (snapshot != null) ApplySnapshot(v, snapshot);
        }

        /// <summary>Rests a bottle on a model snapshot (liquid + cork; stones follow their own sequence).</summary>
        void ApplySnapshot(BottleView v, BottleData snap)
        {
            v.shown.CopyFrom(snap);
            v.SyncLiquid();
            if (v.corked != snap.completed) v.SetCorked(snap.completed);
            v.streamOn = false;
        }

        /// <summary>Aborts every running / queued job (no snapshots: callers resync). Returns their onDone callbacks.</summary>
        List<Action> AbortAllJobs()
        {
            var callbacks = new List<Action>(_active.Count + _queue.Count);
            for (int pass = 0; pass < 2; pass++)
            {
                var list = pass == 0 ? _active : _queue;
                for (int i = 0; i < list.Count; i++)
                {
                    var job = list[i];
                    try { job.Abort(); }
                    catch (Exception e) { Debug.LogException(e); }
                    job.DropAll();
                    job.running = false;
                    job.finished = true;
                    if (job.onDone != null) callbacks.Add(job.onDone);
                }
            }
            _active.Clear();
            _queue.Clear();
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                if (v == null) continue;
                v.holder = null;
                v.streamOn = false;
            }
            return callbacks;
        }

        /// <summary>Idle board: anything that drifted from the model (session changes without Play*) snaps back.</summary>
        void Reconcile()
        {
            if (Board == null) return;
            if (_views.Count != Board.Count)
            {
                EnsureViews();
                ApplyLayout(true);
            }
            for (int i = 0; i < _views.Count && i < Board.Count; i++)
            {
                var v = _views[i];
                if (v == null || v.holder != null) continue;
                var b = Board[i];
                if (!v.shown.SameLiquid(b))
                {
#if UNITY_EDITOR
                    Debug.LogWarning("[Board] bottle " + i + " drifted from the model (" + b + "); snapped.");
#endif
                    var d = BottleData.Of(b);
                    v.SetData(d);
                }
                if (v.corked != b.Completed) v.SetCorked(b.Completed);
                if (v.lockShown != b.LockRemaining) v.SnapLock(b.LockRemaining);
                if (!_awaitingIntro)
                {
                    v.group.alpha = 1f;
                    v.introScale = 1f;
                    v.introOffset = Vector2.zero;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ stones

        /// <summary>Plays a stone counter change requested by job <paramref name="seq"/> (latest request wins).</summary>
        internal void ApplyLockTick(LockTick tick, int seq)
        {
            var v = View(tick.bottle);
            if (v == null) return;
            if (seq < v.lockSeq)
            {
                v.StoneNudge();
                return;
            }
            v.lockSeq = seq;
            int remaining = Mathf.Max(0, tick.remaining);
            if (remaining > 0)
            {
                if (v.lockShown <= 0) v.StoneRestore(remaining);
                else if (remaining < v.lockShown) v.StoneTick(remaining);
                else if (remaining > v.lockShown) v.StoneCountUp(remaining);
                else v.StoneNudge();
            }
            else if (v.lockShown > 0)
            {
                v.StoneBreak();
                v.fxBusyUntil = _clock + 0.55f;
            }
            v.lockShown = remaining;
        }

        /// <summary>Schedules the stone ticks of a job starting at job time <paramref name="start"/>; returns the time the
        /// last one finishes.</summary>
        float ScheduleLockTicks(BoardJob job, IList<LockTick> ticks, float start)
        {
            if (ticks == null || ticks.Count == 0) return start;
            float end = start;
            for (int i = 0; i < ticks.Count; i++)
            {
                var tick = ticks[i];
                float at = start + i * 0.14f;
                int seq = job.seq;
                job.At(at, () => ApplyLockTick(tick, seq));
                end = Mathf.Max(end, at + (tick.remaining <= 0 ? 0.6f : 0.4f));
                // Visually still locked while the model is already open: ignore taps until the stone has broken.
                var v = View(tick.bottle);
                if (v != null && tick.remaining <= 0) v.fxBusyUntil = Mathf.Max(v.fxBusyUntil, _clock + (at - job.t) + 0.6f);
            }
            return end;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>Hands a bottle over to a job: selection / hint dropped, pose tweens killed, made visible.</summary>
        void PrepareForJob(BottleView v, bool keepPose)
        {
            if (v == null) return;
            if (_selected == v.index) _selected = -1;
            if (_hintActive && (v.index == _hint.from || v.index == _hint.to)) HideHint();
            v.SetSelectedGlow(false);
            if (keepPose)
            {
                v.KillPoseTweens();
                v.visual.localRotation = Quaternion.identity;
                v.visual.localScale = Vector3.one;
                v.visual.anchoredPosition = Vector2.zero;
                v.lifted = false;
            }
            else if (v.lifted || v.body.anchoredPosition.sqrMagnitude > 0.01f || v.body.localRotation != Quaternion.identity)
            {
                v.KillPoseTweens();
                v.visual.localRotation = Quaternion.identity;
                v.visual.localScale = Vector3.one;
                v.visual.anchoredPosition = Vector2.zero;
                v.Lower(true);
            }
            v.group.alpha = 1f;
        }
    }
}
