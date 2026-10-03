using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PotionPop
{
    public enum HapticType { Light, Medium, Heavy, Success, Warning, Selection }

    /// <summary>
    /// Short vibrations. Android: android.os.Vibrator (VibrationEffect with amplitude on API 26+, legacy
    /// vibrate(ms) below). iOS: UIFeedbackGenerators via Plugins/iOS/SPHaptics.mm. Editor/other platforms: no-op.
    /// The setting is persisted in PlayerData.vibrationOn.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Minimum seconds between two haptics of the same type (avoids a buzzing mess on fast combos).</summary>
        const float MinInterval = 0.04f;
        static float[] _last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _last = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            _vibrator = null;
            _vibrationEffect = null;
            _effects = null;
            _androidReady = false;
            _androidFailed = false;
#endif
        }

        public static bool Enabled
        {
            get => SaveSystem.Data.vibrationOn;
            set
            {
                var d = SaveSystem.Data;
                if (d.vibrationOn == value) return;
                d.vibrationOn = value;
                SaveSystem.MarkDirty();
            }
        }

        public static void Play(HapticType type)
        {
            if (!Application.isPlaying || !Enabled) return;
            int i = (int)type;
            if (i < 0 || i > (int)HapticType.Selection) return;
            if (_last == null)
            {
                _last = new float[(int)HapticType.Selection + 1];
                for (int k = 0; k < _last.Length; k++) _last[k] = -1f;
            }
            float now = Time.unscaledTime;
            if (_last[i] >= 0f && now - _last[i] < MinInterval) return;
            _last[i] = now;

#if UNITY_IOS && !UNITY_EDITOR
            try { _SP_Haptic(i); }
            catch (Exception) { /* plugin missing: ignore */ }
#elif UNITY_ANDROID && !UNITY_EDITOR
            PlayAndroid(type);
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _SP_Haptic(int type);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static AndroidJavaClass _vibrationEffect;   // null below API 26
        static AndroidJavaObject[] _effects;        // VibrationEffect per HapticType (immutable, reused)
        static bool _androidReady, _androidFailed;

        static void PlayAndroid(HapticType type)
        {
            if (_androidFailed) return;
            try
            {
                if (!_androidReady) InitAndroid();
                if (_vibrator == null) return;

                long ms; int amplitude;
                switch (type)
                {
                    case HapticType.Light: ms = 12; amplitude = 60; break;
                    case HapticType.Medium: ms = 22; amplitude = 140; break;
                    case HapticType.Heavy: ms = 38; amplitude = 255; break;
                    case HapticType.Success: ms = 30; amplitude = 180; break;
                    case HapticType.Warning: ms = 55; amplitude = 220; break;
                    default: ms = 8; amplitude = 40; break;   // Selection
                }

                if (_vibrationEffect != null)
                {
                    if (_effects == null) _effects = new AndroidJavaObject[(int)HapticType.Selection + 1];
                    var effect = _effects[(int)type];
                    if (effect == null)
                    {
                        if (type == HapticType.Success || type == HapticType.Warning)
                        {
                            // Two pulses: (delay, on, pause, on)
                            long[] timings = type == HapticType.Success ? new long[] { 0, 25, 70, 35 } : new long[] { 0, 45, 90, 45 };
                            int[] amps = type == HapticType.Success ? new[] { 0, 150, 0, 220 } : new[] { 0, 230, 0, 230 };
                            effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createWaveform", timings, amps, -1);
                        }
                        else effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude);
                        _effects[(int)type] = effect;
                    }
                    _vibrator.Call("vibrate", effect);
                }
                else
                {
                    _vibrator.Call("vibrate", ms);
                }
            }
            catch (Exception e)
            {
                _androidFailed = true;   // don't spam JNI exceptions every tap
                Debug.LogWarning("[Haptics] Android vibration unavailable: " + e.Message);
            }
        }

        static void InitAndroid()
        {
            _androidReady = true;
            // Never true: only references Handheld.Vibrate so Unity adds the VIBRATE permission to the manifest.
            if (Time.frameCount < 0) Handheld.Vibrate();
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                {
                    vibrator?.Dispose();
                    return;
                }
                _vibrator = vibrator;
            }
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            if (sdk >= 26) _vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
        }
#endif
    }
}
