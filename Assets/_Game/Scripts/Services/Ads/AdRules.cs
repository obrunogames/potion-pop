using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>Interstitial frequency rules (GDD §6) as pure functions.</summary>
    public static class AdRules
    {
        /// <summary>No interstitial before the end of this level.</summary>
        public const int InterstitialFromLevel = 6;
        /// <summary>At most one interstitial every N finished levels (wins and losses count).</summary>
        public const int InterstitialEveryLevels = 2;
        /// <summary>Minimum time between two interstitials.</summary>
        public const long InterstitialMinGapSeconds = 90;
        /// <summary>Never an interstitial this soon after a rewarded ad.</summary>
        public const long NoInterstitialAfterRewardedSeconds = 30;

        /// <summary>
        /// May an interstitial be shown now? <paramref name="levelsSinceInterstitial"/> already counts the level that just
        /// ended. Timestamps are unix seconds (0 = never); a timestamp in the future (clock moved back) does not block.
        /// </summary>
        public static bool ShouldShowInterstitial(int level, int levelsSinceInterstitial, long lastInterstitialAt, long lastRewardedAt, long now) =>
            WhyNoInterstitial(level, levelsSinceInterstitial, lastInterstitialAt, lastRewardedAt, now) == null;

        /// <summary>Why no interstitial would be shown (null = allowed). For logs and debug UI.</summary>
        public static string WhyNoInterstitial(int level, int levelsSinceInterstitial, long lastInterstitialAt, long lastRewardedAt, long now)
        {
            if (level < InterstitialFromLevel) return "level " + level + " < " + InterstitialFromLevel;
            if (levelsSinceInterstitial < InterstitialEveryLevels)
                return levelsSinceInterstitial + " of " + InterstitialEveryLevels + " levels since the last interstitial";
            if (lastInterstitialAt > 0)
            {
                long elapsed = now - lastInterstitialAt;
                if (elapsed >= 0 && elapsed < InterstitialMinGapSeconds) return elapsed + " s since the last interstitial";
            }
            if (lastRewardedAt > 0)
            {
                long elapsed = now - lastRewardedAt;
                if (elapsed >= 0 && elapsed < NoInterstitialAfterRewardedSeconds) return elapsed + " s since a rewarded ad";
            }
            return null;
        }

        /// <summary>Exponential backoff: min, 2·min, 4·min... capped at max.</summary>
        public static float BackoffSeconds(int failures, float min, float max)
        {
            if (failures <= 0) return 0f;
            return Mathf.Min(max, min * Mathf.Pow(2f, Mathf.Min(failures - 1, 12)));
        }
    }
}
