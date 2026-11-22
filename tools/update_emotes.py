#!/usr/bin/env python3
"""Bring the chat emoji up to date: redraw every emoji on the sheet in Google's Noto Emoji art and
add the ones Unicode has added since the sheet was made (salute, melting face, shaking face...).

The sheet is client/src/kabam/rotmg/assets/custom/images/Emotes.png, 16 x 16 cells in rows of
16; client/src/kabam/rotmg/emotes/Emotes.as says which `:code:` shows which cell. This keeps
every cell number as it is (so every existing code still works), puts the new art in the cells,
and adds a cell and `addEmote` line per new emoji at the end. Emotes that aren't plain emoji (the
bags, :rip:, :kappa:, the :bocchi: animation) are left alone, as is any cell that has no emoji.
Run tools/gen_rs_manifest.py afterwards: the Rust client reads the codes from its manifest.

    python tools/update_emotes.py --cache <folder>          # downloads what it needs into <folder>
    python tools/update_emotes.py --cache <folder> --dry-run

Inputs (downloaded into --cache): GitHub's gemoji list (the `:code:` of every emoji, MIT) and the
Noto Emoji 72 px PNGs (Apache 2.0, https://github.com/googlefonts/noto-emoji; credited in
website/wiki/credits.md). Safe to run again: codes already in Emotes.as keep their cell.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "Emotes.png")
EMOTES_AS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "emotes", "Emotes.as")
GEMOJI_URL = "https://raw.githubusercontent.com/github/gemoji/master/db/emoji.json"
NOTO_URL = "https://raw.githubusercontent.com/googlefonts/noto-emoji/main/2D/png/72/{}"
# Country and region flags aren't in Noto's emoji PNGs; its region-flags folder has them by ISO code.
FLAG_URL = "https://raw.githubusercontent.com/googlefonts/noto-emoji/main/third_party/region-flags/png/{}.png"
CELL = 16
COLUMNS = 16
# Extra codes players may type for an emoji, besides gemoji's names.
EXTRA_ALIASES = {"\U0001fae1": ["salute"]}

ADD = re.compile(r'^\s*addEmote\("((?:[^"\\]|\\.)*)",AssetLibrary\.getImageFromSet\("emotes",(\d+)\)(?:,[^)]*)?\);\s*$')


def noto_name(emoji):
    """Noto's file name: code points in hex, joined by `_`, without the variation selector."""
    return "emoji_u" + "_".join(f"{ord(c):04x}" for c in emoji if ord(c) != 0xFE0F) + ".png"


def flag_code(emoji):
    """The ISO code of a country flag (two regional indicators) or a subdivision flag (a black
    flag and tag letters: England is GB-ENG), else None."""
    pts = [ord(c) for c in emoji]
    if len(pts) == 2 and all(0x1F1E6 <= p <= 0x1F1FF for p in pts):
        return "".join(chr(p - 0x1F1E6 + 65) for p in pts)
    if pts and pts[0] == 0x1F3F4 and len(pts) > 2 and pts[-1] == 0xE007F:
        tag = "".join(chr(p - 0xE0000).upper() for p in pts[1:-1])
        return tag[:2] + "-" + tag[2:]
    return None


def fetch(url, path):
    if os.path.exists(path):
        return True
    data = None
    for _ in range(3):
        try:
            with urllib.request.urlopen(url, timeout=60) as r:
                data = r.read()
            break
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return False
        except Exception:
            pass
    if data is None:
        return False
    with open(path, "wb") as f:
        f.write(data)
    return True


