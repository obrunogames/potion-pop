// ============================================================================================================
// Level failed (GDD §6): Luna sad, the lost heart breaking away (or the infinite-hearts note), the hearts row, a warning
// when a win streak was lost, Retry (→ Level Start popup, Out of Lives first when no heart is left) and Home.
// The fail itself (Progress.ReportFail + AdsService.OnLevelEnded) is reported by the session before opening this.
// ============================================================================================================
using System;
using PotionPop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public sealed class LevelFailedPopup : Popup
    {
        protected override string TitleKey => "game.failed.title";
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(900f, _streakLost > 0 || _infinite ? 1160f : 1020f);

        int _level;
        int _streakLost;
        bool _infinite;
        bool _heartLost;
        int _heartsLeft;
        Action _onRetry, _onHome;
        bool _done;
        Image[] _rowHearts;

        /// <summary>streakLost: the streak that just ended (0 = none). heartLost: a heart was taken by this loss.</summary>
        public static LevelFailedPopup Open(int level, int streakLost, bool infiniteHearts, bool heartLost, int heartsLeft,
            Action onRetry, Action onHome)
        {
            var existing = PopupManager.Get<LevelFailedPopup>();
            if (existing != null) return existing;
            return PopupManager.Show<LevelFailedPopup>(p =>
            {
                p._level = level;
                p._streakLost = streakLost;
                p._infinite = infiniteHearts;
                p._heartLost = heartLost && !infiniteHearts;
                p._heartsLeft = heartsLeft;
                p._onRetry = onRetry;
                p._onHome = onHome;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float btnW = Mathf.Min(w, 720f);
            float y = 0f;

            var chip = UIKit.LocText(content, "level.number", TextStyle.Small, new Vector2(w, 50f), _level);
            GameUI.PlaceTop(chip.rectTransform, y, new Vector2(w, 50f));
            y += 50f + DS.Space.XS;

            GameUI.Mascot(content, "mascot_sad", new Vector2(250f, 348f), new Vector2(-170f, -y));
            BuildHeart(content, y);
            y += 348f + DS.Space.M;

            BuildHeartsRow(content, y);
            y += 80f + DS.Space.M;

            if (_streakLost > 0 || _infinite)
            {
                var row = GameUI.InsetRow(content, new Vector2(btnW, 116f));
                GameUI.PlaceTop(row, y, new Vector2(btnW, 116f));
                var icon = UIKit.Image(row, _infinite ? "icon_heart" : "icon_flame", new Vector2(80f, 80f));
                UIKit.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(80f, 80f), new Vector2(28f, 2f));
                if (!_infinite) icon.color = new Color(0.75f, 0.72f, 0.8f, 1f);
                var text = _infinite
                    ? UIKit.LocText(row, "game.failed.infinite", TextStyle.Body, new Vector2(btnW - 150f, 100f))
                    : UIKit.LocText(row, "game.failed.streak_lost", TextStyle.Body, new Vector2(btnW - 150f, 100f), _streakLost);
                DS.Apply(text, TextStyle.Body, 36f);
                if (!_infinite) text.color = DS.Colors.Danger;
                text.alignment = TMPro.TextAlignmentOptions.Left;
                UIKit.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(btnW - 150f, 100f), new Vector2(124f, 2f));
                GameUI.RiseIn(row, 1.3f, 30f);
                if (!_infinite) Tween.Delay(1.5f, () => { if (row != null) Tween.Shake(row, 10f, 0.35f); }).SetLink(row);
                y += 116f + DS.Space.M;
            }

            var retry = UIKit.ButtonLoc(content, "ui.retry", ButtonColor.Green, new Vector2(btnW, 170f), OnRetry, "icon_restart");
            GameUI.PlaceTop((RectTransform)retry.transform, y, new Vector2(btnW, 170f));
            GameUI.PopInDelayed(retry.transform, 0.5f);
            GameUI.PulseAfter(retry.transform, 1.2f, 1.04f, 1f);
            y += 170f + DS.Space.M;

            var home = UIKit.ButtonLoc(content, "game.home", ButtonColor.Blue, new Vector2(btnW, 140f), OnHome, "icon_home");
            GameUI.PlaceTop((RectTransform)home.transform, y, new Vector2(btnW, 140f));
            GameUI.PopInDelayed(home.transform, 0.58f);
        }

        void BuildHeart(RectTransform content, float y)
        {
            var holder = UIKit.Rect("Heart", content);
            GameUI.PlaceTop(holder, y + 30f, new Vector2(300f, 300f), 170f);
            var glow = GameUI.Glow(holder, 380f, DS.WithAlpha(_heartLost ? DS.Colors.Danger : DS.Colors.Accent, 0.45f));
            GameUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(380f, 380f));
            var heartRt = UIKit.Rect("Icon", holder);
            GameUI.PlaceCenter(heartRt, Vector2.zero, new Vector2(230f, 190f));
            var heart = UIKit.Image(heartRt, "icon_heart", new Vector2(230f, 190f));
            UIKit.Stretch(heart.rectTransform);
            GameUI.PopInDelayed(holder, 0.25f, Sfx.Pop);

            if (!_heartLost)
            {
                // Infinite hearts (or nothing was at stake): the heart stays, pulsing.
                Tween.Pulse(heartRt, 1.08f, 0.9f);
                Tween.Delay(0.6f, () => { if (holder != null) FX.Sparkles(null, FX.LocalCenterOf(holder), 12); }).SetLink(holder);
                return;
            }

            // shake, then break away with "-1"
            Tween.Delay(0.8f, () => { if (heartRt != null) Tween.Shake(heartRt, 14f, 0.4f); }).SetLink(holder);
            Tween.Delay(1.25f, () =>
            {
                if (heartRt == null) return;
                Vector2 c = FX.LocalCenterOf(heartRt);
                AudioManager.Play(Sfx.Heart);
                Haptics.Play(HapticType.Warning);
                FX.Burst(null, c, "fx_poof", 6, null, 300f, 0.6f, 120f, 0f);
                FX.Burst(null, c, "icon_heart", 8, null, 700f, 0.8f, 46f, -1600f);
                FX.FloatingText(null, c + new Vector2(0f, 40f), Loc.T("game.failed.minus_heart"), TextStyle.Display, DS.Colors.Danger);
                Tween.Rotate(heartRt, -24f, 0.6f, Ease.OutQuad);
                Tween.Move(heartRt, new Vector2(30f, -170f), 0.6f, Ease.InQuad);
                Tween.Scale(heartRt, 0.6f, 0.6f, Ease.InQuad);
                Tween.Fade(heart, 0f, 0.55f).SetDelay(0.05f);
                if (glow != null) Tween.Fade(glow, 0f, 0.4f);
                LoseRowHeart();
            }).SetLink(holder);
        }

        void BuildHeartsRow(RectTransform content, float y)
        {
            int max = Lives.Max;
            const float s = 70f, gap = 14f;
            var row = UIKit.Rect("HeartsRow", content);
            float rw = max * s + (max - 1) * gap;
            GameUI.PlaceTop(row, y, new Vector2(rw, 80f));
            _rowHearts = new Image[max];
            // before the animation the lost heart is still drawn full
            int shownFull = _infinite ? max : Mathf.Min(max, _heartsLeft + (_heartLost ? 1 : 0));
            for (int i = 0; i < max; i++)
            {
                var h = UIKit.Image(row, "icon_heart", new Vector2(s, s * 0.82f));
                UIKit.Place(h.rectTransform, new Vector2(0f, 0.5f), new Vector2(s, s * 0.82f), new Vector2(i * (s + gap), 0f));
                h.color = i < shownFull ? Color.white : new Color(0.55f, 0.5f, 0.62f, 0.55f);
                _rowHearts[i] = h;
                GameUI.PopInDelayed(h.transform, 0.35f + i * DS.Motion.Stagger);
            }
        }

        void LoseRowHeart()
        {
            if (_rowHearts == null || _heartsLeft < 0 || _heartsLeft >= _rowHearts.Length) return;
            var h = _rowHearts[_heartsLeft];
            if (h == null) return;
            Tween.Color(h, new Color(0.55f, 0.5f, 0.62f, 0.55f), 0.3f);
            Tween.Punch(h.transform, 0.35f, 0.35f);
        }

        /// <summary>No accidental dismiss by tapping outside: the player picks Retry or Home.</summary>
        protected override void OnOverlayTap() { }
        public override void OnBack() => OnHome();

        void OnRetry()
        {
            if (_done || IsClosing) return;
            _done = true;
            var cb = _onRetry;
            Close();
            GameUI.Invoke(cb);
        }

        void OnHome()
        {
            if (_done || IsClosing) return;
            _done = true;
            var cb = _onHome;
            Close();
            GameUI.Invoke(cb);
        }
    }
}
