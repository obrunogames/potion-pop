using System.Collections.Generic;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Profile tab: big avatar (→ avatar picker with the 12 avatars), editable name (TMP_InputField, 16 chars, checked by
    /// NameFilter), player ID + level pills, the 3x3 stats grid (levels won, stars, 3-star levels, bottles completed,
    /// best streak, best combo, hard levels, replays won, pours) and
    /// the account card (signed in: provider, cloud state, last sync, Sync now, Sign out; signed out: benefits and the
    /// Google / Apple sign-in buttons). A gear button opens Settings.
    /// </summary>
    public class ProfileScreen : TabScreen
    {
        public override ScreenId Id => ScreenId.Profile;

        const float IdentityH = 570f, StatH = 250f;
        /// <summary>Gap above the avatar so its halo is not cut by the top edge of the scroll viewport.</summary>
        const float AvatarTop = 40f;

        sealed class Stat
        {
            public TMP_Text value;
            public RectTransform root;
            public bool combo;
        }

        float _w;
        ScrollRect _scroll;
        RectTransform _list;
        readonly List<RectTransform> _reveal = new List<RectTransform>();

        RectTransform _avatar, _nameRow, _settingsBtn;
        TMP_Text _name, _idText, _levelText;
        UIButton _editName;
        TMP_InputField _nameInput;
        bool _editing;

        readonly List<Stat> _stats = new List<Stat>();

        RectTransform _account;
        bool _accountSignedIn;
        bool _accountBuilt;
        TMP_Text _cloudState, _lastSync;
        Image _cloudIcon;
        UIButton _syncBtn;
        CommonSpinner _syncSpinner;
        float _tick;

        public override void Build()
        {
            BuildFrame("tabs.profile.title", DS.Colors.Mint);
            BuildList();
            PlayerProfile.OnChanged += RefreshIdentityOnly;
            AuthService.OnAuthChanged += OnAuthChanged;
            CloudSave.OnStateChanged += OnCloudState;
            Loc.OnLanguageChanged += OnLanguageChanged;
            Progress.OnLevelChanged += OnLevelChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            PlayerProfile.OnChanged -= RefreshIdentityOnly;
            AuthService.OnAuthChanged -= OnAuthChanged;
            CloudSave.OnStateChanged -= OnCloudState;
            Loc.OnLanguageChanged -= OnLanguageChanged;
            Progress.OnLevelChanged -= OnLevelChanged;
        }

        void OnLevelChanged(int level) => RequestRefresh();
        void OnLanguageChanged() { _accountBuilt = false; RequestRefresh(); }

        void OnAuthChanged()
        {
            if (this == null) return;
            _accountBuilt = false;
            if (IsVisible) RebuildAccount(true);
        }

        void OnCloudState()
        {
            if (this == null || !IsVisible) return;
            RefreshCloudRow();
        }

        protected override void OnLayoutChanged()
        {
            if (Mathf.Abs(ContentWidth - _w) < 1f) return;
            BuildList();
            if (IsVisible) Refresh(false);
        }

        public override void OnHide()
        {
            if (_editing) CommitName(_nameInput != null ? _nameInput.text : null);
        }

        // ---------------------------------------------------------------------------------------- build

        void BuildList()
        {
            if (_scroll != null)
            {
                _scroll.gameObject.SetActive(false);
                Destroy(_scroll.gameObject);
            }
            _reveal.Clear();
            _stats.Clear();
            _accountBuilt = false;
            _editing = false;
            _w = ContentWidth;

            _scroll = UIKit.ScrollView(Body, new Vector2(_w, BodyHeight), out _list, DS.Space.M, DS.Space.S);
            UIKit.Stretch((RectTransform)_scroll.transform);

            BuildIdentity();
            _reveal.Add(CommonUI.SectionHeader(_list, "tabs.profile.stats", "icon_trophy", _w, DS.Colors.Orange));
            BuildStats();
            _reveal.Add(CommonUI.SectionHeader(_list, "tabs.profile.account", "icon_cloud", _w, DS.Colors.Secondary));
            _account = UIKit.Rect("Account", _list);
            _account.sizeDelta = new Vector2(_w, 200f);
            _reveal.Add(_account);
            UIKit.Spacer(_list, new Vector2(_w, DS.Space.XL));
        }

        void BuildIdentity()
        {
            var root = UIKit.Rect("Identity", _list);
            root.sizeDelta = new Vector2(_w, IdentityH);
            _reveal.Add(root);

            // Settings gear (top right).
            var gear = UIKit.IconButton(root, "icon_settings", 118f, () => SettingsPopup.Open(), "btn_square");
            _settingsBtn = (RectTransform)gear.transform;
            UIKit.Place(_settingsBtn, new Vector2(1f, 1f), new Vector2(118f, 118f), new Vector2(0f, -4f));
            Tween.Rotate(gear.icon.transform, -360f, 9f, Ease.Linear).SetLoops(-1, false);

            // Avatar with glow + edit badge.
            const float av = 290f;
            // Halo centered on the avatar and small enough (x1.4, pulsing to x1.1) to fade out before the viewport top.
            const float haloS = av * 1.4f;
            var halo = UIKit.NewImage(root, "Halo", UISprites.Glow, DS.WithAlpha(DS.Colors.Accent, 0.55f));
            UIKit.Place(halo.rectTransform, new Vector2(0.5f, 1f), new Vector2(haloS, haloS), Vector2.zero);
            halo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            halo.rectTransform.anchoredPosition = new Vector2(0f, -(AvatarTop + av * 0.5f));
            Tween.Pulse(halo.transform, 1.1f, 2.2f);
            var avatarHit = UIKit.Rect("AvatarButton", root);
            UIKit.Place(avatarHit, new Vector2(0.5f, 1f), new Vector2(av, av), new Vector2(0f, -AvatarTop));
            var avatarVisual = UIKit.Stretch(UIKit.Rect("Visual", avatarHit));
            _avatar = CommonUI.Avatar(avatarVisual, PlayerProfile.AvatarSprite, av, Color.white);
            UIKit.Stretch(_avatar);
            var badge = UIKit.Rect("Edit", avatarVisual);
            UIKit.Place(badge, new Vector2(1f, 0f), new Vector2(92f, 92f), new Vector2(-6f, 6f));
            var badgeBg = UIKit.NewImage(badge, "Bg", UISprites.Circle, DS.Colors.Secondary);
            UIKit.Stretch(badgeBg.rectTransform);
            var badgeRim = UIKit.NewImage(badge, "Rim", UISprites.Ring, Color.white);
            UIKit.Stretch(badgeRim.rectTransform);
            var badgeIcon = UIKit.Image(badge, "icon_profile", new Vector2(58f, 58f));
            UIKit.Stretch(badgeIcon.rectTransform, 17f, 17f, 17f, 17f);
            CommonUI.MakeTappable(avatarHit, avatarVisual, AvatarPickerPopup.Open);
            Tween.Bob(avatarVisual, 6f, 2.6f);

            // Name (label + edit button, or the input field while editing).
            _nameRow = UIKit.Rect("NameRow", root);
            UIKit.Place(_nameRow, new Vector2(0.5f, 1f), new Vector2(_w, 110f), new Vector2(0f, -(AvatarTop + av + 20f)));
            float nameW = _w - 440f;
            _name = CommonUI.Label(_nameRow, "", TextStyle.H1, new Vector2(nameW, 104f), TextAlignmentOptions.Center, 76f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(_name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(nameW, 104f), Vector2.zero);
            _editName = UIKit.ButtonLoc(_nameRow, "tabs.profile.edit", ButtonColor.Blue, new Vector2(190f, 92f), BeginEditName);
            UIKit.Place((RectTransform)_editName.transform, new Vector2(1f, 0.5f), new Vector2(190f, 92f), new Vector2(-6f, 0f));
            _editName.ExpandHitArea();
            _nameInput = BuildNameInput(_nameRow, new Vector2(Mathf.Min(_w - 120f, 720f), 110f));

            // Player ID + level pills.
            var idPill = CommonUI.GlassPill(root, new Vector2(360f, 66f), null, out _idText);
            UIKit.Place(idPill, new Vector2(0.5f, 1f), new Vector2(360f, 66f), new Vector2(-196f, -(AvatarTop + av + 150f)));
            var lvPill = CommonUI.GlassPill(root, new Vector2(360f, 66f), "icon_star", out _levelText);
            UIKit.Place(lvPill, new Vector2(0.5f, 1f), new Vector2(360f, 66f), new Vector2(196f, -(AvatarTop + av + 150f)));
        }

        TMP_InputField BuildNameInput(Transform parent, Vector2 size)
        {
            var holder = UIKit.Rect("NameInput", parent);
            UIKit.Place(holder, new Vector2(0.5f, 0.5f), size, Vector2.zero);
            holder.gameObject.SetActive(false);   // configure before TMP_InputField.OnEnable runs

            var rim = UIKit.Capsule(holder, size, DS.Colors.Secondary);
            UIKit.Stretch(rim.rectTransform, -6f, -6f, -6f, -6f);
            var bg = UIKit.Capsule(holder, size, Color.white);
            UIKit.Stretch(bg.rectTransform);
            bg.raycastTarget = true;

            var area = UIKit.Stretch(UIKit.Rect("TextArea", holder), size.y * 0.45f, 6f, size.y * 0.45f, 6f);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = UIKit.LocText(area, "common.player", TextStyle.Body, size);   // follows language changes
            UIKit.Stretch(placeholder.rectTransform);
            DS.Apply(placeholder, TextStyle.Body, 52f);
            placeholder.enableAutoSizing = false;
            placeholder.fontSize = 52f;
            placeholder.color = DS.WithAlpha(DS.Colors.InkSoft, 0.6f);
            placeholder.alignment = TextAlignmentOptions.Center;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            var text = UIKit.Text(area, "", TextStyle.Body, size);
            UIKit.Stretch(text.rectTransform);
            DS.Apply(text, TextStyle.Body, 52f);
            text.enableAutoSizing = false;
            text.fontSize = 52f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var input = holder.gameObject.AddComponent<TMP_InputField>();
            input.transition = Selectable.Transition.None;
            input.targetGraphic = bg;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = PlayerProfile.MaxNameLength;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.customCaretColor = true;
            input.caretColor = DS.Colors.Ink;
            input.caretWidth = 4;
            input.selectionColor = DS.WithAlpha(DS.Colors.BrandLight, 0.6f);
            input.onEndEdit.AddListener(CommitName);
            return input;
        }

        void BuildStats()
        {
            const int cols = 3, rows = 3;
            float gap = DS.Space.S;
            float cw = (_w - gap * (cols - 1)) / cols;
            var grid = UIKit.Grid(_list, new Vector2(_w, StatH * rows + gap * (rows - 1)), new Vector2(cw, StatH), new Vector2(gap, gap), cols);
            AddStat(grid.transform, cw, "icon_trophy", "tabs.profile.levels_won", false);
            AddStat(grid.transform, cw, "icon_star", "tabs.profile.total_stars", false);
            AddStat(grid.transform, cw, "icon_crown", "tabs.profile.perfect_levels", false);
            AddStat(grid.transform, cw, "icon_bottle", "tabs.profile.bottles", false);
            AddStat(grid.transform, cw, "icon_flame", "tabs.profile.best_streak", false);
            AddStat(grid.transform, cw, "icon_medal_gold", "tabs.profile.max_combo", true);
            AddStat(grid.transform, cw, "icon_skull", "tabs.profile.hard_won", false);
            AddStat(grid.transform, cw, "icon_restart", "tabs.profile.replays", false);
            AddStat(grid.transform, cw, "icon_pour", "tabs.profile.pours", false);
        }

        void AddStat(Transform parent, float cw, string icon, string key, bool combo)
        {
            var root = UIKit.Rect("Stat", parent);
            var bg = CommonUI.Card(root, new Vector2(cw, StatH));
            UIKit.Stretch(bg.rectTransform);
            var ic = UIKit.Image(root, icon, new Vector2(90f, 90f));
            UIKit.Place(ic.rectTransform, new Vector2(0.5f, 1f), new Vector2(90f, 90f), new Vector2(0f, -22f));
            var value = CommonUI.Label(root, "0", TextStyle.Body, new Vector2(cw - 24f, 70f), TextAlignmentOptions.Center, 62f);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(value.rectTransform, new Vector2(0.5f, 1f), new Vector2(cw - 24f, 70f), new Vector2(0f, -116f));
            var label = CommonUI.LocLabel(root, key, TextStyle.Small, new Vector2(cw - 24f, 56f), TextAlignmentOptions.Center, 30f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(cw - 24f, 56f), new Vector2(0f, 28f));
            _stats.Add(new Stat { value = value, root = root, combo = combo });
        }

        // ---------------------------------------------------------------------------------------- refresh

        protected override void Refresh(bool animate)
        {
            if (_list == null) return;
            RefreshIdentity();
            RefreshStats(animate);
            if (!_accountBuilt || _accountSignedIn != AuthService.IsSignedIn) RebuildAccount(false);
            else RefreshCloudRow();
            if (!animate) return;
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _reveal.Count; i++) CommonUI.PopIn(_reveal[i], 0.06f + i * 0.06f);
            for (int i = 0; i < _stats.Count; i++) CommonUI.PopIn(_stats[i].root, 0.2f + i * DS.Motion.Stagger);
        }

        void RefreshIdentityOnly()
        {
            if (this == null || !IsVisible) return;
            string before = _avatarSprite;
            RefreshIdentity();
            if (before != _avatarSprite && _avatar != null)
            {
                Tween.Punch(_avatar, 0.2f, 0.4f);
                FX.Sparkles(null, FX.LocalCenterOf(_avatar), 12);
            }
        }

        string _avatarSprite;

        void RefreshIdentity()
        {
            _avatarSprite = PlayerProfile.AvatarSprite;
            CommonUI.SetAvatarSprite(_avatar, _avatarSprite);
            if (!_editing && _name != null) _name.text = PlayerProfile.DisplayName;
            if (_idText != null) _idText.text = Loc.T("tabs.profile.player_id", PlayerProfile.PlayerId);
            if (_levelText != null) _levelText.text = Loc.T("ui.level", Progress.CurrentLevel);
        }

        void RefreshStats(bool animate)
        {
            var d = SaveSystem.Data;
            long[] values =
            {
                d.levelsWon, d.totalStars, Progress.PerfectLevels,
                d.totalBottles, d.bestStreak, d.maxCombo,
                d.hardLevelsWon, d.replaysWon, d.totalPours,
            };
            for (int i = 0; i < _stats.Count && i < values.Length; i++)
            {
                var s = _stats[i];
                long v = values[i];
                Tween.Kill(s.value);
                if (animate && v > 0)
                {
                    var text = s.value;
                    bool combo = s.combo;
                    SetStat(text, 0, combo);
                    Tween.Value(0f, v, 0.9f, f => SetStat(text, (long)f, combo), Ease.OutCubic).SetDelay(0.3f + i * 0.06f).SetLink(text);
                }
                else SetStat(s.value, v, s.combo);
            }
        }

        static void SetStat(TMP_Text t, long v, bool combo)
        {
            if (t == null) return;
            t.text = combo ? Loc.T("reward.amount.count", v) : Loc.Number(v);
        }

        // ---------------------------------------------------------------------------------------- name editing

        void BeginEditName()
        {
            if (_nameInput == null || _editing) return;
            _editing = true;
            _name.gameObject.SetActive(false);
            _editName.gameObject.SetActive(false);
            _nameInput.gameObject.SetActive(true);
            _nameInput.text = PlayerProfile.HasCustomName ? SaveSystem.Data.playerName : "";
            CommonUI.PopIn(_nameInput.transform, 0f);
            _nameInput.ActivateInputField();
            _nameInput.Select();
        }

        void CommitName(string value)
        {
            if (!_editing) return;
            _editing = false;
            string before = PlayerProfile.DisplayName;
            // Refused by the name filter (the name is public in the global ranking): the previous name stays.
            bool refused = value != null && !PlayerProfile.SetName(value);
            if (_nameInput != null) _nameInput.gameObject.SetActive(false);
            if (_name != null)
            {
                _name.gameObject.SetActive(true);
                _name.text = PlayerProfile.DisplayName;
            }
            if (_editName != null) _editName.gameObject.SetActive(true);
            if (refused)
            {
                UIKit.Toast(Loc.T("tabs.profile.name_refused"));
                if (_name != null && IsVisible) CommonUI.Deny(_name.transform);
            }
            else if (_name != null && before != PlayerProfile.DisplayName && IsVisible)
            {
                AudioManager.Play(Sfx.Pop);
                Haptics.Play(HapticType.Light);
                Tween.Punch(_name.transform, 0.18f, 0.35f);
                FX.Sparkles(null, FX.LocalCenterOf(_name.rectTransform), 10);
            }
        }

        // ---------------------------------------------------------------------------------------- account

        void RebuildAccount(bool animate)
        {
            if (_account == null) return;
            for (int i = _account.childCount - 1; i >= 0; i--)
            {
                var child = _account.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _cloudState = _lastSync = null;
            _cloudIcon = null;
            _syncBtn = null;
            _syncSpinner = null;
            _accountBuilt = true;
            _accountSignedIn = AuthService.IsSignedIn;

            float h = _accountSignedIn ? BuildSignedIn() : BuildSignedOut();
            _account.sizeDelta = new Vector2(_w, h);
            LayoutRebuilder.MarkLayoutForRebuild(_list);
            if (animate) CommonUI.PopIn(_account, 0f);
        }

        float BuildSignedIn()
        {
            const float h = 470f;
            var bg = CommonUI.Card(_account, new Vector2(_w, h));
            UIKit.Stretch(bg.rectTransform);
            var user = AuthService.User;
            var provider = user != null ? user.provider : AuthProvider.Google;

            var logo = SignInButtons.ProviderLogo(_account, provider, 100f);
            UIKit.Place(logo, new Vector2(0f, 1f), new Vector2(100f, 100f), new Vector2(34f, -34f));
            float textW = _w - 180f;
            var with = CommonUI.Label(_account, Loc.T("auth.signed_in_with", Loc.T(SignInButtons.ProviderNameKey(provider))),
                TextStyle.Body, new Vector2(textW, 56f), TextAlignmentOptions.Left, 44f);
            UIKit.Place(with.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 56f), new Vector2(156f, -32f));
            string who = user != null && !string.IsNullOrEmpty(user.NameOrEmpty) ? user.NameOrEmpty : (user != null ? user.email : "");
            var name = CommonUI.Label(_account, who ?? "", TextStyle.Small, new Vector2(textW, 46f), TextAlignmentOptions.Left, 34f);
            UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 46f), new Vector2(156f, -90f));

            var divider = UIKit.Capsule(_account, new Vector2(_w - 68f, 6f), DS.Colors.CreamDark);
            UIKit.Place(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 68f, 6f), new Vector2(0f, -158f));

            _cloudIcon = UIKit.Image(_account, "icon_cloud", new Vector2(96f, 72f));
            UIKit.Place(_cloudIcon.rectTransform, new Vector2(0f, 1f), new Vector2(96f, 72f), new Vector2(36f, -186f));
            Tween.Bob(_cloudIcon.rectTransform, 5f, 2.4f);
            _cloudState = CommonUI.Label(_account, "", TextStyle.Body, new Vector2(textW, 52f), TextAlignmentOptions.Left, 40f);
            UIKit.Place(_cloudState.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 52f), new Vector2(156f, -178f));
            _lastSync = CommonUI.Label(_account, "", TextStyle.Small, new Vector2(textW, 44f), TextAlignmentOptions.Left, 32f);
            UIKit.Place(_lastSync.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 44f), new Vector2(156f, -232f));

            float bw = (_w - 3f * DS.Space.M) * 0.5f;
            _syncBtn = UIKit.ButtonLoc(_account, "cloud.sync_now", ButtonColor.Blue, new Vector2(bw, 124f), SyncNow, "icon_cloud");
            UIKit.Place((RectTransform)_syncBtn.transform, new Vector2(0f, 0f), new Vector2(bw, 124f), new Vector2(DS.Space.M, 36f));
            _syncSpinner = CommonUI.Spinner(_syncBtn.Visual, 64f, Color.white);
            _syncSpinner.SetVisible(false);
            var signOut = UIKit.ButtonLoc(_account, "auth.sign_out", ButtonColor.Gray, new Vector2(bw, 124f), SignOut, "icon_logout");
            UIKit.Place((RectTransform)signOut.transform, new Vector2(1f, 0f), new Vector2(bw, 124f), new Vector2(-DS.Space.M, 36f));

            RefreshCloudRow();
            return h;
        }

        float BuildSignedOut()
        {
            float y = 36f;
            var bg = CommonUI.Card(_account, new Vector2(_w, 400f));
            UIKit.Stretch(bg.rectTransform);
            var benefit = CommonUI.LocLabel(_account, "auth.login_benefit", TextStyle.Body, new Vector2(_w - 80f, 130f),
                TextAlignmentOptions.Center, 42f);
            UIKit.Place(benefit.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 80f, 130f), new Vector2(0f, -y));
            y += 130f + DS.Space.M;
            if (SignInButtons.AnyProviderAvailable)
            {
                float bw = Mathf.Min(_w - 80f, 720f);
                var sb = SignInButtons.Create(_account, bw, null);
                UIKit.Place((RectTransform)sb.transform, new Vector2(0.5f, 1f), new Vector2(bw, sb.Height), new Vector2(0f, -y));
                y += sb.Height + DS.Space.M;
            }
            else
            {
                var off = CommonUI.LocLabel(_account, "auth.error.cloud_disabled", TextStyle.Small, new Vector2(_w - 80f, 110f));
                UIKit.Place(off.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 80f, 110f), new Vector2(0f, -y));
                y += 110f + DS.Space.M;
            }
            if (AuthService.UsingMock)
            {
                var mock = CommonUI.LocLabel(_account, "auth.mock_notice", TextStyle.Small, new Vector2(_w - 80f, 46f),
                    TextAlignmentOptions.Center, 30f);
                UIKit.Place(mock.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 80f, 46f), new Vector2(0f, -y));
                y += 46f + DS.Space.S;
            }
            return y + 10f;
        }

        void RefreshCloudRow()
        {
            if (_cloudState == null) return;
            var state = CloudSave.State;
            _cloudState.text = Loc.T(CloudSave.StateLocKey);
            switch (state)
            {
                case SyncState.Synced: _cloudState.color = Color.Lerp(DS.Colors.Primary, DS.Colors.Ink, 0.35f); break;
                case SyncState.Error: _cloudState.color = DS.Colors.Danger; break;
                case SyncState.Syncing: _cloudState.color = DS.Colors.Secondary; break;
                default: _cloudState.color = DS.Colors.Ink; break;
            }
            RefreshLastSync();
            bool syncing = state == SyncState.Syncing;
            if (_syncBtn != null)
            {
                _syncBtn.Interactable = !syncing;
                if (_syncBtn.ContentRow != null) _syncBtn.ContentRow.gameObject.SetActive(!syncing);
            }
            if (_syncSpinner != null) _syncSpinner.SetVisible(syncing);
        }

        void RefreshLastSync()
        {
            if (_lastSync == null) return;
            long at = CloudSave.LastSyncAt;
            if (at <= 0)
            {
                _lastSync.text = Loc.T("cloud.never_synced");
                return;
            }
            long ago = System.Math.Max(0, TimeUtil.Now - at);
            // Under an hour: whole minutes ("12m ago"), not FormatDuration's mm:ss countdown style that reads like a clock.
            string when = ago < 60 ? Loc.T("tabs.profile.just_now")
                : Loc.T("tabs.profile.ago", ago < 3600 ? Loc.T("time.fmt_m", ago / 60) : TimeUtil.FormatDuration(ago));
            _lastSync.text = Loc.T("cloud.last_sync", when);
        }

        void SyncNow()
        {
            AudioManager.Play(Sfx.Whoosh);
            CloudSave.SyncNow(ok =>
            {
                if (ok)
                {
                    AudioManager.Play(Sfx.Sparkle);
                    UIKit.Toast(Loc.T("cloud.state.synced"));
                    if (this != null && _cloudIcon != null) FX.Sparkles(null, FX.LocalCenterOf(_cloudIcon.rectTransform), 10);
                }
                else
                {
                    AudioManager.Play(Sfx.Error);
                    UIKit.Toast(Loc.T(string.IsNullOrEmpty(CloudSave.LastErrorKey) ? "cloud.error.failed" : CloudSave.LastErrorKey));
                }
                if (this != null) RefreshCloudRow();
            });
            RefreshCloudRow();
        }

        void SignOut()
        {
            ConfirmPopup.Open("tabs.profile.sign_out_title", "tabs.profile.sign_out_confirm", "auth.sign_out", "ui.cancel", yes =>
            {
                if (!yes) return;
                AuthService.SignOut();
                AudioManager.Play(Sfx.Whoosh);
            });
        }

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshLastSync();
        }
    }
}
