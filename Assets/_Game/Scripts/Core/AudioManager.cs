using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PotionPop
{
    public enum Sfx
    {
        // UI / meta
        Click, PopupOpen, PopupClose, Toggle, Error, Purchase, Pop, Sparkle, Countdown, Fanfare, Swoosh, Whoosh,
        Star, Coin, Reward, Heart, CardFlip, ChestOpen, SpinTick, SpinWin, Win, Lose, Combo, Unlock,
        // board (potion bottles)
        Select,          // bottle lifted (glass clink)
        Deselect,        // bottle put back down
        Pour,            // liquid stream glug (one per pour)
        PourEnd,         // liquid settles in the target (small splash)
        Complete,        // bottle completed: cork pops in + chime (pitch rises with the combo)
        Invalid,         // illegal pour (wrong color / full)
        Reveal,          // a hidden "?" layer reveals its color
        StoneCrack,      // a stone-wrapped bottle cracks (lock counter ticks down)
        StoneBreak,      // the stone shatters and frees the bottle
        Undo, AddBottle, Wand, Shuffle, Crystal, Rainbow,
        Bubble,          // ambient bubble pops (completed bottles, magic)
    }

    public enum Music { None, Home, Game, Hard }

    /// <summary>
    /// Clips: Resources/Audio/sfx_&lt;snake_case&gt; (e.g. Sfx.StoneCrack → "sfx_stone_crack") and
    /// Resources/Audio/music_&lt;snake_case&gt; (Music.Home → "music_home"). Missing clips are ignored silently.
    /// Two music sources cross-fade; a pool of SFX sources plays one-shots (same sound rate-limited to 30 ms).
    /// Everything is a no-op outside play mode (editor tools, EditMode tests).
    /// </summary>
    public static class AudioManager
    {
        const string Folder = "Audio/";
        const int SfxPoolSize = 10;
        const float SameSfxMinInterval = 0.03f;
        const float MusicVolume = 0.55f;
        const float DuckFadeSeconds = 0.25f;

        static GameObject _root;
        static AudioSource[] _music;     // [0], [1]: cross-fade pair
        static int _activeMusic;
        static AudioSource[] _sfx;
        static float[] _sfxStartTime;
        static int _nextSfx;
        static Music _currentMusic = Music.None;   // the requested track (may be silent while music is off)
        static Dictionary<string, AudioClip> _clips;  // null value = known missing
        static float[] _lastPlay;
        static string[] _sfxNames, _musicNames;

        // fades
        static float _fadeDuration, _fadeTime;
        static float _duck = 1f, _duckTarget = 1f;
        /// <summary>Application.quitting was raised: never (re)create the audio object during teardown.</summary>
        static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _root = null;
            _music = null;
            _sfx = null;
            _sfxStartTime = null;
            _clips = null;
            _lastPlay = null;
            _currentMusic = Music.None;
            _activeMusic = 0;
            _nextSfx = 0;
            _fadeDuration = _fadeTime = 0f;
            _duck = _duckTarget = 1f;
            _quitting = false;
        }

        public static bool MusicOn
        {
            get => SaveSystem.Data.musicOn;
            set
            {
                var d = SaveSystem.Data;
                if (d.musicOn == value) return;
                d.musicOn = value;
                SaveSystem.MarkDirty();
                ApplyMusicSetting();
            }
        }

        public static bool SoundOn
        {
            get => SaveSystem.Data.soundOn;
            set
            {
                var d = SaveSystem.Data;
                if (d.soundOn == value) return;
                d.soundOn = value;
                SaveSystem.MarkDirty();
                if (!value && _sfx != null)
                    foreach (var s in _sfx) if (s != null) s.Stop();
            }
        }

        /// <summary>The track last requested with PlayMusic.</summary>
        public static Music CurrentMusic => _currentMusic;

        /// <summary>Creates the hidden audio GameObject (DontDestroyOnLoad) and loads clips. Idempotent.</summary>
        public static void Init()
        {
            if (!Application.isPlaying || _quitting) return;
            if (_root != null) return;

            // A sound requested from OnDestroy while exiting would otherwise spawn a new object during teardown
            // ("Some objects were not cleaned up when closing the scene").
            Application.quitting -= HandleQuitting;
            Application.quitting += HandleQuitting;

            BuildNames();
            if (_clips == null) _clips = new Dictionary<string, AudioClip>(64, StringComparer.Ordinal);

            _root = new GameObject("[Audio]") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioRunner>();

            // The UI is a screen-space overlay; make sure something hears the sources.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) _root.AddComponent<AudioListener>();

            _music = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var s = _root.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.volume = 0f;
                _music[i] = s;
            }
            _sfx = new AudioSource[SfxPoolSize];
            _sfxStartTime = new float[SfxPoolSize];
            for (int i = 0; i < SfxPoolSize; i++)
            {
                var s = _root.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.priority = 64;
                _sfx[i] = s;
            }
            _lastPlay = new float[_sfxNames.Length];
            for (int i = 0; i < _lastPlay.Length; i++) _lastPlay[i] = -1f;
        }

        public static void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            if (!Application.isPlaying || !SoundOn) return;
            Init();
            if (_root == null) return;
            int id = (int)sfx;
            if (id < 0 || id >= _sfxNames.Length) return;

            float now = Time.unscaledTime;
            if (_lastPlay[id] >= 0f && now - _lastPlay[id] < SameSfxMinInterval) return;

            var clip = GetClip(_sfxNames[id]);
            if (clip == null) return;
            _lastPlay[id] = now;

            int index = NextSfxSource();
            var source = _sfx[index];
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            source.Play();
            _sfxStartTime[index] = now;
        }

        /// <summary>Major-scale steps (do re mi fa sol; ≤ +60% so the clip never aliases) so a chain of completed bottles climbs a melody.</summary>
        static readonly float[] ComboPitches = { 1f, 1.1225f, 1.2599f, 1.3348f, 1.4983f };

        /// <summary>Bottle-complete sound with pitch rising per combo step (combo 1 = base pitch).</summary>
        public static void PlayCombo(int combo)
        {
            int step = Mathf.Clamp(combo - 1, 0, ComboPitches.Length - 1);
            Play(Sfx.Complete, 1f, ComboPitches[step]);
        }

        public static void PlayMusic(Music music, float fadeSeconds = 0.6f)
        {
            if (!Application.isPlaying) return;
            Init();
            if (_root == null) return;

            var active = _music[_activeMusic];
            // Same track requested again (e.g. switching screens): keep it playing seamlessly.
            if (music == _currentMusic && (music == Music.None || !MusicOn || active.isPlaying)) return;
            _currentMusic = music;
            if (!MusicOn) return;
            StartTrack(music, fadeSeconds);
        }

        /// <summary>Temporarily lowers music (e.g. during ads/popups). 1 = normal.</summary>
        public static void DuckMusic(float volume01)
        {
            _duckTarget = Mathf.Clamp01(volume01);
            if (!Application.isPlaying || _root == null) _duck = _duckTarget;
        }

        /// <summary>Stops every sound effect currently playing (e.g. leaving the board).</summary>
        public static void StopAllSfx()
        {
            if (_sfx == null) return;
            foreach (var s in _sfx) if (s != null) s.Stop();
        }

        /// <summary>Sound file name of a Sfx ("sfx_layer_reveal").</summary>
        public static string ClipName(Sfx sfx)
        {
            BuildNames();
            int i = (int)sfx;
            return i >= 0 && i < _sfxNames.Length ? _sfxNames[i] : "sfx_" + ToSnakeCase(sfx.ToString());
        }

        /// <summary>Music file name ("music_home"); null for Music.None.</summary>
        public static string ClipName(Music music)
        {
            BuildNames();
            int i = (int)music;
            return music == Music.None ? null : i >= 0 && i < _musicNames.Length ? _musicNames[i] : "music_" + ToSnakeCase(music.ToString());
        }

        /// <summary>"StoneCrack" → "stone_crack".</summary>
        public static string ToSnakeCase(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return "";
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && (char.IsLower(pascal[i - 1]) || char.IsDigit(pascal[i - 1]) ||
                                  (i + 1 < pascal.Length && char.IsLower(pascal[i + 1]) && char.IsUpper(pascal[i - 1]))))
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ internals

        internal static void OnSaveReplaced()
        {
            if (_root != null) ApplyMusicSetting();
        }

        static void HandleQuitting() => _quitting = true;

        static void ApplyMusicSetting()
        {
            if (!Application.isPlaying || _root == null) return;
            if (MusicOn) StartTrack(_currentMusic, 0.4f);
            else StartTrack(Music.None, 0.3f);
        }

        static void StartTrack(Music music, float fadeSeconds)
        {
            var current = _music[_activeMusic];
            AudioClip clip = music == Music.None ? null : GetClip(ClipName(music));

            if (clip != null && current.clip == clip && current.isPlaying)
            {
                // Already the audible track (e.g. music toggled back on during its fade-out): fade it back in.
                BeginFade(fadeSeconds);
                return;
            }

            // Swap: the new track fades in on the other source while the current one fades out.
            _activeMusic = 1 - _activeMusic;
            var next = _music[_activeMusic];
            if (clip != null && next.clip == clip && next.isPlaying)
            {
                // The requested track is the one still fading out (music toggled off then on again): fade it back in
                // where it is instead of restarting it.
                BeginFade(fadeSeconds);
                return;
            }
            next.Stop();
            next.clip = clip;
            next.volume = 0f;
            if (clip != null) next.Play();
            BeginFade(fadeSeconds);
        }

        static void BeginFade(float seconds)
        {
            _fadeDuration = Mathf.Max(0.01f, seconds);
            _fadeTime = 0f;
        }

        /// <summary>Per-frame fades (driven by the hidden runner).</summary>
        internal static void Update(float dt)
        {
            if (_music == null) return;
            if (_duck != _duckTarget) _duck = Mathf.MoveTowards(_duck, _duckTarget, dt / DuckFadeSeconds);

            _fadeTime += dt;
            bool fadeDone = _fadeTime >= _fadeDuration;
            float step = dt * MusicVolume / _fadeDuration;   // linear fade over _fadeDuration
            for (int i = 0; i < _music.Length; i++)
            {
                var s = _music[i];
                if (s == null) continue;
                if (i == _activeMusic)
                {
                    float target = s.clip != null ? MusicVolume * _duck : 0f;
                    s.volume = fadeDone ? target : Mathf.MoveTowards(s.volume, target, step);
                }
                else if (s.isPlaying)
                {
                    s.volume = Mathf.MoveTowards(s.volume, 0f, step);
                    if (fadeDone || s.volume <= 0.0001f) s.Stop();
                }
            }
        }

        /// <summary>Index of the SFX source to use: an idle one, otherwise the one that started first.</summary>
        static int NextSfxSource()
        {
            for (int i = 0; i < _sfx.Length; i++)
            {
                int idx = (_nextSfx + i) % _sfx.Length;
                if (!_sfx[idx].isPlaying)
                {
                    _nextSfx = (idx + 1) % _sfx.Length;
                    return idx;
                }
            }
            int oldest = 0;
            for (int i = 1; i < _sfx.Length; i++)
                if (_sfxStartTime[i] < _sfxStartTime[oldest]) oldest = i;
            _sfx[oldest].Stop();
            return oldest;
        }

        static AudioClip GetClip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_clips == null) _clips = new Dictionary<string, AudioClip>(64, StringComparer.Ordinal);
            if (_clips.TryGetValue(name, out var clip))
            {
                if (ReferenceEquals(clip, null)) return null;   // known missing
                if (clip != null) return clip;                  // cached (a destroyed clip falls through and reloads)
            }
            try { clip = Resources.Load<AudioClip>(Folder + name); }
            catch (Exception) { clip = null; }
            _clips[name] = clip;   // also caches misses (silently ignored)
            return clip;
        }

        static void BuildNames()
        {
            if (_sfxNames != null) return;
            var sfx = (Sfx[])Enum.GetValues(typeof(Sfx));
            int maxSfx = 0;
            foreach (var s in sfx) maxSfx = Math.Max(maxSfx, (int)s);
            _sfxNames = new string[maxSfx + 1];
            foreach (var s in sfx) _sfxNames[(int)s] = "sfx_" + ToSnakeCase(s.ToString());

            var music = (Music[])Enum.GetValues(typeof(Music));
            int maxMusic = 0;
            foreach (var m in music) maxMusic = Math.Max(maxMusic, (int)m);
            _musicNames = new string[maxMusic + 1];
            foreach (var m in music) _musicNames[(int)m] = "music_" + ToSnakeCase(m.ToString());
        }
    }

    /// <summary>Hidden driver for AudioManager fades.</summary>
    [AddComponentMenu("")]
    internal sealed class AudioRunner : MonoBehaviour
    {
        void Update() => AudioManager.Update(Time.unscaledDeltaTime);
    }
}
