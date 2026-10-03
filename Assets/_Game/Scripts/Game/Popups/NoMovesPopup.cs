// ============================================================================================================
// "No more moves!" (GDD §2 Stuck): three booster cards — Undo, Shuffle, Extra Bottle — each "Use" when owned, "+ price"
// to buy a pack (BuyBoosterPopup) and use it right away, padlock + level while locked, grayed when it can't help now
// (nothing to undo, extra-bottle cap reached); continue with +1 empty bottle for Progress.ContinuePrice coins or once
// per level for a rewarded ad (both count toward Economy.MaxExtraBottles); a free stone break by ad when that would
// unstick the board; or give up (→ Level Failed). No close button and Back does nothing: the player picks a way out.
// The popup pays for continues itself; the session applies the chosen option after the popup closed.
// ============================================================================================================
using System;
using PotionPop.Services;
using PotionPop.UI;
using TMPro;
using UnityEngine;

namespace PotionPop.Game
{
    public sealed class NoMovesPopup : Popup
    {
        /// <summary>What the session can offer right now.</summary>
        public sealed class Options
        {
            /// <summary>The undo history is not empty.</summary>
            public bool canUndo;
            /// <summary>Below Economy.MaxExtraBottles (Extra Bottle booster and continues).</summary>
            public bool canAddBottle;
            /// <summary>The free (rewarded ad) continue is still available in this level.</summary>
            public bool adContinue;
            /// <summary>A stone whose ad break would unstick the board (-1 = none / no ad ready).</summary>
            public int stoneBottle = -1;
        }

        static readonly BoosterType[] Cards = { BoosterType.Undo, BoosterType.Shuffle, BoosterType.Bottle };
        const float CardH = 470f, ContinueH = 150f, StoneH = 120f, GiveUpH = 124f;

        protected override string TitleKey => "game.no_moves.title";
        protected override bool ShowClose => false;
        protected override Vector2 PanelSize => new Vector2(920f, ContentHeight() + 150f);

        Options _o = new Options();
        Action<BoosterType> _onBooster;
        Action<bool> _onContinue;
        Action<int> _onStone;
        Action _onGiveUp;
        bool _done, _busy;
        CounterPill _coins;
        UIButton _coinsButton, _adButton, _stoneButton, _giveUpButton;
        bool _subscribed;

        /// <summary>onBooster(type) after the popup closed with a booster the player owns (the caller applies and
        /// consumes it); onContinue(viaAd) after a continue was paid (coins spent / ad watched: the caller adds the
        /// bottle); onStone(bottle) to run the stone-break ad; onGiveUp to lose the level.</summary>
        public static NoMovesPopup Open(Options options, Action<BoosterType> onBooster, Action<bool> onContinue,
            Action<int> onStone, Action onGiveUp)
        {
            var existing = PopupManager.Get<NoMovesPopup>();
            if (existing != null) return existing;
            return PopupManager.Show<NoMovesPopup>(p =>
            {
                p._o = options ?? new Options();
                p._onBooster = onBooster;
                p._onContinue = onContinue;
                p._onStone = onStone;
                p._onGiveUp = onGiveUp;
            });
        }

