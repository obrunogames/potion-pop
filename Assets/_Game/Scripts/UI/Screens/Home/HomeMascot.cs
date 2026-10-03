// ============================================================================================================
// Mimi on the Home screen: stands beside the shop entrance, breathes and bobs, hops or waves every few seconds,
// and on tap does a happy jump (cheer pose, sparkles, sound, haptic) and says a random localized tip in a speech
// bubble. Timers run on unscaled time; nothing allocates per frame.
// ============================================================================================================
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class HomeMascot : MonoBehaviour, IPointerClickHandler
    {
        public const int TipCount = 12;
        const float BubbleSeconds = 3.6f;

        /// <summary>Moves (hop/jump); its parent places Mimi.</summary>
        RectTransform _body;
        Image _image;
        RectTransform _bubble;
        CanvasGroup _bubbleGroup;
        TMP_Text _bubbleText;
        Sprite _idleSprite, _cheerSprite;
        float _nextAction;
        float _bubbleHideAt = -1f;
        int _lastTip = -1;
        bool _busy;

        /// <summary>Builds Mimi under `parent` (a rect placed at her feet, pivot bottom-center).</summary>
        public static HomeMascot Create(RectTransform parent, Vector2 size)
        {
            var root = UIKit.Rect("Mimi", parent);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = size;
            root.anchoredPosition = Vector2.zero;
            root.gameObject.AddComponent<HitArea>();   // tap target
            var m = root.gameObject.AddComponent<HomeMascot>();

            // Contact shadow on the sidewalk.
            var shadow = UIKit.NewImage(root, "Shadow", UISprites.Circle, new Color(0.1f, 0.05f, 0.2f, 0.28f));
            UIKit.Place(shadow.rectTransform, new Vector2(0.5f, 0f), new Vector2(size.x * 0.7f, size.x * 0.16f), new Vector2(0f, -size.x * 0.05f));

            var body = UIKit.Rect("Body", root);
            body.anchorMin = body.anchorMax = new Vector2(0.5f, 0f);
            body.pivot = new Vector2(0.5f, 0f);
            body.sizeDelta = size;
            body.anchoredPosition = Vector2.zero;
            m._body = body;
            m._idleSprite = UISprites.Get("mascot_wave");
            m._cheerSprite = UISprites.Get("mascot_cheer");
            var img = UIKit.Image(body, "mascot_wave", size);
            UIKit.Stretch(img.rectTransform);
            img.rectTransform.pivot = new Vector2(0.5f, 0f);
            m._image = img;

            m.BuildBubble(root, size);
            return m;
        }

        void BuildBubble(RectTransform root, Vector2 size)
        {
            const float w = 500f, h = 170f;
            _bubble = UIKit.Rect("Bubble", root);
            UIKit.Place(_bubble, new Vector2(0.5f, 1f), new Vector2(w, h), Vector2.zero);
            _bubble.pivot = new Vector2(0.78f, 0f);
            _bubble.anchoredPosition = new Vector2(-size.x * 0.05f, -size.y * 0.04f);
            _bubbleGroup = _bubble.gameObject.AddComponent<CanvasGroup>();
            _bubbleGroup.blocksRaycasts = false;
            UIKit.Shadow(_bubble, 26f, -12f, 0.3f);
            var tail = UIKit.RoundedRect(_bubble, new Vector2(54f, 54f), Color.white, 10f);
            UIKit.Place(tail.rectTransform, new Vector2(0.78f, 0f), new Vector2(54f, 54f), new Vector2(0f, 2f));
            tail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tail.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            tail.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            var bg = UIKit.RoundedRect(_bubble, Vector2.zero, Color.white, 54f);
            UIKit.Stretch(bg.rectTransform);
            var rim = UIKit.RoundedRect(_bubble, Vector2.zero, DS.WithAlpha(DS.Colors.BrandLight, 0.35f), 48f);
            UIKit.Stretch(rim.rectTransform, 8f, h - 26f, 8f, 8f);
            rim.gameObject.SetActive(false);
            _bubbleText = UIKit.Text(_bubble, "", TextStyle.Body, new Vector2(w - 60f, h - 30f));
            DS.Apply(_bubbleText, TextStyle.Body, 38f);
            UIKit.Stretch(_bubbleText.rectTransform, 30f, 14f, 30f, 18f);
            _bubble.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            _nextAction = Time.unscaledTime + Random.Range(2.5f, 4.5f);
            StartIdle();
        }

        void OnDisable()
        {
            if (_body != null)
            {
                Tween.Kill(_body);
                _body.anchoredPosition = Vector2.zero;
                _body.localScale = Vector3.one;
                _body.localEulerAngles = Vector3.zero;
            }
            if (_image != null && _idleSprite != null) _image.sprite = _idleSprite;
            HideBubble(false);
            _busy = false;
        }

        void StartIdle()
        {
            if (_body == null) return;
            Tween.Kill(_body);
            _body.anchoredPosition = Vector2.zero;
            _body.localEulerAngles = Vector3.zero;
            _body.localScale = Vector3.one;
            // Breathing: a tiny squash & stretch anchored at the feet.
            Tween.Scale(_body, new Vector3(1.025f, 0.975f, 1f), 1.1f, Ease.InOutSine).SetLoops(-1, true);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (_bubbleHideAt > 0f && now >= _bubbleHideAt) HideBubble(true);
            if (_busy || now < _nextAction) return;
            _nextAction = now + Random.Range(4.5f, 7.5f);
            if (Random.value < 0.55f) Hop(46f, false);
            else Wave();
        }

        public void OnPointerClick(PointerEventData e)
        {
            Hop(110f, true);
            AudioManager.Play(Sfx.Pop, 1f, Random.Range(1.05f, 1.25f));
            Haptics.Play(HapticType.Light);
            ShowTip();
            _nextAction = Time.unscaledTime + Random.Range(5f, 8f);
        }

        /// <summary>Jump with anticipation squash, airtime stretch and landing squash.</summary>
        public void Hop(float height, bool happy)
        {
            if (_body == null || !isActiveAndEnabled) return;
            _busy = true;
            Tween.Kill(_body);
            _body.anchoredPosition = Vector2.zero;
            _body.localEulerAngles = Vector3.zero;
            _body.localScale = Vector3.one;
            float up = happy ? 0.24f : 0.2f, down = happy ? 0.26f : 0.2f;
            Tween.Scale(_body, new Vector3(1.12f, 0.86f, 1f), 0.09f, Ease.OutQuad);
            Tween.Scale(_body, new Vector3(0.92f, 1.1f, 1f), 0.12f, Ease.OutQuad).SetDelay(0.09f);
            Tween.Move(_body, new Vector2(0f, height), up, Ease.OutQuad).SetDelay(0.09f);
            Tween.Move(_body, Vector2.zero, down, Ease.InQuad).SetDelay(0.09f + up);
            if (happy)
            {
                if (_image != null && _cheerSprite != null) _image.sprite = _cheerSprite;
                Tween.Rotate(_body, 8f, up, Ease.OutQuad).SetDelay(0.09f);
                Tween.Rotate(_body, 0f, down, Ease.InOutSine).SetDelay(0.09f + up);
                var fxSpace = ScreenManager.Instance != null ? ScreenManager.Instance.FxLayer : null;
                if (fxSpace != null)
                {
                    var rt = (RectTransform)transform;
                    Vector2 head = FX.ToLocal(fxSpace, rt.TransformPoint(new Vector3(0f, rt.rect.height * 0.85f, 0f)));
                    FX.Sparkles(fxSpace, head, 10);
                    FX.Burst(fxSpace, head, "ui_star_small", 8, DS.Colors.Gold, 520f, 0.6f, 34f, -900f);
                }
            }
            float land = 0.09f + up + down;
            Tween.Scale(_body, new Vector3(1.14f, 0.86f, 1f), 0.08f, Ease.OutQuad).SetDelay(land);
            Tween.Scale(_body, Vector3.one, 0.3f, Ease.OutBack).SetOvershoot(2.5f).SetDelay(land + 0.08f)
                .OnComplete(() =>
                {
                    if (this == null) return;
                    if (_image != null && _idleSprite != null) _image.sprite = _idleSprite;
                    _busy = false;
                    StartIdle();
                });
        }

        /// <summary>Friendly sway (the art already waves; the sway makes it read as a wave).</summary>
        public void Wave()
        {
            if (_body == null || !isActiveAndEnabled) return;
            _busy = true;
            Tween.Kill(_body);
            _body.localScale = Vector3.one;
            _body.localEulerAngles = Vector3.zero;
            Tween.Rotate(_body, -5f, 0.18f, Ease.InOutSine);
            Tween.Rotate(_body, 5f, 0.3f, Ease.InOutSine).SetDelay(0.18f);
            Tween.Rotate(_body, -4f, 0.28f, Ease.InOutSine).SetDelay(0.48f);
            Tween.Rotate(_body, 0f, 0.22f, Ease.OutBack).SetDelay(0.76f).OnComplete(() =>
            {
                if (this == null) return;
                _busy = false;
                StartIdle();
            });
        }

        /// <summary>Shows a random tip (never the same twice in a row).</summary>
        public void ShowTip()
        {
            if (_bubble == null) return;
            int tip = Random.Range(0, TipCount);
            if (tip == _lastTip) tip = (tip + 1) % TipCount;
            _lastTip = tip;
            ShowText(Loc.T("home.tip." + (tip + 1)));
        }

        public void ShowText(string text)
        {
            if (_bubble == null || string.IsNullOrEmpty(text)) return;
            _bubbleText.text = text;
            _bubble.gameObject.SetActive(true);
            _bubble.SetAsLastSibling();
            Tween.Kill(_bubble);
            Tween.Kill(_bubbleGroup);
            _bubbleGroup.alpha = 1f;
            _bubble.localScale = Vector3.one * 0.4f;
            Tween.Scale(_bubble, 1f, 0.35f, Ease.OutBack).SetOvershoot(2f);
            _bubbleHideAt = Time.unscaledTime + BubbleSeconds;
        }

        void HideBubble(bool animate)
        {
            _bubbleHideAt = -1f;
            if (_bubble == null || !_bubble.gameObject.activeSelf) return;
            Tween.Kill(_bubble);
            Tween.Kill(_bubbleGroup);
            if (!animate)
            {
                _bubble.gameObject.SetActive(false);
                return;
            }
            var b = _bubble;
            Tween.Scale(b, 0.6f, 0.18f, Ease.InBack);
            Tween.Fade(_bubbleGroup, 0f, 0.18f).OnComplete(() => { if (b != null) b.gameObject.SetActive(false); });
        }
    }
}
