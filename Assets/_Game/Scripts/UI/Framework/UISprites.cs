// ============================================================================================================
// Sprite access for the UI: Art.Get first, then procedural fallbacks generated at runtime for the "ui_*" helper
// sprites (rounded rect, circle, ring, soft shadow, glow, confetti...) so the UI renders correctly even before the
// art pipeline ran. Also hosts the 9-slice fitting rules (SliceFit) and a non-drawing raycast target (HitArea).
// ============================================================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public static class UISprites
    {
        static readonly Dictionary<string, Sprite> _procedural = new Dictionary<string, Sprite>();
        static readonly HashSet<string> _warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _procedural.Clear();
            _warned.Clear();
        }

        /// <summary>
        /// Sprite by name: the real art when the pipeline provides it (Art.Exists → Art.Get); otherwise the generated
        /// stand-in for "ui_*" helpers, or null for anything else (callers then draw their design-system fallback:
        /// tinted candy pill for buttons, rounded rects for panels, a soft dot for icons). Logs each miss once.
        /// </summary>
        public static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (SafeExists(name))
            {
                try { return Art.Get(name); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            var generated = Procedural(name);
            if (generated != null) return generated;
            if (_warned.Add(name)) Debug.LogWarning("[UI] Missing sprite '" + name + "' (using a design-system fallback).");
            return null;
        }

        /// <summary>True when the art pipeline provides this sprite (Art.Exists), never throws.</summary>
        public static bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return SafeExists(name);
        }

        static bool SafeExists(string name)
        {
            try { return Art.Exists(name); }
            catch (System.Exception) { return false; }
        }

        // Shortcuts to the helpers (Art version when present, generated otherwise).
        public static Sprite Rounded => Get("ui_rounded");
        public static Sprite Circle => Get("ui_circle");
        public static Sprite Ring => Get("ui_ring");
        public static Sprite SoftShadow => Get("ui_soft_shadow");
        public static Sprite Glow => Get("ui_glow");
        public static Sprite Pixel => Get("ui_pixel");
        public static Sprite Confetti => Get("ui_confetti");
        /// <summary>White capsule (round caps = half the height), sliced horizontally: pills, switches, wide badges.</summary>
        public static Sprite Capsule => Get("ui_capsule");

        /// <summary>
        /// ui_rounded has a corner radius smaller than its 9-slice border (art: radius 40 / border 44; generated:
        /// 28 / 30). Corner-mode slicing sizes the border, so a wanted on-screen radius r needs a border of r * this.
        /// </summary>
        public const float RoundedBorderPerRadius = 1.1f;
        /// <summary>Generated 3D-ish candy pill (white face, darker lip). Tinted fallback for btn_* art.</summary>
        public static Sprite ButtonPill => Procedural("ui_fallback_button");
        /// <summary>Generated glossy bar fill (white highlight, shaded bottom); tint it with any color.</summary>
        public static Sprite BarFill => Procedural("ui_fallback_bar");

        /// <summary>Generated sprite for a helper name, or null for unknown names.</summary>
        public static Sprite Procedural(string name)
        {
            if (_procedural.TryGetValue(name, out var cached) && cached != null) return cached;
            Sprite s;
            switch (name)
            {
                case "ui_rounded": s = MakeRounded(64, 28, 30); break;
                case "ui_capsule": s = MakeCapsule(128, 64); break;
                case "ui_circle": s = MakeCircle(128); break;
                case "ui_ring": s = MakeRing(128, 9f); break;
                case "ui_soft_shadow": s = MakeSoftShadow(128, 36); break;
                case "ui_glow": s = MakeGlow(128); break;
                case "ui_pixel": s = MakeSolid(4, 4, 0); break;
                case "ui_confetti": s = MakeSolid(16, 24, 3); break;
                case "ui_star_small": s = MakeGlow(64); break;
                case "ui_gradient_v": s = MakeGradientV(4, 64); break;
                case "ui_fallback_button": s = MakeButtonPill(136, 68); break;
                case "ui_fallback_bar": s = MakeBarFill(96, 48); break;
                default: return null;
            }
            s.name = name;
            _procedural[name] = s;
            return s;
        }

        // ---------------------------------------------------------------------------------------- slicing

        /// <summary>
        /// Configures a 9-slice image for its current rect: Sliced type when the sprite has borders, and a
        /// pixelsPerUnitMultiplier so the caps scale with the rect (Height: the full sprite height spans the rect height;
        /// Corner: the biggest border is drawn `cornerSize` units). Caps never exceed half of the rect width/height.
        /// </summary>
        public static void FitSlices(Image img, SliceFit.Mode mode, float cornerSize)
        {
            if (img == null) return;
            var sp = img.sprite;
            if (sp == null) return;
            Vector4 b = sp.border;
            if (b == Vector4.zero)
            {
                if (img.type == Image.Type.Sliced) img.type = Image.Type.Simple;
                return;
            }
            if (img.type != Image.Type.Sliced) img.type = Image.Type.Sliced;
            img.fillCenter = true;

            Rect r = img.rectTransform.rect;
            float w = Mathf.Max(1f, r.width), h = Mathf.Max(1f, r.height);
            // Image.pixelsPerUnit = sprite PPU / canvas reference PPU (the multiplier is applied on top of it).
            float basePpu = Mathf.Max(0.0001f, img.pixelsPerUnit);
            float m;
            if (mode == SliceFit.Mode.Height) m = sp.rect.height / (h * basePpu);
            else m = Mathf.Max(Mathf.Max(b.x, b.z), Mathf.Max(b.y, b.w)) / (Mathf.Max(1f, cornerSize) * basePpu);
            m = Mathf.Max(m, Mathf.Max(b.x, b.z) / (0.5f * w * basePpu));
            m = Mathf.Max(m, Mathf.Max(b.y, b.w) / (0.5f * h * basePpu));
            m = Mathf.Max(0.01f, m);
            if (Mathf.Abs(img.pixelsPerUnitMultiplier - m) > 0.0005f) img.pixelsPerUnitMultiplier = m;
        }

        // ---------------------------------------------------------------------------------------- generators

        static Sprite Finish(Texture2D tex, Color32[] px, Vector4 border)
        {
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        static Texture2D NewTex(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
        }

        /// <summary>Signed distance to a rounded rectangle centered in (w,h) with corner radius r (negative inside).</summary>
        static float RoundedDist(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        static byte A(float a) => (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);

        static Sprite MakeRounded(int size, int radius, int border)
        {
            var tex = NewTex(size, size, "ui_rounded");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDist(x + 0.5f, y + 0.5f, size, size, radius);
                px[y * size + x] = new Color32(255, 255, 255, A(0.5f - d));
            }
            return Finish(tex, px, new Vector4(border, border, border, border));
        }

        static Sprite MakeCapsule(int w, int h)
        {
            var tex = NewTex(w, h, "ui_capsule");
            var px = new Color32[w * h];
            float r = h * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, A(0.5f - RoundedDist(x + 0.5f, y + 0.5f, w, h, r)));
            return Finish(tex, px, new Vector4(h / 2, 0, h / 2, 0));
        }

        static Sprite MakeCircle(int size)
        {
            var tex = NewTex(size, size, "ui_circle");
            var px = new Color32[size * size];
            float c = size * 0.5f, r = c - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                px[y * size + x] = new Color32(255, 255, 255, A(0.5f - d));
            }
            return Finish(tex, px, Vector4.zero);
        }

        static Sprite MakeRing(int size, float thickness)
        {
            var tex = NewTex(size, size, "ui_ring");
            var px = new Color32[size * size];
            float c = size * 0.5f, r = c - thickness * 0.5f - 1.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - thickness * 0.5f;
                px[y * size + x] = new Color32(255, 255, 255, A(0.5f - d));
            }
            return Finish(tex, px, Vector4.zero);
        }

        static Sprite MakeSoftShadow(int size, int blur)
        {
            var tex = NewTex(size, size, "ui_soft_shadow");
            var px = new Color32[size * size];
            const float radius = 12f;
            float box = size - blur, off = blur * 0.5f;   // solid core box, fading out across `blur` px around its edge
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDist(x + 0.5f - off, y + 0.5f - off, box, box, radius);
                float t = Mathf.Clamp01((d + blur * 0.5f) / blur);
                float a = 1f - t * t * (3f - 2f * t);
                px[y * size + x] = new Color32(0, 0, 0, A(a));
            }
            int border = blur + (int)radius;
            return Finish(tex, px, new Vector4(border, border, border, border));
        }

        static Sprite MakeGlow(int size)
        {
            var tex = NewTex(size, size, "ui_glow");
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / c);
                px[y * size + x] = new Color32(255, 255, 255, A(t * t));
            }
            return Finish(tex, px, Vector4.zero);
        }

        static Sprite MakeSolid(int w, int h, int radius)
        {
            var tex = NewTex(w, h, "ui_solid");
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = radius > 0 ? 0.5f - RoundedDist(x + 0.5f, y + 0.5f, w, h, radius) : 1f;
                px[y * w + x] = new Color32(255, 255, 255, A(a));
            }
            return Finish(tex, px, Vector4.zero);
        }

        static Sprite MakeGradientV(int w, int h)
        {
            var tex = NewTex(w, h, "ui_gradient_v");
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, A(y / (float)(h - 1)));
            return Finish(tex, px, Vector4.zero);
        }

        /// <summary>Pill with a darker bottom lip and outline; multiplied by a tint it reads as a raised candy button.</summary>
        static Sprite MakeButtonPill(int w, int h)
        {
            var tex = NewTex(w, h, "ui_fallback_button");
            var px = new Color32[w * h];
            float r = h * 0.5f;
            const float lip = 7f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = RoundedDist(fx, fy, w, h, r);
                float alpha = Mathf.Clamp01(0.5f - d);
                // face = the pill shifted up by the lip height and inset by the outline
                float face = RoundedDist(fx, fy - lip * 0.5f, w - 6f, h - lip - 4f, (h - lip - 4f) * 0.5f);
                float shade = face < 0f ? 1f : 0.7f;
                if (face < 0f && fy > h * 0.62f) shade = 1f; // upper face
                else if (face < 0f) shade = 0.93f;
                if (d > -2.5f) shade = 0.55f;                // outline
                byte v = A(shade);
                px[y * w + x] = new Color32(v, v, v, A(alpha));
            }
            int bx = Mathf.FloorToInt(r);
            return Finish(tex, px, new Vector4(bx, h / 2 - 1, bx, h / 2 - 1));
        }

        static Sprite MakeBarFill(int w, int h)
        {
            var tex = NewTex(w, h, "ui_fallback_bar");
            var px = new Color32[w * h];
            float r = h * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = RoundedDist(fx, fy, w, h, r);
                float t = fy / h; // 0 bottom .. 1 top
                float shade = t > 0.62f && t < 0.82f ? 1f : (t < 0.3f ? 0.78f : 0.9f);
                byte v = A(shade);
                px[y * w + x] = new Color32(v, v, v, A(0.5f - d));
            }
            int bx = Mathf.FloorToInt(r);
            return Finish(tex, px, new Vector4(bx, h / 2 - 1, bx, h / 2 - 1));
        }
    }
}
