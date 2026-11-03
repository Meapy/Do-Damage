"""Builds the Nexus as floating islands in a sunset sky (world `testnexus` for now; it is to
replace the garden, tools/build_nexus.py), and the islands' own art.

    python tools/build_nexus_islands.py [--preview <folder>] [--dry-run]

Writes `Exalt/Nexus Islands.jm` (the lowest floor) and `Nexus Islands 1.jm` to `3.jm` (the floors
above it, `FLOOR` tiles apart: the world's `<Levels height="3"/>`), all `SIZE`, and the islands'
art: NexusIslandsGrounds.xml and NexusIslandsObjects.xml (server and client copies), the sheets
NexusIslands8x8.png, NexusIslands16x16.png, NexusIslandsChars16x16.png and
NexusIslandsModels128x128.png, and the bar's model (custom/objects/nexusIslandsBar.obj), all
registered for the Rust client by tools/gen_rs_manifest.py. Type codes are kept stable in
tools/nexus_garden/islands_types.json (grounds from 0x9D00, objects from 0x9D80).

The garden's theme and art (tools/build_nexus_garden.py), rebuilt as five islands at three
heights, joined by bridges; floor 1 is the lowest map (the game's `World.Levels[0]`):

- SPAWN (middle, floor 2): the spawn deck in its koi-pond ring (`Spawn`), the healing basins, the
  Armoire, the Vault Portal, the Support Bot and its dungeon portal (`Nexus Bot`, `Nexus Dungeon`),
  the XP ducks on the pond (NexusGuide), the Name Changer and the Character Changer about the deck
  (`Nexus Name Changer`, `Nexus Character Changer`), and in the pond's south-east corner the
  storehouse yard with the four shared vault chests (`CHESTS`: each keeps its castle tile as its
  name, so its items stay where they are in Redis).
- MARKET (west, floor 2), across a flat wooden bridge: the market square (the Broker on
  `Nexus Broker`, the stalls' `Market Stall` tiles, the Tinkerer and the Chronicler on their
  markers), the shop street (`Store N`, MerchantLists.Shops), the guild's teahouse (Guild Hall
  door, Guild Register) and the quest house beside the square.
- NORTH (floor 3, a floor above the spawn), up a wooden bridge that climbs: the forge court (the
  Divine Forge in its smithy, Amatsumara at his anvil, the Enchanter, a weapon rack and straw
  dummies) and the dry garden of raked gravel round the elder cherry.
- PORTALS (south-west, floor 1, a floor below the spawn), down a stone stair-bridge: the shrine
  of the realm portals (Realm 2's vortex on `Realm2 Portal`, four caverns on `Realm Portals`, the
  Arena's sand pit on `Arena Portal`), the pagoda and the torii, and the forest south of it on
  petal ground (blossom falls there), with the memorial clearing and the twins (`Nexus Twins`).
- ARENA (east, floor 2), across a flat wooden bridge: the Arena court (the dojo yard, the samurai
  statue, the Herald on `Nexus Herald`), and the tower: a spiral stair round a lacquered column,
  two turns (`SPIRAL`, a quarter of a tile a twelfth of a turn, as the Stair Tower's), up to the
  top deck two floors up, whose sides go down to the court as the tower's walls (`<FaceDown/>`):
  the Hall of Champions (the monument and a plinth for each board), the Arena Leaderboard, the
  lookout and the bar (`Nexus Barman`, `Nexus Bar`: NexusBar).

Every bridge is made by `bridge` (straight, its width square to the islands' straight bridge
heads, `jetty`; level landings at both ends, a quarter of a tile a square between) and every
island's edge is a rim of rock (or dressed stone where paved) with a railing, whose side drops
into the sky in soil and strata. The Rust client hangs the islands' rocky undersides, the rim's
clouds and the bridges' piers under them (islands.rs) and draws the sky (sunset.rs).

The layout is drawn here, so change it below and run it again. `--preview` draws each floor and
all of them stacked as the game shows them (each floor `FLOOR` tiles up the picture).
"""
import json
import math
import os
import random
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jm_maps as cm  # noqa: E402
import build_nexus as bn  # noqa: E402
import build_nexus_garden as garden  # noqa: E402
from nexus_garden import islands as island_art  # noqa: E402
from nexus_garden import models as model_art  # noqa: E402

ROOT = cm.ROOT
WORLDS = cm.WORLDS
MAP_NAME = "Nexus Islands"
SIZE = (204, 178)
FLOOR = 3                # tiles between floors (Dungeons.xml testnexus <Levels height="3"/>)
LEVELS = 4               # floors: the portals (1), spawn, market and the Arena's foot (2), north (3), the tower's top (4)
RISE = 0.25              # a bridge rises this much a square (never more than half a tile)

# the garden's palette (build_nexus)
MEADOW, GRASS, MOSS, PETALS, EARTH, PATH, MOSSY, SLAB, GRAVEL, COBBLE, TATAMI, DECK, BEAM = (
    bn.MEADOW, bn.GRASS, bn.MOSS, bn.PETALS, bn.EARTH, bn.PATH, bn.MOSSY, bn.SLAB, bn.GRAVEL, bn.COBBLE, bn.TATAMI,
    bn.DECK, bn.BEAM)
POND, SHALLOWS, SAND = bn.POND, bn.SHALLOWS, bn.SAND
SAKURA, WEEPING, PINE, MAPLE, YOUNG, BAMBOO, AZALEA, BOULDER = (bn.SAKURA, bn.WEEPING, bn.PINE, bn.MAPLE, bn.YOUNG,
                                                                bn.BAMBOO, bn.AZALEA, bn.BOULDER)
LANTERN, PAPER, PAPER_L, BASIN, BLOCKER = bn.LANTERN, bn.PAPER, bn.PAPER_L, bn.BASIN, bn.BLOCKER
GREEN = bn.GREEN
NATURAL = (MEADOW, GRASS, MOSS, PETALS, SAND, EARTH, GRAVEL, "NG Petal Gravel")
PAVED = (PATH, MOSSY, SLAB, COBBLE, TATAMI, DECK, BEAM, "NG Dojo Sand")
RIM, PAVED_RIM = "NI Island Rim", "NI Paved Rim"

# --- the layout ----------------------------------------------------------------------------------

