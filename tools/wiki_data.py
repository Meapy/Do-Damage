"""Reads the game's own data for the website's wiki (tools/build_wiki.py): the server's XML (items,
monsters, portals, classes, worlds), the behaviours (who drops which portal, key and item), the
Nexus Bazaar's prices and the chat commands. Nothing here is typed in by hand except the words
for the commands (COMMAND_HELP in build_wiki.py), so the wiki follows the game as it changes.

    python tools/wiki_data.py [dungeons|classes|commands|sets|pets|gear]   # print what it found
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "source")
XML = os.path.join(SRC, "Shared", "resources", "xml")
WORLDS = os.path.join(SRC, "Shared", "resources", "worlds")
LOGIC = os.path.join(SRC, "WorldServer", "logic")
COMMANDS = os.path.join(SRC, "WorldServer", "core", "commands")
MERCHANTS = os.path.join(SRC, "WorldServer", "core", "objects", "vendors", "MerchantLists.cs")

# SlotType numbers (ItemConstants): what goes where
WEAPONS = {1: "Sword", 2: "Dagger", 3: "Bow", 8: "Wand", 17: "Staff", 24: "Katana"}
ABILITIES = {4: "Tome", 5: "Shield", 11: "Spell", 12: "Seal", 13: "Cloak", 15: "Quiver", 16: "Helm",
             18: "Poison", 19: "Skull", 20: "Trap", 21: "Orb", 22: "Prism", 23: "Scepter", 25: "Shuriken"}
ARMORS = {6: "Leather armor", 7: "Heavy armor", 14: "Robe"}
RING = 9
SLOT_NAMES = {**WEAPONS, **ABILITIES, **ARMORS}
SLOT_NAMES[RING] = "Ring"
# the Doomed Realms slot types, named as the clients name them (ItemConstants)
SLOT_NAMES.update({27: "Wakizashi", 28: "Lute", 30: "Pearl", 32: "Turret", 36: "Dual Blades", 37: "Jacket",
                   126: "Halo", 128: "Summon Scroll", 130: "Fountain", 200: "Martial Art",
                   5000: "Oceanic Spell", 5005: "Cannon"})

STATS = {0: "Max HP", 3: "Max MP", 20: "Attack", 21: "Defense", 22: "Speed", 26: "Vitality", 27: "Wisdom",
         28: "Dexterity"}


def _text(el, tag, default=None):
    child = el.find(tag)
    return child.text.strip() if child is not None and child.text else default


def _int(value, default=0):
    try:
        return int(value, 0) if isinstance(value, str) else int(value)
    except (TypeError, ValueError):
        try:
            return int(float(value))
        except (TypeError, ValueError):
            return default


class Data:
    def __init__(self):
        self.objects = {}       # id -> dict (every Object in the server's XML)
        self.elements = {}      # id -> the XML element
        self.worlds = {}        # world id -> dict
        self._load_xml()
        self._load_worlds()
        self.inits = self._load_behaviours()

    # --- XML ---------------------------------------------------------------------------------
    def _load_xml(self):
        for path in sorted(glob.glob(os.path.join(XML, "**", "*.xml"), recursive=True)):
            if os.path.basename(path) in ("Dungeons.xml", "Worlds.xml") or path.endswith("Worlds.xml"):
                continue
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue
            for el in root.iter("Object"):
                oid = el.get("id")
                if not oid:
                    continue
                o = {
                    "id": oid,
                    "type": _int(el.get("type")),
                    "file": os.path.relpath(path, XML).replace(os.sep, "/"),
                    "class": _text(el, "Class", ""),
                    "name": _text(el, "DisplayId") or oid,
                    "desc": (_text(el, "Description", "") or "").replace("\\n", "\n"),
                    "slot": _int(_text(el, "SlotType"), -1),
                    "tier": _int(_text(el, "Tier"), -1),
                    "bag": _int(_text(el, "BagType"), 0),
                    "item": el.find("Item") is not None,
                    "enemy": el.find("Enemy") is not None,
                    "quest": el.find("Quest") is not None,
                    "oryx": el.find("Oryx") is not None,
                    "soulbound": el.find("Soulbound") is not None,
                    "consumable": el.find("Consumable") is not None,
                    "hp": _int(_text(el, "MaxHitPoints"), 0),
                    "defense": _int(_text(el, "Defense"), 0),
                    "level": _int(_text(el, "Level"), 0),
                    "dungeon": _text(el, "DungeonName"),
                    "activates": [(a.text or "").strip() for a in el.findall("Activate")],
                    "activate_attrs": [dict(a.attrib, effect=(a.text or "").strip()) for a in el.findall("Activate")],
                    "admin_only": el.find("AdminOnly") is not None,
                    "player": el.find("Player") is not None,
                }
                self.objects[oid] = o
                self.elements[oid] = el

    def name(self, oid):
        o = self.objects.get(oid)
        return o["name"] if o else oid

    def _load_worlds(self):
        for fn in ("Dungeons.xml", os.path.join("custom", "DoomedRealmsWorlds.xml")):
            root = ET.parse(os.path.join(XML, fn)).getroot()
            for el in root.iter("World"):
                wid = el.get("id")
                rl = el.find("RandomLayout")
                self.worlds[wid] = {
                    "id": wid,
                    "file": fn,
                    "map": _text(el, "MapJM") or _text(el, "Map"),
                    "random": rl is not None,
                    "goals": [g.text for g in rl.findall("Goal")] if rl is not None else [],
                    "dungeon": el.find("Dungeon") is not None,
                    "name": _text(el, "DisplayName") or wid,
                }

    def map_objects(self, world):
        """The objects a world's map places (its .jm's tile dictionary)."""
        path = world.get("map")
        if not path:
            return []
        full = os.path.join(WORLDS, path.replace("/", os.sep))
        if not os.path.isfile(full):
            # maps are looked up without regard to case
            want = full.lower()
            for f in glob.glob(os.path.join(WORLDS, "**", "*.jm"), recursive=True):
                if f.lower() == want:
                    full = f
                    break
            else:
                return []
        with open(full, encoding="utf-8-sig") as f:
            try:
                jm = json.load(f)
            except ValueError:
                return []
        found = []
        for tile in jm.get("dict", []):
            for obj in tile.get("objs") or []:
                if obj.get("id") and obj["id"] not in found:
                    found.append(obj["id"])
        return found

    # --- behaviours --------------------------------------------------------------------------
    def _load_behaviours(self):
        """{monster id: [source text of each hand-written .Init block]}; the generated Doomed
        Realms monsters go in self.doomed ({monster id: its definition}), used only where no
        hand-written behaviour replaces it (as InitDoomed does)."""
        inits = {}
        start = re.compile(r'\.Init(Doomed)?\(\s*(E\(|Elder\()?\s*"([^"]+)"')
        doomed_names = {}
        for path in glob.glob(os.path.join(LOGIC, "**", "*.cs"), recursive=True):
            if os.path.basename(path) == "DoomedMonsters.cs":
                continue
            with open(path, encoding="utf-8-sig") as f:
                text = f.read()
            marks = list(start.finditer(text))
            for i, m in enumerate(marks):
                end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
                block = text[m.start():end]
                name = m.group(3)
                if m.group(1):
                    d = re.search(r'DoomedMonsterDefs\.(M_\w+)', block)
                    if d:
                        doomed_names[name] = d.group(1)
                    continue
                inits.setdefault(name, []).append(block)
                # E("x") builds "x" and "Elder x" from one block when the file takes an elder flag
                if m.group(2) == "E(":
                    inits.setdefault("Elder " + name, []).append(block)
        self.doomed = {}
        defs = os.path.join(LOGIC, "doomed", "DoomedMonsters.cs")
        if os.path.isfile(defs):
            with open(defs, encoding="utf-8-sig") as f:
                text = f.read()
            heads = list(re.finditer(r'public static readonly Definition (M_\w+)\s*=', text))
            bodies = {}
            for i, m in enumerate(heads):
                end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
                bodies[m.group(1)] = text[m.end():end]
            for name, mid in doomed_names.items():
                if name not in inits and mid in bodies:
                    self.doomed[name] = bodies[mid]
        return inits

    def monsters(self):
        return set(self.inits) | set(self.doomed)

    def drops(self, monster, _seen=None):
        """[(kind, id, chance)] a monster drops: kind is 'portal', 'item' or 'tier'. Loot a boss
        leaves in a chest or balloon it turns into or spawns counts as the boss's."""
        seen = _seen or set()
        seen.add(monster)
        out = []
        for block in self.inits.get(monster, []):
            for m in re.finditer(r'(?:TransformOnDeath|Spawn|TossObject)\(\s*"([^"]*(?:Loot|Chest|Balloon|Treasure)[^"]*)"', block):
                if m.group(1) not in seen and self.objects.get(m.group(1), {}).get("class") != "Portal":
                    out.extend(x for x in self.drops(m.group(1), seen) if x[0] != "portal")
        for block in self.inits.get(monster, []):
            elder = monster.startswith("Elder ")
            for m in re.finditer(r'DropPortalOnDeath\(\s*(?:target:\s*)?(?:elder\s*\?\s*"([^"]+)"\s*:\s*)?"([^"]+)"\s*(?:,\s*(?:probability:\s*)?([^,)]+))?', block):
                portal = m.group(1) if (m.group(1) and elder) else m.group(2)
                chance = m.group(3)
                c = _chance(chance)
                if chance and re.match(r'\s*elder\s*\?', chance):
                    two = re.findall(r'[0-9.]+', chance)
                    c = float(two[0] if elder else two[1]) if len(two) >= 2 else c
                out.append(("portal", portal, c))
            for m in re.finditer(r'(?:new Spawn|new TossObject)\(\s*"([^"]+)"', block):
                o = self.objects.get(m.group(1))
                if o and o["class"] == "Portal":
                    out.append(("portal", m.group(1), 1.0))
            for m in re.finditer(r'ItemLoot\(\s*"([^"]+)"\s*,\s*([0-9.]+)', block):
                out.append(("item", m.group(1), float(m.group(2))))
            for m in re.finditer(r'TierLoot\(\s*(\d+)\s*,\s*ItemType\.(\w+)', block):
                out.append(("tier", (m.group(2), int(m.group(1))), None))
        block = self.doomed.get(monster)
        if block:
            for m in re.finditer(r'DeathPortal\(\s*T\("([^"]+)"\)\s*,\s*([0-9.]+)', block):
                out.append(("portal", m.group(1), min(1.0, float(m.group(2)) / 100)))
            for m in re.finditer(r'Tuple\.Create\(\s*([0-9.]+)\s*,\s*\(ILoot\)\s*new ItemLoot\("([^"]+)"', block):
                out.append(("item", m.group(2), float(m.group(1))))
            for m in re.finditer(r'new TierLoot\(\s*(\d+)\s*,\s*ItemType\.(\w+)', block):
                out.append(("tier", (m.group(2), int(m.group(1))), None))
        return out

    def drops_index(self):
        """{portal or item id: [(monster, chance)]}, from every behaviour."""
        idx = {}
        for monster in sorted(self.monsters()):
            for kind, oid, chance in self.drops(monster):
                if kind in ("portal", "item"):
                    lst = idx.setdefault(oid, [])
                    if not any(m == monster for m, _ in lst):
                        lst.append((monster, chance))
        return idx

    # --- the Nexus Bazaar --------------------------------------------------------------------
    def bazaar(self):
        """{item id: (price in fame, shop name)} from MerchantLists.cs."""
        with open(MERCHANTS, encoding="utf-8-sig") as f:
            text = f.read()
        out = {}
        for listname, shop in (("StatPotions", "Stat Potions"), ("Consumables", "Consumables"),
                               ("Specials", "Specials")):
            m = re.search(listname + r'\s*=\s*new List<ISellableItem>\s*\{(.*?)\};', text, re.S)
            for name, price in re.findall(r'ShopItem\("([^"]+)",\s*(\d+)\)', m.group(1) if m else ""):
                out[name] = (int(price), shop)
        for arr, shop in (("DungeonKeyPrices", "Dungeon Keys"), ("DoomedKeyPrices", "Elder and Doomed Keys")):
            m = re.search(arr + r'\s*=\s*\{(.*?)\};', text, re.S)
            prices = re.findall(r'\("([^"]+)",\s*(\d+)\)', m.group(1) if m else "")
            for name, price in prices:
                out[name] = (int(price), shop)
        m = re.search(r'EnchantedKeyDungeons\s*=\s*\{(.*?)\};', text, re.S)
        for d in re.findall(r'"([^"]+)"', m.group(1) if m else ""):
            base = out.get(d + " Key")
            if base:
                out[d + " Enchanted Key"] = (base[0] * 2 + 250, "Enchanted Keys")
        m = re.search(r'petGenerators\.Add\(new ShopItem\(i\.ObjectId,\s*(\d+)\)', text)
        self.pet_generator_price = int(m.group(1)) if m else None
        return out

    # --- items -------------------------------------------------------------------------------
    def keys(self):
        """{portal id: [key item ids]}: items whose CreatePortal makes that portal."""
        out = {}
        for oid, o in self.objects.items():
            if not o["item"]:
                continue
            for a in o["activate_attrs"]:
                if a["effect"] == "CreatePortal" and a.get("id"):
                    out.setdefault(a["id"], []).append(oid)
        return out

    def portals(self):
        """{world id: [portal ids]} of every portal object leading to a world."""
        out = {}
        for oid, o in self.objects.items():
            if o["class"] == "Portal" and o["dungeon"]:
                out.setdefault(o["dungeon"], []).append(oid)
        return out

    def classes(self):
        """The playable classes, in the order of their XML (regular, then Doomed Realms)."""
        out = []
        for oid, o in self.objects.items():
            el = self.elements[oid]
            if o["class"] != "Player" or o["admin_only"]:
                continue
            slots = [_int(s) for s in (_text(el, "SlotTypes", "") or "").split(",")[:4]]
            equip = [_int(s) for s in (_text(el, "Equipment", "") or "").split(",")[:4]]
            stats = {}
            for tag in ("MaxHitPoints", "MaxMagicPoints", "Attack", "Defense", "Speed", "Dexterity",
                        "HpRegen", "MpRegen"):
                s = el.find(tag)
                if s is not None:
                    stats[tag] = (_int(s.text), _int(s.get("max")))
            by_type = {v["type"]: k for k, v in self.objects.items()}
            out.append({
                "id": oid,
                "name": o["name"],
                "desc": o["desc"],
                "file": o["file"],
                "doomed": "DoomedRealms" in o["file"],
                "weapon": SLOT_NAMES.get(slots[0], "slot %d" % slots[0]) if slots else "",
                "ability": SLOT_NAMES.get(slots[1], "slot %d" % slots[1]) if len(slots) > 1 else "",
                "armor": SLOT_NAMES.get(slots[2], "slot %d" % slots[2]) if len(slots) > 2 else "",
                "slots": slots,
                "start": [by_type.get(t) for t in equip if t not in (-1, 0xFFFF)],
                "stats": stats,
            })
        out.sort(key=lambda c: c["doomed"])
        return out

    def sets(self):
        """Equipment sets: [{name, pieces: [item ids], bonus: {pieces worn: [lines]}}]."""
        by_type = {o["type"]: oid for oid, o in self.objects.items() if o["item"]}
        out = []
        for path in sorted(glob.glob(os.path.join(XML, "**", "*.xml"), recursive=True)):
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue
            for el in root.iter("EquipmentSet"):
                pieces = []
                for sp in el.findall("Setpiece"):
                    oid = by_type.get(_int(sp.get("itemtype"), -1))
                    if oid and oid not in pieces:
                        pieces.append(oid)
                bonus = {}
                for tag, worn in (("ActivateOnEquip2", 2), ("ActivateOnEquip3", 3), ("ActivateOnEquipAll", 0)):
                    for b in el.findall(tag):
                        if (b.text or "").strip() == "IncrementStat":
                            stat = STATS.get(_int(b.get("stat")), "stat " + str(b.get("stat")))
                            bonus.setdefault(worn, []).append("%+d %s" % (_int(b.get("amount")), stat))
                        elif (b.text or "").strip() == "ChangeSkin":
                            bonus.setdefault(worn, []).append("a new look")
                out.append({"id": el.get("id"), "pieces": pieces, "bonus": bonus})
        return out

    def pets(self):
        """Pet generators with their PermaPet numbers: [(item, pet, dps, hp, hpms, mp, mpms, size,
        effects)]; hp and mp are what one heal restores, as written ("10-60" or "76"), empty for
        none; size is Small, Medium, Large or Admin (empty for none) and effects what else it does."""
        out = []
        for oid, o in self.objects.items():
            for a in o["activate_attrs"]:
                if a["effect"] == "PermaPet":
                    out.append({
                        "item": oid, "name": o["name"], "pet": a.get("objectId"),
                        # heals are "min-max" or one number
                        "dps": _int(a.get("petDps")), "hp": (a.get("petHp") or "").strip(),
                        "hpms": _int(a.get("petHpMs")), "mp": (a.get("petMp") or "").strip(),
                        "mpms": _int(a.get("petMpMs")), "doomed": "DoomedRealms" in o["file"],
                        "size": (a.get("petSize") or "").strip(),
                        "effects": [e for e in (a.get("petEffects") or "").split("; ") if e],
                    })
        return out


    def boss_health(self, monster):
        """The health a boss really has: its BossStats when it sets one, else its XML."""
        for block in self.inits.get(monster, []):
            m = re.search(r'new BossStats\(\s*(\d+)', block)
            if m:
                return int(m.group(1))
        o = self.objects.get(monster)
        return o["hp"] if o else 0

    def incantations(self):
        """{open portal id: [(item id, [dungeons it works in])]}: items that unlock a locked
        portal (the XML ChangeObject effect on the Locked Wine Cellar Portal)."""
        out = {}
        for oid, o in self.objects.items():
            for a in o["activate_attrs"]:
                if a["effect"] == "ChangeObject" and a.get("targetId") == "Locked Wine Cellar Portal" and a.get("newId"):
                    areas = [x.strip() for x in a.get("onlyInArea", "").split(",") if x.strip()]
                    lst = out.setdefault(a["newId"], [])
                    if not any(i == oid for i, _ in lst):
                        lst.append((oid, areas))
        return out

    def gear(self):
        """Every piece of equipment (weapons, abilities, armor and rings): [{id, name, slot, kind,
        category, group, tier, texture, el, ...}]. `category` is where the classes wear its slot
        type (weapons, abilities, armor or rings) and `group` what kind of item it is, as the
        client colours it (tier_edge): admin, legendary, ascended, ethereal, tiered, set, untiered,
        or test for the test items."""
        cats = {RING: "rings"}
        for c in self.classes():
            for i, cat in enumerate(("weapons", "abilities", "armor")):
                if i < len(c["slots"]):
                    cats.setdefault(c["slots"][i], cat)
        loot = tier_loot_types()
        out = []
        for oid, o in self.objects.items():
            if not o["item"] or o["slot"] not in SLOT_NAMES or o["consumable"]:
                continue
            el = self.elements[oid]
            cat = cats.get(o["slot"])
            if cat is None:  # a slot no class wears: by what the item does
                cat = "weapons" if el.find("Projectile") is not None and el.find("Usable") is None else "abilities"
            if "Test" in o["file"] and el.find("Admin") is None:
                group = "test"
            else:
                group = next((t.lower() for t in ("Admin", "Legendary", "Ascended", "Ethereal") if el.find(t) is not None),
                             None) or ("tiered" if o["tier"] >= 0 else "set" if el.get("setType") else "untiered")
            doomed = 0xD000 <= o["type"] <= 0xDFFF
            # the random tier drops (Loots.Initialize): no Doomed Realms items but the tiered gear of
            # the Doomed Realms classes' own slots
            drops_as = (loot["types"].get(o["slot"]) if o["tier"] >= 0 and group == "tiered"
                        and (not doomed or o["slot"] in loot["doomed"]) else None)
            out.append(dict(o, kind=SLOT_NAMES[o["slot"]], category=cat, group=group, el=el, doomed=doomed,
                            texture=self.texture(oid), tier_loot=drops_as))
        return out

    def texture(self, oid):
        """(sheet name, index) of an object's sprite, or None."""
        el = self.elements.get(oid)
        if el is None:
            return None
        tex = el.find("Texture")
        if tex is None:
            tex = el.find("AnimatedTexture")
        if tex is None or not _text(tex, "File"):
            return None
        return (_text(tex, "File"), _int(_text(tex, "Index"), 0))

    def summons(self):
        """Items that summon a helper beside you (the Pet effect, Player.AEPet): [{item, summons}]."""
        out = []
        for oid, o in self.objects.items():
            if o["item"]:
                for a in o["activate_attrs"]:
                    if a["effect"] == "Pet" and a.get("objectId"):
                        out.append({"item": oid, "summons": a["objectId"]})
                        break
        return out

    def yard_pets(self):
        """The pet yard's pets (EmbeddedData_PetsCXML): [{id, name, family, rarity, ability, skin}]. The
        pet's own Texture is a stand-in: what it looks like is its DefaultSkin's sprite."""
        out = []
        for oid, o in self.objects.items():
            if o["class"] == "Pet":
                el = self.elements[oid]
                out.append({"id": oid, "name": o["name"], "family": _text(el, "Family", ""),
                            "rarity": _text(el, "Rarity", ""), "ability": _text(el, "FirstAbility", ""),
                            "skin": _text(el, "DefaultSkin")})
        return out

    def bazaar_pets(self):
        """The pet generators the Nexus Bazaar sells (MerchantLists.InitDyes: usable items named
        "...Generator...")."""
        return {oid for oid, el in self.elements.items()
                if self.objects[oid]["item"] and "Generator" in oid and el.find("InvUse") is not None}

    def gear_items(self, slot_type):
        """What /gear equips in a slot of this type (GearCommand.StarterItem)."""
        max_tier = gear_rules()["max_tier"]
        cands = [o for o in self.objects.values() if o["item"] and o["slot"] == slot_type and o["tier"] >= 0
                 and not o["consumable"]]
        if not cands:
            return None
        tiers = {o["tier"] for o in cands}
        top = min(tiers)
        while top + 1 in tiers:
            top += 1
        target = min(top // 2, max_tier)
        pick = [o for o in cands if o["tier"] <= target]
        pick.sort(key=lambda o: (-o["tier"], o["type"]))
        return pick[0] if pick else None


def _read(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read()


def gear_rules():
    """/gear's limits, from GearCommand."""
    src = _read(os.path.join(COMMANDS, "doomed", "DoomedEconomyCommands.cs"))
    lvl = re.search(r'class GearCommand.*?MAX_LEVEL\s*=\s*(\d+)', src, re.S)
    tier = re.search(r'MAX_TIER\s*=\s*(\d+)', src)
    boosters = re.search(r'BOOSTERS\s*=\s*\{(.*?)\}', src)
    return {"max_level": int(lvl.group(1)) if lvl else 20, "max_tier": int(tier.group(1)) if tier else 8,
            "boosters": re.findall(r'"([^"]+)"', boosters.group(1)) if boosters else []}


def xp_boost():
    """How much an XP Booster multiplies experience by (DamageCounter)."""
    src = _read(os.path.join(LOGIC, "DamageCounter.cs"))
    m = re.search(r'XPBoostTime\s*!=\s*0\)\s*\n?\s*playerXp\s*\*=\s*([0-9.]+)f?', src)
    return float(m.group(1)) if m else None


def support_bot():
    """The Nexus Support Bot's numbers, tips and dungeon list (NexusGuide.cs)."""
    src = _read(os.path.join(SRC, "WorldServer", "core", "worlds", "impl", "NexusGuide.cs"))

    def num(name):
        return float(re.search(name + r'\s*=\s*([0-9.]+)', src).group(1))
    lines = re.search(r'string\[\] Lines\s*=\s*\{(.*?)\};', src, re.S).group(1)
    dungeons = re.search(r'string\[\] DungeonPortals\s*=\s*\{(.*?)\};', src, re.S).group(1)
    return {
        "gifts": int(num("GIFT_COUNT")),
        "gift_minutes": num("GIFT_INTERVAL") / 60,
        "talk_seconds": num("TALK_INTERVAL"),
        "greet_range": num("GREET_RANGE"),
        "cleared_seconds": num("CLEARED_WAIT"),
        "tips": [t.replace('\\"', '"') for t in re.findall(r'"((?:[^"\\]|\\.)*)"', lines)],
        "portals": re.findall(r'"([^"]+)"', dungeons),
    }


def realm():
    """The realm's Godlands gods, its events and how many events close it (RealmManager.cs)."""
    src = _read(os.path.join(SRC, "WorldServer", "core", "worlds", "RealmManager.cs"))
    zones = {}
    for m in re.finditer(r'TerrainType\.(\w+),\s*Tuple\.Create\(\s*\d+,\s*new \[\]\s*\{(.*?)\}\)', src, re.S):
        zones[m.group(1)] = re.findall(r'Tuple\.Create\("([^"]+)"', m.group(2))
    events = re.search(r'_events\s*=\s*new List<.*?>\(\)\s*\{(.*?)\};', src, re.S)
    # EventsToClose(players) => Math.Clamp(players + offset, min, max): the count scales with the
    # realm's players. Older code closed after a fixed `_EventCount == N`.
    scaled = re.search(r'EventsToClose\(int \w+\)\s*=>\s*Math\.Clamp\(\w+\s*\+\s*(\d+),\s*(\d+),\s*(\d+)\)', src)
    if scaled:
        offset, low, high = (int(g) for g in scaled.groups())
        few = low - offset  # the most players that still need only `low`
        close = ("%d events with 1 to %d players, one more for each extra player, up to %d" % (low, few, high)
                 if few > 1 else "%d to %d events, depending on the players in it" % (low, high))
    else:
        fixed = re.search(r'_EventCount\s*==\s*(\d+)', src)
        close = "%s events" % fixed.group(1) if fixed else "enough events"
    return {
        "gods": zones.get("Mountains", []),
        "events": re.findall(r'Tuple\.Create\("([^"]+)"', events.group(1)) if events else [],
        "close_after": close,
        "zones": zones,
    }


def account():
    """New accounts and what they can buy (init.xml, DbAccount)."""
    root = ET.parse(os.path.join(SRC, "Shared", "resources", "data", "init.xml")).getroot()
    na = root.find("NewAccounts")
    acc = _read(os.path.join(SRC, "Shared", "database", "account", "DbAccount.cs"))

    def const(name):
        return int(re.search(name + r'\s*=\s*(\d+)', acc).group(1))
    return {
        "char_slots": _int(_text(na, "MaxCharSlot"), 1),
        "vault_chests": _int(_text(na, "VaultCount"), 1),
        "slot_cost": _int(_text(na, "SlotCost"), 1000),
        "slot_currency": "fame" if _text(na, "SlotCurrency") == "1" else "gold",
        "classes_unlocked": na.find("ClassesUnlocked") is not None,
        "vault_chest_price": _int(_text(root, "VaultChestPrice"), 0),
        "potion_stack": _int(_text(root, "MaxStackablePotions"), 6),
        "storage_base": const("PotionStorageBase"),
        "storage_step": const("PotionStoragePerLevel"),
        "storage_cost": const("PotionStorageUpgradeCost"),
    }


def tier_loot_types():
    """Which tier drops a slot type comes in (MobDrops.cs): {"types": {slot type: "Weapon" |
    "Ability" | "Armor" | "Ring"}, "doomed": {the Doomed Realms classes' slot types}}."""
    src = _read(os.path.join(LOGIC, "loot", "MobDrops.cs"))
    types = {}
    for name, kind in (("WeaponT", "Weapon"), ("AbilityT", "Ability"), ("ArmorT", "Armor"), ("RingT", "Ring")):
        m = re.search(name + r'\s*=\s*new int\[\]\s*\{([^}]*)\}', src)
        for n in re.findall(r'\d+', m.group(1) if m else ""):
            types[int(n)] = kind
    m = re.search(r'DoomedClassSlots\s*=\s*new HashSet<int>\s*\{([^}]*)\}', src)
    return {"types": types, "doomed": {int(n) for n in re.findall(r'\d+', m.group(1) if m else "")}}


def shop_items():
    """What the /shop shops sell: [(shop, item id, price, currency)] (DoomedEconomyData.cs)."""
    src = _read(os.path.join(COMMANDS, "doomed", "DoomedEconomyData.cs"))
    m = re.search(r'ShopItems\s*=\s*\{(.*?)\};', src, re.S)
    return [(s, i, int(p), c.lower()) for s, i, p, c in
            re.findall(r'\("([^"]+)",\s*"([^"]+)",\s*(\d+),\s*CurrencyType\.(\w+)\)', m.group(1) if m else "")]


def image_sets():
    """The client's sprite sheets: {name: (path under client/src, tile width, tile height, block
    width, block height)}, from the Rust client's asset manifest. Index n is the nth block; a block
    is one tile, or an animated character's frames."""
    with open(os.path.join(ROOT, "client-rs", "assets", "manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    out = {s["name"]: (s["file"], s["w"], s["h"], s["w"], s["h"]) for s in manifest["image_sets"] if s.get("file")}
    # animated characters: each is a block of frames; its first frame is the block's top left tile
    for c in manifest["animated_chars"]:
        out.setdefault(c["name"], (c["file"], c["w"], c["h"], c["sheet_w"], c["sheet_h"]))
    return out


def mystery_crate():
    """What the Mystery ST Crate gives: [(item id, weight out of the total)] (Player.MysteryBoxes.cs)."""
    src = _read(os.path.join(SRC, "WorldServer", "core", "objects", "player", "Player.MysteryBoxes.cs"))
    m = re.search(r'MysteryStCrate\s*=\s*\{(.*?)\};', src, re.S)
    return [(i, int(w)) for i, w in re.findall(r'\("([^"]+)",\s*(\d+)\)', m.group(1))] if m else []


def level_exp(level):
    """Experience from level 1 to a level (Player.GetLevelExp)."""
    return 0 if level == 1 else 50 * (level - 1) + (level - 2) * (level - 1) * 50


def star_fames():
    """The fame of each class quest (the Rust client's STARS)."""
    src = _read(os.path.join(ROOT, "client-rs", "app", "src", "stars.rs"))
    m = re.search(r'STARS:\s*\[i32;\s*\d+\]\s*=\s*\[([^\]]+)\]', src)
    return [int(x) for x in re.findall(r'\d+', m.group(1))]


def tier_colours():
    """The client's item slot edge colours (tier_edge in client-rs screens.rs): [(label, #rrggbb)]."""
    src = _read(os.path.join(ROOT, "client-rs", "app", "src", "screens.rs"))
    body = re.search(r'fn tier_edge\(.*?\n\}', src, re.S).group(0)
    out = []
    ranges = re.findall(r'(\d+)\.\.=(\d+)\s*=>\s*0x([0-9A-Fa-f]{6})', body)
    for lo, hi, col in ranges:
        out.append(("Tier %s to %s" % (lo, hi), "#" + col))
    m = re.search(r'_\s*=>\s*0x([0-9A-Fa-f]{6})', body)
    if m and ranges:
        out.append(("Tier %d and up" % (int(ranges[-1][1]) + 1), "#" + m.group(1)))
    m = re.search(r'setType.*?Some\(0x([0-9A-Fa-f]{6})\)', body, re.S)
    if m:
        out.append(("Set pieces (ST)", "#" + m.group(1)))
    m = re.search(r'has\("SlotType"\)\.then_some\(0x([0-9A-Fa-f]{6})\)', body)
    if m:
        out.append(("Untiered items (UT)", "#" + m.group(1)))
    m = re.search(r'\[([^\]]+)\]\.iter\(\)\.any.*?Some\(0x([0-9A-Fa-f]{6})\)', body, re.S)
    if m:
        names = [n for n in re.findall(r'"(\w+)"', m.group(1)) if n != "Admin"]
        out.append((", ".join(names) + " items", "#" + m.group(2)))
    return out


def _chance(text):
    if text is None:
        return 1.0
    text = text.strip()
    m = re.match(r'([0-9.]+)', text)
    if not m:
        return None
    v = float(m.group(1))
    return 1.0 if v > 1 else v  # some behaviours write 100 for certain


# --- commands --------------------------------------------------------------------------------
RANKS = ["Regular", "Donator", "Supporter", "Sponsor", "Sandbox", "Moderator", "Admin"]


def commands():
    """[(name, alias, rank)] of every chat command, the rank resolved through base classes."""
    found = []
    bases = {}
    blocks = []
    for path in glob.glob(os.path.join(COMMANDS, "**", "*.cs"), recursive=True):
        with open(path, encoding="utf-8-sig") as f:
            text = f.read()
        heads = list(re.finditer(r'class (\w+)\s*:\s*(\w+)\b', text))
        for i, m in enumerate(heads):
            end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
            body = text[m.end():end]
            rank = re.search(r'RankRequirement\s*=>\s*RankingType\.(\w+)', body)
            bases[m.group(1)] = (m.group(2), rank.group(1) if rank else None)
            blocks.append((m.group(1), m.group(2), body, path))
    for cls, base, body, path in blocks:
        name = re.search(r'CommandName\s*=>\s*"([^"]+)"', body)
        if not name:
            continue
        alias = re.search(r'Alias\s*=>\s*"([^"]+)"', body)
        rank = bases[cls][1]
        b = base
        while rank is None and b in bases:
            b, rank = bases[b][0], bases[b][1]
        found.append((name.group(1), alias.group(1) if alias else None, rank or "Regular",
                      os.path.relpath(path, ROOT).replace(os.sep, "/")))
    return sorted(found)


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "dungeons"
    if what == "commands":
        for c in commands():
            print(*c)
        sys.exit()
    d = Data()
    if what == "classes":
        for c in d.classes():
            print(c["name"], "|", c["weapon"], c["ability"], c["armor"], c["start"], c["doomed"])
    elif what == "sets":
        for st in d.sets():
            print(st["id"], st["pieces"])
    elif what == "gear":
        for it in sorted(d.gear(), key=lambda it: (it["category"], it["kind"], it["group"], it["tier"])):
            print(it["category"], "|", it["kind"], "|", it["group"], it["tier"], "|", it["name"], "|", it["texture"])
    elif what == "pets":
        for p in d.pets()[:10]:
            print(p)
        print(len(d.pets()))
    else:
        portals, keys, idx = d.portals(), d.keys(), d.drops_index()
        for wid, w in sorted(d.worlds.items()):
            ps = portals.get(wid, [])
            ks = [k for p in ps for k in keys.get(p, [])]
            src = [(m, c) for p in ps for m, c in idx.get(p, [])]
            print(wid, "|", ps, "|", ks, "|", src[:6])
