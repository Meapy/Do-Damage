"""What each stage of the Oryx chain drops, per player and kill, as the loot code rolls it.

    python tools/oryx_loot_report.py [--event 0.25] [--weekend 0.3] [--ref main] [--compare] [--items]

Reads the loot tables of the chain's bosses from the behaviour files and works out the expected
drops of one player in a group of 1, 3 and 5 players who share the damage evenly. It follows
`Loot.Handle` (logic/loot/Loots.cs): each player rolls every entry whose threshold is below their
share of the damage, at the listed chance times 1 + their share + the loot boosts (the server-wide
event, weekends, a Loot Drop Potion), capped at certain. Spawned monsters drop nothing; Elder and
seasonal loot is left out.

--ref reads the tables as they are in a git commit (e.g. `--ref main`), to compare before and after.
--compare adds the bosses of other late dungeons, for scale. --items lists, per stage, the chance of
each white-bag and special-bag item for one of three players.
"""
import argparse
import glob
import os
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DB = "source/WorldServer/logic/db"

# (label, file, [(monster, how many of it the stage has)])
COURT = ["Oryx Noble", "Oryx Aristocrat", "Oryx Officer", "Oryx Sergeant", "Oryx Major", "Oryx Deacon",
         "Oryx Cleric", "Oryx Cardinal"]
STAGES = [
    ("1 Stone Guardians (both)", "BehaviorDb.OryxCastle.cs", [("Oryx Stone Guardian Right", 1), ("Oryx Stone Guardian Left", 1)]),
    ("2 Janus", "BehaviorDb.OryxCastle.cs", [("md Janus the Doorwarden", 1)]),
    ("3 Oryx 1", "BehaviorDb.Oryx.cs", [("Oryx the Mad God 1", 1)]),
    ("3 Giant Oryx Chicken", "BehaviorDb.ChickenChamber.cs", [("TestChicken 2", 1)]),
    ("4 Oryx 2", "BehaviorDb.Oryx.cs", [("Oryx the Mad God 2", 1)]),
    # tools/build_oryx_sanctuary.py: 4 nobles in the Gallery, 2 of each of the other six in the Court
    ("5 Sanctuary court (12)", "BehaviorDb.OryxSanctuary.cs",
     [("Oryx Noble", 3), ("Oryx Aristocrat", 3)] + [(m, 1) for m in COURT[2:]]),
    ("5 Oryx 3", "BehaviorDb.OryxSanctuary.cs", [("Oryx the Mad God 3", 1)]),
    ("6 Oryx 4", "BehaviorDb.OryxDoomed.cs", [("Oryx the Mad God 4", 1)]),
    ("7 Oryx 5", "BehaviorDb.Elysium.cs", [("Oryx the Ascended God", 1)]),
]
COMPARE = [
    ("Lost Halls: Marble Colossus", "BehaviorDb.LostHalls.cs", [("LH Marble Colossus", 1)]),
    ("Shatters: King's balloon", "BehaviorDb.Shatters.cs", [("shtrs Loot Balloon King", 1)]),
    ("Tomb: three bosses", "BehaviorDb.Tomb.cs", [("Tomb Defender", 1), ("Tomb Attacker", 1), ("Tomb Support", 1)]),
    ("Ocean Trench: Thessal", "BehaviorDb.OceanTrench.cs", [("Thessal the Mermaid Goddess", 1)]),
]

STATS = {"Potion of Attack", "Potion of Defense", "Potion of Speed", "Potion of Dexterity",
         "Potion of Vitality", "Potion of Wisdom"}
ORYX_SET = {"Ring of Oryx", "Oryx Hide Armor", "Oryx Hide Robe", "Oryx Scale Armor", "Oryx Tooth Sword",
            "Doom Dirk", "Flying Doom Daggers", "Tome of Noble Assault"}

REF = None


