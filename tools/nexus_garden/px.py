"""Pixel helpers shared by the Nexus garden's painters: colours, hashing, pixel maps."""
import math

from PIL import Image


def hexc(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, f):
    f = max(0.0, min(1.0, f))
    return tuple(int(round(x + (y - x) * f)) for x, y in zip(a[:3], b[:3]))


def hash2(x, y, seed):
    """A stable value in 0..1 for a pixel and a seed."""
    h = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return (h ^ (h >> 16)) / 0xFFFFFFFF


def ramp(colours, v):
    """The colour of a ramp (dark to light) at v in 0..1, without blending: pixel art keeps to
    its palette."""
    v = max(0.0, min(0.999, v))
    return colours[int(v * len(colours))]


BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def dither(colours, v, x, y):
    """Like `ramp`, ordered-dithered between neighbouring steps."""
    v = max(0.0, min(0.999, v)) * (len(colours) - 1)
    lo = int(v)
    frac = v - lo
    hi = min(lo + 1, len(colours) - 1)
    return colours[hi] if frac > (BAYER[y % 4][x % 4] + 0.5) / 16 else colours[lo]


def canvas(w, h=None):
    return Image.new("RGBA", (w, h or w), (0, 0, 0, 0))


def tile(fn, size=8):
    img = Image.new("RGBA", (size, size))
    for y in range(size):
        for x in range(size):
            img.putpixel((x, y), tuple(fn(x, y)[:3]) + (255,))
    return img


def picture(rows, pal, w=None, h=None):
    """A pixel map: one string a row, one character a pixel ('.' clear), coloured from `pal`."""
    w = w or max(len(r) for r in rows)
    h = h or len(rows)
    img = canvas(w, h)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == "." or ch == " ":
                continue
            if ch not in pal:
                raise ValueError("no colour %r in %r" % (ch, row))
            img.putpixel((x, y), tuple(pal[ch][:3]) + (255,))
    return img


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((x, y), tuple(c[:3]) + (255,))


def get(img, x, y):
    if 0 <= x < img.width and 0 <= y < img.height:
        return img.getpixel((x, y))
    return (0, 0, 0, 0)


def rect(img, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(img, x, y, c)


def outline(img, colour_of):
    """Edges every drawn shape with a darker shade of the pixel it borders (`colour_of(rgb)`), the
    selective outline the game's larger scenery has, instead of flat black."""
    w, h = img.size
    src = img.copy()
    for y in range(h):
        for x in range(w):
            if src.getpixel((x, y))[3]:
                continue
            for dx, dy in ((0, 1), (1, 0), (-1, 0), (0, -1)):
                p = get(src, x + dx, y + dy)
                if p[3]:
                    img.putpixel((x, y), tuple(colour_of(p[:3])) + (255,))
                    break


def shifted(img, dx, dy):
    out = canvas(img.width, img.height)
    out.paste(img, (dx, dy))
    return out


def sphere_light(dx, dy, r, light=(-0.55, -0.7, 0.45)):
    """How lit a point of a sphere of radius r is (0..1), from the upper left, dx and dy from its
    middle; None outside it."""
    d2 = dx * dx + dy * dy
    if d2 > r * r:
        return None
    nz = math.sqrt(max(0.0, r * r - d2)) / r
    nx, ny = dx / r, dy / r
    lx, ly, lz = light
    n = math.sqrt(lx * lx + ly * ly + lz * lz)
    return max(0.0, (nx * lx + ny * ly + nz * lz) / n)
