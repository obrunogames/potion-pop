// ============================================================================================================
// Procedural level generator (GDD §3). Deterministic per level number (seed = level * 7919 + 17) and every board is
// VERIFIED solvable by LevelSolver before it is returned; among several solvable candidates the one with the longest
// solution is kept (harder). Pure C#: safe on a worker thread once the catalog is loaded (LevelPrefetch).
// ============================================================================================================
using System;
using System.Collections.Generic;

namespace PotionPop.Levels
{
    public static class LevelGenerator
    {
        /// <summary>Boards tried before relaxing the parameters (one more empty bottle).</summary>
        const int MaxAttempts = 40;

        public static int SeedFor(int level) => unchecked(Math.Max(1, level) * 7919 + 17);

        public static LevelDefinition Generate(int level) => Generate(level, Difficulty.For(level));

        public static LevelDefinition Generate(int level, LevelParams p)
        {
            level = Math.Max(1, level);
            string areaId = AreaIdFor(level);
            if (level == 1) return Tutorial(areaId);

            int seed = SeedFor(level);
            var rng = new Random(seed);
            int capacity = Math.Max(2, p.capacity);
            int nodes = p.colors <= 6 ? 30000 : p.colors <= 9 ? 60000 : 90000;

            for (int relax = 0; relax < 3; relax++)
            {
                LevelDefinition best = null;
                List<Move> bestSolution = null;
                int found = 0;
                for (int attempt = 0; attempt < MaxAttempts && found < Math.Max(1, p.candidates); attempt++)
                {
                    var def = Build(level, seed, areaId, p, capacity, rng);
                    if (def == null) continue;
                    var res = LevelSolver.Solve(def, nodes);
                    if (!res.Solved || res.Moves.Count == 0) continue;
                    found++;
                    if (best == null || res.Moves.Count > bestSolution.Count)
                    {
                        best = def;
                        bestSolution = new List<Move>(res.Moves);
                    }
                }
                if (best != null)
                {
                    Finish(best, bestSolution);
                    return best;
                }
                // Too tight: relax (fewer stones, shorter runs allowed, one more empty bottle).
                p.locks = Math.Max(0, p.locks - 1);
                p.maxRun = Math.Min(capacity - 1, p.maxRun + 1);
                p.empties++;
            }

            // Last resort (never expected): the colors already sorted except one swap — always solvable.
            var fallback = Trivial(level, seed, areaId, Math.Max(2, Math.Min(p.colors, 4)), capacity);
            var fr = LevelSolver.Solve(fallback, 20000);
            Finish(fallback, fr.Solved ? new List<Move>(fr.Moves) : new List<Move>());
            return fallback;
        }

        static string AreaIdFor(int level)
        {
            try
            {
                var area = Areas.AreaForLevel(level);
                if (area != null && !string.IsNullOrEmpty(area.id)) return area.id;
            }
            catch (Exception) { /* catalog unavailable (tests without resources) */ }
            return "forest";
        }

        /// <summary>Star thresholds from the solution length.</summary>
        static void Finish(LevelDefinition def, List<Move> solution)
        {
            def.solution = solution ?? new List<Move>();
            def.par = def.solution.Count;
            def.movesFor3Stars = (int)Math.Ceiling(def.par * 1.3) + 2;
            def.movesFor2Stars = (int)Math.Ceiling(def.par * 1.75) + 4;
        }

        // ------------------------------------------------------------------------------------------------ build

