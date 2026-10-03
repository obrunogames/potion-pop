// EditMode tests for the Levels module (board rules, boosters, solver, generator, difficulty, stars) and the pure parts
// of the game flow (wand / rainbow color pick).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using PotionPop.Game;
using PotionPop.Levels;

namespace PotionPop.Tests
{
    public class LevelTests
    {
        // ============================================================================================ builders

        static BottleDef B(params int[] units) => new BottleDef(units);

        /// <summary>Bottle whose bottom `hidden` units are hidden ("?").</summary>
        static BottleDef H(int hidden, params int[] units) => new BottleDef(units, hidden);

        /// <summary>Stone-wrapped bottle (opens after `count` completions).</summary>
        static BottleDef Stone(int count, params int[] units) => new BottleDef(units, 0, count);

        static LevelDefinition Def(params BottleDef[] bottles)
        {
            var d = new LevelDefinition { number = 99, seed = 1, areaId = "forest", capacity = 4 };
            d.bottles.AddRange(bottles);
            d.colorCount = d.ColorsUsed().Length;
            return d;
        }

        static BoardState Board(params BottleDef[] bottles) => new BoardState(Def(bottles));

        static string Snap(BoardState b) => b + "| moves=" + b.Moves + " ticks=" + b.Ticks;

        static int[] Units(BoardState b, int i) => b[i].Units.ToArray();

