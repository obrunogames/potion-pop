using System;
using System.Collections.Generic;

namespace PotionPop.Services
{
    /// <summary>
    /// Deterministic simulated players for the weekly contest and the offline global ranking. Pure functions of
    /// (week id, week fraction) / (day index) so every device shows the same rivals and the tests can pin them down.
    /// </summary>
    public static class LeaderboardSim
    {
        public const int BotCount = 49;
        /// <summary>Weekly bots advance in hourly steps (168 per week), like players finishing sessions.</summary>
        public const int StepsPerWeek = 168;
        /// <summary>Day index (days since 1970-01-01) the simulated global ranking starts growing from (2026-10-01).</summary>
        public const int GlobalEpochDay = 20727;

        public static readonly string[] AvatarIds = { "puppy", "kitten", "bunny", "duck", "panda", "fox", "bear", "frog" };

        /// <summary>(display name, country code) — proper names/gamer tags are not translated; the country is ("lb.country.xx").</summary>
        static readonly string[,] Names =
        {
            { "Ana Clara", "br" }, { "Pedro Henrique", "br" }, { "Juju Doces", "br" }, { "Rafa Gamer", "br" },
            { "Bia Mercado", "br" }, { "Caio Matos", "br" }, { "Lari Sorvete", "br" }, { "Docinho", "br" },
            { "Brigadeiro", "br" }, { "PopStar99", "br" }, { "Sofía R.", "mx" }, { "Diego Torres", "mx" },
            { "ComboKing", "mx" }, { "Pastelito", "mx" }, { "Valentina", "ar" }, { "Mateo López", "ar" },
            { "Camila Ríos", "co" }, { "Santi Gómez", "co" }, { "Lucía Martín", "es" }, { "Hugo Pardo", "es" },
            { "LunaLuz", "es" }, { "Gominola", "es" }, { "Inês Costa", "pt" }, { "Tiago Silva", "pt" },
            { "Martina Rossi", "it" }, { "Luca Bianchi", "it" }, { "TripleTina", "it" }, { "Emma Dubois", "fr" },
            { "Léo Martin", "fr" }, { "Macaron", "fr" }, { "Mia Schneider", "de" }, { "Jonas Weber", "de" },
            { "SuperSorter", "de" }, { "Olivia Smith", "us" }, { "Jake Miller", "us" }, { "CandyQueen", "us" },
            { "Chloe Brown", "gb" }, { "Harry Evans", "gb" }, { "ShelfMaster", "gb" }, { "Yuki Tanaka", "jp" },
            { "Haruto S.", "jp" }, { "MochiMochi", "jp" }, { "Mei Chen", "cn" }, { "Minjun Kim", "kr" },
            { "Seoyeon Park", "kr" }, { "Priya Sharma", "in" }, { "Arjun Patel", "in" }, { "Ayse Kaya", "tr" },
            { "Noah Jansen", "nl" }, { "Sanne de Vries", "nl" }, { "Liam Tremblay", "ca" }, { "DonutDuke", "ca" },
            { "Zoe Wilson", "au" }, { "Kai Nguyen", "au" }, { "BerryBlast", "au" }, { "Andrea Cruz", "ph" },
            { "KittyKat", "ph" }, { "Putri Ayu", "id" }, { "Rizky Pratama", "id" }, { "Fernanda Soto", "cl" },
            { "Joaquín Vera", "cl" }, { "Paola Quispe", "pe" }, { "Mimi Lover", "us" }, { "Gummy Bea", "gb" },
        };

        public static int NamePoolSize => Names.GetLength(0);

        /// <summary>A simulated rival before ranking.</summary>
        public struct Bot
        {
            public string name;
            public string country;     // ISO code, Loc key "lb.country." + country
            public string avatarId;    // "puppy", ...
            public long score;
        }

        // ---------------------------------------------------------------------------------------- weekly

        /// <summary>The 49 weekly rivals of <paramref name="weekId"/> with their scores at <paramref name="weekFraction"/> (0..1).</summary>
        public static List<Bot> WeeklyBots(int weekId, double weekFraction)
        {
            int[] order = Permutation(Hash(weekId, 0x5A17));
            var bots = new List<Bot>(BotCount);
            for (int slot = 0; slot < BotCount; slot++)
            {
                int n = order[slot];
                bots.Add(new Bot
                {
                    name = Names[n, 0],
                    country = Names[n, 1],
                    avatarId = AvatarIds[(int)(Hash(weekId, 0xA7A0UL + (ulong)slot) % (ulong)AvatarIds.Length)],
                    score = WeeklyBotScore(weekId, slot, weekFraction),
                });
            }
            return bots;
        }

