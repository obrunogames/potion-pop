using System;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Card reveal after a win (flip animation; duplicate shows +coins). The card is already granted by the
    /// caller (Collection.GrantRandomCard / Progress.ReportWin): this popup only shows it.</summary>
    public class NewCardPopup : Popup
    {
        string _cardId;
        bool _duplicate;
        Action _onClosed;
        bool _reported;
        bool _revealed;

        RectTransform _holder, _front, _back, _badge, _stage;
        CanvasGroup _info;
        ProgressBar _bar;
        TMP_Text _barLabel;
        UIButton _collect;
        int _owned, _albumSize;

        protected override string TitleKey => _duplicate ? "tabs.card.duplicate_title" : "tabs.card.new_title";
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(860f, 1460f);

        public static void Open(string cardId, bool duplicate, Action onClosed = null)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                CommonUI.SafeInvoke(onClosed);
                return;
            }
            PopupManager.Show<NewCardPopup>(p =>
            {
                p._cardId = cardId;
                p._duplicate = duplicate;
                p._onClosed = onClosed;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            string areaId = Catalog.AreaOfCard(_cardId);
            var area = Catalog.GetArea(areaId);
            Color accent = DS.AreaAccent(areaId);
            _owned = Collection.OwnedCount(areaId);
            _albumSize = Mathf.Max(1, Collection.AlbumSize(areaId));

            // ---- stage: sunburst + glow + card
            float stageH = Mathf.Min(720f, size.y - 520f);
            _stage = UIKit.Rect("Stage", content);
            UIKit.Place(_stage, new Vector2(0.5f, 1f), new Vector2(size.x, stageH), Vector2.zero);
            var burst = UIKit.Image(_stage, "sunburst", new Vector2(stageH * 1.15f, stageH * 1.15f));
            burst.color = DS.WithAlpha(Color.Lerp(accent, Color.white, 0.4f), 0.95f);
            UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(stageH * 1.15f, stageH * 1.15f), Vector2.zero);
            Tween.Rotate(burst.transform, 360f, 16f, Ease.Linear).SetLoops(-1, false);
            burst.transform.localScale = Vector3.zero;
            Tween.Scale(burst.transform, 1f, 0.6f, Ease.OutBack).SetDelay(0.7f);
            var glow = UIKit.NewImage(_stage, "Glow", UISprites.Glow, DS.WithAlpha(accent, 0.7f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(stageH * 0.95f, stageH * 0.95f), Vector2.zero);
            Tween.Pulse(glow.transform, 1.1f, 1.8f);

            float cardW = Mathf.Min(400f, (stageH - 40f) / CommonUI.CardAspect);
            _holder = UIKit.Rect("Card", _stage);
            UIKit.Place(_holder, new Vector2(0.5f, 0.5f), new Vector2(cardW, cardW * CommonUI.CardAspect), Vector2.zero);
            _back = CommonUI.CardBack(_holder, cardW);
            UIKit.Place(_back, new Vector2(0.5f, 0.5f), _back.sizeDelta, Vector2.zero);
            _front = CommonUI.CardFront(_holder, _cardId, cardW);
            UIKit.Place(_front, new Vector2(0.5f, 0.5f), _front.sizeDelta, Vector2.zero);
            _front.gameObject.SetActive(false);

            // "NEW!" sticker on the card corner (pops after the flip).
            if (!_duplicate)
            {
                _badge = UIKit.Rect("NewBadge", _holder);
                UIKit.Place(_badge, new Vector2(1f, 1f), new Vector2(210f, 86f), new Vector2(-16f, -24f));
                _badge.pivot = new Vector2(0.5f, 0.5f);
                var sticker = UIKit.Capsule(_badge, new Vector2(210f, 86f), DS.Colors.Pink);
                UIKit.Stretch(sticker.rectTransform);
                var shine = UIKit.Capsule(_badge, new Vector2(180f, 26f), DS.WithAlpha(Color.white, 0.3f));
                UIKit.Place(shine.rectTransform, new Vector2(0.5f, 1f), new Vector2(180f, 26f), new Vector2(0f, -8f));
                var label = UIKit.LocText(_badge, "ui.new", TextStyle.H3, new Vector2(190f, 76f));
                UIKit.Stretch(label.rectTransform, 10f, 0f, 10f, 4f);
                _badge.localRotation = Quaternion.Euler(0f, 0f, -12f);
                _badge.gameObject.SetActive(false);
            }

            // ---- info: name, world, album progress, duplicate note (fades in after the flip)
            var info = UIKit.Rect("Info", content);
            UIKit.Stretch(info, 0f, stageH + DS.Space.S, 0f, 180f);
            _info = info.gameObject.AddComponent<CanvasGroup>();
            _info.alpha = 0f;
            float y = 0f;
            var name = UIKit.LocText(info, Catalog.CardNameKey(_cardId), TextStyle.H2, new Vector2(size.x, 84f));
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, 84f), new Vector2(0f, -y));
            y += 92f;
            if (area != null)
            {
                var areaName = CommonUI.LocLabel(info, area.NameKey, TextStyle.Body, new Vector2(size.x, 56f), TextAlignmentOptions.Center, 40f,
                    Color.Lerp(accent, DS.Colors.Ink, 0.45f));
                UIKit.Place(areaName.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, 56f), new Vector2(0f, -y));
                y += 72f;
            }
            float barW = Mathf.Min(560f, size.x - 120f);
            _bar = UIKit.ProgressBar(info, new Vector2(barW, 62f));
            UIKit.Place((RectTransform)_bar.transform, new Vector2(0.5f, 1f), new Vector2(barW, 62f), new Vector2(40f, -y));
            _bar.SetIcon("icon_collection");
            int startOwned = _duplicate ? _owned : Mathf.Max(0, _owned - 1);
            _bar.SetValue(startOwned / (float)_albumSize, false);
            _bar.SetLabel(Loc.T("quest.progress", startOwned, _albumSize));
            y += 92f;
            if (_duplicate)
            {
                var dup = CommonUI.LocLabel(info, "reward.card_duplicate", TextStyle.Body, new Vector2(size.x, 60f), TextAlignmentOptions.Center, 42f,
                    DS.Colors.Orange, Collection.DuplicateCoins);
                UIKit.Place(dup.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x, 60f), new Vector2(0f, -y));
            }

            _collect = UIKit.ButtonLoc(content, "ui.collect", ButtonColor.Green, ButtonSize.Large, OnCollect);
            var crt = (RectTransform)_collect.transform;
            UIKit.Place(crt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 10f));
            crt.localScale = Vector3.zero;

            PlayIntro();
        }

        void PlayIntro()
        {
            AudioManager.Play(Sfx.Whoosh);
            _holder.localScale = Vector3.zero;
            _holder.localRotation = Quaternion.identity;
            Tween.Scale(_holder, 1f, 0.55f, Ease.OutBack);
            Tween.Rotate(_holder, 720f, 0.65f, Ease.OutCubic);
            Tween.Delay(0.8f, Flip).SetLink(this);
        }

        void Flip()
        {
            if (_holder == null) return;
            Tween.Scale(_holder, new Vector3(0f, 1.06f, 1f), 0.14f, Ease.InQuad).OnComplete(() =>
            {
                if (_holder == null) return;
                if (_back != null) _back.gameObject.SetActive(false);
                if (_front != null) _front.gameObject.SetActive(true);
                Tween.Scale(_holder, Vector3.one, 0.26f, Ease.OutBack).SetOvershoot(2f);
                Reveal();
            });
        }

        void Reveal()
        {
            _revealed = true;
            AudioManager.Play(Sfx.CardFlip);
            AudioManager.Play(_duplicate ? Sfx.Coin : Sfx.Sparkle);
            Haptics.Play(HapticType.Medium);
            Vector2 c = FX.LocalCenterOf(_holder);
            FX.Sparkles(null, c, 18);
            FX.Ring(null, c, DS.Colors.Accent, 620f);
            if (!_duplicate) FX.Burst(null, c, "ui_star_small", 22, DS.Colors.Gold, 900f, 0.8f, 44f, -1300f);

            if (_badge != null)
            {
                _badge.gameObject.SetActive(true);
                CommonUI.PopIn(_badge, 0.15f).OnComplete(() => { if (_badge != null) Tween.Pulse(_badge, 1.08f); });
                Tween.Delay(0.15f, () => AudioManager.Play(Sfx.Pop)).SetLink(this);
            }
            if (_duplicate)
            {
                // The coins were granted with the win (decoration only). After a level the top bar is hidden (game
                // screen): flying to its off-screen pill would look like coins vanishing, so only fly when it is shown.
                FX.FloatingText(null, c + new Vector2(0f, 150f), "+" + Loc.Number(Collection.DuplicateCoins), TextStyle.H1, DS.Colors.Accent);
                var bar = TopBar.Instance;
                var target = CommonUI.CoinsTarget;
                if (bar != null && bar.IsShown && target != null)
                    UIKit.FlyRewards("icon_coin", 5, CommonUI.WorldCenter(_holder), target);
            }

            if (_info != null) Tween.Fade(_info, 1f, 0.3f, Ease.OutQuad).SetDelay(0.2f);
            if (!_duplicate && _bar != null)
            {
                var bar = _bar;
                int owned = _owned, total = _albumSize;
                Tween.Delay(0.55f, () =>
                {
                    if (bar == null) return;
                    bar.SetValue(owned / (float)total, true);
                    bar.SetLabel(Loc.T("quest.progress", owned, total));
                    Tween.Punch(bar.transform, 0.12f, 0.35f);
                    AudioManager.Play(Sfx.Star);
                }).SetLink(this);
            }
            if (_collect != null)
            {
                var b = _collect;
                CommonUI.PopIn(b.transform, 0.75f).OnComplete(() => { if (b != null) Tween.Pulse(b.transform, 1.05f); });
            }
            if (_front != null) Tween.Bob(_front, 6f, 2.4f);
        }

        void OnCollect()
        {
            if (!_revealed || IsClosing) return;
            Close();
        }

        protected override void OnClosing()
        {
            if (_reported) return;
            _reported = true;
            CommonUI.SafeInvoke(_onClosed);
        }
    }
}
