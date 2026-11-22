"""Draws the Mechanic's skins sold at the Armoire: four 8 by 8 (200 gold) and one 16 by 16 (800 gold).

    python tools/draw_mechanic_skins.py [--preview <png>]

Every skin is painted pixel by pixel, a letter a colour. An 8 by 8 skin paints, for each way it
faces, its figure (`body`, rows 0 to 6), its feet (`legs`: standing, then a step, then the other
step facing down or up; row 7) and its weapon apart (`weapon`, (x, y, letter) pixels), so its
frames are put together as the classes' are: standing, two steps (one facing right), the weapon
raised a pixel (`raise`, or the weapon one row up) and the strike (`strike`; facing right the
frame is 16 wide and the weapon thrusts out ahead). Its colours are its own (`colors`).

The 16 by 16 skin, the Titan Artificer, is drawn as the Realm Pass's skins are
(tools/draw_pass_skins.py `block`, whose shading it uses: a letter a material, lit from above and
the left, upper case its shadow, the GLOWS never shaded).

Outputs (generated, don't edit):
- MechanicSkins8x8.png and MechanicSkins16x16.png in the client's custom/images, the classes'
  layout (per skin 7 frames by 3 rows: right, down, up), registered for the Rust client by
  tools/gen_rs_manifest.py (RS_ONLY_CHARS, "mechanicSkins8" and "mechanicSkins16");
- MechanicSkins.xml in source/Shared/resources/xml/custom and the client's custom/xmls (the Rust
  client's RS_ONLY_OBJECT_XML): the skins, with their price in gold (`Cost`, which the Armoire
  sells them for: ReskinHandler, Database.PurchaseSkin).
Type codes 0xE200 on, fixed here (characters keep their skin by type: never renumber one). Needs
Pillow. `--preview` also saves the sheets as the game draws them (black outline, bigger).
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
WAYS = ("right", "down", "up")


def hexc(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


# --- the 8 by 8 skins, painted one by one -------------------------------------------------------

# The Steamwright: a tall black top hat banded in brass, amber goggles on the band, a ruddy face
# with a curled moustache, an oxblood frock coat over a cream shirt with brass buttons, a wrench.
STEAMWRIGHT = {
    "name": "Steamwright",
    "colors": {
        "K": 0x1C1620, "k": 0x52445A, "B": 0xC0862C, "G": 0xFFB030, "f": 0xF2C29A, "e": 0x2A1A14,
        "m": 0x8A4A24, "c": 0xB0442E, "C": 0x6E2218, "v": 0xF0E4C8, "b": 0xFFD060, "w": 0xD0D6E0,
        "W": 0x7A808C, "l": 0x2B1D18,
    },
    "right": {
        "body": ["..KKK...",
                 "..KkK...",
                 ".KBGBK..",
                 "..fffe..",
                 "Cccvmmf.",
                 "Cccvbcc.",
                 ".CcccC.."],
        "legs": [".l...l..", "..ll...."],
        "weapon": [(6, 1, "w"), (7, 0, "w"), (7, 1, "w"), (7, 2, "w"), (7, 3, "W"), (7, 4, "W")],
        "strike": [(7, 4, "W"), (8, 4, "W"), (9, 4, "w"), (10, 3, "w"), (10, 4, "w"), (10, 5, "w"),
                   (11, 3, "w"), (11, 5, "w")],
    },
    "down": {
        "body": ["..KKK...",
                 "..KkK...",
                 ".KGBGK..",
                 ".fefef..",
                 "cCmmmCf.",
                 "Ccvbvcc.",
                 ".CcccC.."],
        "legs": [".l...l..", ".l......", ".....l.."],
        "weapon": [(6, 1, "w"), (7, 0, "w"), (7, 1, "w"), (7, 2, "w"), (7, 3, "W"), (7, 4, "W")],
        "strike": [(7, 4, "W"), (7, 5, "W"), (7, 6, "w"), (6, 7, "w"), (7, 7, "w")],
    },
    "up": {
        "body": ["...KKK..",
                 "...kKK..",
                 "..KBBBK.",
                 "...mmm..",
                 ".fcCCCc.",
                 ".cccCcc.",
                 "..CcCc.."],
        "legs": ["..l...l.", "..l.....", "......l."],
        "weapon": [(1, 1, "w"), (0, 0, "w"), (0, 1, "w"), (0, 2, "w"), (0, 3, "W"), (0, 4, "W")],
        "strike": [(1, 3, "W"), (1, 2, "w"), (0, 1, "w"), (2, 1, "w"), (0, 0, "w"), (2, 0, "w")],
    },
}

# The Scrapper: a dented welding helmet with a white-hot slit, rust-orange overalls with a hazard
# stripe, a green oxygen tank on the back and a jagged blade of rusty scrap.
SCRAPPER = {
    "name": "Scrapper",
    "colors": {
        "h": 0xA8B0BA, "H": 0x646A74, "d": 0x34383F, "S": 0xFF8A1A, "s": 0xFFF0B0, "o": 0xE8782A,
        "O": 0xA24818, "y": 0xFFD23A, "k": 0x1E1E22, "g": 0x5A4030, "w": 0xD6DBE2, "r": 0xB8582A,
        "l": 0x2A2420, "t": 0x9AD0A0, "T": 0x4E8A5A,
    },
    "right": {
        "body": ["..hHH...",
                 ".hHHHd..",
                 ".HHSsd..",
                 "..dHd...",
                 "oykykog.",
                 "Ooooooo.",
                 ".OoOoO.."],
        "legs": [".l...l..", "..ll...."],
        "weapon": [(7, 0, "w"), (7, 1, "w"), (7, 2, "r"), (7, 3, "w"), (7, 4, "k")],
        "strike": [(7, 4, "k"), (8, 4, "w"), (9, 4, "r"), (10, 4, "w"), (11, 4, "w"), (9, 3, "w")],
    },
    "down": {
        "body": ["..hHH...",
                 ".hHHHd..",
                 ".dSsSd..",
                 "..dHd...",
                 "oykykog.",
                 "Ooooooo.",
                 ".Oo.oO.."],
        "legs": [".l...l..", ".l......", ".....l.."],
        "weapon": [(7, 0, "w"), (7, 1, "w"), (7, 2, "r"), (7, 3, "w"), (7, 4, "k")],
        "strike": [(7, 4, "k"), (7, 5, "w"), (7, 6, "r"), (7, 7, "w")],
    },
    "up": {
        "body": ["...HHh..",
                 "..dHHHh.",
                 "..dHHHH.",
                 "...dHd..",
                 ".goTtoy.",
                 ".ooTtoo.",
                 "..OTtO.."],
        "legs": ["..l...l.", "..l.....", "......l."],
        "weapon": [(0, 0, "w"), (0, 1, "w"), (0, 2, "r"), (0, 3, "w"), (0, 4, "k")],
        "strike": [(1, 3, "k"), (1, 2, "w"), (1, 1, "r"), (1, 0, "w")],
    },
}

# The Arcwright: a storm-blue hard hat with a white lamp, cyan goggles, a navy suit with a yellow
# lightning bolt, a copper coil humming on the back and a blade of crackling arc.
ARCWRIGHT = {
    "name": "Arcwright",
    "colors": {
        "n": 0x4A62B8, "N": 0x1E2A5A, "x": 0xFFFFFF, "c": 0x7AF8FF, "C": 0x2AB8E8, "f": 0xE8B890,
        "B": 0x3A64C8, "b": 0x7AA4F4, "y": 0xFFE23A, "g": 0x22222E, "k": 0xD07A3A, "l": 0x141830,
    },
    "right": {
        "body": ["..nnn...",
                 ".nNNNx..",
                 ".NNfcc..",
                 "..fff...",
                 "bBByBBg.",
                 "NBBByBB.",
                 ".NBNBN.."],
        "legs": [".l...l..", "..ll...."],
        "weapon": [(7, 0, "x"), (7, 1, "c"), (7, 2, "c"), (7, 3, "C"), (7, 4, "k")],
        "strike": [(7, 4, "k"), (8, 4, "C"), (9, 4, "c"), (10, 4, "c"), (11, 4, "x"), (11, 3, "c"),
                   (10, 5, "c")],
    },
    "down": {
        "body": ["..nnn...",
                 ".nNxNn..",
                 ".NcfcN..",
                 "..fff...",
                 "bBBByBg.",
                 "NBByBBN.",
                 ".BN.NB.."],
        "legs": [".l...l..", ".l......", ".....l.."],
        "weapon": [(7, 0, "x"), (7, 1, "c"), (7, 2, "c"), (7, 3, "C"), (7, 4, "k")],
        "strike": [(7, 4, "k"), (7, 5, "C"), (7, 6, "c"), (7, 7, "x"), (6, 6, "c")],
    },
    "up": {
        "body": ["...nnn..",
                 "..nNNNn.",
                 "..NNNNN.",
                 "...NNN..",
                 ".gBkckB.",
                 ".NBkCkB.",
                 "..NBBN.."],
        "legs": ["..l...l.", "..l.....", "......l."],
        "weapon": [(0, 0, "x"), (0, 1, "c"), (0, 2, "c"), (0, 3, "C"), (0, 4, "k")],
        "strike": [(1, 3, "k"), (1, 2, "C"), (1, 1, "c"), (1, 0, "x"), (0, 0, "c"), (2, 0, "c")],
    },
}

# The Clockwork Mechanic: an automaton of brass and gold with one red eye over a grille, steel
# arms, a gear on its chest, a winding key in its back and a blade of polished steel.
CLOCKWORK = {
    "name": "Clockwork Mechanic",
    "colors": {
        "A": 0xFFE07A, "a": 0xE0A63A, "D": 0x9A6A1E, "d": 0x5A3A10, "r": 0xFF2A2A, "R": 0xFF9A7A,
        "g": 0x3A2A10, "s": 0xDDE4EC, "S": 0x7A8290, "w": 0xFFF6C8,
    },
    "right": {
        "body": ["..AAa...",
                 ".AaaaD..",
                 "waaarD..",
                 "..DgD...",
                 "SDAwADs.",
                 ".aDaDa..",
                 ".dDddD.."],
        "legs": [".d...d..", "..dd...."],
        "weapon": [(7, 0, "w"), (7, 1, "s"), (7, 2, "s"), (7, 3, "S"), (7, 4, "D")],
        "strike": [(7, 4, "D"), (8, 4, "S"), (9, 4, "s"), (10, 4, "s"), (11, 4, "w")],
    },
    "down": {
        "body": ["..AAA...",
                 ".AaaaD..",
                 ".aRrRD..",
                 ".DgggD..",
                 "SaAwAas.",
                 ".DaDaD..",
                 ".dDddD.."],
        "legs": [".d...d..", ".d......", ".....d.."],
        "weapon": [(7, 0, "w"), (7, 1, "s"), (7, 2, "s"), (7, 3, "S"), (7, 4, "D")],
        "strike": [(7, 4, "D"), (7, 5, "S"), (7, 6, "s"), (7, 7, "w")],
    },
    "up": {
        "body": ["...AAA..",
                 "..DaaaA.",
                 "..DaaaA.",
                 "...wAw..",
                 ".SDaADS.",
                 "..aDaD..",
                 "..dDddD."],
        "legs": ["..d...d.", "..d.....", "......d."],
        "weapon": [(0, 0, "w"), (0, 1, "s"), (0, 2, "s"), (0, 3, "S"), (0, 4, "D")],
        "strike": [(1, 3, "D"), (1, 2, "S"), (1, 1, "s"), (1, 0, "w")],
    },
}

SMALL = [STEAMWRIGHT, SCRAPPER, ARCWRIGHT, CLOCKWORK]


def small_frame(skin, way, legs, weapon, width=8):
    """One frame of an 8 by 8 skin: its figure facing `way`, these feet, the weapon where given."""
    rows = skin[way]["body"]
    assert len(rows) == 7 and all(len(r) == 8 for r in rows) and len(legs) == 8, (skin["name"], way)
    g = [list(r) + ["."] * (width - 8) for r in rows] + [list(legs) + ["."] * (width - 8)]
    for x, y, ch in weapon:
        if 0 <= y < 8 and 0 <= x < width:
            g[y][x] = ch
    return g


def paint_small(skin):
    """A skin's 56 by 24 block: its frames facing right, down and up."""
    out = Image.new("RGBA", (56, 24), (0, 0, 0, 0))
    for r, way in enumerate(WAYS):
        d = skin[way]
        legs = d["legs"]
        frames = {0: small_frame(skin, way, legs[0], d["weapon"]),
                  1: small_frame(skin, way, legs[1], d["weapon"])}
        # facing right the classes walk with the standing frame and one step
        if way != "right":
            frames[2] = small_frame(skin, way, legs[2], d["weapon"])
        raised = d.get("raise") or [(x, y - 1, ch) for x, y, ch in d["weapon"]]
        frames[4] = small_frame(skin, way, legs[0], raised)
        frames[5] = small_frame(skin, way, legs[0], d["strike"], 16 if way == "right" else 8)
        for f, g in frames.items():
            for y, row in enumerate(g):
                for x, ch in enumerate(row):
                    if ch != ".":
                        out.putpixel((f * 8 + x, r * 8 + y), hexc(skin["colors"][ch]) + (255,))
    return out


