// ============================================================================================================
// Small building blocks of the Worlds screen:
//  * TrailGraphic — a smooth ribbon drawn as ONE triangle strip along a polyline (the winding road of the level map).
//    Its cross-section samples the middle column of the anti-aliased ui_circle sprite, so both edges stay soft at any
//    width without extra images. Rounded caps are added at both ends.
//  * ScrollDragWatcher — tells the screen when the player grabs a ScrollRect (cancels an auto-scroll tween).
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Anti-aliased ribbon along a polyline (local coordinates of its RectTransform). Raycasts off.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrailGraphic : MaskableGraphic
    {
        readonly List<Vector2> _points = new List<Vector2>(256);
        float _width = 60f;
        Sprite _sprite;

        public override Texture mainTexture => _sprite != null && _sprite.texture != null ? _sprite.texture : s_WhiteTexture;

        public static TrailGraphic Create(Transform parent, string name, float width, Color color)
        {
            var rt = UIKit.Rect(name, parent);
            UIKit.Stretch(rt);
            var g = rt.gameObject.AddComponent<TrailGraphic>();
            g._width = Mathf.Max(1f, width);
            g._sprite = UISprites.Circle;
            g.color = color;
            g.raycastTarget = false;
            return g;
        }

        /// <summary>Ribbon width in canvas units.</summary>
        public float Width
        {
            get => _width;
            set
            {
                if (Mathf.Approximately(_width, value)) return;
                _width = Mathf.Max(1f, value);
                SetVerticesDirty();
            }
        }

        /// <summary>Replaces the polyline (copied). Fewer than 2 points draws nothing.</summary>
        public void SetPoints(IList<Vector2> points, int count = -1)
        {
            _points.Clear();
            if (points != null)
            {
                int n = count < 0 ? points.Count : Mathf.Min(count, points.Count);
                for (int i = 0; i < n; i++) _points.Add(points[i]);
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = _points.Count;
            if (n < 2) return;

            // UVs of the middle column of the circle sprite: v runs across the ribbon (soft edge → solid → soft edge).
            float u = 0.5f, v0 = 0f, v1 = 1f;
            float uCap0 = 0f, uCap1 = 1f;
            if (_sprite != null && _sprite.texture != null)
            {
                try
                {
                    // textureRect throws for sprites tightly packed in an atlas: keep the full-texture UVs then.
                    Rect r = _sprite.textureRect;
                    float tw = _sprite.texture.width, th = _sprite.texture.height;
                    u = (r.x + r.width * 0.5f) / tw;
                    v0 = r.y / th;
                    v1 = (r.y + r.height) / th;
                    uCap0 = r.x / tw;
                    uCap1 = (r.x + r.width) / tw;
                }
                catch (Exception) { /* full texture */ }
            }
            Color32 c = color;
            float half = _width * 0.5f;

            for (int i = 0; i < n; i++)
            {
                Vector2 p = _points[i];
                Vector2 dir;
                if (i == 0) dir = _points[1] - p;
                else if (i == n - 1) dir = p - _points[i - 1];
                else dir = (_points[i + 1] - _points[i - 1]);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                dir.Normalize();
                var normal = new Vector2(-dir.y, dir.x) * half;
                vh.AddVert(p + normal, c, new Vector2(u, v1));
                vh.AddVert(p - normal, c, new Vector2(u, v0));
                if (i > 0)
                {
                    int b = i * 2;
                    vh.AddTriangle(b - 2, b, b + 1);
                    vh.AddTriangle(b - 2, b + 1, b - 1);
                }
            }

            // Round caps: a full circle quad at both ends (same texture, the whole sprite).
            AddCap(vh, _points[0], half, c, uCap0, uCap1, v0, v1);
            AddCap(vh, _points[n - 1], half, c, uCap0, uCap1, v0, v1);
        }

        static void AddCap(VertexHelper vh, Vector2 p, float half, Color32 c, float u0, float u1, float v0, float v1)
        {
            int b = vh.currentVertCount;
            vh.AddVert(p + new Vector2(-half, -half), c, new Vector2(u0, v0));
            vh.AddVert(p + new Vector2(-half, half), c, new Vector2(u0, v1));
            vh.AddVert(p + new Vector2(half, half), c, new Vector2(u1, v1));
            vh.AddVert(p + new Vector2(half, -half), c, new Vector2(u1, v0));
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }
    }

    /// <summary>Raises <see cref="onBeginDrag"/> when the player starts dragging the ScrollRect on the same object.</summary>
    [DisallowMultipleComponent]
    public sealed class ScrollDragWatcher : MonoBehaviour, IBeginDragHandler
    {
        public Action onBeginDrag;

        public static ScrollDragWatcher Attach(ScrollRect scroll, Action onBeginDrag)
        {
            if (scroll == null) return null;
            var w = scroll.GetComponent<ScrollDragWatcher>();
            if (w == null) w = scroll.gameObject.AddComponent<ScrollDragWatcher>();
            w.onBeginDrag = onBeginDrag;
            return w;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (onBeginDrag == null) return;
            try { onBeginDrag(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
