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
SEALED = os.path.join(SRC, "WorldServer", "core", "worlds", "SealedDungeons.cs")

# SlotType numbers (ItemConstants): what goes where
WEAPONS = {1: "Sword", 2: "Dagger", 3: "Bow", 8: "Wand", 17: "Staff", 24: "Katana"}
ABILITIES = {4: "Tome", 5: "Shield", 11: "Spell", 12: "Seal", 13: "Cloak", 15: "Quiver", 16: "Helm",
             18: "Poison", 19: "Skull", 20: "Trap", 21: "Orb", 22: "Prism", 23: "Scepter", 25: "Shuriken"}
ARMORS = {6: "Leather armor", 7: "Heavy armor", 14: "Robe"}
RING = 9
SLOT_NAMES = {**WEAPONS, **ABILITIES, **ARMORS}
SLOT_NAMES[RING] = "Ring"
# Do Damage's own items that tools/port_doomed.py keeps among the Doomed Realms ones (type codes
# 0xD000 on): the T16 armours, Oryx the Ascended God's, drawn and balanced here. Not "From Doomed
# Realms".
OWN_IN_DOOMED = {"Sphinx Leather Armor", "Armor of the Pyramid", "Robe of the Nile"}
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


def _without_calls(text, call):
    """`text` without each `call` (like "ElderLoot(") and its arguments."""
    while call in text:
        i = text.index(call)
        depth, j = 1, i + len(call)
        while depth and j < len(text):
            depth += {"(": 1, ")": -1}.get(text[j], 0)
            j += 1
        text = text[:i] + text[j:]
    return text


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
            # a file's own loot lists (private static MobDrops[] CourtLoot() => new MobDrops[] {...}),
            # which its monsters call
            helpers = dict(re.findall(r'static MobDrops\[\]\s+(\w+)\(\)\s*=>\s*new MobDrops\[\]\s*\{(.*?)\};', text, re.S))
            # and a shared builder's local ones (MobDrops[] Common() => [ ... ];, BehaviorDb.Draconis.cs)
            helpers.update(re.findall(r'\bMobDrops\[\]\s+(\w+)\(\)\s*=>\s*\[(.*?)\];', text, re.S))
            marks = list(start.finditer(text))
            for i, m in enumerate(marks):
                end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
                block = text[m.start():end]
                block += "".join(helpers[h] for h in re.findall(r'\b(\w+)\(\)', block) if h in helpers)
                name = m.group(3)
                if m.group(1):
                    d = re.search(r'DoomedMonsterDefs\.(M_\w+)', block)
                    if d:
                        doomed_names[name] = d.group(1)
                    continue
                # E("x") builds "x" and "Elder x" from one block when the file takes an elder flag;
                # its ElderLoot(...) is the Elder one's only
                if m.group(2) == "E(":
                    inits.setdefault("Elder " + name, []).append(block)
                    block = _without_calls(block, "ElderLoot(")
                inits.setdefault(name, []).append(block)
        self.doomed = {}
        self.elder_loot = {}  # hand-written Elder monsters' loot, from their generated definition
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
            # Elder("x") in a hand-written block takes the loot of the generated Elder x (ElderDrops)
            for name, blocks in inits.items():
                mid = "M_" + re.sub(r"\W", "_", name)
                if name.startswith("Elder ") and mid in bodies and any('Elder("' in b for b in blocks):
                    self.elder_loot[name] = bodies[mid]
        # loot DoomedSetup.Drops adds to the generated monsters (DoomedExtraLoot.Extra): {monster: its entry}
        self.extra_loot = {}
        extra = os.path.join(LOGIC, "doomed", "DoomedExtraLoot.cs")
        if os.path.isfile(extra):
            with open(extra, encoding="utf-8-sig") as f:
                text = f.read()
            marks = list(re.finditer(r'\["([^"]+)"\]\s*=\s*\(\)\s*=>', text))
            for i, m in enumerate(marks):
                end = marks[i + 1].start() if i + 1 < len(marks) else text.find("};", m.end())
                self.extra_loot[m.group(1)] = text[m.end():end]
        return inits

    def monsters(self):
        return set(self.inits) | set(self.doomed)

    # TierLoot(5, ItemType.Weapon ...) or TierLoot(tier: 5, type: ItemType.Weapon ...)
    # ItemLoot("x", 0.01) or ItemLoot(item: "x", probability: 0.01); OnePlayerLoot("x", 0.5, 0.01)
    # by its one player's chance
    ITEM_LOOT = re.compile(r'(?:ItemLoot|OnePlayerLoot)\(\s*(?:item:\s*)?"([^"]+)"\s*,\s*(?:probability:\s*)?([0-9.]+)')
    ITEM_LOOT_ID = re.compile(r'(?:ItemLoot|OnePlayerLoot)\(\s*(?:item:\s*)?"([^"]+)"')  # with or without a chance
    TIER_LOOT = re.compile(r'TierLoot\(\s*(?:tier:\s*)?(\d+)\s*,\s*(?:type:\s*)?ItemType\.(\w+)')

    def _loot_templates(self):
        """{name: source of its list} for the shared drop lists in MobDrops.cs (LootTemplates:
        MountainDrop, the godlands gods'; BasicDrop and StrongerDrop, many dungeon bosses')."""
        if not hasattr(self, "_templates"):
            with open(os.path.join(LOGIC, "loot", "MobDrops.cs"), encoding="utf-8-sig") as f:
                text = f.read()
            self._templates = dict(re.findall(r'public static MobDrops\[\] (\w+)\(\)(.*?)\];', text, re.S))
        return self._templates

    def drops(self, monster, _seen=None):
        """[(kind, id, chance)] a monster drops: kind is 'portal', 'item' or 'tier'. Loot a boss
        leaves in a chest or balloon it turns into or spawns counts as the boss's."""
        seen = _seen or set()
        seen.add(monster)
        out = []
        for block in self.inits.get(monster, []):
            # N("x") in a shared builder is "Elder x" in the Elder version, where there is one
            for m in re.finditer(r'(?:TransformOnDeath|Spawn|TossObject)\(\s*(N\()?"([^"]*(?:Loot|Chest|Balloon|Treasure|Coffin)[^"]*)"', block):
                target = m.group(2)
                if m.group(1) and monster.startswith("Elder ") and "Elder " + target in self.inits:
                    target = "Elder " + target
                if target not in seen and self.objects.get(target, {}).get("class") != "Portal":
                    out.extend(x for x in self.drops(target, seen) if x[0] != "portal")
        for spawner in self.chest_spawners().get(monster, []):
            if spawner not in seen:
                out.extend(x for x in self.drops(spawner, seen) if x[0] != "portal")
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
            # the portals in a set piece it places on death (the Ghost Ship's, on its island)
            for m in re.finditer(r'PlaceMapAtDeath\(\s*"([^"]+)"', block):
                out.extend(("portal", p, 1.0) for p in self.map_portals(m.group(1)))
            block += "".join(self._loot_templates().get(t, "")
                             for t in re.findall(r'LootTemplates\.(\w+)\(\)', block))
            for m in self.ITEM_LOOT.finditer(block):
                out.append(("item", m.group(1), float(m.group(2))))
            for m in self.TIER_LOOT.finditer(block):
                out.append(("tier", (m.group(2), int(m.group(1))), None))
        block = self.doomed.get(monster) or self.elder_loot.get(monster)
        if block:
            if monster in self.doomed:
                for m in re.finditer(r'DeathPortal\(\s*T\("([^"]+)"\)\s*,\s*([0-9.]+)', block):
                    out.append(("portal", m.group(1), min(1.0, float(m.group(2)) / 100)))
            for m in re.finditer(r'Tuple\.Create\(\s*([0-9.]+)\s*,\s*\(ILoot\)\s*new ItemLoot\("([^"]+)"', block):
                out.append(("item", m.group(2), float(m.group(1))))
            for m in re.finditer(r'new TierLoot\(\s*(\d+)\s*,\s*ItemType\.(\w+)', block):
                out.append(("tier", (m.group(2), int(m.group(1))), None))
        if monster in self.doomed or monster in self.elder_loot:  # DoomedSetup.Drops, as ElderDrops is too
            for m in self.ITEM_LOOT.finditer(self.extra_loot.get(monster, "")):
                out.append(("item", m.group(1), float(m.group(2))))
        return out

    def map_portals(self, path):
        """The portals a set piece (a .jm under resources/worlds) holds."""
        try:
            with open(os.path.join(WORLDS, path), encoding="utf-8-sig") as f:
                entries = json.load(f).get("dict", [])
        except (OSError, ValueError):
            return []
        ids = [o["id"] for e in entries for o in e.get("objs") or []]
        return [i for i in ids if self.objects.get(i, {}).get("class") == "Portal"]

    def chest_spawners(self):
        """{boss: [its chest spawners]}: a "Chest Spawner" opens (its loot balloon or chest) once the
        bosses its (Entity|Entities)NotExistsTransition waits for are gone (the Shatters'), as does
        any other hidden object that waits for a boss and spawns a loot balloon or chest (Shaitan's
        md1 LeftHandSmash)."""
        if not hasattr(self, "_chest_spawners"):
            self._chest_spawners = {}
            for name, blocks in self.inits.items():
                if "Chest Spawner" not in name and (self.objects.get(name, {}).get("enemy") or not any(
                        re.search(r'(?:Spawn|TossObject)\(\s*"[^"]*(?:Loot|Chest|Balloon|Treasure)[^"]*"', b) for b in blocks)):
                    continue
                for b in blocks:
                    for m in re.finditer(r'Entit(?:y|ies)NotExistsTransition\(([^)]*)\)', b):
                        for boss in re.findall(r'"([^"]+)"', m.group(1)):
                            if boss in self.inits and self.objects.get(boss, {}).get("enemy"):
                                self._chest_spawners.setdefault(boss, []).append(name)
        return self._chest_spawners

    def drops_index(self):
        """{portal or item id: [(monster, chance)]}, from every behaviour."""
        idx = {}
        spawners = {s for lst in self.chest_spawners().values() for s in lst}  # their loot is their boss's
        for monster in sorted(self.monsters()):
            if monster in spawners:
                continue
            for kind, oid, chance in self.drops(monster):
                if kind == "item" and oid in sealed_items():
                    continue  # a closed dungeon's key doesn't drop (ItemLoot)
                if kind in ("portal", "item"):
                    lst = idx.setdefault(oid, [])
                    if not any(m == monster for m, _ in lst):
                        lst.append((monster, chance))
        return idx

    # --- the Divine Forge ---------------------------------------------------------------------
    # The fame for a tiered result by tier on the weapons' 0-14 scale, and its cap (ForgeRecipes.cs)
    FORGE_TIER_FAME = [0, 1, 2, 4, 6, 10, 15, 25, 40, 60, 125, 250, 400, 750, 1000]
    FORGE_TIERED_CAP = 1000
    FORGE_UNTIERED_CAP = 1500  # ForgeRecipes.UntieredCap
    FORGE_PRICE_SCALE = 0.5    # ForgeRecipes.PriceScale: the Doomed Realms list's and ForgeOnly's prices

    def starter_items(self):
        """What /gear hands out, for any class (GearCommand.StarterItems): for each slot a class
        wears, the highest tier at or below half its unbroken chain's top (at most T8), the oldest
        of that tier. The forge never takes them."""
        out = set()
        slots = set()
        for oid, o in self.objects.items():
            if o["class"] == "Player":
                slots.update(_int(x) for x in (_text(self.elements[oid], "SlotTypes", "") or "").split(",")[:4])
        for slot in slots:
            cands = [(oid, o) for oid, o in self.objects.items()
                     if o["class"] == "Equipment" and o["slot"] == slot and o["tier"] >= 0
                     and self.elements[oid].find("Consumable") is None]
            if not cands:
                continue
            tiers = {o["tier"] for _, o in cands}
            top = min(tiers)
            while top + 1 in tiers:
                top += 1
            target = min(top // 2, 8)
            pick = sorted((c for c in cands if c[1]["tier"] <= target), key=lambda c: (-c[1]["tier"], c[1]["type"]))
            if pick:
                out.add(pick[0][0])
        return out

    @classmethod
    def _scaled(cls, fame):
        return 0 if fame <= 0 else max(1, int(round(fame * cls.FORGE_PRICE_SCALE)))

    def forge_recipes(self):
        """Every recipe the Divine Forge knows, as ForgeRecipes.Build makes them: [{result, fame,
        advanced, items, chain}]. `chain` marks the two-of-the-tier-below recipes (the Doomed Realms
        list's own included), `advanced` the ones that need a worn Forge Amulet."""
        src = _read(os.path.join(SRC, "Shared", "resources", "Item.cs"))
        m = re.search(r'OpItems\s*=\s*new HashSet<string>\s*\{(.*?)\};', src, re.S)
        op = set(re.findall(r'"([^"]+)"', m.group(1) if m else ""))
        items = {}  # the tradeable items: Equipment (and Dye) objects without <Admin/> or OpItems
        for oid, o in self.objects.items():
            if o["class"] in ("Equipment", "Dye") and oid not in op and self.elements[oid].find("Admin") is None:
                items[oid] = o

        # tiered items two of the tier below don't make (ForgeRecipes.NotChained: the T16 armours, made
        # from a T15 and a shard); they still count for their slot's price scale
        m = re.search(r'NotChained\s*=\s*new HashSet<string>\s*\{([^}]*)\}', _read(os.path.join(COMMANDS, "doomed", "ForgeRecipes.cs")))
        not_chained = set(re.findall(r'"([^"]+)"', m.group(1) if m else ""))

        # what some monster drops: its named loot and every item of the tier pools it rolls
        loot = tier_loot_types()
        kinds = {**loot["types"], 10: "Potion"}
        pools = {}
        for oid, o in items.items():
            doomed = 0xD000 <= o["type"] <= 0xDFFF
            if o["slot"] not in kinds or (doomed and (o["slot"] not in loot["doomed"] or o["tier"] < 0)
                                          and oid not in loot["doomed_items"]):
                continue
            tier = loot["extra_items"].get(oid, o["tier"])  # TierLoot.ExtraPoolItems join the pool of their tier
            pools.setdefault((kinds[o["slot"]], tier), []).append(oid)
        blocks = ([b for bs in self.inits.values() for b in bs] + list(self.doomed.values())
                  + list(self.elder_loot.values()))
        blocks.append(_read(os.path.join(LOGIC, "doomed", "DoomedExtraLoot.cs")))
        templates = self._loot_templates()
        seasonal = set()
        dropped = set()
        for b in blocks:
            b = re.sub(r"//[^\n]*", "", b)
            for t in re.findall(r'LootTemplates\.(\w+)\(\)', b):
                b += templates.get(t, "")
            # a seasonal drop's items (SeasonalThreshold): the forge never makes them
            for sm in re.finditer(r'SeasonalThreshold\(', b):
                depth, j = 1, sm.end()
                while depth and j < len(b):
                    depth += {"(": 1, ")": -1}.get(b[j], 0)
                    j += 1
                seasonal.update(self.ITEM_LOOT_ID.findall(b[sm.end():j]))
            dropped.update(self.ITEM_LOOT_ID.findall(b))
            for tier, kind in self.TIER_LOOT.findall(b):
                dropped.update(pools.get((kind, int(tier)), []))
        dropped -= seasonal
        winter = re.search(r'WinterBossDrops\s*=\s*\{(.*?)\};', _read(os.path.join(LOGIC, "loot", "Loots.cs")), re.S)
        seasonal.update(re.findall(r'\("([^"]+)"', winter.group(1) if winter else ""))

        slots, ability_slots = set(), set()
        for oid, o in self.objects.items():
            if o["class"] == "Player":
                st = [_int(x) for x in (_text(self.elements[oid], "SlotTypes", "") or "").split(",")[:4]]
                slots.update(st)
                if len(st) > 1:
                    ability_slots.add(st[1])
        slots.discard(0)
        # each slot's items by tier, and the fame for each tier it can forge: the price scale runs to
        # the top of the unbroken chain from the lowest tier
        chains = {}
        for slot in sorted(slots):
            by_tier = {}
            for oid, o in sorted(items.items(), key=lambda kv: kv[1]["type"]):
                if o["slot"] == slot and o["tier"] >= 0:
                    by_tier.setdefault(o["tier"], []).append(oid)
            if not by_tier:
                continue
            top = min(by_tier)
            while top + 1 in by_tier:
                top += 1
            # the weapons' scale still ends at T14, as it did before their T15s: those cost the top price
            scale_top = min(top, 14) if loot["types"].get(slot) == "Weapon" else top
            fames = {}
            for tier in by_tier:
                if tier <= top and tier - 1 in by_tier:
                    scaled = round(tier * 14.0 / max(scale_top, 1))
                    fames[tier] = min(self.FORGE_TIER_FAME[max(1, min(scaled, len(self.FORGE_TIER_FAME) - 1))],
                                      self.FORGE_TIERED_CAP)
            chains[slot] = (by_tier, fames)

        def ability_chain_fame(res, ing):
            """Any two of the tier below costs its tier's price on the scale (ForgeRecipes.ChainFame)."""
            r, f = items.get(res), items.get(ing[0]) if ing else None
            if (not r or r["tier"] < 1 or len(ing) != 2 or ing[0] != ing[1]
                    or not f or f["slot"] != r["slot"] or f["tier"] != r["tier"] - 1 or r["slot"] not in chains):
                return None
            return chains[r["slot"]][1].get(r["tier"])

        econ = _read(os.path.join(COMMANDS, "doomed", "DoomedEconomyData.cs"))
        m = re.search(r'Recipes\s*=\s*\{(.*?)\};', econ, re.S)
        recipes = []
        for res, fame, adv, ing in re.findall(
                r'\("([^"]+)",\s*(\d+),\s*(true|false),\s*new\[\]\s*\{([^}]*)\}\)', m.group(1) if m else ""):
            ing = re.findall(r'"([^"]+)"', ing)
            fame = int(fame)
            if ability_chain_fame(res, ing) is not None:
                fame = ability_chain_fame(res, ing)
            elif res in items and items[res]["tier"] >= 0:
                fame = min(self._scaled(fame), self.FORGE_TIERED_CAP)
            else:
                fame = min(self._scaled(fame), self.FORGE_UNTIERED_CAP)
            recipes.append({"result": res, "fame": fame, "advanced": adv == "true", "items": ing})
        # the items only the forge makes, which no monster drops (ForgeRecipes.ForgeOnly)
        m = re.search(r'ForgeOnly\s*=\s*\{(.*?)\};', _read(os.path.join(COMMANDS, "doomed", "ForgeRecipes.cs")), re.S)
        forge_only = set()
        for res, fame, adv, ing in re.findall(
                r'\("([^"]+)",\s*(\d+),\s*(true|false),\s*new\[\]\s*\{([^}]*)\}\)', m.group(1) if m else ""):
            forge_only.add(res)
            recipes.append({"result": res, "fame": self._scaled(int(fame)), "advanced": adv == "true",
                            "items": re.findall(r'"([^"]+)"', ing)})

        # the Ascended items (ForgeRecipes.Ascended): an item of a tier, of its slot or (slot 0) of any
        # slot of its kind, and an Ascended shard; `ascended` marks them, with the kind and tier when
        # any item of them will do, for the forge page. The T16 armours come the same way (a T15
        # armour and a shard) but aren't Ascended items: they go with the other recipes.
        forge_src = _read(os.path.join(COMMANDS, "doomed", "ForgeRecipes.cs"))
        consts = dict(re.findall(r'(\w+Shard)\s*=\s*"([^"]+)"', forge_src))
        fame_m = re.search(r'AscendedFame\s*=\s*(\d+)', forge_src)
        m = re.search(r'\bAscended\s*=\s*\{(.*?)\};', forge_src, re.S)
        for res, kind, tier, slot, shard in re.findall(
                r'\("([^"]+)",\s*ItemType\.(\w+),\s*(\d+),\s*(\d+),\s*(\w+)\)', m.group(1) if m else ""):
            tier, slot = int(tier), int(slot)
            forge_only.add(res)
            for oid, o in sorted(items.items(), key=lambda kv: kv[1]["type"]):
                if o["tier"] == tier and (o["slot"] == slot if slot else loot["types"].get(o["slot"]) == kind):
                    r = {"result": res, "fame": int(fame_m.group(1)), "advanced": False, "items": [oid, consts[shard]]}
                    if res in self.elements and self.elements[res].find("Ascended") is not None:
                        r["ascended"] = {"any": (kind, tier) if slot == 0 else None}
                    recipes.append(r)
        # the Infernal potions (ForgeRecipes.InfernalPotions): an Infernal shard, and the stat's
        # potions from the potion storage (`storage`: the potion and how many), not a socket each
        potions = re.findall(r'"([^"]+)"', (re.search(r'StatPotions\s*=\s*\{(.*?)\};', forge_src, re.S) or [None, ""])[1])
        cost = re.search(r'InfernalPotionCost\s*=\s*(\d+)', forge_src)
        ifame = re.search(r'InfernalPotionFame\s*=\s*(\d+)', forge_src)
        m = re.search(r'InfernalPotions\s*=\s*\{(.*?)\};', forge_src, re.S)
        for res, shard, stat in re.findall(r'\("([^"]+)",\s*"([^"]+)",\s*(\d+)\)', m.group(1) if m else ""):
            forge_only.add(res)
            recipes.append({"result": res, "fame": int(ifame.group(1)) if ifame else 0, "advanced": False,
                            "items": [shard], "storage": (potions[int(stat)], int(cost.group(1)) if cost else 100)})
        # and the ones from one item (ForgeRecipes.AscendedFrom)
        m = re.search(r'\bAscendedFrom\s*=\s*\{(.*?)\};', forge_src, re.S)
        for res, frm, shard in re.findall(r'\("([^"]+)",\s*"([^"]+)",\s*(\w+)\)', m.group(1) if m else ""):
            forge_only.add(res)
            recipes.append({"result": res, "fame": int(fame_m.group(1)), "advanced": False,
                            "items": [frm, consts[shard]], "ascended": {"any": None}})

        def key(r):
            return r["result"] + "=" + "+".join(sorted(r["items"]))
        known = {key(r) for r in recipes}
        for slot, (by_tier, fames) in chains.items():
            for tier, results in by_tier.items():
                if tier not in fames:
                    continue
                below = by_tier[tier - 1]
                for res in results:
                    if res in not_chained:
                        continue
                    last = res.rsplit(" ", 1)[-1]
                    frm = below[0] if len(below) == 1 else next(
                        (b for b in below if b.rsplit(" ", 1)[-1] == last), below[0])
                    r = {"result": res, "fame": fames[tier], "advanced": False, "items": [frm, frm]}
                    if key(r) not in known:
                        known.add(key(r))
                        recipes.append(r)

        starter = self.starter_items()
        out = []
        for r in recipes:
            if (r["result"] in items
                    and (r["result"] in dropped or r["result"] in forge_only) and r["result"] not in seasonal
                    and all(i in items for i in r["items"]) and not any(i in starter for i in r["items"])):
                res = items[r["result"]]
                a, b = (r["items"] + [None, None])[:2]
                r["chain"] = (len(r["items"]) == 2 and a == b and res["tier"] >= 1
                              and items[a]["slot"] == res["slot"] and items[a]["tier"] == res["tier"] - 1)
                out.append(r)
        return out

    # --- the Nexus Bazaar --------------------------------------------------------------------
    def bazaar(self):
        """{item id: (price in fame, shop name)} from MerchantLists.cs, without a closed dungeon's
        key (MerchantLists leaves those out)."""
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
                if name not in sealed_items():
                    out[name] = (int(price), shop)
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
        """The health a boss really has: its BossStats when it sets one (not the one for the
        Arena, `arena: true`), else its XML."""
        for block in self.inits.get(monster, []):
            for m in re.finditer(r'new BossStats\(\s*(\d+)([^)]*)\)', block):
                if "arena: true" not in m.group(2):
                    return int(m.group(1))
        o = self.objects.get(monster)
        return o["hp"] if o else 0

    def incantations(self, dropped=None):
        """{open portal id: [(item id, [dungeons it works in])]}: items that unlock a locked
        portal (the XML ChangeObject effect on a "Locked ..." portal, such as the Locked Oryx's
        Inferno Portal). With `dropped` (drops_index()), only those whose locked portal something
        still drops: Oryx 1 and 2 leave the Wine Cellar's and the Sanctuary's open."""
        out = {}
        for oid, o in self.objects.items():
            for a in o["activate_attrs"]:
                if (a["effect"] == "ChangeObject" and (a.get("targetId") or "").startswith("Locked ")
                        and a.get("newId") and (dropped is None or dropped.get(a["targetId"]))):
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
        reward for the staff's rewards (<Reward/>), or test for the test items."""
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
                group = next((t.lower() for t in ("Admin", "Reward", "Unobtainable", "Legendary", "Ascended", "Ethereal")
                              if el.find(t) is not None),
                             None) or ("tiered" if o["tier"] >= 0 else "set" if el.get("setType") else "untiered")
            doomed = 0xD000 <= o["type"] <= 0xDFFF and oid not in OWN_IN_DOOMED
            # the random tier drops (Loots.Initialize): no Doomed Realms items but the tiered gear of
            # the Doomed Realms classes' own slots
            drops_as = (loot["types"].get(o["slot"]) if o["tier"] >= 0 and group == "tiered"
                        and (not doomed or o["slot"] in loot["doomed"] or oid in loot["doomed_items"]) else None)
            pool_tier = o["tier"]
            if oid in loot["extra_items"]:  # an untiered item in a tier pool (TierLoot.ExtraPoolItems)
                drops_as, pool_tier = loot["types"].get(o["slot"]), loot["extra_items"][oid]
            out.append(dict(o, kind=SLOT_NAMES[o["slot"]], category=cat, group=group, el=el, doomed=doomed,
                            texture=self.texture(oid), tier_loot=drops_as, pool_tier=pool_tier))
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


def sealed_dungeons():
    """{dungeon: (portal, item, why)}: the dungeons closed for now or kept for special events
    (SealedDungeons.cs)."""
    text = re.sub(r"//[^\n]*", "", _read(SEALED))
    return {d: (p, i, w) for d, p, i, w in
            re.findall(r'\(\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\s*\)', text)}


def sealed_items():
    return {i for _, i, _ in sealed_dungeons().values()}


def name_change_fame():
    """What changing your name costs, in fame (ChooseNameHandler; the first name is free)."""
    src = _read(os.path.join(SRC, "WorldServer", "core", "net", "handlers", "ChooseNameHandler.cs"))
    m = re.search(r'NAME_CHANGE_FAME\s*=\s*([\d_]+)', src)
    return int(m.group(1).replace("_", "")) if m else 0


def nexus_afk_minutes():
    """How long standing still in the Nexus sends a player back to the character screen, in
    minutes (serverSettings.nexusAfkMinutes in wServer.json, Shared/NexusAfk.cs; 0: never)."""
    try:
        cfg = json.loads(_read(os.path.join(SRC, "WorldServer", "wServer.json")).lstrip("﻿"))
        return float(cfg["serverSettings"].get("nexusAfkMinutes", 20))
    except (OSError, ValueError, KeyError):
        return 20.0


def market_rules():
    """The Marketplace's tax, listing limit and how long a listing lasts (Marketplace.cs)."""
    src = _read(os.path.join(SRC, "WorldServer", "core", "market", "Marketplace.cs"))

    def const(name):
        m = re.search(r'const int %s\s*=\s*([\d_]+)' % name, src)
        return int(m.group(1).replace("_", "")) if m else 0
    return {"tax": const("TaxPercent"), "limit": const("MaxListings"), "max_price": const("MaxPrice"),
            "default_days": const("DefaultHours") // 24, "max_days": const("MaxHours") // 24}


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


def imprints():
    """The imprints (Shared/resources/Imprints.cs), in its order: [{name, kind, description, fits}]."""
    src = _read(os.path.join(SRC, "Shared", "resources", "Imprints.cs"))
    slot_names = {"SwordSlot": "Swords", "DaggerSlot": "Daggers", "BowSlot": "Bows", "WandSlot": "Wands",
                  "RingSlot": "Rings", "StaffSlot": "Staves", "KatanaSlot": "Katanas", "BladesSlot": "Dual Blades",
                  "FistsSlot": "Fists"}
    out = []
    for name, kind, desc, slots in re.findall(
            r'new Imprint\(\w+, "([^"]+)", ImprintKind\.(\w+), "([^"]+)"(?:, ([^)]*\}|\w+))?\)', src):
        if not slots:
            fits = "Any gear"
        elif slots.strip() == "WeaponSlots":
            fits = "Weapons"
        else:
            fits = ", ".join(slot_names.get(s, s) for s in re.findall(r'\w+Slot', slots))
        out.append({"name": name, "kind": kind, "description": desc, "fits": fits})
    return out


def tier_loot_types():
    """Which tier drops a slot type comes in (MobDrops.cs): {"types": {slot type: "Weapon" |
    "Ability" | "Armor" | "Ring"}, "doomed": {the Doomed Realms classes' slot types}, "doomed_items":
    {the Doomed Realms items in the pools all the same}}."""
    src = _read(os.path.join(LOGIC, "loot", "MobDrops.cs"))
    types = {}
    for name, kind in (("WeaponT", "Weapon"), ("AbilityT", "Ability"), ("ArmorT", "Armor"), ("RingT", "Ring")):
        m = re.search(name + r'\s*=\s*new int\[\]\s*\{([^}]*)\}', src)
        for n in re.findall(r'\d+', m.group(1) if m else ""):
            types[int(n)] = kind
    m = re.search(r'DoomedClassSlots\s*=\s*new HashSet<int>\s*\{([^}]*)\}', src)
    items = re.search(r'DoomedPoolItems\s*=\s*new HashSet<string>\s*\{([^}]*)\}', src)
    return {"types": types, "doomed": {int(n) for n in re.findall(r'\d+', m.group(1) if m else "")},
            "doomed_items": set(re.findall(r'"([^"]+)"', items.group(1) if items else "")),
            # untiered items in a tier pool: {id: tier}
            "extra_items": {i: int(t) for i, t in re.findall(r'\["([^"]+)"\]\s*=\s*\((\d+),', src)}}


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
    m = re.search(r'has\("Ascended"\)\s*\{\s*return Some\(0x([0-9A-Fa-f]{6})\)', body)
    if m:
        out.append(("Ascended items", "#" + m.group(1)))
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
