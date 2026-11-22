"""Draws the Mechanic's turrets: each tier's blueprint (the item), its turret and its shot.

    python tools/draw_turrets.py [--preview <png>]

Writes two sheets into client/src/kabam/rotmg/assets/custom/images, which tools/gen_rs_manifest.py
registers for the Rust client:

  MechanicTurrets16x16.png (`mechanicTurrets16x16`): row 0 the blueprints, row 1 the turrets'
      bases and row 2 their heads (which the client turns to aim), a column a tier (0 to 8), the
      War Turret's in column 9
  MechanicShots8x8.png (`mechanicShots8x8`): the shots, in the same columns, pointing right

  0 Makeshift Turret  scrap planks and a rusty pipe lashed with rope, on a parchment scrawl
  1 Basic Turret      plain iron, riveted, one barrel
  2 Advanced Turret   blued steel with a scope and a long barrel
  3 Organic Turret    a living stump, leaves on its crown, twin thorn barrels
  4 Aquatic Turret    a coral foot and a spiral shell firing bubbles
  5 Demonic Turret    black iron cracked with hellfire, horns, twin barrels
  6 Light Turret      white gold round a burning prism
  7 Ancient Turret    weathered sandstone, jade runes, a golden eye, twin barrels
  8 Skystorm Turret   storm-dark steel under a crackling tesla orb, twin barrels
  9 War Turret        an olive gatling with a brass ammunition belt

Row 3 is Ferrogrim's Bastion Turret (the Crucible's white bag, tools/build_crucible.py): its base
in column 0 (index 0x1e), a squat keep of black iron with molten seams and a forge burning in its
gate, and its head in column 1 (0x1f), a riveted iron housing round a glowing core, three barrels.

Each turret's base stands on its feet, light from the upper left, and its head, seen from above,
points right; the Rust client turns the head to aim and gives the turret a plate on the ground and
its muzzle flashes (client-rs/app/src/particles/turret.rs). The colours
of each tier's effects are the server's Turret.COLORS. The art is drawn here rather than in an
editor so it can be changed and redrawn; it needs Pillow. `--preview` also writes the sheets at 8
times their size, on dark and light ground.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMAGES = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images")
OUT = os.path.join(IMAGES, "MechanicTurrets16x16.png")
OUT_SHOTS = os.path.join(IMAGES, "MechanicShots8x8.png")
CELL = 16
COLUMNS = 10


def hexc(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


def ramp(*vals):
    """A ramp of colours, dark to light."""
    return [hexc(v) for v in vals]


class Canvas:
    def __init__(self, size=CELL):
        self.size = size
        self.px = {}

    def set(self, x, y, c, a=255):
        x, y = int(x), int(y)
        if 0 <= x < self.size and 0 <= y < self.size:
            self.px[(x, y)] = (c[0], c[1], c[2], a) if len(c) == 3 else c

    def get(self, x, y):
        return self.px.get((int(x), int(y)))

    def under(self, x, y, c, a=255):
        if (int(x), int(y)) not in self.px:
            self.set(x, y, c, a)

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def shade_rect(self, x0, y0, x1, y1, pal):
        """A block lit from the upper left: its top row and left column light, its right and
        bottom dark, the rest the middle tones."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if y == y0 or x == x0:
                    c = pal[-1]
                elif x == x1 or y == y1:
                    c = pal[0]
                elif x == x1 - 1:
                    c = pal[1]
                else:
                    c = pal[2] if len(pal) > 3 else pal[1]
                self.set(x, y, c)

    def image(self):
        img = Image.new("RGBA", (self.size, self.size), (0, 0, 0, 0))
        for (x, y), c in self.px.items():
            img.putpixel((x, y), c)
        return img


def glow(cv, color, alpha):
    """A soft halo round what's drawn, in `color`."""
    drawn = set(cv.px)
    for (x, y) in list(drawn):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, y + dy) not in drawn:
                cv.under(x + dx, y + dy, color, alpha)


# --- the turrets -------------------------------------------------------------------------------
#
# Each turret is two pictures: its base, standing (row 1), with a turntable on top whose middle is
# row 8, the head's pivot; and its head (row 2), seen from above, its barrels pointing right from
# the cell's middle (8, 8) to the muzzle at column 14, which the Rust client turns to aim and draws
# on the base's turntable (client-rs/app/src/particles/turret.rs, `draw_head`). At the turrets'
# size, 80, the muzzle is half a tile from the pivot: where the server starts their shots
# (Turret.MUZZLE).

