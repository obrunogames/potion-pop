// ============================================================================================================
// GameSession — pours (GDD §2): BoardView.OnPourRequested → BoardState.Pour → PlayPour / PlayInvalid; moves & stars in
// the HUD; the combo chain (completions at most 2 pours apart: x2 Good! … x5+ Fantastic!) with Progress reporting;
// the post-move check once the board is idle (won → win flow, no useful move → No Moves popup); and a background
// solver analysis of every settled board (worker thread, cancelled by the next move) that feeds the idle hint and the
// gentle dead-end tip ("No way out from here — try Undo or an Extra Bottle").
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed partial class GameSession
    {
        /// <summary>A completion at most this many pours after the previous one keeps the chain going (GDD §2).</summary>
        public const int ComboGap = 2;
        /// <summary>Seconds without any action before the board shows a hint.</summary>
        public const float IdleHintSeconds = 15f;
        /// <summary>Solver budget of the background analysis (hint + dead-end detection).</summary>
        public const int AnalysisNodes = 30000;
        /// <summary>The first few illegal pours of each kind per app session explain themselves with a toast.</summary>
        public const int InvalidToastsPerKind = 3;

        const float CheckDelay = 0.05f;
        const float CheckForceAfter = 6f;          // the view claims to animate forever: check anyway
        const float PraiseTimeout = 4f;            // the view never corked a completed bottle: praise anyway

        sealed class PendingPraise
        {
            public int bottle;
            public int combo;
            public float time;
            public bool done;
        }

        struct Analysis
        {
            public int version;
            public bool solved;
            public bool exhausted;
            public Move first;
        }

        /// <summary>Current combo chain (0 = none running).</summary>
        public int Combo { get; private set; }
        public int MaxComboReached { get; private set; }
        /// <summary>Successful pours in this attempt (an undo does not take them back).</summary>
        public int PoursMade { get; private set; }
        /// <summary>Changes on every model change (pour, undo, booster): background results for older versions are
        /// dropped.</summary>
        public int BoardVersion => _version;

        readonly List<PendingPraise> _pendingPraise = new List<PendingPraise>(4);
        static int[] _invalidToasts;
        int _boosterCorks;

        int _version;
        int _pourIndex;
        int _lastCompletionPour = -100;
        bool _checkPending;
        float _checkAt, _checkScheduledAt;
        float _idle;
        bool _hintShown;
        WorkerJob<Analysis> _analysisJob;
        bool _analysisWanted;
        Analysis _analysis;
        bool _hasAnalysis;
        bool _inDeadEnd;

        static void ResetStaticFeedback() => _invalidToasts = null;

        void ResetPlayState()
        {
            Combo = 0;
            MaxComboReached = 0;
            PoursMade = 0;
            LevelStarted = false;
            _pourIndex = 0;
            _lastCompletionPour = -100;
            _checkPending = false;
            _idle = 0f;
            _hintShown = false;
            _hasAnalysis = false;
            _inDeadEnd = false;
            _analysisWanted = false;
            _analysisJob = null;
            _pendingPraise.Clear();
            _boosterCorks = 0;
        }

        // ============================================================================================ view events

        /// <summary>BoardView.OnPourRequested: the player asks to pour from → to.</summary>
        public void HandlePourRequest(int from, int to)
        {
            if (State != SessionState.Playing || Board == null || _busy > 0 || Halted)
            {
                // Not now (popup, booster animation): just put the lifted bottle back down.
                SafeView(v => v.Deselect(true));
                return;
            }
            var why = Board.Check(from, to);
            PourResult r = null;
            if (why == PourRefusal.None)
            {
                try { r = Board.Pour(from, to); }
                catch (Exception e) { Debug.LogException(e); }
                if (r == null) why = PourRefusal.Invalid;
            }
            if (r == null)
            {
                OnInvalidPour(from, to, why);
                return;
            }
            OnPoured(r);
        }

        /// <summary>BoardView.OnBottleSelected: a bottle was lifted.</summary>
        public void OnBottleSelected(int bottle)
        {
            _idle = 0f;
            HideIdleHint();
            HideBoosterHand();
            // A timed tip (rules / hidden colors / stones) may cover a row of bottles: playing dismisses it.
            if (_v.tutorial != null && _v.tutorial.TimedTipVisible)
            {
                _v.tutorial.HideBubble();
                if (_tipHandActive)
                {
                    _v.tutorial.HideHand();
                    _tipHandActive = false;
                }
            }
            OnTutorialSelect(bottle);
        }

        /// <summary>BoardView.OnIdle: every animation finished — run the pending post-move check now.</summary>
        public void OnViewIdle()
        {
            if (_checkPending) _checkAt = Mathf.Min(_checkAt, Time.unscaledTime);
        }

        /// <summary>BoardView.OnBottleCorked: a cork just landed — the completion chime and the combo praise play in sync
        /// with it (the end of the pour animation / a timeout are the fallbacks when this never comes).</summary>
        public void OnBottleCorked(int bottle)
        {
            if (State == SessionState.Idle) return;   // a cork tween outliving the attempt
            for (int i = 0; i < _pendingPraise.Count; i++)
            {
                if (_pendingPraise[i].bottle != bottle) continue;
                DeliverPraise(_pendingPraise[i]);
                return;
            }
            if (_boosterCorks > 0)
            {
                // A bottle completed by a booster (shuffle / wand / rainbow).
                _boosterCorks--;
                AudioManager.PlayCombo(1);
                Haptics.Play(HapticType.Medium);
                FX.Sparkles(null, FX.ToLocal(null, BottleMouthWorld(bottle)), 10);
            }
        }

        // ============================================================================================ pours

        void OnPoured(PourResult r)
        {
            PoursMade++;
            _pourIndex++;
            if (!LevelStarted && _result == null)
            {
                // The attempt only "counts" (heart at stake, abandoned-level detection) once the player poured.
                LevelStarted = true;
                Progress.ReportLevelStart(Level);
            }
            Progress.ReportPour();
            _idle = 0f;
            HideIdleHint();
            HideBoosterHand();
            BumpVersion();
            _v.hud?.SetMoves(Board.Moves, true);

            PendingPraise praise = null;
            if (r.completed)
            {
                bool chain = _pourIndex - _lastCompletionPour <= ComboGap;
                Combo = chain ? Combo + 1 : 1;
                _lastCompletionPour = _pourIndex;
                if (Combo > MaxComboReached) MaxComboReached = Combo;
                Progress.ReportBottleCompleted(Combo);
                praise = new PendingPraise { bottle = r.to, combo = Combo, time = Time.unscaledTime };
                _pendingPraise.Add(praise);
            }
            else if (Combo > 0 && _pourIndex - _lastCompletionPour >= ComboGap) Combo = 0;   // the chain can't continue

            Haptics.Play(HapticType.Light);
            OnTutorialPour(r);

            int token = _token;
            PlayOnView((v, done) => v.PlayPour(r, done), () =>
            {
                if (token == _token && praise != null) DeliverPraise(praise);
            });
            RefreshBoosterStates();

            if (r.won || Board.IsWon)
            {
                OnWonDetected();
                return;
            }
            ScheduleCheck(CheckDelay);
        }

        void OnInvalidPour(int from, int to, PourRefusal why)
        {
            AudioManager.Play(Sfx.Invalid, 0.8f);
            Haptics.Play(HapticType.Light);
            SafeView(v => v.PlayInvalid(from, to, why));
            string key = InvalidToastKey(why);
            if (key != null && TakeInvalidToast(why)) UIKit.Toast(Loc.T(key));
            OnTutorialInvalid();
        }

        static string InvalidToastKey(PourRefusal why)
        {
            switch (why)
            {
                case PourRefusal.WrongColor: return "board.invalid.wrong_color";
                case PourRefusal.TargetFull: return "board.invalid.full";
                case PourRefusal.TargetDone: return "board.invalid.done";
                case PourRefusal.TargetLocked:
                case PourRefusal.SourceLocked: return "board.invalid.stone";
                default: return null;
            }
        }

        static bool TakeInvalidToast(PourRefusal why)
        {
            if (_invalidToasts == null) _invalidToasts = new int[Enum.GetValues(typeof(PourRefusal)).Length];
            int i = (int)why;
            if (i < 0 || i >= _invalidToasts.Length) return false;
            if (_invalidToasts[i] >= InvalidToastsPerKind) return false;
            _invalidToasts[i]++;
            return true;
        }

        // ============================================================================================ completions

        void DeliverPraise(PendingPraise p)
        {
            if (p == null || p.done) return;
            p.done = true;
            _pendingPraise.Remove(p);
            // The cork just landed: the completion chime rises with the chain (GDD §2, AudioManager.PlayCombo).
            AudioManager.PlayCombo(p.combo);
            if (p.combo >= 2) AudioManager.Play(Sfx.Combo, 0.9f, Mathf.Min(1.7f, 0.95f + 0.08f * (p.combo - 2)));
            Haptics.Play(p.combo >= 4 ? HapticType.Heavy : HapticType.Medium);
            Vector3 at = BottleMouthWorld(p.bottle);
            if (p.combo >= 2)
            {
                FX.Burst(null, FX.ToLocal(null, at), "ui_star_small", Mathf.Min(6 + p.combo * 2, 20), DS.Colors.Gold, 620f, 0.7f, 38f, -1100f);
                GameFx.Praise(at, p.combo);
            }
        }

        void UpdatePendingPraise(float now)
        {
            for (int i = _pendingPraise.Count - 1; i >= 0; i--)
            {
                if (i >= _pendingPraise.Count) continue;
                var p = _pendingPraise[i];
                if (now - p.time >= PraiseTimeout) DeliverPraise(p);
            }
        }

        void FlushPendingPraise()
        {
            while (_pendingPraise.Count > 0) DeliverPraise(_pendingPraise[0]);
        }

        /// <summary>Bottles completed by a booster (shuffle, wand, rainbow): reported for the quests right away (the chain
        /// is not touched); their chime plays when the view corks them (OnBottleCorked) or when the booster animation
        /// ends (CelebrateBoosterCompletions).</summary>
        void ReportBoosterCompletions(int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++) Progress.ReportBottleCompleted(1);
            _boosterCorks += count;
        }

        bool[] CompletedMask()
        {
            if (Board == null) return new bool[0];
            var mask = new bool[Board.Count];
            for (int i = 0; i < Board.Count; i++) mask[i] = Board[i].Completed;
            return mask;
        }

        int NewCompletions(bool[] before)
        {
            if (Board == null || before == null) return 0;
            int n = 0;
            for (int i = 0; i < Board.Count; i++)
                if (Board[i].Completed && (i >= before.Length || !before[i])) n++;
            return n;
        }

        // ============================================================================================ post-move check

        void ScheduleCheck(float delay)
        {
            float now = Time.unscaledTime;
            float at = now + delay;
            if (!_checkPending)
            {
                _checkPending = true;
                _checkAt = at;
                _checkScheduledAt = now;
                return;
            }
            if (at > _checkAt) _checkAt = at;
        }

        void BumpVersion()
        {
            _version++;
            _hasAnalysis = false;
        }

        /// <summary>Playing and not halted: pending check once the board settled, dead-end tip.</summary>
        void TickChecks(float now)
        {
            if (_checkPending && now >= _checkAt && _busy == 0
                && (!ViewAnimating() || now - _checkScheduledAt > CheckForceAfter))
            {
                PostCheck();
                if (State != SessionState.Playing) return;
            }
            if (_hasAnalysis && _analysis.version == _version && !_checkPending && _busy == 0)
            {
                if (_analysis.exhausted) OnDeadEnd();
                else _inDeadEnd = false;
            }
        }

        void PostCheck()
        {
            _checkPending = false;
            if (State != SessionState.Playing || Board == null) return;
            bool won, useful;
            try
            {
                won = Board.IsWon;
                useful = won || Board.HasUsefulMove;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (won)
            {
                OnWonDetected();
                return;
            }
            if (!useful)
            {
                ShowNoMoves();
                return;
            }
            RequestAnalysis();
            RefreshTutorialHand();
        }

        // ============================================================================================ background analysis

        /// <summary>Solves a clone of the settled board on a worker thread (one job at a time; a newer board version
        /// re-runs it when the current job finishes). The result feeds the idle hint and the dead-end tip.</summary>
        void RequestAnalysis()
        {
            if (State != SessionState.Playing || Board == null) return;
            if (_hasAnalysis && _analysis.version == _version) return;
            if (_analysisJob != null)
            {
                if (_analysisJob.Version != _version) _analysisWanted = true;
                return;
            }
            BoardState clone;
            try { clone = Board.Clone(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            int version = _version;
            _analysisJob = WorkerJob<Analysis>.Run(() => Analyze(clone, version), version);
        }

        static Analysis Analyze(BoardState clone, int version)
        {
            var res = LevelSolver.Solve(clone, AnalysisNodes);
            return new Analysis
            {
                version = version,
                solved = res.Solved && res.Moves.Count > 0,
                exhausted = res.Exhausted,
                first = res.Moves.Count > 0 ? res.Moves[0] : default,
            };
        }

        void PollJobs(float now)
        {
            if (_analysisJob != null && _analysisJob.IsDone)
            {
                var job = _analysisJob;
                _analysisJob = null;
                if (job.TryGet(out var a) && a.version == _version)
                {
                    _analysis = a;
                    _hasAnalysis = true;
                }
                if (_analysisWanted)
                {
                    _analysisWanted = false;
                    if (!_checkPending) RequestAnalysis();
                }
            }
            PollColorJob(now);
        }

        void CancelJobs()
        {
            // Workers can't be interrupted; their results are simply dropped (version / token checks).
            _analysisJob = null;
            _analysisWanted = false;
            _hasAnalysis = false;
            CancelColorJob();
        }

        void OnDeadEnd()
        {
            if (_inDeadEnd || State != SessionState.Playing || Board == null) return;
            _inDeadEnd = true;
            if (!Board.HasUsefulMove) return;   // the No Moves popup handles a fully stuck board
            bool undoOk = Economy.IsBoosterUnlocked(BoosterType.Undo) && Board.CanUndo;
            bool bottleOk = Economy.IsBoosterUnlocked(BoosterType.Bottle) && Board.ExtraBottles < Economy.MaxExtraBottles;
            string key = undoOk && bottleOk ? "game.dead_end.undo_bottle"
                : undoOk ? "game.dead_end.undo"
                : bottleOk ? "game.dead_end.bottle"
                : "game.dead_end.restart";
            UIKit.Toast(Loc.T(key));
            AudioManager.Play(Sfx.Bubble, 0.7f);
            if (_v.boosters != null)
            {
                if (undoOk) _v.boosters.SetHighlight(BoosterType.Undo, 4.5f);
                else if (bottleOk) _v.boosters.SetHighlight(BoosterType.Bottle, 4.5f);
            }
        }

        // ============================================================================================ idle hint

        void TickIdleHint(float dt)
        {
            if (_hintShown || _tutorialActive || _busy > 0 || Board == null) return;
            _idle += dt;
            if (_idle < IdleHintSeconds) return;
            if (_hasAnalysis && _analysis.version == _version)
            {
                if (!_analysis.solved)
                {
                    _idle = 0f;   // dead end / budget exceeded: nothing useful to point at, try again later
                    return;
                }
                if (ViewAnimating()) return;
                var move = _analysis.first;
                SafeView(v => v.ShowHint(move));
                _hintShown = true;
                return;
            }
            if (_analysisJob == null && !_checkPending) RequestAnalysis();
        }

        void HideIdleHint()
        {
            if (!_hintShown) return;
            _hintShown = false;
            SafeView(v => v.HideHint());
        }
    }
}
