using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Firebase Authentication through the REST API (identitytoolkit accounts:signInWithIdp) using native ID tokens:
    /// Android Credential Manager / iOS GoogleSignIn (Google), Sign in with Apple (iOS, AppleAuth package).
    /// In the Editor a mock provider simulates the flow (and the cloud lives in mock_cloud.json); on devices without
    /// Firebase config the providers report unavailable and the game stays offline (guest).
    /// The refresh token is persisted (PlayerPrefs) and the session restored on Init (token refreshed in background).
    /// Every callback is invoked exactly once, on the main thread.
    /// </summary>
    public static class AuthService
    {
        const string PrefsKey = "sp.auth.v1";
        /// <summary>The ID token is refreshed when it expires within this margin.</summary>
        const long TokenRefreshMarginSeconds = 120;
        /// <summary>Firebase requires a sign-in younger than ~5 min for accounts:delete; we re-authenticate above this.</summary>
        const long RecentLoginSeconds = 240;

        public static event Action OnAuthChanged;

        [Serializable]
        sealed class PersistedSession
        {
            public string uid;
            public int provider;
            public string displayName;
            public string email;
            public string photoUrl;
            public string refreshToken;
            public bool isMock;
        }

        static bool _initialized;
        static MonoBehaviour _host;
        static AuthUser _user;
        static string _idToken;
        static string _refreshToken;
        static long _idTokenExpiresAt;
        static bool _busy;
        static bool _refreshing;
        static int _generation;
        static readonly List<Action<string>> TokenWaiters = new List<Action<string>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnAuthChanged = null;
            _initialized = false;
            _host = null;
            _user = null;
            _idToken = null;
            _refreshToken = null;
            _idTokenExpiresAt = 0;
            _busy = false;
            _refreshing = false;
            _generation = 0;
            TokenWaiters.Clear();
            LastErrorKey = null;
        }

        public static bool IsSignedIn => _user != null;
        public static AuthUser User => _user;

        /// <summary>True when the real Firebase backend is configured (otherwise sign-in is mock/offline).</summary>
        public static bool CloudAvailable => Config.FirebaseConfigured && !UsingMock;

        /// <summary>The editor mock provider is used instead of native sign-in + Firebase (always in the Editor).</summary>
        public static bool UsingMock => Application.isEditor;

        /// <summary>A sign-in or account deletion flow is running.</summary>
        public static bool IsBusy => _busy;

        /// <summary>Loc key of the last failure (sign-in, refresh, deletion), null after a success.</summary>
        public static string LastErrorKey { get; private set; }

        /// <summary>The host passed to Init (informational: requests run on the persistent ServicesRunner).</summary>
        public static MonoBehaviour Host => _host;

        static ServicesConfig Config => ServicesConfig.Load();
        static long Now => TimeUtil.Now;

        // ---------------------------------------------------------------------------------------- init

        /// <summary>
        /// Restores the saved session and starts the cloud save. Idempotent. The host is kept for reference; network
        /// coroutines run on the internal DontDestroyOnLoad <see cref="ServicesRunner"/> so they survive scene loads.
        /// </summary>
        public static void Init(MonoBehaviour host)
        {
            if (host != null) _host = host;
            if (_initialized) return;
            _initialized = true;
            ServicesRunner.Ensure();
            RestoreSession();
            CloudSave.Attach();
            if (_user != null && !_user.isMock) GetIdToken(null);   // background refresh; ends the session if revoked
            RaiseChanged();
        }

        static void EnsureInit()
        {
            if (!_initialized) Init(null);
        }

        public static bool IsProviderAvailable(AuthProvider provider)
        {
            if (UsingMock) return provider == AuthProvider.Google || provider == AuthProvider.Apple || provider == AuthProvider.Mock;
            if (!Config.FirebaseConfigured) return false;
            switch (provider)
            {
                case AuthProvider.Google: return NativeSignIn.GoogleSupported(Config);
                case AuthProvider.Apple: return NativeSignIn.AppleSupported;
                default: return false;
            }
        }

        // ---------------------------------------------------------------------------------------- sign in

        /// <summary>done(success, errorLocKey). errorLocKey is a Loc key ("auth.error.cancelled", ...).</summary>
        public static void SignIn(AuthProvider provider, Action<bool, string> done)
        {
            EnsureInit();
            bool answered = false;
            Action<bool, string> reply = (ok, key) =>
            {
                if (answered) return;
                answered = true;
                LastErrorKey = ok ? null : key;
                ServicesRunner.SafeInvoke(done, ok, ok ? null : key);
            };

            if (_busy || NativeSignIn.IsBusy)
            {
                reply(false, FirebaseAuthApi.ErrBusy);
                return;
            }
            if (!IsProviderAvailable(provider))
            {
                reply(false, !UsingMock && !Config.FirebaseConfigured ? FirebaseAuthApi.ErrCloudDisabled : FirebaseAuthApi.ErrUnavailable);
                return;
            }

            _busy = true;
            if (UsingMock)
            {
                MockAuthProvider.SignIn(provider, user =>
                {
                    _busy = false;
                    ClearTokens();
                    _user = user;
                    _generation++;
                    Persist();
                    RaiseChanged();
                    reply(true, null);
                });
                return;
            }

            AcquireSession(provider, (ok, key, session, native) =>
            {
                _busy = false;
                if (!ok)
                {
                    reply(false, key);
                    return;
                }
                ApplySession(provider, session, native);
                RaiseChanged();
                reply(true, null);
            });
        }

        /// <summary>Native prompt → ID token → accounts:signInWithIdp. cb(ok, errorKey, session, nativeResult).</summary>
        static void AcquireSession(AuthProvider provider, Action<bool, string, FirebaseSession, NativeSignInResult> cb)
        {
            Action<NativeSignInResult> onNative = native =>
            {
                if (!native.ok)
                {
                    cb(false, native.errorKey ?? FirebaseAuthApi.ErrFailed, null, native);
                    return;
                }
                string body = FirebaseAuthApi.BuildSignInWithIdpBody(provider, native.idToken, native.rawNonce);
                Http.PostJson(FirebaseAuthApi.SignInWithIdpUrl(Config.firebaseApiKey), body, null, r =>
                {
                    if (!r.Ok)
                    {
                        Debug.LogWarning("[Auth] signInWithIdp failed: " + r);
                        cb(false, FirebaseAuthApi.ErrorLocKey(r), null, native);
                        return;
                    }
                    if (!FirebaseAuthApi.TryParseSignIn(r.text, out FirebaseSession session, out string message))
                    {
                        Debug.LogWarning("[Auth] signInWithIdp without session: " + message);
                        cb(false, FirebaseAuthApi.ErrorLocKey(message), null, native);
                        return;
                    }
                    cb(true, null, session, native);
                });
            };
            if (provider == AuthProvider.Apple) NativeSignIn.RequestApple(onNative);
            else NativeSignIn.RequestGoogle(Config, onNative);
        }

        static void ApplySession(AuthProvider provider, FirebaseSession session, NativeSignInResult native)
        {
            string previousName = _user != null && _user.uid == session.uid ? _user.displayName : null;
            string name = FirstNonEmpty(session.displayName, native.displayName, previousName);
            FlushTokenWaiters(null);
            _user = new AuthUser
            {
                uid = session.uid,
                displayName = name ?? "",
                email = FirstNonEmpty(session.email, native.email) ?? "",
                photoUrl = session.photoUrl ?? "",
                provider = provider,
                isMock = false,
            };
            SetTokens(session);
            _generation++;
            Persist();
            // Apple sends the name only once and never inside the token: store it on the Firebase user.
            if (provider == AuthProvider.Apple && string.IsNullOrEmpty(session.displayName) && !string.IsNullOrEmpty(native.displayName))
                UpdateFirebaseDisplayName(native.displayName);
        }

        static void SetTokens(FirebaseSession session)
        {
            _idToken = session.idToken;
            if (!string.IsNullOrEmpty(session.refreshToken)) _refreshToken = session.refreshToken;
            _idTokenExpiresAt = Now + Math.Max(60, session.expiresInSeconds);
        }

        static void UpdateFirebaseDisplayName(string displayName)
        {
            GetIdToken(token =>
            {
                if (string.IsNullOrEmpty(token)) return;
                Http.PostJson(FirebaseAuthApi.UpdateUrl(Config.firebaseApiKey), FirebaseAuthApi.BuildUpdateProfileBody(token, displayName), null, r =>
                {
                    if (!r.Ok) Debug.LogWarning("[Auth] could not store the Apple display name: " + r);
                });
            });
        }

        // ---------------------------------------------------------------------------------------- sign out

        public static void SignOut()
        {
            EnsureInit();
            if (_user == null)
            {
                RaiseChanged();
                return;
            }
            CloudSave.BeforeSignOut();   // best-effort final upload while the token is still valid
            bool nativeGoogle = _user.provider == AuthProvider.Google && !_user.isMock;
            ClearSession();
            if (nativeGoogle) NativeSignIn.SignOutGoogle();
            RaiseChanged();
        }

        static void ClearSession()
        {
            _user = null;
            ClearTokens();
            _generation++;
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        static void ClearTokens()
        {
            _idToken = null;
            _refreshToken = null;
            _idTokenExpiresAt = 0;
            FlushTokenWaiters(null);
        }

        // ---------------------------------------------------------------------------------------- delete

        /// <summary>
        /// Deletes the cloud save and the Firebase user, then signs out (local save is kept). May show the native
        /// sign-in again: Firebase requires a recent login, and Apple tokens are revoked with a fresh authorization
        /// code. On failure see <see cref="LastErrorKey"/>.
        /// </summary>
        public static void DeleteAccount(Action<bool> done)
        {
            EnsureInit();
            bool answered = false;
            Action<bool, string> finish = (ok, key) =>
            {
                if (answered) return;
                answered = true;
                _busy = false;
                CloudSave.SetSuspended(false);
                LastErrorKey = ok ? null : key ?? FirebaseAuthApi.ErrDeleteFailed;
                if (ok)
                {
                    bool nativeGoogle = _user != null && _user.provider == AuthProvider.Google && !_user.isMock;
                    // The kept local save becomes a guest save again (GDD §7), so it can merge into a new account later.
                    try
                    {
                        SaveSystem.Data.cloudUid = "";
                        SaveSystem.MarkDirtyHousekeeping();
                    }
                    catch (Exception e) { Debug.LogException(e); }
                    ClearSession();
                    if (nativeGoogle) NativeSignIn.SignOutGoogle();
                    RaiseChanged();
                }
                else
                {
                    Debug.LogWarning("[Auth] account deletion failed: " + LastErrorKey);
                }
                ServicesRunner.SafeInvoke(done, ok);
            };

            // Refusals that never started the flow must not touch the busy flag / sync suspension of another flow.
            if (_user == null || _busy || NativeSignIn.IsBusy)
            {
                answered = true;
                LastErrorKey = _user == null ? FirebaseAuthApi.ErrNotSignedIn : FirebaseAuthApi.ErrBusy;
                ServicesRunner.SafeInvoke(done, false);
                return;
            }

            _busy = true;
            CloudSave.SetSuspended(true);   // no new upload may recreate the document meanwhile
            string uid = _user.uid;

            if (_user.isMock)
            {
                // An upload already on the wire must land before the delete, or it would recreate the document.
                CloudSave.WhenIdle(() => MockCloudStore.DeletePlayer(uid, _ => finish(true, null)));
                return;
            }

            EnsureRecentLogin(uid, (okLogin, keyLogin, authCode) =>
            {
                if (!okLogin) { finish(false, keyLogin); return; }
                RevokeAppleTokens(authCode, () => CloudSave.WhenIdle(() =>
                    FirestoreClient.DeletePlayer(uid, doc =>
                    {
                        if (!doc.ok) { finish(false, doc.errorKey); return; }
                        GetIdToken(token =>
                        {
                            if (string.IsNullOrEmpty(token)) { finish(false, FirebaseAuthApi.ErrNetwork); return; }
                            Http.PostJson(FirebaseAuthApi.DeleteUrl(Config.firebaseApiKey), FirebaseAuthApi.BuildIdTokenBody(token), null, r =>
                            {
                                if (r.Ok || FirebaseAuthApi.ErrorCode(FirebaseAuthApi.ParseErrorMessage(r.text)) == "USER_NOT_FOUND")
                                {
                                    finish(true, null);
                                    return;
                                }
                                Debug.LogWarning("[Auth] accounts:delete failed: " + r);
                                finish(false, FirebaseAuthApi.ErrorLocKey(r));
                            });
                        });
                    })));
            });
        }

        /// <summary>
        /// Re-authenticates with the same provider when the last login is older than <see cref="RecentLoginSeconds"/>
        /// (always for Apple, to obtain the authorization code needed to revoke its tokens). cb(ok, errorKey, appleCode).
        /// </summary>
        static void EnsureRecentLogin(string uid, Action<bool, string, string> cb)
        {
            AuthProvider provider = _user.provider;
            GetIdToken(token =>
            {
                if (_user == null || _user.uid != uid) { cb(false, FirebaseAuthApi.ErrNotSignedIn, null); return; }
                // No token = offline (or revoked): nothing can be deleted now, don't show the native prompt for nothing.
                if (string.IsNullOrEmpty(token)) { cb(false, FirebaseAuthApi.ErrNetwork, null); return; }
                bool needLogin = provider == AuthProvider.Apple;
                if (!needLogin)
                {
                    long authTime = Jwt.TryReadClaims(token, out JwtClaims claims) ? claims.auth_time : 0;
                    needLogin = authTime <= 0 || Now - authTime > RecentLoginSeconds;
                }
                if (!needLogin) { cb(true, null, null); return; }

                AcquireSession(provider, (ok, key, session, native) =>
                {
                    if (!ok) { cb(false, key, null); return; }
                    if (_user == null || session.uid != uid) { cb(false, FirebaseAuthApi.ErrWrongAccount, null); return; }
                    // New generation: a refresh started with the old refresh token must not overwrite these fresh
                    // tokens (its ID token would carry the old auth_time → CREDENTIAL_TOO_OLD_LOGIN_AGAIN).
                    _generation++;
                    SetTokens(session);
                    Persist();
                    FlushTokenWaiters(_idToken);
                    cb(true, null, native.authorizationCode);
                });
            });
        }

        static void RevokeAppleTokens(string authorizationCode, Action next)
        {
            if (string.IsNullOrEmpty(authorizationCode)) { next(); return; }
            GetIdToken(token =>
            {
                if (string.IsNullOrEmpty(token)) { next(); return; }
                Http.PostJson(FirebaseAuthApi.RevokeTokenUrl(Config.firebaseApiKey), FirebaseAuthApi.BuildRevokeAppleBody(token, authorizationCode), null, r =>
                {
                    // Needs the Apple "Services ID / key" OAuth settings in Firebase; never blocks the deletion.
                    if (!r.Ok) Debug.LogWarning("[Auth] Apple token revocation failed (continuing): " + r);
                    next();
                });
            });
        }

        // ---------------------------------------------------------------------------------------- tokens

        /// <summary>A valid Firebase ID token (refreshing it if needed) or null.</summary>
        public static void GetIdToken(Action<string> done)
        {
            EnsureInit();
            if (_user == null) { ServicesRunner.SafeInvoke(done, (string)null); return; }
            if (_user.isMock) { ServicesRunner.SafeInvoke(done, MockAuthProvider.IdToken); return; }
            if (!string.IsNullOrEmpty(_idToken) && Now < _idTokenExpiresAt - TokenRefreshMarginSeconds)
            {
                ServicesRunner.SafeInvoke(done, _idToken);
                return;
            }
            if (done != null) TokenWaiters.Add(done);
            if (!_refreshing) RefreshIdToken();
        }

        /// <summary>Forces the next <see cref="GetIdToken"/> to refresh (after a 401 from a Google API).</summary>
        public static void InvalidateIdToken() => _idTokenExpiresAt = 0;

        /// <summary>GetIdToken would answer synchronously (mock user, or a cached token that is not about to expire).</summary>
        public static bool HasFreshIdToken =>
            _user != null && (_user.isMock || (!string.IsNullOrEmpty(_idToken) && Now < _idTokenExpiresAt - TokenRefreshMarginSeconds));

        static void RefreshIdToken()
        {
            if (string.IsNullOrEmpty(_refreshToken))
            {
                Debug.LogWarning("[Auth] no refresh token; signing out.");
                FlushTokenWaiters(null);
                LastErrorKey = FirebaseAuthApi.ErrSessionExpired;
                ClearSession();
                RaiseChanged();
                return;
            }
            _refreshing = true;
            int generation = _generation;
            Http.PostForm(FirebaseAuthApi.RefreshUrl(Config.firebaseApiKey), FirebaseAuthApi.BuildRefreshForm(_refreshToken), r =>
            {
                _refreshing = false;
                if (generation != _generation)
                {
                    // Session changed meanwhile: the old waiters were already answered (null). Serve anyone who queued
                    // for the new session behind this stale request.
                    if (TokenWaiters.Count == 0) return;
                    if (HasFreshIdToken) FlushTokenWaiters(_user != null && _user.isMock ? MockAuthProvider.IdToken : _idToken);
                    else if (_user != null) RefreshIdToken();
                    else FlushTokenWaiters(null);
                    return;
                }
                if (r.Ok && FirebaseAuthApi.TryParseRefresh(r.text, out FirebaseSession session))
                {
                    SetTokens(session);
                    Persist();
                    FlushTokenWaiters(_idToken);
                    return;
                }
                if (FirebaseAuthApi.IsSessionInvalid(r))
                {
                    Debug.LogWarning("[Auth] session revoked or expired: " + r);
                    LastErrorKey = FirebaseAuthApi.ErrSessionExpired;
                    FlushTokenWaiters(null);
                    ClearSession();
                    RaiseChanged();
                    return;
                }
                Debug.LogWarning("[Auth] token refresh failed (will retry later): " + r);
                FlushTokenWaiters(null);
            });
        }

        static void FlushTokenWaiters(string token)
        {
            if (TokenWaiters.Count == 0) return;
            Action<string>[] waiters = TokenWaiters.ToArray();
            TokenWaiters.Clear();
            foreach (Action<string> w in waiters) ServicesRunner.SafeInvoke(w, token);
        }

        // ---------------------------------------------------------------------------------------- persistence

        static void Persist()
        {
            if (_user == null)
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                PlayerPrefs.Save();
                return;
            }
            var s = new PersistedSession
            {
                uid = _user.uid,
                provider = (int)_user.provider,
                displayName = _user.displayName,
                email = _user.email,
                photoUrl = _user.photoUrl,
                refreshToken = _user.isMock ? "" : _refreshToken,
                isMock = _user.isMock,
            };
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        static void RestoreSession()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            PersistedSession s = null;
            try { s = JsonUtility.FromJson<PersistedSession>(json); }
            catch (Exception e) { Debug.LogWarning("[Auth] corrupt saved session: " + e.Message); }

            bool valid = s != null && !string.IsNullOrEmpty(s.uid) &&
                         (s.isMock ? UsingMock : !UsingMock && Config.FirebaseConfigured && !string.IsNullOrEmpty(s.refreshToken));
            if (!valid)
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                return;
            }
            _user = new AuthUser
            {
                uid = s.uid,
                displayName = s.displayName ?? "",
                email = s.email ?? "",
                photoUrl = s.photoUrl ?? "",
                provider = (AuthProvider)s.provider,
                isMock = s.isMock,
            };
            _refreshToken = s.isMock ? null : s.refreshToken;
            _idToken = null;
            _idTokenExpiresAt = 0;
            _generation++;
        }

        // ---------------------------------------------------------------------------------------- helpers

        static void RaiseChanged()
        {
            Action handler = OnAuthChanged;
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList()) ServicesRunner.SafeInvoke((Action)d);
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }
    }
}
