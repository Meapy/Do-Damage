"""Draws the Divine Forge's sprite: a god's anvil before a fire-filled hearth, in three frames.

    python tools/draw_forge_sprite.py [--preview <png>]

Writes `CustomAssets32x32.png` (client/src/kabam/rotmg/assets/custom/images), 32 by 32 pixels a
cell: cells 0-2 are the forge with its fire flickering, cell 3 the flash of a finished forging.
The object (`Divine Forge`, CustomObjects.xml) animates through the first three. The art is drawn
here rather than in an editor so it can be changed and redrawn; it needs Pillow.
"""
import math
import os
import random
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "CustomAssets32x32.png")
CELL = 32

# the palette: the castle's violet stone, a god's gold, the fire
OUTLINE = (20, 14, 30)
STONE_D = (46, 38, 66)
STONE = (68, 58, 94)
STONE_L = (96, 86, 126)
MORTAR = (34, 27, 50)
GOLD_D = (148, 96, 22)
GOLD = (222, 168, 52)
GOLD_L = (255, 222, 120)
GOLD_W = (255, 248, 210)
FIRE = [(120, 24, 18), (196, 52, 22), (240, 104, 30), (255, 162, 48), (255, 214, 90), (255, 246, 196)]
EMBER = (255, 120, 40)
STEEL_D = (60, 58, 72)
STEEL = (104, 102, 122)
STEEL_L = (156, 154, 176)
HOT = [(200, 60, 20), (255, 140, 40), (255, 220, 120)]


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


def in_arch(x, y):
    """The hearth's mouth: a round-topped opening."""
    cx, top, r = 15.5, 10, 6.5
    if y < top:
        return math.hypot(x - cx, y - top) <= r
    return abs(x - cx) <= r and y <= 19


def hearth(c):
    # the chimney stack and its gold crown
    c.rect(11, 1, 20, 4, STONE)
    for x in range(11, 21):
        c.set(x, 1, STONE_L)
    c.rect(10, 4, 21, 4, GOLD)
    c.set(10, 4, GOLD_D)
    c.set(21, 4, GOLD_D)
    for x in (12, 15, 16, 19):
        c.set(x, 0, GOLD_L)
    # the body: stone blocks in courses
    for y in range(5, 22):
        half = 11 if y >= 8 else 9 + (y - 5)
        for x in range(16 - half, 16 + half):
            if in_arch(x, y):
                continue
            course = (y - 5) // 3
            joint = (x + (course % 2) * 3) % 6 == 0 or (y - 5) % 3 == 2
            col = MORTAR if joint else (STONE_L if (y - 5) % 3 == 0 else STONE)
            if x <= 16 - half + 1:
                col = STONE_D if not joint else MORTAR
            c.set(x, y, col)
    # the arch's gold frame
    for y in range(2, 21):
        for x in range(6, 26):
            if not in_arch(x, y):
                near = any(in_arch(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                if near and y <= 19:
                    c.set(x, y, GOLD_L if y < 8 else GOLD if x < 16 else GOLD_D)
    # a keystone, and gold bands where the body steps out
    c.rect(15, 2, 16, 3, GOLD_L)
    c.set(15, 2, GOLD_W)
    for x in range(5, 27):
        c.set(x, 8, GOLD if 6 < x < 25 else GOLD_D)


def fire(c, frame):
    rng = random.Random(7 + frame * 31)
    phase = frame * 2 * math.pi / 3
    for y in range(3, 20):
        for x in range(9, 23):
            if not in_arch(x, y):
                continue
            # hotter low and in the middle, tongues that move with the frame
            dx = abs(x - 15.5) / 6.5
            tongue = 0.5 + 0.5 * math.sin(x * 1.3 + phase) * math.cos(y * 0.7 - phase)
            heat = (y - 3) / 16 * 1.25 - dx * 0.75 + tongue * 0.35 + rng.uniform(-0.08, 0.08)
            i = max(0, min(len(FIRE) - 1, int(heat * len(FIRE))))
            c.set(x, y, FIRE[i])
    # embers leaping out of the mouth
    for _ in range(3):
        x, y = rng.randint(9, 22), rng.randint(3, 8)
        if in_arch(x, y):
            c.set(x, y, FIRE[5] if rng.random() < 0.4 else EMBER)


def anvil(c, frame):
    # the base and the plinth's gold trim
    c.rect(4, 29, 27, 30, STONE_D)
    c.rect(4, 28, 27, 28, GOLD)
    for x in range(4, 28, 3):
        c.set(x, 30, MORTAR)
    c.set(4, 28, GOLD_D)
    c.set(27, 28, GOLD_L)
    # the anvil: a gold god's anvil, horn to the left, a heel to the right
    c.rect(11, 25, 20, 27, GOLD_D)      # feet
    c.rect(10, 27, 21, 27, GOLD)
    c.rect(13, 23, 18, 25, GOLD)        # waist
    c.rect(13, 23, 13, 25, GOLD_L)
    c.rect(18, 23, 18, 25, GOLD_D)
    c.rect(7, 20, 23, 22, GOLD)         # body
    c.rect(7, 20, 23, 20, GOLD_L)       # the face, catching the fire's light
    for x in range(9, 22):
        c.set(x, 20, GOLD_W if x % 4 == frame % 4 else GOLD_L)
    c.rect(4, 21, 6, 21, GOLD)          # the horn
    c.set(3, 21, GOLD_L)
    c.set(4, 22, GOLD_D)
    c.set(5, 22, GOLD_D)
    c.rect(24, 20, 25, 21, GOLD)        # the heel
    c.rect(7, 22, 23, 22, GOLD_D)
    # a blade on it, glowing at the tip, a dark edge to part it from the fire behind
    c.rect(8, 17, 20, 19, OUTLINE)
    for x in range(9, 17):
        hot = x < 12
        c.set(x, 18, (HOT[2] if (x + frame) % 2 else GOLD_W) if hot else STEEL_L)
        c.set(x, 19, HOT[1] if hot else STEEL)
    c.rect(17, 18, 19, 18, GOLD)         # the guard and grip
    c.rect(17, 19, 19, 19, GOLD_D)


def wings(c):
    """Gold wings either side of the crown: a god's forge."""
    for i in range(5):
        for j in range(4 - i // 2):
            c.set(9 - i, 2 + i + j, GOLD_L if j == 0 else GOLD if j == 1 else GOLD_D)
            c.set(22 + i, 2 + i + j, GOLD_L if j == 0 else GOLD if j == 1 else GOLD_D)


def frame_image(frame):
    c = Canvas()
    hearth(c)
    wings(c)
    fire(c, frame)
    anvil(c, frame)
    c.outline()
    return c.image()


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
    sheet = Image.new("RGBA", (CELL * 4, CELL), (0, 0, 0, 0))
    for f in range(3):
        sheet.paste(frame_image(f), (f * CELL, 0))
    sheet.paste(flash_image(), (3 * CELL, 0))
    sheet.save(OUT)
    print("Wrote", os.path.relpath(OUT, ROOT))
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        bg = Image.new("RGBA", sheet.size, (24, 18, 36, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST).save(path)


if __name__ == "__main__":
    main()
