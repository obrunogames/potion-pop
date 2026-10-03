// ============================================================================================================
// Water-sort solver: weighted A* over canonical board states, with a node budget.
//
//  * Heuristic h = Σ(color runs per bottle) − (colors on the board). A pour lowers Σruns by at most one, so h is a
//    lower bound on the pours left (admissible); the search weights it (greedier = much faster, still short paths).
//  * Pruning (all solution-preserving): completed bottles are inert; a single-color bottle is never emptied into an
//    empty bottle (it only swaps bottles); among empty open bottles only the first is tried (they are equivalent).
//  * Canonical states: open bottles are interchangeable, so the state hash sorts their hashes; stone-wrapped bottles
//    keep their position (each has its own counter).
//  * Hidden units are irrelevant here: the solver knows every color (generation, hints, dead-end detection).
//  * Exhausted = the whole reachable space was explored without a solution → the board is a dead end.
// Pure C#, no shared mutable state: safe on worker threads (LevelPrefetch).
// ============================================================================================================
using System;
using System.Collections.Generic;

namespace PotionPop.Levels
{
    public static class LevelSolver
    {
        public sealed class Result
        {
            public bool Solved;
            /// <summary>The reachable space was explored completely without finding a solution.</summary>
            public bool Exhausted;
            /// <summary>States generated.</summary>
            public int Nodes;
            /// <summary>Pours (indices of the original board) from the start state to the solution.</summary>
            public readonly List<Move> Moves = new List<Move>();
        }

        /// <summary>Solves from the current state of a board (completed bottles are left out).</summary>
        public static Result Solve(BoardState board, int maxNodes = 60000)
        {
            if (board == null) return new Result();
            var bottles = new List<int[]>();
            var locks = new List<int>();
            var map = new List<int>();
            for (int i = 0; i < board.Count; i++)
            {
                var b = board[i];
                if (b.Completed) continue;
                bottles.Add(b.Units.ToArray());
                locks.Add(Math.Max(0, b.LockRemaining));
                map.Add(i);
            }
            return Solve(board.Capacity, bottles, locks.ToArray(), map.ToArray(), maxNodes);
        }

        /// <summary>Solves a level definition from its start state.</summary>
        public static Result Solve(LevelDefinition def, int maxNodes = 60000) => Solve(new BoardState(def), maxNodes);

        /// <summary>
        /// Two passes: a moderately weighted search (short solutions) and, if its budget runs out, a greedy one with a
        /// larger budget. Exhausted is only reported when a pass explored everything.
        /// </summary>
        public static Result Solve(int capacity, List<int[]> bottles, int[] lockRemaining, int[] indexMap, int maxNodes)
        {
            var first = Search(capacity, bottles, lockRemaining, indexMap, Math.Max(1000, maxNodes), 1, 2);
            if (first.Solved || first.Exhausted) return first;
            var second = Search(capacity, bottles, lockRemaining, indexMap, Math.Max(2000, maxNodes * 2), 1, 6);
            second.Nodes += first.Nodes;
            return second;
        }

        // ------------------------------------------------------------------------------------------------ search

        sealed class Space
        {
            public int C, B, Stride;
            public int[] Lock;          // stone counters at the start (0 = open)
            public int Comp0;           // sorted-full bottles at the start
            public bool AnyLock;
        }

