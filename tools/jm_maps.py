"""What the map builders share: the game's ids and sprites, checking a map against them, and
writing it as a `.jm` file.

The maps themselves are built by tools/build_nexus.py (the Nexus), build_vault.py (the vault)
and build_guild_halls.py (the guild halls), all gardens from tools/build_nexus_garden.py's art.
Until 2026-10-31 this was build_castle_maps.py, which also built the castle vault.
"""
import base64
import glob
import json
import os
import re
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORLDS = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Exalt")


def inside(poly, px, py):
    hit = False
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        if (y0 > py) != (y1 > py) and px < x0 + (py - y0) * (x1 - x0) / (y1 - y0):
            hit = not hit
    return hit


_game_data = None


def game_data():
    """id -> (sheet, index) for grounds and objects, and the set of ids that exist."""
    global _game_data
    if _game_data is None:
        textures, ids = {}, set()
        for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
            text = open(f, encoding="utf-8", errors="ignore").read()
            for m in re.finditer(r'<(Ground|Object) type="[^"]+" id="([^"]+)">(.*?)</\1>', text, re.S):
                ids.add(m.group(2))
                tex = re.search(r"<File>(.*?)</File>\s*<Index>(.*?)</Index>", m.group(3))
                if tex and re.fullmatch(r"0x[0-9a-fA-F]+", tex.group(2).strip()):
                    textures.setdefault(m.group(2), (tex.group(1), int(tex.group(2), 16)))
        _game_data = textures, ids
    return _game_data


def entry(t):
    e = {}
    if t["ground"]:
        e["ground"] = t["ground"]
    if t["obj"]:
        e["objs"] = [{"id": t["obj"], "name": t["name"]} if t["name"] else {"id": t["obj"]}]
    if t["region"]:
        e["regions"] = [{"id": t["region"]}]
    return e


def save(g, path):
    entries, index, out = [], {}, bytearray()
    for y in range(g.h):
        for x in range(g.w):
            e = entry(g[x, y])
            key = json.dumps(e, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(e)
            i = index[key]
            out += bytes((i >> 8, i & 0xff))
    jm = {"data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii"),
          "dict": entries, "height": g.h, "width": g.w}
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


def check(g):
    _, ids = game_data()
    used = {t[k] for row in g.t for t in row for k in ("ground", "obj") if t[k]}
    missing = sorted(used - ids)
    if missing:
        sys.exit(f"{g.name}: not in the game data: " + ", ".join(missing))
    counts = {}
    for row in g.t:
        for t in row:
            if t["region"]:
                counts[t["region"]] = counts.get(t["region"], 0) + 1
                assert not t["obj"], f"{g.name}: an object stands on a region tile"
    return counts
