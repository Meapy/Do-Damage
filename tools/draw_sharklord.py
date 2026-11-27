"""Draws the Sharklord: their own 8 by 8 sprite, their skins sold at the Armoire (four 8 by 8 at 200
gold, one 16 by 16 at 800), each with the dye mask that lets cloth and dyes colour it, and the
oceanic spells' icons (the Maw of the Deep, tier 0 to 8).

    python tools/draw_sharklord.py [--preview <png>]

Every figure is its own drawing, not a recolour:

  Sharklord         a shark's head on a man's shoulders: a dorsal fin, a black eye, a white grin of
                    teeth, a sea-green robe tied with a belt of gold, a driftwood staff crowned
                    with a great white tooth
  Hammerhead        the hammer-shaped head reaching past the shoulders, an eye at each end, a sandy
                    hide, a sash of ochre over a dark robe
  Tiger Shark       a striped hide of grey and rust, a scarred snout, an orange robe slashed dark,
                    a staff of rust-red coral
  Lanternshark      black as the deep, rows of glowing blue spots down the hide, pale eyes that
                    shine, a robe of abyss blue, a staff hung with a lantern
  Whale Shark       a broad flat head of deep blue dotted white, a gentle wide mouth, a robe of
                    sea foam, a staff of white coral
  The Megalodon     (16 by 16) the ancient king of the sea: a great slate head under a crown of
                    teeth, red eyes, jaws open on row after row of teeth, a scarred white chest,
                    a mantle of kelp over a coral sash, a bone trident

The skins' effects (the Rust client's auras.rs, by their `<SkinLook>`): the Hammerhead's rings of
sense pulsing out over the ground, the Tiger Shark's bubbles, the Lanternshark's spots glowing and
the motes of light drifting off it, the Whale Shark's little pilot fish circling it, and the
Megalodon's dark water churning under it, a great fin's shadow circling it and its eyes burning.

The 8 by 8 figures are painted as the King's are (tools/draw_king.py, through tools/draw_druid.py
`paint`), the 16 by 16 as the Realm Pass's skins are (tools/draw_pass_skins.py `block`, through
tools/draw_monk.py `big_block`). Dyes: a figure's `mask` says which letters (8 by 8) or materials
(16 by 16) are cloth: red the main dye, green the accent.

The icons: the spells' teeth and jaws over a whirl of the water the shark rises from, in the
colour its Maw is drawn in (the server's `Player.MawColors`): a single tooth growing from the
Reef Tooth (T0) to the Hammerhead Fang (T3), a jaw of teeth from the Bull Shark Jaw (T4) to the
Great White Jaw (T6), the Jaws of the Abyss (T7) open in a violet glow, and the Megalodon Tooth (T8)
huge and old, its root dark, a glow of blood round it.

Outputs (generated, don't edit):
- the Sharklord's block (index 20) in the client's doomed/images/players.png and playersMask.png;
- SharklordSkins8x8.png, SharklordSkins16x16.png and their masks, and SharklordMaws16x16.png, in
  the client's custom/images, registered for the Rust client by tools/gen_rs_manifest.py
  ("sharklordSkins8", "sharklordSkins16", "sharklordMaws16x16");
- SharklordSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML).
Type codes 0xE250 on, fixed here (characters keep their skin by type: never renumber one). Needs
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
SHARKLORD_INDEX = 20
OUT_8 = os.path.join(IMAGES, "SharklordSkins8x8.png")
OUT_8_MASK = os.path.join(IMAGES, "SharklordSkinsMask8x8.png")
OUT_16 = os.path.join(IMAGES, "SharklordSkins16x16.png")
OUT_16_MASK = os.path.join(IMAGES, "SharklordSkinsMask16x16.png")
OUT_ICONS = os.path.join(IMAGES, "SharklordMaws16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "SharklordSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "SharklordSkins.xml")]
SHARKLORD = 0xD24E
FIRST_TYPE = 0xE250


# --- the 8 by 8 figures ---------------------------------------------------------------------------
# Laid out as the classes' own sprites are (players.png): the fin on top, the head with its eye and
# its teeth, shoulders across the frame with a hand at each end, a belt, the robe, the feet; the
# staff held upright down the frame's edge (column 7, column 0 seen from behind). Each material two
# tones. `mask`: letter -> ("main" or "accent", how lit: 0 lit, 1 base, 2 shadow).

LEGS = {"right": [".l...l..", "..lll..."], "down": [".l...l..", "..l.....", "....l..."],
        "up": ["..l...l.", ".....l..", "...l...."]}


def staff(head="c", rod="w", hand="a", top="c"):
    """The staff in each pose: `top` and `head` its crown, `rod` its shaft, `hand` the hand."""
    return {
        "right": {
            "weapon": [(7, 0, top), (7, 1, head), (7, 2, rod), (7, 3, rod), (7, 4, hand), (7, 5, rod)],
            "raise": [(7, 0, head), (7, 1, rod), (7, 2, rod), (7, 3, hand), (7, 4, rod)],
            "strike": [(6, 4, hand), (7, 4, rod), (8, 4, rod), (9, 4, rod), (10, 4, head), (11, 4, top)],
        },
        "down": {
            "weapon": [(7, 1, top), (7, 2, head), (7, 3, rod), (7, 4, hand), (7, 5, rod)],
            "raise": [(7, 0, top), (7, 1, head), (7, 2, rod), (7, 3, hand), (7, 4, rod)],
            "strike": [(7, 4, hand), (7, 5, rod), (7, 6, head), (7, 7, top)],
        },
        "up": {
            "weapon": [(0, 1, top), (0, 2, head), (0, 3, rod), (0, 4, hand), (0, 5, rod)],
            "raise": [(0, 0, top), (0, 1, head), (0, 2, rod), (0, 3, hand), (0, 4, rod)],
            "strike": [(0, 3, hand), (0, 2, rod), (0, 1, head), (0, 0, top)],
        },
    }


def figure(name, look, colors, mask, right, down, up, weapon=None, legs=None):
    w = weapon or staff()
    lg = legs or LEGS
    out = {"name": name, "look": look, "colors": dict(colors, g=colors.get("g", 0x000000)), "mask": mask}
    for way, body in (("right", right), ("down", down), ("up", up)):
        assert len(body) == 7 and all(len(r) == 8 for r in body), (name, way, body)
        out[way] = dict(w[way], body=body, legs=lg[way])
    return out


# The Sharklord: a grey-blue shark's head with its fin, a black eye, a red mouth full of white
# teeth, the white of the belly at the throat, a sea-green robe and a gold belt, the tail behind,
# a driftwood staff crowned with a great white tooth.
SHARKLORD_LOOK = figure(
    "Sharklord", None,
    {"a": 0x6A96C0, "A": 0x3C6088, "f": 0x2C4A70, "w": 0xF0F4F8, "m": 0xA01828, "k": 0xE8B830,
     "r": 0x2E9C88, "R": 0x1C6458, "l": 0x24384C, "d": 0x9A7048, "c": 0xFFFFFF},
    {"r": ("main", 1), "R": ("main", 2), "k": ("accent", 1)},
    ["...f....",
     "..faaa..",
     ".aaaaga.",
     ".aAwmwm.",
     "rrrwwrr.",
     "a.kkk...",
     "AfrrRr.."],
    ["...f....",
     ".aafaa..",
     ".gaaag..",
     ".wmwmw..",
     "rrwwwrr.",
     "a.kkk...",
     ".rRrRr.."],
    ["....f...",
     "..aafaa.",
     "..aaAaa.",
     "..AaaaA.",
     ".rrrrrrr",
     "...kkk.a",
     "..rRfRr."],
    weapon=staff(head="c", rod="d", hand="a", top="c"),
)

# The Hammerhead: the wide hammer of a head reaching past the shoulders, an eye at each end, a
# sandy hide, a robe of dark sea-brown with an ochre sash.
HAMMERHEAD = figure(
    "Hammerhead", "hammerhead",
    {"a": 0xC8B48C, "A": 0x8C7858, "f": 0x7C6848, "w": 0xF4EEDC, "m": 0x802030, "k": 0xE09A30,
     "K": 0xA06818, "r": 0x4A3C34, "R": 0x2C2420, "l": 0x2A2018, "d": 0x6C5440, "c": 0xF4EEDC},
    {"r": ("main", 1), "R": ("main", 2), "k": ("accent", 1), "K": ("accent", 2)},
    ["...f....",
     "gaaaaaag",
     "AAaaaaA.",
     "..awm...",
     "rkrrrrr.",
     "a.rkr...",
     ".rrRkr.."],
    ["...f....",
     "gaaaaaag",
     "AAaaaaAA",
     "..wmw...",
     "rkrwwrr.",
     "a.rkr...",
     ".rRrkr.."],
    ["....f...",
     "aaaaaaaa",
     "AAaaaaAA",
     "...AaA..",
     ".rrrrrkr",
     "...rkr.a",
     "..rRrkr."],
    weapon=staff(head="c", rod="d", hand="a", top="c"),
)

# The Tiger Shark: a hide of grey striped rust, a scarred snout, an orange robe slashed dark, a
# staff of rust-red coral.
TIGER = figure(
    "Tiger Shark", "tiger",
    {"a": 0x9AA0A8, "s": 0x8C4A28, "A": 0x5C626C, "f": 0x5C626C, "w": 0xF0F0EA, "m": 0x901828,
     "k": 0x2A2A2A, "r": 0xE07828, "R": 0xA04C14, "l": 0x302820, "d": 0xC04030, "c": 0xFF7050},
    {"r": ("main", 1), "R": ("main", 2), "k": ("accent", 2)},
    ["...f....",
     "..fsas..",
     ".asasga.",
     ".aAwmwm.",
     "rkrwwrk.",
     "a.kkk...",
     "AfrkRr.."],
    ["...f....",
     ".asfsa..",
     ".gsasg..",
     ".wmwmw..",
     "rkwwwkr.",
     "a.kkk...",
     ".rkrRk.."],
    ["....f...",
     "..safas.",
     "..asAsa.",
     "..AsasA.",
     ".rkrrrkr",
     "...kkk.a",
     "..rkfkr."],
    weapon=staff(head="c", rod="d", hand="a", top="c"),
)

# The Lanternshark: black as the deep, glowing blue spots down the hide, pale shining eyes, a robe
# of abyss blue, a staff hung with a lantern.
LANTERN = figure(
    "Lanternshark", "lantern",
    {"a": 0x1E2430, "A": 0x10141C, "f": 0x10141C, "o": 0x50D8FF, "g": 0xC8F4FF, "w": 0xB8C8D8,
     "m": 0x2C0C1C, "k": 0x3A4658, "r": 0x243C78, "R": 0x142448, "l": 0x0C1018, "d": 0x384048,
     "c": 0x80F0FF},
    {"r": ("main", 1), "R": ("main", 2)},
    ["...f....",
     "..faoa..",
     ".aoaaga.",
     ".aAwmwm.",
     "rrorrro.",
     "a.kkk...",
     "AfrrRr.."],
    ["...f....",
     ".aafaa..",
     ".gaoag..",
     ".wmwmw..",
     "rroaorr.",
     "a.kkk...",
     ".rRrRr.."],
    ["....f...",
     "..aofoa.",
     "..aoAoa.",
     "..AaoaA.",
     ".rrrrrrr",
     "...kkk.a",
     "..rRfRr."],
    weapon=staff(head="k", rod="d", hand="a", top="c"),
)

# The Whale Shark: a broad flat head of deep blue dotted with white, a wide gentle mouth, a robe of
# sea foam, a staff of white coral.
WHALE = figure(
    "Whale Shark", "whale",
    {"a": 0x2C5C9C, "A": 0x1C3C70, "f": 0x1C3C70, "o": 0xE8F4FF, "w": 0xDCE8F0, "m": 0x1A2440,
     "k": 0x3CB8C8, "r": 0xC8ECF0, "R": 0x88C0D0, "l": 0x1C2C48, "d": 0xE8E8E0, "c": 0xFFFFFF},
    {"r": ("main", 0), "R": ("main", 2), "k": ("accent", 1)},
    ["...f....",
     ".aoaoaa.",
     "aaoaaoga",
     "aAmmmmmm",
     "rrrwwrr.",
     "a.kkk...",
     "AfrrRr.."],
    ["...f....",
     ".aoafoa.",
     "agoaaog.",
     "ammmmmma",
     "rrwwwwr.",
     "a.kkk...",
     ".rRrRr.."],
    ["....f...",
     ".aoafoaa",
     ".aaoAoaa",
     "..oaoao.",
     ".rrrrrrr",
     "...kkk.a",
     "..rRfRr."],
    weapon=staff(head="c", rod="d", hand="a", top="c"),
)

SMALL = [HAMMERHEAD, TIGER, LANTERN, WHALE]


# --- the Megalodon, 16 by 16 -----------------------------------------------------------------------
# The ancient king of the sea, drawn at the classes' proportions (a great head, the body under it):
# a crown of teeth on a slate head, a burning red eye, the jaws hanging open on rows of teeth, the
# white of its chest scarred, a mantle of kelp (the main dye) over a sash of coral (the accent),
# its tail sweeping behind, a trident of bone. Its aura (auras.rs) is dark water churning under it,
# a great fin's shadow circling it and the eye's glow.

MEGALODON_MATERIALS = {
    # the slate hide
    "h": [(150, 172, 200), (94, 114, 144), (60, 74, 100), (32, 40, 58)],
    # the white of the belly and the teeth
    "w": [(255, 255, 252), (236, 236, 226), (190, 188, 174), (124, 122, 110)],
    # the kelp mantle (the main dye)
    "k": [(122, 204, 150), (56, 142, 104), (32, 92, 70), (16, 52, 40)],
    # the coral sash (the accent)
    "c": [(255, 156, 132), (232, 92, 80), (164, 52, 50), (98, 26, 32)],
    # bone: the crown and the trident
    "b": [(252, 246, 222), (222, 208, 172), (162, 146, 112), (102, 90, 66)],
    # the inside of the jaws
    "m": [(160, 44, 56), (108, 22, 36), (68, 12, 24), (38, 6, 14)],
    # the feet
    "d": [(60, 66, 82), (36, 40, 54), (22, 26, 36), (10, 12, 18)],
}
MEGALODON_GLOWS = {
    "e": (255, 70, 80),
    "x": (255, 255, 255),
    "q": (150, 236, 255),
}
MEGALODON_MASK = {"k": "main", "c": "accent"}


def trident(x, top, length):
    """A bone trident standing at column x, its three prongs from row `top`."""
    parts = [(x - 1, top, "b.b"), (x - 1, top + 1, "bbb")]
    for y in range(top + 2, top + length):
        parts.append((x, y, "b"))
    return parts


MEGALODON = {
    "right": {
        "stand": [
            "...h..b.b.b.....",
            "...hhhbbbbb.....",
            "....hhhhhhhhh...",
            "....hhhhhhhhhh..",
            "....hhhhhhhehhh.",
            "....hhhhhwmwmwm.",
            "....hhhhhmmmmmm.",
            ".....hhhwmwmwm..",
            "....kkhhwwwww...",
            "...kkkhhwwwwh...",
            "..hkkkhhwWwwh...",
            ".hhkkcccccccc...",
            "hh.kkkkhhhhh....",
            "...kkkkkkkkk....",
            "...kkkkkkkkkk...",
            "....ddd...ddd...",
        ],
        "hold": trident(15, 0, 12) + [(13, 9, "hh")],
        "raise": trident(15, 0, 9)[:2] + [(15, y, "b") for y in range(2, 9)] + [(13, 8, "hh")],
        "strike": [(12, 9, "hhbbbb")] + [(18, 8, "b.."), (18, 9, "bbb"), (18, 10, "b.."), (21, 9, "x")],
        "steps": [
            ["...kkkkkkkkkk...", "...dd......dd..."],
            ["...kkkkkkkkkk...", "....ddddddd....."],
        ],
    },
    "down": {
        "stand": [
            "......b.bb.b....",
            "......bbbbbb....",
            ".......hhhh.....",
            "....hhhhhhhhhh..",
            "...hehhhhhhhheh.",
            "...hhhhhhhhhhhh.",
            "...hwmwmwmwmwmh.",
            "....mmmmmmmmmm..",
            "....wmwmwmwmwm..",
            "..kk.hwwwwwwh.kk",
            ".kkkkhwwWwwwhkkk",
            ".hh.kcccccccckhh",
            "....kkhhhhhhkk..",
            "....kkkkkkkkkk..",
            "...kkkkk..kkkkk.",
            "...ddd......ddd.",
        ],
        "hold": trident(15, 0, 9),
        "raise": trident(15, 0, 8),
        "strike": [(15, 9, "b"), (15, 10, "b"), (15, 11, "b"), (14, 12, "bbb"), (14, 13, "b.b")],
        "steps": [
            ["...kkkkk..kkkkk.", "...dd.......dd.."],
            ["...kkkkk..kkkkk.", "....dd.....dd..."],
        ],
    },
    "up": {
        "stand": [
            "......b.bb.b....",
            "......bbbbbb....",
            "......hhhhhh....",
            ".......hhhh.....",
            "....hhhhhhhhhh..",
            "....hhhhhhhhhh..",
            "....hhhhhhhhhh..",
            ".....hhhhhhhh...",
            "..kk.kkkkkkkk.kk",
            ".kkkkkkkkkkkkkkk",
            ".hh.kkkkkkkkk.hh",
            "....kkkkkkkkk...",
            "....kkkhhhkkk...",
            "....kkkkhkkkk...",
            "...kkkkhhhkkkk..",
            "...ddd......ddd.",
        ],
        "hold": trident(1, 0, 10),
        "raise": trident(1, 0, 9),
        "strike": [(1, 9, "b"), (1, 8, "b"), (0, 0, "b.b"), (0, 1, "bbb")] + [(1, y, "b") for y in range(2, 8)],
        "steps": [
            ["...kkkkhhhkkkk..", "...dd.......dd.."],
            ["...kkkkhhhkkkk..", "....dd.....dd..."],
        ],
    },
}

for _way, _d in MEGALODON.items():
    assert len(_d["stand"]) == 16 and all(len(r) == 16 for r in _d["stand"]), (_way, [len(r) for r in _d["stand"]])
    assert all(len(r) == 16 for s in _d["steps"] for r in s), _way

BIG = [(MEGALODON, MEGALODON_MATERIALS, MEGALODON_GLOWS, MEGALODON_MASK)]


def big_sheet(mask=False):
    out = Image.new("RGBA", (passkins.CELL * 7, passkins.CELL * 3 * len(BIG)), (0, 0, 0, 0))
    for i, (look, materials, glows, dyes) in enumerate(BIG):
        out.paste(monk.big_block(look, materials, glows, dyes, mask), (0, i * passkins.CELL * 3))
    return out


# --- the oceanic spells' icons ---------------------------------------------------------------------

ICON = 16
# the Maw's colour by tier (the server's Player.MawColors)
MAW_COLORS = [0x8FE0C8, 0x5CD6C8, 0x48C8E8, 0x3CA0F0, 0x3070E0, 0x40E0FF, 0xE0F4FF, 0x8C5CFF, 0xFF3C50]
BONE = ((255, 255, 250), (230, 228, 214), (176, 170, 150), (112, 106, 92))
OLD_BONE = ((236, 220, 180), (196, 172, 128), (138, 116, 82), (84, 68, 46))


def put(img, x, y, c, a=255):
    if 0 <= x < ICON and 0 <= y < ICON:
        img.putpixel((x, y), tuple(c[:3]) + (a,))


def whirl(img, tier, color):
    """The water the shark rises from: a whirl round the icon, fuller with each tier."""
    deep = monk.mixc(color, (10, 20, 40), 0.55)
    turns = 0.55 + 0.1 * tier
    for i in range(160):
        t = i / 160.0
        if t > turns:
            break
        a = t * 2 * math.pi * 1.1 + 0.6
        r = 7.2 - 2.2 * t
        x, y = 7.5 + r * math.cos(a), 7.5 + r * 0.9 * math.sin(a)
        c = monk.mixc(deep, color, t / max(turns, 0.01))
        put(img, int(round(x)), int(round(y)), c, int(150 + 90 * t))
    if tier >= 5:
        for y in range(ICON):
            for x in range(ICON):
                d = math.hypot(x - 7.5, y - 8.5)
                if d < 6.5 and img.getpixel((x, y))[3] == 0:
                    img.putpixel((x, y), color + (int(70 * (1 - d / 6.5)),))


def tooth(img, cx, top, half, length, bone, serrate):
    """A shark's tooth hanging point down from row `top`: `half` wide each side at its root."""
    lit, base, shade, dark = bone
    for dy in range(length):
        w = half * (1 - dy / length) + 0.3
        for dx in range(-int(w), int(w) + 1):
            x, y = cx + dx, top + dy
            edge = abs(dx) >= int(w)
            c = lit if dx < 0 and not edge else base if not edge else shade
            if edge and serrate and dy % 2 == 1:
                c = dark
            put(img, x, y, c)
    # the root, darker, across the top
    for dx in range(-half, half + 1):
        put(img, cx + dx, top - 1, shade if abs(dx) < half else dark)


