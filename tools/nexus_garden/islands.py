"""The Nexus islands' own art (tools/build_nexus_islands.py), beside the garden's (the islands are
built from the garden's grounds, scenery and models): what only floating islands need.

- Grounds: the rim round each island (mossy rock, or dressed stone where it's paved) whose side,
  where the island's edge drops into the sky, shows soil over rock strata; the rock the undersides
  hang in (`NI Sky Rock`, drawn by the Rust client, islands.rs); the bridges' decks (planks across
  the way, a dark timber edge beam, vermilion railings, piers under them: `<Piers/>`) and their
  rising decks (`<Lift>`): the wooden ones arching as the garden's over its pond do, the stone one
  a quarter of a tile a square; the Arena tower's top
  deck and the column in its stair's well, whose sides go down to the ground as walls
  (`<FaceDown/>`: rose timber and lit shoji, a pagoda's); the spiral stair's steps.
- The bar on the tower's top: its counter pavilion (a 3D model: shelves of bottles, a counter,
  noren curtains, a string of lanterns, a teal roof), stools, and the barman, who throws drinks
  (NexusBar on the world server) in a sake bottle (`NI Thrown Sake`, the art of the throw).
- The lookout's telescope.

Palette and pixel rules as the garden's (nexus_garden.px): ramps dark to light, lit from the upper
left, dithered, no pure black."""
import math

from PIL import Image

from . import grounds as G
from .extras import walk_strip
from . import models as M
from .models import (Mesh, R_CORD, R_END, R_GOLD, R_LAMP, R_SLOPE, R_TIMBER, base_texture, fill, solid, GOLD, INDIGO, ROSE,
                     TIMBER)
from .px import canvas, dither, hash2, hexc as C, outline, picture, put, tile  # noqa: F401
from .sprites import LACQUER, WOOD

# --- palettes ------------------------------------------------------------------------------------

ROCK = [C("#2e2838"), C("#3d3549"), C("#4d4459"), C("#5f556b"), C("#73687e"), C("#8a7e94"), C("#a296ab")]
SOILR = [C("#3a2418"), C("#4e3020"), C("#643f28"), C("#7a5034")]
ROOTS = [C("#1e3424"), C("#2c4a2e"), C("#3e6436")]
GRANITE = [C("#4a4858"), C("#5e5c6e"), C("#747286"), C("#8c8a9e"), C("#a6a4b6"), C("#c2c0d0")]
BEAM = [C("#1e0e10"), C("#2e1618"), C("#432024"), C("#5a2c2e"), C("#74403a")]
VERMILION = 0xC8402E          # the railings of wood (NG Wood Rail's lacquer)
GRANITE_RAIL = 0x8C8A9E       # and of stone
TOWER = [C("#3e1820"), C("#5a2430"), C("#783242"), C("#964254"), C("#b25a68")]
SHOJI = [C("#c86a92"), C("#e88aac"), C("#f7b0c8"), C("#ffd8e6")]
SKIN = [C("#9a6448"), C("#c8946c"), C("#f2c8a0")]


def _n(x, y, seed, k=0):
    return hash2(x + k * 17, y + k * 31, seed)


# --- ground painters (8 by 8) ------------------------------------------------------------------

