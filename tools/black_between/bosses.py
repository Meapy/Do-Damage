"""The Black Between's three bosses at 32x32 (tools/build_black_between.py puts them on its sheets):
Vesper, the Dusk Star, a dark-winged sorceress; Aurel, the Dawn Blade, a winged knight whose sword
flies at his command; and their father, the Mysterious One, hooded, robed in the night sky, with a
wing of each.

Shapes are rasterised and shaded the pixel-art way (light from the upper left: a lit rim on the
upper-left edges, a dark rim lower-right, two mid tones between), wings are built feather by
feather, and faces, armour and hands are hand-placed pixels (`paint`). The client draws the black
outline, so these have none of their own; `outline` adds one for previews only."""
import math, random
from PIL import Image


def C(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def R(*hs):
    return [C(h) for h in hs]


class Canvas:
    def __init__(self, w=32, h=32):
        self.img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.w, self.h = w, h

    def px(self, x, y, c, a=255):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h:
            if isinstance(c, str):
                c = C(c)
            if a < 255:
                old = self.img.getpixel((x, y))
                if old[3]:
                    c = tuple(int(old[i] + (c[i] - old[i]) * a / 255) for i in range(3)); a = 255
            self.img.putpixel((x, y), tuple(c) + (a,))

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.img.getpixel((x, y))
        return (0, 0, 0, 0)

    def shade(self, mask, ramp, grad=None, rim=True):
        """Fill a set of pixels with a 4-colour ramp (dark..light), lit from the upper left."""
        for (x, y) in mask:
            if grad:
                lv = grad(x, y)
            else:
                lv = 1
            if rim:
                if (x - 1, y) not in mask or (x, y - 1) not in mask:
                    lv = max(lv, 2) + 1
                elif (x + 1, y) not in mask or (x, y + 1) not in mask:
                    lv = min(lv, 1) - 1
            self.px(x, y, ramp[max(0, min(len(ramp) - 1, lv))])


def poly_mask(pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    m = set()
    for y in range(int(min(ys)) - 1, int(max(ys)) + 2):
        for x in range(int(min(xs)) - 1, int(max(xs)) + 2):
            cx, cy = x + 0.5, y + 0.5
            ins = False
            j = len(pts) - 1
            for i in range(len(pts)):
                xi, yi = pts[i]; xj, yj = pts[j]
                if (yi > cy) != (yj > cy) and cx < (xj - xi) * (cy - yi) / (yj - yi + 1e-9) + xi:
                    ins = not ins
                j = i
            if ins:
                m.add((x, y))
    return m


def ell_mask(cx, cy, rx, ry):
    return {(x, y) for y in range(int(cy - ry) - 1, int(cy + ry) + 2) for x in range(int(cx - rx) - 1, int(cx + rx) + 2)
            if ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 <= 1}


def lin(x0, y0, x1, y1, levels=(1, 2)):
    """A gradient across a shape: lighter towards (x0, y0)."""
    def g(x, y):
        dx, dy = x1 - x0, y1 - y0
        t = ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy + 1e-9)
        return levels[1] if t < 0.5 else levels[0]
    return g


def feather(root, ang, length, width):
    a = math.radians(ang)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    rx, ry = root
    return [(rx + nx * width / 2, ry + ny * width / 2), (rx + dx * length * .55 + nx * width * .55, ry + dy * length * .55 + ny * width * .55),
            (rx + dx * length, ry + dy * length), (rx + dx * length * .55 - nx * width * .4, ry + dy * length * .55 - ny * width * .4),
            (rx - nx * width / 2, ry - ny * width / 2)]


def along(path, t):
    segs = [(path[i], path[i + 1]) for i in range(len(path) - 1)]
    lens = [math.dist(a, b) for a, b in segs]
    d = t * sum(lens)
    for (a, b), l in zip(segs, lens):
        if d <= l:
            f = d / l if l else 0
            return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)
        d -= l
    return path[-1]


def wing(cv, arm, ang0, ang1, len0, len1, ramp, covert_ramp, n=8, tip=None, tipcol=None, width=3.2, flap=0):
    """Primaries and secondaries hung from the wing's arm (shoulder -> wrist), drawn wrist first so
    the inner feathers overlap; then a band of short coverts along the arm."""
    for k in reversed(range(n)):
        t = k / (n - 1)
        root = along(arm, 0.08 + t * 0.92)
        ang = ang0 + (ang1 - ang0) * t + flap * t
        ln = len0 + (len1 - len0) * t
        m = poly_mask(feather(root, ang, ln, width))
        cv.shade(m, ramp, grad=lambda x, y, r=root: 2 if math.dist((x, y), r) < ln * .45 else 1)
        if tip:  # the feather's tip catches the glow
            a = math.radians(ang)
            tx, ty = root[0] + math.cos(a) * (ln - 1), root[1] + math.sin(a) * (ln - 1)
            if (k + tip) % 2 == 0:
                cv.px(tx, ty, tipcol)
    for k in range(n + 2):  # coverts
        t = k / (n + 1)
        root = along(arm, t)
        ang = ang0 + (ang1 - ang0) * t + flap * t
        m = poly_mask(feather((root[0], root[1]), ang, 4.2 + 1.2 * t, 3.4))
        cv.shade(m, covert_ramp)


