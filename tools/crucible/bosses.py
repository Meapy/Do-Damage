"""The Crucible's five champions at 32x32 (tools/build_crucible.py puts them on its sheets), one
for each floor of the tower:

- Ferrogrim, the Iron Warden: a hulking knight of riveted iron, a tower shield and a great hammer,
  the forge's glow in his visor;
- Nerissa, the Tidecaller: a sea-queen with a serpent's tail, a coral crown and a golden trident;
- Pyrrhax, the Forge Tyrant: a horned demon of obsidian split by molten veins, a hammer of lava;
- Chronarch, the Hour Thief: a hooded thief of time, a clock for a face, an hourglass for a heart,
  four arms with blades made of a clock's hands, an orrery's ring wheeling round him;
- Vaelith, the Crucible Sovereign: the tower's maker, a champion in white and gold with wings of
  flame, a crown and a ring of gold set with the four floors' gems.

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


PEARL = "#fff4ec"
WAVE = R("#0a3a4a", "#12627a", "#2a9ab0", "#7ad8e0")


def nerissa(frame=0, cast=False, maelstrom=False):
    """A sea-queen rising out of a cresting wave: a serpent's tail in an S, banded and scaled, a
    fin of coral at its tip; long dark hair streaming and moving with the sea; a crown of coral set
    with pearls, eyes lit with sea-light, a golden trident. The wave's foam breaks (frame). In her
    Maelstrom her hair whips up round her, her eyes burn white and a spiral of water turns round
    her."""
    cv = Canvas()
    f = frame % 3
    sway = (-1, 0, 1)[f]
    eye = "#ffffff" if maelstrom else "#7af8ff"
    if maelstrom:
        # the spiral of water turning round her, behind her
        for k in range(26):
            a = math.radians(k * 14 + f * 24)
            r = 10 + k * 0.18
            x, y = 16 + math.cos(a) * r * 1.35, 17 + math.sin(a) * r * 0.8
            cv.px(x, y, (FOAM, SCALE[3], WAVE[2])[k % 3], 230 - k * 4)
    # the wave she rises from: a crest curling in from the left, foam breaking along its lip
    wave = ell_mask(15.5, 28, 15, 4.2) | poly_mask([(1, 28), (3, 23), (8, 21), (12, 24), (6, 26)])
    cv.shade(wave, WAVE, grad=lambda x, y: 2 if y < 26 else 1, rim=False)
    curl = poly_mask([(0, 25), (2, 20), (6, 18), (9, 19), (8, 21), (5, 21), (3, 23)])
    cv.shade(curl, WAVE, grad=lambda x, y: 3 if y < 20 else 2, rim=False)
    for (x, y) in wave:
        if (x, y - 1) not in wave and (x + f) % 4 != 0:
            cv.px(x, y, FOAM if (x + f) % 3 else "#ffffff")
            cv.px(x, y + 1, WAVE[3], 200)
    for (x, y) in ((2, 19), (5, 17), (7, 18), (1, 21)):
        cv.px(x, y + (f == 1), FOAM)
    # the long hair, streaming back to the left (up round her in the Maelstrom)
    if maelstrom:
        hair = poly_mask([(12, 4), (19, 4), (22, 1 - sway), (24, 6), (20, 10), (14, 12), (8, 10), (6, 2 + sway), (10, 3)])
    else:
        hair = poly_mask([(12, 3), (19, 4), (19, 10), (15, 16 + sway), (10, 21), (5, 20 - sway), (4, 15), (7, 10), (9, 5)])
    cv.shade(hair, HAIR, grad=lambda x, y: 2 if x > 11 else 1)
    for (x, y) in ((8, 9), (7, 13), (6, 17), (10, 6), (11, 14)):
        if (x, y) in hair:
            cv.px(x, y + (f == 2), HAIR[3])
    # the torso, a shell bodice, a string of pearls, a band of gold at the waist
    torso = poly_mask([(13, 11), (19, 11), (20, 16), (19, 22), (13, 22), (12, 16)])
    cv.shade(torso, SKIN, grad=lambda x, y: 2 if x < 16 else 1)
    cv.shade(poly_mask([(13, 14), (19, 14), (19, 17), (16, 18), (13, 17)]), CORAL)
    for (x, y) in ((14, 15), (18, 15)):
        cv.px(x, y, CORAL[3])
    for x in range(13, 20):
        cv.px(x, 12 + (1 if x in (15, 16, 17) else 0), PEARL if x % 2 else SCALE_BELLY[3])
    for x in range(13, 20):
        cv.px(x, 21, GOLD[2] if x % 2 else GOLD[3])
    cv.px(16, 22, SCALE[3])
    # head: a face lit by its eyes, fins for ears
    cv.shade(ell_mask(16, 7.5, 3.3, 3.6), SKIN)
    for x in (15, 18):
        cv.px(x, 7, eye); cv.px(x, 8, SCALE[2])
    cv.px(17, 10, CORAL[1])
    for (x, y) in ((12, 6), (11, 7), (12, 8), (20, 6), (21, 7), (20, 8)):
        cv.px(x, y, SCALE[3])
    # the crown: branches of coral, a pearl at the front
    for (x, top) in ((13, 1), (14.5, -1), (16, 0), (17.5, -1), (19, 1)):
        line(cv, round(x), 4, round(x), top + 1, CORAL[2])
        cv.px(round(x), top + 1, CORAL[3])
    for x in range(13, 20):
        cv.px(x, 4, CORAL[1])
    cv.px(16, 3, PEARL); cv.px(16, 2, "#ffffff")
    # her left arm, the sea gathering in her hand
    thick(cv, 13, 13, 9, 18, 2, SKIN)
    orb(cv, 8, 19, 2.6 if cast else 1.6, SCALE, glow=SCALE[3])
    if cast:
        for k in range(6):
            a = math.radians(k * 60 + f * 30)
            cv.px(8 + math.cos(a) * 4, 19 + math.sin(a) * 3, FOAM, 220)
    # the trident in her right, its tines lit
    tx, top = (25, 0) if cast else (24, 2)
    thick(cv, 19, 13, tx - 1, 15 if not cast else 9, 2, SKIN)
    line(cv, tx, top + 3, tx, 30, GOLD[2])
    line(cv, tx + 1, top + 4, tx + 1, 30, GOLD[1])
    for dx in (-3, 0, 3):
        line(cv, tx + dx, top, tx + dx, top + 3, GOLD[3])
        cv.px(tx + dx, top - 1, "#bff4ff" if not cast else "#ffffff")
    line(cv, tx - 3, top + 3, tx + 3, top + 3, GOLD[2])
    cv.px(tx, top + 5, SCALE[3])
    if cast:
        for dx in (-3, 0, 3):
            sparkle(cv, tx + dx, top, SCALE[3])
    # the tail: out of the wave in an S, scaled, a pale band down its belly
    tail = [(12, 27), (16, 25), (20, 27), (24, 27), (27, 24 + sway), (28, 20 + sway)]
    for (a, b) in zip(tail, tail[1:]):
        thick(cv, a[0], a[1], b[0], b[1], 4.6 - tail.index(a) * 0.55, SCALE)
    for i, (x, y) in enumerate(tail[:-1]):
        cv.px(x, y + 1, SCALE_BELLY[2]); cv.px(x + 1, y + 1, SCALE_BELLY[3])
        cv.px(x - 1, y - 1, SCALE[3])
    # the fin at its tip, coral, see-through at its edge
    fin = poly_mask([(27, 21 + sway), (31, 14 + sway), (31, 20 + sway), (29, 23 + sway)])
    cv.shade(fin, CORAL, rim=False)
    for k in range(3):
        line(cv, 28, 21 + sway, 31, 15 + k * 2 + sway, CORAL[3], 200)
    # droplets thrown up off the wave
    for k, (x, y) in enumerate(((3, 14), (28, 27), (10, 26), (22, 19))):
        cv.px(x, y - (f + k) % 2, FOAM, 210)
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


CLOAK = R("#0e0a22", "#1e1640", "#33286a", "#54469a")
CLOAK_REV = R("#220a1e", "#40163a", "#6a2862", "#9a4690")
SAND = R("#7a5418", "#c08a30", "#f2c860", "#fff0b0")


def chronarch(frame=0, cast=False, reversal=False):
    """A thief of time: a hooded cloak of night over a brass frame, a clock for a face, an hourglass
    for a heart, its sand running (frame). Four arms: the upper pair carry blades made of a clock's
    hands, the lower pair turn gears. An orrery's ring wheels round him, its little worlds circling
    (frame), and a pendulum swings under his hem. In his Reversal the cloak and light turn magenta,
    the sand runs up and the clock runs backwards."""
    cv = Canvas()
    f = frame % 3
    back = -1 if reversal else 1
    light = "#ff4ad8" if reversal else LENS
    cloak = CLOAK_REV if reversal else CLOAK
    bob = (0, -1, 0)[f]
    # the orrery's ring behind him, tilted, and its little worlds
    ring = [(16 + math.cos(math.radians(a)) * 14, 18 + math.sin(math.radians(a)) * 3.4 - math.cos(math.radians(a)) * 1.5)
            for a in range(0, 360, 7)]
    for i, (x, y) in enumerate(ring):
        if y < 18.5 - (x - 16) * 0.1:
            cv.px(x, y, BRASS[1], 150)
    worlds = ((0, LAVA, 1.2), (120, SCALE, 1.0), (240, IRON, 0.9))
    for (a0, col, r) in worlds:
        a = math.radians(a0 + f * 40 * back)
        wx, wy = 16 + math.cos(a) * 14, 18 + math.sin(a) * 3.4 - math.cos(a) * 1.5
        if math.sin(a) < 0:
            cv.shade(ell_mask(wx, wy, r, r), col, rim=False)
    # the great gear turning behind his hood, like a halo
    cog(cv, 16, 7 + bob, 6.2, R("#120c06", "#2a1c0a", "#4a3414", "#6a4c1c"), teeth=10, turn=f * 0.21 * back)
    # the pendulum under his hem
    sw = (-3, 0, 3)[f] * back
    line(cv, 16, 24, 16 + sw, 29, BRASS[2])
    cv.shade(ell_mask(16 + sw, 29.5, 2.2, 1.9), BRASS)
    cv.px(15 + sw, 29, BRASS[3]); cv.px(16 + sw, 30, light)
    # the cloak: a hood and a long cloak, its hem torn into points that drift
    body = poly_mask([(11, 4 + bob), (21, 4 + bob), (23, 12), (25, 20), (26, 26), (6, 26), (7, 20), (9, 12)])
    cv.shade(body, cloak, grad=lambda x, y: 2 if x < 14 else 1)
    for k, hx in enumerate(range(6, 27, 3)):
        d = (k + f) % 3
        cv.shade(poly_mask([(hx, 25), (hx + 3, 25), (hx + 1.5, 28 + d)]), cloak, grad=lambda x, y: 1, rim=False)
    # stars in the cloak's folds
    for k, (x, y) in enumerate(((9, 20), (23, 18), (11, 24), (21, 23), (8, 15), (24, 25))):
        if (k + f) % 3:
            cv.px(x, y, cloak[3] if k % 2 else "#ffffff", 160 + 30 * ((k + f) % 3))
    # the hourglass heart: two bulbs of glass, the sand running from one to the other
    glass = poly_mask([(13, 12), (19, 12), (16.5, 17), (19, 22), (13, 22), (15.5, 17)])
    for (x, y) in glass:
        cv.px(x, y, "#5a7a9a" if not reversal else "#8a5a9a", 200)
    for y in range(12, 22):
        cv.px(13, y, BRASS[1]); cv.px(19, y, BRASS[0])
    cv.px(14, 13, "#ffffff", 200); cv.px(14, 19, "#ffffff", 160)
    level = (1, 2, 3)[f]
    top_y, bot_y = (14 + level, 21 - level) if not reversal else (17 - level, 18 + level)
    for (x, y) in glass:
        if (y < 17 and y >= top_y and not reversal) or (y > 17 and y >= bot_y and not reversal) or \
           (reversal and ((y < 17 and y <= top_y) or (y > 17 and y <= bot_y))):
            cv.px(x, y, SAND[2] if (x + y) % 3 else SAND[3])
    for y in range(16, 19):
        cv.px(16, y + (f % 2 if not reversal else -(f % 2)), SAND[3])
    for x in range(12, 21):
        cv.px(x, 11, BRASS[2 if x < 17 else 1]); cv.px(x, 22, BRASS[2 if x < 17 else 1])
    cv.px(12, 11, BRASS[3]); cv.px(20, 22, BRASS[0])
    # the hood, deep, and the clock in it for a face
    hood = poly_mask([(11.5, 3 + bob), (16, -0.5 + bob), (20.5, 3 + bob), (22, 9 + bob), (19, 12), (13, 12), (10, 9 + bob)])
    cv.shade(hood, cloak, grad=lambda x, y: 3 if x < 14 and y < 6 else 2)
    face = ell_mask(16, 6.8 + bob, 3.4, 3.4)
    for (x, y) in face:
        cv.px(x, y, "#05040c")
    cv.shade(ell_mask(16, 6.8 + bob, 2.8, 2.8), DIAL, rim=False)
    glow_ring(cv, 16, 6.8 + bob, 2.8, light, 120)
    for k in range(4):
        a = k * math.pi / 2
        cv.px(16 + math.cos(a) * 2.3, 6.8 + bob + math.sin(a) * 2.3, BRASS[0])
    hour = -math.pi / 2 + f * math.pi / 3 * back
    minute = -math.pi / 2 + f * 2 * math.pi / 3 * back
    line(cv, 16, 7 + bob, 16 + math.cos(hour) * 1.5, 7 + bob + math.sin(hour) * 1.5, "#1a1a1a")
    line(cv, 16, 7 + bob, 16 + math.cos(minute) * 2.4, 7 + bob + math.sin(minute) * 2.4, "#b01818" if not reversal else "#7a14d8")
    # two eyes lit in the dial
    cv.px(15, 6 + bob, light); cv.px(17, 6 + bob, light)
    cv.px(16, 7 + bob, BRASS[3])
    # four arms: brass, jointed
    up = -4 if cast else 0
    for d in (-1, 1):
        sx = 16 + d * 6
        ex, ey = 16 + d * 10, 9 + up
        thick(cv, sx, 12, ex, ey, 2, STEEL)
        cv.shade(ell_mask(sx, 12, 1.5, 1.5), BRASS)
        cv.shade(ell_mask(ex, ey, 1.3, 1.3), BRASS)
        # the blade: a clock's minute hand, long, with an arrowhead and a counterweight ring
        if cast:
            tx, ty = 16 + d * 3, -0.5
        else:
            tx, ty = 16 + d * 13, -1 + (f == 1)
        bx, by = ex - (tx - ex) * 0.3, ey - (ty - ey) * 0.3
        thick(cv, bx, by, tx, ty, 1.7, BRASS)
        line(cv, bx + (tx - bx) * 0.3, by + (ty - by) * 0.3, tx, ty, BRASS[3], 140)
        cv.px(bx, by, BRASS[3]); glow_ring(cv, bx, by, 0.8, BRASS[2], 220)
        a = math.atan2(ty - by, tx - bx)
        for s in (-1, 1):
            cv.px(tx - math.cos(a) * 2 + math.cos(a + s * math.pi / 2) * 1.2,
                  ty - math.sin(a) * 2 + math.sin(a + s * math.pi / 2) * 1.2, BRASS[3])
        cv.px(tx, ty, light)
        # the lower arm, its hand cradling the hourglass
        thick(cv, 16 + d * 7, 15, 16 + d * 5, 19, 1.6, STEEL)
        cv.shade(ell_mask(16 + d * 4.6, 19.5, 1.2, 1.2), BRASS)
    if cast:
        # the blades crossed overhead, time cracking where they meet
        sparkle(cv, 16, 0, light)
        for k in range(8):
            a = math.radians(k * 45 + f * 20)
            cv.px(16 + math.cos(a) * 4, 1 + math.sin(a) * 2.5, light, 200)
        glow_ring(cv, 16, 17, 6.5, light, 110)
    # the near half of the ring, in front of him, and the worlds on it
    for i, (x, y) in enumerate(ring):
        if not (y < 17.5 - (x - 16) * 0.14):
            cv.px(x, y, BRASS[2 + (i % 4 == 0)], 190)
    for (a0, col, r) in worlds:
        a = math.radians(a0 + f * 40 * back)
        wx, wy = 16 + math.cos(a) * 14, 18 + math.sin(a) * 3.4 - math.cos(a) * 1.5
        if math.sin(a) >= 0:
            cv.shade(ell_mask(wx, wy, r + 0.3, r + 0.3), col)
    return cv.img


# =================================================================================================
# Vaelith, the Crucible Sovereign

PLATE = R("#4a4660", "#8e8aa6", "#d0ccdf", "#ffffff")
TRIM = GOLD
FLAME = R("#7a1a06", "#d8500e", "#ffa024", "#fff0a0")
FLAME_FAR = R("#4a1004", "#962e08", "#d8661a", "#ffbe5a")
VOID_FLAME = R("#24062e", "#5a0e7a", "#a828d8", "#f0a0ff")
VOID_FAR = R("#14041a", "#36084a", "#6a1890", "#b060e0")
CAPE = R("#3a0614", "#6e0e26", "#a01a36", "#d03a52")
SIGILS = ("#a4afc0", "#46c2b4", "#ff9a24", "#4af0ff")


def flame_wing(cv, d, f, ramp, far, spread):
    """A wing of fire from the shoulder, its feathers flames, their tips flickering white-hot (frame)
    and embers lifting off it; spread raises it."""
    if d < 0:
        arm = [(13, 12), (8, 6 - spread * 0.15), (1, 1 - spread * 0.2)]
        wing(cv, arm, 100 + spread, 200 + spread * 0.5, 11 + spread * 0.15, 8, ramp, far, n=9, tip=f, tipcol=ramp[3], flap=0)
    else:
        arm = [(19, 12), (24, 6 - spread * 0.15), (31, 1 - spread * 0.2)]
        wing(cv, arm, 80 - spread, -20 - spread * 0.5, 11 + spread * 0.15, 8, ramp, far, n=9, tip=f + 1, tipcol=ramp[3], flap=0)
    for k in range(3):
        cv.px(16 + d * (9 + k * 5), 21 - k * 5 - ((f + k) % 3), ramp[3], 210)


def vaelith(frame=0, cast=False, ascended=False, unbound=False):
    """The tower's maker: a sovereign in white plate chased with gold, a crimson cape, wings of fire
    and a ring of gold behind him set with the four floors' gems, a crown, a visor burning gold, a
    glaive of white flame. The wings flicker (frame). Ascendant, the wings spread wider, the ring
    blazes and the four gems leave it to circle him. Unbound, the fire turns to the void, the plate
    cracks with violet light, the ring breaks and the cape tears."""
    cv = Canvas()
    f = frame % 3
    fl, ff = (VOID_FLAME, VOID_FAR) if unbound else (FLAME, FLAME_FAR)
    eye = "#e040ff" if unbound else "#ffe68a"
    bob = (0, -1, 0)[f]
    spread = (10 if ascended or unbound else 0) + (12 if cast else (0, 4, -3)[f])
    # the great ring behind him, gold, the four floors' gems set in it (broken, unbound)
    for k in range(40):
        a = math.radians(k * 9)
        if unbound and (k % 10) in (2, 3, 7):
            continue
        x, y = 16 + math.cos(a) * 9, 9 + bob + math.sin(a) * 9
        cv.px(x, y, TRIM[2 + (k % 5 == 0)] if not unbound else VOID_FAR[2], 220 if not ascended else 255)
        if ascended:
            cv.px(16 + math.cos(a) * 10, 9 + bob + math.sin(a) * 10, fl[2], 90)
    if not ascended and not unbound:
        for k, col in enumerate(SIGILS):
            a = math.radians(-135 + k * 90)
            gx, gy = 16 + math.cos(a) * 9, 9 + bob + math.sin(a) * 9
            cv.shade(ell_mask(gx, gy, 1.2, 1.2), R(col, col, col, "#ffffff"))
    # the wings of fire
    for d in (-1, 1):
        flame_wing(cv, d, f + (d > 0), fl, ff, spread)
    # the cape, falling behind him to the ground, torn when unbound
    cape = poly_mask([(11, 11), (21, 11), (24, 29), (16, 30), (8, 29)])
    cv.shade(cape, CAPE if not unbound else R("#1a0420", "#34082e", "#5a1040", "#7a1a50"), grad=lambda x, y: 2 if x < 13 else 1)
    if unbound:
        for (x, y) in ((10, 29), (13, 28), (19, 29), (22, 28), (16, 29), (9, 27)):
            cv.px(x, y, (0, 0, 0), 0)
            cv.img.putpixel((x, y), (0, 0, 0, 0))
    # greaves, under a skirt of plates
    for lx in (12, 17):
        cv.shade(rect_mask(lx, 23, lx + 2, 29), PLATE, grad=lambda x, y: 2 if x == lx else 1)
        cv.px(lx + 1, 25, TRIM[2]); cv.shade(rect_mask(lx - 1, 29, lx + 3, 30), PLATE)
    skirt = poly_mask([(12, 18), (20, 18), (22, 25), (10, 25)])
    cv.shade(skirt, PLATE, grad=lambda x, y: 2 if x < 16 else 1)
    for x in range(10, 23):
        cv.px(x, 24, TRIM[2] if x % 2 else TRIM[1])
    for x in (13, 16, 19):
        line(cv, x, 19, x - (16 - x) * 0.2, 23, PLATE[1])
    # the breastplate: gold chased round a sun of the four floors
    body = poly_mask([(12, 11 + bob), (20, 11 + bob), (20, 16), (18.5, 19), (13.5, 19), (12, 16)])
    cv.shade(body, PLATE, grad=lambda x, y: 2 if x < 14 else 1)
    for (x, y) in ((16, 13), (15, 14), (17, 14), (16, 15), (14, 14), (18, 14), (16, 12), (16, 16)):
        cv.px(x, y + bob * (y < 14), TRIM[2] if (x + y) % 2 else TRIM[3])
    cv.px(16, 14, fl[3] if not unbound else VOID_FLAME[3])
    for x in range(12, 21):
        cv.px(x, 18, TRIM[2] if x % 2 else TRIM[3])
    if unbound:
        # the plate cracked, violet light in it
        for (x, y) in ((12, 13), (13, 14), (13, 15), (14, 16), (19, 12), (19, 13), (18, 14), (20, 16), (19, 17)):
            cv.px(x, y, VOID_FLAME[2 + (x + f) % 2])
    # pauldrons, layered, edged in gold
    for d in (-1, 1):
        px_ = 16 + d * 5
        cv.shade(ell_mask(px_, 11.5 + bob, 2.8, 2.2), PLATE, grad=lambda x, y: 2 if y < 11 else 1)
        cv.shade(ell_mask(px_ + d * 0.8, 13.5 + bob, 2, 1.4), PLATE, grad=lambda x, y: 1)
        for k in range(-1, 2):
            cv.px(px_ + k, 13 + bob + abs(k) * 0.3, TRIM[2])
        cv.px(px_ - d, 10 + bob, "#ffffff")
    # the helm: a crowned great helm, a burning visor
    helm = poly_mask([(13.5, 3 + bob), (18.5, 3 + bob), (19.5, 7 + bob), (18, 10.5 + bob), (16, 11.5 + bob), (14, 10.5 + bob), (12.5, 7 + bob)])
    cv.shade(helm, PLATE, grad=lambda x, y: 2 if x < 18 else 1)
    for x in range(14, 19):
        cv.px(x, 6 + bob, eye if x != 16 else PLATE[0])
    for y in range(7, 10):
        cv.px(16, y + bob, eye if y == 7 else PLATE[0])
    cv.px(14, 6 + bob, "#ffffff"); cv.px(18, 6 + bob, "#ffffff")
    for (x, y) in ((13, 5), (19, 5), (12, 4), (20, 4)):
        cv.px(x, y + bob, TRIM[2])
    # the crown: five points of gold, each with a floor's gem (and the Apex's white)
    for k, x in enumerate((13.5, 15, 16, 17, 18.5)):
        h = (2, 3, 4, 3, 2)[k]
        line(cv, round(x), 3 + bob, round(x), 3 + bob - h, TRIM[2])
        cv.px(round(x), 3 + bob - h, (SIGILS[0], SIGILS[1], "#ffffff", SIGILS[2], SIGILS[3])[k])
    for x in range(14, 19):
        cv.px(x, 3 + bob, TRIM[3] if x % 2 else TRIM[2])
    if ascended:
        # the four gems off the ring, circling him
        for k, col in enumerate(SIGILS):
            a = math.radians(k * 90 + f * 30)
            gx, gy = 16 + math.cos(a) * 13.5, 17 + math.sin(a) * 5
            if math.sin(a) > -0.2:
                orb(cv, gx, gy, 1.2, R(col, col, col, "#ffffff"), glow=col)
            else:
                cv.px(gx, gy, col, 160)
    # the glaive of white flame
    blade_r = R("#c8c4e0", "#f0eefa", "#ffffff", "#ffffff") if not unbound else VOID_FLAME
    if cast:
        # raised high in his right hand, fire pouring off the blade; the left hand calls fire
        thick(cv, 21, 13 + bob, 25, 6, 2, PLATE)
        line(cv, 26, 14, 26, 0, TRIM[1])
        line(cv, 27, 14, 27, 1, TRIM[2])
        cv.shade(poly_mask([(25, -1), (29, -1), (28, 3), (26.5, 6), (25, 3)]), blade_r)
        sparkle(cv, 27, 1, fl[2], big=True)
        for k in range(4):
            cv.px(24 - k, 2 + k * 2 + f % 2, fl[3 - k % 2], 220 - k * 30)
        thick(cv, 11, 13 + bob, 8, 18, 2, PLATE)
        orb(cv, 7, 19, 2.6, fl, glow=fl[2])
    else:
        # held upright at his side, its blade a tongue of white flame
        line(cv, 25, 5, 25, 30, TRIM[1])
        line(cv, 26, 6, 26, 30, TRIM[2])
        cv.shade(poly_mask([(24, 0), (27.5, 0), (28, 3), (25.5, 8), (23, 3)]), blade_r)
        cv.px(25, 2 + f % 2, fl[2]); cv.px(26, 4, fl[3])
        for x in (24, 27):
            cv.px(x, 8, TRIM[3])
        thick(cv, 21, 13 + bob, 24, 16, 2, PLATE)
        cv.shade(ell_mask(25, 16.5, 1.5, 1.5), TRIM)
        thick(cv, 11, 13 + bob, 9, 19, 2, PLATE)
        cv.shade(ell_mask(9, 19.5, 1.5, 1.5), PLATE)
    if unbound:
        # shards of the broken ring drifting
        for k, (x, y) in enumerate(((4, 4), (28, 9), (6, 16), (3, 24), (29, 22))):
            cv.px(x + (f + k) % 2, y - (f + k) % 3, TRIM[2] if k % 2 else VOID_FLAME[3], 230)
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
