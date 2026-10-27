"""Draws the forge yard's sprites: the Divine Forge, its flash, the Enchanter, the Enchanter's wisps
and bolt.

    python tools/draw_forge_sprite.py [--forge <png>] [--preview <png>]

Writes `CustomAssets32x32.png` (client/src/kabam/rotmg/assets/custom/images), 32 by 32 pixels a
cell: cells 0-5 are the Divine Forge, its fire flickering and smoke rising (drawn by the owner as a
strip of six frames: `--forge <png>` takes them from one, otherwise they're kept from the sheet as
it is); cell 6 the flash of a finished forging; cells 7-12 the Enchanter, an altar in the forge's
stone and gold with a crystal over it and a red, a green and a purple rune circling it, the
imprints' three colours. The objects (`Divine Forge`, `Enchanter`, CustomObjects.xml) animate
through their six frames.

Also writes `ImprintFx8x8.png`, 8 by 8 a cell: the Wisp Caller imprint's three wisps (green,
blue, red; cells 0-2) and the Battle Mage imprint's bolt (cell 3).

The art is drawn here rather than in an editor so it can be changed and redrawn; it needs Pillow.
"""
import math
import os
import random
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMAGES = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images")
OUT = os.path.join(IMAGES, "CustomAssets32x32.png")
OUT_FX = os.path.join(IMAGES, "ImprintFx8x8.png")
CELL = 32
FORGE_FRAMES = 6
FLASH = 6
ENCHANTER = 7
CELLS = ENCHANTER + 6

OUTLINE = (32, 21, 16)
GOLD_D = (148, 96, 22)
GOLD = (222, 168, 52)
GOLD_L = (255, 222, 120)
GOLD_W = (255, 248, 210)
# the Enchanter's: the forge's own stone and gold (its six frames), the crystal's violet light
E_DARK = (63, 53, 48)
E_MID = (108, 93, 80)
E_LIGHT = (143, 121, 111)
E_MORTAR = (46, 25, 44)
E_GOLD_D = (160, 113, 56)
E_GOLD = (182, 131, 76)
E_GOLD_L = (240, 178, 47)
E_CRYSTAL = [(120, 84, 196), (176, 140, 240), (226, 210, 255)]
E_GLOW = [(150, 110, 230), (230, 214, 255)]
# the imprints' colours (Offensive, Defensive, Misc), bright and dark
RUNES = [((255, 140, 130), (214, 40, 52)), ((160, 255, 170), (40, 180, 80)), ((226, 180, 255), (150, 70, 220))]


class Canvas:
    def __init__(self):
        self.px = {}

    def set(self, x, y, c):
        if 0 <= x < CELL and 0 <= y < CELL:
            self.px[(x, y)] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def outline(self):
        """A dark edge round everything drawn, as the game's sprites have."""
        filled = set(self.px)
        for (x, y) in list(filled):
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (x + dx, y + dy)
                if n not in filled and 0 <= n[0] < CELL and 0 <= n[1] < CELL:
                    self.px[n] = OUTLINE

    def image(self):
        img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        for (x, y), c in self.px.items():
            img.putpixel((x, y), c + (255,) if len(c) == 3 else c)
        return img


def in_crystal(x, y, top):
    """The Enchanter's crystal: a long diamond, its point at `top`."""
    cx, mid = 15.5, top + 3.5
    half = 3.6 * (1 - abs(y - mid) / 4.6)
    return top <= y <= top + 8 and abs(x - cx) <= half


