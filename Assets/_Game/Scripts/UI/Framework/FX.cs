// ============================================================================================================
// Pooled uGUI particle effects (bursts, sparkles, confetti, rings, floating text, reward flights, screen shake).
// Particles are plain Images simulated by one hidden runner (no ParticleSystem, works on any canvas). Images are
// recycled through a pool; nothing allocates per frame. Default space = ScreenManager.Instance.FxLayer.
// ============================================================================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace PotionPop.UI
{
    /// <summary>Pooled uGUI particle effects drawn on the FX layer (or a given space).</summary>
    public static class FX
    {
        /// <summary>Hard cap of simultaneously simulated particles (extra spawns are skipped).</summary>
        public const int MaxParticles = 600;

        enum Mode : byte { Burst, Sparkle, Confetti, Ring }

        sealed class Particle
        {
            public RectTransform rt;
            public Image img;
            public Mode mode;
            public Vector2 pos, vel;
            public float rot, rotVel, age, life, size, aspect, gravity, drag;
            public float phase, freq, amp;
            public Color color;
        }

        static readonly List<Particle> _active = new List<Particle>(256);
        static readonly Stack<Particle> _pool = new Stack<Particle>(256);
        static readonly Stack<TMP_Text> _textPool = new Stack<TMP_Text>(16);
        static FXRunner _runner;
        static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _active.Clear();
            _pool.Clear();
            _textPool.Clear();
            _runner = null;
            _quitting = false;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        static void OnQuit() => _quitting = true;

        /// <summary>Number of live particles (debug).</summary>
        public static int ActiveParticles => _active.Count;

        // ---------------------------------------------------------------------------------------- public effects

        public static void Burst(RectTransform space, Vector2 localPos, string sprite, int count, Color[] colors,
            float speed = 600f, float life = 0.7f, float size = 40f, float gravity = -1200f)
        {
            space = Resolve(space);
            if (space == null || count <= 0) return;
            var sp = UISprites.Get(string.IsNullOrEmpty(sprite) ? "ui_circle" : sprite);
            if (sp == null) sp = UISprites.Circle;
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(space, sp);
                if (p == null) return;
                float a = Random.value * Mathf.PI * 2f;
                float s = speed * Random.Range(0.45f, 1f);
                p.mode = Mode.Burst;
                p.pos = localPos;
                p.vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s + new Vector2(0f, speed * 0.25f);
                p.rot = Random.Range(0f, 360f);
                p.rotVel = Random.Range(-360f, 360f);
                p.life = life * Random.Range(0.75f, 1.15f);
                p.size = size * Random.Range(0.7f, 1.2f);
                p.gravity = gravity;
                p.drag = 1.2f;
                p.color = Pick(colors, Color.white);
                Init(p);
            }
        }

        /// <summary>Twinkling fx_sparkle stars popping around a point.</summary>
        public static void Sparkles(RectTransform space, Vector2 localPos, int count = 10)
        {
            space = Resolve(space);
            if (space == null || count <= 0) return;
            var sp = UISprites.Get("fx_sparkle");
            if (sp == null) sp = UISprites.Glow;
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(space, sp);
                if (p == null) return;
                Vector2 dir = Random.insideUnitCircle;
                p.mode = Mode.Sparkle;
                p.pos = localPos + dir * 70f;
                p.vel = dir.normalized * Random.Range(60f, 220f);
                p.rot = Random.Range(0f, 90f);
                p.rotVel = Random.Range(-120f, 120f);
                p.life = Random.Range(0.45f, 0.85f);
                p.age = -Random.Range(0f, 0.18f);    // slight stagger
                p.size = Random.Range(50f, 100f);
                p.gravity = 0f;
                p.drag = 2.5f;
                p.color = Random.value < 0.6f ? Color.white : DS.Colors.Accent;
                Init(p);
            }
        }

        /// <summary>Candy confetti raining from above the top edge of `space`, fluttering and spinning.</summary>
        public static void Confetti(RectTransform space, int count = 120)
        {
            space = Resolve(space);
            if (space == null || count <= 0) return;
            var sp = UISprites.Confetti;
            Rect r = space.rect;
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(space, sp);
                if (p == null) return;
                p.mode = Mode.Confetti;
                p.pos = new Vector2(Random.Range(r.xMin, r.xMax), r.yMax + Random.Range(20f, 700f));
                p.vel = new Vector2(Random.Range(-120f, 120f), Random.Range(-420f, -160f));
                p.rot = Random.Range(0f, 360f);
                p.rotVel = Random.Range(-260f, 260f);
                p.life = Random.Range(4.2f, 5.6f);
                p.size = Random.Range(22f, 34f);
                p.aspect = Random.Range(1.3f, 1.9f);
                p.gravity = -520f;
                p.drag = 0.9f;
                p.phase = Random.Range(0f, Mathf.PI * 2f);
                p.freq = Random.Range(2.2f, 4.2f);
                p.amp = Random.Range(40f, 110f);
                p.color = Pick(DS.Colors.Candy, Color.white);
                Init(p);
            }
        }

        /// <summary>Expanding, fading ring (shockwave) at a point.</summary>
        public static void Ring(RectTransform space, Vector2 localPos, Color color, float maxSize = 400f)
        {
            space = Resolve(space);
            if (space == null) return;
            var p = Spawn(space, UISprites.Ring);
            if (p == null) return;
            p.mode = Mode.Ring;
            p.pos = localPos;
            p.vel = Vector2.zero;
            p.rot = 0f;
            p.rotVel = 0f;
            p.life = 0.45f;
            p.size = maxSize;
            p.color = color;
            Init(p);
        }

        /// <summary>Text that pops in, rises ~140 px and fades (combo labels, "+50"...). Text is already localized.</summary>
        public static void FloatingText(RectTransform space, Vector2 localPos, string text, TextStyle style, Color color)
        {
            space = Resolve(space);
            if (space == null || string.IsNullOrEmpty(text)) return;
            var t = AcquireText(space, style);
            if (t == null) return;
            t.text = text;
            t.color = color;
            var rt = t.rectTransform;
            rt.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.zero;
            float dur = 1.0f;
            Tween.Scale(rt, 1.15f, 0.16f, Ease.OutBack);
            Tween.Scale(rt, 1f, 0.12f, Ease.OutQuad).SetDelay(0.16f);
            Tween.MoveLocal(rt, new Vector3(localPos.x, localPos.y + 140f, 0f), dur, Ease.OutCubic);
            Tween.Fade(t, 0f, 0.3f).SetDelay(dur - 0.3f).OnComplete(() => ReleaseText(t));
        }

        /// <summary>Shakes ScreensLayer (camera-less screen shake).</summary>
        public static void ScreenShake(float strength = 14f, float duration = 0.25f)
        {
            var sm = ScreenManager.Instance;
            if (sm == null || sm.ScreensLayer == null) return;
            Tween.Shake(sm.ScreensLayer, strength, duration);
        }

        /// <summary>Converts a world position (e.g. from RectTransform.position) to a local position in space.</summary>
        public static Vector2 ToLocal(RectTransform space, Vector3 worldPos)
        {
            space = Resolve(space);
            if (space == null) return Vector2.zero;
            Vector3 l = space.InverseTransformPoint(worldPos);
            return new Vector2(l.x, l.y);
        }

        /// <summary>Local position (in the FX layer) of the center of any RectTransform.</summary>
        public static Vector2 LocalCenterOf(RectTransform target, RectTransform space = null)
        {
            if (target == null) return Vector2.zero;
            return ToLocal(space, target.TransformPoint(target.rect.center));
        }

        /// <summary>Celebration combo used by win screens: confetti + sparkles + ring at a point.</summary>
        public static void Celebrate(Vector2 localPos, RectTransform space = null)
        {
            Ring(space, localPos, DS.Colors.Accent, 520f);
            Sparkles(space, localPos, 14);
            Burst(space, localPos, "ui_star_small", 24, DS.Colors.Gold, 900f, 0.9f, 46f, -1400f);
            Confetti(space, 120);
        }

        // ---------------------------------------------------------------------------------------- reward flights

        /// <summary>
        /// Spawns `count` sprites at a world position that pop out, then fly (quadratic bezier, InOutCubic 0.55-0.8 s,
        /// staggered 0.04 s) to target, punching it on each arrival with Sfx.Coin (coin sprites) or Sfx.Star.
        /// Callbacks are cosmetic (grant rewards before calling); they still fire if the target disappears.
        /// </summary>
        public static void FlyRewards(string spriteName, int count, Vector3 fromWorld, RectTransform target,
            Action onEachArrive = null, Action onAllArrived = null)
        {
            var space = Resolve(null);
            if (count <= 0)
            {
                SafeInvoke(onAllArrived);
                return;
            }
            count = Mathf.Min(count, 30);   // more than ~30 flying icons reads as noise and costs fill-rate
            if (space == null)
            {
                for (int i = 0; i < count; i++) SafeInvoke(onEachArrive);
                SafeInvoke(onAllArrived);
                return;
            }
            var sp = UISprites.Get(spriteName);
            if (sp == null) sp = UISprites.Circle;
            bool coin = !string.IsNullOrEmpty(spriteName) && spriteName.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0;
            Sfx sfx = coin ? Sfx.Coin : Sfx.Star;
            Vector3 toWorld = target != null ? target.TransformPoint(target.rect.center) : fromWorld;
            float unit = Mathf.Max(0.0001f, space.lossyScale.x);   // world units per canvas unit
            float iconSize = 96f;
            if (target != null)
            {
                Rect tr = target.rect;
                float targetSize = Mathf.Max(tr.width, tr.height) * target.lossyScale.x / unit;
                if (targetSize > 1f) iconSize = Mathf.Clamp(targetSize, 64f, 120f);
            }
            var flight = new Flight { remaining = count, target = target, onEach = onEachArrive, onAll = onAllArrived, sfx = sfx };

            for (int i = 0; i < count; i++)
            {
                var img = AcquireImage(space, sp);
                if (img == null) { flight.Arrive(i); continue; }
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(iconSize, iconSize);
                rt.position = fromWorld;
                rt.localScale = Vector3.zero;
                img.preserveAspect = true;

                // 1) pop out to a scattered point around the source
                Vector2 scatter = count == 1 ? Vector2.zero : Random.insideUnitCircle * (60f + 8f * count);
                Vector3 popWorld = fromWorld + new Vector3(scatter.x, scatter.y, 0f) * unit;
                float popDur = 0.22f;
                Tween.Scale(rt, 1f, popDur, Ease.OutBack);
                Tween.MoveWorld(rt, popWorld, popDur, Ease.OutCubic);

                // 2) bezier flight to the target, staggered
                Vector3 mid = (popWorld + toWorld) * 0.5f;
                Vector3 dir = toWorld - popWorld;
                Vector3 perp = new Vector3(-dir.y, dir.x, 0f).normalized;
                float bend = dir.magnitude * Random.Range(0.18f, 0.35f) * (Random.value < 0.5f ? -1f : 1f);
                Vector3 control = mid + perp * bend + new Vector3(0f, 120f * unit, 0f);
                float flyDur = Random.Range(DS.Motion.FlyMin, DS.Motion.FlyMax);
                float delay = popDur + i * DS.Motion.FlyStagger;
                int index = i;
                Tween.MoveBezier(rt, control, toWorld, flyDur, Ease.InOutCubic).SetDelay(delay)
                    .OnComplete(() =>
                    {
                        ReleaseImage(img);
                        flight.Arrive(index);
                    });
                Tween.Scale(rt, 0.8f, flyDur, Ease.InQuad).SetDelay(delay);
            }
        }

        sealed class Flight
        {
            public int remaining;
            public RectTransform target;
            public Action onEach, onAll;
            public Sfx sfx;

            public void Arrive(int index)
            {
                if (target != null) Tween.Punch(target, 0.22f, 0.3f);
                AudioManager.Play(sfx, 0.8f, 1f + Mathf.Min(index, 12) * 0.03f);
                if ((index & 1) == 0) Haptics.Play(HapticType.Light);
                SafeInvoke(onEach);
                remaining--;
                if (remaining == 0) SafeInvoke(onAll);
            }
        }

        // ---------------------------------------------------------------------------------------- pooling (public)

        /// <summary>Gets a pooled raycast-free Image parented to `space` (return it with ReleaseImage).</summary>
        public static Image AcquireImage(RectTransform space, Sprite sprite)
        {
            space = Resolve(space);
            if (space == null) return null;
            var p = TakeFromPool(space);
            if (p == null) return null;
            p.img.sprite = sprite;
            p.img.color = Color.white;
            p.img.preserveAspect = false;
            p.img.enabled = true;
            return p.img;
        }

        /// <summary>Returns an Image obtained from AcquireImage to the pool.</summary>
        public static void ReleaseImage(Image img)
        {
            if (img == null) return;
            Tween.Kill(img.rectTransform);
            Tween.Kill(img);
            img.enabled = false;
            _pool.Push(new Particle { rt = img.rectTransform, img = img });
        }

        // ---------------------------------------------------------------------------------------- internals

        static RectTransform Resolve(RectTransform space)
        {
            if (space != null) return space;
            var sm = ScreenManager.Instance;
            return sm != null ? sm.FxLayer : null;
        }

        static Color Pick(Color[] colors, Color fallback) =>
            colors != null && colors.Length > 0 ? colors[Random.Range(0, colors.Length)] : fallback;

        static Particle TakeFromPool(RectTransform space)
        {
            Particle p = null;
            while (_pool.Count > 0)
            {
                p = _pool.Pop();
                if (p.rt != null) break;   // its old space may have been destroyed with it
                p = null;
            }
            if (p == null)
            {
                var rt = UIKit.Rect("fx", space);
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                p = new Particle { rt = rt, img = img };
            }
            else if (p.rt.parent != space)
            {
                p.rt.SetParent(space, false);
            }
            var r = p.rt;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.localRotation = Quaternion.identity;
            r.localScale = Vector3.one;
            r.SetAsLastSibling();
            return p;
        }

        static Particle Spawn(RectTransform space, Sprite sprite)
        {
            if (_active.Count >= MaxParticles) return null;
            var p = TakeFromPool(space);
            if (p == null) return null;
            p.img.sprite = sprite;
            p.img.preserveAspect = false;
            p.img.enabled = true;
            p.age = 0f;
            p.aspect = 1f;
            p.phase = p.freq = p.amp = 0f;
            _active.Add(p);
            EnsureRunner();
            return p;
        }

        static void Init(Particle p)
        {
            p.img.color = p.color;
            Render(p, 0f);
        }

        static void EnsureRunner()
        {
            if (_runner != null || _quitting || !Application.isPlaying) return;
            // No HideFlags.DontSave (it would survive leaving Play Mode in the Editor and simulate twice next time).
            var go = new GameObject("[PotionPop.FX]") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<FXRunner>();
        }

        internal static void RunnerUpdate(FXRunner runner)
        {
            if (_runner == null) _runner = runner;
            if (_runner != runner)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                return;
            }
            Tick(Time.unscaledDeltaTime);
        }

        internal static void RunnerDestroyed(FXRunner runner)
        {
            if (ReferenceEquals(_runner, runner)) _runner = null;
        }

        internal static void Tick(float dt)
        {
            dt = Mathf.Clamp(dt, 0f, Tween.MaxStep);
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                if (p.rt == null)
                {
                    RemoveAt(i, false);
                    continue;
                }
                p.age += dt;
                if (p.age >= p.life)
                {
                    RemoveAt(i, true);
                    continue;
                }
                if (p.age < 0f) { p.img.enabled = false; continue; }  // staggered start
                p.img.enabled = true;
                float k = Mathf.Max(0f, 1f - p.drag * dt);
                p.vel.y += p.gravity * dt;
                p.vel *= k;
                p.pos += p.vel * dt;
                p.rot += p.rotVel * dt;
                Render(p, p.age / p.life);
            }
        }

        static void Render(Particle p, float t)
        {
            float scale = 1f, alpha = 1f, sx = 1f;
            Vector2 offset = Vector2.zero;
            switch (p.mode)
            {
                case Mode.Burst:
                    scale = t < 0.12f ? Mathf.Lerp(0.3f, 1f, t / 0.12f) : (t > 0.6f ? Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f) : 1f);
                    alpha = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                    break;
                case Mode.Sparkle:
                    scale = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    break;
                case Mode.Confetti:
                {
                    float time = p.age * p.freq + p.phase;
                    offset.x = Mathf.Sin(time) * p.amp;
                    sx = Mathf.Cos(time * 1.7f);             // paper flip
                    alpha = t > 0.85f ? 1f - (t - 0.85f) / 0.15f : 1f;
                    break;
                }
                case Mode.Ring:
                {
                    float e = Tween.Evaluate(Ease.OutCubic, t);
                    scale = Mathf.Lerp(0.08f, 1f, e);
                    alpha = 1f - t * t;
                    break;
                }
            }
            var rt = p.rt;
            rt.localPosition = new Vector3(p.pos.x + offset.x, p.pos.y + offset.y, 0f);
            rt.localRotation = Quaternion.Euler(0f, 0f, p.rot);
            rt.sizeDelta = new Vector2(p.size, p.size * p.aspect);
            rt.localScale = new Vector3(scale * sx, scale, 1f);
            var c = p.color;
            c.a *= alpha;
            p.img.color = c;
        }

        static void RemoveAt(int i, bool recycle)
        {
            var p = _active[i];
            int last = _active.Count - 1;
            _active[i] = _active[last];
            _active.RemoveAt(last);
            if (!recycle || p.rt == null) return;
            p.img.enabled = false;
            _pool.Push(p);
        }

        static TMP_Text AcquireText(RectTransform space, TextStyle style)
        {
            TMP_Text t = null;
            while (_textPool.Count > 0)
            {
                t = _textPool.Pop();
                if (t != null) break;
            }
            if (t == null)
            {
                t = UIKit.Text(space, "", style, new Vector2(900, 200));
                if (t == null) return null;
                t.name = "FloatingText";
            }
            else
            {
                if (t.transform.parent != space) t.transform.SetParent(space, false);
                t.gameObject.SetActive(true);
            }
            DS.Apply(t, style);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900, DS.Size(style) * 1.6f);
            rt.SetAsLastSibling();
            return t;
        }

        static void ReleaseText(TMP_Text t)
        {
            if (t == null) return;
            Tween.Kill(t.rectTransform);
            Tween.Kill(t);
            t.gameObject.SetActive(false);
            _textPool.Push(t);
        }

        static void SafeInvoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Hidden runner that simulates FX particles every frame (unscaled time).</summary>
    [AddComponentMenu("")]
    internal sealed class FXRunner : MonoBehaviour
    {
        void Update() => FX.RunnerUpdate(this);
        void OnDestroy() => FX.RunnerDestroyed(this);
    }
}
