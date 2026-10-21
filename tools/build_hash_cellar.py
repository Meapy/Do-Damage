"""Builds the Hash Cellar, the Oryx 4 dungeon of the Oryx chain, in the red of fire and doom.

    python tools/build_hash_cellar.py [--preview <folder>]

Oryx's Sanctuary (tools/build_oryx_sanctuary.py) built from the same pieces, burnt: every tile,
wall, banner and torch it uses gets an "O4" copy recoloured from deep crimson to ember orange.
From south to north:

- the vestibule, where players arrive (Spawn region) with a Nexus Portal to leave by;
- the Burning Path, a corridor that winds through four chambers, held by Oryx's Infernal Court:
  O3's court monsters with Oryx 2's minions' tricks, a little stronger (BehaviorDb.HashCellar.cs);
- the Pyre, a round arena a little larger than the old one (his rings and guardians land 9 tiles
  out), with Oryx the Mad God 4 at its centre and a couple of lava pools on the floor.

Oryx the Mad God 4 is redrawn too: his Doomed Realms sprite with burning horns and embers in his
armour (`hashCellarOryx16x16`; port_doomed.py's TEXTURE_OVERRIDES points him at it).

Outputs (generated, don't edit):
- the map, source/Shared/resources/worlds/Dungeons/Hash Cellar/Hash Cellar.jm;
- HashCellarGrounds.xml and HashCellarObjects.xml, in source/Shared/resources/xml/custom and the
  client's custom/xmls;
- the sheets HashCellar8x8.png, HashCellar16x16.png, HashCellar32x32.png, HashCellarChars16x16.png
  and HashCellarOryx16x16.png in the client's custom/images (registered for the Rust client by
  tools/gen_rs_manifest.py).
Type codes are kept stable in tools/hash_cellar/types.json.
"""
import base64
import copy
import json
import math
import os
import sys
import xml.etree.ElementTree as ET
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_elder import read_png, write_png  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
PROD_XML = os.path.join(XML_DIR, "prod")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")
COURT_CHARS = os.path.join(CLIENT, "prod", "objects", "oryxSanctuaryChars16x16.png")
ORYX_SPRITE = os.path.join(CLIENT, "doomed", "images", "drRemote_draw_935820711.png")
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Hash Cellar", "Hash Cellar.jm")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "HashCellarGrounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "HashCellarGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "HashCellarObjects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "HashCellarObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "hash_cellar", "types.json")
GROUND_TYPES = (0x5900, 0x5980)
OBJECT_TYPES = (0x5980, 0x5B00)
# the new sheets: name -> (file, cell size)
SHEETS = {
    8: ("hashCellar8x8", "HashCellar8x8.png"),
    16: ("hashCellar16x16", "HashCellar16x16.png"),
    32: ("hashCellar32x32", "HashCellar32x32.png"),
}
CHARS = ("hashCellarChars16x16", "HashCellarChars16x16.png")
ORYX = ("hashCellarOryx16x16", "HashCellarOryx16x16.png")

# ------------------------------------------------------------------------------------------------
# colours: brightness picks a colour along a ramp from soot to flame

FLOOR_RAMP = [(0.0, (10, 2, 2)), (0.25, (54, 10, 8)), (0.5, (106, 22, 14)), (0.75, (166, 48, 22)), (1.0, (226, 116, 52))]
WALL_RAMP = [(0.0, (14, 4, 4)), (0.3, (66, 13, 10)), (0.6, (146, 34, 17)), (0.85, (226, 96, 34)), (1.0, (255, 204, 120))]
CHAR_RAMP = [(0.0, (16, 3, 3)), (0.3, (80, 14, 10)), (0.55, (170, 40, 18)), (0.8, (240, 120, 36)), (1.0, (255, 230, 150))]


def ramp(stops, v):
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if v <= b:
            f = (v - a) / (b - a) if b > a else 0
            return tuple(int(x + (y - x) * f) for x, y in zip(ca, cb))
    return stops[-1][1]


def luma(r, g, b):
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255