def art(cache, emoji):
    """The emoji drawn 16 pixels square, or None if Noto has no picture of it."""
    path = os.path.join(cache, "noto", noto_name(emoji))
    if not os.path.exists(path):
        return None
    try:
        im = Image.open(path).convert("RGBA")
    except OSError:
        return None
    # Trim to the picture, so a narrow emoji isn't smaller than the cell for no reason, then fit it
    # in the cell keeping its shape.
    box = im.getbbox()
    if box:
        im = im.crop(box)
    w, h = im.size
    k = CELL / max(w, h)
    im = im.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)
    out = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    out.paste(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cache", required=True, help="folder for the downloaded gemoji list and Noto PNGs")
    ap.add_argument("--dry-run", action="store_true", help="report, change nothing")
    args = ap.parse_args()
    os.makedirs(os.path.join(args.cache, "noto"), exist_ok=True)

    gem_path = os.path.join(args.cache, "gemoji.json")
    if not fetch(GEMOJI_URL, gem_path):
        sys.exit("could not download the gemoji list")
    with open(gem_path, encoding="utf-8") as f:
        gemoji = json.load(f)
    by_alias = {a: e["emoji"] for e in gemoji for a in e["aliases"]}

    with open(EMOTES_AS, encoding="utf-8", newline="") as f:
        source = f.read()
    eol = "\r\n" if "\r\n" in source else "\n"
    lines = source.split(eol)

    # What the sheet has: code -> cell, and which emoji each cell shows (from its `:alias:`).
    codes = {}
    cell_emoji = {}
    last_add = -1
    for i, line in enumerate(lines):
        m = ADD.match(line)
        if not m:
            continue
        code, idx = m.group(1).replace("\\'", "'"), int(m.group(2))
        codes[code] = idx
        last_add = i
        mm = re.fullmatch(r":([^:]+):", code)
        if mm and mm.group(1) in by_alias:
            cell_emoji.setdefault(idx, by_alias[mm.group(1)])
    sheet = Image.open(SHEET).convert("RGBA")
    cells = (sheet.height // CELL) * COLUMNS
    print(f"{len(codes)} codes, {cells} cells, {len(cell_emoji)} cells map to an emoji")

    # Emoji that are new: none of their names is a code yet.
    known = set(cell_emoji.values())
    new = [e for e in gemoji if e["emoji"] not in known and not any(f":{a}:" in codes for a in e["aliases"])]
    print(f"{len(new)} new emoji")

    # Download what's needed.
    need = sorted({noto_name(e) for e in list(cell_emoji.values()) + [n["emoji"] for n in new]})
    missing_dl = []

    by_name = {noto_name(e["emoji"]): e["emoji"] for e in gemoji}

    def get(name):
        if fetch(NOTO_URL.format(name), os.path.join(args.cache, "noto", name)):
            return
        code = flag_code(by_name.get(name, ""))
        path = os.path.join(args.cache, "noto", name)
        if code and fetch(FLAG_URL.format(code), path):
            # A few flags are links to another flag's file (Bouvet Island is "NO.png").
            if os.path.getsize(path) < 100:
                with open(path, encoding="ascii", errors="replace") as f:
                    target = f.read().strip()
                os.remove(path)
                if target.endswith(".png") and fetch(FLAG_URL.format(target[:-4]), path):
                    return
            else:
                return
        missing_dl.append(name)

    if not args.dry_run:
        with ThreadPoolExecutor(16) as pool:
            list(pool.map(get, need))
    print(f"{len(need)} pictures wanted, {len(missing_dl)} not in Noto")

    # Redraw the existing cells.
    redrawn = 0
    kept = []
    for idx, emoji in sorted(cell_emoji.items()):
        im = None if args.dry_run else art(args.cache, emoji)
        if im is None:
            kept.append((idx, emoji))
            continue
        x, y = (idx % COLUMNS) * CELL, (idx // COLUMNS) * CELL
        sheet.paste((0, 0, 0, 0), (x, y, x + CELL, y + CELL))
        sheet.paste(im, (x, y))
        redrawn += 1
    print(f"{redrawn} cells redrawn; {len(kept)} kept as they were (no Noto picture)")

    # Add the new ones after the last cell.
    adds = []
    next_idx = max(cells, max(codes.values()) + 1)
    pasted = []
    for e in new:
        im = None if args.dry_run else art(args.cache, e["emoji"])
        if im is None and not args.dry_run:
            continue
        names = [f":{a}:" for a in e["aliases"]] + [f":{a}:" for a in EXTRA_ALIASES.get(e["emoji"], [])]
        names = [n for n in names if n not in codes and n not in {a for a, _ in adds}]
        if not names:
            continue
        for n in names:
            adds.append((n, next_idx))
        pasted.append((next_idx, im))
        next_idx += 1
    rows = -(-next_idx // COLUMNS)
    print(f"{len(pasted)} emoji added, {len(adds)} new codes; sheet becomes {COLUMNS * CELL} x {rows * CELL}")
    if args.dry_run:
        return

    if rows * CELL > sheet.height:
        bigger = Image.new("RGBA", (sheet.width, rows * CELL), (0, 0, 0, 0))
        bigger.paste(sheet, (0, 0))
        sheet = bigger
    for idx, im in pasted:
        sheet.paste(im, ((idx % COLUMNS) * CELL, (idx // COLUMNS) * CELL))
    sheet.save(SHEET, optimize=True)

    add_lines = [f'        addEmote("{n}",AssetLibrary.getImageFromSet("emotes",{i}));' for n, i in adds]
    lines[last_add + 1:last_add + 1] = add_lines
    with open(EMOTES_AS, "w", encoding="utf-8", newline="") as f:
        f.write(eol.join(lines))
    if missing_dl:
        print("no Noto picture for:", " ".join(missing_dl[:40]), "..." if len(missing_dl) > 40 else "")


if __name__ == "__main__":
    main()
