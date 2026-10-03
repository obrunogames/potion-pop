// ============================================================================================================
// New area unlocked: the new store facade in a tilted card that swings into place, the area name and number,
// Mimi cheering beside it, confetti + fanfare, and a pulsing "Let's go!".
// ============================================================================================================
using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>New area unlocked (store facade + area name + mascot cheer).</summary>
    public class AreaUnlockedPopup : Popup
    {
        int _areaNumber;
        RectTransform _card;
        float _sparkle;

        protected override string TitleKey => "home.area_title";
        protected override Vector2 PanelSize => new Vector2(880f, 1360f);

        public static void Open(int areaNumber, Action onClosed = null)
        {
            var popup = PopupManager.Show<AreaUnlockedPopup>(p => p._areaNumber = Mathf.Max(0, areaNumber));
            if (popup != null && onClosed != null) popup.OnClosed += onClosed;
        }

        protected override void BuildContent(RectTransform content)
        {
            var area = Areas.AreaForNumber(_areaNumber);
            float w = content.rect.width;

            // "Area N"
            var number = MetaUI.LocLabel(content, "area.number", TextStyle.Body, 40f, new Vector2(w, 54f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), _areaNumber + 1);
            number.color = DS.Colors.InkSoft;

            // Facade card with sunburst behind.
            const float cardW = 520f, cardH = 600f;
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(w, cardH + 40f), new Vector2(0f, -66f));
            var burst = MetaUI.Sunburst(stage, 860f, 0.8f, 20f);
            if (burst != null) UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(860f, 860f), Vector2.zero);

            _card = UIKit.Rect("Card", stage);
            UIKit.Place(_card, new Vector2(0.5f, 0.5f), new Vector2(cardW, cardH), Vector2.zero);
            UIKit.Shadow(_card, 30f, -16f, 0.35f);
            var frame = UIKit.RoundedRect(_card, Vector2.zero, area != null ? area.accent : DS.Colors.Brand, 48f);
            UIKit.Stretch(frame.rectTransform);
            var window = UIKit.Rect("Window", _card);
            UIKit.Stretch(window, 16f, 16f, 16f, 16f);
            window.gameObject.AddComponent<RectMask2D>();
            var facade = UIKit.Image(window, area != null ? area.HomeBackground : "home_grocery", new Vector2(cardW - 32f, (cardW - 32f) * 1.5f));
            // Show the facade (~45% from the top of the art) in the middle of the window.
            UIKit.Place(facade.rectTransform, new Vector2(0.5f, 0.5f), facade.rectTransform.sizeDelta, new Vector2(0f, -(cardW - 32f) * 1.5f * 0.05f));
            var gloss = UIKit.RoundedRect(_card, Vector2.zero, new Color(1f, 1f, 1f, 0.16f), 40f);
            UIKit.Stretch(gloss.rectTransform, 16f, 16f, 16f, cardH * 0.62f);

            // Entrance: tilt & swing in.
            _card.localScale = Vector3.one * 0.3f;
            _card.localEulerAngles = new Vector3(0f, 0f, -16f);
            Tween.Scale(_card, 1f, 0.6f, Ease.OutBack).SetOvershoot(1.8f).SetDelay(0.15f);
            Tween.Rotate(_card, 3f, 0.6f, Ease.OutBack).SetDelay(0.15f).OnComplete(() =>
            {
                if (_card != null) Tween.Rotate(_card, -3f, 2.2f, Ease.InOutSine).SetLoops(-1, true);
            });

            // Mimi cheering at the card's lower-left corner.
            var mimi = UIKit.Image(stage, "mascot_cheer", new Vector2(250f, 348f));
            UIKit.Place(mimi.rectTransform, new Vector2(0.5f, 0f), new Vector2(250f, 348f), new Vector2(-cardW * 0.5f - 10f, -40f));
            mimi.rectTransform.pivot = new Vector2(0.5f, 0f);
            mimi.rectTransform.localScale = Vector3.zero;
            Tween.Scale(mimi.transform, 1f, 0.45f, Ease.OutBack).SetOvershoot(2.4f).SetDelay(0.5f).OnComplete(() =>
            {
                if (mimi == null) return;
                Tween.Move(mimi.rectTransform, mimi.rectTransform.anchoredPosition + new Vector2(0f, 26f), 0.32f, Ease.OutQuad).SetLoops(-1, true);
            });

            // Area name + description.
            float y = 66f + cardH + 60f;
            var name = MetaUI.LocLabel(content, area != null ? area.NameKey : "area.grocery", TextStyle.H1, 78f, new Vector2(w, 100f),
                new Vector2(0.5f, 1f), new Vector2(0f, -y));
            if (area != null) name.color = Color.Lerp(area.accent, Color.white, 0.15f);
            name.transform.localScale = Vector3.zero;
            Tween.Scale(name.transform, 1f, 0.45f, Ease.OutBack).SetOvershoot(2f).SetDelay(0.45f);
            var desc = MetaUI.LocLabel(content, "home.area_desc", TextStyle.Body, 36f, new Vector2(w - 40f, 100f), new Vector2(0.5f, 1f), new Vector2(0f, -(y + 100f)));
            desc.color = DS.Colors.InkSoft;

            var go = UIKit.ButtonLoc(content, "home.lets_go", ButtonColor.Green, ButtonSize.Large, Close);
            var grt = (RectTransform)go.transform;
            UIKit.Place(grt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 4f));
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(0f, 4f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);
            Tween.Pulse(grt);
        }

        protected override void OnOpened()
        {
            AudioManager.Play(Sfx.Fanfare);
            Haptics.Play(HapticType.Success);
            FX.Confetti(null, 140);
            if (_card != null) MetaUI.Celebrate(_card, 16, 20);
        }

        void Update()
        {
            if (_card == null || !IsOpen) return;
            _sparkle -= Time.unscaledDeltaTime;
            if (_sparkle > 0f) return;
            _sparkle = 0.9f;
            var sm = ScreenManager.Instance;
            if (sm == null || sm.FxLayer == null) return;
            Rect r = _card.rect;
            var p = new Vector3(UnityEngine.Random.Range(r.xMin, r.xMax), UnityEngine.Random.Range(r.yMin, r.yMax), 0f);
            FX.Sparkles(sm.FxLayer, FX.ToLocal(sm.FxLayer, _card.TransformPoint(p)), 3);
        }
    }
}
