"""Draws the gateways and incantations of the last three stages of the Oryx chain.

    python tools/draw_oryx_gates.py [--preview <png>]

- OryxGates32x32.png: the portals into Oryx's Inferno (a horned maw of charred rock round a
  turning vortex of fire) and Oryx's Elysium (a marble temple door, gold pediment and columns,
  round a swirl of light), four frames each, and each one locked: still, dark and dimmed.
- OryxIncantations16x16.png: the incantations that open them, a seal for each stage in its own
  colours: Oryx's Sanctuary (a gold crescent on navy stone in a crimson ring, a crimson tassel),
  Oryx's Inferno (a flame on charred stone, its tongues licking over the rim) and Oryx's Elysium
  (a radiant star on white and gold, with wings). They're 16x16 for the detail; the client draws
  16-pixel items at the size of the 8-pixel ones (hud.rs, item_icon).

Both go in the client's custom/images (registered for the Rust client by tools/gen_rs_manifest.py).
The cells are at fixed places (below), which tools/build_hash_cellar.py, tools/build_elysium.py and
the incantations' XML point at (port_doomed.TEXTURE_OVERRIDES; the Sanctuary's in the prod XML).
"""
import math
import os
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_elder import write_png  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMAGES = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images")
GATES = ("oryxGates32x32", "OryxGates32x32.png")
INCANTATIONS = ("oryxIncantations16x16", "OryxIncantations16x16.png")

# where each picture is
INFERNO_FRAMES = [0, 1, 2, 3]
INFERNO_LOCKED = 4
ELYSIUM_FRAMES = [5, 6, 7, 8]
ELYSIUM_LOCKED = 9
INCANTATION_CELLS = {"Oryx Sanctuary Key": 0, "Hash Cellar Incantation": 1, "Doomed Cellar Incantation": 2}

CLEAR = (0, 0, 0, 0)


def blank(n):
    return [[CLEAR] * n for _ in range(n)]


def mix(a, b, f):
    f = min(1.0, max(0.0, f))
    return tuple(int(round(p + (q - p) * f)) for p, q in zip(a, b))


def ramp(stops, v):
    v = min(1.0, max(0.0, v))
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if v <= b:
            return mix(ca, cb, (v - a) / (b - a) if b > a else 0)
    return stops[-1][1]


def noise(x, y, seed=0):
    h = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xffff) / 65535


def put(img, x, y, c, a=255):
    if 0 <= x < len(img) and 0 <= y < len(img):
        img[y][x] = c[:3] + (a,)


def locked(img, dim=0.62, grey=0.55):
    """A still gateway: dimmed and drained of most of its colour."""
    out = blank(len(img))
    for y, row in enumerate(img):
        for x, (r, g, b, a) in enumerate(row):
            if a:
                l = 0.299 * r + 0.587 * g + 0.114 * b
                c = mix((r, g, b), (l, l, l), grey)
                out[y][x] = tuple(int(v * dim) for v in c) + (a,)
    return out


# ------------------------------------------------------------------------------------------------
# the gateways

def swirl(cx, cy, x, y, frame, arms, twist):
    """0..1: a spiral turning a quarter of an arm each frame, brightest at its heart."""
    dx, dy = x + 0.5 - cx, y + 0.5 - cy
    r = math.hypot(dx, dy)
    t = math.atan2(dy, dx)
    wave = 0.5 + 0.5 * math.sin(arms * t - twist * r + frame * math.pi / 2)
    return r, wave


VORTEX = [(0.0, (110, 14, 8)), (0.35, (214, 64, 18)), (0.7, (255, 156, 36)), (1.0, (255, 240, 170))]
ROCK = [(0.0, (26, 10, 10)), (0.5, (52, 20, 16)), (1.0, (84, 32, 24))]


