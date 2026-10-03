// ============================================================================================================
// Level complete (GDD §2/§6/§9): three big stars on a sunburst that fill one by one (rising chime, sparkles and a ring per
// earned star, a gold burst for a perfect 3), "New best!" on an improved replay, the moves line (+ the 3-star goal when
// missed), coins with a rewarded "x2", the win streak and the star-chest progress. Next / Home hand the rest of the flow
// (card reveal, interstitial chance, destination) back to the session.
// Rewards are already granted by Progress.ReportWin; this popup only shows them (plus the optional ad bonus).
// ============================================================================================================
using System;
using PotionPop.Services;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public sealed class LevelCompletePopup : Popup
    {
        protected override string TitleKey => "game.complete.title";
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(900f, ContentHeight() + 150f);

        const float HeroH = 340f;
        static readonly Color SlotColor = new Color(0.36f, 0.3f, 0.5f, 0.38f);
        static readonly Vector2[] StarPos = { new Vector2(-245f, -12f), new Vector2(0f, 30f), new Vector2(245f, -12f) };
        static readonly float[] StarSize = { 168f, 210f, 168f };
        static readonly float[] StarTilt = { 14f, 0f, -14f };

        LevelResult _result;
        int _movesFor3;
        Action<bool> _onDone;   // true = Next, false = Home
        bool _done, _doubled, _busy;
        UIButton _doubleButton;
        TMP_Text _coinsText;
        Image _coinIcon;
        int _coinsShown;

        bool ShowCoins => _result.coins > 0;
        bool ShowStreak => !_result.replay && _result.winStreak > 0;
        bool ShowGoal => _result.stars < 3 && _movesFor3 > 0;
        bool NewBest => _result.replay && _result.starsGained > 0 && _result.previousBest > 0;

        /// <summary>movesFor3Stars: the level's 3-star goal (shown when it was missed). onDone(true) = Next, onDone(false) =
        /// Home (after the popup closed).</summary>
        public static LevelCompletePopup Open(LevelResult result, int movesFor3Stars, Action<bool> onDone)
        {
            var existing = PopupManager.Get<LevelCompletePopup>();
            if (existing != null) return existing;
            return PopupManager.Show<LevelCompletePopup>(p =>
            {
                p._result = result ?? new LevelResult { stars = 3 };
                p._movesFor3 = movesFor3Stars;
                p._onDone = onDone;
            });
        }

        float ContentHeight()
        {
            if (_result == null) return 1100f;
            float h = HeroH + DS.Space.S + 60f;
            if (ShowGoal) h += 52f;
            h += DS.Space.M;
            if (ShowCoins) h += 130f + DS.Space.S;
            if (ShowStreak) h += 90f + DS.Space.S;
            h += 110f + DS.Space.L;
            h += 160f;
            return h;
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float rowW = Mathf.Min(w, 740f);
            float y = 0f;
            int stars = Mathf.Clamp(_result.stars, 1, 3);
            float starsDone = 0.5f + stars * 0.38f + 0.3f;   // when the last earned star has landed

            // ---- stars on a sunburst
            var hero = UIKit.Rect("Hero", content);
            GameUI.PlaceTop(hero, y, new Vector2(w, HeroH));
            var burst = GameUI.Sunburst(hero, 660f, DS.WithAlpha(DS.Colors.Accent, 0.5f), 12f);
            if (burst != null) GameUI.PlaceCenter(burst.rectTransform, new Vector2(0f, 0f), new Vector2(660f, 660f));
            var glow = GameUI.Glow(hero, 540f, DS.WithAlpha(Color.white, 0.8f));
            if (glow != null) GameUI.PlaceCenter(glow.rectTransform, new Vector2(0f, 0f), new Vector2(540f, 540f));
            for (int i = 0; i < 3; i++) BuildStar(hero, i, i < stars);
            if (stars >= 3)
            {
                Tween.Delay(starsDone, () =>
                {
                    if (hero == null) return;
                    Vector2 c = FX.LocalCenterOf(hero);
                    FX.Burst(null, c, "ui_star_small", 28, DS.Colors.Gold, 1000f, 0.9f, 46f, -1300f);
                    FX.Ring(null, c, DS.Colors.Accent, 760f);
                    AudioManager.Play(Sfx.Fanfare, 0.8f);
                }).SetLink(hero);
            }
            else Tween.Delay(starsDone, () => AudioManager.Play(Sfx.Reward, 0.7f)).SetLink(hero);

            if (NewBest)
            {
                var tag = MetaUI.Tag(hero, "game.complete.new_best", DS.Colors.Pink, "icon_crown", new Vector2(330f, 74f), 38f);
                UIKit.Place(tag, new Vector2(0.5f, 0f), new Vector2(330f, 74f), new Vector2(0f, 4f));
                tag.pivot = new Vector2(0.5f, 0.5f);
                tag.anchoredPosition = new Vector2(0f, 41f);
                GameUI.PopInDelayed(tag, starsDone + 0.1f, Sfx.Pop);
                Tween.Delay(starsDone + 0.6f, () => { if (tag != null) Tween.Scale(tag, 1.06f, 0.55f, Ease.InOutSine).SetLoops(-1, true); }).SetLink(tag);
            }
            y += HeroH + DS.Space.S;

            // ---- moves (+ the 3-star goal when it was missed)
            var movesLine = UIKit.LocText(content, "game.complete.moves", TextStyle.Body, new Vector2(w, 60f), _result.moves);
            DS.Apply(movesLine, TextStyle.Body, 44f);
            GameUI.PlaceTop(movesLine.rectTransform, y, new Vector2(w, 60f));
            GameUI.RiseIn(movesLine.rectTransform, 0.35f, 24f);
            y += 60f;
            if (ShowGoal)
            {
                var goal = UIKit.LocText(content, "game.complete.goal3", TextStyle.Small, new Vector2(w, 52f), _movesFor3);
                GameUI.PlaceTop(goal.rectTransform, y, new Vector2(w, 52f));
                GameUI.RiseIn(goal.rectTransform, 0.42f, 20f);
                y += 52f;
            }
            y += DS.Space.M;

            // ---- coins (+ x2 ad)
            if (ShowCoins)
            {
                var coinsRow = GameUI.InsetRow(content, new Vector2(rowW, 130f));
                GameUI.PlaceTop(coinsRow, y, new Vector2(rowW, 130f));
                _coinIcon = UIKit.Image(coinsRow, "icon_coin", new Vector2(96f, 96f));
                UIKit.Place(_coinIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(96f, 96f), new Vector2(36f, 4f));
                _coinsText = UIKit.Text(coinsRow, "+0", TextStyle.H1, new Vector2(240f, 110f), TextAlignmentOptions.Left);
                DS.Apply(_coinsText, TextStyle.H1, 76f);
                _coinsText.color = DS.Colors.Accent;
                _coinsText.alignment = TextAlignmentOptions.Left;
                UIKit.Place(_coinsText.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, 110f), new Vector2(150f, 4f));
                _doubleButton = UIKit.ButtonLoc(coinsRow, "game.complete.double", ButtonColor.Purple, new Vector2(250f, 110f), OnDouble);
                _doubleButton.SetAdBadge(true);
                UIKit.Place((RectTransform)_doubleButton.transform, new Vector2(1f, 0.5f), new Vector2(250f, 110f), new Vector2(-18f, 4f));
                GameUI.RiseIn(coinsRow, 0.55f);
                GameUI.PopInDelayed(_doubleButton.transform, starsDone + 0.5f, Sfx.Pop);
                GameUI.PulseAfter(_doubleButton.transform, starsDone + 1f, 1.07f, 0.9f);
                CountUp(_coinsText, _result.coins, starsDone, 0.7f, Sfx.Coin);
                _coinsShown = _result.coins;
                y += 130f + DS.Space.S;
            }

            // ---- win streak
            if (ShowStreak)
            {
                var streakRow = UIKit.Rect("Streak", content);
                GameUI.PlaceTop(streakRow, y, new Vector2(rowW, 90f));
                var flame = UIKit.Image(streakRow, "icon_flame", new Vector2(74f, 90f));
                var label = UIKit.LocText(streakRow, "game.complete.streak", TextStyle.Body, new Vector2(rowW - 120f, 80f), _result.winStreak);
                float tw = Mathf.Clamp(label.preferredWidth + 8f, 120f, rowW - 120f);
                float groupW = 74f + DS.Space.S + tw;
                UIKit.Place(flame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(74f, 90f), new Vector2(-groupW * 0.5f + 37f, 0f));
                UIKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(tw, 80f), new Vector2(-groupW * 0.5f + 74f + DS.Space.S + tw * 0.5f, 0f));
                GameUI.PopInDelayed(streakRow, starsDone + 0.2f, Sfx.Pop);
                Tween.Scale(flame.rectTransform, 1.12f, 0.35f, Ease.InOutSine).SetLoops(-1, true).SetDelay(starsDone + 0.6f);
                y += 90f + DS.Space.S;
            }

            // ---- star chest progress
            var chestRow = UIKit.Rect("Chest", content);
            GameUI.PlaceTop(chestRow, y, new Vector2(rowW, 110f));
            var chest = UIKit.Image(chestRow, StarChest.CanOpen ? "chest_open" : "chest_closed", new Vector2(120f, 107f));
            UIKit.Place(chest.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 107f), new Vector2(0f, 4f));
            var bar = UIKit.ProgressBar(chestRow, new Vector2(rowW - 150f, 58f));
            UIKit.Place((RectTransform)bar.transform, new Vector2(1f, 0.5f), new Vector2(rowW - 150f, 58f), new Vector2(-6f, 0f));
            float after = StarChest.Progress01;
            float before = Mathf.Clamp01((StarChest.Progress - _result.starsGained) / (float)Mathf.Max(1, StarChest.Goal));
            if (StarChest.CanOpen) before = Mathf.Min(before, after);
            bar.SetValue(before, false);
            bar.SetFillColor(DS.Colors.Accent);
            bar.SetLabel(StarChest.CanOpen ? Loc.T("game.complete.chest_ready")
                : Loc.T("game.complete.chest_progress", Loc.Number(StarChest.Progress), Loc.Number(StarChest.Goal)));
            GameUI.RiseIn(chestRow, 0.7f, 30f);
            Tween.Delay(starsDone + 0.3f, () =>
            {
                if (bar == null) return;
                bar.SetValue(after, true);
                if (StarChest.CanOpen && chest != null)
                {
                    Tween.Punch(chest.rectTransform, 0.3f, 0.5f);
                    FX.Sparkles(null, FX.LocalCenterOf(chest.rectTransform), 10);
                    Tween.Rotate(chest.rectTransform, 6f, 0.25f, Ease.InOutSine).SetLoops(-1, true).SetDelay(0.5f);
                }
            }).SetLink(chestRow);
            y += 110f + DS.Space.L;

            // ---- buttons
            float homeW = 280f, nextW = Mathf.Min(rowW - homeW - DS.Space.M, 440f);
            float total = homeW + DS.Space.M + nextW;
            var home = UIKit.ButtonLoc(content, "game.home", ButtonColor.Blue, new Vector2(homeW, 160f), () => Done(false), "icon_home");
            GameUI.PlaceTop((RectTransform)home.transform, y, new Vector2(homeW, 160f), -total * 0.5f + homeW * 0.5f);
            var next = UIKit.ButtonLoc(content, "ui.next", ButtonColor.Green, new Vector2(nextW, 160f), () => Done(true), "icon_play");
            GameUI.PlaceTop((RectTransform)next.transform, y, new Vector2(nextW, 160f), total * 0.5f - nextW * 0.5f);
            GameUI.PopInDelayed(home.transform, starsDone * 0.6f + 0.5f);
            GameUI.PopInDelayed(next.transform, starsDone * 0.6f + 0.6f);
            GameUI.PulseAfter(next.transform, starsDone + 0.9f, 1.05f, 1f);
        }

        void BuildStar(RectTransform hero, int i, bool earned)
        {
            float size = StarSize[i];
            var slot = UIKit.Rect("Star" + i, hero);
            GameUI.PlaceCenter(slot, StarPos[i], new Vector2(size, size));
            slot.localRotation = Quaternion.Euler(0f, 0f, StarTilt[i]);
            var empty = UIKit.Image(slot, "icon_star", new Vector2(size, size));
            UIKit.Stretch(empty.rectTransform);
            empty.color = SlotColor;
            GameUI.PopInDelayed(slot, 0.12f + i * 0.06f);
            if (!earned) return;

            var star = UIKit.Image(slot, "icon_star", new Vector2(size, size));
            UIKit.Stretch(star.rectTransform);
            var rt = star.rectTransform;
            rt.localScale = Vector3.zero;
            float delay = 0.5f + i * 0.38f;
            Tween.Scale(rt, 1f, 0.42f, Ease.OutBack).SetOvershoot(2.4f).SetDelay(delay);
            Tween.Delay(delay + 0.14f, () =>
            {
                if (rt == null) return;
                Vector2 c = FX.LocalCenterOf(rt);
                FX.Sparkles(null, c, 12);
                FX.Ring(null, c, DS.WithAlpha(DS.Colors.Accent, 0.9f), size * 2.2f);
                AudioManager.Play(Sfx.Star, 0.9f, 1f + i * 0.12f);
                Haptics.Play(i >= 2 ? HapticType.Medium : HapticType.Light);
                Tween.Punch(slot, 0.12f, 0.3f);
            }).SetLink(rt);
            // idle twinkle once filled
            Tween.Delay(delay + 0.9f, () =>
            {
                if (slot != null) Tween.Rotate(slot, StarTilt[i] + (i == 1 ? 4f : -StarTilt[i] * 0.4f), 1.2f + i * 0.2f, Ease.InOutSine).SetLoops(-1, true);
            }).SetLink(rt);
        }

        void CountUp(TMP_Text text, int target, float delay, float duration, Sfx tick)
        {
            if (text == null) return;
            text.text = "+0";
            if (target <= 0) return;
            int last = -1;
            Tween.Value(0f, target, duration, v =>
            {
                if (text == null) return;
                int n = Mathf.RoundToInt(v);
                if (n == last) return;
                last = n;
                text.text = "+" + Loc.Number(n);
            }, Ease.OutCubic).SetDelay(delay).SetLink(text).OnComplete(() =>
            {
                if (text == null) return;
                Tween.Punch(text.transform, 0.25f, 0.3f);
                AudioManager.Play(tick, 0.8f, 1.2f);
            });
        }

        void OnDouble()
        {
            if (_doubled || _busy || _done || IsClosing) return;
            _busy = true;
            if (_doubleButton != null) _doubleButton.Interactable = false;
            AdsService.ShowRewarded(AdPlacement.DoubleCoins, earned =>
            {
                // A watched ad always pays, even if the popup was closed meanwhile (grant first, then animate).
                int extra = Mathf.Max(0, _result != null ? _result.coins : 0);
                if (earned && !_doubled)
                {
                    _doubled = true;
                    Economy.AddCoins(extra, "double_coins");
                }
                if (this == null || IsClosing) return;
                _busy = false;
                if (!earned)
                {
                    if (_doubleButton != null) _doubleButton.Interactable = true;
                    return;
                }
                AudioManager.Play(Sfx.Reward);
                Haptics.Play(HapticType.Success);
                Vector3 from = _doubleButton != null ? _doubleButton.transform.position : transform.position;
                if (_doubleButton != null)
                {
                    var b = _doubleButton.transform;
                    Tween.Kill(b);
                    Tween.Scale(b, 0f, 0.25f, Ease.InBack);
                }
                int start = _coinsShown;
                _coinsShown += extra;
                UIKit.FlyRewards("icon_coin", Mathf.Clamp(extra / 4, 6, 14), from, _coinIcon != null ? _coinIcon.rectTransform : null, null, () =>
                {
                    if (_coinsText == null) return;
                    _coinsText.text = "+" + Loc.Number(_coinsShown);
                    Tween.Punch(_coinsText.transform, 0.35f, 0.35f);
                    FX.Sparkles(null, FX.LocalCenterOf(_coinsText.rectTransform), 10);
                });
                if (_coinsText != null)
                {
                    var text = _coinsText;
                    Tween.Value(start, _coinsShown, 0.9f, v =>
                    {
                        if (text != null) text.text = "+" + Loc.Number(Mathf.RoundToInt(v));
                    }, Ease.OutCubic).SetDelay(0.3f).SetLink(text);
                }
            });
        }

        protected override void OnOverlayTap() { }
        public override void OnBack() => Done(false);

        void Done(bool next)
        {
            if (_done || _busy || IsClosing) return;
            _done = true;
            var cb = _onDone;
            _onDone = null;
            Close();
            GameUI.Invoke(cb, next);
        }
    }
}