        static Result Search(int capacity, List<int[]> bottles, int[] lockRemaining, int[] indexMap, int maxNodes, int wg, int wh)
        {
            var result = new Result();
            int B = bottles.Count;
            int C = Math.Max(1, capacity);
            if (B == 0)
            {
                result.Solved = true;
                return result;
            }
            var sp = new Space { C = C, B = B, Stride = C + 1, Lock = new int[B] };
            for (int i = 0; i < B; i++)
            {
                sp.Lock[i] = lockRemaining != null && i < lockRemaining.Length ? Math.Max(0, lockRemaining[i]) : 0;
                if (sp.Lock[i] > 0) sp.AnyLock = true;
            }

            var start = new byte[B * sp.Stride];
            for (int i = 0; i < B; i++)
            {
                var u = bottles[i] ?? Array.Empty<int>();
                int n = Math.Min(u.Length, C);
                start[i * sp.Stride] = (byte)n;
                for (int k = 0; k < n; k++) start[i * sp.Stride + 1 + k] = (byte)Math.Max(0, Math.Min(250, u[k]));
            }
            sp.Comp0 = SortedCount(sp, start);

            var states = new List<byte[]>(Math.Min(maxNodes, 1 << 16));
            var parent = new List<int>(states.Capacity);
            var moveOf = new List<int>(states.Capacity);    // from * 256 + to
            var gOf = new List<int>(states.Capacity);
            var best = new Dictionary<ulong, int>(Math.Min(maxNodes, 1 << 16));
            var heap = new LongHeap(1024);

            int h0 = Heuristic(sp, start);
            states.Add(start);
            parent.Add(-1);
            moveOf.Add(-1);
            gOf.Add(0);
            best[Hash(sp, start)] = 0;
            heap.Push(Key(h0 * wh, h0, 0));

            int goal = -1;
            var children = new List<(byte[] s, int move)>(64);
            while (heap.Count > 0)
            {
                long key = heap.Pop();
                int node = (int)(key & 0xFFFFFF);
                var s = states[node];
                int g = gOf[node];
                if (best.TryGetValue(Hash(sp, s), out var bg) && bg < g) continue;   // stale entry
                if (IsGoal(sp, s))
                {
                    goal = node;
                    break;
                }
                if (states.Count >= maxNodes) break;

                children.Clear();
                Expand(sp, s, children);
                foreach (var (child, mv) in children)
                {
                    ulong hc = Hash(sp, child);
                    int gc = g + 1;
                    if (best.TryGetValue(hc, out var old) && old <= gc) continue;
                    best[hc] = gc;
                    int idx = states.Count;
                    if (idx >= 0xFFFFFF) break;
                    states.Add(child);
                    parent.Add(node);
                    moveOf.Add(mv);
                    gOf.Add(gc);
                    int h = Heuristic(sp, child);
                    heap.Push(Key(gc * wg + h * wh, h, idx));
                }
            }

            result.Nodes = states.Count;
            if (goal < 0)
            {
                result.Exhausted = heap.Count == 0 && states.Count < maxNodes;
                return result;
            }
            var path = new List<Move>();
            for (int n = goal; parent[n] >= 0; n = parent[n])
            {
                int mv = moveOf[n];
                int f = mv / 256, t = mv % 256;
                path.Add(new Move(indexMap != null && f < indexMap.Length ? indexMap[f] : f,
                                  indexMap != null && t < indexMap.Length ? indexMap[t] : t));
            }
            path.Reverse();
            result.Solved = true;
            result.Moves.AddRange(path);
            return result;
        }

        static long Key(int f, int h, int node) =>
            ((long)Math.Min(f, 0x7FFFF) << 44) | ((long)Math.Min(h, 0xFFFFF) << 24) | (uint)(node & 0xFFFFFF);

        // ------------------------------------------------------------------------------------------------ state helpers

        static bool SortedFull(Space sp, byte[] s, int i)
        {
            int o = i * sp.Stride;
            int n = s[o];
            if (n != sp.C) return false;
            byte c = s[o + 1];
            for (int k = 2; k <= n; k++) if (s[o + k] != c) return false;
            return true;
        }

        static int SortedCount(Space sp, byte[] s)
        {
            int n = 0;
            for (int i = 0; i < sp.B; i++) if (SortedFull(sp, s, i)) n++;
            return n;
        }

        static bool IsGoal(Space sp, byte[] s)
        {
            int comp = sp.AnyLock ? SortedCount(sp, s) - sp.Comp0 : 0;
            for (int i = 0; i < sp.B; i++)
            {
                int n = s[i * sp.Stride];
                if (n == 0) continue;
                if (!SortedFull(sp, s, i)) return false;
                if (sp.Lock[i] > comp) return false;
            }
            return true;
        }

        static int Heuristic(Space sp, byte[] s)
        {
            int runs = 0;
            ulong seenLo = 0, seenHi = 0;
            int colors = 0;
            for (int i = 0; i < sp.B; i++)
            {
                int o = i * sp.Stride;
                int n = s[o];
                for (int k = 0; k < n; k++)
                {
                    byte c = s[o + 1 + k];
                    if (k == 0 || c != s[o + k]) runs++;
                    if (c < 64)
                    {
                        ulong bit = 1UL << c;
                        if ((seenLo & bit) == 0) { seenLo |= bit; colors++; }
                    }
                    else if (c < 128)
                    {
                        ulong bit = 1UL << (c - 64);
                        if ((seenHi & bit) == 0) { seenHi |= bit; colors++; }
                    }
                }
            }
            return Math.Max(0, runs - colors);
        }

