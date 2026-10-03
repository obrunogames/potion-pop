using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Developer cheats (menu Potion Pop/Debug/...). Save edits work in and out of Play Mode and are written to disk
    /// immediately; clock and UI items need Play Mode (the runtime statics live there).
    /// </summary>
    public static class DebugMenu
    {
        const string Root = "Potion Pop/Debug/";
        const int Priority = 200;

        // ------------------------------------------------------------------ economy

        [MenuItem(Root + "Give 5000 Coins", priority = Priority)]
        public static void GiveCoins()
        {
            SyncFromDisk();
            Economy.AddCoins(5000, "debug");
            Persist("+5000 coins (now " + Economy.Coins + ")");
        }

        [MenuItem(Root + "Give 5 Of Each Booster", priority = Priority + 1)]
        public static void GiveBoosters()
        {
            SyncFromDisk();
            foreach (BoosterType type in Enum.GetValues(typeof(BoosterType))) Economy.AddBooster(type, 5);
            Persist("+5 of each booster");
        }

        // ------------------------------------------------------------------ hearts

        [MenuItem(Root + "Refill Hearts", priority = Priority + 20)]
        public static void RefillHearts()
        {
            SyncFromDisk();
            Lives.RefillFull();
            Persist("hearts refilled (" + Lives.Hearts + ")");
        }

        [MenuItem(Root + "Empty Hearts", priority = Priority + 21)]
        public static void EmptyHearts()
        {
            SyncFromDisk();
            SaveSystem.Data.infiniteHeartsUntil = 0; // TryConsume is a no-op while infinite hearts are active
            for (int i = 0; i <= Lives.Max && Lives.Hearts > 0; i++)
                if (!Lives.TryConsume()) break;
            Persist("hearts emptied (" + Lives.Hearts + ", next in " + TimeUtil.FormatDuration(Lives.SecondsToNext) + ")");
        }

        // ------------------------------------------------------------------ progress

        [MenuItem(Root + "Jump To Level...", priority = Priority + 40)]
        public static void JumpToLevel()
        {
            SyncFromDisk();
            IntInputDialog.Show("Jump to level", "Next level to play:", Progress.CurrentLevel, 1, 100000, level =>
            {
                SyncFromDisk();
                Progress.SetLevel(level);
                Persist("jumped to level " + level);
            });
        }

        // ------------------------------------------------------------------ clock (Play Mode)

        [MenuItem(Root + "Clock +1 Hour", priority = Priority + 60)]
        public static void ClockPlusHour() => AdvanceClock(3600);

        [MenuItem(Root + "Clock +1 Day", priority = Priority + 61)]
        public static void ClockPlusDay() => AdvanceClock(86400);

        [MenuItem(Root + "Clock Reset", priority = Priority + 62)]
        public static void ClockReset()
        {
            TimeUtil.DebugOffsetSeconds = 0;
            Lives.Tick();
            Debug.Log(EditorUtil.LogPrefix + "Clock offset reset.");
        }

        [MenuItem(Root + "Clock +1 Hour", true)]
        [MenuItem(Root + "Clock +1 Day", true)]
        [MenuItem(Root + "Clock Reset", true)]
        [MenuItem(Root + "Open Design System Popup", true)]
        static bool IsPlaying() => EditorApplication.isPlaying;

        static void AdvanceClock(long seconds)
        {
            TimeUtil.DebugOffsetSeconds += seconds;
            Lives.Tick(); // apply heart regeneration right away
            Debug.Log(EditorUtil.LogPrefix + $"Clock offset now {TimeUtil.FormatDuration(TimeUtil.DebugOffsetSeconds)} " +
                      $"({TimeUtil.DebugOffsetSeconds} s). Daily systems refresh when their screen/popup is reopened.");
        }

        // ------------------------------------------------------------------ save

        [MenuItem(Root + "Reset Save...", priority = Priority + 80)]
        public static void ResetSave()
        {
            if (!EditorUtility.DisplayDialog("Potion Pop", "Delete the local save (progress, coins, boosters, settings)?", "Delete", "Cancel"))
                return;
            ResetSaveNow();
        }

        /// <summary>
        /// Deletes the save file (+ temp copy) and its PlayerPrefs backup, then SaveSystem.ResetAll writes a brand-new
        /// save (new player id) so disk and memory agree (a running game rebuilds through SaveSystem.OnReplaced).
        /// No confirmation (for scripts/tests).
        /// </summary>
        public static void ResetSaveNow()
        {
            string file = SaveSystem.FilePath;
            foreach (string path in new[] { file, file + ".tmp" })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception e) { Debug.LogWarning(EditorUtil.LogPrefix + "Could not delete " + path + ": " + e.Message); }
            }
            PlayerPrefs.DeleteKey(SaveSystem.BackupKey);
            PlayerPrefs.Save();
            SaveSystem.ResetAll();
            Debug.Log(EditorUtil.LogPrefix + "Save reset (" + file + "), new player id " + SaveSystem.Data.playerId + ".");
        }

        [MenuItem(Root + "Delete All PlayerPrefs...", priority = Priority + 81)]
        public static void DeletePlayerPrefs()
        {
            if (!EditorUtility.DisplayDialog("Potion Pop", "Delete ALL PlayerPrefs (save backup, sign-in session, ad counters)?", "Delete", "Cancel"))
                return;
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log(EditorUtil.LogPrefix + "PlayerPrefs deleted.");
        }

        [MenuItem(Root + "Reveal Save Folder", priority = Priority + 82)]
        public static void RevealSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        // ------------------------------------------------------------------ UI (Play Mode)

        [MenuItem(Root + "Open Design System Popup", priority = Priority + 100)]
        public static void OpenDesignSystemPopup()
        {
            var popupBase = EditorUtil.FindType("PotionPop.UI.Popup");
            var popupType = EditorUtil.FindDerivedType(popupBase, "PotionPop.UI.DesignSystemPopup");
            var manager = EditorUtil.FindType("PotionPop.UI.PopupManager");
            MethodInfo show = null;
            if (manager != null)
                foreach (var method in manager.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (method.Name == "Show" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1) { show = method; break; }
            if (popupType == null || show == null)
            {
                Debug.LogWarning(EditorUtil.LogPrefix + "DesignSystemPopup or PopupManager.Show<T> not found.");
                return;
            }
            try { show.MakeGenericMethod(popupType).Invoke(null, new object[] { null }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Edit mode: reload the save from disk first, so a cheat never overwrites progress written since the editor
        /// last read it (a Play Mode session, a standalone build sharing the same persistentDataPath).
        /// </summary>
        static void SyncFromDisk()
        {
            if (!Application.isPlaying) SaveSystem.Load();
        }

        static void Persist(string message)
        {
            SaveSystem.MarkDirty();
            SaveSystem.Save(); // write now: Play Mode may stop before the debounced save, edit mode has no runner
            Debug.Log(EditorUtil.LogPrefix + "Debug: " + message);
        }
    }

    /// <summary>Tiny modal-less integer prompt (EditorInputDialog-like).</summary>
    public sealed class IntInputDialog : EditorWindow
    {
        string _message;
        string _text;
        int _min, _max;
        Action<int> _onOk;
        bool _focused;

        public static void Show(string title, string message, int value, int min, int max, Action<int> onOk)
        {
            var window = CreateInstance<IntInputDialog>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._text = value.ToString();
            window._min = min;
            window._max = max;
            window._onOk = onOk;
            var size = new Vector2(320, 130);
            window.minSize = window.maxSize = size;
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center - size * 0.5f, size);
            window.ShowUtility();
        }

        void OnGUI()
        {
            // Read Enter/Escape before the text field: a focused field consumes the key event.
            var e = Event.current;
            bool submit = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); GUIUtility.ExitGUI(); }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(_message);
            GUI.SetNextControlName("value");
            _text = EditorGUILayout.TextField(_text);
            if (!_focused)
            {
                EditorGUI.FocusTextInControl("value");
                _focused = true;
            }

            bool valid = int.TryParse(_text, out int value) && value >= _min && value <= _max;
            if (!valid) EditorGUILayout.HelpBox($"Enter a number from {_min} to {_max}.", MessageType.None);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) { Close(); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(!valid))
                {
                    if (GUILayout.Button("OK", GUILayout.Width(80)) || (submit && valid))
                    {
                        var callback = _onOk;
                        Close();
                        callback?.Invoke(value);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }
}
