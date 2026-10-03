using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>Outcome of a cloud document operation.</summary>
    public struct CloudResult
    {
        public bool ok;
        /// <summary>GET: the document does not exist (not an error).</summary>
        public bool notFound;
        public long status;
        /// <summary>Loc key when the call failed (cloud.error.*).</summary>
        public string errorKey;
        public CloudPlayerDoc doc;

        public static CloudResult Fail(string key, long status = 0) => new CloudResult { ok = false, errorKey = key, status = status };
    }

    /// <summary>
    /// Firestore REST v1 calls on players/{uid}, the leaderboard and name reports (Bearer = Firebase ID token from
    /// <see cref="AuthService.GetIdToken"/>). All callbacks arrive on the main thread.
    /// </summary>
    public static class FirestoreClient
    {
        public const string ErrNetwork = "cloud.error.network";
        public const string ErrAuth = "cloud.error.auth";
        public const string ErrDenied = "cloud.error.denied";
        public const string ErrFailed = "cloud.error.failed";

        static string ProjectId => ServicesConfig.Load().firebaseProjectId;

        /// <summary>Loc key for a failed Firestore call; a 401 also invalidates the cached ID token.</summary>
        public static string ErrorKey(HttpResult r)
        {
            if (r.networkError || r.status == 0 || r.status == 408 || r.status >= 500) return ErrNetwork;
            if (r.status == 401)
            {
                AuthService.InvalidateIdToken();
                return ErrAuth;
            }
            if (r.status == 403) return ErrDenied;
            return ErrFailed;
        }

        static void WithToken(Action<CloudResult> fail, Action<string> next)
        {
            AuthService.GetIdToken(token =>
            {
                if (string.IsNullOrEmpty(token)) ServicesRunner.SafeInvoke(fail, CloudResult.Fail(AuthService.IsSignedIn ? ErrNetwork : ErrAuth));
                else next(token);
            });
        }

        public static void GetPlayer(string uid, Action<CloudResult> done)
        {
            WithToken(done, token => Http.Get(FirestoreCodec.PlayerDocUrl(ProjectId, uid), token, r =>
            {
                if (r.status == 404) { ServicesRunner.SafeInvoke(done, new CloudResult { ok = false, notFound = true, status = 404 }); return; }
                if (!r.Ok)
                {
                    Debug.LogWarning("[CloudSave] GET failed: " + r);
                    ServicesRunner.SafeInvoke(done, CloudResult.Fail(ErrorKey(r), r.status));
                    return;
                }
                if (!FirestoreCodec.TryDecodePlayerDoc(r.text, out CloudPlayerDoc doc))
                {
                    Debug.LogWarning("[CloudSave] unreadable cloud document: " + r);
                    doc = null;
                }
                ServicesRunner.SafeInvoke(done, new CloudResult { ok = true, status = r.status, doc = doc });
            }));
        }

        /// <summary>
        /// Creates/replaces players/{uid} with <paramref name="docJson"/> (FirestoreCodec.EncodePlayerDoc), then — when
        /// <paramref name="leaderboardJson"/> is given — the public ranking row leaderboard/{uid}. The ranking write is
        /// best effort: its failure is logged and never fails the save sync.
        /// </summary>
        public static void PutPlayer(string uid, string docJson, Action<CloudResult> done, string leaderboardJson = null)
        {
            WithToken(done, token => Http.PatchJson(FirestoreCodec.PlayerDocUrl(ProjectId, uid), docJson, token, r =>
            {
                if (!r.Ok)
                {
                    Debug.LogWarning("[CloudSave] PATCH failed: " + r);
                    ServicesRunner.SafeInvoke(done, CloudResult.Fail(ErrorKey(r), r.status));
                    return;
                }
                var result = new CloudResult { ok = true, status = r.status };
                if (string.IsNullOrEmpty(leaderboardJson)) { ServicesRunner.SafeInvoke(done, result); return; }
                Http.PatchJson(FirestoreCodec.LeaderboardDocUrl(ProjectId, uid), leaderboardJson, token, lr =>
                {
                    if (!lr.Ok) Debug.LogWarning("[Leaderboard] ranking row PATCH failed: " + lr);
                    ServicesRunner.SafeInvoke(done, result);
                });
            }));
        }

        /// <summary>Deletes leaderboard/{uid} and players/{uid} (missing documents count as success).</summary>
        public static void DeletePlayer(string uid, Action<CloudResult> done)
        {
            WithToken(done, token => Http.Delete(FirestoreCodec.LeaderboardDocUrl(ProjectId, uid), token, lr =>
            {
                if (!(lr.Ok || lr.status == 404)) Debug.LogWarning("[Leaderboard] ranking row DELETE failed: " + lr);
                Http.Delete(FirestoreCodec.PlayerDocUrl(ProjectId, uid), token, r =>
                {
                    bool ok = r.Ok || r.status == 404;
                    if (!ok) Debug.LogWarning("[CloudSave] DELETE failed: " + r);
                    ServicesRunner.SafeInvoke(done, ok ? new CloudResult { ok = true, status = r.status } : CloudResult.Fail(ErrorKey(r), r.status));
                });
            }));
        }

        /// <summary>Top players by stars. done(ok, docs in rank order).</summary>
        public static void TopPlayers(int limit, Action<bool, List<CloudPlayerDoc>> done)
        {
            WithToken(_ => ServicesRunner.SafeInvoke(done, false, (List<CloudPlayerDoc>)null), token =>
                Http.PostJson(FirestoreCodec.RunQueryUrl(ProjectId), FirestoreCodec.BuildTopPlayersQuery(limit), token, r =>
                {
                    if (!r.Ok)
                    {
                        Debug.LogWarning("[Leaderboard] runQuery failed: " + r);
                        ErrorKey(r);
                        ServicesRunner.SafeInvoke(done, false, (List<CloudPlayerDoc>)null);
                        return;
                    }
                    ServicesRunner.SafeInvoke(done, true, FirestoreCodec.DecodeQueryResponse(r.text));
                }));
        }

        /// <summary>
        /// Files a name report: POST on the reports collection (Firestore picks the id) = { reporter, target, name,
        /// createdAt }. Create-only for the signed-in reporter (firebase/firestore.rules). done(ok).
        /// </summary>
        public static void CreateReport(string reporterUid, string targetUid, string name, Action<bool> done)
        {
            WithToken(_ => ServicesRunner.SafeInvoke(done, false), token =>
                Http.PostJson(FirestoreCodec.ReportsUrl(ProjectId), FirestoreCodec.EncodeReportDoc(reporterUid, targetUid, name, TimeUtil.Now),
                    token, r =>
                    {
                        if (!r.Ok)
                        {
                            Debug.LogWarning("[Leaderboard] report POST failed: " + r);
                            ErrorKey(r);
                        }
                        ServicesRunner.SafeInvoke(done, r.Ok);
                    }));
        }

        /// <summary>Number of players with more stars than <paramref name="stars"/>; -1 on failure.</summary>
        public static void CountAbove(long stars, Action<long> done)
        {
            WithToken(_ => ServicesRunner.SafeInvoke(done, -1L), token =>
                Http.PostJson(FirestoreCodec.RunAggregationQueryUrl(ProjectId), FirestoreCodec.BuildCountAboveQuery(stars), token, r =>
                {
                    if (!r.Ok) Debug.LogWarning("[Leaderboard] count query failed: " + r);
                    ServicesRunner.SafeInvoke(done, r.Ok ? FirestoreCodec.DecodeCountResponse(r.text) : -1L);
                }));
        }
    }
}
