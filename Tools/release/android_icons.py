"""Derive Android adaptive layers from Potion Pop's original store icon.

  uv run --with pillow python Tools/release/android_icons.py

The artwork stays unchanged. Center it in the 72dp viewport of a 108dp
adaptive layer, with a solid purple background behind the transparent margin.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "Assets/_Game/Art/AppIcon"


def main():
    original = Image.open(FOLDER / "app_icon.png").convert("RGBA")
    if original.size != (1024, 1024) or original.getextrema()[3] != (255, 255):
        raise ValueError("Expected the opaque original 1024x1024 store artwork")
    size = 1024
    viewport = round(size * 72 / 108)
    offset = (size - viewport) // 2
    foreground = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    foreground.paste(original.resize((viewport, viewport), Image.Resampling.LANCZOS), (offset, offset))
    background = Image.new("RGB", (size, size), original.getpixel((0, 0))[:3])
    foreground.save(FOLDER / "android_foreground.png", optimize=True)
    background.save(FOLDER / "android_background.png", optimize=True)
    print("Generated Android adaptive layers from the original Luna app_icon.png")


if __name__ == "__main__":
    main()
