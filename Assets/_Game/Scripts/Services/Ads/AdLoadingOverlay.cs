using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Services
{
    /// <summary>
    /// "Loading ad…" wait shown when the player asks for a rewarded ad that is not loaded yet. Polls a readiness check
    /// for a few seconds (cancellable); on timeout shows "no ad available" briefly. onResult(ready) exactly once.
    /// </summary>
    public sealed class AdLoadingOverlay : MonoBehaviour
    {
        const float MessageSeconds = 1.8f;

        Func<bool> _isReady;
        Action<bool> _onResult;
        float _deadline;
        float _closeAt = -1f;
        bool _finished;
        int _dots;
        Text _label;
        GameObject _cancel;

        public static void Show(float waitSeconds, Func<bool> isReady, Action<bool> onResult)
        {
            if (!Application.isPlaying)
            {
                ServicesRunner.SafeInvoke(onResult, false);
                return;
            }
            Canvas canvas = ServicesUI.CreateCanvas("SPAdLoading", SimulatedAdView.SortingOrder);
            var view = canvas.gameObject.AddComponent<AdLoadingOverlay>();
            view._isReady = isReady;
            view._onResult = onResult;
            view._deadline = Time.realtimeSinceStartup + Mathf.Max(0.5f, waitSeconds);
            view.Build((RectTransform)canvas.transform);
        }

        void Build(RectTransform root)
        {
            Image backdrop = ServicesUI.Panel(root, "Backdrop", new Color(0.102f, 0.043f, 0.2f, 0.75f), true);
            ServicesUI.Stretch(backdrop.rectTransform);

            Image card = ServicesUI.Panel(root, "Card", ServicesUI.Brand);
            ServicesUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 440f));
            Image inner = ServicesUI.Panel(card.transform, "Inner", ServicesUI.Cream);
            ServicesUI.Stretch(inner.rectTransform, 14, 14, 14, 14);

            _label = ServicesUI.Label(inner.transform, "Label", Loc.T("ads.loading"), 50, ServicesUI.Ink);
            ServicesUI.Place(_label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(740f, 180f));

            Button cancel = ServicesUI.Button(inner.transform, "Cancel", Loc.T("ads.cancel"), ServicesUI.Danger, 46, () => Finish(false));
            ServicesUI.Place((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(380f, 116f));
            _cancel = cancel.gameObject;
        }

        void Update()
        {
            if (_finished) return;
            float now = Time.realtimeSinceStartup;
            if (_closeAt >= 0f)
            {
                if (now >= _closeAt) Finish(false);
                return;
            }
            bool ready = false;
            try { ready = _isReady != null && _isReady(); }
            catch (Exception e) { Debug.LogException(e); }
            if (ready)
            {
                Finish(true);
                return;
            }
            if (now >= _deadline)
            {
                _label.text = Loc.T("ads.unavailable");
                if (_cancel != null) _cancel.SetActive(false);
                _closeAt = now + MessageSeconds;
                return;
            }
            int dots = 1 + (int)(now * 2.5f) % 3;
            if (dots != _dots)
            {
                _dots = dots;
                _label.text = Loc.T("ads.loading") + new string('.', dots);
            }
        }

        void Finish(bool ready)
        {
            if (_finished) return;
            _finished = true;
            Destroy(gameObject);
            ServicesRunner.SafeInvoke(_onResult, ready);
        }

        void OnDestroy()
        {
            if (_finished) return;
            _finished = true;
            ServicesRunner.SafeInvoke(_onResult, false);
        }
    }
}
