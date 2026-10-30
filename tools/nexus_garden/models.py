"""The Nexus garden's 3D pieces: houses, a storehouse, a pagoda, torii gates and the railings and
fences, as the OBJ models the Rust client draws (`<Model>`, view.rs draw_model), each with a 64 by
64 texture of its own.

Units are tiles: x east, y south, z up, from the middle of the object's tile. The client draws the
faces lowest first and skips those facing away (their first three corners wound clockwise on the
screen), so every face here is wound with its outward normal along (p1 - p0) x (pn - p0), and
roofs, being higher, are drawn over the walls they overhang. Textures are about eight pixels a
tile, as the ground's are; `uv` rectangles are in the texture's pixels, v down."""
import math

from .px import hexc as C, canvas, dither, hash2, mix, put
from .sprites import BAMBOO, LACQUER, STONE, WOOD

PLASTER = [C("#a89c8c"), C("#c8bcaa"), C("#ded4c4"), C("#eee8dc"), C("#faf6ee")]
TIMBER = [C("#24160e"), C("#3a2416"), C("#523420"), C("#6a462c")]
TILES = [C("#2a2c3c"), C("#3a3e54"), C("#4c526c"), C("#626a88"), C("#8088a8")]
THATCH = [C("#4a3a1e"), C("#6a5428"), C("#8a7036"), C("#a88c48"), C("#c8aa62")]
SHOJI = [C("#b8b0a0"), C("#e8e2d4"), C("#fffaf0")]
INDIGO = [C("#16203e"), C("#22305a"), C("#30447a")]
GOLD = [C("#6a4a14"), C("#a8781e"), C("#e0b040"), C("#ffe08a")]
BLACK = [C("#0e0c10"), C("#1c1a20"), C("#2c2a32"), C("#3e3c46")]
GRANITE = [C("#5a5862"), C("#76747e"), C("#94929a"), C("#b2b0b6"), C("#cccad0")]
NAMAKO = [C("#1a1c24"), C("#262a34")]


