// ============================================================================================================
// Persistent top bar (inside the safe-area top, on ScreenManager.ChromeLayer): gold-ringed avatar (→ Profile),
// hearts pill (count drawn inside the heart, "Full" / mm:ss / unlimited timer; tap → OutOfLivesPopup), coins pill
// with "+" (→ Shop), stars pill (→ Ranking) and the settings gear. Counters count up on change; rewards shown by
// popups can be "held" so the counter only rises when the flying icons land (see Hold / Fly).
// ============================================================================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Top bar: avatar (→ Profile), hearts pill + timer, coins pill (+ → Shop), stars pill.</summary>
    public class TopBar : MonoBehaviour
    {
        public static TopBar Instance { get; protected set; }
        /// <summary>Fly-to targets for rewards.</summary>
        public RectTransform CoinsTarget, StarsTarget, HeartsTarget;

        // ---- additions
        public const float Height = DS.Space.TopBarHeight;
        public CounterPill Hearts, Coins, Stars;
        public UIButton AvatarButton, SettingsButton;
        /// <summary>True while the bar is shown (slid in).</summary>
        public bool IsShown => _shown;

        const float PillHearts = 230f, PillCoins = 292f, PillStars = 214f, PillGap = 12f;
        const float AvatarSize = 120f, GearSize = 104f;

        RectTransform _root, _shade, _row;
        CanvasGroup _group;
        TMP_Text _heartCount;
        Image _avatar;
        ScreenManager _sm;
        bool _shown = true;
        bool _dirty;
        float _tick;
        int _heldCoins, _heldHearts;
        long _heldStars;
        int _shownHeartCount = -1;
        bool _shownInfinite;
        string _shownAvatar;
        int _coinsFrame = -100, _starsFrame = -100, _heartsFrame = -100;
        Canvas _canvas;
        GraphicRaycaster _raycaster;
        int _raises;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        // ---------------------------------------------------------------------------------------- creation

        /// <summary>Builds the bar on `layer` (ScreenManager.ChromeLayer). Called by Chrome.Build.</summary>
        public static TopBar Create(RectTransform layer)
        {
            if (layer == null) return null;
            var root = UIKit.Rect("TopBar", layer);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, Height);
            root.anchoredPosition = Vector2.zero;
            var bar = root.gameObject.AddComponent<TopBar>();
            bar.Build(root);
            return bar;
        }

        void Build(RectTransform root)
        {
            Instance = this;
            _root = root;
            _sm = ScreenManager.Instance;
            _group = root.gameObject.AddComponent<CanvasGroup>();
            // Own nested canvas: lets the bar be lifted above the popup dim while rewards fly in (see Raise).
            _canvas = root.gameObject.AddComponent<Canvas>();
            if (_sm != null && _sm.Canvas != null) _canvas.additionalShaderChannels = _sm.Canvas.additionalShaderChannels;
            _raycaster = root.gameObject.AddComponent<GraphicRaycaster>();

            // Soft plum shade from the very top of the screen (under the notch) so counters read on any backdrop.
            var gradient = UISprites.Get("ui_gradient_v");
            var shade = UIKit.NewImage(root, "Shade", gradient, DS.WithAlpha(DS.Colors.Ink, gradient != null ? 0.55f : 0.25f));
            _shade = shade.rectTransform;
            LayoutShade();

            // Avatar in a circular gold ring.
            AvatarButton = UIKit.IconButton(root, PlayerProfile.AvatarSprite, AvatarSize, OnAvatar);
            var art = (RectTransform)AvatarButton.transform;
            UIKit.Place(art, new Vector2(0f, 0.5f), new Vector2(AvatarSize, AvatarSize), new Vector2(DS.Space.ScreenMargin, 0f));
            BuildAvatarFrame(AvatarButton);

            // Gear.
            SettingsButton = UIKit.IconButton(root, "icon_settings", GearSize, OnSettings);
            UIKit.Place((RectTransform)SettingsButton.transform, new Vector2(1f, 0.5f), new Vector2(GearSize, GearSize),
                new Vector2(-DS.Space.ScreenMargin + 6f, 0f));

            // Pills, centered between avatar and gear.
            var row = UIKit.HRow(root, new Vector2(PillHearts + PillCoins + PillStars + PillGap * 2f, 110f), PillGap);
            _row = (RectTransform)row.transform;
            float leftEdge = DS.Space.ScreenMargin + AvatarSize, rightEdge = DS.Space.ScreenMargin + GearSize - 6f;
            UIKit.Place(_row, new Vector2(0.5f, 0.5f), _row.sizeDelta, new Vector2((leftEdge - rightEdge) * 0.5f + 6f, 0f));

            Hearts = UIKit.CounterPill(_row, "icon_heart", PillHearts, null);
            MakeTappable(Hearts, OnHearts);
            HeartsTarget = Hearts.IconTarget;
            _heartCount = UIKit.Text(Hearts.Icon.rectTransform, "5", TextStyle.H3, new Vector2(80f, 60f));
            DS.Apply(_heartCount, TextStyle.H3, 44f);
            _heartCount.name = "HeartCount";
            UIKit.Stretch(_heartCount.rectTransform, 10f, 10f, 10f, 18f);
            Hearts.valueText.fontSizeMax = 44f;

            Coins = UIKit.CounterPill(_row, "icon_coin", PillCoins, OnShop);
            MakeTappable(Coins, OnShop);
            CoinsTarget = Coins.IconTarget;

            Stars = UIKit.CounterPill(_row, "icon_star", PillStars, null);
            MakeTappable(Stars, OnStars);
            StarsTarget = Stars.IconTarget;

            Economy.OnCoinsChanged += OnCoinsChanged;
            Economy.OnStarsChanged += OnStarsChanged;
            Lives.OnChanged += OnLivesChanged;
            PlayerProfile.OnChanged += RefreshAvatar;
            SaveSystem.OnReplaced += OnSaveReplaced;
            Loc.OnLanguageChanged += OnLanguageChanged;
            if (_sm != null) _sm.OnSafeAreaChanged += OnSafeAreaChanged;

            RefreshAll(false);
        }

        void OnDestroy()
        {
            Economy.OnCoinsChanged -= OnCoinsChanged;
            Economy.OnStarsChanged -= OnStarsChanged;
            Lives.OnChanged -= OnLivesChanged;
            PlayerProfile.OnChanged -= RefreshAvatar;
            SaveSystem.OnReplaced -= OnSaveReplaced;
            Loc.OnLanguageChanged -= OnLanguageChanged;
            if (_sm != null) _sm.OnSafeAreaChanged -= OnSafeAreaChanged;
            if (Instance == this) Instance = null;
        }

        void BuildAvatarFrame(UIButton b)
        {
            var visual = b.Visual;
            // Round contact shadow (a soft glow tinted dark; the 9-sliced shadow would read as a square).
            var shadow = UIKit.NewImage(visual, "Shadow", UISprites.Glow, new Color(0.1f, 0.03f, 0.2f, 0.55f));
            UIKit.Stretch(shadow.rectTransform, -14f, -4f, -14f, -26f);
            shadow.transform.SetSiblingIndex(0);
            var outer = UIKit.NewImage(visual, "RingOuter", UISprites.Circle, DS.Hex("C98A00"));
            UIKit.Stretch(outer.rectTransform);
            outer.transform.SetSiblingIndex(1);
            var ring = UIKit.NewImage(visual, "Ring", UISprites.Circle, DS.Hex("FFD23F"));
            UIKit.Stretch(ring.rectTransform, 5f, 4f, 5f, 6f);
            ring.transform.SetSiblingIndex(2);
            var fill = UIKit.NewImage(visual, "Fill", UISprites.Circle, DS.Hex("FFE9F4"));
            UIKit.Stretch(fill.rectTransform, 13f, 12f, 13f, 14f);
            fill.transform.SetSiblingIndex(3);
            var gloss = UIKit.NewImage(visual, "Gloss", UISprites.Circle, new Color(1f, 1f, 1f, 0.35f));
            UIKit.Stretch(gloss.rectTransform, 30f, 9f, 30f, 72f);
            _avatar = b.icon;
            if (_avatar != null)
            {
                UIKit.Stretch(_avatar.rectTransform, 17f, 15f, 17f, 19f);
                _avatar.transform.SetAsLastSibling();
            }
            gloss.transform.SetAsLastSibling();
        }

        static void MakeTappable(CounterPill pill, Action onTap)
        {
            if (pill == null) return;
            var go = pill.gameObject;
            var hit = go.AddComponent<HitArea>();
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            float last = -10f;
            btn.onClick.AddListener(() =>
            {
                if (Time.unscaledTime - last < 0.25f) return;
                last = Time.unscaledTime;
                onTap?.Invoke();
            });
            var press = go.AddComponent<Pressable>();
            press.pressedScale = 0.94f;
            // The "+" button keeps its own hit area/sound (it sits above the pill's HitArea in the hierarchy).
            if (pill.PlusButton != null) pill.PlusButton.transform.SetAsLastSibling();
        }

        // ---------------------------------------------------------------------------------------- visibility

        /// <summary>Slides the bar in/out (and blocks/unblocks its input).</summary>
        public void SetVisible(bool visible, bool animate)
        {
            _shown = visible;
            if (_root == null) return;
            Tween.Kill(_root);
            Tween.Kill(_group);
            float inset = _sm != null ? _sm.SafeInsets.w : 0f;
            var target = visible ? Vector2.zero : new Vector2(0f, Height + inset + 80f);
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
            if (!animate)
            {
                _root.anchoredPosition = target;
                _group.alpha = visible ? 1f : 0f;
                return;
            }
            Tween.Move(_root, target, visible ? 0.4f : 0.25f, visible ? Ease.OutBack : Ease.InCubic);
            Tween.Fade(_group, visible ? 1f : 0f, visible ? 0.2f : 0.25f);
            if (visible) RefreshAll(false);
        }

        // ---------------------------------------------------------------------------------------- refresh

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshHearts();
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            RefreshCounters(true);
        }

        void RefreshAll(bool animate)
        {
            RefreshCounters(animate);
            RefreshHearts();
            RefreshAvatar();
        }

        void RefreshCounters(bool animate)
        {
            if (Coins != null) Coins.SetValue(Math.Max(0L, (long)Economy.Coins - _heldCoins), animate);
            if (Stars != null) Stars.SetValue(Math.Max(0L, Economy.TotalStars - _heldStars), animate);
        }

        void RefreshHearts()
        {
            if (Hearts == null) return;
            bool infinite = Lives.HasInfinite;
            int hearts = Lives.Hearts;
            int shown = Mathf.Max(0, hearts - _heldHearts);
            if (_heartCount != null && (shown != _shownHeartCount || infinite != _shownInfinite))
            {
                _shownHeartCount = shown;
                _shownInfinite = infinite;
                _heartCount.text = infinite ? "∞" : shown.ToString();
            }
            string value;
            if (infinite) value = TimeUtil.FormatDuration(Lives.InfiniteRemainingSeconds);
            else if (shown >= Lives.Max) value = Loc.T("hearts.full");
            else value = TimeUtil.FormatMMSS(Lives.SecondsToNext);
            if (Hearts.valueText == null || Hearts.valueText.text != value) Hearts.SetText(value);
        }

        void RefreshAvatar()
        {
            string sprite = PlayerProfile.AvatarSprite;
            if (_avatar == null || sprite == _shownAvatar) return;
            _shownAvatar = sprite;
            var sp = UISprites.Get(sprite);
            _avatar.sprite = sp != null ? sp : UISprites.Circle;
            _avatar.color = sp != null ? Color.white : DS.Colors.BrandLight;
            if (_root != null && _root.gameObject.activeInHierarchy) Tween.Punch(_avatar.transform, 0.2f, 0.35f);
        }

        void OnCoinsChanged(int oldValue, int newValue)
        {
            _dirty = true;
            _coinsFrame = Time.frameCount;
        }

        void OnStarsChanged(long oldValue, long newValue)
        {
            _dirty = true;
            _starsFrame = Time.frameCount;
        }

        void OnLivesChanged()
        {
            _heartsFrame = Time.frameCount;
            RefreshHearts();
        }
        void OnLanguageChanged()
        {
            RefreshHearts();
            RefreshCounters(false);   // number separators differ per language
        }

        void OnSaveReplaced()
        {
            ClearHolds();
            _shownAvatar = null;
            RefreshAll(false);
        }

        void OnSafeAreaChanged()
        {
            LayoutShade();
            if (!_shown) SetVisible(false, false);
        }

        void LayoutShade()
        {
            if (_shade == null) return;
            Vector4 i = _sm != null ? _sm.SafeInsets : Vector4.zero;
            _shade.anchorMin = new Vector2(0f, 1f);
            _shade.anchorMax = new Vector2(1f, 1f);
            _shade.pivot = new Vector2(0.5f, 1f);
            _shade.offsetMin = new Vector2(-i.x - 8f, -(Height + 70f));
            _shade.offsetMax = new Vector2(i.z + 8f, i.w + 8f);
        }

        // ---------------------------------------------------------------------------------------- holds

        /// <summary>
        /// Hides already-granted coins/stars/hearts from the counters until they are released (by Fly when the icons
        /// land, by Release, or instantly with Unhold). Call right after granting (same frame) to avoid a count-up.
        /// </summary>
        public void Hold(Reward reward) { AddHold(reward, 1); RefreshNow(); }

        public void Hold(IList<Reward> rewards)
        {
            if (rewards == null) return;
            for (int i = 0; i < rewards.Count; i++) AddHold(rewards[i], 1);
            RefreshNow();
        }

        /// <summary>
        /// Holds only the rewards whose counter changed in the last couple of frames (i.e. granted right before this
        /// call), so a popup opened long after the grant never makes a counter jump back down. Returns true if
        /// anything was held.
        /// </summary>
        public bool HoldIfFresh(IList<Reward> rewards)
        {
            if (rewards == null) return false;
            int now = Time.frameCount;
            bool any = false;
            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                int frame;
                switch (r.type)
                {
                    case RewardType.Coins: frame = _coinsFrame; break;
                    case RewardType.Stars: frame = _starsFrame; break;
                    case RewardType.Hearts: frame = _heartsFrame; break;
                    default: continue;
                }
                if (now - frame > 2) continue;
                AddHold(r, 1);
                any = true;
            }
            if (any) RefreshNow();
            return any;
        }

        /// <summary>Gives back held amounts without animation (e.g. before another popup holds them again).</summary>
        public void Unhold(Reward reward) { AddHold(reward, -1); RefreshNow(); }

        public void Unhold(IList<Reward> rewards)
        {
            if (rewards == null) return;
            for (int i = 0; i < rewards.Count; i++) AddHold(rewards[i], -1);
            RefreshNow();
        }

        /// <summary>Releases a held amount with a count-up and a bump of the pill.</summary>
        public void Release(RewardType type, int amount)
        {
            if (amount <= 0) return;
            AddHold(new Reward { type = type, amount = amount }, -1);
            RefreshCounters(true);
            RefreshHearts();
            switch (type)
            {
                case RewardType.Coins: if (Coins != null) Coins.Bump(); break;
                case RewardType.Stars: if (Stars != null) Stars.Bump(); break;
                case RewardType.Hearts:
                case RewardType.InfiniteHeartsMinutes: if (Hearts != null) Hearts.Bump(); break;
            }
        }

        /// <summary>Drops every hold (counters jump to the real balances).</summary>
        public void ClearHolds()
        {
            _heldCoins = 0;
            _heldStars = 0;
            _heldHearts = 0;
            RefreshNow();
        }

        void AddHold(Reward r, int sign)
        {
            if (r.amount <= 0) return;
            switch (r.type)
            {
                case RewardType.Coins: _heldCoins = Mathf.Max(0, _heldCoins + sign * r.amount); break;
                case RewardType.Stars: _heldStars = Math.Max(0L, _heldStars + sign * (long)r.amount); break;
                case RewardType.Hearts: _heldHearts = Mathf.Max(0, _heldHearts + sign * r.amount); break;
            }
        }

        void RefreshNow()
        {
            _dirty = false;
            RefreshCounters(false);
            RefreshHearts();
        }

        /// <summary>Sprite and fly target of a reward on the top bar (null target = nothing to fly to).</summary>
        public RectTransform TargetFor(RewardType type)
        {
            switch (type)
            {
                case RewardType.Coins: return CoinsTarget;
                case RewardType.Stars: return StarsTarget;
                case RewardType.Hearts:
                case RewardType.InfiniteHeartsMinutes: return HeartsTarget;
                default: return null;
            }
        }

        // ---------------------------------------------------------------------------------------- raise

        /// <summary>
        /// Lifts the bar above popups (and their dim overlay) so counters read clearly while rewards land; input on
        /// the bar is off meanwhile. Balanced by Lower (nested calls are counted).
        /// </summary>
        public void Raise()
        {
            _raises++;
            if (_canvas == null) return;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 5;
            if (_raycaster != null) _raycaster.enabled = false;
        }

        public void Lower()
        {
            _raises = Mathf.Max(0, _raises - 1);
            if (_raises > 0 || _canvas == null) return;
            _canvas.overrideSorting = false;
            _canvas.sortingOrder = 0;
            if (_raycaster != null) _raycaster.enabled = true;
        }

        // ---------------------------------------------------------------------------------------- flights

        sealed class Release_
        {
            public RewardType type;
            public int amount, icons;
            public bool release;

            public void Arrive()
            {
                int chunk = icons <= 1 ? amount : amount / icons;
                icons--;
                amount -= chunk;
                var bar = Instance;
                if (release && bar != null) bar.Release(type, chunk);
            }
        }

        /// <summary>
        /// Flies the icons of already-granted rewards from a world position to their top-bar pills (coins, stars,
        /// hearts). With releaseHolds the held amounts are released piece by piece as the icons land (count-up).
        /// Boosters/cards have no counter and are skipped. onAllArrived fires once everything landed.
        /// </summary>
        public static void Fly(IList<Reward> rewards, Vector3 fromWorld, Action onAllArrived = null, bool releaseHolds = true)
        {
            var bar = Instance;
            int flights = 0;
            // Hidden bar (e.g. on the game screen): its pills are off-screen, so nothing flies — the holds are released
            // right away instead of icons vanishing towards a counter nobody can see.
            if (rewards != null && bar != null && bar.IsShown)
                for (int i = 0; i < rewards.Count; i++)
                    if (bar.TargetFor(rewards[i].type) != null && rewards[i].amount > 0) flights++;
            if (flights == 0)
            {
                if (bar != null && releaseHolds && rewards != null)
                    for (int i = 0; i < rewards.Count; i++) bar.Release(rewards[i].type, rewards[i].amount);
                onAllArrived?.Invoke();
                return;
            }
            int remaining = flights;
            bar.Raise();
            Action one = () =>
            {
                remaining--;
                if (remaining != 0) return;
                Tween.Delay(0.35f, () => { if (bar != null) bar.Lower(); }).SetLink(bar);
                onAllArrived?.Invoke();
            };
            float delay = 0f;
            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                var target = bar.TargetFor(r.type);
                if (target == null || r.amount <= 0) continue;
                int icons;
                string sprite;
                switch (r.type)
                {
                    case RewardType.Coins: icons = Mathf.Clamp(r.amount / 10, 4, 14); sprite = "icon_coin"; break;
                    case RewardType.Stars: icons = Mathf.Clamp(r.amount / 10, 4, 14); sprite = "icon_star"; break;
                    case RewardType.Hearts: icons = Mathf.Clamp(r.amount, 1, 5); sprite = "icon_heart"; break;
                    default: icons = 3; sprite = "icon_heart"; break;
                }
                var rel = new Release_
                {
                    type = r.type,
                    amount = r.amount,
                    icons = icons,
                    release = releaseHolds && r.type != RewardType.InfiniteHeartsMinutes,
                };
                Vector3 from = fromWorld;
                Action start = () => FX.FlyRewards(sprite, icons, from, target, rel.Arrive, one);
                if (delay <= 0f) start();
                else Tween.Delay(delay, start).SetLink(bar);
                delay += 0.18f;
            }
        }

        // ---------------------------------------------------------------------------------------- taps

        void OnAvatar() => ShowTab(ScreenId.Profile);
        void OnShop() => ShowTab(ScreenId.Shop);
        void OnStars() => ShowTab(ScreenId.Leaderboard);

        void OnSettings()
        {
            if (SettingsButton != null && SettingsButton.icon != null)
            {
                var t = SettingsButton.icon.transform;
                Tween.Kill(t);
                t.localEulerAngles = Vector3.zero;
                Tween.Rotate(t, -180f, 0.45f, Ease.OutBack).OnComplete(() => { if (t != null) t.localEulerAngles = Vector3.zero; });
            }
            SettingsPopup.Open();
        }

        void OnHearts()
        {
            if (Lives.HasInfinite || Lives.IsFull)
            {
                if (Hearts != null) Hearts.Bump();
                Haptics.Play(HapticType.Light);
                UIKit.Toast(Loc.T(Lives.HasInfinite ? "home.lives_infinite" : "home.lives_full"));
                return;
            }
            OutOfLivesPopup.Open();
        }

        static void ShowTab(ScreenId id)
        {
            var sm = ScreenManager.Instance;
            if (sm == null) return;
            if (sm.Current == id && sm.CurrentScreen != null && sm.CurrentScreen.IsVisible) return;
            sm.Show(id);
        }
    }
}
