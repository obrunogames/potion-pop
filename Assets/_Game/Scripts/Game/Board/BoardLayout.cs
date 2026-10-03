// ============================================================================================================
// Board module: bottle layout math (pure: only Vector2 / Mathf). Bottles are laid out in 1..3 centered rows
// (balanced, extra bottles in the top rows, at most 7 per row while fewer rows can hold them) and scaled uniformly
// to the largest size that fits the board rect with room for the shelf, the lift above every bottle and the gaps.
// More rows are only chosen when they give clearly bigger bottles (≥ 12%), so small boards stay on one line.
// Board-local coordinates: origin at the board rect center, y up, units = canvas units.
// ============================================================================================================
using UnityEngine;

namespace PotionPop.Game.Board
{
    internal sealed class BoardLayout
    {
        public const int MaxRows = 3;
        public const int MaxPerRow = 7;
        /// <summary>Free space between neighbours (fraction of the glass width).</summary>
        public const float GapX = 0.32f;
        /// <summary>Free space between rows (fraction of the glass height): lift + breathing room.</summary>
        public const float RowGap = 0.28f;
        /// <summary>Room above the top row (fraction of the glass height): lift + glow.</summary>
        public const float HeadRoom = 0.16f;
        /// <summary>Room below the bottom row (fraction of the glass height): shelf plank + brackets + shadow.</summary>
        public const float FootRoom = 0.13f;
        /// <summary>Side margin (fraction of the glass width) on each side of the widest row.</summary>
        public const float SideMargin = 0.06f;
        public const float MaxScale = 1.45f;
        public const float MinScale = 0.05f;
        /// <summary>A layout with more rows must give bottles this much bigger to be preferred.</summary>
        public const float MoreRowsMustWinBy = 1.12f;

        public int count;
        public int rows;
        public int[] rowCounts = new int[0];
        public float scale = 1f;
        /// <summary>Board-local position of each bottle's shape origin (inner bottom center).</summary>
        public Vector2[] origins = new Vector2[0];
        public int[] rowOf = new int[0];
        public int[] colOf = new int[0];
        /// <summary>Glass bottom (y) of each row, board units.</summary>
        public float[] rowBottom = new float[0];
        /// <summary>Distance between neighbours' axes, board units.</summary>
        public float pitchX;
        /// <summary>Glass size (unscaled, design pixels) the layout was computed for.</summary>
        public float glassW, glassH;

        /// <summary>Glass height on screen (board units).</summary>
        public float BottleHeight => glassH * scale;
        public float BottleWidth => glassW * scale;

        /// <summary>
        /// Computes the layout of <paramref name="n"/> bottles in a rect of <paramref name="rect"/> board units. Glass size in
        /// design pixels; <paramref name="originAboveBottom"/> = distance from the glass bottom up to the shape origin.
        /// </summary>
        public static BoardLayout Compute(int n, Vector2 rect, float gw, float gh, float originAboveBottom)
        {
            n = Mathf.Max(0, n);
            gw = Mathf.Max(1f, gw);
            gh = Mathf.Max(1f, gh);
            float rw = Mathf.Max(1f, rect.x), rh = Mathf.Max(1f, rect.y);

            int bestRows = 1;
            float bestScale = -1f;
            for (int r = 1; r <= MaxRows; r++)
            {
                if (r > 1 && r > n) break;
                int widest = Widest(n, r);
                if (widest > MaxPerRow && r < MaxRows) continue;
                float contentW = Mathf.Max(1, widest) * gw + Mathf.Max(0, widest - 1) * GapX * gw + 2f * SideMargin * gw;
                float contentH = r * gh + (r - 1) * RowGap * gh + (HeadRoom + FootRoom) * gh;
                float s = Mathf.Min(Mathf.Min(rw / contentW, rh / contentH), MaxScale);
                if (bestScale < 0f || s > bestScale * MoreRowsMustWinBy)
                {
                    bestScale = s;
                    bestRows = r;
                }
            }

            var l = new BoardLayout
            {
                count = n,
                rows = bestRows,
                scale = Mathf.Max(MinScale, bestScale),
                glassW = gw,
                glassH = gh,
                rowCounts = Split(n, bestRows),
                origins = new Vector2[n],
                rowOf = new int[n],
                colOf = new int[n],
                rowBottom = new float[bestRows],
            };
            float s2 = l.scale;
            l.pitchX = gw * (1f + GapX) * s2;
            float contentH2 = (bestRows * gh + (bestRows - 1) * RowGap * gh + (HeadRoom + FootRoom) * gh) * s2;
            float top = contentH2 * 0.5f;
            int index = 0;
            for (int row = 0; row < bestRows; row++)
            {
                float glassTop = top - HeadRoom * gh * s2 - row * (gh + RowGap * gh) * s2;
                float bottom = glassTop - gh * s2;
                l.rowBottom[row] = bottom;
                int c = l.rowCounts[row];
                for (int k = 0; k < c && index < n; k++, index++)
                {
                    l.origins[index] = new Vector2((k - (c - 1) * 0.5f) * l.pitchX, bottom + originAboveBottom * s2);
                    l.rowOf[index] = row;
                    l.colOf[index] = k;
                }
            }
            return l;
        }

        /// <summary>Bottles per row, balanced, the extra ones in the top rows (7 → 4 + 3, 16 → 6 + 5 + 5).</summary>
        public static int[] Split(int n, int rows)
        {
            rows = Mathf.Max(1, rows);
            var counts = new int[rows];
            int baseCount = n / rows, extra = n % rows;
            for (int r = 0; r < rows; r++) counts[r] = baseCount + (r < extra ? 1 : 0);
            return counts;
        }

        static int Widest(int n, int rows) => (n + rows - 1) / Mathf.Max(1, rows);

        /// <summary>Hit rect (board units, x/y = lower-left) of a bottle: its whole column including the space above.</summary>
        public Rect HitRect(int i)
        {
            if (i < 0 || i >= count) return new Rect(0f, 0f, 0f, 0f);
            int row = rowOf[i];
            float below = FootRoom * 0.6f;
            float bottom = rowBottom[row] - below * BottleHeight;
            // up to the next row's lower edge (never overlapping it), or the headroom for the top row
            float above = row == 0 ? HeadRoom : Mathf.Max(0f, RowGap - below - 0.01f);
            float top = rowBottom[row] + BottleHeight * (1f + above);
            float w = pitchX > 0f ? pitchX : BottleWidth * 1.4f;
            return new Rect(origins[i].x - w * 0.5f, bottom, w, top - bottom);
        }
    }
}
