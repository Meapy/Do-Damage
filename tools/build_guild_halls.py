"""Builds the four guild halls, each grander than the last, as gardens in the look of the Nexus.

    python tools/build_guild_halls.py [--preview <folder>] [--dry-run]

Writes `Guild Hall.jm` and `Guild Hall 2.jm` to `Guild Hall 4.jm` (Worlds.xml's Guild Hall 1-4).
A guild starts in the first and its leaders buy the next with guild fame at the Guild Hall Upgrade
(Shared/database/guild/GuildHalls.cs has the prices and what each level adds; keep the two in
step). Every hall is built from the Nexus garden's parts (tools/build_nexus.py and
build_nexus_garden.py: its grounds, scenery and 3D models): an island of cherry-strewn grass in
a deep sea nobody can walk on, each in its own shape (`outline`: an octagon, a cross, a six-pointed
star and a long hall with a round head), black pines where its points are.

From the south up, every hall has:
- the spawn (the `Spawn` region) on a deck of pink-stained planks, the Nexus Portal and the Vault
  Portal either side of it, stone lanterns at its corners;
- a stone path north under a torii, lined with lanterns and young cherries, to the shrine: a
  stone dais where the Guild Hall Upgrade stands (none in the last hall), with the Guild Board and
  the Guild Chronicle either side and banners at its head, and behind it the teahouse with GUILD
  HALL over its door, on raked gravel between two great cherries;
- the guild vault: a yard fenced with bamboo, `VAULT_CHESTS[level]` Vault Chests in it, which
  `GuildHallWorld` links to the guild's shared vault in Redis, chest n being the n-th from the top,
  left to right. A hall's chests come first in the next hall's order (the vault keeps chest n
  whatever the hall), so lay new chests out after the old ones: the yard fills top to bottom, a
  row per two chests.

Then by level: 2 adds a training yard of dojo sand (Training Dummies, which measure damage), 3 the
guild's quarters on tatami (the Armoire and Potion Storage) and a koi pond, 4 the guild's own
smithy with the Divine Forge and the Enchanter (the Divine Workbench), as the Nexus's, and
Amatsumara at his anvil.

`--preview` draws each hall as build_nexus.py draws the Nexus, models and all.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_nexus as nx  # noqa: E402
import jm_maps as cm  # noqa: E402
from jm_maps import inside  # noqa: E402
from build_nexus import (  # noqa: E402
    SEA, POND, SHALLOWS, SAND, GRASS, MEADOW, PETALS, PATH, SLAB, GRAVEL, TATAMI, DECK, BEAM,
    SAKURA, WEEPING, PINE, MAPLE, GREAT, YOUNG, LANTERN, PAPER, PAPER_L, AZALEA, BOULDER, BAMBOO,
)

WORLDS = os.path.join(cm.ROOT, "source", "Shared", "resources", "worlds")
DOJO = "NG Dojo Sand"
FENCE, FENCE_NS = "NG Bamboo Fence", "NG Bamboo Fence NS"
BANNERS = ("NG Nobori Red", "NG Nobori Indigo")

# per level 1-4: the map's size, the guild vault's chests (GuildHalls.VaultChests), how wide the
# vault and the training yard are, and how far below the dais they start
SIZES = {1: (36, 36), 2: (46, 48), 3: (62, 56), 4: (72, 76)}
VAULT_CHESTS = {1: 2, 2: 4, 3: 6, 4: 8}
WING = {1: 7, 2: 10, 3: 11, 4: 12}
WING_DROP = {1: 5, 2: 5, 3: 5, 4: 13}
FILES = {1: "Guild Hall.jm", 2: "Guild Hall 2.jm", 3: "Guild Hall 3.jm", 4: "Guild Hall 4.jm"}
# rows added above each hall's outline for the shrine garden behind the dais
TOP = 4
# what can be walked over (anything else standing on a tile blocks it)
WALKABLE = {"NG Grass Tuft", "NG Pink Flowers", "NG Blue Flowers", "NG Fallen Petals", "NG Lily Pad", "NG Lotus",
            "NG Koi", "NG Stepping Stone", "Nexus Portal", "Vault Portal"}


def outline(level, w, h):
    """Each hall's own shape and its points (where black pines stand): an octagon, a cross (the
    vault and the training yard in its arms), a hexagon pointed east and west, and a long hall with
    a round head over the shrine and bays down its flanks."""
    cx = w // 2
    if level == 1:
        k = 10
        poly = [(3 + k, 3), (w - 3 - k, 3), (w - 3, 3 + k), (w - 3, h - 3 - k),
                (w - 3 - k, h - 3), (3 + k, h - 3), (3, h - 3 - k), (3, 3 + k)]
        m = k / 2 + 1
        return poly, [(3 + m, 3 + m), (w - 3 - m, 3 + m), (3 + m, h - 3 - m), (w - 3 - m, h - 3 - m)]
    if level == 2:
        n0, n1, t0, t1 = cx - 10, cx + 10, 8, 22
        poly = [(n0, 3), (n1, 3), (n1, t0), (w - 3, t0), (w - 3, t1), (n1, t1), (n1, h - 3),
                (n0, h - 3), (n0, t1), (3, t1), (3, t0), (n0, t0)]
        return poly, [(n0 + 1, 4), (n1 - 1, 4), (4, t0 + 1), (4, t1 - 1), (w - 4, t0 + 1), (w - 4, t1 - 1),
                      (n0 + 1, h - 4), (n1 - 1, h - 4)]
    if level == 3:
        poly = [(cx - 16, 3), (cx + 16, 3), (w - 3, h // 2), (cx + 16, h - 3), (cx - 16, h - 3), (3, h // 2)]
        return poly, [(x + (2 if x < cx else -2 if x > cx else 0), y + (2 if y < h // 2 else -2 if y > h // 2 else 0))
                      for x, y in poly]
    acx, acy, r = cx, 19, 16
    apse = [(acx - r * math.cos(math.radians(a)), acy - r * math.sin(math.radians(a))) for a in range(0, 181, 15)]
    poly = apse + [(w - 6, 19), (w - 2, 27), (w - 6, 35), (w - 6, 52), (w - 2, 60), (w - 6, 68), (cx + 10, h - 3),
                   (cx - 10, h - 3), (6, 68), (2, 60), (6, 52), (6, 35), (2, 27), (6, 19)]
    return poly, [(w - 5, 27), (w - 5, 60), (5, 27), (5, 60), (cx - 8, h - 5), (cx + 8, h - 5),
                  (cx - 14, 13), (cx + 14, 13)]


def build_hall(level):
    w, h0 = SIZES[level]
    h = h0 + TOP
    g = nx.Grid(w, h)
    g.name = f"Guild Hall {level}"
    rng = random.Random(20261031 + level)
    cx = w // 2
    poly, points = outline(level, w, h0)
    poly = [(x, y + TOP) for x, y in poly]
    points = [(int(round(x)), int(round(y)) + TOP) for x, y in points]
    sy = h - 10                                   # the spawn
    dy0 = 7 + TOP                                 # the dais's top row
    shrine = (cx, dy0 - 3)                        # the teahouse, behind the dais

    # the land: the outline and a rounded garden behind the dais for the shrine, sand at its shore
    for y in range(h):
        for x in range(w):
            sx_, sy_ = (x + 0.5 - shrine[0]) / 7.5, (y + 0.5 - shrine[1]) / 5.5
            if inside(poly, x + 0.5, y + 0.5) or sx_ * sx_ + sy_ * sy_ <= 1:
                g.ground(x, y, GRASS)
    for y in range(h):
        for x in range(w):
            if g[x, y]["ground"] == GRASS and any(not g.ok(x + dx, y + dy) or g[x + dx, y + dy]["ground"] == SEA
                                                  for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                g.ground(x, y, SAND)

    # the spawn deck: the spawn, the two portals either side, lanterns at its corners
    for y in range(sy - 2, sy + 3):
        for x in range(cx - 6, cx + 7):
            edge = y in (sy - 2, sy + 2) or x in (cx - 6, cx + 6)
            g.ground(x, y, BEAM if edge else DECK, keep=True)
    g.put(cx, sy, region="Spawn")
    g.place(cx - 4, sy, "Nexus Portal")
    g.place(cx + 4, sy, "Vault Portal")
    for x in (cx - 6, cx + 6):
        g.place(x, sy - 2, LANTERN, slot=nx.ACCENT)
        g.place(x, sy + 2, LANTERN, slot=nx.ACCENT)

    # the shrine: the dais, the teahouse behind it on raked gravel between two great cherries
    for y in range(dy0 - 9, dy0 + 1):
        for x in range(cx - 9, cx + 10):
            d = math.hypot((x - shrine[0]) / 7.5, (y - shrine[1]) / 5.0)
            if d <= 1 and g.land(x, y):
                g.ground(x, y, GRAVEL, keep=True)
    g.model("NG Teahouse", *shrine, ground=SLAB)
    for dx in (-6, 6):
        if g.empty(cx + dx, dy0 - 2) and g.land(cx + dx, dy0 - 2):
            g.place(cx + dx, dy0 - 2, GREAT)
    dais_w = 3 + level
    g.rect(cx - dais_w, dy0, cx + dais_w, dy0 + 2, SLAB)
    if level < 4:
        g.place(cx, dy0 + 1, f"Guild Hall Upgrade {level}")
    g.place(cx - 3, dy0 + 1, "NG Guild Board")
    g.place(cx + 3, dy0 + 1, "Guild Chronicle")
    for dx in (-dais_w, dais_w):
        g.place(cx + dx, dy0, LANTERN, slot=nx.ACCENT)
        g.place(cx + dx, dy0 + 2, LANTERN, slot=nx.ACCENT)
    g.place(cx - 1, dy0, BANNERS[0])
    g.place(cx + 1, dy0, BANNERS[1])

    # the path from the deck to the dais, under a torii (lined once the yards are laid)
    g.rect(cx - 1, dy0 + 3, cx + 1, sy - 3, PATH)
    torii = sy - 5
    g.model("NG Torii", cx, torii, blockers=nx.TORII_POSTS)

    # the guild vault: a fenced yard east of the path, chests two to a row on stone
    vault_x = cx + 4
    vx1 = vault_x + WING[level]
    vy0 = dy0 + WING_DROP[level]
    rows = (VAULT_CHESTS[level] + 1) // 2
    vy1 = vy0 + 2 * rows + 2
    yard(g, vault_x, vy0, vx1, vy1, SLAB, door=(vault_x, (vy0 + vy1) // 2))
    n = 0
    for r in range(rows):
        for x in (vault_x + 2, vx1 - 2):
            if n < VAULT_CHESTS[level]:
                g.place(x, vy0 + 2 + 2 * r, "Vault Chest")
                n += 1
    assert n == VAULT_CHESTS[level]
    # lanterns in the far corners, clear of the way between the chests
    for y in (vy0 + 1, vy1 - 1):
        g.place(vx1 - 1, y, PAPER_L, slot=nx.FLAME)

    # the west wing: from level 2 a training yard of dojo sand, the dummies in rows
    west_x1 = cx - 4
    wx0 = west_x1 - WING[level]
    ty0, ty1 = vy0, vy1
    if level >= 2:
        yard(g, wx0, ty0, west_x1, ty1, DOJO, door=(west_x1, (ty0 + ty1) // 2))
        for y in range(ty0 + 2, ty1 - 1, 2):
            for x in (wx0 + 2, wx0 + 4):
                g.place(x, y, "Training Dummy")
        for y in range(ty0 + 1, ty1):
            g.ground(west_x1 - 2, y, PATH, keep=True)
        g.place(west_x1 - 2, ty0 + 1, "NG Weapon Rack")
        g.place(west_x1 - 2, ty1 - 1, "NG Taiko")
        if level >= 3:
            g.place(wx0 + 6, ty0 + 1, BANNERS[0])
            g.place(wx0 + 6, ty1 - 1, BANNERS[1])

    # level 4: the guild's smithy below the wings, astride the path's west side, an armoury east
    ry1 = None
    if level >= 4:
        ry0 = max(vy1, ty1) + 2
        ry1 = min(sy - 4, ry0 + 11)
        rx0, rx1 = cx - 16, cx + 16
        yard(g, rx0, ry0, rx1, ry1, SLAB, door=(cx, ry0), door2=(cx, ry1))
        g.rect(cx - 1, ry0, cx + 1, ry1, PATH)
        nx.smithy(g, (cx - 9, ry1 - 3))
        g.model("NG Storehouse", cx + 9, ry1 - 5, ground=SLAB)
        for x in (cx + 5, cx + 13):
            g.place(x, ry1 - 7, BANNERS[(x - cx) // 8 % 2])
        g.place(cx + 6, ry1 - 2, "NG Weapon Rack")
        g.place(cx + 12, ry1 - 2, "NG Weapon Rack")
        for x, lamp in ((cx - 3, PAPER), (cx + 3, PAPER_L)):
            g.place(x, ry0 + 2, lamp, slot=nx.FLAME)
            g.place(x, ry1 - 2, lamp, slot=nx.FLAME)

    # from level 3, below the wings: the guild's quarters on tatami to the west (the Armoire and
    # the Potion Storage) and a koi pond to the east (in the last hall, below the smithy)
    if level >= 3:
        qy0 = (ry1 if level >= 4 else max(vy1, ty1)) + 3
        qy1 = min(qy0 + 8, sy - 4)
        yard(g, wx0, qy0, west_x1, qy1, TATAMI, door=(west_x1, (qy0 + qy1) // 2))
        for x in range(wx0 + 1, west_x1):
            g.ground(x, qy0 + 1, BEAM, keep=True)
            g.ground(x, qy1 - 1, BEAM, keep=True)
        g.place(wx0 + 3, (qy0 + qy1) // 2, "NG Armoire")
        g.place(wx0 + 6, (qy0 + qy1) // 2, "Potion Storage")
        g.place(wx0 + 1, qy0 + 1, PAPER, slot=nx.FLAME)
        g.place(wx0 + 1, qy1 - 1, PAPER, slot=nx.FLAME)
        g.place(west_x1 - 1, qy0 + 1, "NG Bonsai")
        koi_pond(g, rng, (vault_x + vx1) // 2, (qy0 + qy1) // 2)

    # lanterns and young cherries along the path, where nothing else stands
    for y in range(dy0 + 4, sy - 3):
        if abs(y - torii) <= 1 or (y - dy0) % 4 != 0:
            continue
        for x in (cx - 2, cx + 2):
            if g.empty(x, y) and g[x, y]["ground"] not in (SLAB, DOJO, TATAMI):
                g.place(x, y, LANTERN if (y - dy0) % 8 == 0 else YOUNG, slot=nx.ACCENT)
    plant(g, rng, points)
    g.mark_open()
    return g


def yard(g, x0, y0, x1, y1, floor, door, door2=None):
    """A yard inside the garden: `floor` within a bamboo fence, open at its doors (each three tiles
    wide along the side it's on)."""
    doors = set()
    for d in (door, door2):
        if d:
            dx, dy = d
            if dx in (x0, x1):
                doors |= {(dx, dy + i) for i in (-1, 0, 1)}
            else:
                doors |= {(dx + i, dy) for i in (-1, 0, 1)}
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            g.ground(x, y, floor if (x, y) not in doors else PATH, keep=True)
            if (x in (x0, x1) or y in (y0, y1)) and (x, y) not in doors:
                g.model(FENCE_NS if x in (x0, x1) else FENCE, x, y)


def koi_pond(g, rng, cx, cy):
    """A round pond to wade through, koi and lilies in it, the healing basin on an islet in its
    middle, lanterns round it."""
    for y in range(cy - 6, cy + 7):
        for x in range(cx - 7, cx + 8):
            d = math.hypot((x - cx) / 1.2, y - cy)
            if d <= 1.2:
                g.ground(x, y, SLAB, keep=True)
            elif d <= 3.6:
                g.ground(x, y, POND, keep=True)
            elif d <= 4.6:
                g.ground(x, y, SHALLOWS, keep=True)
            elif d <= 5.6:
                g.ground(x, y, PATH, keep=True)
    g.place(cx, cy, nx.BASIN)
    for k, (dx, dy) in enumerate(((-3, -1), (3, 1), (-1, 2), (2, -2), (-2, -2), (1, 2))):
        g.place(cx + dx, cy + dy, ("NG Koi", "NG Lily Pad", "NG Lotus")[k % 3], keep=False)
    for dx, dy in ((-6, 0), (6, 0), (0, -5), (0, 5)):
        if g.empty(cx + dx, cy + dy):
            g.place(cx + dx, cy + dy, LANTERN, slot=nx.ACCENT)


def plant(g, rng, points):
    """Black pines on the hall's points, trees along its shore and here and there inside, bushes,
    flowers and grass on the open ground, petals under the cherries."""
    for x, y in points:
        if g.ok(x, y) and g.land(x, y) and g.empty(x, y) and (x, y) not in g.kept:
            g.place(x, y, PINE, keep=False)
    spots = [(x, y) for y in range(g.h) for x in range(g.w)]
    rng.shuffle(spots)

    def shore(x, y, r):
        return any(g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] in (SAND, SEA)
                   for dx in range(-r, r + 1) for dy in range(-r, r + 1))

    def near_kept(x, y, r):
        return any((x + dx, y + dy) in g.kept for dx in range(-r, r + 1) for dy in range(-r, r + 1))

    green = (GRASS, MEADOW, PETALS)
    for x, y in spots:
        if g[x, y]["ground"] not in green:
            continue
        edge = shore(x, y, 2)
        if rng.random() < (0.22 if edge else 0.04) and g.free(x, y, 1, green + (SAND,)) and not near_kept(x, y, 2):
            tree = rng.choice([PINE, SAKURA, SAKURA, BAMBOO, MAPLE] if edge else [SAKURA, SAKURA, WEEPING, MAPLE, PINE])
            g.place(x, y, tree, keep=False)
            if tree in (SAKURA, WEEPING):
                for dy in range(-1, 3):
                    for dx in range(-2, 3):
                        if g.ok(x + dx, y + dy) and g[x + dx, y + dy]["ground"] == GRASS and (x + dx, y + dy) not in g.kept \
                                and rng.random() < 0.3:
                            g.ground(x + dx, y + dy, PETALS)
            continue
        r = rng.random()
        if r < 0.025 and g.free(x, y, 1, green):
            g.place(x, y, rng.choice([AZALEA, AZALEA, BOULDER, "NG Bonsai"]), keep=False)
        elif r < 0.1 and g.free(x, y, 0, green):
            g.place(x, y, rng.choice(["NG Grass Tuft", "NG Grass Tuft", "NG Pink Flowers", "NG Blue Flowers", "NG Fallen Petals"]),
                    keep=False)


