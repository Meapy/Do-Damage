"""Forge recipes, sell prices and shops for tools/port_doomed.py.

Reads them from the Doomed Realms server code and data:
  * forge recipes: wserver/realm/entities/player/extras/Combinations.cs (AddCombo / AddAdvCombo)
  * fame for selling items: wserver/realm/entities/player/extras/Prices.cs (AddPrice)
  * shops: wserver/realm/entities/merchant/MerchantLists.cs (prices and store lists by type),
    items with <Key>price</Key> (the key shop) and <Shop><Name/><Price/></Shop> (gold shops) in
    db/data/*.xml
and writes them, by object id, to source/WorldServer/core/commands/doomed/DoomedEconomyData.cs.
Items that exist in neither game are left out and listed.
"""

import difflib
import glob
import os
import re

from port_doomed_monsters import strip_comments
from port_doomed_pets import cs_string

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_CS = os.path.join(ROOT, "source", "WorldServer", "core", "commands", "doomed", "DoomedEconomyData.cs")

# Doomed Realms' "Default" shop sold five starter weapons, and its store lists (MerchantLists
# store2List..store10List) were meant for store regions its shop map never had; here each list is a
# shop of its own. Shop name -> store lists.
STORE_SHOPS = {
    "abilities": ["store2List"],
    "weapons": ["store3List"],
    "armor": ["store4List"],
    "consumables": ["store5List", "store10List"],
    "rings": ["store6List"],
    "dungeonkeys": ["store7List"],
    "dyes": ["store8List", "store9List"],
}
# items MerchantLists prices but puts in no store list
EXTRA_CONSUMABLES = [2878, 0xAEB, 0xAEC]  # Amulet of Resurrection, Greater Health / Magic Potion
PET_SHOP_TYPES = range(0x1500, 0x1541)  # MerchantLists.AddPetShop
PET_PRICE = 1250  # Doomed Realms sold them for no price; betterSkillys' pet generator price
DEFAULT_SHOP = "abilities"
# items no shop sells: pets taken out of the game (whoever has one keeps it), and from the rings
# pass (SETUP-LOCAL.md, "Untiered rings") the admin items, the reward rings and the Amulet of
# Dispersion, which drops in the Haunted Cemetery instead, and (the daggers pass) the Dirk of Cronus
NOT_SOLD = {"Twilight Generator", "Dinosaur's Pet Generator", "Crown", "God Rushing", "America Ring",
            "Amulet of Resurrection", "Dirk of Cronus", "Staff of Endless Energy",
            "America Sword", "Turkey Leg of Doom", "America Shield", "Spell of Invisibility", "Spell of Life",
            "Spell of Shielding", "Spell of Teleportation", "Tome of Poison", "Tome of Speed", "Tome of the Dead",
            "Tome of Shining Stars",
            # Oryx 4's alone (the Oryx UT pass), and Lil' Oryx, which Oryx 4 and 5 drop
            "Doom Poison", "Lil' Oryx Generator",
            # no longer needed: Oryx 1 leaves the Wine Cellar portal open (players keep and can sell theirs)
            "Wine Cellar Incantation"}
# items gone from the forge (the Oryx UT pass): the old forge materials, which the forge shards
# replaced, and the Forge Amulet. No recipe takes or makes them, and the forge doesn't buy them.
FORGE_GONE = {"Shattered Rune of Oryx", "Broken Oryx Armor", "Purified Doom Saliva", "Forge Amulet"}
# results the Divine Forge doesn't make: the admin items, the rewards, and the very rare drops of
# Oryx 5 its Doomed Realms recipes would make easy (the T16 armours only he drops), and the Elder
# Draconis Key (bought at the Bazaar instead of melded from the Lair of Draconis's four relics)
NOT_FORGED = {"Crown", "Anatis Staff", "Staff of Endless Energy", "Eternal Sword",
              "Sphinx Leather Armor", "Armor of the Pyramid", "Robe of the Nile", "Elder Draconis Key"}


def _num(s):
    return int(s, 16) if s.lower().startswith("0x") else int(s)


def _strings(text):
    return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def parse_combos(dr_root):
    t = strip_comments(open(os.path.join(dr_root, "wserver", "realm", "entities", "player", "extras", "Combinations.cs"),
                            encoding="utf-8-sig").read())
    out = []
    for m in re.finditer(r"\b(AddCombo|AddAdvCombo)\(\s*\"([^\"]+)\"\s*,\s*(\d+)\s*,(.*?)\);", t, re.S):
        out.append((m.group(2), int(m.group(3)), m.group(1) == "AddAdvCombo", _strings(m.group(4))))
    return out


def parse_sell_prices(dr_root):
    t = strip_comments(open(os.path.join(dr_root, "wserver", "realm", "entities", "player", "extras", "Prices.cs"),
                            encoding="utf-8-sig").read())
    out = {}
    for m in re.finditer(r"\bAddPrice\(\s*(\d+)\s*,(.*?)\);", t, re.S):
        for item in _strings(m.group(2)):
            out.setdefault(item, int(m.group(1)))  # Doomed Realms kept the first price of a duplicate
    return out


def parse_merchant_lists(dr_root):
    t = strip_comments(open(os.path.join(dr_root, "wserver", "realm", "entities", "merchant", "MerchantLists.cs"),
                            encoding="utf-8-sig").read())
    prices = {}
    for m in re.finditer(r"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*new Tuple<int,\s*CurrencyType>\(\s*(\d+)\s*,\s*CurrencyType\.(\w+)\s*\)\s*\}", t):
        prices[_num(m.group(1))] = (int(m.group(2)), m.group(3))
    lists = {}
    for m in re.finditer(r"public static int\[\] (store\d+List)\s*=\s*\{(.*?)\}", t, re.S):
        lists[m.group(1)] = [_num(x) for x in re.findall(r"0x[0-9a-fA-F]+|\d+", m.group(2))]
    return prices, lists


