#if SP_ADMOB
using System;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Google Mobile Ads 11.x: UMP consent first (ConsentInformation.Update → LoadAndShowConsentFormIfRequired), then
    /// MobileAds.Initialize. The app does not request tracking (no ATT prompt, no IDFA explainer message published in
    /// AdMob): ads on iOS are served without the IDFA. Rewarded and interstitial are preloaded and reloaded after every
    /// show; failed or hanging loads retry with exponential backoff. Adaptive anchored banner at the bottom, loaded on
    /// first request and then shown/hidden.
    /// In the Editor the Google plugin renders its placeholder ads.
    /// </summary>
    internal sealed class AdMobBackend : IAdsBackend
    {
        const float LoadTimeoutSeconds = 45f;
        const float RetryMinSeconds = 4f;
        const float RetryMaxSeconds = 120f;
        const float ConsentRetryMinSeconds = 30f;
        const float ConsentRetryMaxSeconds = 600f;
        /// <summary>A shown ad that never reports "opened" while the game keeps running is considered failed.</summary>
        const float OpenTimeoutSeconds = 10f;
        /// <summary>The reward callback may arrive just after "closed" on some devices.</summary>
        const float RewardGraceSeconds = 0.5f;

        sealed class Slot
        {
            public readonly bool rewardedFormat;
            public RewardedAd rewarded;
            public InterstitialAd interstitial;
            public bool loading;
            public float loadStartedAt;
            public int ticket;
            public int failures;
            public float retryAt = -1f;
            /// <summary>Cached CanShowAd() (a native call), refreshed once per second by Tick.</summary>
            public bool canShow;
            public float nextCheckAt;

            public Slot(bool rewardedFormat) => this.rewardedFormat = rewardedFormat;

            public string Format => rewardedFormat ? "rewarded" : "interstitial";

            public bool HasAd => rewardedFormat ? rewarded != null : interstitial != null;

            public bool Ready => HasAd && canShow;

            public bool CanShowNow()
            {
                try { return rewardedFormat ? rewarded != null && rewarded.CanShowAd() : interstitial != null && interstitial.CanShowAd(); }
                catch (Exception) { return false; }
            }

            public void DestroyAd()
            {
                try
                {
                    rewarded?.Destroy();
                    interstitial?.Destroy();
                }
                catch (Exception) { }
                rewarded = null;
                interstitial = null;
                canShow = false;
            }
        }

        /// <summary>A full-screen ad on screen, watched for the "never opened" case.</summary>
        sealed class Showing
        {
            public bool opened;
            public float runningSeconds;
            public Action<bool> fail;
        }

        readonly ServicesConfig _config;
        readonly Slot _rewarded = new Slot(true);
        readonly Slot _interstitial = new Slot(false);
        Showing _showing;

        bool _started;
        bool _sdkInitStarted;
        bool _sdkReady;
        int _consentFailures;
        float _consentRetryAt = -1f;

        BannerView _banner;
        bool _bannerWanted;
        bool _bannerLoaded;
        bool _bannerLoading;
        int _bannerFailures;
        float _bannerRetryAt = -1f;
        float _bannerHeightPx;

        public AdMobBackend(ServicesConfig config)
        {
            _config = config;
        }

        static float Now => Time.realtimeSinceStartup;

        // ---------------------------------------------------------------------------------------- start / consent

        public void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                // Ad events are raised on Unity's main thread (we still Post everything, which is a no-op there).
                MobileAds.RaiseAdEventsOnUnityMainThread = true;
                // iOS: pause the Unity player while a full-screen ad is shown, like Android does.
                MobileAds.SetiOSAppPauseOnBackground(true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            RequestConsent();
        }

        void RequestConsent()
        {
            try
            {
                var request = new ConsentRequestParameters { TagForUnderAgeOfConsent = false };
                if (_config.umpDebugGeographyEea || (_config.umpTestDeviceHashedIds != null && _config.umpTestDeviceHashedIds.Count > 0))
                {
                    request.ConsentDebugSettings = new ConsentDebugSettings
                    {
                        DebugGeography = _config.umpDebugGeographyEea ? DebugGeography.EEA : DebugGeography.Disabled,
                        TestDeviceHashedIds = new List<string>(_config.umpTestDeviceHashedIds ?? new List<string>()),
                    };
                }

                // Consent given in a previous session: start loading ads right away, in parallel with the update.
                if (ConsentInformation.CanRequestAds()) InitSdk();

                ConsentInformation.Update(request, updateError => ServicesRunner.Post(() =>
                {
                    if (updateError != null)
                    {
                        Debug.LogWarning("[Ads] consent info update failed: " + updateError.ErrorCode + " " + updateError.Message);
                        ConsentFlowEnded(true);
                        return;
                    }
                    ConsentForm.LoadAndShowConsentFormIfRequired(formError => ServicesRunner.Post(() =>
                    {
                        if (formError != null) Debug.LogWarning("[Ads] consent form failed: " + formError.ErrorCode + " " + formError.Message);
                        ConsentFlowEnded(formError != null);
                    }));
                }));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ConsentFlowEnded(true);
            }
        }

        void ConsentFlowEnded(bool failed)
        {
            bool canRequest;
            try { canRequest = ConsentInformation.CanRequestAds(); }
            catch (Exception) { canRequest = false; }
            if (canRequest)
            {
                _consentFailures = 0;
                _consentRetryAt = -1f;
                InitSdk();
                return;
            }
            if (failed)
            {
                // Typically offline at launch: try the consent flow again later.
                _consentFailures++;
                float wait = AdRules.BackoffSeconds(_consentFailures, ConsentRetryMinSeconds, ConsentRetryMaxSeconds);
                _consentRetryAt = Now + wait;
                Debug.LogWarning("[Ads] cannot request ads yet; consent retry in " + Mathf.RoundToInt(wait) + " s.");
            }
        }

        void InitSdk()
        {
            if (_sdkInitStarted) return;
            _sdkInitStarted = true;
            try
            {
                var testIds = new List<string> { AdRequest.TestDeviceSimulator };
                if (_config.admobTestDeviceIds != null)
                    foreach (string id in _config.admobTestDeviceIds)
                        if (!string.IsNullOrWhiteSpace(id)) testIds.Add(id.Trim());
                MobileAds.SetRequestConfiguration(new RequestConfiguration { TestDeviceIds = testIds });
                MobileAds.Initialize(status => ServicesRunner.Post(() =>
                {
                    _sdkReady = true;
                    Debug.Log("[Ads] AdMob initialized.");
                    Load(_rewarded);
                    Load(_interstitial);
                    if (_bannerWanted) LoadBanner();
                }));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ---------------------------------------------------------------------------------------- tick

        public void Tick()
        {
            float now = Now;
            if (_consentRetryAt >= 0f && now >= _consentRetryAt)
            {
                _consentRetryAt = -1f;
                RequestConsent();
            }
            TickSlot(_rewarded, now);
            TickSlot(_interstitial, now);
            if (_bannerWanted && !_bannerLoaded && !_bannerLoading && _bannerRetryAt >= 0f && now >= _bannerRetryAt) LoadBanner();

            if (_showing != null)
            {
                // Clamped so the first frame after an ad (Unity was paused) cannot jump past the timeout.
                _showing.runningSeconds += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                if (!_showing.opened && _showing.runningSeconds > OpenTimeoutSeconds)
                {
                    Debug.LogWarning("[Ads] full-screen ad never opened; giving up.");
                    _showing.fail?.Invoke(true);
                }
            }
        }

        void TickSlot(Slot slot, float now)
        {
            if (slot.HasAd && now >= slot.nextCheckAt)
            {
                slot.nextCheckAt = now + 1f;
                slot.canShow = slot.CanShowNow();
                if (!slot.canShow)
                {
                    // Loaded ads expire (about an hour): throw it away and load a fresh one.
                    Debug.Log("[Ads] " + slot.Format + " expired; reloading.");
                    slot.DestroyAd();
                    Load(slot);
                }
            }
            if (slot.loading && now - slot.loadStartedAt > LoadTimeoutSeconds)
            {
                slot.ticket++;          // the late answer will be discarded
                slot.loading = false;
                ScheduleRetry(slot, "timeout");
            }
            if (!slot.loading && slot.retryAt >= 0f && now >= slot.retryAt) Load(slot);
        }

        // ---------------------------------------------------------------------------------------- loading

        void Load(Slot slot)
        {
            if (!_sdkReady || slot.loading || slot.HasAd) return;
            slot.loading = true;
            slot.retryAt = -1f;
            slot.loadStartedAt = Now;
            int ticket = ++slot.ticket;
            try
            {
                if (slot.rewardedFormat)
                    RewardedAd.Load(_config.RewardedUnit, new AdRequest(), (ad, error) => ServicesRunner.Post(() => OnLoaded(slot, ticket, ad, null, error)));
                else
                    InterstitialAd.Load(_config.InterstitialUnit, new AdRequest(), (ad, error) => ServicesRunner.Post(() => OnLoaded(slot, ticket, null, ad, error)));
            }
            catch (Exception e)
            {
                slot.loading = false;
                Debug.LogException(e);
                ScheduleRetry(slot, "exception");
            }
        }

        void OnLoaded(Slot slot, int ticket, RewardedAd rewarded, InterstitialAd interstitial, LoadAdError error)
        {
            if (ticket != slot.ticket)
            {
                rewarded?.Destroy();
                interstitial?.Destroy();
                return;
            }
            slot.loading = false;
            if (error != null || (rewarded == null && interstitial == null))
            {
                ScheduleRetry(slot, error != null ? error.GetCode() + " " + error.GetMessage() : "empty response");
                return;
            }
            slot.failures = 0;
            slot.DestroyAd();
            slot.rewarded = rewarded;
            slot.interstitial = interstitial;
            slot.canShow = true;
            slot.nextCheckAt = Now + 1f;
        }

        void ScheduleRetry(Slot slot, string reason)
        {
            slot.failures++;
            float wait = AdRules.BackoffSeconds(slot.failures, RetryMinSeconds, RetryMaxSeconds) * UnityEngine.Random.Range(0.85f, 1.15f);
            slot.retryAt = Now + wait;
            Debug.LogWarning("[Ads] " + slot.Format + " did not load (" + reason + "); retry in " + Mathf.RoundToInt(wait) + " s.");
        }

        void Hurry(Slot slot)
        {
            if (slot.loading || slot.HasAd) return;
            if (!_sdkReady)
            {
                if (_consentRetryAt >= 0f) _consentRetryAt = Now;   // offline at launch: retry the consent flow now
                return;
            }
            slot.retryAt = -1f;
            Load(slot);
        }

        public bool RewardedReady => _sdkReady && _rewarded.Ready;
        public bool InterstitialReady => _sdkReady && _interstitial.Ready;
        public void LoadRewardedNow() => Hurry(_rewarded);
        public void LoadInterstitialNow() => Hurry(_interstitial);

        // ---------------------------------------------------------------------------------------- showing

        public void ShowRewarded(AdPlacement placement, Action<bool, bool> done)
        {
            if (!_rewarded.CanShowNow())
            {
                _rewarded.DestroyAd();
                ServicesRunner.SafeInvoke(done, false, false);
                Hurry(_rewarded);
                return;
            }
            RewardedAd ad = _rewarded.rewarded;
            _rewarded.rewarded = null;
            _rewarded.canShow = false;

            bool earned = false, finished = false;
            var showing = new Showing();
            Action<bool> finish = failed =>
            {
                if (finished) return;
                finished = true;
                if (_showing == showing) _showing = null;
                try { ad.Destroy(); } catch (Exception) { }
                ServicesRunner.SafeInvoke(done, earned && !failed, !failed);
                Load(_rewarded);
            };
            showing.fail = finish;
            _showing = showing;

            try
            {
                ad.OnAdFullScreenContentOpened += () => ServicesRunner.Post(() => showing.opened = true);
                ad.OnAdFullScreenContentClosed += () => ServicesRunner.Post(() =>
                {
                    showing.opened = true;
                    if (earned) finish(false);
                    else ServicesRunner.Delay(RewardGraceSeconds, () => finish(false));
                });
                ad.OnAdFullScreenContentFailed += error => ServicesRunner.Post(() =>
                {
                    Debug.LogWarning("[Ads] rewarded failed to show: " + (error != null ? error.GetMessage() : "?"));
                    finish(true);
                });
                ad.Show(reward => ServicesRunner.Post(() => earned = true));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                finish(true);
            }
        }

        public void ShowInterstitial(Action<bool> done)
        {
            if (!_interstitial.CanShowNow())
            {
                _interstitial.DestroyAd();
                ServicesRunner.SafeInvoke(done, false);
                Hurry(_interstitial);
                return;
            }
            InterstitialAd ad = _interstitial.interstitial;
            _interstitial.interstitial = null;
            _interstitial.canShow = false;

            bool finished = false;
            var showing = new Showing();
            Action<bool> finish = failed =>
            {
                if (finished) return;
                finished = true;
                if (_showing == showing) _showing = null;
                try { ad.Destroy(); } catch (Exception) { }
                ServicesRunner.SafeInvoke(done, !failed);
                Load(_interstitial);
            };
            showing.fail = finish;
            _showing = showing;

            try
            {
                ad.OnAdFullScreenContentOpened += () => ServicesRunner.Post(() => showing.opened = true);
                ad.OnAdFullScreenContentClosed += () => ServicesRunner.Post(() => finish(false));
                ad.OnAdFullScreenContentFailed += error => ServicesRunner.Post(() =>
                {
                    Debug.LogWarning("[Ads] interstitial failed to show: " + (error != null ? error.GetMessage() : "?"));
                    finish(true);
                });
                ad.Show();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                finish(true);
            }
        }

        // ---------------------------------------------------------------------------------------- banner

        public void SetBannerVisible(bool visible)
        {
            _bannerWanted = visible;
            try
            {
                if (visible)
                {
                    if (_bannerLoaded && _banner != null) _banner.Show();
                    else if (!_bannerLoading) LoadBanner();
                }
                else
                {
                    _banner?.Hide();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void LoadBanner()
        {
            if (!_sdkReady || _bannerLoading) return;
            _bannerRetryAt = -1f;
            try
            {
                if (_banner == null)
                {
                    AdSize size = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
                    _banner = new BannerView(_config.BannerUnit, size, AdPosition.Bottom);
                    _banner.OnBannerAdLoaded += () => ServicesRunner.Post(OnBannerLoaded);
                    _banner.OnBannerAdLoadFailed += error => ServicesRunner.Post(() => OnBannerFailed(error));
                }
                _bannerLoading = true;
                _banner.LoadAd(new AdRequest());
            }
            catch (Exception e)
            {
                _bannerLoading = false;
                Debug.LogException(e);
                ScheduleBannerRetry("exception");
            }
        }

        void OnBannerLoaded()
        {
            _bannerLoading = false;
            _bannerLoaded = true;
            _bannerFailures = 0;
            try
            {
                _bannerHeightPx = _banner.GetHeightInPixels();
                // Banners show themselves when they load (and on every refresh): obey the current request.
                if (_bannerWanted) _banner.Show();
                else _banner.Hide();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnBannerFailed(LoadAdError error)
        {
            _bannerLoading = false;
            // A failed refresh keeps the previous creative on screen; only retry when nothing was ever shown.
            if (!_bannerLoaded) ScheduleBannerRetry(error != null ? error.GetCode() + " " + error.GetMessage() : "?");
        }

        void ScheduleBannerRetry(string reason)
        {
            _bannerFailures++;
            float wait = AdRules.BackoffSeconds(_bannerFailures, RetryMinSeconds, RetryMaxSeconds);
            _bannerRetryAt = Now + wait;
            Debug.LogWarning("[Ads] banner did not load (" + reason + "); retry in " + Mathf.RoundToInt(wait) + " s.");
        }

        public bool BannerVisible => _bannerWanted && _bannerLoaded;
        public float BannerHeightPixels => BannerVisible ? _bannerHeightPx : 0f;

        // ---------------------------------------------------------------------------------------- privacy

        public bool PrivacyOptionsRequired
        {
            get
            {
                try { return ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required; }
                catch (Exception) { return false; }
            }
        }

        public void ShowPrivacyOptions(Action done)
        {
            try
            {
                ConsentForm.ShowPrivacyOptionsForm(error => ServicesRunner.Post(() =>
                {
                    if (error != null) Debug.LogWarning("[Ads] privacy options form failed: " + error.Message);
                    // The choice may have changed what can be requested (e.g. consent given now).
                    if (!_sdkInitStarted) ConsentFlowEnded(false);
                    ServicesRunner.SafeInvoke(done);
                }));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ServicesRunner.SafeInvoke(done);
            }
        }
    }
}
#endif
