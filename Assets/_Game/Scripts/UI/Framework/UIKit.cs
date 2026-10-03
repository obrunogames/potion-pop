// ============================================================================================================
// UIKit: code-first factory for every design-system component (Docs/DesignSystem.md §6). All methods are null-safe
// for missing art: sprites come from Art (via UISprites) and fall back to procedural shapes / plain colored rects.
// ============================================================================================================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public static class UIKit
    {
        /// <summary>Unity's built-in "UI" layer.</summary>
        public const int UILayer = 5;

        // ---------------------------------------------------------------------------------------- layout primitives

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Rect" : name, typeof(RectTransform)) { layer = UILayer };
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchors to stretch the full parent with the given insets.</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            if (rt == null) return null;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Sets anchor+pivot to a point (e.g. (0.5,1) = top center), size and anchored position.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 anchoredPos)
        {
            if (rt == null) return null;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        /// <summary>
        /// Makes a rect cover the whole screen even though its parent lives inside the safe area (backgrounds,
        /// dim overlays). Use on rects whose parent spans the SafeRoot (a screen Root, a layer, a popup root).
        /// </summary>
        public static RectTransform FullBleed(RectTransform rt, float overscan = 0f)
        {
            if (rt == null) return null;
            var fb = rt.GetComponent<PotionPop.UI.FullBleed>();
            if (fb == null) fb = rt.gameObject.AddComponent<PotionPop.UI.FullBleed>();
            fb.overscan = overscan;
            fb.Apply();
            return rt;
        }

        /// <summary>Empty layout spacer (for HRow/VColumn).</summary>
        public static RectTransform Spacer(Transform parent, Vector2 size)
        {
            var rt = Rect("Spacer", parent);
            rt.sizeDelta = size;
            return rt;
        }

        // ---------------------------------------------------------------------------------------- images

        /// <summary>Raw Image creation (no slicing logic, raycastTarget off). Null sprite = plain colored rect.</summary>
        public static UnityEngine.UI.Image NewImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static UnityEngine.UI.Image Image(Transform parent, string spriteName, Vector2 size, bool preserveAspect = true)
        {
            if (parent == null) return null;
            var sp = UISprites.Get(spriteName);
            var img = Image(parent, sp, size, preserveAspect);
            img.name = string.IsNullOrEmpty(spriteName) ? "Image" : spriteName;
            if (sp == null)
            {
                // Missing art: soft lavender dot so the layout stays readable.
                img.sprite = UISprites.Circle;
                img.color = DS.WithAlpha(DS.Colors.BrandLight, 0.6f);
                img.preserveAspect = true;
            }
            return img;
        }

        public static UnityEngine.UI.Image Image(Transform parent, Sprite sprite, Vector2 size, bool preserveAspect = true)
        {
            if (parent == null) return null;
            var img = NewImage(parent, sprite != null ? sprite.name : "Image", sprite, Color.white);
            img.rectTransform.sizeDelta = size;
            img.preserveAspect = preserveAspect;
            return img;
        }

        /// <summary>9-sliced image (sprite borders come from the importer). Caps scale with the rect height.</summary>
        public static UnityEngine.UI.Image Sliced(Transform parent, string spriteName, Vector2 size, Color? tint = null)
        {
            if (parent == null) return null;
            var sp = UISprites.Get(spriteName);
            var img = NewImage(parent, string.IsNullOrEmpty(spriteName) ? "Sliced" : spriteName,
                sp != null ? sp : UISprites.Rounded, tint ?? Color.white);
            img.rectTransform.sizeDelta = size;
            if (sp != null) SliceFit.Attach(img, SliceFit.Mode.Height);
            else
            {
                if (tint == null) img.color = DS.Colors.CreamDark;
                SliceFit.Attach(img, SliceFit.Mode.Corner, 28f);
            }
            return img;
        }

        /// <summary>Procedural rounded rectangle ("ui_rounded" 9-slice) tinted with a color.</summary>
        public static UnityEngine.UI.Image RoundedRect(Transform parent, Vector2 size, Color color) => RoundedRect(parent, size, color, 28f);

        /// <summary>Rounded rectangle with a given corner radius (clamped to half the smaller side).</summary>
        public static UnityEngine.UI.Image RoundedRect(Transform parent, Vector2 size, Color color, float radius)
        {
            if (parent == null) return null;
            var img = NewImage(parent, "RoundedRect", UISprites.Rounded, color);
            img.rectTransform.sizeDelta = size;
            SliceFit.Attach(img, SliceFit.Mode.Corner, radius * UISprites.RoundedBorderPerRadius);
            return img;
        }

        /// <summary>Capsule ("ui_capsule", round ends = half the height) tinted with a color. Exact pill at any size.</summary>
        public static UnityEngine.UI.Image Capsule(Transform parent, Vector2 size, Color color)
        {
            if (parent == null) return null;
            var img = NewImage(parent, "Capsule", UISprites.Capsule, color);
            img.rectTransform.sizeDelta = size;
            SliceFit.Attach(img, SliceFit.Mode.Height);
            return img;
        }

        /// <summary>Card/inset/popup surfaces ("panel_card", "panel_inset", "panel_popup").</summary>
        public static UnityEngine.UI.Image Panel(Transform parent, string spriteName, Vector2 size)
        {
            if (parent == null) return null;
            var sp = UISprites.Get(spriteName);
            if (sp == null)
            {
                // Fallback surfaces drawn with rounded rects.
                switch (spriteName)
                {
                    case "panel_popup":
                    {
                        var frame = RoundedRect(parent, size, DS.Colors.Brand, 64f);
                        frame.name = "panel_popup";
                        var inner = RoundedRect(frame.rectTransform, Vector2.zero, DS.Colors.Cream, 44f);
                        Stretch(inner.rectTransform, 22, 22, 22, 30);
                        return frame;
                    }
                    case "panel_inset":
                    {
                        var r = RoundedRect(parent, size, DS.Colors.CreamDark, 32f);
                        r.name = "panel_inset";
                        return r;
                    }
                    default:
                    {
                        var r = RoundedRect(parent, size, Color.white, 36f);
                        r.name = string.IsNullOrEmpty(spriteName) ? "Panel" : spriteName;
                        return r;
                    }
                }
            }
            var img = NewImage(parent, spriteName, sp, Color.white);
            img.rectTransform.sizeDelta = size;
            SliceFit.Attach(img, SliceFit.Mode.Corner, PanelCorner(spriteName));
            return img;
        }

        /// <summary>Default on-screen corner size of a panel sprite (Corner slicing mode).</summary>
        public static float PanelCorner(string spriteName)
        {
            switch (spriteName)
            {
                case "panel_popup": return 110f;
                case "panel_card": return 48f;
                case "panel_inset": return 40f;
                case "btn_square": return 44f;
                default: return 48f;
            }
        }

        /// <summary>Soft drop shadow (ui_soft_shadow) behind a rect: same size grown by `spread`, offset down.</summary>
        public static UnityEngine.UI.Image Shadow(Transform parent, float spread = 40f, float offsetY = -18f, float alpha = 0.35f)
        {
            if (parent == null) return null;
            var img = NewImage(parent, "Shadow", UISprites.SoftShadow, new Color(0f, 0f, 0f, alpha));
            Stretch(img.rectTransform, -spread, -spread - offsetY, -spread, -spread + offsetY);
            SliceFit.Attach(img, SliceFit.Mode.Corner, spread * 2f);
            return img;
        }

        /// <summary>Full-screen background image (bleeds under notches, 40 px overscan for screen shakes) that covers
        /// the screen without distortion. Put it first under a screen Root.</summary>
        public static UnityEngine.UI.Image Backdrop(Transform parent, string spriteName, Color? color = null)
        {
            if (parent == null) return null;
            var holder = FullBleed(Rect("Backdrop", parent), 40f);
            holder.SetAsFirstSibling();
            var sp = UISprites.Get(spriteName);
            var img = NewImage(holder, string.IsNullOrEmpty(spriteName) ? "Backdrop" : spriteName, sp,
                color ?? (sp != null ? Color.white : DS.Colors.Lavender));
            Stretch(img.rectTransform);
            if (sp != null && sp.rect.height > 0f)
            {
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = sp.rect.width / sp.rect.height;
            }
            return img;
        }

        // ---------------------------------------------------------------------------------------- text

        public static TMP_Text Text(Transform parent, string text, TextStyle style, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            if (parent == null) return null;
            var rt = Rect("Text", parent);
            rt.sizeDelta = size;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            DS.Apply(t, style);
            t.alignment = align;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        /// <summary>Localized text (adds LocText so it follows language changes).</summary>
        public static TMP_Text LocText(Transform parent, string key, TextStyle style, Vector2 size, params object[] args)
        {
            var t = Text(parent, "", style, size);
            if (t == null) return null;
            t.name = string.IsNullOrEmpty(key) ? "LocText" : key;
            var loc = t.gameObject.AddComponent<PotionPop.UI.LocText>();
            loc.Set(key, args);
            return t;
        }

        // ---------------------------------------------------------------------------------------- buttons

        public static UIButton Button(Transform parent, string label, ButtonColor color, ButtonSize size, Action onClick, string iconSprite = null) =>
            UIButton.Create(parent, label, color, DS.ButtonDimensions(size), DS.ButtonTextStyle(size), onClick, iconSprite);

        /// <summary>Button with a custom size (label style picked from the height).</summary>
        public static UIButton Button(Transform parent, string label, ButtonColor color, Vector2 size, Action onClick, string iconSprite = null) =>
            UIButton.Create(parent, label, color, size, size.y >= 160f ? TextStyle.H2 : TextStyle.H3, onClick, iconSprite);

        public static UIButton ButtonLoc(Transform parent, string key, ButtonColor color, ButtonSize size, Action onClick, string iconSprite = null)
        {
            var b = Button(parent, "", color, size, onClick, iconSprite);
            if (b != null) b.SetLabelKey(key);
            return b;
        }

        /// <summary>Localized button with a custom size.</summary>
        public static UIButton ButtonLoc(Transform parent, string key, ButtonColor color, Vector2 size, Action onClick, string iconSprite = null)
        {
            var b = Button(parent, "", color, size, onClick, iconSprite);
            if (b != null) b.SetLabelKey(key);
            return b;
        }

        /// <summary>Icon-only button (no background unless bgSprite given).</summary>
        public static UIButton IconButton(Transform parent, string spriteName, float size, Action onClick, string bgSprite = null) =>
            UIButton.CreateIcon(parent, spriteName, size, onClick, bgSprite);

        // ---------------------------------------------------------------------------------------- widgets

        public static CounterPill CounterPill(Transform parent, string iconSprite, float width, Action onPlus) =>
            PotionPop.UI.CounterPill.Create(parent, iconSprite, width, onPlus);

        public static ProgressBar ProgressBar(Transform parent, Vector2 size) => PotionPop.UI.ProgressBar.Create(parent, size);

        /// <summary>Red badge anchored to the parent's top-right corner at anchoredPos (hidden until SetCount/SetText).</summary>
        public static Badge Badge(Transform parent, Vector2 anchoredPos) => PotionPop.UI.Badge.Create(parent, anchoredPos);

        public static UIToggle Toggle(Transform parent, bool value, Action<bool> onChanged) => UIToggle.Create(parent, value, onChanged);

        /// <summary>Title ribbon with localized H1 text.</summary>
        public static RectTransform Ribbon(Transform parent, string key, float width)
        {
            if (parent == null) return null;
            float h = RibbonHeight(width);
            var root = Rect("Ribbon", parent);
            root.sizeDelta = new Vector2(width, h);
            var sp = UISprites.Get("ribbon_title");
            var img = NewImage(root, "ribbon_title", sp != null ? sp : UISprites.Rounded, sp != null ? Color.white : DS.Colors.Pink);
            Stretch(img.rectTransform);
            if (sp == null) SliceFit.Attach(img, SliceFit.Mode.Corner, h * 0.3f);
            else if (sp.border != Vector4.zero) SliceFit.Attach(img, SliceFit.Mode.Height);
            else img.preserveAspect = true;   // the root has the art aspect unless RibbonHeight clamped it: never distort

            // ribbon_title is an arched band with folded tails hanging below it: across the middle 64% of the width the
            // front band spans ~6%..60% of the height from the top, i.e. its center line is ~15% above the sprite center.
            var title = LocText(root, key, TextStyle.H1, new Vector2(width * 0.64f, h * 0.44f));
            if (title != null)
            {
                title.rectTransform.anchoredPosition = new Vector2(0, h * (sp != null ? 0.15f : 0f));
                title.name = "Title";
            }
            return root;
        }

        /// <summary>Height of a ribbon of the given width: the exact ribbon art aspect (no distortion), or 26% of the
        /// width clamped to 140..260 for the drawn fallback.</summary>
        public static float RibbonHeight(float width)
        {
            var sp = UISprites.Get("ribbon_title");
            if (sp != null && sp.rect.width > 0f) return width * sp.rect.height / sp.rect.width;
            return Mathf.Clamp(width * 0.26f, 140f, 260f);
        }

        // ---------------------------------------------------------------------------------------- feedback

        /// <summary>Top toast message (already localized text).</summary>
        public static void Toast(string text) => Toasts.Show(text);

        /// <summary>Spawns `count` sprites at a world position that fly (bezier, staggered) to target and bump it.</summary>
        public static void FlyRewards(string spriteName, int count, Vector3 fromWorld, RectTransform target, Action onEachArrive = null, Action onAllArrived = null) =>
            FX.FlyRewards(spriteName, count, fromWorld, target, onEachArrive, onAllArrived);

        // ---------------------------------------------------------------------------------------- layout groups

        /// <summary>Horizontal/vertical layout helpers.</summary>
        public static HorizontalLayoutGroup HRow(Transform parent, Vector2 size, float spacing)
        {
            if (parent == null) return null;
            var rt = Rect("HRow", parent);
            rt.sizeDelta = size;
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = false;
            g.childControlHeight = false;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static VerticalLayoutGroup VColumn(Transform parent, Vector2 size, float spacing)
        {
            if (parent == null) return null;
            var rt = Rect("VColumn", parent);
            rt.sizeDelta = size;
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = TextAnchor.UpperCenter;
            g.childControlWidth = false;
            g.childControlHeight = false;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        /// <summary>Grid of fixed-size cells (rows grow downwards, centered).</summary>
        public static GridLayoutGroup Grid(Transform parent, Vector2 size, Vector2 cellSize, Vector2 spacing, int columns)
        {
            if (parent == null) return null;
            var rt = Rect("Grid", parent);
            rt.sizeDelta = size;
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cellSize;
            g.spacing = spacing;
            g.childAlignment = TextAnchor.UpperCenter;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = Mathf.Max(1, columns);
            return g;
        }

        /// <summary>
        /// Vertical scroll view. `content` is a top-anchored VerticalLayoutGroup (children keep their own sizes,
        /// centered horizontally) whose height follows its children (ContentSizeFitter).
        /// </summary>
        public static ScrollRect ScrollView(Transform parent, Vector2 size, out RectTransform content, float spacing = 16f, float padding = 16f)
        {
            content = null;
            if (parent == null) return null;
            var root = Rect("ScrollView", parent);
            root.sizeDelta = size;
            var viewport = Stretch(Rect("Viewport", root));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<HitArea>();   // drag anywhere, even between children

            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            int p = Mathf.RoundToInt(padding);
            v.padding = new RectOffset(p, p, p, p);
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = false;
            v.childControlHeight = false;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;
            return scroll;
        }
    }
}
