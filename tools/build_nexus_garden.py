"""Draws the Nexus garden's art and writes its game data: the grounds, scenery, 3D models, the
realm portals sunk into the floor, the villagers and the bees.

    python tools/build_nexus_garden.py [--preview <png>]

The map is laid out by tools/build_nexus.py from what this makes. The villagers and bees are moved
by the world server (NexusVillagers); the portals are placed by RealmPortalMonitor.

Outputs (generated, don't edit):
- NexusGardenGrounds.xml and NexusGardenObjects.xml, in source/Shared/resources/xml/custom and
  the client's custom/xmls;
- the sheets NexusGarden8x8.png, NexusGarden16x16.png, NexusGarden32x32.png, NexusGarden48x48.png
  (grounds and scenery, the cavern and the vortex), NexusGardenModels64x64.png (the models'
  textures) and NexusGardenChars8x8.png (villagers and bees) in the client's custom/images, and
  the models' OBJ files in its custom/objects, all registered for the Rust client by
  tools/gen_rs_manifest.py.
Type codes are kept stable in tools/nexus_garden/types.json. Needs Pillow.
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from nexus_garden import chars, extras, floors, grounds, models, sprites  # noqa: E402
from realm2 import art as realm2_art  # noqa: E402
from nexus_garden.px import hexc as C  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom")
IMAGES = os.path.join(CLIENT, "images")
MODELS_DIR = os.path.join(CLIENT, "objects")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "NexusGardenGrounds.xml"), os.path.join(CLIENT, "xmls", "NexusGardenGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "NexusGardenObjects.xml"), os.path.join(CLIENT, "xmls", "NexusGardenObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "nexus_garden", "types.json")
GROUND_TYPES = (0x9B00, 0x9C00)
OBJECT_TYPES = (0x9A00, 0x9B00)
SHEETS = {
    8: ("nexusGarden8x8", "NexusGarden8x8.png"),
    16: ("nexusGarden16x16", "NexusGarden16x16.png"),
    32: ("nexusGarden32x32", "NexusGarden32x32.png"),
    48: ("nexusGarden48x48", "NexusGarden48x48.png"),
    64: ("nexusGarden64x64", "NexusGarden64x64.png"),
    192: ("nexusGarden192x192", "NexusGarden192x192.png"),
}
MODEL_SHEET = ("nexusGardenModels128x128", "NexusGardenModels128x128.png")
CHARS = ("nexusGardenChars8x8", "NexusGardenChars8x8.png")
CHARS16 = ("nexusGardenChars16x16", "NexusGardenChars16x16.png")
COLS = 16

# Realm 2's eleven lands, round its vortex's ring as round its standing portal's arch
LANDS = ["cube", "penta", "lost", "hermit", "ship", "sphinx", "skull", "garden", "dragon", "kingdom", "bees"]

# The ids the world server and the map builder use
REALM_PORTAL = "NG Realm Cavern"        # RealmPortalMonitor: a realm's portal in the Nexus
REALM2_PORTAL = "NG Realm2 Vortex"      # and Realm 2's
BASIN = "NG Garden Basin"               # NexusWorld: heals whoever stands by it
BLOCKER = "NG Blocker"                  # invisible: fills a building's tiles round its model
ARENA_PORTAL = "NG Arena Portal"        # DoomedWorlds.ArenaPortal: the Arena's, in the shrine
DUCK = "NG XP Duck"                     # NexusGuide: the XP gifts, swimming round the island
SMITH = "NG Smith God"                  # NexusVillagers: speaks by the forge
NAME_CHANGER, CHARACTER_CHANGER = "NG Name Changer", "NG Character Changer"   # NexusVillagers
TWINS = list(extras.TWINS)              # NexusVillagers: about the forest
SMOKE = "NG Chimney Smoke"              # over the smithy's chimney (models.SMITHY_CHIMNEY)
# invisible: what players use where the 3D models show it (the smithy's furnace and altar, the
# Guild Hall's door), each of the class the client and the server know it by
UNSEEN = {
    "NG Divine Forge": ("Divine Forge", "Forge", True, {}),
    "NG Enchanter": ("Enchanter", "Enchanter", True, {}),
    "NG Guild Hall Door": ("Guild Hall", "GuildHallPortal", False, {"IntergamePortal": None, "DungeonName": "Guild Hall"}),
}
# the NPCs who walk about the market (NexusVillagers, QuestGivers, MarketBroker): their own
# sprites (QuestNpcs16x16) walking, their classes as before
WALKING_NPCS = [("NG Tinkerer", "The Tinkerer", "QuestMaster", 0), ("NG Chronicler", "The Chronicler", "Chronicler", 2),
                ("NG Market Broker", "Market Broker", "MarketNPC", 4)]


class Types:
    def __init__(self):
        self.data = json.load(open(TYPES)) if os.path.exists(TYPES) else {"grounds": {}, "objects": {}}

    def get(self, kind, name):
        table = self.data[kind]
        if name not in table:
            lo, hi = GROUND_TYPES if kind == "grounds" else OBJECT_TYPES
            used = set(int(v, 16) for v in table.values())
            table[name] = "0x%04X" % next(t for t in range(lo, hi) if t not in used)
        return table[name]

    def save(self):
        with open(TYPES, "w", newline="\n") as f:
            json.dump(self.data, f, indent=1, sort_keys=True)
            f.write("\n")


class Sheet:
    def __init__(self, size, cols=COLS):
        self.w = self.h = size
        self.cols, self.cells = cols, []

    def add(self, img):
        if img.size != (self.w, self.h):
            raise ValueError("a %dx%d cell on the %d sheet" % (img.width, img.height, self.w))
        self.cells.append(img)
        return len(self.cells) - 1

    def save(self, path):
        rows = max(1, (len(self.cells) + self.cols - 1) // self.cols)
        out = Image.new("RGBA", (self.cols * self.w, rows * self.h), (0, 0, 0, 0))
        for i, img in enumerate(self.cells):
            out.paste(img, ((i % self.cols) * self.w, (i // self.cols) * self.h))
        out.save(path)
        return out


def sub(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, attrs)
    if text is not None:
        e.text = str(text)
    return e


def texture(parent, file, index, tag="Texture"):
    t = sub(parent, tag)
    sub(t, "File", file)
    sub(t, "Index", "0x%02x" % index)
    return t


# --- scenery: name -> (cell size, images, kind, extra) -------------------------------------------
# kind: "block" stands in the way, "plain" is walked through, "flat" lies on the ground. Several
# images are picked from at random (`RandomTexture`); with `anim` they're frames instead.

def scenery():
    s = sprites
    blossom_flowers = [s.BLOSSOM[5], s.WHITE[3], s.BLOSSOM[4]]
    return {
        "NG Sakura Tree": (32, [s.sakura(k) for k in (11, 12, 13, 14)], "block", {"shadow": 140}),
        "NG Weeping Sakura": (32, [s.sakura(k, weeping=True) for k in (21, 22)], "block", {"shadow": 140}),
        "NG Black Pine": (32, [s.pine(k) for k in (31, 32)], "block", {"shadow": 130}),
        "NG Maple": (32, [s.maple(k) for k in (41, 42)], "block", {"shadow": 130}),
        "NG Great Sakura": (48, [s.sakura(51, size=48)], "block", {"shadow": 200}),
        "NG Young Sakura": (16, [s.young_sakura(k) for k in (1, 2, 3)], "block", {}),
        "NG Stone Lantern": (16, [s.stone_lantern(True), s.stone_lantern(False)], "block", {"anim": (0.6, 0.2)}),
        "NG Paper Lantern": (16, [s.paper_lantern(True), s.paper_lantern(False)], "block", {"anim": (0.7, 0.15)}),
        "NG Bamboo": (16, [s.bamboo(k) for k in (1, 2, 3)], "block", {}),
        "NG Azalea": (16, [s.azalea(k) for k in (1, 2, 3)], "block", {}),
        "NG Boulder": (16, [s.boulder(k) for k in (1, 2, 3)], "block", {}),
        BASIN: (16, [s.basin(f) for f in range(3)], "block", {"anim": (0.35, 0.35, 0.35)}),
        "NG Bench": (16, [s.bench()], "block", {}),
        "NG Well": (16, [s.well()], "block", {}),
        "NG Sake Barrels": (16, [s.barrels()], "block", {}),
        "NG Rice Bales": (16, [s.rice_bags()], "block", {}),
        "NG Bonsai": (16, [s.bonsai()], "block", {}),
        "NG Wayside Shrine": (16, [s.shrine()], "block", {}),
        "NG Memorial Stone": (16, [s.grave(1)], "block", {}),
        "NG Koi": (16, [s.koi(f) for f in range(8)], "flat", {"anim": (0.25,) * 8}),
        "NG Grass Tuft": (8, [s.tuft(k) for k in (1, 2, 3, 4)], "plain", {}),
        "NG Pink Flowers": (8, [s.flowers(k, blossom_flowers) for k in (1, 2)], "flat", {}),
        "NG Blue Flowers": (8, [s.flowers(k, [sprites.CERAMIC[3], sprites.WHITE[3]]) for k in (3, 4)], "flat", {}),
        "NG Lily Pad": (8, [s.lily_pad(k) for k in (1, 2, 3)], "flat", {}),
        "NG Lotus": (8, [s.lily_pad(k, True) for k in (4, 5)], "flat", {}),
        "NG Stepping Stone": (8, [s.stepping_stone(k) for k in (1, 2)], "flat", {}),
        "NG Fallen Petals": (8, [s.petals_pile(k) for k in (1, 2, 3)], "flat", {}),
        BLOCKER: (8, [s.blank()], "block", {"hidden": True}),
        "NG Elder Sakura": (192, [extras.elder_sakura()], "block", {"shadow": 600}),
        "NG Samurai Statue": (32, [extras.samurai_statue()], "block", {"shadow": 160}),
        "NG Nobori Red": (32, [extras.nobori(f, [C("#5a1014"), C("#8a1c1e"), C("#c03028"), C("#e4523a")]) for f in range(2)],
                          "block", {"anim": (0.5, 0.5), "shadow": 40}),
        "NG Nobori Indigo": (32, [extras.nobori(f, [C("#101a34"), C("#1c2c56"), C("#2c447a"), C("#4a66a4")]) for f in range(2)],
                             "block", {"anim": (0.55, 0.55), "shadow": 40}),
        "NG Weapon Rack": (16, [extras.weapon_rack()], "block", {}),
        "NG Straw Dummy": (16, [extras.straw_dummy()], "block", {}),
        "NG Taiko": (16, [extras.taiko()], "block", {}),
        # the Guild Register, beside the Guild Hall's door: its name shows under it
        "NG Guild Register": (32, [extras.guild_register()], "block",
                              {"class": "GuildRegister", "shown": "Guild Register", "showname": True, "shadow": 60}),
        SMOKE: (32, [_tall(extras.smoke(f)) for f in range(4)], "block", {"anim": (0.3,) * 4, "z": 6.3, "noshadow": True}),
        "NG Forge Flame": (8, [extras.forge_flame(f) for f in range(3)], "plain", {"anim": (0.12, 0.12, 0.12), "noshadow": True}),
        "NG Anvil": (16, [extras.anvil()], "block", {}),
        "NG Armoire": (16, [extras.armoire()], "block", {"class": "ReskinVendor", "shown": "Armoire"}),
    }


def _tall(img):
    """A 16 by 32 picture in a 32 cell, standing on its foot."""
    from PIL import Image
    out = Image.new("RGBA", (32, 32))
    out.paste(img, (8, 0))
    return out


VILLAGER_SIZE = 100
BEE_ID = "NG Bee"


def build(preview=None):
    types = Types()
    sheets = {k: Sheet(k) for k in SHEETS}
    char_sheet = Sheet(56)
    char_sheet.h = 8
    char_sheet.cols = 4
    ground_xml, object_xml = [], []

    for name, (painter, variants, extra, colour) in grounds.GROUNDS.items():
        g = ET.Element("Ground", {"type": types.get("grounds", name), "id": name})
        cells = [sheets[8].add(painter(i * 7 + 3)) for i in range(variants)]
        rt = sub(g, "RandomTexture")
        for c in cells:
            texture(rt, SHEETS[8][0], c)
        for tag, val in extra.items():
            if isinstance(val, tuple):
                sub(g, tag, val[0], **val[1])
            else:
                sub(g, tag, val)
        sub(g, "Color", colour)
        ground_xml.append(g)

    blank_cell = None
    for name, (size, imgs, kind, extra) in scenery().items():
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        if "shown" in extra:
            sub(o, "DisplayId", extra["shown"])
        sub(o, "Class", extra.get("class", "GameObject"))
        file = SHEETS[size][0]
        cells = [sheets[size].add(i) for i in imgs]
        if name == BLOCKER:
            blank_cell = cells[0]
        if "anim" in extra:
            texture(o, file, cells[0])
            anim = sub(o, "Animation", prob="1.0")
            for c, t in zip(cells, extra["anim"]):
                texture(sub(anim, "Frame", time=str(t)), file, c)
        elif len(cells) == 1:
            texture(o, file, cells[0])
        else:
            rt = sub(o, "RandomTexture")
            for c in cells:
                texture(rt, file, c)
        sub(o, "Size", 100)
        if kind == "flat":
            sub(o, "DrawOnGround")
        if extra.get("hidden") or kind == "flat" or extra.get("noshadow"):
            sub(o, "NoMiniMap")
            sub(o, "ShadowSize", 0)
        elif "shadow" in extra:
            sub(o, "ShadowSize", extra["shadow"])
        if "z" in extra:
            sub(o, "Z", extra["z"])
        if extra.get("showname"):
            sub(o, "ShowName")
        sub(o, "Static")
        if kind == "block":
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        object_xml.append(o)

    # the 3D models; a railing, fence or torii also turned a quarter for paths that run north
    for name, (shown, cls, blocks, extra) in UNSEEN.items():
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "DisplayId", shown)
        sub(o, "Class", cls)
        texture(o, SHEETS[8][0], blank_cell)
        for k, v in extra.items():
            sub(o, k, v)
        sub(o, "NoMiniMap")
        sub(o, "ShadowSize", 0)
        # a portal must be an entity to be used: a static map object isn't one
        if cls != "GuildHallPortal":
            sub(o, "Static")
        if blocks:
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        object_xml.append(o)

    model_sheet = Sheet(models.TEX, 8)
    for name, (builder, footprint, blocks, shadow) in models.MODELS.items():
        mesh, img = builder()
        cell = model_sheet.add(img)
        with open(os.path.join(MODELS_DIR, model_file(name)), "w", newline="\n") as f:
            f.write(mesh.obj(name))
        # railings, fences and torii turned a quarter for paths that run north; houses turned
        # round to face a street to their north
        turns = [("", 0)] + models.TURNS.get(name, [])
        for suffix, rotation in turns:
            oid = name + suffix
            o = ET.Element("Object", {"type": types.get("objects", oid), "id": oid})
            sub(o, "Class", "GameObject")
            sub(o, "Model", name)
            texture(o, MODEL_SHEET[0], cell)
            if rotation:
                sub(o, "Rotation", rotation)
            sub(o, "Static")
            sub(o, "ShadowSize", 0)
            # the garden's models are lit harder than the old ones, and cast shadows (view.rs)
            sub(o, "ModelContrast", 0.5)
            if shadow:
                sub(o, "ModelShadow", shadow)
            if name in models.UNDER:
                sub(o, "DrawUnder")
            if blocks:
                sub(o, "OccupySquare")
                sub(o, "EnemyOccupySquare")
            object_xml.append(o)

    # the realm portals, flat on the floor and several tiles across (`GroundSize`)
    cav = [sheets[32].add(floors.cavern(f)) for f in range(4)]
    gems = [realm2_art.PALETTES[k]["8"] for k in LANDS]
    vor = [sheets[48].add(floors.vortex(f, gems)) for f in range(8)]
    for oid, shown, file, cells, ground_size, time, extra in (
            (REALM_PORTAL, "Realm Portal", SHEETS[32][0], cav, 4, 0.25, {}),
            (REALM2_PORTAL, "Realm 2", SHEETS[48][0], vor, 6, 0.12, {"DungeonName": "Realm 2"}),
            (ARENA_PORTAL, "Arena Portal", SHEETS[32][0], [sheets[32].add(extras.arena_portal(f)) for f in range(4)], 4, 0.3,
             {"DungeonName": "Arena", "ShowName": None})):
        o = ET.Element("Object", {"type": types.get("objects", oid), "id": oid})
        sub(o, "DisplayId", shown)
        sub(o, "Class", "Portal")
        sub(o, "IntergamePortal")
        # the client writes a NexusPortal's name under it ("Realm 2: Zenith (3/85)")
        if oid != ARENA_PORTAL:
            sub(o, "NexusPortal")
        for k, v in extra.items():
            sub(o, k, v)
        texture(o, file, cells[0])
        anim = sub(o, "Animation", prob="1.0")
        for c in cells:
            texture(sub(anim, "Frame", time=str(time)), file, c)
        sub(o, "Size", 100)
        sub(o, "DrawOnGround")
        sub(o, "GroundSize", ground_size)
        sub(o, "ShadowSize", 0)
        object_xml.append(o)

    # villagers and bees: characters, but not enemies, so pets and shots leave them be
    for name, (pal, rows, steps) in chars.VILLAGERS.items():
        index = char_sheet.add(chars.strip(pal, rows, steps))
        object_xml.append(character(types, name, "Villager", index, VILLAGER_SIZE, False))
    index = char_sheet.add(chars.bee_strip())
    object_xml.append(character(types, BEE_ID, "Bee", index, 70, True))
    for name, (pal, rows, steps) in extras.TWINS.items():
        index = char_sheet.add(chars.strip(pal, rows, steps))
        object_xml.append(character(types, name, name.split()[-1], index, 85, False))
    # the Name Changer and the Character Changer, walking (their own sprites, lofiChar8x8 0x0a and
    # 0x08); still their classes, so the client opens their windows
    for name, shown, cls, cell in ((NAME_CHANGER, "Name Changer", "NameChanger", 0x0a),
                                   (CHARACTER_CHANGER, "Character Changer", "CharacterChanger", 0x08)):
        index = char_sheet.add(extras.changer(cell))
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "DisplayId", shown)
        sub(o, "Class", cls)
        at = sub(o, "AnimatedTexture")
        sub(at, "File", CHARS[0])
        sub(at, "Index", index)
        sub(o, "Size", 100)
        object_xml.append(o)
    chars16 = Sheet(112, 4)
    chars16.h = 16
    for name, shown, cls, cell in WALKING_NPCS:
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "DisplayId", shown)
        sub(o, "Class", cls)
        at = sub(o, "AnimatedTexture")
        sub(at, "File", CHARS16[0])
        sub(at, "Index", chars16.add(extras.quest_npc(cell)))
        sub(o, "Size", 100)
        sub(o, "ShadowSize", 80)
        object_xml.append(o)
    # the XP ducks: the XP Gift's numbers, swimming
    frames = [sheets[8].add(extras.duck(f)) for f in range(3)]
    o = ET.Element("Object", {"type": types.get("objects", DUCK), "id": DUCK})
    sub(o, "DisplayId", "XP Duck")
    sub(o, "Group", "Evaluators")
    sub(o, "Enemy")
    sub(o, "Class", "Character")
    texture(o, SHEETS[8][0], frames[0])
    anim = sub(o, "Animation", prob="1.0", period="1.2", periodJitter="1.0")
    for c, t in zip(frames + [frames[0]], (0.25, 0.25, 0.25, 0.1)):
        texture(sub(anim, "Frame", time=str(t)), SHEETS[8][0], c)
    sub(o, "HitSound", "monster/chicken_hit")
    sub(o, "DeathSound", "monster/chicken_death")
    sub(o, "MaxHitPoints", 600)
    sub(o, "Size", 90)
    sub(o, "Defense", 0)
    sub(o, "Exp", 4200)
    sub(o, "NoMiniMap")
    object_xml.append(o)
    # Amatsumara at his anvil: at rest, now and then the hammer up and down in a shower of sparks
    frames = [sheets[16].add(extras.smith_god(f)) for f in range(3)]
    o = ET.Element("Object", {"type": types.get("objects", SMITH), "id": SMITH})
    sub(o, "DisplayId", "Amatsumara")
    sub(o, "Class", "Character")
    texture(o, SHEETS[16][0], frames[0])
    anim = sub(o, "Animation", prob="1.0", period="3.5", periodJitter="2.5")
    for c, t in ((frames[1], 0.35), (frames[2], 0.25), (frames[1], 0.2), (frames[2], 0.25), (frames[0], 0.1)):
        texture(sub(anim, "Frame", time=str(t)), SHEETS[16][0], c)
    sub(o, "Size", 130)
    sub(o, "MaxHitPoints", 1000)
    sub(o, "NoMiniMap")
    sub(o, "Invincible")
    sub(o, "StasisImmune")
    sub(o, "ShadowSize", 120)
    object_xml.append(o)

    write_xml(OUT_XML["grounds"], "GroundTypes", ground_xml, "The Nexus garden's grounds: generated by tools/build_nexus_garden.py, don't edit")
    write_xml(OUT_XML["objects"], "Objects", object_xml,
              "The Nexus garden's scenery, models, portals, villagers and bees: generated by tools/build_nexus_garden.py, don't edit")
    out = {}
    for size, (_, fname) in SHEETS.items():
        out[fname] = sheets[size].save(os.path.join(IMAGES, fname))
    out[CHARS[1]] = char_sheet.save(os.path.join(IMAGES, CHARS[1]))
    out[MODEL_SHEET[1]] = model_sheet.save(os.path.join(IMAGES, MODEL_SHEET[1]))
    out[CHARS16[1]] = chars16.save(os.path.join(IMAGES, CHARS16[1]))
    types.save()
    print("Nexus garden: %d grounds, %d objects, %d models" % (len(ground_xml), len(object_xml), len(models.MODELS)))
    if preview:
        save_preview(out, preview)


def model_file(name):
    """The OBJ file a model is written to (tools/gen_rs_manifest.py RS_ONLY_MODELS lists them)."""
    return "nexusGarden" + name[3:].replace(" ", "") + ".obj"


def character(types, name, shown, index, size, flying):
    o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
    sub(o, "DisplayId", shown)
    sub(o, "Class", "Character")
    at = sub(o, "AnimatedTexture")
    sub(at, "File", CHARS[0])
    sub(at, "Index", index)
    sub(o, "Size", size)
    sub(o, "MaxHitPoints", 1000)
    sub(o, "Defense", 0)
    sub(o, "NoMiniMap")
    # pets don't attack what is invincible (PetShoot), nor does anything else
    sub(o, "Invincible")
    sub(o, "StasisImmune")
    if flying:
        sub(o, "Flying")
        sub(o, "ShadowSize", 40)
        sub(o, "Z", 0.6)
    return o


def write_xml(paths, root_tag, elements, comment):
    root = ET.Element(root_tag)
    for e in elements:
        root.append(e)
    ET.indent(root, "\t")
    text = "<!-- %s -->\n%s\n" % (comment, ET.tostring(root, encoding="unicode"))
    for p in paths:
        with open(p, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(text)


def save_preview(sheets, path):
    """Every sheet stacked, four times bigger, on grey."""
    scale = 4
    imgs = [img.resize((img.width * scale, img.height * scale), Image.NEAREST) for img in sheets.values()]
    out = Image.new("RGBA", (max(i.width for i in imgs), sum(i.height + 12 for i in imgs)), (90, 90, 96, 255))
    y = 0
    for img in imgs:
        out.alpha_composite(img, (0, y))
        y += img.height + 12
    out.save(path)


if __name__ == "__main__":
    args = sys.argv[1:]
    build(args[args.index("--preview") + 1] if "--preview" in args else None)
