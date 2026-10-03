using System;
using System.Text;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Extensions;
using AppleAuth.Interfaces;
using AppleAuth.Native;
#endif

namespace PotionPop.Services
{
    /// <summary>Result of a native sign-in prompt (the ID token Firebase will exchange).</summary>
    public struct NativeSignInResult
    {
        public bool ok;
        public bool cancelled;
        public string idToken;
        /// <summary>Apple only: the raw nonce whose SHA-256 was sent to Apple (Firebase verifies it).</summary>
        public string rawNonce;
        /// <summary>Apple only: authorization code (used to revoke the Apple tokens when the account is deleted).</summary>
        public string authorizationCode;
        /// <summary>Apple only: full name, given only on the very first authorization.</summary>
        public string displayName;
        public string email;
        /// <summary>Loc key when !ok (auth.error.*).</summary>
        public string errorKey;
        /// <summary>Technical detail for logs.</summary>
        public string detail;

        public static NativeSignInResult Fail(string errorKey, string detail, bool cancelled = false) =>
            new NativeSignInResult { ok = false, cancelled = cancelled, errorKey = errorKey, detail = detail };
    }

    /// <summary>
    /// Native ID-token providers:
    /// * Google on Android — androidx Credential Manager ("Sign in with Google") via com.potionpop.auth.GoogleSignInBridge.
    /// * Google on iOS — GoogleSignIn SDK via _SP_GoogleSignIn (Plugins/iOS/SPGoogleSignIn.mm).
    /// * Apple on iOS — AppleAuth package (lupidan), with a SHA-256 nonce.
    /// Google results come back through UnitySendMessage → <see cref="AuthReceiver"/>.
    /// </summary>
    public static class NativeSignIn
    {
        const float GoogleTimeoutSeconds = 300f;
        const string AndroidBridgeClass = "com.potionpop.auth.GoogleSignInBridge";

        static Action<NativeSignInResult> _pendingGoogle;
        static float _pendingGoogleSince;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _SP_GoogleSignIn(string clientId, string serverClientId, string objectName, string method);

        [DllImport("__Internal")]
        static extern void _SP_GoogleSignOut();
#endif

#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
        static AppleAuthManager _apple;
        static Action<NativeSignInResult> _pendingApple;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _pendingGoogle = null;
            _pendingGoogleSince = 0f;
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
            _apple = null;
            _pendingApple = null;
#endif
        }

        // ---------------------------------------------------------------------------------------- availability

        /// <summary>Native Google sign-in exists on this build/platform and its client id is configured.</summary>
        public static bool GoogleSupported(ServicesConfig config)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return config != null && !string.IsNullOrEmpty(config.googleWebClientId);
#elif UNITY_IOS && !UNITY_EDITOR
            return config != null && !string.IsNullOrEmpty(config.googleIosClientId);
#else
            return false;
#endif
        }

        /// <summary>Sign in with Apple is available (iOS 13+ with the AppleAuth package).</summary>
        public static bool AppleSupported
        {
            get
            {
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
                return AppleAuthManager.IsCurrentPlatformSupported;
#else
                return false;
#endif
            }
        }

        public static bool IsBusy
        {
            get
            {
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
                if (_pendingApple != null) return true;
#endif
                return _pendingGoogle != null;
            }
        }

        // ---------------------------------------------------------------------------------------- Google

        /// <summary>Shows the native Google account picker. <paramref name="done"/> is called once on the main thread.</summary>
        public static void RequestGoogle(ServicesConfig config, Action<NativeSignInResult> done)
        {
            if (_pendingGoogle != null)
            {
                ServicesRunner.SafeInvoke(done, NativeSignInResult.Fail(FirebaseAuthApi.ErrBusy, "google sign-in already running"));
                return;
            }
            if (!GoogleSupported(config))
            {
                ServicesRunner.SafeInvoke(done, NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, "google sign-in not supported/configured"));
                return;
            }
            AuthReceiver.Ensure();
            _pendingGoogle = done;
            _pendingGoogleSince = Time.realtimeSinceStartup;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(AndroidBridgeClass))
                {
                    bridge.CallStatic("signIn", activity, config.googleWebClientId, AuthReceiver.ObjectName, AuthReceiver.GoogleResultMethod);
                }
#elif UNITY_IOS && !UNITY_EDITOR
                _SP_GoogleSignIn(config.googleIosClientId, config.googleWebClientId ?? "", AuthReceiver.ObjectName, AuthReceiver.GoogleResultMethod);
#else
                CompleteGoogle(NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, "no native google sign-in on this platform"));
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                CompleteGoogle(NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, e.Message));
            }
        }

        /// <summary>Forgets the Google account choice on the device (next sign-in shows the picker again).</summary>
        public static void SignOutGoogle()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(AndroidBridgeClass))
                {
                    bridge.CallStatic("signOut", activity);
                }
