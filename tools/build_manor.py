"""Builds the Manor of the Immortals.

    python tools/build_manor.py [--preview]

Writes source/Shared/resources/worlds/Dungeons/Manor of the Immortals.jm from scratch. The map used
to be Lord Ruthven's hall alone, with players arriving a few tiles from him. Now, from south to
north:

- the Foyer, where players arrive (Spawn region);
- the Gallery, a long hall of portraits and pillars with Lesser Bald Vampires and Lil Feratus,
  and a door to each side:
  - the Library (west), shelves and Armor Guards;
  - the Kennels (east), Hellhounds;
- the Crypt, coffins of big tiles with Coffin Creatures, Nosferatu and bats, and a door to each
  side:
  - the Chapel (west), a rug cross before a row of pillars, Nosferatu and Lesser Bald Vampires;
  - the Dining Hall (east), candles down a long rug, Hellhounds and Lil Feratus;
- Lord Ruthven's hall, as it was (its windows, pillars and rug), entered from the south.

The world has a <RandomLayout> (Dungeons.xml), so each visit joins these rooms in a new order with
the hall at the end of a branch; this map is also the layout used when that fails. The Elder Manor
is built from this map by tools/port_doomed_worlds.py (bs_elder_map). Use only grounds and walls
that already have an Elder version (tools/make_elder.py makes them), or run that first.

The output is generated: change this script and run it again rather than editing the map.
"""
import base64
import json
import os
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Manor of the Immortals.jm")

W, H = 64, 87
CX = 32                      # the map's spine: the hall, Crypt, Gallery and Foyer are centred on it

SLABS = "Gothic Floor Slabs 3"
RUG = "Gothic Rug"
BIG = "Gothic Big Tile"
ARCH = "Gothic Arch"
PILLAR = "Gothic Pillar"
SHELF = "Gothic Bookshelf"
PORTRAIT = "Gothic Portrait"
CANDLE = "Gothic Candelabra"

# Lord Ruthven's hall, the old map: . slabs, = rug, a arch, w window, p pillar, b bookshelf,
# r portrait, c candelabra on slabs, P a pillar on big tile, D/d/L/l/R/E the window lights (on
# slabs), B Lord Ruthven (on the rug), space nothing. Its spawn tiles are slabs now, and its south
# wall has a door (build).
HALL = [
    "       aaawaapbbbbpaawaaa       ",
    "      ap..D..c....c..D..pa      ",
    "      a...d..........d...a      ",
    "      r..................r      ",
    "      r...pp........pp...r      ",
    "      a...pp........pp...a      ",
    " aarrap..................parraa ",
    "ap............................pa",
    "a..............................a",
    "a..............................a",
    "wLl.pp....============....pp.REw",
    "a...pp....============....pp...a",
    "a.........============.........a",
    "pc........============........cp",
    "b.........============.........b",
    "b.........======B=====.........b",
    "b.........============.........b",
    "b.........============.........b",
    "pc........============........cp",
    "a.........============.........a",
    "a...pp....============....pp...a",
    "wLl.pp....============....pp.REw",
    "a..............................a",
    "a..............................a",
    "ap............................pa",
    " aarrap..................parraa ",
    "      a...pp........pp...a      ",
    "      r...pp........pp...r      ",
    "      r..................r      ",
    "      a..................a      ",
    "      ap.....c.....c....pa      ",
    "       aaaraaPbbbbbparaaa       ",
]
HALL_X = CX - 16             # the hall's west edge (Lord Ruthven stands on the spine)

# floors (x0, y0, x1, y1, inclusive)
CRYPT = (CX - 8, 38, CX + 8, 50)
GALLERY = (CX - 11, 57, CX + 11, 69)
LIBRARY = (1, 55, 14, 71)
KENNELS = (W - 15, 55, W - 2, 71)
FOYER = (CX - 6, 76, CX + 6, 84)
CHAPEL = (3, 37, 18, 50)
DINING = (W - 19, 37, W - 4, 50)


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def floor(self, x, y, ground):
        self.ground[y][x] = ground
        self.obj[y][x] = None

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] is not None

    def place(self, x, y, obj):
        assert self.is_floor(x, y), (obj, x, y)
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj


def rect(g, box, ground):
    x0, y0, x1, y1 = box
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            g.floor(x, y, ground(x, y) if callable(ground) else ground)


def hall(g):
    walls = {"a": ARCH, "w": "Gothic Window 1", "r": PORTRAIT, "b": SHELF, "p": PILLAR}
    on_floor = {"c": CANDLE, "D": "Gothic Window Light Da", "d": "Gothic Window Light Db",
                "L": "Gothic Window Light La", "l": "Gothic Window Light Lb",
                "R": "Gothic Window Light Rb", "E": "Gothic Window Light Ra", "B": "Lord Ruthven"}
    for y, row in enumerate(HALL):
        for i, ch in enumerate(row):
            x = HALL_X + i
            if ch in walls:
                g.obj[y][x] = walls[ch]
            elif ch == "P":
                g.floor(x, y, BIG)
                g.obj[y][x] = PILLAR
            elif ch != " ":
                g.floor(x, y, RUG if ch in "=B" else SLABS)
                if ch in on_floor:
                    g.obj[y][x] = on_floor[ch]


