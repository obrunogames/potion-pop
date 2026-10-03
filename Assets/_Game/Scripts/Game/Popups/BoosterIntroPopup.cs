// ============================================================================================================
// First time an in-game booster is unlocked: "New booster!" with the booster on a sunburst, its name and description.
// Grants nothing (the player already owns the starting stock); the session then points the hand at the tile.
// ============================================================================================================
using System;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed class BoosterIntroPopup : Popup
    {
        protected override string TitleKey => "game.booster_intro.title";
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(860f, 1080f);

        BoosterType _type;
        Action _onDone;
        bool _done;

        /// <summary>Tutorial id stored with Progress.MarkTutorialSeen for a booster.</summary>
        public static string TutorialId(BoosterType type) => "booster_" + Economy.BoosterId(type);

        public static BoosterIntroPopup Open(BoosterType type, Action onDone)
        {
            var existing = PopupManager.Get<BoosterIntroPopup>();
            if (existing != null) return existing;
            return PopupManager.Show<BoosterIntroPopup>(p =>
            {
                p._type = type;
                p._onDone = onDone;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float y = 0f;

            var hero = UIKit.Rect("Hero", content);
            GameUI.PlaceTop(hero, y, new Vector2(w, 400f));
            var burst = GameUI.Sunburst(hero, 640f, DS.WithAlpha(DS.Colors.Accent, 0.55f), 10f);
            if (burst != null) GameUI.PlaceCenter(burst.rectTransform, Vector2.zero, new Vector2(640f, 640f));
            var glow = GameUI.Glow(hero, 520f, DS.WithAlpha(Color.white, 0.85f));
            if (glow != null) GameUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(520f, 520f));
            var tile = UIKit.Rect("Tile", hero);
            GameUI.PlaceCenter(tile, Vector2.zero, new Vector2(280f, 280f));
            var bg = UIKit.Panel(tile, "btn_square", new Vector2(280f, 280f));
            UIKit.Stretch(bg.rectTransform);
            var icon = UIKit.Image(tile, Economy.BoosterSprite(_type), new Vector2(210f, 210f));
            GameUI.PlaceCenter(icon.rectTransform, new Vector2(0f, 8f), new Vector2(210f, 210f));
            GameUI.PopInDelayed(tile, 0.2f, Sfx.Reward);
            Tween.Bob(icon.rectTransform, 10f, 1.6f);
            Tween.Delay(0.5f, () =>
            {
                if (tile == null) return;
                Vector2 c = FX.LocalCenterOf(tile);
                FX.Sparkles(null, c, 14);
                FX.Ring(null, c, DS.Colors.Accent, 520f);
            }).SetLink(tile);
            y += 400f + DS.Space.M;

            var name = UIKit.LocText(content, Economy.BoosterNameKey(_type), TextStyle.H2, new Vector2(w, 90f));
            GameUI.PlaceTop(name.rectTransform, y, new Vector2(w, 90f));
            GameUI.PopInDelayed(name.transform, 0.35f);
            y += 90f + DS.Space.S;

            var desc = UIKit.LocText(content, Economy.BoosterDescKey(_type), TextStyle.Body, new Vector2(w - 40f, 150f));
            GameUI.PlaceTop(desc.rectTransform, y, new Vector2(w - 40f, 150f));
            GameUI.RiseIn(desc.rectTransform, 0.45f, 30f);
            y += 150f + DS.Space.L;

            var ok = UIKit.ButtonLoc(content, "game.booster_intro.ok", ButtonColor.Green, new Vector2(Mathf.Min(w, 560f), 160f), Done);
            GameUI.PlaceTop((RectTransform)ok.transform, y, new Vector2(Mathf.Min(w, 560f), 160f));
            GameUI.PopInDelayed(ok.transform, 0.6f);
            GameUI.PulseAfter(ok.transform, 1.1f, 1.05f, 1f);
        }

        public override void OnBack() => Done();
        protected override void OnOverlayTap() { }

        void Done()
        {
            if (_done || IsClosing) return;
            _done = true;
            var cb = _onDone;
            _onDone = null;
            Close();
            GameUI.Invoke(cb);
        }
    }
}
