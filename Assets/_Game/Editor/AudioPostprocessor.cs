using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Import settings for Assets/_Game/Resources/Audio (clips loaded by AudioManager):
    /// * sfx_*   — mono, Decompress On Load (zero playback latency, no decode cost while playing). Short clips
    ///             (≤ 3 s, measured from the WAV header) use ADPCM (tiny CPU cost on load); longer or already
    ///             compressed sources use Vorbis quality 0.6.
    /// * music_* — Streaming, Vorbis quality 0.5, stereo kept, loaded in the background (no memory spike).
    /// </summary>
    public sealed class AudioPostprocessor : AssetPostprocessor
    {
        public const string AudioFolder = "Assets/_Game/Resources/Audio/";
        const float ShortSfxSeconds = 3f;
        const float SfxVorbisQuality = 0.6f, MusicVorbisQuality = 0.5f;

        public override uint GetVersion() => 1;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioFolder, StringComparison.Ordinal)) return;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;

            if (name.StartsWith("music_", StringComparison.Ordinal))
            {
                importer.forceToMono = false;
                importer.loadInBackground = true;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = MusicVorbisQuality;
                settings.preloadAudioData = false;
            }
            else if (name.StartsWith("sfx_", StringComparison.Ordinal))
            {
                importer.forceToMono = true;
                importer.loadInBackground = false;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                float seconds = WavDurationSeconds(assetPath);
                bool shortClip = seconds >= 0f && seconds <= ShortSfxSeconds;
                settings.compressionFormat = shortClip ? AudioCompressionFormat.ADPCM : AudioCompressionFormat.Vorbis;
                settings.quality = SfxVorbisQuality;
                settings.preloadAudioData = true;
            }
            else return;

            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            foreach (string platform in new[] { "Android", "iOS", "Standalone" })
                if (importer.ContainsSampleSettingsOverride(platform)) importer.ClearSampleSettingOverride(platform);
        }

        static string Tag(BinaryReader reader) => System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));

        /// <summary>Duration of a PCM WAV file from its RIFF header, or -1 (not a WAV / unreadable).</summary>
        static float WavDurationSeconds(string path)
        {
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return -1f;
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    if (reader.BaseStream.Length < 12) return -1f;
                    if (Tag(reader) != "RIFF") return -1f;
                    reader.ReadInt32();
                    if (Tag(reader) != "WAVE") return -1f;
                    int byteRate = 0;
                    while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                    {
                        string id = Tag(reader);
                        uint size = reader.ReadUInt32();
                        long next = reader.BaseStream.Position + size + (size & 1); // chunks are word aligned
                        if (id == "fmt " && size >= 12)
                        {
                            reader.ReadInt16(); // format
                            reader.ReadInt16(); // channels
                            reader.ReadInt32(); // sample rate
                            byteRate = reader.ReadInt32();
                        }
                        else if (id == "data")
                        {
                            return byteRate > 0 ? size / (float)byteRate : -1f;
                        }
                        if (next > reader.BaseStream.Length) break;
                        reader.BaseStream.Position = next;
                    }
                }
            }
            catch (Exception) { /* unreadable header: treat as long */ }
            return -1f;
        }
    }
}
