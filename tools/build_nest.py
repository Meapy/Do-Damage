"""Builds the map of The Nest, the Killer Bee Queen's dungeon: the rooms its random layout joins.

    python tools/build_nest.py

The game has The Nest's objects and grounds (the epicHive XML) but RotMG's own map isn't here, so
this draws its rooms in its manner (RealmEye, The Nest): honeycomb rooms with pools of Killer Honey,
broken tiles making paths across them, the boss room a darker circle with a pool in its middle,
Maggot Eggs along its edge and the Queen over the pool, guarded by a squad of Killer Bees; and the
Beekeeper's treasure room, a laboratory reached through breakable blocks at the end of a dead-end
room. The rooms stand on a grid joined by long, narrow halls only so the server can tell them
apart: every visit, its RandomLayout (Dungeons.xml) picks some of them and joins them anew, the
Queen's room and the laboratory each ending a branch (DungeonGenerator: the breakable blocks are
kept inside the laboratory's room, which is only ever entered from the dead-end side, where the
EH Waiting Floor marks its way in). The rooms' monsters are the dungeon's own: Soldier Bees,
Killer Bees, Mini Killer Bee Nests, larvae and eggs (BehaviorDb.Nest.cs).

Output (generated, don't edit): source/Shared/resources/worlds/Dungeons/The Nest.jm
"""
import base64
import json
import math
import os
import random
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "The Nest.jm")

CELL, COLS, ROWS = 44, 4, 5
W, H = CELL * COLS, CELL * ROWS

FLOOR, BOSS_FLOOR, LAB_FLOOR = "EH Floor", "EH Left Dark Floor", "EH S Floor"
HONEY, UNDER_PATH = "EH Venom", "EH UnderPartialFloor Venom"
PATH = "EH Static Partial Floor"
WALL, BOSS_WALL = "EH Wall", "EH Boss Wall"
GATE, ENTRANCE = "EH Destructible Wall", "EH Waiting Floor"

YELLOW_SOLDIER, RED_SOLDIER, BLUE_SOLDIER = "EH Large Yellow Bee", "EH Large Red Bee", "EH Large Blue Bee"
YELLOW_KILLER, RED_KILLER, BLUE_KILLER = "EH Fat Bees", "EH Red Fat Bees", "EH Blue Fat Bees"
YELLOW_LARVA, RED_LARVA, BLUE_LARVA = "EH Yellow Maggots", "EH Red Maggots", "EH Blue Maggots"
MINI_NEST = "EH Mini Hive"
EGGS = ["EH Maggot Egg1", "EH Maggot Egg2", "EH Maggot Egg3"]

# what each kind of room holds
SQUADS = [
    [YELLOW_SOLDIER, YELLOW_KILLER, YELLOW_KILLER, YELLOW_KILLER, YELLOW_LARVA, YELLOW_LARVA, MINI_NEST],
    [RED_SOLDIER, RED_KILLER, RED_KILLER, RED_KILLER, RED_LARVA, RED_LARVA, RED_LARVA],
    [BLUE_SOLDIER, BLUE_KILLER, BLUE_KILLER, BLUE_KILLER, MINI_NEST, BLUE_LARVA, BLUE_LARVA],
    [YELLOW_SOLDIER, YELLOW_KILLER, YELLOW_KILLER, MINI_NEST, YELLOW_LARVA, EGGS[0], EGGS[1]],
    [RED_SOLDIER, BLUE_SOLDIER, RED_KILLER, BLUE_KILLER, YELLOW_KILLER, MINI_NEST, MINI_NEST],
    [BLUE_SOLDIER, BLUE_KILLER, BLUE_KILLER, BLUE_LARVA, EGGS[2], EGGS[0], MINI_NEST],
    [RED_SOLDIER, RED_KILLER, RED_KILLER, MINI_NEST, RED_LARVA, EGGS[1]],
]