# each island: its floor (0 the lowest), middle, half width and height, squareness (2 an
# ellipse, more a rounded rectangle), seed for its wobble
SPAWN = (1, 112, 84, 23, 23, 2.0, 1)
MARKET = (1, 46, 74, 30, 26, 4.0, 2)
NORTH = (2, 112, 31, 24, 19, 2.4, 3)
PORTALS = (0, 104, 146, 34, 26, 2.2, 4)
ARENA = (1, 172, 84, 26, 24, 3.0, 5)

# the bridges: (name, axis, across (first, last), along (first, last), height at each end,
# material); the along range runs from the first island to the second, both ends a landing
BRIDGES = [
    ("market", "x", (82, 85), (91, 79), (3, 3), "wood"),         # spawn west to the market
    ("arena", "x", (82, 85), (133, 145), (3, 3), "wood"),        # spawn east to the Arena
    ("north", "y", (110, 113), (63, 51), (3, 6), "wood"),        # spawn north, up to the north island
    ("portals", "y", (110, 113), (105, 117), (3, 0), "stone"),   # spawn south, down to the portals
]
# each bridge head: the straight edge an island meets its bridge with, a paved jetty out to it
# (bridge name, which end: 0 its first, 1 its last)

# the spawn (build_nexus's island, a little smaller): its deck and the pond round it
SPAWN_DECK = (8, 9)                  # half width and height of the deck's hexagon
POND_SCALE = (1.55, 1.45)
# The shared vault chests (build_nexus.CHESTS's castle tiles, their Redis records), in the
# storehouse yard at the pond's south-east corner: (offset from the spawn's middle, castle tile)
STOREHOUSE = (10, 7)
CHESTS = [((7, 10), (61, 70)), ((9, 10), (79, 70)), ((11, 10), (61, 74)), ((13, 10), (79, 74))]

# the market: build_nexus's village moved here (its coordinates less MARKET_SHIFT)
MARKET_SHIFT = (56, -16)
STREET = (20, 67, 60)                # x0, x1, row
CROSS = (44, 60, 91)                 # the cross street: column, y0, y1
SHOPS = [(r, x - MARKET_SHIFT[0], n, north) for r, x, n, north in bn.SHOPS]
SQUARE = (45, 66, 65, 89)            # the market square
SQUARE_ROWS = [(72, False), (83, True)]
STALLS_X = list(range(48, 63, 2))
BROKER = (54, 77)
QUEST_NPCS = [(50, 77), (58, 77)]
GUILD_HALL = (32, 71)                # the teahouse: GUILD HALL over its door
GUILD = {"NG Guild Register": (27, 74), "NG Guild Hall Door": (32, 74)}
QUEST_HOUSE = (32, 83)

# the north island
FORGE_COURT = (113, 17, 131, 39)
SMITHY = (122, 35)
ELDER = (99, 28)

# the portals island: the shrine round Realm 2's vortex (build_nexus's, its gate to the north)
VORTEX = (111, 138)
CAVERNS = [(101, 132), (121, 132), (101, 144), (121, 144)]
ARENA_PIT = (99, 138)                # the Arena's sand pit (4 across), west of the vortex
SHRINE = (17, 10)                    # the court's half width and height round the vortex
PAGODA = (111, 152)
TORII = (111, 126)
MEMORIAL = (104, 164)

# the Arena island: the court, and the tower east of it
COURT = (153, 88, 169, 100)
SAMURAI = (158, 94)
HERALD = (166, 92)
# the tower: its top deck two floors up (whose sides go down to the court as its walls), the
# spiral stair round its column north-west of it (build_stair_tower.spiral_squares: a ring two
# wide round the 2 by 2 column, sector 0 its south-west corner, where the stair starts from the
# court and, two turns up, steps out onto the deck)
DECK_RECT = (168, 68, 191, 81)
SPIRAL = (168, 62)
SPIRAL_SECTORS = 12
SPIRAL_START = 120
CHAMPIONS = (177, 74)
LEADERBOARD = (186, 70)
BAR = (187, 76)                      # the bar's model: its counter's middle
BARMAN = (187, 75)
TELESCOPE = (169, 80)


# --- the grid ------------------------------------------------------------------------------------

class Level(bn.Grid):
    """One floor: no ground but the islands'."""

    def __init__(self, n):
        super().__init__(*SIZE)
        self.level = n
        self.name = MAP_NAME + (" %d" % n if n else "")
        for row in self.t:
            for t in row:
                t["ground"] = None

    def land(self, x, y):
        return self.ok(x, y) and self[x, y]["ground"] not in (None, POND, SHALLOWS)

    def has(self, x, y):
        return self.ok(x, y) and self[x, y]["ground"] is not None


def shape(x, y, island):
    """Whether (x, y) is on an island: inside its wobbly superellipse."""
    _, cx, cy, rx, ry, p, seed = island
    dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
    a = math.atan2(dy, dx)
    wob = 1 + 0.05 * math.sin(3 * a + seed) + 0.035 * math.sin(5 * a + 2 * seed) + 0.025 * math.sin(9 * a + 3 * seed)
    return abs(dx) ** p + abs(dy) ** p <= wob ** p


def lay_island(g, island, ground=MEADOW):
    for y in range(g.h):
        for x in range(g.w):
            if shape(x, y, island):
                g.ground(x, y, ground)


def bridge_tiles(b):
    """A bridge's squares from its first end to its last: (x, y, height)."""
    _, axis, (c0, c1), (a0, a1), (h0, h1), _ = b
    step = 1 if a1 > a0 else -1
    along = list(range(a0, a1 + step, step))
    n = len(along) - 1
    if h0 != h1:
        assert abs(abs(h1 - h0) / n - RISE) < 1e-9, (b[0], "rises %.3f a square" % (abs(h1 - h0) / n))
    out = []
    for i, a in enumerate(along):
        h = h0 + (h1 - h0) * i / n
        for c in range(c0, c1 + 1):
            out.append((a, c, h) if axis == "x" else (c, a, h))
    return out


def bridge(levels, b):
    """A straight bridge: its squares on the floor at each one's height, the deck's ground lifting
    what crosses it a quarter of a tile a square between its two level landings."""
    name, axis, _, _, _, material = b
    for x, y, h in bridge_tiles(b):
        level = min(int(h / FLOOR + 1e-6), LEVELS - 1)
        lift = round(h - level * FLOOR, 4)
        step = int(round(lift / RISE))
        if material == "wood":
            ground = ("NI Bridge Deck EW" if axis == "x" else "NI Bridge Deck NS") if step == 0 else "NI Wood Ramp %d" % step
        else:
            ground = "NI Stone Bridge" if step == 0 else "NI Stone Ramp %d" % step
        g = levels[level]
        assert not g.has(x, y), ("bridge %s over ground" % name, x, y)
        g.ground(x, y, ground, keep=True)


