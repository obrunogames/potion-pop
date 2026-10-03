#!/usr/bin/env python3
"""Downscale QA screenshots (Screenshots/qa/*.png) to *_s.jpg for quick viewing; optional side-by-side sheet.
uv run --with pillow python Tools/qa_thumb.py name1 [name2 ...] [--sheet out]"""
import sys, os
from PIL import Image
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, "Screenshots", "qa")
args = sys.argv[1:]
sheet = None
if "--sheet" in args:
    i = args.index("--sheet"); sheet = args[i + 1]; args = args[:i] + args[i + 2:]
ims = []
for n in args:
    im = Image.open(os.path.join(D, n + ".png")).convert("RGB")
    im.thumbnail((480, 1040))
    ims.append(im)
    if not sheet:
        im.save(os.path.join(D, n + "_s.jpg"), quality=85)
if sheet:
    w = sum(i.width for i in ims) + 10 * (len(ims) - 1); h = max(i.height for i in ims)
    out = Image.new("RGB", (w, h), (30, 30, 30)); x = 0
    for i in ims:
        out.paste(i, (x, 0)); x += i.width + 10
    out.save(os.path.join(D, sheet + ".jpg"), quality=85)
print("ok")
