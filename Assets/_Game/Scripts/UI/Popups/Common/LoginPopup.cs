using System;
using PotionPop.Services;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Sign in with Google / Apple to save progress in the cloud. onDone(true) when signed in.</summary>
    public class LoginPopup : Popup
    {
        Action<bool> _onDone;
        bool _signedIn;
        bool _reported;
        RectTransform _mascot;

        protected override string TitleKey => "tabs.login.title";
        protected override Vector2 PanelSize => new Vector2(900f, 1380f);

        public static void Open(Action<bool> onDone = null)
        {
            if (AuthService.IsSignedIn)
            {
                CommonUI.SafeInvoke(onDone, true);
                return;
            }
            // Never stack two (e.g. the post-level-10 nudge while the ranking banner was tapped): the second caller
            // shares the open popup's answer.
            var open = PopupManager.Get<LoginPopup>();
            if (open != null)
            {
                if (onDone != null) open._onDone += onDone;
                return;
            }
            PopupManager.Show<LoginPopup>(p => p._onDone = onDone);
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            float y = 0f;

            // Luna pointing at the benefits, with a soft glow behind her.
            const float mascotH = 330f;
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(size.x, mascotH), new Vector2(0f, -y));
            var glow = UIKit.NewImage(stage, "Glow", UISprites.Glow, DS.WithAlpha(DS.Colors.Accent, 0.55f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(mascotH * 1.25f, mascotH * 1.25f), Vector2.zero);
            Tween.Pulse(glow.transform, 1.12f, 2.4f);
            var cloud = UIKit.Image(stage, "icon_cloud", new Vector2(170f, 125f));
            UIKit.Place(cloud.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(170f, 125f), new Vector2(170f, 70f));
            Tween.Bob(cloud.rectTransform, 10f, 2.6f);
            var mascot = UIKit.Image(stage, "mascot_point", new Vector2(mascotH * 0.72f, mascotH));
            _mascot = mascot.rectTransform;
            UIKit.Place(_mascot, new Vector2(0.5f, 0.5f), new Vector2(mascotH * 0.72f, mascotH), new Vector2(-90f, 0f));
            Tween.Bob(_mascot, 8f, 2.2f);
            y += mascotH + DS.Space.S;

            var headline = CommonUI.LocLabel(content, "tabs.login.benefits", TextStyle.Body, new Vector2(size.x, 110f),
                TextAlignmentOptions.Center, 46f);
            UIKit.Place(headline.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, 110f), new Vector2(0f, -y));
            y += 110f + DS.Space.S;

            string[] icons = { "icon_cloud", "icon_heart", "icon_trophy" };
            string[] keys = { "tabs.login.benefit_cloud", "tabs.login.benefit_devices", "tabs.login.benefit_ranking" };
            for (int i = 0; i < icons.Length; i++)
            {
                var row = UIKit.Rect("Benefit", content);
                UIKit.Place(row, new Vector2(0.5f, 1f), new Vector2(size.x - 40f, 76f), new Vector2(0f, -y));
                var bg = UIKit.Capsule(row, new Vector2(size.x - 40f, 76f), DS.WithAlpha(DS.Colors.CreamDark, 0.7f));
                UIKit.Stretch(bg.rectTransform);
                var ic = UIKit.Image(row, icons[i], new Vector2(64f, 64f));
                UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(64f, 64f), new Vector2(18f, 0f));
                var t = CommonUI.LocLabel(row, keys[i], TextStyle.Body, new Vector2(size.x - 160f, 64f), TextAlignmentOptions.Left, 38f);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                UIKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(size.x - 160f, 64f), new Vector2(100f, 0f));
                CommonUI.SlideIn(row, row.gameObject.AddComponent<CanvasGroup>(), new Vector2(-60f, 0f), 0.15f + i * 0.08f);
                y += 76f + DS.Space.XS + 4f;
            }
            y += DS.Space.S;

            float buttonsW = Mathf.Min(size.x - 20f, 720f);
            if (SignInButtons.AnyProviderAvailable)
            {
                var sb = SignInButtons.Create(content, buttonsW, OnSignInResult);
                UIKit.Place((RectTransform)sb.transform, new Vector2(0.5f, 1f), new Vector2(buttonsW, sb.Height), new Vector2(0f, -y));
                CommonUI.PopIn(sb.transform, 0.35f);
            }
            else
            {
                var off = CommonUI.LocLabel(content, "auth.error.cloud_disabled", TextStyle.Small, new Vector2(size.x, 120f));
                UIKit.Place(off.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, 120f), new Vector2(0f, -y));
            }

            var later = UIKit.ButtonLoc(content, "tabs.login.not_now", ButtonColor.Gray, new Vector2(380f, 110f), Close);
            UIKit.Place((RectTransform)later.transform, new Vector2(0.5f, 0f), new Vector2(380f, 110f), Vector2.zero);
        }

        void OnSignInResult(bool ok)
        {
            if (!ok || this == null || IsClosing) return;
            _signedIn = true;
            if (Panel != null) FX.Celebrate(FX.LocalCenterOf(Panel));
            if (_mascot != null)
            {
                var img = _mascot.GetComponent<UnityEngine.UI.Image>();
                var cheer = UISprites.Get("mascot_cheer");
                if (img != null && cheer != null) img.sprite = cheer;
                Tween.Punch(_mascot, 0.25f, 0.4f);
            }
            Tween.Delay(0.9f, () => { if (this != null && !IsClosing) Close(); }).SetLink(this);
        }

        protected override void OnClosing()
        {
            if (_reported) return;
            _reported = true;
            CommonUI.SafeInvoke(_onDone, _signedIn || AuthService.IsSignedIn);
        }
    }
}
