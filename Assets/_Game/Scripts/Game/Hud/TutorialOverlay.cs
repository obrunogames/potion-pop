// ============================================================================================================
// Tutorial layer over the board (never blocks input): an animated hand_pointer that taps a point (a bottle, a booster
// tile) or taps one bottle then another ("pick up here, pour there") in a loop, and a speech bubble with Luna pointing
// at it. Positions are re-evaluated every frame through callbacks, so the hand follows relayouts (banner, extra bottle).
// ============================================================================================================
using System;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public sealed class TutorialOverlay : MonoBehaviour
    {
        enum HandMode { None, Tap, Sequence }

        const float TapCycle = 1.25f, SequenceCycle = 2.7f;
        static readonly Vector2 HandSize = new Vector2(190f, 207f);
        /// <summary>Fingertip of hand_pointer (normalized, origin bottom-left).</summary>
        static readonly Vector2 HandPivot = new Vector2(0.31f, 0.95f);

        /// <summary>Default bubble position: distance from the top of the tutorial (= board) area.</summary>
        public const float BubbleTop = 6f;
        const float BubbleHeight = 190f;

        public RectTransform Root { get; private set; }
        public bool HandVisible => _mode != HandMode.None;
        public bool BubbleVisible => _bubble != null && _bubble.gameObject.activeSelf;
        /// <summary>A timed tip (auto-hides) is showing — the session dismisses it as soon as the player starts playing.</summary>
        public bool TimedTipVisible => BubbleVisible && _autoHideBubble > 0f;

        RectTransform _hand;
        Image _handImage;
        RectTransform _bubble;
        TMP_Text _bubbleText;
        Image _mascot;
        CanvasGroup _bubbleGroup;
        HandMode _mode;
        Func<Vector3> _from, _to;
        float _t;
        float _autoHideHand = -1f, _autoHideBubble = -1f;
        bool _ringA, _ringB;
        float _prevPhase;

        public static TutorialOverlay Create(RectTransform parent)
        {
            var root = UIKit.Rect("Tutorial", parent);
            UIKit.Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var tut = root.gameObject.AddComponent<TutorialOverlay>();
            tut.Root = root;
            tut.Build();
            return tut;
        }

        void Build()
        {
            // speech bubble (top of the board area) with Luna pointing
            _bubble = UIKit.Rect("Bubble", Root);
            GameUI.PlaceTop(_bubble, BubbleTop, new Vector2(900f, BubbleHeight));
            _bubbleGroup = _bubble.gameObject.AddComponent<CanvasGroup>();
            var panel = UIKit.Rect("Panel", _bubble);
            UIKit.Stretch(panel, 170f, 10f, 0f, 10f);
            UIKit.Shadow(panel, 22f, -10f, 0.3f);
            var bg = UIKit.RoundedRect(panel, Vector2.zero, Color.white, 44f);
            UIKit.Stretch(bg.rectTransform);
            var inner = UIKit.RoundedRect(bg.rectTransform, Vector2.zero, DS.WithAlpha(DS.Colors.BrandLight, 0.18f), 36f);
            UIKit.Stretch(inner.rectTransform, 8f, 8f, 8f, 8f);
            var tail = UIKit.NewImage(panel, "Tail", UISprites.Rounded, Color.white);
            UIKit.Place(tail.rectTransform, new Vector2(0f, 0.5f), new Vector2(46f, 46f), new Vector2(-14f, -18f));
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tail.transform.SetSiblingIndex(1);
            _bubbleText = UIKit.Text(panel, "", TextStyle.Body, new Vector2(680f, 160f));
            DS.Apply(_bubbleText, TextStyle.Body, 44f);
            UIKit.Stretch(_bubbleText.rectTransform, 36f, 18f, 32f, 22f);
            _mascot = UIKit.Image(_bubble, "mascot_point", new Vector2(170f, 236f));
            UIKit.Place(_mascot.rectTransform, new Vector2(0f, 0.5f), new Vector2(170f, 236f), new Vector2(-6f, -18f));
            _bubble.gameObject.SetActive(false);

            // hand
            _hand = UIKit.Rect("Hand", Root);
            _hand.anchorMin = _hand.anchorMax = new Vector2(0.5f, 0.5f);
            _hand.pivot = HandPivot;
            _hand.sizeDelta = HandSize;
            _handImage = UIKit.Image(_hand, "hand_pointer", HandSize);
            UIKit.Stretch(_handImage.rectTransform);
            _hand.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- hand

        /// <summary>Loops a tap gesture on a point; hides itself after `seconds` (≤ 0 = until HideHand).</summary>
        public void ShowTap(Func<Vector3> at, float seconds = 0f)
        {
            if (at == null || _hand == null) { HideHand(); return; }
            bool same = _mode == HandMode.Tap && _from == at;
            _from = at;
            _to = null;
            _mode = HandMode.Tap;
            if (!same) _t = 0f;
            _autoHideHand = seconds > 0f ? seconds : -1f;
            _hand.gameObject.SetActive(true);
            _hand.SetAsLastSibling();
            Evaluate();
        }

        /// <summary>Loops "tap A, then tap B" (pick up a bottle, pour into another). Positions are world points
        /// evaluated every frame.</summary>
        public void ShowSequence(Func<Vector3> first, Func<Vector3> second)
        {
            if (first == null || second == null || _hand == null) { HideHand(); return; }
            _from = first;
            _to = second;
            _mode = HandMode.Sequence;
            _t = 0f;
            _autoHideHand = -1f;
            _hand.gameObject.SetActive(true);
            _hand.SetAsLastSibling();
            Evaluate();
        }

        public void HideHand()
        {
            _mode = HandMode.None;
            _from = _to = null;
            if (_hand != null) _hand.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- bubble

        /// <summary>Speech bubble with Luna at the default spot; autoHide ≤ 0 keeps it until HideBubble.</summary>
        public void ShowBubble(string key, float autoHide = 0f, params object[] args) => ShowBubbleAt(BubbleTop, key, autoHide, args);

        /// <summary>Speech bubble with Luna, its top edge <paramref name="top"/> units below the top of the area (negative =
        /// above it, over the HUD band). autoHide ≤ 0 keeps it until HideBubble.</summary>
        public void ShowBubbleAt(float top, string key, float autoHide = 0f, params object[] args)
        {
            if (_bubble == null) return;
            _bubble.anchoredPosition = new Vector2(0f, -top);   // PopIn / Punch only touch scale
            _bubbleText.text = Loc.T(key, args);
            bool wasVisible = _bubble.gameObject.activeSelf;
            _bubble.gameObject.SetActive(true);
            _autoHideBubble = autoHide > 0f ? autoHide : -1f;
            Tween.Kill(_bubble);
            Tween.Kill(_bubbleGroup);
            _bubbleGroup.alpha = 1f;
            if (!wasVisible)
            {
                Tween.PopIn(_bubble);
                if (_mascot != null)
                {
                    _mascot.rectTransform.localScale = Vector3.zero;
                    Tween.Scale(_mascot.rectTransform, 1f, DS.Motion.Slow, Ease.OutBack).SetDelay(0.1f);
                }
                AudioManager.Play(Sfx.Pop, 0.7f);
            }
            else
            {
                _bubble.localScale = Vector3.one;
                Tween.Punch(_bubble, 0.08f, 0.3f);
            }
        }

        /// <summary>Bubble at the bottom of the area (keeps the top rows of a big board visible).</summary>
        public void ShowBubbleBottom(string key, float autoHide = 0f, params object[] args)
        {
            float h = Root != null ? Root.rect.height : 0f;
            ShowBubbleAt(Mathf.Max(BubbleTop, h - BubbleHeight - 10f), key, autoHide, args);
        }

        public void HideBubble()
        {
            if (_bubble == null || !_bubble.gameObject.activeSelf) return;
            _autoHideBubble = -1f;
            Tween.Kill(_bubble);
            Tween.Kill(_bubbleGroup);
            var b = _bubble;
            Tween.Scale(b, 0.8f, DS.Motion.PopOutDuration, Ease.InBack);
            Tween.Fade(_bubbleGroup, 0f, DS.Motion.PopOutDuration).OnComplete(() =>
            {
                if (b != null) b.gameObject.SetActive(false);
            });
        }

        public void HideAll()
        {
            HideHand();
            if (_bubble != null)
            {
                Tween.Kill(_bubble);
                Tween.Kill(_bubbleGroup);
                _bubble.gameObject.SetActive(false);
            }
            _autoHideBubble = -1f;
        }

        // ---------------------------------------------------------------------------------------- animation

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_autoHideBubble > 0f)
            {
                _autoHideBubble -= dt;
                if (_autoHideBubble <= 0f) HideBubble();
            }
            if (_mode == HandMode.None) return;
            if (_autoHideHand > 0f)
            {
                _autoHideHand -= dt;
                if (_autoHideHand <= 0f) { HideHand(); return; }
            }
            _t += dt;
            Evaluate();
        }

        void Evaluate()
        {
            if (_hand == null || _from == null) return;
            Vector3 a;
            try { a = _from(); }
            catch (Exception) { HideHand(); return; }

            if (_mode == HandMode.Tap)
            {
                float c = _t % TapCycle;
                if (c < _prevPhase) _ringA = false;
                _prevPhase = c;
                // press at 0.3..0.45, release afterwards
                float press = c < 0.3f ? 0f : c < 0.45f ? (c - 0.3f) / 0.15f : c < 0.7f ? 1f - (c - 0.45f) / 0.25f : 0f;
                _hand.position = a;
                _hand.localRotation = Quaternion.Euler(0f, 0f, -12f);
                _hand.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, press);
                SetAlpha(1f);
                if (!_ringA && c >= 0.45f)
                {
                    _ringA = true;
                    FX.Ring(null, FX.ToLocal(null, a), DS.WithAlpha(Color.white, 0.9f), 220f);
                }
                return;
            }

            // Sequence: appear on A, press A, travel to B along a little arc, press B, fade out, loop.
            Vector3 b;
            try { b = _to(); }
            catch (Exception) { HideHand(); return; }
            float t = _t % SequenceCycle;
            if (t < _prevPhase) { _ringA = false; _ringB = false; }
            _prevPhase = t;
            float alpha, pressA = 0f, pressB = 0f, move;
            if (t < 0.25f) { alpha = t / 0.25f; move = 0f; }
            else if (t < 0.75f) { alpha = 1f; move = 0f; pressA = Press01((t - 0.25f) / 0.5f); }
            else if (t < 1.5f) { alpha = 1f; move = Tween.Evaluate(Ease.InOutCubic, (t - 0.75f) / 0.75f); }
            else if (t < 2.0f) { alpha = 1f; move = 1f; pressB = Press01((t - 1.5f) / 0.5f); }
            else if (t < 2.35f) { alpha = 1f - (t - 2.0f) / 0.35f; move = 1f; }
            else { alpha = 0f; move = 1f; }

            if (!_ringA && t >= 0.5f) { _ringA = true; FX.Ring(null, FX.ToLocal(null, a), DS.WithAlpha(Color.white, 0.9f), 220f); }
            if (!_ringB && t >= 1.75f) { _ringB = true; FX.Ring(null, FX.ToLocal(null, b), DS.WithAlpha(Color.white, 0.9f), 220f); }

            Vector3 mid = (a + b) * 0.5f;
            float lift = (b - a).magnitude * 0.22f;
            Vector3 control = mid + new Vector3(0f, lift, 0f);
            float u = move, iu = 1f - u;
            _hand.position = iu * iu * a + 2f * iu * u * control + u * u * b;
            _hand.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-10f, -16f, move));
            _hand.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, Mathf.Max(pressA, pressB));
            SetAlpha(alpha);
        }

        /// <summary>0 → 1 → 0 press curve over a normalized phase (down fast, release slower).</summary>
        static float Press01(float p)
        {
            p = Mathf.Clamp01(p);
            return p < 0.35f ? p / 0.35f : 1f - (p - 0.35f) / 0.65f;
        }

        void SetAlpha(float a)
        {
            if (_handImage == null) return;
            var c = _handImage.color;
            c.a = a;
            _handImage.color = c;
        }
    }
}
