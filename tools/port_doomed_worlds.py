"""Dungeons for tools/port_doomed.py.

Reads the Doomed Realms world classes (wserver/realm/worlds/*.cs) and their .wmap maps, converts
the maps to betterSkillys .jm files in source/Shared/resources/worlds/Doomed/, and writes the world
definitions (source/Shared/resources/xml/custom/DoomedRealmsWorlds.xml) that portals point at
through <DungeonName>.

.wmap (Doomed Realms wserver/realm/terrain/Wmap.cs): a version byte, then a zlib stream of
  int16 dictionary size; per entry: int16 ground type, string object id, string object name,
  byte terrain, byte region (v1: + byte elevation); int32 width, int32 height;
  per tile, row by row: int16 dictionary index (v2: + byte elevation).
  Strings are .NET BinaryReader strings (7-bit encoded length, UTF-8), little-endian numbers.
.jm (betterSkillys WorldServer/core/terrain/Json2Wmap.cs): JSON with width, height,
  dict [{ground, objs [{id, name}], regions [{id}]}] and data, the base64 of a zlib stream of
  big-endian int16 dictionary indexes.
"""

import base64
import glob
import json
import math
import make_elder
import os
import re
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORLDS_DIR = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Doomed")
WORLDS_XML = os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealmsWorlds.xml")

# Doomed Realms' TileRegion (wserver/realm/JsonMap.cs), in order
REGIONS = ["None", "Spawn", "Realm_Portals", "Store_1", "Store_2", "Store_3", "Store_4", "Store_5", "Store_6",
           "Store_7", "Store_8", "Store_9", "Vault", "Loot", "Defender", "Hallway", "Hallway_1",
           "Hallway_2", "Hallway_3", "Enemy"]

# Doomed Realms worlds ported as dungeons: world class -> portal ids that lead there.
# (The classic dungeons, nexus, vault, guild halls, market and tutorial are betterSkillys' own.)
PORTED_WORLDS = {
    "ElderPirateCave": ["Elder Pirate Cave Portal"],
    "ElderDraconis": ["Elder Draconis Portal"],
    "ElderAbyss": ["Elder Abyss Portal"],
    "ElderSnakePit": ["Elder Snake Pit Portal"],
    "ElderSpiderDen": ["Elder Spider Den Portal"],
    "ElderSpriteWorld": ["Elder Sprite World Portal"],
    "ElderTombMap": ["Elder Tomb Portal"],
    "ElderUndeadL": ["Elder Undead Lair Portal"],
    "Dcellar": ["Doomed Cellar Portal"],
    "Dragonlord": ["Hash Cellar Portal"],
    "DoomCrystal": ["Doom Crystal Portal"],
    "Island": ["Island Portal"],
    "ZombieMap": ["Zombies Portal"],
    "ZombieMG": ["Zombie Minigame Portal"],
    "Herding": ["Sheep Herding Portal"],
    "ForestMap": ["Forest Sanctuary Portal"],
    "ChristmasOryxsChamber": ["Oryx's Christmas Portal"],
    "PartyCellarMap": ["Birthday Portal"],
    "Kitchen": ["Kitchen Portal"],
    "Secret": ["Secret Portal"],
    "BattleArenaMap": ["Battle Arena Portal"],
    "BattleArenaMap2": ["Free Battle Arena Portal"],
    "Mine": [],
    "GauntletMap": [],
    "TurkeyMap": [],
    "Admin": [],
    "ShopMap": [],
}

# Ported worlds whose map betterSkillys builds itself (a path under worlds/): the world keeps its
# Doomed Realms background and portals, the map is the script's. Oryx's Inferno (Oryx 4):
# tools/build_hash_cellar.py.
OWN_MAPS = {"Dragonlord": "Dungeons/Oryx's Inferno/Oryx's Inferno.jm"}
# Ported worlds renamed for players: Oryx 4's Hash Cellar is Oryx's Inferno.
WORLD_NAMES = {"Dragonlord": "Oryx's Inferno"}