class Mesh:
    def __init__(self, tex=64):
        self.tex = tex
        self.vs, self.vts, self.faces = [], [], []

    def _v(self, p):
        self.vs.append(p)
        return len(self.vs)

    def _vt(self, u, v):
        # half a pixel in, so nearest sampling never picks the neighbouring region
        self.vts.append((u / self.tex, v / self.tex))
        return len(self.vts)

    def poly(self, pts, uvs):
        """A face: corners in order (outward normal along (p1 - p0) x (pn - p0)) and their
        texture points in pixels."""
        ids = [(self._v(p), self._vt(*uv)) for p, uv in zip(pts, uvs)]
        self.faces.append(ids)

    def quad(self, p0, p1, p2, p3, uv):
        """p0 the texture's top left, p1 its top right, p2 bottom right, p3 bottom left."""
        u0, v0, u1, v1 = uv
        e = 0.5
        self.poly([p0, p1, p2, p3], [(u0 + e, v0 + e), (u1 - e, v0 + e), (u1 - e, v1 - e), (u0 + e, v1 - e)])

    def box(self, x0, y0, z0, x1, y1, z1, side, top=None, front=None, back=None, lid=True):
        """A box without a bottom (nor a top without `lid`: under a roof it would be drawn over
        it). `side` textures the east and west faces, `front` the south (default `side`), `back`
        the north (default `front`), `top` the top."""
        front = front or side
        back = back or front
        top = top or side
        self.quad((x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0), front)     # south
        self.quad((x1, y0, z1), (x0, y0, z1), (x0, y0, z0), (x1, y0, z0), back)      # north
        self.quad((x1, y1, z1), (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), side)      # east
        self.quad((x0, y0, z1), (x0, y1, z1), (x0, y1, z0), (x0, y0, z0), side)      # west
        if lid:
            self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), top)   # top

    def hip(self, outer, inner, z0, z1, slope, end):
        """A band of hipped roof from the ring `outer` (x0, y0, x1, y1) at z0 up to `inner` at z1;
        `inner` may close to a ridge (y0 == y1) or a point. `slope` textures the long south and
        north faces, `end` the east and west ones."""
        ox0, oy0, ox1, oy1 = outer
        ix0, iy0, ix1, iy1 = inner
        s, n = slope, (slope[0], slope[1], slope[2], slope[3])
        # south: outer bottom edge, inner top edge
        self.poly([(ix0, iy1, z1), (ix1, iy1, z1), (ox1, oy1, z0), (ox0, oy1, z0)][::1] if ix1 > ix0 else
                  [(ix0, iy1, z1), (ox1, oy1, z0), (ox0, oy1, z0)],
                  self._trap_uv(s, ix0, ix1, ox0, ox1))
        self.poly([(ix1, iy0, z1), (ix0, iy0, z1), (ox0, oy0, z0), (ox1, oy0, z0)] if ix1 > ix0 else
                  [(ix1, iy0, z1), (ox0, oy0, z0), (ox1, oy0, z0)],
                  self._trap_uv(n, -ix1, -ix0, -ox1, -ox0))
        self.poly([(ix1, iy1, z1), (ix1, iy0, z1), (ox1, oy0, z0), (ox1, oy1, z0)] if iy1 > iy0 else
                  [(ix1, iy1, z1), (ox1, oy0, z0), (ox1, oy1, z0)],
                  self._trap_uv(end, iy1, iy0, oy1, oy0, flip=True))
        self.poly([(ix0, iy0, z1), (ix0, iy1, z1), (ox0, oy1, z0), (ox0, oy0, z0)] if iy1 > iy0 else
                  [(ix0, iy0, z1), (ox0, oy1, z0), (ox0, oy0, z0)],
                  self._trap_uv(end, -iy0, -iy1, -oy0, -oy1, flip=True))

    @staticmethod
    def _trap_uv(rect, i0, i1, o0, o1, flip=False):
        """Texture points for a trapezoid (or triangle) face: its top edge (i0..i1) placed in
        proportion to its bottom edge (o0..o1) across the rectangle."""
        u0, v0, u1, v1 = rect
        span = (o1 - o0) if not flip else (o0 - o1)
        span = span or 1

        def u(t):
            return u0 + 0.5 + (u1 - u0 - 1) * t
        if abs(i1 - i0) < 1e-9:
            mid = ((i0 - o0) / span) if not flip else ((o0 - i0) / span)
            return [(u(mid), v0 + 0.5), (u(1), v1 - 0.5), (u(0), v1 - 0.5)]
        if not flip:
            a, b = (i0 - o0) / span, (i1 - o0) / span
        else:
            a, b = (o0 - i0) / span, (o0 - i1) / span
        return [(u(a), v0 + 0.5), (u(b), v0 + 0.5), (u(1), v1 - 0.5), (u(0), v1 - 0.5)]

    def obj(self, name):
        lines = ["# %s: generated by tools/build_nexus_garden.py, don't edit" % name, "o %s" % name.replace(" ", "_")]
        lines += ["v %.4f %.4f %.4f" % v for v in self.vs]
        lines += ["vt %.5f %.5f" % t for t in self.vts]
        for f in self.faces:
            lines.append("f " + " ".join("%d/%d" % ids for ids in f))
        return "\n".join(lines) + "\n"


# --- texture painters (regions of a 64 by 64 image) ----------------------------------------------

def fill(img, rect, fn):
    u0, v0, u1, v1 = rect
    for y in range(v0, v1):
        for x in range(u0, u1):
            put(img, x, y, fn(x - u0, y - v0, u1 - u0, v1 - v0))


