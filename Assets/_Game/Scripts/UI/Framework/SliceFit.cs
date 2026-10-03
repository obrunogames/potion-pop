using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Keeps a 9-sliced Image looking right at any size: re-applies UISprites.FitSlices whenever the rect or the
    /// sprite changes. Height mode (pills, buttons, bars) scales caps with the rect height; Corner mode (panels)
    /// keeps a constant corner size. Added automatically by UIKit to every sliced image.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public sealed class SliceFit : MonoBehaviour
    {
        public enum Mode { Height, Corner }

        public Mode mode = Mode.Height;
        /// <summary>Corner mode: on-screen size (canvas units) of the largest sprite border.</summary>
        public float cornerSize = 40f;

        Image _img;
        bool _applying;

        public Image Image => _img != null ? _img : (_img = GetComponent<Image>());

        public static SliceFit Attach(Image img, Mode mode, float cornerSize = 40f)
        {
            if (img == null) return null;
            var f = img.GetComponent<SliceFit>();
            if (f == null) f = img.gameObject.AddComponent<SliceFit>();
            f.mode = mode;
            f.cornerSize = cornerSize;
            f.Apply();
            return f;
        }

        public void Apply()
        {
            if (_applying) return;
            _applying = true;
            try { UISprites.FitSlices(Image, mode, cornerSize); }
            finally { _applying = false; }
        }

        void OnEnable()
        {
            // Image.sprite / color setters dirty the vertices: re-fit then, so sprite swaps need no extra call.
            if (Image != null) Image.RegisterDirtyVerticesCallback(Apply);
            Apply();
        }

        void OnDisable()
        {
            if (_img != null) _img.UnregisterDirtyVerticesCallback(Apply);
        }

        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Apply(); }
    }
}
