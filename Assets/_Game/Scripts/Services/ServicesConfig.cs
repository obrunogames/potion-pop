using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Resources/ServicesConfig.asset (created by the editor builder). Load() never returns null: without the asset the
    /// defaults below are used (offline game + Google's sample AdMob ids). See Docs/Firebase-Setup.md.
    /// </summary>
    public class ServicesConfig : ScriptableObject
    {
        public const string ResourcePath = "ServicesConfig";

        [Header("Firebase (leave empty to run offline)")]
        [Tooltip("Firebase console > Project settings > General > Web API Key.")]
        public string firebaseApiKey = "";
        [Tooltip("Firebase project id (e.g. potion-pop-12345).")]
        public string firebaseProjectId = "";
        [Header("Google Sign-In")]
        public string googleWebClientId = "";    // OAuth "Web application" client id (Android serverClientId)
        public string googleIosClientId = "";    // OAuth iOS client id (GIDClientID)
        [Header("AdMob (defaults: Google sample test ids)")]
        public string admobAndroidAppId = "ca-app-pub-3940256099942544~3347511713";
        public string admobIosAppId = "ca-app-pub-3940256099942544~1458002511";
        public string bannerAndroid = "ca-app-pub-3940256099942544/9214589741";
        public string interstitialAndroid = "ca-app-pub-3940256099942544/1033173712";
        public string rewardedAndroid = "ca-app-pub-3940256099942544/5224354917";
        public string bannerIos = "ca-app-pub-3940256099942544/2435281174";
        public string interstitialIos = "ca-app-pub-3940256099942544/4411468910";
        public string rewardedIos = "ca-app-pub-3940256099942544/1712485313";
        public bool simulatedAdsInEditor = false;
        [Header("AdMob testing (optional)")]
        [Tooltip("AdMob test device ids (printed by the SDK in logcat / Xcode console). Test ads are always served to them.")]
        public List<string> admobTestDeviceIds = new List<string>();
        [Tooltip("Forces the UMP consent form as if the device were in the EEA (only for the hashed test devices below).")]
        public bool umpDebugGeographyEea = false;
        [Tooltip("UMP hashed test device ids (printed by the UMP SDK in the device log).")]
        public List<string> umpTestDeviceHashedIds = new List<string>();
        [Header("Links")]
        public string privacyPolicyUrl = "https://brunogames.com.br/jogos/potion-pop/privacidade";
        public string termsUrl = "https://brunogames.com.br/termos";
        [Tooltip("Support page (FAQ + contact), opened from Settings.")]
        public string supportUrl = "https://brunogames.com.br/jogos/potion-pop/suporte";
        public string supportEmail = "suporte@brunogames.com.br";

        public bool FirebaseConfigured => !string.IsNullOrEmpty(firebaseApiKey) && !string.IsNullOrEmpty(firebaseProjectId);

        /// <summary>Ad unit ids for the current build platform (iOS ids on iOS, Android ids everywhere else).</summary>
        public string BannerUnit =>
#if UNITY_IOS
            bannerIos;
#else
            bannerAndroid;
#endif

        public string InterstitialUnit =>
#if UNITY_IOS
            interstitialIos;
#else
            interstitialAndroid;
#endif

        public string RewardedUnit =>
#if UNITY_IOS
            rewardedIos;
#else
            rewardedAndroid;
#endif

        public string AdMobAppId =>
#if UNITY_IOS
            admobIosAppId;
#else
            admobAndroidAppId;
#endif

        static ServicesConfig _cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _cached = null;

        /// <summary>Resources/ServicesConfig or an in-memory instance with the defaults. Cached in Play Mode.</summary>
        public static ServicesConfig Load()
        {
            if (_cached != null) return _cached;
            ServicesConfig config = Resources.Load<ServicesConfig>(ResourcePath);
            if (config == null)
            {
                config = CreateInstance<ServicesConfig>();
                config.name = "ServicesConfig (defaults)";
                config.hideFlags = HideFlags.DontSave;
            }
            if (Application.isPlaying) _cached = config;
            return config;
        }
    }
}
