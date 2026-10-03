// ============================================================================================================
// Base of the tab screens (Shop, Ranking, Collection, Profile, Worlds): themed full-bleed backdrop (the blurred potion
// workshop of the current world + tab tint + top/bottom shades + drifting sparkles), a big title ribbon under the top
// bar and a Body
// rect that spans the space between the ribbon and the bottom nav. Refresh plumbing: RequestRefresh() refreshes now
// when visible, otherwise on the next OnShow; SaveSystem.OnReplaced and safe-area changes are wired here.
// ============================================================================================================
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public abstract class TabScreen : UIScreen
    {
        /// <summary>Top bar height (DS) + breathing room; the chrome lives inside the safe area like the screens.</summary>
        public const float TopReserved = DS.Space.TopBarHeight;
        /// <summary>Bottom nav height (DS) + a small gap.</summary>
        public const float BottomReserved = DS.Space.BottomNavHeight + 12f;
        public const float RibbonWidth = 600f;
        /// <summary>Space between the bottom of the top bar and the top of the title ribbon.</summary>
        public const float RibbonGap = 4f;
        public const float MaxContentWidth = 1000f;

        /// <summary>Content area between the title ribbon and the bottom nav (stretched; full safe width).</summary>
        protected RectTransform Body;
        protected RectTransform TitleRibbon;
        Image _backdrop;
        AspectRatioFitter _backdropFit;
        string _backdropSprite;
        bool _dirty = true;
        bool _frameBuilt;

        /// <summary>Width for centered content (safe width minus margins, capped for tablets).</summary>
        protected float ContentWidth
        {
            get
            {
                var sm = ScreenManager.Instance;
                float safeW = sm != null ? sm.SafeSize.x : DS.Space.ReferenceWidth;
                return Mathf.Min(MaxContentWidth, safeW - DS.Space.ScreenMargin * 2f);
            }
        }

        /// <summary>Height of the Body rect in canvas units.</summary>
        protected float BodyHeight
        {
            get
            {
                var sm = ScreenManager.Instance;
                float safeH = sm != null ? sm.SafeSize.y : DS.Space.ReferenceHeight;
                return Mathf.Max(400f, safeH - BodyTop - BottomReserved);
            }
        }

        static float BodyTop => TopReserved + RibbonGap + UIKit.RibbonHeight(RibbonWidth) * 0.88f;   // clears the ribbon tails

        /// <summary>Builds backdrop, ribbon and Body. Call first in Build().</summary>
        protected void BuildFrame(string titleKey, Color tint)
        {
            _frameBuilt = true;
            _backdropSprite = CurrentBackdrop();
            _backdrop = UIKit.Backdrop(Root, _backdropSprite);
            if (_backdrop != null) _backdropFit = _backdrop.GetComponent<AspectRatioFitter>();

            // Tab tint over the blurred workshop (keeps each tab recognizable and text readable).
            var tintImg = UIKit.Backdrop(Root, "ui_pixel", DS.WithAlpha(tint, 0.38f));
            tintImg.transform.parent.SetSiblingIndex(1);

            // Shades: plum at the top (behind the top bar + ribbon), plum at the bottom (behind the nav).
            var shades = UIKit.FullBleed(UIKit.Rect("Shades", Root));
            shades.SetSiblingIndex(2);
            var grad = UISprites.Get("ui_gradient_v");
            var top = UIKit.NewImage(shades, "Top", grad, DS.WithAlpha(DS.Colors.BrandDark, 0.75f));
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 620f);
            top.rectTransform.anchoredPosition = Vector2.zero;
            var bottom = UIKit.NewImage(shades, "Bottom", grad, DS.WithAlpha(DS.Colors.Ink, 0.7f));
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 520f);
            bottom.rectTransform.anchoredPosition = new Vector2(0f, 260f);
            bottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);   // gradient flipped: opaque at the bottom
            if (grad == null)
            {
                top.color = DS.WithAlpha(DS.Colors.BrandDark, 0.35f);
                bottom.color = DS.WithAlpha(DS.Colors.Ink, 0.3f);
            }

            var floaters = AmbientFloaters.Create(Root, 16, Color.Lerp(tint, Color.white, 0.6f));
            floaters.transform.SetSiblingIndex(3);

            float ribbonH = UIKit.RibbonHeight(RibbonWidth);
            TitleRibbon = UIKit.Ribbon(Root, titleKey, RibbonWidth);
            UIKit.Place(TitleRibbon, new Vector2(0.5f, 1f), new Vector2(RibbonWidth, ribbonH), new Vector2(0f, -(TopReserved + RibbonGap)));

            Body = UIKit.Stretch(UIKit.Rect("Body", Root), 0f, BodyTop, 0f, BottomReserved);
            TitleRibbon.SetAsLastSibling();   // the ribbon tails overlap the body edge

            SaveSystem.OnReplaced += HandleSaveReplaced;
            var sm = ScreenManager.Instance;
            if (sm != null) sm.OnSafeAreaChanged += HandleSafeArea;
        }

        protected virtual void OnDestroy()
        {
            if (!_frameBuilt) return;
            SaveSystem.OnReplaced -= HandleSaveReplaced;
            var sm = ScreenManager.Instance;
            if (sm != null) sm.OnSafeAreaChanged -= HandleSafeArea;
        }

        static string CurrentBackdrop()
        {
            var area = Areas.AreaForLevel(Progress.CurrentLevel);
            return area != null ? area.GameBackground : "gamebg_forest";
        }

        /// <summary>Follows the current world (the backdrop changes every 20 levels).</summary>
        void UpdateBackdrop()
        {
            string want = CurrentBackdrop();
            if (want == _backdropSprite || _backdrop == null) return;
            var sp = UISprites.Get(want);
            if (sp == null) return;
            _backdropSprite = want;
            _backdrop.sprite = sp;
            if (_backdropFit != null && sp.rect.height > 0f) _backdropFit.aspectRatio = sp.rect.width / sp.rect.height;
        }

        public override void OnShow(object arg)
        {
            UpdateBackdrop();
            if (TitleRibbon != null)
            {
                Tween.Kill(TitleRibbon);
                CommonUI.PopIn(TitleRibbon, 0.04f);
                TitleRibbon.localRotation = Quaternion.Euler(0f, 0f, -4f);
                Tween.Rotate(TitleRibbon, 0f, 0.5f, Ease.OutBack).SetDelay(0.04f);
            }
            _dirty = false;
            Refresh(true);
        }

        /// <summary>Re-reads the model and updates the UI. animate = coming from OnShow (play reveal animations).</summary>
        protected abstract void Refresh(bool animate);

        /// <summary>Refresh now when visible, else on the next OnShow.</summary>
        protected void RequestRefresh()
        {
            if (this == null) return;
            if (IsVisible) Refresh(false);
            else _dirty = true;
        }

        /// <summary>True when a refresh was requested while hidden (OnShow refreshes anyway).</summary>
        protected bool IsDirty => _dirty;

        void HandleSaveReplaced()
        {
            if (this == null) return;
            UpdateBackdrop();
            OnSaveReplaced();
        }

        /// <summary>The whole save was replaced (cloud restore / reset). Default: RequestRefresh.</summary>
        protected virtual void OnSaveReplaced() => RequestRefresh();

        void HandleSafeArea()
        {
            if (this == null) return;
            OnLayoutChanged();
        }

        /// <summary>The safe area / resolution changed (anchored layout already followed). Default: nothing.</summary>
        protected virtual void OnLayoutChanged() { }
    }

    /// <summary>
    /// Slow ambient particles behind the tab content: sparkles and bubbles drifting upwards with a sine sway and a
    /// twinkle. Lives on its own nested canvas so moving them never rebatches the screen. No per-frame allocations.
    /// </summary>
    public sealed class AmbientFloaters : MonoBehaviour
    {
        struct Mote
        {
            public RectTransform rt;
            public Image img;
            public float x, y, speed, sway, phase, freq, alpha, twinkle, spin, rot;
        }

        Mote[] _motes;
        RectTransform _rt;
        float _time;
        Color _tint;

        public static AmbientFloaters Create(Transform parent, int count, Color tint)
        {
            var holder = UIKit.FullBleed(UIKit.Rect("Floaters", parent));
            holder.gameObject.AddComponent<Canvas>();
            var f = holder.gameObject.AddComponent<AmbientFloaters>();
            f._rt = holder;
            f._tint = tint;
            f._motes = new Mote[Mathf.Max(0, count)];
            string[] sprites = { "ui_sparkle_small", "ui_star_small", "ui_circle", "ui_sparkle_small" };
            for (int i = 0; i < f._motes.Length; i++)
            {
                var sp = UISprites.Get(sprites[i % sprites.Length]);
                if (sp == null) sp = UISprites.Circle;
                bool bubble = i % sprites.Length == 2;
                var img = UIKit.NewImage(holder, "Mote", sp, Color.white);
                float s = bubble ? Random.Range(18f, 42f) : Random.Range(26f, 60f);
                img.rectTransform.sizeDelta = new Vector2(s, s);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                f._motes[i] = new Mote
                {
                    rt = img.rectTransform,
                    img = img,
                    x = Random.Range(-0.5f, 0.5f),
                    y = Random.Range(-0.5f, 0.5f),
                    speed = Random.Range(0.012f, 0.035f),
                    sway = Random.Range(14f, 46f),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    freq = Random.Range(0.4f, 1.1f),
                    alpha = bubble ? Random.Range(0.12f, 0.25f) : Random.Range(0.35f, 0.8f),
                    twinkle = Random.Range(1.2f, 3f),
                    spin = bubble ? 0f : Random.Range(-40f, 40f),
                };
            }
            return f;
        }

        void Update()
        {
            if (_motes == null || _rt == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            Rect r = _rt.rect;
            for (int i = 0; i < _motes.Length; i++)
            {
                ref Mote m = ref _motes[i];
                if (m.rt == null) continue;
                m.y += m.speed * dt;
                if (m.y > 0.56f)
                {
                    m.y = -0.56f;
                    m.x = Random.Range(-0.5f, 0.5f);
                }
                m.rot += m.spin * dt;
                float px = m.x * r.width + Mathf.Sin(m.phase + _time * m.freq) * m.sway;
                float py = m.y * r.height;
                m.rt.anchoredPosition = new Vector2(px, py);
                m.rt.localRotation = Quaternion.Euler(0f, 0f, m.rot);
                float a = m.alpha * (0.55f + 0.45f * Mathf.Sin(m.phase * 2f + _time * m.twinkle));
                var c = _tint;
                c.a = a;
                m.img.color = c;
            }
        }
    }
}
