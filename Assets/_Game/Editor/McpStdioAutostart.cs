using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Keeps the MCP for Unity *stdio* bridge (local TCP listener + ~/.unity-mcp status file) running in this project's
    /// editor after every domain reload, in addition to whatever transport the MCP window uses (HTTP/WebSocket).
    /// Stdio-based MCP clients (e.g. Claude Code sessions) can then always reach the editor.
    /// Works through reflection so the project compiles without the MCP package; it never changes the package's
    /// global EditorPrefs (other projects keep their transport). Toggle: Potion Pop/Debug/MCP Stdio Bridge Autostart.
    /// </summary>
    [InitializeOnLoad]
    static class McpStdioAutostart
    {
        const string PrefKey = "PotionPop.McpStdioAutostart";
        const string MenuPath = "Potion Pop/Debug/MCP Stdio Bridge Autostart";
        const string HostType = "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor";

        static McpStdioAutostart()
        {
            if (Application.isBatchMode) return;
            // After the package's own [InitializeOnLoad] logic has run.
            EditorApplication.delayCall += TryStart;
        }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static void TryStart()
        {
            if (!Enabled) return;
            var host = Type.GetType(HostType);
            if (host == null) return;
            try
            {
                var running = host.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (running is bool b && b) return;
                host.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)?.Invoke(null, null);
                var port = host.GetMethod("GetCurrentPort", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                Debug.Log($"Potion Pop: MCP stdio bridge listening on port {port}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Potion Pop: could not start the MCP stdio bridge: " + (e.InnerException ?? e).Message);
            }
        }

        [MenuItem(MenuPath, priority = 200)]
        static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) TryStart();
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
