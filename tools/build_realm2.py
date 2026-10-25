"""Draws Realm 2's art and writes its game data: the grounds, scenery, walls and monsters of its
eleven event areas (one for each realm event boss), its portal and its key.

    python tools/build_realm2.py [--preview <png>]

The map itself is made by the world server each time a Realm 2 opens
(source/WorldServer/core/worlds/realm2): this script only makes what it is built from. The
monsters' behaviours are in BehaviorDb.Realm2.cs; their projectiles (the index order the
behaviours shoot by) are here, in MOBS.

Outputs (generated, don't edit):
- Realm2Grounds.xml and Realm2Objects.xml, in source/Shared/resources/xml/custom and the client's
  custom/xmls;
- the sheets Realm2Env8x8.png (grounds, scenery, walls, the key), Realm2Env16x16.png (the portal),
  Realm2Chars8x8.png and Realm2Chars16x16.png (monsters and quest monsters, seven frames each) in
  the client's custom/images, registered for the Rust client by tools/gen_rs_manifest.py.
Type codes are kept stable in tools/realm2/types.json. Needs Pillow.
"""
import json
import math
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from realm2 import art  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "Realm2Grounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "Realm2Grounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "Realm2Objects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "Realm2Objects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "realm2", "types.json")
GROUND_TYPES = (0x7800, 0x7900)
OBJECT_TYPES = (0x7900, 0x7B00)
ENV8 = ("realm2Env8x8", "Realm2Env8x8.png")
ENV16 = ("realm2Env16x16", "Realm2Env16x16.png")
CHARS8 = ("realm2Chars8x8", "Realm2Chars8x8.png")
CHARS16 = ("realm2Chars16x16", "Realm2Chars16x16.png")
COLS = 16           # cells across the environment sheets
CHAR_COLS = 4       # characters across the character sheets (seven frames each)

# ------------------------------------------------------------------------------------------------
# pixels


