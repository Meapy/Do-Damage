"""Builds the forge shards and the Ascended items: their sprites and their item XML.

    python tools/build_ascended_items.py [--preview <png>]

The forge shards: an Infernal and an Ascended shard for each kind of gear (weapon, ability,
armor, accessory), dropped by Oryx 4 and Oryx 5. Used, a shard goes to the account's forge
storage (ForgeStorage.cs), which the Divine Forge takes them from.

The Ascended items: what nothing drops, forged from an item of Oryx the Ascended God's top tier
and an Ascended shard (ForgeRecipes.Ascended), marked <Ascended/> (the red slot edge, the wiki's
Ascended group, and a drop bag only their owner sees); all are named Enthroned, and each gives a
10% fame bonus (FAME_BONUS). Each has the numbers of the item it's forged from (its stats, its
ability, its shots' damage and what they do: a katana's still pierce, dual blades' still ignore
defense), but for STATS. Every Enthroned weapon flies the same two shots of Oryx 5's (SHOTS, drawn
here at 16 by 16), whose volleys take turns (<AlternateProjectiles/>, Player.NoteWeaponShot): his
gold Radiance with the weapon's own numbers, then his violet Rift, the same but piercing and
ignoring defense. A third, his crystal Sigil, is what the imprints' own shots look like (the
Battle Mage's bolts, the Gathered Shot's arrow, the Spearform thrust). All but the Fists fly a
shot of their own in place of the Radiance (OWN_SHOTS). The weapons, their shots, the armours and
the boots are hand-drawn by the owner on EnthronedItems8x8.png (ITEMS8); the Fists keep their
source's sprite, with a few purple sparkles added round it (SPARKLE); his helm (and the robe and
boots no longer used) is drawn here at 8 by 8, and his staff (no longer used), two rings and the
three shots at 16 by 16, on `AscendedItems16x16.png` (`ascendedItems16x16`, in the order of SPRITES16): the staff his
own sprite holds, gold with a crystal head, sideways as items lie (STAFF_HALF), the rings gold at
the bottom, light blue up the sides and purple at the top, under a gem in its setting (ring), and
the shots pointing up and right as projectiles lie (draw_shot).

Writes `AscendedItems8x8.png` (client/src/kabam/rotmg/assets/custom/images, `ascendedItems8x8`,
which tools/gen_rs_manifest.py registers for the Rust client), 8 by 8 pixels a cell in the order
of SPRITES, and the items and the shots' projectile objects into both copies of CustomItems.xml,
between the BEGIN and END marks (replacing what's there). Type codes 0x5840 on are kept:
characters store items by type code, so never renumber them. Needs Pillow.
"""

import copy
import math
import os
import re
import sys
import xml.etree.ElementTree as ET

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rework_buff_auras  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
SERVER_XML = os.path.join(ROOT, "source", "Shared", "resources", "xml")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "AscendedItems8x8.png")
OUT16 = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "AscendedItems16x16.png")
ITEM_XMLS = [
    os.path.join(SERVER_XML, "custom", "CustomItems.xml"),
    os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "xmls", "CustomItems.xml"),
]
SHEET = "ascendedItems8x8"
SHEET16 = "ascendedItems16x16"
# the Enthroned rings, hand-drawn by the owner (EnthronedRings8x8.png: Wrath, then Grace); their
# 16 by 16 cells are still drawn but no longer used
RINGS8 = "enthronedRings8x8"
# the Enthroned weapons, their shots, armours and boots, hand-drawn by the owner
# (EnthronedItems8x8.png, 7 cells a row): the weapons on the top row and each one's shot under it,
# in the order of OWN_SHOTS; the Hide, Plate and Robe on the fourth row; the Boots on the sixth
ITEMS8 = "enthronedItems8x8"
ITEMS8_COLS = 7
CELL = 8
COLS = 8
BEGIN = "<!-- BEGIN forge shards and Ascended items (tools/build_ascended_items.py) -->"
END = "<!-- END forge shards and Ascended items -->"

# Oryx 5's colours, dark to light (ElysiumOryx16x16.png, as tools/draw_t15_weapons.py has them)
SILVER = [(40, 30, 86), (104, 112, 170), (152, 170, 216), (238, 242, 252), (255, 255, 255)]
PURPLE = [(64, 30, 112), (124, 64, 196), (176, 124, 240)]
GOLD = [(168, 112, 26), (240, 192, 64), (255, 238, 150)]

