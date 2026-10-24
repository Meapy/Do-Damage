"""Draws the Oryx chain's untiered items that get sprites of their own: Oryx 4's daggers with red in
their shading, and Oryx 5's Ascended gems.

    python tools/draw_oryx_uts.py [--preview <png>]

Writes `OryxUts8x8.png` (client/src/kabam/rotmg/assets/custom/images, `oryxUts8x8`, which
tools/gen_rs_manifest.py registers for the Rust client), 8 by 8 pixels a cell in the order of CELLS:

- the Doom Dirk and the Flying Doom Daggers (tools/port_doomed.py TEXTURE_OVERRIDES): their own
  Doomed Realms sprites, white and black kept and red put in the shades between (RED_GREYS maps
  each grey to a red; a two-colour sprite gets its red where its dark meets its white);
- the Ascended Gem of Adoration and of Tenderness (CustomItems.xml): Oryx 4's Infernal gems in Oryx 5's
  silver, purple and gold (their RECOLOUR).
Needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "OryxUts8x8.png")
CELL = 8

DOOMED5 = "kabam/rotmg/assets/doomed/images/lofiObj5.png"
D2 = "kabam/rotmg/assets/prod/images/d2LofiObj.png"

# Oryx 5's colours (ElysiumOryx16x16.png, as tools/draw_t15_weapons.py has them)
SILVER = [(40, 30, 86), (104, 112, 170), (152, 170, 216), (238, 242, 252), (255, 255, 255)]
PURPLE = [(64, 30, 112), (124, 64, 196), (176, 124, 240)]
GOLD = [(168, 112, 26), (240, 192, 64), (255, 238, 150)]

# reds for Oryx 4's daggers, dark to light
RED = [(96, 8, 16), (156, 18, 24), (214, 40, 36)]

# (item, sheet, index, how): "greys" puts RED_GREYS's red for each grey, "edge" makes the dark that
# touches white red and the rest a dark red, or a {colour: colour} recolour
CELLS = [
    ("Doom Dirk", DOOMED5, 0xae, "greys"),
    ("Flying Doom Daggers", DOOMED5, 0xc9, "edge"),
    ("Ascended Gem of Adoration", D2, 0x21a, {          # Infernal Gem of Adoration
        (86, 16, 41): PURPLE[0], (142, 20, 20): PURPLE[1], (189, 51, 45): PURPLE[2],
        (255, 185, 165): GOLD[2]}),
    ("Ascended Gem of Tenderness", D2, 0x219, {         # Infernal Gem of Tenderness
        (30, 22, 101): SILVER[0], (20, 61, 142): SILVER[1], (38, 106, 184): SILVER[2],
        (143, 255, 250): GOLD[2]}),
]
# the Doom Dirk's greys, dark to light, as reds
RED_GREYS = {(44, 44, 43): RED[0], (76, 77, 77): RED[1], (107, 107, 107): RED[2]}
WHITE, DARK = (252, 251, 251), (44, 44, 43)


def source(path, index):
    img = Image.open(os.path.join(SRC, path)).convert("RGBA")
    cols = img.width // CELL
    x, y = (index % cols) * CELL, (index // cols) * CELL
    return img.crop((x, y, x + CELL, y + CELL))


def draw_cell(path, index, how):
    cell = source(path, index)
    px = {(x, y): cell.getpixel((x, y)) for x in range(CELL) for y in range(CELL)}
    for (x, y), p in px.items():
        if p[3] == 0:
            continue
        rgb = p[:3]
        if how == "greys":
            new = RED_GREYS.get(rgb, rgb)
        elif how == "edge":
            touches = any(px.get((x + dx, y + dy), (0, 0, 0, 0))[:3] == WHITE
                          for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            new = (RED[2] if touches else RED[0]) if rgb == DARK else rgb
        else:
            if rgb not in how:
                sys.exit(f"{path} {index:#x}: no colour for {rgb}")
            new = how[rgb]
        cell.putpixel((x, y), new + (255,))
    return cell


def main():
    img = Image.new("RGBA", (CELL * len(CELLS), CELL), (0, 0, 0, 0))
    for n, (_, path, index, how) in enumerate(CELLS):
        img.paste(draw_cell(path, index, how), (n * CELL, 0))
    img.save(OUT)
    if "--preview" in sys.argv:
        bg = Image.new("RGBA", img.size, (30, 24, 48, 255))
        bg.alpha_composite(img)
        bg.resize((img.width * 16, img.height * 16), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])
    print(f"{len(CELLS)} sprites")


if __name__ == "__main__":
    main()
