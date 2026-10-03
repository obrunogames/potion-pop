// ============================================================================================================
// Shared building blocks of the Home meta popups: reward items (icon + amount), styled labels, a check stamp,
// the slowly spinning sunburst and small animation helpers. Everything goes through the design system (UIKit/DS).
// ============================================================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public static class MetaUI
    {
        /// <summary>Reward icon with its amount underneath ("250", "x2", "30m"). Card rewards get a card frame.</summary>
        public static RectTransform RewardItem(Transform parent, Reward r, float iconSize, float textSize = 46f)
        {
            var root = UIKit.Rect("Reward_" + r.type, parent);
            float textH = textSize * 1.25f;
            root.sizeDelta = new Vector2(iconSize + 24f, iconSize + textH * 0.75f);
            var holder = UIKit.Rect("Icon", root);
            UIKit.Place(holder, new Vector2(0.5f, 1f), new Vector2(iconSize, iconSize), Vector2.zero);
            if (r.type == RewardType.Card)
            {
                var frame = UIKit.Image(holder, "card_frame", new Vector2(iconSize * 0.72f, iconSize));
                UIKit.Stretch(frame.rectTransform);
                var product = UIKit.Image(holder, r.IconSprite, new Vector2(iconSize * 0.6f, iconSize * 0.6f));
                UIKit.Place(product.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(iconSize * 0.56f, iconSize * 0.56f), new Vector2(0f, iconSize * 0.05f));
            }
            else
            {
                var icon = UIKit.Image(holder, r.IconSprite, new Vector2(iconSize, iconSize));
                UIKit.Stretch(icon.rectTransform);
            }
            var t = UIKit.Text(root, r.AmountText, TextStyle.H3, new Vector2(iconSize + 60f, textH));
            DS.Apply(t, TextStyle.H3, textSize);
            t.name = "Amount";
            UIKit.Place(t.rectTransform, new Vector2(0.5f, 0f), new Vector2(iconSize + 60f, textH), new Vector2(0f, -textH * 0.25f));
            return root;
        }

        /// <summary>The icon holder of a RewardItem (for fly-from positions and punches).</summary>
        public static RectTransform IconOf(RectTransform rewardItem)
        {
            if (rewardItem == null) return null;
            var t = rewardItem.Find("Icon") as RectTransform;
            return t != null ? t : rewardItem;
        }

        /// <summary>Plain text in a style with a custom max size, placed at (anchor, pos).</summary>
        public static TMP_Text Label(Transform parent, string text, TextStyle style, float fontSize, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            var t = UIKit.Text(parent, text, style, size);
            if (t == null) return null;
            DS.Apply(t, style, fontSize);
            UIKit.Place(t.rectTransform, anchor, size, pos);
            return t;
        }

        /// <summary>Localized text (follows language changes) with a custom max size.</summary>
        public static TMP_Text LocLabel(Transform parent, string key, TextStyle style, float fontSize, Vector2 size, Vector2 anchor, Vector2 pos, params object[] args)
        {
            var t = UIKit.LocText(parent, key, style, size, args);
            if (t == null) return null;
            DS.Apply(t, style, fontSize);
            UIKit.Place(t.rectTransform, anchor, size, pos);
            return t;
        }

        /// <summary>Sets a LocText key/args on a text created by LocLabel/UIKit.LocText.</summary>
        public static void SetLoc(TMP_Text text, string key, params object[] args)
        {
            if (text == null) return;
            var loc = text.GetComponent<LocText>();
            if (loc == null) loc = text.gameObject.AddComponent<LocText>();
            loc.Set(key, args);
        }

        /// <summary>Slowly rotating golden sunburst (behind rewards/chests). Returns the image.</summary>
        public static Image Sunburst(Transform parent, float size, float alpha = 0.9f, float secondsPerTurn = 14f)
        {
            var img = UIKit.Image(parent, "sunburst", new Vector2(size, size));
            if (img == null) return null;
            img.color = new Color(1f, 1f, 1f, alpha);
            img.name = "Sunburst";
            Spin(img.transform, secondsPerTurn);
            return img;
        }

        /// <summary>Endless linear rotation (clockwise for positive seconds).</summary>
        public static void Spin(Transform t, float secondsPerTurn)
        {
            if (t == null || Mathf.Abs(secondsPerTurn) < 0.01f) return;
            t.localEulerAngles = Vector3.zero;
            Tween.Rotate(t, secondsPerTurn > 0f ? -360f : 360f, Mathf.Abs(secondsPerTurn), Ease.Linear).SetLoops(-1, false);
        }

        /// <summary>Green circle with a white check, stamped in with a thump.</summary>
        public static RectTransform CheckStamp(Transform parent, float size, bool animate)
        {
            var root = UIKit.Rect("Check", parent);
            root.sizeDelta = new Vector2(size, size);
            UIKit.Shadow(root, size * 0.16f, -size * 0.06f, 0.3f);
            var circle = UIKit.NewImage(root, "Circle", UISprites.Circle, DS.Colors.Primary);
            UIKit.Stretch(circle.rectTransform);
            var rim = UIKit.NewImage(root, "Rim", UISprites.Ring, new Color(1f, 1f, 1f, 0.9f));
            UIKit.Stretch(rim.rectTransform, size * 0.04f, size * 0.04f, size * 0.04f, size * 0.04f);
            var check = UIKit.Image(root, "icon_check", new Vector2(size * 0.62f, size * 0.62f));
            UIKit.Stretch(check.rectTransform, size * 0.18f, size * 0.2f, size * 0.18f, size * 0.16f);
            if (animate)
            {
                root.localScale = Vector3.one * 2.4f;
                Tween.Scale(root, 1f, 0.32f, Ease.OutBack).SetOvershoot(1.6f);
            }
            return root;
        }

        /// <summary>Small capsule tag (e.g. HARD, FREE) with an optional leading icon.</summary>
        public static RectTransform Tag(Transform parent, string key, Color color, string icon, Vector2 size, float fontSize = 34f)
        {
            var root = UIKit.Rect("Tag", parent);
            root.sizeDelta = size;
            UIKit.Shadow(root, 14f, -6f, 0.28f);
            var bg = UIKit.Capsule(root, Vector2.zero, color);
            UIKit.Stretch(bg.rectTransform);
            var gloss = UIKit.Capsule(root, Vector2.zero, new Color(1f, 1f, 1f, 0.22f));
            UIKit.Stretch(gloss.rectTransform, size.y * 0.4f, size.y * 0.1f, size.y * 0.4f, size.y * 0.52f);
            float left = size.y * 0.35f;
            if (!string.IsNullOrEmpty(icon))
            {
                float s = size.y * 1.25f;
                var img = UIKit.Image(root, icon, new Vector2(s, s));
                UIKit.Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(s, s), new Vector2(-s * 0.2f, size.y * 0.06f));
                left = s * 0.82f;
            }
            var t = UIKit.LocText(root, key, TextStyle.H3, size);
            DS.Apply(t, TextStyle.H3, fontSize);
            UIKit.Stretch(t.rectTransform, left, 2f, size.y * 0.35f, 4f);
            return root;
        }

        /// <summary>Staggered pop-in of a list of transforms.</summary>
        public static void PopInAll(IList<RectTransform> items, float delay, float stagger)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var t = items[i];
                if (t == null) continue;
                t.localScale = Vector3.zero;
                Tween.Scale(t, 1f, DS.Motion.Slow, Ease.OutBack).SetOvershoot(DS.Motion.PopOvershoot + 0.6f).SetDelay(delay + i * stagger);
            }
        }

        /// <summary>World position of the visual center of a rect.</summary>
        public static Vector3 WorldCenter(RectTransform rt) => rt != null ? rt.TransformPoint(rt.rect.center) : Vector3.zero;

        /// <summary>Sparkles + gold star burst at a rect, on the FX layer.</summary>
        public static void Celebrate(RectTransform at, int sparkles = 10, int stars = 14)
        {
            var sm = ScreenManager.Instance;
            if (sm == null || sm.FxLayer == null || at == null) return;
            Vector2 p = FX.LocalCenterOf(at);
            FX.Sparkles(sm.FxLayer, p, sparkles);
            if (stars > 0) FX.Burst(sm.FxLayer, p, "ui_star_small", stars, DS.Colors.Gold, 700f, 0.75f, 40f, -1200f);
        }

        /// <summary>Horizontal "x / y" style progress text, e.g. "350/1000".</summary>
        public static string Fraction(long value, long total) => Loc.T("quest.progress", Loc.Number(value), Loc.Number(total));
    }
}
