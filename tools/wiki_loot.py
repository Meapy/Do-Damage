"""The wiki's Loot Simulation page (website/wiki/loot-simulation.md, {{gen:loot_sim}}): what Oryx 3, 4
and 5 drop, read from their behaviours, with the rules the server rolls it by (Loots.cs), for
website/wiki/loot.js to show and to roll kills with.

- Rows: every ItemLoot and TierLoot in the boss's .Init block, in order, each with its chance and
  the damage share it needs (the Threshold round it, 0 outside one). Seasonal rows are left out.
- Tier rows: the pool Loots.Initialize fills for that kind and tier, every item weighted 1 but the
  untiered ones that join a pool (TierLoot.ExtraPoolItems) at their own weight. A Doomed Realms item
  drops in the best bag of the other items really of its tier, as the server gives it.
- The boosts: the loot event and weekend boosts from wServer.json, the Loot Drop Potion's from
  Loots.cs, and the bag ranks (LOOT_BAG_WEIGHTS) that decide a kill's bag colour.

Run it to print the data: python tools/wiki_loot.py
"""
import json
import os
import re

import wiki_data as wd

LOOTS = os.path.join(wd.LOGIC, "loot", "Loots.cs")
MOBDROPS = os.path.join(wd.LOGIC, "loot", "MobDrops.cs")
CONFIG = os.path.join(wd.ROOT, "source", "WorldServer", "wServer.json")

# (key, short name, the monster, its dungeon); more bosses can join later
BOSSES = [
    ("o3", "Oryx 3", "Oryx the Mad God 3", "Oryx's Sanctuary"),
    ("o4", "Oryx 4", "Oryx the Mad God 4", "Oryx's Inferno"),
    ("o5", "Oryx 5", "Oryx the Ascended God", "Oryx's Elysium"),
]
BAG_NAMES = ["Brown", "Pink", "Purple", "Egg", "Blue", "Green", "White", "Gold", "Orange", "Red"]
KINDS = ("Weapon", "Ability", "Armor", "Ring")

ITEM = re.compile(r'new ItemLoot\(\s*(?:item:\s*)?"([^"]+)"(?:\s*,\s*(?:probability:\s*)?([0-9.]+))?')
TIER = re.compile(r'new TierLoot\(\s*(?:tier:\s*)?(\d+)\s*,\s*(?:type:\s*)?ItemType\.(\w+)'
                  r'(?:\s*,\s*(?:probability:\s*)?([0-9.]+))?')


def _blank_comments(text):
    """Comments as spaces, so positions stay where they were."""
    return re.sub(r'//[^\n]*', lambda m: " " * len(m.group(0)), text)


def _spans(text, call):
    """[(start, end, first argument)] of each `call(...)` in text, end at its closing bracket."""
    out = []
    for m in re.finditer(re.escape(call) + r'\(', text):
        depth, j = 1, m.end()
        while depth and j < len(text):
            depth += {"(": 1, ")": -1}.get(text[j], 0)
            j += 1
        arg = re.match(r'\s*([0-9.]+)', text[m.end():])
        out.append((m.start(), j, float(arg.group(1)) if arg else 0.0))
    return out


def rows_of(block):
    """[(kind, id or (type, tier), chance, threshold)] of a boss's loot, in the order it's written."""
    text = _blank_comments(block)
    thresholds = _spans(text, "new Threshold")
    seasonal = _spans(text, "new SeasonalThreshold")

    def threshold(pos):
        inside = [t for t in thresholds if t[0] <= pos < t[1]]
        return max(inside, key=lambda t: t[0])[2] if inside else 0.0

    found = []
    for m in ITEM.finditer(text):
        found.append((m.start(), "item", m.group(1), float(m.group(2) or 1)))
    for m in TIER.finditer(text):
        found.append((m.start(), "tier", (m.group(2), int(m.group(1))), float(m.group(3) or 1)))
    out = []
    for pos, kind, what, p in sorted(found):
        if any(s[0] <= pos < s[1] for s in seasonal):
            continue
        out.append((kind, what, p, threshold(pos)))
    return out


def _extra_weights():
    """{id: (tier, weight)} of the untiered items in the tier pools (TierLoot.ExtraPoolItems)."""
    src = wd._read(MOBDROPS)
    return {i: (int(t), float(w)) for i, t, w in re.findall(r'\["([^"]+)"\]\s*=\s*\((\d+),\s*([0-9.]+)\)', src)}


def bag_ranks():
    m = re.search(r'LOOT_BAG_WEIGHTS\s*=\s*\[([^\]]*)\]', wd._read(LOOTS))
    return [int(n) for n in re.findall(r'\d+', m.group(1))]


