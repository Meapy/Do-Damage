"""Draws the Druid's pearls, tier 0 to 8, a 16 by 16 cell each.

    python tools/draw_pearls.py [--preview <png>]

Writes `DruidPearls16x16.png` (client/src/kabam/rotmg/assets/custom/images, `druidPearls16x16`,
which tools/gen_rs_manifest.py registers for the Rust client), cell N being the tier N pearl, each
in the colour its tide pool is drawn in (the server's TidePearl.COLORS):

  0 Magic Pearl                    blue, a wisp of magic curling round inside it
  1 Lightning Pearl                pale yellow, a bolt of lightning through it, sparks off it
  2 Pearl of Nature                green, wrapped in a vine, a leaf on top
  3 Pearl of Alleviation           violet, a soft halo round it, motes of light rising
  4 Pearl of Silent Life           cyan, a ring of water round it, a drop falling
  5 Pearl of the Infinite Shadows  black with a violet sheen, shadow trailing off it
  6 Pearl of Wasted Blood          crimson, blood running down it and dripping
  7 Pearl of the Blessed Heavens   white gold in a golden setting, rays behind it
  8 Pearl of Total Chaos           half white and half black, curling into each other, crackling

Each is a shaded sphere lit from the upper left, with a shine and its own detail. The art is drawn
here rather than in an editor so it can be changed and redrawn; it needs Pillow. `--preview` also
writes the nine at 8 times their size, on dark and light ground.
"""
import math
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "DruidPearls16x16.png")
CELL = 16
TIERS = 9


class Canvas:
    def __init__(self):
        self.px = {}

    def set(self, x, y, c):
        x, y = int(x), int(y)
        if 0 <= x < CELL and 0 <= y < CELL:
            self.px[(x, y)] = c if len(c) == 4 else c + (255,)

    def get(self, x, y):
        return self.px.get((int(x), int(y)))

    def under(self, x, y, c):
        """Set only where nothing is drawn yet (behind what's there)."""
        if (int(x), int(y)) not in self.px:
            self.set(x, y, c)

    def image(self):
        img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        for (x, y), c in self.px.items():
            img.putpixel((x, y), c)
        return img


