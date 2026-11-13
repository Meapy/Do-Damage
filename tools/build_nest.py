"""Builds the map of The Nest, the Killer Bee Queen's dungeon: the rooms its random layout joins.

    python tools/build_nest.py

The game has The Nest's objects and grounds (the epicHive XML) but RotMG's own map isn't here, so
this draws its rooms after RealmEye's page and screenshots (The Nest), with the dungeon's own art:

- the rooms: set pieces of orange honeycomb floor with straight, stepped edges in walls of waxy
  yellow blocks (three kinds, mixed, and now and then a crack with Killer Honey flowing down it),
  broken honeycomb chunks lying about, and pools of Killer Honey: diamond pools, chevrons and
  crosses, rivers, all edged with broken yellow tiles scattered over them as paths;
- the start room: two chevrons of honey edged with broken tiles either side of the spawn;
- the Queen's chamber: a great octagon in darker walls, its floor dark, a wide diamond of honey in
  its middle with the Queen over it, rings of broken tiles across the honey, Maggot Eggs round the
  edge and her squad of Killer Bees;
- the Beekeeper's laboratory, behind breakable blocks in the north of a dead-end room (RealmEye:
  "similar aesthetics to the Mad Lab ... navy blue floor and wall tiles. Six chambers, each
  containing a Soldier Bee, are at the top of the room, along with a large monitor. The edges of
  the room contain artificial beehives"): six bee tanks across the top either side of the monitor,
  the bee machines below them, beehives down both sides, pods in the bottom corners, and the
  hive's honeycomb creeping in at its edges.

The rooms stand on a grid joined by long, narrow halls only so the server can tell them apart:
every visit, its RandomLayout (Dungeons.xml) picks some of them and joins them anew, always five
rooms between the start and the Queen (`<Goal path="5">`), the Queen's room and the laboratory each
ending a branch (DungeonGenerator: the breakable blocks are kept inside the laboratory's room,
which is only ever entered from the dead-end side, where the EH Waiting Floor marks its way in).
The rooms' monsters are the dungeon's own: Soldier Bees, Killer Bees, Mini Killer Bee Nests, larvae
and eggs (BehaviorDb.Nest.cs).

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

CELL, COLS, ROWS = 46, 4, 5
W, H = CELL * COLS, CELL * ROWS

FLOOR, DARK_L, DARK_R = "EH Floor", "EH Left Dark Floor", "EH Right Dark Floor"
LAB_FLOOR = "EH S Floor"
HONEY, UNDER_PATH = "EH Venom", "EH UnderPartialFloor Venom"
PATH = "EH Static Partial Floor"
WALLS = [("EH Wall", 50), ("EH Alt Wall 1", 25), ("EH Alt Wall 2", 20)]
ART = "EH Cracked Art Wall"
BOSS_WALLS = [("EH Boss Wall", 40), ("EH Alt Boss Wall 1", 25), ("EH Alt Boss Wall 2", 20),
              ("EH Medium Boss Wall 1", 6), ("EH Medium Boss Wall 2", 5), ("EH Medium Boss Wall 3", 4)]
BOSS_ART = "EH Boss Cracked Art Wall"
CHUNKS = ["EH Chunk 1", "EH Chunk 2", "EH Chunk 3", "EH Chunk 4"]
GATE, ENTRANCE = "EH Destructible Wall", "EH Waiting Floor"
HIVE_FLOORS = (FLOOR,)
BOSS_FLOORS = (DARK_L, DARK_R)
LAB_FLOORS = (LAB_FLOOR,)
WALKABLE = HIVE_FLOORS + BOSS_FLOORS + LAB_FLOORS + (HONEY, UNDER_PATH)

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


def pick(rnd, weighted):
    total = sum(w for _, w in weighted)
    r = rnd.random() * total
    for item, w in weighted:
        r -= w
        if r < 0:
            return item
    return weighted[-1][0]


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]
        self.style = [[None] * W for _ in range(H)]   # which walls a room gets: hive, boss or lab

    def inside(self, x, y):
        return 0 <= x < W and 0 <= y < H

    def octagon(self, cx, cy, a, b, c, ground, style):
        """A room with straight, stepped edges: within a tiles across, b tiles down and c tiles
        across and down together (the cut corners)."""
        for y in range(cy - b, cy + b + 1):
            for x in range(cx - a, cx + a + 1):
                if abs(x - cx) + abs(y - cy) <= c and self.inside(x, y):
                    self.ground[y][x] = ground
                    self.style[y][x] = style

    def rect(self, x0, y0, x1, y1, ground, style):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.ground[y][x] = ground
                self.style[y][x] = style

    def hall(self, a, b):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for yy in range(int(y - HALL), int(y + HALL) + 1):
                for xx in range(int(x - HALL), int(x + HALL) + 1):
                    if abs(xx - x) <= HALL and abs(yy - y) <= HALL and self.ground[yy][xx] is None:
                        self.ground[yy][xx] = FLOOR
                        self.style[yy][xx] = "hive"

    def walls(self, rnd):
        """A wall on every empty tile beside a floor, in the style of the room it borders: wax for
        the hive (a crack with honey running down it now and then, on a face you look at), darker
        wax for the Queen's chamber, navy metal for the laboratory (wax where the hive's honeycomb
        has crept in)."""
        found = []
        for y in range(H):
            for x in range(W):
                if self.ground[y][x] is not None:
                    continue
                near = [(dx, dy, self.style[y + dy][x + dx], self.ground[y + dy][x + dx])
                        for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                        if self.inside(x + dx, y + dy) and self.ground[y + dy][x + dx] is not None]
                if near:
                    styles = [s for _, _, s, _ in near]
                    style = "lab" if "lab" in styles else "boss" if "boss" in styles else "hive"
                    if style == "lab" and all(g == FLOOR for _, _, s, g in near if s == "lab"):
                        style = "hive"
                    found.append((x, y, style, near))
        for x, y, style, near in found:
            face = self.inside(x, y + 1) and self.ground[y + 1][x] is not None and self.obj[y + 1][x] is None
            if style == "lab":
                below = any(dy == 1 and dx == 0 for dx, dy, _, _ in near)
                above = any(dy == -1 and dx == 0 for dx, dy, _, _ in near)
                left = any(dx == -1 and dy == 0 for dx, dy, _, _ in near)
                right = any(dx == 1 and dy == 0 for dx, dy, _, _ in near)
                wall = ("EH S Wall Top" if below else "EH S Wall Bottom" if above
                        else "EH S Wall Left" if right else "EH S Wall Right" if left else "EH Small S Wall Clean")
                self.ground[y][x] = LAB_FLOOR
            elif style == "boss":
                wall = BOSS_ART if face and rnd.random() < 0.07 else pick(rnd, BOSS_WALLS)
                self.ground[y][x] = DARK_L
            else:
                wall = ART if face and rnd.random() < 0.06 else pick(rnd, WALLS)
                self.ground[y][x] = FLOOR
            self.obj[y][x] = wall
            self.style[y][x] = style

    def free(self, x, y, grounds=HIVE_FLOORS + BOSS_FLOORS + LAB_FLOORS):
        return self.inside(x, y) and self.ground[y][x] in grounds and self.obj[y][x] is None and self.region[y][x] is None

    def place(self, x, y, obj, grounds=WALKABLE):
        x, y = int(round(x)), int(round(y))
        assert self.free(x, y, grounds), (x, y, obj)
        self.obj[y][x] = obj

    def by_wall(self, x, y, d=2):
        return any(self.inside(x + dx, y + dy) and self.ground[y + dy][x + dx] is None
                   for dx in range(-d, d + 1) for dy in range(-d, d + 1))

    # --- the honey -------------------------------------------------------------------------------

    def honey(self, x, y, floors=HIVE_FLOORS):
        if self.inside(x, y) and self.ground[y][x] in floors and self.obj[y][x] is None:
            self.ground[y][x] = HONEY

    def broken(self, x, y):
        """A broken yellow tile: over honey it makes a path (the honey under it can't hurt)."""
        if not self.inside(x, y) or self.obj[y][x] is not None:
            return
        if self.ground[y][x] == HONEY:
            self.ground[y][x] = UNDER_PATH
            self.obj[y][x] = PATH
        elif self.ground[y][x] in HIVE_FLOORS + BOSS_FLOORS:
            self.obj[y][x] = PATH

    def rim(self, rnd, cells, share=0.55):
        """Broken tiles scattered over the edge of a patch of honey, inside and out, as RealmEye's
        screenshots show them: a dithered border."""
        edge = set()
        for x, y in cells:
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if self.inside(nx, ny) and self.ground[ny][nx] != HONEY:
                        edge.add((x, y))
                        edge.add((nx, ny))
        for x, y in sorted(edge):
            if (x + y) % 2 == 0 and rnd.random() < share + 0.25 or rnd.random() < share * 0.4:
                self.broken(x, y)

    def diamond(self, rnd, cx, cy, r, floors=HIVE_FLOORS):
        cells = [(x, y) for y in range(cy - r, cy + r + 1) for x in range(cx - r, cx + r + 1)
                 if abs(x - cx) + abs(y - cy) <= r and self.inside(x, y) and self.ground[y][x] in floors]
        for x, y in cells:
            self.honey(x, y, floors)
        self.rim(rnd, [(x, y) for x, y in cells if self.ground[y][x] == HONEY])

    def band(self, rnd, points, width=3, floors=HIVE_FLOORS):
        """A river of honey along a polyline (a chevron, a zigzag), edged with broken tiles."""
        cells = set()
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
            for i in range(n + 1):
                t = i / n
                x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
                for dx in range(-(width // 2), width - width // 2):
                    for dy in range(-(width // 2), width - width // 2):
                        cells.add((int(round(x)) + dx, int(round(y)) + dy))
        cells = [(x, y) for x, y in cells if self.inside(x, y) and self.ground[y][x] in floors and self.obj[y][x] is None]
        for x, y in cells:
            self.honey(x, y, floors)
        self.rim(rnd, cells, share=0.5)


def ring(cx, cy, r, n, start=0.0):
    return [(cx + math.cos(start + i * math.tau / n) * r, cy + math.sin(start + i * math.tau / n) * r) for i in range(n)]


def centre(cell):
    return cell[0] * CELL + CELL // 2, cell[1] * CELL + CELL // 2


def chevron(cx, cy, w, h, up=True):
    s = -1 if up else 1
    return [(cx - w, cy + s * h), (cx, cy - s * h), (cx + w, cy + s * h)]


def honeycomb_room(g, rnd, cx, cy, kind):
    """One of the hive's set pieces: its shape and its honey."""
    shape = kind % 6
    if shape == 0:
        # a clearing round a diamond pool (RealmEye's "Dungeon Clearing")
        g.octagon(cx, cy, 13, 12, 17, FLOOR, "hive")
        g.diamond(rnd, cx + rnd.choice((-2, 2)), cy + rnd.choice((-1, 1)), 5)
        reach = 12
    elif shape == 1:
        # chevrons of honey, like the start room's
        g.octagon(cx, cy, 13, 11, 16, FLOOR, "hive")
        up = rnd.random() < 0.5
        g.band(rnd, chevron(cx - 6, cy, 4, 4, up))
        g.band(rnd, chevron(cx + 6, cy, 4, 4, up))
        reach = 11
    elif shape == 2:
        # a cross of honey through the middle, the corners dry
        g.octagon(cx, cy, 12, 12, 17, FLOOR, "hive")
        g.band(rnd, [(cx - 9, cy), (cx + 9, cy)])
        g.band(rnd, [(cx, cy - 9), (cx, cy + 9)])
        reach = 12
    elif shape == 3:
        # a cell of the comb, a hexagon with flat top and bottom, two small pools
        g.octagon(cx, cy, 14, 10, 18, FLOOR, "hive")
        for dx in (-7, 7):
            g.diamond(rnd, cx + dx, cy + rnd.choice((-3, 3)), 3)
        reach = 10
    elif shape == 4:
        # a zigzag river across the room
        g.octagon(cx, cy, 12, 13, 17, FLOOR, "hive")
        if rnd.random() < 0.5:
            g.band(rnd, [(cx - 12, cy - 6), (cx - 4, cy + 2), (cx + 4, cy - 4), (cx + 12, cy + 4)], width=3)
        else:
            g.band(rnd, [(cx - 5, cy - 13), (cx + 3, cy - 4), (cx - 3, cy + 4), (cx + 5, cy + 13)], width=3)
        reach = 12
    else:
        # a long hall of a room with pools down its middle
        if rnd.random() < 0.5:
            g.octagon(cx, cy, 15, 8, 19, FLOOR, "hive")
            for dx in (-8, 0, 8):
                g.diamond(rnd, cx + dx, cy, 2)
        else:
            g.octagon(cx, cy, 8, 15, 19, FLOOR, "hive")
            for dy in (-8, 0, 8):
                g.diamond(rnd, cx, cy + dy, 2)
        reach = 8
    return reach


def chunks(g, rnd, cx, cy, reach, n, floors=HIVE_FLOORS):
    spots = [(x, y) for y in range(int(cy - reach * 1.5), int(cy + reach * 1.5) + 1)
             for x in range(int(cx - reach * 1.5), int(cx + reach * 1.5) + 1)
             if g.free(x, y, floors) and g.by_wall(x, y, 1)]
    for x, y in rnd.sample(spots, min(len(spots), n)):
        if not any(g.inside(x + dx, y + dy) and g.obj[y + dy][x + dx] in CHUNKS for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2)):
            g.obj[y][x] = rnd.choice(CHUNKS)


def furnish_honeycomb(g, rnd, cx, cy, reach, kind):
    chunks(g, rnd, cx, cy, reach, rnd.randint(4, 7))
    # its bees, off the honey and the walls
    squad = SQUADS[kind % len(SQUADS)]
    spots = [(round(x), round(y)) for x, y in ring(cx, cy, reach * 0.6, 10, rnd.random() * 6)
             + ring(cx, cy, reach * 0.35, 6, 1.2) + ring(cx, cy, reach * 0.8, 10, 0.4)]
    spots = [s for s in spots if g.free(*s, (FLOOR,)) and not g.by_wall(*s)]
    for monster, (px, py) in zip(squad, spots):
        g.place(px, py, monster)


def start_room(g, rnd, sx, sy):
    """RealmEye's "Start Room": an octagon with two chevrons of honey either side of the spawn."""
    g.octagon(sx, sy, 11, 11, 15, FLOOR, "hive")
    g.band(rnd, [(sx - 9, sy - 2), (sx - 5, sy + 3), (sx - 2, sy - 3)], width=2)
    g.band(rnd, [(sx + 2, sy - 3), (sx + 5, sy + 3), (sx + 9, sy - 2)], width=2)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            x, y = sx + dx, sy + 4 + dy
            g.ground[y][x] = FLOOR
            g.obj[y][x] = None
            g.region[y][x] = "Spawn"


def boss_room(g, rnd, cx, cy):
    """The Queen's chamber: a great octagon, dark floor (lighter to the east), a wide diamond of
    honey in its middle crossed by rings of broken tiles."""
    br = 16
    g.octagon(cx, cy, br, br, 22, DARK_L, "boss")
    for y in range(cy - br, cy + br + 1):
        for x in range(cx + 1, cx + br + 1):
            if g.ground[y][x] == DARK_L:
                g.ground[y][x] = DARK_R
    cells = [(x, y) for y in range(cy - 10, cy + 11) for x in range(cx - 10, cx + 11) if abs(x - cx) + abs(y - cy) <= 10]
    for x, y in cells:
        g.honey(x, y, BOSS_FLOORS)
    g.rim(rnd, cells)
    # rings of broken tiles across the honey, checkered
    for x, y in cells:
        d = abs(x - cx) + abs(y - cy)
        if d in (4, 5, 7) and (x + y) % 2 == 0:
            g.broken(x, y)
    return br


def furnish_boss(g, rnd, cx, cy, br):
    g.ground[cy][cx] = HONEY
    g.obj[cy][cx] = "EH King Bee"
    for (px, py), bee in zip(ring(cx, cy, 6, 6, 0.3), [YELLOW_KILLER, RED_KILLER, BLUE_KILLER] * 2):
        x, y = int(round(px)), int(round(py))
        if g.obj[y][x] is not None:
            g.obj[y][x] = None
            if g.ground[y][x] == UNDER_PATH:
                g.ground[y][x] = HONEY
        g.place(x, y, bee)
    for (px, py), egg in zip(ring(cx, cy, 13, 9, 0.0), EGGS * 3):
        g.place(px, py, egg, BOSS_FLOORS)
    chunks(g, rnd, cx, cy, br, 8, BOSS_FLOORS)


def laboratory(g, rnd, cx, cy):
    """The dead-end room and, behind breakable blocks in its north wall, the Beekeeper's
    laboratory, with the hive's honeycomb creeping in at its corners and edges."""
    # the dead-end room, entered from the south (where its marker stands); wide where it meets the
    # blocks, so the server sees one room through them
    g.octagon(cx, cy + 12, 7, 6, 10, FLOOR, "hive")
    g.rect(cx - 6, cy + 6, cx + 6, cy + 8, FLOOR, "hive")
    # the laboratory
    x0, y0, x1, y1 = cx - 9, cy - 12, cx + 8, cy + 4
    g.rect(x0, y0, x1, y1, LAB_FLOOR, "lab")
    for px, py, r in [(x0, y0 + 1, 2.5), (x1, y0 + 1, 2.5), (x0, y1 - 1, 2), (x1, y1, 2.5),
                      (x0 + 5, y1, 1.5), (x1 - 4, y1, 1.5), (x1, y0 + 9, 1.5)]:
        for y in range(int(py - r) - 1, int(py + r) + 2):
            for x in range(int(px - r) - 1, int(px + r) + 2):
                if g.inside(x, y) and g.ground[y][x] == LAB_FLOOR and math.hypot(x - px, y - py) <= r + rnd.uniform(-0.4, 0.4):
                    g.ground[y][x] = FLOOR
    # the breakable blocks between them, three wide
    for x in (cx - 1, cx, cx + 1):
        g.ground[cy + 5][x] = FLOOR
        g.obj[cy + 5][x] = GATE
        g.style[cy + 5][x] = "hive"


def furnish_laboratory(g, rnd, cx, cy):
    ay = cy + 12
    g.place(cx, ay + 4, ENTRANCE)
    for (px, py), larva in zip(ring(cx, ay, 3, 3, 0.5), [YELLOW_LARVA, RED_LARVA, BLUE_LARVA]):
        g.place(px, py, larva)
    chunks(g, rnd, cx, ay, 6, 4)
    x0, y0, x1, y1 = cx - 9, cy - 12, cx + 8, cy + 4
    # the large monitor in the middle of the top, six chambers (the bee tanks, a Soldier Bee in
    # each) either side of it
    for dx, part in zip((-2, -1, 0, 1), ("EH Bee Screen Left", "EH Bee Screen Middle Left",
                                         "EH Bee Screen Middle Right", "EH Bee Screen Right")):
        g.place(cx + dx, y0, part)
    tanks = ["EH Yellow Bee Tank", "EH Red Bee Tank", "EH Blue Bee Tank"]
    for i, dx in enumerate((-7, -5, -3, 3, 5, 7)):
        g.place(cx + dx, y0 + 1, tanks[i % 3])
    # the bee machines under the outer tanks
    g.place(x0 + 2, y0 + 3, "EH Big S Light")
    g.place(x1 - 2, y0 + 3, "EH Big S Dark")
    # artificial beehives down both edges
    for i, y in enumerate((y0 + 6, y0 + 9, y0 + 12)):
        g.place(x0, y, ["EH Apiary", "EH Apiary 2", "EH Apiary 3"][i])
        g.place(x1, y, ["EH Apiary 3", "EH Apiary", "EH Apiary 2"][i])
    # pods in the bottom corners
    g.place(x0 + 1, y1 - 1, "EH Purple S Dark")
    g.place(x0 + 3, y1, "EH Purple S Light")
    g.place(x1 - 1, y1 - 1, "EH Small S Dark")
    g.place(x1 - 3, y1, "EH Small S Light")
    g.place(cx, y0 + 4, "EH Beekeeper")


def build():
    g = Grid()
    rnd = random.Random(1717)
    cells = [(c, r) for r in range(ROWS) for c in range(COLS)]
    fillers = [c for c in cells if c not in (START_CELL, BOSS_CELL, LAB_CELL)]
    # the halls first, under the rooms: across each row and down the middle columns; the
    # laboratory only from the room below it
    lx, ly = centre(LAB_CELL)
    for r in range(ROWS):
        for c in range(COLS - 1):
            if LAB_CELL not in ((c, r), (c + 1, r)):
                g.hall(centre((c, r)), centre((c + 1, r)))
    for c in (1, 3):
        for r in range(ROWS - 1):
            g.hall(centre((c, r)), centre((c, r + 1)))
    g.hall(centre((LAB_CELL[0], LAB_CELL[1] + 1)), (lx, ly + 16))

    sx, sy = centre(START_CELL)
    start_room(g, rnd, sx, sy)
    bx, by = centre(BOSS_CELL)
    br = boss_room(g, rnd, bx, by)
    laboratory(g, rnd, lx, ly)
    rooms = []
    for i, cell in enumerate(fillers):
        cx, cy = centre(cell)
        rooms.append((cx, cy, honeycomb_room(g, rnd, cx, cy, i), i))
    g.walls(rnd)

    chunks(g, rnd, sx, sy, 11, 5)
    furnish_boss(g, rnd, bx, by, br)
    furnish_laboratory(g, rnd, lx, ly)
    for cx, cy, reach, i in rooms:
        furnish_honeycomb(g, rnd, cx, cy, reach, i)
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
