"""The Crucible's smaller art (tools/build_crucible.py puts it on its sheets): the floor of each of
the tower's floors (8x8 tiles, several variants each), its minions (16x16), what stands on its
floors (pillars, walls, braziers, coral, pylons, obelisks; 16x16), the ascension sigil (16x16,
animated), the portal (32x32, animated), and the shots (8x8, pointing right).

Light from the upper left, as on the game's other sprites; the client draws the black outline
round characters and shots."""
import math
import os
import random
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from black_between.bosses import Canvas, R, poly_mask, ell_mask, sparkle, orb  # noqa: E402
from black_between.small import put  # noqa: E402
from crucible.bosses import (IRON, IRON_DARK, EMBER, SCALE, CORAL, GOLD, OBSIDIAN, LAVA, BRASS, STEEL,  # noqa: E402
                             PLATE, FLAME, SIGILS, WAVE, ROCK, DIAL, VOID_FLAME, cog, thick, line, rect_mask,
                             glow_ring)


def _tile(fn, seed):
    rnd = random.Random(seed * 7919 + 13)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    fn(img, rnd)
    return img


def stone(base, dark, light, fleck=None, seam=None, flecks=1):
    """A floor of dressed stone: mottled, a seam on some edges, a fleck now and then."""
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    v = rnd.random()
                    put(img, x, y, base if v > 0.22 else (dark if v > 0.08 else light))
            s = seam or dark
            if seed % 2:
                for y in range(8):
                    if rnd.random() < 0.85:
                        put(img, 0, y, s)
            if seed % 3 == 1:
                for x in range(rnd.randrange(3, 8)):
                    put(img, x, 0, s)
            if fleck and seed % 2 == 0:
                for _ in range(flecks):
                    put(img, rnd.randrange(1, 7), rnd.randrange(1, 7), fleck)
        return _tile(f, seed)
    return make


def liquid(cols, glint, wave=2):
    """Water or lava: bands that wave across the tile, glints of light."""
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    k = int((math.sin((x + seed * 3) * 0.9 + y * 0.5) + 1) * wave + y * 0.15 + rnd.random() * 0.8) % len(cols)
                    put(img, x, y, cols[k])
            for _ in range(2):
                put(img, rnd.randrange(8), rnd.randrange(8), glint)
        return _tile(f, seed)
    return make


def pattern(cols, fn, fleck=None):
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    put(img, x, y, cols[fn(x, y, seed) % len(cols)])
            if fleck:
                put(img, rnd.randrange(8), rnd.randrange(8), fleck)
        return _tile(f, seed)
    return make


def cracked(base, crack, glow):
    """Floor about to break away: its own stone split by bright cracks."""
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    put(img, x, y, base if rnd.random() > 0.15 else crack)
            x, y = rnd.randrange(8), 0
            while y < 8:
                put(img, x, y, glow)
                x = max(0, min(7, x + rnd.choice((-1, 0, 1))))
                y += 1
            put(img, rnd.randrange(8), rnd.randrange(8), glow)
        return _tile(f, seed)
    return make


