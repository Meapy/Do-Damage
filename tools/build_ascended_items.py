"""Builds the forge shards and the Ascended items: their sprites and their item XML.

    python tools/build_ascended_items.py [--preview <png>]

The forge shards: an Infernal and an Ascended shard for each kind of gear (weapon, ability,
armor, accessory), dropped by Oryx 4 and Oryx 5. Used, a shard goes to the account's forge
storage (ForgeStorage.cs), which the Divine Forge takes them from.

The Ascended items: placeholders that nothing drops, forged from an item of Oryx the Ascended
God's top tier and an Ascended shard (ForgeRecipes.Ascended), marked <Ascended/> (the red slot
edge, and the wiki's Ascended group). Each keeps the numbers of the item it's copied from until
it gets its own. The weapons and the leather and heavy armours keep its sprite too, with a few
purple sparkles added round it (SPARKLE); Oryx 5's own Enthroned pieces (his staff, as his sprite
holds it, and his helm, boots, robe and two rings) are drawn here.

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
    # the staff Oryx 5 holds: a pale crystal on a purple collar, a gold shaft banded light and dark
    "staff": [
        "......CW",
        ".....CKC",
        "....BKC.",
        "...pPB..",
        "..Gg....",
        ".Gg.....",
        "Gg......",
        "g.......",
    ],
}

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
    (0x5857, "Enthroned Staff", "Staff of Endless Energy", (SHEET, cell_of("piece", "staff")), None,
     OWN.format(what="staff", source="the T15 staff")),
    (0x5858, "Ascended Hide", "Sphinx Leather Armor", None, None, PLACEHOLDER.format(tier=16)),
    (0x5859, "Ascended Plate", "Armor of the Pyramid", None, None, PLACEHOLDER.format(tier=16)),
    (0x585a, "Enthroned Robe", "Robe of the Nile", (SHEET, cell_of("piece", "robe")), None,
     OWN.format(what="robe", source="the T16 robe")),
    (0x585b, "Enthroned Helm", "Helm of Imperial Right", (SHEET, cell_of("piece", "helm")), None,
     OWN.format(what="helm", source="the T8 helm")),
    (0x585c, "Enthroned Boots", "Ring of Absolute Speed", (SHEET, cell_of("piece", "boots")), None,
     OWN.format(what="boots, worn as a ring", source="the T8 speed ring")),
    (0x585d, "Enthroned Ring of Wrath", "Ring of Absolute Attack", (SHEET, cell_of("piece", "ring wrath")), None,
     OWN.format(what="ring of wrath", source="the T8 attack ring")),
    (0x585e, "Enthroned Ring of Grace", "Ring of Absolute Defense", (SHEET, cell_of("piece", "ring grace")), None,
     OWN.format(what="ring of grace", source="the T8 defense ring")),
]
# what an Ascended item leaves out of the item it copies
DROPPED = {"Texture", "Tier", "Description", "DisplayId", "Shop", "SlotType"}


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
    for code, name, source, texture, slot, desc in ASCENDED:
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


def source_texture(src):
    return src.findtext("Texture/File"), int(src.findtext("Texture/Index"), 0)


def main():
    items = server_items()
    for _, name, source, texture, _, _ in ASCENDED:
        if texture is None and source in items:
            SPRITES.append(("sparkle",) + source_texture(items[source]))
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
