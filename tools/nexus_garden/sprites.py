"""The Nexus garden's scenery, drawn from shapes rather than by hand so it can be changed: each
shape is shaded as if lit from the upper left, in a few steps of its own ramp, ordered-dithered
between them, and edged with a darker shade of itself. Sizes are in the game's pixels (a tile is
eight), so everything sits with the characters and the tiles at the same scale."""
import math

from .px import hexc as C, canvas, dither, get, hash2, mix, outline, put, sphere_light

# --- ramps (dark to light) -----------------------------------------------------------------------

BLOSSOM = [C("#6e2c52"), C("#9a4472"), C("#c86492"), C("#e98cb2"), C("#f8b6cf"), C("#ffdbe8"), C("#fff3f7")]
BARK = [C("#2a1a18"), C("#3e2820"), C("#56382a"), C("#704a34"), C("#8a6044")]
PINE = [C("#142a22"), C("#1e3c2c"), C("#2a5236"), C("#3a6a42"), C("#548552"), C("#76a266")]
MAPLE = [C("#4a1214"), C("#7a1e1a"), C("#a8301e"), C("#d24e24"), C("#ee7a34"), C("#f8a85a")]
LEAF = [C("#1c3a24"), C("#28502e"), C("#386a3a"), C("#4e8646"), C("#6aa458"), C("#90c070")]
BAMBOO = [C("#2a4a26"), C("#3c6630"), C("#56843a"), C("#76a24a"), C("#a2c46a")]
STONE = [C("#3c3a44"), C("#56545e"), C("#72707a"), C("#8e8c96"), C("#aaa8b0"), C("#c8c6cc")]
MOSS = [C("#2c4a2a"), C("#3e6436"), C("#56804a"), C("#74a060")]
WOOD = [C("#3a2418"), C("#543422"), C("#70482e"), C("#8c5e3c"), C("#a8784e")]
LACQUER = [C("#4a1210"), C("#7a1e18"), C("#a82e22"), C("#d0462e"), C("#ee6a48")]
PAPER = [C("#7a1a1e"), C("#a82a26"), C("#d24a32"), C("#f07a48"), C("#ffb070")]
GLOW = [C("#c06a1e"), C("#f0a040"), C("#ffd27a"), C("#fff0b8")]
WATER = [C("#1e5a74"), C("#2e86a0"), C("#5ab0c8"), C("#a8e2ee"), C("#e8fbff")]
STRAW = [C("#5a4422"), C("#7a5e30"), C("#9c7c40"), C("#bc9c58"), C("#d8bc78")]
WHITE = [C("#8a8a94"), C("#b4b4bc"), C("#d8d8de"), C("#f4f4f6")]
CERAMIC = [C("#1a2a44"), C("#26406a"), C("#3a5c92"), C("#6488c0")]

LIGHT = (-0.6, -0.65, 0.5)


def darker(ramp):
    """The outline colour for a shape in `ramp`: a step under its darkest, toward violet black."""
    return mix(ramp[0], C("#120a18"), 0.45)


def edge(img, ramp=None):
    outline(img, lambda c: mix(c, C("#140c1a"), 0.62) if ramp is None else darker(ramp))


# --- shapes --------------------------------------------------------------------------------------

def column(img, x0, x1, y0, y1, ramp, top=0.12, bottom=-0.12, base=0.0):
    """An upright box or post: lit on its left, shaded on its right, a lit top row."""
    w = max(1, x1 - x0)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            t = 0.78 - 0.55 * (x - x0) / w + base
            if y == y0:
                t += top
            elif y == y1:
                t += bottom
            put(img, x, y, dither(ramp, t, x, y))


