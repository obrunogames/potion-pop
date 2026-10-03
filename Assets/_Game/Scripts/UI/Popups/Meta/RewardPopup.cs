// ============================================================================================================
// Generic reward reveal: a slowly spinning sunburst behind the rewards, which pop in one by one (icon + amount,
// sound with rising pitch, sparkles). "Claim" closes the popup and flies coins / stars / hearts to the top bar
// (whose counters count up as they land). Rewards are granted by the caller; this popup only shows them.
// ============================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Generic reward reveal (sunburst, items pop in one by one, "Claim" flies them to the top bar).
    /// Rewards must already be granted by the caller (this popup only shows them).</summary>
    public class RewardPopup : Popup
    {
        const int PerRow = 4;

        Reward[] _rewards = Array.Empty<Reward>();
        string _titleKey = "reward.title";
        bool _alreadyHeld;
        bool _held;
        bool _claimed;
        readonly List<RectTransform> _items = new List<RectTransform>();
        UIButton _claim;

        protected override string TitleKey => _titleKey;
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(880f, Rows <= 1 ? 800f : 1000f);

        int Rows => Mathf.Max(1, (_rewards.Length + PerRow - 1) / PerRow);

        public static void Open(Reward[] rewards, string titleKey = "reward.title", Action onClosed = null) =>
            Open(rewards, titleKey, onClosed, false);

        /// <summary>
        /// alreadyHeld: the caller already called TopBar.Hold for these rewards (e.g. it granted them before an
        /// animation); otherwise the popup holds the ones granted in the last frames (TopBar.HoldIfFresh).
        /// </summary>
        public static void Open(Reward[] rewards, string titleKey, Action onClosed, bool alreadyHeld)
        {
            var list = new List<Reward>();
            if (rewards != null)
                for (int i = 0; i < rewards.Length; i++)
                    if (!rewards[i].IsEmpty) list.Add(rewards[i]);
            if (list.Count == 0)
            {
                if (alreadyHeld && TopBar.Instance != null) TopBar.Instance.Unhold(rewards);
                onClosed?.Invoke();
                return;
            }
            var popup = PopupManager.Show<RewardPopup>(p =>
            {
                p._rewards = list.ToArray();
                p._titleKey = string.IsNullOrEmpty(titleKey) ? "reward.title" : titleKey;
                p._alreadyHeld = alreadyHeld;
            });
            if (popup != null && onClosed != null) popup.OnClosed += onClosed;
        }

        protected override void BuildContent(RectTransform content)
        {
            var bar = TopBar.Instance;
            if (bar != null) _held = _alreadyHeld || bar.HoldIfFresh(_rewards);

            Vector2 size = content.rect.size;
            float areaH = size.y - 200f;

            // Rewards area with the sunburst behind.
            var area = UIKit.Rect("Rewards", content);
            area.anchorMin = new Vector2(0f, 1f);
            area.anchorMax = new Vector2(1f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(0f, areaH);
            area.anchoredPosition = Vector2.zero;

            var glow = UIKit.NewImage(area, "Glow", UISprites.Glow, new Color(1f, 0.93f, 0.55f, 0.75f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(620f, 620f), Vector2.zero);
            Tween.Scale(glow.transform, 1.1f, 1.2f, Ease.InOutSine).SetLoops(-1, true);
            var burst = MetaUI.Sunburst(area, Mathf.Min(720f, areaH + 160f), 0.85f, 16f);
            if (burst != null) UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), burst.rectTransform.sizeDelta, Vector2.zero);

            int n = _rewards.Length;
            int rows = Rows;
            float icon = n <= 2 ? 200f : (n <= 4 ? 150f : 130f);
            float cellW = Mathf.Min(220f, (size.x - 20f) / Mathf.Min(n, PerRow));
            float cellH = icon + 80f;
            float totalH = rows * cellH;
            for (int i = 0; i < n; i++)
            {
                int row = i / PerRow;
                int inRow = Mathf.Min(PerRow, n - row * PerRow);
                int col = i % PerRow;
                float x = (col - (inRow - 1) * 0.5f) * cellW;
                float y = totalH * 0.5f - row * cellH - cellH * 0.5f + 20f;
                var item = MetaUI.RewardItem(area, _rewards[i], icon, n <= 2 ? 60f : 48f);
                UIKit.Place(item, new Vector2(0.5f, 0.5f), item.sizeDelta, new Vector2(x, y));
                item.pivot = new Vector2(0.5f, 0.5f);
                item.anchoredPosition = new Vector2(x, y);
                _items.Add(item);
            }

            // Staggered reveal.
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                int index = i;
                item.localScale = Vector3.zero;
                float delay = 0.3f + i * 0.2f;
                Tween.Scale(item, 1f, 0.42f, Ease.OutBack).SetOvershoot(2.2f).SetDelay(delay).OnComplete(() =>
                {
                    if (item == null) return;
                    AudioManager.Play(index == 0 ? Sfx.Reward : Sfx.Pop, 1f, 1f + index * 0.06f);
                    Haptics.Play(HapticType.Light);
                    MetaUI.Celebrate(MetaUI.IconOf(item), 6, 0);
                    var iconRt = MetaUI.IconOf(item);
                    if (iconRt != null)
                    {
                        Tween.Kill(iconRt);
                        Tween.MoveLocal(iconRt, iconRt.localPosition + new Vector3(0f, 10f, 0f), 1.1f, Ease.InOutSine).SetLoops(-1, true).SetDelay(index * 0.15f);
                    }
                });
            }

            _claim = UIKit.ButtonLoc(content, "ui.claim", ButtonColor.Green, ButtonSize.Large, Claim);
            UIKit.Place((RectTransform)_claim.transform, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 6f));
            var claimRt = (RectTransform)_claim.transform;
            claimRt.localScale = Vector3.zero;
            Tween.Scale(claimRt, 1f, 0.4f, Ease.OutBack).SetDelay(0.3f + n * 0.2f).OnComplete(() =>
            {
                if (claimRt != null) Tween.Pulse(claimRt);
            });
        }

        protected override void OnOpened()
        {
            AudioManager.Play(Sfx.Fanfare, 0.8f);
            Haptics.Play(HapticType.Success);
        }

        /// <summary>Back = claim (nothing is lost).</summary>
        public override void OnBack() => Claim();

        void Claim()
        {
            if (_claimed || IsClosing) return;
            _claimed = true;
            if (_claim != null) _claim.Interactable = false;
            // Fly every reward from its own icon; the popup fades out meanwhile so the counters are in plain view.
            var bar = TopBar.Instance;
            for (int i = 0; i < _rewards.Length && i < _items.Count; i++)
            {
                var r = _rewards[i];
                var from = MetaUI.WorldCenter(MetaUI.IconOf(_items[i]));
                if (bar != null && bar.IsShown && bar.TargetFor(r.type) != null)
                {
                    TopBar.Fly(new[] { r }, from, null, _held);
                }
                else
                {
                    // No visible counter to fly to (boosters/cards, or the bar is hidden): celebrate in place.
                    MetaUI.Celebrate(MetaUI.IconOf(_items[i]), 8, 10);
                    if (_held && bar != null) bar.Release(r.type, r.amount);
                }
            }
            _held = false;
            Close();
        }

        protected override void OnClosing()
        {
            // Closed by code without claiming: give the held amounts back (with a count-up).
            if (!_held) return;
            _held = false;
            var bar = TopBar.Instance;
            if (bar == null) return;
            for (int i = 0; i < _rewards.Length; i++) bar.Release(_rewards[i].type, _rewards[i].amount);
        }
    }
}
