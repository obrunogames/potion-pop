// ============================================================================================================
// Home: the store facade of the current area (slow breathing zoom, drifting clouds), Mimi beside the entrance,
// the hanging area sign with progress, side event buttons (left: Star Chest, Daily Reward, Quests; right: Lucky
// Spin, Weekly Ranking), the big pulsing LEVEL button with the win-streak badge, and the queue of automatic
// popups (new area, daily reward, login nudge) shown one at a time.
// ============================================================================================================
using PotionPop.Levels;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class HomeScreen : UIScreen
    {
        public override ScreenId Id => ScreenId.Home;

        // ---- additions
        public static HomeScreen Instance { get; private set; }
        /// <summary>Level from which the login nudge popup / Profile badge appear.</summary>
        public const int LoginNudgeLevel = 10;

        const float SignWidth = 660f;
        const float ColumnGap = 10f;
        const float LevelButtonBottom = DS.Space.BottomNavHeight + 46f;
        static readonly Vector2 LevelButtonSize = new Vector2(620f, 176f);
        static readonly Vector2 MimiAnchor = new Vector2(0.70f, 0.275f);   // feet, in backdrop-image space

        Image _backdrop;
        RectTransform _backdropHolder;
        AspectRatioFitter _backdropFit;
        string _backdropSprite;
        RectTransform _mimiSpot;
        HomeMascot _mimi;

        RectTransform _sign, _signSwing;
        TMP_Text _areaName;
        ProgressBar _areaBar;

        HomeSideButton _chest, _daily, _quests, _spin, _rank;
        HomeSideButton[] _sideButtons;

        RectTransform _levelHolder;
        UIButton _levelButton;
        Image _levelGlow;
        RectTransform _streak;
        TMP_Text _streakText;
        Image _flame;

        bool _dirty;
        float _tick, _attention, _sparkle;
        bool _autoPending;
        float _autoAt;
        bool _spinFast;
        ScreenManager _sm;

        static int _dailyAutoDay = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            _dailyAutoDay = -1;
        }

        // ---------------------------------------------------------------------------------------- build

        public override void Build()
        {
            Instance = this;
            _sm = ScreenManager.Instance;
            BuildBackdrop();
            BuildSign();
            BuildSideButtons();
            BuildLevelButton();
            Layout();

            SaveSystem.OnReplaced += MarkDirty;
            Progress.OnLevelChanged += OnLevelChanged;
            StarChest.OnChanged += MarkDirty;
            DailyRewards.OnChanged += MarkDirty;
            Quests.OnChanged += MarkDirty;
            LuckySpin.OnChanged += MarkDirty;
            Economy.OnStarsChanged += OnStarsChanged;
            Loc.OnLanguageChanged += MarkDirty;
            if (_sm != null) _sm.OnSafeAreaChanged += Layout;
            Refresh(false);
        }

        void OnDestroy()
        {
            SaveSystem.OnReplaced -= MarkDirty;
            Progress.OnLevelChanged -= OnLevelChanged;
            StarChest.OnChanged -= MarkDirty;
            DailyRewards.OnChanged -= MarkDirty;
            Quests.OnChanged -= MarkDirty;
            LuckySpin.OnChanged -= MarkDirty;
            Economy.OnStarsChanged -= OnStarsChanged;
            Loc.OnLanguageChanged -= MarkDirty;
            if (_sm != null) _sm.OnSafeAreaChanged -= Layout;
            if (Instance == this) Instance = null;
        }

        void BuildBackdrop()
        {
            _backdropSprite = CurrentBackdrop();
            _backdrop = UIKit.Backdrop(Root, _backdropSprite);
            _backdropHolder = (RectTransform)_backdrop.transform.parent;
            _backdropFit = _backdrop.GetComponent<AspectRatioFitter>();

            // Mimi stands on the sidewalk beside the entrance: placed in the image's own space so she stays there
            // whatever the aspect ratio (the envelope fit crops the image differently on every phone).
            _mimiSpot = UIKit.Rect("MimiSpot", _backdrop.rectTransform);
            _mimiSpot.anchorMin = _mimiSpot.anchorMax = MimiAnchor;
            _mimiSpot.pivot = new Vector2(0.5f, 0f);
            _mimiSpot.sizeDelta = new Vector2(10f, 10f);
            _mimiSpot.anchoredPosition = Vector2.zero;
            _mimi = HomeMascot.Create(_mimiSpot, MimiSize());

            HomeClouds.Create(_backdropHolder, 4, 0.13f, 0.25f);

            // Soft plum shade at the bottom so the LEVEL button and the nav pop over the street.
            var gradient = UISprites.Get("ui_gradient_v");
            if (gradient != null)
            {
                var shade = UIKit.NewImage(_backdropHolder, "BottomShade", gradient, DS.WithAlpha(DS.Colors.Ink, 0.38f));
                var rt = shade.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0.3f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.localEulerAngles = new Vector3(0f, 0f, 180f);
            }
        }

        void BuildSign()
        {
            float h = SignWidth * 372f / 768f;
            var info = Art.Info("store_sign");
            if (info != null && info.width > 0) h = SignWidth * info.height / (float)info.width;
            _sign = UIKit.Rect("AreaSign", Root);
            UIKit.Place(_sign, new Vector2(0.5f, 1f), new Vector2(SignWidth, h), new Vector2(0f, -(TopBar.Height - 4f)));

            _signSwing = UIKit.Rect("Swing", _sign);
            UIKit.Stretch(_signSwing);
            _signSwing.pivot = new Vector2(0.5f, 1f);
            _signSwing.anchoredPosition = Vector2.zero;
            var hit = _signSwing.gameObject.AddComponent<HitArea>();
            var btn = _signSwing.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(OnSignTapped);

            var img = UIKit.Image(_signSwing, "store_sign", new Vector2(SignWidth, h));
            UIKit.Stretch(img.rectTransform);

            // Cream inner panel of the sign (art_index "inner", origin bottom-left).
            Rect inner = new Rect(0.0951f, 0.1398f, 0.8099f, 0.3414f);
            if (info != null && info.hasInner && info.inner.width > 0.1f) inner = info.inner;
            var panel = UIKit.Rect("Inner", _signSwing);
            panel.anchorMin = new Vector2(inner.xMin, inner.yMin);
            panel.anchorMax = new Vector2(inner.xMax, inner.yMax);
            panel.offsetMin = new Vector2(10f, 4f);
            panel.offsetMax = new Vector2(-10f, -4f);

            _areaName = UIKit.LocText(panel, "area.grocery", TextStyle.Body, new Vector2(400f, 50f));
            DS.Apply(_areaName, TextStyle.Body, 46f);
            _areaName.textWrappingMode = TextWrappingModes.NoWrap;
            var nrt = _areaName.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0.5f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.offsetMin = new Vector2(8f, 0f);
            nrt.offsetMax = new Vector2(-8f, 2f);

            _areaBar = UIKit.ProgressBar(panel, new Vector2(400f, 40f));
            var brt = (RectTransform)_areaBar.transform;
            brt.anchorMin = new Vector2(0.12f, 0.08f);
            brt.anchorMax = new Vector2(0.88f, 0.08f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(0f, 40f);
            brt.anchoredPosition = Vector2.zero;
            _areaBar.label.gameObject.SetActive(true);
            DS.Apply(_areaBar.label, TextStyle.Badge, 28f);
        }

        void BuildSideButtons()
        {
            _chest = HomeSideButton.Create(Root, "chest_closed", OnChest, true);
            _daily = HomeSideButton.Create(Root, "icon_calendar", OnDaily);
            _quests = HomeSideButton.Create(Root, "icon_quest", OnQuests);
            _spin = HomeSideButton.Create(Root, "icon_wheel", OnSpin);
            _rank = HomeSideButton.Create(Root, "icon_trophy", OnRank);
            _quests.SetCaptionKey("home.side.quests");
            _sideButtons = new[] { _chest, _daily, _quests, _spin, _rank };
        }

        void BuildLevelButton()
        {
            _levelHolder = UIKit.Rect("LevelButton", Root);
            UIKit.Place(_levelHolder, new Vector2(0.5f, 0f), LevelButtonSize, new Vector2(0f, LevelButtonBottom));

            _levelGlow = UIKit.NewImage(_levelHolder, "Glow", UISprites.Glow, new Color(1f, 0.95f, 0.6f, 0.55f));
            UIKit.Stretch(_levelGlow.rectTransform, -90f, -70f, -90f, -90f);

            _levelButton = UIKit.ButtonLoc(_levelHolder, "level.number", ButtonColor.Green, LevelButtonSize, OnLevel);
            var lrt = (RectTransform)_levelButton.transform;
            UIKit.Stretch(lrt);
            _levelButton.Pressable.clickSound = Sfx.Click;
            if (_levelButton.label != null) DS.Apply(_levelButton.label, TextStyle.H2, 70f);

            // Win streak badge above the button.
            _streak = UIKit.Rect("WinStreak", _levelHolder);
            UIKit.Place(_streak, new Vector2(0.5f, 1f), new Vector2(300f, 70f), new Vector2(0f, 82f));
            _streak.pivot = new Vector2(0.5f, 0.5f);
            _streak.anchoredPosition = new Vector2(0f, 50f);
            UIKit.Shadow(_streak, 18f, -8f, 0.3f);
            var plate = UIKit.Capsule(_streak, Vector2.zero, DS.Colors.Orange);
            UIKit.Stretch(plate.rectTransform);
            var plateGloss = UIKit.Capsule(_streak, Vector2.zero, new Color(1f, 1f, 1f, 0.25f));
            UIKit.Stretch(plateGloss.rectTransform, 30f, 6f, 30f, 38f);
            _flame = UIKit.Image(_streak, "icon_flame", new Vector2(96f, 96f));
            UIKit.Place(_flame.rectTransform, new Vector2(0f, 0.5f), new Vector2(96f, 96f), new Vector2(-34f, 8f));
            _flame.rectTransform.pivot = new Vector2(0.5f, 0.2f);
            _streakText = UIKit.Text(_streak, "", TextStyle.H3, new Vector2(220f, 60f));
            DS.Apply(_streakText, TextStyle.H3, 40f);
            UIKit.Stretch(_streakText.rectTransform, 58f, 4f, 16f, 6f);
            _streak.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- layout

        void Layout()
        {
            float top = TopBar.Height + 16f;
            float slot = HomeSideButton.SlotHeight + ColumnGap;
            PlaceColumn(_chest, 0, top, slot, true);
            PlaceColumn(_daily, 1, top, slot, true);
            PlaceColumn(_quests, 2, top, slot, true);
            PlaceColumn(_spin, 0, top, slot, false);
            PlaceColumn(_rank, 1, top, slot, false);
            if (_mimi != null)
            {
                var size = MimiSize();
                var rt = (RectTransform)_mimi.transform;
                rt.sizeDelta = size;
                var body = rt.Find("Body") as RectTransform;
                if (body != null) body.sizeDelta = size;
            }
        }

        static void PlaceColumn(HomeSideButton b, int index, float top, float slot, bool left)
        {
            if (b == null) return;
            var rt = (RectTransform)b.transform;
            float x = DS.Space.ScreenMargin - 12f;
            UIKit.Place(rt, new Vector2(left ? 0f : 1f, 1f), rt.sizeDelta, new Vector2(left ? x : -x, -(top + index * slot)));
        }

        Vector2 MimiSize()
        {
            float screenH = _sm != null ? _sm.CanvasSize.y : DS.Space.ReferenceHeight;
            float h = Mathf.Clamp(screenH * 0.2f, 370f, 470f);
            var info = Art.Info("mascot_wave");
            float aspect = info != null && info.height > 0 ? info.width / (float)info.height : 523f / 768f;
            return new Vector2(h * aspect, h);
        }

        // ---------------------------------------------------------------------------------------- show / hide

        public override void OnShow(object arg)
        {
            PotionPop.Levels.LevelPrefetch.Prefetch(Progress.CurrentLevel);   // the Level button's board, built in the background
            Refresh(false);
            PlayEntrance();
            StartIdle();
            _autoPending = true;
            _autoAt = Time.unscaledTime + 0.6f;
        }

        public override void OnHide()
        {
            _autoPending = false;
            StopIdle();
        }

        void PlayEntrance()
        {
            if (_sign != null)
            {
                Tween.Kill(_sign);
                var target = new Vector2(0f, -(TopBar.Height - 4f));
                _sign.anchoredPosition = target + new Vector2(0f, 420f);
                Tween.Move(_sign, target, 0.6f, Ease.OutBack).SetOvershoot(1.2f).SetDelay(0.05f);
            }
            for (int i = 0; i < _sideButtons.Length; i++) _sideButtons[i].PopIn(0.12f + i * 0.06f);
            if (_levelHolder != null)
            {
                Tween.Kill(_levelHolder);
                _levelHolder.localScale = Vector3.zero;
                Tween.Scale(_levelHolder, 1f, 0.5f, Ease.OutBack).SetOvershoot(2f).SetDelay(0.22f).OnComplete(StartLevelPulse);
            }
            if (_mimi != null) Tween.Delay(0.35f, () => { if (_mimi != null && IsVisible) _mimi.Hop(70f, false); }).SetLink(_mimi);
        }

        void StartIdle()
        {
            if (_backdrop != null)
            {
                var t = _backdrop.transform;
                Tween.Kill(t);
                t.localScale = Vector3.one;
                Tween.Scale(t, 1.045f, 7f, Ease.InOutSine).SetLoops(-1, true);
            }
            if (_signSwing != null)
            {
                Tween.Kill(_signSwing);
                _signSwing.localEulerAngles = new Vector3(0f, 0f, -1.6f);
                Tween.Rotate(_signSwing, 1.6f, 1.6f, Ease.InOutSine).SetLoops(-1, true);
            }
            for (int i = 0; i < _sideButtons.Length; i++) _sideButtons[i].StartBob(i * 0.37f);
            StartSpinIcon(LuckySpin.HasFreeSpin);
            if (_levelGlow != null)
            {
                Tween.Kill(_levelGlow);
                _levelGlow.transform.localScale = Vector3.one;
                Tween.Scale(_levelGlow.transform, 1.08f, 0.9f, Ease.InOutSine).SetLoops(-1, true);
            }
            if (_flame != null)
            {
                var f = _flame.transform;
                Tween.Kill(f);
                f.localScale = Vector3.one;
                f.localEulerAngles = new Vector3(0f, 0f, -4f);
                Tween.Scale(f, new Vector3(1.08f, 1.16f, 1f), 0.32f, Ease.InOutSine).SetLoops(-1, true);
                Tween.Rotate(f, 4f, 0.5f, Ease.InOutSine).SetLoops(-1, true);
            }
            _attention = 1.5f;
            _sparkle = 1f;
        }

        void StopIdle()
        {
            if (_backdrop != null) Tween.Kill(_backdrop.transform);
            if (_signSwing != null) Tween.Kill(_signSwing);
            if (_levelHolder != null) Tween.Kill(_levelHolder);
            if (_levelGlow != null) Tween.Kill(_levelGlow.transform);
            if (_flame != null) Tween.Kill(_flame.transform);
            if (_spin != null && _spin.Icon != null) Tween.Kill(_spin.Icon.transform);
        }

        void StartLevelPulse()
        {
            if (_levelHolder == null || !IsVisible) return;
            Tween.Kill(_levelHolder);
            _levelHolder.localScale = Vector3.one;
            Tween.Pulse(_levelHolder, 1.06f, 0.9f);
        }

        void StartSpinIcon(bool fast)
        {
            _spinFast = fast;
            if (_spin == null || _spin.Icon == null) return;
            var t = _spin.Icon.transform;
            Tween.Kill(t);
            t.localEulerAngles = Vector3.zero;
            t.localScale = Vector3.one;
            Tween.Rotate(t, -360f, fast ? 4.5f : 12f, Ease.Linear).SetLoops(-1, false);
        }

        // ---------------------------------------------------------------------------------------- refresh

        void MarkDirty() => _dirty = true;
        void OnLevelChanged(int level) => _dirty = true;
        void OnStarsChanged(long oldValue, long newValue) => _dirty = true;

        void LateUpdate()
        {
            if (!_dirty || !IsVisible) return;
            _dirty = false;
            Refresh(true);
        }

        void Refresh(bool animate)
        {
            _dirty = false;
            int level = Progress.CurrentLevel;
            var area = Areas.AreaForLevel(level);

            // Backdrop of the current area.
            string sprite = CurrentBackdrop();
            if (_backdrop != null && sprite != _backdropSprite)
            {
                _backdropSprite = sprite;
                var sp = UISprites.Get(sprite);
                if (sp != null)
                {
                    _backdrop.sprite = sp;
                    _backdrop.color = Color.white;
                    if (_backdropFit != null && sp.rect.height > 0f) _backdropFit.aspectRatio = sp.rect.width / sp.rect.height;
                }
            }

            // Area sign.
            if (_areaName != null)
            {
                var loc = _areaName.GetComponent<LocText>();
                string key = area != null ? area.NameKey : "area.grocery";
                if (loc != null) loc.Set(key);
            }
            if (_areaBar != null)
            {
                int inArea = Areas.LevelInArea(level);
                _areaBar.SetValue(inArea / (float)Areas.LevelsPerArea, animate);
                _areaBar.SetLabel(Loc.T("quest.progress", inArea, Areas.LevelsPerArea));
                _areaBar.label.gameObject.SetActive(true);
            }

            // LEVEL button.
            if (_levelButton != null)
            {
                bool hard = Difficulty.IsHard(level);
                _levelButton.SetLabelKey("level.number", level);
                _levelButton.SetColor(hard ? ButtonColor.Purple : ButtonColor.Green);
                _levelButton.SetIcon(hard ? "icon_skull" : null);
                if (_levelGlow != null) _levelGlow.color = hard ? new Color(0.8f, 0.6f, 1f, 0.6f) : new Color(1f, 0.95f, 0.6f, 0.55f);
            }
            int streak = Progress.WinStreak;
            if (_streak != null)
            {
                bool show = streak >= 2;
                if (show)
                {
                    _streakText.text = Loc.T("home.streak", streak);
                    if (!_streak.gameObject.activeSelf && animate)
                    {
                        _streak.localScale = Vector3.zero;
                        Tween.Scale(_streak, 1f, 0.4f, Ease.OutBack).SetOvershoot(2f);
                    }
                }
                _streak.gameObject.SetActive(show);
            }

            RefreshSideButtons(animate);
            RefreshRank();
        }

        void RefreshSideButtons(bool animate)
        {
            if (_chest != null)
            {
                bool open = StarChest.CanOpen;
                _chest.SetBar(StarChest.Progress01, open ? Loc.T("ui.open") : Loc.T("quest.progress", StarChest.Progress, StarChest.Goal), animate);
                _chest.SetGlow(open);
                if (open) _chest.Badge.SetCount(Mathf.Max(1, StarChest.ChestsReady));
                else _chest.Badge.Hide();
            }
            if (_daily != null)
            {
                bool claim = DailyRewards.CanClaim;
                _daily.Button.SetIcon(claim ? "icon_gift" : "icon_calendar");
                _daily.SetGlow(claim);
                if (claim) _daily.Badge.SetText("!");
                else _daily.Badge.Hide();
            }
            if (_quests != null)
            {
                _quests.SetCaptionKey("home.side.quests");   // static caption, re-read after a language change
                int n = Quests.ClaimableCount;
                _quests.Badge.SetCount(n);
                _quests.SetGlow(n > 0);
            }
            if (_spin != null)
            {
                bool free = LuckySpin.HasFreeSpin;
                if (free) _spin.Badge.SetText("!");
                else _spin.Badge.Hide();
                _spin.SetGlow(free);
                if (free != _spinFast && IsVisible) StartSpinIcon(free);
            }
            RefreshTimers();
        }

        /// <summary>Captions that count down (called every second while visible).</summary>
        void RefreshTimers()
        {
            if (_daily != null)
                _daily.SetCaption(DailyRewards.CanClaim ? Loc.T("home.side.daily") : TimeUtil.FormatDuration(DailyRewards.SecondsToNextClaim));
            if (_spin != null)
            {
                if (LuckySpin.HasFreeSpin) _spin.SetCaption(Loc.T("home.side.spin_free"));
                else if (LuckySpin.AdSpinsLeft > 0) _spin.SetCaption(Loc.T("home.side.spin"));
                else _spin.SetCaption(TimeUtil.FormatDuration(TimeUtil.SecondsToMidnight));
            }
        }

        void RefreshRank()
        {
            if (_rank == null) return;
            var button = _rank;
            LeaderboardService.GetWeekly(list =>
            {
                if (button == null) return;
                int rank = 0;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null && list[i].isPlayer) { rank = list[i].rank; break; }
                button.SetCaption(Loc.T("home.rank", rank > 0 ? rank.ToString() : Loc.T("lb.rank_unknown")));
            });
        }

        static string CurrentBackdrop()
        {
            var area = Areas.AreaForLevel(Progress.CurrentLevel);
            return area != null ? area.HomeBackground : "home_grocery";
        }

        // ---------------------------------------------------------------------------------------- per frame

        void Update()
        {
            if (!IsVisible) return;
            float dt = Time.unscaledDeltaTime;

            _tick += dt;
            if (_tick >= 1f)
            {
                _tick = 0f;
                RefreshTimers();
            }

            bool calm = !PopupManager.AnyOpen && (_sm == null || !_sm.IsTransitioning);

            // Claimables wiggle for attention.
            _attention -= dt;
            if (_attention <= 0f)
            {
                _attention = 3.2f;
                if (calm)
                {
                    float delay = 0f;
                    if (StarChest.CanOpen) { Wiggle(_chest, delay); delay += 0.15f; }
                    if (DailyRewards.CanClaim) { Wiggle(_daily, delay); delay += 0.15f; }
                    if (Quests.ClaimableCount > 0) Wiggle(_quests, delay);
                }
            }

            // Twinkles on the LEVEL button.
            _sparkle -= dt;
            if (_sparkle <= 0f)
            {
                _sparkle = Random.Range(1.6f, 2.6f);
                if (calm && _levelButton != null && _sm != null && _sm.FxLayer != null)
                {
                    var rt = (RectTransform)_levelButton.transform;
                    Rect r = rt.rect;
                    var local = new Vector3(Random.Range(r.xMin + 40f, r.xMax - 40f), Random.Range(r.yMin + 30f, r.yMax - 20f), 0f);
                    FX.Sparkles(_sm.FxLayer, FX.ToLocal(_sm.FxLayer, rt.TransformPoint(local)), 3);
                }
            }

            if (_autoPending && Time.unscaledTime >= _autoAt && calm) RunAutoPopups();
        }

        static void Wiggle(HomeSideButton b, float delay)
        {
            if (b == null) return;
            if (delay <= 0f) b.Wiggle();
            else Tween.Delay(delay, () => { if (b != null) b.Wiggle(); }).SetLink(b);
        }

        // ---------------------------------------------------------------------------------------- auto popups

        /// <summary>One automatic popup at a time: new area → daily reward (once per day per session) → login nudge.</summary>
        void RunAutoPopups()
        {
            _autoPending = false;
            if (!IsVisible) return;

            if (Progress.HasUnseenArea)
            {
                int area = Progress.CurrentAreaNumber;
                Progress.MarkAreaSeen();
                AreaUnlockedPopup.Open(area, QueueNextAuto);
                return;
            }
            if (DailyRewards.CanClaim && _dailyAutoDay != TimeUtil.Today)
            {
                _dailyAutoDay = TimeUtil.Today;
                DailyRewardPopup.Open(QueueNextAuto);
                return;
            }
            var data = SaveSystem.Data;
            if (!data.loginNudgeShown && !AuthService.IsSignedIn && Progress.CurrentLevel >= LoginNudgeLevel)
            {
                data.loginNudgeShown = true;
                SaveSystem.MarkDirty();
                LoginPopup.Open(ok => QueueNextAuto());
            }
        }

        void QueueNextAuto()
        {
            if (!IsVisible) return;
            _autoPending = true;
            _autoAt = Time.unscaledTime + 0.35f;
        }

        // ---------------------------------------------------------------------------------------- taps

        void OnLevel()
        {
            if (_levelHolder != null)
            {
                FX.Ring(null, FX.LocalCenterOf((RectTransform)_levelButton.transform), DS.Colors.Accent, 520f);
            }
            LevelStartPopup.Open(Progress.CurrentLevel);
        }

        void OnChest() => StarChestPopup.Open();
        void OnDaily() => DailyRewardPopup.Open();
        void OnQuests() => QuestsPopup.Open();
        void OnSpin() => LuckySpinPopup.Open();

        void OnRank()
        {
            if (_sm != null) _sm.Show(ScreenId.Leaderboard);
        }

        void OnSignTapped()
        {
            if (_signSwing == null) return;
            AudioManager.Play(Sfx.Whoosh, 0.7f);
            Haptics.Play(HapticType.Light);
            Tween.Kill(_signSwing);
            _signSwing.localEulerAngles = new Vector3(0f, 0f, 7f);
            Tween.Rotate(_signSwing, -1.6f, 1.1f, Ease.OutElastic).OnComplete(() =>
            {
                if (_signSwing == null || !IsVisible) return;
                Tween.Rotate(_signSwing, 1.6f, 1.6f, Ease.InOutSine).SetLoops(-1, true);
            });
            if (_mimi != null && Random.value < 0.5f) _mimi.ShowTip();
        }
    }
}
