// ============================================================================================================
// Level Start: "Level N" ribbon, a "Hard level!" tag (skull, double coins) on hard levels, the star goal read from the
// level definition ("★★★ in ≤ N moves", 2-star threshold below; the definition comes from the background prefetch, so
// the line fills in as soon as it is ready), feature tags ("Hidden colors!", "Stone bottles!"), and the two
// pre-level boosters — Rainbow Potion and Crystal Ball — as tiles: owned count / "+" to buy, free and pre-selected by
// the win streak, locked below their unlock level, and the Crystal Ball disabled on levels without hidden colors.
// Replays (Worlds map) show the best stars so far and that only new stars pay. Play checks hearts (Out of Lives
// otherwise), consumes the selected boosters and starts the level.
// ============================================================================================================
using System;
using PotionPop.Game;
using PotionPop.Levels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>Level start popup. Open(level) for the next level, Open(level, true) to replay a won level.</summary>
    public class LevelStartPopup : Popup
    {
        const float TileW = 320f, TileH = 360f, TileGap = 52f;
        const float HardH = 140f, ReplayH = 150f, GoalH = 156f, FeaturesH = 86f;
        /// <summary>Seconds to wait for the background prefetch before generating the level on the main thread.</summary>
        const float SyncFallbackSeconds = 1.25f;

        int _level = 1;
        bool _hard, _replay, _started;
        bool _hidden, _stones;
        int _best;
        LevelDefinition _def;
        float _defWait;
        bool _syncTried;

        RectTransform _goalStars, _goalSpinner;
        TMP_Text _goal, _goalSub;
        RectTransform _featureRow;
        UIButton _play;
        Tile _rainbow, _crystal;

        static int _cacheLevel = -1;
        static LevelDefinition _cacheDef;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _cacheLevel = -1;
            _cacheDef = null;
        }

        protected override string TitleKey => "level.number";

        protected override Vector2 PanelSize
        {
            get
            {
                float h = 1000f;
                if (_hard) h += HardH;
                if (_replay) h += ReplayH;
                if (_hidden || _stones) h += FeaturesH;
                return new Vector2(DS.Space.PopupWidth, h);
            }
        }

        /// <summary>
        /// Opens the popup for <paramref name="level"/>. <paramref name="replay"/> = replaying an already won level from the
        /// Worlds map (ignored for levels not won yet).
        /// </summary>
        public static void Open(int level, bool replay = false)
        {
            if (PopupManager.IsOpen<LevelStartPopup>()) return;
            level = Mathf.Max(1, level);
            PopupManager.Show<LevelStartPopup>(p =>
            {
                p._level = level;
                p._hard = Difficulty.IsHard(level);
                p._replay = replay && level < Progress.CurrentLevel;
                p._best = Progress.BestStars(level);
                // Features from the difficulty curve right away (cheap); the definition confirms them when ready.
                var parameters = Difficulty.For(level);
                p._hidden = parameters.hiddenBottles > 0;
                p._stones = parameters.locks > 0;
                p._def = PeekDefinition(level);
                if (p._def != null)
                {
                    p._hidden = p._def.HasHidden;
                    p._stones = p._def.HasLocks;
                }
            });
        }

        /// <summary>The level definition if it is already known (cached or prefetched); starts the prefetch otherwise.</summary>
        static LevelDefinition PeekDefinition(int level)
        {
            if (_cacheLevel == level && _cacheDef != null) return _cacheDef;
            if (LevelPrefetch.TryPeek(level, out var def) && def != null)
            {
                Cache(level, def);
                return def;
            }
            LevelPrefetch.Prefetch(level);
            return null;
        }

        static void Cache(int level, LevelDefinition def)
        {
            _cacheLevel = level;
            _cacheDef = def;
        }

        // ---------------------------------------------------------------------------------------- build

        protected override void BuildContent(RectTransform content)
        {
            if (Ribbon != null)
            {
                var title = Ribbon.Find("Title");
                var loc = title != null ? title.GetComponent<LocText>() : null;
                if (loc != null) loc.Set("level.number", Loc.Number(_level));
            }

            float w = content.rect.width;
            float y = 0f;
            if (_hard)
            {
                var tag = MetaUI.Tag(content, "level.hard_tag", DS.Colors.Brand, "icon_skull", new Vector2(400f, 80f), 42f);
                UIKit.Place(tag, new Vector2(0.5f, 1f), tag.sizeDelta, Vector2.zero);
                tag.pivot = new Vector2(0.5f, 0.5f);
                tag.anchoredPosition = new Vector2(12f, -46f);
                Tween.Scale(tag, 1.06f, 0.6f, Ease.InOutSine).SetLoops(-1, true);
                var desc = MetaUI.LocLabel(content, "home.hard_desc", TextStyle.Body, 36f, new Vector2(w, 50f), new Vector2(0.5f, 1f), new Vector2(0f, -92f));
                desc.color = DS.Colors.Brand;
                y += HardH;
            }
            if (_replay)
            {
                BuildReplay(content, w, y);
                y += ReplayH;
            }

            BuildGoal(content, w, y);
            y += GoalH + DS.Space.M;

            if (_hidden || _stones)
            {
                BuildFeatures(content, w, y);
                y += FeaturesH;
            }

            var header = MetaUI.LocLabel(content, "home.pick_boosters", TextStyle.Small, 38f, new Vector2(w, 56f), new Vector2(0.5f, 1f), new Vector2(0f, -y));
            header.color = DS.Colors.InkSoft;
            y += 96f;   // room for the "Free!" tags that stick out above the tiles

            var bonuses = Progress.StreakBonuses;
            var row = UIKit.Rect("Boosters", content);
            UIKit.Place(row, new Vector2(0.5f, 1f), new Vector2(TileW * 2f + TileGap, TileH), new Vector2(0f, -y));
            _rainbow = Tile.Create(this, row, BoosterType.Rainbow, Array.IndexOf(bonuses, BoosterType.Rainbow) >= 0,
                new Vector2(-(TileW + TileGap) * 0.5f, 0f));
            _crystal = Tile.Create(this, row, BoosterType.Crystal, Array.IndexOf(bonuses, BoosterType.Crystal) >= 0,
                new Vector2((TileW + TileGap) * 0.5f, 0f));
            _crystal.SetDisabled(!_hidden, false);

            _play = UIKit.ButtonLoc(content, "ui.play", ButtonColor.Green, ButtonSize.Large, OnPlay, "icon_play");
            var prt = (RectTransform)_play.transform;
            UIKit.Place(prt, new Vector2(0.5f, 0f), DS.ButtonDimensions(ButtonSize.Large), new Vector2(0f, 4f));
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 4f + DS.ButtonDimensions(ButtonSize.Large).y * 0.5f);
            Tween.Pulse(prt);

            // Luna points at the Play button from the panel's corner.
            var luna = UIKit.Image(Panel, "mascot_point", new Vector2(190f, 265f));
            UIKit.Place(luna.rectTransform, new Vector2(0f, 0f), new Vector2(190f, 265f), new Vector2(-40f, -26f));
            luna.rectTransform.pivot = new Vector2(0.5f, 0f);
            luna.rectTransform.anchoredPosition = new Vector2(55f, -26f);
            Tween.Scale(luna.transform, new Vector3(1.03f, 0.97f, 1f), 0.9f, Ease.InOutSine).SetLoops(-1, true);

            RefreshGoal(false);
            MetaUI.PopInAll(new[] { _rainbow.Root, _crystal.Root }, 0.18f, 0.1f);
        }

        void BuildReplay(RectTransform content, float w, float y)
        {
            var box = UIKit.Rect("Replay", content);
            UIKit.Place(box, new Vector2(0.5f, 1f), new Vector2(w, ReplayH - DS.Space.S), new Vector2(0f, -y));
            var bg = UIKit.RoundedRect(box, Vector2.zero, DS.WithAlpha(DS.Colors.StarGold, 0.22f), 40f);
            UIKit.Stretch(bg.rectTransform);
            var label = MetaUI.LocLabel(box, "level.replay_best", TextStyle.Small, 32f, new Vector2(230f, 44f), new Vector2(0f, 1f), new Vector2(24f, -12f));
            label.alignment = TextAlignmentOptions.Center;
            var stars = MetaUI.StarRow(box, _best, 62f, -4f, 12f);
            UIKit.Place(stars, new Vector2(0f, 0f), stars.sizeDelta, new Vector2(24f + (230f - stars.sizeDelta.x) * 0.5f, 14f));
            MetaUI.PopStars(stars, _best, 0.35f);
            string key = _best >= 3 ? "level.replay_perfect" : "level.replay_rule";
            var rule = MetaUI.LocLabel(box, key, TextStyle.Body, 36f, new Vector2(w - 300f, ReplayH - 40f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f),
                Progress.ReplayCoinsPerStar);
            rule.alignment = TextAlignmentOptions.Left;
        }

        void BuildGoal(RectTransform content, float w, float y)
        {
            var goalRow = UIKit.Rect("Goal", content);
            UIKit.Place(goalRow, new Vector2(0.5f, 1f), new Vector2(w, GoalH), new Vector2(0f, -y));
            var inset = UIKit.Panel(goalRow, "panel_inset", new Vector2(w, GoalH));
            UIKit.Stretch(inset.rectTransform);
            _goalStars = MetaUI.StarRow(goalRow, 3, 70f, -8f, 14f);
            UIKit.Place(_goalStars, new Vector2(0f, 0.5f), _goalStars.sizeDelta, new Vector2(26f, 4f));
            Tween.Scale(_goalStars, 1.05f, 0.8f, Ease.InOutSine).SetLoops(-1, true);
            float textX = 26f + _goalStars.sizeDelta.x + 18f;
            _goal = UIKit.Text(goalRow, "", TextStyle.Body, new Vector2(w - textX - 20f, 64f), TextAlignmentOptions.Left);
            DS.Apply(_goal, TextStyle.Body, 44f);
            _goal.alignment = TextAlignmentOptions.Left;
            _goal.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(_goal.rectTransform, new Vector2(0f, 0.5f), new Vector2(w - textX - 20f, 64f), new Vector2(textX, 22f));
            _goalSub = UIKit.Text(goalRow, "", TextStyle.Small, new Vector2(w - textX - 20f, 48f), TextAlignmentOptions.Left);
            DS.Apply(_goalSub, TextStyle.Small, 32f);
            _goalSub.alignment = TextAlignmentOptions.Left;
            _goalSub.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(_goalSub.rectTransform, new Vector2(0f, 0.5f), new Vector2(w - textX - 20f, 48f), new Vector2(textX, -30f));
            var spinner = CommonUI.Spinner(goalRow, 54f, DS.Colors.BrandLight);
            _goalSpinner = (RectTransform)spinner.transform;
            UIKit.Place(_goalSpinner, new Vector2(1f, 0.5f), new Vector2(54f, 54f), new Vector2(-30f, 0f));
        }

        void BuildFeatures(RectTransform content, float w, float y)
        {
            _featureRow = UIKit.Rect("Features", content);
            UIKit.Place(_featureRow, new Vector2(0.5f, 1f), new Vector2(w, FeaturesH - 14f), new Vector2(0f, -y));
            int count = (_hidden ? 1 : 0) + (_stones ? 1 : 0);
            float tagW = count > 1 ? (w - DS.Space.S) * 0.5f : Mathf.Min(w, 420f);
            float x = count > 1 ? -(tagW + DS.Space.S) * 0.5f : 0f;
            if (_hidden)
            {
                var tag = MetaUI.Tag(_featureRow, "level.feature_hidden", DS.Colors.Secondary, "booster_crystal", new Vector2(tagW, 66f), 34f);
                UIKit.Place(tag, new Vector2(0.5f, 0.5f), tag.sizeDelta, new Vector2(x, 0f));
                x += tagW + DS.Space.S;
                CommonUI.PopIn(tag, 0.2f);
            }
            if (_stones)
            {
                var tag = MetaUI.Tag(_featureRow, "level.feature_stone", DS.Colors.Orange, "icon_lock", new Vector2(tagW, 66f), 34f);
                UIKit.Place(tag, new Vector2(0.5f, 0.5f), tag.sizeDelta, new Vector2(x, 0f));
                CommonUI.PopIn(tag, 0.28f);
            }
        }

        // ---------------------------------------------------------------------------------------- goal

        void Update()
        {
            if (_def != null || IsClosing) return;
            _defWait += Time.unscaledDeltaTime;
            if (LevelPrefetch.TryPeek(_level, out var def) && def != null)
            {
                OnDefinitionReady(def);
                return;
            }
            if (_syncTried || _defWait < SyncFallbackSeconds) return;
            // The worker never delivered (no thread pool / still busy): generate this one level here (bounded cost).
            _syncTried = true;
            try { def = LevelGenerator.Generate(_level); }
            catch (Exception e) { Debug.LogException(e); def = null; }
            if (def != null) OnDefinitionReady(def);
        }

        void OnDefinitionReady(LevelDefinition def)
        {
            _def = def;
            Cache(_level, def);
            if (_crystal != null && def.HasHidden != _hidden)
            {
                _hidden = def.HasHidden;
                _crystal.SetDisabled(!_hidden, true);
            }
            RefreshGoal(true);
        }

        void RefreshGoal(bool punch)
        {
            if (_goal == null) return;
            bool ready = _def != null;
            if (_goalSpinner != null) _goalSpinner.gameObject.SetActive(!ready);
            if (!ready)
            {
                _goal.text = Loc.T("level.goal_loading");
                _goalSub.text = "";
                return;
            }
            if (_def.movesFor3Stars > 0)
            {
                _goal.text = Loc.T("level.goal_3stars", _def.movesFor3Stars);
                _goalSub.text = _def.movesFor2Stars > _def.movesFor3Stars ? Loc.T("level.goal_2stars", _def.movesFor2Stars) : "";
            }
            else
            {
                _goal.text = Loc.T("level.goal_any");
                _goalSub.text = "";
            }
            if (!punch) return;
            Tween.Punch(_goal.transform, 0.12f, 0.3f);
            if (_goalStars != null) MetaUI.PopStars(_goalStars, 3, 0f);
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
            var pre = new PreBoosters
            {
                rainbow = Consume(_rainbow),
                crystal = Consume(_crystal),
            };
            _started = true;
            int level = _level;
            bool replay = _replay;
            Close();
            GameScreen.StartLevel(level, pre, replay);
        }

        /// <summary>Closed without playing while on a finished Game screen (opened by the game's "Next" / restart flow):
        /// go back to the world map (replays) or Home instead of leaving the player on an empty board.</summary>
        protected override void OnClosing()
        {
            if (_started) return;
            var sm = ScreenManager.Instance;
            if (sm == null || sm.Current != ScreenId.Game) return;
            var game = GameScreen.Instance;
            if (game != null && game.IsPlaying) return;
            if (_replay) WorldsScreen.OpenWorld(Areas.AreaNumberForLevel(_level));
            else sm.Show(ScreenId.Home);
        }

        static bool Consume(Tile t)
        {
            if (t == null || !t.Selected || t.Disabled || t.Locked) return false;
            if (t.Free) return true;
            return Economy.TryUseBooster(t.Type);
        }

        // ---------------------------------------------------------------------------------------- tile

        sealed class Tile
        {
            public BoosterType Type;
            public bool Free;
            public bool Selected;
            public bool Disabled;
            public RectTransform Root;
            public bool Locked => !Economy.IsBoosterUnlocked(Type);

            LevelStartPopup _owner;
            RectTransform _visual;
            Image _rim, _icon;
            RectTransform _check, _count, _lockOverlay, _disabledOverlay, _freeTag;
            TMP_Text _countText;
            Image _countBg;

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
                    t._freeTag = MetaUI.Tag(visual, "booster.free", DS.Colors.Orange, "icon_flame", new Vector2(220f, 66f), 36f);
                    UIKit.Place(t._freeTag, new Vector2(0.5f, 1f), t._freeTag.sizeDelta, new Vector2(8f, 30f));
                    Tween.Scale(t._freeTag, 1.07f, 0.45f, Ease.InOutSine).SetLoops(-1, true);
                }

                // Lock overlay (below the unlock level).
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

            /// <summary>Not usable in this level (Crystal Ball without hidden colors): gray, a note, never consumed.</summary>
            public void SetDisabled(bool disabled, bool animate)
            {
                if (Locked) disabled = false;   // the lock already explains it
                if (Disabled == disabled && (_disabledOverlay != null || !disabled)) return;
                Disabled = disabled;
                if (disabled && _disabledOverlay == null)
                {
                    _disabledOverlay = UIKit.Rect("NotNeeded", _visual);
                    UIKit.Stretch(_disabledOverlay);
                    var dim = UIKit.RoundedRect(_disabledOverlay, Vector2.zero, new Color(0.93f, 0.91f, 0.96f, 0.82f), 46f);
                    UIKit.Stretch(dim.rectTransform, 4f, 4f, 4f, 10f);
                    var note = MetaUI.LocLabel(_disabledOverlay, "booster.crystal_not_needed", TextStyle.Body, 34f, new Vector2(TileW - 50f, 130f),
                        new Vector2(0.5f, 0f), new Vector2(0f, 96f));
                    note.color = DS.Colors.InkSoft;
                }
                if (_disabledOverlay != null)
                {
                    _disabledOverlay.gameObject.SetActive(disabled);
                    if (disabled && animate) CommonUI.PopIn(_disabledOverlay, 0f);
                }
                if (_freeTag != null) _freeTag.gameObject.SetActive(!disabled);
                if (_icon != null && !Locked) _icon.color = disabled ? new Color(0.72f, 0.7f, 0.78f, 0.85f) : Color.white;
                Refresh(animate);
            }

            void Refresh(bool animate)
            {
                bool locked = Locked;
                bool on = Selected && !locked && !Disabled;
                int count = Economy.GetBooster(Type);
                if (_rim != null)
                {
                    if (animate && on && !_rim.gameObject.activeSelf)
                    {
                        _rim.transform.localScale = Vector3.one * 0.9f;
                        Tween.Scale(_rim.transform, 1f, 0.3f, Ease.OutBack);
                    }
                    _rim.gameObject.SetActive(on);
                }
                if (_check != null)
                {
                    if (on && !_check.gameObject.activeSelf && animate)
                    {
                        _check.localScale = Vector3.one * 2.2f;
                        Tween.Scale(_check, 1f, 0.3f, Ease.OutBack).SetOvershoot(1.6f);
                    }
                    _check.gameObject.SetActive(on);
                }
                if (_count != null)
                {
                    _count.gameObject.SetActive(!locked && !Free && !Disabled);
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
                    Tween.Kill(it);
                    it.localEulerAngles = Vector3.zero;
                    if (animate && !Disabled)
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
                if (Disabled)
                {
                    AudioManager.Play(Sfx.Invalid);
                    Haptics.Play(HapticType.Warning);
                    Tween.Shake(_visual, 12f, 0.3f);
                    UIKit.Toast(Loc.T("booster.crystal_not_needed"));
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
                    });
                    return;
                }
                Selected = !Selected;
                AudioManager.Play(Selected ? Sfx.Toggle : Sfx.Click);
                Haptics.Play(HapticType.Selection);
                if (Selected) MetaUI.Celebrate((RectTransform)_icon.transform, 8, 0);
                Refresh(true);
            }
        }
    }
}