def lerp(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(colors, t):
    """A colour along a ramp of colours, t from 0 (the first) to 1 (the last)."""
    t = max(0.0, min(1.0, t)) * (len(colors) - 1)
    i = min(int(t), len(colors) - 2)
    return lerp(colors[i], colors[i + 1], t - i)


# the sphere: where it sits and how big
CX, CY, R = 7.5, 8.0, 5.3


def sphere(cv, shades, cx=CX, cy=CY, r=R, shade=None):
    """A sphere lit from the upper left: `shades` from its shadow side to its lit side, an outline
    a step darker than the darkest. `shade(x, y, t)` may change a pixel's colour."""
    light = (-0.55, -0.65, 0.52)
    norm = math.sqrt(sum(c * c for c in light))
    light = tuple(c / norm for c in light)
    edge = lerp(shades[0], (0, 0, 0), 0.45)
    for y in range(CELL):
        for x in range(CELL):
            dx, dy = (x + 0.5 - cx) / r, (y + 0.5 - cy) / r
            d2 = dx * dx + dy * dy
            if d2 > 1.0:
                continue
            dz = math.sqrt(1.0 - d2)
            lit = max(0.0, dx * light[0] + dy * light[1] + dz * light[2])
            # a little light bounced back up onto its lower right
            rim = max(0.0, dx * 0.5 + dy * 0.6) * (1 - dz) * 0.35
            t = min(1.0, lit * 0.95 + rim)
            c = ramp(shades, t)
            if d2 > 0.78:
                c = lerp(c, edge, 0.55 if d2 > 0.9 else 0.25)
            if shade:
                c = shade(x, y, t) or c
            cv.set(x, y, c)


def shine(cv, color=(255, 255, 255), cx=CX, cy=CY):
    """The shine on its upper left, and a softer one below it."""
    x, y = int(cx - 2.2), int(cy - 2.6)
    cv.set(x, y, color)
    cv.set(x + 1, y, lerp(color, cv.get(x + 1, y) or color, 0.35))
    cv.set(x, y + 1, lerp(color, cv.get(x, y + 1) or color, 0.45))
    cv.set(int(cx + 2), int(cy + 2), lerp(cv.get(int(cx + 2), int(cy + 2)) or color, (255, 255, 255), 0.35))


def glow(cv, color, alpha, reach=1):
    """A faint glow round what's drawn."""
    drawn = set(cv.px)
    for (x, y) in list(drawn):
        for oy in range(-reach, reach + 1):
            for ox in range(-reach, reach + 1):
                p = (x + ox, y + oy)
                if p not in drawn and 0 <= p[0] < CELL and 0 <= p[1] < CELL and p not in cv.px:
                    fall = alpha // (abs(ox) + abs(oy))
                    cv.px[p] = color + (fall,)


def sparkle(cv, x, y, core, arm):
    cv.set(x, y, core)
    for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.under(x + ox, y + oy, arm)


def inside(x, y, cx=CX, cy=CY, r=R):
    return (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r


BLUE = [(18, 30, 110), (52, 78, 210), (112, 135, 255), (180, 194, 255)]
YELLOW = [(120, 112, 30), (196, 200, 70), (228, 246, 117), (250, 255, 210)]
GREEN = [(24, 84, 30), (70, 170, 60), (141, 245, 116), (210, 255, 196)]
VIOLET = [(70, 16, 100), (150, 50, 200), (221, 68, 255), (244, 190, 255)]
CYAN = [(10, 80, 100), (30, 170, 200), (55, 239, 255), (196, 252, 255)]
SHADOW = [(6, 4, 12), (22, 16, 36), (52, 40, 82), (120, 100, 170)]
BLOOD = [(70, 6, 10), (160, 20, 24), (255, 55, 55), (255, 170, 160)]
HEAVEN = [(150, 120, 70), (220, 200, 150), (255, 240, 200), (255, 255, 246)]
WHITE = [(130, 130, 150), (200, 200, 214), (240, 240, 248), (255, 255, 255)]
DARK = [(4, 4, 8), (20, 20, 28), (48, 46, 60), (90, 88, 110)]
GOLD = [(120, 76, 10), (200, 150, 40), (255, 214, 90)]


def magic():
    cv = Canvas()

    # a wisp of magic curling round inside it
    def wisp(x, y, t):
        a = math.atan2(y + 0.5 - CY, x + 0.5 - CX)
        d = math.hypot(x + 0.5 - CX, y + 0.5 - CY)
        if abs(d - (1.6 + (a + math.pi) * 0.55)) < 0.55 and d < R - 1:
            return lerp(ramp(BLUE, t), (220, 230, 255), 0.6)
        return None
    sphere(cv, BLUE, shade=wisp)
    shine(cv)
    glow(cv, (112, 135, 255), 70)
    return cv


def lightning():
    cv = Canvas()
    bolt = {(9, 3), (8, 4), (8, 5), (7, 6), (6, 7), (7, 7), (8, 7), (9, 7), (8, 8), (7, 9), (7, 10), (6, 11), (6, 12)}

    def strike(x, y, t):
        if (x, y) in bolt:
            return (255, 255, 236)
        if any((x + ox, y + oy) in bolt for ox, oy in ((1, 0), (-1, 0))):
            return lerp(ramp(YELLOW, t), (255, 255, 255), 0.25)
        return None
    sphere(cv, YELLOW, shade=strike)
    shine(cv)
    sparkle(cv, 13, 3, (255, 255, 255), (238, 255, 147))
    sparkle(cv, 2, 13, (255, 255, 220), (228, 246, 117))
    cv.set(13, 12, (238, 255, 147))
    glow(cv, (238, 255, 147), 80)
    return cv


def nature():
    cv = Canvas()

    # a vine wound round it: two bands curving across, dark with a lit edge
    def vine(x, y, t):
        px, py = x + 0.5 - CX, y + 0.5 - CY
        for k in (-2.6, 1.6):
            v = py - 0.55 * px + 0.06 * px * px - k
            if -0.6 < v <= 0.4:
                return (30, 76, 20) if v > -0.2 else (40, 100, 26)
            if -1.4 < v <= -0.6:
                return lerp(ramp(GREEN, t), (220, 255, 200), 0.35)
        return None
    sphere(cv, GREEN, shade=vine)
    # a leaf on top, and a smaller one at its side
    for x, y, c in ((8, 1, (120, 210, 80)), (9, 1, (90, 180, 60)), (7, 2, (100, 196, 66)), (8, 2, (150, 230, 100)),
                    (9, 2, (70, 150, 48)), (8, 3, (40, 100, 26)), (13, 9, (110, 200, 70)), (14, 8, (80, 170, 56))):
        cv.set(x, y, c)
    shine(cv)
    glow(cv, (141, 245, 116), 60)
    return cv


def alleviation():
    cv = Canvas()
    # a soft halo round it, behind
    for y in range(CELL):
        for x in range(CELL):
            d = math.hypot(x + 0.5 - CX, (y + 0.5 - CY) * 1.0)
            if R + 0.4 < d < R + 1.5:
                cv.set(x, y, (244, 190, 255, 90))
    sphere(cv, VIOLET)
    shine(cv)
    # motes of light rising off it
    for x, y in ((3, 2), (12, 1), (13, 5)):
        sparkle(cv, x, y, (255, 255, 255), (238, 160, 255))
    return cv


def silent_life():
    cv = Canvas()
    sphere(cv, CYAN, r=4.9, cy=7.6)
    shine(cv, cy=7.6)
    # a ring of water round its middle, seen from a little above: behind it, then in front
    for i in range(48):
        a = i / 48 * 2 * math.pi
        x, y = CX + 7.0 * math.cos(a), 9.0 + 2.0 * math.sin(a)
        front = math.sin(a) > 0
        c = (196, 252, 255) if front else (40, 150, 180)
        if front:
            cv.set(x, y, c)
        else:
            cv.under(x, y, c)
    # a drop falling from it
    cv.set(7, 14, (120, 240, 255))
    cv.set(7, 15, (55, 200, 230))
    glow(cv, (55, 239, 255), 60)
    return cv


def shadows():
    cv = Canvas()

    def sheen(x, y, t):
        # a violet sheen along its lit edge
        d = math.hypot(x + 0.5 - CX, y + 0.5 - CY)
        if R - 1.4 < d < R - 0.4 and x < CX and y < CY + 1:
            return (130, 100, 200)
        return None
    sphere(cv, SHADOW, shade=sheen)
    cv.set(5, 5, (200, 180, 255))
    cv.set(6, 5, (110, 90, 170))
    # shadow trailing off it, down and to the right, in wisps
    for x, y, a in ((13, 9, 200), (14, 10, 150), (13, 12, 180), (14, 13, 120), (15, 12, 80), (11, 14, 160),
                    (12, 15, 100), (2, 12, 120), (1, 13, 70), (12, 3, 110)):
        cv.under(x, y, (40, 26, 70, a))
    glow(cv, (90, 60, 150), 80)
    return cv


def blood():
    cv = Canvas()
    run = {(6, 5): 0, (6, 6): 0, (6, 7): 0, (7, 7): 1, (6, 8): 0, (6, 9): 0, (10, 7): 0, (10, 8): 0, (10, 9): 0,
           (10, 10): 0, (9, 10): 1}

    def runs(x, y, t):
        if (x, y) in run:
            return (110, 6, 14) if run[(x, y)] == 0 else (150, 10, 20)
        return None
    sphere(cv, BLOOD, shade=runs)
    shine(cv, color=(255, 220, 220))
    # dripping off it
    cv.set(6, 14, (170, 12, 20))
    cv.set(10, 14, (150, 10, 18))
    cv.set(10, 15, (110, 6, 14))
    glow(cv, (255, 55, 55), 70)
    return cv


def heavens():
    cv = Canvas()
    # rays behind it
    for k in range(8):
        a = k / 8 * 2 * math.pi + math.pi / 8
        for d in (6.3, 7.2):
            cv.under(CX + d * math.cos(a), CY + d * math.sin(a), (255, 236, 170, 200 if d < 7 else 120))
    sphere(cv, HEAVEN, r=4.9)
    shine(cv)
    # a golden setting cradling it from below: a band, three prongs
    for x in range(4, 12):
        cv.set(x, 12, ramp(GOLD, 1 - abs(x - 7.5) / 4))
    cv.set(5, 13, GOLD[0])
    cv.set(10, 13, GOLD[0])
    for x, y in ((3, 10), (3, 11), (12, 10), (12, 11), (7, 13), (8, 13)):
        cv.set(x, y, GOLD[1])
    cv.set(7, 14, GOLD[2])
    cv.set(8, 14, GOLD[1])
    glow(cv, (255, 240, 200), 70)
    return cv


def chaos():
    cv = Canvas()

    # half white and half black, curling into each other, each with a seed of the other
    def split(x, y, t):
        px, py = x + 0.5 - CX, y + 0.5 - CY
        r = R
        top = (px * px + (py + r / 2) ** 2) <= (r / 2) ** 2
        bottom = (px * px + (py - r / 2) ** 2) <= (r / 2) ** 2
        white = px < 0
        if top:
            white = True
        if bottom:
            white = False
        if (px * px + (py + r / 2) ** 2) <= 0.9:
            white = False
        if (px * px + (py - r / 2) ** 2) <= 0.9:
            white = True
        return ramp(WHITE if white else DARK, t)
    sphere(cv, WHITE, shade=split)
    cv.set(5, 4, (255, 255, 255))
    # violet crackle off it
    for x, y in ((13, 4), (14, 3), (14, 5), (1, 11), (2, 12), (12, 13), (13, 14)):
        cv.set(x, y, (200, 120, 255))
    cv.set(13, 4, (255, 255, 255))
    glow(cv, (190, 110, 255), 80)
    return cv


DRAW = [magic, lightning, nature, alleviation, silent_life, shadows, blood, heavens, chaos]


def main():
    sheet = Image.new("RGBA", (CELL * TIERS, CELL), (0, 0, 0, 0))
    for i, draw in enumerate(DRAW):
        sheet.paste(draw().image(), (i * CELL, 0))
    sheet.save(OUT)
    print("wrote", os.path.relpath(OUT, ROOT))
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        big = sheet.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST)
        out = Image.new("RGBA", (big.width, big.height * 2), (0, 0, 0, 255))
        dark = Image.new("RGBA", big.size, (38, 32, 48, 255))
        light = Image.new("RGBA", big.size, (200, 196, 190, 255))
        out.paste(Image.alpha_composite(dark, big), (0, 0))
        out.paste(Image.alpha_composite(light, big), (0, big.height))
        out.save(path)
        print("preview", path)


if __name__ == "__main__":
    main()
