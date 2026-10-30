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
from nexus_garden import chars, floors, grounds, models, sprites  # noqa: E402
from realm2 import art as realm2_art  # noqa: E402

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
    64: ("nexusGardenModels64x64", "NexusGardenModels64x64.png"),
}
CHARS = ("nexusGardenChars8x8", "NexusGardenChars8x8.png")
COLS = 16

# Realm 2's eleven lands, round its vortex's ring as round its standing portal's arch
LANDS = ["cube", "penta", "lost", "hermit", "ship", "sphinx", "skull", "garden", "dragon", "kingdom", "bees"]

# The ids the world server and the map builder use
REALM_PORTAL = "NG Realm Cavern"        # RealmPortalMonitor: a realm's portal in the Nexus
REALM2_PORTAL = "NG Realm2 Vortex"      # and Realm 2's
BASIN = "NG Garden Basin"               # NexusWorld: heals whoever stands by it
BLOCKER = "NG Blocker"                  # invisible: fills a building's tiles round its model


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
    }


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

    for name, (size, imgs, kind, extra) in scenery().items():
        o = ET.Element("Object", {"type": types.get("objects", name), "id": name})
        sub(o, "Class", "GameObject")
        file = SHEETS[size][0]
        cells = [sheets[size].add(i) for i in imgs]
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
        if extra.get("hidden") or kind == "flat":
            sub(o, "NoMiniMap")
            sub(o, "ShadowSize", 0)
        elif "shadow" in extra:
            sub(o, "ShadowSize", extra["shadow"])
        sub(o, "Static")
        if kind == "block":
            sub(o, "OccupySquare")
            sub(o, "EnemyOccupySquare")
        object_xml.append(o)

    # the 3D models; a railing, fence or torii also turned a quarter for paths that run north
    for name, (builder, footprint, blocks) in models.MODELS.items():
        mesh, img = builder()
        cell = sheets[64].add(img)
        file_name = name[3:].replace(" ", "") + ".obj"
        with open(os.path.join(MODELS_DIR, "nexusGarden" + file_name), "w", newline="\n") as f:
            f.write(mesh.obj(name))
        turns = [("", 0)] + ([(" NS", 90)] if footprint[1] == 1 else [])
        for suffix, rotation in turns:
            oid = name + suffix
            o = ET.Element("Object", {"type": types.get("objects", oid), "id": oid})
            sub(o, "Class", "GameObject")
            sub(o, "Model", name)
            texture(o, SHEETS[64][0], cell)
            if rotation:
                sub(o, "Rotation", rotation)
            sub(o, "Static")
            sub(o, "ShadowSize", 0)
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
            (REALM2_PORTAL, "Realm 2", SHEETS[48][0], vor, 6, 0.12, {"DungeonName": "Realm 2"})):
        o = ET.Element("Object", {"type": types.get("objects", oid), "id": oid})
        sub(o, "DisplayId", shown)
        sub(o, "Class", "Portal")
        sub(o, "IntergamePortal")
        # the client writes a NexusPortal's name under it ("Realm 2: Zenith (3/85)")
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

    write_xml(OUT_XML["grounds"], "GroundTypes", ground_xml, "The Nexus garden's grounds: generated by tools/build_nexus_garden.py, don't edit")
    write_xml(OUT_XML["objects"], "Objects", object_xml,
              "The Nexus garden's scenery, models, portals, villagers and bees: generated by tools/build_nexus_garden.py, don't edit")
    out = {}
    for size, (_, fname) in SHEETS.items():
        out[fname] = sheets[size].save(os.path.join(IMAGES, fname))
    out[CHARS[1]] = char_sheet.save(os.path.join(IMAGES, CHARS[1]))
    types.save()
    print("Nexus garden: %d grounds, %d objects, %d models" % (len(ground_xml), len(object_xml), len(models.MODELS)))
    if preview:
        save_preview(out, preview)


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