def sparkle(cv, x, y, c, big=False):
    cv.px(x, y, "#ffffff")
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.px(x + dx, y + dy, c)
    if big:
        for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)):
            cv.px(x + dx, y + dy, c, 150)


def orb(cv, x, y, r, ramp, glow=None):
    m = ell_mask(x, y, r, r)
    if glow:
        for (gx, gy) in ell_mask(x, y, r + 1.6, r + 1.6) - m:
            cv.px(gx, gy, glow, 110)
    cv.shade(m, ramp, grad=lambda px, py: 2 if (px - x) + (py - y) < 0 else 1)
    cv.px(x - r * .4, y - r * .4, "#ffffff")


# =================================================================================================
# Vesper, the Dusk Star: a dark-winged sorceress. Black-violet wings with magenta-lit tips, a gown of
# night, long dark hair, a dusk-star diadem, magenta orbs in both hands.

V_WING = R("#0e0718", "#1e0f33", "#34195a", "#55308a")
V_WING_FAR = R("#08040e", "#130a22", "#22123c", "#341c56")
V_COVERT = R("#160b26", "#2c1748", "#46266e", "#6c46a2")
V_HAIR = R("#0c0616", "#221038", "#3c1e66", "#6a40a8")
V_SKIN = R("#6e5088", "#a888c4", "#d4bfe8", "#f2e6fa")
V_GOWN = R("#100618", "#240c3a", "#3e165e", "#62248e")
V_METAL = R("#16121e", "#2e2840", "#504868", "#8c84ac")
V_GLOW = R("#6a0c96", "#b41ede", "#ec5cff", "#ffd2ff")


def paint(cv, rows, pal):
    """Hand-placed pixels: rows of (column, string) segments; a space leaves a pixel alone."""
    for y, segs in rows.items():
        for (x0, st) in segs:
            for i, ch in enumerate(st):
                if ch != " ":
                    cv.px(x0 + i, y, pal[ch])


V_PAL = {"h": "#0c0616", "H": "#2e1850", "j": "#6a40a8", "O": "#ec5cff", "l": "#b04a8a", "b": "#4a2c6a",
         "S": "#5e4078", "s": "#9a78b8", "t": "#c4a8dc", "T": "#e4d2f2", "E": "#ff3ad8", "e": "#8a12b0",
         "m": "#2e2840", "M": "#504868", "n": "#8c84ac", "k": "#16121e",
         "1": "#100618", "2": "#240c3a", "3": "#3e165e", "4": "#62248e"}
V_BODY = {
    1: [(16, "hhhh")],
    2: [(14, "hhHHHHh")],
    3: [(13, "hHHjjHHHh")],
    4: [(13, "hHjHHHOHHh")],
    5: [(12, "hHjHjH"), (18, "jHHh")],
    6: [(12, "hHHhH"), (17, "tTbbt"), (22, "h")],
    7: [(12, "hHjhH"), (17, "tTEet")],
    8: [(11, "hhHHhH"), (17, "tTTTt")],
    9: [(11, "hHjHhH"), (17, "stTls")],
    10: [(10, "hHHHHhh"), (17, "sttS")],
    11: [(10, "hHjHhh"), (17, "sts")],
    12: [(9, "hHHHh"), (15, "StmnnmtT")],
    13: [(9, "hHjh"), (14, "StmMeMmtTTtt")],
    14: [(8, "hHHh"), (14, "SsmMMMm"), (24, "stt")],
    15: [(8, "hHh"), (14, "Ss2mMMm2")],
    16: [(7, "hjh"), (14, "Ss23mm32")],
    17: [(7, "hh"), (14, "sS2kkkk2")],
    18: [(6, "hh"), (14, "sS")],
    19: [(13, "ts")],
}
V_CAST = {  # the front arm raised high, the orb overhead
    12: [(15, "StmnnmtT"), (23, "tT")],
    11: [(17, "sts"), (22, "tT"), (24, "t")],
    10: [(23, "st"), (25, "tT")],
    13: [(14, "StmMeMmt")],
    14: [(14, "SsmMMMm")],
}