# Each shard kind's shape, '1' dark to '4' light
SHAPES = {
    "Weapon": [
        ".....114",
        "....1443",
        "...14431",
        "..14321.",
        ".14321..",
        "14321...",
        "1321....",
        ".11.....",
    ],
    "Ability": [
        "...11...",
        "..1431..",
        ".143321.",
        "14433221",
        "12332211",
        ".122211.",
        "..1211..",
        "...11...",
    ],
    "Armor": [
        ".1111...",
        "14443111",
        "14333321",
        "13332221",
        ".1322221",
        ".132221.",
        "..1221..",
        "...11...",
    ],
    "Accessory": [
        "....1...",
        "...141..",
        ".1.1431.",
        "141.1321",
        "1431.121",
        "13321.1.",
        ".1221...",
        "..11....",
    ],
}
KINDS = ["Weapon", "Ability", "Armor", "Accessory"]

# Each set's ramp for each kind, '1' to '4': Oryx 4's fire, Oryx 5's silver, purple and gold
RAMPS = {
    "Infernal": {
        "Weapon": [(92, 8, 12), (176, 24, 20), (236, 64, 32), (255, 168, 96)],
        "Ability": [(96, 24, 4), (196, 72, 8), (248, 132, 24), (255, 214, 120)],
        "Armor": [(48, 12, 12), (116, 28, 20), (184, 52, 28), (244, 120, 48)],
        "Accessory": [(120, 56, 4), (212, 120, 12), (252, 188, 40), (255, 240, 160)],
    },
    "Ascended": {
        "Weapon": [SILVER[0], SILVER[1], SILVER[2], SILVER[4]],
        "Ability": [PURPLE[0], PURPLE[1], PURPLE[2], SILVER[4]],
        "Armor": [GOLD[0], GOLD[1], GOLD[2], SILVER[4]],
        "Accessory": [SILVER[1], SILVER[2], SILVER[3], GOLD[2]],
    },
}
SETS = {"Infernal": "Oryx the Mad God's", "Ascended": "Oryx the Ascended God's"}

# Oryx 5's own pieces, by colour: k b s w W silver, p P v purple, g G y gold, B K C his staff's
# crystal (ElysiumOryx16x16.png)
O5 = {"k": SILVER[0], "b": SILVER[1], "s": SILVER[2], "w": SILVER[3], "W": SILVER[4],
      "p": PURPLE[0], "P": PURPLE[1], "v": PURPLE[2], "g": GOLD[0], "G": GOLD[1], "y": GOLD[2],
      "B": (110, 200, 245), "K": (120, 228, 255), "C": (196, 240, 255)}
PIECES = {
    "helm": [
        "...Gy...",
        "..bwWb..",
        ".bswwsb.",
        ".sGGGGs.",
        ".sPkkPs.",
        ".bsPPsb.",
        "..b..b..",
        "........",
    ],
    "boots": [
        "...bsw..",
        "...bsw..",
        "..yGGG..",
        "...bsw..",
        "...bswsb",
        "..bswwws",
        "..PPPPPv",
        "..kkkkkk",
    ],
    "robe": [
        "..gyyg..",
        ".bsGGsb.",
        ".bsPPsb.",
        ".bswwsb.",
        "bswPPwsb",
        "bswPvwsb",
        "bswwwwsb",
        "GGGGGGGG",
    ],
}

# The 16 by 16 pieces' colours: his staff's crystal (ElysiumOryx16x16.png), blues, golds, purples
BIG = {"W": (255, 255, 255), "C": (196, 240, 255), "K": (120, 228, 255), "B": (110, 200, 245),
       "b": (60, 120, 200), "y": GOLD[2], "G": GOLD[1], "g": GOLD[0],
       "P": PURPLE[1], "p": PURPLE[0], "v": PURPLE[2], "V": (214, 160, 255)}
DARKER = {"W": "C", "C": "K", "K": "B", "B": "b", "b": "b", "y": "G", "G": "g", "g": "g",
          "V": "v", "v": "P", "P": "p", "p": "p"}