#elif UNITY_IOS && !UNITY_EDITOR
                _SP_GoogleSignOut();
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Auth] native Google sign-out failed: " + e.Message);
            }
        }

        /// <summary>Called by <see cref="AuthReceiver"/> with the UnitySendMessage payload.</summary>
        public static void HandleGooglePayload(string payload)
        {
            NativeSignInResult result = ParseGooglePayload(payload);
            if (!result.ok) Debug.LogWarning("[Auth] Google sign-in failed: " + result.errorKey + " (" + result.detail + ")");
            CompleteGoogle(result);
        }

        static void CompleteGoogle(NativeSignInResult result)
        {
            Action<NativeSignInResult> done = _pendingGoogle;
            _pendingGoogle = null;
            ServicesRunner.SafeInvoke(done, result);
        }

        /// <summary>
        /// Parses "OK|&lt;idToken&gt;" or "ERR|&lt;code&gt;|&lt;message&gt;" (code: cancelled, nocredential, network,
        /// config, failed...). The message may itself contain '|'.
        /// </summary>
        public static NativeSignInResult ParseGooglePayload(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return NativeSignInResult.Fail(FirebaseAuthApi.ErrFailed, "empty payload");
            string[] parts = payload.Split(new[] { '|' }, 3);
            if (parts[0] == "OK")
            {
                string token = parts.Length > 1 ? parts[1].Trim() : "";
                return token.Length > 0
                    ? new NativeSignInResult { ok = true, idToken = token }
                    : NativeSignInResult.Fail(FirebaseAuthApi.ErrFailed, "empty token");
            }
            string code = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "failed";
            string message = parts.Length > 2 ? parts[2] : "";
            switch (code)
            {
                case "cancelled":
                case "canceled":
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrCancelled, message, true);
                case "nocredential":
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrNoAccount, message);
                case "network":
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrNetwork, message);
                case "config":
                case "unavailable":
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, message);
                case "busy":
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrBusy, message);
                default:
                    return NativeSignInResult.Fail(FirebaseAuthApi.ErrFailed, code + ": " + message);
            }
        }

        // ---------------------------------------------------------------------------------------- Apple

        /// <summary>Shows the Sign in with Apple sheet (iOS). <paramref name="done"/> is called once on the main thread.</summary>
        public static void RequestApple(Action<NativeSignInResult> done)
        {
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
            if (_pendingApple != null)
            {
                ServicesRunner.SafeInvoke(done, NativeSignInResult.Fail(FirebaseAuthApi.ErrBusy, "apple sign-in already running"));
                return;
            }
            if (!AppleSupported)
            {
                ServicesRunner.SafeInvoke(done, NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, "apple sign-in not supported"));
                return;
            }
            AuthReceiver.Ensure();   // pumps AppleAuthManager.Update()
            _apple ??= new AppleAuthManager(new PayloadDeserializer());
            _pendingApple = done;
            string rawNonce = Nonce.Generate(32);
            var args = new AppleAuthLoginArgs(LoginOptions.IncludeEmail | LoginOptions.IncludeFullName, Nonce.Sha256Hex(rawNonce));
            try
            {
                _apple.LoginWithAppleId(args,
                    credential => CompleteApple(FromAppleCredential(credential, rawNonce)),
                    error =>
                    {
                        AuthorizationErrorCode code = error.GetAuthorizationErrorCode();
                        bool cancelled = code == AuthorizationErrorCode.Canceled;
                        string detail = code + " " + error.Code + " " + error.LocalizedDescription;
                        CompleteApple(NativeSignInResult.Fail(cancelled ? FirebaseAuthApi.ErrCancelled : FirebaseAuthApi.ErrFailed, detail, cancelled));
                    });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                CompleteApple(NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, e.Message));
            }
#else
            ServicesRunner.SafeInvoke(done, NativeSignInResult.Fail(FirebaseAuthApi.ErrUnavailable, "sign in with apple not available"));
#endif
        }

#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
        static NativeSignInResult FromAppleCredential(ICredential credential, string rawNonce)
        {
            var apple = credential as IAppleIDCredential;
            if (apple == null || apple.IdentityToken == null || apple.IdentityToken.Length == 0)
                return NativeSignInResult.Fail(FirebaseAuthApi.ErrFailed, "apple credential without identity token");
            string name = null;
            if (apple.FullName != null) name = FirebaseAuthApi.JoinName(apple.FullName.GivenName, apple.FullName.FamilyName);
            return new NativeSignInResult
            {
                ok = true,
                idToken = Encoding.UTF8.GetString(apple.IdentityToken),
                rawNonce = rawNonce,
                authorizationCode = apple.AuthorizationCode != null && apple.AuthorizationCode.Length > 0
                    ? Encoding.UTF8.GetString(apple.AuthorizationCode)
                    : null,
                displayName = name,
                email = apple.Email,
            };
        }

        static void CompleteApple(NativeSignInResult result)
        {
            Action<NativeSignInResult> done = _pendingApple;
            _pendingApple = null;
            if (!result.ok) Debug.LogWarning("[Auth] Apple sign-in failed: " + result.errorKey + " (" + result.detail + ")");
            ServicesRunner.Post(() => ServicesRunner.SafeInvoke(done, result));
        }
#endif

        // ---------------------------------------------------------------------------------------- per frame

        /// <summary>Called every frame by <see cref="AuthReceiver"/>.</summary>
        public static void Tick()
        {
#if SP_APPLE_AUTH && UNITY_IOS && !UNITY_EDITOR
            _apple?.Update();
#endif
            // Safety net: if the native side never answers (activity recreated, plugin missing), release the flow.
            if (_pendingGoogle != null && Time.realtimeSinceStartup - _pendingGoogleSince > GoogleTimeoutSeconds)
                CompleteGoogle(NativeSignInResult.Fail(FirebaseAuthApi.ErrCancelled, "timeout", true));
        }

        // ---------------------------------------------------------------------------------------- Android helpers

        /// <summary>SHA-1 of the APK signing certificate (upper-case hex, no colons) or "" — for API-key restrictions.</summary>
        public static string AndroidSigningCertSha1()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(AndroidBridgeClass))
                {
                    return bridge.CallStatic<string>("getSigningCertSha1", activity) ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Auth] could not read the signing certificate: " + e.Message);
                return "";
            }
#else
            return "";
#endif
        }
    }
}
