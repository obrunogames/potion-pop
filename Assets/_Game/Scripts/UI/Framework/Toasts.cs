using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Toast messages on ScreenManager.TopLayer: slide down from the top (below the top bar), stay 2.2 s, slide back.
    /// Messages are queued (max 4 pending, consecutive duplicates dropped). Never blocks input.
    /// </summary>
    public static class Toasts
    {
        const int MaxQueue = 4;
        const float Height = 112f;
        /// <summary>Pill height for messages too long for one line at full width (they wrap to two lines).</summary>
        const float TallHeight = 150f;
        const float MaxWidth = 940f;

        static readonly Queue<string> _queue = new Queue<string>();
        static string _current;
        static bool _showing;
        static RectTransform _view;
        static CanvasGroup _group;
        static TMP_Text _text;
        static Image _bg;
        static Image _rim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _queue.Clear();
            _current = null;
            _showing = false;
            _view = null;
            _group = null;
            _text = null;
            _bg = null;
            _rim = null;
        }

        /// <summary>True while a toast is on screen.</summary>
        public static bool IsShowing => _showing;

        /// <summary>Shows (or queues) an already localized message.</summary>
        public static void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_showing && _view == null)
            {
                // The view was destroyed mid-animation (scene change): the completion never came, start over.
                _showing = false;
                _current = null;
            }
            if (_showing && text == _current) return;
            foreach (var q in _queue) if (q == text) return;
            if (_queue.Count >= MaxQueue) return;
            _queue.Enqueue(text);
            if (!_showing) Next();
        }

        /// <summary>Hides the current toast immediately and drops the queue.</summary>
        public static void Clear()
        {
            _queue.Clear();
            if (_view != null)
            {
                Tween.Kill(_view);
                Tween.Kill(_group);
                _view.gameObject.SetActive(false);
            }
            _showing = false;
            _current = null;
        }

        static void Next()
        {
            if (_queue.Count == 0)
            {
                _showing = false;
                _current = null;
                if (_view != null) _view.gameObject.SetActive(false);
                return;
            }
            string text = _queue.Dequeue();
            if (!EnsureView())
            {
                Debug.Log("[Toast] " + text);
                _queue.Clear();
                _showing = false;
                return;
            }
            _showing = true;
            _current = text;
            _text.text = text;

            // Fit the pill to the one-line text width (min 420, max 940 units). Longer messages (pt/es errors) wrap to two
            // lines in a taller pill instead of running off the screen edges; auto-size shrinks them a little if needed.
            float pref = _text.GetPreferredValues(text, 2000f, Height).x;
            float width = Mathf.Clamp(pref + 96f, 420f, MaxWidth);
            float h = pref + 96f > MaxWidth ? TallHeight : Height;
            _view.sizeDelta = new Vector2(width, h);
            if (_rim != null) _rim.rectTransform.offsetMin = new Vector2(6f, h * 0.5f);   // gloss keeps covering the top half

            float shown = -(DS.Space.TopBarHeight + DS.Space.S);
            float hidden = h + 40f;
            _view.gameObject.SetActive(true);
            _view.SetAsLastSibling();
            _view.anchoredPosition = new Vector2(0f, hidden);
            _group.alpha = 1f;
            Tween.Kill(_view);
            Tween.Kill(_group);
            Tween.Move(_view, new Vector2(0f, shown), 0.35f, Ease.OutBack);
            Tween.Move(_view, new Vector2(0f, hidden), 0.25f, Ease.InBack).SetDelay(0.35f + DS.Motion.ToastDuration);
            Tween.Fade(_group, 0f, 0.25f).SetDelay(0.35f + DS.Motion.ToastDuration).OnComplete(Next);
        }

        static bool EnsureView()
        {
            if (_view != null) return true;
            var sm = ScreenManager.Instance;
            if (sm == null || sm.TopLayer == null) return false;
            _view = UIKit.Rect("Toast", sm.TopLayer);
            UIKit.Place(_view, new Vector2(0.5f, 1f), new Vector2(600f, Height), Vector2.zero);
            _group = _view.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            UIKit.Shadow(_view, 24f, -10f, 0.3f);
            _bg = UIKit.Capsule(_view, Vector2.zero, DS.WithAlpha(DS.Colors.Ink, 0.94f));
            UIKit.Stretch(_bg.rectTransform);
            _rim = UIKit.Capsule(_view, Vector2.zero, new Color(1f, 1f, 1f, 0.08f));   // top gloss
            UIKit.Stretch(_rim.rectTransform, 6, 6, 6, Height * 0.5f);
            _text = UIKit.Text(_view, "", TextStyle.BodyLight, new Vector2(600f, Height));
            UIKit.Stretch(_text.rectTransform, 40, 6, 40, 10);
            // Wrapping on (DS.Apply's default): a message wider than the 860-unit text box wraps instead of overflowing the
            // pill and the screen (the one-line width above is measured without the box, so short toasts look the same).
            _text.textWrappingMode = TextWrappingModes.Normal;
            _view.gameObject.SetActive(false);
            return true;
        }
    }
}
