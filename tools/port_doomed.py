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
TEXTURE_OVERRIDES = {"Oryx the Mad God 4": ("hashCellarOryx16x16", 0)}
# projectiles drawn anew: id -> {projectile id: object}. Oryx 4's first phase shoots hellfire.
PROJECTILE_OVERRIDES = {"Oryx the Mad God 4": {"2": "Hellfire Bolt", "4": "Hellfire Star"}}
# names for players: each Oryx after the Chamber is named for his theme
DISPLAY_OVERRIDES = {"Oryx the Mad God 4": "Oryx the Infernal Mad God", "Hash Cellar Incantation": "Oryx's Inferno Incantation",
                     "Doomed Cellar Incantation": "Oryx's Elysium Incantation"}


# Loot bags (<BagType>): Doomed Realms numbered its bags differently, so its rarest items dropped
# in brown or blue bags. Here 6 is the white bag (announced to the server) and 7 the bag of special
# consumables. These are the Oryx chain's top drops (BehaviorDb.OryxDoomed.cs).
BAG_TYPES = {
    "Ring of Oryx": 6, "Oryx Scale Armor": 6, "Oryx Hide Armor": 6, "Oryx Hide Robe": 6,
    "Oryx Tooth Sword": 6, "Doom Dirk": 6, "Flying Doom Daggers": 6, "Tome of Noble Assault": 6,
    "Doom Poison": 6, "Sword of Dark Enchantment": 6,
    "Potion of Oryx": 7, "Potion of Power": 7,
    "Potion of Max Life": 6,
}


# What an IncrementStat item puts into potion storage when its stat is already at the class
# maximum (storeAmount, in stat points; it was the whole amount): (stat, stored, description).
# The Potion of Max Life still fills health to the maximum (+1000), but would have stored 200
# life potions; it stores 30 health (6 potions).
ITEM_STORE = {
    "Potion of Max Life": ("0", "30", "A rare potion that permanently maxes health. At full maximum health, it adds 30 health to your potion storage instead."),
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
# id -> {speed, lifetime, rof, damage: (min, max), mp, stats: {stat: amount}, untier, tier}
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
CANNON_TUNING = {
    "Nature Cannon": {"damage": (335, 395), "mp": 80, "stats": {VIT: 2, WIS: 2, DEX: 6}},
    "Shade Cannon": {"damage": (380, 440), "mp": 85, "stats": {VIT: 3, WIS: 3, DEX: 7}},
    "Fragment Cannon": {"damage": (435, 470), "mp": 90, "stats": {VIT: 4, WIS: 4, DEX: 8}},
}
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
        "Obsidian Blades", "Mithril Blades", "Infinity Blades", "Emerald Malevolence Blades")},
    "Rage Claw Blades": {"speed": 200, "lifetime": 250, "rof": "1", "damage": (105, 168)},
    "Eldricht Blades": {"speed": 200, "lifetime": 250, "stats": {DEF: 2}},
    "Master Blades": {"speed": 200, "lifetime": 250, "stats": {DEF: 4, SPD: 2}},
    "Fists of Broken Bones": {"untier": True},
    "Power of the Fountains": {"tier": "7"},
    **{oid: {"untier": True} for oid in UNTIERED_ABILITIES},
    **{oid: {"tier": str(t), **CANNON_TUNING.get(oid, {})} for t, oid in enumerate(CANNON_TIERS)},
}


# Class stats changed from Doomed Realms': class -> {stat element: (starting value, highest)}. The
# Sharklord's highest Attack is 70 (was 80), its start 13 (was 15) so a level 20 one is as far
# along as before; it still gains 1-2 a level.
CLASS_STATS = {"Sharklord": {"Attack": ("13", "70")}}


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
    for oid, (amount, secs) in JACKET_BOOSTS.items():
        o = dr.get(oid)
        if o is not None and not any(a.text == "StatBoostSelf" for a in o.findall("Activate")):
            acts = o.findall("Activate")
            e = ET.Element("Activate", {"stat": "20", "amount": str(amount), "duration": str(secs)})
            e.text = "StatBoostSelf"
            o.insert(list(o).index(acts[-1]) + 1 if acts else len(o), e)
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
