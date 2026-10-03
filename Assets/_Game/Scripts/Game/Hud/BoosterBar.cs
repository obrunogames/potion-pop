// ============================================================================================================
// Booster bar (GDD §5): four btn_square tiles (Undo, Shuffle, Extra Bottle, Magic Wand) on a soft tray. Each tile
// shows the booster icon and a count badge, a green "+" when empty (→ buy), or a padlock + "Level N" while locked.
// Tiles dim while they can't do anything right now (nothing to undo, extra-bottle cap reached) but stay tappable so the
// session can explain why. They bounce when used, shake when refused and glow when the session highlights one (dead-end
// tip, first-use tutorial). Refreshes on Economy / save / level changes.
// ============================================================================================================
using System;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game
{
    public sealed class BoosterBar : MonoBehaviour
    {
        public const float Height = DS.Space.BoosterBarHeight;
        const float TileSize = 176f;
        const float TrayWidth = 1000f, TrayHeight = 214f;

        sealed class Tile
        {
            public BoosterType type;
            public UIButton button;
            public RectTransform rect;
            public Image icon;
            public RectTransform countBadge;
            public TMP_Text countText;
            public Image plus;
            public RectTransform lockGroup;
            public Image glow;
            public TweenHandle glowPulse;
            public CanvasGroup visualGroup;
            public int shownCount = -1;
            public bool shownLocked;
            public bool usable = true;
        }

        public RectTransform Root { get; private set; }
        public event Action<BoosterType> OnTileTapped;

        readonly Tile[] _tiles = new Tile[4];
        RectTransform _tray;
        bool _subscribed;
        BoosterType? _highlighted;
        TweenHandle _highlightTimer;

        public static BoosterBar Create(RectTransform parent)
        {
            var root = UIKit.Rect("BoosterBar", parent);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(0f, Height);
            root.anchoredPosition = Vector2.zero;
            // Own canvas (tile glows / pulses rebatch only the bar); raycaster required: the tiles are buttons.
            GameUI.NestCanvas(root.gameObject, true);
            var bar = root.gameObject.AddComponent<BoosterBar>();
            bar.Root = root;
            bar.Build();
            return bar;
        }

        void Build()
        {
            _tray = UIKit.Rect("Tray", Root);
            GameUI.PlaceCenter(_tray, new Vector2(0f, 4f), new Vector2(TrayWidth, TrayHeight));
            var trayShadow = UIKit.Shadow(_tray, 30f, -10f, 0.22f);
            trayShadow.name = "TrayShadow";
            var tray = UIKit.RoundedRect(_tray, new Vector2(TrayWidth, TrayHeight), DS.WithAlpha(DS.Colors.BrandDark, 0.55f), 56f);
            UIKit.Stretch(tray.rectTransform);
            var rim = UIKit.RoundedRect(tray.rectTransform, Vector2.zero, DS.WithAlpha(Color.white, 0.14f), 48f);
            UIKit.Stretch(rim.rectTransform, 8f, 8f, 8f, TrayHeight * 0.5f);

            var types = Economy.InGameBoosters;
            int n = Mathf.Min(types.Length, _tiles.Length);
            float step = TrayWidth / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
                _tiles[i] = BuildTile(types[i], -TrayWidth * 0.5f + step * (i + 0.5f));
            Subscribe();
            Refresh(false);
        }

        Tile BuildTile(BoosterType type, float x)
        {
            var t = new Tile { type = type };
            var holder = UIKit.Rect(type.ToString(), _tray);
            GameUI.PlaceCenter(holder, new Vector2(x, 6f), new Vector2(TileSize, TileSize));
            t.rect = holder;

            t.glow = GameUI.Glow(holder, TileSize * 1.7f, DS.WithAlpha(DS.Colors.Accent, 0f));
            GameUI.PlaceCenter(t.glow.rectTransform, Vector2.zero, new Vector2(TileSize * 1.7f, TileSize * 1.7f));

            var btn = UIKit.IconButton(holder, Economy.BoosterSprite(type), TileSize, () => Tap(type), "btn_square");
            UIKit.Stretch((RectTransform)btn.transform);
            btn.Pressable.playSound = false;   // the session plays the right sound (use / refuse / buy)
            t.button = btn;
            t.icon = btn.icon;

            var visual = btn.Visual != null ? btn.Visual : (RectTransform)btn.transform;
            t.visualGroup = visual.GetComponent<CanvasGroup>();

            // count badge (top-right)
            t.countBadge = GameUI.CountBadge(visual, 0, 66f, new Vector2(1f, 1f), new Vector2(14f, 14f), out t.countText);

            // "+" (empty → buy)
            t.plus = UIKit.Image(visual, "icon_plus", new Vector2(70f, 70f));
            UIKit.Place(t.plus.rectTransform, new Vector2(1f, 1f), new Vector2(70f, 70f), new Vector2(16f, 16f));

            // lock overlay
            t.lockGroup = UIKit.Stretch(UIKit.Rect("Lock", visual));
            var dim = UIKit.RoundedRect(t.lockGroup, Vector2.zero, DS.WithAlpha(DS.Colors.Ink, 0.35f), 40f);
            UIKit.Stretch(dim.rectTransform, 10f, 10f, 10f, 18f);
            var padlock = UIKit.Image(t.lockGroup, "icon_lock", new Vector2(86f, 86f));
            GameUI.PlaceCenter(padlock.rectTransform, new Vector2(0f, 14f), new Vector2(86f, 86f));
            var plate = UIKit.Capsule(t.lockGroup, new Vector2(150f, 50f), DS.WithAlpha(DS.Colors.Ink, 0.85f));
            UIKit.Place(plate.rectTransform, new Vector2(0.5f, 0f), new Vector2(150f, 50f), new Vector2(0f, -14f));
            var lockText = UIKit.LocText(plate.rectTransform, "booster.unlock_at", TextStyle.Badge, new Vector2(140f, 46f),
                Economy.BoosterUnlockLevel(type));
            UIKit.Stretch(lockText.rectTransform, 10f, 2f, 10f, 4f);
            return t;
        }

        void Tap(BoosterType type) => OnTileTapped?.Invoke(type);

        // ---------------------------------------------------------------------------------------- state

        /// <summary>Re-reads counts and unlock state (animates changes when `animate`).</summary>
        public void Refresh(bool animate = true)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                var t = _tiles[i];
                if (t == null || t.button == null) continue;
                bool locked = !Economy.IsBoosterUnlocked(t.type);
                int count = Economy.GetBooster(t.type);
                bool lockChanged = locked != t.shownLocked || t.shownCount < 0;
                bool countChanged = count != t.shownCount;
                t.shownLocked = locked;
                t.shownCount = count;

                if (t.lockGroup != null) t.lockGroup.gameObject.SetActive(locked);
                bool hasCount = !locked && count > 0;
                if (t.countBadge != null) t.countBadge.gameObject.SetActive(hasCount);
                if (t.plus != null) t.plus.gameObject.SetActive(!locked && count <= 0);
                if (t.countText != null) t.countText.text = count > 99 ? "99+" : count.ToString();
                ApplyLook(t);

                if (!animate) continue;
                if (countChanged && hasCount && t.countBadge != null) Tween.Punch(t.countBadge, 0.35f, 0.35f);
                if (countChanged && !hasCount && !locked && t.plus != null) GameUI.PopInDelayed(t.plus.transform, 0f);
                if (lockChanged && !locked) Tween.Punch(t.rect, 0.2f, 0.4f);
            }
        }

        /// <summary>Dims a tile while it can't do anything right now (it stays tappable: the session explains why).</summary>
        public void SetUsable(BoosterType type, bool usable)
        {
            var t = Find(type);
            if (t == null || t.usable == usable) return;
            t.usable = usable;
            ApplyLook(t);
        }

        public bool IsUsable(BoosterType type)
        {
            var t = Find(type);
            return t != null && t.usable;
        }

        static void ApplyLook(Tile t)
        {
            bool locked = t.shownLocked;
            if (t.icon != null) t.icon.color = locked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            if (t.visualGroup != null) t.visualGroup.alpha = locked || t.usable ? 1f : 0.55f;
        }

        public RectTransform TileRect(BoosterType type)
        {
            var t = Find(type);
            return t != null ? t.rect : null;
        }

        /// <summary>World position of a tile's center (tutorial hand, wand orbs).</summary>
        public Vector3 TileWorldCenter(BoosterType type)
        {
            var t = Find(type);
            if (t == null || t.rect == null) return transform.position;
            return t.rect.TransformPoint(t.rect.rect.center);
        }

        /// <summary>Booster used: squash-and-stretch bounce + burst.</summary>
        public void Bounce(BoosterType type)
        {
            var t = Find(type);
            if (t == null || t.rect == null) return;
            Tween.Punch(t.rect, 0.3f, 0.45f);
            FX.Burst(null, FX.LocalCenterOf(t.rect), "ui_star_small", 10, DS.Colors.Gold, 520f, 0.55f, 34f, -900f);
        }

        /// <summary>Refused tap (locked, nothing to do, busy): horizontal shake.</summary>
        public void Shake(BoosterType type)
        {
            var t = Find(type);
            if (t == null || t.button == null || t.button.Visual == null) return;
            Tween.Shake(t.button.Visual, 14f, 0.3f);
        }

        /// <summary>Pulsing golden glow behind a tile; null clears it. seconds &gt; 0 clears it automatically.</summary>
        public void SetHighlight(BoosterType? type, float seconds = 0f)
        {
            _highlightTimer?.Kill();
            _highlightTimer = null;
            _highlighted = type;
            for (int i = 0; i < _tiles.Length; i++)
            {
                var t = _tiles[i];
                if (t == null || t.glow == null) continue;
                bool on = type.HasValue && t.type == type.Value;
                t.glowPulse?.Kill();
                t.glowPulse = null;
                Tween.Kill(t.glow);
                if (on)
                {
                    t.glow.color = DS.WithAlpha(DS.Colors.Accent, 0.25f);
                    t.glowPulse = Tween.Fade(t.glow, 0.9f, 0.45f, Ease.InOutSine).SetLoops(-1, true);
                    Tween.Kill(t.rect);
                    t.rect.localScale = Vector3.one;
                    Tween.Pulse(t.rect, 1.08f, 0.8f);
                }
                else
                {
                    Tween.Fade(t.glow, 0f, DS.Motion.Base);
                    Tween.Kill(t.rect);
                    t.rect.localScale = Vector3.one;
                }
            }
            if (type.HasValue && seconds > 0f)
            {
                var which = type.Value;
                _highlightTimer = Tween.Delay(seconds, () =>
                {
                    if (_highlighted.HasValue && _highlighted.Value == which) SetHighlight(null);
                }).SetLink(this);
            }
        }

        /// <summary>Hides the tray and tiles until PopIn (level loading / screen fade).</summary>
        public void HideForIntro()
        {
            if (_tray != null)
            {
                var g = _tray.GetComponent<CanvasGroup>();
                if (g == null) g = _tray.gameObject.AddComponent<CanvasGroup>();
                Tween.Kill(g);
                g.alpha = 0f;
            }
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == null || _tiles[i].rect == null) continue;
                Tween.Kill(_tiles[i].rect);
                _tiles[i].rect.localScale = Vector3.zero;
            }
        }

        /// <summary>Staggered entrance (level start).</summary>
        public void PopIn()
        {
            if (_tray != null) GameUI.RiseIn(_tray, 0.05f, 80f);
            for (int i = 0; i < _tiles.Length; i++)
                if (_tiles[i] != null) GameUI.PopInDelayed(_tiles[i].rect, 0.18f + i * DS.Motion.Stagger * 1.4f);
        }

        Tile Find(BoosterType type)
        {
            for (int i = 0; i < _tiles.Length; i++)
                if (_tiles[i] != null && _tiles[i].type == type) return _tiles[i];
            return null;
        }

        // ---------------------------------------------------------------------------------------- events

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            Economy.OnBoosterChanged += OnBoosterChanged;
            SaveSystem.OnReplaced += OnSaveReplaced;
            Progress.OnLevelChanged += OnLevelChanged;
        }

        void OnBoosterChanged(BoosterType type, int count) => Refresh(true);
        void OnSaveReplaced() => Refresh(false);
        void OnLevelChanged(int level) => Refresh(true);

        void OnEnable() => Refresh(false);

        void OnDestroy()
        {
            if (!_subscribed) return;
            _subscribed = false;
            Economy.OnBoosterChanged -= OnBoosterChanged;
            SaveSystem.OnReplaced -= OnSaveReplaced;
            Progress.OnLevelChanged -= OnLevelChanged;
        }
    }
}
