"""Builds the Elder dungeons: their darker look, the Elder monsters, and their maps.

    python tools/make_elder.py                     # the Elder tiles, monsters and sheet
    python tools/make_elder.py map <WorldClass> ["<file>.jm"]   # one Elder dungeon's map

Each Elder dungeon in DUNGEONS is built from the game's own map of the regular dungeon:

- Tiles: every ground and wall the regular map uses gets an "Elder <id>" copy in a darker shade
  (the floor dark stone, the walls aged gold), drawn on the client's ElderAssets8x8.png. Each
  walkable ground also gets "Elder <id> Lit 1" to "Lit <LIGHT_LEVELS>", the floor around a brazier.
- Monsters: every monster the regular map places, or the regular dungeon's behaviours define,
  that has no Elder version yet gets an "Elder <id>" copy with the Elder stats (rebalance_elders).
  They share the regular monster's sprite and behaviour.
- Spawners: other objects the map places and the behaviours define (Snake Grate) get an
  "Elder <id>" copy, so the Elder version's behaviour spawns Elder monsters.
- Maps (`map`): the regular map with every ground, wall and monster swapped for its Elder version
  and braziers lighting its rooms, written to source/Shared/resources/worlds/<MAPS_DIR>/. The
  world's entry (in custom/DoomedRealmsWorlds.xml) is edited by hand; `map` prints the
  <RandomLayout> to put in it when the regular dungeon has one.

Outputs: source/Shared/resources/xml/custom/ElderGrounds.xml and ElderObjects.xml, their client
copies, and the sheet. Type codes are kept stable in tools/elder/types.json.
"""

