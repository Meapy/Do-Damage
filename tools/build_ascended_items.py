"""Builds the forge shards and the Ascended items: their sprites and their item XML.

    python tools/build_ascended_items.py [--preview <png>]

The forge shards: an Infernal and an Ascended shard for each kind of gear (weapon, ability,
armor, accessory), dropped by Oryx 4 and Oryx 5. Used, a shard goes to the account's forge
storage (ForgeStorage.cs), which the Divine Forge takes them from.

The Ascended items: placeholders that nothing drops, forged from an item of Oryx the Ascended
God's top tier and an Ascended shard (ForgeRecipes.Ascended). Each keeps the numbers of the item
it's copied from (SOURCE below) until it gets its own, and the weapons and the leather and heavy
armours its sprite too; Oryx 5's own pieces (the staff, a spear like his Divine Spear, and his
helm, boots, robe and two rings) have sprites drawn here.

Writes `AscendedItems8x8.png` (client/src/kabam/rotmg/assets/custom/images, `ascendedItems8x8`,
which tools/gen_rs_manifest.py registers for the Rust client), 8 by 8 pixels a cell in the order
of SPRITES, and the items into both copies of CustomItems.xml, between the BEGIN and END marks
(replacing what's there). Type codes 0x5840 on are kept: characters store items by type code, so
never renumber them. Needs Pillow.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
SERVER_XML = os.path.join(ROOT, "source", "Shared", "resources", "xml")
OUT = os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "images", "AscendedItems8x8.png")
ITEM_XMLS = [
    os.path.join(SERVER_XML, "custom", "CustomItems.xml"),
    os.path.join(SRC, "kabam", "rotmg", "assets", "custom", "xmls", "CustomItems.xml"),
]
SHEET = "ascendedItems8x8"
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

# Oryx 5's own pieces, by colour: k b s w W silver, p P v purple, g G y gold
O5 = {"k": SILVER[0], "b": SILVER[1], "s": SILVER[2], "w": SILVER[3], "W": SILVER[4],
      "p": PURPLE[0], "P": PURPLE[1], "v": PURPLE[2], "g": GOLD[0], "G": GOLD[1], "y": GOLD[2]}
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
    "ring wrath": [
        "...vv...",
        "..vPPp..",
        "..GppG..",
        ".G....G.",
        ".G....G.",
        ".g....g.",
        "..gGGg..",
        "........",
    ],
    "ring grace": [
        "...WW...",
        "..Wwsb..",
        "..GbbG..",
        ".G....G.",
        ".G....G.",
        ".g....g.",
        "..gGGg..",
        "........",
    ],
}

# The sheet's cells, in order: the shards, then Oryx 5's pieces
SPRITES = [("shard", s, k) for s in ("Infernal", "Ascended") for k in KINDS] + \
          [("piece", p, None) for p in PIECES]


def cell_of(kind, a, b=None):
    return SPRITES.index((kind, a, b))


# The shards: (type code, set, kind); the Ascended items: (type code, id, the item whose numbers
# it copies, its texture as (sheet, index) or None for the source's, its slot type or None for
# the source's, description). Only ever add to these lists.
SHARDS = [
    (0x5840, "Infernal", "Weapon"), (0x5841, "Infernal", "Ability"),
    (0x5842, "Infernal", "Armor"), (0x5843, "Infernal", "Accessory"),
    (0x5844, "Ascended", "Weapon"), (0x5845, "Ascended", "Ability"),
    (0x5846, "Ascended", "Armor"), (0x5847, "Ascended", "Accessory"),
]
PLACEHOLDER = "A placeholder for the first of the Ascended gear: it has its T{tier} counterpart's numbers for now."
OWN = "Oryx the Ascended God's own {what}, a placeholder: it has the numbers of {source} for now."
ASCENDED = [
    (0x5850, "Ascended Sword", "Sword of Ascension", None, None, PLACEHOLDER.format(tier=15)),
    (0x5851, "Ascended Dagger", "Dagger of Divine Wrath", None, None, PLACEHOLDER.format(tier=15)),
    (0x5852, "Ascended Bow", "Bow of the Firmament", None, None, PLACEHOLDER.format(tier=15)),
    (0x5853, "Ascended Wand", "Wand of Absolution", None, None, PLACEHOLDER.format(tier=15)),
    (0x5854, "Ascended Katana", "Ame-no-Habakiri", None, None, PLACEHOLDER.format(tier=15)),
    (0x5855, "Ascended Dual Blades", "Seraphic Blades", None, None, PLACEHOLDER.format(tier=15)),
    (0x5856, "Ascended Fists", "Fists of the Ascended", None, None, PLACEHOLDER.format(tier=15)),
    (0x5857, "Spear of Heaven", "Staff of Endless Energy", ("elysium8x8", 0x54), None,
     "Oryx the Ascended God's Divine Spear, borne as a staff. A placeholder: it has the numbers of the T15 staff for now."),
    (0x5858, "Ascended Hide", "Sphinx Leather Armor", None, None, PLACEHOLDER.format(tier=16)),
    (0x5859, "Ascended Plate", "Armor of the Pyramid", None, None, PLACEHOLDER.format(tier=16)),
    (0x585a, "Robe of the Ascended God", "Robe of the Nile", (SHEET, cell_of("piece", "robe")), None,
     OWN.format(what="robe", source="the T16 robe")),
    (0x585b, "Helm of the Ascended God", "Helm of Imperial Right", (SHEET, cell_of("piece", "helm")), None,
     OWN.format(what="helm", source="the T8 helm")),
    (0x585c, "Boots of the Ascended God", "Ring of Absolute Speed", (SHEET, cell_of("piece", "boots")), None,
     OWN.format(what="boots, worn as a ring", source="the T8 speed ring")),
    (0x585d, "Ring of Ascended Wrath", "Ring of Absolute Attack", (SHEET, cell_of("piece", "ring wrath")), None,
     "The offensive Ascended ring, a placeholder: it has the numbers of the T8 attack ring for now."),
    (0x585e, "Ring of Ascended Grace", "Ring of Absolute Defense", (SHEET, cell_of("piece", "ring grace")), None,
     "The defensive Ascended ring, a placeholder: it has the numbers of the T8 defense ring for now."),
]
# what an Ascended item leaves out of the item it copies
DROPPED = {"Texture", "Tier", "Description", "DisplayId", "Shop", "SlotType"}


def draw():
    rows = (len(SPRITES) + COLS - 1) // COLS
    img = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))
    for n, (kind, a, b) in enumerate(SPRITES):
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
    for code, name, source, texture, slot, desc in ASCENDED:
        src = items.get(source)
        if src is None:
            sys.exit(f"{name}: no item {source!r} to copy")
        sheet, index = texture or (src.findtext("Texture/File"), int(src.findtext("Texture/Index"), 0))
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
        ]
        for child in src:
            if child.tag in DROPPED or child.tag in ("Class", "Item", "Soulbound", "BagType"):
                continue
            out += element_lines(child, 2)
        out += ["\t\t<BagType>9</BagType>", "\t\t<Soulbound/>", "\t</Object>"]
    out.append("\t" + END)
    return out


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


def main():
    img = draw()
    img.save(OUT)
    if "--preview" in sys.argv:
        img.resize((img.width * 12, img.height * 12), Image.NEAREST).save(sys.argv[sys.argv.index("--preview") + 1])
    lines = block(server_items())
    for path in ITEM_XMLS:
        write_xml(path, lines)
    print(f"{len(SPRITES)} sprites, {len(SHARDS)} shards and {len(ASCENDED)} Ascended items")


if __name__ == "__main__":
    main()
