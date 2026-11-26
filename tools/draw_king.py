"""Draws the King: his own 8 by 8 sprite, his skins sold at the Armoire (four 8 by 8 at 200 gold,
one 16 by 16 at 800), each with the dye mask that lets cloth and dyes colour it, and his scepters'
icons (the Royal Decree, tier 0 to 8).

    python tools/draw_king.py [--preview <png>]

Every figure is its own drawing, not a recolour:

  King              a small crown of three points over white hair and a long beard, a crimson
                    robe under an ermine cape flecked black, a short gold scepter set with a ruby
  Sun Pharaoh       the striped headdress falling to the shoulders, kohl-dark eyes, the narrow
                    false beard, a broad collar of turquoise and gold over white linen
  Frost Sovereign   a crown of icicles, long white hair, a robe of winter blue under white fur, a
                    scepter of ice
  The Usurper       a stolen crown sliding off black hair, red eyes in a pale face, a black robe
                    slashed with crimson, a broken iron scepter
  The Hollow King   an empty suit of armour that still holds the throne: a tarnished crown on a
                    closed helm, cold light behind the visor, a torn cloak over an old red tabard
  The Eclipse King  (16 by 16) a warrior king, broad and heavy: a solid gold crown over a dark
                    beard, an ermine mantle over plate of black steel rimmed in gold, the black sun on his
                    breastplate, a crimson tunic, a mace-scepter whose head is the black sun

The skins' effects (the Rust client's auras.rs, by their `<SkinLook>`) are kept quiet: the
Pharaoh's drifting sand, the Sovereign's few flakes of snow, the Usurper's smoke and smouldering
eyes, the Hollow King's cold mist and eyes, the Eclipse King's corona breathing round the black sun on
his chest and the motes of light it draws in.

The 8 by 8 figures are painted as the Druid's are (tools/draw_druid.py `paint`), the 16 by 16 as
the Realm Pass's skins are (tools/draw_pass_skins.py `block`), through tools/draw_monk.py
`big_block`. Dyes: a figure's `mask` says which letters (8 by 8) or materials (16 by 16) are
cloth: red the main dye, green the accent.

The scepters' icons: a scepter standing on its point, its crowned head holding a gem in the colour
its decree is drawn in (the server's `Player.DecreeColors`), the metal tin, copper, bronze, silver,
gilt, then gold (T5-T8), more ornament with each tier.

Outputs (generated, don't edit):
- the King's block (index 24) in the client's doomed/images/players.png and playersMask.png;
- KingSkins8x8.png, KingSkins16x16.png and their masks, and KingScepters16x16.png, in the
  client's custom/images, registered for the Rust client by tools/gen_rs_manifest.py
  ("kingSkins8", "kingSkins16", "kingScepters16x16");
- KingSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML).
Type codes 0xE240 on, fixed here (characters keep their skin by type: never renumber one). Needs
Pillow. `--preview` also saves the sheets as the game draws them, each dyed red and blue, and the
icons large.
"""
import math
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import draw_druid as druid  # noqa: E402
import draw_monk as monk  # noqa: E402
import draw_pass_skins as passkins  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(ASSETS, "custom", "images")
PLAYERS = os.path.join(ASSETS, "doomed", "images", "players.png")
PLAYERS_MASK = os.path.join(ASSETS, "doomed", "images", "playersMask.png")
KING_INDEX = 24
OUT_8 = os.path.join(IMAGES, "KingSkins8x8.png")
OUT_8_MASK = os.path.join(IMAGES, "KingSkinsMask8x8.png")
OUT_16 = os.path.join(IMAGES, "KingSkins16x16.png")
OUT_16_MASK = os.path.join(IMAGES, "KingSkinsMask16x16.png")
OUT_ICONS = os.path.join(IMAGES, "KingScepters16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "KingSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "KingSkins.xml")]
KING = 0xD24B
FIRST_TYPE = 0xE240


