// ============================================================================================================
// Board module: booster animations (the session plays the booster sounds; the view only animates).
//  * PlayAddBottle  — the extra bottle pops in at its new slot with sparkles while the others slide over.
//  * PlayRemoveColor — every unit of the color glows (hidden ones flip first), leaves as a glowing orb that rises
//                      out of the mouth and flies to the given world point with a sparkle trail; the liquid above
//                      settles; corks of emptied bottles vanish; new tops reveal; new completions cork; stones tick.
//  * PlayShuffle    — bottles shake while their liquid swirls to white, the new contents appear under the flash.
//  * PlayRevealAll  — every "?" flips to its color with a sparkle, staggered bottle by bottle.
//  * PlayBreakLock  — the stone shatters (rewarded ad).
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game.Board
{
    public sealed partial class BoardView
    {
        static readonly Color[] SparkleWhite = { Color.white, new Color(1f, 0.95f, 0.75f, 1f) };
        readonly List<Image> _orbImages = new List<Image>(16);

        // ------------------------------------------------------------------------------------------------ extra bottle

        /// <summary>Extra Bottle: an empty bottle (already in the model at <paramref name="bottle"/>) pops into the
        /// layout; the others slide to make room.</summary>
        public void PlayAddBottle(int bottle, Action onDone = null)
        {
            if (Board == null || bottle < 0 || bottle >= Board.Count)
            {
                Invoke(onDone);
                return;
            }
            EnsureViews();
            var nv = View(bottle);
            if (nv != null && nv.holder == null)
            {
                nv.group.alpha = 0f;   // pops in when its job starts
                nv.introScale = 0.3f;
            }
            ApplyLayout(true);   // the others slide; new views snap to their first slot (BottleView.placed)
            var job = new AddBottleJob(bottle);
            job.Hold(bottle);
            job.Snapshot(bottle, Board);
            Run(job, onDone);
        }

        sealed class AddBottleJob : BoardJob
        {
            readonly int _b;
            BottleView _v;

            public AddBottleJob(int bottle) { _b = bottle; }

            public override void Begin()
            {
                _v = board.View(_b);
                if (_v == null)
                {
                    finished = true;
                    return;
                }
                board.PrepareForJob(_v, false);
                _v.group.alpha = 0f;
                _v.introScale = 0.3f;
                duration = 0.6f;
                At(0.14f, () =>
                {
                    if (_v.slot == null) return;
                    Vector2 p = board.FxLocal(_v.RestCenterWorld);
                    FX.Sparkles(board._fx, p, 10);
                    FX.Ring(board._fx, p, new Color(1f, 1f, 1f, 0.8f), 300f * _v.FxScale);
                });
            }

            public override void Tick(float dt)
            {
                if (_v == null || _v.slot == null) return;
                float p = Mathf.Clamp01(t / 0.46f);
                _v.introScale = Mathf.LerpUnclamped(0.3f, 1f, Tween.Evaluate(Ease.OutBack, p, 1.9f));
                _v.group.alpha = Mathf.Clamp01(t / 0.12f);
            }

            public override void End() => Restore();
            public override void Abort() => Restore();

            void Restore()
            {
                if (_v == null) return;
                _v.introScale = 1f;
                if (_v.group != null) _v.group.alpha = 1f;
            }
        }

        // ------------------------------------------------------------------------------------------------ wand / rainbow

        /// <summary>Magic Wand / Rainbow Potion: every unit of the color glows, rises out of its bottle as magic
        /// sparkles and flies to <paramref name="flyTo"/> (world position, e.g. the booster button); the liquid above
        /// settles down; corks of emptied bottles vanish; stones tick.</summary>
        public void PlayRemoveColor(RemoveColorResult result, Vector3 flyTo, Action onDone = null)
        {
            if (result == null || Board == null)
            {
                Invoke(onDone);
                return;
            }
            var job = new RemoveColorJob(result, flyTo);
            HoldChanged(job, result.changed);
            HoldChanged(job, result.emptiedCompleted);
            for (int i = 0; i < result.reveals.Count; i++) HoldOne(job, result.reveals[i].bottle);
            // bottles completed by the removal (e.g. a sorted bottle freed by the stone tick) changed too
            for (int i = 0; i < _views.Count && i < Board.Count; i++)
                if (_views[i].shown.completed != Board[i].Completed) HoldOne(job, i);
            Run(job, onDone);
        }

        void HoldChanged(BoardJob job, List<int> bottles)
        {
            if (bottles == null) return;
            for (int i = 0; i < bottles.Count; i++) HoldOne(job, bottles[i]);
        }

        void HoldOne(BoardJob job, int b)
        {
            if (View(b) == null || job.Holds(b)) return;
            job.Hold(b);
            job.Snapshot(b, Board);
        }

        Vector2 FxLocal(Vector3 world) => (Vector2)_fx.InverseTransformPoint(world);

        void TrackOrb(Image img) { if (img != null) _orbImages.Add(img); }

        void ReleaseOrb(Image img)
        {
            if (img == null) return;
            _orbImages.Remove(img);
            FX.ReleaseImage(img);
        }

        void ReleaseOrbs()
        {
            for (int i = 0; i < _orbImages.Count; i++) if (_orbImages[i] != null) FX.ReleaseImage(_orbImages[i]);
            _orbImages.Clear();
        }

        sealed class RemoveColorJob : BoardJob
        {
            const float GlowTime = 0.3f, ShrinkTime = 0.4f, Rise = 0.2f;

            struct Orb
            {
                public Image glow, core;
                public Vector3 start, apex, end, ctrl;
                public float launch, fly, nextSpark, size;
                public bool spawned, done;
            }

            readonly RemoveColorResult _r;
            readonly Vector3 _flyTo;
            readonly List<Orb> _orbs = new List<Orb>(16);
            readonly List<int> _bottles = new List<int>(8);
            float[][] _start;
            int[] _versions;

            public RemoveColorJob(RemoveColorResult r, Vector3 flyTo)
            {
                _r = r;
                _flyTo = flyTo;
            }

            public override void Begin()
            {
                var held = Held;
                for (int i = 0; i < held.Count; i++) _bottles.Add(held[i]);
                _start = new float[_bottles.Count][];
                _versions = new int[_bottles.Count];
                int color = _r.color;
                for (int k = 0; k < _bottles.Count; k++)
                {
                    var v = board.View(_bottles[k]);
                    if (v == null) continue;
                    board.PrepareForJob(v, false);
                    var layers = v.liquid.Layers;
                    _start[k] = new float[layers.Count];
                    for (int i = 0; i < layers.Count; i++)
                    {
                        _start[k][i] = layers[i].amount;
                        if (layers[i].color == color && layers[i].hidden) v.FlipLayer(i, 0f, false);
                    }
                    _versions[k] = v.liquid.Version;
                }

                // orbs: one per removed unit, from its slot in the bottle, staggered
                float launch = GlowTime;
                int n = 0;
                for (int i = 0; i < _r.removed.Count; i++)
                {
                    var ru = _r.removed[i];
                    var v = board.View(ru.bottle);
                    if (v == null) continue;
                    Vector2 local = v.liquid.UnitCenterUpright(ru.index);
                    Vector3 start = v.liquid.rectTransform.TransformPoint(local);
                    Vector3 apex = v.visual.TransformPoint(v.MouthLocal + new Vector2(0f, 0.55f * U));
                    float unitW = v.slot.lossyScale.x;
                    Vector3 mid = (apex + _flyTo) * 0.5f;
                    Vector3 dir = _flyTo - apex;
                    var perp = new Vector3(-dir.y, dir.x, 0f).normalized;
                    float bend = dir.magnitude * 0.22f * ((n & 1) == 0 ? 1f : -1f);
                    var orb = new Orb
                    {
                        start = start,
                        apex = apex,
                        end = _flyTo,
                        ctrl = mid + perp * bend + new Vector3(0f, 1.2f * U * unitW, 0f),
                        launch = launch + n * 0.04f,
                        fly = Mathf.Clamp(0.5f + dir.magnitude / Mathf.Max(1f, Screen.height) * 0.3f, 0.5f, 0.8f),
                        size = 0.95f * U * v.FxScale,
                    };
                    _orbs.Add(orb);
                    n++;
                }
                float orbsEnd = launch;
                for (int i = 0; i < _orbs.Count; i++) orbsEnd = Mathf.Max(orbsEnd, _orbs[i].launch + Rise + _orbs[i].fly);

                // after the color left: corks of emptied bottles, reveals, new corks, stones
                float after = GlowTime + ShrinkTime;
                At(after, CorksOff);
                At(after + 0.05f, () => Reveals(after + 0.05f));
                float corkAt = after + 0.4f;
                At(corkAt, CorksOn);
                float ticksEnd = board.ScheduleLockTicks(this, _r.lockTicks, corkAt + 0.3f);
                float release = corkAt + 0.65f;
                At(release, ReleaseAll);
                duration = Mathf.Max(Mathf.Max(release, orbsEnd + 0.05f), ticksEnd);
            }

            public override void Tick(float dt)
            {
                int color = _r.color;
                float glow = Mathf.Clamp01(t / GlowTime);
                float shrink = Tween.Evaluate(Ease.InOutQuad, Mathf.Clamp01((t - GlowTime) / ShrinkTime));
                for (int k = 0; k < _bottles.Count; k++)
                {
                    var v = board.View(_bottles[k]);
                    if (v == null || v.slot == null || _start[k] == null || v.liquid.Version != _versions[k]) continue;
                    var layers = v.liquid.Layers;
                    bool dirty = false;
                    for (int i = 0; i < layers.Count && i < _start[k].Length; i++)
                    {
                        var l = layers[i];
                        if (l.color != color) continue;
                        float pulse = 0.75f + 0.25f * Mathf.Sin(t * 22f);
                        l.glow = glow * pulse;
                        l.amount = _start[k][i] * (1f - shrink);
                        layers[i] = l;
                        dirty = true;
                    }
                    if (dirty) v.liquid.MarkDirty();
                }
                UpdateOrbs();
            }

            void UpdateOrbs()
            {
                for (int i = 0; i < _orbs.Count; i++)
                {
                    var o = _orbs[i];
                    if (o.done) continue;
                    float tau = t - o.launch;
                    if (tau < 0f) continue;
                    if (!o.spawned)
                    {
                        o.spawned = true;
                        var glowSprite = UISprites.Glow;
                        o.glow = FX.AcquireImage(null, glowSprite);
                        o.core = FX.AcquireImage(null, UISprites.Circle);
                        var c = Liquids.Color(_r.color);
                        if (o.glow != null)
                        {
                            o.glow.color = Color.Lerp(c, Color.white, 0.2f);
                            o.glow.rectTransform.sizeDelta = new Vector2(o.size, o.size);
                            board.TrackOrb(o.glow);
                        }
                        if (o.core != null)
                        {
                            o.core.color = Color.Lerp(c, Color.white, 0.75f);
                            o.core.rectTransform.sizeDelta = new Vector2(o.size * 0.32f, o.size * 0.32f);
                            board.TrackOrb(o.core);
                        }
                    }
                    Vector3 pos;
                    float scale;
                    if (tau < Rise)
                    {
                        float e = Tween.Evaluate(Ease.OutQuad, tau / Rise);
                        pos = Vector3.LerpUnclamped(o.start, o.apex, e);
                        scale = Mathf.Lerp(0.35f, 1f, e);
                    }
                    else if (tau < Rise + o.fly)
                    {
                        float e = Tween.Evaluate(Ease.InOutCubic, (tau - Rise) / o.fly);
                        float u = 1f - e;
                        pos = u * u * o.apex + 2f * u * e * o.ctrl + e * e * o.end;
                        scale = Mathf.Lerp(1f, 0.65f, e);
                        if (t >= o.nextSpark)
                        {
                            o.nextSpark = t + 0.07f;
                            FX.Burst(null, FX.ToLocal(null, pos), "fx_sparkle", 1, SparkleWhite, 60f, 0.4f, o.size * 0.42f, 0f);
                        }
                    }
                    else
                    {
                        o.done = true;
                        board.ReleaseOrb(o.glow);
                        board.ReleaseOrb(o.core);
                        o.glow = o.core = null;
                        if (i == 0 || i == _orbs.Count - 1) FX.Sparkles(null, FX.ToLocal(null, o.end), 6);
                        Haptics.Play(HapticType.Light);
                        _orbs[i] = o;
                        continue;
                    }
                    if (o.glow != null)
                    {
                        o.glow.rectTransform.position = pos;
                        o.glow.rectTransform.localScale = new Vector3(scale, scale, 1f);
                    }
                    if (o.core != null)
                    {
                        o.core.rectTransform.position = pos;
                        o.core.rectTransform.localScale = new Vector3(scale, scale, 1f);
                    }
                    _orbs[i] = o;
                }
            }

            void CorksOff()
            {
                for (int i = 0; i < _r.emptiedCompleted.Count; i++)
                {
                    var v = board.View(_r.emptiedCompleted[i]);
                    if (v == null || v.slot == null || !v.corked) continue;
                    v.corked = false;
                    var img = v.cork;
                    var rt = img.rectTransform;
                    Tween.Kill(rt);
                    Tween.Kill(img);
                    Tween.Scale(rt, 0.2f, 0.25f, Ease.InBack);
                    Tween.Fade(img, 0f, 0.25f).OnComplete(() => { if (img != null) img.gameObject.SetActive(false); });
                    Vector2 p = board.FxLocal(rt.position);
                    FX.Burst(board._fx, p, "fx_sparkle", 4, SparkleWhite, 140f * v.FxScale, 0.45f, 32f * v.FxScale, 0f);
                }
            }

            /// <summary>New tops (and whole bottles completed by the removal) flip to their colors. Rebuilds the layers
            /// of those bottles from the post-removal units with the revealed units still hidden, then flips them.</summary>
            float Reveals(float now)
            {
                float end = now;
                for (int k = 0; k < _bottles.Count; k++)
                {
                    int b = _bottles[k];
                    int count = 0;
                    for (int i = 0; i < _r.reveals.Count; i++) if (_r.reveals[i].bottle == b) count++;
                    if (count == 0) continue;
                    var v = board.View(b);
                    var snap = SnapshotOf(b);
                    if (v == null || v.slot == null || snap == null) continue;
                    v.liquid.SetUnits(snap.units, Mathf.Min(snap.units.Count, snap.hidden + count));
                    _versions[k] = v.liquid.Version;
                    int n = 0;
                    for (int i = 0; i < _r.reveals.Count; i++)
                    {
                        if (_r.reveals[i].bottle != b) continue;
                        v.FlipLayer(_r.reveals[i].index, n * 0.06f, n == 0);
                        n++;
                    }
                    end = Mathf.Max(end, now + n * 0.06f + 0.4f);
                }
                return end;
            }

            void CorksOn()
            {
                for (int k = 0; k < _bottles.Count; k++)
                {
                    int b = _bottles[k];
                    var v = board.View(b);
                    var snap = SnapshotOf(b);
                    if (v == null || v.slot == null || snap == null) continue;
                    if (snap.completed && !v.corked) v.PopCork(() => board.RaiseCorked(b));
                }
            }

            public override void End()
            {
                for (int i = 0; i < _orbs.Count; i++)
                {
                    board.ReleaseOrb(_orbs[i].glow);
                    board.ReleaseOrb(_orbs[i].core);
                }
                _orbs.Clear();
            }

            public override void Abort() => End();
        }

        // ------------------------------------------------------------------------------------------------ shuffle

        /// <summary>Shuffle: bottles shake and swirl, then show their new contents.</summary>
        public void PlayShuffle(ShuffleResult result, Action onDone = null)
        {
            if (result == null || Board == null)
            {
                Invoke(onDone);
                return;
            }
            var job = new ShuffleJob(result);
            HoldChanged(job, result.changed);
            for (int i = 0; i < _views.Count && i < Board.Count; i++)
                if (_views[i].shown.completed != Board[i].Completed) HoldOne(job, i);
            Run(job, onDone);
        }

        sealed class ShuffleJob : BoardJob
        {
            const float Shake = 0.64f, Swap = 0.3f;
            readonly ShuffleResult _r;
            readonly List<int> _bottles = new List<int>(12);
            bool _swapped;

            public ShuffleJob(ShuffleResult r) { _r = r; }

            public override void Begin()
            {
                var held = Held;
                for (int i = 0; i < held.Count; i++)
                {
                    _bottles.Add(held[i]);
                    var v = board.View(held[i]);
                    if (v == null) continue;
                    board.PrepareForJob(v, false);
                    v.liquid.Wobble(9f);
                }
                At(Swap, SwapContents);
                At(Shake + 0.06f, () =>
                {
                    for (int i = 0; i < _bottles.Count; i++)
                    {
                        int b = _bottles[i];
                        var v = board.View(b);
                        var snap = SnapshotOf(b);
                        if (v != null && v.slot != null && snap != null && snap.completed && !v.corked) v.PopCork(() => board.RaiseCorked(b));
                    }
                });
                float ticksEnd = board.ScheduleLockTicks(this, _r.lockTicks, Shake + 0.3f);
                duration = Mathf.Max(Shake + 0.65f, ticksEnd);
            }

            void SwapContents()
            {
                _swapped = true;
                for (int i = 0; i < _bottles.Count; i++)
                {
                    int b = _bottles[i];
                    var v = board.View(b);
                    var snap = SnapshotOf(b);
                    if (v == null || v.slot == null || snap == null) continue;
                    v.liquid.SetUnits(snap.units, snap.hidden);
                    v.liquid.Wobble(8f);
                    FX.Burst(board._fx, board.FxLocal(v.CenterWorld), "fx_sparkle", 5, SparkleWhite, 220f * v.FxScale, 0.55f, 40f * v.FxScale, 0f);
                }
            }

            public override void Tick(float dt)
            {
                float p = Mathf.Clamp01(t / Shake);
                float flash = t < Swap ? Mathf.Clamp01(t / Swap) * 0.9f : 0.9f * (1f - Mathf.Clamp01((t - Swap) / (Shake - Swap + 0.1f)));
                float angle = p < 1f ? Mathf.Sin(t * 30f) * 7f * (1f - p) : 0f;
                float squeeze = p < 1f ? 1f - 0.06f * Mathf.Sin(p * Mathf.PI) : 1f;
                for (int i = 0; i < _bottles.Count; i++)
                {
                    var v = board.View(_bottles[i]);
                    if (v == null || v.slot == null) continue;
                    v.visual.localRotation = Quaternion.Euler(0f, 0f, angle * ((i & 1) == 0 ? 1f : -1f));
                    v.visual.localScale = new Vector3(squeeze, 1f / squeeze, 1f);
                    v.liquid.Flash = flash;
                }
            }

            public override void End()
            {
                if (!_swapped) SwapContents();
                for (int i = 0; i < _bottles.Count; i++)
                {
                    var v = board.View(_bottles[i]);
                    if (v == null || v.slot == null) continue;
                    v.visual.localRotation = Quaternion.identity;
                    v.visual.localScale = Vector3.one;
                    v.liquid.Flash = 0f;
                }
            }

            public override void Abort()
            {
                for (int i = 0; i < _bottles.Count; i++)
                {
                    var v = board.View(_bottles[i]);
                    if (v == null || v.slot == null) continue;
                    v.liquid.Flash = 0f;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ crystal ball

        /// <summary>Crystal Ball: every "?" unit flips to its color with a sparkle (staggered).</summary>
        public void PlayRevealAll(List<Reveal> reveals, Action onDone = null)
        {
            if (reveals == null || reveals.Count == 0 || Board == null)
            {
                Invoke(onDone);
                return;
            }
            var job = new RevealAllJob(reveals);
            for (int i = 0; i < reveals.Count; i++) HoldOne(job, reveals[i].bottle);
            Run(job, onDone);
        }

        sealed class RevealAllJob : BoardJob
        {
            readonly List<Reveal> _reveals;

            public RevealAllJob(List<Reveal> reveals) { _reveals = new List<Reveal>(reveals); }

            public override void Begin()
            {
                // bottle order on screen (rows top → bottom, left → right), units bottom → top
                var order = new List<int>(Held);
                order.Sort((a, b) =>
                {
                    var va = board.View(a);
                    var vb = board.View(b);
                    if (va == null || vb == null) return a.CompareTo(b);
                    return va.row != vb.row ? va.row.CompareTo(vb.row) : va.column.CompareTo(vb.column);
                });
                float end = 0.1f;
                for (int k = 0; k < order.Count; k++)
                {
                    int b = order[k];
                    var v = board.View(b);
                    if (v == null) continue;
                    board.PrepareForJob(v, false);
                    int n = 0;
                    for (int i = 0; i < _reveals.Count; i++)
                    {
                        if (_reveals[i].bottle != b) continue;
                        float delay = 0.05f + k * 0.09f + n * 0.05f;
                        v.FlipLayer(_reveals[i].index, delay, n == 0);
                        end = Mathf.Max(end, delay + 0.4f);
                        n++;
                    }
                }
                duration = end + 0.05f;
            }
        }

        // ------------------------------------------------------------------------------------------------ stone (ad)

        /// <summary>A stone broke by rewarded ad (model already unlocked).</summary>
        public void PlayBreakLock(int bottle, Action onDone = null)
        {
            if (Board == null || View(bottle) == null)
            {
                Invoke(onDone);
                return;
            }
            var job = new BreakLockJob(bottle);
            job.Hold(bottle);
            job.Snapshot(bottle, Board);
            Run(job, onDone);
        }

        sealed class BreakLockJob : BoardJob
        {
            readonly int _b;

            public BreakLockJob(int bottle) { _b = bottle; }

            public override void Begin()
            {
                var v = board.View(_b);
                if (v != null) board.PrepareForJob(v, false);
                int seq = this.seq;
                board.ApplyLockTick(new LockTick(_b, 0), seq);
                duration = 0.6f;
            }
        }
    }
}
