"""Draws the Monk: its own 8 by 8 sprite, its skins sold at the Armoire (four 8 by 8 at 200 gold,
one 16 by 16 at 800), each with the dye mask that lets cloth and dyes colour it, and its fountains'
icons (the Flowing Strike, tier 0 to 8).

    python tools/draw_monk.py [--preview <png>]

Every figure is its own drawing, not a recolour:

  Monk                   a shaven head with a topknot, a saffron robe with a crimson sash over one
                         shoulder, prayer beads at the throat, hands wrapped in white cloth
  Iron Lotus             a crimson robe and a saffron sash, a red mark on the brow, golden beads
                         and knuckle rings, a lotus at the shoulder
  Stormfist Disciple     a storm-blue gi, a white headband whose tails stream behind, black hair,
                         hands crackling with blue light
  Ember Fist             a black gi trimmed in red, hair standing up like flame, fists burning
  Jade Wanderer          a wide straw hat over a shadowed face, a jade-green robe, a long mala of
                         jade beads, a gourd at the hip
  Celestial Grandmaster  (16 by 16) an old master with a white beard to the waist and long brows,
                         eyes glowing sky blue, robes of white and gold with a sky sash, a ring
                         of golden beads floating behind the head, palms glowing with chi

The skins' effects (the Rust client's auras.rs, by their `<SkinLook>`): the Lotus's petals
drifting and a lotus under its feet, the Stormfist's sparks jumping between its hands, the Ember
Fist's flames at its fists and rising embers, the Wanderer's mist and falling bamboo leaves, the
Grandmaster's bead halo turning, its eyes' glow and golden lotuses in its steps.

The 8 by 8 figures are painted as the Druid's are (tools/draw_druid.py `paint`), the 16 by 16 as
the Realm Pass's skins are (tools/draw_pass_skins.py `block`). Dyes: a figure's `mask` says which
letters (8 by 8) or materials (16 by 16) are cloth: red the main dye, green the accent.

The fountains' icons: a ring of prayer beads round an open palm of water, the water in the colour
its strike is drawn in (the server's `Player.FlowColors`), the beads wood (T0-T2), jade (T3-T5)
and gold (T6-T8), more ornament with each tier.

Outputs (generated, don't edit):
- the Monk's block (index 30) in the client's doomed/images/players.png and playersMask.png;
- MonkSkins8x8.png, MonkSkins16x16.png and their masks, and MonkFountains16x16.png, in the
  client's custom/images, registered for the Rust client by tools/gen_rs_manifest.py
  ("monkSkins8", "monkSkins16", "monkFountains16x16");
- MonkSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML).
Type codes 0xE220 on, fixed here (characters keep their skin by type: never renumber one). Needs
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
import draw_pass_skins as passkins  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(ASSETS, "custom", "images")
PLAYERS = os.path.join(ASSETS, "doomed", "images", "players.png")
PLAYERS_MASK = os.path.join(ASSETS, "doomed", "images", "playersMask.png")
MONK_INDEX = 30
OUT_8 = os.path.join(IMAGES, "MonkSkins8x8.png")
OUT_8_MASK = os.path.join(IMAGES, "MonkSkinsMask8x8.png")
OUT_16 = os.path.join(IMAGES, "MonkSkins16x16.png")
OUT_16_MASK = os.path.join(IMAGES, "MonkSkinsMask16x16.png")
OUT_ICONS = os.path.join(IMAGES, "MonkFountains16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "MonkSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "MonkSkins.xml")]
MONK = 0xD252
FIRST_TYPE = 0xE220

# --- the 8 by 8 figures ---------------------------------------------------------------------------
# `mask`: letter -> ("main" or "accent", how lit: 0 lit, 1 its base, 2 shadow)

MONK_LOOK = {
    "name": "Monk",
    "colors": {
        "k": 0x2A1A10, "s": 0xF0BC90, "S": 0xC08660, "e": 0x1A1010, "b": 0x6A3E1C, "o": 0xF0962E,
        "O": 0xB0601A, "r": 0xD0342C, "R": 0x8A1E1C, "w": 0xF4EEE0, "l": 0x3A2818, "c": 0xFFE9A0,
    },
    "mask": {"o": ("main", 0), "O": ("main", 2), "r": ("accent", 1), "R": ("accent", 2)},
    "right": {
        "body": ["...kk...",
                 "..ssss..",
                 "..Ssse..",
                 "..bSsb..",
                 ".ooroo..",
                 ".oOoro..",
                 ".oOoOr.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 4, "w")],
        "raise": [(5, 3, "w"), (6, 2, "w")],
        "strike": [(5, 4, "o"), (6, 4, "s"), (7, 4, "w"), (8, 4, "w"), (9, 3, "c"), (10, 4, "c"), (9, 5, "c")],
    },
    "down": {
        "body": ["...kk...",
                 "..ssss..",
                 "..eSSe..",
                 "..bbbb..",
                 ".roooor.",
                 "w.rooO.w",
                 ".oOrOoO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 5, "w"), (6, 6, "w"), (6, 7, "c"), (1, 7, "c")],
    },
    "up": {
        "body": ["...kk...",
                 "..ssss..",
                 "..SssS..",
                 "..bSSb..",
                 ".ooooro.",
                 "w.oorO.w",
                 ".oOoroO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "c"), (6, 3, "w")],
    },
}

# The Iron Lotus: the robe takes the main dye, the sash the accent.
LOTUS = {
    "name": "Iron Lotus",
    "look": "lotus",
    "colors": {
        "s": 0xE8B088, "S": 0xB07A56, "e": 0x1A1010, "m": 0xFF3040, "g": 0xFFD040, "G": 0xB88A20,
        "o": 0xC02A2A, "O": 0x7A1418, "y": 0xF8B030, "Y": 0xB87018, "p": 0xFF9AC8, "l": 0x2A1810,
        "w": 0xFFE070,
    },
    "mask": {"o": ("main", 1), "O": ("main", 2), "y": ("accent", 0), "Y": ("accent", 2)},
    "right": {
        "body": ["..sss...",
                 "..sssm..",
                 "..Ssse..",
                 "..gSsg..",
                 ".poyoo..",
                 ".oOyoo..",
                 ".oOoyO.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 4, "w")],
        "raise": [(5, 3, "w"), (6, 2, "g")],
        "strike": [(5, 4, "o"), (6, 4, "s"), (7, 4, "w"), (8, 4, "g"), (9, 4, "p"), (10, 3, "p"), (10, 5, "p")],
    },
    "down": {
        "body": ["...ss...",
                 "..ssss..",
                 "..emme..",
                 "..gggg..",
                 ".yooopo.",
                 "w.yooO.w",
                 ".oOyOoO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 5, "w"), (6, 6, "w"), (6, 7, "p"), (1, 7, "p")],
    },
    "up": {
        "body": ["...ss...",
                 "..ssss..",
                 "..SssS..",
                 "..gSSg..",
                 ".poooyo.",
                 "w.ooyO.w",
                 ".oOoyoO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "p"), (6, 3, "w")],
    },
}

# The Stormfist Disciple: the gi takes the main dye, the headband the accent.
STORM = {
    "name": "Stormfist Disciple",
    "look": "storm",
    "colors": {
        "k": 0x14161E, "s": 0xF0C49C, "e": 0x6AE8FF, "h": 0xF4F6FF, "H": 0xA8B0C8, "o": 0x2A5AB8,
        "O": 0x163070, "t": 0xE8ECF8, "l": 0x101828, "w": 0x8AF0FF, "c": 0xFFFFFF, "C": 0x40C8FF,
    },
    "mask": {"o": ("main", 1), "O": ("main", 2), "h": ("accent", 0), "H": ("accent", 2)},
    "right": {
        "body": ["..kkk...",
                 "Hhhhhh..",
                 ".Hkssek.",
                 "..ksss..",
                 ".ootoo..",
                 ".oOtoo..",
                 ".oOooO.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 4, "w")],
        "raise": [(5, 3, "w"), (6, 2, "C")],
        "strike": [(5, 4, "o"), (6, 4, "s"), (7, 4, "w"), (8, 4, "w"), (9, 4, "c"), (10, 3, "C"), (10, 5, "C"),
                   (11, 4, "C")],
    },
    "down": {
        "body": ["..kkkk..",
                 ".hhhhhh.",
                 "..esse..",
                 "..ksss..",
                 ".ootto..",
                 "w.ottO.w",
                 ".oOooOO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 5, "w"), (6, 6, "w"), (6, 7, "C"), (1, 7, "C"), (7, 7, "c")],
    },
    "up": {
        "body": ["..kkkk..",
                 ".hhhhhh.",
                 ".kkHHkk.",
                 "..kkkkH.",
                 ".ooooo.H",
                 "w.oooO.w",
                 ".oOooOO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 3, "w"), (7, 3, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "C"), (6, 3, "w")],
    },
}

# The Ember Fist: the gi takes the main dye, the trim the accent.
EMBER = {
    "name": "Ember Fist",
    "look": "ember",
    "colors": {
        "f": 0xFF5A1E, "F": 0xFFC040, "s": 0xE0A880, "S": 0xA87050, "e": 0xFFE040, "o": 0x242028,
        "O": 0x121014, "t": 0xD02A1E, "T": 0x80140E, "l": 0x101010, "w": 0xFF8A2A, "c": 0xFFF0A0,
        "C": 0xFF4A10,
    },
    "mask": {"o": ("main", 1), "O": ("main", 2), "t": ("accent", 1), "T": ("accent", 2)},
    "right": {
        "body": ["..F.F...",
                 ".fFfFf..",
                 "..ffse..",
                 "..Ssst..",
                 ".ootoo..",
                 ".oOoto..",
                 ".OOooT.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 4, "w"), (5, 3, "F")],
        "raise": [(5, 3, "w"), (6, 2, "w"), (6, 1, "F")],
        "strike": [(5, 4, "o"), (6, 4, "s"), (7, 4, "w"), (8, 4, "w"), (9, 4, "c"), (10, 4, "F"), (10, 3, "C"),
                   (10, 5, "C"), (11, 4, "C")],
    },
    "down": {
        "body": [".F.FF.F.",
                 ".fFffFf.",
                 "..esse..",
                 "..tSSt..",
                 ".ottto..",
                 "w.otoO.w",
                 ".OOooOO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w"), (0, 4, "F"), (7, 4, "F")],
        "raise": [(0, 3, "w"), (7, 3, "w"), (0, 2, "F"), (7, 2, "F")],
        "strike": [(1, 5, "w"), (6, 6, "w"), (6, 7, "F"), (1, 7, "F"), (7, 7, "C")],
    },
    "up": {
        "body": [".F.FF.F.",
                 ".fFffFf.",
                 "..ffff..",
                 "..tfft..",
                 ".ottto..",
                 "w.oooO.w",
                 ".OOooOO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w"), (0, 4, "F"), (7, 4, "F")],
        "raise": [(0, 3, "w"), (7, 3, "w"), (0, 2, "F"), (7, 2, "F")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "F"), (6, 3, "w")],
    },
}

# The Jade Wanderer: the robe takes the main dye, the hat the accent.
JADE = {
    "name": "Jade Wanderer",
    "look": "jade",
    "colors": {
        "h": 0xE8C878, "H": 0xA88A44, "d": 0x1E1A14, "e": 0xA8FFD0, "s": 0xD8A880, "o": 0x2E9A6A,
        "O": 0x186040, "j": 0x7AF0B0, "J": 0x30B078, "g": 0xC87A30, "l": 0x20180E, "w": 0xEAE2CC,
    },
    "mask": {"o": ("main", 1), "O": ("main", 2), "h": ("accent", 0), "H": ("accent", 2)},
    "right": {
        "body": ["...hh...",
                 "..hhhh..",
                 "HhhhhhhH",
                 "..ddde..",
                 ".ojooo..",
                 ".oOjgo..",
                 ".oOOjO.."],
        "legs": [".l..l...", "..ll...."],
        "weapon": [(5, 4, "w")],
        "raise": [(5, 3, "w"), (6, 3, "w")],
        "strike": [(5, 4, "o"), (6, 4, "s"), (7, 4, "w"), (8, 4, "w"), (9, 4, "j"), (10, 3, "J"), (10, 5, "J")],
    },
    "down": {
        "body": ["...hh...",
                 "..hhhh..",
                 "HhhhhhhH",
                 "..edde..",
                 ".ojjjjo.",
                 "w.oojO.w",
                 ".oOgjoO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 4, "w"), (7, 4, "w")],
        "strike": [(1, 5, "w"), (6, 6, "w"), (6, 7, "J"), (1, 7, "J")],
    },
    "up": {
        "body": ["...hh...",
                 "..hhhh..",
                 "HhhhhhhH",
                 "..dddd..",
                 ".oooojo.",
                 "w.oojO.w",
                 ".oOjooO."],
        "legs": ["..l..l..", "..l.....", ".....l.."],
        "weapon": [(0, 5, "w"), (7, 5, "w")],
        "raise": [(0, 4, "w"), (7, 4, "w")],
        "strike": [(1, 3, "w"), (1, 2, "w"), (1, 1, "J"), (6, 3, "w")],
    },
}

SMALL = [LOTUS, STORM, EMBER, JADE]


# --- the Celestial Grandmaster, 16 by 16 ---------------------------------------------------

GRAND_MATERIALS = {
    # skin
    "k": [(255, 226, 198), (236, 188, 150), (190, 140, 104), (124, 88, 62)],
    # white robe (the main dye)
    "w": [(255, 255, 255), (232, 232, 244), (184, 186, 210), (118, 120, 150)],
    # gold: the bead ring, trim and cuffs
    "g": [(255, 244, 170), (244, 200, 76), (186, 136, 40), (112, 80, 22)],
    # the sky sash (the accent)
    "s": [(170, 232, 255), (84, 182, 244), (40, 120, 200), (20, 70, 132)],
    # white hair, brows and beard
    "h": [(255, 255, 255), (222, 226, 238), (172, 178, 200), (112, 118, 142)],
    # dark sandals and the inside of the sleeves
    "d": [(96, 86, 76), (64, 54, 48), (42, 36, 32), (24, 20, 18)],
}
GRAND_GLOWS = {
    "e": (170, 240, 255),
    "x": (255, 255, 255),
    "c": (255, 220, 120),
    "q": (255, 250, 220),
}
GRAND_MASK = {"w": "main", "s": "accent"}

GRANDMASTER = {
    "right": {
        "stand": [
            "...g..g..g......",
            "..g........g....",
            ".g...kkkk...g...",
            ".g..kkkkkk..g...",
            ".g..khhkhe..g...",
            "..g.kkkkkk.g....",
            "...g.hhhhh......",
            "....whhhhhw.....",
            "...wwwhhhwwg....",
            "..wwwssshwwg....",
            "..wwwwwsshw.....",
            "..gwwwwwssw.....",
            "...wwwwwwsw.....",
            "...wwwwwwwsw....",
            "..wwwwwwwwwsw...",
            "..gwwwwwwwwwwg..",
        ],
        "hold": [(10, 9, "dk"), (11, 8, "k")],
        "raise": [(10, 7, "w"), (11, 6, "k"), (11, 5, "kc")],
        "strike": [(10, 9, "wwww"), (14, 9, "kk"), (16, 8, "c"), (16, 9, "q"), (16, 10, "c"), (17, 9, "cq"),
                   (19, 8, "c.x"), (19, 10, "c"), (20, 9, "x")],
        "steps": [
            ["..wwwwwwwwwsw...", "...dd.....dd...."],
            ["...wwwwwwwwsw...", ".....dddd......."],
        ],
    },
    "down": {
        "stand": [
            "...g..g..g..g...",
            "..g..........g..",
            ".g...kkkkkk...g.",
            ".g..kkkkkkkk..g.",
            ".g..khhkkhhk..g.",
            "..g.kekkkkek.g..",
            "....kkhhhhkk....",
            "....whhhhhhw....",
            "...wwhhhhhhww...",
            "..gwwshhhhwwwg..",
            "..dwwwsshhwwwd..",
            "..kgwwwsshwwgk..",
            "...wwwwwsswww...",
            "...wwwwwwsswww..",
            "..wwwwwwwwssww..",
            "..gwwwwwwwwwwg..",
        ],
        "hold": [(2, 11, "k"), (13, 11, "k")],
        "raise": [(2, 9, "kc"), (12, 9, "ck")],
        "strike": [(6, 11, "kqqk"), (7, 12, "cc"), (6, 13, "c..c"), (7, 14, "xx")],
        "steps": [
            ["..wwwwwwwwssww..", "...dd......dd..."],
            ["..wwwwwwwwssww..", "....dd....dd...."],
        ],
    },
    "up": {
        "stand": [
            "...g..g..g..g...",
            "..g..........g..",
            ".g...kkkkkk...g.",
            ".g..kkkkkkkk..g.",
            ".g..kkkkkkkk..g.",
            "..g.kkkkkkkk.g..",
            "....hhhkkhhh....",
            "....wwhhhhww....",
            "...wwwwhhwwww...",
            "..gwwwwwwwwsgg..",
            "..dwwwwwwwsswd..",
            "..kgwwwwwsswgk..",
            "...wwwwwsswww...",
            "...wwwwsswwww...",
            "..wwwwsswwwwww..",
            "..gwwsswwwwwwg..",
        ],
        "hold": [(2, 11, "k"), (13, 11, "k")],
        "raise": [(2, 9, "kc"), (12, 9, "ck")],
        "strike": [(2, 8, "k"), (2, 7, "c"), (13, 8, "k"), (13, 7, "c"), (6, 0, "cqqc")],
        "steps": [
            ["..wwwwsswwwwww..", "...dd......dd..."],
            ["..wwwwsswwwwww..", "....dd....dd...."],
        ],
    },
}


def grand_block(mask=False):
    """The Grandmaster's block, drawn by passkins.block; for the mask, its shading turned into the
    dyes' amounts."""
    passkins.MATERIALS = GRAND_MATERIALS
    passkins.GLOWS = GRAND_GLOWS
    if not mask:
        return passkins.block(GRANDMASTER)
    shade = passkins.shade

    def mask_shade(g):
        img = shade(g)
        out = Image.new("RGBA", img.size, (0, 0, 0, 0))
        for y in range(len(g)):
            for x in range(len(g[0])):
                ch = g[y][x]
                which = GRAND_MASK.get(ch.lower()) if ch != "." and ch not in GRAND_GLOWS else None
                if not which:
                    continue
                idx = GRAND_MATERIALS[ch.lower()].index(img.getpixel((x, y))[:3])
                out.putpixel((x, y), druid.mask_color(which, idx) + (255,))
        return out
    passkins.shade = mask_shade
    try:
        return passkins.block(GRANDMASTER)
    finally:
        passkins.shade = shade