PIVOT = 8


def disc(cv, cx, cy, r, pal):
    """A round housing seen from above, lit from the upper left."""
    for y in range(CELL):
        for x in range(CELL):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = (dx * dx + dy * dy) ** 0.5
            if d <= r:
                light = (-dx - dy) / (r * 1.6) + 0.5 - d / (r * 3)
                cv.set(x, y, pal[max(0, min(len(pal) - 1, int(light * len(pal) + 0.5)))])


def box(cv, x0, y0, x1, y1, pal):
    cv.shade_rect(x0, y0, x1, y1, pal)
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        cv.px.pop((x, y), None)


def gun(cv, y, x0, x1, pal, tip=None, thick=2):
    """A barrel seen from above along row y (and y+1), pointing right, ending at x1."""
    d, m, l = pal[0], pal[1], pal[-1]
    for x in range(x0, x1 + 1):
        cv.set(x, y, l if thick > 1 else m)
        if thick > 1:
            cv.set(x, y + 1, d)
    cv.set(x1, y, tip or m)
    if thick > 1:
        cv.set(x1, y + 1, tip or d)


def turntable(cv, pal, y=8, x0=4, x1=11):
    """The ring the head turns on, seen a little from above, on rows y and y+1."""
    d, m, l = pal[0], pal[1], pal[-1]
    for x in range(x0 + 1, x1):
        cv.set(x, y - 1, l)
    for x in range(x0, x1 + 1):
        cv.set(x, y, m)
        cv.set(x, y + 1, d)
    cv.set(x0, y, l)


def legs(cv, pal, top=12):
    """A splayed tripod under the body, from row `top` down."""
    d, m, l = pal[0], pal[1], pal[-1]
    cv.rect(6, top, 9, top, m)
    for i, (xl, xr) in enumerate(((5, 10), (4, 11), (3, 12))):
        y = top + 1 + i
        cv.set(xl, y, l)
        cv.set(xl + 1, y, m)
        cv.set(xr, y, d)
        cv.set(xr - 1, y, m)
    cv.set(7, top + 1, d)
    cv.set(8, top + 1, d)


# the tiers' colours, dark to light
WOOD = ramp(0x4A2C14, 0x6E4422, 0x8E5C30, 0xB07A44)
RUST = ramp(0x5A2A10, 0x8A4418, 0xB0602A, 0xD08A4A)
IRON = ramp(0x3A4048, 0x5A626C, 0x7C8692, 0xA8B2BE)
STEEL = ramp(0x1E3C5C, 0x2E5A88, 0x4A82B8, 0x8CC0EE)
GUNMETAL = ramp(0x1A2430, 0x2E3C4C, 0x48586A, 0x6A7C90)
BARK = ramp(0x3A2410, 0x5C3A1A, 0x7E5426, 0x9E7038)
LEAF = ramp(0x1E5A18, 0x2E8424, 0x52B032, 0x8CDC5A)
THORN = ramp(0x2A4A14, 0x4A7A22, 0x6EA830, 0xB0E070)
CORAL = ramp(0x8A2A3A, 0xC84A50, 0xF07A6A, 0xFFB09A)
SHELL = ramp(0x2A6A78, 0x3A9AA8, 0x60C8C8, 0xB8F4E8)
HELLIRON = ramp(0x140A0C, 0x2A1418, 0x3E2026, 0x5A3038)
FIRE = ramp(0x8A1000, 0xD02800, 0xFF5A10, 0xFFB040)
HORN = ramp(0x5A4A3A, 0x8A7860, 0xBCA888, 0xE8DCC0)
GOLD = ramp(0x8A6A20, 0xC0962E, 0xE8C450, 0xFFF0A8)
WHITE = ramp(0xB8B0A0, 0xD8D2C4, 0xF0ECE0, 0xFFFFFF)
STONE = ramp(0x6A5A40, 0x8E7A58, 0xB4A078, 0xD8C8A0)
JADE = ramp(0x1A6A50, 0x24A07A, 0x40D8A0, 0xA8FFE0)
STORM = ramp(0x141428, 0x24244A, 0x383A70, 0x5A5EA0)
VOLT = ramp(0x3A2AB0, 0x6A5AFF, 0x9AD8FF, 0xFFFFFF)
OLIVE = ramp(0x2E3418, 0x4A5226, 0x6A7438, 0x8E9A52)
BRASS = ramp(0x7A5418, 0xB07E28, 0xD8A840, 0xFFDA7A)
GUN = ramp(0x202020, 0x383838, 0x585858, 0x808080)


