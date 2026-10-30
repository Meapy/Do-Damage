"""Draws the NPCs of the Nexus: the Tinkerer (quests), the Chronicler (daily rewards and
achievements), the Market Broker (the Marketplace) and the Herald (the server's week), two frames
each.

    python tools/draw_quest_npcs.py [--preview <png>]

Writes `QuestNpcs16x16.png` (client/src/kabam/rotmg/assets/custom/images), 16 by 16 pixels a
cell: cells 0-1 the Tinkerer, 2-3 the Chronicler, 4-5 the Market Broker, 6-7 the Herald. The objects
(`The Tinkerer`, `The Chronicler`, `Market Broker`, `The Herald`, CustomObjects.xml) animate between
their two. At Size 100 a 16-pixel cell draws at
the same pixel size as the 8-pixel characters, so they stand a head taller than players. The art
is drawn here rather than in an editor so it can be changed and redrawn; it needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "QuestNpcs16x16.png")
CELL = 16

# the website's palette where it fits: violets, ember and gold
PALETTE = {
    "o": (20, 14, 30),       # outline
    "k": (236, 190, 146),    # skin
    "K": (198, 142, 104),    # skin in shade
    "e": (40, 26, 40),       # eyes
    "w": (236, 232, 246),    # beard, hair
    "W": (178, 170, 200),    # beard in shade
    "h": (122, 78, 44),      # leather
    "H": (164, 110, 62),     # leather, lit
    "j": (82, 52, 32),       # leather in shade
    "g": (222, 168, 52),     # gold
    "G": (255, 222, 120),    # gold, lit
    "d": (148, 96, 22),      # gold in shade
    "l": (110, 214, 255),    # goggle lens
    "L": (220, 248, 255),    # glint
    "c": (255, 122, 61),     # ember coat
    "C": (196, 78, 34),      # ember in shade
    "s": (242, 230, 196),    # parchment
    "S": (204, 184, 140),    # parchment in shade
    "r": (204, 44, 58),      # wax seal
    "v": (92, 70, 140),      # violet robe
    "V": (60, 44, 96),       # violet in shade
    "u": (128, 104, 186),    # violet, lit
    "p": (255, 246, 214),    # glowing page
    "y": (255, 214, 90),     # glow
    "f": (46, 38, 66),       # boots
}

# The Tinkerer: a gnome tinker in a leather cap with brass goggles, an ember coat and a leather
# apron, holding up the day's quest scroll. Frame 2: the goggles glint and the scroll lifts.
TINKERER = [
    [
        "................",
        "......oooo......",
        "....ooHHHHoo....",
        "...oHHhhhhhho...",
        "..oogGlgogGlgo..",
        "..ohdgggggggdho.",
        ".ohhhhhhhhhhhhho",
        "..ookkkkkkkkoo..",
        "...okekkkkekko..",
        "...oKkkwwkkKo...",
        "..owwwwwwwwwwo..",
        ".oCcowwwwwwocCos",
        ".oCcchwWWwhccoSs",
        ".oKoohhhhhhhooSo",
        "..ooChhhghhCoo..",
        "...ofoo..oofo...",
    ],
    [
        "................",
        "......oooo......",
        "....ooHHHHoo....",
        "...oHHhhhhhho...",
        "..oogLlgogLlgo..",
        "..ohdgggggggdho.",
        ".ohhhhhhhhhhhhho",
        "..ookkkkkkkkoo.s",
        "...okekkkkekkoSs",
        "...oKkkwwkkKoSro",
        "..owwwwwwwwwwKo.",
        ".oCcowwwwwwocco.",
        ".oCcchwWWwhccCo.",
        ".oKoohhhhhhhoo..",
        "..ooChhhghhCo...",
        "...ofoo..oofo...",
    ],
]

# The Chronicler: a hooded scholar in violet robes edged with gold, reading from a great glowing
# book of deeds. Frame 2: the glow brightens and a spark rises from the page.
CHRONICLER = [
    [
        "......oooo......",
        ".....ovvvvo.....",
        "....ovuuvvVo....",
        "...ovuoooovVo...",
        "...ovokkkkoVo...",
        "...ovoekkeoVo...",
        "...ovokkkkoVo...",
        "..ovvgwwwwgvVo..",
        "..ovgvwwwwvgVo..",
        ".ovvgvvwwvvgvVo.",
        ".odGGGGGdGGGGGdo",
        ".odpSSpSdpSpSSdo",
        ".odpSpSSdpSSpSdo",
        "..oddddddddddoo.",
        "...ovvVvvVvvVo..",
        "...ooffoooffoo..",
    ],
    [
        "......oooo....y.",
        ".....ovvvvo.....",
        "....ovuuvvVo..y.",
        "...ovuoooovVo...",
        "...ovokkkkoVoy..",
        "...ovoekkeoVo...",
        "...ovokkkkoVo...",
        "..ovvgwwwwgvVo..",
        "..ovgvwwwwvgVo..",
        ".ovvgvvwwvvgvVo.",
        "yodGGGGGdGGGGGdo",
        ".odySSpSdpSySSdo",
        ".odpSySSdySSpSdo",
        "..oddddddddddoo.",
        "...ovvVvvVvvVo..",
        "...ooffoooffoo..",
    ],
]

# The Market Broker: a merchant in a broad ember hat with a gold band, a grand moustache and a
# violet coat with gold buttons, holding up a gold coin. Frame 2: the coin glints.
BROKER = [
    [
        "................",
        ".....oooooo.....",
        "....occcccco....",
        "....occCCcco....",
        "...ogggggggggo..",
        ".occcccccccccco.",
        "...okkkkkkkko...",
        "...okekkkkeko...",
        "...oKkwwwwkKo...",
        "....oKkkkkKo....",
        "..ovvgvvvvgvvo..",
        ".ovuvvgvvgvvuvkG",
        ".ovVvvgvvgvvVvo.",
        "..ovVvhhhhvVvo..",
        "...ovVvvvvVvo...",
        "...offo..offo...",
    ],
    [
        "................",
        ".....oooooo.....",
        "....occcccco....",
        "....occCCcco....",
        "...ogggggggggo..",
        ".occcccccccccco.",
        "...okkkkkkkko...",
        "...okekkkkeko...",
        "...oKkwwwwkKo...",
        "....oKkkkkKo..y.",
        "..ovvgvvvvgvvo.y",
        ".ovuvvgvvgvvuvky",
        ".ovVvvgvvgvvVvoy",
        "..ovVvhhhhvVvo..",
        "...ovVvvvvVvo...",
        "...offo..offo...",
    ],
]

# The Herald: a crier in a violet cap with a red plume and a gold-trimmed ember tabard, a long
# gold horn at his lips with the realm's violet banner hanging from it, who keeps the week's
# tally. Frame 2: he blows, the bell flares and the call rings out.
HERALD = [
    [
        "................",
        ".....oooooo..o..",
        "....ovvuuvvoorr.",
        "....ovuuvvvVrro.",
        "...ooVVVVVVVVo..",
        ".....okkkkko....",
        ".....okekeko....",
        ".....okkkkgGGGGo",
        ".....oKkkKoddGGo",
        "....occggccovvVo",
        "...occgcgggcvuVo",
        "..oCcgcrrcgcvvVo",
        "..oCcgcrrcgcvuVo",
        "...oCcgggcCogdgo",
        "....ovVVVVvo....",
        "....offo.offo...",
    ],
    [
        "................",
        ".....oooooo..o..",
        "....ovvuuvvoorr.",
        "....ovuuvvvVrro.",
        "...ooVVVVVVVVo..",
        ".....okkkkko..y.",
        ".....okekeko...y",
        ".....okKkKgGGGGG",
        ".....oKkkKoddGGG",
        "....occggccovvVo",
        "...occgcgggcvuVo",
        "..oCcgcrrcgcvvVo",
        "..oCcgcrrcgcvuVo",
        "...oCcgggcCogdgo",
        "....ovVVVVvo...y",
        "....offo.offo.y.",
    ],
]


def cell(rows):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    assert len(rows) == CELL, len(rows)
    for y, row in enumerate(rows):
        assert len(row) == CELL, (y, row)
        for x, ch in enumerate(row):
            if ch != ".":
                img.putpixel((x, y), PALETTE[ch] + (255,))
    return img


def main():
    frames = TINKERER + CHRONICLER + BROKER + HERALD
    sheet = Image.new("RGBA", (CELL * len(frames), CELL), (0, 0, 0, 0))
    for i, rows in enumerate(frames):
        sheet.paste(cell(rows), (i * CELL, 0))
    sheet.save(OUT)
    print(f"wrote {OUT}")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        bg = Image.new("RGBA", sheet.size, (24, 18, 36, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 12, sheet.height * 12), Image.NEAREST).save(path)
        print(f"preview {path}")


if __name__ == "__main__":
    main()
