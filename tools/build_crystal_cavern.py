"""Builds the map of the Crystal Cavern, the second part of the Fungal Cavern: the rooms its random
layout joins.

    python tools/build_crystal_cavern.py

The game has the Crystal Cavern's objects and grounds (the crystalCave and fungalCavern XML) but
RotMG's own map isn't here, so this draws its rooms in its manner (RealmEye, Crystal Cavern): caves
of crystal floor with Cavern Crystals to break and the cave's bats, slimes and Crystallised beasts;
a drill room whose drill, once 20 crystals are broken, cuts through the wall to the Crystal
Entity's chamber, a pentagon platform standing in water. The rooms stand on a grid joined by long,
narrow halls only so the server can tell them apart: every visit, its RandomLayout (Dungeons.xml)
picks some of them and joins them anew, the drill room and the chamber behind its wall kept as one
room that ends a branch, entered from the drill room's south (where the Dwarf Miner stands).
Monsters and the drill: BehaviorDb.CrystalCavern.cs.

Output (generated, don't edit): source/Shared/resources/worlds/Dungeons/Crystal Cavern.jm
"""
import base64
import json
import math
import os
import random
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Crystal Cavern.jm")

CELL, COLS, ROWS = 42, 4, 5
TOP = 12     # room above the first row for the chamber, the tallest room
W, H = CELL * COLS, CELL * ROWS + TOP

FLOOR, DARK, LIGHT = "Crystal Main Floor Dungeon", "Crystal Main Dark", "Crystal Light Floor"
SHALLOW, DEEP = "Crystal Cave Shallow Water", "Crystal Cave Deep Water"
WALL, WALL_GROUND = "CC Wall", "Crystal Main Dark"
GATE, DWARF, DRILL = "CC Drill Wall", "CC Drill Dwarf", "CC Drill Charge"
CRYSTAL, BOSS = "CC Crystal", "mc"
WALKABLE = (FLOOR, DARK, LIGHT, SHALLOW)

BATS = ["Amethyst Bat", "Emerald Bat", "Ruby Bat", "Sapphire Bat", "Topaze Bat"]
SLIMES = ["Amethyst Slime", "Emerald Slime", "Ruby Slime", "Saphhire Slime", "Topaze Slime"]
BEASTS = ["Crystal Charger", "Crystal Watcher", "Crystal Splitter", "Crystal Shrieker", "Crystal Boomer"]
WORM = "CC Crystal Worm Child"

START_CELL, BOSS_CELL = (1, 4), (2, 0)
HALL = 1.5   # half the width of the halls (3 tiles)


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def disc(self, cx, cy, r, ground, only=None):
        for y in range(max(0, int(cy - r - 1)), min(H, int(cy + r + 2))):
            for x in range(max(0, int(cx - r - 1)), min(W, int(cx + r + 2))):
                if math.hypot(x - cx, y - cy) <= r and (only is None or self.ground[y][x] in only):
                    self.ground[y][x] = ground

    def blob(self, rnd, cx, cy, r, ground):
        # a cave: overlapping discs round a middle
        self.disc(cx, cy, r, ground)
        for _ in range(rnd.randint(2, 4)):
            a = rnd.random() * math.tau
            d = r * rnd.uniform(0.4, 0.7)
            self.disc(cx + math.cos(a) * d, cy + math.sin(a) * d, r * rnd.uniform(0.45, 0.7), ground)

    def rect(self, x0, y0, x1, y1, ground):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.ground[y][x] = ground

    def polygon(self, cx, cy, r, sides, ground, turn=-math.pi / 2):
        pts = [(cx + math.cos(turn + i * math.tau / sides) * r, cy + math.sin(turn + i * math.tau / sides) * r) for i in range(sides)]
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                px, py = x + 0.5, y + 0.5
                inside = True
                for i in range(sides):
                    (ax, ay), (bx, by) = pts[i], pts[(i + 1) % sides]
                    if (bx - ax) * (py - ay) - (by - ay) * (px - ax) < 0:
                        inside = False
                        break
                if inside:
                    self.ground[y][x] = ground

    def hall(self, a, b, ground):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for yy in range(int(y - HALL), int(y + HALL) + 1):
                for xx in range(int(x - HALL), int(x + HALL) + 1):
                    if abs(xx - x) <= HALL and abs(yy - y) <= HALL and self.ground[yy][xx] is None:
                        self.ground[yy][xx] = ground

    def walls(self):
        found = []
        for y in range(H):
            for x in range(W):
                if self.ground[y][x] is not None:
                    continue
                if any(0 <= x + dx < W and 0 <= y + dy < H and self.ground[y + dy][x + dx] is not None
                       for dy in (-1, 0, 1) for dx in (-1, 0, 1)):
                    found.append((x, y))
        for x, y in found:
            self.ground[y][x] = WALL_GROUND
            self.obj[y][x] = WALL

    def free(self, x, y, grounds=(FLOOR, DARK, LIGHT)):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] in grounds and self.obj[y][x] is None \
            and self.region[y][x] is None

    def place(self, x, y, obj):
        x, y = int(round(x)), int(round(y))
        assert self.free(x, y, WALKABLE), (x, y, obj)
        self.obj[y][x] = obj


def centre(cell):
    return cell[0] * CELL + CELL // 2, cell[1] * CELL + CELL // 2 + TOP


def cave(g, rnd, cx, cy, kind):
    """A cave of the cavern: crystal floor with a shallow pool or two, three Cavern Crystals near
    its walls and its monsters in the middle."""
    r = rnd.randint(9, 12)
    g.blob(rnd, cx, cy, r, FLOOR)
    g.disc(cx, cy, r * 0.45, [DARK, LIGHT, FLOOR][kind % 3])
    for _ in range(rnd.randint(0, 2)):
        a = rnd.random() * math.tau
        g.disc(cx + math.cos(a) * r * 0.5, cy + math.sin(a) * r * 0.5, rnd.choice([2, 2.5, 3]), SHALLOW, only=(FLOOR,))
    return r


