using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Candy button built by UIKit.Button / ButtonLoc / IconButton.
    /// Hierarchy: root (HitArea + Button + Pressable + UIButton, fixed hit rect) → Visual (squashed on press, faded
    /// when disabled) → Background (9-sliced btn_&lt;color&gt;) + Content row [AD video icon][icon][label][coin][price].
    /// </summary>
    public class UIButton : MonoBehaviour
    {
        public Button button;
        public Image background;
        public TMP_Text label;
        public Image icon;
        public event Action OnClick;

        // ---- additions
        /// <summary>Visual child (background + content). Pressable squashes it; animate the root freely (pulse...).</summary>
        public RectTransform Visual;
        /// <summary>Horizontal layout row holding the icons, label and price.</summary>
        public RectTransform ContentRow;
        public Pressable Pressable;
        /// <summary>Minimum seconds between two clicks (prevents double-open of popups).</summary>
        public float clickCooldown = 0.2f;

        ButtonColor _color;
        bool _hasColor;
        bool _customBackground;
        bool _interactable = true;
        float _lastClick = -10f;
        int _price;
        float _height;
        TextStyle _labelStyle = TextStyle.H3;
        float _labelSize = 50f;
        CanvasGroup _visualGroup;
        Image _priceCoin;
        TMP_Text _priceText;
        Image _adIcon;

        public bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                ApplyState();
            }
        }

        public ButtonColor Color => _color;
        public int Price => _price;

        public void SetLabel(string text)
        {
            if (label == null) return;
            var loc = label.GetComponent<LocText>();
            if (loc != null) loc.Clear();
            label.text = text ?? "";
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>Localized label that follows language changes.</summary>
        public void SetLabelKey(string key, params object[] args)
        {
            if (label == null) return;
            var loc = label.GetComponent<LocText>();
            if (loc == null) loc = label.gameObject.AddComponent<LocText>();
            label.gameObject.SetActive(!string.IsNullOrEmpty(key));
            loc.Set(key, args);
        }

        public void SetColor(ButtonColor color)
        {
            _color = color;
            _hasColor = true;
            _customBackground = false;
            ApplyBackground();
        }

        /// <summary>Shows a coin icon + price next to/below the label. price &lt;= 0 hides it.</summary>
        public void SetPrice(int coins)
        {
            _price = coins;
            if (coins <= 0)
            {
                if (_priceCoin != null) _priceCoin.gameObject.SetActive(false);
                if (_priceText != null) _priceText.gameObject.SetActive(false);
                return;
            }
            if (_priceCoin == null && ContentRow != null)
            {
                _priceCoin = AddRowIcon("icon_coin", _height * 0.52f, -1);
                _priceText = UIKit.Text(ContentRow, "", _labelStyle, new Vector2(10, _height * 0.8f));
                if (_priceText != null)
                {
                    _priceText.name = "Price";
                    DS.Apply(_priceText, _labelStyle, _labelSize);
                }
            }
            if (_priceCoin != null) _priceCoin.gameObject.SetActive(true);
            if (_priceText != null)
            {
                _priceText.gameObject.SetActive(true);
                _priceText.text = Loc.Number(coins);
            }
        }

        /// <summary>Shows the "watch ad" video icon badge.</summary>
        public void SetAdBadge(bool visible)
        {
            if (_adIcon == null)
            {
                if (!visible || ContentRow == null) return;
                _adIcon = AddRowIcon("icon_video", _height * 0.6f, 0);
            }
            if (_adIcon != null) _adIcon.gameObject.SetActive(visible);
        }

        /// <summary>Sets/replaces the inline icon (null hides it).</summary>
        public void SetIcon(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                if (icon != null) icon.gameObject.SetActive(false);
                return;
            }
            if (icon == null && ContentRow != null)
            {
                int index = _adIcon != null ? _adIcon.transform.GetSiblingIndex() + 1 : 0;
                icon = AddRowIcon(spriteName, _height * 0.6f, index);
                return;
            }
            if (icon == null) return;
            icon.gameObject.SetActive(true);
            var sp = UISprites.Get(spriteName);
            icon.sprite = sp != null ? sp : UISprites.Circle;
            icon.color = sp != null ? UnityEngine.Color.white : DS.Colors.BrandLight;
        }

        /// <summary>Overrides the background sprite (keeps the disabled-alpha look, ignores ButtonColor).</summary>
        public void SetBackground(string spriteName)
        {
            if (background == null) return;
            var sp = UISprites.Get(spriteName);
            background.sprite = sp != null ? sp : UISprites.Rounded;
            background.color = UnityEngine.Color.white;
            _customBackground = true;
            _hasColor = false;
        }

        /// <summary>
        /// Grows the (invisible) hit rect to at least minSize x minSize (default: the 110 px design-system tap
        /// target) while the visual keeps its size and on-screen position. For fixed-size (non-stretched) buttons.
        /// </summary>
        public void ExpandHitArea(float minSize = DS.Space.MinTapTarget)
        {
            var rt = (RectTransform)transform;
            Vector2 size = rt.sizeDelta;
            var grow = new Vector2(Mathf.Max(0f, minSize - size.x), Mathf.Max(0f, minSize - size.y));
            if (grow == Vector2.zero || Visual == null) return;
            rt.sizeDelta = size + grow;
            // The rect grows around its pivot; move it so the (centered) visual stays where it was.
            rt.anchoredPosition += new Vector2((rt.pivot.x - 0.5f) * grow.x, (rt.pivot.y - 0.5f) * grow.y);
            Visual.offsetMin += grow * 0.5f;
            Visual.offsetMax -= grow * 0.5f;
        }

        protected void RaiseClick() { OnClick?.Invoke(); }

        // ---------------------------------------------------------------------------------------- internals

        void HandleClick()
        {
            if (!_interactable) return;
            float now = Time.unscaledTime;
            if (now - _lastClick < clickCooldown) return;
            _lastClick = now;
            RaiseClick();
        }

        void ApplyState()
        {
            if (button != null) button.interactable = _interactable;
            if (_visualGroup != null) _visualGroup.alpha = _interactable ? 1f : 0.6f;
            if (_hasColor && !_customBackground) ApplyBackground();
            else if (background == null && icon != null)
                icon.color = _interactable ? UnityEngine.Color.white : new UnityEngine.Color(0.8f, 0.8f, 0.8f, 1f);
        }

        void ApplyBackground()
        {
            if (background == null) return;
            var c = _interactable ? _color : ButtonColor.Gray;
            var sp = UISprites.Get(DS.ButtonSprite(c));
            if (sp != null)
            {
                background.sprite = sp;
                background.color = UnityEngine.Color.white;
            }
            else
            {
                background.sprite = UISprites.ButtonPill;
                background.color = DS.ButtonTint(c);
            }
            var fit = background.GetComponent<SliceFit>();
            if (fit != null) fit.Apply();
        }

        Image AddRowIcon(string spriteName, float size, int siblingIndex)
        {
            var img = UIKit.Image(ContentRow, spriteName, new Vector2(size, size));
            if (img == null) return null;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = size;
            le.minHeight = le.preferredHeight = size;
            le.flexibleWidth = 0;
            if (siblingIndex >= 0) img.transform.SetSiblingIndex(siblingIndex);
            return img;
        }

        void Wire()
        {
            if (button == null) return;
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        static UIButton CreateRoot(Transform parent, string name, Vector2 size, out RectTransform visual)
        {
            var root = UIKit.Rect(name, parent);
            root.sizeDelta = size;
            var hit = root.gameObject.AddComponent<HitArea>();
            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var ui = root.gameObject.AddComponent<UIButton>();
            ui.button = btn;
            ui._height = size.y;

            visual = UIKit.Stretch(UIKit.Rect("Visual", root));
            ui.Visual = visual;
            ui._visualGroup = visual.gameObject.AddComponent<CanvasGroup>();

            var press = root.gameObject.AddComponent<Pressable>();
            press.target = visual;
            press.pressedScale = DS.Motion.PressScale;
            ui.Pressable = press;
            ui.Wire();
            return ui;
        }

        /// <summary>Builds a text button (see UIKit.Button).</summary>
        internal static UIButton Create(Transform parent, string text, ButtonColor color, Vector2 size, TextStyle style,
            Action onClick, string iconSprite)
        {
            if (parent == null) return null;
            var ui = CreateRoot(parent, "Button", size, out var visual);
            float h = size.y;

            var bg = UIKit.NewImage(visual, "Background", null, UnityEngine.Color.white);
            UIKit.Stretch(bg.rectTransform);
            ui.background = bg;
            SliceFit.Attach(bg, SliceFit.Mode.Height);

            // Content row, lifted a little so it sits on the button face above the 3D bottom lip.
            float padX = Mathf.Min(h * 0.32f, size.x * 0.12f);
            var row = UIKit.Stretch(UIKit.Rect("Content", visual), padX, 0, padX, 0);
            row.anchoredPosition = new Vector2(0, h * 0.035f);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = h * 0.08f;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            ui.ContentRow = row;

            ui._labelStyle = style;
            ui._labelSize = Mathf.Min(DS.Size(style), h * 0.46f);
            if (!string.IsNullOrEmpty(iconSprite)) ui.icon = ui.AddRowIcon(iconSprite, h * 0.6f, -1);

            var label = UIKit.Text(row, text ?? "", style, new Vector2(10, h * 0.8f));
            label.name = "Label";
            DS.Apply(label, style, ui._labelSize);
            ui.label = label;
            if (string.IsNullOrEmpty(text)) label.gameObject.SetActive(false);

            ui.SetColor(color);
            if (onClick != null) ui.OnClick += onClick;
            return ui;
        }

        /// <summary>Builds an icon-only button (see UIKit.IconButton).</summary>
        internal static UIButton CreateIcon(Transform parent, string spriteName, float size, Action onClick, string bgSprite)
        {
            if (parent == null) return null;
            var ui = CreateRoot(parent, "IconButton", new Vector2(size, size), out var visual);
            float inset = 0f;
            if (!string.IsNullOrEmpty(bgSprite))
            {
                var bgSp = UISprites.Get(bgSprite);
                var bg = UIKit.NewImage(visual, "Background", bgSp != null ? bgSp : UISprites.Rounded,
                    bgSp != null ? UnityEngine.Color.white : DS.Colors.Cream);
                UIKit.Stretch(bg.rectTransform);
                SliceFit.Attach(bg, SliceFit.Mode.Corner, size * 0.28f);
                ui.background = bg;
                ui._customBackground = true;
                inset = size * 0.17f;
            }
            var sp = UISprites.Get(spriteName);
            var icon = UIKit.NewImage(visual, "Icon", sp != null ? sp : UISprites.Circle,
                sp != null ? UnityEngine.Color.white : DS.Colors.BrandLight);
            icon.preserveAspect = true;
            UIKit.Stretch(icon.rectTransform, inset, inset * 0.8f, inset, inset * 1.2f);
            ui.icon = icon;
            if (onClick != null) ui.OnClick += onClick;
            return ui;
        }
    }
}
