"""Builds the Nexus: a garden village on an island, cherry trees, lanterns, temples and a koi pond.

    python tools/build_nexus.py [--preview <folder>] [--dry-run]

Writes `Exalt/Nexus Garden.jm` and, for each event in build_castle_maps.EVENTS, a decorated copy
(`Exalt/Nexus Garden <Event>.jm`, played while `serverSettings.mapEvent` names it). The art is
tools/build_nexus_garden.py's; the vault and the guild halls are still built by
build_castle_maps.py and build_guild_halls.py.

The land is a hexagon, pointed at the top and the bottom, in a deep sea nobody can walk on; the
sea comes in over its southern point in two bays either side of a wooded headland. From the middle
out:
- the spawn (`Spawn`) on an island of pink-stained deck, itself a hexagon (`ISLAND`), with the
  healing basins (`NG Garden Basin`, NexusWorld) at its corners, the Armoire (`NG Armoire`) beside
  it, the Vault Portal, and the Support Bot's dungeon portal south of it (NexusGuide); the Name
  Changer and the Character Changer walk about its upper corners (NexusVillagers), and the XP
  ducks swim round it;
- a koi pond round the island, shallow enough to wade, crossed by wooden bridges to the west and
  east and cobbled ones from the island's two upper sides, all railed;
- north-west, the shrine of the realm portals: Realm 2's vortex in the middle of its court
  (`Realm2 Portal`) and four caverns round it where the realms' portals open (`Realm Portals`),
  all sunk into the floor (RealmPortalMonitor), the Arena's sand pit west of the vortex
  (`ARENA_PORTALS`, which DoomedWorlds.NexusPortals must match), a pagoda behind them and a torii
  at the gate;
- north of the pond, a dry garden of raked gravel round the elder cherry;
- north-east and east, the village: a shopping street whose stalls are the shops (`Store N`, see
  MerchantLists.Shops; a shop with more items than stalls rotates them) in front of its houses;
  the forge court north of it (the Divine Forge in an open smithy, `SMITHY`, with Amatsumara the
  smith god at his anvil, and the Enchanter), the quest house, a teahouse (the Tinkerer
  and the Chronicler, QuestGivers), the guild's house (Guild Hall Portal, Guild Register, notice
  boards), the market square (the Market Broker, `BROKER`, and its two pavilions of stalls, the
  `Market Stall` region, MarketStalls), and the storehouse whose yard holds the four shared vault
  chests. Their items are kept in Redis by the tile they had in the castle (`DbNexusChest`), so
  each chest carries its old tile as its name (`CHESTS`, NexusWorld.LinkVaultChests);
- west, the Hall of Champions (`CHAMPIONS`: the monument and a plinth for each board, the ring
  open to the east where the road comes in) and the Arena Leaderboard's court north of it, where
  the Herald keeps the server's week (QuestGivers);
- south, a forest of cherry, pine and maple between the pond and the sea, its floor marked
  `Petals` so blossom falls across the screen there (the Rust client), with a memorial clearing,
  where the twins Hana and Sora run about (NexusVillagers).
Villagers walk between the waypoints (`Nexus Waypoint`, NexusVillagers) and bees drift over the
flowers.

The layout is drawn here rather than in a map editor, so change it below and run it again.
`--preview` draws the map from the game's sprites and models, one tile per 8 pixels.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_castle_maps as cm  # noqa: E402
import build_nexus_garden as art  # noqa: E402
from nexus_garden import models as model_art  # noqa: E402

ROOT = cm.ROOT
WORLDS = cm.WORLDS
MAP_NAME = "Nexus Garden"

# --- the palette ---------------------------------------------------------------------------------

SEA = "NG Deep Sea"
POND = "NG Pond"
SHALLOWS = "NG Pond Shallows"
SAND = "NG Shore Sand"
MEADOW = "NG Meadow Grass"
GRASS = "NG Sakura Grass"
MOSS = "NG Forest Moss"
PETALS = "NG Petal Carpet"
EARTH = "NG Packed Earth"
PATH = "NG Stone Path"
MOSSY = "NG Mossy Path"
SLAB = "NG Shrine Slab"
GRAVEL = "NG Raked Gravel"
COBBLE = "NG Cobblestone"
TATAMI = "NG Tatami"
DECK = "NG Sakura Deck"
BEAM = "NG Deck Beam"
PLANKS_EW = "NG Bridge Planks EW"
PLANKS_NS = "NG Bridge Planks NS"
WATER = (SEA, POND, SHALLOWS)
GREEN = (MEADOW, GRASS, MOSS, PETALS)

SAKURA, WEEPING, PINE, MAPLE, GREAT = "NG Sakura Tree", "NG Weeping Sakura", "NG Black Pine", "NG Maple", "NG Great Sakura"
YOUNG = "NG Young Sakura"
LANTERN, PAPER = "NG Stone Lantern", "NG Paper Lantern"
BAMBOO, AZALEA, BOULDER = "NG Bamboo", "NG Azalea", "NG Boulder"
BASIN = art.BASIN
BLOCKER = art.BLOCKER
TREES = {SAKURA: 2, WEEPING: 2, PINE: 2, MAPLE: 2, GREAT: 3, YOUNG: 1}     # how far their crowns reach

# decoration slots, as the castle's (build_castle_maps): an event swaps what stands in them, and
# scatters its things on the open ground (`OPEN`)
ACCENT, FLAME, OPEN = "accent", "flame", "open"
EVENTS = {
    "Halloween": {
        "slots": {FLAME: ["HN Ppk 1", "HN Ppk 2", "HN Ppk 3", "HN Ppk 4"],      # jack-o'-lanterns for paper ones
                  ACCENT: [LANTERN, LANTERN, "HN Cdl"]},
        "scatter": [("AN FaLvs O", 0.05), ("AN FaLvs R", 0.04), ("HN Ppk 1", 0.008), ("HN Ppk 3", 0.008),
                    ("HN Dd Tr", 0.006), ("Spider Silk", 0.008), ("HN Stt Grg dk", 0.003)],
    },
}

# --- the layout ----------------------------------------------------------------------------------

SIZE = (136, 108)
HEX = (68, 10, 58, 88)          # the land: middle x, top y, half its width, its height
# the coast across the land's southern point: (x, y) west to east, the sea south of it
COAST = [(8, 82), (18, 78), (30, 73), (38, 77), (46, 84), (55, 89), (68, 92), (82, 88), (90, 82),
         (96, 75), (106, 77), (116, 80), (128, 86)]
ISLAND = (68, 61, 11, 12)       # the spawn's island: middle x and y, half its width and height
POND_SCALE = (1.6, 1.5)         # the pond round it, as the island's hexagon made this much bigger
CENTRE = ISLAND[:2]
BRIDGE_W, BRIDGE_E = (49, 57), (79, 87)          # the wooden bridges' x spans, at the spawn's row
BRIDGE_NW, BRIDGE_NE = 62, 74                    # the cobbled bridges' middle columns

# the shrine of the realm portals
VORTEX = (46, 31)                                # Realm 2's portal (6 tiles across)
CAVERNS = [(36, 25), (56, 26), (36, 37), (56, 37)]   # the realms' portals (4 across)
# the Arena's portal (4 across), west of the vortex: DoomedWorlds.NexusPortals must match
ARENA_PORTALS = [(34.5, 31.5)]
SHRINE = (28, 21, 62, 41)                        # its court, x0, y0, x1, y1
PAGODA = (46, 21)
SHRINE_TORII = (BRIDGE_NW, 41)                   # at the court's gate, over the path from the bridge
TORII_POSTS = [(-2, 0), (2, 0)]                  # a torii blocks only its pillars' tiles
ARCH_SPAN = 9                                    # the arched bridges' length (models.arch)

# the village
STREET_A = (76, 123, 44)                         # the shopping street: x0, x1 and its middle row
STREET_B = (100, 28, 75)                         # the cross street: its middle column, y0, y1
STREET_C = (86, 100, 61)                         # from the east bridge to the cross street
# shops along the street: (region, first stall x, stalls, north side); a stall every other tile
SHOPS = [
    ("Store 1", 78, 8, True),           # stat potions, a stall each
    ("Store 11", 104, 5, True),         # dungeon keys
    ("Store 12", 116, 4, True),         # Elder and Doomed keys
    ("Store 2", 80, 4, False),          # consumables
    ("Store 3", 90, 3, False),          # specials
    ("Store 5", 104, 3, False),         # pet generators
    ("Store 8", 110, 3, False),         # large cloths and clothing dyes
    ("Store 9", 116, 3, False),         # small cloths and accessory dyes
]
FORGE_COURT = (79, 19, 97, 33)
SMITHY = (87, 31)                       # the smithy: its model's tile, the front middle
# the Divine Forge, the smithy's furnace, and the Enchanter (which imprints gear,
# ImprintHandlers.cs), its altar: their invisible objects where the model shows them
FORGE = (85, 27)
ENCHANTER = (89, 27)
SMITH = (89, 33)                        # Amatsumara, the smith god, at his anvil before the smithy
GUILD_HALL = (108, 31)                  # the teahouse: GUILD HALL over its door
GUILD = {"NG Guild Register": (106, 34), "NG Guild Hall Door": (108, 34), "BulletinBoard": [(104, 35), (112, 35)]}
GUILD_HOUSE = (118, 32)                 # a plain house now
# the market: a courtyard between two rows of stalls (the `Market Stall` region in front of each),
# where the Market Broker, the Tinkerer and the Chronicler walk (MarketBroker, QuestGivers,
# NexusVillagers)
MARKET = (101, 50, 121, 73)
MARKET_ROWS = [(56, False), (67, True)]   # each row's stalls' y, and whether they face north
MARKET_STALLS_X = list(range(104, 119, 2))
BROKER = (110, 61)                      # where the Broker starts (MarketBroker)
QUEST_NPCS = [(106.5, 61.5), (114.5, 61.5)]  # QuestGivers' spots for the Tinkerer and the Chronicler
STOREHOUSE = (93, 67)
# The shared vault chests, and the castle tiles their items are kept under in Redis
CHESTS = [((89, 71), (61, 70)), ((91, 71), (79, 70)), ((95, 71), (61, 74)), ((97, 71), (79, 74))]

# the west: the Hall of Champions and the Arena
CHAMPIONS = (29, 61)
CHAMPIONS_RADIUS = 7.2
# the plinths round the monument, open to the east where the road comes in
# (Shared/champions ChampionBoards, `name:<board id>`)
CHAMPION_STATUES = [((5, -3), "fame"), ((5, 3), "stars"), ((1, 6), "achievements"),
                    ((-3, 6), "arenaSolo"), ((-6, 3), "arenaGroup"), ((-6, 0), "guildLiving"),
                    ((-6, -3), "guildDead"), ((-3, -6), "weeklyDungeons"), ((1, -6), "weeklyFame")]
ARENA_COURT = (18, 44, 42, 51)          # the dojo yard: the leaderboard's court (the portal is in the shrine)
ARENA_LEADERBOARD = (34, 47)
HERALD = (38.5, 47.5)                   # the Herald, beside it (QuestGivers, keep the two in step)
ARENA_MONUMENT = (24, 47)               # the dojo yard's samurai statue

# the forest's memorial clearing
MEMORIAL = (68, 86)
ELDER = (68, 30)                        # the elder cherry (192 pixels: its crown spans 24 tiles)


def hex_inside(x, y, cx, cy, hw, hh):
    """Inside a hexagon pointed at its top and bottom, `hw` half its width, `hh` half its height."""
    dx, dy = abs(x - cx), abs(y - cy)
    return dx <= hw and dy <= hh - dx * (hh / 2) / hw


def coast_y(x):
    for (x0, y0), (x1, y1) in zip(COAST, COAST[1:]):
        if x0 <= x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return COAST[0][1] if x < COAST[0][0] else COAST[-1][1]


class Grid:
    def __init__(self, w, h):
        self.name, self.w, self.h = MAP_NAME, w, h
        self.t = [[{"ground": SEA, "obj": None, "name": None, "region": None, "slot": None} for _ in range(w)]
                  for _ in range(h)]
        self.kept = set()           # tiles nothing may be scattered on: paths, courts, doorsteps
        self.models = []            # (name, x, y) of the 3D models, for the preview
        # the castle's Grid's fields, so build_castle_maps.decorate and save work on this one
        self.castle = [[True] * w for _ in range(h)]
        self.paths = self.kept

    def __getitem__(self, xy):
        return self.t[xy[1]][xy[0]]

    def ok(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def put(self, x, y, ground=None, obj=None, region=None, name=None, slot=None, keep=False):
        if not self.ok(x, y):
            return
        t = self.t[y][x]
        if ground:
            t["ground"] = ground
        if obj is not None or region is not None:
            t["obj"], t["name"], t["slot"] = obj, name, slot
        if region:
            t["region"] = region
        if keep:
            self.kept.add((x, y))

    def ground(self, x, y, ground, keep=False):
        if self.ok(x, y):
            self.t[y][x]["ground"] = ground
            if keep:
                self.kept.add((x, y))

    def land(self, x, y):
        return self.ok(x, y) and self.t[y][x]["ground"] not in WATER

    def empty(self, x, y):
        t = self[x, y] if self.ok(x, y) else None
        return t is not None and not t["obj"] and not t["region"]

    def place(self, x, y, obj, ground=None, name=None, slot=None, keep=True):
        """An object that must stand where it's put: stops the build if the tile is taken."""
        if not self.ok(x, y) or not self.empty(x, y):
            sys.exit("%s: %s can't stand at %d,%d (%s)" % (MAP_NAME, obj, x, y, self[x, y] if self.ok(x, y) else "off the map"))
        self.put(x, y, ground, obj, name=name, slot=slot, keep=keep)

    def model(self, name, x, y, ground=None, blockers=None):
        """A 3D model on (x, y), invisible blockers on the rest of its footprint (or on
        `blockers`, offsets from it)."""
        fw, fh = model_art.MODELS[name.replace(" NS", "").replace(" N", "") if name.endswith((" NS", " N")) else name][1]
        if name.endswith(" NS"):
            fw, fh = fh, fw
        if blockers is None:
            blockers = [(dx, dy) for dy in range(-(fh // 2), fh // 2 + 1) for dx in range(-(fw // 2), fw // 2 + 1)
                        if (dx, dy) != (0, 0)]
        for dx, dy in blockers:
            self.place(x + dx, y + dy, BLOCKER, ground)
        self.place(x, y, name, ground)
        self.models.append((name, x, y))

    def line(self, pts, width, ground, keep=True, only=None):
        """A path through the points, `width` tiles wide."""
        r = (width - 1) / 2
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
            for i in range(n + 1):
                x, y = x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n
                for yy in range(int(math.floor(y - r)), int(math.ceil(y + r)) + 1):
                    for xx in range(int(math.floor(x - r)), int(math.ceil(x + r)) + 1):
                        if math.hypot(xx - x, yy - y) <= r + 0.35 and self.ok(xx, yy):
                            if only is None or self[xx, yy]["ground"] in only:
                                self.ground(xx, yy, ground, keep)

    def rect(self, x0, y0, x1, y1, ground, keep=True):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.ground(x, y, ground, keep)

    def free(self, x, y, margin=1, grounds=GREEN):
        """Open green ground with nothing standing within `margin`, off every path."""
        for dy in range(-margin, margin + 1):
            for dx in range(-margin, margin + 1):
                if not self.ok(x + dx, y + dy):
                    return False
                t = self[x + dx, y + dy]
                if t["obj"] or t["region"] or t["ground"] not in grounds or (x + dx, y + dy) in self.kept:
                    return False
        return True

    def mark_open(self):
        for y in range(2, self.h - 2):
            for x in range(2, self.w - 2):
                if self.free(x, y, 1, (MEADOW, GRASS, MOSS)):
                    self.t[y][x]["slot"] = OPEN


# --- building it ---------------------------------------------------------------------------------

def build():
    g = Grid(*SIZE)
    rng = random.Random(20261030)
    lay_land(g, rng)
    lay_island(g)
    lay_bridges(g)
    lay_shrine(g)
    lay_village(g)
    lay_west(g)
    lay_forest(g, rng)
    lay_garden(g, rng)
    plant(g, rng)
    g.mark_open()
    return g


def lay_land(g, rng):
    hx, hy, hw, hh = HEX
    ix, iy, iw, ih = ISLAND
    for y in range(g.h):
        for x in range(g.w):
            wob = 0.9 * math.sin(x * 0.45 + y * 0.2) + 0.6 * math.sin(y * 0.7 - x * 0.3)
            inland = hex_inside(x + 0.5, y + 0.5, hx, hy + hh / 2, hw + wob * 0.6, hh / 2 + wob * 0.4)
            if inland and y + 0.5 < coast_y(x + 0.5) + wob:
                g.ground(x, y, MEADOW)
            pw, ph = iw * POND_SCALE[0] + wob * 0.8, ih * POND_SCALE[1] + wob * 0.6
            if hex_inside(x + 0.5, y + 0.5, ix, iy, pw, ph):
                g.ground(x, y, POND)
    # the shores: sand along the sea, shallows round the pond's edge
    for y in range(g.h):
        for x in range(g.w):
            here = g[x, y]["ground"]
            near = {g[x + dx, y + dy]["ground"] for dx in (-1, 0, 1) for dy in (-1, 0, 1) if g.ok(x + dx, y + dy)}
            if here == MEADOW and (SEA in near or any(g.ok(x + dx, y) and g[x + dx, y]["ground"] == SEA for dx in (-2, 2))):
                g.ground(x, y, SAND)
            elif here == POND and MEADOW in near:
                g.ground(x, y, SHALLOWS)
    # the garden's grass in the middle of the land, the forest's moss south of the pond
    for y in range(g.h):
        for x in range(g.w):
            if g[x, y]["ground"] != MEADOW:
                continue
            d = math.hypot((x - ix) / 1.3, y - iy)
            if y > iy + ih * 0.9 and 26 < x < 104:
                g.ground(x, y, MOSS)
            elif d < 34 + 3 * math.sin(x * 0.3):
                g.ground(x, y, GRASS)


def lay_island(g):
    ix, iy, iw, ih = ISLAND
    for y in range(iy - ih - 1, iy + ih + 2):
        for x in range(ix - iw - 1, ix + iw + 2):
            if hex_inside(x + 0.5, y + 0.5, ix + 0.5, iy + 0.5, iw, ih):
                edge = not all(hex_inside(x + 0.5 + dx, y + 0.5 + dy, ix + 0.5, iy + 0.5, iw, ih)
                               for dx in (-1, 0, 1) for dy in (-1, 0, 1))
                g.ground(x, y, BEAM if edge else DECK, keep=True)
    for y in range(iy - 2, iy + 3):
        for x in range(ix - 2, ix + 3):
            g.put(x, y, region="Spawn")
    for dx, dy in ((-4, -4), (4, -4), (-4, 4), (4, 4)):
        g.place(ix + dx, iy + dy, BASIN)
    # the Name Changer and the Character Changer walk about the island (NexusVillagers)
    g.place(ix, iy + 4, "NG Armoire")
    g.place(ix - 6, iy + 5, "Vault Portal")
    # stone lanterns at the island's corners, a paper lantern either side of each bridge's foot
    for x, y in ((ix, iy - ih + 1), (ix, iy + ih - 1), (ix - iw + 1, iy - ih // 2), (ix + iw - 1, iy - ih // 2),
                 (ix - iw + 1, iy + ih // 2), (ix + iw - 1, iy + ih // 2)):
        g.place(x, y, LANTERN, slot=ACCENT)
    for x in (ix - 7, ix + 7):
        g.place(x, iy - 2, PAPER, slot=FLAME)
        g.place(x, iy + 2, PAPER, slot=FLAME)
    # saplings off the bridges' lines
    for x, y in ((ix - 9, iy - 4), (ix + 9, iy - 4), (ix - 4, iy + 8), (ix + 4, iy + 8)):
        g.place(x, y, YOUNG)


def lay_bridges(g):
    """Four arched bridges over the pond (models.arch, drawn under what crosses them): nine tiles
    long, three across, their deck's grounds lifting whoever walks on them up and over the arch
    (`NG Arch Deck 1-5`); the tiles beside the deck, under the railings, are water and blocked."""
    ix, iy, iw, ih = ISLAND

    def deck(i):
        return "NG Arch Deck %d" % (min(i, ARCH_SPAN - 1 - i) + 1)
    # east and west, at the spawn's row
    for x0, x1 in (BRIDGE_W, BRIDGE_E):
        for i, x in enumerate(range(x0, x1 + 1)):
            for y in range(iy - 1, iy + 2):
                g.ground(x, y, deck(i), keep=True)
            for y in (iy - 2, iy + 2):
                if g[x, y]["ground"] not in (DECK, BEAM):
                    g.ground(x, y, POND)
                g.place(x, y, BLOCKER)
        mx = (x0 + x1) // 2
        g.place(mx, iy, "NG Arch Bridge")
        g.models.append(("NG Arch Bridge", mx, iy))
    # north-west and north-east, from the island's upper sides to the north shore
    for cx in (BRIDGE_NW, BRIDGE_NE):
        y = iy
        while g[cx, y]["ground"] not in (BEAM,):
            y -= 1
        y1 = y                                   # the island's edge
        y0 = y1 - ARCH_SPAN + 1
        for i, yy in enumerate(range(y0, y1 + 1)):
            for x in range(cx - 1, cx + 2):
                g.ground(x, yy, deck(i), keep=True)
            for x in (cx - 2, cx + 2):
                if g[x, yy]["ground"] not in (DECK, BEAM):
                    g.ground(x, yy, POND)
                if g.empty(x, yy):
                    g.place(x, yy, BLOCKER)
        my = (y0 + y1) // 2
        g.place(cx, my, "NG Stone Arch NS")
        g.models.append(("NG Stone Arch NS", cx, my))
        g.line([(cx, y0 - 1), (cx, y0 - 3)], 3, PATH)


def lay_shrine(g):
    x0, y0, x1, y1 = SHRINE
    vx, vy = VORTEX
    # the court: slabs, raked gravel either side of the vortex and round the caverns' circles
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if not g.land(x, y):
                continue
            d = math.hypot((x - (x0 + x1) / 2) / ((x1 - x0) / 2), (y - (y0 + y1) / 2) / ((y1 - y0) / 2))
            if d <= 1.0:
                g.ground(x, y, SLAB, keep=True)
    for (cx, cy), r in [(VORTEX, 4.6)] + [(c, 3.4) for c in CAVERNS] + [((int(ARENA_PORTALS[0][0]), int(ARENA_PORTALS[0][1])), 3.4)]:
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if math.hypot(x - cx, y - cy) <= r:
                    g.ground(x, y, SLAB, keep=True)
    g.put(vx, vy, region="Realm2 Portal")
    for cx, cy in CAVERNS:
        g.put(cx, cy, region="Realm Portals")
    # lanterns round the vortex, the pagoda behind it, the paths in from the bridge
    for k in range(8):
        a = k / 8 * math.tau + math.pi / 8
        g.place(int(round(vx + math.cos(a) * 5.6)), int(round(vy + math.sin(a) * 5.6)), LANTERN, slot=ACCENT)
    g.model("NG Pagoda", *PAGODA, ground=SLAB)
    for dx in (-4, 4):
        g.place(PAGODA[0] + dx, PAGODA[1] + 1, LANTERN, SLAB, slot=ACCENT)
    tx, ty = SHRINE_TORII
    g.line([(tx, 46), (tx, ty), (vx + 5, vy + 4)], 3, PATH)
    g.model("NG Torii", tx, ty + 1, blockers=TORII_POSTS)


def lay_village(g):
    x0, x1, sy = STREET_A
    # the streets: packed earth, the cross street and the one from the east bridge
    g.line([(BRIDGE_NE, 44), (BRIDGE_NE, sy), (x1, sy)], 3, EARTH)
    g.line([(STREET_B[0], STREET_B[1]), (STREET_B[0], STREET_B[2])], 3, EARTH)
    g.line([(BRIDGE_E[1], STREET_C[2]), (STREET_C[1], STREET_C[2])], 3, EARTH)
    # the shops: a strip of tatami along the street, a stall every other tile, a paper lantern
    # between each shop and the next, a house behind every few stalls
    for region, sx, n, north in SHOPS:
        y = sy - 2 if north else sy + 2
        strip = (y - 1, y) if north else (y, y + 1)
        lo, hi = sx - 1, sx + 2 * (n - 1) + 1
        for yy in strip:
            for x in range(lo, hi + 1):
                g.ground(x, yy, TATAMI, keep=True)
        for i in range(n):
            g.put(sx + 2 * i, y, TATAMI, region=region, keep=True)
        for x in (lo - 1, hi + 1):
            if g.empty(x, y):
                g.place(x, y, PAPER, slot=FLAME)
        hy = y - 4 if north else y + 4
        if not north and sx >= MARKET[0]:
            continue                             # the market's courtyard is behind these
        for hx in range(lo + 2, hi - 1, 6):
            if hi - hx >= 2:
                g.model("NG House" if north else "NG House N", hx + 1, hy)
    # the forge court: slabs, the smithy on them (the Divine Forge and the Enchanter inside it),
    # lanterns at its corners
    fx0, fy0, fx1, fy1 = FORGE_COURT
    g.rect(fx0, fy0, fx1, fy1, SLAB)
    g.line([(SMITHY[0], fy1), (SMITHY[0], sy)], 3, SLAB)
    for x, y in ((fx0, fy0), (fx1, fy0), (fx0, fy1), (fx1, fy1)):
        g.place(x, y, LANTERN, slot=ACCENT)
    smithy(g)
    g.place(fx1 - 1, fy1 - 4, PAPER, slot=FLAME)
    g.model("NG House N", fx1 - 3, fy0 + 2)
    # the Guild Hall: the teahouse, GUILD HALL over its door; its register and its door to the
    # guild's hall stand (unseen) on the steps, the notice boards either side
    g.model("NG Teahouse", *GUILD_HALL)
    g.rect(GUILD_HALL[0] - 5, GUILD_HALL[1] + 3, GUILD_HALL[0] + 5, GUILD_HALL[1] + 6, PATH)
    for name in ("NG Guild Register", "NG Guild Hall Door"):
        g.place(*GUILD[name], name)
    for x, y in GUILD["BulletinBoard"]:
        g.place(x, y, "BulletinBoard")
    g.model("NG House", *GUILD_HOUSE)
    g.line([(STREET_B[0], GUILD_HALL[1] + 4), (GUILD_HOUSE[0], GUILD_HALL[1] + 4)], 3, PATH)
    lay_market(g)
    # the storehouse and its yard of vault chests, fenced
    sx, sy2 = STOREHOUSE
    g.rect(sx - 6, sy2 + 2, sx + 6, sy2 + 6, PATH)
    g.model("NG Storehouse", sx, sy2)
    for (x, y), (ox, oy) in CHESTS:
        g.place(x, y, "Vault Chest", name="%d_%d" % (ox, oy))
    g.line([(STREET_B[0], sy2 + 4), (sx + 6, sy2 + 4)], 3, PATH)
    for x in (sx - 6, sx + 6):
        for y in range(sy2 + 2, sy2 + 7):
            if x == sx - 6 or abs(y - sy2 - 4) > 1:     # the yard's gate faces the cross street
                g.model("NG Bamboo Fence NS", x, y)


def smithy(g):
    """The smithy (models.smithy): its posts, back wall, furnace and altar block their tiles; the
    Divine Forge's and the Enchanter's (invisible) objects stand in the furnace and the altar, the
    smoke on the chimney; Amatsumara at his anvil before it."""
    sx, sy = SMITHY
    blocked = set()
    for dx in (-5, 4):                          # the posts, east and west
        for dy in (0, -2, -4, -6):
            blocked.add((sx + dx, sy + dy))
    for dx in range(-4, 4):                     # the low back wall
        blocked.add((sx + dx, sy - 6))
    for x in range(sx - 4, sx):                 # the furnace
        for y in range(sy - 6, sy - 3):
            blocked.add((x, y))
    for x in range(sx + 1, sx + 4):             # the altar
        for y in (sy - 5, sy - 4):
            blocked.add((x, y))
    smoke = (sx - 3, sy - 5)
    special = {smoke, FORGE, ENCHANTER}
    for x, y in sorted(blocked - special):
        g.place(x, y, BLOCKER)
    g.place(*smoke, "NG Chimney Smoke")
    g.place(*FORGE, "NG Divine Forge")
    g.place(*ENCHANTER, "NG Enchanter")
    g.place(*SMITH, "NG Smith God")
    g.place(SMITH[0] + 1, SMITH[1], "NG Anvil")
    g.place(sx, sy, "NG Smithy")
    g.models.append(("NG Smithy", sx, sy))


def lay_market(g):
    """The market: a stone court, two rows of stalls in the village's style facing a courtyard
    (models.yatai, what sells on the tile in front), a cherry and lanterns in the middle, the
    Broker's house at the far end."""
    x0, y0, x1, y1 = MARKET
    g.rect(x0, y0, x1, y1, SLAB)
    for y in range(58, 66):
        for x in range(103, 120):
            g.ground(x, y, PATH, keep=True)
    for y, north in MARKET_ROWS:
        for x in MARKET_STALLS_X:
            g.put(x, y, SLAB, region="Market Stall", keep=True)
            g.model("NG Yatai N" if north else "NG Yatai", x, y + (1 if north else -1))
        for x in (MARKET_STALLS_X[0] - 2, MARKET_STALLS_X[-1] + 2):
            g.place(x, y, PAPER, slot=FLAME)
    cx, cy = (x0 + x1) // 2, 61
    g.place(cx, cy, YOUNG)
    for dx in (-5, 5):
        g.place(cx + dx, cy - 2, LANTERN, slot=ACCENT)
        g.place(cx + dx, cy + 2, LANTERN, slot=ACCENT)
    for dx in (-3, 3):
        g.place(cx + dx, cy + 3, "NG Bench")
    g.model("NG House N", cx, y1 - 2)


def lay_west(g):
    hx, hy = CHAMPIONS
    # the road from the west bridge
    g.line([(BRIDGE_W[0], CENTRE[1]), (hx + 8, CENTRE[1]), (hx + 8, hy)], 3, PATH)
    # the Hall of Champions: a round court of slabs, the monument in its middle, the plinths round it
    for y in range(hy - 9, hy + 10):
        for x in range(hx - 9, hx + 10):
            d = math.hypot(x - hx, y - hy)
            if d <= CHAMPIONS_RADIUS:
                g.ground(x, y, SLAB if d > 1.5 else GRAVEL, keep=True)
            elif d <= CHAMPIONS_RADIUS + 1:
                g.ground(x, y, BEAM, keep=True)
    g.rect(hx + 1, hy - 1, hx + 9, hy + 1, PATH)
    g.place(hx, hy, "Champions Monument")
    for (dx, dy), board in CHAMPION_STATUES:
        g.place(hx + dx, hy + dy, "Champion Statue", name="name:%s" % board)
    for dx, dy in ((7, -5), (7, 5), (-5, -7), (-5, 7)):
        g.place(hx + dx, hy + dy, LANTERN, slot=ACCENT)
    # the dojo yard north of it: raked sand inside a border of slabs, the samurai statue honouring
    # the Arena's champions, banners along its back, racks of weapons, straw dummies, a taiko by
    # the leaderboard, and the dojo behind
    ax0, ay0, ax1, ay1 = ARENA_COURT
    g.rect(ax0, ay0, ax1, ay1, SLAB)
    g.rect(ax0 + 1, ay0 + 1, ax1 - 1, ay1 - 1, "NG Dojo Sand")
    g.line([(hx + 8, hy), (hx + 8, ay1)], 3, PATH)
    g.place(*ARENA_LEADERBOARD, "Arena Leaderboard")
    g.place(*ARENA_MONUMENT, "NG Samurai Statue")
    for i, x in enumerate(range(ax0 + 1, ax1, 3)):
        if g.empty(x, ay0):
            g.place(x, ay0, "NG Nobori Red" if i % 2 == 0 else "NG Nobori Indigo")
    for x, y in ((ax0, ay1), (ax1, ay1)):
        g.place(x, y, PAPER, slot=FLAME)
    for x in (ax0 + 2, ax0 + 3):
        g.place(x, ay1 - 1, "NG Weapon Rack")
    for x in (27, 29, 31):
        g.place(x, ay1 - 2, "NG Straw Dummy")
    g.place(ARENA_LEADERBOARD[0] + 2, ARENA_LEADERBOARD[1] - 2, "NG Taiko")
    g.place(ARENA_LEADERBOARD[0] - 4, ARENA_LEADERBOARD[1], LANTERN, slot=ACCENT)
    g.model("NG House", 30, ay0 - 3)


def lay_forest(g, rng):
    # a mossy path from the west round through the woods to the storehouse
    pts = [(BRIDGE_W[0] - 4, CENTRE[1] + 1), (44, 72), (52, 82), (MEMORIAL[0] - 4, MEMORIAL[1]), (MEMORIAL[0] + 4, MEMORIAL[1]),
           (82, 82), (88, 76), (STOREHOUSE[0] - 7, STOREHOUSE[1] + 4)]
    g.line(pts, 2, MOSSY, only=GREEN + (SAND,))
    mx, my = MEMORIAL
    for y in range(my - 3, my + 4):
        for x in range(mx - 5, mx + 6):
            if math.hypot((x - mx) / 1.5, y - my) <= 3.6 and g.land(x, y):
                g.ground(x, y, PETALS, keep=True)
    g.place(mx, my - 2, "NG Wayside Shrine")
    for dx in (-3, 3):
        g.place(mx + dx, my + 2, "NG Bench")
    g.place(mx - 6, my, LANTERN, slot=ACCENT)
    g.place(mx + 6, my, LANTERN, slot=ACCENT)


def lay_garden(g, rng):
    """The garden north of the pond between the shrine and the village: a dry garden of raked
    gravel round the elder cherry, twenty-four tiles tall, its blossom strewn on the gravel about
    its foot (`NG Petal Gravel`, where petals fall round you in the Rust client)."""
    tx, ty = ELDER
    for y in range(ty - 8, ty + 8):
        for x in range(tx - 9, tx + 10):
            d = math.hypot((x - tx) / 8.5, (y - ty) / 6.5)
            if d <= 1 and g.land(x, y) and g[x, y]["ground"] not in WATER:
                g.ground(x, y, "NG Petal Gravel" if d < 0.75 else GRAVEL, keep=True)
    g.place(tx, ty, "NG Elder Sakura")
    for x, y in ((tx - 6, ty - 1), (tx + 6, ty + 1), (tx - 4, ty + 3), (tx + 4, ty + 3)):
        g.place(x, y, BOULDER)
    for x, y in ((tx - 8, ty + 1), (tx + 8, ty + 2)):
        g.place(x, y, LANTERN, slot=ACCENT)
    g.line([(BRIDGE_NW, 43), (BRIDGE_NE, 43)], 2, PATH)


def plant(g, rng):
    """Trees, bamboo, bushes and flowers on the open ground, thicker in the forest; lily pads and
    koi on the pond; waypoints for the villagers at the village's doors and squares."""
    spots = [(x, y) for y in range(g.h) for x in range(g.w)]
    rng.shuffle(spots)

    def shore(x, y, r):
        return any(g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] in (SAND, SEA)
                   for dx in range(-r, r + 1, 2) for dy in range(-r, r + 1, 2))

    def near_kept(x, y, r):
        return any((x + dx, y + dy) in g.kept for dx in range(-r, r + 1) for dy in range(-r, r + 1))

    for x, y in spots:
        ground = g[x, y]["ground"]
        if ground not in GREEN + (SAND,):
            continue
        forest = ground in (MOSS, PETALS)
        edge = shore(x, y, 6)
        r = rng.random()
        # trees: a wood in the south, groves along the shores, a few in the garden and the village
        chance = 0.3 if forest else 0.2 if edge else 0.05
        if ground == SAND:
            chance = 0.06
        if r < chance and g.free(x, y, 1, GREEN + (SAND,)) and not near_kept(x, y, 2):
            if ground == SAND:
                tree = PINE
            elif forest:
                tree = rng.choice([SAKURA, SAKURA, SAKURA, SAKURA, WEEPING, WEEPING, PINE, MAPLE, BAMBOO])
            elif edge:
                tree = rng.choice([PINE, PINE, SAKURA, SAKURA, BAMBOO, BAMBOO, MAPLE])
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
        elif r < 0.12 and g.free(x, y, 0, GREEN + (SAND,)):
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
    # waypoints: before each house's door, at the squares and the bridges' feet
    points = [(x, y + 3) for name, x, y in g.models if name in ("NG House", "NG Teahouse")]
    points += [(x, y - 3) for name, x, y in g.models if name == "NG House N"]
    points += [(BROKER[0], BROKER[1] + 4), (STREET_B[0], 50), (STREET_B[0], 66), (90, STREET_A[2]),
               (114, STREET_A[2]), (BRIDGE_NE, 43), (BRIDGE_NW, 43), (FORGE[0] + 3, FORGE[1] + 3),
               (STOREHOUSE[0], STOREHOUSE[1] + 4), (CHAMPIONS[0] + 8, CENTRE[1]), (MEMORIAL[0], MEMORIAL[1] + 1),
               (VORTEX[0] + 5, VORTEX[1] + 6), (64, 40)]
    for x, y in points:
        for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
            if g.ok(x + dx, y + dy) and g.empty(x + dx, y + dy) and g.land(x + dx, y + dy):
                g.put(x + dx, y + dy, region="Nexus Waypoint")
                break


# --- checks and output ---------------------------------------------------------------------------

def check_nexus(g):
    regions = {}
    for row in g.t:
        for t in row:
            if t["region"]:
                regions[t["region"]] = regions.get(t["region"], 0) + 1
    assert regions.get("Spawn") == 25, regions
    assert regions.get("Market Stall") == 16, regions
    assert regions.get("Realm Portals") == len(CAVERNS), regions
    assert regions.get("Realm2 Portal") == 1, regions
    assert regions.get("Nexus Waypoint", 0) >= 10, regions
    for (x, y), _ in CHESTS:
        assert g[x, y]["obj"] == "Vault Chest", (x, y)
    for x, y in ARENA_PORTALS:
        for yy in range(int(y) - 2, int(y) + 3):
            for xx in range(int(x) - 2, int(x) + 3):
                assert not g[xx, yy]["obj"] and g.land(xx, yy), ("the Arena portal's floor", xx, yy)
    assert g[FORGE]["obj"] == "NG Divine Forge"
    assert g[ENCHANTER]["obj"] == "NG Enchanter"
    assert g.empty(*BROKER) and g.land(*BROKER), BROKER
    assert g[ARENA_LEADERBOARD]["obj"] == "Arena Leaderboard"
    assert g[CHAMPIONS]["obj"] == "Champions Monument"
    for (dx, dy), board in CHAMPION_STATUES:
        t = g[CHAMPIONS[0] + dx, CHAMPIONS[1] + dy]
        assert t["obj"] == "Champion Statue" and t["name"] == "name:%s" % board, (dx, dy)
        assert dx < 5 or abs(dy) > 1, "the road to the monument stays open"
    for x, y in QUEST_NPCS + [HERALD]:
        assert g.empty(int(x), int(y)) and g.land(int(x), int(y)), (x, y)
    # the portals' floors: nothing standing on a cavern's or the vortex's tiles
    for (cx, cy), r in [(VORTEX, 3)] + [(c, 2) for c in CAVERNS]:
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                assert not g[x, y]["obj"], ("something on a portal's floor", x, y, g[x, y]["obj"])


def preview(g, path):
    """The map as the game draws it, from above: grounds, flat things, then everything standing,
    back to front, the models through the client's own drawing (nexus_garden.render)."""
    from PIL import Image
    from nexus_garden import render
    textures, _ = cm.game_data()
    manifest = __import__("json").load(open(os.path.join(ROOT, "client-rs", "assets", "manifest.json"), encoding="utf-8"))
    sheets = {s["name"]: s for s in manifest["image_sets"]}
    cache, meshes = {}, {}

    def sprite(name):
        if name not in cache:
            cache[name] = None
            if name in textures and textures[name][0] in sheets:
                sheet, index = textures[name]
                s = sheets[sheet]
                img = Image.open(os.path.join(ROOT, "client", "src", s["file"])).convert("RGBA")
                cols = img.width // s["w"]
                box = ((index % cols) * s["w"], (index // cols) * s["h"])
                cache[name] = img.crop(box + (box[0] + s["w"], box[1] + s["h"]))
        return cache[name]

    T = 8
    out = Image.new("RGBA", (g.w * T, g.h * T), (6, 10, 24, 255))
    flat = set(["NG Koi", "NG Lily Pad", "NG Lotus", "NG Pink Flowers", "NG Blue Flowers", "NG Fallen Petals", "NG Stepping Stone"])
    for y in range(g.h):
        for x in range(g.w):
            t = g[x, y]
            s = sprite(t["ground"]) if t["ground"] else None
            if s:
                out.paste(s.resize((T, T)), (x * T, y * T))
            if t["obj"] in flat and sprite(t["obj"]):
                out.alpha_composite(sprite(t["obj"]).resize((T, T)), (x * T, y * T))
    # the floor portals, as the server will place them
    arena = (int(ARENA_PORTALS[0][0]), int(ARENA_PORTALS[0][1]))
    for (cx, cy), name, size in [(VORTEX, art.REALM2_PORTAL, 6), (arena, art.ARENA_PORTAL, 4)] + [(c, art.REALM_PORTAL, 4) for c in CAVERNS[:2]]:
        img = sprite(name)
        if img:
            px = size * T
            out.alpha_composite(img.resize((px, px), Image.NEAREST), (int((cx + 0.5) * T - px / 2), int((cy + 0.5) * T - px / 2)))
    models = {(x, y): n for n, x, y in g.models}
    for y in range(g.h):
        for x in range(g.w):
            t = g[x, y]
            name = t["obj"]
            if (x, y) in models:
                base = models[(x, y)]
                key = base.replace(" NS", "").replace(" N", "") if base.endswith((" NS", " N")) else base
                if key not in meshes:
                    meshes[key] = model_art.MODELS[key][0]()
                mesh, tex = meshes[key]
                turn = 90 if base.endswith(" NS") else 180 if base.endswith(" N") else 0
                render.draw_model(mesh, tex, out, (x + 0.5) * T, (y + 0.5) * T, T, 0.0, turn, 0.5)
            elif name and name not in flat and name != BLOCKER and sprite(name):
                img = sprite(name)
                w, h = img.width, img.height
                out.alpha_composite(img.resize((w, h), Image.NEAREST), (int((x + 0.5) * T - w / 2), (y + 1) * T - h))
            if t["region"] and t["region"] not in ("Spawn",):
                colour = (255, 122, 61, 120) if t["region"] != "Nexus Waypoint" else (120, 200, 255, 90)
                out.alpha_composite(Image.new("RGBA", (T, T), colour), (x * T, y * T))
    out.resize((g.w * T * 2, g.h * T * 2), Image.NEAREST).save(path)


def decorate(g, event, seed):
    """A copy of the map with the event's decorations (see EVENTS)."""
    spec = EVENTS[event]
    rng = random.Random(seed)
    out = Grid(g.w, g.h)
    out.name = "%s %s" % (MAP_NAME, event)
    out.t = [[dict(t) for t in row] for row in g.t]
    out.kept, out.models = g.kept, g.models
    for row in out.t:
        for t in row:
            if t["slot"] in spec["slots"]:
                t["obj"] = rng.choice(spec["slots"][t["slot"]])
            elif t["slot"] == OPEN and not t["obj"]:
                r = rng.random()
                for obj, chance in spec["scatter"]:
                    if r < chance:
                        t["obj"] = obj
                        break
                    r -= chance
    return out


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    nexus = build()
    check_nexus(nexus)
    maps = [nexus] + [decorate(nexus, event, 3) for event in EVENTS]
    for g in maps:
        counts = cm.check(g)
        print(f"{g.name}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        path = os.path.join(WORLDS, g.name + ".jm")
        if folder:
            preview(g, os.path.join(folder, g.name + ".png"))
        if "--dry-run" not in sys.argv:
            cm.save(g, path)
            print("  written", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()
