using System;
using System.Collections.Generic;

namespace PotionPop
{
    /// <summary>
    /// The whole persistent state of a player (JsonUtility-serializable: fields only, no dictionaries).
    /// CONTRACT (owner: Core agent): existing fields may not be renamed/removed (cloud saves depend on them);
    /// new fields may be added with sensible defaults. Bump <see cref="CurrentVersion"/> when migrating.
    /// </summary>
    [Serializable]
    public class PlayerData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;

        // identity
        public string playerId = "";        // 7-digit display id, generated on first launch
        public string playerName = "";      // empty = "Player" localized
        public string avatar = "puppy";     // avatar_<name> sprite

        // progression
        public int level = 1;               // next level to play
        public int winStreak;
        public int bestStreak;
        public int levelsWon;
        public int levelsLost;
        public int hardLevelsWon;
        public int totalMatches;            // (Shelf Pop field, unused)
        public int maxCombo;                // longest chain of bottles completed in a row
        /// <summary>Best star rating per level: char i = stars of level i + 1 ('0' = not won yet, '1'..'3').</summary>
        public string levelStars = "";
        public int totalBottles;            // bottles completed (corked)
        public int totalPours;
        public int replaysWon;
        public bool tutorialDone;
        public List<string> seenTutorials = new List<string>();   // booster tutorials etc.
        public int lastSeenArea;            // area number shown in the "area unlocked" popup

        // currencies
        public int coins = 300;
        public long totalStars;
        public int starChestProgress;
        public int weekId;
        public long weeklyStars;

        // hearts
        public int hearts = 5;
        public long nextHeartAt;            // unix seconds; 0 when full
        public long infiniteHeartsUntil;    // unix seconds

        // boosters
        public int undo = 3;
        public int wand = 1;
        public int bottle = 1;
        public int shuffle = 2;
        public int crystal = 1;
        public int rainbow = 1;

        // continue pricing (resets every level)
        public int continuesThisLevel;
        public int levelInProgress;         // level started (Progress.ReportLevelStart) but not won/lost yet; 0 = none

        // collection
        public List<string> cards = new List<string>();
        public List<string> albumsClaimed = new List<string>();

        // daily systems
        public int dailyDay;                // 0..6 next day to claim
        public int dailyLastClaimDay = -1;  // TimeUtil day index of last claim
        public int spinDay = -1;
        public bool freeSpinUsed;
        public int adSpinsUsed;
        public int questDay = -1;
        public List<QuestState> quests = new List<QuestState>();
        public int shopAdsDay = -1;
        public int shopAdsUsed;
        public int freeGiftDay = -1;

        // ads
        public int levelsSinceInterstitial;
        public long lastInterstitialAt;
        public bool loginNudgeShown;

        // settings
        public bool musicOn = true;
        public bool soundOn = true;
        public bool vibrationOn = true;
        public string language = "";        // "" = follow device

        // global ranking
        public List<string> blockedUids = new List<string>();   // Firebase uids the player blocked / reported (rows hidden)

        // cloud
        public long updatedAt;              // unix seconds of the last local change (used by cloud merge)
        /// <summary>Firebase uid this save was last merged with ("" = guest save never synced). A save owned by another
        /// account is never merged into (or uploaded over) a different account's cloud document.</summary>
        public string cloudUid = "";
    }

    [Serializable]
    public class QuestState
    {
        public int kind;                    // (int)QuestKind
        public int target;
        public int progress;
        public bool claimed;
    }
}
