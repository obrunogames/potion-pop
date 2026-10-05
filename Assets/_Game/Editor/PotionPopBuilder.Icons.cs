using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PotionPop.EditorTools
{
    public static partial class PotionPopBuilder
    {
        public const string AndroidIconForegroundPath = "Assets/_Game/Art/AppIcon/android_foreground.png";
        public const string AndroidIconBackgroundPath = "Assets/_Game/Art/AppIcon/android_background.png";

        /// <summary>Focused batch entry point: persist icons without rebuilding scenes/services or changing versions.</summary>
        public static void ApplyAppIcons()
        {
            var icon = RequiredIcon(AppIconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
#if UNITY_ANDROID
            var foreground = RequiredIcon(AndroidIconForegroundPath);
            var background = RequiredIcon(AndroidIconBackgroundPath);
            var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
            if (kinds.Length == 0) throw new BuildFailedException("Android platform exposes no icon kinds.");
            foreach (var kind in kinds)
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                if (slots.Length == 0) throw new BuildFailedException("No Android icon slots for " + kind);
                bool adaptive = kind == UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
                foreach (var slot in slots)
                {
                    // Unity stores adaptive background first and foreground second.
                    slot.SetTexture(adaptive ? background : icon, 0);
                    if (adaptive) slot.SetTexture(foreground, 1);
                    // Photographic artwork is not an Android monochrome silhouette.
                    for (int layer = adaptive ? 2 : 1; layer < slot.maxLayerCount; layer++) slot.SetTexture(null, layer);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            ValidateAndroidIcons();
#endif
            AssetDatabase.SaveAssets();
        }

        static Texture2D RequiredIcon(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new BuildFailedException("Missing app icon: " + path + ". Run Tools/release/android_icons.py.");
            return texture;
        }

#if UNITY_ANDROID
        /// <summary>Fail all Android builds, including Build Profiles builds, if a platform slot is empty/wrong.</summary>
        public static void ValidateAndroidIcons()
        {
            var defaults = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (defaults.Length == 0 || AssetDatabase.GetAssetPath(defaults[0]) != AppIconPath)
                throw new BuildFailedException("Default app icon must use " + AppIconPath);
            var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
            if (kinds.Length == 0) throw new BuildFailedException("Android platform exposes no icon kinds.");
            foreach (var kind in kinds)
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                if (slots.Length == 0) throw new BuildFailedException("No Android icon slots for " + kind);
                bool adaptive = kind == UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
                foreach (var slot in slots)
                {
                    CheckLayer(slot, 0, adaptive ? AndroidIconBackgroundPath : AppIconPath);
                    if (adaptive) CheckLayer(slot, 1, AndroidIconForegroundPath);
                }
            }
        }

        static void CheckLayer(PlatformIcon slot, int layer, string path)
        {
            if (AssetDatabase.GetAssetPath(slot.GetTexture(layer)) != path)
                throw new BuildFailedException($"Android {slot.width}px icon layer {layer} must use {path}. " +
                                               "Run Potion Pop > Rebuild Project before building.");
        }
#endif
    }

#if UNITY_ANDROID
    /// <summary>Unity 6000.6 only exposes adaptive slots; its legacy launcher template still contains Unity's logo.</summary>
    sealed class AndroidLauncherIconPostProcess : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string res = Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "res"));
            if (!File.Exists(Path.Combine(res, "mipmap-mdpi", "app_icon.png")))
                throw new BuildFailedException("Generated Android launcher fallback is missing in " + res);
            var original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(original, File.ReadAllBytes(PotionPopBuilder.AppIconPath)))
                    throw new BuildFailedException("Unable to read original Luna icon.");
                var densities = new[] { "ldpi", "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" };
                var sizes = new[] { 36, 48, 72, 96, 144, 192 };
                for (int i = 0; i < densities.Length; i++)
                {
                    int size = sizes[i];
                    var small = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    try
                    {
                        var pixels = new Color[size * size];
                        for (int y = 0; y < size; y++)
                            for (int x = 0; x < size; x++)
                                pixels[y * size + x] = original.GetPixelBilinear((x + .5f) / size, (y + .5f) / size);
                        small.SetPixels(pixels);
                        small.Apply();
                        string folder = Path.Combine(res, "mipmap-" + densities[i]);
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, "app_icon.png"), ImageConversion.EncodeToPNG(small));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(small); }
                }
                Debug.Log(EditorUtil.LogPrefix + "Android launcher fallbacks use the original Luna artwork in all densities.");
            }
            finally { UnityEngine.Object.DestroyImmediate(original); }
        }
    }
#endif
}
