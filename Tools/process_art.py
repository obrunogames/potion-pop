#!/usr/bin/env python3
"""Turn the raw Codex images (ArtSource/raw) into game-ready sprites for Potion Pop!.

    uv run --with pillow --with numpy --with scipy python Tools/process_art.py [--only prefix1,prefix2]

Pipeline per raw image (see rule_for() for the per-group sizes):
  1. images that should be transparent but came back opaque get their background removed (corner-color flood fill);
  2. alpha cleanup: alpha < 12 -> 0, alpha >= 248 -> 255 (the generator writes "opaque" as 253); glow sprites
     (sunburst, sparkle, bubble) roll their faint halo off smoothly instead of cutting it (no visible contour);
  3. defringe: semi-transparent edge pixels take the color of the nearest solid pixel in proportion to their
     transparency (removes light/dark matte halos while keeping the antialiasing);
  4. trim transparent borders, resize with LANCZOS (premultiplied), add a 2 px transparent pad;
  5. transparent pixels get the color of the nearest visible pixel (no dark fringes with bilinear filtering / mips).

Procedural sprites (no raw image):
  * ui_*      white helpers tinted in Unity (rounded rect, capsule, circle, soft shadow, glow, ring, ...);
  * bottle_*  the potion bottle glass (back / front / glow / mask on ONE shared canvas + a soft contact shadow), drawn
              from the parametric silhouette in Resources/bottle_shape.json -- the same outline the game uses to build
              the liquid mesh -- so glass and liquid always line up. The canvas size and the pixel position of the
              shape origin are written back into the "sprite" block of bottle_shape.json.
              Re-run with --only bottle after changing the shape parameters.
Brand assets (ArtSource/brand/brand_*.png, e.g. the official Google "G" of the sign-in button) are copied unchanged.

Outputs:
  Assets/_Game/Resources/Art/<name>.png          flat folder, one sprite per raw image + procedural sprites + brand
  Assets/_Game/Resources/Art/art_index.json      {"sprites":[{"name","w","h","border":[l,b,r,t],"pivot":[x,y],
                                                  "inner":[x,y,w,h]}]}  (border in output pixels, Unity order
                                                  left/bottom/right/top; inner normalized, origin bottom-left)
  Assets/_Game/Resources/bottle_shape.json       "sprite" block only (everything else is left untouched)
  Assets/_Game/Art/AppIcon/app_icon.png          1024x1024 opaque app icon
  ArtSource/preview/processed.png                contact sheet of every processed sprite on a checkerboard
  ArtSource/preview/nineslice.png                every 9-sliced sprite stretched to other aspect ratios
  ArtSource/preview/bottles.png                  glass + flat liquid bands over three game backgrounds, and a zoomed
                                                  alignment check (magenta = the interior polygon of the liquid)
A full run (no --only) also deletes sprites in Resources/Art that nothing produces anymore, with their .meta files.
"""
import argparse
import json
import math
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
from art_manifest import ASSETS  # noqa: E402

RAW = os.path.join(ROOT, "ArtSource", "raw")
BRAND = os.path.join(ROOT, "ArtSource", "brand")
PREVIEW = os.path.join(ROOT, "ArtSource", "preview")
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "Art")
APP_ICON_DIR = os.path.join(ROOT, "Assets", "_Game", "Art", "AppIcon")
INDEX = os.path.join(OUT, "art_index.json")
BOTTLE_JSON = os.path.join(ROOT, "Assets", "_Game", "Resources", "bottle_shape.json")

ALPHA_CUT = 12        # alpha below this is treated as fully transparent (and trimmed)
ALPHA_SOLID = 248     # alpha at/above this becomes 255
GLOW_FLOOR = 2        # glow sprites: alpha at/below this is noise (0); between it and ALPHA_CUT a smooth roll-off
PAD = 2               # transparent pad (output pixels) around trimmed sprites

FULL_RECT = [0.0, 0.0, 1.0, 1.0]
CENTER = [0.5, 0.5]


# ============================================================================================ rules

class Rule:
    """How one sprite is processed.

    fit: ("long", n)   longest side = n          ("w", n) width = n        ("h", n) height = n
         ("exact", w, h) exact size (no trim)    ("keep",) keep raw size (backgrounds, no trim)
    trim: "bbox" (alpha bounding box), "center" (symmetric around the image center: keeps rotation pivots),
          "square" (bbox, then padded to a square around its center) or None.
    glow: mostly semi-transparent by design (light rays, sparkles, soap bubble): soft alpha roll-off, no defringe.
    """

    def __init__(self, fit, trim="bbox", glow=False, opaque=False):
        self.fit, self.trim, self.glow, self.opaque = fit, trim, glow, opaque


def rule_for(name):
    # ---- worlds and branding
    if name.startswith(("home_", "gamebg_")):
        return Rule(("exact", 1024, 1536), trim=None, opaque=True)
    if name == "app_icon":
        return Rule(("exact", 256, 256), trim=None, opaque=True)   # small in-game copy; full size goes to AppIcon/
    if name == "logo":
        return Rule(("w", 1024))
    # ---- cards: the shared portrait card chrome keeps its width; collection card art is fitted by its long side
    if name in ("card_back", "card_frame"):
        return Rule(("w", 384))
    if name.startswith("card_"):
        return Rule(("long", 384))
    # ---- gameplay
    if name == "cork":
        return Rule(("long", 256))
    if name == "stone_wrap":
        return Rule(("h", 768))
    if name == "stone_chunk":
        return Rule(("long", 128))
    if name == "bottle_rack":
        return Rule(("w", 1024))
    if name == "fx_bubble":
        return Rule(("long", 128), trim="center", glow=True)
    if name == "fx_splash":
        return Rule(("long", 256))
    # ---- shared UI kit (Shelf Pop! rules)
    if name in ("btn_square", "btn_round_close"):
        return Rule(("long", 256))
    if name.startswith("btn_"):
        return Rule(("w", 768))
    if name.startswith(("icon_", "booster_", "chest_", "coins_")) or name == "hearts_refill":
        return Rule(("long", 256))
    if name.startswith("avatar_"):
        return Rule(("long", 256))
    if name.startswith("mascot_"):
        return Rule(("h", 768))
    if name.startswith("panel_"):
        return Rule(("long", 512))
    if name in ("pill_counter", "progress_track", "progress_fill", "store_sign"):
        return Rule(("w", 768))
    if name == "ribbon_title":
        return Rule(("w", 1024))
    if name == "spin_wheel":
        return Rule(("long", 768), trim="square")
    if name == "spin_pointer":
        return Rule(("h", 256))
    if name == "sunburst":
        return Rule(("long", 512), trim="center", glow=True)
    if name == "fx_sparkle":
        return Rule(("long", 128), trim="center", glow=True)
    if name == "fx_poof":
        return Rule(("long", 256), trim="center")
    if name == "hand_pointer":
        return Rule(("long", 256))
    print(f"  ? no rule for {name}, using long side 512")
    return Rule(("long", 512))


# ============================================================================================ pixel helpers

def to_array(im):
    return np.asarray(im.convert("RGBA"), dtype=np.float32).copy()


def to_image(arr):
    return Image.fromarray(np.clip(np.rint(arr), 0, 255).astype(np.uint8), "RGBA")


def ensure_alpha(arr, name):
    """Background removal for images that should be transparent but came back opaque.

    The background color is the median of the four corners; every pixel close to it that is connected to the image
    border becomes transparent, with a soft ramp on the boundary."""
    if arr[..., 3].min() < 250:
        return arr
    print(f"  ! {name}: no transparency, removing the background by corner flood fill")
    rgb = arr[..., :3]
    k = 8
    corners = np.concatenate([rgb[:k, :k].reshape(-1, 3), rgb[:k, -k:].reshape(-1, 3),
                              rgb[-k:, :k].reshape(-1, 3), rgb[-k:, -k:].reshape(-1, 3)])
    bg = np.median(corners, axis=0)
    dist = np.sqrt(((rgb - bg) ** 2).sum(-1))
    t0, t1 = 18.0, 46.0
    cand = dist < t1
    labels, _ = ndimage.label(cand)
    border = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    border = border[border > 0]
    region = np.isin(labels, border)
    soft = np.clip((dist - t0) / (t1 - t0), 0, 1)
    arr[..., 3] = np.where(region, soft * 255, arr[..., 3])
    return arr


def clean_alpha(arr, soft=False):
    """alpha < ALPHA_CUT -> 0 and alpha >= ALPHA_SOLID -> 255.

    soft (glows: light rays, sparkles, bubble): the faint outer halo spans thousands of pixels with alpha 1..11, so a
    hard cut would draw a visible jagged contour on dark backgrounds (e.g. the sunburst over the popup dim). There
    the tail rolls off smoothly instead: a * smoothstep(GLOW_FLOOR, ALPHA_CUT, a) -- continuous (value and slope) at
    ALPHA_CUT and exactly 0 at/below GLOW_FLOOR, so trimming still finds a tight box."""
    a = arr[..., 3]
    if soft:
        lo = a < ALPHA_CUT
        t = np.clip((a[lo] - GLOW_FLOOR) / float(ALPHA_CUT - GLOW_FLOOR), 0.0, 1.0)
        a[lo] = a[lo] * t * t * (3.0 - 2.0 * t)
    else:
        a[a < ALPHA_CUT] = 0
    a[a >= ALPHA_SOLID] = 255
    return arr


