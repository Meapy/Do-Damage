"""Draws the Druid: its own 8 by 8 sprite, and its skins sold at the Armoire (four 8 by 8 at 200
gold, one 16 by 16 at 800), each with the dye mask that lets cloth and dyes colour it.

    python tools/draw_druid.py [--preview <png>]

Every figure is its own drawing, its own shape and weapon, not a recolour:

  Druid               antlers rising through a moss-green hood, eyes glowing in its shadow, a
                      cloak with a sash of autumn leaves, a wand of twisted wood tipped with a pearl
  Deepwater Oracle    no hood but a crown of coral, sea-green hair to the waist, pale eyes, a robe
                      of the deep with pearls at the throat, a tall coral staff crowned by a pearl
  Autumn Warden       great antlers hung with red leaves, amber eyes in the dark of a hood, a heavy
                      fur mantle over a russet cloak belted in gold, a golden sickle
  Moonbloom Druid     long white hair crowned with moonflowers, violet eyes, a midnight robe with a
                      silver collar and a train, a staff with a moonflower open at its head
  Mosshide Hermit     hunched under a great red toadstool, eyes glinting in its shade, a beard of
                      moss to the knees, a cloak of moss and lichen, a lantern on a crooked stick
  Archdruid  (16 by 16) a towering druid behind a stag's skull, eyes burning green in the
                      sockets, great antlers hung with leaves, a cloak of living leaves over a robe
                      of moss belted with bark, a staff of roots cradling a great glowing pearl

The skins' own effects (the Rust client's auras.rs, by their `<SkinLook>`) are each one quiet
thing: the Oracle's bubbles, the Warden's falling leaves, the Moonbloom's moonlight, the Hermit's
fireflies, the Archdruid's footsteps sprouting and its eyes' glow.

The 8 by 8 figures are painted as the Mechanic's skins are (tools/draw_mechanic_skins.py
`paint_small`: for each way it faces, the figure, its feet, its weapon held and struck out, a
letter a colour); the 16 by 16 as the Realm Pass's skins are (tools/draw_pass_skins.py `block`).

Dyes: a figure's `mask` says which letters (8 by 8) or materials (16 by 16) are cloth: red the
main dye, green the accent. A dye replaces the pixel with the dye times the mask's amount, so the
amount carries the shading: lit 255, its base 216, shadow 160, deep 110.

Outputs (generated, don't edit):
- the Druid's block (index 14) in the client's doomed/images/players.png and playersMask.png;
- DruidSkins8x8.png, DruidSkins16x16.png and their masks DruidSkinsMask8x8.png and
  DruidSkinsMask16x16.png in the client's custom/images, the classes' layout, registered for the
  Rust client by tools/gen_rs_manifest.py (RS_ONLY_CHARS, "druidSkins8" and "druidSkins16");
- DruidSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML): the skins, with their price in gold (`Cost`, which the Armoire
  sells them for) and their look.
Type codes 0xE210 on, fixed here (characters keep their skin by type: never renumber one). Needs
Pillow. `--preview` also saves the sheets as the game draws them (black outline, bigger), and each
dyed red and blue.
"""
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import draw_mechanic_skins as mechskins  # noqa: E402
import draw_pass_skins as passkins  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(ASSETS, "custom", "images")
PLAYERS = os.path.join(ASSETS, "doomed", "images", "players.png")
PLAYERS_MASK = os.path.join(ASSETS, "doomed", "images", "playersMask.png")
DRUID_INDEX = 14
OUT_8 = os.path.join(IMAGES, "DruidSkins8x8.png")
OUT_8_MASK = os.path.join(IMAGES, "DruidSkinsMask8x8.png")
OUT_16 = os.path.join(IMAGES, "DruidSkins16x16.png")
OUT_16_MASK = os.path.join(IMAGES, "DruidSkinsMask16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DruidSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "DruidSkins.xml")]
DRUID = 0xD247
FIRST_TYPE = 0xE210

# a dye's amount by how lit the pixel is: lit, base, shadow, deep
AMOUNT = (255, 216, 160, 110)


def mask_color(which, idx):
    v = AMOUNT[idx]
    return (v, 0, 0) if which == "main" else (0, v, 0)


# --- the 8 by 8 figures ---------------------------------------------------------------------------
# `mask`: letter -> ("main" or "accent", how lit: 1 its base, 2 shadow; 0 lit)

DRUID_LOOK = {
    "name": "Druid",
    "colors": {
        "A": 0xE6DAB8, "h": 0x5C9A44, "H": 0x3A6A2C, "f": 0xE8B48A, "e": 0xA8FFB8, "c": 0x4E8A3A,
        "C": 0x2E5A24, "t": 0xD8A23A, "l": 0x3A2818, "w": 0x8A5A2A, "p": 0xE8FAFF, "P": 0x7AE0FF,
    },
    "mask": {"h": ("main", 0), "H": ("main", 2), "c": ("main", 1), "C": ("main", 2), "t": ("accent", 0)},
    "right": {
        "body": [".A..A...",
                 "..AhA...",
                 "..hhhh..",
                 "..Hhfe..",
                 ".Cctcf..",
                 ".Ccctc..",
                 "CccccC.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(6, 1, "p"), (6, 2, "w"), (6, 3, "w"), (6, 4, "w")],
        "strike": [(6, 4, "w"), (7, 4, "w"), (8, 4, "w"), (9, 4, "p"), (10, 4, "P"), (10, 3, "P"), (10, 5, "P")],
    },
    "down": {
        "body": [".A....A.",
                 "..AhhA..",
                 "..hhhh..",
                 "..heeh..",
                 ".ctcccC.",
                 ".cctccC.",
                 ".CccctCC"],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(7, 1, "p"), (7, 2, "w"), (7, 3, "w"), (7, 4, "w")],
        "strike": [(7, 4, "w"), (7, 5, "w"), (7, 6, "p"), (7, 7, "P"), (6, 7, "P")],
    },
    "up": {
        "body": [".A....A.",
                 "..AhhA..",
                 "..hhhh..",
                 "..HhhH..",
                 ".ctcccC.",
                 ".cctccC.",
                 "CcCccCC."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 1, "p"), (0, 2, "w"), (0, 3, "w"), (0, 4, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "p"), (1, 0, "P"), (0, 0, "P"), (2, 0, "P")],
    },
}

