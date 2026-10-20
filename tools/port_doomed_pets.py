"""Pet behaviours for tools/port_doomed.py.

Reads the Doomed Realms pet definitions (wserver/logic/db/BehaviorDb.Pets.cs and HealPets.cs) and
writes source/WorldServer/logic/db/BehaviorDb.DoomedPets.cs for the ported pets:
  * PetFollow (betterSkillys' follow, which also teleports the pet back when it is 20+ tiles away)
  * PetHeal for PetsHealingHP / PetsHealingMP, with the same amounts and cooldowns
  * PetStrike for PetAttack and ThrowAttackPet, which hit the nearest enemy directly
  * PetShoot for the projectile attacks (PetSimpleAttack, PetMultiAttack, PetRingAttack,
    PetPredictiveMultiAttack), with the same projectile, count, spread and offset
A pet drawn as a boss (it has a betterSkillys boss's sprite: a <Quest/> monster with projectiles,
or one in EXTRA_BOSSES) shoots like that boss instead: the boss's projectiles replace the pet's in
DoomedRealms.xml, and each distinct Shoot of the boss's behaviour becomes a PetShoot (count,
spread, offset, projectile and cooldown; aimed at the nearest enemy, as every pet shot is).
betterSkillys' own permanent pets, which had no behaviour, get the follow.
Pets are sorted into sizes (SMALL_PETS, MEDIUM_PETS, LARGE_PETS), which set their healing.

Run on its own (python tools/port_doomed_pets.py), it applies what's set by hand (apply_pet_changes:
sizes, PET_ATTACKS, PET_ABILITIES, PET_PROJECTILES...) to the files the last port wrote, without the
Doomed Realms repo.
"""

import copy
import glob
import math
import os
import re
import xml.etree.ElementTree as ET
from xml.sax import saxutils

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PET_BEHAVIOR_FILES = ["BehaviorDb.Pets.cs", "BehaviorDb.HealPets.cs"]
PET_BEHAVIORS_CS = os.path.join(ROOT, "source", "WorldServer", "logic", "db", "BehaviorDb.DoomedPets.cs")
CS_USINGS = ("using Shared.resources;\nusing WorldServer.core.net.stats;\nusing WorldServer.logic.behaviors;\n"
             "using WorldServer.logic.behaviors.doomed;\n\n")
ITEM_XMLS = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealms.xml"),
             os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "xmls", "DoomedRealms.xml")]
