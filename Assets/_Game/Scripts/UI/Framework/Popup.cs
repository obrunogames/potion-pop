// ============================================================================================================
// Popups: dim overlay + panel_popup 9-slice with soft shadow, title ribbon overlapping the top edge, round close
// button, Content inset 56 px (below the ribbon). PopIn OutBack(1.4) 0.45 s; Close animates out, destroys and
// raises OnClosed. PopupManager keeps the stack (only the top popup dims the screen).
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Base popup: dim overlay (tap outside closes if ShowClose), panel PopIn, optional ribbon title and close button.
    /// Usage: PopupManager.Show&lt;MyPopup&gt;(p =&gt; p.SetData(...)) — setup runs before BuildContent.
    /// </summary>
    public abstract class Popup : MonoBehaviour
    {
        protected RectTransform Panel;
        protected RectTransform Content;
        public event Action OnClosed;
        protected virtual string TitleKey => null;
        protected virtual bool ShowClose => true;
        protected virtual bool CloseOnOverlayTap => ShowClose;
        protected virtual Vector2 PanelSize => new Vector2(920, 1100);
        /// <summary>Build the inside of the panel. `content` is the cream inner area (padding applied).</summary>
        protected abstract void BuildContent(RectTransform content);
        public bool IsClosing { get; private set; }

        // ---- additions
        /// <summary>Dim overlay image (full screen).</summary>
        protected Image Overlay;
        /// <summary>The 9-sliced panel surface.</summary>
        protected Image PanelImage;
        /// <summary>Title ribbon (null without TitleKey).</summary>
        protected RectTransform Ribbon;
        /// <summary>Round close button (null when ShowClose is false).</summary>
        protected UIButton CloseButton;
        /// <summary>Panel surface sprite (override for special popups).</summary>
        protected virtual string PanelSprite => "panel_popup";
        /// <summary>True once the PopIn animation finished.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>The popup root (full-screen child of PopupLayer).</summary>
        public RectTransform Root => (RectTransform)transform;

        CanvasGroup _group;
        CanvasGroup _panelGroup;
        bool _built;
        bool _animatedIn;
        bool _overlayVisible = true;

        /// <summary>Called when the PopIn animation finished.</summary>
        protected virtual void OnOpened() { }
        /// <summary>Called when Close() starts (before the close animation).</summary>
        protected virtual void OnClosing() { }
        /// <summary>Overlay tapped. Default: Close() when CloseOnOverlayTap.</summary>
        protected virtual void OnOverlayTap() { if (CloseOnOverlayTap) Close(); }

        public void Close()
        {
            if (IsClosing) return;
            IsClosing = true;
            IsOpen = false;
            PopupManager.Remove(this);
            try { OnClosing(); }
            catch (Exception e) { Debug.LogException(e); }
            if (_animatedIn) AudioManager.Play(Sfx.PopupClose);

            if (_group != null) _group.blocksRaycasts = false;
            // Closed before it was ever shown (e.g. BuildContent decided there is nothing to show): no pop-out.
            if (Panel == null || !_animatedIn || !isActiveAndEnabled)
            {
                Finish();
                return;
            }
            Tween.Kill(Panel);
            if (_panelGroup != null) Tween.Kill(_panelGroup);
            if (Overlay != null) Tween.Kill(Overlay);
            float d = DS.Motion.PopOutDuration;
            Tween.Scale(Panel, DS.Motion.PopOutTo, d, Ease.InBack);
            if (_panelGroup != null) Tween.Fade(_panelGroup, 0f, d, Ease.InQuad);
            Tween.Delay(d + 0.02f, Finish).SetLink(this);
            if (Overlay != null) Tween.Fade(Overlay, 0f, d + 0.02f);
        }

        /// <summary>Closes without animation (destroyed at the end of the frame, OnClosed raised now).</summary>
        public void CloseImmediate()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                IsOpen = false;
                PopupManager.Remove(this);
                try { OnClosing(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Finish();
        }

        void Finish()
        {
            if (this == null) return;
            var handler = OnClosed;
            OnClosed = null;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>Back button / ESC. Default: Close() if ShowClose.</summary>
        public virtual void OnBack() { if (ShowClose) Close(); }

        /// <summary>Called by PopupManager after setup.</summary>
        public void BuildInternal()
        {
            if (_built) return;
            _built = true;
            var root = Root;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            // Overlay (full screen, catches taps).
            Overlay = UIKit.NewImage(root, "Overlay", null, DS.Colors.Overlay);
            UIKit.FullBleed(Overlay.rectTransform);
            Overlay.raycastTarget = true;
            Overlay.gameObject.AddComponent<OverlayTap>().popup = this;

            // Panel, clamped to the safe area and shifted down so panel + ribbon stay centered.
            var sm = ScreenManager.Instance;
            Vector2 avail = sm != null ? sm.SafeSize : new Vector2(DS.Space.ReferenceWidth, DS.Space.ReferenceHeight);
            Vector2 size = PanelSize;
            bool hasRibbon = !string.IsNullOrEmpty(TitleKey);
            float ribbonW = Mathf.Min(size.x * 0.8f, 760f);
            float ribbonH = hasRibbon ? UIKit.RibbonHeight(ribbonW) : 0f;
            float above = hasRibbon ? Mathf.Max(0f, ribbonH - DS.Space.RibbonOverlap) : 0f;
            size.x = Mathf.Min(size.x, avail.x - DS.Space.ScreenMargin * 2f);
            size.y = Mathf.Min(size.y, avail.y - above - DS.Space.ScreenMargin * 2f);

            Panel = UIKit.Rect("Panel", root);
            UIKit.Place(Panel, new Vector2(0.5f, 0.5f), size, new Vector2(0f, -above * 0.5f));
            _panelGroup = Panel.gameObject.AddComponent<CanvasGroup>();

            UIKit.Shadow(Panel, 46f, -18f, DS.Colors.Shadow.a);
            PanelImage = UIKit.Panel(Panel, PanelSprite, size);
            UIKit.Stretch(PanelImage.rectTransform);
            PanelImage.raycastTarget = true;   // taps on the panel never reach the overlay

            float top = hasRibbon ? DS.Space.RibbonOverlap + 28f : DS.Space.PopupPadding;
            Content = UIKit.Stretch(UIKit.Rect("Content", Panel), DS.Space.PopupPadding, Mathf.Max(top, DS.Space.PopupPadding),
                DS.Space.PopupPadding, DS.Space.PopupPadding);

            if (hasRibbon)
            {
                Ribbon = UIKit.Ribbon(Panel, TitleKey, ribbonW);
                UIKit.Place(Ribbon, new Vector2(0.5f, 1f), new Vector2(ribbonW, ribbonH), new Vector2(0f, ribbonH - DS.Space.RibbonOverlap));
            }

            if (ShowClose)
            {
                const float s = 116f;
                CloseButton = UIKit.IconButton(Panel, "btn_round_close", s, OnCloseButton);
                CloseButton.Pressable.playSound = false;   // Close() plays Sfx.PopupClose
                var crt = (RectTransform)CloseButton.transform;
                UIKit.Place(crt, new Vector2(1f, 1f), new Vector2(s, s), Vector2.zero);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = new Vector2(-30f, -30f);
                if (!UISprites.Exists("btn_round_close") && CloseButton.icon != null)
                {
                    // Missing art: red circle with a drawn white cross (no text, nothing to translate).
                    CloseButton.icon.color = DS.Colors.Danger;
                    for (int i = 0; i < 2; i++)
                    {
                        var bar = UIKit.Capsule(CloseButton.Visual, new Vector2(s * 0.5f, s * 0.13f), Color.white);
                        bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                    }
                }
            }

            try { BuildContent(Content); }
            catch (Exception e) { Debug.LogException(e); }

            if (!IsClosing) AnimateIn();   // BuildContent may decide to Close() right away
        }

        /// <summary>Close button pressed (default: Close()).</summary>
        protected virtual void OnCloseButton() => Close();

        void AnimateIn()
        {
            _animatedIn = true;
            AudioManager.Play(Sfx.PopupOpen);
            float a = _overlayVisible ? DS.Colors.Overlay.a : 0f;
            Overlay.color = DS.WithAlpha(DS.Colors.Overlay, 0f);
            Tween.Fade(Overlay, a, DS.Motion.Base, Ease.OutQuad);
            _panelGroup.alpha = 0f;
            Tween.Fade(_panelGroup, 1f, DS.Motion.Fast, Ease.OutQuad);
            Tween.PopIn(Panel).OnComplete(() =>
            {
                if (IsClosing) return;
                IsOpen = true;
                try { OnOpened(); }
                catch (Exception e) { Debug.LogException(e); }
            });
        }

        /// <summary>Only the top popup shows its dim overlay (no stacked darkening).</summary>
        internal void SetOverlayVisible(bool visible)
        {
            if (_overlayVisible == visible) return;
            _overlayVisible = visible;
            if (Overlay == null || IsClosing) return;
            Tween.Kill(Overlay);
            Tween.Fade(Overlay, visible ? DS.Colors.Overlay.a : 0f, DS.Motion.Base, Ease.OutQuad);
        }

        internal void HandleOverlayTap()
        {
            if (IsClosing || PopupManager.Top != this) return;
            OnOverlayTap();
        }

        sealed class OverlayTap : MonoBehaviour, IPointerClickHandler
        {
            public Popup popup;
            public void OnPointerClick(PointerEventData e) { if (popup != null) popup.HandleOverlayTap(); }
        }
    }

    public static class PopupManager
    {
        static readonly List<Popup> _stack = new List<Popup>();
        static readonly List<Popup> _buffer = new List<Popup>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _stack.Clear();
            _buffer.Clear();
            OnStackChanged = null;
        }

        public static bool AnyOpen { get { Prune(); return _stack.Count > 0; } }

        /// <summary>Top-most popup that is not closing (null when none).</summary>
        public static Popup Top { get { Prune(); return _stack.Count > 0 ? _stack[_stack.Count - 1] : null; } }

        public static event Action OnStackChanged;

        /// <summary>Number of open (not closing) popups.</summary>
        public static int Count { get { Prune(); return _stack.Count; } }

        public static T Show<T>(Action<T> setup = null) where T : Popup
        {
            var sm = ScreenManager.EnsureInstance();
            if (sm == null || sm.PopupLayer == null) return null;   // edit mode / shutting down: never build UI
            var go = new GameObject(typeof(T).Name, typeof(RectTransform)) { layer = UIKit.UILayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(sm.PopupLayer, false);
            UIKit.Stretch(rt);
            rt.SetAsLastSibling();
            go.AddComponent<CanvasGroup>();
            var popup = go.AddComponent<T>();
            Prune();
            _stack.Add(popup);
            try { setup?.Invoke(popup); }
            catch (Exception e) { Debug.LogException(e); }
            popup.BuildInternal();
            RefreshOverlays();
            Raise();
            return popup;
        }

        public static void CloseAll()
        {
            Prune();
            _buffer.Clear();
            _buffer.AddRange(_stack);
            for (int i = _buffer.Count - 1; i >= 0; i--)
                if (_buffer[i] != null) _buffer[i].Close();
            _buffer.Clear();
        }

        /// <summary>CloseAll without animations (e.g. before a scene/screen reset).</summary>
        public static void CloseAll(bool animate)
        {
            if (animate) { CloseAll(); return; }
            Prune();
            _buffer.Clear();
            _buffer.AddRange(_stack);
            for (int i = _buffer.Count - 1; i >= 0; i--)
                if (_buffer[i] != null) _buffer[i].CloseImmediate();
            _buffer.Clear();
        }

        public static bool IsOpen<T>() where T : Popup => Get<T>() != null;

        /// <summary>The top-most open popup of type T, or null.</summary>
        public static T Get<T>() where T : Popup
        {
            Prune();
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i] is T t && !t.IsClosing) return t;
            return null;
        }

        internal static void Remove(Popup p)
        {
            if (!_stack.Remove(p)) return;
            RefreshOverlays();
            Raise();
        }

        static void Prune()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i] == null) _stack.RemoveAt(i);
        }

        static void RefreshOverlays()
        {
            Prune();
            for (int i = 0; i < _stack.Count; i++) _stack[i].SetOverlayVisible(i == _stack.Count - 1);
        }

        static void Raise()
        {
            if (OnStackChanged == null) return;
            try { OnStackChanged(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
