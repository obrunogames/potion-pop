// ============================================================================================================
// Drifting clouds across the Home sky (fx_poof tinted white). Bigger, nearer clouds move faster and are more
// opaque (parallax); they wrap around the screen edges. Positions are relative to the full-bleed holder they live
// in, so they follow any aspect ratio. Unscaled time, no allocations per frame.
// ============================================================================================================
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class HomeClouds : MonoBehaviour
    {
        struct Cloud
        {
            public RectTransform rt;
            public float speed;      // canvas units per second
            public float y01;        // vertical position (fraction of the holder height from the top)
            public float width;
            public float bobPhase;
        }

        Cloud[] _clouds;
        RectTransform _rt;
        float _time;

        /// <summary>Adds `count` clouds to a full-bleed holder, in the band [top01, bottom01] measured from the top.</summary>
        public static HomeClouds Create(RectTransform holder, int count, float top01, float bottom01)
        {
            var layer = UIKit.Rect("Clouds", holder);
            UIKit.Stretch(layer);
            var c = layer.gameObject.AddComponent<HomeClouds>();
            c._rt = layer;
            c._clouds = new Cloud[count];
            var sprite = UISprites.Get("fx_poof");
            if (sprite == null) sprite = UISprites.Glow;
            for (int i = 0; i < count; i++)
            {
                float depth = count > 1 ? i / (float)(count - 1) : 1f;          // 0 = far, 1 = near
                float w = Mathf.Lerp(200f, 430f, depth) * Random.Range(0.85f, 1.15f);
                var img = UIKit.NewImage(layer, "Cloud", sprite, new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 0.9f, depth)));
                img.preserveAspect = true;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w, w * 0.6f);
                if (Random.value < 0.5f) rt.localScale = new Vector3(-1f, 1f, 1f);
                c._clouds[i] = new Cloud
                {
                    rt = rt,
                    speed = Mathf.Lerp(9f, 30f, depth) * Random.Range(0.85f, 1.15f),
                    y01 = Mathf.Lerp(top01, bottom01, Random.value),
                    width = w,
                    bobPhase = Random.value * 6.28f,
                };
            }
            c.Scatter();
            return c;
        }

        /// <summary>Spreads the clouds over the whole width (called once; afterwards they drift and wrap).</summary>
        void Scatter()
        {
            if (_clouds == null || _rt == null) return;
            float w = Mathf.Max(1080f, _rt.rect.width);
            float h = Mathf.Max(1920f, _rt.rect.height);
            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                float x = (i + Random.Range(0.1f, 0.9f)) / _clouds.Length * (w + c.width) - c.width * 0.5f;
                c.rt.anchoredPosition = new Vector2(x, -c.y01 * h);
            }
        }

        void Update()
        {
            if (_clouds == null || _rt == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            Rect r = _rt.rect;
            float w = r.width, h = r.height;
            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                if (c.rt == null) continue;
                Vector2 p = c.rt.anchoredPosition;
                p.x += c.speed * dt;
                if (p.x - c.width * 0.6f > w) p.x = -c.width * 0.6f;
                p.y = -c.y01 * h + Mathf.Sin(_time * 0.35f + c.bobPhase) * 6f;
                c.rt.anchoredPosition = p;
            }
        }
    }
}