def vesper(frame=0, cast=False, ghost=False):
    cv = Canvas()
    flap = (-6, 0, 4)[frame % 3] if not cast else -9
    wing(cv, [(20, 12), (25, 5), (31, 1)], 75, -10, 10, 9, V_WING_FAR, V_WING_FAR, n=8, flap=-flap * .6)
    wing(cv, [(13, 12), (7, 4), (0, 0)], 100, 205, 13, 10, V_WING, V_COVERT, n=10, tip=frame, tipcol=V_GLOW[2], flap=flap)
    # hair streaming back and down behind her
    cv.shade(poly_mask([(13, 3), (17, 3), (16, 12), (14, 19), (10, 25), (6, 23), (9, 16), (10, 8)]), V_HAIR, grad=lin(10, 4, 10, 24))
    for (x, y) in ((11, 12), (10, 15), (9, 18), (8, 21)):
        cv.px(x, y, V_HAIR[3])
    # the skirt: night cloth flaring out and swept back, a glowing torn hem, a few stars
    sk = poly_mask([(15, 17), (21.5, 17), (24, 23), (27, 30), (24, 29.5), (22, 31), (19, 29.5), (16, 31), (13, 29.5), (9, 31), (8, 29), (12, 23)])
    cv.shade(sk, V_GOWN, grad=lambda x, y: 2 if x < 17 - (y - 17) * .3 + 3 else 1)
    for (x, y) in sk:
        if (x, y + 1) not in sk and (x + y) % 2 == 0:
            cv.px(x, y, V_GLOW[1])
        if (x, y + 2) not in sk and (x, y + 1) in sk and (x * 7 + y) % 5 == 0:
            cv.px(x, y, V_GLOW[2])
    for (x, y) in ((14, 26), (20, 25), (17, 22), (22, 28), (12, 28)):
        cv.px(x, y, "#c8b8ff")
    for (x, y) in ((18, 18), (19, 19), (20, 20), (20, 21), (21, 22), (22, 24), (23, 26)):  # a slit of shadow
        cv.px(x, y, V_GOWN[0])
    paint(cv, V_BODY, V_PAL)
    if cast:
        for (x, y) in ((24, 14), (25, 14), (26, 14), (24, 13), (25, 13)):
            cv.img.putpixel((x, y), (0, 0, 0, 0))
        paint(cv, V_CAST, V_PAL)
        orb(cv, 26.5, 6.5, 3.0, V_GLOW, glow=V_GLOW[2])
        for (x, y) in ((30, 3), (23, 2), (30, 9), (29, 11), (22, 6)):
            cv.px(x, y, V_GLOW[3] if (x + y) % 2 else V_GLOW[2])
        cv.px(19, 7, "#ffffff")
    else:
        orb(cv, 28.5, 13.5, 2.4, V_GLOW, glow=V_GLOW[2])
    orb(cv, 13, 21, 1.6 if not cast else 2.1, V_GLOW, glow=V_GLOW[2])
    if ghost:
        im = cv.img
        for y in range(32):
            for x in range(32):
                r, g, b, a = im.getpixel((x, y))
                if a:
                    lum = (r + g + b) / 3
                    im.putpixel((x, y), (int(lum * .55 + 40), int(lum * .35 + 18), int(lum * .7 + 70), 175))
    return cv.img


# =================================================================================================
# Aurel, the Dawn Blade: a winged knight in white and gold. A sun-ray crest on an open helm, white
# wings tipped with gold, sky-blue cloth under the plate, and a sword that flies at his command.

A_WING = R("#8c8ca8", "#c8c8dc", "#ecebf6", "#ffffff")
A_WING_FAR = R("#6a6a86", "#9c9cb6", "#c4c4d8", "#dcdcec")
A_COVERT = R("#a0741a", "#d8a83a", "#f6d470", "#fff2c0")
A_PLATE = R("#8a8478", "#c8c2b2", "#ebe6da", "#ffffff")
A_GOLD = R("#7a4e10", "#c08a1c", "#eec04a", "#fff0a8")
A_CLOTH = R("#1c3468", "#2e56a0", "#4c86d0", "#8cc0f0")
A_SKIN = R("#9a6848", "#d09c74", "#f0c8a0", "#ffe6cc")
A_HAIR = R("#9a6410", "#d8a028", "#f6d060", "#fff0b0")
A_BLADE = R("#7a9cb0", "#bcdcea", "#e8f8ff", "#ffffff")
A_EDGE = "#7ee8ff"


