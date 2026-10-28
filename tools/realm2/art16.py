"""Realm 2's bigger sprites, drawn by code rather than pixel maps (tools/build_realm2.py uses them):
the Realm 2 portal, the waystone beacon that marks a safe arrival point, and the Pentaract Tower
(the realm event's, in Realm 1 too). Each function returns its animation frames as 16 by 16 RGBA
images. Light comes from the upper left, as on the game's other sprites; the client draws the black
outline round characters, so these have none of their own.
"""
import math

from PIL import Image

from . import art

C = art.hexc


def _mix(a, b, f):
    return tuple(int(round(a[i] + (b[i] - a[i]) * f)) for i in range(3))


def _img():
    return Image.new("RGBA", (16, 16), (0, 0, 0, 0))


def _put(img, x, y, c, a=255):
    if 0 <= x < 16 and 0 <= y < 16:
        img.putpixel((x, y), tuple(c) + (a,))


# ------------------------------------------------------------------------------------------------
# the Realm 2 portal: a gateway like the realm's own stone arch, grander: dark violet stone edged in
# gold, a gem for each of the eleven lands set round the arch, and a vortex turning inside

ARCH_STONE = [C("#1c1226"), C("#2e1f42"), C("#4a3468"), C("#6c5290")]   # dark to light
GOLD = [C("#7a4a12"), C("#c88a24"), C("#ffd25a"), C("#fff4c0")]
VORTEX = [C("#12081e"), C("#3a1868"), C("#7a3ad0"), C("#38c8e8"), C("#d8fbff")]


def portal_frames(gem_colours, frames=4):
    """The arch with the vortex turned a little further in each frame; the gems glint in turn."""
    out = []
    cx, cy = 7.5, 8.0           # the arch's centre: the top is a half circle round it
    outer, inner = 7.4, 4.6
    for f in range(frames):
        img = _img()
        phase = f * math.pi * 2 / frames
        for y in range(16):
            for x in range(16):
                px, py = x + 0.5, y + 0.5
                dx = px - (cx + 0.5)
                if py < cy:
                    d = math.hypot(dx, py - cy)
                else:
                    d = abs(dx)
                if y >= 14:
                    # two steps at its foot, wider than the arch
                    if 0 <= x <= 15 and (y == 15 or 1 <= x <= 14):
                        step = ARCH_STONE[2] if y == 14 else ARCH_STONE[1]
                        if x <= 2 or (y == 14 and x == 3):
                            step = _mix(step, ARCH_STONE[3], 0.5)
                        if x >= 13:
                            step = ARCH_STONE[0]
                        _put(img, x, y, step)
                    continue
                if d > outer:
                    continue
                if d <= inner:
                    # the vortex: three arms turning round a bright eye
                    r = math.hypot(dx, py - 9.0)
                    a = math.atan2(py - 9.0, dx)
                    v = math.sin(3 * a + r * 1.25 - phase) * 0.5 + 0.5
                    depth = max(0.0, 1 - r / 5.2)
                    t = v * 0.55 + depth * 0.65
                    i = min(len(VORTEX) - 1, int(t * len(VORTEX)))
                    c = VORTEX[i]
                    if r < 1.1:
                        c = VORTEX[4]
                    _put(img, x, y, c)
                    continue
                band = (d - inner) / (outer - inner)       # 0 at the opening, 1 at the outside
                if d <= inner + 1.0:
                    # the gold trim round the opening, lit from the upper left
                    lit = 2 if (dx < 0 and py < cy) else 1 if dx < 0 or py < cy - 3 else 0
                    _put(img, x, y, GOLD[lit + 1] if lit == 2 else GOLD[lit])
                else:
                    shade = 2 if dx < -1.5 else 1 if dx < 2 else 0
                    if py < cy - 4 and dx < 1:
                        shade = 3
                    if band > 0.85:
                        shade = max(0, shade - 1)
                    _put(img, x, y, ARCH_STONE[shade])
        # the gems: eleven round the arch's crown, one glinting brighter each frame
        mid = inner + 1.0 + (outer - inner - 1.0) / 2 + 0.2
        for i, col in enumerate(gem_colours):
            a = math.pi + math.pi * (i + 0.5) / len(gem_colours)
            gx = int(round(cx + 0.5 + math.cos(a) * mid - 0.5))
            gy = int(round(cy + math.sin(a) * mid - 0.5))
            bright = (i + f * 3) % len(gem_colours) < 3
            _put(img, gx, gy, _mix(col, (255, 255, 255), 0.55) if bright else col)
        # the keystone, gold, and a glow at the vortex's heart
        _put(img, 7, 0, GOLD[2])
        _put(img, 8, 0, GOLD[1])
        out.append(img)
    return out


# ------------------------------------------------------------------------------------------------
# the waystone beacon: a pale stone obelisk banded with gold, a crystal floating over it and
# pulsing, where players arrive and teleport to

OBELISK = [C("#5a5470"), C("#8c86a6"), C("#bdb8d4"), C("#ecebf6")]
CRYSTAL = [C("#1a6e8c"), C("#38c8e8"), C("#a8f4ff"), C("#ffffff")]


