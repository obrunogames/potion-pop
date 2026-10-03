using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Import settings for every game image: Assets/_Game/Resources/Art/*.png (flat sprite folder written by
    /// Tools/process_art.py) and Assets/_Game/Art/** (app icon, store art...).
    /// * Sprite (Single), alpha is transparency, no mipmaps, bilinear, clamp.
    /// * 9-slice borders and pivots come from Resources/Art/art_index.json; sliced sprites use a FullRect mesh.
    /// * Max size 2048 for home_/gamebg_/logo*, 1024 otherwise. ASTC 6x6 on Android/iOS (backgrounds ASTC 8x8),
    ///   high-quality compression on desktop. Small procedural "ui_*" helpers (≤ 512 px) and the in-game copy of the
    ///   app icon stay uncompressed (smooth gradients, crisp 9-slice edges).
    /// * Assets/_Game/Art/**/app_icon.png (PlayerSettings icon source): Default texture type, uncompressed.
    /// When art_index.json changes, only the sprites whose border/pivot changed are reimported.
    /// </summary>
    public sealed class ArtImportProcessor : AssetPostprocessor
    {
        public const string ResourcesArtFolder = "Assets/_Game/Resources/Art/";
        public const string ArtFolder = "Assets/_Game/Art/";
        public const string IndexPath = "Assets/_Game/Resources/Art/art_index.json";
        const int LargeMaxSize = 2048, DefaultMaxSize = 1024, UncompressedHelperMaxSize = 512;

        // Bump when the rules below change: Unity then reimports every texture handled here.
        public override uint GetVersion() => 2;

        public static bool IsArtTexture(string path)
        {
            if (!path.StartsWith(ResourcesArtFolder, StringComparison.Ordinal) && !path.StartsWith(ArtFolder, StringComparison.Ordinal))
                return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        void OnPreprocessTexture()
        {
            if (!IsArtTexture(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            SourceSize(importer, assetPath, out int width, out int height);
            bool appIconSource = name == "app_icon" && assetPath.StartsWith(ArtFolder, StringComparison.Ordinal);
            Apply(importer, name, width, height, ArtIndex.Find(name), appIconSource);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool indexChanged = Array.IndexOf(imported, IndexPath) >= 0 || Array.IndexOf(moved, IndexPath) >= 0;
            if (!indexChanged) return;
            ArtIndex.Invalidate();
            ReimportOutdatedSprites();
        }

        // ------------------------------------------------------------------ rules

        static bool IsBackground(string name) => name.StartsWith("home_", StringComparison.Ordinal) || name.StartsWith("gamebg_", StringComparison.Ordinal);
        static bool IsLarge(string name) => IsBackground(name) || name.StartsWith("logo", StringComparison.Ordinal) || name == "fx_ice_frame";

        static void Apply(TextureImporter importer, string name, int width, int height, ArtIndex.SpriteMeta meta, bool appIconSource)
        {
            int maxSize = IsLarge(name) ? LargeMaxSize : DefaultMaxSize;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maxSize;

            if (appIconSource)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                foreach (string platform in new[] { "Android", "iPhone", "Standalone" }) ClearOverride(importer, platform);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;

            var layout = Layout(name, width, height, meta);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteBorder = layout.border;
            settings.spriteMeshType = layout.mesh;
            settings.spriteAlignment = (int)layout.alignment;
            settings.spritePivot = layout.pivot;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);

            bool uncompressed = name == "app_icon" ||
                                (name.StartsWith("ui_", StringComparison.Ordinal) && width > 0 && Mathf.Max(width, height) <= UncompressedHelperMaxSize);
            if (uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                foreach (string platform in new[] { "Android", "iPhone", "Standalone" }) ClearOverride(importer, platform);
                return;
            }

            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var mobile = IsBackground(name) ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
            SetOverride(importer, "Android", maxSize, mobile);
            SetOverride(importer, "iPhone", maxSize, mobile);
            SetOverride(importer, "Standalone", maxSize, TextureImporterFormat.Automatic);
        }

        static void SetOverride(TextureImporter importer, string platform, int maxSize, TextureImporterFormat format)
        {
            var s = importer.GetPlatformTextureSettings(platform);
            s.overridden = true;
            s.maxTextureSize = maxSize;
            s.format = format;
            s.textureCompression = TextureImporterCompression.CompressedHQ;
            s.compressionQuality = 50;
            s.crunchedCompression = false;
            s.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            importer.SetPlatformTextureSettings(s);
        }

        static void ClearOverride(TextureImporter importer, string platform)
        {
            var s = importer.GetPlatformTextureSettings(platform);
            if (!s.overridden) return;
            s.overridden = false;
            importer.SetPlatformTextureSettings(s);
        }

        // ------------------------------------------------------------------ sprite layout (border / pivot / mesh)

        struct SpriteLayout
        {
            public Vector4 border;            // left, bottom, right, top (source pixels)
            public SpriteAlignment alignment;
            public Vector2 pivot;
            public SpriteMeshType mesh;
        }

        static SpriteLayout Layout(string name, int width, int height, ArtIndex.SpriteMeta meta)
        {
            var layout = new SpriteLayout
            {
                border = Vector4.zero,
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            };
            if (meta != null)
            {
                // Border/pivot pixels are relative to the w x h written in the index; rescale if the PNG differs.
                float sx = meta.w > 0f && width > 0 ? width / meta.w : 1f;
                float sy = meta.h > 0f && height > 0 ? height / meta.h : 1f;
                if (meta.border != null && meta.border.Length >= 4)
                {
                    float l = Mathf.Max(0f, Mathf.Round(meta.border[0] * sx));
                    float b = Mathf.Max(0f, Mathf.Round(meta.border[1] * sy));
                    float r = Mathf.Max(0f, Mathf.Round(meta.border[2] * sx));
                    float t = Mathf.Max(0f, Mathf.Round(meta.border[3] * sy));
                    // Unity rejects borders that overlap: keep at least one stretchable pixel.
                    if (width > 0 && l + r >= width) { float k = (width - 1) / (l + r); l = Mathf.Floor(l * k); r = Mathf.Floor(r * k); }
                    if (height > 0 && b + t >= height) { float k = (height - 1) / (b + t); b = Mathf.Floor(b * k); t = Mathf.Floor(t * k); }
                    layout.border = new Vector4(l, b, r, t);
                }
                if (meta.pivot != null && meta.pivot.Length >= 2)
                {
                    float px = meta.pivot[0], py = meta.pivot[1];
                    // Normalized (0..1, origin bottom-left) expected; pixel values are accepted too.
                    if (px > 1f && meta.w > 0f) px /= meta.w;
                    if (py > 1f && meta.h > 0f) py /= meta.h;
                    var pivot = new Vector2(Mathf.Clamp01(px), Mathf.Clamp01(py));
                    if ((pivot - new Vector2(0.5f, 0.5f)).sqrMagnitude > 1e-8f)
                    {
                        layout.alignment = SpriteAlignment.Custom;
                        layout.pivot = pivot;
                    }
                }
            }
            bool sliced = layout.border != Vector4.zero;
            layout.mesh = sliced || IsBackground(name) ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            return layout;
        }

        static bool Approximately(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 0.01f;

        // ------------------------------------------------------------------ source image size

        /// <summary>
        /// Pixel size of the source image. Read from the PNG/JPEG header: TextureImporter.GetSourceTextureWidthAndHeight
        /// throws on the first import of a new file (no import result yet), which is exactly when the rules matter most.
        /// 0×0 when unknown (borders are then used as written in art_index.json).
        /// </summary>
        static void SourceSize(TextureImporter importer, string path, out int width, out int height)
        {
            if (ImageHeader.TryReadSize(path, out width, out height)) return;
            try { importer.GetSourceTextureWidthAndHeight(out width, out height); }
            catch (InvalidOperationException) { width = height = 0; }
            if (width < 0 || height < 0) width = height = 0;
        }

        /// <summary>Reimports the art sprites whose importer border/pivot/mesh differ from art_index.json.</summary>
        static void ReimportOutdatedSprites()
        {
            var folders = new List<string>();
            foreach (string folder in new[] { ResourcesArtFolder.TrimEnd('/'), ArtFolder.TrimEnd('/') })
                if (AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
            if (folders.Count == 0) return;

            var outdated = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", folders.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsArtTexture(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType != TextureImporterType.Sprite) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                SourceSize(importer, path, out int width, out int height);
                var expected = Layout(name, width, height, ArtIndex.Find(name));
                var current = new TextureImporterSettings();
                importer.ReadTextureSettings(current);
                bool same = Approximately(current.spriteBorder, expected.border)
                            && current.spriteMeshType == expected.mesh
                            && current.spriteAlignment == (int)expected.alignment
                            && (expected.alignment != SpriteAlignment.Custom || (current.spritePivot - expected.pivot).sqrMagnitude < 1e-6f);
                if (!same) outdated.Add(path);
            }
            if (outdated.Count == 0) return;

            Debug.Log(EditorUtil.LogPrefix + $"art_index.json changed: reimporting {outdated.Count} sprite(s).");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in outdated) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }

    /// <summary>
    /// Reader of Assets/_Game/Resources/Art/art_index.json:
    /// {"sprites":[{"name":"panel_popup","w":512,"h":512,"border":[l,b,r,t],"pivot":[x,y],"inner":[x,y,w,h]}]}.
    /// Read straight from disk (cached by timestamp) so texture imports always see the latest file.
    /// </summary>
    public static class ArtIndex
    {
        [Serializable]
        public sealed class SpriteMeta
        {
            public string name;
            public float w, h;
            public float[] border;   // left, bottom, right, top in pixels of w x h
            public float[] pivot;    // normalized, origin bottom-left
            public float[] inner;    // normalized interior rect (runtime use)
        }

        [Serializable]
        sealed class IndexFile
        {
            public SpriteMeta[] sprites;
        }

        static Dictionary<string, SpriteMeta> s_sprites;
        static DateTime s_stamp;
        static long s_length;

        /// <summary>Metadata of a sprite by file name (without extension), or null.</summary>
        public static SpriteMeta Find(string name)
        {
            Load();
            return s_sprites.TryGetValue(name, out var meta) ? meta : null;
        }

        public static void Invalidate() => s_sprites = null;

        static void Load()
        {
            var file = new FileInfo(ArtImportProcessor.IndexPath);
            if (!file.Exists)
            {
                s_sprites = new Dictionary<string, SpriteMeta>();
                s_stamp = default;
                s_length = -1;
                return;
            }
            if (s_sprites != null && file.LastWriteTimeUtc == s_stamp && file.Length == s_length) return;

            var map = new Dictionary<string, SpriteMeta>(StringComparer.Ordinal);
            try
            {
                var index = JsonUtility.FromJson<IndexFile>(File.ReadAllText(file.FullName));
                if (index?.sprites != null)
                    foreach (var sprite in index.sprites)
                        if (sprite != null && !string.IsNullOrEmpty(sprite.name)) map[sprite.name] = sprite;
            }
            catch (Exception e)
            {
                Debug.LogError(EditorUtil.LogPrefix + "Could not parse " + ArtImportProcessor.IndexPath + ": " + e.Message);
            }
            s_sprites = map;
            s_stamp = file.LastWriteTimeUtc;
            s_length = file.Length;
        }
    }
    /// <summary>Reads the pixel size of PNG and JPEG files from their headers (no decoding).</summary>
    public static class ImageHeader
    {
        public static bool TryReadSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var head = new byte[24];
                    if (stream.Read(head, 0, head.Length) < head.Length) return false;
                    // PNG: 8-byte signature, then the IHDR chunk (length, "IHDR", width, height; big endian).
                    if (head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G' &&
                        head[12] == (byte)'I' && head[13] == (byte)'H' && head[14] == (byte)'D' && head[15] == (byte)'R')
                    {
                        width = BigEndian(head, 16);
                        height = BigEndian(head, 20);
                        return width > 0 && height > 0;
                    }
                    if (head[0] == 0xFF && head[1] == 0xD8) return TryReadJpeg(stream, out width, out height);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        static int BigEndian(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        /// <summary>Walks the JPEG segments up to the first SOFn frame header.</summary>
        static bool TryReadJpeg(Stream stream, out int width, out int height)
        {
            width = height = 0;
            stream.Position = 2;
            var buffer = new byte[7];
            while (stream.Position + 4 <= stream.Length)
            {
                int b = stream.ReadByte();
                if (b != 0xFF) return false;
                int marker = stream.ReadByte();
                while (marker == 0xFF) marker = stream.ReadByte(); // fill bytes
                if (marker < 0) return false;
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue; // no length
                if (stream.Read(buffer, 0, 2) < 2) return false;
                int length = (buffer[0] << 8) | buffer[1];
                if (length < 2) return false;
                bool frame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (frame)
                {
                    if (stream.Read(buffer, 0, 5) < 5) return false; // precision, height, width
                    height = (buffer[1] << 8) | buffer[2];
                    width = (buffer[3] << 8) | buffer[4];
                    return width > 0 && height > 0;
                }
                stream.Position += length - 2;
            }
            return false;
        }
    }
}
