// ============================================================================================================
// Level Start: "Level N" ribbon, HARD tag (skull) on hard levels, the goal line with the level's time limit, the
// pre-level booster tiles (Time, Bomb) — locked below their unlock level, free and pre-selected with a flame when
// the win streak grants them — and the pulsing Play button. Play checks hearts (else Out of Lives), consumes the
// selected non-free boosters and starts the level.
// ============================================================================================================
using System;
using PotionPop.Game;
using PotionPop.Levels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Level start: level number, hard badge, pre-boosters (Time, Bomb) toggles incl. streak freebies,
    /// Play → checks Lives.CanPlay (else OutOfLivesPopup), consumes selected boosters, GameScreen.StartLevel.</summary>
    public class LevelStartPopup : Popup
    {
        const float TileW = 320f, TileH = 360f, TileGap = 52f;

        int _level = 1;
        bool _hard;
        int _baseSeconds;
        bool _started;
        TMP_Text _goal;
        UIButton _play;
        Tile _time, _bomb;

        static int _cachedLevel = -1, _cachedSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _cachedLevel = -1;
            _cachedSeconds = 0;
        }

        protected override string TitleKey => "level.number";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, _hard ? 1130f : 980f);

        public static void Open(int level)
        {
            if (PopupManager.IsOpen<LevelStartPopup>()) return;
            PopupManager.Show<LevelStartPopup>(p =>
            {
                p._level = Mathf.Max(1, level);
                p._hard = Difficulty.IsHard(p._level);
            });
        }

        /// <summary>Time limit of a level in seconds (generated once per level and cached).</summary>
        public static int LevelSeconds(int level)
        {
            if (level == _cachedLevel) return _cachedSeconds;
            int seconds = 0;
            try
            {
                var def = LevelGenerator.Generate(level);
                if (def != null) seconds = def.par;   // TEMP (meta agent): the popup shows the goal from the definition
            }
            catch (Exception e) { Debug.LogException(e); }
            if (seconds <= 0) seconds = 10;
            _cachedLevel = level;
            _cachedSeconds = seconds;
            return seconds;
        }

        // ---------------------------------------------------------------------------------------- build

        protected override void BuildContent(RectTransform content)
        {
            // Ribbon "Level N".
            if (Ribbon != null)
            {
                var title = Ribbon.Find("Title");
                var loc = title != null ? title.GetComponent<LocText>() : null;
                if (loc != null) loc.Set("level.number", _level);
            }
            _baseSeconds = LevelSeconds(_level);
            var bonuses = Progress.StreakBonuses;
            bool freeTime = Array.IndexOf(bonuses, BoosterType.Crystal) >= 0;
            bool freeBomb = Array.IndexOf(bonuses, BoosterType.Rainbow) >= 0;

            float y = 0f;
            float w = content.rect.width;
            if (_hard)
            {
                var tag = MetaUI.Tag(content, "home.hard_badge", DS.Colors.Brand, "icon_skull", new Vector2(300f, 74f), 40f);
                UIKit.Place(tag, new Vector2(0.5f, 1f), tag.sizeDelta, new Vector2(10f, -6f));
                tag.pivot = new Vector2(0.5f, 0.5f);
                tag.anchoredPosition = new Vector2(10f, -43f);
                Tween.Scale(tag, 1.06f, 0.6f, Ease.InOutSine).SetLoops(-1, true);
                var desc = MetaUI.LocLabel(content, "home.hard_desc", TextStyle.Body, 34f, new Vector2(w, 50f), new Vector2(0.5f, 1f), new Vector2(0f, -88f));
                desc.color = DS.Colors.Brand;
                y = 150f;
            }

            // Goal line with the timer icon.
            var goalRow = UIKit.Rect("Goal", content);
            UIKit.Place(goalRow, new Vector2(0.5f, 1f), new Vector2(w, 120f), new Vector2(0f, -y));
            var inset = UIKit.Panel(goalRow, "panel_inset", new Vector2(w, 120f));
            UIKit.Stretch(inset.rectTransform);
            var timer = UIKit.Image(goalRow, "icon_timer", new Vector2(92f, 92f));
            UIKit.Place(timer.rectTransform, new Vector2(0f, 0.5f), new Vector2(92f, 92f), new Vector2(26f, 2f));
            Tween.Rotate(timer.transform, 8f, 0.5f, Ease.InOutSine).SetLoops(-1, true);
            _goal = UIKit.Text(goalRow, "", TextStyle.Body, new Vector2(w - 150f, 110f));
            DS.Apply(_goal, TextStyle.Body, 42f);
            UIKit.Stretch(_goal.rectTransform, 130f, 8f, 24f, 8f);
            y += 140f;

            var header = MetaUI.LocLabel(content, "home.pick_boosters", TextStyle.Small, 38f, new Vector2(w, 56f), new Vector2(0.5f, 1f), new Vector2(0f, -y));
            header.color = DS.Colors.InkSoft;
            y += 96f;   // room for the "FREE" tags that stick out 30 px above the tiles

            // Booster tiles.
            var row = UIKit.Rect("Boosters", content);
            UIKit.Place(row, new Vector2(0.5f, 1f), new Vector2(TileW * 2f + TileGap, TileH), new Vector2(0f, -y));
            _time = Tile.Create(this, row, BoosterType.Crystal, freeTime, new Vector2(-(TileW + TileGap) * 0.5f, 0f));
            _bomb = Tile.Create(this, row, BoosterType.Rainbow, freeBomb, new Vector2((TileW + TileGap) * 0.5f, 0f));

            // Play.
            _play = UIKit.ButtonLoc(content, "ui.play", ButtonColor.Green, ButtonSize.Large, OnPlay, "icon_play");
            var prt = (RectTransform)_play.transform;
            UIKit.Place(prt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 4f));
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 4f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);
            Tween.Pulse(prt);

            // Mimi points at the Play button from the panel's corner.
            var mimi = UIKit.Image(Panel, "mascot_point", new Vector2(190f, 265f));
            UIKit.Place(mimi.rectTransform, new Vector2(0f, 0f), new Vector2(190f, 265f), new Vector2(-40f, -26f));
            mimi.rectTransform.pivot = new Vector2(0.5f, 0f);
            mimi.rectTransform.anchoredPosition = new Vector2(55f, -26f);
            Tween.Scale(mimi.transform, new Vector3(1.03f, 0.97f, 1f), 0.9f, Ease.InOutSine).SetLoops(-1, true);

            RefreshGoal(false);
            var tiles = new[] { _time.Root, _bomb.Root };
            MetaUI.PopInAll(tiles, 0.18f, 0.1f);
        }

        void RefreshGoal(bool punch)
        {
            if (_goal == null) return;
            int seconds = _baseSeconds;   // TEMP (meta agent): goal = star thresholds
            _goal.text = Loc.T("home.goal", TimeUtil.FormatMMSS(seconds));
            if (punch) Tween.Punch(_goal.transform, 0.12f, 0.3f);
        }

        // ---------------------------------------------------------------------------------------- play

        void OnPlay()
        {
            if (_started || IsClosing) return;
            if (!Lives.CanPlay)
            {
                // Refill / ad / regen → the hearts fly in and the level starts right away.
                OutOfLivesPopup.Open(() => { if (this != null && !IsClosing && !_started && Lives.CanPlay) OnPlay(); });
                return;
            }
            var pre = new PreBoosters();
            pre.crystal = Consume(_time);
            pre.rainbow = Consume(_bomb);
            _started = true;
            int level = _level;
            Close();
            GameScreen.StartLevel(level, pre);
        }

        /// <summary>Closed without playing while on a finished Game screen (opened by the game's "Next"/restart
        /// flow): go back Home instead of leaving the player on an empty board.</summary>
        protected override void OnClosing()
        {
            if (_started) return;
            var sm = ScreenManager.Instance;
            if (sm == null || sm.Current != ScreenId.Game) return;
            var game = GameScreen.Instance;
            if (game != null && game.IsPlaying) return;
            sm.Show(ScreenId.Home);
        }

        static bool Consume(Tile t)
        {
            if (t == null || !t.Selected) return false;
            if (t.Free) return true;
            return Economy.TryUseBooster(t.Type);
        }

        void OnTileChanged(Tile t)
        {
            if (t == _time) RefreshGoal(true);
        }

        // ---------------------------------------------------------------------------------------- tile

        sealed class Tile
        {
            public BoosterType Type;
            public bool Free;
            public bool Selected;
            public RectTransform Root;

            LevelStartPopup _owner;
            RectTransform _visual;
            Image _rim, _icon;
            RectTransform _check, _count, _lockOverlay;
            TMP_Text _countText;
            Image _countBg;
            bool Locked => !Economy.IsBoosterUnlocked(Type);

            public static Tile Create(LevelStartPopup owner, RectTransform parent, BoosterType type, bool free, Vector2 pos)
            {
                var t = new Tile { Type = type, Free = free, _owner = owner };
                var root = UIKit.Rect("Tile_" + type, parent);
                UIKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(TileW, TileH), pos);
                t.Root = root;
                var hit = root.gameObject.AddComponent<HitArea>();
                var btn = root.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = hit;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.onClick.AddListener(t.OnTap);

                var visual = UIKit.Stretch(UIKit.Rect("Visual", root));
                t._visual = visual;
                var press = root.gameObject.AddComponent<Pressable>();
                press.target = visual;
                press.playSound = false;

                t._rim = UIKit.RoundedRect(visual, Vector2.zero, DS.Colors.Primary, 54f);
                UIKit.Stretch(t._rim.rectTransform, -12f, -12f, -12f, -12f);
                var card = UIKit.Panel(visual, "panel_card", new Vector2(TileW, TileH));
                UIKit.Stretch(card.rectTransform);

                var glow = UIKit.NewImage(visual, "Glow", UISprites.Glow, new Color(1f, 0.95f, 0.7f, 0.8f));
                UIKit.Place(glow.rectTransform, new Vector2(0.5f, 1f), new Vector2(250f, 250f), new Vector2(0f, -10f));
                t._icon = UIKit.Image(visual, Economy.BoosterSprite(type), new Vector2(180f, 180f));
                UIKit.Place(t._icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(180f, 180f), new Vector2(0f, -40f));

                var name = MetaUI.LocLabel(visual, Economy.BoosterNameKey(type), TextStyle.Body, 36f, new Vector2(TileW - 40f, 54f),
                    new Vector2(0.5f, 0f), new Vector2(0f, 50f));
                name.textWrappingMode = TextWrappingModes.NoWrap;

                // Count bubble (bottom-right).
                t._count = UIKit.Rect("Count", visual);
                UIKit.Place(t._count, new Vector2(1f, 0f), new Vector2(84f, 84f), new Vector2(14f, -14f));
                t._count.pivot = new Vector2(0.5f, 0.5f);
                t._count.anchoredPosition = new Vector2(-24f, 30f);
                t._countBg = UIKit.NewImage(t._count, "Bg", UISprites.Circle, DS.Colors.Brand);
                UIKit.Stretch(t._countBg.rectTransform);
                var ring = UIKit.NewImage(t._count, "Ring", UISprites.Ring, Color.white);
                UIKit.Stretch(ring.rectTransform);
                t._countText = UIKit.Text(t._count, "", TextStyle.H3, new Vector2(84f, 84f));
                DS.Apply(t._countText, TextStyle.H3, 38f);
                UIKit.Stretch(t._countText.rectTransform, 6f, 4f, 6f, 8f);

                // Selected check (top-right).
                t._check = MetaUI.CheckStamp(visual, 78f, false);
                UIKit.Place(t._check, new Vector2(1f, 1f), new Vector2(78f, 78f), Vector2.zero);
                t._check.pivot = new Vector2(0.5f, 0.5f);
                t._check.anchoredPosition = new Vector2(-18f, -18f);

                // Free (win streak) tag on top.
                if (free)
                {
                    var tag = MetaUI.Tag(visual, "booster.free", DS.Colors.Orange, "icon_flame", new Vector2(220f, 66f), 36f);
                    UIKit.Place(tag, new Vector2(0.5f, 1f), tag.sizeDelta, new Vector2(8f, 30f));
                    Tween.Scale(tag, 1.07f, 0.45f, Ease.InOutSine).SetLoops(-1, true);
                }

                // Lock overlay.
                if (t.Locked)
                {
                    t._lockOverlay = UIKit.Rect("Locked", visual);
                    UIKit.Stretch(t._lockOverlay);
                    var dim = UIKit.RoundedRect(t._lockOverlay, Vector2.zero, new Color(0.23f, 0.12f, 0.36f, 0.55f), 46f);
                    UIKit.Stretch(dim.rectTransform, 4f, 4f, 4f, 10f);
                    var lockIcon = UIKit.Image(t._lockOverlay, "icon_lock", new Vector2(120f, 120f));
                    UIKit.Place(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(120f, 120f), new Vector2(0f, 40f));
                    MetaUI.LocLabel(t._lockOverlay, "booster.unlock_at", TextStyle.H3, 40f, new Vector2(TileW - 30f, 60f),
                        new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), Economy.BoosterUnlockLevel(type));
                    t._icon.color = new Color(0.75f, 0.72f, 0.8f, 1f);
                }

                t.Selected = free && !t.Locked;
                t.Refresh(false);
                return t;
            }

            void Refresh(bool animate)
            {
                bool locked = Locked;
                int count = Economy.GetBooster(Type);
                if (_rim != null)
                {
                    bool on = Selected && !locked;
                    if (animate && on && !_rim.gameObject.activeSelf)
                    {
                        _rim.transform.localScale = Vector3.one * 0.9f;
                        Tween.Scale(_rim.transform, 1f, 0.3f, Ease.OutBack);
                    }
                    _rim.gameObject.SetActive(on);
                }
                if (_check != null)
                {
                    bool show = Selected && !locked;
                    if (show && !_check.gameObject.activeSelf && animate)
                    {
                        _check.localScale = Vector3.one * 2.2f;
                        Tween.Scale(_check, 1f, 0.3f, Ease.OutBack).SetOvershoot(1.6f);
                    }
                    _check.gameObject.SetActive(show);
                }
                if (_count != null)
                {
                    _count.gameObject.SetActive(!locked && !Free);
                    if (count > 0)
                    {
                        _countText.text = count.ToString();
                        _countBg.color = DS.Colors.Brand;
                    }
                    else
                    {
                        _countText.text = "+";
                        _countBg.color = DS.Colors.Primary;
                    }
                }
                if (_icon != null)
                {
                    var it = _icon.transform;
                    bool on = Selected && !locked;
                    Tween.Kill(it);
                    it.localEulerAngles = Vector3.zero;
                    if (animate)
                    {
                        it.localScale = Vector3.one * 1.3f;
                        Tween.Scale(it, 1f, 0.32f, Ease.OutBack).SetOvershoot(2f).OnComplete(() =>
                        {
                            if (on && it != null && Selected) Tween.Scale(it, 1.08f, 0.55f, Ease.InOutSine).SetLoops(-1, true);
                        });
                    }
                    else
                    {
                        it.localScale = Vector3.one;
                        if (on) Tween.Scale(it, 1.08f, 0.55f, Ease.InOutSine).SetLoops(-1, true);
                    }
                }
            }

            void OnTap()
            {
                if (_owner == null || _owner.IsClosing) return;
                if (Locked)
                {
                    AudioManager.Play(Sfx.Invalid);
                    Haptics.Play(HapticType.Warning);
                    Tween.Shake(_visual, 14f, 0.3f);
                    UIKit.Toast(Loc.T("booster.locked", Economy.BoosterUnlockLevel(Type)));
                    return;
                }
                if (Free)
                {
                    AudioManager.Play(Sfx.Pop);
                    Haptics.Play(HapticType.Light);
                    Refresh(true);
                    UIKit.Toast(Loc.T("booster.streak_bonus"));
                    return;
                }
                if (!Selected && Economy.GetBooster(Type) <= 0)
                {
                    AudioManager.Play(Sfx.Click);
                    BuyBoosterPopup.Open(Type, bought =>
                    {
                        if (!bought || _owner == null || _owner.IsClosing) return;
                        Selected = true;
                        Refresh(true);
                        MetaUI.Celebrate((RectTransform)_icon.transform, 8, 8);
                        _owner.OnTileChanged(this);
                    });
                    return;
                }
                Selected = !Selected;
                AudioManager.Play(Selected ? Sfx.Toggle : Sfx.Click);
                Haptics.Play(HapticType.Selection);
                if (Selected) MetaUI.Celebrate((RectTransform)_icon.transform, 8, 0);
                Refresh(true);
                _owner.OnTileChanged(this);
            }
        }
    }
}
