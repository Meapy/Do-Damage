"""Builds the nexus and the vault, castles of the website's night sky, and their event versions.

    python tools/build_castle_maps.py [--preview <folder>] [--dry-run]

Writes `Exalt/Nexus Castle.jm` and `Exalt/Vault Castle.jm`, and for each event in `EVENTS` a
decorated copy, `Exalt/Nexus Castle <Event>.jm` and `Exalt/Vault Castle <Event>.jm`. The world
server plays an event's maps while `serverSettings.mapEvent` in wServer.json names it
(`World.LoadMapFromData`), and the plain ones otherwise.

The maps are drawn here rather than in a map editor, so change a layout below and run it again.
Both are built from the same parts (`Grid.castle`, `Grid.pavilion` and the palette), in the look
of the client: dark violet stone, ember light from braziers and lit windows, a dark moat.

An event never moves anything. The layouts mark decoration slots as they go (`ACCENT`, `FLAME`,
`BRAZIER`, `BANNER`, `CORNER`, `CENTRE`), and the open floor away from paths, regions and anything
players use is found afterwards (`OPEN`). An event swaps what stands in the slots and scatters
decorations on the open floor (`decorate`), so regions, portals and NPCs stay where they are.

The nexus, from the middle out:
- the spawn (the `Spawn` region) on an ember rug, the healing fountains at its corners, the Name
  Changer, Character Changer and Armoire beside it, the Vault Portal to the west and the guild's
  corner (Guild Hall Portal, Guild Register, notice boards) to the east;
- the keep to the north, whose great hall holds the realm portals (the `Realm Portals` region,
  one portal per tile) along a red carpet;
- a market ward behind a colonnade on each side: pavilions whose stalls are shop regions
  (`Store N`, see `MerchantLists.Shops`; a shop with more items than stalls rotates them). The
  west ward holds the dungeon keys, pet generators, clothing, accessories, stat potions and
  specials; the east ward the Elder and Doomed keys and the consumables north of the street,
  and south of it the Hall of Champions (`CHAMPIONS`): a carpet from the street to a round court
  of gold, the Champions Monument in its middle (the living's leaderboard in the Rust client)
  and a plinth for each board's champion around it (`CHAMPION_STATUES`, in Shared/champions'
  board order);
- the south yard: the Arena portal (`ARENA_PORTALS`, which `DoomedWorlds.NexusPortals`
  must match) west of the road with the Arena Leaderboard east of it, the gatehouse with its portcullis, the forge
  hall (the Divine Forge and the Enchanter side by side on floors of gold, a carpet from its door
  before them), and the treasury. The four shared vault chests keep their items in Redis by tile (`DbNexusChest`), so
  they stay on the tiles they had in the old map (`CHESTS`).

The vault is a cellar under the castle, walled with the tall walls of Oryx's Sanctuary and dark
beyond them (`Grid.underground_walls`): its chest hall (the `Vault` region; `VaultWorld` fills the chests
nearest the spawn first, so only their number matters), the gift chests (`Gifting Chest`) and a
court with the Potion Storage, Armoire, Guild Chronicle, the Training Dummy and the portals.

`--preview` draws each map from the game's sprites, one tile per 8 pixels, to check a change
without starting a server.
"""
import base64
import glob
import json
import math
import os
import random
import re
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORLDS = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Exalt")

# --- the palette ---------------------------------------------------------------------------------

# grounds
FLOOR = "O3B Pattern Base Dark"     # courtyards, dark violet stone
PLAZA = "LOD Black Tile"            # squares, violet diamonds
PATH = "GhostGround Dark"           # streets, dark violet brick
RUG = "Puppet Wood Tile"            # spawns, an ember-red rug
STALL = "Purple Rug"                # pavilion floor
STALL_EDGE = "Gothic Big Tile"      # pavilion border
HALL_FLOOR = "Dragon Tile Purple"   # great halls
CARPET = "Gothic Rug"               # their red carpets
STONE = "Castle Stone Floor Tile Dark"  # treasuries and the memorial
MOAT = "Black Water Light"          # dark violet water, out of reach outside the walls
GOLD_FLOOR = ["Elder Gold Tile"] + [f"Elder Gold Tile Lit {i}" for i in range(1, 7)]  # the forge's floor, dark to bright

# objects
WALL_OUT = "GhostShip Wall"         # the curtain wall's outer face
WALL_IN = "Gothic Bricks"           # its inner face, and brick posts
WINDOW = "Library Corrupt Flame Wall"   # a lit window
TOWER = "Dragon Wall Purple"
PORTCULLIS = "Grey Bars Wall"
BRAZIER_OBJ = "Jungle Brazier"      # fills its tile
FLAME_OBJ = "Elder Brazier"         # walkable, for open floor
CANDLES = "Big Candle"
CRYSTAL = "CC Medium Amethyst Crystal"
BANNER_OBJ = "O3 D Banner"
FOUNTAIN = "White Fountain"
STATUE = "Statue of Oryx"
# underground: the tall walls of Oryx's Sanctuary, which hide what's behind them
HIGH_WALL = "O3B High Wall 1 SE"
HIGH_BRICKS = "O3B High Wall Bricks 1 SE"
HIGH_FLAME = "O3B High Wall Flame Right SE"
HIGH_BANNER = ("O3B High Wall Banner L SE", "O3B High Wall Banner R SE")
WALLS = (WALL_OUT, WALL_IN, WINDOW, TOWER, PORTCULLIS, HIGH_WALL, HIGH_BRICKS, HIGH_FLAME) + HIGH_BANNER