# The Enthroned Staff, lying from bottom left to top right: its upper-left half and its middle
# line (x + y <= 15), {(x, y): colour}; the other half is the mirror of it across the middle line,
# a shade darker. A crystal head in a gold cradle, a purple crossbar with a jewel, a gold shaft
# three pixels across with purple grip bands, and a pommel.
STAFF_HALF = {
    # the crystal
    (15, 0): "W", (14, 1): "W", (13, 2): "C", (12, 3): "C", (11, 4): "K",
    (14, 0): "W", (13, 1): "C", (12, 2): "W", (11, 3): "C", (10, 4): "K",
    (13, 0): "C", (12, 1): "C", (11, 2): "C", (10, 3): "K",
    (12, 0): "K", (11, 1): "C", (10, 2): "K",
    # the gold prongs and cradle
    (10, 1): "y", (9, 2): "G", (8, 3): "G", (8, 4): "G", (9, 4): "G", (9, 5): "G", (10, 5): "G",
    # the crossbar, a jewel where it meets the shaft
    (9, 6): "y", (8, 6): "P", (8, 5): "v", (7, 5): "V", (7, 4): "V",
    (0, 13): "y",  # the pommel
}
for _i in range(9):  # the shaft, from the crossbar to the corner
    _x, _y = 8 - _i, 7 + _i
    STAFF_HALF[(_x, _y)] = "P" if _x - _y in (-5, -7, -13) else "G"
    if _x >= 1:
        STAFF_HALF[(_x - 1, _y)] = "v" if _x - 1 - _y in (-6, -8, -14) else "y"

# Each ring's gem (dark to light) and setting (dark to light)
RINGS = {
    "wrath": ([PURPLE[0], PURPLE[1], PURPLE[2], (214, 160, 255)], [GOLD[0], GOLD[1], GOLD[2]]),
    "grace": ([GOLD[0], GOLD[1], GOLD[2], (255, 250, 220)], [PURPLE[0], PURPLE[1], (214, 160, 255)]),
}
# The 16 by 16 sheet's cells, in order (only ever add to the end)
SPRITES16 = ["staff", "ring wrath", "ring grace", "shot radiance", "shot rift", "shot sigil"]

# The sheets the Ascended weapons and armours copy their sprites from
SOURCE_SHEETS = {
    "t15Weapons8x8": os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "T15Weapons8x8.png"),
    "t16Armors8x8": os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "T16Armors8x8.png"),
}
# the sparkles added round a copied sprite: in the empty pixels touching it nearest these corners
SPARKLE = [((7, 0), (214, 160, 255)), ((0, 7), PURPLE[2]), ((7, 7), (214, 160, 255)), ((0, 0), PURPLE[2])]

# The sheet's cells, in order: the shards, Oryx 5's pieces, then the copied sprites with sparkles
# (("sparkle", sheet, index), which main adds as the items need them)
SPRITES = [("shard", s, k) for s in ("Infernal", "Ascended") for k in KINDS] + \
          [("piece", p, None) for p in PIECES]


def cell_of(kind, a, b=None):
    return SPRITES.index((kind, a, b))


