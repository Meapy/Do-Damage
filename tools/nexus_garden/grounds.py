"""The Nexus garden's grounds: 8 by 8 tiles, a few variants each (a painter takes the variant's
seed). Soft greens, pale stone and pink-stained wood, so the garden reads calm and bright beside
the realms' darker lands."""
import math

from .px import hexc as C, hash2, mix, dither, tile

# --- the palette ---------------------------------------------------------------------------------

GRASS = [C("#2f5a32"), C("#3e7038"), C("#4f8640"), C("#64a04c"), C("#82b85e")]
MOSS = [C("#22402e"), C("#2c5236"), C("#38643e"), C("#4a7a48"), C("#5e8e52")]
PETAL = [C("#c8668e"), C("#e892b2"), C("#f6bcd0"), C("#ffe2ec")]
SOIL = [C("#5a3e2c"), C("#6e4c34"), C("#84603e"), C("#9c7650"), C("#b48e64")]
STONE = [C("#5c5a64"), C("#76747e"), C("#908e98"), C("#acaab2"), C("#c8c6cc")]
SLAB = [C("#7a7270"), C("#948a86"), C("#aea49e"), C("#c6bcb4"), C("#dcd4cc")]
GRAVEL = [C("#a8a49c"), C("#c0bcb2"), C("#d6d2c8"), C("#e8e4da"), C("#f6f2ea")]
DECK = [C("#6a4646"), C("#8a5e5a"), C("#a6786e"), C("#c09284"), C("#d8ae9e")]
PLANK = [C("#4a2e1e"), C("#603c26"), C("#7a4e30"), C("#946238"), C("#ae7a48")]
TATAMI = [C("#6e6a34"), C("#86803e"), C("#9e984c"), C("#b6ae5e"), C("#cac274")]
POND = [C("#1e5a74"), C("#24708a"), C("#2e86a0"), C("#46a2b8"), C("#78c4d2"), C("#c4ecf0")]
SEA = [C("#0a1a34"), C("#0e2444"), C("#132e54"), C("#1a3a66"), C("#2a5482"), C("#6a94bc")]
SAND = [C("#a89472"), C("#bca884"), C("#cebc98"), C("#ded0ae"), C("#ece2c4")]


def _n(x, y, seed, k=0):
    return hash2(x + k * 17, y + k * 31, seed)


# --- painters ------------------------------------------------------------------------------------

