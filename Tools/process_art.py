#!/usr/bin/env python3
"""Turn the raw Codex images (ArtSource/raw) into game-ready sprites for Potion Pop!.

    uv run --with pillow --with numpy --with scipy python Tools/process_art.py [--only prefix1,prefix2]

Pipeline per image (see RULES below for the per-group sizes):
  1. images that should be transparent but came back opaque get their background removed (corner-color flood fill);
  2. alpha cleanup: alpha < 12 -> 0, alpha >= 248 -> 255 (the generator writes "opaque" as 253); glow sprites
     (sunburst, sparkle, ice frame) roll their faint halo off smoothly instead of cutting it (no visible contour);
  3. defringe: semi-transparent edge pixels take the color of the nearest solid pixel in proportion to their
     transparency (removes light/dark matte halos while keeping the antialiasing);
  4. trim transparent borders, resize with LANCZOS (premultiplied), add a 2 px transparent pad;
  5. transparent pixels get the color of the nearest visible pixel (no dark fringes with bilinear filtering / mips).

Outputs:
  Assets/_Game/Resources/Art/<name>.png          flat folder, one sprite per raw image + procedural ui_* primitives
  Assets/_Game/Resources/Art/art_index.json      {"sprites":[{"name","w","h","border":[l,b,r,t],"pivot":[x,y],
                                                  "inner":[x,y,w,h]}]}  (border in output pixels, Unity order
                                                  left/bottom/right/top; inner normalized, origin bottom-left)
  Assets/_Game/Art/AppIcon/app_icon.png          1024x1024 opaque app icon
  ArtSource/preview/processed.png                contact sheet of every processed sprite on a checkerboard
  ArtSource/preview/nineslice.png                every 9-sliced sprite stretched to other aspect ratios
  ArtSource/preview/cubby_<name>.png             detected cubby interior rect + sample products standing on it
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
from art_manifest import ASSETS  # noqa: E402

RAW = os.path.join(ROOT, "ArtSource", "raw")
PREVIEW = os.path.join(ROOT, "ArtSource", "preview")
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "Art")
APP_ICON_DIR = os.path.join(ROOT, "Assets", "_Game", "Art", "AppIcon")
INDEX = os.path.join(OUT, "art_index.json")

ALPHA_CUT = 12        # alpha below this is treated as fully transparent (and trimmed)
ALPHA_SOLID = 248     # alpha at/above this becomes 255
GLOW_FLOOR = 2        # glow sprites: alpha at/below this is noise (0); between it and ALPHA_CUT a smooth roll-off
PAD = 2               # transparent pad (output pixels) around trimmed sprites

FULL_RECT = [0.0, 0.0, 1.0, 1.0]
CENTER = [0.5, 0.5]
CUBBY_FALLBACK_INNER = [0.06, 0.12, 0.88, 0.78]   # same as Art.DefaultInner (Scripts/Core/Art.cs)


# ============================================================================================ rules

class Rule:
    """How one sprite is processed.

    fit: ("long", n)   longest side = n          ("w", n) width = n        ("h", n) height = n
         ("exact", w, h) exact size (no trim)    ("keep",) keep raw size (backgrounds, no trim)
    trim: "bbox" (alpha bounding box), "center" (symmetric around the image center: keeps rotation pivots),
          "square" (bbox, then padded to a square around its center) or None.
    glow: mostly semi-transparent by design (light rays, sparkles, ice): no defringe.
    """

    def __init__(self, fit, trim="bbox", glow=False, opaque=False):
        self.fit, self.trim, self.glow, self.opaque = fit, trim, glow, opaque


def rule_for(name):
    if name.startswith(("home_", "gamebg_")):
        return Rule(("exact", 1024, 1536), trim=None, opaque=True)
    if name == "app_icon":
        return Rule(("exact", 256, 256), trim=None, opaque=True)   # small in-game copy; full size goes to AppIcon/
    if name.startswith("p_"):
        return Rule(("long", 512))
    if name.startswith("cubby3_"):
        return Rule(("w", 1024))
    if name.startswith("cubby1_"):
        return Rule(("w", 512))
    if name == "lock_chains":
        return Rule(("w", 1024))      # overlays a cubby3 at the same width
    if name == "lock_chains_tall":
        return Rule(("w", 512))       # overlays a cubby1
    if name in ("btn_square", "btn_round_close"):
        return Rule(("long", 256))
    if name.startswith("btn_"):
        return Rule(("w", 768))
    if name.startswith(("icon_", "booster_", "chest_", "coins_")) or name in ("hearts_refill", "booster_pack"):
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
    if name.startswith("card_"):
        return Rule(("w", 384))
    if name == "sunburst":
        return Rule(("long", 512), trim="center", glow=True)
    if name == "fx_sparkle":
        return Rule(("long", 128), trim="center", glow=True)
    if name == "fx_poof":
        return Rule(("long", 256), trim="center")
    if name == "fx_ice_frame":
        return Rule(("exact", 1080, 1620), trim=None, glow=True)
    if name == "logo":
        return Rule(("w", 1024))
    if name == "hand_pointer":
        return Rule(("long", 256))
    print(f"  ? no rule for {name}, using long side 512")
    return Rule(("long", 512))


# ============================================================================================ pixel helpers

def to_array(im):
    return np.asarray(im.convert("RGBA"), dtype=np.float32).copy()


def to_image(arr):
    return Image.fromarray(np.clip(np.rint(arr), 0, 255).astype(np.uint8), "RGBA")


def rgb_to_hsv(rgb):
    """Vectorized RGB (0..255, [...,3]) -> hue degrees, saturation 0..1, value 0..255."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rr = nz & (mx == r)
    gg = nz & (mx == g) & ~rr
    bb = nz & ~rr & ~gg
    h[rr] = ((g - b)[rr] / d[rr]) % 6
    h[gg] = (b - r)[gg] / d[gg] + 2
    h[bb] = (r - g)[bb] / d[bb] + 4
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h * 60.0, s, mx


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

    soft (glows: light rays, sparkles, ice): the faint outer halo spans thousands of pixels with alpha 1..11, so a
    hard cut would draw a visible jagged contour on dark backgrounds (e.g. the sunburst over the popup dim). There
    the tail rolls off smoothly instead: a * smoothstep(GLOW_FLOOR, ALPHA_CUT, a) — continuous (value and slope) at
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