def plaster_wall(img, rect, door=False, windows=0, base=NAMAKO, posts=8):
    """White plaster between dark timber posts and beams, a stone or dark board footing; a
    noren-hung doorway in the middle, shoji windows between the posts."""
    w, h = rect[2] - rect[0], rect[3] - rect[1]
    win_cols = set()
    if windows:
        for i in range(windows):
            cx = int((i + 1) * w / (windows + 1))
            win_cols.update(range(cx - 3, cx + 3))

    def px(x, y, w, h):
        if y == 0 or y == h - 4 or x % posts == 0 or x == w - 1:
            return dither(TIMBER, 0.55 if y else 0.8, x, y)
        if y >= h - 3:
            return base[(x + y) % 2] if base is NAMAKO else dither(base, 0.5, x, y)
        if door and abs(x - w / 2 + 0.5) < 4 and y >= 2:
            if y < 6:                                         # the noren
                c = INDIGO[1 + (x % 3 == 0)]
                if y == 4 and abs(x - w / 2 + 0.5) < 1:
                    c = SHOJI[2]                              # its white crest
                return c
            return BLACK[1]
        if x in win_cols and 2 <= y <= h - 6:
            # a shoji window: a dark frame, paper in a lattice
            left = max(c for c in win_cols if c <= x and c - 1 not in win_cols)
            dx = x - left
            if y in (2, h - 6) or dx in (0, 5):
                return TIMBER[1]
            if dx == 3 or (y - 2) % 3 == 0:
                return SHOJI[0]
            return SHOJI[2] if y == 3 else SHOJI[1]
        return dither(PLASTER, 0.65 + 0.15 * (hash2(x, y, 3) - 0.5), x, y)
    fill(img, rect, px)


