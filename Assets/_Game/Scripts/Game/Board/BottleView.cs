// ============================================================================================================
// Board module: one bottle's visuals and its self-contained effects (the BoardView owns state and choreography).
//
// Hierarchy (every rect's local origin = the bottle's shape origin, the inner bottom center; 1 shape unit = UnitPx):
//   Slot     layout position + scale, CanvasGroup (intro fade); rect = glass bounds  ← BoardView.BottleRect
//   ├ Shadow contact shadow on the shelf (stays down when the bottle lifts / flies)
//   └ Body   pose: lift, pour flight + tilt, win hop
//      └ Visual   secondary motion: squash/bounce, "no" wobble, shake, hint pulse
//         ├ Glow     selection / hint halo (bottle_glow, or a soft radial glow)
//         ├ Back     glass back (bottle_back, or GlassGraphic)
//         ├ Cork     pops into the neck when the bottle is completed: drawn under the liquid and the front glass, so
//         │          only its top sticks out of the mouth (the brim-full liquid and the lip hide the part inside)
//         ├ Liquid   LiquidGraphic (layers with a horizontal surface)
//         ├ Marks    "?" glyphs of hidden units (TMP, kept upright, following their layer)
//         ├ InnerFx  bubbles / splash inside the glass
//         ├ Stream   incoming pour stream (StreamGraphic)
//         ├ Front    glass front (bottle_front, or GlassGraphic)
//         └ Stone    stone wrap + counter badge + AD badge (built on demand)
// Missing sprites never show the magenta placeholder: every piece has a procedural/design-system fallback.
// ============================================================================================================
using System;
using System.Collections.Generic;
using PotionPop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.Game.Board
{
    internal sealed class BottleView
    {
        /// <summary>Local pixels per shape unit (the layout scales the slot).</summary>
        public const float UnitPx = 100f;
        /// <summary>A lifted bottle rises this fraction of its glass height.</summary>
        public const float LiftFraction = 0.12f;
        const float LiftScale = 1.04f;
        const float StreamWidthUnits = 0.15f;
        /// <summary>Share of the cork's height above the mouth; the rest is inside the neck.</summary>
        const float CorkOutside = 0.45f;

        static readonly Color GlowSelect = new Color(1f, 0.87f, 0.4f, 1f);
        static readonly Color GlowHint = new Color(1f, 1f, 1f, 1f);
        static readonly Color ShadowTint = new Color(0.12f, 0.04f, 0.2f, 0.34f);
        static readonly Color StoneTint = new Color(0.87f, 0.82f, 0.73f, 1f);
        static readonly Color CorkTint = new Color(0.78f, 0.58f, 0.38f, 1f);
        static readonly Color[] ChunkColors = { new Color(0.93f, 0.89f, 0.8f, 1f), new Color(0.82f, 0.76f, 0.66f, 1f), new Color(0.7f, 0.64f, 0.56f, 1f) };
        static readonly Color[] PoofColors = { new Color(1f, 1f, 1f, 0.9f), new Color(0.95f, 0.92f, 0.86f, 0.85f) };
        static readonly Color[] SparkColors = { Color.white, new Color(1f, 0.93f, 0.6f, 1f) };
        static readonly Color[] StarColors = { new Color(1f, 0.78f, 0f, 1f), new Color(1f, 0.88f, 0.4f, 1f), Color.white };

        public readonly int index;
        public readonly BottleShape shape;
        readonly RectTransform _fxSpace;

        public RectTransform slot, body, visual, marks, innerFx;
        public CanvasGroup group;
        public Image shadow, glow, cork;
        public Graphic back, front;
        public LiquidGraphic liquid;
        public StreamGraphic stream;

        RectTransform _stoneRoot, _stoneBadge, _adBadge;
        CanvasGroup _stoneGroup;
        Image _stoneImage;
        TMP_Text _stoneText;
        readonly List<TMP_Text> _marks = new List<TMP_Text>(3);
        Vector2 _corkHome;
        Color _corkColor = Color.white;
        float _shadowAlpha;

        /// <summary>What the visuals represent at rest (the model state of the last finished animation).</summary>
        public readonly BottleData shown = new BottleData();
        /// <summary>Stone counter on screen and the request sequence it came from (latest request wins).</summary>
        public int lockShown, lockSeq;
        public bool corked;
        /// <summary>Animation job currently owning this bottle (null = idle).</summary>
        internal BoardJob holder;
        /// <summary>Board clock until which taps are ignored (stone shatter).</summary>
        public float fxBusyUntil;

        // placement (driven by BoardView.UpdatePlacement)
        public Vector2 home, slideFrom;
        public float scale = 1f, scaleFrom = 1f, slideT = 1f, slideDuration = 0.35f;
        public Vector2 introOffset;
        public float introScale = 1f;
        public int row, column;
        /// <summary>Has been given a slot by a layout (new views snap to their first slot instead of sliding).</summary>
        public bool placed;

        // pose / modes
        public bool lifted;
        bool _glowSelected, _glowHint;
        TweenHandle _hintPulse;

        // incoming stream (set by a pour job, drawn in UpdateStream)
        public bool streamOn;
        public Vector3 streamTopWorld;
        public float streamHead, streamTail, streamWidth;
        public int streamColor;
        public Vector2 streamLanding;
        float _streamPhase;

        BottleView(int index, BottleShape shape, RectTransform fxSpace)
        {
            this.index = index;
            this.shape = shape;
            _fxSpace = fxSpace;
        }

        // ------------------------------------------------------------------------------------------- geometry

        public float GlassHeightPx => shape.GlassHeight * UnitPx;
        public float GlassWidthPx => shape.GlassWidth * UnitPx;
        public Vector2 MouthLocal => new Vector2(0f, shape.MouthY * UnitPx);
        public Vector2 CenterLocal => new Vector2((shape.GlassLeft + shape.GlassRight) * 0.5f * UnitPx, (shape.GlassBottom + shape.GlassTop) * 0.5f * UnitPx);
        /// <summary>Current layout scale of the bottle (FX sizes follow it).</summary>
        public float FxScale => slot != null ? Mathf.Max(0.05f, slot.localScale.x) : 1f;

        /// <summary>Point of the lip the liquid leaves from when pouring over side <paramref name="pourSide"/> (−1 left).</summary>
        public Vector2 LipLocal(int pourSide) =>
            new Vector2(pourSide * (shape.NeckHalf + shape.glass * 0.5f) * UnitPx, (shape.MouthY + shape.lipHeight * 0.2f) * UnitPx);

        public Vector3 MouthWorld => visual.TransformPoint(MouthLocal);
        public Vector3 RestMouthWorld => slot.TransformPoint(MouthLocal);
        public Vector3 RestCenterWorld => slot.TransformPoint(CenterLocal);
        public Vector3 CenterWorld => visual.TransformPoint(CenterLocal);

        Vector2 FxPos(Vector3 world) => _fxSpace != null ? (Vector2)_fxSpace.InverseTransformPoint(world) : Vector2.zero;

        // ------------------------------------------------------------------------------------------- build

        public static BottleView Create(RectTransform parent, int index, BottleShape shape, RectTransform fxSpace, int capacity)
        {
            var v = new BottleView(index, shape, fxSpace);
            v.Build(parent, capacity);
            return v;
        }

        void Build(RectTransform parent, int capacity)
        {
            float u = UnitPx;
            var size = new Vector2(shape.GlassWidth * u, shape.GlassHeight * u);
            var pivot = new Vector2(-shape.GlassLeft / shape.GlassWidth, -shape.GlassBottom / shape.GlassHeight);

            slot = UIKit.Rect("Bottle" + index, parent);
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.pivot = pivot;
            slot.sizeDelta = size;
            group = slot.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // contact shadow (child of the slot: it stays on the shelf)
            var frame = BottleArt.Frame;
            var shadowSprite = BottleArt.Shadow;
            Vector2 shSize, shPos = new Vector2(0f, shape.GlassBottom * u);
            if (shadowSprite != null && frame.hasShadow)
            {
                shSize = frame.ShadowSizePx(u);
                shPos = frame.ShadowCenterPx(u);
            }
            else if (shadowSprite != null) shSize = new Vector2(1.35f * u, 1.35f * u / Mathf.Max(0.5f, BottleArt.Aspect("bottle_shadow", 4.7f)));
            else shSize = new Vector2(1.55f * u, 0.3f * u);
            shadow = NewImage("Shadow", slot, shadowSprite != null ? shadowSprite : UISprites.Glow,
                shadowSprite != null ? Color.white : ShadowTint, shSize, new Vector2(0.5f, 0.5f), shPos);
            _shadowAlpha = shadow.color.a;

            body = Child("Body", slot);
            body.pivot = pivot;
            body.sizeDelta = size;
            visual = Child("Visual", body);
            visual.pivot = pivot;
            visual.sizeDelta = size;

            Vector2 frameSize = frame.SizePx(u), framePivot = frame.Pivot;
            var clear = new Color(1f, 1f, 1f, 0f);

            var glowSprite = BottleArt.Glow;
            glow = glowSprite != null
                ? NewImage("Glow", visual, glowSprite, clear, frameSize, framePivot, Vector2.zero)
                : NewImage("Glow", visual, UISprites.Glow, clear, new Vector2(shape.GlassWidth * u * 2.2f, shape.GlassHeight * u * 1.28f),
                    new Vector2(0.5f, 0.5f), CenterLocal);
            glow.canvasRenderer.cullTransparentMesh = true;   // invisible most of the time: no overdraw while alpha = 0

            back = BottleArt.Back != null
                ? (Graphic)NewImage("Back", visual, BottleArt.Back, Color.white, frameSize, framePivot, Vector2.zero)
                : GlassFallback("Back", false);

            // cork: about as wide as the neck glass, pushed into the neck so only its top (CorkOutside) shows above
            // the mouth; created before the liquid and the front glass, which cover the part inside the neck
            var corkSprite = BottleArt.Cork;
            float corkW = (shape.NeckHalf + shape.glass) * 2f * 0.98f * u;
            float corkH = corkW / Mathf.Clamp(BottleArt.Aspect("cork", 0.86f), 0.4f, 2f);
            if (corkSprite == null) corkH = corkW * 0.8f;
            _corkHome = new Vector2(0f, shape.MouthY * u - corkH * (1f - CorkOutside));
            _corkColor = corkSprite != null ? Color.white : CorkTint;
            cork = NewImage("Cork", visual, corkSprite != null ? corkSprite : UISprites.Rounded, _corkColor,
                new Vector2(corkW, corkH), new Vector2(0.5f, 0f), _corkHome);
            if (corkSprite == null) Sliced(cork, 1.6f);
            cork.gameObject.SetActive(false);

            var lrt = Child("Liquid", visual);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(shape.innerWidth * u, shape.MouthY * u);
            liquid = lrt.gameObject.AddComponent<LiquidGraphic>();
            liquid.Setup(shape, u, capacity);

            marks = Child("Marks", visual);
            innerFx = Child("InnerFx", visual);
            var srt = Child("Stream", visual);
            stream = srt.gameObject.AddComponent<StreamGraphic>();

            front = BottleArt.Front != null
                ? (Graphic)NewImage("Front", visual, BottleArt.Front, Color.white, frameSize, framePivot, Vector2.zero)
                : GlassFallback("Front", true);

        }

        Graphic GlassFallback(string name, bool isFront)
        {
            var rt = Child(name, visual);
            var g = rt.gameObject.AddComponent<GlassGraphic>();
            g.Setup(shape, UnitPx, isFront);
            return g;
        }

        /// <summary>Child rect whose anchored position is an offset from the parent's local origin (pivot).</summary>
        static RectTransform Child(string name, RectTransform parent)
        {
            var rt = UIKit.Rect(name, parent);
            rt.anchorMin = rt.anchorMax = parent.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        static Image NewImage(string name, RectTransform parent, Sprite sprite, Color color, Vector2 size, Vector2 pivot, Vector2 pos)
        {
            var img = UIKit.NewImage(parent, name, sprite, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = parent.pivot;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            img.preserveAspect = false;
            return img;
        }

        /// <summary>9-slices a procedural rounded sprite (corner radius ≈ 28 px / multiplier).</summary>
        static void Sliced(Image img, float multiplier)
        {
            if (img == null || img.sprite == null || img.sprite.border == Vector4.zero) return;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = multiplier;
        }

        /// <summary>Kills every tween of the bottle (their callbacks must not run after a Clear) and destroys it.</summary>
        public void Destroy()
        {
            if (slot == null) return;
            var all = slot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) Tween.Kill(all[i].gameObject);
            UnityEngine.Object.Destroy(slot.gameObject);
            slot = null;
        }

        // ------------------------------------------------------------------------------------------- state sync

        /// <summary>Snaps liquid, marks and cork to <paramref name="d"/> (the stone counter is separate: SnapLock).</summary>
        public void SetData(BottleData d)
        {
            shown.CopyFrom(d);
            SyncLiquid();
            SetCorked(shown.completed);
        }

        /// <summary>Rebuilds the liquid layers from <see cref="shown"/> (no animation).</summary>
        public void SyncLiquid()
        {
            liquid.SetUnits(shown.units, shown.hidden);
            liquid.Refresh(0f);
            UpdateMarks();
        }

        public void SetCorked(bool on)
        {
            corked = on;
            if (cork == null) return;
            var rt = cork.rectTransform;
            Tween.Kill(rt);
            Tween.Kill(cork);
            cork.gameObject.SetActive(on);
            rt.anchoredPosition = _corkHome;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            cork.color = _corkColor;
        }

        /// <summary>Puts the visuals in their resting pose (kills pose tweens; keeps liquid, cork and stone).</summary>
        public void ResetPose()
        {
            KillPoseTweens();
            lifted = false;
            body.anchoredPosition = Vector2.zero;
            body.localRotation = Quaternion.identity;
            body.localScale = Vector3.one;
            visual.anchoredPosition = Vector2.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            Tween.Kill(shadow);
            Tween.Kill(shadow.rectTransform);
            shadow.color = DS.WithAlpha(shadow.color, _shadowAlpha);
            shadow.rectTransform.localScale = Vector3.one;
            streamOn = false;
            if (stream != null) stream.Hide();
        }

        /// <summary>Kills the tweens that move the body / visual (lift, wobble, squash, hint pulse).</summary>
        public void KillPoseTweens()
        {
            Tween.Kill(body);
            Tween.Kill(visual);
            _hintPulse = null;
        }

        // ------------------------------------------------------------------------------------------- selection

        public void Lift(float tiltDeg)
        {
            lifted = true;
            Tween.Kill(body);
            Tween.Move(body, new Vector2(0f, GlassHeightPx * LiftFraction), 0.17f, Ease.OutBack).SetOvershoot(1.7f);
            Tween.Rotate(body, tiltDeg, 0.2f, Ease.OutQuad);
            Tween.Scale(body, LiftScale, 0.17f, Ease.OutBack);
            SetShadow(0.5f, 0.82f, 0.15f);
            SetSelectedGlow(true);
            liquid.Wobble(3.5f);
        }

        public void Lower(bool animate, float delay = 0f)
        {
            lifted = false;
            SetSelectedGlow(false);
            Tween.Kill(body);
            if (!animate)
            {
                body.anchoredPosition = Vector2.zero;
                body.localRotation = Quaternion.identity;
                body.localScale = Vector3.one;
                SetShadow(1f, 1f, 0f);
                return;
            }
            Tween.Move(body, Vector2.zero, 0.2f, Ease.InQuad).SetDelay(delay).OnComplete(Land);
            Tween.Rotate(body, 0f, 0.2f, Ease.OutQuad).SetDelay(delay);
            Tween.Scale(body, 1f, 0.18f, Ease.OutQuad).SetDelay(delay);
            SetShadow(1f, 1f, 0.2f, delay);
        }

        /// <summary>Soft landing: tiny squash and a slosh.</summary>
        public void Land()
        {
            if (visual == null) return;
            liquid.Wobble(2.5f);
            Squash(new Vector3(1.05f, 0.95f, 1f), 0.06f, 0.24f, 2f);
        }

        void Squash(Vector3 squash, float inTime, float outTime, float overshoot)
        {
            var v = visual;
            Tween.Scale(v, squash, inTime, Ease.OutQuad).OnComplete(() =>
            {
                if (v != null) Tween.Scale(v, Vector3.one, outTime, Ease.OutBack).SetOvershoot(overshoot);
            });
        }

        public void SetShadow(float alphaK, float scaleK, float duration, float delay = 0f)
        {
            if (shadow == null) return;
            Tween.Kill(shadow);
            Tween.Kill(shadow.rectTransform);
            float a = _shadowAlpha * alphaK;
            if (duration <= 0f)
            {
                shadow.color = DS.WithAlpha(shadow.color, a);
                shadow.rectTransform.localScale = new Vector3(scaleK, scaleK, 1f);
                return;
            }
            Tween.Fade(shadow, a, duration).SetDelay(delay);
            Tween.Scale(shadow.rectTransform, scaleK, duration, Ease.OutQuad).SetDelay(delay);
        }

        public void SetSelectedGlow(bool on)
        {
            if (_glowSelected == on) return;
            _glowSelected = on;
            UpdateGlow();
        }

        public void SetHint(bool on)
        {
            if (_glowHint == on) return;
            _glowHint = on;
            UpdateGlow();
            if (_hintPulse != null)
            {
                _hintPulse.Kill();
                _hintPulse = null;
                if (visual != null) visual.localScale = Vector3.one;
            }
            if (on && visual != null)
            {
                // start from the resting pose (a pulse captured mid-squash would oscillate around the squash)
                Tween.Kill(visual);
                visual.localScale = Vector3.one;
                visual.localRotation = Quaternion.identity;
                visual.anchoredPosition = Vector2.zero;
                _hintPulse = Tween.Pulse(visual, 1.05f, 0.8f);
            }
        }

        void UpdateGlow()
        {
            if (glow == null) return;
            Tween.Kill(glow);
            bool on = _glowSelected || _glowHint;
            if (!on)
            {
                Tween.Fade(glow, 0f, 0.15f);
                return;
            }
            var c = _glowSelected ? GlowSelect : GlowHint;
            c.a = glow.color.a;
            glow.color = c;
            var g = glow;
            Tween.Fade(g, 0.95f, 0.12f).OnComplete(() =>
            {
                if (g != null) Tween.Fade(g, 0.5f, 0.55f, Ease.InOutSine).SetLoops(-1, true);
            });
        }

        // ------------------------------------------------------------------------------------------- feedback

        /// <summary>"No" wobble (invalid pour): a few damped swings of the visual.</summary>
        public void PlayNo()
        {
            var v = visual;
            Tween.Value(0f, 1f, 0.36f, p =>
            {
                if (v == null) return;
                float a = Mathf.Sin(p * Mathf.PI * 3f) * (1f - p) * 9f;
                v.localRotation = Quaternion.Euler(0f, 0f, a);
            }).SetLink(v).OnComplete(() => { if (v != null) v.localRotation = Quaternion.identity; });
            liquid.Wobble(5f);
        }

        /// <summary>Small horizontal shake (refused tap).</summary>
        public void PlayNudge()
        {
            Tween.Shake(visual, 0.07f * UnitPx, 0.28f);
            liquid.Wobble(3f);
        }

        /// <summary>Bottle bounce (cork impact, win hop landing).</summary>
        public void Bounce(float k = 1f)
        {
            Squash(new Vector3(1f + 0.07f * k, 1f - 0.09f * k, 1f), 0.07f, 0.32f, 2.4f);
            liquid.Wobble(3f * k);
        }

        /// <summary>Idle shimmer of a completed bottle: a light sweep through the liquid.</summary>
        public void Shimmer() => liquid.Shine(0.85f);

        // ------------------------------------------------------------------------------------------- cork

        /// <summary>Cork drops in and slides into the neck (behind the liquid and the glass) with a squash, sparkles, a
        /// star burst and a bottle bounce.</summary>
        public void PopCork(Action onImpact)
        {
            corked = true;
            var rt = cork.rectTransform;
            Tween.Kill(rt);
            Tween.Kill(cork);
            cork.gameObject.SetActive(true);
            rt.anchoredPosition = _corkHome + new Vector2(0f, 0.95f * UnitPx);
            rt.localScale = new Vector3(0.72f, 1.3f, 1f);
            rt.localRotation = Quaternion.identity;
            cork.color = DS.WithAlpha(_corkColor, 0f);
            Tween.Fade(cork, _corkColor.a, 0.08f);
            Tween.Scale(rt, new Vector3(0.9f, 1.12f, 1f), 0.18f, Ease.OutQuad);
            Tween.Move(rt, _corkHome, 0.19f, Ease.InQuad).OnComplete(() =>
            {
                if (rt == null) return;
                Tween.Scale(rt, new Vector3(1.32f, 0.7f, 1f), 0.05f, Ease.OutQuad).OnComplete(() =>
                {
                    if (rt != null) Tween.Scale(rt, Vector3.one, 0.32f, Ease.OutBack).SetOvershoot(2.4f);
                });
                Bounce(1f);
                Vector2 p = FxPos(visual.TransformPoint(new Vector2(0f, (shape.MouthY + 0.15f) * UnitPx)));
                float s = FxScale;
                FX.Sparkles(_fxSpace, p, 8);
                FX.Burst(_fxSpace, p, "ui_star_small", 9, StarColors, 520f * s, 0.65f, 34f * s, -1100f);
                FX.Ring(_fxSpace, p, new Color(1f, 0.95f, 0.7f, 0.85f), 230f * s);
                Haptics.Play(HapticType.Light);
                if (onImpact != null)
                {
                    try { onImpact(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            });
        }

        /// <summary>Cork pops off and flies away (undo of a completion).</summary>
        public void PopCorkOff()
        {
            corked = false;
            if (!cork.gameObject.activeSelf) return;
            var rt = cork.rectTransform;
            var img = cork;
            Tween.Kill(rt);
            Tween.Kill(img);
            Tween.Move(rt, _corkHome + new Vector2(0.35f * UnitPx, 1.1f * UnitPx), 0.32f, Ease.OutQuad);
            Tween.Rotate(rt, -35f, 0.32f, Ease.OutQuad);
            Tween.Fade(img, 0f, 0.3f).SetDelay(0.05f).OnComplete(() =>
            {
                if (img == null) return;
                img.gameObject.SetActive(false);
                img.color = _corkColor;
                rt.localRotation = Quaternion.identity;
                rt.anchoredPosition = _corkHome;
            });
            AudioManager.Play(Sfx.Pop, 0.7f, 1.15f);
            Bounce(0.6f);
        }

        // ------------------------------------------------------------------------------------------- stone

        /// <summary>Shows/hides the stone at a counter value without animation.</summary>
        public void SnapLock(int remaining)
        {
            lockShown = Mathf.Max(0, remaining);
            if (lockShown <= 0)
            {
                if (_stoneRoot != null)
                {
                    KillStoneTweens();
                    _stoneRoot.gameObject.SetActive(false);
                }
                return;
            }
            EnsureStone();
            KillStoneTweens();
            _stoneRoot.gameObject.SetActive(true);
            _stoneRoot.localScale = Vector3.one;
            _stoneGroup.alpha = 1f;
            _stoneText.text = lockShown.ToString();
        }

        /// <summary>Counter goes down: the stone cracks / shakes and sheds a few chunks.</summary>
        public void StoneTick(int remaining)
        {
            if (remaining <= 0)
            {
                StoneBreak();
                return;
            }
            EnsureStone();
            KillStoneTweens();
            _stoneRoot.gameObject.SetActive(true);
            _stoneRoot.localScale = Vector3.one;
            _stoneGroup.alpha = 1f;
            lockShown = remaining;
            _stoneText.text = remaining.ToString();
            Tween.Shake(_stoneImage.rectTransform, 0.08f * UnitPx, 0.34f);
            Tween.Punch(_stoneBadge, 0.32f, 0.36f);
            float s = FxScale;
            FX.Burst(_fxSpace, FxPos(_stoneBadge.position), "stone_chunk", 5, ChunkColors, 420f * s, 0.6f, 26f * s, -1500f);
            AudioManager.Play(Sfx.StoneCrack);
            Haptics.Play(HapticType.Light);
        }

        /// <summary>Counter reached zero: the stone shatters into chunks and a puff of dust.</summary>
        public void StoneBreak()
        {
            lockShown = 0;
            if (_stoneRoot == null || !_stoneRoot.gameObject.activeSelf) return;
            KillStoneTweens();
            Vector2 p = FxPos(_stoneRoot.position);
            float s = FxScale;
            FX.Burst(_fxSpace, p, "stone_chunk", 14, ChunkColors, 760f * s, 0.8f, 36f * s, -1700f);
            FX.Burst(_fxSpace, p, "fx_poof", 5, PoofColors, 150f * s, 0.6f, 170f * s, 0f);
            FX.Ring(_fxSpace, p, new Color(1f, 0.96f, 0.88f, 0.8f), 320f * s);
            AudioManager.Play(Sfx.StoneBreak);
            Haptics.Play(HapticType.Medium);
            var root = _stoneRoot;
            var grp = _stoneGroup;
            Tween.Scale(root, 1.18f, 0.2f, Ease.OutQuad);
            Tween.Fade(grp, 0f, 0.2f).OnComplete(() =>
            {
                if (root == null) return;
                root.gameObject.SetActive(false);
                root.localScale = Vector3.one;
            });
            Bounce(0.8f);
        }

        /// <summary>Stone comes back (undo of the completion that broke it).</summary>
        public void StoneRestore(int remaining)
        {
            EnsureStone();
            KillStoneTweens();
            lockShown = Mathf.Max(1, remaining);
            _stoneText.text = lockShown.ToString();
            _stoneRoot.gameObject.SetActive(true);
            _stoneRoot.localScale = Vector3.one * 1.25f;
            _stoneGroup.alpha = 0f;
            Tween.Fade(_stoneGroup, 1f, 0.18f);
            Tween.Scale(_stoneRoot, 1f, 0.35f, Ease.OutBack);
            AudioManager.Play(Sfx.Pop, 0.6f, 0.85f);
        }

        /// <summary>Counter goes back up (undo) on a stone that is still there.</summary>
        public void StoneCountUp(int remaining)
        {
            EnsureStone();
            lockShown = remaining;
            _stoneText.text = remaining.ToString();
            Tween.Punch(_stoneBadge, 0.25f, 0.3f);
        }

        /// <summary>A tap on a stone bottle: small shake of the wrap.</summary>
        public void StoneNudge()
        {
            if (_stoneRoot == null || !_stoneRoot.gameObject.activeSelf) return;
            Tween.Shake(_stoneImage.rectTransform, 0.05f * UnitPx, 0.25f);
            Tween.Punch(_stoneBadge, 0.15f, 0.25f);
        }

        void KillStoneTweens()
        {
            if (_stoneRoot == null) return;
            Tween.Kill(_stoneRoot);
            Tween.Kill(_stoneGroup);
            Tween.Kill(_stoneBadge);
            if (_stoneImage != null)
            {
                Tween.Kill(_stoneImage.rectTransform);
                _stoneImage.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (_stoneBadge != null) _stoneBadge.localScale = Vector3.one;
        }

        void EnsureStone()
        {
            if (_stoneRoot != null) return;
            float u = UnitPx;
            _stoneRoot = Child("Stone", visual);
            _stoneRoot.anchoredPosition = CenterLocal;
            _stoneGroup = _stoneRoot.gameObject.AddComponent<CanvasGroup>();
            _stoneGroup.blocksRaycasts = false;
            _stoneGroup.interactable = false;

            // capsule covering the whole glass, lip included (its rounded ends must clear the lip corners)
            var sp = BottleArt.Stone;
            float h = shape.GlassHeight * u * 1.12f;
            float aspect = sp != null ? Mathf.Clamp(BottleArt.Aspect("stone_wrap", 0.35f), 0.2f, 1.5f) : 0.4f;
            float w = h * aspect, minW = shape.GlassWidth * u * 1.1f;
            if (w < minW)
            {
                w = minW;
                if (sp != null) h = w / aspect;
            }
            if (sp != null) _stoneImage = NewImage("Wrap", _stoneRoot, sp, Color.white, new Vector2(w, h), new Vector2(0.5f, 0.5f), Vector2.zero);
            else
            {
                _stoneImage = NewImage("Wrap", _stoneRoot, UISprites.Rounded, StoneTint, new Vector2(w, h), new Vector2(0.5f, 0.5f), Vector2.zero);
                Sliced(_stoneImage, 0.45f);
                var inner = NewImage("Inner", _stoneImage.rectTransform, UISprites.Rounded, new Color(1f, 1f, 1f, 0.18f),
                    new Vector2(w * 0.78f, h * 0.9f), new Vector2(0.5f, 0.5f), new Vector2(-w * 0.04f, h * 0.02f));
                inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                Sliced(inner, 0.55f);
            }

            float bd = 0.76f * u;
            _stoneBadge = Child("Badge", _stoneRoot);
            _stoneBadge.anchoredPosition = new Vector2(0f, 0.22f * u);
            NewImage("Ring", _stoneBadge, UISprites.Circle, Color.white, new Vector2(bd, bd), new Vector2(0.5f, 0.5f), Vector2.zero);
            NewImage("Disc", _stoneBadge, UISprites.Circle, DS.Colors.Ink, new Vector2(bd * 0.84f, bd * 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero);
            _stoneText = UIKit.Text(_stoneBadge, "", TextStyle.H2, new Vector2(bd, bd));
            if (_stoneText != null)
            {
                DS.Apply(_stoneText, TextStyle.H2, 0.5f * u);
                _stoneText.rectTransform.anchoredPosition = new Vector2(0f, 0.02f * u);
            }

            _adBadge = Child("Ad", _stoneRoot);
            _adBadge.anchoredPosition = new Vector2(0f, -0.66f * u);
            var pill = NewImage("Pill", _adBadge, UISprites.Rounded, DS.Colors.Accent, new Vector2(0.74f * u, 0.44f * u), new Vector2(0.5f, 0.5f), Vector2.zero);
            Sliced(pill, 1.3f);
            var icon = BottleArt.Optional("icon_video");
            if (icon != null)
            {
                var img = NewImage("Icon", _adBadge, icon, Color.white, new Vector2(0.48f * u, 0.36f * u), new Vector2(0.5f, 0.5f), Vector2.zero);
                img.preserveAspect = true;
            }
            else
            {
                var t = UIKit.Text(_adBadge, "AD", TextStyle.Badge, new Vector2(0.7f * u, 0.4f * u));
                if (t != null) DS.Apply(t, TextStyle.Badge, 0.3f * u);
            }
        }

        // ------------------------------------------------------------------------------------------- liquid effects

        /// <summary>Hidden layer flips to its color (smooth color change, "?" squashes away, sparkles).</summary>
        public void FlipLayer(int layer, float delay, bool sound)
        {
            var lq = liquid;
            int version = lq.Version;
            Tween.Value(0f, 1f, 0.34f, v =>
            {
                if (lq == null || lq.Version != version || layer < 0 || layer >= lq.Layers.Count) return;
                var l = lq.Layers[layer];
                l.reveal = v;
                lq.SetLayer(layer, l);
            }, Ease.InOutQuad).SetDelay(delay).SetLink(lq);
            Tween.Delay(delay, () =>
            {
                if (lq == null) return;
                Sparkle(layer, 5);
                if (sound) AudioManager.Play(Sfx.Reveal, 0.85f);
            }).SetLink(lq);
        }

        /// <summary>Small sparkle burst at a liquid layer's center.</summary>
        public void Sparkle(int layer, int count)
        {
            if (liquid == null) return;
            Vector3 world;
            if (liquid.TryGetLayerCenter(layer, out var c, out _)) world = liquid.rectTransform.TransformPoint(c);
            else world = CenterWorld;
            float s = FxScale;
            Vector2 p = FxPos(world);
            FX.Burst(_fxSpace, p, "fx_sparkle", count, SparkColors, 170f * s, 0.5f, 36f * s, 0f);
            FX.Ring(_fxSpace, p, new Color(1f, 1f, 1f, 0.55f), 110f * s);
        }

        // ------------------------------------------------------------------------------------------- per frame

        /// <summary>Positions the "?" glyphs on their hidden layers (after the liquid geometry was rebuilt).</summary>
        public void UpdateMarks()
        {
            if (liquid == null || marks == null) return;
            var layers = liquid.Layers;
            int used = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (!l.hidden || l.reveal >= 0.5f || l.amount < 0.04f) continue;
                if (!liquid.TryGetLayerCenter(i, out var c, out float thick)) continue;
                var t = Mark(used++);
                if (t == null) break;
                var rt = t.rectTransform;
                rt.anchoredPosition = c;
                float k = Mathf.Clamp(thick / (0.62f * UnitPx), 0.3f, 1f) * Mathf.Clamp01(l.amount * 2f);
                float flip = Mathf.Clamp01(1f - 2f * Mathf.Max(0f, l.reveal));
                rt.localScale = new Vector3(k * flip, k, 1f);
                rt.rotation = Quaternion.identity;
            }
            for (int i = used; i < _marks.Count; i++)
                if (_marks[i] != null && _marks[i].gameObject.activeSelf) _marks[i].gameObject.SetActive(false);
        }

        TMP_Text Mark(int i)
        {
            while (_marks.Count <= i)
            {
                var t = UIKit.Text(marks, "?", TextStyle.H2, new Vector2(0.8f * UnitPx, 0.8f * UnitPx));
                if (t == null) return null;
                DS.Apply(t, TextStyle.H2, 0.5f * UnitPx);
                t.name = "Mark";
                _marks.Add(t);
            }
            var m = _marks[i];
            if (m != null && !m.gameObject.activeSelf) m.gameObject.SetActive(true);
            return m;
        }

        /// <summary>Draws the incoming stream from <see cref="streamTopWorld"/> down to the liquid surface.</summary>
        public void UpdateStream(float dt)
        {
            if (stream == null) return;
            if (!streamOn || streamWidth <= 0.01f || streamHead <= streamTail)
            {
                if (stream.IsOn) stream.Hide();
                return;
            }
            _streamPhase += dt;
            Vector3 t3 = visual.InverseTransformPoint(streamTopWorld);
            var top = new Vector2(t3.x, t3.y);
            float y = Mathf.Max(liquid.SurfaceYAt(top.x), 0.03f * UnitPx);
            if (y > top.y) y = top.y;
            var bottom = new Vector2(top.x, y);
            streamLanding = bottom;
            Vector2 a = Vector2.LerpUnclamped(top, bottom, Mathf.Clamp01(streamTail));
            Vector2 b = Vector2.LerpUnclamped(top, bottom, Mathf.Clamp01(streamHead));
            int c = streamColor;
            stream.Set(a, b, StreamWidthUnits * UnitPx * streamWidth, Liquids.Color(c), Liquids.Light(c), Liquids.Dark(c), _streamPhase);
        }
    }
}
