// ============================================================================================================
// Out of lives: Luna napping on her spellbook (floating "Zzz"), a beating heart with the count, a live countdown to the next life,
// "Refill" for Lives.RefillPrice coins and "+1 life" for a rewarded ad. As soon as hearts are available (bought,
// watched or regenerated) the hearts fly to the top bar, the popup closes and onHeartsAvailable runs.
// ============================================================================================================
using System;
using PotionPop.Services;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>No hearts: countdown to next heart, refill for coins, +1 heart by rewarded ad.</summary>
    public class OutOfLivesPopup : Popup
    {
        Action _onHeartsAvailable;
        TMP_Text _countdown, _heartCount;
        RectTransform _heart;
        UIButton _refill, _ad;
        RectTransform _zzz;
        CanvasGroup _zzzGroup;
        float _tick;
        int _heartsAtOpen;
        bool _done;

        protected override string TitleKey => "home.lives_title";
        protected override Vector2 PanelSize => new Vector2(880f, 1260f);

        public static void Open(Action onHeartsAvailable = null)
        {
            var open = PopupManager.Get<OutOfLivesPopup>();
            if (open != null)
            {
                if (onHeartsAvailable != null) open._onHeartsAvailable += onHeartsAvailable;
                return;
            }
            // Full (or unlimited): nothing to refill. Some hearts left but not full: still useful (refill / ad).
            if (Lives.HasInfinite || Lives.IsFull)
            {
                onHeartsAvailable?.Invoke();
                return;
            }
            PopupManager.Show<OutOfLivesPopup>(p => p._onHeartsAvailable = onHeartsAvailable);
        }

        protected override void BuildContent(RectTransform content)
        {
            _heartsAtOpen = Lives.Hearts;
            float w = content.rect.width;

            // Luna asleep + heart.
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(w, 430f), Vector2.zero);
            var glow = UIKit.NewImage(stage, "Glow", UISprites.Glow, new Color(0.72f, 0.6f, 1f, 0.55f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560f, 520f), new Vector2(-60f, 0f));
            var luna = UIKit.Image(stage, "mascot_sleep", new Vector2(280f, 418f));
            UIKit.Place(luna.rectTransform, new Vector2(0.5f, 0f), new Vector2(280f, 418f), new Vector2(-150f, 0f));
            luna.rectTransform.pivot = new Vector2(0.5f, 0f);
            Tween.Scale(luna.transform, new Vector3(1.03f, 0.96f, 1f), 1.4f, Ease.InOutSine).SetLoops(-1, true);

            _zzz = UIKit.Rect("Zzz", stage);
            UIKit.Place(_zzz, new Vector2(0.5f, 0f), new Vector2(160f, 80f), new Vector2(-40f, 330f));
            _zzzGroup = _zzz.gameObject.AddComponent<CanvasGroup>();
            var z = UIKit.LocText(_zzz, "home.zzz", TextStyle.H2, new Vector2(160f, 80f));
            UIKit.Stretch(z.rectTransform);
            z.color = DS.Colors.BrandLight;
            LoopZzz();

            _heart = UIKit.Rect("Heart", stage);
            UIKit.Place(_heart, new Vector2(0.5f, 0.5f), new Vector2(250f, 220f), new Vector2(170f, 10f));
            var heartImg = UIKit.Image(_heart, "icon_heart", new Vector2(250f, 220f));
            UIKit.Stretch(heartImg.rectTransform);
            _heartCount = UIKit.Text(_heart, "0", TextStyle.H1, new Vector2(200f, 140f));
            DS.Apply(_heartCount, TextStyle.H1, 96f);
            UIKit.Stretch(_heartCount.rectTransform, 30f, 30f, 30f, 50f);
            HeartBeat();

            // Countdown + description.
            _countdown = MetaUI.Label(content, "", TextStyle.H3, 52f, new Vector2(w, 70f), new Vector2(0.5f, 1f), new Vector2(0f, -446f));
            _countdown.color = DS.Colors.Orange;
            var desc = MetaUI.LocLabel(content, "home.no_lives_desc", TextStyle.Body, 38f, new Vector2(w - 20f, 110f), new Vector2(0.5f, 1f), new Vector2(0f, -526f));
            desc.color = DS.Colors.InkSoft;

            // Buttons.
            var wide = new Vector2(w - 40f, 160f);
            _refill = UIKit.ButtonLoc(content, "home.refill", ButtonColor.Green, wide, OnRefill, "hearts_refill");
            _refill.SetPrice(Lives.RefillPrice);
            UIKit.Place((RectTransform)_refill.transform, new Vector2(0.5f, 0f), wide, new Vector2(0f, 190f));
            _ad = UIKit.ButtonLoc(content, "home.one_life", ButtonColor.Purple, wide, OnAd);
            _ad.SetAdBadge(true);
            UIKit.Place((RectTransform)_ad.transform, new Vector2(0.5f, 0f), wide, new Vector2(0f, 6f));
            Tween.Pulse(_refill.transform, 1.04f, 1f);

            Lives.OnChanged += OnLivesChanged;
            Refresh();
        }

        void OnDestroy() => Lives.OnChanged -= OnLivesChanged;

        void Update()
        {
            if (_done) return;
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            Refresh();
        }

        void Refresh()
        {
            int hearts = Lives.Hearts;
            if (_heartCount != null) _heartCount.text = hearts.ToString();
            if (_countdown != null)
                _countdown.text = Lives.IsFull ? Loc.T("hearts.full") : Loc.T("home.next_life", TimeUtil.FormatMMSS(Lives.SecondsToNext));
            if (_refill != null) _refill.Interactable = !Lives.IsFull;
        }

        void LoopZzz()
        {
            if (_zzz == null) return;
            _zzz.anchoredPosition = new Vector2(-40f, 320f);
            _zzz.localScale = Vector3.one * 0.6f;
            _zzzGroup.alpha = 0f;
            Tween.Fade(_zzzGroup, 1f, 0.5f);
            Tween.Scale(_zzz, 1.1f, 2f, Ease.OutQuad);
            Tween.Fade(_zzzGroup, 0f, 0.6f).SetDelay(1.4f);
            Tween.Move(_zzz, new Vector2(10f, 400f), 2f, Ease.OutQuad).OnComplete(() =>
            {
                if (this == null || _done) return;
                Tween.Delay(0.3f, LoopZzz).SetLink(this);
            });
        }

        void HeartBeat()
        {
            if (_heart == null) return;
            // lub-dub every 1.2 s
            Tween.Scale(_heart, 1.12f, 0.1f, Ease.OutQuad);
            Tween.Scale(_heart, 1f, 0.12f, Ease.InQuad).SetDelay(0.1f);
            Tween.Scale(_heart, 1.08f, 0.1f, Ease.OutQuad).SetDelay(0.24f);
            Tween.Scale(_heart, 1f, 0.16f, Ease.InQuad).SetDelay(0.34f);
            Tween.Delay(1.2f, () => { if (this != null && !_done) HeartBeat(); }).SetLink(this);
        }

        void OnRefill()
        {
            if (_done) return;
            if (Lives.IsFull) return;
            if (!Economy.CanAfford(Lives.RefillPrice))
            {
                AudioManager.Play(Sfx.Error);
                Haptics.Play(HapticType.Warning);
                Tween.Shake((RectTransform)_refill.transform, 16f, 0.35f);
                UIKit.Toast(Loc.T("common.not_enough_coins"));
                return;
            }
            if (Lives.TryBuyRefill()) AudioManager.Play(Sfx.Purchase);
        }

        void OnAd()
        {
            if (_done) return;
            AdsService.ShowRewarded(AdPlacement.ExtraHeart, earned =>
            {
                if (!earned || this == null) return;
                Lives.Add(1);
            });
        }

        void OnLivesChanged()
        {
            if (_done || this == null) return;
            Refresh();
            if (!Lives.CanPlay) return;
            // Opened with some hearts left (from the top bar): wait until there are more (or full / unlimited).
            if (_heartsAtOpen > 0 && !Lives.IsFull && !Lives.HasInfinite && Lives.Hearts <= _heartsAtOpen) return;
            _done = true;
            int gained = Mathf.Max(0, Lives.Hearts - _heartsAtOpen);
            var bar = TopBar.Instance;
            var from = MetaUI.WorldCenter(_heart);
            AudioManager.Play(Sfx.Heart);
            Haptics.Play(HapticType.Success);
            MetaUI.Celebrate(_heart, 12, 16);
            if (gained > 0 && bar != null)
            {
                var rewards = new[] { Reward.Hearts(gained) };
                bar.Hold(rewards);
                TopBar.Fly(rewards, from);
            }
            if (_onHeartsAvailable != null) OnClosed += _onHeartsAvailable;
            _onHeartsAvailable = null;
            Close();
        }
    }
}
