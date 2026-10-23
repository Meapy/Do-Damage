"""Monster behaviours for tools/port_doomed.py.

Copies the Doomed Realms definitions of the ported monsters (wserver/logic/db/BehaviorDb.*.cs,
`.Init(0xTYPE, Behaves("Name", behaviours..., loot: ..., condBehaviors: ...))`) nearly verbatim
into source/WorldServer/logic/doomed/DoomedMonsters.cs, where they build the Doomed Realms
behaviour engine rebuilt in DoomedRuntime.cs. Object types written as numbers become
T("object id") lookups, so they resolve to the ported (or betterSkillys) objects.
source/WorldServer/logic/db/BehaviorDb.DoomedMonsters.cs registers them.
"""

import glob
import os
import re

from port_doomed_pets import balanced, cs_string

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFS_CS = os.path.join(ROOT, "source", "WorldServer", "logic", "doomed", "DoomedMonsters.cs")
DB_CS = os.path.join(ROOT, "source", "WorldServer", "logic", "db", "BehaviorDb.DoomedMonsters.cs")

# behaviour -> index of its object type argument
OBJTYPE_ARGS = {
    "Chasing": 3, "Circling": 3, "Charge": 2, "IsEntityPresent": 1, "IsEntityNotPresent": 1,
    "EntityLesserThan": 2, "TossEnemy": 2, "TossEnemyAtPlayer": 1, "SpawnMinionImmediate": 0,
    "SpawnMinion": 0, "SpawnRoot": 0, "Reproduce": 0, "Transmute": 0, "DeathTransmute": 0,
    "DeathPortal": 0, "OrderAllEntity": 1, "OrderEntity": 1, "Heal": 2, "StrictCircling": 2, "MaintainDist": 3,
}
# shared values the Doomed Realms definitions use (from its BehaviorDb.Mountain.cs)
SHARED = """        private const double PotProbability = 0.075;

        private static readonly LootDef CommonGodSoulBag =
            new LootDef(0, 2, 0, 8,
                Tuple.Create(0.020, (ILoot) new TierLoot(6, ItemType.Weapon)),
                Tuple.Create(0.015, (ILoot) new TierLoot(7, ItemType.Weapon)),
                Tuple.Create(0.010, (ILoot) new TierLoot(8, ItemType.Weapon)),
                Tuple.Create(0.020, (ILoot) new TierLoot(7, ItemType.Armor)),
                Tuple.Create(0.015, (ILoot) new TierLoot(8, ItemType.Armor)),
                Tuple.Create(0.010, (ILoot) new TierLoot(9, ItemType.Armor)),
                Tuple.Create(0.015, (ILoot) new TierLoot(3, ItemType.Ring)),
                Tuple.Create(0.001, (ILoot) new TierLoot(4, ItemType.Ring)),
                Tuple.Create(0.030, (ILoot) new TierLoot(4, ItemType.Ability)),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Kirby Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Beholder Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Reaper Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Priest Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Paladin Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Companion Knight Generator")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Oceanic Curse")),
                Tuple.Create(0.01, (ILoot)new ItemLoot("Djinn Generator")));

"""
HEX = re.compile(r"^\s*(\(\s*short\s*\)\s*)?(0x[0-9a-fA-F]+)\s*$")

# Drops changed from Doomed Realms' (SETUP-LOCAL.md, "Untiered rings"): monster -> {item: its new
# chance, or None to drop it}. The Crown is an admin item, the America Ring a reward, and Santa's
# Hat, which saves its wearer from death, is rarer.
LOOT_CHANGES = {
    "Christmas Oryx": {"Crown": None, "Santa's Hat": 0.05},
    "Oryx the Mad God 4": {"Crown": None},
    "Oryx the Mad God 5": {"Crown": None},
    "Turkey God": {"America Ring": None},
}


