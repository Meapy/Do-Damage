"""Builds the map of The Hive, the Queen Bee's dungeon: the rooms its random layout joins.

    python tools/build_hive.py

The Hive was a player-made dungeon (Suestwo's UGC1, github.com/Suestwo/UGC1): its generator
(HiveGen.xml) lays out round rooms of honeycomb floor in walls of wax, joined by halls three or
four tiles wide, with honey pooling on the floor here and there and chunks of broken comb about.
This draws the rooms that generator makes, after its own numbers:

- the start room: a circle of radius 6, the way out (Portal of Cowardice) beside the spawn;
- the rooms between (`default`, and `branchEnd` at the ends of side branches): circles of radius
  4 to 7, each rolling its monsters from the generator's lists: 3-5 Fat Bees, 0-3 Fat Angry Bees,
  then either 2-5 Small Bees (60%) or a Mini Hive (40%), then one kind of Maggot Egg, 0-3 of them
  (0-2 for the first kind);
- the Queen's room: a circle of radius 10, the Queen in its middle and one or two of each kind
  of Maggot Egg round her.

The rooms stand on a grid joined by narrow halls only so the server can tell them apart: every
visit, its RandomLayout (Dungeons.xml) picks some of them and joins them anew, the Queen's room
ending a branch five rooms from the start (the generator's main branch of seven), the rest as
side branches. The monsters' behaviours are in BehaviorDb.Hive.cs.

Output (generated, don't edit): source/Shared/resources/worlds/Dungeons/The Hive.jm
"""
import base64
import json
import math
import os
import random
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "The Hive.jm")

CELL, COLS, ROWS = 26, 5, 4
W, H = CELL * COLS, CELL * ROWS

FLOOR, HONEY, WALL = "TH Floor", "TH Honey", "TH Wall"
CHUNKS = ["TH Chunk1", "TH Chunk2", "TH Chunk3", "TH Chunk4"]
FAT, RED, SMALL, MINI = "TH Fat Bees", "TH Red Fat Bees", "TH Small Bees", "TH Mini Hive"
EGGS = ["TH Maggot Egg1", "TH Maggot Egg2", "TH Maggot Egg3"]
QUEEN, EXIT = "TH Queen Bee", "Portal of Cowardice"

START_CELL, BOSS_CELL = (0, 3), (4, 0)
HALL = 1   # the halls between the cells are 3 tiles wide


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def inside(self, x, y):
        return 0 <= x < W and 0 <= y < H

    def circle(self, cx, cy, r):
        cells = []
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if math.hypot(x - cx, y - cy) <= r + 0.5 and self.inside(x, y):
                    self.ground[y][x] = FLOOR
                    cells.append((x, y))
        return cells

    def hall(self, a, b):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t)
            for yy in range(y - HALL, y + HALL + 1):
                for xx in range(x - HALL, x + HALL + 1):
                    self.ground[yy][xx] = FLOOR

    def walls(self):
        """A wax wall on every empty tile beside a floor."""
        found = [(x, y) for y in range(H) for x in range(W) if self.ground[y][x] is None
                 and any(self.inside(x + dx, y + dy) and self.ground[y + dy][x + dx] is not None
                         for dy in (-1, 0, 1) for dx in (-1, 0, 1))]
        for x, y in found:
            self.ground[y][x] = FLOOR
            self.obj[y][x] = WALL

    def by_wall(self, x, y, d=1):
        return any(not self.inside(x + dx, y + dy) or self.obj[y + dy][x + dx] == WALL
                   for dx in range(-d, d + 1) for dy in range(-d, d + 1))

    def free(self, x, y):
        return (self.inside(x, y) and self.ground[y][x] in (FLOOR, HONEY) and self.obj[y][x] is None
                and self.region[y][x] is None)

    def honey(self, rnd, cells, share):
        """Honey pooling on the floor (the generator's perlin-placed honey): a few blobs grown out
        from random seeds until about `share` of the room is honey."""
        want = int(len(cells) * share)
        cellset = set(cells)
        done = set()
        while len(done) < want:
            seed = rnd.choice(cells)
            blob = [seed]
            for _ in range(rnd.randint(3, 9)):
                x, y = rnd.choice(blob)
                nx, ny = x + rnd.choice((-1, 0, 1)), y + rnd.choice((-1, 0, 1))
                if (nx, ny) in cellset:
                    blob.append((nx, ny))
            done.update(blob)
        for x, y in done:
            if self.obj[y][x] is None and self.region[y][x] is None:
                self.ground[y][x] = HONEY

    def chunks(self, rnd, cells, n):
        spots = [(x, y) for x, y in cells if self.free(x, y) and self.ground[y][x] == FLOOR]
        for x, y in rnd.sample(spots, min(n, len(spots))):
            self.obj[y][x] = rnd.choice(CHUNKS)

    def scatter(self, rnd, cells, monsters):
        spots = [(x, y) for x, y in cells if self.free(x, y) and not self.by_wall(x, y)]
        rnd.shuffle(spots)
        taken = []
        for monster in monsters:
            for i, (x, y) in enumerate(spots):
                if all(abs(x - tx) + abs(y - ty) >= 2 for tx, ty in taken):
                    self.obj[y][x] = monster
                    taken.append((x, y))
                    del spots[i]
                    break