# shots this close to the aim count towards a pet's damage on one enemy
HIT_CONE = 15.0
BEHAVIOR_DIR = os.path.join(ROOT, "source", "WorldServer", "logic", "db")
# generated here or by the other port scripts: no betterSkillys bosses in them
GENERATED_CS = {"BehaviorDb.DoomedPets.cs", "BehaviorDb.DoomedAllies.cs", "BehaviorDb.DoomedMonsters.cs"}
# bosses without a <Quest/> flag
EXTRA_BOSSES = {"Horrific Creation"}
# a boss pet takes at most this many of the boss's attacks, in the order its behaviour lists them
MAX_BOSS_ATTACKS = 4
# a boss pet's fastest attack, ms (Mama Megamoth fires every 50)
MIN_COOLDOWN = 300
# Pet sizes: every sized pet heals its owner's HP and MP by the same amount, replacing Doomed Realms'
# healing (name: (amount, every how many ms)). Its attacks are as before.
PET_SIZES = {"Small": (15, 2000), "Medium": (30, 2000), "Large": (45, 1500), "Admin": (200, 800)}
# the pet shop's critters and pets like them
SMALL_PETS = {
    "Black Cat (Doomed)", "Grey Cat (Doomed)", "Orange Cat (Doomed)", "Tan Cat (Doomed)", "Yellow Cat (Doomed)",
    "White Cat (Doomed)", "Brown Pup (Doomed)", "Grey Pup (Doomed)", "Golden Pup (Doomed)", "Blue Snail (Doomed)",
    "Purple Snail (Doomed)", "Grey Wolf (Doomed)", "Black Wolf (Doomed)", "Brown Fox (Doomed)", "Grey Fox (Doomed)",
    "Green Frog (Doomed)", "Purple Frog (Doomed)", "Lion (Doomed)", "White Lion (Doomed)", "Penguin (Doomed)",
    "Green Penguin (Doomed)", "Grass Penguin (Doomed)", "Purple Penguin", "Karate Penguin Red", "Karate Penguin Blue",
    "Karate Penguin Yellow", "Karate Penguin Purple", "Sheepdog (Doomed)", "Golden Sheepdog (Doomed)", "Panda (Doomed)",
    "Bluebird (Doomed)", "Chick (Doomed)", "Pig (Doomed)", "Sea Slurp (Doomed)", "Squirrel (Doomed)", "Bat (Doomed)",
    "Lil' Ghost (Doomed)", "Leprechaun (Doomed)", "Toucan (Doomed)", "Skunk (Doomed)", "Mallard (Doomed)",
    "Duck (Doomed)", "Eagle (Doomed)", "Turtle (Doomed)", "Jelly (Doomed)", "Bee (Doomed)", "Raven (Doomed)",
    "Elf (Doomed)", "Crab (Doomed)", "Hamster (Doomed)", "Dino (Doomed)", "Elephant (Doomed)", "Demon Frog (Doomed)",
    "Gargoyle (Doomed)", "Gummy Bear (Doomed)", "Dragonfly (Doomed)", "Lil' Cyclops (Doomed)", "Snowy Owl (Doomed)",
    "Turkey (Doomed)", "Baby (Doomed)", "Baby Dragon (Doomed)", "Robobuddy (Doomed)", "Beach Ball",
    "Beer Slurp", "Valentine", "Bunnyy", "White Bag Pet", "Monkeyn", "Sea Horsee", "Kirbyy", "Pentaract Eyee",
}
# godland and dungeon monsters, heroes, the players' own named pets and other striking sprites
MEDIUM_PETS = {
    "Angell", "Ninjaa", "Knightt", "Paladinn", "Priestt", "sharkmann", "Pumpkinnnnn", "Beholderr", "Drakee",
    "Reddemonn", "Phoenixx", "Djinnn", "Reaperr", "Tornadoo", "Gyaradoss", "pumpkmann", "Zombie Penguin",
    "Skeletal Guardian Pet", "Sea Maree", "Pyramid Artifact P", "KingSlime", "Josh's Pet", "Dallas's Pet",
    "Astro's Griffin", "Aceaku's Pet", "Lunar's Phoenix", "Support Bot", "Knight Pet",
    # pets that shoot a plain pattern of their own
    "Kythal the Necromancer", "Kythal the Skull", "Zenco The Undead God", "Shroom (Doomed)", "Cerberus Skull",
    "Twilight Sparkle",
}
# bosses, the stronger and more unique pets (most have Elder versions): these, and any pet drawn as a
# boss (find_bosses), which shoots that boss's patterns
LARGE_PETS = {
    "Gebb", "Bess", "Nutt", "Thessal Pet", "Dinosaur's Pet", "Skull Shrine P", "Cubee", "Septaviuss", "Malphass",
    "Sthenos", "Horrificc", "Limonn", "Coill Snake", "Scootaloo", "sphinxxx", "Doomed Nature Beast",
    # the strongest of the pets with shots of their own
    "Cookiee", "Nightmareee",
}
# admins' own pets: their generators are marked <Admin/>, so they can't be traded
ADMIN_PETS = {"Lil' Oryx (Doomed)"}
# Set by hand, after the port (apply_pet_changes):
# A pet's attacks, in place of the ones the port found, as written in BehaviorDb.DoomedPets.cs:
# PetShoot(range, count, degrees between shots, offset, projectile id, cooldown ms), each volley aimed
# at the pet's target; PetStrike(min, max, cooldown ms, range, bleed damage, bleed ms).
PET_ATTACKS = {
    # Grand Sphinx's three phases (the port took 4 shots from the first, all the same Fireball):
    # Fireballs, Zedshots, Hellfire
    "sphinxxx": ["new PetShoot(12, 1, 0, 0, 0, 800)", "new PetShoot(12, 3, 10, 0, 0, 1000)",
                 "new PetShoot(10, 3, 10, 0, 1, 2000)", "new PetShoot(10, 2, 4, 0, 2, 1500)"],
    # Archdemon Malphas' bullets, then its big bullet and shields, aimed (its pair of shields flew 72
    # degrees apart, both wide of the target, and its heaviest shots weren't used)
    "Malphass": ["new PetShoot(10, 2, 20, 0, 0, 1000)", "new PetShoot(8, 1, 0, 0, 0, 1000)",
                 "new PetShoot(12, 1, 0, 0, 3, 1500)", "new PetShoot(12, 2, 10, 0, 4, 1500)"],
    # Tomb Support's, and its small paralysing Ice Magic, spread so not every one flies at the target
    "Nutt": ["new PetShoot(15, 20, 18, 0, 7, 1000)", "new PetShoot(15, 1, 0, 0, 5, 1000)",
             "new PetShoot(12, 3, 120, 0, 1, 1000)", "new PetShoot(12, 4, 90, 0, 2, 1000)",
             "new PetShoot(12, 3, 25, 0, 8, 1500)"],
    # without the ring of 12 White Balls
    "Horrificc": ["new PetShoot(12, 3, 15, 0, 1, 1500)", "new PetShoot(10, 8, 45, 0, 5, 3000)",
                  "new PetShoot(12, 1, 0, 0, 6, 800)"],
    # Moth Dust, and every fifth shot a fast slowing wave
    "Scootaloo": ["new PetShoot(10, 1, 0, 0, 0, 300)", "new PetShoot(10, 1, 0, 0, 1, 1500)"],
    # its zap bleeds the enemy for 300 over 4 seconds
    "Doomed Nature Beast": ["new PetStrike(90, 200, 4000, 10, 300, 4000)"],
    # a Medium pet's damage; the big shot weakens (Kythal) or bleeds (Zenco)
    "Kythal the Necromancer": ["new PetShoot(10, 3, 10, 0, 0, 1000)", "new PetShoot(10, 1, 0, 0, 1, 2000)"],
    "Kythal the Skull": ["new PetShoot(10, 3, 10, 0, 0, 1000)", "new PetShoot(10, 1, 0, 0, 1, 2000)"],
    "Zenco The Undead God": ["new PetShoot(10, 3, 10, 0, 0, 1000)", "new PetShoot(10, 1, 0, 0, 1, 2000)"],
    # three shots in an arc, like a bow
    "Nightmareee": ["new PetShoot(10, 3, 8, 0, 0, 1000)"],
    # Oryx the Mad God 2's attacks (its Attack phase, aimed), without its minions
    "Lil' Oryx (Doomed)": ["new PetShoot(12, 8, 45, 0, 0, 1500)", "new PetShoot(12, 3, 10, 0, 1, 1000)",
                           "new PetShoot(5, 3, 10, 0, 2, 1000)", "new PetShoot(12, 2, 10, 0, 3, 1000)",
                           "new PetShoot(12, 3, 10, 0, 4, 1000)", "new PetShoot(12, 2, 10, 0, 5, 1000)",
                           "new PetShoot(12, 3, 10, 0, 6, 1000)"],
}
# What a pet does for its owner, besides healing: PetOwnerEffect(condition, ms, cooldown ms) and
# PetOwnerBoost(stat, amount, ms, cooldown ms)
PET_ABILITIES = {
    "Gebb": ["new PetOwnerEffect(ConditionEffectIndex.Healing, 2000, 5000)"],
    "Limonn": ["new PetOwnerEffect(ConditionEffectIndex.Speedy, 2000, 5000)"],
    "Horrificc": ["new PetOwnerEffect(ConditionEffectIndex.Invulnerable, 1000, 10000)"],
    "Malphass": ["new PetOwnerBoost(StatDataType.Vitality, 15, 5000, 10000)"],
    "Cubee": ["new PetOwnerBoost(StatDataType.Wisdom, 15, 5000, 10000)"],
    "Skull Shrine P": ["new PetOwnerBoost(StatDataType.Attack, 15, 5000, 10000)"],
}
# The pet's projectiles copied from a betterSkillys boss (without their condition effects), in place
# of its own
PET_BOSS_PROJECTILES = {"Lil' Oryx (Doomed)": "Oryx the Mad God 2"}
# Projectiles changed by hand: (pet, projectile id) -> what to set: damage, speed, lifetime (ms),
# object (its sprite), effects (condition, seconds) on the enemy hit, bleed (damage, seconds), multihit.
# A projectile the pet doesn't have is made from these.
PET_PROJECTILES = {
    # the Tomb bosses' ring of spinners did no damage
    ("Gebb", 3): {"damage": 30}, ("Bess", 3): {"damage": 30}, ("Nutt", 7): {"damage": 30},
    ("Bess", 1): {"effects": [("Armor Broken", 2)]},
    ("Nutt", 8): {"effects": [("Paralyzed", 0.25)]},
    # Moth Dust flew 1 tile
    ("Scootaloo", 0): {"damage": 80, "speed": 50, "lifetime": 1600, "multihit": False},
    ("Scootaloo", 1): {"object": "Cyan Fairy Wave", "damage": 100, "speed": 120, "lifetime": 900,
                       "effects": [("Slowed", 1)], "multihit": False},
    ("Kythal the Necromancer", 0): {"damage": 70},
    ("Kythal the Necromancer", 1): {"damage": 120, "effects": [("Weak", 2)]},
    ("Kythal the Skull", 0): {"damage": 70},
    ("Kythal the Skull", 1): {"damage": 120, "effects": [("Weak", 2)]},
    ("Zenco The Undead God", 0): {"damage": 70},
    ("Zenco The Undead God", 1): {"damage": 120, "bleed": (120, 3)},
}
# The pet drawn as another object's sprite (its generator's)
PET_TEXTURES = {"Scootaloo": "Scootaloo Generator"}
# How big each size is drawn, in a character's heights (a character is an 8x8 sprite at Size 100): a
# pet's <Size> is set from how much of its sprite's frame the picture covers (pet_draw_sizes).
SIZE_HEIGHTS = {"Small": 0.6, "Medium": 0.85, "Large": 1.2, "Admin": 1.2}
# where pets' <Size> is written: the port's XML and betterSkillys' own pets
PET_XMLS = ITEM_XMLS + [os.path.join(ROOT, "source", "Shared", "resources", "xml", "prod", "EmbeddedData_PermapetsCXML.xml"),
                        os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "prod", "xmls", "Permapets.xml")]