# The Deepwater Oracle: the robe takes the main dye, the hair the accent.
ORACLE = {
    "name": "Deepwater Oracle",
    "look": "oracle",
    "colors": {
        "A": 0xE0604E, "a": 0xF29A84, "h": 0x2EA898, "H": 0x186A62, "f": 0xD6E6EA, "e": 0x8AF4FF,
        "c": 0x1C5478, "C": 0x0C2A46, "T": 0xF4FCFF, "l": 0x0C1E2E, "w": 0xD8CCB0, "W": 0x9A8C70,
        "p": 0xFFFFFF, "P": 0x46E2D4,
    },
    "mask": {"c": ("main", 1), "C": ("main", 2), "h": ("accent", 1), "H": ("accent", 2)},
    "right": {
        "body": ["..a.a...",
                 "..AAA...",
                 ".hhff...",
                 ".hhfe...",
                 "hhcTc...",
                 ".hccc...",
                 ".cccCc.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 0, "P"), (5, 1, "p"), (5, 2, "w"), (5, 3, "w"), (5, 4, "W"), (5, 5, "w"), (5, 6, "W")],
        "raise": [(5, 0, "p"), (4, 0, "P"), (5, 1, "w"), (5, 2, "w"), (5, 3, "W"), (5, 4, "w"), (5, 5, "W")],
        "strike": [(5, 4, "W"), (6, 4, "w"), (7, 4, "W"), (8, 4, "w"), (9, 4, "w"), (10, 4, "p"), (11, 4, "P")],
    },
    "down": {
        "body": ["..a..a..",
                 "..aAAa..",
                 ".hffffh.",
                 ".heffeh.",
                 "hcTTTch.",
                 "hcccccH.",
                 ".cCccCc."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(7, 0, "P"), (7, 1, "p"), (7, 2, "w"), (7, 3, "w"), (7, 4, "W"), (7, 5, "w"), (7, 6, "W")],
        "raise": [(7, 0, "p"), (7, 1, "w"), (7, 2, "w"), (7, 3, "W"), (7, 4, "w"), (7, 5, "W")],
        "strike": [(7, 4, "W"), (7, 5, "w"), (7, 6, "p"), (7, 7, "P"), (6, 7, "P")],
    },
    "up": {
        "body": ["..a..a..",
                 "..aAAa..",
                 ".hhhhhh.",
                 ".hhHHhh.",
                 ".hhhhhh.",
                 ".chHHhc.",
                 ".cCccCc."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 0, "P"), (0, 1, "p"), (0, 2, "w"), (0, 3, "w"), (0, 4, "W"), (0, 5, "w"), (0, 6, "W")],
        "raise": [(0, 0, "p"), (0, 1, "w"), (0, 2, "w"), (0, 3, "W"), (0, 4, "w"), (0, 5, "W")],
        "strike": [(1, 3, "W"), (1, 2, "w"), (1, 1, "p"), (1, 0, "P")],
    },
}

