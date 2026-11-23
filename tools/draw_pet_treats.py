"""Draws the Realm Pass's pet food: the Pet Biscuit, the Hearty Pet Stew, the Royal Pet Feast and the
Growth Tonic (source/Shared/resources/xml/custom/CustomItems.xml, Shared/pets/PetFood.cs).

    python tools/draw_pet_treats.py [--preview <png>]

Writes `PetTreats8x8.png` (client/src/kabam/rotmg/assets/custom/images, `petTreats8x8`, which
tools/gen_rs_manifest.py registers for the Rust client), 8 by 8 pixels a cell in the order of
TREATS; the items' <Index> is the cell. Drawn without an outline: the client gives items theirs.
The art is drawn here rather than in an editor so it can be changed and redrawn; it needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "PetTreats8x8.png")
CELL = 8

PALETTE = {
    "B": (236, 196, 126),    # biscuit
    "b": (184, 128, 64),     # biscuit in shade
    "w": (255, 250, 236),    # shine, steam, bone
    "W": (214, 204, 186),    # bone in shade
    "o": (232, 120, 44),     # stew
    "O": (255, 170, 70),     # stew, lit
    "n": (96, 176, 72),      # greens
    "k": (128, 78, 44),      # bowl, cork
    "K": (86, 50, 28),       # bowl in shade
    "r": (186, 74, 40),      # roast
    "R": (226, 116, 60),     # roast, lit
    "g": (222, 168, 52),     # gold
    "G": (255, 226, 120),    # gold, lit
    "t": (60, 200, 140),     # tonic
    "T": (150, 255, 200),    # tonic, lit
    "s": (180, 236, 255),    # glass
}

TREATS = [
    # 0 the Pet Biscuit: a bone-shaped biscuit
    ("Pet Biscuit", [
        "........",
        "BB....BB",
        "BwBBBBBb",
        ".BwBBBb.",
        ".BBBBbb.",
        "BbbbbbBb",
        "bb....bb",
        "........",
    ]),
    # 1 the Hearty Pet Stew: a bowl of stew, steaming, greens in it
    ("Hearty Pet Stew", [
        "..w..w..",
        ".w..w...",
        "..w..w..",
        "OoOnoOoO",
        "kKkkkkkK",
        "kkkkkkKK",
        ".kkkkKK.",
        "..KKKK..",
    ]),
    # 2 the Royal Pet Feast: a roast leg on a golden platter
    ("Royal Pet Feast", [
        "......ww",
        ".....wW.",
        "..RRRw..",
        ".RRRrr..",
        ".Rrrrr..",
        "..rrr...",
        "GgGGGGgG",
        ".gggggg.",
    ]),
    # 3 the Growth Tonic: a green flask, glowing, a spark in it
    ("Growth Tonic", [
        "...kk...",
        "...ss...",
        "..sTTs..",
        ".sTtTts.",
        ".tTwttt.",
        ".ttttTt.",
        ".tttttt.",
        "..tttt..",
    ]),
]


def cell(rows):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    assert len(rows) == CELL, len(rows)
    for y, row in enumerate(rows):
        assert len(row) == CELL, (y, row)
        for x, ch in enumerate(row):
            if ch != ".":
                img.putpixel((x, y), PALETTE[ch] + (255,))
    return img


def main():
    sheet = Image.new("RGBA", (CELL * len(TREATS), CELL), (0, 0, 0, 0))
    for i, (_, rows) in enumerate(TREATS):
        sheet.paste(cell(rows), (i * CELL, 0))
    sheet.save(OUT)
    print(f"wrote {OUT}")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        bg = Image.new("RGBA", sheet.size, (40, 30, 56, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 16, sheet.height * 16), Image.NEAREST).save(path)
        print(f"preview {path}")


if __name__ == "__main__":
    main()
