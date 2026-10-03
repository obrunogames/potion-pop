// ============================================================================================================
// CONTRACT STUB — the public API of the board view (owner: Board agent). The gameplay flow (GameSession) is written
// against exactly these members; the Board agent replaces the bodies (and adds private members / partial files /
// helper classes such as BottleView, LiquidGraphic, BottleShape, BoardLayout) WITHOUT changing these signatures.
//
// Responsibilities of the view:
//  * draws the bottles of a BoardState inside its RectTransform (layout in rows, scaled to fit), glass sprites from
//    bottle_shape.json + liquid drawn as a mesh with a horizontal surface even while the bottle tilts;
//  * handles taps: first tap lifts a selectable bottle, a tap on another bottle asks the session to pour
//    (OnPourRequested), a tap on the lifted bottle puts it back; taps on busy (animating) bottles are ignored;
//  * animates whatever the session tells it (PlayPour, PlayUndo, boosters...). Several pours may animate at once
//    on different bottles: the model (BoardState) is already updated when Play* is called.
// The view never changes the BoardState itself.
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.Levels;
using UnityEngine;

namespace PotionPop.Game.Board
{
    public sealed class BoardView : MonoBehaviour
    {
        // ------------------------------------------------------------------------------------------------ events

        /// <summary>The player asks to pour from → to (both idle, from was lifted). The session validates with
        /// BoardState.Pour and answers with PlayPour (legal) or PlayInvalid (illegal).</summary>
        public event Action<int, int> OnPourRequested;
        /// <summary>A bottle was lifted (tutorial hand progression, sound is played by the view).</summary>
        public event Action<int> OnBottleSelected;
        /// <summary>A stone-wrapped bottle was tapped (the session may offer the rewarded-ad unlock).</summary>
        public event Action<int> OnLockedTapped;
        /// <summary>Every running animation finished (the session checks win / stuck here).</summary>
        public event Action OnIdle;

        // ------------------------------------------------------------------------------------------------ setup

        /// <summary>Creates the view stretched inside <paramref name="parent"/>.</summary>
        public static BoardView Create(RectTransform parent)
        {
            var go = new GameObject("BoardView", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go.AddComponent<BoardView>();
        }

        /// <summary>The board being shown (null after Clear).</summary>
        public BoardState Board { get; private set; }

        /// <summary>Builds every bottle of <paramref name="board"/> (snapped to its current state, hidden until
        /// PlayIntro). <paramref name="areaId"/> picks themed accents if any.</summary>
        public void Build(BoardState board, string areaId) { Board = board; }

        /// <summary>Destroys every bottle and stops every animation.</summary>
        public void Clear() { Board = null; }

        /// <summary>Recomputes the layout for the current rect / bottle count (call after the rect changed).</summary>
        public void Relayout(bool animate) { }

        /// <summary>Snaps every bottle's visuals to the model (no animation). Safe at any time.</summary>
        public void RefreshAll() { }

        // ------------------------------------------------------------------------------------------------ state

        /// <summary>Taps are accepted (the session turns it off during intro, popups, win...).</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Index of the lifted bottle (-1 none).</summary>
        public int Selected => -1;

        /// <summary>Any animation is running.</summary>
        public bool IsAnimating => false;

        /// <summary>That bottle is part of a running animation (can't be tapped).</summary>
        public bool IsBusy(int bottle) => false;

        /// <summary>Puts the lifted bottle back down.</summary>
        public void Deselect(bool animate = true) { }

        /// <summary>World position of a bottle's center (tutorial hand, FX, flying rewards).</summary>
        public Vector3 BottleWorldPosition(int bottle) => transform.position;

        /// <summary>World position of a bottle's mouth (top opening).</summary>
        public Vector3 BottleMouthWorldPosition(int bottle) => transform.position;

        /// <summary>RectTransform of a bottle (null if out of range).</summary>
        public RectTransform BottleRect(int bottle) => null;

        // ------------------------------------------------------------------------------------------------ animations
        // Every Play* call is safe to make while other animations run; onDone fires when that animation ends (also
        // when the view is cleared mid-way). OnIdle fires after the last running animation ends.

        /// <summary>Bottles drop into place one after another (level start).</summary>
        public void PlayIntro(Action onDone) { onDone?.Invoke(); }

        /// <summary>Pour: the source flies above the target, tilts, a stream of the color falls, the target fills, the
        /// source returns; then reveals (new top of the source, hidden units poured), the cork when completed (with
        /// sparkles and a little bounce) and stone counter ticks / stone breaking.</summary>
        public void PlayPour(PourResult result, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Illegal pour: the lifted bottle wobbles "no" and goes back down.</summary>
        public void PlayInvalid(int from, int to, PourRefusal why) { }

        /// <summary>A bottle that can't be lifted was tapped (empty / completed): small shake.</summary>
        public void PlayRefuseSelect(int bottle) { }

        /// <summary>Undo: the liquid flows back from result.to into result.from (cork pops off if uncorked; stone
        /// counters go back up).</summary>
        public void PlayUndo(UndoResult result, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Extra Bottle: an empty bottle (already in the model at <paramref name="bottle"/>) pops into the
        /// layout; the others slide to make room.</summary>
        public void PlayAddBottle(int bottle, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Magic Wand / Rainbow Potion: every unit of the color glows, rises out of its bottle as magic
        /// sparkles and flies to <paramref name="flyTo"/> (world position, e.g. the booster button); the liquid above
        /// settles down; corks of emptied bottles vanish; stones tick.</summary>
        public void PlayRemoveColor(RemoveColorResult result, Vector3 flyTo, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Shuffle: bottles shake and swirl, then show their new contents.</summary>
        public void PlayShuffle(ShuffleResult result, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Crystal Ball: every "?" unit flips to its color with a sparkle (staggered).</summary>
        public void PlayRevealAll(List<Reveal> reveals, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>A stone broke by rewarded ad (model already unlocked).</summary>
        public void PlayBreakLock(int bottle, Action onDone = null) { onDone?.Invoke(); }

        /// <summary>Level won: completed bottles bounce in a wave with sparkles.</summary>
        public void PlayWin(Action onDone) { onDone?.Invoke(); }

        /// <summary>Hint: pulses the source and target bottles with an arrow between them until HideHint.</summary>
        public void ShowHint(Move move) { }

        public void HideHint() { }

        // ------------------------------------------------------------------------------------------------ helpers for the stub

        void RaiseUnusedEvents()
        {
            OnPourRequested?.Invoke(-1, -1);
            OnBottleSelected?.Invoke(-1);
            OnLockedTapped?.Invoke(-1);
            OnIdle?.Invoke();
        }
    }
}
