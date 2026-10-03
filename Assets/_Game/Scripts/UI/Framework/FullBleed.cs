using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>
    /// Stretches a rect over the whole screen although its parent lives inside the safe area: offsets it by the
    /// SafeRoot insets (updated when the safe area changes) plus an optional overscan (so screen shakes never reveal
    /// an edge). The parent must span the SafeRoot (a screen Root, a layer, a popup root).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FullBleed : MonoBehaviour
    {
        /// <summary>Extra margin (canvas units) beyond the screen edges.</summary>
        public float overscan;

        ScreenManager _sm;

        void OnEnable()
        {
            _sm = ScreenManager.Instance;
            if (_sm != null) _sm.OnSafeAreaChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            if (_sm != null) _sm.OnSafeAreaChanged -= Apply;
            _sm = null;
        }

        public void Apply()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            var sm = ScreenManager.Instance;
            Vector4 i = sm != null ? sm.SafeInsets : Vector4.zero;   // left, bottom, right, top
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-i.x - overscan, -i.y - overscan);
            rt.offsetMax = new Vector2(i.z + overscan, i.w + overscan);
        }
    }
}
