"""Builds the Stair Tower, the prototype of a dungeon of floors (world `testadmin1`).

    python tools/build_stair_tower.py [--preview]

Writes source/Shared/resources/worlds/Dungeons/Stair Tower.jm from scratch. Admins open it with
`/dungeon testadmin1`; nothing in the game leads there. Three floors, one above the other, each
with its boss, joined by stairs that climb for 20 seconds. The floors lie one after another up the
flat map, and the height is all show: StairTowerWorld (the world server) keeps the order. From
south to north:

- the vestibule, where players arrive (Spawn region) with a Nexus Portal to leave by;
- floor 1, Dreadstump the Pirate King's;
- the first stairs, closed by a door of walls (Tower Door) until floor 1's boss (Tower Boss)
  dies;
- floor 2, Stheno the Snake Queen's, the second stairs, then floor 3, Arachna the Spider Queen's,
  with a Nexus Portal at its far end.

Each stairwell loops: whoever crosses its loop row (Stair Loop) before they've climbed for 20
seconds since its first row (Stair Start) is moved back down SEGMENT tiles, onto stairs that look
the same, so the stairs go on as long as the server likes on a short map. Everything in a
stairwell repeats every SEGMENT tiles (the steps, the walls, a torch every TORCH_EVERY rows), and
MARGIN rows of it lie either side of the loop, so the jump shows nothing new on the screen.

The output is generated: change this script and run it again rather than editing the map.
"""
import base64
import json
import os
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Stair Tower.jm")

W, H = 48, 204
CX = 24                     # the map's spine: every floor and stairwell is centred on this column

ROOM_HALF, ROOM_H = 12, 21  # each floor: 25 tiles across, 21 deep
STAIR_HALF = 2              # the stairwells: 5 tiles across
SEGMENT = 8                 # how far the loop moves a climber back down (StairTowerWorld.Segment)
TORCH_EVERY = 4             # a torch on each wall of the stairwell every this many rows; divides SEGMENT
MARGIN = 24                 # rows of stairs either side of the loop, more than anyone sees
DOOR_ROWS = 2

FLOOR = "Blue Quad"
EDGE = "Blue Closed"
STEPS = "O3 Stair Sanctuary N"
WALL = "Blue Wall"
TORCH = "Blue Torch Wall"
PORTAL = "Nexus Portal"

BOSSES = ["Dreadstump the Pirate King", "Stheno the Snake Queen", "Arachna the Spider Queen"]

assert SEGMENT % TORCH_EVERY == 0


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def floor(self, x, y, ground):
        self.ground[y][x] = ground

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] is not None

    def place(self, x, y, obj, region=None):
        assert self.is_floor(x, y), (obj, x, y)
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj
        if region:
            self.region[y][x] = region


def room(g, y0, y1):
    """A floor: blue quads edged with closed tiles."""
    x0, x1 = CX - ROOM_HALF, CX + ROOM_HALF
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            g.floor(x, y, EDGE if x in (x0, x1) or y in (y0, y1) else FLOOR)


def stairs(g, top):
    """A stairwell from `top` down: MARGIN rows, the loop row, SEGMENT + MARGIN rows below it, its
    first row and the door. Gives the row below its door."""
    loop = top + MARGIN
    start = loop + SEGMENT + MARGIN
    door = start + 1
    for y in range(top, door + DOOR_ROWS):
        for x in range(CX - STAIR_HALF, CX + STAIR_HALF + 1):
            g.floor(x, y, STEPS)
            if y == loop:
                g.region[y][x] = "Stair Loop"
            elif y == start:
                g.region[y][x] = "Stair Start"
            elif y >= door:
                g.place(x, y, WALL, "Tower Door")
    return door + DOOR_ROWS


def build():
    g = Grid()
    y = 4
    floors = []
    # from the top floor down: floor 3, its stairs, floor 2, its stairs, floor 1
    for i, boss in reversed(list(enumerate(BOSSES))):
        y0, y1 = y, y + ROOM_H - 1
        room(g, y0, y1)
        floors.append((i, boss, y0, y1))
        y = y1 + 1
        if i > 0:
            y = stairs(g, y)
    floor1_bottom = y - 1

    # the vestibule below floor 1, through a short passage
    for yy in range(floor1_bottom + 1, floor1_bottom + 4):
        for x in range(CX - 2, CX + 3):
            g.floor(x, yy, EDGE)
    vy0, vy1 = floor1_bottom + 4, floor1_bottom + 12
    vx0, vx1 = CX - 6, CX + 6
    for yy in range(vy0, vy1 + 1):
        for x in range(vx0, vx1 + 1):
            g.floor(x, yy, EDGE if x in (vx0, vx1) or yy in (vy0, vy1) else FLOOR)
    assert vy1 + 3 < H, vy1

    # walls: two tiles thick round every floor tile; in the stairwells a torch every TORCH_EVERY
    # rows, the same on each loop, elsewhere one here and there
    for yy in range(H):
        for x in range(W):
            if g.is_floor(x, yy):
                continue
            near = [(dx, dy) for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2) if g.is_floor(x + dx, yy + dy)]
            if not near:
                continue
            face = any(abs(dx) + abs(dy) == 1 for dx, dy in near)
            in_stairs = any(g.ground[yy + dy][x + dx] == STEPS for dx, dy in near)
            if in_stairs:
                torch = face and yy % TORCH_EVERY == 0
            else:
                torch = face and (x * 7 + yy * 3) % 11 == 0
            g.obj[yy][x] = TORCH if torch else WALL

    # the bosses, in the middle of their floors
    for i, boss, y0, y1 in floors:
        g.place(CX, (y0 + y1) // 2, boss, "Tower Boss")
    # the way out at the top
    top = next(f for f in floors if f[0] == len(BOSSES) - 1)
    g.place(CX, top[2] + 2, PORTAL)

    # the vestibule
    for yy in range(vy0 + 2, vy1 - 1):
        for x in range(vx0 + 2, vx1 - 1):
            if abs(x - CX) >= 2:
                g.region[yy][x] = "Spawn"
    g.place(CX, vy1 - 1, PORTAL)
    return g


def save(g):
    entries, index = [], {}
    out = bytearray()
    for y in range(H):
        for x in range(W):
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
    jm = {"width": W, "height": H, "dict": entries,
          "data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii")}
    with open(OUT, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    print(f"wrote {OUT} ({W}x{H}, {len(entries)} tile kinds)")


def preview(g):
    """Print the map as text: # wall, t torch, D door, B boss, N portal, S spawn, L loop row,
    s first row of the stairs, = steps."""
    marks = {WALL: "#", TORCH: "t", PORTAL: "N"}
    for y in range(H):
        row = ""
        for x in range(W):
            o, r = g.obj[y][x], g.region[y][x]
            if r == "Tower Door":
                row += "D"
            elif r == "Tower Boss":
                row += "B"
            elif o:
                row += marks.get(o, "?")
            elif r:
                row += {"Spawn": "S", "Stair Loop": "L", "Stair Start": "s"}[r]
            elif g.ground[y][x]:
                row += "=" if g.ground[y][x] == STEPS else "."
            else:
                row += " "
        print("%3d %s" % (y, row.rstrip()))


if __name__ == "__main__":
    grid = build()
    if "--preview" in sys.argv:
        preview(grid)
    else:
        save(grid)
