// ============================================================================================================
// Board module: level intro, win celebration, hint mode and the idle shimmer of completed bottles.
//  * PlayIntro — racks fade in, bottles drop into their slots row by row with a soft OutBack bounce and a slosh.
//  * PlayWin   — completed bottles hop in a left → right wave (squash on landing) with sparkles and bubbles.
//  * ShowHint  — source and target pulse with a white glow; hand_pointer loops "tap source → tap target".
//  * Shimmer   — every ~2 s one idle completed bottle gets a light sweep through its liquid.
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
        static readonly Vector2 HandPivot = new Vector2(0.31f, 0.95f);   // fingertip of hand_pointer
        static readonly Vector2 HandSize = new Vector2(190f, 207f);
        const float HintCycle = 2f;

        bool _hintActive;
        Move _hint;
        Image _hand;
        float _hintT;
        float _shimmerTimer = 1.6f;

        // ------------------------------------------------------------------------------------------------ intro

        /// <summary>Bottles drop into place one after another (level start).</summary>
        public void PlayIntro(Action onDone)
        {
            EnsureInit();
            if (Board == null || _views.Count == 0)
            {
                _awaitingIntro = false;
                Invoke(onDone);
                return;
            }
            var job = new IntroJob();
            for (int i = 0; i < _views.Count; i++) job.Hold(i);
            Run(job, onDone);
        }

        sealed class IntroJob : BoardJob
        {
            const float Drop = 0.5f;
            float[] _delay;
            float _height;

            public override void Begin()
            {
                var views = board._views;
                int n = views.Count;
                _delay = new float[n];
                _height = board._layout != null ? board._layout.BottleHeight * 0.6f : 260f;
                float last = 0f;
                for (int i = 0; i < n; i++)
                {
                    var v = views[i];
                    if (board._selected == i) board._selected = -1;
                    v.ResetPose();
                    v.SetSelectedGlow(false);
                    _delay[i] = 0.12f + v.row * 0.13f + v.column * 0.05f;
                    last = Mathf.Max(last, _delay[i]);
                    v.introOffset = new Vector2(0f, _height);
                    v.introScale = 0.85f;
                    v.group.alpha = 0f;
                    int index = i;
                    At(_delay[i] + Drop * 0.55f, () =>
                    {
                        var view = board.View(index);
                        if (view == null || view.slot == null) return;
                        view.liquid.Wobble(5f);
                        AudioManager.Play(Sfx.Pop, 0.35f, 0.92f + index * 0.02f);
                    });
                }
                board._awaitingIntro = false;
                board.FadeInRacks(0.35f);
                AudioManager.Play(Sfx.Whoosh, 0.7f);
                duration = last + Drop + 0.08f;
            }

            public override void Tick(float dt)
            {
                var views = board._views;
                for (int i = 0; i < views.Count && i < _delay.Length; i++)
                {
                    var v = views[i];
                    if (v == null || v.slot == null) continue;
                    float tau = t - _delay[i];
                    if (tau <= 0f)
                    {
                        v.group.alpha = 0f;
                        continue;
                    }
                    float e = Tween.Evaluate(Ease.OutBack, Mathf.Clamp01(tau / Drop), 1.35f);
                    v.introOffset = new Vector2(0f, (1f - e) * _height);
                    v.introScale = Mathf.LerpUnclamped(0.85f, 1f, e);
                    v.group.alpha = Mathf.Clamp01(tau / 0.15f);
                }
            }

            public override void End() => Finish();
            public override void Abort() => Finish();

            void Finish()
            {
                var views = board._views;
                for (int i = 0; i < views.Count; i++)
                {
                    var v = views[i];
                    if (v == null || v.slot == null) continue;
                    v.introOffset = Vector2.zero;
                    v.introScale = 1f;
                    v.group.alpha = 1f;
                }
                board._awaitingIntro = false;
                board.UpdatePlacement(0f);
            }
        }

        void FadeInRacks(float duration)
        {
            for (int i = 0; i < _rackImages.Count; i++)
            {
                var img = _rackImages[i];
                if (img == null) continue;
                float a = i < _rackAlphas.Count ? _rackAlphas[i] : 1f;
                Tween.Kill(img);
                Tween.Fade(img, a, duration);
            }
        }

        // ------------------------------------------------------------------------------------------------ win

        /// <summary>Level won: completed bottles bounce in a wave with sparkles.</summary>
        public void PlayWin(Action onDone)
        {
            if (Board == null || _views.Count == 0)
            {
                Invoke(onDone);
                return;
            }
            HideHint();
            DeselectInternal(true, false);
            var job = new WinJob();
            for (int i = 0; i < _views.Count; i++) job.Hold(i);
            Run(job, onDone);
        }

        sealed class WinJob : BoardJob
        {
            const float Hop = 0.5f;
            readonly List<int> _order = new List<int>(16);
            float[] _start;
            bool[] _landed;

            public override void Begin()
            {
                var views = board._views;
                for (int i = 0; i < views.Count; i++)
                {
                    var v = views[i];
                    if (v == null) continue;
                    board.PrepareForJob(v, false);
                    if (v.corked || v.shown.completed) _order.Add(i);
                }
                _order.Sort((a, b) =>
                {
                    var va = views[a];
                    var vb = views[b];
                    float xa = va.home.x, xb = vb.home.x;
                    return Mathf.Abs(xa - xb) > 1f ? xa.CompareTo(xb) : va.row.CompareTo(vb.row);
                });
                _start = new float[_order.Count];
                _landed = new bool[_order.Count];
                float last = 0f;
                for (int k = 0; k < _order.Count; k++)
                {
                    _start[k] = 0.08f + k * 0.08f;
                    last = _start[k];
                    int b = _order[k];
                    At(_start[k] + Hop * 0.45f, () =>
                    {
                        var v = board.View(b);
                        if (v == null || v.slot == null) return;
                        float s = v.FxScale;
                        FX.Sparkles(board._fx, board.FxLocal(v.MouthWorld), 6);
                        FX.Burst(v.innerFx, v.liquid.UnitCenterUpright(1f), "fx_bubble", 4, BubbleColors, 90f, 0.7f, 16f, 320f);
                        v.Shimmer();
                        if (s > 0f) Haptics.Play(HapticType.Light);
                    });
                }
                duration = (_order.Count > 0 ? last + Hop : 0f) + 0.35f;
            }

            public override void Tick(float dt)
            {
                var views = board._views;
                for (int k = 0; k < _order.Count; k++)
                {
                    var v = board.View(_order[k]);
                    if (v == null || v.slot == null) continue;
                    float tau = t - _start[k];
                    if (tau < 0f) continue;
                    if (tau >= Hop)
                    {
                        if (_landed[k]) continue;
                        _landed[k] = true;
                        v.body.anchoredPosition = Vector2.zero;
                        v.body.localRotation = Quaternion.identity;
                        v.Bounce(1.1f);
                        continue;
                    }
                    float p = tau / Hop;
                    float h = Mathf.Sin(p * Mathf.PI) * v.GlassHeightPx * 0.2f;
                    v.body.anchoredPosition = new Vector2(0f, h);
                    v.body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(p * Mathf.PI * 2f) * 4f);
                }
            }

            public override void End()
            {
                for (int k = 0; k < _order.Count; k++)
                {
                    var v = board.View(_order[k]);
                    if (v == null || v.slot == null) continue;
                    v.body.anchoredPosition = Vector2.zero;
                    v.body.localRotation = Quaternion.identity;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ hint

        /// <summary>Hint: pulses the source and target bottles with an arrow between them until HideHint.</summary>
        public void ShowHint(Move move)
        {
            EnsureInit();
            HideHintInternal();
            if (Board == null || move.from == move.to || View(move.from) == null || View(move.to) == null) return;
            _hint = move;
            _hintActive = true;
            _hintT = 0f;
            var a = View(move.from);
            var b = View(move.to);
            if (a.holder == null) a.SetHint(true);
            if (b.holder == null) b.SetHint(true);
            EnsureHand();
            if (_hand != null)
            {
                _hand.gameObject.SetActive(true);
                _hand.color = new Color(1f, 1f, 1f, 0f);
                float s = Mathf.Clamp(BottleScale, 0.75f, 1.1f);
                _hand.rectTransform.sizeDelta = HandSize * s;
            }
            UpdateHint(0f);
        }

        public void HideHint() => HideHintInternal();

        void HideHintInternal()
        {
            if (_hintActive)
            {
                View(_hint.from)?.SetHint(false);
                View(_hint.to)?.SetHint(false);
            }
            _hintActive = false;
            if (_hand != null) _hand.gameObject.SetActive(false);
        }

        void ReapplyHint()
        {
            if (!_hintActive) return;
            var m = _hint;
            ShowHint(m);
        }

        void EnsureHand()
        {
            if (_hand != null || _hintLayer == null) return;
            var sp = BottleArt.Optional("hand_pointer");
            _hand = UIKit.NewImage(_hintLayer, "Hand", sp != null ? sp : UISprites.Circle, Color.white);
            var rt = _hand.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = sp != null ? HandPivot : new Vector2(0.5f, 0.5f);
            rt.sizeDelta = sp != null ? HandSize : new Vector2(70f, 70f);
            _hand.preserveAspect = true;
            _hand.gameObject.SetActive(false);
        }

        void UpdateHint(float dt)
        {
            if (!_hintActive || _hand == null) return;
            var a = View(_hint.from);
            var b = View(_hint.to);
            if (a == null || b == null || a.slot == null || b.slot == null)
            {
                HideHintInternal();
                return;
            }
            _hintT += dt;
            float p = (_hintT % HintCycle) / HintCycle;
            Vector2 pa = HintPoint(a), pb = HintPoint(b);
            Vector2 pos;
            float alpha = 1f, press = 1f;
            if (p < 0.1f)
            {
                pos = pa + new Vector2(30f, -40f) * (1f - p / 0.1f);
                alpha = p / 0.1f;
            }
            else if (p < 0.24f)
            {
                pos = pa;
                press = 1f - 0.14f * Mathf.Sin((p - 0.1f) / 0.14f * Mathf.PI);
            }
            else if (p < 0.62f)
            {
                float e = Tween.Evaluate(Ease.InOutCubic, (p - 0.24f) / 0.38f);
                pos = Vector2.LerpUnclamped(pa, pb, e) + new Vector2(0f, Mathf.Sin(e * Mathf.PI) * 70f);
            }
            else if (p < 0.78f)
            {
                pos = pb;
                press = 1f - 0.14f * Mathf.Sin((p - 0.62f) / 0.16f * Mathf.PI);
            }
            else
            {
                pos = pb;
                alpha = 1f - (p - 0.78f) / 0.22f;
            }
            var rt = _hand.rectTransform;
            rt.anchoredPosition = pos;
            rt.localScale = new Vector3(press, press, 1f);
            var c = _hand.color;
            c.a = Mathf.Clamp01(alpha);
            _hand.color = c;
        }

        /// <summary>Where the fingertip taps a bottle (upper middle of the glass), in hint-layer coordinates.</summary>
        Vector2 HintPoint(BottleView v)
        {
            Vector3 w = v.slot.TransformPoint(new Vector2(v.CenterLocal.x, v.GlassHeightPx * 0.62f));
            return (Vector2)_hintLayer.InverseTransformPoint(w);
        }

        // ------------------------------------------------------------------------------------------------ shimmer

        void UpdateShimmer(float dt)
        {
            if (Board == null || _views.Count == 0) return;
            _shimmerTimer -= dt;
            if (_shimmerTimer > 0f) return;
            _shimmerTimer = UnityEngine.Random.Range(1.4f, 2.6f);
            int n = _views.Count;
            int start = UnityEngine.Random.Range(0, n);
            for (int k = 0; k < n; k++)
            {
                var v = _views[(start + k) % n];
                if (v == null || v.slot == null || !v.corked || v.holder != null || v.lifted || v.group.alpha < 0.99f) continue;
                v.Shimmer();
                break;
            }
        }
    }
}
