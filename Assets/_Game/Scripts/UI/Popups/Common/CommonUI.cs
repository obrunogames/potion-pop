// ============================================================================================================
// Shared building blocks of the tab screens and the common popups (owner: Tabs agent): section headers, cards,
// avatar discs, collection card faces, a loading spinner and small motion helpers. Everything is code-built from the
// design system (DS/UIKit) and null-safe for missing art.
// ============================================================================================================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public static class CommonUI
    {
        /// <summary>Card art aspect (height / width) of card_frame (384 x 596).</summary>
        public const float CardAspect = 596f / 384f;
        /// <summary>Google's label gray (#1F1F1F) for the "Sign in with Google" button.</summary>
        public static readonly Color GoogleText = DS.Hex("1F1F1F");
        /// <summary>Deep plum used for product silhouettes on missing cards.</summary>
        public static readonly Color Silhouette = DS.Hex("2A1650");

        static readonly Rect DefaultCardInner = new Rect(0.0807f, 0.0587f, 0.8359f, 0.8087f);

        // ---------------------------------------------------------------------------------------- text

        /// <summary>Plain (non localized) text with a style, optional max font size and color override.</summary>
        public static TMP_Text Label(Transform parent, string text, TextStyle style, Vector2 size,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float fontSize = 0f, Color? color = null)
        {
            var t = UIKit.Text(parent, text, style, size, align);
            if (t == null) return null;
            if (fontSize > 0f) DS.Apply(t, style, fontSize);
            t.alignment = align;
            if (color.HasValue) t.color = color.Value;
            return t;
        }

        /// <summary>Localized text with a style, optional max font size and color override.</summary>
        public static TMP_Text LocLabel(Transform parent, string key, TextStyle style, Vector2 size,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float fontSize = 0f, Color? color = null, params object[] args)
        {
            var t = UIKit.LocText(parent, key, style, size, args);
            if (t == null) return null;
            if (fontSize > 0f) DS.Apply(t, style, fontSize);
            t.alignment = align;
            if (color.HasValue) t.color = color.Value;
            return t;
        }

        // ---------------------------------------------------------------------------------------- surfaces

        /// <summary>White card surface (panel_card 9-slice) of a fixed size.</summary>
        public static Image Card(Transform parent, Vector2 size)
        {
            var img = UIKit.Panel(parent, "panel_card", size);
            if (img != null) img.name = "Card";
            return img;
        }

        /// <summary>
        /// Section header for content placed directly on a backdrop: a glossy capsule with an icon overlapping its left
        /// end and an H3 caption. The returned root is `width` wide (the capsule hugs the left side).
        /// </summary>
        public static RectTransform SectionHeader(Transform parent, string key, string icon, float width, Color color)
        {
            var root = UIKit.Rect("Section", parent);
            root.sizeDelta = new Vector2(width, 104f);
            const float h = 80f;
            float capW = Mathf.Min(width, 520f);
            var shadow = UIKit.Capsule(root, new Vector2(capW, h), DS.WithAlpha(DS.Colors.Ink, 0.35f));
            UIKit.Place(shadow.rectTransform, new Vector2(0f, 0.5f), new Vector2(capW, h), new Vector2(40f, -6f));
            var cap = UIKit.Capsule(root, new Vector2(capW, h), color);
            UIKit.Place(cap.rectTransform, new Vector2(0f, 0.5f), new Vector2(capW, h), new Vector2(40f, 0f));
            var shine = UIKit.Capsule(cap.rectTransform, new Vector2(capW - 40f, h * 0.36f), DS.WithAlpha(Color.white, 0.22f));
            UIKit.Place(shine.rectTransform, new Vector2(0.5f, 1f), new Vector2(capW - 40f, h * 0.36f), new Vector2(0f, -8f));
            var label = UIKit.LocText(cap.rectTransform, key, TextStyle.H3, new Vector2(capW - 130f, h - 8f));
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Left;
                UIKit.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(capW - 130f, h - 8f), new Vector2(100f, 2f));
            }
            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIKit.Image(root, icon, new Vector2(104f, 104f));
                UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(104f, 104f), new Vector2(0f, 4f));
                ic.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            }
            return root;
        }

        /// <summary>Glass capsule (white 18% + 35% border) with an optional icon and a text, for info strips.</summary>
        public static RectTransform GlassPill(Transform parent, Vector2 size, string icon, out TMP_Text text)
        {
            var root = UIKit.Rect("GlassPill", parent);
            root.sizeDelta = size;
            var border = UIKit.Capsule(root, size, DS.Colors.GlassBorder);
            UIKit.Stretch(border.rectTransform);
            var fill = UIKit.Capsule(root, size, DS.WithAlpha(DS.Colors.Ink, 0.35f));
            UIKit.Stretch(fill.rectTransform, 4, 4, 4, 4);
            float iconSize = string.IsNullOrEmpty(icon) ? 0f : size.y * 0.9f;
            if (iconSize > 0f)
            {
                var ic = UIKit.Image(root, icon, new Vector2(iconSize, iconSize));
                UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(iconSize, iconSize), new Vector2(size.y * 0.15f, 0f));
            }
            float left = iconSize > 0f ? iconSize + size.y * 0.25f : size.y * 0.4f;
            text = UIKit.Text(root, "", TextStyle.BodyLight, size);
            DS.Apply(text, TextStyle.BodyLight, Mathf.Min(42f, size.y * 0.52f));
            text.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Stretch(text.rectTransform, left, 0f, size.y * 0.4f, 2f);
            text.alignment = iconSize > 0f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            return root;
        }

        // ---------------------------------------------------------------------------------------- avatar

        /// <summary>Avatar disc: colored ring, cream disc and the avatar face (child named "Face").</summary>
        public static RectTransform Avatar(Transform parent, string spriteName, float size, Color ring)
        {
            var root = UIKit.Rect("Avatar", parent);
            root.sizeDelta = new Vector2(size, size);
            var shadow = UIKit.NewImage(root, "Shadow", UISprites.Circle, DS.WithAlpha(DS.Colors.Ink, 0.3f));
            UIKit.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size, size), new Vector2(0f, -size * 0.04f));
            var rim = UIKit.NewImage(root, "Ring", UISprites.Circle, ring);
            UIKit.Stretch(rim.rectTransform);
            var disc = UIKit.NewImage(root, "Disc", UISprites.Circle, DS.Colors.Cream);
            float inner = size * 0.86f;
            UIKit.Place(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(inner, inner), Vector2.zero);
            var gloss = UIKit.NewImage(disc.rectTransform, "Gloss", UISprites.Circle, DS.WithAlpha(DS.Colors.Lavender, 0.35f));
            UIKit.Place(gloss.rectTransform, new Vector2(0.5f, 0f), new Vector2(inner * 0.9f, inner * 0.5f), new Vector2(0f, inner * 0.02f));
            var face = UIKit.Image(root, spriteName, new Vector2(size * 0.74f, size * 0.74f));
            face.name = "Face";
            UIKit.Place(face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size * 0.74f, size * 0.74f), new Vector2(0f, size * 0.01f));
            return root;
        }

        /// <summary>Swaps the face sprite of an avatar built by <see cref="Avatar"/>.</summary>
        public static void SetAvatarSprite(RectTransform avatar, string spriteName)
        {
            if (avatar == null) return;
            var face = avatar.Find("Face");
            var img = face != null ? face.GetComponent<Image>() : null;
            if (img == null) return;
            var sp = UISprites.Get(spriteName);
            img.sprite = sp != null ? sp : UISprites.Circle;
            img.color = sp != null ? Color.white : DS.Colors.BrandLight;
        }

        /// <summary>Recolors the ring of an avatar built by <see cref="Avatar"/>.</summary>
        public static void SetAvatarRing(RectTransform avatar, Color ring)
        {
            if (avatar == null) return;
            var r = avatar.Find("Ring");
            var img = r != null ? r.GetComponent<Image>() : null;
            if (img != null) img.color = ring;
        }

        // ---------------------------------------------------------------------------------------- collection cards

        /// <summary>Accent color of the area a product belongs to (brand purple when unknown).</summary>
        public static Color ProductAccent(string productId)
        {
            string area = Catalog.AreaOfProduct(productId);
            return DS.AreaAccent(area);
        }

        /// <summary>
        /// Owned card face: gold card_frame, area-tinted window, glow, product image and localized product name.
        /// Root size = (width, width * CardAspect).
        /// </summary>
        public static RectTransform CardFront(Transform parent, string productId, float width)
        {
            float h = width * CardAspect;
            var root = UIKit.Rect("CardFront", parent);
            root.sizeDelta = new Vector2(width, h);

            var shadow = UIKit.RoundedRect(root, new Vector2(width * 0.9f, h * 0.92f), DS.WithAlpha(DS.Colors.Ink, 0.28f), width * 0.1f);
            UIKit.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width * 0.9f, h * 0.92f), new Vector2(0f, -h * 0.025f));

            var frame = UIKit.Image(root, "card_frame", new Vector2(width, h), false);
            UIKit.Stretch(frame.rectTransform);

            var info = Art.Info("card_frame");
            Rect inner = info != null && info.hasInner ? info.inner : DefaultCardInner;
            var window = UIKit.Rect("Window", root);
            window.anchorMin = new Vector2(inner.x, inner.y);
            window.anchorMax = new Vector2(inner.x + inner.width, inner.y + inner.height);
            window.offsetMin = new Vector2(width * 0.012f, width * 0.012f);
            window.offsetMax = new Vector2(-width * 0.012f, -width * 0.012f);

            Color accent = ProductAccent(productId);
            float ww = width * inner.width, wh = h * inner.height;
            var tint = UIKit.RoundedRect(window, Vector2.zero, Color.Lerp(accent, Color.white, 0.62f), width * 0.07f);
            UIKit.Stretch(tint.rectTransform);
            var light = UIKit.NewImage(window, "Light", UISprites.Get("ui_gradient_v"), DS.WithAlpha(Color.white, 0.75f));
            UIKit.Stretch(light.rectTransform, 0f, 0f, 0f, wh * 0.45f);

            var glow = UIKit.NewImage(window, "Glow", UISprites.Glow, DS.WithAlpha(Color.Lerp(accent, Color.white, 0.3f), 0.85f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(ww * 1.05f, ww * 1.05f), Vector2.zero);

            float ps = Mathf.Min(ww * 0.8f, wh * 0.62f);
            var product = UIKit.Image(window, "p_" + productId, new Vector2(ps, ps));
            product.name = "Product";
            UIKit.Place(product.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(ps, ps), Vector2.zero);

            float bandH = wh * 0.2f;
            var band = UIKit.Capsule(window, new Vector2(ww * 0.92f, bandH), DS.WithAlpha(Color.white, 0.92f));
            UIKit.Place(band.rectTransform, new Vector2(0.5f, 0f), new Vector2(ww * 0.92f, bandH), new Vector2(0f, wh * 0.04f));
            var name = UIKit.LocText(band.rectTransform, Catalog.ProductNameKey(productId), TextStyle.Body, new Vector2(ww * 0.84f, bandH * 0.9f));
            if (name != null)
            {
                DS.Apply(name, TextStyle.Body, Mathf.Max(14f, bandH * 0.5f));
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.fontSizeMin = Mathf.Max(8f, bandH * 0.22f);
                UIKit.Stretch(name.rectTransform, bandH * 0.3f, 0f, bandH * 0.3f, 0f);
                name.name = "Name";
            }
            return root;
        }

        /// <summary>
        /// Card back (card_back art). With dimmed + silhouetteProductId it becomes the "missing card" look: darker back,
        /// deep plum product silhouette and a "?" disc.
        /// </summary>
        public static RectTransform CardBack(Transform parent, float width, bool dimmed = false, string silhouetteProductId = null)
        {
            float h = width * CardAspect;
            var root = UIKit.Rect("CardBack", parent);
            root.sizeDelta = new Vector2(width, h);

            var shadow = UIKit.RoundedRect(root, new Vector2(width * 0.9f, h * 0.88f), DS.WithAlpha(DS.Colors.Ink, dimmed ? 0.18f : 0.28f), width * 0.1f);
            UIKit.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width * 0.9f, h * 0.88f), new Vector2(0f, -h * 0.025f));

            var back = UIKit.Image(root, "card_back", new Vector2(width, h), true);
            UIKit.Stretch(back.rectTransform);
            if (dimmed) back.color = new Color(0.62f, 0.58f, 0.72f, 1f);

            if (!string.IsNullOrEmpty(silhouetteProductId))
            {
                var veil = UIKit.RoundedRect(root, new Vector2(width * 0.8f, h * 0.78f), DS.WithAlpha(DS.Colors.Ink, 0.35f), width * 0.08f);
                UIKit.Place(veil.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width * 0.8f, h * 0.78f), Vector2.zero);
                float ps = width * 0.62f;
                var sil = UIKit.Image(root, "p_" + silhouetteProductId, new Vector2(ps, ps));
                sil.name = "Silhouette";
                sil.color = DS.WithAlpha(Silhouette, 0.92f);
                UIKit.Place(sil.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(ps, ps), Vector2.zero);
                float qs = width * 0.3f;
                var disc = UIKit.NewImage(root, "QDisc", UISprites.Circle, DS.Colors.Accent);
                UIKit.Place(disc.rectTransform, new Vector2(0.5f, 0f), new Vector2(qs, qs), new Vector2(0f, h * 0.08f));
                var q = Label(disc.rectTransform, "?", TextStyle.H2, new Vector2(qs, qs), TextAlignmentOptions.Center, qs * 0.7f);
                if (q != null) UIKit.Stretch(q.rectTransform, 0f, 0f, 0f, qs * 0.04f);
            }
            return root;
        }

        // ---------------------------------------------------------------------------------------- spinner

        /// <summary>Classic 8-dot loading spinner (steps clockwise, unscaled time).</summary>
        public static CommonSpinner Spinner(Transform parent, float size, Color color) => CommonSpinner.Create(parent, size, color);

        // ---------------------------------------------------------------------------------------- motion

        /// <summary>PopIn (0.6 → 1, OutBack) after a delay; the target stays invisible-small until it starts.</summary>
        public static TweenHandle PopIn(Transform t, float delay, float to = 1f)
        {
            if (t == null) return new TweenHandle();
            Tween.Kill(t);
            t.localScale = delay > 0f ? Vector3.zero : Vector3.one * (to * DS.Motion.PopInFrom);
            return Tween.Scale(t, to, DS.Motion.Slow, Ease.OutBack).SetOvershoot(DS.Motion.PopOvershoot).SetDelay(delay);
        }

        /// <summary>Fades a CanvasGroup in and slides its rect from `offset` to its current anchored position.</summary>
        public static void SlideIn(RectTransform t, CanvasGroup group, Vector2 offset, float delay, float duration = 0.35f)
        {
            if (t == null) return;
            // Kill BOTH before creating anything: Tween.Kill(group) also stops Move tweens on the group's own
            // RectTransform, so killing it after Tween.Move would cancel the slide and leave the row offset.
            Tween.Kill(t);
            if (group != null) Tween.Kill(group);
            Vector2 rest = t.anchoredPosition;
            t.anchoredPosition = rest + offset;
            Tween.Move(t, rest, duration, Ease.OutCubic).SetDelay(delay);
            if (group != null)
            {
                group.alpha = 0f;
                Tween.Fade(group, 1f, duration * 0.8f, Ease.OutQuad).SetDelay(delay);
            }
        }

        /// <summary>"Can't do that" feedback: shake, error sound, warning haptic.</summary>
        public static void Deny(Transform t)
        {
            AudioManager.Play(Sfx.Error);
            Haptics.Play(HapticType.Warning);
            var rt = t as RectTransform;
            if (rt != null) Tween.Shake(rt, 14f, 0.3f);
        }

        /// <summary>World-space center of a rect (for FlyRewards origins).</summary>
        public static Vector3 WorldCenter(RectTransform rt) => rt != null ? rt.TransformPoint(rt.rect.center) : Vector3.zero;

        /// <summary>Top-bar coins counter (fly target), or null while the chrome is missing.</summary>
        public static RectTransform CoinsTarget => TopBar.Instance != null ? TopBar.Instance.CoinsTarget : null;
        /// <summary>Top-bar hearts counter (fly target), or null.</summary>
        public static RectTransform HeartsTarget => TopBar.Instance != null ? TopBar.Instance.HeartsTarget : null;

        /// <summary>Invokes a callback, logging (not throwing) its exceptions.</summary>
        public static void SafeInvoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T>(Action<T> a, T value)
        {
            if (a == null) return;
            try { a(value); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Makes any rect tappable: invisible hit area + Button + press squash of `visual` + click sound/haptic.</summary>
        public static Button MakeTappable(RectTransform rt, Transform visual, Action onClick)
        {
            if (rt == null) return null;
            var hit = rt.GetComponent<HitArea>();
            if (hit == null) hit = rt.gameObject.AddComponent<HitArea>();
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var press = rt.gameObject.AddComponent<Pressable>();
            press.target = visual != null ? visual : rt;
            press.pressedScale = DS.Motion.PressScale;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }
    }

    /// <summary>8-dot spinner built by <see cref="CommonUI.Spinner"/>. Steps 45° every 85 ms (unscaled).</summary>
    public sealed class CommonSpinner : MonoBehaviour
    {
        const int Dots = 8;
        const float Step = 0.085f;
        float _t;
        int _index;

        void OnEnable()
        {
            _t = 0f;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t < Step) return;
            _t -= Step;
            if (_t > Step) _t = 0f;
            _index = (_index + 1) % Dots;
            transform.localRotation = Quaternion.Euler(0f, 0f, -_index * (360f / Dots));
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        internal static CommonSpinner Create(Transform parent, float size, Color color)
        {
            if (parent == null) return null;
            var root = UIKit.Rect("Spinner", parent);
            root.sizeDelta = new Vector2(size, size);
            float dot = size * 0.22f;
            float r = size * 0.5f - dot * 0.5f;
            for (int i = 0; i < Dots; i++)
            {
                // Head at 12 o'clock, trail fading counter-clockwise (the whole thing steps clockwise).
                float a = (90f + i * (360f / Dots)) * Mathf.Deg2Rad;
                var img = UIKit.NewImage(root, "Dot", UISprites.Circle, DS.WithAlpha(color, 1f - i * 0.11f));
                float s = dot * (1f - i * 0.05f);
                UIKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(s, s), new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            return root.gameObject.AddComponent<CommonSpinner>();
        }
    }
}
