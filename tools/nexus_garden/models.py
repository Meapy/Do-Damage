"""The Nexus garden's 3D pieces: houses and a teahouse, a storehouse, a pagoda, the smithy round
the Divine Forge, torii gates and the railings and fences, as the OBJ models the Rust client draws
(`<Model>`, view.rs draw_model), each with a 128 by 128 texture of its own.

The look is a garden at dusk: rose-red timber, shoji lit pink from within, dark teal tiled roofs
whose eaves sweep up at the corners, lanterns hung from them, stone plinths and verandas.

Units are tiles: x east, y south, z up, from the middle of the object's tile. The client draws the
faces lowest first and skips those facing away (their first three corners wound clockwise on the
screen); `Mesh.face` winds every face outward by itself, from the point it should face away from,
so roofs, being higher, are drawn over the walls they overhang. Faces in the `Glow` material give
off their own light (unshaded): windows, lanterns, a furnace's mouth. Textures are about eight
pixels a tile, as the ground's are; `uv` rectangles are in the texture's pixels, v down."""
import math

from .px import hexc as C, canvas, dither, hash2, mix, put
from .sprites import BAMBOO, LACQUER, STONE, WOOD

TEX = 128

PLASTER = [C("#a89c8c"), C("#c8bcaa"), C("#ded4c4"), C("#eee8dc"), C("#faf6ee")]
TIMBER = [C("#1c0c12"), C("#2e1420"), C("#44202c"), C("#5a2c38")]
ROSE = [C("#3a1420"), C("#5a1e2e"), C("#7a2a3c"), C("#9a3a4c"), C("#b85062")]
TEAL = [C("#10201f"), C("#18302f"), C("#22423f"), C("#2e5852"), C("#3e6f66"), C("#5a8c80"), C("#7eaa9c")]
SHOJI = [C("#c86a92"), C("#e88aac"), C("#f7b0c8"), C("#ffd8e6")]
LAMP = [C("#ff8ab4"), C("#ffc4da"), C("#fff0f6")]
EMBER = [C("#6a1408"), C("#b0300e"), C("#ee6a1e"), C("#ffb04a"), C("#ffe6a0")]
INDIGO = [C("#16203e"), C("#22305a"), C("#30447a")]
GOLD = [C("#6a4a14"), C("#a8781e"), C("#e0b040"), C("#ffe08a")]
BLACK = [C("#0e0c10"), C("#1c1a20"), C("#2c2a32"), C("#3e3c46")]
GRANITE = [C("#3a3848"), C("#4e4c60"), C("#646278"), C("#7c7a92"), C("#9a98ae")]
NAMAKO = [C("#1a1c24"), C("#262a34")]
PALE = [C("#5e5c6c"), C("#7c7a8c"), C("#9a98a8"), C("#b8b6c4"), C("#d6d4de")]


