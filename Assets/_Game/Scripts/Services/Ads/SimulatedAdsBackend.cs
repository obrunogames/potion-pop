using System;

namespace PotionPop.Services
{
    /// <summary>Offline ads: always "loaded", shown as <see cref="SimulatedAdView"/> / <see cref="SimulatedBannerView"/>.</summary>
    internal sealed class SimulatedAdsBackend : IAdsBackend
    {
        public void Start() { }
        public void Tick() { }

        public bool RewardedReady => true;
        public bool InterstitialReady => true;
        public void LoadRewardedNow() { }
        public void LoadInterstitialNow() { }

        public void ShowRewarded(AdPlacement placement, Action<bool, bool> done)
        {
            SimulatedAdView.Show(true, Loc.T(AdsService.PlacementLocKey(placement)), SimulatedAdView.DefaultSeconds,
                watched => ServicesRunner.SafeInvoke(done, watched, true));
        }

        public void ShowInterstitial(Action<bool> done)
        {
            SimulatedAdView.Show(false, "", SimulatedAdView.DefaultSeconds, _ => ServicesRunner.SafeInvoke(done, true));
        }

        public void SetBannerVisible(bool visible)
        {
            if (visible) SimulatedBannerView.Show();
            else SimulatedBannerView.Hide();
        }

        public bool BannerVisible => SimulatedBannerView.Visible;
        public float BannerHeightPixels => SimulatedBannerView.HeightPixels;

        public bool PrivacyOptionsRequired => false;
        public void ShowPrivacyOptions(Action done) => ServicesRunner.SafeInvoke(done);
    }
}