# The shards: (type code, set, kind); the Ascended items: (type code, id, the item whose numbers
# it copies, its texture as (sheet, index) or None for the source's with sparkles, its slot type or
# None for the source's, description). Only ever add to these lists.
SHARDS = [
    (0x5840, "Infernal", "Weapon"), (0x5841, "Infernal", "Ability"),
    (0x5842, "Infernal", "Armor"), (0x5843, "Infernal", "Accessory"),
    (0x5844, "Ascended", "Weapon"), (0x5845, "Ascended", "Ability"),
    (0x5846, "Ascended", "Armor"), (0x5847, "Ascended", "Accessory"),
]
TURNS = " Every second volley is Oryx's Rift, which pierces and ignores defense."
TURNS_AP = TURNS
DESC = {
    "Enthroned Sword": "The blade Oryx the Ascended God carried up the steps of his throne." + TURNS,
    "Enthroned Dagger": "A knife from Oryx the Ascended God's own belt, never once drawn in mercy." + TURNS,
    "Enthroned Bow": "The bow of Oryx the Ascended God's sky guard, strung with light." + TURNS_AP,
    "Enthroned Wand": "A sceptre of Oryx the Ascended God's court, humming with judgement." + TURNS_AP,
    "Enthroned Katana": "A blade folded in the Elysium for Oryx the Ascended God's champion." + TURNS_AP,
    "Enthroned Dual Blades": "Twin blades of Oryx the Ascended God's executioners." + TURNS,
    "Enthroned Fists": "Gauntlets of the Ascended God's honour guard, gold over silver." + TURNS_AP,
    "Enthroned Staff": "The staff Oryx the Ascended God raised over his Elysium." + TURNS,
    "Enthroned Hide": "The hide Oryx the Ascended God wore over his armour, as hard as the sky.",
    "Enthroned Plate": "The plate of Oryx the Ascended God's last stand, purple steel under gold.",
    "Enthroned Robe": "Oryx the Ascended God's own robe.",
    "Enthroned Helm": "Oryx the Ascended God's own helm.",
    "Enthroned Boots": "Oryx the Ascended God's own boots, worn as a ring.",
    "Enthroned Ring of Wrath": "Oryx the Ascended God's own ring of wrath.",
    "Enthroned Ring of Grace": "Oryx the Ascended God's own ring of grace, forged from both his gems.",
}
ASCENDED = [
    (0x5850, "Enthroned Sword", "Sword of Ascension", (ITEMS8, 0), None),
    (0x5851, "Enthroned Dagger", "Dagger of Divine Wrath", (ITEMS8, 3), None),
    (0x5852, "Enthroned Bow", "Bow of the Firmament", (ITEMS8, 2), None),
    (0x5853, "Enthroned Wand", "Wand of Absolution", (ITEMS8, 1), None),
    (0x5854, "Enthroned Katana", "Ame-no-Habakiri", (ITEMS8, 5), None),
    (0x5855, "Enthroned Dual Blades", "Seraphic Blades", (ITEMS8, 6), None),
    (0x5856, "Enthroned Fists", "Fists of the Ascended", None, None),
    # was the Enthroned Spear: a staff like the others now (its 16 by 16 cell no longer used)
    (0x5857, "Enthroned Staff", "Staff of Endless Energy", (ITEMS8, 4), None),
    (0x5858, "Enthroned Hide", "Sphinx Leather Armor", (ITEMS8, 3 * ITEMS8_COLS), None),
    (0x5859, "Enthroned Plate", "Armor of the Pyramid", (ITEMS8, 3 * ITEMS8_COLS + 1), None),
    (0x585a, "Enthroned Robe", "Robe of the Nile", (ITEMS8, 3 * ITEMS8_COLS + 2), None),
    (0x585b, "Enthroned Helm", "Helm of Imperial Right", (SHEET, cell_of("piece", "helm")), None),
    (0x585c, "Enthroned Boots", "Ring of Absolute Speed", (ITEMS8, 5 * ITEMS8_COLS), None),
    (0x585d, "Enthroned Ring of Wrath", "Ring of Absolute Attack", (RINGS8, 0), None),
    (0x585e, "Enthroned Ring of Grace", "Ring of Absolute Defense", (RINGS8, 1), None),
]

# Oryx 5's shots for the Enthroned weapons, every one of them the same: (projectile object, type
# code, 16 by 16 cell). A weapon's volleys take turns with the first two (SHOTS); the third is the
# imprints' (Imprints.cs: the Battle Mage Bolt Shot in CustomObjects.xml, and the client's look for
# the Gathered Shot's arrow and the Spearform thrust). The codes 0x5866-0x586f were the shots of
# before (a gold and a silver one for each kind of weapon): projectiles, which nothing stores.
SHOT_OBJECTS = [
    ("Enthroned Radiance", 0x5870, "shot radiance"),
    ("Enthroned Rift", 0x5871, "shot rift"),
    ("Enthroned Sigil", 0x5872, "shot sigil"),
]
# each weapon's own shot, hand-drawn under it on EnthronedItems8x8.png (column n of the top row,
# cell ITEMS8_COLS + n): (projectile object, type code, weapon). Its first volley flies this in
# place of the Radiance; the Fists keep the Radiance. 0x5878 is taken.
OWN_SHOTS = [
    ("Enthroned Sword Shot", 0x5873, "Enthroned Sword"),
    ("Enthroned Wand Shot", 0x5874, "Enthroned Wand"),
    ("Enthroned Bow Shot", 0x5875, "Enthroned Bow"),
    ("Enthroned Dagger Shot", 0x5876, "Enthroned Dagger"),
    ("Enthroned Staff Shot", 0x5877, "Enthroned Staff"),
    ("Enthroned Katana Shot", 0x5879, "Enthroned Katana"),
    ("Enthroned Dual Blades Shot", 0x587a, "Enthroned Dual Blades"),
]
# what the second shot adds to the weapon's own projectile
RIFT_FLAGS = {"MultiHit": True, "ArmorPiercing": True}
# each Enthroned weapon's shots, in the order its volleys fire them: (projectile object, flags added)
SHOTS = {name: [("Enthroned Radiance", {}), ("Enthroned Rift", RIFT_FLAGS)]
         for name in ("Enthroned Sword", "Enthroned Dagger", "Enthroned Bow", "Enthroned Wand", "Enthroned Katana",
                      "Enthroned Dual Blades", "Enthroned Fists", "Enthroned Staff")}
