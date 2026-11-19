"""Builds the Nexus Bazaar on the nexus map's south island.

    python tools/build_nexus_bazaar.py [--dry-run]

The nexus (`Exalt/Nexus New Auru.jm`) has a large sand island south of the town that nobody could
reach: the town's dock ends at Dark Water Flowing, which can't be walked on. This script
- builds a bridge from the dock to the island (over the unused Hallway region),
- lays a road from the bridge to the memorial plaza and paths to the market,
- builds open-air market pavilions whose stalls are shop regions (`Store N`, see
  `MerchantLists.Shops`), with pillars at their corners,
- and plants palm trees, lanterns and beach umbrellas on the open sand.

Every pavilion tile must be open sand, so a layout that runs into water or the plaza fails before
anything is written. The script refuses to run twice (it looks for the bazaar's `Store 11`
region); to change the layout, restore the map from git and run it again.
"""
import base64
import json
import os
import random
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Exalt", "Nexus New Auru.jm")

SAND = "Dark Sand"
FLOOR = "Oryx Castle Rug"   # pavilion floor, as in the town's shop room
EDGE = "Grey Closed"        # pavilion border
PATH = "Dark Cobblestone"
BRIDGE = "Bridge"
RAIL = "Dummy"              # the post the dock and the island's old bridge use as railing
PILLAR = "Grey Pillar"

# (name, region, x0, y0, columns, rows): stalls sit on every other tile inside a one-tile border,
# so a pavilion is 2 * columns + 3 tiles wide and 2 * rows + 3 tall.
PAVILIONS = [
    ("Dungeon Keys", "Store 11", 86, 193, 11, 4),
    ("Elder Keys", "Store 12", 117, 193, 10, 2),
    ("Enchanted Keys", "Store 4", 133, 208, 9, 2),
    ("Consumables", "Store 2", 133, 219, 9, 2),
    ("Stat Potions", "Store 1", 133, 229, 4, 2),
    ("Specials", "Store 3", 145, 229, 4, 2),
    ("Small Cloths", "Store 7", 82, 210, 5, 2),
    ("Clothing Dyes", "Store 8", 82, 219, 5, 2),
    ("Accessory Dyes", "Store 9", 82, 228, 5, 2),
]

# paths are painted on open sand only, so they stop at the plaza, the pavilions and the water
PATHS = [
    (111, 178, 116, 206),   # bridge to the plaza
    (126, 216, 155, 217),   # plaza to Enchanted Keys / Consumables
    (126, 226, 155, 227),   # plaza to Consumables / Stat Potions / Specials
    (144, 228, 144, 236),   # between Stat Potions and Specials
    (82, 217, 99, 218),     # plaza to Small Cloths / Clothing Dyes
    (82, 226, 99, 227),     # plaza to Clothing Dyes / Accessory Dyes
    (86, 204, 110, 205),    # along the front of Dungeon Keys
    (117, 200, 139, 201),   # along the front of Elder Keys
]

BRIDGE_X = (111, 115)       # railing, three planks, railing, in line with the dock
BRIDGE_Y = (117, 177)       # dock end to the island's shore


def load():
    jm = json.load(open(MAP, encoding="utf-8-sig"))
    w, h = jm["width"], jm["height"]
    data = zlib.decompress(base64.b64decode(jm["data"]))
    d = jm["dict"]
    grid = [[dict(d[(data[(y * w + x) * 2] << 8) | data[(y * w + x) * 2 + 1]]) for x in range(w)] for y in range(h)]
    return jm, grid