# the bases ---------------------------------------------------------------------------------------

def base_makeshift():
    cv = Canvas()
    for x, y in ((5, 12), (4, 13), (4, 14), (3, 15)):
        cv.set(x, y, WOOD[2])
    for x, y in ((10, 12), (11, 13), (11, 14), (12, 15)):
        cv.set(x, y, WOOD[0])
    cv.rect(7, 13, 8, 14, WOOD[1])
    # a crate for a body, its boards and nails
    cv.shade_rect(4, 9, 11, 12, WOOD)
    for x in range(5, 11):
        cv.set(x, 11, WOOD[1])
    cv.set(5, 10, hexc(0x303030))
    cv.set(10, 10, hexc(0x303030))
    # a cart wheel laid flat for it to turn on
    turntable(cv, RUST, 8, 5, 10)
    return cv


def base_basic():
    cv = Canvas()
    legs(cv, IRON)
    cv.shade_rect(6, 9, 9, 11, IRON)
    turntable(cv, IRON)
    for x in (5, 10):
        cv.set(x, 8, hexc(0xD0D8E0))
    return cv


def base_advanced():
    cv = Canvas()
    legs(cv, GUNMETAL)
    cv.shade_rect(5, 9, 10, 11, STEEL)
    cv.set(7, 10, hexc(0x58E0FF))
    cv.set(8, 10, hexc(0x1E5A80))
    turntable(cv, GUNMETAL, 8, 3, 12)
    cv.set(12, 8, hexc(0xFF5040))   # a light
    return cv


def base_organic():
    cv = Canvas()
    # roots
    for x, y in ((5, 12), (4, 13), (3, 14), (2, 15), (6, 13), (6, 14)):
        cv.set(x, y, BARK[1] if x > 3 else BARK[2])
    for x, y in ((10, 12), (11, 13), (12, 14), (13, 15), (9, 13), (9, 14)):
        cv.set(x, y, BARK[0])
    # the stump, cut flat, its rings for a turntable
    cv.shade_rect(4, 9, 11, 12, BARK)
    for y in (10, 11):
        cv.set(6, y, BARK[0])
        cv.set(9, y + 1, BARK[0])
    for x in range(5, 11):
        cv.set(x, 8, BARK[3] if x % 3 else hexc(0xC8A070))
    cv.set(4, 8, BARK[2])
    cv.set(11, 8, BARK[1])
    # leaves sprouting off it
    for x, y, c in ((3, 9, 3), (2, 10, 2), (12, 9, 2), (13, 10, 1), (3, 11, 1)):
        cv.set(x, y, LEAF[c])
    cv.set(12, 11, hexc(0xFF80C0))
    return cv


def base_aquatic():
    cv = Canvas()
    # a branching coral foot
    for x, y in ((4, 15), (5, 14), (5, 13), (3, 13), (4, 12), (11, 15), (10, 14), (10, 13), (12, 13), (11, 12),
                 (7, 15), (8, 15), (7, 14), (8, 13)):
        cv.set(x, y, CORAL[2] if x < 7 else CORAL[1])
    cv.set(3, 12, CORAL[3])
    cv.set(12, 12, CORAL[0])
    cv.shade_rect(6, 10, 9, 12, CORAL)
    # a clam shell, open, for it to sit in
    turntable(cv, SHELL, 8, 3, 12)
    cv.set(4, 9, SHELL[0])
    cv.set(11, 9, SHELL[0])
    cv.set(7, 7, hexc(0xF4F0FF))   # a pearl
    return cv


def base_demonic():
    cv = Canvas()
    legs(cv, HELLIRON)
    cv.set(7, 13, FIRE[1])
    cv.shade_rect(5, 9, 10, 11, HELLIRON)
    cv.set(7, 10, FIRE[2])
    cv.set(8, 11, FIRE[1])
    cv.set(6, 11, FIRE[0])
    turntable(cv, HELLIRON, 8, 3, 12)
    # horns off its rim, and spikes
    for x, y, c in ((2, 7, 2), (1, 6, 3), (13, 7, 1), (14, 6, 2)):
        cv.set(x, y, HORN[c])
    glow(cv, FIRE[0], 60)
    return cv


def base_light():
    cv = Canvas()
    legs(cv, GOLD)
    cv.shade_rect(6, 9, 9, 11, WHITE)
    cv.set(7, 10, GOLD[3])
    turntable(cv, GOLD, 8, 4, 11)
    # wings of gold off its sides
    for x, y in ((3, 9), (2, 10), (12, 9), (13, 10)):
        cv.set(x, y, GOLD[2])
    glow(cv, hexc(0xFFE890), 70)
    return cv