# Elder dungeons Doomed Realms left unfinished (it made their keys, portals and monsters, but no
# maps): built like its finished ones, from the regular dungeon's maps with every object that has
# an "Elder <id>" version swapped for it. world -> (regular world class, name, portal ids)
ELDER_WORLDS = {
    "ElderManor": ("Manor", "Elder Manor", ["Elder Manor Portal"]),
    "ElderOceanTrench": ("OceanTrench", "Elder Ocean Trench", ["Elder Ocean Trench Portal"]),
    "ElderJungle": ("Jungle", "Elder Jungle", ["Elder Jungle Portal"]),
    "ElderBeachZone": ("BeachZone", "Elder Beach Zone", ["Elder Beach Portal"]),
    "ElderDavy": ("Davy", "Elder Davy Jones' Locker", ["Elder Davy Portal"]),
}
# Elder dungeons built from betterSkillys' own map of the regular dungeon instead of Doomed Realms'
# one (tools/make_elder.py lists them and makes their Elder tiles and monsters): each ground,
# wall and monster swapped for its Elder version, braziers lighting the rooms, and a new layout
# for every visit where the regular dungeon has one.
BS_MAPS = os.path.join(ROOT, "source", "Shared", "resources", "worlds")
BS_ELDER_WORLDS = make_elder.DUNGEONS
ELDER_BRAZIER = "Elder Brazier"
# braziers stand by the rooms' walls, corners first, at least this many tiles apart
BRAZIER_SPACING = 9
# the floor a brazier lights becomes the "<ground> Lit <level>" ground (tools/make_elder.py), where
# it can see the tile: level 6 within 2.5 tiles of it, down to level 1 within 10
LIGHT_REACH = [10, 8.5, 7, 5.5, 4, 2.5]
# Doomed Realms' shop map places merchants as objects; betterSkillys places them on store regions
SHOP_WORLD = "ShopMap"


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def take(self, fmt):
        v = struct.unpack_from("<" + fmt, self.data, self.pos)
        self.pos += struct.calcsize("<" + fmt)
        return v[0]

    def string(self):
        n, shift = 0, 0
        while True:
            b = self.data[self.pos]
            self.pos += 1
            n |= (b & 0x7F) << shift
            if not b & 0x80:
                break
            shift += 7
        s = self.data[self.pos:self.pos + n].decode("utf-8")
        self.pos += n
        return s


def read_wmap(path):
    raw = open(path, "rb").read()
    ver = raw[0]
    r = Reader(zlib.decompress(raw[1:]))
    entries = []
    for _ in range(r.take("h")):
        ground = r.take("h")
        obj = r.string()
        name = r.string()
        r.take("B")  # terrain
        region = r.take("B")
        if ver == 1:
            r.take("B")
        entries.append((ground & 0xFFFF, obj, name, region))
    w, h = r.take("i"), r.take("i")
    grid = []
    for _ in range(w * h):
        grid.append(r.take("h"))
        if ver == 2:
            r.take("B")
    return {"width": w, "height": h, "dict": entries, "grid": grid}


def parse_world_classes(dr_root):
    """{class: (display name, background, allow teleport, [wmap file names])}."""
    worlds = {}
    folder = os.path.join(dr_root, "wserver", "realm", "worlds")
    for f in glob.glob(os.path.join(folder, "*.cs")):
        t = open(f, encoding="utf-8-sig", errors="replace").read()
        cls = re.search(r"class (\w+)\s*:\s*World", t)
        if not cls:
            continue
        name = re.search(r'Name\s*=\s*"([^"]+)"', t)
        bg = re.search(r"Background\s*=\s*(\d+)", t)
        tp = re.search(r"AllowTeleport\s*=\s*(true|false)", t)
        maps = []
        for m in re.finditer(r'"wServer\.realm\.worlds\.([^"]+?)"(\s*\+\s*r\.Next\((\d+),\s*(\d+)\s*(\+\s*1)?\))?', t):
            if m.group(2):
                hi = int(m.group(4)) + (1 if m.group(5) else 0)
                maps += ["%s%d.wmap" % (m.group(1), i) for i in range(int(m.group(3)), hi)]
            else:
                maps.append(m.group(1))
        worlds[cls.group(1)] = (name.group(1) if name else cls.group(1), int(bg.group(1)) if bg else 0,
                                tp is None or tp.group(1) == "true", maps)
    return worlds