def check_hall(g, level):
    chests = [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["obj"] == "Vault Chest"]
    assert len(chests) == VAULT_CHESTS[level], (g.name, len(chests))
    names = {g[x, y]["obj"] for y in range(g.h) for x in range(g.w)}
    want = {"Nexus Portal", "Vault Portal", "NG Guild Board", "Guild Chronicle"}
    if level < 4:
        want.add(f"Guild Hall Upgrade {level}")
    if level >= 2:
        want.add("Training Dummy")
    if level >= 3:
        want |= {"NG Armoire", "Potion Storage"}
    if level >= 4:
        want |= {"NG Divine Forge", "NG Enchanter"}
    assert want <= names, (g.name, want - names)
    spawn = [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["region"] == "Spawn"]
    assert len(spawn) == 1, g.name
    reach(g, spawn[0], chests + all_of(g, want - {"NG Divine Forge", "NG Enchanter"}))
    if level >= 4:
        # the forge and the Enchanter stand inside the smithy: within reach of its open front
        for name in ("NG Divine Forge", "NG Enchanter"):
            (fx, fy), = all_of(g, {name})
            assert reachable(g, spawn[0], (fx, fy + 3)), f"{g.name}: the front of the {name}"


def all_of(g, names):
    return [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["obj"] in names]


