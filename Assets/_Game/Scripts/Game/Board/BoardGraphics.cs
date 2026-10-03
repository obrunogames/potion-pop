// ============================================================================================================
// Board module: small helper graphics and input relays.
//  * StreamGraphic: the falling stream of a pour — a glossy strip (dark edges, light core) from the source's lip to
//    the target's liquid surface, tapering and wobbling slightly. Lives inside the TARGET bottle between its liquid
//    and its front glass, so the part inside the target reads as "inside the glass".
//  * GlassGraphic: procedural stand-in for bottle_back / bottle_front while the art pipeline has not produced them:
//    translucent interior + glass wall strip (outline offset outward by the glass thickness) + highlight + lip.
//  * BottleTap / BoardTap: pointer-down relays (instant response) from the bottle hit areas and the board
//    background to the BoardView.
// ============================================================================================================
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.Game.Board
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class StreamGraphic : MaskableGraphic
    {
        const int Segments = 12;
        static readonly float[] Columns = { -1f, -0.35f, 0.2f, 1f };

        Vector2 _top, _bottom;
        float _width;
        Color _mid, _light, _dark;
        float _phase;
        bool _on;

        public bool IsOn => _on;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>Shows the stream between two local points (pixels) with the given width and liquid colors.</summary>
        public void Set(Vector2 top, Vector2 bottom, float width, Color mid, Color light, Color dark, float phase)
        {
            _top = top;
            _bottom = bottom;
            _width = width;
            _mid = mid;
            _light = light;
            _dark = dark;
            _phase = phase;
            _on = true;
            SetVerticesDirty();
        }

        public void Hide()
        {
            if (!_on) return;
            _on = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_on || _width <= 0.2f) return;
            Vector2 axis = _bottom - _top;
            float len = axis.magnitude;
            if (len < 0.5f) return;
            Vector2 dir = axis / len;
            var perp = new Vector2(-dir.y, dir.x);
            Color tint = color;
            Color edgeL = Color.Lerp(_mid, _dark, 0.45f) * tint, core = _light * tint, body = _mid * tint, edgeR = Color.Lerp(_mid, _dark, 0.6f) * tint;
            for (int s = 0; s <= Segments; s++)
            {
                float v = s / (float)Segments;
                float half = _width * 0.5f * (1f - 0.24f * v);
                float wob = Mathf.Sin(_phase * 13f + v * 7.5f) * _width * 0.14f * v;
                Vector2 c = _top + axis * v + perp * wob;
                vh.AddVert(c + perp * (Columns[0] * half), edgeL, Vector4.zero);
                vh.AddVert(c + perp * (Columns[1] * half), core, Vector4.zero);
                vh.AddVert(c + perp * (Columns[2] * half), body, Vector4.zero);
                vh.AddVert(c + perp * (Columns[3] * half), edgeR, Vector4.zero);
            }
            for (int s = 0; s < Segments; s++)
                for (int j = 0; j < 3; j++)
                {
                    int a = s * 4 + j;
                    vh.AddTriangle(a, a + 1, a + 5);
                    vh.AddTriangle(a, a + 5, a + 4);
                }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class GlassGraphic : MaskableGraphic
    {
        BottleShape _shape;
        float _unit = 100f;
        bool _front;

        public void Setup(BottleShape shape, float unitPx, bool front)
        {
            _shape = shape;
            _unit = unitPx;
            _front = front;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_shape == null || !_shape.IsBuilt) return;
            Color tint = color;
            if (!_front)
            {
                // translucent interior, a bit brighter toward the top
                var pieces = _shape.Pieces;
                for (int p = 0; p < pieces.Length; p++)
                {
                    var poly = pieces[p];
                    int baseIndex = vh.currentVertCount;
                    for (int i = 0; i < poly.Length; i++)
                    {
                        float t = Mathf.Clamp01(poly[i].y / Mathf.Max(0.01f, _shape.MouthY));
                        vh.AddVert(P(poly[i]), new Color(1f, 1f, 1f, Mathf.Lerp(0.10f, 0.17f, t)) * tint, Vector4.zero);
                    }
                    for (int i = 1; i < poly.Length - 1; i++) vh.AddTriangle(baseIndex, baseIndex + i, baseIndex + i + 1);
                }
                Wall(vh, new Color(1f, 1f, 1f, 0.16f) * tint, new Color(1f, 1f, 1f, 0.26f) * tint);
                return;
            }
            Wall(vh, new Color(1f, 1f, 1f, 0.28f) * tint, new Color(1f, 1f, 1f, 0.72f) * tint);
            // vertical highlight stripe near the left wall (fades at both ends)
            float x0 = -_shape.HalfWidth + 0.1f, x1 = x0 + 0.1f;
            float y0 = _shape.bottomRadius * 0.9f, y1 = _shape.bodyHeight - 0.12f, ym = (y0 + y1) * 0.5f;
            var clear = new Color(1f, 1f, 1f, 0f) * tint;
            var shine = new Color(1f, 1f, 1f, 0.34f) * tint;
            int b = vh.currentVertCount;
            vh.AddVert(P(new Vector2(x0, y0)), clear, Vector4.zero);
            vh.AddVert(P(new Vector2(x1, y0)), clear, Vector4.zero);
            vh.AddVert(P(new Vector2(x0, ym)), shine, Vector4.zero);
            vh.AddVert(P(new Vector2(x1, ym)), shine, Vector4.zero);
            vh.AddVert(P(new Vector2(x0, y1)), clear, Vector4.zero);
            vh.AddVert(P(new Vector2(x1, y1)), clear, Vector4.zero);
            vh.AddTriangle(b, b + 2, b + 3);
            vh.AddTriangle(b, b + 3, b + 1);
            vh.AddTriangle(b + 2, b + 4, b + 5);
            vh.AddTriangle(b + 2, b + 5, b + 3);
            // lip band at the mouth
            float hl = Mathf.Max(_shape.lipWidth * 0.5f, _shape.NeckHalf + _shape.glass);
            float ly0 = _shape.MouthY - _shape.lipHeight * 0.5f, ly1 = _shape.MouthY + _shape.lipHeight * 0.5f;
            var lipTop = new Color(1f, 1f, 1f, 0.8f) * tint;
            var lipBottom = new Color(0.86f, 0.84f, 0.95f, 0.65f) * tint;
            b = vh.currentVertCount;
            vh.AddVert(P(new Vector2(-hl, ly0)), lipBottom, Vector4.zero);
            vh.AddVert(P(new Vector2(hl, ly0)), lipBottom, Vector4.zero);
            vh.AddVert(P(new Vector2(hl, ly1)), lipTop, Vector4.zero);
            vh.AddVert(P(new Vector2(-hl, ly1)), lipTop, Vector4.zero);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }

        Vector3 P(Vector2 v) => new Vector3(v.x * _unit, v.y * _unit, 0f);

        /// <summary>Glass wall: strip between the interior outline and its outward offset by the glass thickness.</summary>
        void Wall(VertexHelper vh, Color inner, Color outer)
        {
            var o = _shape.Outline;
            if (o == null || o.Length < 2) return;
            float g = Mathf.Max(0.02f, _shape.glass);
            int baseIndex = vh.currentVertCount;
            for (int i = 0; i < o.Length; i++)
            {
                Vector2 nPrev = i > 0 ? RightNormal(o[i] - o[i - 1]) : Vector2.zero;
                Vector2 nNext = i < o.Length - 1 ? RightNormal(o[i + 1] - o[i]) : Vector2.zero;
                Vector2 m = nPrev + nNext;
                if (m.sqrMagnitude < 1e-8f) m = nPrev.sqrMagnitude > 0f ? nPrev : nNext;
                m.Normalize();
                Vector2 refN = nPrev.sqrMagnitude > 0f ? nPrev : nNext;
                float cos = Mathf.Max(0.35f, Vector2.Dot(m, refN));
                vh.AddVert(P(o[i]), inner, Vector4.zero);
                vh.AddVert(P(o[i] + m * (g / cos)), outer, Vector4.zero);
            }
            for (int i = 0; i < o.Length - 1; i++)
            {
                int a = baseIndex + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        /// <summary>Outward normal of an outline edge (the interior lies on the left of the walking direction).</summary>
        static Vector2 RightNormal(Vector2 e)
        {
            var n = new Vector2(e.y, -e.x);
            float m = n.magnitude;
            return m > 1e-8f ? n / m : Vector2.zero;
        }
    }

    /// <summary>Pointer-down relay of a bottle's hit area (instant response, generous column-sized target).</summary>
    internal sealed class BottleTap : MonoBehaviour, IPointerDownHandler
    {
        public BoardView board;
        public int index;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || board == null) return;
            board.HandleBottleTap(index);
        }
    }

    /// <summary>Pointer-down relay of the board background (taps between bottles put the lifted one down).</summary>
    internal sealed class BoardTap : MonoBehaviour, IPointerDownHandler
    {
        public BoardView board;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || board == null) return;
            board.HandleBackgroundTap();
        }
    }
}