def jaws(img, color, bone, rows, throat=(10, 20, 40)):
    """Open jaws seen from the front: the throat in the water's colour, a rim of bone, the teeth
    of the upper jaw hanging down into it and the lower's pointing up, `rows` deep."""
    lit, base, shade, dark = bone
    cx, cy = 7.5, 8.0
    for y in range(ICON):
        for x in range(ICON):
            e = ((x - cx) / 6.6) ** 2 + ((y - cy) / 5.6) ** 2
            if e <= 1.0:
                inner = ((x - cx) / 5.2) ** 2 + ((y - cy) / 4.0) ** 2
                if inner <= 1.0:
                    put(img, x, y, monk.mixc(color, throat, min(1.0, inner ** 0.5 + 0.15)))
                else:
                    put(img, x, y, base if y < cy else shade)
                    if e > 0.86:
                        put(img, x, y, dark)
    for k in range(rows):
        top, bottom = int(cy - 4 + 2 * k), int(cy + 3 - 2 * k)
        for x in range(3 + k, 13 - k, 2):
            # the upper teeth, two wide, hanging two down; the lower pointing up
            put(img, x, top, lit)
            put(img, x + 1, top, base)
            put(img, x, top + 1, lit if k == 0 else base)
            put(img, x + 1, bottom, base)
            put(img, x, bottom, lit)
            put(img, x + 1, bottom - 1, lit if k == 0 else base)


