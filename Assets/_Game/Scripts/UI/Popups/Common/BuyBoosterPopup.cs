using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Buy a booster pack for coins (icon, description, count, price; not enough coins → offer Shop).
    /// onDone(true) when bought.</summary>
    public class BuyBoosterPopup : Popup
    {
        BoosterType _type;
        Action<bool> _onDone;
        bool _bought, _reported;
        RectTransform _icon;
        TMP_Text _owned;
        RectTransform _ownedPill;
        UIButton _buy, _getCoins;

        protected override string TitleKey => Economy.BoosterNameKey(_type);
        protected override Vector2 PanelSize => new Vector2(880f, 1240f);

        public static void Open(BoosterType type, Action<bool> onDone = null)
        {
            PopupManager.Show<BuyBoosterPopup>(p =>
            {
                p._type = type;
                p._onDone = onDone;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            var pack = Economy.BoosterPack(_type);

            // Hero: spinning sunburst + bobbing booster.
            const float stageH = 400f;
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(size.x, stageH), Vector2.zero);
            var burst = UIKit.Image(stage, "sunburst", new Vector2(520f, 520f));
            burst.color = DS.WithAlpha(Color.white, 0.85f);
            UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, 520f), Vector2.zero);
            Tween.Rotate(burst.transform, -360f, 14f, Ease.Linear).SetLoops(-1, false);
            var disc = UIKit.NewImage(stage, "Disc", UISprites.Circle, DS.WithAlpha(DS.Colors.BrandLight, 0.35f));
            UIKit.Place(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300f, 300f), Vector2.zero);
            var icon = UIKit.Image(stage, Economy.BoosterSprite(_type), new Vector2(270f, 270f));
            _icon = icon.rectTransform;
            UIKit.Place(_icon, new Vector2(0.5f, 0.5f), new Vector2(270f, 270f), new Vector2(0f, 6f));
            CommonUI.PopIn(_icon, 0.1f).OnComplete(() => { if (_icon != null) Tween.Bob(_icon, 8f, 2.2f); });

            // Owned pill overlapping the bottom of the hero.
            _ownedPill = UIKit.Rect("Owned", stage);
            UIKit.Place(_ownedPill, new Vector2(0.5f, 0f), new Vector2(300f, 72f), new Vector2(0f, 0f));
            var pill = UIKit.Capsule(_ownedPill, new Vector2(300f, 72f), DS.Colors.Brand);
            UIKit.Stretch(pill.rectTransform);
            _owned = CommonUI.Label(_ownedPill, "", TextStyle.H3, new Vector2(280f, 64f), TextAlignmentOptions.Center, 40f);
            UIKit.Stretch(_owned.rectTransform, 12f, 0f, 12f, 2f);

            float y = stageH + DS.Space.M;
            var desc = CommonUI.LocLabel(content, Economy.BoosterDescKey(_type), TextStyle.Body, new Vector2(size.x - 20f, 130f),
                TextAlignmentOptions.Center, 44f);
            UIKit.Place(desc.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x - 20f, 130f), new Vector2(0f, -y));
            y += 130f + DS.Space.S;

            // "x3" pack preview: three small boosters fanned out + the count.
            var fan = UIKit.Rect("Pack", content);
            UIKit.Place(fan, new Vector2(0.5f, 1f), new Vector2(size.x, 150f), new Vector2(0f, -y));
            var inset = UIKit.Panel(fan, "panel_inset", new Vector2(520f, 150f));
            UIKit.Place(inset.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, 150f), Vector2.zero);
            for (int i = 0; i < 3; i++)
            {
                var mini = UIKit.Image(fan, Economy.BoosterSprite(_type), new Vector2(104f, 104f));
                UIKit.Place(mini.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(104f, 104f), new Vector2(-150f + i * 62f, 4f));
                mini.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 14f - i * 14f);
                CommonUI.PopIn(mini.transform, 0.25f + i * 0.07f);
            }
            var count = CommonUI.Label(fan, Loc.T("reward.amount.count", pack.count), TextStyle.Display, new Vector2(220f, 130f),
                TextAlignmentOptions.Center, 96f, DS.Colors.Accent);
            UIKit.Place(count.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(220f, 130f), new Vector2(130f, 0f));
            CommonUI.PopIn(count.transform, 0.45f);

            bool unlocked = Economy.IsBoosterUnlocked(_type);
            if (!unlocked)
            {
                var lockRow = CommonUI.LocLabel(content, "booster.locked", TextStyle.Body, new Vector2(size.x, 70f),
                    TextAlignmentOptions.Center, 40f, DS.Colors.InkSoft, Economy.BoosterUnlockLevel(_type));
                UIKit.Place(lockRow.rectTransform, new Vector2(0.5f, 0f), new Vector2(size.x, 70f), new Vector2(0f, 300f));
            }

            // Buy (price) and, when short of coins outside a level, "Get coins".
            _buy = UIKit.ButtonLoc(content, "ui.buy", ButtonColor.Green, ButtonSize.Large, OnBuy);
            _buy.SetPrice(pack.price);
            var brt = (RectTransform)_buy.transform;
            UIKit.Place(brt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 130f));
            CommonUI.PopIn(brt, 0.3f);

            _getCoins = UIKit.ButtonLoc(content, "tabs.buy.get_coins", ButtonColor.Yellow, new Vector2(460f, 110f), OnGetCoins, "icon_coin");
            UIKit.Place((RectTransform)_getCoins.transform, new Vector2(0.5f, 0f), new Vector2(460f, 110f), Vector2.zero);

            Economy.OnCoinsChanged += OnCoins;
            Economy.OnBoosterChanged += OnBooster;
            Refresh();
        }

        void OnDestroy()
        {
            Economy.OnCoinsChanged -= OnCoins;
            Economy.OnBoosterChanged -= OnBooster;
        }

        void OnCoins(int oldValue, int newValue) => Refresh();
        void OnBooster(BoosterType t, int count) { if (t == _type) Refresh(); }

        void Refresh()
        {
            if (_owned != null) _owned.text = Loc.T("tabs.shop.owned", Economy.GetBooster(_type));
            var pack = Economy.BoosterPack(_type);
            bool canAfford = Economy.CanAfford(pack.price);
            if (_getCoins != null)
                _getCoins.gameObject.SetActive(!_bought && !canAfford && !InLevel);
        }

        /// <summary>Opened from the board: never navigate away from a running level.</summary>
        static bool InLevel => ScreenManager.Instance != null && ScreenManager.Instance.Current == ScreenId.Game;

        void OnBuy()
        {
            if (_bought || IsClosing) return;
            var pack = Economy.BoosterPack(_type);
            if (!Economy.CanAfford(pack.price))
            {
                CommonUI.Deny(_buy.transform);
                UIKit.Toast(Loc.T("ui.not_enough_coins"));
                if (_getCoins != null && _getCoins.gameObject.activeSelf) Tween.Punch(_getCoins.transform, 0.18f, 0.35f);
                return;
            }
            if (!Economy.TryBuyBoosterPack(_type)) return;
            _bought = true;
            _buy.Interactable = false;
            AudioManager.Play(Sfx.Purchase);
            Haptics.Play(HapticType.Success);
            var from = CommonUI.WorldCenter((RectTransform)_buy.transform);
            UIKit.FlyRewards(Economy.BoosterSprite(_type), pack.count, from, _ownedPill, null, () =>
            {
                if (this == null) return;
                if (_icon != null)
                {
                    FX.Sparkles(null, FX.LocalCenterOf(_icon), 14);
                    FX.Ring(null, FX.LocalCenterOf(_icon), DS.Colors.Accent, 420f);
                }
                Tween.Delay(0.45f, () => { if (this != null && !IsClosing) Close(); }).SetLink(this);
            });
            Refresh();
        }

        void OnGetCoins()
        {
            _reported = true;
            CommonUI.SafeInvoke(_onDone, false);
            PopupManager.CloseAll();
            ScreenManager.Instance?.Show(ScreenId.Shop);
        }

        protected override void OnClosing()
        {
            if (_reported) return;
            _reported = true;
            CommonUI.SafeInvoke(_onDone, _bought);
        }
    }
}
