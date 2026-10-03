using System;
using UnityEngine;

namespace PotionPop
{
    /// <summary>Player identity shown in Profile / Ranking: display name, avatar and the 7-digit player id.</summary>
    public static class PlayerProfile
    {
        public const int MaxNameLength = 16;
        /// <summary>Avatar of new players (Luna, the witch kitten mascot).</summary>
        public const string DefaultAvatar = "luna";
        /// <summary>Avatar ids, in picker order (the four magic ones first); sprite = "avatar_" + id.</summary>
        public static readonly string[] Avatars =
        {
            "luna", "owl", "dragon", "unicorn",
            "puppy", "kitten", "bunny", "duck", "panda", "fox", "bear", "frog",
        };
        /// <summary>The magical avatars (Luna and her friends) get a sparkle in the picker.</summary>
        public static bool IsMagicAvatar(string id) => id == "luna" || id == "owl" || id == "dragon" || id == "unicorn";

        /// <summary>A known avatar id ("puppy", "avatar_puppy" → "puppy"), or <see cref="DefaultAvatar"/>.</summary>
        public static string NormalizeAvatar(string id)
        {
            if (string.IsNullOrEmpty(id)) return DefaultAvatar;
            if (id.StartsWith("avatar_", StringComparison.Ordinal)) id = id.Substring(7);
            return Array.IndexOf(Avatars, id) >= 0 ? id : DefaultAvatar;
        }
        public static event Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnChanged = null;

        public static string PlayerId => SaveSystem.Data.playerId;

        /// <summary>Saved name, or the localized default ("Player") when empty or refused by <see cref="NameFilter"/>.</summary>
        public static string DisplayName
        {
            get
            {
                string n = Displayable(SaveSystem.Data.playerName);   // names saved before the glyph filter existed
                // Names saved before the name filter existed (or restored from the cloud) fall back to the default too.
                return string.IsNullOrEmpty(n) || NameFilter.IsOffensive(n) ? Loc.T("common.player") : n;
            }
        }

        public static bool HasCustomName => !string.IsNullOrEmpty(SaveSystem.Data.playerName);

        /// <summary>
        /// Trims, collapses control characters and caps the length. Empty → default name. Returns false (nothing saved)
        /// when <see cref="NameFilter"/> refuses the name: other players see it in the global ranking.
        /// </summary>
        public static bool SetName(string name)
        {
            string clean = Sanitize(name);
            if (NameFilter.IsOffensive(clean)) return false;
            var d = SaveSystem.Data;
            if (d.playerName == clean) return true;
            d.playerName = clean;
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
            return true;
        }

        public static string Avatar => NormalizeAvatar(SaveSystem.Data.avatar);
        public static string AvatarSprite => "avatar_" + Avatar;

        public static void SetAvatar(string avatarId)
        {
            if (Array.IndexOf(Avatars, avatarId) < 0) return;
            var d = SaveSystem.Data;
            if (d.avatar == avatarId) return;
            d.avatar = avatarId;
            SaveSystem.MarkDirty();
            OnChanged?.Invoke();
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var chars = new System.Text.StringBuilder(name.Length);
            foreach (char c in name.Trim())
                if (!char.IsControl(c) && c != '<' && c != '>') chars.Append(c);   // no TMP rich-text tags
            string s = Displayable(chars.ToString());   // no glyph → no empty boxes in Profile / Ranking
            if (s.Length <= MaxNameLength) return s;
            int cut = MaxNameLength;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;   // never split an emoji / surrogate pair
            return s.Substring(0, cut).TrimEnd();
        }

        /// <summary>
        /// Drops characters the UI font (Lilita One + its Liberation Sans fallback) cannot draw — emoji (surrogate pairs,
        /// joiners, variation selectors), CJK... — so names never render as empty boxes. Used for the saved name and for
        /// names coming from other players / Google accounts. Returns the trimmed result ("" when nothing is drawable).
        /// </summary>
        public static string Displayable(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            TMPro.TMP_FontAsset font = null;
            try { font = PotionPop.UI.DS.Font; }
            catch (Exception) { font = null; }
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsSurrogate(c)) continue;   // astral plane: emoji etc., never in our font atlases
                if (!char.IsWhiteSpace(c) && font != null)
                {
                    bool has;
                    // Dynamic atlases: tryAddCharacter adds the glyph from the source font when it exists there.
                    try { has = font.HasCharacter(c, true, true); }
                    catch (Exception) { has = true; }
                    if (!has) continue;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
