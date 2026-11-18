"""Draws the Ascendant's halos, tier 0 to 8, a 16 by 16 cell each.

    python tools/draw_halos.py [--preview <png>]

Writes `AscendantHalos16x16.png` (client/src/kabam/rotmg/assets/custom/images,
`ascendantHalos16x16`, which tools/gen_rs_manifest.py registers for the Rust client), cell N being
the tier N halo, each in the colour its forcefield is raised in (Forcefield.COLORS):

  0 Worn Halo               dull iron, dented and cracked through at the back
  1 Rusty Halo              rust, pitted and flaking, a drip of it hanging off
  2 Bronze Halo             bronze patched with verdigris, riveted
  3 Silver Halo             polished silver, engraved, a glint off its front
  4 Golden Halo             gold set with a ruby and two sapphires, glowing
  5 Halo of Eternal Peace   jade and pearl, wreathed in a laurel of leaves
  6 Halo of God's Blessing  white gold with a sunburst of rays behind it
  7 Halo of the Seraphim    a ring of fire between two burning wings
  8 Halo of the Archangel   a double ring of violet and starlight, great white wings, a star at its heart

Each is a ring seen from a little above, its back darker than its front. The art is drawn here
rather than in an editor so it can be changed and redrawn; it needs Pillow. `--preview` also
writes the nine at 8 times their size, on dark and light ground.
"""
import math
import os
import random
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "AscendantHalos16x16.png")
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
        return self.px.get((x, y))

    def clear(self, x, y):
        self.px.pop((int(x), int(y)), None)

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
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def ramp(colors, t):
    """A colour along the ramp `colors` (dark to light) at t in 0..1, stepped as pixel art is."""
    t = max(0.0, min(0.999, t))
    return colors[int(t * len(colors))]


def ring(cv, ramp_colors, cx=7.5, cy=8.5, rx=6.6, ry=3.4, w=1.9, keep=None):
    """A halo seen from a little above: an elliptical band, its back (top) darker than its front,
    lit along its upper edge. Returns the pixels it drew with their angle round the ring."""
    drawn = {}
    for y in range(CELL):
        for x in range(CELL):
            dx, dy = (x + 0.5 - cx), (y + 0.5 - cy)
            outer = (dx / rx) ** 2 + (dy / ry) ** 2
            inner = (dx / (rx - w)) ** 2 + (dy / max(0.6, ry - w * 0.62)) ** 2
            if outer > 1.0 or inner < 1.0:
                continue
            ang = math.atan2(dy / ry, dx / rx)  # -pi/2 at the back (top), +pi/2 at the front
            if keep and not keep(x, y, ang):
                continue
            front = (math.sin(ang) + 1) / 2  # 0 back, 1 front
            # across the band: 0 at its inner edge, 1 at its outer
            across = (1 - inner ** -0.5) / max(0.01, 1 - outer ** 0.5 + (1 - inner ** -0.5))
            lit = 0.18 + 0.55 * front + 0.25 * (1 - abs(across - 0.45) * 2)
            if dy < 0:
                lit -= 0.12
            cv.set(x, y, ramp(ramp_colors, lit))
            drawn[(x, y)] = ang
    return drawn


def glow(cv, color, alpha, reach=1):
    """A soft edge of `color` round what's drawn."""
    filled = set(cv.px)
    for (x, y) in list(filled):
        for dx in range(-reach, reach + 1):
            for dy in range(-reach, reach + 1):
                if abs(dx) + abs(dy) > reach:
                    continue
                n = (x + dx, y + dy)
                if n not in filled and 0 <= n[0] < CELL and 0 <= n[1] < CELL:
                    cv.px[n] = color + (alpha,)


def sparkle(cv, x, y, core, arm, size=1):
    cv.set(x, y, core)
    for k in range(1, size + 1):
        for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)):
            cv.set(x + dx, y + dy, arm)


def fan(cv, side, root_x, root_y, feathers, ramp_colors, behind=True):
    """A wing fanned out from (root_x, root_y) to the `side` (-1 left, 1 right): `feathers` as
    (angle in degrees above level, length), each a line darker at its root, lighter at its tip."""
    put = cv.under if behind else cv.set
    for deg, length in feathers:
        a = math.radians(deg)
        steps = int(length * 2) + 1
        for k in range(steps):
            t = k / max(1, steps - 1)
            x = root_x + side * t * length * math.cos(a)
            y = root_y - t * length * math.sin(a)
            put(round(x), round(y), ramp(ramp_colors, 0.25 + 0.75 * t))


