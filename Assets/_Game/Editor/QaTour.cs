using System;
using System.IO;
using System.Reflection;
using PotionPop.Game;
using PotionPop.Game.Board;
using PotionPop.Levels;
using PotionPop.Services;
using PotionPop.UI;
using UnityEditor;
using UnityEngine;
using SessionState = PotionPop.Game.SessionState;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Play-mode QA helpers (driven from the Potion Pop/QA menu or by automation through MCP execute_code): screenshots
    /// at phone resolution, opening screens / popups (with sample data for the in-level ones), starting levels and an
    /// autoplayer that plays the solver's moves through GameSession.HandlePourRequest — the same entry point the
    /// BoardView's taps use, so pours, combos, animations, the win flow and the reporting all run for real.
    /// RunScript runs a whole tour asynchronously (poll ScriptRunning / ScriptLog).
    /// </summary>
    public static class QaTour
    {
        public const string ShotDir = "Screenshots/qa";

        /// <summary>The default tour: every screen, the in-level popups, a hidden-color level, a stone level, boosters
        /// and two complete auto-played levels (win flow included). Run it from Potion Pop/QA/Run Full Tour.</summary>
        public const string DefaultTour =
            "view:1080x2340;nobanner;cheat:coins;cheat:hearts;cheat:boosters;closeall;" +
            "show:Home;wait:1.2;shot:01_home;show:Worlds;wait:1;shot:02_worlds;show:Shop;shot:03_shop;show:Home;wait:0.8;" +
            "tutorial-reset;setlevel:1;level:1;ready;shot:10_level1_tutorial;auto;wait-auto;wait:1.6;shot:11_level_complete;closeall;" +
            "setlevel:9;level:9;ready;shot:12_level9;auto:3;wait-auto;booster:Undo;wait:1.2;shot:13_after_undo;" +
            "booster:Bottle;wait:1.2;shot:14_extra_bottle;booster:Shuffle;wait:1.6;shot:15_shuffle;" +
            "pause;shot:16_pause;closeall;nomoves;shot:17_no_moves;closeall;" +
            "setlevel:15;level:15:crystal;ready;shot:20_hidden_crystal;" +
            "setlevel:25;level:25:rainbow;ready;shot:21_stone_rainbow;stone;shot:22_stone_popup;closeall;booster:Wand;wait:2;shot:23_wand;" +
            "setlevel:30;level:30;ready;shot:24_hard;auto;wait-auto;wait:2;shot:25_complete_hard;closeall;" +
            "gamepopup:complete1;shot:30_complete_1star;closeall;gamepopup:replay;shot:31_replay_best;closeall;" +
            "gamepopup:failed;shot:32_failed;closeall;gamepopup:intro:Wand;shot:33_booster_intro;closeall;show:Home";

        // ---------------------------------------------------------------------------------------- menu

        [MenuItem("Potion Pop/QA/Run Full Tour", priority = 60)]
        static void RunTourMenu() => Debug.Log(EditorUtil.LogPrefix + RunScript(DefaultTour));

        [MenuItem("Potion Pop/QA/Auto-play Current Level", priority = 61)]
        static void AutoPlayMenu() => Debug.Log(EditorUtil.LogPrefix + AutoPlay());

        [MenuItem("Potion Pop/QA/Stop", priority = 62)]
        static void StopMenu()
        {
            StopAutoPlay();
            if (ScriptRunning) EndScript("stopped from the menu");
            Debug.Log(EditorUtil.LogPrefix + "QA stopped.\n" + ScriptLog);
        }

        [MenuItem("Potion Pop/QA/Run Full Tour", true)]
        [MenuItem("Potion Pop/QA/Auto-play Current Level", true)]
        static bool IsPlaying() => EditorApplication.isPlaying;

        // ---------------------------------------------------------------------------------------- basics

        /// <summary>Phone-sized capture (1080x2340) of camera + overlay canvases. Returns the absolute path.</summary>
        public static string Shot(string name, int width = 1080, int height = 2340)
        {
            Directory.CreateDirectory(Path.Combine(EditorUtil.ProjectRoot, ShotDir));
            return CaptureTool.Capture(Path.GetFullPath(Path.Combine(EditorUtil.ProjectRoot, ShotDir, name + ".png")), width, height);
        }

        public static string Show(ScreenId id)
        {
            var sm = ScreenManager.Instance;
            if (sm == null) return "no ScreenManager (not playing?)";
            PopupManager.CloseAll(false);
            sm.Show(id);
            return "showing " + id;
        }

        /// <summary>Calls the static Open() of a popup type by simple name (e.g. "DailyRewardPopup"), with default args
        /// (ints get the current level). In-level popups with data: see OpenGamePopup.</summary>
        public static string OpenPopup(string typeName)
        {
            var t = EditorUtil.FindType("PotionPop.UI." + typeName) ?? EditorUtil.FindType("PotionPop.Game." + typeName);
            if (t == null) return "type not found: " + typeName;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Open") continue;
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                        : ps[i].ParameterType == typeof(int) ? (object)Progress.CurrentLevel
                        : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                m.Invoke(null, args);
                return "opened " + typeName;
            }
            return "no static Open() on " + typeName;
        }

        /// <summary>In-level popups with sample data: complete3, complete1, replay, failed, nomoves, pause, stone,
        /// intro:&lt;BoosterType&gt;.</summary>
        public static string OpenGamePopup(string which, string arg = null)
        {
            int level = Progress.CurrentLevel;
            switch (which)
            {
                case "complete3":
                    LevelCompletePopup.Open(new LevelResult { level = level, stars = 3, starsGained = 3, coins = 20, moves = 18, winStreak = 4, cardId = null }, 20, null);
                    return "complete (3 stars)";
                case "complete1":
                    LevelCompletePopup.Open(new LevelResult { level = level, stars = 1, starsGained = 1, coins = 20, moves = 41, winStreak = 1 }, 24, null);
                    return "complete (1 star)";
                case "replay":
                    LevelCompletePopup.Open(new LevelResult { level = Math.Max(1, level - 3), stars = 3, previousBest = 2, starsGained = 1, coins = 10, moves = 17, replay = true }, 19, null);
                    return "complete (replay, new best)";
                case "failed":
                    LevelFailedPopup.Open(level, 3, false, true, Math.Max(0, Lives.Hearts - 1), null, null);
                    return "failed";
                case "nomoves":
                    NoMovesPopup.Open(new NoMovesPopup.Options { canUndo = true, canAddBottle = true, adContinue = true, stoneBottle = 2 }, null, null, null, null);
                    return "no moves";
                case "pause":
                    PausePopup.Open(level, Difficulty.IsHard(level), DS.Colors.Secondary, true, null, null);
                    return "pause";
                case "stone":
                    StonePopup.Open(3, null);
                    return "stone";
                case "intro":
                    var type = string.IsNullOrEmpty(arg) ? BoosterType.Wand : (BoosterType)Enum.Parse(typeof(BoosterType), arg);
                    BoosterIntroPopup.Open(type, null);
                    return "booster intro " + type;
                default:
                    return "unknown game popup: " + which;
            }
        }

        public static string StartLevel(int level, bool crystal = false, bool rainbow = false)
        {
            PopupManager.CloseAll(false);
            GameScreen.StartLevel(level, new PreBoosters { crystal = crystal, rainbow = rainbow });
            return "starting level " + level;
        }

        static GameSession Session => GameScreen.Instance != null ? GameScreen.Instance.Session : null;
        static BoardView View => GameScreen.Instance != null ? GameScreen.Instance.Board : null;

        // ---------------------------------------------------------------------------------------- autoplay

        /// <summary>One solver move through the session's pour entry point. Returns a description.</summary>
        public static string AutoMove()
        {
            var s = Session;
            if (s == null || s.Board == null) return "no session";
            if (s.State != SessionState.Playing) return "state " + s.State;
            if (s.Board.IsWon) return "won";
            if (s.IsBusy) return "busy";
            var view = View;
            if (view != null && view.IsAnimating) return "animating";
            LevelSolver.Result res;
            try { res = LevelSolver.Solve(s.Board.Clone(), 200000); }
            catch (Exception e) { return "solver error " + e.Message; }
            if (!res.Solved || res.Moves.Count == 0) return "no solution (exhausted=" + res.Exhausted + ", useful=" + s.Board.HasUsefulMove + ")";
            var m = res.Moves[0];
            s.HandlePourRequest(m.from, m.to);
            return $"poured {m} moves={s.Moves} combo={s.Combo} left={res.Moves.Count - 1}";
        }

        static int _autoMovesLeft;
        static double _nextAt;
        static float _interval;

        /// <summary>Plays up to `moves` solver moves, one every `interval` seconds (waits for animations).</summary>
        public static string AutoPlay(int moves = 400, float interval = 0.55f)
        {
            _autoMovesLeft = moves;
            _interval = interval;
            _nextAt = EditorApplication.timeSinceStartup + interval;
            AutoPlaying = true;
            EditorApplication.update -= AutoTick;
            EditorApplication.update += AutoTick;
            return "autoplay started";
        }

        public static void StopAutoPlay()
        {
            EditorApplication.update -= AutoTick;
            AutoPlaying = false;
        }

        public static bool AutoPlaying { get; private set; }
        public static string LastAutoResult { get; private set; } = "";

        static void AutoTick()
        {
            if (!EditorApplication.isPlaying || _autoMovesLeft <= 0)
            {
                FinishAuto("stopped: moves exhausted or not playing");
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextAt) return;
            var s = Session;
            if (s == null) { FinishAuto("no session"); return; }
            if (s.State == SessionState.Waiting || s.State == SessionState.Intro)
            {
                _nextAt = EditorApplication.timeSinceStartup + 0.2;
                return;
            }
            if (s.State != SessionState.Playing) { FinishAuto("session state " + s.State); return; }
            if (PopupManager.AnyOpen)
            {
                // A booster intro at level start: dismiss it like a player would; anything else ends the run.
                var top = PopupManager.Top;
                if (top is BoosterIntroPopup intro)
                {
                    intro.OnBack();
                    _nextAt = EditorApplication.timeSinceStartup + 0.6;
                    return;
                }
                FinishAuto("popup open: " + (top != null ? top.GetType().Name : "?"));
                return;
            }
            string r = AutoMove();
            LastAutoResult = r;
            if (r.StartsWith("poured", StringComparison.Ordinal)) _autoMovesLeft--;
            if (r == "won" || r.StartsWith("no solution", StringComparison.Ordinal) || r.StartsWith("solver error", StringComparison.Ordinal))
            {
                FinishAuto(r);
                return;
            }
            _nextAt = EditorApplication.timeSinceStartup + (r.StartsWith("poured", StringComparison.Ordinal) ? _interval : 0.1f);
        }

        static void FinishAuto(string why)
        {
            EditorApplication.update -= AutoTick;
            AutoPlaying = false;
            LastAutoResult = why;
        }

        /// <summary>Clicks the first active, interactable UIButton (top popup first, then the whole UI) whose label
        /// contains `text` (case-insensitive).</summary>
        public static string Click(string text)
        {
            Transform scope = PopupManager.Top != null ? PopupManager.Top.transform : null;
            for (int pass = 0; pass < 2; pass++)
            {
                var buttons = pass == 0 && scope != null
                    ? scope.GetComponentsInChildren<UIButton>(false)
                    : UnityEngine.Object.FindObjectsByType<UIButton>(FindObjectsInactive.Exclude);
                foreach (var b in buttons)
                {
                    if (b == null || b.button == null || !b.button.interactable || b.label == null) continue;
                    if (b.label.text.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    b.button.onClick.Invoke();
                    return "clicked '" + b.label.text + "'";
                }
                if (scope == null) break;
            }
            return "button not found: " + text;
        }

        /// <summary>Selects (adding if needed) a fixed-resolution Game view size, e.g. 1080x2340 phone or 1536x2048 tablet.</summary>
        public static string SetGameViewSize(int width, int height)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var asm = typeof(Editor).Assembly;
                var gvType = asm.GetType("UnityEditor.GameView");
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var instance = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
                var groupType = sizesType.GetProperty("currentGroupType", F).GetValue(instance);
                var group = sizesType.GetMethod("GetGroup", F).Invoke(instance, new object[] { (int)groupType });
                var gType = group.GetType();
                int total = (int)gType.GetMethod("GetTotalCount", F).Invoke(group, null);
                int found = -1;
                for (int i = 0; i < total && found < 0; i++)
                {
                    var sz = gType.GetMethod("GetGameViewSize", F).Invoke(group, new object[] { i });
                    if ((int)sz.GetType().GetProperty("width", F).GetValue(sz) == width &&
                        (int)sz.GetType().GetProperty("height", F).GetValue(sz) == height) found = i;
                }
                if (found < 0)
                {
                    var enumType = asm.GetType("UnityEditor.GameViewSizeType");
                    var ctor = asm.GetType("UnityEditor.GameViewSize").GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) });
                    var size = ctor.Invoke(new object[] { Enum.ToObject(enumType, 1), width, height, $"{width}x{height} (Potion Pop QA)" });
                    gType.GetMethod("AddCustomSize", F).Invoke(group, new[] { size });
                    found = (int)gType.GetMethod("GetTotalCount", F).Invoke(group, null) - 1;
                }
                var gv = EditorWindow.GetWindow(gvType);
                gvType.GetProperty("selectedSizeIndex", F).SetValue(gv, found);
                gv.Repaint();
                return $"game view {width}x{height} (index {found})";
            }
            catch (Exception e)
            {
                return "game view size not set: " + (e.InnerException ?? e).Message;
            }
        }

        // ---------------------------------------------------------------------------------------- scripted tours

        static string[] _script;
        static int _step;
        static double _resumeAt, _readySince;
        public static bool ScriptRunning { get; private set; }
        public static string ScriptLog { get; private set; } = "";

        /// <summary>
        /// Runs a tour asynchronously, one step per editor tick (poll ScriptRunning / ScriptLog). Steps (';' or newline
        /// separated): "show:Shop", "popup:DailyRewardPopup", "gamepopup:complete3|complete1|replay|failed|nomoves|pause|
        /// stone|intro:Wand", "closeall", "level:15[:crystal][:rainbow]", "auto[:moves]", "wait-auto", "shot:name[:WxH]",
        /// "wait:0.8", "setlevel:15", "booster:Undo|Shuffle|Bottle|Wand", "stone" (tap the first stone), "nomoves",
        /// "win[:stars]", "fail", "pause", "state", "lang:pt", "nobanner", "view:1080x2340", "click:label",
        /// "cheat:coins|hearts|boosters", "tutorial-reset", "ready" (wait until the level is playable).
        /// </summary>
        public static string RunScript(string script)
        {
            _script = (script ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _step = 0;
            _resumeAt = 0;
            _readySince = 0;
            ScriptLog = "";
            ScriptRunning = true;
            EditorApplication.update -= ScriptTick;
            EditorApplication.update += ScriptTick;
            return "script started (" + _script.Length + " steps)";
        }

        static void ScriptTick()
        {
            if (!EditorApplication.isPlaying) { EndScript("not playing"); return; }
            if (EditorApplication.timeSinceStartup < _resumeAt) return;
            if (_step >= _script.Length) { EndScript("done"); return; }
            string step = _script[_step].Trim();
            string[] a = step.Split(':');
            string r;
            try
            {
                switch (a[0])
                {
                    case "show": r = Show((ScreenId)Enum.Parse(typeof(ScreenId), a[1])); Pause(0.7); break;
                    case "popup": r = OpenPopup(a[1]); Pause(0.8); break;
                    case "gamepopup": r = OpenGamePopup(a[1], a.Length > 2 ? a[2] : null); Pause(1.6); break;
                    case "closeall": PopupManager.CloseAll(false); r = "closed"; Pause(0.3); break;
                    case "level":
                        r = StartLevel(int.Parse(a[1]), Array.IndexOf(a, "crystal") > 0, Array.IndexOf(a, "rainbow") > 0);
                        Pause(0.5); break;
                    case "auto": r = AutoPlay(a.Length > 1 ? int.Parse(a[1]) : 400); Pause(0.5); break;
                    case "wait-auto":
                        if (AutoPlaying) { _resumeAt = EditorApplication.timeSinceStartup + 0.3; return; }
                        r = "auto finished: " + LastAutoResult; Pause(0.6); break;
                    case "shot":
                        if (a.Length > 2) { var wh = a[2].Split('x'); r = Path.GetFileName(Shot(a[1], int.Parse(wh[0]), int.Parse(wh[1])) ?? "failed"); }
                        else r = Path.GetFileName(Shot(a[1]) ?? "failed");
                        Pause(0.15); break;
                    case "wait": r = "wait"; Pause(double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "setlevel": Progress.SetLevel(int.Parse(a[1])); LevelPrefetch.Clear(); r = "level set"; Pause(0.2); break;
                    case "booster":
                        Session.OnBoosterTapped((BoosterType)Enum.Parse(typeof(BoosterType), a[1]));
                        r = "booster " + a[1]; Pause(1.4); break;
                    case "ready":
                    {
                        // Waits until the level is playable (generated, intro and pre-boosters done), dismissing booster
                        // intros on the way; gives up after 20 s.
                        double now = EditorApplication.timeSinceStartup;
                        if (_readySince <= 0) _readySince = now;
                        var s = Session;
                        bool timedOut = now - _readySince > 20.0;
                        if (s != null && !timedOut)
                        {
                            if (s.State == SessionState.Idle || s.State == SessionState.Waiting || s.State == SessionState.Intro || s.IsBusy)
                            {
                                _resumeAt = now + 0.2;
                                return;
                            }
                            if (PopupManager.Top is BoosterIntroPopup intro)
                            {
                                intro.OnBack();
                                _resumeAt = now + 0.6;
                                return;
                            }
                        }
                        _readySince = 0;
                        r = timedOut ? "ready: TIMEOUT (" + (s != null ? s.State.ToString() : "no session") + ")" : "ready (" + (s != null ? s.State.ToString() : "-") + ")";
                        Pause(0.8);
                        break;
                    }
                    case "stone": r = TapFirstStone(); Pause(1.2); break;
                    case "nomoves": Session.DebugNoMoves(); r = "no moves popup"; Pause(1.4); break;
                    case "win": Session.DebugWin(a.Length > 1 ? int.Parse(a[1]) : 3); r = "win"; Pause(3.5); break;
                    case "fail": PopupManager.CloseAll(false); Session.Fail(); r = "fail"; Pause(1.8); break;
                    case "pause": r = "pause " + Session.Pause(); Pause(0.9); break;
                    case "state": r = State(); break;
                    case "lang": Loc.Language = a[1]; r = "language " + Loc.Language; Pause(0.6); break;
                    // Store captures: no ad banner (in the editor it is Google's placeholder ad).
                    case "nobanner": AdsService.HideBanner(); r = "banner hidden"; Pause(0.4); break;
                    case "view":
                    {
                        var wh = a[1].Split('x');
                        r = SetGameViewSize(int.Parse(wh[0]), int.Parse(wh[1]));
                        Pause(1.0);
                        break;
                    }
                    case "click": r = Click(step.Substring(6)); Pause(1.2); break;
                    case "tutorial-reset":
                        SaveSystem.Data.tutorialDone = false;
                        SaveSystem.Data.seenTutorials.Clear();
                        SaveSystem.MarkDirty();
                        r = "tutorials reset"; Pause(0.1); break;
                    case "cheat":
                        if (a[1] == "coins") Economy.AddCoins(5000, "qa");
                        else if (a[1] == "hearts") Lives.RefillFull();
                        else if (a[1] == "boosters") foreach (BoosterType b in Enum.GetValues(typeof(BoosterType))) Economy.AddBooster(b, 5);
                        r = "cheat " + a[1]; Pause(0.2); break;
                    default: r = "unknown step"; break;
                }
            }
            catch (Exception e) { r = "ERROR " + (e.InnerException ?? e).Message; }
            ScriptLog += step + " => " + r + "\n";
            _step++;
        }

        static void Pause(double seconds) => _resumeAt = EditorApplication.timeSinceStartup + seconds;

        static void EndScript(string why)
        {
            EditorApplication.update -= ScriptTick;
            ScriptRunning = false;
            ScriptLog += "[" + why + "]\n";
        }

        /// <summary>Taps the first stone-wrapped bottle (the session opens the stone popup).</summary>
        static string TapFirstStone()
        {
            var s = Session;
            if (s == null || s.Board == null) return "no session";
            for (int i = 0; i < s.Board.Count; i++)
            {
                if (!s.Board[i].IsLocked) continue;
                s.OnLockedTapped(i);
                return "tapped stone " + i;
            }
            return "no stone on this board";
        }

        /// <summary>Snapshot of the current state for logs.</summary>
        public static string State()
        {
            var sm = ScreenManager.Instance;
            var s = Session;
            return $"screen={(sm != null ? sm.Current.ToString() : "-")} popup={(PopupManager.Top != null ? PopupManager.Top.GetType().Name : "-")} " +
                   $"level={Progress.CurrentLevel} coins={Economy.Coins} hearts={Lives.Hearts} " +
                   (s != null ? $"session={s.State} lvl={s.Level} moves={s.Moves} pours={s.PoursMade} combo={s.Combo} busy={s.IsBusy} " +
                                $"won={(s.Board != null && s.Board.IsWon)} board={(s.Board != null ? s.Board.ToString() : "-")}" : "session=-") +
                   $" auto={AutoPlaying} last='{LastAutoResult}' frame={Time.frameCount}";
        }
    }
}