def read(rel):
    if REF:
        return subprocess.run(["git", "-C", ROOT, "show", "%s:%s" % (REF, rel)], capture_output=True,
                              check=True, encoding="utf-8").stdout.lstrip("﻿")
    with open(os.path.join(ROOT, rel), encoding="utf-8-sig") as f:
        return f.read()


def bag_types():
    """{item id: <BagType>} from the server's XML."""
    out = {}
    for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
        try:
            tree = ET.parse(f)
        except ET.ParseError:
            continue
        for o in tree.iter("Object"):
            if o.findtext("BagType"):
                out[o.get("id")] = int(o.findtext("BagType"))
    return out


BAGS = None


def close_paren(s, i):
    """Index of the parenthesis closing the one at s[i]."""
    depth = 0
    for j in range(i, len(s)):
        if s[j] == "(":
            depth += 1
        elif s[j] == ")":
            depth -= 1
            if depth == 0:
                return j
    return len(s)


def templates(text):
    """{name: body} of the MobDrops[] helpers in a file (LootTemplates, CourtLoot, ...)."""
    out = {}
    for m in re.finditer(r"static MobDrops\[\] (\w+)\(\)", text):
        a = m.end()
        b = text.find(";", a)
        out[m.group(1)] = text[a:b]
    return out


def entries(text, helpers, thr=0.0):
    """[(item, chance, threshold)] of loot source text, following Threshold nesting."""
    out = []
    spans = []
    for m in re.finditer(r"new (Threshold|SeasonalThreshold)\(([\d.]+)|ElderLoot\(([\d.]+)|"
                         r"new SeasonalThreshold\(\"\w+\", ([\d.]+)", text):
        start = text.index("(", m.start())
        kind = m.group(1) or ("Elder" if m.group(3) else "SeasonalThreshold")
        value = float(m.group(2) or m.group(3) or m.group(4))
        spans.append((start, close_paren(text, start), kind, value))

    def threshold_at(pos, own):
        inner = None
        for a, b, kind, value in spans:
            if a < pos < b and (inner is None or a > inner[0]):
                inner = (a, b, kind, value)
        if inner is None:
            return own if own is not None else thr
        if inner[2] != "Threshold":
            return None     # elder or seasonal: not counted
        return inner[3]

    for m in re.finditer(r'new ItemLoot\("([^"]+)"(?:, ([\d.]+))?(?:, (\d+))?(?:, ([\d.]+))?', text):
        t = threshold_at(m.start(), float(m.group(4)) if m.group(4) else None)
        if t is not None:
            out.append((m.group(1), float(m.group(2) or 1), t))
    for m in re.finditer(r"new TierLoot\((\d+), ItemType\.(\w+)(?:, ([\d.]+))?(?:, (\d+))?(?:, ([\d.]+))?", text):
        t = threshold_at(m.start(), float(m.group(5)) if m.group(5) else None)
        if t is not None:
            out.append(("T%s %s" % (m.group(1), m.group(2)), float(m.group(3) or 1), t))
    for m in re.finditer(r"(?:LootTemplates\.)?(\w+)\(\)", text):
        if m.group(1) in helpers:
            t = threshold_at(m.start(), None)
            if t is not None:
                out += entries(helpers[m.group(1)], helpers, t)
    return out


def loot_of(fname, name):
    """[(item, chance, threshold)] of one Init."""
    s = read(DB + "/" + fname)
    helpers = templates(s)
    helpers.update(templates(read(DB.replace("/db", "/loot") + "/MobDrops.cs")))
    m = re.search(r'\.Init\(\s*(?:E\()?"%s"' % re.escape(name), s)
    a = m.start()
    n = re.compile(r"\.Init(?:Doomed)?\(").search(s, m.end())
    return entries(s[a:n.start() if n else len(s)], helpers)


def tier(item):
    m = re.match(r"T(\d+) (\w+)$", item)
    return (int(m.group(1)), m.group(2)) if m else None