        /// <summary>color → units on the whole board.</summary>
        static Dictionary<int, int> ColorCounts(BoardState b)
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i < b.Count; i++)
                foreach (int u in b[i].Units) d[u] = d.TryGetValue(u, out int v) ? v + 1 : 1;
            return d;
        }

        static bool Replays(BoardState board, IList<Move> moves)
        {
            foreach (var m in moves)
                if (board.Pour(m.from, m.to) == null) return false;
            return board.IsWon;
        }

        // ============================================================================================ pours

        [Test]
        public void Pour_MovesTheWholeTopRunOntoTheSameColor()
        {
            var b = Board(B(0, 1, 1), B(1), B());
            Assert.AreEqual(PourRefusal.None, b.Check(0, 1));
            Assert.AreEqual(2, b.PourAmount(0, 1));
            var r = b.Pour(0, 1);
            Assert.IsNotNull(r);
            Assert.AreEqual(1, r.color);
            Assert.AreEqual(2, r.amount);
            Assert.AreEqual(3, r.fromCountBefore);
            Assert.AreEqual(1, r.toCountBefore);
            CollectionAssert.AreEqual(new[] { 0 }, Units(b, 0));
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, Units(b, 1));
            Assert.IsFalse(r.completed);
            Assert.AreEqual(1, b.Moves);
        }

        [Test]
        public void Pour_IntoAnEmptyBottle()
        {
            var b = Board(B(2, 3, 3), B());
            var r = b.Pour(0, 1);
            Assert.AreEqual(2, r.amount);
            CollectionAssert.AreEqual(new[] { 3, 3 }, Units(b, 1));
            CollectionAssert.AreEqual(new[] { 2 }, Units(b, 0));
        }

        [Test]
        public void Pour_IsPartialWhenTheTargetHasLittleRoom()
        {
            var b = Board(B(2, 1, 1, 1), B(0, 0, 1));
            Assert.AreEqual(1, b.PourAmount(0, 1));
            var r = b.Pour(0, 1);
            Assert.AreEqual(1, r.amount);
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, Units(b, 0));
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, Units(b, 1));
        }

        [Test]
        public void Pour_IllegalPoursChangeNothing()
        {
            var b = Board(B(0, 1), B(2, 2, 2, 3), B(1, 2), B(), B(4, 4, 4, 4), Stone(2, 5, 1));
            string before = Snap(b);
            Assert.AreEqual(PourRefusal.WrongColor, b.Check(0, 2));
            Assert.AreEqual(PourRefusal.TargetFull, b.Check(0, 1));
            Assert.AreEqual(PourRefusal.SameBottle, b.Check(0, 0));
            Assert.AreEqual(PourRefusal.SourceEmpty, b.Check(3, 0));
            Assert.AreEqual(PourRefusal.TargetDone, b.Check(0, 4));
            Assert.AreEqual(PourRefusal.SourceDone, b.Check(4, 3));
            Assert.AreEqual(PourRefusal.TargetLocked, b.Check(0, 5));
            Assert.AreEqual(PourRefusal.SourceLocked, b.Check(5, 3));
            Assert.AreEqual(PourRefusal.Invalid, b.Check(0, 9));
            Assert.AreEqual(PourRefusal.Invalid, b.Check(-1, 0));
            foreach (var m in new[] { new Move(0, 2), new Move(0, 1), new Move(0, 0), new Move(3, 0), new Move(0, 4), new Move(4, 3), new Move(0, 5), new Move(5, 3), new Move(0, 9) })
            {
                Assert.AreEqual(0, b.PourAmount(m.from, m.to), m.ToString());
                Assert.IsNull(b.Pour(m.from, m.to), m.ToString());
            }
            Assert.AreEqual(before, Snap(b));
            Assert.IsFalse(b.CanUndo);
        }

        [Test]
        public void Pour_CompletedBottleIsCorkedAndInert()
        {
            var b = Board(B(0, 0, 0), B(1, 0), B());
            var r = b.Pour(1, 0);
            Assert.IsTrue(r.completed);
            Assert.IsTrue(b[0].Completed);
            Assert.IsFalse(b.CanSelect(0));
            Assert.AreEqual(PourRefusal.SourceDone, b.Check(0, 2));
            Assert.AreEqual(PourRefusal.TargetDone, b.Check(1, 0));
            Assert.AreEqual(1, b.Ticks, "a completion counts for the stones");
        }

        [Test]
        public void Pour_HiddenUnitsOfThePouredColorPourThroughAndReveal()
        {
            // [?2, ?1, 1, 1]: the hidden 1 right under the visible run pours out too ("liquid is liquid").
            var b = Board(H(2, 2, 1, 1, 1), B());
            Assert.AreEqual(3, b[0].TopRun);
            Assert.AreEqual(2, b[0].VisibleTopRun);
            var r = b.Pour(0, 1);
            Assert.AreEqual(3, r.amount);
            Assert.AreEqual(1, r.hiddenPoured);
            CollectionAssert.AreEqual(new[] { 2 }, Units(b, 0));
            Assert.AreEqual(0, b[0].Hidden, "the new top is visible");
            Assert.AreEqual(1, r.reveals.Count);
            Assert.AreEqual(0, r.reveals[0].bottle);
            Assert.AreEqual(0, r.reveals[0].index);
            Assert.AreEqual(2, r.reveals[0].color);
        }

        [Test]
        public void Pour_RevealsTheNewTopWhenTheVisiblePartEmpties()
        {
            var b = Board(H(2, 0, 2, 1), B());
            var r = b.Pour(0, 1);
            Assert.AreEqual(1, r.amount);
            Assert.AreEqual(0, r.hiddenPoured);
            Assert.AreEqual(1, b[0].Hidden, "only the bottom unit stays hidden");
            Assert.AreEqual(1, r.reveals.Count);
            Assert.AreEqual(new Reveal(0, 1, 2).color, r.reveals[0].color);
            Assert.AreEqual(1, r.reveals[0].index);
            Assert.IsTrue(b[0].IsHidden(0));
            Assert.IsFalse(b[0].IsHidden(1));
        }

        [Test]
        public void Pour_CompletingABottleRevealsItsHiddenUnits()
        {
            var b = Board(H(1, 1, 1, 1), B(1));
            var r = b.Pour(1, 0);
            Assert.IsTrue(r.completed);
            Assert.AreEqual(0, b[0].Hidden);
            Assert.IsTrue(r.reveals.Any(x => x.bottle == 0 && x.index == 0 && x.color == 1));
        }

        [Test]
        public void Pour_ReportsTheWin()
        {
            var b = Board(B(0, 0, 0), B(0), B(1, 1, 1, 1));
            Assert.IsFalse(b.IsWon);
            var r = b.Pour(1, 0);
            Assert.IsTrue(r.won);
            Assert.IsTrue(b.IsWon);
        }

        // ============================================================================================ stones

        /// <summary>Two completions (colors 0 and 1) crack the stone around bottle 4; color 2 then completes through it.</summary>
        static BoardState StoneBoard() => Board(B(0, 0, 0), B(0), B(1, 1, 1), B(1), Stone(2, 2, 2, 2), B(2));

        [Test]
        public void Stones_BlockPouringUntilBroken()
        {
            var b = StoneBoard();
            Assert.IsTrue(b[4].IsLocked);
            Assert.IsTrue(b.AnyLocked);
            Assert.IsFalse(b.CanSelect(4));
            Assert.AreEqual(PourRefusal.SourceLocked, b.Check(4, 5));
            Assert.AreEqual(PourRefusal.TargetLocked, b.Check(1, 4));
        }

        [Test]
        public void Stones_TickOnEveryCompletionAndBreakAtZero()
        {
            var b = StoneBoard();
            var r1 = b.Pour(1, 0);
            Assert.IsTrue(r1.completed);
            Assert.AreEqual(1, r1.lockTicks.Count);
            Assert.AreEqual(4, r1.lockTicks[0].bottle);
            Assert.AreEqual(1, r1.lockTicks[0].remaining);
            Assert.IsTrue(b[4].IsLocked);
            var r2 = b.Pour(3, 2);
            Assert.AreEqual(0, r2.lockTicks[0].remaining);
            Assert.IsFalse(b[4].IsLocked, "the stone broke");
            Assert.IsTrue(b.CanSelect(4));
            var r3 = b.Pour(5, 4);
            Assert.IsNotNull(r3);
            Assert.IsTrue(r3.completed);
            Assert.AreEqual(0, r3.lockTicks.Count, "no stone left to tick");
            Assert.IsTrue(b.IsWon);
        }

        [Test]
        public void Stones_RewardedAdBreaksOneAndAnUndoNeverPutsItBack()
        {
            var b = StoneBoard();
            Assert.IsNotNull(b.Pour(1, 0));   // ticks the stone to 1
            var tick = b.BreakLock(4);
            Assert.IsTrue(tick.HasValue);
            Assert.AreEqual(0, tick.Value.remaining);
            Assert.IsFalse(b[4].IsLocked);
            Assert.IsNull(b.BreakLock(4), "already broken");
            Assert.IsNull(b.BreakLock(0), "not a stone");
            Assert.IsNull(b.BreakLock(42), "out of range");
            var u = b.Undo();
            Assert.IsTrue(u.uncorked);
            Assert.AreEqual(0, u.lockTicks.Count);
            Assert.IsFalse(b[4].IsLocked);
        }

        // ============================================================================================ undo

        [Test]
        public void Undo_RestoresTheBoardAndTheMoveCount()
        {
            var b = Board(B(0, 1, 1), B(2, 1), B());
            string before = Snap(b);
            Assert.IsFalse(b.CanUndo);
            Assert.IsNull(b.Undo());
            b.Pour(0, 1);
            b.Pour(0, 2);
            Assert.AreEqual(2, b.UndoDepth);
            var u2 = b.Undo();
            Assert.AreEqual(0, u2.from);
            Assert.AreEqual(2, u2.to);
            Assert.AreEqual(0, u2.color);
            Assert.AreEqual(1, u2.amount);
            Assert.IsFalse(u2.uncorked);
            b.Undo();
            Assert.AreEqual(before, Snap(b));
            Assert.AreEqual(0, b.Moves);
            Assert.IsFalse(b.CanUndo);
        }

        [Test]
        public void Undo_UncorksAndRestoresStoneCounters()
        {
            var b = StoneBoard();
            b.Pour(1, 0);
            b.Pour(3, 2);
            Assert.IsFalse(b[4].IsLocked);
            var u = b.Undo();
            Assert.IsTrue(u.uncorked);
            Assert.IsFalse(b[2].Completed);
            Assert.AreEqual(1, u.lockTicks.Count);
            Assert.AreEqual(4, u.lockTicks[0].bottle);
            Assert.AreEqual(1, u.lockTicks[0].remaining);
            Assert.IsTrue(b[4].IsLocked);
            Assert.AreEqual(1, b[4].LockRemaining);
            Assert.AreEqual(1, b.Ticks);
            b.Undo();
            Assert.AreEqual(2, b[4].LockRemaining);
            Assert.AreEqual(0, b.Ticks);
            Assert.AreEqual(0, b.Moves);
        }

        [Test]
        public void Undo_KeepsRevealedColorsRevealed()
        {
            var b = Board(H(2, 0, 2, 1), B());
            b.Pour(0, 1);
            Assert.AreEqual(1, b[0].Hidden);
            b.Undo();
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, Units(b, 0));
            Assert.AreEqual(1, b[0].Hidden, "the player already saw that color");
        }

        // ============================================================================================ boosters

        [Test]
        public void AddBottle_AppendsAnEmptyExtraBottle()
        {
            var b = Board(B(0, 1, 0, 1), B(1, 0, 1, 0));
            Assert.IsFalse(b.HasAnyMove);
            int i = b.AddBottle();
            Assert.AreEqual(2, i);
            Assert.AreEqual(3, b.Count);
            Assert.IsTrue(b[i].IsEmpty);
            Assert.IsTrue(b[i].Extra);
            Assert.AreEqual(1, b.ExtraBottles);
            Assert.AreEqual(4, b[i].Capacity);
            Assert.IsTrue(b.HasUsefulMove);
            b.AddBottle();
            Assert.AreEqual(2, b.ExtraBottles);
        }

        [Test]
        public void RemoveColor_RemovesEveryUnitTicksStonesAndClearsUndo()
        {
            var b = Board(B(2, 0, 1), B(0, 2, 1), B(1, 2), Stone(3, 2, 1), B(3, 3, 3, 3), B());
            b.Pour(0, 5);   // something to undo
            Assert.IsTrue(b.CanUndo);
            var r = b.RemoveColor(2);
            Assert.IsNotNull(r);
            Assert.AreEqual(2, r.color);
            Assert.AreEqual(4, r.removed.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, r.changed);
            CollectionAssert.AreEqual(new[] { 0 }, Units(b, 0));
            CollectionAssert.AreEqual(new[] { 0, 1 }, Units(b, 1));
            CollectionAssert.AreEqual(new[] { 1 }, Units(b, 2));
            CollectionAssert.AreEqual(new[] { 1 }, Units(b, 3));
            Assert.AreEqual(1, r.lockTicks.Count, "a removed color ticks the stones like a completion");
            Assert.AreEqual(2, b[3].LockRemaining);
            Assert.IsFalse(b.CanUndo);
            Assert.IsFalse(ColorCounts(b).ContainsKey(2));
            Assert.IsNull(b.RemoveColor(7), "absent color");
        }

        [Test]
        public void RemoveColor_EmptiesCompletedBottlesOfThatColorAndCanWin()
        {
            var b = Board(B(0, 0, 0, 0), B(1, 1), B(1, 1), B(2, 2, 2, 2));
            Assert.IsTrue(b[0].Completed);
            var r0 = b.RemoveColor(0);
            CollectionAssert.AreEqual(new[] { 0 }, r0.emptiedCompleted);
            Assert.IsTrue(b[0].IsEmpty);
            Assert.IsFalse(b[0].Completed);
            Assert.IsFalse(r0.won);
            var r1 = b.RemoveColor(1);
            Assert.IsTrue(r1.won);
            Assert.IsTrue(b.IsWon);
        }

        [Test]
        public void RemoveColor_RevealsTheNewTop()
        {
            var b = Board(H(2, 3, 4, 5), B());
            var r = b.RemoveColor(5);
            Assert.AreEqual(1, b[0].Hidden);
            Assert.IsTrue(r.reveals.Any(x => x.bottle == 0 && x.index == 1 && x.color == 4));
        }

        [Test]
        public void WandCandidates_MostBuriedColorFirst()
        {
            var b = Board(B(0, 1, 1), B(0, 2, 2), B(0, 0, 3), B(1, 2, 3), B());
            var c = b.WandCandidates();
            Assert.AreEqual(0, c[0], "the color buried at the bottom of three bottles helps most");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, c);
            Assert.IsEmpty(Board(B(0, 0, 0, 0), B()).WandCandidates(), "completed bottles are left alone");
        }

        static BoardState ShuffleBoard() => Board(
            B(0, 0, 0, 0),             // completed: untouched
            Stone(2, 4, 5, 4, 5),      // stone: untouched
            H(1, 1, 2, 3, 1),          // hidden count kept
            B(2, 3, 1, 2),
            B(3, 1, 2, 3),
            B(4, 5, 4, 5),
            B(),
            B());

        [Test]
        public void Shuffle_KeepsColorsBottleSizesHiddenCountsAndLeavesClosedBottlesAlone()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var b = ShuffleBoard();
                b.Pour(5, 6);
                var countsBefore = ColorCounts(b);
                var sizes = Enumerable.Range(0, b.Count).Select(i => b[i].Count).ToArray();
                var hidden = Enumerable.Range(0, b.Count).Select(i => b[i].Hidden).ToArray();
                var r = b.Shuffle(new Random(seed));
                Assert.IsNotNull(r, "seed " + seed);
                CollectionAssert.AreEquivalent(countsBefore, ColorCounts(b), "seed " + seed);
                for (int i = 0; i < b.Count; i++)
                {
                    Assert.AreEqual(sizes[i], b[i].Count, "seed " + seed + " bottle " + i);
                    Assert.AreEqual(hidden[i], b[i].Hidden, "seed " + seed + " bottle " + i);
                }
                CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, Units(b, 0));
                CollectionAssert.AreEqual(new[] { 4, 5, 4, 5 }, Units(b, 1));
                Assert.IsFalse(r.changed.Contains(0) || r.changed.Contains(1) || r.changed.Contains(7));
                Assert.IsFalse(b.CanUndo, "a shuffle clears the undo history");
                Assert.IsTrue(LevelSolver.Solve(b, 60000).Solved, "the shuffled board stays solvable (seed " + seed + ")");
            }
        }

        [Test]
        public void Shuffle_ReturnsNullWhenThereIsNothingToShuffle()
        {
            var b = Board(B(1, 2, 1, 2), B(), B(3, 3, 3, 3));
            string before = Snap(b);
            Assert.IsNull(b.Shuffle(new Random(1)));
            Assert.AreEqual(before, Snap(b));
        }

        [Test]
        public void RevealAll_RevealsEveryHiddenUnit()
        {
            var b = Board(H(3, 0, 1, 2, 3), H(1, 4, 5), B(6), B());
            Assert.IsTrue(b.AnyHidden);
            var list = b.RevealAll();
            Assert.AreEqual(4, list.Count);
            Assert.IsFalse(b.AnyHidden);
            Assert.IsTrue(list.Any(x => x.bottle == 1 && x.index == 0 && x.color == 4));
            Assert.IsEmpty(b.RevealAll(), "nothing left to reveal");
        }

        // ============================================================================================ board queries

        [Test]
        public void HasUsefulMove_IgnoresMovingASingleColorBottleIntoAnEmptyOne()
        {
            var b = Board(B(1, 1), B(2, 2), B());
            Assert.IsTrue(b.HasAnyMove);
            Assert.IsFalse(b.HasUsefulMove, "only bottle swaps are possible");
            Assert.IsTrue(Board(B(1, 2), B()).HasUsefulMove);
        }

        [Test]
        public void IsWon_WhenEveryBottleIsEmptyOrCompleted()
        {
            Assert.IsTrue(Board(B(0, 0, 0, 0), B(), B(1, 1, 1, 1)).IsWon);
            Assert.IsFalse(Board(B(0, 0, 0), B(0)).IsWon);
            Assert.IsFalse(Board(B(0, 0, 0, 0), Stone(1, 1, 2)).IsWon);
        }

        [Test]
        public void Clone_IsDeepAndForgetsTheUndoHistory()
        {
            var b = Board(B(0, 1, 1), B(1), B());
            b.Pour(0, 1);
            var c = b.Clone();
            Assert.AreEqual(b.ToString(), c.ToString());
            Assert.AreEqual(b.Moves, c.Moves);
            Assert.IsFalse(c.CanUndo);
            c.Pour(0, 2);
            Assert.AreNotEqual(b.ToString(), c.ToString());
            CollectionAssert.AreEqual(new[] { 0 }, Units(b, 0));
        }

        [Test]
        public void BoardState_NeverWritesIntoTheDefinition()
        {
            var def = Def(B(0, 1, 1), H(1, 2, 1), Stone(1, 3, 3), B());
            string before = def.ToString();
            var b = new BoardState(def);
            b.Pour(0, 1);
            b.RevealAll();
            b.BreakLock(2);
            b.AddBottle();
            b.RemoveColor(3);
            Assert.AreEqual(before, def.ToString());
            Assert.AreEqual(4, new BoardState(def).Count);
        }

        // ============================================================================================ solver

        [Test]
        public void Solver_SolvesAndTheSolutionReplays()
        {
            var def = Def(B(0, 1, 0, 1), B(1, 0, 1, 0), B(2, 3, 2, 3), B(3, 2, 3, 2), B(), B());
            var res = LevelSolver.Solve(def);
            Assert.IsTrue(res.Solved);
            Assert.IsFalse(res.Exhausted);
            Assert.Greater(res.Moves.Count, 0);
            Assert.IsTrue(Replays(new BoardState(def), res.Moves));
            Assert.IsTrue(LevelSolver.Solve(Board(B(0, 0, 0, 0), B())).Solved, "an already won board needs no move");
        }

        [Test]
        public void Solver_DetectsDeadEnds()
        {
            var noMove = Board(B(0, 1, 0, 1), B(1, 0, 1, 0), B(2, 2, 2));
            Assert.IsFalse(noMove.HasAnyMove);
            var r1 = LevelSolver.Solve(noMove, 50000);
            Assert.IsFalse(r1.Solved);
            Assert.IsTrue(r1.Exhausted);

            // Legal (even useful) pours left, but no way to finish: the background analysis reports it.
            var stuck = Board(B(1, 1, 0, 2), B(2, 1, 0, 0), B(1, 0), B(2, 2));
            Assert.IsTrue(stuck.HasUsefulMove);
            var r2 = LevelSolver.Solve(stuck, 50000);
            Assert.IsFalse(r2.Solved);
            Assert.IsTrue(r2.Exhausted);
            Assert.IsNull(stuck.Hint(5000));
        }

        [Test]
        public void Solver_WaitsForStonesToBreak()
        {
            // Color 1 can only be completed through the stone bottle, which opens after the first completion.
            var b = Board(B(0, 0, 0), B(0), Stone(1, 1, 1, 1), B(1));
            var res = LevelSolver.Solve(b, 20000);
            Assert.IsTrue(res.Solved);
            var first = res.Moves[0];
            var test = b.Clone();
            var r = test.Pour(first.from, first.to);
            Assert.IsNotNull(r);
            Assert.IsTrue(r.completed, "complete color 0 first to crack the stone");
            Assert.AreEqual(0, r.color);
            Assert.IsFalse(test[2].IsLocked);
            Assert.IsTrue(Replays(b.Clone(), res.Moves));
        }

        [Test]
        public void Solver_KnowsHiddenColors()
        {
            var def = Def(H(2, 1, 0, 0, 1), H(1, 0, 1, 1, 0), B(), B());
            var res = LevelSolver.Solve(def);
            Assert.IsTrue(res.Solved);
            Assert.IsTrue(Replays(new BoardState(def), res.Moves));
        }

        [Test]
        public void Hint_IsTheFirstMoveOfASolution()
        {
            var b = new BoardState(LevelGenerator.Generate(12));
            var hint = b.Hint();
            Assert.IsTrue(hint.HasValue);
            Assert.IsTrue(b.CanPour(hint.Value.from, hint.Value.to));
            var rest = b.Clone();
            Assert.IsNotNull(rest.Pour(hint.Value.from, hint.Value.to));
            Assert.IsTrue(LevelSolver.Solve(rest, 60000).Solved);
        }

        // ============================================================================================ game flow (pure)

        [Test]
        public void PickColor_PrefersARemovalThatKeepsTheBoardSolvable()
        {
            for (int level = 20; level <= 120; level += 20)
            {
                var b = new BoardState(LevelGenerator.Generate(level));
                var candidates = b.WandCandidates();
                int color = GameSession.PickColor(b, candidates);
                Assert.IsTrue(candidates.Contains(color), "level " + level);
                var after = b.Clone();
                Assert.IsNotNull(after.RemoveColor(color));
                Assert.IsTrue(after.IsWon || LevelSolver.Solve(after, 60000).Solved, "level " + level + " color " + color);
            }
            Assert.AreEqual(-1, GameSession.PickColor(Board(B()), new List<int>()));
            Assert.AreEqual(1, GameSession.PickColor(Board(B(1, 1, 1, 1)), new List<int> { 1 }), "falls back to the best candidate");
        }

        // ============================================================================================ generator

        const int LevelCount = 300;
        static LevelDefinition[] _levels;

        static LevelDefinition[] Levels()
        {
            if (_levels != null) return _levels;
            var arr = new LevelDefinition[LevelCount + 1];
            for (int l = 1; l <= LevelCount; l++) arr[l] = LevelGenerator.Generate(l);
            return _levels = arr;
        }

        static string Describe(LevelDefinition d) =>
            d + " | sol " + string.Join(" ", d.solution ?? new List<Move>()) + " | 3*" + d.movesFor3Stars + " 2*" + d.movesFor2Stars;

        [Test]
        public void Generator_IsDeterministic()
        {
            foreach (int l in new[] { 1, 2, 7, 15, 25, 33, 50, 101, 250 })
                Assert.AreEqual(Describe(LevelGenerator.Generate(l)), Describe(LevelGenerator.Generate(l)), "level " + l);
            Assert.AreNotEqual(Describe(LevelGenerator.Generate(40)), Describe(LevelGenerator.Generate(41)));
            Assert.AreEqual(LevelGenerator.SeedFor(5), LevelGenerator.Generate(5).seed);
        }

        [Test]
        public void Generator_Level1IsTheTutorialBoard()
        {
            var d = LevelGenerator.Generate(1);
            Assert.AreEqual(3, d.BottleCount);
            Assert.AreEqual(2, d.ColorsUsed().Length);
            Assert.IsFalse(d.HasHidden);
            Assert.IsFalse(d.HasLocks);
            Assert.AreEqual(3, d.par);
            // The hand pointer follows the verified solution: tap bottle 1, then bottle 3.
            Assert.AreEqual(new Move(0, 2), d.solution[0]);
            Assert.IsTrue(Replays(new BoardState(d), d.solution));
        }

        [Test]
        public void Generator_Levels1To300AreValidAndTheirSolutionsWin()
        {
            var levels = Levels();
            for (int l = 1; l <= LevelCount; l++)
            {
                var d = levels[l];
                string at = "level " + l;
                Assert.IsNotNull(d, at);
                Assert.AreEqual(l, d.number, at);
                Assert.AreEqual(4, d.capacity, at);
                Assert.IsFalse(string.IsNullOrEmpty(d.areaId), at);
                Assert.LessOrEqual(d.BottleCount, 15, at + " fits the screen (≤ 14 + relax)");

                // every color exactly `capacity` units; hidden prefix keeps the top visible
                var counts = new Dictionary<int, int>();
                foreach (var b in d.bottles)
                {
                    Assert.LessOrEqual(b.units.Length, d.capacity, at);
                    if (b.units.Length > 0) Assert.Less(b.hidden, b.units.Length, at + " top must be visible");
                    else Assert.AreEqual(0, b.hidden, at);
                    foreach (int u in b.units)
                    {
                        Assert.That(u, Is.InRange(0, Difficulty.PaletteSize - 1), at);
                        counts[u] = counts.TryGetValue(u, out int v) ? v + 1 : 1;
                    }
                    bool mono = b.units.Length == d.capacity && b.units.All(x => x == b.units[0]);
                    Assert.IsFalse(mono, at + " no bottle starts sorted");
                }
                foreach (var kv in counts) Assert.AreEqual(d.capacity, kv.Value, at + " color " + kv.Key);
                Assert.AreEqual(d.colorCount, counts.Count, at);

                // verified solution, par and star thresholds (GDD §2)
                Assert.IsNotNull(d.solution, at);
                Assert.AreEqual(d.par, d.solution.Count, at);
                Assert.IsTrue(Replays(new BoardState(d), d.solution), at + " solution does not replay to a win");
                Assert.AreEqual((int)Math.Ceiling(d.par * 1.3) + 2, d.movesFor3Stars, at);
                Assert.AreEqual((int)Math.Ceiling(d.par * 1.75) + 4, d.movesFor2Stars, at);

                // difficulty features
                Assert.AreEqual(Difficulty.IsHard(l), d.hard, at);
                if (l < Difficulty.FirstHiddenLevel) Assert.IsFalse(d.HasHidden, at + " hidden colors too early");
                if (l < Difficulty.FirstLockLevel) Assert.IsFalse(d.HasLocks, at + " stones too early");
                Assert.IsTrue(new BoardState(d).HasUsefulMove, at + " playable at the start");
            }
            Assert.IsTrue(levels[Difficulty.FirstHiddenLevel].HasHidden, "hidden colors are introduced on their level");
            Assert.IsTrue(levels[Difficulty.FirstLockLevel].HasLocks, "stones are introduced on their level");
        }

        [Test]
        public void Generator_FollowsTheDifficultyTable()
        {
            var levels = Levels();
            Assert.AreEqual(2, levels[1].colorCount);
            Assert.AreEqual(3, levels[2].colorCount);
            for (int l = 3; l <= LevelCount; l++)
            {
                var p = Difficulty.For(l);
                int baseColors = p.colors - (p.hard ? 1 : 0);
                int lo, hi;
                if (l <= 5) { lo = hi = 4; }
                else if (l <= 9) { lo = hi = 5; }
                else if (l <= 14) { lo = hi = 6; }
                else if (l <= 19) { lo = hi = 7; }
                else if (l <= 29) { lo = 7; hi = 8; }
                else if (l <= 39) { lo = 8; hi = 9; }
                else if (l <= 59) { lo = 9; hi = 10; }
                else if (l <= 99) { lo = 10; hi = 11; }
                else { lo = 10; hi = 12; }
                string at = "level " + l;
                if (p.colors < Difficulty.MaxColors || !p.hard) Assert.That(baseColors, Is.InRange(lo, hi), at);
                Assert.AreEqual(2, p.empties, at);
                Assert.AreEqual(p.colors, levels[l].colorCount, at);
                Assert.GreaterOrEqual(levels[l].BottleCount, p.colors + p.empties, at);
            }
        }

        [Test]
        public void Generator_PerformanceBudget()
        {
            LevelGenerator.Generate(77);   // JIT warm-up
            var watch = Stopwatch.StartNew();
            for (int l = 1; l <= 150; l++) LevelGenerator.Generate(l);
            double avg = watch.Elapsed.TotalMilliseconds / 150.0;
            // Off the critical path anyway (LevelPrefetch / the session generate on a worker thread).
            Assert.Less(avg, 150.0, $"average generation time {avg:0.0} ms");
        }

        // ============================================================================================ stars & difficulty

        [Test]
        public void Stars_FollowTheThresholds()
        {
            var d = new LevelDefinition { par = 10, movesFor3Stars = 15, movesFor2Stars = 22 };
            Assert.AreEqual(3, d.StarsFor(0));
            Assert.AreEqual(3, d.StarsFor(15));
            Assert.AreEqual(2, d.StarsFor(16));
            Assert.AreEqual(2, d.StarsFor(22));
            Assert.AreEqual(1, d.StarsFor(23));
            Assert.AreEqual(1, d.StarsFor(500));
            Assert.AreEqual(3, new LevelDefinition().StarsFor(99), "no thresholds: always 3 stars");

            // Generated levels: the generator's own solution always earns 3 stars.
            var levels = Levels();
            for (int l = 1; l <= LevelCount; l++)
            {
                Assert.AreEqual(3, levels[l].StarsFor(levels[l].par), "level " + l);
                Assert.Less(levels[l].movesFor3Stars, levels[l].movesFor2Stars, "level " + l);
            }
        }

        [Test]
        public void Difficulty_HardLevels()
        {
            foreach (int l in new[] { 10, 20, 30, 40, 45, 50, 55, 65, 100, 105 }) Assert.IsTrue(Difficulty.IsHard(l), "level " + l);
            foreach (int l in new[] { 1, 5, 9, 15, 25, 35, 41, 46, 99 }) Assert.IsFalse(Difficulty.IsHard(l), "level " + l);
            var levels = Levels();
            int hard = 0;
            for (int l = 1; l <= LevelCount; l++)
            {
                var p = Difficulty.For(l);
                Assert.AreEqual(Difficulty.IsHard(l), p.hard, "level " + l);
                if (!p.hard) continue;
                hard++;
                Assert.AreEqual(1, p.maxRun, "level " + l + ": hard levels never start with two equal units side by side");
                foreach (var b in levels[l].bottles)
                    for (int k = 1; k < b.units.Length; k++)
                        Assert.AreNotEqual(b.units[k - 1], b.units[k], "level " + l);
                // one more color than its normal neighbour (before the noisy 10-12 plateau of levels 100+)
                if (l < 100)
                {
                    var prev = Difficulty.For(l - 1);
                    Assert.GreaterOrEqual(p.colors, Math.Min(Difficulty.MaxColors, prev.colors + (prev.hard ? 0 : 1)), "level " + l);
                }
            }
            Assert.Greater(hard, 40);
        }

        [Test]
        public void Difficulty_HiddenAndStonesArriveOnTheirLevels()
        {
            var p15 = Difficulty.For(Difficulty.FirstHiddenLevel);
            Assert.AreEqual(2, p15.hiddenBottles);
            Assert.AreEqual(1, p15.hiddenDepth);
            var p25 = Difficulty.For(Difficulty.FirstLockLevel);
            Assert.AreEqual(1, p25.locks);
            Assert.AreEqual(2, p25.lockCount);
            for (int l = 1; l < Difficulty.FirstHiddenLevel; l++) Assert.AreEqual(0, Difficulty.For(l).hiddenBottles, "level " + l);
            for (int l = 1; l < Difficulty.FirstLockLevel; l++) Assert.AreEqual(0, Difficulty.For(l).locks, "level " + l);
            for (int l = 1; l <= LevelCount; l++)
                Assert.LessOrEqual(Difficulty.For(l).hiddenDepth, Difficulty.Capacity - 1, "level " + l);
        }
    }
}