# --- the Titan Artificer, 16 by 16 ---------------------------------------------------------------
# A Mechanic in a walking exo-frame: a brass diving helm with a porthole burning cyan, great brass
# pauldrons over gunmetal, copper arms, a reactor glowing in the chest and a boiler on the back
# (its core seen through a window), a red scarf, armoured legs and a great wrench.

TITAN_MATERIALS = {
    # gunmetal plate
    "s": [(170, 188, 204), (104, 120, 138), (64, 76, 92), (34, 42, 54)],
    # brass
    "g": [(255, 240, 160), (232, 184, 76), (170, 120, 40), (104, 68, 22)],
    # copper arms, pipes and the boiler
    "c": [(250, 176, 120), (204, 110, 58), (142, 66, 34), (88, 36, 20)],
    # the porthole's dark glass
    "v": [(70, 84, 110), (36, 42, 60), (22, 24, 36), (12, 12, 20)],
    # the red scarf
    "r": [(255, 124, 104), (220, 56, 44), (152, 28, 30), (92, 16, 20)],
    # leather gauntlets, belt and grip
    "e": [(210, 160, 108), (166, 114, 74), (120, 80, 52), (80, 52, 34)],
    # boots of dark steel
    "b": [(110, 118, 140), (68, 74, 94), (44, 48, 64), (24, 26, 38)],
    # the wrench's steel
    "t": [(244, 248, 255), (190, 200, 218), (126, 136, 162), (78, 84, 108)],
}
TITAN_GLOWS = {
    "x": (255, 255, 255),
    "o": (90, 236, 255),
    "y": (180, 250, 255),
}


