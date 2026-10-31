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


def teahouse():
    """The quest house and the guild's: two storeys, a veranda all round the lower one with a
    railing and steps, lit shoji everywhere, a skirt of teal roof between the storeys and a tall
    roof over the upper one, both sweeping up at the corners, lanterns hung at every corner and a
    gilded ornament at each end of the ridge. Seven tiles by five."""
    img = base_texture()
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
    """The smithy round the Divine Forge: an open frame of rose timber on stone footings, its
    rafters bare over the front and tiled over the back, a stone furnace in the back corner glowing
    through its mouth with its chimney rising through the roof, a low stone wall behind, and a
    lantern at the front. Nine tiles by six; the forge stands inside, in front of the furnace.
    The model's tile is its front middle, so whatever stands inside is drawn under its roof."""
    img = base_texture()
    FURNACE, MOUTH = (0, 96, 32, 112), (32, 96, 48, 112)
    stone_blocks(img, FURNACE, GRANITE)

    def mouth(x, y, w, h):
        d = math.hypot((x - w / 2 + 0.5) / (w / 2), (y - h + 0.5) / h)
        return dither(EMBER, 1.05 - d * 0.9, x, y)
    fill(img, MOUTH, mouth)
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
    # the furnace in the back left corner, its mouth glowing, its chimney up through the roof
    fx0, fx1, fy0, fy1 = -X + 0.4, -X + 2.2, B + 0.25, B + 1.65
    m.box(fx0, fy0, 0, fx1, fy1, 1.5, FURNACE)
    m.quad((fx0 + 0.45, fy1 + 0.02, 0.95), (fx1 - 0.45, fy1 + 0.02, 0.95), (fx1 - 0.45, fy1 + 0.02, 0.25), (fx0 + 0.45, fy1 + 0.02, 0.25),
           MOUTH, (0, -9, 0), "Glow")
    m.box(fx0 + 0.35, fy0 + 0.25, 1.5, fx1 - 0.35, fy1 - 0.25, 6.4, FURNACE)
    m.box(fx0 + 0.25, fy0 + 0.15, 6.4, fx1 - 0.25, fy1 - 0.15, 6.6, R_STONE)
    m.lantern(X + 0.2, F + 0.15, eave - 0.05, R_CORD, R_LAMP, 0.26)
    m.lantern(-X - 0.2, F + 0.15, eave - 0.05, R_CORD, R_LAMP, 0.26)
    return m, img


# where the smithy's chimney top is, from its tile: the smoke stands there (`NG Chimney Smoke`)
SMITHY_CHIMNEY = (-4.2 + 1.3, -5.6 + 0.95, 6.6)


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


# name -> (builder, footprint (x, y) in tiles, blocks its tile, shadow opacity)
MODELS = {
    "NG House": (house, (5, 3), True, 0.3),
    "NG Teahouse": (teahouse, (7, 5), True, 0.32),
    "NG Storehouse": (storehouse, (3, 3), True, 0.3),
    "NG Pagoda": (pagoda, (5, 5), True, 0.32),
    "NG Smithy": (smithy, (9, 6), False, 0.26),
    "NG Torii": (torii, (5, 1), False, 0.22),
    "NG Wood Rail": (wood_rail, (1, 1), True, 0.0),
    "NG Stone Rail": (stone_rail, (1, 1), True, 0.0),
    "NG Bamboo Fence": (bamboo_fence, (1, 1), True, 0.0),
}