# decoration slots (see the docstring)
ACCENT, FLAME, BRAZIER, BANNER, CORNER, CENTRE, OPEN = "accent", "flame", "brazier", "banner", "corner", "centre", "open"

# Events: what each slot holds instead (a list is picked from at random), and what's scattered
# on the open floor (object, chance per tile). An object an event doesn't list stays.
EVENTS = {
    "Halloween": {
        "slots": {
            ACCENT: ["HN Ppk 1", "HN Ppk 2", "HN Ppk 3", "HN Ppk 4", "HN Cdl", "HN Stn Mmts dk", "HN Bns dk"],
            FLAME: [FLAME_OBJ, "HN Cdl"],
            CORNER: ["HN Ppk 1", "HN Ppk 2", "HN Ppk 3", "HN Ppk 4"],
            CENTRE: ["HN WH Pt L"],              # a witch's cauldron
            BRAZIER: [BRAZIER_OBJ, BRAZIER_OBJ, "Pumpkin"],
        },
        "scatter": [
            ("AN FaLvs O", 0.05), ("AN FaLvs R", 0.04),      # fallen leaves
            ("HN Ppk 1", 0.008), ("HN Ppk 3", 0.008),
            ("HN Dd Tr", 0.012), ("AN BTr O", 0.006), ("AN BTr R", 0.006),
            ("HN Stt Grg dk", 0.004), ("Spider Silk", 0.01), ("HN Bns", 0.008),
        ],
    },
}


# --- building blocks -----------------------------------------------------------------------------

def inside(poly, px, py):
    hit = False
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        if (y0 > py) != (y1 > py) and px < x0 + (py - y0) * (x1 - x0) / (y1 - y0):
            hit = not hit
    return hit


