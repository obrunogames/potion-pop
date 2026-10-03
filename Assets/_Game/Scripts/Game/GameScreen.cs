// TEMPORARY STUB (replaced by the gameplay module): keeps the meta screens compiling while the board is rebuilt.
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game
{
    public class GameScreen : UIScreen
    {
        public static GameScreen Instance { get; private set; }
        public override ScreenId Id => ScreenId.Game;
        public override bool ShowBottomNav => false;
        public override bool ShowTopBar => false;
        public override Music Music => Music.Game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>True while a level is being played.</summary>
        public bool IsPlaying => false;

        public override void Build() { Instance = this; }

        /// <summary>Starts a level (Level Start popup / Worlds screen).</summary>
        public static void StartLevel(int level, PreBoosters pre, bool replay = false)
        {
            var sm = ScreenManager.Instance;
            if (sm != null) sm.Show(ScreenId.Game, new LevelLaunch { level = level, pre = pre, replay = replay });
        }
    }
}