        float ContentHeight()
        {
            float h = CounterPill.DefaultHeight + DS.Space.S + 96f + DS.Space.M + CardH + DS.Space.L;
            if (_o.canAddBottle) h += ContinueH + DS.Space.M;
            if (_o.stoneBottle >= 0) h += StoneH + DS.Space.M;
            return h + GiveUpH;
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            float y = 0f;

            // coin balance (the continue costs coins)
            _coins = UIKit.CounterPill(content, "icon_coin", 280f, null);
            if (_coins != null)
            {
                GameUI.PlaceTop((RectTransform)_coins.transform, y, new Vector2(280f, CounterPill.DefaultHeight), 30f);
                _coins.SetValue(Economy.Coins, false);
                Economy.OnCoinsChanged += OnCoinsChanged;
                _subscribed = true;
            }
            y += CounterPill.DefaultHeight + DS.Space.S;

            var msg = UIKit.LocText(content, "game.no_moves.message", TextStyle.Body, new Vector2(w - 190f, 96f));
            GameUI.PlaceTop(msg.rectTransform, y, new Vector2(w - 190f, 96f), -80f);
            var luna = UIKit.Image(content, "mascot_sad", new Vector2(130f, 181f));
            UIKit.Place(luna.rectTransform, new Vector2(1f, 1f), new Vector2(130f, 181f), new Vector2(10f, -(y - 70f)));
            GameUI.PopInDelayed(luna.transform, 0.1f);
            y += 96f + DS.Space.M;

            // booster cards
            float cardW = (w - DS.Space.M * 2f) / 3f;
            for (int i = 0; i < Cards.Length; i++)
            {
                float x = (i - 1) * (cardW + DS.Space.M);
                BuildCard(content, Cards[i], new Vector2(cardW, CardH), x, y, 0.15f + i * 0.08f);
            }
            y += CardH + DS.Space.L;

            // continue +1 bottle: coins, or once per level by ad
            if (_o.canAddBottle)
            {
                bool ad = _o.adContinue;
                float bw = ad ? (w - DS.Space.M) * 0.5f : Mathf.Min(w, 720f);
                // Compact label ("+1" next to the bottle icon) so the coin price always fits in the half-width button.
                _coinsButton = UIKit.Button(content, Loc.T("game.no_moves.plus_one"), ButtonColor.Green, new Vector2(bw, ContinueH), OnContinueCoins, "booster_bottle");
                _coinsButton.SetPrice(Progress.ContinuePrice);
                GameUI.PlaceTop((RectTransform)_coinsButton.transform, y, new Vector2(bw, ContinueH), ad ? -(bw + DS.Space.M) * 0.5f : 0f);
                GameUI.PopInDelayed(_coinsButton.transform, 0.35f);
                GameUI.PulseAfter(_coinsButton.transform, 0.9f, 1.04f, 1f);
                if (ad)
                {
                    _adButton = UIKit.ButtonLoc(content, "ui.free", ButtonColor.Purple, new Vector2(bw, ContinueH), OnContinueAd, "booster_bottle");
                    _adButton.SetAdBadge(true);
                    GameUI.PlaceTop((RectTransform)_adButton.transform, y, new Vector2(bw, ContinueH), (bw + DS.Space.M) * 0.5f);
                    GameUI.PopInDelayed(_adButton.transform, 0.42f);
                }
                y += ContinueH + DS.Space.M;
            }

            // free stone break (GDD §2: a rewarded ad breaks a stone)
            if (_o.stoneBottle >= 0)
            {
                _stoneButton = UIKit.ButtonLoc(content, "game.no_moves.break_stone", ButtonColor.Blue, new Vector2(Mathf.Min(w, 640f), StoneH), OnStone);
                _stoneButton.SetAdBadge(true);
                GameUI.PlaceTop((RectTransform)_stoneButton.transform, y, new Vector2(Mathf.Min(w, 640f), StoneH));
                GameUI.PopInDelayed(_stoneButton.transform, 0.48f);
                y += StoneH + DS.Space.M;
            }

            _giveUpButton = UIKit.ButtonLoc(content, "ui.give_up", ButtonColor.Red, new Vector2(440f, GiveUpH), OnGiveUp);
            GameUI.PlaceTop((RectTransform)_giveUpButton.transform, y, new Vector2(440f, GiveUpH));
            GameUI.PopInDelayed(_giveUpButton.transform, 0.55f);

            AudioManager.Play(Sfx.Invalid, 0.7f);
            Haptics.Play(HapticType.Warning);
        }

        void BuildCard(RectTransform content, BoosterType type, Vector2 size, float x, float y, float delay)
        {
            var card = UIKit.Panel(content, "panel_card", size).rectTransform;
            GameUI.PlaceTop(card, y, size, x);

            bool locked = !Economy.IsBoosterUnlocked(type);
            bool helps = type == BoosterType.Undo ? _o.canUndo : type != BoosterType.Bottle || _o.canAddBottle;
            int count = Economy.GetBooster(type);

            const float tileS = 170f;
            var tile = UIKit.Rect("Tile", card);
            GameUI.PlaceTop(tile, 22f, new Vector2(tileS, tileS));
            var glow = GameUI.Glow(tile, tileS * 1.5f, DS.WithAlpha(DS.Colors.Accent, locked || !helps ? 0f : 0.55f));
            GameUI.PlaceCenter(glow.rectTransform, Vector2.zero, new Vector2(tileS * 1.5f, tileS * 1.5f));
            var bg = UIKit.Panel(tile, "btn_square", new Vector2(tileS, tileS));
            UIKit.Stretch(bg.rectTransform);
            var icon = UIKit.Image(tile, Economy.BoosterSprite(type), new Vector2(tileS * 0.74f, tileS * 0.74f));
            GameUI.PlaceCenter(icon.rectTransform, new Vector2(0f, 6f), new Vector2(tileS * 0.74f, tileS * 0.74f));
            if (locked || !helps) icon.color = new Color(1f, 1f, 1f, 0.45f);
            else Tween.Bob(icon.rectTransform, 6f, 1.8f);
            if (count > 0 && !locked) GameUI.CountBadge(tile, count, 64f, new Vector2(1f, 1f), new Vector2(12f, 12f), out _);

            var name = UIKit.LocText(card, Economy.BoosterNameKey(type), TextStyle.Body, new Vector2(size.x - 24f, 56f));
            DS.Apply(name, TextStyle.Body, 36f);
            GameUI.PlaceTop(name.rectTransform, 22f + tileS + 8f, new Vector2(size.x - 24f, 56f));
            // Short descriptions: the cards are a third of the popup wide (the long booster.*.desc texts don't fit).
            string descKey = !locked && !helps ? (type == BoosterType.Undo ? "game.no_moves.undo_none" : "game.no_moves.bottle_max")
                : "game.no_moves.desc_" + Economy.BoosterId(type);
            var desc = UIKit.LocText(card, descKey, TextStyle.Small, new Vector2(size.x - 30f, 110f));
            DS.Apply(desc, TextStyle.Small, 28f);
            GameUI.PlaceTop(desc.rectTransform, 22f + tileS + 8f + 56f, new Vector2(size.x - 30f, 110f));

            UIButton button;
            Vector2 bs = new Vector2(size.x - 36f, 104f);
            if (locked)
            {
                button = UIKit.Button(card, Loc.T("booster.unlock_at", Economy.BoosterUnlockLevel(type)), ButtonColor.Gray, bs, null, "icon_lock");
                button.Interactable = false;
            }
            else if (!helps)
            {
                button = UIKit.ButtonLoc(card, "game.use", ButtonColor.Gray, bs, null);
                button.Interactable = false;
            }
            else if (count > 0)
            {
                button = UIKit.ButtonLoc(card, "game.use", ButtonColor.Green, bs, () => Use(type));
                GameUI.PulseAfter(button.transform, delay + 0.6f, 1.05f, 1f);
            }
            else
            {
                button = UIKit.Button(card, "", ButtonColor.Blue, bs, () => Buy(type), "icon_plus");
                button.SetPrice(Economy.BoosterPack(type).price);
            }
            UIKit.Place((RectTransform)button.transform, new Vector2(0.5f, 0f), bs, new Vector2(0f, 24f));
            GameUI.PopInDelayed(card, delay);
        }

