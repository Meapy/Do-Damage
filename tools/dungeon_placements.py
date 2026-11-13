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

# the 34 Cavern Crystals: spread over the caves at least 13 tiles apart, away from the start
# (25 of them power the drill; BehaviorDb.FungalCavern.cs)
CAVERN_CRYSTALS = [
    (6, 56), (7, 105), (9, 75), (10, 23), (10, 122), (21, 70), (30, 138), (31, 113), (37, 66),
    (38, 88), (45, 27), (50, 99), (50, 115), (51, 73), (52, 49), (55, 9), (55, 128), (62, 25),
    (68, 93), (72, 57), (76, 104), (79, 68), (81, 2), (83, 21), (84, 44), (99, 52), (101, 91),
    (102, 107), (105, 25), (113, 54), (116, 33), (121, 16), (124, 95), (126, 78),
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
            # the drill facing it, and the Dwarf Miner beside
            (149, 26, "GC Drill Charge"),
            (147, 24, "GC Drill Dwarf"),
            # the Crystal Worm Mother, in the chamber's centre
            (169, 26, "GC Boss"),
        ]
    ),
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
        if changed:
            save(path, jm, data)
            print(f"{name}: placed {changed}")
    if check and missing:
        sys.exit(1)


if __name__ == "__main__":
    main()
