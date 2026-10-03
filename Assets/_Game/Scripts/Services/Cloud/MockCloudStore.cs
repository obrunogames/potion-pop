using System;
using System.IO;
using UnityEngine;

namespace PotionPop.Services
{
    /// <summary>
    /// Editor stand-in for Firestore: keeps the mock user's players/{uid} document in
    /// Application.persistentDataPath/mock_cloud.json (same JSON encoding as Firestore, so the codec is exercised) with
    /// a small simulated latency. Delete the file to "wipe the cloud".
    /// </summary>
    public static class MockCloudStore
    {
        public const string FileName = "mock_cloud.json";
        const float LatencySeconds = 0.5f;

        [Serializable]
        sealed class MockFile
        {
            public string uid;
            public string doc;   // Firestore document JSON
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void GetPlayer(string uid, Action<CloudResult> done)
        {
            Later(() =>
            {
                MockFile file = Read();
                if (file == null || file.uid != uid || string.IsNullOrEmpty(file.doc))
                    return new CloudResult { ok = false, notFound = true, status = 404 };
                if (!FirestoreCodec.TryDecodePlayerDoc(file.doc, out CloudPlayerDoc doc)) doc = null;
                if (doc != null) doc.uid = uid;
                return new CloudResult { ok = true, status = 200, doc = doc };
            }, done);
        }

        public static void PutPlayer(string uid, string docJson, Action<CloudResult> done)
        {
            Later(() =>
            {
                try
                {
                    File.WriteAllText(FilePath, JsonUtility.ToJson(new MockFile { uid = uid, doc = docJson }));
                    return new CloudResult { ok = true, status = 200 };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CloudSave] mock cloud write failed: " + e.Message);
                    return CloudResult.Fail(FirestoreClient.ErrFailed);
                }
            }, done);
        }

        public static void DeletePlayer(string uid, Action<CloudResult> done)
        {
            Later(() =>
            {
                MockFile file = Read();
                try
                {
                    if (file != null && file.uid == uid) File.Delete(FilePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CloudSave] mock cloud delete failed: " + e.Message);
                }
                return new CloudResult { ok = true, status = 200 };
            }, done);
        }

        /// <summary>The stored document (any uid), or null — for debug tools.</summary>
        public static CloudPlayerDoc Peek()
        {
            MockFile file = Read();
            return file != null && FirestoreCodec.TryDecodePlayerDoc(file.doc, out CloudPlayerDoc doc) ? doc : null;
        }

        static MockFile Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                return JsonUtility.FromJson<MockFile>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CloudSave] mock cloud unreadable: " + e.Message);
                return null;
            }
        }

        static void Later(Func<CloudResult> work, Action<CloudResult> done)
        {
            if (ServicesRunner.Delay(LatencySeconds, () => ServicesRunner.SafeInvoke(done, work())) == null)
                ServicesRunner.SafeInvoke(done, work());
        }
    }
}
