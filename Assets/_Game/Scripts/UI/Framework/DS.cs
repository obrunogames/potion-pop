// ============================================================================================================
// Design tokens of the "Candy Boutique" design system (Docs/DesignSystem.md): colors, typography, spacing,
// motion, button geometry. DS.Apply styles any TMP text with a cached material preset per TextStyle.
// ============================================================================================================
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    public enum TextStyle { Display, H1, H2, H3, Body, BodyLight, Small, Badge }
    public enum ButtonColor { Green, Blue, Orange, Purple, Red, Yellow, Pink, Gray }
    public enum ButtonSize { Small, Medium, Large, Wide }

    public static class DS
    {
        // ---------------------------------------------------------------------------------------- colors

        public static class Colors
        {
            public static readonly Color Brand = Hex("7B4DFF");
            public static readonly Color BrandDark = Hex("5A2FD6");
            public static readonly Color BrandLight = Hex("B79BFF");
            public static readonly Color Ink = Hex("3B1F5C");
            public static readonly Color InkSoft = Hex("7A6A92");
            public static readonly Color Cream = Hex("FFF6E5");
            public static readonly Color CreamDark = Hex("F1DDBF");
            public static readonly Color White = Color.white;
            public static readonly Color Primary = Hex("58CC02");
            public static readonly Color Secondary = Hex("1CB0F6");
            public static readonly Color Accent = Hex("FFC800");
            public static readonly Color Orange = Hex("FF9600");
            public static readonly Color Pink = Hex("FF4B9A");
            public static readonly Color Danger = Hex("FF4B4B");
            public static readonly Color Mint = Hex("2ED6A1");
            public static readonly Color Sky = Hex("6FD3FF");
            public static readonly Color Lavender = Hex("B69CFF");
            public static readonly Color Overlay = new Color(0.102f, 0.043f, 0.2f, 0.7f);
            public static readonly Color Frost = Hex("9EE7FF");

            // ---- additions
            /// <summary>Neutral gray used by disabled controls and the "off" toggle track.</summary>
            public static readonly Color Gray = Hex("B9B0C7");
            /// <summary>Drop shadow color for Surface.Popup (black 35%).</summary>
            public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.35f);
            /// <summary>Glass surface fill (white 18%) and border (white 35%).</summary>
            public static readonly Color Glass = new Color(1f, 1f, 1f, 0.18f);
            public static readonly Color GlassBorder = new Color(1f, 1f, 1f, 0.35f);
            /// <summary>Area accents (Docs/DesignSystem.md §1).</summary>
            public static readonly Color AreaGrocery = Hex("2ED6A1");
            public static readonly Color AreaSweets = Hex("FF7EB6");
            public static readonly Color AreaToys = Hex("4FB3FF");
            public static readonly Color AreaBeauty = Hex("A98BFF");
            public static readonly Color AreaFresh = Hex("FFA94D");
            /// <summary>Candy palette used by confetti and celebratory bursts.</summary>
            public static readonly Color[] Candy =
            {
                Hex("FF4B9A"), Hex("FFC800"), Hex("1CB0F6"), Hex("58CC02"), Hex("7B4DFF"), Hex("FF9600"), Hex("2ED6A1"),
            };
            /// <summary>Warm palette (gold/white) for star and coin bursts.</summary>
            public static readonly Color[] Gold = { Hex("FFC800"), Hex("FFE066"), Color.white, Hex("FF9600") };
        }

        // ---------------------------------------------------------------------------------------- spacing

        public static class Space
        {
            public const float XXS = 4, XS = 8, S = 16, M = 24, L = 40, XL = 64, XXL = 96;
            public const float ScreenMargin = 32, TopBarHeight = 130, BottomNavHeight = 210, BoosterBarHeight = 230;
            public const float PopupWidth = 920;

            // ---- additions
            public const float PopupPadding = 56;
            /// <summary>How far the popup title ribbon reaches down into the panel from its top edge.</summary>
            public const float RibbonOverlap = 60;
            public const float MinTapTarget = 110;
            public const float IconInline = 64, IconButton = 96, IconSide = 140;
            public const float ReferenceWidth = 1080, ReferenceHeight = 1920;
        }

        // ---------------------------------------------------------------------------------------- motion

        public static class Motion
        {
            public const float Fast = 0.12f, Base = 0.25f, Slow = 0.45f, Stagger = 0.05f;

            // ---- additions
            public const float PopInFrom = 0.6f, PopOutTo = 0.7f, PopOvershoot = 1.4f, PopOutDuration = 0.2f;
            public const float PressScale = 0.92f;
            public const float BobAmplitude = 6f, BobPeriod = 2.2f;
            public const float PulseScale = 1.06f, PulsePeriod = 0.9f;
            public const float FlyMin = 0.55f, FlyMax = 0.8f, FlyStagger = 0.04f;
            public const float ScreenSlide = 40f;
            public const float ToastDuration = 2.2f;
        }

        // ---------------------------------------------------------------------------------------- typography

        struct TextSpec
        {
            public float size;
            public Color color;
            public float outline;          // TMP outline width (0 = none), color Ink
            public float shadowPx;         // underlay offset in pixels at the style size (0 = none), color Ink
            public bool noWrap;

            public TextSpec(float size, Color color, float outline, float shadowPx, bool noWrap)
            {
                this.size = size; this.color = color; this.outline = outline; this.shadowPx = shadowPx; this.noWrap = noWrap;
            }
        }

        static TextSpec Spec(TextStyle s)
        {
            switch (s)
            {
                case TextStyle.Display: return new TextSpec(120, Colors.White, 0.28f, 4, true);
                case TextStyle.H1: return new TextSpec(84, Colors.White, 0.25f, 3, true);
                case TextStyle.H2: return new TextSpec(64, Colors.White, 0.22f, 3, true);
                case TextStyle.H3: return new TextSpec(50, Colors.White, 0.20f, 2, true);
                case TextStyle.Body: return new TextSpec(42, Colors.Ink, 0f, 0, false);
                case TextStyle.BodyLight: return new TextSpec(42, Colors.White, 0.18f, 0, false);
                case TextStyle.Small: return new TextSpec(34, Colors.InkSoft, 0f, 0, false);
                case TextStyle.Badge: return new TextSpec(30, Colors.White, 0.25f, 0, true);
                default: return new TextSpec(42, Colors.Ink, 0f, 0, false);
            }
        }

        const int StyleCount = 8;
        /// <summary>Minimum auto-size as a fraction of the style size.</summary>
        public const float AutoSizeMin = 0.6f;

        static TMP_FontAsset _font;
        static bool _fontSearched;
        static TMP_FontAsset _presetFont;
        static readonly Material[] _presets = new Material[StyleCount];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _font = null;
            _fontSearched = false;
            _presetFont = null;
            for (int i = 0; i < _presets.Length; i++) _presets[i] = null;
        }

        /// <summary>Lilita One TMP font asset (Resources/Fonts/LilitaOne SDF). Null-safe: falls back to TMP default.</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                if (!_fontSearched)
                {
                    _fontSearched = true;
                    _font = Resources.Load<TMP_FontAsset>("Fonts/LilitaOne SDF");
                    if (_font == null)
                    {
                        // TMP_Settings throws when the TMP Essentials/settings asset is missing.
                        try { _font = TMP_Settings.defaultFontAsset; }
                        catch (System.Exception) { _font = null; }
                        Debug.LogWarning("[DS] Resources/Fonts/LilitaOne SDF not found, using the TMP default font.");
                    }
                }
                return _font;
            }
        }

        /// <summary>Font size of a style (Display 120, H1 84, H2 64, H3 50, Body 42, BodyLight 42, Small 34, Badge 30).</summary>
        public static float Size(TextStyle s) => Spec(s).size;

        /// <summary>Default face color of a style.</summary>
        public static Color TextColor(TextStyle s) => Spec(s).color;

        /// <summary>Applies font, size, color, outline and underlay of a style to a TMP text.</summary>
        public static void Apply(TMP_Text text, TextStyle style) => Apply(text, style, Spec(style).size);

        /// <summary>Same as Apply(text, style) with a custom (maximum) font size. Auto-size goes down to 60%.</summary>
        public static void Apply(TMP_Text text, TextStyle style, float size)
        {
            if (text == null) return;
            var spec = Spec(style);
            var font = Font;
            if (font != null)
            {
                if (text.font != font) text.font = font;      // resets the material, so the preset goes after
                var preset = StyleMaterial(style);
                if (preset != null) text.fontSharedMaterial = preset;
            }
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Max(8f, size * AutoSizeMin);
            text.fontSize = size;
            text.color = spec.color;
            text.textWrappingMode = spec.noWrap ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
        }

        /// <summary>
        /// Shared material preset of a style, derived from the font material (outline width/color, face dilate,
        /// underlay color/offset/softness, OUTLINE_ON / UNDERLAY_ON keywords). Created once per style and cached.
        /// </summary>
        public static Material StyleMaterial(TextStyle style)
        {
            var font = Font;
            if (font == null || font.material == null) return null;
            if (_presetFont != font)
            {
                for (int i = 0; i < _presets.Length; i++)
                {
                    if (_presets[i] == null) continue;
                    // Edit Mode (tests, editor tools) cannot use Destroy.
                    if (Application.isPlaying) Object.Destroy(_presets[i]);
                    else Object.DestroyImmediate(_presets[i]);
                    _presets[i] = null;
                }
                _presetFont = font;
            }
            int idx = (int)style;
            if (idx < 0 || idx >= StyleCount) idx = (int)TextStyle.Body;
            if (_presets[idx] != null) return _presets[idx];

            var spec = Spec(style);
            var m = new Material(font.material) { name = font.name + " (" + style + ")", hideFlags = HideFlags.DontSave };
            if (spec.outline > 0f)
            {
                m.EnableKeyword(ShaderUtilities.Keyword_Outline);
                SetFloat(m, ShaderUtilities.ID_OutlineWidth, spec.outline);
                SetColor(m, ShaderUtilities.ID_OutlineColor, Colors.Ink);
                // Push the outline outwards so the white face keeps the weight of the glyph.
                SetFloat(m, ShaderUtilities.ID_FaceDilate, spec.outline * 0.5f);
            }
            else
            {
                m.DisableKeyword(ShaderUtilities.Keyword_Outline);
                SetFloat(m, ShaderUtilities.ID_OutlineWidth, 0f);
                SetFloat(m, ShaderUtilities.ID_FaceDilate, 0f);
            }

            if (spec.shadowPx > 0f)
            {
                m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                SetColor(m, ShaderUtilities.ID_UnderlayColor, Colors.Ink);
                SetFloat(m, ShaderUtilities.ID_UnderlayOffsetX, 0f);
                SetFloat(m, ShaderUtilities.ID_UnderlayOffsetY, -PixelsToUnderlay(font, spec.shadowPx, spec.size));
                SetFloat(m, ShaderUtilities.ID_UnderlaySoftness, 0.05f);
                SetFloat(m, ShaderUtilities.ID_UnderlayDilate, Mathf.Min(1f, spec.outline));
            }
            else
            {
                m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }

            try { ShaderUtilities.UpdateShaderRatios(m); }
            catch (System.Exception) { /* older TMP builds: ratios are refreshed by TMP itself */ }
            _presets[idx] = m;
            return m;
        }

        /// <summary>
        /// Underlay offsets are in SDF units: 1 unit = atlas padding texels, i.e. padding * size / samplingSize pixels
        /// at a given font size. Converts a pixel offset at the style size to that unit (clamped to the shader range).
        /// </summary>
        static float PixelsToUnderlay(TMP_FontAsset font, float px, float size)
        {
            float sampling = font.faceInfo.pointSize;
            float padding = font.atlasPadding;
            float pxPerUnit = sampling > 0f && padding > 0f ? padding * size / sampling : size * 0.1f;
            return Mathf.Clamp(px / Mathf.Max(0.01f, pxPerUnit), -1f, 1f);
        }

        static void SetFloat(Material m, int id, float v) { if (m.HasProperty(id)) m.SetFloat(id, v); }
        static void SetColor(Material m, int id, Color v) { if (m.HasProperty(id)) m.SetColor(id, v); }

        // ---------------------------------------------------------------------------------------- misc tokens

        public static Color AreaAccent(string areaId)
        {
            switch (areaId)
            {
                case "grocery": return Colors.AreaGrocery;
                case "sweets": return Colors.AreaSweets;
                case "toys": return Colors.AreaToys;
                case "beauty": return Colors.AreaBeauty;
                case "fresh": return Colors.AreaFresh;
                default: return Colors.Brand;
            }
        }

        /// <summary>Flat color of a button color (used to tint the procedural fallback when btn_* art is missing).</summary>
        public static Color ButtonTint(ButtonColor c)
        {
            switch (c)
            {
                case ButtonColor.Green: return Colors.Primary;
                case ButtonColor.Blue: return Colors.Secondary;
                case ButtonColor.Orange: return Colors.Orange;
                case ButtonColor.Purple: return Colors.Brand;
                case ButtonColor.Red: return Colors.Danger;
                case ButtonColor.Yellow: return Colors.Accent;
                case ButtonColor.Pink: return Colors.Pink;
                case ButtonColor.Gray: return Colors.Gray;
                default: return Color.white;
            }
        }

        /// <summary>Sprite name of a button color ("btn_green", ...).</summary>
        public static string ButtonSprite(ButtonColor c)
        {
            switch (c)
            {
                case ButtonColor.Green: return "btn_green";
                case ButtonColor.Blue: return "btn_blue";
                case ButtonColor.Orange: return "btn_orange";
                case ButtonColor.Purple: return "btn_purple";
                case ButtonColor.Red: return "btn_red";
                case ButtonColor.Yellow: return "btn_yellow";
                case ButtonColor.Pink: return "btn_pink";
                default: return "btn_gray";
            }
        }

        /// <summary>Button dimensions: Small 260x110, Medium 420x140, Large 600x170, Wide 820x170.</summary>
        public static Vector2 ButtonDimensions(ButtonSize s)
        {
            switch (s)
            {
                case ButtonSize.Small: return new Vector2(260, 110);
                case ButtonSize.Medium: return new Vector2(420, 140);
                case ButtonSize.Large: return new Vector2(600, 170);
                default: return new Vector2(820, 170);
            }
        }

        /// <summary>Label style of a button size (H2 for Large/Wide, H3 otherwise).</summary>
        public static TextStyle ButtonTextStyle(ButtonSize s) => s == ButtonSize.Large || s == ButtonSize.Wide ? TextStyle.H2 : TextStyle.H3;

        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        /// <summary>Same color with another alpha.</summary>
        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
