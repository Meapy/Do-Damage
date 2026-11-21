"""The Crucible's five champions at 32x32 (tools/build_crucible.py puts them on its sheets), one
for each floor of the tower:

- Ferrogrim, the Iron Warden: a hulking knight of riveted iron, a tower shield and a great hammer,
  the forge's glow in his visor;
- Nerissa, the Tidecaller: a sea-queen with a serpent's tail, a coral crown and a golden trident;
- Pyrrhax, the Forge Tyrant: a horned demon of obsidian split by molten veins, a hammer of lava;
- Chronarch, the Hour Thief: a brass automaton with a clock for a heart, four arms and a pendulum;
- Vaelith, the Crucible Sovereign: the tower's maker, a champion in white and gold with wings of
  flame and a halo of the four floors' colours.

Drawn as the Black Between's are (tools/black_between/bosses.py, whose helpers these use): shapes
shaded from the upper left, hand-placed details, no outline of their own (the client draws it)."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from black_between.bosses import Canvas, C, R, poly_mask, ell_mask, wing, sparkle, orb, spectral  # noqa: E402


def rect_mask(x0, y0, x1, y1):
    return {(x, y) for y in range(int(y0), int(y1) + 1) for x in range(int(x0), int(x1) + 1)}


def line(cv, x0, y0, x1, y1, c, a=255):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for i in range(n + 1):
        t = i / max(1, n)
        cv.px(round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t), c, a)


def thick(cv, x0, y0, x1, y1, w, ramp):
    """A shaded bar from (x0, y0) to (x1, y1), w pixels across."""
    a = math.atan2(y1 - y0, x1 - x0)
    nx, ny = -math.sin(a) * w / 2, math.cos(a) * w / 2
    cv.shade(poly_mask([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)]), ramp)


def glow_ring(cv, x, y, r, c, a=90):
    for (gx, gy) in ell_mask(x, y, r + 1.2, r + 1.2) - ell_mask(x, y, r, r):
        cv.px(gx, gy, c, a)


# =================================================================================================
# Ferrogrim, the Iron Warden

IRON = R("#1c2029", "#3a414f", "#646e80", "#a4afc0")
IRON_DARK = R("#121419", "#242831", "#3c4250", "#5c6476")
TABARD = R("#3a0e0e", "#6a1a16", "#962a20", "#c4482c")
EMBER = R("#8a2a06", "#d8600e", "#ff9a2a", "#ffe29a")


def ferrogrim(frame=0, swing=False, tempest=False):
    """A horned juggernaut of riveted iron: a furnace burning in his chest, spiked pauldrons, a
    tower shield with the anvil on it, a great hammer whose head is still molten from the forge.
    The furnace breathes (frame), embers drip off the hammer; in his Tempest the forge breaks
    through every seam of the armor and the hammer whirls round him."""
    cv = Canvas()
    f = frame % 3
    hot = (EMBER[2], EMBER[3], EMBER[2])[f]
    core = (EMBER[1], EMBER[2], EMBER[3])[f]
    seam = EMBER[2] if tempest else IRON_DARK[0]
    bob = (0, 1, 0)[f]
    # the shadow of his bulk
    for (x, y) in ell_mask(16, 30.5, 11, 1.6):
        cv.px(x, y, "#000000", 70)
    # greaves and sabatons, the knees riveted
    for lx, off in ((10, (0, 1, 0)[f]), (18, (1, 0, 0)[f])):
        cv.shade(rect_mask(lx, 23, lx + 4, 29 - off), IRON, grad=lambda x, y: 2 if x < lx + 2 else 1)
        cv.shade(rect_mask(lx - 1, 28 - off, lx + 5, 30 - off), IRON_DARK)
        cv.px(lx + 2, 25, IRON[3]); cv.px(lx + 2, 26, seam)
    # the hammer over his right shoulder (behind him), its head molten
    if not swing and not tempest:
        thick(cv, 27, 27, 26, 6, 2, IRON_DARK)
        head = rect_mask(21, 1 + bob, 31, 7 + bob)
        cv.shade(head, IRON, grad=lambda x, y: 2 if y < 4 + bob else 1)
        for x in range(22, 31):
            cv.px(x, 4 + bob, hot if x % 2 else core)
        cv.px(21, 1 + bob, IRON[3]); cv.px(31, 7 + bob, IRON_DARK[0])
        # embers dripping off it
        for k, (ex, ey) in enumerate(((23, 9), (29, 10), (26, 12))):
            cv.px(ex, ey + (f + k) % 3 + bob, EMBER[2 + (k + f) % 2], 220)
    # the body: a barrel of plate, darker to the right
    body = poly_mask([(8, 12), (24, 12), (25, 18), (22, 24), (10, 24), (7, 18)])
    cv.shade(body, IRON, grad=lambda x, y: 2 if x < 15 else 1)
    # the furnace in his chest: a grate over the fire
    cv.shade(rect_mask(12, 14, 20, 20), IRON_DARK)
    for y in range(15, 20):
        for x in range(13, 20):
            if x % 2 == 1:
                cv.px(x, y, core if y > 16 else hot)
    cv.px(16, 17, "#ffffff" if f == 2 else EMBER[3])
    # the seams of the plate (fire showing through them in his Tempest)
    for (x0, y0, x1, y1) in ((9, 21, 23, 21), (11, 13, 11, 20), (21, 13, 21, 20)):
        line(cv, x0, y0, x1, y1, seam, 255 if tempest else 160)
    # the belt and its anvil buckle, the faulds below
    cv.shade(rect_mask(9, 21, 23, 22), IRON_DARK)
    for (x, y) in ((15, 21), (16, 21), (17, 21), (16, 22)):
        cv.px(x, y, EMBER[3] if tempest else EMBER[2])
    for fx in (10, 14, 18):
        cv.shade(rect_mask(fx, 23, fx + 3, 25), IRON, grad=lambda x, y: 2 if y == 23 else 1)
    # pauldrons, each with two spikes
    for px_, d in ((8, -1), (24, 1)):
        cv.shade(ell_mask(px_, 12.5, 4.6, 3.6), IRON, grad=lambda x, y: 3 if y < 11 else 1)
        for k in (0, 1):
            sx, sy = px_ + d * (1 + 2 * k), 9.5 - k
            line(cv, sx, sy, sx + d * 2, sy - 3, IRON[3])
            cv.px(sx + d * 2, sy - 3, "#ffffff")
        cv.px(px_, 14, seam)
    # the helm: a horned great helm, a burning T in its face
    helm = poly_mask([(12, 2), (20, 2), (21.5, 10), (19, 12.5), (13, 12.5), (10.5, 10)])
    cv.shade(helm, IRON, grad=lambda x, y: 2 if x < 16 else 1)
    for hx, d in ((12, -1), (20, 1)):
        line(cv, hx, 4, hx + d * 3, 2, IRON_DARK[2])
        line(cv, hx + d * 3, 2, hx + d * 4, -1, IRON_DARK[3])
        cv.px(hx + d * 4, 0, IRON[3])
    for x in range(13, 20):
        cv.px(x, 6, hot if 14 <= x <= 18 else core)
    for y in range(7, 11):
        cv.px(16, y, hot if y < 9 else core)
    cv.px(14, 6, "#ffffff"); cv.px(18, 6, "#ffffff")
    cv.px(16, 1, IRON[3]); cv.px(16, 2, IRON[3])
    # the tower shield on his left arm: an iron rim, the anvil burning on it
    sh = poly_mask([(1, 11), (10, 11), (10, 25), (5.5, 30), (1, 25)])
    cv.shade(sh, IRON, grad=lambda x, y: 2 if y < 18 else 1)
    for (x, y) in sh:
        if (x - 1, y) not in sh or (x + 1, y) not in sh or (x, y - 1) not in sh or (x, y + 1) not in sh:
            cv.px(x, y, IRON_DARK[2] if (x + y) % 3 else IRON[3])
    anvil = [(3, 15), (4, 15), (5, 15), (6, 15), (7, 15), (8, 15), (4, 16), (5, 16), (6, 16), (7, 16),
             (5, 17), (6, 17), (5, 18), (6, 18), (4, 19), (5, 19), (6, 19), (7, 19)]
    for (x, y) in anvil:
        cv.px(x, y, core if (x + y + f) % 4 else hot)
    for (x, y) in ((3, 12), (8, 12), (3, 24), (8, 24), (5, 28)):
        cv.px(x, y, IRON[3])
    if swing and not tempest:
        # the hammer brought down in front of him: the floor cracking and fire leaping
        thick(cv, 22, 14, 28, 24, 2, IRON_DARK)
        cv.shade(rect_mask(24, 21, 31, 27), IRON)
        for x in range(25, 31):
            cv.px(x, 24, hot if x % 2 else core)
        for (x, y) in ((23, 29), (24, 30), (30, 29), (31, 28), (27, 30), (21, 30), (28, 31)):
            cv.px(x, y, EMBER[2])
        for (x, y) in ((26, 19), (29, 18), (31, 20), (23, 19)):
            cv.px(x, y, EMBER[3], 220)
        cv.px(27, 29, "#ffffff")
    if tempest:
        # the hammer whirling round him, its arc of fire behind it
        ang = f * 120 + 20
        a = math.radians(ang)
        hx, hy = 16 + math.cos(a) * 12, 16 + math.sin(a) * 9
        thick(cv, 16, 16, hx, hy, 2, IRON_DARK)
        cv.shade(ell_mask(hx, hy, 3.4, 3.4), IRON)
        cv.px(hx, hy, hot); cv.px(hx - 1, hy - 1, "#ffffff")
        for k in range(1, 7):
            b = math.radians(ang - k * 18)
            cv.px(16 + math.cos(b) * 13, 16 + math.sin(b) * 10, EMBER[3 if k < 3 else 2], 240 - k * 32)
            cv.px(16 + math.cos(b) * 11, 16 + math.sin(b) * 8, EMBER[2], 200 - k * 28)
        # sparks thrown off
        for k in range(4):
            b = math.radians(ang + 40 + k * 70 + f * 25)
            cv.px(16 + math.cos(b) * 15, 15 + math.sin(b) * 13, EMBER[3], 200)
    # the gauntlet on the haft
    cv.shade(ell_mask(24.5, 17 if not swing else 15, 2.2, 2.2), IRON)
    return cv.img


# =================================================================================================
# Nerissa, the Tidecaller

SCALE = R("#0a2a36", "#11505e", "#1a8288", "#46c2b4")
SCALE_BELLY = R("#2a5a52", "#4a8a78", "#7ac0a4", "#b4e8cc")
SKIN = R("#2e6a6a", "#5aa8a0", "#94d8c8", "#d0f4e8")
HAIR = R("#0a1430", "#152a5a", "#22448a", "#3a6ac0")
CORAL = R("#6a1430", "#b02a48", "#ec5a6a", "#ffa8a0")
GOLD = R("#5a3a0a", "#9a6a14", "#d8a428", "#ffe68a")
FOAM = "#e8fbff"


def nerissa(frame=0, cast=False, maelstrom=False):
    cv = Canvas()
    sway = (-1, 0, 1)[frame % 3]
    if maelstrom:  # a ring of water turning round her
        for k in range(18):
            a = math.radians(k * 20 + frame * 20)
            x, y = 16 + math.cos(a) * 14, 18 + math.sin(a) * 9
            cv.px(x, y, SCALE[3] if k % 3 else FOAM, 220)
            cv.px(x + 1, y, SCALE[2], 150)
    # the tail: a coil at the bottom, its tip curling out to the right
    coil = ell_mask(15, 26, 9, 4.6)
    cv.shade(coil, SCALE, grad=lambda x, y: 2 if y < 25 else 1)
    inner = ell_mask(15, 27, 5.5, 2.2)
    cv.shade(inner, SCALE_BELLY)
    tip = poly_mask([(22, 24), (27 + sway, 21), (31, 17 + sway), (29, 22), (25, 27)])
    cv.shade(tip, SCALE)
    for (x, y) in ((29 + sway, 17), (30 + sway, 16), (28 + sway, 18)):  # a fin at its tip
        cv.px(x, y, CORAL[2])
    for (x, y) in coil:
        if (x * 3 + y * 5) % 7 == 0 and (x, y) not in inner:
            cv.px(x, y, SCALE[3])
    # the long hair, streaming back to the left
    cv.shade(poly_mask([(12, 3), (19, 4), (18, 10), (14, 16 + sway), (8, 21), (5, 18 - sway), (8, 12), (10, 6)]), HAIR)
    for (x, y) in ((9, 12), (8, 15), (7, 18), (11, 9)):
        cv.px(x, y, HAIR[3])
    # torso
    cv.shade(poly_mask([(13, 11), (19, 11), (20, 17), (19, 22), (13, 22), (12, 17)]), SKIN, grad=lambda x, y: 2 if x < 16 else 1)
    cv.shade(poly_mask([(13, 13), (19, 13), (19, 16), (16, 17), (13, 16)]), CORAL)  # shell top
    cv.px(14, 14, CORAL[3]); cv.px(18, 14, CORAL[3])
    for x in range(13, 20):  # a band of gold at the waist
        cv.px(x, 21, GOLD[2] if x % 2 else GOLD[3])
    # head and face
    cv.shade(ell_mask(16, 7.5, 3.3, 3.6), SKIN)
    cv.px(15, 7, "#0a1430"); cv.px(18, 7, "#0a1430")
    cv.px(15, 6, "#ffffff"); cv.px(18, 6, "#ffffff")
    cv.px(17, 9, CORAL[1])
    for (x, y) in ((13, 6), (12, 7), (20, 6), (21, 7)):  # fin ears
        cv.px(x, y, SCALE[3])
    # the coral crown
    for (x, top) in ((13, 1), (15, 0), (17, 1), (19, 0), (21, 2)):
        line(cv, x, 4, x - 0.5, top, CORAL[2])
        cv.px(x - 0.5, top, CORAL[3])
    for x in range(13, 21):
        cv.px(x, 4, CORAL[1])
    # arms and the trident
    tx = 25 if not cast else 24
    top = 1 if not cast else 0
    thick(cv, 19, 13, 24, 15 if not cast else 9, 2, SKIN)
    thick(cv, 13, 13, 9, 18, 2, SKIN)
    line(cv, tx, top + 3, tx, 30, GOLD[2])
    line(cv, tx + 1, top + 4, tx + 1, 30, GOLD[1])
    for dx in (-3, 0, 3):
        line(cv, tx + dx, top, tx + dx, top + 3, GOLD[3])
    line(cv, tx - 3, top + 3, tx + 3, top + 3, GOLD[2])
    if cast:  # the trident's tips flare with sea-light, and water gathers in her other hand
        for dx in (-3, 0, 3):
            sparkle(cv, tx + dx, top, SCALE[3])
        orb(cv, 8, 19, 2.6, SCALE, glow=SCALE[3])
    else:
        orb(cv, 8.5, 19, 1.6, SCALE, glow=SCALE[3])
    # droplets
    for (x, y) in ((4, 26 + frame % 2), (28, 29), (2, 14 + sway)):
        cv.px(x, y, FOAM, 200)
    return cv.img


# =================================================================================================
# Pyrrhax, the Forge Tyrant

OBSIDIAN = R("#120c0e", "#261a1c", "#3e2a28", "#5e403a")
LAVA = R("#7a1606", "#e0400c", "#ff9a24", "#fff2a6")
HORN = R("#2a2420", "#544a40", "#8a7a66", "#c4b49a")


ROCK = R("#1e1212", "#3e2622", "#62403a", "#8e6454")
ASH_WING = R("#140a0a", "#2e1614", "#4e2418", "#7a3418")


def _rim(cv, mask, c, a=200):
    """The fire under him lighting the edges of a shape from below."""
    for (x, y) in mask:
        if (x, y + 1) not in mask:
            cv.px(x, y, c, a)


def pyrrhax(frame=0, slam=False, molten=False):
    """A horned demon of the forge: rock split by molten veins round a burning heart, a crown of
    fire between his horns, wings of ash and cinder, a forge hammer of lava in his fist; the fire
    in him breathes (frame). As his Core goes molten it breaks out all over him and his wings
    catch."""
    cv = Canvas()
    f = frame % 3
    hot = (LAVA[2], LAVA[3], LAVA[2])[f] if not molten else LAVA[3]
    warm = LAVA[1] if not molten else LAVA[2]
    # the heat under him
    for (x, y) in ell_mask(16, 30.5, 11, 1.6):
        cv.px(x, y, LAVA[1], 90)
    # wings of ash, ragged, their edges smouldering (burning when molten)
    flap = (0, 1, 0)[f]
    for d in (-1, 1):
        pts = [(16 + d * 6, 11), (16 + d * 15, 3 - flap), (16 + d * 14, 8), (16 + d * 16, 12 + flap),
               (16 + d * 13, 14), (16 + d * 14, 19 + flap), (16 + d * 9, 17)]
        m = poly_mask(pts)
        cv.shade(m, ASH_WING, rim=False)
        for (x, y) in m:
            if (x + d, y) not in m or (x, y + 1) not in m:
                cv.px(x, y, hot if molten and (x + y + f) % 2 else warm, 230)
        line(cv, 16 + d * 6, 11, 16 + d * 15, 3 - flap, ROCK[2])
    # legs: digitigrade, hooves of horn
    for lx, off in ((10, (0, 1, 0)[f]), (18, (1, 0, 0)[f])):
        leg = poly_mask([(lx, 21), (lx + 4, 21), (lx + 5, 26), (lx + 3, 29 - off), (lx, 29 - off), (lx - 1, 25)])
        cv.shade(leg, ROCK)
        _rim(cv, leg, warm, 150)
        cv.shade(rect_mask(lx - 1, 29 - off, lx + 4, 31 - off), HORN)
        cv.px(lx + 2, 24, warm)
    # the body: rock plates over the fire
    body = poly_mask([(8, 11), (24, 11), (24, 19), (20, 24), (12, 24), (8, 19)])
    cv.shade(body, ROCK, grad=lambda x, y: 2 if x < 14 else 1)
    _rim(cv, body, warm)
    # the burning heart and the veins running from it
    cv.shade(ell_mask(16, 16.5, 3.2, 2.8), LAVA)
    cv.px(15, 15, "#ffffff" if f == 1 or molten else LAVA[3])
    veins = [[(16, 17), (12, 14), (9, 12)], [(16, 17), (20, 14), (23, 12)], [(16, 17), (13, 21), (11, 24)],
             [(16, 17), (19, 21), (21, 24)], [(16, 17), (16, 23)], [(12, 14), (10, 18)], [(20, 14), (22, 18)]]
    for v in veins:
        for (a, b) in zip(v, v[1:]):
            line(cv, a[0], a[1], b[0], b[1], hot if molten else LAVA[2])
    for (x, y) in ((12, 14), (20, 14), (13, 21), (19, 21)):
        cv.px(x, y, LAVA[3])
    # shoulders: plates of rock, a horn on each; flames on them when molten
    for sx, d in ((8, -1), (24, 1)):
        m = ell_mask(sx, 11.5, 4.2, 3.2)
        cv.shade(m, ROCK, grad=lambda x, y: 3 if y < 10 else 1)
        _rim(cv, m, warm)
        line(cv, sx + d, 9, sx + d * 3, 6, HORN[2])
        cv.px(sx + d * 3, 6, HORN[3])
        if molten:
            for k in range(3):
                cv.px(sx - 1 + k, 7 - (f + k) % 3, LAVA[2 + (k + f) % 2])
    # the head: a long skull, horns curling up and out, eyes and maw burning
    head = poly_mask([(12, 4), (20, 4), (21, 8), (19, 12), (13, 12), (11, 8)])
    cv.shade(head, ROCK, grad=lambda x, y: 2 if x < 16 else 1)
    _rim(cv, head, warm)
    for d in (-1, 1):
        pts = [(16 + d * 3, 5), (16 + d * 7, 3), (16 + d * 9, -1), (16 + d * 10, 3), (16 + d * 7, 6)]
        cv.shade(poly_mask(pts), HORN)
        cv.px(16 + d * 9, 0, HORN[3])
    for x, y in ((14, 7), (18, 7)):
        cv.px(x, y, "#ffffff"); cv.px(x - 1, y, LAVA[3]); cv.px(x + 1, y, LAVA[2])
    for x in range(14, 19):
        cv.px(x, 10, LAVA[2] if x % 2 else LAVA[1])
    cv.px(15, 11, LAVA[3]); cv.px(17, 11, LAVA[3])
    # the crown of fire between his horns
    for k, x in enumerate(range(13, 20)):
        h = (2, 3, 4, 5, 4, 3, 2)[k] + ((k + f) % 2)
        for y in range(4 - h, 4):
            t = (4 - y) / h
            cv.px(x, y, LAVA[3] if t < 0.4 else LAVA[2] if t < 0.75 else LAVA[1], 255 if t < 0.8 else 170)
    # his left arm, the claw open
    thick(cv, 8, 13, 5, 21, 3, ROCK)
    cv.shade(ell_mask(5, 22, 2.2, 2.2), ROCK)
    for (x, y) in ((3, 24), (5, 25), (7, 24)):
        cv.px(x, y, HORN[3])
    # the hammer of lava in his right
    if slam:
        thick(cv, 24, 13, 27, 21, 3, ROCK)
        thick(cv, 27, 20, 27, 26, 2, HORN)
        cv.shade(rect_mask(22, 24, 31, 30), ROCK)
        for x in range(22, 32):
            cv.px(x, 27, LAVA[3] if x % 2 else LAVA[2])
        for (x, y) in ((21, 31), (23, 31), (30, 31), (31, 30), (26, 31), (19, 30), (28, 22), (24, 21), (31, 23)):
            cv.px(x, y, LAVA[2 + (x + y) % 2])
        cv.px(26, 28, "#ffffff")
    else:
        thick(cv, 24, 13, 27, 19, 3, ROCK)
        thick(cv, 27, 19, 27, 5, 2, HORN)
        cv.shade(rect_mask(22, 0 + (f == 1), 31, 6 + (f == 1)), ROCK)
        for x in range(22, 32):
            cv.px(x, 3 + (f == 1), LAVA[3] if (x + f) % 2 else LAVA[2])
        for x in range(23, 31, 2):
            cv.px(x, 7 + (f == 1) + (x + f) % 3, LAVA[2], 220)
    cv.shade(ell_mask(26.5, 18 if not slam else 20, 2.2, 2.2), ROCK)
    # embers rising off him
    for k, (x, y) in enumerate(((2, 8), (30, 12), (5, 3), (26, 16), (14, 1), (19, 0))):
        cv.px(x, y - f + k % 2, LAVA[2 + (k + f) % 2], 220)
    return cv.img


# =================================================================================================
# Chronarch, the Hour Thief

BRASS = R("#3a220a", "#7a4e18", "#c08a30", "#f2d282")
STEEL = R("#1e2230", "#3c4458", "#6a7894", "#aab8d0")
DIAL = R("#8a8270", "#c8c0a8", "#ece6d2", "#ffffff")
LENS = "#4af0ff"


def cog(cv, x, y, r, ramp, teeth=6, turn=0.0):
    m = ell_mask(x, y, r, r)
    for k in range(teeth):
        a = turn + k * 2 * math.pi / teeth
        m |= ell_mask(x + math.cos(a) * (r + 0.8), y + math.sin(a) * (r + 0.8), 0.9, 0.9)
    m -= ell_mask(x, y, max(0.6, r * 0.35), max(0.6, r * 0.35))
    cv.shade(m, ramp)


def chronarch(frame=0, cast=False, reversal=False):
    cv = Canvas()
    lens = LENS if not reversal else "#ff4ad8"
    swing = (-3, 0, 3)[frame % 3]
    # the pendulum he floats on
    line(cv, 16, 22, 16 + swing, 29, BRASS[1])
    cv.shade(ell_mask(16 + swing, 29.5, 2.4, 2), BRASS)
    # floating gears about him
    cog(cv, 4, 25, 2.4, BRASS, turn=frame * 0.5)
    cog(cv, 28, 26, 2, STEEL, teeth=5, turn=-frame * 0.6)
    # body: a cabinet of brass
    cv.shade(poly_mask([(9, 9), (23, 9), (24, 20), (20, 23), (12, 23), (8, 20)]), BRASS, grad=lambda x, y: 2 if x < 14 else 1)
    # the dial: a clock for a heart, its hands turning frame to frame
    cv.shade(ell_mask(16, 15.5, 5.6, 5.6), DIAL)
    glow_ring(cv, 16, 15.5, 5.6, BRASS[2], 255)
    for k in range(12):
        a = k * math.pi / 6
        cv.px(16 + math.cos(a) * 4.6, 15.5 + math.sin(a) * 4.6, BRASS[0] if k % 3 else "#000000")
    hour = (-math.pi / 2 + frame * math.pi / 6) * (-1 if reversal else 1)
    minute = (-math.pi / 2 + frame * 2 * math.pi / 3) * (-1 if reversal else 1)
    line(cv, 16, 15.5, 16 + math.cos(hour) * 2.6, 15.5 + math.sin(hour) * 2.6, "#1a1a1a")
    line(cv, 16, 15.5, 16 + math.cos(minute) * 4, 15.5 + math.sin(minute) * 4, "#b01818" if not reversal else "#1858d8")
    cv.px(16, 15, BRASS[3])
    # head: a dome with one great lens
    cv.shade(ell_mask(16, 5.5, 4, 3.8), BRASS)
    cv.shade(rect_mask(13, 8, 19, 9), STEEL)
    cv.shade(ell_mask(16, 5.5, 1.9, 1.9), STEEL)
    cv.px(16, 5, lens); cv.px(15, 5, lens); cv.px(16, 6, lens); cv.px(15, 4, "#ffffff")
    line(cv, 16, 1, 16, 0, BRASS[3])
    cv.px(16, 0, lens)
    # four arms, the upper pair holding a clock-hand blade each
    up = -3 if cast else 0
    thick(cv, 9, 11, 4, 7 + up, 2, STEEL)
    thick(cv, 23, 11, 28, 7 + up, 2, STEEL)
    thick(cv, 4, 7 + up, 2, 1 + up, 1.6, BRASS)
    thick(cv, 28, 7 + up, 30, 1 + up, 1.6, BRASS)
    cv.px(2, 1 + up, BRASS[3]); cv.px(30, 1 + up, BRASS[3])
    thick(cv, 9, 18, 5, 21, 2, STEEL)
    thick(cv, 23, 18, 27, 21, 2, STEEL)
    cog(cv, 5, 21, 1.4, BRASS, teeth=4)
    cog(cv, 27, 21, 1.4, BRASS, teeth=4)
    if cast:
        for (x, y) in ((2, -1), (30, -1), (1, 0), (31, 0)):
            sparkle(cv, x, y + 2, lens)
        glow_ring(cv, 16, 15.5, 6.6, lens, 140)
    return cv.img


# =================================================================================================
# Vaelith, the Crucible Sovereign

PLATE = R("#6e6a80", "#b4b0c4", "#e6e2f0", "#ffffff")
TRIM = GOLD
FLAME = R("#7a1a06", "#d8500e", "#ffa024", "#fff0a0")
FLAME_FAR = R("#4a1004", "#962e08", "#d8661a", "#ffbe5a")
VOID_FLAME = R("#24062e", "#5a0e7a", "#a828d8", "#f0a0ff")
VOID_FAR = R("#14041a", "#36084a", "#6a1890", "#b060e0")
CAPE = R("#3a0614", "#6e0e26", "#a01a36", "#d03a52")
SIGILS = ("#a4afc0", "#46c2b4", "#ff9a24", "#4af0ff")


def vaelith(frame=0, cast=False, ascended=False, unbound=False):
    cv = Canvas()
    flap = (-5, 0, 4)[frame % 3] if not cast else -8
    fl, ff = (VOID_FLAME, VOID_FAR) if unbound else (FLAME, FLAME_FAR)
    big = 2 if ascended or unbound else 0
    wing(cv, [(19, 12), (24, 6), (30, 1)], 70, -15, 10 + big, 8 + big, ff, ff, n=8, flap=-flap * .6)
    wing(cv, [(13, 12), (8, 5), (1, 0)], 100, 205, 12 + big, 9 + big, fl, fl, n=10, tip=frame, tipcol=fl[3], flap=flap)
    # cape
    cv.shade(poly_mask([(12, 12), (20, 12), (23, 30), (16, 28), (9, 30)]), CAPE, grad=lambda x, y: 2 if x < 15 else 1)
    # legs
    for lx in (12, 17):
        cv.shade(rect_mask(lx, 22, lx + 2, 30), PLATE)
        cv.px(lx + 1, 26, TRIM[2])
    # body
    cv.shade(poly_mask([(11, 11), (21, 11), (21, 18), (19, 23), (13, 23), (11, 18)]), PLATE, grad=lambda x, y: 2 if x < 16 else 1)
    for (x, y) in ((16, 13), (15, 14), (16, 14), (17, 14), (16, 15), (16, 16)):
        cv.px(x, y, TRIM[2] if not unbound else VOID_FLAME[2])
    for x in range(11, 22):
        cv.px(x, 19, TRIM[2] if x % 2 else TRIM[3])
    for sx in (11, 21):
        cv.shade(ell_mask(sx, 11.5, 2.6, 2), PLATE)
        cv.px(sx, 10, TRIM[3])
    # head: an open helm, a face of light
    cv.shade(ell_mask(16, 6.5, 3.2, 3.6), PLATE)
    cv.shade(rect_mask(15, 6, 17, 8), R("#e8b890", "#f4d0b0", "#ffe6cc", "#fff4e8"))
    eye = "#ffe68a" if not unbound else "#e040ff"
    cv.px(15, 6, eye); cv.px(17, 6, eye)
    # the halo of the four floors, crowned (broken, when unbound)
    for k in range(16):
        a = math.radians(k * 22.5 + frame * 8)
        if unbound and k in (3, 4, 11):
            continue
        x, y = 16 + math.cos(a) * 5.2, 2.2 + math.sin(a) * 1.6
        cv.px(x, y, SIGILS[k % 4] if not unbound else VOID_FLAME[2 + k % 2])
    # the glaive of white flame in his right hand
    if cast:
        line(cv, 22, 14, 29, 2, TRIM[1])
        for (x, y) in ((29, 2), (30, 1), (28, 1), (30, 3), (31, 0)):
            cv.px(x, y, fl[3])
        sparkle(cv, 30, 1, fl[2])
        orb(cv, 9, 16, 2.4, fl, glow=fl[2])
    else:
        line(cv, 24, 4, 24, 30, TRIM[1])
        line(cv, 25, 5, 25, 30, TRIM[2])
        blade = poly_mask([(23, 0), (26, 0), (27, 3), (24.5, 8), (22, 3)])
        cv.shade(blade, R("#c8c4e0", "#f0eefa", "#ffffff", "#ffffff") if not unbound else VOID_FLAME)
        cv.px(24, 2, fl[2])
        thick(cv, 21, 13, 24, 15, 2, PLATE)
        thick(cv, 11, 13, 9, 18, 2, PLATE)
    if ascended:
        glow_ring(cv, 16, 16, 13, fl[2], 50)
    return cv.img


def all_sprites():
    """name -> frames: three idle frames and an attack frame."""
    return {
        "Ferrogrim, the Iron Warden": [ferrogrim(0), ferrogrim(1), ferrogrim(2), ferrogrim(0, swing=True)],
        "Ferrogrim, Iron Tempest": [ferrogrim(f, tempest=True) for f in range(3)] + [ferrogrim(0, swing=True, tempest=True)],
        "Nerissa, the Tidecaller": [nerissa(0), nerissa(1), nerissa(2), nerissa(0, cast=True)],
        "Nerissa, Maelstrom": [nerissa(f, maelstrom=True) for f in range(3)] + [nerissa(0, cast=True, maelstrom=True)],
        "Pyrrhax, the Forge Tyrant": [pyrrhax(0), pyrrhax(1), pyrrhax(2), pyrrhax(1, slam=True)],
        "Pyrrhax, Molten Core": [pyrrhax(f, molten=True) for f in range(3)] + [pyrrhax(1, slam=True, molten=True)],
        "Chronarch, the Hour Thief": [chronarch(0), chronarch(1), chronarch(2), chronarch(1, cast=True)],
        "Chronarch, Reversal": [chronarch(f, reversal=True) for f in range(3)] + [chronarch(1, cast=True, reversal=True)],
        "Vaelith, the Crucible Sovereign": [vaelith(0), vaelith(1), vaelith(2), vaelith(1, cast=True)],
        "Vaelith, Ascendant": [vaelith(f, ascended=True) for f in range(3)] + [vaelith(1, cast=True, ascended=True)],
        "Vaelith, Unbound": [vaelith(f, unbound=True) for f in range(3)] + [vaelith(1, cast=True, unbound=True)],
    }


# the champions' echoes on the Apex, in the Sovereign's last trial: each a ghost of its colour
ECHO_TINT = {
    "Echo of Ferrogrim": ("Ferrogrim, the Iron Warden", (190, 200, 230)),
    "Echo of Nerissa": ("Nerissa, the Tidecaller", (120, 240, 220)),
    "Echo of Pyrrhax": ("Pyrrhax, the Forge Tyrant", (255, 170, 90)),
    "Echo of Chronarch": ("Chronarch, the Hour Thief", (120, 230, 255)),
}


def echoes(sprites):
    return {name: [spectral(f, rgb) for f in sprites[src]] for name, (src, rgb) in ECHO_TINT.items()}