def picture(rows, pal, size):
    """A pixel map (art.py) as an RGBA image `size` square."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if len(rows) > size:
        raise ValueError("%d rows, at most %d" % (len(rows), size))
    for y, row in enumerate(rows):
        if len(row) > size:
            raise ValueError("row %d is %d wide, at most %d: %r" % (y, len(row), size, row))
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            if ch not in pal:
                raise ValueError("no colour %r" % ch)
            img.putpixel((x, y), pal[ch] + (255,))
    return img


def shifted(img, dx, dy):
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(img, (dx, dy))
    return out


def char_strip(stand, attack, size):
    """The seven frames of a character: stand, walk 1 and 2 (a step's bob), unused, attack 1 and 2,
    and the attack's second cell (blank)."""
    attack = attack or shifted(stand, 1, 0)
    strip = Image.new("RGBA", (size * 7, size), (0, 0, 0, 0))
    for i, frame in enumerate([stand, stand, shifted(stand, 0, -1), stand, stand, attack]):
        strip.paste(frame, (i * size, 0))
    return strip


def _hash(x, y, seed):
    h = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return (h ^ (h >> 16)) / 0xFFFFFFFF


def _mix(a, b, f):
    return tuple(int(x + (y - x) * f) for x, y in zip(a, b))


def tile(fn):
    img = Image.new("RGBA", (8, 8))
    for y in range(8):
        for x in range(8):
            img.putpixel((x, y), tuple(fn(x, y)) + (255,))
    return img


# ------------------------------------------------------------------------------------------------
# grounds: each painter makes one 8x8 variant from a seed

def p_speckle(base, dark, light, density=0.14):
    # soft: the flecks are halfway to the base, the rest only a shade off it
    dark, light = _mix(base, dark, 0.55), _mix(base, light, 0.5)

    def paint(seed):
        def px(x, y):
            v = _hash(x, y, seed)
            if v < density / 2:
                return dark
            if v > 1 - density / 2:
                return light
            return _mix(base, dark if _hash(x + 9, y, seed) < 0.5 else light, 0.2 * _hash(x, y + 7, seed))
        return tile(px)
    return paint


def p_grid(base, line, glow=None):
    line = _mix(base, line, 0.55)

    def paint(seed):
        def px(x, y):
            if x == 0 or y == 0:
                return line
            if glow and (x, y) == (1 + seed % 6, 1 + (seed * 3) % 6):
                return glow
            v = _hash(x, y, seed)
            return _mix(base, line, 0.15 * v)
        return tile(px)
    return paint


def p_flagstones(stone, mortar, light):
    # two stones a tile, the seam moving with the seed
    def paint(seed):
        cut = 3 + seed % 3
        def px(x, y):
            if y == 0 or (x == 0 and y < cut) or (x == 4 and y >= cut) or y == cut:
                return mortar
            v = _hash(x, y, seed)
            return _mix(stone, light, 0.45) if v > 0.96 else _mix(stone, mortar, 0.2 * v)
        return tile(px)
    return paint


def p_cracks(base, crack, glow=None, density=0.2):
    def paint(seed):
        pts = set()
        x, y = seed % 8, 0
        for _ in range(10):
            pts.add((x % 8, y % 8))
            x += (1 if _hash(x, y, seed) > 0.5 else -1)
            y += 1
        def px(x, y):
            if (x, y) in pts:
                return glow if glow and _hash(x, y, seed + 1) > 0.4 else crack
            v = _hash(x, y, seed)
            return _mix(base, crack, density * v)
        return tile(px)
    return paint


def p_petals(base, dark, petals):
    def paint(seed):
        def px(x, y):
            v = _hash(x, y, seed)
            if v > 0.9:
                return petals[int(_hash(y, x, seed) * len(petals)) % len(petals)]
            return _mix(base, dark, 0.35 * _hash(x + 3, y + 5, seed))
        return tile(px)
    return paint


def p_comb(base, line, light):
    # a honeycomb: hexagons four pixels across, offset each row of cells
    line, light = _mix(base, line, 0.5), _mix(base, light, 0.5)

    def paint(seed):
        def px(x, y):
            row = y // 4
            cx = (x + (2 if row % 2 else 0)) % 4
            cy = y % 4
            if cy == 0 or cx == 0:
                return line
            return light if (cx, cy) == (1, 1) and _hash(x, y, seed) > 0.4 else base
        return tile(px)
    return paint


def p_water(dark, light, crest):
    def paint(seed):
        def px(x, y):
            v = math.sin((x + y * 0.5 + seed * 1.7) * 0.9) * 0.5 + 0.5
            c = _mix(dark, light, v * 0.6)
            return _mix(c, crest, 0.6) if v > 0.96 and _hash(x, y, seed) > 0.6 else c
        return tile(px)
    return paint


def p_planks(wood, gap, light):
    def paint(seed):
        def px(x, y):
            if y in (0, 4) or (x == (seed * 3) % 8 and y < 4) or (x == (seed * 5 + 4) % 8 and y > 4):
                return gap
            v = _hash(x, y, seed)
            return light if v > 0.88 else _mix(wood, gap, 0.2 * v)
        return tile(px)
    return paint


def p_runes(base, dark, rune):
    shapes = [((2, 2), (3, 2), (4, 2), (3, 3), (3, 4)), ((2, 3), (3, 2), (4, 3), (3, 4)),
              ((2, 2), (2, 3), (2, 4), (3, 4), (4, 4))]
    def paint(seed):
        mark = set(shapes[seed % len(shapes)]) if seed % 2 == 0 else set()
        def px(x, y):
            if (x, y) in mark:
                return rune
            return _mix(base, dark, 0.5 * _hash(x, y, seed))
        return tile(px)
    return paint


def p_stars(base, star, dim):
    def paint(seed):
        def px(x, y):
            v = _hash(x, y, seed)
            if v > 0.985:
                return star
            if v > 0.95:
                return dim
            return _mix(base, (0, 0, 0), 0.4 * _hash(x + 1, y + 2, seed))
        return tile(px)
    return paint


def p_dunes(base, light, dark):
    def paint(seed):
        def px(x, y):
            v = math.sin((x * 0.8 + y * 1.6 + seed) * 0.8)
            if v > 0.85:
                return light
            if v < -0.9:
                return dark
            return _mix(base, light, 0.15 * _hash(x, y, seed))
        return tile(px)
    return paint


C = art.hexc
P = art.PALETTES

# name -> (area, painter, variants, extra XML, minimap colour)
GROUNDS = {
    # shared: the void between the areas, the bridges and the pools that join them
    "R2 Abyss": (None, p_stars(C("#06040e"), C("#c8c0ff"), C("#4a4070")), 4,
                 {"NoWalk": None, "BlendPriority": "-1"}, "0x08060E"),
    "R2 Bridge": (None, p_planks(C("#7a5432"), C("#3a2414"), C("#a8784a")), 3, {"BlendPriority": "20"}, "0x7A5432"),
    "R2 Bridge Rail": (None, p_planks(C("#4e321c"), C("#2a180c"), C("#6e4a2a")), 2,
                       {"NoWalk": None, "BlendPriority": "21"}, "0x4E321C"),
    "R2 Pool Water": (None, p_water(C("#1a5c8a"), C("#48a6d8"), C("#c8f0ff")), 3,
                      {"Sink": None, "Speed": "0.75", "Animate": ("Wave", {"dx": "0.25", "dy": "0.25"}),
                       "BlendPriority": "15"}, "0x2A78AA"),
    # the Cubic Expanse
    "R2 Cube Grid": ("cube", p_grid(P["cube"]["1"], P["cube"]["2"]), 4, {"BlendPriority": "5"}, "0x1B2A6B"),
    "R2 Cube Grid Bright": ("cube", p_grid(P["cube"]["2"], P["cube"]["3"], P["cube"]["5"]), 4,
                            {"BlendPriority": "6"}, "0x2F49B5"),
    "R2 Prism Floor": ("cube", p_grid(P["cube"]["4"], P["cube"]["5"], P["cube"]["6"]), 4,
                       {"BlendPriority": "7"}, "0x1F7F9E"),
    # the Watching Dark
    "R2 Dark Soil": ("penta", p_speckle(P["penta"]["2"], P["penta"]["1"], P["penta"]["3"]), 4,
                     {"BlendPriority": "5"}, "0x2A1A36"),
    "R2 Ashen Soil": ("penta", p_speckle(P["penta"]["4"], P["penta"]["1"], P["penta"]["5"]), 4,
                      {"BlendPriority": "6"}, "0x3A3A30"),
    "R2 Ritual Floor": ("penta", p_runes(P["penta"]["1"], P["penta"]["2"], P["penta"]["7"]), 4,
                        {"BlendPriority": "7"}, "0x140C1C"),
    # the Lost Lands
    "R2 Lost Dust": ("lost", p_speckle(P["lost"]["2"], P["lost"]["1"], P["lost"]["3"], 0.2), 4,
                     {"BlendPriority": "5"}, "0x4A5468"),
    "R2 Lost Cracked Earth": ("lost", p_cracks(P["lost"]["2"], P["lost"]["1"], P["lost"]["7"]), 4,
                              {"BlendPriority": "6"}, "0x3A4458"),
    "R2 Lost Flagstones": ("lost", p_flagstones(P["lost"]["3"], P["lost"]["1"], P["lost"]["6"]), 4,
                           {"BlendPriority": "7"}, "0x7A8498"),
    # the Tidepools
    "R2 Tide Sand": ("hermit", p_speckle(C("#c8b480"), C("#9a8658"), C("#ece0b0")), 4,
                     {"BlendPriority": "5"}, "0xC8B480"),
    "R2 Kelp Bed": ("hermit", p_speckle(P["hermit"]["1"], C("#06201e"), P["hermit"]["2"]), 4,
                    {"BlendPriority": "6"}, "0x0E3A3A"),
    "R2 Tide Pool": ("hermit", p_water(P["hermit"]["1"], P["hermit"]["3"], C("#d8fff0")), 3,
                     {"Sink": None, "Speed": "0.8", "Animate": ("Wave", {"dx": "0.2", "dy": "0.2"}),
                      "BlendPriority": "4"}, "0x1E6A5E"),
    # the Wreck Coast
    "R2 Wet Shingle": ("ship", p_speckle(P["ship"]["2"], P["ship"]["1"], P["ship"]["3"]), 4,
                       {"BlendPriority": "5"}, "0x34443A"),
    "R2 Fog Shallows": ("ship", p_water(C("#22302c"), C("#4a6058"), C("#a8c8b8")), 3,
                        {"Sink": None, "Speed": "0.8", "Animate": ("Wave", {"dx": "0.15", "dy": "0.15"}),
                         "BlendPriority": "4"}, "0x2E3E38"),
    "R2 Wreck Planks": ("ship", p_planks(P["ship"]["5"], P["ship"]["4"], P["ship"]["6"]), 4,
                        {"BlendPriority": "7"}, "0x6C5038"),
    # the Sunken Tombs
    "R2 Tomb Sand": ("sphinx", p_dunes(P["sphinx"]["3"], C("#fff0c0"), P["sphinx"]["2"]), 4,
                     {"BlendPriority": "5"}, "0xE8CC80"),
    "R2 Dune Sand": ("sphinx", p_dunes(P["sphinx"]["2"], P["sphinx"]["3"], P["sphinx"]["1"]), 4,
                     {"BlendPriority": "6"}, "0xB08A40"),
    "R2 Sandstone Tiles": ("sphinx", p_flagstones(C("#d0b070"), P["sphinx"]["1"], P["sphinx"]["8"]), 4,
                           {"BlendPriority": "7"}, "0xD0B070"),
    # the Bonefields
    "R2 Blood Soil": ("skull", p_speckle(P["skull"]["2"], P["skull"]["1"], P["skull"]["3"], 0.18), 4,
                      {"BlendPriority": "5"}, "0x701818"),
    "R2 Bone Dust": ("skull", p_speckle(P["skull"]["4"], C("#5a4c38"), P["skull"]["5"], 0.18), 4,
                     {"BlendPriority": "6"}, "0x8A7A60"),
    "R2 Charred Stone": ("skull", p_flagstones(C("#3a2a26"), P["skull"]["1"], P["skull"]["7"]), 4,
                         {"BlendPriority": "7"}, "0x3A2A26"),
    # the Jade Garden
    "R2 Moss Grass": ("garden", p_speckle(P["garden"]["2"], P["garden"]["1"], P["garden"]["3"], 0.18), 4,
                      {"BlendPriority": "5"}, "0x3C6C34"),
    "R2 Petal Grass": ("garden", p_petals(P["garden"]["2"], P["garden"]["1"],
                                          [P["garden"]["5"], P["garden"]["6"], P["garden"]["4"]]), 4,
                       {"BlendPriority": "6"}, "0x4C6C44"),
    "R2 Garden Stones": ("garden", p_flagstones(C("#8a8a84"), C("#4a4a44"), C("#c8c8c0")), 4,
                         {"BlendPriority": "7"}, "0x8A8A84"),
    # the Ashen Caldera
    "R2 Obsidian Ground": ("dragon", p_speckle(P["dragon"]["2"], P["dragon"]["1"], P["dragon"]["3"], 0.18), 4,
                           {"BlendPriority": "5"}, "0x302830"),
    "R2 Ash Ground": ("dragon", p_speckle(C("#5a5250"), C("#3a3434"), C("#847a76"), 0.18), 4,
                      {"BlendPriority": "6"}, "0x5A5250"),
    "R2 Magma Cracks": ("dragon", p_cracks(P["dragon"]["1"], P["dragon"]["4"], P["dragon"]["6"], 0.18), 4,
                        {"BlendPriority": "7"}, "0x3A1810"),
    "R2 Lava": ("dragon", p_water(P["dragon"]["4"], P["dragon"]["5"], P["dragon"]["8"]), 3,
                {"Sink": None, "Speed": "0.6", "MinDamage": "50", "MaxDamage": "50",
                 "Animate": ("Flow", {"dx": "0.3", "dy": "0.3"}), "BlendPriority": "3"}, "0xE05010"),
    # the Forgotten Kingdom
    "R2 Kingdom Slate": ("kingdom", p_flagstones(P["kingdom"]["2"], P["kingdom"]["1"], P["kingdom"]["3"]), 4,
                         {"BlendPriority": "5"}, "0x2C3038"),
    "R2 Kingdom Rubble Floor": ("kingdom", p_cracks(P["kingdom"]["3"], P["kingdom"]["1"], None, 0.18), 4,
                                {"BlendPriority": "6"}, "0x4E5462"),
    "R2 Kingdom Mosaic": ("kingdom", p_grid(P["kingdom"]["4"], P["kingdom"]["1"], P["kingdom"]["7"]), 4,
                          {"BlendPriority": "7"}, "0x3A2460"),
    # the Honey Meadow
    "R2 Meadow Grass": ("bees", p_petals(P["bees"]["5"], P["bees"]["4"],
                                         [P["bees"]["3"], C("#f8f8f0"), C("#e070c0")]), 4,
                        {"BlendPriority": "5"}, "0x58A830"),
    "R2 Honeycomb Floor": ("bees", p_comb(P["bees"]["3"], P["bees"]["1"], P["bees"]["8"]), 4,
                           {"BlendPriority": "7"}, "0xF8C030"),
    "R2 Honey Pool": ("bees", p_water(P["bees"]["2"], P["bees"]["3"], P["bees"]["8"]), 3,
                      {"Sink": None, "Speed": "0.55", "Animate": ("Flow", {"dx": "0.05", "dy": "0.05"}),
                       "BlendPriority": "4"}, "0xC07810"),
}

# ------------------------------------------------------------------------------------------------
# scenery: name -> kind ("block": stands in the way, "plain": walk through, "flat": on the ground)
# and size range

DECOR = {
    "R2 Cube Crystal": ("block", 100, 130), "R2 Floating Cube": ("plain", 80, 100),
    "R2 Cube Monolith": ("block", 120, 150), "R2 Cube Shards": ("flat", 100, 100),
    "R2 Dead Tree": ("block", 130, 160), "R2 Dead Sapling": ("plain", 90, 110),
    "R2 Eye Stalk": ("block", 90, 120), "R2 Bone Pile": ("flat", 100, 100),
    "R2 Broken Pillar": ("block", 110, 140), "R2 Lost Crystal": ("block", 90, 120),
    "R2 Gravestone": ("block", 90, 110), "R2 Withered Shrub": ("plain", 90, 110),
    "R2 Coral": ("block", 100, 130), "R2 Giant Shell": ("block", 100, 120),
    "R2 Seaweed": ("plain", 90, 120), "R2 Barnacle Rock": ("block", 100, 130),
    "R2 Broken Mast": ("block", 120, 150), "R2 Wreck Hull": ("block", 110, 130),
    "R2 Anchor": ("block", 90, 110), "R2 Barrel": ("block", 80, 100),
    "R2 Obelisk": ("block", 120, 150), "R2 Broken Statue": ("block", 100, 120),
    "R2 Sandstone Block": ("block", 100, 110), "R2 Tomb Urn": ("plain", 80, 100),
    "R2 Skull Totem": ("block", 100, 120), "R2 Rib Cage": ("flat", 100, 100),
    "R2 Bone Brazier": ("block", 90, 110), "R2 Blood Spike": ("block", 100, 130),
    "R2 Blossom Tree": ("block", 130, 160), "R2 Stone Lantern": ("block", 90, 110),
    "R2 Bamboo": ("block", 110, 140), "R2 Garden Shrub": ("plain", 100, 120),
    "R2 Dragon Bones": ("flat", 100, 120), "R2 Obsidian Spire": ("block", 110, 140),
    "R2 Ember Vent": ("plain", 90, 110), "R2 Charred Tree": ("block", 120, 150),
    "R2 Fallen Column": ("block", 100, 120), "R2 Forgotten Statue": ("block", 110, 130),
    "R2 Soul Brazier": ("block", 90, 110), "R2 Kingdom Rubble": ("flat", 100, 100),
    "R2 Giant Flower": ("plain", 100, 140), "R2 Sunflower": ("plain", 110, 140),
    "R2 Small Hive": ("block", 90, 110), "R2 Clover Bush": ("plain", 100, 120),
}

# braziers, vents and lanterns flicker: their glow letters swap for a second frame
FLICKER = {"R2 Bone Brazier", "R2 Soul Brazier", "R2 Ember Vent", "R2 Stone Lantern"}

# ------------------------------------------------------------------------------------------------
# monsters: name -> (shown as, health, defence, size, flags, projectiles)
# a projectile: (object id, damage, speed, lifetime ms, {extras})
# flags: q quest (with its level), f flying, s stationary (doesn't move), g counts as a god

def shot(obj, damage, speed, life, **extra):
    return (obj, damage, speed, life, extra)


MOBS = {
    # --- the Cubic Expanse
    "R2 Cubelet": ("Cubelet", 900, 8, 80, "g", [shot("Blue Bolt", 70, 70, 1100)]),
    "R2 Prism Sentinel": ("Prism Sentinel", 1800, 15, 100, "gf", [
        shot("Crystal Shard", 90, 55, 2000), shot("Blue Star", 60, 80, 1200, effect=("Slowed", 2))]),
    "R2 Shard Walker": ("Shard Walker", 1500, 12, 100, "g", [shot("Crystal Bolt", 110, 80, 1000)]),
    "R2 Cube Overseer": ("Cube Overseer", 2400, 18, 110, "gf", [shot("Blue Magic", 100, 60, 2000)]),
    "R2 Tesseract": ("Tesseract", 14000, 22, 120, "f", [
        shot("Crystal Shard", 120, 60, 2500), shot("Blue Star", 90, 45, 3000, effect=("Slowed", 2)),
        shot("Purple Magic", 140, 80, 1500)], 19),
    "R2 Hypercube Warden": ("Hypercube Warden", 12000, 25, 120, "", [
        shot("Silver Shield", 150, 60, 1500, multi=True), shot("Blue Bolt", 100, 90, 1300)], 18),
    # --- the Watching Dark
    "R2 Floating Eye": ("Floating Eye", 1400, 10, 100, "gf", [shot("Red Star", 90, 60, 1800)]),
    "R2 Eye Swarmer": ("Eye Swarmer", 700, 5, 70, "gf", [shot("Red Fire", 60, 90, 900)]),
    "R2 Gazer": ("Gazer", 2200, 15, 110, "gf", [
        shot("White Bolt", 120, 50, 2700, size=120), shot("Purple Star", 0, 70, 3000, effect=("Blind", 3))]),
    "R2 Watcher Stalk": ("Watcher Stalk", 1600, 20, 110, "gs", [
        shot("Red Fire", 100, 70, 2000), shot("Darkness Bolt", 80, 50, 3000, effect=("Darkness", 2))]),
    "R2 Eye of the Pentagram": ("Eye of the Pentagram", 15000, 20, 120, "f", [
        shot("Red Star", 120, 60, 2500), shot("Darkness Bolt", 100, 40, 4000, effect=("Blind", 2)),
        shot("Red Shard Bolt", 150, 100, 1000)], 19),
    "R2 Elder Watcher": ("Elder Watcher", 12000, 18, 120, "f", [
        shot("White Bolt", 140, 50, 3000, size=130), shot("Purple Star", 0, 70, 3000, effect=("Blind", 3))], 18),
    # --- the Lost Lands
    "R2 Lost Sentry": ("Lost Sentry", 1800, 16, 100, "g", [
        shot("Blade", 110, 90, 600), shot("White Shard Bolt", 90, 70, 1500)]),
    "R2 Forsaken Archer": ("Forsaken Archer", 1300, 8, 100, "g", [shot("Blue Arrow", 110, 120, 1200)]),
    "R2 Wailing Wraith": ("Wailing Wraith", 1200, 5, 100, "gf", [
        shot("White Flame", 80, 50, 2000, effect=("Slowed", 2))]),
    "R2 Ruin Golem": ("Ruin Golem", 2800, 25, 120, "g", [shot("Grey Star", 130, 40, 2500)]),
    "R2 Sentry Captain": ("Sentry Captain", 12000, 22, 120, "", [
        shot("Blade", 140, 90, 700), shot("White Shard Bolt", 110, 70, 1800), shot("Blue Arrow", 120, 120, 1300)], 18),
    "R2 Forgotten Warlord": ("Forgotten Warlord", 15000, 25, 130, "", [
        shot("Dark Gray Spinner", 140, 50, 2500), shot("Silver Shield", 160, 70, 1200, multi=True)], 19),
    # --- the Tidepools
    "R2 Hermit Brute": ("Hermit Brute", 2400, 22, 110, "g", [shot("Aqua Missile", 110, 60, 1500)]),
    "R2 Tide Snapper": ("Tide Snapper", 1200, 10, 90, "g", [
        shot("Blue Boomerang", 90, 70, 1400, multi=True)]),
    "R2 Barnacle Turret": ("Barnacle Turret", 1500, 18, 100, "gs", [shot("Aqua Missile", 90, 55, 2000)]),
    "R2 Kelp Lurker": ("Kelp Lurker", 1600, 8, 100, "g", [
        shot("Green Spinner", 90, 40, 2600, effect=("Slowed", 2))]),
    "R2 Shellback Champion": ("Shellback Champion", 14000, 30, 130, "", [
        shot("Aqua Missile", 140, 60, 2000), shot("Blue Bolt", 110, 90, 1300)], 18),
    "R2 Ancient Snapper": ("Ancient Snapper", 12000, 20, 130, "", [
        shot("Blue Boomerang", 150, 80, 1600, multi=True), shot("Aqua Missile", 110, 55, 2200)], 19),
    # --- the Wreck Coast
    "R2 Drowned Buccaneer": ("Drowned Buccaneer", 1600, 12, 100, "g", [
        shot("Blade", 120, 100, 500), shot("Grey Boomerang", 90, 70, 1200)]),
    "R2 Spectral Gull": ("Spectral Gull", 800, 4, 80, "gf", [shot("Feather", 60, 90, 900)]),
    "R2 Bone Gunner": ("Bone Gunner", 1700, 15, 100, "g", [shot("Black Ball", 140, 50, 2200, size=130)]),
    "R2 Wreck Wraith": ("Wreck Wraith", 1400, 6, 100, "gf", [
        shot("Green Magic", 90, 60, 1800, effect=("Slowed", 2))]),
    "R2 Phantom Quartermaster": ("Phantom Quartermaster", 13000, 20, 120, "", [
        shot("Green Star", 130, 60, 2400), shot("Black Ball", 160, 45, 2500, size=140)], 18),
    "R2 Abyssal Kraken": ("Abyssal Kraken", 15000, 18, 140, "", [
        shot("Deep Sea Blast", 150, 50, 2600), shot("Black Missile", 110, 70, 2000)], 19),
    # --- the Sunken Tombs
    "R2 Scarab": ("Scarab", 700, 15, 70, "g", [shot("Yellow Shard Bolt", 70, 80, 800)]),
    "R2 Tomb Mummy": ("Tomb Mummy", 2000, 12, 100, "g", [
        shot("White Bullet", 100, 60, 1600, effect=("Slowed", 2))]),
    "R2 Jackal Guard": ("Jackal Guard", 1800, 16, 100, "g", [shot("Yellow Missile", 120, 90, 1200)]),
    "R2 Sun Obelisk": ("Sun Obelisk", 2200, 30, 120, "gs", [shot("Yellow Energy Shot", 110, 55, 2500)]),
    "R2 Tomb Sentinel": ("Tomb Sentinel", 14000, 24, 130, "", [
        shot("Yellow Energy Shot", 130, 55, 2600), shot("Yellow Missile", 140, 90, 1400)], 18),
    "R2 Sand Scourge": ("Sand Scourge", 12000, 18, 130, "", [
        shot("Yellow Shard Bolt", 110, 80, 1200), shot("Brown Magic", 100, 50, 2400, effect=("Slowed", 3))], 19),
    # --- the Bonefields
    "R2 Bone Flayer": ("Bone Flayer", 1700, 12, 100, "g", [shot("White Shard Bolt", 110, 90, 900)]),
    "R2 Skull Wisp": ("Skull Wisp", 900, 5, 90, "gf", [shot("Fire Bolt", 90, 70, 1400)]),
    "R2 Blood Imp": ("Blood Imp", 1100, 8, 90, "g", [shot("Red Fire", 90, 80, 1200)]),
    "R2 Shrine Cultist": ("Shrine Cultist", 1500, 10, 100, "g", [shot("Cultist Fire Shot", 110, 60, 2000)]),
    "R2 Shrine Keeper": ("Shrine Keeper", 14000, 20, 120, "", [
        shot("Cultist Fire Shot", 140, 60, 2400), shot("Red Star", 110, 70, 1800)], 19),
    "R2 Marrow Fiend": ("Marrow Fiend", 12000, 22, 130, "", [
        shot("White Shard Bolt", 120, 80, 1600), shot("Red Shard Bolt", 150, 100, 1000)], 18),
    # --- the Jade Garden
    "R2 Komainu": ("Komainu", 2000, 20, 100, "g", [shot("Grey Star", 110, 60, 1400)]),
    "R2 Oni Mask": ("Oni Mask", 1500, 10, 100, "gf", [shot("Red Fire", 100, 70, 1500)]),
    "R2 Kitsune Spirit": ("Kitsune Spirit", 1300, 8, 100, "g", [
        shot("Cyan Magic", 90, 80, 1500), shot("Pink Bolt", 80, 60, 2000)]),
    "R2 Lantern Wisp": ("Lantern Wisp", 900, 5, 90, "gf", [shot("Fire Bullet", 80, 60, 1600)]),
    "R2 Oni Warlord": ("Oni Warlord", 14000, 24, 130, "", [
        shot("Red Fire", 130, 75, 1700), shot("Silver Shield", 160, 65, 1200, multi=True)], 19),
    "R2 Jade Golem": ("Jade Golem", 15000, 30, 130, "", [
        shot("Crystal Green Shard", 140, 50, 2400), shot("Green Bolt", 110, 80, 1400)], 18),
    # --- the Ashen Caldera
    "R2 Drakeling": ("Drakeling", 1400, 10, 100, "gf", [shot("Fire Bolt", 100, 80, 1200)]),
    "R2 Magma Salamander": ("Magma Salamander", 1800, 14, 100, "g", [shot("Fire Missile", 110, 60, 1600)]),
    "R2 Ember Wyrm": ("Ember Wyrm", 1600, 10, 110, "gf", [
        shot("Dragon Attack Red Spinning Fireball", 100, 50, 2400, amplitude=0.5, frequency=2)]),
    "R2 Obsidian Golem": ("Obsidian Golem", 3000, 30, 120, "g", [
        shot("LOD Red Big Fireball", 150, 40, 2500)]),
    "R2 Wyrm Matriarch": ("Wyrm Matriarch", 15000, 22, 140, "f", [
        shot("Dragon Attack Red Firewave", 160, 60, 1600, multi=True), shot("Fire Bolt", 110, 70, 2000)], 19),
    "R2 Elder Drake": ("Elder Drake", 13000, 24, 130, "", [
        shot("LOD Red Spinning Fireball", 140, 55, 2200), shot("Fire Missile", 120, 70, 1800)], 18),
    # --- the Forgotten Kingdom
    "R2 Shattered Knight": ("Shattered Knight", 2000, 22, 100, "g", [
        shot("Silver Shield", 120, 70, 800, multi=True), shot("Blade", 110, 90, 600)]),
    "R2 Forgotten Mage": ("Forgotten Mage", 1300, 8, 100, "g", [
        shot("Violet Missile", 100, 80, 1600), shot("Purple Magic", 90, 60, 2000)]),
    "R2 Stone Gargoyle": ("Stone Gargoyle", 1800, 25, 100, "gf", [shot("Grey Star", 100, 70, 1300)]),
    "R2 Soul Lantern": ("Soul Lantern", 1100, 10, 90, "gf", [shot("Cyan Magic", 80, 50, 2400)]),
    "R2 Forgotten Archmage": ("Forgotten Archmage", 13000, 18, 120, "", [
        shot("Violet Missile", 130, 70, 2200), shot("Purple Star", 90, 60, 2000, effect=("Quiet", 2))], 19),
    "R2 King's Champion": ("King's Champion", 15000, 28, 130, "", [
        shot("Silver Shield", 160, 70, 1400, multi=True), shot("Blade", 130, 100, 700)], 18),
    # --- the Honey Meadow
    "R2 Worker Bee": ("Worker Bee", 700, 4, 70, "gf", [shot("EH Yellow Stinger Small", 70, 90, 800)]),
    "R2 Hornet Drone": ("Hornet Drone", 1200, 10, 90, "gf", [shot("EH Red Stinger Small", 100, 100, 900)]),
    "R2 Honey Slime": ("Honey Slime", 1600, 12, 100, "g", [
        shot("Yellow Energy Shot", 90, 40, 2000, effect=("Slowed", 3))]),
    "R2 Pollen Sprite": ("Pollen Sprite", 900, 4, 90, "gf", [
        shot("Green Spinner", 60, 50, 2000, effect=("Slowed", 1))]),
    "R2 Royal Guard Bee": ("Royal Guard Bee", 13000, 20, 130, "f", [
        shot("EH Yellow Glaive", 140, 70, 1800, multi=True), shot("EH Yellow Stinger Small", 100, 90, 1100)], 18),
    "R2 Hornet Queen": ("Hornet Queen", 15000, 18, 140, "f", [
        shot("EH Red Glaive", 150, 70, 1800, multi=True), shot("EH Red Stinger Small", 110, 100, 1100)], 19),
}

SOUNDS = {
    "cube": ("monster/cube_god_hit", "monster/cube_god_death"),
    "penta": ("monster/beholder_hit", "monster/beholder_death"),
    "lost": ("monster/ghost_god_hit", "monster/ghost_god_death"),
    "hermit": ("monster/crab_hit", "monster/crab_death"),
    "ship": ("monster/pirates_hit", "monster/pirates_death"),
    "sphinx": ("monster/mummy_hit", "monster/mummy_death"),
    "skull": ("monster/skeletons_hit", "monster/skeletons_death"),
    "garden": ("monster/stone_walls_hit", "monster/stone_walls_death"),
    "dragon": ("monster/lizard_god_hit", "monster/lizard_god_death"),
    "kingdom": ("monster/knights_hit", "monster/knights_death"),
    "bees": ("monster/flies_hit", "monster/flies_death"),
}

PORTAL_ID = "Realm2"            # /spawn realm2 finds it by name
PORTAL_NAME = "Realm 2"
KEY_ID = "Realm 2 Key"

# ------------------------------------------------------------------------------------------------
# types


class Types:
    def __init__(self):
        self.data = json.load(open(TYPES)) if os.path.exists(TYPES) else {"grounds": {}, "objects": {}}

    def get(self, kind, name):
        table = self.data[kind]
        if name not in table:
            lo, hi = GROUND_TYPES if kind == "grounds" else OBJECT_TYPES
            used = set(int(v, 16) for v in table.values())
            free = next(t for t in range(lo, hi) if t not in used)
            table[name] = "0x%04X" % free
        return table[name]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w", newline="\n") as f:
            json.dump(self.data, f, indent=1, sort_keys=True)
            f.write("\n")


# ------------------------------------------------------------------------------------------------
# sheets


class Sheet:
    """Cells `w` by `h` (a character's seven frames are one cell), `cols` to a row."""

    def __init__(self, w, h=None, cols=COLS):
        self.w, self.h, self.cols, self.cells = w, h or w, cols, []

    def add(self, img):
        self.cells.append(img)
        return len(self.cells) - 1

    def save(self, path):
        rows = max(1, (len(self.cells) + self.cols - 1) // self.cols)
        out = Image.new("RGBA", (self.cols * self.w, rows * self.h), (0, 0, 0, 0))
        for i, img in enumerate(self.cells):
            out.paste(img, ((i % self.cols) * self.w, (i // self.cols) * self.h))
        out.save(path)
        return out


def sub(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, attrs)
    if text is not None:
        e.text = str(text)
    return e


def texture(parent, file, index, tag="Texture"):
    t = sub(parent, tag)
    sub(t, "File", file)
    sub(t, "Index", "0x%02x" % index)
    return t


def flicker(rows):
    swap = {"7": "8", "8": "7", "y": "8"}
    return ["".join(swap.get(c, c) for c in row) for row in rows]


def paint_wall(area, face, top, style):
    pal = art.PALETTES[area]
    f, t = pal[face], pal[top]
    dark = _mix(f, (0, 0, 0), 0.45)
    light = _mix(t, (255, 255, 255), 0.25)

    def face_px(x, y):
        if style == "brick":
            if y % 4 == 3 or (x == (0 if (y // 4) % 2 == 0 else 4) and y % 4 != 3):
                return dark
        elif style == "grid":
            if x in (0, 7) or y in (0, 7):
                return dark
            if (x, y) in ((3, 3), (4, 4)):
                return pal["5"]
        elif style == "planks":
            if x % 3 == 0:
                return dark
        elif style == "comb":
            row = y // 4
            cx = (x + (2 if row % 2 else 0)) % 4
            if y % 4 == 0 or cx == 0:
                return dark
        v = _hash(x, y, len(area) * 31)
        return _mix(f, dark, 0.35 * v) if style == "rough" or v < 0.8 else _mix(f, light, 0.3)

    def top_px(x, y):
        v = _hash(x, y, len(area) * 17)
        if x in (0, 7) or y in (0, 7):
            return _mix(t, dark, 0.4)
        return _mix(t, light, 0.4 * v)

    return tile(face_px), tile(top_px)


def build(preview=None):
    types = Types()
    env8, env16 = Sheet(8), Sheet(16)
    chars8, chars16 = Sheet(56, 8, CHAR_COLS), Sheet(112, 16, CHAR_COLS)
    grounds, objects = [], []

    # grounds
    for name, (area, painter, variants, extra, colour) in GROUNDS.items():
        g = ET.Element("Ground", {"type": types.get("grounds", name), "id": name})
        cells = [env8.add(painter(i * 7 + 3)) for i in range(variants)]
        if len(cells) == 1:
            texture(g, ENV8[0], cells[0])
        else:
            rt = sub(g, "RandomTexture")
            for c in cells:
                texture(rt, ENV8[0], c)
        for tag, val in extra.items():
            if isinstance(val, tuple):
                sub(g, tag, val[0], **val[1])
            else:
                sub(g, tag, val)
        sub(g, "Color", colour)
        grounds.append(g)

    # scenery
    for name, (area, rows) in art.DECOR8.items():
        kind, lo, hi = DECOR[name]
        pal = art.PALETTES[area]
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "Class", "GameObject")
        index = env8.add(picture(rows, pal, 8))
        texture(o, ENV8[0], index)
        if name in FLICKER:
            second = env8.add(picture(flicker(rows), pal, 8))
            anim = sub(o, "Animation", prob="1.0")
            for i in (index, second):
                fr = sub(anim, "Frame", time="0.25")
                texture(fr, ENV8[0], i)
        if lo != hi:
            sub(o, "MinSize", lo)
            sub(o, "MaxSize", hi)
            sub(o, "SizeStep", 5)
        else:
            sub(o, "Size", lo)
        if kind == "flat":
            sub(o, "DrawOnGround")
        sub(o, "Static")
        if kind == "block":
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        objects.append(o)

    # walls
    for name, (area, face, top, style) in art.WALLS.items():
        face_img, top_img = paint_wall(area, face, top, style)
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "Class", "Wall")
        texture(o, ENV8[0], env8.add(face_img))
        t = sub(o, "Top")
        texture(t, ENV8[0], env8.add(top_img))
        sub(o, "HitSound", "monster/stone_walls_hit")
        sub(o, "DeathSound", "monster/stone_walls_death")
        for tag in ("Static", "FullOccupy", "OccupySquare", "EnemyOccupySquare", "BlocksSight"):
            sub(o, tag)
        objects.append(o)

    # monsters
    pictures = {}
    for name, (area, stand, attack) in list(art.MOBS8.items()) + list(art.QUESTS16.items()):
        size = 16 if name in art.QUESTS16 else 8
        pal = art.PALETTES[area]
        s = picture(stand, pal, size)
        a = picture(attack, pal, size) if attack else None
        pictures[name] = (area, size, s, a)

    for name, entry in MOBS.items():
        shown, hp, defence, size, flags, shots = entry[:6]
        level = entry[6] if len(entry) > 6 else None
        area, cell, s, a = pictures[name]
        sheet, sheet_name = (chars16, CHARS16[0]) if cell == 16 else (chars8, CHARS8[0])
        index = sheet.add(char_strip(s, a, cell))
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "DisplayId", shown)
        sub(o, "Class", "Character")
        sub(o, "Enemy")
        sub(o, "Group", "Realm 2 " + area)       # an area's monsters heal each other (HealGroup)
        at = sub(o, "AnimatedTexture")
        sub(at, "File", sheet_name)
        sub(at, "Index", index)
        hit, death = SOUNDS[area]
        sub(o, "HitSound", hit)
        sub(o, "DeathSound", death)
        sub(o, "Size", size)
        sub(o, "MaxHitPoints", hp)
        sub(o, "Defense", defence)
        if level:
            sub(o, "Level", level)
            sub(o, "Quest")
            sub(o, "StasisImmune")
        if "g" in flags:
            sub(o, "God")
        if "f" in flags:
            sub(o, "Flying")
        for i, (obj, damage, speed, life, extra) in enumerate(shots):
            p = sub(o, "Projectile", id=str(i))
            sub(p, "ObjectId", obj)
            sub(p, "Damage", damage)
            sub(p, "Speed", speed)
            sub(p, "LifetimeMS", life)
            if "size" in extra:
                sub(p, "Size", extra["size"])
            if "amplitude" in extra:
                sub(p, "Amplitude", extra["amplitude"])
                sub(p, "Frequency", extra["frequency"])
            if extra.get("multi"):
                sub(p, "MultiHit")
            if "effect" in extra:
                cond, dur = extra["effect"]
                sub(p, "ConditionEffect", cond, duration=str(dur))
        objects.append(o)
    missing = set(pictures) - set(MOBS)
    if missing:
        raise SystemExit("drawn but not in MOBS: %s" % sorted(missing))

    # the portal: four frames, the swirl turning a quarter each
    base = picture(art.PORTAL_FRAMES[0], art.PORTAL_PALETTE, 16)
    frames = [env16.add(base.rotate(-90 * i)) for i in range(4)]
    o = ET.Element("Object", {"type": types.get("objects", PORTAL_ID), "id": PORTAL_ID})
    sub(o, "DisplayId", PORTAL_NAME)
    sub(o, "Class", "Portal")
    sub(o, "IntergamePortal")
    sub(o, "DungeonName", PORTAL_NAME)
    texture(o, ENV16[0], frames[0])
    sub(o, "Size", 120)
    anim = sub(o, "Animation", prob="1.0")
    for i in frames:
        fr = sub(anim, "Frame", time="0.15")
        texture(fr, ENV16[0], i)
    objects.append(o)

    # the key
    o = ET.Element("Object", {"type": types.get("objects", KEY_ID), "id": KEY_ID})
    sub(o, "Class", "Equipment")
    sub(o, "Item")
    texture(o, ENV8[0], env8.add(picture(art.KEY8, art.PORTAL_PALETTE, 8)))
    sub(o, "SlotType", 10)
    sub(o, "Description", "A key woven from the eleven lands of Realm 2: opens a portal to a realm "
                          "made afresh each time, with an area for every realm event and Oryx waiting "
                          "for the last of them to fall.")
    sub(o, "Sound", "use_key")
    sub(o, "Consumable")
    sub(o, "Activate", "CreatePortal", id=PORTAL_ID)
    sub(o, "BagType", 4)
    sub(o, "Soulbound")
    objects.append(o)

    for path in OUT_XML["grounds"]:
        os.makedirs(os.path.dirname(path), exist_ok=True)
    write_xml(OUT_XML["grounds"], "GroundTypes", grounds, "Realm 2's grounds: generated by tools/build_realm2.py, don't edit")
    write_xml(OUT_XML["objects"], "Objects", objects, "Realm 2's scenery, walls, monsters, portal and key: generated by tools/build_realm2.py, don't edit")

    out = {}
    for sheet, (_, fname) in ((env8, ENV8), (env16, ENV16), (chars8, CHARS8), (chars16, CHARS16)):
        out[fname] = sheet.save(os.path.join(IMAGES, fname))
    types.save()
    print("Realm 2: %d grounds, %d objects (%d monsters)" % (len(grounds), len(objects), len(MOBS)))
    if preview:
        save_preview(out, preview)


def write_xml(paths, root_tag, elements, comment):
    root = ET.Element(root_tag)
    for e in elements:
        root.append(e)
    ET.indent(root, "\t")
    text = "<!-- %s -->\n%s\n" % (comment, ET.tostring(root, encoding="unicode"))
    for p in paths:
        with open(p, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(text)


def save_preview(sheets, path):
    """Every sheet stacked, six times bigger, on grey: to look the art over."""
    scale = 6
    imgs = [img.resize((img.width * scale, img.height * scale), Image.NEAREST) for img in sheets.values()]
    w = max(i.width for i in imgs)
    h = sum(i.height + 12 for i in imgs)
    out = Image.new("RGBA", (w, h), (90, 90, 96, 255))
    y = 0
    for img in imgs:
        out.alpha_composite(img, (0, y))
        y += img.height + 12
    out.save(path)


if __name__ == "__main__":
    args = sys.argv[1:]
    build(args[args.index("--preview") + 1] if "--preview" in args else None)
