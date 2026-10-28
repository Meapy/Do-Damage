"""Builds the maps of the Cultist Hideout and the Void, reached from the Lost Halls.

    python tools/build_cult.py

The layouts are RotMG's own (tools/cult/*.base.jm, as they came with the Lost Halls assets); this
script puts the monsters and the boss set pieces on them:

- the Cultist Hideout: packs of Malus' followers (one colour per cultist they serve) and his
  demons along the winding corridors from the arrival room, and in Malus' hall a pentagram with
  candles round it and The Cult (LH Cult Controller) below it, which starts the fight
  (BehaviorDb.Cult.cs);
- the Void: an arrival point on the platform's south edge and the Void Controller at its centre,
  which wakes the Void Entity (BehaviorDb.Void.cs).

Outputs (generated, don't edit): source/Shared/resources/worlds/Dungeons/Cultist Hideout.jm and
The Void/The Void.jm.
"""
import base64
import json
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(ROOT, "tools", "cult")
WORLDS = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons")

# Malus' followers, by the cultist they serve
RED, BLUE, YELLOW, WHITE, PURPLE, BLACK, GREEN = (
    "LH Red Cultist", "LH Blue Cultist", "LH Yellow Cultist", "LH White Cultist",
    "LH Purple Cultist", "LH Black Cultist", "LH Green Cultist")
FIRE, ICE, LIGHTNING = "LH Fire Demon", "LH Ice Demon", "LH Lightning Demon"

# (x, y, monsters): a pack, its members set round the point, along the way from the arrival room
# (east) down, west along the south hall, up the west corridor and east again to Malus' hall
PACKS = [
    (98, 78, [RED, RED]),
    (98, 92, [BLUE, YELLOW, ICE]),
    (98, 104, [WHITE, PURPLE]),
    (85, 114, [RED, FIRE, YELLOW]),
    (65, 114, [BLUE, BLUE, WHITE]),
    (45, 114, [PURPLE, LIGHTNING]),
    (32, 98, [BLACK, GREEN, RED]),       # the dead end north of the south hall
    (32, 87, [BLACK, FIRE]),
    (32, 124, [YELLOW, WHITE]),
    (18, 133, [RED, BLUE, FIRE, PURPLE]),
    (4, 120, [GREEN, WHITE]),
    (4, 100, [YELLOW, ICE]),
    (4, 82, [RED, PURPLE]),
    (4, 48, [BLACK, BLACK, GREEN]),      # the dead end north of the west corridor
    (4, 39, [RED, LIGHTNING]),
    (15, 67, [BLUE, LIGHTNING, YELLOW]),
    (36, 67, [WHITE, RED, PURPLE]),
    (51, 50, [FIRE, ICE, LIGHTNING]),    # before the hall
]
OFFSETS = [(0, 0), (-2, 1), (2, 1), (0, -2), (-2, -1), (2, -1)]

HALL_CENTRE = (51, 21)       # the pentagram's centre, where Malus stands
CONTROLLER = (51, 27)        # The Cult, below the pentagram


def load(path):
    with open(path, encoding="utf-8") as f:
        j = json.load(f)
    w, h = j["width"], j["height"]
    raw = zlib.decompress(base64.b64decode(j["data"]))
    tiles = list(struct.unpack(">%dH" % (w * h), raw))
    return j, tiles


def save(path, j, tiles):
    raw = struct.pack(">%dH" % len(tiles), *tiles)
    j["data"] = base64.b64encode(zlib.compress(raw, 9)).decode("ascii")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(j, f, separators=(",", ":"))
    print("wrote", os.path.relpath(path, ROOT))


class Map:
    def __init__(self, name):
        self.j, self.tiles = load(os.path.join(BASE, name + ".base.jm"))
        self.w = self.j["width"]
        self.dict = self.j["dict"]

    def entry(self, x, y):
        return self.dict[self.tiles[y * self.w + x]]

    def floor(self, x, y):
        e = self.entry(x, y)
        return "ground" in e and not e.get("objs") and not e.get("regions")

    def put(self, x, y, **extra):
        """Sets what's on (x, y), keeping its ground."""
        e = dict(self.entry(x, y))
        e.pop("objs", None)
        e.update(extra)
        if e not in self.dict:
            self.dict.append(e)
        self.tiles[y * self.w + x] = self.dict.index(e)

    def obj(self, x, y, obj_id):
        if not self.floor(x, y):
            raise SystemExit(f"({x}, {y}) isn't free floor for {obj_id}: {self.entry(x, y)}")
        self.put(x, y, objs=[{"id": obj_id}])

    def find(self, ground):
        return [(i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                if self.dict[t].get("ground") == ground]


def hideout():
    m = Map("Cultist Hideout")
    for x, y, mobs in PACKS:
        for mob, (dx, dy) in zip(mobs, OFFSETS):
            m.obj(x + dx, y + dy, mob)
    cx, cy = HALL_CENTRE
    # the pentagram, 7 x 7 pieces (LH Pent 1 to 49, row by row)
    for i in range(49):
        m.obj(cx - 3 + i % 7, cy - 3 + i // 7, f"LH Pent {i + 1}")
    # candles round it: big ones at the star's five points, small ones between
    for k, (dx, dy) in enumerate([(0, -5), (5, -2), (3, 4), (-3, 4), (-5, -2)]):
        m.obj(cx + dx, cy + dy, "LH Big Candle")
    for dx, dy in [(-4, -4), (4, -4), (5, 2), (-5, 2), (0, 5)]:
        if (cx + dx, cy + dy) != CONTROLLER:
            m.obj(cx + dx, cy + dy, "LH Small Candle")
    m.obj(*CONTROLLER, "LH Cult Controller")
    # the hall's furnishings along its north wall and in its corners
    props = ["LH Cult Prop1", "LH Cult Prop2", "LH Cult Prop3", "LH Cult Prop4", "LH Cult Prop5",
             "LH Cult Prop6", "LH Cult Prop7", "LH Cult Prop9"]
    for k, x in enumerate(range(41, 62, 3)):
        m.obj(x, 2, props[k % len(props)])
    for x, y in [(32, 2), (70, 2), (32, 40), (70, 40)]:
        m.obj(x, y, "LH Big Candle")
    save(os.path.join(WORLDS, "Cultist Hideout.jm"), m.j, m.tiles)


def void():
    m = Map("The Void")
    plat = m.find("LH Void Platform")
    xs, ys = [p[0] for p in plat], [p[1] for p in plat]
    cx, cy = (min(xs) + max(xs)) // 2, (min(ys) + max(ys)) // 2
    m.obj(cx, cy, "LH Void Controller")
    # arrive on the platform's south edge, facing the Entity
    for dx in (-1, 0, 1):
        m.put(cx + dx, max(ys) - 1, regions=[{"id": "Spawn"}])
    save(os.path.join(WORLDS, "The Void", "The Void.jm"), m.j, m.tiles)
    n = m.find("LH Void Landmark N")
    print(f"Void: platform centre ({cx}, {cy}), radius {(max(xs) - min(xs)) // 2}; landmark N at {n}")


if __name__ == "__main__":
    hideout()
    void()