# --- the 8 by 8 figures ---------------------------------------------------------------------------
# Laid out as the classes' own sprites are (players.png): the crown or hair on top, a face five wide
# with two dark eyes, shoulders across the frame with a hand at each end, a belt, the body, the feet;
# the scepter held upright down the frame's edge (column 7, column 0 seen from behind). Skin is the
# classes' own (#ffc97a), eyes black, each material two tones. `mask`: letter -> ("main" or
# "accent", how lit: 0 lit, 1 base, 2 shadow).

SKIN = 0xFFC97A
LEGS = {"right": [".l...l..", "..lll..."], "down": [".l...l..", "..l.....", "....l..."],
        "up": ["..l...l.", ".....l..", "...l...."]}


def scepter(head="c", rod="w", hand="s", top="c"):
    """The scepter in each pose: `top` and `head` its finial, `rod` its shaft, `hand` the hand."""
    return {
        "right": {
            "weapon": [(7, 0, top), (7, 1, head), (7, 2, rod), (7, 3, rod), (7, 4, hand), (7, 5, rod)],
            "raise": [(7, 0, head), (7, 1, rod), (7, 2, rod), (7, 3, hand), (7, 4, rod)],
            "strike": [(6, 4, hand), (7, 4, rod), (8, 4, rod), (9, 4, rod), (10, 3, top), (10, 4, head),
                       (10, 5, top), (11, 4, top)],
        },
        "down": {
            "weapon": [(7, 1, top), (7, 2, head), (7, 3, rod), (7, 4, hand), (7, 5, rod)],
            "raise": [(7, 0, top), (7, 1, head), (7, 2, rod), (7, 3, hand), (7, 4, rod)],
            "strike": [(7, 4, hand), (7, 5, rod), (7, 6, head), (7, 7, top), (6, 7, top)],
        },
        "up": {
            "weapon": [(0, 1, top), (0, 2, head), (0, 3, rod), (0, 4, hand), (0, 5, rod)],
            "raise": [(0, 0, top), (0, 1, head), (0, 2, rod), (0, 3, hand), (0, 4, rod)],
            "strike": [(0, 3, hand), (0, 2, rod), (0, 1, head), (0, 0, top), (1, 0, top)],
        },
    }


def figure(name, look, colors, mask, right, down, up, weapon=None, legs=None):
    w = weapon or scepter()
    lg = legs or LEGS
    out = {"name": name, "look": look, "colors": dict(colors, s=colors.get("s", SKIN), g=0x000000),
           "mask": mask}
    for way, body in (("right", right), ("down", down), ("up", up)):
        assert len(body) == 7 and all(len(r) == 8 for r in body), (name, way, body)
        out[way] = dict(w[way], body=body, legs=lg[way])
    return out


# The King: a crown of three points with a ruby, white hair and a full white beard, an ermine
# collar over a crimson robe, a gold belt, a gold scepter tipped with a ruby.
KING_LOOK = figure(
    "King", None,
    {"k": 0xFFC830, "K": 0xC08A18, "j": 0xE0203C, "h": 0xF2F2F2, "H": 0xC8C8C8, "f": 0xFFFFFF,
     "r": 0xC81E32, "R": 0x8C1424, "l": 0x4A2A1A, "w": 0xD89A20, "c": 0xE0203C},
    {"r": ("main", 1), "R": ("main", 2)},
    [".k.k.k..",
     ".kkkjk..",
     ".hsgsg..",
     ".hhhhh..",
     "rrfffrr.",
     "s.KkK...",
     ".rrrRr.."],
    [".k.k.k..",
     ".kkjkk..",
     ".sgsgs..",
     ".hhhhh..",
     "rrfffrr.",
     "s.KkK...",
     ".rRrRr.."],
    ["..k.k.k.",
     "..kkkkk.",
     "..hhhhh.",
     "..HhhhH.",
     ".rrfffrr",
     "...KkK.s",
     "..rRrRr."],
)

