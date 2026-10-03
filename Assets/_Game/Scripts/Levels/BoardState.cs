// ============================================================================================================
// Authoritative water-sort rules (GDD §2). Pure C#, no Unity dependency: the view and the session only read it and
// call its commands; every command returns a result object describing what changed so the view can animate it.
//
//  * A bottle holds up to Capacity units (stacked bottom → top). Every color has exactly Capacity units.
//  * Pour: the same-color run at the top of the source moves into the target if the target is empty or its top has
//    that color; as many units as fit move. Hidden ("?") units of that color right under the top pour out too and
//    reveal on the way (liquid is liquid), so the rules don't depend on what the player knows.
//  * A bottle that becomes full of one color is completed: it gets a cork and is inert (never poured from/into).
//  * Hidden units are a prefix at the bottom; when the visible part of a bottle empties, the new top reveals.
//  * Stone-wrapped bottles can't be touched; every completed bottle (and every color removed by the wand) ticks
//    every stone down by one; at 0 the stone breaks. A rewarded ad breaks one at once.
//  * Win: every bottle is empty or completed. Stuck: no legal pour left.
// ============================================================================================================
using System;
using System.Collections.Generic;

namespace PotionPop.Levels
{
    public sealed class BoardState
    {
        /// <summary>Undo history kept per level (oldest entries are dropped beyond this).</summary>
        public const int MaxUndo = 200;

        sealed class UndoRecord
        {
            public int from, to, color, amount;
            public bool completed;
            public List<int> lockBottles;   // stones the completion ticked down
        }

        readonly List<Bottle> _bottles = new List<Bottle>();
        readonly List<UndoRecord> _undo = new List<UndoRecord>();

        public LevelDefinition Definition { get; private set; }
        public int Capacity { get; private set; }
        public IReadOnlyList<Bottle> Bottles => _bottles;
        public int Count => _bottles.Count;
        public Bottle this[int index] => _bottles[index];

        /// <summary>Pours made in this attempt (an undo takes one back).</summary>
        public int Moves { get; private set; }
        /// <summary>Completions + colors removed by boosters (what stone counters count).</summary>
        public int Ticks { get; private set; }
        /// <summary>Bottles added by the Extra Bottle booster.</summary>
        public int ExtraBottles { get; private set; }
        public bool CanUndo => _undo.Count > 0;
        public int UndoDepth => _undo.Count;

        BoardState() { }

        public BoardState(LevelDefinition def)
        {
            Definition = def ?? throw new ArgumentNullException(nameof(def));
            Capacity = Math.Max(1, def.capacity);
            var defs = def.bottles ?? new List<BottleDef>();
            for (int i = 0; i < defs.Count; i++)
            {
                var bd = defs[i] ?? new BottleDef();
                var b = new Bottle { Index = i, Capacity = Capacity, LockRemaining = Math.Max(0, bd.lockCount) };
                if (bd.units != null)
                    for (int u = 0; u < bd.units.Length && u < Capacity; u++) b.Units.Add(bd.units[u]);
                b.Hidden = b.Count == 0 ? 0 : Math.Max(0, Math.Min(bd.hidden, b.Count - 1));
                if (b.IsSortedFull)
                {
                    b.Completed = true;
                    b.Hidden = 0;
                }
                _bottles.Add(b);
            }
        }

        // ------------------------------------------------------------------------------------------------ queries

        public int CompletedCount
        {
            get
            {
                int n = 0;
                foreach (var b in _bottles) if (b.Completed) n++;
                return n;
            }
        }

        /// <summary>Bottles that will be completed in a solved board (one per color still on the board).</summary>
        public int ColorsLeft
        {
            get
            {
                var seen = new HashSet<int>();
                foreach (var b in _bottles) foreach (var u in b.Units) seen.Add(u);
                return seen.Count;
            }
        }

        public bool IsWon
        {
            get
            {
                foreach (var b in _bottles)
                    if (!(b.IsEmpty || b.Completed || (b.IsSortedFull && !b.IsLocked))) return false;
                return true;
            }
        }

        public bool AnyLocked
        {
            get
            {
                foreach (var b in _bottles) if (b.IsLocked) return true;
                return false;
            }
        }

        public bool AnyHidden
        {
            get
            {
                foreach (var b in _bottles) if (b.Hidden > 0) return true;
                return false;
            }
        }