def base_ancient():
    cv = Canvas()
    # a stepped plinth
    cv.shade_rect(3, 13, 12, 15, STONE)
    cv.shade_rect(5, 9, 10, 12, STONE)
    for x in (4, 7, 10):
        cv.set(x, 14, JADE[2])
    cv.set(7, 10, JADE[2])
    cv.set(8, 11, JADE[1])
    turntable(cv, STONE, 8, 4, 11)
    cv.set(4, 8, hexc(0xFFD040))
    cv.set(11, 8, hexc(0xFFD040))
    glow(cv, JADE[0], 50)
    return cv


def base_skystorm():
    cv = Canvas()
    legs(cv, STORM)
    cv.set(7, 13, VOLT[2])
    cv.shade_rect(5, 9, 10, 11, STORM)
    # coils round its neck
    for x in range(5, 11):
        cv.set(x, 10, hexc(0xC08A30) if x % 2 == 0 else hexc(0x7A5418))
    turntable(cv, STORM, 8, 3, 12)
    for x, y in ((2, 8), (1, 9), (13, 8), (14, 9), (3, 12)):
        cv.set(x, y, VOLT[2], 210)   # arcs off it
    glow(cv, VOLT[0], 80)
    return cv


def base_war():
    cv = Canvas()
    legs(cv, OLIVE)
    cv.shade_rect(5, 9, 10, 11, OLIVE)
    cv.set(7, 10, hexc(0xE8E0C0))
    # an ammunition box hanging off it, its belt
    cv.shade_rect(10, 10, 13, 12, OLIVE)
    cv.set(11, 11, BRASS[2])
    cv.set(12, 11, BRASS[1])
    turntable(cv, GUN, 8, 4, 11)
    return cv


# the heads, from above, pointing right ----------------------------------------------------------

def head_makeshift():
    cv = Canvas()
    disc(cv, 7.5, 8, 3.6, RUST)
    for y in range(5, 12):
        cv.set(7, y, hexc(0xD8C088))   # rope round the bucket
    cv.set(6, 6, hexc(0xE0A060))
    # a bent pipe
    for x in range(10, 14):
        cv.set(x, 8, RUST[3])
        cv.set(x, 9, RUST[1])
    cv.set(13, 7, RUST[2])
    cv.set(14, 7, RUST[3])
    cv.set(14, 8, hexc(0x281408))
    return cv


def head_basic():
    cv = Canvas()
    box(cv, 4, 5, 10, 11, IRON)
    for x, y in ((5, 6), (9, 6), (5, 10), (9, 10)):
        cv.set(x, y, hexc(0xD0D8E0))
    cv.set(7, 8, hexc(0x88C8FF))
    gun(cv, 7, 11, 14, IRON, tip=hexc(0x2A2E34))
    return cv


def head_advanced():
    cv = Canvas()
    box(cv, 3, 5, 10, 11, STEEL)
    # its scope along the top
    for x in range(5, 10):
        cv.set(x, 4, GUNMETAL[2])
    cv.set(9, 4, hexc(0xFF5040))
    cv.set(6, 8, hexc(0x58E0FF))
    cv.set(6, 9, hexc(0x1E5A80))
    gun(cv, 7, 11, 15, GUNMETAL, tip=hexc(0x58B8FF))
    cv.set(14, 6, GUNMETAL[2])
    cv.set(14, 9, GUNMETAL[1])
    return cv


def head_organic():
    cv = Canvas()
    disc(cv, 7.5, 8, 3.2, BARK)
    # leaves round it
    for x, y, c in ((4, 4, 3), (5, 3, 2), (8, 3, 3), (10, 4, 2), (3, 7, 2), (3, 10, 1), (5, 12, 1), (9, 12, 1),
                    (11, 11, 0), (4, 5, 2), (10, 5, 1), (4, 11, 1)):
        cv.set(x, y, LEAF[c])
    cv.set(7, 8, hexc(0xC8FF60))
    cv.set(6, 4, hexc(0xFF80C0))
    # twin thorns
    gun(cv, 6, 10, 14, THORN, tip=hexc(0xE8FFB0), thick=1)
    gun(cv, 9, 10, 14, THORN, tip=hexc(0xE8FFB0), thick=1)
    cv.set(12, 5, THORN[2])
    cv.set(13, 10, THORN[2])
    return cv