def icon(tier):
    img = Image.new("RGBA", (ICON, ICON), (0, 0, 0, 0))
    color = monk.hexc(MAW_COLORS[tier])
    whirl(img, tier, color)
    if tier <= 3:
        # a single tooth, bigger and sharper with each tier, on a cord from the Hammerhead Fang
        tooth(img, 8, 4 - (tier >= 2), 2 + (tier + 1) // 2, 7 + tier, BONE, tier >= 2)
        if tier >= 3:
            for x in range(3, 13):
                put(img, x, 2 if 5 <= x <= 10 else 1, (120, 84, 50))
    elif tier <= 6:
        # a jaw of teeth from the Bull Shark Jaw, its throat brighter with each tier
        jaws(img, color, BONE, 1)
    elif tier == 7:
        # the Jaws of the Abyss: dark bone, a violet glow in the throat
        dark = tuple(monk.mixc(c, (60, 40, 90), 0.35) for c in BONE)
        jaws(img, monk.mixc(color, (255, 255, 255), 0.25), dark, 1, throat=(20, 0, 40))
        put(img, 7, 8, (255, 255, 255))
        put(img, 8, 8, (220, 200, 255))
    else:
        # the Megalodon Tooth: huge, old, its root dark, a glow of blood round it, sparkles
        for y in range(ICON):
            for x in range(ICON):
                d = math.hypot(x - 7.5, y - 7.5)
                if d < 7.5 and img.getpixel((x, y))[3] == 0:
                    img.putpixel((x, y), color + (int(90 * (1 - d / 7.5)),))
        tooth(img, 8, 3, 5, 12, OLD_BONE, True)
        for dx in range(-5, 6):
            put(img, 8 + dx, 2, (70, 50, 40))
            put(img, 8 + dx, 1, (50, 34, 28) if abs(dx) < 5 else (0, 0, 0), 255 if abs(dx) < 5 else 0)
        for x, y in ((2, 3), (13, 11)):
            put(img, x, y, (255, 255, 255))
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if 0 <= x + dx < ICON and 0 <= y + dy < ICON and img.getpixel((x + dx, y + dy))[3] < 200:
                    put(img, x + dx, y + dy, monk.mixc(color, (255, 255, 255), 0.5), 200)
    return img


# --- the sheets ----------------------------------------------------------------------------------

# name, gold, sheet, index, size, look
SKINS = [(s["name"], 200, "sharklordSkins8", i, None, s["look"]) for i, s in enumerate(SMALL)] + [
    ("The Megalodon", 800, "sharklordSkins16", 0, passkins.SIZE, "megalodon")]


def put_block(path, block):
    """The Sharklord's block in a player sheet, replacing what was there."""
    sheet = Image.open(path).convert("RGBA")
    y = SHARKLORD_INDEX * 24
    sheet.paste(Image.new("RGBA", (56, 24), (0, 0, 0, 0)), (0, y))
    sheet.paste(block, (0, y))
    sheet.save(path)


def main():
    me, me_mask = druid.paint(SHARKLORD_LOOK), druid.paint(SHARKLORD_LOOK, mask=True)
    put_block(PLAYERS, me)
    put_block(PLAYERS_MASK, me_mask)
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
        druid.sub(o, "PlayerClassType", "0x%04x" % SHARKLORD)
        druid.sub(o, "UnlockLevel", 0)
        druid.sub(o, "Cost", gold)
        druid.sub(o, "SkinLook", look)
        if size:
            druid.sub(o, "Size", size)
    ET.indent(root, "\t")
    text = ("<!-- The Sharklord's skins sold at the Armoire: generated by tools/draw_sharklord.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"Sharklord, {len(SKINS)} skins and 9 oceanic spells")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        root_, ext = os.path.splitext(path)
        sheet = Image.new("RGBA", (56, 24 * (len(SMALL) + 1)), (0, 0, 0, 0))
        sheet.paste(me, (0, 0))
        sheet.paste(small, (0, 24))
        sheet_mask = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        sheet_mask.paste(me_mask, (0, 0))
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
