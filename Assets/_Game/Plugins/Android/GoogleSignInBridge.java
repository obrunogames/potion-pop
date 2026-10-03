package com.potionpop.auth;

import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.os.Build;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import androidx.annotation.Keep;
import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.GetCredentialInterruptedException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;

import java.security.MessageDigest;
import java.util.Locale;
import java.util.concurrent.Executor;

/**
 * Google Sign-In for Potion Pop! through androidx Credential Manager ("Sign in with Google" bottom sheet).
 *
 * Called from C# (PotionPop.Services.NativeSignIn) with AndroidJavaClass. The result goes back to Unity with
 * UnityPlayer.UnitySendMessage(objectName, method, payload):
 *   "OK|<google id token>"            on success
 *   "ERR|<code>|<message>"            code = cancelled | nocredential | network | config | failed
 *
 * The ID token's audience is the OAuth "Web application" client id (serverClientId) — the one Firebase Auth accepts in
 * accounts:signInWithIdp. Dependencies (EDM4U, Assets/_Game/Editor/PotionPopDependencies.xml):
 * androidx.credentials:credentials, androidx.credentials:credentials-play-services-auth,
 * com.google.android.libraries.identity.googleid:googleid.
 */
@Keep
public final class GoogleSignInBridge {
    private static final String TAG = "SPGoogleSignIn";

    /**
     * Callbacks run on the UI thread. Own executor instead of ContextCompat.getMainExecutor: androidx.core is not part of
     * the compile classpath our dependencies guarantee (androidx.annotation is, through androidx.credentials' API).
     */
    private static final Executor MAIN = new Executor() {
        private final Handler handler = new Handler(Looper.getMainLooper());

        @Override
        public void execute(Runnable command) {
            if (!handler.post(command)) Log.w(TAG, "main looper gone; callback dropped");
        }
    };

    private GoogleSignInBridge() {
    }

