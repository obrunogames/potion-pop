using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PotionPop.EditorTools
{
    /// <summary>Small helpers shared by the Potion Pop editor tools (folders, type lookup, command line).</summary>
    public static class EditorUtil
    {
        public const string LogPrefix = "[Potion Pop] ";

        /// <summary>Creates an asset folder and its parents if missing ("Assets/_Game/Scenes").</summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return; // "Assets" always exists
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        /// <summary>Finds a type by full name in every loaded assembly (internal types included). Null when missing.</summary>
        public static Type FindType(string fullName)
        {
            foreach (var assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type type;
                try { type = assembly.GetType(fullName, false); }
                catch (Exception) { continue; } // dynamic / broken assemblies
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>
        /// Finds a concrete type deriving from <paramref name="baseType"/> by full name, falling back to the simple
        /// name (first match whose namespace starts with <paramref name="namespacePrefix"/>). Null when missing.
        /// </summary>
        public static Type FindDerivedType(Type baseType, string fullName, string namespacePrefix = "PotionPop")
        {
            if (baseType == null) return null;
            string simpleName = fullName;
            int dot = fullName.LastIndexOf('.');
            if (dot >= 0) simpleName = fullName.Substring(dot + 1);
            Type fallback = null;
            foreach (var type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.ContainsGenericParameters) continue;
                if (type.FullName == fullName) return type;
                if (fallback == null && type.Name == simpleName &&
                    (string.IsNullOrEmpty(namespacePrefix) || (type.Namespace ?? "").StartsWith(namespacePrefix, StringComparison.Ordinal)))
                    fallback = type;
            }
            return fallback;
        }

        /// <summary>Value following "-name" on the command line ("-buildOutput Builds/x.apk"), or null.</summary>
        public static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static bool HasArg(string name)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Absolute path of the project root (the folder that contains Assets/).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }
    }

    /// <summary>
    /// Editor-side reader of the localization tables (every CSV in Assets/_Game/Resources/Loc, header "key,en,pt,es",
    /// RFC4180 quoting, "\n" inside a cell = newline). For build tooling that writes user-visible text (e.g. iOS
    /// Info.plist usage strings, kept in Resources/Loc/editor.csv), so those texts live in the CSVs like every other string.
    /// </summary>
    public static class EditorLoc
    {
        public const string LocFolder = "Assets/_Game/Resources/Loc";
        public static readonly string[] Languages = { "en", "pt", "es" };

        /// <summary>Text for key/language. Missing cell → English → null.</summary>
        public static string Get(string key, string language)
        {
            var tables = Load();
            if (!tables.TryGetValue(key, out var row)) return null;
            if (row.TryGetValue(language, out var text) && !string.IsNullOrEmpty(text)) return text;
            return row.TryGetValue("en", out var en) && !string.IsNullOrEmpty(en) ? en : null;
        }

        static Dictionary<string, Dictionary<string, string>> Load()
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (!Directory.Exists(LocFolder)) return result;
            var files = Directory.GetFiles(LocFolder, "*.csv");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                List<List<string>> rows;
                try { rows = ParseCsv(File.ReadAllText(file, Encoding.UTF8)); }
                catch (Exception e)
                {
                    Debug.LogWarning(EditorUtil.LogPrefix + "Could not read " + file + ": " + e.Message);
                    continue;
                }
                if (rows.Count == 0) continue;
                var header = rows[0];
                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0])) continue;
                    if (!result.TryGetValue(row[0].Trim(), out var cells))
                        result[row[0].Trim()] = cells = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int c = 1; c < header.Count && c < row.Count; c++)
                        cells[header[c].Trim()] = row[c].Replace("\\n", "\n");
                }
            }
            return result;
        }

        /// <summary>RFC4180 parser: quoted fields, doubled quotes, line breaks inside quotes, CRLF or LF.</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false, rowHasData = false;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '"': quoted = true; rowHasData = true; break;
                    case ',': row.Add(cell.ToString()); cell.Length = 0; rowHasData = true; break;
                    case '\r': break;
                    case '\n':
                        if (rowHasData || cell.Length > 0) { row.Add(cell.ToString()); rows.Add(row); }
                        row = new List<string>();
                        cell.Length = 0;
                        rowHasData = false;
                        break;
                    default: cell.Append(ch); rowHasData = true; break;
                }
            }
            if (rowHasData || cell.Length > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