# --- the fountains' icons ------------------------------------------------------------------------

ICON = 16
# the strike's colour by tier (the server's Player.FlowColors)
FLOW_COLORS = [0x9AD8FF, 0x7FE6E0, 0x6EF0A8, 0x4FC3FF, 0x4A8CFF, 0xB48CFF, 0xFF7A9A, 0xFFC857, 0xFFF4D6]
# the beads by tier: wood, jade, gold (lit, base, shadow)
BEADS = [((176, 120, 70), (128, 82, 44), (78, 48, 24))] * 3 + [((150, 250, 196), (60, 186, 126), (28, 110, 72))] * 3 \
    + [((255, 240, 160), (236, 184, 64), (150, 100, 30))] * 3
# the open palm, fingers up: 'p' the water, 'P' its shadow side
PALM = [
    "................",
    "................",
    "................",
    "......p.p.......",
    ".....pp.pp.p....",
    ".....pp.pp.pp...",
    "...p.pp.pp.pp...",
    "...pppppppppp...",
    "....pppppppPp...",
    "....pppppppPP...",
    ".....pppppPP....",
    "......ppPPP.....",
    "................",
    "................",
    "................",
    "................",
]


def hexc(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


def mixc(a, b, t):
    return tuple(int(round(x + (y - x) * t)) for x, y in zip(a, b))


def icon(tier):
    img = Image.new("RGBA", (ICON, ICON), (0, 0, 0, 0))
    color = hexc(FLOW_COLORS[tier])
    deep = mixc(color, (10, 30, 60), 0.55)
    light = mixc(color, (255, 255, 255), 0.6)
    lit, base, shadow = BEADS[tier]
    # a glow behind the palm from tier 6, a soft halo
    if tier >= 6:
        for y in range(ICON):
            for x in range(ICON):
                d = math.hypot(x - 7.5, y - 7.5)
                if d < 5.5:
                    a = int(90 * (1 - d / 5.5))
                    img.putpixel((x, y), color + (a,))
    # the palm of water: lit from the upper left, the palm's heart bright
    for y, row in enumerate(PALM):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            c = deep if ch == "P" else color
            above = PALM[y - 1][x] if y > 0 else "."
            left = row[x - 1] if x > 0 else "."
            if ch == "p" and (above == "." or left == "."):
                c = light
            img.putpixel((x, y), c + (255,))
    for x, y in ((7, 8), (8, 9)):
        img.putpixel((x, y), (255, 255, 255, 255))
    # the ring of beads: twelve round it (more, closer, from tier 3)
    n = 12 if tier < 3 else 14 if tier < 6 else 16
    for i in range(n):
        a = 2 * math.pi * i / n - math.pi / 2
        bx, by = 7.5 + 6.6 * math.cos(a), 7.5 + 6.6 * math.sin(a)
        x, y = int(round(bx)), int(round(by))
        if not (0 <= x < ICON and 0 <= y < ICON):
            continue
        # lit on the upper left of the ring, shadowed down the right
        k = -math.cos(a + math.pi / 4)
        c = lit if k > 0.35 else shadow if k < -0.35 else base
        img.putpixel((x, y), c + (255,))
    # the tassel at the foot of the ring
    for x, y, c in ((7, 15, base), (8, 15, shadow), (7, 14, lit)):
        img.putpixel((x, y), c + (255,))
    # a guru bead at the top, from tier 3 the tier's own colour
    img.putpixel((7, 0), (color if tier >= 3 else lit) + (255,))
    img.putpixel((8, 0), (light if tier >= 3 else base) + (255,))
    # sparkles from tier 5, more by tier 8
    sparks = [] if tier < 5 else [(2, 3)] if tier < 7 else [(2, 3), (13, 12)] if tier < 8 else [(2, 3), (13, 12), (13, 3), (2, 12)]
    for x, y in sparks:
        img.putpixel((x, y), (255, 255, 255, 255))
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if 0 <= x + dx < ICON and 0 <= y + dy < ICON and img.getpixel((x + dx, y + dy))[3] == 0:
                img.putpixel((x + dx, y + dy), light + (200,))
    return img


# --- the sheets ----------------------------------------------------------------------------------

# name, gold, sheet, index, size, look
SKINS = [(s["name"], 200, "monkSkins8", i, None, s["look"]) for i, s in enumerate(SMALL)] + [
    ("Celestial Grandmaster", 800, "monkSkins16", 0, passkins.SIZE, "grandmaster")]


def put_block(path, block):
    """The Monk's block in a player sheet, replacing what was there."""
    sheet = Image.open(path).convert("RGBA")
    y = MONK_INDEX * 24
    sheet.paste(Image.new("RGBA", (56, 24), (0, 0, 0, 0)), (0, y))
    sheet.paste(block, (0, y))
    sheet.save(path)


def main():
    monk, monk_mask = druid.paint(MONK_LOOK), druid.paint(MONK_LOOK, mask=True)
    put_block(PLAYERS, monk)
    put_block(PLAYERS_MASK, monk_mask)
    small = Image.new("RGBA", (56, 24 * len(SMALL)), (0, 0, 0, 0))
    small_mask = small.copy()
    for i, skin in enumerate(SMALL):
        small.paste(druid.paint(skin), (0, i * 24))
        small_mask.paste(druid.paint(skin, mask=True), (0, i * 24))
    small.save(OUT_8)
    small_mask.save(OUT_8_MASK)
    big, big_mask = grand_block(), grand_block(mask=True)
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
        druid.sub(o, "PlayerClassType", "0x%04x" % MONK)
        druid.sub(o, "UnlockLevel", 0)
        druid.sub(o, "Cost", gold)
        druid.sub(o, "SkinLook", look)
        if size:
            druid.sub(o, "Size", size)
    ET.indent(root, "\t")
    text = ("<!-- The Monk's skins sold at the Armoire: generated by tools/draw_monk.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"Monk, {len(SKINS)} skins and 9 fountains")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        root_, ext = os.path.splitext(path)
        sheet = Image.new("RGBA", (56, 24 * (len(SMALL) + 1)), (0, 0, 0, 0))
        sheet.paste(monk, (0, 0))
        sheet.paste(small, (0, 24))
        sheet_mask = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        sheet_mask.paste(monk_mask, (0, 0))
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
