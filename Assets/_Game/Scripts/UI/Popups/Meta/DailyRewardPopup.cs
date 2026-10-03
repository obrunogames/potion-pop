// ============================================================================================================
// Daily Reward: the 7-day calendar (claimed days checked, today highlighted and bouncing, day 7 = big chest).
// Claim → DailyRewards.Claim → check stamp → the rewards fly to the top bar (day 7 opens the reward reveal).
// Once claimed it shows the countdown to tomorrow's reward.
// ============================================================================================================
using System;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    public class DailyRewardPopup : Popup
    {
        const float CardW = 248f, CardH = 280f, Gap = 22f;

        readonly RectTransform[] _cards = new RectTransform[DailyRewards.CycleLength];
        UIButton _claim;
        TMP_Text _countdown;
        float _tick;
        bool _claimed, _delivered, _big, _held;
        Reward[] _granted;
        Vector3 _from;

        protected override string TitleKey => "home.daily_title";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, 1420f);

        public static void Open() => Open(null);

        public static void Open(Action onClosed)
        {
            if (PopupManager.IsOpen<DailyRewardPopup>())
            {
                onClosed?.Invoke();
                return;
            }
            var popup = PopupManager.Show<DailyRewardPopup>();
            if (popup != null && onClosed != null) popup.OnClosed += onClosed;
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            var desc = MetaUI.LocLabel(content, "home.daily_desc", TextStyle.Body, 36f, new Vector2(w, 60f), new Vector2(0.5f, 1f), new Vector2(0f, 0f));
            desc.color = DS.Colors.InkSoft;

            bool canClaim = DailyRewards.CanClaim;
            int today = DailyRewards.CurrentDay;
            float top = 76f;
            for (int day = 0; day < DailyRewards.CycleLength; day++)
            {
                bool chest = DailyRewards.IsChestDay(day);
                int row = day / 3, col = day % 3;
                Vector2 size = chest ? new Vector2(CardW * 3f + Gap * 2f, CardH) : new Vector2(CardW, CardH);
                float x = chest ? 0f : (col - 1) * (CardW + Gap);
                float y = -(top + row * (CardH + Gap) + CardH * 0.5f);
                var card = BuildCard(content, day, size, chest, day == today, DailyRewards.IsDayClaimed(day));
                UIKit.Place(card, new Vector2(0.5f, 1f), size, Vector2.zero);
                card.pivot = new Vector2(0.5f, 0.5f);
                card.anchoredPosition = new Vector2(x, y);
                _cards[day] = card;
            }
            MetaUI.PopInAll(_cards, 0.12f, 0.05f);

            // Today's card bounces.
            if (today >= 0 && today < _cards.Length && canClaim)
            {
                var t = _cards[today];
                Tween.Scale(t, 1.06f, 0.5f, Ease.InOutSine).SetLoops(-1, true).SetDelay(0.12f + today * 0.05f + DS.Motion.Slow);
            }

            _countdown = MetaUI.Label(content, "", TextStyle.H3, 46f, new Vector2(w, 64f), new Vector2(0.5f, 0f), new Vector2(0f, 196f));
            _countdown.color = DS.Colors.Orange;
            _claim = UIKit.ButtonLoc(content, canClaim ? "ui.claim" : "ui.ok", canClaim ? ButtonColor.Green : ButtonColor.Blue, ButtonSize.Large,
                canClaim ? (Action)OnClaim : Close);
            var crt = (RectTransform)_claim.transform;
            UIKit.Place(crt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), Vector2.zero);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0f, 6f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);
            if (canClaim) Tween.Pulse(crt);
            RefreshCountdown();
        }

        RectTransform BuildCard(RectTransform parent, int day, Vector2 size, bool chest, bool today, bool claimed)
        {
            var root = UIKit.Rect("Day" + (day + 1), parent);
            root.sizeDelta = size;
            if (today && !claimed)
            {
                var glow = UIKit.NewImage(root, "Glow", UISprites.Glow, new Color(1f, 0.85f, 0.25f, 0.9f));
                UIKit.Stretch(glow.rectTransform, -50f, -50f, -50f, -50f);
                Tween.Scale(glow.transform, 1.05f, 0.6f, Ease.InOutSine).SetLoops(-1, true);
                var rim = UIKit.RoundedRect(root, Vector2.zero, DS.Colors.Accent, 50f);
                UIKit.Stretch(rim.rectTransform, -10f, -10f, -10f, -10f);
            }
            var card = UIKit.Panel(root, "panel_card", size);
            UIKit.Stretch(card.rectTransform);
            if (claimed) card.color = new Color(0.86f, 0.84f, 0.9f, 1f);

            // Header capsule "Day N".
            Color headColor = claimed ? DS.Colors.Gray : (today ? DS.Colors.Orange : (chest ? DS.Colors.Pink : DS.Colors.Brand));
            var head = UIKit.Capsule(root, new Vector2(Mathf.Min(size.x - 40f, 220f), 56f), headColor);
            UIKit.Place(head.rectTransform, new Vector2(0.5f, 1f), head.rectTransform.sizeDelta, new Vector2(0f, -14f));
            var headText = UIKit.LocText(head.rectTransform, "time.day", TextStyle.H3, head.rectTransform.sizeDelta, day + 1);
            DS.Apply(headText, TextStyle.H3, 36f);
            UIKit.Stretch(headText.rectTransform, 10f, 2f, 10f, 6f);

            var rewards = DailyRewards.DayRewards(day);
            var body = UIKit.Rect("Body", root);
            UIKit.Stretch(body, 10f, 80f, 10f, 14f);
            if (chest)
            {
                var burst = MetaUI.Sunburst(body, 250f, 0.6f, 18f);
                UIKit.Place(burst.rectTransform, new Vector2(0f, 0.5f), new Vector2(250f, 250f), new Vector2(15f, 0f));
                var img = UIKit.Image(body, claimed ? "chest_open" : "chest_closed", new Vector2(200f, 180f));
                UIKit.Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(200f, 180f), new Vector2(40f, 0f));
                if (!claimed) Tween.Rotate(img.transform, 4f, 0.7f, Ease.InOutSine).SetLoops(-1, true);
                float startX = 270f, cell = (size.x - 20f - startX) / Mathf.Max(1, rewards.Length);
                for (int i = 0; i < rewards.Length; i++)
                {
                    var item = MetaUI.RewardItem(body, rewards[i], Mathf.Min(86f, cell - 14f), 32f);
                    UIKit.Place(item, new Vector2(0f, 0.5f), item.sizeDelta, new Vector2(startX + i * cell + (cell - item.sizeDelta.x) * 0.5f, 8f));
                }
            }
            else if (rewards.Length == 1)
            {
                var item = MetaUI.RewardItem(body, rewards[0], 118f, 42f);
                UIKit.Place(item, new Vector2(0.5f, 0.5f), item.sizeDelta, new Vector2(0f, 10f));
            }
            else
            {
                float cell = (size.x - 20f) / rewards.Length;
                for (int i = 0; i < rewards.Length; i++)
                {
                    var item = MetaUI.RewardItem(body, rewards[i], 86f, 34f);
                    UIKit.Place(item, new Vector2(0.5f, 0.5f), item.sizeDelta, new Vector2((i - (rewards.Length - 1) * 0.5f) * cell, 10f));
                }
            }
            if (claimed)
            {
                var check = MetaUI.CheckStamp(root, 96f, false);
                UIKit.Place(check, new Vector2(chest ? 1f : 0.5f, 0.5f), new Vector2(96f, 96f), new Vector2(chest ? -70f : 0f, -10f));
                check.pivot = new Vector2(0.5f, 0.5f);
            }
            return root;
        }

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshCountdown();
        }

        void RefreshCountdown()
        {
            if (_countdown == null) return;
            bool show = !DailyRewards.CanClaim;
            _countdown.gameObject.SetActive(show);
            if (show) _countdown.text = Loc.T("home.daily_next", TimeUtil.FormatDuration(DailyRewards.SecondsToNextClaim));
        }

        void OnClaim()
        {
            if (_claimed || IsClosing || !DailyRewards.CanClaim) return;
            _claimed = true;
            int day = DailyRewards.CurrentDay;
            var rewards = DailyRewards.Claim();
            var bar = TopBar.Instance;
            if (bar != null) bar.Hold(rewards);
            if (_claim != null)
            {
                Tween.Kill(_claim.transform);
                _claim.transform.localScale = Vector3.one;
                _claim.Interactable = false;
            }

            var card = day >= 0 && day < _cards.Length ? _cards[day] : null;
            if (card != null)
            {
                Tween.Kill(card);
                card.localScale = Vector3.one;
                var check = MetaUI.CheckStamp(card, 104f, true);
                bool chest = DailyRewards.IsChestDay(day);
                UIKit.Place(check, new Vector2(chest ? 1f : 0.5f, 0.5f), new Vector2(104f, 104f), new Vector2(chest ? -70f : 0f, -10f));
                check.pivot = new Vector2(0.5f, 0.5f);
                Tween.Punch(card, 0.12f, 0.35f);
                MetaUI.Celebrate(card, 12, 18);
            }
            AudioManager.Play(Sfx.Reward);
            Haptics.Play(HapticType.Success);

            _granted = rewards;
            _held = bar != null;
            _from = card != null ? MetaUI.WorldCenter(card) : MetaUI.WorldCenter(Panel);
            _big = DailyRewards.IsChestDay(day);
            Tween.Delay(0.6f, () => { if (this != null) Close(); }).SetLink(this);
        }

        /// <summary>Hands the claimed rewards to the top bar (fly) or to the reward reveal (day 7), exactly once —
        /// also when the popup is closed early (back button) right after claiming.</summary>
        protected override void OnClosing()
        {
            if (!_claimed || _delivered || _granted == null) return;
            _delivered = true;
            if (_big) RewardPopup.Open(_granted, "home.daily_title", null, _held);
            else TopBar.Fly(_granted, _from, null, _held);
        }
    }
}
