namespace PotionPop.Services
{
    public enum AuthProvider { None, Google, Apple, Mock }

    public sealed class AuthUser
    {
        public string uid;
        public string displayName;
        public string email;
        public string photoUrl;
        public AuthProvider provider;
        /// <summary>Signed in with the editor mock provider (no real Firebase account; cloud = mock_cloud.json).</summary>
        public bool isMock;

        /// <summary>Display name, or "" when the provider gave none (Apple after the first login, for example).</summary>
        public string NameOrEmpty => string.IsNullOrWhiteSpace(displayName) ? "" : displayName.Trim();
    }
}