def walls_around(g):
    """Every empty tile next to a floor becomes an arch, unless the hall already set a wall."""
    for y in range(H):
        for x in range(W):
            if g.is_floor(x, y) or g.obj[y][x]:
                continue
            if any(g.is_floor(x + i, y + k) for i in (-1, 0, 1) for k in (-1, 0, 1)):
                g.obj[y][x] = ARCH


def dress_wall(g, box, obj, every=2, sides="nsew"):
    """Puts obj on every so many arches along a room's walls (portraits, shelves)."""
    x0, y0, x1, y1 = box
    spots = []
    if "n" in sides:
        spots += [(x, y0 - 1) for x in range(x0, x1 + 1)]
    if "s" in sides:
        spots += [(x, y1 + 1) for x in range(x0, x1 + 1)]
    if "w" in sides:
        spots += [(x0 - 1, y) for y in range(y0, y1 + 1)]
    if "e" in sides:
        spots += [(x1 + 1, y) for y in range(y0, y1 + 1)]
    for n, (x, y) in enumerate(spots):
        # not beside a door, so a doorway stays plain arch
        if n % every == 0 and g.obj[y][x] == ARCH and not any(
                g.is_floor(x + i, y + k) and not (x0 <= x + i <= x1 and y0 <= y + k <= y1)
                for i in (-1, 0, 1) for k in (-1, 0, 1)):
            g.obj[y][x] = obj


