using System.Collections.Generic;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Ranking tab: Weekly / Global segmented control, weekly reset countdown, ranked rows (medals for the top 3,
    /// avatar, name + team, stars), the player's row highlighted green and pinned at the bottom while it is scrolled out
    /// of view. Global without an online ranking shows a "sign in to compete worldwide" banner (→ LoginPopup).
    /// Rows of other real players (online global ranking) show "⋯" and open PlayerOptionsPopup: report the name /
    /// block the player (App Store guideline 1.2); blocked rows disappear right away.
    /// </summary>
    public class LeaderboardScreen : TabScreen
    {
        public override ScreenId Id => ScreenId.Leaderboard;

        const float SegH = 112f, InfoH = 72f, RowH = 150f, BannerH = 190f, MoreW = 84f;

        sealed class Row
        {
            public RectTransform root, visual;
            public CanvasGroup group;
            public Image bg, medal, star;
            public RectTransform avatar;
            public TMP_Text rank, name, team, score;
            public bool isPlayer;
            // List rows only: tap → report / block (enabled for other real players).
            public RectTransform more;
            public Button tap;
            public HitArea hit;
            public float nameW;
            public LeaderboardEntry entry;
        }

        bool _global;
        int _request;
        bool _loading;
        float _w;

        RectTransform _segment, _knob;
        TMP_Text _segWeekly, _segGlobal;
        RectTransform _info;
        TMP_Text _infoText;
        Image _infoIcon;
        RectTransform _banner;
        LocText _bannerText;
        UIButton _bannerBtn;
        bool _bannerRetry;
        ScrollRect _scroll;
        RectTransform _scrollRt, _list;
        CommonSpinner _spinner;
        TMP_Text _empty;
        readonly List<Row> _rows = new List<Row>();
        int _shown;
        Row _playerRow;
        Row _pinned;
        LeaderboardEntry _playerEntry;
        bool _pinnedVisible;
        float _tick;
        readonly Vector3[] _corners = new Vector3[4];
        readonly Vector3[] _viewCorners = new Vector3[4];

        public override void Build()
        {
            BuildFrame("tabs.leaderboard.title", DS.Colors.Secondary);
            BuildLayout();
            AuthService.OnAuthChanged += OnAuthChanged;
            Economy.OnStarsChanged += OnStarsChanged;
            PlayerProfile.OnChanged += RequestRefresh;
            Loc.OnLanguageChanged += OnLanguageChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            AuthService.OnAuthChanged -= OnAuthChanged;
            Economy.OnStarsChanged -= OnStarsChanged;
            PlayerProfile.OnChanged -= RequestRefresh;
            Loc.OnLanguageChanged -= OnLanguageChanged;
        }

        void OnAuthChanged() { if (_global) RequestRefresh(); }
        void OnStarsChanged(long a, long b) => RequestRefresh();
        // Rows carry localized subtitles (area / country names): reload them too.
        void OnLanguageChanged() => RequestRefresh();

        protected override void OnLayoutChanged()
        {
            if (Mathf.Abs(ContentWidth - _w) < 1f) return;
            BuildLayout();
            if (IsVisible) Refresh(false);
        }

        // ---------------------------------------------------------------------------------------- layout

        void BuildLayout()
        {
            for (int i = Body.childCount - 1; i >= 0; i--)
            {
                var child = Body.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _rows.Clear();
            _playerRow = null;
            _shown = 0;
            _w = ContentWidth;

            BuildSegment();

            _info = CommonUI.GlassPill(Body, new Vector2(Mathf.Min(_w, 720f), InfoH), "icon_timer", out _infoText);
            UIKit.Place(_info, new Vector2(0.5f, 1f), new Vector2(Mathf.Min(_w, 720f), InfoH), new Vector2(0f, -(SegH + DS.Space.S)));
            _infoIcon = _info.Find("icon_timer") != null ? _info.Find("icon_timer").GetComponent<Image>() : null;

            BuildBanner();

            var scroll = UIKit.ScrollView(Body, new Vector2(_w, 600f), out _list, DS.Space.S, DS.Space.XS);
            _scroll = scroll;
            _scrollRt = (RectTransform)scroll.transform;
            _scroll.onValueChanged.AddListener(_ => UpdatePinned(true));
            // Room below the last row: the pinned copy of the player's row covers the bottom of the viewport.
            var listLayout = _list.GetComponent<VerticalLayoutGroup>();
            if (listLayout != null)
            {
                int pad = Mathf.RoundToInt(DS.Space.XS);
                listLayout.padding = new RectOffset(pad, pad, pad, Mathf.RoundToInt(RowH + DS.Space.M));
            }

            _spinner = CommonUI.Spinner(Body, 110f, Color.white);
            UIKit.Place((RectTransform)_spinner.transform, new Vector2(0.5f, 0.5f), new Vector2(110f, 110f), new Vector2(0f, -60f));
            _spinner.SetVisible(false);

            _empty = CommonUI.LocLabel(Body, "tabs.lb.error", TextStyle.BodyLight, new Vector2(_w, 120f));
            UIKit.Place(_empty.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(_w, 120f), new Vector2(0f, -60f));
            _empty.gameObject.SetActive(false);

            _pinned = CreateRow(Body, true);
            UIKit.Place(_pinned.root, new Vector2(0.5f, 0f), new Vector2(_w, RowH), new Vector2(0f, 6f));
            _pinned.root.gameObject.SetActive(false);
            _pinnedVisible = false;

            LayoutList(false);
            ApplySegment(false);
        }

        void BuildSegment()
        {
            float w = Mathf.Min(_w, 700f);
            _segment = UIKit.Rect("Segment", Body);
            UIKit.Place(_segment, new Vector2(0.5f, 1f), new Vector2(w, SegH), Vector2.zero);
            var track = UIKit.Capsule(_segment, new Vector2(w, SegH), DS.WithAlpha(DS.Colors.Ink, 0.55f));
            UIKit.Stretch(track.rectTransform);
            var rim = UIKit.Capsule(_segment, new Vector2(w, SegH), DS.Colors.GlassBorder);
            UIKit.Stretch(rim.rectTransform, -4f, -4f, -4f, -4f);
            rim.transform.SetAsFirstSibling();

            float half = w * 0.5f;
            _knob = UIKit.Rect("Knob", _segment);
            UIKit.Place(_knob, new Vector2(0.5f, 0.5f), new Vector2(half - 12f, SegH - 14f), new Vector2(-half * 0.5f, 0f));
            var knobShadow = UIKit.Capsule(_knob, new Vector2(half - 12f, SegH - 14f), DS.WithAlpha(DS.Colors.Ink, 0.35f));
            UIKit.Stretch(knobShadow.rectTransform, 0f, 6f, 0f, -6f);
            var knob = UIKit.Capsule(_knob, new Vector2(half - 12f, SegH - 14f), Color.white);
            UIKit.Stretch(knob.rectTransform);
            // Glossy highlight on the TOP of the knob (a bottom bar used to cross the label's baseline).
            var knobShine = UIKit.Capsule(_knob, new Vector2(half - 60f, 16f), DS.WithAlpha(DS.Colors.Lavender, 0.22f));
            UIKit.Place(knobShine.rectTransform, new Vector2(0.5f, 1f), new Vector2(half - 60f, 16f), new Vector2(0f, -8f));

            _segWeekly = SegmentButton(half, -half * 0.5f, "tabs.lb.weekly", false);
            _segGlobal = SegmentButton(half, half * 0.5f, "lb.global", true);
        }

        TMP_Text SegmentButton(float width, float x, string key, bool global)
        {
            var hit = UIKit.Rect(global ? "Global" : "Weekly", _segment);
            UIKit.Place(hit, new Vector2(0.5f, 0.5f), new Vector2(width, SegH), new Vector2(x, 0f));
            var visual = UIKit.Stretch(UIKit.Rect("Visual", hit));
            var label = UIKit.LocText(visual, key, TextStyle.H3, new Vector2(width - 30f, SegH - 20f));
            // Same vertical box as the knob (SegH - 14, centered) so the label sits in the middle of the selected pill.
            UIKit.Stretch(label.rectTransform, 24f, 9f, 24f, 9f);
            label.alignment = TextAlignmentOptions.Center;
            label.margin = Vector4.zero;
            CommonUI.MakeTappable(hit, visual, () => SelectTab(global));
            return label;
        }

        void BuildBanner()
        {
            _banner = UIKit.Rect("SignInBanner", Body);
            float top = SegH + DS.Space.S + InfoH + DS.Space.S;
            UIKit.Place(_banner, new Vector2(0.5f, 1f), new Vector2(_w, BannerH), new Vector2(0f, -top));
            var bg = CommonUI.Card(_banner, new Vector2(_w, BannerH));
            UIKit.Stretch(bg.rectTransform);
            var trophy = UIKit.Image(_banner, "icon_trophy", new Vector2(130f, 130f));
            UIKit.Place(trophy.rectTransform, new Vector2(0f, 0.5f), new Vector2(130f, 130f), new Vector2(26f, 6f));
            Tween.Bob(trophy.rectTransform, 6f, 2.2f);
            float btnW = 270f;
            float textW = _w - 170f - btnW - 40f;
            var text = CommonUI.LocLabel(_banner, "tabs.lb.signin_banner", TextStyle.Body, new Vector2(textW, BannerH - 40f),
                TextAlignmentOptions.Left, 42f);
            UIKit.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(textW, BannerH - 40f), new Vector2(170f, 4f));
            _bannerText = text.GetComponent<LocText>();
            _bannerBtn = UIKit.ButtonLoc(_banner, "tabs.lb.signin", ButtonColor.Blue, new Vector2(btnW, 116f), OnBannerButton);
            UIKit.Place((RectTransform)_bannerBtn.transform, new Vector2(1f, 0.5f), new Vector2(btnW, 116f), new Vector2(-24f, 4f));
            _bannerRetry = false;
            _banner.gameObject.SetActive(false);
        }

        /// <summary>
        /// Signed out → "Sign in to compete worldwide" + Sign in (LoginPopup). Signed in but the online ranking could not
        /// be reached (no network, cloud disabled, editor mock) → "online ranking unavailable" + Retry: a Sign in button
        /// would do nothing for a player who is already signed in.
        /// </summary>
        void SetBannerMode(bool retry)
        {
            _bannerRetry = retry;
            if (_bannerText != null) _bannerText.Set(retry ? "tabs.lb.offline_signed_in" : "tabs.lb.signin_banner");
            if (_bannerBtn != null) _bannerBtn.SetLabelKey(retry ? "ui.retry" : "tabs.lb.signin");
        }

        void OnBannerButton()
        {
            if (!_bannerRetry || !AuthService.IsSignedIn)
            {
                OpenLogin();
                return;
            }
            if (_loading) return;
            LeaderboardService.InvalidateGlobalCache();
            Load(true);
        }

        /// <summary>Scroll view between the info strip (and the banner when visible) and the bottom of the Body.</summary>
        void LayoutList(bool withBanner)
        {
            if (_scrollRt == null) return;
            float top = SegH + DS.Space.S + InfoH + DS.Space.S + (withBanner ? BannerH + DS.Space.S : 0f);
            UIKit.Stretch(_scrollRt, 0f, top, 0f, 0f);
        }

        // ---------------------------------------------------------------------------------------- rows

        Row CreateRow(Transform parent, bool pinned)
        {
            var r = new Row();
            r.root = UIKit.Rect(pinned ? "PinnedRow" : "Row", parent);
            r.root.sizeDelta = new Vector2(_w, RowH);
            r.visual = UIKit.Stretch(UIKit.Rect("Visual", r.root));
            r.group = r.visual.gameObject.AddComponent<CanvasGroup>();
            r.group.blocksRaycasts = false;

            if (pinned)
            {
                var glow = UIKit.Capsule(r.visual, new Vector2(_w, RowH), DS.WithAlpha(DS.Colors.Accent, 0.45f));
                UIKit.Stretch(glow.rectTransform, -10f, -10f, -10f, -10f);
            }
            r.bg = UIKit.Panel(r.visual, "panel_card", new Vector2(_w, RowH));
            UIKit.Stretch(r.bg.rectTransform);

            r.medal = UIKit.Image(r.visual, "icon_medal_gold", new Vector2(92f, 92f));
            UIKit.Place(r.medal.rectTransform, new Vector2(0f, 0.5f), new Vector2(92f, 92f), new Vector2(30f, 6f));
            r.rank = CommonUI.Label(r.visual, "", TextStyle.H3, new Vector2(120f, 80f), TextAlignmentOptions.Center, 50f);
            UIKit.Place(r.rank.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 80f), new Vector2(16f, 6f));

            r.avatar = CommonUI.Avatar(r.visual, "avatar_puppy", 112f, Color.white);
            UIKit.Place(r.avatar, new Vector2(0f, 0.5f), new Vector2(112f, 112f), new Vector2(142f, 6f));

            float nameX = 274f, scoreW = 230f;
            float nameW = _w - nameX - scoreW - 30f;
            r.nameW = nameW;
            r.name = CommonUI.Label(r.visual, "", TextStyle.Body, new Vector2(nameW, 58f), TextAlignmentOptions.Left, 46f);
            r.name.textWrappingMode = TextWrappingModes.NoWrap;
            r.name.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Place(r.name.rectTransform, new Vector2(0f, 0.5f), new Vector2(nameW, 58f), new Vector2(nameX, 28f));
            r.team = CommonUI.Label(r.visual, "", TextStyle.Small, new Vector2(nameW, 44f), TextAlignmentOptions.Left, 32f);
            r.team.textWrappingMode = TextWrappingModes.NoWrap;
            r.team.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Place(r.team.rectTransform, new Vector2(0f, 0.5f), new Vector2(nameW, 44f), new Vector2(nameX, -26f));

            var pill = UIKit.Capsule(r.visual, new Vector2(scoreW, 78f), DS.WithAlpha(DS.Colors.Ink, 0.08f));
            UIKit.Place(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(scoreW, 78f), new Vector2(-26f, 6f));
            r.star = UIKit.Image(pill.rectTransform, "icon_star", new Vector2(70f, 70f));
            UIKit.Place(r.star.rectTransform, new Vector2(0f, 0.5f), new Vector2(70f, 70f), new Vector2(-14f, 2f));
            r.score = CommonUI.Label(pill.rectTransform, "", TextStyle.Body, new Vector2(scoreW - 70f, 70f), TextAlignmentOptions.Center, 44f);
            UIKit.Place(r.score.rectTransform, new Vector2(1f, 0.5f), new Vector2(scoreW - 70f, 70f), new Vector2(-12f, 2f));

            if (!pinned)
            {
                r.more = MoreDots(r.visual);
                // Left of the score pill, clear of its star (which overhangs the pill's left edge).
                UIKit.Place(r.more, new Vector2(1f, 0.5f), new Vector2(MoreW, 40f), new Vector2(-(26f + scoreW + 14f), 6f));
                r.tap = CommonUI.MakeTappable(r.root, r.visual, () => OnRowTapped(r));
                r.hit = r.root.GetComponent<HitArea>();
            }
            return r;
        }

        /// <summary>"⋯" drawn with three dots (no glyph needed): this row opens the report / block popup.</summary>
        static RectTransform MoreDots(Transform parent)
        {
            var root = UIKit.Rect("More", parent);
            root.sizeDelta = new Vector2(MoreW, 40f);
            for (int i = 0; i < 3; i++)
            {
                var dot = UIKit.NewImage(root, "Dot", UISprites.Circle, DS.WithAlpha(DS.Colors.Ink, 0.45f));
                UIKit.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(13f, 13f), new Vector2((i - 1) * 19f, 0f));
            }
            return root;
        }

        static Color RankTint(int rank)
        {
            switch (rank)
            {
                case 1: return Color.Lerp(DS.Colors.Accent, Color.white, 0.55f);
                case 2: return Color.Lerp(DS.Hex("C9D3E6"), Color.white, 0.35f);
                case 3: return Color.Lerp(DS.Hex("F0A46C"), Color.white, 0.5f);
                default: return Color.white;
            }
        }

        void Bind(Row r, LeaderboardEntry e)
        {
            r.isPlayer = e.isPlayer;
            r.entry = e;
            if (r.tap != null)
            {
                // Only other real players can be reported / blocked: simulated rivals and the player's row stay inert.
                bool moderate = LeaderboardService.CanModerate(e);
                r.tap.interactable = moderate;
                if (r.hit != null) r.hit.raycastTarget = moderate;
                r.more.gameObject.SetActive(moderate);
                r.name.rectTransform.sizeDelta = new Vector2(moderate ? r.nameW - MoreW : r.nameW, r.name.rectTransform.sizeDelta.y);
            }
            bool top3 = e.rank >= 1 && e.rank <= 3;
            r.medal.gameObject.SetActive(top3);
            r.rank.gameObject.SetActive(!top3);
            if (top3)
            {
                var sp = UISprites.Get(e.rank == 1 ? "icon_medal_gold" : e.rank == 2 ? "icon_medal_silver" : "icon_medal_bronze");
                if (sp != null) r.medal.sprite = sp;
            }
            else r.rank.text = e.rank > 0 ? Loc.Number(e.rank) : Loc.T("lb.rank_unknown");

            CommonUI.SetAvatarSprite(r.avatar, e.avatar);
            r.name.text = e.name ?? "";
            r.team.text = e.team ?? "";
            r.team.gameObject.SetActive(!string.IsNullOrEmpty(e.team));
            r.score.text = Loc.Number(e.score);

            if (e.isPlayer)
            {
                r.bg.color = Color.Lerp(DS.Colors.Primary, Color.white, 0.12f);
                DS.Apply(r.name, TextStyle.H3, 48f);
                DS.Apply(r.team, TextStyle.BodyLight, 32f);
                DS.Apply(r.score, TextStyle.H3, 46f);
                DS.Apply(r.rank, TextStyle.H3, 50f);
                CommonUI.SetAvatarRing(r.avatar, DS.Colors.Accent);
            }
            else
            {
                r.bg.color = RankTint(e.rank);
                DS.Apply(r.name, TextStyle.Body, 46f);
                DS.Apply(r.team, TextStyle.Small, 32f);
                DS.Apply(r.score, TextStyle.Body, 44f);
                DS.Apply(r.rank, TextStyle.H3, 50f);
                CommonUI.SetAvatarRing(r.avatar, top3 ? DS.Colors.Accent : DS.Colors.Lavender);
            }
            r.name.textWrappingMode = TextWrappingModes.NoWrap;
            r.name.overflowMode = TextOverflowModes.Ellipsis;
            r.name.alignment = TextAlignmentOptions.Left;
            r.team.textWrappingMode = TextWrappingModes.NoWrap;
            r.team.overflowMode = TextOverflowModes.Ellipsis;
            r.team.alignment = TextAlignmentOptions.Left;
            r.score.alignment = TextAlignmentOptions.Center;
            r.rank.alignment = TextAlignmentOptions.Center;
        }

        // ---------------------------------------------------------------------------------------- data

        void SelectTab(bool global)
        {
            if (_global == global && !_loading) return;
            _global = global;
            AudioManager.Play(Sfx.Toggle);
            Haptics.Play(HapticType.Selection);
            ApplySegment(true);
            Load(true);
        }

        void ApplySegment(bool animate)
        {
            if (_knob == null) return;
            float half = _segment.sizeDelta.x * 0.5f;
            var pos = new Vector2(_global ? half * 0.5f : -half * 0.5f, 0f);
            Tween.Kill(_knob);
            if (animate)
            {
                Tween.Move(_knob, pos, DS.Motion.Base, Ease.OutBack);
                Tween.Punch(_knob, 0.08f, 0.3f);
            }
            else _knob.anchoredPosition = pos;
            StyleSegLabel(_segWeekly, !_global);
            StyleSegLabel(_segGlobal, _global);
        }

        static void StyleSegLabel(TMP_Text t, bool selected)
        {
            if (t == null) return;
            if (selected)
            {
                DS.Apply(t, TextStyle.Body, 46f);
                t.color = DS.Colors.Ink;
            }
            else DS.Apply(t, TextStyle.H3, 44f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.Center;
        }

        protected override void Refresh(bool animate)
        {
            ApplySegment(false);
            Load(animate);
        }

        /// <summary>(Re)loads the selected ranking; <paramref name="keepScroll"/> keeps the scroll position (row hidden).</summary>
        void Load(bool animate, bool keepScroll = false)
        {
            int token = ++_request;
            float scroll = keepScroll && _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            _loading = true;
            HideRows();
            _empty.gameObject.SetActive(false);
            _spinner.SetVisible(true);
            _spinner.transform.localScale = Vector3.zero;
            Tween.Scale(_spinner.transform, 1f, 0.2f, Ease.OutBack).SetDelay(0.1f);
            UpdateInfo();
            if (_global)
                LeaderboardService.GetGlobal((list, online) => OnLoaded(token, list, online, animate, scroll));
            else
                LeaderboardService.GetWeekly(list => OnLoaded(token, list, true, animate, scroll));
        }

        void OnLoaded(int token, List<LeaderboardEntry> list, bool online, bool animate, float scroll = 1f)
        {
            if (this == null || token != _request) return;
            _loading = false;
            _spinner.SetVisible(false);
            bool signedIn = AuthService.IsSignedIn;
            bool showBanner = _global && (!online || !signedIn);
            if (showBanner) SetBannerMode(signedIn);
            _banner.gameObject.SetActive(showBanner);
            if (showBanner) CommonUI.PopIn(_banner, 0.05f);
            LayoutList(showBanner);
            UpdateInfo();

            if (list == null || list.Count == 0)
            {
                _empty.gameObject.SetActive(true);
                _playerRow = null;
                _playerEntry = null;
                UpdatePinned(false);
                return;
            }

            _playerRow = null;
            _playerEntry = null;
            for (int i = 0; i < list.Count; i++)
            {
                Row r = i < _rows.Count ? _rows[i] : null;
                if (r == null)
                {
                    r = CreateRow(_list, false);
                    _rows.Add(r);
                }
                r.root.gameObject.SetActive(true);
                Bind(r, list[i]);
                if (list[i].isPlayer)
                {
                    _playerRow = r;
                    _playerEntry = list[i];
                }
                Tween.Kill(r.visual);
                Tween.Kill(r.group);
                r.visual.anchoredPosition = Vector2.zero;
                r.group.alpha = 1f;
                if (animate && i < 14) CommonUI.SlideIn(r.visual, r.group, new Vector2(140f, 0f), 0.05f + i * 0.04f, 0.32f);
            }
            for (int i = list.Count; i < _rows.Count; i++) _rows[i].root.gameObject.SetActive(false);
            _shown = list.Count;

            if (_playerEntry != null) Bind(_pinned, _playerEntry);
            _scroll.StopMovement();
            if (scroll < 1f) Canvas.ForceUpdateCanvases();   // content height of the new rows before restoring a kept position
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll);
            Canvas.ForceUpdateCanvases();
            _pinnedVisible = false;
            _pinned.root.gameObject.SetActive(false);
            UpdatePinned(animate);
        }

        void HideRows()
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].root.gameObject.SetActive(false);
            _shown = 0;
            _playerRow = null;
            if (_pinned != null) _pinned.root.gameObject.SetActive(false);
            _pinnedVisible = false;
        }

        void UpdateInfo()
        {
            if (_infoText == null) return;
            if (!_global)
            {
                SetInfoIcon("icon_timer");
                _infoText.text = Loc.T("lb.ends_in", TimeUtil.FormatDuration(LeaderboardService.WeeklyResetSeconds));
            }
            else
            {
                SetInfoIcon("icon_crown");
                _infoText.text = Loc.T("tabs.lb.top_players", LeaderboardService.GlobalLimit);
            }
        }

        void SetInfoIcon(string sprite)
        {
            if (_infoIcon == null) return;
            var sp = UISprites.Get(sprite);
            if (sp != null) _infoIcon.sprite = sp;
        }

        /// <summary>Shows the pinned copy of the player's row while the real one is (partly) outside the viewport.</summary>
        void UpdatePinned(bool animate)
        {
            if (_pinned == null) return;
            bool show = false;
            if (_playerRow != null && _playerRow.root.gameObject.activeInHierarchy && _scroll != null)
            {
                _playerRow.root.GetWorldCorners(_corners);
                _scroll.viewport.GetWorldCorners(_viewCorners);
                float rowTop = _corners[1].y, rowBottom = _corners[0].y;
                float viewTop = _viewCorners[1].y, viewBottom = _viewCorners[0].y;
                // The pinned row covers the bottom of the viewport: treat that strip as hidden.
                float pinnedH = (_viewCorners[1].y - _viewCorners[0].y) * (RowH / Mathf.Max(1f, _scrollRt.rect.height));
                show = rowBottom < viewBottom + pinnedH * 0.5f || rowTop > viewTop;
            }
            if (show == _pinnedVisible) return;
            _pinnedVisible = show;
            var rt = _pinned.root;
            Tween.Kill(rt);
            Tween.Kill(_pinned.group);
            if (show)
            {
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(0f, -RowH);
                Tween.Move(rt, new Vector2(0f, 6f), animate ? 0.3f : 0.01f, Ease.OutBack);
                _pinned.group.alpha = 1f;
            }
            else
            {
                Tween.Move(rt, new Vector2(0f, -RowH), 0.2f, Ease.InQuad)
                    .OnComplete(() => { if (rt != null && !_pinnedVisible) rt.gameObject.SetActive(false); });
            }
        }

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            if (!_global) UpdateInfo();
        }

        void OnRowTapped(Row r)
        {
            if (r == null || !LeaderboardService.CanModerate(r.entry)) return;
            // Reported or blocked: reload in place (served from the cache, without the blocked rows).
            PlayerOptionsPopup.Open(r.entry, blocked =>
            {
                if (blocked && this != null && IsVisible) Load(false, true);
            });
        }

        void OpenLogin()
        {
            LoginPopup.Open(ok =>
            {
                if (ok && this != null) RequestRefresh();
            });
        }
    }
}
