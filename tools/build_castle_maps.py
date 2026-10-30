"""Builds the vault, a cellar under a castle of the website's night sky, and its event versions;
the guild halls (build_guild_halls.py) are built from the same parts.

    python tools/build_castle_maps.py [--preview <folder>] [--dry-run]

Writes `Exalt/Vault Castle.jm` and, for each event in `EVENTS`, a decorated copy,
`Exalt/Vault Castle <Event>.jm`. The world server plays an event's maps while
`serverSettings.mapEvent` in wServer.json names it (`World.LoadMapFromData`), and the plain ones
otherwise. The Nexus was a castle built here too until 2.3.5; it is now a garden, built by
tools/build_nexus.py.

The maps are drawn here rather than in a map editor, so change a layout below and run it again.
They are built from shared parts (`Grid.castle`, `Grid.pavilion` and the palette), in the look
of the client: dark violet stone, ember light from braziers and lit windows, a dark moat.

An event never moves anything. The layouts mark decoration slots as they go (`ACCENT`, `FLAME`,
`BRAZIER`, `BANNER`, `CORNER`, `CENTRE`), and the open floor away from paths, regions and anything
players use is found afterwards (`OPEN`). An event swaps what stands in the slots and scatters
decorations on the open floor (`decorate`), so regions, portals and NPCs stay where they are.

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

    def crystal_court(self, rng, x0, y0, x1, y1):
        """A court in this rectangle, in the castle's own stone: violet diamonds framed by the
        pavilions' border tiles, two lanes of stone crossing in the middle, amethyst crystals on
        stone plinths in staggered rows either side, candles between and ember braziers at the
        corners. It takes no decoration slot."""
        mx, my = (x0 + x1) // 2, (y0 + y1) // 2
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if not self.castle[y][x] or self.is_wall(x, y) or self.t[y][x]["obj"] or self.t[y][x]["region"]:
                    continue
                lane = y in (my, my + 1) or x in (mx, mx + 1) and abs(y - my) <= 2
                edge = x in (x0, x1) or y in (y0, y1)
                self.put(x, y, STONE if lane else STALL_EDGE if edge else PLAZA)
                self.paths.add((x, y))
                if lane or edge:
                    continue
                if (y - y0) % 3 == 1 and (x - x0 + (y - y0) // 3 * 2) % 4 == 1 and abs(x - mx) > 2:
                    self.put(x, y, STONE, CRYSTAL)
                elif rng.random() < 0.06:
                    self.put(x, y, None, CANDLES)
        for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
            if self.castle[y][x] and not self.is_wall(x, y):
                self.put(x, y, None, FLAME_OBJ)

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
    vault = build_vault()
    maps = [vault]
    for event in EVENTS:
        maps.append(decorate(vault, event, 2))
    for g in maps:
        counts = check(g)
        print(f"{g.name}: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        path = os.path.join(WORLDS, g.name.replace("Vault", "Vault Castle", 1) + ".jm")
        if folder:
            preview(g, os.path.join(folder, os.path.basename(path)[:-3] + ".png"))
        if "--dry-run" not in sys.argv:
            save(g, path)
            print("  written", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()