# The Autumn Warden: hood and cloak the main dye, the fur mantle the accent.
WARDEN = {
    "name": "Autumn Warden",
    "look": "warden",
    "colors": {
        "A": 0xD8C098, "g": 0xE8482A, "h": 0x8A3A18, "H": 0x5A2410, "d": 0x2A140A, "e": 0xFFB830,
        "F": 0xB8966A, "f": 0xE2CCA0, "c": 0xA84418, "C": 0x6A2610, "T": 0xF0C040, "l": 0x2E1A0C, "w": 0x5A3A1E,
        "s": 0xFFE08A, "S": 0xC89A30,
    },
    "mask": {"h": ("main", 0), "H": ("main", 2), "c": ("main", 1), "C": ("main", 2), "F": ("accent", 1), "f": ("accent", 0)},
    "right": {
        "body": ["g.A.Ag..",
                 ".AAhA...",
                 "..hhhh..",
                 "..Hdde..",
                 ".FfFfF..",
                 ".ccTcc..",
                 ".CccCc.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(6, 1, "S"), (7, 1, "s"), (7, 2, "s"), (7, 3, "S"), (6, 4, "w"), (6, 5, "w")],
        "raise": [(6, 0, "S"), (7, 0, "s"), (7, 1, "s"), (7, 2, "S"), (6, 3, "w"), (6, 4, "w")],
        "strike": [(6, 4, "w"), (7, 4, "w"), (8, 4, "w"), (9, 3, "S"), (10, 3, "s"), (11, 4, "s"), (11, 5, "s"),
                   (10, 6, "S")],
    },
    "down": {
        "body": ["gA....Ag",
                 ".AA..AA.",
                 "..hhhh..",
                 "..deed..",
                 ".FfFFfF.",
                 ".ccTTcC.",
                 ".CccccC."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(6, 2, "S"), (7, 2, "s"), (7, 3, "s"), (7, 4, "S"), (7, 5, "w"), (7, 6, "w")],
        "strike": [(7, 4, "w"), (7, 5, "w"), (6, 6, "S"), (6, 7, "s"), (7, 7, "s"), (5, 7, "S")],
    },
    "up": {
        "body": ["gA....Ag",
                 ".AA..AA.",
                 "..hhhh..",
                 "..hHHh..",
                 ".FfFfFF.",
                 ".ccccgC.",
                 ".CccccC."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(1, 2, "S"), (0, 2, "s"), (0, 3, "s"), (0, 4, "S"), (0, 5, "w"), (0, 6, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "S"), (0, 0, "s"), (1, 0, "s"), (2, 0, "S")],
    },
}

# The Moonbloom Druid: the robe takes the main dye, the hair the accent.
MOONBLOOM = {
    "name": "Moonbloom Druid",
    "look": "moonbloom",
    "colors": {
        "g": 0xFFFFFF, "A": 0xC8D0F0, "W": 0xECEAF8, "V": 0xA8A4C8, "f": 0xE0D6F0, "e": 0xC88CFF,
        "c": 0x262A64, "C": 0x12143A, "t": 0xB8C4F0, "l": 0x0E0E22, "w": 0x9AA2C8, "p": 0xFFFFFF,
        "P": 0xC890FF,
    },
    "mask": {"c": ("main", 1), "C": ("main", 2), "W": ("accent", 0), "V": ("accent", 2)},
    "right": {
        "body": ["..gAg...",
                 ".WWWW...",
                 ".WWfe...",
                 "VWctc...",
                 "Vccccf..",
                 ".ccccC..",
                 "CcccccC."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(6, 0, "P"), (7, 1, "P"), (6, 1, "p"), (6, 2, "w"), (6, 3, "w"), (6, 4, "w"), (6, 5, "w")],
        "strike": [(6, 4, "w"), (7, 4, "w"), (8, 4, "w"), (9, 4, "w"), (10, 4, "p"), (10, 3, "P"), (11, 4, "P"),
                   (10, 5, "P")],
    },
    "down": {
        "body": ["..gAAg..",
                 ".WWWWWW.",
                 ".WeffeW.",
                 ".WcttcW.",
                 ".ccccgc.",
                 ".cccccC.",
                 "CcccccCC"],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(7, 0, "P"), (6, 1, "P"), (7, 1, "p"), (7, 2, "w"), (7, 3, "w"), (7, 4, "w"), (7, 5, "w")],
        "strike": [(7, 4, "w"), (7, 5, "w"), (7, 6, "p"), (6, 6, "P"), (7, 7, "P")],
    },
    "up": {
        "body": ["..gAAg..",
                 ".WWWWWW.",
                 ".WWVVWW.",
                 ".WWWWWW.",
                 ".cWVVWc.",
                 ".ccWWcC.",
                 "CcccccCC"],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 0, "P"), (1, 1, "P"), (0, 1, "p"), (0, 2, "w"), (0, 3, "w"), (0, 4, "w"), (0, 5, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "p"), (0, 1, "P"), (1, 0, "P")],
    },
}

