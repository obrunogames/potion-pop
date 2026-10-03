using System;
using System.Globalization;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Wall-clock helpers. All persisted timestamps are UTC unix seconds; day/week indices are computed in the
    /// device's local time zone so daily systems roll over at local midnight.
    /// </summary>
    public static class TimeUtil
    {
        const long SecondsPerDay = 86400;
        /// <summary>Day index of Monday 1970-01-05 (1970-01-01 was a Thursday).</summary>
        const int FirstMondayDay = 4;

        /// <summary>Added to the clock (debug menu / tests can fast-forward time).</summary>
        public static long DebugOffsetSeconds;

        /// <summary>
        /// Time zone used for day/week indices. Null = device local time. Tests set <see cref="TimeZoneInfo.Utc"/>
        /// so results do not depend on the machine running them.
        /// </summary>
        public static TimeZoneInfo TimeZoneOverride;

        // One-entry cache of the zone's UTC offset. UI countdowns query Today / SecondsToMidnight every frame and
        // TimeZoneInfo.GetUtcOffset walks the adjustment rules (allocating on some runtimes). Offsets only change on
        // 15-minute UTC boundaries (every real-world transition is), so a 15-minute bucket is exact.
        const long OffsetBucketSeconds = 900;
        static TimeZoneInfo _cachedZone;
        static long _cachedBucket = long.MinValue;
        static long _cachedOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DebugOffsetSeconds = 0;
            TimeZoneOverride = null;
            ClearZoneCache();
        }

        /// <summary>Forgets the cached time zone data (call when the app resumes: the device zone may have changed).</summary>
        public static void ClearZoneCache()
        {
            _cachedZone = null;
            _cachedBucket = long.MinValue;
            _cachedOffset = 0;
            try { TimeZoneInfo.ClearCachedData(); } catch (Exception) { /* not supported: keep the runtime cache */ }
        }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + DebugOffsetSeconds;

        /// <summary>Local calendar day number (days since 1970-01-01 in the device time zone).</summary>
        public static int DayIndex(long unixSeconds) => (int)FloorDiv(ToLocalSeconds(unixSeconds), SecondsPerDay);

        public static int Today => DayIndex(Now);

        /// <summary>Week number (weeks since 1970-01-05, Monday-based, local time). Used by the weekly contest.</summary>
        public static int WeekIndex => WeekIndexOf(Now);

        /// <summary>Monday-based local week number of a timestamp.</summary>
        public static int WeekIndexOf(long unixSeconds) => (int)FloorDiv(DayIndex(unixSeconds) - FirstMondayDay, 7);

        /// <summary>0 = Monday ... 6 = Sunday (local time).</summary>
        public static int DayOfWeek(long unixSeconds) => (int)FloorMod(DayIndex(unixSeconds) - FirstMondayDay, 7);

        /// <summary>Seconds until the next local midnight (1..86400).</summary>
        public static long SecondsToMidnight => SecondsToMidnightFrom(Now);

        /// <summary>Seconds until the next local Monday 00:00 (weekly contest reset).</summary>
        public static long SecondsToNextWeek
        {
            get
            {
                long now = Now;
                return SecondsToMidnightFrom(now) + (6 - DayOfWeek(now)) * SecondsPerDay;
            }
        }

        public static long SecondsToMidnightFrom(long unixSeconds)
        {
            long intoDay = FloorMod(ToLocalSeconds(unixSeconds), SecondsPerDay);
            return SecondsPerDay - intoDay;
        }

        /// <summary>"05:50" (mm:ss, minutes may exceed 59). Rounds up so a countdown only shows 00:00 when it is over.</summary>
        public static string FormatMMSS(float seconds)
        {
            if (float.IsNaN(seconds) || seconds <= 0f) return "00:00";
            if (seconds > 5999999f) seconds = 5999999f;   // keeps the cast sane (also for +Infinity)
            long total = (long)Math.Ceiling(seconds - 0.0001f);
            long m = total / 60, s = total % 60;
            return m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Compact duration: "2d 1h", "10h 4m", "22:34" (under one hour).</summary>
        public static string FormatDuration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds >= SecondsPerDay)
            {
                long d = seconds / SecondsPerDay, h = seconds % SecondsPerDay / 3600;
                return Loc.Has("time.fmt_dh") ? Loc.T("time.fmt_dh", d, h) : d + "d " + h + "h";
            }
            if (seconds >= 3600)
            {
                long h = seconds / 3600, m = seconds % 3600 / 60;
                return Loc.Has("time.fmt_hm") ? Loc.T("time.fmt_hm", h, m) : h + "h " + m + "m";
            }
            return FormatMMSS(seconds);
        }

        // ------------------------------------------------------------------ internals

        static long ToLocalSeconds(long unixSeconds)
        {
            try
            {
                var zone = TimeZoneOverride ?? TimeZoneInfo.Local;
                long bucket = FloorDiv(unixSeconds, OffsetBucketSeconds);
                if (!ReferenceEquals(zone, _cachedZone) || bucket != _cachedBucket)
                {
                    var utc = DateTimeOffset.FromUnixTimeSeconds(ClampUnix(unixSeconds)).UtcDateTime;
                    _cachedOffset = (long)zone.GetUtcOffset(utc).TotalSeconds;
                    _cachedZone = zone;
                    _cachedBucket = bucket;
                }
                return unixSeconds + _cachedOffset;
            }
            catch (Exception)
            {
                // Broken tz data on some devices: fall back to UTC rather than crashing the daily systems.
                return unixSeconds;
            }
        }

        static long ClampUnix(long s)
        {
            // DateTimeOffset.FromUnixTimeSeconds accepts years 0001..9999 only.
            const long min = -62135596800L, max = 253402300799L;
            return s < min ? min : s > max ? max : s;
        }

        static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        static long FloorMod(long a, long b)
        {
            long m = a % b;
            return m < 0 ? m + b : m;
        }
    }
}
