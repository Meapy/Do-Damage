"""Draws the Oryx chain's untiered items that get sprites of their own: Oryx 4's daggers with red in
their shading, Oryx 5's Ascended gems, and the Sword of Dark Enchantment's and Elysian Staff's shots
and helpers.

    python tools/draw_oryx_uts.py [--preview <png>]

Writes `OryxUts8x8.png` (client/src/kabam/rotmg/assets/custom/images, `oryxUts8x8`, which
tools/gen_rs_manifest.py registers for the Rust client), 8 by 8 pixels a cell in the order of CELLS:

- the Doom Dirk and the Flying Doom Daggers (tools/port_doomed.py TEXTURE_OVERRIDES): their own
  Doomed Realms sprites, white and black kept and red put in the shades between (RED_GREYS maps
  each grey to a red; a two-colour sprite gets its red where its dark meets its white);
- the Ascended Gem of Adoration and of Tenderness (CustomItems.xml): Oryx 4's Infernal gems in Oryx 5's
  silver, purple and gold (their RECOLOUR);
- the Sword of Dark Enchantment's shot, a black blade with a red core and a purple glow, and its
  small dark orb (CustomObjects.xml), drawn here (DRAWN);
- the five Lil' Oryx generators (tools/port_doomed.py LIL_ORYXES, TEXTURE_OVERRIDES): Doomed Realms'
  Lil' Oryx head in each Oryx's colours (LIL_ORYX_HEADS).
And `OryxUts16x16.png` (`oryxUts16x16`), 16 by 16 a cell in the order of CELLS16: the Elysian Staff's
shot, Oryx 5's Celestial Bolt in his silver, purple and gold.
Needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "OryxUts8x8.png")
OUT16 = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "OryxUts16x16.png")
ELYSIUM16 = "kabam/rotmg/assets/custom/images/Elysium16x16.png"
CELL = 8

DOOMED5 = "kabam/rotmg/assets/doomed/images/lofiObj5.png"
DOOMED2 = "kabam/rotmg/assets/doomed/images/lofiObj2.png"
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
    ("Dark Enchantment Slash", None, "slash", "drawn"),
    ("Dark Enchantment Orb", None, "orb", "drawn"),
]


def head(outline, dark, body, horn_shade, horn, eyes):
    """The Lil' Oryx head's colours, from its own: black outline, dark and body, grey horn shade and
    horns, red eyes."""
    return {(0, 0, 0): outline, (35, 40, 42): dark, (23, 23, 23): body, (102, 102, 102): horn_shade,
            (201, 201, 201): horn, (162, 33, 35): eyes}


# Each Oryx's Lil' Oryx generator: the head as Oryx 2 has it, and in the other Oryxes' colours
LIL_ORYX_HEADS = [
    # Oryx 1: a grey helm with red horns
    ("Lil' Oryx Generator", head((0, 0, 0), (54, 54, 62), (88, 88, 98), (120, 18, 24), (200, 36, 40), (255, 255, 255))),
    # Oryx 2: as it was drawn
    ("Lil' Wine Cellar Oryx Generator", head((0, 0, 0), (35, 40, 42), (23, 23, 23), (102, 102, 102), (201, 201, 201), (162, 33, 35))),
    # Oryx 3: black with white horns, his purple glow in the eyes
    ("Lil' Celestial Oryx Generator", head((0, 0, 0), (40, 22, 60), (20, 12, 30), PURPLE[1], (255, 255, 255), PURPLE[2])),
    # Oryx 4: hellfire
    ("Lil' Infernal Oryx Generator", head((40, 4, 8), RED[0], RED[1], (240, 120, 30), (255, 190, 60), GOLD[2])),
    # Oryx 5: silver and gold
    ("Lil' Ascended Oryx Generator", head(SILVER[0], SILVER[1], SILVER[2], GOLD[0], GOLD[1], PURPLE[1])),
]
CELLS += [(item, DOOMED2, 0x5e, colours) for item, colours in LIL_ORYX_HEADS]
# 16 by 16: (object, sheet, index, recolour)
CELLS16 = [
    ("Elysian Bolt", ELYSIUM16, 8, {                     # Celestial Bolt
        (238, 216, 255): SILVER[2], (123, 70, 195): GOLD[1], (70, 33, 128): PURPLE[1]}),
]
# drawn cells: k black, R red, r dark red, v P p purples, W white
INK = {"k": (22, 12, 32), "R": (214, 40, 36), "r": RED[0], "v": PURPLE[2], "P": PURPLE[1], "p": PURPLE[0],
       "W": (255, 255, 255)}
DRAWN = {
    # pointing up and right, as projectiles are drawn
    "slash": ["......vW",
              ".....vRv",
              "....vRkv",
              "...vRkv.",
              "..vRkv..",
              ".vRkv...",
              "vRkv....",
              "Pv......"],
    "orb": ["..pPPp..",
            ".pkkkvp.",
            "pkkrkkvp",
            "PkrWRkkP",
            "PkkRkkkP",
            "pkkkkkkp",
            ".pkkkkp.",
            "..pPPp.."],
}
# the Doom Dirk's greys, dark to light, as reds
RED_GREYS = {(44, 44, 43): RED[0], (76, 77, 77): RED[1], (107, 107, 107): RED[2]}
WHITE, DARK = (252, 251, 251), (44, 44, 43)


def source(path, index, size=CELL):
    img = Image.open(os.path.join(SRC, path)).convert("RGBA")
    cols = img.width // size
    x, y = (index % cols) * size, (index // cols) * size
    return img.crop((x, y, x + size, y + size))


def recolour(cell, colours, what):
    for x in range(cell.width):
        for y in range(cell.height):
            p = cell.getpixel((x, y))
            if p[3]:
                if p[:3] not in colours:
                    sys.exit(f"{what}: no colour for {p[:3]}")
                cell.putpixel((x, y), colours[p[:3]] + (p[3],))
    return cell


def draw_cell(path, index, how):
    if how == "drawn":
        cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        for y, row in enumerate(DRAWN[index]):
            for x, ch in enumerate(row):
                if ch != ".":
                    cell.putpixel((x, y), INK[ch] + (255,))
        return cell
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
    img16 = Image.new("RGBA", (16 * len(CELLS16), 16), (0, 0, 0, 0))
    for n, (what, path, index, colours) in enumerate(CELLS16):
        img16.paste(recolour(source(path, index, 16), colours, what), (n * 16, 0))
    img16.save(OUT16)
    if "--preview" in sys.argv:
        bg = Image.new("RGBA", img.size, (30, 24, 48, 255))
        bg.alpha_composite(img)
        bg.resize((img.width * 16, img.height * 16), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])
    print(f"{len(CELLS)} sprites, {len(CELLS16)} at 16 by 16")


if __name__ == "__main__":
    main()
