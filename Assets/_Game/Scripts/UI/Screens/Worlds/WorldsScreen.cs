// ============================================================================================================
// Worlds — the level groups screen (GDD §4). Two views in one screen:
//  * World list: a vertical scroll of world cards (art in a rounded window with a soft parallax, accent frame with a
//    3D lip, "World N" plate, name, "Levels 21–40", stars x/60, levels-won bar) in three states: completed (gold
//    check, crown at 60/60), current ("Current" tag, glow, pulse) and locked (dimmed art, padlock, "Reach level N").
//    Every reached world is listed plus the next two; themes cycle after the sixth (world 7 = Enchanted Forest II).
//  * Level map: 20 level nodes on a winding road over the world's blurred backdrop. Won levels show their best stars
//    (tap = Level Start in replay mode), the current level pulses with Luna beside it (tap = Level Start), hard levels
//    are purple with a skull, future levels are gray. The world's album cards decorate the roadside and the next
//    world waits at the end of the road.
// Opens on the list scrolled to the current world (WorldsScreen.Open) or straight on a map (OpenWorld). Back: map →
// list → Home. Built only from the design system (DS / UIKit / Tween / FX); null-safe when art is missing.
// ============================================================================================================
using System.Collections.Generic;
using PotionPop.Levels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class WorldsScreen : TabScreen
    {
        public override ScreenId Id => ScreenId.Worlds;
        public static WorldsScreen Instance { get; private set; }

        /// <summary>Argument of ScreenManager.Show(ScreenId.Worlds, …) that opens a world's level map right away.</summary>
        public sealed class MapRequest
        {
            public int areaNumber;
        }

        /// <summary>Shows the world list, scrolled to the current world.</summary>
        public static void Open()
        {
            var sm = ScreenManager.Instance;
            if (sm != null) sm.Show(ScreenId.Worlds);
        }

        /// <summary>Shows the level map of an (unbounded, 0-based) world number. Locked worlds fall back to the list.</summary>
        public static void OpenWorld(int areaNumber)
        {
            var sm = ScreenManager.Instance;
            if (sm != null) sm.Show(ScreenId.Worlds, new MapRequest { areaNumber = Mathf.Max(0, areaNumber) });
        }

        /// <summary>Shows the level map of the world of the next level to play.</summary>
        public static void OpenCurrentWorld() => OpenWorld(Progress.CurrentAreaNumber);

        /// <summary>Locked worlds listed after the last reached one.</summary>
        public const int LockedPreview = 2;

        const float CardH = 400f, CardGap = 40f, CardRadius = 54f, CardBorder = 16f, CardLip = 14f;
        /// <summary>Point of the world art (from the top) kept in the middle of the card window: the building.</summary>
        const float ArtFocus = 0.42f;
        const float Parallax = 0.12f;

        ScrollRect _scroll;
        RectTransform _list, _footer;
        readonly List<WorldCard> _cards = new List<WorldCard>();
        float _w;
        TweenHandle _autoScroll;
        LevelMap _map;
        int _shownCount = -1;
        readonly Vector3[] _corners = new Vector3[4];
        readonly Vector3[] _viewCorners = new Vector3[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        // ======================================================================================== lifecycle

        public override void Build()
        {
            Instance = this;
            BuildFrame("worlds.title", DS.Colors.Brand);
            BuildList();
            _map = new LevelMap(this);
            Progress.OnLevelChanged += OnLevelChanged;
            Progress.OnLevelStarsChanged += OnLevelStarsChanged;
            Loc.OnLanguageChanged += RequestRefresh;
            Collection.OnChanged += OnCollectionChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Progress.OnLevelChanged -= OnLevelChanged;
            Progress.OnLevelStarsChanged -= OnLevelStarsChanged;
            Loc.OnLanguageChanged -= RequestRefresh;
            Collection.OnChanged -= OnCollectionChanged;
            if (Instance == this) Instance = null;
        }

        void OnLevelChanged(int level) => RequestRefresh();
        void OnLevelStarsChanged(int level, int stars) => RequestRefresh();
        void OnCollectionChanged() { if (_map != null && _map.IsOpen && IsVisible) _map.Refresh(false); }

        public override void OnShow(object arg)
        {
            int request = arg is MapRequest m ? m.areaNumber : -1;
            if (request >= 0 && Progress.StateOfArea(request) == WorldState.Locked) request = -1;
            base.OnShow(arg);   // ribbon pop + Refresh(true): the list scrolls to the current world
            if (request >= 0) _map.Open(request, null, true);
        }

        public override void OnHide()
        {
            _autoScroll?.Kill();
            _autoScroll = null;
            if (_map != null) _map.CloseImmediate();
            SetListVisible(true);
        }

        /// <summary>Hardware back / ESC: the map closes back to the list; the list goes Home (GameRoot).</summary>
        public override bool OnBack()
        {
            if (_map != null && _map.IsOpen)
            {
                _map.Close();
                return true;
            }
            return false;
        }

        protected override void OnLayoutChanged()
        {
            if (Mathf.Abs(ContentWidth - _w) < 1f) return;
            BuildList();
            _map?.Rebuild();
            if (IsVisible) Refresh(false);
        }

        protected override void Refresh(bool animate)
        {
            if (_list == null) return;
            int count = WorldCount;
            EnsureCards(count);
            for (int i = 0; i < count; i++) _cards[i].Bind(i);
            if (_footer != null) _footer.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            if (animate || count != _shownCount)
            {
                int current = Mathf.Clamp(Progress.CurrentAreaNumber, 0, count - 1);
                if (animate) PlayEntrance(current);
                ScrollToWorld(current, animate);
            }
            _shownCount = count;
            UpdateParallax();
            if (_map != null && _map.IsOpen) _map.Refresh(false);
        }

        /// <summary>Every reached world plus <see cref="LockedPreview"/> locked ones.</summary>
        static int WorldCount => Mathf.Max(1, Progress.CurrentAreaNumber + 1 + LockedPreview);

        // ======================================================================================== list

        void BuildList()
        {
            if (_scroll != null)
            {
                _scroll.gameObject.SetActive(false);
                Destroy(_scroll.gameObject);
            }
            _cards.Clear();
            _shownCount = -1;
            _w = ContentWidth;

            _scroll = UIKit.ScrollView(Body, new Vector2(_w, BodyHeight), out _list, CardGap, DS.Space.M);
            UIKit.Stretch((RectTransform)_scroll.transform);
            var layout = _list.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                // Room above for the tags / stamps that stick out of the first card, below for the raised nav tile.
                layout.padding = new RectOffset(0, 0, 44, 120);
            }
            _scroll.onValueChanged.AddListener(_ => UpdateParallax());
            ScrollDragWatcher.Attach(_scroll, StopAutoScroll);

            _footer = UIKit.Rect("Footer", _list);
            _footer.sizeDelta = new Vector2(_w, 150f);
            var icon = UIKit.Image(_footer, "icon_map", new Vector2(96f, 96f));
            UIKit.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(96f, 96f), new Vector2(0f, 0f));
            Tween.Bob(icon.rectTransform, 6f, 2.4f);
            var note = CommonUI.LocLabel(_footer, "worlds.more_coming", TextStyle.BodyLight, new Vector2(_w - 80f, 56f),
                TextAlignmentOptions.Center, 36f);
            UIKit.Place(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(_w - 80f, 56f), new Vector2(0f, 4f));
        }

        void EnsureCards(int count)
        {
            while (_cards.Count < count)
            {
                var card = WorldCard.Create(this, _list, _w);
                _cards.Add(card);
            }
            for (int i = 0; i < _cards.Count; i++)
            {
                bool active = i < count;
                if (_cards[i].Root.gameObject.activeSelf != active) _cards[i].Root.gameObject.SetActive(active);
            }
        }

        void PlayEntrance(int current)
        {
            int count = Mathf.Min(WorldCount, _cards.Count);
            for (int i = 0; i < count; i++)
            {
                // Cards around the current world appear first (that is where the list scrolls to).
                float delay = 0.06f + Mathf.Min(8, Mathf.Abs(i - current)) * 0.06f;
                _cards[i].PlayEntrance(delay);
            }
        }

        void SetListVisible(bool visible)
        {
            if (_scroll == null) return;
            if (_scroll.gameObject.activeSelf != visible) _scroll.gameObject.SetActive(visible);
            if (TitleRibbon != null && TitleRibbon.gameObject.activeSelf != visible) TitleRibbon.gameObject.SetActive(visible);
        }

        void StopAutoScroll()
        {
            _autoScroll?.Kill();
            _autoScroll = null;
        }

        /// <summary>Centers a world card in the viewport (smooth when animated).</summary>
        void ScrollToWorld(int index, bool animate)
        {
            if (_scroll == null || index < 0 || index >= _cards.Count) return;
            StopAutoScroll();
            _scroll.StopMovement();
            var view = _scroll.viewport;
            float viewH = view.rect.height;
            float maxY = Mathf.Max(0f, _list.rect.height - viewH);
            var card = _cards[index].Root;
            card.GetWorldCorners(_corners);
            _list.GetWorldCorners(_viewCorners);
            float scale = Mathf.Max(0.0001f, _list.lossyScale.y);
            float centerFromTop = (_viewCorners[1].y - (_corners[0].y + _corners[1].y) * 0.5f) / scale;
            float target = Mathf.Clamp(centerFromTop - viewH * 0.5f, 0f, maxY);
            if (!animate)
            {
                SetListY(target);
                return;
            }
            float from = _list.anchoredPosition.y;
            if (Mathf.Abs(from - target) < 1f) return;
            _autoScroll = Tween.Value(from, target, 0.75f, SetListY, Ease.InOutCubic).SetDelay(0.08f).SetLink(this);
        }

        void SetListY(float y)
        {
            if (_list == null) return;
            _list.anchoredPosition = new Vector2(_list.anchoredPosition.x, y);
            UpdateParallax();
        }

        /// <summary>World art drifts a little against the scroll (depth).</summary>
        void UpdateParallax()
        {
            if (_scroll == null || !_scroll.gameObject.activeInHierarchy) return;
            _scroll.viewport.GetWorldCorners(_viewCorners);
            float viewCenter = (_viewCorners[0].y + _viewCorners[1].y) * 0.5f;
            float scale = Mathf.Max(0.0001f, _scroll.viewport.lossyScale.y);
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (!c.Root.gameObject.activeSelf) continue;
                c.Root.GetWorldCorners(_corners);
                float center = (_corners[0].y + _corners[1].y) * 0.5f;
                c.SetParallax((center - viewCenter) / scale);
            }
        }

        void OnCardTapped(WorldCard card)
        {
            if (card == null || _map == null || _map.IsOpen) return;
            int area = card.Area;
            if (Progress.StateOfArea(area) == WorldState.Locked)
            {
                CommonUI.Deny(card.Visual);
                UIKit.Toast(Loc.T("worlds.reach_level", Areas.FirstLevelOfArea(area)));
                return;
            }
            StopAutoScroll();
            Tween.Punch(card.Visual, 0.08f, 0.3f);
            _map.Open(area, card.Root, false);
        }

        void Update()
        {
            if (!IsVisible) return;
            _map?.Tick(Time.unscaledDeltaTime);
        }

        // ======================================================================================== world card

        sealed class WorldCard
        {
            public RectTransform Root, Visual;
            public int Area = -1;

            WorldsScreen _owner;
            string _themeId;
            Image _glow, _lip, _face, _art, _shade, _dim;
            RectTransform _window, _artRt;
            TMP_Text _number, _name, _levels, _stars, _lockText;
            RectTransform _starsPill, _currentTag, _check, _crown, _lockLayer, _luna;
            CanvasGroup _group;
            ProgressBar _bar;
            float _artBaseY, _artRange;
            TweenHandle _pulse, _glowPulse;
            WorldState _state = (WorldState)(-1);
            static readonly Vector2 LunaPos = new Vector2(-312f, -10f);

            public static WorldCard Create(WorldsScreen owner, RectTransform parent, float w)
            {
                var c = new WorldCard { _owner = owner };
                var root = UIKit.Rect("World", parent);
                root.sizeDelta = new Vector2(w, CardH);
                c.Root = root;
                var visual = UIKit.Stretch(UIKit.Rect("Visual", root));
                c.Visual = visual;
                c._group = visual.gameObject.AddComponent<CanvasGroup>();

                c._glow = UIKit.NewImage(visual, "Glow", UISprites.Glow, DS.WithAlpha(DS.Colors.StarGold, 0.75f));
                UIKit.Stretch(c._glow.rectTransform, -80f, -70f, -80f, -100f);
                c._glow.gameObject.SetActive(false);
                UIKit.Shadow(visual, 34f, -16f, 0.38f);
                c._lip = UIKit.RoundedRect(visual, Vector2.zero, DS.Colors.BrandDark, CardRadius);
                UIKit.Stretch(c._lip.rectTransform);
                c._face = UIKit.RoundedRect(visual, Vector2.zero, DS.Colors.Brand, CardRadius);
                UIKit.Stretch(c._face.rectTransform, 0f, 0f, 0f, CardLip);

                // Art window: stencil mask in a rounded rect.
                var window = UIKit.Rect("Window", visual);
                UIKit.Stretch(window, CardBorder, CardBorder, CardBorder, CardBorder + CardLip);
                var maskImg = window.gameObject.AddComponent<Image>();
                maskImg.sprite = UISprites.Rounded;
                maskImg.raycastTarget = false;
                SliceFit.Attach(maskImg, SliceFit.Mode.Corner, (CardRadius - CardBorder) * UISprites.RoundedBorderPerRadius);
                window.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                c._window = window;

                c._art = UIKit.NewImage(window, "Art", null, Color.white);
                c._artRt = c._art.rectTransform;
                c._artRt.anchorMin = c._artRt.anchorMax = new Vector2(0.5f, 0.5f);
                c._artRt.pivot = new Vector2(0.5f, 0.5f);

                // Shade at the bottom of the window (text legibility) and a soft one at the top (tags).
                var gradient = UISprites.Get("ui_gradient_v");
                c._shade = UIKit.NewImage(window, "Shade", gradient, DS.WithAlpha(DS.Colors.Ink, gradient != null ? 0.8f : 0.35f));
                var srt = c._shade.rectTransform;
                srt.anchorMin = new Vector2(0f, 0f);
                srt.anchorMax = new Vector2(1f, 0.62f);
                srt.offsetMin = srt.offsetMax = Vector2.zero;
                srt.localEulerAngles = new Vector3(0f, 0f, 180f);
                var top = UIKit.NewImage(window, "TopShade", gradient, DS.WithAlpha(DS.Colors.Ink, gradient != null ? 0.35f : 0.12f));
                var trt = top.rectTransform;
                trt.anchorMin = new Vector2(0f, 0.72f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.offsetMin = trt.offsetMax = Vector2.zero;

                c._dim = UIKit.NewImage(window, "Dim", UISprites.Pixel, new Color(0.18f, 0.1f, 0.3f, 0.5f));
                UIKit.Stretch(c._dim.rectTransform);
                c._dim.gameObject.SetActive(false);

                var gloss = UIKit.RoundedRect(window, Vector2.zero, DS.WithAlpha(Color.white, 0.13f), CardRadius - CardBorder);
                var grt = gloss.rectTransform;
                grt.anchorMin = new Vector2(0f, 1f);
                grt.anchorMax = new Vector2(1f, 1f);
                grt.pivot = new Vector2(0.5f, 1f);
                grt.offsetMin = new Vector2(10f, -64f);
                grt.offsetMax = new Vector2(-10f, -8f);

                // "World N" plate (top-left).
                var plate = UIKit.Rect("NumberPlate", visual);
                UIKit.Place(plate, new Vector2(0f, 1f), new Vector2(230f, 62f), new Vector2(34f, -32f));
                var plateBg = UIKit.Capsule(plate, Vector2.zero, DS.WithAlpha(DS.Colors.Ink, 0.7f));
                UIKit.Stretch(plateBg.rectTransform);
                c._number = CommonUI.Label(plate, "", TextStyle.Badge, new Vector2(210f, 56f), TextAlignmentOptions.Center, 34f);
                UIKit.Stretch(c._number.rectTransform, 14f, 2f, 14f, 4f);

                // Name + levels range (bottom-left).
                float textW = w * 0.6f;
                c._name = CommonUI.Label(visual, "", TextStyle.H1, new Vector2(textW, 92f), TextAlignmentOptions.Left, 74f);
                c._name.textWrappingMode = TextWrappingModes.NoWrap;
                UIKit.Place(c._name.rectTransform, new Vector2(0f, 0f), new Vector2(textW, 92f), new Vector2(42f, 128f));
                c._levels = CommonUI.Label(visual, "", TextStyle.BodyLight, new Vector2(textW, 48f), TextAlignmentOptions.Left, 38f);
                c._levels.textWrappingMode = TextWrappingModes.NoWrap;
                UIKit.Place(c._levels.rectTransform, new Vector2(0f, 0f), new Vector2(textW, 48f), new Vector2(44f, 88f));

                // Stars pill (bottom-right).
                c._starsPill = UIKit.Rect("Stars", visual);
                UIKit.Place(c._starsPill, new Vector2(1f, 0f), new Vector2(250f, 74f), new Vector2(-34f, 96f));
                var pillBg = UIKit.Capsule(c._starsPill, Vector2.zero, DS.WithAlpha(DS.Colors.Ink, 0.62f));
                UIKit.Stretch(pillBg.rectTransform, 30f, 0f, 0f, 0f);
                var star = UIKit.Image(c._starsPill, "icon_star", new Vector2(92f, 92f));
                UIKit.Place(star.rectTransform, new Vector2(0f, 0.5f), new Vector2(92f, 92f), new Vector2(-6f, 4f));
                c._stars = CommonUI.Label(c._starsPill, "", TextStyle.H3, new Vector2(150f, 66f), TextAlignmentOptions.Center, 44f);
                UIKit.Stretch(c._stars.rectTransform, 86f, 4f, 14f, 6f);
                c._crown = UIKit.Image(c._starsPill, "icon_crown", new Vector2(74f, 74f)).rectTransform;
                UIKit.Place(c._crown, new Vector2(1f, 1f), new Vector2(74f, 74f), new Vector2(18f, 40f));
                c._crown.localEulerAngles = new Vector3(0f, 0f, -16f);

                // Levels-won bar (bottom).
                c._bar = UIKit.ProgressBar(visual, new Vector2(w - 84f, 46f));
                UIKit.Place((RectTransform)c._bar.transform, new Vector2(0.5f, 0f), new Vector2(w - 84f, 46f), new Vector2(0f, 34f));
                c._bar.label.gameObject.SetActive(true);
                DS.Apply(c._bar.label, TextStyle.Badge, 30f);

                // State: "Current" tag + Luna peeking | completed stamp.
                c._currentTag = MetaUI.Tag(visual, "worlds.current", DS.Colors.Pink, "icon_play", new Vector2(270f, 74f), 40f);
                UIKit.Place(c._currentTag, new Vector2(1f, 1f), c._currentTag.sizeDelta, new Vector2(-30f, -30f));
                c._luna = CommonUI.Avatar(visual, "avatar_luna", 118f, DS.Colors.StarGold);
                UIKit.Place(c._luna, new Vector2(1f, 1f), new Vector2(118f, 118f), LunaPos);
                c._check = MetaUI.CheckStamp(visual, 104f, false);
                UIKit.Place(c._check, new Vector2(1f, 1f), new Vector2(104f, 104f), new Vector2(-28f, -24f));

                // Locked: padlock + "Reach level N".
                c._lockLayer = UIKit.Rect("Locked", visual);
                UIKit.Stretch(c._lockLayer);
                var lockIcon = UIKit.Image(c._lockLayer, "icon_lock", new Vector2(150f, 150f));
                UIKit.Place(lockIcon.rectTransform, new Vector2(1f, 0.5f), new Vector2(150f, 150f), new Vector2(-70f, 40f));
                lockIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                lockIcon.rectTransform.anchoredPosition = new Vector2(-150f, 50f);
                var lockPlate = UIKit.Capsule(c._lockLayer, new Vector2(330f, 66f), DS.WithAlpha(DS.Colors.Ink, 0.78f));
                UIKit.Place(lockPlate.rectTransform, new Vector2(1f, 0f), new Vector2(330f, 66f), new Vector2(-34f, 40f));
                c._lockText = CommonUI.Label(lockPlate.rectTransform, "", TextStyle.H3, new Vector2(310f, 60f), TextAlignmentOptions.Center, 36f);
                UIKit.Stretch(c._lockText.rectTransform, 16f, 2f, 16f, 4f);

                var card = c;
                CommonUI.MakeTappable(root, visual, () => owner.OnCardTapped(card));
                return c;
            }

            public void Bind(int areaNumber)
            {
                Area = areaNumber;
                var theme = Areas.AreaForNumber(areaNumber);
                Color accent = theme != null ? theme.accent : DS.Colors.Brand;
                var state = Progress.StateOfArea(areaNumber);
                bool locked = state == WorldState.Locked;

                string themeId = theme != null ? theme.id : "";
                if (themeId != _themeId)
                {
                    _themeId = themeId;
                    SetArt(theme, accent);
                }
                Color frame = locked ? DS.Colors.Gray : accent;
                _face.color = frame;
                _lip.color = DS.Darken(frame, 0.45f);
                _art.color = locked ? new Color(0.6f, 0.56f, 0.7f, 1f) : Color.white;
                _dim.gameObject.SetActive(locked);

                _number.text = Loc.T("worlds.world_number", areaNumber + 1);
                _name.text = MetaUI.WorldName(areaNumber);
                _name.color = locked ? new Color(1f, 1f, 1f, 0.85f) : Color.white;
                int first = Areas.FirstLevelOfArea(areaNumber), last = Areas.LastLevelOfArea(areaNumber);
                _levels.text = Loc.T("worlds.levels_range", first, last);
                int stars = Progress.StarsInArea(areaNumber);
                int won = Progress.LevelsWonInArea(areaNumber);
                _stars.text = Loc.T("quest.progress", stars, Areas.MaxStarsPerArea);
                _bar.SetValue(won / (float)Areas.LevelsPerArea, false);
                _bar.SetLabel(Loc.T("worlds.levels_won", won, Areas.LevelsPerArea));
                _bar.label.gameObject.SetActive(true);
                _bar.SetFillColor(state == WorldState.Completed ? Color.white : DS.Colors.Secondary);

                _starsPill.gameObject.SetActive(!locked);
                _bar.gameObject.SetActive(!locked);
                _crown.gameObject.SetActive(stars >= Areas.MaxStarsPerArea);
                _lockLayer.gameObject.SetActive(locked);
                _lockText.text = Loc.T("worlds.reach_level", first);
                _check.gameObject.SetActive(state == WorldState.Completed);
                _currentTag.gameObject.SetActive(state == WorldState.Current);
                _luna.gameObject.SetActive(state == WorldState.Current);

                if (state != _state)
                {
                    _state = state;
                    ApplyIdle();
                }
            }

            /// <summary>World art covering the window (focus on the building), or an accent gradient without art.</summary>
            void SetArt(AreaInfo theme, Color accent)
            {
                string sprite = theme != null ? theme.HomeBackground : null;
                var sp = !string.IsNullOrEmpty(sprite) && UISprites.Exists(sprite) ? UISprites.Get(sprite) : null;
                Rect win = _window.rect;
                float ww = Mathf.Max(10f, win.width), wh = Mathf.Max(10f, win.height);
                if (sp != null && sp.rect.height > 0f)
                {
                    float aspect = sp.rect.width / sp.rect.height;
                    float w = ww, h = ww / aspect;
                    if (h < wh)
                    {
                        h = wh;
                        w = wh * aspect;
                    }
                    _art.sprite = sp;
                    _art.color = Color.white;
                    _art.preserveAspect = false;
                    _artRt.sizeDelta = new Vector2(w, h);
                    _artBaseY = -(0.5f - ArtFocus) * h;
                    _artRange = Mathf.Max(0f, (h - wh) * 0.5f - Mathf.Abs(_artBaseY));
                }
                else
                {
                    _art.sprite = UISprites.Get("ui_gradient_v");
                    _art.color = Color.white;
                    _artRt.sizeDelta = new Vector2(ww, wh);
                    _artBaseY = 0f;
                    _artRange = 0f;
                    // Accent-tinted backdrop: tint via the face color behind (the gradient is white → transparent).
                    _art.color = DS.Lighten(accent, 0.35f);
                }
                _artRt.anchoredPosition = new Vector2(0f, _artBaseY);
            }

            public void SetParallax(float offsetFromCenter)
            {
                if (_artRt == null) return;
                float shift = Mathf.Clamp(offsetFromCenter * Parallax, -_artRange, _artRange);
                _artRt.anchoredPosition = new Vector2(0f, _artBaseY + shift);
            }

            void ApplyIdle()
            {
                _pulse?.Kill();
                _glowPulse?.Kill();
                _pulse = _glowPulse = null;
                Tween.Kill(Root);
                Root.localScale = Vector3.one;
                bool current = _state == WorldState.Current;
                _glow.gameObject.SetActive(current);
                if (!current) return;
                _pulse = Tween.Scale(Root, 1.025f, 0.8f, Ease.InOutSine).SetLoops(-1, true);
                _glow.transform.localScale = Vector3.one * 0.96f;
                _glowPulse = Tween.Scale(_glow.transform, 1.05f, 0.8f, Ease.InOutSine).SetLoops(-1, true);
                Tween.Kill(_luna);
                _luna.anchoredPosition = LunaPos;
                Tween.Bob(_luna, 7f, 1.8f);
            }

            public void PlayEntrance(float delay)
            {
                if (_group == null) return;
                Tween.Kill(Visual);
                Visual.anchoredPosition = Vector2.zero;
                CommonUI.SlideIn(Visual, _group, new Vector2(0f, -90f), delay, 0.42f);
                Visual.localScale = Vector3.one * 0.9f;
                Tween.Scale(Visual, 1f, 0.45f, Ease.OutBack).SetOvershoot(1.6f).SetDelay(delay);
                if (_state == WorldState.Completed && _check != null) CommonUI.PopIn(_check, delay + 0.3f);
                if (_state == WorldState.Current && _currentTag != null) CommonUI.PopIn(_currentTag, delay + 0.25f);
            }
        }

        // ======================================================================================== level map

        enum NodeState { Won, Current, Locked }

        sealed class LevelMap
        {
            const float NodeSize = 150f, Spacing = 210f, TopPad = 150f, FinishGap = 1.25f, Freq = 0.84f, Phase = -0.95f;
            const float RoadWidth = 78f, DoneWidth = 30f, DotSpacing = 46f, InfoH = 70f;

            readonly WorldsScreen _owner;
            public RectTransform Panel;
            public bool IsOpen { get; private set; }
            public int Area { get; private set; } = -1;

            CanvasGroup _group;
            Image _bg, _tint;
            AspectRatioFitter _bgFit;
            string _bgSprite;
            RectTransform _ribbon, _info, _back;
            TMP_Text _title, _infoText;
            ScrollRect _scroll;
            RectTransform _content, _decoLayer, _dotLayer, _nodeLayer;
            TrailGraphic _road, _done;
            readonly List<Vector2> _pathPoints = new List<Vector2>(512);
            readonly List<float> _pathT = new List<float>(512);
            readonly List<Image> _dots = new List<Image>();
            readonly List<float> _dotT = new List<float>();
            readonly MapNode[] _nodes = new MapNode[Areas.LevelsPerArea];
            readonly List<RectTransform> _decos = new List<RectTransform>();
            RectTransform _luna, _finish;
            Image _finishArt;
            TMP_Text _finishText;
            RectTransform _finishLock;
            float _contentW, _contentH, _amp;
            bool _built;
            TweenHandle _autoScroll;
            float _sparkle;
            readonly Vector3[] _corners = new Vector3[4];
            readonly Vector3[] _viewCorners = new Vector3[4];

            public LevelMap(WorldsScreen owner) { _owner = owner; }

            // ------------------------------------------------------------------------ build

            void Build()
            {
                _built = true;
                var root = _owner.Root;
                Panel = UIKit.Stretch(UIKit.Rect("LevelMap", root));
                Panel.SetAsLastSibling();
                _group = Panel.gameObject.AddComponent<CanvasGroup>();

                // Backdrop: the world's blurred interior, tinted towards plum, with a vignette.
                _bg = UIKit.Backdrop(Panel, "");
                _bgFit = _bg != null ? _bg.GetComponent<AspectRatioFitter>() : null;
                if (_bg != null && _bgFit == null)
                {
                    _bgFit = _bg.gameObject.AddComponent<AspectRatioFitter>();
                    _bgFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    _bgFit.aspectRatio = 9f / 16f;
                }
                var tintHolder = UIKit.FullBleed(UIKit.Rect("Tint", Panel), 40f);
                tintHolder.SetSiblingIndex(1);
                _tint = UIKit.NewImage(tintHolder, "Tint", UISprites.Pixel, DS.WithAlpha(DS.Colors.Ink, 0.35f));
                UIKit.Stretch(_tint.rectTransform);
                var vignette = UISprites.Get("ui_vignette");
                if (vignette != null)
                {
                    var v = UIKit.NewImage(tintHolder, "Vignette", vignette, DS.WithAlpha(DS.Colors.Ink, 0.7f));
                    UIKit.Stretch(v.rectTransform);
                }
                var floaters = AmbientFloaters.Create(Panel, 14, Color.Lerp(DS.Colors.BrandLight, Color.white, 0.5f));
                floaters.transform.SetSiblingIndex(2);

                float ribbonH = UIKit.RibbonHeight(RibbonWidth);
                float bodyTop = TopReserved + RibbonGap + ribbonH * 0.88f;

                // Road scroll (between the info strip and the bottom nav), laid out by hand (no layout group).
                _scroll = PlainScroll(Panel, out _content);
                var srt = (RectTransform)_scroll.transform;
                UIKit.Stretch(srt, 0f, bodyTop + InfoH + DS.Space.XS, 0f, BottomReserved);
                ScrollDragWatcher.Attach(_scroll, StopAutoScroll);

                // Header: ribbon with the world name, back button, info strip.
                _ribbon = UIKit.Ribbon(Panel, "worlds.title", RibbonWidth);
                UIKit.Place(_ribbon, new Vector2(0.5f, 1f), new Vector2(RibbonWidth, ribbonH), new Vector2(0f, -(TopReserved + RibbonGap)));
                var title = _ribbon.Find("Title");
                _title = title != null ? title.GetComponent<TMP_Text>() : null;
                var loc = title != null ? title.GetComponent<LocText>() : null;
                if (loc != null) loc.Clear();

                var back = UIKit.IconButton(Panel, "icon_arrow_back", 116f, Close, "btn_square");
                _back = (RectTransform)back.transform;
                float bandCenter = TopReserved + RibbonGap + ribbonH * 0.35f;
                UIKit.Place(_back, new Vector2(0f, 1f), new Vector2(116f, 116f), new Vector2(DS.Space.ScreenMargin - 6f, -(bandCenter - 58f)));
                back.ExpandHitArea();

                _info = CommonUI.GlassPill(Panel, new Vector2(560f, InfoH), "icon_star", out _infoText);
                UIKit.Place(_info, new Vector2(0.5f, 1f), new Vector2(560f, InfoH), new Vector2(0f, -bodyTop));
                _infoText.alignment = TextAlignmentOptions.Center;

                BuildRoad();
                Panel.gameObject.SetActive(false);
            }

            /// <summary>Lays the road, nodes and decorations out for the current width.</summary>
            void BuildRoad()
            {
                var sm = ScreenManager.Instance;
                _contentW = sm != null ? sm.SafeSize.x : DS.Space.ReferenceWidth;
                _amp = Mathf.Clamp(_contentW * 0.5f - NodeSize * 0.5f - 120f, 120f, 300f);
                float endT = Areas.LevelsPerArea - 1 + FinishGap;
                _contentH = TopPad + endT * Spacing + 330f;
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.sizeDelta = new Vector2(0f, _contentH);
                _content.anchoredPosition = Vector2.zero;

                var roadLayer = UIKit.Rect("Road", _content);
                UIKit.Place(roadLayer, new Vector2(0.5f, 1f), new Vector2(_contentW, _contentH), Vector2.zero);
                roadLayer.pivot = new Vector2(0.5f, 1f);
                // TrailGraphic draws in the local space of its rect: the rect is stretched over the layer, whose
                // origin (pivot top-center) is the path origin, so offset the points by the rect center.
                _road = TrailGraphic.Create(roadLayer, "RoadBase", RoadWidth, DS.WithAlpha(Color.white, 0.22f));
                _done = TrailGraphic.Create(roadLayer, "RoadDone", DoneWidth, DS.WithAlpha(DS.Colors.StarGold, 0.95f));

                _decoLayer = UIKit.Rect("Decorations", _content);
                UIKit.Place(_decoLayer, new Vector2(0.5f, 1f), new Vector2(_contentW, _contentH), Vector2.zero);
                _decoLayer.pivot = new Vector2(0.5f, 1f);
                _dotLayer = UIKit.Rect("Dots", _content);
                UIKit.Place(_dotLayer, new Vector2(0.5f, 1f), new Vector2(_contentW, _contentH), Vector2.zero);
                _dotLayer.pivot = new Vector2(0.5f, 1f);
                _nodeLayer = UIKit.Rect("Nodes", _content);
                UIKit.Place(_nodeLayer, new Vector2(0.5f, 1f), new Vector2(_contentW, _contentH), Vector2.zero);
                _nodeLayer.pivot = new Vector2(0.5f, 1f);

                // Sample the path densely (arc-length spacing for the dotted trail).
                _pathPoints.Clear();
                _pathT.Clear();
                _dots.Clear();
                _dotT.Clear();
                Vector2 prev = Point(0f);
                float acc = 0f, nextDot = DotSpacing * 0.5f;
                Vector2 centerOffset = new Vector2(0f, _contentH * 0.5f);   // layer pivot top → rect center
                for (float t = 0f; t <= endT + 0.0001f; t += 0.025f)
                {
                    Vector2 p = Point(t);
                    acc += (p - prev).magnitude;
                    prev = p;
                    _pathPoints.Add(p + centerOffset);
                    _pathT.Add(t);
                    if (acc < nextDot) continue;
                    nextDot += DotSpacing;
                    float nearestNode = Mathf.Clamp(Mathf.Round(t), 0f, Areas.LevelsPerArea - 1);
                    if ((p - Point(nearestNode)).magnitude < NodeSize * 0.62f) continue;   // hidden under a node
                    var dot = UIKit.NewImage(_dotLayer, "Dot", UISprites.Circle, Color.white);
                    UIKit.Place(dot.rectTransform, new Vector2(0.5f, 1f), new Vector2(18f, 18f), p);
                    dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    dot.rectTransform.anchoredPosition = p;
                    _dots.Add(dot);
                    _dotT.Add(t);
                }
                _road.SetPoints(_pathPoints);

                // Nodes.
                for (int i = 0; i < _nodes.Length; i++)
                {
                    _nodes[i] = MapNode.Create(this, _nodeLayer, i);
                    _nodes[i].Root.anchoredPosition = Point(i);
                }

                // Luna (beside the current node).
                _luna = UIKit.Rect("Luna", _nodeLayer);
                UIKit.Place(_luna, new Vector2(0.5f, 1f), new Vector2(124f, 124f), Vector2.zero);
                _luna.pivot = new Vector2(0.5f, 0.5f);
                var lunaGlow = UIKit.NewImage(_luna, "Glow", UISprites.Glow, DS.WithAlpha(DS.Colors.StarGold, 0.6f));
                UIKit.Stretch(lunaGlow.rectTransform, -40f, -40f, -40f, -40f);
                var lunaAvatar = CommonUI.Avatar(_luna, "avatar_luna", 124f, DS.Colors.StarGold);
                UIKit.Stretch(lunaAvatar);

                // Album cards along the roadside.
                for (int i = 0; i < 9; i++)
                {
                    var deco = UIKit.Rect("Deco" + i, _decoLayer);
                    UIKit.Place(deco, new Vector2(0.5f, 1f), new Vector2(112f, 112f * CommonUI.CardAspect), Vector2.zero);
                    deco.pivot = new Vector2(0.5f, 0.5f);
                    float t = 0.9f + i * ((Areas.LevelsPerArea - 2.2f) / 8f);
                    Vector2 p = Point(t);
                    float side = Mathf.Sin(t * Freq + Phase) >= 0f ? -1f : 1f;
                    float x = Mathf.Clamp(side * (_amp + 30f) * 0.95f + side * 40f, -_contentW * 0.5f + 90f, _contentW * 0.5f - 90f);
                    deco.anchoredPosition = new Vector2(x, p.y);
                    deco.localEulerAngles = new Vector3(0f, 0f, side * -8f);
                    int index = i;
                    var visual = UIKit.Stretch(UIKit.Rect("Visual", deco));
                    CommonUI.MakeTappable(deco, visual, () => OnDecoTapped(index));
                    _decos.Add(deco);
                }

                // The next world at the end of the road.
                _finish = UIKit.Rect("Finish", _nodeLayer);
                Vector2 fp = Point(endT);
                UIKit.Place(_finish, new Vector2(0.5f, 1f), new Vector2(360f, 300f), Vector2.zero);
                _finish.pivot = new Vector2(0.5f, 0.5f);
                _finish.anchoredPosition = fp + new Vector2(0f, -40f);
                var fVisual = UIKit.Stretch(UIKit.Rect("Visual", _finish));
                UIKit.Shadow(fVisual, 26f, -12f, 0.35f);
                var fFrame = UIKit.RoundedRect(fVisual, Vector2.zero, DS.Colors.StarGold, 40f);
                UIKit.Stretch(fFrame.rectTransform, 0f, 0f, 0f, 70f);
                var fWindow = UIKit.Rect("Window", fVisual);
                UIKit.Stretch(fWindow, 12f, 12f, 12f, 82f);
                var fMask = fWindow.gameObject.AddComponent<Image>();
                fMask.sprite = UISprites.Rounded;
                fMask.raycastTarget = false;
                SliceFit.Attach(fMask, SliceFit.Mode.Corner, 30f * UISprites.RoundedBorderPerRadius);
                fWindow.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                _finishArt = UIKit.NewImage(fWindow, "Art", null, Color.white);
                _finishArt.rectTransform.anchorMin = _finishArt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _finishLock = UIKit.Image(fVisual, "icon_lock", new Vector2(110f, 110f)).rectTransform;
                UIKit.Place(_finishLock, new Vector2(0.5f, 1f), new Vector2(110f, 110f), new Vector2(0f, -55f));
                var fPlate = UIKit.Capsule(fVisual, new Vector2(380f, 70f), DS.WithAlpha(DS.Colors.Ink, 0.8f));
                UIKit.Place(fPlate.rectTransform, new Vector2(0.5f, 0f), new Vector2(380f, 70f), new Vector2(0f, 0f));
                _finishText = CommonUI.Label(fPlate.rectTransform, "", TextStyle.H3, new Vector2(360f, 62f), TextAlignmentOptions.Center, 36f);
                UIKit.Stretch(_finishText.rectTransform, 18f, 2f, 18f, 4f);
                CommonUI.MakeTappable(_finish, fVisual, OnFinishTapped);
            }

            /// <summary>Vertical scroll view without layout group; the viewport fades its content out at both edges.</summary>
            static ScrollRect PlainScroll(Transform parent, out RectTransform content)
            {
                var root = UIKit.Rect("RoadScroll", parent);
                var viewport = UIKit.Stretch(UIKit.Rect("Viewport", root));
                var mask = viewport.gameObject.AddComponent<RectMask2D>();
                mask.softness = new Vector2Int(0, 48);
                viewport.gameObject.AddComponent<HitArea>();   // drag anywhere, even between the nodes
                content = UIKit.Rect("Content", viewport);
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.sizeDelta = Vector2.zero;
                content.anchoredPosition = Vector2.zero;
                var scroll = root.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.elasticity = 0.1f;
                scroll.inertia = true;
                scroll.decelerationRate = 0.135f;
                scroll.scrollSensitivity = 40f;
                return scroll;
            }

            /// <summary>Path position of parameter t (t = node index; 0 = first level), content space (y down from top).</summary>
            Vector2 Point(float t) => new Vector2(_amp * Mathf.Sin(t * Freq + Phase), -(TopPad + t * Spacing));

            public void Rebuild()
            {
                if (!_built) return;
                bool open = IsOpen;
                int area = Area;
                Object.Destroy(Panel.gameObject);
                Panel = null;
                _built = false;
                IsOpen = false;
                _decos.Clear();
                _bgSprite = null;
                if (open && area >= 0) Open(area, null, true);
            }

            // ------------------------------------------------------------------------ open / close

            public void Open(int areaNumber, RectTransform from, bool instant)
            {
                if (!_built) Build();
                // Hopping from one world's map to the next (end of the road): no fade, the content pops again.
                bool hop = IsOpen && Panel.gameObject.activeSelf && _group.alpha > 0.99f;
                Area = Mathf.Max(0, areaNumber);
                IsOpen = true;
                Panel.gameObject.SetActive(true);
                Panel.SetAsLastSibling();
                Tween.Kill(_group);
                Tween.Kill(Panel);
                _group.blocksRaycasts = true;
                Refresh(true);

                AudioManager.Play(Sfx.Swoosh);
                Haptics.Play(HapticType.Light);
                if (instant || hop)
                {
                    _group.alpha = 1f;
                    Panel.localScale = Vector3.one;
                    _owner.SetListVisible(false);
                }
                else
                {
                    _group.alpha = 0f;
                    Panel.localScale = Vector3.one * 1.06f;
                    Tween.Fade(_group, 1f, 0.28f, Ease.OutQuad).OnComplete(() =>
                    {
                        if (IsOpen && _owner != null) _owner.SetListVisible(false);
                    });
                    Tween.Scale(Panel, 1f, 0.38f, Ease.OutCubic);
                }
                CommonUI.PopIn(_ribbon, 0.05f);
                _ribbon.localRotation = Quaternion.Euler(0f, 0f, -4f);
                Tween.Rotate(_ribbon, 0f, 0.5f, Ease.OutBack).SetDelay(0.05f);
                CommonUI.PopIn(_back, 0.12f);
                CommonUI.PopIn(_info, 0.16f);

                int focus = FocusIndex();
                for (int i = 0; i < _nodes.Length; i++)
                    _nodes[i].PlayEntrance(0.12f + Mathf.Min(10, Mathf.Abs(i - focus)) * 0.035f);
                for (int i = 0; i < _decos.Count; i++) CommonUI.PopIn(_decos[i], 0.3f + i * 0.04f);
                ScrollToNode(focus, true);
            }

            public void Close()
            {
                if (!IsOpen) return;
                IsOpen = false;
                StopAutoScroll();
                AudioManager.Play(Sfx.Swoosh, 0.8f);
                _owner.SetListVisible(true);
                _group.blocksRaycasts = false;
                Tween.Kill(_group);
                Tween.Kill(Panel);
                var panel = Panel;
                Tween.Fade(_group, 0f, 0.22f, Ease.InQuad).OnComplete(() =>
                {
                    if (panel != null && !IsOpen) panel.gameObject.SetActive(false);
                });
                Tween.Scale(Panel, 1.05f, 0.22f, Ease.InQuad);
                _owner.Refresh(false);
            }

            public void CloseImmediate()
            {
                if (!_built) return;
                IsOpen = false;
                StopAutoScroll();
                Tween.Kill(_group);
                Tween.Kill(Panel);
                _group.alpha = 0f;
                Panel.localScale = Vector3.one;
                Panel.gameObject.SetActive(false);
            }

            void StopAutoScroll()
            {
                _autoScroll?.Kill();
                _autoScroll = null;
            }

            // ------------------------------------------------------------------------ refresh

            public void Refresh(bool full)
            {
                if (!_built || Area < 0) return;
                int area = Area;
                var theme = Areas.AreaForNumber(area);
                Color accent = theme != null ? theme.accent : DS.Colors.Brand;

                // Backdrop + tint.
                string bg = theme != null ? theme.GameBackground : null;
                if (_bg != null && bg != _bgSprite)
                {
                    _bgSprite = bg;
                    var sp = !string.IsNullOrEmpty(bg) && UISprites.Exists(bg) ? UISprites.Get(bg) : null;
                    if (sp != null)
                    {
                        _bg.sprite = sp;
                        _bg.color = Color.white;
                        if (_bgFit != null && sp.rect.height > 0f) _bgFit.aspectRatio = sp.rect.width / sp.rect.height;
                    }
                    else
                    {
                        _bg.sprite = null;
                        _bg.color = DS.Darken(accent, 0.3f);
                    }
                }
                _tint.color = DS.WithAlpha(Color.Lerp(DS.Colors.Ink, accent, 0.18f), 0.42f);

                // Header.
                if (_title != null) _title.text = MetaUI.WorldName(area);
                int first = Areas.FirstLevelOfArea(area);
                if (_infoText != null)
                    _infoText.text = Loc.T("worlds.map_info", Progress.StarsInArea(area), Areas.MaxStarsPerArea, first, first + Areas.LevelsPerArea - 1);

                // Road progress: solid gold up to the current level, dots beyond.
                int current = Progress.CurrentLevel;
                float doneT = Mathf.Clamp(current - first, 0, Areas.LevelsPerArea - 1);
                if (Progress.StateOfArea(area) == WorldState.Completed) doneT = Areas.LevelsPerArea - 1 + FinishGap;
                int doneCount = 0;
                while (doneCount < _pathT.Count && _pathT[doneCount] <= doneT + 0.0001f) doneCount++;
                _done.SetPoints(_pathPoints, doneT > 0f ? doneCount : 0);
                _done.color = DS.WithAlpha(DS.Colors.StarGold, 0.95f);
                for (int i = 0; i < _dots.Count; i++)
                {
                    bool done = _dotT[i] <= doneT;
                    _dots[i].color = done ? DS.WithAlpha(Color.white, 0f) : DS.WithAlpha(Color.white, 0.75f);
                }

                // Nodes.
                MapNode currentNode = null;
                for (int i = 0; i < _nodes.Length; i++)
                {
                    int level = first + i;
                    NodeState state = level < current ? NodeState.Won : level == current ? NodeState.Current : NodeState.Locked;
                    _nodes[i].Bind(level, state, Progress.BestStars(level), Difficulty.IsHard(level), accent);
                    if (state == NodeState.Current) currentNode = _nodes[i];
                }

                // Luna beside the current level (inner side of the curve).
                if (_luna != null)
                {
                    _luna.gameObject.SetActive(currentNode != null);
                    if (currentNode != null)
                    {
                        Vector2 p = currentNode.Root.anchoredPosition;
                        float side = p.x > 0f ? -1f : 1f;
                        var target = p + new Vector2(side * (NodeSize * 0.5f + 92f), 26f);
                        Tween.Kill(_luna);
                        _luna.anchoredPosition = target;
                        Tween.Bob(_luna, 8f, 1.9f);
                    }
                }

                RefreshDecorations(theme);
                RefreshFinish(area);
            }

            void RefreshDecorations(AreaInfo theme)
            {
                var cards = theme != null ? theme.cardIds : null;
                for (int i = 0; i < _decos.Count; i++)
                {
                    var deco = _decos[i];
                    bool has = cards != null && i < cards.Length && !string.IsNullOrEmpty(cards[i]);
                    deco.gameObject.SetActive(has);
                    if (!has) continue;
                    var visual = deco.Find("Visual") as RectTransform;
                    if (visual == null) continue;
                    string id = cards[i];
                    bool owned = Collection.Has(id);
                    string key = (owned ? "o:" : "m:") + id;
                    if (visual.name == key) continue;   // already showing this face
                    for (int k = visual.childCount - 1; k >= 0; k--)
                    {
                        var old = visual.GetChild(k).gameObject;
                        old.SetActive(false);   // Destroy is deferred: never show both faces for a frame
                        Object.Destroy(old);
                    }
                    visual.name = key;
                    var face = CommonUI.MiniCard(visual, id, 112f, owned);
                    if (face != null) UIKit.Place(face, new Vector2(0.5f, 0.5f), face.sizeDelta, Vector2.zero);
                    var group = visual.GetComponent<CanvasGroup>();
                    if (group == null) group = visual.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = owned ? 1f : 0.8f;
                    Tween.Kill(visual);
                    visual.anchoredPosition = Vector2.zero;
                    Tween.Bob(visual, 6f, 2.2f + i * 0.17f);
                }
            }

            void RefreshFinish(int area)
            {
                if (_finish == null) return;
                int next = area + 1;
                var theme = Areas.AreaForNumber(next);
                bool reached = Progress.StateOfArea(next) != WorldState.Locked;
                string sprite = theme != null ? theme.HomeBackground : null;
                var sp = !string.IsNullOrEmpty(sprite) && UISprites.Exists(sprite) ? UISprites.Get(sprite) : null;
                var rt = _finishArt.rectTransform;
                if (sp != null && sp.rect.height > 0f)
                {
                    float aspect = sp.rect.width / sp.rect.height;
                    float w = 336f;
                    _finishArt.sprite = sp;
                    rt.sizeDelta = new Vector2(w, w / aspect);
                    rt.anchoredPosition = new Vector2(0f, -(0.5f - ArtFocus) * (w / aspect));
                }
                else
                {
                    _finishArt.sprite = UISprites.Get("ui_gradient_v");
                    rt.sizeDelta = new Vector2(336f, 206f);
                    rt.anchoredPosition = Vector2.zero;
                }
                _finishArt.color = reached ? Color.white : new Color(0.58f, 0.54f, 0.68f, 1f);
                _finishLock.gameObject.SetActive(!reached);
                _finishText.text = Loc.T("worlds.next_world", MetaUI.WorldName(next));
            }

            int FocusIndex()
            {
                int first = Areas.FirstLevelOfArea(Area);
                int current = Progress.CurrentLevel;
                if (current >= first && current < first + Areas.LevelsPerArea) return current - first;
                // Completed world: the first level still missing stars (else the top).
                for (int i = 0; i < Areas.LevelsPerArea; i++)
                    if (Progress.BestStars(first + i) < 3) return i;
                return 0;
            }

            void ScrollToNode(int index, bool animate)
            {
                if (_scroll == null) return;
                StopAutoScroll();
                _scroll.StopMovement();
                Canvas.ForceUpdateCanvases();
                float viewH = _scroll.viewport.rect.height;
                float maxY = Mathf.Max(0f, _contentH - viewH);
                float y = TopPad + Mathf.Clamp(index, 0, Areas.LevelsPerArea - 1) * Spacing;
                float target = Mathf.Clamp(y - viewH * 0.48f, 0f, maxY);
                if (!animate)
                {
                    _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, target);
                    return;
                }
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Clamp(target - 260f, 0f, maxY));
                float from = _content.anchoredPosition.y;
                _autoScroll = Tween.Value(from, target, 0.8f, v =>
                {
                    if (_content != null) _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, v);
                }, Ease.OutCubic).SetDelay(0.12f).SetLink(Panel);
            }

            // ------------------------------------------------------------------------ taps

            void OnNodeTapped(MapNode node)
            {
                if (node == null || !IsOpen) return;
                int level = node.Level;
                int current = Progress.CurrentLevel;
                if (level > current)
                {
                    CommonUI.Deny(node.Visual);
                    UIKit.Toast(Loc.T("worlds.locked_level", current));
                    return;
                }
                StopAutoScroll();
                Tween.Punch(node.Visual, 0.12f, 0.3f);
                var sm = ScreenManager.Instance;
                if (sm != null && sm.FxLayer != null) FX.Ring(sm.FxLayer, FX.LocalCenterOf(node.Visual), DS.Colors.Accent, 360f);
                LevelStartPopup.Open(level, level < current);
            }

            void OnDecoTapped(int index)
            {
                var theme = Areas.AreaForNumber(Area);
                var cards = theme != null ? theme.cardIds : null;
                if (cards == null || index < 0 || index >= cards.Length) return;
                var deco = index < _decos.Count ? _decos[index] : null;
                if (Collection.Has(cards[index]))
                {
                    if (deco != null) Tween.Punch(deco, 0.12f, 0.3f);
                    CardZoomPopup.Open(cards[index]);
                }
                else
                {
                    if (deco != null) CommonUI.Deny(deco);
                    UIKit.Toast(Loc.T("tabs.col.missing_card"));
                }
            }

            void OnFinishTapped()
            {
                int next = Area + 1;
                if (Progress.StateOfArea(next) == WorldState.Locked)
                {
                    CommonUI.Deny(_finish);
                    UIKit.Toast(Loc.T("worlds.reach_level", Areas.FirstLevelOfArea(next)));
                    return;
                }
                // Hop to the next world's map.
                Tween.Punch(_finish, 0.1f, 0.25f);
                Open(next, null, false);
            }

            // ------------------------------------------------------------------------ idle

            public void Tick(float dt)
            {
                if (!IsOpen || _luna == null || !_luna.gameObject.activeInHierarchy) return;
                _sparkle -= dt;
                if (_sparkle > 0f) return;
                _sparkle = Random.Range(1.4f, 2.4f);
                var sm = ScreenManager.Instance;
                if (sm == null || sm.FxLayer == null || PopupManager.AnyOpen || sm.IsTransitioning) return;
                if (!IsInView(_luna)) return;
                FX.Sparkles(sm.FxLayer, FX.LocalCenterOf(_luna) + new Vector2(Random.Range(-60f, 60f), Random.Range(-20f, 60f)), 3);
            }

            bool IsInView(RectTransform rt)
            {
                if (_scroll == null || rt == null) return false;
                rt.GetWorldCorners(_corners);
                float y = (_corners[0].y + _corners[1].y) * 0.5f;
                _scroll.viewport.GetWorldCorners(_viewCorners);
                return y > _viewCorners[0].y && y < _viewCorners[1].y;
            }

            // ------------------------------------------------------------------------ node

            sealed class MapNode
            {
                public RectTransform Root, Visual;
                public int Level;

                LevelMap _map;
                Image _glow, _ring, _lip, _face;
                TMP_Text _number;
                RectTransform _stars, _skull, _lock, _playTag, _halo;
                TweenHandle _pulse, _haloSpin;
                NodeState _state = (NodeState)(-1);

                public static MapNode Create(LevelMap map, RectTransform parent, int index)
                {
                    var n = new MapNode { _map = map };
                    var root = UIKit.Rect("Node" + (index + 1), parent);
                    UIKit.Place(root, new Vector2(0.5f, 1f), new Vector2(NodeSize, NodeSize), Vector2.zero);
                    root.pivot = new Vector2(0.5f, 0.5f);
                    n.Root = root;
                    var visual = UIKit.Stretch(UIKit.Rect("Visual", root));
                    n.Visual = visual;

                    bool rays = UISprites.Exists("sunburst");
                    var halo = rays ? UIKit.Image(visual, "sunburst", new Vector2(NodeSize * 2.1f, NodeSize * 2.1f))
                        : UIKit.NewImage(visual, "Halo", UISprites.Glow, Color.white);
                    n._halo = halo.rectTransform;
                    UIKit.Place(n._halo, new Vector2(0.5f, 0.5f), new Vector2(NodeSize * 2.1f, NodeSize * 2.1f), Vector2.zero);
                    halo.color = rays ? DS.WithAlpha(Color.white, 0.7f) : DS.WithAlpha(DS.Colors.StarGold, 0.45f);
                    n._halo.gameObject.SetActive(false);
                    n._glow = UIKit.NewImage(visual, "Glow", UISprites.Glow, DS.WithAlpha(DS.Colors.StarGold, 0.85f));
                    UIKit.Stretch(n._glow.rectTransform, -46f, -46f, -46f, -46f);
                    n._glow.gameObject.SetActive(false);
                    var shadow = UIKit.NewImage(visual, "Shadow", UISprites.Circle, DS.WithAlpha(DS.Colors.Ink, 0.4f));
                    UIKit.Stretch(shadow.rectTransform, 4f, 14f, 4f, -10f);
                    n._ring = UIKit.NewImage(visual, "Ring", UISprites.Circle, Color.white);
                    UIKit.Stretch(n._ring.rectTransform);
                    n._lip = UIKit.NewImage(visual, "Lip", UISprites.Circle, DS.Colors.BrandDark);
                    UIKit.Stretch(n._lip.rectTransform, 10f, 10f, 10f, 10f);
                    n._face = UIKit.NewImage(visual, "Face", UISprites.Circle, DS.Colors.Brand);
                    UIKit.Stretch(n._face.rectTransform, 10f, 10f, 10f, 19f);
                    var gloss = UIKit.NewImage(visual, "Gloss", UISprites.Circle, DS.WithAlpha(Color.white, 0.3f));
                    UIKit.Place(gloss.rectTransform, new Vector2(0.5f, 1f), new Vector2(NodeSize * 0.56f, NodeSize * 0.26f), new Vector2(0f, -18f));
                    n._number = CommonUI.Label(visual, "", TextStyle.H2, new Vector2(NodeSize - 30f, NodeSize - 40f), TextAlignmentOptions.Center, 60f);
                    UIKit.Stretch(n._number.rectTransform, 14f, 16f, 14f, 26f);

                    n._skull = UIKit.Image(visual, "icon_skull", new Vector2(70f, 70f)).rectTransform;
                    UIKit.Place(n._skull, new Vector2(0.5f, 1f), new Vector2(70f, 70f), new Vector2(0f, 34f));
                    n._lock = UIKit.Image(visual, "icon_lock", new Vector2(58f, 58f)).rectTransform;
                    UIKit.Place(n._lock, new Vector2(1f, 0f), new Vector2(58f, 58f), new Vector2(6f, -4f));

                    n._stars = MetaUI.StarRow(visual, 0, 48f, -6f, 12f);
                    UIKit.Place(n._stars, new Vector2(0.5f, 0f), n._stars.sizeDelta, new Vector2(0f, -54f));
                    n._playTag = MetaUI.Tag(visual, "ui.play", DS.Colors.Pink, null, new Vector2(170f, 62f), 36f);
                    UIKit.Place(n._playTag, new Vector2(0.5f, 0f), n._playTag.sizeDelta, new Vector2(0f, -66f));

                    var node = n;
                    CommonUI.MakeTappable(root, visual, () => map.OnNodeTapped(node));
                    return n;
                }

                public void Bind(int level, NodeState state, int best, bool hard, Color accent)
                {
                    Level = level;
                    _number.text = Loc.Number(level);
                    Color face;
                    switch (state)
                    {
                        case NodeState.Current: face = hard ? DS.Colors.Brand : DS.Colors.Primary; break;
                        case NodeState.Won: face = hard ? DS.Colors.Brand : accent; break;
                        default: face = DS.Colors.Gray; break;
                    }
                    _face.color = face;
                    _lip.color = DS.Darken(face, 0.4f);
                    bool perfect = state == NodeState.Won && best >= 3;
                    _ring.color = perfect ? DS.Colors.StarGold : (state == NodeState.Locked ? new Color(0.9f, 0.88f, 0.94f, 1f) : Color.white);
                    _number.color = state == NodeState.Locked ? new Color(1f, 1f, 1f, 0.8f) : Color.white;
                    _skull.gameObject.SetActive(hard);
                    var skullImg = _skull.GetComponent<Image>();
                    if (skullImg != null) skullImg.color = state == NodeState.Locked ? new Color(1f, 1f, 1f, 0.75f) : Color.white;
                    _lock.gameObject.SetActive(state == NodeState.Locked);
                    _stars.gameObject.SetActive(state == NodeState.Won);
                    if (state == NodeState.Won) MetaUI.SetStars(_stars, best);
                    _playTag.gameObject.SetActive(state == NodeState.Current);

                    if (state == _state) return;
                    _state = state;
                    _pulse?.Kill();
                    _haloSpin?.Kill();
                    _pulse = _haloSpin = null;
                    Tween.Kill(_glow.transform);
                    Tween.Kill(Root);
                    Root.localScale = Vector3.one;
                    bool current = state == NodeState.Current;
                    _glow.gameObject.SetActive(current);
                    _halo.gameObject.SetActive(current);
                    if (!current) return;
                    _pulse = Tween.Scale(Root, 1.08f, 0.6f, Ease.InOutSine).SetLoops(-1, true);
                    _glow.transform.localScale = Vector3.one;
                    Tween.Scale(_glow.transform, 1.15f, 0.6f, Ease.InOutSine).SetLoops(-1, true);
                    _halo.localEulerAngles = Vector3.zero;
                    _haloSpin = Tween.Rotate(_halo, -360f, 12f, Ease.Linear).SetLoops(-1, false);
                    Tween.Kill(_playTag);
                    _playTag.localScale = Vector3.one;
                    Tween.Scale(_playTag, 1.07f, 0.5f, Ease.InOutSine).SetLoops(-1, true);
                }

                public void PlayEntrance(float delay)
                {
                    Tween.Kill(Visual);
                    Visual.localScale = Vector3.zero;
                    Tween.Scale(Visual, 1f, 0.42f, Ease.OutBack).SetOvershoot(2.2f).SetDelay(delay);
                    if (_state == NodeState.Won && _stars != null) MetaUI.PopStars(_stars, 3, delay + 0.25f, false);
                }
            }
        }
    }
}