def carve(levels, b, wide=7):
    """Clears the way for a bridge on every floor: no island reaches into the gap it spans, for
    `wide` squares either side of it, so each island meets it with a straight edge."""
    _, axis, (c0, c1), (a0, a1), _, _ = b
    for a in range(min(a0, a1), max(a0, a1) + 1):
        for c in range(c0 - wide, c1 + wide + 1):
            x, y = (a, c) if axis == "x" else (c, a)
            for g in levels:
                if g.has(x, y):
                    assert not g[x, y]["obj"] and not g[x, y]["region"], ("something in a bridge's way", b[0], x, y)
                    g.t[y][x]["ground"] = None


def jetty(levels, b, end, ground=COBBLE):
    """The bridge head at one end of a bridge: a paved jetty, two squares wider each side than the
    bridge, out from its island to a straight edge square to the bridge, flush with its end."""
    _, axis, (c0, c1), (a0, a1), (h0, h1), _ = b
    a, h = (a0, h0) if end == 0 else (a1, h1)
    away = (1 if a1 > a0 else -1) * (-1 if end == 0 else 1)        # from the bridge into the island
    g = levels[int(h / FLOOR + 1e-6)]
    across = range(c0 - 2, c1 + 3)
    at = lambda k, c: (a + away * k, c) if axis == "x" else (c, a + away * k)
    for k in range(1, 16):
        solid = all(g.has(*at(k + 1, c)) and g.has(*at(k + 2, c)) for c in across)
        for c in across:
            x, y = at(k, c)
            if not g[x, y]["obj"] and not g[x, y]["region"]:
                g.ground(x, y, ground, keep=True)
        if solid:
            break
    return g


# --- the islands ---------------------------------------------------------------------------------

def hex_inside(x, y, cx, cy, hw, hh):
    return bn.hex_inside(x, y, cx, cy, hw, hh)