def head_aquatic():
    cv = Canvas()
    disc(cv, 7.5, 8, 3.8, SHELL)
    # its spiral
    for x, y in ((6, 6), (7, 6), (8, 6), (9, 7), (9, 8), (8, 9), (7, 9), (6, 8), (7, 7)):
        cv.set(x, y, SHELL[0])
    cv.set(8, 8, hexc(0xF4F0FF))
    # a bubble nozzle
    gun(cv, 7, 11, 13, SHELL, tip=SHELL[3])
    cv.set(14, 7, hexc(0xD8FFFF))
    cv.set(14, 8, hexc(0xA8F0FF))
    return cv


def head_demonic():
    cv = Canvas()
    box(cv, 3, 4, 10, 11, HELLIRON)
    for x, y in ((4, 6), (5, 7), (5, 10), (9, 5), (8, 6)):
        cv.set(x, y, FIRE[1])
    cv.set(7, 7, FIRE[3])
    cv.set(7, 8, FIRE[3])
    # horns sweeping back
    for x, y, c in ((3, 3, 2), (2, 2, 3), (1, 2, 3), (3, 12, 1), (2, 13, 2), (1, 13, 3)):
        cv.set(x, y, HORN[c])
    gun(cv, 5, 11, 14, HELLIRON, tip=FIRE[3], thick=1)
    gun(cv, 10, 11, 14, HELLIRON, tip=FIRE[3], thick=1)
    cv.set(11, 6, HELLIRON[2])
    cv.set(11, 9, HELLIRON[2])
    glow(cv, FIRE[0], 70)
    return cv


def head_light():
    cv = Canvas()
    disc(cv, 7.5, 8, 3.6, WHITE)
    # a gold ring round it, and the prism at its heart
    for x, y in ((5, 5), (10, 5), (5, 11), (10, 11), (4, 8), (11, 8)):
        cv.set(x, y, GOLD[2])
    for x, y, c in ((7, 6, 0xFFF8C0), (6, 7, 0xFFFFFF), (7, 7, 0xC8F0FF), (8, 7, 0xFFE070), (7, 8, 0xFFFFFF),
                    (6, 8, 0xFFE070), (8, 8, 0xC8F0FF), (7, 9, 0xFFF8C0)):
        cv.set(x, y, hexc(c))
    gun(cv, 7, 11, 15, GOLD, tip=hexc(0xFFFFFF))
    glow(cv, hexc(0xFFE890), 80)
    return cv


def head_ancient():
    cv = Canvas()
    box(cv, 3, 4, 11, 12, STONE)
    for x, y in ((4, 5), (4, 7), (5, 11), (10, 5), (10, 11), (4, 10)):
        cv.set(x, y, JADE[2])
    cv.rect(6, 7, 8, 9, hexc(0xFFD040))
    cv.set(7, 8, hexc(0x201000))
    cv.set(6, 7, hexc(0xFFF0A0))
    gun(cv, 5, 12, 15, STONE, tip=JADE[3])
    gun(cv, 9, 12, 15, STONE, tip=JADE[3])
    glow(cv, JADE[1], 60)
    return cv


def head_skystorm():
    cv = Canvas()
    box(cv, 3, 4, 10, 11, STORM)
    # coils round the orb
    for x, y in ((5, 5), (9, 5), (4, 8), (9, 10), (5, 10)):
        cv.set(x, y, hexc(0xC08A30))
    disc(cv, 7, 7.5, 2.2, VOLT)
    cv.set(6, 6, hexc(0xFFFFFF))
    # arcs off it
    for x, y in ((2, 3), (1, 2), (2, 12), (1, 13), (11, 3)):
        cv.set(x, y, VOLT[2], 220)
    gun(cv, 5, 11, 15, STORM, tip=VOLT[2], thick=1)
    gun(cv, 9, 11, 15, STORM, tip=VOLT[2], thick=1)
    cv.set(11, 6, STORM[1])
    cv.set(11, 8, STORM[1])
    glow(cv, VOLT[0], 90)
    return cv


def head_war():
    cv = Canvas()
    box(cv, 3, 5, 9, 11, OLIVE)
    cv.set(5, 8, hexc(0xE8E0C0))
    cv.set(4, 8, hexc(0xB8B090))
    cv.set(6, 8, hexc(0xB8B090))
    cv.set(5, 7, hexc(0xB8B090))
    # the gatling's cluster of barrels, and its drum
    for y, c in ((6, 3), (7, 2), (8, 1), (9, 0)):
        for x in range(10, 16):
            cv.set(x, y, GUN[c])
    cv.rect(10, 5, 11, 10, GUN[2])
    cv.set(15, 7, hexc(0x101010))
    cv.set(15, 8, hexc(0x101010))
    cv.rect(4, 2, 7, 4, BRASS[1])
    cv.set(4, 2, BRASS[3])
    return cv


