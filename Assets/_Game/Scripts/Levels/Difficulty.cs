// Difficulty curve (GDD §3): colors, empty bottles, hidden layers and stone bottles per level, with deterministic
// per-level variety so consecutive levels don't feel the same.
using System;

namespace PotionPop.Levels
{
    public struct LevelParams
    {
        public int level;
        public bool hard;
        /// <summary>Distinct colors (= bottles that start with liquid).</summary>
        public int colors;
        /// <summary>Empty bottles at the start.</summary>
        public int empties;
        public int capacity;
        /// <summary>Longest same-color run allowed in a starting bottle (lower = more mixed = harder).</summary>
        public int maxRun;
        /// <summary>Bottles that start with hidden ("?") units under their top.</summary>
        public int hiddenBottles;
        /// <summary>Hidden units in each of those bottles (≤ capacity − 1).</summary>
        public int hiddenDepth;
        /// <summary>Stone-wrapped bottles.</summary>
        public int locks;
        /// <summary>Completions that free the first stone (the next one needs one more).</summary>
        public int lockCount;
        /// <summary>Solvable boards generated; the one with the longest solution is kept.</summary>
        public int candidates;

        public int Bottles => colors + empties;

        public override string ToString() =>
            $"L{level}{(hard ? " HARD" : "")}: colors={colors} empties={empties} cap={capacity} run≤{maxRun} " +
            $"hidden={hiddenBottles}x{hiddenDepth} locks={locks}({lockCount}) candidates={candidates}";
    }

    public static class Difficulty
    {
        public const int Capacity = 4;
        /// <summary>Colors in the liquid palette (PotionPop.Liquids).</summary>
        public const int PaletteSize = 12;
        public const int MaxColors = 12;
        public const int FirstHiddenLevel = 15;
        public const int FirstLockLevel = 25;

        /// <summary>Hard levels (skull): n % 10 == 0 from 10 on, n % 10 == 5 from 45 on.</summary>
        public static bool IsHard(int level) => (level >= 10 && level % 10 == 0) || (level >= 45 && level % 10 == 5);

        /// <summary>Deterministic 0..1 noise per level and channel.</summary>
        static double Noise(int level, int channel)
        {
            uint x = (uint)(level * 7919 + channel * 104729 + 17);
            unchecked
            {
                x += 0x9E3779B9u;
                x ^= x >> 16; x *= 0x85EBCA6Bu;
                x ^= x >> 13; x *= 0xC2B2AE35u;
                x ^= x >> 16;
            }
            return (x & 0xFFFFFF) / (double)0x1000000;
        }

        static int Lerp(int a, int b, int level, int from, int to)
        {
            if (to <= from) return b;
            double t = Math.Max(0, Math.Min(1, (level - from) / (double)(to - from)));
            return (int)Math.Round(a + (b - a) * t);
        }

        /// <summary>Base color count of a level (before the hard-level bonus).</summary>
        static int BaseColors(int level)
        {
            if (level <= 1) return 2;
            if (level == 2) return 3;
            if (level <= 5) return 4;
            if (level <= 9) return 5;
            if (level <= 14) return 6;
            if (level <= 19) return 7;
            if (level <= 29) return Lerp(7, 8, level, 20, 29);
            if (level <= 39) return Lerp(8, 9, level, 30, 39);
            if (level <= 59) return Lerp(9, 10, level, 40, 59);
            if (level <= 99) return Lerp(10, 11, level, 60, 99);
            // plateau with noise: 10..12
            double n = Noise(level, 1);
            return n < 0.3 ? 10 : n < 0.75 ? 11 : 12;
        }

        public static LevelParams For(int level)
        {
            level = Math.Max(1, level);
            bool hard = IsHard(level);
            var p = new LevelParams
            {
                level = level,
                hard = hard,
                capacity = Capacity,
                colors = BaseColors(level),
                empties = level <= 1 ? 1 : 2,
                maxRun = level < 10 ? 3 : 2,
                candidates = level < 10 ? 1 : level < 30 ? 3 : 4,
            };
            if (hard)
            {
                p.colors = Math.Min(MaxColors, p.colors + 1);
                p.maxRun = 1;
                p.candidates += 1;
            }

            // Mystery levels: always the first one (15), then a growing share of levels.
            if (level >= FirstHiddenLevel)
            {
                double chance = level < 30 ? 0.4 : level < 60 ? 0.5 : 0.6;
                bool mystery = level == FirstHiddenLevel || hard && level >= 20 || Noise(level, 2) < chance;
                if (mystery)
                {
                    double share = level < 30 ? 0.3 : level < 60 ? 0.45 : 0.6;
                    if (hard) share += 0.15;
                    p.hiddenBottles = Math.Max(2, (int)Math.Round(p.colors * Math.Min(1.0, share)));
                    p.hiddenDepth = level < 30 ? 1 : level < 60 ? 2 : 3;
                    if (level == FirstHiddenLevel) { p.hiddenBottles = 2; p.hiddenDepth = 1; }
                }
            }

            // Stone levels: always the first one (25), then some levels.
            if (level >= FirstLockLevel)
            {
                double chance = level < 50 ? 0.3 : 0.4;
                bool stone = level == FirstLockLevel || Noise(level, 3) < chance;
                if (stone)
                {
                    p.locks = level >= 60 && Noise(level, 4) < 0.35 ? 2 : 1;
                    p.lockCount = Math.Min(5, 2 + level / 40);
                    if (level == FirstLockLevel) { p.locks = 1; p.lockCount = 2; }
                }
            }
            return p;
        }
    }
}