        /// <summary>
        /// Weekly stars of bot <paramref name="slot"/>: a target total for the week reached along a personal curve
        /// (some start late), sampled in hourly steps. Non-decreasing in <paramref name="weekFraction"/>.
        /// </summary>
        public static long WeeklyBotScore(int weekId, int slot, double weekFraction)
        {
            var rng = new Rng(Hash(weekId, 0xB0750000UL + (ulong)slot));
            double target = 150.0 + 3300.0 * Math.Pow(rng.NextDouble(), 1.7);   // most bots are casual
            if (slot < 2) target += 1500.0 + 2000.0 * rng.NextDouble();         // two "whales" per week
            double curve = 0.6 + 0.8 * rng.NextDouble();                         // <1 front-loaded, >1 late sprint
            double start = rng.NextDouble() < 0.3 ? 0.35 * rng.NextDouble() : 0.0;
            double f = Math.Floor(Clamp01(weekFraction) * StepsPerWeek) / StepsPerWeek;
            double local = Clamp01((f - start) / (1.0 - start));
            return (long)Math.Floor(target * Math.Pow(local, curve));
        }

        // ---------------------------------------------------------------------------------------- global

        /// <summary>The 49 offline "global" rivals (large all-time totals growing every day since <see cref="GlobalEpochDay"/>).</summary>
        public static List<Bot> GlobalBots(int dayIndex)
        {
            int[] order = Permutation(0x61087A11UL);
            int days = Math.Max(0, dayIndex - GlobalEpochDay);
            var bots = new List<Bot>(BotCount);
            for (int slot = 0; slot < BotCount; slot++)
            {
                int n = order[slot];
                var rng = new Rng(Hash(0x6106, 0xC0DE0000UL + (ulong)slot));
                double baseStars = 800.0 + 120000.0 * Math.Pow(rng.NextDouble(), 2.4);
                double perDay = 20.0 + 680.0 * rng.NextDouble();
                bots.Add(new Bot
                {
                    name = Names[n, 0],
                    country = Names[n, 1],
                    avatarId = AvatarIds[(int)(rng.Next() % (ulong)AvatarIds.Length)],
                    score = (long)Math.Floor(baseStars + perDay * days),
                });
            }
            return bots;
        }

        // ---------------------------------------------------------------------------------------- week clock

        /// <summary>
        /// Local Monday-based week (same definition as TimeUtil.WeekIndex: weeks since Monday 1970-01-05, local time).
        /// </summary>
        public static void WeekClock(long unixUtc, long utcOffsetSeconds, out int weekId, out double fraction, out long secondsToReset)
        {
            const long Day = 86400, Week = 7 * Day, FirstMonday = 4 * Day;
            long local = unixUtc + utcOffsetSeconds;
            long sinceMonday = local - FirstMonday;
            long week = FloorDiv(sinceMonday, Week);
            long into = sinceMonday - week * Week;
            weekId = (int)week;
            fraction = into / (double)Week;
            secondsToReset = Week - into;
        }

        /// <summary>Elapsed fraction (0..1) of the current week from the seconds left until the reset.</summary>
        public static double WeekFraction(long secondsToReset)
        {
            const double Week = 7 * 86400.0;
            return Clamp01(1.0 - secondsToReset / Week);
        }

        static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        // ---------------------------------------------------------------------------------------- random

        static int[] Permutation(ulong seed)
        {
            int count = NamePoolSize;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            var rng = new Rng(seed);
            for (int i = count - 1; i > 0; i--)
            {
                int j = (int)(rng.Next() % (ulong)(i + 1));
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            return order;
        }

        static ulong Hash(long a, ulong b)
        {
            var rng = new Rng(unchecked((ulong)a * 0x9E3779B97F4A7C15UL ^ b));
            return rng.Next();
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>SplitMix64 — tiny, fast, identical on every platform (unlike UnityEngine.Random/System.Random).</summary>
        struct Rng
        {
            ulong _state;

            public Rng(ulong seed) => _state = seed;

            public ulong Next()
            {
                unchecked
                {
                    ulong z = _state += 0x9E3779B97F4A7C15UL;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
        }
    }
}