    /** Shows the Google account picker. Fallback: the classic Google ID option if the button flow is unavailable. */
    @Keep
    public static void signIn(final Activity activity, final String serverClientId, final String objectName, final String method) {
        if (activity == null || serverClientId == null || serverClientId.isEmpty()) {
            send(objectName, method, error("config", "missing activity or web client id"));
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(serverClientId).build();
                    GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
                    request(activity, request, objectName, method, new Runnable() {
                        @Override
                        public void run() {
                            fallback(activity, serverClientId, objectName, method);
                        }
                    });
                } catch (Throwable t) {
                    Log.w(TAG, "sign-in setup failed", t);
                    fallback(activity, serverClientId, objectName, method);
                }
            }
        });
    }

    private static void fallback(final Activity activity, final String serverClientId, final String objectName, final String method) {
        try {
            GetGoogleIdOption option = new GetGoogleIdOption.Builder()
                    .setServerClientId(serverClientId)
                    .setFilterByAuthorizedAccounts(false)
                    .setAutoSelectEnabled(false)
                    .build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
            request(activity, request, objectName, method, null);
        } catch (Throwable t) {
            Log.w(TAG, "fallback sign-in failed", t);
            send(objectName, method, error("config", String.valueOf(t)));
        }
    }

    private static void request(final Activity activity, GetCredentialRequest request, final String objectName, final String method,
                                final Runnable onRetryableError) {
        CredentialManager manager = CredentialManager.create(activity);
        manager.getCredentialAsync(activity, request, new CancellationSignal(), MAIN,
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override
                    public void onResult(GetCredentialResponse response) {
                        handleCredential(response.getCredential(), objectName, method);
                    }

                    @Override
                    public void onError(GetCredentialException e) {
                        if (e instanceof GetCredentialCancellationException) {
                            send(objectName, method, error("cancelled", message(e)));
                            return;
                        }
                        if (e instanceof GetCredentialInterruptedException) {
                            send(objectName, method, error("cancelled", "interrupted: " + message(e)));
                            return;
                        }
                        if (onRetryableError != null) {
                            Log.w(TAG, "sign in with google failed (" + e.getType() + "), trying the google id option", e);
                            onRetryableError.run();
                            return;
                        }
                        String code = e instanceof NoCredentialException ? "nocredential" : looksLikeNetwork(e) ? "network" : "failed";
                        send(objectName, method, error(code, e.getType() + ": " + message(e)));
                    }
                });
    }

    private static void handleCredential(Credential credential, String objectName, String method) {
        try {
            if (credential instanceof CustomCredential
                    && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                GoogleIdTokenCredential google = GoogleIdTokenCredential.createFrom(credential.getData());
                String token = google.getIdToken();
                if (token != null && !token.isEmpty()) {
                    send(objectName, method, "OK|" + token);
                    return;
                }
                send(objectName, method, error("failed", "empty id token"));
                return;
            }
            send(objectName, method, error("failed", "unexpected credential type " + (credential != null ? credential.getType() : "null")));
        } catch (Throwable t) {
            Log.w(TAG, "could not read the google credential", t);
            send(objectName, method, error("failed", String.valueOf(t)));
        }
    }

    /** Forgets the account choice so the next sign-in shows the picker again. */
    @Keep
    public static void signOut(final Activity activity) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    CredentialManager manager = CredentialManager.create(activity);
                    manager.clearCredentialStateAsync(new ClearCredentialStateRequest(), new CancellationSignal(), MAIN,
                            new CredentialManagerCallback<Void, ClearCredentialException>() {
                                @Override
                                public void onResult(Void result) {
                                }

                                @Override
                                public void onError(ClearCredentialException e) {
                                    Log.w(TAG, "clearCredentialState failed", e);
                                }
                            });
                } catch (Throwable t) {
                    Log.w(TAG, "sign-out failed", t);
                }
            }
        });
    }

    /** SHA-1 of the signing certificate (upper-case hex, no colons) for API-key app restrictions, or "". */
    @Keep
    @SuppressWarnings("deprecation")
    public static String getSigningCertSha1(Context context) {
        if (context == null) return "";
        try {
            PackageManager pm = context.getPackageManager();
            String pkg = context.getPackageName();
            Signature[] signatures;
            if (Build.VERSION.SDK_INT >= 28) {
                PackageInfo info = pm.getPackageInfo(pkg, PackageManager.GET_SIGNING_CERTIFICATES);
                signatures = info.signingInfo != null ? info.signingInfo.getApkContentsSigners() : null;
            } else {
                PackageInfo info = pm.getPackageInfo(pkg, PackageManager.GET_SIGNATURES);
                signatures = info.signatures;
            }
            if (signatures == null || signatures.length == 0) return "";
            byte[] digest = MessageDigest.getInstance("SHA-1").digest(signatures[0].toByteArray());
            StringBuilder sb = new StringBuilder(digest.length * 2);
            for (byte b : digest) sb.append(String.format(Locale.US, "%02X", b));
            return sb.toString();
        } catch (Throwable t) {
            Log.w(TAG, "could not read the signing certificate", t);
            return "";
        }
    }

    private static boolean looksLikeNetwork(GetCredentialException e) {
        String m = (message(e) + " " + e.getType()).toLowerCase(Locale.US);
        return m.contains("network") || m.contains("timeout") || m.contains("connect");
    }

    private static String message(Throwable t) {
        String m = t.getMessage();
        return m != null ? m : t.getClass().getSimpleName();
    }

    private static String error(String code, String message) {
        String clean = message == null ? "" : message.replace('\n', ' ').replace('\r', ' ');
        return "ERR|" + code + "|" + clean;
    }

    private static void send(String objectName, String method, String payload) {
        try {
            UnityPlayer.UnitySendMessage(objectName, method, payload);
        } catch (Throwable t) {
            Log.e(TAG, "UnitySendMessage failed", t);
        }
    }
}
