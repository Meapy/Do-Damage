"""The Black Between's smaller art (tools/build_black_between.py puts it on its sheets): the
minions (8x8), the shots (8x8, pointing right), the floor (8x8 tiles), the walls of light the
bosses raise, Vesper's dusk mines, and the Rift of the Between (16x16, four frames turning).

Pixel maps are one character a pixel ('.' clear), light from the upper left, as on the game's
other sprites; the client draws the black outline round characters and shots.
"""
import math
import random

from PIL import Image


def C(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def from_map(rows, pal, alpha=255):
    h, w = len(rows), len(rows[0])
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for y, r in enumerate(rows):
        assert len(r) == w, (r, len(r), w)
        for x, ch in enumerate(r):
            if ch != ".":
                img.putpixel((x, y), C(pal[ch]) + (alpha,))
    return img


def put(img, x, y, c, a=255):
    x, y = int(x), int(y)
    if 0 <= x < img.size[0] and 0 <= y < img.size[1]:
        img.putpixel((x, y), (C(c) if isinstance(c, str) else tuple(c)) + (a,))


# the son's gold and white, the daughter's dusk
GOLD = {"1": "#7a4c0e", "2": "#c08a1c", "3": "#ecc040", "4": "#fff0a8", "w": "#fffaf0", "p": "#f6f2e6",
        "P": "#c4baa4", "t": "#d09a28", "k": "#3a2410", "c": "#a8401a", "C": "#e07028", "B": "#ffffff",
        "b": "#dce2f0", "d": "#8a92aa", "g": "#a06a12", "G": "#ffd24a", "f": "#f2cca4"}
DUSK = {"1": "#1a0c30", "2": "#3a1d6a", "3": "#5f34a3", "4": "#9064d8", "s": "#f0e4ff", "e": "#ff6ae8"}

# ------------------------------------------------------------------------------------------------
# minions: (stand, attack)

TEMPLAR = ["...Cc...", "..tppt.B", "..pkkP.B", ".tpppPtB", ".PtGtPfG", "..pPpP..", "..p..P..", "..t..t.."]
TEMPLAR_HIT = ["...Cc...", "..tppt..", "..pkkP..", ".tpppPtB", ".PtGtPfB", "..pPpPGB", "..p..P.B", "..t..t.."]
SHADE = ["..3333..", ".344432.", ".31e1e2.", ".311112.", "..2s222.", "..2222..", "...21.2.", "....1..."]
SHADE_HIT = ["..3333..", ".344432.", ".3eeee2.", ".311112.", "..2s222.", ".22222..", "..21.2..", "...1...."]
SENTINEL = ["s...t...", "...t4t..", "..t434t.", ".t3VY3t.", "..t232t.", "s..t2t.s", "....t...", "..s....."]
SENTINEL_HIT = [".s..t..s", "...t4t..", "..t4W4t.", ".t3VY3t.", "..t232t.", "...t2t..", "s...t..s", "........"]
SENTINEL_PAL = {"s": "#f0ecff", "t": "#7a70c0", "4": "#9a8ae0", "3": "#5a48a0", "2": "#251a4a",
                "V": "#c070ff", "Y": "#ffcf40", "W": "#ffffff"}


def minions():
    """name -> (stand, attack), 8x8."""
    return {
        "Dawn Templar": (from_map(TEMPLAR, GOLD), from_map(TEMPLAR_HIT, GOLD)),
        "Dusk Shade": (from_map(SHADE, DUSK), from_map(SHADE_HIT, DUSK)),
        "Starbound Sentinel": (from_map(SENTINEL, SENTINEL_PAL), from_map(SENTINEL_HIT, SENTINEL_PAL)),
    }


# ------------------------------------------------------------------------------------------------
# shots, 8x8, pointing right

def _blank():
    return Image.new("RGBA", (8, 8), (0, 0, 0, 0))


def dusk_bolt():
    """A violet comet."""
    img = _blank()
    for x in range(8):
        for y in range(8):
            d = math.hypot(x - 5.5, y - 3.5)
            if d < 2.1:
                put(img, x, y, "#ffffff" if d < 0.8 else ("#e0b0ff" if d < 1.4 else "#a040f0"))
            elif x < 5 and abs(y - 3.5) < (x + 1) * 0.3:
                put(img, x, y, "#5f34a3" if x < 2 else "#8a4ae0")
    return img


def umbral_orb():
    """A dark orb ringed in violet: Vesper's rings."""
    img = _blank()
    for x in range(8):
        for y in range(8):
            d = math.hypot(x - 3.5, y - 3.5)
            if d < 3.2:
                put(img, x, y, "#1a0c30" if d < 1.4 else ("#7a2ad0" if d < 2.4 else "#c070ff"))
    put(img, 2, 2, "#f0d0ff")
    return img


def crescent():
    """A golden sword-wave."""
    img = _blank()
    for y in range(8):
        for x in range(8):
            d1 = math.hypot(x - 2.0, y - 3.5)
            d2 = math.hypot(x - 0.6, y - 3.5)
            if d1 < 4.2 and d2 > 4.0:
                put(img, x, y, "#ffffff" if d2 < 4.8 else ("#ffd24a" if d2 < 5.8 else "#c08a1c"))
    return img


def sunspark():
    """A four-pointed sun star."""
    img = _blank()
    for (x, y, c) in ((3, 0, 1), (4, 0, 1), (3, 1, 2), (4, 1, 2), (0, 3, 1), (1, 3, 2), (6, 3, 2), (7, 3, 1),
                      (0, 4, 1), (1, 4, 2), (6, 4, 2), (7, 4, 1), (3, 6, 2), (4, 6, 2), (3, 7, 1), (4, 7, 1)):
        put(img, x, y, ["", "#c08a1c", "#ffd24a"][c])
    for x in range(2, 6):
        for y in range(2, 6):
            put(img, x, y, "#ffffff" if 3 <= x <= 4 and 3 <= y <= 4 else "#fff0a8")
    return img


def eclipse_orb():
    """Half dusk, half dawn, a black heart: the father's."""
    img = _blank()
    for y in range(8):
        for x in range(8):
            d = math.hypot(x - 3.5, y - 3.5)
            if d < 3.8:
                if d < 1.6:
                    put(img, x, y, "#05030a")
                elif d < 2.4:
                    put(img, x, y, "#ffffff")
                else:
                    put(img, x, y, "#a040f0" if x + y < 7 else "#ffcf40")
    return img


def starbound_mote():
    """The Sentinels' pale star."""
    img = _blank()
    for (x, y) in ((3, 1), (4, 1), (1, 3), (6, 3), (1, 4), (6, 4), (3, 6), (4, 6)):
        put(img, x, y, "#7a70c0")
    for x in range(2, 6):
        for y in range(2, 6):
            put(img, x, y, "#ffffff" if 3 <= x <= 4 and 3 <= y <= 4 else "#c8c0ff")
    return img


SHOTS = {
    "Dusk Bolt": dusk_bolt,
    "Umbral Orb": umbral_orb,
    "Dawn Crescent": crescent,
    "Sunspark": sunspark,
    "Eclipse Orb": eclipse_orb,
    "Starbound Mote": starbound_mote,
}


# ------------------------------------------------------------------------------------------------
# things on the floor

def dusk_mine(frame):
    """A dusk mine: a dark star pulsing violet, about to burst."""
    img = _blank()
    glow = ("#7a2ad0", "#c070ff")[frame]
    for (x, y) in ((3, 0), (4, 0), (0, 3), (0, 4), (7, 3), (7, 4), (3, 7), (4, 7)):
        put(img, x, y, glow)
    for x in range(1, 7):
        for y in range(1, 7):
            if abs(x - 3.5) + abs(y - 3.5) < 3.6:
                put(img, x, y, "#1a0c30" if abs(x - 3.5) + abs(y - 3.5) < 1.6 else ("#5f34a3" if (x + y) % 2 else "#3a1d6a"))
    put(img, 3, 3, "#ff6ae8" if frame else "#c040e0")
    return img


def wall_of_light(kind, frame):
    """A wall the bosses raise (16x16, standing): Vesper's violet veil or Aurel's dawnlight, a
    shimmering column of light over a glowing seam in the floor."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    if kind == "veil":
        cols = ["#2a0c4a", "#5f2aa0", "#a060f0", "#e8c8ff"]
    else:
        cols = ["#7a4c0e", "#d09a28", "#ffe070", "#fffbe8"]
    rnd = random.Random(frame * 7 + (kind == "veil"))
    for y in range(16):
        for x in range(2, 14):
            core = 1 - abs(x - 7.5) / 6
            wave = 0.5 + 0.5 * math.sin(y * 0.8 + frame * 1.9 + x * 0.4)
            v = core * 0.75 + wave * 0.35
            k = 3 if v > 0.92 else 2 if v > 0.62 else 1 if v > 0.32 else 0
            a = int(120 + 135 * min(1.0, core + 0.2))
            put(img, x, y, cols[k], a)
    for _ in range(5):
        put(img, rnd.randrange(3, 13), rnd.randrange(0, 16), "#ffffff")
    return img


# ------------------------------------------------------------------------------------------------
# the floor: 8x8 tiles

def _tile(fn, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    fn(img, rnd)
    return img


def astral_stone(seed):
    """Dark violet-slate floor: soft mottling, a seam here and there (not round every tile, which
    reads as a grid), a fleck of starlight caught in some."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                v = rnd.random()
                put(img, x, y, "#2a2442" if v > 0.22 else ("#262039" if v > 0.08 else "#30294c"))
        if seed % 2:
            for y in range(8):
                if rnd.random() < 0.8:
                    put(img, 0, y, "#211c34")
        else:
            for x in range(rnd.randrange(3, 8)):
                put(img, x, 0, "#211c34")
        if seed % 3 == 0:
            put(img, rnd.randrange(2, 6), rnd.randrange(2, 6), "#7a6ac0")
    return _tile(f, seed)


def platform_rim(seed):
    """The platform's rim: polished dark stone, lighter than the floor, flecked with gold and a
    violet glint now and then. No band or seam along it, which reads as broken blocks where the
    round edge steps from square to square."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                v = rnd.random()
                put(img, x, y, "#3a3260" if v > 0.25 else ("#342c56" if v > 0.08 else "#443a70"))
        for _ in range(2):
            put(img, rnd.randrange(8), rnd.randrange(8), "#d8a030" if rnd.random() < 0.7 else "#ffd24a")
        if seed % 2:
            put(img, rnd.randrange(8), rnd.randrange(8), "#a070f0")
    return _tile(f, seed)


def underside(seed, rim=False):
    """The rock the platform floats on (never on the map: the client hangs it under the platform's
    edges): dark violet stone with veins of starlight, or (rim) the band of dressed stone and gold
    just under the floor."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                if rim:
                    c = "#2e2648" if y < 6 else "#251e3e"
                    if (x + y * 3 + seed) % 11 == 0:
                        c = "#3c3260"
                else:
                    v = rnd.random()
                    c = "#1a1430" if v > 0.3 else ("#14102a" if v > 0.1 else "#221a3e")
                put(img, x, y, c)
        if not rim:
            x, y = rnd.randrange(8), rnd.randrange(8)
            for i in range(rnd.randrange(2, 5)):
                put(img, (x + i) % 8, (y + i // 2) % 8, "#4a3a86")
            if seed % 2:
                put(img, rnd.randrange(8), rnd.randrange(8), "#c8b8ff")
    return _tile(f, seed)


def pylon(frame):
    """A star pylon on the rim: a slim dark obelisk, a crystal of dusk or dawn glowing at its tip."""
    img = _blank()
    glow = (("#7a2ad0", "#d8a0ff"), ("#c08a1c", "#fff0a8"))[frame % 2]
    for y in range(2, 8):
        put(img, 3, y, "#3e3562" if y < 7 else "#251f3d")
        put(img, 4, y, "#251f3d")
    put(img, 2, 7, "#332b52"); put(img, 5, 7, "#1c1830")
    put(img, 3, 1, glow[1]); put(img, 4, 1, glow[0]); put(img, 3, 0, glow[0])
    put(img, 2, 1, glow[0]); put(img, 4, 0, glow[1] if frame > 1 else glow[0])
    return img


def veil(seed):
    """Vesper's ward on the floor: a violet weave that burns to stand on."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                k = (x + y + seed) % 4
                c = "#5a1a90" if k == 0 else ("#2c0c4a" if (x - y + seed) % 4 == 0 else "#3a1060")
                put(img, x, y, c)
        put(img, rnd.randrange(8), rnd.randrange(8), "#c070ff")
    return _tile(f, seed)


def black_between(seed):
    """The void come up through the floor: starry black, a violet shimmer at its edge (it burns)."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                put(img, x, y, "#08051a" if rnd.random() > 0.08 else "#1a0f36")
        for _ in range(2):
            put(img, rnd.randrange(8), rnd.randrange(8), "#c8c0ff" if rnd.random() < 0.5 else "#6a5aa8")
    return _tile(f, seed)


def constellation(seed):
    """A star of the Constellation Path: safe footing in the dark."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                put(img, x, y, "#121030" if (x + y) % 2 else "#16133a")
        for i in range(8):
            put(img, i, 3 + (seed % 2), "#3a3478")
        put(img, 3, 3, "#ffffff"); put(img, 4, 4, "#ffffff"); put(img, 3, 4, "#d8d0ff"); put(img, 4, 3, "#d8d0ff")
        put(img, 2, 3, "#8a80d0"); put(img, 5, 4, "#8a80d0"); put(img, 3, 2, "#8a80d0"); put(img, 4, 5, "#8a80d0")
    return _tile(f, seed)


def void_scar(seed):
    """The ground round the Mysterious One where he steps into another world (Oryx's Elysium): the
    void showing through cracks, harmless to stand on."""
    def f(img, rnd):
        for y in range(8):
            for x in range(8):
                put(img, x, y, "#0c0820" if rnd.random() > 0.1 else "#1a1036")
        for _ in range(3):
            put(img, rnd.randrange(8), rnd.randrange(8), "#c8c0ff" if rnd.random() < 0.5 else "#6a5aa8")
        x, y = rnd.randrange(8), rnd.randrange(8)
        for i in range(4):
            put(img, (x + i) % 8, (y + (i * (seed % 3 - 1))) % 8, "#7a2ad0" if i % 2 else "#d8a030")
    return _tile(f, seed)


def whirl():
    """The Cyclone Arrow (16x16): Aurel's tornado seen from above, three arms of gold and white
    curling into a bright eye. Its object spins it (<Rotation>) as it flies."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    ramp = ["#fff6d0", "#ffe27a", "#ffd24a", "#d6981e", "#8a5a12"]
    for arm in range(3):
        for k in range(40):
            t = k / 39
            a = arm * 2 * math.pi / 3 + t * 2.6 * math.pi
            r = 0.8 + t * 6.6
            x, y = 7.5 + math.cos(a) * r, 7.5 + math.sin(a) * r
            c = ramp[min(4, int(t * 5))]
            put(img, x, y, c)
            if t < 0.6:
                put(img, x + 0.5, y + 0.5, c)
    for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        put(img, 7 + dx, 7 + dy, "#ffffff")
    return img


def unstable_essence():
    """Unstable Essence (8x8): a shard of the Between that won't hold still, spikes of violet
    crystal flung out from a white-gold heart."""
    rows = ["Y..P...Y", ".v.Vv.v.", "..vPVv..", "PVPWYVvV", ".uvVYvu.", "..vVvu..", ".u.vu.u.", "u..u...u"]
    img = _blank()
    for y, r in enumerate(rows):
        assert len(r) == 8, r
        for x, ch in enumerate(r):
            if ch != ".":
                put(img, x, y, ITEM_PAL[ch])
    return img


def cosmic_anvil(frame):
    """The Cosmic Anvil (16x16): a black anvil of star-iron on a plinth, gold-edged, a rift of the
    Between glowing in its face, pulsing violet and gold."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    rows = [
        "................",
        "................",
        "..GGGGGGGGGGGG..",
        ".gkkkkkkkkkkkkg.",
        "gkk3kkkkkkkkkkkg",
        ".gkkkkkkkkkkkkg.",
        "...gkkkkkkkkg...",
        ".....kkkkkk.....",
        ".....kkVYkk.....",
        ".....kkYVkk.....",
        ".....kkkkkk.....",
        "....gkkkkkkg....",
        "...gkkkkkkkkg...",
        "..GGGGGGGGGGGG..",
        "..gggggggggggg..",
        "................",
    ]
    glow = (("#b040ff", "#ffd24a"), ("#ffd24a", "#e0b0ff"))[frame % 2]
    pal = {"G": "#ffd24a", "g": "#a07418", "k": "#1a1430", "3": "#4a3a86", "V": glow[0], "Y": glow[1]}
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch != ".":
                put(img, x, y, pal[ch])
    # starlight on the iron, and the glow spilling round the rift
    for (x, y) in ((4, 3), (9, 4), (12, 3), (7, 11), (10, 12)):
        put(img, x, y, "#8a7ad0" if (x + y + frame) % 2 else "#d8d0ff")
    for (x, y) in ((6, 7), (9, 7), (6, 10), (9, 10)):
        put(img, x, y, glow[0], 160)
    return img


# the Mysterious One's family's gear (8x8 items): name -> pixel rows, in one palette of ramps
# (gold: Y highlight, G, g, o shadow; violet: P, V, v, u; night: n, k, K; W white, B sky, R rift)
ITEM_PAL = {
    "W": "#ffffff", "Y": "#fff3b0", "G": "#ffd24a", "g": "#d6981e", "o": "#8a5a12",
    "P": "#f0d8ff", "V": "#c070ff", "v": "#8a3fe0", "u": "#4e2392",
    "n": "#463a78", "k": "#2a2248", "K": "#17122c", "B": "#7fe6ff", "R": "#ff7ad9",
}
ITEMS = {
    # a gold quiver slung corner to corner, a violet strap round it, Aurel's tornado whirling out
    # of its mouth
    "Quiver of the Solar Cyclone": [".....YWY", "....oWGW", "...ogGY.", "..ogGYo.", ".oVVYo..", "ogGYo...", "oGYo....", ".oo....."],
    # a sprung trap of gold jaws; inside, the three of them: Aurel's gold, Vesper's violet, their
    # father's rift round a white star
    "Trinity Trap": [".G.GG.G.", "GgYGGYgG", ".gVvRug.", "GVPWWRuG", "GvVWWuRG", ".gvVuRg.", "GgYGGYgG", ".G.GG.G."],
    # a gold helm with white wings, a sky gem at its brow and a dark visor
    "Helm of the Dawn Blade": ["W..YG..W", "WWGYGgWW", ".WGGBgW.", "..GYGg..", "..GKKg..", "..GKkg..", "..gGgo..", "...oo..."],
    # a dusk crystal, lit from the left, a pale glint at its heart
    "Prism of the Dusk Veil": ["...PV...", "..PVVv..", ".PVPVvu.", "PVPWVvuu", ".VVPvvu.", "..vVvu..", "...vu...", "........"],
    # a hooded cloak of the night, gold-trimmed, the Between's rift at its heart
    "Cloak of the Planar Rift": ["..kKKk..", ".kKnnKk.", ".KnVRnK.", "kKnRVnKk", "GKnnnnKG", "GKknnkKG", "gKkKKkKg", ".gK..Kg."],
    # a round flask of the dusk under a gold stopper, its glass catching the light
    "Vial of Cascading Dusk": ["...GG...", "...go...", "...nn...", "..nVVn..", ".nVPVvn.", ".nPVvun.", ".nvvuun.", "..nnnn.."],
}


def item_icon(name):
    img = _blank()
    for y, r in enumerate(ITEMS[name]):
        assert len(r) == 8, (name, r)
        for x, ch in enumerate(r):
            if ch != ".":
                put(img, x, y, ITEM_PAL[ch])
    return img


TILES = {
    "Astral Stone": [astral_stone(s) for s in (3, 4, 5, 6)],
    "Platform Rim": [platform_rim(s) for s in (1, 2, 3, 4)],
    "Veil of Dusk": [veil(s) for s in (0, 1, 2)],
    "Black Between": [black_between(s) for s in (1, 2, 3)],
    "Constellation Path": [constellation(s) for s in (0, 1)],
    # under the platform's edges (the client's undersides, never on the map)
    "BB Underside Rock": [underside(s) for s in (1, 2, 3, 4)],
    "BB Underside Rim": [underside(s, rim=True) for s in (1, 2)],
    # in Oryx's Elysium, round the Mysterious One as he steps out of his Rift (harmless)
    "Void Scar": [void_scar(s) for s in (1, 2, 3)],
}


# ------------------------------------------------------------------------------------------------
# the Rift of the Between: a galactic portal ringed in dusk and dawn, 16x16, turning

def rift(frame):
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            dx, dy = x - 7.5, y - 7.5
            r = math.hypot(dx, dy)
            if r > 7.6:
                continue
            if r > 6.4:
                ang = math.atan2(dy, dx)
                gold = math.cos(ang - 0.8) > 0
                lit = math.cos(ang + 2.3) * 0.5 + 0.5
                c = ["#7a4c0e", "#c08a1c", "#ffd24a"] if gold else ["#2a1450", "#5f34a3", "#a070f0"]
                put(img, x, y, c[int(lit * 2.99)])
                continue
            ang = math.atan2(dy, dx) + frame * math.pi / 6
            arm = math.sin(ang * 2 - r * 0.9)
            if r < 1.5:
                put(img, x, y, "#ffffff")
            elif arm > 0.55:
                put(img, x, y, "#c8a0ff" if (x + y + frame) % 2 else "#fff0b0")
            elif arm > 0.0:
                put(img, x, y, "#3a1d6a")
            else:
                put(img, x, y, "#0a0718")
    rnd = random.Random(frame)
    for _ in range(4):
        a, r = rnd.random() * 6.28, rnd.random() * 5.5
        put(img, 7.5 + math.cos(a) * r, 7.5 + math.sin(a) * r, "#ffffff")
    return img