for _shot, _, _weapon in OWN_SHOTS:
    SHOTS[_weapon][0] = (_shot, {})

# The stats of an Ascended item where they aren't its source's: (stat, amount) in place of all of
# them. The Ring of Grace: 200 health and magic, 10 of every other stat; the Boots: 50 speed.
STATS = {
    "Enthroned Boots": [("22", 50)],
    "Enthroned Ring of Grace": [("0", 200), ("3", 200), ("20", 10), ("21", 10), ("22", 10), ("28", 10),
                                ("26", 10), ("27", 10)],
}
FAME_BONUS = 10  # every Ascended item's, in place of its source's

# Oryx 5's colours for the shots: his gold, the violet of his void, his silver and his staff's
# crystal blues, dark to light
SHOT_GOLD = [(104, 52, 8), (196, 124, 18), (240, 192, 64), (255, 232, 140), (255, 252, 226)]
SHOT_VIOLET = [(38, 14, 74), (88, 36, 160), (150, 84, 232), (206, 160, 255)]
SHOT_SILVER = [(104, 112, 170), (188, 206, 246), (255, 255, 255)]
SHOT_BLUE = [(40, 96, 176), (110, 200, 245), (196, 240, 255)]


# what an Ascended item leaves out of the item it copies (a weapon's projectile is SHOTS')
DROPPED = {"Texture", "Tier", "Description", "DisplayId", "Shop", "SlotType", "Class", "Item", "Soulbound", "BagType",
           "FameBonus"}


