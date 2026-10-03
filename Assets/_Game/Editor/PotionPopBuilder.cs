using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using PotionPop.Services;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Sets up everything that is not hand-written code: TMP resources and the Lilita One font asset, the services
    /// config, the Main scene, player settings, Android gradle templates and the AdMob settings.
    /// Every step is idempotent (safe to run any number of times) and keeps hand-made changes where it can.
    /// Menu: Potion Pop/Rebuild Project. Command line:
    ///   Unity -batchmode -quit -projectPath . -executeMethod PotionPop.EditorTools.PotionPopBuilder.RebuildBatch
    /// </summary>
    public static partial class PotionPopBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        public const string FontSourcePath = "Assets/_Game/Fonts/LilitaOne-Regular.ttf";
        public const string FontAssetPath = "Assets/_Game/Resources/Fonts/LilitaOne SDF.asset";
        public const string ServicesConfigPath = "Assets/_Game/Resources/ServicesConfig.asset";
        public const string GameRootTypeName = "PotionPop.GameRoot";
        const string GameRootObjectName = "GameRoot";
        const string TmpEssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        /// <summary>#2A1650: camera clear color and splash background (deep plum behind every screen).</summary>
        public static readonly Color BackgroundColor = new Color32(0x2A, 0x16, 0x50, 0xFF);

        const string RestartRequiredKey = "PotionPop.RestartRequired";

        /// <summary>
        /// True when a rebuild in this editor session changed a setting that only applies after an editor restart
        /// (active input handling). Kept in SessionState: survives script reloads, cleared by the restart itself.
        /// </summary>
        public static bool RestartRequired
        {
            get => SessionState.GetBool(RestartRequiredKey, false);
            private set => SessionState.SetBool(RestartRequiredKey, value);
        }

        // ------------------------------------------------------------------ menu / batch

        [MenuItem("Potion Pop/Rebuild Project", priority = 0)]
        public static void RebuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ReportToUser(Rebuild());
        }

        /// <summary>Editor dialogs after an interactive rebuild (errors, background TMP import, restart needed).</summary>
        internal static void ReportToUser(bool ok)
        {
            if (Application.isBatchMode) return;
            if (!ok && s_lastRebuildPaused && PotionPopRebuildResumer.IsPending)
            {
                EditorUtility.DisplayDialog("Potion Pop", "TMP Essential Resources are being imported. The rebuild finishes by itself " +
                                                         "in a few seconds (see the Console).", "OK");
                return;
            }
            if (!ok)
            {
                EditorUtility.DisplayDialog("Potion Pop", "Rebuild finished with errors. See the Console for details.", "OK");
                return;
            }
            if (RestartRequired &&
                EditorUtility.DisplayDialog("Potion Pop",
                    "Active input handling was switched to the Input System package. Unity must restart before entering Play Mode.",
                    "Restart now", "Later"))
                EditorApplication.OpenProject(EditorUtil.ProjectRoot);
        }

        [MenuItem("Potion Pop/Rebuild Project", true)]
        [MenuItem("Potion Pop/Open Main Scene", true)]
        static bool NotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Potion Pop/Open Main Scene", priority = 1)]
        public static void OpenMainScene()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogWarning(EditorUtil.LogPrefix + ScenePath + " does not exist yet: run Potion Pop/Rebuild Project.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>Batch entry (-executeMethod). Exits with code 1 when a step fails.</summary>
        public static void RebuildBatch()
        {
            bool ok;
            try { ok = Rebuild(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Runs every setup step. Returns false if any step failed (warnings do not count).</summary>
        public static bool Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(EditorUtil.LogPrefix + "Rebuild is not available in Play Mode.");
                return false;
            }

            var watch = Stopwatch.StartNew();
            var failures = new List<string>();
            s_waitingForTmpImport = false;
            s_lastRebuildPaused = false;

            Step("TMP Essential Resources", EnsureTmpEssentials, failures);
            Step("Lilita One font asset", EnsureFontAsset, failures);
            Step("Services config", EnsureServicesConfig, failures);
            Step("Main scene", BuildMainScene, failures);
            Step("Player settings", ApplyPlayerSettings, failures);
            Step("Android gradle templates", EnsureGradleTemplates, failures);
            Step("Google Mobile Ads settings", ApplyGoogleMobileAdsSettings, failures);

            AssetDatabase.SaveAssets();
            if (failures.Count == 0 && s_waitingForTmpImport)
            {
                s_lastRebuildPaused = true;
                Debug.Log(EditorUtil.LogPrefix + "Rebuild paused: the font step runs when TMP Essential Resources finish importing.");
                return false; // incomplete (no font asset yet): builds must not continue
            }
            if (failures.Count == 0)
            {
                Debug.Log(EditorUtil.LogPrefix + $"Project rebuilt in {watch.Elapsed.TotalSeconds:0.0} s." +
                          (RestartRequired ? " Restart Unity to apply the new input handling." : ""));
                return true;
            }
            Debug.LogError(EditorUtil.LogPrefix + $"Rebuild finished with {failures.Count} failed step(s): {string.Join(", ", failures)}.");
            return false;
        }

        static void Step(string name, Action action, List<string> failures)
        {
            try { action(); }
            catch (Exception e)
            {
                failures.Add(name);
                Debug.LogError(EditorUtil.LogPrefix + $"Rebuild step '{name}' failed: {e.Message}\n{e}");
            }
        }

        // ------------------------------------------------------------------ 1) TMP essential resources

        static void EnsureTmpEssentials()
        {
            if (FindTmpSettings() != null) return;

            string package = ResolvePackageFile(TmpEssentialsPackage);
            if (package == null) throw new FileNotFoundException("TMP Essential Resources package not found", TmpEssentialsPackage);

            Debug.Log(EditorUtil.LogPrefix + "Importing TMP Essential Resources...");
            UnityEditor.AssetPackage.Package.Import(package, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (FindTmpSettings() != null) return;

            // Non-interactive imports complete synchronously in batch mode. In the editor they finish a few frames
            // later (usually with a domain reload): the font step is postponed and PotionPopRebuildResumer runs the
            // rebuild again once the package is in.
            if (Application.isBatchMode)
                throw new InvalidOperationException("TMP Settings not found after importing TMP Essential Resources.");
            s_waitingForTmpImport = true;
            PotionPopRebuildResumer.MarkPending();
            Debug.Log(EditorUtil.LogPrefix + "TMP Essential Resources are importing in the background: the rebuild resumes by itself when they are in.");
        }

        /// <summary>Set by step 1 when the editor imports TMP Essential Resources asynchronously (rebuild incomplete).</summary>
        static bool s_waitingForTmpImport;
        /// <summary>The last Rebuild() returned false only because it waits for the TMP import (no failed step).</summary>
        static bool s_lastRebuildPaused;

        /// <summary>True once TMP Essential Resources (TMP Settings) are in the project.</summary>
        public static bool TmpResourcesPresent => FindTmpSettings() != null;

        static TMP_Settings FindTmpSettings()
        {
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null) return settings;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_Settings"))
            {
                settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null) return settings;
            }
            return null;
        }

        /// <summary>"Packages/&lt;name&gt;/rest" → physical path (registry, embedded or local packages). Null if missing.</summary>
        static string ResolvePackageFile(string virtualPath)
        {
            string[] parts = virtualPath.Split('/');
            if (parts.Length > 2 && parts[0] == "Packages")
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(parts[0] + "/" + parts[1]);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string physical = Path.Combine(info.resolvedPath, string.Join("/", parts, 2, parts.Length - 2));
                    if (File.Exists(physical)) return physical;
                }
            }
            string full = Path.GetFullPath(virtualPath);
            return File.Exists(full) ? full : null;
        }

        // ------------------------------------------------------------------ 2) font asset

        // Sampled at 84 pt with 8 px padding: the same padding/size ratio as TMP's 90/9 default (DS outlines and
        // underlays look identical) while ASCII + every pt/es accent fits in the first 1024² atlas, so common text is
        // always a single draw call. Rarer Latin-1 glyphs may spill into a second atlas (multi-atlas is enabled).
        public const int FontSamplingSize = 84;
        public const int FontPadding = 8;
        public const int FontAtlasSize = 1024;

        /// <summary>ASCII + the accented letters and punctuation used by Portuguese and Spanish (added first).</summary>
        public static string FontCoreCharacters
        {
            get
            {
                var sb = new StringBuilder();
                for (char c = ' '; c <= '~'; c++) sb.Append(c);
                sb.Append("ÁÀÂÃÄÇÉÈÊËÍÌÎÏÑÓÒÔÕÖÚÙÛÜáàâãäçéèêëíìîïñóòôõöúùûü¡¿ºª«»°©®×·");
                sb.Append("–—‘’“”…•€™"); // – — ‘ ’ “ ” … • € ™
                return sb.ToString();
            }
        }

        /// <summary>The rest of Latin-1 (U+00A0..U+00FF, no C1 controls) plus the Windows-1252 extras (added after the core set).</summary>
        public static string FontExtendedCharacters
        {
            get
            {
                var sb = new StringBuilder();
                for (char c = '\u00A0'; c <= '\u00FF'; c++) sb.Append(c);
                sb.Append("\u0152\u0153\u0160\u0161\u0178\u017D\u017E\u0192\u201A\u201E\u2039\u203A\u2030"); // Œ œ Š š Ÿ Ž ž ƒ ‚ „ ‹ › ‰
                return sb.ToString();
            }
        }

        /// <summary>TMP Essentials' dynamic Liberation Sans: draws glyphs Lilita One lacks (player names, rare symbols).</summary>
        const string FallbackFontName = "LiberationSans SDF - Fallback";

        static void EnsureFontAsset()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (font == null) throw new FileNotFoundException("Font not found", FontSourcePath);
            var tmpSettings = FindTmpSettings();
            if (tmpSettings == null)
            {
                if (s_waitingForTmpImport)
                {
                    Debug.Log(EditorUtil.LogPrefix + "Font asset postponed until TMP Essential Resources finish importing.");
                    return;
                }
                throw new InvalidOperationException("TMP Settings missing (TMP Essential Resources not imported).");
            }

            // Dynamic SDF font assets rasterize from the font file at runtime: it must keep its font data.
            if (AssetImporter.GetAtPath(FontSourcePath) is TrueTypeFontImporter fontImporter && !fontImporter.includeFontData)
            {
                fontImporter.includeFontData = true;
                fontImporter.SaveAndReimport();
                font = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            }

            EditorUtil.EnsureFolder(Path.GetDirectoryName(FontAssetPath));
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (asset != null && !FontAssetMatches(asset, font))
            {
                Debug.Log(EditorUtil.LogPrefix + "Font asset settings changed: recreating " + FontAssetPath);
                AssetDatabase.DeleteAsset(FontAssetPath);
                asset = null;
            }
            if (asset == null) asset = CreateFontAsset(font);

            // Core set first so ASCII + accents share the first atlas texture; TryAddCharacters skips known glyphs.
            // (Its "missing" output is useless here: it returns the whole input when nothing new was added.)
            int before = asset.characterTable?.Count ?? 0;
            asset.TryAddCharacters(FontCoreCharacters, out _);
            asset.TryAddCharacters(FontExtendedCharacters, out _);
            PersistFontSubAssets(asset);
            bool dirty = (asset.characterTable?.Count ?? 0) != before;

            // Keep the prepopulated glyphs in builds and across editor restarts (TMP clears dynamic data otherwise).
            var so = new SerializedObject(asset);
            var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null && clear.boolValue)
            {
                clear.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                dirty = true;
            }
            if (!asset.isMultiAtlasTexturesEnabled)
            {
                asset.isMultiAtlasTexturesEnabled = true;
                dirty = true;
            }
            dirty |= EnsureFallbackFont(asset);
            if (dirty) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            if (dirty)
            {
                string missing = MissingGlyphs(asset, FontCoreCharacters + FontExtendedCharacters);
                if (missing.Length > 0)
                    Debug.Log(EditorUtil.LogPrefix + "Glyphs not in Lilita One (drawn by the " + FallbackFontName + " fallback): " + missing);
            }

            // Default font for every TMP text (SerializedObject: does not depend on TMP_Settings' static cache).
            var settingsObject = new SerializedObject(tmpSettings);
            var defaultFont = settingsObject.FindProperty("m_defaultFontAsset");
            if (defaultFont == null) throw new InvalidOperationException("TMP_Settings.m_defaultFontAsset not found.");
            if (defaultFont.objectReferenceValue != asset)
            {
                defaultFont.objectReferenceValue = asset;
                settingsObject.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(tmpSettings);
            }
        }

        /// <summary>Characters of <paramref name="characters"/> the font asset cannot draw itself (whitespace excluded).</summary>
        static string MissingGlyphs(TMP_FontAsset asset, string characters)
        {
            if (asset.HasCharacters(characters, out List<char> missing) || missing == null) return "";
            var sb = new StringBuilder();
            foreach (char c in missing)
                if (!char.IsWhiteSpace(c) && c != '­') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Adds TMP Essentials' dynamic Liberation Sans as fallback (once). Returns true if it changed.</summary>
        static bool EnsureFallbackFont(TMP_FontAsset asset)
        {
            TMP_FontAsset fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != FallbackFontName) continue;
                fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fallback != null) break;
            }
            if (fallback == null || fallback == asset) return false;
            var table = asset.fallbackFontAssetTable;
            if (table == null) asset.fallbackFontAssetTable = table = new List<TMP_FontAsset>();
            table.RemoveAll(f => f == null);
            if (table.Contains(fallback)) return false;
            table.Add(fallback);
            return true;
        }

        static bool FontAssetMatches(TMP_FontAsset asset, Font font)
        {
            return asset.atlasPopulationMode == AtlasPopulationMode.Dynamic
                   && asset.sourceFontFile == font
                   && asset.atlasRenderMode == GlyphRenderMode.SDFAA
                   && asset.atlasPadding == FontPadding
                   && asset.atlasWidth == FontAtlasSize && asset.atlasHeight == FontAtlasSize
                   && Mathf.RoundToInt(asset.faceInfo.pointSize) == FontSamplingSize
                   && asset.material != null
                   && asset.atlasTextures != null && asset.atlasTextures.Length > 0 && asset.atlasTextures[0] != null
                   && AssetDatabase.Contains(asset.atlasTextures[0]);
        }

        static TMP_FontAsset CreateFontAsset(Font font)
        {
            var asset = TMP_FontAsset.CreateFontAsset(font, FontSamplingSize, FontPadding, GlyphRenderMode.SDFAA,
                FontAtlasSize, FontAtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset == null) throw new InvalidOperationException("TMP could not load the font face of " + FontSourcePath);
            asset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
            AssetDatabase.CreateAsset(asset, FontAssetPath);
            PersistFontSubAssets(asset);
            asset.creationSettings = new FontAssetCreationSettings
            {
                sourceFontFileName = font.name,
                sourceFontFileGUID = AssetDatabase.AssetPathToGUID(FontSourcePath),
                pointSizeSamplingMode = 1, // custom size
                pointSize = FontSamplingSize,
                padding = FontPadding,
                atlasWidth = FontAtlasSize,
                atlasHeight = FontAtlasSize,
                characterSetSelectionMode = 7, // custom characters
                characterSequence = FontCoreCharacters + FontExtendedCharacters,
                renderMode = (int)GlyphRenderMode.SDFAA,
            };
            EditorUtility.SetDirty(asset);
            Debug.Log(EditorUtil.LogPrefix + "Created " + FontAssetPath);
            return asset;
        }

        /// <summary>Atlas textures and the material must be sub-assets of the font asset or they are lost on save.</summary>
        static void PersistFontSubAssets(TMP_FontAsset asset)
        {
            var textures = asset.atlasTextures;
            if (textures != null)
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    var texture = textures[i];
                    if (texture == null || AssetDatabase.Contains(texture)) continue;
                    texture.name = asset.name + (i == 0 ? " Atlas" : " Atlas " + i);
                    AssetDatabase.AddObjectToAsset(texture, asset);
                    EditorUtility.SetDirty(asset);
                }
            }
            var material = asset.material;
            if (material != null && !AssetDatabase.Contains(material))
            {
                material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(material, asset);
                EditorUtility.SetDirty(asset);
            }
        }

        // ------------------------------------------------------------------ 3) services config

        static void EnsureServicesConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<ServicesConfig>(ServicesConfigPath) != null) return;
            if (File.Exists(ServicesConfigPath))
            {
                // Never overwrite: it holds real ids. It exists but does not load (script error / not imported yet).
                Debug.LogWarning(EditorUtil.LogPrefix + ServicesConfigPath + " exists but could not be loaded as ServicesConfig; left untouched.");
                return;
            }
            EditorUtil.EnsureFolder(Path.GetDirectoryName(ServicesConfigPath));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ServicesConfig>(), ServicesConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log(EditorUtil.LogPrefix + "Created " + ServicesConfigPath + " (fill in the Firebase / Google / AdMob ids for release).");
        }

        /// <summary>The project's ServicesConfig asset (null before the first rebuild).</summary>
        public static ServicesConfig LoadServicesConfig() => AssetDatabase.LoadAssetAtPath<ServicesConfig>(ServicesConfigPath);

        // ------------------------------------------------------------------ 4) scene

        static void BuildMainScene()
        {
            EditorUtil.EnsureFolder(Path.GetDirectoryName(ScenePath));
            bool exists = File.Exists(ScenePath);
            Scene scene = exists
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = EnsureCamera(scene);
            EnsureSingleAudioListener(scene, camera);
            EnsureEventSystem(scene);
            EnsureGameRoot(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (!exists) Debug.Log(EditorUtil.LogPrefix + "Created " + ScenePath);
        }

        static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        static GameObject NewSceneObject(Scene scene, string name)
        {
            var go = new GameObject(name);
            if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static Camera EnsureCamera(Scene scene)
        {
            Camera camera = null;
            foreach (var c in FindInScene<Camera>(scene))
            {
                if (c.CompareTag("MainCamera")) { camera = c; break; }
                if (camera == null) camera = c;
            }
            if (camera == null) camera = NewSceneObject(scene, "Main Camera").AddComponent<Camera>();

            var go = camera.gameObject;
            go.name = "Main Camera";
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            go.transform.localScale = Vector3.one;

            // The whole game is a Screen Space Overlay canvas: the camera only clears the screen.
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            camera.depth = -1f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            return camera;
        }

        static void EnsureSingleAudioListener(Scene scene, Camera camera)
        {
            AudioListener keep = null;
            foreach (var listener in FindInScene<AudioListener>(scene))
            {
                if (keep == null && listener.gameObject == camera.gameObject) { keep = listener; continue; }
                Object.DestroyImmediate(listener);
            }
            if (keep == null) camera.gameObject.AddComponent<AudioListener>();
        }

        static void EnsureEventSystem(Scene scene)
        {
            var systems = FindInScene<EventSystem>(scene);
            var eventSystem = systems.Count > 0 ? systems[0] : NewSceneObject(scene, "EventSystem").AddComponent<EventSystem>();
            for (int i = 1; i < systems.Count; i++)
            {
                foreach (var module in systems[i].GetComponents<BaseInputModule>()) Object.DestroyImmediate(module);
                Object.DestroyImmediate(systems[i]);
            }

            // Input System only (the legacy Input Manager is disabled): drop StandaloneInputModule & co.
            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                if (!(module is InputSystemUIInputModule)) Object.DestroyImmediate(module);
            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null) inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            if (inputModule.actionsAsset == null) inputModule.AssignDefaultActions();
        }

        static void EnsureGameRoot(Scene scene)
        {
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == GameRootObjectName) { root = go; break; }

            var type = ResolveGameRootType();
            if (type == null)
            {
                if (root == null) root = NewSceneObject(scene, GameRootObjectName);
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                Debug.LogWarning(EditorUtil.LogPrefix + $"Type {GameRootTypeName} not found: the GameRoot object has no component yet. " +
                                 "Run Potion Pop/Rebuild Project again once it compiles.");
                return;
            }

            var existing = new List<Component>();
            foreach (var go in scene.GetRootGameObjects()) existing.AddRange(go.GetComponentsInChildren(type, true));
            if (root == null) root = existing.Count > 0 ? existing[0].gameObject : NewSceneObject(scene, GameRootObjectName);
            root.name = GameRootObjectName;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (root.GetComponent(type) == null) root.AddComponent(type);
            foreach (var component in existing)
                if (component != null && component.gameObject != root) Object.DestroyImmediate(component); // one bootstrap only
        }

        /// <summary>PotionPop.GameRoot is written by the gameplay module: resolved by name so this tool never depends on it.</summary>
        static Type ResolveGameRootType()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (type.FullName == GameRootTypeName && !type.IsAbstract) return type;
            var found = EditorUtil.FindType(GameRootTypeName);
            return found != null && typeof(MonoBehaviour).IsAssignableFrom(found) && !found.IsAbstract ? found : null;
        }
    }

    /// <summary>
    /// Finishes a rebuild the user started while TMP Essential Resources were still importing (editor imports are
    /// asynchronous and usually reload the scripts, which would lose any in-memory callback).
    /// </summary>
    [InitializeOnLoad]
    static class PotionPopRebuildResumer
    {
        const string PendingKey = "PotionPop.RebuildAfterTmpImportTicks";
        static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        static PotionPopRebuildResumer()
        {
            if (Application.isBatchMode) return;
            AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += TryResume;
            if (IsPending) EditorApplication.delayCall += TryResume;
        }

        /// <summary>True while a rebuild waits for the TMP Essential Resources import (younger than 10 min).</summary>
        internal static bool IsPending
        {
            get
            {
                string ticks = SessionState.GetString(PendingKey, "");
                return long.TryParse(ticks, out long t) && DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc) <= MaxAge;
            }
        }

        internal static void MarkPending() =>
            SessionState.SetString(PendingKey, DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        static void TryResume()
        {
            if (!IsPending)
            {
                SessionState.EraseString(PendingKey);
                return;
            }
            if (!PotionPopBuilder.TmpResourcesPresent) return; // import still running: next import / reload retries
            if (EditorApplication.isPlayingOrWillChangePlaymode) return; // retried on the next script reload / package import
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryResume;
                return;
            }
            SessionState.EraseString(PendingKey);
            // The rebuild reopens Main.unity: never discard edits made while the package was importing.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "TMP Essential Resources imported. Run Potion Pop/Rebuild Project to finish the setup.");
                return;
            }
            Debug.Log(EditorUtil.LogPrefix + "TMP Essential Resources imported: resuming the rebuild.");
            PotionPopBuilder.ReportToUser(PotionPopBuilder.Rebuild());
        }
    }
}