def wrench(x, top, length):
    """A great wrench standing jaw up at column x: the open jaw, the shaft, a grip of leather."""
    parts = [(x - 1, top, "t.t"), (x - 1, top + 1, "ttt")]
    for y in range(top + 2, top + length):
        parts.append((x, y, "t"))
    parts.append((x, top + length, "e"))
    return parts


def wrench_down(x, top, length):
    """A great wrench swung jaw down at column x from its grip at row `top`."""
    parts = [(x, top, "e")]
    for y in range(top + 1, top + length):
        parts.append((x, y, "t"))
    return parts + [(x - 1, top + length, "ttt"), (x - 1, top + length + 1, "t.t")]


TITAN = {
    "right": {
        "stand": [
            ".......gggg.....",
            "......gggggg....",
            "......gggvvvg...",
            "......ggvoxxv...",
            "......gggvoov...",
            ".......gGGgg....",
            ".rr..cggrrrr....",
            "r.rcccgggGgs....",
            "..coyocgGGgss...",
            "..cyxycsGsssc...",
            "..coyocsssssc...",
            "..ccccceeggee...",
            ".....ssSsss.....",
            ".....ss..ss.....",
            ".....bs..sb.....",
            "....bbb..bbb....",
        ],
        "hold": wrench(14, 3, 8) + [(13, 11, "e")],
        "raise": wrench(14, 0, 8) + [(12, 9, "ee")],
        "strike": [(11, 10, "ee")] + [(13 + i, 10, "t") for i in range(6)] + [
            (19, 8, "tt"), (19, 9, "t"), (19, 11, "t"), (19, 12, "tt"), (20, 10, "y")],
        "steps": [
            ["....ss...ss.....", "...bs.....sb....", "..bbb.....bbb..."],
            [".....sssss......", "......bsb.......", ".....bbbbb......"],
        ],
    },
    "down": {
        "stand": [
            "......gggg......",
            ".....gggggg.....",
            ".....gvvvvg.....",
            ".....voxxov.....",
            ".....gvoovg.....",
            "......gGGg......",
            "..ggg.rrrr.ggg..",
            ".gggGgsrrsgGggg.",
            ".gGG.ssyyss.GGg.",
            "..cc.syxxys.cc..",
            "..cc.ssyyss.cc..",
            "..ee.eeggee.ee..",
            ".....ssSSss.....",
            ".....ss..ss.....",
            ".....bs..sb.....",
            "....bbb..bbb....",
        ],
        "hold": wrench(14, 3, 8),
        "raise": wrench(14, 0, 6) + [(13, 7, "e")],
        "strike": wrench_down(13, 9, 5) + [(11, 15, "y"), (15, 15, "y"), (12, 11, "e")],
        "steps": [
            [".....ss..ss.....", ".....bs...sb....", "....bbb...bbb..."],
            [".....ss..ss.....", "....bs...sb.....", "...bbb...bbb...."],
        ],
    },
    "up": {
        "stand": [
            "......gggg......",
            ".....gggggg.....",
            ".....gggggg.....",
            ".....gGggGg.....",
            ".....gggggg.....",
            "......cggc......",
            "..ggg.crrc.ggg..",
            ".gggGcrrrrcGggg.",
            ".gGG.coyyoc.GGg.",
            "..cc.cyxxyc.cc..",
            "..cc.coyyoc.cc..",
            "..ee.scccCs.ee..",
            ".....ssSSss.....",
            ".....ss..ss.....",
            ".....bs..sb.....",
            "....bbb..bbb....",
        ],
        "hold": wrench(1, 3, 8),
        "raise": wrench(1, 0, 6) + [(2, 7, "e")],
        "strike": wrench(2, 0, 5) + [(2, 6, "ee"), (1, 0, "y"), (3, 0, "y")],
        "steps": [
            [".....ss..ss.....", ".....bs...sb....", "....bbb...bbb..."],
            [".....ss..ss.....", "....bs...sb.....", "...bbb...bbb...."],
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