# The Sun Pharaoh: the striped headdress of gold and lapis falling to the shoulders, the narrow gold
# false beard, a broad collar of turquoise, bare arms, a gold belt over white linen, the crook.
PHARAOH = figure(
    "Sun Pharaoh", "pharaoh",
    {"n": 0xF0C040, "N": 0x2848B0, "b": 0xF0C040, "t": 0x30C0B0, "T": 0x1A8070, "k": 0xF0C040,
     "r": 0xFFFFFF, "R": 0xD0CCC0, "l": 0xC08A50, "w": 0x2848B0, "c": 0xF0C040},
    {"r": ("main", 0), "R": ("main", 2), "t": ("accent", 1), "T": ("accent", 2)},
    ["..nnn...",
     ".NNNNN..",
     ".nsgsg..",
     ".Nssbs..",
     "stttTts.",
     "s.kkk...",
     ".rrrRr.."],
    ["..nnn...",
     ".NNNNN..",
     "nsgsgsn.",
     "N.sbs.N.",
     "stttTts.",
     "s.kkk...",
     ".rrrRr.."],
    ["...nnn..",
     "..NNNNN.",
     "..nnnnn.",
     "..NNNNN.",
     ".stttts.",
     "...kkk.s",
     "..rrrRr."],
    weapon=scepter(head="c", rod="w", top="c"),
    legs={"right": [".l...l..", "..lll..."], "down": [".l...l..", "..l.....", "....l..."],
          "up": ["..l...l.", ".....l..", "...l...."]},
)

# The Frost Sovereign: a crown of icicles, long white hair down the back, pale skin, white fur at
# the collar, a robe of winter blue, a scepter of ice.
FROST = figure(
    "Frost Sovereign", "frost",
    {"i": 0xC8F0FF, "I": 0x6CB4E8, "s": 0xFFE4D6, "h": 0xF4F8FF, "H": 0xC0CCE0, "f": 0xFFFFFF,
     "r": 0x3C78D0, "R": 0x24488C, "l": 0x24304C, "w": 0xA0DCFF, "c": 0xFFFFFF},
    {"r": ("main", 1), "R": ("main", 2), "f": ("accent", 0)},
    [".i.I.i..",
     ".iIiIi..",
     ".hsgsg..",
     ".hssss..",
     "rrfffrr.",
     "s.rRr...",
     ".hrrRr.."],
    [".i.I.i..",
     ".iIiIi..",
     "hsgsgsh.",
     "hsssssh.",
     "rrfffrr.",
     "s.rRr...",
     ".rrrRr.."],
    ["..i.I.i.",
     "..iIiIi.",
     "..hhhhh.",
     "..hHhHh.",
     ".rrhhhrr",
     "...hHh.s",
     "..rrhRr."],
)

# The Usurper: a stolen crown slipping off black hair, red eyes, a black robe slashed with crimson
# from shoulder to hem, a broken iron scepter.
USURPER = figure(
    "The Usurper", "usurper",
    {"k": 0xD8B040, "K": 0x8C6C20, "a": 0x202020, "e": 0xFF2030, "r": 0x302838, "R": 0x18141C,
     "t": 0xC01830, "l": 0x101010, "w": 0x808898, "c": 0xC01830},
    {"r": ("main", 1), "R": ("main", 2), "t": ("accent", 1)},
    ["...kKk..",
     ".aakkk..",
     ".asese..",
     ".aasss..",
     "rrtrrrr.",
     "s.rtr...",
     ".rrrtR.."],
    ["..kKk...",
     ".akkkaa.",
     ".seses..",
     ".asssa..",
     "rrtrrrr.",
     "s.rtr...",
     ".rRrtr.."],
    ["...kKk..",
     "..akkka.",
     "..aaaaa.",
     "..aaaaa.",
     ".rrrrtrr",
     "...rt..s",
     "..rRtrr."],
    weapon=scepter(head="w", rod="w", top="c"),
)