def is_top(t):
    return (t[1] in ("Weapon", "Armor") and t[0] >= 13) or (t[1] in ("Ability", "Ring") and t[0] >= 6)


def stage_totals(f, names, share, mult):
    totals = dict(stats=0.0, life=0.0, mana=0.0, oryx=0.0, gear=0.0, top=0.0, none=1.0, set=0.0)
    per_item = {}
    for n, count in names:
        for item, p, thr in loot_of(f, n):
            if thr >= share:     # Loots.cs: Threshold < share of the damage
                continue
            e = min(1.0, p * mult)
            t = tier(item)
            if item in STATS:
                totals["stats"] += e * count
            elif item in ("Potion of Life", "Greater Potion of Life"):
                totals["life"] += e * count
            elif item in ("Potion of Mana", "Greater Potion of Mana"):
                totals["mana"] += e * count
            elif item == "Potion of Oryx":
                totals["oryx"] += e * count
            elif t:
                totals["top" if is_top(t) else "gear"] += e * count
            if BAGS.get(item) == 6:
                # chance of at least one white-bag item
                totals["none"] *= (1 - e) ** count
            if item in ORYX_SET:
                totals["set"] += e * count
            if BAGS.get(item) in (6, 7) and not t:
                per_item[item] = 1 - (1 - per_item.get(item, 0)) * (1 - e) ** count
    totals["white"] = 1 - totals.pop("none")
    return totals, per_item


def report(group, boost, stages):
    share = 1.0 / group
    mult = 1 + share + boost
    print("\n%d player%s (each %.0f%% of the damage): chances x%.2f" % (group, "" if group == 1 else "s", share * 100, mult))
    print("  %-28s %6s %5s %5s %6s %6s %7s %7s %8s" % ("stage", "stats", "life", "mana", "oryx", "gear", "topgear", "oryxset", "white"))
    for label, f, names in stages:
        t, _ = stage_totals(f, names, share, mult)
        print("  %-28s %6.2f %5.2f %5.2f %5.0f%% %6.2f %7.2f %6.1f%% %7.1f%%" % (
            label, t["stats"], t["life"], t["mana"], t["oryx"] * 100, t["gear"], t["top"],
            t["set"] * 100, t["white"] * 100))


def items(boost, stages):
    share = 1.0 / 3
    mult = 1 + share + boost
    print("\nWhite- and special-bag items, chance for one of three players per kill:")
    for label, f, names in stages:
        _, per = stage_totals(f, names, share, mult)
        if per:
            print("  %-28s %s" % (label, ", ".join("%s %.1f%%" % (i, c * 100) for i, c in sorted(per.items(), key=lambda x: -x[1]))))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--event", type=float, default=0.25, help="serverSettings.lootEvent (live: 0.25)")
    ap.add_argument("--weekend", type=float, default=0.0, help="serverSettings.wkndBoost on a weekend (live: 0.3)")
    ap.add_argument("--ref", help="read the behaviour files from this git commit instead of the working tree")
    ap.add_argument("--compare", action="store_true", help="add the bosses of other late dungeons")
    ap.add_argument("--items", action="store_true", help="list the white- and special-bag items per stage")
    args = ap.parse_args()
    global BAGS, REF
    BAGS = bag_types()
    REF = args.ref
    stages = STAGES + (COMPARE if args.compare else [])
    print("Expected drops per player and kill%s. stats: the six stat potions; life/mana include the Greater"
          % (" (as of %s)" % REF if REF else ""))
    print("ones; oryx: Potion of Oryx; gear / topgear: tiered items (top: T13+ weapons and armour, T6+ abilities")
    print("and rings); oryxset: expected Oryx set items; white: chance of at least one white-bag item (the Oryx")
    print("set, Oryx's Escutcheon, seals...).")
    for group in (1, 3, 5):
        report(group, args.event + args.weekend, stages)
    if args.items:
        items(args.event + args.weekend, STAGES)


if __name__ == "__main__":
    main()
