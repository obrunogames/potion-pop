using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Player builds (menu Potion Pop/Build/...). Every build first runs the project rebuild (font, scene, player
    /// settings, gradle templates, AdMob ids) so it never ships stale settings. Command line:
    ///   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod PotionPop.EditorTools.BuildScript.BuildAndroidAab
    ///   Unity -batchmode -quit -projectPath . -buildTarget iOS     -executeMethod PotionPop.EditorTools.BuildScript.BuildIOS
    /// Options: -buildOutput &lt;path&gt;, -development. Exit code 1 on failure in batch mode.
    /// The target platform must be the active one (batch: pass -buildTarget): editor-side build processors of the
    /// plugins are compiled per platform (#if UNITY_ANDROID / UNITY_IOS: AdMob manifest + gradle, Info.plist, Sign in
    /// with Apple), so building for another platform than the active one would silently skip them. From the menu the
    /// build offers to switch platform and continues by itself after the script reload.
    /// Release signing (Android) comes from environment variables, never from files in the repo:
    ///   POTIONPOP_KEYSTORE (path), POTIONPOP_KEYSTORE_PASS, POTIONPOP_KEY_ALIAS, POTIONPOP_KEY_PASS.
    /// </summary>
    public static class BuildScript
    {
        public enum BuildKind { AndroidApk, AndroidAab, IOS, MacOS }

        /// <summary>SessionState key of a build waiting for a platform switch: "kind|development|ticks|output".</summary>
        internal const string PendingBuildKey = "PotionPop.PendingBuild";

        [MenuItem("Potion Pop/Build/Android APK", priority = 100)]
        public static void BuildAndroidApk() => Run(BuildKind.AndroidApk);

        [MenuItem("Potion Pop/Build/Android AAB (Google Play)", priority = 101)]
        public static void BuildAndroidAab() => Run(BuildKind.AndroidAab);

        [MenuItem("Potion Pop/Build/iOS (Xcode project)", priority = 102)]
        public static void BuildIOS() => Run(BuildKind.IOS);

        [MenuItem("Potion Pop/Build/macOS (test build)", priority = 103)]
        public static void BuildMacOS() => Run(BuildKind.MacOS);

        [MenuItem("Potion Pop/Build/Android APK", true)]
        [MenuItem("Potion Pop/Build/Android AAB (Google Play)", true)]
        [MenuItem("Potion Pop/Build/iOS (Xcode project)", true)]
        [MenuItem("Potion Pop/Build/macOS (test build)", true)]
        static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;

        public static string DefaultOutput(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.AndroidApk: return "Builds/Android/PotionPop.apk";
                case BuildKind.AndroidAab: return "Builds/Android/PotionPop.aab";
                case BuildKind.IOS: return "Builds/iOS";
                default: return "Builds/macOS/PotionPop.app";
            }
        }

        public static BuildTarget TargetOf(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.AndroidApk:
                case BuildKind.AndroidAab: return BuildTarget.Android;
                case BuildKind.IOS: return BuildTarget.iOS;
                default: return BuildTarget.StandaloneOSX;
            }
        }

        static void Run(BuildKind kind)
        {
            bool ok;
            try { ok = Build(kind, EditorUtil.GetArg("-buildOutput"), EditorUtil.HasArg("-development")); }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Builds the player. Returns false on failure (details in the Console).</summary>
        public static bool Build(BuildKind kind, string outputPath = null, bool development = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(EditorUtil.LogPrefix + "Exit Play Mode before building.");
                return false;
            }
            var target = TargetOf(kind);
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                Debug.LogError(EditorUtil.LogPrefix + $"{target} Build Support is not installed in this Unity editor.");
                return false;
            }

            // Plugin build processors (and iOSPostProcess) are compiled for the active platform only.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError(EditorUtil.LogPrefix + $"Start the editor with -buildTarget {BatchTargetName(kind)} for {kind} builds: the " +
                                   "platform-specific build processors (AdMob, Info.plist, Sign in with Apple) only compile on the active platform.");
                    return false;
                }
                if (!EditorUtility.DisplayDialog("Potion Pop", $"{kind} builds need the {target} platform active. Switch now? " +
                                                              "The build starts by itself after the switch.", "Switch platform", "Cancel"))
                    return false;
                SessionState.SetString(PendingBuildKey, string.Join("|", ((int)kind).ToString(CultureInfo.InvariantCulture),
                    development ? "1" : "0", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture), outputPath ?? ""));
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    SessionState.EraseString(PendingBuildKey);
                    Debug.LogError(EditorUtil.LogPrefix + $"Could not switch to the {target} platform.");
                    return false;
                }
                return true; // PendingBuildResumer continues after the domain reload
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (!PotionPopBuilder.Rebuild())
            {
                Debug.LogError(EditorUtil.LogPrefix + "Build aborted: the project rebuild failed.");
                return false;
            }

            string path = string.IsNullOrEmpty(outputPath) ? DefaultOutput(kind) : outputPath;
            string folder = kind == BuildKind.IOS ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError(EditorUtil.LogPrefix + "No enabled scene in the build settings.");
                return false;
            }

            AndroidSigning signing = null;
            if (target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = kind == BuildKind.AndroidAab;
                signing = AndroidSigning.Configure(kind == BuildKind.AndroidAab);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                targetGroup = group,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var watch = Stopwatch.StartNew();
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(options); }
            finally { signing?.Restore(); }
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(EditorUtil.LogPrefix + $"{kind} build {summary.result} ({summary.totalErrors} error(s)) after {watch.Elapsed.TotalSeconds:0} s.");
                return false;
            }

            Debug.Log(EditorUtil.LogPrefix + $"{kind} build succeeded in {watch.Elapsed.TotalSeconds:0} s → {path}\n" +
                      SizeReport(kind, path, report));
            return true;
        }

        static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && File.Exists(scene.path)) list.Add(scene.path);
            return list.ToArray();
        }

        /// <summary>Platform name for the -buildTarget command line option.</summary>
        static string BatchTargetName(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.IOS: return "iOS";
                case BuildKind.MacOS: return "OSXUniversal";
                default: return "Android";
            }
        }

        // ------------------------------------------------------------------ android signing

        /// <summary>
        /// Release signing from POTIONPOP_KEYSTORE / POTIONPOP_KEYSTORE_PASS / POTIONPOP_KEY_ALIAS / POTIONPOP_KEY_PASS for the
        /// duration of one build. The previous Player Settings are restored afterwards, so the keystore path never ends
        /// up in ProjectSettings.asset (git) and later builds without the variables do not fail on missing passwords.
        /// </summary>
        sealed class AndroidSigning
        {
            bool _use;
            string _keystore, _alias, _storePass, _keyPass;

            public static AndroidSigning Configure(bool release)
            {
                var previous = new AndroidSigning
                {
                    _use = PlayerSettings.Android.useCustomKeystore,
                    _keystore = PlayerSettings.Android.keystoreName,
                    _alias = PlayerSettings.Android.keyaliasName,
                    _storePass = PlayerSettings.Android.keystorePass,
                    _keyPass = PlayerSettings.Android.keyaliasPass,
                };

                string keystore = Environment.GetEnvironmentVariable("POTIONPOP_KEYSTORE");
                string storePass = Environment.GetEnvironmentVariable("POTIONPOP_KEYSTORE_PASS");
                string alias = Environment.GetEnvironmentVariable("POTIONPOP_KEY_ALIAS");
                string keyPass = Environment.GetEnvironmentVariable("POTIONPOP_KEY_PASS");
                bool complete = !string.IsNullOrEmpty(keystore) && !string.IsNullOrEmpty(storePass) &&
                                !string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(keyPass);
                if (complete && File.Exists(keystore))
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = Path.GetFullPath(keystore);
                    PlayerSettings.Android.keystorePass = storePass;
                    PlayerSettings.Android.keyaliasName = alias;
                    PlayerSettings.Android.keyaliasPass = keyPass;
                    Debug.Log(EditorUtil.LogPrefix + "Android signing: release keystore from POTIONPOP_KEYSTORE.");
                    return previous;
                }
                if (complete) Debug.LogWarning(EditorUtil.LogPrefix + "POTIONPOP_KEYSTORE points to a missing file: " + keystore);

                // A keystore configured by hand in Player Settings is kept when its passwords were entered this session.
                bool manualKeystoreUsable = previous._use && !string.IsNullOrEmpty(previous._keystore) && File.Exists(previous._keystore) &&
                                            !string.IsNullOrEmpty(previous._storePass) && !string.IsNullOrEmpty(previous._keyPass);
                if (manualKeystoreUsable) return previous;
                if (previous._use)
                {
                    Debug.LogWarning(EditorUtil.LogPrefix + "Custom keystore enabled without passwords: this build uses the debug keystore.");
                    PlayerSettings.Android.useCustomKeystore = false;
                }
                if (release)
                    Debug.LogWarning(EditorUtil.LogPrefix + "AAB signed with the DEBUG keystore: Google Play will reject it. " +
                                     "Set POTIONPOP_KEYSTORE / POTIONPOP_KEYSTORE_PASS / POTIONPOP_KEY_ALIAS / POTIONPOP_KEY_PASS.");
                return previous;
            }

            public void Restore()
            {
                PlayerSettings.Android.useCustomKeystore = _use;
                PlayerSettings.Android.keystoreName = _keystore ?? "";
                PlayerSettings.Android.keyaliasName = _alias ?? "";
                PlayerSettings.Android.keystorePass = _storePass ?? "";
                PlayerSettings.Android.keyaliasPass = _keyPass ?? "";
            }
        }

        // ------------------------------------------------------------------ size report

        static string SizeReport(BuildKind kind, string path, BuildReport report)
        {
            long output = -1;
            if (File.Exists(path)) output = new FileInfo(path).Length;
            else if (kind == BuildKind.MacOS && Directory.Exists(path)) output = DirectorySize(path);

            var lines = new List<string>();
            lines.Add(output >= 0
                ? $"Size: {EditorUtil.FormatBytes(output)} on disk (player data {EditorUtil.FormatBytes((long)report.summary.totalSize)})."
                : $"Player data: {EditorUtil.FormatBytes((long)report.summary.totalSize)} (Xcode project; the App Store size depends on app thinning).");

            // Largest assets (helps keep the download small).
            var bySource = new Dictionary<string, ulong>();
            foreach (var packed in report.packedAssets)
                foreach (var content in packed.contents)
                {
                    string source = string.IsNullOrEmpty(content.sourceAssetPath) ? "(built-in)" : content.sourceAssetPath;
                    bySource.TryGetValue(source, out ulong size);
                    bySource[source] = size + content.packedSize;
                }
            var top = new List<KeyValuePair<string, ulong>>(bySource);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (top.Count > 0) lines.Add("Largest assets:");
            for (int i = 0; i < top.Count && i < 10; i++)
                lines.Add($"  {EditorUtil.FormatBytes((long)top[i].Value),10}  {top[i].Key}");
            return string.Join("\n", lines);
        }

        static long DirectorySize(string path)
        {
            long total = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
            }
            return total;
        }
    }

    /// <summary>Continues a build requested from the menu once the platform switch has reloaded the scripts.</summary>
    [InitializeOnLoad]
    static class PendingBuildResumer
    {
        static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

        static PendingBuildResumer()
        {
            if (Application.isBatchMode || string.IsNullOrEmpty(SessionState.GetString(BuildScript.PendingBuildKey, ""))) return;
            EditorApplication.delayCall += Resume;
        }

        static void Resume()
        {
            string pending = SessionState.GetString(BuildScript.PendingBuildKey, "");
            if (string.IsNullOrEmpty(pending)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Resume;
                return;
            }
            SessionState.EraseString(BuildScript.PendingBuildKey);
            string[] parts = pending.Split(new[] { '|' }, 4);
            if (parts.Length < 4 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ||
                !Enum.IsDefined(typeof(BuildScript.BuildKind), k) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ||
                DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > MaxAge)
                return;
            var kind = (BuildScript.BuildKind)k;
            if (EditorUserBuildSettings.activeBuildTarget != BuildScript.TargetOf(kind)) return; // switch cancelled / failed
            BuildScript.Build(kind, parts[3].Length > 0 ? parts[3] : null, parts[1] == "1");
        }
    }
}
