"""Builds the vault: a private garden on an island, in the look of the Nexus and the guild halls.

    python tools/build_vault.py [--preview <folder>] [--dry-run]

Writes `Exalt/Vault Garden.jm` (Worlds.xml's Vault). Built from the Nexus garden's parts
(tools/build_nexus.py's grid and preview, build_guild_halls.py's fenced yards and planting).
`VaultWorld` places the chests on the region tiles nearest the spawn first, so only their number
matters: 60 `Vault` tiles (as many chests as any account can have) and 26 `Gifting Chest` ones.
In the Rust client blossom falls all over the vault (`petals::map_strength`).

From the south up:
- the spawn on a deck of pink-stained planks, the Glowing Portal of Cowardice back to the Nexus
  and the Daily Quest Portal beside it, the Training Dummy east of the spawn (`DpsMeter`: in reach
  of every weapon from the spawn);
- a stone court with the Potion Storage, the Armoire, the Guild Chronicle and the Guild Hall
  Portal;
- the gift chests in two bamboo-fenced yards of tatami either side of the path;
- under a torii, the chest garden: raked gravel, the chests in four rows on stone pads either side
  of the path, lanterns down it and storehouses at its head.
"""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_guild_halls as gh  # noqa: E402
import build_nexus as nx  # noqa: E402
import jm_maps as cm  # noqa: E402
from build_nexus import SEA, SAND, GRASS, PATH, SLAB, GRAVEL, TATAMI, DECK, BEAM, LANTERN, PAPER, PAPER_L, YOUNG, GREAT  # noqa: E402

SIZE = (46, 46)
# the island, clockwise: a broad garden of chests and a narrow court below it
OUTLINE = [(5, 2), (41, 2), (44, 5), (44, 28), (38, 34), (32, 34), (32, 44), (14, 44), (14, 34),
           (8, 34), (2, 28), (2, 5)]
SPAWN = (23, 39)
VAULT_CHESTS = 60
GIFT_CHESTS = 26
CHEST_ROWS = (6, 9, 12, 15)
CHEST_XS = list(range(7, 20, 2)) + list(range(27, 40, 2))
GIFT_ROWS = (24, 27)
GIFT_XS = list(range(5, 18, 2)) + list(range(29, 42, 2))
PATH_X = (22, 24)                       # the path up the middle, from the deck to the storehouses