def flight_sword(cv, x, y, ang, scale=1.0, trail=True):
    """The flying sword: a long white blade with a pale fire edge, a gold wing-shaped guard."""
    a = math.radians(ang)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    L = 11 * scale
    blade = [(x + nx * 1.3, y + ny * 1.3), (x + dx * L * .8 + nx * 1.1, y + dy * L * .8 + ny * 1.1), (x + dx * L, y + dy * L),
             (x + dx * L * .8 - nx * 1.1, y + dy * L * .8 - ny * 1.1), (x - nx * 1.3, y - ny * 1.3)]
    if trail:
        for i in range(1, 5):
            for s in (-1, 0, 1):
                cv.px(x - dx * i * 1.3 + nx * s * .9, y - dy * i * 1.3 + ny * s * .9, A_EDGE, 140 - i * 25)
    m = poly_mask(blade)
    cv.shade(m, A_BLADE)
    for t in range(2, int(L) - 1):  # the fuller, gold
        cv.px(x + dx * t, y + dy * t, A_GOLD[2] if t % 3 else A_GOLD[3])
    for (px_, py_) in m:  # pale fire along the lower edge
        if (px_ + 1, py_ + 1) not in m and (px_ + py_) % 2 == 0:
            cv.px(px_ + 1, py_ + 1, A_EDGE, 170)
    guard = [(x + nx * 3.4 - dx, y + ny * 3.4 - dy), (x + nx * 1 + dx * .6, y + ny * 1 + dy * .6), (x - nx * 1 + dx * .6, y - ny * 1 + dy * .6),
             (x - nx * 3.4 - dx, y - ny * 3.4 - dy), (x - dx * 1.2, y - dy * 1.2)]
    cv.shade(poly_mask(guard), A_GOLD)
    cv.px(x, y, "#4cc8ff")
    for t in range(1, 4):  # grip and pommel
        cv.px(x - dx * (t + .8), y - dy * (t + .8), "#6a3a18" if t % 2 else "#9a5a28")
    cv.px(x - dx * 4.6, y - dy * 4.6, A_GOLD[3])


A_PAL = {"a": "#8a8478", "b": "#c8c2b2", "c": "#ebe6da", "d": "#ffffff", "y": "#7a4e10", "g": "#c08a1c", "G": "#eec04a",
         "Y": "#fff0a8", "v": "#1c3468", "u": "#2e56a0", "U": "#4c86d0", "z": "#9a6848", "s": "#d09c74", "S": "#f0c8a0",
         "h": "#d8a028", "H": "#f6d060", "e": "#2a5cd0", "q": "#4cc8ff"}
A_BODY = {
    0: [(14, "Y"), (16, "Y"), (18, "Y")],
    1: [(13, "Yg"), (16, "G"), (18, "gY")],
    2: [(14, "gGGGGg")],
    3: [(13, "gGYYGGGy")],
    4: [(12, "gGYGGqGGy")],
    5: [(12, "hgGGGGGGy")],
    6: [(12, "hHgSSSSgy")],
    7: [(12, "hHgSSSeS")],
    8: [(12, "hHgsSSSz")],
    9: [(12, "Hhygssz")],
    10: [(12, "hH"), (14, "y"), (16, "sz")],
    11: [(10, "gGGg"), (14, "bccdcb"), (20, "gGGg")],
    12: [(9, "gGYGg"), (14, "bcddcb"), (19, "gGYGgy")],
    13: [(9, "ygGGy"), (14, "bcGYcb"), (20, "ygy"), (23, "bcdS")],
    14: [(10, "yby"), (14, "bcYGcb"), (20, "ab"), (23, "abSsS")],
    15: [(10, "bcb"), (14, "abccba"), (20, "a")],
    16: [(10, "bcb"), (13, "gGGGGGGy")],
    17: [(10, "GgG"), (13, "gyGqGyg"), (20, "y")],
    18: [(11, "vuUcbbccbUuv")],
    19: [(11, "vuUbcbbcbUuv")],
    20: [(12, "vUbcbbcbUv")],
    21: [(12, "vuUbccbUv")],
    22: [(13, "vUuuuUv")],
}


def dawn_sword_shot():
    """Aurel's sword flying across the arena (16x16, pointing right, its pale fire trailing)."""
    cv = Canvas(16, 16)
    flight_sword(cv, 3.5, 8, 0, 1.0)
    return cv.img


A_COAT = R("#7a7464", "#c4bca8", "#ece6d6", "#fffcf2")
A2_PAL = {"h": "#9a6410", "H": "#d8a028", "j": "#f6d060", "J": "#fff0b0", "y": "#7a4e10", "g": "#c08a1c",
          "G": "#eec04a", "Y": "#fff0a8", "q": "#4cc8ff", "s": "#c08c64", "S": "#f0c8a0", "T": "#ffe6cc",
          "e": "#2a5cd0", "b": "#8a5a20", "a": "#8a8478", "m": "#c8c2b2", "c": "#ebe6da", "d": "#ffffff"}
