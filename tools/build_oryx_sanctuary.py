"""Builds Oryx's Sanctuary, the Oryx 3 dungeon of the Oryx chain.

    python tools/build_oryx_sanctuary.py

Writes source/Shared/resources/worlds/Dungeons/Oryx's Sanctuary/Oryx's Sanctuary.jm from scratch,
using the Exalt sanctuary art both clients already have (the O3 floor, rug, wall, banner and torch
pieces, laid out the way the court maps in the same folder use them). From south to north:

- the vestibule, where players arrive (Spawn region) with a Nexus Portal to leave by;
- the Gallery, a corridor guarded by Oryx's nobles;
- the Court, a hall held by his officers (west) and clergy (east);
- the Gate, sealed by O3 Wall Gates until the Court's guards are dead (`O3 Gate Deleter 1` watches,
  see BehaviorDb.OryxSanctuary.cs);
- the Sanctum, a round arena with Oryx the Mad God 3 at its centre.

The output is generated: change this script and run it again rather than editing the map.
"""
import base64
import json
import math
import os
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Oryx's Sanctuary",
                   "Oryx's Sanctuary.jm")

W, H = 80, 136
CX = 40                     # the map's spine: every room is centred on this column

ARENA_C = (CX, 30)          # the Sanctum
ARENA_R = 19.5
GATE_Y = (52, 53)           # two rows of gates in the Sanctum's corridor
COURT = (19, 71, 61, 99)    # x0, y0, x1, y1 of the Court's floor (inclusive)
VESTIBULE = (31, 120, 49, 130)

NORMAL = "O3 Normal Tile"
RUG = "O3 Sanctuary Rug Base"
WALL = "O3 Wall 1"
ARCH = ("O3 Wall Arch 1", "O3 Wall Arch 2")
COLUMN = "O3 Wall Column 1"
TORCH = "O3C Torch Wall Flame"


def sdf(x, y):
    """The sanctuary's large patterned floor: a 4x4 tile repeating motif."""
    return f"O3 SDF {((y + 1) % 4) * 4 + x % 4} B"


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def floor(self, x, y, ground):
        self.ground[y][x] = ground

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] is not None

    def place(self, x, y, obj):
        assert self.is_floor(x, y), (obj, x, y)
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj


def rect(g, x0, y0, x1, y1, ground):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            g.floor(x, y, ground(x, y) if callable(ground) else ground)


def rug_vertical(g, x, y0, y1):
    """A three-wide red rug running north-south, centred on column x."""
    for y in range(y0, y1 + 1):
        g.floor(x - 1, y, "O3 Sanctuary Rug D Edge L")
        g.floor(x, y, RUG)
        g.floor(x + 1, y, "O3 Sanctuary Rug D Edge R")


def rug_horizontal(g, y, x0, x1):
    for x in range(x0, x1 + 1):
        g.floor(x, y - 1, "O3 Sanctuary Rug D Edge U")
        g.floor(x, y, RUG)
        g.floor(x, y + 1, "O3 Sanctuary Rug D Edge D")


