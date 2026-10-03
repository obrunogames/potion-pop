using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PotionPop.Services
{
    /// <summary>
    /// Minimal raw-uGUI toolkit for the few overlays the services draw themselves (simulated ads, the "loading ad"
    /// wait, the simulated banner). The Services module must not depend on PotionPop.UI, so these use plain Images and
    /// legacy Text with Unity's built-in runtime font (always available, covers pt/es accents).
    /// Reference canvas 1080 wide (portrait), like the game UI.
    /// </summary>
    public static class ServicesUI
    {
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1920f;

        // Palette borrowed from Docs/DesignSystem.md (values only — no dependency on PotionPop.UI.DS).
        public static readonly Color Backdrop = new Color(0.102f, 0.043f, 0.2f, 0.94f);
        public static readonly Color Brand = new Color32(0x7B, 0x4D, 0xFF, 0xFF);
        public static readonly Color BrandDark = new Color32(0x5A, 0x2F, 0xD6, 0xFF);
        public static readonly Color Ink = new Color32(0x3B, 0x1F, 0x5C, 0xFF);
        public static readonly Color Cream = new Color32(0xFF, 0xF6, 0xE5, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0xC8, 0x00, 0xFF);
        public static readonly Color Primary = new Color32(0x58, 0xCC, 0x02, 0xFF);
        public static readonly Color Danger = new Color32(0xFF, 0x4B, 0x4B, 0xFF);
        public static readonly Color Gray = new Color32(0x9A, 0x8F, 0xAD, 0xFF);

        static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>A Screen Space Overlay canvas (DontDestroyOnLoad) scaled to a 1080-wide reference.</summary>
        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.overrideSorting = true;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Creates an EventSystem (Input System UI module) when the scene has none, so overlay buttons work.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem (services)", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretches to the parent with optional insets (left, bottom, right, top).</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Places a rect with a single anchor/pivot point, a position offset and a size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            var rt = Rect(parent, name);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = anchor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(10, size / 2);
            label.resizeTextMaxSize = size;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -3f);
            return label;
        }

        public static Button Button(Transform parent, string name, string text, Color color, int fontSize, Action onClick)
        {
            Image bg = Panel(parent, name, color, true);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            Text label = Label(bg.transform, "Label", text, fontSize, Color.white);
            Stretch(label.rectTransform, 12, 6, 12, 6);
            return button;
        }

        /// <summary>A rect inside the screen safe area (bottom/top insets of notches and home indicators).</summary>
        public static RectTransform SafeArea(RectTransform canvasRoot)
        {
            RectTransform rt = Rect(canvasRoot, "SafeArea");
            Rect safe = Screen.safeArea;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            rt.anchorMin = new Vector2(safe.xMin / w, safe.yMin / h);
            rt.anchorMax = new Vector2(safe.xMax / w, safe.yMax / h);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
