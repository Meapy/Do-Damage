"""Builds the forge shards and the Ascended items: their sprites and their item XML.

    python tools/build_ascended_items.py [--preview <png>]

The forge shards: an Infernal and an Ascended shard for each kind of gear (weapon, ability,
armor, accessory), dropped by Oryx 4 and Oryx 5. Used, a shard goes to the account's forge
storage (ForgeStorage.cs), which the Divine Forge takes them from.

The Ascended items: what nothing drops, forged from an item of Oryx the Ascended God's top tier
and an Ascended shard (ForgeRecipes.Ascended), marked <Ascended/> (the red slot edge, the wiki's
Ascended group, and a drop bag only their owner sees); all are named Enthroned. Each has the
numbers of the item it's forged from (its stats, its ability, its shots' damage). The weapons fly
Oryx 5's own shots instead (SHOTS, drawn here in his gold and silver), two kinds whose volleys take
turns (<AlternateProjectiles/>, Player.NoteWeaponShot): a gold shot with the weapon's own numbers,
then a silver one that pierces, or a gold one that ignores defense where the weapon pierced
already. The Enthroned Spear, a staff, takes turns between a yellow shot doing a T15 staff's damage
and a white one that pierces. The weapons and the leather and heavy armours keep its
sprite too, with a few purple sparkles added round it (SPARKLE); his helm, boots and robe are
drawn here at 8 by 8, and his staff and two rings at 16 by 16, on `AscendedItems16x16.png`
(`ascendedItems16x16`, in the order of SPRITES16): the staff his own sprite holds, gold with a
crystal head, sideways as items lie (STAFF_HALF), and the rings gold at the bottom, light blue up
the sides and purple at the top, under a gem in its setting (ring).

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

# The Enthroned Spear (a staff), lying from bottom left to top right: its upper-left half and its middle
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
# The 16 by 16 sheet's cells, in order
SPRITES16 = ["staff", "ring wrath", "ring grace"]

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
TURNS = " Its volleys take turns: Oryx's gold shot, then his silver one, which pierces."
TURNS_AP = " Its volleys take turns: Oryx's silver shot, then his gold one, which ignores defense."
DESC = {
    "Enthroned Sword": "The blade Oryx the Ascended God carried up the steps of his throne." + TURNS,
    "Enthroned Dagger": "A knife from Oryx the Ascended God's own belt, never once drawn in mercy." + TURNS,
    "Enthroned Bow": "The bow of Oryx the Ascended God's sky guard, strung with light." + TURNS_AP,
    "Enthroned Wand": "A sceptre of Oryx the Ascended God's court, humming with judgement." + TURNS_AP,
    "Enthroned Katana": "A blade folded in the Elysium for Oryx the Ascended God's champion." + TURNS_AP,
    "Enthroned Dual Blades": "Twin blades of Oryx the Ascended God's executioners. Its volleys take turns: Oryx's silver "
                             "shots, then his gold ones, which leave the wound bleeding.",
    "Enthroned Fists": "Gauntlets of the Ascended God's honour guard, gold over silver." + TURNS_AP,
    "Enthroned Spear": "Oryx the Ascended God's own spear, borne as a staff. Its volleys take turns: a yellow bolt "
                       "as strong as a T15 staff's, then a white one that pierces.",
    "Enthroned Hide": "The hide Oryx the Ascended God wore over his armour, as hard as the sky.",
    "Enthroned Plate": "The plate of Oryx the Ascended God's last stand, purple steel under gold.",
    "Enthroned Robe": "Oryx the Ascended God's own robe.",
    "Enthroned Helm": "Oryx the Ascended God's own helm.",
    "Enthroned Boots": "Oryx the Ascended God's own boots, worn as a ring.",
    "Enthroned Ring of Wrath": "Oryx the Ascended God's own ring of wrath.",
    "Enthroned Ring of Grace": "Oryx the Ascended God's own ring of grace.",
}
ASCENDED = [
    (0x5850, "Enthroned Sword", "Sword of Ascension", None, None),
    (0x5851, "Enthroned Dagger", "Dagger of Divine Wrath", None, None),
    (0x5852, "Enthroned Bow", "Bow of the Firmament", None, None),
    (0x5853, "Enthroned Wand", "Wand of Absolution", None, None),
    (0x5854, "Enthroned Katana", "Ame-no-Habakiri", None, None),
    (0x5855, "Enthroned Dual Blades", "Seraphic Blades", None, None),
    (0x5856, "Enthroned Fists", "Fists of the Ascended", None, None),
    (0x5857, "Enthroned Spear", "Staff of Endless Energy", (SHEET16, SPRITES16.index("staff")), None),
    (0x5858, "Enthroned Hide", "Sphinx Leather Armor", None, None),
    (0x5859, "Enthroned Plate", "Armor of the Pyramid", None, None),
    (0x585a, "Enthroned Robe", "Robe of the Nile", (SHEET, cell_of("piece", "robe")), None),
    (0x585b, "Enthroned Helm", "Helm of Imperial Right", (SHEET, cell_of("piece", "helm")), None),
    (0x585c, "Enthroned Boots", "Ring of Absolute Speed", (SHEET, cell_of("piece", "boots")), None),
    (0x585d, "Enthroned Ring of Wrath", "Ring of Absolute Attack", (SHEET16, SPRITES16.index("ring wrath")), None),
    (0x585e, "Enthroned Ring of Grace", "Ring of Absolute Defense", (SHEET16, SPRITES16.index("ring grace")), None),
]

# The weapons' shots, in the order their volleys fire them: (projectile object, colour, shape),
# then what changes from the weapon's own projectile: damage ((min, max), or None for its own),
# and the flags to add or take away. The projectile objects (type codes from SHOT_TYPES, in order
# of SHOT_LOOKS) are drawn here, pointing up and right as projectiles lie (AngleCorrection 1).
SHOTS = {
    "Enthroned Sword": [("gold", "blade", None, {}), ("silver", "blade", (300, 322), {"MultiHit": True})],
    "Enthroned Dagger": [("gold", "dagger", None, {}), ("silver", "dagger", (150, 217), {"MultiHit": True})],
    "Enthroned Bow": [("silver", "arrow", None, {}), ("gold", "arrow", None, {"ArmorPiercing": True})],
    "Enthroned Wand": [("silver", "bolt", None, {}), ("gold", "bolt", None, {"ArmorPiercing": True})],
    "Enthroned Katana": [("silver", "blade", None, {}), ("gold", "blade", None, {"ArmorPiercing": True})],
    "Enthroned Dual Blades": [("silver", "dagger", None, {}), ("gold", "dagger", None, {"Bleed": (300, 3)})],
    "Enthroned Fists": [("silver", "bolt", None, {}), ("gold", "bolt", None, {"ArmorPiercing": True})],
    # a yellow shot doing a T15 staff's damage (the Staff of Endless Energy's 100-135, a little over),
    # then a white one that pierces
    "Enthroned Spear": [("yellow", "spear", (105, 140), {}), ("white", "spear", (85, 115), {"MultiHit": True})],
}
SHOT_LOOKS = sorted({(c, s) for shots in SHOTS.values() for c, s, _, _ in shots},
                    key=lambda cs: (["blade", "dagger", "arrow", "bolt", "spear"].index(cs[1]), cs[0]))
SHOT_TYPES = range(0x5866, 0x5866 + 16)  # only ever add to the end of SHOT_LOOKS' order
assert len(SHOT_LOOKS) <= len(SHOT_TYPES)


def shot_id(colour, shape):
    return f"Enthroned {colour.title()} {shape.title()}"


# the shots' shapes, pointing up and right: '1' outline, '2' dark, '3' light, '4' the glint
SHOT_SHAPES = {
    "blade": [
        ".....111",
        "....1341",
        "...13431",
        "..13421.",
        "1.1321..",
        "1121....",
        ".21.....",
        "1.1.....",
    ],
    "dagger": [
        "........",
        "....111.",
        "...1341.",
        "..1342..",
        "1.131...",
        ".121....",
        "121.....",
        "........",
    ],
    "arrow": [
        "....1111",
        "....1431",
        "....1331",
        "...12111",
        "..121...",
        ".121....",
        "1321....",
        "111.....",
    ],
    "bolt": [
        "....11..",
        "...1341.",
        "..134431",
        "..13321.",
        ".1221...",
        "121.....",
        "21......",
        "........",
    ],
    # the Enthroned Spear's: a spear head with a short crossbar, as its shots look in flight
    "spear": [
        "......11",
        ".....141",
        "....1431",
        "...1331.",
        "1.1321..",
        "1121....",
        ".21.....",
        "1.1.....",
    ],
}
# each colourway, '1' to '4': Oryx 5's gold and silver, and the spear's yellow and white
SHOT_RAMPS = {
    "gold": [(84, 48, 10), GOLD[0], GOLD[1], GOLD[2]],
    "silver": [SILVER[0], SILVER[1], SILVER[3], SILVER[4]],
    "yellow": [(96, 66, 24), (196, 150, 70), (238, 206, 118), (255, 244, 196)],
    "white": [(30, 26, 40), (150, 154, 176), (226, 230, 240), (255, 255, 255)],
}

# what an Ascended item leaves out of the item it copies (a weapon's projectile is SHOTS')
DROPPED = {"Texture", "Tier", "Description", "DisplayId", "Shop", "SlotType", "Class", "Item", "Soulbound", "BagType"}


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
        elif kind == "shot":
            grid, colours = SHOT_SHAPES[b], {str(i + 1): c for i, c in enumerate(SHOT_RAMPS[a])}
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
    for (colour, shape), code in zip(SHOT_LOOKS, SHOT_TYPES):
        out += [
            f'\t<Object type="0x{code:04x}" id="{shot_id(colour, shape)}">',
            "\t\t<Class>Projectile</Class>",
            "\t\t<Texture>",
            f"\t\t\t<File>{SHEET}</File>",
            f"\t\t\t<Index>0x{cell_of('shot', colour, shape):02x}</Index>",
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
            if child.tag in DROPPED:
                continue
            if child.tag == "Projectile" and name in SHOTS:
                for i, shot in enumerate(SHOTS[name]):
                    out += element_lines(shot_projectile(child, i, *shot), 2)
                continue
            out += element_lines(child, 2)
        out += ["\t\t<BagType>9</BagType>", "\t\t<Soulbound/>", "\t</Object>"]
    out.append("\t" + END)
    return out


def shot_projectile(src, i, colour, shape, damage, flags):
    """The weapon's projectile as its i-th shot: Oryx's look, its damage and flags changed."""
    p = ET.Element("Projectile", {"id": str(i)})
    for child in src:
        if child.tag in flags or child.tag in ("Damage",) and damage:
            continue
        c = copy.deepcopy(child)
        if c.tag == "ObjectId":
            c.text = shot_id(colour, shape)
        elif damage and c.tag == "MinDamage":
            c.text = str(damage[0])
        elif damage and c.tag == "MaxDamage":
            c.text = str(damage[1])
        p.append(c)
    for tag, value in flags.items():
        if tag == "Bleed":
            ET.SubElement(p, "Bleed", {"duration": str(value[1])}).text = str(value[0])
        elif value:
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
    """The Enthroned Spear: STAFF_HALF and its darker mirror."""
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


def draw16():
    img = Image.new("RGBA", (16 * len(SPRITES16), 16), (0, 0, 0, 0))
    for n, name in enumerate(SPRITES16):
        cell = draw_staff() if name == "staff" else draw_ring(*RINGS[name.split()[1]])
        img.paste(cell, (16 * n, 0))
    return img


def main():
    items = server_items()
    draw16().save(OUT16)
    for _, name, source, texture, _ in ASCENDED:
        if texture is None and source in items:
            SPRITES.append(("sparkle",) + source_texture(items[source]))
    SPRITES.extend(("shot", c, s) for c, s in SHOT_LOOKS)
    img = draw()
    img.save(OUT)
    if "--preview" in sys.argv:
        img.resize((img.width * 12, img.height * 12), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])
    lines = block(items)
    for path in ITEM_XMLS:
        write_xml(path, lines)
    print(f"{len(SPRITES)} sprites, {len(SHARDS)} shards and {len(ASCENDED)} Ascended items")


if __name__ == "__main__":
    main()
