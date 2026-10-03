// ============================================================================================================
// GameSession — boosters (GDD §5) and stones (GDD §2).
//  * In-game (booster bar): Undo (takes back the last pour), Shuffle (BoardState.Shuffle, verified solvable; "won't
//    help" when no arrangement works), Extra Bottle (≤ Economy.MaxExtraBottles per level, continues included) and
//    Magic Wand (removes the best color that keeps the board solvable; its orbs fly to the wand tile).
//    Inventory is consumed only once the booster actually applied; every use is reported (quests).
//  * Pre-level (Level Start popup, already paid): Crystal Ball (reveal every "?") and Rainbow Potion (remove one color),
//    applied right after the intro.
//  * Stones: tapping a stone offers a rewarded ad that breaks it at once.
// Color picks (wand / rainbow) run the solver on a worker thread while the board is locked.
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.Services;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed partial class GameSession
    {
        /// <summary>Solver budget per candidate color of the wand / rainbow solvability check.</summary>
        public const int WandSolverNodes = 20000;
        /// <summary>Candidate colors (best first) checked before falling back to the best one.</summary>
        public const int WandCandidatesChecked = 4;
        public const int ShuffleAttempts = 16;
        public const int ShuffleSolverNodes = 10000;
        const float ColorJobTimeout = 4f;

        WorkerJob<int> _colorJob;
        Action<int> _colorJobDone;
        int _colorJobFallback;

        // ============================================================================================ booster bar

        /// <summary>Booster tile tapped (BoosterBar).</summary>
        public void OnBoosterTapped(BoosterType type)
        {
            if (State != SessionState.Playing || Board == null || Halted) return;
            HideBoosterHand();
            if (!Economy.IsBoosterUnlocked(type))
            {
                Refuse(type, null);
                UIKit.Toast(Loc.T("booster.locked", Economy.BoosterUnlockLevel(type)));
                return;
            }
            // A pour / booster is still animating: wait (soft "not now" wiggle so the tap never feels dead).
            if (_busy > 0 || ViewAnimating())
            {
                _v.boosters?.Shake(type);
                Haptics.Play(HapticType.Light);
                return;
            }
            string why = WhyNotApplicable(type);
            if (why != null)
            {
                Refuse(type, why);
                return;
            }
            if (Economy.GetBooster(type) <= 0)
            {
                OpenBuy(type);
                return;
            }
            TryApplyBooster(type);
        }

        /// <summary>Loc key explaining why a booster can't do anything right now (null = it can).</summary>
        string WhyNotApplicable(BoosterType type)
        {
            if (Board == null) return "ui.error";
            switch (type)
            {
                case BoosterType.Undo: return Board.CanUndo ? null : "game.nothing_to_undo";
                case BoosterType.Bottle: return Board.ExtraBottles < Economy.MaxExtraBottles ? null : "game.bottle_max";
                case BoosterType.Wand: return Board.WandCandidates().Count > 0 ? null : "game.nothing_to_remove";
                default: return null;
            }
        }

        /// <summary>Applies an owned booster now (bar or No Moves popup). False when it could not apply (nothing is
        /// consumed then).</summary>
        bool TryApplyBooster(BoosterType type)
        {
            if (State != SessionState.Playing || Board == null || _busy > 0) return false;
            if (Economy.GetBooster(type) <= 0)
            {
                OpenBuy(type);
                return false;
            }
            SafeView(v => v.Deselect(true));
            HideIdleHint();
            try
            {
                switch (type)
                {
                    case BoosterType.Undo: return UseUndo();
                    case BoosterType.Shuffle: return UseShuffle();
                    case BoosterType.Bottle: return AddExtraBottle(true);
                    case BoosterType.Wand: return UseWand();
                    default: return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ScheduleCheck(CheckDelay);
                return false;
            }
        }

        bool UseUndo()
        {
            var r = Board.Undo();
            if (r == null)
            {
                Refuse(BoosterType.Undo, "game.nothing_to_undo");
                return false;
            }
            Consume(BoosterType.Undo);
            UsedBooster(BoosterType.Undo);
            // An undo breaks the chain: the next completion starts a new combo.
            Combo = 0;
            _lastCompletionPour = -100;
            BumpVersion();
            _inDeadEnd = false;
            _v.hud?.SetMoves(Board.Moves, true);
            _v.hud?.BumpMoves();
            OnTutorialUndo();
            BusyBegin();
            PlayOnView((v, d) => v.PlayUndo(r, d), BusyCallback(() => ScheduleCheck(CheckDelay)));
            RefreshBoosterStates();
            return true;
        }

        bool UseShuffle()
        {
            var before = CompletedMask();
            ShuffleResult r = null;
            try { r = Board.Shuffle(_rng, ShuffleAttempts, ShuffleSolverNodes); }
            catch (Exception e) { Debug.LogException(e); }
            if (r == null)
            {
                Refuse(BoosterType.Shuffle, "game.shuffle_no_help");
                return false;
            }
            // The board changed: the booster must be paid now (the count was checked by the caller).
            Consume(BoosterType.Shuffle);
            UsedBooster(BoosterType.Shuffle);
            ReportBoosterCompletions(NewCompletions(before));
            BumpVersion();
            _inDeadEnd = false;
            BusyBegin();
            PlayOnView((v, d) => v.PlayShuffle(r, d), BusyCallback(() =>
            {
                CelebrateBoosterCompletions();
                ScheduleCheck(CheckDelay);
            }));
            RefreshBoosterStates();
            if (r.won || Board.IsWon) OnWonDetected();
            return true;
        }

        /// <summary>Adds an empty bottle: the Extra Bottle booster (consumeBooster) or a paid continue (No Moves).</summary>
        bool AddExtraBottle(bool consumeBooster)
        {
            if (Board.ExtraBottles >= Economy.MaxExtraBottles)
            {
                Refuse(BoosterType.Bottle, "game.bottle_max");
                return false;
            }
            int index = Board.AddBottle();
            if (consumeBooster)
            {
                Consume(BoosterType.Bottle);
                UsedBooster(BoosterType.Bottle);
            }
            else
            {
                AudioManager.Play(Sfx.AddBottle);
                Haptics.Play(HapticType.Success);
            }
            BumpVersion();
            _inDeadEnd = false;
            BusyBegin();
            PlayOnView((v, d) => v.PlayAddBottle(index, d), BusyCallback(() => ScheduleCheck(CheckDelay)));
            RefreshBoosterStates();
            return true;
        }

        bool UseWand()
        {
            var candidates = Board.WandCandidates();
            if (candidates.Count == 0)
            {
                Refuse(BoosterType.Wand, "game.nothing_to_remove");
                return false;
            }
            int token = _token;
            BusyBegin(ColorJobTimeout + BusyTimeout);
            if (_v.boosters != null)
            {
                var tile = _v.boosters.TileRect(BoosterType.Wand);
                if (tile != null) FX.Sparkles(null, FX.LocalCenterOf(tile), 12);
                _v.boosters.SetHighlight(BoosterType.Wand);
            }
            PickColorAsync(candidates, color =>
            {
                if (token != _token) return;
                BusyDone();
                _v.boosters?.SetHighlight(null);
                if (State != SessionState.Playing || Board == null) return;
                ApplyWand(color);
            });
            return true;
        }

        void ApplyWand(int color)
        {
            var before = CompletedMask();
            RemoveColorResult r = null;
            try { r = Board.RemoveColor(color); }
            catch (Exception e) { Debug.LogException(e); }
            if (r == null)
            {
                Refuse(BoosterType.Wand, "game.nothing_to_remove");
                ScheduleCheck(CheckDelay);
                return;
            }
            Consume(BoosterType.Wand);
            UsedBooster(BoosterType.Wand);
            ReportBoosterCompletions(NewCompletions(before));
            BumpVersion();
            _inDeadEnd = false;
            Vector3 flyTo = _v.boosters != null ? _v.boosters.TileWorldCenter(BoosterType.Wand) : BoardCenterWorld();
            BusyBegin();
            PlayOnView((v, d) => v.PlayRemoveColor(r, flyTo, d), BusyCallback(() =>
            {
                _v.boosters?.Bounce(BoosterType.Wand);
                CelebrateBoosterCompletions();
                ScheduleCheck(CheckDelay);
            }));
            RefreshBoosterStates();
            if (r.won || Board.IsWon) OnWonDetected();
        }

        void Consume(BoosterType type)
        {
            if (!Economy.TryUseBooster(type)) Debug.LogWarning("[Game] " + type + " applied without stock");
        }

        void UsedBooster(BoosterType type)
        {
            Progress.ReportBoosterUsed(type);
            _v.boosters?.Bounce(type);
            _v.boosters?.SetHighlight(null);
            Haptics.Play(HapticType.Medium);
            AudioManager.Play(BoosterSfx(type));
            _idle = 0f;
            HideIdleHint();
            HideBoosterHand();
        }

        static Sfx BoosterSfx(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.Undo: return Sfx.Undo;
                case BoosterType.Shuffle: return Sfx.Shuffle;
                case BoosterType.Bottle: return Sfx.AddBottle;
                case BoosterType.Wand: return Sfx.Wand;
                case BoosterType.Crystal: return Sfx.Crystal;
                case BoosterType.Rainbow: return Sfx.Rainbow;
                default: return Sfx.Pop;
            }
        }

        void Refuse(BoosterType type, string toastKey)
        {
            _v.boosters?.Shake(type);
            AudioManager.Play(Sfx.Error, 0.8f);
            Haptics.Play(HapticType.Warning);
            if (!string.IsNullOrEmpty(toastKey)) UIKit.Toast(Loc.T(toastKey));
        }

        void OpenBuy(BoosterType type)
        {
            AudioManager.Play(Sfx.Click);
            int token = _token;
            BuyBoosterPopup.Open(type, bought =>
            {
                if (!bought || token != _token) return;
                _v.boosters?.Refresh();
                _v.boosters?.Bounce(type);
            });
        }

        /// <summary>End of a booster animation: booster completions the view never corked (no OnBottleCorked) chime now.</summary>
        void CelebrateBoosterCompletions()
        {
            if (_boosterCorks <= 0) return;
            int count = _boosterCorks;
            _boosterCorks = 0;
            AudioManager.PlayCombo(1);
            Haptics.Play(HapticType.Medium);
            FX.Sparkles(null, FX.ToLocal(null, BoardCenterWorld()), Mathf.Min(8 + count * 4, 24));
        }

        /// <summary>Dims the tiles that can't do anything right now (nothing to undo, extra-bottle cap reached).</summary>
        void RefreshBoosterStates()
        {
            var bar = _v.boosters;
            if (bar == null) return;
            bool has = Board != null;
            bar.SetUsable(BoosterType.Undo, has && Board.CanUndo);
            bar.SetUsable(BoosterType.Bottle, has && Board.ExtraBottles < Economy.MaxExtraBottles);
            bar.SetUsable(BoosterType.Shuffle, true);
            bar.SetUsable(BoosterType.Wand, true);
        }

        // ============================================================================================ pre-level boosters

        /// <summary>Crystal Ball, then Rainbow Potion (each only if chosen and useful), then `done`.</summary>
        void ApplyPreBoosters(Action done)
        {
            int token = _token;
            Action rainbow = () =>
            {
                if (!Alive(token) || State != SessionState.Playing) return;
                if (_pre.rainbow) ApplyRainbow(done);
                else GameUI.Invoke(done);
            };
            if (_pre.crystal) ApplyCrystal(rainbow);
            else rainbow();
        }

        void ApplyCrystal(Action next)
        {
            if (Board == null || !Board.AnyHidden)
            {
                // Nothing hidden on this level (the Level Start popup disables the Crystal Ball there).
                GameUI.Invoke(next);
                return;
            }
            var reveals = Board.RevealAll();
            BumpVersion();
            Progress.ReportBoosterUsed(BoosterType.Crystal);
            AudioManager.Play(Sfx.Crystal);
            Haptics.Play(HapticType.Medium);
            BusyBegin();
            var after = BusyCallback(next);
            int token = _token;
            GameFx.BoosterPop(_v.overlay, BoosterType.Crystal, BoardCenterWorld(), null, () =>
            {
                if (token != _token) return;
                PlayOnView((v, d) => v.PlayRevealAll(reveals, d), after);
            });
        }

        void ApplyRainbow(Action next)
        {
            List<int> candidates = Board != null ? Board.WandCandidates() : null;
            if (candidates == null || candidates.Count == 0)
            {
                GameUI.Invoke(next);
                return;
            }
            int token = _token;
            AudioManager.Play(Sfx.Rainbow);
            Haptics.Play(HapticType.Medium);
            var badge = GameFx.BoosterBadge(_v.overlay, BoosterType.Rainbow, BoardCenterWorld());
            BusyBegin(ColorJobTimeout + BusyTimeout);
            PickColorAsync(candidates, color =>
            {
                if (token != _token)
                {
                    badge.Dismiss();
                    return;
                }
                BusyDone();
                if (State != SessionState.Playing || Board == null)
                {
                    badge.Dismiss();
                    return;
                }
                var before = CompletedMask();
                RemoveColorResult r = null;
                try { r = Board.RemoveColor(color); }
                catch (Exception e) { Debug.LogException(e); }
                if (r == null)
                {
                    badge.Dismiss();
                    GameUI.Invoke(next);
                    return;
                }
                Progress.ReportBoosterUsed(BoosterType.Rainbow);
                ReportBoosterCompletions(NewCompletions(before));
                BumpVersion();
                BusyBegin();
                Vector3 flyTo = badge.World;
                PlayOnView((v, d) => v.PlayRemoveColor(r, flyTo, d), BusyCallback(() =>
                {
                    badge.Dismiss();
                    CelebrateBoosterCompletions();
                    if (State == SessionState.Playing) GameUI.Invoke(next);
                }));
                if (r.won || Board.IsWon) OnWonDetected();
            });
        }

        // ============================================================================================ color pick (worker)

        /// <summary>Picks the wand / rainbow color on a worker thread: the best candidate whose removal keeps the board
        /// solvable (solver on a clone, ~20k nodes each), else one the solver could not decide, else the best one.</summary>
        void PickColorAsync(List<int> candidates, Action<int> done)
        {
            CancelColorJob();
            if (candidates == null || candidates.Count == 0 || Board == null)
            {
                GameUI.Invoke(done, -1);
                return;
            }
            BoardState clone;
            try { clone = Board.Clone(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                GameUI.Invoke(done, candidates[0]);
                return;
            }
            var list = new List<int>(candidates);
            _colorJobFallback = list[0];
            _colorJobDone = done;
            _colorJob = WorkerJob<int>.Run(() => PickColor(clone, list), _version);
        }

        /// <summary>Pure: see PickColorAsync. Public for the tests / QA tools.</summary>
        public static int PickColor(BoardState board, List<int> candidates)
        {
            if (board == null || candidates == null || candidates.Count == 0) return -1;
            int unknown = -1;
            int n = Math.Min(WandCandidatesChecked, candidates.Count);
            for (int i = 0; i < n; i++)
            {
                int color = candidates[i];
                var b = board.Clone();
                var r = b.RemoveColor(color);
                if (r == null) continue;
                if (r.won || b.IsWon) return color;
                var res = LevelSolver.Solve(b, WandSolverNodes);
                if (res.Solved) return color;
                if (!res.Exhausted && unknown < 0) unknown = color;
            }
            return unknown >= 0 ? unknown : candidates[0];
        }

        void PollColorJob(float now)
        {
            var job = _colorJob;
            if (job == null) return;
            bool timedOut = now - job.StartedAt > ColorJobTimeout;
            if (!job.IsDone && !timedOut) return;
            var done = _colorJobDone;
            int color = _colorJobFallback;
            _colorJob = null;
            _colorJobDone = null;
            if (job.IsDone && job.TryGet(out int picked) && picked >= 0) color = picked;
            else if (timedOut) Debug.LogWarning("[Game] color pick timed out; using the best candidate");
            GameUI.Invoke(done, color);
        }

        void CancelColorJob()
        {
            _colorJob = null;
            _colorJobDone = null;
        }

        // ============================================================================================ stones

        /// <summary>BoardView.OnLockedTapped: offer the rewarded-ad stone break (GDD §2).</summary>
        public void OnLockedTapped(int bottle)
        {
            if (State != SessionState.Playing || Board == null || Halted || _busy > 0) return;
            if (bottle < 0 || bottle >= Board.Count || !Board[bottle].IsLocked) return;
            HideIdleHint();
            HideBoosterHand();
            _idle = 0f;
            int token = _token;
            StonePopup.Open(Board[bottle].LockRemaining, () =>
            {
                if (token == _token) BreakStoneByAd(bottle);
            });
        }

        /// <summary>Rewarded ad → the stone around `bottle` shatters.</summary>
        void BreakStoneByAd(int bottle)
        {
            if (State != SessionState.Playing || Board == null || _busy > 0) return;
            if (bottle < 0 || bottle >= Board.Count || !Board[bottle].IsLocked) return;
            int token = _token;
            BusyBegin(AdBusyTimeout);
            AdsService.ShowRewarded(AdPlacement.UnlockShelf, earned =>
            {
                if (token != _token) return;
                BusyDone();
                // A declined / failed ad (possibly started from the No Moves popup) must not leave a silently stuck
                // board: re-check so the popup comes back when needed.
                if (!earned || State != SessionState.Playing || Board == null)
                {
                    ScheduleCheck(CheckDelay);
                    return;
                }
                var tick = Board.BreakLock(bottle);
                if (!tick.HasValue)
                {
                    ScheduleCheck(CheckDelay);
                    return;
                }
                BumpVersion();
                _inDeadEnd = false;
                AudioManager.Play(Sfx.Unlock);
                Haptics.Play(HapticType.Success);
                BusyBegin();
                PlayOnView((v, d) => v.PlayBreakLock(bottle, d), BusyCallback(() => ScheduleCheck(CheckDelay)));
            });
        }

        /// <summary>A stone whose ad break would give the stuck board a useful move again (lowest counter first), or -1.</summary>
        int StoneCandidate()
        {
            if (Board == null) return -1;
            int best = -1, bestCount = int.MaxValue;
            try
            {
                for (int i = 0; i < Board.Count; i++)
                {
                    var b = Board[i];
                    if (!b.IsLocked) continue;
                    var test = Board.Clone();
                    if (!test.BreakLock(i).HasValue || !test.HasUsefulMove) continue;
                    if (b.LockRemaining < bestCount)
                    {
                        bestCount = b.LockRemaining;
                        best = i;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return -1;
            }
            return best;
        }
    }
}
