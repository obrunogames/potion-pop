using System.Collections.Generic;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Shop tab (GDD §5/§8): Free (daily gift, coins for rewarded ads), Boosters (one pack card per booster) and
    /// Bundles (1 of each in-game booster, hearts refill). Purchases fly items/coins with Sfx.Purchase.
    /// </summary>
    public class ShopScreen : TabScreen
    {
        public override ScreenId Id => ScreenId.Shop;

        /// <summary>Booster bundle: one of each in-game booster. Priced under its contents at pack prices (Hammer 100 +
        /// Wand ~117 + Freeze ~67 + Shuffle ~67 ≈ 350 per unit, Economy.BoosterPack) so the "Best value!" sticker is true.</summary>
        public const int BundlePrice = 300;

        const float FreeCardH = 520f, BoosterCardH = 560f, BundleH = 300f, RefillH = 270f;

        sealed class BoosterCard
        {
            public BoosterType type;
            public RectTransform root, iconRt, ownedPill, lockRt, lockRow;
            public Image icon;
            public UIButton buy;
            public TMP_Text owned;
        }

        ScrollRect _scroll;
        RectTransform _list;
        float _w;
        readonly List<RectTransform> _reveal = new List<RectTransform>();
        readonly List<BoosterCard> _cards = new List<BoosterCard>();

        UIButton _giftBtn, _adBtn, _bundleBtn, _refillBtn;
        RectTransform _giftIcon, _adIcon, _bundleIcon, _refillIcon;
        TMP_Text _adSub, _refillSub;
        TweenHandle _giftPulse, _giftWobble, _adPulse;
        bool _adPending;
        float _tick;

        public override void Build()
        {
            BuildFrame("tabs.shop.title", DS.Colors.Orange);
            BuildList();
            Economy.OnBoosterChanged += OnBoosterChanged;
            Lives.OnChanged += RequestRefresh;
            ShopOffers.OnChanged += RequestRefresh;
            Loc.OnLanguageChanged += RequestRefresh;
            Progress.OnLevelChanged += OnLevelChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Economy.OnBoosterChanged -= OnBoosterChanged;
            Lives.OnChanged -= RequestRefresh;
            ShopOffers.OnChanged -= RequestRefresh;
            Loc.OnLanguageChanged -= RequestRefresh;
            Progress.OnLevelChanged -= OnLevelChanged;
        }

        void OnBoosterChanged(BoosterType t, int count) => RequestRefresh();
        void OnLevelChanged(int level) => RequestRefresh();

        protected override void OnLayoutChanged()
        {
            if (Mathf.Abs(ContentWidth - _w) < 1f) return;
            BuildList();
            Refresh(false);
        }

        // ---------------------------------------------------------------------------------------- build

        void BuildList()
        {
            if (_scroll != null) Destroy(_scroll.gameObject);
            _reveal.Clear();
            _cards.Clear();
            KillLoops();

            _w = ContentWidth;
            _scroll = UIKit.ScrollView(Body, new Vector2(_w, BodyHeight), out _list, DS.Space.M, DS.Space.S);
            UIKit.Stretch((RectTransform)_scroll.transform);

            _reveal.Add(CommonUI.SectionHeader(_list, "tabs.shop.section_free", "icon_gift", _w, DS.Colors.Pink));
            BuildFreeRow();
            _reveal.Add(CommonUI.SectionHeader(_list, "tabs.shop.section_boosters", "booster_undo", _w, DS.Colors.Secondary));
            BuildBoosterGrid();
            _reveal.Add(CommonUI.SectionHeader(_list, "tabs.shop.section_bundles", "booster_pack", _w, DS.Colors.Brand));
            BuildBundle();
            BuildRefill();
            UIKit.Spacer(_list, new Vector2(_w, DS.Space.XL));
        }

        void KillLoops()
        {
            _giftPulse?.Kill();
            _giftWobble?.Kill();
            _adPulse?.Kill();
            _giftPulse = _giftWobble = _adPulse = null;
        }

        RectTransform CardShell(Transform parent, string name, Vector2 size)
        {
            var root = UIKit.Rect(name, parent);
            root.sizeDelta = size;
            var bg = CommonUI.Card(root, size);
            UIKit.Stretch(bg.rectTransform);
            return root;
        }

        RectTransform TitleCapsule(RectTransform card, float width, string key, Color color)
        {
            var cap = UIKit.Capsule(card, new Vector2(width, 74f), color);
            UIKit.Place(cap.rectTransform, new Vector2(0.5f, 1f), new Vector2(width, 74f), new Vector2(0f, -22f));
            var shine = UIKit.Capsule(cap.rectTransform, new Vector2(width - 30f, 24f), DS.WithAlpha(Color.white, 0.25f));
            UIKit.Place(shine.rectTransform, new Vector2(0.5f, 1f), new Vector2(width - 30f, 24f), new Vector2(0f, -7f));
            var t = UIKit.LocText(cap.rectTransform, key, TextStyle.H3, new Vector2(width - 30f, 66f));
            DS.Apply(t, TextStyle.H3, 42f);
            UIKit.Stretch(t.rectTransform, 18f, 0f, 18f, 4f);
            return cap.rectTransform;
        }

        static RectTransform GlowIcon(RectTransform card, string sprite, float size, Vector2 anchor, Vector2 pos, Color glowColor)
        {
            var glow = UIKit.NewImage(card, "Glow", UISprites.Glow, DS.WithAlpha(glowColor, 0.65f));
            UIKit.Place(glow.rectTransform, anchor, new Vector2(size * 1.45f, size * 1.45f), pos);
            glow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Tween.Pulse(glow.transform, 1.12f, 2.2f);
            var icon = UIKit.Image(card, sprite, new Vector2(size, size));
            UIKit.Place(icon.rectTransform, anchor, new Vector2(size, size), pos);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return icon.rectTransform;
        }

        static RectTransform AmountRow(RectTransform card, string icon, string text, float y)
        {
            var row = UIKit.Rect("Amount", card);
            UIKit.Place(row, new Vector2(0.5f, 0f), new Vector2(260f, 60f), new Vector2(0f, y));
            var ic = UIKit.Image(row, icon, new Vector2(60f, 60f));
            UIKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f, 60f), new Vector2(30f, 0f));
            var t = CommonUI.Label(row, text, TextStyle.H2, new Vector2(170f, 60f), TextAlignmentOptions.Left, 54f, DS.Colors.Accent);
            UIKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(170f, 60f), new Vector2(100f, 2f));
            return row;
        }

        void BuildFreeRow()
        {
            var row = UIKit.Rect("FreeRow", _list);
            row.sizeDelta = new Vector2(_w, FreeCardH);
            float cw = (_w - DS.Space.M) * 0.5f;

            // Daily gift.
            var gift = CardShell(row, "DailyGift", new Vector2(cw, FreeCardH));
            UIKit.Place(gift, new Vector2(0f, 0.5f), new Vector2(cw, FreeCardH), Vector2.zero);
            TitleCapsule(gift, cw - 50f, "tabs.shop.daily_gift", DS.Colors.Pink);
            _giftIcon = GlowIcon(gift, "icon_gift", 175f, new Vector2(0.5f, 0f), new Vector2(0f, 330f), DS.Colors.Accent);
            AmountRow(gift, "icon_coin", "+" + Loc.Number(ShopOffers.FreeGiftCoins), 186f);
            var every = CommonUI.LocLabel(gift, "tabs.shop.every_day", TextStyle.Small, new Vector2(cw - 40f, 40f),
                TextAlignmentOptions.Center, 30f);
            UIKit.Place(every.rectTransform, new Vector2(0.5f, 0f), new Vector2(cw - 40f, 40f), new Vector2(0f, 144f));
            _giftBtn = UIKit.ButtonLoc(gift, "ui.free", ButtonColor.Green, new Vector2(cw - 44f, 116f), ClaimGift);
            UIKit.Place((RectTransform)_giftBtn.transform, new Vector2(0.5f, 0f), new Vector2(cw - 44f, 116f), new Vector2(0f, 20f));
            _reveal.Add(gift);

            // Coins for a rewarded ad.
            var ad = CardShell(row, "AdCoins", new Vector2(cw, FreeCardH));
            UIKit.Place(ad, new Vector2(1f, 0.5f), new Vector2(cw, FreeCardH), Vector2.zero);
            TitleCapsule(ad, cw - 50f, "ads.placement.shop_coins", DS.Colors.Brand);
            _adIcon = GlowIcon(ad, "coins_medium", 175f, new Vector2(0.5f, 0f), new Vector2(0f, 330f), DS.Colors.Accent);
            AmountRow(ad, "icon_coin", "+" + Loc.Number(ShopOffers.AdCoins), 186f);
            _adSub = CommonUI.Label(ad, "", TextStyle.Small, new Vector2(cw - 40f, 40f), TextAlignmentOptions.Center, 30f);
            UIKit.Place(_adSub.rectTransform, new Vector2(0.5f, 0f), new Vector2(cw - 40f, 40f), new Vector2(0f, 144f));
            _adBtn = UIKit.ButtonLoc(ad, "ui.watch_ad", ButtonColor.Purple, new Vector2(cw - 44f, 116f), WatchAd);
            _adBtn.SetAdBadge(true);
            UIKit.Place((RectTransform)_adBtn.transform, new Vector2(0.5f, 0f), new Vector2(cw - 44f, 116f), new Vector2(0f, 20f));
            _reveal.Add(ad);
        }

        void BuildBoosterGrid()
        {
            var types = Economy.AllBoosters;
            int rows = (types.Length + 1) / 2;
            float cw = (_w - DS.Space.M) * 0.5f;
            var grid = UIKit.Grid(_list, new Vector2(_w, rows * BoosterCardH + (rows - 1) * DS.Space.M),
                new Vector2(cw, BoosterCardH), new Vector2(DS.Space.M, DS.Space.M), 2);
            for (int i = 0; i < types.Length; i++)
            {
                var card = BuildBoosterCard(grid.transform, types[i], cw);
                _cards.Add(card);
                _reveal.Add(card.root);
            }
        }

        BoosterCard BuildBoosterCard(Transform parent, BoosterType type, float cw)
        {
            var c = new BoosterCard { type = type };
            c.root = CardShell(parent, "Booster_" + Economy.BoosterId(type), new Vector2(cw, BoosterCardH));
            var holder = c.root;
            c.iconRt = GlowIcon(holder, Economy.BoosterSprite(type), 170f, new Vector2(0.5f, 1f), new Vector2(0f, -118f), DS.Colors.BrandLight);
            c.icon = c.iconRt.GetComponent<Image>();

            c.lockRt = UIKit.Image(holder, "icon_lock", new Vector2(86f, 86f)).rectTransform;
            UIKit.Place(c.lockRt, new Vector2(0.5f, 1f), new Vector2(86f, 86f), new Vector2(54f, -118f));

            c.ownedPill = UIKit.Rect("Owned", holder);
            UIKit.Place(c.ownedPill, new Vector2(1f, 1f), new Vector2(118f, 64f), new Vector2(-20f, -20f));
            var pill = UIKit.Capsule(c.ownedPill, new Vector2(118f, 64f), DS.Colors.Brand);
            UIKit.Stretch(pill.rectTransform);
            c.owned = CommonUI.Label(c.ownedPill, "", TextStyle.Badge, new Vector2(110f, 60f), TextAlignmentOptions.Center, 36f);
            UIKit.Stretch(c.owned.rectTransform, 6f, 0f, 6f, 2f);

            var name = CommonUI.LocLabel(holder, Economy.BoosterNameKey(type), TextStyle.Body, new Vector2(cw - 40f, 60f),
                TextAlignmentOptions.Center, 48f);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(cw - 40f, 60f), new Vector2(0f, -212f));
            var desc = CommonUI.LocLabel(holder, Economy.BoosterDescKey(type), TextStyle.Small, new Vector2(cw - 44f, 128f),
                TextAlignmentOptions.Top, 31f);
            UIKit.Place(desc.rectTransform, new Vector2(0.5f, 1f), new Vector2(cw - 44f, 128f), new Vector2(0f, -276f));

            var pack = Economy.BoosterPack(type);
            c.buy = UIKit.Button(holder, Loc.T("reward.amount.count", pack.count), ButtonColor.Yellow, new Vector2(cw - 40f, 116f), null);
            c.buy.SetPrice(pack.price);
            var card = c;
            c.buy.OnClick += () => BuyBooster(card);
            UIKit.Place((RectTransform)c.buy.transform, new Vector2(0.5f, 0f), new Vector2(cw - 40f, 116f), new Vector2(0f, 20f));

            c.lockRow = UIKit.Rect("Locked", holder);
            UIKit.Place(c.lockRow, new Vector2(0.5f, 0f), new Vector2(cw - 40f, 104f), new Vector2(0f, 26f));
            var lockBg = UIKit.Capsule(c.lockRow, new Vector2(cw - 40f, 104f), DS.WithAlpha(DS.Colors.CreamDark, 0.9f));
            UIKit.Stretch(lockBg.rectTransform);
            var lockText = CommonUI.LocLabel(c.lockRow, "booster.locked", TextStyle.Body, new Vector2(cw - 80f, 96f),
                TextAlignmentOptions.Center, 34f, DS.Colors.InkSoft, Economy.BoosterUnlockLevel(type));
            UIKit.Stretch(lockText.rectTransform, 24f, 0f, 24f, 0f);
            return c;
        }

        void BuildBundle()
        {
            var card = CardShell(_list, "Bundle", new Vector2(_w, BundleH));
            _reveal.Add(card);
            _bundleIcon = GlowIcon(card, "booster_pack", 220f, new Vector2(0f, 0.5f), new Vector2(150f, 6f), DS.Colors.Accent);
            Tween.Bob(_bundleIcon, 6f, 2.4f);

            float textX = 290f, btnW = 290f;
            float textW = _w - textX - btnW - 50f;
            var name = CommonUI.LocLabel(card, "tabs.shop.bundle_name", TextStyle.Body, new Vector2(textW, 64f), TextAlignmentOptions.Left, 50f);
            UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 64f), new Vector2(textX, -34f));
            var desc = CommonUI.LocLabel(card, "tabs.shop.bundle_desc", TextStyle.Small, new Vector2(textW, 50f), TextAlignmentOptions.Left, 32f);
            UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 50f), new Vector2(textX, -100f));
            var boosters = Economy.InGameBoosters;
            float mini = Mathf.Min(84f, (textW - 12f) / Mathf.Max(1, boosters.Length));
            for (int i = 0; i < boosters.Length; i++)
            {
                var ic = UIKit.Image(card, Economy.BoosterSprite(boosters[i]), new Vector2(mini, mini));
                UIKit.Place(ic.rectTransform, new Vector2(0f, 0f), new Vector2(mini, mini), new Vector2(textX + i * (mini + 4f), 44f));
                Tween.Bob(ic.rectTransform, 4f, 1.8f + i * 0.25f);
            }

            _bundleBtn = UIKit.Button(card, "", ButtonColor.Green, new Vector2(btnW, 130f), BuyBundle);
            _bundleBtn.SetPrice(BundlePrice);
            UIKit.Place((RectTransform)_bundleBtn.transform, new Vector2(1f, 0.5f), new Vector2(btnW, 130f), new Vector2(-28f, -20f));

            // "Best value!" sticker on the corner.
            var tag = UIKit.Rect("BestValue", card);
            UIKit.Place(tag, new Vector2(1f, 1f), new Vector2(290f, 70f), new Vector2(-28f, 22f));
            tag.pivot = new Vector2(0.5f, 0.5f);
            tag.anchoredPosition = new Vector2(-28f - 145f, -8f);
            var tagBg = UIKit.Capsule(tag, new Vector2(290f, 70f), DS.Colors.Pink);
            UIKit.Stretch(tagBg.rectTransform);
            var tagText = UIKit.LocText(tag, "tabs.shop.best_value", TextStyle.H3, new Vector2(270f, 64f));
            DS.Apply(tagText, TextStyle.H3, 38f);
            UIKit.Stretch(tagText.rectTransform, 12f, 0f, 12f, 3f);
            tag.localRotation = Quaternion.Euler(0f, 0f, 5f);
            Tween.Pulse(tag, 1.07f, 1.1f);
        }

        void BuildRefill()
        {
            var card = CardShell(_list, "Refill", new Vector2(_w, RefillH));
            _reveal.Add(card);
            _refillIcon = GlowIcon(card, "hearts_refill", 200f, new Vector2(0f, 0.5f), new Vector2(150f, 4f), DS.Colors.Pink);

            float textX = 290f, btnW = 290f;
            float textW = _w - textX - btnW - 50f;
            var name = CommonUI.LocLabel(card, "tabs.shop.refill_name", TextStyle.Body, new Vector2(textW, 64f), TextAlignmentOptions.Left, 50f);
            UIKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 64f), new Vector2(textX, -46f));
            var desc = CommonUI.LocLabel(card, "tabs.shop.refill_desc", TextStyle.Small, new Vector2(textW, 50f), TextAlignmentOptions.Left, 32f,
                null, Lives.Max);
            UIKit.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 50f), new Vector2(textX, -112f));
            var heart = UIKit.Image(card, "icon_heart", new Vector2(56f, 56f));
            UIKit.Place(heart.rectTransform, new Vector2(0f, 0f), new Vector2(56f, 56f), new Vector2(textX, 40f));
            Tween.Pulse(heart.transform, 1.15f, 0.8f);
            _refillSub = CommonUI.Label(card, "", TextStyle.Body, new Vector2(textW - 70f, 56f), TextAlignmentOptions.Left, 40f, DS.Colors.Danger);
            UIKit.Place(_refillSub.rectTransform, new Vector2(0f, 0f), new Vector2(textW - 70f, 56f), new Vector2(textX + 68f, 40f));

            _refillBtn = UIKit.Button(card, "", ButtonColor.Green, new Vector2(btnW, 130f), BuyRefill);
            UIKit.Place((RectTransform)_refillBtn.transform, new Vector2(1f, 0.5f), new Vector2(btnW, 130f), new Vector2(-28f, 0f));
        }

        // ---------------------------------------------------------------------------------------- refresh

        protected override void Refresh(bool animate)
        {
            if (_list == null) return;
            RefreshGift();
            RefreshAd();
            for (int i = 0; i < _cards.Count; i++) RefreshCard(_cards[i]);
            RefreshBundle();
            RefreshRefill();
            if (!animate) return;

            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _reveal.Count; i++) CommonUI.PopIn(_reveal[i], 0.06f + i * DS.Motion.Stagger);
        }

        void RefreshGift()
        {
            if (_giftBtn == null) return;
            bool available = ShopOffers.FreeGiftAvailable;
            _giftBtn.Interactable = available;
            if (available)
            {
                _giftBtn.SetLabelKey("ui.free");
                if (_giftPulse == null || !_giftPulse.IsActive)
                {
                    _giftBtn.transform.localScale = Vector3.one;
                    _giftPulse = Tween.Pulse(_giftBtn.transform, 1.05f);
                }
                if (_giftIcon != null && (_giftWobble == null || !_giftWobble.IsActive))
                {
                    _giftIcon.localRotation = Quaternion.Euler(0f, 0f, -7f);
                    _giftWobble = Tween.Rotate(_giftIcon, 7f, 0.55f, Ease.InOutSine).SetLoops(-1, true);
                }
            }
            else
            {
                _giftBtn.SetLabel(Loc.T("ui.free_in", TimeUtil.FormatDuration(TimeUtil.SecondsToMidnight)));
                if (_giftPulse != null && _giftPulse.IsActive)
                {
                    _giftPulse.Kill();
                    _giftBtn.transform.localScale = Vector3.one;
                }
                if (_giftWobble != null && _giftWobble.IsActive)
                {
                    _giftWobble.Kill();
                    if (_giftIcon != null) _giftIcon.localRotation = Quaternion.identity;
                }
            }
        }

        void RefreshAd()
        {
            if (_adBtn == null) return;
            int left = ShopOffers.AdCoinsLeft;
            bool can = left > 0;
            _adBtn.Interactable = can && !_adPending;
            _adBtn.SetAdBadge(can);
            if (can)
            {
                _adBtn.SetLabelKey("ui.watch_ad");
                _adSub.text = Loc.T("tabs.shop.ads_left", left, ShopOffers.AdCoinsPerDay);
                if (_adPulse == null || !_adPulse.IsActive) _adPulse = Tween.Pulse(_adIcon, 1.06f, 1.4f);
            }
            else
            {
                _adBtn.SetLabel(Loc.T("ui.free_in", TimeUtil.FormatDuration(TimeUtil.SecondsToMidnight)));
                _adSub.text = Loc.T("tabs.shop.ads_done");
                if (_adPulse != null && _adPulse.IsActive)
                {
                    _adPulse.Kill();
                    if (_adIcon != null) _adIcon.localScale = Vector3.one;
                }
            }
        }

        void RefreshCard(BoosterCard c)
        {
            bool unlocked = Economy.IsBoosterUnlocked(c.type);
            c.owned.text = Loc.T("reward.amount.count", Economy.GetBooster(c.type));
            c.ownedPill.gameObject.SetActive(unlocked);
            c.lockRt.gameObject.SetActive(!unlocked);
            c.lockRow.gameObject.SetActive(!unlocked);
            c.buy.gameObject.SetActive(unlocked);
            if (c.icon != null) c.icon.color = unlocked ? Color.white : new Color(0.45f, 0.4f, 0.55f, 0.55f);
        }

        /// <summary>Highest unlock level among the bundle's boosters that are still locked (0 = all unlocked).</summary>
        static int BundleLockLevel()
        {
            int lockLvl = 0;
            foreach (var b in Economy.InGameBoosters)
                if (!Economy.IsBoosterUnlocked(b)) lockLvl = Mathf.Max(lockLvl, Economy.BoosterUnlockLevel(b));
            return lockLvl;
        }

        void RefreshBundle()
        {
            // Gray until every booster in it can be used (taps still explain why, see BuyBundle).
            if (_bundleBtn != null) _bundleBtn.SetColor(BundleLockLevel() > 0 ? ButtonColor.Gray : ButtonColor.Green);
        }

        void RefreshRefill()
        {
            if (_refillBtn == null) return;
            bool infinite = Lives.HasInfinite;
            bool full = Lives.IsFull;
            if (infinite)
            {
                _refillSub.text = Loc.T("hearts.infinite");
                _refillBtn.SetPrice(0);
                _refillBtn.SetLabelKey("hearts.infinite");
                _refillBtn.Interactable = false;
            }
            else if (full)
            {
                _refillSub.text = Loc.T("quest.progress", Lives.Hearts, Lives.Max);
                _refillBtn.SetPrice(0);
                _refillBtn.SetLabelKey("hearts.full");
                _refillBtn.Interactable = false;
            }
            else
            {
                _refillSub.text = Loc.T("quest.progress", Lives.Hearts, Lives.Max);
                _refillBtn.SetLabel("");
                _refillBtn.SetPrice(Lives.RefillPrice);
                _refillBtn.Interactable = true;
            }
        }

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshGift();
            RefreshAd();
        }

        // ---------------------------------------------------------------------------------------- actions

        void ClaimGift()
        {
            if (!ShopOffers.FreeGiftAvailable)
            {
                RefreshGift();
                return;
            }
            var reward = ShopOffers.ClaimFreeGift();
            if (reward.IsEmpty) return;
            CelebrateCoins(_giftIcon, reward);
            RefreshGift();
        }

        void WatchAd()
        {
            if (_adPending || ShopOffers.AdCoinsLeft <= 0) return;
            _adPending = true;
            RefreshAd();
            AdsService.ShowRewarded(AdPlacement.ShopCoins, earned =>
            {
                _adPending = false;
                if (this == null) return;
                if (earned)
                {
                    var reward = ShopOffers.GrantAdCoins();
                    if (!reward.IsEmpty) CelebrateCoins(_adIcon, reward);
                }
                RefreshAd();
            });
        }

        /// <summary>Already-granted coins/hearts: burst at the source, then the icons fly to the top-bar pill and the
        /// counter counts up as they land (held until then, GDD §9 "coins fly to the counter with count-up").</summary>
        static void FlyToTopBar(RectTransform from, Reward reward)
        {
            var bar = TopBar.Instance;
            if (from == null || bar == null || reward.IsEmpty) return;
            bar.Hold(reward);
            TopBar.Fly(new[] { reward }, CommonUI.WorldCenter(from));
        }

        void CelebrateCoins(RectTransform from, Reward reward)
        {
            AudioManager.Play(Sfx.Purchase);
            Haptics.Play(HapticType.Success);
            if (from == null) return;
            Vector2 c = FX.LocalCenterOf(from);
            FX.Burst(null, c, "ui_star_small", 16, DS.Colors.Gold, 700f, 0.7f, 40f);
            FX.Ring(null, c, DS.Colors.Accent, 360f);
            FX.FloatingText(null, c + new Vector2(0f, 90f), "+" + Loc.Number(reward.amount), TextStyle.H1, DS.Colors.Accent);
            FlyToTopBar(from, reward);
            Tween.Punch(from, 0.25f, 0.35f);
        }

        void BuyBooster(BoosterCard c)
        {
            if (!Economy.IsBoosterUnlocked(c.type))
            {
                CommonUI.Deny(c.root);
                return;
            }
            var pack = Economy.BoosterPack(c.type);
            if (!Economy.CanAfford(pack.price))
            {
                CommonUI.Deny(c.buy.transform);
                UIKit.Toast(Loc.T("ui.not_enough_coins"));
                return;
            }
            if (!Economy.TryBuyBoosterPack(c.type)) return;
            AudioManager.Play(Sfx.Purchase);
            Haptics.Play(HapticType.Success);
            Vector2 at = FX.LocalCenterOf(c.iconRt);
            FX.FloatingText(null, at + new Vector2(0f, 110f), Loc.T("reward.amount.count", pack.count), TextStyle.H1, DS.Colors.Accent);
            var card = c;
            UIKit.FlyRewards(Economy.BoosterSprite(c.type), pack.count, CommonUI.WorldCenter((RectTransform)c.buy.transform), c.iconRt, null, () =>
            {
                if (card.iconRt == null) return;
                FX.Sparkles(null, FX.LocalCenterOf(card.iconRt), 12);
                if (card.ownedPill != null) Tween.Punch(card.ownedPill, 0.3f, 0.35f);
            });
        }

        void BuyBundle()
        {
            // Like BuyBooster: never sell boosters the player can't use yet (Freeze / Shuffle unlock at levels 7 / 9).
            int lockLvl = BundleLockLevel();
            if (lockLvl > 0)
            {
                CommonUI.Deny(_bundleBtn.transform);
                UIKit.Toast(Loc.T("booster.locked", lockLvl));
                return;
            }
            if (!Economy.CanAfford(BundlePrice))
            {
                CommonUI.Deny(_bundleBtn.transform);
                UIKit.Toast(Loc.T("ui.not_enough_coins"));
                return;
            }
            if (!Economy.TrySpendCoins(BundlePrice, "booster_bundle")) return;
            var boosters = Economy.InGameBoosters;
            for (int i = 0; i < boosters.Length; i++) Economy.AddBooster(boosters[i], 1);
            AudioManager.Play(Sfx.Purchase);
            Haptics.Play(HapticType.Success);
            Vector3 from = CommonUI.WorldCenter((RectTransform)_bundleBtn.transform);
            for (int i = 0; i < boosters.Length; i++)
                UIKit.FlyRewards(Economy.BoosterSprite(boosters[i]), 1, from, _bundleIcon);
            if (_bundleIcon != null)
            {
                Vector2 c = FX.LocalCenterOf(_bundleIcon);
                FX.Sparkles(null, c, 16);
                FX.Ring(null, c, DS.Colors.Accent, 420f);
            }
        }

        void BuyRefill()
        {
            if (Lives.IsFull || Lives.HasInfinite)
            {
                CommonUI.Deny(_refillBtn.transform);
                return;
            }
            if (!Economy.CanAfford(Lives.RefillPrice))
            {
                CommonUI.Deny(_refillBtn.transform);
                UIKit.Toast(Loc.T("ui.not_enough_coins"));
                return;
            }
            int before = Lives.Hearts;
            if (!Lives.TryBuyRefill()) return;
            AudioManager.Play(Sfx.Purchase);
            AudioManager.Play(Sfx.Heart);
            Haptics.Play(HapticType.Success);
            if (_refillIcon != null)
            {
                FX.Burst(null, FX.LocalCenterOf(_refillIcon), "icon_heart", 10, null, 600f, 0.7f, 46f);
                FlyToTopBar(_refillIcon, Reward.Hearts(Mathf.Max(1, Lives.Hearts - before)));
            }
        }
    }
}
