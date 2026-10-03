using System;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Editor sign-in simulator: after ~1 s returns the fake user "Luna Fan" with a uid that is stable per machine
    /// ("mock-" + hash of SystemInfo.deviceUniqueIdentifier). Works for Google and Apple buttons alike.
    /// </summary>
    public static class MockAuthProvider
    {
        public const string DisplayName = "Luna Fan";
        public const string Email = "luna.fan@example.com";
        public const string IdToken = "mock-id-token";
        public const float DelaySeconds = 1f;

        public static string Uid
        {
            get
            {
                string device = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(device) || device == SystemInfo.unsupportedIdentifier) device = "editor";
                return "mock-" + Nonce.Sha256Hex(device).Substring(0, 12);
            }
        }

        public static AuthUser CreateUser(AuthProvider provider) => new AuthUser
        {
            uid = Uid,
            displayName = DisplayName,
            email = Email,
            photoUrl = "",
            provider = provider == AuthProvider.None ? AuthProvider.Mock : provider,
            isMock = true,
        };

        /// <summary>Simulates the native prompt + Firebase exchange. done(user) on the main thread.</summary>
        public static void SignIn(AuthProvider provider, Action<AuthUser> done)
        {
            AuthUser user = CreateUser(provider);
            if (ServicesRunner.Delay(DelaySeconds, () => ServicesRunner.SafeInvoke(done, user)) == null)
                ServicesRunner.SafeInvoke(done, user);
        }
    }
}
