"""Builds the Infernal potions: their sprites and their item XML.

    python tools/build_infernal_potions.py [--preview <png>]

An Infernal potion raises a stat past its class's maximum: drunk with the stat maxed, it adds what
the stat's own potion adds (5 life or magic, 1 of the others), up to five potions past the maximum
(Player.AEOvercapStat; DbChar.Overcap keeps the share). The Divine Forge makes them, from an
Infernal shard and 100 of the stat's potions out of the potion storage (ForgeRecipes.InfernalPotions):
the weapon shard's are attack and dexterity, the ability shard's magic and wisdom, the armor
shard's defense and vitality, the accessory shard's life and speed.

Each sprite is the stat's own potion (d3LofiObj, 0x100 on) set alight in Oryx 4's colours: a
smouldering cork, flames licking up both sides and embers under it, so the eight read as the
potions players know.

Writes `InfernalPotions8x8.png` (client/src/kabam/rotmg/assets/custom/images, `infernalPotions8x8`,
which tools/gen_rs_manifest.py registers for the Rust client), one 8 by 8 cell a potion in stat
order, and the items into both copies of CustomItems.xml between the BEGIN and END marks
(replacing what's there). Type codes 0x5880 on are kept: characters store items by type code, so
never renumber them. Needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
SOURCE = os.path.join(SRC, "prod", "images", "d3LofiObj.png")
OUT = os.path.join(SRC, "custom", "images", "InfernalPotions8x8.png")
ITEM_XMLS = [
    os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "CustomItems.xml"),
    os.path.join(SRC, "custom", "xmls", "CustomItems.xml"),
]
SHEET = "infernalPotions8x8"
CELL = 8
FIRST_TYPE = 0x5880
BEGIN = "<!-- BEGIN Infernal potions (tools/build_infernal_potions.py) -->"
END = "<!-- END Infernal potions -->"

# (name as the potions end, the XML stat id, how much a potion adds, its sprite on d3LofiObj, the shard)
STATS = [
    ("Life", 0, 5, 0x100, "Accessory"),
    ("Mana", 3, 5, 0x101, "Ability"),
    ("Attack", 20, 1, 0x102, "Weapon"),
    ("Defense", 21, 1, 0x103, "Armor"),
    ("Speed", 22, 1, 0x104, "Accessory"),
    ("Dexterity", 28, 1, 0x105, "Weapon"),
    ("Vitality", 26, 1, 0x106, "Armor"),
    ("Wisdom", 27, 1, 0x107, "Ability"),
]
WORDS = {"Life": "life", "Mana": "magic", "Attack": "attack", "Defense": "defense", "Speed": "speed",
         "Dexterity": "dexterity", "Vitality": "vitality", "Wisdom": "wisdom"}
POTIONS = 5     # how many go past the maximum (Player.OvercapPotions)
COST = 100      # potions out of the storage (ForgeRecipes.InfernalPotionCost)

# Oryx 4's fire (the Hash Cellar's hellfire): dark to light
EMBER = [(70, 14, 8), (150, 32, 12), (226, 84, 20), (255, 150, 40), (255, 222, 120)]
# pixels set on every potion: the cork smoulders, flames climb both sides, embers glow beneath
FIRE = {
    (3, 1): EMBER[1], (4, 1): EMBER[3], (5, 1): EMBER[1],
    (4, 0): EMBER[4],
    (1, 5): EMBER[2], (1, 4): EMBER[3], (1, 3): EMBER[4],
    (7, 6): EMBER[2], (7, 5): EMBER[3], (7, 4): EMBER[4],
    (2, 7): EMBER[0], (3, 7): EMBER[1], (4, 7): EMBER[2], (5, 7): EMBER[1], (6, 7): EMBER[0],
}


def draw():
    src = Image.open(SOURCE).convert("RGBA")
    cols = src.width // CELL
    sheet = Image.new("RGBA", (CELL * len(STATS), CELL), (0, 0, 0, 0))
    for k, (_, _, _, index, _) in enumerate(STATS):
        x0, y0 = (index % cols) * CELL, (index // cols) * CELL
        cell = src.crop((x0, y0, x0 + CELL, y0 + CELL))
        for (x, y), rgb in FIRE.items():
            cell.putpixel((x, y), rgb + (255,))
        sheet.paste(cell, (k * CELL, 0))
    return sheet


def items():
    lines = [f"\t{BEGIN}"]
    for k, (name, stat, amount, _, shard) in enumerate(STATS):
        word = WORDS[name]
        lines += [
            f'\t<Object type="0x{FIRST_TYPE + k:04x}" id="Infernal Potion of {name}">',
            "\t\t<Class>Equipment</Class>",
            "\t\t<Item/>",
            "\t\t<Texture>",
            f"\t\t\t<File>{SHEET}</File>",
            f"\t\t\t<Index>0x{k:02x}</Index>",
            "\t\t</Texture>",
            "\t\t<SlotType>10</SlotType>",
            f"\t\t<Description>A Potion of {name} set alight in Oryx's fire, forged from an Infernal {shard} Shard "
            f"and {COST} Potions of {name} from your potion storage. Drunk with your {word} maxed, it raises it "
            f"{amount} past your class's maximum, up to {POTIONS} of these.</Description>",
            "\t\t<Sound>use_potion</Sound>",
            f'\t\t<Activate stat="{stat}" amount="{amount}">OvercapStat</Activate>',
            "\t\t<Consumable/>",
            "\t\t<Potion/>",
            "\t\t<BagType>7</BagType>",
            "\t</Object>",
        ]
    lines.append(f"\t{END}")
    return lines


def write_xml(path, lines):
    with open(path, "rb") as f:
        raw = f.read()
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")
    block = "\n".join(lines) + "\n"
    if BEGIN in text:
        start = text.index(f"\t{BEGIN}")
        end = text.index(END) + len(END) + 1
        text = text[:start] + block + text[end:]
    else:
        at = text.rindex("</Objects>")
        text = text[:at] + block + text[at:]
    data = (b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + text.replace("\n", nl).encode("utf-8")
    if data != raw:
        with open(path, "wb") as f:
            f.write(data)


def main():
    sheet = draw()
    sheet.save(OUT)
    print(f"wrote {os.path.relpath(OUT, ROOT)}")
    lines = items()
    for path in ITEM_XMLS:
        write_xml(path, lines)
        print(f"wrote {os.path.relpath(path, ROOT)}")
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        src = Image.open(SOURCE).convert("RGBA")
        cols = src.width // CELL
        scale = 12
        preview = Image.new("RGBA", (len(STATS) * (CELL + 2) * scale, 2 * (CELL + 2) * scale), (36, 28, 48, 255))
        for k, (_, _, _, index, _) in enumerate(STATS):
            x0, y0 = (index % cols) * CELL, (index // cols) * CELL
            before = src.crop((x0, y0, x0 + CELL, y0 + CELL)).resize((CELL * scale, CELL * scale), Image.NEAREST)
            after = sheet.crop((k * CELL, 0, (k + 1) * CELL, CELL)).resize((CELL * scale, CELL * scale), Image.NEAREST)
            preview.alpha_composite(before, (k * (CELL + 2) * scale + scale, scale))
            preview.alpha_composite(after, (k * (CELL + 2) * scale + scale, (CELL + 3) * scale))
        preview.save(out)
        print(f"wrote {out}")


if __name__ == "__main__":
    main()