# The Hollow King: an empty suit of armour that still holds the throne, a tarnished crown on a
# closed helm, cold light for eyes behind the visor, an old red tabard and a ragged cloak.
HOLLOW = figure(
    "The Hollow King", "hollow",
    {"k": 0xB09848, "a": 0xB8C0CC, "A": 0x707888, "v": 0x181C24, "e": 0x80E8FF, "t": 0x8C2030,
     "T": 0x581420, "r": 0x48506A, "l": 0x40465A, "w": 0x9098A8, "c": 0x80E8FF},
    {"r": ("main", 1), "t": ("accent", 1), "T": ("accent", 2)},
    [".k.k.k..",
     ".kkkkk..",
     ".Aveve..",
     ".AAaaa..",
     "AAtttAA.",
     "a.tTt...",
     ".rtttr.."],
    [".k.k.k..",
     ".kkkkk..",
     ".vevev..",
     ".AaaaA..",
     "AAtttAA.",
     "a.tTt...",
     ".rtttr.."],
    ["..k.k.k.",
     "..kkkkk.",
     "..aaaaa.",
     "..AaaaA.",
     ".AArrrAA",
     "...rrr.a",
     "..rr.rr."],
    weapon=scepter(head="c", rod="w", hand="a", top="w"),
)

SMALL = [PHARAOH, FROST, USURPER, HOLLOW]


# --- the Eclipse King, 16 by 16 ----------------------------------------------------------------
# A warrior king, broad and heavy where the other great skins are tall and robed, drawn at the
# classes' proportions (a head half the figure's width): a solid gold crown set with a ruby over a
# dark beard, an ermine mantle over plate of black steel rimmed in gold, the black sun on his
# breastplate (a ring of gold round a dark heart), a crimson tunic, and a mace-scepter whose head is
# the black sun again. Its aura (auras.rs) is the sun's corona breathing round that emblem.

ECLIPSE_MATERIALS = {
    # black steel plate
    "a": [(160, 160, 190), (96, 96, 126), (58, 58, 84), (28, 28, 46)],
    # the ermine mantle over his shoulders (flecked with "d")
    "f": [(255, 255, 255), (236, 234, 240), (190, 188, 202), (120, 118, 134)],
    # the tunic (the main dye)
    "c": [(236, 96, 96), (180, 40, 52), (118, 22, 38), (64, 10, 22)],
    # gold: the crown, the rims, the black sun's ring (the accent)
    "p": [(255, 238, 150), (240, 192, 64), (172, 128, 36), (104, 72, 20)],
    # face and hands
    "s": [(255, 230, 180), (255, 201, 122), (206, 150, 86), (140, 96, 52)],
    # the dark beard and hair
    "b": [(126, 94, 72), (84, 58, 42), (54, 36, 26), (28, 18, 14)],
    # boots, and the eyes
    "d": [(60, 54, 66), (34, 30, 40), (22, 20, 28), (10, 8, 14)],
}
ECLIPSE_GLOWS = {
    "j": (240, 50, 74),
    "y": (255, 236, 190),
    "x": (255, 255, 255),
}
ECLIPSE_MASK = {"c": "main", "p": "accent"}


def black_sun(x, y):
    """The black sun, 3 by 3 with its top-left at (x, y): a ring of gold round a dark heart."""
    return [(x, y, "ppp"), (x, y + 1, "pdp"), (x, y + 2, "ppp")]


