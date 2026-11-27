"""The Ice Tomb's art (tools/build_ice_tomb.py puts it on its sheets): the tomb's floors and walls
(8x8), what stands in its rooms (16x16), its monsters (16x16), the three Seal Keepers (16x16,
drawn large), the Frozen Court's three (32x32), and the shots (8x8, pointing right).

- Kaldrun, the Rime Warden: a hulking knight in blue steel, horned and icicled, a glacier for a
  shield and a long frost blade; in his Permafrost the ice grows over him in spikes.
- Ysolde, the Aurora Oracle: a veiled priestess in white and violet, the aurora streaming up behind
  her like a mantle, a staff of ice with a star in it; in her Polar Night her robes go dark and the
  aurora burns violet.
- Hrimgar, the Frost Reaver: a berserker under a white wolf's pelt, a beard of icicles, two axes of
  blue ice; in the Shattering the cold splits through him in glowing cracks.

Drawn as the Crucible's are (tools/crucible/bosses.py, whose helpers these use): shapes shaded from
the upper left, hand-placed details, no outline of their own (the client draws it)."""
import math
import os
import random
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from black_between.bosses import Canvas, R, poly_mask, ell_mask, sparkle, orb, spectral  # noqa: E402
from black_between.small import put, C  # noqa: E402
from crucible.bosses import thick, line, rect_mask, glow_ring  # noqa: E402

# =================================================================================================
# palettes (dark .. light)

ICE = R("#2a5a82", "#4c8ab8", "#8cc8ea", "#dcf6ff")
ICE_DEEP = R("#0e2440", "#1a3a5e", "#2c5a86", "#4a80ae")
SNOW = R("#8ea4bc", "#bccce0", "#e2ecf6", "#ffffff")
STEEL = R("#1a2434", "#34465e", "#5c7494", "#9ab4d4")
STEEL_DARK = R("#0e141e", "#1c2636", "#2e3c52", "#46587a")
FROST = R("#3a8ab0", "#5ec4e8", "#a8f0ff", "#ffffff")
AURORA = R("#0e5a44", "#1aa070", "#4ae8a4", "#c4ffe4")
VIOLET = R("#2e1258", "#5a2a9a", "#9060d8", "#dcc4ff")
ROBE = R("#7a8aa8", "#b4c2da", "#e0e8f4", "#ffffff")
ROBE_DARK = R("#0c0a1e", "#1c1638", "#30265a", "#4a3c80")
PELT = R("#8c96a4", "#bcc6d2", "#e4eaf0", "#ffffff")
SKIN_COLD = R("#4a6a86", "#7a9cb8", "#aac8de", "#d8ecf8")
WRAP = R("#3e4c5c", "#6a7c8e", "#9cb0c2", "#d0e2ee")
BONE = R("#5a5a62", "#8a8a94", "#bcbcc6", "#eeeef4")
GOLD = R("#6a4a12", "#b08420", "#e8c048", "#fff2b0")
WOOD = R("#2a1c14", "#4a3422", "#6e5034", "#94744e")
STONE = R("#1e2838", "#2c3a50", "#40546e", "#5c7290")


def _blank(c="#000000"):
    img = Image.new("RGBA", (8, 8), C(c) + (255,))
    return img


def _clear():
    return Image.new("RGBA", (8, 8), (0, 0, 0, 0))


def _blob(img, rnd, colour, n, size=(1, 2), avoid=()):
    """A few soft specks: n patches of 1 or 2 pixels, kept off the squares in avoid."""
    for _ in range(n):
        x, y = rnd.randrange(8), rnd.randrange(8)
        if (x, y) in avoid:
            continue
        put(img, x, y, colour)
        if rnd.randint(*size) == 2:
            nx, ny = (x + 1, y) if rnd.random() < 0.5 else (x, y + 1)
            if (nx, ny) not in avoid:
                put(img, nx, ny, colour)


def _crack(img, rnd, colour, length, start=None, glow=None):
    """A hairline crack wandering across the square; with glow, a fainter halo beside it."""
    x, y = start if start else (rnd.randrange(8), rnd.randrange(8))
    dx, dy = rnd.choice(((1, 0), (0, 1), (1, 1), (1, -1)))
    for _ in range(length):
        if glow:
            put(img, x + dy, y - dx, glow)
        put(img, x, y, colour)
        if rnd.random() < 0.45:
            x += dx
            y += dy
        else:
            x += rnd.choice((dx, dy, 0)) or 1
            y += rnd.choice((dy, 0))
        if not (0 <= x < 8 and 0 <= y < 8):
            break


def slab(base, seam, light, dark, frost=None, cracked=None, vein=None):
    """One flagstone to a square: a darker seam along its right and lower edges, a lit upper-left
    corner, a face kept quiet (a few specks); frost creeping in from a corner on some, a crack in
    one."""
    def make(seed):
        rnd = random.Random(seed * 131 + 5)
        img = _blank(base)
        for i in range(8):
            put(img, 7, i, seam)
            put(img, i, 7, seam)
        put(img, 0, 0, light)
        put(img, 1, 0, light)
        put(img, 0, 1, light)
        put(img, 6, 6, dark)
        face = {(x, y) for x in range(7) for y in range(7)}
        _blob(img, rnd, light, 2, avoid={(x, y) for x in range(8) for y in range(8)} - face)
        _blob(img, rnd, dark, 2, avoid={(x, y) for x in range(8) for y in range(8)} - face)
        if frost and seed % 3 == 1:
            cx, cy = rnd.choice(((0, 0), (6, 0), (0, 6), (6, 6)))
            for x, y in ((cx, cy), (cx + (1 if cx == 0 else -1), cy), (cx, cy + (1 if cy == 0 else -1))):
                put(img, x, y, frost[0])
            put(img, cx, cy, frost[1])
        if cracked and seed == 3:
            _crack(img, rnd, cracked, 5, start=(1, rnd.randrange(2, 5)))
        if vein and seed % 2 == 1:
            # a vein through the stone, from one side to another
            x, y = 0, rnd.randrange(1, 6)
            while x < 7 and 0 <= y < 7:
                put(img, x, y, vein)
                x += 1
                y += rnd.choice((-1, 0, 0, 1))
        return img
    return make


def smooth(base, shades, specks=3, sparkle=None, cracks=None, deep=None):
    """Even ground (packed snow, clear ice, still water): a base with a few close shades in soft
    patches, a glint now and then, a fine crack in some, a darker hollow in others."""
    def make(seed):
        rnd = random.Random(seed * 977 + 13)
        img = _blank(base)
        if deep and seed % 2 == 0:
            cx, cy = rnd.randrange(2, 6), rnd.randrange(2, 6)
            for x in range(8):
                for y in range(8):
                    if (x - cx) ** 2 + (y - cy) ** 2 * 1.4 < 5:
                        put(img, x, y, deep)
        for c in shades:
            _blob(img, rnd, c, specks)
        if cracks and seed % 3 == 2:
            _crack(img, rnd, cracks, 6)
        if sparkle and seed % 3 == 1:
            x, y = rnd.randrange(1, 7), rnd.randrange(1, 7)
            put(img, x, y, sparkle[0])
            put(img, x - 1, y, sparkle[1])
            put(img, x, y + 1, sparkle[1])
        return img
    return make


def drift(base, shadow, light, sparkle="#ffffff"):
    """Snow: soft, its shadows pooled low in the square, a sparkle or two."""
    def make(seed):
        rnd = random.Random(seed * 211 + 3)
        img = _blank(base)
        sx = rnd.randrange(8)
        for x in range(8):
            h = 6 + round(math.sin((x - sx) * 0.7) * 1.2)
            for y in range(h, 8):
                put(img, x, y, shadow)
        _blob(img, rnd, light, 3)
        if seed % 2 == 0:
            put(img, rnd.randrange(8), rnd.randrange(6), sparkle)
        return img
    return make


