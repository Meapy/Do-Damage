"""Port Doomed Realms items and pets into betterSkillys.

Usage:
    python tools/port_doomed.py --dr "<path to Doomed Realms repo>" [--plan]

What it does:
  * Selects every Doomed Realms object betterSkillys doesn't have (by id) of the ported classes
    (items, dyes, and the projectiles / pets / summons those items reference), skipping class
    abilities that need the Doomed Realms classes (PORT_LATER_EFFECTS).
  * Gives them new type codes in 0xD000-0xDFFF. The mapping is kept in tools/doomed/types.json
    so a type never changes once characters hold the item.
  * Points their sprites at copies of the Doomed Realms sprite sheets, registered in the client
    under a "dr" prefix (drLofiObj5, drChars8x8rPets1, ...), so they keep their exact art.
    RemoteTexture sprites (downloaded once into tools/doomed/remote/ by --fetch-remote) become
    one small sheet each (drRemote<id>).
  * Writes the XML for the server (source/Shared/resources/xml/custom/DoomedRealms.xml) and the
    client (client/src/kabam/rotmg/assets/custom/xmls/DoomedRealms.xml), and generates
    client/src/kabam/rotmg/assets/doomed/DoomedAssets.as, which registers the sheets.

The Doomed Realms sprite sheets are exported from its client with FFDec (Doomed Realms
tools/ffdec) into build/doomed/images on first use.
"""

import argparse
import copy
import glob
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time
import urllib.request
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE = os.path.join(ROOT, "tools", "doomed")
TYPES_JSON = os.path.join(STATE, "types.json")
REMOTE_DIR = os.path.join(STATE, "remote")
BUILD = os.path.join(ROOT, "build", "doomed")
SERVER_XML = os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealms.xml")
CLIENT_ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
CLIENT_XML = os.path.join(CLIENT_ASSETS, "custom", "xmls", "DoomedRealms.xml")
CLIENT_XML_AS = os.path.join(CLIENT_ASSETS, "custom", "xmls", "DoomedRealms.as")
CLIENT_IMAGES = os.path.join(CLIENT_ASSETS, "doomed", "images")
CLIENT_ASSETS_AS = os.path.join(CLIENT_ASSETS, "doomed", "DoomedAssets.as")

TYPE_START, TYPE_END = 0xD000, 0xDFFF
PORTED_CLASSES = {"Equipment", "Dye", "Player"}
# Item abilities that need code not ported yet (none: the class abilities are implemented in
# source/WorldServer/core/objects/player/Player.UseItem.Doomed.cs).
PORT_LATER_EFFECTS = set()
# Doomed Realms slot types that mean something else in betterSkillys (26 = pet egg, 28 = lute).
SLOT_TYPE_REMAP = {"26": "126", "28": "128"}
REMOTE_URL = "https://realmofthemadgod.appspot.com/picture/get?id={}"

