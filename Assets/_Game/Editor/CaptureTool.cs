using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.EditorTools
{
    /// <summary>
    /// Renders the camera together with every Screen Space Overlay canvas into a PNG of any size (store screenshots,
    /// automated visual checks in Play Mode). Overlay canvases are switched to Screen Space Camera for the capture
    /// and restored right after. Best results when the Game view has the same aspect ratio as the capture.
    /// Menu: Potion Pop/Capture Screenshot → Screenshots/shot_&lt;n&gt;.png in the project root.
    /// </summary>
    public static class CaptureTool
    {
        public const int DefaultWidth = 1080;
        public const int DefaultHeight = 2340;
        public const string Folder = "Screenshots";

        [MenuItem("Potion Pop/Capture Screenshot", priority = 30)]
        static void CaptureMenu()
        {
            string path = CaptureNext();
            if (path != null) EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Potion Pop/Capture Screenshot", true)]
        static bool CaptureMenuValidate() => EditorApplication.isPlaying;

        /// <summary>Next free Screenshots/shot_&lt;n&gt;.png (absolute path).</summary>
        public static string NextPath()
        {
            string folder = Path.Combine(EditorUtil.ProjectRoot, Folder);
            Directory.CreateDirectory(folder);
            int n = 1;
            while (File.Exists(Path.Combine(folder, "shot_" + n + ".png"))) n++;
            return Path.Combine(folder, "shot_" + n + ".png");
        }

        /// <summary>Captures to the next free shot_&lt;n&gt;.png. Returns its absolute path, or null on failure.</summary>
        public static string CaptureNext(int width = DefaultWidth, int height = DefaultHeight) => Capture(NextPath(), width, height);

        /// <summary>Renders camera + overlay UI to a PNG (path relative to the project root or absolute).
        /// Returns the absolute path written, or null on failure.</summary>
        public static string Capture(string path, int width = DefaultWidth, int height = DefaultHeight)
        {
            if (width <= 0 || height <= 0)
            {
                Debug.LogError(EditorUtil.LogPrefix + "Capture size must be positive.");
                return null;
            }
            if (!Path.IsPathRooted(path)) path = Path.Combine(EditorUtil.ProjectRoot, path);

            var cam = FindCamera(out bool temporaryCamera);
            var canvases = new List<Canvas>();
            var modes = new List<RenderMode>();
            var cameras = new List<Camera>();
            var distances = new List<float>();
            var orders = new List<int>();
            RenderTexture rt = null;
            Texture2D image = null;
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                foreach (var canvas in Object.FindObjectsByType<Canvas>())
                {
                    if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    canvases.Add(canvas);
                    modes.Add(canvas.renderMode);
                    cameras.Add(canvas.worldCamera);
                    distances.Add(canvas.planeDistance);
                    orders.Add(canvas.sortingOrder);
                }
                // Keep the overlay stacking order: higher sortingOrder in front (plane distance breaks ties).
                for (int i = 0; i < canvases.Count; i++)
                {
                    var canvas = canvases[i];
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.05f, 1f - i * 0.001f);
                    canvas.sortingOrder = orders[i] + 1000; // overlay UI always drew above anything else
                }

                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                RefreshScalers(canvases);
                Canvas.ForceUpdateCanvases();
                cam.Render();

                RenderTexture.active = rt;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EditorUtil.ProjectRoot);
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log(EditorUtil.LogPrefix + $"Screenshot {width}x{height} saved: {path}");
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogError(EditorUtil.LogPrefix + "Capture failed: " + e);
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (cam != null) cam.targetTexture = previousTarget;
                for (int i = 0; i < canvases.Count; i++)
                {
                    if (canvases[i] == null) continue;
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = cameras[i];
                    canvases[i].planeDistance = distances[i];
                    canvases[i].sortingOrder = orders[i];
                }
                RefreshScalers(canvases);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (image != null) Object.DestroyImmediate(image);
                if (temporaryCamera && cam != null) Object.DestroyImmediate(cam.gameObject);
            }
        }

        static Camera FindCamera(out bool temporary)
        {
            temporary = false;
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) return cam;
            foreach (var c in Object.FindObjectsByType<Camera>())
                if (c.isActiveAndEnabled) return c;

            // No camera (pure overlay UI): a hidden one renders the canvases on the game background color.
            temporary = true;
            var go = new GameObject("CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PotionPopBuilder.BackgroundColor;
            return cam;
        }

        /// <summary>Re-runs every CanvasScaler under the canvases so they pick up the capture resolution.</summary>
        static void RefreshScalers(List<Canvas> canvases)
        {
            foreach (var canvas in canvases)
            {
                if (canvas == null) continue;
                foreach (var scaler in canvas.GetComponentsInChildren<CanvasScaler>())
                {
                    if (!scaler.enabled) continue;
                    scaler.enabled = false; // OnDisable/OnEnable recompute the scale factor immediately
                    scaler.enabled = true;
                }
            }
        }
    }
}
