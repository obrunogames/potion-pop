// ============================================================================================================
// "Sign in with Google" / "Sign in with Apple" buttons shared by LoginPopup, SettingsPopup and ProfileScreen.
// Google: white pill, official "G" logo on the left, #1F1F1F label (Google branding guidelines).
// Apple: black pill, white Apple logo glyph (U+F8FF, rendered with an OS font created at runtime) + white label;
// the glyph is skipped when no OS font provides it. Only providers reported by AuthService.IsProviderAvailable are
// shown. While AuthService.IsBusy the pressed button shows a spinner. On success CloudSave.OnSignedIn is called
// (the "restored" toast belongs to GameRoot); on failure the error key is toasted.
// ============================================================================================================
using System;
using PotionPop.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public sealed class SignInButtons : MonoBehaviour
    {
        public const float DefaultHeight = 132f;
        public const float Gap = 22f;
        const char AppleLogo = '';

        static TMP_FontAsset _appleFont;
        static bool _appleFontSearched;

        Action<bool> _onDone;
        UIButton _google, _apple;
        CommonSpinner _googleSpinner, _appleSpinner;
        AuthProvider _pending = AuthProvider.None;
        bool _shownBusy;

        /// <summary>At least one provider button was created.</summary>
        public bool HasAnyProvider => _google != null || _apple != null;
        /// <summary>Total height of the stacked buttons (0 when no provider is available).</summary>
        public float Height { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _appleFont = null;
            _appleFontSearched = false;
        }

        /// <summary>True when AuthService can sign in with Google or Apple on this device.</summary>
        public static bool AnyProviderAvailable =>
            AuthService.IsProviderAvailable(AuthProvider.Google) || AuthService.IsProviderAvailable(AuthProvider.Apple);

        /// <summary>
        /// Builds the available buttons stacked from the top of a `width` wide root (root height = <see cref="Height"/>).
        /// onDone(true) after a successful sign-in (CloudSave already notified), onDone(false) after a failure.
        /// </summary>
        public static SignInButtons Create(Transform parent, float width, Action<bool> onDone, float buttonHeight = DefaultHeight)
        {
            var root = UIKit.Rect("SignInButtons", parent);
            var sb = root.gameObject.AddComponent<SignInButtons>();
            sb._onDone = onDone;
            float y = 0f;
            if (AuthService.IsProviderAvailable(AuthProvider.Google))
            {
                sb._google = sb.BuildGoogle(root, new Vector2(width, buttonHeight), out sb._googleSpinner);
                UIKit.Place((RectTransform)sb._google.transform, new Vector2(0.5f, 1f), new Vector2(width, buttonHeight), new Vector2(0f, -y));
                y += buttonHeight + Gap;
            }
            if (AuthService.IsProviderAvailable(AuthProvider.Apple))
            {
                sb._apple = sb.BuildApple(root, new Vector2(width, buttonHeight), out sb._appleSpinner);
                UIKit.Place((RectTransform)sb._apple.transform, new Vector2(0.5f, 1f), new Vector2(width, buttonHeight), new Vector2(0f, -y));
                y += buttonHeight + Gap;
            }
            sb.Height = Mathf.Max(0f, y - Gap);
            root.sizeDelta = new Vector2(width, sb.Height);
            sb.ApplyBusy(true);
            return sb;
        }

        // ---------------------------------------------------------------------------------------- build

        UIButton BuildGoogle(Transform parent, Vector2 size, out CommonSpinner spinner)
        {
            var b = UIKit.Button(parent, "", ButtonColor.Gray, size, () => Press(AuthProvider.Google));
            b.name = "GoogleSignIn";
            b.SetBackground("ui_capsule");
            AddOutline(b, size, DS.Hex("747775"));
            AddLift(b, size);
            b.SetIcon("brand_google_g");
            SizeRowIcon(b.icon, size.y * 0.4f);
            StyleLabel(b, "auth.signin_google", size.y, CommonUI.GoogleText);
            spinner = CommonUI.Spinner(b.Visual, size.y * 0.5f, CommonUI.GoogleText);
            spinner.SetVisible(false);
            return b;
        }

        UIButton BuildApple(Transform parent, Vector2 size, out CommonSpinner spinner)
        {
            var b = UIKit.Button(parent, "", ButtonColor.Gray, size, () => Press(AuthProvider.Apple));
            b.name = "AppleSignIn";
            b.SetBackground("ui_capsule");
            if (b.background != null) b.background.color = Color.black;
            AddLift(b, size);
            StyleLabel(b, "auth.signin_apple", size.y, Color.white);
            var font = AppleGlyphFont();
            if (font != null && b.ContentRow != null)
            {
                var glyph = UIKit.Text(b.ContentRow, AppleLogo.ToString(), TextStyle.Body, new Vector2(size.y * 0.42f, size.y * 0.6f));
                glyph.name = "AppleLogo";
                glyph.font = font;
                glyph.fontSharedMaterial = font.material;
                glyph.enableAutoSizing = false;
                glyph.fontSize = size.y * 0.42f;
                glyph.color = Color.white;
                glyph.textWrappingMode = TextWrappingModes.NoWrap;
                glyph.alignment = TextAlignmentOptions.Center;
                var le = glyph.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = size.y * 0.42f;
                le.minHeight = le.preferredHeight = size.y * 0.6f;
                le.flexibleWidth = 0f;
                glyph.transform.SetSiblingIndex(0);
            }
            spinner = CommonUI.Spinner(b.Visual, size.y * 0.5f, Color.white);
            spinner.SetVisible(false);
            return b;
        }

        static void StyleLabel(UIButton b, string key, float h, Color color)
        {
            if (b.label == null) return;
            DS.Apply(b.label, TextStyle.Body, Mathf.Min(46f, h * 0.36f));
            b.label.color = color;
            b.label.textWrappingMode = TextWrappingModes.NoWrap;
            b.SetLabelKey(key);
        }

        static void SizeRowIcon(Image icon, float s)
        {
            if (icon == null) return;
            icon.rectTransform.sizeDelta = new Vector2(s, s);
            var le = icon.GetComponent<LayoutElement>();
            if (le == null) return;
            le.minWidth = le.preferredWidth = s;
            le.minHeight = le.preferredHeight = s;
        }

        /// <summary>Thin pill outline behind the white Google background.</summary>
        static void AddOutline(UIButton b, Vector2 size, Color color)
        {
            if (b.Visual == null) return;
            var outline = UIKit.Capsule(b.Visual, size, color);
            outline.name = "Outline";
            UIKit.Stretch(outline.rectTransform, -3f, -3f, -3f, -3f);
            outline.transform.SetAsFirstSibling();
        }

        /// <summary>Soft drop shadow under the pill so it reads as a button on any surface.</summary>
        static void AddLift(UIButton b, Vector2 size)
        {
            if (b.Visual == null) return;
            var shadow = UIKit.Capsule(b.Visual, size, DS.WithAlpha(DS.Colors.Ink, 0.25f));
            shadow.name = "Lift";
            UIKit.Stretch(shadow.rectTransform, 2f, -2f, 2f, 8f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            shadow.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// Runtime TMP font that has the Apple logo (U+F8FF): an OS font (Font.CreateDynamicFontFromOSFont →
        /// TMP_FontAsset.CreateFontAsset), else a system family looked up by name. Null when no OS font has the glyph
        /// (non-Apple platforms): the button then shows its text only.
        /// </summary>
        static TMP_FontAsset AppleGlyphFont()
        {
            if (_appleFontSearched) return _appleFont;
            _appleFontSearched = true;
            try
            {
                var os = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", ".SF NS", "Arial" }, 64);
                if (os != null)
                {
                    var fa = TMP_FontAsset.CreateFontAsset(os);
                    if (HasAppleGlyph(fa)) return _appleFont = fa;
                    if (fa != null) Destroy(fa);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SignIn] OS font for the Apple logo failed: " + e.Message); }

            string[] families = { "Helvetica Neue", "Helvetica", "Lucida Grande", ".SF NS", "SF Pro Text" };
            foreach (var family in families)
            {
                try
                {
                    var fa = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (HasAppleGlyph(fa)) return _appleFont = fa;
                    if (fa != null) Destroy(fa);
                }
                catch (Exception) { /* family missing on this platform */ }
            }
            return null;
        }

        static bool HasAppleGlyph(TMP_FontAsset fa)
        {
            if (fa == null) return false;
            try { return fa.HasCharacter(AppleLogo, false, true); }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------------------------------- behaviour

        void Update()
        {
            if (AuthService.IsBusy != _shownBusy) ApplyBusy(false);
        }

        void ApplyBusy(bool force)
        {
            bool busy = AuthService.IsBusy;
            if (!force && busy == _shownBusy) return;
            _shownBusy = busy;
            if (!busy) _pending = AuthProvider.None;
            SetButtonBusy(_google, _googleSpinner, busy, busy && (_pending == AuthProvider.Google || _pending == AuthProvider.None));
            SetButtonBusy(_apple, _appleSpinner, busy, busy && _pending == AuthProvider.Apple);
        }

        static void SetButtonBusy(UIButton b, CommonSpinner spinner, bool anyBusy, bool spinning)
        {
            if (b == null) return;
            b.Interactable = !anyBusy;
            if (b.ContentRow != null) b.ContentRow.gameObject.SetActive(!spinning);
            if (spinner != null) spinner.SetVisible(spinning);
        }

        void Press(AuthProvider provider)
        {
            if (AuthService.IsBusy)
            {
                UIKit.Toast(Loc.T("auth.error.busy"));
                return;
            }
            _pending = provider;
            var onDone = _onDone;
            var self = this;
            AuthService.SignIn(provider, (ok, key) => HandleResult(self, onDone, ok, key));
            if (this != null) ApplyBusy(true);   // spinner on the pressed button while the flow runs
        }

        /// <summary>Static so a sign-in that finishes after this UI was destroyed still notifies CloudSave.</summary>
        static void HandleResult(SignInButtons self, Action<bool> onDone, bool ok, string errorKey)
        {
            if (self != null)
            {
                self._pending = AuthProvider.None;
                self.ApplyBusy(true);
            }
            if (ok)
            {
                AudioManager.Play(Sfx.Reward);
                Haptics.Play(HapticType.Success);
                CloudSave.OnSignedIn(restored =>
                {
                    if (restored) return;   // GameRoot toasts "progress restored"
                    UIKit.Toast(SignedInMessage());
                });
            }
            else if (!string.IsNullOrEmpty(errorKey))
            {
                AudioManager.Play(Sfx.Error);
                UIKit.Toast(Loc.T(errorKey));
            }
            CommonUI.SafeInvoke(onDone, ok);
        }

        static string SignedInMessage()
        {
            if (CloudSave.State == SyncState.Synced) return Loc.T("cloud.kept_local");
            var user = AuthService.User;
            string name = user != null ? user.NameOrEmpty : "";
            if (!string.IsNullOrEmpty(name)) return Loc.T("auth.signed_in_as", name);
            return Loc.T("auth.signed_in_with", Loc.T(ProviderNameKey(user != null ? user.provider : AuthProvider.None)));
        }

        /// <summary>"auth.provider.google" / "auth.provider.apple" (mock counts as Google).</summary>
        public static string ProviderNameKey(AuthProvider p) => p == AuthProvider.Apple ? "auth.provider.apple" : "auth.provider.google";

        /// <summary>Small provider logo for "signed in with" rows: the Google "G" or a white Apple glyph on black.</summary>
        public static RectTransform ProviderLogo(Transform parent, AuthProvider provider, float size)
        {
            var root = UIKit.Rect("ProviderLogo", parent);
            root.sizeDelta = new Vector2(size, size);
            var disc = UIKit.NewImage(root, "Disc", UISprites.Circle, provider == AuthProvider.Apple ? Color.black : Color.white);
            UIKit.Stretch(disc.rectTransform);
            if (provider == AuthProvider.Apple)
            {
                var font = AppleGlyphFont();
                if (font != null)
                {
                    var glyph = UIKit.Text(root, AppleLogo.ToString(), TextStyle.Body, new Vector2(size, size));
                    glyph.font = font;
                    glyph.fontSharedMaterial = font.material;
                    glyph.enableAutoSizing = false;
                    glyph.fontSize = size * 0.55f;
                    glyph.color = Color.white;
                    UIKit.Stretch(glyph.rectTransform, 0f, 0f, 0f, size * 0.06f);
                }
            }
            else
            {
                var g = UIKit.Image(root, "brand_google_g", new Vector2(size * 0.62f, size * 0.62f));
                UIKit.Place(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size * 0.62f, size * 0.62f), Vector2.zero);
            }
            return root;
        }
    }
}
