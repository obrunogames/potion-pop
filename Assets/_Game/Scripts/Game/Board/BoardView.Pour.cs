// ============================================================================================================
// Board module: the pour (PlayPour) and the undo (PlayUndo = the same pour played from the target back into the
// source). Timeline of one PourJob (seconds, ~0.9–1.1 s for the source, the target is free earlier):
//   [uncork]  undo of a completion only: the cork pops off the bottle that gives the liquid back (0.3 s)
//   fly       the source travels on an arc from its current pose (lifted or resting) so that its lip lands just
//             above the target's mouth, tilting to the angle where its liquid reaches the lip
//             (BottleShape.PourAngleDeg of its current volume, at least MinTiltDeg since full bottles are brim-full)
//             and pouring TOWARD the target (side chosen from the positions, flipped when the tilted bottle would
//             leave the board)
//   drain     the stream falls (head reaches the surface in 0.07 s), the source drains while the target fills
//             (a lag of 0.07 s), the source keeps tilting further as it empties (angle re-solved every frame from
//             the remaining volume while the lip stays above the target's mouth), splash + bubbles at the landing
//             point, Sfx.Pour at the start and Sfx.PourEnd at the end; hidden units of the poured color flip on the
//             way out
//   return    the stream thins and its tail falls, the source swings back to its slot and lands with a squash
//   after     target: hidden units flip when completed, cork pops in (OnBottleCorked), released; source: its new
//             top "?" flips, released; stones tick / shatter (latest request wins).
// Positions are recomputed every frame from the live transforms, so relayouts (Extra Bottle) during a pour are fine.
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game.Board
{
    public sealed partial class BoardView
    {
        static readonly Color[] BubbleColors = { new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.6f) };
        readonly Color[] _splashColors = new Color[2];

        /// <summary>Pour: the source flies above the target, tilts, a stream of the color falls, the target fills, the
        /// source returns; then reveals (new top of the source, hidden units poured), the cork when completed (with
        /// sparkles and a little bounce) and stone counter ticks / stone breaking.</summary>
        public void PlayPour(PourResult result, Action onDone = null)
        {
            _answerStamp++;
            if (result == null || Board == null || result.from == result.to || View(result.from) == null || View(result.to) == null)
            {
                if (result != null && _selected == result.from) DeselectInternal(true, false);
                Invoke(onDone);
                return;
            }
            if (_selected == result.from)
            {
                _selected = -1;   // the job takes the lifted bottle over from its current pose
                View(result.from).SetSelectedGlow(false);
            }
            var job = new PourJob(result.from, result.to, result.color, Mathf.Max(0, result.amount))
            {
                hiddenPoured = Mathf.Max(0, result.hiddenPoured),
                completeTarget = result.completed,
            };
            job.reveals.AddRange(result.reveals);
            job.ticks.AddRange(result.lockTicks);
            job.Hold(result.from);
            job.Hold(result.to);
            job.Snapshot(result.from, Board);
            job.Snapshot(result.to, Board);
            Run(job, onDone);
        }

        /// <summary>Undo: the liquid flows back from result.to into result.from (cork pops off if uncorked; stone
        /// counters go back up).</summary>
        public void PlayUndo(UndoResult result, Action onDone = null)
        {
            if (result == null || Board == null || result.from == result.to || View(result.from) == null || View(result.to) == null)
            {
                Invoke(onDone);
                return;
            }
            var job = new PourJob(result.to, result.from, result.color, Mathf.Max(0, result.amount))
            {
                undo = true,
                uncorkSource = result.uncorked,
            };
            job.ticks.AddRange(result.lockTicks);
            job.Hold(result.from);
            job.Hold(result.to);
            job.Snapshot(result.from, Board);
            job.Snapshot(result.to, Board);
            Run(job, onDone);
        }

        /// <summary>
        /// Side the source hovers on (+1 = right of the target, tilting counter-clockwise over its left lip): the side it
        /// comes from, unless the tilted bottle would stick out of the board there and the other side has room.
        /// </summary>
        internal int ChooseSide(BottleView s, BottleView d)
        {
            float sx = s.home.x, dx = d.home.x;
            int side = sx > dx + 1f ? 1 : (sx < dx - 1f ? -1 : (dx > 0f ? -1 : 1));
            if (_layout == null || _rect == null) return side;
            float reach = _layout.BottleHeight * 0.9f;
            float half = _rect.rect.width * 0.5f + _layout.BottleWidth * 0.5f;
            bool outHere = Mathf.Abs(dx + side * reach) > half;
            bool outThere = Mathf.Abs(dx - side * reach) > half;
            if (outHere && !outThere) side = -side;
            return side;
        }

        /// <summary>Droplets + a splash crown where the stream lands in <paramref name="v"/>.</summary>
        internal void Splash(BottleView v, int color)
        {
            if (v == null || v.innerFx == null) return;
            _splashColors[0] = Liquids.Color(color);
            _splashColors[1] = Liquids.Light(color);
            Vector2 p = v.streamLanding;
            FX.Burst(v.innerFx, p, "ui_circle", 6, _splashColors, 240f, 0.34f, 9f, -950f);
            var sp = BottleArt.Optional("fx_splash");
            if (sp == null) return;
            var img = FX.AcquireImage(v.innerFx, sp);
            if (img == null) return;
            var rt = img.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.12f);
            rt.sizeDelta = new Vector2(0.62f * U, 0.42f * U);
            rt.localPosition = new Vector3(p.x, p.y, 0f);
            rt.localScale = new Vector3(0.3f, 0.3f, 1f);
            img.color = Liquids.Light(color);
            Tween.Scale(rt, 1.1f, 0.22f, Ease.OutQuad);
            Tween.Fade(img, 0f, 0.2f).SetDelay(0.12f).OnComplete(() => FX.ReleaseImage(img));
        }

        /// <summary>A couple of bubbles rising from just under the landing point.</summary>
        internal void Bubbles(BottleView v)
        {
            if (v == null || v.innerFx == null) return;
            var p = v.streamLanding + new Vector2(UnityEngine.Random.Range(-0.16f, 0.16f) * U, -0.1f * U);
            FX.Burst(v.innerFx, p, "fx_bubble", 2, BubbleColors, 60f, 0.5f, 15f, 280f);
        }

        // ============================================================================================================

        sealed class PourJob : BoardJob
        {
            const float Lag = 0.07f;          // stream fall time before the target starts filling
            const float ReturnTime = 0.32f;
            const float PourGapUnits = 0.42f; // lip height above the target's mouth (shape units): clears its shoulders
            const float PourOffsetUnits = 0.1f;  // toward the source side (the stream still lands well inside the neck)
            // A brim-full bottle reaches its lip at 0°: never pour from (nearly) upright, the body would hang over the
            // target. Below the physical angle the liquid simply rests against the mouth until the drain takes it.
            const float MinTiltDeg = 60f;

            readonly int _src, _dst, _color, _amount;
            public int hiddenPoured;
            public bool completeTarget, uncorkSource, undo;
            public readonly List<Reveal> reveals = new List<Reveal>(4);
            public readonly List<LockTick> ticks = new List<LockTick>(4);

            BottleView _s, _d;
            BottleShape _shape;
            int _side;
            Vector2 _lipLocal;
            float _tFly, _tPour, _drain, _tDrainEnd, _tReturn, _tBack;
            float[] _srcStart;
            int _srcVersion, _dstVersion;
            int _dstLayer = -1;
            float _dstStart;
            float _v0;
            Vector3 _p0;
            float _a0, _sc0;
            Vector3 _h0;
            float _hA;
            bool _poured, _returning, _landed, _splashed, _streamDone;
            float _nextBubble;

            public PourJob(int src, int dst, int color, int amount)
            {
                _src = src;
                _dst = dst;
                _color = color;
                _amount = amount;
            }

            public override void Begin()
            {
                _s = board.View(_src);
                _d = board.View(_dst);
                _shape = board._shape;
                if (_s == null || _d == null || _shape == null)
                {
                    finished = true;
                    return;
                }
                board.PrepareForJob(_s, true);
                board.PrepareForJob(_d, false);
                _s.slot.SetAsLastSibling();
                _side = board.ChooseSide(_s, _d);
                _lipLocal = _s.LipLocal(-_side);

                // liquids: remember where the drain starts; the target grows its matching top layer or a new one
                var sl = _s.liquid.Layers;
                _srcStart = new float[sl.Count];
                for (int i = 0; i < sl.Count; i++) _srcStart[i] = Mathf.Max(0f, sl[i].amount);
                _v0 = _s.liquid.TotalAmount * _s.liquid.UnitVolume;
                var dl = _d.liquid.Layers;
                int top = dl.Count - 1;
                if (top >= 0 && !dl[top].hidden && dl[top].color == _color)
                {
                    _dstLayer = top;
                    _dstStart = dl[top].amount;
                }
                else
                {
                    _dstLayer = _d.liquid.AddLayer(new LiquidLayer { color = _color, amount = 0f, reveal = 1f });
                    _dstStart = 0f;
                }
                _srcVersion = _s.liquid.Version;
                _dstVersion = _d.liquid.Version;

                // start pose (lifted or resting)
                _p0 = _s.body.position;
                _a0 = Mathf.DeltaAngle(0f, _s.body.localEulerAngles.z);
                _sc0 = _s.body.localScale.x;

                // timeline
                float t0 = uncorkSource ? 0.3f : 0f;
                float unitW = Mathf.Max(1e-5f, _s.slot.lossyScale.x);
                float distPx = (PoseFor(PourPoint(), StartAngle()) - _p0).magnitude / unitW;
                float fly = Mathf.Clamp(0.24f + distPx * 0.00022f, 0.26f, 0.4f);
                _tFly = t0;
                _tPour = t0 + fly;
                _drain = Mathf.Clamp(0.3f + 0.06f * _amount, 0.32f, 0.6f);
                _tDrainEnd = _tPour + _drain;
                float streamEnd = _tDrainEnd + Lag + 0.06f;
                _tReturn = _tDrainEnd + 0.05f;
                _tBack = _tReturn + ReturnTime;

                if (uncorkSource) At(0f, () => { if (_s.slot != null) _s.PopCorkOff(); });
                At(_tPour, StartPour);
                At(_tDrainEnd + Lag * 0.5f, () => AudioManager.Play(Sfx.PourEnd, undo ? 0.7f : 1f));
                At(streamEnd, () => { if (_d.slot != null) _d.liquid.Wobble(4f); });

                // target: reveals (a completion shows every hidden unit), then the cork
                float targetDone = streamEnd + 0.1f;
                float tickStart = undo ? Mathf.Max(0.2f, t0) : streamEnd + 0.1f;
                float corkAt = streamEnd + 0.04f;
                int flips = 0;
                for (int i = 0; i < reveals.Count; i++)
                {
                    if (reveals[i].bottle != _dst) continue;
                    int layer = reveals[i].index;
                    bool sound = flips == 0;
                    At(corkAt + flips * 0.06f, () => { if (_d.slot != null) _d.FlipLayer(layer, 0f, sound); });
                    flips++;
                }
                if (flips > 0)
                {
                    corkAt += 0.32f + flips * 0.06f;
                    targetDone = corkAt + 0.05f;
                }
                if (completeTarget)
                {
                    int dst = _dst;
                    At(corkAt, () => { if (_d.slot != null) _d.PopCork(() => board.RaiseCorked(dst)); });
                    targetDone = corkAt + 0.62f;
                    if (!undo) tickStart = corkAt + 0.32f;
                }
                At(targetDone, () => Release(_dst));

                // source: its new top reveals while it flies back
                float sourceDone = _tBack + 0.1f;
                for (int i = 0; i < reveals.Count; i++)
                {
                    if (reveals[i].bottle != _src) continue;
                    int layer = reveals[i].index;
                    At(_tReturn + 0.1f, () => { if (_s.slot != null) _s.FlipLayer(layer, 0f, true); });
                    sourceDone = Mathf.Max(sourceDone, _tReturn + 0.5f);
                }
                At(sourceDone, () => Release(_src));

                float ticksEnd = board.ScheduleLockTicks(this, ticks, tickStart);
                duration = Mathf.Max(Mathf.Max(targetDone, sourceDone), ticksEnd) + 0.02f;
                _s.SetShadow(0.25f, 0.7f, 0.2f);
            }

            /// <summary>Tilt (signed by side) that brings <paramref name="volume"/> to the lip, at least <see cref="MinTiltDeg"/>.</summary>
            float TiltFor(float volume) => _side * Mathf.Max(MinTiltDeg, _shape.PourAngleDeg(volume) + 1.5f);

            float StartAngle() => TiltFor(_v0);

            /// <summary>World point the source's lip hovers at: just above the target's mouth, slightly to the source side.</summary>
            Vector3 PourPoint()
            {
                Vector3 mouth = _d.MouthWorld;
                return mouth + _d.visual.TransformVector(new Vector3(_side * PourOffsetUnits * U, PourGapUnits * U, 0f));
            }

            /// <summary>Body position that puts the lip on <paramref name="lip"/> at <paramref name="angle"/> (scale 1).</summary>
            Vector3 PoseFor(Vector3 lip, float angle)
            {
                float unitW = _s.slot.lossyScale.x;
                var q = Quaternion.Euler(0f, 0f, angle);
                return lip - q * new Vector3(_lipLocal.x * unitW, _lipLocal.y * unitW, 0f);
            }

            void SetPose(Vector3 lip, float angle, float scale)
            {
                var b = _s.body;
                b.localRotation = Quaternion.Euler(0f, 0f, angle);
                if (Mathf.Abs(b.localScale.x - scale) > 1e-5f) b.localScale = new Vector3(scale, scale, 1f);
                b.position = lip - b.TransformVector(new Vector3(_lipLocal.x, _lipLocal.y, 0f));
            }

            void StartPour()
            {
                if (_s.slot == null || _d.slot == null) return;
                _poured = true;
                AudioManager.Play(Sfx.Pour, undo ? 0.7f : 1f);
                if (hiddenPoured > 0)
                {
                    // liquid is liquid: the hidden units of this color right under the top run leave with it
                    var layers = _s.liquid.Layers;
                    int flipped = 0;
                    for (int i = layers.Count - 1; i >= 0 && flipped < hiddenPoured; i--)
                    {
                        if (!layers[i].hidden) continue;
                        if (layers[i].color != _color) break;
                        _s.FlipLayer(i, flipped * 0.05f, flipped == 0);
                        flipped++;
                    }
                }
                _d.streamColor = _color;
                _d.streamHead = 0f;
                _d.streamTail = 0f;
                _d.streamWidth = 0f;
                _d.streamOn = true;
                _nextBubble = t + Lag;
            }

            public override void Tick(float dt)
            {
                if (_s == null || _d == null || _s.slot == null || _d.slot == null)
                {
                    finished = true;
                    return;
                }
                if (t < _tFly) return;

                if (t >= _tPour)
                {
                    SetDrained(_amount * Ease01((t - _tPour) / _drain));
                    SetFilled(_amount * Ease01((t - _tPour - Lag) / _drain));
                }

                if (t < _tPour) Fly((t - _tFly) / Mathf.Max(0.01f, _tPour - _tFly));
                else if (t < _tReturn) Hold();
                else if (t < _tBack) Return((t - _tReturn) / ReturnTime);
                else if (!_landed) Land();

                UpdateStream();
            }

            static float Ease01(float x) => Tween.Evaluate(Ease.InOutSine, Mathf.Clamp01(x));

            void SetDrained(float drained)
            {
                var lq = _s.liquid;
                if (lq.Version != _srcVersion) return;
                var layers = lq.Layers;
                float rem = drained;
                bool dirty = false;
                for (int i = Mathf.Min(layers.Count, _srcStart.Length) - 1; i >= 0; i--)
                {
                    float start = _srcStart[i];
                    float take = Mathf.Min(start, rem);
                    rem -= take;
                    float a = start - take;
                    var l = layers[i];
                    if (Mathf.Abs(l.amount - a) <= 1e-5f) continue;
                    l.amount = a;
                    layers[i] = l;
                    dirty = true;
                }
                if (dirty) lq.MarkDirty();
            }

            void SetFilled(float filled)
            {
                var lq = _d.liquid;
                if (lq.Version != _dstVersion || _dstLayer < 0 || _dstLayer >= lq.Layers.Count) return;
                var l = lq.Layers[_dstLayer];
                float a = _dstStart + filled;
                if (Mathf.Abs(l.amount - a) <= 1e-5f) return;
                l.amount = a;
                lq.SetLayer(_dstLayer, l);
            }

            void Fly(float p)
            {
                p = Mathf.Clamp01(p);
                float e = Tween.Evaluate(Ease.InOutQuad, p);
                float aT = StartAngle();
                Vector3 end = PoseFor(PourPoint(), aT);
                float unitW = _s.slot.lossyScale.x;
                float arc = (_s.GlassHeightPx * 0.16f + Mathf.Abs(end.x - _p0.x) / Mathf.Max(1e-5f, unitW) * 0.06f) * unitW;
                Vector3 ctrl = (_p0 + end) * 0.5f + new Vector3(0f, arc, 0f);
                float u = 1f - e;
                Vector3 pos = u * u * _p0 + 2f * u * e * ctrl + e * e * end;
                float angle = Mathf.LerpAngle(_a0, aT, Tween.Evaluate(Ease.InOutCubic, Mathf.Clamp01(p * 1.08f)));
                float sc = Mathf.Lerp(_sc0, 1f, e);
                var b = _s.body;
                b.localRotation = Quaternion.Euler(0f, 0f, angle);
                b.localScale = new Vector3(sc, sc, 1f);
                b.position = pos;
            }

            void Hold()
            {
                SetPose(PourPoint(), TiltFor(_s.liquid.TotalAmount * _s.liquid.UnitVolume), 1f);
            }

            void Return(float p)
            {
                var b = _s.body;
                if (!_returning)
                {
                    _returning = true;
                    _h0 = b.position;
                    _hA = Mathf.DeltaAngle(0f, b.localEulerAngles.z);
                    _s.SetShadow(1f, 1f, ReturnTime);
                }
                p = Mathf.Clamp01(p);
                float e = Tween.Evaluate(Ease.InOutQuad, p);
                Vector3 end = _s.slot.position;
                float unitW = _s.slot.lossyScale.x;
                Vector3 ctrl = (_h0 + end) * 0.5f + new Vector3(0f, _s.GlassHeightPx * 0.08f * unitW, 0f);
                float u = 1f - e;
                b.position = u * u * _h0 + 2f * u * e * ctrl + e * e * end;
                b.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(_hA, 0f, Tween.Evaluate(Ease.OutCubic, p)));
            }

            void Land()
            {
                _landed = true;
                var b = _s.body;
                b.anchoredPosition = Vector2.zero;
                b.localRotation = Quaternion.identity;
                b.localScale = Vector3.one;
                _s.Land();
            }

            void UpdateStream()
            {
                if (!_poured || _streamDone) return;
                float since = t - _tPour;
                float tail = Mathf.Clamp01((t - _tDrainEnd) / (Lag + 0.05f));
                if (tail >= 1f)
                {
                    // once only: the target may already belong to the next pour (its own stream)
                    _streamDone = true;
                    StopStream();
                    return;
                }
                float head = Mathf.Clamp01(since / Lag);
                float width = Mathf.Clamp01(since / 0.06f) * (1f - 0.55f * Mathf.Clamp01((t - _tDrainEnd + 0.04f) / 0.1f));
                _d.streamOn = true;
                _d.streamTopWorld = _s.visual.TransformPoint(new Vector3(_lipLocal.x, _lipLocal.y, 0f));
                _d.streamHead = head;
                _d.streamTail = tail;
                _d.streamWidth = width;
                if (head >= 1f)
                {
                    if (!_splashed)
                    {
                        _splashed = true;
                        board.Splash(_d, _color);
                    }
                    if (t >= _nextBubble && t < _tDrainEnd + Lag)
                    {
                        _nextBubble = t + 0.085f;
                        board.Bubbles(_d);
                    }
                }
            }

            /// <summary>Hides the stream if the target is still ours (never touches the next pour's stream).</summary>
            void StopStream()
            {
                if (_d != null && Holds(_dst)) _d.streamOn = false;
            }

            public override void End()
            {
                StopStream();
                if (_s != null && _s.slot != null && !_landed)
                {
                    var b = _s.body;
                    b.anchoredPosition = Vector2.zero;
                    b.localRotation = Quaternion.identity;
                    b.localScale = Vector3.one;
                    _s.SetShadow(1f, 1f, 0f);
                }
            }

            public override void Abort() => StopStream();
        }
    }
}
