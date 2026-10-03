using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// DontDestroyOnLoad GameObject "SPAuthReceiver" that receives the native Google Sign-In results sent with
    /// UnitySendMessage (Android: GoogleSignInBridge.java, iOS: SPGoogleSignIn.mm) and pumps the Sign in with Apple
    /// callbacks every frame (AppleAuthManager.Update).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuthReceiver : MonoBehaviour
    {
        public const string ObjectName = "SPAuthReceiver";
        public const string GoogleResultMethod = "OnGoogleSignInResult";

        static AuthReceiver _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        /// <summary>Creates the receiver if needed (main thread, Play Mode). Returns null in Edit Mode.</summary>
        public static AuthReceiver Ensure()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;
            // Deliberately NOT hidden: UnitySendMessage looks the target up by name like GameObject.Find, so keep it a
            // plain active root object (DontDestroyOnLoad) to stay findable on every Unity version.
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AuthReceiver>();
            return _instance;
        }

        /// <summary>UnitySendMessage target. Payload: "OK|&lt;idToken&gt;" or "ERR|&lt;code&gt;|&lt;message&gt;".</summary>
        public void OnGoogleSignInResult(string payload)
        {
            NativeSignIn.HandleGooglePayload(payload);
        }

        void Update()
        {
            NativeSignIn.Tick();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