def find_map(dr_root, name):
    folder = os.path.join(dr_root, "wserver", "realm", "worlds")
    for f in os.listdir(folder):
        if f.lower() == name.lower():
            return os.path.join(folder, f)
    return None


def ground_names(xml_files):
    """{ground type: id} from <Ground> elements."""
    import xml.etree.ElementTree as ET
    out = {}
    for f in xml_files:
        text = re.sub(r"<\?xml[^>]*\?>", "", open(f, encoding="utf-8", errors="replace").read())
        try:
            root = ET.fromstring("<r>" + text + "</r>")
        except ET.ParseError:
            continue
        for g in root.iter("Ground"):
            if g.get("type") and g.get("id"):
                out[int(g.get("type"), 16)] = g.get("id")
    return out


def to_jm(wmap, dr_grounds, object_id):
    """betterSkillys .jm text for a read .wmap; object_id maps a Doomed Realms object id to the ported one."""
    entries = []
    for ground, obj, name, region in wmap["dict"]:
        e = {}
        if ground not in (0xFF, 0xFFFF) and ground in dr_grounds:
            e["ground"] = dr_grounds[ground]
        if obj:
            o = {"id": object_id(obj)}
            if name:
                o["name"] = name
            e["objs"] = [o]
        if 0 < region < len(REGIONS):
            e["regions"] = [{"id": REGIONS[region]}]
        entries.append(e)
    data = b"".join(struct.pack(">h", i) for i in wmap["grid"])
    return json.dumps({"width": wmap["width"], "height": wmap["height"], "dict": entries,
                       "data": base64.b64encode(zlib.compress(data, 9)).decode("ascii")})


def elder_swap(wmap, dr_ids):
    """The map with each object id X replaced by "Elder X" where Doomed Realms defines one."""
    entries = [(g, "Elder " + o if o and "Elder " + o in dr_ids else o, n, r) for (g, o, n, r) in wmap["dict"]]
    return dict(wmap, dict=entries)


