using System;
using UnityEngine;

namespace PotionPop.Services
{
    public enum AdPlacement { Continue, DoubleCoins, ExtraHeart, ExtraSpin, ShopCoins, BreakStone }

    /// <summary>
    /// Google Mobile Ads (AdMob) with UMP consent; simulated overlay when SP_ADMOB is missing, in the Editor with
    /// ServicesConfig.simulatedAdsInEditor, in batch mode and on desktop builds.
    /// Rules (GDD §6): interstitial from level 6, at most every 2 finished levels, ≥ 90 s apart, never within 30 s after
    /// a rewarded ad (counters in PlayerData.levelsSinceInterstitial / lastInterstitialAt). AudioListener.pause while a
    /// full-screen ad is on screen. Every callback is invoked exactly once on the main thread.
    /// </summary>
    public static class AdsService
    {
        /// <summary>How long ShowRewarded waits (with a "loading" overlay) for an ad that is not loaded yet.</summary>
        public const float RewardedWaitSeconds = 6f;

        public static event Action OnRewardedReadyChanged;

        /// <summary>The banner appeared, disappeared or changed height: re-layout the board.</summary>
        public static event Action OnBannerChanged;

        /// <summary>
        /// Converts banner pixels to reference-canvas units. Default: px * 1080 / Screen.width (CanvasScaler matching
        /// width). The UI may replace it (e.g. px / canvas.scaleFactor).
        /// </summary>
        public static Func<float, float> PixelsToCanvasUnits = DefaultPixelsToCanvasUnits;

        static IAdsBackend _backend;
        static bool _initialized;
        static bool _showing;
        static bool _bannerRequested;
        static bool _audioWasPaused;
        static long _lastRewardedAt;
        static bool _lastReady;
        static bool _lastBannerVisible;
        static float _lastBannerPx;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnRewardedReadyChanged = null;
            OnBannerChanged = null;
            PixelsToCanvasUnits = DefaultPixelsToCanvasUnits;
            _backend = null;
            _initialized = _showing = _bannerRequested = _audioWasPaused = false;
            _lastRewardedAt = 0;
            _lastReady = _lastBannerVisible = false;
            _lastBannerPx = 0f;
        }

        public static float DefaultPixelsToCanvasUnits(float px) => Screen.width > 0 ? px * ServicesUI.ReferenceWidth / Screen.width : px;

        // ---------------------------------------------------------------------------------------- state

        /// <summary>A rewarded ad can be shown right now (ShowRewarded also works when false: it waits a few seconds).</summary>
        public static bool RewardedReady => _backend != null && !_showing && _backend.RewardedReady;

        public static bool BannerVisible => _backend != null && _bannerRequested && _backend.BannerVisible;

        /// <summary>Banner height in reference-canvas units (1080 wide); 0 when hidden. Excludes the bottom safe-area inset.</summary>
        public static float BannerHeightCanvasUnits
        {
            get
            {
                if (!BannerVisible) return 0f;
                float px = _backend.BannerHeightPixels;
                if (px <= 0f) return 0f;
                Func<float, float> convert = PixelsToCanvasUnits ?? DefaultPixelsToCanvasUnits;
                return convert(px);
            }
        }

        /// <summary>Ads are simulated (overlay) instead of AdMob.</summary>
        public static bool Simulated => _backend is SimulatedAdsBackend;

        /// <summary>A full-screen ad (or the rewarded "loading" wait) is on screen.</summary>
        public static bool IsShowingFullScreen => _showing;

        /// <summary>Unix seconds of the last rewarded ad shown this session (0 = none).</summary>
        public static long LastRewardedAt => _lastRewardedAt;

        /// <summary>UMP says the app must offer a "privacy options" entry (Settings) to change the consent.</summary>
        public static bool PrivacyOptionsRequired => _backend != null && _backend.PrivacyOptionsRequired;

        /// <summary>Opens the UMP privacy options form (no-op when simulated). done is always called.</summary>
        public static void ShowPrivacyOptions(Action done = null)
        {
            Init();
            if (_backend == null) { ServicesRunner.SafeInvoke(done); return; }
            _backend.ShowPrivacyOptions(done);
        }

        /// <summary>Loc key of an ad placement ("ads.placement.double_coins", ...).</summary>
        public static string PlacementLocKey(AdPlacement placement)
        {
            switch (placement)
            {
                case AdPlacement.Continue: return "ads.placement.continue";
                case AdPlacement.DoubleCoins: return "ads.placement.double_coins";
                case AdPlacement.ExtraHeart: return "ads.placement.extra_heart";
                case AdPlacement.ExtraSpin: return "ads.placement.extra_spin";
                case AdPlacement.ShopCoins: return "ads.placement.shop_coins";
                case AdPlacement.BreakStone: return "ads.placement.break_stone";
                default: return "ads.placement.continue";
            }
        }

        // ---------------------------------------------------------------------------------------- init