def inferno(frame, still=False):
    img = blank(32)
    cx = 15.5
    for y in range(32):
        for x in range(32):
            dx = (x + 0.5 - cx)
            outer = (dx / 14.5) ** 2 + ((y + 0.5 - 31) / 28.5) ** 2 <= 1
            inner = (dx / 7.2) ** 2 + ((y + 0.5 - 31) / 20.5) ** 2 <= 1
            if inner:
                if still:
                    c = mix((22, 6, 6), (48, 12, 10), noise(x, y, 7) * 0.6)
                else:
                    r, w = swirl(cx, 21, x, y, frame, 3, 0.55)
                    heat = max(0.0, 1 - r / 13)
                    c = ramp(VORTEX, 0.25 * w + 0.75 * heat)
                put(img, x, y, c)
            elif outer:
                # charred rock, with molten veins running through it
                v = noise(x // 2, y // 2, 3) * 0.6 + noise(x, y, 4) * 0.4
                c = ramp(ROCK, v)
                vein = abs(math.sin(x * 0.45 + y * 0.3) + math.sin(y * 0.6 - x * 0.25)) < 0.07
                if vein and y > 6:
                    c = (150, 34, 14) if still else (255, 122, 30)
                put(img, x, y, c)
    # a rim of embers round the opening
    for y in range(32):
        for x in range(32):
            dx = (x + 0.5 - cx)
            e = (dx / 8.2) ** 2 + ((y + 0.5 - 31) / 21.5) ** 2
            if 1 < e <= 1.18 and img[y][x][3]:
                put(img, x, y, (120, 30, 12) if still else (226, 82, 22))
    # horns curling up from its shoulders, glowing at the tips
    for side in (-1, 1):
        for k in range(12):
            t = k / 11
            x = cx + side * (9.5 + 4.5 * t + 1.5 * math.sin(t * 2.6))
            y = 9 - 8.5 * t
            w = 1.6 * (1 - t) + 0.5
            for yy in range(int(y - w), int(y + w) + 1):
                for xx in range(int(x - w), int(x + w) + 1):
                    if math.hypot(xx + 0.5 - x, yy + 0.5 - y) <= w:
                        tip = t > 0.75
                        put(img, xx, yy, (255, 140, 40) if tip and not still else (120, 40, 24) if tip else (140, 38, 24))
    # embers rising above it
    if not still:
        for k in range(6):
            x = int(6 + noise(k, 1, 9) * 20)
            y = int((noise(k, 2, 9) * 12 - frame * 3) % 12)
            put(img, x, y, (255, 200, 80) if k % 2 else (255, 130, 40), 220)
    return img


MARBLE = [(0.0, (150, 158, 196)), (0.5, (210, 216, 236)), (1.0, (250, 251, 255))]
GOLD = [(0.0, (150, 100, 24)), (0.5, (232, 180, 56)), (1.0, (255, 238, 150))]
LIGHT = [(0.0, (88, 52, 170)), (0.3, (60, 140, 230)), (0.65, (150, 220, 255)), (1.0, (255, 255, 255))]


def elysium(frame, still=False):
    img = blank(32)
    # the doorway's light, behind everything
    for y in range(9, 28):
        for x in range(8, 24):
            if still:
                c = mix((40, 44, 70), (64, 70, 104), noise(x, y, 5) * 0.7)
            else:
                r, w = swirl(15.5, 18.5, x, y, frame, 2, 0.5)
                c = ramp(LIGHT, 0.3 * w + 0.7 * max(0.0, 1 - r / 11))
            put(img, x, y, c)
    # columns, fluted, with gold capitals and bases
    for x0 in (4, 24):
        for y in range(9, 28):
            for x in range(x0, x0 + 4):
                edge = x in (x0, x0 + 3)
                c = ramp(MARBLE, 0.15 if edge else (0.95 if (x - x0) % 2 else 0.6))
                put(img, x, y, c)
        for x in range(x0 - 1, x0 + 5):
            put(img, x, 8, ramp(GOLD, 0.8))
            put(img, x, 9, ramp(GOLD, 0.45))
            put(img, x, 27, ramp(GOLD, 0.45))
    # the lintel, a gold band under it
    for x in range(2, 30):
        put(img, x, 6, ramp(MARBLE, 0.9))
        put(img, x, 7, ramp(GOLD, 0.6 if x % 3 else 0.95))
    # the pediment: a marble triangle edged in gold, a light blue jewel in it
    for y in range(0, 6):
        half = 14.5 * (y + 1) / 6
        for x in range(32):
            d = abs(x + 0.5 - 16)
            if d <= half:
                put(img, x, y, ramp(GOLD, 0.85) if d > half - 1.2 or y == 5 else ramp(MARBLE, 0.85))
    put(img, 15, 3, (150, 220, 255))
    put(img, 16, 3, (150, 220, 255))
    put(img, 15, 4, (90, 150, 230))
    put(img, 16, 4, (90, 150, 230))
    put(img, 12, 4, (150, 90, 220))
    put(img, 19, 4, (150, 90, 220))
    # three steps of marble
    for i, y in enumerate((28, 29, 30, 31)):
        for x in range(3 - i, 29 + i):
            put(img, x, y, ramp(MARBLE, 0.9 - 0.25 * i) if y < 31 else ramp(MARBLE, 0.1))
        put(img, 3 - i, y, ramp(GOLD, 0.5))
        put(img, 28 + i, y, ramp(GOLD, 0.5))
    # sparkles in the light
    if not still:
        for k in range(4):
            x = 9 + int(noise(k, frame, 11) * 14)
            y = 10 + int(noise(k, frame + 7, 11) * 16)
            put(img, x, y, (255, 255, 255))
    return img


# ------------------------------------------------------------------------------------------------
# the incantations

def seal(img, rim, face, cx=7.5, cy=7.5, r=6.4):
    for y in range(16):
        for x in range(16):
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            if d <= r:
                put(img, x, y, rim if d > r - 1.2 else face(x, y, d))


def sanctuary_incantation():
    img = blank(16)
    navy = lambda x, y, d: mix((22, 30, 72), (48, 62, 122), (8 - y) / 10 + 0.2)
    seal(img, (232, 188, 72), navy, cy=7.0)
    # a crimson ring inside the rim
    for y in range(16):
        for x in range(16):
            d = math.hypot(x + 0.5 - 7.5, y + 0.5 - 7.0)
            if 4.4 < d <= 5.2:
                put(img, x, y, (186, 32, 52))
    # a gold crescent moon, a teal star by it
    for y in range(16):
        for x in range(16):
            a = math.hypot(x + 0.5 - 7.0, y + 0.5 - 7.0)
            b = math.hypot(x + 0.5 - 8.6, y + 0.5 - 6.2)
            if a <= 3.4 and b > 2.6:
                put(img, x, y, (255, 222, 120) if a < 2.4 else (232, 178, 60))
    put(img, 9, 7, (140, 230, 230))
    put(img, 10, 8, (140, 230, 230))
    # a crimson tassel hanging below
    for y, xs in ((13, (7, 8)), (14, (7, 8)), (15, (6, 9))):
        for x in xs:
            put(img, x, y, (160, 24, 40) if y < 15 else (210, 60, 70))
    return img


FLAME = [
    "......o.......",
    ".....oo....o..",
    "....ooy...oo..",
    "...oyyo..oyo..",
    "...oyyoo.oyo..",
    "..ooyWyooyyo..",
    "..oyWWyoyWyo..",
    "..oyWWWyyWyo..",
    "..oyWWWWWWyo..",
    "..roWWWWWWor..",
    "..rooWWWWoor..",
    "...rooyyoor...",
    "....rrooorr...",
    ".....rrrrr....",
]
FLAME_COLOURS = {"W": (255, 246, 200), "y": (255, 210, 80), "o": (255, 130, 30), "r": (196, 40, 16)}


def inferno_incantation():
    img = blank(16)
    char = lambda x, y, d: mix((30, 10, 8), (64, 22, 16), noise(x, y, 2))
    seal(img, (214, 70, 22), char, cy=8.5, r=6.6)
    # a flame of three tongues, white-hot at its heart, licking up over the rim
    for y, row in enumerate(FLAME):
        for x, ch in enumerate(row):
            if ch in FLAME_COLOURS:
                put(img, x + 1, y, FLAME_COLOURS[ch])
    return img


def elysium_incantation():
    img = blank(16)
    face = lambda x, y, d: mix((255, 255, 255), (214, 228, 250), d / 6)
    seal(img, (232, 184, 56), face, r=5.4)
    # wings on each side, white with a blue shade under them
    for side in (-1, 1):
        for y in range(4, 11):
            span = int(3 - abs(y - 6.5) * 0.6) + 1
            for k in range(span):
                x = 7 + side * (5 + k) + (1 if side > 0 else 0)
                put(img, x, y, (255, 255, 255) if y < 8 else (196, 220, 248))
    # a radiant star: white rays out past the rim (gold tipped), short gold rays inside, a light
    # blue heart, purple at its corners
    for x in (7, 8):
        for y, c in ((0, (240, 192, 64)), (1, (255, 255, 255)), (14, (255, 255, 255)), (15, (240, 192, 64))):
            put(img, x, y, c)
    for x, y in ((7, 4), (8, 4), (7, 11), (8, 11), (4, 7), (4, 8), (11, 7), (11, 8)):
        put(img, x, y, (240, 192, 64))
    for y in range(5, 11):
        for x in range(5, 11):
            d = abs(x + 0.5 - 8) + abs(y + 0.5 - 8)
            if d <= 2.6:
                put(img, x, y, (230, 250, 255) if d < 1.2 else (110, 200, 250))
    for x, y in ((5, 5), (10, 5), (5, 10), (10, 10)):
        put(img, x, y, (150, 90, 220))
    return img


# ------------------------------------------------------------------------------------------------
# the portal objects the dungeon scripts make from them

def gate_object(type_code, oid, frames, dungeon, display=None):
    """An open portal showing `frames` of OryxGates32x32 in turn."""
    e = ET.Element("Object", {"type": type_code, "id": oid})
    if display:
        ET.SubElement(e, "DisplayId").text = display
    ET.SubElement(e, "Class").text = "Portal"
    ET.SubElement(e, "IntergamePortal")
    ET.SubElement(e, "DungeonName").text = dungeon
    tex = ET.SubElement(e, "Texture")
    ET.SubElement(tex, "File").text = GATES[0]
    ET.SubElement(tex, "Index").text = "0x%02x" % frames[0]
    ET.SubElement(e, "Size").text = "100"
    ET.SubElement(e, "ShadowSize").text = "0"
    anim = ET.SubElement(e, "Animation", {"prob": "1.0"})
    for f in frames:
        frame = ET.SubElement(anim, "Frame", {"time": "0.12"})
        t = ET.SubElement(frame, "Texture")
        ET.SubElement(t, "File").text = GATES[0]
        ET.SubElement(t, "Index").text = "0x%02x" % f
    return e


def locked_object(type_code, oid, cell):
    """A locked portal: the gateway, still (an incantation turns it into the open one)."""
    e = ET.Element("Object", {"type": type_code, "id": oid})
    ET.SubElement(e, "Class").text = "Portal"
    ET.SubElement(e, "LockedPortal")
    tex = ET.SubElement(e, "Texture")
    ET.SubElement(tex, "File").text = GATES[0]
    ET.SubElement(tex, "Index").text = "0x%02x" % cell
    ET.SubElement(e, "Size").text = "100"
    ET.SubElement(e, "ShadowSize").text = "0"
    return e


def sheet(cells, n, per_row):
    rows = (len(cells) + per_row - 1) // per_row
    pix = [[CLEAR] * (per_row * n) for _ in range(rows * n)]
    for k, cell in enumerate(cells):
        ox, oy = (k % per_row) * n, (k // per_row) * n
        for y in range(n):
            for x in range(n):
                pix[oy + y][ox + x] = cell[y][x]
    return per_row * n, rows * n, pix


def main():
    gates = [None] * 10
    for i, f in enumerate(INFERNO_FRAMES):
        gates[f] = inferno(i)
    gates[INFERNO_LOCKED] = locked(inferno(0, still=True), dim=0.8, grey=0.3)
    for i, f in enumerate(ELYSIUM_FRAMES):
        gates[f] = elysium(i)
    gates[ELYSIUM_LOCKED] = locked(elysium(0, still=True))
    incs = [None] * 3
    incs[INCANTATION_CELLS["Oryx Sanctuary Key"]] = sanctuary_incantation()
    incs[INCANTATION_CELLS["Hash Cellar Incantation"]] = inferno_incantation()
    incs[INCANTATION_CELLS["Doomed Cellar Incantation"]] = elysium_incantation()

    os.makedirs(IMAGES, exist_ok=True)
    write_png(os.path.join(IMAGES, GATES[1]), *sheet(gates, 32, 8))
    write_png(os.path.join(IMAGES, INCANTATIONS[1]), *sheet(incs, 16, 8))
    print("wrote", GATES[1], "and", INCANTATIONS[1])

    if "--preview" in sys.argv:
        # everything at 6x on a dark background
        path = sys.argv[sys.argv.index("--preview") + 1]
        z, items = 6, gates + [None] + [[[CLEAR] * 32 for _ in range(8)] + [[CLEAR] * 8 + r + [CLEAR] * 8 for r in c] + [[CLEAR] * 32 for _ in range(8)] for c in incs]
        w = len(items) * 34 * z
        pix = [[(40, 40, 48, 255)] * w for _ in range(34 * z)]
        for k, cell in enumerate(items):
            if cell is None:
                continue
            for y in range(32 * z):
                for x in range(32 * z):
                    p = cell[y // z][x // z]
                    if p[3]:
                        a = p[3] / 255
                        b = pix[z + y][k * 34 * z + z + x]
                        pix[z + y][k * 34 * z + z + x] = tuple(int(p[i] * a + b[i] * (1 - a)) for i in range(3)) + (255,)
        write_png(path, w, 34 * z, pix)


if __name__ == "__main__":
    main()
