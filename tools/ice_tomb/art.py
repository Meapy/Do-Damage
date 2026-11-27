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
from black_between.small import put  # noqa: E402
from crucible.bosses import thick, line, rect_mask, glow_ring  # noqa: E402
from crucible.small import stone, liquid, pattern, cracked  # noqa: E402

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


def _tile(fn, seed):
    rnd = random.Random(seed * 7919 + 29)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    fn(img, rnd)
    return img


def frosted(base, dark, light, rime="#e8f8ff"):
    """Flagstones under a skin of frost: mottled stone, a seam on some edges, rime creeping in
    from a corner."""
    plain = stone(base, dark, light, fleck=rime, seam=dark)

    def make(seed):
        img = plain(seed)
        rnd = random.Random(seed * 31 + 7)
        cx, cy = rnd.choice(((0, 0), (7, 0), (0, 7), (7, 7)))
        for _ in range(5 + seed % 4):
            x = max(0, min(7, cx + rnd.randint(-2, 2)))
            y = max(0, min(7, cy + rnd.randint(-2, 2)))
            put(img, x, y, rime if rnd.random() < 0.4 else light)
        return img
    return make


def ice_sheet(cols, crack, sparkle_c="#ffffff", cracks=1):
    """Clear ice: bands of colour slanting across, a fine crack or two, a glint."""
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    k = (x + y * 2 + seed * 3) // 3 + (1 if rnd.random() < 0.12 else 0)
                    put(img, x, y, cols[k % len(cols)])
            for _ in range(cracks):
                x, y = rnd.randrange(8), rnd.randrange(8)
                for _ in range(4):
                    put(img, x, y, crack)
                    x = max(0, min(7, x + rnd.choice((-1, 1))))
                    y = max(0, min(7, y + rnd.choice((-1, 0, 1))))
            put(img, rnd.randrange(8), rnd.randrange(8), sparkle_c)
        return _tile(f, seed)
    return make


def drift(cols, shadow):
    def make(seed):
        def f(img, rnd):
            for y in range(8):
                for x in range(8):
                    v = math.sin((x + seed * 2) * 0.8) * 1.2 + y * 0.35 + rnd.random() * 0.7
                    put(img, x, y, cols[int(v) % len(cols)])
            for _ in range(2):
                put(img, rnd.randrange(8), rnd.randrange(8), shadow)
        return _tile(f, seed)
    return make