def lay_spawn(g):
    lv, sx, sy, *_ = SPAWN
    lay_island(g, SPAWN, GRASS)
    dw, dh = SPAWN_DECK
    pw, ph = dw * POND_SCALE[0], dh * POND_SCALE[1]
    for y in range(g.h):
        for x in range(g.w):
            if not g.has(x, y):
                continue
            wob = 0.6 * math.sin(x * 0.45 + y * 0.2) + 0.4 * math.sin(y * 0.7 - x * 0.3)
            if hex_inside(x + 0.5, y + 0.5, sx + 0.5, sy + 0.5, pw + wob, ph + wob * 0.6):
                g.ground(x, y, POND)
    for y in range(g.h):
        for x in range(g.w):
            if g[x, y]["ground"] == POND and any(g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] == GRASS
                                                 for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                g.ground(x, y, SHALLOWS)
    for y in range(sy - dh - 1, sy + dh + 2):
        for x in range(sx - dw - 1, sx + dw + 2):
            if hex_inside(x + 0.5, y + 0.5, sx + 0.5, sy + 0.5, dw, dh):
                edge = not all(hex_inside(x + 0.5 + ddx, y + 0.5 + ddy, sx + 0.5, sy + 0.5, dw, dh)
                               for ddx in (-1, 0, 1) for ddy in (-1, 0, 1))
                g.ground(x, y, BEAM if edge else DECK, keep=True)
    for y in range(sy - 2, sy + 3):
        for x in range(sx - 2, sx + 3):
            g.put(x, y, region="Spawn")
    for dx, dy in ((-4, -4), (4, -4), (-4, 4), (4, 4)):
        g.place(sx + dx, sy + dy, BASIN)
    g.place(sx, sy + 4, "NG Armoire")
    g.place(sx - 6, sy + 5, "Vault Portal")
    g.put(sx - 2, sy + 4, region="Nexus Bot")
    g.put(sx, sy + 7, region="Nexus Dungeon")
    g.put(sx - 5, sy - 2, region="Nexus Name Changer")
    g.put(sx + 5, sy - 2, region="Nexus Character Changer")
    for x, y in ((sx, sy - dh + 1), (sx - dw + 1, sy - dh // 2), (sx + dw - 1, sy - dh // 2),
                 (sx - dw + 1, sy + dh // 2), (sx + dw - 1, sy + dh // 2)):
        g.place(x, y, LANTERN)
    # four plank bridges over the pond, to the shore at the big bridges' feet, railed
    for d in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        x, y = sx, sy
        while g[x, y]["ground"] in (DECK, BEAM):
            x, y = x + d[0], y + d[1]
        while g[x, y]["ground"] in (POND, SHALLOWS):
            for k in (-1, 0, 1):
                px, py = (x, y + k) if d[1] == 0 else (x + k, y)
                g.ground(px, py, "NG Bridge Planks EW" if d[1] == 0 else "NG Bridge Planks NS", keep=True)
            for k in (-2, 2):
                px, py = (x, y + k) if d[1] == 0 else (x + k, y)
                if g[px, py]["ground"] in (POND, SHALLOWS) and g.empty(px, py):
                    g.model("NG Wood Rail" if d[1] == 0 else "NG Wood Rail NS", px, py, blockers=[])
            x, y = x + d[0], y + d[1]
        # a path on to the island's edge
        ex, ey = x, y
        while g.has(ex + d[0], ey + d[1]):
            ex, ey = ex + d[0], ey + d[1]
        g.line([(x, y), (ex, ey)], 4, PATH)
        # paper lanterns at each pond bridge's shore end
        for k, lamp in ((-2, PAPER), (2, PAPER_L)):
            px, py = (x, y + k) if d[1] == 0 else (x + k, y)
            if g.land(px, py) and g.empty(px, py):
                g.place(px, py, lamp)
    # the storehouse yard in the pond's south-east corner, fenced, its gate to the north
    yx, yy = sx + STOREHOUSE[0], sy + STOREHOUSE[1]
    g.rect(yx - 3, yy - 1, yx + 3, yy + 4, PATH)
    g.model("NG Storehouse", yx, yy)
    for (dx, dy), (ox, oy) in CHESTS:
        g.place(sx + dx, sy + dy, "Vault Chest", name="%d_%d" % (ox, oy))
    for y in range(yy - 1, yy + 5):
        for x in (yx - 4, yx + 4):
            if g.has(x, y) and g.empty(x, y):
                g.model("NG Bamboo Fence NS", x, y)
    for x in range(yx - 3, yx + 4):
        if g.has(x, yy + 5) and g.empty(x, yy + 5):
            g.model("NG Bamboo Fence", x, yy + 5)
    # a path to the yard's gate from the east pond bridge's shore
    g.line([(sx + 13, sy + 2), (yx, yy - 2)], 2, PATH, only=(GRASS, SHALLOWS, POND))
    return [(sx - 15, sy + 3), (sx + 15, sy + 3), (sx + 3, sy - 15), (sx + 3, sy + 15), (yx, yy - 2)]


def lay_market(g):
    lay_island(g, MARKET, MEADOW)
    x0, x1, sy = STREET
    g.line([(x0, sy), (x1, sy)], 3, EARTH)
    g.line([(CROSS[0], CROSS[1]), (CROSS[0], CROSS[2])], 3, EARTH)
    houses = []
    for region, sx, n, north in SHOPS:
        y = sy - 2 if north else sy + 2
        strip = (y - 1, y) if north else (y, y + 1)
        lo, hi = sx - 1, sx + 2 * (n - 1) + 1
        for yy in strip:
            for x in range(lo, hi + 1):
                g.ground(x, yy, TATAMI, keep=True)
        for i in range(n):
            g.put(sx + 2 * i, y, TATAMI, region=region, keep=True)
        for x, lamp in ((lo - 1, PAPER), (hi + 1, PAPER_L)):
            if g.empty(x, y):
                g.place(x, y, lamp)
        hy = y - 4 if north else y + 4
        if not north and sx >= SQUARE[0]:
            continue                             # the market square is behind these
        for hx in range(lo + 2, hi - 1, 6):
            if hi - hx >= 2 and not (not north and abs(hx + 1 - GUILD_HALL[0]) < 7):
                houses.append(("NG House" if north else "NG House N", hx + 1, hy))
    for name, x, y in houses:
        g.model(name, x, y)
    # the market square: a stone court, two rows of stalls facing its courtyard, the Broker's
    # house at its far end (build_nexus.lay_market's)
    qx0, qy0, qx1, qy1 = SQUARE
    g.rect(qx0, qy0, qx1, qy1, SLAB)
    for y in range(qy0 + 8, qy0 + 16):
        for x in range(qx0 + 2, qx1 - 1):
            g.ground(x, y, PATH, keep=True)
    for y, north in SQUARE_ROWS:
        for i, x in enumerate(STALLS_X):
            g.put(x, y, SLAB, region="Market Stall", keep=True)
            stall = bn.MARKET_STALL_KINDS[(i + (3 if north else 0)) % len(bn.MARKET_STALL_KINDS)]
            g.model(stall + (" N" if north else ""), x, y + (1 if north else -1))
        for x, lamp in ((STALLS_X[0] - 2, PAPER), (STALLS_X[-1] + 2, PAPER_L)):
            g.place(x, y, lamp)
    cx, cy = (qx0 + qx1) // 2, BROKER[1]
    for dx in (-5, 5):
        g.place(cx + dx, cy - 2, LANTERN)
        g.place(cx + dx, cy + 2, LANTERN)
    for dx in (-3, 3):
        g.place(cx + dx, cy + 3, "NG Bench")
    g.model("NG House N", cx, qy1 - 2)
    g.put(*BROKER, region="Nexus Broker")
    g.put(*QUEST_NPCS[0], region="Nexus Tinkerer")
    g.put(*QUEST_NPCS[1], region="Nexus Chronicler")
    # the guild's teahouse, GUILD HALL over its door, the Guild Register beside it; the quest house
    g.rect(GUILD_HALL[0] - 6, GUILD_HALL[1] + 3, GUILD_HALL[0] + 6, GUILD_HALL[1] + 5, PATH)
    g.model("NG Teahouse", *GUILD_HALL)
    for name in ("NG Guild Register", "NG Guild Hall Door"):
        g.place(*GUILD[name], name)
    g.model("NG House", *QUEST_HOUSE)
    g.line([(CROSS[0], GUILD_HALL[1] + 4), (GUILD_HALL[0] + 6, GUILD_HALL[1] + 4)], 3, PATH)
    g.line([(CROSS[0], QUEST_HOUSE[1] + 3), (QUEST_HOUSE[0], QUEST_HOUSE[1] + 3)], 3, PATH)
    # from the square to the bridge head
    g.line([(qx1, 83.5), (79, 83.5)], 4, PATH)
    return [(x, y + 3) for name, x, y in houses if name == "NG House"] + \
        [(x, y - 3) for name, x, y in houses if name == "NG House N"] + \
        [(BROKER[0], BROKER[1] + 4), (CROSS[0], 66), (CROSS[0], 88), (30, sy), (60, sy),
         (GUILD_HALL[0], GUILD_HALL[1] + 4), (QUEST_HOUSE[0], QUEST_HOUSE[1] + 3), (72, 83)]


def lay_north(g):
    lay_island(g, NORTH, GRASS)
    fx0, fy0, fx1, fy1 = FORGE_COURT
    g.rect(fx0, fy0, fx1, fy1, SLAB)
    for x, y in ((fx0, fy0), (fx1, fy0), (fx0, fy1), (fx1, fy1)):
        g.place(x, y, LANTERN)
    bn.smithy(g, SMITHY)
    for x in (fx0 + 2, fx0 + 3):
        g.place(x, fy1 - 1, "NG Weapon Rack")
    for y in (fy1 - 7, fy1 - 4):
        g.place(fx1 - 2, y, "NG Straw Dummy")
    g.model("NG House N", fx1 - 4, fy0 + 2)
    # the dry garden round the elder cherry
    tx, ty = ELDER
    for y in range(ty - 8, ty + 8):
        for x in range(tx - 9, tx + 10):
            d = math.hypot((x - tx) / 8.5, (y - ty) / 6.5)
            if d <= 1 and g.has(x, y):
                g.ground(x, y, "NG Petal Gravel" if d < 0.75 else GRAVEL, keep=True)
    g.place(tx, ty, "NG Elder Sakura")
    for x, y in ((tx - 6, ty - 1), (tx + 6, ty + 1), (tx - 4, ty + 3), (tx + 4, ty + 3)):
        g.place(x, y, BOULDER)
    for x, y in ((tx - 8, ty + 1), (tx + 8, ty + 2)):
        g.place(x, y, LANTERN)
    # from the bridge head north, then to the forge court and the garden
    g.line([(111.5, 49), (111.5, 40)], 4, SLAB)
    g.line([(111.5, 38.5), (fx0, 38.5)], 3, SLAB)
    g.line([(110, 36), (tx + 9, ty + 3)], 2, PATH)
    return [(111, 41), (fx0 + 4, fy1 - 2), (SMITHY[0] + 3, SMITHY[1] + 2), (tx + 9, ty + 4), (tx, ty + 8)]


def lay_portals(g):
    lay_island(g, PORTALS, MEADOW)
    vx, vy = VORTEX
    rx, ry = SHRINE
    # the forest south of the shrine, on moss
    for y in range(g.h):
        for x in range(g.w):
            if g.has(x, y) and y > vy + ry - 2:
                g.ground(x, y, MOSS)
    for y in range(vy - ry - 1, vy + ry + 2):
        for x in range(vx - rx - 1, vx + rx + 2):
            if math.hypot((x - vx) / rx, (y - vy) / ry) <= 1.0 and g.has(x, y):
                g.ground(x, y, SLAB, keep=True)
    for (cx, cy), r in [(VORTEX, 4.6)] + [(c, 3.4) for c in CAVERNS] + [(ARENA_PIT, 3.4)]:
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if math.hypot(x - cx, y - cy) <= r:
                    g.ground(x, y, SLAB, keep=True)
    g.put(vx, vy, region="Realm2 Portal")
    for cx, cy in CAVERNS:
        g.put(cx, cy, region="Realm Portals")
    g.put(*ARENA_PIT, region="Arena Portal")
    for k in range(8):
        a = k / 8 * math.tau + math.pi / 8
        g.place(int(round(vx + math.cos(a) * 5.6)), int(round(vy + math.sin(a) * 5.6)), LANTERN)
    # the pagoda south of the vortex on a slab terrace, lanterns either side
    px, py = PAGODA
    g.rect(px - 4, py - 3, px + 4, py + 3, SLAB)
    g.model("NG Pagoda", px, py, ground=SLAB)
    for dx in (-4, 4):
        g.place(px + dx, py + 1, LANTERN, SLAB)
    # in from the bridge head, under the torii
    tx, ty = TORII
    g.line([(tx, 121), (tx, vy - 6)], 3, PATH)
    g.model("NG Torii", tx, ty, blockers=bn.TORII_POSTS)
    # round the pagoda into the forest, to the memorial clearing
    mx, my = MEMORIAL
    g.line([(vx - rx + 3, vy + 4), (mx - 10, my - 4), (mx - 4, my), (mx + 4, my), (vx + rx - 2, vy + 6)], 2, MOSSY,
           only=GREEN)
    for y in range(my - 3, my + 4):
        for x in range(mx - 5, mx + 6):
            if math.hypot((x - mx) / 1.5, y - my) <= 3.6 and g.has(x, y):
                g.ground(x, y, PETALS, keep=True)
    g.place(mx, my - 2, "NG Wayside Shrine")
    for dx in (-3, 3):
        g.place(mx + dx, my + 2, "NG Bench")
    g.place(mx - 6, my, LANTERN)
    g.place(mx + 6, my, LANTERN)
    g.put(mx, my - 4, region="Nexus Twins")
    return [(tx, 123), (vx + 6, vy + 7), (vx - 8, vy + 6), (mx, my + 1), (mx - 10, my - 4)]


def spiral_squares():
    """build_stair_tower.spiral_squares, here: the ring's squares and each one's sector (0 to
    11, sector 0 the south-west corner), and the column's."""
    sx, sy = SPIRAL
    cx, cy = sx + 3, sy + 3
    ring, core = {}, []
    for y in range(sy, sy + 6):
        for x in range(sx, sx + 6):
            if sx + 2 <= x <= sx + 3 and sy + 2 <= y <= sy + 3:
                core.append((x, y))
                continue
            a = math.degrees(math.atan2(y + 0.5 - cy, x + 0.5 - cx))
            ring[(x, y)] = int(((a - SPIRAL_START) % 360) / (360 / SPIRAL_SECTORS))
    return ring, core


def lay_arena(levels):
    g = levels[1]
    lay_island(g, ARENA, MEADOW)
    # the court: raked sand in a border of slabs, the samurai statue, banners, racks, dummies
    ax0, ay0, ax1, ay1 = COURT
    g.rect(ax0, ay0, ax1, ay1, SLAB)
    g.rect(ax0 + 1, ay0 + 1, ax1 - 1, ay1 - 1, "NG Dojo Sand")
    g.place(*SAMURAI, "NG Samurai Statue")
    for i, x in enumerate(range(ax0 + 1, ax1, 3)):
        if g.empty(x, ay0):
            g.place(x, ay0, "NG Nobori Red" if i % 2 == 0 else "NG Nobori Indigo")
    for x, y, lamp in ((ax0, ay1, PAPER), (ax1, ay1, PAPER_L)):
        g.place(x, y, lamp)
    for x in (ax0 + 2, ax0 + 3):
        g.place(x, ay1 - 1, "NG Weapon Rack")
    for x in (ax1 - 6, ax1 - 4, ax1 - 2):
        g.place(x, ay1 - 2, "NG Straw Dummy")
    g.place(ax1 - 2, ay0 + 2, "NG Taiko")
    g.put(*HERALD, region="Nexus Herald")
    # the tower's foot: stone, not walked on (its walls stand on it), and the spiral's
    dx0, dy0, dx1, dy1 = DECK_RECT
    ring, core = spiral_squares()
    for y in range(dy0, dy1 + 1):
        for x in range(dx0, dx1 + 1):
            g.ground(x, y, "NI Tower Base", keep=True)
    for x, y in core:
        g.ground(x, y, "NI Tower Base", keep=True)
    for (x, y), k in ring.items():
        levels[1].ground(x, y, "NI Spiral %d" % k, keep=True)
        levels[2].ground(x, y, "NI Spiral %d" % k, keep=True)
        if k == 0:
            levels[3].ground(x, y, "NI Spiral 0", keep=True)
    for x, y in core:
        levels[3].ground(x, y, "NI Column", keep=True)
    # paths: from the bridge head to the court and to the stair's foot
    g.line([(147, 83.5), (160, 83.5), (160, 66.5), (167, 66.5)], 3, PATH)
    g.line([(161, 85), (161, ay0)], 3, PATH)
    # the top deck
    top = levels[3]
    for y in range(dy0, dy1 + 1):
        for x in range(dx0, dx1 + 1):
            top.ground(x, y, "NI Tower Deck", keep=True)
    # the Hall of Champions: the monument and a plinth for each board (build_nexus), its ring of
    # slabs open to the stair's landing
    hx, hy = CHAMPIONS
    top.place(hx, hy, "Champions Monument")
    for (dx, dy), board in bn.CHAMPION_STATUES:
        top.place(hx + dx, hy + dy, "Champion Statue", name="name:%s" % board)
    top.place(*LEADERBOARD, "Arena Leaderboard")
    # the bar: its pavilion, the barman behind the counter, stools before it, its floor
    bx, by = BAR
    for x in range(bx - 4, bx + 5):
        for y in range(by - 2, by + 5):
            top.ground(x, y, "NI Bar Floor", keep=True)
    blockers = [(dx, 0) for dx in range(-3, 4) if dx] + [(-3, -1), (3, -1)] + [(dx, -2) for dx in range(-3, 4)]
    top.model("NI Bar", bx, by, blockers=blockers)
    top.put(*BARMAN, region="Nexus Barman")
    stools = [(bx - 2, by + 1), (bx, by + 1), (bx + 2, by + 1)]
    for x, y in stools:
        top.place(x, y, "NI Bar Stool")
    for y in range(by + 1, by + 5):
        for x in range(bx - 4, bx + 5):
            if top.empty(x, y) and top.has(x, y):
                top.put(x, y, region="Nexus Bar")
    # the lookout, and lanterns at the deck's corners
    top.place(*TELESCOPE, "NI Telescope")
    for x, y in ((dx0, dy1), (dx1, dy0), (dx0 + 4, dy0)):
        if top.empty(x, y):
            top.place(x, y, LANTERN)
    return [(150, 83), (158, ay0 - 2), (166, 66), (ax0 + 6, ay1 + 2)]


# --- the rims, trees and waypoints ---------------------------------------------------------------

def smooth(levels):
    """No slivers: ground a square wide sticking out of an island (left by a bridge's cut) goes,
    unless something stands on it."""
    for g in levels:
        for _ in range(3):
            gone = []
            for y in range(g.h):
                for x in range(g.w):
                    if not g.has(x, y) or g[x, y]["obj"] or g[x, y]["region"] or g[x, y]["ground"].startswith("NI "):
                        continue
                    n = [g.has(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))]
                    if sum(n) <= 1 or (not n[0] and not n[1]) or (not n[2] and not n[3]):
                        gone.append((x, y))
            for x, y in gone:
                g.t[y][x]["ground"] = None
                g.kept.discard((x, y))


def lay_rims(levels):
    """Each island's outermost squares: a rim of rock (dressed stone where paved), railed."""
    for g in levels:
        edge = []
        for y in range(g.h):
            for x in range(g.w):
                t = g[x, y]["ground"]
                if t is None or not any(not g.has(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    continue
                if t in NATURAL or t in (POND, SHALLOWS):
                    edge.append((x, y, RIM))
                elif t in PAVED:
                    edge.append((x, y, PAVED_RIM))
        for x, y, rim in edge:
            g.ground(x, y, rim, keep=True)
            obj = g[x, y]["obj"]
            if obj and obj != BLOCKER:
                print("  on the rim of floor %d at %d,%d: %s" % (g.level + 1, x, y, obj))


def plant(g, rng, waypoints):
    """Trees, bamboo, bushes and flowers on the open ground, thicker in the forest and along the
    rims; lily pads and koi on the pond; the villagers' waypoints."""
    spots = [(x, y) for y in range(g.h) for x in range(g.w)]
    rng.shuffle(spots)

    def near_kept(x, y, r):
        return any((x + dx, y + dy) in g.kept for dx in range(-r, r + 1) for dy in range(-r, r + 1))

    def near_rim(x, y, r):
        return any(g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] in (RIM, None)
                   for dx in range(-r, r + 1, 2) for dy in range(-r, r + 1, 2))

    for x, y in spots:
        ground = g[x, y]["ground"]
        if ground not in GREEN:
            continue
        forest = ground in (MOSS, PETALS)
        edge = near_rim(x, y, 5)
        r = rng.random()
        chance = 0.3 if forest else 0.14 if edge else 0.04
        if r < chance and g.free(x, y, 1, GREEN) and not near_kept(x, y, 2) and not near_rim(x, y, 1):
            if forest:
                tree = rng.choice([SAKURA, SAKURA, SAKURA, SAKURA, WEEPING, WEEPING, PINE, MAPLE, BAMBOO])
            elif edge:
                tree = rng.choice([PINE, PINE, SAKURA, SAKURA, BAMBOO, MAPLE, WEEPING])
            else:
                tree = rng.choice([SAKURA, SAKURA, SAKURA, WEEPING, PINE, MAPLE])
            g.place(x, y, tree, keep=False)
            if tree in (SAKURA, WEEPING):
                for dy in range(-1, 3):
                    for dx in range(-2, 3):
                        if g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] in (MOSS, GRASS) and rng.random() < (0.55 if forest else 0.25):
                            g.ground(x + dx, y + dy, PETALS)
            continue
        r = rng.random()
        if r < 0.03 and g.free(x, y, 1):
            g.place(x, y, rng.choice([AZALEA, AZALEA, BOULDER, BAMBOO]), keep=False)
        elif r < 0.12 and g.free(x, y, 0, GREEN):
            g.place(x, y, rng.choice(["NG Grass Tuft", "NG Grass Tuft", "NG Pink Flowers", "NG Blue Flowers"]
                                     if not forest else ["NG Fallen Petals", "NG Grass Tuft", "NG Fallen Petals"]), keep=False)
    for x, y in spots:
        if g[x, y]["ground"] == POND and g.empty(x, y) and (x, y) not in g.kept:
            r = rng.random()
            if r < 0.04:
                g.place(x, y, "NG Lily Pad", keep=False)
            elif r < 0.055:
                g.place(x, y, "NG Lotus", keep=False)
            elif r < 0.062 and all(g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] == POND for dx in (-1, 1) for dy in (-1, 1)):
                g.place(x, y, "NG Koi", keep=False)
    for x, y in waypoints:
        x, y = int(x), int(y)
        for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1)):
            if g.ok(x + dx, y + dy) and g.empty(x + dx, y + dy) and g.land(x + dx, y + dy) \
                    and g[x + dx, y + dy]["ground"] not in (RIM, PAVED_RIM):
                g.put(x + dx, y + dy, region="Nexus Waypoint")
                break