def emblem(g, cx, cy):
    """The 3x3 crest where two rugs cross."""
    for i in range(9):
        g.floor(cx - 1 + i % 3, cy - 1 + i // 3, f"O3 Sanctuary Rug D Emblem {i + 1}")


def build():
    g = Grid()
    ax, ay = ARENA_C

    # the Sanctum: patterned floor inside a ring of plain tiles, a rug cross and the crest
    for y in range(H):
        for x in range(W):
            d = math.hypot(x - ax, y - ay)
            if d <= ARENA_R:
                g.floor(x, y, sdf(x, y) if d <= ARENA_R - 1.5 else NORMAL)
    reach = int(ARENA_R) - 1
    rug_vertical(g, ax, ay - reach, ay + reach)
    rug_horizontal(g, ay, ax - reach, ax + reach)
    emblem(g, ax, ay)

    # corridor from the Sanctum to the Court (the Gate is in it)
    rect(g, CX - 4, ay + reach, CX + 4, COURT[1] - 1, NORMAL)
    rug_vertical(g, CX, ay + reach, COURT[1] - 1)

    # the Court
    x0, y0, x1, y1 = COURT
    rect(g, x0, y0, x1, y1, sdf)
    rect(g, x0, y0, x1, y0, NORMAL)
    rect(g, x0, y1, x1, y1, NORMAL)
    rect(g, x0, y0, x0, y1, NORMAL)
    rect(g, x1, y0, x1, y1, NORMAL)
    cy = (y0 + y1) // 2
    rug_vertical(g, CX, y0, y1)
    rug_horizontal(g, cy, x0 + 1, x1 - 1)
    emblem(g, CX, cy)

    # the Gallery, from the vestibule up to the Court
    rect(g, CX - 3, y1 + 1, CX + 3, VESTIBULE[1] - 1, NORMAL)
    rug_vertical(g, CX, y1 + 1, VESTIBULE[1] - 1)

    # the vestibule
    vx0, vy0, vx1, vy1 = VESTIBULE
    rect(g, vx0, vy0, vx1, vy1, NORMAL)
    rug_vertical(g, CX, vy0, vy1 - 2)

    # walls: two tiles thick around every floor tile, arches on the face
    walls = {}
    for y in range(H):
        for x in range(W):
            if g.is_floor(x, y):
                continue
            near = [(dx, dy) for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2)
                    if g.is_floor(x + dx, y + dy)]
            if not near:
                continue
            face = any(max(abs(dx), abs(dy)) == 1 for dx, dy in near)
            walls[(x, y)] = ARCH[(x + y) % 2] if face else WALL
    for (x, y), w in walls.items():
        g.obj[y][x] = w

    # --- the Sanctum's furnishings -------------------------------------------------------------
    # dead centre: his spawn point is the middle he dashes back to between phases
    g.place(ax, ay, "Oryx the Mad God 3")
    # four pillars to hide behind, on the diagonals
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        g.place(ax + dx * 9, ay + dy * 9, COLUMN)
    # banners on the north wall and torches all the way round
    for i, banner in enumerate(("O3 G Banner", "O3 D Banner", "O3 O Banner", "O3 L Banner", "O3 B Banner")):
        bx = ax - 8 + i * 4
        by = min(y for y in range(H) if g.is_floor(bx, y)) + 1
        g.place(bx, by, banner)
    for k in range(24):
        a = 2 * math.pi * k / 24 + math.pi / 24
        tx, ty = round(ax + math.cos(a) * (ARENA_R - 1)), round(ay + math.sin(a) * (ARENA_R - 1))
        if g.is_floor(tx, ty) and g.obj[ty][tx] is None and g.ground[ty][tx] == NORMAL:
            g.place(tx, ty, TORCH)

    # --- the Gate ------------------------------------------------------------------------------
    for y in GATE_Y:
        for x in range(CX - 4, CX + 5):
            g.place(x, y, "O3 Wall Gate")
    g.place(CX, GATE_Y[1] + 3, "O3 Gate Deleter 1")
    for y in range(ay + reach + 2, COURT[1], 4):
        g.place(CX - 4, y, TORCH)
        g.place(CX + 4, y, TORCH)

    # --- the Court ----------------------------------------------------------------------------
    officers = ["Oryx Officer", "Oryx Sergeant", "Oryx Major"]
    clergy = ["Oryx Deacon", "Oryx Cleric", "Oryx Cardinal"]
    for i, (west, east) in enumerate(zip(officers, clergy)):
        y = y0 + 6 + i * 9
        g.place(x0 + 8, y, west)
        g.place(x1 - 8, y, east)
    # (no Patricians or Oryx Portals: their shots put players in stasis, and a player in stasis
    # still takes full damage on this server)
    g.place(x0 + 13, cy + 5, "Oryx Noble")
    g.place(x1 - 13, cy - 5, "Oryx Aristocrat")
    # columns in two rows down the hall, braziers between them
    for y in range(y0 + 3, y1 - 1, 6):
        for x in (x0 + 4, CX - 7, CX + 7, x1 - 4):
            if g.obj[y][x] is None:
                g.place(x, y, COLUMN)
        for x in (x0 + 1, x1 - 1):
            g.place(x, y + 3, TORCH)
    for x, y in ((x0 + 2, y0 + 2), (x1 - 2, y0 + 2), (x0 + 2, y1 - 2), (x1 - 2, y1 - 2)):
        g.place(x, y, "O3 Treasure Pile")
    g.place(x0 + 3, y0 + 2, "O3 Wine Glass")
    g.place(x1 - 3, y0 + 2, "O3 Wine Glass")

    # --- the Gallery --------------------------------------------------------------------------
    for i, noble in enumerate(["Oryx Noble", "Oryx Aristocrat", "Oryx Noble", "Oryx Aristocrat"]):
        g.place(CX - 2 if i % 2 == 0 else CX + 2, y1 + 4 + i * 4, noble)
    for y in range(y1 + 2, VESTIBULE[1] - 1, 4):
        g.place(CX - 3, y, TORCH)
        g.place(CX + 3, y, TORCH)

    # --- the vestibule ------------------------------------------------------------------------
    for y in range(vy0 + 2, vy1 - 1):
        for x in range(vx0 + 3, vx1 - 2):
            if abs(x - CX) >= 2:
                g.region[y][x] = "Spawn"
    g.place(CX, vy1 - 1, "Nexus Portal")
    for x in (vx0 + 1, vx1 - 1):
        for y in (vy0 + 1, vy1 - 1):
            g.place(x, y, TORCH)
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


if __name__ == "__main__":
    save(build())
