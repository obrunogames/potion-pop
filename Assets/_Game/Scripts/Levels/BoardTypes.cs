// Value types and result objects of the board logic (see BoardState).
using System;
using System.Collections.Generic;

namespace PotionPop.Levels
{
    /// <summary>A pour from one bottle into another (bottle indices).</summary>
    public struct Move : IEquatable<Move>
    {
        public int from;
        public int to;
        public Move(int from, int to) { this.from = from; this.to = to; }
        public bool Equals(Move o) => from == o.from && to == o.to;
        public override bool Equals(object obj) => obj is Move o && Equals(o);
        public override int GetHashCode() => from * 397 ^ to;
        public override string ToString() => $"{from}->{to}";
    }

    /// <summary>Why a pour is not allowed (feedback: shake + toast).</summary>
    public enum PourRefusal { None, SameBottle, SourceEmpty, SourceDone, SourceLocked, TargetLocked, TargetDone, TargetFull, WrongColor, Invalid }

    /// <summary>A hidden unit whose color became visible.</summary>
    public struct Reveal
    {
        public int bottle;
        public int index;      // unit index (0 = bottom)
        public int color;
        public Reveal(int bottle, int index, int color) { this.bottle = bottle; this.index = index; this.color = color; }
    }

    /// <summary>A stone-wrapped bottle's counter went down (remaining 0 = the stone broke).</summary>
    public struct LockTick
    {
        public int bottle;
        public int remaining;
        public LockTick(int bottle, int remaining) { this.bottle = bottle; this.remaining = remaining; }
    }

    /// <summary>Runtime state of one bottle.</summary>
    public sealed class Bottle
    {
        public int Index;
        public int Capacity;
        /// <summary>Liquid units bottom → top (color ids).</summary>
        public readonly List<int> Units = new List<int>(4);
        /// <summary>Units [0, Hidden) are hidden ("?"). Invariant: Hidden &lt; Count unless the bottle is empty.</summary>
        public int Hidden;
        /// <summary>&gt; 0: wrapped in stone, opens after this many more completions.</summary>
        public int LockRemaining;
        /// <summary>Full of one color and corked: inert for the rest of the level.</summary>
        public bool Completed;
        /// <summary>Added by the Extra Bottle booster.</summary>
        public bool Extra;

        public int Count => Units.Count;
        public bool IsEmpty => Units.Count == 0;
        public bool IsFull => Units.Count >= Capacity;
        public int Free => Math.Max(0, Capacity - Units.Count);
        public bool IsLocked => LockRemaining > 0;
        public int Top => Units.Count > 0 ? Units[Units.Count - 1] : -1;
        public bool IsHidden(int index) => index < Hidden;

        /// <summary>
        /// Same-color run at the top: the most a pour can move out of this bottle. Liquid is liquid: hidden units of the
        /// top color right under it pour out too (they reveal on the way).
        /// </summary>
        public int TopRun
        {
            get
            {
                int n = Units.Count;
                if (n == 0) return 0;
                int c = Units[n - 1];
                int run = 0;
                for (int i = n - 1; i >= 0 && Units[i] == c; i--) run++;
                return run;
            }
        }

        /// <summary>Visible part of the top run (what the player can see will pour).</summary>
        public int VisibleTopRun
        {
            get
            {
                int n = Units.Count;
                if (n == 0) return 0;
                int c = Units[n - 1];
                int run = 0;
                for (int i = n - 1; i >= 0 && i >= Hidden && Units[i] == c; i--) run++;
                return run;
            }
        }

        /// <summary>Every unit has the same color (true for an empty bottle).</summary>
        public bool IsMono
        {
            get
            {
                for (int i = 1; i < Units.Count; i++) if (Units[i] != Units[0]) return false;
                return true;
            }
        }

        /// <summary>Full and one color (what a completed bottle looks like).</summary>
        public bool IsSortedFull => Units.Count == Capacity && Capacity > 0 && IsMono;

        /// <summary>Number of same-color runs (0 for an empty bottle).</summary>
        public int Runs
        {
            get
            {
                int runs = 0;
                for (int i = 0; i < Units.Count; i++) if (i == 0 || Units[i] != Units[i - 1]) runs++;
                return runs;
            }
        }

        public Bottle Clone()
        {
            var b = new Bottle { Index = Index, Capacity = Capacity, Hidden = Hidden, LockRemaining = LockRemaining, Completed = Completed, Extra = Extra };
            b.Units.AddRange(Units);
            return b;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < Units.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (i < Hidden) sb.Append('?');
                sb.Append(Units[i]);
            }
            sb.Append(']');
            if (Completed) sb.Append('*');
            if (IsLocked) sb.Append('L').Append(LockRemaining);
            return sb.ToString();
        }
    }

    /// <summary>Everything one pour changed (the view animates it in this order: stream, reveal, cork, stones).</summary>
    public sealed class PourResult
    {
        public int from, to;
        public int color;
        public int amount;
        /// <summary>Unit counts before the pour.</summary>
        public int fromCountBefore, toCountBefore;
        /// <summary>Hidden units of the poured color that left the source with the stream (they reveal mid-pour).</summary>
        public int hiddenPoured;
        /// <summary>The target became a full single-color bottle (cork).</summary>
        public bool completed;
        /// <summary>Units revealed by this pour (new top of the source; whole target when completed).</summary>
        public readonly List<Reveal> reveals = new List<Reveal>();
        /// <summary>Stone counters that went down because of the completion.</summary>
        public readonly List<LockTick> lockTicks = new List<LockTick>();
        public bool won;
    }

    /// <summary>One unit removed by the Magic Wand / Rainbow Potion (index in the bottle before the removal).</summary>
    public struct RemovedUnit
    {
        public int bottle;
        public int index;
        public RemovedUnit(int bottle, int index) { this.bottle = bottle; this.index = index; }
    }

    /// <summary>Result of removing every unit of one color (Magic Wand / Rainbow Potion).</summary>
    public sealed class RemoveColorResult
    {
        public int color;
        public readonly List<RemovedUnit> removed = new List<RemovedUnit>();
        public readonly List<Reveal> reveals = new List<Reveal>();
        /// <summary>Completed bottles of that color (now empty, uncorked).</summary>
        public readonly List<int> emptiedCompleted = new List<int>();
        public readonly List<LockTick> lockTicks = new List<LockTick>();
        /// <summary>Bottles whose units changed (the view refreshes them).</summary>
        public readonly List<int> changed = new List<int>();
        public bool won;
    }

    /// <summary>Result of the Shuffle booster: per changed bottle, its new units.</summary>
    public sealed class ShuffleResult
    {
        public readonly List<int> changed = new List<int>();
        public readonly List<LockTick> lockTicks = new List<LockTick>();
        public bool won;
    }

    /// <summary>What an undo restored.</summary>
    public sealed class UndoResult
    {
        public int from, to;          // of the pour that was taken back
        public int color;
        public int amount;
        /// <summary>The target had been completed by that pour (its cork comes off).</summary>
        public bool uncorked;
        /// <summary>Stones that got their counter back (remaining after the undo).</summary>
        public readonly List<LockTick> lockTicks = new List<LockTick>();
    }
}
