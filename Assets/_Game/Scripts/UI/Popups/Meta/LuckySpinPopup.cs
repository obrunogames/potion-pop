// ============================================================================================================
// Lucky Spin: the spin_wheel art (segment i covers [45i°, 45i+45°) clockwise from 12 o'clock) with each segment's
// reward icon + amount at its center (22.5 + 45i°), blinking rim bulbs and the spin_pointer at the top. A spin
// turns the wheel 4-5 full turns plus the offset that brings the segment returned by LuckySpin.Spin under the
// pointer (OutCubic, ~4 s), ticking on every segment boundary. Free spin / "Spin" + AD (ad spins left) / "Come
// back tomorrow" with a countdown. The reward (granted by LuckySpin.Spin) is revealed with the RewardPopup.
// ============================================================================================================
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class LuckySpinPopup : Popup
    {
        const float WheelSize = 700f;
        const int BulbCount = 16;
        const float SpinSeconds = 4.2f;

        RectTransform _wheel, _pointer, _halo;
        readonly Image[] _bulbs = new Image[BulbCount];
        readonly RectTransform[] _segments = new RectTransform[LuckySpin.SegmentCount];
        UIButton _button;
        TMP_Text _info;
        float _angle;          // clockwise wheel rotation in degrees
        int _lastBoundary;
        bool _spinning;
        float _blinkTimer, _tick;
        bool _blinkPhase;
        Reward _pending;
        bool _hasPending;

        protected override string TitleKey => "home.spin_title";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, 1320f);
        protected override bool CloseOnOverlayTap => !_spinning;

        public static void Open()
        {
            if (PopupManager.IsOpen<LuckySpinPopup>()) return;
            PopupManager.Show<LuckySpinPopup>();
        }

        // ---------------------------------------------------------------------------------------- build

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(w, WheelSize + 70f), new Vector2(0f, -60f));

            // Halo + rays behind the wheel.
            var halo = UIKit.Image(stage, "sunburst", new Vector2(WheelSize + 220f, WheelSize + 220f));
            halo.color = new Color(1f, 1f, 1f, 0.75f);
            UIKit.Place(halo.rectTransform, new Vector2(0.5f, 0.5f), halo.rectTransform.sizeDelta, new Vector2(0f, -20f));
            MetaUI.Spin(halo.transform, -26f);
            _halo = halo.rectTransform;
            var shadow = UIKit.NewImage(stage, "Shadow", UISprites.SoftShadow, new Color(0f, 0f, 0f, 0.4f));
            UIKit.Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(WheelSize + 40f, WheelSize + 40f), new Vector2(0f, -40f));

            // Wheel (rotating) inside a circular mask (the art's corners are transparent anyway).
            var holder = UIKit.Rect("WheelHolder", stage);
            UIKit.Place(holder, new Vector2(0.5f, 0.5f), new Vector2(WheelSize, WheelSize), new Vector2(0f, -20f));
            _wheel = UIKit.Rect("Wheel", holder);
            UIKit.Stretch(_wheel);
            var art = UIKit.Image(_wheel, "spin_wheel", new Vector2(WheelSize, WheelSize));
            UIKit.Stretch(art.rectTransform);

            float r = WheelSize * 0.5f;
            for (int i = 0; i < LuckySpin.SegmentCount; i++)
            {
                float theta = 22.5f + 45f * i;
                float rad = theta * Mathf.Deg2Rad;
                var seg = UIKit.Rect("Segment" + i, _wheel);
                UIKit.Place(seg, new Vector2(0.5f, 0.5f), new Vector2(170f, 190f), new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (r * 0.58f));
                seg.pivot = new Vector2(0.5f, 0.5f);
                seg.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (r * 0.58f);
                seg.localEulerAngles = new Vector3(0f, 0f, -theta);
                var reward = LuckySpin.SegmentReward(i);
                var icon = UIKit.Image(seg, reward.IconSprite, new Vector2(104f, 104f));
                UIKit.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(104f, 104f), new Vector2(0f, -4f));
                var amount = UIKit.Text(seg, reward.AmountText, TextStyle.H3, new Vector2(170f, 56f));
                DS.Apply(amount, TextStyle.H3, 42f);
                UIKit.Place(amount.rectTransform, new Vector2(0.5f, 0f), new Vector2(170f, 56f), new Vector2(0f, 20f));
                _segments[i] = seg;
            }

            // Rim bulbs (glows over the art's bulbs, every 22.5°).
            for (int i = 0; i < BulbCount; i++)
            {
                float rad = i * (360f / BulbCount) * Mathf.Deg2Rad;
                var bulb = UIKit.NewImage(_wheel, "Bulb" + i, UISprites.Glow, new Color(1f, 0.97f, 0.75f, 1f));
                UIKit.Place(bulb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(66f, 66f), Vector2.zero);
                bulb.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                bulb.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (r * 0.935f);
                _bulbs[i] = bulb;
            }

            // Pointer at 12 o'clock (its tip points down into the wheel).
            _pointer = UIKit.Rect("Pointer", stage);
            UIKit.Place(_pointer, new Vector2(0.5f, 0.5f), new Vector2(112f, 168f), Vector2.zero);
            _pointer.pivot = new Vector2(0.5f, 0.8f);
            // Pivot 80% up the art: the tip (0.8 * 168 below the pivot) rests on the rim, just inside the bulbs.
            _pointer.anchoredPosition = new Vector2(0f, -20f + r + 104f);
            var pointerShadow = UIKit.NewImage(_pointer, "Shadow", UISprites.SoftShadow, new Color(0f, 0f, 0f, 0.35f));
            UIKit.Stretch(pointerShadow.rectTransform, -6f, 10f, -6f, -16f);
            var pointer = UIKit.Image(_pointer, "spin_pointer", new Vector2(112f, 168f));
            UIKit.Stretch(pointer.rectTransform);

            // Button + info line.
            _info = MetaUI.Label(content, "", TextStyle.Body, 38f, new Vector2(w, 56f), new Vector2(0.5f, 0f), new Vector2(0f, 184f));
            _info.color = DS.Colors.InkSoft;
            _button = UIKit.ButtonLoc(content, "home.spin_free_btn", ButtonColor.Green, ButtonSize.Large, OnSpin);
            var brt = (RectTransform)_button.transform;
            UIKit.Place(brt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), Vector2.zero);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, 6f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);

            SetAngle(Random.Range(0f, 360f));
            _lastBoundary = Mathf.FloorToInt(_angle / 45f);
            RefreshButton();

            holder.localScale = Vector3.one * 0.5f;
            holder.localEulerAngles = new Vector3(0f, 0f, 40f);
            Tween.Scale(holder, 1f, 0.6f, Ease.OutBack).SetOvershoot(1.6f).SetDelay(0.1f);
            Tween.Rotate(holder, 0f, 0.7f, Ease.OutBack).SetDelay(0.1f);
        }

        // ---------------------------------------------------------------------------------------- state

        void RefreshButton()
        {
            if (_button == null) return;
            var brt = _button.transform;
            Tween.Kill(brt);
            brt.localScale = Vector3.one;
            if (LuckySpin.HasFreeSpin)
            {
                _button.SetLabelKey("home.spin_free_btn");
                _button.SetColor(ButtonColor.Green);
                _button.SetAdBadge(false);
                _button.Interactable = !_spinning;
                if (!_spinning) Tween.Pulse(brt);
            }
            else if (LuckySpin.AdSpinsLeft > 0)
            {
                _button.SetLabelKey("home.spin_btn");
                _button.SetColor(ButtonColor.Purple);
                _button.SetAdBadge(true);
                _button.Interactable = !_spinning;
                if (!_spinning) Tween.Pulse(brt, 1.04f);
            }
            else
            {
                _button.SetLabelKey("home.come_back");
                _button.SetColor(ButtonColor.Gray);
                _button.SetAdBadge(false);
                _button.Interactable = false;
            }
            RefreshInfo();
        }

        void RefreshInfo()
        {
            if (_info == null) return;
            if (LuckySpin.HasFreeSpin) _info.text = "";
            else if (LuckySpin.AdSpinsLeft > 0) _info.text = Loc.T("home.spins_left", LuckySpin.AdSpinsLeft);
            else _info.text = Loc.T("home.spin_next", TimeUtil.FormatDuration(TimeUtil.SecondsToMidnight));
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinkTimer = _spinning ? 0.09f : 0.4f;
                _blinkPhase = !_blinkPhase;
                for (int i = 0; i < _bulbs.Length; i++)
                {
                    var b = _bulbs[i];
                    if (b == null) continue;
                    bool on = ((i & 1) == 0) == _blinkPhase;
                    var c = b.color;
                    c.a = on ? 1f : 0.15f;
                    b.color = c;
                }
            }
            if (_spinning) return;
            _tick += dt;
            if (_tick >= 1f)
            {
                _tick = 0f;
                RefreshInfo();
            }
        }

        // ---------------------------------------------------------------------------------------- spin

        void OnSpin()
        {
            if (_spinning || IsClosing) return;
            if (LuckySpin.HasFreeSpin)
            {
                Spin(false);
                return;
            }
            if (LuckySpin.AdSpinsLeft <= 0) return;
            AdsService.ShowRewarded(AdPlacement.ExtraSpin, earned =>
            {
                if (!earned || this == null || IsClosing || _spinning) return;
                Spin(true);
            });
        }

        void Spin(bool viaAd)
        {
            int index = LuckySpin.Spin(viaAd);
            if (index < 0)
            {
                RefreshButton();
                return;
            }
            _pending = LuckySpin.SegmentReward(index);
            _hasPending = true;
            var bar = TopBar.Instance;
            if (bar != null) bar.Hold(_pending);

            _spinning = true;
            RefreshButton();
            AudioManager.Play(Sfx.Whoosh);
            Haptics.Play(HapticType.Medium);

            // Final clockwise angle: the segment center (22.5 + 45i) ends under the pointer (0°), with a little jitter
            // inside the segment so it does not look mechanical.
            float theta = 22.5f + 45f * index;
            float jitter = Random.Range(-15f, 15f);
            float start = Mathf.Repeat(_angle, 360f);
            _lastBoundary = Mathf.FloorToInt(start / 45f);
            SetAngle(start);
            float targetMod = Mathf.Repeat(360f - theta + jitter, 360f);
            float delta = Mathf.Repeat(targetMod - start, 360f);
            int turns = Random.Range(4, 6);
            float end = start + turns * 360f + delta;

            Tween.Value(start, end, SpinSeconds, SetAngle, Ease.OutCubic).SetLink(this).OnComplete(() => OnStopped(index));
            if (_halo != null) Tween.Punch(_halo, 0.08f, 0.5f);
        }

        void SetAngle(float clockwise)
        {
            _angle = clockwise;
            if (_wheel != null) _wheel.localEulerAngles = new Vector3(0f, 0f, -clockwise);
            if (!_spinning) return;
            int boundary = Mathf.FloorToInt(clockwise / 45f);
            if (boundary == _lastBoundary) return;
            _lastBoundary = boundary;
            AudioManager.Play(Sfx.SpinTick, 0.7f);
            if (_pointer != null)
            {
                Tween.Kill(_pointer);
                _pointer.localEulerAngles = new Vector3(0f, 0f, 14f);
                Tween.Rotate(_pointer, 0f, 0.12f, Ease.OutQuad);
            }
        }

        void OnStopped(int index)
        {
            _spinning = false;
            AudioManager.Play(Sfx.SpinWin);
            Haptics.Play(HapticType.Success);
            var seg = index >= 0 && index < _segments.Length ? _segments[index] : null;
            if (seg != null)
            {
                Tween.Kill(seg);
                seg.localScale = Vector3.one;
                Tween.Scale(seg, 1.35f, 0.18f, Ease.OutQuad);
                Tween.Scale(seg, 1f, 0.35f, Ease.OutBack).SetDelay(0.5f);
                MetaUI.Celebrate(seg, 14, 20);
            }
            var sm = ScreenManager.Instance;
            if (sm != null && _pointer != null) FX.Ring(sm.FxLayer, FX.LocalCenterOf(_pointer), DS.Colors.Accent, 360f);

            Tween.Delay(0.85f, Reveal).SetLink(this);
        }

        void Reveal()
        {
            if (!_hasPending) return;
            _hasPending = false;
            var reward = _pending;
            RewardPopup.Open(new[] { reward }, "home.you_won", RefreshButton, TopBar.Instance != null);
        }

        protected override void OnClosing()
        {
            // Closed while the result was still pending: hand the held reward to the counters.
            if (!_hasPending) return;
            _hasPending = false;
            var bar = TopBar.Instance;
            if (bar != null) bar.Release(_pending.type, _pending.amount);
        }

        public override void OnBack()
        {
            if (_spinning) return;
            base.OnBack();
        }

        protected override void OnCloseButton()
        {
            if (_spinning) return;
            base.OnCloseButton();
        }
    }
}
