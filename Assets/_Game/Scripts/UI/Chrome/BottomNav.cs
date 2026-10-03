// ============================================================================================================
// Persistent bottom navigation (inside the safe-area bottom, its backdrop bleeding under the home indicator):
// Shop · Ranking · Home · Collection · Profile. Flat full-width violet bar with a gold top border and dark dividers;
// the selected tab is wider and sits on a lighter panel raised above the bar inside a rounded gold frame (art:
// BottomNavArt). Selecting slides the frame and eases the tab widths; the selected icon pops up bigger and shows its
// label, the others are icon-only. Red badges flag claimables (RefreshBadges).
// ============================================================================================================
using System;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Bottom navigation: Shop · Ranking · Home · Collection · Profile, with claimable badges.</summary>
    public class BottomNav : MonoBehaviour
    {
        public static BottomNav Instance { get; protected set; }

        // ---- additions
        public const float Height = DS.Space.BottomNavHeight;
        /// <summary>Tab order, left to right.</summary>
        public static readonly ScreenId[] TabIds = { ScreenId.Shop, ScreenId.Leaderboard, ScreenId.Home, ScreenId.Collection, ScreenId.Profile };
        public bool IsShown => _shown;

        static readonly string[] Icons = { "icon_shop", "icon_trophy", "icon_home", "icon_collection", "icon_profile" };
        static readonly string[] Labels = { "home.tab.shop", "home.tab.ranking", "home.tab.home", "home.tab.collection", "home.tab.profile" };

        const float SelectedWeight = 1.42f;     // selected tab width vs the others (26% of the bar for 5 tabs)
        const float BarCenterY = 100f;
        const float IconSize = 140f;
        const float SelectedIconY = 172f, SelectedScale = 1.32f;
        const float LabelY = 48f, LabelWidth = 240f, LabelSize = 46f;
        const float DividerCore = 5f, DividerLight = 3f, DividerShade = 1.5f;
        const float SlideSeconds = 0.36f, FrameDip = -14f;
        const float BadgeRefreshSeconds = 3f;

        sealed class Tab
        {
            public ScreenId id;
            public RectTransform rt, holder;
            public UIButton button;
            public TMP_Text label;
            public CanvasGroup labelGroup;
            public Badge badge;
        }

        Tab[] _tabs;
        RectTransform _root, _bar, _frame;
        RectTransform[] _dividers;
        CanvasGroup[] _dividerGroups;
        CanvasGroup _group;
        ScreenManager _sm;
        int _selected = -1;
        float[] _sel, _selFrom;        // how selected each tab is (1 = selected; overshoots a little while sliding)
        float _frameX = 0.5f, _frameFromX; // frame center, fraction of the bar width
        float _frameBottom, _frameDip;
        TweenHandle _slide, _dip;
        bool _shown = true;
        float _badgeTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        // ---------------------------------------------------------------------------------------- creation

        /// <summary>Builds the nav on `layer` (ScreenManager.ChromeLayer). Called by Chrome.Build.</summary>
        public static BottomNav Create(RectTransform layer)
        {
            if (layer == null) return null;
            var root = UIKit.Rect("BottomNav", layer);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(0f, Height);
            root.anchoredPosition = Vector2.zero;
            var nav = root.gameObject.AddComponent<BottomNav>();
            nav.Build(root);
            return nav;
        }

        void Build(RectTransform root)
        {
            Instance = this;
            _root = root;
            _sm = ScreenManager.Instance;
            _group = root.gameObject.AddComponent<CanvasGroup>();

            int n = TabIds.Length;
            _sel = new float[n];
            _selFrom = new float[n];

            // Bar: flat violet slab with the gold top border, continuing under the home indicator.
            var bar = UIKit.NewImage(root, "Bar", BottomNavArt.Bar, Color.white);
            SetSliced(bar);
            bar.raycastTarget = true;   // taps between tabs never fall through to the screen
            _bar = bar.rectTransform;

            // Dividers between tabs: dark line, light edge on its right.
            _dividers = new RectTransform[n - 1];
            _dividerGroups = new CanvasGroup[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var d = UIKit.Rect("Divider", root);
                Strip(d, new Color(0f, 0f, 0f, 0.2f), -DividerCore * 0.5f - DividerShade, -DividerCore * 0.5f);
                Strip(d, DS.Hex("1A0089"), -DividerCore * 0.5f, DividerCore * 0.5f);
                Strip(d, new Color(1f, 1f, 1f, 0.1f), DividerCore * 0.5f, DividerCore * 0.5f + DividerLight);
                _dividerGroups[i] = d.gameObject.AddComponent<CanvasGroup>();
                _dividerGroups[i].blocksRaycasts = false;
                _dividers[i] = d;
            }

            // Selected tab frame (slides between tabs).
            var frame = UIKit.NewImage(root, "SelectionFrame", BottomNavArt.Tile, Color.white);
            SetSliced(frame);
            frame.raycastTarget = true;   // the part raised above the bar never lets taps through to the screen below
            _frame = frame.rectTransform;
            LayoutBackdrop();

            // Tabs.
            _tabs = new Tab[TabIds.Length];
            for (int i = 0; i < TabIds.Length; i++) _tabs[i] = BuildTab(i);

            DailyRewards.OnChanged += RefreshBadges;
            Quests.OnChanged += RefreshBadges;
            StarChest.OnChanged += RefreshBadges;
            LuckySpin.OnChanged += RefreshBadges;
            ShopOffers.OnChanged += RefreshBadges;
            Collection.OnChanged += RefreshBadges;
            AuthService.OnAuthChanged += RefreshBadges;
            Progress.OnLevelChanged += OnLevelChanged;
            SaveSystem.OnReplaced += RefreshBadges;
            if (_sm != null) _sm.OnSafeAreaChanged += OnSafeAreaChanged;

            Select(ScreenId.Home, false);
            RefreshBadges();
        }

        Tab BuildTab(int i)
        {
            var tab = new Tab { id = TabIds[i] };
            var rt = UIKit.Rect("Tab_" + TabIds[i], _root);
            rt.anchorMin = new Vector2(i / (float)TabIds.Length, 0f);   // x follows the tab widths (ApplyLayout)
            rt.anchorMax = new Vector2((i + 1) / (float)TabIds.Length, 1f);
            // Hit cell = the bar itself. (Taller cells would steal taps from screen content just above the nav —
            // tab screens only reserve the bar height; the raised tile swallows its own taps, see Build.)
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            tab.rt = rt;

            int index = i;
            // Full-cell button (fixed hit rect) whose visual is the icon holder.
            var button = UIKit.IconButton(rt, null, 10f, () => OnTabClicked(index));
            UIKit.Stretch((RectTransform)button.transform);
            if (button.icon != null) button.icon.gameObject.SetActive(false);
            button.Pressable.target = null;
            tab.button = button;

            var holder = UIKit.Rect("Icon", rt);
            UIKit.Place(holder, new Vector2(0.5f, 0f), new Vector2(IconSize, IconSize), new Vector2(0f, BarCenterY - IconSize * 0.5f));
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = new Vector2(0f, BarCenterY);
            var icon = UIKit.Image(holder, Icons[i], new Vector2(IconSize, IconSize));
            UIKit.Stretch(icon.rectTransform);
            tab.holder = holder;
            button.Pressable.target = holder;

            tab.badge = UIKit.Badge(holder, new Vector2(-6f, -6f));

            var label = UIKit.LocText(rt, Labels[i], TextStyle.H3, new Vector2(LabelWidth, 60f));
            DS.Apply(label, TextStyle.H3, LabelSize);
            UIKit.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(LabelWidth, 60f), new Vector2(0f, LabelY - 30f));
            var lg = label.gameObject.AddComponent<CanvasGroup>();
            lg.alpha = 0f;
            tab.label = label;
            tab.labelGroup = lg;
            return tab;
        }

        void OnDestroy()
        {
            DailyRewards.OnChanged -= RefreshBadges;
            Quests.OnChanged -= RefreshBadges;
            StarChest.OnChanged -= RefreshBadges;
            LuckySpin.OnChanged -= RefreshBadges;
            ShopOffers.OnChanged -= RefreshBadges;
            Collection.OnChanged -= RefreshBadges;
            AuthService.OnAuthChanged -= RefreshBadges;
            Progress.OnLevelChanged -= OnLevelChanged;
            SaveSystem.OnReplaced -= RefreshBadges;
            if (_sm != null) _sm.OnSafeAreaChanged -= OnSafeAreaChanged;
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------------------------------- selection

        /// <summary>Highlights a tab (screens that are not tabs leave the current highlight).</summary>
        public void Select(ScreenId id, bool animate)
        {
            int index = Array.IndexOf(TabIds, id);
            if (index < 0 || _tabs == null) return;
            if (index == _selected)
            {
                if (animate) Bounce(_tabs[index]);
                return;
            }
            int prev = _selected;
            _selected = index;
            _slide?.Kill();
            _slide = null;
            Array.Copy(_sel, _selFrom, _sel.Length);
            _frameFromX = _frameX;
            if (animate && prev >= 0)
            {
                _slide = Tween.Value(0f, 1f, SlideSeconds, SetSlide, Ease.OutBack).SetOvershoot(1.1f).SetLink(this);
                // The frame dips while it slides and springs back up.
                _dip?.Kill();
                _dip = Tween.Value(FrameDip, 0f, 0.42f, v => { _frameDip = v; ApplyFrame(); }, Ease.OutBack).SetOvershoot(2f).SetLink(this);
            }
            else SetSlide(1f);

            for (int i = 0; i < _tabs.Length; i++) ApplyState(_tabs[i], i == index, animate);
        }

        void ApplyState(Tab tab, bool selected, bool animate)
        {
            var h = tab.holder;
            Tween.Kill(h);
            Tween.Kill(tab.labelGroup);
            var pos = new Vector2(0f, selected ? SelectedIconY : BarCenterY);
            float scale = selected ? SelectedScale : 1f;
            if (!animate)
            {
                h.anchoredPosition = pos;
                h.localScale = Vector3.one * scale;
                h.localEulerAngles = Vector3.zero;
                tab.labelGroup.alpha = selected ? 1f : 0f;
                return;
            }
            Tween.Move(h, pos, selected ? 0.38f : 0.25f, selected ? Ease.OutBack : Ease.OutCubic);
            if (selected)
            {
                h.localScale = Vector3.one * 0.85f;
                Tween.Scale(h, scale, 0.45f, Ease.OutBack).SetOvershoot(3f);
                h.localEulerAngles = new Vector3(0f, 0f, -10f);
                Tween.Rotate(h, 0f, 0.45f, Ease.OutElastic);
                Tween.Fade(tab.labelGroup, 1f, 0.2f).SetDelay(0.08f);
            }
            else
            {
                Tween.Scale(h, 1f, 0.25f, Ease.OutCubic);
                h.localEulerAngles = Vector3.zero;
                Tween.Fade(tab.labelGroup, 0f, 0.12f);
            }
        }

        /// <summary>Re-tap feedback: the icon springs back to its resting pose with a bounce (no Punch: it would
        /// capture the squashed press scale as its rest scale).</summary>
        void Bounce(Tab tab)
        {
            if (tab == null || tab.holder == null) return;
            var h = tab.holder;
            bool selected = _selected >= 0 && _tabs[_selected] == tab;
            float scale = selected ? SelectedScale : 1f;
            Tween.Kill(h);
            h.anchoredPosition = new Vector2(0f, selected ? SelectedIconY : BarCenterY);
            h.localEulerAngles = Vector3.zero;
            h.localScale = Vector3.one * scale * 0.82f;
            Tween.Scale(h, scale, 0.45f, Ease.OutBack).SetOvershoot(3f);
        }

        void SetSlide(float p)
        {
            for (int i = 0; i < _sel.Length; i++) _sel[i] = Mathf.LerpUnclamped(_selFrom[i], i == _selected ? 1f : 0f, p);
            _frameX = Mathf.LerpUnclamped(_frameFromX, CellCenter(_selected), p);
            ApplyLayout();
        }

        float Weight(int i) => 1f + (SelectedWeight - 1f) * _sel[i];

        float TotalWeight()
        {
            float total = 0f;
            for (int i = 0; i < _sel.Length; i++) total += Weight(i);
            return total;
        }

        /// <summary>Center of a tab, fraction of the bar width, with the current widths.</summary>
        float CellCenter(int index)
        {
            float x = 0f, total = TotalWeight();
            for (int i = 0; i < index; i++) x += Weight(i);
            return (x + Weight(index) * 0.5f) / total;
        }

        /// <summary>Tab cells by weight, dividers on their edges (hidden next to the selected tab), frame position.</summary>
        void ApplyLayout()
        {
            if (_tabs == null) return;
            float total = TotalWeight(), x = 0f;
            for (int i = 0; i < _tabs.Length; i++)
            {
                float w = Weight(i) / total;
                _tabs[i].rt.anchorMin = new Vector2(x, 0f);
                _tabs[i].rt.anchorMax = new Vector2(x + w, 1f);
                x += w;
                if (i >= _dividers.Length) continue;
                _dividers[i].anchorMin = _dividers[i].anchorMax = new Vector2(x, 0f);
                _dividerGroups[i].alpha = 1f - Mathf.Clamp01(Mathf.Max(_sel[i], _sel[i + 1]));
            }
            ApplyFrame();
        }

        void ApplyFrame()
        {
            if (_frame == null) return;
            float half = 0.5f * SelectedWeight / (SelectedWeight + TabIds.Length - 1);
            _frame.anchorMin = new Vector2(_frameX - half, 0f);
            _frame.anchorMax = new Vector2(_frameX + half, 0f);
            _frame.offsetMin = new Vector2(-BottomNavArt.TileMargin, _frameBottom + _frameDip);
            _frame.offsetMax = new Vector2(BottomNavArt.TileMargin, Height + BottomNavArt.Raise + BottomNavArt.ShadowH + _frameDip);
        }

        static void SetSliced(Image img)
        {
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
        }

        /// <summary>Vertical strip of a divider, from x0 to x1 around its center line.</summary>
        static void Strip(RectTransform parent, Color color, float x0, float x1)
        {
            var img = UIKit.NewImage(parent, "Strip", UISprites.Pixel, color);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(x0, 0f);
            rt.offsetMax = new Vector2(x1, 0f);
        }

        void OnTabClicked(int index)
        {
            if (_tabs == null || index < 0 || index >= _tabs.Length) return;
            var sm = ScreenManager.Instance;
            if (sm == null) return;
            var id = _tabs[index].id;
            if (sm.Current == id && sm.CurrentScreen != null && sm.CurrentScreen.IsVisible)
            {
                Bounce(_tabs[index]);
                return;
            }
            AudioManager.Play(Sfx.Swoosh, 0.6f);
            sm.Show(id);
        }

        // ---------------------------------------------------------------------------------------- visibility

        /// <summary>Slides the nav in/out (and blocks/unblocks its input).</summary>
        public void SetVisible(bool visible, bool animate)
        {
            _shown = visible;
            if (_root == null) return;
            Tween.Kill(_root);
            Tween.Kill(_group);
            float inset = _sm != null ? _sm.SafeInsets.y : 0f;
            var target = visible ? Vector2.zero : new Vector2(0f, -(Height + inset + 90f));
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
            if (!animate)
            {
                _root.anchoredPosition = target;
                _group.alpha = visible ? 1f : 0f;
                return;
            }
            Tween.Move(_root, target, visible ? 0.42f : 0.25f, visible ? Ease.OutBack : Ease.InCubic);
            Tween.Fade(_group, visible ? 1f : 0f, visible ? 0.2f : 0.25f);
            if (visible) RefreshBadges();
        }

        void OnSafeAreaChanged()
        {
            LayoutBackdrop();
            if (!_shown) SetVisible(false, false);
        }

        void LayoutBackdrop()
        {
            Vector4 i = _sm != null ? _sm.SafeInsets : Vector4.zero;
            _frameBottom = -i.y - BottomNavArt.Bleed;
            if (_bar != null)
            {
                _bar.anchorMin = Vector2.zero;
                _bar.anchorMax = Vector2.one;
                _bar.pivot = new Vector2(0.5f, 0.5f);
                _bar.offsetMin = new Vector2(-i.x - 6f, _frameBottom);
                _bar.offsetMax = new Vector2(i.z + 6f, BottomNavArt.ShadowH);
            }
            if (_dividers != null)
            {
                foreach (var d in _dividers)
                {
                    d.pivot = new Vector2(0.5f, 0f);
                    d.offsetMin = new Vector2(-DividerCore, _frameBottom);
                    d.offsetMax = new Vector2(DividerCore, Height - BottomNavArt.GoldH);
                }
            }
            ApplyFrame();
        }

        // ---------------------------------------------------------------------------------------- badges

        void Update()
        {
            _badgeTimer += Time.unscaledDeltaTime;
            if (_badgeTimer < BadgeRefreshSeconds) return;
            RefreshBadges();   // day rollovers (daily reward, free spin, free gift) have no event
        }

        void OnLevelChanged(int level) => RefreshBadges();

        /// <summary>Recomputes the red badges of every tab.</summary>
        public void RefreshBadges()
        {
            _badgeTimer = 0f;
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                var badge = _tabs[i].badge;
                if (badge == null) continue;
                switch (_tabs[i].id)
                {
                    case ScreenId.Shop:
                        badge.SetCount(ShopOffers.ClaimableCount);
                        break;
                    case ScreenId.Collection:
                        badge.SetCount(Collection.ClaimableAlbums);
                        break;
                    case ScreenId.Home:
                        badge.SetCount(HomeClaimables());
                        break;
                    case ScreenId.Profile:
                        badge.SetText(!AuthService.IsSignedIn && Progress.CurrentLevel >= HomeScreen.LoginNudgeLevel ? "!" : null);
                        break;
                    default:
                        badge.Hide();
                        break;
                }
            }
        }

        /// <summary>Claimables on Home: daily reward, quests, star chest, free spin.</summary>
        public static int HomeClaimables()
        {
            int n = 0;
            if (DailyRewards.CanClaim) n++;
            n += Quests.ClaimableCount;
            if (StarChest.CanOpen) n++;
            if (LuckySpin.HasFreeSpin) n++;
            return n;
        }
    }
}