        /// <summary>Why pouring from → to is not allowed (None = legal).</summary>
        public PourRefusal Check(int from, int to)
        {
            if (from < 0 || to < 0 || from >= _bottles.Count || to >= _bottles.Count) return PourRefusal.Invalid;
            if (from == to) return PourRefusal.SameBottle;
            var a = _bottles[from];
            var b = _bottles[to];
            if (a.IsLocked) return PourRefusal.SourceLocked;
            if (a.Completed) return PourRefusal.SourceDone;
            if (a.IsEmpty) return PourRefusal.SourceEmpty;
            if (b.IsLocked) return PourRefusal.TargetLocked;
            if (b.Completed) return PourRefusal.TargetDone;
            if (b.IsFull) return PourRefusal.TargetFull;
            if (!b.IsEmpty && b.Top != a.Top) return PourRefusal.WrongColor;
            return PourRefusal.None;
        }

        /// <summary>Units a pour from → to would move (0 = illegal).</summary>
        public int PourAmount(int from, int to)
        {
            if (Check(from, to) != PourRefusal.None) return 0;
            return Math.Min(_bottles[from].TopRun, _bottles[to].Free);
        }

        public bool CanPour(int from, int to) => PourAmount(from, to) > 0;

        /// <summary>Whether a bottle can be picked up as a pour source.</summary>
        public bool CanSelect(int index)
        {
            if (index < 0 || index >= _bottles.Count) return false;
            var b = _bottles[index];
            return !b.IsEmpty && !b.Completed && !b.IsLocked;
        }