# his sister's figure, a man's: swept golden hair under a sun circlet, a strong jaw, gold pauldrons
# over white plate with the sun on it, a belt with a sky-blue gem
A2_BODY = {
    1: [(16, "hHHh")],
    2: [(14, "hHjJjHh")],
    3: [(13, "hHjgGYGgh")],
    4: [(13, "hHHgGqGgh")],
    5: [(12, "hHjH"), (16, "SSTTSh")],
    6: [(12, "hHjh"), (16, "STbbS")],
    7: [(12, "hHHh"), (16, "STTeS")],
    8: [(11, "hHjH"), (16, "STTTs")],
    9: [(11, "hHHh"), (16, "sSTTs")],
    10: [(11, "hjh"), (16, "ssSs")],
    11: [(16, "sSs")],
    12: [(11, "yGYGy"), (16, "acddca"), (22, "yGYGy")],
    13: [(11, "yGGGy"), (16, "acGYca"), (22, "yGGGy")],
    14: [(12, "ama"), (16, "acYGca"), (22, "ama")],
    15: [(12, "ama"), (16, "amccma"), (22, "am")],
    16: [(12, "aSa"), (15, "gyGqGyg")],
}


def aurel(frame=0, slash=False, gauntlets=False):
    """His sister's look in white and gold: wings white edged in gold, swept golden hair, a long coat
    flaring and swept back as her gown is, and a great sword in his hand (upright; swung down and
    forward when he strikes), or empty gauntlets blazing while his sword flies (`gauntlets`)."""
    cv = Canvas()
    flap = (-5, 0, 4)[frame % 3]
    wing(cv, [(20, 12), (25, 5), (31, 2)], 75, -8, 10, 9, A_WING_FAR, A_WING_FAR, n=8, flap=-flap * .6)
    wing(cv, [(12, 12), (6, 4), (0, 1)], 100, 200, 13, 10, A_WING, R("#9c9cb6", "#d0d0e4", "#f0f0fa", "#ffffff"),
         n=10, tip=frame, tipcol=A_GOLD[2], flap=flap)
    for (x, y) in ((11, 11), (10, 10), (9, 9), (8, 7), (7, 6), (6, 5), (5, 4), (4, 3), (3, 2)):  # gold along the wing's edge
        cv.px(x, y, A_GOLD[2] if (x + y) % 2 else A_GOLD[3])
    # hair swept back to his shoulders
    cv.shade(poly_mask([(13, 3), (17, 3), (16, 10), (14, 14), (11, 15), (10, 11), (11, 6)]), A_HAIR, grad=lin(11, 4, 12, 14))
    for (x, y) in ((11, 9), (11, 12), (12, 14)):
        cv.px(x, y, A_HAIR[3])
    # the long coat: ivory, flaring and swept back, a glowing gold hem, sky-blue lining in its slit
    coat = poly_mask([(15, 16), (22, 16), (24, 22), (27, 30), (24, 29.5), (22, 31), (19, 29.5), (16, 31), (13, 29.5),
                      (9, 31), (8, 29), (12, 22)])
    cv.shade(coat, A_COAT, grad=lambda x, y: 2 if x < 17 - (y - 16) * .3 + 3 else 1)
    for (x, y) in coat:
        if (x, y + 1) not in coat and (x + y) % 2 == 0:
            cv.px(x, y, A_GOLD[2])
        if (x, y + 2) not in coat and (x, y + 1) in coat and (x * 7 + y) % 5 == 0:
            cv.px(x, y, A_GOLD[3])
    for (x, y) in ((18, 18), (19, 19), (20, 20), (20, 21), (21, 22), (22, 24), (23, 26)):  # the slit, its blue lining
        cv.px(x, y, "#2e56a0" if y < 22 else "#4c86d0")
    for (x, y) in ((14, 25), (12, 27), (17, 22)):  # gold thread in the cloth
        cv.px(x, y, A_GOLD[1])
    paint(cv, A2_BODY, A2_PAL)
    if gauntlets:
        # his hands empty, gauntlets blazing while his sword flies
        cv.shade(ell_mask(24.5, 15.5, 1.8, 1.6), A_GOLD)
        cv.shade(ell_mask(12.8, 17.2, 1.5, 1.3), A_GOLD)
        for (x, y) in ((27, 12), (28, 14), (27, 16), (25, 18), (28, 11), (26, 10), (11, 19), (10, 16)):
            cv.px(x, y, A_GOLD[3] if (x + y) % 2 else A_GOLD[2])
    elif slash:
        # swung down and forward
        greatsword(cv, 23.5, 16, 34)
        paint(cv, {15: [(23, "SS")], 16: [(23, "ST")]}, A2_PAL)
    else:
        # held upright before him
        greatsword(cv, 24.5, 15.5, -66)
        paint(cv, {15: [(24, "SSs")], 16: [(24, "STs")]}, A2_PAL)
    return cv.img


