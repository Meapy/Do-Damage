"""Ally behaviours for tools/port_doomed.py.

Reads the Doomed Realms behaviours of the creatures that class abilities spawn
(wserver/logic/db/BehaviorDb.Towers.cs, PearlSubstitutes.cs and Summons.cs) and writes
source/WorldServer/logic/db/BehaviorDb.DoomedAllies.cs using betterSkillys' ally behaviours:
  * Mechanic turrets (Tower*): stand still and AllyShoot the nearest enemy.
  * Summoner summons (* Summon): AllyCharge at enemies within 10 tiles and AllyShoot them,
    AllyFollow the owner otherwise, like betterSkillys' own "Pirate Ally".
  * Druid pearl substitutes (PetAttackTarget): fire one volley, then disappear.
Their lifetimes come from the item (Player.UseItem.Doomed.cs).
"""

import os
import re

from port_doomed_pets import balanced, cs_string

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALLY_BEHAVIOR_FILES = ["BehaviorDb.Towers.cs", "BehaviorDb.PearlSubstitutes.cs", "BehaviorDb.Summons.cs"]
ALLY_BEHAVIORS_CS = os.path.join(ROOT, "source", "WorldServer", "logic", "db", "BehaviorDb.DoomedAllies.cs")
# Summons fire this many times slower than in Doomed Realms: a Summoner keeps up to six out
# (Player.UseItem.Doomed.cs), and six at the Doomed Realms rate doubled its damage.
SUMMON_FIRE_SLOWDOWN = 3


def degrees(expr):
    """'5 * (float)Math.PI / 180' or '10*(float) Math.PI/180' -> 5 / 10; '0' -> 0."""
    m = re.match(r"\s*([\d.]+)", expr or "")
    return float(m.group(1)) if m else 0.0


def parse_shot(call):
    """(radius, count, angle in degrees, projectile index) of a Doomed Realms pet attack."""
    m = re.match(r"(Pet(?:Simple|Multi|Ring|Predictive|PredictiveMulti)Attack)\.Instance\((.*)\)$", call.strip(), re.S)
    if not m:
        return None
    kind, args = m.group(1), m.group(2)
    named = re.search(r"projectileIndex\s*:\s*(\d+)", args)
    pos = [x.strip() for x in re.sub(r"\w+\s*:\s*[^,]+", "", args).split(",") if x.strip()]
    index = int(named.group(1)) if named else 0
    try:
        if kind == "PetSimpleAttack":
            return float(pos[0]), 1, 0.0, index if named or len(pos) < 2 else int(pos[1])
        if kind == "PetRingAttack":
            return 8.0, int(pos[0]), 360.0 / max(1, int(pos[0])), index if named or len(pos) < 4 else int(pos[3])
        count = int(re.sub(r"[^\d]", "", pos[2]) or 1) if len(pos) >= 3 else 1
        return float(pos[0]), count, degrees(pos[1]) if len(pos) >= 2 else 0.0, index
    except (ValueError, IndexError):
        return None


def parse_ally_behaviors(dr_root):
    """{Doomed Realms type: definition}."""
    defs = {}
    for name in ALLY_BEHAVIOR_FILES:
        text = open(os.path.join(dr_root, "wserver", "logic", "db", name), encoding="utf-8-sig").read()
        text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        starts = [m.start() for m in re.finditer(r"\.Init\(0x", text)]
        for n, s in enumerate(starts):
            block = text[s:starts[n + 1] if n + 1 < len(starts) else len(text)]
            t = int(re.match(r"\.Init\((0x[0-9a-fA-F]+)", block).group(1), 16)
            d = {"burst": None, "chase": "PetChasingEnemy" in block, "shots": []}
            target = re.search(r"PetAttackTarget\.Instance\(([\d.]+),\s*(\d+),\s*(\d+)\)", block)
            if target:
                d["burst"] = (float(target.group(1)), int(target.group(2)), int(target.group(3)))
            for c in re.finditer(r"Cooldown\.Instance\(", block):
                inner = block[c.end():balanced(block, c.end() - 1) - 1]
                m = re.match(r"\s*(\d+)\s*,\s*(.*)$", inner, re.S)
                if m:
                    shot = parse_shot(m.group(2))
                    if shot:
                        d["shots"].append((int(m.group(1)),) + shot)
            defs[t] = d
    return defs


def shoot(radius, count, angle, index, cooldown):
    args = ["%g" % radius, "%d" % count]
    if count > 1:
        args.append("shootAngle: %g" % angle)
    args.append("projectileIndex: %d" % index)
    args.append("coolDown: %d" % cooldown)
    return "new AllyShoot(%s)" % ", ".join(args)


def write_ally_behaviors(dr_root, chosen):
    defs = parse_ally_behaviors(dr_root)
    lines = []
    for oid, o in sorted(chosen.items(), key=lambda kv: kv[0]):
        if o.findtext("Class") != "Character" or not o.get("type"):
            continue
        d = defs.get(int(o.get("type"), 16))
        if not d:
            continue
        if d["burst"]:
            radius, count, index = d["burst"]
            state = ('new State(new State("fire", %s, new TimedTransition(500, "die")), new State("die", new Suicide()))'
                     % shoot(radius, count, 10, index, 10000))
        elif d["chase"]:
            slow = SUMMON_FIRE_SLOWDOWN if oid.endswith(" Summon") else 1
            shots = ", ".join(shoot(r, c, a, i, ms * slow) for ms, r, c, a, i in d["shots"])
            state = ('new State(new State("attack", new AllyCharge(1, 10, 1), %s, new NoEntityWithinAllyTransition(10, false, "follow")), '
                     'new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack")))' % shots)
        else:
            state = "new State(%s)" % ", ".join(shoot(r, c, a, i, ms) for ms, r, c, a, i in d["shots"])
        lines.append("            .Init(%s, %s)" % (cs_string(oid), state))

    with open(ALLY_BEHAVIORS_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_allies.py). Do not edit.\n")
        f.write("// Turrets, summons and pearl substitutes spawned by the abilities of the classes ported from\n")
        f.write("// Doomed Realms, as betterSkillys allies.\n")
        f.write("using WorldServer.logic.behaviors;\nusing WorldServer.logic.transitions;\n\n")
        f.write("namespace WorldServer.logic\n{\n    partial class BehaviorDb\n    {\n")
        f.write("        private _ DoomedAllies = () => Behav()\n")
        f.write("\n".join(lines) + ";\n")
        f.write("    }\n}\n")
    return len(lines)