        /// <summary>At least one legal pour exists.</summary>
        public bool HasAnyMove
        {
            get
            {
                for (int i = 0; i < _bottles.Count; i++)
                {
                    if (!CanSelect(i)) continue;
                    for (int j = 0; j < _bottles.Count; j++)
                        if (j != i && PourAmount(i, j) > 0) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// At least one pour that can make progress: excludes emptying a single-color bottle into an empty one (it only
        /// swaps bottles). A board with legal pours but no useful one is just as stuck for the player.
        /// </summary>
        public bool HasUsefulMove
        {
            get
            {
                for (int i = 0; i < _bottles.Count; i++)
                {
                    if (!CanSelect(i)) continue;
                    var a = _bottles[i];
                    bool wholeRun = a.TopRun == a.Count && a.Hidden == 0;
                    for (int j = 0; j < _bottles.Count; j++)
                    {
                        if (j == i || PourAmount(i, j) <= 0) continue;
                        if (_bottles[j].IsEmpty && wholeRun) continue;
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Every legal pour (for the solver-less helpers and tests).</summary>
        public List<Move> LegalMoves()
        {
            var list = new List<Move>();
            for (int i = 0; i < _bottles.Count; i++)
            {
                if (!CanSelect(i)) continue;
                for (int j = 0; j < _bottles.Count; j++)
                    if (j != i && PourAmount(i, j) > 0) list.Add(new Move(i, j));
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------------ pour

        /// <summary>Pours from → to. Returns null (and changes nothing) when the pour is illegal.</summary>
        public PourResult Pour(int from, int to)
        {
            int n = PourAmount(from, to);
            if (n <= 0) return null;
            var a = _bottles[from];
            var b = _bottles[to];
            int color = a.Top;
            var r = new PourResult
            {
                from = from, to = to, color = color, amount = n,
                fromCountBefore = a.Count, toCountBefore = b.Count,
            };
            a.Units.RemoveRange(a.Count - n, n);
            if (a.Hidden > a.Count)
            {
                r.hiddenPoured = a.Hidden - a.Count;
                a.Hidden = a.Count;
            }
            for (int i = 0; i < n; i++) b.Units.Add(color);
            Moves++;
            RevealTop(a, r.reveals);

            var rec = new UndoRecord { from = from, to = to, color = color, amount = n };
            if (b.IsSortedFull)
            {
                Complete(b, r.reveals);
                r.completed = true;
                rec.completed = true;
                rec.lockBottles = Tick(r.lockTicks);
            }
            PushUndo(rec);
            r.won = IsWon;
            return r;
        }

        void Complete(Bottle b, List<Reveal> reveals)
        {
            for (int i = 0; i < b.Hidden; i++) reveals?.Add(new Reveal(b.Index, i, b.Units[i]));
            b.Hidden = 0;
            b.Completed = true;
        }

        static void RevealTop(Bottle b, List<Reveal> into)
        {
            if (b.Count > 0 && b.Hidden >= b.Count)
            {
                b.Hidden = b.Count - 1;
                into?.Add(new Reveal(b.Index, b.Count - 1, b.Top));
            }
            if (b.Count == 0) b.Hidden = 0;
        }

        /// <summary>One completion / removed color: every stone goes down by one. Returns the stones it touched.</summary>
        List<int> Tick(List<LockTick> into)
        {
            Ticks++;
            List<int> touched = null;
            foreach (var b in _bottles)
            {
                if (!b.IsLocked) continue;
                b.LockRemaining--;
                (touched ??= new List<int>()).Add(b.Index);
                into?.Add(new LockTick(b.Index, b.LockRemaining));
            }
            return touched;
        }

        void PushUndo(UndoRecord rec)
        {
            _undo.Add(rec);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        }

        // ------------------------------------------------------------------------------------------------ undo

        /// <summary>
        /// Takes back the last pour (Undo booster). Revealed colors stay revealed (the player already saw them).
        /// Returns null when there is nothing to undo.
        /// </summary>
        public UndoResult Undo()
        {
            if (_undo.Count == 0) return null;
            var rec = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            var a = _bottles[rec.from];
            var b = _bottles[rec.to];
            var r = new UndoResult { from = rec.from, to = rec.to, color = rec.color, amount = rec.amount, uncorked = rec.completed };
            int take = Math.Min(rec.amount, b.Count);
            b.Units.RemoveRange(b.Count - take, take);
            if (b.Hidden >= b.Count && b.Count > 0) b.Hidden = b.Count - 1;
            if (b.Count == 0) b.Hidden = 0;
            for (int i = 0; i < take; i++) a.Units.Add(rec.color);
            if (rec.completed)
            {
                b.Completed = false;
                Ticks = Math.Max(0, Ticks - 1);
                if (rec.lockBottles != null)
                    foreach (var idx in rec.lockBottles)
                    {
                        var s = _bottles[idx];
                        s.LockRemaining++;
                        r.lockTicks.Add(new LockTick(idx, s.LockRemaining));
                    }
            }
            Moves = Math.Max(0, Moves - 1);
            return r;
        }

        /// <summary>Forgets the undo history (after boosters that rewrite the board).</summary>
        public void ClearUndo() => _undo.Clear();

        // ------------------------------------------------------------------------------------------------ boosters

        /// <summary>Extra Bottle booster: appends an empty bottle. Returns its index.</summary>
        public int AddBottle()
        {
            var b = new Bottle { Index = _bottles.Count, Capacity = Capacity, Extra = true };
            _bottles.Add(b);
            ExtraBottles++;
            return b.Index;
        }

        /// <summary>Breaks a stone right away (rewarded ad). Null when that bottle isn't locked.</summary>
        public LockTick? BreakLock(int index)
        {
            if (index < 0 || index >= _bottles.Count || !_bottles[index].IsLocked) return null;
            _bottles[index].LockRemaining = 0;
            // An undo must never put this stone back.
            foreach (var rec in _undo) rec.lockBottles?.Remove(index);
            return new LockTick(index, 0);
        }

        /// <summary>Crystal Ball: reveals every hidden unit.</summary>
        public List<Reveal> RevealAll()
        {
            var list = new List<Reveal>();
            foreach (var b in _bottles)
            {
                for (int i = 0; i < b.Hidden && i < b.Count; i++) list.Add(new Reveal(b.Index, i, b.Units[i]));
                b.Hidden = 0;
            }
            return list;
        }

        /// <summary>
        /// Colors the Magic Wand may remove, best first: the most buried color (units under other colors, hidden units,
        /// spread over many bottles) is the one that helps most.
        /// </summary>
        public List<int> WandCandidates()
        {
            var score = new Dictionary<int, int>();
            var bottlesOf = new Dictionary<int, HashSet<int>>();
            foreach (var b in _bottles)
            {
                if (b.Completed) continue;
                for (int i = 0; i < b.Count; i++)
                {
                    int c = b.Units[i];
                    int above = b.Count - 1 - i;
                    int s = above * 2 + (i < b.Hidden ? 2 : 0) + (b.IsLocked ? 3 : 0);
                    score[c] = (score.TryGetValue(c, out var v) ? v : 0) + s;
                    if (!bottlesOf.TryGetValue(c, out var set)) bottlesOf[c] = set = new HashSet<int>();
                    set.Add(b.Index);
                }
            }
            var colors = new List<int>(score.Keys);
            colors.Sort((x, y) =>
            {
                int sx = score[x] + bottlesOf[x].Count * 3, sy = score[y] + bottlesOf[y].Count * 3;
                return sx != sy ? sy.CompareTo(sx) : x.CompareTo(y);
            });
            return colors;
        }

        /// <summary>
        /// Magic Wand / Rainbow Potion: removes every unit of one color from every bottle (stone-wrapped ones too).
        /// Counts as one completion for the stones. Clears the undo history.
        /// </summary>
        public RemoveColorResult RemoveColor(int color)
        {
            var r = new RemoveColorResult { color = color };
            bool any = false;
            foreach (var b in _bottles)
            {
                int hiddenRemoved = 0;
                bool changed = false;
                for (int i = b.Count - 1; i >= 0; i--)
                {
                    if (b.Units[i] != color) continue;
                    r.removed.Add(new RemovedUnit(b.Index, i));
                    if (i < b.Hidden) hiddenRemoved++;
                    changed = true;
                }
                if (!changed) continue;
                any = true;
                if (b.Completed)
                {
                    b.Completed = false;
                    r.emptiedCompleted.Add(b.Index);
                }
                b.Units.RemoveAll(u => u == color);
                b.Hidden = Math.Max(0, b.Hidden - hiddenRemoved);
                RevealTop(b, r.reveals);
                r.changed.Add(b.Index);
            }
            if (!any) return null;
            Tick(r.lockTicks);
            // A bottle that happens to be full and single-colored after the removal is completed too.
            foreach (var b in _bottles)
                if (!b.Completed && !b.IsLocked && b.IsSortedFull)
                {
                    Complete(b, r.reveals);
                    Tick(r.lockTicks);
                }
            _undo.Clear();
            r.won = IsWon;
            return r;
        }

        /// <summary>
        /// Shuffle booster: redistributes the units of every open bottle (not completed, not stone-wrapped), keeping
        /// each bottle's unit count and hidden count. Tries up to <paramref name="attempts"/> arrangements and keeps the
        /// first one the solver can still finish (when <paramref name="solverNodes"/> &gt; 0). Null when nothing could
        /// be shuffled (then the board is unchanged). Clears the undo history.
        /// </summary>
        public ShuffleResult Shuffle(Random rng, int attempts = 24, int solverNodes = 20000)
        {
            rng ??= new Random();
            var open = new List<Bottle>();
            var pool = new List<int>();
            foreach (var b in _bottles)
            {
                if (b.Completed || b.IsLocked || b.IsEmpty) continue;
                open.Add(b);
                pool.AddRange(b.Units);
            }
            if (open.Count < 2 || pool.Count < 2) return null;

            var original = new List<int[]>();
            foreach (var b in open) original.Add(b.Units.ToArray());
            int[] best = null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var candidate = pool.ToArray();
                for (int i = candidate.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (candidate[i], candidate[j]) = (candidate[j], candidate[i]);
                }
                // Reject arrangements that leave a bottle sorted (free completions) or change nothing.
                bool sorted = false, same = true;
                int k = 0;
                for (int o = 0; o < open.Count; o++)
                {
                    int n = open[o].Count;
                    bool mono = true;
                    for (int u = 0; u < n; u++)
                    {
                        if (candidate[k + u] != candidate[k]) mono = false;
                        if (candidate[k + u] != original[o][u]) same = false;
                    }
                    if (mono && n == Capacity) sorted = true;
                    k += n;
                }
                if (same || (sorted && attempt < attempts - 1)) continue;
                if (solverNodes > 0)
                {
                    var test = Clone();
                    Apply(test, open, candidate);
                    var res = LevelSolver.Solve(test, solverNodes);
                    if (!res.Solved && attempt < attempts - 1) continue;
                    if (!res.Solved) break;
                }
                best = candidate;
                break;
            }
            if (best == null) return null;

            var result = new ShuffleResult();
            Apply(this, open, best);
            foreach (var b in open) result.changed.Add(b.Index);
            foreach (var b in _bottles)
                if (!b.Completed && !b.IsLocked && b.IsSortedFull)
                {
                    Complete(b, null);
                    Tick(result.lockTicks);
                }
            _undo.Clear();
            result.won = IsWon;
            return result;
        }

        static void Apply(BoardState target, List<Bottle> open, int[] units)
        {
            int k = 0;
            foreach (var src in open)
            {
                var b = target._bottles[src.Index];
                int n = b.Count;
                for (int u = 0; u < n; u++) b.Units[u] = units[k + u];
                k += n;
            }
        }

        // ------------------------------------------------------------------------------------------------ solver

        /// <summary>Next pour toward a solution (null when none was found within the node budget).</summary>
        public Move? Hint(int maxNodes = 30000)
        {
            var res = LevelSolver.Solve(this, maxNodes);
            return res.Solved && res.Moves.Count > 0 ? res.Moves[0] : (Move?)null;
        }

        /// <summary>Deep copy without the undo history.</summary>
        public BoardState Clone()
        {
            var s = new BoardState
            {
                Definition = Definition,
                Capacity = Capacity,
                Moves = Moves,
                Ticks = Ticks,
                ExtraBottles = ExtraBottles,
            };
            foreach (var b in _bottles) s._bottles.Add(b.Clone());
            return s;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in _bottles) sb.Append(b).Append(' ');
            return sb.ToString();
        }
    }
}