def defringe(arr, reach=3):
    """Semi-transparent pixels near the silhouette move toward the nearest solid color, weighted by transparency.

    A matte halo contributes (1 - alpha) * matte to an edge pixel, so blending with weight (1 - alpha) toward the
    interior color removes it while nearly opaque edge pixels keep their own antialiased color."""
    a = arr[..., 3]
    solid = a >= 250
    if not solid.any():
        return arr
    semi = (a > 0) & ~solid
    d, (iy, ix) = ndimage.distance_transform_edt(~solid, return_indices=True)
    m = semi & (d <= reach)
    if m.any():
        w = (1.0 - a[m] / 255.0)[:, None]
        arr[m, :3] = arr[m, :3] * (1 - w) + arr[iy[m], ix[m], :3] * w
    return arr


def bleed(arr):
    """Fully transparent pixels take the RGB of the nearest visible pixel (avoids dark edges when filtered)."""
    a = arr[..., 3]
    vis = a > 0
    if not vis.any() or vis.all():
        return arr
    _, (iy, ix) = ndimage.distance_transform_edt(~vis, return_indices=True)
    inv = ~vis
    arr[inv, :3] = arr[iy[inv], ix[inv], :3]
    return arr


def crop_box(arr, mode):
    H, W = arr.shape[:2]
    if mode is None:
        return (0, 0, W, H)
    ys, xs = np.nonzero(arr[..., 3] > 0)
    if len(xs) == 0:
        return (0, 0, W, H)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    if mode == "center":
        hw = max(W / 2 - x0, x1 - W / 2)
        hh = max(H / 2 - y0, y1 - H / 2)
        x0, x1 = int(math.floor(W / 2 - hw)), int(math.ceil(W / 2 + hw))
        y0, y1 = int(math.floor(H / 2 - hh)), int(math.ceil(H / 2 + hh))
    elif mode == "square":
        cx, cy, half = (x0 + x1) / 2, (y0 + y1) / 2, max(x1 - x0, y1 - y0) / 2
        x0, x1 = int(math.floor(cx - half)), int(math.ceil(cx + half))
        y0, y1 = int(math.floor(cy - half)), int(math.ceil(cy + half))
    return (x0, y0, x1, y1)


def crop_padded(arr, box):
    """Crop that may extend outside the image (filled with transparent)."""
    x0, y0, x1, y1 = box
    H, W = arr.shape[:2]
    out = np.zeros((y1 - y0, x1 - x0, 4), np.float32)
    sx0, sy0, sx1, sy1 = max(x0, 0), max(y0, 0), min(x1, W), min(y1, H)
    out[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = arr[sy0:sy1, sx0:sx1]
    return out


def fit_image(arr, rule):
    """Trim + resize + pad according to the rule. Returns the output array."""
    box = crop_box(arr, rule.trim)
    src = crop_padded(arr, box)
    sh, sw = src.shape[:2]
    kind = rule.fit[0]
    if kind == "exact":
        tw, th = rule.fit[1], rule.fit[2]
        return to_array(to_image(src).resize((tw, th), Image.LANCZOS))
    if kind == "keep":
        return src
    pad = PAD if rule.trim else 0
    n = rule.fit[1]
    if kind == "long":
        s = (n - 2 * pad) / max(sw, sh)
    elif kind == "w":
        s = (n - 2 * pad) / sw
    else:
        s = (n - 2 * pad) / sh
    if s > 1.0:
        print(f"    note: upscaling x{s:.2f}")
    iw, ih = max(1, round(sw * s)), max(1, round(sh * s))
    if rule.trim == "square":
        iw = ih = max(iw, ih)
    im = to_image(src).resize((iw, ih), Image.LANCZOS)
    out = np.zeros((ih + 2 * pad, iw + 2 * pad, 4), np.float32)
    out[pad:pad + ih, pad:pad + iw] = to_array(im)
    return out


# ============================================================================================ measurements

def corner_radii(mask):
    """Corner radius of a rounded-rect mask at each corner (tl, tr, bl, br), measured along the 45 degree diagonal.

    For a circular corner of radius r the boundary crosses the diagonal at an inset t = r * (1 - 1/sqrt(2))."""
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return (0, 0, 0, 0)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    k = 1.0 - 1.0 / math.sqrt(2.0)
    out = []
    for (cx, cy, dx, dy) in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
        t = 0
        lim = min(x1 - x0, y1 - y0) // 2
        while t < lim and not mask[cy + dy * t, cx + dx * t]:
            t += 1
        out.append(t / k)
    return tuple(out)


def region_from_center(arr, tol, ref_patch=0.06, seed=None):
    """Connected region of pixels similar to the color around `seed` (default: the image center), e.g. the cream
    inside of a frame. seed = (x, y) in pixels."""
    H, W = arr.shape[:2]
    sx, sy = (W // 2, H // 2) if seed is None else (int(seed[0]), int(seed[1]))
    rgb = arr[..., :3]
    py, px = max(2, int(H * ref_patch)), max(2, int(W * ref_patch))
    ref = np.median(rgb[max(sy - py, 0):sy + py, max(sx - px, 0):sx + px].reshape(-1, 3), axis=0)
    dist = np.sqrt(((rgb - ref) ** 2).sum(-1))
    cand = (dist < tol) & (arr[..., 3] > 200)
    cand = ndimage.binary_opening(cand, iterations=2)
    labels, _ = ndimage.label(cand)
    lab = labels[sy, sx]
    if lab == 0:
        return None
    region = ndimage.binary_fill_holes(labels == lab)
    return region


def body_rows(alpha, frac=0.6):
    """First/last row where the opaque mask covers most of its widest row: the panel body without parts that hang
    above or below it (chains of store_sign)."""
    cov = alpha.mean(1)
    rows = np.nonzero(cov >= frac * cov.max())[0]
    return int(rows.min()), int(rows.max())


def bbox(mask):
    ys, xs = np.nonzero(mask)
    return xs.min(), xs.max(), ys.min(), ys.max()


def frame_borders(arr, tol, margin=2, use_outer=True, seed=None):
    """9-slice borders of a framed sprite: distance to the inner region + the inner corner radius on each side,
    never less than the outer corner radius (use_outer=False for shapes with protrusions such as the star on top
    of card_frame or the chains of store_sign, whose alpha bbox corners are not the panel corners).
    seed: a pixel inside the inner region when the image center is not (store_sign: chains above the body).
    Returns ([l, b, r, t], inner rect normalized) or (None, None)."""
    H, W = arr.shape[:2]
    region = region_from_center(arr, tol, seed=seed)
    if region is None:
        return None, None
    ix0, ix1, iy0, iy1 = bbox(region)
    rtl, rtr, rbl, rbr = corner_radii(region)
    alpha = arr[..., 3] > 128
    otl, otr, obl, obr = corner_radii(alpha) if use_outer else (0, 0, 0, 0)
    left = max(ix0 + max(rtl, rbl), max(otl, obl) + PAD)
    right = max((W - 1 - ix1) + max(rtr, rbr), max(otr, obr) + PAD)
    top = max(iy0 + max(rtl, rtr), max(otl, otr) + PAD)
    bottom = max((H - 1 - iy1) + max(rbl, rbr), max(obl, obr) + PAD)
    border = [int(round(left + margin)), int(round(bottom + margin)), int(round(right + margin)), int(round(top + margin))]
    inner = [ix0 / W, (H - 1 - iy1) / H, (ix1 - ix0 + 1) / W, (iy1 - iy0 + 1) / H]
    return border, inner


def clamp_border(border, lo, hi, W, H, label):
    """Clamp each side to [lo, hi] fraction of the relevant dimension (sanity net around the measurement)."""
    l, b, r, t = border
    out = [
        int(min(max(l, lo * W), hi * W)), int(min(max(b, lo * H), hi * H)),
        int(min(max(r, lo * W), hi * W)), int(min(max(t, lo * H), hi * H)),
    ]
    if out != border:
        print(f"    {label}: measured border {border} clamped to {out}")
    return out


def check_border(name, border, w, h):
    """Warns about 9-slice borders Unity cannot use as measured (no stretchable band: the importer then squeezes them)
    or that SliceFit (UI/Framework/UISprites.FitSlices) would shrink because a side exceeds half the sprite."""
    l, b, r, t = border
    if not any(border):
        return
    if l + r >= w or b + t >= h:
        print(f"  ! {name}: border {border} leaves no stretchable band in {w}x{h}")
    elif max(l, r) > w / 2 or max(b, t) > h / 2:
        print(f"  ! {name}: border {border} has a side larger than half of {w}x{h} (SliceFit will shrink it)")


def nine_slice_info(name, arr):
    """Returns (border [l, b, r, t] in output pixels, inner rect) for sprites that are 9-sliced in Unity."""
    H, W = arr.shape[:2]
    if name.startswith("btn_") and name not in ("btn_square", "btn_round_close"):
        # Pills: the round caps are half the height; the glossy highlight / 3D edge need most of the height.
        return [round(H * 0.5), round(H * 0.45), round(H * 0.5), round(H * 0.45)], FULL_RECT
    if name == "btn_square":
        alpha = arr[..., 3] > 128
        r = max(corner_radii(alpha))
        side = max(round(0.30 * W), int(r + PAD + 2))
        sideh = max(round(0.30 * H), int(r + PAD + 2))
        print(f"    btn_square outer corner radius {r:.0f}px -> border {side}")
        return [side, sideh, side, sideh], FULL_RECT
    if name in ("pill_counter", "progress_track", "progress_fill"):
        return [round(H / 2), 0, round(H / 2), 0], FULL_RECT
    if name == "panel_popup":
        border, inner = frame_borders(arr, tol=38)
        if border is None:
            return [round(0.2 * W)] * 4, FULL_RECT
        print(f"    panel_popup measured border {border} ({border[0] / W:.1%} of width)")
        return clamp_border(border, 0.15, 0.26, W, H, name), inner
    if name == "panel_card":
        border, inner = frame_borders(arr, tol=22)
        if border is None:
            return [round(0.16 * W)] * 4, FULL_RECT
        print(f"    panel_card measured border {border} ({border[0] / W:.1%} of width)")
        return clamp_border(border, 0.12, 0.24, W, H, name), inner
    if name == "panel_inset":
        # The recessed slot has an inner shadow along the top and a highlight along the bottom: per spec, most of
        # the height goes to the borders (45% of the height laterally, 40% vertically).
        alpha = arr[..., 3] > 128
        r = max(corner_radii(alpha))
        lat = max(round(0.45 * H), int(r + PAD + 2))
        ver = round(0.40 * H)
        region = region_from_center(arr, tol=20)
        inner = FULL_RECT
        if region is not None:
            ix0, ix1, iy0, iy1 = bbox(region)
            inner = [ix0 / W, (H - 1 - iy1) / H, (ix1 - ix0 + 1) / W, (iy1 - iy0 + 1) / H]
        return [lat, ver, lat, ver], inner
    if name == "store_sign":
        # The two hanging chains put the image center on the top rim, so the cream label area is seeded at the
        # center of the sign body. The lateral borders must contain the chains (and their mounting plates) so they
        # sit in the unscaled corner slices: otherwise a wider sign stretches the chain links horizontally.
        alpha = arr[..., 3] > 128
        by0, by1 = body_rows(alpha)
        border, inner = frame_borders(arr, tol=40, use_outer=False, seed=(W // 2, (by0 + by1) // 2))
        if border is None:
            print("    store_sign: label area not found, using fallback borders")
            border, inner = [round(0.27 * W), round(0.26 * H), round(0.27 * W), round(0.64 * H)], FULL_RECT
        cov = alpha.mean(1)
        hanging = np.nonzero(cov[:by0] < 0.35)[0]               # rows above the body that only hold the chains
        cols = np.nonzero(alpha[hanging].any(0))[0] if len(hanging) else np.array([], int)
        left_cols, right_cols = cols[cols < W / 2], cols[cols >= W / 2]
        need_l = int(left_cols.max()) + 1 + 8 if len(left_cols) else 0
        need_r = W - int(right_cols.min()) + 8 if len(right_cols) else 0
        print(f"    store_sign measured border {border}, chains need l/r {need_l}/{need_r}")
        border[0] = max(border[0], need_l, round(0.20 * W))
        border[2] = max(border[2], need_r, round(0.20 * W))
        border[1] = max(border[1], H - by1 + 2)                  # never cut through the bottom rim
        border[3] = max(border[3], by0 + 2)                      # chains always in the top slices
        # The Potion Pop! sign has its ribbon and chains above a short label with round ends, so "label top + corner
        # radius" can exceed half the height. Unity needs a stretchable band and SliceFit shrinks every border as
        # soon as one is larger than half the rect, so each side is capped at half the sprite (the sign is drawn at
        # its native aspect: the band only has to exist; chains and rims stay inside the capped slices).
        half_w, half_h = (W - 1) // 2, (H - 1) // 2
        capped = [min(border[0], half_w), min(border[1], half_h), min(border[2], half_w), min(border[3], half_h)]
        if capped != border:
            print(f"    store_sign: border {border} capped to half the sprite -> {capped}")
        return capped, inner
    if name == "card_frame":
        border, inner = frame_borders(arr, tol=26, use_outer=False)
        if border is None:
            return [round(0.22 * W)] * 4, FULL_RECT
        print(f"    card_frame measured border {border} ({border[0] / W:.1%} of width)")
        return clamp_border(border, 0.12, 0.30, W, H, name), inner
    return [0, 0, 0, 0], FULL_RECT


# ============================================================================================ procedural primitives

SS = 4  # supersampling factor


def supersample(w, h, fn):
    """Evaluates coverage fn(x, y) (pixel units of the final image, arrays) on a SSxSS grid per pixel, box-filters."""
    ys, xs = np.mgrid[0:h * SS, 0:w * SS].astype(np.float64)
    xs = (xs + 0.5) / SS
    ys = (ys + 0.5) / SS
    cov = fn(xs, ys)
    return cov.reshape(h, SS, w, SS).mean(axis=(1, 3))


def sd_round_rect(x, y, x0, y0, x1, y1, r):
    """Signed distance to a rounded rectangle (negative inside)."""
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    qx = np.abs(x - cx) - hx
    qy = np.abs(y - cy) - hy
    outside = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2)
    inside = np.minimum(np.maximum(qx, qy), 0)
    return outside + inside - r


def white(alpha):
    h, w = alpha.shape
    arr = np.full((h, w, 4), 255.0, np.float32)
    arr[..., 3] = np.clip(alpha, 0, 1) * 255.0
    return arr


def erfc(x):
    # Abramowitz-Stegun 7.1.26 approximation (max error 1.5e-7), vectorized; avoids a scipy.special dependency.
    z = np.abs(x)
    t = 1.0 / (1.0 + 0.3275911 * z)
    poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))))
    r = poly * np.exp(-z * z)
    return np.where(x >= 0, r, 2.0 - r)