# The Mosshide Hermit: the cloak takes the main dye, the beard the accent.
HERMIT = {
    "name": "Mosshide Hermit",
    "look": "hermit",
    "colors": {
        "m": 0xD8402C, "M": 0xFFF0DC, "h": 0x5E7034, "H": 0x3A4620, "f": 0xC09A74, "e": 0xFFF29A,
        "b": 0xA8B490, "B": 0x748060, "l": 0x2A2014, "w": 0x5E3E1E, "k": 0x2A2A30, "p": 0xFFE070,
    },
    "mask": {"h": ("main", 1), "H": ("main", 2), "b": ("accent", 1), "B": ("accent", 2)},
    "right": {
        "body": [".mmmm...",
                 "mMmmMm..",
                 "..hfe...",
                 ".hbbb...",
                 "hhbbbf..",
                 "hHhbB...",
                 ".HhhH..."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(6, 0, "w"), (7, 0, "w"), (7, 1, "k"), (7, 2, "p"), (6, 1, "w"), (6, 2, "w"), (6, 3, "w"),
                   (6, 4, "w"), (6, 5, "w")],
        "raise": [(6, 0, "w"), (7, 0, "k"), (7, 1, "p"), (6, 1, "w"), (6, 2, "w"), (6, 3, "w"), (6, 4, "w")],
        "strike": [(6, 4, "w"), (7, 4, "w"), (8, 3, "w"), (9, 3, "w"), (10, 3, "k"), (10, 4, "p"), (10, 5, "p")],
    },
    "down": {
        "body": [".mmmmmm.",
                 "mMmmmMmm",
                 "..heeh..",
                 "..bffb..",
                 ".hbbbbh.",
                 ".hhbBhH.",
                 ".HhhhhH."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(7, 2, "w"), (7, 3, "w"), (7, 4, "k"), (7, 5, "p"), (7, 6, "p")],
        "raise": [(7, 1, "w"), (7, 2, "w"), (7, 3, "k"), (7, 4, "p"), (7, 5, "p")],
        "strike": [(7, 4, "w"), (7, 5, "w"), (7, 6, "k"), (7, 7, "p"), (6, 7, "p")],
    },
    "up": {
        "body": [".mmmmmm.",
                 "mmMmmMmm",
                 "..hhhh..",
                 ".hhHHhh.",
                 ".hhhhhh.",
                 ".hHhhHh.",
                 ".HhhhhH."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 2, "w"), (0, 3, "w"), (0, 4, "k"), (0, 5, "p"), (0, 6, "p")],
        "raise": [(0, 1, "w"), (0, 2, "w"), (0, 3, "k"), (0, 4, "p"), (0, 5, "p")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "k"), (1, 0, "p")],
    },
}