def greatsword(cv, x, y, ang):
    """His great sword, gripped at (x, y), its blade along `ang` degrees: broad and long, white with
    a gold fuller and a pale fire along its edge, a gold winged guard and a sun gem."""
    a = math.radians(ang)
    dx, dy = math.cos(a), math.sin(a)
    nx, ny = -dy, dx
    gx, gy = x + dx * 2.2, y + dy * 2.2       # the guard, just past the hand
    L = 14.5
    blade = [(gx + nx * 2.4, gy + ny * 2.4), (gx + dx * L * .78 + nx * 2.2, gy + dy * L * .78 + ny * 2.2),
             (gx + dx * L, gy + dy * L), (gx + dx * L * .78 - nx * 2.2, gy + dy * L * .78 - ny * 2.2),
             (gx - nx * 2.4, gy - ny * 2.4)]
    m = poly_mask(blade)
    # a dark steel edge round it first, so the blade reads against his pale wings
    for (px_, py_) in m:
        for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (px_ + ox, py_ + oy) not in m:
                cv.px(px_ + ox, py_ + oy, "#2a3450")
    cv.shade(m, A_BLADE)
    for t in range(2, int(L) - 1):  # the fuller, gold
        cv.px(gx + dx * t, gy + dy * t, A_GOLD[2] if t % 3 else A_GOLD[3])
    for (px_, py_) in m:  # pale fire along the lower edge
        if (px_ + 1, py_ + 1) not in m and (px_ + py_) % 2 == 0:
            cv.px(px_ + 1, py_ + 1, A_EDGE, 170)
    guard = [(gx + nx * 4.2 - dx, gy + ny * 4.2 - dy), (gx + nx * 1.2 + dx * .7, gy + ny * 1.2 + dy * .7),
             (gx - nx * 1.2 + dx * .7, gy - ny * 1.2 + dy * .7), (gx - nx * 4.2 - dx, gy - ny * 4.2 - dy),
             (gx - dx * 1.3, gy - dy * 1.3)]
    cv.shade(poly_mask(guard), A_GOLD)
    cv.px(gx, gy, "#4cc8ff")
    for t in range(0, 3):  # grip, under his hand, and the pommel
        cv.px(x - dx * t, y - dy * t, "#6a3a18" if t % 2 else "#9a5a28")
    cv.px(x - dx * 3.2, y - dy * 3.2, A_GOLD[3])


def cyclone(frame):
    """His Solar Cyclone: a spinning column of gold and white feathers, the blade glinting through."""
    cv = Canvas()
    cols = [C("#7a4e10"), C("#c08a1c"), C("#eec04a"), C("#fff0a8"), C("#ffffff")]
    for y in range(2, 31):
        hw = 2.2 + (31 - y) * 0.42
        cx = 15.5 + math.sin(y * 0.33 + frame * 1.6) * 1.8
        for x in range(32):
            u = (x - cx) / hw
            if abs(u) <= 1:
                band = math.asin(u) * 1.7 + y * 0.32 - frame * 1.4
                lit = 1 - (u + 1) / 2 * 0.7
                k = int((math.sin(band * 2.4) * .5 + .5) * 2.4 + lit * 1.9)
                cv.px(x, y, cols[max(0, min(4, k))])
    rnd = random.Random(frame)
    for _ in range(9):  # loose feathers
        a = rnd.random() * 6.28; r = rnd.uniform(9, 14); yy = rnd.uniform(4, 22)
        x, y = 15.5 + math.cos(a) * r, yy
        cv.px(x, y, "#ffffff"); cv.px(x + 1, y, "#d8d8ea")
    a = frame * math.pi / 2  # the blade, whipping round
    for t in range(7):
        cv.px(15.5 + math.cos(a) * (2 + t * 1.5), 10 + math.sin(a) * (1 + t * .6), A_EDGE if t > 4 else "#ffffff")
    return cv.img


def planted_sword():
    """The flying sword driven into the floor, standing as tall as two tiles."""
    cv = Canvas(16, 32)
    for (x, y, c) in ((7, 0, 3), (8, 0, 2), (7, 1, 2), (8, 1, 1)):
        cv.px(x, y, A_GOLD[c])
    for y in range(2, 6):
        cv.px(7, y, "#9a5a28" if y % 2 else "#6a3a18"); cv.px(8, y, "#4a2810")
    guard = poly_mask([(0, 5), (3, 6.2), (7, 7.2), (9, 7.2), (13, 6.2), (16, 5), (14, 8), (9, 9), (7, 9), (2, 8)])
    cv.shade(guard, A_GOLD)
    cv.px(7, 7, "#4cc8ff"); cv.px(8, 7, "#2a80c0"); cv.px(7, 8, "#a8e8ff")
    m = poly_mask([(5, 9), (11, 9), (10.6, 26), (5.4, 26)])
    cv.shade(m, A_BLADE, grad=lambda x, y: 2 if x < 8 else 1)
    for y in range(10, 26):
        cv.px(7.5, y, A_GOLD[2] if y % 3 else A_GOLD[3])
        if y % 2:
            cv.px(11, y, A_EDGE, 160)
    for y in range(24, 32):
        for x in range(16):
            if abs(x - 7.5) < 3 + (y - 24) * .8 and cv.get(x, y)[3] == 0:
                cv.px(x, y, "#3a3258" if (x * 3 + y) % 4 else "#241e38")
    for (x, y) in ((4, 27), (3, 28), (11, 27), (12, 28), (13, 30), (2, 30), (7, 28), (8, 29), (6, 31), (9, 31), (1, 31), (14, 31)):
        cv.px(x, y, A_GOLD[2] if (x + y) % 2 else A_GOLD[3])
    return cv.img