def beacon_frames(frames=4):
    out = []
    for f in range(frames):
        img = _img()
        pulse = (math.sin(f * math.pi * 2 / frames) + 1) / 2      # 0 to 1
        # the plinth: two wide steps
        for y, (x0, x1) in ((15, (2, 13)), (14, (3, 12)), (13, (4, 11))):
            for x in range(x0, x1 + 1):
                c = OBELISK[1] if y == 15 else OBELISK[2]
                if x <= x0 + 1:
                    c = OBELISK[3] if y != 15 else OBELISK[2]
                if x >= x1 - 1:
                    c = OBELISK[0]
                _put(img, x, y, c)
        # the shaft, tapering, banded with gold and inscribed with a glowing rune
        for y in range(6, 13):
            hw = 1 if y < 8 else 2
            x0, x1 = 8 - hw, 7 + hw
            for x in range(x0, x1 + 1):
                c = OBELISK[3] if x == x0 else OBELISK[2] if x < 8 else OBELISK[1] if x < x1 else OBELISK[0]
                if y in (8, 11):
                    c = GOLD[2] if x <= 7 else GOLD[1]
                _put(img, x, y, c)
        for y in (9, 10):
            _put(img, 7, y, _mix(CRYSTAL[1], CRYSTAL[3], pulse * 0.6))
        # the pointed cap
        _put(img, 7, 5, OBELISK[3])
        _put(img, 8, 5, OBELISK[1])
        # the crystal over it, bobbing a pixel with the pulse
        lift = 0 if pulse > 0.5 else 1
        diamond = [(7, 0), (8, 0), (6, 1), (7, 1), (8, 1), (9, 1), (6, 2), (7, 2), (8, 2), (9, 2), (7, 3), (8, 3)]
        for x, y in diamond:
            c = CRYSTAL[2] if x <= 7 else CRYSTAL[1]
            if y == 3:
                c = CRYSTAL[0] if x == 8 else CRYSTAL[1]
            if (x, y) == (7, 1):
                c = CRYSTAL[3]
            _put(img, x, y + lift, c)
        # sparkles drifting round it, more as it brightens
        sparks = [(4, 2), (11, 1), (3, 5), (12, 4), (5, 0), (10, 4)]
        for i, (x, y) in enumerate(sparks):
            if (i + f) % 3 == 0 or pulse > 0.8 and i % 2 == 0:
                _put(img, x, y, CRYSTAL[2], 220)
        out.append(img)
    return out


# ------------------------------------------------------------------------------------------------
# the Pentaract Tower: an obsidian spire in tiers, red light burning in its windows and down veins
# in its stone, a pentagram's points glowing at its foot and an ember at its tip. Its frames: dim,
# lit, blazing, and the veins lit; the XML plays them as the old tower's did.

TOWER = [C("#16141c"), C("#2a2733"), C("#444050"), C("#67637a"), C("#8e8aa2")]  # dark to light
RED = [C("#5a0812"), C("#a8121e"), C("#ee2a2a"), C("#ff8a6a"), C("#ffe2d0")]

# half widths, row by row from the tip, round the spire's middle at x = 8
TOWER_PROFILE = [1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 5, 6, 7]
TIER_TOPS = {5, 9, 12}
WINDOW_ROWS = (3, 7, 10)


def tower_frames():
    """[dim, lit, blazing, veins] frames."""
    out = []
    for level in range(4):
        img = _img()
        glow = RED[1 + min(level, 2)]                 # windows: dark red, red, bright
        tip = RED[2 + min(level, 2)] if level < 3 else RED[3]
        for y, hw in enumerate(TOWER_PROFILE):
            for x in range(16):
                t = (x + 0.5 - 8) / hw
                if abs(t) > 1.0001:
                    continue
                shade = 3 if t < -0.45 else 2 if t < 0.25 else 1
                if t > 0.8 and hw >= 2:
                    shade = 0
                if y in TIER_TOPS and t < 0.6:
                    shade = min(4, shade + 1)         # the lit ledge of each tier
                if y == 15:
                    shade = max(0, shade - 1)
                _put(img, x, y, TOWER[shade])
        # windows: a slit up the middle of each tier, the lowest with two beside it
        for y in WINDOW_ROWS:
            _put(img, 7, y, glow)
            _put(img, 8, y, _mix(glow, RED[0], 0.35))
        _put(img, 6, 12, glow)
        _put(img, 9, 12, _mix(glow, RED[0], 0.35))
        if level >= 2:
            _put(img, 7, 7, RED[4] if level == 2 else RED[3])     # the core blazes
        # veins of red light down the stone
        if level >= 3:
            for x, y in ((7, 4), (8, 5), (7, 6), (6, 8), (6, 9), (9, 11), (5, 13), (10, 13)):
                _put(img, x, y, RED[1])
        # the ember at the tip, and the pentagram's five points at its foot
        _put(img, 7, 0, tip)
        _put(img, 8, 0, _mix(tip, RED[0], 0.3))
        for x, y in ((3, 15), (12, 15), (5, 14), (10, 14), (7, 15)):
            _put(img, x, y, glow if level else RED[1])
        out.append(img)
    return out


def tower_corpse():
    """The fallen tower, waiting to rise again: its stone cold, the windows and the ember out."""
    img = tower_frames()[0]
    px = img.load()
    for y in range(16):
        for x in range(16):
            r, g, b, a = px[x, y]
            if a and r > g + 30:            # the red light: dark, burnt-out glass
                px[x, y] = TOWER[0] + (a,)
    return img
