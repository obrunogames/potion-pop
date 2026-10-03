using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using PotionPop.Levels;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Generates levels and checks them (GDD §3: every level must be solvable).
    /// Menu: Potion Pop/Validate Levels 1-500 → Logs/level_report.csv + summary in the Console.
    /// Command line: -executeMethod PotionPop.EditorTools.LevelValidator.ValidateBatch [-levels 1-500]
    /// (exit code 1 when a level fails a hard rule or the generator throws).
    /// Errors (the level is broken): a color without exactly `capacity` units, the generator's solution does not replay
    /// to a win with the real rules (BoardState.Pour, stones included), an inconsistent definition (hidden units ≥ the
    /// bottle's units, overfull bottles, star thresholds out of order). Warnings: hard flag off the GDD formula, hidden /
    /// stone bottles before their first level, a bottle that starts completed, more than 14 starting bottles, generation
    /// slower than 1 s, a solution shorter than the colors count.
    /// </summary>
    public static class LevelValidator
    {
        public const string ReportPath = "Logs/level_report.csv";
        const double SlowMs = 1000.0;
        const int MaxStartBottles = 14;

        public sealed class Row
        {
            public int level;
            public bool hard;
            public int colors, bottles, empties, hiddenBottles, hiddenUnits, stones, par, moves3, moves2;
            public bool solutionOk;
            public double ms;
            public string error;
            /// <summary>GDD rule violations that do not make the level unplayable ("; "-separated), or null.</summary>
            public string warnings;
        }

        public sealed class Summary
        {
            public int from, to, levels, errors, warnings;
            public double totalMs, maxMs;
            public int slowestLevel;
            public long parSum;
            public bool cancelled;
            public readonly List<int> failedLevels = new List<int>();

            public override string ToString()
            {
                string failed = failedLevels.Count == 0 ? "none" : string.Join(", ", failedLevels.GetRange(0, Math.Min(30, failedLevels.Count))) +
                                                                     (failedLevels.Count > 30 ? ", ..." : "");
                return $"levels {from}-{to}: {levels} checked{(cancelled ? " (cancelled)" : "")}, {errors} broken, {warnings} with warnings; " +
                       $"avg par {(levels > 0 ? parSum / (double)levels : 0):0.0}, avg {(levels > 0 ? totalMs / levels : 0):0.0} ms, " +
                       $"slowest level {slowestLevel} ({maxMs:0} ms). Failed: {failed}";
            }
        }

        [MenuItem("Potion Pop/Validate Levels 1-500", priority = 40)]
        public static void ValidateMenu()
        {
            var summary = Validate(1, 500, true);
            Log(summary);
        }

        /// <summary>Batch entry. Optional "-levels A-B" (default 1-500).</summary>
        public static void ValidateBatch()
        {
            int from = 1, to = 500;
            string range = EditorUtil.GetArg("-levels");
            if (!string.IsNullOrEmpty(range))
            {
                string[] parts = range.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b) && a >= 1 && b >= a)
                {
                    from = a;
                    to = b;
                }
                else Debug.LogWarning(EditorUtil.LogPrefix + "Ignoring invalid -levels '" + range + "' (expected A-B).");
            }
            Summary summary;
            try { summary = Validate(from, to, false); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            Log(summary);
            if (UnityEngine.Application.isBatchMode && summary.errors > 0) EditorApplication.Exit(1);
        }

        /// <summary>Generates and checks levels [from, to], writes the CSV report and returns the summary.</summary>
        public static Summary Validate(int from, int to, bool showProgress)
        {
            var summary = new Summary { from = from, to = to };
            var rows = new List<Row>(Math.Max(0, to - from + 1));
            try
            {
                for (int level = from; level <= to; level++)
                {
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Validating levels",
                            $"Level {level} / {to}  ({summary.errors} broken)", (level - from) / (float)Math.Max(1, to - from + 1)))
                    {
                        summary.cancelled = true;
                        break;
                    }
                    var row = Check(level);
                    rows.Add(row);
                    summary.levels++;
                    summary.totalMs += row.ms;
                    summary.parSum += row.par;
                    if (row.ms > summary.maxMs) { summary.maxMs = row.ms; summary.slowestLevel = level; }
                    if (row.error != null)
                    {
                        summary.errors++;
                        summary.failedLevels.Add(level);
                    }
                    if (row.warnings != null) summary.warnings++;
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            WriteReport(rows);
            return summary;
        }

        /// <summary>Generates one level, replays the generator's solution with the real rules and checks the GDD
        /// invariants (exceptions are reported in Row.error).</summary>
        public static Row Check(int level)
        {
            var row = new Row { level = level };
            var watch = Stopwatch.StartNew();
            LevelDefinition def = null;
            try { def = LevelGenerator.Generate(level); }
            catch (Exception e)
            {
                row.error = e.GetType().Name + ": " + e.Message;
                Debug.LogError(EditorUtil.LogPrefix + $"Level {level}: {e}");
            }
            row.ms = watch.Elapsed.TotalMilliseconds;
            if (def == null)
            {
                row.error ??= "LevelGenerator returned null";
                return row;
            }
            try
            {
                Describe(def, row);
                var errors = new List<string>();
                var warnings = new List<string>();
                CheckDefinition(def, errors, warnings);
                row.solutionOk = ReplaySolution(def, out string replayError);
                if (!row.solutionOk) errors.Add(replayError);
                CheckRules(level, def, row, warnings);
                if (errors.Count > 0) row.error = string.Join("; ", errors);
                if (warnings.Count > 0) row.warnings = string.Join("; ", warnings);
            }
            catch (Exception e)
            {
                row.error = e.GetType().Name + ": " + e.Message;
                Debug.LogError(EditorUtil.LogPrefix + $"Level {level}: {e}");
            }
            return row;
        }

        static void Describe(LevelDefinition def, Row row)
        {
            row.hard = def.hard;
            row.colors = def.ColorsUsed().Length;
            row.bottles = def.BottleCount;
            row.par = def.par;
            row.moves3 = def.movesFor3Stars;
            row.moves2 = def.movesFor2Stars;
            if (def.bottles == null) return;
            foreach (var b in def.bottles)
            {
                if (b == null) continue;
                int n = b.units != null ? b.units.Length : 0;
                if (n == 0) row.empties++;
                if (b.hidden > 0)
                {
                    row.hiddenBottles++;
                    row.hiddenUnits += b.hidden;
                }
                if (b.lockCount > 0) row.stones++;
            }
        }

        /// <summary>Structural invariants: unit counts per color, hidden prefix, capacity, thresholds.</summary>
        static void CheckDefinition(LevelDefinition def, List<string> errors, List<string> warnings)
        {
            int cap = Math.Max(1, def.capacity);
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < def.BottleCount; i++)
            {
                var b = def.bottles[i];
                if (b == null)
                {
                    errors.Add($"bottle {i} is null");
                    continue;
                }
                int n = b.units != null ? b.units.Length : 0;
                if (n > cap) errors.Add($"bottle {i} holds {n} units (capacity {cap})");
                if (n > 0 && b.hidden >= n) errors.Add($"bottle {i}: {b.hidden} hidden units of {n} (the top must be visible)");
                if (n == 0 && b.hidden > 0) errors.Add($"empty bottle {i} has hidden units");
                if (b.lockCount < 0) errors.Add($"bottle {i}: negative stone counter");
                if (b.units == null) continue;
                bool mono = n == cap;
                for (int k = 0; k < n; k++)
                {
                    int c = b.units[k];
                    if (c < 0 || c >= Liquids.Count) errors.Add($"bottle {i}: color {c} outside the palette");
                    counts[c] = counts.TryGetValue(c, out int v) ? v + 1 : 1;
                    if (k > 0 && c != b.units[0]) mono = false;
                }
                if (mono) warnings.Add($"bottle {i} starts completed");
            }
            foreach (var kv in counts)
                if (kv.Value != cap) errors.Add($"color {kv.Key} has {kv.Value} units (expected {cap})");
            if (def.colorCount > 0 && def.colorCount != counts.Count) warnings.Add($"colorCount {def.colorCount} but {counts.Count} colors used");
            if (def.movesFor3Stars <= 0 || def.movesFor2Stars < def.movesFor3Stars || def.movesFor3Stars < def.par)
                errors.Add($"star thresholds 3*={def.movesFor3Stars} 2*={def.movesFor2Stars} par={def.par}");
            int expected3 = (int)Math.Ceiling(def.par * 1.3) + 2, expected2 = (int)Math.Ceiling(def.par * 1.75) + 4;
            if (def.movesFor3Stars != expected3 || def.movesFor2Stars != expected2)
                warnings.Add($"thresholds {def.movesFor3Stars}/{def.movesFor2Stars} off the GDD formula ({expected3}/{expected2})");
        }

        /// <summary>The generator's solution must win the board under the real rules (stones, hidden pour-through...).</summary>
        static bool ReplaySolution(LevelDefinition def, out string error)
        {
            error = null;
            var solution = def.solution;
            if (solution == null || solution.Count == 0)
            {
                error = "no verified solution";
                return false;
            }
            var board = new BoardState(def);
            for (int i = 0; i < solution.Count; i++)
            {
                var m = solution[i];
                if (board.Pour(m.from, m.to) == null)
                {
                    error = $"solution move {i + 1}/{solution.Count} ({m}) is illegal: {board.Check(m.from, m.to)}";
                    return false;
                }
            }
            if (!board.IsWon)
            {
                error = "solution replayed without winning";
                return false;
            }
            if (solution.Count != def.par)
            {
                error = $"par {def.par} but the solution has {solution.Count} moves";
                return false;
            }
            return true;
        }

        /// <summary>GDD §3 difficulty rules (warnings: the level is playable).</summary>
        static void CheckRules(int level, LevelDefinition def, Row row, List<string> warnings)
        {
            bool hard = Difficulty.IsHard(level);
            if (def.hard != hard) warnings.Add($"hard flag {def.hard}, GDD says {hard}");
            if (row.hiddenBottles > 0 && level < Difficulty.FirstHiddenLevel) warnings.Add("hidden colors before level " + Difficulty.FirstHiddenLevel);
            if (row.stones > 0 && level < Difficulty.FirstLockLevel) warnings.Add("stones before level " + Difficulty.FirstLockLevel);
            if (level == Difficulty.FirstHiddenLevel && row.hiddenBottles == 0) warnings.Add("first hidden level has no hidden colors");
            if (level == Difficulty.FirstLockLevel && row.stones == 0) warnings.Add("first stone level has no stone");
            if (row.bottles > MaxStartBottles) warnings.Add($"{row.bottles} starting bottles (> {MaxStartBottles})");
            if (row.ms > SlowMs) warnings.Add($"generation took {row.ms:0} ms");
            if (level > 1 && def.par < row.colors) warnings.Add($"par {def.par} below the colors count");
        }

        static string Csv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        static void WriteReport(List<Row> rows)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("level,hard,colors,bottles,empties,hidden_bottles,hidden_units,stones,par,moves_3_stars,moves_2_stars,solution_ok,ms,status,notes");
            foreach (var r in rows)
            {
                sb.Append(r.level.ToString(inv)).Append(',')
                  .Append(r.hard ? "1" : "0").Append(',')
                  .Append(r.colors.ToString(inv)).Append(',')
                  .Append(r.bottles.ToString(inv)).Append(',')
                  .Append(r.empties.ToString(inv)).Append(',')
                  .Append(r.hiddenBottles.ToString(inv)).Append(',')
                  .Append(r.hiddenUnits.ToString(inv)).Append(',')
                  .Append(r.stones.ToString(inv)).Append(',')
                  .Append(r.par.ToString(inv)).Append(',')
                  .Append(r.moves3.ToString(inv)).Append(',')
                  .Append(r.moves2.ToString(inv)).Append(',')
                  .Append(r.solutionOk ? "1" : "0").Append(',')
                  .Append(r.ms.ToString("0.0", inv)).Append(',')
                  .Append(r.error != null ? "error" : r.warnings != null ? "warning" : "ok").Append(',')
                  .Append(Csv(r.error != null && r.warnings != null ? r.error + "; " + r.warnings : r.error ?? r.warnings)).AppendLine();
            }
            string path = Path.Combine(EditorUtil.ProjectRoot, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EditorUtil.ProjectRoot);
            File.WriteAllText(path, sb.ToString());
        }

        static void Log(Summary summary)
        {
            string message = EditorUtil.LogPrefix + "Level validation " + summary + " Report: " + ReportPath;
            if (summary.errors > 0) Debug.LogError(message);
            else if (summary.warnings > 0) Debug.LogWarning(message + " (see the notes column)");
            else Debug.Log(message);
        }
    }
}