class Mesh:
    def __init__(self, tex=TEX):
        self.tex = tex
        self.vs, self.vts, self.faces = [], [], []

    def _v(self, p):
        self.vs.append(tuple(p))
        return len(self.vs)

    def _vt(self, u, v):
        self.vts.append((u / self.tex, v / self.tex))
        return len(self.vts)

    def face(self, pts, uvs, away=None, mat=None):
        """A face, wound so its normal points away from `away` (a point inside the solid; by
        default straight below the face's middle, so it faces up or out)."""
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        cz = sum(p[2] for p in pts) / len(pts)
        a = [pts[1][i] - pts[0][i] for i in range(3)]
        b = [pts[-1][i] - pts[0][i] for i in range(3)]
        n = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
        if away is None:
            away = (cx, cy, cz - 1)
        out = (cx - away[0], cy - away[1], cz - away[2])
        if n[0] * out[0] + n[1] * out[1] + n[2] * out[2] < 0:
            pts, uvs = pts[::-1], uvs[::-1]
        self.faces.append(([(self._v(p), self._vt(*uv)) for p, uv in zip(pts, uvs)], mat))

    def quad(self, p0, p1, p2, p3, uv, away=None, mat=None):
        """p0 the texture's top left, p1 its top right, p2 bottom right, p3 bottom left."""
        u0, v0, u1, v1 = uv
        e = 0.5
        self.face([p0, p1, p2, p3], [(u0 + e, v0 + e), (u1 - e, v0 + e), (u1 - e, v1 - e), (u0 + e, v1 - e)], away, mat)

    def box(self, x0, y0, z0, x1, y1, z1, side, top=None, front=None, back=None, lid=True, mat=None, front_mat=None):
        """A box without a bottom (nor a top without `lid`: under a roof it would be drawn over
        it). `side` textures the east and west faces, `front` the south (default `side`), `back`
        the north (default `front`), `top` the top."""
        front = front or side
        back = back or front
        top = top or side
        mid = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
        self.quad((x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0), front, mid, front_mat or mat)
        self.quad((x1, y0, z1), (x0, y0, z1), (x0, y0, z0), (x1, y0, z0), back, mid, mat)
        self.quad((x1, y1, z1), (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), side, mid, mat)
        self.quad((x0, y0, z1), (x0, y1, z1), (x0, y1, z0), (x0, y0, z0), side, mid, mat)
        if lid:
            self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), top, (mid[0], mid[1], z0), mat)

    def beam(self, a, b, thick, uv, mat=None):
        """A square timber from point a to point b."""
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        ln = math.sqrt(dx * dx + dy * dy + dz * dz)
        d = (dx / ln, dy / ln, dz / ln)
        up = (0, 0, 1) if abs(d[2]) < 0.9 else (1, 0, 0)
        s = norm(cross(d, up))
        u = norm(cross(s, d))
        h = thick / 2
        corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
        ring = lambda p: [tuple(p[i] + (s[i] * cx + u[i] * cy) * h for i in range(3)) for cx, cy in corners]
        ra, rb = ring(a), ring(b)
        mid = tuple((a[i] + b[i]) / 2 for i in range(3))
        for k in range(4):
            j = (k + 1) % 4
            self.quad(ra[k], ra[j], rb[j], rb[k], uv, mid, mat)
        self.quad(rb[0], rb[1], rb[2], rb[3], uv, mid, mat)
        self.quad(ra[0], ra[1], ra[2], ra[3], uv, mid, mat)

    def roof(self, outer, inner, z0, z1, slope, end, lift=0.2, flare=0.16, segs=8):
        """A hipped roof (or a band of one) from the eaves round `outer` (x0, y0, x1, y1) at z0
        up to `inner` at z1, which may close to a ridge or a point. The eaves sweep up toward the
        corners by `lift` and reach out there by `flare`, as Japanese roofs do. `slope` textures
        the long south and north faces, `end` the east and west ones, the eave at their foot."""
        ox0, oy0, ox1, oy1 = outer
        ix0, iy0, ix1, iy1 = inner
        cx, cy = (ox0 + ox1) / 2, (oy0 + oy1) / 2
        centre = (cx, cy, z0 - 3)

        def eave(t, a, b, n):
            # along the edge, pushed out at the corners (along the normal and the edge itself)
            # and lifted there, more the nearer the corner
            k = abs(2 * t - 1) ** 6
            ln = math.hypot(b[0] - a[0], b[1] - a[1]) or 1
            tx, ty = (b[0] - a[0]) / ln, (b[1] - a[1]) / ln
            ox = (n[0] + (2 * t - 1) * tx) * flare * k
            oy = (n[1] + (2 * t - 1) * ty) * flare * k
            return (a[0] + (b[0] - a[0]) * t + ox, a[1] + (b[1] - a[1]) * t + oy, z0 + lift * k)

        # each side: its outer edge (a to b), its inner edge, the way out, its texture
        sides = [
            ((ox0, oy1), (ox1, oy1), (ix0, iy1), (ix1, iy1), (0, 1), slope),
            ((ox1, oy0), (ox0, oy0), (ix1, iy0), (ix0, iy0), (0, -1), slope),
            ((ox1, oy1), (ox1, oy0), (ix1, iy1), (ix1, iy0), (1, 0), end),
            ((ox0, oy0), (ox0, oy1), (ix0, iy0), (ix0, iy1), (-1, 0), end),
        ]
        for (a, b, ia, ib, n, uv) in sides:
            u0, v0, u1, v1 = uv
            for i in range(segs):
                t0, t1 = i / segs, (i + 1) / segs
                e0, e1 = eave(t0, a, b, n), eave(t1, a, b, n)
                i0 = (ia[0] + (ib[0] - ia[0]) * t0, ia[1] + (ib[1] - ia[1]) * t0, z1)
                i1 = (ia[0] + (ib[0] - ia[0]) * t1, ia[1] + (ib[1] - ia[1]) * t1, z1)
                ua, ub = u0 + 0.5 + (u1 - u0 - 1) * t0, u0 + 0.5 + (u1 - u0 - 1) * t1
                if abs(i0[0] - i1[0]) + abs(i0[1] - i1[1]) < 1e-6:
                    self.face([i0, e1, e0], [((ua + ub) / 2, v0 + 0.5), (ub, v1 - 0.5), (ua, v1 - 0.5)], centre)
                else:
                    self.face([i0, i1, e1, e0], [(ua, v0 + 0.5), (ub, v0 + 0.5), (ub, v1 - 0.5), (ua, v1 - 0.5)], centre)

    def lantern(self, x, y, z, uv_cord, uv_lamp, size=0.2):
        """A paper lantern hung from (x, y, z): its cord and the lit lamp under it."""
        self.box(x - 0.02, y - 0.02, z - 0.25, x + 0.02, y + 0.02, z, uv_cord)
        h = size / 2
        self.box(x - h, y - h, z - 0.25 - size * 1.3, x + h, y + h, z - 0.25, uv_lamp, mat="Glow")

    def obj(self, name):
        lines = ["# %s: generated by tools/build_nexus_garden.py, don't edit" % name, "o %s" % name.replace(" ", "_")]
        lines += ["v %.4f %.4f %.4f" % v for v in self.vs]
        lines += ["vt %.5f %.5f" % t for t in self.vts]
        mat = None
        for ids, m in self.faces:
            if m != mat:
                lines.append("usemtl %s" % (m or "Default"))
                mat = m
            lines.append("f " + " ".join("%d/%d" % i for i in ids))
        return "\n".join(lines) + "\n"


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(v):
    ln = math.sqrt(sum(c * c for c in v)) or 1
    return tuple(c / ln for c in v)


# --- texture painters (regions of the 128 by 128 image) ------------------------------------------

def fill(img, rect, fn):
    u0, v0, u1, v1 = rect
    for y in range(v0, v1):
        for x in range(u0, u1):
            put(img, x, y, fn(x - u0, y - v0, u1 - u0, v1 - v0))


def solid(img, rect, ramp, t=0.6, grain=0.1, seed=1):
    fill(img, rect, lambda x, y, w, h: dither(ramp, t + grain * (hash2(x, y, seed) - 0.5), x, y))


def teal_tiles(img, rect):
    """Dark teal kawara in rows down the slope, a pale edge where they end at the eave."""
    def px(x, y, w, h):
        if y >= h - 2:
            return dither(TEAL, 0.9 if y == h - 2 else 0.55, x, y)
        ridge = x % 3
        t = 0.55 if ridge == 1 else (0.28 if ridge == 2 else 0.4)
        if y % 4 == 3:
            t -= 0.15
        t += 0.25 * (y / h)        # lighter toward the eave, as the light catches it
        t += 0.06 * (hash2(x, y, 11) - 0.5)
        return dither(TEAL, t, x, y)
    fill(img, rect, px)