def build():
    g = Grid()
    hall(g)
    # the hall's door: its south wall opens onto the corridor down to the Crypt
    for x in range(CX - 1, CX + 2):
        g.floor(x, 30, SLABS)
        g.floor(x, 31, SLABS)
    # corridors, three wide
    rect(g, (CX - 1, 32, CX + 1, 37), SLABS)
    rect(g, (CX - 1, 51, CX + 1, 56), SLABS)
    rect(g, (CX - 1, 70, CX + 1, 75), SLABS)
    rect(g, (LIBRARY[2] + 1, 62, GALLERY[0] - 1, 64), SLABS)
    rect(g, (GALLERY[2] + 1, 62, KENNELS[0] - 1, 64), SLABS)
    rect(g, (CHAPEL[2] + 1, 43, CRYPT[0] - 1, 45), SLABS)
    rect(g, (CRYPT[2] + 1, 43, DINING[0] - 1, 45), SLABS)

    # --- the Crypt: big tiles, coffins of pillars in two rows ----------------------------------
    rect(g, CRYPT, BIG)
    x0, y0, x1, y1 = CRYPT
    for cx in (x0 + 3, x1 - 3):
        for cy in (y0 + 3, y1 - 3):
            g.place(cx, cy, PILLAR)
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        g.place(x, y, CANDLE)
    g.place(x0 + 3, y0 + 6, "Coffin Creature")
    g.place(x1 - 3, y0 + 6, "Coffin Creature")
    g.place(CX, y0 + 3, "Nosferatu")
    g.place(CX, y1 - 2, "Nosferatu")
    for x, y in ((x0 + 6, y0 + 2), (x1 - 6, y0 + 2), (x0 + 2, y1 - 1), (x1 - 2, y1 - 1),
                 (x0 + 6, y1 - 4), (x1 - 6, y1 - 4)):
        g.place(x, y, "Vampire Bat Swarmer")

    # --- the Gallery: a rug down the middle, pillars either side, portraits on the walls --------
    rect(g, GALLERY, lambda x, y: RUG if abs(x - CX) <= 1 else SLABS)
    x0, y0, x1, y1 = GALLERY
    for px in (CX - 9, CX - 4, CX + 4, CX + 9):
        g.place(px, y0 + 2, PILLAR)
        g.place(px, y1 - 2, PILLAR)
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        g.place(x, y, CANDLE)
    for x, y in ((x0 + 4, y0 + 5), (x1 - 4, y0 + 5), (x0 + 4, y1 - 5), (x1 - 4, y1 - 5)):
        g.place(x, y, "Lesser Bald Vampire")
    for x, y in ((CX, y0 + 3), (x0 + 8, y0 + 7), (x1 - 8, y0 + 7)):
        g.place(x, y, "Lil Feratu")

    # --- the Library: shelves in rows, Armor Guards among them ---------------------------------
    rect(g, LIBRARY, SLABS)
    x0, y0, x1, y1 = LIBRARY
    for sy in (y0 + 3, y1 - 3):
        for sx in range(x0 + 3, x1 - 2):
            if sx not in (x0 + 7, x0 + 8):
                g.place(sx, sy, SHELF)
    g.place(x0 + 4, y0 + 1, CANDLE)
    g.place(x1 - 4, y1 - 1, CANDLE)
    for x, y in ((x0 + 4, y0 + 6), (x1 - 4, y0 + 8), (x0 + 6, y1 - 6)):
        g.place(x, y, "Armor Guard")
    for x, y in ((x0 + 2, y0 + 1), (x1 - 2, y1 - 1)):
        g.place(x, y, "Lesser Bald Vampire")

    # --- the Kennels: Hellhounds -----------------------------------------------------------------
    x0, y0, x1, y1 = KENNELS
    rect(g, KENNELS, lambda x, y: SLABS if x in (x0, x1) or y in (y0, y1) else BIG)
    for x, y in ((x0 + 1, y0 + 1), (x1 - 1, y0 + 1), (x0 + 1, y1 - 1), (x1 - 1, y1 - 1)):
        g.place(x, y, CANDLE)
    for x, y in ((x0 + 4, y0 + 3), (x1 - 3, y0 + 5), (x0 + 6, y0 + 9), (x1 - 4, y1 - 4),
                 (x0 + 3, y1 - 3)):
        g.place(x, y, "Hellhound")

    # --- the Chapel: a rug cross, a row of pillars at the altar end ----------------------------
    x0, y0, x1, y1 = CHAPEL
    mx = (x0 + x1) // 2
    rect(g, CHAPEL, lambda x, y: RUG if mx - 1 <= x <= mx + 1 or 42 <= y <= 44 else SLABS)
    for px in range(x0 + 2, x1 - 1, 3):
        g.place(px, y0 + 2, PILLAR)
    for x, y in ((x0, y0 + 4), (x1, y0 + 4), (x0, y1), (x1, y1)):
        g.place(x, y, CANDLE)
    g.place(mx, y0 + 4, "Nosferatu")
    for x, y in ((x0 + 3, y1 - 3), (x1 - 3, y1 - 3), (mx, y1 - 1)):
        g.place(x, y, "Lesser Bald Vampire")

    # --- the Dining Hall: a long rug with candles on it --------------------------------------
    x0, y0, x1, y1 = DINING
    my = (y0 + y1) // 2
    rect(g, DINING, lambda x, y: RUG if x0 + 2 <= x <= x1 - 2 and my - 1 <= y <= my + 1 else SLABS)
    for cx in range(x0 + 3, x1 - 1, 4):
        g.place(cx, my, CANDLE)
    for x, y in ((x0 + 3, y0 + 2), (x1 - 3, y0 + 3), (x0 + 8, y1 - 2)):
        g.place(x, y, "Hellhound")
    for x, y in ((x0 + 6, y0 + 4), (x1 - 5, y1 - 3)):
        g.place(x, y, "Lil Feratu")

    # --- the Foyer: where players arrive -------------------------------------------------------
    rect(g, FOYER, lambda x, y: RUG if abs(x - CX) <= 1 else SLABS)
    x0, y0, x1, y1 = FOYER
    for y in range(y0 + 2, y1 - 1):
        for x in range(x0 + 2, x1 - 1):
            g.region[y][x] = "Spawn"
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        g.place(x, y, CANDLE)

    walls_around(g)
    dress_wall(g, GALLERY, PORTRAIT, every=3, sides="ns")
    dress_wall(g, LIBRARY, SHELF, every=1, sides="nw")
    dress_wall(g, FOYER, PORTRAIT, every=4, sides="ewns")
    dress_wall(g, CRYPT, PORTRAIT, every=5, sides="ew")
    dress_wall(g, CHAPEL, "Gothic Window 1", every=4, sides="n")
    dress_wall(g, DINING, PORTRAIT, every=3, sides="ns")
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
    """Print the map as text: # arch, | other wall, o pillar, c candelabra, S spawn, = rug,
    , big tile, B Lord Ruthven, and the monsters' first letters in lower case."""
    marks = {ARCH: "#", PILLAR: "o", CANDLE: "c", "Lord Ruthven": "B", "Coffin Creature": "k",
             "Nosferatu": "n", "Vampire Bat Swarmer": "v", "Lesser Bald Vampire": "l",
             "Lil Feratu": "f", "Armor Guard": "g", "Hellhound": "h"}
    for y in range(H):
        row = ""
        for x in range(W):
            o = g.obj[y][x]
            if o:
                row += marks.get(o, "|" if not g.is_floor(x, y) else "'")
            elif g.region[y][x]:
                row += "S"
            else:
                row += {RUG: "=", BIG: ",", SLABS: "."}.get(g.ground[y][x], " ")
        print(row.rstrip())


if __name__ == "__main__":
    grid = build()
    if "--preview" in sys.argv:
        preview(grid)
    else:
        save(grid)