# the sprite sheets' files and cell sizes (tools/gen_rs_manifest.py), paths relative to client/src
MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")
# Shoot's parameters in order (logic/behaviors/Shoot.cs)
SHOOT_PARAMS = ["radius", "count", "shootAngle", "projectileIndex", "fixedAngle", "rotateAngle", "angleOffset",
                "defaultAngle", "predictive", "coolDownOffset", "coolDown"]


def texture_key(o):
    """(sheet, index) of an object's sprite, with the "dr" prefix of the copied sheets taken off."""
    for tag in ("AnimatedTexture", "Texture"):
        t = o.find(tag)
        if t is not None and t.findtext("File"):
            f = t.findtext("File").strip()
            if f.startswith("dr") and len(f) > 2:
                f = f[2].lower() + f[3:]
            i = t.findtext("Index", "0").strip()
            return f, int(i, 16) if i.lower().startswith("0x") else int(i)
    return None


def find_bosses(pets, chosen, bs):
    """pet id -> the betterSkillys boss it is drawn as."""
    by_sprite = {}
    for oid, o in bs.items():
        boss = o.find("Enemy") is not None and o.findall("Projectile") and (
            o.find("Quest") is not None or oid in EXTRA_BOSSES)
        if boss and texture_key(o):
            by_sprite.setdefault(texture_key(o), []).append(oid)
    found = {}
    for pet in pets:
        candidates = by_sprite.get(texture_key(chosen[pet]), [])
        # the plain boss before its Elder or second ("Deux") forms
        candidates.sort(key=lambda b: (b.startswith("Elder "), "Deux" in b, len(b), b))
        if candidates:
            found[pet] = candidates[0]
    return found


