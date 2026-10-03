using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Track (progress_track) + fill (progress_fill) + optional label/icon. The fill keeps its rounded caps at any
    /// value: a RectMask2D whose width follows the value clips a fill image that is never narrower than its height.
    /// </summary>
    public class ProgressBar : MonoBehaviour
    {
        public TMP_Text label;

        // ---- additions
        public Image Track;
        public Image Fill;
        public Image Icon;
        /// <summary>Inset of the fill inside the track, as a fraction of the bar height.</summary>
        public float fillInset = 0.16f;
        public float animDuration = 0.45f;

        RectTransform _area;
        float _value, _target;
        TweenHandle _anim;

        public float Value => _target;

        public void SetValue(float normalized01, bool animate = true)
        {
            float v = Mathf.Clamp01(float.IsNaN(normalized01) ? 0f : normalized01);
            _target = v;
            _anim?.Kill();
            _anim = null;
            if (!animate || !isActiveAndEnabled || Mathf.Approximately(v, _value))
            {
                Layout(v);
                return;
            }
            _anim = Tween.Value(_value, v, animDuration, Layout, Ease.OutCubic).SetLink(this);
        }

        public void SetLabel(string text)
        {
            if (label == null) return;
            var loc = label.GetComponent<LocText>();
            if (loc != null) loc.Clear();
            label.text = text ?? "";
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>Tints the fill. White restores the art; other colors use a tintable glossy fill.</summary>
        public void SetFillColor(Color color)
        {
            if (Fill == null) return;
            var art = UISprites.Get("progress_fill");
            Fill.sprite = art != null && color == Color.white ? art : UISprites.BarFill;
            Fill.color = color;
        }

        /// <summary>Icon overlapping the left end (e.g. a star or chest). Null hides it.</summary>
        public void SetIcon(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                if (Icon != null) Icon.gameObject.SetActive(false);
                return;
            }
            var rt = (RectTransform)transform;
            float s = rt.rect.height * 1.6f;
            if (Icon == null)
            {
                Icon = UIKit.Image(rt, spriteName, new Vector2(s, s));
                UIKit.Place(Icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(s, s), new Vector2(-s * 0.35f, 0));
            }
            else
            {
                var sp = UISprites.Get(spriteName);
                Icon.sprite = sp != null ? sp : UISprites.Circle;
                Icon.gameObject.SetActive(true);
            }
        }

        void Layout(float v)
        {
            _value = v;
            if (_area == null || Fill == null) return;
            var rt = (RectTransform)transform;
            Rect r = rt.rect;
            float inset = r.height * fillInset;
            float innerW = Mathf.Max(0f, r.width - inset * 2f);
            float innerH = Mathf.Max(0f, r.height - inset * 2f);
            float w = innerW * v;
            _area.anchorMin = new Vector2(0f, 0f);
            _area.anchorMax = new Vector2(0f, 1f);
            _area.pivot = new Vector2(0f, 0.5f);
            _area.offsetMin = new Vector2(inset, inset);
            _area.offsetMax = new Vector2(inset + w, -inset);
            var f = Fill.rectTransform;
            f.anchorMin = new Vector2(0f, 0f);
            f.anchorMax = new Vector2(0f, 1f);
            f.pivot = new Vector2(0f, 0.5f);
            f.anchoredPosition = Vector2.zero;
            f.sizeDelta = new Vector2(Mathf.Max(w, innerH), 0f);
            Fill.enabled = w > 0.5f;
        }

        void OnRectTransformDimensionsChange()
        {
            if (_area != null) Layout(_value);
        }

        void OnDisable()
        {
            if (_anim != null && _anim.IsActive)
            {
                _anim.Kill();
                _anim = null;
                Layout(_target);
            }
        }

        internal static ProgressBar Create(Transform parent, Vector2 size)
        {
            if (parent == null) return null;
            var root = UIKit.Rect("ProgressBar", parent);
            root.sizeDelta = size;
            var bar = root.gameObject.AddComponent<ProgressBar>();

            var trackSp = UISprites.Get("progress_track");
            var track = UIKit.NewImage(root, "Track", trackSp != null ? trackSp : UISprites.Capsule,
                trackSp != null ? Color.white : DS.WithAlpha(DS.Colors.Ink, 0.75f));
            UIKit.Stretch(track.rectTransform);
            SliceFit.Attach(track, SliceFit.Mode.Height);
            bar.Track = track;

            var area = UIKit.Rect("FillArea", root);
            area.gameObject.AddComponent<RectMask2D>();
            bar._area = area;

            var fillSp = UISprites.Get("progress_fill");
            var fill = UIKit.NewImage(area, "Fill", fillSp != null ? fillSp : UISprites.BarFill,
                fillSp != null ? Color.white : DS.Colors.Primary);
            SliceFit.Attach(fill, SliceFit.Mode.Height);
            bar.Fill = fill;

            var text = UIKit.Text(root, "", TextStyle.Badge, size);
            DS.Apply(text, TextStyle.Badge, Mathf.Clamp(size.y * 0.55f, 18f, 48f));
            UIKit.Stretch(text.rectTransform, 8, 0, 8, 2);
            text.name = "Label";
            text.gameObject.SetActive(false);
            bar.label = text;

            bar.Layout(0f);
            return bar;
        }
    }
}
