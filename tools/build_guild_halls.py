"""Builds the four guild halls, each grander than the last, in the castle look of the nexus.

    python tools/build_guild_halls.py [--preview <folder>] [--dry-run]

Writes `Guild Hall.jm` and `Guild Hall 2.jm` to `Guild Hall 4.jm` (Worlds.xml's Guild Hall 1-4).
A guild starts in the first and its leaders buy the next with guild fame at the Guild Hall Upgrade
(Shared/database/guild/GuildHalls.cs has the prices and what each level adds; keep the two in
step). Every hall is built from the same parts as the nexus and the vault (`build_castle_maps`):
violet stone, braziers, a curtain wall with towers and a moat, each hall in its own shape
(`outline`): an octagonal lodge, a cross-shaped hall, a star-fort keep and a citadel.

From the south gate up, every hall has:
- the court: the spawn (the `Spawn` region) on an ember rug, the Nexus Portal and the Vault Portal
  either side of it;
- a red carpet north to the dais, where the Guild Hall Upgrade stands (none in the last hall),
  with the Guild Board and the Guild Chronicle either side;
- the guild vault: `VAULT_CHESTS[level]` Vault Chests, which `GuildHallWorld` links to the guild's
  shared vault in Redis, chest n being the n-th from the top, left to right. A hall's chests come
  first in the next hall's order (the vault keeps chest n whatever the hall), so lay new chests
  out after the old ones: the vault wing fills top to bottom, a row per two chests.

Then by level: 2 adds a training yard (Training Dummies, which measure damage), 3 the guild's
quarters (the Armoire and Potion Storage) and a fountain garden, 4 a throne room with the Divine
Forge and the Enchanter (the Divine Workbench).

`--preview` draws each hall from the game's sprites, as build_castle_maps does.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_castle_maps as cm  # noqa: E402
from build_castle_maps import (  # noqa: E402
    Grid, FLOOR, PLAZA, PATH, RUG, HALL_FLOOR, CARPET, STONE, STALL, STALL_EDGE, GOLD_FLOOR,
    FLAME_OBJ, CANDLES, CRYSTAL, BANNER_OBJ,
)

WORLDS = os.path.join(cm.ROOT, "source", "Shared", "resources", "worlds")

# per level 1-4: the map's size, the guild vault's chests (GuildHalls.VaultChests), how wide the
# vault and the training yard are, and how far below the dais they start
SIZES = {1: (36, 36), 2: (46, 48), 3: (62, 56), 4: (72, 76)}
VAULT_CHESTS = {1: 2, 2: 4, 3: 6, 4: 8}
WING = {1: 7, 2: 10, 3: 11, 4: 12}
WING_DROP = {1: 5, 2: 5, 3: 5, 4: 13}
FILES = {1: "Guild Hall.jm", 2: "Guild Hall 2.jm", 3: "Guild Hall 3.jm", 4: "Guild Hall 4.jm"}


def outline(level, w, h):
    """Each hall's own shape and towers: the lodge an octagon, the hall a cross (the vault and the
    training yard in its arms), the keep a hexagon with a bastion on every point (a star fort), the
    citadel a round apse over the dais, a broad body and pointed bastions down its flanks."""
    cx = w // 2
    if level == 1:
        k = 10
        poly = [(3 + k, 3), (w - 3 - k, 3), (w - 3, 3 + k), (w - 3, h - 3 - k),
                (w - 3 - k, h - 3), (3 + k, h - 3), (3, h - 3 - k), (3, 3 + k)]
        m = k / 2 + 1
        towers = [(3 + m, 3 + m), (w - 3 - m, 3 + m), (3 + m, h - 3 - m), (w - 3 - m, h - 3 - m)]
        return poly, towers, 2.6
    if level == 2:
        n0, n1, t0, t1 = cx - 10, cx + 10, 8, 22
        poly = [(n0, 3), (n1, 3), (n1, t0), (w - 3, t0), (w - 3, t1), (n1, t1), (n1, h - 3),
                (n0, h - 3), (n0, t1), (3, t1), (3, t0), (n0, t0)]
        towers = [(n0 + 1, 4), (n1 - 1, 4), (4, t0 + 1), (4, t1 - 1), (w - 4, t0 + 1), (w - 4, t1 - 1),
                  (n0 + 1, h - 4), (n1 - 1, h - 4)]
        return poly, towers, 2.4
    if level == 3:
        poly = [(cx - 16, 3), (cx + 16, 3), (w - 3, h // 2), (cx + 16, h - 3), (cx - 16, h - 3), (3, h // 2)]
        return poly, [(x + (1 if x < cx else -1 if x > cx else 0), y + (1 if y < h // 2 else -1 if y > h // 2 else 0))
                      for x, y in poly], 3.4
    acx, acy, r = cx, 19, 16
    apse = [(acx - r * math.cos(math.radians(a)), acy - r * math.sin(math.radians(a))) for a in range(0, 181, 15)]
    poly = apse + [(w - 6, 19), (w - 2, 27), (w - 6, 35), (w - 6, 52), (w - 2, 60), (w - 6, 68), (cx + 10, h - 3),
                   (cx - 10, h - 3), (6, 68), (2, 60), (6, 52), (6, 35), (2, 27), (6, 19)]
    towers = [(w - 4, 27), (w - 4, 60), (4, 27), (4, 60), (cx - 9, h - 4), (cx + 9, h - 4),
              (cx - 15, 14), (cx + 15, 14)]
    return poly, towers, 2.8


def build_hall(level):
    w, h = SIZES[level]
    g = Grid(f"Guild Hall {level}", w, h)
    rng = random.Random(20261029 + level)
    cx = w // 2
    poly, towers, radius = outline(level, w, h)
    g.castle_walls(poly, towers, tower_radius=radius)
    sy = h - 10                                   # the spawn
    dy0 = 7                                       # the dais's top row

    # the gate: through the south wall below the spawn
    for x in range(cx - 1, cx + 2):
        for y in range(sy + 2, h):
            if not g.castle[y][x]:
                break
            if g.is_wall(x, y):
                g.put(x, y, PATH, None)

    # the court by the gate: the spawn and the two portals
    for y in range(sy - 1, sy + 2):
        for x in range(cx - 1, cx + 2):
            g.put(x, y, RUG)
    g.put(cx, sy, RUG, region="Spawn")
    g.put(cx - 4, sy, PLAZA, "Nexus Portal")
    g.put(cx + 4, sy, PLAZA, "Vault Portal")
    for dx in (-4, 4):
        for dy in (-1, 1):
            g.put(cx + dx, sy + dy, PLAZA)
        g.flame(cx + dx - 1, sy - 2, FLOOR)
        g.flame(cx + dx + 1, sy - 2, FLOOR)

    # the dais at the north end, a carpet to it from the spawn
    g.rect(cx - 1, dy0, cx + 1, sy - 2, CARPET)
    g.paths.update((x, y) for x in range(cx - 1, cx + 2) for y in range(dy0, sy + 2))
    dais_w = 3 + level
    for y in range(dy0, dy0 + 3):
        for x in range(cx - dais_w, cx + dais_w + 1):
            g.put(x, y, GOLD_FLOOR[min(6, 2 + level + (1 if abs(x - cx) < 2 else 0))] if (x, y) not in g.paths else CARPET)
            g.paths.add((x, y))
    if level < 4:
        g.put(cx, dy0 + 1, CARPET, f"Guild Hall Upgrade {level}")
    g.put(cx - 3, dy0 + 1, None, "Guild Board")
    g.put(cx + 3, dy0 + 1, None, "Guild Chronicle")
    for dx in (-dais_w, dais_w):
        g.put(cx + dx, dy0, None, FLAME_OBJ)
        g.put(cx + dx, dy0 + 2, None, FLAME_OBJ)
    g.put(cx - 1, dy0, CARPET, BANNER_OBJ)
    g.put(cx + 1, dy0, CARPET, BANNER_OBJ)

    # the nave: violet tiles either side of the carpet, brick posts and flames along it
    for y in range(dy0 + 3, sy - 1):
        for x in (cx - 2, cx + 2):
            g.put(x, y, HALL_FLOOR)
            if (y - dy0) % 4 == 1 and y < sy - 3:
                g.put(x, y, HALL_FLOOR, cm.WALL_IN)
            elif (y - dy0) % 4 == 3 and y < sy - 3:
                g.flame(x, y, HALL_FLOOR)

    # the guild vault: a wing east of the carpet, chests two to a row on a dark floor
    vault_x = cx + 4
    vx1 = vault_x + WING[level]
    vy0 = dy0 + WING_DROP[level]
    rows = (VAULT_CHESTS[level] + 1) // 2
    vy1 = vy0 + 2 * rows + 2
    room(g, vault_x, vy0, vx1, vy1, HALL_FLOOR, door=(vault_x, (vy0 + vy1) // 2))
    n = 0
    for r in range(rows):
        for cxx in (vault_x + 2, vx1 - 2):
            if n < VAULT_CHESTS[level]:
                g.put(cxx, vy0 + 2 + 2 * r, HALL_FLOOR, "Vault Chest")
                n += 1
    assert n == VAULT_CHESTS[level]
    g.put((vault_x + vx1) // 2, vy0 + 1, HALL_FLOOR, CANDLES)
    g.put((vault_x + vx1) // 2, vy1 - 1, HALL_FLOOR, CANDLES)

    # the west wing: from level 2 a training yard of dummies
    west_x1 = cx - 4
    wx0 = west_x1 - WING[level]
    ty0, ty1 = vy0, vy1
    if level >= 2:
        room(g, wx0, ty0, west_x1, ty1, STONE, door=(west_x1, (ty0 + ty1) // 2))
        for y in range(ty0 + 2, ty1 - 1, 2):
            for x in (wx0 + 2, wx0 + 4):
                g.put(x, y, STONE, "Training Dummy")
        for y in range(ty0 + 1, ty1):
            g.put(west_x1 - 2, y, PATH)
        g.put(west_x1 - 2, ty0 + 1, PATH, FLAME_OBJ)
        g.put(west_x1 - 2, ty1 - 1, PATH, FLAME_OBJ)

    # level 4: the throne room behind the vault and the yard, the Divine Workbench in its middle
    if level >= 4:
        ry0 = max(vy1, ty1) + 2
        ry1 = min(sy - 4, ry0 + 11)
        rx0, rx1 = cx - 11, cx + 11
        room(g, rx0, ry0, rx1, ry1, HALL_FLOOR, door=(cx, ry0), door2=(cx, ry1))
        for y in range(ry0, ry1 + 1):
            for x in (cx - 1, cx, cx + 1):
                g.put(x, y, CARPET)
                g.paths.add((x, y))
        my = (ry0 + ry1) // 2
        for y in range(my - 2, my + 3):
            for x in range(cx - 7, cx + 8):
                if abs(x - cx) > 1:
                    g.put(x, y, GOLD_FLOOR[max(0, 6 - abs(x - cx) // 2)])
        g.put(cx - 4, my, None, "Divine Forge")
        g.put(cx + 4, my, None, "Enchanter")
        for x in (rx0 + 2, rx1 - 2):
            for y in range(ry0 + 2, ry1 - 1, 2):
                g.put(x, y, HALL_FLOOR, BANNER_OBJ if (y - ry0) % 4 == 2 else FLAME_OBJ)
        for x in (cx - 7, cx + 7):
            g.put(x, my - 2, None, CANDLES)
            g.put(x, my + 2, None, CANDLES)

    # from level 3, below the wings: the guild's quarters to the west (the Armoire and the Potion
    # Storage) and a fountain garden to the east (in the last hall, below the throne room)
    if level >= 3:
        qy0 = (ry1 if level >= 4 else max(vy1, ty1)) + 3
        qy1 = min(qy0 + 8, sy - 4)
        room(g, wx0, qy0, west_x1, qy1, STALL, door=(west_x1, (qy0 + qy1) // 2))
        for x in range(wx0 + 1, west_x1):
            g.put(x, qy0 + 1, STALL_EDGE)
            g.put(x, qy1 - 1, STALL_EDGE)
        g.put(wx0 + 3, (qy0 + qy1) // 2, STALL, "Armoire")
        g.put(wx0 + 6, (qy0 + qy1) // 2, STALL, "Potion Storage")
        g.put(wx0 + 1, qy0 + 1, STALL_EDGE, FLAME_OBJ)
        g.put(wx0 + 1, qy1 - 1, STALL_EDGE, FLAME_OBJ)
        gx, gy = (vault_x + vx1) // 2, (qy0 + qy1) // 2
        for y in range(qy0, qy1 + 1):
            for x in range(vault_x, vx1 + 1):
                d = abs(x - gx) + abs(y - gy)
                if d <= 5:
                    g.put(x, y, PLAZA if d > 1 else STONE)
        g.put(gx, gy, STONE, cm.FOUNTAIN)
        for dx, dy in ((-3, 0), (3, 0), (0, -3), (0, 3)):
            g.put(gx + dx, gy + dy, PLAZA, CRYSTAL)
        for dx, dy in ((-2, -2), (2, -2), (-2, 2), (2, 2)):
            g.put(gx + dx, gy + dy, PLAZA, CANDLES)

    # light and quiet decoration on the open floor
    g.accents(rng, 0.03 + 0.005 * level)
    g.mark_open()
    return g


def room(g, x0, y0, x1, y1, floor, door, door2=None):
    """A room inside the courtyard: one ring of brick wall, lit windows, open at its doors (each
    door three tiles wide along the wall it's on)."""
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
            edge = x in (x0, x1) or y in (y0, y1)
            if edge and (x, y) not in doors:
                g.put(x, y, FLOOR, cm.WINDOW if (x + y) % 5 == 0 else cm.WALL_IN)
            else:
                g.put(x, y, floor)
                if (x, y) in doors:
                    g.paths.add((x, y))


def check_hall(g, level):
    chests = [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["obj"] == "Vault Chest"]
    assert len(chests) == VAULT_CHESTS[level], (g.name, len(chests))
    names = {g[x, y]["obj"] for y in range(g.h) for x in range(g.w)}
    want = {"Nexus Portal", "Vault Portal", "Guild Board", "Guild Chronicle"}
    if level < 4:
        want.add(f"Guild Hall Upgrade {level}")
    if level >= 2:
        want.add("Training Dummy")
    if level >= 3:
        want |= {"Armoire", "Potion Storage"}
    if level >= 4:
        want |= {"Divine Forge", "Enchanter"}
    assert want <= names, (g.name, want - names)
    for y in range(g.h):
        for x in range(g.w):
            t = g[x, y]
            assert g.castle[y][x] or not (t["obj"] or t["region"]), f"{g.name}: {t['obj']} at {x},{y} is outside the walls"
    spawn = [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["region"] == "Spawn"]
    assert len(spawn) == 1, g.name
    reach(g, spawn[0], chests + [p for p in all_of(g, want)])


def all_of(g, names):
    return [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["obj"] in names]


def reach(g, start, targets):
    """Every target can be walked up to from the spawn (a neighbour tile is reachable)."""
    blocking = set(cm.WALLS) | {"Vault Chest", "Training Dummy", "Armoire", "Potion Storage", "Divine Forge", "Enchanter",
                                "Guild Board", "Guild Chronicle", "Nexus Portal", "Vault Portal", cm.BRAZIER_OBJ, CRYSTAL}
    blocking |= {f"Guild Hall Upgrade {i}" for i in (1, 2, 3)}
    seen, todo = {start}, [start]
    while todo:
        x, y = todo.pop()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) in seen or not (0 <= nx < g.w and 0 <= ny < g.h):
                continue
            t = g[nx, ny]
            if not t["ground"] or t["ground"] == cm.MOAT or t["obj"] in blocking:
                continue
            seen.add((nx, ny))
            todo.append((nx, ny))
    for x, y in targets:
        assert any(n in seen for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))), f"{g.name}: {g[x, y]['obj']} at {x},{y} can't be reached"


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    for level in (1, 2, 3, 4):
        g = build_hall(level)
        check_hall(g, level)
        counts = cm.check(g)
        print(f"{g.name}: {g.w}x{g.h}, " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
        path = os.path.join(WORLDS, FILES[level])
        if folder:
            cm.preview(g, os.path.join(folder, FILES[level][:-3] + ".png"))
        if "--dry-run" not in sys.argv:
            cm.save(g, path)
            print("  written", os.path.relpath(path, cm.ROOT))


if __name__ == "__main__":
    main()
