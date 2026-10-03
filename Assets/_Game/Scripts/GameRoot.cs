using System.Collections;
using PotionPop.Game;
using PotionPop.Services;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Bootstrap of the whole game (the only component in Main.unity besides camera and EventSystem):
    /// loads the save, starts audio/services, builds the UI (screens + chrome) behind a splash, then shows Home.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        const float MinSplashSeconds = 1.4f;

        ScreenManager _screens;
        RectTransform _splash;
        ProgressBar _splashBar;
        float _secondTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Application.targetFrameRate = 60;
#if UNITY_EDITOR
            // Keep Play Mode ticking while the editor is unfocused (automated testing through MCP, side-by-side work).
            Application.runInBackground = true;
#endif
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            SaveSystem.Load();
            AudioManager.Init();

            // Defensive: a UI hierarchy must never be part of the scene (it is always built here at runtime).
            foreach (var stray in FindObjectsByType<ScreenManager>(FindObjectsInactive.Include))
            {
                Debug.LogWarning("Potion Pop: removing a stray UI hierarchy saved in the scene: " + stray.name);
                DestroyImmediate(stray.gameObject);
            }
            var ui = new GameObject("UI");
            _screens = ui.AddComponent<ScreenManager>();
            _screens.Setup();

            BuildSplash();
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            float start = Time.realtimeSinceStartup;
            yield return null;

            SetSplashProgress(0.2f);
            // A level killed mid-play counts as a loss (GDD §5) — resolved before any UI reads hearts/streak.
            Progress.ResolveAbandonedLevel();
            AuthService.Init(this);
            CloudSave.OnRestored += () => UIKit.Toast(Loc.T("root.cloud_restored"));
            yield return null;

            SetSplashProgress(0.45f);
            _screens.Register<HomeScreen>();
            _screens.Register<ShopScreen>();
            _screens.Register<LeaderboardScreen>();
            yield return null;

            SetSplashProgress(0.7f);
            _screens.Register<CollectionScreen>();
            _screens.Register<ProfileScreen>();
            _screens.Register<WorldsScreen>();
            _screens.Register<GameScreen>();
            Chrome.Build(_screens);
            yield return null;

            SetSplashProgress(1f);
            while (Time.realtimeSinceStartup - start < MinSplashSeconds) yield return null;

            AdsService.PixelsToCanvasUnits = px =>
                _screens != null && _screens.Canvas != null && _screens.Canvas.scaleFactor > 0f ? px / _screens.Canvas.scaleFactor : px;
            _screens.OnBackUnhandled += OnBackUnhandled;
            _screens.Show(ScreenId.Home);
            HideSplash();
            yield return new WaitForSecondsRealtime(0.5f);
            // Ads after the splash so the consent form (UMP) appears over Home, not over the loading screen.
            AdsService.Init();
        }

        /// <summary>Android back with nothing left to close: Home asks to quit, other tabs go Home.</summary>
        void OnBackUnhandled()
        {
            if (_screens.Current != ScreenId.Home)
            {
                if (_screens.Current != ScreenId.Game) _screens.Show(ScreenId.Home);
                return;
            }
            if (PopupManager.AnyOpen) return;
            ConfirmPopup.Open("root.quit_title", "root.quit_message", "root.quit_yes", "ui.cancel", yes =>
            {
                if (!yes) return;
                SaveSystem.Flush();
                Application.Quit();
            });
        }

        void Update()
        {
            CloudSave.Tick();
            _secondTimer += Time.unscaledDeltaTime;
            if (_secondTimer >= 1f)
            {
                _secondTimer = 0f;
                Lives.Tick();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            SaveSystem.Flush();
            CloudSave.OnApplicationPause();
        }

        void OnApplicationQuit() => SaveSystem.Flush();

        // ------------------------------------------------------------------ splash

        void BuildSplash()
        {
            var layer = _screens.TopLayer != null ? _screens.TopLayer : _screens.SafeRoot;
            if (layer == null) return;
            _splash = UIKit.Rect("Splash", layer);
            if (_splash == null) return;
            UIKit.Stretch(_splash);

            // The current world's home art behind a plum veil, the logo popping in and Luna waving among bubbles.
            var area = Areas.AreaForLevel(SaveSystem.Data.level);
            UIKit.Backdrop(_splash, area != null ? area.HomeBackground : "home_forest");
            var dim = UIKit.Backdrop(_splash, "ui_pixel", new Color(0.16f, 0.08f, 0.31f, 0.42f));
            if (dim != null) dim.transform.parent.SetSiblingIndex(1);
            var bubbles = AmbientFloaters.Create(_splash, 18, new Color(1f, 0.92f, 1f, 1f));
            if (bubbles != null) bubbles.transform.SetSiblingIndex(2);

            var glow = UIKit.NewImage(_splash, "Glow", UISprites.Glow, new Color(1f, 0.85f, 1f, 0.5f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1100, 1100), new Vector2(0, 330));
            Tween.Scale(glow.transform, 1.08f, 1.6f, Ease.InOutSine).SetLoops(-1, true);
            var logo = UIKit.Image(_splash, "logo", new Vector2(880, 580));
            if (logo != null)
            {
                UIKit.Place(logo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(880, 580), new Vector2(0, 360));
                logo.transform.localScale = Vector3.one * 0.6f;
                Tween.Scale(logo.transform, 1f, 0.6f, Ease.OutBack).SetOvershoot(2f).OnComplete(() =>
                {
                    if (logo != null) Tween.Scale(logo.transform, 1.03f, 1.2f, Ease.InOutSine).SetLoops(-1, true);
                });
            }
            var mascot = UIKit.Image(_splash, "mascot_wave", new Vector2(520, 780));
            if (mascot != null)
            {
                UIKit.Place(mascot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520, 780), new Vector2(0, -260));
                mascot.rectTransform.localScale = Vector3.zero;
                Tween.Scale(mascot.rectTransform, 1f, 0.5f, Ease.OutBack).SetOvershoot(2.2f).SetDelay(0.2f);
                Tween.Move(mascot.rectTransform, new Vector2(0, -240), 1.1f, Ease.InOutSine).SetLoops(-1, true);
            }
            _splashBar = UIKit.ProgressBar(_splash, new Vector2(700, 56));
            if (_splashBar != null)
            {
                UIKit.Place((RectTransform)_splashBar.transform, new Vector2(0.5f, 0f), new Vector2(700, 56), new Vector2(0, 220));
                _splashBar.SetValue(0.05f, false);
            }
            var loading = UIKit.LocText(_splash, "ui.loading", TextStyle.BodyLight, new Vector2(700, 70));
            if (loading != null) UIKit.Place(loading.rectTransform, new Vector2(0.5f, 0f), new Vector2(700, 70), new Vector2(0, 300));
        }

        void SetSplashProgress(float value)
        {
            if (_splashBar != null) _splashBar.SetValue(value, true);
        }

        void HideSplash()
        {
            if (_splash == null) return;
            var group = _splash.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var splash = _splash;
            Tween.Fade(group, 0f, 0.35f).OnComplete(() =>
            {
                if (splash != null) Destroy(splash.gameObject);
            });
            _splash = null;
        }
    }
}
