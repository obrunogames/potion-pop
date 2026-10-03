using System;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Enlarged collection card floating over the dim overlay (no panel), with a wiggle and sparkles.</summary>
    public class CardZoomPopup : Popup
    {
        string _productId;
        RectTransform _card;

        protected override Vector2 PanelSize => new Vector2(720f, 1240f);
        // The panel surface is hidden: a round X floating at the corner of an invisible panel looks broken. Tapping
        // anywhere (the card included: nothing in it catches raycasts) or back closes it.
        protected override bool ShowClose => false;

        public override void OnBack() => Close();

        public static void Open(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            PopupManager.Show<CardZoomPopup>(p => p._productId = productId);
        }

        protected override void BuildContent(RectTransform content)
        {
            // Card only: hide the panel surface and its shadow.
            if (PanelImage != null) PanelImage.enabled = false;
            var shadow = Panel != null ? Panel.Find("Shadow") : null;
            if (shadow != null) shadow.gameObject.SetActive(false);

            Vector2 size = content.rect.size;
            float cardW = Mathf.Min(560f, (size.y - 200f) / CommonUI.CardAspect, size.x);
            var holder = UIKit.Rect("Holder", content);
            UIKit.Place(holder, new Vector2(0.5f, 1f), new Vector2(cardW, cardW * CommonUI.CardAspect), new Vector2(0f, -20f));
            var burst = UIKit.Image(holder, "sunburst", new Vector2(cardW * 1.9f, cardW * 1.9f));
            burst.color = DS.WithAlpha(Color.Lerp(CommonUI.ProductAccent(_productId), Color.white, 0.4f), 0.8f);
            UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(cardW * 1.9f, cardW * 1.9f), Vector2.zero);
            Tween.Rotate(burst.transform, 360f, 18f, Ease.Linear).SetLoops(-1, false);
            _card = CommonUI.CardFront(holder, _productId, cardW);
            UIKit.Place(_card, new Vector2(0.5f, 0.5f), _card.sizeDelta, Vector2.zero);

            var area = Catalog.GetArea(Catalog.AreaOfProduct(_productId));
            if (area != null)
            {
                var areaName = UIKit.LocText(content, area.NameKey, TextStyle.H2, new Vector2(size.x, 80f));
                UIKit.Place(areaName.rectTransform, new Vector2(0.5f, 0f), new Vector2(size.x, 80f), new Vector2(0f, 60f));
            }
            var hint = CommonUI.LocLabel(content, "ui.tap_to_continue", TextStyle.BodyLight, new Vector2(size.x, 50f), TextAlignmentOptions.Center, 34f);
            UIKit.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(size.x, 50f), new Vector2(0f, 0f));
            Tween.Fade(hint, 0.4f, 0.9f, Ease.InOutSine).SetLoops(-1, true);
        }

        protected override void OnOpened()
        {
            if (_card == null) return;
            AudioManager.Play(Sfx.CardFlip);
            Haptics.Play(HapticType.Light);
            FX.Sparkles(null, FX.LocalCenterOf(_card), 16);
            Wiggle(0);
        }

        static readonly float[] WiggleAngles = { 9f, -8f, 6f, -4f, 2f, 0f };

        void Wiggle(int step)
        {
            if (_card == null || step >= WiggleAngles.Length)
            {
                if (_card != null) Tween.Bob(_card, 8f, 2.4f);
                return;
            }
            Tween.Rotate(_card, WiggleAngles[step], 0.09f, Ease.InOutSine).OnComplete(() => Wiggle(step + 1));
        }

        protected override void OnOverlayTap() => Close();
    }

    /// <summary>Avatar picker: 8 avatars in a 4x2 grid, the current one ringed in green with a check.</summary>
    public class AvatarPickerPopup : Popup
    {
        RectTransform[] _discs;
        RectTransform[] _checks;

        protected override string TitleKey => "tabs.profile.choose_avatar";
        protected override Vector2 PanelSize => new Vector2(900f, 840f);

        public static void Open()
        {
            if (PopupManager.IsOpen<AvatarPickerPopup>()) return;
            PopupManager.Show<AvatarPickerPopup>();
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            var ids = PlayerProfile.Avatars;
            const int cols = 4;
            int rows = (ids.Length + cols - 1) / cols;
            float cell = Mathf.Min(190f, (size.x - (cols - 1) * DS.Space.S) / cols);
            float disc = cell * 0.9f;
            var grid = UIKit.Grid(content, new Vector2(size.x, rows * cell + (rows - 1) * DS.Space.M), new Vector2(cell, cell),
                new Vector2(DS.Space.S, DS.Space.M), cols);
            UIKit.Place((RectTransform)grid.transform, new Vector2(0.5f, 1f), new Vector2(size.x, rows * cell + (rows - 1) * DS.Space.M),
                new Vector2(0f, -DS.Space.M));
            _discs = new RectTransform[ids.Length];
            _checks = new RectTransform[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var c = UIKit.Rect("Avatar_" + id, grid.transform);
                var visual = UIKit.Stretch(UIKit.Rect("Visual", c));
                var avatar = CommonUI.Avatar(visual, "avatar_" + id, disc, DS.Colors.Lavender);
                UIKit.Place(avatar, new Vector2(0.5f, 0.5f), new Vector2(disc, disc), Vector2.zero);
                _discs[i] = avatar;
                var check = UIKit.Rect("Check", visual);
                UIKit.Place(check, new Vector2(1f, 0f), new Vector2(64f, 64f), new Vector2(-2f, 2f));
                var checkBg = UIKit.NewImage(check, "Bg", UISprites.Circle, DS.Colors.Primary);
                UIKit.Stretch(checkBg.rectTransform, -6f, -6f, -6f, -6f);
                var checkIcon = UIKit.Image(check, "icon_check", new Vector2(56f, 56f));
                UIKit.Stretch(checkIcon.rectTransform, 4f, 4f, 4f, 4f);
                _checks[i] = check;
                int index = i;
                CommonUI.MakeTappable(c, visual, () => Pick(index));
                CommonUI.PopIn(visual, 0.12f + i * 0.04f);
            }
            Refresh(-1);

            var done = UIKit.ButtonLoc(content, "ui.done", ButtonColor.Green, ButtonSize.Medium, Close);
            UIKit.Place((RectTransform)done.transform, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Medium), Vector2.zero);
        }

        void Pick(int index)
        {
            var ids = PlayerProfile.Avatars;
            if (index < 0 || index >= ids.Length) return;
            bool changed = PlayerProfile.Avatar != ids[index];
            PlayerProfile.SetAvatar(ids[index]);
            AudioManager.Play(changed ? Sfx.Pop : Sfx.Click);
            if (changed)
            {
                FX.Sparkles(null, FX.LocalCenterOf(_discs[index]), 10);
                Haptics.Play(HapticType.Selection);
            }
            Refresh(index);
        }

        void Refresh(int punched)
        {
            var ids = PlayerProfile.Avatars;
            string current = PlayerProfile.Avatar;
            for (int i = 0; i < ids.Length; i++)
            {
                bool on = ids[i] == current;
                CommonUI.SetAvatarRing(_discs[i], on ? DS.Colors.Primary : DS.Colors.Lavender);
                _checks[i].gameObject.SetActive(on);
                if (on && i == punched)
                {
                    Tween.Punch(_discs[i], 0.22f, 0.35f);
                    CommonUI.PopIn(_checks[i], 0f);
                }
            }
        }
    }

    /// <summary>
    /// Another real player's row in the global ranking (App Store guideline 1.2): avatar, name and "Report name"
    /// (reports/{id} for review, falling back to an e-mail to support when it can't be sent) or "Block player" (hidden
    /// from the ranking for good). Reporting blocks too. onDone(true) once the player is blocked.
    /// </summary>
    public class PlayerOptionsPopup : Popup
    {
        LeaderboardEntry _entry;
        Action<bool> _onDone;
        bool _acted, _reported;

        protected override string TitleKey => "tabs.lb.player_title";
        protected override Vector2 PanelSize => new Vector2(880f, 900f);

        public static void Open(LeaderboardEntry entry, Action<bool> onDone = null)
        {
            if (!LeaderboardService.CanModerate(entry) || PopupManager.IsOpen<PlayerOptionsPopup>()) return;
            PopupManager.Show<PlayerOptionsPopup>(p =>
            {
                p._entry = entry;
                p._onDone = onDone;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            const float avatarS = 170f, nameH = 76f, hintH = 170f, buttonH = 130f;
            float y = 0f;

            var avatar = CommonUI.Avatar(content, _entry.avatar, avatarS, DS.Colors.Lavender);
            UIKit.Place(avatar, new Vector2(0.5f, 1f), new Vector2(avatarS, avatarS), new Vector2(0f, -y));
            CommonUI.PopIn(avatar, 0.08f);
            y += avatarS + DS.Space.S;

            // Already the shown name: sanitized, and the default name when the filter refused it.
            var name = CommonUI.Label(content, _entry.name ?? "", TextStyle.Body, new Vector2(size.x, nameH), TextAlignmentOptions.Center, 56f);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, nameH), new Vector2(0f, -y));
            y += nameH + DS.Space.S;

            var hint = CommonUI.LocLabel(content, "tabs.lb.options_hint", TextStyle.Small, new Vector2(size.x - 20f, hintH),
                TextAlignmentOptions.Center, 38f);
            UIKit.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x - 20f, hintH), new Vector2(0f, -y));

            var block = UIKit.ButtonLoc(content, "tabs.lb.block", ButtonColor.Red, new Vector2(size.x, buttonH), Block);
            UIKit.Place((RectTransform)block.transform, new Vector2(0.5f, 0f), new Vector2(size.x, buttonH), Vector2.zero);
            var report = UIKit.ButtonLoc(content, "tabs.lb.report", ButtonColor.Orange, new Vector2(size.x, buttonH), Report);
            UIKit.Place((RectTransform)report.transform, new Vector2(0.5f, 0f), new Vector2(size.x, buttonH), new Vector2(0f, buttonH + DS.Space.M));
            CommonUI.PopIn(report.transform, 0.12f);
            CommonUI.PopIn(block.transform, 0.18f);
        }

        void Report()
        {
            if (_acted) return;
            _acted = true;
            LeaderboardEntry entry = _entry;
            // Thanks right away: the report blocks the player at once, and one that can't be written goes by e-mail.
            LeaderboardService.Report(entry, sent =>
            {
                if (!sent) MailReport(entry);
            });
            AudioManager.Play(Sfx.Pop);
            UIKit.Toast(Loc.T("tabs.lb.reported"));
            Close();
        }

        void Block()
        {
            if (_acted) return;
            _acted = true;
            LeaderboardService.Block(_entry.uid);
            AudioManager.Play(Sfx.Whoosh);
            UIKit.Toast(Loc.T("tabs.lb.blocked"));
            Close();
        }

        /// <summary>A report that could not be written (offline, signed out...): a pre-filled e-mail to support.</summary>
        static void MailReport(LeaderboardEntry entry)
        {
            var cfg = ServicesConfig.Load();
            string to = cfg != null ? (cfg.supportEmail ?? "").Trim() : "";
            if (to.Length == 0) return;
            string reporter = AuthService.IsSignedIn ? AuthService.User.uid : PlayerProfile.PlayerId;
            string body = Loc.T("tabs.lb.report_mail_body", entry.rawName ?? entry.name, entry.uid, reporter).Replace("\n", "\r\n");
            Application.OpenURL("mailto:" + to + "?subject=" + Uri.EscapeDataString(Loc.T("tabs.lb.report_mail_subject")) +
                                "&body=" + Uri.EscapeDataString(body));
        }

        protected override void OnClosing()
        {
            if (_reported) return;
            _reported = true;
            CommonUI.SafeInvoke(_onDone, _acted);
        }
    }
}