def burn(p, stops, keep=0.0):
    """A pixel recoloured along the ramp; `keep` of its own colour shows through."""
    r, g, b, a = p
    if a == 0:
        return p
    # the sanctuary's art is dark: stretch it a little so the ramp's flame end is reached
    v = min(1.0, luma(r, g, b) * 1.25)
    c = ramp(stops, v)
    if keep:
        c = tuple(int(x * (1 - keep) + y * keep) for x, y in zip(c, (r, g, b)))
    return c + (a,)


def burn_hex(text):
    """A ground's minimap colour (0xRRGGBB), burnt like its tile."""
    v = int(text, 16)
    r, g, b, _ = burn(((v >> 16) & 255, (v >> 8) & 255, v & 255, 255), FLOOR_RAMP)
    return "0x%02X%02X%02X" % (r, g, b)


# ------------------------------------------------------------------------------------------------
# sprite sheets

class Sheet:
    """A new sheet of cells copied from the sanctuary's sheets, recoloured."""

    def __init__(self, cell):
        self.cell, self.cells, self.index = cell, [], {}

    def add(self, src, index, stops):
        key = (src, index, id(stops))
        if key not in self.index:
            self.index[key] = len(self.cells)
            self.cells.append((src, index, stops))
        return self.index[key]

    def write(self, path, sources):
        n = self.cell
        per_row = 16
        rows = max(1, (len(self.cells) + per_row - 1) // per_row)
        w, h = per_row * n, rows * n
        pix = [[(0, 0, 0, 0)] * w for _ in range(h)]
        for k, (src, index, stops) in enumerate(self.cells):
            sw, _, px = sources[src]
            sx, sy = (index % (sw // n)) * n, (index // (sw // n)) * n
            dx, dy = (k % per_row) * n, (k // per_row) * n
            for y in range(n):
                for x in range(n):
                    pix[dy + y][dx + x] = burn(px(sx + x, sy + y), stops)
        write_png(path, w, h, pix)


def source_sheets():
    """{sheet name: (png path, cell size)} for the square sheets the clients register."""
    out = {}
    for e in json.load(open(MANIFEST, encoding="utf-8"))["image_sets"]:
        if e["file"] and e["w"] == e["h"] and e["w"] in SHEETS and not e["name"].startswith("hashCellar"):
            out[e["name"]] = (os.path.join(ROOT, "client", "src", e["file"]), e["w"])
    return out


SOURCE_SHEETS = source_sheets()


def retexture(element, sheets, stops):
    """Points every texture of the element at a burnt copy on the new sheets."""
    for node in element.iter():
        f, i = node.find("File"), node.find("Index")
        if f is None or i is None or f.text not in SOURCE_SHEETS:
            continue
        size = SOURCE_SHEETS[f.text][1]
        i.text = "0x%02x" % sheets[size].add(f.text, int(i.text, 0), stops)
        f.text = SHEETS[size][0]


def draw_oryx(path):
    """Oryx 4: his Doomed Realms strip with burning horns, embers in his armour and hotter fire."""
    w, h, px = read_png(ORYX_SPRITE)
    src = [[px(x, y) for x in range(w)] for y in range(h)]
    out = [row[:] for row in src]
    for y in range(h):
        for x in range(w):
            r, g, b, a = src[y][x]
            if a == 0:
                continue
            l, sat = luma(r, g, b), max(r, g, b) - min(r, g, b)
            if sat < 40 and l > 0.45:
                # grey horns and blade: glowing from deep red at the root to white-hot at the tip
                out[y][x] = ramp([(0.45, (190, 34, 16)), (0.75, (255, 120, 32)), (1.0, (255, 226, 140))], l) + (a,)
            elif sat < 40:
                # the black armour turns to charred crimson, with ember cracks in it
                c = (min(255, int(r * 1.25 + 30)), int(g * 0.55), int(b * 0.5))
                if 0.08 < l < 0.32 and (x * 7 + y * 3) % 11 == 0:
                    c = (214, 70, 22)
                out[y][x] = c + (a,)
            elif r > 150 and g < 90 and b < 90:
                # his red eyes and the reds of the fire burn hotter
                out[y][x] = (255, min(255, g + 70), min(255, b + 20), a)
            else:
                out[y][x] = (min(255, r + 10), min(255, g + 20), b, a)
    # a flicker of flame above each horn tip
    cell = 16
    for y in range(1, h):
        for x in range(w):
            if src[y][x][3] and src[y - 1][x][3] == 0:
                r, g, b, _ = src[y][x]
                if max(r, g, b) - min(r, g, b) < 40 and luma(r, g, b) > 0.45 and y % cell < 6:
                    out[y - 1][x] = (255, 150, 40, 255)
                    if y >= 2 and src[y - 2][x][3] == 0 and (x // cell + x) % 2 == 0:
                        out[y - 2][x] = (255, 214, 110, 200)
    write_png(path, w, h, out)


# ------------------------------------------------------------------------------------------------
# XML

class Types:
    """Type codes kept stable between runs."""

    def __init__(self):
        self.types = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, key, span):
        if key not in self.types:
            used = {int(v, 16) for v in self.types.values()}
            self.types[key] = "0x%04X" % next(t for t in range(*span) if t not in used)
        return self.types[key]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w") as f:
            json.dump(self.types, f, indent=1, sort_keys=True)
            f.write("\n")


def load(tag, *files):
    out = {}
    for name in files:
        for e in ET.parse(os.path.join(PROD_XML, name)).getroot().iter(tag):
            out.setdefault(e.get("id"), e)
    return out


def write_xml(paths, root_tag, elements, comment):
    root = ET.Element(root_tag)
    for e in elements:
        root.append(e)
    ET.indent(root, "\t")
    text = "<!-- %s -->\n%s\n" % (comment, ET.tostring(root, encoding="unicode"))
    for p in paths:
        with open(p, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(text)


def o4(name):
    """The burnt copy's id: "O3 Wall 1" -> "O4 Wall 1", "O3C Torch Wall Flame" -> "O4C ..."."""
    return "O4" + name[2:]


# The Infernal Court: an O3 court monster's sprite (burnt) and attacks, plus an O2 minion's
# attacks after them (from projectile 10), every shot a sixth stronger.
# name -> (O3 base, (O2 minion, its projectiles) or None, display name)
COURT = {
    "Infernal Officer": ("Oryx Officer", ("Henchman of Oryx", (0, 1))),
    "Brimstone Major": ("Oryx Major", ("Abomination of Oryx", (0, 1, 2, 3, 4))),
    "Cinder Cleric": ("Oryx Cleric", None),
    "Doom Cardinal": ("Oryx Cardinal", ("Henchman of Oryx", (0,))),
}
COURT_DAMAGE = 7 / 6


def court(types, chars, sanctuary, cellar):
    out = []
    for row, (name, (base, extra)) in enumerate(COURT.items()):
        e = copy.deepcopy(sanctuary[base])
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        group = e.find("Group")
        if group is not None:
            e.remove(group)
        tex = e.find("AnimatedTexture")
        chars.append(int(tex.findtext("Index"), 0))
        tex.find("File").text = CHARS[0]
        tex.find("Index").text = str(row)
        if extra:
            minion, ids = extra
            for k, pid in enumerate(ids):
                p = copy.deepcopy(next(q for q in cellar[minion].iter("Projectile") if q.get("id") == str(pid)))
                p.set("id", str(10 + k))
                e.append(p)
        for d in e.iter("Damage"):
            d.text = str(int(round(int(d.text) * COURT_DAMAGE)))
        exp = e.find("Exp")
        if exp is not None:
            exp.text = "2000"
        out.append(e)
    return out


# Burnt copies of other objects: new id -> (original, ramp, projectile renames). Oryx 4's blue
# Guardian Elements turn to red Infernal Elements with red flames (their own behaviours, in
# BehaviorDb.HashCellar.cs), and the white bolts and green stars of his first phase to hellfire
# (port_doomed.PROJECTILE_OVERRIDES points his projectiles at them).
RED_RAMP = [(0.0, (24, 2, 2)), (0.3, (110, 8, 8)), (0.6, (200, 24, 16)), (0.85, (255, 64, 32)), (1.0, (255, 120, 64))]
FIRE_RAMP = [(0.0, (40, 4, 2)), (0.35, (170, 30, 10)), (0.7, (255, 110, 20)), (1.0, (255, 214, 100))]
BURNT_OBJECTS = {
    "Infernal Element": ("Guardian Element 1", RED_RAMP, {"White Flame": "Infernal Flame", "White Bullet": "Infernal Flame"}),
    "Infernal Outer Element": ("Outer Guardian Element", RED_RAMP, {"White Flame": "Infernal Flame"}),
    "Infernal Flame": ("White Flame", RED_RAMP, {}),
    "Hellfire Bolt": ("White Bolt", FIRE_RAMP, {}),
    "Hellfire Star": ("Green Star", FIRE_RAMP, {}),
}
# the molten dark beyond the walls: lava's flow, burnt dark, no damage, nobody walks on it
MOLTEN = "O4 Molten Abyss"
MOLTEN_RAMP = [(0.0, (10, 2, 2)), (0.4, (44, 8, 6)), (0.7, (96, 18, 10)), (1.0, (168, 52, 18))]


def burnt_objects(types, sheets, everything):
    out = []
    for name, (orig, stops, renames) in BURNT_OBJECTS.items():
        e = copy.deepcopy(everything[orig])
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        for tag in ("DisplayId", "Group"):
            for n in e.findall(tag):
                e.remove(n)
        for oid in e.iter("ObjectId"):
            oid.text = renames.get(oid.text, oid.text)
        retexture(e, sheets, stops)
        out.append(e)
    return out


def molten(types, sheets, lava):
    e = copy.deepcopy(lava)
    e.set("type", types.get(MOLTEN, GROUND_TYPES))
    e.set("id", MOLTEN)
    for tag in ("MinDamage", "MaxDamage", "Sink", "DisplayId", "Speed"):
        for n in e.findall(tag):
            e.remove(n)
    ET.SubElement(e, "NoWalk")
    retexture(e, sheets, MOLTEN_RAMP)
    return e


# ------------------------------------------------------------------------------------------------
# the map

W, H = 64, 122
CX = 32
PYRE_C = (CX, 22)           # the boss room
PYRE_R = 17.5               # the old one was about 15 across its middle
CORRIDOR = 3                # half-width: the path is 7 tiles wide
PATH = [(CX, 38), (CX, 50), (14, 50), (14, 72), (50, 72), (50, 94), (CX, 94), (CX, 108)]
CHAMBERS = [(14, 50), (14, 72), (50, 72), (50, 94)]
CHAMBER_R = 6.5
VESTIBULE = (24, 106, 40, 116)
LAVA = [((19, 17), 1.7), ((45, 27), 1.7), ((10, 76), 1.3), ((54, 90), 1.3)]
# who holds the path, from the vestibule up
GUARDS = [
    ((47, 96), "Infernal Officer"), ((53, 92), "Cinder Cleric"),
    ((CX + 3, 72), "Infernal Officer"),
    ((47, 70), "Brimstone Major"), ((53, 74), "Doom Cardinal"),
    ((11, 70), "Cinder Cleric"), ((17, 74), "Brimstone Major"),
    ((11, 52), "Doom Cardinal"), ((17, 48), "Infernal Officer"),
    ((CX, 46), "Brimstone Major"),
]

NORMAL = o4("O3 Normal Tile")
RUG = o4("O3 Sanctuary Rug Base")
WALL = o4("O3 Wall 1")
ARCH = (o4("O3 Wall Arch 1"), o4("O3 Wall Arch 2"))
COLUMN = o4("O3 Wall Column 1")
TORCH = o4("O3C Torch Wall Flame")
BANNERS = [o4(b) for b in ("O3 G Banner", "O3 D Banner", "O3 O Banner", "O3 L Banner", "O3 B Banner")]
LAVA_TILE = "Lava Blend"


def sdf(x, y):
    """The patterned floor: a 4x4 tile repeating motif."""
    return o4("O3 SDF %d B" % (((y + 1) % 4) * 4 + x % 4))


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def floor(self, x, y, ground):
        self.ground[y][x] = ground

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] not in (None, MOLTEN)

    def place(self, x, y, obj):
        assert self.is_floor(x, y), (obj, x, y)
        assert self.obj[y][x] is None, (obj, x, y, self.obj[y][x])
        self.obj[y][x] = obj


def disc(g, cx, cy, r, ground):
    for y in range(int(cy - r) - 1, int(cy + r) + 2):
        for x in range(int(cx - r) - 1, int(cx + r) + 2):
            if math.hypot(x - cx, y - cy) <= r:
                g.floor(x, y, ground(x, y) if callable(ground) else ground)


def rect(g, x0, y0, x1, y1, ground):
    for y in range(min(y0, y1), max(y0, y1) + 1):
        for x in range(min(x0, x1), max(x0, x1) + 1):
            g.floor(x, y, ground(x, y) if callable(ground) else ground)


def rug(g, a, b):
    """A three-wide rug along one leg of the path."""
    (x0, y0), (x1, y1) = a, b
    if x0 == x1:
        for y in range(min(y0, y1), max(y0, y1) + 1):
            g.floor(x0 - 1, y, o4("O3 Sanctuary Rug D Edge L"))
            g.floor(x0, y, RUG)
            g.floor(x0 + 1, y, o4("O3 Sanctuary Rug D Edge R"))
    else:
        for x in range(min(x0, x1), max(x0, x1) + 1):
            g.floor(x, y0 - 1, o4("O3 Sanctuary Rug D Edge U"))
            g.floor(x, y0, RUG)
            g.floor(x, y0 + 1, o4("O3 Sanctuary Rug D Edge D"))


def emblem(g, cx, cy):
    for i in range(9):
        g.floor(cx - 1 + i % 3, cy - 1 + i // 3, o4("O3 Sanctuary Rug D Emblem %d" % (i + 1)))


def build_map():
    g = Grid()
    ax, ay = PYRE_C

    # the Pyre: patterned floor inside a ring of plain tiles, a rug cross and the crest
    for y in range(H):
        for x in range(W):
            d = math.hypot(x - ax, y - ay)
            if d <= PYRE_R:
                g.floor(x, y, sdf(x, y) if d <= PYRE_R - 1.5 else NORMAL)
    reach = int(PYRE_R) - 1
    rug(g, (ax, ay - reach), (ax, ay + reach))
    rug(g, (ax - reach, ay), (ax + reach, ay))
    emblem(g, ax, ay)

    # the Burning Path: plain corridors, patterned chambers at four of its bends, a rug all along
    for a, b in zip(PATH, PATH[1:]):
        rect(g, min(a[0], b[0]) - CORRIDOR, min(a[1], b[1]) - CORRIDOR,
             max(a[0], b[0]) + CORRIDOR, max(a[1], b[1]) + CORRIDOR, NORMAL)
    for cx, cy in CHAMBERS:
        disc(g, cx, cy, CHAMBER_R, sdf)
    for a, b in zip(PATH, PATH[1:]):
        rug(g, a, b)
    for x, y in PATH[1:-1]:
        if (x, y) in CHAMBERS:
            emblem(g, x, y)
        else:
            rect(g, x - 1, y - 1, x + 1, y + 1, RUG)

    # the vestibule
    vx0, vy0, vx1, vy1 = VESTIBULE
    rect(g, vx0, vy0, vx1, vy1, NORMAL)
    rug(g, (CX, vy0), (CX, vy1 - 2))

    # a couple of lava pools
    for (lx, ly), r in LAVA:
        disc(g, lx, ly, r, LAVA_TILE)

    # walls: two tiles thick around every floor tile, arches on the face; beyond them, molten dark
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
    for y in range(H):
        for x in range(W):
            if g.ground[y][x] is None:
                g.ground[y][x] = MOLTEN

    # --- the Pyre's furnishings ------------------------------------------------------------------
    g.place(ax, ay, "Oryx the Mad God 4")
    # four pillars, outside the rings he raises 9 tiles out
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        g.place(ax + dx * 10, ay + dy * 10, COLUMN)
    for i, banner in enumerate(BANNERS):
        bx = ax - 8 + i * 4
        by = min(y for y in range(H) if g.is_floor(bx, y)) + 1
        g.place(bx, by, banner)
    for k in range(24):
        a = 2 * math.pi * k / 24 + math.pi / 24
        tx, ty = round(ax + math.cos(a) * (PYRE_R - 1)), round(ay + math.sin(a) * (PYRE_R - 1))
        if g.is_floor(tx, ty) and g.obj[ty][tx] is None and g.ground[ty][tx] == NORMAL:
            g.place(tx, ty, TORCH)

    # --- the Burning Path ------------------------------------------------------------------------
    for (x, y), name in GUARDS:
        g.place(x, y, name)
    # torches along both walls of every leg, every 5 tiles
    for a, b in zip(PATH, PATH[1:]):
        (x0, y0), (x1, y1) = a, b
        steps = max(abs(x1 - x0), abs(y1 - y0))
        for s in range(2, steps - 1, 5):
            x = x0 + (x1 - x0) * s // steps
            y = y0 + (y1 - y0) * s // steps
            for side in (-CORRIDOR, CORRIDOR):
                tx, ty = (x + side, y) if x0 == x1 else (x, y + side)
                if (g.is_floor(tx, ty) and g.obj[ty][tx] is None and g.ground[ty][tx] == NORMAL
                        and all(math.hypot(tx - cx, ty - cy) > CHAMBER_R for cx, cy in CHAMBERS)):
                    g.place(tx, ty, TORCH)
    for cx, cy in CHAMBERS:
        for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            x, y = cx + dx * 4, cy + dy * 4
            if g.is_floor(x, y) and g.obj[y][x] is None and g.ground[y][x] != LAVA_TILE:
                g.place(x, y, COLUMN)

    # --- the vestibule ---------------------------------------------------------------------------
    for y in range(vy0 + 2, vy1 - 1):
        for x in range(vx0 + 3, vx1 - 2):
            if abs(x - CX) >= 2:
                g.region[y][x] = "Spawn"
    g.place(CX, vy1 - 1, "Nexus Portal")
    for x in (vx0 + 1, vx1 - 1):
        for y in (vy0 + 1, vy1 - 1):
            g.place(x, y, TORCH)
    return g


def save_map(g):
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
    os.makedirs(os.path.dirname(OUT_MAP), exist_ok=True)
    with open(OUT_MAP, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    return g


def preview(g, path, grounds, objects, sources, sheets):
    """The map drawn at 8 pixels a tile from the new sheets, for checking by eye."""
    def cell_of(desc):
        tex = next((n for n in desc.iter() if n.find("File") is not None and n.find("Index") is not None), None)
        if tex is None:
            return None
        name, idx = tex.findtext("File"), int(tex.findtext("Index"), 0)
        for size, (sname, _) in SHEETS.items():
            if name == sname:
                return size, idx
        return None
    n = 8
    pix = [[(0, 0, 0, 255)] * (W * n) for _ in range(H * n)]
    for y in range(H):
        for x in range(W):
            for name, table, solid in ((g.ground[y][x], grounds, True), (g.obj[y][x], objects, False)):
                if not name:
                    continue
                if name not in table:
                    c = (255, 90, 0, 255) if name == LAVA_TILE else (60, 220, 60, 255)
                    for yy in range(2 if not solid else 0, n - (2 if not solid else 0)):
                        for xx in range(2 if not solid else 0, n - (2 if not solid else 0)):
                            pix[y * n + yy][x * n + xx] = c
                    continue
                c = cell_of(table[name])
                if not c:
                    continue
                size, idx = c
                sw, _, px = sources[size]
                sx, sy = (idx % (sw // size)) * size, (idx // (sw // size)) * size
                for yy in range(n):
                    for xx in range(n):
                        p = px(sx + xx * size // n, sy + yy * size // n)
                        if p[3]:
                            pix[y * n + yy][x * n + xx] = p
    write_png(path, W * n, H * n, pix)


def main():
    types = Types()
    sheets = {size: Sheet(size) for size in SHEETS}
    g = build_map()

    # every O3 tile and furnishing the map uses, burnt
    o3_grounds = load("Ground", "EmbeddedData_oryxSanctuaryGroundCXML.xml")
    o3_objects = load("Object", "EmbeddedData_oryxSanctuaryObjectsCXML.xml", "EmbeddedData_oryxSanctuaryUObjectsCXML.xml")
    used_grounds = sorted({t for row in g.ground for t in row if t and t.startswith("O4") and t != MOLTEN})
    used_objects = sorted({t for row in g.obj for t in row if t and t.startswith("O4")})
    grounds, objects = [], []
    for name in used_grounds:
        e = copy.deepcopy(o3_grounds["O3" + name[2:]])
        e.set("type", types.get(name, GROUND_TYPES))
        e.set("id", name)
        col = e.find("Color")
        if col is not None:
            col.text = burn_hex(col.text)
        retexture(e, sheets, FLOOR_RAMP)
        grounds.append(e)
    for name in used_objects:
        e = copy.deepcopy(o3_objects["O3" + name[2:]])
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        retexture(e, sheets, WALL_RAMP)
        objects.append(e)

    # the Infernal Court
    cellar = load("Object", "EmbeddedData_OryxWineCellarCXML.xml")
    rows = []
    objects += court(types, rows, o3_objects, cellar)
    # the moons and hellfire, and the dark beyond the walls
    everything = load("Object", "EmbeddedData_OryxChamberCXML.xml", "EmbeddedData_ProjectilesCXML.xml")
    objects += burnt_objects(types, sheets, everything)
    grounds.append(molten(types, sheets, load("Ground", "EmbeddedData_GroundCXML.xml")[LAVA_TILE]))

    used = {src for sh in sheets.values() for src, _, _ in sh.cells}
    sources = {k: read_png(SOURCE_SHEETS[k][0]) for k in used}
    sources["oryxSanctuaryChars16x16"] = read_png(COURT_CHARS)
    os.makedirs(IMAGES, exist_ok=True)
    for size, (_, fname) in SHEETS.items():
        sheets[size].write(os.path.join(IMAGES, fname), sources)

    # the court's sprites: one burnt row of the sanctuary's character sheet each
    sw, _, px = sources["oryxSanctuaryChars16x16"]
    pix = [[burn(px(x, row * 16 + y), CHAR_RAMP, keep=0.25) for x in range(sw)] for row in rows for y in range(16)]
    write_png(os.path.join(IMAGES, CHARS[1]), sw, 16 * len(rows), pix)
    draw_oryx(os.path.join(IMAGES, ORYX[1]))

    write_xml(OUT_XML["grounds"], "Grounds", grounds,
              "Generated by tools/build_hash_cellar.py: the Hash Cellar's burnt sanctuary tiles. Do not edit.")
    write_xml(OUT_XML["objects"], "Objects", objects,
              "Generated by tools/build_hash_cellar.py: the Hash Cellar's walls, furnishings and Infernal Court. Do not edit.")
    save_map(g)
    types.save()
    print("wrote %s (%dx%d), %d grounds, %d objects" % (OUT_MAP, W, H, len(grounds), len(objects)))

    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(out, exist_ok=True)
        new = {size: read_png(os.path.join(IMAGES, fname)) for size, (_, fname) in SHEETS.items()}
        preview(g, os.path.join(out, "hash_cellar.png"), {e.get("id"): e for e in grounds},
                {e.get("id"): e for e in objects}, new, sheets)
        print("preview in", out)


if __name__ == "__main__":
    main()
