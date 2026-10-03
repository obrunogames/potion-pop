#if UNITY_IOS
using System;
using System.IO;
using System.Text;
using PotionPop.Services;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
#if SP_APPLE_AUTH
using AppleAuth.Editor;
#endif

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Finishes the generated Xcode project (runs after the AdMob plist processor and the CocoaPods install):
    /// * Info.plist: GIDClientID (+ GIDServerClientID) and the reversed-client-id URL scheme for Google Sign-In when
    ///   configured in ServicesConfig, CFBundleLocalizations (en, pt, es), ITSAppUsesNonExemptEncryption = false (HTTPS
    ///   only), and NO NSUserTrackingUsageDescription: the app declares no tracking and never shows the ATT prompt (ads
    ///   on iOS are served without the IDFA), so the key is removed if any plugin added it;
    /// * en/pt/es .lproj folders with InfoPlist.strings (the display name; that is also how the App Store lists the
    ///   languages);
    /// * Sign in with Apple capability (AppleAuth package, compatibility mode);
    /// * -ObjC in UnityFramework's OTHER_LDFLAGS (Google SDK categories);
    /// * Podfile: pods raised to the app's deployment target; Xcode 15+/26 script sandboxing and module verifier off
    ///   (they reject Unity's IL2CPP build phase / UnityFramework).
    /// </summary>
    public static class iOSPostProcess
    {
        static readonly string[] Languages = { "en", "pt", "es" };
        const string TrackingUsageKey = "NSUserTrackingUsageDescription";
        const string PodMarker = "# Potion Pop: pods use at least the app's iOS deployment target";
        const string DefaultEntitlements = "Unity-iPhone/PotionPop.entitlements";

        // Between EDM4U's Podfile generation (40) and `pod install` (50).
        [PostProcessBuild(45)]
        public static void FixPodfile(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string podfile = Path.Combine(path, "Podfile");
            if (!File.Exists(podfile)) return;
            string text = File.ReadAllText(podfile);
            if (text.Contains(PodMarker)) return;
            if (text.Contains("post_install"))
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "Podfile already has a post_install hook: raise the pods' IPHONEOS_DEPLOYMENT_TARGET there.");
                return;
            }
            string minimum = (PlayerSettings.iOS.targetOSVersionString ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(minimum, @"^\d+(\.\d+){0,2}$"))
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "Unexpected iOS target version '" + minimum + "': Podfile left unchanged.");
                return;
            }
            // Gem::Version compares "15.0" / "16.4.1" correctly (to_f would not parse three-part versions).
            text = text.TrimEnd() + "\n\n" + PodMarker + "\n" +
                   "post_install do |installer|\n" +
                   "  installer.pods_project.targets.each do |target|\n" +
                   "    target.build_configurations.each do |config|\n" +
                   "      if Gem::Version.new(config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'].to_s) < Gem::Version.new('" + minimum + "')\n" +
                   "        config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '" + minimum + "'\n" +
                   "      end\n" +
                   "    end\n" +
                   "  end\n" +
                   "end\n";
            File.WriteAllText(podfile, text);
        }

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var config = PotionPopBuilder.LoadServicesConfig();
            if (config == null) Debug.LogWarning(EditorUtil.LogPrefix + "ServicesConfig missing: Google Sign-In keys not written to Info.plist.");

            UpdateInfoPlist(path, config);
            string projectPath = PBXProject.GetPBXProjectPath(path);
            UpdateProject(path, projectPath);
            AddSignInWithApple(path, projectPath);
            Debug.Log(EditorUtil.LogPrefix + "Xcode project post-processed: " + path);
        }

        // ------------------------------------------------------------------ Info.plist

        static void UpdateInfoPlist(string buildPath, ServicesConfig config)
        {
            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;

            string iosClientId = config != null ? (config.googleIosClientId ?? "").Trim() : "";
            if (iosClientId.Length > 0)
            {
                root.SetString("GIDClientID", iosClientId);
                string webClientId = (config.googleWebClientId ?? "").Trim();
                if (webClientId.Length > 0) root.SetString("GIDServerClientID", webClientId); // ID token audience for Firebase
                AddUrlScheme(root, "google-signin", ReversedClientId(iosClientId));
            }

            // No tracking, no ATT prompt: an ATT text without the request would contradict the App Privacy answers.
            if (root.values.Remove(TrackingUsageKey))
                Debug.Log(EditorUtil.LogPrefix + TrackingUsageKey + " removed from Info.plist (the app does not request tracking).");

            root.SetString("CFBundleDevelopmentRegion", Languages[0]);
            var localizations = root.CreateArray("CFBundleLocalizations"); // replaces any previous array
            foreach (string language in Languages) localizations.AddString(language);
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            plist.WriteToFile(plistPath);
        }

        /// <summary>"123-abc.apps.googleusercontent.com" → "com.googleusercontent.apps.123-abc".</summary>
        public static string ReversedClientId(string clientId)
        {
            string[] parts = clientId.Split('.');
            Array.Reverse(parts);
            if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
                Debug.LogWarning(EditorUtil.LogPrefix + "googleIosClientId does not look like an OAuth iOS client id: " + clientId);
            return string.Join(".", parts);
        }

        static void AddUrlScheme(PlistElementDict root, string name, string scheme)
        {
            var types = root["CFBundleURLTypes"] as PlistElementArray ?? root.CreateArray("CFBundleURLTypes");
            foreach (var element in types.values)
            {
                if (!(element is PlistElementDict dict) || !(dict["CFBundleURLSchemes"] is PlistElementArray schemes)) continue;
                foreach (var s in schemes.values)
                    if (s is PlistElementString str && str.value == scheme) return; // already there (append builds)
            }
            var entry = types.AddDict();
            entry.SetString("CFBundleTypeRole", "Editor");
            entry.SetString("CFBundleURLName", name);
            entry.CreateArray("CFBundleURLSchemes").AddString(scheme);
        }

        // ------------------------------------------------------------------ Xcode project

        static void UpdateProject(string buildPath, string projectPath)
        {
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string main = project.GetUnityMainTargetGuid();
            string framework = project.GetUnityFrameworkTargetGuid();

            // Google SDKs ship Objective-C categories: without -ObjC the linker strips them (runtime crashes).
            string ldFlags = project.GetBuildPropertyForAnyConfig(framework, "OTHER_LDFLAGS") ?? "";
            if (Array.IndexOf(ldFlags.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries), "-ObjC") < 0)
                project.AddBuildProperty(framework, "OTHER_LDFLAGS", "-ObjC");

            foreach (string guid in new[] { project.ProjectGuid(), main, framework, project.TargetGuidByName("GameAssembly") })
            {
                if (string.IsNullOrEmpty(guid)) continue;
                project.SetBuildProperty(guid, "ENABLE_USER_SCRIPT_SANDBOXING", "NO");
                project.SetBuildProperty(guid, "ENABLE_MODULE_VERIFIER", "NO");
            }

            // One .lproj per language with the display name (that is how the App Store lists the languages).
            project.SetDevelopmentRegion(Languages[0]);
            project.AddKnownRegion("Base");
            foreach (string language in Languages)
            {
                project.AddKnownRegion(language);
                string folder = language + ".lproj";
                Directory.CreateDirectory(Path.Combine(buildPath, folder));
                var sb = new StringBuilder();
                sb.Append("\"CFBundleDisplayName\" = \"").Append(Escape(PotionPopBuilder.ProductName)).Append("\";\n");
                // UTF-16 with BOM: read by every iOS version without Xcode converting it.
                File.WriteAllText(Path.Combine(buildPath, folder, "InfoPlist.strings"), sb.ToString(), Encoding.Unicode);
                if (project.FindFileGuidByProjectPath(folder) != null) continue; // append builds keep the reference
                string fileGuid = project.AddFolderReference(folder, folder, PBXSourceTree.Source);
                project.AddFileToBuildSection(main, project.GetResourcesBuildPhaseByTarget(main), fileGuid);
            }
            project.WriteToFile(projectPath);
        }

        static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        // ------------------------------------------------------------------ Sign in with Apple

        static void AddSignInWithApple(string buildPath, string projectPath)
        {
#if SP_APPLE_AUTH
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string main = project.GetUnityMainTargetGuid();
            string entitlements = project.GetBuildPropertyForAnyConfig(main, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlements)) entitlements = DefaultEntitlements;

            string entitlementsFile = Path.Combine(buildPath, entitlements);
            if (File.Exists(entitlementsFile) && File.ReadAllText(entitlementsFile).Contains("com.apple.developer.applesignin"))
                return; // append build: capability already present

            var manager = new ProjectCapabilityManager(projectPath, entitlements, null, main);
            manager.AddSignInWithAppleWithCompatibility();
            manager.WriteToFile();
#else
            Debug.LogWarning(EditorUtil.LogPrefix + "AppleAuth package missing: Sign in with Apple capability not added.");
#endif
        }
    }
}
#endif