        static void Expand(Space sp, byte[] s, List<(byte[] s, int move)> into)
        {
            int C = sp.C, B = sp.B, st = sp.Stride;
            int comp = sp.AnyLock ? SortedCount(sp, s) - sp.Comp0 : 0;
            for (int i = 0; i < B; i++)
            {
                if (sp.Lock[i] > comp) continue;
                int oi = i * st;
                int ni = s[oi];
                if (ni == 0 || SortedFull(sp, s, i)) continue;
                byte top = s[oi + ni];
                int run = 1;
                while (run < ni && s[oi + ni - run] == top) run++;
                bool mono = run == ni;
                bool triedEmpty = false;
                for (int j = 0; j < B; j++)
                {
                    if (j == i || sp.Lock[j] > comp) continue;
                    int oj = j * st;
                    int nj = s[oj];
                    if (nj >= C) continue;
                    if (nj == 0)
                    {
                        if (mono || triedEmpty) continue;
                        triedEmpty = true;
                    }
                    else if (s[oj + nj] != top) continue;
                    int amount = Math.Min(run, C - nj);
                    var child = (byte[])s.Clone();
                    child[oi] = (byte)(ni - amount);
                    for (int k = 0; k < amount; k++)
                    {
                        child[oi + ni - k] = 0;
                        child[oj + nj + 1 + k] = top;
                    }
                    child[oj] = (byte)(nj + amount);
                    into.Add((child, i * 256 + j));
                }
            }
        }

        static ulong BottleHash(Space sp, byte[] s, int i)
        {
            ulong h = 1469598103934665603UL;
            int o = i * sp.Stride;
            int n = s[o];
            h = (h ^ (ulong)(n + 1)) * 1099511628211UL;
            for (int k = 1; k <= n; k++) h = (h ^ (ulong)(s[o + k] + 7)) * 1099511628211UL;
            return h;
        }

        [ThreadStatic] static ulong[] _hashBuf;

        static ulong Hash(Space sp, byte[] s)
        {
            var buf = _hashBuf;
            if (buf == null || buf.Length < sp.B) _hashBuf = buf = new ulong[Math.Max(16, sp.B)];
            int n = 0;
            ulong fixedPart = 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < sp.B; i++)
            {
                ulong h = BottleHash(sp, s, i);
                if (sp.Lock[i] > 0)
                    fixedPart = Mix(fixedPart ^ (h + (ulong)(i + 1) * 0xC2B2AE3D27D4EB4FUL));
                else
                    buf[n++] = h;
            }
            // insertion sort (B ≤ ~20)
            for (int a = 1; a < n; a++)
            {
                ulong v = buf[a];
                int b = a - 1;
                while (b >= 0 && buf[b] > v) { buf[b + 1] = buf[b]; b--; }
                buf[b + 1] = v;
            }
            ulong acc = fixedPart;
            for (int a = 0; a < n; a++) acc = Mix(acc ^ buf[a]) + 0x165667B19E3779F9UL;
            return acc;
        }

        static ulong Mix(ulong x)
        {
            unchecked
            {
                x ^= x >> 33;
                x *= 0xFF51AFD7ED558CCDUL;
                x ^= x >> 33;
                x *= 0xC4CEB9FE1A85EC53UL;
                x ^= x >> 33;
                return x;
            }
        }

        // ------------------------------------------------------------------------------------------------ heap

        sealed class LongHeap
        {
            long[] _a;
            public int Count { get; private set; }

            public LongHeap(int capacity) { _a = new long[Math.Max(16, capacity)]; }

            public void Push(long v)
            {
                if (Count == _a.Length) Array.Resize(ref _a, _a.Length * 2);
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_a[p] <= v) break;
                    _a[i] = _a[p];
                    i = p;
                }
                _a[i] = v;
            }

            public long Pop()
            {
                long top = _a[0];
                long last = _a[--Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = r < Count && _a[r] < _a[l] ? r : l;
                    if (_a[m] >= last) break;
                    _a[i] = _a[m];
                    i = m;
                }
                if (Count > 0) _a[i] = last;
                return top;
            }
        }
    }
}
