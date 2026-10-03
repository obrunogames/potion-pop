using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Squash-on-press behaviour + click sound + light haptic. Added by UIKit to every button.
    /// Press squashes `target` (default: this transform) to pressedScale in DS.Motion.Fast, release springs back with
    /// OutBack. The sound/haptic play on a real click only (not when a ScrollRect drag cancels the press) and never
    /// when the attached Selectable (Button) is not interactable.
    /// </summary>
    [DisallowMultipleComponent]
    public class Pressable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler,
        IPointerEnterHandler, IPointerClickHandler
    {
        public float pressedScale = 0.92f;
        public bool playSound = true;

        // ---- additions
        /// <summary>Transform to squash (UIKit points it at the button's visual child so outside scale tweens such
        /// as a CTA pulse on the root never fight with the press animation).</summary>
        public Transform target;
        public Sfx clickSound = Sfx.Click;
        public bool haptic = true;

        Selectable _selectable;
        bool _selectableSearched;
        bool _pressed;
        int _pointerId;
        Vector3 _rest = Vector3.one;
        bool _hasRest;
        TweenHandle _tween;

        Transform Target => target != null ? target : transform;

        bool CanInteract
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                if (!_selectableSearched)
                {
                    _selectable = GetComponent<Selectable>();
                    _selectableSearched = true;
                }
                return _selectable == null || _selectable.IsInteractable();
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pressed || e.button != PointerEventData.InputButton.Left || !CanInteract) return;
            _pointerId = e.pointerId;
            Press();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_pressed && e.pointerId == _pointerId) Release();
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_pressed && e.pointerId == _pointerId) Release();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            // Finger slid back onto the button while still held: squash again (the click is still eligible).
            if (!_pressed && e.pointerPress == gameObject && e.eligibleForClick && CanInteract)
            {
                _pointerId = e.pointerId;
                Press();
            }
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !CanInteract) return;
            if (playSound) AudioManager.Play(clickSound);
            if (haptic) Haptics.Play(HapticType.Light);
        }

        void Press()
        {
            var t = Target;
            // Capture the rest scale only when no press animation is running (otherwise it is mid-flight).
            if (!_hasRest || _tween == null || !_tween.IsActive)
            {
                _rest = t.localScale;
                _hasRest = true;
            }
            _pressed = true;
            _tween?.Kill();
            _tween = Tween.Scale(t, _rest * pressedScale, DS.Motion.Fast, Ease.OutQuad);
        }

        void Release()
        {
            _pressed = false;
            _tween?.Kill();
            _tween = Tween.Scale(Target, _rest, DS.Motion.Base, Ease.OutBack);
        }

        void OnDisable()
        {
            if (!_hasRest) return;
            bool animating = _tween != null && _tween.IsActive;
            _tween?.Kill();
            _tween = null;
            if (_pressed || animating) Target.localScale = _rest;
            _pressed = false;
        }
    }
}