def build():
    g = nx.Grid(*SIZE)
    g.name = "Vault"
    rng = random.Random(20261031)
    sx, sy = SPAWN
    for y in range(g.h):
        for x in range(g.w):
            if cm.inside(OUTLINE, x + 0.5, y + 0.5):
                g.ground(x, y, GRASS)
    for y in range(g.h):
        for x in range(g.w):
            if g[x, y]["ground"] == GRASS and any(not g.ok(x + dx, y + dy) or g[x + dx, y + dy]["ground"] == SEA
                                                  for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                g.ground(x, y, SAND)

    # the spawn deck, the portals, the Training Dummy
    for y in range(sy - 3, sy + 4):
        for x in range(sx - 6, sx + 7):
            edge = y in (sy - 3, sy + 3) or x in (sx - 6, sx + 6)
            g.ground(x, y, BEAM if edge else DECK, keep=True)
    g.put(sx, sy, region="Spawn")
    g.place(sx, sy + 3, "Glowing Portal of Cowardice")     # back up to the Nexus
    g.place(sx - 4, sy + 2, "Daily Quest Portal")
    g.place(sx + 5, sy, "Training Dummy")
    for x in (sx - 6, sx + 6):
        g.place(x, sy - 3, LANTERN, slot=nx.ACCENT)
        g.place(x, sy + 3, LANTERN, slot=nx.ACCENT)

    # the court: the storage, the wardrobe, the guild's chronicle and its hall's portal
    g.rect(8, 30, 38, 34, SLAB)
    for x in range(g.w):
        for y in range(30, 35):
            if g[x, y]["ground"] == SLAB and not cm.inside(OUTLINE, x + 0.5, y + 0.5):
                g.ground(x, y, SEA)
    g.place(sx - 8, 32, "Potion Storage")
    g.place(sx + 8, 32, "NG Armoire")
    g.place(sx - 12, 32, "Guild Chronicle")
    g.place(sx + 12, 32, "Guild Hall Portal")
    for x, lamp in ((sx - 3, PAPER), (sx + 3, PAPER_L)):
        g.place(x, 31, lamp, slot=nx.FLAME)

    # the path up the middle
    g.rect(PATH_X[0], 3, PATH_X[1], sy - 4, PATH)

    # the gift chests: two yards of tatami either side of the path, their gates on it
    for x0, x1, gate in ((3, 19, 19), (27, 43, 27)):
        gh.yard(g, x0, 22, x1, 29, TATAMI, door=(gate, 25))
    n = 0
    for y in GIFT_ROWS:
        for x in GIFT_XS:
            if n < GIFT_CHESTS:
                g.put(x, y, TATAMI, region="Gifting Chest", keep=True)
                n += 1
    assert n == GIFT_CHESTS, n

    # the chest garden: raked gravel, a stone pad under each chest, the torii at its foot
    for y in range(3, 21):
        for x in range(3, 44):
            if g[x, y]["ground"] == GRASS and not (PATH_X[0] <= x <= PATH_X[1]):
                g.ground(x, y, GRAVEL, keep=True)
    n = 0
    for y in CHEST_ROWS:
        for x in CHEST_XS + [sx]:
            if n < VAULT_CHESTS:
                g.put(x, y, SLAB if x != sx else PATH, region="Vault", keep=True)
                n += 1
    assert n == VAULT_CHESTS, n
    g.model("NG Torii", sx, 20, blockers=nx.TORII_POSTS)
    for y in (5, 11, 17):
        g.place(sx - 2, y, PAPER, slot=nx.FLAME)
        g.place(sx + 2, y, PAPER_L, slot=nx.FLAME)
    for x in (4, 42):
        for y in (7, 13, 19):
            g.place(x, y, LANTERN, slot=nx.ACCENT)
    for x in (4, 42):
        for y in (10, 16):
            g.place(x, y, YOUNG)
    g.model("NG Storehouse", 12, 4, ground=SLAB)
    g.model("NG Storehouse", 34, 4, ground=SLAB)
    for x in (8, 38):
        if g.empty(x, 20):
            g.place(x, 20, GREAT)

    gh.plant(g, rng, [(4, 4), (42, 4), (4, 27), (42, 27), (15, 42), (31, 42)])
    g.mark_open()
    return g


def check(g):
    regions = {}
    for row in g.t:
        for t in row:
            if t["region"]:
                regions[t["region"]] = regions.get(t["region"], 0) + 1
    assert regions == {"Spawn": 1, "Vault": VAULT_CHESTS, "Gifting Chest": GIFT_CHESTS}, regions
    spawn = (SPAWN[0], SPAWN[1])
    tiles = [(x, y) for y in range(g.h) for x in range(g.w) if g[x, y]["region"] in ("Vault", "Gifting Chest")]
    npcs = gh.all_of(g, {"Potion Storage", "NG Armoire", "Guild Chronicle", "Guild Hall Portal", "Glowing Portal of Cowardice",
                         "Daily Quest Portal", "Training Dummy"})
    assert len(npcs) == 7, npcs
    gh.reach(g, spawn, tiles + npcs)


def main():
    folder = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    g = build()
    check(g)
    counts = cm.check(g)
    print(f"{g.name}: {g.w}x{g.h}, " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    path = os.path.join(cm.WORLDS, "Vault Garden.jm")
    if folder:
        nx.preview(g, os.path.join(folder, "Vault Garden.png"), floor_portals=False)
    if "--dry-run" not in sys.argv:
        cm.save(g, path)
        print("  written", os.path.relpath(path, cm.ROOT))


if __name__ == "__main__":
    main()