def build():
    levels = [Level(n) for n in range(LEVELS)]
    rng = random.Random(20261103)
    points = {0: [], 1: [], 2: [], 3: []}
    points[1] += lay_spawn(levels[1])
    points[1] += lay_market(levels[1])
    points[2] += lay_north(levels[2])
    points[0] += lay_portals(levels[0])
    points[1] += lay_arena(levels)
    # the bridges: the gaps they span cleared, a jetty at each end, the bridge between
    for b in BRIDGES:
        carve(levels, b)
    for b in BRIDGES:
        for end in (0, 1):
            jetty(levels, b, end)
        bridge(levels, b)
    # the rims last: the jetties' sides are rim too
    smooth(levels)
    lay_rims(levels)
    for n, g in enumerate(levels):
        plant(g, rng, points[n])
    return levels


# --- checks --------------------------------------------------------------------------------------

def check(levels):
    regions = {}
    for g in levels:
        for row in g.t:
            for t in row:
                if t["region"]:
                    regions[t["region"]] = regions.get(t["region"], 0) + 1
    assert regions.get("Spawn") == 25, regions
    assert regions.get("Market Stall") == 16, regions
    assert regions.get("Realm Portals") == len(CAVERNS), regions
    assert regions.get("Realm2 Portal") == 1, regions
    assert regions.get("Nexus Waypoint", 0) >= 16, regions
    for r in ("Nexus Bot", "Nexus Dungeon", "Nexus Tinkerer", "Nexus Chronicler", "Nexus Herald", "Nexus Broker",
              "Nexus Name Changer", "Nexus Character Changer", "Nexus Twins", "Arena Portal", "Nexus Barman"):
        assert regions.get(r) == 1, (r, regions.get(r))
    assert regions.get("Nexus Bar", 0) >= 12, regions
    for shop, _, n, _ in bn.SHOPS:
        assert regions.get(shop) == n, (shop, regions.get(shop))
    spawn = levels[1]
    sx, sy = SPAWN[1:3]
    for (dx, dy), _ in CHESTS:
        assert spawn[sx + dx, sy + dy]["obj"] == "Vault Chest", (dx, dy)
    # the Arena's pit and the portals' floors clear
    g0 = levels[0]
    for (cx, cy), r in [(VORTEX, 3), (ARENA_PIT, 2)] + [(c, 2) for c in CAVERNS]:
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                assert not g0[x, y]["obj"] and g0.land(x, y), ("something on a portal's floor", x, y, g0[x, y]["obj"])
    # every bridge square level with its neighbours along it, a quarter of a tile at most, and
    # its ends flush with a jetty at the same height
    for b in BRIDGES:
        tiles = bridge_tiles(b)
        for (x, y, h) in tiles:
            level = min(int(h / FLOOR + 1e-6), LEVELS - 1)
            assert levels[level][x, y]["ground"].startswith("NI "), (b[0], x, y)
        _, axis, (c0, c1), (a0, a1), (h0, h1), _ = b
        for a, h, away in ((a0, h0, -1 if a1 > a0 else 1), (a1, h1, 1 if a1 > a0 else -1)):
            g = levels[int(h / FLOOR + 1e-6)]
            for c in range(c0, c1 + 1):
                x, y = (a + away, c) if axis == "x" else (c, a + away)
                assert g.has(x, y), ("a bridge's end with no island", b[0], x, y)
    # nothing floating: islands don't overlap each other's floors except the tower
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            on = [n for n, g in enumerate(levels) if g.has(x, y)]
            if len(on) > 1:
                dx0, dy0, dx1, dy1 = DECK_RECT
                sx0, sy0 = SPIRAL
                assert (dx0 <= x <= dx1 and dy0 <= y <= dy1) or (sx0 <= x < sx0 + 6 and sy0 <= y < sy0 + 6), ("floors overlap", x, y, on)