def star_points(cx, cy, r_out, r_in, n=5, rot=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = rot + i * math.pi / n
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def polygon_coverage(w, h, pts):
    """Antialiased polygon via PIL at SS x resolution, box-downsampled."""
    big = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(big).polygon([(x * SS, y * SS) for x, y in pts], fill=255)
    a = np.asarray(big, np.float64) / 255.0
    return a.reshape(h, SS, w, SS).mean(axis=(1, 3))


def make_primitives():
    """White procedural UI sprites (tinted in Unity with Image.color). Returns {name: (array, border)}."""
    prims = {}

    # 9-slice white rounded rect: radius 40, border 44.
    cov = supersample(128, 128, lambda x, y: np.clip(0.5 - sd_round_rect(x, y, 0, 0, 128, 128, 40), 0, 1))
    prims["ui_rounded"] = (white(cov), [44, 44, 44, 44])

    # Capsule 256x128 (round caps radius 64), sliced horizontally only.
    cov = supersample(256, 128, lambda x, y: np.clip(0.5 - sd_round_rect(x, y, 0, 0, 256, 128, 64), 0, 1))
    prims["ui_capsule"] = (white(cov), [64, 0, 64, 0])

    # Circle 256.
    cov = supersample(256, 256, lambda x, y: np.clip(127.0 - np.hypot(x - 128, y - 128) + 0.5, 0, 1))
    prims["ui_circle"] = (white(cov), [0, 0, 0, 0])

    # Soft shadow: Gaussian-edged rounded rect. Corner radius = 96 - edge inset, so beyond 96 px from any side the
    # profile only depends on the distance to the straight edge: the 9-slice border of 96 is exact.
    edge, sigma = 52.0, 14.0

    def shadow(x, y):
        d = sd_round_rect(x, y, edge, edge, 256 - edge, 256 - edge, 96 - edge)
        return 0.5 * erfc(d / (sigma * math.sqrt(2)))
    a = supersample(256, 256, shadow)
    a = np.where(a < 0.002, 0, a)
    prims["ui_soft_shadow"] = (white(a), [96, 96, 96, 96])

    # Radial glow: smooth falloff reaching exactly 0 at the edge.
    def glow(x, y):
        r = np.hypot(x - 128, y - 128) / 128.0
        k = 3.2
        v = (np.exp(-k * r * r) - math.exp(-k)) / (1 - math.exp(-k))
        return np.clip(v, 0, 1) ** 1.2
    prims["ui_glow"] = (white(supersample(256, 256, glow)), [0, 0, 0, 0])

    # Soft ring (shockwave): Gaussian profile around 74% of the radius, faded to 0 at the edge.
    def ring(x, y):
        r = np.hypot(x - 128, y - 128) / 128.0
        v = np.exp(-((r - 0.74) / 0.085) ** 2)
        return v * np.clip((1.0 - r) / 0.12, 0, 1)
    prims["ui_ring"] = (white(supersample(256, 256, ring)), [0, 0, 0, 0])

    prims["ui_pixel"] = (white(np.ones((8, 8))), [0, 0, 0, 0])

    # Vertical gradient: opaque at the top row, transparent at the bottom row.
    g = np.repeat(np.linspace(1.0, 0.0, 256)[:, None], 8, axis=1)
    prims["ui_gradient_v"] = (white(g), [0, 0, 0, 0])

    # Confetti piece 48x24.
    cov = supersample(48, 24, lambda x, y: np.clip(0.5 - sd_round_rect(x, y, 1, 1, 47, 23, 6), 0, 1))
    prims["ui_confetti"] = (white(cov), [0, 0, 0, 0])

    # Five-point star 96 (chubby: inner radius 48% of the outer), slightly rounded by a small blur.
    pts = star_points(48, 50.5, 46, 22)
    cov = polygon_coverage(96, 96, pts)
    cov = ndimage.gaussian_filter(cov, 0.6)
    prims["ui_star_small"] = (white(np.clip(cov * 1.08, 0, 1)), [0, 0, 0, 0])

    # Vignette 512: transparent center -> dark edges (black, so it darkens whatever is below; tune with alpha).
    def vig(x, y):
        u = (x - 256) / 256.0
        v = (y - 256) / 256.0
        r = np.sqrt(u * u + v * v)                     # 1.0 at the edge midpoints, 1.41 in the corners
        t = np.clip((r - 0.45) / (1.25 - 0.45), 0, 1)
        return t * t * (3 - 2 * t)
    a = supersample(512, 512, vig)
    arr = np.zeros((512, 512, 4), np.float32)
    arr[..., 3] = a * 255.0
    prims["ui_vignette"] = (arr, [0, 0, 0, 0])

    # Four-point sparkle 64: concave astroid-like star (|x|^p + |y|^p <= 1, p = 0.55) plus a faint round glow.
    def sparkle(x, y):
        u = np.abs(x - 32) / 31.0
        v = np.abs(y - 32) / 31.0
        p = 0.55
        f = (u ** p + v ** p)
        core = np.clip((1.0 - f) * 9.0, 0, 1)
        halo = np.clip(1 - np.hypot(u, v) / 0.55, 0, 1) ** 2 * 0.45
        return np.maximum(core, halo)
    prims["ui_sparkle_small"] = (white(supersample(64, 64, sparkle)), [0, 0, 0, 0])
    return prims


# ============================================================================================ procedural bottle glass
#
# The potion bottle is drawn from the parametric silhouette of Resources/bottle_shape.json (shape units: the inner
# body width is 1.0, y = 0 is the inner bottom, y grows upward, x = 0 is the axis). Interior outline:
#   body      x in [-w/2, w/2] from y = 0 to bodyHeight; both bottom corners are quarter circles of radius
#             bottomRadius centered at (+-(w/2 - bottomRadius), bottomRadius);
#   shoulder  from (+-w/2, bodyHeight) to (+-neckWidth/2, bodyHeight + shoulderHeight) along a quadratic Bezier whose
#             control point is the corner (+-w/2, bodyHeight + shoulderHeight) (starts vertical, ends horizontal);
#   neck      x in [-neckWidth/2, neckWidth/2] up to the mouth at y = bodyHeight + shoulderHeight + neckHeight.
# The glass wall is that outline offset outward by `glass`; the lip is a rounded rect lipWidth x lipHeight whose
# vertical center is the mouth. Everything is rendered at pixelsPerUnit with SSxSS supersampling.
#
# Sprites (all but the shadow share ONE canvas and ONE origin, so the game stacks them in a single RectTransform):
#   bottle_back    behind the liquid: faint cool tint of the interior, darker toward the walls, back-wall highlight;
#   bottle_front   over the liquid: the glass walls with a dark outer edge, specular streaks, bottom crescent,
#                  shoulder/neck highlights and the glossy lip (the interior stays mostly transparent);
#   bottle_glow    white halo around the outer outline (selection; tint it), transparent over the interior;
#   bottle_mask    the interior silhouette, opaque white (stencil / clipping of the liquid);
#   bottle_shadow  soft contact shadow, its own small canvas; center at shape (0, -glass) = the outer bottom of the
#                  glass, at the same pixels-per-unit (placement written into bottle_shape.json "sprite").

BOTTLE_STACK = ("bottle_back", "bottle_front", "bottle_glow", "bottle_mask")    # shared canvas + origin
BOTTLE_SPRITES = BOTTLE_STACK + ("bottle_shadow",)
INK = (59, 31, 92)              # DS.Colors.Ink (#3B1F5C): the outline color of the whole UI kit
WHITE = (255, 255, 255)
GLOW_WIDTH = 0.12               # selection glow reach beyond the outer outline (shape units)
OUTLINE_WIDTH = 0.017           # dark outer edge of the glass (shape units: 2.7 px at 160 px/unit)
NECK_FILLET = 0.05              # outer fillet where the shoulder meets the neck (the interior corner stays sharp)
LIGHT = (-0.55, 0.835)          # light from the upper left, like every painted asset
SHADOW_RX, SHADOW_RY = 0.66, 0.13   # contact shadow ellipse (shape units, outer extent of the soft falloff)
LIQUID_PREVIEW = {               # Scripts/Core/Liquids.cs palette (preview only)
    "red": (0xFF, 0x3B, 0x5C), "blue": (0x2F, 0x6B, 0xFF), "yellow": (0xFF, 0xD1, 0x2E), "green": (0x22, 0xC5, 0x5E),
    "purple": (0x9B, 0x4D, 0xFF), "orange": (0xFF, 0x8A, 0x1E), "pink": (0xFF, 0x6F, 0xD0), "sky": (0x38, 0xD4, 0xFF),
    "lime": (0xB8, 0xF0, 0x3C), "brown": (0x9A, 0x5A, 0x32), "white": (0xF4, 0xF0, 0xFF), "teal": (0x14, 0xB8, 0xA6),
    "mystery": (0x8F, 0x88, 0xA8),
}


def smooth(e0, e1, x):
    """Hermite smoothstep from e0 to e1 (e1 < e0 gives a falling edge)."""
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


class BottleShape:
    """The parametric bottle of bottle_shape.json, in shape units (same math as the game's liquid mesh)."""

    def __init__(self, data):
        self.hw = data["innerWidth"] / 2.0
        self.r = data["bottomRadius"]
        self.body = data["bodyHeight"]
        self.sh = data["shoulderHeight"]
        self.nhw = data["neckWidth"] / 2.0
        self.neck = data["neckHeight"]
        self.lip_hw = data["lipWidth"] / 2.0
        self.lip_h = data["lipHeight"]
        self.glass = data["glass"]
        self.fill = data["fillHeight"]
        self.capacity = int(data["capacity"])
        self.ppu = float((data.get("sprite") or {}).get("pixelsPerUnit") or 160)
        self.neck_y = self.body + self.sh                # end of the shoulder = bottom of the neck
        self.mouth = self.neck_y + self.neck
        problems = []
        if not 0 < self.r <= self.hw:
            problems.append("bottomRadius must be in (0, innerWidth/2]")
        if self.r > self.body:
            problems.append("bottomRadius must not exceed bodyHeight")
        if not 0 < self.nhw < self.hw:
            problems.append("neckWidth must be in (0, innerWidth)")
        if min(self.sh, self.neck, self.lip_h, self.glass) <= 0:
            problems.append("shoulderHeight, neckHeight, lipHeight and glass must be > 0")
        if not 0 < self.fill <= self.mouth:
            problems.append("fillHeight must be in (0, mouth]")
        if problems:
            raise SystemExit("bottle_shape.json: " + "; ".join(problems))

    def half_width(self, y):
        """Interior half width at height y (vectorized), 0 below the bottom; constant (neck) above the mouth."""
        y = np.asarray(y, np.float64)
        out = np.zeros_like(y)
        m = (y >= 0) & (y < self.r)                      # bottom corners (quarter circles)
        dy = self.r - y[m]
        out[m] = self.hw - self.r + np.sqrt(np.maximum(self.r * self.r - dy * dy, 0.0))
        m = (y >= self.r) & (y <= self.body)             # straight body
        out[m] = self.hw
        m = (y > self.body) & (y <= self.neck_y)         # shoulder: x = hw - (hw - nhw) t^2, y = neck_y - sh (1-t)^2
        t = 1.0 - np.sqrt(np.clip((self.neck_y - y[m]) / self.sh, 0.0, 1.0))
        out[m] = self.hw - (self.hw - self.nhw) * t * t
        out[y > self.neck_y] = self.nhw                  # neck (and its extension above the mouth)
        return out

    def inside(self, x, y, open_top=False):
        """Interior test. open_top: the neck continues above the mouth (distance fields without a lid)."""
        top = np.inf if open_top else self.mouth
        return (y >= 0) & (y <= top) & (np.abs(x) <= self.half_width(y))

    def profile(self, n_arc=24, n_bez=24):
        """Right half of the interior outline as polygon breakpoints (y, half width), bottom to top: the bottom-right
        corner arc, the straight side (implicit), the shoulder Bezier and the neck up to the mouth."""
        pts = []
        for i in range(n_arc + 1):
            a = -math.pi / 2 + (math.pi / 2) * i / n_arc
            pts.append((self.r + self.r * math.sin(a), self.hw - self.r + self.r * math.cos(a)))
        for i in range(n_bez + 1):
            t = i / n_bez
            pts.append((self.neck_y - self.sh * (1 - t) ** 2, self.hw - (self.hw - self.nhw) * t * t))
        pts.append((self.mouth, self.nhw))
        return pts

    def band_polygon(self, y0, y1):
        """Polygon (x, y) of the interior between heights y0 < y1 -- one liquid layer as the game meshes it: the
        outline breakpoints inside the band plus the two cut lines, right side up, left side down."""
        prof = self.profile()
        ys = np.array([p[0] for p in prof])
        xs = np.array([p[1] for p in prof])
        y0, y1 = max(y0, 0.0), min(y1, self.mouth)
        cut = [y0] + [y for y in ys if y0 < y < y1] + [y1]
        right = [(float(np.interp(y, ys, xs)), y) for y in cut]
        return right + [(-x, y) for x, y in reversed(right)]


class BottleCanvas:
    """Pixel canvas of the stacked bottle sprites and the supersampled grid in shape units."""

    def __init__(self, shape):
        s, ppu = shape, shape.ppu
        half = max(s.hw + s.glass, s.lip_hw) + GLOW_WIDTH
        self.w = 2 * int(math.ceil(half * ppu + PAD))            # even: the axis sits on a pixel boundary
        self.ox = self.w // 2
        self.oy = int(math.ceil((s.glass + GLOW_WIDTH) * ppu + PAD))
        h = self.oy + int(math.ceil((s.mouth + s.lip_h / 2 + GLOW_WIDTH) * ppu + PAD))
        self.h = h + h % 2
        ws, hs = self.w * SS, self.h * SS
        self.xs = ((np.arange(ws) + 0.5) / SS - self.ox) / ppu
        self.ys = ((hs - np.arange(hs) - 0.5) / SS - self.oy) / ppu   # row 0 is the top
        self.X, self.Y = np.meshgrid(self.xs, self.ys)
        self.unit = 1.0 / (SS * ppu)                               # shape units per sample

    def signed_distance(self, mask, normals=False):
        """Signed distance to the boundary of `mask` in shape units (negative inside), from exact Euclidean distance
        transforms between sample centers, shifted half a sample so the zero crossing lies on the boundary.
        normals=True also returns the outward unit normal (toward / away from the nearest sample across it)."""
        if not normals:
            d_out = ndimage.distance_transform_edt(~mask)
            d_in = ndimage.distance_transform_edt(mask)
            return np.where(mask, 0.5 - d_in, d_out - 0.5) * self.unit
        d_out, i_out = ndimage.distance_transform_edt(~mask, return_indices=True)
        d_in, i_in = ndimage.distance_transform_edt(mask, return_indices=True)
        sd = np.where(mask, 0.5 - d_in, d_out - 0.5) * self.unit
        iy = np.where(mask, i_in[0], i_out[0])
        ix = np.where(mask, i_in[1], i_out[1])
        sign = np.where(mask, 1.0, -1.0)
        nx = (self.xs[ix] - self.X) * sign
        ny = (self.ys[iy] - self.Y) * sign
        n = np.hypot(nx, ny)
        n[n == 0] = 1.0
        return sd, nx / n, ny / n


class Layer:
    """Premultiplied RGBA accumulator on a supersampled grid (Porter-Duff "over"); array() box-filters it down."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.c = np.zeros((h * SS, w * SS, 4), np.float32)

    def over(self, color, alpha):
        """color: (r, g, b) 0..255 or an (H, W, 3) array of them; alpha: scalar field 0..1."""
        a = np.clip(np.asarray(alpha, np.float32), 0.0, 1.0)
        if a.ndim == 0:
            a = np.full(self.c.shape[:2], float(a), np.float32)
        a = a[..., None]
        col = np.asarray(color, np.float32) / 255.0
        self.c[..., :3] = col * a + self.c[..., :3] * (1.0 - a)
        self.c[..., 3:] = a + self.c[..., 3:] * (1.0 - a)

    def array(self):
        c = self.c.reshape(self.h, SS, self.w, SS, 4).mean(axis=(1, 3))
        a = c[..., 3:4]
        rgb = np.clip(c[..., :3] / np.maximum(a, 1e-6), 0.0, 1.0)
        out = np.concatenate([rgb * 255.0, a * 255.0], axis=-1)
        out[..., 3][out[..., 3] < 0.5] = 0.0           # rounds to 0 anyway: let bleed() pick the edge color
        return bleed(out)


def closing(mask, radius):
    """Morphological closing with a disk of `radius` samples: concave corners get round fillets, the rest stays."""
    grown = ndimage.distance_transform_edt(~mask) <= radius
    return (ndimage.distance_transform_edt(grown) > radius) | mask


def vstreak(X, Y, xc, half_w, y0, y1, fade_in, fade_out, peak, sharp=1.0):
    """Vertical specular streak: parabolic across (sharp < 1 flattens the top), faded in/out along its length."""
    u = (X - xc) / half_w
    across = np.clip(1.0 - u * u, 0.0, 1.0) ** sharp
    along = smooth(y0, y0 + fade_in, Y) * (1.0 - smooth(y1 - fade_out, y1, Y))
    return peak * across * along


def make_bottle_sprites(shape):
    """Renders the bottle sprites. Returns ({name: RGBA float array}, layout dict for the JSON "sprite" block)."""
    cv = BottleCanvas(shape)
    s, X, Y = shape, cv.X, cv.Y

    interior = s.inside(X, Y)                                  # the liquid's space (closed at the mouth)
    open_in = s.inside(X, Y, open_top=True)                    # walls/distances: the neck has no lid
    sd, nx, ny = cv.signed_distance(open_in, normals=True)
    din = -sd                                                  # depth inside the interior (shape units)
    lam = np.clip(0.5 + 0.5 * (nx * LIGHT[0] + ny * LIGHT[1]), 0.0, 1.0)   # 1 = surface faces the light

    # Glass body = interior + walls (outline offset outward by `glass`), cut at the mouth; the concave outer corner
    # where the shoulder meets the neck gets a small round fillet.
    base = (sd <= s.glass) & (Y <= s.mouth)
    body = closing(base, NECK_FILLET / cv.unit)
    walls = body & ~open_in
    sd_body = cv.signed_distance(body)
    lip_bot, lip_top = s.mouth - s.lip_h / 2, s.mouth + s.lip_h / 2
    lip_sd = sd_round_rect(X, Y, -s.lip_hw, lip_bot, s.lip_hw, lip_top, s.lip_h * 0.45)
    lip = lip_sd <= 0
    sd_all = cv.signed_distance(body | lip)
    below_lip = Y < lip_bot + 0.01

    # ---------------------------------------------------------------- back (behind the liquid)
    back = Layer(cv.w, cv.h)
    back.over((222, 228, 255), 0.08 * interior)                                       # faint cool frost
    back.over((38, 42, 110), 0.20 * (1.0 - smooth(0.0, 0.24, din)) * interior)        # darker toward the walls
    e = np.hypot((X - 0.34 * s.hw) / (0.30 * s.hw), (Y - s.body * 0.56) / (s.body * 0.34))
    back.over(WHITE, 0.15 * (1.0 - smooth(0.35, 1.0, e)) * interior)                  # back-wall ellipse highlight

    # ---------------------------------------------------------------- front (over the liquid)
    front = Layer(cv.w, cv.h)
    glassy = open_in & below_lip                               # interior under the lip (front decorations)
    # cylindrical shading of whatever is inside (flat liquid colors read as round): shade on the side away from the
    # light, a white fresnel rim on the lit side, a broad faint sheen on the lit half
    front.over(INK, 0.24 * (1.0 - smooth(0.0, 0.16, din)) * (1.0 - lam) ** 1.3 * glassy)
    front.over(WHITE, 0.26 * (1.0 - smooth(0.0, 0.07, din)) * (0.2 + 0.8 * lam) * glassy)
    sheen = smooth(0.15, -0.30, X) * (1.0 - smooth(-0.30, -s.hw + 0.02, X))
    front.over(WHITE, 0.07 * sheen * smooth(0.12, 0.5, Y) * (1.0 - smooth(s.body - 0.2, s.neck_y, Y)) * glassy)

    # glass walls, seen edge-on: translucent lavender body, a bright rim toward the outer edge (brighter where the
    # wall faces the light), then the crisp inner edge and the dark outer contour drawn below
    w = np.clip(sd / s.glass, 0.0, 1.0)                        # 0 = inner edge .. 1 = outer edge
    lit, shade = np.array((244, 241, 255), np.float32), np.array((186, 176, 236), np.float32)
    wall_rgb = shade + (lit - shade) * lam[..., None]
    front.over(wall_rgb, (0.20 + 0.30 * lam) * walls)
    front.over(WHITE, (0.30 + 0.55 * lam) * np.exp(-((w - 0.60) / 0.15) ** 2) * walls)

    # specular streaks over the liquid: a bold one inside the left wall (soft band + crisp core), a thin one on the
    # right, a short one in the neck
    xl = -s.hw + 0.13
    front.over(WHITE, vstreak(X, Y, xl, 0.075, 0.34, s.body + 0.06, 0.40, 0.60, 0.34) * glassy)
    front.over(WHITE, vstreak(X, Y, xl - 0.012, 0.030, 0.44, s.body - 0.02, 0.30, 0.50, 0.92, 0.45) * glassy)
    xr = s.hw - 0.08
    front.over(WHITE, vstreak(X, Y, xr, 0.019, 0.66, s.body - 0.18, 0.30, 0.45, 0.62, 0.6) * glassy)
    xn = -s.nhw + 0.075
    front.over(WHITE, vstreak(X, Y, xn, 0.024, s.neck_y + 0.005, lip_bot + 0.02, 0.07, 0.03, 0.70, 0.6) * glassy)

    # curved highlight along the bottom inner edge (bright at the lower left, a fainter reflex at the lower right)
    cy = s.r + 0.08
    phi = np.degrees(np.arctan2(Y - cy, X))
    band = smooth(0.026, 0.038, din) * (1.0 - smooth(0.062, 0.078, din))
    wpos = np.exp(-((phi + 132.0) / 26.0) ** 2) + 0.5 * np.exp(-((phi + 46.0) / 14.0) ** 2)
    front.over(WHITE, 0.88 * band * np.clip(wpos, 0.0, 1.0) * (Y < s.r + 0.22) * glassy)

    # left shoulder: a short arc following the curve
    band = smooth(0.022, 0.032, din) * (1.0 - smooth(0.056, 0.070, din))
    along = smooth(s.body - 0.08, s.body + 0.05, Y) * (1.0 - smooth(s.neck_y - 0.10, s.neck_y - 0.02, Y))
    front.over(WHITE, 0.75 * band * along * (X < -s.nhw * 0.4) * glassy)

    # crisp inner edge of the wall (also hides the stair-stepped edge of the liquid mesh under ~1-2 px of glass)
    edge_in = (sd >= -0.011) & (sd <= 0.006)
    front.over(WHITE, (0.38 + 0.47 * lam) * edge_in * below_lip)

    # dark outer contour: keeps the glass readable on light and busy backgrounds
    edge_out = (sd_body >= -OUTLINE_WIDTH) & (sd_body <= 0.0)
    front.over(INK, (0.58 + 0.27 * (1.0 - lam)) * edge_out * (Y < s.mouth - 0.03))

    # lip (a glass ring seen from the side): soft shadow on the neck, glass band with a vertical gradient and a
    # darker right end, glossy highlight, end dot, lower inner shade and contour
    front.over(INK, 0.32 * (1.0 - smooth(0.0, 0.05, lip_bot - Y)) * (Y <= lip_bot) * body)
    t = np.clip((Y - lip_bot) / s.lip_h, 0.0, 1.0)
    t = (t * t * (3.0 - 2.0 * t))[..., None]
    lip_rgb = np.array((172, 158, 228), np.float32) * (1 - t) + np.array((252, 251, 255), np.float32) * t
    front.over(lip_rgb, 0.93 * lip)
    front.over(INK, 0.20 * smooth(0.0, s.lip_hw, X) * lip)
    hl = sd_round_rect(X, Y, -s.lip_hw + 0.07, s.mouth + 0.010, s.lip_hw - 0.10, lip_top - 0.024, 0.016)
    front.over(WHITE, 0.95 * (1.0 - smooth(-0.006, 0.006, hl)) * lip)
    dot = np.hypot((X + s.lip_hw - 0.05) / 0.020, (Y - s.mouth + 0.008) / 0.017)
    front.over(WHITE, 0.90 * (1.0 - smooth(0.5, 1.0, dot)) * lip)
    front.over(INK, 0.28 * (1.0 - smooth(0.005, 0.04, Y - lip_bot)) * lip)
    front.over(INK, 0.82 * (lip & (lip_sd >= -OUTLINE_WIDTH)))

    # ---------------------------------------------------------------- glow (selection halo, tinted in Unity)
    glow = Layer(cv.w, cv.h)
    t = np.clip(sd_all / GLOW_WIDTH, 0.0, 1.0)
    a = np.where(sd_all >= 0, (1.0 - t) ** 1.7, 1.0 - smooth(0.012, 0.032, -sd_all))
    glow.over(WHITE, a)

    # ---------------------------------------------------------------- mask (the liquid's space)
    mask = Layer(cv.w, cv.h)
    mask.over(WHITE, interior.astype(np.float32))

    sprites = {"bottle_back": back.array(), "bottle_front": front.array(), "bottle_glow": glow.array(),
               "bottle_mask": mask.array()}

    # ---------------------------------------------------------------- shadow (own canvas)
    ppu = s.ppu
    sw = 2 * int(math.ceil(SHADOW_RX * ppu + PAD))
    sh = 2 * int(math.ceil(SHADOW_RY * ppu + PAD))

    def shadow(px, py):
        u = (px - sw / 2) / (SHADOW_RX * ppu)
        v = (py - sh / 2) / (SHADOW_RY * ppu)
        e2 = u * u + v * v
        core = 1.0 - smooth(0.10, 0.62, np.sqrt(e2 * 1.9))      # dark contact patch
        soft = np.exp(-3.2 * e2) * (1.0 - smooth(0.80, 1.0, np.sqrt(e2)))   # wide penumbra, exactly 0 at the rim
        return np.clip(0.50 * core + 0.30 * soft * (1.0 - 0.5 * core), 0.0, 1.0)
    sa = supersample(sw, sh, shadow)
    shadow_arr = np.zeros((sh, sw, 4), np.float32)
    shadow_arr[..., :3] = (20, 10, 36)
    shadow_arr[..., 3] = sa * 255.0
    shadow_arr[..., 3][shadow_arr[..., 3] < 0.5] = 0.0
    sprites["bottle_shadow"] = shadow_arr

    shadow_cy = cv.oy - s.glass * ppu
    layout = {
        "pixelsPerUnit": int(ppu) if float(ppu).is_integer() else ppu,
        "width": cv.w,
        "height": cv.h,
        "originX": cv.ox,
        "originY": cv.oy,
        "shadowWidth": sw,
        "shadowHeight": sh,
        "shadowCenterX": cv.ox,
        "shadowCenterY": round(shadow_cy, 2),
    }
    # normalized interior bounding box (art_index "inner"): x in [-w/2, w/2], y in [0, mouth]
    inner = [(cv.ox - s.hw * ppu) / cv.w, cv.oy / cv.h, (2 * s.hw * ppu) / cv.w, (s.mouth * ppu) / cv.h]
    return sprites, layout, [round(v, 4) for v in inner]


def load_bottle_shape():
    with open(BOTTLE_JSON, encoding="utf-8") as fh:
        return json.load(fh)


SPRITE_ABOUT = (
    "Written by Tools/process_art.py. bottle_back, bottle_front, bottle_glow and bottle_mask share ONE canvas of "
    "width x height pixels; the shape origin (0,0) = inner bottom on the axis is at pixel (originX, originY) from the "
    "canvas BOTTOM-LEFT, and 1 shape unit = pixelsPerUnit pixels. Stack order: shadow, glow (selection, tinted), back, "
    "liquid mesh, front. bottle_shadow has its own canvas (shadowWidth x shadowHeight at the same pixels per unit) "
    "whose center goes at pixel (shadowCenterX, shadowCenterY) of the main canvas (= shape point (0, -glass), the "
    "outer bottom of the glass; it may lie below the canvas)."
)


def write_bottle_layout(layout):
    """Rewrites only the "sprite" block of bottle_shape.json. The file is re-read right before writing (the rest of
    it belongs to the gameplay code and may have been edited meanwhile) and the block is replaced textually, so every
    other byte -- formatting included -- stays as it was."""
    with open(BOTTLE_JSON, encoding="utf-8") as fh:
        text = fh.read()
    data = json.loads(text)
    block = dict({"about": SPRITE_ABOUT}, **layout)
    body = json.dumps(block, indent=2, ensure_ascii=False, allow_nan=False).replace("\n", "\n  ")
    start = text.find('"sprite"')
    span = None
    if start >= 0 and isinstance(data.get("sprite"), dict):
        brace = text.find("{", start)
        depth, i, in_str = 0, brace, False
        while 0 <= i < len(text):
            ch = text[i]
            if in_str:
                if ch == "\\":
                    i += 1
                elif ch == '"':
                    in_str = False
            elif ch == '"':
                in_str = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    span = (brace, i + 1)
                    break
            i += 1
    if span:
        new_text = text[:span[0]] + body + text[span[1]:]
    else:                                   # no sprite block yet: rewrite the whole file
        data["sprite"] = block
        new_text = json.dumps(data, indent=2, ensure_ascii=False, allow_nan=False) + "\n"
    if json.loads(new_text).get("sprite") != block:
        raise SystemExit("bottle_shape.json: could not write the sprite block")
    if new_text != text:
        with open(BOTTLE_JSON, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(new_text)


# ============================================================================================ previews

def checker(w, h, cell=12, dark=False):
    yy, xx = np.mgrid[0:h, 0:w]
    c = ((xx // cell + yy // cell) % 2).astype(np.float32)
    c0, c1 = ([60, 52, 78], [40, 34, 56]) if dark else ([206, 206, 216], [250, 250, 252])
    base = np.where(c[..., None] > 0, np.array(c0, np.float32), np.array(c1, np.float32))
    out = np.concatenate([base, np.full((h, w, 1), 255, np.float32)], axis=-1)
    return to_image(out)


def font(size):
    for path in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc",
                 "/Library/Fonts/Arial.ttf"):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                pass
    return ImageFont.load_default()


def on_dark(name):
    """White/light procedural sprites are shown on a dark checkerboard."""
    return (name.startswith("ui_") and name != "ui_vignette") or name in BOTTLE_STACK


def contact_sheet(sprites, path, cell=170, cols=12):
    names = sorted(sprites)
    rows = (len(names) + cols - 1) // cols
    label_h = 28
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + label_h)), (255, 255, 255, 255))
    d = ImageDraw.Draw(sheet)
    f = font(11)
    for i, n in enumerate(names):
        im = sprites[n]
        x, y = (i % cols) * cell, (i // cols) * (cell + label_h)
        tile = checker(cell, cell, dark=on_dark(n))
        t = im.copy()
        t.thumbnail((cell - 8, cell - 8), Image.LANCZOS)
        tile.alpha_composite(t, ((cell - t.width) // 2, (cell - t.height) // 2))
        sheet.alpha_composite(tile, (x, y))
        d.text((x + 3, y + cell + 1), n[:26], fill=(0, 0, 0, 255), font=f)
        d.text((x + 3, y + cell + 14), f"{im.width}x{im.height}", fill=(90, 90, 90, 255), font=f)
    sheet.convert("RGB").save(path, optimize=True)


def nine_slice(im, border, size):
    """Renders a 9-sliced sprite at size (w, h) like Unity's Image.Type.Sliced (corners unscaled)."""
    l, b, r, t = border
    W, H = im.size
    w, h = size
    # Unity scales the borders down if the target is smaller than the sum of the borders.
    sx = min(1.0, w / max(l + r, 1)) if l + r > 0 else 1.0
    sy = min(1.0, h / max(t + b, 1)) if t + b > 0 else 1.0
    L, R, T, B = int(l * sx), int(r * sx), int(t * sy), int(b * sy)
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    xs_src = [0, l, W - r, W]
    ys_src = [0, t, H - b, H]
    xs_dst = [0, L, w - R, w]
    ys_dst = [0, T, h - B, h]
    for i in range(3):
        for j in range(3):
            sbox = (xs_src[i], ys_src[j], xs_src[i + 1], ys_src[j + 1])
            dw, dh = xs_dst[i + 1] - xs_dst[i], ys_dst[j + 1] - ys_dst[j]
            if sbox[2] <= sbox[0] or sbox[3] <= sbox[1] or dw <= 0 or dh <= 0:
                continue
            out.alpha_composite(im.crop(sbox).resize((dw, dh), Image.BILINEAR), (xs_dst[i], ys_dst[j]))
    return out


def nine_slice_preview(sprites, index, path):
    items = [(n, e) for n, e in sorted(index.items()) if any(e["border"]) and n in sprites]
    tiles = []
    f = font(14)
    for n, e in items:
        im = sprites[n]
        W, H = im.size
        variants = [(W, H), (int(W * 1.7), H), (W, int(H * 1.6)), (int(W * 0.75), int(H * 0.8))]
        ws = sum(v[0] for v in variants) + 20 * (len(variants) + 1)
        hs = max(v[1] for v in variants) + 60
        tile = checker(ws, hs, 16, dark=on_dark(n))
        d = ImageDraw.Draw(tile)
        x = 20
        for k, v in enumerate(variants):
            sl = nine_slice(im, e["border"], v)
            tile.alpha_composite(sl, (x, 40))
            if k == 0:   # draw the slice lines on the native-size copy
                l, b, r, t = e["border"]
                for gx in (x + l, x + W - r):
                    d.line((gx, 40, gx, 40 + H), fill=(255, 0, 80, 255), width=1)
                for gy in (40 + t, 40 + H - b):
                    d.line((x, gy, x + W, gy), fill=(255, 0, 80, 255), width=1)
            x += v[0] + 20
        d.text((10, 10), f"{n}  border l,b,r,t = {e['border']}", fill=(255, 255, 255, 255) if on_dark(n)
               else (0, 0, 0, 255), font=f)
        tiles.append(tile)
    if not tiles:
        return
    scale = min(1.0, 2400 / max(t.width for t in tiles))
    tiles = [t.resize((max(1, int(t.width * scale)), max(1, int(t.height * scale))), Image.LANCZOS) for t in tiles]
    sheet = Image.new("RGBA", (max(t.width for t in tiles), sum(t.height for t in tiles)), (255, 255, 255, 255))
    y = 0
    for t in tiles:
        sheet.alpha_composite(t, (0, y))
        y += t.height
    sheet.convert("RGB").save(path, optimize=True)


def tint(im, rgb, alpha=1.0):
    """White sprite tinted like Image.color = (rgb, alpha)."""
    arr = to_array(im)
    arr[..., :3] = arr[..., :3] * (np.array(rgb, np.float32) / 255.0)
    arr[..., 3] *= alpha
    return to_image(arr)


def liquid_layer(shape, layout, units, scale_ss=SS):
    """Flat liquid bands (bottom to top, one color per unit) on the bottle canvas, rasterized from band_polygon()
    -- the polygon the game meshes -- with SS x SS antialiasing."""
    W, H, ox, oy, ppu = layout["width"], layout["height"], layout["originX"], layout["originY"], shape.ppu
    unit_h = shape.fill / shape.capacity
    big = Image.new("RGBA", (W * scale_ss, H * scale_ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    for k, color in enumerate(units):
        poly = shape.band_polygon(k * unit_h, (k + 1) * unit_h)
        pts = [((ox + x * ppu) * scale_ss, (H - (oy + y * ppu)) * scale_ss) for x, y in poly]
        d.polygon(pts, fill=LIQUID_PREVIEW[color] + (255,))
    return big.resize((W, H), Image.BOX)


def compose_bottle(shape, layout, art, units=(), glow=None, cork=False, stone=False):
    """One bottle stacked like the game does it (shadow, glow, back, liquid, front, cork / stone). The main canvas
    sits at the top-left of the returned image, which adds rows below for the part of the shadow under the canvas.
    Cork and stone placements are only plausible guesses for the preview (the game positions them itself)."""
    W, H = layout["width"], layout["height"]
    sw, sh = layout["shadowWidth"], layout["shadowHeight"]
    scx, scy = layout["shadowCenterX"], layout["shadowCenterY"]
    extra = max(0, int(math.ceil(sh / 2 - scy)) + 2)
    out = Image.new("RGBA", (W, H + extra), (0, 0, 0, 0))
    out.alpha_composite(art["bottle_shadow"], (int(round(scx - sw / 2)), int(round(H - scy - sh / 2))))
    if glow:
        out.alpha_composite(tint(art["bottle_glow"], glow, 0.95), (0, 0))
    out.alpha_composite(art["bottle_back"], (0, 0))
    if units:
        out.alpha_composite(liquid_layer(shape, layout, units), (0, 0))
    out.alpha_composite(art["bottle_front"], (0, 0))
    ppu, ox, oy = shape.ppu, layout["originX"], layout["originY"]
    if cork and "cork" in art:
        c = art["cork"]
        cw = (2 * shape.nhw + 0.12) * ppu                          # a bit wider than the neck: pressed into it
        ch = c.height * cw / c.width
        c = c.resize((int(round(cw)), int(round(ch))), Image.LANCZOS)
        cy_top = H - (oy + (shape.mouth + 0.24) * ppu)
        out.alpha_composite(c, (int(round(ox - c.width / 2)), int(round(cy_top))))
    if stone and "stone_wrap" in art:
        st = art["stone_wrap"]
        sh_px = (shape.mouth + shape.lip_h / 2 + shape.glass + 0.16) * ppu
        sw_px = st.width * sh_px / st.height
        st = st.resize((int(round(sw_px)), int(round(sh_px))), Image.LANCZOS)
        cy = H - (oy + ((shape.mouth + shape.lip_h / 2 - shape.glass) / 2) * ppu)
        out.alpha_composite(st, (int(round(ox - st.width / 2)), int(round(cy - st.height / 2))))
    return out


def cover(im, w, h):
    """Scale + center-crop an image to cover w x h."""
    s = max(w / im.width, h / im.height)
    im = im.resize((int(math.ceil(im.width * s)), int(math.ceil(im.height * s))), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((x, y, x + w, y + h))


def bottle_preview(shape, layout, art, path):
    """Glass + flat liquid bands over three game backgrounds (two sizes), plus a 3x alignment check where the
    interior polygon of the liquid is drawn in magenta over the glass."""
    big_set = [
        dict(units=("red", "blue", "yellow", "green"), glow=(255, 214, 64), lift=34),
        dict(units=("purple", "orange")),
        dict(units=()),
        dict(units=("pink",) * 4, cork=True),
    ]
    small_set = [
        dict(units=("sky", "lime", "sky")),
        dict(units=("mystery", "mystery", "teal", "white")),
        dict(units=("brown",)),
        dict(units=("yellow",) * 4, cork=True),
        dict(units=("blue", "red"), stone=True),
        dict(units=()),
        dict(units=("green", "purple", "orange", "pink"), glow=(255, 255, 255)),
    ]
    bgs = [n for n in ("gamebg_forest", "gamebg_candy", "gamebg_moon") if os.path.exists(os.path.join(RAW, n + ".png"))]
    if not bgs:
        bgs = [None]
    pw, ph = 900, 1010
    s_big, s_small = 0.86, 0.5
    f = font(18)
    panels = []
    for bg in bgs:
        if bg:
            panel = cover(Image.open(os.path.join(RAW, bg + ".png")).convert("RGBA"), pw, ph)
        else:
            panel = checker(pw, ph, 16)
        d = ImageDraw.Draw(panel)
        d.rectangle((0, 0, pw, 30), fill=(0, 0, 0, 150))
        d.text((10, 6), f"{bg or 'checker'}  (top row x{s_big}, bottom row x{s_small})", fill=(255, 255, 255, 255), font=f)
        for items, sc, base_y in ((big_set, s_big, 560), (small_set, s_small, 960)):
            ims = [compose_bottle(shape, layout, art, **{k: v for k, v in it.items() if k != "lift"}) for it in items]
            ims = [im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))), Image.LANCZOS) for im in ims]
            gap = (pw - sum(im.width for im in ims)) / (len(ims) + 1)
            x = gap
            for it, im in zip(items, ims):
                lift = int(it.get("lift", 0) * sc)
                foot = int(round(layout["height"] * sc))                  # main canvas bottom inside the image
                panel.alpha_composite(im, (int(round(x)), base_y - foot - lift))
                x += im.width + gap
        panels.append(panel)

    # alignment check: 3x, nearest-neighbour, with the liquid polygon outline in magenta
    z = 3
    full = compose_bottle(shape, layout, art, units=("red", "blue", "yellow", "green"))
    H, ox, oy, ppu = layout["height"], layout["originX"], layout["originY"], shape.ppu
    zoom = checker(full.width * z, full.height * z, 24, dark=True)
    zoom.alpha_composite(full.resize((full.width * z, full.height * z), Image.NEAREST))
    d = ImageDraw.Draw(zoom)
    outline = shape.band_polygon(0.0, shape.mouth)
    pts = [((ox + x * ppu) * z, (H - (oy + y * ppu)) * z) for x, y in outline]
    d.line(pts + [pts[0]], fill=(255, 0, 255, 255), width=1)
    unit_h = shape.fill / shape.capacity
    for k in range(shape.capacity + 1):
        yy = (H - (oy + k * unit_h * ppu)) * z
        d.line(((ox - shape.hw * ppu) * z - 8, yy, (ox - shape.hw * ppu) * z - 2, yy), fill=(255, 0, 255, 255), width=1)
    cx0 = (ox - (shape.hw + shape.glass) * ppu - 14) * z
    cx1 = (ox + (shape.hw + shape.glass) * ppu + 14) * z
    top_crop = zoom.crop((int(cx0), int((H - (oy + (shape.mouth + 0.2) * ppu)) * z),
                          int(cx1), int((H - (oy + (shape.body - 0.55) * ppu)) * z)))
    bot_crop = zoom.crop((int(cx0), int((H - (oy + 0.75 * ppu)) * z), int(cx1), int((H - (oy - 0.2 * ppu)) * z)))
    strip_h = max(top_crop.height, bot_crop.height) + 40
    strip = Image.new("RGBA", (top_crop.width + bot_crop.width + 60, strip_h), (24, 18, 36, 255))
    strip.alpha_composite(top_crop, (20, 36))
    strip.alpha_composite(bot_crop, (40 + top_crop.width, 36))
    ImageDraw.Draw(strip).text((20, 8), "alignment x3: magenta = liquid interior polygon (band_polygon), ticks = unit "
                               "levels", fill=(255, 255, 255, 255), font=f)

    sheet_w = max(pw * len(panels), strip.width)
    sheet = Image.new("RGBA", (sheet_w, ph + strip.height), (24, 18, 36, 255))
    for i, p in enumerate(panels):
        sheet.alpha_composite(p, (i * pw, 0))
    sheet.alpha_composite(strip, (0, ph))
    sheet.convert("RGB").save(path, optimize=True)