SMALL = [ORACLE, WARDEN, MOONBLOOM, HERMIT]


def paint(look, mask=False):
    """A figure's 56 by 24 block, or its dye mask."""
    if not mask:
        return mechskins.paint_small(look)
    # paint it with a colour per masked letter, then turn those into the mask's amounts
    letters = sorted(look["mask"])
    code = {k: (0x000001 * (letters.index(k) + 1) if k in look["mask"] else 0xFFFFFF) for k in look["colors"]}
    coded = mechskins.paint_small(dict(look, colors=code))
    out = Image.new("RGBA", coded.size, (0, 0, 0, 0))
    for y in range(coded.height):
        for x in range(coded.width):
            r, g, b, a = coded.getpixel((x, y))
            if a and (r, g) == (0, 0) and 1 <= b <= len(letters):
                which, idx = look["mask"][letters[b - 1]]
                out.putpixel((x, y), mask_color(which, idx) + (255,))
    return out


# --- the Archdruid, 16 by 16 -------------------------------------------------------------

ARCH_MATERIALS = {
    # bone: the skull and antlers
    "b": [(252, 248, 232), (226, 212, 182), (170, 150, 116), (104, 90, 64)],
    # living leaves: the cloak and hood (the main dye)
    "l": [(156, 232, 112), (88, 172, 66), (48, 112, 42), (24, 64, 24)],
    # moss: the robe (the accent)
    "m": [(118, 146, 92), (76, 100, 62), (48, 66, 42), (28, 40, 26)],
    # bark: the belt, the staff, the boots
    "r": [(200, 146, 94), (142, 94, 54), (96, 62, 34), (58, 36, 18)],
    # autumn leaves hung on the antlers and the cloak
    "o": [(255, 226, 120), (236, 164, 52), (176, 104, 30), (104, 58, 16)],
    # the dark inside the hood
    "d": [(70, 70, 82), (40, 40, 52), (24, 24, 34), (12, 12, 18)],
}
ARCH_GLOWS = {
    "e": (150, 255, 160),
    "x": (255, 255, 255),
    "p": (140, 236, 255),
    "q": (232, 252, 255),
    "f": (255, 238, 120),
}
ARCH_MASK = {"l": "main", "m": "accent"}


def pearl_staff(x, top, length=11, hand=None):
    """A staff of roots standing at column x, a great pearl cradled at its top from row `top`."""
    parts = [(x - 1, top, "rpr"), (x - 1, top + 1, "pqp"), (x - 1, top + 2, "rpr"), (x, top + 3, "r")]
    for y in range(top + 4, min(passkins.CELL, top + 4 + length)):
        parts.append((x, y, "r"))
    if hand is not None:
        parts.append((x - 1, hand, "b"))
    return parts


def pearl_staff_down(x, top, length=5):
    """The staff swung down at column x from its grip at row `top`, the pearl at its foot."""
    parts = [(x, y, "r") for y in range(top, top + length)]
    end = top + length
    return parts + [(x - 1, end, "rpr"), (x - 1, end + 1, "pqp")]