def sky_rock(seed):
    """Rock in strata: bands two or three pixels tall, each its own shade, cracked along their
    seams, a few lighter grains: what an island's underside is made of."""
    def px(x, y):
        band = (y + seed) // 3
        v = 0.25 + 0.5 * _n(band, 0, seed, 1) + 0.18 * (_n(x // 2, y, seed, 2) - 0.5)
        if (y + seed) % 3 == 0 and _n(x, band, seed, 3) > 0.45:
            v -= 0.32           # the seam between strata
        if _n(x, y, seed, 4) > 0.93:
            v += 0.3            # a grain catching the light
        return dither(ROCK, v, x, y)
    return tile(px)


def rim_face(seed):
    """An island's edge seen from the side: a fringe of grass and roots, a band of soil, then
    rock strata below."""
    def px(x, y):
        if y == 0:
            return ROOTS[2] if _n(x, 0, seed, 1) > 0.3 else ROOTS[1]
        if y == 1:
            return ROOTS[0] if _n(x, 1, seed, 2) > 0.55 else SOILR[2]
        if y <= 3:
            v = 0.45 + 0.4 * (_n(x, y, seed, 3) - 0.5)
            if _n(x, y, seed, 5) > 0.9:
                return ROOTS[0]
            return dither(SOILR, v, x, y)
        band = (y + seed) // 2
        v = 0.3 + 0.45 * _n(band, 0, seed, 4) + 0.15 * (_n(x, y, seed, 6) - 0.5)
        if (y + seed) % 2 == 0 and _n(x, band, seed, 7) > 0.6:
            v -= 0.3
        return dither(ROCK, v, x, y)
    return tile(px)


def island_rim(seed):
    """The ground at an island's very edge: moss over the rock's lip, small stones, so the edge
    reads before the drop."""
    def px(x, y):
        v = 0.5 + 0.3 * (_n(x // 2, y // 2, seed, 1) - 0.5) + 0.2 * (_n(x, y, seed) - 0.5)
        if _n(x, y, seed, 2) > 0.82:
            return GRANITE[3] if _n(x, y - 1, seed, 3) > 0.5 else GRANITE[2]
        if _n(x, y - 1, seed, 2) > 0.82:
            return GRANITE[1]
        return dither(G.MOSS, v, x, y)
    return tile(px)


def ashlar(pal, seed, rows=(0, 4)):
    """Dressed stone blocks, two courses a tile, the joints staggered."""
    def px(x, y):
        course = y // 4
        off = (course * 3 + seed) % 4
        if y % 4 == 3 or (x + off) % 8 == 0:
            return pal[0]
        v = 0.45 + 0.25 * (_n((x + off) // 8, course, seed, 1) - 0.5) + 0.12 * (_n(x, y, seed, 2) - 0.5)
        if y % 4 == 0:
            v += 0.2            # each block lit along its top
        return dither(pal, v, x, y)
    return tile(px)


def timber_face(seed):
    """A bridge's edge beam seen from the side: dark lacquered timber, its top lit, iron bolts."""
    def px(x, y):
        if y == 0:
            return BEAM[4]
        if y == 7:
            return BEAM[0]
        if (x + seed * 3) % 8 == 2 and y in (2, 5):
            return GRANITE[1]
        v = 0.35 + 0.25 * (_n(x // 3, y, seed, 1) - 0.5)
        return dither(BEAM, v, x, y)
    return tile(px)


def lacquer_face(seed):
    """An arched bridge's side, as the garden's over its pond (models.arch): vermilion lacquer,
    lit along its top, dark at its foot."""
    def px(x, y):
        if y == 0:
            return LACQUER[4]
        if y == 7:
            return LACQUER[0]
        return dither(LACQUER, 0.5 + 0.12 * (_n(x // 4, y, seed, 1) - 0.5), x, y)
    return tile(px)


def tower_wall(seed):
    """A tower's side, a square of it: rose timber posts and rails round a lit shoji panel, a
    pagoda's walls (the tiers' roofs are drawn by the client at each floor)."""
    def px(x, y):
        if x == 0 or y == 7:
            return TIMBER[1]
        if y == 0:
            return TIMBER[2]
        if 2 <= x <= 6 and 2 <= y <= 5:
            if x == 4 or y == 4:
                return TIMBER[2]    # the shoji's lattice
            return dither(SHOJI, 0.55 + 0.25 * (_n(x, y, seed) - 0.5), x, y)
        return dither(TOWER, 0.5 + 0.2 * (_n(x, y, seed, 1) - 0.5), x, y)
    return tile(px)


def column_wall(seed):
    """The stair's middle column: vermilion lacquer, gilded bands."""
    def px(x, y):
        if y in (0, 7):
            return GOLD[2] if x % 2 == 0 else GOLD[1]
        v = 0.55 + 0.3 * math.sin((x + 0.5) / 8 * math.pi) - 0.15
        return dither(LACQUER, v, x, y)
    return tile(px)


def lacquer_planks(pal):
    return G.planks(pal, False, seam=0, ends=0.85)


GROUNDS_DIR = None   # filled by build_art

# name -> (painter, variants, {xml tag: value}, minimap colour, face painter or None)
# Lifts: the bridges climb and fall a quarter of a tile a square (never more), the spiral stair
# a quarter of a tile a twelfth of a turn (tools/build_stair_tower.py's spiral).
LIFTS = [k * 0.25 for k in range(1, 12)]


def tower_stone(seed):
    """A round stone tower's wall, a square of it: rough-dressed granite in uneven courses, some
    squares with a narrow arched window, dark within, a warm light in it (one floor's in four)."""
    img = ashlar(GRANITE, seed)
    if seed % 4 == 0:
        dark, lit, frame = C("#1c1826"), C("#f0a040"), GRANITE[4]
        for y in range(1, 7):
            for x in range(2, 6):
                if y == 1 and x in (2, 5):
                    continue                 # the arch's corners
                edge = x in (2, 5) or y in (1, 6)
                put(img, x, y, frame if edge else (lit if (x + y) % 3 == 0 and y < 5 else dark))
    return img


def tower_floor(seed):
    """Flagstones on the tower's top and inside its door."""
    return G.flagstones(G.SLAB)(seed)


def frog(frame):
    """A green tree frog sitting on a notched lily pad, 16 by 16, facing out: domed eyes with a
    gleam, nostrils, a wide mouth, a darker-spotted back, a cream throat and belly, its front legs
    at its sides and splayed toes. Frame 0 sits, 1 puffs its throat out, 2 flicks its red tongue
    out towards you."""
    pal = {
        "o": C("#2f6e2a"), "G": C("#5aa83c"), "H": C("#8ad05a"), "s": C("#2a5a22"),  # body: edge, green, light, spots
        "W": C("#ffffff"), "k": C("#1c1418"), "y": C("#e8c84a"),                     # eyes: gleam, pupil, iris
        "m": C("#1e3a18"),                                                          # the mouth's line
        "c": C("#e6e2a6"), "C": C("#f6f2c8"),                                       # throat and belly
        "r": C("#c8283a"), "R": C("#f0607a"),                                       # tongue
        "L": C("#2a7468"), "P": C("#46a08a"), "q": C("#86ccb0"),                    # lily pad, bluer than the frog
    }
    rows = [
        "................",
        "..oooo....oooo..",
        ".oHWyko..oHWyko.",
        ".oGykkooooGykko.",
        ".oHGGGGsGGGGGHo.",
        "oHGGsGGGGGGsGGHo",
        "oGGGGGkGGkGGGGGo",
        "oGmGGGGGGGGGGmGo",
        "oGGmmmmmmmmmmGGo",
        ".oGGccccccccGGo.",
        "oGoGccccccccGoGo",
        "oGGoGccccccGoGGo",
        "HGHGoGGooGGoGHGH",
        "LqLLLLqLLLLqLLqL",
        ".LPPLLLLLLLLPPL.",
        "..LLLLLL.LLLLL..",
    ]
    if frame == 1:
        # the throat puffed out, round and pale
        rows[9] = ".oGCCCCCCCCCCGo."
        rows[10] = "oGoCCCCCCCCCCoGo"
        rows[11] = "oGGoCCCCCCCCoGGo"
    if frame == 2:
        # the tongue flicked out of the mouth's middle, down towards you, a round pink tip
        rows[8] = "oGGmmmmrrmmmmGGo"
        rows[9] = ".oGGcccrrcccGGo."
        rows[10] = "oGoGcccrrcccGoGo"
        rows[11] = "oGGoGcRRRRcGoGGo"
        rows[12] = "HGHGoGRRRRGoGHGH"
    img = picture(rows, pal, 16, 16)
    outline(img, lambda c: tuple(max(0, int(v * 0.45)) for v in c))
    return img


# name -> (painter, variants, {xml tag: value}, minimap colour, face painter or None[, face variants])
# Lifts: the bridges climb and fall a quarter of a tile a square (never more), the spiral stair
# a quarter of a tile a twelfth of a turn (tools/build_stair_tower.py's spiral).
LIFTS = [k * 0.25 for k in range(1, 12)]
# The wooden bridges between the islands arch as the garden's over its pond do (models.arch): their
# decks up to a tile over their ends, an eighth of a tile apart (`ARCH_LIFTS`, the first level),
# in that bridge's dark planks laid across the way, with its vermilion lacquered sides and its
# taller railings, the posts capped in brass (`<RailHeight>`, `<RailCap>`); no piers under them
# (`<NoUnderside/>`): the arch carries itself.
ARCH_LIFTS = [k * 0.125 for k in range(0, 9)]
ARCH_PLANK = [TIMBER[1]] + WOOD[1:]
ARCH_RAIL_HEIGHT = 0.62       # models.arch's top rail over its deck
BRASS = 0xE0B040              # its posts' caps (models.GOLD)
WOOD_RAIL = 0x7A4A2C          # the tower's balcony: dark oak, as its timbers


balcony_planks = G.planks(BEAM[2:] + [C("#8a5a3a"), C("#a8724a")], True, seam=0, ends=0.8)


def grounds():
    wood_rail = {"Rail": "0x%06X" % VERMILION}
    stone_rail = {"Rail": "0x%06X" % GRANITE_RAIL}
    oak_rail = {"Rail": "0x%06X" % WOOD_RAIL}
    # a bridge's deck slopes from square to square (no steps) and stays solid; piers under it
    bridge = {"StaySolid": None, "Piers": None, "Slope": None, "Speed": G.PATH_SPEED}
    # the stone bridge's walls as the garden's stone bridges' (models.arch): thick granite with a
    # coping, as high as theirs (`<RailHeight>`)
    stone_bridge = {"StaySolid": None, "Piers": "stone", "Slope": None, "Parapet": None, "RailHeight": "0.6",
                    "Speed": G.PATH_SPEED}
    stone_face = lambda s: ashlar(GRANITE, s + 2)
    out = {
        "NI Sky Rock": (sky_rock, 4, {"NoWalk": None, "BlendPriority": "-1"}, "0x4D4459", None),
        "NI Island Rim": (island_rim, 4, {"BlendPriority": "5", **stone_rail}, "0x46603E", rim_face),
        "NI Paved Rim": (lambda s: ashlar(GRANITE, s), 3, {"BlendPriority": "19", **stone_rail}, "0x8C8A9E",
                         lambda s: ashlar(GRANITE, s + 5)),
        "NI Bridge Deck EW": (G.planks(G.PLANK, True), 3, {"BlendPriority": "24", **wood_rail, **bridge}, "0x7A4E30",
                              timber_face),
        "NI Bridge Deck NS": (G.planks(G.PLANK, False), 3, {"BlendPriority": "24", **wood_rail, **bridge}, "0x7A4E30",
                              timber_face),
        "NI Stone Bridge": (lambda s: ashlar(GRANITE, s + 9), 3, {"BlendPriority": "23", **stone_rail, **stone_bridge},
                            "0x908E98", stone_face),
        # the Arena's round stone tower: its foot (walls not walked through, the door's floor),
        # its top inside the balcony (its sides go down to the ground as the tower's walls, a
        # cornice at each floor: <Column/>; over the door a doorway), the core under its cap, and
        # the oak balcony round it, hanging over the court
        "NI Tower Wall": (lambda s: ashlar(GRANITE, s + 3), 2, {"NoWalk": None, "BlendPriority": "18"}, "0x5E5C6E", stone_face),
        "NI Tower Floor": (tower_floor, 3, {"BlendPriority": "18"}, "0xAEA49E", stone_face),
        "NI Tower Top": (balcony_planks, 3, {"BlendPriority": "26", "Column": None, "Eave": "0x9A98AE", **oak_rail},
                         "0x74403A", tower_stone, 4),
        "NI Tower Doorway": (balcony_planks, 3, {"BlendPriority": "26", "Column": None, "Doorway": None, "Eave": "0x9A98AE",
                                                 **oak_rail}, "0x74403A", tower_stone, 4),
        "NI Tower Core": (lambda s: ashlar(GRANITE, s + 4), 2, {"NoWalk": None, "BlendPriority": "25", "Column": None,
                                                                "Eave": "0x9A98AE"}, "0x5E5C6E", tower_stone, 4),
        "NI Balcony": (balcony_planks, 3,
                       {"BlendPriority": "26", **oak_rail}, "0x74403A", timber_face),
        "NI Tower Base": (lambda s: ashlar(GRANITE, s + 3), 2, {"NoWalk": None, "BlendPriority": "18", "NoUnderside": None},
                          "0x5E5C6E", stone_face),
    }
    # the wooden bridges arch, east to west (`ARCH_LIFTS`); the stone one steps a quarter of a
    # tile a square
    arch = {"BlendPriority": "24", **wood_rail, "RailHeight": str(ARCH_RAIL_HEIGHT), "RailCap": "0x%06X" % BRASS,
            "StaySolid": None, "Slope": None, "NoUnderside": None, "Slab": "0.26", "Speed": G.PATH_SPEED}
    for k, lift in enumerate(ARCH_LIFTS):
        tags = dict(arch, Lift=str(lift)) if lift else arch
        out["NI Arch Deck EW %d" % k] = (G.planks(ARCH_PLANK, True), 3, tags, "0x70482E", lacquer_face)
    for k, lift in enumerate(LIFTS, 1):
        out["NI Stone Ramp %d" % k] = (lambda s: ashlar(GRANITE, s + 9), 3,
                                       {"Lift": str(lift), "BlendPriority": "23", **stone_rail, **stone_bridge}, "0x908E98",
                                       stone_face)
    for k in range(12):
        tags = {"StaySolid": None, "BlendPriority": "25", **wood_rail}
        if k:
            tags["Lift"] = str(k * 0.25)
        out["NI Spiral %d" % k] = (lacquer_planks(TOWER), 2, tags, "0x783242", tower_wall)
    return out


# --- sprites -----------------------------------------------------------------------------------

def barman():
    """Kenji the barman, 16 by 16 facing right: a stocky man in an indigo happi coat with white
    trim, a white hachimaki round his brow, a towel over his shoulder, a sake bottle in his hand."""
    pal = {
        "h": C("#1c1418"), "H": C("#3a2a2a"),            # hair
        "w": C("#f4f2f0"), "W": C("#c8c4cc"),            # headband, towel, trim
        "r": C("#c82a2e"),                               # the headband's knot
        "s": SKIN[2], "S": SKIN[1], "d": SKIN[0],        # skin
        "e": C("#241418"),                               # eye
        "i": INDIGO[1], "I": INDIGO[2], "j": INDIGO[0],  # happi
        "a": C("#5a2c2e"), "A": C("#74403a"),            # apron
        "k": C("#1e1a22"), "K": C("#2e2a34"),            # trousers, sandals
        "b": C("#f2f0ec"), "B": C("#3a5c92"),            # the bottle: ceramic and its blue band
    }
    rows = [
        "................",
        "......hhhh......",
        ".....hhHHhh.....",
        ".....wwwwwwr....",
        ".....hsssss.r...",
        ".....hsssesS....",
        "......sssSS.....",
        "......dSSS......",
        "....iiIwwIii....",
        "...iiIIwWIiisb..",
        "...iWIIiiIIi.B..",
        "...iWiiaaiIi.b..",
        "...ss.aaAa......",
        "......aaAa......",
        "......kK.kK.....",
        ".....kk..kk.....",
    ]
    img = picture(rows, pal, 16, 16)
    outline(img, lambda c: tuple(max(0, int(v * 0.45)) for v in c))
    return img


def barman_strip():
    return walk_strip(barman(), 16, 2)


def thrown_sake():
    """A sake bottle (tokkuri) in white glaze with a blue band, 8 by 8: what the barman throws."""
    pal = {"w": C("#f4f2ee"), "W": C("#c8c4cc"), "b": C("#3a5c92"), "B": C("#26406a"), "k": C("#5a4a3a")}
    rows = [
        "...kk...",
        "...ww...",
        "...wW...",
        "..wwwW..",
        ".wbbbBW.",
        ".wwwwWW.",
        ".wwwwWW.",
        "..WWWW..",
    ]
    img = picture(rows, pal, 8, 8)
    outline(img, lambda c: tuple(max(0, int(v * 0.4)) for v in c))
    return img


def bar_stool(seed=0):
    """A round stool, red lacquer seat on dark legs, 8 by 8."""
    pal = {"r": C(LACQ_HEX[3]), "R": C(LACQ_HEX[4]), "q": C(LACQ_HEX[1]), "k": C("#2e1618"), "K": C("#432024")}
    rows = [
        "........",
        "........",
        "..RRRR..",
        ".rRRRRr.",
        ".qrrrrq.",
        "..kKKk..",
        "..k..k..",
        ".k....k.",
    ]
    img = picture(rows, pal, 8, 8)
    outline(img, lambda c: tuple(max(0, int(v * 0.45)) for v in c))
    return img


LACQ_HEX = ["#4a1210", "#7a1e18", "#a82e22", "#d0462e", "#ee6a48"]


def telescope():
    """A brass telescope on a tripod, pointed out over the islands, 16 by 16."""
    img = canvas(16)
    brass = [C("#5a3e14"), C("#8a6420"), C("#c09030"), C("#f0c860"), C("#fff0b0")]
    wood = WOOD
    # the tripod
    for (x0, y0, x1, y1) in ((8, 9, 4, 15), (8, 9, 12, 15), (8, 9, 8, 15)):
        n = max(abs(x1 - x0), abs(y1 - y0))
        for k in range(n + 1):
            x = round(x0 + (x1 - x0) * k / n)
            y = round(y0 + (y1 - y0) * k / n)
            put(img, x, y, wood[1] if x != 8 else wood[2])
    # the tube, rising to the upper right, lit along its top
    for k in range(10):
        x, y = 3 + k, 10 - k * 0.6
        for d in (-1, 0, 1):
            c = brass[3] if d < 0 else brass[2] if d == 0 else brass[1]
            put(img, x, int(round(y + d)), c)
    put(img, 13, 3, brass[4])
    put(img, 13, 4, brass[3])
    put(img, 2, 10, brass[1])
    put(img, 2, 11, brass[0])
    outline(img, lambda c: tuple(max(0, int(v * 0.4)) for v in c))
    return img


# --- the bar's model -----------------------------------------------------------------------------

R_SHELF = (0, 96, 64, 128)      # shelves of bottles
R_COUNTER = (64, 96, 96, 112)   # the counter's front: rose panels
R_TOP = (96, 96, 112, 104)      # its top: dark polished wood
R_NOREN = (112, 96, 128, 112)   # indigo curtains, a white crest
R_SIGN = (64, 112, 128, 128)


# the letters the garden's signs lack (models.GLYPHS: a 3 by 5 pixel font)
GLYPHS = {
    "S": ["111", "100", "111", "001", "111"], "K": ["101", "101", "110", "101", "101"],
    "Y": ["101", "101", "010", "010", "010"], "B": ["110", "101", "110", "101", "110"],
    "R": ["110", "101", "110", "101", "101"], "E": ["111", "100", "110", "100", "111"],
}


def sign(img, rect, text):
    """models.sign, with these letters too."""
    for k, v in GLYPHS.items():
        M.GLYPHS.setdefault(k, v)
    M.sign(img, rect, text)


def _shelves(img, rect):
    """Three shelves of bottles: tokkuri, tall sake bottles, a few of glass, on dark wood."""
    x0, y0, x1, y1 = rect
    colours = [[C("#f4f2ee"), C("#c8c4cc")], [C("#2a5a3a"), C("#3e7a52")], [C("#8a4a1e"), C("#c07030")],
               [C("#3a5c92"), C("#6488c0")], [C("#6a1420"), C("#a82e3a")]]
    for y in range(y0, y1):
        for x in range(x0, x1):
            put(img, x, y, TIMBER[1] if (y - y0) % 10 < 8 else TIMBER[3])
    for row in range(3):
        base = y0 + row * 10 + 8
        x = x0 + 1
        k = row * 7
        while x < x1 - 2:
            c = colours[int(hash2(x, row, 5) * len(colours))]
            h = 4 + int(hash2(x, row, 6) * 3)
            w = 2 if hash2(x, row, 7) > 0.4 else 3
            for yy in range(base - h, base):
                for xx in range(x, x + w):
                    neck = yy < base - h + 2 and xx != x + w // 2
                    if not neck:
                        put(img, xx, yy, c[1] if xx == x else c[0])
            x += w + 1 + (k % 2)
            k += 1


def bar():
    """The bar on the overhang at the side of the Arena tower's balcony: an open counter seven
    tiles by three under the sky, no roof, its model's tile the counter's middle (the front row;
    the barman's aisle and the shelves behind it, to the north). At the back, oak shelves of
    bottles; the aisle; the counter in front, rose panels and a dark top with the SKY BAR sign set
    in it; at its two ends an oak post with an arm, a pink lantern hanging from each. Nothing
    across the front, so the barman shows over his counter."""
    img = base_texture()
    _shelves(img, R_SHELF)
    fill(img, R_COUNTER, lambda x, y, w, h: TIMBER[1] if x % 8 == 0 or y in (0, h - 1) else dither(ROSE, 0.5 + 0.2 * (hash2(x, y, 3) - 0.5), x, y))
    solid(img, R_TOP, TIMBER, 0.75, 0.1)
    sign(img, R_SIGN, "SKY BAR")
    m = Mesh()
    X0, X1, YB, YF = -3.45, 3.45, -1.45, 1.45
    # the shelves along the back, a cornice on top
    m.box(X0 + 0.1, YB, 0, X1 - 0.1, YB + 0.35, 1.7, R_TIMBER, front=R_SHELF)
    m.box(X0, YB - 0.05, 1.7, X1, YB + 0.45, 1.82, R_TIMBER)
    # the counter along the front
    m.box(X0 + 0.25, 0.55, 0, X1 - 0.25, 1.05, 0.62, R_COUNTER, top=R_TOP)
    m.box(X0 + 0.15, 0.5, 0.62, X1 - 0.15, 1.12, 0.7, R_TOP)
    # its ends, closing the aisle
    for x in (X0 + 0.25, X1 - 0.55):
        m.box(x, YB + 0.35, 0, x + 0.3, 0.55, 0.62, R_COUNTER, top=R_TOP)
    # the sign set in the counter's top, readable from the front
    m.quad((-1.7, 0.56, 0.71), (1.7, 0.56, 0.71), (1.7, 1.06, 0.71), (-1.7, 1.06, 0.71), R_SIGN, (0, 0.8, 0.0))
    # an oak post at each end of the counter, an arm reaching in over it, a pink lantern hung from it
    TOP = 2.1
    for x, inward in ((X0 + 0.1, 1), (X1 - 0.1, -1)):
        m.box(x - 0.1, YF - 0.6, 0, x + 0.1, YF - 0.4, TOP, R_TIMBER)
        m.box(min(x, x + inward * 0.7) - 0.02, YF - 0.6, TOP - 0.14, max(x, x + inward * 0.7) + 0.02, YF - 0.4, TOP, R_TIMBER)
        m.lantern(x + inward * 0.6, YF - 0.5, TOP - 0.14, R_CORD, R_LAMP, 0.26)
    # its tile is the counter's middle: all of it a tile north of the middle row
    m.vs = [(x, y - 1.0, z) for x, y, z in m.vs]
    return m, img


def tower_cap():
    """The shrine on top of the Arena tower, a pagoda's top storeys as the garden's pagoda
    (models.pagoda): a room of rose timber and lit shoji on the stair's core, two roofs of teal
    tiles flaring out and sweeping up at their corners, lanterns at the lower eaves, a gilded spire
    of rings (a sorin) over all. Centred on the corner its model's tile shares with the core's other
    three squares (half a tile north-west of the tile's middle)."""
    img = base_texture()
    m = Mesh()
    z = 0.0
    for i, (half, height, eave) in enumerate(((1.0, 1.45, 1.85), (0.78, 1.05, 1.45))):
        M.walls(m, -half, -half, half, half, z, z + height)
        z += height
        m.box(-half - 0.08, -half - 0.08, z, half + 0.08, half + 0.08, z + 0.12, R_TIMBER, lid=False)
        z += 0.12
        top = (0.7, 0.0)[i]
        rise = 0.6 if i == 0 else 1.05
        m.roof((-eave, -eave, eave, eave), (-top, -top, top, top), z - 0.05, z + rise, R_SLOPE, R_END, 0.24, 0.22, 10)
        if i == 0:
            for cx, cy in ((-eave, eave), (eave, eave), (-eave, -eave), (eave, -eave)):
                m.lantern(cx * 0.97, cy * 0.97, z + 0.2, R_CORD, R_LAMP, 0.2)
        z += rise if i == 1 else rise * 0.62
    m.box(-0.06, -0.06, z - 0.1, 0.06, 0.06, z + 1.3, R_GOLD)
    for k in range(5):
        r = 0.18 - k * 0.02
        zz = z + 0.15 + k * 0.2
        m.box(-r, -r, zz, r, r, zz + 0.05, R_GOLD)
    m.box(-0.11, -0.11, z + 1.3, 0.11, 0.11, z + 1.52, R_GOLD)
    m.vs = [(x - 0.5, y - 0.5, z) for x, y, z in m.vs]
    return m, img


MODELS = {"NI Bar": (bar, (7, 3), True, 0.14), "NI Tower Cap": (tower_cap, (2, 2), False, 0.0)}
