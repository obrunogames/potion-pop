using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PotionPop.Services
{
    /// <summary>Outcome of an HTTP call. <see cref="Ok"/> = 2xx. <see cref="NetworkError"/> = no HTTP answer at all.</summary>
    public struct HttpResult
    {
        public long status;
        public string text;
        public bool networkError;
        public string error;

        public bool Ok => !networkError && status >= 200 && status < 300;

        public static HttpResult Offline(string why) => new HttpResult { networkError = true, error = why, text = "" };

        public override string ToString() => networkError ? "network error: " + error : "HTTP " + status + " " + Truncate(text, 300);

        static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    /// <summary>
    /// Tiny REST client over UnityWebRequest for the Google APIs (Firebase Auth, Secure Token, Firestore). Requests run
    /// as coroutines on <see cref="ServicesRunner"/>, so callbacks always arrive on the main thread and never block it.
    /// Adds the platform headers Google uses to enforce API-key application restrictions.
    /// </summary>
    public static class Http
    {
        public const int TimeoutSeconds = 20;
        public const string JsonType = "application/json";
        public const string FormType = "application/x-www-form-urlencoded";

        static string _androidCert;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _androidCert = null;

        public static void Get(string url, string bearer, Action<HttpResult> done) => Send("GET", url, null, null, bearer, done);

        public static void PostJson(string url, string json, string bearer, Action<HttpResult> done) =>
            Send("POST", url, json, JsonType, bearer, done);

        public static void PostForm(string url, string form, Action<HttpResult> done) => Send("POST", url, form, FormType, null, done);

        public static void PatchJson(string url, string json, string bearer, Action<HttpResult> done) =>
            Send("PATCH", url, json, JsonType, bearer, done);

        public static void Delete(string url, string bearer, Action<HttpResult> done) => Send("DELETE", url, null, null, bearer, done);

        /// <summary>
        /// Sends a request (main thread). <paramref name="done"/> is called exactly once on the main thread: deferred to
        /// the next frame when the device is offline, synchronously in Edit Mode (no runner).
        /// </summary>
        public static void Send(string method, string url, string body, string contentType, string bearer, Action<HttpResult> done)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                HttpResult offline = HttpResult.Offline("not reachable");
                // Deferred so callers never see a re-entrant answer; without a runner (Edit Mode) nothing would ever
                // drain the queue, so answer right away instead of losing the callback.
                if (ServicesRunner.Ensure() != null) ServicesRunner.NextFrame(() => ServicesRunner.SafeInvoke(done, offline));
                else ServicesRunner.SafeInvoke(done, offline);
                return;
            }
            if (ServicesRunner.Run(Routine(method, url, body, contentType, bearer, done)) == null)
                ServicesRunner.SafeInvoke(done, HttpResult.Offline("no runner (edit mode)"));
        }

        static IEnumerator Routine(string method, string url, string body, string contentType, string bearer, Action<HttpResult> done)
        {
            HttpResult result;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = contentType ?? JsonType };
                    request.SetRequestHeader("Content-Type", contentType ?? JsonType);
                }
                if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
                AddPlatformHeaders(request);
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                string text = request.downloadHandler != null ? request.downloadHandler.text : "";
                bool noAnswer = request.result == UnityWebRequest.Result.ConnectionError ||
                                (request.result != UnityWebRequest.Result.Success && request.responseCode == 0);
                result = new HttpResult
                {
                    status = request.responseCode,
                    text = text ?? "",
                    networkError = noAnswer,
                    error = request.error,
                };
            }
            ServicesRunner.SafeInvoke(done, result);
        }

        /// <summary>
        /// Google checks these headers when the API key is restricted to an Android/iOS app. Harmless for unrestricted
        /// keys (the recommended "Web API key" from the Firebase console).
        /// </summary>
        static void AddPlatformHeaders(UnityWebRequest request)
        {
#if UNITY_IOS && !UNITY_EDITOR
            request.SetRequestHeader("X-Ios-Bundle-Identifier", Application.identifier);
#elif UNITY_ANDROID && !UNITY_EDITOR
            request.SetRequestHeader("X-Android-Package", Application.identifier);
            _androidCert ??= NativeSignIn.AndroidSigningCertSha1();
            if (!string.IsNullOrEmpty(_androidCert)) request.SetRequestHeader("X-Android-Cert", _androidCert);
#endif
        }

        /// <summary>application/x-www-form-urlencoded value escaping.</summary>
        public static string FormEscape(string value) => Uri.EscapeDataString(value ?? "");
    }
}