ARCHDRUID = {
    "right": {
        "stand": [
            "..b..b..b..b....",
            "..bb.bb.bb.b....",
            "...bbbo.bbb.....",
            ".....bbbbb......",
            "....lllbbbb.....",
            "....lldbebbb....",
            "....lldbbbBb....",
            "....llldbBB.....",
            "...llllllll.....",
            "...lllmmmll.....",
            "...llmmmmml.....",
            "..lllrrorrl.....",
            "..llmmmmmml.....",
            "..llmmmMmmll....",
            ".llmmmmMmmmll...",
            ".lllmmmMmmmlll..",
        ],
        "hold": pearl_staff(13, 1, hand=9),
        "raise": pearl_staff(12, 0, length=7, hand=6),
        "strike": [(10, 9, "bb")] + [(12 + i, 9, "r") for i in range(7)] + [
            (19, 8, "rpr"), (19, 9, "pqp"), (19, 10, "rpr"), (22, 9, "x"), (22, 8, "p"), (22, 10, "p")],
        "steps": [
            ["..llmmmMmmmll...", "...rr.....rr...."],
            ["...lmmmMmmml....", ".....rrrr......."],
        ],
    },
    "down": {
        "stand": [
            ".b..b......b..b.",
            ".bb.bb....bb.bb.",
            "..bbbo....obbb..",
            "....bbb..bbb....",
            ".....bbbbbb.....",
            "....lbbbbbbl....",
            "....lbebbebl....",
            "....ldbbbbdl....",
            "...llldbbdlll...",
            "..llllmmmmllll..",
            "..lllmmmmmmlll..",
            "..llrrrorrrrll..",
            "...lmmmmmmmml...",
            "..llmmmMMmmmll..",
            "..llmmmMMmmmll..",
            ".lllmmmMMmmmlll.",
        ],
        "hold": pearl_staff(14, 1, hand=10),
        "raise": pearl_staff(14, 0, length=6, hand=6),
        "strike": [(12, 10, "bb")] + pearl_staff_down(13, 11, 3),
        "steps": [
            ["..llmmmMMmmmll..", "...rr......lol.."],
            ["..llmmmMMmmmll..", "..lol......rr..."],
        ],
    },
    "up": {
        "stand": [
            ".b..b......b..b.",
            ".bb.bb....bb.bb.",
            "..bbbo....obbb..",
            "....bbb..bbb....",
            ".....bbbbbb.....",
            "....llllllll....",
            "....llllllll....",
            "....lLllllLl....",
            "...llLllllLll...",
            "..lLllLllLllLl..",
            "..llLllLllLlll..",
            "..llrrrrrrrrll..",
            "...lLllLllLll...",
            "..llLllLllLlll..",
            "..lLllLllLllLl..",
            ".lllLllLllLllll.",
        ],
        "hold": pearl_staff(1, 1, hand=10),
        "raise": pearl_staff(1, 0, length=6, hand=6),
        "strike": pearl_staff(2, 0, length=5, hand=6),
        "steps": [
            ["..llLlllllLlll..", "...rr......lol.."],
            ["..llLlllllLlll..", "..lol......rr..."],
        ],
    },
}


def arch_block(mask=False):
    """The Archdruid's block, drawn by passkins.block; for the mask, its shading turned into the
    dyes' amounts (passkins.shade's own rule for which pixel is lit)."""
    passkins.MATERIALS = ARCH_MATERIALS
    passkins.GLOWS = ARCH_GLOWS
    if not mask:
        return passkins.block(ARCHDRUID)
    shade = passkins.shade

    def mask_shade(g):
        img = shade(g)
        out = Image.new("RGBA", img.size, (0, 0, 0, 0))
        for y in range(len(g)):
            for x in range(len(g[0])):
                ch = g[y][x]
                which = ARCH_MASK.get(ch.lower()) if ch != "." and ch not in ARCH_GLOWS else None
                if not which:
                    continue
                idx = ARCH_MATERIALS[ch.lower()].index(img.getpixel((x, y))[:3])
                out.putpixel((x, y), mask_color(which, idx) + (255,))
        return out
    passkins.shade = mask_shade
    try:
        return passkins.block(ARCHDRUID)
    finally:
        passkins.shade = shade


# name, gold, sheet, index, size, look
SKINS = [(s["name"], 200, "druidSkins8", i, None, s["look"]) for i, s in enumerate(SMALL)] + [
    ("Archdruid", 800, "druidSkins16", 0, passkins.SIZE, "archdruid")]


