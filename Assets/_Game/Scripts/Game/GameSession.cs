// ============================================================================================================
// One level attempt (GDD §2–§9):
//   Idle → Waiting (level generated on a worker thread, then wait for popups / the screen transition)
//        → Intro (bottles drop in + "Level N" banner) → Playing ⇄ (halted by popups / full-screen ads / app pause)
//        → Won | Lost.   Abort() returns to Idle from anywhere.
// Owns the authoritative BoardState: pours, the combo chain, moves & stars, boosters, stones, tutorials and every
// in-level popup flow (pause, no moves, win, fail). The BoardView only animates: pour requests come back through
// HandlePourRequest; booster / ad / pre-booster outcomes are produced here and handed to the view to play.
// Partial files: GameSession.Pour (pours, combos, idle checks, background solver analysis, hints),
// GameSession.Boosters (in-game + pre-level boosters, stones), GameSession.End (pause, no moves, fail, win) and
// GameSession.Tutorial (hand pointer, tips, booster intros).
// Driven by GameScreen.Update with unscaled time. Async callbacks (ads, popups, tweens, workers) are guarded by a
// session token, so nothing from an old attempt ever touches a new one.
// ============================================================================================================
using System;
using System.Threading.Tasks;
using PotionPop.Game.Board;
using PotionPop.Levels;
using PotionPop.Services;
using PotionPop.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PotionPop.Game
{
    public sealed partial class GameSession
    {
        const float BusyTimeout = 8f;              // safety net if the view never calls back after a booster animation
        const float AdBusyTimeout = 90f;           // a rewarded ad (stone) may take a while
        const float EndedIdleToHome = 0.6f;        // finished level, nothing on screen → Home
        const float EndFlowStallToHome = 3f;       // end flow waits for a callback that never came, nothing on screen → Home
        const float IntroTimeout = 7f;             // board intro never reported done → start anyway
        const float GenerationTimeout = 15f;       // worker generation never finished → generate on the main thread

        readonly GameViews _v;

        public SessionState State { get; private set; } = SessionState.Idle;
        public int Level { get; private set; }
        public bool Hard { get; private set; }
        /// <summary>Replay of an already-won level (only newly earned stars are rewarded; no streak, no card).</summary>
        public bool Replay { get; private set; }
        public LevelDefinition Definition { get; private set; }
        public BoardState Board { get; private set; }
        /// <summary>Pours counted for the stars (an undo takes one back).</summary>
        public int Moves => Board != null ? Board.Moves : 0;
        /// <summary>Progress.ReportLevelStart was sent (first pour made) and no result (win/fail) yet. Leaving the
        /// level now costs a heart (GDD §2: restart/quit cost a heart if any pour was made).</summary>
        public bool LevelStarted { get; private set; }
        /// <summary>Result of the win (null otherwise).</summary>
        public LevelResult Result => _result;
        public PreBoosters Pre => _pre;

        /// <summary>Anything that stops play: a popup, a full-screen ad, the app in background.</summary>
        public bool Halted => _appPaused || PopupManager.AnyOpen || AdsService.IsShowingFullScreen;
        /// <summary>A level is running (not halted, not finished).</summary>
        public bool IsPlaying => State == SessionState.Playing && !Halted;
        /// <summary>A booster animation / rewarded ad holds the board (input off).</summary>
        public bool IsBusy => _busy > 0;

        PreBoosters _pre;
        int _token;
        GameObject _owner;
        System.Random _rng = new System.Random();
        LevelResult _result;
        bool _appPaused;
        int _busy;
        float _busySince, _busyTimeout = BusyTimeout;
        float _introSince, _waitSince;
        Task<LevelDefinition> _genTask;

        /// <summary>The last definition used: a restart (or Retry) of the same level reuses it instead of generating
        /// again (BoardState deep-copies it and never writes back).</summary>
        static LevelDefinition _lastDefinition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _lastDefinition = null;
            ResetStaticFeedback();
        }

        public GameSession(GameViews views)
        {
            _v = views ?? new GameViews();
        }

        BoardView View => _v.board;

        // ============================================================================================ lifecycle

        /// <summary>Starts a level attempt (any previous one is aborted first). The definition comes from the restart
        /// cache, LevelPrefetch or a worker thread; the intro waits for it and for popups / the screen transition.
        /// Returns false only when the attempt could not even be set up.</summary>
        public bool Start(LevelLaunch launch)
        {
            Abort();
            _token++;
            int current = Math.Max(1, Progress.CurrentLevel);
            Level = Math.Max(1, launch != null && launch.level > 0 ? launch.level : current);
            _pre = launch != null ? launch.pre : default;
            Replay = (launch != null && launch.replay) || Level < current;
            Hard = Difficulty.IsHard(Level);
            Definition = null;
            Board = null;
            _result = null;
            _busy = 0;
            _rng = new System.Random(unchecked(Level * 7919 + Environment.TickCount));
            ResetPlayState();
            ResetEndState();
            ResetTutorialState();

            try
            {
                _owner = new GameObject("SessionLinks");
                if (_v.host != null) _owner.transform.SetParent(_v.host.transform, false);
            }
            catch (Exception e) { Debug.LogException(e); }

            if (_v.hud != null)
            {
                var area = SafeArea(Level);
                _v.hud.SetLevel(Level, Hard, area != null ? area.accent : DS.Colors.Secondary);
                _v.hud.SetGoal(-1, -1);
                _v.hud.SetMoves(0, false);
                _v.hud.HideForIntro();
            }
            if (_v.boosters != null)
            {
                _v.boosters.Refresh(false);
                _v.boosters.SetHighlight(null);
                _v.boosters.HideForIntro();
            }
            _v.tutorial?.HideAll();
            if (View != null)
            {
                SafeView(v => v.InputEnabled = false);
                SafeView(v => v.Clear());
            }

            State = SessionState.Waiting;
            _waitSince = Time.unscaledTime;
            BeginLoad();
            return true;
        }

        /// <summary>Stops the attempt (screen hidden / restarted). A level left after a pour counts as a loss.</summary>
        public void Abort() => Abort(true);

        /// <summary>Abort; clearBoard=false keeps the board drawn (screen fading out — the next Start clears it).</summary>
        public void Abort(bool clearBoard)
        {
            _token++;
            if (LevelStarted)
            {
                LevelStarted = false;
                Progress.ReportFail(Level);
            }
            _genTask = null;
            CancelJobs();
            _pendingPraise.Clear();
            _boosterCorks = 0;
            if (_v.boosters != null) _v.boosters.SetHighlight(null);
            if (_v.tutorial != null) _v.tutorial.HideAll();
            _hintShown = false;
            _busy = 0;
            _winPending = false;
            _endFlowBusy = false;
            _celebrating = false;
            if (View != null)
            {
                SafeView(v => v.InputEnabled = false);
                SafeView(v => v.HideHint());
                if (clearBoard) SafeView(v => v.Clear());
            }
            if (_owner != null) Object.Destroy(_owner);
            _owner = null;
            if (_v.overlay != null)
                for (int i = _v.overlay.childCount - 1; i >= 0; i--) Object.Destroy(_v.overlay.GetChild(i).gameObject);
            State = SessionState.Idle;
        }

        /// <summary>Closes the popups that only make sense inside a running level (their callbacks are dead once the
        /// attempt ended, and e.g. No Moves would still charge coins for a continue that can't apply).</summary>
        public static void CloseInLevelPopups()
        {
            CloseNow(PopupManager.Get<PausePopup>());
            CloseNow(PopupManager.Get<NoMovesPopup>());
            CloseNow(PopupManager.Get<BoosterIntroPopup>());
            CloseNow(PopupManager.Get<StonePopup>());
        }

        static void CloseNow(Popup p)
        {
            if (p != null) p.CloseImmediate();
        }

        // ============================================================================================ level loading

        void BeginLoad()
        {
            int level = Level;
            var cached = _lastDefinition;
            if (cached != null && cached.number == level)
            {
                OnDefinitionReady(cached);
                return;
            }
            if (LevelPrefetch.TryPeek(level, out _))
            {
                var ready = LevelPrefetch.Take(level);
                if (ready != null)
                {
                    OnDefinitionReady(ready);
                    return;
                }
            }
            // The catalog loads through Resources.Load (main thread only): touch it here so the worker only reads it.
            try { var _ = Catalog.Areas; }
            catch (Exception e) { Debug.LogException(e); }
            try
            {
                // On the worker: wait for a prefetch of this level already in flight (never slower than starting over),
                // otherwise generate it. Nothing blocks the main thread.
                _genTask = Task.Run(() => LevelPrefetch.Take(level) ?? LevelGenerator.Generate(level));
            }
            catch (Exception)
            {
                _genTask = null;
                GenerateNow();
            }
        }

        void TickWaiting(float now)
        {
            if (Definition == null)
            {
                var task = _genTask;
                if (task == null)
                {
                    GenerateNow();
                    return;
                }
                if (!task.IsCompleted)
                {
                    if (now - _waitSince > GenerationTimeout)
                    {
                        Debug.LogWarning("[Game] level generation is taking too long; generating on the main thread");
                        _genTask = null;
                        GenerateNow();
                    }
                    return;
                }
                _genTask = null;
                LevelDefinition def = null;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    try { def = task.Result; }
                    catch (Exception e) { Debug.LogException(e); }
                }
                else if (task.Exception != null) Debug.LogException(task.Exception.GetBaseException());
                if (def != null) OnDefinitionReady(def);
                else GenerateNow();
                return;
            }
            if (State != SessionState.Waiting) return;
            var sm = ScreenManager.Instance;
            if (!PopupManager.AnyOpen && !AdsService.IsShowingFullScreen && (sm == null || !sm.IsTransitioning)) BeginIntro();
        }

        void GenerateNow()
        {
            LevelDefinition def = null;
            try { def = LevelGenerator.Generate(Level); }
            catch (Exception e) { Debug.LogException(e); }
            if (def != null) OnDefinitionReady(def);
            else FailToLoad();
        }

        void OnDefinitionReady(LevelDefinition def)
        {
            if (State != SessionState.Waiting || def == null) return;
            try
            {
                Board = new BoardState(def);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                FailToLoad();
                return;
            }
            Definition = def;
            _lastDefinition = def;
            Hard = def.hard || Difficulty.IsHard(Level);
            BumpVersion();
            if (_v.hud != null)
            {
                _v.hud.SetGoal(def.movesFor3Stars, def.movesFor2Stars);
                _v.hud.SetMoves(0, false);
            }
            RefreshBoosterStates();
            if (View != null)
            {
                string areaId = !string.IsNullOrEmpty(def.areaId) ? def.areaId : (SafeArea(Level)?.id ?? "forest");
                var board = Board;
                SafeView(v => v.Build(board, areaId));
            }
        }

        /// <summary>The level could not be built (should never happen): don't strand the player on an empty board.</summary>
        void FailToLoad()
        {
            Debug.LogError("[Game] could not build level " + Level);
            State = SessionState.Idle;
            UIKit.Toast(Loc.T("ui.error"));
            int token = _token;
            Tween.Delay(0.2f, () =>
            {
                if (token == _token) GoHome();
            });
        }

        // ============================================================================================ frame

        /// <summary>Called every frame by GameScreen (unscaled delta, clamped).</summary>
        public void Tick(float dt)
        {
            if (State == SessionState.Idle) return;
            float now = Time.unscaledTime;

            if (State == SessionState.Waiting)
            {
                TickWaiting(now);
                return;
            }
            if (State == SessionState.Intro)
            {
                // Safety net: a board (or banner) that never reports its intro must not strand the player.
                if (now - _introSince > IntroTimeout)
                {
                    Debug.LogWarning("[Game] board intro never completed; starting the level anyway");
                    StartPlaying();
                }
                return;
            }

            PollJobs(now);
            UpdatePendingPraise(now);
            if (_busy > 0 && now - _busySince > _busyTimeout)
            {
                Debug.LogWarning("[Game] board callback timed out; releasing the input lock");
                _busy = 0;
                ScheduleCheck(0.05f);
            }

            if (State == SessionState.Won || State == SessionState.Lost)
            {
                SetInput(false);
                TickEnded(dt, now);
                return;
            }
            if (State != SessionState.Playing) return;

            bool halted = Halted;
            SetInput(!halted && _busy == 0);
            if (halted) return;

            TickChecks(now);
            if (State != SessionState.Playing) return;
            TickTutorial(dt, now);
            TickIdleHint(dt);
        }

        /// <summary>App sent to background / back: pause popup while playing.</summary>
        public void OnAppPause(bool paused)
        {
            _appPaused = paused;
            if (paused && State == SessionState.Playing && !AdsService.IsShowingFullScreen) Pause();
        }

        /// <summary>Hardware back on the game screen (no popup open). Always handled while the screen is visible.</summary>
        public bool HandleBack()
        {
            if (State == SessionState.Playing && !PopupManager.AnyOpen && !AdsService.IsShowingFullScreen) Pause();
            return true;
        }

        // ============================================================================================ intro

        void BeginIntro()
        {
            State = SessionState.Intro;
            _introSince = Time.unscaledTime;
            int token = _token;
            bool boardDone = false, bannerDone = false;
            Action tryStart = () =>
            {
                if (token != _token || State != SessionState.Intro) return;
                if (boardDone && bannerDone) StartPlaying();
            };

            AudioManager.PlayMusic(Hard ? Music.Hard : Music.Game);
            _v.hud?.PopIn();
            _v.boosters?.PopIn();
            SetInput(false);
            if (View != null)
            {
                bool started = false;
                try
                {
                    View.PlayIntro(() =>
                    {
                        boardDone = true;
                        tryStart();
                    });
                    started = true;
                }
                catch (Exception e) { Debug.LogException(e); }
                if (!started) boardDone = true;
            }
            else boardDone = true;

            GameFx.LevelBanner(_v.overlay, Level, Hard, () =>
            {
                bannerDone = true;
                tryStart();
            });
        }

        void StartPlaying()
        {
            if (State != SessionState.Intro) return;
            State = SessionState.Playing;
            _idle = 0f;
            RefreshBoosterStates();
            int token = _token;
            ApplyPreBoosters(() =>
            {
                if (token != _token || State != SessionState.Playing) return;
                // Tutorials / booster intros start on the first frame nothing halts the level (a pause opened
                // during the pre-boosters must not get a popup stacked on top of it).
                _tutorialsPending = true;
                ScheduleCheck(0.2f);
            });
        }

        // ============================================================================================ busy lock

        /// <summary>A booster animation / ad holds the board: input off until BusyDone (or the safety timeout).</summary>
        void BusyBegin(float timeout = BusyTimeout)
        {
            _busy++;
            _busySince = Time.unscaledTime;
            _busyTimeout = Mathf.Max(timeout, _busy > 1 ? _busyTimeout : 0f);
            SetInput(false);
        }

        void BusyDone()
        {
            if (_busy > 0) _busy--;
            if (_busy == 0) _busyTimeout = BusyTimeout;
        }

        /// <summary>Wraps a view callback so it runs once, only for this attempt, and always releases the busy lock.</summary>
        Action BusyCallback(Action after)
        {
            int token = _token;
            bool finished = false;
            return () =>
            {
                if (token != _token || finished) return;   // a view calling back twice must not release another lock
                finished = true;
                BusyDone();
                GameUI.Invoke(after);
            };
        }

        // ============================================================================================ helpers

        void SetInput(bool on)
        {
            var view = View;
            if (view == null) return;
            try
            {
                if (view.InputEnabled != on) view.InputEnabled = on;
            }
            catch (Exception) { /* a broken view must not break the session */ }
        }

        bool ViewAnimating()
        {
            if (View == null) return false;
            try { return View.IsAnimating; }
            catch (Exception) { return false; }
        }

        /// <summary>Wait for every queued job. If the view truly stalled, reconcile it before any end/stuck flow.</summary>
        bool ViewSettled(float now, float since, float timeout)
        {
            if (!ViewAnimating()) return true;
            try
            {
                if (now - Mathf.Max(since, View.LastAnimationProgressTime) <= timeout) return false;
                View.RefreshAll();
            }
            catch (Exception e) { Debug.LogException(e); }
            return !ViewAnimating();
        }

        void SafeView(Action<BoardView> a)
        {
            if (View == null || a == null) return;
            try { a(View); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Calls a view animation with a completion callback; if the view is missing or throws, the
        /// callback runs right away (the flow never waits for an animation that will not come).</summary>
        void PlayOnView(Action<BoardView, Action> play, Action done)
        {
            if (View == null || play == null)
            {
                GameUI.Invoke(done);
                return;
            }
            try { play(View, done); }
            catch (Exception e)
            {
                Debug.LogException(e);
                GameUI.Invoke(done);
            }
        }

        Vector3 BoardCenterWorld()
        {
            var r = _v.boardArea;
            if (r == null && View != null) r = View.transform as RectTransform;
            return r != null ? r.TransformPoint(r.rect.center) : Vector3.zero;
        }

        Vector3 BottleWorld(int bottle)
        {
            if (bottle < 0 || View == null) return BoardCenterWorld();
            try { return View.BottleWorldPosition(bottle); }
            catch (Exception) { return BoardCenterWorld(); }
        }

        Vector3 BottleMouthWorld(int bottle)
        {
            if (bottle < 0 || View == null) return BoardCenterWorld();
            try { return View.BottleMouthWorldPosition(bottle); }
            catch (Exception) { return BoardCenterWorld(); }
        }

        static AreaInfo SafeArea(int level)
        {
            try { return Areas.AreaForLevel(level); }
            catch (Exception) { return null; }
        }

        bool Alive(int token) => token == _token && State != SessionState.Idle;
    }
}
