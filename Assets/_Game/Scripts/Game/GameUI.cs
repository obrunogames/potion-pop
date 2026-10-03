// ============================================================================================================
// Small layout/animation helpers shared by the game screen, HUD and in-level popups (top-down stacking, sunburst,
// staggered reveals, mascot entrance). Everything goes through the design-system factories (UIKit / DS / Tween).
// ============================================================================================================
using System;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public static class GameUI
    {
        /// <summary>Anchors a child to the top-center of its parent at y (distance from the top) with a size.</summary>
        public static RectTransform PlaceTop(RectTransform rt, float y, Vector2 size, float x = 0f)
        {
            if (rt == null) return null;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        /// <summary>Anchors a child to the bottom-center of its parent at y (distance from the bottom).</summary>
        public static RectTransform PlaceBottom(RectTransform rt, float y, Vector2 size, float x = 0f)
        {
            if (rt == null) return null;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        /// <summary>Centered child (pivot center) at an offset from the parent's center.</summary>
        public static RectTransform PlaceCenter(RectTransform rt, Vector2 offset, Vector2 size)
        {
            if (rt == null) return null;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        /// <summary>Endlessly rotating sunburst (decorative glow behind rewards).</summary>
        public static Image Sunburst(Transform parent, float size, Color tint, float secondsPerTurn = 14f)
        {
            var img = UIKit.Image(parent, "sunburst", new Vector2(size, size));
            if (img == null) return null;
            img.color = tint;
            img.rectTransform.localRotation = Quaternion.identity;
            Tween.Rotate(img.rectTransform, -360f, secondsPerTurn, Ease.Linear).SetLoops(-1, false);
            return img;
        }

        /// <summary>Soft glow disc (ui_glow) behind an icon.</summary>
        public static Image Glow(Transform parent, float size, Color tint)
        {
            if (parent == null) return null;
            var img = UIKit.NewImage(parent, "Glow", UISprites.Glow, tint);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        /// <summary>Pops a rect in after a delay (scale 0 → 1, OutBack), optionally with a sound.</summary>
        public static void PopInDelayed(Transform t, float delay, Sfx? sfx = null, float to = 1f)
        {
            if (t == null) return;
            t.localScale = Vector3.zero;
            Tween.Scale(t, to, DS.Motion.Slow, Ease.OutBack).SetOvershoot(DS.Motion.PopOvershoot).SetDelay(delay);
            if (sfx.HasValue)
            {
                Sfx s = sfx.Value;
                Tween.Delay(delay, () => AudioManager.Play(s, 0.7f)).SetLink(t);
            }
        }

        /// <summary>Starts an endless CTA pulse after a delay (so it pulses around the final scale of a pop-in).</summary>
        public static void PulseAfter(Transform t, float delay, float scale = DS.Motion.PulseScale, float period = DS.Motion.PulsePeriod)
        {
            if (t == null) return;
            Tween.Delay(delay, () =>
            {
                if (t == null) return;
                t.localScale = Vector3.one;
                Tween.Pulse(t, scale, period);
            }).SetLink(t);
        }

        /// <summary>Slides a rect up from `distance` below its position while fading it in (CanvasGroup added).</summary>
        public static void RiseIn(RectTransform rt, float delay, float distance = 60f)
        {
            if (rt == null) return;
            var g = rt.GetComponent<CanvasGroup>();
            if (g == null) g = rt.gameObject.AddComponent<CanvasGroup>();
            // Finish a previous entrance first so its start offset never becomes the new resting position.
            Tween.Kill(rt, true);
            Tween.Kill(g, true);
            g.alpha = 0f;
            Vector2 p = rt.anchoredPosition;
            rt.anchoredPosition = p - new Vector2(0f, distance);
            Tween.Move(rt, p, DS.Motion.Slow, Ease.OutBack).SetDelay(delay);
            Tween.Fade(g, 1f, DS.Motion.Base, Ease.OutQuad).SetDelay(delay);
        }

        /// <summary>Mascot image with an idle bob (sprite: mascot_cheer, mascot_sad, ...).</summary>
        public static Image Mascot(RectTransform parent, string sprite, Vector2 size, Vector2 anchoredTopCenter)
        {
            if (parent == null) return null;
            var holder = UIKit.Rect("Mascot", parent);
            PlaceTop(holder, -anchoredTopCenter.y, size, anchoredTopCenter.x);
            var img = UIKit.Image(holder, sprite, size);
            if (img == null) return null;
            UIKit.Stretch(img.rectTransform);
            PopInDelayed(holder, 0.12f);
            Tween.Bob(img.rectTransform, 8f, 2.4f);
            return img;
        }

        /// <summary>Localized label anchored top-center at y.</summary>
        public static TMP_Text LabelTop(RectTransform parent, string key, TextStyle style, float y, Vector2 size, params object[] args)
        {
            var t = UIKit.LocText(parent, key, style, size, args);
            if (t != null) PlaceTop(t.rectTransform, y, size);
            return t;
        }

        /// <summary>Cream inset row (panel_inset) used for settings-like rows inside popups.</summary>
        public static RectTransform InsetRow(RectTransform parent, Vector2 size)
        {
            var img = UIKit.Panel(parent, "panel_inset", size);
            return img != null ? img.rectTransform : UIKit.Rect("Inset", parent);
        }

        /// <summary>Round count badge (brand purple, white ring, white number) anchored to a corner of `parent`.</summary>
        public static RectTransform CountBadge(RectTransform parent, int count, float size, Vector2 anchor, Vector2 offset, out TMP_Text text)
        {
            var badge = UIKit.Rect("Count", parent);
            UIKit.Place(badge, anchor, new Vector2(size, size), offset);
            var circle = UIKit.NewImage(badge, "Circle", UISprites.Circle, DS.Colors.Brand);
            UIKit.Stretch(circle.rectTransform);
            var ring = UIKit.NewImage(badge, "Ring", UISprites.Ring, Color.white);
            UIKit.Stretch(ring.rectTransform, -3f, -3f, -3f, -3f);
            text = UIKit.Text(badge, count > 99 ? "99+" : count.ToString(), TextStyle.Badge, new Vector2(size - 6f, size - 6f));
            DS.Apply(text, TextStyle.Badge, size * 0.55f);
            UIKit.Stretch(text.rectTransform, 4f, 2f, 4f, 4f);
            return badge;
        }

        /// <summary>Clamps an x position (FX layer space) so a centered element of `halfWidth` stays on screen.</summary>
        public static Vector2 ClampToFxLayer(Vector2 local, float halfWidth)
        {
            var sm = ScreenManager.Instance;
            if (sm == null || sm.FxLayer == null) return local;
            float half = sm.FxLayer.rect.width * 0.5f - halfWidth;
            if (half < 0f) half = 0f;
            local.x = Mathf.Clamp(local.x, -half, half);
            return local;
        }

        /// <summary>World-space center of a rect (null-safe: Vector3.zero).</summary>
        public static Vector3 WorldCenter(RectTransform rt) => rt != null ? rt.TransformPoint(rt.rect.center) : Vector3.zero;

        /// <summary>
        /// Gives a game-screen element its own nested canvas (same pattern as the ScreenManager layers and TopBar), so its
        /// per-frame changes (pulses, particles, counters) rebatch only that canvas instead of the board + HUD graphics.
        /// overrideSorting stays off: hierarchy draw order, parent CanvasGroup alpha / raycast blocking and RectMask2D
        /// clipping keep working. <paramref name="withRaycaster"/> is REQUIRED when the element holds anything clickable
        /// (graphics register with their nearest canvas, and only that canvas' raycaster can hit them).
        /// </summary>
        public static Canvas NestCanvas(GameObject go, bool withRaycaster)
        {
            if (go == null) return null;
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            var sm = ScreenManager.Instance;
            if (sm != null && sm.Canvas != null) canvas.additionalShaderChannels = sm.Canvas.additionalShaderChannels;   // TMP outline/underlay
            if (withRaycaster && go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Safe invoke for UI callbacks (exceptions logged, never thrown into tweens/ads).</summary>
        public static void Invoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void Invoke<T>(Action<T> a, T v)
        {
            if (a == null) return;
            try { a(v); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
