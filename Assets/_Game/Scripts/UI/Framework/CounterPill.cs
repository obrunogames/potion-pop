using System;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>
    /// Top-bar counter: pill_counter capsule, icon overlapping the left end (fly-to target), value with count-up
    /// animation, optional green "+" button overlapping the right end.
    /// </summary>
    public class CounterPill : MonoBehaviour
    {
        /// <summary>Fly-to target for rewards (the icon).</summary>
        public RectTransform IconTarget;
        public TMP_Text valueText;

        // ---- additions
        public UIButton PlusButton;
        public UnityEngine.UI.Image Background;
        public UnityEngine.UI.Image Icon;
        /// <summary>Seconds of the count-up animation.</summary>
        public float countDuration = 0.6f;

        public const float DefaultHeight = 84f;

        double _shown;
        long _target;
        bool _hasValue;
        TweenHandle _count;

        /// <summary>Value currently displayed (mid-animation values included).</summary>
        public long DisplayedValue => (long)Math.Round(_shown);

        /// <summary>Displays a number. Animates (count-up, OutCubic) only from a previously set value while visible;
        /// the very first call always jumps straight to the value.</summary>
        public void SetValue(long value, bool animate = true)
        {
            _count?.Kill();
            _count = null;
            bool canAnimate = animate && _hasValue && isActiveAndEnabled && value != DisplayedValue;
            _target = value;
            _hasValue = true;
            if (!canAnimate)
            {
                Show(value);
                return;
            }
            double start = _shown;
            _count = Tween.Value(0f, 1f, countDuration, p => Show(start + (value - start) * p), Ease.OutCubic).SetLink(this);
        }

        /// <summary>Free text instead of a number (e.g. "Full", "12:34"). Stops any count-up.</summary>
        public void SetText(string text)
        {
            _count?.Kill();
            _count = null;
            _hasValue = false;   // the next SetValue jumps (no count-up from a number that was not on screen)
            if (valueText != null) valueText.text = text ?? "";
        }

        public void Bump()
        {
            if (IconTarget != null) Tween.Punch(IconTarget, 0.25f, 0.35f);
            if (valueText != null) Tween.Punch(valueText.transform, 0.12f, 0.3f);
        }

        void Show(double v)
        {
            _shown = v;
            if (valueText != null) valueText.text = Loc.Number((long)Math.Round(v));
        }

        void OnDisable()
        {
            // Jump to the final value when hidden mid-animation.
            if (_count != null && _count.IsActive)
            {
                _count.Kill();
                _count = null;
                Show(_target);
            }
        }

        internal static CounterPill Create(Transform parent, string iconSprite, float width, Action onPlus)
        {
            if (parent == null) return null;
            float h = DefaultHeight;
            var root = UIKit.Rect("CounterPill", parent);
            root.sizeDelta = new Vector2(width, h);
            var pill = root.gameObject.AddComponent<CounterPill>();

            // Capsule (starts a bit right of the root so the icon overlaps its left end).
            var bgSprite = UISprites.Get("pill_counter");
            var bg = UIKit.NewImage(root, "Pill", bgSprite != null ? bgSprite : UISprites.Capsule,
                bgSprite != null ? Color.white : DS.Colors.Cream);
            UIKit.Stretch(bg.rectTransform, h * 0.45f, 0, onPlus != null ? h * 0.25f : 0, 0);
            SliceFit.Attach(bg, SliceFit.Mode.Height);
            pill.Background = bg;

            float iconSize = h * 1.25f;
            var icon = UIKit.Image(root, iconSprite, new Vector2(iconSize, iconSize));
            UIKit.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(iconSize, iconSize), new Vector2(-h * 0.08f, 0));
            pill.Icon = icon;
            pill.IconTarget = icon.rectTransform;

            float plusSize = h * 0.92f;
            var text = UIKit.Text(root, "0", TextStyle.Body, new Vector2(10, h));
            DS.Apply(text, TextStyle.Body, 48f);
            text.name = "Value";
            // The value starts right after the icon's right edge (the icon overlaps the capsule's left end), so long
            // numbers ("12.345") shrink with auto-size instead of sliding under the icon.
            float iconRight = iconSize - h * 0.08f;
            UIKit.Stretch(text.rectTransform, iconRight + 2f, 4, onPlus != null ? plusSize * 0.92f : h * 0.3f, 0);
            text.enableAutoSizing = true;
            text.fontSizeMin = 26f;
            text.fontSizeMax = 48f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            pill.valueText = text;

            if (onPlus != null)
            {
                var plus = UIKit.IconButton(root, "icon_plus", plusSize, onPlus);
                UIKit.Place((RectTransform)plus.transform, new Vector2(1f, 0.5f), new Vector2(plusSize, plusSize), new Vector2(0, 0));
                plus.ExpandHitArea();   // the 77 px "+" still gets a 110 px tap target
                pill.PlusButton = plus;
            }
            // Show "0" without marking a value as set: the first real SetValue jumps straight to the player's
            // balance instead of counting up from 0 every time a screen is built.
            pill.Show(0);
            return pill;
        }
    }
}
