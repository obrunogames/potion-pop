// ============================================================================================================
// Board module: the liquid inside one bottle, a custom uGUI mesh (no texture, vertex colors only).
//
// The layers are stacked bottom → top with fractional amounts (units). Every rebuild:
//  1. n = world "up" expressed in this graphic's local space (from its world matrix, so the surface stays
//     HORIZONTAL on screen whatever the bottle's tilt/scale), optionally rotated by a damped-sine wobble;
//  2. each layer's top level c_i solves VolumeBelow(n, c_i) = Σ amounts · unit volume (BottleShape.LevelForVolume);
//  3. each convex piece of the interior is clipped between consecutive levels (sub-bands: body with a Dark → Color
//     gradient, a thin lighter top band, a 1-2 px feathered rim above the free surface) and fan-triangulated. Colors
//     are linear in dot(p, n) inside each sub-band, so per-vertex colors reproduce the gradient exactly.
// Hidden units are drawn in Liquids.Mystery (their "?" glyphs are TMP children positioned by BottleView from
// TryGetLayerCenter). The mesh is rebuilt only when something changed (dirty flag, orientation change, wobble or
// shine running) — idle bottles cost one cheap orientation check per frame and no allocations.
// Local space: 1 shape unit = unitPx pixels, local (0,0) = shape (0,0) (the RectTransform pivot is placed there).
// ============================================================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game.Board
{
    /// <summary>One stacked layer of liquid as drawn by <see cref="LiquidGraphic"/>.</summary>
    internal struct LiquidLayer
    {
        public int color;
        /// <summary>Units of liquid (fractional while a pour animates).</summary>
        public float amount;
        /// <summary>A hidden ("?") unit (drawn misty, with a glyph).</summary>
        public bool hidden;
        /// <summary>Hidden → real color transition (0 = mystery, 1 = color).</summary>
        public float reveal;
        /// <summary>0..1 extra brightness (wand glow).</summary>
        public float glow;
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LiquidGraphic : MaskableGraphic
    {
        /// <summary>Thickest top band (shape units).</summary>
        const float BandMax = 0.075f;
        /// <summary>Anti-aliasing rim above the free surface (shape units, ~1.5 screen px at usual sizes).</summary>
        const float Feather = 0.014f;
        const float WobbleFreq = 13.5f;     // rad/s (~2.1 Hz)
        const float WobbleDecay = 3.2f;     // 1/s
        const float ShineWidth = 0.16f;
        const float ShineAlpha = 0.34f;
        static readonly Vector2 ShineDir = new Vector2(0.55f, 0.835f);
        static readonly Color FlashTint = new Color(0.97f, 0.95f, 1f, 1f);

        BottleShape _shape;
        float _unit = 100f;
        float _unitVolume = 0.6f;
        readonly List<LiquidLayer> _layers = new List<LiquidLayer>(6);

        // cached mesh
        Vector3[] _pos = new Vector3[512];
        Color[] _col = new Color[512];
        int[] _idx = new int[1536];
        int _vn, _in;
        bool _dirty = true, _built;

        // orientation
        Vector2 _rawUp = Vector2.up, _drawUp = Vector2.up;
        float _wobAmp, _wobT, _wobPhi;
        float _shineT = -1f, _shineDur = 1f;
        float _flash;

        // results of the last rebuild (shape units)
        float[] _top = new float[8];
        float[] _thick = new float[8];
        Vector2[] _center = new Vector2[8];
        float _surface;
        bool _hasLiquid;

        static readonly Vector2[] _buf = new Vector2[BottleShape.MaxVertices];
        static readonly Vector2[] _bufS = new Vector2[BottleShape.MaxVertices];
        static readonly Vector2[] _bufT = new Vector2[BottleShape.MaxVertices];

        // ------------------------------------------------------------------------------------------- setup

        public void Setup(BottleShape shape, float unitPx, int capacity)
        {
            _shape = shape;
            _unit = Mathf.Max(1f, unitPx);
            float fill = shape != null ? shape.FillArea : 2.4f;
            _unitVolume = fill / Mathf.Max(1, capacity);
            raycastTarget = false;
            _dirty = true;
            SetVerticesDirty();
        }

        /// <summary>Incremented whenever the layer list is rebuilt (tweens holding layer indices check it).</summary>
        public int Version { get; private set; }

        /// <summary>Live layer list (bottom → top). Call MarkDirty after editing entries.</summary>
        public List<LiquidLayer> Layers => _layers;

        public void MarkDirty() => _dirty = true;

        /// <summary>Replaces the layers from model units: hidden units one layer each, visible same-color runs merged.</summary>
        public void SetUnits(List<int> units, int hidden)
        {
            _layers.Clear();
            if (units != null)
                for (int i = 0; i < units.Count; i++)
                {
                    int c = units[i];
                    bool h = i < hidden;
                    int last = _layers.Count - 1;
                    if (!h && last >= 0 && !_layers[last].hidden && _layers[last].color == c)
                    {
                        var top = _layers[last];
                        top.amount += 1f;
                        _layers[last] = top;
                        continue;
                    }
                    _layers.Add(new LiquidLayer { color = c, amount = 1f, hidden = h, reveal = h ? 0f : 1f });
                }
            Version++;
            _dirty = true;
        }

        /// <summary>Adds a layer on top (e.g. the incoming color of a pour) and returns its index.</summary>
        public int AddLayer(LiquidLayer layer)
        {
            _layers.Add(layer);
            Version++;
            _dirty = true;
            return _layers.Count - 1;
        }

        public void SetLayer(int index, LiquidLayer layer)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index] = layer;
            _dirty = true;
        }

        public float TotalAmount
        {
            get
            {
                float a = 0f;
                for (int i = 0; i < _layers.Count; i++) a += Mathf.Max(0f, _layers[i].amount);
                return a;
            }
        }

        /// <summary>Volume (shape units²) of one unit of liquid.</summary>
        public float UnitVolume => _unitVolume;

        /// <summary>Whitening of the whole liquid (shuffle swirl), 0..1.</summary>
        public float Flash
        {
            get => _flash;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Abs(value - _flash) < 1e-4f) return;
                _flash = value;
                _dirty = true;
            }
        }

        /// <summary>Starts (or boosts) the surface wobble: damped sine of the surface angle, in degrees.</summary>
        public void Wobble(float degrees)
        {
            float current = _wobAmp * Mathf.Exp(-_wobT * WobbleDecay);
            if (degrees <= current) return;
            _wobAmp = degrees;
            _wobT = 0f;
            _dirty = true;
        }

        /// <summary>A soft diagonal light sweep across the liquid (idle shimmer of completed bottles).</summary>
        public void Shine(float duration)
        {
            _shineDur = Mathf.Max(0.1f, duration);
            _shineT = 0f;
            _dirty = true;
        }

        public bool IsWobbling => _wobAmp > 0f;

        // ------------------------------------------------------------------------------------------- queries

        /// <summary>Level of the free surface along the current up (shape units; the interior bottom when empty).</summary>
        public float SurfaceLevel => _surface;

        /// <summary>Local y (pixels) of the free surface at local x (pixels).</summary>
        public float SurfaceYAt(float xPx)
        {
            if (!_built) Rebuild();
            var n = _drawUp;
            if (n.y < 0.2f) return _surface * _unit;
            float x = xPx / _unit;
            return (_surface - x * n.x) / n.y * _unit;
        }

        /// <summary>Area-weighted center (local pixels) and thickness along up (pixels) of a layer, from the last rebuild.</summary>
        public bool TryGetLayerCenter(int index, out Vector2 centerPx, out float thicknessPx)
        {
            centerPx = Vector2.zero;
            thicknessPx = 0f;
            if (!_built || index < 0 || index >= _layers.Count || index >= _thick.Length || _thick[index] <= 1e-5f) return false;
            centerPx = _center[index] * _unit;
            thicknessPx = _thick[index] * _unit;
            return true;
        }

        /// <summary>Local position (pixels) of the center of unit slot <paramref name="unitIndex"/> (0 = bottom) when the
        /// bottle stands upright (orb / sparkle origins).</summary>
        public Vector2 UnitCenterUpright(float unitIndex)
        {
            if (_shape == null || !_shape.IsBuilt) return new Vector2(0f, (unitIndex + 0.5f) * 0.6f * _unit);
            float y = _shape.LevelForVolume(Vector2.up, (unitIndex + 0.5f) * _unitVolume);
            return new Vector2(0f, y * _unit);
        }

        // ------------------------------------------------------------------------------------------- per frame

        /// <summary>Checks orientation / wobble / shine and rebuilds the geometry when needed. Returns true when rebuilt.</summary>
        public bool Refresh(float dt)
        {
            if (_shape == null) return false;
            if (TryGetUp(out var up) && (up - _rawUp).sqrMagnitude > 1e-9f)
            {
                _rawUp = up;
                _dirty = true;
            }
            float phi = 0f;
            if (_wobAmp > 0f)
            {
                _wobT += dt;
                float amp = _wobAmp * Mathf.Exp(-_wobT * WobbleDecay);
                if (amp < 0.06f) _wobAmp = 0f;
                else phi = amp * Mathf.Sin(_wobT * WobbleFreq);
                _dirty = true;
            }
            if (_shineT >= 0f)
            {
                _shineT += dt;
                if (_shineT >= _shineDur) _shineT = -1f;
                _dirty = true;
            }
            if (!_dirty) return false;
            _wobPhi = phi;
            Rebuild();
            SetVerticesDirty();
            return true;
        }

        bool TryGetUp(out Vector2 up)
        {
            var t = transform;
            Vector3 r = t.InverseTransformVector(Vector3.right);
            Vector3 u = t.InverseTransformVector(Vector3.up);
            var n = new Vector2(-r.y, r.x);
            if (n.x * u.x + n.y * u.y < 0f) n = -n;
            float m = n.magnitude;
            if (!(m > 1e-8f) || float.IsInfinity(m))
            {
                up = _rawUp;
                return false;
            }
            up = n / m;
            return true;
        }

        // ------------------------------------------------------------------------------------------- mesh

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_shape == null) return;
            if (!_built || _dirty) Rebuild();
            Color tint = color;
            for (int i = 0; i < _vn; i++) vh.AddVert(_pos[i], _col[i] * tint, Vector4.zero);
            for (int i = 0; i + 2 < _in; i += 3) vh.AddTriangle(_idx[i], _idx[i + 1], _idx[i + 2]);
        }

        void Rebuild()
        {
            _built = true;
            _dirty = false;
            _vn = _in = 0;
            int count = _layers.Count;
            EnsureOutputs(count);
            if (_shape == null || !_shape.IsBuilt) return;

            float c = Mathf.Cos(_wobPhi * Mathf.Deg2Rad), s = Mathf.Sin(_wobPhi * Mathf.Deg2Rad);
            var n = new Vector2(_rawUp.x * c - _rawUp.y * s, _rawUp.x * s + _rawUp.y * c);
            _drawUp = n;
            _shape.LevelRange(n, out float bottom, out _);

            float cum = 0f, lo = float.NegativeInfinity, hint = float.MinValue;
            int topLayer = -1;
            _surface = bottom;
            Color topLight = Color.white;
            for (int i = 0; i < count; i++)
            {
                var l = _layers[i];
                _thick[i] = 0f;
                _center[i] = Vector2.zero;
                _top[i] = float.IsNegativeInfinity(lo) ? bottom : lo;
                float a = l.amount;
                if (!(a > 1e-4f)) continue;
                cum += a * _unitVolume;
                float hi = _shape.LevelForVolume(n, cum, hint);
                hint = hi;
                float gLo = float.IsNegativeInfinity(lo) ? bottom : lo;
                if (hi - gLo <= 1e-5f) continue;
                _top[i] = hi;

                LayerColors(l, out var dark, out var mid, out var light);
                float band = Mathf.Min(BandMax, (hi - gLo) * 0.35f);
                float mainHi = hi - band;
                var moment = Vector2.zero;
                float area = EmitBand(n, lo, mainHi, gLo, mainHi, dark, mid, ref moment);
                area += EmitBand(n, mainHi, hi, mainHi, hi, Color.Lerp(mid, light, 0.55f), light, ref moment);
                _thick[i] = hi - gLo;
                _center[i] = area > 1e-7f ? moment / area : new Vector2(0f, (gLo + hi) * 0.5f);
                topLayer = i;
                topLight = light;
                _surface = hi;
                lo = hi;
            }
            _hasLiquid = topLayer >= 0;
            if (_hasLiquid)
            {
                var clear = topLight;
                clear.a = 0f;
                var unused = Vector2.zero;
                EmitBand(n, _surface, _surface + Feather, _surface, _surface + Feather, topLight, clear, ref unused);
                if (_shineT >= 0f) EmitShine(n);
            }
        }

        /// <summary>Emits the part of every piece with lo ≤ dot(p, n) ≤ hi; color goes c0 → c1 between gLo and gHi.
        /// Returns the emitted area and accumulates area·centroid into <paramref name="moment"/>.</summary>
        float EmitBand(Vector2 n, float lo, float hi, float gLo, float gHi, Color c0, Color c1, ref Vector2 moment)
        {
            if (hi <= lo) return 0f;
            float area = 0f;
            var pieces = _shape.Pieces;
            for (int p = 0; p < pieces.Length; p++)
            {
                int k = _shape.Slice(p, n, lo, hi, _buf);
                if (k < 3) continue;
                area += BottleShape.AccumulateCentroid(_buf, k, ref moment);
                EmitPolygon(_buf, k, n, gLo, gHi, c0, c1);
            }
            return area;
        }

        void EmitPolygon(Vector2[] poly, int k, Vector2 n, float gLo, float gHi, Color c0, Color c1)
        {
            EnsureCapacity(_vn + k, _in + (k - 2) * 3);
            int baseIndex = _vn;
            float range = gHi - gLo;
            float inv = range > 1e-6f ? 1f / range : 0f;
            for (int j = 0; j < k; j++)
            {
                var v = poly[j];
                float t = Mathf.Clamp01((v.x * n.x + v.y * n.y - gLo) * inv);
                _pos[_vn] = new Vector3(v.x * _unit, v.y * _unit, 0f);
                _col[_vn] = Color.LerpUnclamped(c0, c1, t);
                _vn++;
            }
            for (int j = 1; j < k - 1; j++)
            {
                _idx[_in++] = baseIndex;
                _idx[_in++] = baseIndex + j;
                _idx[_in++] = baseIndex + j + 1;
            }
        }

        /// <summary>Diagonal light stripe sweeping across the liquid (alpha ramps 0 → ShineAlpha → 0 across it).</summary>
        void EmitShine(Vector2 n)
        {
            var m = ShineDir;
            _shape.LevelRange(m, out float mLo, out float mHi);
            float p = Mathf.Clamp01(_shineT / _shineDur);
            float sc = Mathf.Lerp(mLo - ShineWidth, mHi + ShineWidth, p);
            var edge = new Color(1f, 1f, 1f, 0f);
            var core = new Color(1f, 1f, 1f, ShineAlpha);
            var pieces = _shape.Pieces;
            for (int i = 0; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                int k = BottleShape.ClipBelow(piece, piece.Length, n, _surface, _bufS);
                if (k < 3) continue;
                // lower half of the stripe: sc - w .. sc
                int a = BottleShape.ClipBelow(_bufS, k, m, sc, _bufT);
                if (a >= 3)
                {
                    a = BottleShape.ClipBelow(_bufT, a, -m, -(sc - ShineWidth), _buf);
                    if (a >= 3) EmitPolygon(_buf, a, m, sc - ShineWidth, sc, edge, core);
                }
                // upper half: sc .. sc + w
                int b = BottleShape.ClipBelow(_bufS, k, -m, -sc, _bufT);
                if (b >= 3)
                {
                    b = BottleShape.ClipBelow(_bufT, b, m, sc + ShineWidth, _buf);
                    if (b >= 3) EmitPolygon(_buf, b, m, sc, sc + ShineWidth, core, edge);
                }
            }
        }

        void LayerColors(in LiquidLayer l, out Color dark, out Color mid, out Color light)
        {
            mid = Liquids.Color(l.color);
            dark = Liquids.Dark(l.color);
            light = Liquids.Light(l.color);
            if (l.hidden && l.reveal < 1f)
            {
                var mc = Liquids.Mystery;
                var md = new Color(mc.r * 0.78f, mc.g * 0.78f, mc.b * 0.78f, 1f);
                var ml = Color.Lerp(mc, Color.white, 0.45f);
                float t = Mathf.Clamp01(l.reveal);
                mid = Color.Lerp(mc, mid, t);
                dark = Color.Lerp(md, dark, t);
                light = Color.Lerp(ml, light, t);
            }
            float g = Mathf.Clamp01(l.glow) * 0.6f;
            if (g > 0f)
            {
                mid = Color.Lerp(mid, Color.white, g);
                dark = Color.Lerp(dark, Color.white, g * 0.8f);
                light = Color.Lerp(light, Color.white, g);
            }
            if (_flash > 0f)
            {
                mid = Color.Lerp(mid, FlashTint, _flash);
                dark = Color.Lerp(dark, FlashTint, _flash * 0.85f);
                light = Color.Lerp(light, Color.white, _flash);
            }
        }

        void EnsureOutputs(int count)
        {
            if (_top.Length >= count) return;
            int size = Mathf.Max(count, _top.Length * 2);
            System.Array.Resize(ref _top, size);
            System.Array.Resize(ref _thick, size);
            System.Array.Resize(ref _center, size);
        }

        void EnsureCapacity(int verts, int indices)
        {
            if (verts > _pos.Length)
            {
                int size = Mathf.Max(verts, _pos.Length * 2);
                System.Array.Resize(ref _pos, size);
                System.Array.Resize(ref _col, size);
            }
            if (indices > _idx.Length) System.Array.Resize(ref _idx, Mathf.Max(indices, _idx.Length * 2));
        }
    }
}
