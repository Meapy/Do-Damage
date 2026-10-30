"""The realm portals, sunk into the Nexus's floor and drawn flat on it (`DrawOnGround` with
`GroundSize`, the Rust client's view.rs): a cavern opening into the dark for the realms, and a
great vortex in a ring of carved stone for Realm 2. Each is a few frames the client plays in turn."""
import math

from .px import hexc as C, canvas, dither, hash2, mix, put
from .sprites import MOSS, STONE, BARK

DARK = [C("#000000"), C("#07050a"), C("#100b14"), C("#1c1520"), C("#2a2130"), C("#3a3040")]
EMBER = [C("#3a0c10"), C("#6a1416"), C("#a42a1a"), C("#e05a24"), C("#ff9a48"), C("#ffd890")]
VORTEX = [C("#0c0618"), C("#1e0c3a"), C("#3a1466"), C("#6224a0"), C("#9a3ccc"), C("#d06ae6"),
          C("#ff9ae8"), C("#ffe0f8")]
RIM = [C("#2e2a34"), C("#46424e"), C("#5e5a68"), C("#7a7684"), C("#9894a2"), C("#bab6c2")]


def cavern(frame, frames=4, size=32):
    """A cave mouth in the ground: a rim of mossy rocks, rough stone steps going down from its
    south side, the far wall catching the light, darkness below with an ember glow breathing
    in its depths and sparks drifting up."""
    img = canvas(size)
    c = (size - 1) / 2
    r_out, r_in = size / 2 - 0.5, size / 2 - 4.5
    pulse = 0.5 + 0.5 * math.sin(frame / frames * math.tau)
    for y in range(size):
        for x in range(size):
            dx, dy = x - c, y - c
            # a little irregular, as a cave mouth is
            wob = 1.0 + 0.045 * math.sin(math.atan2(dy, dx) * 4 + 1.3) + 0.025 * math.sin(math.atan2(dy, dx) * 7)
            r = math.hypot(dx, dy) / wob
            if r > r_out:
                continue
            if r > r_in:
                # the rim: boulders lit from the upper left, moss on their tops
                k = int(math.atan2(dy, dx) / math.tau * 22) % 22
                rock = hash2(k, 0, 7)
                t = 0.62 - 0.35 * (dx + dy) / size + 0.2 * (rock - 0.5) - 0.25 * ((r - r_in) / (r_out - r_in)) ** 3
                if (r - r_in) < 0.9 or hash2(x, y, 3) > 0.92:
                    t -= 0.25
                col = dither(RIM, t, x, y)
                if dy < -2 and r > r_out - 1.6 and hash2(x, y, 5) > 0.45:
                    col = MOSS[1 + int(hash2(x, y, 6) * 3)]
                put(img, x, y, col)
                continue
            depth = 1 - r / r_in                       # 0 at the edge, 1 in the middle
            if dy > 1 and abs(dx) < 4.5 and r > r_in * 0.3:
                # the steps down, on the near side: each a lit tread over a dark riser
                step = int((r_in - r) / 2.2)
                tread = (r_in - r) % 2.2 < 1.2
                t = (0.75 if tread else 0.35) - step * 0.13 - 0.06 * abs(dx)
                put(img, x, y, dither(RIM, t, x, y) if t > 0.05 else DARK[2])
                continue
            # the far wall is lit, the near one in shadow, the bottom dark
            wall = max(0.0, -dy / r_in) * (1 - depth) * 1.6
            t = 0.55 * (1 - depth) ** 1.5 + wall * 0.45
            col = dither(DARK, t, x, y)
            # roots hanging over the north edge
            if dy < -r_in * 0.45 and depth < 0.3 and hash2(int(x), 0, 11) > 0.8:
                col = BARK[1 + int(depth * 5) % 2]
            # the glow far down
            g = max(0.0, 1 - math.hypot(dx * 1.1, dy + 1.5) / (r_in * 0.5))
            if g > 0:
                gl = g * (0.45 + 0.3 * pulse)
                if gl > 0.15:
                    col = dither(EMBER, gl * 0.85, x, y)
            put(img, x, y, col)
    # sparks rising out of it, moving up each frame
    for i in range(6):
        sx = c + (hash2(i, 1, 9) - 0.5) * r_in * 1.0
        sy = c + 3 - ((hash2(i, 2, 9) * r_in * 1.6 + frame * 2.5) % (r_in * 1.6))
        if math.hypot(sx - c, sy - c) < r_in - 1:
            put(img, int(sx), int(sy), EMBER[4 + (i + frame) % 2])
    return img


def vortex(frame, gems, frames=8, size=48):
    """Realm 2's portal: a vortex of violet and rose light turning in a ring of carved stone, a gem
    for each of the realm's eleven lands set round the ring, a band of runes inside it."""
    img = canvas(size)
    c = (size - 1) / 2
    r_out, r_ring, r_in = size / 2 - 0.5, size / 2 - 4.0, size / 2 - 6.5
    phase = frame / frames * math.tau
    gem_angles = [i / len(gems) * math.tau - math.pi / 2 for i in range(len(gems))]
    for y in range(size):
        for x in range(size):
            dx, dy = x - c, y - c
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx)
            if r > r_out:
                continue
            if r > r_ring:
                # the stone ring, its top face lit from the upper left, a groove in its middle
                t = 0.6 - 0.3 * (dx + dy) / size
                if abs(r - (r_ring + r_out) / 2) < 0.6:
                    t -= 0.3
                if r > r_out - 0.8:
                    t -= 0.25
                t += 0.08 * (hash2(x, y, 4) - 0.5)
                put(img, x, y, dither(RIM, t, x, y))
                continue
            if r > r_in:
                # the rune band: dark stone with marks glowing in time with the vortex
                k = int((a + math.pi) / math.tau * 36)
                lit = (k + frame) % 3 == 0 and abs(r - (r_in + r_ring) / 2) < 0.8 and hash2(k, 1, 5) > 0.3
                put(img, x, y, VORTEX[6] if lit else dither(RIM, 0.12 + 0.1 * hash2(x, y, 6), x, y))
                continue
            # the vortex: spiral arms turning, brighter toward the eye
            u = r / r_in
            arms = math.sin(3 * (a + phase) + u * 7.5)
            v = 0.25 + 0.35 * (1 - u) + 0.28 * arms * (0.4 + 0.6 * u) + 0.06 * (hash2(x, y, frame) - 0.5)
            if u < 0.18:
                v = 0.9 + (0.18 - u)
            put(img, x, y, dither(VORTEX, v, x, y))
    # the gems on the ring
    for g, ang in zip(gems, gem_angles):
        gx = c + math.cos(ang) * (r_ring + r_out) / 2
        gy = c + math.sin(ang) * (r_ring + r_out) / 2
        for ox, oy in ((0, 0), (1, 0), (0, 1), (1, 1)):
            col = mix(g, C("#ffffff"), 0.55) if (ox, oy) == (0, 0) else (mix(g, C("#000000"), 0.35) if (ox, oy) == (1, 1) else g)
            put(img, int(gx) + ox, int(gy) + oy, col)
    # motes drawn into the eye
    for i in range(10):
        ang = hash2(i, 1, 2) * math.tau - phase * 1.5
        rr = r_in * (1 - ((hash2(i, 2, 2) + frame / frames) % 1.0)) * 0.95
        put(img, int(c + math.cos(ang) * rr), int(c + math.sin(ang) * rr), VORTEX[7])
    return img