# =================================================================================================
# The Mysterious One: their father. Taller than both, hooded and faceless; his robe is the night sky,
# a gold ring of the heavens turns behind his head, one wing is his son's white and one his daughter's
# black, and his eyes are one of each.

F_ROBE = R("#0a0718", "#161034", "#261c52", "#3a2c74")
F_TRIM = A_GOLD
F_RING = R("#5a3a10", "#a87a20", "#e8b848", "#fff0b0")
F_MASK = R("#05030a", "#0c0816", "#16102a", "#241c40")


def father(frame=0, cast=False):
    cv = Canvas()
    flap = (-5, 0, 4)[frame % 3]
    # the heavens' ring behind his head, with ticks like a dial
    for i in range(72):
        a = i / 72 * 6.283
        x, y = 16 + math.cos(a) * 7.6, 7.5 + math.sin(a) * 7.0
        lv = 3 if math.cos(a + 2.4) > .5 else (2 if math.cos(a + 2.4) > -.3 else 1)
        cv.px(x, y, F_RING[lv])
        if i % 9 == 0:
            cv.px(16 + math.cos(a) * 8.6, 7.5 + math.sin(a) * 8.0, F_RING[2])
    # wings: the son's white behind on the right, the daughter's black in front on the left
    wing(cv, [(19, 12), (25, 6), (31, 3)], 70, -5, 10, 8, A_WING_FAR, R("#a0741a", "#c89a3a", "#e8c870", "#fff0c0"), n=7, flap=-flap * .6)
    wing(cv, [(12, 12), (6, 5), (0, 3)], 105, 200, 12, 9, V_WING, V_COVERT, n=9, tip=frame, tipcol=V_GLOW[2], flap=flap)
    # the robe: a starfield, wide at the hem, trailing off into stars
    robe = poly_mask([(13, 9), (19, 9), (22, 14), (24, 22), (26, 29), (22, 30), (18, 31), (13, 30), (8, 29), (9, 22), (11, 14)])
    rnd = random.Random(7 + frame)
    for (x, y) in robe:
        lv = 1
        if (x - 1, y) not in robe or (x, y - 1) not in robe:
            lv = 3
        elif (x + 1, y) not in robe or (x, y + 1) not in robe:
            lv = 0
        elif x < 13:
            lv = 2
        cv.px(x, y, F_ROBE[lv])
    # nebulae in the cloth: violet on his left, gold on his right, meeting in the middle
    for (x, y) in robe:
        n1 = math.sin(x * .55 + y * .3) + math.sin(y * .45 - x * .2)
        if n1 > 1.2 and 13 < x < 23 and y > 13:
            cv.px(x, y, "#5a2a90" if x < 17 else "#8a6420", 160)
    stars = [p for p in robe if F_ROBE[0] != cv.get(*p)[:3] and 11 < p[0] < 24 and p[1] > 12]
    for (x, y) in rnd.sample(stars, 12):
        cv.px(x, y, "#ffffff" if rnd.random() < .4 else "#b8b0f0")
    sparkle(cv, 17, 22, "#b8b0f0")
    # the gold trim down the front of the robe and at the hem
    for (a_, b_) in (((13, 17), (15, 20)), ((15, 20), (14, 24)), ((14, 24), (17, 26)), ((19, 18), (21, 22)), ((21, 22), (20, 26))):
        for t in range(9):
            f = t / 8
            cv.px(a_[0] + (b_[0] - a_[0]) * f, a_[1] + (b_[1] - a_[1]) * f, "#6a5ab0", 170)
    for (x, y) in ((13, 17), (15, 20), (14, 24), (17, 26), (19, 18), (21, 22), (20, 26)):
        cv.px(x, y, "#ffffff")
    # the mantle over his shoulders, edged in gold, with a clasp of both colours
    mantle = poly_mask([(11, 11), (21, 11), (23.5, 15.5), (19, 16.5), (16.5, 15), (14, 16.5), (9.5, 15.5)])
    cv.shade(mantle, F_ROBE, grad=lambda x, y: 2 if x < 15 else 1)
    for (x, y) in mantle:
        if (x, y + 1) not in mantle:
            cv.px(x, y, F_TRIM[2] if (x + y) % 3 else F_TRIM[3])
    cv.px(16, 14, V_GLOW[2]); cv.px(17, 14, F_TRIM[3]); cv.px(16, 15, V_GLOW[1]); cv.px(17, 15, F_TRIM[2])
    for (x, y) in robe:
        if (x, y + 1) not in robe and (x + y) % 3 != 0:
            cv.px(x, y, F_TRIM[1])
    # a scatter of stars where the robe dissolves
    for (x, y) in ((7, 30), (10, 31), (24, 31), (27, 30), (21, 31), (5, 28)):
        cv.px(x, y, "#c8c0ff")
    # hood: deep, with a gold rim round the opening and an empty dark where a face should be
    hood = poly_mask([(16, 1.5), (20, 3), (22.5, 7), (22.5, 11), (20.5, 13.5), (11.5, 13.5), (10, 10), (11.5, 4.5)])
    cv.shade(hood, F_ROBE, grad=lambda x, y: 2 if x < 15 else 1)
    face = ell_mask(18.0, 8.5, 2.7, 3.1)
    for (x, y) in face:
        cv.px(x, y, F_MASK[0] if x > 17 else F_MASK[1])
    for (x, y) in hood - face:
        if any((x + dx, y + dy) in face for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1))) and (x < 18 or y < 7):
            cv.px(x, y, F_TRIM[3] if y < 7 else F_TRIM[2])
    cv.px(16, 2, F_TRIM[2]); cv.px(16, 3, F_TRIM[1])  # a seam at the hood's peak
    # his eyes: dusk and dawn, each with a faint glow below
    vi, go = (V_GLOW[3], "#ffffff") if cast else (V_GLOW[2], A_GOLD[3])
    cv.px(17, 8, vi); cv.px(19, 8, go)
    cv.px(17, 9, V_GLOW[0]); cv.px(19, 9, A_GOLD[1])
    if cast:
        cv.px(16, 8, V_GLOW[1]); cv.px(20, 8, A_GOLD[2])
    # hands from the sleeves, an orb of each
    if cast:
        orb(cv, 6, 18, 2.4, V_GLOW, glow=V_GLOW[2])
        orb(cv, 27, 17, 2.4, R("#d89a20", "#ffd24a", "#fff0a0", "#ffffff"), glow="#ffd24a")
        for (x, y) in ((4, 15), (8, 14), (29, 14), (25, 13), (30, 19)):
            cv.px(x, y, "#ffffff")
        cv.shade(poly_mask([(9, 15), (11.5, 15), (9, 19), (7.5, 19)]), F_ROBE)
        cv.shade(poly_mask([(21.5, 15), (24, 15), (25.5, 18.5), (23.5, 19)]), F_ROBE)
    else:
        cv.shade(poly_mask([(10, 15), (12.5, 15), (12, 20), (9.5, 20)]), F_ROBE)
        cv.shade(poly_mask([(20, 15), (22.5, 15), (23, 20), (20.5, 20)]), F_ROBE)
        orb(cv, 11, 21, 1.4, V_GLOW, glow=V_GLOW[2])
        orb(cv, 22, 21, 1.5, R("#d89a20", "#ffd24a", "#fff0a0", "#ffffff"))
    return cv.img


