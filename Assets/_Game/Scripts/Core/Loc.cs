using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PotionPop
{
    /// <summary>
    /// Localized strings. Tables: every CSV in Resources/Loc (merged), header "key,en,pt,es", RFC4180 quoting,
    /// "\n" inside a cell means a newline. Missing cell → English → the key itself.
    /// Language: saved choice (PlayerData.language) else device language (Portuguese→pt, Spanish→es, else en).
    /// Works in edit mode too (editor tools, EditMode tests): tables are loaded lazily with Resources.LoadAll.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Codes = { "en", "pt", "es" };
        public static IReadOnlyList<string> Languages => Codes;
        public static event Action OnLanguageChanged;

        const string ResourceFolder = "Loc";
        const int English = 0;

        /// <summary>key → one value per entry of <see cref="Codes"/> (null = missing in that language).</summary>
        static Dictionary<string, string[]> _table;
        static int _lang = -1;
        static NumberFormatInfo _numberFormat;
        static HashSet<string> _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _table = null;
            _lang = -1;
            _numberFormat = null;
            _warned = null;
            OnLanguageChanged = null;
        }

        // ------------------------------------------------------------------ language

        /// <summary>Current language code ("en", "pt", "es"). Setting it persists the choice in the save.</summary>
        public static string Language
        {
            get
            {
                EnsureLanguage();
                return Codes[_lang];
            }
            set
            {
                int index = IndexOfCode(value);
                if (index < 0)
                {
                    Debug.LogWarning("[Loc] Unsupported language: " + value);
                    return;
                }
                var data = SaveSystem.Data;
                if (data.language != Codes[index])
                {
                    data.language = Codes[index];
                    SaveSystem.MarkDirty();
                }
                SetCurrent(index);
            }
        }

        /// <summary>True when the player never picked a language (the game follows the device).</summary>
        public static bool FollowsDevice => string.IsNullOrEmpty(SaveSystem.Data.language);

        /// <summary>Forgets the saved choice and follows the device language again.</summary>
        public static void FollowDeviceLanguage()
        {
            var data = SaveSystem.Data;
            if (!string.IsNullOrEmpty(data.language))
            {
                data.language = "";
                SaveSystem.MarkDirty();
            }
            SetCurrent(IndexOfCode(DeviceLanguage()));
        }

        /// <summary>Supported code for the device language (Portuguese → pt, Spanish → es, anything else → en).</summary>
        public static string DeviceLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Spanish: return "es";
                default: return "en";
            }
        }

        /// <summary>"English", "Português", "Español" (always written in that language).</summary>
        public static string LanguageDisplayName(string code)
        {
            int index = IndexOfCode(code);
            if (index < 0) return code ?? "";
            string key = "lang." + Codes[index];
            if (Has(key)) return T(key);
            switch (index)
            {
                case 1: return "Português";
                case 2: return "Español";
                default: return "English";
            }
        }

        // ------------------------------------------------------------------ lookup

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureLanguage();
            if (_table.TryGetValue(key, out var row))
            {
                string v = row[_lang];
                if (!string.IsNullOrEmpty(v)) return v;
                v = row[English];
                if (!string.IsNullOrEmpty(v)) return v;
            }
            if (Debug.isDebugBuild) WarnOnce("missing:" + key, "[Loc] Missing key: " + key);   // no string garbage in release
            return key;
        }

        /// <summary>string.Format with the current culture; {0}, {1}...</summary>
        public static string T(string key, params object[] args)
        {
            string format = T(key);
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(FormatProvider, format, args);
            }
            catch (FormatException)
            {
                WarnOnce("format:" + key, "[Loc] Bad format string for key " + key + ": " + format);
                return format;
            }
        }

        public static bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureLoaded();
            if (!_table.TryGetValue(key, out var row)) return false;
            for (int i = 0; i < row.Length; i++)
                if (!string.IsNullOrEmpty(row[i])) return true;
            return false;
        }

        /// <summary>Formats numbers for the current language (1,234 / 1.234).</summary>
        public static string Number(long value) => value.ToString("#,0", FormatProvider);

        /// <summary>Number format of the current language (en: 1,234.5 · pt/es: 1.234,5). Built by hand so it never
        /// depends on the device's culture tables (which IL2CPP may strip).</summary>
        public static NumberFormatInfo FormatProvider
        {
            get
            {
                if (_numberFormat != null) return _numberFormat;
                EnsureLanguage();
                var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
                bool commaDecimal = _lang != English;
                nfi.NumberGroupSeparator = commaDecimal ? "." : ",";
                nfi.NumberDecimalSeparator = commaDecimal ? "," : ".";
                nfi.CurrencyGroupSeparator = nfi.NumberGroupSeparator;
                nfi.CurrencyDecimalSeparator = nfi.NumberDecimalSeparator;
                nfi.PercentGroupSeparator = nfi.NumberGroupSeparator;
                nfi.PercentDecimalSeparator = nfi.NumberDecimalSeparator;
                _numberFormat = nfi;
                return nfi;
            }
        }

        // ------------------------------------------------------------------ tables

        /// <summary>Drops every table and reloads Resources/Loc (editor tools / tests).</summary>
        public static void Reload()
        {
            _table = null;
            _warned = null;
            EnsureLoaded();
        }

        /// <summary>Merges a CSV table (header "key,en,pt,es", any column order) into the loaded strings.</summary>
        public static void AddCsv(string csvText, string sourceName = "runtime")
        {
            EnsureLoaded();
            Merge(csvText, sourceName);
        }

        /// <summary>
        /// RFC 4180 parser: quoted cells may contain commas, newlines and doubled quotes (""). Accepts LF, CRLF and CR
        /// line ends and a UTF-8 BOM. Returns one list of cells per line (blank lines are skipped).
        /// </summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false, cellWasQuoted = false;
            int i = text[0] == '\uFEFF' ? 1 : 0;   // UTF-8 BOM
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else quoted = false;
                    }
                    else if (c == '\r')
                    {
                        // Normalize CRLF / CR inside quoted cells to '\n'.
                        cell.Append('\n');
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                    else cell.Append(c);
                }
                else if (c == '"' && cell.Length == 0 && !cellWasQuoted)
                {
                    // Quotes only open a quoted cell at its start; a stray quote inside text (5" box) is literal.
                    quoted = true;
                    cellWasQuoted = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    cellWasQuoted = false;
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow(rows, ref row, cell, cellWasQuoted);
                    cellWasQuoted = false;
                }
                else cell.Append(c);
            }
            EndRow(rows, ref row, cell, cellWasQuoted);
            return rows;
        }

        static void EndRow(List<List<string>> rows, ref List<string> row, StringBuilder cell, bool cellWasQuoted)
        {
            bool blankLine = row.Count == 0 && cell.Length == 0 && !cellWasQuoted;
            if (!blankLine)
            {
                row.Add(cell.ToString());
                rows.Add(row);
                row = new List<string>();
            }
            cell.Clear();
        }

        static void EnsureLoaded()
        {
            if (_table != null) return;
            _table = new Dictionary<string, string[]>(1024, StringComparer.Ordinal);
            TextAsset[] assets;
            try { assets = Resources.LoadAll<TextAsset>(ResourceFolder); }
            catch (Exception e)
            {
                Debug.LogWarning("[Loc] Could not load Resources/" + ResourceFolder + ": " + e.Message);
                return;
            }
            if (assets == null || assets.Length == 0)
            {
                WarnOnce("noTables", "[Loc] No tables found in Resources/" + ResourceFolder);
                return;
            }
            // Deterministic merge order (Resources.LoadAll order is unspecified).
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
            foreach (var asset in assets)
                if (asset != null) Merge(asset.text, asset.name);
        }

        static void Merge(string csvText, string source)
        {
            var rows = ParseCsv(csvText);
            if (rows.Count == 0) return;

            // Map header columns to language indices.
            var header = rows[0];
            int keyCol = -1;
            var colLang = new int[header.Count];
            for (int c = 0; c < header.Count; c++)
            {
                string h = header[c].Trim().ToLowerInvariant();
                colLang[c] = -1;
                if (h == "key") keyCol = c;
                else colLang[c] = IndexOfCode(h);
            }
            if (keyCol < 0)
            {
                Debug.LogWarning("[Loc] Table '" + source + "' has no 'key' column; skipped.");
                return;
            }

            for (int r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                if (keyCol >= cells.Count) continue;
                string key = cells[keyCol].Trim();
                if (key.Length == 0 || key[0] == '#') continue;

                if (!_table.TryGetValue(key, out var values))
                {
                    values = new string[Codes.Length];
                    _table[key] = values;
                }
                for (int c = 0; c < cells.Count && c < colLang.Length; c++)
                {
                    int lang = colLang[c];
                    if (lang < 0) continue;
                    string v = Unescape(cells[c]);
                    if (v.Length == 0) continue;
                    if (!string.IsNullOrEmpty(values[lang]) && values[lang] != v)
                        WarnOnce("dup:" + key + ":" + lang, "[Loc] Key '" + key + "' (" + Codes[lang] + ") redefined by " + source);
                    values[lang] = v;
                }
            }
        }

        /// <summary>Literal "\n" in a cell becomes a newline.</summary>
        static string Unescape(string v) => v.IndexOf('\\') >= 0 ? v.Replace("\\n", "\n") : v;

        // ------------------------------------------------------------------ language state

        static void EnsureLanguage()
        {
            EnsureLoaded();
            if (_lang >= 0) return;
            _lang = ResolveSavedOrDevice();
        }

        static int ResolveSavedOrDevice()
        {
            int index = IndexOfCode(SaveSystem.Data.language);
            if (index < 0) index = IndexOfCode(DeviceLanguage());
            return index < 0 ? English : index;
        }

        static void SetCurrent(int index)
        {
            if (index < 0) index = English;
            EnsureLoaded();
            if (index == _lang) return;
            _lang = index;
            _numberFormat = null;
            OnLanguageChanged?.Invoke();
        }

        /// <summary>Called by SaveSystem when the whole save was replaced (cloud restore / reset).</summary>
        internal static void OnSaveReplaced()
        {
            if (_lang < 0) return;   // not resolved yet: the next lookup reads the new save anyway
            SetCurrent(ResolveSavedOrDevice());
        }

        /// <summary>
        /// Called by SaveSystem when the storage is swapped (test hook): forget the resolved language without reading
        /// the save now, so switching back to the real storage never loads the player's save from an EditMode test.
        /// </summary>
        internal static void InvalidateLanguage()
        {
            _lang = -1;
            _numberFormat = null;
        }

        /// <summary>Index in <see cref="Codes"/>; accepts "pt-BR"/"es_419" style tags. -1 when unsupported.</summary>
        static int IndexOfCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;
            string c = code.Trim().ToLowerInvariant();
            int sep = c.IndexOfAny(new[] { '-', '_' });
            if (sep > 0) c = c.Substring(0, sep);
            for (int i = 0; i < Codes.Length; i++)
                if (Codes[i] == c) return i;
            return -1;
        }

        static void WarnOnce(string id, string message)
        {
            if (!Debug.isDebugBuild) return;
            if (_warned == null) _warned = new HashSet<string>();
            if (_warned.Add(id)) Debug.LogWarning(message);
        }
    }
}