class Grid:
    def __init__(self, name, w, h):
        self.name, self.w, self.h = name, w, h
        self.t = [[{"ground": None, "obj": None, "name": None, "region": None, "slot": None}
                   for _ in range(w)] for _ in range(h)]
        self.castle = [[False] * w for _ in range(h)]
        self.paths = set()          # tiles kept clear for walking: no event decoration there

    def __getitem__(self, xy):
        return self.t[xy[1]][xy[0]]

    def put(self, x, y, ground=None, obj=None, region=None, name=None, slot=None):
        t = self.t[y][x]
        if ground:
            t["ground"] = ground
        t["obj"], t["name"], t["slot"] = obj, name, slot
        if region:
            t["region"] = region

    def rect(self, x0, y0, x1, y1, ground, obj=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, ground, obj)

    def floor(self, x0, y0, x1, y1, ground):
        """Lays ground under the castle's open tiles, leaving its walls standing."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if self.castle[y][x] and not self.is_wall(x, y):
                    self.put(x, y, ground)

    def path(self, x0, y0, x1, y1, ground=PATH):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if not self.is_wall(x, y):
                    self.put(x, y, ground)
                    self.paths.add((x, y))

    def is_wall(self, x, y):
        return self.t[y][x]["obj"] in WALLS

    def flame(self, x, y, ground):
        self.put(x, y, ground, FLAME_OBJ, slot=FLAME)

    def brazier(self, x, y, ground):
        self.put(x, y, ground, BRAZIER_OBJ, slot=BRAZIER)

    def banner(self, x, y, ground):
        self.put(x, y, ground, BANNER_OBJ, slot=BANNER)

    def castle_walls(self, outline, towers, tower_radius=2.6, moat=2):
        """Floor inside the outline, the curtain wall on its two outer rings (every sixth inner
        tile a lit window), round towers on the given points and the moat outside."""
        w, h = self.w, self.h
        for y in range(h):
            for x in range(w):
                self.castle[y][x] = inside(outline, x + 0.5, y + 0.5)

        def ring(x, y):
            for r in (1, 2):
                if any(not (0 <= x + dx < w and 0 <= y + dy < h) or not self.castle[y + dy][x + dx]
                       for dx in range(-r, r + 1) for dy in range(-r, r + 1)):
                    return r
            return 3

        n = 0
        for y in range(h):
            for x in range(w):
                if not self.castle[y][x]:
                    continue
                r = ring(x, y)
                if r == 1:
                    self.put(x, y, FLOOR, WALL_OUT)
                elif r == 2:
                    n += 1
                    self.put(x, y, FLOOR, WINDOW if n % 6 == 0 else WALL_IN)
                else:
                    self.put(x, y, FLOOR)
        for tx, ty in towers:
            for y in range(int(ty - tower_radius) - 1, int(ty + tower_radius) + 2):
                for x in range(int(tx - tower_radius) - 1, int(tx + tower_radius) + 2):
                    d = math.hypot(x + 0.5 - tx, y + 0.5 - ty)
                    if d <= tower_radius and 0 <= x < w and 0 <= y < h:
                        lit = d > tower_radius - 1 and (x + y) % 3 == 0
                        self.put(x, y, FLOOR, WINDOW if lit else TOWER)
                        self.castle[y][x] = True
        for y in range(h):
            for x in range(w):
                if not self.castle[y][x] and any(
                        0 <= x + dx < w and 0 <= y + dy < h and self.castle[y + dy][x + dx]
                        for dx in range(-moat, moat + 1) for dy in range(-moat, moat + 1)):
                    self.put(x, y, MOAT)

    def underground_walls(self, outline, banners=()):
        """Floor inside the outline and one ring of high walls, flames every few tiles and a
        pair of banners at each of `banners` (the left one's tile); nothing outside, so it's
        dark as a cellar."""
        w, h = self.w, self.h
        for y in range(h):
            for x in range(w):
                self.castle[y][x] = inside(outline, x + 0.5, y + 0.5)
        n = 0
        for y in range(h):
            for x in range(w):
                if not self.castle[y][x]:
                    continue
                edge = any(not (0 <= x + dx < w and 0 <= y + dy < h) or not self.castle[y + dy][x + dx]
                           for dx in (-1, 0, 1) for dy in (-1, 0, 1))
                if edge:
                    n += 1
                    self.put(x, y, FLOOR, HIGH_FLAME if n % 7 == 0 else (HIGH_BRICKS if n % 7 == 3 else HIGH_WALL))
                else:
                    self.put(x, y, FLOOR)
        for x, y in banners:
            self.put(x, y, FLOOR, HIGH_BANNER[0])
            self.put(x + 1, y, FLOOR, HIGH_BANNER[1])

    def high_wall(self, x0, y0, x1, y1, doors=()):
        """A straight run of high wall, open at the door tiles."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if (x, y) not in doors:
                    self.put(x, y, FLOOR, HIGH_FLAME if (x + y) % 6 == 0 else HIGH_WALL)

    def hall(self, x0, y0, x1, y1, floor, doors=()):
        """A walled hall, walls two thick with lit windows, open at the door tiles."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                edge = min(x - x0, x1 - x, y - y0, y1 - y)
                if edge <= 1:
                    if self.t[y][x]["obj"] != TOWER and (x, y) not in doors:
                        self.put(x, y, FLOOR, WINDOW if edge == 1 and (x + y) % 5 == 0 else (WALL_IN if edge else WALL_OUT))
                else:
                    self.put(x, y, floor)

    def pavilion(self, name, region, x0, y0, cols, rows, count=None):
        """A market pavilion: stalls (region tiles) on every other tile inside a one-tile border,
        2 * cols + 3 tiles wide and 2 * rows + 3 tall, braziers at its corners."""
        x1, y1 = x0 + 2 * cols + 2, y0 + 2 * rows + 2
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                t = self.t[y][x]
                if not self.castle[y][x] or t["obj"] or t["region"] or t["ground"] not in (FLOOR, PATH):
                    sys.exit(f"{self.name}: {name} doesn't fit: {x},{y} is {t['ground']} {t['obj'] or ''}")
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, STALL_EDGE if x in (x0, x1) or y in (y0, y1) else STALL)
        for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            self.put(x, y, STALL_EDGE, FLAME_OBJ, slot=CORNER)
        n = 0
        for j in range(rows):
            for i in range(cols):
                if count is None or n < count:
                    self.put(x0 + 2 + 2 * i, y0 + 2 + 2 * j, STALL, region=region)
                    n += 1

    def accents(self, rng, chance=0.045):
        """Crystals and candles here and there on open courtyard floor."""
        for y in range(3, self.h - 3):
            for x in range(3, self.w - 3):
                if not self.free(x, y, 2):
                    continue
                r = rng.random()
                if r < chance * 2 / 3:
                    self.put(x, y, FLOOR, CRYSTAL, slot=ACCENT)
                elif r < chance:
                    self.put(x, y, FLOOR, CANDLES, slot=ACCENT)

    def free(self, x, y, margin):
        """Open courtyard floor with nothing within `margin` tiles."""
        if not self.castle[y][x] or self.t[y][x]["ground"] != FLOOR or (x, y) in self.paths:
            return False
        for dy in range(-margin, margin + 1):
            for dx in range(-margin, margin + 1):
                t = self.t[y + dy][x + dx]
                if t["obj"] or t["region"] or t["ground"] != FLOOR:
                    return False
        return True

    def mark_open(self):
        for y in range(3, self.h - 3):
            for x in range(3, self.w - 3):
                if self.free(x, y, 1):
                    self.t[y][x]["slot"] = OPEN


# --- the nexus -----------------------------------------------------------------------------------

NEXUS_SIZE = (97, 92)
# The castle's outline, clockwise: tiles whose middle is inside are the castle.
NEXUS_OUTLINE = [(12, 16), (34, 16), (34, 3), (63, 3), (63, 16), (84, 16), (92, 24), (92, 64), (89, 67),
                 (89, 83), (57, 83), (57, 88), (39, 88), (39, 83), (10, 83), (4, 76), (4, 24)]
NEXUS_TOWERS = NEXUS_OUTLINE + [(34, 20), (63, 20), (4, 50), (92, 44), (24, 83), (73, 83)]
CENTRE = (48, 42)                   # the middle of the spawn
PLAZA_RADIUS = 10.5
KEEP_RECT = (34, 3, 62, 20)         # the keep's walls, x0, y0, x1, y1
KEEP_DOOR = (45, 51)                # the great hall's door, x0 and x1
COLONNADES = (34, 62)               # x of the colonnades between the courtyard and the market wards
WARD_Y = (23, 62)
GATE = (46, 50)                     # the gate passage, x0 and x1
# The shared vault chests: their tiles are their Redis keys (items.<x>_<y> in nexusChests).
CHESTS = [(61, 70), (79, 70), (61, 74), (79, 74)]
TREASURY = (57, 66, 83, 78)         # its walls
FORGE_HALL = (11, 64, 33, 81)       # the forge hall's walls, x0, y0, x1, y1
FORGE = (18, 70)                    # the Divine Forge (its sprite stands four tiles tall above it)
ENCHANTER = (26, 70)                # the Enchanter, which imprints gear (ImprintHandlers.cs)
FORGE_HALL_DOOR = (74, 76)          # the doors in its east and west walls, y0 and y1
FORGE_HALL_NORTH = (20, 24)         # the door in its north wall, x0 and x1
# the Hall of Champions, in the east ward south of the street: the monument at its middle, a gold
# court around it, and the plinths of the boards' champions in a ring open to the north, so the
# carpet in from the street stays open; each plinth names its board
# (Shared/champions ChampionBoards, `name:<board id>`)
CHAMPIONS = (77, 53)
CHAMPIONS_RADIUS = 6.8       # the gold inside; a border tile wide round it
CHAMPION_STATUES = [((-3, -5), "fame"), ((3, -5), "stars"), ((6, -1), "achievements"),
                    ((6, 3), "arenaSolo"), ((3, 6), "arenaGroup"), ((0, 6), "guildLiving"),
                    ((-3, 6), "guildDead"), ((-6, 3), "weeklyDungeons"), ((-6, -1), "weeklyFame")]
# where DoomedWorlds.AddNexusPortals puts the Arena portal (keep the two in step)
ARENA_PORTALS = [(45.5, 68.5)]
# (name, region, x0, y0, columns, rows[, stalls]): see Grid.pavilion. One stall per item where a
# shop has few, so each stall shows something different; the dyes and cloths rotate through theirs.
PAVILIONS = [
    # the west ward
    ("Dungeon Keys", "Store 11", 8, 23, 11, 4),
    ("Pet Generators", "Store 5", 19, 34, 5, 2),
    ("Accessories", "Store 9", 7, 46, 5, 2),        # small cloths and accessory dyes
    ("Stat Potions", "Store 1", 22, 46, 4, 2),
    ("Clothing", "Store 8", 7, 55, 5, 2),           # large cloths and clothing dyes
    ("Specials", "Store 3", 22, 55, 4, 2),
    # the east ward, north of the street (the Hall of Champions is south of it)
    ("Elder and Doomed Keys", "Store 12", 65, 24, 10, 2, 19),   # the old map's 20, less the Christmas Key
    ("Consumables", "Store 2", 66, 32, 9, 2),
]


def build_nexus():
    g = Grid("Nexus", *NEXUS_SIZE)
    rng = random.Random(20261016)
    cx, cy = CENTRE
    g.castle_walls(NEXUS_OUTLINE, NEXUS_TOWERS)

    # streets: from the keep's door to the gate, across the courtyard, along the south yard
    g.path(cx - 1, KEEP_RECT[3] + 1, cx + 1, 85)
    g.path(6, cy - 1, 89, cy + 1)
    g.path(35, 64, 61, 65)

    # the keep: the great hall, its carpet from the door to the statue, the realm portals
    kx0, ky0, kx1, ky1 = KEEP_RECT
    g.hall(kx0, ky0, kx1, ky1, HALL_FLOOR, doors={(x, y) for x in range(KEEP_DOOR[0], KEEP_DOOR[1] + 1) for y in (ky1 - 1, ky1)})
    g.path(KEEP_DOOR[0], ky1 - 1, KEEP_DOOR[1], ky1)
    g.rect(cx - 2, ky0 + 2, cx + 2, ky1 - 2, CARPET)
    g.put(cx, ky0 + 3, CARPET, STATUE)
    for x in (cx - 2, cx + 2):
        g.brazier(x, ky0 + 3, CARPET)
    for y in range(ky0 + 6, ky1 - 2, 3):     # brick posts along the carpet, flames between
        g.put(cx - 3, y, HALL_FLOOR, WALL_IN)
        g.put(cx + 3, y, HALL_FLOOR, WALL_IN)
    for y in range(ky0 + 4, ky1 - 2, 3):
        g.flame(cx - 3, y, HALL_FLOOR)
        g.flame(cx + 3, y, HALL_FLOOR)
    for y in (ky0 + 5, ky0 + 9, ky0 + 13):    # seven to a row
        for x in (kx0 + 4, kx0 + 6, kx0 + 8, cx, kx1 - 8, kx1 - 6, kx1 - 4):
            g.put(x, y, CARPET if x == cx else HALL_FLOOR, region="Realm Portals")
    for x in (kx0 + 2, kx1 - 2):
        for y in range(ky0 + 3, ky1 - 1, 4):
            g.banner(x, y, HALL_FLOOR)
    for x in (KEEP_DOOR[0] - 1, KEEP_DOOR[1] + 1):
        g.brazier(x, ky1 + 1, FLOOR)

    # the colonnades: brick posts with flames between, open where the street crosses
    for x in COLONNADES:
        for y in range(WARD_Y[0], WARD_Y[1] + 1):
            if cy - 2 <= y <= cy + 2 or g.is_wall(x, y):
                continue
            if y % 3 == 0:
                g.put(x, y, FLOOR, WALL_IN)
            elif y % 6 == 2:
                g.flame(x, y, FLOOR)

    # the plaza around the spawn, ringed with flames and crystals
    for y in range(g.h):
        for x in range(g.w):
            if math.hypot(x - cx, y - cy) <= PLAZA_RADIUS and not g[x, y]["obj"]:
                g.put(x, y, PLAZA)
                g.paths.add((x, y))
    for i in range(16):
        a = 2 * math.pi * (i + 0.5) / 16
        x, y = int(round(cx + math.cos(a) * PLAZA_RADIUS)), int(round(cy + math.sin(a) * PLAZA_RADIUS))
        if i % 2 == 0:
            g.flame(x, y, PLAZA)
        else:
            g.put(x, y, PLAZA, CRYSTAL, slot=ACCENT)
    for y in range(cy - 2, cy + 3):
        for x in range(cx - 2, cx + 3):
            g.put(x, y, RUG, region="Spawn")
    for dx, dy in ((-4, -4), (4, -4), (-4, 4), (4, 4)):
        g.put(cx + dx, cy + dy, PLAZA, FOUNTAIN)
    g.put(cx - 2, cy - 4, PLAZA, "Name Changer")
    g.put(cx + 2, cy - 4, PLAZA, "Character Changer")
    g.put(cx, cy + 4, PLAZA, "Armoire")

    # the vault to the west, the guild's corner to the east
    g.put(COLONNADES[0] + 3, cy, PATH, "Vault Portal")
    g.put(COLONNADES[1] - 3, cy, PATH, "Guild Hall Portal")
    for x in (COLONNADES[0] + 3, COLONNADES[1] - 3):
        g.brazier(x, cy - 3, FLOOR)
        g.brazier(x, cy + 3, FLOOR)
    g.put(COLONNADES[1] - 2, cy + 6, FLOOR, "Guild Register")
    g.put(COLONNADES[1] - 4, cy - 6, FLOOR, "BulletinBoard")
    g.put(COLONNADES[1] - 2, cy - 6, FLOOR, "BulletinBoard")

    # the south yard: the arena portal, the gate
    g.rect(36, 66, 60, 81, PLAZA)
    g.path(cx - 1, 64, cx + 1, 85)
    for x, y in ARENA_PORTALS:
        g.put(int(x), int(y), PLAZA)
        g.paths.add((int(x), int(y)))
        g.banner(int(x), int(y) - 2, PLAZA)
    # the arena leaderboard (its trophy opens the top runs in the Rust client), across the road
    # from the portal
    g.put(2 * cx - int(ARENA_PORTALS[0][0]), int(ARENA_PORTALS[0][1]), PLAZA, "Arena Leaderboard")
    for x in (38, 44, 52, 58):
        g.brazier(x, 72, PLAZA)
    for y in range(67, 81, 3):
        g.put(36, y, PLAZA, CRYSTAL, slot=ACCENT)
        g.put(60, y, PLAZA, CRYSTAL, slot=ACCENT)
    # the gatehouse: solid either side of the passage, a portcullis at its end
    for y in range(83, 88):
        for x in range(40, 57):
            if GATE[0] <= x <= GATE[1]:
                g.put(x, y, PATH, PORTCULLIS if y >= 86 else None)
            elif g[x, y]["obj"] is None:
                g.put(x, y, FLOOR, TOWER)
    for x in (GATE[0] - 1, GATE[1] + 1):
        g.brazier(x, 82, PATH)

    # the forge hall: walled, with doors to the east and west (a carpet between them along the front
    # of the Divine Forge and the Enchanter) and to the north, between the two; the two on gold
    # that brightens towards each, braziers in the corners, flames by the forge, crystals by the
    # Enchanter, banners and candles on the walls. Nothing in it takes a decoration slot, so an
    # event leaves the hall as it is.
    hx0, hy0, hx1, hy1 = FORGE_HALL
    dy0, dy1 = FORGE_HALL_DOOR
    nx0, nx1 = FORGE_HALL_NORTH
    doors = {(x, y) for x in (hx0, hx0 + 1, hx1 - 1, hx1) for y in range(dy0, dy1 + 1)}
    doors |= {(x, y) for x in range(nx0, nx1 + 1) for y in (hy0, hy0 + 1)}
    g.hall(hx0, hy0, hx1, hy1, STONE, doors=doors)

    def gold(x, y):
        d = min(math.hypot(x - ox, y - oy + 1) for ox, oy in (FORGE, ENCHANTER))
        return GOLD_FLOOR[max(0, min(6, int(6.4 - d)))] if d < 5.5 else STONE

    for y in range(hy0 + 2, hy1 - 1):
        for x in range(hx0 + 2, hx1 - 1):
            g.put(x, y, gold(x, y))
    g.rect(hx0, dy0, hx1, dy1, CARPET)
    g.rect(nx0, hy0, nx1, hy0 + 2, CARPET)
    g.path(hx1 + 1, dy0, 35, dy1)
    g.path(hx0 - 4, dy0, hx0 - 1, dy1)
    g.path(nx0, hy0 - 2, nx1, hy0 - 1)
    g.put(*FORGE, gold(*FORGE), "Divine Forge")
    g.put(*ENCHANTER, gold(*ENCHANTER), "Enchanter")
    for x, y in ((hx0 + 2, hy0 + 2), (hx1 - 2, hy0 + 2), (hx0 + 2, hy1 - 2), (hx1 - 2, hy1 - 2)):
        g.put(x, y, STONE, BRAZIER_OBJ)
    fx, fy = FORGE
    ex, ey = ENCHANTER
    for dx in (-2, 2):
        g.put(fx + dx, fy, gold(fx + dx, fy), FLAME_OBJ)
        g.put(ex + dx, ey, gold(ex + dx, ey), CRYSTAL)
    for x in (hx0 + 5, hx1 - 5):
        g.put(x, hy0 + 2, STONE, BANNER_OBJ)
    for x in range(hx0 + 4, hx1 - 2, 3):
        g.put(x, hy1 - 2, STONE, CANDLES)

    # the treasury: walled, its door to the west, the chests where they always were
    hx0, hy0, hx1, hy1 = TREASURY
    for y in range(hy0, hy1 + 1):
        for x in range(hx0, hx1 + 1):
            edge = x in (hx0, hx1) or y in (hy0, hy1)
            if edge and not (x == hx0 and 71 <= y <= 73):
                g.put(x, y, FLOOR, WINDOW if (x + y) % 6 == 0 else WALL_IN)
            else:
                g.put(x, y, STONE)
    for x, y in CHESTS:
        g.put(x, y, STONE, "Vault Chest")
    for (x, y), cls, who in (((61, 68), "Knight", None), ((79, 68), "Archer", "Aurusenth"),
                             ((61, 76), "Rogue", None), ((79, 76), "Wizard", None)):
        g.put(x, y, STONE, f"destnex {cls} Grave", name=who)    # as in the old map's chest room
    g.put(70, 72, STONE, "Arena Large Monument 3", name="size:300")
    for x in range(hx0 + 4, hx1 - 1, 4):
        g.put(x, hy0 + 1, STONE, CANDLES, slot=ACCENT)
        g.put(x, hy1 - 1, STONE, CANDLES, slot=ACCENT)

    # the Hall of Champions: a red carpet from the street, as the keep's leads to its statue, to the
    # Champions Monument in a round court of gold brightening towards it, inside a border like the
    # pavilions'; the plinths round the monument, flames at the court's entrance and at its foot,
    # crystals behind the side plinths, a banner behind the monument and candles round the outside.
    # Nothing in it takes a decoration slot, so an event leaves it as it is.
    hx, hy = CHAMPIONS
    outer = CHAMPIONS_RADIUS + 1
    for y in range(hy - 8, hy + 9):
        for x in range(hx - 8, hx + 9):
            d = math.hypot(x - hx, y - hy)
            if d <= CHAMPIONS_RADIUS:
                g.put(x, y, GOLD_FLOOR[max(0, min(6, int(7.0 - d * 0.95)))])
            elif d <= outer:
                g.put(x, y, STALL_EDGE)
            else:
                continue
            g.paths.add((x, y))
    for y in range(cy + 2, hy + 1):
        for x in range(hx - 1, hx + 2):
            if y > cy + 1 and (y < hy - CHAMPIONS_RADIUS or math.hypot(x - hx, y - hy) <= outer):
                g.put(x, y, CARPET)
                g.paths.add((x, y))
    g.put(hx, hy, CARPET, "Champions Monument")
    for (dx, dy), board in CHAMPION_STATUES:
        g.put(hx + dx, hy + dy, None, "Champion Statue", name=f"name:{board}")
    for dx, dy in ((-3, -7), (3, -7), (-3, 7), (3, 7)):
        g.put(hx + dx, hy + dy, STALL_EDGE, FLAME_OBJ)
    for dx in (-8, 8):
        g.put(hx + dx, hy, FLOOR, CRYSTAL)
    g.put(hx, hy + 7, STALL_EDGE, BANNER_OBJ)
    for dx, dy in ((-6, -6), (6, -6), (-6, 6), (6, 6)):
        g.put(hx + dx, hy + dy, FLOOR, CANDLES)

    for p in PAVILIONS:
        g.pavilion(*p)
    g.accents(rng)
    g.mark_open()
    return g


def check_nexus(g):
    for x, y in CHESTS:
        assert g[x, y]["obj"] == "Vault Chest", (x, y)
    for x, y in ARENA_PORTALS:
        t = g[int(x), int(y)]
        assert not t["obj"] and t["ground"], (x, y)
    assert g[FORGE]["obj"] == "Divine Forge"
    assert g[CHAMPIONS]["obj"] == "Champions Monument"
    for (dx, dy), board in CHAMPION_STATUES:
        t = g[CHAMPIONS[0] + dx, CHAMPIONS[1] + dy]
        assert t["obj"] == "Champion Statue" and t["name"] == f"name:{board}", (dx, dy)
        assert dy > 0 or (abs(dx) != 1 and dx != 0), "the road to the monument stays open"
    assert g[ENCHANTER]["obj"] == "Enchanter"


# --- the vault -----------------------------------------------------------------------------------

VAULT_SIZE = (46, 46)
# a cellar under the castle: its outline, clockwise
VAULT_OUTLINE = [(5, 2), (41, 2), (44, 5), (44, 28), (38, 34), (32, 34), (32, 44), (14, 44), (14, 34),
                 (8, 34), (2, 28), (2, 5)]
VAULT_BANNERS = [(12, 2), (32, 2), (17, 43), (27, 43)]
VAULT_SPAWN = (23, 39)
VAULT_CHESTS = 60                   # the old map's: as many as any account can have
GIFT_CHESTS = 26


def build_vault():
    g = Grid("Vault", *VAULT_SIZE)
    rng = random.Random(20261017)
    sx, sy = VAULT_SPAWN
    g.underground_walls(VAULT_OUTLINE, VAULT_BANNERS)

    # the chest hall: four rows of fifteen chests either side of a carpet
    g.floor(3, 3, 43, 20, HALL_FLOOR)
    g.rect(sx - 1, 3, sx + 1, 38, CARPET)
    g.paths.update((x, y) for x in range(sx - 1, sx + 2) for y in range(3, 45))
    n = 0
    for y in (6, 9, 12, 15):
        for x in list(range(7, 20, 2)) + list(range(27, 40, 2)) + [sx]:
            if n < VAULT_CHESTS and g[x, y]["region"] is None:
                g.put(x, y, CARPET if x == sx else HALL_FLOOR, region="Vault")
                n += 1
    assert n == VAULT_CHESTS, n
    for x in (sx - 2, sx + 2):
        for y in range(5, 19, 4):
            g.flame(x, y, HALL_FLOOR)
    g.banner(sx, 3, CARPET)
    for x in (4, 42):
        for y in (6, 12, 18):
            g.brazier(x, y, HALL_FLOOR)
    for x in (5, 41):
        for y in (9, 15):
            g.put(x, y, HALL_FLOOR, WALL_IN)          # brick posts along the sides

    # the gift chests, in a gallery below the hall behind a wall with three doors
    g.high_wall(3, 21, 43, 21, doors={(x, 21) for x in (8, 9, 10, sx - 1, sx, sx + 1, 36, 37, 38)})
    g.floor(3, 22, 43, 31, STONE)
    n = 0
    for y in (24, 27):
        for x in list(range(5, 18, 2)) + list(range(29, 42, 2)):
            if n < GIFT_CHESTS:
                g.put(x, y, STONE, region="Gifting Chest")
                n += 1
    assert n == GIFT_CHESTS, n
    for x in (sx - 3, sx + 3):
        g.brazier(x, 25, STONE)

    # the court by the stairs up: the spawn, the NPCs and the portals
    for y in range(sy - 1, sy + 2):
        for x in range(sx - 1, sx + 2):
            g.put(x, y, RUG)
    g.put(sx, sy, RUG, region="Spawn")
    g.put(sx - 7, 32, STONE, "Potion Storage")
    g.put(sx + 7, 32, STONE, "Armoire")
    g.put(sx - 12, 26, STONE, "Guild Chronicle")
    g.put(sx + 12, 26, STONE, "Guild Hall Portal")
    g.put(sx - 4, 41, FLOOR, "Daily Quest Portal")
    g.put(sx, 42, CARPET, "Glowing Portal of Cowardice")     # back up to the nexus
    for x in (sx - 2, sx + 2):
        g.brazier(x, 42, FLOOR)
    for x in (sx - 3, sx + 3):
        g.banner(x, 36, FLOOR)
    g.accents(rng, 0.08)
    # the Training Dummy beside the spawn, in reach of every weapon from it (`DpsMeter`)
    assert not g[sx + 5, sy]["obj"]
    g.put(sx + 5, sy, obj="Training Dummy")
    g.mark_open()
    return g


# --- events --------------------------------------------------------------------------------------

def decorate(g, event, seed):
    """A copy of the map with the event's decorations (see EVENTS)."""
    spec = EVENTS[event]
    rng = random.Random(seed)
    out = Grid(f"{g.name} {event}", g.w, g.h)
    out.t = [[dict(t) for t in row] for row in g.t]
    out.castle, out.paths = g.castle, g.paths
    for row in out.t:
        for t in row:
            choices = spec["slots"].get(t["slot"])
            if choices and t["slot"] != OPEN:
                t["obj"] = rng.choice(choices)
            elif t["slot"] == OPEN and not t["obj"]:
                r = rng.random()
                for obj, chance in spec["scatter"]:
                    if r < chance:
                        t["obj"] = obj
                        break
                    r -= chance
    return out


# --- output --------------------------------------------------------------------------------------

def entry(t):
    e = {}
    if t["ground"]:
        e["ground"] = t["ground"]
    if t["obj"]:
        e["objs"] = [{"id": t["obj"], "name": t["name"]} if t["name"] else {"id": t["obj"]}]
    if t["region"]:
        e["regions"] = [{"id": t["region"]}]
    return e


def save(g, path):
    entries, index, out = [], {}, bytearray()
    for y in range(g.h):
        for x in range(g.w):
            e = entry(g[x, y])
            key = json.dumps(e, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(e)
            i = index[key]
            out += bytes((i >> 8, i & 0xff))
    jm = {"data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii"),
          "dict": entries, "height": g.h, "width": g.w}
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


_game_data = None


def game_data():
    """id -> (sheet, index) for grounds and objects, and the set of ids that exist."""
    global _game_data
    if _game_data is None:
        textures, ids = {}, set()
        for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
            text = open(f, encoding="utf-8", errors="ignore").read()
            for m in re.finditer(r'<(Ground|Object) type="[^"]+" id="([^"]+)">(.*?)</\1>', text, re.S):
                ids.add(m.group(2))
                tex = re.search(r"<File>(.*?)</File>\s*<Index>(.*?)</Index>", m.group(3))
                if tex and re.fullmatch(r"0x[0-9a-fA-F]+", tex.group(2).strip()):
                    textures.setdefault(m.group(2), (tex.group(1), int(tex.group(2), 16)))
        _game_data = textures, ids
    return _game_data


def check(g):
    _, ids = game_data()
    used = {t[k] for row in g.t for t in row for k in ("ground", "obj") if t[k]}
    missing = sorted(used - ids)
    if missing:
        sys.exit(f"{g.name}: not in the game data: " + ", ".join(missing))
    counts = {}
    for row in g.t:
        for t in row:
            if t["region"]:
                counts[t["region"]] = counts.get(t["region"], 0) + 1
                assert not t["obj"], f"{g.name}: an object stands on a region tile"
    return counts


def preview(g, path):
    from PIL import Image
    textures, _ = game_data()
    manifest = json.load(open(os.path.join(ROOT, "client-rs", "assets", "manifest.json"), encoding="utf-8"))
    sheets = {s["name"]: s for s in manifest["image_sets"]}
    cache = {}

    def sprite(name):
        if name not in cache:
            cache[name] = None
            if name in textures and textures[name][0] in sheets:
                sheet, index = textures[name]
                s = sheets[sheet]
                img = Image.open(os.path.join(ROOT, "client", "src", s["file"])).convert("RGBA")
                cols = img.width // s["w"]
                box = ((index % cols) * s["w"], (index // cols) * s["h"])
                cache[name] = img.crop(box + (box[0] + s["w"], box[1] + s["h"])).resize((8, 8), Image.NEAREST)
        return cache[name]

    out = Image.new("RGBA", (g.w * 8, g.h * 8), (12, 9, 21, 255))
    for y in range(g.h):
        for x in range(g.w):
            t = g[x, y]
            if t["ground"] and sprite(t["ground"]):
                out.paste(sprite(t["ground"]), (x * 8, y * 8))
            if t["obj"] and sprite(t["obj"]):
                out.alpha_composite(sprite(t["obj"]), (x * 8, y * 8))
            if t["region"] and t["region"] != "Spawn":
                out.alpha_composite(Image.new("RGBA", (8, 8), (255, 122, 61, 110)), (x * 8, y * 8))
    out.resize((g.w * 16, g.h * 16), Image.NEAREST).save(path)


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    nexus, vault = build_nexus(), build_vault()
    check_nexus(nexus)
    maps = [nexus, vault]
    for event in EVENTS:
        maps += [decorate(nexus, event, 1), decorate(vault, event, 2)]
    for g in maps:
        counts = check(g)
        print(f"{g.name}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        path = os.path.join(WORLDS, g.name.replace("Nexus", "Nexus Castle", 1).replace("Vault", "Vault Castle", 1) + ".jm")
        if folder:
            preview(g, os.path.join(folder, os.path.basename(path)[:-3] + ".png"))
        if "--dry-run" not in sys.argv:
            save(g, path)
            print("  written", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()
