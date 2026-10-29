"""Draws the Hall of Champions in the Nexus: the Champions Monument and the champions' plinths.

    python tools/draw_champions.py [--preview <png>]

Writes `Champions32x32.png` (client/src/kabam/rotmg/assets/custom/images), 32 by 32 pixels a cell:
cells 0-1 the monument (a stele of violet stone edged in gold, a crown above its tablet and an
ember gem that glows brighter in the second frame), cell 2 a champion's plinth. The objects
(`Champions Monument`, `Champion Statue`, CustomObjects.xml) draw them at Size 100, where a pixel is
the size of a character's, so the monument stands about three tiles tall. The Rust client draws
each board's champion standing on a plinth (`champions.rs`, `PLINTH_TOP`: the plinth's top is
row 22, ten pixels above the cell's foot). Drawn here rather than in an editor so it can be
changed and redrawn; it needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "Champions32x32.png")
CELL = 32

# the website's palette where it fits: violet stone, gold, ember
O = (20, 14, 30)          # outline
S0 = (52, 42, 74)         # stone in shade
S1 = (78, 64, 108)        # stone
S2 = (110, 94, 148)       # stone, lit
S3 = (150, 134, 190)      # stone edge highlight
T0 = (30, 22, 48)         # the tablet
T1 = (44, 34, 68)
G0 = (148, 96, 22)        # gold in shade
G1 = (222, 168, 52)       # gold
G2 = (255, 222, 120)      # gold, lit
W = (255, 250, 230)       # glint
E0 = (196, 78, 34)        # ember in shade
E1 = (255, 122, 61)       # ember
E2 = (255, 196, 140)      # ember, lit


def rect(img, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            img.putpixel((x, y), c)


def outline(img):
    """A dark outline round everything drawn, as the game's sprites have."""
    px = img.load()
    w, h = img.size
    edge = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0 and any(0 <= x + dx < w and 0 <= y + dy < h and px[x + dx, y + dy][3] and px[x + dx, y + dy][:3] != O
                                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                edge.append((x, y))
    for x, y in edge:
        px[x, y] = O + (255,)


def block(img, x0, y0, x1, y1, lit=S2, mid=S1, shade=S0):
    """Stone lit from the top left: a light top row and left column, a dark right column."""
    rect(img, x0, y0, x1, y1, mid)
    rect(img, x0, y0, x1, y0, lit)
    rect(img, x0, y0, x0, y1, lit)
    rect(img, x1, y0 + 1, x1, y1, shade)


def gold_band(img, x0, x1, y):
    rect(img, x0, y, x1, y, G1)
    for x in range(x0, x1 + 1, 3):
        img.putpixel((x, y), G2)
    img.putpixel((x1, y), G0)


def monument(frame):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    # the dais: two steps
    block(img, 3, 28, 28, 30)
    gold_band(img, 3, 28, 28)
    block(img, 6, 25, 25, 27)
    gold_band(img, 6, 25, 25)
    # the stele
    block(img, 9, 9, 22, 24)
    rect(img, 9, 9, 9, 24, S3)
    # its gold frame and the tablet inside
    rect(img, 11, 12, 20, 22, G1)
    rect(img, 11, 12, 20, 12, G2)
    rect(img, 20, 13, 20, 22, G0)
    rect(img, 12, 13, 19, 21, T0)
    rect(img, 12, 13, 19, 13, T1)
    # the tablet's crown and its lines of names
    for x, y in ((13, 15), (15, 14), (16, 14), (18, 15)):
        img.putpixel((x, y), G2)
    rect(img, 13, 16, 18, 16, G1)
    for y, (a, b) in ((18, (13, 18)), (20, (14, 17))):
        rect(img, a, y, b, y, G0)
    # the capstone, and the gem it holds
    block(img, 8, 6, 23, 8, lit=S3)
    gold_band(img, 8, 23, 8)
    rect(img, 13, 3, 18, 5, G1)
    rect(img, 13, 3, 18, 3, G2)
    rect(img, 12, 4, 12, 5, G1)
    rect(img, 19, 4, 19, 5, G0)
    gem = (E1, E2, W) if frame else (E0, E1, E2)
    rect(img, 14, 1, 17, 4, gem[0])
    rect(img, 15, 1, 16, 3, gem[1])
    img.putpixel((15, 1), gem[2])
    # gold glints on the frame, moving between frames
    for x, y in (((11, 12), (20, 22)) if frame else ((20, 12), (11, 22))):
        img.putpixel((x, y), W)
    outline(img)
    return img


PLINTH_TOP = 22


def plinth():
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    block(img, 9, 28, 22, 30)
    gold_band(img, 9, 22, 28)
    block(img, 11, PLINTH_TOP + 1, 20, 27)
    rect(img, 11, PLINTH_TOP + 1, 11, 27, S3)
    rect(img, 12, 25, 19, 26, T0)
    block(img, 10, PLINTH_TOP, 21, PLINTH_TOP + 1, lit=S3)
    gold_band(img, 10, 21, PLINTH_TOP + 1)
    outline(img)
    return img


def main():
    cells = [monument(0), monument(1), plinth()]
    sheet = Image.new("RGBA", (CELL * 4, CELL), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        sheet.paste(c, (i * CELL, 0))
    sheet.save(OUT)
    print("written", os.path.relpath(OUT, ROOT))
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        sheet.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST).save(path)


if __name__ == "__main__":
    main()