ECLIPSE = {
    "right": {
        "stand": [
            ".....p..p..p....",
            ".....pppppjp....",
            ".....pppppppp...",
            ".....bbssssss...",
            ".....bbssssds...",
            ".....bbssssss...",
            ".....bbbbbbbb...",
            "......fbbbbbb...",
            ".....ffdbbbb....",
            "....ffaaaapp....",
            "....faaaaaap....",
            "....aaaaaaap....",
            "....aaaaaaa.....",
            "....cccccccc....",
            "....ccccccccc...",
            ".....ddd..ddd...",
        ],
        "hold": black_sun(13, 1) + [(14, y, "a") for y in range(4, 12)] + [(12, 10, "ss")],
        "raise": black_sun(13, 0) + [(14, y, "a") for y in range(3, 9)] + [(12, 8, "ss")],
        "strike": [(12, 10, "ssaaa")] + black_sun(17, 9) + [(21, 10, "y"), (22, 10, "x")],
        "steps": [
            ["....ccccccccc...", "....dd.....dd..."],
            ["....ccccccccc...", ".....ddddd......"],
        ],
    },
    "down": {
        "stand": [
            "....p..pp..p....",
            "....pppjjppp....",
            "....pppppppp....",
            "....bssssssb....",
            "....bsdssdsb....",
            "....bssssssb....",
            "....bbbbbbbb....",
            "..ff.bbbbbb.ff..",
            ".fffdfbbbbfdfff.",
            ".ffaaaappaaaaff.",
            ".ss.aapdapaa.ss.",
            "....aapddpaa....",
            "....aaappaaa....",
            "....cccccccc....",
            "...cccc..cccc...",
            "...ddd....ddd...",
        ],
        "hold": black_sun(13, 1) + [(14, y, "a") for y in range(4, 10)],
        "raise": black_sun(13, 0) + [(14, y, "a") for y in range(3, 9)] + [(14, 9, "s")],
        "strike": [(14, 11, "a"), (14, 12, "a")] + black_sun(13, 13),
        "steps": [
            ["...cccc..cccc...", "...dd......dd..."],
            ["...cccc..cccc...", "....dd....dd...."],
        ],
    },
    "up": {
        "stand": [
            "....p..pp..p....",
            "....pppppppp....",
            "....pppppppp....",
            "....bbbbbbbb....",
            "....bbbbbbbb....",
            "....bbbbbbbb....",
            ".....bbbbbb.....",
            "..ff.ffffff.ff..",
            ".fffdffffffdfff.",
            ".ffaaaaaaaaaaff.",
            ".ss.aaaaaaaa.ss.",
            "....aaaaaaaa....",
            "....aaaaaaaa....",
            "....cccccccc....",
            "...cccc..cccc...",
            "...ddd....ddd...",
        ],
        "hold": black_sun(0, 1) + [(1, y, "a") for y in range(4, 10)],
        "raise": black_sun(0, 0) + [(1, y, "a") for y in range(3, 9)] + [(1, 9, "s")],
        "strike": [(1, 9, "a"), (1, 8, "a")] + black_sun(0, 5) + [(1, 4, "y"), (1, 2, "x")],
        "steps": [
            ["...cccc..cccc...", "...dd......dd..."],
            ["...cccc..cccc...", "....dd....dd...."],
        ],
    },
}

for _way, _d in ECLIPSE.items():
    assert len(_d["stand"]) == 16 and all(len(r) == 16 for r in _d["stand"]), (_way, [len(r) for r in _d["stand"]])
    assert all(len(r) == 16 for s in _d["steps"] for r in s), _way

BIG = [(ECLIPSE, ECLIPSE_MATERIALS, ECLIPSE_GLOWS, ECLIPSE_MASK)]


def big_sheet(mask=False):
    out = Image.new("RGBA", (passkins.CELL * 7, passkins.CELL * 3 * len(BIG)), (0, 0, 0, 0))
    for i, (look, materials, glows, dyes) in enumerate(BIG):
        out.paste(monk.big_block(look, materials, glows, dyes, mask), (0, i * passkins.CELL * 3))
    return out


# --- the scepters' icons -------------------------------------------------------------------------