class Fit:
    """Maps raw (pre-trim) pixel coordinates to output coordinates: out = (raw - crop_origin) * scale + pad."""

    def __init__(self, crop, scale, pad, out_size):
        self.cx, self.cy = crop[0], crop[1]
        self.s, self.pad = scale, pad
        self.w, self.h = out_size

    def x(self, rx):
        return (rx - self.cx) * self.s + self.pad

    def y(self, ry):
        return (ry - self.cy) * self.s + self.pad


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
    """Trim + resize + pad according to the rule. Returns (output array, Fit transform)."""
    box = crop_box(arr, rule.trim)
    src = crop_padded(arr, box)
    sh, sw = src.shape[:2]
    kind = rule.fit[0]
    if kind == "exact":
        tw, th = rule.fit[1], rule.fit[2]
        im = to_image(src).resize((tw, th), Image.LANCZOS)
        return to_array(im), Fit(box, tw / sw, 0, (tw, th))
    if kind == "keep":
        return src, Fit(box, 1.0, 0, (sw, sh))
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
    return out, Fit(box, iw / sw, pad, (iw + 2 * pad, ih + 2 * pad))


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
        return border, inner
    if name == "card_frame":
        border, inner = frame_borders(arr, tol=26, use_outer=False)
        if border is None:
            return [round(0.22 * W)] * 4, FULL_RECT
        print(f"    card_frame measured border {border} ({border[0] / W:.1%} of width)")
        return clamp_border(border, 0.12, 0.30, W, H, name), inner
    return [0, 0, 0, 0], FULL_RECT


# ============================================================================================ cubby interior

def valid_inner(r):
    """Plausible normalized interior rect (finite, inside the sprite, at least 30% of each dimension)."""
    return (r is not None and len(r) == 4 and all(math.isfinite(v) for v in r) and r[0] >= 0 and r[1] >= 0
            and r[2] >= 0.3 and r[3] >= 0.3 and r[0] + r[2] <= 1.0001 and r[1] + r[3] <= 1.0001)


