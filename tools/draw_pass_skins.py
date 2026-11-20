"""Draws the Realm Pass's skins: hand-drawn 16 by 16 characters, twice the detail of the classes'
own 8 by 8 sprites, each for one class.

    python tools/draw_pass_skins.py [--preview <png>]

Each skin is drawn here as text, a character a pixel. A lower-case letter is a material (MATERIALS:
its light, base, shade and deep colours), shaded as it's drawn: lit where it meets something else
above or to its left, in shadow below and to its right, so every part of the figure has volume.
The same letter in upper case is that material in shadow (folds, the far side), and a few
characters are lights that are never shaded (GLOWS: eyes, ice crystals, a bowstring).

For each way it faces (right, down, up) there is the figure without its weapon (`stand`), the
weapon held (`hold`), raised (`raise`) and struck out (`strike`) as pieces drawn over it, and
the walking steps (`steps`: rows that replace the foot of the figure, the figure moving with them
where they say). Facing right is a true profile, the way the classes' own side frames are.

Outputs (generated, don't edit):
- PassSkins16x16.png in the client's custom/images: per skin 7 frames of 16 by 3 rows (right,
  down, up), the classes' own layout (stand, walk, walk, -, attack, attack and its weapon's
  reach); registered for the Rust client by tools/gen_rs_manifest.py (RS_ONLY_CHARS, "passSkins16");
- PassSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML): the skin objects, which resources/data/pass.json gives by id
  ({"skin": "Frostcrown Archmage"}).
Type codes are kept stable in tools/pass_skins/types.json (0xE100 on). Needs Pillow.
`--preview` also saves the sheet as the game draws it (black outline, 8 times bigger).
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
OUT_PNG = os.path.join(ASSETS, "custom", "images", "PassSkins16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "PassSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "PassSkins.xml")]
TYPES = os.path.join(ROOT, "tools", "pass_skins", "types.json")
FIRST_TYPE, LAST_TYPE = 0xE100, 0xE1FF
SHEET_NAME = "passSkins16"
CELL = 16
# a 16-pixel skin at size 50 stands as tall as the 8-pixel characters; these stand a third
# taller, so their detail shows and they stand out as the season's prize
SIZE = 65

# material: light, base, shade, deep
MATERIALS = {
    # ice: crowns, horns, blades, frost
    "c": [(236, 252, 255), (140, 222, 255), (64, 156, 226), (30, 86, 166)],
    "i": [(244, 253, 255), (184, 236, 255), (110, 190, 244), (64, 128, 206)],
    # silver trim, a staff's shaft, a bow
    "l": [(255, 255, 255), (208, 218, 238), (142, 156, 192), (90, 100, 140)],
    "t": [(214, 222, 244), (154, 166, 202), (104, 114, 156), (62, 68, 104)],
    # gold
    "g": [(255, 246, 170), (255, 206, 74), (204, 138, 40), (132, 82, 24)],
    # beard, hair, fur
    "w": [(255, 255, 255), (230, 236, 250), (180, 192, 220), (122, 134, 168)],
    "f": [(255, 255, 255), (236, 238, 246), (190, 196, 216), (134, 140, 168)],
    # skin
    "k": [(255, 226, 196), (240, 190, 152), (198, 140, 110), (142, 94, 78)],
    # robes: midnight blue, and the lighter panel
    "r": [(96, 130, 226), (54, 80, 172), (34, 50, 122), (20, 26, 74)],
    "m": [(156, 186, 255), (100, 132, 228), (64, 92, 188), (38, 54, 130)],
    # plate, the helm's darker steel, the visor's dark
    "s": [(238, 246, 255), (170, 188, 218), (112, 130, 172), (66, 78, 118)],
    "h": [(204, 218, 244), (132, 148, 188), (86, 100, 142), (50, 60, 96)],
    "v": [(70, 70, 96), (34, 34, 52), (22, 22, 34), (12, 12, 20)],
    # a cape: navy
    "n": [(76, 102, 188), (42, 60, 134), (26, 36, 92), (14, 18, 54)],
    # boots and leather
    "b": [(104, 106, 138), (66, 68, 98), (44, 46, 70), (26, 28, 46)],
    "e": [(206, 156, 104), (164, 112, 72), (118, 78, 50), (78, 50, 34)],
    "u": [(130, 92, 76), (92, 62, 52), (64, 42, 38), (40, 26, 26)],
    # the aurora: green, teal, violet
    "a": [(176, 255, 216), (104, 238, 182), (52, 182, 152), (30, 122, 112)],
    "q": [(126, 234, 234), (62, 184, 194), (38, 132, 164), (24, 88, 124)],
    "p": [(218, 176, 255), (166, 120, 242), (112, 76, 192), (72, 46, 132)],
}
# lights, never shaded: an ice crystal's heart, cold eyes, the aurora's eyes, a glowing string
GLOWS = {
    "x": (255, 255, 255),
    "o": (128, 248, 255),
    "y": (150, 255, 214),
    "z": (220, 255, 246),
}


# --- The Frostcrown Archmage (Wizard) ------------------------------------------------------------
# A crown of ice shards over a silver circlet with a gold gem, eyes burning with cold light, a
# long white beard, midnight robes edged in silver with a snowflake on the back, frost creeping up
# the hem, and a silver staff crowned with a living ice crystal.

def staff(x, top, length=12, hand=None):
    """A staff standing at column x: its crystal from row `top`, the shaft below."""
    parts = [(x, top, "i"), (x - 1, top + 1, "cxc"), (x - 1, top + 2, "CxC"), (x, top + 3, "c"),
             (x - 1, top + 4, "l.l"), (x, top + 4, "l")]
    for y in range(top + 5, min(CELL, top + 5 + length)):
        parts.append((x, y, "t"))
    if hand is not None:
        parts.append((x, hand, "k"))
    return parts


ARCHMAGE = {
    "class": 0x030e,
    "right": {
        "stand": [
            "......cc........",
            "....c.cxc.c.....",
            "....ccccccc.....",
            "....lllllgl.....",
            "....wwkkkkk.....",
            "....wwkkkok.....",
            "....wwwkkkkk....",
            "...rwwwwwwww....",
            "..rrrrwwwwww....",
            "..rRrrrwwww.....",
            "..rRrrlrww......",
            ".rrRrrrlrr......",
            ".rrRrgggggr.....",
            "rrrRrrrrmrr.....",
            "rrrRrrrrmrrr....",
            "rirrirrrrirr....",
        ],
        "hold": staff(12, 0, hand=9) + [(10, 9, "lk")],
        "raise": staff(11, 0, length=7, hand=6) + [(9, 7, "lr"), (10, 6, "k")],
        "strike": [(9, 9, "lkk")] + [(12 + i, 9, "t") for i in range(9)] + [(20, 8, "l.l"), (21, 7, "c"), (21, 8, "x"),
                                                                            (21, 9, "x"), (21, 10, "c"), (22, 8, "cxc"),
                                                                            (22, 9, "cxc"), (24, 9, "c"), (24, 8, "z")],
        "steps": [
            [".rrRrrrrmrrr....", ".rirbbirbbir...."],
            ["rrrRrrrrmrr.....", "rirrrbbbirr....."],
        ],
    },
    "down": {
        "stand": [
            ".......cc.......",
            ".....c.xc.c.....",
            ".....cccccc.....",
            ".....llggll.....",
            "....wkkkkkkw....",
            "....wkokkokw....",
            "....wwkkkkww....",
            "...rwwwwwwwwr...",
            "..rrrwwwwwwrrr..",
            "..rlrrwwwwrrlr..",
            ".rrlrrrwwrrrlrr.",
            ".krlrrrrrrrrlr..",
            "..rlrgggggrrlr..",
            "..rlrrrmmRrrlr..",
            ".rrlrrrmmRrrlrr.",
            ".rirrirmmirrirr.",
        ],
        "hold": staff(13, 0, hand=11),
        "raise": staff(13, 0, length=6, hand=7) + [(12, 8, "r"), (12, 7, "l")],
        "strike": [(12, 10, "lk"), (13, 11, "t"), (13, 12, "t"), (12, 13, "l.l"), (13, 13, "c"), (12, 14, "cxc"),
                   (12, 15, "CxC")],
        "steps": [
            [".rrlrrrmmRrrlr..", ".bbrrirmmirrirr."],
            ["..rlrrrmmRrrlrr.", ".rirrirmmirrbb.."],
        ],
    },
    "up": {
        "stand": [
            ".......cc.......",
            ".....c.cc.c.....",
            ".....cccccc.....",
            ".....llllll.....",
            "....wwwwwwww....",
            "....wwwwwwww....",
            "....wWwwwwWw....",
            "...rwwWwwWwwr...",
            "..rrrwwwwwwrrr..",
            "..rrrrwwwwrrrr..",
            ".rrrrrrwwrrrrrr.",
            "..rrrrirrirrrrk.",
            "..rrrrriirrrrr..",
            "..rrrrirrirrrr..",
            ".rrRrrrrrrrrRrr.",
            ".rirrirrrrirrir.",
        ],
        "hold": staff(2, 0, hand=11),
        "raise": staff(2, 0, length=6, hand=7) + [(3, 8, "r"), (3, 7, "l")],
        "strike": staff(2, 0, length=5, hand=6) + [(3, 7, "r"), (3, 6, "l"), (1, 0, "z"), (3, 0, "z")],
        "steps": [
            [".rrRrrrrrrrrRr..", ".bbrirrrrirrirr."],
            ["..rRrrrrrrrrRrr.", ".rirrirrrrirrbb."],
        ],
    },
}

# --- The Winterguard (Warrior) -------------------------------------------------------------------
# A horned steel helm, the horns of ice, eyes burning cold through the visor's dark, a white fur
# mantle over frost-steel plate with an ice crest, a gold belt, a navy cape trimmed with frost,
# and a greatsword of ice with a gold guard.

def greatsword(x, top, length=8):
    """A greatsword standing point up at columns x, x+1, its guard below the blade."""
    parts = [(x, top, "x"), (x, top + 1, "c"), (x + 1, top + 1, "C")]
    for y in range(top + 2, top + length):
        parts.append((x, y, "cC"))
    g = top + length
    parts += [(x - 1, g, "gggg"), (x, g + 1, "u"), (x, g + 2, "g")]
    return parts


WINTERGUARD = {
    "class": 0x031d,
    "right": {
        "stand": [
            "..c.............",
            "...c............",
            "...cciiihh......",
            "....hhhhhhh.....",
            "....hHhhvvvv....",
            "....hHhhvov.....",
            ".....hhhhvh.....",
            "..ffffffhh......",
            ".nffffffffs.....",
            ".nnssssicss.....",
            ".nnsssiccsh.....",
            ".nnSssscshh.....",
            ".nngggggggg.....",
            "..nnsssbss......",
            "..nn.bss.bs.....",
            "....bbb.bbb.....",
        ],
        "hold": greatsword(11, 0, 8) + [(10, 10, "hh")],
        "raise": [(12, 0, "x"), (11, 1, "cC"), (11, 2, "cC"), (11, 3, "cC"), (10, 4, "cC"), (9, 5, "gggg"),
                  (10, 6, "u"), (10, 7, "hh"), (10, 8, "s")],
        "strike": [(10, 10, "hh"), (12, 9, "g"), (12, 10, "u"), (12, 11, "g"), (13, 9, "g")]
        + [(13 + i, 10, "c") for i in range(10)] + [(14 + i, 11, "C") for i in range(9)] + [(23, 10, "cx"), (25, 10, "x")],
        "steps": [
            ["..nnsbs..bs.....", "...bbb....bbb..."],
            ["..nn.bssbs......", ".....bbbbb......"],
        ],
    },
    "down": {
        "stand": [
            "..c..........c..",
            "...c........c...",
            "...cchhiihhcc...",
            "....hhhiihhh....",
            "....hvvvvvvh....",
            "....hvovvovh....",
            ".....hhvvhh.....",
            "..ffffhhhhffff..",
            ".fffffsggsfffff.",
            ".ssnsssccsssnss.",
            ".ssnssccccssnSs.",
            ".hhnsssccsssnhh.",
            "..nnggggggggnn..",
            "..nnsssbbsssnn..",
            "..n.sbs..sbs.n..",
            "....bbb..bbb....",
        ],
        "hold": greatsword(13, 0, 8) + [(13, 11, "hh")],
        "raise": [(13, 0, "cC"), (13, 1, "cC"), (13, 2, "cC"), (12, 3, "gggg"), (13, 4, "u"), (13, 5, "hh"),
                  (14, 6, "s"), (14, 7, "s")],
        "strike": [(12, 8, "gggg"), (13, 9, "cC"), (13, 10, "cC"), (13, 11, "cC"), (13, 12, "cC"), (13, 13, "cC"),
                   (13, 14, "cC"), (13, 15, "x"), (12, 7, "hh")],
        "steps": [
            ["..n.sbs...sbs...", "....bbb...bbb..."],
            ["...sbs..sbs..n..", "...bbb..bbb....."],
        ],
    },
    "up": {
        "stand": [
            "..c..........c..",
            "...c........c...",
            "...cchhiihhcc...",
            "....hhhiihhh....",
            "....hhhiihhh....",
            "....hHhiihHh....",
            ".....hhhhhh.....",
            "..ffffhhhhffff..",
            ".ffffffffffffff.",
            ".snnnnnnnnnnnns.",
            ".snnnnniinnnnns.",
            ".hnnnnicciNnnnh.",
            "..nnnnniinnNnn..",
            "..nnnnnnnnnNnn..",
            "..icinnnnnnicic.",
            "....bbb..bbb....",
        ],
        "hold": greatsword(1, 0, 8) + [(1, 11, "hh")],
        "raise": [(1, 0, "cC"), (1, 1, "cC"), (1, 2, "cC"), (0, 3, "gggg"), (1, 4, "u"), (1, 5, "hh"), (1, 6, "s"),
                  (1, 7, "s")],
        "strike": [(1, 0, "x"), (1, 1, "cC"), (2, 2, "cC"), (2, 3, "cC"), (1, 4, "gggg"), (2, 5, "u"), (2, 6, "hh")],
        "steps": [
            ["..icinnnnnnicic.", "...bbb....bbb..."],
            ["..icinnnnnnicic.", "....bbb..bbb...."],
        ],
    },
}

# --- The Aurora Ranger (Archer) ------------------------------------------------------------------
# A white fur hood framing eyes that glow like the northern lights, a leather jerkin with a gold
# buckle and belt, a cloak running green to teal to violet like the aurora, and a silver bow
# strung with light.

def bow(x, top, front=1):
    """A bow standing at column x, bending towards `front` (1 right, -1 left), strung with light."""
    out = x + front
    return [(x, top, "l"), (out, top + 1, "l"), (out, top + 2, "l"), (out, top + 3, "t"), (out, top + 4, "l"),
            (out, top + 5, "t"), (out, top + 6, "l"), (out, top + 7, "l"), (x, top + 8, "l")] + \
        [(x, top + k, "z") for k in range(1, 8)]


AURORA = {
    "class": 0x0307,
    "right": {
        "stand": [
            "....ffff........",
            "...ffffff.......",
            "...fffffff......",
            "...ffffkkkk.....",
            "...fffkkkyk.....",
            "...ffffkkkk.....",
            "..afffffff......",
            ".aaaaeeeee......",
            ".aqqeeeegee.....",
            ".qqqAeeeee......",
            "qqpqegggge......",
            "qppPeeeeee......",
            "ppppeeeee.......",
            ".pp..ee.ee......",
            ".p...uu.uu......",
            ".....uu.uu......",
        ],
        "hold": bow(11, 3) + [(10, 7, "k"), (11, 7, "k")],
        "raise": bow(12, 3) + [(12, 7, "k"), (8, 7, "kiiii"), (13, 7, "x"), (7, 6, "a"), (7, 8, "a")] +
        [(9, 4, "z"), (8, 5, "z"), (9, 9, "z"), (8, 8, "z")],
        "strike": bow(12, 3) + [(12, 7, "k"), (9, 7, "k"), (16, 6, "a"), (16, 8, "a"), (17, 7, "aiiiiii"), (24, 7, "x"),
                               (23, 6, "z"), (23, 8, "z")],
        "steps": [
            [".pp.ee...ee.....", "....uu....uu...."],
            ["pp...eeee.......", ".....uuuu......."],
        ],
    },
    "down": {
        "stand": [
            "......ffff......",
            ".....ffffff.....",
            "....ffffffff....",
            "....fkkkkkkf....",
            "....fkykkykf....",
            "....fFkkkkFf....",
            "...affffffffa...",
            "..aaaeeeeeeaaa..",
            "..aqeeeggeeeqa..",
            "..qqeeeeeeeeqq..",
            ".kqqeeggggeeqqk.",
            "..qpeeeeeeeepq..",
            "..ppeueeeeuepp..",
            "..pp.eee.eee.pp.",
            "...p.uu...uu.p..",
            ".....uu...uu....",
        ],
        "hold": bow(14, 2, front=1) + [(14, 10, "k")],
        "raise": [(5, 4, "l"), (4, 5, "l"), (5, 5, "t"), (6, 5, "l"), (7, 5, "l"), (8, 5, "l"), (9, 5, "t"), (10, 5, "l"),
                  (11, 5, "l"), (10, 4, "l"), (7, 6, "z"), (8, 6, "z"), (7, 7, "x"), (8, 7, "x")],
        "strike": [(4, 8, "l"), (5, 9, "lttl"), (9, 9, "l"), (10, 8, "l"), (7, 10, "x"), (7, 11, "i"), (7, 12, "i"),
                   (7, 13, "i"), (7, 14, "x"), (6, 15, "a.a")],
        "steps": [
            ["..pp.eee..eee.p.", "...p.uu....uu..."],
            [".p.eee..eee.pp..", "...uu....uu.p..."],
        ],
    },
    "up": {
        "stand": [
            "......ffff......",
            ".....ffffff.....",
            "....ffffffff....",
            "....ffffffff....",
            "....ffffffff....",
            "....fFffffFf....",
            "...affffffffa...",
            "..aaaaaaaaaaaa..",
            "..aaaqaaaaqaaa..",
            "..qqqqqqqqqqqq..",
            ".kqqpqqqqqqpqqk.",
            "..qpqppqqppqpq..",
            "..pppppppppppp..",
            "..pPpppPPpppPp..",
            "...p.uu..uu.p...",
            ".....uu..uu.....",
        ],
        "hold": bow(1, 2, front=-1) + [(1, 10, "k"), (10, 3, "a"), (11, 3, "i"), (10, 4, "e"), (10, 5, "e"),
                                         (9, 6, "e")],
        "raise": [(3, 1, "l"), (2, 2, "lttllttl"), (10, 1, "l"), (5, 3, "zz"), (10, 3, "a"), (11, 3, "i"), (10, 4, "e"),
                  (10, 5, "e"), (9, 6, "e")],
        "strike": [(4, 0, "lttl"), (3, 1, "l"), (8, 1, "l"), (5, 1, "zz"), (10, 3, "a"), (11, 3, "i"), (10, 4, "e"),
                   (10, 5, "e"), (9, 6, "e")],
        "steps": [
            ["..pPpppPPpppPp..", "...p.uu...uu.p.."],
            ["..pPpppPPpppPp..", "..p.uu...uu.p..."],
        ],
    },
}

# name, drawing, and the look the Rust client gives it (auras.rs: its particles)
SKINS = [
    ("Frostcrown Archmage", ARCHMAGE, "archmage"),
    ("Winterguard", WINTERGUARD, "winterguard"),
    ("Aurora Ranger", AURORA, "aurora"),
]


def grid(rows, width=CELL):
    assert len(rows) == CELL, len(rows)
    out = []
    for y, row in enumerate(rows):
        assert len(row) == CELL, (y, row, len(row))
        out.append(list(row) + ["."] * (width - CELL))
    return out


def put(g, parts):
    for x, y, text in parts:
        for i, ch in enumerate(text):
            px = x + i
            if ch != "." and 0 <= y < len(g) and 0 <= px < len(g[0]):
                g[y][px] = ch


def shade(g):
    """The text drawn: each material lit from above and the left, shadowed below and right."""
    h, w = len(g), len(g[0])
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))

    def at(x, y):
        return g[y][x] if 0 <= x < w and 0 <= y < h else "."

    for y in range(h):
        for x in range(w):
            ch = g[y][x]
            if ch == ".":
                continue
            if ch in GLOWS:
                img.putpixel((x, y), GLOWS[ch] + (255,))
                continue
            m = ch.lower()
            ramp = MATERIALS[m]

            def out(dx, dy):
                return at(x + dx, y + dy).lower() != m

            if ch.isupper():
                idx = 3 if out(0, 1) and out(1, 0) else 2
            else:
                score = 1.0 * out(0, -1) + 0.6 * out(-1, 0) - 1.0 * out(0, 1) - 0.6 * out(1, 0)
                idx = 0 if score >= 0.9 else 2 if score <= -0.9 else 1
                # the figure's own edge against the dark, at its foot and far side, goes deep
                if at(x, y + 1) == "." and at(x + 1, y) == "." and idx == 2:
                    idx = 3
            img.putpixel((x, y), ramp[idx] + (255,))
    return img


def stepped(rows, legs):
    return rows[:CELL - len(legs)] + legs


def block(skin):
    """A skin's 112 by 48 block: its three rows of seven frames."""
    out = Image.new("RGBA", (CELL * 7, CELL * 3), (0, 0, 0, 0))
    for r, way in enumerate(("right", "down", "up")):
        d = skin[way]
        stand = d["stand"]
        y = r * CELL

        def draw(rows, parts, wide=False):
            g = grid(rows, CELL * 2 if wide else CELL)
            put(g, parts)
            return shade(g)

        frames = [draw(stand, d["hold"])] + [draw(stepped(stand, legs), d["hold"]) for legs in d["steps"]]
        # facing right the classes walk with the standing frame and the first step
        if way == "right":
            frames[2] = None
        for i, f in enumerate(frames):
            if f is not None:
                out.paste(f, (i * CELL, y))
        out.paste(draw(stand, d["raise"]), (4 * CELL, y))
        out.paste(draw(stand, d["strike"], wide=way == "right"), (5 * CELL, y))
    return out


