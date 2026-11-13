"""Objects placed by hand on dungeon maps that came without them (2026-11-13).

Some upstream dungeon maps lack the controllers and set pieces their fights need: the Magic Woods
had no boss spawner in its fountain, the Fungal Cavern no Cavern Crystals, drill, drill wall or
Crystal Worm Mother; the Deadwater Docks names its breakable walls by an id the game lacks. This
script renames those and puts each listed object on its tile of the `.jm` map, and is safe
to run again: a tile that already holds the object is left alone, and one that holds anything
else stops the script, so nothing on a map is overwritten by mistake.

    python tools/dungeon_placements.py [--check]

--check only reports what is missing (exit 1 if anything is).
"""
import base64
import json
import os
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUNGEONS = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons")

# the 55 Cavern Crystals: spread over the caves' rooms at least 9 tiles apart, away from the start
# (20 of them power the drill; BehaviorDb.FungalCavern.cs). The random layout uses part of the
# rooms, so a run keeps 20 to 50 of them.
CAVERN_CRYSTALS = [
    (6, 56), (7, 105), (9, 75), (10, 23), (10, 122), (21, 70), (30, 138), (31, 113), (37, 66),
    (38, 88), (45, 27), (50, 99), (50, 115), (51, 73), (52, 49), (55, 9), (55, 128), (62, 25),
    (68, 93), (72, 57), (76, 104), (79, 68), (81, 2), (83, 21), (84, 44), (99, 52), (101, 91),
    (102, 107), (105, 25), (113, 54), (116, 33), (121, 16), (124, 95), (126, 78),
    (6, 91), (16, 32), (16, 58), (16, 89), (19, 19), (25, 26), (26, 126), (40, 111), (40, 139),
    (46, 7), (47, 90), (58, 65), (58, 93), (59, 56), (72, 27), (80, 91), (91, 27), (105, 59),
    (106, 46), (119, 85), (121, 25),
]

PLACEMENTS = {
    "Magic Woods.jm": [
        # in the fountain's pool, four tiles above its spout (132, 116); BehaviorDb.MagicWoods.cs
        (132, 112, "MW Boss Spawner"),
    ],
    "Fungal Cavern.jm": (
        [(x, y, "GC Crystal") for x, y in CAVERN_CRYSTALS]
        + [
            # the wall across the corridor into the round chamber, which the drill breaks
            (152, 24, "GC Drill Wall"), (152, 25, "GC Drill Wall"), (152, 26, "GC Drill Wall"),
            (152, 27, "GC Drill Wall"), (152, 28, "GC Drill Wall"),
            # the drill facing it, and the Dwarf Miner by the drill room's way in (its entrance marker
            # for the random layout: Dungeons.xml)
            (148, 26, "GC Drill Charge"),
            (138, 24, "GC Drill Dwarf"),
            # the Crystal Worm Mother, in the chamber's centre
            (169, 26, "GC Boss"),
        ]
    ),
}

# rooms cut into a map: (x0, y0, x1, y1, floor ground, wall object, openings). The inside becomes
# bare floor, the ring around it wall, except the openings (tiles left as they are) and the tiles
# PLACEMENTS fills. The Fungal Cavern's drill hall, a 5-wide passage with six alcoves, becomes a
# drill room, so the random layout (Dungeons.xml) keeps it whole, with the drill wall and the
# Crystal Worm Mother's chamber behind it.
CARVES = {
    "Fungal Cavern.jm": [
        (134, 18, 152, 34, "Crystal Main Floor Dungeon", "GC Wall",
         [(134, y) for y in range(24, 29)] + [(152, y) for y in range(24, 29)]),
    ],
}

# objects a map names by an id the game doesn't have, so they were left out of the dungeon
RENAMES = {
    # the walls in front of the docks' treasure rooms, which players shoot through
    "Deadwater Docks.jm": {"Destructible Brown Wall 1": "Destructible Brown Wall"},
}


def rename(name, check):
    path = os.path.join(DUNGEONS, name)
    jm, data = load(path)
    found = 0
    for entry in jm["dict"]:
        for o in entry.get("objs") or []:
            new = RENAMES[name].get(o.get("id"))
            if new:
                found += 1
                if check:
                    print(f"{name}: {o['id']} not yet renamed to {new}")
                else:
                    o["id"] = new
    if found and not check:
        save(path, jm, data)
        print(f"{name}: renamed {found} map entries")
    return found


def carve(name, jm, data, check):
    """Applies CARVES to a loaded map; returns how many tiles it changed (or would change)."""
    w = jm["width"]
    index = {json.dumps(e, sort_keys=True): i for i, e in enumerate(jm["dict"])}
    placed = {(x, y) for x, y, _ in PLACEMENTS.get(name, [])}
    changed = 0
    for x0, y0, x1, y1, floor, wall, openings in CARVES.get(name, []):
        skip = set(openings) | placed
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if (x, y) in skip:
                    continue
                inside = x0 < x < x1 and y0 < y < y1
                want = {"ground": floor} if inside else {"objs": [{"id": wall, "name": "size:100"}], "ground": "Composite"}
                at = (y * w + x) * 2
                if jm["dict"][(data[at] << 8) | data[at + 1]] == want:
                    continue
                changed += 1
                if check:
                    continue
                key = json.dumps(want, sort_keys=True)
                if key not in index:
                    index[key] = len(jm["dict"])
                    jm["dict"].append(want)
                i = index[key]
                data[at], data[at + 1] = i >> 8, i & 0xFF
    if changed:
        print(f"{name}: {'would carve' if check else 'carved'} {changed} tiles")
    return changed


def load(path):
    with open(path, encoding="utf-8") as f:
        jm = json.load(f)
    data = bytearray(zlib.decompress(base64.b64decode(jm["data"])))
    return jm, data


def save(path, jm, data):
    jm["data"] = base64.b64encode(zlib.compress(bytes(data), 9)).decode("ascii")
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


def main():
    check = "--check" in sys.argv[1:]
    missing = sum(rename(name, check) for name in RENAMES)
    for name, items in PLACEMENTS.items():
        path = os.path.join(DUNGEONS, name)
        jm, data = load(path)
        w = jm["width"]
        carved = carve(name, jm, data, check)
        missing += carved if check else 0
        index = {json.dumps(e, sort_keys=True): i for i, e in enumerate(jm["dict"])}
        changed = 0
        for x, y, obj in items:
            at = (y * w + x) * 2
            entry = jm["dict"][(data[at] << 8) | data[at + 1]]
            objs = entry.get("objs") or []
            if any(o.get("id") == obj for o in objs):
                continue
            if objs:
                sys.exit(f"{name} ({x}, {y}) holds {objs[0].get('id')}, not {obj}: not overwriting it")
            missing += 1
            if check:
                print(f"{name}: {obj} missing at ({x}, {y})")
                continue
            new = dict(entry)
            new["objs"] = [{"id": obj}]
            key = json.dumps(new, sort_keys=True)
            if key not in index:
                index[key] = len(jm["dict"])
                jm["dict"].append(new)
            i = index[key]
            data[at], data[at + 1] = i >> 8, i & 0xFF
            changed += 1
        if changed or (carved and not check):
            save(path, jm, data)
        if changed:
            print(f"{name}: placed {changed}")
    if check and missing:
        sys.exit(1)


if __name__ == "__main__":
    main()