# name -> (art, variants)
TILES = {
    # the tomb's halls: pale flagstones furred with frost, darker inlay, a runner of deep blue
    "Ice Tomb Floor": (frosted("#4a6684", "#3e5872", "#587696"), 4),
    "Ice Tomb Inlay": (pattern(["#2c3f5c", "#34496a", "#3e5678"], lambda x, y, s: ((x // 2 + y // 2 + s) % 3), fleck="#8cc8ea"), 3),
    "Rime Runner": (pattern(["#1e2a5a", "#26346a", "#30407c", "#d8c070"], lambda x, y, s: 3 if x in (0, 7) and (y + s) % 3 == 0 else (x + y + s) % 3), 2),
    "Snowdrift": (drift(["#c4d4e6", "#d6e2f0", "#e6eef8", "#f6faff"], "#a8bcd4"), 4),
    # the Frozen Court: white marble veined with frost, an aurora sigil laid in it
    "Court Marble": (frosted("#b8c6d8", "#a6b6ca", "#ccd8e6", rime="#ffffff"), 4),
    "Court Rim": (pattern(["#5e7aa0", "#4c6890", "#7290b6", "#e8c048"], lambda x, y, s: 3 if (x * 3 + y + s) % 11 == 0 else (x + 2 * y + s) % 3), 3),
    "Court Sigil": (pattern(["#2a8a74", "#3ac8a0", "#9060d8", "#c4ffe4"], lambda x, y, s: (x + y + s) % 4 if (x + y) % 2 else 1), 2),
    # the frozen lake: thin ice over black water. It cracks under whoever stands on it
    # (ThinIce in BehaviorDb.IceTomb.cs), and breaks
    "Lake Ice": (ice_sheet(["#3a6e9a", "#4680ac", "#5490bc", "#4074a2"], "#9cd0f0"), 4),
    "Cracking Ice": (cracked("#4680ac", "#2a5a82", "#ffffff"), 3),
    # freezing water: walkable, slow, and it burns with cold
    "Frigid Water": (liquid(["#0a1a34", "#0e2444", "#123056", "#0a1e3c"], "#4a90c8"), 4),
    # where something is coming (any of the fights): pale stripes, harmless
    "Rime Warning": (pattern(["#5a8ab0", "#8cc8ea", "#dcf6ff"], lambda x, y, s: ((x + y + s) // 2) % 3), 3),
    # the Court's phases: each walkable, each burning with cold
    "Avalanche": (drift(["#dceaf8", "#f0f6ff", "#ffffff", "#bcd2ea"], "#8cb0d4"), 3),
    "Aurora Light": (pattern(["#1aa070", "#4ae8a4", "#9060d8", "#c4ffe4"], lambda x, y, s: ((x * 2 - y + s) // 2) % 4), 3),
    "Blizzard": (pattern(["#ffffff", "#dcecf8", "#b0cce4", "#e8f4ff"], lambda x, y, s: ((x - y * 2 + s * 3) // 2) % 4), 3),
    "Frost Fissure": (cracked("#0e2440", "#1a3a5e", "#a8f0ff"), 3),
    "Frozen Scar": (stone("#2c4a6a", "#24405e", "#36587c", fleck="#8cc8ea"), 3),
    "Polar Dark": (pattern(["#0c0a1e", "#1c1638", "#5a2a9a", "#0e0c26"], lambda x, y, s: 2 if (x * 5 + y * 3 + s) % 13 == 0 else (x + y) % 2 * 3), 3),
    "Permafrost": (ice_sheet(["#8cc8ea", "#a8dcf6", "#dcf6ff", "#5ec4e8"], "#ffffff", cracks=2), 3),
    # the floe the Shattered Lake leaves, and its rim
    "Ice Floe": (ice_sheet(["#a8d4ee", "#bce0f4", "#cce8f8", "#9cc8e6"], "#ffffff"), 3),
    "Ice Floe Rim": (pattern(["#e8f6ff", "#ffffff", "#bcdcf0"], lambda x, y, s: (x + y + s) % 3), 2),
    # under the walls
    "Ice Tomb Wall Floor": (stone("#1a2436", "#141c2c", "#222e42"), 2),
    # the glacier caves: rough blue ice underfoot, and the old tomb's mosaic in its finer rooms
    "Glacier Floor": (ice_sheet(["#3c6890", "#44729a", "#365f86", "#4a7aa2"], "#7ab4dc"), 4),
    "Tomb Mosaic": (pattern(["#3a5274", "#4a6488", "#2e4462", "#c8a850"], lambda x, y, s: 3 if (x + y) % 8 == 0 and (x - y) % 4 == 0 else ((x // 2 + y // 2) % 2) * (1 + s % 2) + ((x + y) % 2) * 0), 4),
    "Bone Floor": (stone("#4a5262", "#3e4656", "#5a6474", fleck="#d8d8e0", flecks=3), 3),
    # the chasms: a drop into the dark, mist drifting far below; and the bridges of ice that freeze
    # across them as someone comes near (IceBridge in BehaviorDb.IceTomb.cs)
    "Tomb Abyss": (pattern(["#020408", "#04080e", "#060c16", "#03060c"], lambda x, y, s: ((x * 3 + y * 7 + s * 5) % 17) // 5, fleck="#1a3a5e"), 4),
    "Ice Bridge": (ice_sheet(["#7ab4dc", "#8cc4e8", "#9cd0f0", "#6ca6d2"], "#dcf6ff"), 3),
    "Ice Bridge Rim": (pattern(["#dcf6ff", "#ffffff", "#a8dcf6", "#5ec4e8"], lambda x, y, s: (x + y * 2 + s) % 4), 2),
}


def tiles():
    return {name: [fn(i) for i in range(n)] for name, (fn, n) in TILES.items()}


# =================================================================================================
# walls (8x8): blocks of old ice over dark stone; the top face frosted

def wall_face(seed, sconce=False, frame=0):
    rnd = random.Random(seed * 101 + 3)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    for y in range(8):
        for x in range(8):
            row = y // 4
            brick = (x + (2 if row else 0)) // 4
            edge = y % 4 == 3 or (x + (2 if row else 0)) % 4 == 3
            c = ICE_DEEP[0] if edge else ICE_DEEP[1 + (brick + row + seed) % 2]
            if not edge and rnd.random() < 0.12:
                c = ICE_DEEP[3]
            put(img, x, y, c)
    for x in range(8):
        if rnd.random() < 0.6:
            put(img, x, 0, ICE[3])
    if sconce:
        # a sconce of blue flame
        put(img, 3, 6, STEEL[2]); put(img, 4, 6, STEEL[2]); put(img, 3, 5, STEEL[1]); put(img, 4, 5, STEEL[1])
        f = frame % 2
        put(img, 3, 4, FROST[1 + f]); put(img, 4, 4, FROST[2 - f]); put(img, 3 + f, 3, FROST[3]); put(img, 4 - f, 2, FROST[2])
    return img


def glacier_face(seed):
    """A wall of the glacier caves: rough blue ice in slabs, bright where it catches the light."""
    rnd = random.Random(seed * 53 + 17)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    for y in range(8):
        for x in range(8):
            v = math.sin(x * 1.3 + seed) + math.cos(y * 0.9 + seed * 2) + rnd.random() * 0.8
            put(img, x, y, ICE[0] if v < 0 else (ICE[1] if v < 1.2 else ICE[2]))
    for _ in range(2):
        x = rnd.randrange(8)
        for y in range(rnd.randrange(2, 6)):
            put(img, x, y, ICE[3])
    return img


def wall_top(seed):
    rnd = random.Random(seed * 37 + 11)
    img = Image.new("RGBA", (8, 8), (0, 0, 0, 255))
    for y in range(8):
        for x in range(8):
            v = rnd.random()
            put(img, x, y, SNOW[1] if v > 0.3 else (SNOW[0] if v > 0.1 else SNOW[2]))
    for _ in range(2):
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
    """One of the Court's thrones, carved of ice, its sitter's colour in its back."""
    cv = Canvas(16, 16)
    c = {"warden": STEEL, "oracle": AURORA, "reaver": FROST}[who]
    cv.shade(rect_mask(3, 2, 12, 9), ICE_DEEP)
    cv.shade(rect_mask(2, 9, 13, 12), ICE)
    cv.shade(rect_mask(3, 12, 5, 15), ICE_DEEP); cv.shade(rect_mask(10, 12, 12, 15), ICE_DEEP)
    for y in range(3, 8):
        cv.px(7, y, c[2]); cv.px(8, y, c[3])
    cv.px(3, 1, SNOW[3]); cv.px(12, 1, SNOW[3]); cv.px(7, 1, c[3]); cv.px(8, 1, c[3])
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


def spectral_court(frames, tint):
    return [spectral(f, tint) for f in frames]