# name -> (art, variants)
TILES = {
    # the Threshold, where a climb begins: worn grey stone and old gold
    "Threshold Stone": (stone("#3a3640", "#322e38", "#46424e", fleck="#a88a3a"), 4),
    "Threshold Rim": (stone("#4e4858", "#443e4c", "#5a5466", fleck="#d8b040", flecks=2), 3),
    # I. the Iron Bastion: iron-grey flagstones riveted at the seams
    "Bastion Flagstone": (stone("#3c414c", "#343842", "#474d5a", fleck="#7a8496", seam="#262a32"), 4),
    "Bastion Rim": (pattern(["#565e6e", "#4a5160", "#606a7c"], lambda x, y, s: (x * 3 + y + s) % 5 // 2, fleck="#a4afc0"), 3),
    "Bastion Inlay": (pattern(["#6a3a1a", "#8a4a1e", "#5a2e14"], lambda x, y, s: (x + y) % 3, fleck="#ff9a2a"), 2),
    # his grinding blades and burning grates: walkable, but they cut
    "Grinding Blade": (pattern(["#c4482c", "#ff9a2a", "#7a1a10", "#e8e2d8"], lambda x, y, s: ((x - y + s) % 4 == 0) * 3 + ((x + y) % 2), fleck="#ffe29a"), 3),
    "Molten Grate": (pattern(["#1c2029", "#ff9a2a", "#d8600e"], lambda x, y, s: 0 if x % 3 == 0 or y % 3 == 0 else 1 + (x + y + s) % 2), 2),
    # II. the Drowned Sanctum: sea-glass mosaic, coral islands, the tide
    "Sanctum Mosaic": (pattern(["#1a4a52", "#1f5a60", "#246a6e", "#163e48"], lambda x, y, s: ((x // 2 + y // 2 + s) % 3) + (1 if (x + y) % 4 == 0 else 0), fleck="#7ae0d0"), 4),
    "Sanctum Rim": (stone("#2c6a6a", "#245c5c", "#347878", fleck="#d8a428", flecks=2), 3),
    "Coral Isle": (stone("#7a4a5a", "#6a3e4e", "#8a5868", fleck="#ffa8a0", flecks=2), 3),
    "Sanctum Shallows": (liquid(["#2a8a96", "#2e96a2", "#3aa6b0"], "#bff4ff", 1), 3),
    # the flood: deep water slows and drags, the surging tide crushes
    "Deep Tide": (liquid(["#0c3a5a", "#0e4466", "#124e74", "#0a3050"], "#5ab4e0"), 4),
    "Surging Tide": (liquid(["#3ab0d0", "#5ac8e0", "#e8fbff", "#2a8ab0"], "#ffffff", 3), 3),
    # III. the Ember Foundry: dark iron plate, slag, lava
    "Foundry Plate": (stone("#2e2624", "#262020", "#3a302c", fleck="#e0400c", seam="#1a1414"), 4),
    "Foundry Rim": (pattern(["#4a3a30", "#3e3028", "#56443a"], lambda x, y, s: (x + 2 * y + s) % 3, fleck="#ff9a24"), 3),
    "Forge Lava": (liquid(["#e0400c", "#ff6a14", "#ff9a24", "#c02e08"], "#fff2a6"), 4),
    "Cooling Slag": (stone("#4a2418", "#3a1c14", "#6a3018", fleck="#ff6a14", flecks=3), 3),
    # IV. the Clockwork Orrery: a dial of ivory and brass
    "Orrery Dial": (stone("#3a3248", "#332c40", "#443a54", fleck="#c08a30", seam="#2a2436"), 4),
    "Orrery Rim": (pattern(["#7a4e18", "#c08a30", "#5a3a12"], lambda x, y, s: (x + s) % 3, fleck="#f2d282"), 3),
    "Dial Numeral": (pattern(["#c08a30", "#f2d282", "#7a4e18"], lambda x, y, s: 1 if (x in (2, 5) and 1 <= y <= 6) else (0 if (x + y) % 2 else 2)), 1),
    "Chime Light": (pattern(["#c08a30", "#f2d282", "#fff8d0", "#4af0ff"], lambda x, y, s: ((x - y + s) % 4 == 0) * 3 + (x + y) % 3), 3),
    "Hour Hand": (pattern(["#1a1a24", "#b01818", "#e83a2a", "#ffd0a0"], lambda x, y, s: 1 + (x + y + s) % 3 if (x + y) % 5 else 0), 3),
    # V. the Apex: white marble veined with gold, the Sovereign's flame
    "Apex Marble": (stone("#c8c4d4", "#b8b4c6", "#d8d4e2", fleck="#d8a428", seam="#a4a0b4"), 4),
    "Apex Rim": (pattern(["#d8a428", "#ffe68a", "#9a6a14"], lambda x, y, s: (x * 2 + y + s) % 3, fleck="#ffffff"), 3),
    "Apex Sigil": (pattern(["#ffe68a", "#d8a428", "#fff8e0"], lambda x, y, s: (x + y + s) % 3), 2),
    "Sovereign Flame": (liquid(["#ff9a24", "#ffd04a", "#fff0a0", "#e0600e"], "#ffffff", 3), 3),
    # where the floor is about to break away into the sky (any floor)
    "Crumbling Floor": (cracked("#3a3036", "#2a2228", "#ffb03a"), 3),
    # never on the map: the rock the client hangs under each floor's edges
    "Crucible Underside Rock": (stone("#2a2230", "#221a28", "#342a3c", fleck="#ff9a3a"), 4),
    "Crucible Underside Rim": (pattern(["#4a3e50", "#3e3446", "#d8a428"], lambda x, y, s: 2 if (x * 3 + y + s) % 13 == 0 else (0 if y < 6 else 1)), 2),
}


def tiles():
    return {name: [fn(seed) for seed in range(n)] for name, (fn, n) in TILES.items()}


# ------------------------------------------------------------------------------------------------
# minions, 16x16: (stand, stand 2, attack)

def sentinel(frame=0, fire=False):
    """An Iron Sentinel: a squat turret of riveted iron, a burning eye in its slit."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(3, 11, 12, 15), IRON_DARK)
    cv.shade(ell_mask(7.5, 8.5, 5, 4.5), IRON)
    cv.shade(rect_mask(11, 7, 15, 9), IRON_DARK)
    for x in range(4, 11):
        cv.px(x, 8, EMBER[2] if x in (7, 8) else EMBER[1])
    cv.px(7 + frame % 2, 8, EMBER[3])
    for (x, y) in ((4, 12), (11, 12), (7, 5)):
        cv.px(x, y, IRON[3])
    if fire:
        sparkle(cv, 15, 8, EMBER[2])
    return cv.img


def banner(frame=0, fire=False):
    """A Bastion Banner: his standard on an iron pole, its cloth stirring."""
    cv = Canvas(16, 16)
    line(cv, 4, 1, 4, 15, IRON[1])
    line(cv, 5, 1, 5, 15, IRON[2])
    cv.px(4, 0, EMBER[3]); cv.px(5, 0, EMBER[2])
    wave = frame % 2
    cloth = poly_mask([(6, 2), (14, 2 + wave), (13, 7), (14, 12 - wave), (6, 11)])
    cv.shade(cloth, R("#3a0e0e", "#6a1a16", "#962a20", "#c4482c"))
    for (x, y) in ((9, 5), (10, 6), (9, 7), (10, 7), (11, 7), (10, 8)):
        cv.px(x, y, EMBER[2] if not fire else EMBER[3])
    return cv.img


def eel(frame=0, bite=False):
    """A Riptide Eel: a sinuous teal eel with a coral fin, jaws wide when it bites."""
    cv = Canvas(16, 16)
    ph = frame * 1.3
    pts = [(2 + i, 9 + math.sin(i * 0.7 + ph) * 2.2) for i in range(12)]
    for i, (x, y) in enumerate(pts):
        cv.shade(ell_mask(x, y, 1.6 if i < 10 else 2.2, 1.6 if i < 10 else 2), SCALE)
    hx, hy = pts[-1]
    cv.shade(ell_mask(hx + 1.5, hy, 2.6, 2.2), SCALE)
    cv.px(hx + 2, hy - 1, "#ffe68a")
    if bite:
        for k in range(3):
            cv.px(hx + 3 + k, hy + 1 + k // 2, "#ffffff")
            cv.px(hx + 3 + k, hy - 2 - k // 2, "#ffffff")
    for i in range(2, 10, 2):
        cv.px(pts[i][0], pts[i][1] - 2, CORAL[2])
    return cv.img


def golem(frame=0, slam=False):
    """A Molten Golem: a lump of slag that walks, lava showing through its cracks."""
    cv = Canvas(16, 16)
    hot = LAVA[3] if frame % 2 else LAVA[2]
    for lx in (4, 9):
        cv.shade(rect_mask(lx, 12, lx + 2, 15 - (frame % 2 if lx == 4 else 0)), OBSIDIAN)
    cv.shade(ell_mask(8, 8, 5.5, 5), OBSIDIAN)
    for (x0, y0, x1, y1) in ((5, 5, 8, 9), (8, 9, 11, 7), (8, 9, 7, 12)):
        line(cv, x0, y0, x1, y1, LAVA[1])
    cv.px(8, 9, hot)
    cv.px(6, 6, hot); cv.px(10, 6, hot)
    up = -3 if slam else 0
    cv.shade(ell_mask(2, 9 + up, 2, 2), OBSIDIAN)
    cv.shade(ell_mask(14, 9 + up, 2, 2), OBSIDIAN)
    if slam:
        cv.px(2, 5, hot); cv.px(14, 5, hot)
    return cv.img


def sentry(frame=0, fire=False):
    """A Clockwork Sentry: a brass beetle on four legs, a cog on its back, a lens for an eye."""
    cv = Canvas(16, 16)
    for k, lx in enumerate((3, 6, 10, 13)):
        off = (frame + k) % 2
        line(cv, 8, 10, lx, 14 - off, STEEL[2])
    cv.shade(ell_mask(8, 9, 5.5, 3.6), BRASS)
    cog(cv, 8, 6, 2.4, STEEL, teeth=6, turn=frame * 0.5)
    cv.shade(ell_mask(13, 10, 2, 1.8), BRASS)
    cv.px(14, 10, "#4af0ff" if not fire else "#ffffff")
    if fire:
        sparkle(cv, 15, 10, "#4af0ff")
    return cv.img


# the rushers: small, quick things each champion sends at the players now and then, to be killed
# before they reach you (BehaviorDb.Crucible.cs, Rusher)

def hound(frame=0, bite=False):
    """An Iron Hound: a low hound of riveted iron, ember eyes, legs at full stretch."""
    cv = Canvas(16, 16)
    stride = frame % 2
    for (lx, back) in ((4, True), (6, False), (10, True), (12, False)):
        reach = (1 if back else -1) * (1 if stride else -1)
        line(cv, lx, 10, lx + reach, 14, IRON_DARK[1])
    cv.shade(ell_mask(8, 9, 5, 2.6), IRON)
    cv.shade(ell_mask(13, 7.5, 2.4, 2), IRON)
    cv.px(14, 7, EMBER[3])
    line(cv, 3, 8, 1, 6 + stride, IRON[2])
    for x in (6, 8, 10):
        cv.px(x, 7, IRON[3])
    if bite:
        for k in range(3):
            cv.px(15, 8 + k % 2, "#ffffff")
        sparkle(cv, 15, 9, EMBER[2])
    return cv.img


def darter(frame=0, bite=False):
    """A Brine Darter: a sleek teal fish with coral fins, darting through the air as through water."""
    cv = Canvas(16, 16)
    sway = 1 if frame % 2 else 0
    cv.shade(poly_mask([(1, 6 + sway), (4, 8), (1, 11 - sway)]), CORAL)
    cv.shade(ell_mask(8.5, 8.5, 5.5, 2.8), SCALE)
    cv.shade(poly_mask([(6, 6), (9, 3 + sway), (10, 6)]), CORAL)
    cv.shade(poly_mask([(7, 11), (9, 13 - sway), (10, 11)]), CORAL)
    cv.px(12, 7, "#ffe68a")
    for x in (6, 8, 10):
        cv.px(x, 9, SCALE[3])
    if bite:
        for k in range(2):
            cv.px(14, 8 + k, "#ffffff")
        sparkle(cv, 15, 8, "#bff4ff")
    return cv.img


def imp(frame=0, burst=False):
    """A Cinder Imp: a little horned imp of obsidian, lava in its belly, ash wings beating."""
    cv = Canvas(16, 16)
    flap = frame % 2
    cv.shade(poly_mask([(6, 7), (1, 3 + 2 * flap), (2, 9)]), OBSIDIAN)
    cv.shade(poly_mask([(10, 7), (15, 3 + 2 * flap), (14, 9)]), OBSIDIAN)
    cv.shade(ell_mask(8, 9, 3, 3.4), OBSIDIAN)
    cv.shade(ell_mask(8, 5, 2.4, 2.2), OBSIDIAN)
    cv.px(6, 2, LAVA[2]); cv.px(10, 2, LAVA[2])
    cv.px(7, 5, LAVA[3]); cv.px(9, 5, LAVA[3])
    hot = LAVA[3] if burst else LAVA[2]
    for (x, y) in ((8, 9), (7, 10), (9, 10), (8, 11)):
        cv.px(x, y, hot)
    line(cv, 7, 12, 6, 15, OBSIDIAN[1]); line(cv, 9, 12, 10, 15, OBSIDIAN[1])
    if burst:
        sparkle(cv, 8, 9, LAVA[3])
    return cv.img


def mite(frame=0, bite=False):
    """A Clockwork Mite: a tiny brass spider, a turning cog for a body, a cyan lens."""
    cv = Canvas(16, 16)
    for k, (lx, ly) in enumerate(((2, 6), (2, 13), (14, 6), (14, 13), (4, 3), (12, 3))):
        off = (frame + k) % 2
        line(cv, 8, 9, lx, ly + off, STEEL[2])
    cog(cv, 8, 9, 3.4, BRASS, teeth=8, turn=frame * 0.4)
    cv.shade(ell_mask(8, 9, 1.4, 1.4), STEEL)
    cv.px(8, 9, "#4af0ff" if not bite else "#ffffff")
    if bite:
        sparkle(cv, 8, 9, "#4af0ff")
    return cv.img


def wisp(frame=0, flare=False):
    """A Gilded Wisp: a teardrop of the Sovereign's gold flame, a white heart, a trailing tail."""
    cv = Canvas(16, 16)
    lick = frame % 2
    cv.shade(poly_mask([(3, 8), (9, 4), (14, 8), (9, 12)]), FLAME)
    cv.shade(poly_mask([(1, 6 + lick), (5, 8), (1, 10 - lick)]), FLAME)
    cv.shade(ell_mask(10, 8, 2.6, 2.6), GOLD)
    cv.px(10, 8, "#ffffff"); cv.px(11, 7, "#ffffff")
    if flare:
        sparkle(cv, 10, 8, "#fff8e0")
    return cv.img


def minions():
    """name -> (stand frames, attack)."""
    return {
        "Iron Sentinel": ([sentinel(0), sentinel(1)], sentinel(0, True)),
        "Bastion Banner": ([banner(0), banner(1)], banner(0, True)),
        "Riptide Eel": ([eel(0), eel(1), eel(2)], eel(1, True)),
        "Molten Golem": ([golem(0), golem(1)], golem(0, True)),
        "Clockwork Sentry": ([sentry(0), sentry(1)], sentry(0, True)),
        "Iron Hound": ([hound(0), hound(1)], hound(0, True)),
        "Brine Darter": ([darter(0), darter(1)], darter(0, True)),
        "Cinder Imp": ([imp(0), imp(1)], imp(0, True)),
        "Clockwork Mite": ([mite(0), mite(1)], mite(0, True)),
        "Gilded Wisp": ([wisp(0), wisp(1)], wisp(0, True)),
    }


# ------------------------------------------------------------------------------------------------
# what stands on the floors, 16x16

def pillar(theme, frame=0):
    """A pillar of each floor: iron with a burning cap, a coral spire, a brazier, a brass orrery
    pylon with its globe turning, a marble obelisk of the four colours."""
    cv = Canvas(16, 16)
    if theme == "iron":
        cv.shade(rect_mask(4, 4, 11, 15), IRON, grad=lambda x, y: 2 if x < 7 else 1)
        cv.shade(rect_mask(3, 2, 12, 4), IRON_DARK)
        for y in (7, 11):
            for x in (5, 10):
                cv.px(x, y, IRON[3])
        cv.px(7, 1, EMBER[2 + frame % 2]); cv.px(8, 1, EMBER[2]); cv.px(7, 0, EMBER[3] if frame % 2 else EMBER[2])
    elif theme == "coral":
        for (x0, top, w) in ((7, 0, 2.6), (4, 5, 1.8), (11, 4, 1.8)):
            cv.shade(poly_mask([(x0 - w, 15), (x0 + w, 15), (x0 + 0.6, top), (x0 - 0.6, top)]), CORAL)
        for (x, y) in ((7, 0), (4, 5), (11, 4)):
            cv.px(x, y, "#ffe0d8")
        for k in range(3):
            cv.px(2 + k * 5, 13 - (frame + k) % 3, "#e8fbff", 200)
    elif theme == "forge":
        cv.shade(poly_mask([(3, 9), (12, 9), (10, 15), (5, 15)]), OBSIDIAN)
        cv.shade(rect_mask(2, 8, 13, 9), R("#2a2420", "#544a40", "#8a7a66", "#c4b49a"))
        for k in range(5):
            h = 3 + ((k * 3 + frame) % 4)
            line(cv, 4 + k * 2, 8, 4 + k * 2, 8 - h, LAVA[1 + (k + frame) % 3])
        cv.px(7, 2 + frame % 2, LAVA[3])
    elif theme == "orrery":
        line(cv, 7, 6, 7, 15, BRASS[1]); line(cv, 8, 6, 8, 15, BRASS[2])
        cv.shade(rect_mask(4, 14, 11, 15), BRASS)
        cv.shade(ell_mask(7.5, 4, 3, 3), STEEL)
        a = frame * math.pi / 2
        for k in range(2):
            b = a + k * math.pi
            cv.px(7.5 + math.cos(b) * 5, 4 + math.sin(b) * 1.6, "#4af0ff")
        cv.px(6, 3, "#ffffff")
    else:  # apex: an obelisk banded in the four floors' colours
        cv.shade(poly_mask([(5, 15), (10, 15), (9, 2), (6, 2)]), PLATE)
        cv.shade(poly_mask([(6, 2), (9, 2), (7.5, 0)]), GOLD)
        for k, c in enumerate(SIGILS):
            for x in range(6, 10):
                cv.px(x, 5 + k * 2, c)
        cv.px(7, 0, "#ffffff" if frame % 2 else GOLD[3])
    return cv.img


def grate(frame=0):
    """A Bastion Grate: an iron portcullis risen from the floor, molten at its foot."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            bar = x % 4 in (1, 2) or y % 5 == 1
            if bar:
                c = IRON[3] if x % 4 == 1 and not y % 5 == 1 else (IRON[2] if y % 5 != 1 else IRON[1])
                put(img, x, y, c)
            elif y >= 13:
                put(img, x, y, EMBER[1 + (x + frame) % 3])
    for x in range(1, 16, 4):
        put(img, x, 0, EMBER[3] if (x // 4 + frame) % 2 else EMBER[2])
    return img


def brazier(frame=0):
    """A brazier of the Threshold, old gold, its fire flickering."""
    cv = Canvas(16, 16)
    line(cv, 7, 8, 7, 15, GOLD[1]); line(cv, 8, 8, 8, 15, GOLD[2])
    cv.shade(rect_mask(5, 14, 10, 15), GOLD)
    cv.shade(poly_mask([(3, 6), (12, 6), (10, 9), (5, 9)]), GOLD)
    for k in range(4):
        h = 2 + (k + frame) % 3
        line(cv, 5 + k * 2, 6, 5 + k * 2, 6 - h, FLAME[1 + (k + frame) % 3])
    cv.px(7, 1 + frame % 2, FLAME[3])
    return cv.img


def _mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def _ramp(cols, t):
    """A colour along a ramp of colours, t from 0 to 1."""
    t = max(0.0, min(0.999, t)) * (len(cols) - 1)
    i = int(t)
    return _mix(cols[i], cols[i + 1], t - i)


PORTAL_FRAMES = 8
_FIRE = [(40, 6, 4), (110, 16, 6), (200, 52, 10), (255, 140, 30), (255, 214, 110), (255, 248, 220)]
_RIM = [(18, 14, 16), (40, 32, 34), (70, 56, 54), (104, 86, 78), (150, 128, 110)]
_GEMS = [(164, 175, 192), (70, 194, 180), (255, 154, 36), (74, 240, 255)]


def portal(frame=0):
    """The Crucible Portal, sunk into the Nexus's floor across Realm 2's vortex from the Arena's pit (32x32, four tiles
    across, drawn flat): a crucible's iron lip chased with gold, a gem of each of the four floors
    at its quarters lighting in turn; inside it a channel of molten gold running round, a ring of
    the tower's runes turning the other way, and a vortex of fire spiralling down to a white-hot
    heart. Eight frames."""
    s = 32
    c = (s - 1) / 2
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    turn = frame / PORTAL_FRAMES * 2 * math.pi
    lit = (frame // 2) % 4
    for y in range(s):
        for x in range(s):
            dx, dy = x - c, y - c
            r = math.hypot(dx, dy)
            if r > 15.6:
                continue
            ang = math.atan2(dy, dx)
            light = -(dx + dy) / (r + 1e-9)          # lit from the upper left
            if r > 13.0:
                # the iron lip, bevelled, a line of gold set in it
                t = 0.45 + 0.3 * light * (1 if r > 14.2 else -1) - (0.25 if r > 15.0 else 0)
                col = _ramp(_RIM, t)
                if 13.9 < r < 14.6 and int((ang + math.pi) / (2 * math.pi) * 48) % 6:
                    col = _ramp([GOLD[1], GOLD[2], GOLD[3]], 0.5 + 0.5 * light)
                put(img, x, y, col)
            elif r > 11.6:
                # the channel of molten gold, running round
                wave = math.sin(ang * 6 - turn * 3 + r)
                put(img, x, y, _ramp(_FIRE, 0.62 + 0.2 * wave + (0.1 if r < 12.3 else 0)))
            elif r > 9.4:
                # the basin's dark stone and the tower's runes in it, turning back
                rune = math.cos((ang + turn / 2) * 10)
                if rune > 0.82 and 9.9 < r < 11.1:
                    put(img, x, y, _ramp([GOLD[1], GOLD[3], (255, 250, 220)], 0.4 + 0.6 * (rune - 0.82) / 0.18))
                else:
                    put(img, x, y, _ramp(_RIM, 0.12 + 0.12 * (11.6 - r) / 2 - 0.08 * light))
            else:
                # the vortex: two arms of fire spiralling down to the heart
                arm = math.sin(2 * ang + r * 0.95 - turn * 2)
                heat = 1 - r / 9.4
                t = 0.18 + 0.55 * heat + 0.28 * arm
                if r < 2.4:
                    t = 0.92 + 0.08 * math.cos(turn * 2)
                put(img, x, y, _ramp(_FIRE, t))
    # the four floors' gems at the quarters, in gold settings, lighting one after another
    for k in range(4):
        a = -math.pi / 2 + k * math.pi / 2
        gx, gy = c + math.cos(a) * 13.3, c + math.sin(a) * 13.3
        if k == lit:
            for (px_, py_) in ell_mask(gx + 0.5, gy + 0.5, 3.4, 3.4):
                old = img.getpixel((int(px_), int(py_))) if 0 <= px_ < s and 0 <= py_ < s else (0, 0, 0, 0)
                if old[3]:
                    put(img, px_, py_, _mix(old[:3], _GEMS[k], 0.45))
        for (px_, py_) in ell_mask(gx + 0.5, gy + 0.5, 2.3, 2.3):
            put(img, px_, py_, GOLD[1] if (px_ - gx) + (py_ - gy) > 0 else GOLD[3])
        gem = _GEMS[k]
        bright = k == lit
        for (px_, py_) in ell_mask(gx + 0.5, gy + 0.5, 1.5, 1.5):
            up = (px_ - gx) + (py_ - gy) < 0
            put(img, px_, py_, _mix(gem, (255, 255, 255), 0.55 if bright and up else (0.15 if up else 0)) if bright
                else _mix(gem, (0, 0, 0), 0.25 if up else 0.5))
        if bright:
            put(img, int(round(gx)) - 1, int(round(gy)) - 1, (255, 255, 255))
    # sparks whirling over the vortex
    for k in range(6):
        a = turn * 1.5 + k * math.pi / 3
        rr = 3.5 + (k % 3) * 2
        put(img, int(round(c + math.cos(a) * rr)), int(round(c + math.sin(a) * rr)), (255, 250, 230))
    return img


def sigil(frame=0):
    """The Ascension Sigil at a cleared floor's centre: a ring of light that turns, the way up."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            dx, dy = x - 7.5, y - 7.5
            r = math.hypot(dx, dy)
            ang = math.atan2(dy, dx) + frame * math.pi / 8
            if 6.2 < r < 7.6:
                put(img, x, y, GOLD[3] if int(ang * 8 / math.pi) % 2 else GOLD[2], 230)
            elif 3.5 < r < 4.6 and math.sin(ang * 4) > 0:
                put(img, x, y, "#ffffff", 220)
            elif r < 1.5:
                put(img, x, y, "#fff8d0", 240)
    return img


# ------------------------------------------------------------------------------------------------
# shots, 16x16 (twice an 8x8's detail at the same size on the ground), pointing right. Each
# champion's shots are its own and each kind looks like what it does: what's aimed at you (and
# carries an affliction) is sharp and bright, the rings and spirals to weave through are rounder
# and plainer, the signatures the biggest. SPIN: the ones that turn as they fly (ms a radian).

CHRONO = R("#0a4a5a", "#1a8aa8", "#4af0ff", "#d8ffff")
BLADE = R("#5a0a0a", "#b01818", "#e83a2a", "#ffd0a0")
RIPTIDE = R("#1a0a3a", "#4a2a8a", "#8a5ad8", "#e0c8ff")
WOOD = R("#2a160a", "#4a2a14", "#6a4020", "#9a6a3a")
SPIN = {"Forge Hammer": 45, "Undertow": 70, "Slag Boulder": 90, "Gear Shot": 60, "Cog": 80,
        "Stolen Moment": 140, "Sovereign Star": 60, "Halo Shard": 50, "Iron Rivet": 70}


def _s16(fn):
    cv = Canvas(16, 16)
    fn(cv)
    return cv.img


def _halo(cv, m, c, a=80, r=1):
    """A soft glow `r` pixels round a shape, under nothing already drawn (a bright colour: it's
    faint)."""
    a = min(255, a + 40)
    ring = {(x + dx, y + dy) for (x, y) in m for dx in range(-r, r + 1) for dy in range(-r, r + 1)} - m
    for (x, y) in ring:
        if not cv.get(x, y)[3]:
            cv.px(x, y, c, a)


def _iron_shard(cv):
    m = poly_mask([(1, 5.5), (7, 4), (12, 5), (15.5, 8), (12, 11), (7, 12), (1, 10.5), (4, 8)])
    cv.shade(m, IRON)
    for (x, y) in m:  # its edge still hot from the forge
        if x >= 12:
            cv.px(x, y, EMBER[2] if x < 14 else EMBER[3])
    _halo(cv, m, EMBER[2], 70)


def _rending_splinter(cv):
    # a jagged sliver of red-hot iron, barbed along both edges so it tears
    m = poly_mask([(0, 7.5), (3, 6), (4, 7), (7, 5.5), (8, 6.8), (11, 6), (15.5, 8), (11, 10), (8, 9.2),
                   (7, 10.5), (4, 9), (3, 10), (0, 8.5)])
    cv.shade(m, BLADE)
    line(cv, 2, 8, 14, 8, BLADE[3])
    cv.px(15, 8, "#ffffff"); cv.px(14, 8, "#ffffff")
    _halo(cv, m, "#ff5a2a", 70)


def _iron_rivet(cv):
    m = ell_mask(8, 8, 4.2, 4.2)
    cv.shade(m, IRON)
    for (x, y) in ell_mask(8, 8, 1.6, 1.6):
        cv.px(x, y, EMBER[2])
    cv.px(8, 8, EMBER[3])
    cv.px(5, 8, IRON_DARK[0]); cv.px(11, 8, IRON_DARK[0])  # its slot, so its turning shows
    _halo(cv, m, EMBER[2], 60)


def _hammer_wave(cv):
    m = ell_mask(5, 8, 9, 7.5) - ell_mask(2, 8, 8, 6.4)
    cv.shade(m, EMBER)
    for (x, y) in m - ell_mask(5, 8, 8.2, 6.8):
        cv.px(x, y, EMBER[3])  # the front of the wave white-hot
    _halo(cv, m, EMBER[2], 90)


def _forge_hammer(cv):
    thick(cv, 1, 8, 10, 8, 2.2, WOOD)
    head = rect_mask(9, 3, 13, 12)
    cv.shade(head, IRON)
    for y in range(4, 12):  # its face glowing from the anvil
        cv.px(14, y, EMBER[2] if 5 < y < 10 else EMBER[1])
    cv.px(14, 7, EMBER[3]); cv.px(14, 8, EMBER[3])
    _halo(cv, head | {(14, y) for y in range(4, 12)}, EMBER[2], 70)


def _trident(cv, shaft, prong, foam):
    for x in range(0, 3):
        for y in range(6, 11):
            cv.px(x, y, foam, 70 + x * 30)
    thick(cv, 1, 8, 11, 8, 2, shaft)
    line(cv, 10, 4, 10, 12, shaft[2])
    line(cv, 10, 8, 15, 8, prong)
    line(cv, 10, 4, 14, 4, prong)
    line(cv, 10, 12, 14, 12, prong)
    for y in (4, 8, 12):
        cv.px(15 if y == 8 else 14, y, "#ffffff")


def _tide_orb(cv):
    orb(cv, 8, 8, 5.2, SCALE, glow="#7ae0d0")
    for k in range(9):  # a curl of foam in it
        a = 0.4 + k * 0.32
        cv.px(8 + math.cos(a) * (3.2 - k * 0.2), 8 + math.sin(a) * (3.2 - k * 0.2), WAVE[3])
    cv.px(6, 5, "#ffffff"); cv.px(5, 6, "#ffffff")


def _bubble(cv):
    for (x, y) in ell_mask(8, 8, 4.6, 4.6):
        cv.px(x, y, "#7ad8e0", 60)
    glow_ring(cv, 8, 8, 4.6, "#bff4ff", 230)
    cv.px(6, 5, "#ffffff"); cv.px(5, 6, "#ffffff"); cv.px(6, 6, "#e8ffff")
    glow_ring(cv, 12.5, 3.5, 1.0, "#bff4ff", 180)


def _undertow(cv):
    m = ell_mask(8, 8, 6.4, 6.4)
    for (x, y) in m:
        cv.px(x, y, WAVE[0])
    for k in range(3):  # three arms of a whirlpool, drawing in
        for i in range(24):
            t = i / 23
            a = k * 2 * math.pi / 3 + t * 3.6
            r = 0.8 + t * 5.6
            cv.px(8 + math.cos(a) * r, 8 + math.sin(a) * r, WAVE[3] if t < 0.35 else WAVE[2])
    cv.px(8, 8, "#e0fff8")
    _halo(cv, m, "#46c2b4", 90)


def _wave_crest(cv):
    m = ell_mask(5, 8, 9.5, 7.2) - ell_mask(1.5, 8.6, 8.6, 6.2)
    cv.shade(m, WAVE)
    for (x, y) in m - ell_mask(5, 8, 8.6, 6.4):
        cv.px(x, y, "#e8ffff")  # the foam along its crest
    for (x, y) in ((13, 2), (15, 5), (15, 11), (13, 14)):
        cv.px(x, y, "#bff4ff", 200)
    _halo(cv, m, "#46c2b4", 70)


def _magma_ball(cv):
    orb(cv, 8, 8, 5.4, LAVA, glow=LAVA[2])
    for (x, y) in ((5, 10), (6, 10), (6, 11), (10, 5), (11, 6), (11, 10), (10, 11), (4, 7)):
        cv.px(x, y, OBSIDIAN[2])  # its crust breaking
    cv.px(7, 6, "#ffffff")


def _cinder(cv):
    for (x, y) in ((1, 6), (2, 10), (3, 7), (0, 9)):
        cv.px(x, y, LAVA[1], 160)
    m = poly_mask([(1.5, 8), (6, 5), (11, 5.5), (15.5, 8), (11, 10.5), (6, 11)])
    cv.shade(m, LAVA)
    for (x, y) in ell_mask(11, 8, 2.4, 1.6):
        cv.px(x, y, LAVA[3])
    cv.px(13, 8, "#ffffff")
    _halo(cv, m, LAVA[2], 60)


def _ember(cv):
    m = ell_mask(8, 8, 3.2, 3.2)
    cv.shade(m, LAVA)
    cv.px(8, 8, "#ffffff"); cv.px(7, 7, LAVA[3])
    for (x, y) in ((8, 3), (13, 8), (8, 13), (3, 8)):
        cv.px(x, y, LAVA[3], 200)  # sparks flying off it
    _halo(cv, m, LAVA[2], 90)


def _molten_drop(cv):
    m = ell_mask(10, 8, 4.4, 4.4) | poly_mask([(0.5, 8), (9, 4), (9, 12)])
    cv.shade(m, LAVA)
    cv.px(11, 6, "#ffffff"); cv.px(12, 7, LAVA[3])
    for x in (3, 6):
        cv.px(x, 11, LAVA[2], 200)
    _halo(cv, m, LAVA[2], 70)


def _slag_boulder(cv):
    m = poly_mask([(3, 4.5), (7, 1.5), (12, 2.5), (14.5, 7), (13.5, 12), (8, 14.5), (3, 12.5), (1.5, 8)])
    cv.shade(m, ROCK)
    for a, b in (((4, 6), (8, 8)), ((8, 8), (12, 5)), ((8, 8), (9, 13)), ((12, 5), (13, 9))):
        line(cv, a[0], a[1], b[0], b[1], LAVA[2])  # the melt in its cracks
    cv.px(8, 8, LAVA[3])
    _halo(cv, m, LAVA[2], 60)


def _meteor_fragment(cv):
    tail = poly_mask([(0, 8), (8, 4.5), (9, 11.5)])
    cv.shade(tail, FLAME, rim=False)
    for (x, y) in tail:
        if x < 4:
            cv.px(x, y, FLAME[1], 150)
    cv.shade(ell_mask(11, 8, 3.6, 3.6), ROCK)
    cv.px(13, 7, LAVA[3]); cv.px(12, 9, LAVA[2])


def _gear_shot(cv):
    cog(cv, 8, 8, 4.4, BRASS, teeth=8)
    cv.px(8, 8, "#3a220a")


def _cog(cv):
    cog(cv, 8, 8, 5.2, STEEL, teeth=10)
    for k in range(4):  # spokes, a brass hub
        a = k * math.pi / 2 + 0.4
        line(cv, 8, 8, 8 + math.cos(a) * 3.6, 8 + math.sin(a) * 3.6, STEEL[3])
    for (x, y) in ell_mask(8, 8, 1.4, 1.4):
        cv.px(x, y, BRASS[3])


def _chrono_orb(cv):
    orb(cv, 8, 8, 5.4, CHRONO, glow="#4af0ff")
    for (x, y) in ((8, 3), (13, 8), (8, 13), (3, 8)):
        cv.px(x, y, "#ffffff")  # the hours
    line(cv, 8, 8, 8, 5, "#0a3a4a")
    line(cv, 8, 8, 11, 8, "#0a3a4a")
    cv.px(8, 8, "#ffffff")


def _clock_hand(cv, ramp, glow):
    m = poly_mask([(4, 7), (11, 6.5), (11, 9.5), (4, 9)]) | poly_mask([(10.5, 4.5), (15.5, 8), (10.5, 11.5)])
    cv.shade(m, ramp)
    cv.px(14, 8, "#ffffff")
    _halo(cv, m, glow, 60)
    return m


def _hand_blade(cv):
    _clock_hand(cv, BLADE, "#ff3a1a")
    glow_ring(cv, 3, 8, 1.6, BRASS[3], 230)  # the hand's counterweight


def _rewind_blade(cv):
    _clock_hand(cv, CHRONO, "#4af0ff")
    for k in range(8):  # an arrow curling back on itself: time running the wrong way
        a = math.pi * 0.55 + k * 0.28
        cv.px(4 + math.cos(a) * 3.2, 8 + math.sin(a) * 3.2, "#ffffff" if k == 7 else "#4af0ff", 220)


def _stolen_moment(cv):
    case = ell_mask(8, 9, 5.6, 5.6)
    cv.shade(case, BRASS)
    for (x, y) in ell_mask(8, 9, 4, 4):
        cv.px(x, y, DIAL[2])
    cv.shade(rect_mask(7, 1, 8, 3), BRASS)  # the crown
    line(cv, 8, 9, 8, 6, "#1a1a1a")
    line(cv, 8, 9, 10, 10, "#1a1a1a")
    cv.px(8, 9, BLADE[2])
    _halo(cv, case, "#4af0ff", 80)


def _lance(cv, ramp, head, big=False):
    thick(cv, 0, 8, 11, 8, 2.4 if big else 2, ramp)
    m = poly_mask([(10, 4 if big else 5), (16, 8), (10, 12 if big else 11)])
    cv.shade(m, head)
    line(cv, 10, 4, 10, 12, ramp[3])  # the guard
    cv.px(14, 8, "#ffffff"); cv.px(15, 8, "#ffffff")
    if big:  # the Sovereign's wings on it
        for (x, y) in ((8, 3), (7, 2), (6, 2), (8, 13), (7, 14), (6, 14), (9, 4), (9, 12)):
            cv.px(x, y, PLATE[3])
        for (x, y) in ((7, 3), (6, 3), (7, 13), (6, 13)):
            cv.px(x, y, PLATE[2])
    _halo(cv, m, FLAME[2], 80)


def _star(cv):
    m = poly_mask([(8, 0.5), (9.6, 6.4), (15.5, 8), (9.6, 9.6), (8, 15.5), (6.4, 9.6), (0.5, 8), (6.4, 6.4)])
    cv.shade(m, FLAME)
    for (x, y) in ell_mask(8, 8, 1.8, 1.8):
        cv.px(x, y, "#ffffff")
    _halo(cv, m, GOLD[3], 60)


def _feather(cv, ramp, tip):
    # a feather flying point first: a long vane, notched at the back, its barbs swept back
    m = poly_mask([(1, 4), (6, 5), (12, 5.5), (15.5, 8), (12, 10.5), (6, 11), (1, 12), (3.5, 8)])
    cv.shade(m, ramp)
    for x0 in (7, 10, 13):
        line(cv, x0, 8, x0 - 3, 5.5, ramp[1])
        line(cv, x0, 8, x0 - 3, 10.5, ramp[1])
    line(cv, 2, 8, 14, 8, ramp[3])
    cv.px(15, 8, tip); cv.px(14, 8, tip)
    _halo(cv, m, ramp[2], 50)


def _void_ember(cv):
    cv.shade(poly_mask([(0.5, 8), (7, 3.5), (7, 12.5)]), VOID_FLAME, rim=False)
    orb(cv, 10, 8, 4.4, VOID_FLAME, glow="#e040ff")
    for (x, y) in ell_mask(10, 8, 1.7, 1.7):
        cv.px(x, y, "#14041a")  # the void in its heart
    cv.px(12, 6, "#ffffff")


def _halo_shard(cv):
    glow_ring(cv, 8, 8, 6.2, "#ffd04a", 90)
    cv.shade(poly_mask([(8, 2), (13.5, 8), (8, 14), (2.5, 8)]), GOLD)
    for (x, y) in ell_mask(8, 8, 1.6, 1.6):
        cv.px(x, y, "#ffffff")


def shots():
    return {
        # Ferrogrim: iron and the forge's fire
        "Iron Shard": _s16(_iron_shard),
        "Rending Splinter": _s16(_rending_splinter),
        "Iron Rivet": _s16(_iron_rivet),
        "Hammer Wave": _s16(_hammer_wave),
        "Forge Hammer": _s16(_forge_hammer),
        # Nerissa: the sea
        "Trident Bolt": _s16(lambda cv: _trident(cv, GOLD, "#bff4ff", "#bff4ff")),
        "Riptide Trident": _s16(lambda cv: _trident(cv, RIPTIDE, "#c8a0ff", "#8a5ad8")),
        "Tide Orb": _s16(_tide_orb),
        "Bubble": _s16(_bubble),
        "Undertow": _s16(_undertow),
        "Wave Crest": _s16(_wave_crest),
        # Pyrrhax: the melt
        "Magma Ball": _s16(_magma_ball),
        "Cinder": _s16(_cinder),
        "Ember": _s16(_ember),
        "Molten Drop": _s16(_molten_drop),
        "Slag Boulder": _s16(_slag_boulder),
        "Meteor Fragment": _s16(_meteor_fragment),
        # Chronarch: brass, steel and stolen time
        "Gear Shot": _s16(_gear_shot),
        "Cog": _s16(_cog),
        "Chrono Orb": _s16(_chrono_orb),
        "Hand Blade": _s16(_hand_blade),
        "Rewind Blade": _s16(_rewind_blade),
        "Stolen Moment": _s16(_stolen_moment),
        # Vaelith: gold, flame and the void
        "Sovereign Lance": _s16(lambda cv: _lance(cv, GOLD, FLAME)),
        "Decree Lance": _s16(lambda cv: _lance(cv, GOLD, R("#9a6a14", "#ffd04a", "#fff0a0", "#ffffff"), big=True)),
        "Sovereign Star": _s16(_star),
        "Flame Feather": _s16(lambda cv: _feather(cv, FLAME, FLAME[3])),
        "Gilded Quill": _s16(lambda cv: _feather(cv, GOLD, "#ffffff")),
        "Void Ember": _s16(_void_ember),
        "Halo Shard": _s16(_halo_shard),
    }


# ------------------------------------------------------------------------------------------------
# the Crucible's white bags (8x8 items): name -> pixel rows, in the Black Between's item palette
# (gold: Y highlight, G, g, o shadow; violet: P, V, v, u; night: n, k, K; W white) and the
# tower's own: iron (I light, i, d dark), fire (F white-hot, l, L), the sea (s foam, t, T deep),
# the Orrery's cyan (c, C) and brass (b)

CRUCIBLE_PAL = {
    "I": "#b4bcc8", "i": "#6e7684", "d": "#2e323c", "F": "#fff0b0", "l": "#ffb040", "L": "#ff6a14",
    "s": "#bff4ff", "t": "#46c2b4", "T": "#16505a", "c": "#4af0ff", "C": "#1a8aa8", "b": "#d0a040",
}

ITEMS = {
    # Ferrogrim's own sentinel, folded for the Mechanic to raise: an iron tower, its visor aglow
    "Ferrogrim's Bastion Turret": ["..dIId..", ".dIiiid.", ".dLlLLd.", ".dIiiid.", "..dIid..", ".diIiid.", "dIiiiiid", "dddddddd"],
    # a pearl of Nerissa's sea in a gold clasp, foam catching the light
    "Nerissa's Tidepearl": ["...GG...", "..TgoT..", ".TtsWtT.", "TtssttTT", "TttttTTT", ".TtTTTT.", "..TTTT..", "........"],
    # Pyrrhax's mortar: an iron barrel cocked at the sky on a brass carriage, molten at the muzzle
    "Pyrrhax's Meteor Mortar": [".....dLF", "....dIlL", "...dIiLd", "..dIiid.", ".dIiid..", "bdiid...", "bKbb....", "KbK....."],
    # Chronarch's coat: night-blue, brass-trimmed, the Orrery's clock over its heart
    "Chronarch's Coat of Stolen Hours": ["..bKKb..", ".bKnnKb.", ".KnCcnK.", "bKncWnKb", "bKnCcnKb", "bKknnkKb", "bKkKKkKb", ".b....b."],
    # Vaelith's own lute, the Bard's: a gold body with a violet rose of a soundhole, its neck
    # running up to the right, a white feather of the Sovereign's wings at the headstock
    "Vaelith's Final Aria": ["......PW", ".....oWP", "....oG..", "..GYG...", ".GYWYg..", "GYvuVgo.", ".gVPvgo.", "..ooo..."],
}


def item_icon(name):
    from black_between.small import ITEM_PAL
    pal = dict(ITEM_PAL, **CRUCIBLE_PAL)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    for y, r in enumerate(ITEMS[name]):
        assert len(r) == 8, (name, r)
        for x, ch in enumerate(r):
            if ch != ".":
                put(img, x, y, pal[ch])
    return img
