// ============================================================================================================
// Tapped a stone-wrapped bottle (GDD §2): explains that every completed bottle cracks the stone (N to go) and offers to
// break it right now with a rewarded ad. "Break it!" closes the popup and the session runs the ad; closing just resumes.
// ============================================================================================================
using System;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed class StonePopup : Popup
    {
        protected override string TitleKey => "board.stone.title";
        protected override Vector2 PanelSize => new Vector2(880f, 1000f);

        int _remaining;
        Action _onBreak;
        bool _done;

        /// <summary>remaining: completions still needed to crack it. onBreak runs after the popup closed.</summary>
        public static StonePopup Open(int remaining, Action onBreak)
        {
            var existing = PopupManager.Get<StonePopup>();
            if (existing != null) return existing;
            return PopupManager.Show<StonePopup>(p =>
            {
                p._remaining = Mathf.Max(1, remaining);
                p._onBreak = onBreak;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float y = 0f;

            // stone with its counter on a soft glow
            var hero = UIKit.Rect("Hero", content);
            GameUI.PlaceTop(hero, y, new Vector2(w, 360f));
            var glow = GameUI.Glow(hero, 480f, DS.WithAlpha(DS.Colors.BrandLight, 0.6f));
            if (glow != null) GameUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(480f, 480f));
            var stone = UIKit.Rect("Stone", hero);
            GameUI.PlaceCenter(stone, Vector2.zero, new Vector2(240f, 240f));
            var disc = UIKit.NewImage(stone, "Disc", UISprites.Circle, new Color(0.55f, 0.52f, 0.62f, 1f));
            UIKit.Stretch(disc.rectTransform);
            var rim = UIKit.NewImage(stone, "Rim", UISprites.Ring, new Color(0.36f, 0.33f, 0.44f, 1f));
            UIKit.Stretch(rim.rectTransform, -6f, -6f, -6f, -6f);
            var padlock = UIKit.Image(stone, "icon_lock", new Vector2(150f, 150f));
            GameUI.PlaceCenter(padlock.rectTransform, new Vector2(0f, 10f), new Vector2(150f, 150f));
            GameUI.CountBadge(stone, _remaining, 92f, new Vector2(1f, 1f), new Vector2(10f, 10f), out _);
            GameUI.PopInDelayed(stone, 0.15f, Sfx.StoneCrack);
            Tween.Delay(0.6f, () =>
            {
                if (stone == null) return;
                Tween.Shake(stone, 10f, 0.3f);
                FX.Burst(null, FX.LocalCenterOf(stone), "fx_poof", 4, null, 220f, 0.5f, 70f, 0f);
            }).SetLink(stone);
            y += 360f + DS.Space.M;

            var msg = UIKit.LocText(content, "board.stone.message", TextStyle.Body, new Vector2(w - 20f, 190f), _remaining);
            GameUI.PlaceTop(msg.rectTransform, y, new Vector2(w - 20f, 190f));
            GameUI.RiseIn(msg.rectTransform, 0.25f, 24f);
            y += 190f + DS.Space.L;

            float bw = Mathf.Min(w, 620f);
            var breakIt = UIKit.ButtonLoc(content, "board.stone.break", ButtonColor.Green, new Vector2(bw, 160f), OnBreak);
            breakIt.SetAdBadge(true);
            GameUI.PlaceTop((RectTransform)breakIt.transform, y, new Vector2(bw, 160f));
            GameUI.PopInDelayed(breakIt.transform, 0.35f);
            GameUI.PulseAfter(breakIt.transform, 0.9f, 1.05f, 1f);
        }

        void OnBreak()
        {
            if (_done || IsClosing) return;
            _done = true;
            var cb = _onBreak;
            _onBreak = null;
            Close();   // leaves the popup stack synchronously, so the session is no longer halted for the ad
            GameUI.Invoke(cb);
        }
    }
}
