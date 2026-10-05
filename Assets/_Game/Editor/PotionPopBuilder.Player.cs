using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PotionPop.EditorTools
{
    public static partial class PotionPopBuilder
    {
        public const string CompanyName = "Bruno Games";
        public const string ProductName = "Potion Pop!";
        public const string BundleId = "br.com.brunogames.potionpop";
        public const string InitialVersion = "1.0.0";
        public const string AppIconPath = "Assets/_Game/Art/AppIcon/app_icon.png";
        /// <summary>GDD target. Raised automatically to the engine minimum when Unity requires more (see IosTargetVersion).</summary>
        public const string DesiredIosTarget = "13.0";
        public const string AndroidPluginsFolder = "Assets/Plugins/Android";
        /// <summary>GDD target. Raised automatically to the engine minimum when Unity requires more (see AndroidMinSdk).</summary>
        public const int DesiredAndroidMinSdk = 24;
        public const int AndroidTargetSdk = 36;   // Google Play's required target API level
        const int InputSystemOnly = 1; // ProjectSettings activeInputHandler: 0 legacy, 1 Input System, 2 both

        // ------------------------------------------------------------------ 5) player settings

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            // No Unity hardware statistics: the privacy policy says the game has no analytics. The property is not public
            // API (Player Settings › "Hardware Statistics"), so it is set through reflection when available.
            try
            {
                var prop = typeof(PlayerSettings).GetProperty("submitAnalytics",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite) prop.SetValue(null, false);
            }
            catch (System.Exception e) { Debug.LogWarning(EditorUtil.LogPrefix + "Could not turn off hardware statistics: " + e.Message); }
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone })
                PlayerSettings.SetApplicationIdentifier(target, BundleId);

            // Versions are initialised once and never moved backwards: release bumps are done by hand / CI.
            if (IsUnsetVersion(PlayerSettings.bundleVersion)) PlayerSettings.bundleVersion = InitialVersion;
            if (PlayerSettings.Android.bundleVersionCode < 1) PlayerSettings.Android.bundleVersionCode = 1;
            if (!int.TryParse(PlayerSettings.iOS.buildNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out int build) || build < 1)
                PlayerSettings.iOS.buildNumber = "1";

            // Portrait only, no autorotation.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.statusBarHidden = true;
            // A portrait-only app that allows iPad Split View is rejected by App Store Connect (ITMS-90474).
            PlayerSettings.iOS.requiresFullScreen = true;

            ApplyAppIcons();

            // GameRoot draws its own animated splash on the first frame: the engine splash only provides the
            // background color (also used by the iOS launch screen) instead of adding a second logo screen.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = BackgroundColor;

            // Android
            PlayerSettings.Android.minSdkVersion = AndroidMinSdk();
            // Pinned instead of "highest installed": a newer SDK in the editor must not change runtime behavior untested.
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)AndroidTargetSdk;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

            // iOS
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // 1 = ARM64
            PlayerSettings.iOS.targetOSVersionString = IosTargetVersion();
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);

            // macOS / desktop test builds: a phone-shaped resizable window.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 1170;
            PlayerSettings.resizableWindow = true;

            // Gamma: the UI is authored as sRGB colors and alpha-blended sprites; linear blending would shift every
            // semi-transparent color of the design system. Changing it reimports all textures, so only when needed.
            if (PlayerSettings.colorSpace != ColorSpace.Gamma) PlayerSettings.colorSpace = ColorSpace.Gamma;

            if (SetActiveInputHandler(InputSystemOnly)) RestartRequired = true;
            AssetDatabase.SaveAssets();
        }

        static bool IsUnsetVersion(string version) =>
            string.IsNullOrWhiteSpace(version) || version == "0.1" || version == "1.0" || version == "0.0";

        /// <summary>Sets ProjectSettings' activeInputHandler (no public API). Returns true if it changed.</summary>
        static bool SetActiveInputHandler(int value)
        {
            Object playerSettings = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset"))
                if (asset is PlayerSettings) { playerSettings = asset; break; }
            if (playerSettings == null) throw new InvalidOperationException("ProjectSettings/ProjectSettings.asset not found.");

            var so = new SerializedObject(playerSettings);
            var property = so.FindProperty("activeInputHandler");
            if (property == null) throw new InvalidOperationException("PlayerSettings.activeInputHandler property not found.");
            if (property.intValue == value) return false;
            property.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(EditorUtil.LogPrefix + "Active input handling set to the Input System package (restart Unity to apply it in the editor).");
            return true;
        }

        /// <summary>
        /// DesiredAndroidMinSdk, or the lowest API level this Unity version still supports when it is higher (Unity marks
        /// the unsupported AndroidSdkVersions values [Obsolete]; 6000.6 requires 26).
        /// </summary>
        public static AndroidSdkVersions AndroidMinSdk()
        {
            int engineMinimum = int.MaxValue;
            foreach (var field in typeof(AndroidSdkVersions).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.Name.StartsWith("AndroidApiLevel", StringComparison.Ordinal) || field.IsDefined(typeof(ObsoleteAttribute), false)) continue;
                int level = Convert.ToInt32(field.GetValue(null), CultureInfo.InvariantCulture);
                if (level > 0 && level < engineMinimum) engineMinimum = level; // AndroidApiLevelAuto = 0
            }
            if (engineMinimum == int.MaxValue) engineMinimum = DesiredAndroidMinSdk;
            return (AndroidSdkVersions)Math.Max(DesiredAndroidMinSdk, engineMinimum);
        }

        /// <summary>DesiredIosTarget, or the engine's minimum deployment target when it is higher.</summary>
        public static string IosTargetVersion()
        {
            string engineMinimum = IosEngineMinimum();
            return engineMinimum != null && CompareVersions(engineMinimum, DesiredIosTarget) > 0 ? engineMinimum : DesiredIosTarget;
        }

        /// <summary>Deployment target of Unity's own Xcode template (the lowest iOS this Unity version builds for).</summary>
        static string IosEngineMinimum()
        {
            try
            {
                string engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.iOS, BuildOptions.None);
                string pbx = Path.Combine(engine, "Trampoline", "Unity-iPhone.xcodeproj", "project.pbxproj");
                if (!File.Exists(pbx)) return null;
                string best = null;
                foreach (Match m in Regex.Matches(File.ReadAllText(pbx), @"IPHONEOS_DEPLOYMENT_TARGET = ""?([0-9.]+)""?;"))
                    if (best == null || CompareVersions(m.Groups[1].Value, best) > 0) best = m.Groups[1].Value;
                return best;
            }
            catch (Exception) { return null; } // iOS Build Support not installed
        }

        static int CompareVersions(string a, string b)
        {
            string[] pa = a.Split('.'), pb = b.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        // ------------------------------------------------------------------ 6) android gradle templates

        /// <summary>
        /// EDM4U (External Dependency Manager) injects the Google Mobile Ads / UMP dependencies into the custom
        /// mainTemplate.gradle and needs AndroidX + Jetifier in gradleTemplate.properties. Existing templates are kept.
        /// </summary>
        static void EnsureGradleTemplates()
        {
            string source = FindGradleTemplatesFolder();
            if (source == null)
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "Android Build Support not found: gradle templates skipped.");
                return;
            }

            bool changed = false;
            Directory.CreateDirectory(AndroidPluginsFolder);
            foreach (string file in new[] { "mainTemplate.gradle", "gradleTemplate.properties" })
            {
                string target = Path.Combine(AndroidPluginsFolder, file);
                if (File.Exists(target)) continue;
                string from = Path.Combine(source, file);
                if (!File.Exists(from)) throw new FileNotFoundException("Unity gradle template missing", from);
                File.Copy(from, target);
                changed = true;
                Debug.Log(EditorUtil.LogPrefix + "Enabled custom " + file);
            }

            changed |= EnsureGradleProperties(Path.Combine(AndroidPluginsFolder, "gradleTemplate.properties"),
                new KeyValuePair<string, string>("android.useAndroidX", "true"),
                new KeyValuePair<string, string>("android.enableJetifier", "true"));
            if (changed) AssetDatabase.Refresh();
        }

        static string FindGradleTemplatesFolder()
        {
            var candidates = new List<string>();
            try { candidates.Add(Path.Combine(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None), "Tools", "GradleTemplates")); }
            catch (Exception) { /* Android module not installed */ }
            const string relative = "PlaybackEngines/AndroidPlayer/Tools/GradleTemplates";
            candidates.Add(Path.Combine(EditorApplication.applicationContentsPath, relative));                        // Windows/Linux: Editor/Data
            candidates.Add(Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath) ?? "", relative));   // macOS: next to Unity.app
            candidates.Add("/Applications/Unity/Hub/Editor/" + Application.unityVersion + "/" + relative);
            foreach (string dir in candidates)
                if (File.Exists(Path.Combine(dir, "mainTemplate.gradle"))) return dir;
            return null;
        }

        /// <summary>Sets key=value lines (replacing other values); new keys go before Unity's ADDITIONAL_PROPERTIES marker.</summary>
        static bool EnsureGradleProperties(string path, params KeyValuePair<string, string>[] properties)
        {
            if (!File.Exists(path)) return false;
            var lines = new List<string>(File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            bool changed = false;
            foreach (var property in properties)
            {
                string wanted = property.Key + "=" + property.Value;
                int found = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == property.Key) { found = i; break; }
                }
                if (found >= 0)
                {
                    if (lines[found].Trim() == wanted) continue;
                    lines[found] = wanted;
                }
                else
                {
                    int marker = lines.FindIndex(l => l.Contains("**ADDITIONAL_PROPERTIES**"));
                    lines.Insert(marker >= 0 ? marker : lines.Count, wanted);
                }
                changed = true;
            }
            if (changed) File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return changed;
        }

        // ------------------------------------------------------------------ 7) google mobile ads settings

        /// <summary>
        /// Copies the AdMob app ids of ServicesConfig into the plugin's GoogleMobileAdsSettings (internal class, so by
        /// reflection; skipped gracefully when the plugin or its members are missing) and keeps its ATT text empty.
        /// </summary>
        static void ApplyGoogleMobileAdsSettings()
        {
            var config = LoadServicesConfig();
            if (config == null)
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "ServicesConfig missing: AdMob app ids not applied.");
                return;
            }
            var type = EditorUtil.FindType("GoogleMobileAds.Editor.GoogleMobileAdsSettings");
            var load = type?.GetMethod("LoadInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (load == null)
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "GoogleMobileAdsSettings not found (Google Mobile Ads plugin missing?): AdMob app ids not applied.");
                return;
            }
            if (!(load.Invoke(null, null) is Object settings) || settings == null)
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "GoogleMobileAdsSettings.LoadInstance returned nothing: AdMob app ids not applied.");
                return;
            }

            bool changed = SetStringProperty(settings, "GoogleMobileAdsAndroidAppId", config.admobAndroidAppId);
            changed |= SetStringProperty(settings, "GoogleMobileAdsIOSAppId", config.admobIosAppId);
            // The game never requests tracking (no ATT prompt; ads on iOS are served without the IDFA): with an empty
            // text the plugin does not declare NSUserTrackingUsageDescription in Info.plist.
            changed |= SetStringProperty(settings, "UserTrackingUsageDescription", "");
            if (!changed) return;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log(EditorUtil.LogPrefix + "Google Mobile Ads settings updated from ServicesConfig.");
        }

        static bool SetStringProperty(object target, string name, string value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "GoogleMobileAdsSettings." + name + " not found: skipped.");
                return false;
            }
            value ??= "";
            if ((property.GetValue(target) as string ?? "") == value) return false;
            property.SetValue(target, value);
            return true;
        }
    }
}
