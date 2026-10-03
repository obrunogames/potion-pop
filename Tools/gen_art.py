#!/usr/bin/env python3
"""Generate every missing image in Tools/art_manifest.py with Codex CLI.

Usage:
    python3 Tools/gen_art.py                    # generate all missing images (priority order)
    python3 Tools/gen_art.py p_apple logo       # (re)generate specific images
    python3 Tools/gen_art.py --prefix icon_     # (re)generate everything whose name starts with icon_
    python3 Tools/gen_art.py -j 8               # parallel Codex jobs (default 8)

Each image is produced by `codex exec` using Codex's built-in image generation tool and saved to
ArtSource/raw/<name>.png. Afterwards run Tools/process_art.py to trim/convert them into Assets/_Game/Art.
"""
import argparse
import concurrent.futures as cf
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
from art_manifest import ASSETS  # noqa: E402

RAW = os.path.join(ROOT, "ArtSource", "raw")
LOG_DIR = os.path.join(ROOT, "ArtSource", "logs")
MODEL = os.environ.get("CODEX_MODEL", "gpt-5.6-sol")
EFFORT = os.environ.get("CODEX_EFFORT", "low")


def build_prompt(asset):
    out = f"ArtSource/raw/{asset['name']}.png"
    if asset["transparent"]:
        bg = ("The image MUST have a TRANSPARENT background (PNG with an alpha channel): only the subject is opaque, "
              "everything around it is fully transparent.")
        keep = " (keep the alpha transparency, do not flatten it)"
    else:
        bg = "The image is fully opaque (no transparency)."
        keep = ""
    return (
        "Use your built-in image generation tool to create exactly ONE image. "
        f"{bg} Image size {asset['size']}.\n\n"
        f"Description: {asset['prompt']}\n\n"
        f"Save the final PNG{keep} to {out} (relative to the current directory). "
        "Do not create or modify any other files. Do not draw the image with code or scripts - only use the "
        "image generation tool. If the generation fails, retry the image generation tool."
    )


def generate(asset, attempts=3):
    name = asset["name"]
    out = os.path.join(RAW, f"{name}.png")
    bak = out + ".bak"
    log_path = os.path.join(LOG_DIR, f"{name}.log")
    if os.path.exists(out):
        os.replace(out, bak)  # keep the previous image until a new one is generated successfully
    ok = False
    try:
        for attempt in range(1, attempts + 1):
            if os.path.exists(out):
                os.remove(out)
            start = time.time()
            try:
                with open(log_path, "w") as log:
                    proc = subprocess.run(
                        [
                            "codex", "exec", "--skip-git-repo-check",
                            "-m", MODEL,
                            "-c", f"model_reasoning_effort={EFFORT}",
                            "-c", "mcp_servers={}",
                            "-s", "workspace-write",
                            "-C", ROOT,
                            build_prompt(asset),
                        ],
                        stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, timeout=900,
                    )
                code = proc.returncode
            except subprocess.TimeoutExpired:
                code = "timeout"
            ok = os.path.exists(out) and os.path.getsize(out) > 10_000
            dt = time.time() - start
            if ok:
                if os.path.exists(bak):
                    os.remove(bak)
                return f"OK   {name} ({dt:.0f}s, attempt {attempt})"
            print(f"RETRY {name} (exit {code}, attempt {attempt})", flush=True)
    finally:
        if not ok:
            if os.path.exists(bak):
                os.replace(bak, out)
            elif os.path.exists(out):
                os.remove(out)
    return f"FAIL {name} (previous image kept)" if os.path.exists(out) else f"FAIL {name}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("names", nargs="*")
    ap.add_argument("--prefix", action="append", default=[])
    ap.add_argument("-j", "--jobs", type=int, default=8)
    args = ap.parse_args()

    os.makedirs(RAW, exist_ok=True)
    os.makedirs(LOG_DIR, exist_ok=True)

    if args.names or args.prefix:
        todo = [a for a in ASSETS if a["name"] in args.names or any(a["name"].startswith(p) for p in args.prefix)]
    else:
        todo = [a for a in ASSETS if not os.path.exists(os.path.join(RAW, f"{a['name']}.png"))]
    todo.sort(key=lambda a: a["priority"])

    print(f"Generating {len(todo)} image(s) with {args.jobs} parallel Codex jobs (model {MODEL})...", flush=True)
    with cf.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = [pool.submit(generate, a) for a in todo]
        for f in cf.as_completed(futures):
            try:
                print(f.result(), flush=True)
            except Exception as e:  # never abort the whole batch because of one image
                print(f"FAIL ({e})", flush=True)
    print("DONE", flush=True)


if __name__ == "__main__":
    main()
