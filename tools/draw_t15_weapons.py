"""Draws the T15 weapons' sprites: each is its kind's T14 sprite in Oryx 5's own colours.

    python tools/draw_t15_weapons.py [--preview <png>]

Writes `T15Weapons8x8.png` (client/src/kabam/rotmg/assets/custom/images), 8 by 8 pixels a cell, a
cell per weapon in the order of SOURCES; the T15 weapons in CustomItems.xml use it as
`t15Weapons8x8` (tools/gen_rs_manifest.py registers it for the Rust client). Each T14 pixel keeps
its place; its colour becomes a shade of one of Oryx the Ascended God's three colours
(ElysiumOryx16x16.png): the blue-silver of his armour for blades and bodies, the purple of his shield
for grips and gems, and the orange-gold of his trim for guards and fittings. Each SOURCES entry
says which T14 colour takes which shade, and a colour it leaves out stops the script. Needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "T15Weapons8x8.png")
CELL = 8

# Oryx 5's colours, dark to light, as his sprite has them (ElysiumOryx16x16.png)
SILVER = [(40, 30, 86), (104, 112, 170), (152, 170, 216), (238, 242, 252), (255, 255, 255)]
PURPLE = [(64, 30, 112), (124, 64, 196), (176, 124, 240)]
GOLD = [(168, 112, 26), (240, 192, 64), (255, 238, 150)]

# (T15 weapon, its T14's sheet under client/src, the T14's index there, {T14 colour: Oryx shade});
# cell n is the nth
SOURCES = [
    ("Sword of Ascension", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x3e, {   # Sword of Majesty
        (49, 45, 89): SILVER[2], (94, 94, 94): SILVER[3],                            # blade
        (173, 126, 43): GOLD[0], (247, 221, 107): GOLD[1], (121, 45, 207): PURPLE[1]}),  # hilt, gem
    ("Dagger of Divine Wrath", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x6e, {  # Dagger of Dire Hatred
        (0, 60, 255): SILVER[2], (103, 129, 214): SILVER[1], (173, 191, 253): SILVER[3],
        (255, 255, 255): SILVER[4],                                                  # blade
        (171, 171, 171): GOLD[1], (45, 45, 44): PURPLE[1], (94, 94, 94): GOLD[0]}),  # guard, grip, pommel
    ("Bow of the Firmament", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x5e, {   # Bow of Deep Enchantment
        (255, 255, 255): SILVER[3], (94, 94, 94): SILVER[1],                         # limbs
        (103, 129, 214): PURPLE[2], (51, 61, 130): PURPLE[1],                        # string
        (45, 45, 44): GOLD[1]}),                                                     # tips
    ("Wand of Absolution", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x4e, {     # Wand of Retribution
        (90, 10, 113): PURPLE[0], (124, 28, 219): PURPLE[1], (181, 49, 235): PURPLE[2],
        (233, 208, 255): SILVER[4],                                                  # head
        (173, 126, 43): GOLD[0], (247, 221, 107): GOLD[1],                           # collar, end
        (45, 45, 44): SILVER[1], (94, 94, 94): SILVER[2]}),                          # shaft
    ("Ame-no-Habakiri", "kabam/rotmg/assets/prod/images/lofiObj5b.png", 0x8e, {        # Kusanagi
        (255, 255, 255): SILVER[3], (140, 144, 211): SILVER[2], (53, 60, 211): SILVER[1],  # blade
        (0, 118, 127): GOLD[1], (11, 56, 71): GOLD[0],                               # tassel, guard
        (94, 94, 94): PURPLE[1], (45, 45, 44): PURPLE[0], (12, 12, 12): SILVER[0]}),  # wrap, grip
    ("Seraphic Blades", "kabam/rotmg/assets/doomed/images/lofiObj5.png", 0xdc, {       # Eclipse Blades
        (149, 132, 11): SILVER[1], (247, 180, 0): SILVER[2], (243, 207, 9): SILVER[3],
        (247, 210, 52): SILVER[4],                                                   # blades
        (147, 5, 6): GOLD[1], (0, 59, 118): PURPLE[1]}),                             # gems
    ("Fists of the Ascended", "kabam/rotmg/assets/custom/images/CustomAssets8x8.png", 0x7e, {  # Fists of the Eternal Master
        (240, 230, 200): SILVER[2], (248, 240, 220): SILVER[3], (255, 250, 240): SILVER[4],  # gauntlet
        (64, 224, 255): PURPLE[1],                                                   # gems
        (200, 160, 72): GOLD[0], (232, 176, 48): GOLD[1]}),                          # cuff
]


def tile(sheet, index):
    img = Image.open(os.path.join(SRC, sheet)).convert("RGBA")
    cols = img.width // CELL
    x, y = index % cols * CELL, index // cols * CELL
    return img.crop((x, y, x + CELL, y + CELL))


def recolour(name, src, roles):
    px = src.load()
    out = Image.new("RGBA", (CELL, CELL))
    o = out.load()
    for y in range(CELL):
        for x in range(CELL):
            r, g, b, a = px[x, y]
            if not a:
                continue
            if (r, g, b) not in roles:
                sys.exit("%s: no Oryx colour for the T14's %s" % (name, (r, g, b)))
            o[x, y] = roles[(r, g, b)] + (a,)
    return out


def main():
    sheet = Image.new("RGBA", (CELL * len(SOURCES), CELL))
    for i, (name, src, index, roles) in enumerate(SOURCES):
        sheet.paste(recolour(name, tile(src, index), roles), (i * CELL, 0))
    sheet.save(OUT)
    print("wrote %s: %d cells" % (OUT, len(SOURCES)))
    if "--preview" in sys.argv:
        sheet.resize((sheet.width * 12, sheet.height * 12), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])


if __name__ == "__main__":
    main()
