// ============================================================================================================
// Fails player builds whose application identifier is not the project's bundle id (PotionPopBuilder.BundleId).
// The identity is only written by PotionPopBuilder.Rebuild (menu "Potion Pop/Rebuild Project", also run by the
// Potion Pop/Build menu and BuildScript.Build). A build started from Unity's own Build Profiles window / Build And Run
// without that step would ship under a stale id: Google Sign-In (package + SHA-1 bound OAuth client) and Firebase
// calls restricted to the real bundle would fail, and the app would install next to the real one with its own save.
// It fails the build instead of fixing the settings: ApplyPlayerSettings also changes icons, scripting backend,
// architectures and splash settings, which is unsafe to do in the middle of a build.
// Non-development mobile builds also need a loadable ServicesConfig with Firebase set up and real AdMob ids for the
// platform being built: when the asset does not load, the game silently falls back to Google's test ad units and no
// cloud save.
// ============================================================================================================
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PotionPop.EditorTools
{
    sealed class BuildIdentityGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            NamedBuildTarget target;
            try { target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup); }
            catch (System.Exception) { return; }   // a platform group without a named target: nothing to check
            if (target != NamedBuildTarget.Android && target != NamedBuildTarget.iOS && target != NamedBuildTarget.Standalone) return;

            string id = PlayerSettings.GetApplicationIdentifier(target);
            if (id != PotionPopBuilder.BundleId)
                throw new BuildFailedException(
                    $"Application identifier is '{id}', expected '{PotionPopBuilder.BundleId}'. " +
                    "Run Potion Pop > Rebuild Project (or build from the Potion Pop/Build menu) to apply the player settings.");

            bool release = (report.summary.options & BuildOptions.Development) == 0;
            if (release && target != NamedBuildTarget.Standalone) CheckServicesConfig(target == NamedBuildTarget.iOS);
        }

        const string TestPublisher = "ca-app-pub-3940256099942544";

        /// <summary>Only the ad ids of the platform being built: an Android release may ship before the iOS AdMob app exists.</summary>
        static void CheckServicesConfig(bool ios)
        {
            var config = AssetDatabase.LoadAssetAtPath<PotionPop.Services.ServicesConfig>(PotionPopBuilder.ServicesConfigPath);
            if (config == null)
                throw new BuildFailedException(PotionPopBuilder.ServicesConfigPath + " does not load as a ServicesConfig (missing, or its script reference is broken).");
            if (!config.FirebaseConfigured)
                throw new BuildFailedException("ServicesConfig: Firebase API key / project id are empty (see Docs/Firebase-Setup.md).");
            string[] ids = ios
                ? new[] { config.admobIosAppId, config.bannerIos, config.interstitialIos, config.rewardedIos }
                : new[] { config.admobAndroidAppId, config.bannerAndroid, config.interstitialAndroid, config.rewardedAndroid };
            foreach (string adId in ids)
                if (string.IsNullOrEmpty(adId) || adId.StartsWith(TestPublisher))
                    throw new BuildFailedException("ServicesConfig still has a Google test AdMob id: " + adId);
        }
    }
}
