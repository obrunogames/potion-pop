// ============================================================================================================
// The in-level screen (GDD §9 "Game"): full-bleed world backdrop (gamebg_<world>) + vignette (purple on hard levels),
// the HUD at the top of the safe area, the board in between, the booster bar at the bottom above the reserved AdMob
// banner height. OnShow(LevelLaunch) starts a GameSession; OnHide aborts it (a level left after a pour counts as a
// loss). Board events are relayed to the session; the board area follows the banner and the safe area.
// ============================================================================================================
using System;
using PotionPop.Game.Board;
using PotionPop.Services;
using PotionPop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public class GameScreen : UIScreen
    {
        public static GameScreen Instance { get; private set; }
        public override ScreenId Id => ScreenId.Game;
        public override bool ShowBottomNav => false;
        public override bool ShowTopBar => false;
        /// <summary>Game music; the hard-level track while a hard level is loaded.</summary>
        public override Music Music => _session != null && _session.State != SessionState.Idle && _session.Hard ? Music.Hard : Music.Game;

        /// <summary>Horizontal breathing room between the board and the screen edges.</summary>
        const float BoardSideMargin = 18f;
        static readonly Color VignetteNormal = new Color(0.1f, 0.04f, 0.2f, 0.4f);
        static readonly Color VignetteHard = new Color(0.36f, 0.12f, 0.72f, 0.78f);

        Image _background;
        Image _vignette;
        RectTransform _boardArea, _tutorialArea, _overlay;
        BoardView _board;
        GameHud _hud;
        BoosterBar _boosters;
        TutorialOverlay _tutorial;
        GameSession _session;
        string _backgroundSprite;
        float _bannerShown = -1f;
        bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>The current level attempt (null before Build).</summary>
        public GameSession Session => _session;
        public BoardView Board => _board;
        public GameHud Hud => _hud;
        public BoosterBar Boosters => _boosters;
        public TutorialOverlay Tutorial => _tutorial;

        /// <summary>True while a level is being played (not halted by a popup, not finished).</summary>
        public bool IsPlaying => _session != null && _session.IsPlaying;

        public override void Build()
        {
            Instance = this;

            _backgroundSprite = BackgroundFor(Progress.CurrentLevel);
            _background = UIKit.Backdrop(Root, _backgroundSprite);

            var vignetteRoot = UIKit.FullBleed(UIKit.Rect("Vignette", Root), 40f);
            _vignette = UIKit.NewImage(vignetteRoot, "ui_vignette", UISprites.Get("ui_vignette"), VignetteNormal);
            UIKit.Stretch(_vignette.rectTransform);

            _boardArea = UIKit.Rect("BoardArea", Root);
            UIKit.Stretch(_boardArea);
            try { _board = BoardView.Create(_boardArea); }
            catch (Exception e) { Debug.LogException(e); }   // keep building: HUD + flow still work

            _boosters = BoosterBar.Create(Root);
            _hud = GameHud.Create(Root, OnPausePressed);

            _tutorialArea = UIKit.Rect("TutorialArea", Root);
            UIKit.Stretch(_tutorialArea);
            _tutorial = TutorialOverlay.Create(_tutorialArea);

            _overlay = UIKit.Stretch(UIKit.Rect("Overlay", Root));
            var og = _overlay.gameObject.AddComponent<CanvasGroup>();
            og.blocksRaycasts = false;
            og.interactable = false;

            _session = new GameSession(new GameViews
            {
                host = this,
                board = _board,
                boardArea = _boardArea,
                hud = _hud,
                boosters = _boosters,
                tutorial = _tutorial,
                overlay = _overlay,
            });

            if (_boosters != null) _boosters.OnTileTapped += OnBoosterTapped;
            if (_board != null)
            {
                _board.OnPourRequested += OnPourRequested;
                _board.OnBottleSelected += OnBottleSelected;
                _board.OnLockedTapped += OnLockedTapped;
                _board.OnIdle += OnBoardIdle;
                _board.OnBottleCorked += OnBottleCorked;
            }

            ApplyLayout(false);
        }

        /// <summary>Shows the game screen and starts `level` (hearts are checked by the callers: Level Start popup).</summary>
        public static void StartLevel(int level, PreBoosters pre, bool replay = false)
        {
            var sm = ScreenManager.Instance;
            if (sm != null) sm.Show(ScreenId.Game, new LevelLaunch { level = level, pre = pre, replay = replay });
        }

        // ---------------------------------------------------------------------------------------- show / hide

        public override void OnShow(object arg)
        {
            var launch = arg as LevelLaunch ?? new LevelLaunch { level = Progress.CurrentLevel };
            Subscribe();
            AdsService.ShowBanner();
            _bannerShown = -1f;
            ApplyLayout(false);
            ApplyLevelLook(launch.level > 0 ? launch.level : Progress.CurrentLevel);
            if (_session == null || !_session.Start(launch))
            {
                // Should never happen: don't strand the player on an empty board.
                UIKit.Toast(Loc.T("ui.error"));
                Tween.Delay(0.1f, () => ScreenManager.Instance?.Show(ScreenId.Home)).SetLink(this);
            }
        }

        public override void OnHide()
        {
            // Keep the board drawn while the screen cross-fades out (the next Start clears it).
            _session?.Abort(false);
            GameSession.CloseInLevelPopups();
            AdsService.HideBanner();
            Unsubscribe();
        }

        public override bool OnBack() => _session == null || _session.HandleBack();

        void Update()
        {
            // Clamped like Tween.MaxStep: a hitch or the first frame after a resume never jumps timers.
            _session?.Tick(Mathf.Min(Time.unscaledDeltaTime, Tween.MaxStep));
        }

        void OnApplicationPause(bool paused)
        {
            if (!IsVisible || _session == null) return;
            _session.OnAppPause(paused);
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (_boosters != null) _boosters.OnTileTapped -= OnBoosterTapped;
            if (_board != null)
            {
                _board.OnPourRequested -= OnPourRequested;
                _board.OnBottleSelected -= OnBottleSelected;
                _board.OnLockedTapped -= OnLockedTapped;
                _board.OnIdle -= OnBoardIdle;
                _board.OnBottleCorked -= OnBottleCorked;
            }
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------------------------------- look & layout

        static string BackgroundFor(int level)
        {
            AreaInfo area = null;
            try { area = Areas.AreaForLevel(level); }
            catch (Exception e) { Debug.LogException(e); }
            return area != null ? area.GameBackground : "gamebg_forest";
        }

        /// <summary>World backdrop of the level and the vignette (purple on hard levels).</summary>
        void ApplyLevelLook(int level)
        {
            string bg = BackgroundFor(level);
            if (bg != _backgroundSprite && _background != null)
            {
                var sp = UISprites.Get(bg);
                if (sp != null)
                {
                    _backgroundSprite = bg;
                    _background.sprite = sp;
                    _background.color = Color.white;
                    var fit = _background.GetComponent<AspectRatioFitter>();
                    if (fit != null && sp.rect.height > 0f) fit.aspectRatio = sp.rect.width / sp.rect.height;
                }
            }
            if (_vignette != null)
            {
                Tween.Kill(_vignette);
                _vignette.color = Levels.Difficulty.IsHard(level) ? VignetteHard : VignetteNormal;
            }
        }

        /// <summary>Board area between the HUD and the booster bar; the booster bar sits on top of the banner height.</summary>
        void ApplyLayout(bool animate)
        {
            float banner = Mathf.Max(0f, AdsService.BannerHeightCanvasUnits);
            if (Mathf.Approximately(banner, _bannerShown)) return;
            float from = _bannerShown < 0f ? banner : _bannerShown;
            _bannerShown = banner;

            if (_boosters != null && _boosters.Root != null)
            {
                var bar = _boosters.Root;
                Tween.Kill(bar);
                if (animate && isActiveAndEnabled && !Mathf.Approximately(from, banner))
                {
                    bar.anchoredPosition = new Vector2(0f, from);
                    Tween.Move(bar, new Vector2(0f, banner), DS.Motion.Slow, Ease.OutCubic);
                }
                else bar.anchoredPosition = new Vector2(0f, banner);
            }
            float bottom = banner + BoosterBar.Height;
            float top = GameHud.Height;
            SetArea(_boardArea, top, bottom);
            SetArea(_tutorialArea, top, bottom);
            if (_board != null)
            {
                try { _board.Relayout(animate); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static void SetArea(RectTransform rt, float top, float bottom)
        {
            if (rt == null) return;
            rt.offsetMin = new Vector2(BoardSideMargin, bottom);
            rt.offsetMax = new Vector2(-BoardSideMargin, -top);
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            AdsService.OnBannerChanged += OnBannerChanged;
            var sm = ScreenManager.Instance;
            if (sm != null) sm.OnSafeAreaChanged += OnSafeAreaChanged;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            AdsService.OnBannerChanged -= OnBannerChanged;
            var sm = ScreenManager.Instance;
            if (sm != null) sm.OnSafeAreaChanged -= OnSafeAreaChanged;
        }

        void OnBannerChanged() => ApplyLayout(true);

        void OnSafeAreaChanged()
        {
            _bannerShown = -1f;
            ApplyLayout(false);
        }

        // ---------------------------------------------------------------------------------------- input relays

        void OnPausePressed() => _session?.Pause();
        void OnBoosterTapped(BoosterType type) => _session?.OnBoosterTapped(type);
        void OnPourRequested(int from, int to) => _session?.HandlePourRequest(from, to);
        void OnBottleSelected(int bottle) => _session?.OnBottleSelected(bottle);
        void OnLockedTapped(int bottle) => _session?.OnLockedTapped(bottle);
        void OnBoardIdle() => _session?.OnViewIdle();
        void OnBottleCorked(int bottle) => _session?.OnBottleCorked(bottle);
    }
}