START_CELL, BOSS_CELL, LAB_CELL = (1, 4), (3, 0), (0, 0)
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

    def ellipse(self, cx, cy, rx, ry, ground):
        for y in range(max(0, int(cy - ry - 1)), min(H, int(cy + ry + 2))):
            for x in range(max(0, int(cx - rx - 1)), min(W, int(cx + rx + 2))):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    self.ground[y][x] = ground

    def rect(self, x0, y0, x1, y1, ground, cut=0):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                # rounded corners
                dx = max(x0 + cut - x, x - (x1 - cut), 0)
                dy = max(y0 + cut - y, y - (y1 - cut), 0)
                if math.hypot(dx, dy) <= cut + 0.01 or cut == 0:
                    self.ground[y][x] = ground

    def hall(self, a, b, ground):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for yy in range(int(y - HALL), int(y + HALL) + 1):
                for xx in range(int(x - HALL), int(x + HALL) + 1):
                    if abs(xx + 0.5 - x - 0.5) <= HALL and abs(yy + 0.5 - y - 0.5) <= HALL and self.ground[yy][xx] is None:
                        self.ground[yy][xx] = ground

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

    def free(self, x, y, grounds=(FLOOR, BOSS_FLOOR, LAB_FLOOR)):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] in grounds and self.obj[y][x] is None

    def place(self, x, y, obj):
        x, y = int(round(x)), int(round(y))
        assert self.free(x, y), (x, y, obj)
        self.obj[y][x] = obj


def ring(cx, cy, r, n, start=0):
    return [(cx + math.cos(start + i * 2 * math.pi / n) * r, cy + math.sin(start + i * 2 * math.pi / n) * r) for i in range(n)]


def centre(cell):
    return cell[0] * CELL + CELL // 2, cell[1] * CELL + CELL // 2