def detect_cubby_inner(raw, fit, name, log):
    """Finds the empty interior of a cubby (where products stand) in raw (trimmed source) pixels and returns it
    normalized to the output sprite (x, y, w, h; origin bottom-left) plus debug lines for the preview.

    left/right: inner faces of the white side walls (first saturated pixel from each side at mid height),
                cross-checked against the width of the floor board's front band;
    top:        just below the LED strip (at the back edge of the ceiling when its shadow line is visible);
    bottom:     the standing line on the visible top surface of the floor board: the front edge (start of the
                front band / bevel highlight) moved 30% of the top surface depth toward the back."""
    rgb = raw[..., :3]
    alpha = raw[..., 3]
    ys, xs = np.nonzero(alpha > 128)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    bw, bh = x1 - x0, y1 - y0
    c0, c1 = int(x0 + bw * 0.35), int(x0 + bw * 0.65)
    rows = np.median(rgb[:, c0:c1], axis=1)                     # (H, 3) median color per row
    L = rows.mean(1)
    C = rows.max(1) - rows.min(1)
    hue, sat, _ = rgb_to_hsv(rows)

    def is_frame(y):
        return C[y] < 14 and L[y] > 190

    def is_band(y):                                              # front band of the floor board: saturated wood
        return 12 <= hue[y] <= 50 and sat[y] > 0.33 and L[y] < 200

    # ---------------------------------------------------------------- top
    y = y0
    while y < y0 + bh * 0.2 and not is_frame(y):
        y += 1
    while y < y0 + bh * 0.3 and is_frame(y):
        y += 1
    frame_end = y
    led0 = led1 = None
    y = frame_end
    while y < y0 + bh * 0.3:
        if L[y] >= 248 and C[y] < 30:
            led0 = y
            while y < y1 and L[y] >= 244:
                y += 1
            led1 = y
            break
        y += 1
    if led1 is None:
        led1 = frame_end
        log.append("no LED strip found, using the frame edge")
    win = int(bh * 0.06)
    Ls = ndimage.uniform_filter1d(L, 3)
    seg = Ls[led1:led1 + win]
    k = int(np.argmin(seg))
    after = Ls[led1 + k:led1 + win].max() if k < len(seg) - 1 else Ls[led1 + k]
    if 1 < k < len(seg) - 2 and after - seg[k] > 8:
        top = led1 + k + 2
        top_how = f"ceiling back edge (shadow line, dip {after - seg[k]:.0f})"
    else:
        top = led1 + int(bh * 0.015)
        top_how = "LED strip + margin"

    # ---------------------------------------------------------------- floor: front band and highlight
    y = y1
    while y > y0 + bh * 0.5 and not is_band(y):
        y -= 1
    band_bottom = y
    gap = 0
    while y > y0 + bh * 0.5:                                     # climb through the band (tolerate grain rows)
        if is_band(y):
            gap = 0
        else:
            gap += 1
            if gap > 2:
                break
        y -= 1
    band_top = y + gap + 1                                        # first band row below the bevel highlight
    # The rounded front edge of the board shows a bright bevel highlight between the top surface and the band:
    # its top is the front edge of the surface products stand on.
    w0, w1 = band_top - max(4, int(bh * 0.035)), band_top + max(2, int(bh * 0.01))
    peak = w0 + int(np.argmax(L[w0:w1]))
    surf_L = np.median(L[max(w0 - int(bh * 0.02), 0):w0])
    if L[peak] > surf_L + 12:
        thr = (L[peak] + surf_L) / 2
        y = peak
        while y > w0 - int(bh * 0.02) and L[y - 1] > thr:
            y -= 1
        front_edge = y
    else:
        front_edge = band_top

    # Top surface of the board up to the back wall. Color alone is ambiguous (warm-lit peach walls look like wood),
    # but wood has horizontal grain: the mean absolute horizontal high-pass of the luminance is ~2-3x higher on
    # the wood than on the smooth painted wall.
    lum = rgb.mean(2)
    grain = np.abs(lum[:, c0:c1] - ndimage.uniform_filter1d(lum[:, c0:c1], 15, axis=1)).mean(1)
    grain = ndimage.median_filter(grain, 5)
    wood_tex = np.median(grain[max(front_edge - int(bh * 0.025), 0):front_edge - 2])
    wall_tex = np.median(grain[int(y0 + bh * 0.4):int(y0 + bh * 0.6)])
    back_edge = None
    if wood_tex > wall_tex * 1.4:
        thr = math.sqrt(wood_tex * wall_tex)
        y = front_edge - 3
        while y > y0 + bh * 0.4 and grain[y] > thr:
            y -= 1
        back_edge = y + 1
        # snap to the shadow line where the wall meets the floor (strongest vertical luminance step nearby)
        dv = np.abs(np.diff(L))
        j0, j1 = max(back_edge - int(bh * 0.012), 0), back_edge + int(bh * 0.012)
        back_edge = j0 + int(np.argmax(dv[j0:j1])) + 1
    if back_edge is None or front_edge - back_edge < bh * 0.01:
        back_edge = front_edge - int(bh * 0.05)
        log.append("floor back edge not found, assuming 5% depth")
    bottom = front_edge - 0.30 * (front_edge - back_edge)

    # ---------------------------------------------------------------- left / right
    band_rows = range(int(band_top + (band_bottom - band_top) * 0.3), int(band_top + (band_bottom - band_top) * 0.7) + 1)
    lefts, rights = [], []
    for ry in band_rows:
        line = rgb[ry]
        h, s, _ = rgb_to_hsv(line)
        wood = (h >= 12) & (h <= 50) & (s > 0.3) & (alpha[ry] > 200)
        wood = ndimage.binary_opening(wood, structure=np.ones(5))
        idx = np.nonzero(wood)[0]
        if len(idx):
            lefts.append(idx.min())
            rights.append(idx.max() + 1)
    left = float(np.median(lefts))
    right = float(np.median(rights))

    # cross-check at mid height: first saturated (non-frame) pixel from each side
    mid_l, mid_r = [], []
    for ry in range(int(y0 + bh * 0.4), int(y0 + bh * 0.6), 4):
        ch = rgb[ry].max(1) - rgb[ry].min(1)
        col = (ch > 18) & (alpha[ry] > 200)
        col = ndimage.binary_opening(col, structure=np.ones(5))
        idx = np.nonzero(col)[0]
        if len(idx):
            mid_l.append(idx.min())
            mid_r.append(idx.max() + 1)
    ml, mr = float(np.median(mid_l)), float(np.median(mid_r))
    if abs(ml - left) > bw * 0.01 or abs(mr - right) > bw * 0.01:
        log.append(f"note: floor board spans {left:.0f}/{right:.0f}, side walls {ml:.0f}/{mr:.0f} (using the walls)")
    left, right = ml, mr

    log.append(f"frame_end={frame_end} led={led0}-{led1} top={top} ({top_how}) back_edge={back_edge} "
               f"front_edge={front_edge} band={band_top}-{band_bottom} bottom={bottom:.0f} left={left:.0f} "
               f"right={right:.0f} (mid {ml:.0f}/{mr:.0f})")

    # map to output pixels, then normalize (origin bottom-left)
    ox0, ox1 = fit.x(left), fit.x(right)
    oy_top, oy_bot = fit.y(top), fit.y(bottom)
    W, H = fit.w, fit.h
    inner = [ox0 / W, (H - oy_bot) / H, (ox1 - ox0) / W, (oy_bot - oy_top) / H]
    debug = {"front_edge": fit.y(front_edge), "back_edge": fit.y(back_edge), "led": fit.y(led1)}
    return [round(v, 4) for v in inner], debug


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
        # white procedural primitives are shown on a dark checkerboard
        tile = checker(cell, cell, dark=n.startswith("ui_") and n != "ui_vignette")
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
    items = [(n, e) for n, e in sorted(index.items()) if any(e["border"])]
    tiles = []
    f = font(14)
    for n, e in items:
        im = sprites[n]
        W, H = im.size
        variants = [(W, H), (int(W * 1.7), H), (W, int(H * 1.6)), (int(W * 0.75), int(H * 0.8))]
        ws = sum(v[0] for v in variants) + 20 * (len(variants) + 1)
        hs = max(v[1] for v in variants) + 60
        tile = checker(ws, hs, 16)
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
        d.text((10, 10), f"{n}  border l,b,r,t = {e['border']}", fill=(0, 0, 0, 255), font=f)
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


