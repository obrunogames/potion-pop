// Serializable level data (JsonUtility friendly: public fields only, no dictionaries).
// A LevelDefinition is immutable by convention once generated: BoardState deep-copies it and never writes back.
using System;
using System.Collections.Generic;

namespace PotionPop.Levels
{
    /// <summary>One bottle at the start of a level.</summary>
    [Serializable]
    public class BottleDef
    {
        /// <summary>Liquid color ids (see <c>PotionPop.Liquids</c>), bottom → top. Length ≤ capacity.</summary>
        public int[] units = Array.Empty<int>();
        /// <summary>The bottom <c>hidden</c> units start hidden ("?"). Always &lt; units.Length (the top is visible).</summary>
        public int hidden;
        /// <summary>0 = free. N = wrapped in stone until N bottles are completed (or a rewarded ad breaks it).</summary>
        public int lockCount;

        public BottleDef() { }

        public BottleDef(int[] units, int hidden = 0, int lockCount = 0)
        {
            this.units = units ?? Array.Empty<int>();
            this.hidden = hidden;
            this.lockCount = lockCount;
        }

        public BottleDef Clone() => new BottleDef((int[])(units ?? Array.Empty<int>()).Clone(), hidden, lockCount);
    }

    [Serializable]
    public class LevelDefinition
    {
        public int number;
        public int seed;
        /// <summary>World theme (backgrounds, accent).</summary>
        public string areaId;
        public bool hard;
        /// <summary>Units a bottle holds (every color has exactly this many units on the board).</summary>
        public int capacity = 4;
        /// <summary>Distinct colors on the board.</summary>
        public int colorCount;
        public List<BottleDef> bottles = new List<BottleDef>();
        /// <summary>Moves of the verified solution found by the generator.</summary>
        public int par;
        /// <summary>Most moves that still earn 3 stars / 2 stars (more moves = 1 star).</summary>
        public int movesFor3Stars;
        public int movesFor2Stars;
        /// <summary>A verified full solution found by the generator (not serialized). The hint system starts from it.</summary>
        [NonSerialized] public List<Move> solution;

        public int BottleCount => bottles != null ? bottles.Count : 0;

        public bool HasHidden
        {
            get
            {
                if (bottles == null) return false;
                foreach (var b in bottles) if (b != null && b.hidden > 0) return true;
                return false;
            }
        }

        public bool HasLocks
        {
            get
            {
                if (bottles == null) return false;
                foreach (var b in bottles) if (b != null && b.lockCount > 0) return true;
                return false;
            }
        }

        public int TotalUnits()
        {
            if (bottles == null) return 0;
            int n = 0;
            foreach (var b in bottles) if (b?.units != null) n += b.units.Length;
            return n;
        }

        /// <summary>Distinct color ids on the board, ascending.</summary>
        public int[] ColorsUsed()
        {
            var set = new SortedSet<int>();
            if (bottles != null)
                foreach (var b in bottles)
                    if (b?.units != null)
                        foreach (var u in b.units) set.Add(u);
            var arr = new int[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        /// <summary>Star rating (1..3) of a win in <paramref name="moves"/> pours.</summary>
        public int StarsFor(int moves)
        {
            if (movesFor3Stars <= 0) return 3;
            if (moves <= movesFor3Stars) return 3;
            if (moves <= movesFor2Stars) return 2;
            return 1;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("L").Append(number).Append(hard ? " HARD" : "").Append(" colors=").Append(colorCount)
              .Append(" bottles=").Append(BottleCount).Append(" par=").Append(par).Append(" | ");
            if (bottles != null)
                foreach (var b in bottles)
                {
                    sb.Append('[');
                    if (b?.units != null)
                        for (int i = 0; i < b.units.Length; i++)
                        {
                            if (i > 0) sb.Append(',');
                            if (i < b.hidden) sb.Append('?');
                            sb.Append(b.units[i]);
                        }
                    sb.Append(']');
                    if (b != null && b.lockCount > 0) sb.Append("L").Append(b.lockCount);
                    sb.Append(' ');
                }
            return sb.ToString();
        }
    }
}