        /// <summary>Starts consent + SDK (or the simulation). Idempotent; every public call initializes lazily.</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            ServicesRunner.Ensure();
            ServicesRunner.OnUpdate -= Tick;
            ServicesRunner.OnUpdate += Tick;
            ServicesConfig config = ServicesConfig.Load();
            _backend = CreateBackend(config);
            Debug.Log("[Ads] using " + (_backend is SimulatedAdsBackend ? "simulated ads" : "AdMob"));
            try { _backend.Start(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static IAdsBackend CreateBackend(ServicesConfig config)
        {
#if SP_ADMOB
            bool real;
            if (Application.isBatchMode) real = false;
            else if (Application.isEditor) real = !config.simulatedAdsInEditor;
            else real = Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
            if (real) return new AdMobBackend(config);
#endif
            return new SimulatedAdsBackend();
        }

        static void Tick()
        {
            if (_backend == null) return;
            _backend.Tick();
            bool ready = RewardedReady;
            if (ready != _lastReady)
            {
                _lastReady = ready;
                Raise(OnRewardedReadyChanged);
            }
            bool bannerVisible = BannerVisible;
            float px = bannerVisible ? _backend.BannerHeightPixels : 0f;
            if (bannerVisible != _lastBannerVisible || Mathf.Abs(px - _lastBannerPx) > 0.5f)
            {
                _lastBannerVisible = bannerVisible;
                _lastBannerPx = px;
                Raise(OnBannerChanged);
            }
        }

        // ---------------------------------------------------------------------------------------- rewarded

        /// <summary>
        /// onDone(true) only when the user earned the reward; called exactly once (false when no ad could be shown, the
        /// wait was cancelled or the ad was closed early). Pauses audio while showing.
        /// </summary>
        public static void ShowRewarded(AdPlacement placement, Action<bool> onDone)
        {
            Init();
            bool answered = false;
            Action<bool> reply = earned =>
            {
                if (answered) return;
                answered = true;
                ServicesRunner.SafeInvoke(onDone, earned);
            };

            if (_showing || _backend == null)
            {
                reply(false);
                return;
            }
            if (_backend.RewardedReady)
            {
                PresentRewarded(placement, reply);
                return;
            }

            // Not loaded yet: wait a little behind a "loading ad" overlay (cancellable).
            _backend.LoadRewardedNow();
            _showing = true;
            AdLoadingOverlay.Show(RewardedWaitSeconds, () => _backend != null && _backend.RewardedReady, ready =>
            {
                _showing = false;
                if (ready) PresentRewarded(placement, reply);
                else reply(false);
            });
        }

        static void PresentRewarded(AdPlacement placement, Action<bool> reply)
        {
            Debug.Log("[Ads] rewarded: " + placement);
            _showing = true;
            PauseAudio();
            try
            {
                _backend.ShowRewarded(placement, (earned, shown) =>
                {
                    _showing = false;
                    ResumeAudio();
                    if (shown) _lastRewardedAt = TimeUtil.Now;
                    reply(earned);
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _showing = false;
                ResumeAudio();
                reply(false);
            }
        }

        // ---------------------------------------------------------------------------------------- banner

        public static void ShowBanner()
        {
            Init();
            _bannerRequested = true;
            _backend?.SetBannerVisible(true);
        }

        public static void HideBanner()
        {
            Init();
            _bannerRequested = false;
            _backend?.SetBannerVisible(false);
        }

        // ---------------------------------------------------------------------------------------- interstitial

        /// <summary>
        /// Called when a level ends (win or lose); shows an interstitial if the frequency rules allow (GDD §6) and one is
        /// already loaded (the player never waits for a load). onClosed is always called (immediately if no ad).
        /// Also schedules the post-level cloud sync. Call it once per finished level (win or loss).
        /// </summary>
        public static void OnLevelEnded(int level, Action onClosed)
        {
            Init();
            bool answered = false;
            Action close = () =>
            {
                if (answered) return;
                answered = true;
                ServicesRunner.SafeInvoke(onClosed);
            };

            PlayerData data = SaveSystem.Data;
            data.levelsSinceInterstitial = Math.Max(0, data.levelsSinceInterstitial) + 1;
            SaveSystem.MarkDirty();
            CloudSave.NotifyLevelEnded();   // GDD §7: sync after each level, losses included

            long now = TimeUtil.Now;
            string why = _backend == null ? "ads not available"
                : _showing ? "another ad is on screen"
                : AdRules.WhyNoInterstitial(level, data.levelsSinceInterstitial, data.lastInterstitialAt, _lastRewardedAt, now);
            if (why == null && !_backend.InterstitialReady)
            {
                _backend.LoadInterstitialNow();
                why = "not loaded";
            }
            if (why != null)
            {
                Debug.Log("[Ads] no interstitial after level " + level + ": " + why);
                close();
                return;
            }

            Debug.Log("[Ads] interstitial after level " + level);
            _showing = true;
            PauseAudio();
            try
            {
                _backend.ShowInterstitial(shown =>
                {
                    _showing = false;
                    ResumeAudio();
                    if (shown)
                    {
                        PlayerData d = SaveSystem.Data;   // may have been replaced meanwhile (cloud restore)
                        d.levelsSinceInterstitial = 0;
                        d.lastInterstitialAt = now;
                        SaveSystem.MarkDirty();
                    }
                    close();
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _showing = false;
                ResumeAudio();
                close();
            }
        }

        // ---------------------------------------------------------------------------------------- helpers

        static void PauseAudio()
        {
            _audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
        }

        static void ResumeAudio()
        {
            AudioListener.pause = _audioWasPaused;
        }

        static void Raise(Action handler)
        {
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList()) ServicesRunner.SafeInvoke((Action)d);
        }
    }
}