def cubby_preview(name, im, inner, debug, products, path):
    """Draws the inner rect and stands sample products on it (3 slots for cubby3, 1 for cubby1) the way the game
    lays them out (Docs/DesignSystem.md §7: bottom-aligned, fit inside 92% x 88% of the slot)."""
    W, H = im.size
    canvas = checker(W + 40, H + 40, 16)
    canvas.alpha_composite(im, (20, 20))
    x, yb, w, h = inner
    px0, px1 = 20 + x * W, 20 + (x + w) * W
    py_bot, py_top = 20 + (1 - yb) * H, 20 + (1 - yb - h) * H
    slots = 3 if name.startswith("cubby3") else 1
    sw = (px1 - px0) / slots
    for i in range(slots):
        p = products[i % len(products)]
        bw, bh = sw * 0.92, (py_bot - py_top) * 0.88
        s = min(bw / p.width, bh / p.height)
        pi = p.resize((max(1, int(p.width * s)), max(1, int(p.height * s))), Image.LANCZOS)
        cx = px0 + sw * (i + 0.5)
        canvas.alpha_composite(pi, (int(cx - pi.width / 2), int(py_bot - pi.height)))
    d = ImageDraw.Draw(canvas)
    d.rectangle((px0, py_top, px1, py_bot), outline=(255, 0, 90, 255), width=2)
    for key, col in (("front_edge", (0, 160, 255, 255)), ("back_edge", (0, 200, 90, 255)), ("led", (255, 160, 0, 255))):
        yy = 20 + debug[key]
        d.line((20, yy, 20 + W * 0.08, yy), fill=col, width=3)
    d.text((24, 2), f"{name} inner={inner}", fill=(0, 0, 0, 255), font=font(13))
    canvas.convert("RGB").save(path, optimize=True)


