"""Draws the Mechanic's skins sold at the Armoire: four 8 by 8 (200 gold) and one 16 by 16 (800 gold).

    python tools/draw_mechanic_skins.py [--preview <png>]

The 8 by 8 skins are painted pixel by pixel over the Mechanic's own frames (BASE: its sprite in
doomed/images/players.png, row 13, a letter a colour), so they move as the class does: each skin
gives every letter its own colour (`colors`) and paints each way it faces over the head and body
(`paint`: over what's drawn; `add`: where nothing is, a hat's crown or a visor's glow), the same
in each of that way's frames. The 16 by 16 skin, the Titan Artificer, is drawn as the Realm Pass's
skins are (tools/draw_pass_skins.py, whose shading it uses: a letter a material, upper case its
shadow, GLOWS never shaded).

Outputs (generated, don't edit):
- MechanicSkins8x8.png and MechanicSkins16x16.png in the client's custom/images, the classes'
  layout (per skin 7 frames by 3 rows: right, down, up), registered for the Rust client by
  tools/gen_rs_manifest.py (RS_ONLY_CHARS, "mechanicSkins8" and "mechanicSkins16");
- MechanicSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML): the skins, with their price in gold (`Cost`, which the Armoire
  sells them for: ReskinHandler, Database.PurchaseSkin).
Type codes 0xE200 on, fixed here (characters keep their skin by type: never renumber one). Needs
Pillow. `--preview` also saves the sheets as the game draws them (black outline, 8 times bigger).
"""
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import draw_pass_skins as passkins  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
OUT_8 = os.path.join(ASSETS, "custom", "images", "MechanicSkins8x8.png")
OUT_16 = os.path.join(ASSETS, "custom", "images", "MechanicSkins16x16.png")
OUT_XML = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "MechanicSkins.xml"),
           os.path.join(ASSETS, "custom", "xmls", "MechanicSkins.xml")]
MECHANIC = 0xD248

# The Mechanic's own frames (doomed/images/players.png, row 13), a letter a colour: its helmet (a
# light, c grey), the leather of its vest (b dark, d light), goggles (f), face (g), boots and grips
# (h), its dagger (e blade, i hilt). Seven 8-pixel frames to a row: stand, step, step, -, attack,
# and the wide attack; rows right, down, up.
BASE = [
    "..aba.....aba.....................aba.....aba...........",
    ".cdaba.e.cdabae..................cdaba...cdaba..........",
    ".ccfaf.e.ccfafe..................ccfafe..ccfaf..........",
    ".bdggg.e.bdggge..................bdggg.hhbdggg..........",
    "bbdddbbh.bdddbh.................bbdddbb.bbdddbbheee.....",
    "hccada.d.hcadad.................hccada...ccaba..........",
    ".bbbbb...bbbbb...................bbbbb...bbbbb..........",
    ".h...h....hhh....................h....h.h....h..........",
    "..aba.....aba.....aba.............aba.....aba...........",
    ".dabad.e.dabade..dabad...........dabad.i.dabad..........",
    ".cfafc.e.cfafce..cfafc.e.........cfafc.e.efafe..........",
    ".dgggd.e.dgggde..dgggd.e.........bgggb.h.bgggb..........",
    "bbdddbbh.bdddbh..bdddb.e........bbdddbb.hbdddb..........",
    "hcadac.dhcadacd.bhadacbh........hcadac...eadaeh.........",
    ".bbbbb...bbbbb...bbbbb.d.........bbbbb...bbbbb..........",
    ".h...h....h.........h............h...h...h...h..........",
    "...aba.....aba.....aba.............aba.....aba..........",
    "e.dcbcd..edcbcd...dcbcd.........i.dcbed...debed.........",
    "e.ccccc..eccccc.e.ccccc.........e.ccccc...ccccc.........",
    "e.bdddb..ebdddb.e.bdddb.........h.bdddb...bdddb.........",
    "hbbbdbbb.hbbdbb.e.bbdbb..........bbbdbbb..bbdbb.........",
    "d.ccccch.dccccchhbccccc...........ccccch.hccccc.........",
    "..bbbbb...bbbbb.d.bbbbb...........bbbbb..ebbbbb.........",
    "..h...h......h.....h..............h...h..ih...h.........",
]
FRAMES = (0, 1, 2, 4, 5)   # the frames each way has (3 is always empty)
WAYS = ("right", "down", "up")