ICON = 16
# the decree's colour by tier (the server's Player.DecreeColors)
DECREE_COLORS = [0xB8C0C8, 0xE0885A, 0xD8A050, 0xE8F0FF, 0xFFD050, 0xFFA830, 0xFFE890, 0xB070FF, 0xFF4A5A]
GOLD = ((255, 246, 170), (236, 186, 60), (150, 102, 28))
# the metal by tier (lit, base, shadow): tin, copper, bronze, silver, gilt, then gold
METALS = [
    ((214, 218, 224), (150, 156, 166), (92, 96, 108)),
    ((244, 168, 116), (192, 102, 60), (118, 58, 34)),
    ((236, 190, 110), (178, 126, 58), (110, 72, 30)),
    ((252, 252, 255), (196, 204, 218), (128, 136, 156)),
    GOLD, GOLD, GOLD, GOLD, GOLD,
]
# the head: a cross over an orb set with the gem; 'l' lit, 'm' metal, 'd' shadow, 'G' the gem,
# 'H' its light
HEAD = [
    "...l...",
    "..lmd..",
    "...m...",
    ".lmmmd.",
    "lmHGGmd",
    "lmGGGmd",
    ".mmmmd.",
    "..mmd..",
]
HEAD_AT = (8, 0)


def icon(tier):
    img = Image.new("RGBA", (ICON, ICON), (0, 0, 0, 0))
    gem = monk.hexc(DECREE_COLORS[tier])
    gem_deep = monk.mixc(gem, (20, 10, 30), 0.45)
    gem_light = monk.mixc(gem, (255, 255, 255), 0.65)
    lit, base, shadow = METALS[tier]
    # a glow round the head from tier 7
    if tier >= 7:
        for y in range(ICON):
            for x in range(ICON):
                d = math.hypot(x - 11, y - 4.5)
                if d < 5.0:
                    img.putpixel((x, y), gem + (int(110 * (1 - d / 5.0)),))
    # the rod, from under the head down to the left, lit on its upper side
    for i in range(7):
        x, y = 10 - i, 8 + i
        img.putpixel((x, y), lit + (255,))
        if x + 1 < ICON:
            img.putpixel((x + 1, y), shadow + (255,))
    # the grip, wound in the decree's colour from tier 3, plain leather below
    grip = gem_deep if tier >= 3 else (96, 62, 40)
    for i in (4, 5):
        img.putpixel((10 - i, 8 + i), grip + (255,))
    # the pommel
    for x, y, c in ((2, 15, base), (3, 15, shadow), (3, 14, lit)):
        img.putpixel((x, y), c + (255,))
    # a second gem on the rod from tier 5, a band of gold below the head from tier 4
    if tier >= 4:
        img.putpixel((10, 8), GOLD[0] + (255,))
        img.putpixel((11, 8), GOLD[2] + (255,))
    if tier >= 5:
        img.putpixel((7, 11), gem + (255,))
        img.putpixel((8, 11), gem_deep + (255,))
    # the head
    hx, hy = HEAD_AT
    shades = {"l": lit, "m": base, "d": shadow, "G": gem, "H": gem_light}
    for y, row in enumerate(HEAD):
        for x, ch in enumerate(row):
            if ch != ".":
                c = shades[ch]
                # the lowest tiers' gem is dull
                if ch in "GH" and tier < 2:
                    c = monk.mixc(c, (120, 120, 120), 0.4)
                img.putpixel((hx + x, hy + y), c + (255,))
    # the cross and the orb's sides set with pearls from tier 6
    if tier >= 6:
        for x, y in ((11, 0), (8, 4), (14, 4)):
            img.putpixel((x, y), (255, 255, 255, 255))
    # sparkles at tier 8
    if tier >= 8:
        for x, y in ((4, 3), (14, 10)):
            img.putpixel((x, y), (255, 255, 255, 255))
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if 0 <= x + dx < ICON and 0 <= y + dy < ICON and img.getpixel((x + dx, y + dy))[3] == 0:
                    img.putpixel((x + dx, y + dy), gem_light + (200,))
    return img


# --- the sheets ----------------------------------------------------------------------------------

