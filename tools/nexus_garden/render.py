"""A software copy of the Rust client's model drawing (view.rs draw_model: the camera's oblique
projection, faces facing away skipped, the rest lowest first, shaded by Lighting3D), for previews
of the models and of the Nexus map without starting the game."""
import math


def shade(n):
    ln = math.sqrt(14)
    light = (1 / ln, 3 / ln, 2 / ln)
    nl = math.sqrt(sum(c * c for c in n)) or 1
    return 0.75 + 0.25 * max(0.0, sum(a * b for a, b in zip(n, light)) / nl)


def draw_model(mesh, tex, out, ox, oy, scale=8, angle=0.0, rotation=0.0):
    """Draws `mesh` (a models.Mesh) textured with `tex` onto `out`, its origin at pixel (ox, oy),
    `scale` pixels a tile, the camera turned by `angle` and the model by `rotation` degrees."""
    s, c = math.sin(angle), math.cos(angle)
    rs, rc = math.sin(math.radians(rotation)), math.cos(math.radians(rotation))

    def screen(p):
        x, y, z = p
        x, y = x * rc - y * rs, x * rs + y * rc
        return ox + (x * c + y * s) * scale, oy + (-x * s + y * c - z) * scale

    faces = []
    for f in mesh.faces:
        pts = [mesh.vs[i - 1] for i, _ in f]
        uvs = [mesh.vts[t - 1] for _, t in f]
        zs = [p[2] for p in pts]
        faces.append(((min(zs), max(zs)), pts, uvs))
    faces.sort(key=lambda k: k[0])
    tw, th = tex.size
    tp, op = tex.load(), out.load()
    for _, pts, uvs in faces:
        sp = [screen(p) for p in pts]
        u = (sp[1][0] - sp[0][0], sp[1][1] - sp[0][1])
        v = (sp[-1][0] - sp[0][0], sp[-1][1] - sp[0][1])
        if u[0] * v[1] - u[1] * v[0] < 0:
            continue
        p0, p1, pl = pts[0], pts[1], pts[-1]
        a = [p1[i] - p0[i] for i in range(3)]
        b = [pl[i] - p0[i] for i in range(3)]
        sh = shade((a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]))
        for k in range(1, len(pts) - 1):
            P = [sp[0], sp[k], sp[k + 1]]
            T = [uvs[0], uvs[k], uvs[k + 1]]
            (x0, y0), (x1, y1), (x2, y2) = P
            den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
            if abs(den) < 1e-9:
                continue
            for yy in range(max(0, int(min(y0, y1, y2))), min(out.height, int(max(y0, y1, y2)) + 1)):
                for xx in range(max(0, int(min(x0, x1, x2))), min(out.width, int(max(x0, x1, x2)) + 1)):
                    px, py = xx + 0.5, yy + 0.5
                    w0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den
                    w1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den
                    w2 = 1 - w0 - w1
                    if w0 < -1e-6 or w1 < -1e-6 or w2 < -1e-6:
                        continue
                    tu = w0 * T[0][0] + w1 * T[1][0] + w2 * T[2][0]
                    tv = w0 * T[0][1] + w1 * T[1][1] + w2 * T[2][1]
                    col = tp[min(tw - 1, int(tu * tw)), min(th - 1, int(tv * th))]
                    if col[3]:
                        op[xx, yy] = (int(col[0] * sh), int(col[1] * sh), int(col[2] * sh), 255)
