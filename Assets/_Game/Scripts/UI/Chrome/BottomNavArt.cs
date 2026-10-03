// ============================================================================================================
// Procedural art of the bottom navigation (BottomNav), generated once as 9-sliced sprites at Px pixels per unit:
//  · Bar  — flat violet slab: dark outline, gold border with an orange lip, dark line, lavender highlight, then a
//           violet gradient body (stretchable middle) and a solid bleed under the screen bottom.
//  · Tile — the selected tab: lighter panel raised `Raise` units above the bar inside a rounded gold frame that flows
//           out of the bar's gold border through concave fillets; below the border its sides turn into dark outlines.
// Colors and proportions were sampled from the reference tab bar (gold top border, dark dividers, raised tab).
// ============================================================================================================
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Generated sprites and geometry (canvas units) of the bottom navigation bar.</summary>
    public static class BottomNavArt
    {
        /// <summary>Generated pixels per canvas unit.</summary>
        public const float Px = 2f;
        /// <summary>Soft shadow above the bar top and around the raised frame.</summary>
        public const float ShadowH = 8f;
        /// <summary>Dark outline + gold + orange lip at the top of the bar.</summary>
        public const float GoldH = 14.8f;
        /// <summary>How far the selected tab rises above the bar top.</summary>
        public const float Raise = 42f;
        /// <summary>Tile sprite margin outside the tab sides (fillets + shadow).</summary>
        public const float TileMargin = 20f;
        /// <summary>Solid part below the screen bottom (covers the slide-in overshoot).</summary>
        public const float Bleed = 60f;

        const float TrimH = 32f;                 // gold border + dark line + lavender highlight
        const float FrameRadius = 44f, Fillet = 16f;
        const float TileSide = 50f;              // tile 9-slice side border (corner + bevel)
        const float TileBelowBar = 50f;          // tile 9-slice top border ends this far below the bar top
        const float MidH = 32f, MidW = 4f;       // stretchable middles
        const float CoreW = 4f;                  // dark outline of the tile sides below the gold border
        const float GoldLip = 12.6f;             // gold ramp depth where the frame bevel starts
        const float FadeTop = 20f, FadeSpan = 268f;   // tile body gradient: f = 0 at this height above the bar top

        static readonly Color ShadowColor = Hex(0x1A0A30);
        static readonly Color BodyTop = Hex(0x7442F7), BodyBottom = Hex(0x4621BD);
        static readonly Color TileTop = Hex(0x9A6BFF), TileBottom = Hex(0x4826BC);
        static readonly Color CoreTop = Hex(0x18008B), CoreBottom = Hex(0x14007F);
        static readonly Color SideBandTop = Hex(0x4418C4), SideBandBottom = Hex(0x3A18A9);
        static readonly Color SideHiTop = Hex(0x9C70FF), SideHiBottom = Hex(0x6040D0);
        static readonly Color FrameHi = Hex(0xA97CFF);

        // Bar trim by depth below the bar top (units): outline, gold, lip, dark line, lavender highlight, body.
        static readonly float[] TrimD = { 0f, 0.9f, 2.6f, 4.6f, 6.4f, 8.2f, 10.1f, 11.9f, 13.6f, 14.8f, 15.8f, 17.4f, 19.2f, 21f, 22.9f, 24.7f, 26.5f, 28.4f, 30.2f, 32f };
        static readonly Color[] TrimC =
        {
            Hex(0x4A2A24), Hex(0x8E622E), Hex(0xE6A92E), Hex(0xF0AD14), Hex(0xF8B70B), Hex(0xFFCF1E), Hex(0xF8B61D),
            Hex(0xC16807), Hex(0xA4522A), Hex(0x6A2A3E), Hex(0x4A1450), Hex(0x390A57), Hex(0x7545C3), Hex(0x996CF8),
            Hex(0xAD83FC), Hex(0xBD94FF), Hex(0xCAA1FF), Hex(0x9266EB), Hex(0x7342E2), Hex(0x7442F7),
        };
        // Frame bevel band (gold lip → dark violet → highlight) across its width.
        static readonly float[] BandT = { 0f, 0.3f, 0.65f, 1f };
        static readonly Color[] BandC = { Hex(0xA85E40), Hex(0x8E4A80), Hex(0x6E36AE), Hex(0x6A3CCB) };

        static Sprite _bar, _tile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _bar = null;
            _tile = null;
        }

        /// <summary>Bar sprite: covers ShadowH above the bar top down to Bleed below the screen bottom.</summary>
        public static Sprite Bar => _bar != null ? _bar : (_bar = MakeBar());

        /// <summary>Selected tab sprite: tab width + 2 TileMargin wide, from Raise + ShadowH above the bar top down to
        /// Bleed below the screen bottom.</summary>
        public static Sprite Tile => _tile != null ? _tile : (_tile = MakeTile());

        // ---------------------------------------------------------------------------------------- bar

        static Sprite MakeBar()
        {
            const int w = 4;
            int top = Mathf.RoundToInt((ShadowH + TrimH) * Px), mid = Mathf.RoundToInt(MidH * Px), bot = Mathf.RoundToInt(Bleed * Px);
            int h = top + mid + bot;
            var px = new Color32[w * h];
            for (int row = 0; row < h; row++)
            {
                Color c;
                if (row >= h - top) c = BarTrim((h - row - 0.5f) / Px - ShadowH);
                else if (row >= bot) c = BarBody(((h - top) - (row + 0.5f)) / mid);
                else c = BodyBottom;
                Color32 c32 = c;
                for (int x = 0; x < w; x++) px[row * w + x] = c32;
            }
            return Finish("ui_nav_bar", w, h, px, new Vector4(0f, bot, 0f, top));
        }

        /// <summary>Bar color at `d` units below the bar top (negative = shadow above it).</summary>
        static Color BarTrim(float d)
        {
            if (d < 0f) return WithA(ShadowColor, ShadowAlpha(-d));
            return Ramp(TrimD, TrimC, d);
        }

        /// <summary>Body gradient, t = 0 under the trim → 1 at the screen bottom (eases out like the reference).</summary>
        static Color BarBody(float t) => Color.Lerp(BodyTop, BodyBottom, 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 1.6f));

        static float ShadowAlpha(float dist) => dist >= ShadowH ? 0f : 0.3f * Sq(1f - dist / ShadowH);

        // ---------------------------------------------------------------------------------------- selected tile

        static Sprite MakeTile()
        {
            float widthU = 2f * (TileMargin + TileSide) + MidW;
            float topU = ShadowH + Raise + TileBelowBar;
            int w = Mathf.RoundToInt(widthU * Px);
            int top = Mathf.RoundToInt(topU * Px), mid = Mathf.RoundToInt(MidH * Px), bot = Mathf.RoundToInt(Bleed * Px);
            int h = top + mid + bot;
            int side = Mathf.RoundToInt((TileMargin + TileSide) * Px);
            float tileW = widthU - 2f * TileMargin;
            float fMidTop = Mathf.Clamp01((FadeTop + TileBelowBar) / FadeSpan);
            var px = new Color32[w * h];
            for (int row = 0; row < h; row++)
            {
                float fromTop = (h - row - 0.5f) / Px;
                float yb = ShadowH + Raise - fromTop;      // height above the bar top
                float f;
                if (row >= h - top) f = Mathf.Clamp01((FadeTop - yb) / FadeSpan);
                else if (row >= bot) f = Mathf.Lerp(fMidTop, 1f, ((h - top) - (row + 0.5f)) / mid);
                else f = 1f;
                for (int col = 0; col < w; col++)
                {
                    float x = (col + 0.5f) / Px - TileMargin;
                    float xm = Mathf.Min(x, tileW - x);    // distance from the nearer tab side (mirrored)
                    px[row * w + col] = TilePixel(xm, yb, f);
                }
            }
            return Finish("ui_nav_tile", w, h, px, new Vector4(side, bot, side, top));
        }

        /// <summary>Tile color at `xm` units inside the nearer tab side (negative = outside) and `yb` above the bar top.</summary>
        static Color TilePixel(float xm, float yb, float f)
        {
            float a = Quadrant(xm, yb, 0f, Raise, FrameRadius);   // the raised tab
            float u = RoundUnion(a, yb, Fillet);                  // tab ∪ bar, with concave fillets
            float dU = -u;

            // Outside the tab: the bar trim continues, following the fillet (transparent once the trim ends).
            Color outside = WithA(Ramp(TrimD, TrimC, Mathf.Max(dU, 0f)), Mathf.Clamp01((TrimH - dU) * Px + 0.5f));
            Color inside = TileInterior(xm, yb, dU, a, f);
            Color c = LerpPremul(outside, inside, Mathf.Clamp01(0.5f - a * Px));

            float cover = Mathf.Clamp01(0.5f - u * Px);
            float ia = c.a * cover;
            // Shadow around the frame, on top of the bar's own shadow strip (composited so they never double up).
            float sU = ShadowAlpha(Mathf.Max(u, 0f)), sB = yb > 0f ? ShadowAlpha(yb) : 1f;
            float st = sB >= 1f ? 0f : Mathf.Clamp01(1f - (1f - sU) / (1f - sB));
            float sa = st * (1f - ia);
            float oa = ia + sa;
            if (oa <= 0.0001f) return new Color(0f, 0f, 0f, 0f);
            var rgb = ((Vector4)c * ia + (Vector4)ShadowColor * sa) / oa;
            return new Color(rgb.x, rgb.y, rgb.z, oa);
        }

        /// <summary>Inside the tab: gold frame (raised part) or dark outline (sides below the bar border), dark-violet
        /// band, lavender highlight and the lighter body. Bevel insets are top-heavy like the reference.</summary>
        static Color TileInterior(float xm, float yb, float dU, float a, float f)
        {
            float sideBlend = Smooth01((-yb - 4f) / 36f);                     // raised bevel → thinner side bevel
            float lower = Mathf.Clamp01((-GoldH - yb) * Px + 0.5f);          // ring: gold above the border bottom
            float d1 = Inset(xm, yb, Mathf.Lerp(12.5f, 11f, sideBlend), 16.5f);
            float d2 = Inset(xm, yb, Mathf.Lerp(18f, 14.5f, sideBlend), 28f);
            float d3 = Inset(xm, yb, Mathf.Lerp(23.5f, 18f, sideBlend), 41f);

            Color body = Color.Lerp(TileTop, TileBottom, f);
            Color core = Color.Lerp(CoreTop, CoreBottom, f);
            Color sideBand = Color.Lerp(SideBandTop, SideBandBottom, f);
            Color gold = Ramp(TrimD, TrimC, Mathf.Min(dU, GoldLip));
            Color ringLow = Color.Lerp(core, sideBand, Mathf.Clamp01((-a - CoreW) * Px + 0.5f));
            Color ring = Color.Lerp(gold, ringLow, lower);

            float t12 = Mathf.Clamp01(-d1 / Mathf.Max(0.001f, d2 - d1));
            Color band = Color.Lerp(Ramp(BandT, BandC, t12), sideBand, sideBlend);
            Color bandEnd = Color.Lerp(BandC[BandC.Length - 1], sideBand, sideBlend);
            Color peak = Color.Lerp(FrameHi, Color.Lerp(SideHiTop, SideHiBottom, f), sideBlend);
            float t23 = Mathf.Clamp01(-d2 / Mathf.Max(0.001f, d3 - d2));
            Color hi = t23 < 0.45f
                ? Color.Lerp(bandEnd, peak, Smooth01(t23 / 0.45f))
                : Color.Lerp(peak, body, Smooth01((t23 - 0.45f) / 0.55f));

            Color c = ring;
            c = Color.Lerp(c, band, Mathf.Clamp01(0.5f - d1 * Px));
            c = Color.Lerp(c, hi, Mathf.Clamp01(0.5f - d2 * Px));
            c = Color.Lerp(c, body, Mathf.Clamp01(0.5f - d3 * Px));
            return c;
        }

        /// <summary>SDF of the tab shape inset by `side` (sides) and `top` (top edge), concentric corners.</summary>
        static float Inset(float xm, float yb, float side, float top) =>
            Quadrant(xm, yb, side, Raise - top, Mathf.Max(FrameRadius - side, 6f));

        /// <summary>SDF of the quadrant {x ≥ side, y ≤ top} with a rounded corner of radius r (negative inside).</summary>
        static float Quadrant(float x, float y, float side, float top, float r)
        {
            float qx = side + r - x, qy = y - (top - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Union of two SDFs with a circular fillet of radius r in their concave corners.</summary>
        static float RoundUnion(float a, float b, float r)
        {
            float ux = Mathf.Max(r - a, 0f), uy = Mathf.Max(r - b, 0f);
            return Mathf.Max(r, Mathf.Min(a, b)) - Mathf.Sqrt(ux * ux + uy * uy);
        }

        // ---------------------------------------------------------------------------------------- helpers

        static Color Ramp(float[] stops, Color[] colors, float v)
        {
            if (v <= stops[0]) return colors[0];
            for (int i = 1; i < stops.Length; i++)
                if (v <= stops[i]) return Color.Lerp(colors[i - 1], colors[i], (v - stops[i - 1]) / (stops[i] - stops[i - 1]));
            return colors[colors.Length - 1];
        }

        static Color LerpPremul(Color a, Color b, float t)
        {
            float al = Mathf.Lerp(a.a, b.a, t);
            if (al <= 0.0001f) return new Color(0f, 0f, 0f, 0f);
            var rgb = ((Vector4)a * a.a * (1f - t) + (Vector4)b * b.a * t) / al;
            return new Color(rgb.x, rgb.y, rgb.z, al);
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float Sq(float v) => v * v;

        static Color WithA(Color c, float a) { c.a = a; return c; }

        static Color Hex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        static Sprite Finish(string name, int w, int h, Color32[] px, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            // 100 * Px pixels per unit: with the canvas' 100 reference PPU one canvas unit spans Px texels.
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * Px, 0, SpriteMeshType.FullRect, border);
            sp.name = name;
            return sp;
        }
    }
}