# name, gold, sheet, index, size, look
SKINS = [(s["name"], 200, "kingSkins8", i, None, s["look"]) for i, s in enumerate(SMALL)] + [
    ("The Eclipse King", 800, "kingSkins16", 0, passkins.SIZE, "eclipse")]


def put_block(path, block):
    """The King's block in a player sheet, replacing what was there."""
    sheet = Image.open(path).convert("RGBA")
    y = KING_INDEX * 24
    sheet.paste(Image.new("RGBA", (56, 24), (0, 0, 0, 0)), (0, y))
    sheet.paste(block, (0, y))
    sheet.save(path)


def main():
    king, king_mask = druid.paint(KING_LOOK), druid.paint(KING_LOOK, mask=True)
    put_block(PLAYERS, king)
    put_block(PLAYERS_MASK, king_mask)
    small = Image.new("RGBA", (56, 24 * len(SMALL)), (0, 0, 0, 0))
    small_mask = small.copy()
    for i, skin in enumerate(SMALL):
        small.paste(druid.paint(skin), (0, i * 24))
        small_mask.paste(druid.paint(skin, mask=True), (0, i * 24))
    small.save(OUT_8)
    small_mask.save(OUT_8_MASK)
    big, big_mask = big_sheet(), big_sheet(mask=True)
    big.save(OUT_16)
    big_mask.save(OUT_16_MASK)
    icons = Image.new("RGBA", (ICON * 9, ICON), (0, 0, 0, 0))
    for t in range(9):
        icons.paste(icon(t), (t * ICON, 0))
    icons.save(OUT_ICONS)
    root = ET.Element("Objects")
    for i, (name, gold, sheet, index, size, look) in enumerate(SKINS):
        o = ET.SubElement(root, "Object", {"type": "0x%04X" % (FIRST_TYPE + i), "id": name})
        druid.sub(o, "DisplayId", name)
        druid.sub(o, "Skin")
        druid.sub(o, "Class", "Skin")
        at = druid.sub(o, "AnimatedTexture")
        druid.sub(at, "File", sheet)
        druid.sub(at, "Index", index)
        druid.sub(o, "PlayerClassType", "0x%04x" % KING)
        druid.sub(o, "UnlockLevel", 0)
        druid.sub(o, "Cost", gold)
        druid.sub(o, "SkinLook", look)
        if size:
            druid.sub(o, "Size", size)
    ET.indent(root, "\t")
    text = ("<!-- The King's skins sold at the Armoire: generated by tools/draw_king.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"King, {len(SKINS)} skins and 9 scepters")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        root_, ext = os.path.splitext(path)
        sheet = Image.new("RGBA", (56, 24 * (len(SMALL) + 1)), (0, 0, 0, 0))
        sheet.paste(king, (0, 0))
        sheet.paste(small, (0, 24))
        sheet_mask = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        sheet_mask.paste(king_mask, (0, 0))
        sheet_mask.paste(small_mask, (0, 24))
        wide = Image.new("RGBA", (56 * 3 + 16, sheet.height), (0, 0, 0, 0))
        wide.paste(sheet, (0, 0))
        wide.paste(druid.dyed(sheet, sheet_mask, (200, 40, 40), (240, 200, 60)), (64, 0))
        wide.paste(druid.dyed(sheet, sheet_mask, (50, 90, 220), (240, 240, 250)), (128, 0))
        passkins.preview(wide, root_ + "-8" + ext, scale=8)
        wide16 = Image.new("RGBA", (big.width * 3 + 32, big.height), (0, 0, 0, 0))
        wide16.paste(big, (0, 0))
        wide16.paste(druid.dyed(big, big_mask, (200, 40, 40), (240, 200, 60)), (big.width + 16, 0))
        wide16.paste(druid.dyed(big, big_mask, (50, 90, 220), (240, 240, 250)), (2 * big.width + 32, 0))
        passkins.preview(wide16, root_ + "-16" + ext, scale=5)
        passkins.preview(icons, root_ + "-icons" + ext, scale=10)


if __name__ == "__main__":
    main()
