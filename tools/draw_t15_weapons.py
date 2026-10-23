"""Draws the T15 weapons' sprites: each is its kind's T14 sprite in Oryx 5's white and gold.

    python tools/draw_t15_weapons.py [--preview <png>]

Writes `T15Weapons8x8.png` (client/src/kabam/rotmg/assets/custom/images), 8 by 8 pixels a cell, a
cell per weapon in the order of SOURCES; the T15 weapons in CustomItems.xml use it as
`t15Weapons8x8` (tools/gen_rs_manifest.py registers it for the Rust client). Each T14 pixel keeps
its place and its brightness rank: the darkest shades become the outline, the rest climb the gold
ramp to white. Needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "T15Weapons8x8.png")
CELL = 8

# (T15 weapon, its T14's sheet under client/src, the T14's index there); cell n is the nth
SOURCES = [
    ("Sword of Ascension", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x3e),       # Sword of Majesty
    ("Dagger of Divine Wrath", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x6e),   # Dagger of Dire Hatred
    ("Bow of the Firmament", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x5e),     # Bow of Deep Enchantment
    ("Wand of Absolution", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x4e),       # Wand of Retribution
    ("Ame-no-Habakiri", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x8e),          # Kusanagi
    ("Seraphic Blades", "kabam/rotmg/assets/doomed/images/lofiObj5.png", 0xdc),         # Eclipse Blades
    ("Fists of the Ascended", "kabam/rotmg/assets/custom/images/CustomAssets8x8.png", 0x7e),  # Fists of the Eternal Master
]

OUTLINE = (44, 30, 14)
# Oryx 5's gold climbing to white (the Divine Forge's gold, tools/draw_forge_sprite.py)
RAMP = [(148, 96, 22), (196, 138, 40), (222, 168, 52), (255, 210, 96), (255, 232, 150), (255, 248, 210), (255, 255, 255)]


def tile(sheet, index):
    img = Image.open(os.path.join(SRC, sheet)).convert("RGBA")
    cols = img.width // CELL
    x, y = index % cols * CELL, index // cols * CELL
    return img.crop((x, y, x + CELL, y + CELL))


def recolour(src):
    px = src.load()
    lit = sorted({sum(px[x, y][:3]) for y in range(CELL) for x in range(CELL) if px[x, y][3]})
    out = Image.new("RGBA", (CELL, CELL))
    o = out.load()
    for y in range(CELL):
        for x in range(CELL):
            r, g, b, a = px[x, y]
            if not a:
                continue
            rank = lit.index(r + g + b) / max(1, len(lit) - 1)
            # the darkest shade (and anything near black) is the outline
            if r + g + b < 120 or (len(lit) > 2 and rank == 0):
                o[x, y] = OUTLINE + (a,)
            else:
                o[x, y] = RAMP[min(len(RAMP) - 1, int(rank * len(RAMP)))] + (a,)
    return out


def main():
    sheet = Image.new("RGBA", (CELL * len(SOURCES), CELL))
    for i, (_, src, index) in enumerate(SOURCES):
        sheet.paste(recolour(tile(src, index)), (i * CELL, 0))
    sheet.save(OUT)
    print("wrote %s: %d cells" % (OUT, len(SOURCES)))
    if "--preview" in sys.argv:
        sheet.resize((sheet.width * 12, sheet.height * 12), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])


if __name__ == "__main__":
    main()
