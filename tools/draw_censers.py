"""Draws the Father's censers, tier 0 to 8, a 16 by 16 cell each.

    python tools/draw_censers.py [--preview <png>]

Writes `FatherCensers16x16.png` (client/src/kabam/rotmg/assets/custom/images,
`fatherCensers16x16`, which tools/gen_rs_manifest.py registers for the Rust client), cell N being
the tier N censer. A censer swung raises a grey ward over the Father and his allies (the server's
Player.Ward.cs); its smoke takes the colour of the glints its ward throws off (Ward.ACCENTS):

  0 Tin Censer                    dull, dented tin on a thin chain, a single wisp of smoke
  1 Iron Censer                   black iron, a row of rivets round its rim
  2 Pewter Censer                 soft blue-grey pewter, a band of beading round its bowl
  3 Silver Censer                 polished silver, engraved, a glint off its bowl
  4 Cathedral Censer              steel trimmed in gold, a cross on its lid
  5 Censer of the Bulwark         steel plates, a shield on its bowl set with a sapphire
  6 Censer of the Unbroken Vigil  white steel and gold, a halo over it, its smoke glowing
  7 Censer of the Iron Choir      gunmetal laced with gold, its smoke spread like wings, embers
  8 Censer of the Last Rite       obsidian and silver, a white-violet fire inside, shards round it

Each hangs from its chain, light from the upper left. The art is drawn here rather than in an
editor so it can be changed and redrawn; it needs Pillow. `--preview` also writes the nine at 8
times their size, on dark and light ground.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "FatherCensers16x16.png")
CELL = 16
TIERS = 9

# the censer's body, row by row: (first column, last column, part)
BODY = [
    (4, 6, 9, "lid"),
    (5, 5, 10, "lid"),
    (6, 4, 11, "lid"),
    (7, 4, 11, "lid"),
    (8, 3, 12, "rim"),
    (9, 3, 12, "bowl"),
    (10, 4, 11, "bowl"),
    (11, 4, 11, "bowl"),
    (12, 5, 10, "bowl"),
    (13, 6, 9, "stem"),
    (14, 5, 10, "foot"),
]
HOLES = [(5, 6), (7, 6), (9, 6), (6, 5), (8, 5)]   # the lid's perforations, where the smoke comes out


class Canvas:
    def __init__(self):
        self.px = {}

    def set(self, x, y, c):
        x, y = int(x), int(y)
        if 0 <= x < CELL and 0 <= y < CELL:
            self.px[(x, y)] = c if len(c) == 4 else c + (255,)

    def under(self, x, y, c):
        """Set only where nothing is drawn yet (behind what's there)."""
        if (int(x), int(y)) not in self.px:
            self.set(x, y, c)

    def image(self):
        img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        for (x, y), c in self.px.items():
            img.putpixel((x, y), c)
        return img


def ramp(colors, t):
    """A colour along the ramp `colors` (dark to light) at t in 0..1, stepped as pixel art is."""
    t = max(0.0, min(0.999, t))
    return colors[int(t * len(colors))]


def hexc(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


def body(cv, metal, trim, outline, lid_metal=None):
    """The censer's lid, rim, bowl and foot, shaded from the upper left; the rim and foot in
    `trim`. Its outline is `outline`."""
    rows = {y: (l, r) for y, l, r, _ in BODY}
    for y, l, r, part in BODY:
        for x in range(l, r + 1):
            rel = (x - l) / max(1, r - l)
            lit = 0.92 - 0.7 * rel - 0.025 * (y - 4)
            if abs(rel - 0.28) < 0.12:
                lit += 0.14
            edge = x == l or x == r or (y == 14) or (part == "bowl" and y == 12 and x in (l, r))
            # where the row above is narrower, its overhang is in shadow
            above = rows.get(y - 1)
            if part == "bowl" and y == 9:
                lit -= 0.25
            colors = trim if part in ("rim", "foot") else (lid_metal or metal) if part == "lid" else metal
            c = outline if edge else ramp(colors, lit)
            if above and part == "lid" and (x < above[0] or x > above[1]) and not edge:
                c = ramp(colors, lit + 0.1)
            cv.set(x, y, c)


def chain(cv, links, top=0, bottom=3):
    """The chain it hangs from, its links alternating light and dark, a loop at its top."""
    for y in range(top, bottom + 1):
        cv.set(7 if y % 2 else 8, y, links[1] if y % 2 else links[0])
        if y == top:
            cv.set(7, y, links[0])
            cv.set(8, y, links[1])


def holes(cv, inside):
    for x, y in HOLES:
        cv.set(x, y, inside)


def smoke(cv, colors, left=True, right=True, tall=False, alpha=170):
    """Wisps curling up off its lid."""
    lw = [(4, 5), (3, 4), (3, 3), (4, 2)] + ([(3, 1), (2, 0)] if tall else [])
    rw = [(11, 5), (12, 4), (12, 3), (11, 2), (12, 1)] + ([(13, 0)] if tall else [])
    for i, (x, y) in enumerate(lw if left else []):
        c = colors[i % len(colors)]
        cv.under(x, y, c + (max(60, alpha - 18 * i),))
    for i, (x, y) in enumerate(rw if right else []):
        c = colors[(i + 1) % len(colors)]
        cv.under(x, y, c + (max(60, alpha - 18 * i),))


def glow(cv, color, alpha, reach=1):
    """A soft edge of `color` round what's drawn."""
    filled = set(cv.px)
    for (x, y) in list(filled):
        if cv.px[(x, y)][3] < 200:
            continue
        for dx in range(-reach, reach + 1):
            for dy in range(-reach, reach + 1):
                if abs(dx) + abs(dy) > reach:
                    continue
                n = (x + dx, y + dy)
                if n not in cv.px and 0 <= n[0] < CELL and 0 <= n[1] < CELL:
                    cv.px[n] = color + (alpha,)


def sparkle(cv, x, y, core, arm):
    cv.set(x, y, core)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.under(x + dx, y + dy, arm + (170,))


OUTLINE = (24, 24, 30)


def tin():
    cv = Canvas()
    metal = [(70, 70, 66), (104, 102, 94), (138, 134, 124), (170, 166, 154), (196, 192, 180)]
    chain(cv, [(84, 84, 84), (140, 140, 136)])
    body(cv, metal, metal, (44, 42, 40))
    holes(cv, (34, 32, 30))
    # a dent in its bowl, and a crack in its lid
    cv.set(8, 10, (92, 90, 84))
    cv.set(9, 11, (80, 78, 72))
    cv.set(9, 10, (118, 114, 106))
    cv.set(10, 7, (60, 58, 54))
    smoke(cv, [(150, 150, 150), (180, 180, 180)], right=False, alpha=130)
    return cv


def iron():
    cv = Canvas()
    metal = [(34, 36, 40), (52, 56, 62), (74, 78, 86), (98, 104, 112), (124, 130, 138)]
    chain(cv, [(50, 52, 58), (110, 114, 122)])
    body(cv, metal, metal, OUTLINE)
    holes(cv, (14, 14, 18))
    for x in (4, 6, 9, 11):   # rivets round its rim
        cv.set(x, 8, (168, 172, 180))
    smoke(cv, [(140, 144, 150), (176, 180, 186)], alpha=150)
    return cv


def pewter():
    cv = Canvas()
    metal = [(70, 78, 92), (96, 106, 122), (124, 136, 152), (152, 164, 180), (182, 194, 208)]
    chain(cv, [(90, 98, 112), (160, 170, 186)])
    body(cv, metal, metal, (34, 38, 48))
    holes(cv, (30, 34, 44))
    for x in range(5, 11):   # beading round its bowl
        cv.set(x, 11, (196, 206, 220) if x % 2 else (110, 120, 136))
    smoke(cv, [(160, 168, 182), (196, 202, 214)], alpha=160)
    return cv


def silver():
    cv = Canvas()
    metal = [(104, 110, 122), (146, 154, 168), (186, 194, 206), (218, 224, 234), (246, 248, 252)]
    chain(cv, [(150, 156, 168), (232, 236, 244)])
    body(cv, metal, metal, (52, 56, 68))
    holes(cv, (40, 44, 56))
    for x in (6, 8, 10):   # engraving
        cv.set(x, 10, (130, 138, 152))
    sparkle(cv, 5, 10, (255, 255, 255), (230, 236, 255))
    smoke(cv, [(196, 204, 216), (230, 234, 242)], alpha=170)
    return cv


def cathedral():
    cv = Canvas()
    metal = [(78, 84, 96), (112, 120, 134), (150, 158, 172), (184, 192, 204), (214, 220, 230)]
    gold = [(120, 78, 20), (176, 124, 34), (222, 172, 58), (246, 210, 104), (255, 236, 160)]
    cv.set(7, 0, gold[2])
    cv.set(8, 0, gold[3])
    # the cross on its lid
    for y in (1, 2, 3):
        cv.set(7, y, gold[3] if y < 3 else gold[2])
    cv.set(6, 2, gold[2])
    cv.set(8, 2, gold[1])
    body(cv, metal, gold, OUTLINE)
    holes(cv, (40, 30, 20))
    cv.set(7, 11, gold[3])
    cv.set(8, 11, gold[2])
    smoke(cv, [(200, 204, 212), (236, 226, 196)], alpha=170)
    return cv


def bulwark():
    cv = Canvas()
    metal = [(60, 68, 82), (92, 102, 118), (128, 138, 154), (164, 174, 190), (200, 208, 222)]
    trim = [(46, 58, 92), (66, 86, 138), (98, 124, 188), (140, 168, 228), (188, 210, 250)]
    chain(cv, [(80, 88, 104), (170, 180, 198)])
    body(cv, metal, trim, OUTLINE)
    holes(cv, (20, 30, 60))
    # plate seams on its lid
    cv.set(7, 5, (70, 78, 92))
    cv.set(7, 6, (70, 78, 92))
    # a heater shield on its bowl, a sapphire at its heart
    for x, y, c in [(6, 9, (220, 226, 236)), (7, 9, (220, 226, 236)), (8, 9, (190, 198, 212)), (9, 9, (170, 178, 192)),
                    (6, 10, (206, 214, 228)), (9, 10, (150, 160, 176)), (6, 11, (190, 198, 212)), (9, 11, (140, 150, 166)),
                    (7, 12, (170, 178, 192)), (8, 12, (140, 150, 166))]:
        cv.set(x, y, c)
    cv.set(7, 10, (110, 170, 255))
    cv.set(8, 10, (60, 110, 220))
    cv.set(7, 11, (40, 80, 190))
    cv.set(8, 11, (30, 60, 150))
    smoke(cv, [(150, 170, 210), (190, 204, 232)], alpha=175)
    return cv


def vigil():
    cv = Canvas()
    metal = [(150, 150, 160), (190, 190, 200), (220, 222, 230), (240, 242, 248), (255, 255, 255)]
    gold = [(150, 104, 30), (204, 156, 52), (240, 200, 90), (255, 228, 140), (255, 246, 200)]
    # a halo over its chain
    for x in (5, 6, 9, 10):
        cv.set(x, 1, gold[3])
    for x in (6, 7, 8, 9):
        cv.set(x, 0, gold[2])
    cv.set(7, 2, gold[2])
    cv.set(8, 3, gold[3])
    cv.set(7, 3, gold[1])
    body(cv, metal, gold, (70, 64, 70))
    holes(cv, (255, 236, 170))
    sparkle(cv, 5, 10, (255, 255, 255), (255, 240, 190))
    smoke(cv, [(255, 246, 214), (255, 255, 240)], tall=True, alpha=190)
    glow(cv, (255, 232, 150), 70)
    return cv


def iron_choir():
    cv = Canvas()
    metal = [(32, 34, 40), (48, 52, 60), (68, 72, 82), (92, 96, 108), (118, 122, 134)]
    gold = [(130, 84, 20), (190, 132, 36), (236, 182, 62), (255, 216, 110), (255, 240, 180)]
    chain(cv, [(70, 74, 84), (236, 182, 62)])
    body(cv, metal, gold, (14, 14, 18))
    holes(cv, (255, 150, 50))
    # gold filigree on its bowl
    for x, y in [(5, 10), (6, 11), (7, 10), (8, 11), (9, 10), (10, 10)]:
        cv.set(x, y, gold[3] if x % 2 else gold[2])
    # its smoke spread like wings to either side
    wing = [(186, 190, 200), (214, 218, 226), (150, 154, 166)]
    for i, (x, y) in enumerate([(3, 6), (2, 5), (1, 4), (2, 4), (1, 3), (0, 2), (2, 3), (3, 4)]):
        cv.under(x, y, wing[i % 3] + (200 - 10 * i,))
    for i, (x, y) in enumerate([(12, 6), (13, 5), (14, 4), (13, 4), (14, 3), (15, 2), (13, 3), (12, 4)]):
        cv.under(x, y, wing[(i + 1) % 3] + (200 - 10 * i,))
    # embers
    cv.under(4, 1, (255, 170, 60, 230))
    cv.under(11, 0, (255, 120, 40, 220))
    cv.under(10, 2, (255, 200, 90, 200))
    return cv


def last_rite():
    cv = Canvas()
    metal = [(16, 12, 24), (30, 24, 44), (48, 40, 68), (72, 62, 98), (98, 88, 128)]
    silver = [(110, 110, 130), (160, 160, 184), (200, 200, 222), (232, 232, 246), (255, 255, 255)]
    chain(cv, [(120, 120, 140), (230, 230, 246)])
    body(cv, metal, silver, (8, 6, 14))
    # the fire inside, white at its heart, through every hole and a cut down its bowl
    for x, y in HOLES:
        cv.set(x, y, (236, 210, 255))
    cv.set(7, 6, (255, 255, 255))
    cv.set(7, 10, (220, 180, 255))
    cv.set(7, 11, (255, 240, 255))
    cv.set(8, 11, (190, 140, 255))
    # shards of ward circling it
    for x, y, c in [(2, 9, (214, 220, 236)), (1, 10, (150, 156, 176)), (13, 11, (214, 220, 236)), (14, 10, (150, 156, 176)),
                    (12, 2, (230, 214, 255)), (3, 13, (180, 160, 230))]:
        cv.set(x, y, c)
    smoke(cv, [(200, 176, 240), (236, 226, 255)], tall=True, alpha=190)
    glow(cv, (170, 120, 255), 64)
    return cv


DRAW = [tin, iron, pewter, silver, cathedral, bulwark, vigil, iron_choir, last_rite]


def main():
    sheet = Image.new("RGBA", (CELL * TIERS, CELL), (0, 0, 0, 0))
    for i, draw in enumerate(DRAW):
        sheet.paste(draw().image(), (i * CELL, 0))
    sheet.save(OUT)
    print("wrote", OUT)
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        big = sheet.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST)
        out = Image.new("RGBA", (big.width, big.height * 2), (0, 0, 0, 255))
        out.paste((40, 40, 46, 255), (0, 0, big.width, big.height))
        out.paste((200, 200, 196, 255), (0, big.height, big.width, big.height * 2))
        out.alpha_composite(big, (0, 0))
        out.alpha_composite(big, (0, big.height))
        out.save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
