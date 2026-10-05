"""Offline AAB/APK icon validation against the original Potion Pop artwork.

  uv run --with pillow python Tools/release/validate_android_icons.py build.aab --code 2 --extract-dir /tmp/icons

Used by build_release.py before accepting an Android build. Checks packaged pixels,
bundle structure, manifest identity, and AAB signature using Unity's bundled JDK/tools.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

from PIL import Image, ImageChops, ImageStat

ROOT = Path(__file__).resolve().parents[2]
ANDROID = Path("/Applications/Unity/Hub/Editor/6000.6.3f1/PlaybackEngines/AndroidPlayer")
ANDROID_NS = "{http://schemas.android.com/apk/res/android}"


def run(*args):
    result = subprocess.run([str(a) for a in args], capture_output=True, text=True)
    if result.returncode:
        raise ValueError(f"Android validation tool failed: {Path(str(args[0])).name}: {result.stderr[-1000:]}")
    return result.stdout


def validate(path, code=None, extract_dir=None):
    path = Path(path)
    art = ROOT / "Assets/_Game/Art/AppIcon"
    expected = {"app_icon.png": "app_icon.png", "ic_launcher_foreground.png": "android_foreground.png",
                "ic_launcher_background.png": "android_background.png"}
    references = {name: Image.open(art / source).convert("RGBA") for name, source in expected.items()}
    counts = {name: 0 for name in expected}
    icons = []
    with zipfile.ZipFile(path) as bundle:
        for entry in bundle.namelist():
            name = Path(entry).name
            if name not in expected or "/res/mipmap-" not in "/" + entry:
                continue
            pixels = bundle.read(entry)
            actual = Image.open(io.BytesIO(pixels)).convert("RGBA")
            # Legacy fallback uses point-sampled bilinear GetPixelBilinear, while
            # Unity imports adaptive layers with a downsampling filter.
            source = references[name]
            reference = (source.transform(actual.size, Image.Transform.EXTENT,
                         (0, 0, *source.size), Image.Resampling.BILINEAR) if name == "app_icon.png"
                         else source.resize(actual.size, Image.Resampling.LANCZOS))
            # Unity's icon resampling/import compression can differ slightly from Pillow.
            error = max(ImageStat.Stat(ImageChops.difference(actual, reference)).mean)
            if error > 15:
                raise ValueError(f"Packaged {entry} does not match the original Luna artwork (pixel error {error:.2f})")
            if name == "app_icon.png" and actual.getextrema()[3] != (255, 255):
                raise ValueError(f"Legacy icon is not opaque: {entry}")
            if name == "ic_launcher_foreground.png" and actual.getextrema()[3] != (0, 255):
                raise ValueError(f"Adaptive foreground lacks transparent margin: {entry}")
            counts[name] += 1
            icons.append({"entry": entry, "size": list(actual.size), "pixel_error": round(error, 3)})
            if extract_dir:
                output = Path(extract_dir) / entry
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_bytes(pixels)
        if any(count != 6 for count in counts.values()):
            raise ValueError(f"Expected six densities of each packaged launcher layer: {counts}")
        adaptive = ("base/" if path.suffix == ".aab" else "") + "res/mipmap-anydpi-v26/app_icon.xml"
        if adaptive not in bundle.namelist():
            raise ValueError("Missing adaptive launcher XML")

    report = {"artifact": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "icons": icons}
    if path.suffix == ".aab":
        java = ANDROID / "OpenJDK/bin/java"
        tool = ANDROID / "Tools/bundletool-all-1.17.2.jar"
        run(java, "-jar", tool, "validate", "--bundle=" + str(path))
        manifest_text = run(java, "-jar", tool, "dump", "manifest", "--bundle=" + str(path), "--module=base")
        manifest = ET.fromstring(manifest_text)
        package = manifest.get("package")
        version_code = manifest.get(ANDROID_NS + "versionCode")
        if package != "br.com.brunogames.potionpop" or (code is not None and version_code != str(code)):
            raise ValueError(f"Unexpected Android identity: {package}, code {version_code}")
        application = manifest.find("application")
        if application is None or application.get(ANDROID_NS + "icon") != "@mipmap/app_icon":
            raise ValueError("Manifest does not use the validated app_icon launcher")
        verified = run(ANDROID / "OpenJDK/bin/jarsigner", "-J-Duser.language=en", "-verify", path)
        if "jar verified." not in verified:
            raise ValueError("AAB signature was not verified")
        report.update(package=package, version_code=version_code, version_name=manifest.get(ANDROID_NS + "versionName"),
                      icon=application.get(ANDROID_NS + "icon"), signature_verified=True)
        if extract_dir:
            (Path(extract_dir) / "manifest.xml").write_text(manifest_text)
    return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("artifact", type=Path)
    parser.add_argument("--code", type=int)
    parser.add_argument("--extract-dir", type=Path)
    args = parser.parse_args()
    try:
        report = validate(args.artifact, args.code, args.extract_dir)
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error)) from None
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