def hexc(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


# --- the 8 by 8 skins, painted one by one -------------------------------------------------------
#
# `paint` and `add` are 8 rows of 8 for each way, a letter (a colour of `colors`) or `.` to leave
# the pixel as it is; they're laid over each of that way's frames.

# The Steamwright: a black top hat banded in brass, amber goggles in brass rims, a copper coat
# over dark leather, a brass cog on the chest and a brass boiler on the back; a brass wrench.
STEAMWRIGHT = {
    "name": "Steamwright",
    "colors": {
        "a": 0xE0B048, "b": 0x4A2A18, "c": 0x5A3A2A, "d": 0xB8642C, "e": 0xE8C870, "f": 0xFFB030,
        "g": 0xF0C090, "h": 0x221812, "i": 0xA07828, "k": 0x1E1A1E, "n": 0x5E5458, "o": 0xFFE8A0,
    },
    "paint": {
        "right": ["..kkk...", ".kaaak..", "..afaf..", "........", "...a....", "........", "........", "........"],
        "down": ["..kkk...", ".kaaak..", ".afafa..", "........", "...a....", "...o....", "........", "........"],
        "up": ["...kkk..", "..kaaak.", "..kkkkk.", "...aaa..", "...aoa..", "...aaa..", "........", "........"],
    },
    "add": {
        "right": ["..n.....", "......k.", "........", "........", "........", "........", "........", "........"],
        "down": ["..n.....", "......k.", "........", "........", "........", "........", "........", "........"],
        "up": ["...n....", ".k......", "........", "........", "........", "........", "........", "........"],
    },
}

# The Scrapper: a welding helmet of dented steel with a glowing slit, rust-orange overalls
# patched in grey, hazard stripes round the waist and a jagged blade of scrap.
SCRAPPER = {
    "name": "Scrapper",
    "colors": {
        "a": 0x5A5E66, "b": 0x8A3A14, "c": 0x3A3C42, "d": 0xD8702A, "e": 0xB8B8B0, "f": 0xFF9A20,
        "g": 0x484C54, "h": 0x1A1A1C, "i": 0x6A6A6A, "y": 0xFFD830, "k": 0x202022, "w": 0xFFF0B0,
    },
    "paint": {
        "right": [".aaaa...", ".aaaaa..", ".aaffw..", ".aaaaa..", "........", "........", ".ykyky..", "........"],
        "down": ["..aaa...", ".aaaaa..", ".fwffw..", ".aaaaa..", "........", "........", ".ykyky..", "........"],
        "up": ["...aaa..", "..aaaaa.", "..aaaaa.", "..aaaaa.", "........", "........", "..ykyky.", "........"],
    },
    "add": {
        "right": [".a......", "......f.", "........", "........", "........", "........", "........", "........"],
        "down": ["........", "......a.", "......a.", "........", "........", "........", "........", "........"],
        "up": ["........", "........", "........", "........", "........", "........", "........", "........"],
    },
}

# The Arcwright: a hard hat of storm blue with a lamp, cyan goggles burning with charge, a navy
# suit traced with lightning, a coil humming on the back and a blade of crackling arc.
ARCWRIGHT = {
    "name": "Arcwright",
    "colors": {
        "a": 0x2E3E8A, "b": 0x141A3A, "c": 0x2A3460, "d": 0x3A58C0, "e": 0x9AF4FF, "f": 0x60FFFF,
        "g": 0xE8B890, "h": 0x0C0E1E, "i": 0x4A6AD0, "y": 0xFFE040, "x": 0xFFFFFF, "l": 0xC8F8FF,
    },
    "paint": {
        "right": ["..aaa...", ".aaaaa..", "..ffxf..", "........", "...y....", "....y...", "........", "........"],
        "down": ["..axa...", ".aaaaa..", ".fxafx..", "........", "...y....", "...y....", "........", "........"],
        "up": ["...aaa..", "..aaaaa.", "...lel..", "...lyl..", "...lel..", "...y....", "........", "........"],
    },
    "add": {
        "right": ["....x...", "......a.", "......e.", "........", "........", "........", "........", "........"],
        "down": ["........", "......a.", "........", "........", "........", "........", "........", "........"],
        "up": ["....l...", "........", "........", "........", "........", "........", "........", "........"],
    },
}

# The Clockwork Mechanic: an automaton of brass and gold, one red eye over a grille, rivets down
# its chest, a winding key turning in its back and a blade of polished steel.
CLOCKWORK = {
    "name": "Clockwork Mechanic",
    "colors": {
        "a": 0xFFD060, "b": 0x6A4A14, "c": 0xB07A28, "d": 0xD8A038, "e": 0xD0D8E0, "f": 0xFF2A2A,
        "g": 0xE8C070, "h": 0x2A1C08, "i": 0x8A6018, "k": 0x3A2808, "w": 0xFFF4C0,
    },
    "paint": {
        "right": ["..aaa...", ".caaaa..", ".cgfgg..", "..kgkg..", "...w....", "........", "........", "........"],
        "down": ["..aaa...", ".caaac..", ".cgfgc..", ".dkgkd..", "...w....", "...w....", "........", "........"],
        "up": ["...aaa..", "..caaac.", "..ccccc.", "...wkw..", "...kwk..", "........", "........", "........"],
    },
    "add": {
        "right": ["...w....", "........", "......f.", "........", "........", "........", "........", "........"],
        "down": ["...w....", "........", "........", "........", "........", "........", "........", "........"],
        "up": ["....w...", "........", "........", "........", "........", "........", "........", "........"],
    },
}

SMALL = [STEAMWRIGHT, SCRAPPER, ARCWRIGHT, CLOCKWORK]


def paint_small(skin):
    """A skin's 56 by 24 block: the Mechanic's frames in its colours, its paint laid over them."""
    grid = [list(r) for r in BASE]
    for r, way in enumerate(WAYS):
        top = BASE[r * 8]
        for f in FRAMES:
            x0 = f * 8
            # a frame's head may stand a pixel off the standing one's (striking, it leans), so
            # the paint follows where the frame's helmet starts
            dx = top.index("a", x0) - x0 - top.index("a")
            for y in range(8):
                for x in range(8):
                    gy, gx = r * 8 + y, x0 + x + dx
                    if not x0 <= gx < x0 + (16 if f == 5 else 8):
                        continue
                    over = skin["paint"][way][y][x]
                    if over != "." and grid[gy][gx] != ".":
                        grid[gy][gx] = over
                    more = skin["add"][way][y][x]
                    if more != "." and grid[gy][gx] == ".":
                        grid[gy][gx] = more
    img = Image.new("RGBA", (56, 24), (0, 0, 0, 0))
    for y, row in enumerate(grid):
        for x, ch in enumerate(row):
            if ch != ".":
                img.putpixel((x, y), hexc(skin["colors"][ch]) + (255,))
    return img


# --- the Titan Artificer, 16 by 16 ---------------------------------------------------------------
# A Mechanic in a walking exo-frame of gunmetal trimmed in brass: a brass helm with a dark visor
# and a burning cyan lens, a red scarf streaming behind, a reactor glowing cyan in its chest and
# another on its back with copper pipes running to the helm, armoured legs and a great wrench.

TITAN_MATERIALS = {
    # gunmetal plate
    "s": [(150, 170, 186), (92, 108, 124), (58, 70, 84), (32, 40, 50)],
    # brass
    "g": [(255, 236, 150), (224, 176, 72), (164, 116, 40), (102, 66, 22)],
    # copper pipes
    "c": [(244, 168, 112), (196, 104, 56), (138, 64, 34), (86, 36, 20)],
    # the visor's dark
    "v": [(62, 70, 90), (30, 34, 48), (18, 20, 30), (10, 10, 18)],
    # skin
    "k": [(255, 226, 196), (240, 190, 152), (198, 140, 110), (142, 94, 78)],
    # the red scarf
    "r": [(255, 120, 100), (214, 52, 42), (150, 28, 30), (92, 16, 20)],
    # leather straps and belt
    "e": [(206, 156, 104), (164, 112, 72), (118, 78, 50), (78, 50, 34)],
    # boots of dark steel
    "b": [(104, 112, 132), (64, 70, 88), (42, 46, 60), (24, 26, 36)],
    # the wrench's steel
    "t": [(240, 246, 255), (186, 196, 214), (124, 134, 160), (76, 82, 106)],
}
TITAN_GLOWS = {
    "x": (255, 255, 255),
    "o": (96, 240, 255),
    "y": (190, 252, 255),
}


def wrench(x, top, length=8):
    """A great wrench standing jaw up at column x: the open jaw, the shaft, a grip."""
    parts = [(x - 1, top, "t.t"), (x - 1, top + 1, "ttt")]
    for y in range(top + 2, top + length):
        parts.append((x, y, "t"))
    parts.append((x, top + length, "e"))
    return parts


TITAN = {
    "class": MECHANIC,
    "right": {
        "stand": [
            "......gggg......",
            ".....gggggggg...",
            "....cgGgggggggg.",
            "....cgvvvvoxv...",
            "...cc.Gkkkkkk...",
            "...c...kkkkk....",
            "..rrrrrrkkk.....",
            ".rrRrrrrrrss....",
            "r.ccsssssssss...",
            "..cosssyysss....",
            "..cosssyxsgs....",
            "..cGseeeeees....",
            "...sssbbsbbs....",
            "...ss.bs..bs....",
            "....bbs...bs....",
            "...bbbbb.bbbbb..",
        ],
        "hold": wrench(14, 4, 7) + [(12, 10, "kk")],
        "raise": [(12, 0, "t.t"), (12, 1, "ttt"), (13, 2, "t"), (12, 3, "t"), (12, 4, "t"), (11, 5, "t"), (11, 6, "t"),
                  (10, 7, "e"), (10, 8, "kk")],
        "strike": [(10, 10, "kkk")] + [(13 + i, 10, "t") for i in range(8)] + [(21, 8, "tt"), (21, 9, "t"),
                                                                              (21, 11, "t"), (21, 12, "tt"), (22, 10, "y")],
        "steps": [
            ["...ss.bs...bs...", "...bbbbb...bbbbb"],
            ["....sbs..bs.....", "...bbbbbbbbb...."],
        ],
    },
    "down": {
        "stand": [
            "......gggg......",
            "....gggggggg....",
            "...gggggggggg...",
            "...gvoxvvoxvg...",
            "...cGkkkkkkGc...",
            "...c.kkkkkk.c...",
            "...rrrkkkkrrr...",
            "..srrrrrrrrrrs..",
            ".gssssssssssssg.",
            ".gsssssyysssssg.",
            ".csssssyxssssSc.",
            "..sseeeggeeess..",
            "...ssbbssbbss...",
            "...sbs....sbs...",
            "...bbs....sbb...",
            "..bbbbb..bbbbb..",
        ],
        "hold": wrench(14, 5, 8) + [(13, 11, "kk")],
        "raise": wrench(14, 0, 5) + [(13, 6, "kk"), (14, 7, "s")],
        "strike": [(13, 10, "kk"), (14, 11, "e"), (14, 12, "t"), (14, 13, "t"), (13, 14, "ttt"), (13, 15, "t.t")],
        "steps": [
            ["...sbs.....sbs..", "..bbbbb...bbbbb."],
            ["..sbs.....sbs...", ".bbbbb...bbbbb.."],
        ],
    },
    "up": {
        "stand": [
            "......gggg......",
            "....gggggggg....",
            "...gggggggggg...",
            "...ggGggggGgg...",
            "...cggggggggc...",
            "...c.cggggc.c...",
            "...rrrrrrrrrr...",
            "..srrrrrrrrrrs..",
            ".gssscsssscssg..",
            ".gsscgoyogcssg..",
            ".csscgyxygcsSc..",
            "..sscgoyogcss...",
            "...sssssssss....",
            "...sbs....sbs...",
            "...bbs....sbb...",
            "..bbbbb..bbbbb..",
        ],
        "hold": wrench(1, 5, 8) + [(1, 11, "kk")],
        "raise": wrench(1, 0, 5) + [(1, 6, "kk"), (2, 7, "s")],
        "strike": wrench(2, 1, 5) + [(2, 7, "kk"), (1, 0, "y"), (3, 0, "y")],
        "steps": [
            ["...sbs.....sbs..", "..bbbbb...bbbbb."],
            ["..sbs.....sbs...", ".bbbbb...bbbbb.."],
        ],
    },
}

# name, gold, sheet, index, size
SKINS = [(s["name"], 200, "mechanicSkins8", i, None) for i, s in enumerate(SMALL)] + [
    ("Titan Artificer", 800, "mechanicSkins16", 0, passkins.SIZE)]
FIRST_TYPE = 0xE200


def titan_block():
    passkins.MATERIALS = TITAN_MATERIALS
    passkins.GLOWS = TITAN_GLOWS
    return passkins.block(TITAN)


def sub(parent, tag, text=None):
    e = ET.SubElement(parent, tag)
    if text is not None:
        e.text = str(text)
    return e


def main():
    small = Image.new("RGBA", (56, 24 * len(SMALL)), (0, 0, 0, 0))
    for i, skin in enumerate(SMALL):
        small.paste(paint_small(skin), (0, i * 24))
    small.save(OUT_8)
    big = titan_block()
    big.save(OUT_16)
    root = ET.Element("Objects")
    for i, (name, gold, sheet, index, size) in enumerate(SKINS):
        o = ET.SubElement(root, "Object", {"type": "0x%04X" % (FIRST_TYPE + i), "id": name})
        sub(o, "DisplayId", name)
        sub(o, "Skin")
        sub(o, "Class", "Skin")
        at = sub(o, "AnimatedTexture")
        sub(at, "File", sheet)
        sub(at, "Index", index)
        sub(o, "PlayerClassType", "0x%04x" % MECHANIC)
        sub(o, "UnlockLevel", 0)
        sub(o, "Cost", gold)
        if size:
            sub(o, "Size", size)
    ET.indent(root, "\t")
    text = ("<!-- The Mechanic's skins sold at the Armoire: generated by tools/draw_mechanic_skins.py, don't edit -->\n"
            + ET.tostring(root, encoding="unicode") + "\n")
    for path in OUT_XML:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"Mechanic skins: {len(SKINS)}")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        root_, ext = os.path.splitext(path)
        passkins.preview(small, root_ + "-8" + ext, scale=12)
        passkins.preview(big, root_ + "-16" + ext, scale=8)


if __name__ == "__main__":
    main()
