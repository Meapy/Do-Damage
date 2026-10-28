"""Builds the Lost Halls.

    python tools/build_lost_halls.py [--preview]

Writes source/Shared/resources/worlds/Dungeons/Lost Halls.jm from scratch: the marble rooms the
world's <RandomLayout> (Dungeons.xml) joins in a new order for every visit, on a grid of halls that
is also the layout used when that fails. The spawn room (Spawn region) is the root; the Colossus
room (`LH Monster Spawner`, the goal) ends a branch. The other rooms, each its own kind as in the
original game:
- the Crusade (Crusade soldiers, shipwrights and explorers round their Commander);
- Oryx's army (swordsmen, armorbearers and admirals round the Champion of Oryx);
- the Grotto (bats, rats and slimes over pools, round the Grotto Blob);
- the Golems (Anger, Fear and Sorrow round the Tormented Golem);
- the Ghost Bridges (a cross of floor over a pit, the Spectral Sentries floating over it);
- the Agonized Titan's hall;
- the Void rifts (Void Fragments and Shades round a Greater Void Shade);
- the trap halls (grenade traps in the floor, arrow traps along the walls);
- the pot room, the treasure room of seven pots (some hold an Evil Spirit).

The behaviours are in BehaviorDb.LostHallsRooms.cs (the rooms and the Colossus room's controller)
and BehaviorDb.LostHalls.cs (the Marble Colossus). The map is generated: change this script and
run it again rather than editing it.
"""
import base64
import json
import os
import random
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUNGEONS = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons")
LOST_HALLS = os.path.join(DUNGEONS, "Lost Halls.jm")

WALL = "LH Main Wall"
FLOOR = "LH Main Tile"
HALL = "LH Hall Tile"
EDGE = {"UL": "LH Main Tile UL", "U": "LH Main Tile U", "UR": "LH Main Tile UR",
        "L": "LH Main Tile L", "R": "LH Main Tile R",
        "BL": "LH Main Tile BL", "B": "LH Main Tile B", "BR": "LH Main Tile BR"}
GEM = "LH Gem Tile"
BLOODY = "LH Main Tile Bloody"
WATER = "LH Water Tile"
HOLE = "LH Hole Tile"
TITAN_TILE = "LH Titan Tile"
CORRIDOR = 7                  # the halls between rooms (RandomLayout passage="7" takes them for corridors)
PITCH = 34                    # the grid's cells: the largest room (27 with its walls) and a hall