def sub(parent, tag, text=None):
    e = ET.SubElement(parent, tag)
    if text is not None:
        e.text = str(text)
    return e


def put_block(path, block):
    """The Druid's block in a player sheet, replacing what was there."""
    sheet = Image.open(path).convert("RGBA")
    y = DRUID_INDEX * 24
    sheet.paste(Image.new("RGBA", (56, 24), (0, 0, 0, 0)), (0, y))
    sheet.paste(block, (0, y))
    sheet.save(path)


def dyed(img, mask, main, accent):
    """`img` dyed as the game dyes it (redraw.rs `retexture`), with two flat colours."""
    out = img.copy()
    for y in range(img.height):
        for x in range(img.width):
            m = mask.getpixel((x, y))
            p = list(out.getpixel((x, y)))
            for amount, c in ((m[0], main), (m[1], accent)):
                if m[3] and amount:
                    p[:3] = [v * amount // 255 for v in c]
            out.putpixel((x, y), tuple(p))
    return out


def main():
    druid, druid_mask = paint(DRUID_LOOK), paint(DRUID_LOOK, mask=True)
    put_block(PLAYERS, druid)
    put_block(PLAYERS_MASK, druid_mask)
    small = Image.new("RGBA", (56, 24 * len(SMALL)), (0, 0, 0, 0))
    small_mask = small.copy()
    for i, skin in enumerate(SMALL):
        small.paste(paint(skin), (0, i * 24))
        small_mask.paste(paint(skin, mask=True), (0, i * 24))
    small.save(OUT_8)
    small_mask.save(OUT_8_MASK)
    big, big_mask = arch_block(), arch_block(mask=True)
    big.save(OUT_16)
    big_mask.save(OUT_16_MASK)
    root = ET.Element("Objects")
    for i, (name, gold, sheet, index, size, look) in enumerate(SKINS):
        o = ET.SubElement(root, "Object", {"type": "0x%04X" % (FIRST_TYPE + i), "id": name})
        sub(o, "DisplayId", name)
        sub(o, "Skin")
        sub(o, "Class", "Skin")
        at = sub(o, "AnimatedTexture")
        sub(at, "File", sheet)
        sub(at, "Index", index)
        sub(o, "PlayerClassType", "0x%04x" % DRUID)
        sub(o, "UnlockLevel", 0)
        sub(o, "Cost", gold)
        sub(o, "SkinLook", look)
        if size:
            sub(o, "Size", size)
    ET.indent(root, "\t")
    text = ("<!-- The Druid's skins sold at the Armoire: generated by tools/draw_druid.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"Druid and {len(SKINS)} skins")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        root_, ext = os.path.splitext(path)
        sheet = Image.new("RGBA", (56, 24 * (len(SMALL) + 1)), (0, 0, 0, 0))
        sheet.paste(druid, (0, 0))
        sheet.paste(small, (0, 24))
        sheet_mask = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        sheet_mask.paste(druid_mask, (0, 0))
        sheet_mask.paste(small_mask, (0, 24))
        # each beside itself dyed red and gold, and blue and white
        wide = Image.new("RGBA", (56 * 3 + 16, sheet.height), (0, 0, 0, 0))
        wide.paste(sheet, (0, 0))
        wide.paste(dyed(sheet, sheet_mask, (200, 40, 40), (240, 200, 60)), (64, 0))
        wide.paste(dyed(sheet, sheet_mask, (50, 90, 220), (240, 240, 250)), (128, 0))
        passkins.preview(wide, root_ + "-8" + ext, scale=8)
        wide16 = Image.new("RGBA", (big.width * 3 + 32, big.height), (0, 0, 0, 0))
        wide16.paste(big, (0, 0))
        wide16.paste(dyed(big, big_mask, (200, 40, 40), (240, 200, 60)), (big.width + 16, 0))
        wide16.paste(dyed(big, big_mask, (50, 90, 220), (240, 240, 250)), (2 * big.width + 32, 0))
        passkins.preview(wide16, root_ + "-16" + ext, scale=5)


if __name__ == "__main__":
    main()