class Types:
    def __init__(self):
        self.data = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, name):
        if name not in self.data:
            used = {int(v, 16) for v in self.data.values()}
            self.data[name] = "0x%04X" % next(t for t in range(FIRST_TYPE, LAST_TYPE + 1) if t not in used)
        return self.data[name]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w", newline="\n") as f:
            json.dump(self.data, f, indent=1, sort_keys=True)
            f.write("\n")


def sub(parent, tag, text=None):
    e = ET.SubElement(parent, tag)
    if text is not None:
        e.text = str(text)
    return e


def preview(sheet, path, scale=8):
    """The sheet as the game draws it: bigger, with a black outline round every figure."""
    big = sheet.resize((sheet.width * scale, sheet.height * scale), Image.NEAREST)
    alpha = big.getchannel("A").filter(ImageFilter.MaxFilter(scale // 2 * 2 + 1))
    out = Image.new("RGBA", big.size, (58, 62, 82, 255))
    out.paste((0, 0, 0, 255), (0, 0), alpha)
    out.alpha_composite(big)
    out.save(path)


def main():
    types = Types()
    sheet = Image.new("RGBA", (CELL * 7, CELL * 3 * len(SKINS)), (0, 0, 0, 0))
    root = ET.Element("Objects")
    for i, (name, skin, look) in enumerate(SKINS):
        sheet.paste(block(skin), (0, i * CELL * 3))
        o = ET.SubElement(root, "Object", {"type": types.get(name), "id": name})
        sub(o, "DisplayId", name)
        sub(o, "Skin")
        sub(o, "Class", "Skin")
        at = sub(o, "AnimatedTexture")
        sub(at, "File", SHEET_NAME)
        sub(at, "Index", i)
        sub(o, "PlayerClassType", "0x%04x" % skin["class"])
        sub(o, "UnlockLevel", 0)
        sub(o, "Size", SIZE)
        # only the Realm Pass gives it, never sold (the account server checks); the Rust client
        # gives it its particles by this look
        sub(o, "PassSkin", look)
    sheet.save(OUT_PNG)
    ET.indent(root, "\t")
    text = ("<!-- The Realm Pass's skins: generated by tools/draw_pass_skins.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    types.save()
    print(f"Realm Pass skins: {len(SKINS)}")
    if "--preview" in sys.argv:
        preview(sheet, sys.argv[sys.argv.index("--preview") + 1])


if __name__ == "__main__":
    main()