IRON = [(46, 48, 52), (74, 78, 84), (104, 110, 116), (142, 148, 154), (178, 182, 186)]
RUST = [(62, 26, 12), (110, 52, 24), (160, 80, 34), (196, 104, 44), (226, 146, 80)]
BRONZE = [(74, 40, 16), (130, 74, 30), (180, 116, 52), (214, 152, 76), (244, 206, 140)]
VERDIGRIS = [(40, 120, 100), (80, 180, 154), (140, 222, 196)]
SILVER = [(70, 82, 100), (120, 136, 156), (170, 186, 206), (214, 226, 240), (250, 252, 255)]
GOLD = [(120, 72, 10), (188, 124, 18), (236, 176, 40), (255, 214, 90), (255, 244, 186)]
JADE = [(26, 96, 72), (60, 156, 120), (120, 214, 176), (190, 244, 222), (246, 255, 250)]
LEAF = [(30, 90, 40), (58, 140, 60), (110, 196, 90)]
HOLY = [(170, 130, 50), (230, 196, 100), (255, 232, 160), (255, 248, 214), (255, 255, 255)]
FLAME = [(110, 18, 10), (200, 40, 16), (255, 106, 40), (255, 176, 60), (255, 238, 150)]
FEATHER = [(150, 140, 170), (196, 190, 214), (232, 228, 246), (255, 255, 255)]
VIOLET = [(54, 24, 110), (100, 52, 180), (162, 110, 250), (206, 170, 255), (244, 230, 255)]
CYAN = [(30, 90, 150), (70, 160, 220), (130, 220, 255), (220, 248, 255)]


def worn():
    cv = Canvas()
    rnd = random.Random(0)
    # cracked through at the back right, a dent in the front
    drawn = ring(cv, IRON, keep=lambda x, y, a: not (-1.25 < a < -0.95))
    for (x, y), a in drawn.items():
        if rnd.random() < 0.16:
            cv.set(x, y, IRON[0])
    # a dent in its front
    cv.clear(5, 12)
    cv.clear(6, 12)
    cv.set(5, 11, IRON[1])
    cv.set(6, 11, IRON[1])
    # a burr either side of the crack
    cv.set(11, 5, IRON[4])
    cv.set(12, 6, IRON[2])
    return cv


def rusty():
    cv = Canvas()
    rnd = random.Random(1)
    drawn = ring(cv, RUST)
    for (x, y), a in drawn.items():
        r = rnd.random()
        if r < 0.18:
            cv.set(x, y, RUST[0])
        elif r < 0.28:
            cv.set(x, y, (224, 120, 48))
    # flakes standing off its edge, and a drip of rust hanging from the front
    for x, y in ((1, 7), (14, 9), (3, 4), (12, 12)):
        cv.under(x, y, RUST[2])
    cv.set(8, 12, RUST[1])
    cv.set(8, 13, RUST[2])
    cv.set(8, 14, RUST[3])
    cv.set(5, 12, RUST[1])
    cv.set(5, 13, RUST[2])
    return cv


def bronze():
    cv = Canvas()
    rnd = random.Random(2)
    drawn = ring(cv, BRONZE)
    # patches of verdigris, mostly round the back where the weather got at it
    for (x, y), a in drawn.items():
        if rnd.random() < (0.35 if a < 0 else 0.1):
            cv.set(x, y, VERDIGRIS[0] if rnd.random() < 0.5 else VERDIGRIS[1])
    # four rivets round the front
    for x, y in ((2, 9), (5, 11), (10, 11), (13, 9)):
        cv.set(x, y, BRONZE[4])
    cv.set(7, 5, VERDIGRIS[2])
    return cv


def silver():
    cv = Canvas()
    drawn = ring(cv, SILVER)
    # engraved notches along its front
    for (x, y), a in drawn.items():
        if 0.4 < a < 2.7 and (x % 3 == 0) and cv.get(x, y) and cv.get(x, y)[:3] != SILVER[4]:
            cv.set(x, y, SILVER[1])
    # a glint off its front left
    sparkle(cv, 4, 10, (255, 255, 255), (200, 230, 255))
    sparkle(cv, 12, 5, (255, 255, 255), (190, 214, 240))
    return cv


