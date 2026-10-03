#!/usr/bin/env python3
"""Contact sheet of raw images on a checkerboard (to judge transparency).
uv run --with pillow python Tools/contact_sheet.py out.png name1 name2 ...  (or a glob prefix with --prefix)"""
import sys, os, glob
from PIL import Image, ImageDraw
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = sys.argv[1]; names = sys.argv[2:]
files = []
for n in names:
    if n.endswith("*"):
        files += sorted(glob.glob(os.path.join(ROOT, "ArtSource/raw", n + ".png")))
    else:
        files.append(os.path.join(ROOT, "ArtSource/raw", n + ".png"))
files = [f for f in files if os.path.exists(f)]
cell = 300; cols = min(6, max(1, len(files))); rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * (cell + 20)), (255, 255, 255))
d = ImageDraw.Draw(sheet)
for i, f in enumerate(files):
    im = Image.open(f).convert("RGBA"); im.thumbnail((cell - 10, cell - 10))
    x, y = (i % cols) * cell, (i // cols) * (cell + 20)
    bg = Image.new("RGBA", (cell, cell), (255, 255, 255, 255)); bd = ImageDraw.Draw(bg)
    for cy in range(0, cell, 20):
        for cx in range(0, cell, 20):
            if (cx // 20 + cy // 20) % 2: bd.rectangle([cx, cy, cx + 19, cy + 19], fill=(205, 205, 215, 255))
    bg.alpha_composite(im, ((cell - im.width) // 2, (cell - im.height) // 2))
    sheet.paste(bg.convert("RGB"), (x, y))
    a = Image.open(f)
    d.text((x + 4, y + cell + 3), f"{os.path.basename(f)[:-4]} {a.size[0]}x{a.size[1]} {a.mode}", fill=(0, 0, 0))
sheet.save(out); print(out, len(files))