        static LevelDefinition Build(int level, int seed, string areaId, LevelParams p, int capacity, Random rng)
        {
            int colors = Math.Max(1, Math.Min(Difficulty.MaxColors, p.colors));
            int[] palette = PickColors(colors, rng);

            var units = new int[colors * capacity];
            for (int c = 0; c < colors; c++)
                for (int k = 0; k < capacity; k++) units[c * capacity + k] = palette[c];

            int maxRun = Math.Max(1, Math.Min(capacity - 1, p.maxRun));
            bool ok = false;
            for (int tries = 0; tries < 600 && !ok; tries++)
            {
                Shuffle(units, rng);
                ok = Valid(units, colors, capacity, maxRun);
                if (!ok && tries == 400) maxRun = Math.Min(capacity - 1, maxRun + 1);
            }
            if (!ok) return null;

            var def = new LevelDefinition
            {
                number = level,
                seed = seed,
                areaId = areaId,
                hard = p.hard,
                capacity = capacity,
                colorCount = colors,
            };
            for (int b = 0; b < colors; b++)
            {
                var arr = new int[capacity];
                Array.Copy(units, b * capacity, arr, 0, capacity);
                def.bottles.Add(new BottleDef(arr));
            }
            for (int e = 0; e < Math.Max(0, p.empties); e++) def.bottles.Add(new BottleDef(Array.Empty<int>()));

            // Hidden layers: some liquid bottles hide the units under their top.
            if (p.hiddenBottles > 0 && p.hiddenDepth > 0)
            {
                var order = Indices(colors, rng);
                int n = Math.Min(colors, p.hiddenBottles);
                for (int i = 0; i < n; i++) def.bottles[order[i]].hidden = Math.Min(capacity - 1, p.hiddenDepth);
            }

            // Stones: never the first bottle on screen (the player's eye starts there).
            if (p.locks > 0)
            {
                var order = Indices(colors, rng);
                int placed = 0;
                foreach (var idx in order)
                {
                    if (placed >= p.locks) break;
                    if (idx == 0 && colors > 2) continue;
                    def.bottles[idx].lockCount = Math.Max(1, p.lockCount + placed);
                    placed++;
                }
            }
            return def;
        }

        /// <summary>No bottle starts sorted and no run is longer than maxRun.</summary>
        static bool Valid(int[] units, int bottles, int capacity, int maxRun)
        {
            for (int b = 0; b < bottles; b++)
            {
                int o = b * capacity;
                int run = 1;
                bool mono = true;
                for (int k = 1; k < capacity; k++)
                {
                    if (units[o + k] == units[o + k - 1])
                    {
                        run++;
                        if (run > maxRun) return false;
                    }
                    else
                    {
                        run = 1;
                        mono = false;
                    }
                }
                if (mono) return false;
            }
            return true;
        }

        /// <summary>Distinct palette ids: small levels draw from the most distinguishable colors.</summary>
        static int[] PickColors(int count, Random rng)
        {
            int poolSize = Math.Min(Difficulty.PaletteSize, Math.Max(count + 2, 8));
            var pool = new List<int>();
            for (int i = 0; i < poolSize; i++) pool.Add(i);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = pool[i % pool.Count];
            return result;
        }

        static List<int> Indices(int n, Random rng)
        {
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(i);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        static void Shuffle(int[] a, Random rng)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        // ------------------------------------------------------------------------------------------------ fixed boards

        /// <summary>Level 1: two colors, three pours (teaches select → pour → cork).</summary>
        static LevelDefinition Tutorial(string areaId)
        {
            var def = new LevelDefinition
            {
                number = 1,
                seed = SeedFor(1),
                areaId = areaId,
                capacity = Difficulty.Capacity,
                colorCount = 2,
            };
            const int pink = 6, blue = 1;
            def.bottles.Add(new BottleDef(new[] { pink, pink, blue, blue }));
            def.bottles.Add(new BottleDef(new[] { blue, blue, pink, pink }));
            def.bottles.Add(new BottleDef(Array.Empty<int>()));
            var res = LevelSolver.Solve(def, 5000);
            Finish(def, res.Solved ? new List<Move>(res.Moves) : new List<Move>());
            return def;
        }

        static LevelDefinition Trivial(int level, int seed, string areaId, int colors, int capacity)
        {
            var def = new LevelDefinition { number = level, seed = seed, areaId = areaId, capacity = capacity, colorCount = colors };
            for (int c = 0; c < colors; c++)
            {
                var arr = new int[capacity];
                for (int k = 0; k < capacity; k++) arr[k] = c;
                def.bottles.Add(new BottleDef(arr));
            }
            // swap the tops of the first two bottles
            int t = def.bottles[0].units[capacity - 1];
            def.bottles[0].units[capacity - 1] = def.bottles[1].units[capacity - 1];
            def.bottles[1].units[capacity - 1] = t;
            def.bottles.Add(new BottleDef(Array.Empty<int>()));
            def.bottles.Add(new BottleDef(Array.Empty<int>()));
            return def;
        }
    }
}
