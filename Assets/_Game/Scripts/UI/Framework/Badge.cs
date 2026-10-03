using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Red notification badge (ui_circle tinted Danger) with a white number or "!", idle pulse.
    /// Anchored to the parent's top-right corner (inside the Visual of a UIButton parent, so it squashes with it).
    /// Hidden (inactive) when empty.</summary>
    public class Badge : MonoBehaviour
    {
        // ---- additions
        public Image Background;
        public TMP_Text Text;
        public const float DefaultSize = 56f;

        TweenHandle _pulse;
        bool _popOnEnable;

        /// <summary>0 hides the badge.</summary>
        public void SetCount(int count)
        {
            if (count <= 0) { Hide(); return; }
            SetText(count > 99 ? "99+" : count.ToString());
        }

        /// <summary>Shows text (e.g. "!"); null/empty hides.</summary>
        public void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            if (Text != null) Text.text = text;
            var rt = (RectTransform)transform;
            float h = rt.sizeDelta.y > 0f ? rt.sizeDelta.y : DefaultSize;
            bool wide = text.Length > 2;
            rt.sizeDelta = new Vector2(wide ? h * 1.5f : h, h);
            if (Background != null)
            {
                // Circle for 1-2 characters, capsule for "99+". The SliceFit stays attached: it slices the capsule
                // and switches the borderless circle back to a Simple image by itself.
                var sprite = wide ? UISprites.Capsule : UISprites.Circle;
                if (Background.sprite != sprite) Background.sprite = sprite;
                SliceFit.Attach(Background, SliceFit.Mode.Height);
            }
            if (!gameObject.activeSelf)
            {
                _popOnEnable = true;
                gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        void OnEnable()
        {
            var t = transform;
            Tween.Kill(t);
            if (_popOnEnable)
            {
                _popOnEnable = false;
                t.localScale = Vector3.zero;
                Tween.Scale(t, 1f, DS.Motion.Slow, Ease.OutBack).SetOvershoot(DS.Motion.PopOvershoot)
                    .OnComplete(StartPulse);
            }
            else
            {
                t.localScale = Vector3.one;
                StartPulse();
            }
        }

        void StartPulse()
        {
            if (!isActiveAndEnabled) return;
            _pulse = Tween.Scale(transform, 1.1f, DS.Motion.PulsePeriod * 0.5f, Ease.InOutSine).SetLoops(-1, true);
        }

        void OnDisable()
        {
            _pulse?.Kill();
            _pulse = null;
            Tween.Kill(transform);
            transform.localScale = Vector3.one;
        }

        internal static Badge Create(Transform parent, Vector2 anchoredPos)
        {
            if (parent == null) return null;
            // On a UIButton, live inside its Visual so the badge squashes together with the button.
            var button = parent.GetComponent<UIButton>();
            if (button != null && button.Visual != null) parent = button.Visual;
            float s = DefaultSize;
            var root = UIKit.Rect("Badge", parent);
            UIKit.Place(root, new Vector2(1f, 1f), new Vector2(s, s), anchoredPos);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = anchoredPos;
            root.gameObject.SetActive(false);   // hidden until SetCount/SetText (so OnEnable pops it in)
            var badge = root.gameObject.AddComponent<Badge>();
            var bg = UIKit.NewImage(root, "Circle", UISprites.Circle, DS.Colors.Danger);
            UIKit.Stretch(bg.rectTransform);
            badge.Background = bg;
            var text = UIKit.Text(root, "", TextStyle.Badge, new Vector2(s, s));
            UIKit.Stretch(text.rectTransform, 4, 0, 4, 2);
            badge.Text = text;
            return badge;
        }
    }
}