def boosts():
    """{event, weekend, ld}: what each adds to the chance multiplier."""
    cfg = json.loads(wd._read(CONFIG)).get("serverSettings", {})
    ld = re.search(r'LDBoostTime\s*>\s*0\s*\?\s*([0-9.]+)', wd._read(LOOTS))
    return {"event": float(cfg.get("lootEvent", 0)), "weekend": float(cfg.get("wkndBoost", 0)),
            "ld": float(ld.group(1)) if ld else 0.0}


def pools(d):
    """{(kind, tier): [(id, weight)]} as Loots.Initialize fills them, and {id: its bag} for the Doomed
    Realms items in them, raised to the best bag of the other items really of their tier."""
    loot = wd.tier_loot_types()
    extras = _extra_weights()
    out, doomed = {}, []
    for oid, o in d.objects.items():
        if not o["item"] or o["class"] != "Equipment":
            continue
        kind = loot["types"].get(o["slot"])
        if kind is None:
            continue
        if oid in extras:
            out.setdefault((kind, extras[oid][0]), []).append((oid, extras[oid][1]))
            continue
        is_doomed = 0xD000 <= o["type"] <= 0xDFFF
        if is_doomed and (o["slot"] not in loot["doomed"] or o["tier"] < 0) and oid not in loot["doomed_items"]:
            continue
        if o["tier"] < 0:
            continue
        out.setdefault((kind, o["tier"]), []).append((oid, 1.0))
        if is_doomed:
            doomed.append((oid, kind))
    rank = bag_ranks()
    bags = {}
    for oid, kind in doomed:
        o = d.objects[oid]
        own = [d.objects[i]["bag"] for i, _ in out[(kind, o["tier"])]
               if not 0xD000 <= d.objects[i]["type"] <= 0xDFFF and d.objects[i]["tier"] == o["tier"]]
        best = max(own + [o["bag"]], key=lambda b: rank[b])
        bags[oid] = best if rank[best] > rank[o["bag"]] else o["bag"]
    return out, bags


def page_data(gen):
    """The page's data: the bosses, their rows, every item's look, the boosts and the bags. `gen` is
    build_wiki's Gen (its gear pages give the icons and links)."""
    d, eq = gen.d, gen.equip
    pool, doomed_bags = pools(d)
    items = {}

    def item(oid):
        if oid not in items:
            o = d.objects.get(oid, {})
            it = next((i for i in eq.items if i["id"] == oid), None)
            edge = eq.edge(it) if it else "var(--line-hi)"
            items[oid] = {"name": d.name(oid), "bag": doomed_bags.get(oid, o.get("bag", 0)),
                          "ico": eq.icon(it) if it else eq.sprite(d.texture(oid), edge),
                          "href": eq.href.get(oid), "gear": bool(it), "edge": edge}
        return oid

    bosses = []
    for key, short, monster, world in BOSSES:
        rows = []
        for block in d.inits.get(monster, []):
            for kind, what, p, thr in rows_of(block):
                if kind == "item":
                    if what in d.objects:
                        rows.append({"kind": "item", "id": item(what), "p": p, "thr": thr})
                else:
                    t, tier = what
                    members = pool.get((t, tier), [])
                    rows.append({"kind": "tier", "type": t, "tier": tier, "p": p, "thr": thr,
                                 "pool": [[item(i), w] for i, w in members]})
        bosses.append({"key": key, "short": short, "title": d.name(monster), "world": world,
                       "hp": d.boss_health(monster), "ico": eq.sprite(d.texture(monster), "var(--line-hi)"),
                       "rows": rows})
    bag_icons = [eq.sprite(d.texture("Loot Bag %d" % b), "transparent") for b in range(len(BAG_NAMES))]
    return {"bosses": bosses, "items": items, "boosts": boosts(), "bagRank": bag_ranks(),
            "bagNames": BAG_NAMES, "bagIcons": bag_icons}


if __name__ == "__main__":
    d = wd.Data()
    p, bags = pools(d)
    for key, short, monster, world in BOSSES:
        print("==", short, monster, d.boss_health(monster))
        for block in d.inits.get(monster, []):
            for row in rows_of(block):
                extra = " (%d items)" % len(p.get(row[1], [])) if row[0] == "tier" else ""
                print("  %-5s %-40s %6.3f  over %d%%%s" % (row[0], row[1], row[2], row[3] * 100, extra))
    print("boosts", boosts(), "bag ranks", bag_ranks())
    print("raised Doomed bags", {k: v for k, v in bags.items() if v != d.objects[k]["bag"]})
