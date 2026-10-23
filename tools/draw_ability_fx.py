"""Draws ability shots recoloured from existing ones, for items that need a look of their own.

    python tools/draw_ability_fx.py [--preview <png>]

Writes `AbilityFx8x8.png` (client/src/kabam/rotmg/assets/custom/images), 8 by 8 pixels a cell, a
cell per entry of SOURCES; the projectiles in CustomProj.xml use it as `abilityFx8x8`
(tools/gen_rs_manifest.py registers it for the Rust client). Needs Pillow.
"""
import colorsys
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "AbilityFx8x8.png")
CELL = 8

# (projectile, the sheet it's drawn from under client/src, its index there, recolour)
SOURCES = [
    # the Thousand Suns Spell's shot: the Idol Wave with its orange turned bright yellow
    ("Thousand Suns Wave", "kabam/rotmg/assets/prod/images/lofiObj3.png", 0x4e, "yellow"),
]


def tile(sheet, index):
    img = Image.open(os.path.join(SRC, sheet)).convert("RGBA")
    cols = img.width // CELL
    x, y = index % cols * CELL, index // cols * CELL
    return img.crop((x, y, x + CELL, y + CELL))


def yellow(src):
    """Reds and oranges (hues up to 50 degrees) become a bright yellow of the same lightness."""
    out = src.copy()
    px = out.load()
    for y in range(CELL):
        for x in range(CELL):
            r, g, b, a = px[x, y]
            if not a:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.25 and (h < 50 / 360 or h > 340 / 360):
                r, g, b = colorsys.hsv_to_rgb(56 / 360, min(1.0, s), min(1.0, v * 1.15 + 0.1))
                px[x, y] = (round(r * 255), round(g * 255), round(b * 255), a)
    return out


RECOLOURS = {"yellow": yellow}


def main():
    sheet = Image.new("RGBA", (CELL * len(SOURCES), CELL))
    for i, (_, src, index, how) in enumerate(SOURCES):
        sheet.paste(RECOLOURS[how](tile(src, index)), (i * CELL, 0))
    sheet.save(OUT)
    print("wrote %s: %d cells" % (OUT, len(SOURCES)))
    if "--preview" in sys.argv:
        sheet.resize((sheet.width * 16, sheet.height * 16), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])


if __name__ == "__main__":
    main()