def slab(img, x0, x1, y0, y1, ramp, base=0.0):
    """A flat-topped block seen from above and in front: its top lit, its face darker."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            t = (0.85 if y == y0 else 0.5) - 0.3 * (x - x0) / max(1, x1 - x0) + base
            put(img, x, y, dither(ramp, t, x, y))


def ball(img, cx, cy, rx, ry, ramp, light=LIGHT, base=0.0, noise=0.0, seed=0, keep=None):
    """A lit ellipsoid. `keep(x, y)` limits where it's drawn."""
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if keep and not keep(x, y):
                continue
            dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            v = sphere_light(dx, dy, 1.0, light)
            if v is None:
                continue
            v = 0.12 + v * 0.95 + base + noise * (hash2(x, y, seed) - 0.5)
            put(img, x, y, dither(ramp, v, x, y))


def line(img, x0, y0, x1, y1, colour_at, width=1):
    """A line from (x0, y0) to (x1, y1); `colour_at(x, y, t)` colours each pixel."""
    n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
    for i in range(n + 1):
        t = i / n
        x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
        for k in range(width):
            put(img, int(round(x)) + k, int(round(y)), colour_at(int(round(x)) + k, int(round(y)), t))


# --- trees ---------------------------------------------------------------------------------------

def _trunk(img, x, y_base, y_top, width, branches, seed):
    """A trunk widening into roots at its foot, and branches as tapering lines."""
    for y in range(y_top, y_base + 1):
        f = (y - y_top) / max(1, y_base - y_top)
        w = width + (2 if y >= y_base - 1 else 0) + (1 if f > 0.75 else 0)
        sway = int(round(math.sin(f * 2.4 + seed) * 0.8))
        x0 = x - w // 2 + sway
        for i in range(w):
            t = 0.75 - 0.6 * i / max(1, w - 1) + 0.15 * (hash2(i, y, seed) - 0.5)
            if hash2(x0 + i, y // 2, seed + 3) > 0.8:
                t -= 0.25          # a crack in the bark
            put(img, x0 + i, y, dither(BARK, t, x0 + i, y))
    for bx0, by0, bx1, by1, bw in branches:
        line(img, bx0, by0, bx1, by1, lambda px_, py_, t: dither(BARK, 0.6 - 0.3 * t, px_, py_), bw)


def _canopy(img, clusters, ramp, seed, light=LIGHT, crown=None, holes=0.0, flowers=None):
    """Clusters of foliage (cx, cy, rx, ry), back to front: each a lit lump, also lit by where it
    sits in the whole crown, with a few gaps and, for leaves, flowers."""
    cx0, cy0, crx, cry = crown
    for i, (cx, cy, rx, ry) in enumerate(clusters):
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
                # a ragged edge: leaves and blossom, not a ball
                r = 1.0 + 0.22 * (hash2(x, y, seed + i) - 0.5)
                v = sphere_light(dx / r, dy / r, 1.0, light)
                if v is None:
                    continue
                if holes and hash2(x, y, seed + 50) < holes and (dx * dx + dy * dy) > 0.45:
                    continue
                g = sphere_light((x + 0.5 - cx0) / crx * 0.85, (y + 0.5 - cy0) / cry * 0.85, 1.0, light)
                g = 0.5 if g is None else g
                t = 0.08 + v * 0.62 + g * 0.42 + 0.14 * (hash2(x, y, seed + 7) - 0.5)
                put(img, x, y, dither(ramp, t, x, y))
                if flowers and hash2(x, y, seed + 21) > 0.93 and v > 0.3:
                    put(img, x, y, flowers[int(hash2(y, x, seed) * len(flowers)) % len(flowers)])


def _falling(img, ramp, count, seed, region):
    x0, y0, x1, y1 = region
    for i in range(count):
        x = int(x0 + hash2(i, 1, seed) * (x1 - x0))
        y = int(y0 + hash2(i, 2, seed) * (y1 - y0))
        if get(img, x, y)[3] == 0:
            put(img, x, y, ramp[4 + i % 2])


def sakura(seed, size=32):
    """A cherry tree in full bloom: a dark twisting trunk, its crown in clusters of blossom."""
    img = canvas(size)
    s = size / 32
    cx = size // 2
    rnd = lambda i, k: hash2(i, k, seed)
    branches = [(cx, int(21 * s), int(cx - 8 * s), int(13 * s), 2), (cx, int(20 * s), int(cx + 8 * s), int(12 * s), 2),
                (cx, int(19 * s), cx + int(1 * s), int(7 * s), 2)]
    _trunk(img, cx, size - 1, int(17 * s), max(3, int(3 * s)), branches, seed)
    clusters = []
    n = int(16 * s * s)
    for i in range(n):
        a = rnd(i, 1) * math.tau
        d = math.sqrt(rnd(i, 2))
        x = cx + math.cos(a) * d * 11 * s
        y = 11 * s + math.sin(a) * d * 7 * s
        clusters.append((x, y, (3.2 + rnd(i, 3) * 2.6) * s, (2.8 + rnd(i, 4) * 2.0) * s))
    clusters.sort(key=lambda c: c[1])
    _canopy(img, clusters, BLOSSOM, seed, crown=(cx, 11 * s, 14 * s, 10 * s), holes=0.04)
    edge(img, BLOSSOM)
    _falling(img, BLOSSOM, int(6 * s), seed, (2, int(18 * s), size - 2, size - 2))
    return img


def weeping_sakura(seed):
    """A shidarezakura, 32 by 32: a dome of blossom on arching limbs, and from its rim a curtain
    of blossom falling nearly to the ground, longest on the outside, open in the middle where the
    trunk shows. The curtain is a shaded backing with lit tresses over it, so it reads as one
    mass of hanging blossom rather than separate lines; their tips break into single flowers."""
    size = 32
    img = canvas(size)
    cx = size // 2
    rnd = lambda i, k: hash2(i, k, seed)
    crown_y, crown_rx, crown_ry = 9, 11.5, 6.0
    _trunk(img, cx, size - 1, 13, 3, [(cx, 17, cx - 7, 9, 2), (cx, 16, cx + 7, 8, 2), (cx, 15, cx, 5, 2)], seed)
    opening = 3.2          # the gap round the trunk, half its width

    def top_of(x):
        """Where the curtain hangs from over column x: under the dome, lower toward the trunk."""
        d = abs(x - cx) / (crown_rx + 1.5)
        return crown_y + 1 + 4 * math.sqrt(max(0.0, 1 - d * d))

    def hem_of(x):
        """How far down it reaches: longest on the outside, as a fountain falls."""
        d = min(1.0, abs(x - cx) / (crown_rx + 1.5))
        return min(size - 3.0, 19 + 8 * d * d + 1.5 * math.sin(x * 1.3 + seed))

    # the tresses, back ones first: each sways out a little as it falls, lit on the left
    tresses = []
    x = cx - crown_rx - 0.5
    i = 0
    while x < cx + crown_rx + 1:
        if abs(x - cx) >= opening:
            tresses.append((x + (rnd(i, 43) - 0.5) * 0.6, i))
        x += 1.0 + rnd(i, 44) * 0.8
        i += 1
    # the backing: the curtain's shaded inside, so no ground shows between the tresses
    for x in range(int(cx - crown_rx - 1), int(cx + crown_rx + 2)):
        if abs(x + 0.5 - cx) < opening:
            continue
        top, bottom = top_of(x + 0.5), hem_of(x + 0.5) - 3
        for y in range(int(top), int(bottom) + 1):
            f = (y - top) / max(1.0, bottom - top)
            t = 0.3 - 0.2 * f - 0.08 * (x - cx) / crown_rx + 0.06 * (rnd(x, y) - 0.5)
            put(img, x, y, dither(BLOSSOM, t, x, y))
    for x0, i in tresses:
        out = (x0 - cx) / crown_rx
        top = top_of(x0)
        length = hem_of(x0) - top + (rnd(i, 45) - 0.4) * 4
        lit = rnd(i, 46) > 0.35          # some tresses hang behind, in shade
        for k in range(int(length) + 1):
            f = k / max(1.0, length)
            px_ = int(round(x0 + out * f * f * 2.2))
            py_ = int(top + k)
            if py_ >= size - 2:
                break
            if f > 0.7 and rnd(i, k + 60) > 1.15 - f:
                continue                   # the tip breaking into single flowers
            t = (0.82 if lit else 0.52) - 0.36 * f - 0.14 * out + 0.12 * (rnd(px_, py_ + 9) - 0.5)
            if rnd(i * 7, k) > 0.84:
                t += 0.2                   # a flower catching the light
            put(img, px_, py_, dither(BLOSSOM, t, px_, py_))
    # the dome over the curtain's top
    clusters = []
    for i in range(15):
        a = rnd(i, 1) * math.tau
        d = math.sqrt(rnd(i, 2))
        clusters.append((cx + math.cos(a) * d * 9.5, crown_y - 1 + math.sin(a) * d * 4.5,
                         3.0 + rnd(i, 3) * 2.4, 2.6 + rnd(i, 4) * 1.6))
    clusters.sort(key=lambda c: c[1])
    _canopy(img, clusters, BLOSSOM, seed, crown=(cx, crown_y, crown_rx + 2, crown_ry + 3), holes=0.03)
    edge(img, BLOSSOM)
    _falling(img, BLOSSOM, 6, seed, (1, 20, size - 1, size - 2))
    return img


def pine(seed, size=32):
    """A black pine pruned into cloud pads on a leaning trunk."""
    img = canvas(size)
    cx = size // 2
    branches = [(cx, 22, cx - 9, 16, 2), (cx + 1, 18, cx + 10, 13, 2), (cx, 14, cx - 4, 6, 2)]
    _trunk(img, cx + 1, size - 1, 12, 3, branches, seed)
    pads = [(cx - 9, 15, 6.5, 3.2), (cx + 10, 12, 6.0, 3.0), (cx - 3, 6, 6.5, 3.4), (cx + 4, 8, 4.5, 2.6),
            (cx - 2, 19, 4.0, 2.2)]
    _canopy(img, pads, PINE, seed, crown=(cx, 11, 14, 9), holes=0.02)
    edge(img, PINE)
    return img


def maple(seed, size=32):
    """A Japanese maple in red, its crown wide and soft."""
    img = canvas(size)
    cx = size // 2
    branches = [(cx, 20, cx - 9, 13, 2), (cx, 19, cx + 9, 12, 2)]
    _trunk(img, cx, size - 1, 16, 3, branches, seed)
    clusters = []
    for i in range(18):
        a = hash2(i, 1, seed) * math.tau
        d = math.sqrt(hash2(i, 2, seed))
        clusters.append((cx + math.cos(a) * d * 11, 11 + math.sin(a) * d * 7, 3.2 + hash2(i, 3, seed) * 2.4,
                         2.8 + hash2(i, 4, seed) * 1.8))
    clusters.sort(key=lambda c: c[1])
    _canopy(img, clusters, MAPLE, seed, crown=(cx, 11, 14, 10), holes=0.05)
    edge(img, MAPLE)
    _falling(img, MAPLE, 5, seed, (2, 18, size - 2, size - 2))
    return img


def young_sakura(seed):
    """A sapling for the island's corners and the paths' sides: 16 by 16."""
    img = canvas(16)
    _trunk(img, 8, 15, 9, 2, [(8, 10, 4, 6, 1), (8, 10, 12, 6, 1)], seed)
    clusters = [(4, 6, 3.0, 2.6), (11, 6, 3.0, 2.6), (8, 4, 3.6, 3.0), (6, 8, 2.6, 2.0), (10, 8, 2.6, 2.0)]
    _canopy(img, clusters, BLOSSOM, seed, crown=(8, 6, 7, 5))
    edge(img, BLOSSOM)
    return img


# --- garden pieces (16 by 16 unless said) --------------------------------------------------------

def stone_lantern(lit):
    """A tōrō: a finial, a roof with upturned corners, the light box, a deck, a post and a foot."""
    img = canvas(16)
    column(img, 4, 11, 14, 15, STONE)                     # the foot
    column(img, 6, 9, 10, 13, STONE)                      # the post
    slab(img, 4, 11, 9, 9, STONE)                         # the deck under the light
    column(img, 5, 10, 6, 8, STONE, top=0.0)              # the light box
    for x in (7, 8):                                      # its window
        for y in (6, 7, 8):
            put(img, x, y, GLOW[3 if (lit and y == 7) else 2] if lit else GLOW[1])
    put(img, 6, 7, GLOW[1] if lit else GLOW[0])
    for i, y in enumerate(range(3, 6)):                   # the roof, wider each row
        x0, x1 = 6 - i * 2 + (1 if i == 0 else 0), 9 + i * 2 - (1 if i == 0 else 0)
        for x in range(x0, x1 + 1):
            t = 0.85 - 0.5 * (x - x0) / max(1, x1 - x0) - 0.12 * i
            put(img, x, y, dither(STONE, t, x, y))
    put(img, 1, 4, STONE[3])
    put(img, 14, 4, STONE[2])                             # the upturned corners
    for x in range(3, 13):                                # moss on the roof
        if hash2(x, 3, 5) > 0.55:
            put(img, x, 4 if x in (3, 12) else 3, MOSS[2 + (x % 2)])
    column(img, 7, 8, 1, 2, STONE)                        # the finial
    put(img, 7, 0, STONE[4])
    edge(img)
    return img


def paper_lantern(lit, left=False):
    """A red paper lantern hung from a wooden post's arm, to the right of the post, or with
    `left` to its left (for the right-hand end of a row, hanging in toward it); lit from the
    upper left either way."""
    img = canvas(16)
    px0 = 11 if left else 3
    column(img, px0, px0 + 1, 2, 15, WOOD)                # the post
    for x in range(4, 13) if left else range(3, 12):      # its arm
        put(img, x, 2, dither(WOOD, 0.75, x, 2))
    cx, cy = (6, 8) if left else (10, 8)
    put(img, cx, 3, WOOD[1])
    for y in range(4, 13):                                # the lantern: ribbed paper
        half = [2, 3, 4, 4, 4, 4, 4, 3, 2][y - 4]
        for x in range(cx - half, cx + half):
            t = 0.75 - 0.5 * (x - (cx - half)) / (2 * half) + (0.2 if lit else -0.1)
            if (y - 4) % 2 == 1:
                t -= 0.18                                 # a rib
            if lit and abs(x - cx + 0.5) < 1.5 and 6 <= y <= 10:
                t += 0.3
            put(img, x, y, dither(PAPER, t, x, y))
    for x in range(cx - 2, cx + 2):                       # its black caps
        put(img, x, 3, C("#1e1414"))
        put(img, x, 13, C("#1e1414"))
    if lit:
        put(img, cx - 1, 8, GLOW[3])
    column(img, px0 - 1, px0 + 2, 14, 15, STONE)          # a stone at the foot
    edge(img)
    return img


def bamboo(seed):
    """A clump of bamboo: stalks ringed at their nodes, sprays of leaves high up."""
    img = canvas(16)
    stalks = [(2, 3), (5, 0), (8, 2), (11, 0), (13, 4)]
    for i, (x, top) in enumerate(stalks):
        for y in range(top, 16):
            node = (y + i * 2) % 5 == 0
            for k in range(2):
                t = (0.95 if k == 0 else 0.55) - (0.35 if node else 0) + 0.1 * (hash2(x, y, seed) - 0.5)
                put(img, x + k, y, dither(BAMBOO, t, x + k, y))
    for i in range(26):                                   # the leaves: slim, drooping
        x = int(hash2(i, 1, seed) * 15)
        y = int(hash2(i, 2, seed) * 10)
        d = 1 if hash2(i, 3, seed) > 0.5 else -1
        for k in range(4):
            put(img, x + k * d, y + k // 2, LEAF[5 - k] if k < 3 else LEAF[2])
    edge(img, BAMBOO)
    return img


def azalea(seed, petals=BLOSSOM):
    """A clipped azalea bush in flower."""
    img = canvas(16)
    lumps = [(4.5, 11, 4.0, 3.5), (11.5, 11, 4.0, 3.5), (8, 8.5, 5.0, 4.5), (8, 12, 5.5, 3.5)]
    _canopy(img, lumps, LEAF, seed, crown=(8, 10, 7, 5), flowers=[petals[4], petals[5], petals[3]])
    for i in range(16):                                   # more flowers on the lit side
        x, y = int(2 + hash2(i, 1, seed) * 12), int(5 + hash2(i, 2, seed) * 9)
        if get(img, x, y)[3] and hash2(i, 3, seed) > 0.3:
            put(img, x, y, petals[4 if (x + y) % 3 else 5])
    edge(img, LEAF)
    return img


def boulder(seed):
    """A moss-topped garden rock."""
    img = canvas(16)
    ball(img, 8, 10.5, 6.5, 5.0, STONE, noise=0.18, seed=seed)
    ball(img, 5, 12, 3.5, 3.0, STONE, noise=0.18, seed=seed + 1)
    for x in range(2, 14):                                 # moss over the top
        for y in range(5, 10):
            p = get(img, x, y)
            if p[3] and (get(img, x, y - 1)[3] == 0 or get(img, x, y - 2)[3] == 0) and hash2(x, y, seed) > 0.3:
                put(img, x, y, MOSS[2 + (hash2(x, y, seed + 1) > 0.6)])
    edge(img)
    return img


def basin(frame):
    """A tsukubai: a stone water basin fed by a bamboo spout, a ladle across it. The water's glint
    moves between the frames. The healing fountains of the spawn."""
    img = canvas(16)
    ball(img, 8, 11, 6.5, 4.0, STONE, noise=0.1, seed=3)
    for y in range(9, 13):                                 # the water in its bowl
        for x in range(4, 13):
            if ((x + 0.5 - 8) / 4.0) ** 2 + ((y + 0.5 - 10.5) / 1.8) ** 2 <= 1:
                put(img, x, y, dither(WATER, 0.35 + 0.15 * math.sin(x * 0.9 + frame * 1.6), x, y))
    gx = 6 + frame * 2
    put(img, gx, 10, WATER[4])
    put(img, gx + 1, 10, WATER[3])
    for x in range(10, 16):                                # the spout, on a post
        put(img, x, 6, dither(BAMBOO, 0.8, x, 6))
        put(img, x, 7, dither(BAMBOO, 0.4, x, 7))
    column(img, 14, 15, 6, 13, BAMBOO)
    put(img, 10, 8, WATER[3])
    put(img, 10, 9 + frame % 2, WATER[4])                 # the falling water
    for x in range(2, 9):                                  # the ladle's handle
        put(img, x, 8, WOOD[3] if x < 8 else WOOD[2])
    put(img, 2, 9, WOOD[2])
    edge(img)
    return img


def bench():
    img = canvas(16)
    for x in range(1, 15):
        put(img, x, 8, dither(WOOD, 0.9, x, 8))
        put(img, x, 9, dither(WOOD, 0.6, x, 9))
        put(img, x, 10, dither(WOOD, 0.3, x, 10))
    for x in (2, 12):
        column(img, x, x + 1, 11, 14, WOOD)
    edge(img)
    return img


def well():
    """A stone well under a little roof, its bucket on the rim."""
    img = canvas(16)
    ball(img, 8, 12.5, 6.0, 3.0, STONE, noise=0.12, seed=9)
    for x in range(4, 13):
        for y in range(11, 14):
            if ((x + 0.5 - 8) / 4.2) ** 2 + ((y + 0.5 - 12) / 1.3) ** 2 <= 1:
                put(img, x, y, C("#0e1a24") if y > 11 else C("#1e3444"))
    for x in (2, 13):
        column(img, x, x + 1, 3, 12, WOOD)
    for i, y in enumerate((1, 2, 3)):
        for x in range(1 - i + 1, 15 + i - 1):
            put(img, x, y, dither(WOOD, 0.85 - 0.25 * i, x, y))
    line(img, 8, 4, 8, 9, lambda x, y, t: C("#8a7a5a"))
    column(img, 7, 9, 9, 10, WOOD)
    edge(img)
    return img


def barrels():
    """Sake casks bound in straw, stacked two on one, with red labels."""
    img = canvas(16)

    def cask(cx, cy):
        for y in range(cy - 3, cy + 4):
            for x in range(cx - 3, cx + 3):
                t = 0.8 - 0.55 * (x - cx + 3) / 6 + (0.1 if y == cy - 3 else 0)
                put(img, x, y, dither(STRAW, t, x, y))
        for x in range(cx - 3, cx + 3):
            put(img, x, cy - 2, dither(WOOD, 0.4, x, cy - 2))
            put(img, x, cy + 2, dither(WOOD, 0.4, x, cy + 2))
        for y in range(cy - 1, cy + 2):
            for x in range(cx - 1, cx + 1):
                put(img, x, y, LACQUER[3] if x == cx - 1 else LACQUER[2])
    cask(4, 12)
    cask(11, 12)
    cask(8, 6)
    edge(img)
    return img


def rice_bags():
    """Tawara: rice bales of straw, roped."""
    img = canvas(16)
    for cx, cy in ((5, 12), (11, 12), (8, 8)):
        ball(img, cx, cy, 3.6, 2.6, STRAW, seed=cx)
        for y in range(cy - 2, cy + 3):
            put(img, cx - 2, y, STRAW[1])
            put(img, cx + 2, y, STRAW[1])
    edge(img)
    return img


def bonsai():
    """A pine bonsai in a blue glazed pot."""
    img = canvas(16)
    for y in range(12, 16):
        for x in range(4 - (y == 12), 12 + (y == 12)):
            put(img, x, y, dither(CERAMIC, 0.8 - 0.5 * (x - 4) / 8 + (0.2 if y == 12 else 0), x, y))
    line(img, 8, 12, 6, 7, lambda x, y, t: BARK[2], 1)
    line(img, 7, 9, 11, 6, lambda x, y, t: BARK[3], 1)
    _canopy(img, [(5, 6, 3.4, 2.0), (11, 5, 3.4, 2.0), (8, 3, 3.0, 1.8)], PINE, 5, crown=(8, 5, 6, 3))
    edge(img)
    return img


def shrine():
    """A hokora: a small wayside shrine on a stone footing, an offering before it. The memorial's."""
    img = canvas(16)
    column(img, 3, 12, 13, 15, STONE)
    column(img, 4, 11, 7, 12, WOOD)
    for y in range(8, 12):                                 # its open front, dark inside
        for x in range(6, 10):
            put(img, x, y, C("#1a1014"))
    put(img, 7, 10, GLOW[2])
    put(img, 8, 10, GLOW[1])
    for i, y in enumerate((3, 4, 5, 6)):                   # its roof, vermilion-edged
        for x in range(6 - i * 2 + 1, 10 + i * 2 - 1):
            put(img, x, y, dither(CERAMIC if i < 3 else LACQUER, 0.85 - 0.5 * (x - 6 + i * 2) / (4 * i + 4), x, y))
    put(img, 1, 6, LACQUER[3])
    put(img, 14, 6, LACQUER[2])
    for x in range(5, 11):                                 # a shimenawa rope with paper streamers
        put(img, x, 7, STRAW[4])
    put(img, 6, 8, WHITE[3])
    put(img, 9, 8, WHITE[3])
    edge(img)
    return img


def grave(seed):
    """A memorial stone: a tall polished stele on a stepped base, incense before it."""
    img = canvas(16)
    column(img, 3, 12, 13, 15, STONE)
    column(img, 5, 10, 11, 12, STONE)
    column(img, 6, 9, 2, 10, STONE, top=0.2)
    for y in range(4, 9):                                  # the carved name
        put(img, 7, y, STONE[1] if y % 2 else STONE[0])
    put(img, 4, 12, GLOW[2])
    put(img, 4, 11, WHITE[2])                              # an incense stick's smoke
    put(img, 4, 10, WHITE[1])
    put(img, 11, 12, BLOSSOM[4])
    put(img, 12, 12, BLOSSOM[3])                           # flowers left for it
    edge(img)
    return img


# --- small pieces, 8 by 8 ------------------------------------------------------------------------

def tuft(seed):
    img = canvas(8)
    for i in range(5):
        x = 1 + int(hash2(i, 1, seed) * 6)
        h = 2 + int(hash2(i, 2, seed) * 4)
        for k in range(h):
            put(img, x + (k > 2) * (1 if i % 2 else -1), 7 - k, LEAF[2 + (k > 1) + (k > 3)])
    if seed % 2:
        put(img, 3, 3, BLOSSOM[5])
        put(img, 5, 4, BLOSSOM[4])
    return img


def flowers(seed, colours):
    """A patch of small flowers on the ground (drawn flat)."""
    img = canvas(8)
    for i in range(7):
        x, y = int(hash2(i, 1, seed) * 7), int(hash2(i, 2, seed) * 7)
        put(img, x, y + 1, LEAF[2])
        put(img, x, y, colours[i % len(colours)])
        put(img, x + 1, y, colours[(i + 1) % len(colours)])
    return img


def lily_pad(seed, lotus=False):
    """A lily pad on the pond, flat; with a lotus flower on some."""
    img = canvas(8)
    cx, cy = 3.5 + (seed % 2) * 0.5, 3.5
    for y in range(8):
        for x in range(8):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            if dx * dx + dy * dy <= 9.5 and not (abs(dx - dy) < 0.8 and dx > 0 and dy > 0):
                v = sphere_light(dx / 3.2, dy / 3.2, 1.0, LIGHT) or 0.3
                put(img, x, y, dither(LEAF, 0.3 + v * 0.7, x, y))
    if lotus:
        for (x, y), c in (((3, 2), BLOSSOM[5]), ((2, 3), BLOSSOM[4]), ((4, 3), BLOSSOM[4]), ((3, 3), GLOW[2]),
                          ((3, 4), BLOSSOM[3]), ((2, 2), BLOSSOM[6]), ((4, 2), BLOSSOM[5])):
            put(img, x, y, c)
    return img


def stepping_stone(seed):
    img = canvas(8)
    ball(img, 4, 4, 3.2, 2.6, STONE, noise=0.12, seed=seed, light=(-0.4, -0.5, 0.75))
    return img


def koi(frame, frames=8, size=16):
    """Two koi circling in the pond, flat on the water: an orange and white one and a gold one."""
    img = canvas(size)
    for k, (body, spot, r) in enumerate(((C("#f07a2e"), C("#fff4ec"), 5.0), (C("#f2c040"), C("#fff8d8"), 3.6))):
        a0 = (frame / frames) * math.tau * (1 if k == 0 else -1) + k * math.pi
        for i in range(5):                                 # head to tail along the circle
            a = a0 - i * (0.32 if k == 0 else -0.42)
            x = int(round(size / 2 - 0.5 + math.cos(a) * r))
            y = int(round(size / 2 - 0.5 + math.sin(a) * r * 0.8))
            c = body if i in (0, 2, 3) else spot
            if i == 4:
                c = mix(body, C("#2e86a0"), 0.45)          # the tail, fading into the water
            put(img, x, y, c)
            if i == 1 and k == 0:
                put(img, x + 1, y, body)
    return img


def petals_pile(seed):
    """Fallen blossom gathered on paths, flat."""
    img = canvas(8)
    for i in range(9):
        x, y = int(hash2(i, 1, seed) * 8), int(hash2(i, 2, seed) * 8)
        put(img, x, y, BLOSSOM[3 + i % 4])
    return img


def blank():
    return canvas(8)
