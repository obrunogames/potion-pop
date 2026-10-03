using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Hearts (GDD §5): max 5, one regenerates every 30 min, refill for 500 coins, optional "infinite hearts" timer.
    /// Regeneration is computed from timestamps (works while the app is closed); <see cref="Tick"/> only applies it.
    /// </summary>
    public static class Lives
    {
        public const int Max = 5;
        public const int RegenSeconds = 1800;
        public const int RefillPrice = 500;
        public static event Action OnChanged;

        static bool _infiniteWasActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnChanged = null;
            _infiniteWasActive = false;
        }

        /// <summary>Current hearts after applying regeneration.</summary>
        public static int Hearts
        {
            get
            {
                Apply();
                return SaveSystem.Data.hearts;
            }
        }

        public static bool IsFull => Hearts >= Max;
        public static bool HasInfinite => SaveSystem.Data.infiniteHeartsUntil > TimeUtil.Now;
        public static long InfiniteRemainingSeconds => Math.Max(0, SaveSystem.Data.infiniteHeartsUntil - TimeUtil.Now);

        /// <summary>Seconds until the next heart (0 when full).</summary>
        public static long SecondsToNext
        {
            get
            {
                Apply();
                var d = SaveSystem.Data;
                return d.hearts >= Max ? 0 : Math.Max(0, d.nextHeartAt - TimeUtil.Now);
            }
        }

        /// <summary>Can the player start a level (hearts &gt; 0 or infinite)?</summary>
        public static bool CanPlay => HasInfinite || Hearts > 0;

        /// <summary>Removes one heart (no-op with infinite hearts). Returns false if there was none.</summary>
        public static bool TryConsume()
        {
            if (HasInfinite) return true;
            Apply();
            var d = SaveSystem.Data;
            if (d.hearts <= 0) return false;
            if (d.hearts >= Max) d.nextHeartAt = TimeUtil.Now + RegenSeconds;   // regen starts with the first loss
            d.hearts--;
            Changed();
            return true;
        }

        public static void Add(int n)
        {
            if (n <= 0) return;
            Apply();
            var d = SaveSystem.Data;
            int old = d.hearts;
            d.hearts = Math.Min(Max, d.hearts + n);
            if (d.hearts >= Max) d.nextHeartAt = 0;
            if (d.hearts != old) Changed();
        }

        public static void RefillFull()
        {
            var d = SaveSystem.Data;
            if (d.hearts == Max && d.nextHeartAt == 0) return;
            d.hearts = Max;
            d.nextHeartAt = 0;
            Changed();
        }

        /// <summary>Extends (or starts) the infinite-hearts timer.</summary>
        public static void AddInfinite(int seconds)
        {
            if (seconds <= 0) return;
            var d = SaveSystem.Data;
            long now = TimeUtil.Now;
            d.infiniteHeartsUntil = Math.Max(d.infiniteHeartsUntil, now) + seconds;
            _infiniteWasActive = true;
            Changed();
        }

        /// <summary>Spends <see cref="RefillPrice"/> coins to refill all hearts. False when full or too poor.</summary>
        public static bool TryBuyRefill()
        {
            if (IsFull) return false;
            if (!Economy.TrySpendCoins(RefillPrice, "hearts_refill")) return false;
            RefillFull();
            return true;
        }

        /// <summary>Applies regeneration; call once per second from UI (cheap).</summary>
        public static void Tick()
        {
            Apply();
            bool infinite = HasInfinite;
            if (infinite != _infiniteWasActive)
            {
                _infiniteWasActive = infinite;
                OnChanged?.Invoke();   // infinite timer ended (or started elsewhere)
            }
        }

        /// <summary>Grants every heart regenerated since nextHeartAt. Raises OnChanged when something changed.</summary>
        static void Apply()
        {
            var d = SaveSystem.Data;
            long now = TimeUtil.Now;
            bool changed = false;

            if (d.hearts >= Max)
            {
                if (d.nextHeartAt != 0) { d.nextHeartAt = 0; changed = true; }
            }
            else
            {
                // Missing or tampered timer (clock moved backwards): restart a full interval from now.
                if (d.nextHeartAt <= 0 || d.nextHeartAt - now > RegenSeconds)
                {
                    d.nextHeartAt = now + RegenSeconds;
                    changed = true;
                }
                if (now >= d.nextHeartAt)
                {
                    long gained = 1 + (now - d.nextHeartAt) / RegenSeconds;
                    int hearts = (int)Math.Min(Max, d.hearts + gained);
                    d.nextHeartAt = hearts >= Max ? 0 : d.nextHeartAt + gained * RegenSeconds;
                    d.hearts = hearts;
                    changed = true;
                }
            }
            // Regeneration is automatic housekeeping: it must not stamp the save as "newer" for the cloud merge.
            if (changed)
            {
                SaveSystem.MarkDirtyHousekeeping();
                OnChanged?.Invoke();
            }
        }

        static void Changed()
        {
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
        }
    }
}
