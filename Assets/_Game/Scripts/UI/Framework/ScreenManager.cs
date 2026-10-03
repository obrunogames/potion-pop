// ============================================================================================================
// Screens + the single Screen Space Overlay canvas. Hierarchy built by ScreenManager.Setup():
//   UI (Canvas overlay, CanvasScaler 1080x1920, GraphicRaycaster)
//   └ SafeRoot (follows Screen.safeArea)
//       ├ ScreensLayer  (screens, one active at a time; shaken by FX.ScreenShake)
//       ├ ChromeLayer   (top bar / bottom nav)
//       ├ PopupLayer    (PopupManager stack)
//       ├ FxLayer       (particles, reward flights; never blocks input)
//       └ TopLayer      (toasts, splash, loading overlays)
// Each layer is a nested canvas so per-frame changes (FX, tweens) only rebatch their own layer.
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace PotionPop.UI
{
    public enum ScreenId { Home, Shop, Leaderboard, Collection, Profile, Game, Worlds }

    /// <summary>Full-screen view. Built once (Build) when registered; OnShow/OnHide on every switch.</summary>
    public abstract class UIScreen : MonoBehaviour
    {
        public RectTransform Root;
        public abstract ScreenId Id { get; }
        public virtual bool ShowBottomNav => true;
        public virtual bool ShowTopBar => true;
        public virtual Music Music => Music.Home;
        /// <summary>Create children under Root (already stretched full screen).</summary>
        public abstract void Build();
        public virtual void OnShow(object arg) { }
        public virtual void OnHide() { }

        // ---- additions
        /// <summary>Hardware back / ESC while no popup is open. Return true when handled.</summary>
        public virtual bool OnBack() => false;
        /// <summary>True between OnShow and OnHide.</summary>
        public bool IsVisible { get; internal set; }
        /// <summary>CanvasGroup on the screen root (used for the cross-fade).</summary>
        public CanvasGroup Group { get; internal set; }
    }

    /// <summary>Owns the canvas layers. Created by GameRoot.</summary>
    public class ScreenManager : MonoBehaviour
    {
        public static ScreenManager Instance;
        public Canvas Canvas;
        /// <summary>Safe-area root (everything below lives inside the device safe area).</summary>
        public RectTransform SafeRoot;
        public RectTransform ScreensLayer, ChromeLayer, PopupLayer, FxLayer, TopLayer;
        public ScreenId Current { get; private set; }
        public event Action<ScreenId> OnScreenChanged;

        // ---- additions
        public CanvasScaler Scaler;
        /// <summary>Current screen component (null before the first Show).</summary>
        public UIScreen CurrentScreen { get; private set; }
        /// <summary>Raised after the safe area or the resolution changed (layout already updated).</summary>
        public event Action OnSafeAreaChanged;
        /// <summary>Hardware back pressed with no popup open and not handled by the current screen.</summary>
        public event Action OnBackUnhandled;
        /// <summary>Safe-area insets in canvas units: (left, bottom, right, top).</summary>
        public Vector4 SafeInsets { get; private set; }
        /// <summary>Full canvas size in canvas units (e.g. 1080 x 2340 on a 19.5:9 phone).</summary>
        public Vector2 CanvasSize { get; private set; } = new Vector2(DS.Space.ReferenceWidth, DS.Space.ReferenceHeight);
        /// <summary>Safe area size in canvas units.</summary>
        public Vector2 SafeSize => new Vector2(CanvasSize.x - SafeInsets.x - SafeInsets.z, CanvasSize.y - SafeInsets.y - SafeInsets.w);
        public bool IsTransitioning => _outgoing != null;
        public const float TransitionDuration = 0.25f;

        readonly Dictionary<ScreenId, UIScreen> _screens = new Dictionary<ScreenId, UIScreen>();
        UIScreen _outgoing;
        bool _setupDone;
        int _lastW = -1, _lastH = -1;
        Rect _lastSafe;
        int _backFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            ShuttingDown = false;
            Application.quitting -= MarkShuttingDown;
            Application.quitting += MarkShuttingDown;
        }

        static void MarkShuttingDown() => ShuttingDown = true;

        /// <summary>True while the app quits / Play Mode exits: no UI may be created then (a popup opened from an
        /// OnDestroy/OnHide during teardown used to spawn a stray "UI" object that the editor then saved into the scene).</summary>
        public static bool ShuttingDown { get; private set; }

        /// <summary>
        /// Returns the instance, creating a "UI" GameObject with a set-up ScreenManager if none exists.
        /// Returns null (and creates nothing) outside Play Mode or while shutting down.
        /// </summary>
        public static ScreenManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            if (!Application.isPlaying || ShuttingDown) return null;
            var existing = FindAnyObjectByType<ScreenManager>();
            if (existing != null)
            {
                existing.Setup();
                return existing;
            }
            var go = new GameObject("UI");
            var sm = go.AddComponent<ScreenManager>();
            sm.Setup();
            return sm;
        }

        // ---------------------------------------------------------------------------------------- setup

        /// <summary>Creates the canvas hierarchy (camera-less overlay, CanvasScaler 1080x1920 adaptive match,
        /// safe area, layers) on this GameObject.</summary>
        public void Setup()
        {
            Instance = this;
            if (_setupDone) return;
            _setupDone = true;
            gameObject.layer = UIKit.UILayer;

            Canvas = GetComponent<Canvas>();
            if (Canvas == null) Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 0;
            Canvas.pixelPerfect = false;
            Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                               AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

            Scaler = GetComponent<CanvasScaler>();
            if (Scaler == null) Scaler = gameObject.AddComponent<CanvasScaler>();
            Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Scaler.referenceResolution = new Vector2(DS.Space.ReferenceWidth, DS.Space.ReferenceHeight);
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            Scaler.referencePixelsPerUnit = 100f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            SafeRoot = UIKit.Stretch(UIKit.Rect("SafeRoot", transform));
            ScreensLayer = Layer("ScreensLayer", true, 0);
            ChromeLayer = Layer("ChromeLayer", true, 1);
            PopupLayer = Layer("PopupLayer", true, 2);
            FxLayer = Layer("FxLayer", false, 3);
            TopLayer = Layer("TopLayer", true, 4);

            EnsureEventSystem();
            UpdateLayout(true);
        }

        RectTransform Layer(string name, bool raycast, int order)
        {
            var rt = UIKit.Stretch(UIKit.Rect(name, SafeRoot));
            var c = rt.gameObject.AddComponent<Canvas>();
            c.overrideSorting = false;
            c.additionalShaderChannels = Canvas.additionalShaderChannels;
            if (raycast) rt.gameObject.AddComponent<GraphicRaycaster>();
            else
            {
                var g = rt.gameObject.AddComponent<CanvasGroup>();
                g.blocksRaycasts = false;
                g.interactable = false;
            }
            rt.SetSiblingIndex(order);
            return rt;
        }

        static void EnsureEventSystem()
        {
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                go.AddComponent<InputSystemUIInputModule>();
#else
                go.AddComponent<StandaloneInputModule>();
#endif
            }
            // Unity's default 10 px drag threshold is ~0.5 mm on a 460 dpi phone: the slightest finger roll turns a
            // tap on a button inside a ScrollRect into a drag (and cancels the click). Use ~1.5 mm instead.
            float dpi = Screen.dpi;
            int threshold = dpi > 0f ? Mathf.RoundToInt(dpi * 0.06f) : Mathf.RoundToInt(Mathf.Max(Screen.width, Screen.height) * 0.012f);
            es.pixelDragThreshold = Mathf.Max(es.pixelDragThreshold, threshold);
        }

        void UpdateLayout(bool force)
        {
            int w = Screen.width, h = Screen.height;
            Rect safe = Screen.safeArea;
            if (!force && w == _lastW && h == _lastH && safe == _lastSafe) return;
            _lastW = w;
            _lastH = h;
            _lastSafe = safe;
            if (w <= 0 || h <= 0 || SafeRoot == null) return;

            // Taller than 9:16 (modern phones) → keep the 1080 width; wider (tablets) → keep the 1920 height.
            float match = (float)w / h < DS.Space.ReferenceWidth / DS.Space.ReferenceHeight ? 0f : 1f;
            if (Scaler != null) Scaler.matchWidthOrHeight = match;
            float logW = Mathf.Log(w / DS.Space.ReferenceWidth, 2f), logH = Mathf.Log(h / DS.Space.ReferenceHeight, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
            CanvasSize = new Vector2(w / scale, h / scale);

            if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0, 0, w, h);
            SafeRoot.anchorMin = new Vector2(safe.xMin / w, safe.yMin / h);
            SafeRoot.anchorMax = new Vector2(safe.xMax / w, safe.yMax / h);
            SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
            SafeInsets = new Vector4(safe.xMin / scale, safe.yMin / scale, (w - safe.xMax) / scale, (h - safe.yMax) / scale);

            if (OnSafeAreaChanged == null) return;
            try { OnSafeAreaChanged(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            UpdateLayout(false);
            if (BackPressedThisFrame()) HandleBack();
        }

        static bool BackPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        /// <summary>Back button logic (also callable from UI "back" arrows): top popup → current screen → event.</summary>
        public void HandleBack()
        {
            if (_backFrame == Time.frameCount) return;
            _backFrame = Time.frameCount;
            var top = PopupManager.Top;
            if (top != null)
            {
                top.OnBack();
                return;
            }
            bool handled = false;
            if (CurrentScreen != null)
            {
                try { handled = CurrentScreen.OnBack(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (!handled) OnBackUnhandled?.Invoke();
        }

        void OnDestroy()
        {
            // The real manager only dies with the scene: from here on nothing may rebuild the UI.
            if (Instance == this) ShuttingDown = true;
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------------------------------- screens

        /// <summary>Instantiates the screen component on a new full-screen child of ScreensLayer and calls Build.</summary>
        public T Register<T>() where T : UIScreen
        {
            if (!_setupDone) Setup();
            var go = new GameObject(typeof(T).Name, typeof(RectTransform)) { layer = UIKit.UILayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(ScreensLayer, false);
            UIKit.Stretch(rt);
            var group = go.AddComponent<CanvasGroup>();
            var screen = go.AddComponent<T>();
            if (_screens.TryGetValue(screen.Id, out var existing) && existing != null)
            {
                Debug.LogError("[ScreenManager] Screen " + screen.Id + " is already registered.");
                Destroy(go);
                return existing as T;
            }
            screen.Root = rt;
            screen.Group = group;
            _screens[screen.Id] = screen;
            try { screen.Build(); }
            catch (Exception e) { Debug.LogException(e); }
            go.SetActive(false);
            return screen;
        }

        public UIScreen Get(ScreenId id) => _screens.TryGetValue(id, out var s) ? s : null;

        /// <summary>Typed lookup of a registered screen.</summary>
        public T Get<T>() where T : UIScreen
        {
            foreach (var kv in _screens)
                if (kv.Value is T t) return t;
            return null;
        }

        /// <summary>
        /// Switches screens with a 0.25 s cross-fade + 40 px slide (direction by tab order). Showing the current
        /// screen again re-enters it (OnHide + OnShow(arg), no transition), e.g. to restart a level.
        /// </summary>
        public void Show(ScreenId id, object arg = null)
        {
            if (!_screens.TryGetValue(id, out var next) || next == null)
            {
                Debug.LogError("[ScreenManager] Screen " + id + " is not registered.");
                return;
            }
            var prev = CurrentScreen;
            if (prev == next && next.IsVisible)
            {
                SafeHide(next);
                Current = id;
                SafeShow(next, arg);
                RaiseChanged(id);
                return;
            }

            FinishTransition();
            int dir = prev != null ? Math.Sign(TabOrder(id) - TabOrder(prev.Id)) : 0;
            if (prev != null && (TabOrder(id) < 0 || TabOrder(prev.Id) < 0)) dir = 0;
            float slide = DS.Motion.ScreenSlide;

            if (prev != null)
            {
                SafeHide(prev);
                prev.Group.blocksRaycasts = false;
                _outgoing = prev;
                Tween.Kill(prev.Group);
                Tween.Kill(prev.Root);
                Tween.Fade(prev.Group, 0f, TransitionDuration, Ease.OutQuad);
                Tween.Move(prev.Root, new Vector2(-dir * slide, 0f), TransitionDuration, Ease.OutCubic)
                    .OnComplete(FinishTransition);
            }

            next.gameObject.SetActive(true);
            next.transform.SetAsLastSibling();
            Tween.Kill(next.Group);
            Tween.Kill(next.Root);
            next.Group.alpha = 0f;
            next.Group.blocksRaycasts = true;
            next.Group.interactable = true;
            next.Root.anchoredPosition = new Vector2(dir * slide, 0f);
            CurrentScreen = next;
            Current = id;
            SafeShow(next, arg);
            Tween.Fade(next.Group, 1f, TransitionDuration, Ease.OutQuad);
            Tween.Move(next.Root, Vector2.zero, TransitionDuration, Ease.OutCubic);

            AudioManager.PlayMusic(next.Music);
            RaiseChanged(id);
        }

        void FinishTransition()
        {
            var o = _outgoing;
            _outgoing = null;
            if (o == null || o == CurrentScreen) return;
            Tween.Kill(o.Group);
            Tween.Kill(o.Root);
            o.gameObject.SetActive(false);
            o.Group.alpha = 1f;
            o.Group.blocksRaycasts = true;
            o.Root.anchoredPosition = Vector2.zero;
        }

        /// <summary>Tab order for the slide direction (Shop · Ranking · Home · Collection · Profile); Game = no slide.</summary>
        public static int TabOrder(ScreenId id)
        {
            switch (id)
            {
                case ScreenId.Shop: return 0;
                case ScreenId.Leaderboard: return 1;
                case ScreenId.Home: return 2;
                case ScreenId.Collection: return 3;
                case ScreenId.Profile: return 4;
                default: return -1;
            }
        }

        void RaiseChanged(ScreenId id)
        {
            if (OnScreenChanged == null) return;
            try { OnScreenChanged(id); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void SafeShow(UIScreen s, object arg)
        {
            s.IsVisible = true;
            try { s.OnShow(arg); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void SafeHide(UIScreen s)
        {
            s.IsVisible = false;
            try { s.OnHide(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