        /// <summary>Back: nothing (the player must choose).</summary>
        public override void OnBack() { }
        protected override void OnOverlayTap() { }

        void OnCoinsChanged(int old, int now)
        {
            if (_coins != null) _coins.SetValue(now, true);
        }

        void Use(BoosterType type)
        {
            if (_done || _busy || IsClosing) return;
            if (Economy.GetBooster(type) <= 0)
            {
                Buy(type);
                return;
            }
            Finish(() => GameUI.Invoke(_onBooster, type));
        }

        void Buy(BoosterType type)
        {
            if (_done || _busy || IsClosing) return;
            AudioManager.Play(Sfx.Click);
            BuyBoosterPopup.Open(type, bought =>
            {
                if (!bought || this == null || _done || IsClosing) return;
                Use(type);
            });
        }

        void OnContinueCoins()
        {
            if (_done || _busy || IsClosing) return;
            if (!Progress.TryBuyContinue())
            {
                AudioManager.Play(Sfx.Error);
                Haptics.Play(HapticType.Warning);
                UIKit.Toast(Loc.T("ui.not_enough_coins"));
                if (_coinsButton != null && _coinsButton.Visual != null) Tween.Shake(_coinsButton.Visual, 16f, 0.35f);
                if (_coins != null) _coins.Bump();
                return;
            }
            AudioManager.Play(Sfx.Purchase);
            Finish(() => GameUI.Invoke(_onContinue, false));
        }

        void OnContinueAd()
        {
            if (_done || _busy || IsClosing) return;
            _busy = true;
            SetButtons(false);
            AdsService.ShowRewarded(AdPlacement.Continue, earned =>
            {
                if (this == null || IsClosing) return;
                _busy = false;
                if (earned)
                {
                    Finish(() => GameUI.Invoke(_onContinue, true));
                    return;
                }
                SetButtons(true);
            });
        }

        void OnStone()
        {
            if (_done || _busy || IsClosing || _o.stoneBottle < 0) return;
            int bottle = _o.stoneBottle;
            // Close first: the popup leaves the stack synchronously, so the session is no longer halted for the ad.
            Finish(() => GameUI.Invoke(_onStone, bottle));
        }

        void OnGiveUp()
        {
            if (_done || _busy || IsClosing) return;
            Finish(() => GameUI.Invoke(_onGiveUp));
        }

        void Finish(Action then)
        {
            _done = true;
            Close();
            GameUI.Invoke(then);
            _onBooster = null;
            _onContinue = null;
            _onStone = null;
            _onGiveUp = null;
        }

        void SetButtons(bool on)
        {
            if (_coinsButton != null) _coinsButton.Interactable = on;
            if (_adButton != null) _adButton.Interactable = on;
            if (_stoneButton != null) _stoneButton.Interactable = on;
            if (_giveUpButton != null) _giveUpButton.Interactable = on;
        }

        void OnDestroy()
        {
            if (!_subscribed) return;
            _subscribed = false;
            Economy.OnCoinsChanged -= OnCoinsChanged;
        }
    }
}
