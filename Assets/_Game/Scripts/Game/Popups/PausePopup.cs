// ============================================================================================================
// Pause (pause button, Android back, app sent to background): level chip, music / sound / vibration toggles, resume,
// restart and quit. Restart / quit are confirmed by the session when they cost a heart (a pour was made); before the
// first pour they are free and the hint says so. Closing the popup resumes the level.
// ============================================================================================================
using System;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed class PausePopup : Popup
    {
        protected override string TitleKey => "game.pause.title";
        protected override Vector2 PanelSize => new Vector2(880f, 1200f);

        int _level;
        bool _hard;
        Color _accent;
        bool _leavingCosts;
        Action _onRestart, _onQuit;

        /// <summary>Opens the pause menu (returns the open one if any). leavingCosts: restart/quit now cost a heart.</summary>
        public static PausePopup Open(int level, bool hard, Color accent, bool leavingCosts, Action onRestart, Action onQuit)
        {
            var existing = PopupManager.Get<PausePopup>();
            if (existing != null) return existing;
            return PopupManager.Show<PausePopup>(p =>
            {
                p._level = level;
                p._hard = hard;
                p._accent = accent;
                p._leavingCosts = leavingCosts;
                p._onRestart = onRestart;
                p._onQuit = onQuit;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float y = 8f;

            // level chip
            var chip = UIKit.Rect("LevelChip", content);
            GameUI.PlaceTop(chip, y, new Vector2(340f, 76f));
            var chipBg = UIKit.Capsule(chip, new Vector2(340f, 76f), _hard ? DS.Colors.Brand : _accent);
            UIKit.Stretch(chipBg.rectTransform);
            var chipText = UIKit.LocText(chip, "level.number", TextStyle.H3, new Vector2(300f, 70f), _level);
            UIKit.Stretch(chipText.rectTransform, _hard ? 70f : 20f, 4f, 20f, 8f);
            if (_hard)
            {
                var skull = UIKit.Image(chip, "icon_skull", new Vector2(92f, 92f));
                UIKit.Place(skull.rectTransform, new Vector2(0f, 0.5f), new Vector2(92f, 92f), new Vector2(-14f, 2f));
            }
            GameUI.PopInDelayed(chip, 0.1f);
            y += 76f + DS.Space.L;

            // settings rows
            float rowW = Mathf.Min(w, 720f);
            y = ToggleRow(content, y, rowW, "icon_music", "game.pause.music", AudioManager.MusicOn, v =>
            {
                AudioManager.MusicOn = v;
                if (v) AudioManager.PlayMusic(AudioManager.CurrentMusic);
            }, 0.15f);
            y = ToggleRow(content, y, rowW, "icon_sound", "game.pause.sound", AudioManager.SoundOn, v => AudioManager.SoundOn = v, 0.2f);
            y = ToggleRow(content, y, rowW, "icon_vibration", "game.pause.vibration", Haptics.Enabled, v =>
            {
                Haptics.Enabled = v;
                if (v) Haptics.Play(HapticType.Medium);
            }, 0.25f);
            y += DS.Space.M;

            // buttons
            var resume = UIKit.ButtonLoc(content, "game.resume", ButtonColor.Green, new Vector2(rowW, 170f), Close, "icon_play");
            GameUI.PlaceTop((RectTransform)resume.transform, y, new Vector2(rowW, 170f));
            GameUI.PopInDelayed(resume.transform, 0.3f);
            GameUI.PulseAfter(resume.transform, 0.8f, 1.04f, 1.1f);
            y += 170f + DS.Space.M;

            float half = (rowW - DS.Space.M) * 0.5f;
            var restart = UIKit.ButtonLoc(content, "ui.restart", ButtonColor.Orange, new Vector2(half, 140f), OnRestart, "icon_restart");
            GameUI.PlaceTop((RectTransform)restart.transform, y, new Vector2(half, 140f), -(half + DS.Space.M) * 0.5f);
            var quit = UIKit.ButtonLoc(content, "ui.quit", ButtonColor.Red, new Vector2(half, 140f), OnQuit, "icon_home");
            GameUI.PlaceTop((RectTransform)quit.transform, y, new Vector2(half, 140f), (half + DS.Space.M) * 0.5f);
            GameUI.PopInDelayed(restart.transform, 0.36f);
            GameUI.PopInDelayed(quit.transform, 0.42f);

            // what leaving costs right now (nothing to warn about while infinite hearts are active)
            y += 140f + DS.Space.S;
            string hintKey = !_leavingCosts ? "game.pause.free_hint" : Lives.HasInfinite ? null : "game.pause.heart_hint";
            if (hintKey != null)
            {
                var hint = UIKit.LocText(content, hintKey, TextStyle.Small, new Vector2(rowW, 70f));
                GameUI.PlaceTop(hint.rectTransform, y, new Vector2(rowW, 70f));
            }
        }

        static float ToggleRow(RectTransform content, float y, float width, string icon, string key, bool value,
            Action<bool> onChanged, float delay)
        {
            const float h = 118f;
            var row = GameUI.InsetRow(content, new Vector2(width, h));
            GameUI.PlaceTop(row, y, new Vector2(width, h));
            var ic = UIKit.Image(row, icon, new Vector2(78f, 78f));
            UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(78f, 78f), new Vector2(30f, 2f));
            var label = UIKit.LocText(row, key, TextStyle.Body, new Vector2(width - 360f, 80f));
            label.alignment = TMPro.TextAlignmentOptions.Left;
            UIKit.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(width - 360f, 80f), new Vector2(130f, 2f));
            var toggle = UIKit.Toggle(row, value, v => GameUI.Invoke(onChanged, v));
            UIKit.Place((RectTransform)toggle.transform, new Vector2(1f, 0.5f), UIToggle.DefaultSize, new Vector2(-28f, 2f));
            GameUI.RiseIn(row, delay, 40f);
            return y + h + DS.Space.S;
        }

        void OnRestart()
        {
            if (IsClosing) return;
            GameUI.Invoke(_onRestart);
        }

        void OnQuit()
        {
            if (IsClosing) return;
            GameUI.Invoke(_onQuit);
        }
    }
}