def outline(img):
    w, h = img.size
    out = img.copy()
    for y in range(h):
        for x in range(w):
            if img.getpixel((x, y))[3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and img.getpixel((nx, ny))[3] > 0:
                        out.putpixel((x, y), (0, 0, 0, 255)); break
    return out


def spectral(img, tint=(200, 170, 255)):
    """A ghost of a sprite: its light and shade kept, in one colour, glowing and see-through."""
    out = img.copy()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            r, g, b, a = img.getpixel((x, y))
            if a:
                lum = (r * 0.3 + g * 0.55 + b * 0.15) / 255
                k = 0.6 + lum * 0.8
                out.putpixel((x, y), tuple(min(255, int(c * k)) for c in tint) + (205,))
    return out


def all_sprites():
    return {
        "The Mysterious One": [father(0), father(1), father(2), father(1, cast=True)],
        "Vesper, the Dusk Star": [vesper(0), vesper(1), vesper(2), vesper(1, cast=True)],
        "Vesper's Veil Copy": [vesper(1, ghost=True)],
        "Aurel, the Dawn Blade": [aurel(0), aurel(1), aurel(2), aurel(1, slash=True)],
        "Aurel, Solar Cyclone": [cyclone(f) for f in range(4)],
        "Aurel, Gauntlet Form": [aurel(0, gauntlets=True), aurel(1, gauntlets=True)],
        "Sword of the Dawn (planted)": [planted_sword()],
    }
