// Data types shared by the game flow (Level Start popup / Worlds screen → GameScreen → GameSession).
using PotionPop.Game.Board;
using UnityEngine;

namespace PotionPop.Game
{
    /// <summary>Effects chosen in the Level Start popup. The popup already consumed inventory / streak freebies.</summary>
    public struct PreBoosters
    {
        /// <summary>Crystal Ball: every hidden ("?") color is revealed when the level starts.</summary>
        public bool crystal;
        /// <summary>Rainbow Potion: one whole color is removed when the level starts.</summary>
        public bool rainbow;

        public bool Any => crystal || rainbow;
    }

    /// <summary>Argument passed to ScreenManager.Show(ScreenId.Game, ...).</summary>
    public sealed class LevelLaunch
    {
        public int level;
        public PreBoosters pre;
        /// <summary>Replaying an already-won level from the Worlds screen (only better stars are rewarded). The session
        /// also treats any level below Progress.CurrentLevel as a replay.</summary>
        public bool replay;
    }

    /// <summary>Lifecycle of one level attempt (GameSession).</summary>
    public enum SessionState
    {
        /// <summary>No level loaded.</summary>
        Idle,
        /// <summary>The level is being generated (worker thread) or waits for popups / the screen transition to finish
        /// before the intro.</summary>
        Waiting,
        /// <summary>Bottles drop in + "Level N" banner.</summary>
        Intro,
        /// <summary>Player input accepted (still halted while a popup, a full-screen ad or the app pause is on).</summary>
        Playing,
        Won,
        Lost,
    }

    /// <summary>Views driven by the session (built and owned by GameScreen). Any of them may be null: the session
    /// keeps working without a view (tests, a view that failed to build).</summary>
    public sealed class GameViews
    {
        public MonoBehaviour host;
        public BoardView board;
        /// <summary>Rect the board lives in (between the HUD and the booster bar).</summary>
        public RectTransform boardArea;
        public GameHud hud;
        public BoosterBar boosters;
        public TutorialOverlay tutorial;
        /// <summary>Screen-level layer above board and HUD (banners, big texts); never blocks input.</summary>
        public RectTransform overlay;
    }
}