def place_braziers(j, brazier, walls, lit_grounds):
    """Puts a brazier by the walls of every room (in its corners first, then along its walls) and
    lights the floor around it."""
    w, h, d = j["width"], j["height"], j["dict"]
    grid = list(struct.unpack(">%dh" % (w * h), zlib.decompress(base64.b64decode(j["data"]))[:2 * w * h]))
    tile = lambda x, y: d[grid[y * w + x]] if 0 <= x < w and 0 <= y < h else {}
    is_wall = lambda x, y: any(o["id"] in walls for o in tile(x, y).get("objs") or [])
    # part of a wall, not a lone pillar
    wall = lambda x, y: is_wall(x, y) and any(is_wall(x + i, y + k) for i, k in ((1, 0), (-1, 0), (0, 1), (0, -1)))

    def floor(x, y):
        t = tile(x, y)
        # (maps made in the editor can hold an object with an empty id, which is no object)
        return t.get("ground") not in (None, "Empty") and not any(o.get("id") for o in t.get("objs") or []) \
            and not t.get("regions")

    cands = []
    for y in range(h):
        for x in range(w):
            if not floor(x, y):
                continue
            n, s_, e, wst = wall(x, y - 1), wall(x, y + 1), wall(x + 1, y), wall(x - 1, y)
            sides = n + s_ + e + wst
            # by one wall or in a corner, not in a passage between two walls, and with room around it
            if sides == 0 or (n and s_) or (e and wst):
                continue
            if sum(floor(x + i, y + k) for i in (-1, 0, 1) for k in (-1, 0, 1)) < 4:
                continue
            cands.append((0 if sides == 2 else 1, y, x))
    placed = []
    for _, y, x in sorted(cands):
        if all(max(abs(x - px), abs(y - py)) >= BRAZIER_SPACING for px, py in placed):
            placed.append((x, y))
    entries = {}
    for x, y in placed:
        i = grid[y * w + x]
        if i not in entries:
            entries[i] = len(d)
            d.append(dict(d[i], objs=[{"id": brazier}]))
        grid[y * w + x] = entries[i]

    # light passes over floor, not through walls or the empty space outside the rooms
    def clear(x, y):
        return tile(x, y).get("ground") not in (None, "Empty") and not is_wall(x, y)

    def sees(x0, y0, x1, y1):
        n = max(abs(x1 - x0), abs(y1 - y0))
        return all(clear(int(round(x0 + (x1 - x0) * k / n)), int(round(y0 + (y1 - y0) * k / n))) for k in range(1, n))

    light = {}
    reach = max(LIGHT_REACH)
    for bx, by in placed:
        for y in range(by - reach, by + reach + 1):
            for x in range(bx - reach, bx + reach + 1):
                level = sum(math.hypot(x - bx, y - by) <= r for r in LIGHT_REACH)
                if level and light.get((x, y), 0) < level and tile(x, y).get("ground") in lit_grounds \
                        and not is_wall(x, y) and sees(bx, by, x, y):
                    light[(x, y)] = level
    lit = {}
    for (x, y), level in light.items():
        i = grid[y * w + x]
        if (i, level) not in lit:
            lit[(i, level)] = len(d)
            d.append(dict(d[i], ground="%s Lit %d" % (d[i]["ground"], level)))
        grid[y * w + x] = lit[(i, level)]
    j["data"] = base64.b64encode(zlib.compress(b"".join(struct.pack(">h", i) for i in grid), 9)).decode("ascii")
    return len(placed)


def bs_elder_map(path, dr_ids):
    """A betterSkillys .jm map with its grounds, walls and monsters swapped for their Elder versions,
    and braziers lighting its rooms."""
    objects, grounds = make_elder.load(make_elder.xml_files(include_elder=True))
    known = set(objects) | set(dr_ids)
    j = json.load(open(os.path.join(BS_MAPS, path), encoding="utf-8-sig"))
    walls = set()
    for e in j["dict"]:
        if e.get("ground") and "Elder " + e["ground"] in grounds:
            e["ground"] = "Elder " + e["ground"]
        for o in e.get("objs") or []:
            if "Elder " + o["id"] in known:
                if o["id"] in objects and objects[o["id"]].findtext("Class") in make_elder.WALL_CLASSES:
                    walls.add("Elder " + o["id"])
                o["id"] = "Elder " + o["id"]
    place_braziers(j, ELDER_BRAZIER, walls, make_elder.lit_grounds())
    objs = [(None, o["id"], o.get("name", ""), 0) for e in j["dict"] for o in e.get("objs") or []]
    return {"width": j["width"], "height": j["height"], "dict": objs, "jm": json.dumps(j)}


def shop_regions(wmap):
    """Merchant objects become Store_1 region tiles (where the shop world puts its merchants)."""
    store = REGIONS.index("Store_1")
    entries = [(g, "", "", store) if o == "Merchant" else (g, o, n, r) for (g, o, n, r) in wmap["dict"]]
    return dict(wmap, dict=entries)


