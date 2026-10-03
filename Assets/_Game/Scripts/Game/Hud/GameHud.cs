// ============================================================================================================
// In-level HUD (GDD §9 "Game"): level pill in the world accent (purple + skull on hard levels), the moves counter with
// the three star thresholds under it (stars dim one by one as the pours pass movesFor3Stars / movesFor2Stars, with a
// "Max N" caption for the best star rating still reachable; an undo lights them up again) and the pause button.
// No timer and no combo bar: the game is relaxing (GDD §2).
// ============================================================================================================
using System;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public sealed class GameHud : MonoBehaviour
    {
        /// <summary>Total height of the HUD strip (top of the safe area).</summary>
        public const float Height = 200f;

        const float RowWidth = 1032f, RowHeight = 112f, RowTop = 16f;
        const float LevelW = 262f, LevelH = 92f;
        const float MovesWidth = 270f;
        const float StarSize = 50f, StarGap = 6f;
        static readonly Color StarDim = new Color(0.32f, 0.26f, 0.46f, 0.6f);

        public RectTransform Root { get; private set; }
        public RectTransform LevelRect => _levelRoot;
        /// <summary>The moves counter (fly-to target, tutorial pointer).</summary>
        public RectTransform MovesRect => _moves != null ? (RectTransform)_moves.transform : null;
        public RectTransform StarsRect => _starsRow;
        public UIButton PauseButton => _pause;
        /// <summary>Stars the current number of pours would still earn (1..3).</summary>
        public int StarsShown => _lit;

        RectTransform _row;
        RectTransform _levelRoot;
        Image _levelBg, _levelSkull;
        TMP_Text _levelText;
        CounterPill _moves;
        RectTransform _starsRow;
        readonly Image[] _stars = new Image[3];
        TMP_Text _goal;
        CanvasGroup _starsGroup;
        UIButton _pause;

        int _movesFor3 = -1, _movesFor2 = -1;
        int _shownMoves = -1;
        int _lit = 3;

        // ---------------------------------------------------------------------------------------- build

        public static GameHud Create(RectTransform parent, Action onPause)
        {
            var root = UIKit.Rect("Hud", parent);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, Height);
            root.anchoredPosition = Vector2.zero;
            // Own canvas: counters / star pulses rebatch only the HUD, never the bottles. Raycaster: the pause button.
            GameUI.NestCanvas(root.gameObject, true);
            var hud = root.gameObject.AddComponent<GameHud>();
            hud.Root = root;
            hud.Build(onPause);
            return hud;
        }

        void Build(Action onPause)
        {
            _row = UIKit.Rect("Row", Root);
            GameUI.PlaceTop(_row, RowTop, new Vector2(RowWidth, RowHeight));

            // ---- level pill (left)
            _levelRoot = UIKit.Rect("LevelPill", _row);
            UIKit.Place(_levelRoot, new Vector2(0f, 0.5f), new Vector2(LevelW, LevelH), new Vector2(6f, 0f));
            UIKit.Shadow(_levelRoot, 18f, -8f, 0.25f);
            _levelBg = UIKit.Capsule(_levelRoot, new Vector2(LevelW, LevelH), DS.Colors.Secondary);
            UIKit.Stretch(_levelBg.rectTransform);
            var shine = UIKit.Capsule(_levelBg.rectTransform, Vector2.zero, new Color(1f, 1f, 1f, 0.22f));
            UIKit.Stretch(shine.rectTransform, 14f, 8f, 14f, 50f);
            _levelText = UIKit.LocText(_levelRoot, "level.number", TextStyle.H3, new Vector2(240f, 80f), 1);
            UIKit.Stretch(_levelText.rectTransform, 18f, 6f, 18f, 10f);
            _levelSkull = UIKit.Image(_levelRoot, "icon_skull", new Vector2(104f, 104f));
            UIKit.Place(_levelSkull.rectTransform, new Vector2(0f, 0.5f), new Vector2(104f, 104f), new Vector2(-26f, 2f));
            _levelSkull.gameObject.SetActive(false);

            // ---- moves counter (center)
            _moves = UIKit.CounterPill(_row, "icon_pour", MovesWidth, null);
            if (_moves != null)
            {
                UIKit.Place((RectTransform)_moves.transform, new Vector2(0.5f, 0.5f), new Vector2(MovesWidth, CounterPill.DefaultHeight), new Vector2(18f, 0f));
                _moves.SetValue(0, false);
            }

            // ---- pause (right)
            _pause = UIKit.IconButton(_row, "icon_pause", 104f, onPause);
            UIKit.Place((RectTransform)_pause.transform, new Vector2(1f, 0.5f), new Vector2(104f, 104f), Vector2.zero);
            _pause.ExpandHitArea();

            // ---- star thresholds under the moves counter: ★★★ Max 14
            _starsRow = UIKit.Rect("Stars", Root);
            GameUI.PlaceTop(_starsRow, RowTop + RowHeight - 6f, new Vector2(420f, 64f), 18f);
            _starsGroup = _starsRow.gameObject.AddComponent<CanvasGroup>();
            _starsGroup.blocksRaycasts = false;
            var plate = UIKit.Capsule(_starsRow, new Vector2(380f, 60f), DS.WithAlpha(DS.Colors.Ink, 0.45f));
            GameUI.PlaceCenter(plate.rectTransform, Vector2.zero, new Vector2(380f, 60f));
            float x0 = -150f;
            for (int i = 0; i < 3; i++)
            {
                var star = UIKit.Image(_starsRow, "icon_star", new Vector2(StarSize, StarSize));
                GameUI.PlaceCenter(star.rectTransform, new Vector2(x0 + i * (StarSize + StarGap), 2f), new Vector2(StarSize, StarSize));
                _stars[i] = star;
            }
            _goal = UIKit.Text(_starsRow, "", TextStyle.BodyLight, new Vector2(190f, 56f));
            DS.Apply(_goal, TextStyle.BodyLight, 36f);
            _goal.alignment = TextAlignmentOptions.Left;
            GameUI.PlaceCenter(_goal.rectTransform, new Vector2(x0 + 3f * (StarSize + StarGap) + 82f, 0f), new Vector2(190f, 56f));
            SetGoal(-1, -1);
        }

        // ---------------------------------------------------------------------------------------- level

        /// <summary>Level number, hard look (purple + skull) or the world accent color.</summary>
        public void SetLevel(int level, bool hard, Color accent)
        {
            var loc = _levelText != null ? _levelText.GetComponent<LocText>() : null;
            if (loc != null) loc.Set("level.number", level);
            if (_levelBg != null) _levelBg.color = hard ? DS.Colors.Brand : accent;
            if (_levelSkull != null) _levelSkull.gameObject.SetActive(hard);
            if (_levelText != null) UIKit.Stretch(_levelText.rectTransform, hard ? 70f : 18f, 6f, 18f, 10f);
        }

        // ---------------------------------------------------------------------------------------- moves & stars

        /// <summary>Star thresholds of the level (≤ 0 hides the stars row: every win would be 3 stars).</summary>
        public void SetGoal(int movesFor3Stars, int movesFor2Stars)
        {
            _movesFor3 = movesFor3Stars;
            _movesFor2 = Math.Max(movesFor2Stars, movesFor3Stars);
            bool show = movesFor3Stars > 0;
            if (_starsRow != null) _starsRow.gameObject.SetActive(show);
            _lit = 3;
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] == null) continue;
                Tween.Kill(_stars[i].rectTransform);
                Tween.Kill(_stars[i]);
                _stars[i].color = Color.white;
                _stars[i].rectTransform.localScale = Vector3.one;
            }
            RefreshGoalText();
        }

        /// <summary>Pours made. Stars dim (or light up again after an undo) when a threshold is crossed.</summary>
        public void SetMoves(int moves, bool animate)
        {
            moves = Math.Max(0, moves);
            bool changed = moves != _shownMoves;
            _shownMoves = moves;
            if (_moves != null)
            {
                _moves.SetValue(moves, false);
                if (animate && changed && _moves.valueText != null) Tween.Punch(_moves.valueText.transform, 0.2f, 0.25f);
            }
            int lit = _movesFor3 <= 0 ? 3 : moves <= _movesFor3 ? 3 : moves <= _movesFor2 ? 2 : 1;
            if (lit != _lit)
            {
                int old = _lit;
                _lit = lit;
                for (int i = 0; i < _stars.Length; i++)
                {
                    bool on = i < lit;
                    bool wasOn = i < old;
                    if (on != wasOn) SetStar(i, on, animate);
                }
            }
            RefreshGoalText();
        }

        void SetStar(int index, bool on, bool animate)
        {
            var star = _stars[index];
            if (star == null) return;
            var rt = star.rectTransform;
            Tween.Kill(rt);
            Tween.Kill(star);
            rt.localRotation = Quaternion.identity;
            if (!animate)
            {
                star.color = on ? Color.white : StarDim;
                rt.localScale = Vector3.one * (on ? 1f : 0.82f);
                return;
            }
            if (on)
            {
                rt.localScale = Vector3.one * 1.5f;
                Tween.Scale(rt, 1f, 0.35f, Ease.OutBack).SetOvershoot(2f);
                Tween.Color(star, Color.white, 0.2f);
                FX.Sparkles(null, FX.LocalCenterOf(rt), 6);
                AudioManager.Play(Sfx.Star, 0.7f, 1.1f + index * 0.08f);
            }
            else
            {
                // A little "lost it" wobble: shrink, tilt back and gray out.
                Tween.Scale(rt, 0.82f, 0.3f, Ease.OutBack);
                Tween.Color(star, StarDim, 0.3f);
                Tween.Rotate(rt, 14f, 0.12f, Ease.OutQuad).OnComplete(() =>
                {
                    if (rt != null) Tween.Rotate(rt, 0f, 0.35f, Ease.OutElastic);
                });
                FX.Burst(null, FX.LocalCenterOf(rt), "ui_star_small", 5, DS.Colors.Gold, 260f, 0.45f, 22f, -900f);
                AudioManager.Play(Sfx.Swoosh, 0.5f, 0.75f);
            }
        }

        void RefreshGoalText()
        {
            if (_goal == null) return;
            int max = _lit >= 3 ? _movesFor3 : _lit == 2 ? _movesFor2 : -1;
            _goal.text = max > 0 ? Loc.T("game.hud.goal", max) : "";
        }

        // ---------------------------------------------------------------------------------------- motion

        /// <summary>The moves counter bumps (an undo, an extra bottle...).</summary>
        public void BumpMoves()
        {
            if (_moves != null) _moves.Bump();
        }

        /// <summary>Hides the pieces until PopIn (level loading / screen fade: no flash of the old HUD).</summary>
        public void HideForIntro()
        {
            if (_levelRoot != null) { Tween.Kill(_levelRoot); _levelRoot.localScale = Vector3.zero; }
            if (_moves != null) { Tween.Kill(_moves.transform); _moves.transform.localScale = Vector3.zero; }
            if (_pause != null) { Tween.Kill(_pause.transform); _pause.transform.localScale = Vector3.zero; }
            if (_starsGroup != null) { Tween.Kill(_starsGroup); _starsGroup.alpha = 0f; }
        }

        /// <summary>Staggered entrance of the HUD pieces (level start).</summary>
        public void PopIn()
        {
            GameUI.PopInDelayed(_levelRoot, 0.05f);
            if (_moves != null) GameUI.PopInDelayed(_moves.transform, 0.12f);
            if (_pause != null) GameUI.PopInDelayed(_pause.transform, 0.2f);
            if (_starsRow != null && _starsRow.gameObject.activeSelf) GameUI.RiseIn(_starsRow, 0.26f, 20f);
            else if (_starsGroup != null) _starsGroup.alpha = 1f;
        }
    }
}