def number(text):
    """A Shoot argument's value: a number or simple arithmetic ("360 / 9"), None for anything else."""
    text = text.strip().rstrip("f")
    m = re.match(r"new Cooldown\(\s*(\d+)", text)
    if m:
        return float(m.group(1))
    if re.fullmatch(r"[\d.\s+\-*/()]+", text):
        try:
            return float(eval(text, {"__builtins__": {}}))
        except Exception:
            return None
    return None


def boss_attacks(boss):
    """The distinct Shoots in the boss's behaviour: (radius, count, angle, offset, index, cooldown)."""
    for path in sorted(glob.glob(os.path.join(BEHAVIOR_DIR, "*.cs"))):
        if os.path.basename(path) in GENERATED_CS:
            continue
        text = re.sub(r"/\*.*?\*/", "", open(path, encoding="utf-8-sig", errors="replace").read(), flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        m = re.search(r'\.Init\(\s*(?:\w+\(\s*)?"%s"' % re.escape(boss), text)
        if not m:
            continue
        nxt = re.search(r"\.Init\(", text[m.end():])
        block = text[m.start():m.end() + nxt.start() if nxt else len(text)]
        attacks, seen = [], set()
        for c in re.finditer(r"new Shoot\(", block):
            inner = block[c.end():balanced(block, c.end() - 1) - 1]
            args, depth, cur = [], 0, ""
            for ch in inner:
                if ch == "," and depth == 0:
                    args.append(cur)
                    cur = ""
                    continue
                depth += ch in "([" and 1 or ch in ")]" and -1 or 0
                cur += ch
            args.append(cur)
            values = {}
            for n, arg in enumerate(args):
                named = re.match(r"\s*(\w+)\s*:\s*(.*)$", arg, re.S)
                if named:
                    values[named.group(1)] = number(named.group(2))
                elif n < len(SHOOT_PARAMS):
                    values[SHOOT_PARAMS[n]] = number(arg)
            count = int(values.get("count") or 1)
            angle = values.get("shootAngle")
            if angle is None:
                angle = 360.0 / count if count > 1 else 0.0
            index = int(values.get("projectileIndex") or 0)
            offset = values.get("angleOffset") or 0.0
            # Cooldown.Normalize; at most one volley every MIN_COOLDOWN ms (each shot is a message)
            cooldown = max(int(values.get("coolDown") or 0) or 1000, MIN_COOLDOWN)
            radius = values.get("radius") or 0.0
            key = (count, round(angle, 2), round(offset, 2), index)
            if key not in seen:
                seen.add(key)
                attacks.append((radius, count, angle, offset, index, cooldown))
        return attacks[:MAX_BOSS_ATTACKS]
    return []


def boss_projectiles(boss_obj):
    """The boss's projectiles for its pet: the same sprites, speed, size, path and damage, but without
    their condition effects (a pet stunning or paralysing bosses would be too strong)."""
    out = []
    for p in boss_obj.findall("Projectile"):
        p = copy.deepcopy(p)
        for e in p.findall("ConditionEffect"):
            p.remove(e)
        p.tail = None
        out.append(p)
    return out


def write_boss_projectiles(projectiles):
    """Puts each boss pet's projectiles in place of its own in both copies of DoomedRealms.xml."""
    for path in ITEM_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        for pet, projs in projectiles.items():
            # the id as written: apostrophes are usually left as they are
            for pid in (saxutils.escape(pet, {'"': "&quot;"}), saxutils.escape(pet, {'"': "&quot;", "'": "&apos;"})):
                m = re.search(r'(<Object [^>]*id="%s">)(.*?)(\r?\n\t</Object>)' % re.escape(pid), text, re.S)
                if m:
                    break
            if not m:
                print("  %s: not found in %s" % (pet, os.path.basename(path)))
                continue
            nl = "\r\n" if "\r\n" in m.group(2) else "\n"
            body = re.sub(r"\s*<Projectile\b.*?</Projectile>", "", m.group(2), flags=re.S)
            new = ""
            for p in projs:
                ET.indent(p, space="\t", level=2)
                new += nl + "\t\t" + ET.tostring(p, encoding="unicode").replace("\n", nl)
            # after the sprite and flags, where the pets' own projectiles were
            cut = body.find("<Size>")
            cut = body.rfind(nl, 0, cut) if cut >= 0 else len(body)
            body = body[:cut] + new + body[cut:]
            text = text[:m.start()] + m.group(1) + body + m.group(3) + text[m.end():]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def balanced(text, start):
    """Index just past the parenthesis that closes the one at text[start]."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return i + 1
    return len(text)


def projectile_damage(obj, index):
    for p in obj.iter("Projectile"):
        if int(p.get("id", "0")) == index:
            dmg = p.findtext("Damage")
            if dmg:
                return int(dmg), int(dmg)
            lo, hi = p.findtext("MinDamage"), p.findtext("MaxDamage")
            if lo and hi:
                return int(lo), int(hi)
    return None


def parse_attack(call):
    a = re.match(r"new PetAttack\((\d+),\s*(\d+),\s*([\d.]+)\)", call)
    if a:
        return ("direct", int(a.group(1)), int(a.group(2)), float(a.group(3)))
    a = re.match(r"ThrowAttackPet\.Instance\(([\d.]+),\s*([\d.]+),\s*(\d+),\s*(\d+)\)", call)
    if a:
        return ("direct", int(a.group(3)), int(a.group(4)), float(a.group(2)))
    a = re.match(r"(Pet(?:Simple|Multi|Ring|PredictiveMulti)Attack)\.Instance\((.*)\)$", call, re.S)
    if not a:
        return None
    kind = a.group(1)
    # Doomed Realms' parameters. Angles are radians, mostly written "<degrees>*(float)Math.PI/180".
    names = {
        "PetSimpleAttack": ["radius", "projectileIndex"],
        "PetMultiAttack": ["radius", "angle", "numShot", "offset", "projectileIndex"],
        "PetPredictiveMultiAttack": ["radius", "angle", "numShot", "predictive", "projectileIndex"],
        "PetRingAttack": ["count", "radius", "offset", "projectileIndex"],
    }[kind]
    args = {}
    for n, arg in enumerate(x.strip() for x in a.group(2).split(",")):
        named = re.match(r"(\w+)\s*:\s*(.*)$", arg, re.S)
        if named:
            args[named.group(1)] = named.group(2)
        elif n < len(names):
            args[names[n]] = arg

    def number(key, default):
        m = re.match(r"\s*(-?[\d.]+)", args.get(key, ""))
        return float(m.group(1)) if m else default

    def degrees(key):
        if "PI" in args.get(key, ""):
            return number(key, 0.0)
        return math.degrees(number(key, 0.0))

    index = int(number("projectileIndex", 0))
    if kind == "PetRingAttack":
        # a ring starts at the target; PetShoot centres its spread on it
        count = max(1, int(number("count", 1)))
        step = 360.0 / count
        return ("projectile", index, count, number("radius", 10.0), step, degrees("offset") + step * (count - 1) / 2)
    count = max(1, int(number("numShot", 1)))
    offset = degrees("offset") if kind == "PetMultiAttack" else 0.0
    return ("projectile", index, count, number("radius", 10.0), degrees("angle"), offset)


def parse_pet_behaviors(dr_root):
    """Doomed Realms pet definitions: list of ((type min, type max), definition)."""
    defs = []
    for name in PET_BEHAVIOR_FILES:
        path = os.path.join(dr_root, "wserver", "logic", "db", name)
        text = re.sub(r"/\*.*?\*/", "", open(path, encoding="utf-8-sig").read(), flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        starts = [m.start() for m in re.finditer(r"\.Init(?:Many)?\(", text)]
        for n, s in enumerate(starts):
            block = text[s:starts[n + 1] if n + 1 < len(starts) else len(text)]
            many = re.match(r"\.InitMany\((0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+)", block)
            one = re.match(r"\.Init\((0x[0-9a-fA-F]+)", block)
            if not many and not one:
                continue
            lo = int((many or one).group(1), 16)
            hi = int(many.group(2), 16) if many else lo
            d = {"hp": None, "mp": None, "attacks": [], "many": bool(many)}
            for c in re.finditer(r"Cooldown\.Instance\(", block):
                inner = block[c.end():balanced(block, c.end() - 1) - 1]
                m = re.match(r"\s*(\d+)\s*,\s*(.*)$", inner, re.S)
                if not m:
                    continue
                ms, call = int(m.group(1)), m.group(2).strip()
                h = re.match(r"new PetsHealing(HP|MP)\((\d+),\s*(\d+)\)", call)
                if h:
                    d["hp" if h.group(1) == "HP" else "mp"] = (ms, int(h.group(2)), int(h.group(3)))
                    continue
                attack = parse_attack(call)
                if attack:
                    d["attacks"].append((ms,) + attack)
            defs.append(((lo, hi), d))
    return defs


def cs_string(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def write_pet_behaviors(dr_root, dr, chosen, bs, types):
    given = {a.get("objectId") for o in chosen.values() for a in o.iter("Activate") if a.text == "PermaPet"}
    pets = {oid for oid, o in chosen.items() if o.findtext("Class") == "Character" and oid in given}
    by_dr_type = {int(chosen[oid].get("type"), 16): oid for oid in pets}
    behaviors = {}
    # ranges (InitMany) first, single definitions override them
    for (lo, hi), d in sorted(parse_pet_behaviors(dr_root), key=lambda x: not x[1]["many"]):
        for t in range(lo, hi + 1):
            if t in by_dr_type:
                behaviors[by_dr_type[t]] = d
    existing = set()
    for f in glob.glob(os.path.join(ROOT, "source", "WorldServer", "logic", "db", "*.cs")):
        if os.path.abspath(f) != os.path.abspath(PET_BEHAVIORS_CS):
            existing |= set(re.findall(r'\.Init\("([^"]+)"', open(f, encoding="utf-8-sig", errors="replace").read()))
    bs_pets = {a.get("objectId") for o in bs.values() for a in o.iter("Activate")
               if a.text == "PermaPet" and a.get("objectId") in bs} - existing

    bosses = find_bosses(pets, chosen, bs)
    boss_projs = {}
    lines = []
    for oid in sorted(pets, key=lambda p: types[p]):
        d = behaviors.get(oid, {"hp": None, "mp": None, "attacks": []})
        shooter = chosen[oid]
        boss = bosses.get(oid)
        attacks = boss_attacks(boss) if boss else []
        if attacks:
            # the boss's shots instead of the pet's own attacks
            shooter = bs[boss]
            boss_projs[oid] = boss_projectiles(shooter)
            d = dict(d, attacks=[(cooldown, "projectile", index, count, min(max(radius, 8.0), 15.0) if radius else 10.0, angle, offset)
                                 for radius, count, angle, offset, index, cooldown in attacks])
        parts = ["new PetFollow()"]
        for key, mana in (("hp", "false"), ("mp", "true")):
            if d[key]:
                ms, lo, hi = d[key]
                parts.append("new PetHeal(%s, %d, %d, %d)" % (mana, lo, hi, ms))
        for attack in d["attacks"]:
            ms, kind = attack[0], attack[1]
            if kind == "direct":
                lo, hi, rng = attack[2], attack[3], attack[4]
                parts.append("new PetStrike(%d, %d, %d, %g)" % (lo, hi, ms, rng))
            elif projectile_damage(shooter, attack[2]):
                index, count, rng, angle, offset = attack[2:]
                parts.append("new PetShoot(%g, %d, %g, %g, %d, %d)" % (
                    rng, count, round(angle, 2), round(offset % 360, 2), index, ms))
        comment = "  // shoots like %s" % boss if oid in boss_projs else ""
        lines.append("            .Init(%s, new State(%s))%s" % (cs_string(oid), ", ".join(parts), comment))
    for oid in sorted(bs_pets):
        lines.append("            .Init(%s, new State(new PetFollow()))" % cs_string(oid))

    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_pets.py). Do not edit.\n")
        f.write("// Permanent pets: the Doomed Realms pets with their healing and attacks (PetHeal, PetStrike, PetShoot),\n")
        f.write("// and betterSkillys' own permanent pets, which had no behaviour, with a follow. Pets drawn as a\n")
        f.write("// boss shoot that boss's projectiles in its patterns (port_doomed_pets.find_bosses).\n")
        f.write(CS_USINGS)
        f.write("namespace WorldServer.logic\n{\n    partial class BehaviorDb\n    {\n")
        f.write("        private _ DoomedPets = () => Behav()\n")
        f.write("\n".join(lines) + ";\n")
        f.write("    }\n}\n")
    write_boss_projectiles(boss_projs)
    apply_pet_changes(bs)
    for pet in sorted(boss_projs):
        print("  %s shoots like %s" % (pet, bosses[pet]))
    return len(pets), len(bs_pets)


def pet_size(oid, boss):
    """The pet's size (a PET_SIZES key), or None to keep its Doomed Realms healing."""
    for size, pets in (("Small", SMALL_PETS), ("Medium", MEDIUM_PETS), ("Large", LARGE_PETS), ("Admin", ADMIN_PETS)):
        if oid in pets:
            return size
    return "Large" if boss else None


def load_bs():
    """betterSkillys' objects (every XML but the port's), by id."""
    objects = {}
    for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
        if os.path.abspath(f) == os.path.abspath(ITEM_XMLS[0]):
            continue
        try:
            for o in ET.parse(f).getroot().iter("Object"):
                objects.setdefault(o.get("id"), o)
        except ET.ParseError:
            pass
    return objects


def set_child(el, tag, value):
    child = el.find(tag)
    if child is None:
        child = ET.SubElement(el, tag)
    child.text = str(value)


def change_projectile(p, change):
    """Sets PET_PROJECTILES' changes on a <Projectile> element."""
    if "object" in change:
        set_child(p, "ObjectId", change["object"])
    if "damage" in change:
        for tag in ("MinDamage", "MaxDamage"):
            if p.find(tag) is not None:
                p.remove(p.find(tag))
        set_child(p, "Damage", change["damage"])
    if "speed" in change:
        set_child(p, "Speed", change["speed"])
    if "lifetime" in change:
        set_child(p, "LifetimeMS", change["lifetime"])
    if "effects" in change:
        for e in p.findall("ConditionEffect"):
            p.remove(e)
        for name, seconds in change["effects"]:
            ET.SubElement(p, "ConditionEffect", duration="%g" % seconds).text = name
    if "bleed" in change:
        for e in p.findall("Bleed"):
            p.remove(e)
        ET.SubElement(p, "Bleed", duration="%g" % change["bleed"][1]).text = str(change["bleed"][0])
    if change.get("multihit") is False:
        for e in p.findall("MultiHit"):
            p.remove(e)


def write_pet_projectiles(bs):
    """PET_BOSS_PROJECTILES, then PET_PROJECTILES, into both copies of DoomedRealms.xml."""
    pets = set(PET_BOSS_PROJECTILES) | {pet for pet, _ in PET_PROJECTILES}
    objects = {o.get("id"): o for o in ET.parse(ITEM_XMLS[0]).getroot().iter("Object")}
    new = {}
    for pet in sorted(pets):
        if pet not in objects:
            print("  %s: not found" % pet)
            continue
        if pet in PET_BOSS_PROJECTILES:
            projs = boss_projectiles(bs[PET_BOSS_PROJECTILES[pet]])
        else:
            projs = [copy.deepcopy(p) for p in objects[pet].findall("Projectile")]
        by_id = {int(p.get("id", "0")): p for p in projs}
        for (owner, index), change in sorted(PET_PROJECTILES.items()):
            if owner != pet:
                continue
            if index not in by_id:
                by_id[index] = ET.Element("Projectile", id=str(index))
                projs.append(by_id[index])
            change_projectile(by_id[index], change)
        for p in projs:
            p.text = p.tail = None
            for c in p:
                c.tail = None
        new[pet] = projs
    write_boss_projectiles(new)


def write_pet_textures():
    """Draws each pet in PET_TEXTURES as its generator's sprite, in both copies of DoomedRealms.xml."""
    for path in ITEM_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        for pet, source in PET_TEXTURES.items():
            m = re.search(r'<Object [^>]*\sid="%s">.*?</Object>' % re.escape(saxutils.escape(source)), text, re.S)
            tex = m and re.search(r"<(Animated)?Texture>.*?</(Animated)?Texture>", m.group(0), re.S)
            p = re.search(r'<Object [^>]*\sid="%s">.*?</Object>' % re.escape(saxutils.escape(pet)), text, re.S)
            if not tex or not p:
                print("  %s: no sprite from %s in %s" % (pet, source, os.path.basename(path)))
                continue
            obj = re.sub(r"<(Animated)?Texture>.*?</(Animated)?Texture>", lambda _: tex.group(0), p.group(0), count=1, flags=re.S)
            text = text[:p.start()] + obj + text[p.end():]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def behaviour_dps(parts, obj):
    """A pet's damage per second on one enemy, from its PetShoots (with their projectiles' damage and
    bleeds, as many shots as fly at the target) and PetStrikes (with their bleeds)."""
    dps = 0.0
    for kind, args in re.findall(r"new (PetShoot|PetStrike)\(([^)]*)\)", ", ".join(parts)):
        a = [float(x) for x in args.split(",")]
        if kind == "PetStrike":
            dps += (a[0] + a[1]) / 2 * 1000 / a[2]
            if len(a) > 5 and a[4]:
                dps += a[4] * 1000 / max(a[2], a[5])
            continue
        index, cooldown = int(a[4]), a[5]
        damage = obj is not None and projectile_damage(obj, index)
        if not damage:
            continue
        on_target = shots_on_target(int(a[1]), a[2], a[3])
        dps += (damage[0] + damage[1]) / 2 * on_target * 1000 / cooldown
        bleed = next((p.find("Bleed") for p in obj.findall("Projectile") if int(p.get("id", "0")) == index), None)
        if on_target and bleed is not None:
            dps += int(bleed.text) * 1000 / max(cooldown, float(bleed.get("duration")) * 1000)
    return dps


def apply_pet_changes(bs=None):
    """What's set by hand, after the port: the sizes' healing, PET_ATTACKS, PET_ABILITIES and the
    pets' projectiles and sprites, into BehaviorDb.DoomedPets.cs and both copies of DoomedRealms.xml;
    then each pet's tooltip numbers (annotate_generators) and drawn size. It runs at the end of the
    port, and on its own (without the Doomed Realms repo) on the files the last port wrote."""
    write_pet_projectiles(bs or load_bs())
    write_pet_textures()
    objects = {o.get("id"): o for o in ET.parse(ITEM_XMLS[0]).getroot().iter("Object")}
    with open(PET_BEHAVIORS_CS, encoding="utf-8", newline="") as f:
        text = f.read()
    stats, sized = {}, 0

    def change(m):
        nonlocal sized
        oid = m.group(1).replace('\\"', '"').replace("\\\\", "\\")
        parts = [p for p in re.split(r", (?=new )", m.group(2))
                 if not p.startswith(("new PetHeal(", "new PetOwnerEffect(", "new PetOwnerBoost("))]
        if oid in PET_ATTACKS:
            parts = [p for p in parts if not p.startswith(("new PetShoot(", "new PetStrike("))] + PET_ATTACKS[oid]
        parts += PET_ABILITIES.get(oid, [])
        size = pet_size(oid, "boss" if "// shoots like" in m.group(3) else None)
        stat = stats[oid] = {}
        if size:
            sized += 1
            amount, ms = PET_SIZES[size]
            parts[1:1] = ["new PetHeal(%s, %d, %d, %d)" % (mana, amount, amount, ms) for mana in ("false", "true")]
            stat.update(size=size, hp=(amount, amount, ms), mp=(amount, amount, ms))
        else:
            for h in re.finditer(r"new PetHeal\((true|false), (\d+), (\d+), (\d+)\)", m.group(2)):
                stat["mp" if h.group(1) == "true" else "hp"] = (int(h.group(2)), int(h.group(3)), int(h.group(4)))
        dps = behaviour_dps(parts, objects.get(oid))
        if dps:
            stat["dps"] = round(dps)
        return '.Init("%s", new State(%s))%s' % (m.group(1), ", ".join(parts), m.group(3))

    text = re.sub(r'\.Init\("((?:[^"\\]|\\.)*)", new State\((.*)\)\)((?:  //[^\r\n]*)?)', change, text)
    # the usings the abilities need
    head = text.index("namespace ")
    nl = "\r\n" if "\r\n" in text else "\n"
    usings_start = text.index("using ")
    text = text[:usings_start] + CS_USINGS.replace("\n", nl) + text[head:]
    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    annotate_generators(stats)
    write_pet_draw_sizes({oid: s["size"] for oid, s in stats.items() if s.get("size")})
    unsized = sorted(oid for oid in stats if oid not in SMALL_PETS | MEDIUM_PETS | LARGE_PETS | ADMIN_PETS and stats[oid].get("size") is None)
    print("%d pets sized; keeping their own healing: %s" % (sized, ", ".join(unsized) or "none"))


def sprite_extent(obj, sheets, anims):
    """The longer side, in pixels, of what an object's sprite covers in its (first) frame; None when
    it has no picture."""
    from PIL import Image
    anim = obj.find("AnimatedTexture")
    tex = anim if anim is not None else obj.find("Texture")
    if tex is None:
        return None
    sheet = (anims if anim is not None else sheets).get(tex.findtext("File", "").strip())
    if not sheet or not sheet.get("file"):
        return None
    index = int(tex.findtext("Index", "0").strip(), 0)
    image = Image.open(os.path.join(ROOT, "client", "src", sheet["file"])).convert("RGBA")
    if anim is not None:
        # the character's block (as the Rust client's Sprites::char_src finds it); its first frame,
        # the square at the start (a 16x8 sheet's second half is for the attack frame), stands
        w = h = sheet["h"]
        per_row = max(1, image.width // sheet["sheet_w"])
        x, y = index % per_row * sheet["sheet_w"], index // per_row * sheet["sheet_h"]
    else:
        w, h = sheet["w"], sheet["h"]
        cols = image.width // w
        x, y = index % cols * w, index // cols * h
    box = image.crop((x, y, x + w, y + h)).getchannel("A").getbbox()
    return max(box[2] - box[0], box[3] - box[1]) if box else None


def pet_draw_size(extent, size):
    """<Size> drawing a sprite covering extent pixels at its size's height, to the nearest 5."""
    return int(round(SIZE_HEIGHTS[size] * 8 / extent * 100 / 5.0)) * 5


def write_pet_draw_sizes(sizes):
    """Sets each sized pet's <Size> (sizes: pet id -> size) in PET_XMLS, so that pets of a size look
    about as big as one another, whatever their sprite's resolution. Measuring the sprites needs
    Pillow; without it the sizes are left as they are."""
    import json
    try:
        import PIL  # noqa: F401
    except ImportError:
        print("  Pillow isn't installed (pip install pillow): pets' drawn sizes left as they are")
        return
    manifest = json.load(open(MANIFEST, encoding="utf-8"))
    sheets = {s["name"]: s for s in manifest["image_sets"]}
    anims = {s["name"]: s for s in manifest["animated_chars"]}
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        objects = {o.get("id"): o for o in ET.fromstring(text.encode("utf-8")).iter("Object")}

        def resize(m):
            oid = saxutils.unescape(m.group(2), {"&quot;": '"', "&apos;": "'"})
            obj = objects.get(oid)
            if oid not in sizes or obj is None or obj.findtext("Class") != "Character":
                return m.group(0)
            extent = sprite_extent(obj, sheets, anims)
            if not extent:
                return m.group(0)
            new = "<Size>%d</Size>" % pet_draw_size(extent, sizes[oid])
            # the object's own <Size>, not its projectiles'
            return re.sub(r"(<Projectile\b.*?</Projectile>)|<Size>[^<]*</Size>",
                          lambda s: s.group(1) or new, m.group(0), flags=re.S)

        text = re.sub(r'(<Object [^>]*\sid="([^"]*)"[^>]*>)(.*?)</Object>', resize, text, flags=re.S)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def shots_on_target(count, angle, offset):
    """How many shots of a PetShoot volley fly at the enemy it aims at."""
    start = -angle * (count - 1) / 2 + offset
    return sum(1 for i in range(count) if abs((start + angle * i + 180) % 360 - 180) <= HIT_CONE)


def annotate_generators(stats):
    """Puts each pet's damage per second (on one enemy) and healing on the PermaPet activates of
    the items that give it, for the item tooltips: petDps, petSize, petHp / petMp ("min-max") and
    petHpMs / petMpMs (every how many milliseconds). Admin pets' generators get <Admin/>."""
    for path in ITEM_XMLS:
        text = open(path, encoding="utf-8", newline="").read()

        def annotate(m):
            stat = stats.get(saxutils.unescape(m.group(1), {"&quot;": '"', "&apos;": "'"}), {})
            attrs = re.sub(r'\s+pet\w+="[^"]*"', "", m.group(2))
            if "dps" in stat:
                attrs += ' petDps="%d"' % stat["dps"]
            if "size" in stat:
                attrs += ' petSize="%s"' % stat["size"]
            for key, name in (("hp", "Hp"), ("mp", "Mp")):
                if key in stat:
                    lo, hi, ms = stat[key]
                    attrs += ' pet%s="%s" pet%sMs="%d"' % (name, lo if lo == hi else "%d-%d" % (lo, hi), name, ms)
            return '<Activate objectId="%s"%s>PermaPet</Activate>' % (m.group(1), attrs)

        text = re.sub(r'<Activate objectId="([^"]*)"([^>]*)>PermaPet</Activate>', annotate, text)

        def admin(m):
            if 'petSize="Admin"' in m.group(0) and "<Admin" not in m.group(0):
                return re.sub(r"(<Item\s*/>)(\r?\n)([ \t]*)", r"\1\2\3<Admin />\2\3", m.group(0), count=1)
            return m.group(0)

        text = re.sub(r"<Object [^>]*>.*?</Object>", admin, text, flags=re.S)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


if __name__ == "__main__":
    apply_pet_changes()
