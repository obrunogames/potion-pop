"""Builds of Potion Pop! from a copy of the project, so they run while the editor has the project open.

Usage:
  python3 Tools/release/build_release.py android-dev   # development APK -> Builds/dev/PotionPop-<version>-dev.apk
  python3 Tools/release/build_release.py android       # signed AAB -> Builds/release/PotionPop-<version>-<code>.aab
  python3 Tools/release/build_release.py ios           # Xcode project -> archive -> upload to App Store Connect

Release builds refuse Google's test AdMob ids (BuildIdentityGuard): create the real ad units first. Android signing comes
from Keystore/release-signing.json (keystore, keystorePass, alias, keyPass), passed to Unity through the
POTIONPOP_KEYSTORE* environment variables; nothing is printed. The iOS upload signs with the Apple account logged into
Xcode (Settings > Accounts). Set POTIONPOP_BUILD_COPY to move the copy (default: /tmp/potionpop-build).
"""
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
COPY = os.environ.get("POTIONPOP_BUILD_COPY", "/tmp/potionpop-build")
UNITY = "/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity"
TEAM = "XT3CTXMM6Z"


def sync():
    os.makedirs(COPY, exist_ok=True)
    first = not os.path.isdir(os.path.join(COPY, "Library"))
    subprocess.check_call(["rsync", "-a", "--delete", *(os.path.join(ROOT, d) for d in ("Assets", "Packages", "ProjectSettings")), COPY + "/"])
    if first and os.path.isdir(os.path.join(ROOT, "Library")):
        # Seed the copy's Library once (much faster first import); later builds keep the copy's own Library.
        subprocess.call(["rsync", "-a", "--exclude", "Bee/", "--exclude", "BuildPlayerData/", "--exclude", "*.lock",
                         os.path.join(ROOT, "Library") + "/", os.path.join(COPY, "Library") + "/"])


def unity(target, method, log, env=None, extra=()):
    code = subprocess.call([UNITY, "-batchmode", "-quit", "-nographics", "-projectPath", COPY, "-buildTarget", target,
                            "-executeMethod", method, "-logFile", log, *extra], env=env)
    if code != 0:
        sys.exit(f"Unity build failed ({code}); see {log}")


def version():
    settings = open(os.path.join(ROOT, "ProjectSettings/ProjectSettings.asset")).read()
    name = re.search(r"^  bundleVersion: (.+)$", settings, re.M).group(1).strip()
    code = re.search(r"^  AndroidBundleVersionCode: (\d+)$", settings, re.M).group(1)
    return name, code


def signing_env():
    cfg = json.load(open(os.path.join(ROOT, "Keystore/release-signing.json")))
    keystore = cfg["keystore"] if os.path.isabs(cfg["keystore"]) else os.path.join(ROOT, cfg["keystore"])
    return dict(os.environ, POTIONPOP_KEYSTORE=keystore, POTIONPOP_KEYSTORE_PASS=cfg["keystorePass"],
                POTIONPOP_KEY_ALIAS=cfg["alias"], POTIONPOP_KEY_PASS=cfg["keyPass"])


def android_dev():
    out_in_copy = os.path.join(COPY, "Builds/Android/PotionPop-dev.apk")
    unity("Android", "PotionPop.EditorTools.BuildScript.BuildAndroidApk", os.path.join(COPY, "android_dev_build.log"),
          extra=("-development", "-buildOutput", out_in_copy))
    name, _ = version()
    out = os.path.join(ROOT, "Builds/dev", f"PotionPop-{name}-dev.apk")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    shutil.copy(out_in_copy, out)
    print(out)


def android():
    unity("Android", "PotionPop.EditorTools.BuildScript.BuildAndroidAab", os.path.join(COPY, "android_build.log"), signing_env())
    name, code = version()
    out = os.path.join(ROOT, "Builds/release", f"PotionPop-{name}-{code}.aab")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    shutil.copy(os.path.join(COPY, "Builds/Android/PotionPop.aab"), out)
    print(out)


def ios():
    project = os.path.join(COPY, "Builds/iOS")
    shutil.rmtree(project, ignore_errors=True)
    unity("iOS", "PotionPop.EditorTools.BuildScript.BuildIOS", os.path.join(COPY, "ios_build.log"))
    archive = os.path.join(COPY, "PotionPop.xcarchive")
    shutil.rmtree(archive, ignore_errors=True)
    subprocess.check_call(["xcodebuild", "-workspace", os.path.join(project, "Unity-iPhone.xcworkspace"), "-scheme", "Unity-iPhone",
                           "-configuration", "Release", "-destination", "generic/platform=iOS", "-archivePath", archive, "archive",
                           f"DEVELOPMENT_TEAM={TEAM}", "CODE_SIGN_STYLE=Automatic", "-allowProvisioningUpdates", "-quiet"])
    options = os.path.join(COPY, "ExportOptions.plist")
    with open(options, "w") as f:
        f.write(f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>method</key><string>app-store-connect</string>
  <key>destination</key><string>upload</string>
  <key>teamID</key><string>{TEAM}</string>
  <key>signingStyle</key><string>automatic</string>
  <key>manageAppVersionAndBuildNumber</key><false/>
</dict></plist>
""")
    subprocess.check_call(["xcodebuild", "-exportArchive", "-archivePath", archive, "-exportOptionsPlist", options,
                           "-exportPath", os.path.join(COPY, "ios_export"), "-allowProvisioningUpdates"])


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else ""
    if what not in ("android-dev", "android", "ios"):
        sys.exit(__doc__)
    sync()
    {"android-dev": android_dev, "android": android, "ios": ios}[what]()
