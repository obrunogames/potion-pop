using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Settings: music, sound, vibration, language, account (login/logout/delete), privacy/terms, version + player id.</summary>
    public class SettingsPopup : Popup
    {
        RectTransform _list;
        float _w;
        RectTransform _account;
        readonly UIButton[] _langChips = new UIButton[3];
        TMP_Text _footer;
        bool _deleting;

        protected override string TitleKey => "ui.settings";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, 1680f);

        public static void Open()
        {
            if (PopupManager.IsOpen<SettingsPopup>()) return;
            PopupManager.Show<SettingsPopup>();
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            var scroll = UIKit.ScrollView(content, size, out _list, DS.Space.S, DS.Space.XS);
            UIKit.Stretch((RectTransform)scroll.transform);
            _w = Mathf.Max(300f, size.x - DS.Space.XS * 2f);

            int i = 0;
            Stagger(ToggleRow("icon_music", "tabs.settings.music", AudioManager.MusicOn, v =>
            {
                AudioManager.MusicOn = v;
            }), i++);
            Stagger(ToggleRow("icon_sound", "tabs.settings.sound", AudioManager.SoundOn, v =>
            {
                AudioManager.SoundOn = v;
                if (v) AudioManager.Play(Sfx.Toggle);
            }), i++);
            Stagger(ToggleRow("icon_vibration", "tabs.settings.vibration", Haptics.Enabled, v =>
            {
                Haptics.Enabled = v;
                if (v) Haptics.Play(HapticType.Medium);
            }), i++);
            Stagger(LanguageRow(), i++);

            Stagger(SubHeader("tabs.profile.account"), i++);
            _account = UIKit.Rect("Account", _list);
            _account.sizeDelta = new Vector2(_w, 100f);
            BuildAccount();
            Stagger(_account, i++);

            Stagger(SubHeader("tabs.settings.more"), i++);
            Stagger(LegalRows(), i++);

            _footer = CommonUI.Label(_list, "", TextStyle.Small, new Vector2(_w, 60f), TextAlignmentOptions.Center, 32f);
            RefreshFooter();
            UIKit.Spacer(_list, new Vector2(_w, DS.Space.M));

            AuthService.OnAuthChanged += OnAuthChanged;
            Loc.OnLanguageChanged += OnLanguageChanged;
            SaveSystem.OnReplaced += OnSaveReplaced;
        }

        void OnDestroy()
        {
            AuthService.OnAuthChanged -= OnAuthChanged;
            Loc.OnLanguageChanged -= OnLanguageChanged;
            SaveSystem.OnReplaced -= OnSaveReplaced;
        }

        static void Stagger(RectTransform rt, int index)
        {
            if (rt == null) return;
            CommonUI.PopIn(rt, 0.08f + index * DS.Motion.Stagger);
        }

        // ---------------------------------------------------------------------------------------- rows

        RectTransform Row(float height, out RectTransform inner)
        {
            var row = UIKit.Rect("Row", _list);
            row.sizeDelta = new Vector2(_w, height);
            var bg = UIKit.RoundedRect(row, new Vector2(_w, height), DS.Colors.CreamDark, 34f);
            UIKit.Stretch(bg.rectTransform);
            var face = UIKit.RoundedRect(row, new Vector2(_w, height), Color.white, 34f);
            UIKit.Stretch(face.rectTransform, 0f, 0f, 0f, 6f);
            inner = UIKit.Stretch(UIKit.Rect("Inner", row), 24f, 0f, 24f, 6f);
            return row;
        }

        RectTransform ToggleRow(string icon, string key, bool value, System.Action<bool> onChanged)
        {
            var row = Row(128f, out var inner);
            var ic = UIKit.Image(inner, icon, new Vector2(84f, 84f));
            UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(84f, 84f), Vector2.zero);
            var label = CommonUI.LocLabel(inner, key, TextStyle.Body, new Vector2(_w - 330f, 90f), TextAlignmentOptions.Left, 46f);
            UIKit.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(_w - 330f, 90f), new Vector2(110f, 0f));
            var toggle = UIKit.Toggle(inner, value, v =>
            {
                // UIToggle already plays Sfx.Toggle + a selection haptic.
                Tween.Punch(ic.transform, 0.2f, 0.3f);
                onChanged?.Invoke(v);
            });
            UIKit.Place((RectTransform)toggle.transform, new Vector2(1f, 0.5f), UIToggle.DefaultSize, Vector2.zero);
            return row;
        }

        RectTransform LanguageRow()
        {
            var row = Row(240f, out var inner);
            var ic = UIKit.Image(inner, "icon_language", new Vector2(84f, 84f));
            UIKit.Place(ic.rectTransform, new Vector2(0f, 1f), new Vector2(84f, 84f), new Vector2(0f, -16f));
            var label = CommonUI.LocLabel(inner, "tabs.settings.language", TextStyle.Body, new Vector2(_w - 160f, 90f), TextAlignmentOptions.Left, 46f);
            UIKit.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(_w - 160f, 90f), new Vector2(110f, -12f));

            float innerW = _w - 48f;
            float chipW = (innerW - 2f * DS.Space.S) / 3f;
            for (int i = 0; i < Loc.Codes.Length && i < _langChips.Length; i++)
            {
                string code = Loc.Codes[i];
                var chip = UIKit.Button(inner, Loc.LanguageDisplayName(code), ButtonColor.Gray, new Vector2(chipW, 104f), () => PickLanguage(code));
                if (chip.label != null) DS.Apply(chip.label, TextStyle.H3, 40f);
                UIKit.Place((RectTransform)chip.transform, new Vector2(0f, 0f), new Vector2(chipW, 104f),
                    new Vector2(i * (chipW + DS.Space.S), 18f));
                _langChips[i] = chip;
            }
            RefreshLanguageChips(false);
            return row;
        }

        void PickLanguage(string code)
        {
            if (Loc.Language == code && !Loc.FollowsDevice) return;
            Loc.Language = code;   // raises OnLanguageChanged → every LocText refreshes
            AudioManager.Play(Sfx.Toggle);
            RefreshLanguageChips(true);
        }

        void RefreshLanguageChips(bool animate)
        {
            string current = Loc.Language;
            for (int i = 0; i < _langChips.Length; i++)
            {
                var chip = _langChips[i];
                if (chip == null) continue;
                bool on = i < Loc.Codes.Length && Loc.Codes[i] == current;
                chip.SetColor(on ? ButtonColor.Purple : ButtonColor.Gray);
                if (on && animate) Tween.Punch(chip.transform, 0.18f, 0.35f);
            }
        }

        RectTransform SubHeader(string key)
        {
            var block = UIKit.Rect("SubHeader", _list);
            block.sizeDelta = new Vector2(_w, 76f);
            var t = CommonUI.LocLabel(block, key, TextStyle.H3, new Vector2(_w - 20f, 64f), TextAlignmentOptions.Left, 46f);
            UIKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(_w - 20f, 64f), new Vector2(12f, 0f));
            return block;
        }

        // ---------------------------------------------------------------------------------------- account

        void BuildAccount()
        {
            if (_account == null) return;
            // Deactivate first: Destroy is deferred, the old rows would overlap the new ones for a frame.
            for (int i = _account.childCount - 1; i >= 0; i--)
            {
                var child = _account.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            float h;
            if (AuthService.IsSignedIn)
            {
                h = 360f;
                var user = AuthService.User;
                var bg = UIKit.RoundedRect(_account, new Vector2(_w, h), Color.white, 34f);
                UIKit.Stretch(bg.rectTransform);
                var logo = SignInButtons.ProviderLogo(_account, user.provider, 92f);
                UIKit.Place(logo, new Vector2(0f, 1f), new Vector2(92f, 92f), new Vector2(26f, -26f));
                var with = CommonUI.Label(_account, Loc.T("auth.signed_in_with", Loc.T(SignInButtons.ProviderNameKey(user.provider))),
                    TextStyle.Body, new Vector2(_w - 160f, 56f), TextAlignmentOptions.Left, 40f);
                UIKit.Place(with.rectTransform, new Vector2(0f, 1f), new Vector2(_w - 160f, 56f), new Vector2(136f, -22f));
                var name = CommonUI.Label(_account, string.IsNullOrEmpty(user.NameOrEmpty) ? PlayerProfile.DisplayName : user.NameOrEmpty,
                    TextStyle.Small, new Vector2(_w - 160f, 48f), TextAlignmentOptions.Left, 34f);
                UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(_w - 160f, 48f), new Vector2(136f, -76f));

                float bw = (_w - 3f * DS.Space.M) * 0.5f;
                var signOut = UIKit.ButtonLoc(_account, "auth.sign_out", ButtonColor.Blue, new Vector2(bw, 120f), SignOut, "icon_logout");
                UIKit.Place((RectTransform)signOut.transform, new Vector2(0f, 0f), new Vector2(bw, 120f), new Vector2(DS.Space.M, 110f));
                var delete = UIKit.ButtonLoc(_account, "auth.delete_account", ButtonColor.Red, new Vector2(bw, 120f), DeleteAccount);
                UIKit.Place((RectTransform)delete.transform, new Vector2(1f, 0f), new Vector2(bw, 120f), new Vector2(-DS.Space.M, 110f));
                var cloud = CommonUI.Label(_account, Loc.T(CloudSave.StateLocKey), TextStyle.Small, new Vector2(_w - 60f, 60f),
                    TextAlignmentOptions.Center, 32f);
                UIKit.Place(cloud.rectTransform, new Vector2(0.5f, 0f), new Vector2(_w - 60f, 60f), new Vector2(0f, 30f));
            }
            else
            {
                var benefit = CommonUI.LocLabel(_account, "auth.login_benefit", TextStyle.Body, new Vector2(_w - 40f, 120f),
                    TextAlignmentOptions.Center, 38f);
                UIKit.Place(benefit.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 40f, 120f), new Vector2(0f, 0f));
                h = 130f;
                if (SignInButtons.AnyProviderAvailable)
                {
                    float bw = Mathf.Min(_w - 40f, 700f);
                    var sb = SignInButtons.Create(_account, bw, null, 120f);
                    UIKit.Place((RectTransform)sb.transform, new Vector2(0.5f, 1f), new Vector2(bw, sb.Height), new Vector2(0f, -h));
                    h += sb.Height + DS.Space.S;
                }
                else
                {
                    var off = CommonUI.LocLabel(_account, "auth.error.cloud_disabled", TextStyle.Small, new Vector2(_w - 40f, 110f));
                    UIKit.Place(off.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 40f, 110f), new Vector2(0f, -h));
                    h += 120f;
                }
                if (AuthService.UsingMock)
                {
                    var mock = CommonUI.LocLabel(_account, "auth.mock_notice", TextStyle.Small, new Vector2(_w - 40f, 50f),
                        TextAlignmentOptions.Center, 30f);
                    UIKit.Place(mock.rectTransform, new Vector2(0.5f, 1f), new Vector2(_w - 40f, 50f), new Vector2(0f, -h));
                    h += 56f;
                }
            }
            _account.sizeDelta = new Vector2(_w, h);
            LayoutRebuilder.MarkLayoutForRebuild(_list);
        }

        void OnAuthChanged()
        {
            if (this == null || IsClosing) return;
            BuildAccount();
            if (_account != null) Tween.Punch(_account, 0.06f, 0.3f);
        }

        void OnSaveReplaced()
        {
            if (this == null) return;
            RefreshFooter();
            RefreshLanguageChips(false);
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

        void DeleteAccount()
        {
            if (_deleting) return;
            ConfirmPopup.Open("tabs.settings.delete_title", "auth.delete_confirm", "auth.delete_account", "ui.cancel", yes =>
            {
                if (!yes) return;
                _deleting = true;
                AuthService.DeleteAccount(ok =>
                {
                    _deleting = false;
                    if (ok)
                    {
                        AudioManager.Play(Sfx.Whoosh);
                        UIKit.Toast(Loc.T("auth.deleted"));
                    }
                    else
                    {
                        AudioManager.Play(Sfx.Error);
                        UIKit.Toast(Loc.T(string.IsNullOrEmpty(AuthService.LastErrorKey) ? "auth.error.delete_failed" : AuthService.LastErrorKey));
                    }
                });
            }, true);
        }

        // ---------------------------------------------------------------------------------------- legal + footer

        RectTransform LegalRows()
        {
            bool privacyOptions = AdsService.PrivacyOptionsRequired;
            bool designSystem = Application.isEditor || Debug.isDebugBuild;
            float bh = 116f;
            int rows = 1 + (privacyOptions ? 1 : 0) + (designSystem ? 1 : 0);
            var block = UIKit.Rect("Legal", _list);
            block.sizeDelta = new Vector2(_w, rows * bh + (rows - 1) * DS.Space.S);
            float bw = (_w - DS.Space.S) * 0.5f;
            var cfg = ServicesConfig.Load();

            var privacy = UIKit.ButtonLoc(block, "tabs.settings.privacy_policy", ButtonColor.Blue, new Vector2(bw, bh),
                () => OpenUrl(cfg != null ? cfg.privacyPolicyUrl : null));
            UIKit.Place((RectTransform)privacy.transform, new Vector2(0f, 1f), new Vector2(bw, bh), Vector2.zero);
            var terms = UIKit.ButtonLoc(block, "tabs.settings.terms", ButtonColor.Blue, new Vector2(bw, bh),
                () => OpenUrl(cfg != null ? cfg.termsUrl : null));
            UIKit.Place((RectTransform)terms.transform, new Vector2(1f, 1f), new Vector2(bw, bh), Vector2.zero);

            float y = bh + DS.Space.S;
            if (privacyOptions)
            {
                var opts = UIKit.ButtonLoc(block, "ads.privacy_options", ButtonColor.Purple, new Vector2(_w, bh),
                    () => AdsService.ShowPrivacyOptions(), "icon_info");
                UIKit.Place((RectTransform)opts.transform, new Vector2(0.5f, 1f), new Vector2(_w, bh), new Vector2(0f, -y));
                y += bh + DS.Space.S;
            }
            if (designSystem)
            {
                var ds = UIKit.ButtonLoc(block, "ui.ds.title", ButtonColor.Pink, new Vector2(_w, bh), () => DesignSystemPopup.Open(), "icon_settings");
                UIKit.Place((RectTransform)ds.transform, new Vector2(0.5f, 1f), new Vector2(_w, bh), new Vector2(0f, -y));
            }
            return block;
        }

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            Application.OpenURL(url);
        }

        void OnLanguageChanged()
        {
            if (this == null) return;
            RefreshFooter();
            RefreshLanguageChips(false);
            BuildAccount();   // texts composed with Loc.T
        }

        void RefreshFooter()
        {
            if (_footer != null) _footer.text = Loc.T("tabs.settings.footer", PlayerProfile.PlayerId, Application.version);
        }
    }
}
