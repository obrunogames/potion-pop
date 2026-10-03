using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>Tokens and profile returned by Firebase Auth (signInWithIdp or token refresh).</summary>
    public sealed class FirebaseSession
    {
        public string uid;
        public string idToken;
        public string refreshToken;
        public long expiresInSeconds;
        public string displayName;
        public string email;
        public string photoUrl;
        public string providerId;
    }

    /// <summary>
    /// Firebase Authentication REST API: endpoints, request bodies, response parsing and error → Loc key mapping.
    /// Pure functions (no I/O) so they can be unit tested. https://firebase.google.com/docs/reference/rest/auth
    /// </summary>
    public static class FirebaseAuthApi
    {
        public const string IdentityToolkitV1 = "https://identitytoolkit.googleapis.com/v1/";
        public const string IdentityToolkitV2 = "https://identitytoolkit.googleapis.com/v2/";
        public const string SecureTokenUrl = "https://securetoken.googleapis.com/v1/token";
        /// <summary>Any URI works for native ID tokens; Firebase requires the field.</summary>
        public const string RequestUri = "http://localhost";

        // Loc keys (Resources/Loc/services.csv)
        public const string ErrCancelled = "auth.error.cancelled";
        public const string ErrNetwork = "auth.error.network";
        public const string ErrUnavailable = "auth.error.unavailable";
        public const string ErrCloudDisabled = "auth.error.cloud_disabled";
        public const string ErrFailed = "auth.error.failed";
        public const string ErrBusy = "auth.error.busy";
        public const string ErrNoAccount = "auth.error.no_account";
        public const string ErrDisabled = "auth.error.disabled";
        public const string ErrSessionExpired = "auth.error.session_expired";
        public const string ErrRelogin = "auth.error.relogin";
        public const string ErrTooMany = "auth.error.too_many";
        public const string ErrWrongAccount = "auth.error.wrong_account";
        public const string ErrDeleteFailed = "auth.error.delete_failed";
        public const string ErrNotSignedIn = "auth.error.not_signed_in";

        public static string SignInWithIdpUrl(string apiKey) => IdentityToolkitV1 + "accounts:signInWithIdp?key=" + Key(apiKey);
        public static string RefreshUrl(string apiKey) => SecureTokenUrl + "?key=" + Key(apiKey);
        public static string DeleteUrl(string apiKey) => IdentityToolkitV1 + "accounts:delete?key=" + Key(apiKey);
        public static string UpdateUrl(string apiKey) => IdentityToolkitV1 + "accounts:update?key=" + Key(apiKey);
        public static string RevokeTokenUrl(string apiKey) => IdentityToolkitV2 + "accounts:revokeToken?key=" + Key(apiKey);

        static string Key(string apiKey) => Uri.EscapeDataString(apiKey ?? "");

        public static string ProviderId(AuthProvider provider) => provider == AuthProvider.Apple ? "apple.com" : "google.com";

        public static AuthProvider ProviderFromId(string providerId)
        {
            if (providerId == "apple.com") return AuthProvider.Apple;
            if (providerId == "google.com") return AuthProvider.Google;
            return AuthProvider.None;
        }

        // ---------------------------------------------------------------------------------------- request bodies

        [Serializable]
        sealed class SignInWithIdpRequest
        {
            public string postBody;
            public string requestUri;
            public bool returnSecureToken;
            public bool returnIdpCredential;
        }

        [Serializable]
        sealed class IdTokenRequest
        {
            public string idToken;
        }

        [Serializable]
        sealed class UpdateProfileRequest
        {
            public string idToken;
            public string displayName;
            public bool returnSecureToken;
        }

        [Serializable]
        sealed class RevokeTokenRequest
        {
            public string providerId;
            public string tokenType;
            public string token;
            public string idToken;
        }

        /// <summary>
        /// accounts:signInWithIdp body for a native ID token. Apple tokens are bound to a nonce: pass the RAW nonce
        /// (Apple received its SHA-256).
        /// </summary>
        public static string BuildSignInWithIdpBody(AuthProvider provider, string idToken, string rawNonce = null)
        {
            var post = new StringBuilder();
            post.Append("id_token=").Append(Http.FormEscape(idToken));
            post.Append("&providerId=").Append(ProviderId(provider));
            if (!string.IsNullOrEmpty(rawNonce)) post.Append("&nonce=").Append(Http.FormEscape(rawNonce));
            return JsonUtility.ToJson(new SignInWithIdpRequest
            {
                postBody = post.ToString(),
                requestUri = RequestUri,
                returnSecureToken = true,
                returnIdpCredential = true,
            });
        }

        /// <summary>securetoken.googleapis.com form body (grant_type=refresh_token).</summary>
        public static string BuildRefreshForm(string refreshToken) =>
            "grant_type=refresh_token&refresh_token=" + Http.FormEscape(refreshToken);

        /// <summary>{"idToken": "..."} — accounts:delete.</summary>
        public static string BuildIdTokenBody(string idToken) => JsonUtility.ToJson(new IdTokenRequest { idToken = idToken });

        public static string BuildUpdateProfileBody(string idToken, string displayName) =>
            JsonUtility.ToJson(new UpdateProfileRequest { idToken = idToken, displayName = displayName, returnSecureToken = false });

        /// <summary>v2 accounts:revokeToken body: revokes the Apple tokens with a fresh authorization code.</summary>
        public static string BuildRevokeAppleBody(string idToken, string authorizationCode) =>
            JsonUtility.ToJson(new RevokeTokenRequest { providerId = "apple.com", tokenType = "CODE", token = authorizationCode, idToken = idToken });

        // ---------------------------------------------------------------------------------------- responses

        [Serializable]
        sealed class SignInWithIdpResponse
        {
            public string localId;
            public string idToken;
            public string refreshToken;
            public string expiresIn;
            public string email;
            public string displayName;
            public string fullName;
            public string firstName;
            public string lastName;
            public string photoUrl;
            public string providerId;
            public bool needConfirmation;
            public string errorMessage;
        }

        [Serializable]
        sealed class RefreshResponse
        {
            public string id_token;
            public string refresh_token;
            public string expires_in;
            public string user_id;
        }

        [Serializable]
        sealed class ErrorEnvelope
        {
            public ErrorBody error;
        }

        [Serializable]
        sealed class ErrorBody
        {
            public int code;
            public string message;
            public string status;
        }

        /// <summary>Parses a 200 accounts:signInWithIdp response. False (with Firebase's message) when it has no tokens.</summary>
        public static bool TryParseSignIn(string json, out FirebaseSession session, out string errorMessage)
        {
            session = null;
            errorMessage = null;
            SignInWithIdpResponse r = FromJson<SignInWithIdpResponse>(json);
            if (r == null)
            {
                errorMessage = "invalid json";
                return false;
            }
            if (string.IsNullOrEmpty(r.idToken) || string.IsNullOrEmpty(r.localId) || string.IsNullOrEmpty(r.refreshToken))
            {
                errorMessage = !string.IsNullOrEmpty(r.errorMessage) ? r.errorMessage : r.needConfirmation ? "NEED_CONFIRMATION" : "missing tokens";
                return false;
            }
            string name = FirstNonEmpty(r.displayName, r.fullName, JoinName(r.firstName, r.lastName));
            session = new FirebaseSession
            {
                uid = r.localId,
                idToken = r.idToken,
                refreshToken = r.refreshToken,
                expiresInSeconds = ParseLong(r.expiresIn, 3600),
                displayName = name,
                email = r.email,
                photoUrl = r.photoUrl,
                providerId = r.providerId,
            };
            return true;
        }

        /// <summary>Parses a 200 securetoken response (snake_case fields).</summary>
        public static bool TryParseRefresh(string json, out FirebaseSession session)
        {
            session = null;
            RefreshResponse r = FromJson<RefreshResponse>(json);
            if (r == null || string.IsNullOrEmpty(r.id_token)) return false;
            session = new FirebaseSession
            {
                uid = r.user_id,
                idToken = r.id_token,
                refreshToken = r.refresh_token,
                expiresInSeconds = ParseLong(r.expires_in, 3600),
            };
            return true;
        }

        /// <summary>The "message" of a Google API error body ({"error":{"code":400,"message":"..."}}), or null.</summary>
        public static string ParseErrorMessage(string json)
        {
            ErrorEnvelope e = FromJson<ErrorEnvelope>(json);
            if (e == null || e.error == null || string.IsNullOrEmpty(e.error.message)) return null;
            return e.error.message;
        }

        /// <summary>The "status" of a Google API error body (e.g. PERMISSION_DENIED), or null.</summary>
        public static string ParseErrorStatus(string json)
        {
            ErrorEnvelope e = FromJson<ErrorEnvelope>(json);
            return e != null && e.error != null && !string.IsNullOrEmpty(e.error.status) ? e.error.status : null;
        }

        /// <summary>Loc key for a failed Firebase Auth / Secure Token call.</summary>
        public static string ErrorLocKey(HttpResult result)
        {
            if (result.networkError || result.status == 0) return ErrNetwork;
            if (result.status >= 500 || result.status == 408) return ErrNetwork;
            return ErrorLocKey(ParseErrorMessage(result.text));
        }

        /// <summary>Loc key for a Firebase Auth error message ("INVALID_IDP_RESPONSE : detail" → code before " : ").</summary>
        public static string ErrorLocKey(string firebaseMessage)
        {
            string code = ErrorCode(firebaseMessage);
            switch (code)
            {
                case "USER_DISABLED":
                    return ErrDisabled;
                case "TOKEN_EXPIRED":
                case "INVALID_REFRESH_TOKEN":
                case "USER_NOT_FOUND":
                case "INVALID_ID_TOKEN":
                case "INVALID_GRANT_TYPE":
                case "MISSING_REFRESH_TOKEN":
                    return ErrSessionExpired;
                case "CREDENTIAL_TOO_OLD_LOGIN_AGAIN":
                    return ErrRelogin;
                case "TOO_MANY_ATTEMPTS_TRY_LATER":
                case "QUOTA_EXCEEDED":
                    return ErrTooMany;
                case "OPERATION_NOT_ALLOWED":
                case "CONFIGURATION_NOT_FOUND":
                case "PROJECT_NOT_FOUND":
                case "INVALID_API_KEY":
                case "API_KEY_INVALID":
                    return ErrUnavailable;
            }
            if (!string.IsNullOrEmpty(firebaseMessage) && firebaseMessage.StartsWith("API key not valid", StringComparison.Ordinal))
                return ErrUnavailable;
            return ErrFailed;
        }

        /// <summary>The refresh token can never work again (account deleted/disabled/revoked): the session must end.</summary>
        public static bool IsSessionInvalid(HttpResult result)
        {
            if (result.networkError || result.status == 0 || result.status >= 500) return false;
            string key = ErrorLocKey(ParseErrorMessage(result.text));
            return key == ErrSessionExpired || key == ErrDisabled;
        }

        /// <summary>"INVALID_IDP_RESPONSE : The supplied auth credential is malformed" → "INVALID_IDP_RESPONSE".</summary>
        public static string ErrorCode(string firebaseMessage)
        {
            if (string.IsNullOrEmpty(firebaseMessage)) return "";
            int cut = firebaseMessage.IndexOf(" : ", StringComparison.Ordinal);
            return (cut >= 0 ? firebaseMessage.Substring(0, cut) : firebaseMessage).Trim();
        }

        // ---------------------------------------------------------------------------------------- helpers

        internal static T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            string trimmed = json.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{') return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }

        internal static long ParseLong(string value, long fallback)
        {
            return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v)
                ? v
                : fallback;
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }

        internal static string JoinName(string first, string last)
        {
            string a = string.IsNullOrWhiteSpace(first) ? "" : first.Trim();
            string b = string.IsNullOrWhiteSpace(last) ? "" : last.Trim();
            string joined = (a + " " + b).Trim();
            return joined.Length > 0 ? joined : null;
        }
    }

    /// <summary>Random nonce for Sign in with Apple (raw value for Firebase, SHA-256 hex for Apple).</summary>
    public static class Nonce
    {
        // 64 symbols → "byte & 63" picks uniformly (no modulo bias).
        const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

        public static string Generate(int length = 32)
        {
            if (length <= 0) return "";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = Charset[bytes[i] & 63];
            return new string(chars);
        }

        /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes (what Apple expects in the authorization request).</summary>
        public static string Sha256Hex(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>Claims we read from a Firebase ID token (no signature check — only used for client-side decisions).</summary>
    [Serializable]
    public class JwtClaims
    {
        public long auth_time;
        public long iat;
        public long exp;
        public string user_id;
        public string sub;
        public string email;
        public string name;
    }

    public static class Jwt
    {
        /// <summary>Decodes the payload of a JWT (header.payload.signature) without verifying it.</summary>
        public static bool TryReadClaims(string jwt, out JwtClaims claims)
        {
            claims = null;
            if (string.IsNullOrEmpty(jwt)) return false;
            string[] parts = jwt.Split('.');
            if (parts.Length < 2) return false;
            try
            {
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                    case 1: return false;
                }
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                claims = FirebaseAuthApi.FromJson<JwtClaims>(json);
                return claims != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