# Ferrogrim's Bastion Turret: his forge iron, its seams glowing
FORGE = ramp(0x8A2A06, 0xD05A10, 0xFF9A2A, 0xFFE070)
BLACKIRON = ramp(0x16181C, 0x2A2E36, 0x464C58, 0x6E7686)


def base_bastion():
    cv = Canvas()
    # four stout iron feet
    for x in (4, 5, 10, 11):
        cv.set(x, 14, BLACKIRON[1])
        cv.set(x, 15, BLACKIRON[0])
    cv.set(4, 15, BLACKIRON[2])
    cv.set(10, 15, BLACKIRON[2])
    # the keep: a block of black iron, battlements on its rim, a forge in its gate
    cv.shade_rect(3, 9, 12, 14, BLACKIRON)
    for x in (3, 5, 10, 12):
        cv.set(x, 8, BLACKIRON[3])
    cv.rect(6, 12, 9, 14, FORGE[0])
    cv.rect(7, 12, 8, 13, FORGE[2])
    cv.set(7, 12, FORGE[3])
    cv.set(6, 11, BLACKIRON[0])
    cv.set(9, 11, BLACKIRON[0])
    # molten seams and rivets
    for x, y in ((4, 11), (11, 10), (5, 13), (10, 13)):
        cv.set(x, y, FORGE[1])
    for x, y in ((4, 10), (11, 12)):
        cv.set(x, y, hexc(0xA8B2BE))
    turntable(cv, BLACKIRON, 8, 3, 12)
    cv.set(3, 8, FORGE[2])
    cv.set(12, 8, FORGE[2])
    glow(cv, FORGE[0], 55)
    return cv


def head_bastion():
    cv = Canvas()
    # a heavy riveted housing, a molten core in its middle, a crest of iron behind
    box(cv, 2, 3, 10, 13, BLACKIRON)
    for x, y in ((3, 4), (9, 4), (3, 12), (9, 12)):
        cv.set(x, y, hexc(0xA8B2BE))
    disc(cv, 6.5, 8.5, 2.2, FORGE)
    cv.set(6, 8, FORGE[3])
    cv.set(1, 7, BLACKIRON[2])
    cv.set(1, 8, BLACKIRON[1])
    cv.set(1, 9, BLACKIRON[0])
    # three barrels, the shards' (Turret.BARREL_GAP apart), glowing at the mouth
    for y in (4, 8, 12):
        gun(cv, y, 11, 14, BLACKIRON, tip=FORGE[3], thick=1)
        cv.set(11, y, BLACKIRON[3])
    glow(cv, FORGE[0], 50)
    return cv


BASES = [base_makeshift, base_basic, base_advanced, base_organic, base_aquatic, base_demonic, base_light,
         base_ancient, base_skystorm, base_war]
HEADS = [head_makeshift, head_basic, head_advanced, head_organic, head_aquatic, head_demonic, head_light,
         head_ancient, head_skystorm, head_war]


# --- the blueprints ----------------------------------------------------------------------------

# each tier's paper (dark, middle, its grid, the drawing's lines), its seal and its trim
PAPERS = [
    # 0 a parchment scrawl in charcoal
    ((0x8A6E44, 0xC8A670, 0xB49260, 0x3A2A1A), 0xB07840, None),
    # 1-4 blueprints, each its own blue, the roll and seal in the turret's colour
    ((0x1E3E7A, 0x2A5AA8, 0x4072C0, 0xE8F0FF), 0x9AA8B8, None),
    ((0x142C66, 0x1E4C9A, 0x2E66C0, 0xA8F0FF), 0x58B8FF, None),
    ((0x1A4A44, 0x2A6E62, 0x3C8A7A, 0xE0FFD8), 0x7CD050, None),
    ((0x124858, 0x1E6E82, 0x2E8CA0, 0xE0FFFF), 0x40E0D0, 0xC0C8D0),
    # 5 a stolen page, dark with ash
    ((0x2A1430, 0x4A2040, 0x602848, 0xFFC0A0), 0xFF4020, 0xC0C8D0),
    # 6 white vellum, gold lines
    ((0xC8C0A8, 0xF4F0E0, 0xE4DCC4, 0xC0962E), 0xFFF0A0, 0xE8C450),
    # 7 a sandstone tablet carved in jade
    ((0x8E7A58, 0xC4B088, 0xB09C74, 0x24A07A), 0x40D8A0, 0xFFD040),
    # 8 a storm-dark sheet drawn in light
    ((0x10102A, 0x1E1E48, 0x2C2C66, 0x9AD8FF), 0x8C7CFF, 0xFFFFFF),
    # 9 the War Turret's field manual
    ((0x3A4220, 0x5A6430, 0x6A7438, 0xF0E8C8), 0xFFB030, 0xD8A840),
]
# how many barrels each blueprint draws
BARRELS = [1, 1, 1, 2, 1, 2, 1, 2, 2, 4]