def grass(base=GRASS, petals=0.0, flowers=None):
    """Grass: blades as short light strokes over a mottled field, sometimes a fallen petal."""
    def paint(seed):
        def px(x, y):
            v = 0.45 + 0.25 * (_n(x // 2, y // 2, seed, 1) - 0.5) + 0.25 * (_n(x, y, seed) - 0.5)
            # a blade: a light pixel with a darker one under it
            if _n(x, y, seed, 2) > 0.86:
                v = 0.85
            elif _n(x, y - 1, seed, 2) > 0.86:
                v = 0.2
            if petals and _n(x, y, seed, 3) > 1 - petals:
                return PETAL[1 + int(_n(y, x, seed, 4) * 3) % 3]
            if flowers and _n(x, y, seed, 5) > 0.988:
                return flowers[int(_n(x, y, seed, 6) * len(flowers)) % len(flowers)]
            return dither(base, v, x, y)
        return tile(px)
    return paint


def petal_carpet(seed):
    """Moss half hidden under fallen blossom."""
    def px(x, y):
        v = _n(x // 2, y // 2, seed, 1) * 0.5 + _n(x, y, seed) * 0.5
        if v > 0.3:
            return dither(PETAL, 0.25 + (v - 0.3) * 1.1, x, y)
        return MOSS[1 + int(v * 6) % 3]
    return tile(px)


def soil(base=SOIL, pebbles=STONE):
    """Packed earth: mottled, with small pebbles that catch the light on their top edge."""
    def paint(seed):
        def px(x, y):
            if _n(x, y, seed, 2) > 0.965:
                return pebbles[3]
            if _n(x, y - 1, seed, 2) > 0.965:
                return pebbles[1]
            v = 0.5 + 0.3 * (_n(x // 2, y // 2, seed, 1) - 0.5) + 0.3 * (_n(x, y, seed) - 0.5)
            return dither(base, v, x, y)
        return tile(px)
    return paint


def flagstones(pal, moss=None):
    """Large flat stones, one or two to a tile: each lit along its top-left edge and shaded along
    its bottom-right, mortar (or moss) in the seams on the tile's right and bottom."""
    def paint(seed):
        split = (seed * 7) % 5                # 0-2: one stone; 3: split across; 4: split down
        cut = 3 + seed % 3

        def stone_of(x, y):
            if split == 3:
                return (0, 0, 7, cut) if y <= cut else (0, cut + 1, 7, 7)
            if split == 4:
                return (0, 0, cut, 7) if x <= cut else (cut + 1, 0, 7, 7)
            return (0, 0, 7, 7)

        def px(x, y):
            x0, y0, x1, y1 = stone_of(x, y)
            if x == x1 or y == y1:
                if moss and hash2(x, y, seed + 4) > 0.35:
                    return moss[1 + int(hash2(x, y, seed + 5) * 2)]
                return pal[0]
            tone = 0.5 + 0.18 * (hash2(x0, y0, seed + 7) - 0.5)
            if x == x0 or y == y0:
                tone += 0.22
            elif x == x1 - 1 or y == y1 - 1:
                tone -= 0.14
            tone += 0.1 * (hash2(x, y, seed) - 0.5)
            if hash2(x, y, seed + 9) > 0.96:
                tone -= 0.25              # a pit in the stone
            return dither(pal, tone, x, y)
        return tile(px)
    return paint


def gravel(seed):
    """Raked white gravel: the rake's furrows run across, a pebble's shadow here and there."""
    def px(x, y):
        furrow = (y + (seed % 2)) % 3
        v = (0.75 if furrow == 0 else 0.55 if furrow == 1 else 0.3) + 0.15 * (_n(x, y, seed) - 0.5)
        if _n(x, y, seed, 2) > 0.95:
            v = 0.1
        return dither(GRAVEL, v, x, y)
    return tile(px)


def planks(pal, across, seed_shift=0, seam=0, ends=0.7):
    """Boards: `across` False runs them left to right (seams every 4 rows), True up and down.
    `seam` is the ramp step of the gaps between them, `ends` how rarely a board ends in a tile."""
    def paint(seed):
        def px(x, y):
            u, v = (y, x) if across else (x, y)
            board = v // 4
            if v % 4 == 3:
                return pal[seam]
            # the boards' ends, staggered
            if u == (board * 5 + seed * 3 + seed_shift) % 8 and _n(board, seed, 3) > ends:
                return pal[seam]
            tone = 0.55 + 0.2 * (_n(board, seed, 1) - 0.5)
            if v % 4 == 0:
                tone += 0.22
            # the grain
            if _n(u // 3, v, seed, 2) > 0.7:
                tone -= 0.12
            if _n(u, v, seed, 4) > 0.96:
                tone -= 0.3          # a knot
            return dither(pal, tone, x, y)
        return tile(px)
    return paint


def deck_border(seed):
    """The dark lacquered beam round the spawn's deck."""
    def px(x, y):
        tone = 0.3 + 0.15 * (_n(x // 4, y, seed) - 0.5)
        if y in (0, 4):
            tone += 0.2
        if y in (3, 7):
            tone = 0.05
        return dither(PLANK, tone, x, y)
    return tile(px)


def cobbles(seed):
    """Round cobbles: each lit on top, shaded below, dark gaps between."""
    centres = [(1.5 + (seed % 2), 1.5), (5.5, 1.0 + (seed % 3) * 0.5), (3.5, 4.5), (0.0, 5.5),
               (7.5, 5.0), (1.5, 7.8), (5.5, 7.6)]

    def px(x, y):
        best, by = None, 99
        for cx, cy in centres:
            for ox in (-8, 0, 8):
                for oy in (-8, 0, 8):
                    d = math.hypot(x + 0.5 - cx - ox, (y + 0.5 - cy - oy) * 1.15)
                    if d < by:
                        by, best = d, (cx + ox, cy + oy)
        if by > 1.9:
            return STONE[0]
        dy = y + 0.5 - best[1]
        dx = x + 0.5 - best[0]
        tone = 0.55 - dy * 0.18 - dx * 0.06 + 0.1 * (_n(int(best[0]), int(best[1]), seed) - 0.5)
        if by > 1.5:
            tone -= 0.2
        return dither(STONE, tone, x, y)
    return tile(px)


def tatami(seed):
    """Woven rush mats with their dark cloth edges."""
    def px(x, y):
        if y == 0:
            return C("#3e3a2a") if seed % 2 else C("#5a5434")
        tone = 0.55 + (0.15 if x % 2 == 0 else -0.05) + 0.1 * (_n(x, y, seed) - 0.5)
        return dither(TATAMI, tone, x, y)
    return tile(px)


def water(pal, crest=0.94):
    """Water: soft diagonal swells and a few bright crests (the client scrolls it, `Animate`)."""
    def paint(seed):
        def px(x, y):
            v = math.sin((x * 0.8 + y * 0.45 + seed * 1.3) * 0.95) * 0.5 + 0.5
            v = 0.3 + v * 0.45 + 0.1 * (_n(x, y, seed) - 0.5)
            if v > 0.71 and _n(x, y, seed, 2) > 0.55:
                return pal[-1] if v > crest - 0.2 else pal[-2]
            return dither(pal[:-1], v, x, y)
        return tile(px)
    return paint


def sand(seed):
    def px(x, y):
        v = 0.55 + 0.25 * (_n(x // 2, y, seed, 1) - 0.5) + 0.2 * (_n(x, y, seed) - 0.5)
        if _n(x, y, seed, 2) > 0.95:
            v = 0.15
        return dither(SAND, v, x, y)
    return tile(px)


def shallows(seed):
    """The pond's edge: pebbles seen through clear water."""
    def px(x, y):
        if _n(x, y, seed, 3) > 0.9:
            return mix(SAND[3], POND[3], 0.5)
        if _n(x, y - 1, seed, 3) > 0.9:
            return mix(SAND[1], POND[2], 0.5)
        v = 0.55 + 0.25 * math.sin((x + y * 0.5 + seed) * 0.9) + 0.1 * (_n(x, y, seed) - 0.5)
        return dither(POND[2:5], v, x, y)
    return tile(px)


def petal_gravel(seed):
    """Raked gravel under the elder cherry, strewn with its blossom."""
    base = gravel(seed)
    for y in range(8):
        for x in range(8):
            if _n(x, y, seed, 6) > 0.8:
                base.putpixel((x, y), PETAL[1 + int(_n(y, x, seed, 7) * 3) % 3] + (255,))
    return base


def dojo_sand(seed):
    """The dojo yard's sand, raked in long straight lines."""
    def px(x, y):
        line = (y + seed) % 4
        v = (0.75 if line == 0 else 0.5 if line == 1 else 0.4) + 0.12 * (_n(x, y, seed) - 0.5)
        return dither(SAND, v, x, y)
    return tile(px)


# the arched bridges' decks: water under the deck the model draws (models.arch), each tile lifting
# whoever stands on it as high as the deck is there (`Lift`, the Rust client's view.rs), so they
# climb the arch; the nine tiles' lifts go up and down again, five kinds
ARCH_HEIGHT = 0.5
ARCH_LEVELS = [round(ARCH_HEIGHT * math.sin(math.pi * (i + 0.5) / 9), 3) for i in range(5)]


# name -> (painter, variants, extra XML, minimap colour). Extra: NoWalk, Sink, Speed, Animate and
# BlendPriority as Realm 2's; `Petals` marks the forest's floor, where the Rust client lets cherry
# blossom fall across the screen (its text, when there is one, how thickly: three times under the
# elder cherry).
GROUNDS = {
    "NG Deep Sea": (water(SEA), 4, {"NoWalk": None, "Animate": ("Wave", {"dx": "0.15", "dy": "0.1"}),
                                    "BlendPriority": "-1"}, "0x132E54"),
    "NG Pond": (water(POND), 4, {"Sink": None, "Speed": "0.7", "Animate": ("Wave", {"dx": "0.2", "dy": "0.15"}),
                                 "BlendPriority": "2"}, "0x2E86A0"),
    "NG Pond Shallows": (shallows, 4, {"Speed": "0.85", "BlendPriority": "3"}, "0x46A2B8"),
    "NG Shore Sand": (sand, 4, {"BlendPriority": "4"}, "0xCEBC98"),
    "NG Meadow Grass": (grass(), 4, {"BlendPriority": "6"}, "0x4F8640"),
    "NG Sakura Grass": (grass(petals=0.035, flowers=[C("#fff4f8"), C("#f6d36a")]), 4,
                        {"BlendPriority": "7"}, "0x5E8A4C"),
    "NG Forest Moss": (grass(MOSS, petals=0.03), 4, {"BlendPriority": "8", "Petals": None}, "0x38643E"),
    "NG Petal Carpet": (petal_carpet, 4, {"BlendPriority": "9", "Petals": None}, "0xE892B2"),
    "NG Packed Earth": (soil(), 4, {"BlendPriority": "10"}, "0x84603E"),
    "NG Stone Path": (flagstones(STONE), 4, {"BlendPriority": "12"}, "0x908E98"),
    "NG Mossy Path": (flagstones(STONE, MOSS), 4, {"BlendPriority": "11", "Petals": None}, "0x76847A"),
    "NG Shrine Slab": (flagstones(SLAB), 4, {"BlendPriority": "13"}, "0xAEA49E"),
    "NG Raked Gravel": (gravel, 2, {"BlendPriority": "14"}, "0xD6D2C8"),
    "NG Cobblestone": (cobbles, 3, {"BlendPriority": "15"}, "0x908E98"),
    "NG Tatami": (tatami, 2, {"BlendPriority": "16"}, "0x9E984C"),
    "NG Sakura Deck": (planks(DECK, False, seam=1, ends=0.88), 4, {"BlendPriority": "20"}, "0xA8665A"),
    "NG Deck Beam": (deck_border, 2, {"BlendPriority": "21"}, "0x603C26"),
    "NG Bridge Planks EW": (planks(PLANK, True), 3, {"BlendPriority": "22"}, "0x7A4E30"),
    "NG Bridge Planks NS": (planks(PLANK, False), 3, {"BlendPriority": "22"}, "0x7A4E30"),
    "NG Petal Gravel": (petal_gravel, 3, {"BlendPriority": "14", "Petals": "3"}, "0xE0C8CC"),
    "NG Dojo Sand": (dojo_sand, 2, {"BlendPriority": "13"}, "0xCEBC98"),
}
for _i, _lift in enumerate(ARCH_LEVELS):
    GROUNDS["NG Arch Deck %d" % (_i + 1)] = (water(POND), 2, {"Lift": str(_lift), "BlendPriority": "2"}, "0x7A4E30")