import base64
import copy
import glob
import json
import math
import os
import re
import struct
import xml.etree.ElementTree as ET
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
MAPS = os.path.join(ROOT, "source", "Shared", "resources", "worlds")
BEHAVIOURS = os.path.join(ROOT, "source", "WorldServer", "logic", "db")
CLIENT = os.path.join(ROOT, "client", "src")
IMAGES = os.path.join(CLIENT, "kabam", "rotmg", "assets", "prod", "images")
SHEET = os.path.join(CLIENT, "kabam", "rotmg", "assets", "custom", "images", "ElderAssets8x8.png")
SHEET_NAME = "elderAssets8x8"
OUTPUTS = {
    "grounds": [os.path.join(XML_DIR, "custom", "ElderGrounds.xml"),
                os.path.join(CLIENT, "kabam", "rotmg", "assets", "custom", "xmls", "ElderGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "ElderObjects.xml"),
                os.path.join(CLIENT, "kabam", "rotmg", "assets", "custom", "xmls", "ElderObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "elder", "types.json")
GROUND_TYPES = (0xE800, 0xF000)
OBJECT_TYPES = (0xF000, 0xF800)

# A sprite's shade scales it towards a target brightness (the Tomb's: dark stone floors, aged gold
# walls) within limits, so dark dungeons stay readable, then tints it. A floor lit by a brazier is
# up to LIGHT_GAIN brighter and warmer.
FLOOR_TARGET, FLOOR_RANGE, FLOOR_TINT = 69.0, (0.4, 0.75), (1.0, 0.9, 1.05)
WALL_TARGET, WALL_RANGE, WALL_TINT = 150.0, (0.5, 0.9), (1.0, 0.8, 0.56)
LIGHT_GAIN, LIGHT_TINT = 0.45, (1.0, 0.81, 0.724)
LIGHT_LEVELS = 6
FLOOR = ("floor", 0)
WALL = ("wall", 0)
LIGHT = [("floor", n) for n in range(1, LIGHT_LEVELS + 1)]


def multipliers(shade, brightness):
    """The red, green and blue multipliers of a shade for a sprite of this average brightness."""
    kind, level = shade
    target, (lo, hi), tint = (FLOOR_TARGET, FLOOR_RANGE, FLOOR_TINT) if kind == "floor" else (WALL_TARGET, WALL_RANGE, WALL_TINT)
    m = min(hi, max(lo, target / brightness)) if brightness > 0 else lo
    if level:
        f = level / LIGHT_LEVELS
        m *= 1 + LIGHT_GAIN * f
        tint = tuple(t + (lt - t) * f for t, lt in zip(tint, LIGHT_TINT))
    return tuple(m * t for t in tint)
WALL_CLASSES = ("Wall", "ConnectedWall", "CaveWall")

# Elder world class -> (regular map, regular behaviour file). An Elder dungeon is laid out anew for
# every visit if its regular one is (its <RandomLayout>, with each goal swapped for its Elder
# version: random_layout).
DUNGEONS = {
    "ElderTombMap": ("Dungeons/Tomb of the Ancients.jm", "BehaviorDb.Tomb.cs"),
    "ElderPirateCave": ("Dungeons/Pirate Cave.jm", "BehaviorDb.PirateCave.cs"),
    "ElderSpiderDen": ("Dungeons/Spider Den.jm", "BehaviorDb.SpiderDen.cs"),
    "ElderSnakePit": ("Dungeons/Snake Pit.jm", "BehaviorDb.SnakePit.cs"),
    "ElderUndeadL": ("Dungeons/Undead Lair.jm", "BehaviorDb.UndeadLair.cs"),
    "ElderAbyss": ("Dungeons/Abyss of Demons.jm", "BehaviorDb.Abyss.cs"),
    "ElderOceanTrench": ("Dungeons/Ocean Trench.jm", "BehaviorDb.OceanTrench.cs"),
    "ElderBeachZone": ("Dungeons/Beachzone.jm", "BehaviorDb.Beachzone.cs"),
    "ElderJungle": ("Dungeons/Forbidden Jungle.jm", "BehaviorDb.ForbiddenJungle.cs"),
    "ElderManor": ("Dungeons/Manor of the Immortals.jm", "BehaviorDb.Manor.cs"),
    "ElderSpriteWorld": ("Dungeons/Sprite World.jm", "BehaviorDb.SpriteWorld.cs"),
    "ElderDavy": ("Dungeons/Davy Jones' Locker.jm", "BehaviorDb.DavyJones.cs"),
    "ElderDraconis": ("Dungeons/Lair of Draconis.jm", "BehaviorDb.Draconis.cs"),
    "ElderIvoryWyvern": ("Dungeons/Ivory Wyvern.jm", "BehaviorDb.Wyvern.cs"),
    "ElderShatters": ("Dungeons/The Shatters.jm", "BehaviorDb.Shatters.cs"),
    # the Lost Halls and the two worlds it leads to (several behaviour files: "a+b")
    "ElderLostHalls": ("Dungeons/Lost Halls.jm", "BehaviorDb.LostHallsRooms.cs+BehaviorDb.LostHalls.cs"),
    "ElderCultistHideout": ("Dungeons/Cultist Hideout.jm", "BehaviorDb.Cult.cs"),
    "ElderVoid": ("Dungeons/The Void/The Void.jm", "BehaviorDb.Void.cs"),
    # the Haunted Cemetery's four worlds (the first one's gate spawners serve all three arenas)
    "ElderCemetery": ("Dungeons/Haunted Cemetery.jm", "BehaviorDb.HauntedCemetery.cs"),
    "ElderCemeteryGates": ("Dungeons/Haunted Cemetery Gates.jm", "BehaviorDb.HauntedCemeteryGates.cs+BehaviorDb.HauntedCemetery.cs"),
    "ElderCemeteryGraves": ("Dungeons/Haunted Cemetery Graves.jm", "BehaviorDb.HauntedCemeteryGraves.cs+BehaviorDb.HauntedCemetery.cs"),
    "ElderCemeteryFinalBattle": ("Dungeons/Haunted Cemetery Final Battle.jm", "BehaviorDb.HauntedCemeteryFinalBattle.cs"),
    # the Ice Cave and the Inner Sanctum it opens, where Esben waits (one behaviour file for both)
    "ElderIceCave": ("Dungeons/Ice Cave.jm", "BehaviorDb.IceCave.cs"),
    "ElderInnerSanctum": ("Dungeons/The Inner Sanctum.jm", "BehaviorDb.IceCave.cs"),
}
# Dungeons run by hidden objects that watch each other by name (the Shatters' switches, gates,
# bridges, timers and chest spawners): every object their behaviours define gets an Elder copy,
# the gates the behaviours open and the helpers they only spawn included, so the Elder version's
# objects watch the Elder ones.
SCRIPTED_DUNGEONS = {"ElderShatters", "ElderLostHalls", "ElderCultistHideout", "ElderVoid", "ElderCemetery",
                     "ElderCemeteryGates", "ElderCemeteryGraves", "ElderCemeteryFinalBattle"}
# where the Elder maps go, under source/Shared/resources/worlds
MAPS_DIR = "Doomed"

ELDER_HP = 2.0
# Elder bosses whose health isn't twice the regular one's: the Void Entity's long fight (four
# phases, two splits and the Singularity) was a slog at 800k, nearly three times the Elder Colossus
ELDER_HP_FIXED = {"Elder LH Void Entity": 500000}
ELDER_DEFENSE = 10      # added
ELDER_DAMAGE = 1.3
ELDER_XP = 2            # <XpBonus>: the experience (and so fame) of a kill, and its per-kill cap
# regular bosses that are no quest, whose Elder versions are (a Necromantic Relic raises them, and
# a raised boss is a quest: the arrow, and its health at the top of the screen)
ELDER_QUESTS = {"Arena Ghost Bride", "Arena Grave Caretaker", "Ghost of Skuld"}


def rebalance_elders(dr, bs):
    """Elder monsters' health, defense, projectile damage and quest flags, from their regular version's,
    and their experience bonus."""
    def number(o, tag):
        e = o.find(tag)
        try:
            return float(e.text) if e is not None and e.text else None
        except ValueError:
            return None

    def projectile_damage(o):
        out = {}
        for p in o.findall("Projectile"):
            out[p.get("id")] = {t: number(p, t) for t in ("Damage", "MinDamage", "MaxDamage") if number(p, t) is not None}
        return out

    changed = 0
    for oid, o in dr.items():
        regular = bs.get(oid[len("Elder "):]) if oid.startswith("Elder ") else None
        if regular is None and oid.startswith("Elder "):
            regular = dr.get(oid[len("Elder "):])
        if regular is None or o.find("Enemy") is None:
            continue
        hp = number(regular, "MaxHitPoints")
        if hp and o.find("MaxHitPoints") is not None:
            o.find("MaxHitPoints").text = str(ELDER_HP_FIXED.get(oid, int(round(hp * ELDER_HP))))
        defense = number(regular, "Defense")
        if o.find("Defense") is not None:
            o.find("Defense").text = str(int(round((defense or 0) + ELDER_DEFENSE)))
        # a quest where the regular one is (the quest arrow leads to it)
        for tag in ("Quest", "Level"):
            if regular.find(tag) is not None and o.find(tag) is None:
                ET.SubElement(o, tag).text = regular.find(tag).text
        if oid[len("Elder "):] in ELDER_QUESTS and o.find("Quest") is None:
            ET.SubElement(o, "Quest")
        bonus = o.find("XpBonus") if o.find("XpBonus") is not None else ET.SubElement(o, "XpBonus")
        bonus.text = str(ELDER_XP)
        # projectiles: no id twice (Elder Septavius had two 0s), and every one of the regular
        # monster's, since an Elder monster can fight with the regular behaviour (Elder Lord Ruthven
        # lacked the Cross Bullet)
        ids = set()
        for p in list(o.findall("Projectile")):
            if p.get("id") in ids:
                o.remove(p)
            ids.add(p.get("id"))
        for p in regular.findall("Projectile"):
            if p.get("id") not in ids:
                o.append(copy.deepcopy(p))
                ids.add(p.get("id"))
        base = projectile_damage(regular)
        for p in o.findall("Projectile"):
            reg = base.get(p.get("id"))
            if not reg:
                continue
            # one damage for tags the regular projectile doesn't have (Damage vs MinDamage/MaxDamage)
            avg = reg.get("Damage") or (reg.get("MinDamage", 0) + reg.get("MaxDamage", reg.get("MinDamage", 0))) / 2
            for tag in ("Damage", "MinDamage", "MaxDamage"):
                if p.find(tag) is not None:
                    p.find(tag).text = str(int(round(reg.get(tag, avg) * ELDER_DAMAGE)))
        changed += 1
    return changed
ELDER_BRAZIER = "Elder Brazier"
# braziers stand by the rooms' walls, corners first, at least this many tiles apart
BRAZIER_SPACING = 9
# the floor a brazier lights becomes the "<ground> Lit <level>" ground (tools/make_elder.py), where
# it can see the tile: level 6 within 2.5 tiles of it, down to level 1 within 10
LIGHT_REACH = [10, 8.5, 7, 5.5, 4, 2.5]


def load(files):
    objects, grounds = {}, {}
    for f in sorted(files):
        text = re.sub(r"<\?xml[^>]*\?>", "", open(f, encoding="utf-8-sig", errors="replace").read())
        try:
            root = ET.fromstring("<r>" + text + "</r>")
        except ET.ParseError:
            continue
        for o in root.iter("Object"):
            if o.get("id"):
                objects[o.get("id")] = o
        for g in root.iter("Ground"):
            if g.get("id"):
                grounds[g.get("id")] = g
    return objects, grounds


def xml_files(include_elder=False):
    out = []
    for f in glob.glob(os.path.join(XML_DIR, "**", "*.xml"), recursive=True):
        if not include_elder and os.path.abspath(f) in {os.path.abspath(p) for ps in OUTPUTS.values() for p in ps}:
            continue
        out.append(f)
    return out


def map_ids(path):
    """The grounds and objects a .jm map uses."""
    j = json.load(open(os.path.join(MAPS, path), encoding="utf-8-sig"))
    grounds = {e["ground"] for e in j["dict"] if e.get("ground")}
    objects = {o["id"] for e in j["dict"] for o in e.get("objs") or []}
    return grounds, objects


def behaviour_ids(name):
    """The monsters a behaviour file (or several, "a+b") defines or refers to by id."""
    text = "\n".join(open(os.path.join(BEHAVIOURS, n), encoding="utf-8-sig").read() for n in name.split("+"))
    return set(re.findall(r'"([^"\n]+)"', text))


def defined_ids(name):
    """The objects a dungeon's shared builder (BehaviorDb.Elder.cs) gives a behaviour."""
    text = "\n".join(open(os.path.join(BEHAVIOURS, n), encoding="utf-8-sig").read() for n in name.split("+"))
    return set(re.findall(r'\.Init\(E\("([^"\n]+)"\)', text))


def sheet_sizes():
    """{sprite sheet: sprite width} from the client's AssetLoader."""
    text = open(os.path.join(CLIENT, "com", "company", "assembleegameclient", "util", "AssetLoader.as"), encoding="utf-8").read()
    return {m.group(1): int(m.group(2)) for m in re.finditer(r'addImageSet\("([^"]+)",[^;]*?,\s*(\d+)\s*,\s*\d+\s*\)', text)}


# ------------------------------------------------------------------------------------------------
# PNG (8-bit RGB/RGBA/palette/grey)

def read_png(path):
    data = open(path, "rb").read()
    pos, chunks = 8, []
    while pos < len(data):
        n, t = struct.unpack(">I4s", data[pos:pos + 8])
        chunks.append((t, data[pos + 8:pos + 8 + n]))
        pos += 12 + n
    w, h, depth, ctype = struct.unpack(">IIBB", next(d for t, d in chunks if t == b"IHDR")[:10])
    if depth != 8:
        raise ValueError("%s: only 8-bit images are supported" % path)
    plte = next((d for t, d in chunks if t == b"PLTE"), b"")
    trns = next((d for t, d in chunks if t == b"tRNS"), b"")
    raw = zlib.decompress(b"".join(d for t, d in chunks if t == b"IDAT"))
    bpp = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    stride = w * bpp
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(h):
        f, line = raw[i], bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line

    def px(x, y):
        r = rows[y]
        if ctype == 6:
            return tuple(r[x * 4:x * 4 + 4])
        if ctype == 2:
            return tuple(r[x * 3:x * 3 + 3]) + (255,)
        if ctype == 3:
            k = r[x]
            return tuple(plte[k * 3:k * 3 + 3]) + ((trns[k] if k < len(trns) else 255),)
        if ctype == 0:
            return (r[x],) * 3 + (255,)
        return (r[x * 2],) * 3 + (r[x * 2 + 1],)
    return w, h, px


def write_png(path, w, h, pix):
    raw = b"".join(b"\x00" + bytes(c for x in range(w) for c in pix[y][x]) for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


class Sheet:
    """The Elder sprite sheet: shaded 8x8 copies of betterSkillys sprites, 16 to a row."""

    def __init__(self, sizes):
        self.sizes, self.sprites, self.index = sizes, [], {}

    def add(self, sheet, index, shade):
        key = (sheet, index, shade)
        if key not in self.index:
            self.index[key] = len(self.sprites)
            self.sprites.append(key)
        return self.index[key]

    def retexture(self, element, shade):
        """Points every 8x8 texture of the element at a shaded copy on this sheet."""
        for node in element.iter():
            f, i = node.find("File"), node.find("Index")
            if f is None or i is None or self.sizes.get(f.text) != 8:
                continue
            i.text = "0x%02x" % self.add(f.text, int(i.text, 0), shade)
            f.text = SHEET_NAME

    def write(self):
        sources = {}
        rows = max(1, (len(self.sprites) + 15) // 16)
        w, h = 128, max(128, rows * 8)
        pix = [[(0, 0, 0, 0)] * w for _ in range(h)]
        for n, (sheet, index, shade) in enumerate(self.sprites):
            if sheet not in sources:
                sources[sheet] = read_png(os.path.join(IMAGES, sheet + ".png"))
            sw, _, px = sources[sheet]
            sx, sy = (index % (sw // 8)) * 8, (index // (sw // 8)) * 8
            dx, dy = (n % 16) * 8, (n // 16) * 8
            seen = [px(sx + x, sy + y) for y in range(8) for x in range(8)]
            seen = [p for p in seen if p[3] > 0]
            brightness = sum(0.299 * r + 0.587 * g + 0.114 * b for r, g, b, _ in seen) / max(1, len(seen))
            mr, mg, mb = multipliers(shade, brightness)
            for y in range(8):
                for x in range(8):
                    r, g, b, a = px(sx + x, sy + y)
                    pix[dy + y][dx + x] = (min(255, int(r * mr)), min(255, int(g * mg)), min(255, int(b * mb)), a)
        write_png(SHEET, w, h, pix)


# ------------------------------------------------------------------------------------------------

class Types:
    """Type codes kept stable between runs."""

    def __init__(self):
        self.types = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, key, span):
        if key not in self.types:
            used = {int(v, 16) for v in self.types.values()}
            self.types[key] = "0x%04X" % next(t for t in range(*span) if t not in used)
        return self.types[key]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w") as f:
            json.dump(self.types, f, indent=1, sort_keys=True)


def write_xml(paths, root_tag, elements, comment):
    root = ET.Element(root_tag)
    for e in elements:
        root.append(e)
    ET.indent(root, "\t")
    text = "<!-- %s -->\n%s\n" % (comment, ET.tostring(root, encoding="unicode"))
    for p in paths:
        with open(p, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(text)
    # items keep the looks players drew for them (tools/community_sprites.py)
    import community_sprites
    for p in paths:
        community_sprites.rework_file(p)


def build():
    """Writes the Elder tiles and monsters."""
    objects, grounds = load(xml_files())
    sheet = Sheet(sheet_sizes())
    types = Types()
    new_grounds, new_objects = [], []
    made_grounds, made_objects = set(), set()
    scripted = set()
    for f in glob.glob(os.path.join(BEHAVIOURS, "*.cs")):
        if "Doomed" not in os.path.basename(f):
            scripted |= set(re.findall(r'"([^"\n]+)"', open(f, encoding="utf-8-sig").read()))

    def elder_ground(gid, shade, name):
        if name in made_grounds or gid not in grounds:
            return
        g = copy.deepcopy(grounds[gid])
        g.set("id", name)
        g.set("type", types.get("ground:" + name, GROUND_TYPES))
        sheet.retexture(g, shade)
        new_grounds.append(g)
        made_grounds.add(name)

    # the server matches object ids whatever their case (a map's "lod Ivory Wyvern" is the XML's
    # "LOD Ivory Wyvern"); the Elder copy is named as the map and behaviours spell it
    by_lower = {k.lower(): v for k, v in objects.items()}
    for cls, (path, behaviours) in sorted(DUNGEONS.items()):
        map_grounds, map_objects = map_ids(path)
        for gid in sorted(map_grounds):
            if gid == "Empty" or gid.startswith("Elder "):
                continue
            elder_ground(gid, FLOOR, "Elder " + gid)
            if gid in grounds and grounds[gid].find("NoWalk") is None:
                for n, shade in enumerate(LIGHT, 1):
                    elder_ground(gid, shade, "Elder %s Lit %d" % (gid, n))
        monsters = set()
        defined = defined_ids(behaviours)
        all_defined = cls in SCRIPTED_DUNGEONS
        for oid in sorted(map_objects | (defined if all_defined else behaviour_ids(behaviours))):
            o = objects.get(oid, by_lower.get(oid.lower()))
            if o is None or oid.startswith("Elder "):
                continue
            name = "Elder " + oid
            if o.findtext("Class") in WALL_CLASSES and oid in map_objects:
                # walls a behaviour opens or breaks by name (Davy's doors) stay as they are
                if name in made_objects or name in objects or (oid in scripted and not (all_defined and oid in defined)):
                    continue
                w = copy.deepcopy(o)
                w.set("id", name)
                w.set("type", types.get(name, OBJECT_TYPES))
                sheet.retexture(w, WALL)
                new_objects.append(w)
                made_objects.add(name)
            elif o.find("Enemy") is not None and name not in objects and name not in made_objects:
                monsters.add(oid)
            elif (oid in map_objects or all_defined) and oid in defined and name not in objects and name not in made_objects:
                # a spawner the map places (Snake Grate): its Elder copy spawns the Elder monsters
                g = copy.deepcopy(o)
                g.set("id", name)
                g.set("type", types.get(name, OBJECT_TYPES))
                if g.find("DrawOnGround") is not None:
                    sheet.retexture(g, FLOOR)
                new_objects.append(g)
                made_objects.add(name)
        copies = {}
        for oid in sorted(monsters):
            m = copy.deepcopy(objects.get(oid, by_lower.get(oid.lower())))
            m.set("id", "Elder " + oid)
            m.set("type", types.get("Elder " + oid, OBJECT_TYPES))
            display = m.find("DisplayId")
            if display is None:
                display = ET.SubElement(m, "DisplayId")
                display.text = oid
            # ("The Forgotten King" is the Elder Forgotten King)
            display.text = "Elder " + re.sub(r"^The ", "", display.text)
            copies["Elder " + oid] = m
        # (the regular monsters under the names the copies use)
        rebalance_elders(copies, dict(objects, **{oid: objects.get(oid, by_lower.get(oid.lower())) for oid in monsters}))
        for name, m in sorted(copies.items()):
            new_objects.append(m)
            made_objects.add(name)

    write_xml(OUTPUTS["grounds"], "GroundTypes", new_grounds,
              "Generated by tools/make_elder.py: the Elder dungeons' darker grounds. Do not edit.")
    write_xml(OUTPUTS["objects"], "Objects", new_objects,
              "Generated by tools/make_elder.py: the Elder dungeons' walls and monsters. Do not edit.")
    sheet.write()
    types.save()
    return len(new_grounds), len(new_objects), len(sheet.sprites)


def random_layout(path, known):
    """The <RandomLayout> lines of the regular world whose map is path, with each goal swapped for
    its Elder version (known: the ids that exist), or None."""
    for f in glob.glob(os.path.join(XML_DIR, "*.xml")):
        text = re.sub(r"<\?xml[^>]*\?>", "", open(f, encoding="utf-8-sig", errors="replace").read())
        try:
            root = ET.fromstring("<r>" + text + "</r>")
        except ET.ParseError:
            continue
        for w in root.iter("World"):
            if path not in [m.text for m in w.findall("MapJM")] or w.find("RandomLayout") is None:
                continue
            rl = w.find("RandomLayout")
            attrs = "".join(' %s="%s"' % kv for kv in sorted(rl.attrib.items()))
            goals = ["Elder " + g.text if "Elder " + g.text in known else g.text for g in rl.findall("Goal")]
            return ["<RandomLayout%s>" % attrs] + ["\t<Goal>%s</Goal>" % g for g in goals] + ["</RandomLayout>"]
    return None


def lit_grounds():
    """The Elder grounds that have lit versions."""
    _, grounds = load(OUTPUTS["grounds"][:1])
    return {gid for gid in grounds if gid.startswith("Elder ") and gid + " Lit 1" in grounds}


def place_braziers(j, brazier, walls, lit_grounds, avoid=frozenset()):
    """Puts a brazier by the walls of every room (in its corners first, then along its walls) and
    lights the floor around it. None stands on a ground in avoid."""
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

    stands = lambda x, y: floor(x, y) and tile(x, y).get("ground") not in avoid

    cands = []
    for y in range(h):
        for x in range(w):
            if not stands(x, y):
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

def elder_map(path, dry_braziers=False):
    """A .jm map with its grounds, walls and monsters swapped for their Elder versions, and braziers
    lighting its rooms. With dry_braziers none stands in lava or over a void (the Shatters has
    both along its walls)."""
    objects, grounds = load(xml_files(include_elder=True))
    j = json.load(open(os.path.join(MAPS, path), encoding="utf-8-sig"))
    walls = set()
    for e in j["dict"]:
        if e.get("ground") and "Elder " + e["ground"] in grounds:
            e["ground"] = "Elder " + e["ground"]
        for o in e.get("objs") or []:
            if "Elder " + o["id"] in objects:
                if o["id"] in objects and objects[o["id"]].findtext("Class") in WALL_CLASSES:
                    walls.add("Elder " + o["id"])
                o["id"] = "Elder " + o["id"]
    # (nor on the bridges, which open and shut)
    avoid = {gid for gid, g in grounds.items() if g.find("NoWalk") is not None or g.find("MinDamage") is not None
             or gid.endswith(" Bridge")} if dry_braziers else frozenset()
    place_braziers(j, ELDER_BRAZIER, walls, lit_grounds(), avoid)
    return j


if __name__ == "__main__":
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    if len(sys.argv) in (3, 4) and sys.argv[1] == "map":
        cls = sys.argv[2]
        if cls not in DUNGEONS:
            sys.exit("Not in DUNGEONS: %s (%s)" % (cls, ", ".join(sorted(DUNGEONS))))
        path = DUNGEONS[cls][0]
        j = elder_map(path, cls in SCRIPTED_DUNGEONS)
        objects, _ = load(xml_files(include_elder=True))
        name = sys.argv[3] if len(sys.argv) == 4 else "Elder " + os.path.basename(path)
        out = os.path.join(MAPS, MAPS_DIR, name)
        with open(out, "w", encoding="utf-8") as f:
            json.dump(j, f)
        print("Wrote %s (%dx%d). Name it in the world's <MapJM>." % (os.path.relpath(out, ROOT), j["width"], j["height"]))
        layout = random_layout(path, set(objects))
        if layout:
            print("Its <RandomLayout>:\n" + "\n".join(layout))
    else:
        print("Wrote %d Elder grounds, %d Elder objects, %d sprites." % build())