def sparkled(sheet, index):
    """A cell of another sheet, with purple sparkles in the empty pixels round it."""
    src = Image.open(SOURCE_SHEETS[sheet]).convert("RGBA")
    cols = src.width // CELL
    x0, y0 = (index % cols) * CELL, (index // cols) * CELL
    cell = src.crop((x0, y0, x0 + CELL, y0 + CELL))
    solid = {(x, y) for x in range(CELL) for y in range(CELL) if cell.getpixel((x, y))[3]}
    edge = [(x, y) for x in range(CELL) for y in range(CELL) if (x, y) not in solid
            and any((x + dx, y + dy) in solid for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    taken = []
    for (cx, cy), colour in SPARKLE:
        free = [p for p in edge if all(abs(p[0] - q[0]) + abs(p[1] - q[1]) > 2 for q in taken)]
        if not free:
            break
        p = min(free, key=lambda p: (abs(p[0] - cx) + abs(p[1] - cy), p))
        taken.append(p)
        cell.putpixel(p, colour + (255,))
    return cell


def draw():
    rows = (len(SPRITES) + COLS - 1) // COLS
    img = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))
    for n, (kind, a, b) in enumerate(SPRITES):
        if kind == "sparkle":
            img.paste(sparkled(a, b), ((n % COLS) * CELL, (n // COLS) * CELL))
            continue
        if kind == "shard":
            grid, colours = SHAPES[b], {str(i + 1): c for i, c in enumerate(RAMPS[a][b])}
        else:
            grid, colours = PIECES[a], O5
        ox, oy = (n % COLS) * CELL, (n // COLS) * CELL
        for y, row in enumerate(grid):
            assert len(row) == CELL, (a, b, row)
            for x, ch in enumerate(row):
                if ch != ".":
                    img.putpixel((ox + x, oy + y), colours[ch] + (255,))
    return img


def server_items():
    """Every item in the server's XML, by id."""
    found = {}
    for dirpath, _, files in os.walk(SERVER_XML):
        for f in files:
            if not f.endswith(".xml"):
                continue
            try:
                tree = ET.parse(os.path.join(dirpath, f))
            except ET.ParseError:
                continue
            for o in tree.getroot().iter("Object"):
                if o.find("Item") is not None:
                    found.setdefault(o.get("id"), o)
    return found


def esc(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def element_lines(el, depth):
    """An element as lines of text, indented a tab for each level."""
    pad = "\t" * depth
    attrs = "".join(f' {k}="{esc(v)}"' for k, v in el.attrib.items())
    text = (el.text or "").strip()
    if len(el) == 0:
        return [f"{pad}<{el.tag}{attrs}>{esc(text)}</{el.tag}>" if text else f"{pad}<{el.tag}{attrs}/>"]
    lines = [f"{pad}<{el.tag}{attrs}>"]
    for child in el:
        lines += element_lines(child, depth + 1)
    return lines + [f"{pad}</{el.tag}>"]


def block(items):
    out = ["\t" + BEGIN]
    for code, set_, kind in SHARDS:
        name = f"{set_} {kind} Shard"
        out += [
            f'\t<Object type="0x{code:04x}" id="{name}">',
            "\t\t<Class>Equipment</Class>",
            "\t\t<Item/>",
            "\t\t<Texture>",
            f"\t\t\t<File>{SHEET}</File>",
            f"\t\t\t<Index>0x{cell_of('shard', set_, kind):02x}</Index>",
            "\t\t</Texture>",
            "\t\t<SlotType>10</SlotType>",
            f"\t\t<Description>{esc(f'A shard of {SETS[set_]} power, for forging {kind.lower()} gear. Use it to put it in your forge storage, which the Divine Forge takes it from.')}</Description>",
            "\t\t<Sound>use_potion</Sound>",
            "\t\t<Consumable/>",
            f"\t\t<BagType>{7 if set_ == 'Infernal' else 9}</BagType>",
            "\t</Object>",
        ]
    shots = [(shot, code, SHEET16, SPRITES16.index(cell)) for shot, code, cell in SHOT_OBJECTS] + \
            [(shot, code, ITEMS8, ITEMS8_COLS + n) for n, (shot, code, _) in enumerate(OWN_SHOTS)]
    for shot, code, sheet, index in shots:
        out += [
            f'\t<Object type="0x{code:04x}" id="{shot}">',
            "\t\t<Class>Projectile</Class>",
            "\t\t<Texture>",
            f"\t\t\t<File>{sheet}</File>",
            f"\t\t\t<Index>0x{index:02x}</Index>",
            "\t\t</Texture>",
            "\t\t<AngleCorrection>1</AngleCorrection>",
            "\t</Object>",
        ]
    for code, name, source, texture, slot in ASCENDED:
        desc = DESC[name]
        src = items.get(source)
        if src is None:
            sys.exit(f"{name}: no item {source!r} to copy")
        sheet, index = texture or (SHEET, cell_of("sparkle", *source_texture(src)))
        out += [
            f'\t<Object type="0x{code:04x}" id="{name}">',
            "\t\t<Class>Equipment</Class>",
            "\t\t<Item/>",
            "\t\t<Texture>",
            f"\t\t\t<File>{sheet}</File>",
            f"\t\t\t<Index>0x{index:02x}</Index>",
            "\t\t</Texture>",
            f"\t\t<SlotType>{slot or src.findtext('SlotType')}</SlotType>",
            f"\t\t<Description>{esc(desc)}</Description>",
            "\t\t<Ascended/>",
        ]
        if name in SHOTS:
            out.append("\t\t<AlternateProjectiles/>")
        for child in src:
            if child.tag in DROPPED or (name in STATS and child.tag == "ActivateOnEquip"):
                continue
            if child.tag == "Projectile" and name in SHOTS:
                for i, shot in enumerate(SHOTS[name]):
                    out += element_lines(shot_projectile(child, i, *shot), 2)
                continue
            out += element_lines(child, 2)
        for stat, amount in STATS.get(name, []):
            out.append(f'\t\t<ActivateOnEquip stat="{stat}" amount="{amount}">IncrementStat</ActivateOnEquip>')
        out += [f"\t\t<FameBonus>{FAME_BONUS}</FameBonus>", "\t\t<BagType>9</BagType>", "\t\t<Soulbound/>", "\t</Object>"]
    out.append("\t" + END)
    return out


def shot_projectile(src, i, shot, flags):
    """The weapon's projectile as its i-th shot: Oryx's look, with the flags it adds (its own kept)."""
    p = ET.Element("Projectile", {"id": str(i)})
    for child in src:
        if child.tag in flags:
            continue
        c = copy.deepcopy(child)
        if c.tag == "ObjectId":
            c.text = shot
        p.append(c)
    for tag, value in flags.items():
        if value:
            ET.SubElement(p, tag)
    return p


def write_xml(path, lines):
    raw = open(path, "rb").read().decode("utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"
    text = raw.replace("\r\n", "\n")
    body = "\n".join(lines) + "\n"
    pattern = re.compile(r"\t" + re.escape(BEGIN) + r".*?" + re.escape(END) + r"\n", re.S)
    if pattern.search(text):
        text = pattern.sub(lambda _: body, text)
    else:
        end = text.rindex("</Objects>")
        text = text[:end] + body + text[end:]
    open(path, "wb").write(text.replace("\n", nl).encode("utf-8"))


def source_texture(src):
    return src.findtext("Texture/File"), int(src.findtext("Texture/Index"), 0)


def draw_staff():
    """The Enthroned Staff: STAFF_HALF and its darker mirror."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for (x, y), c in STAFF_HALF.items():
        img.putpixel((x, y), BIG[c] + (255,))
        if x + y < 15:
            img.putpixel((15 - y, 15 - x), BIG[DARKER[c]] + (255,))
    return img


def draw_ring(gem, claw):
    """A ring from the front: the band gold at the bottom, light blue up the sides and purple at
    the top, lit from the left, with the gem in its setting above."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    cx, cy, rx, ry = 7.5, 9.5, 5.6, 4.9
    for y in range(16):
        for x in range(16):
            r = math.hypot((x - cx) / rx, (y - cy) / ry)
            if not 0.68 <= r <= 1.05:
                continue
            h = (y - (cy - ry)) / (2 * ry)  # 0 at the top of the band, 1 at the bottom
            lit = x < cx
            if h > 0.62:
                c = "y" if lit and r > 0.88 else ("G" if lit or r > 0.88 else "g")
            elif h > 0.3:
                c = "C" if lit else "K"
            elif h > 0.16:
                c = "B" if lit else "v"
            else:
                c = "V" if lit else "P"
            img.putpixel((x, y), BIG[c] + (255,))
    for (x, y), k in {(4, 4): 1, (5, 3): 2, (11, 4): 0, (10, 3): 1, (7, 5): 1, (8, 5): 0}.items():
        img.putpixel((x, y), claw[k] + (255,))
    for (x, y), k in {(7, 0): 3, (8, 0): 2, (6, 1): 3, (7, 1): 3, (8, 1): 2, (9, 1): 1,
                      (6, 2): 2, (7, 2): 2, (8, 2): 1, (9, 2): 1, (6, 3): 1, (7, 3): 1, (8, 3): 0, (9, 3): 0,
                      (7, 4): 0, (8, 4): 0}.items():
        img.putpixel((x, y), gem[k] + (255,))
    img.putpixel((6, 1), BIG["W"] + (255,))  # a glint
    return img


def shot_axes(x, y):
    """A pixel's place along a shot pointing up and right (its tip at +8) and across it."""
    px, py = x + 0.5 - 8, y + 0.5 - 8
    return (px - py) / math.sqrt(2), (px + py) / math.sqrt(2)


def shot_dot(img, along, across, colour):
    x = 8 + (along + across) / math.sqrt(2) - 0.5
    y = 8 + (across - along) / math.sqrt(2) - 0.5
    img.putpixel((round(x), round(y)), colour + (255,))


def draw_shot(kind):
    """One of Oryx's shots for the Enthroned weapons, 16 by 16, pointing up and right."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    g, v, s, b = SHOT_GOLD, SHOT_VIOLET, SHOT_SILVER, SHOT_BLUE
    for y in range(16):
        for x in range(16):
            a, c = shot_axes(x, y)
            ac = abs(c)
            if kind == "radiance":
                # his gold Radiance: a lance head of light with a white heart, a crossguard where it
                # meets its trail, and the trail fading to deep gold
                if 3.6 <= a <= 7.9:
                    w, part = 3.0 * (7.9 - a) / 4.3, "head"
                elif 2.0 <= a < 3.6:
                    w, part = 3.0 - 2.2 * (3.6 - a) / 1.6, "head"
                elif 0.9 <= a < 2.0:
                    w, part = 0.8, "trail"
                elif 0.0 <= a < 0.9:
                    w, part = 3.4, "guard"
                elif -7.2 <= a < 0.0:
                    w, part = 0.9 * (a + 7.2) / 7.2 + 0.1, "trail"
                else:
                    continue
                if ac > w + 0.3:
                    continue
                t = ac / (w + 0.3)
                if part == "head":
                    colour = g[4] if ac < 0.75 else g[3] if t < 0.6 else g[2] if t < 0.85 else g[1]
                elif part == "guard":
                    colour = g[3] if ac < 0.75 else g[2] if t < 0.7 else g[1]
                else:
                    colour = g[3] if ac < 0.75 and a > -2.5 else g[2] if a > -4.5 else g[1] if a > -6 else g[0]
            elif kind == "rift":
                # his violet Rift: a slim blade of void, silver along both edges and white at the
                # point, a light blue glint in its dark, shedding violet behind
                if 1.0 <= a <= 7.7:
                    w = 1.9 * (7.7 - a) / 6.7 + 0.2
                elif -2.0 <= a < 1.0:
                    w = 2.1 - 0.9 * (1.0 - a) / 3.0
                elif -7.3 <= a < -2.0:
                    w = 1.2 * (a + 7.3) / 5.3
                else:
                    continue
                if ac > w + 0.3:
                    continue
                t = ac / (w + 0.3)
                if a >= -2.0:
                    colour = v[0] if t < 0.25 else v[1] if t < 0.5 else s[1] if t < 0.8 else s[0]
                    if a > 5.5 and t < 0.6:
                        colour = s[2]
                else:
                    colour = v[2] if t < 0.4 else v[1] if a > -5 else v[0]
            else:
                # his crystal Sigil: a four-pointed star of his staff's crystal, longest to the
                # front, round a gold heart, in a thin ring of gold
                ang, r = math.atan2(c, a), math.hypot(a, c)
                k = abs(math.cos(2 * ang)) ** 3
                reach = 2.0 + (5.6 if abs(ang) < 0.4 else 4.3) * k
                if r <= reach:
                    colour = (g[4] if r < 1.1 else g[2] if r < 2.0 else b[2] if r < reach * 0.55
                              else b[1] if r < reach * 0.85 else b[0])
                elif abs(r - 4.6) < 0.45:
                    colour = g[1]
                else:
                    continue
            img.putpixel((x, y), colour + (255,))
    # the sparks each sheds
    sparks = {
        "radiance": [(-3.6, 1.8, g[3]), (-5.4, -1.7, g[2]), (-6.9, 1.0, g[1]), (6.0, 0.0, s[2])],
        "rift": [(3.2, 0.0, b[2]), (2.2, 0.0, b[1]), (-3.6, 1.9, v[3]), (-5.6, -1.6, v[2]), (-6.8, 1.2, v[1])],
    }
    for along, across, colour in sparks.get(kind, []):
        shot_dot(img, along, across, colour)
    return img


def draw16():
    img = Image.new("RGBA", (16 * len(SPRITES16), 16), (0, 0, 0, 0))
    for n, name in enumerate(SPRITES16):
        if name == "staff":
            cell = draw_staff()
        elif name.startswith("shot "):
            cell = draw_shot(name.split()[1])
        else:
            cell = draw_ring(*RINGS[name.split()[1]])
        img.paste(cell, (16 * n, 0))
    return img


def main():
    items = server_items()
    draw16().save(OUT16)
    for _, name, source, texture, _ in ASCENDED:
        if texture is None and source in items:
            SPRITES.append(("sparkle",) + source_texture(items[source]))
    img = draw()
    img.save(OUT)
    if "--preview" in sys.argv:
        img.resize((img.width * 12, img.height * 12), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])
    lines = block(items)
    for path in ITEM_XMLS:
        write_xml(path, lines)
        # the party buffs reach further (tools/rework_buff_auras.py)
        rework_buff_auras.rework_file(path)
    print(f"{len(SPRITES)} sprites, {len(SHARDS)} shards and {len(ASCENDED)} Ascended items")


if __name__ == "__main__":
    main()