def xml_shops(dr_root):
    """[(item id, shop, price, currency)] from <Key> and <Shop> elements (comments excluded)."""
    out = []
    for f in sorted(glob.glob(os.path.join(dr_root, "db", "data", "*.xml"))):
        t = re.sub(r"<!--.*?-->", "", open(f, encoding="utf-8", errors="replace").read(), flags=re.S)
        for m in re.finditer(r'<Object\b[^>]*\bid="([^"]+)"[^>]*>(.*?)</Object>', t, re.S):
            body = m.group(2)
            if not re.search(r"<Class>\s*(Equipment|Dye|Pet)\s*</Class>", body):
                continue
            key = re.search(r"<Key>\s*(\d+)\s*</Key>", body)
            if key:
                out.append((m.group(1), "keys", int(key.group(1)), "Fame"))
            shop = re.search(r"<Shop>.*?<Name>\s*([^<]+?)\s*</Name>.*?<Price>\s*(\d+)\s*</Price>.*?</Shop>", body, re.S)
            if shop:
                out.append((m.group(1), shop.group(1), int(shop.group(2)), "Gold"))
    return out


def write_economy(dr_root, dr, bs, final_id, known):
    dr_by_type = {int(o.get("type"), 16): oid for oid, o in dr.items() if o.get("type")}
    all_ids = sorted(set(dr) | set(bs))
    missing, fixed = set(), {}

    def resolve(name):
        """Ported/betterSkillys id for a Doomed Realms item name (typos fixed by closest match)."""
        oid = final_id(name)
        if known(oid):
            return oid
        close = difflib.get_close_matches(name, all_ids, n=1, cutoff=0.9)
        if close and known(final_id(close[0])):
            fixed[name] = close[0]
            return final_id(close[0])
        missing.add(name)
        return None

    def by_type(t_):
        return resolve(dr_by_type[t_]) if t_ in dr_by_type else None

    combos = []
    for result, price, adv, items in parse_combos(dr_root):
        r, ids = resolve(result), [resolve(i) for i in items]
        if r and all(ids) and r not in NOT_FORGED and r not in FORGE_GONE and not FORGE_GONE & set(ids) and not adv:
            combos.append((r, price, adv, ids))

    sell = {}
    for name, price in parse_sell_prices(dr_root).items():
        oid = resolve(name)
        if oid and oid not in FORGE_GONE:
            sell.setdefault(oid, price)

    prices, lists = parse_merchant_lists(dr_root)
    shops = []  # (shop, item, price, currency)
    for shop, names in STORE_SHOPS.items():
        types = [t_ for n in names for t_ in lists.get(n, [])]
        if shop == "consumables":
            types += EXTRA_CONSUMABLES
        seen = set()
        for t_ in types:
            oid = by_type(t_)
            if oid and t_ in prices and oid not in seen:
                seen.add(oid)
                shops.append((shop, oid, prices[t_][0], prices[t_][1]))
    for t_ in PET_SHOP_TYPES:
        oid = by_type(t_)
        if oid and dr.get(dr_by_type[t_]) is not None and "Generator" in dr_by_type[t_]:
            shops.append(("pets", oid, PET_PRICE, "Fame"))
    seen = set()
    for name, shop, price, currency in xml_shops(dr_root):
        oid = resolve(name)
        if oid and (shop, oid) not in seen:
            seen.add((shop, oid))
            shops.append((shop, oid, price, currency))
    shops = [s for s in shops if s[1] not in NOT_SOLD]

    with open(OUT_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_economy.py) from the Doomed Realms forge\n")
        f.write("// recipes (Combinations.cs), sell prices (Prices.cs) and shops (MerchantLists.cs, <Key>, <Shop>).\n")
        f.write("// Do not edit.\nusing Shared.resources;\n\n")
        f.write("namespace WorldServer.core.commands.doomed\n{\n    internal static class DoomedEconomyData\n    {\n")
        f.write("        public const string DefaultShop = %s;\n\n" % cs_string(DEFAULT_SHOP))
        f.write("        // result, fame, needs a Forge Amulet, ingredients\n")
        f.write("        public static readonly (string Result, int Fame, bool Advanced, string[] Items)[] Recipes =\n        {\n")
        for r, price, adv, ids in combos:
            f.write("            (%s, %d, %s, new[] { %s }),\n" % (cs_string(r), price, "true" if adv else "false",
                                                              ", ".join(cs_string(i) for i in ids)))
        f.write("        };\n\n        // item, fame it sells for\n")
        f.write("        public static readonly (string Item, int Fame)[] SellPrices =\n        {\n")
        for oid, price in sorted(sell.items()):
            f.write("            (%s, %d),\n" % (cs_string(oid), price))
        f.write("        };\n\n        // shop, item, price, currency\n")
        f.write("        public static readonly (string Shop, string Item, int Price, CurrencyType Currency)[] ShopItems =\n        {\n")
        for shop, oid, price, currency in shops:
            f.write("            (%s, %s, %d, CurrencyType.%s),\n" % (cs_string(shop), cs_string(oid), price, currency))
        f.write("        };\n    }\n}\n")

    for k, v in sorted(fixed.items()):
        print("  economy: %r taken as %r" % (k, v))
    if missing:
        print("  economy: left out (no such item): %s" % ", ".join(sorted(missing)))
    print("Wrote %s: %d forge recipes, %d sell prices, %d shop items in %d shops."
          % (OUT_CS, len(combos), len(sell), len(shops), len({s[0] for s in shops})))
