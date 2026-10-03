#!/usr/bin/env python3
"""Export the product catalog (Tools/catalog.py) to the Unity project.

    uv run python Tools/export_catalog.py

Writes:
  * Assets/_Game/Resources/catalog.json
        {"areas":[{"id":"grocery","index":0,"accent":"#2ED6A1","products":["soda_orange", ...]}, ...]}
    Read by PotionPop.Catalog with JsonUtility (arrays of plain objects only, no dictionaries).
  * Assets/_Game/Resources/Loc/catalog.csv
        key,en,pt,es  with area.<id> and product.<id> rows (RFC 4180 quoting, UTF-8 without BOM, LF line endings).

The script validates the catalog (unique ids, 12 products per area, every name in the three languages) and fails
loudly instead of exporting something broken.
"""
import csv
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
from catalog import AREAS  # noqa: E402

RESOURCES = os.path.join(ROOT, "Assets", "_Game", "Resources")
CATALOG_JSON = os.path.join(RESOURCES, "catalog.json")
LOC_CSV = os.path.join(RESOURCES, "Loc", "catalog.csv")
RAW_ART = os.path.join(ROOT, "ArtSource", "raw")

LANGS = ("en", "pt", "es")
PRODUCTS_PER_AREA = 12

# Area accent colors (Docs/DesignSystem.md §1 "Area accents").
ACCENTS = {
    "grocery": "#2ED6A1",
    "sweets": "#FF7EB6",
    "toys": "#4FB3FF",
    "beauty": "#A98BFF",
    "fresh": "#FFA94D",
}


def validate():
    errors = []
    seen_products = set()
    seen_areas = set()
    for area in AREAS:
        aid = area["id"]
        if aid in seen_areas:
            errors.append(f"duplicate area id {aid}")
        seen_areas.add(aid)
        if aid not in ACCENTS:
            errors.append(f"area {aid} has no accent color")
        for lang in LANGS:
            if not area["names"].get(lang, "").strip():
                errors.append(f"area {aid} missing name [{lang}]")
        if len(area["products"]) != PRODUCTS_PER_AREA:
            errors.append(f"area {aid} has {len(area['products'])} products (expected {PRODUCTS_PER_AREA})")
        for pid, _desc, names in area["products"]:
            if pid in seen_products:
                errors.append(f"duplicate product id {pid}")
            seen_products.add(pid)
            if pid != pid.lower() or " " in pid:
                errors.append(f"product id {pid!r} must be lower_snake_case")
            for lang in LANGS:
                if not names.get(lang, "").strip():
                    errors.append(f"product {pid} missing name [{lang}]")
            if not os.path.exists(os.path.join(RAW_ART, f"p_{pid}.png")):
                print(f"  warning: no raw art for p_{pid}")
    if errors:
        for e in errors:
            print("  ERROR:", e)
        sys.exit(1)
    return len(seen_products)


def write_json():
    data = {
        "areas": [
            {
                "id": area["id"],
                "index": i,
                "accent": ACCENTS[area["id"]],
                "products": [pid for pid, _d, _n in area["products"]],
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
        rows.append((f"area.{area['id']}",) + tuple(area["names"][l] for l in LANGS))
    for area in AREAS:
        for pid, _desc, names in area["products"]:
            rows.append((f"product.{pid}",) + tuple(names[l] for l in LANGS))
    buf = io.StringIO()
    # QUOTE_MINIMAL = RFC 4180: only cells with commas, quotes or newlines get quoted; quotes are doubled.
    writer = csv.writer(buf, quoting=csv.QUOTE_MINIMAL, lineterminator="\n")
    for row in rows:
        # A real newline inside a cell is stored as the two characters "\n" (the Loc loader expands it).
        writer.writerow([c.replace("\r\n", "\\n").replace("\n", "\\n") for c in row])
    os.makedirs(os.path.dirname(LOC_CSV), exist_ok=True)
    with open(LOC_CSV, "w", encoding="utf-8", newline="") as f:
        f.write(buf.getvalue())
    return len(rows) - 1


def main():
    n_products = validate()
    write_json()
    n_keys = write_csv()
    print(f"catalog.json: {len(AREAS)} areas, {n_products} products -> {os.path.relpath(CATALOG_JSON, ROOT)}")
    print(f"catalog.csv : {n_keys} keys x {len(LANGS)} languages -> {os.path.relpath(LOC_CSV, ROOT)}")


if __name__ == "__main__":
    main()