def walk(g, start):
    seen, todo = {start}, [start]
    while todo:
        x, y = todo.pop()
        for nx_, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx_, ny) in seen or not g.ok(nx_, ny):
                continue
            t = g[nx_, ny]
            if t["ground"] == SEA or (t["obj"] and t["obj"] not in WALKABLE):
                continue
            seen.add((nx_, ny))
            todo.append((nx_, ny))
    return seen


def reachable(g, start, at):
    return at in walk(g, start)


def reach(g, start, targets):
    """Every target can be walked up to from the spawn (a neighbour tile is reachable)."""
    seen = walk(g, start)
    for x, y in targets:
        assert (x, y) in seen or any(n in seen for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))), \
            f"{g.name}: {g[x, y]['obj']} at {x},{y} can't be reached"


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    for level in (1, 2, 3, 4):
        g = build_hall(level)
        check_hall(g, level)
        counts = cm.check(g)
        print(f"{g.name}: {g.w}x{g.h}, " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        path = os.path.join(WORLDS, FILES[level])
        if folder:
            nx.preview(g, os.path.join(folder, FILES[level][:-3] + ".png"), floor_portals=False)
        if "--dry-run" not in sys.argv:
            cm.save(g, path)
            print("  written", os.path.relpath(path, cm.ROOT))


if __name__ == "__main__":
    main()
