// Data types shared by the game flow (Level Start popup / Worlds screen → GameScreen → GameSession).
namespace PotionPop.Game
{
    /// <summary>Effects chosen in the Level Start popup. The popup already consumed inventory / streak freebies.</summary>
    public struct PreBoosters
    {
        /// <summary>Crystal Ball: every hidden ("?") color is revealed when the level starts.</summary>
        public bool crystal;
        /// <summary>Rainbow Potion: one whole color is removed when the level starts.</summary>
        public bool rainbow;
    }

    /// <summary>Argument passed to ScreenManager.Show(ScreenId.Game, ...).</summary>
    public sealed class LevelLaunch
    {
        public int level;
        public PreBoosters pre;
        /// <summary>Replaying an already-won level from the Worlds screen (only better stars are rewarded).</summary>
        public bool replay;
    }

    /// <summary>Lifecycle of one level attempt (GameSession).</summary>
    public enum SessionState
    {
        /// <summary>No level loaded.</summary>
        Idle,
        /// <summary>Level generated; waiting for popups / the screen transition to finish before the intro.</summary>
        Waiting,
        /// <summary>Bottles fly in + "Level N" banner.</summary>
        Intro,
        /// <summary>Player input accepted.</summary>
        Playing,
        Won,
        Lost,
    }
}
