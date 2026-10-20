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

Run on its own (python tools/port_doomed_pets.py), it puts the sizes' healing into the files the
last port wrote, without the Doomed Realms repo.
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
PET_SIZES = {"Small": (15, 2000), "Medium": (30, 2000), "Large": (45, 1500)}
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
    "Reddemonn", "Phoenixx", "Djinnn", "Reaperr", "Tornadoo", "Cookiee", "Gyaradoss", "Nightmareee", "pumpkmann",
    "Zombie Penguin", "Shroom (Doomed)", "Skeletal Guardian Pet", "Kythal the Skull", "Cerberus Skull", "Sea Maree",
    "Pyramid Artifact P", "KingSlime", "Josh's Pet", "Dallas's Pet", "Astro's Griffin", "Aceaku's Pet",
    "Lunar's Phoenix", "Support Bot", "Knight Pet", "Twilight Sparkle",
}
# bosses: these, and any pet drawn as a boss (find_bosses)
LARGE_PETS = {
    "Gebb", "Bess", "Nutt", "Thessal Pet", "Dinosaur's Pet", "Skull Shrine P", "Cubee", "Septaviuss", "Malphass",
    "Sthenos", "Horrificc", "Limonn", "Coill Snake", "Scootaloo", "sphinxxx", "Lil' Oryx (Doomed)",
    "Kythal the Necromancer", "Zenco The Undead God", "Doomed Nature Beast",
}
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
    lines, stats = [], {}
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
        size = pet_size(oid, boss)
        if size:
            amount, ms = PET_SIZES[size]
            d = dict(d, hp=(ms, amount, amount), mp=(ms, amount, amount))
        parts = ["new PetFollow()"]
        stat = stats[oid] = {"size": size} if size else {}
        for key, mana in (("hp", "false"), ("mp", "true")):
            if d[key]:
                ms, lo, hi = d[key]
                parts.append("new PetHeal(%s, %d, %d, %d)" % (mana, lo, hi, ms))
                stat[key] = (lo, hi, ms)
        dps = 0.0
        for attack in d["attacks"]:
            ms, kind = attack[0], attack[1]
            if kind == "direct":
                lo, hi, rng = attack[2], attack[3], attack[4]
                parts.append("new PetStrike(%d, %d, %d, %g)" % (lo, hi, ms, rng))
                dps += (lo + hi) / 2 * 1000 / ms
            elif projectile_damage(shooter, attack[2]):
                index, count, rng, angle, offset = attack[2:]
                parts.append("new PetShoot(%g, %d, %g, %g, %d, %d)" % (
                    rng, count, round(angle, 2), round(offset % 360, 2), index, ms))
                lo, hi = projectile_damage(shooter, index)
                dps += (lo + hi) / 2 * shots_on_target(count, angle, offset) * 1000 / ms
        if dps:
            stat["dps"] = round(dps)
        comment = "  // shoots like %s" % boss if oid in boss_projs else ""
        lines.append("            .Init(%s, new State(%s))%s" % (cs_string(oid), ", ".join(parts), comment))
    for oid in sorted(bs_pets):
        size = pet_size(oid, None)
        parts = ["new PetFollow()"]
        if size:
            amount, ms = PET_SIZES[size]
            parts += ["new PetHeal(%s, %d, %d, %d)" % (mana, amount, amount, ms) for mana in ("false", "true")]
            stats[oid] = {"size": size, "hp": (amount, amount, ms), "mp": (amount, amount, ms)}
        lines.append("            .Init(%s, new State(%s))" % (cs_string(oid), ", ".join(parts)))

    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_pets.py). Do not edit.\n")
        f.write("// Permanent pets: the Doomed Realms pets with their healing and attacks (PetHeal, PetStrike, PetShoot),\n")
        f.write("// and betterSkillys' own permanent pets, which had no behaviour, with a follow. Pets drawn as a\n")
        f.write("// boss shoot that boss's projectiles in its patterns (port_doomed_pets.find_bosses).\n")
        f.write("using WorldServer.logic.behaviors;\nusing WorldServer.logic.behaviors.doomed;\n\n")
        f.write("namespace WorldServer.logic\n{\n    partial class BehaviorDb\n    {\n")
        f.write("        private _ DoomedPets = () => Behav()\n")
        f.write("\n".join(lines) + ";\n")
        f.write("    }\n}\n")
    annotate_generators(stats)
    write_boss_projectiles(boss_projs)
    for pet in sorted(boss_projs):
        print("  %s shoots like %s" % (pet, bosses[pet]))
    return len(pets), len(bs_pets)


def pet_size(oid, boss):
    """The pet's size (a PET_SIZES key), or None to keep its Doomed Realms healing."""
    for size, pets in (("Small", SMALL_PETS), ("Medium", MEDIUM_PETS), ("Large", LARGE_PETS)):
        if oid in pets:
            return size
    return "Large" if boss else None


def apply_sizes():
    """Puts the sizes' healing into the generated BehaviorDb.DoomedPets.cs and onto the generators,
    without the Doomed Realms repo: the pets' follow and attacks stay as the last port wrote them."""
    with open(PET_BEHAVIORS_CS, encoding="utf-8", newline="") as f:
        text = f.read()
    stats, sized = {}, 0

    def resize(m):
        nonlocal sized
        oid = m.group(1).replace('\\"', '"').replace("\\\\", "\\")
        parts = [p for p in re.split(r", (?=new )", m.group(2)) if not p.startswith("new PetHeal(")]
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
        return '.Init("%s", new State(%s))%s' % (m.group(1), ", ".join(parts), m.group(3))

    text = re.sub(r'\.Init\("((?:[^"\\]|\\.)*)", new State\((.*)\)\)((?:  //[^\r\n]*)?)', resize, text)
    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    # the pets' damage per second, from the generators as the last port wrote it
    with open(ITEM_XMLS[0], encoding="utf-8") as f:
        for m in re.finditer(r'<Activate objectId="([^"]*)"[^>]*\spetDps="(\d+)"', f.read()):
            oid = saxutils.unescape(m.group(1), {"&quot;": '"', "&apos;": "'"})
            stats.setdefault(oid, {})["dps"] = int(m.group(2))
    annotate_generators(stats)
    unsized = sorted(oid for oid in stats if oid not in SMALL_PETS | MEDIUM_PETS | LARGE_PETS and stats[oid].get("size") is None)
    print("%d pets sized; keeping their own healing: %s" % (sized, ", ".join(unsized) or "none"))


def shots_on_target(count, angle, offset):
    """How many shots of a PetShoot volley fly at the enemy it aims at."""
    start = -angle * (count - 1) / 2 + offset
    return sum(1 for i in range(count) if abs((start + angle * i + 180) % 360 - 180) <= HIT_CONE)


def annotate_generators(stats):
    """Puts each pet's damage per second (on one enemy) and healing on the PermaPet activates of
    the items that give it, for the item tooltips: petDps, petSize, petHp / petMp ("min-max") and
    petHpMs / petMpMs (every how many milliseconds)."""
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
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


if __name__ == "__main__":
    apply_sizes()
