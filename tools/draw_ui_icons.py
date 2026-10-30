"""Draws the UI's icons: the Quests and Nexus buttons' pictures, and the Herald's (the server's
week, DbWeeklyStats) for each of the week's counts.

    python tools/draw_ui_icons.py [--preview <png>]

Writes `UiIcons16x16.png` (client/src/kabam/rotmg/assets/custom/images, `uiIcons16x16`, which
tools/gen_rs_manifest.py registers for the Rust client), 16 by 16 pixels a cell in the order of
ICONS; client-rs/app/src/hud.rs names the cells (`ui_icon`). Drawn in the game's palette (violet
stone, ember, gold) with the dark outline the items have. The art is drawn here rather than in an
editor so it can be changed and redrawn; it needs Pillow.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "UiIcons16x16.png")
CELL = 16

PALETTE = {
    "o": (20, 14, 30),       # outline
    "g": (222, 168, 52),     # gold
    "G": (255, 222, 120),    # gold, lit
    "d": (148, 96, 22),      # gold in shade
    "v": (92, 70, 140),      # violet stone
    "V": (60, 44, 96),       # violet in shade
    "u": (128, 104, 186),    # violet, lit
    "c": (255, 122, 61),     # ember
    "C": (196, 78, 34),      # ember in shade
    "s": (242, 230, 196),    # parchment, sand
    "S": (204, 184, 140),    # parchment in shade
    "w": (255, 255, 255),    # shine
    "W": (196, 190, 214),    # steel, bone, stone
    "x": (110, 104, 130),    # steel in shade
    "r": (220, 48, 62),      # red
    "R": (140, 24, 36),      # red in shade
    "b": (110, 200, 245),    # sapphire
    "B": (60, 120, 200),     # sapphire in shade
    "m": (214, 120, 255),    # portal light
    "M": (255, 214, 255),    # portal light, bright
    "k": (122, 78, 44),      # wood, ink, earth
    "K": (82, 52, 32),       # wood in shade
    "y": (255, 214, 90),     # glow, sparks
    "n": (96, 170, 90),      # grass
    "N": (60, 120, 60),      # grass in shade
}

ICONS = [
    # the quest log: a scroll unrolled, its lines of writing and a red wax seal (the Quests button)
    ("quest", [
        "................",
        ".oooooooooooooo.",
        ".osssssssssssso.",
        ".oSSSSSSSSSSSSo.",
        ".oossssssssssoo.",
        "..osssssssssso..",
        "..oskkkkkkksso..",
        "..osssssssssso..",
        "..oskkkkkkkkso..",
        "..osssssssssso..",
        "..oskkkkksooso..",
        "..ossssssorroo..",
        "..oosssssoRroo..",
        ".osssssssssssso.",
        ".oSSSSSSSSSSSSo.",
        ".oooooooooooooo.",
    ]),
    # the Nexus: the castle's keep, violet stone under gold battlements, its gate a swirl of portal light (the Nexus button)
    ("nexus", [
        "..o.oo.oo.oo.o..",
        "..ogoGgoGgoGgo..",
        "..ogggGgggGggo..",
        "..ouvvvvvvvvVo..",
        "..ouvvvvvvvvVo..",
        "..ouvVvVVvVvVo..",
        "..ouvvoooovvVo..",
        "..ouvommmmovVo..",
        "..ouvomMmmovVo..",
        "..ouvommMmovVo..",
        "..ouvomMMmovVo..",
        "..ouvommmmovVo..",
        "..ouvommmmovVo..",
        ".oddddddddddddo.",
        ".oGggggggggggdo.",
        ".oooooooooooooo.",
    ]),
    # dungeons cleared: a stone archway into the dark, a torch either side
    ("dungeon", [
        "................",
        ".....oooooo.....",
        "...ooWWWWWWoo...",
        "..oWWxxxxxxWWo..",
        "..oWxooooooxWo..",
        "..oWxoKKKKoxWo..",
        ".yoWxoKKKKoxWoy.",
        "cyoWxoKKKKoxWoyc",
        "oCoWxoKKKKoxWoCo",
        ".ooWxoKKKKoxWoo.",
        ".koWxoKKKKoxWok.",
        ".koWxoKKKKoxWok.",
        ".koWxoKKKKoxWok.",
        "oWWWxoooooxWWWo.",
        "oxxxxxxxxxxxxxxo",
        "oooooooooooooooo",
    ]),
    # Oryx chains completed: Oryx's gold crown, jewelled
    ("crown", [
        "................",
        "................",
        "..o....o....o...",
        ".oGo..oGo..oGo..",
        ".oGgoogGooggGo..",
        ".oggGoggGoggGo..",
        ".oggggggggggdo..",
        ".ogrgggbgggrdo..",
        ".ogRgggBgggRdo..",
        ".oggggggggggdo..",
        ".oGGGGGGGGGGdo..",
        "..oddddddddddo..",
        "..oooooooooooo..",
        "................",
        "................",
        "................",
    ]),
    # Oryx chains failed: the crown broken in two
    ("crownbroken", [
        "................",
        "................",
        "................",
        "..o....o..o.....",
        "..oGo..oGooGo...",
        "..oggo.oggoGgo..",
        ".ogggooggoggGo..",
        ".ogrggggoogggo..",
        ".oggggggo.ogrgo.",
        "oGGGGGGo..oggdo.",
        "ooddddo....oddo.",
        ".oooooo.....ooo.",
        "................",
        "................",
        "................",
        "................",
    ]),
    # player deaths: a gravestone on a mound of earth
    ("grave", [
        "................",
        ".....oooooo.....",
        "....oWWWWWWo....",
        "...oWWxWWWWWo...",
        "..oWWxoxWWWWo...",
        "..oWxoooxWWWo...",
        "..oWWxoxWWWWo...",
        "..oWWxoxWWWWo...",
        "..oWWWWWWWWWo...",
        "..oWWxxxxxWWo...",
        "..oWWWWWWWWWo...",
        "..oWWWWWWWWxo...",
        "oonnoWWWWWxonnoo",
        "onNNkkkkkkkkkNno",
        "okkkkKKKkkkKkkko",
        "oooooooooooooooo",
    ]),
    # monsters slain: two swords crossed
    ("swords", [
        ".oo..........oo.",
        "oWWo........oWWo",
        "oWwWo......oWwWo",
        ".oWwWo....oWwWo.",
        "..oWwWo..oWwWo..",
        "...oWwWooWwWo...",
        "....oWwWWwWo....",
        ".....oWwwWo.....",
        "....oWwWWwWo....",
        "...oooWoxWooo...",
        "..ogGo.oo.oGgo..",
        ".oggo......oggo.",
        "okko........okko",
        "okKo........oKko",
        "oGo..........oGo",
        "oo............oo",
    ]),
    # realm events defeated: an ember war banner with a star
    ("banner", [
        ".oGo............",
        ".ogooooooooooo..",
        ".ogocccccccccco.",
        ".ogoccccyccccco.",
        ".ogocccyGyccco..",
        ".ogoccyGGGycco..",
        ".ogocccyGyccco..",
        ".ogoccyccyccco..",
        ".ogocccccccccco.",
        ".ogoCCCCCCCCCCo.",
        ".ogoooCCCooCCo..",
        ".ogo..oCo..oCo..",
        ".ogo...o....o...",
        ".odo............",
        ".odo............",
        ".ooo............",
    ]),
    # realms closed: a castle tower, its gate shut and its flag down
    ("tower", [
        "...oo...........",
        "...orro.........",
        "..orRRo.........",
        "...oo...........",
        "..o.oo.oo.oo.o..",
        "..ovovvovvovVo..",
        "..ouvvvvvvvvVo..",
        "...ouvvvvvvVo...",
        "...ouvvooovVo...",
        "...ouvoxxxoVo...",
        "...ouvoxKxoVo...",
        "...ouvoxKxoVo...",
        "...ouvoxKxoVo...",
        "..ouuvvvvvvvVo..",
        "..oVVVVVVVVVVo..",
        "..oooooooooooo..",
    ]),
    # potions drunk: a flask of red potion
    ("potion", [
        "................",
        "......oooo......",
        "......okKo......",
        "......oWWo......",
        "......oWWo......",
        ".....ooWWoo.....",
        "....oWWwwWWo....",
        "...oWrrrrrrWo...",
        "..oWrwRrrrrWo...",
        "..oWrwrrrrrWo...",
        "..oWrrrrrrRWo...",
        "..oWrrrrrRRWo...",
        "...oWRrrrRRWo...",
        "...oWWRRRRWWo...",
        "....oooooooo....",
        "................",
    ]),
    # items forged: the anvil, a hammer coming down and sparks
    ("anvil", [
        "........ooo.....",
        ".......okKko....",
        "......okKko.y...",
        ".......oko...y..",
        ".......oko..y...",
        ".......ooo......",
        "..oooooooooooo..",
        ".oWWWWWWWWWWWWo.",
        ".ooxxxxxxxxxxxo.",
        "..ooxxxxxxxxoo..",
        "....oxxxxxxo....",
        ".....oxxxxo.....",
        "....oWWWWWWo....",
        "...oxxxxxxxxo...",
        "...oooooooooo...",
        "................",
    ]),
    # the most slain monster: a skull
    ("skull", [
        "................",
        ".....oooooo.....",
        "....oWWWWWWo....",
        "...oWWWWWWWWo...",
        "..oWWwwwwWWWWo..",
        "..oWwWWWWWWWWo..",
        "..oWWooWWooWWo..",
        "..oWoooWWoooWo..",
        "..oWWooWWooWWo..",
        "..oWWWWooWWWWo..",
        "...oWWWooWWWo...",
        "....oWxWxWxo....",
        "....oWoWoWoo....",
        ".....ooooooo....",
        "................",
        "................",
    ]),
    # the week: an hourglass running down
    ("hourglass", [
        "................",
        "..oooooooooooo..",
        "..oGggggggggdo..",
        "..oooooooooooo..",
        "...osssssssso...",
        "....osssssso....",
        "....ossssoo.....",
        "......osso......",
        "......oSso......",
        ".....oSsSso.....",
        "....oSSssSSo....",
        "...oSSSSSSSSo...",
        "..oooooooooooo..",
        "..oGggggggggdo..",
        "..oooooooooooo..",
        "................",
    ]),
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
    sheet = Image.new("RGBA", (CELL * len(ICONS), CELL), (0, 0, 0, 0))
    for i, (_, rows) in enumerate(ICONS):
        sheet.paste(cell(rows), (i * CELL, 0))
    sheet.save(OUT)
    print(f"wrote {OUT}")
    if "--preview" in sys.argv:
        path = sys.argv[sys.argv.index("--preview") + 1]
        bg = Image.new("RGBA", sheet.size, (40, 30, 56, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 10, sheet.height * 10), Image.NEAREST).save(path)
        print(f"preview {path}")


if __name__ == "__main__":
    main()