# --- the islands' art ------------------------------------------------------------------------------

XML_DIR = garden.XML_DIR
CLIENT = garden.CLIENT
ART_XML = {
    "grounds": [os.path.join(XML_DIR, "NexusIslandsGrounds.xml"), os.path.join(CLIENT, "xmls", "NexusIslandsGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "NexusIslandsObjects.xml"), os.path.join(CLIENT, "xmls", "NexusIslandsObjects.xml")],
}
ART_TYPES = os.path.join(ROOT, "tools", "nexus_garden", "islands_types.json")
SHEET8 = ("nexusIslands8x8", "NexusIslands8x8.png")
SHEET16 = ("nexusIslands16x16", "NexusIslands16x16.png")
CHARS16 = ("nexusIslandsChars16x16", "NexusIslandsChars16x16.png")
MODELS = ("nexusIslandsModels128x128", "NexusIslandsModels128x128.png")


class Types(garden.Types):
    def __init__(self):
        self.data = json.load(open(ART_TYPES)) if os.path.exists(ART_TYPES) else {"grounds": {}, "objects": {}}

    def get(self, kind, name):
        table = self.data[kind]
        if name not in table:
            lo, hi = (0x9D00, 0x9D80) if kind == "grounds" else (0x9D80, 0x9E00)
            used = set(int(v, 16) for v in table.values())
            table[name] = "0x%04X" % next(t for t in range(lo, hi) if t not in used)
        return table[name]

    def save(self):
        with open(ART_TYPES, "w", newline="\n") as f:
            json.dump(self.data, f, indent=1, sort_keys=True)
            f.write("\n")


def model_file(name):
    return "nexusIslands" + name[3:].replace(" ", "") + ".obj"


def build_art():
    """The islands' grounds, scenery, barman and bar (nexus_garden.islands), as game data."""
    sub, texture = garden.sub, garden.texture
    types = Types()
    s8, s16 = garden.Sheet(8), garden.Sheet(16)
    chars = garden.Sheet(112, 4)
    chars.h = 16
    models = garden.Sheet(model_art.TEX, 8)
    ground_xml, object_xml = [], []
    for name, (painter, variants, tags, colour, face) in island_art.grounds().items():
        g = ET.Element("Ground", {"type": types.get("grounds", name), "id": name})
        cells = [s8.add(painter(i * 7 + 3)) for i in range(variants)]
        if len(cells) == 1:
            texture(g, SHEET8[0], cells[0])
        else:
            rt = sub(g, "RandomTexture")
            for c in cells:
                texture(rt, SHEET8[0], c)
        if face:
            f = sub(g, "Face")
            faces = [s8.add(face(i * 5 + 1)) for i in range(2)]
            rt = sub(f, "RandomTexture")
            for c in faces:
                texture(rt, SHEET8[0], c)
        for tag, val in tags.items():
            sub(g, tag, val)
        sub(g, "Color", colour)
        ground_xml.append(g)

    def scenery(name, shown, sheet, file, cell, size=100, block=True, shadow=None):
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "DisplayId", shown)
        sub(o, "Class", "GameObject")
        texture(o, file, cell)
        sub(o, "Size", size)
        if shadow is not None:
            sub(o, "ShadowSize", shadow)
        sub(o, "Static")
        if block:
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        object_xml.append(o)
        return o

    scenery("NI Bar Stool", "Bar Stool", s8, SHEET8[0], s8.add(island_art.bar_stool()), shadow=60)
    scenery("NI Telescope", "Telescope", s16, SHEET16[0], s16.add(island_art.telescope()), shadow=60)
    # the bottle the barman throws: art for the throw only (ShowEffect), never on the map
    o = scenery("NI Thrown Sake", "Sake", s8, SHEET8[0], s8.add(island_art.thrown_sake()), block=False, shadow=0)
    sub(o, "NoMiniMap")
    # the barman: a character, but not an enemy, so pets and shots leave him be
    o = ET.Element("Object", {"type": types.get("objects", "NI Barman"), "id": "NI Barman"})
    sub(o, "DisplayId", "Barman Kenji")
    sub(o, "Class", "Character")
    at = sub(o, "AnimatedTexture")
    sub(at, "File", CHARS16[0])
    sub(at, "Index", chars.add(island_art.barman_strip()))
    sub(o, "Size", 100)
    sub(o, "MaxHitPoints", 1000)
    sub(o, "Defense", 0)
    sub(o, "NoMiniMap")
    sub(o, "Invincible")
    sub(o, "StasisImmune")
    sub(o, "ShadowSize", 80)
    object_xml.append(o)
    for name, (builder, footprint, blocks, shadow) in island_art.MODELS.items():
        mesh, img = builder()
        cell = models.add(img)
        with open(os.path.join(garden.MODELS_DIR, model_file(name)), "w", newline="\n") as f:
            f.write(mesh.obj(name).replace("build_nexus_garden.py", "build_nexus_islands.py"))
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "Class", "GameObject")
        sub(o, "Model", name)
        texture(o, MODELS[0], cell)
        sub(o, "Static")
        sub(o, "ShadowSize", 0)
        sub(o, "ModelContrast", 0.5)
        sub(o, "ModelShadow", shadow)
        if blocks:
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        object_xml.append(o)
    garden.write_xml(ART_XML["grounds"], "GroundTypes", ground_xml,
                     "The Nexus islands' grounds: generated by tools/build_nexus_islands.py, don't edit")
    garden.write_xml(ART_XML["objects"], "Objects", object_xml,
                     "The Nexus islands' scenery, bar and barman: generated by tools/build_nexus_islands.py, don't edit")
    out = {}
    for (_, fname), sheet in ((SHEET8, s8), (SHEET16, s16), (CHARS16, chars), (MODELS, models)):
        out[fname] = sheet.save(os.path.join(garden.IMAGES, fname))
    types.save()
    print("Nexus islands art: %d grounds, %d objects" % (len(ground_xml), len(object_xml)))
    return out


