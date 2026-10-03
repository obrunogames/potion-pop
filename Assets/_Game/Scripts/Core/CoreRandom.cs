using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Random source for the meta systems (spin, chest, cards). System.Random instead of UnityEngine.Random so it
    /// works in EditMode tests and can be seeded for reproducible results without touching gameplay randomness.
    /// </summary>
    public static class CoreRandom
    {
        static System.Random _rng;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _rng = null;

        static System.Random Rng => _rng ??= new System.Random(Guid.NewGuid().GetHashCode());

        /// <summary>Re-seeds the generator (tests); a null seed goes back to a time-based seed.</summary>
        public static void Seed(int? seed) => _rng = seed.HasValue ? new System.Random(seed.Value) : null;

        /// <summary>[0, 1)</summary>
        public static float Value() => (float)Rng.NextDouble();

        /// <summary>[minInclusive, maxExclusive)</summary>
        public static int Range(int minInclusive, int maxExclusive) =>
            maxExclusive <= minInclusive ? minInclusive : Rng.Next(minInclusive, maxExclusive);

        /// <summary>
        /// Stable 32-bit hash mix (splitmix-style) used for date-seeded picks. Unlike System.Random its output is
        /// guaranteed identical on every runtime/platform, so the daily quests match across devices.
        /// </summary>
        public static uint Hash(uint x)
        {
            unchecked   // wrap-around is intended (stays correct if the project ever enables overflow checks)
            {
                x += 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