class Grid:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.ground = [[None] * w for _ in range(h)]
        self.obj = [[None] * w for _ in range(h)]
        self.region = [[None] * w for _ in range(h)]

    def inside(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def floor(self, x, y, ground):
        self.ground[y][x] = ground
        self.obj[y][x] = None

    def is_floor(self, x, y):
        return self.inside(x, y) and self.ground[y][x] is not None and self.obj[y][x] != WALL

    def place(self, x, y, obj):
        assert self.ground[y][x] is not None and self.ground[y][x] != HOLE, (obj, x, y)
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj


# --- the Lost Halls ----------------------------------------------------------------------------

class Room:
    """A room's floor, w by h, with its top-left corner at (x0, y0) once it is placed."""

    def __init__(self, kind, w, h, fill, round_=False):
        self.kind, self.w, self.h, self.fill, self.round = kind, w, h, fill, round_
        self.x0 = self.y0 = 0

    def cells(self):
        for y in range(self.h):
            for x in range(self.w):
                if not self.round or (x - (self.w - 1) / 2) ** 2 + (y - (self.h - 1) / 2) ** 2 <= ((self.w - 1) / 2 + 0.5) ** 2:
                    yield x, y

    def centre(self):
        return self.x0 + self.w // 2, self.y0 + self.h // 2


def paint(g, room):
    """The room's marble: a border of edge tiles round plain tiles, a gem in the middle."""
    cells = set(room.cells())
    for x, y in cells:
        n, s, w, e = (x, y - 1) not in cells, (x, y + 1) not in cells, (x - 1, y) not in cells, (x + 1, y) not in cells
        key = ("U" if n else "B" if s else "") + ("L" if w else "R" if e else "")
        g.floor(room.x0 + x, room.y0 + y, EDGE.get(key, FLOOR) if not room.round else FLOOR)
    cx, cy = room.centre()
    g.floor(cx, cy, GEM)


def at(room, dx, dy):
    """A spot in the room by its offset from the centre."""
    cx, cy = room.centre()
    return cx + dx, cy + dy


def put(g, room, dx, dy, obj):
    x, y = at(room, dx, dy)
    g.place(x, y, obj)


def ring(g, room, radius, objs, start=0):
    """objs round the room's centre at radius, evenly spaced."""
    import math
    for i, obj in enumerate(objs):
        a = math.radians(start + 360 * i / len(objs))
        put(g, room, round(radius * math.cos(a)), round(radius * math.sin(a)), obj)


def fill_spawn(g, room):
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            x, y = at(room, dx, dy)
            g.region[y][x] = "Spawn"
    for dx, dy in ((-4, -4), (4, -4), (-4, 4), (4, 4)):
        put(g, room, dx, dy, "LH Broken Pillar")


def fill_crusade(g, room):
    put(g, room, 0, 0, "LH Commander of the Crusade")
    ring(g, room, 4, ["LH Crusade Soldier", "LH Crusade Shipwright", "LH Crusade Explorer"] * 2 + ["LH Crusade Soldier"], 20)
    for dx, dy in ((-6, 6), (6, -6)):
        put(g, room, dx, dy, "LH Remains")


def fill_oryx(g, room):
    put(g, room, 0, 0, "LH Champion of Oryx")
    ring(g, room, 4, ["LH Oryx Swordsman", "LH Oryx Armorbearer", "LH Oryx Admiral"] * 2, 0)
    for dx, dy in ((-6, -6), (6, 6)):
        put(g, room, dx, dy, "LH Broken Pillar")


def fill_grotto(g, room):
    rnd = random.Random(room.x0 * 31 + room.y0)
    for px, py in ((-5, -5), (4, -4), (-4, 5), (5, 4)):
        for dy in range(-1, 2):
            for dx in range(-1, 2):
                if rnd.random() < 0.85:
                    x, y = at(room, px + dx, py + dy)
                    g.floor(x, y, WATER)
    put(g, room, 0, 0, "LH Grotto Blob")
    ring(g, room, 3, ["LH Grotto Bat", "LH Grotto Rat", "LH Grotto Slime"] * 2 + ["LH Grotto Bat", "LH Grotto Rat"], 10)
    for dx, dy in ((-7, 1), (7, -1), (1, 7)):
        put(g, room, dx, dy, "LH Slime Goop")


def fill_golem(g, room):
    put(g, room, 0, 0, "LH Tormented Golem")
    ring(g, room, 4, ["LH Golem of Anger", "LH Golem of Fear", "LH Golem of Sorrow", "LH Golem of Anger"], 45)
    for dx, dy in ((-6, 0), (6, 0)):
        put(g, room, dx, dy, "LH Remains")


def fill_ghost(g, room):
    # a cross of floor nine wide over a pit: the corners of the room are holes
    arm = 4
    for x, y in room.cells():
        dx, dy = x - room.w // 2, y - room.h // 2
        if abs(dx) > arm and abs(dy) > arm:
            g.floor(room.x0 + x, room.y0 + y, HOLE)
    for dx, dy in ((0, -8), (0, 8)):
        put(g, room, dx, dy, "LH Spectral Sentry S")
    for dx, dy in ((-8, 0), (8, 0)):
        put(g, room, dx, dy, "LH Spectral Sentry E")
    put(g, room, 0, 0, "LH Halls Pillar")


def fill_titan(g, room):
    for x, y in room.cells():
        dx, dy = x - room.w // 2, y - room.h // 2
        if max(abs(dx), abs(dy)) <= 5:
            g.floor(room.x0 + x, room.y0 + y, TITAN_TILE)
    put(g, room, 0, 0, "LH Agonized Titan")
    for dx, dy in ((-6, -6), (6, -6), (-6, 6), (6, 6)):
        put(g, room, dx, dy, "LH Halls Pillar")


def fill_void(g, room):
    rnd = random.Random(room.x0 * 17 + room.y0)
    for x, y in room.cells():
        if rnd.random() < 0.18:
            g.floor(room.x0 + x, room.y0 + y, BLOODY)
    put(g, room, 0, 0, "LH Greater Void Shade")
    ring(g, room, 4, ["LH Void Fragment", "LH Void Shade", "LH Void Fragment", "LH Void Shade", "LH Void Fragment"], 30)


def fill_traps(g, room):
    for dx, dy in ((-4, -4), (0, -5), (4, -3), (-5, 1), (0, 0), (5, 2), (-3, 5), (3, 5)):
        put(g, room, dx, dy, "LH Grenade Trap")
    half = room.w // 2
    for d in (-5, 0, 5):
        put(g, room, d, -half, "LH Wall Trap N")
        put(g, room, d, half, "LH Wall Trap S")
        put(g, room, -half, d + 2, "LH Wall Trap W")
        put(g, room, half, d - 2, "LH Wall Trap E")


def fill_pots(g, room):
    for i, (dx, dy) in enumerate(((-3, -3), (0, -4), (3, -3), (-4, 0), (4, 0), (-2, 3), (2, 3))):
        put(g, room, dx, dy, f"LH Pot {i + 1}")
    put(g, room, 0, 0, "LH Big Candle")
    for dx, dy in ((-5, -5), (5, -5), (-5, 5), (5, 5)):
        put(g, room, dx, dy, "LH Small Candle")


def fill_boss(g, room):
    # the room the Marble Colossus wakes in: its four Marble Defenders stand round the controller
    # (BehaviorDb.LostHalls: once they fall the Colossus rises, or the way to the Cultist Hideout)
    put(g, room, 0, 0, "LH Monster Spawner")
    put(g, room, 0, -7, "LH Marble Defender Activator N")
    put(g, room, 7, 0, "LH Marble Defender Activator E")
    put(g, room, 0, 7, "LH Marble Defender Activator S")
    put(g, room, -7, 0, "LH Marble Defender Activator W")
    for dx, dy in ((-9, -9), (9, -9), (-9, 9), (9, 9)):
        put(g, room, dx, dy, "LH Halls Pillar")


def lost_halls_rooms():
    # (column, row) on the grid; row 0 holds the Colossus alone, the spawn is at the bottom
    R = Room
    return {
        (1, 0): R("boss", 31, 31, fill_boss, round_=True),
        (0, 1): R("crusade", 17, 17, fill_crusade), (1, 1): R("oryx", 17, 17, fill_oryx),
        (2, 1): R("golem", 17, 17, fill_golem),
        (0, 2): R("ghost", 25, 25, fill_ghost), (1, 2): R("void", 17, 17, fill_void),
        (2, 2): R("grotto", 19, 19, fill_grotto),
        (0, 3): R("titan", 21, 21, fill_titan), (1, 3): R("traps", 17, 17, fill_traps),
        (2, 3): R("crusade", 17, 17, fill_crusade),
        (0, 4): R("grotto", 19, 19, fill_grotto), (1, 4): R("pots", 13, 13, fill_pots),
        (2, 4): R("oryx", 17, 17, fill_oryx),
        (0, 5): R("void", 17, 17, fill_void), (1, 5): R("golem", 17, 17, fill_golem),
        (2, 5): R("traps", 17, 17, fill_traps),
        (1, 6): R("spawn", 13, 13, fill_spawn),
    }


def build_lost_halls():
    rooms = lost_halls_rooms()
    cols = max(c for c, _ in rooms) + 1
    rows = max(r for _, r in rooms) + 1
    margin = 4
    g = Grid(cols * PITCH + 2 * margin, rows * PITCH + 2 * margin)

    def cell_centre(c, r):
        return margin + c * PITCH + PITCH // 2, margin + r * PITCH + PITCH // 2

    for (c, r), room in rooms.items():
        cx, cy = cell_centre(c, r)
        room.x0, room.y0 = cx - room.w // 2, cy - room.h // 2
        paint(g, room)

    # halls: along each row, and down the middle column (and from the boss room to the row below)
    half = CORRIDOR // 2

    def hall(c0, r0, c1, r1):
        (ax, ay), (bx, by) = cell_centre(c0, r0), cell_centre(c1, r1)
        for x in range(min(ax, bx) - (half if ax == bx else 0), max(ax, bx) + (half + 1 if ax == bx else 1)):
            for y in range(min(ay, by) - (half if ay == by else 0), max(ay, by) + (half + 1 if ay == by else 1)):
                if g.ground[y][x] is None:
                    g.floor(x, y, HALL)

    for (c, r) in rooms:
        if (c + 1, r) in rooms:
            hall(c, r, c + 1, r)
        if (c, r + 1) in rooms and (c == 1 or r == 0):
            hall(c, r, c, r + 1)
    # side columns joined at their ends too, so the fixed map has loops as the original does
    hall(0, 1, 0, 2)
    hall(2, 4, 2, 5)

    for (c, r), room in rooms.items():
        room.fill(g, room)

    walls_around(g)
    return g


def walls_around(g):
    for y in range(g.h):
        for x in range(g.w):
            if g.ground[y][x] is not None or g.obj[y][x]:
                continue
            if any(g.inside(x + i, y + k) and g.ground[y + k][x + i] is not None
                   for i in (-1, 0, 1) for k in (-1, 0, 1)):
                g.obj[y][x] = WALL


# --- reading and writing .jm maps --------------------------------------------------------------

def save(g, path):
    entries, index = [], {}
    out = bytearray()
    for y in range(g.h):
        for x in range(g.w):
            t = {}
            if g.ground[y][x]:
                t["ground"] = g.ground[y][x]
            if g.obj[y][x]:
                t["objs"] = [{"id": g.obj[y][x]}]
            if g.region[y][x]:
                t["regions"] = [{"id": g.region[y][x]}]
            key = json.dumps(t, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(t)
            i = index[key]
            out += bytes((i >> 8, i & 0xff))
    jm = {"width": g.w, "height": g.h, "dict": entries,
          "data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii")}
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    print(f"wrote {os.path.relpath(path, ROOT)} ({g.w}x{g.h}, {len(entries)} tile kinds)")


def preview(g):
    marks = {WALL: "#", "LH Monster Spawner": "C"}
    for y in range(g.h):
        row = ""
        for x in range(g.w):
            o = g.obj[y][x]
            if o:
                row += marks.get(o, "#" if "Wall" in o else o.split()[-1][0].lower() if o.startswith("LH ") else "o")
            elif g.region[y][x]:
                row += "S"
            else:
                row += {HALL: ",", HOLE: " ", WATER: "~", None: " "}.get(g.ground[y][x], ".")
        print(row.rstrip())


if __name__ == "__main__":
    halls = build_lost_halls()
    if "--preview" in sys.argv:
        preview(halls)
    else:
        save(halls, LOST_HALLS)
