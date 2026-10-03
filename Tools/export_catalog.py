#!/usr/bin/env python3
"""Export the world + collection card catalog (Tools/catalog.py) to the Unity project.

    uv run python Tools/export_catalog.py

Writes:
  * Assets/_Game/Resources/catalog.json
        {"areas":[{"id":"forest","index":0,"accent":"#2ED6A1","cards":["glow_mushroom", ...]}, ...]}
    Read by PotionPop.Catalog (Scripts/Core/Catalog.cs) with JsonUtility (arrays of plain objects only).
  * Assets/_Game/Resources/Loc/catalog.csv
        key,en,pt,es  with area.<id> (world names) and card.<id> (card names) rows
        (RFC 4180 quoting, UTF-8 without BOM, LF line endings; a newline inside a name is written as "\\n").

The catalog is validated first (unique lower_snake_case ids, 9 cards per world, a #RRGGBB accent, every name in the
three languages, raw art card_<id> / home_<id> / gamebg_<id> present, no key clashing with the other Loc tables) and
the script fails loudly instead of exporting something broken.
"""
import csv
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
from catalog import AREAS  # noqa: E402

RESOURCES = os.path.join(ROOT, "Assets", "_Game", "Resources")
CATALOG_JSON = os.path.join(RESOURCES, "catalog.json")
LOC_DIR = os.path.join(RESOURCES, "Loc")
LOC_CSV = os.path.join(LOC_DIR, "catalog.csv")
RAW_ART = os.path.join(ROOT, "ArtSource", "raw")

LANGS = ("en", "pt", "es")
CARDS_PER_AREA = 9
ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
HEX_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")
RESERVED_CARD_IDS = {"back", "frame"}       # card_back / card_frame are UI kit sprites, not collection cards


def other_loc_keys():
    """Keys defined by the other tables in Resources/Loc (catalog keys must not override them)."""
    keys = {}
    if not os.path.isdir(LOC_DIR):
        return keys
    for f in sorted(os.listdir(LOC_DIR)):
        if not f.endswith(".csv") or os.path.join(LOC_DIR, f) == LOC_CSV:
            continue
        with open(os.path.join(LOC_DIR, f), encoding="utf-8-sig", newline="") as fh:
            rows = list(csv.reader(fh))
        if not rows:
            continue
        col = rows[0].index("key") if "key" in rows[0] else 0
        for row in rows[1:]:
            if len(row) > col and row[col].strip():
                keys[row[col].strip()] = f
    return keys


def validate():
    errors = []
    seen_cards, seen_areas = {}, set()
    for i, area in enumerate(AREAS):
        aid = area.get("id", "")
        where = f"area #{i} ({aid or '?'})"
        if not ID_RE.match(aid):
            errors.append(f"{where}: id must be lower_snake_case")
        if aid in seen_areas:
            errors.append(f"{where}: duplicate area id")
        seen_areas.add(aid)
        if not HEX_RE.match(str(area.get("accent", ""))):
            errors.append(f"{where}: accent must be #RRGGBB (got {area.get('accent')!r})")
        names = area.get("names", {})
        for lang in LANGS:
            if not str(names.get(lang, "")).strip():
                errors.append(f"{where}: missing world name [{lang}]")
        for kind in ("home", "gamebg"):
            if not os.path.exists(os.path.join(RAW_ART, f"{kind}_{aid}.png")):
                errors.append(f"{where}: no raw art ArtSource/raw/{kind}_{aid}.png")
        cards = area.get("cards", [])
        if len(cards) != CARDS_PER_AREA:
            errors.append(f"{where}: {len(cards)} cards (expected {CARDS_PER_AREA})")
        for card in cards:
            if len(card) != 3:
                errors.append(f"{where}: card entry {card!r} must be (id, prompt, names)")
                continue
            cid, _desc, cnames = card
            if not ID_RE.match(cid):
                errors.append(f"card {cid!r} ({aid}): id must be lower_snake_case")
            if cid in RESERVED_CARD_IDS:
                errors.append(f"card {cid!r} ({aid}): id clashes with the UI kit sprite card_{cid}")
            if cid in seen_cards:
                errors.append(f"card {cid!r}: duplicate id (worlds {seen_cards[cid]} and {aid})")
            seen_cards[cid] = aid
            for lang in LANGS:
                if not str(cnames.get(lang, "")).strip():
                    errors.append(f"card {cid!r} ({aid}): missing name [{lang}]")
            if not os.path.exists(os.path.join(RAW_ART, f"card_{cid}.png")):
                errors.append(f"card {cid!r} ({aid}): no raw art ArtSource/raw/card_{cid}.png")
    taken = other_loc_keys()
    for key in [f"area.{a['id']}" for a in AREAS] + [f"card.{c}" for c in seen_cards]:
        if key in taken:
            errors.append(f"loc key {key} is already defined in Loc/{taken[key]}")
    if errors:
        for e in errors:
            print("  ERROR:", e)
        sys.exit(1)
    return len(seen_cards)


def write_json():
    data = {
        "areas": [
            {
                "id": area["id"],
                "index": i,
                "accent": area["accent"].upper(),
                "cards": [cid for cid, _d, _n in area["cards"]],
            }
            for i, area in enumerate(AREAS)
        ]
    }
    os.makedirs(os.path.dirname(CATALOG_JSON), exist_ok=True)
    with open(CATALOG_JSON, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def write_csv():
    rows = [("key",) + LANGS]
    for area in AREAS:
        rows.append((f"area.{area['id']}",) + tuple(area["names"][l].strip() for l in LANGS))
    for area in AREAS:
        for cid, _desc, names in area["cards"]:
            rows.append((f"card.{cid}",) + tuple(names[l].strip() for l in LANGS))
    buf = io.StringIO()
    # QUOTE_MINIMAL = RFC 4180: only cells with commas, quotes or newlines get quoted; quotes are doubled.
    writer = csv.writer(buf, quoting=csv.QUOTE_MINIMAL, lineterminator="\n")
    for row in rows:
        # A real newline inside a cell is stored as the two characters "\n" (the Loc loader expands it).
        writer.writerow([c.replace("\r\n", "\\n").replace("\n", "\\n") for c in row])
    os.makedirs(os.path.dirname(LOC_CSV), exist_ok=True)
    with open(LOC_CSV, "w", encoding="utf-8", newline="") as f:   # "utf-8" never writes a BOM
        f.write(buf.getvalue())
    return len(rows) - 1


def main():
    n_cards = validate()
    write_json()
    n_keys = write_csv()
    print(f"catalog.json: {len(AREAS)} worlds, {n_cards} cards -> {os.path.relpath(CATALOG_JSON, ROOT)}")
    print(f"catalog.csv : {n_keys} keys x {len(LANGS)} languages -> {os.path.relpath(LOC_CSV, ROOT)}")


if __name__ == "__main__":
    main()