def furnish_cave(g, rnd, cx, cy, r, kind):
    # crystals towards its edge, spread round it
    spots = [(cx + math.cos(a) * r * f, cy + math.sin(a) * r * f)
             for a in [rnd.random() * math.tau + i * math.tau / 3 for i in range(3)] for f in (0.75, 0.65, 0.55, 0.45)]
    placed = 0
    for x, y in spots:
        if placed < 3 and g.free(round(x), round(y)):
            g.place(x, y, CRYSTAL)
            placed += 1
            spots = [s for s in spots if math.hypot(s[0] - x, s[1] - y) > 4]
    # its monsters: a pack of one kind of bat and slime, or Crystallised beasts, and a worm now and then
    colour = kind % 5
    packs = [
        [BATS[colour], BATS[colour], BATS[(colour + 2) % 5], SLIMES[colour], SLIMES[colour]],
        [BEASTS[colour], BEASTS[(colour + 1) % 5], SLIMES[colour], BATS[colour]],
        [SLIMES[colour], SLIMES[(colour + 1) % 5], SLIMES[(colour + 3) % 5], BATS[colour], BEASTS[colour]],
    ]
    pack = list(packs[kind % 3])
    if kind % 4 == 3:
        pack.append(WORM)
    ring = [(cx + math.cos(rnd.random() * math.tau) * r * f, cy + math.sin(rnd.random() * math.tau) * r * f)
            for f in [0.1, 0.2, 0.25, 0.3, 0.35, 0.4, 0.2, 0.3, 0.15, 0.35, 0.25, 0.4]]
    for monster in pack:
        while ring:
            x, y = ring.pop(0)
            if g.free(round(x), round(y)):
                g.place(x, y, monster)
                break


def chamber(g, cx, cy):
    """The drill room and the Crystal Entity's chamber behind its wall: a pentagon platform of
    crystal standing in water."""
    # the chamber north of the wall
    ay = cy - 2
    g.disc(cx, ay, 16, SHALLOW)
    for a in range(5):
        ang = -math.pi / 2 + (a + 0.5) * math.tau / 5
        g.disc(cx + math.cos(ang) * 13, ay + math.sin(ang) * 13, 2.5, DEEP)
    g.polygon(cx, ay, 10, 5, FLOOR)
    g.polygon(cx, ay, 4, 5, LIGHT)
    # the wall row and the drill room south of it
    wall_y = ay + 17
    g.rect(cx - 9, wall_y + 1, cx + 9, wall_y + 12, FLOOR)
    # flat and wide where it meets the wall, so the server sees one room through the wall
    g.rect(cx - 9, wall_y - 6, cx + 9, wall_y - 1, SHALLOW)
    for x in range(cx - 2, cx + 3):
        g.ground[wall_y][x] = FLOOR
        g.obj[wall_y][x] = GATE
    return ay, wall_y


def furnish_chamber(g, cx, ay, wall_y):
    g.obj[ay][cx] = BOSS
    g.place(cx, wall_y + 3, DRILL)
    g.place(cx + 1, wall_y + 11, DWARF)


def build():
    g = Grid()
    rnd = random.Random(2911)
    cells = [(c, r) for r in range(ROWS) for c in range(COLS)]
    fillers = [c for c in cells if c not in (START_CELL, BOSS_CELL)]
    bx, by = centre(BOSS_CELL)
    # the halls first: across each row and down two columns; the chamber only from below
    for r in range(ROWS):
        for c in range(COLS - 1):
            if BOSS_CELL not in ((c, r), (c + 1, r)):
                g.hall(centre((c, r)), centre((c + 1, r)), FLOOR)
    for c in (0, 2):
        for r in range(ROWS - 1):
            if (c, r) != BOSS_CELL:
                g.hall(centre((c, r)), centre((c, r + 1)), FLOOR)

    sx, sy = centre(START_CELL)
    g.disc(sx, sy, 6, FLOOR)
    ay, wall_y = chamber(g, bx, by - 12)
    caves = []
    for i, cell in enumerate(fillers):
        cx, cy = centre(cell)
        caves.append((cx, cy, cave(g, rnd, cx, cy, i), i))
    # the hall from the drill room's south to the cave below it
    g.hall((bx, wall_y + 12), centre((BOSS_CELL[0], BOSS_CELL[1] + 1)), FLOOR)
    g.walls()

    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            g.region[sy + dy][sx + dx] = "Spawn"
    furnish_chamber(g, bx, ay, wall_y)
    for cx, cy, r, i in caves:
        furnish_cave(g, rnd, cx, cy, r, i)
    return g


def save(g):
    entries, index, out = [], {}, bytearray()
    for y in range(H):
        for x in range(W):
            e = {}
            if g.ground[y][x]:
                e["ground"] = g.ground[y][x]
            if g.obj[y][x]:
                e["objs"] = [{"id": g.obj[y][x]}]
            if g.region[y][x]:
                e["regions"] = [{"id": g.region[y][x]}]
            key = json.dumps(e, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(e)
            i = index[key]
            out += bytes((i >> 8, i & 0xFF))
    jm = {"data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii"),
          "dict": entries, "height": H, "width": W}
    with open(OUT, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    crystals = sum(row.count(CRYSTAL) for row in g.obj)
    print(f"wrote {os.path.relpath(OUT, ROOT)} ({W}x{H}, {crystals} crystals)")


if __name__ == "__main__":
    save(build())