def blueprint(tier):
    cv = Canvas()
    (dark, mid, grid, ink), seal, trim = PAPERS[tier]
    dark, mid, grid, ink, seal = hexc(dark), hexc(mid), hexc(grid), hexc(ink), hexc(seal)
    # the sheet, its grid
    for y in range(2, 14):
        for x in range(3, 15):
            c = grid if (x % 3 == 0 or y % 3 == 0) else mid
            cv.set(x, y, c)
    # a curled corner bottom right
    cv.px.pop((14, 13), None)
    cv.px.pop((13, 13), None)
    cv.px.pop((14, 12), None)
    cv.set(13, 12, dark)
    cv.set(12, 13, dark)
    cv.set(13, 13, grid)
    # the rolled edge on the left, tied in the turret's colour
    lit = tuple(min(255, v + 70) for v in seal)
    deep = tuple(v * 3 // 5 for v in seal)
    for y in range(1, 15):
        cv.set(1, y, dark)
        cv.set(2, y, mid if y % 2 else grid)
        cv.set(3, y, dark)
    for y in (1, 14):
        cv.set(1, y, deep)
        cv.set(2, y, lit)
        cv.set(3, y, seal)
    # the trim (gold or silver) along the top and right
    if trim:
        t = hexc(trim)
        for x in range(4, 15):
            cv.set(x, 2, t)
        for y in range(2, 12):
            cv.set(14, y, t)
    # the drawing: a turret on its feet, its barrels to the right
    for x in range(6, 11):
        cv.set(x, 11, ink)            # base line
    cv.set(6, 10, ink)
    cv.set(10, 10, ink)
    for y in range(8, 10):
        cv.set(8, y, ink)             # pedestal
    for x in range(6, 11):
        cv.set(x, 4, ink)
        cv.set(x, 7, ink)
    for y in range(4, 8):
        cv.set(6, y, ink)
        cv.set(10, y, ink)
    rows = {1: [5], 2: [5, 6], 4: [4, 5, 6, 7]}[BARRELS[tier]]
    for y in rows:
        for x in range(11, 13 if tier == 0 else 14):
            cv.set(x, y, ink)
    # what sets each drawing apart: a scope, leaves, a shell, horns, a prism, an eye, an orb
    extra = {
        2: [(7, 3), (8, 3), (9, 3)],
        3: [(6, 3), (7, 2), (8, 3), (9, 2)],
        4: [(14, 4), (14, 3)],
        5: [(6, 3), (5, 2), (10, 3), (11, 2)],
        6: [(8, 3), (8, 2), (7, 2), (9, 2)],
        7: [(7, 5), (9, 5)],
        8: [(8, 3), (7, 2), (8, 2), (9, 2)],
    }.get(tier, [])
    for x, y in extra:
        cv.set(x, y, ink)
    cv.set(8, 5, seal)               # the core, in the turret's colour
    # the seal, bottom left
    cv.rect(4, 10, 5, 12, seal)
    cv.set(5, 11, lit)
    cv.set(4, 12, deep)
    cv.set(5, 12, deep)
    if tier >= 6:
        glow(cv, seal, 70)
    return cv


# --- the shots ---------------------------------------------------------------------------------

def shot(tier):
    cv = Canvas(8)
    colors = [
        (0x8A4418, 0xD08A4A, 0xFFE0A0),   # a rusty nail
        (0x5A626C, 0xA8B2BE, 0xFFFFFF),   # an iron bolt
        (0x2E5A88, 0x58B8FF, 0xE8F8FF),   # a blue tracer
        (0x2E8424, 0x8CDC5A, 0xE8FFB0),   # a thorn
        (0x3A9AA8, 0x9AF0FF, 0xFFFFFF),   # a bubble
        (0xD02800, 0xFF7A20, 0xFFE070),   # a hellfire bolt
        (0xE8C450, 0xFFF4C0, 0xFFFFFF),   # a lance of light
        (0x24A07A, 0x60F0C0, 0xE8FFF4),   # a jade dart
        (0x6A5AFF, 0x9AD8FF, 0xFFFFFF),   # a bolt of lightning
        (0xB07E28, 0xFFDA7A, 0xFFFFFF),   # a brass round
    ][tier]
    d, m, l = (hexc(v) for v in colors)
    if tier == 4:
        # a bubble: a ring with a glint
        for x, y in ((2, 2), (3, 1), (4, 1), (5, 2), (6, 3), (6, 4), (5, 5), (4, 6), (3, 6), (2, 5), (1, 4), (1, 3)):
            cv.set(x, y, m)
        cv.set(3, 2, l)
        for x, y in ((3, 3), (4, 3), (3, 4), (4, 4), (5, 3), (5, 4), (2, 3), (2, 4), (4, 2), (3, 5), (4, 5)):
            cv.set(x, y, d, 110)
        return cv
    if tier == 8:
        # a jagged bolt
        for x, y, c in ((0, 4, d), (1, 3, m), (2, 3, l), (3, 4, l), (4, 4, l), (5, 3, l), (6, 3, m), (7, 3, l),
                        (2, 4, m), (5, 4, m), (1, 4, d), (6, 4, d)):
            cv.set(x, y, c)
        glow(cv, d, 120)
        return cv
    if tier == 3:
        # a thorn: thick at the back, a point at the front
        for x, y, c in ((1, 3, d), (1, 4, d), (2, 3, m), (2, 4, d), (3, 3, m), (3, 4, m), (4, 3, l), (4, 4, m),
                        (5, 3, l), (6, 4, l), (5, 4, m), (2, 2, m), (1, 5, d)):
            cv.set(x, y, c)
        return cv
    length = {0: 4, 1: 5, 2: 6, 5: 5, 6: 7, 7: 5, 9: 3}[tier]
    x0 = 7 - length
    for x in range(x0, 8):
        cv.set(x, 3, l if x >= 6 else m)
        cv.set(x, 4, m if x >= 6 else d)
    cv.set(7, 3, l)
    if tier in (2, 5, 6, 7):
        # a trail behind it
        for x in range(max(0, x0 - 2), x0):
            cv.set(x, 3, d, 140)
            cv.set(x, 4, d, 90)
    if tier == 0:
        cv.set(x0, 2, d)   # the nail's head
        cv.set(x0, 5, d)
    if tier in (5, 6, 8):
        glow(cv, d, 90)
    return cv


def sheets():
    sheet = Image.new("RGBA", (CELL * COLUMNS, CELL * 4), (0, 0, 0, 0))
    shots = Image.new("RGBA", (8 * COLUMNS, 8), (0, 0, 0, 0))
    for i in range(COLUMNS):
        sheet.paste(blueprint(i).image(), (i * CELL, 0))
        sheet.paste(BASES[i]().image(), (i * CELL, CELL))
        sheet.paste(HEADS[i]().image(), (i * CELL, CELL * 2))
        shots.paste(shot(i).image(), (i * 8, 0))
    # row 3: Ferrogrim's Bastion Turret, its base then its head
    sheet.paste(base_bastion().image(), (0, CELL * 3))
    sheet.paste(head_bastion().image(), (CELL, CELL * 3))
    return sheet, shots


def main():
    sheet, shots = sheets()
    sheet.save(OUT)
    shots.save(OUT_SHOTS)
    print("wrote", OUT)
    print("wrote", OUT_SHOTS)
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        both = Image.new("RGBA", (sheet.width, sheet.height + CELL), (0, 0, 0, 0))
        both.paste(sheet, (0, 0))
        for i in range(COLUMNS):
            both.paste(shots.crop((i * 8, 0, i * 8 + 8, 8)), (i * CELL + 4, sheet.height + 4))
        big = both.resize((both.width * 8, both.height * 8), Image.NEAREST)
        out = Image.new("RGBA", (big.width, big.height * 2), (0, 0, 0, 255))
        out.paste((40, 40, 46, 255), (0, 0, big.width, big.height))
        out.paste((200, 200, 196, 255), (0, big.height, big.width, big.height * 2))
        out.alpha_composite(big, (0, 0))
        out.alpha_composite(big, (0, big.height))
        out.save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
