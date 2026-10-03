// ============================================================================================================
// "SideEventButton" of the Home side columns (Docs/DesignSystem.md §6): a 140 px glossy icon on a candy disc, a
// caption plate (or a mini progress bar), an optional red badge and a soft glow for "ready" states. It idles with a
// bob (phase offset per button), squashes on press and can wiggle to call for attention.
// ============================================================================================================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class HomeSideButton : MonoBehaviour
    {
        public const float Size = 150f;
        public const float SlotHeight = 206f;

        public UIButton Button;
        public Image Icon;
        public Image Glow;
        public Image Rays;
        public TMP_Text Caption;
        public Image CaptionPlate;
        public ProgressBar Bar;
        public Badge Badge;
        /// <summary>The bobbing rect (child of the slot).</summary>
        public RectTransform Bobber;

        bool _glowing;
        TweenHandle _bob;

        /// <summary>Slot (positioned by the caller) → Bobber → UIButton (disc, glow, icon, caption, badge).</summary>
        public static HomeSideButton Create(Transform parent, string iconSprite, Action onClick, bool withBar = false)
        {
            var slot = UIKit.Rect("SideButton_" + iconSprite, parent);
            slot.sizeDelta = new Vector2(Size + 30f, SlotHeight);
            var sb = slot.gameObject.AddComponent<HomeSideButton>();

            var bobber = UIKit.Rect("Bob", slot);
            UIKit.Stretch(bobber);
            sb.Bobber = bobber;

            var btn = UIKit.IconButton(bobber, iconSprite, Size, onClick);
            var brt = (RectTransform)btn.transform;
            UIKit.Place(brt, new Vector2(0.5f, 1f), new Vector2(Size, Size), Vector2.zero);
            sb.Button = btn;
            sb.Icon = btn.icon;
            var visual = btn.Visual;

            // Ready glow + slowly spinning rays (hidden until SetGlow(true)).
            var rays = UIKit.NewImage(visual, "Rays", UISprites.Get("sunburst"), new Color(1f, 0.92f, 0.55f, 0.9f));
            rays.preserveAspect = true;
            UIKit.Stretch(rays.rectTransform, -60f, -60f, -60f, -60f);
            rays.gameObject.SetActive(false);
            sb.Rays = rays;
            var glow = UIKit.NewImage(visual, "Glow", UISprites.Glow, new Color(1f, 0.85f, 0.3f, 0.85f));
            UIKit.Stretch(glow.rectTransform, -34f, -34f, -34f, -34f);
            glow.gameObject.SetActive(false);
            sb.Glow = glow;

            // Candy disc: soft shadow, white rim, brand fill, top gloss.
            var shadow = UIKit.NewImage(visual, "Shadow", UISprites.SoftShadow, new Color(0f, 0f, 0f, 0.3f));
            UIKit.Stretch(shadow.rectTransform, -6f, 6f, -6f, -18f);
            var rim = UIKit.NewImage(visual, "Rim", UISprites.Circle, new Color(1f, 1f, 1f, 0.85f));
            UIKit.Stretch(rim.rectTransform, 8f, 8f, 8f, 8f);
            var disc = UIKit.NewImage(visual, "Disc", UISprites.Circle, DS.WithAlpha(DS.Colors.Brand, 0.82f));
            UIKit.Stretch(disc.rectTransform, 14f, 14f, 14f, 14f);
            var gloss = UIKit.NewImage(visual, "Gloss", UISprites.Circle, new Color(1f, 1f, 1f, 0.22f));
            UIKit.Stretch(gloss.rectTransform, 34f, 20f, 34f, 78f);
            rays.transform.SetSiblingIndex(0);
            glow.transform.SetSiblingIndex(1);
            shadow.transform.SetSiblingIndex(2);
            rim.transform.SetSiblingIndex(3);
            disc.transform.SetSiblingIndex(4);
            gloss.transform.SetSiblingIndex(5);
            if (sb.Icon != null)
            {
                sb.Icon.transform.SetAsLastSibling();
                UIKit.Stretch(sb.Icon.rectTransform, 20f, 12f, 20f, 26f);
            }

            // Caption plate (or progress bar) overlapping the bottom of the disc.
            if (withBar)
            {
                var bar = UIKit.ProgressBar(visual, new Vector2(Size + 16f, 44f));
                UIKit.Place((RectTransform)bar.transform, new Vector2(0.5f, 0f), new Vector2(Size + 16f, 44f), new Vector2(0f, -30f));
                bar.label.gameObject.SetActive(true);
                DS.Apply(bar.label, TextStyle.Badge, 26f);
                sb.Bar = bar;
            }
            else
            {
                var plate = UIKit.Capsule(visual, new Vector2(Size + 22f, 48f), DS.WithAlpha(DS.Colors.Ink, 0.88f));
                plate.name = "CaptionPlate";
                UIKit.Place(plate.rectTransform, new Vector2(0.5f, 0f), new Vector2(Size + 22f, 48f), new Vector2(0f, -32f));
                var cap = UIKit.Text(plate.rectTransform, "", TextStyle.BodyLight, new Vector2(Size + 6f, 44f));
                DS.Apply(cap, TextStyle.BodyLight, 30f);
                cap.textWrappingMode = TextWrappingModes.NoWrap;
                UIKit.Stretch(cap.rectTransform, 12f, 2f, 12f, 4f);
                sb.CaptionPlate = plate;
                sb.Caption = cap;
            }

            sb.Badge = UIKit.Badge(btn.transform, new Vector2(-14f, -12f));
            return sb;
        }

        /// <summary>Starts the idle bob with a phase offset (seconds).</summary>
        public void StartBob(float phase)
        {
            _bob?.Kill();
            if (Bobber == null) return;
            Bobber.anchoredPosition = Vector2.zero;
            _bob = Tween.Bob(Bobber, DS.Motion.BobAmplitude, DS.Motion.BobPeriod).SetDelay(phase);
        }

        public void SetCaption(string text)
        {
            if (Caption != null && Caption.text != text) Caption.text = text ?? "";
        }

        /// <summary>Localized caption (static key).</summary>
        public void SetCaptionKey(string key) => SetCaption(Loc.T(key));

        public void SetBar(float value01, string label, bool animate)
        {
            if (Bar == null) return;
            Bar.SetValue(value01, animate);
            if (Bar.label != null && Bar.label.text != label) Bar.label.text = label ?? "";
        }

        /// <summary>Gold glow + spinning rays behind the disc ("ready to claim").</summary>
        public void SetGlow(bool on)
        {
            if (_glowing == on) return;
            _glowing = on;
            if (Glow != null)
            {
                Tween.Kill(Glow);
                Glow.gameObject.SetActive(on);
                if (on)
                {
                    Glow.transform.localScale = Vector3.one * 0.92f;
                    Tween.Scale(Glow.transform, 1.08f, 0.7f, Ease.InOutSine).SetLoops(-1, true);
                }
            }
            if (Rays != null)
            {
                Tween.Kill(Rays);
                Rays.gameObject.SetActive(on);
                if (on)
                {
                    Rays.transform.localEulerAngles = Vector3.zero;
                    Tween.Rotate(Rays.transform, -360f, 9f, Ease.Linear).SetLoops(-1, false);
                }
            }
        }

        /// <summary>Quick attention wiggle of the icon.</summary>
        public void Wiggle()
        {
            if (Icon == null || !isActiveAndEnabled) return;
            var t = Icon.transform;
            Tween.Kill(t);
            t.localEulerAngles = Vector3.zero;
            t.localScale = Vector3.one;
            Tween.Rotate(t, 12f, 0.08f, Ease.OutQuad);
            Tween.Rotate(t, -10f, 0.12f, Ease.InOutSine).SetDelay(0.08f);
            Tween.Rotate(t, 7f, 0.1f, Ease.InOutSine).SetDelay(0.2f);
            Tween.Rotate(t, 0f, 0.14f, Ease.OutBack).SetDelay(0.3f);
            Tween.Scale(t, 1.12f, 0.15f, Ease.OutQuad);
            Tween.Scale(t, 1f, 0.3f, Ease.OutBack).SetDelay(0.15f);
        }

        /// <summary>Entrance pop (scale 0 → 1, staggered by the caller).</summary>
        public void PopIn(float delay)
        {
            if (Button == null) return;
            var t = Button.transform;
            Tween.Kill(t);
            t.localScale = Vector3.zero;
            Tween.Scale(t, 1f, DS.Motion.Slow, Ease.OutBack).SetOvershoot(2f).SetDelay(delay);
        }

        void OnDisable()
        {
            // Screens are deactivated when hidden: settle every looping animation on its rest pose.
            if (Button != null) Button.transform.localScale = Vector3.one;
            if (Icon != null)
            {
                Tween.Kill(Icon.transform);
                Icon.transform.localEulerAngles = Vector3.zero;
                Icon.transform.localScale = Vector3.one;
            }
        }
    }
}
