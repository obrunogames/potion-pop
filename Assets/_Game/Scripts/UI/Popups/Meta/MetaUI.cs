// ============================================================================================================
// Shared building blocks of the Home meta popups and the Worlds screen: reward items (icon + amount), styled labels,
// a check stamp, star rows, localized world names, the slowly spinning sunburst and small animation helpers.
// Everything goes through the design system (UIKit/DS).
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
                var item = UIKit.Image(holder, r.IconSprite, new Vector2(iconSize * 0.6f, iconSize * 0.6f));
                UIKit.Place(item.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(iconSize * 0.56f, iconSize * 0.56f), new Vector2(0f, iconSize * 0.05f));
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

        // ---------------------------------------------------------------------------------------- stars

        /// <summary>Tint of an empty star slot (deep plum, translucent): reads as a hole waiting for a star.</summary>
        public static readonly Color EmptyStar = new Color(0.23f, 0.12f, 0.36f, 0.42f);

        /// <summary>
        /// Row of 3 stars (children "Star0..2"): the first <paramref name="filled"/> golden, the others empty slots. With
        /// an arc the middle star is raised and the outer ones tilt outwards (level map nodes, replay best).
        /// </summary>
        public static RectTransform StarRow(Transform parent, int filled, float starSize, float spacing, float arc = 0f)
        {
            var root = UIKit.Rect("Stars", parent);
            if (root == null) return null;
            root.sizeDelta = new Vector2(starSize * 3f + spacing * 2f, starSize + arc);
            for (int i = 0; i < 3; i++)
            {
                var img = UIKit.Image(root, "icon_star", new Vector2(starSize, starSize));
                img.name = "Star" + i;
                float x = (i - 1) * (starSize + spacing);
                float y = (i == 1 ? arc : 0f) - arc * 0.5f;
                UIKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(starSize, starSize), new Vector2(x, y));
                if (arc > 0f) img.rectTransform.localEulerAngles = new Vector3(0f, 0f, (1 - i) * 12f);
            }
            SetStars(root, filled);
            return root;
        }

        /// <summary>Recolors a row built by <see cref="StarRow"/>.</summary>
        public static void SetStars(RectTransform row, int filled)
        {
            if (row == null) return;
            for (int i = 0; i < 3; i++)
            {
                var t = row.Find("Star" + i);
                var img = t != null ? t.GetComponent<Image>() : null;
                if (img != null) img.color = i < filled ? Color.white : EmptyStar;
            }
        }

        /// <summary>Pops the filled stars of a row in one by one (with a sparkle and a rising "star" sound).</summary>
        public static void PopStars(RectTransform row, int filled, float delay, bool sound = true)
        {
            if (row == null) return;
            for (int i = 0; i < 3 && i < filled; i++)
            {
                var t = row.Find("Star" + i);
                if (t == null) continue;
                int index = i;
                var star = t;
                star.localScale = Vector3.zero;
                Tween.Scale(star, 1f, 0.38f, Ease.OutBack).SetOvershoot(2.4f).SetDelay(delay + i * 0.12f).OnComplete(() =>
                {
                    if (star == null) return;
                    if (sound) AudioManager.Play(Sfx.Star, 0.7f, 1f + index * 0.12f);
                    var sm = ScreenManager.Instance;
                    if (sm != null && sm.FxLayer != null) FX.Sparkles(sm.FxLayer, FX.LocalCenterOf((RectTransform)star), 3);
                });
            }
        }

        // ---------------------------------------------------------------------------------------- worlds

        /// <summary>
        /// Localized name of an (unbounded) world number: the theme name, plus a roman numeral once the themes cycle
        /// ("Enchanted Forest", ..., "Moonlit Village", "Enchanted Forest II"...). "World N" when the catalog is empty.
        /// </summary>
        public static string WorldName(int areaNumber)
        {
            var area = Areas.AreaForNumber(areaNumber);
            if (area == null) return Loc.T("worlds.world_number", areaNumber + 1);
            string name = Loc.T(area.NameKey);
            int cycle = Areas.CycleOfArea(areaNumber);
            if (cycle <= 0) return name;
            return Loc.Has("worlds.cycle_name") ? Loc.T("worlds.cycle_name", name, Roman(cycle + 1)) : name + " " + Roman(cycle + 1);
        }

        /// <summary>Roman numeral (1..3999; plain digits outside that range).</summary>
        public static string Roman(int n)
        {
            if (n <= 0 || n >= 4000) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
                while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
            return sb.ToString();
        }

        /// <summary>Horizontal "x / y" style progress text, e.g. "350/1000".</summary>
        public static string Fraction(long value, long total) => Loc.T("quest.progress", Loc.Number(value), Loc.Number(total));
    }
}