def honeycomb_room(g, rnd, cx, cy, kind):
    """A room of the honeycomb: its shape, its pools of Killer Honey with a path of broken tiles
    across each, and its bees."""
    shape = kind % 5
    if shape == 0:
        r = rnd.randint(8, 11)
        g.disc(cx, cy, r, FLOOR)
        reach = r
    elif shape == 1:
        g.ellipse(cx, cy, 13, 8, FLOOR)
        reach = 8
    elif shape == 2:
        g.rect(cx - 11, cy - 8, cx + 11, cy + 8, FLOOR, cut=4)
        reach = 8
    elif shape == 3:
        g.disc(cx - 5, cy, 8, FLOOR)
        g.disc(cx + 6, cy + 1, 7, FLOOR)
        reach = 7
    else:
        g.rect(cx - 12, cy - 4, cx + 12, cy + 4, FLOOR, cut=2)
        g.rect(cx - 4, cy - 12, cx + 4, cy + 12, FLOOR, cut=2)
        reach = 4
    # the pools: a path of broken tiles runs across each, safe to walk
    pools = []
    for _ in range(rnd.randint(1, 3)):
        for _ in range(30):
            px, py, pr = cx + rnd.randint(-reach + 3, reach - 3), cy + rnd.randint(-reach + 3, reach - 3), rnd.choice([2, 2.5, 3])
            if all(math.hypot(px - qx, py - qy) > pr + qr + 3 for qx, qy, qr in pools) and math.hypot(px - cx, py - cy) > 2:
                cells = [(x, y) for y in range(int(py - pr), int(py + pr) + 1) for x in range(int(px - pr), int(px + pr) + 1)
                         if math.hypot(x - px, y - py) <= pr]
                # every tile of the pool and a tile round it must be floor
                if all(g.free(x + dx, y + dy, (FLOOR,)) for x, y in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                    pools.append((px, py, pr))
                    break
    for px, py, pr in pools:
        g.disc(px, py, pr, HONEY, only=(FLOOR,))
        horizontal = rnd.random() < 0.5
        for i in range(-int(pr), int(pr) + 1):
            x, y = (int(px) + i, int(py)) if horizontal else (int(px), int(py) + i)
            if g.ground[y][x] == HONEY:
                g.ground[y][x] = UNDER_PATH
                g.obj[y][x] = PATH
    # its bees, round its middle, off its honey
    squad = SQUADS[kind % len(SQUADS)]
    spots = [(round(x), round(y)) for x, y in ring(cx, cy, reach * 0.55, 8, rnd.random() * 6) + ring(cx, cy, reach * 0.25, 4, 1.2)]
    spots = [s for s in spots if g.free(*s, (FLOOR,))]
    for monster, (px, py) in zip(squad, spots):
        g.place(px, py, monster)


def boss_room(g, cx, cy):
    br = 15
    g.disc(cx, cy, br, BOSS_FLOOR)
    g.disc(cx, cy, 3, HONEY)
    g.obj[cy][cx] = "EH King Bee"
    for (px, py), bee in zip(ring(cx, cy, 6, 6, 0.3), [YELLOW_KILLER, RED_KILLER, BLUE_KILLER] * 2):
        g.place(px, py, bee)
    for (px, py), egg in zip(ring(cx, cy, br - 2, 6, 0.0), EGGS * 2):
        g.place(px, py, egg)


def laboratory(g, cx, cy):
    """The dead-end room and, behind breakable blocks in its north wall, the Beekeeper's
    laboratory (RealmEye: navy floor and walls like the Mad Lab's, six chambers with a Soldier Bee
    each and a large monitor at the top, beehives along the edges)."""
    # the dead-end room, entered from the south (where its marker stands); wide where it meets the
    # blocks, so the server sees one room through them
    ay = cy + 12
    g.rect(cx - 7, cy + 6, cx + 7, cy + 18, FLOOR, cut=3)
    # the laboratory
    x0, y0, x1, y1 = cx - 14, cy - 12, cx + 14, cy + 4
    g.rect(x0, y0, x1, y1, LAB_FLOOR)
    # the breakable blocks between them, three wide
    for x in (cx - 1, cx, cx + 1):
        g.ground[cy + 5][x] = FLOOR
        g.obj[cy + 5][x] = GATE
    return ay


def furnish_laboratory(g, cx, cy):
    ay = cy + 12
    g.place(cx, ay + 4, ENTRANCE)
    for (px, py), larva in zip(ring(cx, ay, 3, 3, 0.5), [YELLOW_LARVA, RED_LARVA, BLUE_LARVA]):
        g.place(px, py, larva)
    x0, y0, x1 = cx - 14, cy - 12, cx + 14
    for i, dx in enumerate((-12, -8, -4, 4, 8, 12)):
        g.place(cx + dx, y0 + 1, ["EH Blue Bee Tank", "EH Red Bee Tank", "EH Yellow Bee Tank"][i % 3])
    for dx, part in zip((-2, -1, 0, 1), ("EH Screen Left", "EH Screen Middle Left", "EH Screen Middle Right", "EH Screen Right")):
        g.place(cx + dx, y0, part)
    g.place(cx, y0 + 2, "EH Computer")
    for y in range(y0 + 3, cy + 3, 3):
        g.place(x0, y, "EH Apiary")
        g.place(x1, y, "EH Apiary")
    g.place(cx, cy - 4, "EH Beekeeper")


def build():
    g = Grid()
    rnd = random.Random(1717)
    cells = [(c, r) for r in range(ROWS) for c in range(COLS)]
    fillers = [c for c in cells if c not in (START_CELL, BOSS_CELL, LAB_CELL)]
    # the halls first, under the rooms: across each row and down the middle columns; the
    # laboratory only from the room below it
    links = []
    for r in range(ROWS):
        for c in range(COLS - 1):
            if LAB_CELL not in ((c, r), (c + 1, r)):
                links.append(((c, r), (c + 1, r)))
    for c in (1, 3):
        for r in range(ROWS - 1):
            links.append(((c, r), (c, r + 1)))
    lx, ly = centre(LAB_CELL)
    links.append(((LAB_CELL[0], LAB_CELL[1] + 1), None))
    for a, b in links:
        if b is None:
            g.hall(centre(a), (lx, ly + 16), FLOOR)
        else:
            g.hall(centre(a), centre(b), FLOOR)

    sx, sy = centre(START_CELL)
    g.disc(sx, sy, 6, FLOOR)
    bx, by = centre(BOSS_CELL)
    boss_room(g, bx, by)
    laboratory(g, lx, ly)
    for i, cell in enumerate(fillers):
        honeycomb_room(g, rnd, *centre(cell), i)
    g.walls()

    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            g.region[sy + dy][sx + dx] = "Spawn"
    furnish_laboratory(g, lx, ly)
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
