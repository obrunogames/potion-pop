// ============================================================================================================
// Board module: taps and selection feedback.
//  * Pointer-down on a bottle column (BottleTap relay, generous hit area incl. the space above the bottle):
//      stone bottle → OnLockedTapped (+ stone nudge) · lifted bottle → put down (Sfx.Deselect) ·
//      another bottle while one is lifted → OnPourRequested(from, to), the session answers PlayPour / PlayInvalid
//      (no answer → the bottle is put down) · selectable bottle → lift (Sfx.Select, light haptic, OnBottleSelected) ·
//      empty / completed bottle → PlayRefuseSelect.
//  * Pouring bottles accept the next intent against the committed model. Their jobs serialize per bottle;
//    independent jobs start immediately. Selection never takes the pose away from an animation.
//  * Shattering stones still ignore taps; InputEnabled gates intro, popups, boosters and ended attempts.
//  * Pointer-down on the board background puts the lifted bottle down.
// ============================================================================================================
using PotionPop.Levels;
using PotionPop.UI;
using UnityEngine;

namespace PotionPop.Game.Board
{
    public sealed partial class BoardView
    {
        const float LiftTiltDeg = 3.5f;
        bool _requestingPour;

        internal void HandleBottleTap(int i)
        {
            if (!InputEnabled || Board == null || _requestingPour || i < 0 || i >= Board.Count) return;
            var v = View(i);
            if (v == null || _clock < v.fxBusyUntil) return;
            var b = Board[i];

            if (b.IsLocked)
            {
                if (_selected >= 0) DeselectInternal(true, false);
                v.StoneNudge();
                Haptics.Play(HapticType.Light);
                Invoke(OnLockedTapped, i);
                return;
            }
            if (v.lockShown > 0) return;   // the stone of an already-opened bottle is about to shatter

            if (_selected == i)
            {
                DeselectInternal(true, true);
                return;
            }

            if (_selected >= 0)
            {
                int from = _selected;
                if (View(from) == null)
                {
                    _selected = -1;
                }
                else
                {
                    int stamp = _answerStamp;
                    _requestingPour = true;
                    try { Invoke(OnPourRequested, from, i); }
                    finally { _requestingPour = false; }
                    // No PlayPour / PlayInvalid came back: don't leave the bottle hanging in the air.
                    if (stamp == _answerStamp && _selected == from) DeselectInternal(true, false);
                    return;
                }
            }

            if (Board.CanSelect(i))
            {
                _selected = i;
                if (IsBusy(i)) v.SetSelectedGlow(true);
                else LiftSelectedWhenReady();
                AudioManager.Play(Sfx.Select);
                Haptics.Play(HapticType.Light);
                Invoke(OnBottleSelected, i);
            }
            else PlayRefuseSelect(i);
        }

        internal void HandleBackgroundTap()
        {
            if (!InputEnabled || Board == null || _selected < 0) return;
            DeselectInternal(true, true);
        }

        /// <summary>A tap during a pour remembers selection; only lift once its job released the pose.</summary>
        void LiftSelectedWhenReady()
        {
            var v = View(_selected);
            if (v == null || v.lifted || IsBusy(_selected)) return;
            v.Lift(LiftTilt(v));
            v.slot.SetAsLastSibling();
        }

        /// <summary>Lifted bottles lean slightly toward the board center (ready to pour).</summary>
        float LiftTilt(BottleView v) => v.home.x > 1f ? LiftTiltDeg : (v.home.x < -1f ? -LiftTiltDeg : 0f);

        void DeselectInternal(bool animate, bool sound)
        {
            if (_selected < 0) return;
            var v = View(_selected);
            _selected = -1;
            if (v == null) return;
            if (v.holder == null) v.Lower(animate);
            else v.SetSelectedGlow(false);
            if (sound) AudioManager.Play(Sfx.Deselect);
        }

        // ------------------------------------------------------------------------------------------------ feedback

        /// <summary>Illegal pour: the lifted bottle wobbles "no" and goes back down. Visual only: the session plays
        /// Sfx.Invalid / the haptic / the toast.</summary>
        public void PlayInvalid(int from, int to, PourRefusal why)
        {
            _answerStamp++;
            var vf = View(from);
            var vt = View(to);
            if (vf != null && vf.holder == null)
            {
                vf.PlayNo();
                if (_selected == from || vf.lifted)
                {
                    if (_selected == from) _selected = -1;
                    vf.Lower(true, 0.24f);
                }
            }
            else if (_selected == from) DeselectInternal(true, false);

            bool targetIssue = why == PourRefusal.TargetFull || why == PourRefusal.WrongColor ||
                               why == PourRefusal.TargetDone || why == PourRefusal.TargetLocked;
            if (vt != null && to != from && vt.holder == null && targetIssue)
            {
                if (why == PourRefusal.TargetLocked) vt.StoneNudge();
                else vt.PlayNudge();
            }
        }

        /// <summary>A bottle that can't be lifted was tapped (empty / completed): small shake.</summary>
        public void PlayRefuseSelect(int bottle)
        {
            var v = View(bottle);
            if (v == null || v.holder != null) return;
            v.PlayNudge();
            Haptics.Play(HapticType.Light);
        }
    }
}
