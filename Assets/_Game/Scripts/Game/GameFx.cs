// ============================================================================================================
// Game-screen set pieces: the "Level N" ribbon swoop (+ HARD LEVEL plate), the big "Level Complete!" moment with Luna
// cheering, the pre-booster "pop" icon, the held booster badge the Rainbow Potion's orbs fly into, and the floating
// combo praise ("Good!" … "Fantastic!"). Everything lives on the screen overlay / FX layer and never blocks input.
// ============================================================================================================
using System;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public static class GameFx
    {
        /// <summary>"Level N" ribbon swooping in from the left, holding, and leaving to the right. Hard levels tint
        /// the ribbon purple and add a shaking "HARD LEVEL" plate with skulls. `done` fires when it left.</summary>
        public static void LevelBanner(RectTransform parent, int level, bool hard, Action done)
        {
            if (parent == null) { GameUI.Invoke(done); return; }
            var root = UIKit.Rect("LevelBanner", parent);
            GameUI.PlaceCenter(root, new Vector2(0f, 80f), new Vector2(1000f, 520f));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // translucent band behind the ribbon
            var band = UIKit.NewImage(root, "Band", UISprites.Pixel, DS.WithAlpha(DS.Colors.Ink, 0f));
            GameUI.PlaceCenter(band.rectTransform, new Vector2(0f, 20f), new Vector2(3000f, hard ? 330f : 240f));
            Tween.Fade(band, hard ? 0.55f : 0.38f, 0.25f);

            const float w = 780f;
            float h = UIKit.RibbonHeight(w);
            var ribbon = UIKit.Ribbon(root, "level.number", w);
            GameUI.PlaceCenter(ribbon, new Vector2(-1400f, 40f), new Vector2(w, h));
            var title = ribbon != null ? ribbon.Find("Title") : null;
            var loc = title != null ? title.GetComponent<LocText>() : null;
            if (loc != null) loc.Set("level.number", level);
            if (hard && ribbon != null)
            {
                var img = ribbon.Find("ribbon_title");
                var g = img != null ? img.GetComponent<Image>() : null;
                if (g != null) g.color = new Color(0.62f, 0.5f, 1f, 1f);
            }
            AudioManager.Play(Sfx.Whoosh);
            if (ribbon != null) Tween.Move(ribbon, new Vector2(0f, 40f), 0.5f, Ease.OutBack).SetOvershoot(1.2f);
            Tween.Delay(0.42f, () =>
            {
                if (ribbon == null) return;
                FX.Sparkles(null, FX.LocalCenterOf(ribbon), 12);
                Tween.Punch(ribbon, 0.08f, 0.3f);
            }).SetLink(root);

            float hold = hard ? 1.55f : 1.05f;
            if (hard)
            {
                var plate = UIKit.Rect("Hard", root);
                GameUI.PlaceCenter(plate, new Vector2(0f, -110f), new Vector2(560f, 104f));
                UIKit.Shadow(plate, 20f, -8f, 0.3f);
                var cap = UIKit.Capsule(plate, new Vector2(560f, 104f), DS.Colors.Brand);
                UIKit.Stretch(cap.rectTransform);
                var label = UIKit.LocText(plate, "level.hard", TextStyle.H2, new Vector2(400f, 90f));
                label.fontStyle = FontStyles.UpperCase;
                UIKit.Stretch(label.rectTransform, 90f, 6f, 90f, 10f);
                for (int i = 0; i < 2; i++)
                {
                    var skull = UIKit.Image(plate, "icon_skull", new Vector2(120f, 120f));
                    UIKit.Place(skull.rectTransform, new Vector2(i == 0 ? 0f : 1f, 0.5f), new Vector2(120f, 120f),
                        new Vector2(i == 0 ? -30f : 30f, 4f));
                    skull.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    Tween.Rotate(skull.rectTransform, i == 0 ? -12f : 12f, 0.18f, Ease.InOutSine).SetLoops(-1, true);
                }
                plate.localScale = Vector3.zero;
                Tween.Scale(plate, 1f, 0.4f, Ease.OutBack).SetOvershoot(2f).SetDelay(0.45f);
                Tween.Delay(0.7f, () =>
                {
                    if (plate == null) return;
                    Tween.Shake(plate, 16f, 0.4f);
                    FX.Ring(null, FX.LocalCenterOf(plate), DS.Colors.Brand, 700f);
                    FX.ScreenShake(10f, 0.3f);
                    AudioManager.Play(Sfx.StoneBreak, 0.55f, 0.8f);
                    Haptics.Play(HapticType.Heavy);
                }).SetLink(root);
            }

            Tween.Delay(0.5f + hold, () =>
            {
                if (root == null) return;
                AudioManager.Play(Sfx.Swoosh, 0.8f);
                if (ribbon != null) Tween.Move(ribbon, new Vector2(1400f, 40f), 0.38f, Ease.InBack);
                Tween.Fade(group, 0f, 0.4f).SetDelay(0.05f);
                Tween.Delay(0.42f, () =>
                {
                    if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                    GameUI.Invoke(done);
                });
            }).SetLink(root);
        }

        /// <summary>Big "Level Complete!" with a rotating sunburst, Luna cheering, sparkles and confetti; fades out
        /// after ~1.8 s and then calls `done`.</summary>
        public static void BigCelebration(RectTransform parent, string key, Action done)
        {
            if (parent == null) { GameUI.Invoke(done); return; }
            var root = UIKit.Rect("Celebration", parent);
            GameUI.PlaceCenter(root, new Vector2(0f, 120f), new Vector2(1000f, 600f));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var burst = GameUI.Sunburst(root, 1100f, DS.WithAlpha(DS.Colors.Accent, 0.55f), 10f);
            if (burst != null)
            {
                burst.rectTransform.localScale = Vector3.zero;
                Tween.Scale(burst.rectTransform, 1f, 0.5f, Ease.OutBack);
            }
            var glow = GameUI.Glow(root, 900f, DS.WithAlpha(Color.white, 0.75f));
            if (glow != null)
            {
                glow.rectTransform.localScale = Vector3.zero;
                Tween.Scale(glow.rectTransform, 1f, 0.4f, Ease.OutCubic);
            }

            var text = UIKit.LocText(root, key, TextStyle.Display, new Vector2(980f, 200f));
            text.rectTransform.localScale = Vector3.zero;
            Tween.Scale(text.rectTransform, 1f, 0.55f, Ease.OutBack).SetOvershoot(2.2f).SetDelay(0.08f);
            Tween.Rotate(text.rectTransform, 3f, 0.6f, Ease.InOutSine).SetLoops(-1, true).SetDelay(0.6f);

            var mascot = UIKit.Image(root, "mascot_cheer", new Vector2(300f, 418f));
            GameUI.PlaceCenter(mascot.rectTransform, new Vector2(0f, -330f), new Vector2(300f, 418f));
            mascot.rectTransform.localScale = Vector3.zero;
            Tween.Scale(mascot.rectTransform, 1f, 0.45f, Ease.OutBack).SetDelay(0.25f);
            Tween.Bob(mascot.rectTransform, 14f, 0.8f);

            Vector2 center = FX.LocalCenterOf(root);
            FX.Celebrate(center);
            Tween.Delay(0.35f, () => FX.Sparkles(null, center + new Vector2(-260f, 40f), 10)).SetLink(root);
            Tween.Delay(0.55f, () => FX.Sparkles(null, center + new Vector2(260f, -20f), 10)).SetLink(root);
            Tween.Delay(0.8f, () => FX.Burst(null, center, "ui_star_small", 26, DS.Colors.Gold, 1000f, 0.9f, 46f, -1300f)).SetLink(root);

            Tween.Delay(1.8f, () =>
            {
                if (root == null) return;
                Tween.Scale(root, 1.12f, 0.3f, Ease.InQuad);
                Tween.Fade(group, 0f, 0.3f).OnComplete(() =>
                {
                    if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                    GameUI.Invoke(done);
                });
            }).SetLink(root);
        }

        /// <summary>A booster icon popping at a world position (pre-boosters), then flying to `to` (or fading).</summary>
        public static void BoosterPop(RectTransform parent, BoosterType type, Vector3 world, Vector3? to, Action onArrive)
        {
            if (parent == null) { GameUI.Invoke(onArrive); return; }
            var holder = BuildBoosterIcon(parent, type, world, 220f);
            AudioManager.Play(Sfx.Pop);
            FX.Sparkles(null, FX.ToLocal(null, world), 10);
            Tween.Delay(0.75f, () =>
            {
                if (holder == null) { GameUI.Invoke(onArrive); return; }
                if (to.HasValue)
                {
                    Vector3 target = to.Value;
                    Vector3 control = (holder.position + target) * 0.5f + Vector3.up * ((target - holder.position).magnitude * 0.35f);
                    Tween.MoveBezier(holder, control, target, 0.5f, Ease.InOutCubic);
                    Tween.Scale(holder, 0.35f, 0.5f, Ease.InQuad).OnComplete(() =>
                    {
                        if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
                        GameUI.Invoke(onArrive);
                    });
                }
                else
                {
                    Tween.Scale(holder, 1.4f, 0.25f, Ease.OutQuad);
                    var g = holder.gameObject.AddComponent<CanvasGroup>();
                    Tween.Fade(g, 0f, 0.25f).OnComplete(() =>
                    {
                        if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
                        GameUI.Invoke(onArrive);
                    });
                }
            }).SetLink(holder);
        }

        /// <summary>A booster icon that pops in at a world position and stays (bobbing, glowing) until Dismiss: the
        /// Rainbow Potion's color orbs fly into it, then it bursts.</summary>
        public sealed class Badge
        {
            RectTransform _holder;

            /// <summary>Current world position (where flying effects should go).</summary>
            public Vector3 World => _holder != null ? _holder.position : Vector3.zero;

            internal Badge(RectTransform holder) { _holder = holder; }

            /// <summary>Burst with sparkles and fade out (safe to call twice / after the screen was destroyed).</summary>
            public void Dismiss()
            {
                var h = _holder;
                _holder = null;
                if (h == null) return;
                FX.Sparkles(null, FX.ToLocal(null, h.position), 14);
                FX.Ring(null, FX.ToLocal(null, h.position), DS.WithAlpha(DS.Colors.Accent, 0.9f), 520f);
                Tween.Kill(h);
                Tween.Scale(h, 1.45f, 0.25f, Ease.OutQuad);
                var g = h.GetComponent<CanvasGroup>();
                if (g == null) g = h.gameObject.AddComponent<CanvasGroup>();
                Tween.Fade(g, 0f, 0.25f).OnComplete(() =>
                {
                    if (h != null) UnityEngine.Object.Destroy(h.gameObject);
                });
            }
        }

        public static Badge BoosterBadge(RectTransform parent, BoosterType type, Vector3 world)
        {
            if (parent == null) return new Badge(null);
            var holder = BuildBoosterIcon(parent, type, world, 200f);
            AudioManager.Play(Sfx.Pop);
            FX.Sparkles(null, FX.ToLocal(null, world), 10);
            Tween.Delay(0.4f, () =>
            {
                if (holder != null) Tween.Scale(holder, 1.08f, 0.5f, Ease.InOutSine).SetLoops(-1, true);
            }).SetLink(holder);
            return new Badge(holder);
        }

        static RectTransform BuildBoosterIcon(RectTransform parent, BoosterType type, Vector3 world, float size)
        {
            var holder = UIKit.Rect("BoosterPop", parent);
            holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(size, size);
            holder.position = world;
            var glow = GameUI.Glow(holder, size * 1.75f, DS.WithAlpha(DS.Colors.Accent, 0.8f));
            if (glow != null) GameUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(size * 1.75f, size * 1.75f));
            var icon = UIKit.Image(holder, Economy.BoosterSprite(type), new Vector2(size, size));
            if (icon != null) UIKit.Stretch(icon.rectTransform);
            holder.localScale = Vector3.zero;
            Tween.Scale(holder, 1f, 0.4f, Ease.OutBack).SetOvershoot(2f);
            return holder;
        }

        /// <summary>Floating combo praise above a bottle: x2 "Good!", x3 "Great!", x4 "Amazing!", x5+ "Fantastic!" (+ the
        /// "Combo xN" line, a ring from x3 and a screen shake from x5). Combos below 2 show nothing.</summary>
        public static void Praise(Vector3 world, int combo)
        {
            if (combo < 2) return;
            string key = combo >= 5 ? "game.praise.fantastic" : combo == 4 ? "game.praise.amazing" : combo == 3 ? "game.praise.great" : "game.praise.good";
            var palette = DS.Colors.Candy;
            Color c = palette[(combo - 2) % palette.Length];
            Vector2 local = FX.ToLocal(null, world);
            Vector2 at = GameUI.ClampToFxLayer(local + new Vector2(0f, 170f), 380f);
            FX.FloatingText(null, at, Loc.T(key), TextStyle.Display, c);
            FX.FloatingText(null, at + new Vector2(0f, -100f), Loc.T("game.combo", combo), TextStyle.H2, DS.Colors.Accent);
            if (combo >= 3) FX.Ring(null, local, DS.WithAlpha(c, 0.8f), 260f + combo * 30f);
            if (combo >= 5) FX.ScreenShake(8f + Mathf.Min(combo, 10) * 1.5f, 0.3f);
        }
    }
}