def change_loot(monster, loot):
    """`loot` (a definition's loot expression) with LOOT_CHANGES applied."""
    for item, chance in LOOT_CHANGES.get(monster, {}).items():
        entry = r'Tuple\.Create\(\s*([0-9.]+)\s*,\s*\(ILoot\)\s*new ItemLoot\(%s\)\)' % re.escape(cs_string(item))
        if chance is not None:
            loot = re.sub(entry, lambda m: m.group(0).replace(m.group(1), "%g" % chance, 1), loot)
            continue
        # with the comma after it, or the one before it when it's the last of its list
        loot, n = re.subn(r'\s*' + entry + r'\s*,', "", loot)
        if not n:
            loot = re.sub(r',\s*' + entry, "", loot)
    return loot


def split_args(text):
    """Top-level comma split of an argument list (no surrounding parentheses)."""
    args, depth, cur, i, n = [], 0, [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            cur.append(text[i:j + 1])
            i = j + 1
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            args.append("".join(cur))
            cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        args.append("".join(cur))
    return args


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return re.sub(r"^\s*#(end)?region[^\n]*$", "", text, flags=re.M)


def load_blocks(dr_root):
    """{type: block text of `.Init(0xTYPE, Behaves(...))`} from every Doomed Realms BehaviorDb file."""
    blocks = {}
    for f in sorted(glob.glob(os.path.join(dr_root, "wserver", "logic", "db", "*.cs"))):
        text = strip_comments(open(f, encoding="utf-8-sig", errors="replace").read())
        for m in re.finditer(r"\.Init\(\s*(0x[0-9a-fA-F]+)\s*,\s*Behaves\(", text):
            start = m.end() - 1
            end = balanced(text, start)
            blocks[int(m.group(1), 16)] = text[start + 1:end - 1]
    return blocks


def handwritten_behaviours():
    """Monster ids the betterSkillys behaviours register (`.Init("id"` or `.Init(E("id")`, which is
    the regular or the Elder version), leaving out the generated BehaviorDb.DoomedMonsters.cs."""
    ids = set()
    for f in glob.glob(os.path.join(ROOT, "source", "WorldServer", "logic", "db", "*.cs")):
        if os.path.abspath(f) == os.path.abspath(DB_CS):
            continue
        text = strip_comments(open(f, encoding="utf-8-sig", errors="replace").read())
        for m in re.finditer(r'\.Init\(\s*(E\(\s*)?"([^"]+)"', text):
            ids.add(m.group(2))
            if m.group(1):
                ids.add("Elder " + m.group(2))
    return ids


def translate(expr, type_to_id, refs):
    """Replaces object-type numbers in the known argument positions with T("id")."""
    calls = list(re.finditer(r"\b(?:new\s+)?(%s)(?:\.Instance)?\s*\(" % "|".join(OBJTYPE_ARGS), expr))
    for m in reversed(calls):
        open_paren = m.end() - 1
        close = balanced(expr, open_paren)
        inner = expr[open_paren + 1:close - 1]
        args = split_args(inner)
        idx = OBJTYPE_ARGS[m.group(1)]
        if idx < len(args):
            h = HEX.match(args[idx])
            if h:
                oid = type_to_id(int(h.group(2), 16))
                if oid:
                    refs.add(oid)
                    args[idx] = " T(%s)" % cs_string(oid)
                else:
                    args[idx] = " null"
                expr = expr[:open_paren + 1] + ",".join(args) + expr[close - 1:]
    # any remaining (short) casts in front of numbers
    return re.sub(r"\(\s*short\s*\)\s*(?=0x)", "", expr)


def definition(block, type_to_id, refs):
    args = split_args(block)
    name = args[0].strip().strip('"')
    behaviours, loot, cond = [], "null", "null"
    for a in args[1:]:
        s = a.strip()
        if s.startswith("loot:"):
            loot = translate(s[len("loot:"):].strip(), type_to_id, refs)
        elif s.startswith("condBehaviors:"):
            cond = translate(s[len("condBehaviors:"):].strip(), type_to_id, refs)
        elif s.startswith("new LootBehavior"):
            loot = translate(s, type_to_id, refs)
        elif s.startswith("new ConditionalBehavior[]"):
            cond = translate(s, type_to_id, refs)
        else:
            # a behaviour can be passed by name ("attack: new RunBehaviors(...)"); here it's an
            # array element
            s = re.sub(r"^[A-Za-z_]\w*:\s*", "", s)
            behaviours.append(translate(s, type_to_id, refs))
    return name, behaviours, cond, loot


def write_monsters(dr_root, monsters, aliases=None):
    """monsters: ported monster id -> (Doomed Realms type whose behaviour it uses, type -> object id
    lookup for its arguments); aliases: Elder monster id -> regular monster whose behaviour it shares.
    Returns the object ids their behaviours use."""
    blocks = load_blocks(dr_root)
    refs, members, inits = set(), [], []
    for oid in sorted(monsters):
        dr_type, type_to_id = monsters[oid]
        block = blocks.get(dr_type)
        if block is None or "PetBehaves" in block or "PetChasing" in block:
            continue  # pets have their own behaviours (port_doomed_pets.py)
        _, behaviours, cond, loot = definition(block, type_to_id, refs)
        loot = change_loot(oid, loot)
        field ="M_" + re.sub(r"\W", "_", oid)
        body = ",\n                ".join(b.strip() for b in behaviours)
        members.append("        // %s\n        public static readonly Definition %s = new Definition(\n"
                       "            new Behavior[] {\n                %s\n            },\n            %s,\n            %s);\n"
                       % (oid, field, body, cond, loot))
        inits.append("            .InitDoomed(%s, DoomedSetup.State(DoomedMonsterDefs.%s), DoomedSetup.Drops(%s, DoomedMonsterDefs.%s))"
                     % (cs_string(oid), field, cs_string(oid), field))

    with open(DEFS_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_monsters.py) from the Doomed Realms\n")
        f.write("// wserver/logic/db/BehaviorDb.*.cs. Do not edit; the engine is DoomedRuntime.cs.\n")
        f.write("using System;\nusing Shared.resources;\n\n")
        f.write("namespace WorldServer.logic.doomed\n{\n    internal static class DoomedMonsterDefs\n    {\n")
        f.write("        private static ushort? T(string id) => DoomedRuntime.T(id);\n\n")
        f.write(SHARED)
        f.write("\n".join(members))
        f.write("    }\n}\n")
    with open(DB_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_monsters.py). Do not edit.\n")
        f.write("using WorldServer.logic.doomed;\n\n")
        f.write("namespace WorldServer.logic\n{\n    partial class BehaviorDb\n    {\n")
        f.write("        private _ DoomedMonsters = () => Behav()\n")
        f.write("\n".join(inits) + ";\n\n")
        f.write("        // Elder monsters that share the behaviour of their regular version\n")
        f.write("        // (applied by Initialize once every behaviour is registered)\n")
        f.write("        private static readonly (string Elder, string Regular)[] DoomedAliases =\n        {\n")
        f.write("".join("            (%s, %s),\n" % (cs_string(k), cs_string(v)) for k, v in sorted((aliases or {}).items())))
        f.write("        };\n")
        f.write("    }\n}\n")
    return refs, len(inits)


def referenced_types(dr_root, dr_types, blocks=None):
    """Object types (numbers) the Doomed Realms behaviours of dr_types use as object type arguments."""
    blocks = blocks if blocks is not None else load_blocks(dr_root)
    out = set()
    for t in dr_types:
        block = blocks.get(t)
        if not block:
            continue
        for m in re.finditer(r"\b(?:new\s+)?(%s)(?:\.Instance)?\s*\(" % "|".join(OBJTYPE_ARGS), block):
            args = split_args(block[m.end():balanced(block, m.end() - 1) - 1])
            idx = OBJTYPE_ARGS[m.group(1)]
            if idx < len(args):
                h = HEX.match(args[idx])
                if h:
                    out.add(int(h.group(2), 16))
    return out
