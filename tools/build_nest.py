"""Builds the map of The Nest, the Killer Bee Queen's dungeon.

    python tools/build_nest.py

The game has The Nest's objects and grounds (the epicHive XML) but RotMG's own map isn't here, so
this draws one in its manner (RealmEye, The Nest): round honeycomb rooms with pools of Killer
Honey, joined by short halls, five rooms between the start and the boss room, and the boss room a
darker circle with a pool of Killer Honey in its middle, Maggot Eggs along its edge and the Queen
over the pool, guarded by a squad of Killer Bees. The rooms' monsters are the dungeon's own: Soldier
Bees, Killer Bees, Mini Killer Bee Nests, larvae and eggs (BehaviorDb.Nest.cs).

Not here: the hidden treasure room and its Beekeeper.

Output (generated, don't edit): source/Shared/resources/worlds/Dungeons/The Nest.jm
"""
import base64
import json
import math
import os
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "The Nest.jm")

W, H = 96, 164

FLOOR, BOSS_FLOOR, HONEY = "EH Floor", "EH Left Dark Floor", "EH Venom"
WALL, BOSS_WALL = "EH Wall", "EH Boss Wall"

YELLOW_SOLDIER, RED_SOLDIER, BLUE_SOLDIER = "EH Large Yellow Bee", "EH Large Red Bee", "EH Large Blue Bee"
YELLOW_KILLER, RED_KILLER, BLUE_KILLER = "EH Fat Bees", "EH Red Fat Bees", "EH Blue Fat Bees"
YELLOW_LARVA, RED_LARVA, BLUE_LARVA = "EH Yellow Maggots", "EH Red Maggots", "EH Blue Maggots"
MINI_NEST = "EH Mini Hive"
EGGS = ["EH Maggot Egg1", "EH Maggot Egg2", "EH Maggot Egg3"]

START = (48, 152, 6)
# (x, y, radius, honey pools as (dx, dy, r), monsters): south to north
ROOMS = [
    (48, 131, 9, [(-4, 3, 2)], [YELLOW_KILLER, YELLOW_KILLER, YELLOW_KILLER, YELLOW_LARVA, YELLOW_LARVA, MINI_NEST]),
    (27, 111, 9, [(3, -3, 2)], [RED_SOLDIER, RED_KILLER, RED_KILLER, RED_KILLER, RED_LARVA, RED_LARVA, RED_LARVA]),
    (67, 93, 10, [(-3, 4, 2), (4, -3, 1.5)], [BLUE_SOLDIER, BLUE_KILLER, BLUE_KILLER, BLUE_KILLER, MINI_NEST, BLUE_LARVA, BLUE_LARVA]),
    (29, 74, 9, [(0, 0, 2)], [YELLOW_SOLDIER, YELLOW_KILLER, YELLOW_KILLER, MINI_NEST, YELLOW_LARVA, EGGS[0], EGGS[1]]),
    (63, 56, 10, [(-4, -2, 2), (4, 3, 1.5)], [RED_SOLDIER, BLUE_SOLDIER, RED_KILLER, BLUE_KILLER, YELLOW_KILLER, MINI_NEST, MINI_NEST]),
]
BOSS = (48, 23, 15)
HALL = 2.5   # half the width of the halls


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def disc(self, cx, cy, r, ground):
        for y in range(max(0, int(cy - r - 1)), min(H, int(cy + r + 2))):
            for x in range(max(0, int(cx - r - 1)), min(W, int(cx + r + 2))):
                if math.hypot(x - cx, y - cy) <= r:
                    self.ground[y][x] = ground

    def hall(self, a, b, ground):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            self.disc(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, HALL, ground)

    def walls(self):
        # a wall on every empty tile beside a floor (found first, then placed, so a new wall doesn't
        # count as a floor); the boss room's darker
        found = []
        for y in range(H):
            for x in range(W):
                if self.ground[y][x] is not None:
                    continue
                near = [self.ground[y + dy][x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                        if 0 <= x + dx < W and 0 <= y + dy < H and self.ground[y + dy][x + dx] is not None]
                if near:
                    found.append((x, y, BOSS_FLOOR in near))
        for x, y, boss in found:
            self.ground[y][x] = BOSS_FLOOR if boss else FLOOR
            self.obj[y][x] = BOSS_WALL if boss else WALL

    def place(self, x, y, obj):
        x, y = int(round(x)), int(round(y))
        assert self.ground[y][x] in (FLOOR, BOSS_FLOOR) and self.obj[y][x] is None, (x, y, obj)
        self.obj[y][x] = obj


def ring(cx, cy, r, n, start=0):
    return [(cx + math.cos(start + i * 2 * math.pi / n) * r, cy + math.sin(start + i * 2 * math.pi / n) * r) for i in range(n)]


def build():
    g = Grid()
    sx, sy, sr = START
    g.disc(sx, sy, sr, FLOOR)
    prev = (sx, sy)
    for x, y, r, pools, _ in ROOMS:
        g.hall(prev, (x, y), FLOOR)
        g.disc(x, y, r, FLOOR)
        prev = (x, y)
    bx, by, br = BOSS
    g.hall(prev, (bx, by + br - 2), FLOOR)
    g.disc(bx, by, br, BOSS_FLOOR)
    # Killer Honey: the rooms' pools and the boss room's
    for x, y, r, pools, _ in ROOMS:
        for dx, dy, pr in pools:
            g.disc(x + dx, y + dy, pr, HONEY)
    g.disc(bx, by, 3, HONEY)
    g.walls()

    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            g.region[sy + dy][sx + dx] = "Spawn"
    # the rooms' monsters, round each room's middle, off its honey
    for x, y, r, pools, monsters in ROOMS:
        spots = [p for p in ring(x, y, r * 0.55, 8, 0.4) + ring(x, y, r * 0.25, 4, 1.2)
                 if all(math.hypot(p[0] - x - dx, p[1] - y - dy) > pr + 1 for dx, dy, pr in pools)]
        for monster, (px, py) in zip(monsters, spots):
            g.place(px, py, monster)
    # the boss room: the Queen over her pool, her squad round her, the eggs along the edge
    g.obj[by][bx] = "EH King Bee"
    for (px, py), bee in zip(ring(bx, by, 6, 6, 0.3), [YELLOW_KILLER, RED_KILLER, BLUE_KILLER] * 2):
        g.place(px, py, bee)
    for (px, py), egg in zip(ring(bx, by, br - 2, 6, 0.0), EGGS * 2):
        g.place(px, py, egg)
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
    print(f"wrote {os.path.relpath(OUT, ROOT)} ({W}x{H})")


if __name__ == "__main__":
    save(build())