def centre(cell):
    return cell[0] * CELL + CELL // 2, cell[1] * CELL + CELL // 2


def roll_room(rnd):
    """A room's monsters, from HiveGen.xml's `default` and `branchEnd` rooms."""
    monsters = [FAT] * rnd.randint(3, 5) + [RED] * rnd.randint(0, 3)
    if rnd.random() < 0.6:
        monsters += [SMALL] * rnd.randint(2, 5)
    else:
        monsters.append(MINI)
    egg = rnd.choices(EGGS, weights=(0.4, 1, 1))[0]
    monsters += [egg] * rnd.randint(0, 2 if egg == EGGS[0] else 3)
    return monsters


def build():
    g = Grid()
    rnd = random.Random(2014)
    cells = [(c, r) for r in range(ROWS) for c in range(COLS)]
    # the halls first, under the rooms: across each row and down each column
    for r in range(ROWS):
        for c in range(COLS - 1):
            g.hall(centre((c, r)), centre((c + 1, r)))
    for c in range(COLS):
        for r in range(ROWS - 1):
            g.hall(centre((c, r)), centre((c, r + 1)))

    rooms = []
    for cell in cells:
        cx, cy = centre(cell)
        if cell == START_CELL:
            rooms.append(("start", g.circle(cx, cy, 6), cx, cy))
        elif cell == BOSS_CELL:
            rooms.append(("end", g.circle(cx, cy, 10), cx, cy))
        else:
            rooms.append(("room", g.circle(cx, cy, rnd.randint(4, 7)), cx, cy))
    g.walls()

    for kind, room, cx, cy in rooms:
        if kind == "start":
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    g.region[cy + dy][cx + dx] = "Spawn"
            g.obj[cy - 3][cx] = EXIT
            g.honey(rnd, room, 0.05)
            g.chunks(rnd, room, 2)
        elif kind == "end":
            g.obj[cy][cx] = QUEEN
            g.honey(rnd, room, 0.08)
            ring = []
            for egg in EGGS:
                ring += [egg] * rnd.randint(1, 2)
            for i, egg in enumerate(ring):
                a = i * math.tau / len(ring) + rnd.uniform(-0.2, 0.2)
                x, y = round(cx + math.cos(a) * 7), round(cy + math.sin(a) * 7)
                if g.free(x, y):
                    g.obj[y][x] = egg
                else:
                    g.scatter(rnd, room, [egg])
            g.chunks(rnd, room, 4)
        else:
            g.honey(rnd, room, 0.08)
            g.chunks(rnd, room, rnd.randint(1, 3))
            g.scatter(rnd, room, roll_room(rnd))
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
