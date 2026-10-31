"""Builds the Stair Tower, the prototype of real height (world `testadmin1`).

    python tools/build_stair_tower.py [--preview]

Writes source/Shared/resources/worlds/Dungeons/Stair Tower.jm (the lowest floor) and
`Stair Tower 1.jm`, `Stair Tower 2.jm` (the floors above it, FLOOR_HEIGHT tiles apart), all the
same size, from scratch. Admins open it with `/dungeon testadmin1`; nothing in the game leads
there. StairTowerWorld stacks the maps over the same ground (World.Levels): everything has a
height, walks on the floor at its height and neither sees nor reaches what is on another floor.

Each floor hangs over the north end of the one below, so from below you see its edge and the
wall under it, and from above you look down onto the floor below:

- floor 1, Dreadstump the Pirate King's, with the vestibule south of it, where players arrive
  (Spawn region) with a Nexus Portal to leave by;
- the east stairs, five steps (`ST Step 1` to `5`, each lifting what stands on it half a tile
  higher, `<Lift>`) from floor 1 up to a landing on floor 2, closed by a door of walls (Tower
  Door) until floor 1's boss (Tower Boss) dies;
- floor 2, Stheno the Snake Queen's, and the west stairs up to floor 3, Arachna the Spider
  Queen's, with a Nexus Portal at its far end.

Walls stand round each floor, except where a lower floor lies beside it: there its edge is open
and drops to the floor below.

The output is generated: change this script and run it again rather than editing the maps.
"""
import base64
import json
import os
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons")
NAME = "Stair Tower"

W, H = 56, 72
FLOOR_HEIGHT = 3            # tiles between floors (StairTowerWorld.FloorHeight)
STEPS = 5                   # steps per flight, FLOOR_HEIGHT / (STEPS + 1) tiles each
CX = 28                     # the floors' middle column

# Blue Quad and Blue Closed, with Blue Wall's stone under an edge that drops (CustomGrounds.xml)
FLOOR = "ST Floor"
EDGE = "ST Floor Edge"
WALL = "Blue Wall"
TORCH = "Blue Torch Wall"
PORTAL = "Nexus Portal"

# each floor: its rectangle (x0, y0, x1, y1, inclusive) and boss; floor n + 1 overlaps the north
# end of floor n
FLOORS = [
    ((6, 34, 50, 56), "Dreadstump the Pirate King"),
    ((6, 14, 50, 38), "Stheno the Snake Queen"),
    ((6, 3, 50, 18), "Arachna the Spider Queen"),
]
# each flight: on floor n, up to floor n + 1: its columns, and the rows of its steps from the
# bottom (south) up; the door is the row below the bottom step
FLIGHTS = [
    ((48, 50), 43),         # floor 1's, in its north-east corner, up to floor 2's landing
    ((6, 8), 23),           # floor 2's, at its west end, up to floor 3
]
VESTIBULE = (22, 61, 34, 68)


class Level:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] is not None

    def place(self, x, y, obj, region=None):
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj
        if region:
            self.region[y][x] = region


def build():
    levels = [Level() for _ in FLOORS]
    for n, ((x0, y0, x1, y1), boss) in enumerate(FLOORS):
        g = levels[n]
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                g.ground[y][x] = EDGE if x in (x0, x1) or y in (y0, y1) else FLOOR

    # the flights: steps rising north, each half a tile (STEPS of them, the floor above at the top)
    for n, ((sx0, sx1), bottom) in enumerate(FLIGHTS):
        g = levels[n]
        for k in range(STEPS):
            for x in range(sx0, sx1 + 1):
                g.ground[bottom - k][x] = "ST Step %d" % (k + 1)
        for x in range(sx0, sx1 + 1):
            g.place(x, bottom + 1, WALL, "Tower Door")

    # the vestibule below floor 1, through a short passage
    vx0, vy0, vx1, vy1 = VESTIBULE
    g0 = levels[0]
    for y in range(FLOORS[0][0][3] + 1, vy0):
        for x in range(CX - 2, CX + 3):
            g0.ground[y][x] = EDGE
    for y in range(vy0, vy1 + 1):
        for x in range(vx0, vx1 + 1):
            g0.ground[y][x] = EDGE if x in (vx0, vx1) or y in (vy0, vy1) else FLOOR
    for y in range(vy0 + 2, vy1 - 1):
        for x in range(vx0 + 2, vx1 - 1):
            if abs(x - CX) >= 2:
                g0.region[y][x] = "Spawn"
    g0.place(CX, vy1 - 1, PORTAL)

    # walls two tiles thick round each floor, but none where a lower floor lies: the edge drops
    for n, g in enumerate(levels):
        below = levels[:n]
        for y in range(H):
            for x in range(W):
                if g.is_floor(x, y) or any(b.is_floor(x, y) for b in below):
                    continue
                near = [(dx, dy) for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2) if g.is_floor(x + dx, y + dy)]
                if not near or g.obj[y][x] is not None:
                    continue
                face = any(abs(dx) + abs(dy) == 1 for dx, dy in near)
                g.obj[y][x] = TORCH if face and (x * 7 + y * 3) % 11 == 0 else WALL

    # the bosses, in the middle of their floors (clear of the floor above's overhang)
    for n, ((x0, y0, x1, y1), boss) in enumerate(FLOORS):
        top = FLOORS[n + 1][0][3] + 1 if n + 1 < len(FLOORS) else y0
        levels[n].place(CX, (top + y1) // 2, boss, "Tower Boss")
    x0, y0, x1, y1 = FLOORS[-1][0]
    levels[-1].place(CX, y0 + 2, PORTAL)
    return levels


def save(g, path):
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
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    print(f"wrote {path} ({W}x{H}, {len(entries)} tile kinds)")


def preview(levels):
    """Each floor as text: # wall, t torch, D door, B boss, N portal, S spawn, 1-5 steps,
    . floor, ' empty."""
    for n, g in enumerate(levels):
        print(f"--- floor {n + 1} (height {n * FLOOR_HEIGHT})")
        for y in range(H):
            row = ""
            for x in range(W):
                o, r, gr = g.obj[y][x], g.region[y][x], g.ground[y][x]
                if r == "Tower Door":
                    row += "D"
                elif r == "Tower Boss":
                    row += "B"
                elif o:
                    row += {WALL: "#", TORCH: "t", PORTAL: "N"}.get(o, "?")
                elif r == "Spawn":
                    row += "S"
                elif gr and gr.startswith("ST Step"):
                    row += gr[-1]
                elif gr:
                    row += "."
                else:
                    row += " "
            if row.strip():
                print("%3d %s" % (y, row.rstrip()))


if __name__ == "__main__":
    floors = build()
    if "--preview" in sys.argv:
        preview(floors)
    else:
        for n, level in enumerate(floors):
            save(level, os.path.join(OUT, NAME + (" %d" % n if n else "") + ".jm"))
