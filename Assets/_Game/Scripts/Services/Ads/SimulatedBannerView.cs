using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Services
{
    /// <summary>
    /// Fake anchored banner at the bottom of the safe area (canvas sorting 4900, above the game UI like a native banner,
    /// below the full-screen simulated ads). Height ≈ a phone adaptive banner: 150 units of the 1080-wide canvas.
    /// </summary>
    public sealed class SimulatedBannerView : MonoBehaviour
    {
        public const int SortingOrder = 4900;
        public const float HeightCanvasUnits = 150f;

        static SimulatedBannerView _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        public static bool Visible => _instance != null && _instance.gameObject.activeSelf;

        /// <summary>Banner height in screen pixels (0 when hidden).</summary>
        public static float HeightPixels => Visible ? HeightCanvasUnits * Screen.width / ServicesUI.ReferenceWidth : 0f;

        public static void Show()
        {
            if (!Application.isPlaying) return;
            if (_instance == null)
            {
                Canvas canvas = ServicesUI.CreateCanvas("SPSimulatedBanner", SortingOrder);
                _instance = canvas.gameObject.AddComponent<SimulatedBannerView>();
                _instance.Build((RectTransform)canvas.transform);
            }
            _instance.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }

        void Build(RectTransform root)
        {
            RectTransform safe = ServicesUI.SafeArea(root);
            Image bar = ServicesUI.Panel(safe, "Banner", new Color(0.93f, 0.93f, 0.95f, 1f), true);
            RectTransform rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, HeightCanvasUnits);

            Image badge = ServicesUI.Panel(bar.transform, "Badge", ServicesUI.Accent);
            ServicesUI.Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(110f, 56f));
            Text badgeText = ServicesUI.Label(badge.transform, "Text", Loc.T("ads.badge"), 30, ServicesUI.Ink);
            ServicesUI.Stretch(badgeText.rectTransform, 4, 2, 4, 2);

            Text label = ServicesUI.Label(bar.transform, "Label", Loc.T("ads.banner_test"), 38, ServicesUI.Ink, TextAnchor.MiddleCenter, FontStyle.Normal);
            ServicesUI.Stretch(label.rectTransform, 150, 10, 40, 10);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