def save(jm, grid):
    w, h = jm["width"], jm["height"]
    entries = list(jm["dict"])
    index = {json.dumps(e, sort_keys=True): i for i, e in reversed(list(enumerate(entries)))}
    out = bytearray()
    for y in range(h):
        for x in range(w):
            t = {k: v for k, v in grid[y][x].items() if v}
            key = json.dumps(t, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(t)
            i = index[key]
            out += bytes((i >> 8, i & 0xff))
    jm["dict"] = entries
    jm["data"] = base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii")
    with open(MAP, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


def obj(t):
    return (t.get("objs") or [{}])[0].get("id")


def open_sand(t):
    # a few lilypads lie on dry sand by mistake; they count as open
    return (t.get("ground") == SAND and not t.get("regions")
            and (not t.get("objs") or "Lilypads" in (obj(t) or "")))


def main():
    dry = "--dry-run" in sys.argv
    jm, grid = load()
    if any(r.get("id") == "Store 11" for row in grid for t in row for r in t.get("regions") or []):
        sys.exit("The bazaar is already built (Store 11 exists). Restore the map from git to rebuild it.")

    def tile(x, y):
        return grid[y][x]

    def put(x, y, ground, obj_id=None, region=None):
        grid[y][x] = {"ground": ground, "objs": [{"id": obj_id}] if obj_id else None,
                      "regions": [{"id": region}] if region else None}

    # check the layout first
    built = set()
    problems = []
    for name, region, x0, y0, cols, rows in PAVILIONS:
        for y in range(y0, y0 + 2 * rows + 3):
            for x in range(x0, x0 + 2 * cols + 3):
                if (x, y) in built:
                    problems.append(f"{name} overlaps another pavilion at {x},{y}")
                elif not open_sand(tile(x, y)):
                    problems.append(f"{name} at {x},{y}: {tile(x, y).get('ground')} {obj(tile(x, y))}")
                built.add((x, y))
    if problems:
        sys.exit("Layout doesn't fit:\n  " + "\n  ".join(problems[:40]))

    # bridge: the dock (x 111-115) ends at y 116; the island's shore starts at y 178
    for y in range(BRIDGE_Y[0], BRIDGE_Y[1] + 1):
        for x in range(BRIDGE_X[0], BRIDGE_X[1] + 1):
            put(x, y, BRIDGE, RAIL if x in BRIDGE_X else None)
            built.add((x, y))

    # paths
    for x0, y0, x1, y1 in PATHS:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if (x, y) not in built and open_sand(tile(x, y)):
                    put(x, y, PATH)
                    built.add((x, y))

    # pavilions
    stalls = {}
    for name, region, x0, y0, cols, rows in PAVILIONS:
        x1, y1 = x0 + 2 * cols + 2, y0 + 2 * rows + 2
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                put(x, y, EDGE if x in (x0, x1) or y in (y0, y1) else FLOOR)
        for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            put(x, y, EDGE, PILLAR)
        for j in range(rows):
            for i in range(cols):
                put(x0 + 2 + 2 * i, y0 + 2 + 2 * j, FLOOR, region=region)
        stalls[name] = (region, cols * rows)

    # lanterns along the road and braziers where it leaves the bridge
    for y in range(181, 203, 4):
        for x in (111, 116):
            put(x, y, PATH, "Nexus Paper Lantern")
    for x in (110, 117):
        put(x, 179, SAND, "Jungle Brazier")
        built.add((x, 179))

    # palm trees and umbrellas on open sand, away from what was built
    near = {(x + dx, y + dy) for x, y in built for dx in range(-2, 3) for dy in range(-2, 3)}
    rng = random.Random(20261014)
    palms = umbrellas = 0
    for y in range(160, min(300, jm["height"])):
        for x in range(60, min(200, jm["width"])):
            if (x, y) in near or not open_sand(tile(x, y)):
                continue
            r = rng.random()
            if r < 0.035:
                put(x, y, SAND, "Palm Tree")
                palms += 1
            elif r < 0.042 and y >= 237:
                put(x, y, SAND, "Beach Umbrella")
                umbrellas += 1

    for name, (region, n) in stalls.items():
        print(f"{name}: {n} stalls ({region})")
    print(f"{palms} palm trees, {umbrellas} beach umbrellas")
    if dry:
        print("dry run: nothing written")
        return
    save(jm, grid)
    print("written", MAP)


if __name__ == "__main__":
    main()