# Doomed Realms client sheet registrations (its util/loadEmbeds): name -> (embed image, mask, kind, args)
# kind "set": AssetLibrary.addImageSet(name, img, w, h)
# kind "char": AnimatedChars.add(name, img, mask, w, h, sheetW, sheetH, dir)
SHEETS = {
    "lofiChar8x8": ("lofiChar", None, "set", (8, 8)),
    "lofiChar16x8": ("lofiChar", None, "set", (16, 8)),
    "lofiChar16x16": ("lofiChar", None, "set", (16, 16)),
    "lofiChar28x8": ("lofiChar2", None, "set", (8, 8)),
    "lofiChar216x8": ("lofiChar2", None, "set", (16, 8)),
    "lofiChar216x16": ("lofiChar2", None, "set", (16, 16)),
    "lofiCharBig": ("lofiCharBig", None, "set", (16, 16)),
    "lofiEnvironment": ("lofiEnvironment", None, "set", (8, 8)),
    "lofiEnvironment2": ("lofiEnvironment2", None, "set", (8, 8)),
    "lofiEnvironment3": ("lofiEnvironment3", None, "set", (8, 8)),
    "lofiInterface": ("lofiInterface", None, "set", (8, 8)),
    "redLootBag": ("redLootBag", None, "set", (8, 8)),
    "lofiInterfaceBig": ("lofiInterfaceBig", None, "set", (16, 16)),
    "lofiInterface2": ("lofiInterface2", None, "set", (8, 8)),
    "lofiObj": ("lofiObj", None, "set", (8, 8)),
    "lofiObj2": ("lofiObj2", None, "set", (8, 8)),
    "lofiObj3": ("lofiObj3", None, "set", (8, 8)),
    "lofiObj4": ("lofiObj4", None, "set", (8, 8)),
    "lofiObj5": ("lofiObj5", None, "set", (8, 8)),
    "lofiObj6": ("lofiObj6", None, "set", (8, 8)),
    "lofiObjBig": ("lofiObjBig", None, "set", (16, 16)),
    "lofiObj40x40": ("lofiObj40x40", None, "set", (40, 40)),
    "lofiProjs": ("lofiProjs", None, "set", (8, 8)),
    "lofiProjsBig": ("lofiProjsBig", None, "set", (16, 16)),
    "lofiParts": ("lofiParts", None, "set", (8, 8)),
    "chars8x8rBeach": ("chars8x8rBeach", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8dBeach": ("chars8x8dBeach", None, "char", (8, 8, 56, 8, "DOWN")),
    "chars8x8rLow1": ("chars8x8rLow1", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rLow2": ("chars8x8rLow2", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rMid": ("chars8x8rMid", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rMid2": ("chars8x8rMid2", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rHigh": ("chars8x8rHigh", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rHero1": ("chars8x8rHero1", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8rHero2": ("chars8x8rHero2", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars8x8dHero1": ("chars8x8dHero1", None, "char", (8, 8, 56, 8, "DOWN")),
    "chars8x8dEncounters": ("chars8x8dEncounters", None, "char", (8, 8, 56, 8, "DOWN")),
    "chars8x8rEncounters": ("chars8x8rEncounters", None, "char", (8, 8, 56, 8, "RIGHT")),
    "chars16x8dEncounters": ("chars16x8dEncounters", None, "char", (16, 8, 112, 8, "DOWN")),
    "chars16x8rEncounters": ("chars16x8rEncounters", None, "char", (16, 8, 112, 8, "DOWN")),
    "chars16x16dEncounters": ("chars16x16dEncounters", None, "char", (16, 16, 112, 16, "DOWN")),
    "chars16x16dEncounters2": ("chars16x16dEncounters2", None, "char", (16, 16, 112, 16, "DOWN")),
    "chars16x16dMountains1": ("chars16x16dMountains1", None, "char", (16, 16, 112, 16, "DOWN")),
    "chars16x16dMountains2": ("chars16x16dMountains2", None, "char", (16, 16, 112, 16, "DOWN")),
    "chars16x16rEncounters": ("chars16x16rEncounters", None, "char", (16, 16, 112, 16, "RIGHT")),
    "players": ("players", "playersMask", "char", (8, 8, 56, 24, "RIGHT")),
    "chars8x8rPets1": ("chars8x8rPets1", "chars8x8rPets1Mask", "char", (8, 8, 56, 8, "RIGHT")),
}
SHARED_SHEETS = {"invisible"}  # same in both clients
# Embeds whose image classes FFDec exports under the client's obfuscated class names
OBFUSCATED_EMBEDS = {"lofiCharBig": "class_429", "lofiInterfaceBig": "class_440", "lofiInterface2": "class_426",
                     "lofiObjBig": "class_450", "lofiProjs": "class_448", "lofiProjsBig": "class_427",
                     "lofiParts": "class_461", "players": "class_458", "playersMask": "class_455",
                     "redLootBag": "class_447"}


def dr_name(sheet):
    return "dr" + sheet[0].upper() + sheet[1:]


def load_objects(files):
    objs = {}
    for f in sorted(files):
        text = re.sub(r"<\?xml[^>]*\?>", "", open(f, encoding="utf-8", errors="replace").read())
        try:
            root = ET.fromstring("<r>" + text + "</r>")
        except ET.ParseError as e:
            print("  skipping unparsable %s: %s" % (os.path.basename(f), e))
            continue
        for o in root.iter("Object"):
            if o.get("id"):
                objs[o.get("id")] = o
    return objs


def references(o):
    """Object ids an item or object points at (projectile sprites, pets, summons, ...)."""
    refs = set()
    for p in o.iter("Projectile"):
        oid = p.findtext("ObjectId")
        if oid:
            refs.add(oid.strip())
    for a in list(o.iter("Activate")) + list(o.iter("ActivateOnEquip")):
        for attr in ("objectId", "id", "dungeonName"):
            if a.get(attr):
                refs.add(a.get(attr))
        if a.text == "Summon" and a.get("objectId"):
            refs.add(a.get("objectId") + " Summon")  # Summon spawns "<objectId> Summon"
    return refs


def select(dr, bs, extra=(), allowed_portals=frozenset(), behaviour_refs=None):
    """extra: ids to port besides the item classes (dungeon map objects and portals);
    allowed_portals: portals of ported dungeons; behaviour_refs: {id: ids its behaviour spawns}."""
    behaviour_refs = behaviour_refs or {}

    def refs_of(o):
        return references(o) | behaviour_refs.get(o.get("id"), set())

    chosen, skipped, renames = {}, {}, {}
    for oid in extra:
        if oid in dr and oid not in bs:
            chosen[oid] = dr[oid]
    for oid, o in dr.items():
        if o.findtext("Class") not in PORTED_CLASSES:
            continue
        if oid in bs:
            # a Doomed Realms class whose name betterSkillys uses for a monster (Pirate, Snowman)
            if o.findtext("Class") == "Player" and bs[oid].findtext("Class") != "Player":
                renames[oid] = oid + " (Doomed)"
            continue
        effects = {a.text for a in o.iter("Activate")}
        if effects & PORT_LATER_EFFECTS:
            skipped[oid] = "class ability: " + ", ".join(sorted(effects & PORT_LATER_EFFECTS))
            continue
        # keys that open Doomed Realms dungeons wait for the dungeons
        portals = [r for r in references(o) if r in dr and r not in bs and dr[r].findtext("Class") == "Portal"
                   and r not in allowed_portals]
        if portals:
            skipped[oid] = "opens " + portals[0]
            continue
        chosen[oid] = o
    # add what they reference (projectile objects, pets, ...), transitively. A referenced Doomed
    # Realms character whose id betterSkillys uses for something else (e.g. "Black Cat", a pet-yard
    # Pet there) is ported under its own id, "<id> (Doomed)", keeping its display name.
    todo = list(chosen.values()) + [dr[old] for old in renames]
    while todo:
        for ref in refs_of(todo.pop()):
            if ref not in dr or ref in chosen or ref in renames:
                continue
            if dr[ref].findtext("Class") == "Portal" and ref not in allowed_portals:
                continue
            if ref in bs:
                if dr[ref].findtext("Class") != "Character" or bs[ref].findtext("Class") == "Character":
                    continue
                renames[ref] = ref + " (Doomed)"
                continue
            chosen[ref] = dr[ref]
            todo.append(dr[ref])
    for old, new in renames.items():
        o = copy.deepcopy(dr[old])
        o.set("id", new)
        if o.find("DisplayId") is None:
            display = ET.Element("DisplayId")
            display.text = old
            o.insert(0, display)
        chosen[new] = o
    if renames:
        for oid in list(chosen):
            o = chosen[oid] if oid in renames.values() else copy.deepcopy(chosen[oid])
            for a in list(o.iter("Activate")) + list(o.iter("ActivateOnEquip")):
                if a.get("objectId") in renames:
                    a.set("objectId", renames[a.get("objectId")])
            for p in o.iter("Projectile"):
                oid_el = p.find("ObjectId")
                if oid_el is not None and (oid_el.text or "").strip() in renames:
                    oid_el.text = renames[oid_el.text.strip()]
            chosen[oid] = o
    return chosen, skipped


def remote_ids(objs):
    ids = set()
    for o in objs.values():
        for r in o.iter("RemoteTexture"):
            ids.add((r.findtext("Instance") or "draw").strip() + ":" + (r.findtext("Id") or "").strip())
    return ids


def remote_file(rid):
    instance, num = rid.split(":", 1)
    return os.path.join(REMOTE_DIR, "%s_%s.png" % (instance, num))


def remote_sheet(rid):
    instance, num = rid.split(":", 1)
    return "drRemote_%s_%s" % (instance, num)


def fetch_remote(ids):
    os.makedirs(REMOTE_DIR, exist_ok=True)
    hosts = {"draw": "https://realmofthemadgod.appspot.com", "tdraw": "https://rotmgtesting.appspot.com"}
    for rid in sorted(ids):
        path = remote_file(rid)
        if os.path.exists(path):
            continue
        instance, num = rid.split(":", 1)
        if instance not in hosts or not num.isdigit():
            print("  can't fetch remote texture %s" % rid)
            continue
        data = b""
        for attempt in range(4):
            time.sleep(1 + 10 * attempt)  # the service answers "<Error>Endpoint spam</Error>" when hit too fast
            try:
                data = urllib.request.urlopen(hosts[instance] + "/picture/get?id=" + num, timeout=30).read()
            except OSError as e:
                print("  remote texture %s: %s" % (rid, e))
                break
            if b"Endpoint spam" not in data:
                break
        if not data.startswith(b"\x89PNG"):
            print("  remote texture %s is not a PNG, skipped" % rid)
            continue
        open(path, "wb").write(data)
    print("Remote textures in %s: %d" % (REMOTE_DIR, len(glob.glob(os.path.join(REMOTE_DIR, "*.png")))))


def png_size(path):
    data = open(path, "rb").read(32)
    return struct.unpack(">II", data[16:24])


def assign_types(objs):
    types = json.load(open(TYPES_JSON)) if os.path.exists(TYPES_JSON) else {}
    used = {int(v, 16) for v in types.values()}
    nxt = TYPE_START
    for oid, o in sorted(objs.items(), key=lambda kv: (int(kv[1].get("type", "0"), 16), kv[0])):
        if oid in types:
            continue
        while nxt in used:
            nxt += 1
        if nxt > TYPE_END:
            sys.exit("Out of type codes in 0x%04X-0x%04X" % (TYPE_START, TYPE_END))
        types[oid] = "0x%04X" % nxt
        used.add(nxt)
    os.makedirs(STATE, exist_ok=True)
    with open(TYPES_JSON, "w") as f:
        json.dump(dict(sorted(types.items(), key=lambda kv: kv[1])), f, indent=1)
    return types


def convert(o, new_type, sheets, remotes):
    """Copy of a Doomed Realms object with its new type and sprites pointed at the dr sheets."""
    o = copy.deepcopy(o)
    o.set("type", new_type)
    # betterSkillys reads <Tier> as a number; Doomed Realms also used "UT" and "UR" (untiered)
    for tier in o.findall("Tier"):
        if not (tier.text or "").strip().lstrip("-").isdigit():
            o.remove(tier)
    for slot in o.findall("SlotType"):
        slot.text = SLOT_TYPE_REMAP.get((slot.text or "").strip(), slot.text)
    for slots in o.findall("SlotTypes"):
        slots.text = ", ".join(SLOT_TYPE_REMAP.get(s.strip(), s.strip()) for s in slots.text.split(","))
    for tex in o.iter():
        if tex.tag in ("Texture", "AnimatedTexture", "Mask") or tex.tag == "AltTexture":
            f = tex.find("File")
            if f is not None and f.text:
                name = f.text.strip()
                if name in SHEETS:
                    f.text = dr_name(name)
                    sheets.add(name)
                elif name not in SHARED_SHEETS:
                    print("  %s: unknown sheet %s left as is" % (o.get("id"), name))
    for parent in o.iter():
        for i, child in enumerate(list(parent)):
            if child.tag != "RemoteTexture":
                continue
            rid = (child.findtext("Instance") or "draw").strip() + ":" + (child.findtext("Id") or "").strip()
            if not os.path.exists(remote_file(rid)):
                print("  %s: remote texture %s missing (run with --fetch-remote)" % (o.get("id"), rid))
                continue
            animated = o.findtext("Class") == "Character"
            new = ET.Element("AnimatedTexture" if animated else "Texture")
            ET.SubElement(new, "File").text = remote_sheet(rid)
            ET.SubElement(new, "Index").text = "0"
            new.tail = child.tail
            parent.remove(child)
            parent.insert(i, new)
            use = remotes.setdefault(rid, {"animated": False, "static": False, "right": False})
            use["animated" if animated else "static"] = True
            use["right"] = use["right"] or child.find("Right") is not None
    # betterSkillys' own sprite in place of the Doomed Realms one
    if o.get("id") in TEXTURE_OVERRIDES:
        sheet, index = TEXTURE_OVERRIDES[o.get("id")]
        tex = next(t for t in o if t.tag in ("AnimatedTexture", "Texture"))
        tex.find("File").text, tex.find("Index").text = sheet, str(index)
    if o.get("id") in DISPLAY_OVERRIDES:
        name = o.find("DisplayId")
        if name is None:
            name = ET.Element("DisplayId")
            o.insert(0, name)
        name.text = DISPLAY_OVERRIDES[o.get("id")]
    for proj in o.findall("Projectile"):
        new = PROJECTILE_OVERRIDES.get(o.get("id"), {}).get(proj.get("id"))
        if new:
            proj.find("ObjectId").text = new
    return o


# Classes that start with the lowest tier of their weapon and ability, as the betterSkillys classes
# do, in place of what Doomed Realms gave them: the Monk started with Fists of Broken Bones (a boss
# drop) and Power of the Fountains (T7), which also kept /gear from upgrading its weapon.
STARTS_AT_LOWEST_TIER = {"Monk"}


def fix_class_references(converted, dr, bs, types):
    """Ported classes list their starting equipment and unlock requirements by type code."""
    dr_by_type = {int(o.get("type"), 16): oid for oid, o in dr.items() if o.get("type")}

    def new_type(oid):
        for candidate in (oid, oid + " (Doomed)"):
            if candidate in types:
                return types[candidate], candidate
        if oid in bs and bs[oid].get("type"):
            return bs[oid].get("type"), oid
        return None, oid

    # lowest-tier item per slot type, for starting slots Doomed Realms left empty or got wrong
    slot_of, starter = {}, {}
    for o in list(bs.values()) + converted:
        if o.findtext("Class") != "Equipment" or not o.get("type"):
            continue
        slot = (o.findtext("SlotType") or "").strip()
        slot_of[o.get("type").lower()] = slot
        tier = o.findtext("Tier")
        rank = int(tier) if tier and tier.strip().isdigit() else 99
        if slot not in starter or rank < starter[slot][0]:
            starter[slot] = (rank, o.get("type"))

    for o in converted:
        if o.findtext("Class") != "Player":
            continue
        eq = o.find("Equipment")
        slots = [s.strip() for s in (o.findtext("SlotTypes") or "").split(",")]
        if eq is not None and eq.text:
            codes = []
            for i, code in enumerate(eq.text.split(",")):
                code = code.strip()
                t = None
                if code not in ("-1", "") and int(code, 16) in dr_by_type:
                    t, _ = new_type(dr_by_type[int(code, 16)])
                # weapon and ability: replace a missing or wrong-slot starting item
                if i < 2 and i < len(slots) and (t is None or slot_of.get(t.lower()) != slots[i]
                                                 or o.get("id") in STARTS_AT_LOWEST_TIER):
                    t = starter.get(slots[i], (0, None))[1]
                codes.append(t or "-1")
            eq.text = ", ".join(codes)
        for u in o.findall("UnlockLevel"):
            t, name = new_type((u.text or "").strip())
            if t:
                u.set("type", t)
                u.text = name
            else:
                o.remove(u)


# objects the Doomed Realms minigame worlds spawn from code (wserver/realm/worlds/ZombieMap.cs,
# ZombieMG.cs, Herding.cs); the battle arenas only spawn betterSkillys monsters
MINIGAME_SPAWNS = {"Zombie", "Hell Hound", "Crawler", "Zombie of Draconis", "Regular Zombie", "Tank Zombie",
                   "Speedy Zombie", "Zombie Tower", "Herding Sheep", "Giant Herding Sheep", "Black Herding Sheep"}


# sprite donors for objects whose remote sprite is gone and whose name matches nothing in betterSkillys
SPRITE_DONORS = {"Elder Beach Portal": "Beachzone Portal"}

# objects drawn anew here: id -> (sheet, index). Oryx 4 burns (tools/build_hash_cellar.py). (Oryx 5
# is a new object, Oryx the Ascended God, made by tools/build_elysium.py; this one is left unused.)
TEXTURE_OVERRIDES = {"Oryx the Mad God 4": ("hashCellarOryx16x16", 0),
                     # the incantations' seals (tools/draw_oryx_gates.py, INCANTATION_CELLS)
                     "Hash Cellar Incantation": ("oryxIncantations16x16", 1),
                     "Doomed Cellar Incantation": ("oryxIncantations16x16", 2),
                     # Oryx 5's colours (tools/draw_t15_weapons.py): the T15 staff, and the T16 armours
                     "Staff of Endless Energy": ("t15Weapons8x8", 7),
                     "Sphinx Leather Armor": ("t16Armors8x8", 0),
                     "Armor of the Pyramid": ("t16Armors8x8", 1),
                     "Robe of the Nile": ("t16Armors8x8", 2),
                     # Oryx 4's daggers, red in their shading (tools/draw_oryx_uts.py)
                     "Doom Dirk": ("oryxUts8x8", 0),
                     "Flying Doom Daggers": ("oryxUts8x8", 1)}
# projectiles drawn anew: id -> {projectile id: object}. Oryx 4's first phase shoots hellfire.
PROJECTILE_OVERRIDES = {"Oryx the Mad God 4": {"2": "Hellfire Bolt", "4": "Hellfire Star"},
                        # Oryx 5's weapons (their projectiles have no id; tools/draw_oryx_uts.py): the
                        # Elysian Staff shoots his Celestial Bolt in his colours, the Sword of Dark
                        # Enchantment a black and red slash (it shot sheep)
                        "Staff of Heavens": {None: "Elysian Bolt"},
                        "Sword of Dark Enchantment": {None: "Dark Enchantment Slash"}}
# names for players: each Oryx after the Chamber is named for his theme
DISPLAY_OVERRIDES = {"Oryx the Mad God 4": "Oryx the Infernal Mad God", "Hash Cellar Incantation": "Oryx's Inferno Incantation",
                     "Doomed Cellar Incantation": "Oryx's Elysium Incantation", "Staff of Heavens": "Elysian Staff",
                     # the T16 armours are Oryx 5's own (SETUP-LOCAL.md, "T16 armours"); their ids stay so
                     # ones players hold keep working
                     "Sphinx Leather Armor": "Hide of the Firmament", "Armor of the Pyramid": "Armor of Ascension",
                     "Robe of the Nile": "Robe of Absolution",
                     # Oryx 4's poison, in his Inferno's name (the Oryx UT pass)
                     "Doom Poison": "Infernal Brimstone Poison",
                     # the portal to the Arena (port_doomed_worlds.WORLD_NAMES)
                     "Battle Arena Portal": "Arena Portal"}


# Loot bags (<BagType>): Doomed Realms numbered its bags differently, so its rarest items dropped
# in brown or blue bags. Here 6 is the white bag (announced to the server) and 7 the bag of special
# consumables. These are the Oryx chain's top drops (BehaviorDb.OryxDoomed.cs).
BAG_TYPES = {
    "Ring of Oryx": 6, "Oryx Scale Armor": 6, "Oryx Hide Armor": 6, "Oryx Hide Robe": 6,
    "Oryx Tooth Sword": 6, "Doom Dirk": 6, "Flying Doom Daggers": 6, "Tome of Noble Assault": 6,
    "Doom Poison": 6, "Sword of Dark Enchantment": 6,
    "Potion of Oryx": 7, "Potion of Power": 7,
    "Potion of Max Life": 6,
    "Sphinx Leather Armor": 6, "Armor of the Pyramid": 6, "Robe of the Nile": 6,
    # Oryx 4 and 5's pet (it had no bag: the donor shop sold it)
    "Lil' Oryx Generator": 6,
}


# What an IncrementStat item puts into potion storage when its stat is already at the class
# maximum (storeAmount, in stat points; it was the whole amount): (stat, stored, description).
# The Potions of Max still fill their stat to the maximum (+100, or +1000 health and magic), but
# would have stored 100 potions (200 life or mana); each stores 6 potions' worth, as the Max Life
# one first did (life and mana potions are 5 points each).
ITEM_STORE = {
    "Potion of Max Life": ("0", "30", "A rare potion that permanently maxes health. At full maximum health, it adds 6 life potions to your potion storage instead."),
    "Potion of Max Mana": ("3", "30", "A potion that permanently maxes magic. At full maximum magic, it adds 6 mana potions to your potion storage instead."),
    "Potion of Max Attack": ("20", "6", "A potion that permanently maxes attack power. At full maximum attack, it adds 6 attack potions to your potion storage instead."),
    "Potion of Max Defense": ("21", "6", "A potion that permanently maxes defense power. At full maximum defense, it adds 6 defense potions to your potion storage instead."),
    "Potion of Max Speed": ("22", "6", "A potion that permanently maxes speed. At full maximum speed, it adds 6 speed potions to your potion storage instead."),
    "Potion of Max Vitality": ("26", "6", "A potion that permanently maxes vitality. At full maximum vitality, it adds 6 vitality potions to your potion storage instead."),
    "Potion of Max Wisdom": ("27", "6", "A potion that permanently maxes wisdom. At full maximum wisdom, it adds 6 wisdom potions to your potion storage instead."),
    "Potion of Max Dexterity": ("28", "6", "A potion that permanently maxes dexterity. At full maximum dexterity, it adds 6 dexterity potions to your potion storage instead."),
}


# The untiered rings pass (SETUP-LOCAL.md, "Untiered rings"). Admin items: untradeable and listed with
# the staff's gear (Doomed Realms' admin gear and rewards, with far more stats than anything else).
# Ring of the Abyss's numbers are in GEAR_TUNING.
ADMIN_ITEMS = ["Doomie Amulet", "Doomed Hero Badge", "Fallen Crown", "God Rushing", "Doomed Gods",
               "Doomie Staff", "Doomie Yolo"]
# rewards the staff give (<Reward/>, which only the wiki reads)
REWARD_ITEMS = ["America Sword", "America Shield"]
# items nothing gives any more (<Unobtainable/>: the client's tooltip and the wiki say so): the T20
# Oryx set and armours, taken out of the drops (port_doomed_monsters.LOOT_CHANGES, the Oryx behaviours)
UNOBTAINABLE_ITEMS = ["Ring of Oryx", "Oryx Hide Armor", "Oryx Hide Robe", "Oryx Scale Armor", "Soul Spawned Armor",
                      # the Oryx UT pass: the old forge materials (the forge shards took their place),
                      # the Forge Amulet (the forge no longer takes it) and the Burning Amulet
                      "Shattered Rune of Oryx", "Broken Oryx Armor", "Purified Doom Saliva", "Forge Amulet",
                      "Burning Amulet"]
# loot in a private bag (the T16 armours, as the T15s they top, and Oryx 4 and 5's pet)
SOULBOUND_ITEMS = ["Sphinx Leather Armor", "Armor of the Pyramid", "Robe of the Nile", "Lil' Oryx Generator"]
DESCRIPTIONS = {
    # the Lair of Draconis's four relics (BehaviorDb.Draconis.cs), the Elder key's recipe
    "Black Dragon Gods Blade": "A shard of Feargus the Obsidian Dragon, dropped in the Lair of Draconis."
        " The Divine Forge melds it with the other three dragons' relics into an Elder Draconis Key.",
    "Blue Dragon Gods Helm": "A relic of Nikao the Azure Dragon, dropped in the Lair of Draconis."
        " The Divine Forge melds it with the other three dragons' relics into an Elder Draconis Key.",
    "Red Dragon Gods Helm": "A relic of Pyyr the Crimson Dragon, dropped in the Lair of Draconis."
        " The Divine Forge melds it with the other three dragons' relics into an Elder Draconis Key.",
    "Green Dragon Gods Helm": "A relic of Limoz the Veridian Dragon, dropped in the Lair of Draconis."
        " The Divine Forge melds it with the other three dragons' relics into an Elder Draconis Key.",
    "Santa's Hat": "A hat that protects Santa from even death itself. A blow that would kill its wearer breaks "
                   "the hat instead, and sends them back to the Nexus.",
    "Staff of Heavens": "Raised over Oryx's Elysium in his last stand, it fires the holy bolts of the Ascended God, "
                        "and now and then calls one of his halos to circle its bearer.",
    "Sword of Dark Enchantment": "A fatal sword enchanted with the black magic of the dark mages. Its strikes "
                                 "now and then tear loose a small dark orb that zaps whatever is near.",
    "Doom Poison": "Brimstone from the deepest fires of Oryx's Inferno. It curses all it burns, but its fumes "
                   "leave the one who throws it silenced for a moment.",
    "Lil' Oryx Generator": "Every evil boss creates miniature versions of himself. It's just something they do. "
                           "Using it gives you Lil' Oryx for good, and uses it up. Oryx the Infernal Mad God and "
                           "Oryx the Ascended God drop it.",
    "America Sword": "Rewarded by DODMG: America!",
    "America Shield": "Rewarded by DODMG: America!",
    "Sphinx Leather Armor": "Leather cut from the open sky over Oryx's Elysium, light as a cloud and as hard to strike. "
                            "Only Oryx the Ascended God drops it.",
    "Armor of the Pyramid": "Plate forged for the Ascended God's own guard, purple steel trimmed in gold. "
                            "Only Oryx the Ascended God drops it.",
    "Robe of the Nile": "Woven for the priests of Oryx's last temple, it hums with the light of the Elysium. "
                        "Only Oryx the Ascended God drops it.",
}


# The tiered gear changes (SETUP-LOCAL.md, "Tiered weapon rebalance" and "Tiered abilities").
# Weapons: the Dual Blades all
# reach exactly 5 tiles (speed 200, LifetimeMS 250), get +2 Defense a tier from T11 and +2 Speed a tier
# from T12; Rage Claw Blades (T9) lose their 1.5x rate of fire for damage between T8's and T10's.
# Fists of Broken Bones, a boss drop, is untiered: the Monk's tiered fists are in CustomItems.xml,
# with the T13 and T14 Dual Blades.
# Abilities: the Doomed Realms copies in the betterSkillys classes' ability slots, which never
# dropped (the tier drops leave them out), are untiered, so each tier has the one ability that
# drops; so are Oceanic Curse (T9, no source) and Oceanic Nova (T10, a boss drop). Power of the
# Fountains, the Monk's only ability, moves from T1 to T7, where its numbers belong; the missing
# tiers of the Doomed Realms classes' abilities are in CustomItems.xml.
# The Pirate's cannons had no tier, so they never dropped: Rusty to Wave Cannon are T0-T5 as their
# numbers stand, and Nature, Shade and Fragment Cannon (copies of Wave Cannon with Vitality and
# Wisdom) become T6-T8, climbing as the quivers do (cannons sit about 15 damage above the quiver of
# their tier). Doom Cannon stays untiered.
# id -> {speed, lifetime, rof, damage: (min, max), wave: (amplitude, frequency), pierce, mp,
# stats: {stat: amount}, activates: [(effect, {attribute: value it has}, {new values} or None)],
# untier, tier}
DEF, SPD, VIT, WIS, DEX = "21", "22", "26", "27", "28"
UNTIERED_ABILITIES = [
    "1940s Orb", "Azure Trap", "Bile Poison", "Bloodorc Quiver", "Class Test", "Cloak of Dimensions",
    "Cloak of the Phantom", "Ent Ancient Quiver", "Flaming Blade", "Fury Orb", "Helm of the Holy Warrior",
    "Helm of the Leviathan General", "Lightning Fury", "Lizard Cut", "Magma Shield", "Mobstopper Trap",
    "Prism of the Bloody Surprise", "Prism of the Soul Stealer", "Quiver of Artemis", "Roar of the Raging Dragon",
    "Scepter of Vorv", "Scepter of the Grand Sorcerer", "Seal of Mass Power", "Seal of War", "Shadow Slash",
    "Shield of Death", "Skull of the Twizted", "Skull of the Undead King", "Snake's Fang", "Souleater Skull",
    "Space Deterioration Spell", "Spell of the Eternal Void", "Strike of Mass Devestation", "Sword Shield",
    "Tome of the Ancients", "Tome of the Fountains", "Toothpaste Poison", "Oceanic Curse", "Oceanic Nova",
]
CANNON_TIERS = ["Rusty Cannon", "Pirate Cannon", "Reinforced Cannon", "Iron Cannon", "Heavy Cannon",
                "Wave Cannon", "Nature Cannon", "Shade Cannon", "Fragment Cannon"]
# From T5 a quiver fires three arrows; a cannon fires one ball that hits about as hard as two of them
# (the ability balance pass; T5-T8 had one arrow's damage).
CANNON_TUNING = {
    "Wave Cannon": {"damage": (580, 700)},
    "Nature Cannon": {"damage": (670, 790), "mp": 80, "stats": {VIT: 2, WIS: 2, DEX: 6}},
    "Shade Cannon": {"damage": (760, 880), "mp": 85, "stats": {VIT: 3, WIS: 3, DEX: 7}},
    "Fragment Cannon": {"damage": (870, 940), "mp": 90, "stats": {VIT: 4, WIS: 4, DEX: 8}},
}
# Druid pearls cost what a tome of the same tier does (they cost 40 up to 160).
PEARL_MP = {"Magic Pearl": 50, "Pearl of Alleviation": 70, "Pearl of Silent Life": 75,
            "Pearl of the Infinite Shadows": 80, "Pearl of Wasted Blood": 85, "Pearl of the Blessed Heavens": 90,
            "Pearl of Total Chaos": 95}
# Every betterSkillys ability gives +20/+40/+60 health and mana at T6/T7/T8: the Doomed Realms
# classes' tiered abilities do too (at least that much; a pearl's own health stays if it's more).
ABILITY_TIER_HP_MP = {6: 20, 7: 40, 8: 60}
# Each shot of the T0-T5 Summoner summons (their projectile's Damage; they were 20-65, a few
# percent of a staff and next to nothing against defense, as summons' shots don't scale with
# Attack). At port_doomed_allies.SUMMON_FIRE_SLOWDOWN a summon does 23-100 damage a second up to the
# Dragon's 125 and the Oryx's 158, and the six a Summoner can keep out add about a quarter to a half
# of a staff of the matching tier.
SUMMON_DAMAGE = {"Bunny Summon": 70, "Snake Summon": 100, "Wasp Summon": 140, "Wolf Summon": 180,
                 "Drake Summon": 100, "Wisp Summon": 100}
# How big the Summoner's summons are drawn (Size; a sprite's pixels are drawn 5 x Size / 100 screen
# pixels, so an 8x8 at 100 is a player). Doomed Realms drew the Dragon and the Oryxes, 16x16
# sprites at 150, three times a player's width, and with six out they hid the boss. Now the T0-T2
# ones are about half a player, T3-T5 three quarters and T6-T8 a little over a player, each at a
# whole number of screen pixels per sprite pixel (40, 60, 80, 100) so they stay crisp.
SUMMON_SIZE = {"Bunny Summon": 60, "Snake Summon": 60, "Wasp Summon": 60, "Wolf Summon": 80,
               "Drake Summon": 60, "Wisp Summon": 80, "Sheep Summon": 80, "Dragon Summon": 60,
               "Oryx Summon": 60, "Dragonholder Summon": 60}
# ...and their shots, at most this (no Size is 100; the Dragon's and Oryxes' waves were 150).
SUMMON_SHOT_SIZE = 80
# The Doomed Realms classes' tiered abilities drop in the bags the betterSkillys abilities of their
# tier use (T0-T8), T7 and T8 Soulbound as theirs are. Slot types as betterSkillys numbers them.
DOOMED_ABILITY_SLOTS = {30, 32, 37, 126, 128, 130, 5000, 5005}
# The Blademaster: every Dual Blades shot ignores defense (<ArmorPiercing/>), and its T3-T8 jackets
# also give it Attack for a while (StatBoostSelf, which shows the Att Boost status): (amount, seconds).
DUAL_BLADES_SLOT = "36"
JACKET_BOOSTS = {"Lightweight Jacket": (3, 3), "Murderous Jacket": (5, 4), "Invincibility Jacket": (7, 5),
                 "Jacket of Sorrows": (10, 6), "Skyfall Jacket": (12, 7), "Jacket of Bloodrut Nature": (15, 8)}
ABILITY_BAGS = [1, 1, 1, 2, 2, 4, 4, 9, 9]
GEAR_TUNING = {
    **{oid: {"speed": 200, "lifetime": 250} for oid in (
        "Bronze Daggers", "Invert Blades", "Dusky Blades", "Duo Blades", "Silver Blades", "Half Blades",
        "Mithril Blades", "Infinity Blades", "Emerald Malevolence Blades")},
    "Rage Claw Blades": {"speed": 200, "lifetime": 250, "rof": "1", "damage": (105, 168)},
    "Eldricht Blades": {"speed": 200, "lifetime": 250, "stats": {DEF: 2}},
    "Master Blades": {"speed": 200, "lifetime": 250, "stats": {DEF: 4, SPD: 2}},
    "Fists of Broken Bones": {"untier": True},
    "Ring of the Abyss": {"stats": {"0": 100, "20": 10, DEF: 6}},  # was 500 health and 24 Defense, then 120, 14 and 10
    # was 405-1045 at rate 1, its shots barely waving
    "Soul Tipped Dirk": {"damage": (120, 230), "rof": "0.9", "wave": ("0.6", "1")},
    "Staff of Tainted Light": {"damage": (95, 105)},  # was 190-210: a better Staff of Extreme Prejudice
    # the staves' T15, a step over the T14's 75-115 (was 120-180), and T15's +10 Defense
    "Staff of Endless Energy": {"damage": (100, 135), "stats": {DEF: 10}},
    # the T16 armours: their kind's T15 a tenth better, rounded up (health and mana to the next 5)
    "Sphinx Leather Armor": {"stats": {DEF: 24, DEX: 8, SPD: 5}},             # Leviathan Armor 21, 7, 4
    "Armor of the Pyramid": {"stats": {DEF: 31, "0": 85, VIT: 9}},            # Annihilation Armor 28, 75, 8
    "Robe of the Nile": {"stats": {"3": 105, "20": 7, DEF: 19, WIS: 9, "0": 55}},  # Robe of the Ancient Intellect 95, 6, 17, 8, 48
    # was 380-410, and T15: a very rare drop of its own, not a step of the staves' tiers (or the forge
    # would make it from two T14 staves)
    "Eternal Sword": {"damage": (180, 180), "pierce": True, "untier": True},
    "Platinum Sword": {"lifetime": 800},  # range 8 (was 5)
    # T20 and T22, which no tier reaches: no source for now, listed with the future additions
    "Oryx Tooth Sword": {"untier": True},
    "Sword of the Angelic Soul": {"untier": True},
    # the abilities pass (SETUP-LOCAL.md, "Untiered abilities")
    "Cloak of Dimensions": {"stats": {VIT: 20}},  # was +3 Vitality
    # the T6 poison's numbers (Baneserpent Poison; was 2,500 over 10.5 s)
    "Tome of Poison": {"activates": [("PoisonGrenade", {}, {"impactDamage": "230", "totalDamage": "1035", "duration": "4.5"})]},
    "Tome of Noble Assault": {"activates": [("TomeDamage", {}, {"totalDamage": "800"})]},  # was 500
    # the Oryx 5 weapons pass: a chance with each shot of a helper (BehaviorDb.Allies.cs): the sword's
    # dark orb, about a fifth more damage, and the staff's halo
    "Sword of Dark Enchantment": {"add_xml": [
        "<OnPlayerShootActivate cooldown=\"2\" objectId=\"Dark Enchantment Orb\" proc=\"0.15\">Pet</OnPlayerShootActivate>",
        "<ExtraTooltipData><EffectInfo name=\"Dark Orb\" description=\"Each shot: a 15% chance (at most every 2 seconds) of a dark orb that zaps the nearest enemy for 220-280 every 0.5 seconds for 4 seconds\" /></ExtraTooltipData>",
    ]},
    # its shots weave twice, as the other staves' do (they weaved five times: jittery)
    "Staff of Heavens": {"wave": ("0.5", "2"), "add_xml": [
        "<OnPlayerShootActivate cooldown=\"6\" objectId=\"Elysian Halo\" proc=\"0.1\">Pet</OnPlayerShootActivate>",
        "<ExtraTooltipData><EffectInfo name=\"Halo\" description=\"Each shot: a 10% chance (at most every 6 seconds) of a halo that circles you for 5 seconds, firing two Holy Orbs at the nearest enemy every second for 120-150 each\" /></ExtraTooltipData>",
    ]},
    # the Oryx UT pass: 2,000 poison damage (was 15,000), cursing instead of dazing, and its thrower
    # silenced 2 s; Oryx 4's alone (no shop)
    "Doom Poison": {"activates": [("PoisonGrenade", {}, {"totalDamage": "2000", "condEffect": "Curse"})],
                    "add_activates": [("ConditionEffectSelf", {"effect": "Silenced", "duration": "2"})],
                    "no_shop": True},
    "Sword Shield": {"damage": (175, 225)},  # was 350-450
    # invulnerable 2 s (was 10), and its healing and damaging auras 6 s (were 15: the T8 seal's last 4.5)
    "Seal of the Fallen Angel": {"activates": [("ConditionEffectSelf", {"effect": "Invulnerable"}, {"duration": "2"}),
                                               ("ConditionEffectAura", {"effect": "Healing"}, {"duration": "6"}),
                                               ("ConditionEffectAura", {"effect": "Damaging"}, {"duration": "6"})]},
    "Tome of Shining Stars": {"activates": [("ConditionEffectAura", {"effect": "Invulnerable"}, None)]},
    # was 4,000 damage to 20 targets (paralysing 3 s, then 1 s): the T8 scepter does 460 to 5
    "Scepter of Zeus": {"activates": [("Lightning", {}, {"condDuration": "1", "totalDamage": "650", "maxTargets": "6"})]},
    "Power of the Fountains": {"tier": "7"},
    # the item pass of 2.1.2: Doomed Realms' Elder drops still had its own far stronger scale. Each
    # is now a little over the best tiered piece of its kind (T16 armour, T8 ring, T15 weapon),
    # keeping its own mix of stats.
    # armour (was): Raw Hope 175 Def, 20 Spd, 25 Dex, 15 Wis, 10 Vit; Angel's Hope 200 Def, 250 HP,
    # 25 Att, 30 Vit; Wings of Natural Bloodlust 155 Def, 20 Att, 30 Wis, 200 MP; Armor of the Leaf
    # 122 Def, 22 Dex, 20 Att; Robe of Silk Water 116 Def, 20 Att, 22 Wis, 180 MP; Momento of the
    # Fire Dragon 128 Def, 14 Vit, 160 HP; Scaled Robe 100 Def, 20 Vit, 15 Wis, 150 MP, 100 HP;
    # America Armor 37 Def, 20 Vit, 20 Wis, 100 HP, 100 MP
    "Raw Hope": {"stats": {DEF: 25, SPD: 6, DEX: 9, WIS: 4, VIT: 4}},
    "Angel's Hope": {"stats": {DEF: 32, "0": 90, "20": 4, VIT: 10}},
    "Wings of Natural Bloodlust": {"stats": {DEF: 20, "20": 8, WIS: 10, "3": 110}},
    "Armor of the Leaf": {"stats": {DEF: 24, DEX: 9, "20": 5}},
    "Robe of Silk Water": {"stats": {DEF: 19, "20": 7, WIS: 10, "3": 100}},
    "Momento of the Fire Dragon": {"stats": {DEF: 31, VIT: 10, "0": 90}},
    "Scaled Robe": {"stats": {VIT: 5, DEF: 19, WIS: 8, "3": 100, "0": 60}},
    "America Armor": {"stats": {DEF: 30, VIT: 10, WIS: 8, "0": 80, "3": 60}},
    # rings (was): Coral Medallion 15 Dex, 13 Vit, 7 Spd, 20 Wis; Oceans Gift 29 Def, 2 Dex
    "Coral Medallion": {"stats": {DEX: 10, VIT: 8, SPD: 6, WIS: 10}},
    "Oceans Gift": {"stats": {DEF: 18, DEX: 4}},
    # weapons (was): Lightning Bow 100-955 (5.8 times the T15 bow), Sword of the Abyss 350-500 (2.3
    # times the T15 sword); now about 1.15 times
    "Lightning Bow": {"damage": (60, 190)},
    "Sword of the Abyss": {"damage": (200, 265)},
    # abilities (was): Helm of the Pumpkin 40 Def, 10 Spd, 10 Dex (the T8 helm gives 14 Def)
    "Helm of the Pumpkin": {"stats": {DEF: 14, SPD: 6, DEX: 6}},
    # the ability balance pass: the T6 and T7 scrolls and the T7 oceanic spell had no stat bonuses
    "Dragon Scroll": {"stats": {VIT: 2, WIS: 2}},
    "Oryx Scroll": {"stats": {VIT: 3, WIS: 3}},
    "Tidal Wave": {"stats": {VIT: 3, WIS: 3}},
    "Obsidian Blades": {"speed": 200, "lifetime": 250, "rof": "1.3"},  # was 2: more than the T7 and T8 blades
    **{oid: {"mp": mp} for oid, mp in PEARL_MP.items()},
    **{oid: {"untier": True} for oid in UNTIERED_ABILITIES},
    **{oid: {"tier": str(t), **CANNON_TUNING.get(oid, {})} for t, oid in enumerate(CANNON_TIERS)},
}


# Class stats changed from Doomed Realms': class -> {stat element: (starting value, highest)}. The
# class balance pass (the wiki's Gear statistics, class totals: every custom class between the
# standard classes' lowest and highest scores):
# - Sharklord: highest Attack 70 (was 80, start 13 was 15), Defense 25 (was 30), health 750 (was 800);
# - King: highest Attack 75 and Dexterity 70 (were 85 and 85), the Wizard's damage with a tome;
# - Blademaster: highest Attack 45 (was 35) and Defense 25 (was 15);
# - Adept: highest Attack 60 (was 50) and Defense 25 (was 20);
# - Snowman: highest Defense 25 (was 20) and Dexterity 70 (was 65);
# - Mechanic: highest health 700 (was 650);
# - Ascendant: highest Attack 70 (was 60).
CLASS_STATS = {
    "Sharklord": {"Attack": ("13", "70"), "Defense": ("2", "25"), "MaxHitPoints": ("120", "750")},
    "King": {"Attack": ("12", "75"), "Dexterity": ("15", "70")},
    "Blademaster": {"Attack": ("10", "45"), "Defense": ("0", "25")},
    "Adept": {"Attack": ("10", "60"), "Defense": ("0", "25")},
    "Snowman (Doomed)": {"Defense": ("0", "25"), "Dexterity": ("15", "70")},
    "Mechanic": {"MaxHitPoints": ("100", "700")},
    "Ascendant": {"Attack": ("12", "70")},
}


def tune_classes(dr):
    for oid, stats in CLASS_STATS.items():
        o = dr.get(oid)
        if o is None:
            continue
        for tag, (start, top) in stats.items():
            e = o.find(tag)
            if e is not None:
                e.text, e.attrib["max"] = start, top


def tune_gear(dr):
    for oid, t in GEAR_TUNING.items():
        o = dr.get(oid)
        if o is None:
            continue
        p = o.find("Projectile")
        if "speed" in t:
            p.find("Speed").text = str(t["speed"])
        if "lifetime" in t:
            p.find("LifetimeMS").text = str(t["lifetime"])
        if "damage" in t:
            p.find("MinDamage").text, p.find("MaxDamage").text = map(str, t["damage"])
        if "rof" in t:
            o.find("RateOfFire").text = t["rof"]
        # (effect, the attributes it has, its new attributes or None to remove it)
        for effect, has, new in t.get("activates", []):
            for a in [a for a in o.findall("Activate") if (a.text or "").strip() == effect
                      and all(a.get(k) == v for k, v in has.items())]:
                if new is None:
                    o.remove(a)
                else:
                    a.attrib.update(new)
        # Activates added after the last one: (effect, its attributes)
        for effect, attrs in t.get("add_activates", []):
            acts = o.findall("Activate")
            e = ET.Element("Activate", attrs)
            e.text = effect
            o.insert(list(o).index(acts[-1]) + 1 if acts else len(o), e)
        # elements added before the BagType (or at the end), as XML text
        for text in t.get("add_xml", []):
            at_bag = list(o).index(o.find("BagType")) if o.find("BagType") is not None else len(o)
            o.insert(at_bag, ET.fromstring(text))
        if t.get("no_shop") and o.find("Shop") is not None:
            o.remove(o.find("Shop"))
        if t.get("pierce") and p.find("ArmorPiercing") is None:
            ET.SubElement(p, "ArmorPiercing")
        if "wave" in t:  # the shots weave side to side: (amplitude in tiles, frequency)
            p.find("Amplitude").text, p.find("Frequency").text = t["wave"]
        if "mp" in t:
            o.find("MpCost").text = str(t["mp"])
        if t.get("untier") and o.find("Tier") is not None:
            o.remove(o.find("Tier"))
        if "tier" in t:
            if o.find("Tier") is None:
                o.insert(list(o).index(o.find("Item")) + 1, ET.Element("Tier"))
            o.find("Tier").text = t["tier"]
        at = list(o).index(o.find("BagType")) if o.find("BagType") is not None else len(o)
        for stat, amount in t.get("stats", {}).items():
            e = next((a for a in o.findall("ActivateOnEquip") if a.get("stat") == stat), None)
            if e is not None:
                e.set("amount", str(amount))
                continue
            e = ET.Element("ActivateOnEquip", {"stat": stat, "amount": str(amount)})
            e.text = "IncrementStat"
            o.insert(at, e)
            at += 1
    for oid, damage in SUMMON_DAMAGE.items():
        o = dr.get(oid)
        if o is not None:
            for p in o.findall("Projectile"):  # the Wisp has three, one per colour
                p.find("Damage").text = str(damage)
    for oid, size in SUMMON_SIZE.items():
        o = dr.get(oid)
        if o is None:
            continue
        if o.find("Size") is None:
            at = next((i for i, e in enumerate(o) if e.tag.endswith("Texture")), 0)  # Texture, RemoteTexture...
            o.insert(at + 1, ET.Element("Size"))
        o.find("Size").text = str(size)
        for p in o.findall("Projectile"):  # no Size is 100
            if p.find("Size") is None:
                p.insert(list(p).index(p.find("Damage")) + 1, ET.Element("Size"))
            p.find("Size").text = str(min(int(p.find("Size").text or 100), SUMMON_SHOT_SIZE))
    for oid, (amount, secs) in JACKET_BOOSTS.items():
        o = dr.get(oid)
        if o is not None and not any(a.text == "StatBoostSelf" for a in o.findall("Activate")):
            acts = o.findall("Activate")
            e = ET.Element("Activate", {"stat": "20", "amount": str(amount), "duration": str(secs)})
            e.text = "StatBoostSelf"
            o.insert(list(o).index(acts[-1]) + 1 if acts else len(o), e)
    for o in dr.values():
        slot = SLOT_TYPE_REMAP.get(o.findtext("SlotType") or "", o.findtext("SlotType") or "")
        tier = o.findtext("Tier")
        if not (slot.isdigit() and int(slot) in DOOMED_ABILITY_SLOTS and tier and tier.isdigit()
                and int(tier) in ABILITY_TIER_HP_MP):
            continue
        bonus = ABILITY_TIER_HP_MP[int(tier)]
        at = list(o).index(o.find("BagType")) if o.find("BagType") is not None else len(o)
        for stat in ("0", "3"):
            e = next((a for a in o.findall("ActivateOnEquip") if a.get("stat") == stat), None)
            if e is not None:
                e.set("amount", str(max(bonus, int(e.get("amount")))))
                continue
            e = ET.Element("ActivateOnEquip", {"stat": stat, "amount": str(bonus)})
            e.text = "IncrementStat"
            o.insert(at, e)
            at += 1
    for o in dr.values():
        if o.findtext("SlotType") == DUAL_BLADES_SLOT and o.find("Projectile") is not None                 and o.find("Projectile").find("ArmorPiercing") is None:
            ET.SubElement(o.find("Projectile"), "ArmorPiercing")
    for o in dr.values():
        slot = SLOT_TYPE_REMAP.get(o.findtext("SlotType") or "", o.findtext("SlotType") or "")
        tier = o.findtext("Tier")
        if (slot.isdigit() and int(slot) in DOOMED_ABILITY_SLOTS and tier and tier.isdigit() and int(tier) <= 8
                and o.find("BagType") is not None):
            o.find("BagType").text = str(ABILITY_BAGS[int(tier)])
            if int(tier) >= 7 and o.find("Soulbound") is None:
                ET.SubElement(o, "Soulbound")


# The Oryx chain's incantations: each unlocks the locked portal the Oryx before it drops, in that
# Oryx's dungeon only (Player.AEChangeObject), instead of opening its dungeon anywhere like a key.
# (incantation, the dungeon it works in, the portal it opens, what it says); the locked portal it
# opens is the stage's own where INCANTATION_LOCKS names one (tools/build_hash_cellar.py draws them)
# Oryx 4's dungeon is Oryx's Inferno (the Doomed Realms "Hash Cellar"; port_doomed_worlds.WORLD_NAMES),
# entered by a burnt Sanctuary portal (tools/build_hash_cellar.py); Oryx 5's is Oryx's Elysium (the
# "Doomed Cellar"), entered by a gold one (tools/build_elysium.py).
INCANTATIONS = {
    "Hash Cellar Incantation": ("Oryx's Sanctuary", "Oryx's Inferno Portal",
                                "Unlocks the portal Oryx leaves behind in his Sanctuary, leading down into Oryx's Inferno."),
    "Doomed Cellar Incantation": ("Oryx's Inferno", "Oryx's Elysium Portal",
                                  "Unlocks the portal Oryx leaves behind in his Inferno, leading up to his last stand in Oryx's Elysium."),
}
INCANTATION_LOCKS = {"Hash Cellar Incantation": "Locked Oryx's Inferno Portal",
                     "Doomed Cellar Incantation": "Locked Oryx's Elysium Portal"}


def fix_doomed_data(dr):
    """Mistakes in the Doomed Realms data, fixed before porting."""
    tune_gear(dr)
    tune_classes(dr)
    for oid, bag in BAG_TYPES.items():
        o = dr.get(oid)
        if o is not None and o.find("BagType") is None and o.find("Description") is not None:
            o.insert(list(o).index(o.find("Description")) + 1, ET.Element("BagType"))
        if o is not None and o.find("BagType") is not None:
            o.find("BagType").text = str(bag)
    for oid, (stat, stored, text) in ITEM_STORE.items():
        o = dr.get(oid)
        if o is None:
            continue
        for a in o.iter("Activate"):
            if a.text == "IncrementStat" and a.get("stat") == stat:
                a.set("storeAmount", stored)
        if o.find("Description") is not None:
            o.find("Description").text = text
    for tag, oids in (("Admin", ADMIN_ITEMS), ("Reward", REWARD_ITEMS), ("Unobtainable", UNOBTAINABLE_ITEMS),
                      ("Soulbound", SOULBOUND_ITEMS)):
        for oid in oids:
            o = dr.get(oid)
            if o is not None and o.find(tag) is None:
                o.insert(list(o).index(o.find("SlotType")) + 1, ET.Element(tag))
    for oid, text in DESCRIPTIONS.items():
        o = dr.get(oid)
        if o is not None and o.find("Description") is not None:
            o.find("Description").text = text
    for oid, (area, portal, text) in INCANTATIONS.items():
        o = dr.get(oid)
        if o is None or o.find("Activate") is None:
            continue
        act = o.find("Activate")
        act.attrib.clear()
        act.set("radius", "25")
        act.set("targetId", INCANTATION_LOCKS.get(oid, "Locked Wine Cellar Portal"))
        act.set("newId", portal)
        act.set("onlyInArea", area)
        act.set("customString", portal.replace(" Portal", "") + " unlocked by")
        act.text = "ChangeObject"
        if o.find("Description") is not None:
            o.find("Description").text = text
    key = dr.get("Elder Beach Key")
    if key is not None:  # it opened the Elder Jungle
        for a in key.iter("Activate"):
            if a.get("id") == "Elder Jungle Portal":
                a.set("id", "Elder Beach Portal")
        desc = key.find("Description")
        if desc is not None:
            desc.text = "A key which opens an entrance to the Elder Beach Zone."
    key = dr.get("Elder Davy Key")
    if key is not None and key.find("Description") is not None:
        key.find("Description").text = "A key which opens an entrance to Elder Davy Jones' Locker."


# Doomed Realms balanced its Elder monsters for its own, much stronger characters: 10 to 700 times
# the health and 10 or more times the damage of the regular monster. Here an Elder monster gets
# these multiples of its regular version's stats instead.
ELDER_HP = 2.0
ELDER_DEFENSE = 10      # added
ELDER_DAMAGE = 1.3
ELDER_XP = 2            # <XpBonus>: the experience (and so fame) of a kill, and its per-kill cap


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
            o.find("MaxHitPoints").text = str(int(round(hp * ELDER_HP)))
        defense = number(regular, "Defense")
        if o.find("Defense") is not None:
            o.find("Defense").text = str(int(round((defense or 0) + ELDER_DEFENSE)))
        # a quest where the regular one is (the quest arrow leads to it)
        for tag in ("Quest", "Level"):
            if regular.find(tag) is not None and o.find(tag) is None:
                ET.SubElement(o, tag).text = regular.find(tag).text
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


def fix_portals_and_keys(converted, portal_world):
    """Portals point at their ported dungeon; keys create them (Doomed Realms "Create" is "CreatePortal")."""
    portal_ids = {o.get("id") for o in converted if o.findtext("Class") == "Portal"}
    for o in converted:
        if o.findtext("Class") == "Portal" and o.get("id") in portal_world:
            dn = o.find("DungeonName")
            if dn is None:
                dn = ET.SubElement(o, "DungeonName")
            dn.text = portal_world[o.get("id")]
            if o.find("IntergamePortal") is None:
                ET.SubElement(o, "IntergamePortal")
        for a in o.iter("Activate"):
            if a.text == "Create" and (a.get("id") in portal_ids or a.get("id") in portal_world):
                a.text = "CreatePortal"


GROUNDS_SERVER = os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealmsGrounds.xml")
GROUNDS_CLIENT = os.path.join(CLIENT_ASSETS, "custom", "xmls", "DoomedRealmsGrounds.xml")
GROUND_TYPE_START = 0xE000


def write_grounds(dr_root, bs, worlds, types, sheets):
    """Doomed Realms ground tiles the ported maps use and betterSkillys lacks."""
    import port_doomed_worlds as pw
    dr_files = glob.glob(os.path.join(dr_root, "db", "data", "*.xml"))
    bs_files = [f for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True)
                if os.path.abspath(f) != os.path.abspath(GROUNDS_SERVER)]
    dr_grounds = pw.ground_names(dr_files)
    bs_ids = set(pw.ground_names(bs_files).values())
    used = {dr_grounds[g] for (_, _, _, maps) in worlds.values() for wm in maps for (g, _, _, _) in wm["dict"] if g in dr_grounds}
    wanted = sorted(used - bs_ids)
    elems = {}
    for f in dr_files:
        text = re.sub(r"<\?xml[^>]*\?>", "", open(f, encoding="utf-8", errors="replace").read())
        try:
            root = ET.fromstring("<r>" + text + "</r>")
        except ET.ParseError:
            continue
        for g in root.iter("Ground"):
            if g.get("id") in wanted:
                elems[g.get("id")] = g
    out = []
    for gid in wanted:
        key = "ground:" + gid
        if key not in types:
            used_t = {int(v, 16) for k, v in types.items() if k.startswith("ground:")}
            t_ = GROUND_TYPE_START
            while t_ in used_t:
                t_ += 1
            types[key] = "0x%04X" % t_
        out.append(convert(elems[gid], types[key], sheets, {}))
    with open(TYPES_JSON, "w") as f:
        json.dump(dict(sorted(types.items(), key=lambda kv: kv[1])), f, indent=1)
    for path in (GROUNDS_SERVER, GROUNDS_CLIENT):
        root = ET.Element("GroundTypes")
        for g in out:
            root.append(g)
        ET.indent(root, space="\t")
        with open(path, "w", encoding="utf-8", newline="\r\n") as f:
            f.write("<!-- Generated by tools/port_doomed.py from the Doomed Realms ground tiles. Do not edit. -->\n")
            f.write(ET.tostring(root, encoding="unicode") + "\n")
    hook_client_grounds()
    return len(out)


def hook_client_grounds():
    as_file = os.path.join(CLIENT_ASSETS, "custom", "xmls", "DoomedRealmsGrounds.as")
    if not os.path.exists(as_file):
        with open(as_file, "w", newline="\r\n") as f:
            f.write("package kabam.rotmg.assets.custom.xmls\n{\nimport mx.core.ByteArrayAsset;\n\n"
                    '[Embed(source="DoomedRealmsGrounds.xml", mimeType="application/octet-stream")]\n'
                    "    public class DoomedRealmsGrounds extends ByteArrayAsset\n    {\n    }\n}\n")
    data_as = os.path.join(CLIENT_ASSETS, "EmbeddedData.as")
    t = open(data_as, encoding="utf-8-sig").read()
    if "doomedRealmsGrounds" not in t:
        t = t.replace("        private static const customProj:Class = CustomProj;",
                      "        private static const customProj:Class = CustomProj;\n"
                      "        private static const doomedRealmsGrounds:Class = DoomedRealmsGrounds;", 1)
        t = t.replace("            new customGrounds()", "            new customGrounds(),\n            new doomedRealmsGrounds()", 1)
        if "new doomedRealmsGrounds()" not in t:
            sys.exit("Could not hook DoomedRealmsGrounds.xml into EmbeddedData.as")
        open(data_as, "w", encoding="utf-8", newline="").write(t)


def write_xml(objs, path):
    root = ET.Element("Objects")
    root.text = "\n"
    for o in objs:
        root.append(o)
    ET.indent(root, space="\t")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("<!-- Generated by tools/port_doomed.py from the Doomed Realms server. Do not edit. -->\n")
        f.write(ET.tostring(root, encoding="unicode"))
        f.write("\n")


def export_dr_images(dr_root):
    out = os.path.join(BUILD, "images")
    if glob.glob(os.path.join(out, "*_Embeds_*.png")):
        return out
    swf = os.path.join(dr_root, "build", "client", "renamed.swf")
    if not os.path.exists(swf):
        swf = os.path.join(dr_root, "localhost.swf-new.swf")
    ffdec = os.path.join(dr_root, "tools", "ffdec", "ffdec.jar")
    print("Exporting Doomed Realms sprite sheets with FFDec ...")
    subprocess.check_call(["java", "-jar", ffdec, "-export", "image", out, swf], stdout=subprocess.DEVNULL)
    return out


def write_assets(sheets, remotes, images_dir):
    if os.path.isdir(CLIENT_IMAGES):
        shutil.rmtree(CLIENT_IMAGES)
    os.makedirs(CLIENT_IMAGES)
    embeds, lines = [], []

    def embed(name, src):
        shutil.copyfile(src, os.path.join(CLIENT_IMAGES, name + ".png"))
        embeds.append('      [Embed(source="images/%s.png")]\n      private static const %s:Class;\n' % (name, name))

    exported = {}
    for f in glob.glob(os.path.join(images_dir, "*_Embeds_*Embed_.png")):
        exported[re.sub(r"^\d+_Embeds_|Embed_\.png$", "", os.path.basename(f))] = f
    for embed_name, cls in OBFUSCATED_EMBEDS.items():
        for f in glob.glob(os.path.join(images_dir, "*_%s.png" % cls)):
            exported[embed_name] = f
    done = set()
    for sheet in sorted(sheets):
        image, mask, kind, a = SHEETS[sheet]
        for n in (image, mask):
            if n and n not in done:
                if n not in exported:
                    sys.exit("Doomed Realms sheet image %s not found in %s" % (n, images_dir))
                embed(n, exported[n])
                done.add(n)
        if kind == "set":
            lines.append('         AssetLibrary.addImageSet("%s", new %s().bitmapData, %d, %d);' % (dr_name(sheet), image, a[0], a[1]))
        else:
            lines.append('         AnimatedChars.add("%s", new %s().bitmapData, %s, %d, %d, %d, %d, AnimatedChar.%s);'
                         % (dr_name(sheet), image, "new %s().bitmapData" % mask if mask else "null", a[0], a[1], a[2], a[3], a[4]))
    for rid, use in sorted(remotes.items()):
        name = remote_sheet(rid)
        embed(name, remote_file(rid))
        w, h = png_size(remote_file(rid))
        # remote character sprites are mostly a strip of 7 frames, as the old client loaded them;
        # a single picture (Cookie, Reaper, Aceaku's Pet) is one frame, not 7 slivers of it
        strip = w == 7 * h
        tile_w = w // 7 if strip else w
        if use["animated"]:
            lines.append('         AnimatedChars.add("%s", new %s().bitmapData, null, %d, %d, %d, %d, AnimatedChar.%s);'
                         % (name, name, tile_w, h, w, h, "RIGHT" if use["right"] else "DOWN"))
        if use["static"]:
            # an item that shares a character's strip (e.g. a pet generator), or whose own sprite is
            # such a strip (the Elder Draconis Key), shows its first frame
            lines.append('         AssetLibrary.addImageSet("%s", new %s().bitmapData, %d, %d);' % (name, name, tile_w, h))

    with open(CLIENT_ASSETS_AS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py: sprite sheets of the content ported from Doomed Realms.\n")
        f.write("package kabam.rotmg.assets.doomed\n{\n")
        f.write("   import com.company.assembleegameclient.util.AnimatedChar;\n")
        f.write("   import com.company.assembleegameclient.util.AnimatedChars;\n")
        f.write("   import com.company.util.AssetLibrary;\n\n")
        f.write("   public class DoomedAssets\n   {\n")
        f.write("".join(embeds))
        f.write("\n      public static function register():void\n      {\n")
        f.write("\n".join(lines) + "\n")
        f.write("      }\n   }\n}\n")
    return len(done) + len(remotes)


def hook_client():
    """One-time edits so the client loads DoomedRealms.xml and registers the sheets."""
    if not os.path.exists(CLIENT_XML_AS):
        with open(CLIENT_XML_AS, "w", newline="\r\n") as f:
            f.write("package kabam.rotmg.assets.custom.xmls\n{\nimport mx.core.ByteArrayAsset;\n\n"
                    '[Embed(source="DoomedRealms.xml", mimeType="application/octet-stream")]\n'
                    "    public class DoomedRealms extends ByteArrayAsset\n    {\n    }\n}\n")
    data_as = os.path.join(CLIENT_ASSETS, "EmbeddedData.as")
    t = open(data_as, encoding="utf-8-sig").read()
    if "doomedRealms" not in t:
        t = t.replace("        private static const customProj:Class = CustomProj;",
                      "        private static const customProj:Class = CustomProj;\n"
                      "        private static const doomedRealms:Class = DoomedRealms;", 1)
        t = t.replace("            new customItems(),", "            new customItems(),\n            new doomedRealms(),", 1)
        if "doomedRealms()" not in t:
            sys.exit("Could not hook DoomedRealms.xml into EmbeddedData.as")
        open(data_as, "w", encoding="utf-8", newline="").write(t)
    loader = os.path.join(ROOT, "client", "src", "com", "company", "assembleegameclient", "util", "AssetLoader.as")
    t = open(loader, encoding="utf-8-sig").read()
    if "DoomedAssets" not in t:
        t = t.replace("import kabam.rotmg.assets.EmbeddedAssets;", "import kabam.rotmg.assets.EmbeddedAssets;\nimport kabam.rotmg.assets.doomed.DoomedAssets;", 1)
        anchor = '      AnimatedChars.add("ancientRuinsChars16x16"'
        i = t.index(anchor)
        end = t.index("\n", i) + 1
        t = t[:end] + "      DoomedAssets.register(); // content ported from Doomed Realms\n" + t[end:]
        open(loader, "w", encoding="utf-8", newline="").write(t)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dr", required=True, help="Doomed Realms repo root (the folder with db/ and wserver/)")
    ap.add_argument("--plan", action="store_true", help="only print what would be ported")
    ap.add_argument("--fetch-remote", action="store_true", help="download missing RemoteTexture images first")
    args = ap.parse_args()

    dr = load_objects(glob.glob(os.path.join(args.dr, "db", "data", "*.xml")))
    # the Elder dungeons' tiles and the Elder monsters Doomed Realms didn't make, first, so the
    # rest of the port sees them
    import make_elder
    print("Elder dungeons: %d grounds, %d objects, %d sprites." % make_elder.build(set(dr)))
    bs = load_objects([f for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True)
                       if os.path.abspath(f) != os.path.abspath(SERVER_XML)])
    fix_doomed_data(dr)
    print("Elder monsters rebalanced from their regular version: %d" % rebalance_elders(dr, bs))
    # dungeons: what their maps place, their portals, what the minigames spawn and what the
    # monsters' behaviours spawn
    import port_doomed_worlds as pw
    import port_doomed_monsters as pm
    worlds = pw.world_maps(args.dr, set(dr))
    map_objs = {o for (_, _, _, maps) in worlds.values() for wm in maps for (_, o, _, _) in wm["dict"] if o}
    map_objs |= MINIGAME_SPAWNS
    # the Elder versions of what the Elder dungeons' (regular) behaviours spawn
    map_objs |= make_elder.referenced_elders(set(dr))
    # what the regular maps of those dungeons place (Abyss of Demons' Native White Demon). Without
    # this the port took them only while the Elder map still used the regular version, and
    # make_elder copies only what the last port wrote, so each run undid the one before.
    map_objs |= make_elder.regular_map_objects()
    portal_world = {pid: worlds[cls][0] for pid, cls in pw.portal_worlds().items()}
    dr_by_type = {int(o.get("type"), 16): oid for oid, o in dr.items() if o.get("type")}
    blocks = pm.load_blocks(args.dr)
    # Elder monsters Doomed Realms gave no behaviour of their own use their regular version's,
    # spawning the Elder versions of its minions
    elder_base = {}
    for t_, oid in dr_by_type.items():
        base = dr.get(oid[len("Elder "):]) if oid.startswith("Elder ") else None
        if t_ not in blocks and base is not None and dr[oid].findtext("Class") == "Character":
            elder_base[oid] = int(base.get("type"), 16)

    def elderize(oid):
        return "Elder " + oid if "Elder " + oid in dr else oid

    behaviour_refs = {}
    for t_, oid in dr_by_type.items():
        src = t_ if t_ in blocks else elder_base.get(oid)
        if src in blocks:
            refs = {dr_by_type[r] for r in pm.referenced_types(args.dr, [src], blocks) if r in dr_by_type}
            if src != t_:
                refs = {elderize(r) for r in refs}
            if refs:
                behaviour_refs[oid] = refs
    # betterSkillys monsters it never gave a behaviour (the realm heroes such as Red Demon and
    # Phoenix Lord, and their minions) use the Doomed Realms one of the same name, and bring over
    # what that spawns
    handwritten = pm.handwritten_behaviours()
    bs_unbehaved = {oid: int(dr[oid].get("type"), 16) for oid, o in bs.items()
                    if o.find("Enemy") is not None and oid not in handwritten
                    and oid in dr and dr[oid].get("type") and int(dr[oid].get("type"), 16) in blocks}
    map_objs |= {dr_by_type[r] for r in pm.referenced_types(args.dr, bs_unbehaved.values(), blocks)
                 if r in dr_by_type and dr_by_type[r] not in bs}
    chosen, skipped = select(dr, bs, extra=map_objs | set(portal_world), allowed_portals=set(portal_world),
                             behaviour_refs=behaviour_refs)
    classes = {}
    for o in chosen.values():
        classes[o.findtext("Class")] = classes.get(o.findtext("Class"), 0) + 1
    remotes = remote_ids(chosen)
    print("Doomed Realms objects to port: %d %s" % (len(chosen), sorted(classes.items())))
    print("Left for later (Doomed Realms class abilities): %d" % len(skipped))
    print("Remote textures needed: %d" % len(remotes))
    if args.plan:
        for oid, why in sorted(skipped.items()):
            print("  later: %s (%s)" % (oid, why))
        return
    if args.fetch_remote:
        fetch_remote(remotes)
    # objects whose remote sprite no longer exists borrow the sprite of a betterSkillys object with
    # the same base name ("Oryx the Mad God 5" -> "Oryx the Mad God"); otherwise they are left out
    for oid, o in list(chosen.items()):
        if not any(not os.path.exists(remote_file(r)) for r in remote_ids({oid: o})):
            continue
        base = re.sub(r"[\s\d']+$", "", (o.findtext("DisplayId") or oid).replace("Lil' ", "")).strip()
        animated = o.findtext("Class") == "Character"
        donor = bs.get(SPRITE_DONORS.get(oid, "")) or next(
            (b for bid, b in sorted(bs.items()) if bid.startswith(base) and
             b.find("AnimatedTexture" if animated else "Texture") is not None), None)
        if donor is None:
            continue
        o = copy.deepcopy(o)
        for parent in list(o.iter()):
            for child in list(parent):
                if child.tag == "RemoteTexture":
                    idx = list(parent).index(child)
                    parent.remove(child)
                    parent.insert(idx, copy.deepcopy(donor.find("AnimatedTexture" if animated else "Texture")))
        chosen[oid] = o
        print("  %s: sprite unavailable, using %s's" % (oid, donor.get("id")))
    # leave out objects whose remote sprite no longer exists, and the items that give them
    dropped = {oid for oid, o in chosen.items()
               if any(not os.path.exists(remote_file(r)) for r in remote_ids({oid: o}))}
    while True:
        more = {oid for oid, o in chosen.items() if oid not in dropped and references(o) & dropped}
        if not more:
            break
        dropped |= more
    for oid in sorted(dropped):
        print("  left out (sprite unavailable): %s" % oid)
        del chosen[oid]

    types = assign_types(chosen)
    sheets, remote_used = set(), {}
    converted = [convert(o, types[oid], sheets, remote_used)
                 for oid, o in sorted(chosen.items(), key=lambda kv: types[kv[0]])]
    fix_class_references(converted, dr, bs, types)
    fix_portals_and_keys(converted, portal_world)
    grounds = write_grounds(args.dr, bs, worlds, types, sheets)
    write_xml(converted, SERVER_XML)
    write_xml(converted, CLIENT_XML)
    n = write_assets(sheets, remote_used, export_dr_images(args.dr))
    hook_client()
    print("Wrote %d objects to %s and the client copy, %d sprite images." % (len(converted), SERVER_XML, n))
    import port_doomed_pets
    dr_pets, bs_pets = port_doomed_pets.write_pet_behaviors(args.dr, dr, chosen, bs, types)
    print("Wrote %s: %d Doomed Realms pets, %d betterSkillys pets given a follow."
          % (port_doomed_pets.PET_BEHAVIORS_CS, dr_pets, bs_pets))
    import port_doomed_allies
    n = port_doomed_allies.write_ally_behaviors(args.dr, chosen)
    print("Wrote %s: %d turrets, summons and pearl substitutes." % (port_doomed_allies.ALLY_BEHAVIORS_CS, n))

    def final_id(oid):
        if oid in bs or oid in types:
            return oid
        if oid + " (Doomed)" in types:
            return oid + " (Doomed)"
        return oid

    def known(oid):
        return oid in bs or oid in types

    dr_grounds = pw.ground_names(glob.glob(os.path.join(args.dr, "db", "data", "*.xml")))
    missing = sorted({o for o in map_objs if not known(final_id(o))})
    if missing:
        print("  map objects with no definition (left as is): %s" % ", ".join(missing))
    ids = pw.write_worlds(worlds, dr_grounds, final_id)
    print("Wrote %d dungeons to %s and %s (%d new ground tiles)." % (len(ids), pw.WORLDS_DIR, pw.WORLDS_XML, grounds))

    pets = {a.get("objectId") for o in chosen.values() for a in o.iter("Activate") if a.text == "PermaPet"}
    ally_types = set(port_doomed_allies.parse_ally_behaviors(args.dr))
    def type_to_id(t_):
        if t_ not in dr_by_type:
            return None
        oid = final_id(dr_by_type[t_])
        return oid if known(oid) else None

    def elder_type_to_id(t_):
        oid = type_to_id(t_)
        return elderize(oid) if oid and known(elderize(oid)) else oid

    monsters, aliases = {}, {}
    for oid, o in chosen.items():
        t_ = int(o.get("type"), 16)
        if o.findtext("Class") != "Character" or oid in pets or t_ in ally_types:
            continue
        if t_ in blocks:
            monsters[oid] = (t_, type_to_id)
        elif elder_base.get(oid) in blocks:
            monsters[oid] = (elder_base[oid], elder_type_to_id)
        elif oid in elder_base and oid[len("Elder "):] in bs:
            aliases[oid] = oid[len("Elder "):]  # only betterSkillys has a behaviour for the regular one
    for oid, t_ in bs_unbehaved.items():
        monsters.setdefault(oid, (t_, type_to_id))
    # Elder monsters make_elder.py made, with no behaviour of their own, share their regular one's
    for oid, o in bs.items():
        regular = oid[len("Elder "):]
        if (oid.startswith("Elder ") and o.find("Enemy") is not None and oid not in handwritten
                and oid not in monsters and oid not in aliases and (regular in monsters or regular in handwritten)):
            aliases[oid] = regular

    _, n = pm.write_monsters(args.dr, monsters, aliases)
    print("Wrote %s: %d monsters (%d betterSkillys ones that had none), %d Elder monsters sharing their regular version's behaviour."
          % (pm.DEFS_CS, n, len(bs_unbehaved), len(aliases)))
    import port_doomed_economy
    port_doomed_economy.write_economy(args.dr, dr, bs, final_id, known)


if __name__ == "__main__":
    main()