# --- output --------------------------------------------------------------------------------------

def preview(levels, folder):
    """Each floor from above (build_nexus.preview), and all of them stacked as the game draws
    them north up: each floor `FLOOR` tiles further up the picture, over the sky."""
    from PIL import Image
    paths = []
    for g in levels:
        path = os.path.join(folder, g.name + ".png")
        bn.preview(g, path, floor_portals=False)
        paths.append(path)
    T = 16                      # build_nexus.preview: 8 a tile, doubled
    imgs = [Image.open(p).convert("RGBA") for p in paths]
    w, h = imgs[0].size
    lift = FLOOR * T
    out = Image.new("RGBA", (w, h + lift * (LEVELS - 1)), (0, 0, 0, 255))
    sky = [(0x27, 0x40, 0x7F), (0x8E, 0xA3, 0xD1), (0xE2, 0xAD, 0xC6), (0xFF, 0xC9, 0xA1)]
    for y in range(out.height):
        f = y / out.height * (len(sky) - 1)
        a, b = sky[int(f)], sky[min(int(f) + 1, len(sky) - 1)]
        k = f - int(f)
        c = tuple(int(a[i] + (b[i] - a[i]) * k) for i in range(3)) + (255,)
        out.paste(c, (0, y, out.width, y + 1))
    for n, img in enumerate(imgs):
        # only the floor's own squares: the preview fills the rest with the night blue
        g = levels[n]
        mask = Image.new("L", img.size, 0)
        for y in range(g.h):
            for x in range(g.w):
                if g.has(x, y):
                    mask.paste(255, (x * T, y * T, (x + 1) * T, (y + 1) * T))
        out.paste(img, (0, lift * (LEVELS - 1 - n)), mask)
    path = os.path.join(folder, MAP_NAME + " stacked.png")
    out.save(path)
    print("  preview", path)


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    dry = "--dry-run" in sys.argv
    if not dry:
        build_art()
    model_art.MODELS.setdefault("NI Bar", island_art.MODELS["NI Bar"])
    levels = build()
    check(levels)
    for g in levels:
        counts = cm.check(g)
        print(f"{g.name}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        if not dry:
            cm.save(g, os.path.join(WORLDS, g.name + ".jm"))
    if folder:
        preview(levels, folder)


if __name__ == "__main__":
    main()
