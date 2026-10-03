// ============================================================================================================
// GameSession — tutorials (GDD §9): level 1 hand pointer that taps the source bottle then the target bottle following
// the generator's verified solution (LevelDefinition.solution; the solver takes over if the player deviates), with
// Luna's bubbles for each step; the level 2 rules tip; first hidden-color / first stone tips (with the hand on the
// bottle); and the first-use intro of each in-game booster when it unlocks (BoosterIntroPopup + hand on the tile).
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed partial class GameSession
    {
        public const string TipRules = "tip_rules";
        public const string TipHidden = "tip_hidden";
        public const string TipStones = "tip_stones";

        bool _tutorialActive;
        List<Move> _tutPath;
        int _tutStep;
        int _tutStage;
        int _tutShownSel = -2;
        bool _tutHandOn;
        float _tutResumeAt = -1f;
        bool _boosterHandActive;
        bool _tipHandActive;
        bool _tutorialsPending;

        /// <summary>The level 1 hand tutorial is running.</summary>
        public bool TutorialActive => _tutorialActive;

        void ResetTutorialState()
        {
            _tutorialActive = false;
            _tutPath = null;
            _tutStep = 0;
            _tutStage = 0;
            _tutShownSel = -2;
            _tutHandOn = false;
            _tutResumeAt = -1f;
            _boosterHandActive = false;
            _tipHandActive = false;
            _tutorialsPending = false;
        }

        /// <summary>Right after the intro (and the pre-boosters): the level 1 tutorial, else a booster intro, else a tip.</summary>
        void StartTutorials()
        {
            if (State != SessionState.Playing || Board == null) return;
            if (Level == 1 && !SaveSystem.Data.tutorialDone)
            {
                StartPourTutorial();
                return;
            }
            if (ShowBoosterIntroIfNeeded()) return;
            ShowLevelTip();
        }

        // ============================================================================================ level 1: pour tutorial

        void StartPourTutorial()
        {
            _tutorialActive = true;
            _tutPath = Definition != null && Definition.solution != null && Definition.solution.Count > 0
                ? new List<Move>(Definition.solution) : null;
            _tutStep = 0;
            _tutStage = 0;
            _v.tutorial?.ShowBubble("game.tutorial.tap");
            RefreshTutorialHand();
        }

        /// <summary>Next pour to show: the generator's solution while the player follows it, else the solver's hint.</summary>
        Move? NextTutorialMove()
        {
            if (Board == null) return null;
            if (_tutPath != null && _tutStep < _tutPath.Count)
            {
                var m = _tutPath[_tutStep];
                if (Board.CanPour(m.from, m.to)) return m;
            }
            _tutPath = null;
            try { return Board.Hint(5000); }   // the tutorial board has 3 bottles: instant
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        int SafeSelected()
        {
            var view = View;
            if (view == null) return -1;
            try { return view.Selected; }
            catch (Exception) { return -1; }
        }

        void RefreshTutorialHand()
        {
            if (!_tutorialActive || Board == null || _v.tutorial == null || View == null) return;
            if (State != SessionState.Playing || Board.IsWon) return;
            var next = NextTutorialMove();
            int sel = SafeSelected();
            _tutShownSel = sel;
            if (!next.HasValue)
            {
                _v.tutorial.HideHand();
                _tutHandOn = false;
                return;
            }
            var m = next.Value;
            var view = View;
            if (sel == m.from) _v.tutorial.ShowTap(() => view.BottleWorldPosition(m.to));
            else if (sel >= 0) _v.tutorial.ShowTap(() => view.BottleWorldPosition(sel));   // put the wrong one back first
            else _v.tutorial.ShowSequence(() => view.BottleWorldPosition(m.from), () => view.BottleWorldPosition(m.to));
            _tutHandOn = true;
        }

        void TickTutorial(float dt, float now)
        {
            if (_tutorialsPending && _busy == 0)
            {
                _tutorialsPending = false;
                StartTutorials();
                return;
            }
            if (!_tutorialActive) return;
            bool settled = _busy == 0 && !ViewAnimating();
            if (_tutResumeAt > 0f && now >= _tutResumeAt && settled)
            {
                _tutResumeAt = -1f;
                RefreshTutorialHand();
                return;
            }
            // The player lifted / put down a bottle: retarget the hand.
            if (_tutHandOn && SafeSelected() != _tutShownSel && settled) RefreshTutorialHand();
        }

        void OnTutorialSelect(int bottle)
        {
            if (_tutorialActive) RefreshTutorialHand();
        }

        void OnTutorialPour(PourResult r)
        {
            if (!_tutorialActive || r == null) return;
            if (_tutPath != null && _tutStep < _tutPath.Count && _tutPath[_tutStep].from == r.from && _tutPath[_tutStep].to == r.to)
                _tutStep++;
            else _tutPath = null;
            _v.tutorial?.HideHand();
            _tutHandOn = false;
            if (r.completed && _tutStage < 2)
            {
                _tutStage = 2;
                _v.tutorial?.ShowBubble("game.tutorial.cork");
            }
            else if (_tutStage < 1)
            {
                _tutStage = 1;
                _v.tutorial?.ShowBubble("game.tutorial.same_color");
            }
            // The post-move check (after the pour animation) brings the hand back on the next pour.
        }

        void OnTutorialInvalid()
        {
            if (!_tutorialActive) return;
            _v.tutorial?.HideHand();
            _tutHandOn = false;
            _tutResumeAt = Time.unscaledTime + 0.7f;
        }

        void OnTutorialUndo()
        {
            if (!_tutorialActive) return;
            _tutPath = null;
            _tutResumeAt = Time.unscaledTime + 0.5f;
        }

        void CompleteTutorialOnWin()
        {
            if (_tipHandActive || _boosterHandActive) _v.tutorial?.HideHand();
            _tipHandActive = false;
            _boosterHandActive = false;
            if (!_tutorialActive)
            {
                _v.tutorial?.HideAll();
                return;
            }
            _tutorialActive = false;
            _tutResumeAt = -1f;
            _v.tutorial?.HideAll();
            var d = SaveSystem.Data;
            if (!d.tutorialDone)
            {
                d.tutorialDone = true;
                SaveSystem.MarkDirty();
            }
        }

        // ============================================================================================ tips

        /// <summary>One first-time tip for this level: the rules (level 2), hidden colors, stones.</summary>
        void ShowLevelTip()
        {
            if (State != SessionState.Playing || Board == null || _v.tutorial == null) return;
            if (Level == 2 && !Progress.HasSeenTutorial(TipRules))
            {
                Progress.MarkTutorialSeen(TipRules);
                _v.tutorial.ShowBubble("game.tutorial.rules", 6f);
                return;
            }
            if (Board.AnyHidden && !Progress.HasSeenTutorial(TipHidden))
            {
                Progress.MarkTutorialSeen(TipHidden);
                ShowTipAt(FirstBottle(b => b.Hidden > 0), "game.tutorial.hidden", 6.5f);
                return;
            }
            if (Board.AnyLocked && !Progress.HasSeenTutorial(TipStones))
            {
                Progress.MarkTutorialSeen(TipStones);
                ShowTipAt(FirstBottle(b => b.IsLocked), "game.tutorial.stones", 7f);
            }
        }

        /// <summary>Tip bubble + a few seconds of hand taps on a bottle; the bubble goes to the half of the board area
        /// the bottle is not in, so it never covers it.</summary>
        void ShowTipAt(int bottle, string key, float seconds)
        {
            var tut = _v.tutorial;
            if (tut == null) return;
            bool bottleInTopHalf = false;
            if (bottle >= 0 && _v.boardArea != null)
            {
                Vector3 local = _v.boardArea.InverseTransformPoint(BottleWorld(bottle));
                bottleInTopHalf = local.y > _v.boardArea.rect.center.y;
            }
            if (bottleInTopHalf) tut.ShowBubbleBottom(key, seconds);
            else tut.ShowBubble(key, seconds);
            if (bottle < 0 || View == null) return;
            var view = View;
            _tipHandActive = true;
            tut.ShowTap(() => view.BottleWorldPosition(bottle), Mathf.Min(seconds, 4f));
        }

        int FirstBottle(Func<Bottle, bool> match)
        {
            if (Board == null) return -1;
            for (int i = 0; i < Board.Count; i++)
                if (match(Board[i])) return i;
            return -1;
        }

        // ============================================================================================ booster intros

        /// <summary>First level where an in-game booster is unlocked and never presented: "New booster!" popup, then the
        /// hand taps its tile for a few seconds. Returns true when a popup was opened.</summary>
        bool ShowBoosterIntroIfNeeded()
        {
            if (State != SessionState.Playing) return false;
            var types = Economy.InGameBoosters;
            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (!Economy.IsBoosterUnlocked(t)) continue;
                string id = BoosterIntroPopup.TutorialId(t);
                if (Progress.HasSeenTutorial(id)) continue;
                Progress.MarkTutorialSeen(id);
                int token = _token;
                AudioManager.Play(Sfx.Unlock);
                BoosterIntroPopup.Open(t, () =>
                {
                    if (token != _token || State != SessionState.Playing) return;
                    var bar = _v.boosters;
                    if (bar != null)
                    {
                        bar.Refresh();
                        bar.Bounce(t);
                        // Undo has nothing to take back yet: no hand on a dimmed tile.
                        if (t != BoosterType.Undo && _v.tutorial != null)
                        {
                            _boosterHandActive = true;
                            _v.tutorial.ShowTap(() => bar.TileWorldCenter(t), 5f);
                        }
                    }
                    ShowLevelTip();
                });
                return true;
            }
            return false;
        }

        void HideBoosterHand()
        {
            if (!_boosterHandActive && !_tipHandActive) return;
            _boosterHandActive = false;
            _tipHandActive = false;
            if (!_tutorialActive) _v.tutorial?.HideHand();
        }
    }
}