def enchanter(frame):
    """The Enchanter, frame 0-5: the forge's stone and gold, an altar with a basin, a crystal
    floating over it, and the three imprint colours' runes circling the crystal."""
    c = Canvas()
    # the plinth: stone blocks in two courses, gold along its top
    for y in range(26, 31):
        for x in range(7, 25):
            joint = (y == 28) or (x + (3 if y > 28 else 0)) % 6 == 0
            col = E_MORTAR if joint else (E_LIGHT if y in (26, 29) else E_MID)
            if x <= 8:
                col = E_DARK
            c.set(x, y, col)
    c.rect(6, 25, 25, 25, E_GOLD)
    c.set(6, 25, E_GOLD_D)
    c.set(25, 25, E_GOLD_D)
    # the column, a rune carved in it that glows the colour of the rune at the front
    for y in range(17, 25):
        for x in range(11, 21):
            col = E_LIGHT if x in (12, 13) else E_MID
            if x == 11 or y in (17, 24):
                col = E_DARK
            c.set(x, y, col)
    front = RUNES[(frame // 2) % 3]
    for x, y in ((15, 19), (16, 19), (15, 20), (16, 20), (14, 21), (17, 21), (15, 22), (16, 22)):
        c.set(x, y, front[0] if (x, y) in ((15, 20), (16, 20)) else front[1])
    # the basin: a stone bowl with a gold lip, the crystal's light pooled in it
    for y in range(13, 17):
        inset = y - 13
        for x in range(8 + inset, 24 - inset):
            c.set(x, y, E_DARK if x <= 9 + inset else (E_LIGHT if y == 14 else E_MID))
    c.rect(8, 13, 23, 13, E_GOLD)
    c.rect(10, 13, 21, 13, E_GOLD_L)
    for x in range(11, 21):
        c.set(x, 12, E_GLOW[0] if (x + frame) % 3 else E_GLOW[1])
    # the crystal, rising and falling a pixel
    top = 2 + (0, 0, 1, 1, 1, 0)[frame]
    for y in range(top, top + 9):
        for x in range(10, 22):
            if in_crystal(x, y, top):
                left = x < 15.5
                c.set(x, y, E_CRYSTAL[2] if left and y < top + 4 else E_CRYSTAL[1] if left else E_CRYSTAL[0])
    c.set(14, top + 2, (255, 255, 255))
    c.set(14, top + 3, (255, 255, 255))
    c.outline()
    # the runes: red, green and purple, a third of the way round from each other, behind the
    # crystal at the back of their circle and before it at the front
    cy = top + 4
    for k, (hi, lo) in enumerate(RUNES):
        a = 2 * math.pi * (frame / 6 + k / 3)
        x, y = int(round(15.5 + 10 * math.cos(a))), int(round(cy + 3 * math.sin(a)))
        behind = math.sin(a) < 0 and abs(x - 15.5) < 4
        for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
            if behind and in_crystal(x + dx, y + dy, top):
                continue
            c.set(x + dx, y + dy, hi if (dx, dy) == (0, 0) else lo)
    # a few motes rising from the basin
    rng = random.Random(frame)
    for _ in range(2):
        mx, my = rng.randint(10, 21), rng.randint(0, 11)
        if (mx, my) not in c.px:
            c.set(mx, my, E_GLOW[1])
    return c.image()


def forge_frames(path):
    """The Divine Forge's six frames: from a strip of them (--forge), else the sheet's own."""
    src = Image.open(path).convert("RGBA")
    if src.size not in ((CELL * FORGE_FRAMES, CELL), (CELL * CELLS, CELL)):
        sys.exit(f"{path}: expected {FORGE_FRAMES} frames of {CELL} by {CELL} in a row")
    return [src.crop((f * CELL, 0, (f + 1) * CELL, CELL)) for f in range(FORGE_FRAMES)]


# --- the 8 by 8 pieces: the Wisp Caller's wisps and the Battle Mage's bolt -----------------------

WISPS = [
    [(20, 70, 30), (60, 190, 80), (170, 255, 170)],    # Wisp of Life: green
    [(20, 40, 90), (70, 130, 230), (180, 220, 255)],   # Wisp of Warding: blue
    [(80, 14, 20), (210, 50, 60), (255, 170, 160)],    # Wisp of Ruin: red
]
BOLT = [
    "....11..",
    "...1341.",
    "..134431",
    "..13321.",
    ".1221...",
    "121.....",
    "21......",
    "........",
]
BOLT_RAMP = [(46, 20, 70), (120, 60, 200), (200, 150, 255), (255, 240, 255)]


def wisp(ramp, frame=0):
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    for y in range(8):
        for x in range(8):
            d = math.hypot(x - 3.5, y - 3.5)
            if d < 1.3:
                img.putpixel((x, y), ramp[2] + (255,))
            elif d < 2.4:
                img.putpixel((x, y), ramp[1] + (255,))
            elif d < 3.4:
                img.putpixel((x, y), ramp[0] + (190,))
    img.putpixel((2, 2), (255, 255, 255, 255))
    return img


def fx_sheet():
    sheet = Image.new("RGBA", (8 * 4, 8), (0, 0, 0, 0))
    for i, ramp in enumerate(WISPS):
        sheet.paste(wisp(ramp), (8 * i, 0))
    bolt = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    for y, row in enumerate(BOLT):
        for x, ch in enumerate(row):
            if ch != ".":
                bolt.putpixel((x, y), BOLT_RAMP[int(ch) - 1] + (255,))
    sheet.paste(bolt, (24, 0))
    return sheet

def flash_image():
    """A burst of golden light, for the moment an item is forged."""
    c = Canvas()
    for y in range(CELL):
        for x in range(CELL):
            d = math.hypot(x - 15.5, y - 15.5)
            ray = abs(math.sin(math.atan2(y - 15.5, x - 15.5) * 4))
            if d < 4:
                c.set(x, y, GOLD_W)
            elif d < 7:
                c.set(x, y, GOLD_L)
            elif d < 15 and ray > 0.92 - d * 0.01:
                c.set(x, y, GOLD if d < 11 else GOLD_D)
    return c.image()


def main():
    forge = sys.argv[sys.argv.index("--forge") + 1] if "--forge" in sys.argv else OUT
    frames = forge_frames(forge)
    sheet = Image.new("RGBA", (CELL * CELLS, CELL), (0, 0, 0, 0))
    for f, img in enumerate(frames):
        sheet.paste(img, (f * CELL, 0))
    sheet.paste(flash_image(), (FLASH * CELL, 0))
    for f in range(6):
        sheet.paste(enchanter(f), ((ENCHANTER + f) * CELL, 0))
    sheet.save(OUT)
    fx = fx_sheet()
    fx.save(OUT_FX)
    print("Wrote", os.path.relpath(OUT, ROOT), "and", os.path.relpath(OUT_FX, ROOT))
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        bg = Image.new("RGBA", (sheet.width, sheet.height + 8), (24, 18, 36, 255))
        bg.alpha_composite(sheet)
        bg.alpha_composite(fx, (0, CELL))
        bg.resize((bg.width * 6, bg.height * 6), Image.NEAREST).save(path)


if __name__ == "__main__":
    main()
