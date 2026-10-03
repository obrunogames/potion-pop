// ============================================================================================================
// Board view (owner: Board agent): draws and animates the bottles of a BoardState. CONTRACT: the public members of
// this file are what the gameplay flow (GameSession) is written against — their signatures never change.
//
// Responsibilities of the view:
//  * draws the bottles of a BoardState inside its RectTransform (layout in rows, scaled to fit), glass sprites from
//    bottle_shape.json + liquid drawn as a mesh with a horizontal surface even while the bottle tilts;
//  * handles taps: first tap lifts a selectable bottle, a tap on another bottle asks the session to pour
//    (OnPourRequested), a tap on the lifted bottle puts it back; taps on busy (animating) bottles are ignored;
//  * animates whatever the session tells it (PlayPour, PlayUndo, boosters...). Several pours may animate at once
//    on different bottles: the model (BoardState) is already updated when Play* is called.
// The view never changes the BoardState itself.
//
// Model ↔ view: every bottle view keeps its own copy of what it shows (BottleView.shown). A Play* call snapshots the
// model bottles it touches at call time and runs as a job (BoardView.Jobs.cs) that owns those bottles ("busy") while
// it animates from the shown state to the snapshot; jobs on the same bottle run in call order, jobs on different
// bottles run concurrently. Stone counters use "latest request wins" (sequence numbers) because completions from
// concurrent pours tick the same stones. When the last job ends the view reconciles with the model and raises OnIdle.
//
// Files: BoardView.cs (API, lifecycle, build, layout, positions) · BoardView.Jobs.cs (scheduler) · BoardView.Input.cs
// (taps, selection, invalid / refuse feedback) · BoardView.Pour.cs (pour + undo) · BoardView.Boosters.cs (extra bottle,
// wand, shuffle, crystal ball, stone break) · BoardView.Modes.cs (intro, win, hint, idle shimmer) · BottleView.cs ·
// LiquidGraphic.cs · BoardGraphics.cs (stream, glass fallback, tap relays) · BoardLayout.cs · BottleShape.cs ·
// BottleArt.cs (assets + data).
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game.Board
{
    public sealed partial class BoardView : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------------------ events

        /// <summary>The player asks to pour from → to (both idle, from was lifted). The session validates with
        /// BoardState.Pour and answers with PlayPour (legal) or PlayInvalid (illegal).</summary>
        public event Action<int, int> OnPourRequested;
        /// <summary>A bottle was lifted (tutorial hand progression, sound is played by the view).</summary>
        public event Action<int> OnBottleSelected;
        /// <summary>A stone-wrapped bottle was tapped (the session may offer the rewarded-ad unlock).</summary>
        public event Action<int> OnLockedTapped;
        /// <summary>Every running animation finished (the session checks win / stuck here).</summary>
        public event Action OnIdle;

        // ---- additions (optional for the session)

        /// <summary>A cork landed in a completed bottle (pour or wand completion): the moment to play the combo sound /
        /// floating combo text in sync with the visuals.</summary>
        public event Action<int> OnBottleCorked;

        // ------------------------------------------------------------------------------------------------ setup

        /// <summary>Creates the view stretched inside <paramref name="parent"/>.</summary>
        public static BoardView Create(RectTransform parent)
        {
            var go = new GameObject("BoardView", typeof(RectTransform)) { layer = UIKit.UILayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<BoardView>();
            view.EnsureInit();
            return view;
        }

        /// <summary>The board being shown (null after Clear).</summary>
        public BoardState Board { get; private set; }

        /// <summary>Builds every bottle of <paramref name="board"/> (snapped to its current state, hidden until
        /// PlayIntro). <paramref name="areaId"/> picks themed accents if any.</summary>
        public void Build(BoardState board, string areaId)
        {
            EnsureInit();
            Clear();
            Board = board;
            _areaId = areaId;
            _accent = AccentOf(areaId);
            if (board == null) return;
            EnsureViews();
            for (int i = 0; i < _views.Count; i++) _views[i].group.alpha = 0f;
            _awaitingIntro = true;
            ApplyLayout(false);
        }

        /// <summary>Destroys every bottle and stops every animation.</summary>
        public void Clear() => ClearInternal(true);

        /// <summary>Recomputes the layout for the current rect / bottle count (call after the rect changed).</summary>
        public void Relayout(bool animate)
        {
            if (Board == null) return;
            EnsureViews();
            ApplyLayout(animate);
        }

        /// <summary>Snaps every bottle's visuals to the model (no animation). Safe at any time.</summary>
        public void RefreshAll()
        {
            EnsureInit();
            if (Board == null) return;
            bool had = _active.Count > 0 || _queue.Count > 0;
            var callbacks = AbortAllJobs();
            _selected = -1;
            EnsureViews();
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                v.ResetPose();
                v.SetSelectedGlow(false);
                v.introOffset = Vector2.zero;
                v.introScale = 1f;
                v.group.alpha = 1f;
                v.fxBusyUntil = 0f;
                SnapToModel(v);
            }
            _awaitingIntro = false;
            ApplyLayout(false);
            ReapplyHint();
            InvokeAll(callbacks);
            if (had && _active.Count == 0 && _queue.Count == 0)
            {
                _hadJobs = false;
                RaiseIdle();
            }
        }

        // ------------------------------------------------------------------------------------------------ state

        /// <summary>Taps are accepted (the session turns it off during intro, popups, win...).</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Index of the lifted bottle (-1 none).</summary>
        public int Selected => _selected;

        /// <summary>Any animation is running.</summary>
        public bool IsAnimating => _active.Count > 0 || _queue.Count > 0;

        /// <summary>That bottle is part of a running animation (can't be tapped).</summary>
        public bool IsBusy(int bottle)
        {
            var v = View(bottle);
            if (v == null) return false;
            if (v.holder != null || _clock < v.fxBusyUntil) return true;
            for (int i = 0; i < _queue.Count; i++) if (_queue[i].Holds(bottle)) return true;
            return false;
        }

        /// <summary>Puts the lifted bottle back down.</summary>
        public void Deselect(bool animate = true) => DeselectInternal(animate, false);

        /// <summary>World position of a bottle's center (tutorial hand, FX, flying rewards).</summary>
        public Vector3 BottleWorldPosition(int bottle)
        {
            var v = View(bottle);
            return v != null ? v.RestCenterWorld : transform.position;
        }

        /// <summary>World position of a bottle's mouth (top opening).</summary>
        public Vector3 BottleMouthWorldPosition(int bottle)
        {
            var v = View(bottle);
            return v != null ? v.RestMouthWorld : transform.position;
        }

        /// <summary>RectTransform of a bottle (null if out of range).</summary>
        public RectTransform BottleRect(int bottle)
        {
            var v = View(bottle);
            return v != null ? v.slot : null;
        }

        // ---- additions

        /// <summary>Current uniform scale of the bottles (design pixels → board units).</summary>
        public float BottleScale => _layout != null ? _layout.scale : 1f;

        /// <summary>Number of rows of the current layout.</summary>
        public int Rows => _layout != null ? _layout.rows : 0;

        // ------------------------------------------------------------------------------------------------ fields

        const float U = BottleView.UnitPx;

        bool _init;
        RectTransform _rect, _racks, _bottles, _fx, _hintLayer, _hits;
        readonly List<BottleView> _views = new List<BottleView>(16);
        readonly List<RectTransform> _hitAreas = new List<RectTransform>(16);
        readonly List<Image> _rackImages = new List<Image>(3);
        BoardLayout _layout;
        BottleShape _shape;
        string _areaId;
        Color _accent = DS.Colors.Brand;
        float _clock, _frameDt;
        Vector2 _lastRectSize = new Vector2(-1f, -1f);
        int _selected = -1;
        bool _awaitingIntro;

        // ------------------------------------------------------------------------------------------------ lifecycle

        void Awake() => EnsureInit();

        void EnsureInit()
        {
            if (_init) return;
            _init = true;
            gameObject.layer = UIKit.UILayer;
            _rect = (RectTransform)transform;

            // Own canvas: pours rebuild liquid meshes every frame, this keeps the rebatch away from the HUD. The
            // raycaster is mandatory (graphics of a nested canvas are not seen by the parent's raycaster).
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            var sm = ScreenManager.Instance;
            if (sm != null && sm.Canvas != null) canvas.additionalShaderChannels = sm.Canvas.additionalShaderChannels;
            else canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            // Whole-rect background target: a tap between bottles puts the lifted one down.
            var bg = GetComponent<HitArea>();
            if (bg == null) bg = gameObject.AddComponent<HitArea>();
            bg.raycastTarget = true;
            var tap = GetComponent<BoardTap>();
            if (tap == null) tap = gameObject.AddComponent<BoardTap>();
            tap.board = this;

            _racks = NewLayer("Racks");
            _bottles = NewLayer("Bottles");
            _fx = NewLayer("Fx");
            _hintLayer = NewLayer("Hint");
            _hits = NewLayer("Hits");
            _shape = BottleArt.Shape;
        }

        RectTransform NewLayer(string name)
        {
            var rt = UIKit.Rect(name, transform);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void OnDestroy()
        {
            // Teardown: abort without invoking session callbacks (the screen is going away).
            AbortAllJobs();
            ReleaseOrbs();
            _views.Clear();
            Board = null;
        }

        void Update()
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, Tween.MaxStep);
            _frameDt = dt;
            _clock += dt;
            TickJobs(dt);
            UpdatePlacement(dt);
            UpdateShimmer(dt);
            UpdateHint(dt);
        }

        void LateUpdate()
        {
            if (Board != null && _rect != null)
            {
                Vector2 size = _rect.rect.size;
                if ((size - _lastRectSize).sqrMagnitude > 0.25f) ApplyLayout(false);
            }
            float dt = _frameDt;
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                if (v == null || v.slot == null) continue;
                if (v.liquid.Refresh(dt)) v.UpdateMarks();
                v.UpdateStream(dt);
            }
        }

        // ------------------------------------------------------------------------------------------------ build / clear

        void ClearInternal(bool invokeCallbacks)
        {
            EnsureInit();
            var callbacks = AbortAllJobs();
            HideHintInternal();
            ReleaseOrbs();
            for (int i = 0; i < _views.Count; i++) _views[i]?.Destroy();
            _views.Clear();
            for (int i = 0; i < _hitAreas.Count; i++) if (_hitAreas[i] != null) Destroy(_hitAreas[i].gameObject);
            _hitAreas.Clear();
            ClearRacks();
            for (int i = _fx.childCount - 1; i >= 0; i--) Destroy(_fx.GetChild(i).gameObject);
            _selected = -1;
            _layout = null;
            _awaitingIntro = false;
            _hadJobs = false;
            Board = null;
            if (invokeCallbacks) InvokeAll(callbacks);
        }

        /// <summary>Creates / destroys bottle views so there is exactly one per model bottle.</summary>
        void EnsureViews()
        {
            int n = Board != null ? Board.Count : 0;
            while (_views.Count > n)
            {
                int last = _views.Count - 1;
                if (_selected == last) _selected = -1;
                _views[last]?.Destroy();
                _views.RemoveAt(last);
            }
            for (int i = _views.Count; i < n; i++)
            {
                var v = BottleView.Create(_bottles, i, _shape, _fx, Board.Capacity);
                SnapToModel(v);
                _views.Add(v);
            }
        }

        /// <summary>Snaps a view's liquid, cork and stone to the model (no animation).</summary>
        void SnapToModel(BottleView v)
        {
            if (Board == null || v == null || v.index >= Board.Count) return;
            var b = Board[v.index];
            var d = new BottleData();
            d.CopyFrom(b);
            v.SetData(d);
            v.SnapLock(b.LockRemaining);
            v.lockSeq = _seq;
        }

        BottleView View(int i) => i >= 0 && i < _views.Count ? _views[i] : null;

        static Color AccentOf(string areaId)
        {
            try
            {
                var area = Catalog.GetArea(areaId);
                if (area != null && area.accent.a > 0f) return area.accent;
            }
            catch (Exception) { /* catalog unavailable: default accent */ }
            return DS.Colors.Brand;
        }

        static void InvokeAll(List<Action> callbacks)
        {
            if (callbacks == null) return;
            for (int i = 0; i < callbacks.Count; i++) Invoke(callbacks[i]);
        }

        static void Invoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Invoke<T>(Action<T> a, T value)
        {
            if (a == null) return;
            try { a(value); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Invoke<T1, T2>(Action<T1, T2> a, T1 v1, T2 v2)
        {
            if (a == null) return;
            try { a(v1, v2); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void RaiseIdle()
        {
            var handler = OnIdle;
            if (handler != null) Invoke(handler);
        }

        internal void RaiseCorked(int bottle) => Invoke(OnBottleCorked, bottle);

        // ------------------------------------------------------------------------------------------------ layout

        void ApplyLayout(bool animate)
        {
            if (_rect == null) return;
            _lastRectSize = _rect.rect.size;
            if (Board == null) return;
            int n = _views.Count;
            float gw = _shape.GlassWidth * U, gh = _shape.GlassHeight * U, oy = -_shape.GlassBottom * U;
            _layout = BoardLayout.Compute(n, _lastRectSize, gw, gh, oy);
            for (int i = 0; i < n; i++)
            {
                var v = _views[i];
                Vector2 target = _layout.origins[i];
                float s = _layout.scale;
                if (animate && v.placed && v.slot != null)
                {
                    float e = Tween.Evaluate(Ease.OutCubic, Mathf.Clamp01(v.slideT));
                    v.slideFrom = Vector2.LerpUnclamped(v.slideFrom, v.home, e);
                    v.scaleFrom = Mathf.LerpUnclamped(v.scaleFrom, v.scale, e);
                    v.slideT = 0f;
                    v.slideDuration = 0.38f;
                }
                else
                {
                    v.slideFrom = target;
                    v.scaleFrom = s;
                    v.slideT = 1f;
                }
                v.home = target;
                v.scale = s;
                v.placed = true;
                v.row = _layout.rowOf[i];
                v.column = _layout.colOf[i];
            }
            UpdatePlacement(0f);
            LayoutHitAreas();
            BuildRacks(animate);
        }

        /// <summary>Applies slide (relayout) and intro offsets to the slots. Cheap when nothing moves.</summary>
        void UpdatePlacement(float dt)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                if (v == null || v.slot == null) continue;
                if (v.slideT < 1f) v.slideT = Mathf.Min(1f, v.slideT + dt / Mathf.Max(0.01f, v.slideDuration));
                float e = v.slideT >= 1f ? 1f : Tween.Evaluate(Ease.OutCubic, v.slideT);
                Vector2 pos = Vector2.LerpUnclamped(v.slideFrom, v.home, e) + v.introOffset;
                float sc = Mathf.LerpUnclamped(v.scaleFrom, v.scale, e) * v.introScale;
                if ((v.slot.anchoredPosition - pos).sqrMagnitude > 1e-6f) v.slot.anchoredPosition = pos;
                if (Mathf.Abs(v.slot.localScale.x - sc) > 1e-6f) v.slot.localScale = new Vector3(sc, sc, 1f);
            }
        }

        void LayoutHitAreas()
        {
            int n = _layout != null ? _layout.count : 0;
            while (_hitAreas.Count > n)
            {
                int last = _hitAreas.Count - 1;
                if (_hitAreas[last] != null) Destroy(_hitAreas[last].gameObject);
                _hitAreas.RemoveAt(last);
            }
            for (int i = 0; i < n; i++)
            {
                if (i >= _hitAreas.Count)
                {
                    var rt = UIKit.Rect("Hit" + i, _hits);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    var hit = rt.gameObject.AddComponent<HitArea>();
                    hit.raycastTarget = true;
                    var relay = rt.gameObject.AddComponent<BottleTap>();
                    relay.board = this;
                    relay.index = i;
                    _hitAreas.Add(rt);
                }
                var r = _layout.HitRect(i);
                _hitAreas[i].sizeDelta = new Vector2(r.width, r.height);
                _hitAreas[i].anchoredPosition = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
            }
        }

        void ClearRacks()
        {
            for (int i = 0; i < _rackImages.Count; i++) if (_rackImages[i] != null) Destroy(_rackImages[i].gameObject);
            _rackImages.Clear();
            _rackAlphas.Clear();
        }

        /// <summary>A wooden shelf plank (bottle_rack) under every row; a soft glass shelf when the art is missing.</summary>
        void BuildRacks(bool animate)
        {
            ClearRacks();
            if (_layout == null || _layout.count == 0) return;
            var sp = BottleArt.RackSliced;
            float aspect = sp != null ? Mathf.Max(1f, BottleArt.Aspect("bottle_rack", 6.4f)) : 0f;
            float h = _layout.BottleHeight, bw = _layout.BottleWidth;
            for (int row = 0; row < _layout.rows; row++)
            {
                int c = _layout.rowCounts[row];
                if (c <= 0) continue;
                float width = (c - 1) * _layout.pitchX + bw * 2.1f;
                Image img;
                float plank;
                if (sp != null)
                {
                    // plank + brackets ≈ 14% of a bottle; the ends keep their aspect (9-sliced), the middle stretches
                    plank = Mathf.Min(h * 0.14f, width / aspect);
                    img = UIKit.NewImage(_racks, "Rack", sp, Color.white);
                    img.type = Image.Type.Sliced;
                    img.fillCenter = true;
                    // sliced borders are drawn border / (Image.pixelsPerUnit · multiplier) units wide
                    float capPx = sp.border.x;
                    float capWanted = plank * aspect * BottleArt.RackCapFraction;
                    float basePpu = Mathf.Max(0.0001f, img.pixelsPerUnit);
                    img.pixelsPerUnitMultiplier = capWanted > 0.01f ? Mathf.Max(0.01f, capPx / (capWanted * basePpu)) : 1f;
                }
                else
                {
                    plank = h * 0.045f;
                    img = UIKit.NewImage(_racks, "Rack", UISprites.Capsule, DS.WithAlpha(Color.Lerp(Color.white, _accent, 0.2f), 0.28f));
                    if (img.sprite != null && img.sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
                }
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(width, plank);
                rt.anchoredPosition = new Vector2(0f, _layout.rowBottom[row] + h * 0.022f);
                img.preserveAspect = false;
                float alpha = img.color.a;
                if (animate || _awaitingIntro)
                {
                    // hidden until the intro fades them in (FadeInRacks), or faded in after a relayout
                    img.color = DS.WithAlpha(img.color, 0f);
                    if (!_awaitingIntro) Tween.Fade(img, alpha, 0.3f);
                }
                _rackImages.Add(img);
                _rackAlphas.Add(alpha);
            }
        }

        readonly List<float> _rackAlphas = new List<float>(3);
    }
}