def world_maps(dr_root, dr_ids=frozenset()):
    """{class: (world name, background, allow teleport, [read wmaps])} for PORTED_WORLDS and ELDER_WORLDS."""
    classes = parse_world_classes(dr_root)
    out = {}
    for cls in PORTED_WORLDS:
        name, bg, tp, maps = classes[cls]
        name = WORLD_NAMES.get(cls, name)
        if cls in OWN_MAPS:
            with open(os.path.join(os.path.dirname(WORLDS_DIR), OWN_MAPS[cls]), encoding="utf-8") as f:
                j = json.load(f)
            out[cls] = (name, bg, tp, [{"width": j["width"], "height": j["height"], "own": OWN_MAPS[cls]}])
            continue
        maps = [read_wmap(find_map(dr_root, m)) for m in maps]
        if cls == SHOP_WORLD:
            maps = [shop_regions(m) for m in maps]
        out[cls] = (name, bg, tp, maps)
    for cls, (base, name, _) in ELDER_WORLDS.items():
        _, bg, tp, maps = classes[base]
        out[cls] = (name, bg, tp, [elder_swap(read_wmap(find_map(dr_root, m)), dr_ids) for m in maps])
    # the Elder dungeons built from betterSkillys' maps instead
    for cls, (name, bg, tp, _) in list(out.items()):
        if cls in BS_ELDER_WORLDS:
            out[cls] = (name, bg, tp, [bs_elder_map(BS_ELDER_WORLDS[cls][0], dr_ids)])
    return out


def portal_worlds():
    """{portal id: world class} for the ported worlds."""
    out = {pid: cls for cls, pids in PORTED_WORLDS.items() for pid in pids}
    out.update({pid: cls for cls, (_, _, pids) in ELDER_WORLDS.items() for pid in pids})
    return out


def write_worlds(worlds, dr_grounds, object_id):
    """Writes the .jm maps and DoomedRealmsWorlds.xml; returns {class: world id}."""
    if os.path.isdir(WORLDS_DIR):
        for f in glob.glob(os.path.join(WORLDS_DIR, "*.jm")):
            os.remove(f)
    os.makedirs(WORLDS_DIR, exist_ok=True)
    ids, lines = {}, []
    for cls, (name, bg, tp, maps) in sorted(worlds.items()):
        safe = re.sub(r"[^A-Za-z0-9 '\-]", "", name).strip() or cls
        files = []
        for i, wm in enumerate(maps):
            if "own" in wm:
                files.append(wm["own"])
                continue
            fname = safe + (" %d" % (i + 1) if len(maps) > 1 else "") + ".jm"
            with open(os.path.join(WORLDS_DIR, fname), "w", encoding="utf-8") as f:
                f.write(wm["jm"] if "jm" in wm else to_jm(wm, dr_grounds, object_id))
            files.append("Doomed/" + fname)
        ids[cls] = name
        w = max(m["width"] for m in maps)
        h = max(m["height"] for m in maps)
        lines.append('\t<World id="%s">' % name.replace("&", "&amp;").replace('"', "&quot;"))
        lines.append("\t\t<Width>%d</Width>" % w)
        lines.append("\t\t<Height>%d</Height>" % h)
        lines += ["\t\t<MapJM>%s</MapJM>" % f.replace("&", "&amp;") for f in files]
        lines.append("\t\t<VisibilityType>1</VisibilityType>")
        if cls in BS_ELDER_WORLDS:
            objects, _ = make_elder.load(make_elder.xml_files(include_elder=True))
            lines += ["\t\t" + x for x in make_elder.random_layout(BS_ELDER_WORLDS[cls][0], set(objects)) or []]
        if not tp:
            lines.append("\t\t<DisableTeleport/>")
        lines.append("\t\t<Dungeon/>")
        lines.append("\t</World>")
    with open(WORLDS_XML, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("<!-- Generated by tools/port_doomed.py (port_doomed_worlds.py) from the Doomed Realms worlds. Do not edit. -->\n")
        f.write("<Worlds>\n" + "\n".join(lines) + "\n</Worlds>\n")
    return ids