def rose_wall(img, rect, panels=3, door=False):
    """Rose-red timber framing, lit shoji panels between the posts (painted here as their frames;
    the panes themselves are a `Glow` region)."""
    def px(x, y, w, h):
        post = w / panels
        if x % max(1, int(post)) == 0 or x == w - 1 or y in (0, h - 1):
            return dither(TIMBER, 0.6, x, y)
        return dither(ROSE, 0.6 + 0.15 * (hash2(x // 2, y, 5) - 0.5) - 0.2 * (y > h - 3), x, y)
    fill(img, rect, px)


def shoji(img, rect, bright=1.0):
    """Paper panes lit from inside, in a fine lattice."""
    def px(x, y, w, h):
        if x in (0, w - 1) or y in (0, h - 1):
            return TIMBER[2]
        if x % 3 == 0 or y % 4 == 0:
            return SHOJI[0]
        return SHOJI[2 if (x + y) % 5 else 3] if bright else SHOJI[1]
    fill(img, rect, px)


def plank_floor(img, rect, ramp=ROSE):
    def px(x, y, w, h):
        if y % 3 == 2:
            return dither(TIMBER, 0.7, x, y)
        return dither(ramp, 0.55 + 0.2 * (hash2(x // 5, y // 3, 2) - 0.5), x, y)
    fill(img, rect, px)


def stone_blocks(img, rect, ramp=GRANITE):
    def px(x, y, w, h):
        if y % 4 == 3 or (x + (y // 4) * 3) % 6 == 0:
            return ramp[0]
        return dither(ramp, 0.55 + 0.15 * (hash2(x, y, 4) - 0.5) + (0.15 if y % 4 == 0 else 0), x, y)
    fill(img, rect, px)


def plaster_wall(img, rect):
    def px(x, y, w, h):
        if y >= h - 5:
            return SHOJI[3] if (x + y) % 4 == 0 or (x - y) % 4 == 0 else NAMAKO[(x // 2 + y) % 2]
        if y < 2:
            return dither(TIMBER, 0.6, x, y)
        return dither(PLASTER[2:], 0.6 + 0.2 * (hash2(x, y, 9) - 0.5), x, y)
    fill(img, rect, px)


def iron_door(img, rect):
    def px(x, y, w, h):
        if x in (0, w - 1) or y == 0:
            return TIMBER[1]
        if y % 3 == 0 and x % 3 == 1:
            return GOLD[2]
        return BLACK[2] if x < w // 2 else BLACK[1]
    fill(img, rect, px)


# regions every building texture shares, in the 128 square
R_WALL = (0, 0, 64, 16)         # rose timber walls
R_PANE = (64, 0, 96, 16)        # lit shoji (Glow)
R_TIMBER = (96, 0, 112, 16)     # dark beams, posts, rails
R_LAMP = (112, 0, 120, 8)       # lantern paper (Glow)
R_CORD = (120, 0, 128, 8)       # cords, iron
R_DECK = (0, 16, 64, 32)        # veranda boards
R_STONE = (64, 16, 128, 32)     # plinths and steps
R_SLOPE = (0, 32, 128, 64)      # roof tiles, long faces
R_END = (0, 64, 64, 96)         # roof tiles, hip ends
R_CAP = (64, 64, 96, 72)        # ridge caps
R_GOLD = (96, 64, 112, 72)
R_EXTRA = (0, 96, 128, 128)     # each building's own


def base_texture():
    img = canvas(TEX)
    rose_wall(img, R_WALL, panels=4)
    shoji(img, R_PANE)
    solid(img, R_TIMBER, TIMBER, 0.55, 0.15)
    solid(img, R_LAMP, LAMP, 0.55, 0.4)
    solid(img, R_CORD, BLACK, 0.5, 0.2)
    plank_floor(img, R_DECK)
    stone_blocks(img, R_STONE)
    teal_tiles(img, R_SLOPE)
    teal_tiles(img, R_END)
    solid(img, R_CAP, TEAL, 0.2, 0.1)
    solid(img, R_GOLD, GOLD, 0.6, 0.3)
    return img


def walls(m, x0, y0, x1, y1, z0, z1, door_side="s", panes=True, inset=0.02):
    """Timber walls on a rectangle, lit panes set in them on every side (and a lit doorway in the
    `door_side`), posts at the corners."""
    m.box(x0, y0, z0, x1, y1, z1, R_WALL, lid=False)
    if panes:
        h0, h1 = z0 + (z1 - z0) * 0.18, z0 + (z1 - z0) * 0.82
        w = x1 - x0
        for k in range(int(w // 1.1)):
            a = x0 + 0.25 + k * (w - 0.5) / max(1, int(w // 1.1))
            b = a + (w - 0.5) / max(1, int(w // 1.1)) - 0.18
            m.quad((a, y1 + inset, h1), (b, y1 + inset, h1), (b, y1 + inset, h0), (a, y1 + inset, h0), R_PANE, (a, y1 - 1, h0), "Glow")
            m.quad((b, y0 - inset, h1), (a, y0 - inset, h1), (a, y0 - inset, h0), (b, y0 - inset, h0), R_PANE, (a, y0 + 1, h0), "Glow")
        d = y1 - y0
        for k in range(max(1, int(d // 1.1))):
            a = y0 + 0.25 + k * (d - 0.5) / max(1, int(d // 1.1))
            b = a + (d - 0.5) / max(1, int(d // 1.1)) - 0.18
            m.quad((x1 + inset, b, h1), (x1 + inset, a, h1), (x1 + inset, a, h0), (x1 + inset, b, h0), R_PANE, (x1 - 1, a, h0), "Glow")
            m.quad((x0 - inset, a, h1), (x0 - inset, b, h1), (x0 - inset, b, h0), (x0 - inset, a, h0), R_PANE, (x0 + 1, a, h0), "Glow")
    for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        m.box(cx - 0.08, cy - 0.08, z0, cx + 0.08, cy + 0.08, z1 + 0.05, R_TIMBER)


def railing(m, x0, y0, x1, y1, z, gap=None, h=0.42):
    """A veranda railing round a rectangle at height z, open at `gap` (x0, x1) on the south side."""
    edges = [((x0, y1), (x1, y1)), ((x1, y1), (x1, y0)), ((x1, y0), (x0, y0)), ((x0, y0), (x0, y1))]
    for i, (a, b) in enumerate(edges):
        if i == 0 and gap:
            parts = [(a, (gap[0], a[1])), ((gap[1], a[1]), b)]
        else:
            parts = [(a, b)]
        for p, q in parts:
            ln = math.hypot(q[0] - p[0], q[1] - p[1])
            if ln < 0.1:
                continue
            m.beam((p[0], p[1], z + h), (q[0], q[1], z + h), 0.07, R_TIMBER)
            n = max(1, int(ln / 0.7))
            for k in range(n + 1):
                t = k / n
                x, y = p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t
                m.box(x - 0.04, y - 0.04, z, x + 0.04, y + 0.04, z + h, R_TIMBER)


def steps(m, x0, x1, y_top, z_top, n=2, depth=0.3):
    """Stone steps down from a veranda's front edge."""
    for k in range(n):
        z = z_top * (n - k) / (n + 1)
        y = y_top + depth * (k + 1)
        m.box(x0, y - depth, 0, x1, y, z, R_STONE)


# --- the buildings -------------------------------------------------------------------------------

def house():
    """A village house: rose timber on a stone plinth, lit shoji all round, a veranda along its
    front with steps down, a teal roof sweeping up at its corners, lanterns at the front eaves.
    Five tiles by three."""
    img = base_texture()
    m = Mesh()
    x, y = 2.35, 1.35
    m.box(-x, -y, 0, x, y, 0.22, R_STONE)                         # plinth
    m.box(-x + 0.05, 0.75, 0.22, x - 0.05, y - 0.02, 0.3, R_DECK)  # veranda
    railing(m, -x + 0.1, 0.8, x - 0.1, y - 0.08, 0.3, gap=(-0.5, 0.5))
    walls(m, -x + 0.3, -y + 0.2, x - 0.3, 0.75, 0.3, 2.0)
    for px_ in (-x + 0.12, x - 0.12):                              # the veranda's posts
        m.box(px_ - 0.06, y - 0.16, 0.3, px_ + 0.06, y - 0.04, 2.05, R_TIMBER)
    m.roof((-x - 0.2, -y - 0.05, x + 0.2, y + 0.12), (-x + 1.1, -0.2, x - 1.1, -0.2), 2.0, 3.55, R_SLOPE, R_END)
    m.beam((-x + 1.0, -0.2, 3.58), (x - 1.0, -0.2, 3.58), 0.14, R_CAP)        # the ridge
    for lx in (-x + 0.15, x - 0.15):
        m.lantern(lx, y + 0.05, 2.0, R_CORD, R_LAMP)
    return m, img


GLYPHS = {
    "G": ["111", "100", "101", "101", "111"], "U": ["101", "101", "101", "101", "111"],
    "I": ["111", "010", "010", "010", "111"], "L": ["100", "100", "100", "100", "111"],
    "D": ["110", "101", "101", "101", "110"], "H": ["101", "101", "111", "101", "101"],
    "A": ["010", "101", "111", "101", "101"], " ": ["000", "000", "000", "000", "000"],
}


def sign(img, rect, text):
    """A lacquered board with gilded letters (a 3 by 5 pixel font) and a gold edge."""
    u0, v0, u1, v1 = rect
    solid(img, rect, BLACK, 0.45, 0.1)
    for x in range(u0, u1):
        put(img, x, v0, GOLD[1])
        put(img, x, v1 - 1, GOLD[0])
    for y in range(v0, v1):
        put(img, u0, y, GOLD[1])
        put(img, u1 - 1, y, GOLD[0])
    width = len(text) * 4 - 1
    x = u0 + (u1 - u0 - width) // 2
    y = v0 + (v1 - v0 - 5) // 2
    for ch in text:
        for j, row in enumerate(GLYPHS[ch]):
            for i, bit in enumerate(row):
                if bit == "1":
                    put(img, x + i, y + j, GOLD[3] if j < 2 else GOLD[2])
        x += 4


def teahouse():
    """The Guild Hall: two storeys, a veranda all round the lower one with a
    railing and steps, lit shoji everywhere, a skirt of teal roof between the storeys and a tall
    roof over the upper one, both sweeping up at the corners, lanterns hung at every corner and a
    gilded ornament at each end of the ridge, and a board reading GUILD HALL over the door. Seven
    tiles by five."""
    img = base_texture()
    SIGN = (64, 72, 128, 88)
    sign(img, SIGN, "GUILD HALL")
    m = Mesh()
    X, Y = 3.35, 2.35
    m.box(-X, -Y, 0, X, Y, 0.3, R_STONE)
    m.box(-X + 0.1, -Y + 0.1, 0.3, X - 0.1, Y - 0.1, 0.38, R_DECK)
    railing(m, -X + 0.15, -Y + 0.15, X - 0.15, Y - 0.15, 0.38, gap=(-0.6, 0.6))
    steps(m, -0.6, 0.6, Y - 0.15, 0.38, 2, 0.25)
    walls(m, -X + 0.85, -Y + 0.75, X - 0.85, Y - 0.85, 0.38, 2.0)
    for cx, cy in ((-X + 0.2, -Y + 0.2), (X - 0.2, -Y + 0.2), (-X + 0.2, Y - 0.2), (X - 0.2, Y - 0.2)):
        m.box(cx - 0.07, cy - 0.07, 0.38, cx + 0.07, cy + 0.07, 2.1, R_TIMBER)
    # the skirt roof round the lower storey, up to the upper storey's walls
    m.roof((-X - 0.2, -Y - 0.1, X + 0.2, Y + 0.2), (-1.75, -1.25, 1.75, 1.05), 2.05, 2.6, R_SLOPE, R_END)
    walls(m, -1.7, -1.2, 1.7, 1.0, 2.55, 3.9)
    m.box(-1.95, 0.95, 2.55, 1.95, 1.3, 2.61, R_DECK)              # the upper balcony
    railing(m, -1.9, 1.0, 1.9, 1.25, 2.61, h=0.3)
    m.roof((-2.35, -1.65, 2.35, 1.6), (-0.9, -0.3, 0.9, -0.3), 3.9, 5.5, R_SLOPE, R_END, 0.24, 0.2)
    m.beam((-1.0, -0.3, 5.53), (1.0, -0.3, 5.53), 0.16, R_CAP)
    for sx in (-1, 1):                                             # shachi on the ridge's ends
        m.beam((sx * 0.95, -0.3, 5.53), (sx * 1.12, -0.3, 5.82), 0.12, R_GOLD)
    for cx, cy in ((-X - 0.05, Y + 0.05), (X + 0.05, Y + 0.05), (-X - 0.05, -Y), (X + 0.05, -Y)):
        m.lantern(cx, cy, 2.2, R_CORD, R_LAMP, 0.24)
    for cx in (-2.3, 2.3):
        m.lantern(cx, 1.5, 4.0, R_CORD, R_LAMP, 0.2)
    # the sign, hung under the front eave over the steps, on two short posts
    m.quad((-1.6, Y - 0.02, 2.0), (1.6, Y - 0.02, 2.0), (1.6, Y - 0.02, 1.4), (-1.6, Y - 0.02, 1.4), SIGN, (0, 0, 1.7))
    for sx in (-1.45, 1.45):
        m.box(sx - 0.04, Y - 0.08, 2.0, sx + 0.04, Y, 2.1, R_TIMBER)
    return m, img


def storehouse():
    """A kura: thick white walls over dark tiles in a lattice of plaster, an iron-bound door, a
    teal roof. The vault chests' storehouse. Three tiles square."""
    img = base_texture()
    WHITE, DOOR = (0, 96, 48, 112), (48, 96, 64, 112)
    plaster_wall(img, WHITE)
    iron_door(img, DOOR)
    m = Mesh()
    x, y, h = 1.25, 1.05, 2.0
    m.box(-x - 0.1, -y - 0.1, 0, x + 0.1, y + 0.1, 0.2, R_STONE)
    m.box(-x, -y, 0.2, x, y, h, WHITE, lid=False)
    m.quad((-0.4, y + 0.02, 1.45), (0.4, y + 0.02, 1.45), (0.4, y + 0.02, 0.2), (-0.4, y + 0.02, 0.2), DOOR, (0, 0, 1))
    m.roof((-x - 0.25, -y - 0.15, x + 0.25, y + 0.25), (-0.6, -0.1, 0.6, -0.1), h - 0.05, h + 1.2, R_SLOPE, R_END)
    m.beam((-0.7, -0.1, h + 1.23), (0.7, -0.1, h + 1.23), 0.14, R_CAP)
    return m, img


def pagoda():
    """A three-storey pagoda in rose and lit shoji on a stone platform, teal roofs flaring and
    sweeping up at every storey, lanterns at the lowest eaves and a gilded spire of rings. Five
    tiles square and about eight high."""
    img = base_texture()
    m = Mesh()
    m.box(-2.4, -2.4, 0, 2.4, 2.4, 0.3, R_STONE)
    steps(m, -0.6, 0.6, 2.4, 0.3, 2, 0.2)
    z = 0.3
    for i, (half, height, eave) in enumerate(((1.55, 1.8, 2.3), (1.25, 1.5, 1.95), (1.0, 1.3, 1.65))):
        walls(m, -half, -half, half, half, z, z + height)
        z += height
        m.box(-half - 0.08, -half - 0.08, z, half + 0.08, half + 0.08, z + 0.12, R_TIMBER, lid=False)
        z += 0.12
        top = (1.25, 1.0, 0.0)[i] * 0.85
        rise = 0.75 if i < 2 else 1.25
        m.roof((-eave, -eave, eave, eave), (-top, -top, top, top), z - 0.05, z + rise, R_SLOPE, R_END, 0.24, 0.22, 10)
        if i == 0:
            for cx, cy in ((-eave, eave), (eave, eave), (-eave, -eave), (eave, -eave)):
                m.lantern(cx * 0.97, cy * 0.97, z + 0.2, R_CORD, R_LAMP, 0.22)
        z += rise if i == 2 else rise * 0.62
    m.box(-0.07, -0.07, z - 0.1, 0.07, 0.07, z + 1.5, R_GOLD)
    for k in range(5):
        r = 0.2 - k * 0.02
        zz = z + 0.15 + k * 0.22
        m.box(-r, -r, zz, r, r, zz + 0.06, R_GOLD)
    m.box(-0.13, -0.13, z + 1.5, 0.13, 0.13, z + 1.75, R_GOLD)
    return m, img


def smithy():
    """The smithy: an open frame of rose timber on stone footings, its rafters bare over the front
    and tiled over the back. Inside, the Divine Forge itself, a great stone furnace with a glowing
    mouth and coals and its chimney rising through the roof, and the Enchanter, a lacquered altar
    with a violet crystal over it. Both are the model, so they turn with the camera; their objects
    on the map are invisible (`SMITHY_FORGE`, `SMITHY_ALTAR`). Nine tiles by six; the model's tile
    is its front middle."""
    img = base_texture()
    FURNACE, MOUTH, CRYSTAL, LACQ, COALS = (0, 96, 32, 112), (32, 96, 48, 112), (48, 96, 64, 112), (64, 96, 80, 112), (80, 96, 96, 112)
    stone_blocks(img, FURNACE, GRANITE)

    def mouth(x, y, w, h):
        # an arched opening: dark round its rim, white-hot at its heart
        d = math.hypot((x - w / 2 + 0.5) / (w / 2), (y - h + 0.5) / h)
        if d > 1.0:
            return BLACK[1]
        return dither(EMBER, 1.1 - d * 0.95 + 0.1 * (hash2(x, y, 3) - 0.5), x, y)
    fill(img, MOUTH, mouth)
    fill(img, CRYSTAL, lambda x, y, w, h: dither([C("#3a1466"), C("#6224a0"), C("#9a3ccc"), C("#d06ae6"), C("#ffd0f8")],
                                                     0.35 + 0.6 * (1 - abs(x - w / 2) / (w / 2)) * (1 - y / h * 0.5), x, y))
    solid(img, LACQ, LACQUER, 0.55, 0.15)
    fill(img, COALS, lambda x, y, w, h: dither(EMBER, 0.45 + 0.5 * hash2(x // 2, y // 2, 9), x, y))
    m = Mesh()
    # the anchor is the front middle (y = 0); the building reaches back (north) to y = B. Its
    # ridge runs north to south, so its A-frames face the street like a gable's.
    X, B, F = 4.2, -5.6, 0.3
    eave, peak = 3.0, 5.6
    frames = [F, F + (B - F) / 3, F + 2 * (B - F) / 3, B]
    for py in frames:
        for px_ in (-X, X):
            m.box(px_ - 0.2, py - 0.2, 0, px_ + 0.2, py + 0.2, 0.2, R_STONE)
            m.box(px_ - 0.13, py - 0.13, 0.2, px_ + 0.13, py + 0.13, eave, R_TIMBER)
        # the A-frame: two rafters, a collar beam and a king post
        m.beam((-X - 0.35, py, eave - 0.15), (0, py, peak), 0.16, R_TIMBER)
        m.beam((X + 0.35, py, eave - 0.15), (0, py, peak), 0.16, R_TIMBER)
        m.beam((-X, py, eave), (X, py, eave), 0.16, R_TIMBER)
        m.beam((-X * 0.5, py, eave + (peak - eave) * 0.5), (X * 0.5, py, eave + (peak - eave) * 0.5), 0.1, R_TIMBER)
        m.beam((0, py, eave), (0, py, peak), 0.1, R_TIMBER)
    m.beam((0, F + 0.3, peak + 0.05), (0, B - 0.3, peak + 0.05), 0.16, R_TIMBER)       # the ridge
    for px_ in (-X, X):
        m.beam((px_, F + 0.3, eave), (px_, B - 0.3, eave), 0.14, R_TIMBER)
    for k in range(1, 4):                                          # purlins along both slopes
        t = k / 4
        for sx in (-1, 1):
            x, z = sx * (X + 0.35) * (1 - t), eave - 0.15 + (peak - eave + 0.15) * t
            m.beam((x, F + 0.3, z), (x, B - 0.3, z), 0.08, R_TIMBER)
    # tiles only over the back third, above the furnace: the rest of the rafters stay bare, so
    # the forge under them shows
    back = B + (F - B) / 3
    m.quad((0, B - 0.4, peak + 0.06), (0, back, peak + 0.06), (-X - 0.45, back, eave - 0.2), (-X - 0.45, B - 0.4, eave - 0.2),
           (0, 32, 64, 64), (2, -2, 0))
    m.quad((0, back, peak + 0.06), (0, B - 0.4, peak + 0.06), (X + 0.45, B - 0.4, eave - 0.2), (X + 0.45, back, eave - 0.2),
           (0, 32, 64, 64), (-2, -2, 0))
    m.box(-X + 0.25, B - 0.15, 0, X - 0.25, B + 0.15, 1.1, R_STONE)   # the low back wall
    # the Divine Forge: a great stone furnace across the back left, a broad arched mouth glowing
    # with fire, a gilded band, a hood narrowing into the chimney that rises through the roof
    fx0, fx1, fy0, fy1 = -X + 0.45, -0.35, B + 0.3, B + 2.25
    m.box(fx0, fy0, 0, fx1, fy1, 1.9, FURNACE)
    m.box(fx0 - 0.06, fy0 - 0.06, 1.55, fx1 + 0.06, fy1 + 0.06, 1.7, R_GOLD, lid=False)
    mx = (fx0 + fx1) / 2
    m.quad((mx - 0.95, fy1 + 0.03, 1.35), (mx + 0.95, fy1 + 0.03, 1.35), (mx + 0.95, fy1 + 0.03, 0.15), (mx - 0.95, fy1 + 0.03, 0.15),
           MOUTH, (mx, fy0, 0.8), "Glow")
    m.quad((fx0 + 0.15, fy0 + 0.15, 1.92), (fx1 - 0.15, fy0 + 0.15, 1.92), (fx1 - 0.15, fy1 - 0.15, 1.92), (fx0 + 0.15, fy1 - 0.15, 1.92),
           COALS, (mx, (fy0 + fy1) / 2, 0), "Glow")
    cx0, cx1, cy0, cy1 = mx - 0.55, mx + 0.55, fy0 + 0.35, fy0 + 1.25
    m.roof((fx0, fy0, fx1, fy1), (cx0, cy0, cx1, cy1), 1.9, 2.9, FURNACE, FURNACE, 0.0, 0.0, 4)
    m.box(cx0, cy0, 2.9, cx1, cy1, 6.6, FURNACE, lid=False)
    m.box(cx0 - 0.1, cy0 - 0.1, 6.6, cx1 + 0.1, cy1 + 0.1, 6.85, R_STONE)
    # the Enchanter: a lacquered altar on a stone step, a violet crystal turning in the air over it
    # and a lamp at each end
    ax0, ax1, ay0, ay1 = 1.0, 3.5, B + 0.6, B + 1.9
    m.box(ax0 - 0.15, ay0 - 0.15, 0, ax1 + 0.15, ay1 + 0.15, 0.2, R_STONE)
    m.box(ax0, ay0, 0.2, ax1, ay1, 0.95, LACQ, top=LACQ)
    m.box(ax0 - 0.05, ay0 - 0.05, 0.95, ax1 + 0.05, ay1 + 0.05, 1.03, R_GOLD, lid=False)
    cx, cy, cz = (ax0 + ax1) / 2, (ay0 + ay1) / 2, 1.85
    r, h = 0.42, 0.65                                              # an octahedron, lit from within
    tip, foot = (cx, cy, cz + h), (cx, cy, cz - h)
    ring = [(cx + r, cy, cz), (cx, cy + r, cz), (cx - r, cy, cz), (cx, cy - r, cz)]
    for k in range(4):
        p0, p1 = ring[k], ring[(k + 1) % 4]
        m.face([tip, p0, p1], [(56, 97), (49, 111), (63, 111)], (cx, cy, cz), "Glow")
        m.face([foot, p1, p0], [(56, 111), (63, 97), (49, 97)], (cx, cy, cz), "Glow")
    for lx in (ax0 + 0.2, ax1 - 0.2):
        m.box(lx - 0.1, cy - 0.1, 1.03, lx + 0.1, cy + 0.1, 1.33, R_LAMP, mat="Glow")
    m.lantern(X + 0.2, F + 0.15, eave - 0.05, R_CORD, R_LAMP, 0.26)
    m.lantern(-X - 0.2, F + 0.15, eave - 0.05, R_CORD, R_LAMP, 0.26)
    return m, img


# the smithy's furnace (the Divine Forge) and altar (the Enchanter), from its tile: the map puts
# their (invisible) objects there, so players use them where they're seen
SMITHY_FORGE = (-2.3, -4.3)
SMITHY_ALTAR = (2.25, -4.35)
# where the smithy's chimney top is, from its tile: the smoke stands there (`NG Chimney Smoke`)
SMITHY_CHIMNEY = (-2.1, -4.6, 6.85)


def yatai():
    """A market stall: a counter of rose wood under a little teal roof on four posts, an indigo
    noren along its front and a paper lantern at its corner. A tile; what it sells stands on the
    tile in front of it (south)."""
    img = base_texture()
    NOREN = (64, 96, 96, 104)

    def noren(x, y, w, h):
        if abs(x - w // 2) <= 1 and 2 <= y <= 5:
            return SHOJI[3]
        return INDIGO[1 + (x % 4 == 0)]
    fill(img, NOREN, noren)
    m = Mesh()
    m.box(-0.48, -0.48, 0, 0.48, -0.02, 0.62, R_WALL, top=R_DECK)       # the counter
    m.box(-0.52, -0.5, 0.62, 0.52, 0.02, 0.68, R_DECK)
    for x in (-0.46, 0.46):
        for y in (-0.46, 0.3):
            m.box(x - 0.04, y - 0.04, 0, x + 0.04, y + 0.04, 1.4 if y < 0 else 1.2, R_TIMBER)
    m.roof((-0.66, -0.62, 0.66, 0.48), (-0.3, -0.12, 0.3, -0.12), 1.15, 1.55, R_SLOPE, R_END, 0.08, 0.06, 4)
    m.quad((-0.6, 0.46, 1.17), (0.6, 0.46, 1.17), (0.6, 0.46, 0.92), (-0.6, 0.46, 0.92), NOREN, (0, 0, 1))
    m.lantern(0.6, 0.45, 1.15, R_CORD, R_LAMP, 0.16)
    return m, img


def arch(stone):
    """An arched bridge, nine tiles long east to west and three across: a deck curving up to half a
    tile over the water, its sides following the curve with the water showing beneath the arch,
    and a railing along each side (vermilion posts with brass caps, or a granite parapet). Drawn
    under what crosses it (`DrawUnder`); the deck's tiles lift whoever walks on them as high
    (`<Lift>` grounds, ARCH_LIFTS), so they climb it."""
    img = base_texture()
    DECK, SIDE, RAIL, CAP = (0, 96, 64, 112), (64, 96, 128, 112), (0, 112, 64, 120), (64, 112, 80, 120)
    if stone:
        def cobble(x, y, w, h):
            return dither(PALE, 0.62 + 0.3 * (hash2(x // 2, y // 2, 4) - 0.5) - (0.3 if (x % 4 == 0 or y % 3 == 0) else 0), x, y)
        fill(img, DECK, cobble)
        stone_blocks(img, SIDE, PALE)
        stone_blocks(img, RAIL, PALE)
        solid(img, CAP, PALE, 0.9, 0.1)
    else:
        def planks_across(x, y, w, h):
            if x % 4 == 3:
                return TIMBER[1]
            return dither(WOOD, 0.6 + 0.2 * (hash2(x // 4, y // 6, 2) - 0.5), x, y)
        fill(img, DECK, planks_across)
        solid(img, SIDE, LACQUER, 0.5, 0.12)
        solid(img, RAIL, LACQUER, 0.65, 0.12)
        solid(img, CAP, GOLD, 0.6, 0.3)
    m = Mesh()
    L, W, H, n = 4.5, 1.5, ARCH_HEIGHT, 18

    def z(x):
        return H * math.sin(math.pi * (x + L) / (2 * L))
    xs = [-L + 2 * L * i / n for i in range(n + 1)]
    for i in range(n):
        x0, x1 = xs[i], xs[i + 1]
        u0, u1 = 64 * i / n, 64 * (i + 1) / n
        m.quad((x0, -W, z(x0)), (x1, -W, z(x1)), (x1, W, z(x1)), (x0, W, z(x0)), (u0, 96, max(u0 + 1, u1), 112), (0, 0, -5))
        for side in (-1, 1):
            y = side * (W + 0.02)
            under = max(0.0, z(x0) - 0.22), max(0.0, z(x1) - 0.22)
            m.quad((x0, y, z(x0) + 0.04), (x1, y, z(x1) + 0.04), (x1, y, under[1]), (x0, y, under[0]),
                   (64 + u0, 96, 64 + max(u0 + 1, u1), 112), (0, -side * 5, 0))
            if stone:
                m.box(x0, side * W - 0.16, z(x0) - 0.02, x1, side * W + 0.16, z(x0) + 0.6, RAIL, top=CAP, lid=True)
            else:
                m.beam((x0, side * (W - 0.05), z(x0) + 0.62), (x1, side * (W - 0.05), z(x1) + 0.62), 0.08, RAIL)
                m.beam((x0, side * (W - 0.05), z(x0) + 0.3), (x1, side * (W - 0.05), z(x1) + 0.3), 0.05, RAIL)
    if stone:
        # a stone lantern on each end of each parapet, lit
        for x in (-L + 0.2, L - 0.2):
            for side in (-1, 1):
                y = side * W
                m.box(x - 0.2, y - 0.2, z(x), x + 0.2, y + 0.2, z(x) + 0.8, RAIL, top=CAP)
                m.box(x - 0.14, y - 0.14, z(x) + 0.8, x + 0.14, y + 0.14, z(x) + 1.05, R_LAMP, mat="Glow")
                m.box(x - 0.24, y - 0.24, z(x) + 1.05, x + 0.24, y + 0.24, z(x) + 1.15, CAP)
    if not stone:
        for i in range(0, n + 1, 2):
            x = xs[i]
            big = i in (0, n)
            for side in (-1, 1):
                y = side * (W - 0.05)
                r = 0.1 if big else 0.07
                m.box(x - r, y - r, z(x), x + r, y + r, z(x) + (0.9 if big else 0.74), RAIL)
                m.box(x - r - 0.02, y - r - 0.02, z(x) + (0.9 if big else 0.74), x + r + 0.02, y + r + 0.02,
                      z(x) + (1.02 if big else 0.82), CAP)
    return m, img


def wood_arch():
    return arch(False)


def stone_arch():
    return arch(True)


# how high the bridges' arch rises, and the lift of each of its nine tiles (the deck's grounds,
# `NG Arch Deck 1-5`, nexus_garden.grounds), west to east
from .grounds import ARCH_HEIGHT  # noqa: E402
ARCH_LIFTS = [round(ARCH_HEIGHT * math.sin(math.pi * (i + 0.5) / 9), 3) for i in range(9)]


def torii():
    """A vermilion torii: two pillars on black footings, the tie beam through them, the top beams
    sweeping up at their ends, black on top. Its pillars stand two tiles either side of its middle."""
    img = canvas(TEX)
    m = Mesh()
    RED, BLK, BASE = (0, 0, 32, 32), (32, 0, 64, 32), (0, 32, 32, 64)
    solid(img, RED, LACQUER, 0.62, 0.12)
    solid(img, BLK, BLACK, 0.55, 0.2)
    solid(img, BASE, BLACK, 0.4, 0.2)
    for px_ in (-2, 2):
        m.box(px_ - 0.16, -0.16, 0.3, px_ + 0.16, 0.16, 2.55, RED)
        m.box(px_ - 0.22, -0.22, 0, px_ + 0.22, 0.22, 0.3, BASE)
    m.box(-2.55, -0.1, 1.95, 2.55, 0.1, 2.15, RED)
    m.box(-0.12, -0.08, 2.15, 0.12, 0.08, 2.5, RED)
    for (x0, x1, lo, hi) in ((-1.6, 1.6, 2.5, 2.5), (1.6, 3.0, 2.5, 2.75), (-3.0, -1.6, 2.75, 2.5)):
        th = 0.22
        mid = ((x0 + x1) / 2, 0, (lo + hi) / 2 + th / 2)
        m.quad((x0, 0.2, lo + th), (x1, 0.2, hi + th), (x1, 0.2, hi), (x0, 0.2, lo), RED, mid)
        m.quad((x1, -0.2, hi + th), (x0, -0.2, lo + th), (x0, -0.2, lo), (x1, -0.2, hi), RED, mid)
        m.quad((x0, -0.24, lo + th), (x1, -0.24, hi + th), (x1, 0.24, hi + th), (x0, 0.24, lo + th), BLK, mid)
        m.quad((x1, 0.2, hi + th), (x1, -0.2, hi + th), (x1, -0.2, hi), (x1, 0.2, hi), RED, mid)
        m.quad((x0, -0.2, lo + th), (x0, 0.2, lo + th), (x0, 0.2, lo), (x0, -0.2, lo), RED, mid)
    return m, img


def wood_rail():
    """A bridge's railing, a tile long east to west: vermilion posts with brass caps at its ends
    and a top rail between them. Turned a quarter (`Rotation`) for bridges that run north."""
    img = canvas(TEX)
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
    img = canvas(TEX)
    m = Mesh()
    FACE, TOP = (0, 0, 64, 32), (0, 32, 64, 64)
    stone_blocks(img, FACE)
    solid(img, TOP, GRANITE, 0.75, 0.15)
    m.box(-0.5, -0.16, 0, 0.5, 0.16, 0.42, FACE, top=TOP)
    m.box(-0.5, -0.2, 0.42, 0.5, 0.2, 0.52, TOP)
    return m, img


def bamboo_fence():
    """A fence of bamboo poles bound to two rails, a tile long."""
    img = canvas(TEX)
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


# models drawn under what stands on them: the bridges' decks
UNDER = {"NG Arch Bridge", "NG Stone Arch"}
# each model's turned copies: suffix and degrees (railings, fences, torii and bridges a quarter for
# paths that run north, houses and stalls round to face north)
TURNS = {"NG House": [(" N", 180)], "NG Yatai": [(" N", 180)], "NG Torii": [(" NS", 90)],
         "NG Wood Rail": [(" NS", 90)], "NG Stone Rail": [(" NS", 90)], "NG Bamboo Fence": [(" NS", 90)],
         "NG Stone Arch": [(" NS", 90)]}

# name -> (builder, footprint (x, y) in tiles, blocks its tile, shadow opacity)
MODELS = {
    "NG House": (house, (5, 3), True, 0.3),
    "NG Teahouse": (teahouse, (7, 5), True, 0.32),
    "NG Storehouse": (storehouse, (3, 3), True, 0.3),
    "NG Pagoda": (pagoda, (5, 5), True, 0.32),
    "NG Smithy": (smithy, (9, 6), False, 0.26),
    "NG Yatai": (yatai, (1, 1), True, 0.2),
    "NG Arch Bridge": (wood_arch, (9, 1), False, 0.0),
    "NG Stone Arch": (stone_arch, (9, 1), False, 0.0),
    "NG Torii": (torii, (5, 1), False, 0.22),
    "NG Wood Rail": (wood_rail, (1, 1), True, 0.0),
    "NG Stone Rail": (stone_rail, (1, 1), True, 0.0),
    "NG Bamboo Fence": (bamboo_fence, (1, 1), True, 0.0),
}