# ============================================================================================ main

def process(only=None):
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PREVIEW, exist_ok=True)
    os.makedirs(APP_ICON_DIR, exist_ok=True)

    index = {}
    sprites = {}
    cubby_debug = {}
    manifest_names = {a["name"] for a in ASSETS}
    for extra in sorted(f[:-4] for f in os.listdir(RAW) if f.endswith(".png") and f[:-4] not in manifest_names):
        print(f"  ? {extra}.png is not in the manifest (skipped)")

    for asset in ASSETS:
        name = asset["name"]
        if only and not any(name.startswith(p) for p in only):
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

        out, fit = fit_image(arr, rule)
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
        if name.startswith(("cubby3_", "cubby1_")):
            log = []
            try:
                inner, dbg = detect_cubby_inner(arr, fit, name, log)
            except (ValueError, IndexError) as e:      # e.g. no wood/frame pixels at all: unexpected art
                inner, dbg = None, None
                log.append(f"detection failed: {e}")
            for line in log:
                print(f"    {line}")
            if inner is None or not valid_inner(inner):
                print(f"  ! {name}: cubby interior not detected ({inner}); using the default rect: CHECK THE ART")
                inner = list(CUBBY_FALLBACK_INNER)
            elif dbg is not None:
                cubby_debug[name] = dbg
        img = to_image(out)
        if rule.opaque:
            img.convert("RGB").save(os.path.join(OUT, name + ".png"), optimize=True)
        else:
            img.save(os.path.join(OUT, name + ".png"), optimize=True)
        sprites[name] = img
        index[name] = {"name": name, "w": img.width, "h": img.height, "border": [int(v) for v in border],
                       "pivot": CENTER, "inner": [round(float(v), 4) for v in inner]}
        extra = f" border={border}" if any(border) else ""
        extra += f" inner={index[name]['inner']}" if inner != FULL_RECT else ""
        print(f"  {name:20s} {raw_im.size[0]}x{raw_im.size[1]} -> {img.width}x{img.height}{extra}")

    for name, (arr, border) in make_primitives().items():
        if only and not any(name.startswith(p) for p in only):
            continue
        img = to_image(arr)
        img.save(os.path.join(OUT, name + ".png"), optimize=True)
        sprites[name] = img
        index[name] = {"name": name, "w": img.width, "h": img.height, "border": border,
                       "pivot": CENTER, "inner": FULL_RECT}
        print(f"  {name:20s} procedural {img.width}x{img.height} border={border}")

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
        # remove stale sprites left over from renamed/removed assets so Resources only ships what the index lists.
        for f in os.listdir(OUT):
            if f.endswith(".png") and f[:-4] not in index:
                print(f"  x removing stale {f}")
                os.remove(os.path.join(OUT, f))
                meta = os.path.join(OUT, f + ".meta")
                if os.path.exists(meta):
                    os.remove(meta)

    with open(INDEX, "w", encoding="utf-8", newline="\n") as fh:
        # one sprite per line: easy to diff and to read, still plain JSON for JsonUtility (allow_nan=False: a NaN
        # would make the whole file unreadable in Unity, so fail here instead)
        lines = [json.dumps(index[n], separators=(",", ":"), allow_nan=False) for n in sorted(index)]
        fh.write('{"sprites":[\n' + ",\n".join(lines) + "\n]}\n")
    print(f"art_index.json: {len(index)} sprites")

    # previews
    contact_sheet(sprites, os.path.join(PREVIEW, "processed.png"))
    nine_slice_preview(sprites, index, os.path.join(PREVIEW, "nineslice.png"))
    samples = [sprites[n] for n in ("p_soda_orange", "p_cupcake", "p_teddy_bear") if n in sprites]
    if not samples:
        samples = [Image.open(os.path.join(OUT, n + ".png")).convert("RGBA")
                   for n in ("p_soda_orange", "p_cupcake", "p_teddy_bear") if os.path.exists(os.path.join(OUT, n + ".png"))]
    for name, dbg in cubby_debug.items():
        if samples:
            cubby_preview(name, sprites[name], index[name]["inner"], dbg, samples,
                          os.path.join(PREVIEW, f"cubby_{name}.png"))
    print(f"previews -> {os.path.relpath(PREVIEW, ROOT)}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="comma separated name prefixes to (re)process, e.g. cubby,btn_")
    args = ap.parse_args()
    process([p for p in args.only.split(",")] if args.only else None)
