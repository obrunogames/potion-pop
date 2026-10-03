// ============================================================================================================
// Board module: shared bottle assets and small data types.
//  * BottleArt: loads Resources/bottle_shape.json once (defaults when missing/broken) into a built BottleShape, the
//    glass sprites (null when the art pipeline has not produced them yet: callers draw procedural fallbacks, never
//    the magenta placeholder) and the GlassFrame that maps the shared glass canvas onto shape units.
//  * GlassFrame: pixels-per-unit + canvas size + origin pixel of shape (0,0) of the bottle_back/front/glow canvas
//    (the json "sprite" block written by Tools/process_art.py; estimated when that block is still zero).
//  * BottleData: a plain copy of one bottle's model state (what a bottle view shows / a snapshot taken when an
//    animation was requested).
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game.Board
{
    /// <summary>How the shared glass sprite canvas maps onto shape units.</summary>
    internal struct GlassFrame
    {
        public float ppu, width, height, originX, originY;
        /// <summary>Values come from the json "sprite" block (false = estimated).</summary>
        public bool fromJson;
        /// <summary>bottle_shadow canvas size and the main-canvas pixel its center goes to (when the json gives them).</summary>
        public float shadowWidth, shadowHeight, shadowCenterX, shadowCenterY;
        public bool hasShadow;

        /// <summary>Shadow size in local pixels (1 shape unit = <paramref name="unitPx"/>).</summary>
        public Vector2 ShadowSizePx(float unitPx) => new Vector2(shadowWidth / ppu * unitPx, shadowHeight / ppu * unitPx);

        /// <summary>Shadow center in local pixels relative to the shape origin.</summary>
        public Vector2 ShadowCenterPx(float unitPx) => new Vector2((shadowCenterX - originX) / ppu * unitPx, (shadowCenterY - originY) / ppu * unitPx);

        /// <summary>Size of the sprite canvas in local pixels when one shape unit is <paramref name="unitPx"/> pixels.</summary>
        public Vector2 SizePx(float unitPx) => new Vector2(width / ppu * unitPx, height / ppu * unitPx);

        /// <summary>Normalized pivot that lands on shape (0,0).</summary>
        public Vector2 Pivot => new Vector2(width > 0f ? originX / width : 0.5f, height > 0f ? originY / height : 0f);
    }

    /// <summary>Copy of one bottle's state (units bottom → top, hidden prefix, stone counter, cork).</summary>
    internal sealed class BottleData
    {
        public readonly List<int> units = new List<int>(4);
        public int hidden;
        public int lockRemaining;
        public bool completed;

        public int Count => units.Count;

        public void CopyFrom(Bottle b)
        {
            units.Clear();
            if (b == null)
            {
                hidden = lockRemaining = 0;
                completed = false;
                return;
            }
            units.AddRange(b.Units);
            hidden = Mathf.Clamp(b.Hidden, 0, units.Count);
            lockRemaining = Mathf.Max(0, b.LockRemaining);
            completed = b.Completed;
        }

        public void CopyFrom(BottleData d)
        {
            units.Clear();
            if (d == null)
            {
                hidden = lockRemaining = 0;
                completed = false;
                return;
            }
            units.AddRange(d.units);
            hidden = d.hidden;
            lockRemaining = d.lockRemaining;
            completed = d.completed;
        }

        /// <summary>Same liquid (units + hidden prefix) and cork as the model bottle (the stone counter is not compared).</summary>
        public bool SameLiquid(Bottle b)
        {
            if (b == null) return units.Count == 0;
            if (b.Units.Count != units.Count || Mathf.Clamp(b.Hidden, 0, units.Count) != hidden || b.Completed != completed) return false;
            for (int i = 0; i < units.Count; i++) if (units[i] != b.Units[i]) return false;
            return true;
        }

        public static BottleData Of(Bottle b)
        {
            var d = new BottleData();
            d.CopyFrom(b);
            return d;
        }
    }

    /// <summary>Lazily loaded bottle shape + glass sprites (shared by every bottle view).</summary>
    internal static class BottleArt
    {
#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable]
        sealed class SpriteJson
        {
            public float pixelsPerUnit;
            public float width;
            public float height;
            public float originX;
            public float originY;
            public float shadowWidth;
            public float shadowHeight;
            public float shadowCenterX;
            public float shadowCenterY;
        }

        [Serializable]
        sealed class ShapeJson
        {
            public float innerWidth = 1f;
            public float bottomRadius = 0.34f;
            public float bodyHeight = 2.62f;
            public float shoulderHeight = 0.30f;
            public float neckWidth = 0.56f;
            public float neckHeight = 0.30f;
            public float lipWidth = 0.82f;
            public float lipHeight = 0.15f;
            public float glass = 0.075f;
            public float fillHeight = 2.48f;
            public int capacity = 4;
            public SpriteJson sprite;
        }
#pragma warning restore 0649

        const float DefaultPpu = 160f;
        const float DefaultPad = 8f;

        static bool _loaded;
        static BottleShape _shape;
        static GlassFrame _frame;
        static Sprite _back, _front, _glow, _shadow, _cork, _stone, _rack, _rackSliced;
        static bool _rackSlicedTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _loaded = false;
            _shape = null;
            _frame = default;
            _back = _front = _glow = _shadow = _cork = _stone = _rack = _rackSliced = null;
            _rackSlicedTried = false;
        }

        public static BottleShape Shape { get { EnsureLoaded(); return _shape; } }
        public static GlassFrame Frame { get { EnsureLoaded(); return _frame; } }
        public static Sprite Back { get { EnsureLoaded(); return _back; } }
        public static Sprite Front { get { EnsureLoaded(); return _front; } }
        public static Sprite Glow { get { EnsureLoaded(); return _glow; } }
        public static Sprite Shadow { get { EnsureLoaded(); return _shadow; } }
        public static Sprite Cork { get { EnsureLoaded(); return _cork; } }
        public static Sprite Stone { get { EnsureLoaded(); return _stone; } }
        public static Sprite Rack { get { EnsureLoaded(); return _rack; } }

        /// <summary>Fraction of the rack width kept unstretched at each end (rounded end, bracket and stars).</summary>
        public const float RackCapFraction = 0.235f;

        /// <summary>
        /// bottle_rack as a horizontally 9-sliced sprite (only the plain wood in the middle stretches, so the brackets
        /// and stars keep their shape on long rows). Built once at runtime from the imported sprite; null when missing.
        /// </summary>
        public static Sprite RackSliced
        {
            get
            {
                EnsureLoaded();
                if (_rackSlicedTried) return _rackSliced;
                _rackSlicedTried = true;
                if (_rack == null) return null;
                try
                {
                    Rect r = _rack.rect;
                    try { r = _rack.textureRect; }
                    catch (Exception) { /* tight-packed sprite: use its rect */ }
                    float cap = Mathf.Round(r.width * RackCapFraction);
                    _rackSliced = Sprite.Create(_rack.texture, r, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                        new Vector4(cap, 0f, cap, 0f));
                    _rackSliced.name = "bottle_rack (sliced)";
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Board] Could not slice bottle_rack: " + e.Message);
                    _rackSliced = null;
                }
                return _rackSliced;
            }
        }

        /// <summary>Real art sprite or null (never the magenta placeholder).</summary>
        public static Sprite Optional(string name) => UISprites.Exists(name) ? UISprites.Get(name) : null;

        /// <summary>Width / height of a sprite (art_index metadata first, then the sprite rect, else the fallback).</summary>
        public static float Aspect(string name, float fallback)
        {
            var info = Art.Info(name);
            if (info != null && info.width > 0 && info.height > 0) return info.width / (float)info.height;
            var sp = Optional(name);
            return sp != null && sp.rect.height > 0f ? sp.rect.width / sp.rect.height : fallback;
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            ShapeJson json = null;
            try
            {
                var asset = Resources.Load<TextAsset>("bottle_shape");
                if (asset != null && !string.IsNullOrEmpty(asset.text)) json = JsonUtility.FromJson<ShapeJson>(asset.text);
                else Debug.LogWarning("[Board] Resources/bottle_shape.json not found: using the default bottle.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Board] Could not read bottle_shape.json (" + e.Message + "): using the default bottle.");
                json = null;
            }

            var s = new BottleShape();
            if (json != null)
            {
                s.innerWidth = json.innerWidth;
                s.bottomRadius = json.bottomRadius;
                s.bodyHeight = json.bodyHeight;
                s.shoulderHeight = json.shoulderHeight;
                s.neckWidth = json.neckWidth;
                s.neckHeight = json.neckHeight;
                s.lipWidth = json.lipWidth;
                s.lipHeight = json.lipHeight;
                s.glass = json.glass;
                s.fillHeight = json.fillHeight;
                s.capacity = json.capacity;
            }
            try { s.Build(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                s = BottleShape.CreateDefault();
            }
            _shape = s;

            _back = Optional("bottle_back");
            _front = Optional("bottle_front");
            _glow = Optional("bottle_glow");
            _shadow = Optional("bottle_shadow");
            _cork = Optional("cork");
            _stone = Optional("stone_wrap");
            _rack = Optional("bottle_rack");
            _frame = ComputeFrame(json != null ? json.sprite : null, s);
        }

        static GlassFrame ComputeFrame(SpriteJson sj, BottleShape s)
        {
            float ppu = sj != null && sj.pixelsPerUnit > 0f ? sj.pixelsPerUnit : DefaultPpu;
            if (sj != null && sj.width > 0f && sj.height > 0f)
            {
                var f = new GlassFrame { ppu = ppu, width = sj.width, height = sj.height, originX = sj.originX, originY = sj.originY, fromJson = true };
                if (sj.shadowWidth > 0f && sj.shadowHeight > 0f)
                {
                    f.hasShadow = true;
                    f.shadowWidth = sj.shadowWidth;
                    f.shadowHeight = sj.shadowHeight;
                    f.shadowCenterX = sj.shadowCenterX;
                    f.shadowCenterY = sj.shadowCenterY;
                }
                return f;
            }

            // The json block is still empty: assume the glass is centered with the same padding left/right/bottom.
            Vector2 size = CanvasSize("bottle_front");
            if (size.x <= 0f) size = CanvasSize("bottle_back");
            if (size.x > 0f && size.y > 0f)
            {
                float pad = Mathf.Max(0f, (size.x - s.GlassWidth * ppu) * 0.5f);
                return new GlassFrame { ppu = ppu, width = size.x, height = size.y, originX = size.x * 0.5f, originY = pad - s.GlassBottom * ppu };
            }
            float w = s.GlassWidth * ppu + 2f * DefaultPad, h = s.GlassHeight * ppu + 2f * DefaultPad;
            return new GlassFrame { ppu = ppu, width = w, height = h, originX = w * 0.5f, originY = DefaultPad - s.GlassBottom * ppu };
        }

        /// <summary>Processed PNG size of a sprite (art_index first: the json ppu refers to those pixels).</summary>
        static Vector2 CanvasSize(string name)
        {
            var info = Art.Info(name);
            if (info != null && info.width > 0 && info.height > 0) return new Vector2(info.width, info.height);
            var sp = Optional(name);
            return sp != null ? sp.rect.size : Vector2.zero;
        }
    }
}