def golden():
    cv = Canvas()
    ring(cv, GOLD)
    glow(cv, (255, 220, 110), 70)
    # a ruby at its front, two sapphires either side
    cv.set(7, 11, (220, 30, 50))
    cv.set(8, 11, (255, 110, 120))
    cv.set(7, 12, (150, 10, 30))
    cv.set(8, 12, (220, 30, 50))
    cv.set(3, 10, (60, 110, 255))
    cv.set(12, 10, (60, 110, 255))
    sparkle(cv, 12, 4, (255, 255, 230), (255, 230, 140))
    return cv


def peace():
    cv = Canvas()
    ring(cv, JADE, ry=3.2)
    glow(cv, (180, 255, 220), 60)
    # a laurel of leaves round its outer edge, in pairs either side of the front
    leaves = [(1, 8), (1, 6), (2, 5), (3, 11), (2, 10), (14, 8), (14, 6), (13, 5), (12, 11), (13, 10)]
    for i, (x, y) in enumerate(leaves):
        cv.set(x, y, LEAF[1 + (i % 2)])
        cv.set(x + (1 if x < 8 else -1) * 0, y - 1 if i % 2 else y + 0, LEAF[0 + (i % 2)])
    # a pearl at the front, a bloom at the back
    cv.set(7, 11, (255, 255, 255))
    cv.set(8, 11, (230, 240, 248))
    cv.set(7, 5, (255, 220, 236))
    cv.set(8, 5, (255, 190, 220))
    return cv


def blessing():
    cv = Canvas()
    ring(cv, HOLY)
    glow(cv, (255, 236, 170), 50)
    # a sunburst of rays behind it, long and short in turn, longest straight up
    for i in range(9):
        ang = math.pi + (i + 0.0) * math.pi / 8
        length = 8.0 if i % 2 == 0 else 6.0
        if i in (0, 8):
            length = 7.6
        for k in range(8, int(length * 3)):
            d = k / 3
            x, y = 7.5 + d * math.cos(ang) * 1.0, 8.5 + d * math.sin(ang) * 0.95
            t = (d - 2.6) / (length - 2.6)
            c = lerp(HOLY[3], HOLY[1], t)
            cv.under(math.floor(x), math.floor(y), c + (round(255 - 70 * t),))
    # a white star set in its front
    cv.set(7, 11, (255, 255, 255))
    cv.set(8, 11, (255, 255, 255))
    cv.set(7, 12, HOLY[2])
    cv.set(8, 10, HOLY[3])
    sparkle(cv, 13, 3, (255, 255, 255), HOLY[3])
    return cv


def seraphim():
    cv = Canvas()
    ring(cv, FLAME, rx=4.6, ry=2.5, w=1.5, cy=9.5)
    # flames licking up off its back edge
    for x, h in ((5, 2), (6, 3), (7, 4), (8, 4), (9, 3), (10, 2)):
        for k in range(h):
            cv.under(x, 7 - k, ramp(FLAME, 0.95 - 0.7 * k / h))
    cv.set(7, 2, FLAME[4])
    # six burning wings: a great pair up and out, a pair level, a pair folded down
    wings = [FLAME[0], FLAME[1], FLAME[2], FLAME[3], FLAME[4]]
    for side, root in ((-1, 3), (1, 12)):
        fan(cv, side, root, 8, [(72, 4.6), (44, 4.4)], wings)
        fan(cv, side, root, 9, [(12, 3.4)], wings)
        fan(cv, side, root, 10, [(-30, 3.2), (-62, 2.8)], wings)
    glow(cv, (255, 110, 40), 45)
    return cv


def archangel():
    cv = Canvas()
    ring(cv, VIOLET, rx=5.0, ry=2.6, w=1.6, cy=9.5)
    # starlight round its inner edge
    for x, y in ((5, 8), (9, 8), (4, 10), (11, 10)):
        cv.set(x, y, CYAN[2])
    cv.set(7, 11, (255, 255, 255))
    cv.set(8, 11, CYAN[3])
    # great white wings behind it, tipped with starlight
    feathers = FEATHER + [CYAN[3]]
    for side, root in ((-1, 4), (1, 11)):
        fan(cv, side, root, 8, [(76, 6.0), (52, 6.4), (28, 5.6), (6, 4.4)], feathers)
        fan(cv, side, root, 10, [(-24, 3.4)], feathers)
    # a star above it
    sparkle(cv, 7, 3, (255, 255, 255), CYAN[2], size=2)
    cv.set(8, 3, (255, 255, 255))
    cv.set(7, 4, (255, 255, 255))
    glow(cv, (180, 140, 255), 45)
    return cv


DRAW = [worn, rusty, bronze, silver, golden, peace, blessing, seraphim, archangel]


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
