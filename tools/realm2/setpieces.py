"""Realm 2's versions of the realm bosses' own set pieces (tools/build_realm2.py writes them): a boss
that places a map round itself (PlaceMap) places its Realm 2 version there, in the colours of its
land, when there is one (SetPieces.MapFor in the world server).

- The Hermit God's island and lagoon (spawn and death) in the Tidepools' grounds: its kelp-bed
  sand, the tide pools' water and a deeper tide for the lagoon's middle, seagrass for the grass.
- The Skull Shrine's court in the Bonefields' blood soil, bone dust and charred stone.
- The Eye of the Dragon's shrine in the Dragon's Roost's scorched earth and gold-strewn hoard.
- The Grand Sphinx: the realm's 81-tile desert dwarfed its land, so a court of the Sunken Tombs'
  own sand and sandstone, the size of the other lands' arenas, with blocks to hide behind (he taunts
  those who do) and patches of sinking sand round it.

Each is written to source/Shared/resources/worlds/Realm2/<the original's path>.
"""
import base64
import json
import math
import os
import random
import struct
import zlib

SETPIECES = os.path.join("source", "Shared", "resources", "worlds")


def load(root, rel):
    with open(os.path.join(root, SETPIECES, rel), encoding="utf-8") as f:
        d = json.load(f)
    data = zlib.decompress(base64.b64decode(d["data"]))
    idx = struct.unpack(">%dH" % (len(data) // 2), data)
    return d["width"], d["height"], [dict(d["dict"][i]) for i in idx]


def save(root, rel, w, h, tiles):
    entries, index, out = [], {}, bytearray()
    for t in tiles:
        e = {k: v for k, v in t.items() if v}
        key = json.dumps(e, sort_keys=True)
        if key not in index:
            index[key] = len(entries)
            entries.append(e)
        i = index[key]
        out += bytes((i >> 8, i & 0xff))
    path = os.path.join(root, SETPIECES, "Realm2", rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    jm = {"data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii"),
          "dict": entries, "height": h, "width": w}
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))
    return path


def remap(root, rel, grounds, objects):
    """The map with its grounds and objects swapped by the tables (anything else kept)."""
    w, h, tiles = load(root, rel)
    for t in tiles:
        if t.get("ground") in grounds:
            t["ground"] = grounds[t["ground"]]
        if t.get("objs"):
            t["objs"] = [dict(o, id=objects.get(o["id"], o["id"])) for o in t["objs"]]
    return save(root, rel, w, h, tiles)


HERMIT_GROUNDS = {"Grass": "R2 Seagrass", "Dark Sand": "R2 Kelp Bed", "Light Sand": "R2 Kelp Bed",
                  "Shallow Water": "R2 Tide Pool", "Dark Water": "R2 Deep Tide"}
HERMIT_OBJECTS = {"Tree A": "R2 Coral", "Large Green Bush": "R2 Seaweed", "Yellow Bush 8": "R2 Seaweed",
                  "Grey Pillar": "R2 Barnacle Rock", "Broken Grey Pillar": "R2 Barnacle Rock"}

SKULL_GROUNDS = {"Blue Grass": "R2 Blood Soil", "Castle Stone Floor Tile Dark": "R2 Charred Stone",
                 "Castle Stone Floor Tile": "R2 Bone Dust", "Cracked Purple Stone": "R2 Charred Stone",
                 "Purple Stone": "R2 Bone Dust"}
SKULL_OBJECTS = {"Blue Pillar": "R2 Skull Totem", "Broken Blue Pillar": "R2 Bone Brazier",
                 "Black Tree Stump": "R2 Rib Cage", **{"Dead Bush %d" % i: ("R2 Blood Spike" if i % 2 else "R2 Rib Cage")
                                                       for i in range(1, 9)}}


# the Eye of the Dragon's shrine of moss and cream tiles becomes the Roost's: scorched earth round
# the hoard's floor, its totems and markers as they are
DRAGON_GROUNDS = {"Japanese Moss No Edge": "R2 Ash Ground", "Dragon Tile Cream": "R2 Hoard Floor",
                  "Dragon Tile Cream 2": "R2 Magma Cracks"}


def sphinx_court(root, seed=7):
    """A 33-tile court for the Grand Sphinx, the size of the lands' arenas: his sandstone floor with
    an obelisk at each corner, a ring of sandstone blocks to hide behind, the Tombs' sand round it,
    patches of sinking sand further out and a ragged edge so it melts into the land."""
    rnd = random.Random(seed)
    n = 33
    c = n // 2
    tiles = [{} for _ in range(n * n)]

    def at(x, y):
        return tiles[y * n + x]

    for y in range(n):
        for x in range(n):
            d = math.hypot(x - c, y - c)
            edge = 15 + math.sin(math.atan2(y - c, x - c) * 5 + seed) * 1.2 + rnd.random() * 0.8
            if d > edge:
                continue
            t = at(x, y)
            t["ground"] = "R2 Tomb Sand" if rnd.random() < 0.75 else "R2 Dune Sand"
            if max(abs(x - c), abs(y - c)) <= 6:
                t["ground"] = "R2 Sandstone Tiles"
    for dx, dy in ((-6, -6), (6, -6), (-6, 6), (6, 6)):
        at(c + dx, c + dy)["objs"] = [{"id": "R2 Obelisk"}]
    # cover: eight blocks of two or three in a ring, gaps between
    for i in range(8):
        a = math.pi * 2 * (i + 0.5) / 8
        for s in (-1, 0, 1)[: 2 + (i % 2)]:
            x = int(round(c + math.cos(a) * 10 - math.sin(a) * s))
            y = int(round(c + math.sin(a) * 10 + math.cos(a) * s))
            at(x, y)["objs"] = [{"id": "R2 Sandstone Block"}]
    # sinking sand beyond the cover
    for i in range(6):
        a = math.pi * 2 * (i + rnd.random() * 0.5) / 6
        px, py = c + math.cos(a) * 13, c + math.sin(a) * 13
        r = 1.5 + rnd.random() * 1.2
        for y in range(n):
            for x in range(n):
                if math.hypot(x - px, y - py) <= r and at(x, y).get("ground") and not at(x, y).get("objs"):
                    at(x, y)["ground"] = "R2 Sinking Sand"
    for _ in range(10):
        a, d = rnd.random() * math.pi * 2, 8 + rnd.random() * 6
        x, y = int(round(c + math.cos(a) * d)), int(round(c + math.sin(a) * d))
        t = at(x, y)
        if t.get("ground") in ("R2 Tomb Sand", "R2 Dune Sand") and not t.get("objs"):
            t["objs"] = [{"id": rnd.choice(["R2 Tomb Urn", "R2 Broken Statue"])}]
    return save(root, "Setpieces/Grand Sphinx.jm", n, n, tiles)


def build(root):
    return [
        remap(root, "Setpieces/Hermit God/Spawn.jm", HERMIT_GROUNDS, HERMIT_OBJECTS),
        remap(root, "Setpieces/Hermit God/Death.jm", HERMIT_GROUNDS, HERMIT_OBJECTS),
        remap(root, "Setpieces/Skull Shrine.jm", SKULL_GROUNDS, SKULL_OBJECTS),
        remap(root, "Setpieces/Rock Dragon.jm", DRAGON_GROUNDS, {}),
        sphinx_court(root),
    ]