# ============================================================================================ main

def save_sprite(name, img, opaque=False):
    path = os.path.join(OUT, name + ".png")
    if opaque:
        img.convert("RGB").save(path, optimize=True)
    else:
        img.save(path, optimize=True)


def process(only=None):
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PREVIEW, exist_ok=True)
    os.makedirs(APP_ICON_DIR, exist_ok=True)

    def wanted(name):
        return not only or any(name.startswith(p) for p in only)

    index = {}
    sprites = {}
    manifest_names = {a["name"] for a in ASSETS}
    for extra in sorted(f[:-4] for f in os.listdir(RAW) if f.endswith(".png") and f[:-4] not in manifest_names):
        print(f"  ? {extra}.png is not in the manifest (skipped)")

    # ---------------------------------------------------------------- raw images
    for asset in ASSETS:
        name = asset["name"]
        if not wanted(name):
            continue
        src = os.path.join(RAW, name + ".png")
        if not os.path.exists(src):
            print(f"  - missing raw image {name}")
            continue
        rule = rule_for(name)
        raw_im = Image.open(src)
        arr = to_array(raw_im)

        if name == "app_icon":
            icon = Image.open(src).convert("RGB")
            if icon.size != (1024, 1024):
                icon = icon.resize((1024, 1024), Image.LANCZOS)
            icon.save(os.path.join(APP_ICON_DIR, "app_icon.png"), optimize=True)
            print(f"  app_icon -> Art/AppIcon/app_icon.png {icon.size}")

        if rule.opaque:
            arr[..., 3] = 255
        else:
            if asset["transparent"]:
                arr = ensure_alpha(arr, name)
            arr = clean_alpha(arr, soft=rule.glow)
            if not rule.glow:
                arr = defringe(arr)

        out = fit_image(arr, rule)
        if rule.opaque:
            out[..., 3] = 255
        else:
            a = out[..., 3]
            if rule.glow:
                # the roll-off was applied before resizing: only drop resampling noise and snap the solid core
                a[a < GLOW_FLOOR] = 0
                a[a >= ALPHA_SOLID] = 255
            else:
                out = clean_alpha(out)       # LANCZOS ringing creates faint alpha around hard edges
                a = out[..., 3]
                a[a < 3] = 0
            out = bleed(out)

        border, inner = nine_slice_info(name, out)
        check_border(name, border, out.shape[1], out.shape[0])
        img = to_image(out)
        save_sprite(name, img, rule.opaque)
        sprites[name] = img
        index[name] = {"name": name, "w": img.width, "h": img.height, "border": [int(v) for v in border],
                       "pivot": CENTER, "inner": [round(float(v), 4) for v in inner]}
        extra = f" border={border}" if any(border) else ""
        extra += f" inner={index[name]['inner']}" if inner != FULL_RECT else ""
        print(f"  {name:20s} {raw_im.size[0]}x{raw_im.size[1]} -> {img.width}x{img.height}{extra}")

    # ---------------------------------------------------------------- brand assets (official art: copied unchanged)
    if os.path.isdir(BRAND):
        for f in sorted(os.listdir(BRAND)):
            name = f[:-4]
            if not (f.endswith(".png") and name.startswith("brand_")) or not wanted(name):
                continue
            shutil.copyfile(os.path.join(BRAND, f), os.path.join(OUT, f))
            img = Image.open(os.path.join(OUT, f)).convert("RGBA")
            sprites[name] = img
            index[name] = {"name": name, "w": img.width, "h": img.height, "border": [0, 0, 0, 0],
                           "pivot": CENTER, "inner": FULL_RECT}
            print(f"  {name:20s} brand {img.width}x{img.height} (copied)")

    # ---------------------------------------------------------------- procedural UI primitives
    for name, (arr, border) in make_primitives().items():
        if not wanted(name):
            continue
        img = to_image(arr)
        save_sprite(name, img)
        sprites[name] = img
        index[name] = {"name": name, "w": img.width, "h": img.height, "border": border,
                       "pivot": CENTER, "inner": FULL_RECT}
        print(f"  {name:20s} procedural {img.width}x{img.height} border={border}")

    # ---------------------------------------------------------------- procedural bottle glass
    bottle = None
    if any(wanted(n) for n in BOTTLE_SPRITES):
        shape = BottleShape(load_bottle_shape())
        arrays, layout, inner = make_bottle_sprites(shape)
        for name in BOTTLE_SPRITES:
            img = to_image(arrays[name])
            save_sprite(name, img)
            sprites[name] = img
            index[name] = {"name": name, "w": img.width, "h": img.height, "border": [0, 0, 0, 0],
                           "pivot": CENTER, "inner": inner if name in BOTTLE_STACK else FULL_RECT}
        write_bottle_layout(layout)
        bottle = (shape, layout)
        print(f"  bottle_* procedural canvas {layout['width']}x{layout['height']} origin "
              f"({layout['originX']},{layout['originY']}) @ {layout['pixelsPerUnit']} px/unit; shadow "
              f"{layout['shadowWidth']}x{layout['shadowHeight']} at ({layout['shadowCenterX']},{layout['shadowCenterY']})"
              f" -> bottle_shape.json")

    if only:
        # Partial run: keep the entries of sprites that were not reprocessed.
        if os.path.exists(INDEX):
            with open(INDEX, encoding="utf-8") as fh:
                old = json.load(fh).get("sprites", [])
            for e in old:
                index.setdefault(e["name"], e)
                p = os.path.join(OUT, e["name"] + ".png")
                if e["name"] not in sprites and os.path.exists(p):
                    sprites[e["name"]] = Image.open(p).convert("RGBA")
        unlisted = sorted(f[:-4] for f in os.listdir(OUT) if f.endswith(".png") and f[:-4] not in index)
        if unlisted:
            print(f"  ! not in art_index.json (run without --only to rebuild it): {unlisted}")
    else:
        # Full run only (a partial run without a previous index would otherwise wipe every other sprite):
        # remove stale sprites left over from renamed/removed assets so Resources only ships what the index lists,
        # together with their .meta files (and .meta files whose asset is already gone).
        removed = 0
        for f in sorted(os.listdir(OUT)):
            if f.endswith(".png") and f[:-4] not in index:
                os.remove(os.path.join(OUT, f))
                meta = os.path.join(OUT, f + ".meta")
                if os.path.exists(meta):
                    os.remove(meta)
                removed += 1
                print(f"  x removed stale {f}")
        for f in sorted(os.listdir(OUT)):
            if f.endswith(".png.meta") and not os.path.exists(os.path.join(OUT, f[:-5])):
                os.remove(os.path.join(OUT, f))
                print(f"  x removed orphan {f}")
        if removed:
            print(f"  {removed} stale sprite(s) removed")

    with open(INDEX, "w", encoding="utf-8", newline="\n") as fh:
        # one sprite per line: easy to diff and to read, still plain JSON for JsonUtility (allow_nan=False: a NaN
        # would make the whole file unreadable in Unity, so fail here instead)
        lines = [json.dumps(index[n], separators=(",", ":"), allow_nan=False) for n in sorted(index)]
        fh.write('{"sprites":[\n' + ",\n".join(lines) + "\n]}\n")
    print(f"art_index.json: {len(index)} sprites")

    # ---------------------------------------------------------------- previews
    contact_sheet(sprites, os.path.join(PREVIEW, "processed.png"))
    nine_slice_preview(sprites, index, os.path.join(PREVIEW, "nineslice.png"))
    if bottle:
        art = {n: sprites[n] for n in BOTTLE_SPRITES}
        for n in ("cork", "stone_wrap"):
            p = os.path.join(OUT, n + ".png")
            if n in sprites:
                art[n] = sprites[n]
            elif os.path.exists(p):
                art[n] = Image.open(p).convert("RGBA")
        bottle_preview(bottle[0], bottle[1], art, os.path.join(PREVIEW, "bottles.png"))
    print(f"previews -> {os.path.relpath(PREVIEW, ROOT)}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="comma separated name prefixes to (re)process, e.g. bottle,card_")
    args = ap.parse_args()
    process([p for p in args.only.split(",")] if args.only else None)