def wood_wall(img, rect, door=False):
    """Weathered boards with a dark beam along the top, a door of boards in the middle."""
    def px(x, y, w, h):
        if y == 0 or x % 8 == 0:
            return TIMBER[1]
        if door and abs(x - w / 2 + 0.5) < 3 and y >= 2:
            return BLACK[1] if x % 2 else TIMBER[2]
        t = 0.55 + (0.2 if x % 2 else -0.05) + 0.12 * (hash2(x // 2, y, 7) - 0.5)
        return dither(WOOD, t, x, y)
    fill(img, rect, px)


def tiled_roof(img, rect, ramp=TILES):
    """Kawara: rows of tiles running down the slope, round ends along the eave."""
    def px(x, y, w, h):
        if y >= h - 2:
            return dither(ramp, 0.85 if x % 3 == 1 else 0.45, x, y)
        ridge = x % 3
        t = 0.62 if ridge == 1 else (0.3 if ridge == 2 else 0.45)
        if y % 4 == 3:
            t -= 0.2
        t += 0.08 * (hash2(x, y, 11) - 0.5)
        return dither(ramp, t, x, y)
    fill(img, rect, px)


def thatch_roof(img, rect):
    def px(x, y, w, h):
        t = 0.55 + 0.3 * (hash2(x, y // 2, 5) - 0.5) + (0.15 if y % 5 == 0 else 0)
        if y >= h - 2:
            t = 0.25
        return dither(THATCH, t, x, y)
    fill(img, rect, px)


def solid(img, rect, ramp, t=0.6, grain=0.1, seed=1):
    fill(img, rect, lambda x, y, w, h: dither(ramp, t + grain * (hash2(x, y, seed) - 0.5), x, y))


def stone_blocks(img, rect):
    def px(x, y, w, h):
        if y % 4 == 3 or (x + (y // 4) * 3) % 6 == 0:
            return GRANITE[0]
        return dither(GRANITE, 0.6 + 0.15 * (hash2(x, y, 4) - 0.5) + (0.15 if y % 4 == 0 else 0), x, y)
    fill(img, rect, px)


# --- the models ----------------------------------------------------------------------------------

def house():
    """A minka: plaster and timber, a hipped roof of dark tiles whose eaves flare out, a noren over
    its door. Five tiles by three."""
    img = canvas(64)
    m = Mesh()
    FRONT, SIDE, BACK = (0, 0, 40, 12), (40, 0, 64, 12), (0, 12, 40, 24)
    SLOPE, END, EAVE = (0, 24, 64, 44), (0, 44, 32, 64), (32, 44, 64, 64)
    plaster_wall(img, FRONT, door=True, windows=2)
    plaster_wall(img, SIDE, windows=1)
    plaster_wall(img, BACK, windows=3)
    tiled_roof(img, SLOPE)
    tiled_roof(img, END)
    tiled_roof(img, EAVE)
    x, y, h = 2.25, 1.1, 1.6
    m.box(-x, -y, 0, x, y, h, SIDE, front=FRONT, back=BACK, lid=False)
    # the flared eaves: out from the wall tops, then the hip up to the ridge
    o = 0.35
    m.hip((-x - o, -y - o, x + o, y + o), (-x + 0.05, -y + 0.05, x - 0.05, y - 0.05), h - 0.05, h + 0.2, EAVE, EAVE)
    m.hip((-x + 0.05, -y + 0.05, x - 0.05, y - 0.05), (-x + 1.2, 0, x - 1.2, 0), h + 0.2, h + 1.25, SLOPE, END)
    return m, img


def thatch_house():
    """A farmhouse of boards under a tall thatched roof. Three tiles square."""
    img = canvas(64)
    m = Mesh()
    FRONT, SIDE = (0, 0, 24, 12), (24, 0, 48, 12)
    SLOPE, END = (0, 12, 64, 40), (0, 40, 40, 64)
    wood_wall(img, FRONT, door=True)
    wood_wall(img, SIDE)
    thatch_roof(img, SLOPE)
    thatch_roof(img, END)
    x, y, h = 1.25, 1.05, 1.3
    m.box(-x, -y, 0, x, y, h, SIDE, front=FRONT, back=SIDE, lid=False)
    o = 0.3
    m.hip((-x - o, -y - o, x + o, y + o), (-0.35, 0, 0.35, 0), h - 0.05, h + 1.5, SLOPE, END)
    return m, img


def storehouse():
    """A kura: thick white walls over a footing of dark tiles set in a lattice of plaster, an
    iron-bound door, a heavy tiled roof. The vault chests' storehouse. Three tiles square."""
    img = canvas(64)
    m = Mesh()
    FRONT, SIDE = (0, 0, 24, 16), (24, 0, 48, 16)
    SLOPE, END = (0, 16, 64, 40), (0, 40, 32, 64)

    def kura(x, y, w, h, door=False):
        if y >= h - 6:                                        # namako: dark tiles, white lattice
            return SHOJI[2] if (x + y) % 4 == 0 or (x - y) % 4 == 0 else NAMAKO[(x // 2 + y) % 2]
        if door and abs(x - w / 2 + 0.5) < 4 and y >= 3:
            dx = x - int(w / 2 - 4)
            if dx in (0, 7) or y == 3:
                return TIMBER[1]
            if y % 3 == 0 and dx % 3 == 1:
                return GOLD[2]                               # an iron door's studs
            return BLACK[2] if dx < 4 else BLACK[1]
        if y < 2:
            return dither(TIMBER, 0.6, x, y)
        return dither(PLASTER[2:], 0.6 + 0.2 * (hash2(x, y, 9) - 0.5), x, y)
    fill(img, FRONT, lambda x, y, w, h: kura(x, y, w, h, True))
    fill(img, SIDE, kura)
    tiled_roof(img, SLOPE)
    tiled_roof(img, END)
    x, y, h = 1.25, 1.05, 2.0
    m.box(-x, -y, 0, x, y, h, SIDE, front=FRONT, back=SIDE, lid=False)
    o = 0.28
    m.hip((-x - o, -y - o, x + o, y + o), (-0.6, 0, 0.6, 0), h - 0.05, h + 0.95, SLOPE, END)
    return m, img


def pagoda():
    """A three-storey pagoda in vermilion and white, dark tiled roofs flaring at every storey and
    a gilded spire of rings. Five tiles square and about eight high."""
    img = canvas(64)
    m = Mesh()
    WALL, ROOF, SPIRE, RED = (0, 0, 32, 12), (0, 12, 64, 40), (32, 0, 40, 12), (40, 0, 64, 12)

    def wall(x, y, w, h):
        if x % 8 in (0, 7) or y in (0, h - 1):
            return dither(LACQUER, 0.55, x, y)
        if 3 <= x % 8 <= 4 and 2 <= y <= h - 3:
            return BLACK[2]                                   # a door between the posts
        return dither(PLASTER[2:], 0.6, x, y)
    fill(img, WALL, wall)
    tiled_roof(img, ROOF)
    solid(img, SPIRE, GOLD, 0.6, 0.3)
    solid(img, RED, LACQUER, 0.55, 0.2)
    z = 0.0
    # a stone platform
    m.box(-2.4, -2.4, 0, 2.4, 2.4, 0.25, (0, 40, 32, 48), top=(0, 48, 32, 64))
    stone_blocks(img, (0, 40, 32, 48))
    stone_blocks(img, (0, 48, 32, 64))
    z = 0.25
    for i, (half, height, eave) in enumerate(((1.6, 1.5, 2.3), (1.3, 1.25, 1.95), (1.05, 1.1, 1.65))):
        m.box(-half, -half, z, half, half, z + height, WALL, lid=False)
        z += height
        # a red bracket band under the eaves, then the roof flaring out and climbing in
        m.box(-half - 0.1, -half - 0.1, z, half + 0.1, half + 0.1, z + 0.12, RED, lid=False)
        z += 0.12
        m.hip((-eave, -eave, eave, eave), (-half - 0.1, -half - 0.1, half + 0.1, half + 0.1), z - 0.05, z + 0.3, ROOF, ROOF)
        top = half * 0.75 if i < 2 else 0.0
        m.hip((-half - 0.1, -half - 0.1, half + 0.1, half + 0.1), (-top, -top, top, top), z + 0.3, z + 0.45 if i < 2 else z + 1.0, ROOF, ROOF)
        z += 0.45
    # the spire: a gilded post with rings
    m.box(-0.08, -0.08, z, 0.08, 0.08, z + 1.6, SPIRE)
    for k in range(5):
        r = 0.22 - k * 0.02
        zz = z + 0.25 + k * 0.22
        m.box(-r, -r, zz, r, r, zz + 0.06, SPIRE)
    m.box(-0.14, -0.14, z + 1.6, 0.14, 0.14, z + 1.85, SPIRE)
    return m, img


def torii():
    """A vermilion torii: two pillars on black footings, the tie beam through them, the top beams
    sweeping up at their ends, black on top. Its pillars stand two tiles either side of its middle."""
    img = canvas(64)
    m = Mesh()
    RED, BLK, BASE = (0, 0, 32, 32), (32, 0, 64, 32), (0, 32, 32, 64)
    solid(img, RED, LACQUER, 0.62, 0.12)
    solid(img, BLK, BLACK, 0.55, 0.2)
    solid(img, BASE, BLACK, 0.4, 0.2)
    for px_ in (-2, 2):
        m.box(px_ - 0.16, -0.16, 0.3, px_ + 0.16, 0.16, 2.55, RED)
        m.box(px_ - 0.22, -0.22, 0, px_ + 0.22, 0.22, 0.3, BASE)
    m.box(-2.55, -0.1, 1.95, 2.55, 0.1, 2.15, RED)             # the nuki
    m.box(-0.12, -0.08, 2.15, 0.12, 0.08, 2.5, RED)            # the gakuzuka strut
    # the kasagi in three runs, the outer two climbing toward their tips
    for (x0, x1, z0, z1) in ((-1.6, 1.6, 2.5, 2.5), (1.6, 3.0, 2.5, 2.75), (-3.0, -1.6, 2.75, 2.5)):
        lo, hi = (z0, z1)
        th = 0.22
        pts_top = [(x0, -0.2, lo + th), (x1, -0.2, hi + th), (x1, 0.2, hi + th), (x0, 0.2, lo + th)]
        pts_bot = [(x0, -0.2, lo), (x1, -0.2, hi), (x1, 0.2, hi), (x0, 0.2, lo)]
        m.quad(pts_top[3], pts_top[2], pts_bot[2], pts_bot[3], RED)               # south face
        m.quad(pts_top[1], pts_top[0], pts_bot[0], pts_bot[1], RED)               # north face
        m.quad((x0, -0.24, lo + th), (x1, -0.24, hi + th), (x1, 0.24, hi + th), (x0, 0.24, lo + th), BLK)
        m.quad((x1, 0.2, hi + th), (x1, -0.2, hi + th), (x1, -0.2, hi), (x1, 0.2, hi), RED)
        m.quad((x0, -0.2, lo + th), (x0, 0.2, lo + th), (x0, 0.2, lo), (x0, -0.2, lo), RED)
    return m, img


def wood_rail():
    """A bridge's railing, a tile long east to west: vermilion posts with brass caps at its ends
    and a top rail between them. Turned a quarter (`Rotation`) for bridges that run north."""
    img = canvas(64)
    m = Mesh()
    RED, CAP, TOP = (0, 0, 32, 32), (32, 0, 64, 32), (0, 32, 64, 64)
    solid(img, RED, LACQUER, 0.6, 0.15)
    solid(img, CAP, GOLD, 0.6, 0.3)
    solid(img, TOP, LACQUER, 0.75, 0.1)
    for x in (-0.45, 0.45):
        m.box(x - 0.09, -0.09, 0, x + 0.09, 0.09, 0.78, RED)
        m.box(x - 0.11, -0.11, 0.78, x + 0.11, 0.11, 0.9, CAP)
    m.box(-0.5, -0.06, 0.62, 0.5, 0.06, 0.72, RED, top=TOP)
    m.box(-0.5, -0.04, 0.3, 0.5, 0.04, 0.36, RED)
    return m, img


def stone_rail():
    """A cobbled bridge's low parapet of dressed granite, a tile long."""
    img = canvas(64)
    m = Mesh()
    FACE, TOP = (0, 0, 64, 32), (0, 32, 64, 64)
    stone_blocks(img, FACE)
    solid(img, TOP, GRANITE, 0.75, 0.15)
    m.box(-0.5, -0.16, 0, 0.5, 0.16, 0.42, FACE, top=TOP)
    m.box(-0.5, -0.2, 0.42, 0.5, 0.2, 0.52, TOP)
    return m, img


def bamboo_fence():
    """A fence of bamboo poles bound to two rails, a tile long."""
    img = canvas(64)
    m = Mesh()
    POLE, TIE = (0, 0, 32, 64), (32, 0, 64, 64)

    def pole(x, y, w, h):
        if y % 12 == 0:
            return BAMBOO[1]
        return dither(BAMBOO, 0.85 - 0.6 * x / w, x, y)
    fill(img, POLE, pole)
    solid(img, TIE, [C("#3a2a1a"), C("#5a4428"), C("#7a603a")], 0.5, 0.3)
    n = 5
    for i in range(n):
        x = -0.5 + (i + 0.5) / n
        top = 0.85 + 0.08 * ((i * 7) % 3)
        m.box(x - 0.07, -0.05, 0, x + 0.07, 0.05, top, POLE)
    for z in (0.28, 0.65):
        m.box(-0.5, -0.08, z, 0.5, 0.08, z + 0.06, TIE)
    return m, img


# name -> (builder, footprint (x, y) in tiles, blocks its tile)
MODELS = {
    "NG House": (house, (5, 3), True),
    "NG Thatch House": (thatch_house, (3, 3), True),
    "NG Storehouse": (storehouse, (3, 3), True),
    "NG Pagoda": (pagoda, (5, 5), True),
    "NG Torii": (torii, (5, 1), False),
    "NG Wood Rail": (wood_rail, (1, 1), True),
    "NG Stone Rail": (stone_rail, (1, 1), True),
    "NG Bamboo Fence": (bamboo_fence, (1, 1), True),
}