def mosaic(a, b, grout, gold):
    """The old tomb's floor: small square tiles in two blues, a gold stud where four meet in some."""
    def make(seed):
        rnd = random.Random(seed * 7 + 1)
        img = _blank(a)
        for y in range(8):
            for x in range(8):
                if x % 4 == 3 or y % 4 == 3:
                    put(img, x, y, grout)
                elif (x // 4 + y // 4 + seed) % 2:
                    put(img, x, y, b)
        if seed % 2 == 0:
            put(img, 3, 3, gold)
        put(img, rnd.choice((0, 4)), rnd.choice((0, 4)), "#5a7aa4")
        return img
    return make


def runner(base, weave):
    """The deep blue runner: a quiet weave of rows; its gold border is its edge (RUNNER_EDGE)."""
    def make(seed):
        img = _blank(base)
        for y in range(8):
            for x in range(8):
                if (y + seed) % 4 == 0 and (x + y // 4) % 2 == 0:
                    put(img, x, y, weave)
        return img
    return make


def luminous(base, core, hem=None, streaks=2):
    """A floor that glows (the aurora's light, the Court's sigil): an even light with brighter
    streaks running up it, and a hem of another colour at its foot."""
    def make(seed):
        rnd = random.Random(seed * 53 + 9)
        img = _blank(base)
        for _ in range(streaks):
            x = rnd.randrange(8)
            top = rnd.randrange(0, 3)
            for y in range(top, top + rnd.randrange(3, 6)):
                put(img, x, y, core)
        if hem:
            for x in range(8):
                if rnd.random() < 0.6:
                    put(img, x, 7, hem)
        return img
    return make


def fissure(base, glow, core, halo):
    """A crack splitting the floor from side to side, light welling up out of it."""
    def make(seed):
        rnd = random.Random(seed * 17 + 4)
        img = _blank(base)
        horizontal = seed % 2 == 0
        p = rnd.randrange(2, 6)
        for i in range(8):
            q = max(1, min(6, p))
            x, y = (i, q) if horizontal else (q, i)
            hx, hy = (i, q - 1) if horizontal else (q - 1, i)
            put(img, hx, hy, halo)
            put(img, x, y, glow)
            if rnd.random() < 0.5:
                put(img, x, y, core)
            p += rnd.choice((-1, 0, 0, 1))
        return img
    return make


def open_drop(mist, glint):
    """A drop into the open: see-through, so the sky the tomb hangs in shows through it (and the
    glacier's cliffs, the client's islands.rs), a wisp of mist drifting across it."""
    def make(seed):
        rnd = random.Random(seed * 19 + 5)
        img = _clear()
        y = rnd.randrange(8)
        for x in range(rnd.randrange(0, 3), rnd.randrange(5, 8)):
            put(img, x, y, mist, 70)
            if rnd.random() < 0.5:
                put(img, x, y + 1, mist, 40)
        if seed % 3 == 0:
            put(img, rnd.randrange(8), rnd.randrange(8), glint, 90)
        return img
    return make


def starry(base, faint, bright):
    def make(seed):
        rnd = random.Random(seed * 3 + 77)
        img = _blank(base)
        _blob(img, rnd, faint, 3, size=(1, 1))
        if seed % 2 == 0:
            put(img, rnd.randrange(8), rnd.randrange(8), bright)
        return img
    return make


def facets(base, light, line, white):
    """Thick old ice in facets: lit planes, darker seams between them, a white glint."""
    def make(seed):
        rnd = random.Random(seed * 59 + 2)
        img = _blank(base)
        a = rnd.randrange(2, 6)
        for i in range(8):
            put(img, i, max(0, min(7, a + (i - 4) // 2)), line)
        for y in range(8):
            for x in range(8):
                if y < a + (x - 4) // 2 - 1 and (x + y) % 5 == 0:
                    put(img, x, y, light)
        put(img, rnd.randrange(8), rnd.randrange(8), white)
        return img
    return make


def outline(base, ring, inner=None):
    """A marked square: a bright frame round a calm middle (where something is about to fall)."""
    def make(seed):
        img = _blank(base)
        for i in range(8):
            for x, y in ((i, 0), (i, 7), (0, i), (7, i)):
                put(img, x, y, ring)
        if inner:
            for x, y in ((3, 3), (4, 4), (3 + seed % 2, 4 - seed % 2)):
                put(img, x, y, inner)
        return img
    return make


def boulders(base, shadow, light):
    """Snow tumbling: rounded clumps, each lit on top and shadowed below."""
    def make(seed):
        rnd = random.Random(seed * 41 + 8)
        img = _blank(base)
        for _ in range(2):
            cx, cy = rnd.randrange(1, 7), rnd.randrange(1, 7)
            for x in range(cx - 1, cx + 2):
                put(img, x, cy + 1, shadow)
            put(img, cx, cy - 1, light)
            put(img, cx - 1, cy, light)
        return img
    return make


def gale(base, streak, shadow):
    """Wind-driven snow: short streaks racing across (the tile flows, Animate)."""
    def make(seed):
        rnd = random.Random(seed * 23 + 6)
        img = _blank(base)
        for _ in range(3):
            x, y = rnd.randrange(8), rnd.randrange(8)
            for i in range(3):
                put(img, x + i, y, streak)
            put(img, x + 3, y, shadow)
        return img
    return make


def ripple(base, ripples, glint):
    """Black water: short ripples lying across it, a glint (the tile flows, Animate)."""
    def make(seed):
        rnd = random.Random(seed * 13 + 1)
        img = _blank(base)
        for c in ripples:
            for _ in range(2):
                x, y = rnd.randrange(7), rnd.randrange(8)
                put(img, x, y, c)
                put(img, x + 1, y, c)
        if seed % 2 == 0:
            put(img, rnd.randrange(8), rnd.randrange(8), glint)
        return img
    return make


def crazed(base, deep, crack, shadow):
    """Ice breaking: two or three bold cracks running across it from edge to edge, white where
    they open, the dark water showing along them."""
    def make(seed):
        rnd = random.Random(seed * 71 + 3)
        img = _blank(base)
        for k in range(2 + seed % 2):
            horizontal = (k + seed) % 2 == 0
            p = rnd.randrange(1, 7)
            for i in range(8):
                x, y = (i, p) if horizontal else (p, i)
                sx, sy = (i, p + 1) if horizontal else (p + 1, i)
                put(img, sx, sy, deep)
                put(img, x, y, crack)
                if rnd.random() < 0.35:
                    p = max(1, min(6, p + rnd.choice((-1, 1))))
        put(img, rnd.randrange(8), rnd.randrange(8), shadow)
        return img
    return make


# name -> (art, variants)
TILES = {
    # the tomb's halls: blue flagstones, rime creeping in at their corners; a darker inlaid stone;
    # a runner of deep blue (trimmed in gold by its edge)
    "Ice Tomb Floor": (slab("#3e5a7c", "#2a3e5a", "#4e6c90", "#34506e", frost=("#9cc8e6", "#e2f4ff"), cracked="#2a3e5a"), 4),
    "Ice Tomb Inlay": (slab("#2c405e", "#1e2c44", "#3a5276", "#24364e", frost=("#6a9cc8", "#a8d4f0")), 3),
    "Rime Runner": (runner("#24327a", "#2c3c8a"), 2),
    "Snowdrift": (drift("#dce8f4", "#c2d4e8", "#eef4fb"), 4),
    # the Frozen Court: polished white marble, a darker rim at its walls, the snowflake laid in it
    # glowing (outlined by its edge)
    "Court Marble": (slab("#c8d6e8", "#a8bad2", "#e2ecf8", "#bccbe0", vein="#a2b8d6"), 6),
    "Court Rim": (slab("#3c5478", "#283c5a", "#4e6a92", "#344a6a", frost=("#8cb8dc", "#d8b860")), 3),
    "Court Sigil": (luminous("#7ad8f0", "#d4f8ff", streaks=2), 3),
    # the frozen lake: clear thin ice over black water, dark where it's deep, a hairline crack
    # here and there. It cracks under whoever stands on it (ThinIce in BehaviorDb.IceTomb.cs),
    # and breaks; it is slippery (SlideAmount)
    "Lake Ice": (smooth("#5e9ccc", ["#66a4d2", "#5896c6"], specks=1, sparkle=("#ffffff", "#9fd2f0"), cracks="#90c8ec", deep="#4c88ba"), 6),
    "Cracking Ice": (crazed("#6aa8d6", "#123a60", "#f2fbff", "#4a84b6"), 3),
    # freezing water: walkable, slow, and it burns with cold (a rim of ice round it: its edge)
    "Frigid Water": (ripple("#0c2846", ["#163a60", "#1e4a74"], "#6ab4e0"), 4),
    # where something is coming (any of the fights): a frame of frost round each square, harmless
    "Rime Warning": (outline("#4e80ae", "#bfe8ff", inner="#8cc8ea"), 2),
    # the Court's phases: each walkable, each burning with cold
    "Avalanche": (boulders("#eef4fc", "#b8cce2", "#ffffff"), 3),
    "Aurora Light": (luminous("#3cd8a2", "#b4fce0", hem="#8a6ae8", streaks=3), 3),
    "Blizzard": (gale("#d8e8f6", "#ffffff", "#a8c0da"), 3),
    "Frost Fissure": (fissure("#0e2038", "#5ad4ff", "#e0faff", "#1e4a72"), 4),
    "Frozen Scar": (fissure("#34567c", "#5a86b0", "#7aa6cc", "#3c5f86"), 2),
    "Polar Dark": (starry("#120e28", "#2e2458", "#9c86f0"), 3),
    "Permafrost": (facets("#8ccaee", "#c8ecff", "#5aa4d6", "#ffffff"), 3),
    # the floe the Shattered Lake leaves, and its rim
    "Ice Floe": (smooth("#cfe6f6", ["#bcdcf0", "#ddf0fb"], specks=3, sparkle=("#ffffff", "#e8f6ff")), 3),
    "Ice Floe Rim": (smooth("#f2faff", ["#dceefa"], specks=2), 2),
    # under the walls: the glacier's ice, which shows where a wall stands at the edge of the drop
    "Ice Tomb Wall Floor": (facets("#2c5e8c", "#4a86b8", "#1e4670", "#9cd4f4"), 2),
    # the glacier caves: packed blue snow and ice; the old tomb's mosaic in its finer rooms; the
    # ossuary's slate strewn with bone
    "Glacier Floor": (smooth("#4a78a2", ["#5486ae", "#42709a"], specks=3, sparkle=("#cfeaff", "#7aaad2"), deep="#44709a"), 6),
    "Tomb Mosaic": (mosaic("#2e4870", "#38557e", "#1e3050", "#d4ac4c"), 4),
    "Bone Floor": (slab("#4c505c", "#363a46", "#5c606c", "#42464f", frost=("#d8d4c8", "#f0ece0")), 4),
    # the chasms: a drop into the dark, mist far below; and the bridges of ice that freeze across
    # them as someone comes near (IceBridge in BehaviorDb.IceTomb.cs), rimmed by their edge
    "Tomb Abyss": (open_drop("#9cc4e6", "#ffffff"), 4),
    "Ice Bridge": (facets("#86c2e8", "#b4e0f8", "#6aa8d6", "#ffffff"), 3),
    "Ice Bridge Rim": (smooth("#d6eefb", ["#b8dcf2", "#eaf8ff"], specks=2), 2),
    # never laid: what the client hangs under the tomb's edges into the polar night round it
    # (islands::ICE): the glacier's blue ice streaked with light, under a band of snow
    "Ice Tomb Underside Rock": (facets("#2c5e8c", "#4a86b8", "#1e4670", "#9cd4f4"), 4),
    "Ice Tomb Underside Rim": (drift("#cddcec", "#9cb6d2", "#e6eff8"), 3),
}

# edges (ground.rs `edged`): name -> (edge facing west, corner at the upper left, inner corner or
# None, same-type mode). Drawn where the ground meets another (same-type mode) or nothing.


def _edge(cols, corner_cols=None):
    e = _clear()
    for i, c in enumerate(cols):
        for y in range(8):
            put(e, i, y, c)
    k = _clear()
    for i, c in enumerate(corner_cols or cols[:1]):
        put(k, i, 0, c)
        put(k, 0, i, c)
    return e, k


EDGES = {
    # gold trim along the runner
    "Rime Runner": _edge(["#1a2250", "#d8b050"]) + (None, True),
    # the snowflake's frost-blue outline and inner glow
    "Court Sigil": _edge(["#2a78b8", "#4aaedc"]) + (None, True),
    # a lip of ice round open water
    "Frigid Water": _edge(["#dcf4ff", "#7ab8e0"]) + (None, True),
}


def tiles():
    return {name: [fn(i) for i in range(n)] for name, (fn, n) in TILES.items()}


# =================================================================================================
# walls (8x8): blocks of old stone under ice, icicles hanging off their brows; the glacier's own
# walls of clear blue ice; snow on top

def wall_face(seed, sconce=False, frame=0):
    rnd = random.Random(seed * 101 + 3)
    img = _blank("#24395a")
    for y in range(8):
        for x in range(8):
            row = (y - 2) // 3
            off = 2 if row % 2 else 0
            if y >= 2 and ((y - 2) % 3 == 2 or (x + off) % 4 == 3):
                put(img, x, y, "#172840")
            elif y >= 2 and ((x + off) % 4 == 0 or (y - 2) % 3 == 0):
                put(img, x, y, "#2e4870")
    # the brow: snow on the lip, icicles below it
    for x in range(8):
        put(img, x, 0, "#e2f0fa")
        put(img, x, 1, "#8cbede" if x % 3 else "#bfe4f8")
    for x in (rnd.randrange(0, 3), rnd.randrange(4, 8)):
        put(img, x, 2, "#9cd0f0")
        if rnd.random() < 0.6:
            put(img, x, 3, "#6aaad6")
    if sconce:
        # a sconce of blue flame
        put(img, 3, 7, "#5c7494"); put(img, 4, 7, "#5c7494"); put(img, 3, 6, "#34465e"); put(img, 4, 6, "#34465e")
        f = frame % 2
        put(img, 3, 5, FROST[1 + f]); put(img, 4, 5, FROST[2 - f]); put(img, 3 + f, 4, FROST[3]); put(img, 4 - f, 3, FROST[2])
    return img


def glacier_face(seed):
    """A wall of the glacier caves: clear blue ice, deepening downwards, light caught in it in
    tall streaks, snow along its brow."""
    rnd = random.Random(seed * 53 + 17)
    img = _blank("#2e6696")
    for y in range(8):
        for x in range(8):
            put(img, x, y, "#3a78aa" if y < 3 else ("#2e6696" if y < 6 else "#245682"))
    for _ in range(2):
        x = rnd.randrange(8)
        top = rnd.randrange(1, 3)
        for y in range(top, top + rnd.randrange(3, 6)):
            put(img, x, y, "#7ab8e2")
        put(img, x, top, "#c8ecff")
    put(img, rnd.randrange(8), rnd.randrange(4, 8), "#1c4670")
    for x in range(8):
        put(img, x, 0, "#e6f2fb")
    return img


def wall_top(seed):
    rnd = random.Random(seed * 37 + 11)
    img = _blank("#d2e0ee")
    _blob(img, rnd, "#c0d2e6", 4)
    _blob(img, rnd, "#e6f0f9", 3)
    if seed % 2 == 0:
        put(img, rnd.randrange(8), rnd.randrange(8), "#ffffff")
    return img


# =================================================================================================
# scenery (16x16)

def pillar(frame=0):
    """A pillar of the tomb: dark stone sheathed in ice, icicles hanging off its capital."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(4, 4, 11, 15), STONE, grad=lambda x, y: 2 if x < 7 else 1)
    cv.shade(poly_mask([(3.5, 15.5), (6, 6), (7, 15.5)]), ICE, rim=False)
    cv.shade(rect_mask(3, 2, 12, 4), STEEL)
    for x in (4, 6, 9, 11):
        line(cv, x, 5, x, 5 + (x * 3 + frame) % 3, FROST[2])
    cv.px(4, 1, SNOW[3]); cv.px(7, 1, SNOW[2]); cv.px(10, 1, SNOW[3])
    return cv.img


def brazier(frame=0):
    """A brazier of black iron burning with the tomb's cold blue fire."""
    cv = Canvas(16, 16)
    cv.shade(poly_mask([(4, 9), (12, 9), (10, 13), (6, 13)]), STEEL_DARK)
    line(cv, 6, 13, 5, 15, STEEL[1]); line(cv, 10, 13, 11, 15, STEEL[1]); line(cv, 8, 13, 8, 15, STEEL[1])
    cv.shade(rect_mask(3, 8, 12, 9), STEEL)
    for k in range(5):
        h = 3 + ((k * 3 + frame) % 4)
        line(cv, 4 + k * 2, 8, 4 + k * 2 + (frame + k) % 2 - 0.5, 8 - h, FROST[(k + frame) % 3 + 1])
    cv.px(8, 2 + frame % 2, "#ffffff")
    sparkle(cv, 6 + frame * 2 % 5, 1, FROST[2])
    return cv.img


def crystals(frame=0):
    """A cluster of ice crystals growing out of the floor (it doesn't block)."""
    cv = Canvas(16, 16)
    for (x0, top, w, ramp) in ((8, 3, 2.2, ICE), (5, 8, 1.6, FROST), (11, 7, 1.6, ICE), (3, 11, 1.2, FROST), (13, 11, 1.1, ICE)):
        cv.shade(poly_mask([(x0 - w, 15), (x0 + w, 15), (x0 + 0.5, top), (x0 - 0.5, top)]), ramp)
    sparkle(cv, 8, 3 + frame % 2, FROST[2])
    return cv.img


def urn(frame=0):
    """A burial urn, frozen to the floor."""
    cv = Canvas(16, 16)
    cv.shade(ell_mask(8, 11, 4.5, 4), WRAP)
    cv.shade(rect_mask(6, 4, 10, 7), WRAP)
    cv.shade(rect_mask(5, 3, 11, 4), GOLD)
    for x in range(4, 13):
        cv.px(x, 10, GOLD[2])
    for x in (5, 9, 11):
        line(cv, x, 14, x, 15, FROST[2])
    cv.px(6, 8, SNOW[3])
    return cv.img


def sarcophagus(stage=0):
    """A sarcophagus of the frozen dead, standing on end, a face carved in its ice. Stage 0 sealed,
    1 cracking (someone came near), 2 open (its dead walked out)."""
    cv = Canvas(16, 16)
    cv.shade(poly_mask([(4, 15.5), (12, 15.5), (13, 4), (11, 1), (5, 1), (3, 4)]), STONE)
    lid = poly_mask([(5, 14.5), (11, 14.5), (12, 4.5), (10.5, 2), (5.5, 2), (4, 4.5)])
    if stage < 2:
        cv.shade(lid, ICE, grad=lambda x, y: 2 if x < 7 else 1)
        # the face in the ice
        for (x, y) in ((6, 5), (9, 5)):
            cv.px(x, y, FROST[3] if stage else ICE_DEEP[1])
        line(cv, 7, 8, 8, 8, ICE_DEEP[1])
        for y in (10, 12):
            line(cv, 6, y, 9, y, ICE[0])
        if stage == 1:
            line(cv, 8, 2, 6, 7, "#ffffff"); line(cv, 6, 7, 9, 11, "#ffffff"); line(cv, 9, 11, 7, 14, FROST[2])
    else:
        cv.shade(lid, ROBE_DARK)
        for k in range(4):
            cv.px(5 + k * 2, 14 - k % 2, ICE[3])
        line(cv, 4, 15, 2, 12, ICE[2]); line(cv, 12, 15, 14, 13, ICE[2])
    return cv.img


def seal(stage=0, frame=0):
    """A Seal of Winter: a great crystal on a plinth, the frost of one of the Court bound in it.
    Stage 0 warded (its keeper lives: a ring of runes round it), 1 bare, 2 cracked."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(3, 13, 12, 15), STONE)
    cv.shade(rect_mask(4, 12, 11, 13), STEEL)
    m = poly_mask([(8, 0), (11.5, 5), (10, 12), (6, 12), (4.5, 5)])
    cv.shade(m, FROST if stage < 2 else ICE, grad=lambda x, y: 2 if x < 8 else 1)
    cv.px(7, 3, "#ffffff"); cv.px(7, 4, FROST[3])
    if stage == 0:
        for k in range(6):
            a = frame * 0.5 + k * math.pi / 3
            cv.px(8 + math.cos(a) * 6.5, 7 + math.sin(a) * 4, AURORA[2 + k % 2])
    if stage == 2:
        line(cv, 8, 1, 7, 6, "#ffffff"); line(cv, 7, 6, 10, 9, "#ffffff"); line(cv, 7, 6, 6, 11, ICE[0])
    return cv.img


def heart_of_winter(stage=0, frame=0):
    """The Heart of Winter in the Court's middle: a frozen star in a cage of ice. Each blow cracks it
    further (stages 0 to 5); broken, the Court wakes."""
    cv = Canvas(16, 16)
    pulse = (0, 1)[frame % 2]
    cv.shade(poly_mask([(8, 0), (14, 7), (8, 15), (2, 7)]), ICE, grad=lambda x, y: 2 if x < 8 else 1)
    orb(cv, 8, 7.5, 2.6 + pulse * 0.4, FROST, glow=FROST[3])
    for k in range(stage):
        a = k * 1.3 + 0.4
        x, y = 8, 7.5
        for _ in range(5):
            x += math.cos(a) * 1.1; y += math.sin(a) * 1.1
            a += 0.5 * (1 if k % 2 else -1)
            cv.px(x, y, "#ffffff")
    return cv.img


def rime_gate(frame=0):
    """A wall of old ice sealing the Court's door, the three seals' sigils in it."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(0, 0, 15, 15), ICE_DEEP, grad=lambda x, y: 2 if (x + y) % 7 < 3 else 1)
    for x in range(16):
        cv.px(x, 0, ICE[3]); cv.px(x, 15, ICE_DEEP[0])
    for k, c in enumerate((STEEL[3], AURORA[2], FROST[2])):
        orb(cv, 3 + k * 5, 7, 1.4, R("#0e2440", c and "#2c5a86", "#8cc8ea", "#ffffff"))
        cv.px(3 + k * 5, 7, c if not isinstance(c, str) else c)
    for k in range(3):
        cv.px(2 + ((frame + k * 5) % 13), 3 + k * 4, "#ffffff")
    return cv.img


def throne(who):
    """One of the Court's thrones, carved of ice: a tall back rising into three spires of crystal,
    its sitter's sigil glowing in it (the Warden's steel, the Oracle's aurora, the Reaver's frost),
    broad arms and a step of snow-dusted ice."""
    cv = Canvas(16, 16)
    c = {"warden": STEEL, "oracle": AURORA, "reaver": FROST}[who]
    # the back and its spires
    cv.shade(poly_mask([(3, 5), (4, 2), (5, 4), (7, 0), (8, 0), (10, 4), (11, 2), (12, 5), (12, 10), (3, 10)]), ICE)
    for x, y in ((4, 2), (7, 0), (8, 0), (11, 2)):
        cv.px(x, y, ICE[3])
    # the sigil
    cv.px(7, 4, c[3]); cv.px(8, 4, c[3]); cv.px(7, 5, c[2]); cv.px(8, 5, c[2]); cv.px(6, 5, c[1]); cv.px(9, 5, c[1])
    cv.px(7, 6, c[2]); cv.px(8, 6, c[1]); cv.px(7, 3, c[1]); cv.px(8, 3, c[2])
    # the seat, the arms, the step
    cv.shade(rect_mask(4, 9, 11, 10), ICE_DEEP)
    cv.shade(rect_mask(2, 8, 3, 12), ICE); cv.shade(rect_mask(12, 8, 13, 12), ICE)
    cv.px(2, 8, ICE[3]); cv.px(13, 8, ICE[2])
    cv.shade(rect_mask(4, 11, 11, 12), ICE)
    cv.shade(rect_mask(2, 13, 13, 14), ICE_DEEP)
    for x in range(2, 14, 2):
        cv.px(x, 13, SNOW[2])
    return cv.img


def statue(frame=0):
    """A knight of the old court, frozen where he stood: a figure under a skin of clear ice."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(3, 13, 12, 15), STONE)
    cv.shade(rect_mask(5, 5, 10, 12), STEEL_DARK)
    cv.shade(ell_mask(7.5, 3.5, 2.3, 2.3), STEEL_DARK)
    line(cv, 11, 2, 11, 12, STEEL[1])
    m = poly_mask([(3, 13), (12, 13), (13, 5), (10, 0), (5, 0), (2, 5)])
    for (x, y) in m:
        r, g, b, a = cv.get(x, y)
        if a:
            cv.px(x, y, (min(255, r + 60), min(255, g + 90), min(255, b + 120)))
        else:
            cv.px(x, y, ICE[2], 110)
    line(cv, 4, 2, 6, 9, ICE[3], 200)
    return cv.img


def bones(frame=0):
    """Bones of those who came before, half under the frost (it doesn't block)."""
    cv = Canvas(16, 16)
    line(cv, 3, 12, 10, 9, BONE[2]); line(cv, 6, 14, 12, 13, BONE[1])
    cv.shade(ell_mask(11, 10, 2, 1.7), BONE)
    cv.px(10, 10, "#000000"); cv.px(12, 10, "#000000")
    for x in range(2, 14, 3):
        cv.px(x, 14, SNOW[2])
    return cv.img


def icefall(frame=0):
    """A waterfall frozen as it fell: columns of ice running down into a heap."""
    cv = Canvas(16, 16)
    for k, x0 in enumerate((3, 6, 9, 12)):
        h = 1 + (k * 3) % 4
        cv.shade(poly_mask([(x0 - 1.6, 15), (x0 + 1.6, 15), (x0 + 1, h), (x0 - 1, h)]), (ICE, FROST)[k % 2])
    cv.shade(ell_mask(8, 14.5, 7, 1.8), SNOW)
    for k in range(3):
        cv.px(4 + k * 4, 3 + (frame + k) % 6 * 2, "#ffffff")
    return cv.img


def lantern(who, lit=False, frame=0):
    """A Seal Lantern by the Sealed Door, one for each seal: a cage of black iron on a post, dark
    until its seal is broken, then burning in its Court member's colour."""
    cv = Canvas(16, 16)
    c = {"warden": STEEL, "oracle": AURORA, "reaver": FROST}[who]
    cv.shade(rect_mask(7, 9, 8, 15), STEEL_DARK)
    cv.shade(rect_mask(5, 14, 10, 15), STEEL_DARK)
    cv.shade(rect_mask(4, 2, 11, 9), R("#06080c", "#0c1018", "#141a26", "#1e2636"), rim=False)
    for x in (4, 7, 11):
        line(cv, x, 2, x, 9, STEEL[1])
    line(cv, 4, 2, 11, 2, STEEL[2]); line(cv, 4, 9, 11, 9, STEEL[1])
    if lit:
        orb(cv, 7.5, 5.5, 2 + frame % 2 * 0.4, c, glow=c[3])
        sparkle(cv, 7, 0, c[2])
    else:
        cv.px(7, 6, c[0]); cv.px(8, 5, c[0])
    return cv.img


def ice_wall(frame=0):
    """A wall of ice raised in a fight (the Glacial Bulwark): jagged, glowing faintly."""
    cv = Canvas(16, 16)
    m = poly_mask([(0, 16), (0, 5), (3, 1), (6, 4), (9, 0), (12, 3), (16, 2), (16, 16)])
    cv.shade(m, ICE, grad=lambda x, y: 2 if (x + y + frame) % 9 < 4 else 1)
    for k in range(3):
        line(cv, 3 + k * 5, 6, 4 + k * 5, 12, ICE[3])
    return cv.img


def rime_pillar(frame=0):
    """A Rime Pillar, which Kaldrun's Bulwark raises: break the four and his ward breaks."""
    cv = Canvas(16, 16)
    cv.shade(poly_mask([(4, 15.5), (12, 15.5), (10.5, 2), (8, 0), (5.5, 2)]), FROST, grad=lambda x, y: 2 if x < 8 else 1)
    orb(cv, 8, 7, 1.6, STEEL, glow=STEEL[3] if frame % 2 else None)
    return cv.img


# =================================================================================================
# monsters (16x16)

def mummy(frame=0, cast=False):
    """A Rimebound Mummy: wrappings stiff with frost, blue light in the eyes, arms out."""
    cv = Canvas(16, 16)
    step = frame % 2
    cv.shade(rect_mask(5, 11, 7, 15 - step), WRAP)
    cv.shade(rect_mask(9, 11, 11, 14 + step), WRAP)
    cv.shade(ell_mask(8, 8.5, 3.6, 4), WRAP)
    cv.shade(ell_mask(8, 3.5, 2.6, 2.6), WRAP)
    for y in (6, 8, 10, 12):
        line(cv, 5, y, 11, y + 1, WRAP[0])
    arm = 2 if cast else 0
    thick(cv, 4, 7, 1, 6 - arm, 2, WRAP); thick(cv, 12, 7, 15, 6 - arm, 2, WRAP)
    cv.px(7, 3, FROST[3]); cv.px(9, 3, FROST[3])
    for (x, y) in ((5, 4), (11, 9), (6, 13)):
        cv.px(x, y, SNOW[3])
    if cast:
        sparkle(cv, 1, 5, FROST[2]); sparkle(cv, 15, 5, FROST[2])
    return cv.img


def wraith(frame=0, cast=False):
    """A Frost Wraith: a hooded shade trailing mist, an icy lantern of a face."""
    cv = Canvas(16, 16)
    bob = frame % 2
    body = poly_mask([(8, 1 + bob), (13, 6 + bob), (14, 13), (11, 11), (9, 15), (7, 12), (4, 15), (2, 11), (3, 6 + bob)])
    cv.shade(body, R("#1c2e48", "#36547a", "#6a90bc", "#b8d8f4"))
    cv.shade(ell_mask(8, 5.5 + bob, 2.4, 2), R("#060a14", "#0c1424", "#18263c", "#24364e"), rim=False)
    cv.px(7, 5 + bob, FROST[3]); cv.px(9, 5 + bob, FROST[3])
    if cast:
        orb(cv, 13, 9, 1.6, FROST, glow=FROST[3])
    for k in range(3):
        cv.px(3 + k * 4, 14 - (frame + k) % 2, SNOW[2], 160)
    return cv.img


def scarab(frame=0, bite=False):
    """A Glacial Scarab: a beetle with a shell of clear ice, a frozen jewel in its back."""
    cv = Canvas(16, 16)
    leg = frame % 2
    for k in range(3):
        y = 7 + k * 2
        line(cv, 4, y, 2, y + (1 if (k + leg) % 2 else -1), STEEL_DARK[2])
        line(cv, 12, y, 14, y + (-1 if (k + leg) % 2 else 1), STEEL_DARK[2])
    cv.shade(ell_mask(8, 9, 4.4, 4.6), ICE)
    line(cv, 8, 5, 8, 13, ICE[0])
    cv.shade(ell_mask(8, 4, 2.2, 1.8), STEEL)
    orb(cv, 8, 9, 1.4, AURORA)
    if bite:
        cv.px(6, 2, "#ffffff"); cv.px(10, 2, "#ffffff")
    return cv.img


def hound(frame=0, bite=False):
    """A Frostfang Hound: a lean white wolf, frost on its breath, eyes of blue fire."""
    cv = Canvas(16, 16)
    stride = frame % 2
    for (lx, back) in ((4, True), (6, False), (10, True), (12, False)):
        reach = (1 if back else -1) * (1 if stride else -1)
        line(cv, lx, 10, lx + reach, 14, PELT[1])
    cv.shade(ell_mask(8, 9, 5.2, 2.8), PELT)
    cv.shade(ell_mask(13, 7, 2.4, 2.1), PELT)
    cv.shade(poly_mask([(12, 5), (13, 2.5), (14, 5)]), PELT)
    cv.px(14, 7, FROST[2]); cv.px(15, 8, PELT[0])
    line(cv, 3, 8, 0, 6 + stride, PELT[2])
    for x in (6, 8, 10):
        cv.px(x, 7, STEEL[2])
    if bite:
        for k in range(3):
            cv.px(15, 8 + k % 2, "#ffffff")
        sparkle(cv, 15, 10, FROST[2])
    return cv.img


def golem(frame=0, slam=False):
    """A Glacier Golem: boulders of blue ice held together by the cold, a light in its chest."""
    cv = Canvas(16, 16)
    step = frame % 2
    cv.shade(rect_mask(4, 11, 6, 15 - step), ICE_DEEP)
    cv.shade(rect_mask(10, 11, 12, 14 + step), ICE_DEEP)
    cv.shade(ell_mask(8, 8, 5, 4.4), ICE)
    cv.shade(ell_mask(8, 3, 2.6, 2.2), ICE)
    lift = 3 if slam else 0
    cv.shade(ell_mask(2.5, 8 - lift, 2.2, 2.6), ICE_DEEP)
    cv.shade(ell_mask(13.5, 8 - lift, 2.2, 2.6), ICE_DEEP)
    orb(cv, 8, 8, 1.6, FROST, glow=FROST[3])
    cv.px(7, 3, FROST[3]); cv.px(9, 3, FROST[3])
    for (x, y) in ((5, 6), (11, 10), (8, 1)):
        cv.px(x, y, SNOW[3])
    return cv.img


def gargoyle(frame=0, fire=False):
    """An Icicle Gargoyle: a crouching stone beast grown over with icicles; it never moves."""
    cv = Canvas(16, 16)
    cv.shade(rect_mask(3, 13, 12, 15), STONE)
    cv.shade(ell_mask(8, 9.5, 4.6, 3.6), STONE)
    cv.shade(ell_mask(8, 5, 3, 2.6), STONE)
    cv.shade(poly_mask([(1, 6), (5, 7), (4, 11)]), STEEL_DARK)
    cv.shade(poly_mask([(15, 6), (11, 7), (12, 11)]), STEEL_DARK)
    cv.px(7, 5, FROST[3] if fire else FROST[1]); cv.px(9, 5, FROST[3] if fire else FROST[1])
    for x in (4, 6, 9, 11):
        line(cv, x, 12, x, 13 + (x + frame) % 2, ICE[3])
    if fire:
        sparkle(cv, 8, 8, FROST[2])
    return cv.img


def wisp(frame=0, flare=False):
    """An Aurora Wisp: a mote of the aurora, green and violet, trailing light."""
    cv = Canvas(16, 16)
    f = frame % 2
    for k in range(4):
        cv.px(8 - k * 2 + f, 12 + (k % 2), AURORA[2] if k % 2 else VIOLET[2], 200 - k * 40)
    orb(cv, 8, 7, 3.4 + (1 if flare else 0), AURORA if f == 0 else R("#2e1258", "#5a2a9a", "#4ae8a4", "#c4ffe4"), glow=VIOLET[3])
    sparkle(cv, 8, 7, AURORA[3])
    return cv.img


def knight(frame=0, swing=False):
    """A Frozen Revenant: a knight of the old court still in its rimed armour, sword and shield."""
    cv = Canvas(16, 16)
    step = frame % 2
    cv.shade(rect_mask(5, 11, 7, 15 - step), STEEL)
    cv.shade(rect_mask(9, 11, 11, 14 + step), STEEL)
    cv.shade(rect_mask(4, 6, 11, 11), STEEL)
    cv.shade(ell_mask(7.5, 3.5, 2.6, 2.6), STEEL)
    line(cv, 6, 3, 9, 3, STEEL_DARK[0]); cv.px(7, 3, FROST[3]); cv.px(8, 3, FROST[3])
    cv.shade(poly_mask([(1, 6), (5, 6), (5, 11), (3, 13), (1, 11)]), ICE)
    if swing:
        thick(cv, 12, 8, 15, 1, 1.6, R("#5ec4e8", "#8cc8ea", "#dcf6ff", "#ffffff"))
    else:
        thick(cv, 12, 9, 13, 15, 1.6, R("#5ec4e8", "#8cc8ea", "#dcf6ff", "#ffffff"))
    for (x, y) in ((4, 6), (10, 7)):
        cv.px(x, y, SNOW[3])
    return cv.img


def serpent(frame=0, surfaced=True):
    """A Rime Serpent: an eel of the black water under the lake; surfaced, its fins of ice."""
    cv = Canvas(16, 16)
    f = frame % 2
    if not surfaced:
        # only the ripple where it waits
        glow_ring(cv, 8, 10, 4 + f, ICE[2], 150)
        cv.px(8, 10, ICE[3])
        return cv.img
    pts = [(2 + k * 1.2, 12 - math.sin(k * 0.9 + f) * 2 - k * 0.6) for k in range(10)]
    for (x, y) in pts:
        cv.shade(ell_mask(x, y, 1.7, 1.7), R("#0e2c4a", "#1c4a72", "#3a78a6", "#8cc8ea"), rim=False)
    hx, hy = pts[-1]
    cv.shade(ell_mask(hx + 1, hy - 1, 2.4, 2), R("#0e2c4a", "#1c4a72", "#3a78a6", "#8cc8ea"))
    cv.px(hx + 2, hy - 2, FROST[3])
    for (x, y) in pts[2:8:2]:
        cv.px(x, y - 2, ICE[3])
    glow_ring(cv, 4, 13, 2.5, ICE[3], 120)
    return cv.img


def colossus(frame=0, slam=False):
    """The Rime Colossus, keeper of the Warden's seal: a giant of glacier ice and dark stone,
    a sarcophagus lid strapped to its arm for a shield, a crown of icicles."""
    cv = Canvas(16, 16)
    step = frame % 2
    cv.shade(rect_mask(3, 11, 6, 15 - step), STONE)
    cv.shade(rect_mask(10, 11, 13, 14 + step), STONE)
    cv.shade(poly_mask([(2, 5), (14, 5), (12, 12), (4, 12)]), ICE_DEEP)
    cv.shade(ell_mask(8, 3, 2.6, 2.3), ICE)
    for x in (6, 8, 10):
        line(cv, x, 0, x, 1, FROST[3])
    cv.shade(poly_mask([(0, 4), (3, 4), (3, 12), (0, 12)]), ICE)
    lift = 2 if slam else 0
    cv.shade(ell_mask(14, 7 - lift, 2, 2.8), STONE)
    orb(cv, 8, 8, 1.6, STEEL, glow=STEEL[3])
    cv.px(7, 3, FROST[3]); cv.px(9, 3, FROST[3])
    return cv.img


def matron(frame=0, cast=False):
    """The Aurora Matron, keeper of the Oracle's seal: a tall veiled shade, the aurora woven
    into her train, a lantern of green fire."""
    cv = Canvas(16, 16)
    f = frame % 2
    cv.shade(poly_mask([(8, 1), (12, 6), (14, 15.5), (2, 15.5), (4, 6)]), ROBE_DARK)
    for k in range(5):
        line(cv, 3 + k * 2.5, 15, 4 + k * 2.2, 9 + (k + f) % 3, (AURORA if k % 2 else VIOLET)[2])
    cv.shade(ell_mask(8, 4, 2.4, 2.4), ROBE, rim=False)
    cv.px(7, 4, AURORA[3]); cv.px(9, 4, AURORA[3])
    orb(cv, 13, 9 - (2 if cast else 0), 1.6, AURORA, glow=AURORA[3])
    line(cv, 13, 10, 13, 13, GOLD[1])
    return cv.img


def alpha(frame=0, bite=False):
    """The Frostfang Alpha, keeper of the Reaver's seal: a great wolf of the pack, its ruff of
    icicles, scars of blue light."""
    cv = Canvas(16, 16)
    stride = frame % 2
    for (lx, back) in ((3, True), (5, False), (10, True), (12, False)):
        reach = (1 if back else -1) * (1 if stride else -1)
        thick(cv, lx, 10, lx + reach, 15, 1.4, PELT)
    cv.shade(ell_mask(8, 8.5, 6, 3.6), PELT)
    cv.shade(ell_mask(13, 6, 2.8, 2.6), PELT)
    cv.shade(poly_mask([(11.5, 4), (12.5, 0.5), (14, 4)]), PELT)
    for x in range(7, 13, 2):
        line(cv, x, 5, x - 1, 3, ICE[3])
    cv.px(14, 6, FROST[3]); cv.px(15, 7, PELT[0])
    line(cv, 4, 7, 7, 10, FROST[2]); line(cv, 6, 7, 9, 10, FROST[2])
    line(cv, 2, 8, 0, 5 + stride, PELT[2])
    if bite:
        for k in range(3):
            cv.px(15, 7 + k, "#ffffff")
    return cv.img


def minions():
    """name -> ([frames], attack frame)"""
    return {
        "Rimebound Mummy": ([mummy(0), mummy(1)], mummy(0, True)),
        "Frost Wraith": ([wraith(0), wraith(1)], wraith(0, True)),
        "Glacial Scarab": ([scarab(0), scarab(1)], scarab(0, True)),
        "Frostfang Hound": ([hound(0), hound(1)], hound(0, True)),
        "Glacier Golem": ([golem(0), golem(1)], golem(0, True)),
        "Icicle Gargoyle": ([gargoyle(0), gargoyle(1)], gargoyle(0, True)),
        "Aurora Wisp": ([wisp(0), wisp(1)], wisp(0, True)),
        "Frozen Revenant": ([knight(0), knight(1)], knight(0, True)),
        "Rime Serpent": ([serpent(0), serpent(1)], serpent(0)),
        "Rime Serpent Below": ([serpent(0, False), serpent(1, False)], serpent(0, False)),
        "Rime Colossus": ([colossus(0), colossus(1)], colossus(0, True)),
        "Aurora Matron": ([matron(0), matron(1)], matron(0, True)),
        "Frostfang Alpha": ([alpha(0), alpha(1)], alpha(0, True)),
    }


# =================================================================================================
# the Frozen Court (32x32)

def kaldrun(frame=0, swing=False, permafrost=False):
    """Kaldrun, the Rime Warden: a hulking knight in blue steel under a horned helm hung with
    icicles, a glacier for a shield on his left arm, a long frost blade in his right. In his
    Permafrost the ice grows out of every seam of the armour in spikes and his eyes blaze white."""
    cv = Canvas()
    f = frame % 3
    bob = (0, 1, 0)[f]
    eye = "#ffffff" if permafrost else FROST[(2, 3, 2)[f]]
    for (x, y) in ell_mask(16, 30.5, 11, 1.6):
        cv.px(x, y, "#000000", 70)
    # legs
    for lx, off in ((10, (0, 1, 0)[f]), (18, (1, 0, 0)[f])):
        cv.shade(rect_mask(lx, 22, lx + 4, 29 - off), STEEL, grad=lambda x, y: 2 if x < lx + 2 else 1)
        cv.shade(rect_mask(lx - 1, 28 - off, lx + 5, 30 - off), STEEL_DARK)
        cv.px(lx + 2, 24, STEEL[3])
    # the blade, behind him unless he swings
    if not swing:
        thick(cv, 26, 28, 28, 4, 2.4, ICE)
        line(cv, 27, 26, 28, 5, ICE[3])
        cv.shade(rect_mask(23, 19, 30, 20), STEEL)
    # body: a cuirass, a tabard of deep blue, pauldrons with ice on them
    cv.shade(poly_mask([(8, 12 + bob), (24, 12 + bob), (23, 23), (9, 23)]), STEEL, grad=lambda x, y: 2 if x < 15 else 1)
    cv.shade(poly_mask([(13, 16 + bob), (19, 16 + bob), (20, 26), (12, 26)]), R("#0e1a3a", "#1c2c5e", "#2e4488", "#4a64b0"))
    for x in range(13, 20):
        cv.px(x, 16 + bob, GOLD[2])
    cv.shade(ell_mask(8, 12.5 + bob, 4, 3.2), STEEL)
    cv.shade(ell_mask(24, 12.5 + bob, 4, 3.2), STEEL)
    for (x0, y0) in ((6, 10 + bob), (9, 10 + bob), (23, 10 + bob), (26, 10 + bob)):
        cv.shade(poly_mask([(x0 - 1, y0 + 1), (x0 + 1, y0 + 1), (x0, y0 - 2)]), ICE, rim=False)
    # helm, horned, icicles hanging from the brow
    cv.shade(ell_mask(16, 7.5 + bob, 4.6, 4.6), STEEL, grad=lambda x, y: 2 if x < 16 else 1)
    cv.shade(poly_mask([(11.5, 6 + bob), (8, 1 + bob), (10, 6 + bob)]), BONE)
    cv.shade(poly_mask([(20.5, 6 + bob), (24, 1 + bob), (22, 6 + bob)]), BONE)
    line(cv, 13, 8 + bob, 19, 8 + bob, STEEL_DARK[0])
    cv.px(14, 8 + bob, eye); cv.px(18, 8 + bob, eye)
    for x in (13, 15, 17, 19):
        line(cv, x, 10 + bob, x, 11 + bob + (x + f) % 2, ICE[3])
    # the glacier shield on his left
    shield = poly_mask([(1, 11), (9, 10), (10, 24), (5, 28), (0, 24)])
    cv.shade(shield, ICE, grad=lambda x, y: 2 if x < 5 else 1)
    for (a, b) in (((2, 14), (7, 20)), ((6, 12), (3, 25))):
        line(cv, a[0], a[1], b[0], b[1], ICE[3])
    orb(cv, 5, 18, 1.6, STEEL, glow=FROST[3] if permafrost else None)
    if swing:
        thick(cv, 23, 16, 31, 2, 2.6, ICE)
        line(cv, 24, 15, 31, 3, "#ffffff")
        sparkle(cv, 30, 3, FROST[2], big=True)
    if permafrost:
        rnd = random.Random(5 + f)
        for _ in range(14):
            x, y = rnd.randint(7, 25), rnd.randint(4, 27)
            if cv.get(x, y)[3]:
                cv.shade(poly_mask([(x - 1, y + 1), (x + 1, y + 1), (x + rnd.choice((-1, 0, 1)), y - 2)]), FROST, rim=False)
        for k in range(3):
            sparkle(cv, 4 + k * 12, 2 + (k + f) % 3, FROST[2])
    return cv.img


def ysolde(frame=0, cast=False, night=False):
    """Ysolde, the Aurora Oracle: a veiled priestess in white and violet, the aurora streaming up
    from her shoulders like a mantle and rippling as she breathes, a staff of ice with a star caught
    in its head. In her Polar Night her robes go dark and the aurora burns violet."""
    cv = Canvas()
    f = frame % 3
    robe = ROBE_DARK if night else ROBE
    lights = (VIOLET, R("#1c0e3a", "#5a2a9a", "#b080ff", "#f0e0ff")) if night else (AURORA, VIOLET)
    for (x, y) in ell_mask(16, 30.5, 9, 1.4):
        cv.px(x, y, "#000000", 60)
    # the aurora mantle: ribbons rising behind her, rippling
    for k in range(7):
        ramp = lights[k % 2]
        x0 = 4 + k * 4
        for t in range(20):
            y = 24 - t
            x = x0 + (x0 - 16) * t * 0.03 + math.sin(t * 0.45 + k + f * 0.9) * (1 + t * 0.1)
            a = max(90, 255 - t * 8)
            cv.px(x, y, ramp[1 + (t // 5) % 3], a)
            cv.px(x + 1, y, ramp[2], a // 2)
    # the gown, flaring to the floor
    cv.shade(poly_mask([(12, 12), (20, 12), (24, 30), (8, 30)]), robe, grad=lambda x, y: 2 if x < 15 else 1)
    for y in range(14, 30, 3):
        cv.px(16 + (y % 2), y, lights[0][2])
    for x in range(9, 24):
        cv.px(x, 29, lights[1][2] if x % 3 else GOLD[2])
    # sleeves and hands
    cv.shade(poly_mask([(12, 13), (8, 19), (10, 21), (13, 17)]), robe)
    cv.shade(poly_mask([(20, 13), (24, 19 - (3 if cast else 0)), (22, 21 - (3 if cast else 0)), (19, 17)]), robe)
    cv.shade(ell_mask(9, 20, 1.3, 1.3), SKIN_COLD)
    # her face under the veil, a circlet of ice
    cv.shade(ell_mask(16, 8, 3.4, 3.8), SKIN_COLD)
    cv.shade(poly_mask([(12, 6), (20, 6), (21, 14), (11, 14)]), robe, rim=False)
    cv.shade(ell_mask(16, 9, 2.4, 2.6), SKIN_COLD, rim=False)
    cv.px(15, 9, lights[0][3]); cv.px(17, 9, lights[0][3])
    for x in range(13, 20):
        cv.px(x, 5, ICE[3] if x % 2 else ICE[2])
    cv.px(16, 3, lights[1][3]); cv.px(14, 4, ICE[3]); cv.px(18, 4, ICE[3])
    # the staff on her left, a star in its head
    line(cv, 8, 28, 7, 6, ICE[1]); line(cv, 9, 28, 8, 6, ICE[2])
    cv.shade(poly_mask([(5, 6), (7.5, 1), (10, 6), (7.5, 8)]), ICE)
    sparkle(cv, 7, 5, lights[0][2], big=cast)
    if cast:
        orb(cv, 23, 15, 2, lights[0], glow=lights[0][3])
    return cv.img


def hrimgar(frame=0, swing=False, shattered=False):
    """Hrimgar, the Frost Reaver: a berserker under a white wolf's pelt (its head his hood), a beard
    of icicles, bare arms gone blue with the cold, an axe of blue ice in each hand. In the
    Shattering the cold splits through him in cracks of white light."""
    cv = Canvas()
    f = frame % 3
    bob = (0, 1, 0)[f]
    for (x, y) in ell_mask(16, 30.5, 10, 1.6):
        cv.px(x, y, "#000000", 70)
    # legs, wrapped in fur
    for lx, off in ((11, (0, 1, 0)[f]), (17, (1, 0, 0)[f])):
        cv.shade(rect_mask(lx, 21, lx + 4, 29 - off), R("#2a2420", "#4a3e34", "#6e5c4c", "#94806a"))
        cv.shade(rect_mask(lx - 1, 27 - off, lx + 5, 30 - off), PELT)
    # body: bare chest under the pelt, a belt of bone
    cv.shade(poly_mask([(10, 12 + bob), (22, 12 + bob), (21, 22), (11, 22)]), SKIN_COLD, grad=lambda x, y: 2 if x < 15 else 1)
    for x in range(11, 22):
        cv.px(x, 21, BONE[2] if x % 2 else BONE[1])
    line(cv, 14, 14 + bob, 13, 19, SKIN_COLD[0]); line(cv, 18, 14 + bob, 19, 19, SKIN_COLD[0])
    # the pelt: over the shoulders and down the back, the wolf's head his hood
    cv.shade(poly_mask([(6, 11 + bob), (26, 11 + bob), (24, 15 + bob), (8, 15 + bob)]), PELT)
    cv.shade(ell_mask(16, 6.5 + bob, 5, 4.4), PELT, grad=lambda x, y: 2 if x < 16 else 1)
    cv.shade(poly_mask([(11, 4 + bob), (12, 0 + bob), (14, 3 + bob)]), PELT)
    cv.shade(poly_mask([(21, 4 + bob), (20, 0 + bob), (18, 3 + bob)]), PELT)
    # his face in the wolf's jaws, eyes of blue fire, the icicle beard
    cv.shade(ell_mask(16, 9 + bob, 2.8, 2.4), SKIN_COLD, rim=False)
    cv.px(15, 8 + bob, FROST[3]); cv.px(17, 8 + bob, FROST[3])
    for x in (14, 15, 16, 17, 18):
        line(cv, x, 11 + bob, x, 12 + bob + (2 - abs(x - 16)) + (x + f) % 2, ICE[3 if x % 2 else 2])
    # arms
    lift = 6 if swing else 0
    thick(cv, 9, 13 + bob, 5, 20 - lift, 2.6, SKIN_COLD)
    thick(cv, 23, 13 + bob, 27, 20 - lift, 2.6, SKIN_COLD)
    # the axes: hafts of dark wood, heads of blue ice
    for (hx, hy, side) in ((5, 20 - lift, -1), (27, 20 - lift, 1)):
        tip = hy - 10
        line(cv, hx, hy + 2, hx, tip, WOOD[2])
        head = poly_mask([(hx, tip), (hx + side * 5, tip - 2), (hx + side * 6, tip + 3), (hx + side * 4, tip + 6), (hx, tip + 4)])
        cv.shade(head, ICE, grad=lambda x, y: 2 if y < tip + 2 else 1)
        line(cv, hx + side * 5, tip - 1, hx + side * 5, tip + 4, ICE[3])
    if swing:
        for (sx, sy) in ((2, 6), (30, 6)):
            sparkle(cv, sx, sy, FROST[2], big=True)
    if shattered:
        rnd = random.Random(11 + f)
        for _ in range(6):
            x, y = rnd.randint(10, 22), rnd.randint(5, 24)
            for _ in range(4):
                if cv.get(x, y)[3]:
                    cv.px(x, y, "#ffffff")
                x += rnd.choice((-1, 1)); y += rnd.choice((-1, 0, 1))
        for k in range(4):
            cv.px(8 + k * 5, 2 + (k + f) % 3, FROST[3])
    return cv.img


def court_sprites():
    """name -> [stand frames x3, attack]; each boss and its last form."""
    return {
        "Kaldrun, the Rime Warden": [kaldrun(0), kaldrun(1), kaldrun(2), kaldrun(0, swing=True)],
        "Kaldrun, Permafrost": [kaldrun(0, permafrost=True), kaldrun(1, permafrost=True), kaldrun(2, permafrost=True), kaldrun(0, True, True)],
        "Ysolde, the Aurora Oracle": [ysolde(0), ysolde(1), ysolde(2), ysolde(0, cast=True)],
        "Ysolde, Polar Night": [ysolde(0, night=True), ysolde(1, night=True), ysolde(2, night=True), ysolde(0, True, True)],
        "Hrimgar, the Frost Reaver": [hrimgar(0), hrimgar(1), hrimgar(2), hrimgar(0, swing=True)],
        "Hrimgar, Shattered": [hrimgar(0, shattered=True), hrimgar(1, shattered=True), hrimgar(2, shattered=True), hrimgar(0, True, True)],
    }


def encased(frame):
    """A Court boss still frozen in its block of ice (before the Heart of Winter breaks)."""
    img = frame.copy()
    cv = Canvas()
    cv.img = img
    m = poly_mask([(5, 31), (27, 31), (29, 8), (24, 1), (8, 1), (3, 8)])
    for (x, y) in m:
        r, g, b, a = img.getpixel((x, y)) if 0 <= x < 32 and 0 <= y < 32 else (0, 0, 0, 0)
        if a:
            img.putpixel((x, y), (int(r * 0.5 + 120), int(g * 0.5 + 170), int(b * 0.5 + 220), 255))
        else:
            img.putpixel((x, y), (120, 180, 230, 120))
    for (a, b) in (((8, 3), (12, 12)), ((22, 4), (26, 20)), ((6, 20), (10, 29))):
        line(cv, a[0], a[1], b[0], b[1], "#ffffff", 200)
    return img


# =================================================================================================
# shots (8x8, pointing right)

def _s8(fn):
    cv = Canvas(8, 8)
    fn(cv)
    return cv.img


def _shard(cv, ramp=ICE):
    cv.shade(poly_mask([(0, 4), (3, 2.5), (8, 4), (3, 5.5)]), ramp)
    cv.px(5, 3, "#ffffff")


def _bolt(cv, ramp=FROST):
    orb(cv, 4.5, 4, 2, ramp, glow=ramp[3])
    cv.px(1, 4, ramp[2], 160); cv.px(0, 4, ramp[1], 100)


def _flake(cv):
    for k in range(3):
        a = k * math.pi / 3
        line(cv, 3.5 - math.cos(a) * 3, 3.5 - math.sin(a) * 3, 3.5 + math.cos(a) * 3, 3.5 + math.sin(a) * 3, FROST[2])
    cv.px(3, 3, "#ffffff"); cv.px(4, 4, "#ffffff")


def _lance(cv):
    line(cv, 0, 4, 5, 4, ICE[2]); line(cv, 0, 3, 4, 3, ICE[1])
    cv.shade(poly_mask([(4, 1.5), (8, 4), (4, 6.5)]), FROST)


def _axe(cv):
    line(cv, 1, 6, 5, 2, WOOD[2])
    cv.shade(poly_mask([(4, 0), (8, 1), (7, 5), (4, 3)]), ICE)


def _ribbon(cv, ramp):
    for x in range(8):
        y = 4 + math.sin(x * 0.9) * 2
        cv.px(x, y, ramp[1 + x % 3]); cv.px(x, y - 1, ramp[2], 140)


def _hail(cv):
    orb(cv, 4, 4, 2.6, R("#8ca4c0", "#bcd0e4", "#e8f2fc", "#ffffff"))


def _fang(cv):
    cv.shade(poly_mask([(0, 2), (8, 4), (0, 6), (2, 4)]), BONE)


def _wisp(cv):
    orb(cv, 4.5, 4, 1.8, R("#1c2e48", "#36547a", "#8cb0d8", "#e0f0ff"), glow="#b8d8f4")
    cv.px(1, 3, "#b8d8f4", 140); cv.px(0, 5, "#b8d8f4", 90)


def _boulder(cv):
    cv.shade(ell_mask(4, 4, 3.4, 3), ICE_DEEP)
    cv.px(3, 2, ICE[3]); cv.px(5, 5, ICE[2])


def _soul(cv):
    orb(cv, 4.5, 4, 2.2, R("#2a2a5a", "#5a5aa8", "#a8a8f0", "#ffffff"), glow="#d0d0ff")


SHOTS = {
    "Rime Shard": lambda: _s8(_shard),
    "Frost Bolt": lambda: _s8(_bolt),
    "Tomb Snowflake": lambda: _s8(_flake),
    "Glacial Lance": lambda: _s8(_lance),
    "Frost Axe": lambda: _s8(_axe),
    "Aurora Ribbon": lambda: _s8(lambda cv: _ribbon(cv, AURORA)),
    "Violet Ribbon": lambda: _s8(lambda cv: _ribbon(cv, VIOLET)),
    "Aurora Spark": lambda: _s8(lambda cv: _bolt(cv, AURORA)),
    "Hailstone": lambda: _s8(_hail),
    "Frost Fang": lambda: _s8(_fang),
    "Wraith Wisp": lambda: _s8(_wisp),
    "Ice Boulder": lambda: _s8(_boulder),
    "Frozen Soul": lambda: _s8(_soul),
    "Steel Shard": lambda: _s8(lambda cv: _shard(cv, STEEL)),
}


# =================================================================================================
# items

def item_icon(name):
    """The white bags' icons (8x8)."""
    cv = Canvas(8, 8)
    if name == "Bulwark of the Rime Warden":
        cv.shade(poly_mask([(1, 0.5), (7, 0.5), (7, 4), (4, 7.5), (1, 4)]), ICE)
        orb(cv, 4, 3, 1, STEEL)
    elif name == "Oracle's Aurora Veil":
        for k in range(4):
            _col = (AURORA, VIOLET)[k % 2]
            for y in range(8):
                cv.px(1 + k * 2 + math.sin(y * 0.8 + k) * 0.8, y, _col[1 + y % 3])
        cv.px(4, 0, "#ffffff")
    else:  # Reaver's Twin Axes
        line(cv, 1, 7, 6, 2, WOOD[2]); line(cv, 6, 7, 1, 2, WOOD[2])
        cv.shade(poly_mask([(5, 0), (8, 1), (7, 3)]), ICE); cv.shade(poly_mask([(2, 0), (0, 1), (1, 3)]), ICE)
    return cv.img


# The Platinum Sword (the Court's treasure; its XML is DoomedRealms.xml's): laid out as the tiered
# swords are (lofiObj5new's Sword of Acclaim: the point at the lower left, a crossguard across the
# blade and the grip up to the right), in the tomb's colours. A platinum blade lit along its upper
# edge, rime over the point, a guard of the Court's teal with a frost gem at its heart, a grip of
# its dark steel and a frost pommel.
PLATINUM_SWORD = [
    "......gp",
    "..tl.gbg",
    "....lbg.",
    "...wcl..",
    "..wsd.l.",
    ".wsd..t.",
    "fsd.....",
    "ft......",
]
PLATINUM_SWORD_COLOURS = {
    "w": "#ffffff", "s": "#c4d2e6", "d": "#6e84aa",    # the blade: edge, flat, back
    "f": "#a8f0ff",                                      # rime on the point
    "t": "#0f7699", "l": "#5ec4e8", "c": "#00e5e5",     # the guard: ends, arms, gem
    "g": "#1c2636", "b": "#5c7494", "p": "#aaffff",     # the grip, its wrap, the pommel
}


def platinum_sword():
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    for y, row in enumerate(PLATINUM_SWORD):
        for x, ch in enumerate(row):
            if ch != ".":
                h = PLATINUM_SWORD_COLOURS[ch]
                img.putpixel((x, y), (int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16), 255))
    return img


def spectral_court(frames, tint):
    return [spectral(f, tint) for f in frames]
