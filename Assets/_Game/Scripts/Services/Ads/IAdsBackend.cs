using System;

namespace PotionPop.Services
{
    /// <summary>
    /// An ads implementation (AdMob or simulated). AdsService owns the rules, the audio pause and the exactly-once
    /// callbacks; a backend only loads and shows. All members are called on the main thread.
    /// </summary>
    internal interface IAdsBackend
    {
        /// <summary>Consent + SDK initialization + first loads.</summary>
        void Start();

        /// <summary>Called every frame (load timeouts, retries).</summary>
        void Tick();

        bool RewardedReady { get; }
        bool InterstitialReady { get; }

        /// <summary>Skip the retry wait: the player wants an ad now.</summary>
        void LoadRewardedNow();
        void LoadInterstitialNow();

        /// <summary>done(earnedReward, wasShown) — exactly once.</summary>
        void ShowRewarded(AdPlacement placement, Action<bool, bool> done);

        /// <summary>done(wasShown) — exactly once.</summary>
        void ShowInterstitial(Action<bool> done);

        void SetBannerVisible(bool visible);
        bool BannerVisible { get; }
        float BannerHeightPixels { get; }

        bool PrivacyOptionsRequired { get; }
        void ShowPrivacyOptions(Action done);
    }
}
