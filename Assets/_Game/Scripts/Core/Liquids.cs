using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// The potion palette (Docs/DesignSystem.md §7). Level data stores color ids (0..Count-1); the first eight are the
    /// most distinguishable and are the only ones small levels use. Colors are vivid candy tones that read on every
    /// world background; Dark/Light are the shading tones used by the liquid mesh (bottom shade, surface highlight).
    /// </summary>
    public static class Liquids
    {
        public const int Count = 12;

        static readonly string[] Ids =
        {
            "red", "blue", "yellow", "green", "purple", "orange", "pink", "sky", "lime", "brown", "white", "teal",
        };

        static readonly Color[] Base =
        {
            Hex(0xFF3B5C),   // 0 red
            Hex(0x2F6BFF),   // 1 blue
            Hex(0xFFD12E),   // 2 yellow
            Hex(0x22C55E),   // 3 green
            Hex(0x9B4DFF),   // 4 purple
            Hex(0xFF8A1E),   // 5 orange
            Hex(0xFF6FD0),   // 6 pink
            Hex(0x38D4FF),   // 7 sky
            Hex(0xB8F03C),   // 8 lime
            Hex(0x9A5A32),   // 9 brown
            Hex(0xF4F0FF),   // 10 white (pearl)
            Hex(0x14B8A6),   // 11 teal
        };

        /// <summary>Hidden ("?") units: a misty lavender-gray.</summary>
        public static readonly Color Mystery = Hex(0x8F88A8);

        /// <summary>Main color of a liquid id (magenta for unknown ids).</summary>
        public static Color Color(int id) => id >= 0 && id < Base.Length ? Base[id] : UnityEngine.Color.magenta;

        /// <summary>Shade for the lower part of a layer.</summary>
        public static Color Dark(int id) => Multiply(Color(id), 0.78f);

        /// <summary>Highlight for the liquid surface.</summary>
        public static Color Light(int id) => UnityEngine.Color.Lerp(Color(id), UnityEngine.Color.white, 0.45f);

        /// <summary>Lower-case id ("red") used in loc keys ("liquid.red") and logs.</summary>
        public static string Id(int id) => id >= 0 && id < Ids.Length ? Ids[id] : "unknown";

        public static string NameKey(int id) => "liquid." + Id(id);

        static Color Multiply(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
