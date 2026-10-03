// ============================================================================================================
// Persistent chrome: builds the TopBar + BottomNav on ScreenManager.ChromeLayer and slides them in/out whenever
// the screen changes, following UIScreen.ShowTopBar / ShowBottomNav. The nav highlight follows the current tab.
// ============================================================================================================
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Persistent chrome on ScreenManager.ChromeLayer: TopBar + BottomNav, shown/hidden per screen
    /// (UIScreen.ShowTopBar / ShowBottomNav) when ScreenManager.OnScreenChanged fires.</summary>
    public static class Chrome
    {
        static ScreenManager _screens;
        static bool _firstShow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _screens = null;
            _firstShow = false;
        }

        public static void Build(ScreenManager screens)
        {
            if (screens == null || screens.ChromeLayer == null) return;
            if (_screens != null) _screens.OnScreenChanged -= OnScreenChanged;
            _screens = screens;
            _firstShow = true;

            if (TopBar.Instance == null) TopBar.Create(screens.ChromeLayer);
            if (BottomNav.Instance == null) BottomNav.Create(screens.ChromeLayer);
            // Hidden until the first screen shows (they slide in together with it, behind the fading splash).
            if (TopBar.Instance != null) TopBar.Instance.SetVisible(false, false);
            if (BottomNav.Instance != null) BottomNav.Instance.SetVisible(false, false);

            screens.OnScreenChanged += OnScreenChanged;
            if (screens.CurrentScreen != null) OnScreenChanged(screens.Current);
        }

        /// <summary>Height the top bar covers below the safe-area top (screens keep content below it).</summary>
        public static float TopInset => TopBar.Height;

        /// <summary>Height the bottom nav covers above the safe-area bottom.</summary>
        public static float BottomInset => BottomNav.Height;

        static void OnScreenChanged(ScreenId id)
        {
            if (_screens == null) return;
            bool first = _firstShow;
            _firstShow = false;

            var bottom = BottomNav.Instance;
            if (bottom != null) bottom.Select(id, !first);
            if (first)
            {
                // Slide in shortly after the first screen (behind the fading splash). The delayed calls re-read the
                // current screen, so a screen switch during the delay never leaves the chrome on a screen that hides it.
                var bar = TopBar.Instance;
                if (bar != null) Tween.Delay(0.12f, ApplyTopBar).SetLink(bar);
                if (bottom != null) Tween.Delay(0.18f, ApplyBottomNav).SetLink(bottom);
                return;
            }
            ApplyTopBar();
            ApplyBottomNav();
        }

        /// <summary>Slides the top bar in/out to match the current screen.</summary>
        static void ApplyTopBar()
        {
            var bar = TopBar.Instance;
            if (_screens == null || bar == null) return;
            var screen = _screens.CurrentScreen;
            bool top = screen == null || screen.ShowTopBar;
            if (bar.IsShown != top) bar.SetVisible(top, true);
        }

        /// <summary>Slides the bottom nav in/out to match the current screen (refreshes its badges when it stays).</summary>
        static void ApplyBottomNav()
        {
            var bottom = BottomNav.Instance;
            if (_screens == null || bottom == null) return;
            var screen = _screens.CurrentScreen;
            bool nav = screen == null || screen.ShowBottomNav;
            if (bottom.IsShown != nav) bottom.SetVisible(nav, true);
            else if (nav) bottom.RefreshBadges();
        }
    }
}
