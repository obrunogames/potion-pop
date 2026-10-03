using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Capsule switch: green track + knob on the right when on, gray + knob left when off. Sfx.Toggle.</summary>
    public class UIToggle : MonoBehaviour, IPointerClickHandler
    {
        public event Action<bool> OnChanged;

        // ---- additions
        public Image Track;
        public RectTransform Knob;
        public static readonly Vector2 DefaultSize = new Vector2(150, 84);

        bool _value;
        bool _interactable = true;
        float _lastClick = -10f;
        Pressable _press;
        CanvasGroup _visualGroup;

        /// <summary>False ignores taps (no squash) and dims the switch to 60%.</summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (_press != null) _press.enabled = value;
                if (_visualGroup != null) _visualGroup.alpha = value ? 1f : 0.6f;
            }
        }

        /// <summary>Setting it animates the switch and raises OnChanged (silent: Sfx.Toggle and the haptic play on
        /// player taps only, so code that syncs settings makes no noise).</summary>
        public bool Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                Animate(isActiveAndEnabled);
                Raise(value);
            }
        }

        public void SetValueWithoutNotify(bool value)
        {
            _value = value;
            Animate(isActiveAndEnabled);
        }

        protected void Raise(bool v) { OnChanged?.Invoke(v); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!Interactable || e.button != PointerEventData.InputButton.Left) return;
            float now = Time.unscaledTime;
            if (now - _lastClick < 0.15f) return;
            _lastClick = now;
            AudioManager.Play(Sfx.Toggle);
            Haptics.Play(HapticType.Selection);
            Value = !_value;
        }

        void Animate(bool animate)
        {
            if (Knob == null || Track == null) return;
            var rt = (RectTransform)transform;
            Vector2 size = rt.rect.size;
            if (size.x <= 0f) size = rt.sizeDelta;
            float travel = Mathf.Max(0f, (size.x - size.y) * 0.5f);
            var pos = new Vector2(_value ? travel : -travel, 0f);
            var color = _value ? DS.Colors.Primary : DS.Colors.Gray;
            Tween.Kill(Knob);
            Tween.Kill(Track);
            if (animate)
            {
                Tween.Move(Knob, pos, DS.Motion.Base, Ease.OutBack);
                Tween.Color(Track, color, DS.Motion.Base);
            }
            else
            {
                Knob.anchoredPosition = pos;
                Track.color = color;
            }
        }

        void OnRectTransformDimensionsChange()
        {
            if (Knob != null) Animate(false);
        }

        internal static UIToggle Create(Transform parent, bool value, Action<bool> onChanged)
        {
            if (parent == null) return null;
            var size = DefaultSize;
            var root = UIKit.Rect("Toggle", parent);
            root.sizeDelta = size;
            root.gameObject.AddComponent<HitArea>();
            var toggle = root.gameObject.AddComponent<UIToggle>();

            var visual = UIKit.Stretch(UIKit.Rect("Visual", root));
            toggle._visualGroup = visual.gameObject.AddComponent<CanvasGroup>();
            var track = UIKit.NewImage(visual, "Track", UISprites.Capsule, DS.Colors.Gray);
            UIKit.Stretch(track.rectTransform);
            SliceFit.Attach(track, SliceFit.Mode.Height);
            toggle.Track = track;

            // Inner shade along the top edge gives the track a recessed look.
            var shade = UIKit.NewImage(visual, "Shade", UISprites.Capsule, new Color(0f, 0f, 0f, 0.12f));
            UIKit.Stretch(shade.rectTransform, 6, 6, 6, size.y * 0.45f);
            SliceFit.Attach(shade, SliceFit.Mode.Height);

            float k = size.y - 14f;
            var knob = UIKit.Rect("Knob", visual);
            UIKit.Place(knob, new Vector2(0.5f, 0.5f), new Vector2(k, k), Vector2.zero);
            var knobShadow = UIKit.NewImage(knob, "Shadow", UISprites.Circle, new Color(0.23f, 0.12f, 0.36f, 0.35f));
            UIKit.Stretch(knobShadow.rectTransform, 0, 5, 0, -5);
            var knobFace = UIKit.NewImage(knob, "Face", UISprites.Circle, Color.white);
            UIKit.Stretch(knobFace.rectTransform);
            toggle.Knob = knob;

            var press = root.gameObject.AddComponent<Pressable>();
            press.target = visual;
            press.playSound = false;   // the toggle plays Sfx.Toggle itself
            press.haptic = false;
            press.pressedScale = 0.95f;
            toggle._press = press;

            toggle.SetValueWithoutNotify(value);
            toggle.Animate(false);
            if (onChanged != null) toggle.OnChanged += onChanged;
            return toggle;
        }
    }
}
