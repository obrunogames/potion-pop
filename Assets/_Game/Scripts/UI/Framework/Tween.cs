// ============================================================================================================
// Allocation-light tween engine for the UI. One TweenHandle object per tween (no closures inside the engine, no
// per-frame allocations), driven by a lazily created hidden runner. Unscaled time by default. Tweens whose
// target (or link) was destroyed stop silently. Start values are captured when the tween actually starts
// (after its delay), so tweens can be queued with SetDelay.
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PotionPop.UI
{
    public enum Ease { Linear, InQuad, OutQuad, InOutQuad, InCubic, OutCubic, InOutCubic, OutBack, InBack, OutElastic, OutBounce, InOutSine }

    /// <summary>Handle of a running tween. Methods are chainable.</summary>
    public sealed class TweenHandle
    {
        internal enum Kind : byte
        {
            Scale, Move, MoveWorld, MoveLocal, Bezier, Rotate, FadeGroup, FadeGraphic, Color, Size, Value, Punch, Shake, Delay
        }

        internal Kind kind;
        internal Object target;          // animated object (null for Value/Delay)
        internal bool hasTarget;
        internal Object link;            // optional lifetime owner
        internal bool hasLink;
        internal Vector4 from, to;
        internal Vector3 control;
        internal float duration, elapsed, delay, strength;
        internal float overshoot = 1.70158f;
        internal Ease ease;
        internal int loops = 1, loopIndex;
        internal bool yoyo, unscaled = true, started, active;
        internal Action onComplete;
        internal Action<float> onValue;    // Value tweens
        internal Action<float> onUpdate;   // normalized progress callback

        /// <summary>Creates an inactive handle (a safe "no tween" placeholder: chainable, Kill is a no-op).
        /// Running tweens are created by the Tween factories.</summary>
        public TweenHandle() { }

        /// <summary>True while the tween is running (or waiting for its delay).</summary>
        public bool IsActive => active;

        /// <summary>Adds a callback invoked once when the tween completes (not when it is killed without completing).</summary>
        public TweenHandle OnComplete(Action action) { onComplete += action; return this; }

        public TweenHandle SetDelay(float seconds) { delay = Mathf.Max(0f, seconds); return this; }

        /// <summary>count = -1 for infinite; yoyo reverses every other loop.</summary>
        public TweenHandle SetLoops(int count, bool yoyo = true)
        {
            loops = count == 0 ? 1 : (count < 0 ? -1 : count);
            this.yoyo = yoyo;
            return this;
        }

        /// <summary>Ignore Time.timeScale (default for UI).</summary>
        public TweenHandle SetUnscaled(bool unscaled = true) { this.unscaled = unscaled; return this; }

        public void Kill(bool complete = false) => Tween.KillHandle(this, complete);

        // ---- additions

        public TweenHandle SetEase(Ease value) { ease = value; return this; }

        /// <summary>Overshoot amount of OutBack/InBack (default 1.70158; DS PopIn uses 1.4).</summary>
        public TweenHandle SetOvershoot(float value) { overshoot = value; return this; }

        /// <summary>Called every frame with the normalized (un-eased) progress of the current loop.</summary>
        public TweenHandle OnUpdate(Action<float> action) { onUpdate += action; return this; }

        /// <summary>Ties the tween to an object's lifetime: it stops silently when the owner is destroyed and
        /// Tween.Kill(owner) kills it. Recommended for Value/Delay tweens that touch UI.</summary>
        public TweenHandle SetLink(Object owner)
        {
            link = owner;
            hasLink = owner != null;
            return this;
        }
    }

    /// <summary>Lightweight tween engine driven by a hidden runner. Uses unscaled time by default.
    /// Tweens on destroyed targets stop silently.</summary>
    public static class Tween
    {
        static readonly List<TweenHandle> _tweens = new List<TweenHandle>(256);
        static readonly Predicate<TweenHandle> _isDead = t => !t.active;
        static TweenRunner _runner;
        static bool _quitting;
        static bool _ticking;

        /// <summary>Largest frame step (seconds) applied to tweens; avoids jumps after hitches/app resume.</summary>
        public const float MaxStep = 0.25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _tweens.Clear();
            _runner = null;
            _quitting = false;
            _ticking = false;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        static void OnQuit() => _quitting = true;

        /// <summary>Number of live tweens (debug/tests).</summary>
        public static int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _tweens.Count; i++) if (_tweens[i].active) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------------------------------- factories

        public static TweenHandle Scale(Transform t, Vector3 to, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.Scale, t, to, dur, ease);

        public static TweenHandle Scale(Transform t, float to, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.Scale, t, Vector3.one * to, dur, ease);

        public static TweenHandle Move(RectTransform t, Vector2 anchoredTo, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.Move, t, anchoredTo, dur, ease);

        public static TweenHandle MoveWorld(Transform t, Vector3 to, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.MoveWorld, t, to, dur, ease);

        /// <summary>Tweens localPosition.</summary>
        public static TweenHandle MoveLocal(Transform t, Vector3 to, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.MoveLocal, t, to, dur, ease);

        /// <summary>Quadratic bezier in world space (current position → control → to).</summary>
        public static TweenHandle MoveBezier(Transform t, Vector3 control, Vector3 to, float dur, Ease ease = Ease.InOutCubic)
        {
            var h = Create(TweenHandle.Kind.Bezier, t, to, dur, ease);
            h.control = control;
            return h;
        }

        /// <summary>Rotates localEulerAngles.z from the current angle (normalized to -180..180) to zDegrees
        /// (values beyond ±180 spin, e.g. 360 = one full turn).</summary>
        public static TweenHandle Rotate(Transform t, float zDegrees, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.Rotate, t, new Vector4(zDegrees, 0, 0, 0), dur, ease);

        public static TweenHandle Fade(CanvasGroup g, float to, float dur, Ease ease = Ease.Linear) =>
            Create(TweenHandle.Kind.FadeGroup, g, new Vector4(to, 0, 0, 0), dur, ease);

        public static TweenHandle Fade(Graphic g, float to, float dur, Ease ease = Ease.Linear) =>
            Create(TweenHandle.Kind.FadeGraphic, g, new Vector4(to, 0, 0, 0), dur, ease);

        public static TweenHandle Color(Graphic g, UnityEngine.Color to, float dur, Ease ease = Ease.Linear) =>
            Create(TweenHandle.Kind.Color, g, to, dur, ease);

        public static TweenHandle Size(RectTransform t, Vector2 sizeDelta, float dur, Ease ease = Ease.OutCubic) =>
            Create(TweenHandle.Kind.Size, t, sizeDelta, dur, ease);

        public static TweenHandle Value(float from, float to, float dur, Action<float> onUpdate, Ease ease = Ease.Linear)
        {
            var h = Create(TweenHandle.Kind.Value, null, new Vector4(to, 0, 0, 0), dur, ease);
            h.from = new Vector4(from, 0, 0, 0);
            h.onValue = onUpdate;
            return h;
        }

        /// <summary>Scale punch around the current scale.</summary>
        public static TweenHandle Punch(Transform t, float strength = 0.2f, float dur = 0.35f)
        {
            // A new punch on a target that is still punching restarts from the original rest scale (no drift).
            var running = FindRunning(t, TweenHandle.Kind.Punch);
            if (running != null)
            {
                if (running.started && t != null) t.localScale = running.from;
                running.active = false;
            }
            var h = Create(TweenHandle.Kind.Punch, t, Vector4.zero, dur, Ease.Linear);
            h.strength = strength;
            return h;
        }

        /// <summary>Position shake (anchored position) that returns to the start.</summary>
        public static TweenHandle Shake(RectTransform t, float strength = 12f, float dur = 0.3f)
        {
            var running = FindRunning(t, TweenHandle.Kind.Shake);
            if (running != null)
            {
                if (running.started && t != null) t.anchoredPosition = running.from;
                running.active = false;
            }
            var h = Create(TweenHandle.Kind.Shake, t, Vector4.zero, dur, Ease.Linear);
            h.strength = strength;
            return h;
        }

        public static TweenHandle Delay(float seconds, Action action)
        {
            var h = Create(TweenHandle.Kind.Delay, null, Vector4.zero, seconds, Ease.Linear);
            if (action != null) h.onComplete = action;
            return h;
        }

        // ---- convenience presets (DS.Motion)

        /// <summary>PopIn: scale from 0.6x to `to` (default 1) with OutBack(1.4) in 0.45 s.</summary>
        public static TweenHandle PopIn(Transform t, float to = 1f, float dur = DS.Motion.Slow)
        {
            if (t != null) t.localScale = Vector3.one * (to * DS.Motion.PopInFrom);
            return Scale(t, to, dur, Ease.OutBack).SetOvershoot(DS.Motion.PopOvershoot);
        }

        /// <summary>Endless CTA pulse 1 → 1.06 (InOutSine yoyo, 0.9 s per cycle) around the current scale.</summary>
        public static TweenHandle Pulse(Transform t, float scale = DS.Motion.PulseScale, float period = DS.Motion.PulsePeriod)
        {
            Vector3 baseScale = t != null ? t.localScale : Vector3.one;
            return Scale(t, baseScale * scale, period * 0.5f, Ease.InOutSine).SetLoops(-1, true);
        }

        /// <summary>Endless idle bob ±amplitude px (InOutSine yoyo, 2.2 s per cycle) around the current position.</summary>
        public static TweenHandle Bob(RectTransform t, float amplitude = DS.Motion.BobAmplitude, float period = DS.Motion.BobPeriod)
        {
            if (t == null) return Create(TweenHandle.Kind.Move, null, Vector4.zero, period, Ease.InOutSine);
            Vector2 p = t.anchoredPosition;
            t.anchoredPosition = p - new Vector2(0, amplitude);
            return Move(t, p + new Vector2(0, amplitude), period * 0.5f, Ease.InOutSine).SetLoops(-1, true);
        }

        // ---------------------------------------------------------------------------------------- control

        /// <summary>Kills every tween whose target is this Transform/Graphic/CanvasGroup (or its RectTransform).
        /// Also kills tweens linked (SetLink) to the object. Passing a GameObject kills every tween on its components.</summary>
        public static void Kill(object target, bool complete = false)
        {
            if (target == null) return;
            // Snapshot the count: completing a tween may start new ones (OnComplete), which must survive this call.
            int count = _tweens.Count;
            for (int i = 0; i < count && i < _tweens.Count; i++)
            {
                var t = _tweens[i];
                if (t.active && Matches(t, target)) KillHandle(t, complete);
            }
        }

        /// <summary>Kills every running tween.</summary>
        public static void KillAll(bool complete = false)
        {
            int count = _tweens.Count;
            for (int i = 0; i < count && i < _tweens.Count; i++)
                if (_tweens[i].active) KillHandle(_tweens[i], complete);
        }

        /// <summary>True if any running tween animates/links this object.</summary>
        public static bool IsTweening(object target)
        {
            if (target == null) return false;
            for (int i = 0; i < _tweens.Count; i++)
                if (_tweens[i].active && Matches(_tweens[i], target)) return true;
            return false;
        }

        /// <summary>Advances all tweens by dt seconds (scaled and unscaled alike). For EditMode tests and tools;
        /// at runtime the hidden runner calls the internal tick every frame.</summary>
        public static void ManualUpdate(float dt) => Tick(dt, dt);

        static bool Matches(TweenHandle t, object key)
        {
            if (ReferenceEquals(t.target, key) || ReferenceEquals(t.link, key)) return true;
            if (key is GameObject go)
            {
                if (t.target is Component tc && tc != null && tc.gameObject == go) return true;
                if (t.link is GameObject lg && lg == go) return true;
                if (t.link is Component lc && lc != null && lc.gameObject == go) return true;
                return false;
            }
            // "or its RectTransform": Kill(graphic) also stops Scale/Move tweens on graphic.transform.
            if (key is Component kc && !(key is Transform) && kc != null)
                return t.target is Transform tt && tt != null && tt == kc.transform;
            return false;
        }

        internal static void KillHandle(TweenHandle t, bool complete)
        {
            if (t == null || !t.active) return;
            t.active = false;
            if (!complete) return;
            if (t.hasTarget && t.target == null) return;
            if (!t.started) Begin(t);
            float lin;
            if (t.loops < 0) lin = t.yoyo ? 0f : 1f;
            else lin = t.yoyo && t.loops % 2 == 0 ? 0f : 1f;
            Apply(t, Evaluate(t.ease, lin, t.overshoot), lin, true);
            Invoke(t.onComplete);
        }

        // ---------------------------------------------------------------------------------------- engine

        static TweenHandle Create(TweenHandle.Kind kind, Object target, Vector4 to, float dur, Ease ease)
        {
            var h = new TweenHandle
            {
                kind = kind,
                target = target,
                hasTarget = kind != TweenHandle.Kind.Value && kind != TweenHandle.Kind.Delay,
                to = to,
                duration = Mathf.Max(0f, dur),
                ease = ease,
                active = true,
            };
            // A missing target makes a dead tween (keeps call sites null-safe and chainable).
            if (h.hasTarget && target == null)
            {
                h.active = false;
                return h;
            }
            EnsureRunner();
            _tweens.Add(h);
            return h;
        }

        static TweenHandle FindRunning(Transform t, TweenHandle.Kind kind)
        {
            if (t == null) return null;
            for (int i = 0; i < _tweens.Count; i++)
            {
                var h = _tweens[i];
                if (h.active && h.kind == kind && ReferenceEquals(h.target, t)) return h;
            }
            return null;
        }

        static void EnsureRunner()
        {
            if (_runner != null || _quitting || !Application.isPlaying) return;
            // No HideFlags.DontSave: DontSave GameObjects survive leaving Play Mode in the Editor, and a leaked runner
            // would tick every tween twice in the next session.
            var go = new GameObject("[PotionPop.Tween]") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<TweenRunner>();
        }

        /// <summary>Called by the runner; stray runners (not the registered one) destroy themselves.</summary>
        internal static void RunnerUpdate(TweenRunner runner)
        {
            if (_runner == null) _runner = runner;
            if (_runner != runner)
            {
                Object.Destroy(runner.gameObject);
                return;
            }
            Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }

        internal static void RunnerDestroyed(TweenRunner runner)
        {
            if (ReferenceEquals(_runner, runner)) _runner = null;
        }

        internal static void Tick(float dt, float unscaledDt)
        {
            // A callback calling ManualUpdate would compact the list under the outer loop.
            if (_ticking) return;
            _ticking = true;
            try { TickInternal(dt, unscaledDt); }
            finally { _ticking = false; }
        }

        static void TickInternal(float dt, float unscaledDt)
        {
            dt = Mathf.Clamp(dt, 0f, MaxStep);
            unscaledDt = Mathf.Clamp(unscaledDt, 0f, MaxStep);
            // Tweens created during this tick (e.g. in OnComplete) start next frame.
            int count = _tweens.Count;
            for (int i = 0; i < count; i++)
            {
                var t = _tweens[i];
                if (!t.active) continue;
                if ((t.hasTarget && t.target == null) || (t.hasLink && t.link == null))
                {
                    t.active = false;
                    continue;
                }
                float d = t.unscaled ? unscaledDt : dt;
                if (t.delay > 0f)
                {
                    t.delay -= d;
                    if (t.delay > 0f) continue;
                    d = -t.delay;
                    t.delay = 0f;
                }
                if (!t.started) Begin(t);
                Step(t, d);
            }
            _tweens.RemoveAll(_isDead);
        }

        static void Begin(TweenHandle t)
        {
            t.started = true;
            switch (t.kind)
            {
                case TweenHandle.Kind.Scale:
                case TweenHandle.Kind.Punch:
                    t.from = ((Transform)t.target).localScale; break;
                case TweenHandle.Kind.Move:
                case TweenHandle.Kind.Shake:
                    t.from = ((RectTransform)t.target).anchoredPosition; break;
                case TweenHandle.Kind.MoveWorld:
                case TweenHandle.Kind.Bezier:
                    t.from = ((Transform)t.target).position; break;
                case TweenHandle.Kind.MoveLocal:
                    t.from = ((Transform)t.target).localPosition; break;
                case TweenHandle.Kind.Rotate:
                    t.from = new Vector4(Mathf.DeltaAngle(0f, ((Transform)t.target).localEulerAngles.z), 0, 0, 0); break;
                case TweenHandle.Kind.FadeGroup:
                    t.from = new Vector4(((CanvasGroup)t.target).alpha, 0, 0, 0); break;
                case TweenHandle.Kind.FadeGraphic:
                    t.from = new Vector4(((Graphic)t.target).color.a, 0, 0, 0); break;
                case TweenHandle.Kind.Color:
                    t.from = ((Graphic)t.target).color; break;
                case TweenHandle.Kind.Size:
                    t.from = ((RectTransform)t.target).sizeDelta; break;
            }
        }

        static void Step(TweenHandle t, float d)
        {
            bool finished = false;
            if (t.duration <= 0f)
            {
                finished = true;
            }
            else
            {
                t.elapsed += d;
                while (t.elapsed >= t.duration)
                {
                    if (t.loops < 0 || t.loopIndex + 1 < t.loops)
                    {
                        t.elapsed -= t.duration;
                        t.loopIndex++;
                    }
                    else
                    {
                        finished = true;
                        break;
                    }
                }
            }

            float lin;
            if (finished) lin = t.yoyo && t.loops > 0 && t.loops % 2 == 0 ? 0f : 1f;
            else
            {
                lin = t.elapsed / t.duration;
                if (t.yoyo && (t.loopIndex & 1) == 1) lin = 1f - lin;
            }

            Apply(t, Evaluate(t.ease, lin, t.overshoot), lin, finished);
            if (t.onUpdate != null)
            {
                try { t.onUpdate(lin); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (finished && t.active)
            {
                t.active = false;
                Invoke(t.onComplete);
            }
        }

        static void Apply(TweenHandle t, float e, float lin, bool final)
        {
            switch (t.kind)
            {
                case TweenHandle.Kind.Scale:
                    ((Transform)t.target).localScale = Vector3.LerpUnclamped(t.from, t.to, e); break;
                case TweenHandle.Kind.Move:
                    ((RectTransform)t.target).anchoredPosition = Vector2.LerpUnclamped(t.from, t.to, e); break;
                case TweenHandle.Kind.MoveWorld:
                    ((Transform)t.target).position = Vector3.LerpUnclamped(t.from, t.to, e); break;
                case TweenHandle.Kind.MoveLocal:
                    ((Transform)t.target).localPosition = Vector3.LerpUnclamped(t.from, t.to, e); break;
                case TweenHandle.Kind.Bezier:
                {
                    Vector3 p0 = t.from, p2 = t.to;
                    float u = 1f - e;
                    ((Transform)t.target).position = u * u * p0 + 2f * u * e * t.control + e * e * p2;
                    break;
                }
                case TweenHandle.Kind.Rotate:
                {
                    var tr = (Transform)t.target;
                    var eu = tr.localEulerAngles;
                    eu.z = Mathf.LerpUnclamped(t.from.x, t.to.x, e);
                    tr.localEulerAngles = eu;
                    break;
                }
                case TweenHandle.Kind.FadeGroup:
                    ((CanvasGroup)t.target).alpha = Mathf.Lerp(t.from.x, t.to.x, e); break;
                case TweenHandle.Kind.FadeGraphic:
                {
                    var g = (Graphic)t.target;
                    var c = g.color;
                    c.a = Mathf.Lerp(t.from.x, t.to.x, e);
                    g.color = c;
                    break;
                }
                case TweenHandle.Kind.Color:
                    ((Graphic)t.target).color = UnityEngine.Color.Lerp(t.from, t.to, e); break;
                case TweenHandle.Kind.Size:
                    ((RectTransform)t.target).sizeDelta = Vector2.LerpUnclamped(t.from, t.to, e); break;
                case TweenHandle.Kind.Value:
                    if (t.onValue != null)
                    {
                        try { t.onValue(Mathf.LerpUnclamped(t.from.x, t.to.x, e)); }
                        catch (Exception ex) { Debug.LogException(ex); t.active = false; }
                    }
                    break;
                case TweenHandle.Kind.Punch:
                {
                    // Damped oscillation: quick swell, one counter-swing, settle back exactly on the rest scale.
                    float s = final ? 0f : Mathf.Sin(lin * Mathf.PI * 3f) * (1f - lin) * t.strength;
                    ((Transform)t.target).localScale = (Vector3)t.from * (1f + s);
                    break;
                }
                case TweenHandle.Kind.Shake:
                {
                    Vector2 offset = final ? Vector2.zero : UnityEngine.Random.insideUnitCircle * (t.strength * (1f - lin));
                    ((RectTransform)t.target).anchoredPosition = (Vector2)t.from + offset;
                    break;
                }
            }
        }

        static void Invoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------------------------------------------------------------------------------- easing

        public static float Evaluate(Ease ease, float t) => Evaluate(ease, t, 1.70158f);

        /// <summary>Easing curve value; `overshoot` is used by OutBack/InBack.</summary>
        public static float Evaluate(Ease ease, float t, float overshoot)
        {
            switch (ease)
            {
                case Ease.Linear: return t;
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case Ease.OutBack:
                {
                    float c1 = overshoot, c3 = c1 + 1f, u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                case Ease.InBack:
                {
                    float c1 = overshoot, c3 = c1 + 1f;
                    return c3 * t * t * t - c1 * t * t;
                }
                case Ease.OutElastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    const float c4 = 2f * Mathf.PI / 3f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                }
                case Ease.OutBounce: return OutBounce(t);
                case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                default: return t;
            }
        }

        static float OutBounce(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }

    /// <summary>Hidden runner that drives Tween every frame (created on demand, survives scene loads).</summary>
    [AddComponentMenu("")]
    internal sealed class TweenRunner : MonoBehaviour
    {
        void Update() => Tween.RunnerUpdate(this);
        void OnDestroy() => Tween.RunnerDestroyed(this);
    }
}
