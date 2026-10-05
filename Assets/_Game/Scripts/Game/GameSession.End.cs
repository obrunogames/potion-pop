// ============================================================================================================
// GameSession — the ends of a level: pause (resume / restart / quit, both costing a heart once a pour was made), the No
// Moves popup (Undo / Shuffle / Extra Bottle, continue +1 bottle for coins or once by ad, a stone ad, give up), the loss
// (heart + streak, interstitial chance, Level Failed) and the win (model-time ReportWin, board wave + "Level Complete!"
// once the board settled, Level Complete popup → card → interstitial chance → Next / Home; a new world is presented by
// Home's AreaUnlockedPopup). A catch-all sends the player Home if an end flow ever stalls.
// ============================================================================================================
using System;
using PotionPop.Levels;
using PotionPop.Services;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed partial class GameSession
    {
        const float WinSettleDelay = 0.35f;        // let the last cork / sparkles land before the wave
        const float WinSettleTimeout = 4f;         // no animation progress after the win → reconcile, then celebrate
        const float CelebrationTimeout = 5f;       // PlayWin never called back → continue the win flow

        bool _winPending;
        bool _endFlowBusy;
        bool _celebrating;
        float _endedIdle;
        float _wonAt, _winSettleAt;
        bool _adContinueUsed;
        int _starsOverride;

        void ResetEndState()
        {
            _winPending = false;
            _endFlowBusy = false;
            _celebrating = false;
            _endedIdle = 0f;
            _adContinueUsed = false;
            _starsOverride = 0;
        }

        // ============================================================================================ pause

        /// <summary>Opens the pause popup while playing (no-op otherwise).</summary>
        public bool Pause()
        {
            if (State != SessionState.Playing || PopupManager.AnyOpen || AdsService.IsShowingFullScreen) return false;
            HideIdleHint();
            int token = _token;
            var area = SafeArea(Level);
            PausePopup.Open(Level, Hard, area != null ? area.accent : DS.Colors.Secondary, LevelStarted,
                () => { if (token == _token) OnRestartPressed(); },
                () => { if (token == _token) OnQuitPressed(); });
            return true;
        }

        void OnRestartPressed()
        {
            if (State != SessionState.Playing) return;
            if (!LevelStarted)
            {
                // Nothing poured yet: nothing at stake, start over right away (same pre-boosters, already paid).
                ClosePause();
                Start(new LevelLaunch { level = Level, pre = _pre, replay = Replay });
                return;
            }
            int token = _token;
            ConfirmPopup.Open("game.restart.title", LeaveCostKey(), "ui.restart", "ui.cancel", yes =>
            {
                if (!yes || token != _token) return;
                ClosePause();
                RestartAsLoss();
            }, true);
        }

        void OnQuitPressed()
        {
            if (State != SessionState.Playing) return;
            if (!LevelStarted)
            {
                ClosePause();
                GoHome();
                return;
            }
            int token = _token;
            ConfirmPopup.Open("game.quit.title", LeaveCostKey(), "ui.quit", "ui.cancel", yes =>
            {
                if (!yes || token != _token) return;
                ClosePause();
                Fail();
            }, true);
        }

        /// <summary>What leaving the level really costs (Progress.ReportFail: a heart unless infinite, and the streak).</summary>
        static string LeaveCostKey()
        {
            bool streak = Progress.WinStreak > 0;
            if (Lives.HasInfinite) return streak ? "game.leave.streak" : "game.leave.free";
            return streak ? "game.leave.life_streak" : "game.leave.life";
        }

        static void ClosePause()
        {
            var pause = PopupManager.Get<PausePopup>();
            if (pause != null) pause.Close();
        }

        /// <summary>Restart after pours: counts as a loss (heart, streak), interstitial chance, then the Level Start popup.</summary>
        void RestartAsLoss()
        {
            if (State != SessionState.Playing && State != SessionState.Intro) return;
            int level = Level;
            int token = _token;
            EndAsLoss();
            _endFlowBusy = true;
            AdsService.OnLevelEnded(level, () =>
            {
                if (token != _token) return;
                _endFlowBusy = false;
                OpenLevelStart(level);
            });
        }

        /// <summary>Level Start popup for `level` (replay mode for an already-won level), or Out of Lives first when no
        /// heart is left.</summary>
        static void OpenLevelStart(int level)
        {
            bool replay = level < Progress.CurrentLevel;
            if (Lives.CanPlay)
            {
                LevelStartPopup.Open(level, replay);
                return;
            }
            OutOfLivesPopup.Open(() =>
            {
                if (Lives.CanPlay) LevelStartPopup.Open(level, replay);
            });
        }

        // ============================================================================================ no moves

        /// <summary>No useful pour left (GDD §2 "Stuck"): offer the ways out.</summary>
        void ShowNoMoves()
        {
            if (State != SessionState.Playing || Board == null) return;
            if (PopupManager.Get<NoMovesPopup>() != null) return;
            HideIdleHint();
            HideBoosterHand();
            _v.tutorial?.HideHand();
            SafeView(v => v.Deselect(true));
            var options = new NoMovesPopup.Options
            {
                canUndo = Board.CanUndo,
                canAddBottle = Board.ExtraBottles < Economy.MaxExtraBottles,
                adContinue = !_adContinueUsed,
                stoneBottle = AdsService.RewardedReady ? StoneCandidate() : -1,
            };
            int token = _token;
            NoMovesPopup.Open(options,
                type =>
                {
                    if (token != _token || State != SessionState.Playing) return;
                    // Not applied (e.g. a shuffle that can't help): the check brings the popup back.
                    if (!TryApplyBooster(type)) ScheduleCheck(0.4f);
                },
                viaAd =>
                {
                    if (token != _token || State != SessionState.Playing) return;
                    if (viaAd) _adContinueUsed = true;
                    if (!AddExtraBottle(false)) ScheduleCheck(0.4f);
                },
                bottle =>
                {
                    if (token != _token || State != SessionState.Playing) return;
                    BreakStoneByAd(bottle);
                    // Safety net: if the request was refused up front, the check brings the popup back (while the ad
                    // runs the session is busy, so this check waits for its outcome).
                    ScheduleCheck(0.4f);
                },
                () => { if (token == _token) Fail(); });
        }

        // ============================================================================================ lose

        /// <summary>Give up / quit after pours: the level is lost (heart, streak), interstitial chance, then Level Failed.</summary>
        public void Fail()
        {
            if (State != SessionState.Playing && State != SessionState.Intro) return;
            int level = Level;
            int token = _token;
            int streak = Progress.WinStreak;
            bool infinite = Lives.HasInfinite;
            bool heartAtStake = LevelStarted;
            EndAsLoss();
            _endFlowBusy = true;
            AudioManager.Play(Sfx.Lose);
            Haptics.Play(HapticType.Warning);
            AdsService.OnLevelEnded(level, () =>
            {
                if (token != _token) return;
                LevelFailedPopup.Open(level, heartAtStake ? streak : 0, infinite, heartAtStake && !infinite, Lives.Hearts,
                    () =>
                    {
                        if (token != _token) return;
                        OpenLevelStart(level);
                    },
                    () => { if (token == _token) GoHome(); });
                _endFlowBusy = false;
            });
        }

        void EndAsLoss()
        {
            HideIdleHint();
            HideBoosterHand();
            CancelJobs();
            _tutorialActive = false;
            _v.tutorial?.HideAll();
            _v.boosters?.SetHighlight(null);
            State = SessionState.Lost;
            SetInput(false);
            SafeView(v => v.Deselect(false));
            if (LevelStarted)
            {
                LevelStarted = false;
                Progress.ReportFail(Level);
            }
        }

        // ============================================================================================ win

        /// <summary>The model is won (from a pour, a booster or the debug menu): lock input, report the win right away
        /// (an app killed during the celebration keeps it), prefetch the next board, celebrate once the board settled.</summary>
        void OnWonDetected()
        {
            if (State == SessionState.Won || State == SessionState.Lost || State == SessionState.Idle) return;
            HideIdleHint();
            HideBoosterHand();
            CancelJobs();
            CompleteTutorialOnWin();
            _v.boosters?.SetHighlight(null);
            State = SessionState.Won;
            SetInput(false);
            SafeView(v => v.Deselect(false));

            int moves = Moves;
            int stars = _starsOverride > 0 ? _starsOverride : Definition != null ? Definition.StarsFor(moves) : 3;
            try { _result = Progress.ReportWin(Level, Hard, stars, moves, MaxComboReached, Replay); }
            catch (Exception e)
            {
                Debug.LogException(e);
                _result = new LevelResult { level = Level, hard = Hard, stars = stars, moves = moves, replay = Replay };
            }
            LevelStarted = false;
            try { LevelPrefetch.Prefetch(Progress.CurrentLevel); }   // build the next board while the win popup plays
            catch (Exception e) { Debug.LogException(e); }

            _endFlowBusy = true;
            _winPending = true;
            _wonAt = Time.unscaledTime;
            _winSettleAt = _wonAt + WinSettleDelay;
            _v.hud?.SetMoves(moves, false);
        }

        void TickEnded(float dt, float now)
        {
            if (_winPending && now >= _winSettleAt
                && _busy == 0 && ViewSettled(now, _wonAt, WinSettleTimeout))
            {
                _winPending = false;
                Celebrate();
            }
            EndedCatchAll(dt);
        }

        void Celebrate()
        {
            int token = _token;
            bool celebrated = false;
            _celebrating = true;
            FlushPendingPraise();
            Action afterBoard = () =>
            {
                if (token != _token || celebrated) return;
                celebrated = true;
                AudioManager.Play(Sfx.Win);
                Haptics.Play(HapticType.Success);
                GameFx.BigCelebration(_v.overlay, "game.level_complete", () =>
                {
                    if (token != _token) return;
                    _celebrating = false;
                    var r = _result ?? new LevelResult { level = Level, hard = Hard, stars = 3, replay = Replay };
                    int m3 = Definition != null ? Definition.movesFor3Stars : 0;
                    LevelCompletePopup.Open(r, m3, next => { if (token == _token) AfterWin(next); });
                });
            };
            if (View == null)
            {
                afterBoard();
                return;
            }
            // Safety net: the win flow must go on even if the board never reports the end of its wave.
            var safety = Tween.Delay(CelebrationTimeout, afterBoard);
            if (_owner != null) safety.SetLink(_owner);
            PlayOnView((v, d) => v.PlayWin(d), afterBoard);
        }

        /// <summary>After the complete popup: card reveal → interstitial chance → Next / Home. A new world goes Home
        /// (its AreaUnlockedPopup and backdrop are presented there).</summary>
        void AfterWin(bool next)
        {
            var r = _result ?? new LevelResult { level = Level, replay = Replay };
            int token = _token;
            _endFlowBusy = true;

            Action destination = () =>
            {
                if (token != _token) return;
                _endFlowBusy = false;
                if (next && !r.areaChanged) OpenLevelStart(r.level + 1);
                else GoHome();
            };
            Action ads = () =>
            {
                if (token != _token) return;
                AdsService.OnLevelEnded(r.level, destination);
            };
            if (!string.IsNullOrEmpty(r.cardId)) NewCardPopup.Open(r.cardId, r.cardDuplicate, ads);
            else ads();
        }

        void EndedCatchAll(float dt)
        {
            var sm = ScreenManager.Instance;
            bool idle = !_winPending && !_celebrating && !PopupManager.AnyOpen && !AdsService.IsShowingFullScreen
                        && (sm == null || !sm.IsTransitioning);
            if (!idle)
            {
                _endedIdle = 0f;
                return;
            }
            _endedIdle += dt;
            // A running end flow (popup chain, interstitial) gets a longer grace period; if one of its callbacks never
            // comes back (another module's popup closed without answering) the player still never stays stranded.
            if (_endedIdle < (_endFlowBusy ? EndFlowStallToHome : EndedIdleToHome)) return;
            if (_endFlowBusy) Debug.LogWarning("[Game] end-of-level flow stalled (no popup / callback); going Home");
            _endedIdle = float.MinValue;
            GoHome();
        }

        /// <summary>Leaves the game screen: Home, or the replayed world's level map after a replay.</summary>
        void GoHome()
        {
            var sm = ScreenManager.Instance;
            if (sm == null) return;
            if (Replay && sm.Get(ScreenId.Worlds) != null)
            {
                WorldsScreen.OpenWorld(Areas.AreaNumberForLevel(Level));
                return;
            }
            if (sm.Current != ScreenId.Home) sm.Show(ScreenId.Home);
        }

        // ============================================================================================ debug (editor tools)

        /// <summary>Debug: wins the level right now with the given rating (1..3) through the real win flow.</summary>
        public void DebugWin(int stars = 3)
        {
            if (State != SessionState.Playing) return;
            PopupManager.CloseAll(false);
            _starsOverride = Mathf.Clamp(stars, 1, 3);
            if (!LevelStarted && _result == null)
            {
                LevelStarted = true;
                Progress.ReportLevelStart(Level);
            }
            OnWonDetected();
        }

        /// <summary>Debug: opens the No Moves popup as if the board were stuck.</summary>
        public void DebugNoMoves()
        {
            if (State != SessionState.Playing) return;
            PopupManager.CloseAll(false);
            ShowNoMoves();
        }
    }
}
