using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Services
{
    /// <summary>
    /// Full-screen fake ad (canvas sorting order 5000) for the Editor / builds without AdMob: "Ad (test)", the placement,
    /// a 3 s countdown and then it closes by itself. Rewarded ads can be skipped early (no reward) to test that path.
    /// Uses real time, so it also runs while Time.timeScale is 0.
    /// </summary>
    public sealed class SimulatedAdView : MonoBehaviour
    {
        public const int SortingOrder = 5000;
        public const float DefaultSeconds = 3f;

        bool _rewarded;
        bool _finished;
        float _seconds;
        float _startedAt;
        Action<bool> _onFinished;
        Text _countdown;
        RectTransform _progressFill;

        /// <summary>Shows the fake ad. onFinished(watchedToTheEnd) is called exactly once.</summary>
        public static void Show(bool rewarded, string subtitle, float seconds, Action<bool> onFinished)
        {
            if (!Application.isPlaying)
            {
                ServicesRunner.SafeInvoke(onFinished, false);
                return;
            }
            Canvas canvas = ServicesUI.CreateCanvas("SPSimulatedAd", SortingOrder);
            var view = canvas.gameObject.AddComponent<SimulatedAdView>();
            view._rewarded = rewarded;
            view._seconds = Mathf.Max(0.5f, seconds);
            view._onFinished = onFinished;
            view.Build((RectTransform)canvas.transform, subtitle);
        }

        void Build(RectTransform root, string subtitle)
        {
            _startedAt = Time.realtimeSinceStartup;

            Image backdrop = ServicesUI.Panel(root, "Backdrop", ServicesUI.Backdrop, true);
            ServicesUI.Stretch(backdrop.rectTransform);
            RectTransform safe = ServicesUI.SafeArea(root);

            // "Ad (test)" badge in the corner, like real ads.
            Image badge = ServicesUI.Panel(safe, "Badge", ServicesUI.Accent);
            ServicesUI.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(330f, 86f));
            Text badgeText = ServicesUI.Label(badge.transform, "Text", Loc.T("ads.test_badge"), 44, ServicesUI.Ink);
            ServicesUI.Stretch(badgeText.rectTransform, 12, 4, 12, 4);

            // The "creative".
            Image card = ServicesUI.Panel(safe, "Card", ServicesUI.Brand);
            ServicesUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(940f, 700f));
            Image inner = ServicesUI.Panel(card.transform, "Inner", ServicesUI.BrandDark);
            ServicesUI.Stretch(inner.rectTransform, 16, 16, 16, 16);

            Text title = ServicesUI.Label(inner.transform, "Title",
                Loc.T(_rewarded ? "ads.rewarded_title" : "ads.interstitial_title"), 64, Color.white);
            ServicesUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(860f, 90f));

            if (!string.IsNullOrEmpty(subtitle))
            {
                Text sub = ServicesUI.Label(inner.transform, "Subtitle", subtitle, 42, ServicesUI.Accent);
                ServicesUI.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(860f, 64f));
            }

            _countdown = ServicesUI.Label(inner.transform, "Countdown", "", 150, Color.white);
            ServicesUI.Place(_countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(600f, 200f));

            Image track = ServicesUI.Panel(inner.transform, "Track", new Color(0f, 0f, 0f, 0.35f));
            ServicesUI.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(780f, 34f));
            Image fill = ServicesUI.Panel(track.transform, "Fill", ServicesUI.Accent);
            _progressFill = fill.rectTransform;
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = _progressFill.offsetMax = Vector2.zero;

            if (_rewarded)
            {
                Text hint = ServicesUI.Label(safe, "Hint", Loc.T("ads.skip_no_reward"), 36, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Normal);
                ServicesUI.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(900f, 60f));
                Button skip = ServicesUI.Button(safe, "Skip", Loc.T("ads.skip"), ServicesUI.Gray, 48, () => Finish(false));
                ServicesUI.Place((RectTransform)skip.transform, new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(420f, 120f));
            }
            Refresh();
        }

        void Update()
        {
            if (_finished) return;
            Refresh();
            if (Time.realtimeSinceStartup - _startedAt >= _seconds) Finish(true);
        }

        void Refresh()
        {
            float elapsed = Time.realtimeSinceStartup - _startedAt;
            int left = Mathf.Max(0, Mathf.CeilToInt(_seconds - elapsed));
            if (_countdown != null) _countdown.text = left.ToString();
            if (_progressFill != null) _progressFill.anchorMax = new Vector2(Mathf.Clamp01(elapsed / _seconds), 1f);
        }

        void Finish(bool watched)
        {
            if (_finished) return;
            _finished = true;
            Destroy(gameObject);
            ServicesRunner.SafeInvoke(_onFinished, watched);
        }

        void OnDestroy()
        {
            // Play Mode stopped / object destroyed externally: never leave the callback hanging.
            if (_finished) return;
            _finished = true;
            ServicesRunner.SafeInvoke(_onFinished, false);
        }
    }
}
