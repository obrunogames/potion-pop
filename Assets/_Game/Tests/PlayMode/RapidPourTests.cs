using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PotionPop.Game;
using PotionPop.Game.Board;
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PotionPop.Tests
{
    /// <summary>Real session + board jobs and tap path; only the save is replaced with memory storage.</summary>
    public class RapidPourTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        BoardView _view;
        GameSession _session;
        BoardState _board;

        [SetUp]
        public void SetUp()
        {
            SaveSystem.UseMemoryStorage(true);
            SaveSystem.Data.level = 9;
            _root = new GameObject("RapidPourTest", typeof(RectTransform), typeof(Canvas));
            var rect = (RectTransform)_root.transform;
            rect.sizeDelta = new Vector2(1044f, 1500f);
            _view = BoardView.Create(rect);
            _session = new GameSession(new GameViews { board = _view });
            _view.OnPourRequested += _session.HandlePourRequest;
            _view.OnBottleSelected += _session.OnBottleSelected;
            _view.OnBottleCorked += _session.OnBottleCorked;
            _view.OnIdle += _session.OnViewIdle;
        }

        [TearDown]
        public void TearDown()
        {
            _session?.Abort();
            if (_root != null) Object.DestroyImmediate(_root);
            Tween.KillAll();
            SaveSystem.UseMemoryStorage(false);
        }

        void Build(params int[][] bottles)
        {
            var def = new LevelDefinition { number = 9, capacity = 4, areaId = "forest", movesFor3Stars = 40 };
            foreach (var units in bottles) def.bottles.Add(new BottleDef(units));
            Build(def);
        }

        void Build(LevelDefinition def)
        {
            _board = new BoardState(def);
            Set(_session, "Board", _board);
            Set(_session, "Definition", def);
            Set(_session, "Level", def.number);
            Set(_session, "State", SessionState.Playing);
            _view.Build(_board, "forest");
            _view.RefreshAll();
            _view.InputEnabled = true;
        }

        static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });

        static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        int Active => ((IList)Field(_view, "_active")).Count;
        int Queued => ((IList)Field(_view, "_queue")).Count;
        void Tap(int i) => Call(_view, "HandleBottleTap", i);
        void Pour(int from, int to) { Tap(from); Tap(to); }

        void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                // Keep the board's effect clock and job clock on the same exact timeline.
                const float dt = 1f / 60f;
                SetField(_view, "_clock", (float)Field(_view, "_clock") + dt);
                SetField(_view, "_frameDt", dt);
                Call(_view, "TickJobs", dt);
                Call(_view, "LiftSelectedWhenReady");
                Call(_view, "UpdatePlacement", dt);
                Tween.ManualUpdate(dt);
                Call(_view, "LateUpdate");
            }
        }

        void Settle()
        {
            for (int i = 0; i < 3000 && _view.IsAnimating; i++) Step();
            Assert.IsFalse(_view.IsAnimating, "the per-bottle queue must drain");
            AssertVisuals();
        }

        void AssertVisuals()
        {
            var views = (IList)Field(_view, "_views");
            for (int i = 0; i < _board.Count; i++)
            {
                var shown = views[i].GetType().GetField("shown").GetValue(views[i]);
                Assert.IsTrue((bool)shown.GetType().GetMethod("SameLiquid").Invoke(shown, new object[] { _board[i] }), "bottle " + i);
            }
        }

        [Test]
        public void IndependentPours_StartInTheSameFrame()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(1, 3);
            Assert.AreEqual(2, _board.Moves);
            Assert.AreEqual(2, Active);
            Assert.AreEqual(0, Queued);
            Assert.IsTrue(_view.InputEnabled);
            Assert.IsFalse(_session.IsBusy);
            Settle();
        }

        [Test]
        public void RapidPours_ReuseBusyBottlesInOrderWithoutLosingLiquid()
        {
            Build(new[] { 0, 1 }, new[] { 1 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(1, 2); // Same receiving bottle: animation must wait, input must be remembered.
            Pour(0, 3); // Same source, using the new top color.
            Assert.AreEqual(3, _board.Moves);
            Assert.AreEqual(1, Active);
            Assert.AreEqual(2, Queued);
            CollectionAssert.AreEqual(new[] { 1, 1 }, _board[2].Units);
            CollectionAssert.AreEqual(new[] { 0 }, _board[3].Units);
            Assert.AreEqual(3, _board.Bottles.Sum(b => b.Count));
            Settle();
        }

        [Test]
        public void IndependentPour_OvertakesAConflictingQueuedPour()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(0, 4);
            Pour(1, 3);
            Assert.AreEqual(3, _board.Moves);
            Assert.AreEqual(2, Active);
            Assert.AreEqual(1, Queued);
            Settle();
        }

        [Test]
        public void BusySelection_DeselectsAndLiftsOnlyAfterTheBottleIsFree()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Tap(0);
            Assert.AreEqual(0, _view.Selected);
            Tap(0);
            Assert.AreEqual(-1, _view.Selected);
            Tap(0);
            Settle();
            Step();
            Assert.AreEqual(0, _view.Selected);
            var views = (IList)Field(_view, "_views");
            Assert.IsTrue((bool)views[0].GetType().GetField("lifted").GetValue(views[0]));
            Assert.AreEqual(1, _board.Moves);
        }

        [Test]
        public void InvalidPourDuringAnimation_DoesNotQueueOrConsumeAMove()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(1, 2); // Wrong color against the already-committed state.
            Assert.AreEqual(1, _board.Moves);
            Assert.AreEqual(0, Queued);
            Assert.AreEqual(-1, _view.Selected);
            var views = (IList)Field(_view, "_views");
            Assert.IsFalse((bool)Field(views[1], "_glowSelected"), "invalid pour clears the selection glow even while busy");
            Pour(1, 3);
            Assert.AreEqual(2, Active);
            Settle();
        }

        [Test]
        public void FullOrCompletedBusyTarget_DoesNotAcceptMoreLiquid()
        {
            Build(new[] { 0 }, new[] { 0, 0, 0 }, new[] { 0 }, new[] { 1, 2 }, Array.Empty<int>());
            Pour(0, 1);
            Pour(2, 1);
            Assert.AreEqual(1, _board.Moves);
            Assert.AreEqual(4, _board[1].Count);
            Assert.AreEqual(1, _board[2].Count);
            Settle();
        }

        [Test]
        public void ReentrantSessionRequest_DoesNotCommitTwice()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            bool called = false;
            Action reenter = () =>
            {
                if (called) return;
                called = true;
                _session.HandlePourRequest(0, 3);
            };
            SaveSystem.OnChanged += reenter;
            try { Pour(0, 2); }
            finally { SaveSystem.OnChanged -= reenter; }
            Assert.IsTrue(called);
            Assert.AreEqual(1, _board.Moves);
            Assert.AreEqual(1, SaveSystem.Data.totalPours);
            Settle();
        }

        [Test]
        public void UndoDuringPours_WaitsWithoutConsumingInventoryThenUndoesTheLastMove()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Economy.AddBooster(BoosterType.Undo, 2);
            int owned = Economy.GetBooster(BoosterType.Undo);
            Pour(0, 2);
            Pour(1, 3);
            _session.OnBoosterTapped(BoosterType.Undo);
            Assert.AreEqual(2, _board.Moves);
            Assert.AreEqual(owned, Economy.GetBooster(BoosterType.Undo));
            Settle();
            _session.OnBoosterTapped(BoosterType.Undo);
            Assert.AreEqual(1, _board.Moves);
            Assert.AreEqual(owned - 1, Economy.GetBooster(BoosterType.Undo));
            Settle();
        }

        [Test]
        public void ClearDuringQueuedPours_DoesNotApplyOldSnapshotsToTheNextBoard()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(0, 3);
            Assert.AreEqual(1, Queued);
            _session.Abort();
            Build(new[] { 4, 5 }, new[] { 5, 4 }, Array.Empty<int>());
            Step(200);
            Assert.AreEqual(0, _board.Moves);
            Assert.AreEqual(SessionState.Playing, _session.State);
            CollectionAssert.AreEqual(new[] { 4, 5 }, _board[0].Units);
            AssertVisuals();
        }

        [Test]
        public void WinWhileMultiplePoursAnimate_LocksNewMovesAndWaitsForTheQueue()
        {
            Build(new[] { 0 }, new[] { 0, 0, 0 }, new[] { 1 }, new[] { 1, 1, 1 });
            Pour(0, 1);
            Pour(2, 3);
            Assert.AreEqual(SessionState.Won, _session.State);
            Assert.AreEqual(2, Active);
            Assert.IsFalse(_view.InputEnabled);
            _session.HandlePourRequest(0, 2);
            Assert.AreEqual(2, _board.Moves);
            Call(_session, "TickEnded", 0.01f, Time.unscaledTime + 1f);
            Assert.IsFalse((bool)Field(_session, "_celebrating"));
            Settle();
        }

        [Test]
        public void LongWinQueue_ProgressKeepsCelebrationFromStartingEarly()
        {
            Build(new[] { 0 }, new[] { 0, 0, 0 }, Array.Empty<int>(), Array.Empty<int>());
            for (int i = 0; i < 8; i++) Pour(i % 2 == 0 ? 0 : 2, i % 2 == 0 ? 2 : 0);
            Pour(0, 1);
            Assert.AreEqual(SessionState.Won, _session.State);
            Assert.Greater(Queued, 0);
            // Overall time since win exceeds the old safety limit; an animation just progressed.
            SetField(_session, "_wonAt", Time.unscaledTime - 10f);
            SetField(_session, "_winSettleAt", Time.unscaledTime - 9f);
            Step();
            Call(_session, "TickEnded", 0.01f, Time.unscaledTime);
            Assert.IsFalse((bool)Field(_session, "_celebrating"), "legitimate queued animations must finish first");
            Settle();
        }

        [Test]
        public void ReentrantTapDuringRequest_DoesNotRecordAnotherMove()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            bool called = false;
            _view.OnPourRequested += (from, to) =>
            {
                if (called) return;
                called = true;
                Pour(0, 3);
            };
            Pour(0, 2);
            Assert.IsTrue(called);
            Assert.AreEqual(1, _board.Moves);
            Settle();
        }

        [Test]
        public void LongQueue_DoesNotTriggerTheIdleCheckWhileJobsAreProgressing()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(0, 3);
            SetField(_session, "_checkScheduledAt", Time.unscaledTime - 10f);
            SetField(_session, "_checkAt", Time.unscaledTime - 1f);
            Step();
            Call(_session, "TickChecks", Time.unscaledTime);
            Assert.IsTrue((bool)Field(_session, "_checkPending"));
            Settle();
        }

        [Test]
        public void StalledView_ReconcilesEveryQueuedSnapshotBeforeCheckingTheResult()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Pour(0, 3);
            Set(_view, "LastAnimationProgressTime", Time.unscaledTime - 10f);
            bool settled = (bool)Call(_session, "ViewSettled", Time.unscaledTime, Time.unscaledTime - 10f, 6f);
            Assert.IsTrue(settled);
            Assert.IsFalse(_view.IsAnimating);
            Assert.AreEqual(2, _board.Moves);
            AssertVisuals();
        }

        [Test]
        public void StoneUnlockDuringConcurrentPours_WaitsForShatterAndPreservesTheLatestCounter()
        {
            var def = new LevelDefinition { number = 25, areaId = "forest", capacity = 4 };
            def.bottles.Add(new BottleDef(new[] { 0 }));
            def.bottles.Add(new BottleDef(new[] { 0, 0, 0 }));
            def.bottles.Add(new BottleDef(new[] { 1 }) { lockCount = 1 });
            def.bottles.Add(new BottleDef(new[] { 1, 1, 1 }));
            def.bottles.Add(new BottleDef(Array.Empty<int>()));
            Build(def);
            Pour(0, 1);
            Assert.IsFalse(_board[2].IsLocked);
            Assert.IsTrue(_view.IsBusy(2));
            Tap(2);
            Assert.AreEqual(-1, _view.Selected, "visually shattering stone still blocks that bottle");
            Pour(3, 4);
            Assert.AreEqual(2, Active);
            Settle();
            Pour(2, 4);
            Assert.AreEqual(SessionState.Won, _session.State);
            Settle();
            var views = (IList)Field(_view, "_views");
            Assert.AreEqual(0, views[2].GetType().GetField("lockShown").GetValue(views[2]));
        }

        [Test]
        public void InputDisabledDuringIntroOrPopup_DoesNotAcceptRapidTaps()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            _view.InputEnabled = false;
            Pour(0, 2);
            Pour(1, 3);
            Assert.AreEqual(0, _board.Moves);
            Assert.AreEqual(-1, _view.Selected);
        }

        [Test]
        public void BackgroundTap_CancelsBusySelectionWithoutChangingItsAnimation()
        {
            Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>());
            Pour(0, 2);
            Tap(0);
            Call(_view, "HandleBackgroundTap");
            Assert.AreEqual(-1, _view.Selected);
            Assert.AreEqual(1, Active);
            Assert.AreEqual(1, _board.Moves);
            Settle();
        }

        [UnityTest]
        public IEnumerator VisualQA_RapidPointerPours_RenderInParallelAndSettle()
        {
            // Opt-in capture through the project's own editor capture tool; ordinary CI needs no graphics/files.
            string directory = Environment.GetEnvironmentVariable("POTIONPOP_QA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) yield break;
            var capture = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PotionPop.EditorTools.CaptureTool")).First(t => t != null);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2340f);
            // The capture temporarily changes the camera viewport. Keep this isolated board's layout fixed.
            var boardRect = (RectTransform)_view.transform;
            boardRect.anchorMin = boardRect.anchorMax = new Vector2(0.5f, 0.5f);
            boardRect.sizeDelta = new Vector2(1044f, 1500f);
            boardRect.anchoredPosition = Vector2.zero;
            var background = UIKit.Backdrop((RectTransform)_root.transform, "gamebg_forest");
            background.transform.SetAsFirstSibling();
            var events = new GameObject("TestEventSystem", typeof(EventSystem));
            try
            {
                Build(new[] { 0, 1 }, new[] { 2, 3 }, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
                yield return null;
                Capture(capture, directory, "01-before");
                PointerTap(events.GetComponent<EventSystem>(), 0);
                PointerTap(events.GetComponent<EventSystem>(), 2);
                PointerTap(events.GetComponent<EventSystem>(), 1);
                PointerTap(events.GetComponent<EventSystem>(), 3);
                PointerTap(events.GetComponent<EventSystem>(), 0);
                PointerTap(events.GetComponent<EventSystem>(), 4);
                Assert.AreEqual(3, _board.Moves);
                Assert.AreEqual(2, Active);
                Assert.AreEqual(1, Queued);
                Step(20);
                yield return null;
                Capture(capture, directory, "02-parallel-and-queued");
                Settle();
                yield return null;
                Capture(capture, directory, "03-settled");
            }
            finally { Object.DestroyImmediate(events); }
        }

        void PointerTap(EventSystem events, int i)
        {
            var hits = (RectTransform)Field(_view, "_hits");
            var hit = (RectTransform)hits.GetChild(i);
            Canvas.ForceUpdateCanvases();
            var data = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, hit.TransformPoint(hit.rect.center)),
            };
            var results = new List<RaycastResult>();
            events.RaycastAll(data, results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(hit.gameObject, results[0].gameObject, "pointer must actually hit the requested bottle");
            ExecuteEvents.Execute(results[0].gameObject, data, ExecuteEvents.pointerDownHandler);
        }

        static void Capture(Type capture, string directory, string name)
        {
            string path = Path.Combine(directory, name + ".png");
            Assert.AreEqual(path, capture.GetMethod("Capture").Invoke(null, new object[] { path, 1080, 2340 }));
            Assert.IsTrue(File.Exists(path));
        }
    }
}
