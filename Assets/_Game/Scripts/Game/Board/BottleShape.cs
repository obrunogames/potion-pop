// ============================================================================================================
// Board module: parametric bottle silhouette + liquid volume math. Pure C# (only the UnityEngine.Vector2 / Mathf
// value types, no engine calls) so it can be compiled and checked outside Unity.
//
// Shape units (Resources/bottle_shape.json, shared with Tools/process_art.py that draws the glass sprites): the inner
// body is 1.0 wide, y = 0 is the inner bottom, y grows upward, x = 0 is the axis.
//  * body:     x ∈ [−w/2, w/2] from y = 0 to bodyHeight, bottom corners = quarter circles of radius bottomRadius;
//  * shoulder: quadratic Bézier from (±w/2, bodyHeight) to (±neckWidth/2, bodyHeight + shoulderHeight), control
//              point (±w/2, bodyHeight + shoulderHeight);
//  * neck:     x ∈ [±neckWidth/2] up to the mouth at y = bodyHeight + shoulderHeight + neckHeight.
// The interior is split into CONVEX pieces (body + shoulders, neck). A liquid surface is the line dot(p, n) = c where
// n is the world "up" expressed in the bottle's local space; the liquid under it is every piece clipped by the
// half-plane dot(p, n) ≤ c, so its area ("volume") is a sum of convex clips. LevelForVolume inverts that by binary
// search; PourAngleDeg gives the tilt at which a volume reaches the mouth lip (tabulated once per shape).
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.Game.Board
{
    public sealed class BottleShape
    {
        // ------------------------------------------------------------------------------------------- parameters

        public float innerWidth = 1f;
        public float bottomRadius = 0.34f;
        public float bodyHeight = 2.62f;
        public float shoulderHeight = 0.30f;
        public float neckWidth = 0.56f;
        public float neckHeight = 0.30f;
        public float lipWidth = 0.82f;
        public float lipHeight = 0.15f;
        public float glass = 0.075f;
        /// <summary>Liquid height when the bottle holds <see cref="capacity"/> units upright: the mouth, so a full bottle
        /// is brim-full (the top unit fills the shoulders and the neck up to the lip).</summary>
        public float fillHeight = 3.22f;
        public int capacity = 4;

        /// <summary>Largest tilt the pour solver considers (degrees).</summary>
        public const float MaxTiltDeg = 150f;
        /// <summary>Scratch buffer size (vertices) for clipping; the body piece has ~2·(arc + curve) + 4 vertices.</summary>
        public const int MaxVertices = 128;

        const int PourTableSize = 65;

        // ------------------------------------------------------------------------------------------- derived

        public float HalfWidth { get; private set; }
        public float NeckHalf { get; private set; }
        public float ShoulderTop { get; private set; }
        public float MouthY { get; private set; }
        /// <summary>Convex interior pieces (counter-clockwise).</summary>
        public Vector2[][] Pieces { get; private set; }
        /// <summary>Interior outline as an open polyline from the left mouth corner, down and around the bottom, up to the
        /// right mouth corner (interior on its left). Used by the procedural glass fallback.</summary>
        public Vector2[] Outline { get; private set; }
        /// <summary>Total interior area (shape units²).</summary>
        public float InteriorArea { get; private set; }
        /// <summary>Interior area below <see cref="fillHeight"/> when upright (= capacity units of liquid).</summary>
        public float FillArea { get; private set; }
        public float UnitVolume => FillArea / Math.Max(1, capacity);
        /// <summary>Outer bounds of the glass (shape units), lip included.</summary>
        public float GlassLeft { get; private set; }
        public float GlassRight { get; private set; }
        public float GlassBottom { get; private set; }
        public float GlassTop { get; private set; }
        public float GlassWidth => GlassRight - GlassLeft;
        public float GlassHeight => GlassTop - GlassBottom;

        float[] _pourTable;
        bool _built;

        // Single-threaded scratch buffers (main thread only).
        static readonly Vector2[] _bufA = new Vector2[MaxVertices];
        static readonly Vector2[] _bufB = new Vector2[MaxVertices];

        public bool IsBuilt => _built;

        // ------------------------------------------------------------------------------------------- build

        /// <summary>Shape with the default parameters (same numbers as the shipped bottle_shape.json), built.</summary>
        public static BottleShape CreateDefault()
        {
            var s = new BottleShape();
            s.Build();
            return s;
        }

        /// <summary>Clamps the parameters to sane values and builds the pieces, areas and the pour-angle table.</summary>
        public void Build(int arcSegments = 10, int curveSegments = 10)
        {
            Sanitize();
            arcSegments = Mathf.Clamp(arcSegments, 1, 24);
            curveSegments = Mathf.Clamp(curveSegments, 1, 24);
            float hw = innerWidth * 0.5f, hn = neckWidth * 0.5f;
            float r = Mathf.Min(bottomRadius, hw, bodyHeight);
            HalfWidth = hw;
            NeckHalf = hn;
            ShoulderTop = bodyHeight + shoulderHeight;
            MouthY = ShoulderTop + neckHeight;

            // ---- body + shoulders (convex: rounded bottom, straight sides, shoulders curving inward)
            var body = new List<Vector2>(4 * (arcSegments + curveSegments) + 8);
            if (r > 1e-4f)
            {
                Add(body, new Vector2(-hw + r, 0f));
                Add(body, new Vector2(hw - r, 0f));
                var c = new Vector2(hw - r, r);
                for (int i = 1; i <= arcSegments; i++)
                {
                    float a = (-90f + 90f * i / arcSegments) * Mathf.Deg2Rad;
                    Add(body, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            else
            {
                Add(body, new Vector2(-hw, 0f));
                Add(body, new Vector2(hw, 0f));
            }
            Add(body, new Vector2(hw, bodyHeight));
            for (int i = 1; i <= curveSegments; i++)
                Add(body, Bezier(new Vector2(hw, bodyHeight), new Vector2(hw, ShoulderTop), new Vector2(hn, ShoulderTop), i / (float)curveSegments));
            Add(body, new Vector2(-hn, ShoulderTop));
            for (int i = 1; i <= curveSegments; i++)
                Add(body, Bezier(new Vector2(-hn, ShoulderTop), new Vector2(-hw, ShoulderTop), new Vector2(-hw, bodyHeight), i / (float)curveSegments));
            if (r > 1e-4f)
            {
                Add(body, new Vector2(-hw, r));
                var c = new Vector2(-hw + r, r);
                for (int i = 1; i < arcSegments; i++)
                {
                    float a = (180f + 90f * i / arcSegments) * Mathf.Deg2Rad;
                    Add(body, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            // closing duplicate of the first vertex?
            if (body.Count > 1 && (body[body.Count - 1] - body[0]).sqrMagnitude < 1e-10f) body.RemoveAt(body.Count - 1);

            var pieces = new List<Vector2[]>(2) { body.ToArray() };
            if (neckHeight > 1e-4f && hn > 1e-4f)
                pieces.Add(new[] { new Vector2(-hn, ShoulderTop), new Vector2(hn, ShoulderTop), new Vector2(hn, MouthY), new Vector2(-hn, MouthY) });
            Pieces = pieces.ToArray();

            // ---- outline polyline (left mouth corner → down → around → right mouth corner)
            var outline = new List<Vector2>(body.Count + 4);
            Add(outline, new Vector2(-hn, MouthY));
            Add(outline, new Vector2(-hn, ShoulderTop));
            for (int i = 1; i <= curveSegments; i++)
                Add(outline, Bezier(new Vector2(-hn, ShoulderTop), new Vector2(-hw, ShoulderTop), new Vector2(-hw, bodyHeight), i / (float)curveSegments));
            if (r > 1e-4f)
            {
                Add(outline, new Vector2(-hw, r));
                var cl = new Vector2(-hw + r, r);
                for (int i = 1; i <= arcSegments; i++)
                {
                    float a = (180f + 90f * i / arcSegments) * Mathf.Deg2Rad;
                    Add(outline, cl + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
                Add(outline, new Vector2(hw - r, 0f));
                var cr = new Vector2(hw - r, r);
                for (int i = 1; i <= arcSegments; i++)
                {
                    float a = (-90f + 90f * i / arcSegments) * Mathf.Deg2Rad;
                    Add(outline, cr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            else
            {
                Add(outline, new Vector2(-hw, 0f));
                Add(outline, new Vector2(hw, 0f));
            }
            Add(outline, new Vector2(hw, bodyHeight));
            for (int i = 1; i <= curveSegments; i++)
                Add(outline, Bezier(new Vector2(hw, bodyHeight), new Vector2(hw, ShoulderTop), new Vector2(hn, ShoulderTop), i / (float)curveSegments));
            Add(outline, new Vector2(hn, MouthY));
            Outline = outline.ToArray();

            // ---- areas, bounds, pour table
            float area = 0f;
            foreach (var p in Pieces) area += Area(p, p.Length);
            InteriorArea = area;
            _built = true;
            FillArea = Mathf.Max(1e-4f, VolumeBelow(Vector2.up, fillHeight));

            float halfOuter = Mathf.Max(hw + glass, lipWidth * 0.5f);
            GlassLeft = -halfOuter;
            GlassRight = halfOuter;
            GlassBottom = -glass;
            GlassTop = MouthY + Mathf.Max(glass, lipHeight * 0.6f);

            _pourTable = new float[PourTableSize];
            for (int i = 0; i < PourTableSize; i++)
                _pourTable[i] = ComputePourAngleDeg(InteriorArea * i / (PourTableSize - 1));
        }

        void Sanitize()
        {
            innerWidth = Positive(innerWidth, 1f);
            bodyHeight = Positive(bodyHeight, 2.62f);
            bottomRadius = Mathf.Clamp(Finite(bottomRadius, 0.34f), 0f, Mathf.Min(innerWidth * 0.5f, bodyHeight));
            shoulderHeight = Mathf.Max(0f, Finite(shoulderHeight, 0.3f));
            neckWidth = Mathf.Clamp(Positive(neckWidth, 0.56f), 0.05f, innerWidth);
            neckHeight = Mathf.Max(0f, Finite(neckHeight, 0.3f));
            lipWidth = Mathf.Max(neckWidth, Finite(lipWidth, 0.82f));
            lipHeight = Mathf.Max(0f, Finite(lipHeight, 0.15f));
            glass = Mathf.Clamp(Finite(glass, 0.075f), 0f, 0.5f);
            fillHeight = Mathf.Clamp(Positive(fillHeight, 3.22f), 0.1f, bodyHeight + shoulderHeight + neckHeight);
            if (capacity <= 0) capacity = 4;
        }

        static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
        static float Positive(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) || v <= 1e-4f ? fallback : v;

        static void Add(List<Vector2> list, Vector2 p)
        {
            if (list.Count > 0 && (list[list.Count - 1] - p).sqrMagnitude < 1e-10f) return;
            list.Add(p);
        }

        static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        // ------------------------------------------------------------------------------------------- volumes

        /// <summary>Interior area below the line dot(p, n) = level (n need not be normalized, but levels then scale).</summary>
        public float VolumeBelow(Vector2 n, float level)
        {
            if (!_built) return 0f;
            float sum = 0f;
            var pieces = Pieces;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                int k = ClipBelow(p, p.Length, n, level, _bufA);
                if (k >= 3) sum += Area(_bufA, k);
            }
            return sum;
        }

        /// <summary>Lowest / highest value of dot(p, n) over the interior.</summary>
        public void LevelRange(Vector2 n, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            if (!_built) { min = max = 0f; return; }
            var pieces = Pieces;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                for (int k = 0; k < p.Length; k++)
                {
                    float d = p[k].x * n.x + p[k].y * n.y;
                    if (d < min) min = d;
                    if (d > max) max = d;
                }
            }
        }

        /// <summary>Surface level c such that the interior area below dot(p, n) = c equals <paramref name="volume"/>.
        /// <paramref name="lowerHint"/> (a level known to be at or below the answer) narrows the search.</summary>
        public float LevelForVolume(Vector2 n, float volume, float lowerHint = float.MinValue)
        {
            LevelRange(n, out float lo, out float hi);
            if (volume <= 0f) return lo;
            if (volume >= InteriorArea) return hi;
            // f(c) = VolumeBelow(n, c) − volume is monotonic: bracketed false position (Illinois variant), which
            // converges in a handful of evaluations where plain bisection needs ~20.
            float flo = -volume, fhi = InteriorArea - volume;
            if (lowerHint > lo && lowerHint < hi)
            {
                float fh = VolumeBelow(n, lowerHint) - volume;
                if (fh <= 0f) { lo = lowerHint; flo = fh; }
                else { hi = lowerHint; fhi = fh; }
            }
            int side = 0;
            for (int i = 0; i < 40; i++)
            {
                float c = fhi - flo > 1e-12f ? (lo * fhi - hi * flo) / (fhi - flo) : (lo + hi) * 0.5f;
                if (!(c > lo && c < hi)) c = (lo + hi) * 0.5f;
                float fc = VolumeBelow(n, c) - volume;
                if ((fc < 0f ? -fc : fc) < 1e-6f || hi - lo < 1e-5f) return c;
                if (fc < 0f)
                {
                    lo = c;
                    flo = fc;
                    if (side == -1) fhi *= 0.5f;
                    side = -1;
                }
                else
                {
                    hi = c;
                    fhi = fc;
                    if (side == 1) flo *= 0.5f;
                    side = 1;
                }
            }
            return (lo + hi) * 0.5f;
        }

        // ------------------------------------------------------------------------------------------- pouring

        /// <summary>World "up" in the local space of a bottle rotated counter-clockwise by <paramref name="tiltDeg"/>.</summary>
        public static Vector2 UpForTilt(float tiltDeg)
        {
            float a = tiltDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>Interior corner of the mouth on side <paramref name="side"/> (−1 left, +1 right).</summary>
        public Vector2 MouthCorner(int side) => new Vector2(side < 0 ? -NeckHalf : NeckHalf, MouthY);

        /// <summary>
        /// Tilt (degrees, counter-clockwise = pouring over the LEFT lip; use the negative for the right lip) at which
        /// <paramref name="volume"/> of liquid reaches the mouth corner. 0 when the bottle is already brim-full.
        /// Interpolated from a table built once per shape.
        /// </summary>
        public float PourAngleDeg(float volume)
        {
            if (_pourTable == null || _pourTable.Length < 2) return 0f;
            float f = Mathf.Clamp01(volume / Mathf.Max(1e-6f, InteriorArea)) * (_pourTable.Length - 1);
            int i = Mathf.Min((int)f, _pourTable.Length - 2);
            return Mathf.Lerp(_pourTable[i], _pourTable[i + 1], f - i);
        }

        /// <summary>Exact (binary search) version of <see cref="PourAngleDeg"/>.</summary>
        public float ComputePourAngleDeg(float volume)
        {
            if (!_built) return 0f;
            var lip = MouthCorner(-1);
            if (LipVolume(lip, 0f) <= volume) return 0f;
            float lo = 0f, hi = MaxTiltDeg;
            if (LipVolume(lip, hi) > volume) return hi;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (LipVolume(lip, mid) > volume) lo = mid;
                else hi = mid;
            }
            return hi;
        }

        /// <summary>Interior area below the horizontal line through the lip for a bottle tilted by tiltDeg.</summary>
        float LipVolume(Vector2 lip, float tiltDeg)
        {
            var n = UpForTilt(tiltDeg);
            return VolumeBelow(n, lip.x * n.x + lip.y * n.y);
        }

        // ------------------------------------------------------------------------------------------- slicing

        /// <summary>
        /// Part of piece <paramref name="piece"/> with lo ≤ dot(p, n) ≤ hi (lo may be float.NegativeInfinity) written to
        /// <paramref name="dst"/> (≥ MaxVertices long). Returns the vertex count (0 when empty).
        /// </summary>
        public int Slice(int piece, Vector2 n, float lo, float hi, Vector2[] dst)
        {
            if (!_built || piece < 0 || piece >= Pieces.Length) return 0;
            var p = Pieces[piece];
            if (float.IsNegativeInfinity(lo)) return ClipBelow(p, p.Length, n, hi, dst);
            int k = ClipBelow(p, p.Length, n, hi, _bufB);
            if (k < 3) return 0;
            return ClipBelow(_bufB, k, -n, -lo, dst);
        }

        /// <summary>Sutherland–Hodgman clip of a convex polygon to the half-plane dot(p, n) ≤ c.</summary>
        public static int ClipBelow(Vector2[] src, int count, Vector2 n, float c, Vector2[] dst)
        {
            if (count < 3) return 0;
            int k = 0, max = dst.Length;
            Vector2 prev = src[count - 1];
            float dp = prev.x * n.x + prev.y * n.y - c;
            for (int i = 0; i < count; i++)
            {
                Vector2 cur = src[i];
                float dc = cur.x * n.x + cur.y * n.y - c;
                if (dc <= 0f)
                {
                    if (dp > 0f && k < max) dst[k++] = prev + (cur - prev) * (dp / (dp - dc));
                    if (k < max) dst[k++] = cur;
                }
                else if (dp <= 0f && k < max)
                {
                    dst[k++] = prev + (cur - prev) * (dp / (dp - dc));
                }
                prev = cur;
                dp = dc;
            }
            return k;
        }

        /// <summary>Signed area (positive for counter-clockwise polygons).</summary>
        public static float Area(Vector2[] p, int count)
        {
            if (count < 3) return 0f;
            float a = 0f;
            Vector2 prev = p[count - 1];
            for (int i = 0; i < count; i++)
            {
                a += prev.x * p[i].y - p[i].x * prev.y;
                prev = p[i];
            }
            return a * 0.5f;
        }

        /// <summary>Area-weighted centroid accumulation: adds the polygon's area·centroid to <paramref name="moment"/>
        /// and returns its area.</summary>
        public static float AccumulateCentroid(Vector2[] p, int count, ref Vector2 moment)
        {
            if (count < 3) return 0f;
            float a = 0f, cx = 0f, cy = 0f;
            Vector2 prev = p[count - 1];
            for (int i = 0; i < count; i++)
            {
                float cross = prev.x * p[i].y - p[i].x * prev.y;
                a += cross;
                cx += (prev.x + p[i].x) * cross;
                cy += (prev.y + p[i].y) * cross;
                prev = p[i];
            }
            // centroid = (cx, cy) / (3a); area = a / 2 → area·centroid = (cx, cy) / 6
            moment.x += cx / 6f;
            moment.y += cy / 6f;
            return a * 0.5f;
        }
    }
}
