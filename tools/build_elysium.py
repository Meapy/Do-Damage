"""Builds Oryx's Elysium, the Oryx 5 dungeon at the end of the Oryx chain (the Doomed Realms "Doomed
Cellar": the world keeps that class, port_doomed_worlds.WORLD_NAMES renames it), in gold, white and
light blue with purple through it.

    python tools/build_elysium.py [--preview <folder>]

A holy island in a turquoise sea, from south to north:

- the Landing, a marble dock where players arrive (Spawn region) with a Nexus Portal to leave by;
- the Crossroads, where the road forks round the island. West, the Oasis: palms round a pool, with
  the Oasis Seraph on its islet. East, the Pyramid: a stepped gold pyramid with the Gilded Sphinx on
  top. Each road has three Trees of Doom on the way, and each guardian drops three more when it
  dies: twelve in all. Oryx's Ascended Host (O3's court monsters, ascended: BehaviorDb.Elysium.cs)
  hold the roads;
- the Courtyard, where the roads meet before the Holy Gates, which open once no Tree of Doom and
  no guardian is left (the Gate Watcher watches);
- the Temple, a round marble arena with Oryx the Ascended God at its centre and his three Ascended
  Wardens circling him: he wakes when they fall.

Oryx 5 is his own object here, "Oryx the Ascended God" (the Doomed Realms "Oryx the Mad God 5"'s
XML, with his shots, sprites and helpers made here; his fight is in BehaviorDb.Elysium.cs). He is
redrawn: white and silver armour with gold horns and trim, light blue eyes, a purple shield and a
gold spear in place of his sword, and below 20% his Apotheosis, gold all over with a light blue
glow (`elysiumOryx16x16`, rows 0 and 1).

Outputs (generated, don't edit):
- the map, source/Shared/resources/worlds/Dungeons/Oryx's Elysium/Oryx's Elysium.jm;
- ElysiumGrounds.xml and ElysiumObjects.xml, in source/Shared/resources/xml/custom and the client's
  custom/xmls;
- the sheets Elysium8x8.png, Elysium16x16.png, Elysium32x32.png, ElysiumChars16x16.png and
  ElysiumOryx16x16.png in the client's custom/images (registered for the Rust client by
  tools/gen_rs_manifest.py).
Type codes are kept stable in tools/elysium/types.json.
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
from build_hash_cellar import load, luma, ramp, write_xml  # noqa: E402
import draw_oryx_gates as gates  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")
ORYX_SPRITE = (os.path.join(CLIENT, "doomed", "images", "chars16x16dEncounters2.png"), 37)
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Oryx's Elysium", "Oryx's Elysium.jm")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "ElysiumGrounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "ElysiumGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "ElysiumObjects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "ElysiumObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "elysium", "types.json")
GROUND_TYPES = (0x5300, 0x5400)
OBJECT_TYPES = (0x5400, 0x5500)
# the new sheets: cell size -> (name, file)
SHEETS = {
    8: ("elysium8x8", "Elysium8x8.png"),
    16: ("elysium16x16", "Elysium16x16.png"),
    32: ("elysium32x32", "Elysium32x32.png"),
}
CHARS = ("elysiumChars16x16", "ElysiumChars16x16.png")
ORYX = ("elysiumOryx16x16", "ElysiumOryx16x16.png")

# ------------------------------------------------------------------------------------------------
# colours: brightness picks a colour along a ramp

MARBLE = [(0.0, (96, 100, 138)), (0.35, (176, 182, 210)), (0.7, (226, 230, 242)), (1.0, (255, 253, 246))]
GILDED = [(0.0, (112, 76, 28)), (0.3, (190, 144, 56)), (0.65, (236, 202, 116)), (1.0, (255, 248, 224))]
STONE = [(0.0, (52, 58, 104)), (0.3, (128, 144, 196)), (0.65, (206, 216, 240)), (1.0, (255, 255, 255))]
GOLD = [(0.0, (74, 46, 10)), (0.35, (180, 128, 30)), (0.7, (242, 198, 72)), (1.0, (255, 246, 196))]
AZURE = [(0.0, (22, 52, 112)), (0.35, (62, 142, 222)), (0.7, (142, 216, 255)), (1.0, (238, 251, 255))]
VIOLET = [(0.0, (34, 14, 70)), (0.35, (98, 48, 172)), (0.7, (172, 112, 240)), (1.0, (238, 216, 255))]
HOLY = [(0.0, (70, 64, 128)), (0.4, (176, 186, 232)), (0.75, (236, 240, 255)), (1.0, (255, 255, 255))]


class Tone:
    """A recolouring: each pixel's brightness, stretched over the range its group of cells covers,
    then squeezed into `window`, picks a colour on `stops`. Pixels `accent` picks (r, g, b -> bool)
    take `accent_stops` instead (the rugs' red stripes turn gold)."""

    def __init__(self, stops, window=(0.0, 1.0), accent=None, accent_stops=None):
        self.stops, self.window = stops, window
        self.accent, self.accent_stops = accent, accent_stops
        self.lo, self.hi = 0.0, 1.0
        self.seen = []

    def fresh(self):
        """The same recolouring, measuring its own cells: one object's art, not a whole group's."""
        return Tone(self.stops, self.window, self.accent, self.accent_stops)

    def measure(self, pixels):
        self.seen += [luma(*p[:3]) for p in pixels if p[3]]

    def settle(self):
        if self.seen:
            self.lo, self.hi = min(self.seen), max(self.seen)

    def __call__(self, p):
        r, g, b, a = p
        if a == 0:
            return p
        v = (luma(r, g, b) - self.lo) / (self.hi - self.lo) if self.hi > self.lo else 1.0
        v = self.window[0] + (self.window[1] - self.window[0]) * min(1.0, max(0.0, v))
        stops = self.accent_stops if self.accent and self.accent(r, g, b) else self.stops
        return ramp(stops, v) + (a,)


def reddish(r, g, b):
    return r > g + 40 and r > b + 10


T_MARBLE = Tone(MARBLE, (0.3, 1.0))
T_GILDED = Tone(GILDED, (0.3, 1.0))
T_RUG = Tone(VIOLET, (0.2, 0.55), accent=reddish, accent_stops=GOLD)
T_STONE = Tone(STONE, (0.25, 1.0))
T_GOLD = Tone(GOLD, (0.15, 1.0))
T_AZURE = Tone(AZURE, (0.2, 1.0))
T_VIOLET = Tone(VIOLET, (0.2, 1.0))
T_HOLY = Tone(HOLY)



# ------------------------------------------------------------------------------------------------
# sprite sheets

def manifest_sets():
    """{sheet name: (png path, cell w, cell h, block w, block h)} for the sheets the clients register:
    image sets (block = cell) and animated characters (block = one character's strip)."""
    m = json.load(open(MANIFEST, encoding="utf-8"))
    out = {}
    for e in m["image_sets"]:
        if e["file"] and not e["name"].startswith("elysium"):
            out[e["name"]] = (os.path.join(ROOT, "client", "src", e["file"]), e["w"], e["h"], e["w"], e["h"])
    for e in m["animated_chars"]:
        if not e["name"].startswith("elysium"):
            out.setdefault(e["name"], (os.path.join(ROOT, "client", "src", e["file"]), e["w"], e["h"],
                                       e["sheet_w"], e["sheet_h"]))
    return out


SOURCE_SETS = manifest_sets()
_pngs = {}


def png(name):
    if name not in _pngs:
        _pngs[name] = read_png(SOURCE_SETS[name][0])
    return _pngs[name]


def block(name, index):
    """The pixels of cell (or character strip) `index` of a registered sheet, row by row."""
    _, _, _, bw, bh = SOURCE_SETS[name]
    w, _, px = png(name)
    per = w // bw
    sx, sy = (index % per) * bw, (index // per) * bh
    return [[px(sx + x, sy + y) for x in range(bw)] for y in range(bh)]


class Sheet:
    """A new sheet of cells copied from registered sheets, recoloured."""

    def __init__(self, cell, per_row=16):
        self.cell, self.per_row, self.cells, self.index = cell, per_row, [], {}

    def add(self, src, index, tone):
        key = (src, index, id(tone))
        if key not in self.index:
            self.index[key] = len(self.cells)
            self.cells.append((src, index, tone))
            tone.measure([p for row in block(src, index) for p in row])
        return self.index[key]

    def add_painted(self, pixels):
        """A cell painted here (the island's tiles): rows of RGBA pixels."""
        self.cells.append((None, pixels, None))
        return len(self.cells) - 1

    def write(self, path, w=None, h=None):
        cw, ch = (w, h) if w else (self.cell, self.cell)
        rows = max(1, (len(self.cells) + self.per_row - 1) // self.per_row)
        pix = [[(0, 0, 0, 0)] * (self.per_row * cw) for _ in range(rows * ch)]
        for k, (src, index, tone) in enumerate(self.cells):
            dx, dy = (k % self.per_row) * cw, (k // self.per_row) * ch
            for y, row in enumerate(index if src is None else block(src, index)):
                for x, p in enumerate(row):
                    pix[dy + y][dx + x] = p if tone is None else tone(p)
        write_png(path, self.per_row * cw, rows * ch, pix)


def retexture(element, sheets, tone):
    """Points every texture of the element at a recoloured copy on the new sheets."""
    for node in element.iter():
        f, i = node.find("File"), node.find("Index")
        if f is None or i is None or f.text not in SOURCE_SETS:
            continue
        size = SOURCE_SETS[f.text][1]
        if size not in sheets or SOURCE_SETS[f.text][2] != size:
            continue
        i.text = "0x%02x" % sheets[size].add(f.text, int(i.text, 0), tone)
        f.text = SHEETS[size][0]


# Oryx 5, ascended: his strip has five colours. Black, the two dark greys and the light grey of his
# horns, sword and shield emblem, and his red eyes.
ORYX_INK = (40, 30, 86)          # black, round his edge: deep purple
ORYX_SHADOW = (104, 112, 170)    # black, inside him: the shadows between his plates
ORYX_STEEL = (152, 170, 216)     # the darker grey: silver-blue plate
ORYX_PLATE = (238, 242, 252)     # the lighter grey: white plate
ORYX_GOLD = (240, 192, 64)
ORYX_GOLD_LIGHT = (255, 238, 150)
ORYX_GOLD_DARK = (168, 112, 26)
ORYX_EYES = (120, 228, 255)
SHIELD = (124, 64, 196)
SHIELD_DARK = (64, 30, 112)
SPEAR_HEAD = (196, 240, 255)


def draw_oryx(path):
    """Oryx the Ascended God: his Doomed Realms strip recoloured, with a gold spear for his sword."""
    src_path, index = ORYX_SPRITE
    w, _, px = read_png(src_path)
    sy = index * 16
    frames = 7
    out = [[(0, 0, 0, 0)] * (16 * frames) for _ in range(16)]
    for f in range(frames):
        cell = [[px(f * 16 + x, sy + y) for x in range(16)] for y in range(16)]
        if not any(p[3] for row in cell for p in row):
            continue
        # how far this frame is shifted down (the walking frame bobs a row)
        top = min(y for y in range(16) for x in range(2, 16) if cell[y][x][3])
        for y in range(16):
            for x in range(16):
                r, g, b, a = cell[y][x]
                if a == 0:
                    continue
                if x <= 1 and y < top + 6 and r > 150:
                    continue                      # the sword's blade: gone, the spear replaces it
                if r > 120 and g < 60:
                    c = ORYX_EYES
                elif r > 150:
                    # the light grey: his horns gold (bright at the tips), the shield's emblem gold
                    c = ORYX_GOLD_LIGHT if y - top <= 1 else ORYX_GOLD
                elif x >= 12 and 6 <= y - top <= 12:
                    c = SHIELD_DARK if r < 10 else SHIELD
                elif r < 10:
                    # gold trim along his shoulders and belt
                    edge = any(not (0 <= x + dx < 16 and 0 <= y + dy < 16) or cell[y + dy][x + dx][3] == 0
                               for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                    if y - top in (6, 11) and 3 <= x <= 11:
                        c = ORYX_GOLD_DARK
                    else:
                        c = ORYX_INK if edge else ORYX_SHADOW
                elif r < 0x16:
                    c = ORYX_STEEL
                else:
                    c = ORYX_PLATE
                out[y][f * 16 + x] = c + (255,)
        # the spear, held upright in his right hand (the left of the sprite): a light blue head with
        # a white point, a purple tassel under it and a gold shaft down past his feet
        # (in the attacking frame its head flashes white)
        x0 = f * 16
        head = top
        tip = (255, 255, 255) if f == 5 else SPEAR_HEAD
        for dy, xs, c in ((0, (1,), (255, 255, 255)), (1, (0, 1, 2), tip), (2, (0, 1, 2), tip),
                          (3, (1,), (110, 200, 245))):
            for dx in xs:
                if head + dy < 16:
                    out[head + dy][x0 + dx] = c + (255,)
        if head + 4 < 16:
            out[head + 4][x0 + 0] = SHIELD + (255,)
            out[head + 4][x0 + 2] = SHIELD + (255,)
        for y in range(head + 4, 16):
            hand = y - top in (7, 8)
            out[y][x0 + 1] = (ORYX_PLATE if hand else (ORYX_GOLD if y % 2 else ORYX_GOLD_DARK)) + (255,)
    write_png(path, 16 * frames, 32, out + apotheosis(out))


# his Apotheosis (below 20%): the same strip, gold where it was white and silver, white-hot horns
# and eyes, a light blue shield and a violet spearhead, glowing light blue round his edge
APOTHEOSIS = {
    ORYX_PLATE: (255, 232, 140), ORYX_STEEL: (232, 176, 52), ORYX_SHADOW: (170, 112, 30),
    ORYX_GOLD: (255, 250, 230), ORYX_GOLD_LIGHT: (255, 255, 255), ORYX_GOLD_DARK: (255, 214, 110),
    ORYX_EYES: (255, 255, 255), SHIELD: (120, 210, 255), SHIELD_DARK: (40, 110, 200),
    SPEAR_HEAD: (214, 170, 255),
}
GLOW = (150, 226, 255, 150)


def apotheosis(strip):
    out = [[(APOTHEOSIS.get(p[:3], p[:3]) + (p[3],)) if p[3] else p for p in row] for row in strip]
    w = len(strip[0])
    for y in range(16):
        for x in range(w):
            if strip[y][x][3]:
                continue
            cx = x % 16
            if any(0 <= cx + dx < 16 and 0 <= y + dy < 16 and strip[y + dy][x + dx][3]
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                out[y][x] = GLOW
    return out


# ------------------------------------------------------------------------------------------------
# the island's own tiles, painted here in place of the game's old flat 8x8 water and sand: the sea
# in three depths, the shallows with bright caustics, wet sand at the waterline, soft dry sand with
# the odd shell, and gilded paving. Each has a few variants; the patterns tile (whole periods of
# waves across the 8 pixels) and the water's scroll as it ripples. Their blend priorities rise
# from the open sea to the paving, so each blends softly into the one below it (the client rounds
# the corners where they meet).

def _hash(x, y, seed):
    h = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xffff) / 65535


def _mix(a, b, f):
    return tuple(int(round(p + (q - p) * f)) for p, q in zip(a, b))


def paint_water(dark, light, crest, seed):
    p1, p2 = _hash(1, 2, seed) * 6.283, _hash(3, 4, seed) * 6.283
    out = []
    for y in range(8):
        row = []
        for x in range(8):
            v = 0.5 + 0.28 * math.sin(2 * math.pi * (x / 8 + 2 * y / 8) + p1) + 0.22 * math.sin(2 * math.pi * (2 * x / 8 - y / 8) + p2)
            v = min(1.0, max(0.0, v))
            c = crest if v > 0.97 else _mix(dark, light, round(v * 4) / 4)
            row.append(c + (255,))
        out.append(row)
    return out


def paint_sand(base, light, dark, seed, shell=None):
    out = []
    for y in range(8):
        row = []
        for x in range(8):
            n = _hash(x, y, seed)
            c = light if n > 0.8 else dark if n < 0.14 else base
            row.append(c + (255,))
        out.append(row)
    if shell:
        sx, sy = 2 + int(_hash(5, 6, seed) * 4), 2 + int(_hash(7, 8, seed) * 4)
        out[sy][sx], out[sy][sx + 1], out[sy + 1][sx] = shell[0] + (255,), shell[0] + (255,), shell[1] + (255,)
    return out


SHELL = ((250, 226, 222), (214, 170, 160))
# id -> (cells, blend priority, minimap colour, extra tags)
ISLAND_GROUNDS = {
    "Elysian Ocean": (lambda: [paint_water((16, 66, 136), (28, 98, 172), (80, 150, 210), s) for s in range(3)],
                      1, "0x1C5AA0", {"NoWalk": None, "Sink": None, "Animate": ("Wave", "0.3")}),
    "Elysian Reef": (lambda: [paint_water((26, 112, 182), (48, 150, 210), (140, 210, 242), s) for s in range(3, 6)],
                     2, "0x2E8AC8", {"NoWalk": None, "Sink": None, "Animate": ("Wave", "0.3")}),
    "Elysian Shallows": (lambda: [paint_water((62, 180, 212), (104, 212, 232), (226, 250, 252), s) for s in range(6, 9)],
                         3, "0x5CC4DC", {"Sink": None, "Speed": "0.8", "Animate": ("Wave", "0.3")}),
    "Elysian Wet Sand": (lambda: [paint_sand((198, 170, 120), (212, 186, 136), (178, 150, 104), s) for s in range(9, 12)],
                         4, "0xC6AA78", {}),
    "Elysian Sand": (lambda: [paint_sand((240, 220, 170), (250, 236, 198), (224, 198, 148), s) for s in range(12, 15)]
                     + [paint_sand((240, 220, 170), (250, 236, 198), (224, 198, 148), 15, SHELL)],
                     5, "0xF0DCAA", {}),
    "Elysian Gilded Sand": (lambda: [paint_sand((242, 200, 104), (255, 226, 144), (214, 166, 72), s) for s in range(16, 19)],
                            6, "0xF2C868", {}),
}


def paint_cloud():
    """A soft cloud, 16x16: overlapping puffs, white on top shading to pale blue beneath, its
    edges half see-through."""
    puffs = [(5, 9.5, 3.6), (9, 7, 4.2), (12.2, 9.6, 3.0), (8.5, 10.5, 3.6)]
    out = []
    for y in range(16):
        row = []
        for x in range(16):
            depth = max((r - math.hypot(x + 0.5 - cx, y + 0.5 - cy)) for cx, cy, r in puffs)
            if depth <= 0:
                row.append((0, 0, 0, 0))
                continue
            shade = min(1.0, max(0.0, (y - 5) / 9))
            c = _mix((255, 255, 255), (216, 232, 250), shade)
            row.append(c + (190 if depth < 1 else 240,))
        out.append(row)
    return out


# Scenery: clouds drifting over the Landing and the sea. Not enemies, so shots pass through them
# and nothing can hurt them (Drift, BehaviorDb.Elysium.cs). name -> size
CLOUDS = {"Elysian Cloud": 230, "Elysian Cloudlet": 150}


def clouds(types, sheet):
    cell = sheet.add_painted(paint_cloud())
    out = []
    for name, size in CLOUDS.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        ET.SubElement(e, "Class").text = "Character"
        tex = ET.SubElement(e, "Texture")
        ET.SubElement(tex, "File").text = SHEETS[16][0]
        ET.SubElement(tex, "Index").text = "0x%02x" % cell
        ET.SubElement(e, "Size").text = str(size)
        ET.SubElement(e, "MaxHitPoints").text = "1000"
        ET.SubElement(e, "NoMiniMap")
        out.append(e)
    return out


def paint_ichor_splat():
    """A splash of golden ichor: a puddle with droplets round it, bright where it pools."""
    blobs = [(8, 9, 4.6), (4.2, 6.2, 1.6), (12.5, 6.5, 1.3), (3.6, 12.2, 1.2), (12.2, 12.6, 1.5), (8.4, 3.2, 1.0)]
    out = []
    for y in range(16):
        row = []
        for x in range(16):
            depth = max(r - math.hypot(x + 0.5 - cx, y + 0.5 - cy) for cx, cy, r in blobs)
            if depth <= 0:
                row.append((0, 0, 0, 0))
                continue
            c = _mix((214, 150, 40), (255, 236, 150), min(1.0, depth / 3.5))
            if _hash(x, y, 31) > 0.93:
                c = (255, 255, 240)
            row.append(c + (235,))
        out.append(row)
    return out


def paint_ichor_sprite():
    """The spirit that rises from it: a little flame of gold, white at its heart, light blue eyes."""
    out = []
    for y in range(16):
        row = []
        for x in range(16):
            dx = x + 0.5 - 8
            # a teardrop: a point at the top, round below
            if y < 2 or y > 14:
                row.append((0, 0, 0, 0))
                continue
            width = 5.2 * math.sin(math.pi * (y - 1.5) / 14) ** 0.8 if y <= 10 else math.sqrt(max(0.0, 25 - (y - 9.5) ** 2 * 1.4))
            if abs(dx) > width:
                row.append((0, 0, 0, 0))
                continue
            heat = 1 - abs(dx) / (width + 0.01)
            c = _mix((226, 140, 40), (255, 232, 140), heat) if heat < 0.7 else _mix((255, 232, 140), (255, 255, 236), (heat - 0.7) / 0.3)
            row.append(c + (255,))
        out.append(row)
    for x, y in ((6, 9), (9, 9), (6, 10), (9, 10)):
        out[y][x] = (90, 190, 255, 255)
    return out


# Oryx 5's splats (BehaviorDb.Elysium.cs): he throws Divine Ichor below 90% health, and each splat
# hatches an Ichor Sprite: few hit points, weak shots that slow. The ichor isn't an enemy (shots
# pass over it). name -> (painter, size, enemy, health)
ICHOR = {
    "Divine Ichor": (paint_ichor_splat, 110, False, 1000),
    "Ichor Sprite": (paint_ichor_sprite, 90, True, 2500),
}


def ichor(types, sheet):
    out = []
    for name, (painter, size, enemy, health) in ICHOR.items():
        shots = [shot(0, "Stilling Mote", 15, 70, 1400, multi=False, effects=[("Slowed", 2)])] if enemy else []
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        ET.SubElement(e, "Class").text = "Character"
        if enemy:
            ET.SubElement(e, "Enemy")
        tex = ET.SubElement(e, "Texture")
        ET.SubElement(tex, "File").text = SHEETS[16][0]
        ET.SubElement(tex, "Index").text = "0x%02x" % sheet.add_painted(painter())
        ET.SubElement(e, "Size").text = str(size)
        ET.SubElement(e, "MaxHitPoints").text = str(health)
        ET.SubElement(e, "Defense").text = "0"
        if enemy:
            ET.SubElement(e, "Exp").text = "200"
        else:
            ET.SubElement(e, "NoMiniMap")
        for p in shots:
            e.append(copy.deepcopy(p))
        out.append(e)
    return out


def island_grounds(types, sheet):
    out = []
    for name, (cells, blend, colour, extra) in ISLAND_GROUNDS.items():
        e = ET.Element("Ground", {"type": types.get(name, GROUND_TYPES), "id": name})
        holder = ET.SubElement(e, "RandomTexture")
        for pixels in cells():
            tex = ET.SubElement(holder, "Texture")
            ET.SubElement(tex, "File").text = SHEETS[8][0]
            ET.SubElement(tex, "Index").text = "0x%02x" % sheet.add_painted(pixels)
        for tag, value in extra.items():
            if tag == "Animate":
                ET.SubElement(e, "Animate", {"dx": value[1], "dy": value[1]}).text = value[0]
            else:
                ET.SubElement(e, tag).text = value
        ET.SubElement(e, "BlendPriority").text = str(blend)
        ET.SubElement(e, "Color").text = colour
        out.append(e)
    return out


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


def o5(name):
    """The ascended copy's id: "O3 Wall 1" -> "O5 Wall 1", "O3C Torch Wall Flame" -> "O5C ..."."""
    return "O5" + name[2:]


def strip(e, *tags):
    for tag in tags:
        for n in e.findall(tag):
            e.remove(n)


def set_field(e, tag, text):
    node = e.find(tag)
    if node is None:
        node = ET.SubElement(e, tag)
    node.text = text


# Divine projectiles: O3's, recoloured. id -> (O3 original, tone)
PROJECTILES = {
    "Sunfire Missile": ("O3 Purple Missile", T_GOLD),
    "Azure Lance": ("O3 Blue Stab", T_HOLY),
    "Halo Wheel": ("O3 Blue Wheel NRot", T_VIOLET),
    "Gilded Stake": ("O3 Red Stake", T_GOLD),
    "Seraph Shockwave": ("O3 Red Big Shockwave", T_AZURE),
    "Radiant Beam": ("O3 Green Beam", T_AZURE),
    "Radiant Stake": ("O3 Green Stake", T_HOLY),
    "Holy Orb": ("O3 Black Magic MRot", T_HOLY),
    "Divine Spike": ("O3 Black Big Spike", T_GOLD),
    "Holy Wave": ("O3 Black Wave", T_AZURE),
    "Celestial Bolt": ("O3 Black Big Bolt", T_VIOLET),
    "Sun Shockwave": ("O3 Red Shockwave", T_GOLD),
    "Divine Spear": ("O3 Black Spear", T_GOLD),
    # Oryx 5's fight: his stars' motes, and the rays of the Pillars of Heaven
    "Stilling Mote": ("Blue Star", T_AZURE),
    "Wounding Mote": ("Blue Star", T_GOLD),
    "Swift Mote": ("Blue Star", T_HOLY),
    "Blighting Mote": ("Blue Star", T_VIOLET),
    "Bleeding Ray": ("O3 Blue Stab", T_GOLD),
    "Blighting Ray": ("O3 Blue Stab", T_VIOLET),
    "Breaking Ray": ("O3 Blue Stab", T_AZURE),
}


def shot(pid, obj, damage, speed, life, size=100, effects=(), amplitude=None, frequency=None,
         boomerang=False, multi=True, cover=False):
    p = ET.Element("Projectile", {"id": str(pid)})
    ET.SubElement(p, "ObjectId").text = obj
    ET.SubElement(p, "Damage").text = str(damage)
    ET.SubElement(p, "Speed").text = str(speed)
    ET.SubElement(p, "LifetimeMS").text = str(life)
    ET.SubElement(p, "Size").text = str(size)
    if amplitude is not None:
        ET.SubElement(p, "Amplitude").text = str(amplitude)
        ET.SubElement(p, "Frequency").text = str(frequency)
    if multi:
        ET.SubElement(p, "MultiHit")
    if boomerang:
        ET.SubElement(p, "Boomerang")
    if cover:
        ET.SubElement(p, "PassesCover")
    for effect, duration in effects:
        ET.SubElement(p, "ConditionEffect", {"duration": str(duration)}).text = effect
    return p


# Oryx's Ascended Host: an O3 court monster's sprite (recoloured) with shots of its own; their
# attacks are in BehaviorDb.Elysium.cs. name -> (O3 base, tone, [projectiles])
HOST = {
    "Ascended Noble": ("Oryx Noble", T_GOLD, [
        shot(0, "Sunfire Missile", 210, 90, 3000, 110, amplitude=0.5, frequency=2),
        shot(1, "Halo Wheel", 160, 30, 2600, effects=[("Slowed", 2)]),
    ]),
    "Ascended Aristocrat": ("Oryx Aristocrat", T_AZURE, [
        shot(0, "Azure Lance", 120, 140, 1300, boomerang=True, effects=[("Dazed", 1.5)]),
        shot(1, "Halo Wheel", 150, 32, 1800, amplitude=2, frequency=1),
    ]),
    "Ascended Sergeant": ("Oryx Sergeant", T_GOLD, [
        shot(0, "Gilded Stake", 220, 55, 1700),
        shot(1, "Gilded Stake", 220, 85, 1700),
        shot(2, "Gilded Stake", 220, 115, 1700, effects=[("Armor Broken", 2)]),
        shot(3, "Seraph Shockwave", 200, 60, 3200, boomerang=True),
    ]),
    "Ascended Officer": ("Oryx Officer", T_HOLY, [
        shot(0, "Holy Orb", 190, 80, 1500, effects=[("Slowed", 2)]),
        shot(1, "Divine Spike", 320, 85, 1500, 120),
        shot(2, "Celestial Bolt", 200, 45, 3000, 140, effects=[("Quiet", 2)]),
    ]),
    "Ascended Major": ("Oryx Major", T_VIOLET, [
        shot(0, "Holy Wave", 110, 90, 1500, cover=True, effects=[("Armor Broken", 3)]),
        shot(1, "Celestial Bolt", 300, 40, 3200, 150),
        shot(2, "Divine Spike", 240, 35, 3000, 120),
    ]),
    "Ascended Deacon": ("Oryx Deacon", T_AZURE, [
        shot(0, "Radiant Beam", 150, 85, 1100, effects=[("Weak", 2)]),
        shot(1, "Radiant Stake", 260, 90, 1800, 120, effects=[("Slowed", 3)]),
    ]),
    "Ascended Cleric": ("Oryx Cleric", T_GOLD, [
        shot(0, "Sun Shockwave", 150, 20, 1000),
        shot(1, "Sun Shockwave", 150, 40, 1000),
        shot(2, "Sun Shockwave", 150, 60, 1000),
        shot(3, "Sun Shockwave", 150, 80, 1000),
        shot(4, "Radiant Beam", 100, 90, 400, 130, multi=False, effects=[("Dazed", 2)]),
    ]),
    "Ascended Cardinal": ("Oryx Cardinal", T_VIOLET, [
        shot(0, "Divine Spear", 120, 100, 2000, boomerang=True, effects=[("Paralyzed", 0.4)]),
        shot(1, "Divine Spear", 240, 130, 1600, multi=False),
    ]),
}
HOST_HEALTH = 17000

# The guardians, for now: sprites borrowed and recoloured, no attacks (BehaviorDb.Elysium.cs).
# name -> (base, its strip: (sheet, index), or None to recolour the base's own texture, tone, size,
# health)
GUARDIANS = {
    "Gilded Sphinx": ("Grand Sphinx", ("chars16x16rEncounters", 4), T_GOLD, 170, 50000),
    "Oasis Seraph": ("Grand Sphinx", ("chars16x16rEncounters", 112), T_AZURE, 170, 50000),
    "Ascended Warden": ("O3 Protector 1", None, T_GOLD, 130, 25000),
}


def characters(types, chars, sheets, everything, tones):
    out = []
    rows = [(name, everything[base], None, tone, None, HOST_HEALTH) for name, (base, tone, _) in HOST.items()]
    rows += [(name, everything[base], strip_, tone, size, hp) for name, (base, strip_, tone, size, hp) in GUARDIANS.items()]
    for name, base, strip_, tone, size, hp in rows:
        e = copy.deepcopy(base)
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        strip(e, "Group", "DisplayId", "Projectile", "Quest", "Hero", "Encounter", "PerRealmMax", "TrackLoot",
              "HealthBarBoss", "Level", "God")
        tone = tone.fresh()
        tones.append(tone)
        tex = e.find("AnimatedTexture")
        if tex is None:
            retexture(e, sheets, tone)
        else:
            src = strip_ or (tex.findtext("File"), int(tex.findtext("Index"), 0))
            tex.find("File").text = CHARS[0]
            tex.find("Index").text = str(chars.add(src[0], src[1], tone))
        set_field(e, "MaxHitPoints", str(hp))
        set_field(e, "Exp", "2500" if name in HOST else "6000")
        if size:
            set_field(e, "Size", str(size))
        for p in HOST.get(name, (None, None, []))[2]:
            e.append(copy.deepcopy(p))
        out.append(e)
    return out


# Oryx 5 himself: his Doomed Realms XML (sounds, size, quest and Oryx flags) under a name of his own,
# with his two sprites and these shots. The phases that fire them are in BehaviorDb.Elysium.cs.
ORYX_ID = "Oryx the Ascended God"
ORYX_BASE = ("Oryx the Mad God 5", os.path.join(XML_DIR, "custom", "DoomedRealms.xml"))
ORYX_HEALTH = 320000
ORYX_SHOTS = [
    # the Radiant Cross: twin lances along four lines, gold arrowheads along the four between
    shot(0, "Azure Lance", 180, 110, 1600),
    shot(1, "Sunfire Missile", 170, 90, 1800, multi=False),
    # the Divine Hunt: a shotgun in front, an arrowhead of spikes (fast in the middle, slower out to
    # the sides), and a fan behind him that slows, or bleeds once he has transcended
    shot(2, "Divine Spike", 200, 120, 800, 130, multi=False),
    shot(3, "Divine Spike", 200, 95, 950, 120, multi=False),
    shot(4, "Divine Spike", 200, 75, 1100, 110, multi=False),
    shot(5, "Radiant Beam", 110, 70, 1600, effects=[("Slowed", 2)]),
    shot(6, "Holy Wave", 120, 70, 1600, effects=[("Bleeding", 3)]),
    # the Spear of Heaven, thrown in his Last Judgement: slow and large
    shot(7, "Divine Spear", 380, 40, 4200, 220, multi=False, effects=[("Armor Broken", 3)]),
    # the Radiant Cross again in his Last Judgement, slowed right down so it fills the arena
    shot(8, "Azure Lance", 180, 30, 6000),
    shot(9, "Sunfire Missile", 170, 26, 6500, multi=False),
]


def ascended_oryx(types):
    base_id, path = ORYX_BASE
    e = copy.deepcopy(next(o for o in ET.parse(path).getroot() if o.get("id") == base_id))
    e.set("type", types.get(ORYX_ID, OBJECT_TYPES))
    e.set("id", ORYX_ID)
    strip(e, "DisplayId", "Projectile", "AltTexture")
    tex = e.find("AnimatedTexture")
    tex.find("File").text, tex.find("Index").text = ORYX[0], "0"
    alt = ET.Element("AltTexture", {"id": "1"})
    alt_tex = ET.SubElement(alt, "AnimatedTexture")
    ET.SubElement(alt_tex, "File").text = ORYX[0]
    ET.SubElement(alt_tex, "Index").text = "1"
    e.insert(list(e).index(tex) + 1, alt)
    set_field(e, "MaxHitPoints", str(ORYX_HEALTH))
    for p in ORYX_SHOTS:
        e.append(copy.deepcopy(p))
    return e


# The helpers of his fight, all invulnerable (BehaviorDb.Elysium.cs): the Firmament's stars, one
# kind for each mote they shoot (the slowing stars nearest him, the swift or, once he has
# transcended, the blighting ones furthest out), the Halo Orbs of his Apotheosis and the Pillars
# of Heaven of his Last Judgement. name -> (sprite donor, tone, size, shots)
FIGHT = {
    "Stilling Star": ("Magic Star", T_AZURE, 150, [shot(0, "Stilling Mote", 90, 55, 2000, multi=False, effects=[("Slowed", 2)])]),
    "Wounding Star": ("Magic Star", T_GOLD, 150, [shot(0, "Wounding Mote", 110, 55, 2000, multi=False, effects=[("Bleeding", 2)])]),
    "Swift Star": ("Magic Star", T_HOLY, 150, [shot(0, "Swift Mote", 120, 150, 900, multi=False)]),
    "Blighting Star": ("Magic Star", T_VIOLET, 150, [shot(0, "Blighting Mote", 110, 70, 1700, multi=False, effects=[("Sick", 3)])]),
    "Halo Orb": ("White Moon", T_AZURE, 110, [shot(0, "Holy Orb", 150, 55, 2400, effects=[("Slowed", 1.5)])]),
    "Pillar of Heaven": ("Ancient Ruins Pillars", T_HOLY, 120, [
        shot(0, "Bleeding Ray", 170, 80, 4400, multi=False, effects=[("Bleeding", 3)]),
        shot(1, "Blighting Ray", 170, 80, 4400, multi=False, effects=[("Sick", 3)]),
        shot(2, "Breaking Ray", 170, 80, 4400, multi=False, effects=[("Armor Broken", 3)]),
    ]),
}


def fight_helpers(types, sheets, everything, tones):
    out = []
    for name, (donor, tone, size, shots) in FIGHT.items():
        tone = tone.fresh()
        tones.append(tone)
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        ET.SubElement(e, "Class").text = "Character"
        ET.SubElement(e, "Enemy")
        # the donor's first picture (some keep it inside a RandomTexture)
        art = next(n for n in everything[donor].iter() if n.find("File") is not None and n.find("Index") is not None)
        tex = ET.SubElement(e, "AnimatedTexture" if art.tag == "AnimatedTexture" else "Texture")
        ET.SubElement(tex, "File").text = art.findtext("File")
        ET.SubElement(tex, "Index").text = art.findtext("Index")
        ET.SubElement(e, "Size").text = str(size)
        ET.SubElement(e, "MaxHitPoints").text = "10000"
        ET.SubElement(e, "Defense").text = "0"
        ET.SubElement(e, "StasisImmune")
        ET.SubElement(e, "XpMult").text = "0"
        retexture(e, sheets, tone)
        for p in shots:
            e.append(copy.deepcopy(p))
        out.append(e)
    return out


# Recoloured copies of other objects: new id -> (original, tone).
COPIES = dict(PROJECTILES)
COPIES.update({
    # the holy gates, and the watcher that opens them
    "O5 Holy Gate": ("O3 Wall Gate", T_GOLD),
    "O5 Gate Watcher": ("O3 Gate Deleter 1", T_HOLY),
    # flames: light blue in the temple's walls, purple on stands along the roads
    "O5C Torch Wall Flame": ("O3C Torch Wall Flame", T_AZURE),
    "O5C Violet Flame": ("O3C Torch Wall Flame", T_VIOLET),
})
COPY_FIELDS = {}
# the way in, a marble temple door round a swirl of light (a Doomed Cellar Incantation, shown as
# the Oryx's Elysium Incantation, opens it), and the locked portal Oryx 4 drops, that door still
# and dark: drawn by tools/draw_oryx_gates.py
ELYSIUM_PORTAL = "Oryx's Elysium Portal"
LOCKED = "Locked Oryx's Elysium Portal"


def copies(types, sheets, everything, tones):
    out = []
    for name, (orig, tone) in COPIES.items():
        e = copy.deepcopy(everything[orig])
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        strip(e, "DisplayId", "Group")
        for tag, text in COPY_FIELDS.get(name, {}).items():
            node = e.find(tag)
            if node is None:
                node = ET.Element(tag)
                e.insert(0, node)
            node.text = text
        tone = tone.fresh()
        tones.append(tone)
        retexture(e, sheets, tone)
        out.append(e)
    out.append(gates.gate_object(types.get(ELYSIUM_PORTAL, OBJECT_TYPES), ELYSIUM_PORTAL, gates.ELYSIUM_FRAMES,
                                 "Oryx's Elysium", "Oryx's Elysium"))
    out.append(gates.locked_object(types.get(LOCKED, OBJECT_TYPES), LOCKED, gates.ELYSIUM_LOCKED))
    return out


# ------------------------------------------------------------------------------------------------
# the map

W, H = 96, 136
CX = 48
TEMPLE_C = (CX, 24)         # Oryx's arena
TEMPLE_R = 16.5
GATE_ROWS = (44, 45)        # the Holy Gates, two rows across the temple's corridor
CORRIDOR = 3                # half-width of the corridor and the roads: 7 tiles wide
COURTYARD = (36, 50, 60, 60)
LANDING = (43, 120, 53, 131)
CROSSROADS = ((CX, 106), 6.5)
OASIS = ((20, 80), 13)
PYRAMID = ((76, 80), 12)
# the roads: crossroads -> guardian, guardian -> courtyard
ROADS = {
    "west": [(CX, 106), (30, 106), (20, 96), (20, 80)],
    "east": [(CX, 106), (66, 106), (76, 96), (76, 80)],
    "west north": [(20, 80), (20, 62), (36, 55)],
    "east north": [(76, 80), (76, 62), (60, 55)],
    "south": [(CX, 125), (CX, 106)],
}
# three Trees of Doom on each road to a guardian
TREES = [(36, 103), (27, 101), (17, 95), (60, 109), (69, 101), (79, 95)]
# Oryx's Ascended Host, from the crossroads out
HOST_POSTS = [
    ((44, 100), "Ascended Noble"), ((52, 100), "Ascended Aristocrat"),
    # the west road and the Oasis: his clergy
    ((32, 109), "Ascended Deacon"), ((24, 99), "Ascended Cleric"),
    ((12, 74), "Ascended Cardinal"), ((28, 74), "Ascended Cleric"), ((20, 70), "Ascended Deacon"),
    ((17, 62), "Ascended Noble"), ((25, 59), "Ascended Aristocrat"),
    # the east road and the Pyramid: his officers
    ((64, 103), "Ascended Sergeant"), ((73, 99), "Ascended Officer"),
    ((65, 70), "Ascended Major"), ((87, 70), "Ascended Sergeant"), ((76, 90), "Ascended Officer"),
    ((79, 62), "Ascended Noble"), ((71, 59), "Ascended Aristocrat"),
    # before the gates
    ((41, 53), "Ascended Officer"), ((55, 53), "Ascended Cardinal"), ((48, 58), "Ascended Major"),
]

# where the clouds drift: a few round the Landing, the rest over the open sea
CLOUD_SPOTS = [
    ((39, 117), "Elysian Cloud"), ((57, 121), "Elysian Cloudlet"), ((41, 133), "Elysian Cloudlet"),
    ((56, 132), "Elysian Cloud"),
    ((10, 18), "Elysian Cloud"), ((84, 14), "Elysian Cloud"), ((90, 44), "Elysian Cloudlet"),
    ((6, 52), "Elysian Cloudlet"), ((90, 108), "Elysian Cloud"), ((7, 112), "Elysian Cloud"),
    ((22, 130), "Elysian Cloudlet"), ((74, 130), "Elysian Cloudlet"), ((24, 30), "Elysian Cloudlet"),
    ((74, 34), "Elysian Cloud"), ((48, 92), "Elysian Cloud"), ((4, 86), "Elysian Cloudlet"),
]

MARBLE_TILE = o5("O3 Normal Tile")
RUG = o5("O3 Sanctuary Rug Base")
WALL = o5("O3 Wall 1")
ARCH = (o5("O3 Wall Arch 1"), o5("O3 Wall Arch 2"))
COLUMN = o5("O3 Wall Column 1")
TORCH = "O5C Torch Wall Flame"
FLAME = "O5C Violet Flame"
GATE = "O5 Holy Gate"
BANNERS = ["O3 G Banner", "O3 D Banner", "O3 O Banner", "O3 L Banner", "O3 B Banner"]
# the island: the game's own tiles and props, as they are
SEA = "Elysian Ocean"
REEF = "Elysian Reef"
SURF = "Elysian Shallows"
SAND = "Elysian Sand"
WET_SAND = "Elysian Wet Sand"
GOLD_SAND = "Elysian Gilded Sand"
GRASS = "Light Grass"
POOL = SURF
SANDSTONE = "Sandstone Tile"
GOLD_TILE = "Gold Tile"
APEX = "Ancient Ruins Gold Tile"
PALM = "Palm Tree"
PILLAR = "Ancient Ruins Pillars"
OWN = {GRASS, SANDSTONE, GOLD_TILE, APEX, PALM, PILLAR} | set(BANNERS)


def sdf(x, y):
    """The patterned floor: a 4x4 tile repeating motif."""
    return o5("O3 SDF %d B" % (((y + 1) % 4) * 4 + x % 4))


class Grid:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]
        self.temple = set()     # floor walled in: the temple and its corridor

    def floor(self, x, y, ground):
        if 0 <= x < W and 0 <= y < H:
            self.ground[y][x] = ground

    def is_floor(self, x, y):
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] not in (None, SEA, REEF, SURF)

    def free(self, x, y):
        return self.is_floor(x, y) and self.obj[y][x] is None and self.ground[y][x] != POOL

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


def road(g, points, r, ground, only_over=None):
    """A band `r` tiles either side of the polyline (each leg straight or diagonal)."""
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        steps = max(abs(x1 - x0), abs(y1 - y0))
        for s in range(steps + 1):
            px, py = x0 + (x1 - x0) * s / steps, y0 + (y1 - y0) * s / steps
            for y in range(int(py - r) - 1, int(py + r) + 2):
                for x in range(int(px - r) - 1, int(px + r) + 2):
                    if math.hypot(x - px, y - py) <= r and (only_over is None or (0 <= x < W and 0 <= y < H
                                                                                  and g.ground[y][x] in only_over)):
                        g.floor(x, y, ground)


def rug(g, a, b):
    """A three-wide rug along a straight line."""
    (x0, y0), (x1, y1) = a, b
    if x0 == x1:
        for y in range(min(y0, y1), max(y0, y1) + 1):
            g.floor(x0 - 1, y, o5("O3 Sanctuary Rug D Edge L"))
            g.floor(x0, y, RUG)
            g.floor(x0 + 1, y, o5("O3 Sanctuary Rug D Edge R"))
    else:
        for x in range(min(x0, x1), max(x0, x1) + 1):
            g.floor(x, y0 - 1, o5("O3 Sanctuary Rug D Edge U"))
            g.floor(x, y0, RUG)
            g.floor(x, y0 + 1, o5("O3 Sanctuary Rug D Edge D"))


def emblem(g, cx, cy):
    for i in range(9):
        g.floor(cx - 1 + i % 3, cy - 1 + i // 3, o5("O3 Sanctuary Rug D Emblem %d" % (i + 1)))


def build_map():
    g = Grid()
    for y in range(H):
        for x in range(W):
            g.ground[y][x] = SEA

    # --- the island: sand, under the roads, the oasis, the pyramid and the courtyard -------------
    (cx, cy), cr = CROSSROADS
    disc(g, cx, cy, cr + 1, SAND)
    for pts in ROADS.values():
        road(g, pts, CORRIDOR + 1.5, SAND)
    (ox, oy), orr = OASIS
    disc(g, ox, oy, orr + 1.5, SAND)
    (px, py), pr = PYRAMID
    rect(g, px - pr - 2, py - pr - 2, px + pr + 2, py + pr + 2, SAND)
    x0, y0, x1, y1 = COURTYARD
    rect(g, x0 - 2, y0 - 2, x1 + 2, y1 + 2, SAND)
    lx0, ly0, lx1, ly1 = LANDING
    rect(g, lx0 - 3, ly0 - 4, lx1 + 3, ly1 - 3, SAND)
    # golden paving down the middle of every road
    for pts in ROADS.values():
        road(g, pts, 1.2, GOLD_SAND, only_over={SAND})

    # the Oasis: grass round a pool, the Seraph's islet in the middle
    disc(g, ox, oy, orr - 1, GRASS)
    disc(g, ox, oy, 6.5, POOL)
    disc(g, ox, oy, 2.5, GOLD_SAND)

    # the Pyramid: steps of sandstone with a gold edge every third, gold at the top
    for y in range(py - pr, py + pr + 1):
        for x in range(px - pr, px + pr + 1):
            d = max(abs(x - px), abs(y - py))
            g.floor(x, y, APEX if d <= 2 else (GOLD_TILE if d % 3 == 0 else SANDSTONE))

    # the Crossroads and the Courtyard: marble with a gold heart
    disc(g, cx, cy, cr, MARBLE_TILE)
    disc(g, cx, cy, 2.5, sdf)
    rect(g, x0, y0, x1, y1, MARBLE_TILE)
    rect(g, x0 + 3, y0 + 2, x1 - 3, y1 - 2, sdf)

    # the Landing: a marble dock reaching into the sea
    rect(g, lx0, ly0, lx1, ly1, MARBLE_TILE)
    rug(g, (CX, ly0), (CX, ly1 - 2))

    # --- the Temple: walled in, reached only through the Holy Gates ------------------------------
    tx, ty = TEMPLE_C
    for y in range(H):
        for x in range(W):
            d = math.hypot(x - tx, y - ty)
            if d <= TEMPLE_R:
                g.floor(x, y, sdf(x, y) if d <= TEMPLE_R - 1.5 else MARBLE_TILE)
                g.temple.add((x, y))
    reach = int(TEMPLE_R) - 1
    rug(g, (tx, ty - reach), (tx, y0 - 1))
    rug(g, (tx - reach, ty), (tx + reach, ty))
    emblem(g, tx, ty)
    for y in range(ty + int(TEMPLE_R) - 1, y0):
        for x in range(CX - CORRIDOR, CX + CORRIDOR + 1):
            if abs(x - CX) > 1:
                g.floor(x, y, MARBLE_TILE)
            g.temple.add((x, y))

    # the temple's walls, two thick round its floor; arches on the face
    walls = {}
    for y in range(H):
        for x in range(W):
            if (x, y) in g.temple:
                continue
            near = [(dx, dy) for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2) if (x + dx, y + dy) in g.temple]
            if not near or (y >= y0 and g.is_floor(x, y)):
                continue
            face = any(max(abs(dx), abs(dy)) == 1 for dx, dy in near)
            walls[(x, y)] = ARCH[(x + y) % 2] if face else WALL
    for (x, y), wall in walls.items():
        if g.ground[y][x] in (None, SEA, SURF):
            g.ground[y][x] = MARBLE_TILE
        g.obj[y][x] = wall

    # surf where the sand meets the sea
    for y in range(H):
        for x in range(W):
            if g.ground[y][x] == SEA and any(
                    g.ground[y + dy][x + dx] not in (SEA, SURF) and g.obj[y + dy][x + dx] not in (WALL,) + ARCH
                    for dx in (-1, 0, 1) for dy in (-1, 0, 1) if 0 <= x + dx < W and 0 <= y + dy < H):
                g.ground[y][x] = SURF
    # past the shallows, two tiles of reef before the open sea
    for _ in range(2):
        reef = [(x, y) for y in range(H) for x in range(W) if g.ground[y][x] == SEA and any(
            g.ground[y + dy][x + dx] in (SURF, REEF) for dx in (-1, 0, 1) for dy in (-1, 0, 1)
            if 0 <= x + dx < W and 0 <= y + dy < H)]
        for x, y in reef:
            g.ground[y][x] = REEF
    # and wet sand where the shallows lap the beach
    wet = [(x, y) for y in range(H) for x in range(W) if g.ground[y][x] == SAND and any(
        g.ground[y + dy][x + dx] == SURF for dx in (-1, 0, 1) for dy in (-1, 0, 1)
        if 0 <= x + dx < W and 0 <= y + dy < H)]
    for x, y in wet:
        g.ground[y][x] = WET_SAND

    # --- the Temple's furnishings ----------------------------------------------------------------
    g.place(tx, ty, ORYX_ID)
    for k in range(3):
        a = 2 * math.pi * k / 3 - math.pi / 2
        g.place(round(tx + math.cos(a) * 6), round(ty + math.sin(a) * 6), "Ascended Warden")
    for i, banner in enumerate(BANNERS):
        bx = tx - 8 + i * 4
        by = min(y for y in range(H) if (bx, y) in g.temple) + 1
        g.place(bx, by, banner)
    for k in range(24):
        a = 2 * math.pi * k / 24 + math.pi / 24
        x, y = round(tx + math.cos(a) * (TEMPLE_R - 1)), round(ty + math.sin(a) * (TEMPLE_R - 1))
        if g.free(x, y) and g.ground[y][x] == MARBLE_TILE and abs(x - tx) > 3:
            g.place(x, y, TORCH)
    # the Holy Gates, and their watcher just outside
    for gy in GATE_ROWS:
        for x in range(CX - CORRIDOR, CX + CORRIDOR + 1):
            g.place(x, gy, GATE)
    g.place(CX, y0 + 1, "O5 Gate Watcher")
    for x in (CX - CORRIDOR, CX + CORRIDOR):
        for y in (GATE_ROWS[0] - 3, GATE_ROWS[1] + 3):
            g.place(x, y, TORCH)

    # --- the Courtyard and the Crossroads --------------------------------------------------------
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1), (x0, (y0 + y1) // 2), (x1, (y0 + y1) // 2)):
        g.place(x, y, COLUMN)
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        g.place(round(cx + math.cos(a) * (cr - 1)), round(cy + math.sin(a) * (cr - 1)), FLAME)

    # --- the Oasis -------------------------------------------------------------------------------
    g.place(ox, oy, "Oasis Seraph")
    for k in range(12):
        a = 2 * math.pi * k / 12
        x, y = round(ox + math.cos(a) * 9), round(oy + math.sin(a) * 9)
        if g.free(x, y) and max(abs(x - ox), abs(y - oy)) > 3:
            g.place(x, y, PALM)

    # --- the Pyramid: obelisks at its corners, purple flames up its stairs -----------------------
    g.place(px, py, "Gilded Sphinx")
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        g.place(px + dx * (pr + 1), py + dy * (pr + 1), PILLAR)
    for d in (4, 7, 10):
        for side in (-2, 2):
            g.place(px + side, py + d, FLAME)

    # --- the roads: ruined pillars and flames along their edges, the trees and the host ----------
    for pts in ROADS.values():
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            steps = max(abs(bx - ax), abs(by - ay))
            nx, ny = (by - ay) / steps, -(bx - ax) / steps      # across the road
            norm = math.hypot(nx, ny)
            for s in range(3, steps - 2, 6):
                x, y = ax + (bx - ax) * s / steps, ay + (by - ay) * s / steps
                for side, what in ((1, PILLAR if (s // 6) % 2 else FLAME), (-1, FLAME if (s // 6) % 2 else PILLAR)):
                    tx2 = round(x + side * nx / norm * (CORRIDOR + 1))
                    ty2 = round(y + side * ny / norm * (CORRIDOR + 1))
                    if (g.free(tx2, ty2) and g.ground[ty2][tx2] == SAND
                            and math.hypot(tx2 - ox, ty2 - oy) > orr + 2
                            and max(abs(tx2 - px), abs(ty2 - py)) > pr + 2
                            and math.hypot(tx2 - cx, ty2 - cy) > cr + 1):
                        g.place(tx2, ty2, what)
    for x, y in TREES:
        g.place(x, y, "Tree of Doom")
    for (x, y), name in HOST_POSTS:
        g.place(x, y, name)

    # --- the Landing -----------------------------------------------------------------------------
    for y in range(ly0 + 2, ly1 - 1):
        for x in range(lx0 + 1, lx1):
            if abs(x - CX) >= 2:
                g.region[y][x] = "Spawn"
    g.place(CX, ly1 - 1, "Nexus Portal")

    # --- clouds over the Landing and the sea ---------------------------------------------------
    for (x, y), name in CLOUD_SPOTS:
        if g.obj[y][x] is None and (g.ground[y][x] in (SEA, REEF) or g.free(x, y)):
            g.obj[y][x] = name
    for x in (lx0, lx1):
        for y in (ly0, ly1):
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


def preview(g, path, everything):
    """The map drawn at 8 pixels a tile from the sheets it uses, for checking by eye."""
    new = {SHEETS[s][0]: (os.path.join(IMAGES, SHEETS[s][1]), s, s, s, s) for s in SHEETS}
    new[CHARS[0]] = (os.path.join(IMAGES, CHARS[1]), 16, 16, 112, 16)
    new[ORYX[0]] = (os.path.join(IMAGES, ORYX[1]), 16, 16, 112, 16)
    SOURCE_SETS.update(new)
    for k in new:
        _pngs.pop(k, None)

    def cell(name):
        e = everything.get(name)
        tex = None if e is None else next((n for n in e.iter() if n.find("File") is not None), None)
        if tex is None or tex.findtext("File") not in SOURCE_SETS:
            return None
        b = block(tex.findtext("File"), int(tex.findtext("Index"), 0))
        size = SOURCE_SETS[tex.findtext("File")][1]
        return [row[:size] for row in b[:size]], size
    n = 8
    pix = [[(0, 0, 0, 255)] * (W * n) for _ in range(H * n)]
    cache = {}
    for y in range(H):
        for x in range(W):
            for name, solid in ((g.ground[y][x], True), (g.obj[y][x], False)):
                if not name:
                    continue
                if name not in cache:
                    cache[name] = cell(name)
                c = cache[name]
                if c is None:
                    for yy in range(2, n - 2):
                        for xx in range(2, n - 2):
                            pix[y * n + yy][x * n + xx] = (60, 220, 60, 255)
                    continue
                b, size = c
                for yy in range(n):
                    for xx in range(n):
                        p = b[yy * size // n][xx * size // n]
                        if p[3]:
                            pix[y * n + yy][x * n + xx] = p
    write_png(path, W * n, H * n, pix)


def main():
    types = Types()
    sheets = {size: Sheet(size) for size in SHEETS}
    chars = Sheet(16, per_row=1)
    g = build_map()

    o3_grounds = load("Ground", "EmbeddedData_oryxSanctuaryGroundCXML.xml")
    o3_objects = load("Object", "EmbeddedData_oryxSanctuaryObjectsCXML.xml", "EmbeddedData_oryxSanctuaryUObjectsCXML.xml")
    used_grounds = sorted({t for row in g.ground for t in row if t and t.startswith("O5")})
    used_objects = sorted({t for row in g.obj for t in row if t and t.startswith("O5") and t not in COPIES})
    grounds, objects = [], []
    for name in used_grounds:
        e = copy.deepcopy(o3_grounds["O3" + name[2:]])
        e.set("type", types.get(name, GROUND_TYPES))
        e.set("id", name)
        tone = T_RUG if "Rug" in name else (T_GILDED if " SDF " in name else T_MARBLE)
        retexture(e, sheets, tone)
        colour = e.find("Color")
        if colour is not None:
            colour.text = {T_RUG: "0x7A44C0", T_GILDED: "0xE2C070"}.get(tone, "0xDCE0EE")
        grounds.append(e)
    grounds += island_grounds(types, sheets[8])
    for name in used_objects:
        e = copy.deepcopy(o3_objects["O3" + name[2:]])
        e.set("type", types.get(name, OBJECT_TYPES))
        e.set("id", name)
        retexture(e, sheets, T_STONE)
        objects.append(e)

    everything = load("Object", *sorted(f for f in os.listdir(os.path.join(XML_DIR, "prod")) if f.endswith(".xml")))
    tones = [T_MARBLE, T_GILDED, T_RUG, T_STONE]
    objects += characters(types, chars, sheets, everything, tones)
    objects += copies(types, sheets, everything, tones)
    objects.append(ascended_oryx(types))
    objects += fight_helpers(types, sheets, everything, tones)
    objects += clouds(types, sheets[16])
    objects += ichor(types, sheets[16])

    for tone in tones:
        tone.settle()
    os.makedirs(IMAGES, exist_ok=True)
    for size, (_, fname) in SHEETS.items():
        sheets[size].write(os.path.join(IMAGES, fname))
    chars.write(os.path.join(IMAGES, CHARS[1]), 112, 16)
    draw_oryx(os.path.join(IMAGES, ORYX[1]))

    write_xml(OUT_XML["grounds"], "Grounds", grounds,
              "Generated by tools/build_elysium.py: Oryx's Elysium's marble and gold sanctuary tiles. Do not edit.")
    write_xml(OUT_XML["objects"], "Objects", objects,
              "Generated by tools/build_elysium.py: Oryx's Elysium's walls, gates, furnishings, portals, "
              "Ascended Host, guardians and divine projectiles. Do not edit.")
    save_map(g)
    types.save()
    unknown = sorted({t for row in g.obj for t in row if t} - {e.get("id") for e in objects} - OWN
                     - {"Tree of Doom", "Nexus Portal"})
    assert not unknown, unknown
    print("wrote %s (%dx%d), %d grounds, %d objects" % (OUT_MAP, W, H, len(grounds), len(objects)))

    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(out, exist_ok=True)
        table = {e.get("id"): e for e in grounds + objects}
        table.update(load("Ground", "EmbeddedData_GroundCXML.xml", "EmbeddedData_ancientRuinsGroundCXML.xml"))
        table.update(load("Object", "EmbeddedData_StaticObjectsCXML.xml", "EmbeddedData_ancientRuinsObjectsCXML.xml",
                          "EmbeddedData_oryxSanctuaryObjectsCXML.xml"))
        table.update({e.get("id"): e for e in grounds + objects})
        preview(g, os.path.join(out, "elysium.png"), table)
        print("preview in", out)


if __name__ == "__main__":
    main()
