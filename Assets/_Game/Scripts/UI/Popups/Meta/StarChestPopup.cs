// ============================================================================================================
// Star Chest: the chest bobbing on a sunburst, progress x/30 stars, what's inside (coins + boosters). When it
// can open: "Open" → the chest shakes harder and harder, bursts open (chest_open, ring, stars, sparkles) and the
// rewards are revealed with the RewardPopup. Otherwise "Play" jumps to the Level Start popup to earn stars.
// ============================================================================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class StarChestPopup : Popup
    {
        RectTransform _chest, _chestBob;
        Image _chestImage, _burst;
        ProgressBar _bar;
        TMP_Text _desc;
        UIButton _button;
        bool _opening;
        Reward[] _pending;
        bool _held;

        protected override string TitleKey => "home.chest_title";
        protected override Vector2 PanelSize => new Vector2(880f, 1260f);
        protected override bool CloseOnOverlayTap => !_opening;

        public static void Open()
        {
            if (PopupManager.IsOpen<StarChestPopup>()) return;
            PopupManager.Show<StarChestPopup>();
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;

            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(w, 480f), Vector2.zero);
            _burst = MetaUI.Sunburst(stage, 640f, 0.85f, 18f);
            UIKit.Place(_burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(640f, 640f), Vector2.zero);
            var glow = UIKit.NewImage(stage, "Glow", UISprites.Glow, new Color(1f, 0.9f, 0.5f, 0.7f));
            UIKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(480f, 480f), Vector2.zero);
            Tween.Scale(glow.transform, 1.12f, 1.1f, Ease.InOutSine).SetLoops(-1, true);

            _chestBob = UIKit.Rect("Bob", stage);
            UIKit.Place(_chestBob, new Vector2(0.5f, 0.5f), new Vector2(360f, 330f), Vector2.zero);
            Tween.Bob(_chestBob, 12f, 2f);
            _chest = UIKit.Rect("Chest", _chestBob);
            UIKit.Stretch(_chest);
            _chest.pivot = new Vector2(0.5f, 0.2f);
            _chestImage = UIKit.Image(_chest, "chest_closed", new Vector2(360f, 330f));
            UIKit.Stretch(_chestImage.rectTransform);

            // Progress.
            _bar = UIKit.ProgressBar(content, new Vector2(w - 160f, 70f));
            UIKit.Place((RectTransform)_bar.transform, new Vector2(0.5f, 1f), new Vector2(w - 160f, 70f), new Vector2(30f, -500f));
            _bar.SetIcon("icon_star");
            _bar.label.gameObject.SetActive(true);
            DS.Apply(_bar.label, TextStyle.Badge, 38f);

            _desc = MetaUI.Label(content, "", TextStyle.Body, 40f, new Vector2(w - 20f, 100f), new Vector2(0.5f, 1f), new Vector2(0f, -590f));

            // Inside: coins + boosters.
            var inside = UIKit.Rect("Inside", content);
            UIKit.Place(inside, new Vector2(0.5f, 1f), new Vector2(w, 150f), new Vector2(0f, -700f));
            var inset = UIKit.Panel(inside, "panel_inset", new Vector2(w, 150f));
            UIKit.Stretch(inset.rectTransform);
            var label = MetaUI.LocLabel(inside, "home.chest_inside", TextStyle.Body, 36f, new Vector2(220f, 60f), new Vector2(0f, 0.5f), new Vector2(30f, 0f));
            label.color = DS.Colors.InkSoft;
            label.alignment = TextAlignmentOptions.Left;
            var coin = UIKit.Image(inside, "coins_medium", new Vector2(100f, 100f));
            UIKit.Place(coin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(100f, 100f), new Vector2(-40f, 4f));
            var coinText = MetaUI.Label(inside, Loc.Number(StarChest.Coins), TextStyle.H3, 44f, new Vector2(140f, 60f), new Vector2(0.5f, 0.5f), new Vector2(92f, 0f));
            coinText.alignment = TextAlignmentOptions.Left;
            var pack = UIKit.Image(inside, "booster_pack", new Vector2(100f, 100f));
            UIKit.Place(pack.rectTransform, new Vector2(1f, 0.5f), new Vector2(100f, 100f), new Vector2(-150f, 4f));
            var packText = MetaUI.Label(inside, Loc.T("reward.amount.count", StarChest.BoosterCount), TextStyle.H3, 44f, new Vector2(100f, 60f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f));
            packText.alignment = TextAlignmentOptions.Left;

            _button = UIKit.ButtonLoc(content, "ui.open", ButtonColor.Green, ButtonSize.Large, OnButton);
            var brt = (RectTransform)_button.transform;
            UIKit.Place(brt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), Vector2.zero);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, 6f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);

            _chestBob.localScale = Vector3.zero;
            Tween.Scale(_chestBob, 1f, 0.5f, Ease.OutBack).SetOvershoot(2f).SetDelay(0.1f);
            Refresh(false);
        }

        void Refresh(bool animate)
        {
            bool open = StarChest.CanOpen;
            if (_bar != null)
            {
                _bar.SetValue(StarChest.Progress01, animate);
                _bar.SetLabel(MetaUI.Fraction(StarChest.Progress, StarChest.Goal));
                _bar.label.gameObject.SetActive(true);
            }
            if (_desc != null) _desc.text = open ? Loc.T("home.chest_ready") : Loc.T("home.chest_desc", Loc.Number(StarChest.Goal));
            if (_burst != null) _burst.color = new Color(1f, 1f, 1f, open ? 0.95f : 0.35f);
            if (_chestImage != null && !_opening)
            {
                var sp = UISprites.Get("chest_closed");
                if (sp != null) _chestImage.sprite = sp;
            }
            if (_button != null)
            {
                var brt = _button.transform;
                Tween.Kill(brt);
                brt.localScale = Vector3.one;
                _button.SetLabelKey(open ? "ui.open" : "ui.play");
                _button.SetIcon(open ? null : "icon_play");
                _button.Interactable = !_opening;
                if (!_opening) Tween.Pulse(brt, open ? 1.07f : 1.04f);
            }
            if (_chest != null && !_opening)
            {
                Tween.Kill(_chest);
                _chest.localEulerAngles = Vector3.zero;
                _chest.localScale = Vector3.one;
                if (open) Tween.Rotate(_chest, 5f, 0.25f, Ease.InOutSine).SetLoops(-1, true);
            }
        }

        void OnButton()
        {
            if (_opening || IsClosing) return;
            if (!StarChest.CanOpen)
            {
                Close();
                LevelStartPopup.Open(Progress.CurrentLevel);
                return;
            }
            var rewards = StarChest.Open();
            if (rewards.Length == 0)
            {
                Refresh(true);
                return;
            }
            var bar = TopBar.Instance;
            if (bar != null) bar.Hold(rewards);
            _pending = rewards;
            _held = bar != null;
            _opening = true;
            Refresh(false);

            // Shake harder and harder, then pop open.
            Tween.Kill(_chest);
            _chest.localEulerAngles = Vector3.zero;
            float t = 0f;
            for (int i = 0; i < 8; i++)
            {
                float a = (i % 2 == 0 ? 1f : -1f) * (4f + i * 1.6f);
                float d = 0.085f;
                Tween.Rotate(_chest, a, d, Ease.InOutSine).SetDelay(t);
                t += d;
            }
            Tween.Rotate(_chest, 0f, 0.08f, Ease.OutQuad).SetDelay(t);
            Tween.Scale(_chest, new Vector3(1.12f, 0.9f, 1f), t, Ease.InQuad);
            AudioManager.Play(Sfx.Whoosh, 0.6f);
            Haptics.Play(HapticType.Light);

            Tween.Delay(t + 0.08f, () =>
            {
                if (this == null) return;
                var open = UISprites.Get("chest_open");
                if (open != null && _chestImage != null) _chestImage.sprite = open;
                _chest.localScale = new Vector3(0.9f, 1.2f, 1f);
                Tween.Scale(_chest, 1f, 0.45f, Ease.OutBack).SetOvershoot(2.5f);
                AudioManager.Play(Sfx.ChestOpen);
                Haptics.Play(HapticType.Heavy);
                var sm = ScreenManager.Instance;
                if (sm != null && sm.FxLayer != null)
                {
                    Vector2 p = FX.LocalCenterOf(_chest);
                    FX.Ring(sm.FxLayer, p, DS.Colors.Accent, 620f);
                    FX.Burst(sm.FxLayer, p, "icon_coin", 14, null, 900f, 0.9f, 56f, -1500f);
                }
                MetaUI.Celebrate(_chest, 18, 26);
                FX.ScreenShake(10f, 0.25f);
            }).SetLink(this);

            Tween.Delay(t + 0.95f, Reveal).SetLink(this);
        }

        void Reveal()
        {
            if (_pending == null) return;
            var rewards = _pending;
            _pending = null;
            RewardPopup.Open(rewards, "home.chest_title", () =>
            {
                if (this == null || IsClosing) return;
                _opening = false;
                Refresh(true);
            }, _held);
        }

        public override void OnBack()
        {
            if (_opening) return;
            base.OnBack();
        }

        protected override void OnCloseButton()
        {
            if (_opening) return;
            base.OnCloseButton();
        }

        protected override void OnClosing()
        {
            // Closed by code mid-animation: never lose the (already granted) rewards' display.
            if (_pending != null) Reveal();
        }
    }
}
