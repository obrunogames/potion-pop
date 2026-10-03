// ============================================================================================================
// New world unlocked: "World N", the world's art in a tilted card that swings into place over a spinning sunburst,
// Luna cheering beside it, the world name in its accent color, the album of 9 cards waiting to be collected (owned
// ones shown, missing ones as silhouettes), confetti + fanfare, and a pulsing "Let's go!". Shown by Home
// (Progress.HasUnseenArea) and by the game flow after the last level of a world.
// ============================================================================================================
using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>New world unlocked (world art + name + album strip + Luna cheering).</summary>
    public class AreaUnlockedPopup : Popup
    {
        const float CardW = 440f, CardH = 520f, MiniW = 62f;

        int _areaNumber;
        RectTransform _card, _album;
        float _sparkle;

        protected override string TitleKey => "home.area_title";
        protected override Vector2 PanelSize => new Vector2(880f, 1430f);

        public static void Open(int areaNumber, Action onClosed = null)
        {
            var popup = PopupManager.Show<AreaUnlockedPopup>(p => p._areaNumber = Mathf.Max(0, areaNumber));
            if (popup != null && onClosed != null) popup.OnClosed += onClosed;
            else if (popup == null) onClosed?.Invoke();
        }

        protected override void BuildContent(RectTransform content)
        {
            var area = Areas.AreaForNumber(_areaNumber);
            Color accent = area != null ? area.accent : DS.Colors.Brand;
            float w = content.rect.width;
            float y = 0f;

            // "World N"
            var number = MetaUI.LocLabel(content, "worlds.world_number", TextStyle.Body, 40f, new Vector2(w, 54f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), _areaNumber + 1);
            number.color = DS.Colors.InkSoft;
            y += 58f;

            // World art card over a sunburst.
            var stage = UIKit.Rect("Stage", content);
            UIKit.Place(stage, new Vector2(0.5f, 1f), new Vector2(w, CardH + 30f), new Vector2(0f, -y));
            var burst = MetaUI.Sunburst(stage, 900f, 0.75f, 20f);
            if (burst != null)
            {
                UIKit.Place(burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 900f), Vector2.zero);
                burst.color = DS.WithAlpha(DS.Lighten(accent, 0.55f), 0.8f);
            }
            _card = BuildCard(stage, area, accent);
            UIKit.Place(_card, new Vector2(0.5f, 0.5f), new Vector2(CardW, CardH), new Vector2(40f, 0f));

            // Entrance: tilt & swing in.
            _card.localScale = Vector3.one * 0.3f;
            _card.localEulerAngles = new Vector3(0f, 0f, -16f);
            Tween.Scale(_card, 1f, 0.6f, Ease.OutBack).SetOvershoot(1.8f).SetDelay(0.15f);
            Tween.Rotate(_card, 3f, 0.6f, Ease.OutBack).SetDelay(0.15f).OnComplete(() =>
            {
                if (_card != null) Tween.Rotate(_card, -3f, 2.2f, Ease.InOutSine).SetLoops(-1, true);
            });

            // Luna cheering at the card's lower-left corner.
            var luna = UIKit.Image(stage, "mascot_cheer", new Vector2(250f, 348f));
            UIKit.Place(luna.rectTransform, new Vector2(0.5f, 0f), new Vector2(250f, 348f), new Vector2(-CardW * 0.5f - 30f, -30f));
            luna.rectTransform.pivot = new Vector2(0.5f, 0f);
            luna.rectTransform.localScale = Vector3.zero;
            Tween.Scale(luna.transform, 1f, 0.45f, Ease.OutBack).SetOvershoot(2.4f).SetDelay(0.5f).OnComplete(() =>
            {
                if (luna == null) return;
                Tween.Move(luna.rectTransform, luna.rectTransform.anchoredPosition + new Vector2(0f, 26f), 0.32f, Ease.OutQuad).SetLoops(-1, true);
            });
            y += CardH + 50f;

            // World name + description.
            var name = MetaUI.Label(content, MetaUI.WorldName(_areaNumber), TextStyle.H1, 76f, new Vector2(w, 98f),
                new Vector2(0.5f, 1f), new Vector2(0f, -y));
            name.color = Color.Lerp(accent, Color.white, 0.12f);
            name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            name.transform.localScale = Vector3.zero;
            Tween.Scale(name.transform, 1f, 0.45f, Ease.OutBack).SetOvershoot(2f).SetDelay(0.45f);
            y += 100f;
            var desc = MetaUI.LocLabel(content, "home.area_desc", TextStyle.Body, 36f, new Vector2(w - 40f, 92f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), Areas.FirstLevelOfArea(_areaNumber), Areas.LastLevelOfArea(_areaNumber));
            desc.color = DS.Colors.InkSoft;
            y += 100f;

            // The album of this world.
            BuildAlbum(content, area, w, y);

            var go = UIKit.ButtonLoc(content, "home.lets_go", ButtonColor.Green, ButtonSize.Large, Close);
            var grt = (RectTransform)go.transform;
            UIKit.Place(grt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 4f));
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(0f, 4f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);
            Tween.Pulse(grt);
        }

        static RectTransform BuildCard(RectTransform parent, AreaInfo area, Color accent)
        {
            var card = UIKit.Rect("Card", parent);
            card.sizeDelta = new Vector2(CardW, CardH);
            UIKit.Shadow(card, 30f, -16f, 0.35f);
            var lip = UIKit.RoundedRect(card, Vector2.zero, DS.Darken(accent, 0.45f), 48f);
            UIKit.Stretch(lip.rectTransform);
            var frame = UIKit.RoundedRect(card, Vector2.zero, accent, 48f);
            UIKit.Stretch(frame.rectTransform, 0f, 0f, 0f, 12f);

            var window = UIKit.Rect("Window", card);
            UIKit.Stretch(window, 16f, 16f, 16f, 28f);
            var maskImg = window.gameObject.AddComponent<Image>();
            maskImg.sprite = UISprites.Rounded;
            maskImg.raycastTarget = false;
            SliceFit.Attach(maskImg, SliceFit.Mode.Corner, 34f * UISprites.RoundedBorderPerRadius);
            window.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            string sprite = area != null ? area.HomeBackground : null;
            var sp = !string.IsNullOrEmpty(sprite) && UISprites.Exists(sprite) ? UISprites.Get(sprite) : null;
            float ww = CardW - 32f, wh = CardH - 44f;
            var art = UIKit.NewImage(window, "Art", sp, sp != null ? Color.white : DS.Lighten(accent, 0.35f));
            if (sp != null && sp.rect.height > 0f)
            {
                float aspect = sp.rect.width / sp.rect.height;
                float w = ww, h = ww / aspect;
                if (h < wh) { h = wh; w = wh * aspect; }
                UIKit.Place(art.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(w, h), new Vector2(0f, -(0.5f - 0.44f) * h));
            }
            else UIKit.Stretch(art.rectTransform);

            var gloss = UIKit.RoundedRect(window, Vector2.zero, new Color(1f, 1f, 1f, 0.16f), 34f);
            UIKit.Stretch(gloss.rectTransform, 0f, 0f, 0f, wh * 0.62f);
            return card;
        }

        void BuildAlbum(RectTransform content, AreaInfo area, float w, float y)
        {
            var cards = area != null ? area.cardIds : null;
            int n = cards != null ? cards.Length : 0;
            if (n == 0) return;
            float miniH = MiniW * CommonUI.CardAspect;
            _album = UIKit.Rect("Album", content);
            UIKit.Place(_album, new Vector2(0.5f, 1f), new Vector2(w, miniH + 70f), new Vector2(0f, -y));
            int owned = Collection.OwnedCount(area.id);
            var label = MetaUI.LocLabel(_album, "worlds.album_progress", TextStyle.Body, 36f, new Vector2(w, 50f), new Vector2(0.5f, 1f),
                Vector2.zero, owned, n);
            label.color = DS.Darken(area.accent, 0.35f);
            float gap = Mathf.Min(12f, (w - n * MiniW) / Mathf.Max(1, n - 1));
            float total = n * MiniW + (n - 1) * gap;
            var minis = new RectTransform[n];
            for (int i = 0; i < n; i++)
            {
                var mini = CommonUI.MiniCard(_album, cards[i], MiniW, Collection.Has(cards[i]));
                if (mini == null) continue;
                UIKit.Place(mini, new Vector2(0.5f, 0f), mini.sizeDelta, new Vector2(-total * 0.5f + MiniW * 0.5f + i * (MiniW + gap), 6f));
                mini.localEulerAngles = new Vector3(0f, 0f, (i - (n - 1) * 0.5f) * -2.5f);
                minis[i] = mini;
            }
            MetaUI.PopInAll(minis, 0.65f, 0.06f);
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
