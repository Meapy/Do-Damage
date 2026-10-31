"""The Nexus garden's second round of art: the elder cherry tree north of the spawn, the smithy's
smoke and flame, the smith god Amatsumara and his anvil, the XP ducks, the twins, the Armoire's
new wardrobe, the Arena's portal sunk into the shrine's floor, and walking frames for the Name
Changer and the Character Changer from their own sprites."""
import math
import os

from PIL import Image

from .px import hexc as C, canvas, dither, hash2, mix, picture, put
from .sprites import BLOSSOM, GLOW, STONE, WOOD, column, edge, sakura, slab
from .floors import RIM, EMBER as FIRE
from .models import GOLD

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LOFI_CHAR = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "prod", "images", "lofiChar.png")


def elder_sakura():
    """The great cherry of the dry garden: 192 pixels, twenty-four tiles tall, its crown over the
    whole garden."""
    return sakura(61, size=192)


def smoke(frame, frames=4):
    """Smoke from the smithy's chimney: puffs rising and spreading as they fade, 16 by 32."""
    img = canvas(16, 32)
    for k in range(4):
        t = ((k + frame / frames) / 4)                 # 0 at the chimney, 1 at the top
        cy = 30 - t * 28
        cx = 8 + math.sin(t * 4 + k) * 2 + t * 3
        r = 2.4 + t * 3.6
        shade = [C("#4a4650"), C("#6c6874"), C("#8e8a96"), C("#b4b0ba")]
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy) / r
                if d > 1 or hash2(x, y, k + frame * 7) < t * t * 0.45:
                    continue
                put(img, x, y, dither(shade, 0.85 - d * 0.5 - t * 0.3 + (0.15 if x < cx and y < cy else 0), x, y))
    return img


def forge_flame(frame):
    """Fire licking out of the furnace's mouth, 8 by 8."""
    img = canvas(8)
    for x in range(1, 7):
        h = 2 + int(3 * abs(math.sin(x * 1.7 + frame * 2.1))) + (x in (3, 4))
        for k in range(h):
            put(img, x, 7 - k, FIRE[min(4, 4 - k + (x in (3, 4)))] if k < h - 1 else FIRE[2])
    return img


def anvil():
    """A black iron anvil on a stump, glowing metal on its face, 16 by 16."""
    img = canvas(16)
    column(img, 5, 10, 11, 15, WOOD)                   # the stump
    iron = [C("#141218"), C("#24222a"), C("#3a3842"), C("#565464"), C("#7a7888")]
    slab(img, 6, 9, 8, 10, iron)                       # the waist
    slab(img, 2, 13, 6, 7, iron)                       # the face and horn
    put(img, 1, 6, iron[2])
    put(img, 0, 6, iron[1])
    for x, c in ((6, FIRE[3]), (7, FIRE[4]), (8, FIRE[3])):
        put(img, x, 5, c)                              # a glowing billet on it
    edge(img)
    return img


def smith_god(frame):
    """Amatsumara, the smith god: a towering one-eyed smith, bronze-skinned and bare-chested in a
    leather apron, a white rope tied round his brow and his hair bound up, his great hammer
    raised (frame 1), struck down in a shower of sparks (2), or at rest (0). 16 by 16, facing
    right toward his anvil."""
    pal = {
        "s": C("#c07a4a"), "S": C("#8a5030"), "h": C("#e8e4dc"), "H": C("#b8b4ac"), "w": C("#f4f0e8"),
        "e": C("#ffcc40"), "a": C("#5a3420"), "A": C("#3a2014"), "b": C("#2a1e1a"), "i": C("#4a4858"),
        "I": C("#7a7888"), "t": C("#6a4a2a"), "y": C("#ffd27a"), "Y": C("#fff0b8"), "r": C("#c84a2a"),
    }
    body = [
        "......hhh.......",
        ".....hhhhh......",
        ".....wwwww......",
        "....hsseSs......",
        "....hssssS......",
        ".....hhhh.......",
        "...sssssssS.....",
        "..sSsaaaasSs....",
        "..sS.aAAa.Ss....",
        "..sS.aAAa.Ss....",
        ".....aAAa.......",
        ".....aaaa.......",
        ".....bb.bb......",
        ".....bb.bb......",
        "....bbb.bbb.....",
        "................",
    ]
    img = picture(body, pal, 16, 16)
    if frame == 0:          # the hammer resting on his shoulder
        for (x, y) in ((11, 5), (11, 6), (10, 7), (10, 8), (9, 9)):
            put(img, x, y, pal["t"])
        for (x, y) in ((11, 3), (12, 3), (12, 4), (11, 4), (13, 4)):
            put(img, x, y, pal["i"] if x != 12 else pal["I"])
    elif frame == 1:        # raised high
        for (x, y) in ((11, 7), (12, 6), (12, 5), (13, 4), (13, 3)):
            put(img, x, y, pal["t"])
        for (x, y) in ((12, 0), (13, 0), (14, 0), (12, 1), (13, 1), (14, 1), (14, 2)):
            put(img, x, y, pal["I"] if y == 0 else pal["i"])
    else:                   # struck down, sparks flying
        for (x, y) in ((11, 8), (12, 9), (13, 10)):
            put(img, x, y, pal["t"])
        for (x, y) in ((13, 11), (14, 11), (15, 11), (13, 12), (14, 12), (15, 12)):
            put(img, x, y, pal["I"] if y == 11 else pal["i"])
        for (x, y), c in (((12, 13), "Y"), ((15, 9), "y"), ((14, 8), "Y"), ((11, 12), "y"), ((15, 13), "r")):
            put(img, x, y, pal[c])
    edge(img)
    return img


def duck(frame):
    """An XP duck, a mallard bobbing on the water, 8 by 8 (the pond's water hides its bottom)."""
    pal = {"g": C("#1e6a3a"), "G": C("#3a9a5a"), "w": C("#f4f0e8"), "b": C("#8a6a4a"), "B": C("#b48a5e"),
           "y": C("#f2b030"), "k": C("#1a1418"), "W": C("#d8d0c4")}
    rows = [
        "........",
        "....gG..",
        "....gkyy",
        "....wg..",
        ".BbbbB..",
        "bBBbbWb.",
        ".bbbbb..",
        "........",
    ]
    if frame == 1:
        rows = ["........"] + rows[:-1]
    if frame == 2:
        rows = ["........", "........", "....gG..", "...gkyy.", "....w...", ".BbbbB..", "bBBbbWb.", ".bbbbb.."]
    return picture(rows, pal, 8, 8)


TWINS = {
    # Hana in pink, her hair in two buns with ribbons, and Sora in blue: they run about together
    "NG Twin Hana": ({"h": C("#2a1a1a"), "r": C("#ff7aa8"), "s": C("#f8d0b0"), "S": C("#d8a888"), "e": C("#1a1014"),
                      "p": C("#f08ab0"), "P": C("#ffc0d6"), "o": C("#c8365a"), "k": C("#3a2418")}, [
        "........",
        "..rh.hr.",
        "..hhhh..",
        "..hsse..",
        "...ss...",
        "..pPPp..",
        "..pooP..",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
    "NG Twin Sora": ({"h": C("#2a1a1a"), "r": C("#6ab0ff"), "s": C("#f8d0b0"), "S": C("#d8a888"), "e": C("#1a1014"),
                      "p": C("#4a7ad0"), "P": C("#8ab4f0"), "o": C("#24407a"), "k": C("#3a2418")}, [
        "........",
        "..rh.hr.",
        "..hhhh..",
        "..hsse..",
        "...ss...",
        "..pPPp..",
        "..pooP..",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
}


def armoire():
    """The Armoire as a tansu: a tall wardrobe of dark rose wood with iron fittings, a pair of
    doors above and drawers below, a kimono showing at its open door. 16 by 16."""
    img = canvas(16)
    rose = [C("#2e1018"), C("#4a1a26"), C("#682634"), C("#863444"), C("#a44a5a")]
    iron = [C("#1a1820"), C("#3a3842"), C("#6a6878")]
    column(img, 3, 12, 1, 15, rose, top=0.2)
    for x in range(3, 13):
        put(img, x, 0, rose[3])
    put(img, 2, 0, rose[2])
    put(img, 13, 0, rose[1])
    for y in (7, 10, 13):                               # the drawers' edges
        for x in range(4, 12):
            put(img, x, y, rose[0])
    for y in (8, 11, 14):                               # their iron pulls
        put(img, 6, y, iron[2])
        put(img, 9, y, iron[2])
    for y in range(2, 7):                               # the doors, the right one ajar
        put(img, 7, y, rose[0])
    for y in range(2, 7):
        for x in range(9, 12):
            put(img, x, y, (C("#f08ab0") if (x + y) % 2 else C("#ffd0e0")) if x < 11 else C("#c8365a"))
    for x, y in ((4, 2), (11, 2), (4, 6), (6, 4)):      # the iron corners and lock
        put(img, x, y, iron[1])
    edge(img)
    return img


def arena_portal(frame, frames=4, size=32):
    """The Arena's portal, sunk into the shrine's floor: a round pit of raked sand ringed with
    stone, crimson banners at the four quarters, two crossed blades glowing in the sand that
    pulse with the frames. 32 by 32, four tiles across."""
    img = canvas(size)
    c = (size - 1) / 2
    sand = [C("#6a5638"), C("#8a7048"), C("#a88a5a"), C("#c4a672"), C("#dcc290")]
    pulse = 0.5 + 0.5 * math.sin(frame / frames * math.tau)
    for y in range(size):
        for x in range(size):
            dx, dy = x - c, y - c
            r = math.hypot(dx, dy)
            if r > size / 2 - 0.5:
                continue
            if r > size / 2 - 3.5:
                t = 0.6 - 0.3 * (dx + dy) / size - (0.25 if r > size / 2 - 1.2 else 0)
                put(img, x, y, dither(RIM, t, x, y))
                continue
            # the sand, raked in rings, darker toward the edge as the pit deepens
            ring = (r * 1.1) % 2 < 1
            t = 0.65 - 0.4 * (r / (size / 2)) + (0.08 if ring else -0.04)
            put(img, x, y, dither(sand, t, x, y))
    for ang in (0, 90, 180, 270):                        # the banners at the quarters
        a = math.radians(ang - 90)
        bx, by = c + math.cos(a) * (size / 2 - 2), c + math.sin(a) * (size / 2 - 2)
        for k in range(-1, 2):
            for j in range(-1, 2):
                put(img, int(bx + k), int(by + j), C("#a8242a") if (k + j) % 2 else C("#e04a3a"))
    glow = [C("#5a1410"), C("#a82a1a"), C("#f05a2a"), C("#ffb070"), C("#fff0c8")]
    for s in (1, -1):                                    # two crossed blades
        for k in range(-9, 10):
            x, y = int(round(c + k * 0.7)), int(round(c + s * k * 0.7))
            v = 0.45 + 0.45 * pulse - abs(k) * 0.02
            put(img, x, y, dither(glow, v + 0.2, x, y))
            put(img, x + 1, y, dither(glow, v, x, y))
        hx, hy = int(round(c - 9 * 0.7)), int(round(c - s * 9 * 0.7))
        for k in range(-2, 3):
            put(img, hx + k, hy + (k * s), C("#e0b040"))  # the hilts' guards
    return img


def changer(index):
    """The Name Changer's or the Character Changer's own sprite (lofiChar8x8) as a walking strip:
    stand, then two steps where only the feet move (the bottom row slides a pixel each way), so
    nothing above them is cut off."""
    sheet = Image.open(LOFI_CHAR).convert("RGBA")
    cols = sheet.width // 8
    stand = sheet.crop(((index % cols) * 8, (index // cols) * 8, (index % cols) * 8 + 8, (index // cols) * 8 + 8))
    return walk_strip(stand, 8)


def guild_register():
    """The Guild Register: a kōsatsu, a notice board under its own little teal roof on two posts,
    GUILD painted across its head in gold and its notices below. 32 by 32."""
    from .models import TEAL, GLYPHS, BLACK as INK
    img = canvas(32)
    for x in (6, 25):
        column(img, x, x + 1, 9, 31, WOOD)
    board = [C("#6a4a2e"), C("#8a6440"), C("#a87e52"), C("#c49a68")]
    for y in range(10, 26):
        for x in range(5, 27):
            t = 0.6 + (0.25 if y == 10 else 0) - (0.3 if y == 25 or x in (5, 26) else 0) + 0.08 * (hash2(x, y, 3) - 0.5)
            put(img, x, y, dither(board, t, x, y))
    x = 7
    for ch in "GUILD":
        for j, row in enumerate(GLYPHS[ch]):
            for i, bit in enumerate(row):
                if bit == "1":
                    put(img, x + i, 12 + j, GOLD[3] if j < 2 else GOLD[2])
        x += 4
    for y in (19, 22):                                       # its notices: lines of brushed script
        for x in range(8, 24):
            if hash2(x, y, 7) > 0.3:
                put(img, x, y, INK[1])
    for i, y in enumerate(range(4, 10)):                     # the roof, wider each row
        for x in range(9 - i * 2 + 1, 23 + i * 2):
            put(img, x, y, dither(TEAL, 0.75 - 0.35 * (x - 9 + i * 2) / (14 + 4 * i), x, y))
    put(img, 1, 9, TEAL[4])
    put(img, 30, 9, TEAL[3])
    edge(img)
    return img


def walk_strip(stand, size, feet=1):
    """Seven frames from a standing picture: stand, a step with its lowest `feet` rows slid left
    and one with them slid right, then the stand again."""
    steps = []
    for d in (-1, 1):
        f = stand.copy()
        for y in range(size - feet, size):
            row = [stand.getpixel((x, y)) for x in range(size)]
            for x in range(size):
                f.putpixel((x, y), row[x - d] if 0 <= x - d < size else (0, 0, 0, 0))
        steps.append(f)
    out = Image.new("RGBA", (size * 7, size))
    for i, f in enumerate([stand, steps[0], steps[1], stand, stand, stand, canvas(size)]):
        out.paste(f, (i * size, 0))
    return out


QUEST_NPCS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images", "QuestNpcs16x16.png")


def quest_npc(index):
    """The Tinkerer (0), the Chronicler (2) or the Market Broker (4), from their 16 by 16 sprites
    (tools/draw_quest_npcs.py), as walking strips: their feet step, the rest stays whole."""
    sheet = Image.open(QUEST_NPCS).convert("RGBA")
    stand = sheet.crop((index * 16, 0, index * 16 + 16, 16))
    return walk_strip(stand, 16, 2)


def samurai_statue():
    """A samurai in bronze gone green, his sword raised, on a granite plinth: the dojo yard's
    monument, where the Arena's champions are honoured. 32 by 32."""
    img = canvas(32)
    bronze = [C("#1e3a34"), C("#2a5048"), C("#3a6a5e"), C("#5a8a78"), C("#86b0a0")]
    granite = [C("#3a3848"), C("#56546a"), C("#74728a"), C("#9896aa")]
    column(img, 7, 24, 26, 31, granite, top=0.2)
    column(img, 9, 22, 23, 25, granite, top=0.2)
    rows = [
        "......kk........",
        ".....kHHk.......",
        "....kHHHHk......",
        ".....hfFh.......",
        "....aAAAAa......",
        "...aAAhAAAa.....",
        "..aA.aAAa.Aa....",
        "..aA.aAAa.Aa....",
        "..Hh.aAAa.hH....",
        ".....aAAa.......",
        "....aaAAaa......",
        "....aA..Aa......",
        "....aA..Aa......",
        "...aaA..Aaa.....",
    ]
    pal = {"k": bronze[0], "H": bronze[3], "h": bronze[2], "f": bronze[1], "F": bronze[2], "a": bronze[1], "A": bronze[3]}
    from .px import picture as pic
    fig = pic(rows, pal, 16, 14)
    img.alpha_composite(fig.resize((16, 14)), (9, 9))
    for k in range(9):                                     # the raised katana
        put(img, 20 + k // 2, 9 - k, bronze[4] if k > 1 else bronze[2])
    put(img, 19, 10, GLOW[2])
    edge(img)
    return img


def nobori(frame, colour):
    """A tall nobori banner on its pole, stirring in the wind (two frames). 16 wide, 32 tall, in a
    32 cell."""
    img = canvas(32)
    pole = [C("#2a1a12"), C("#4a3020"), C("#6a4a30")]
    for y in range(2, 32):
        put(img, 12, y, pole[1])
        put(img, 13, y, pole[2])
    for x in range(12, 21):
        put(img, x, 3, pole[1])
    cloth = colour
    for y in range(4, 24):
        sway = int(round(math.sin(y * 0.35 + frame * 1.7) * 0.8))
        for x in range(14, 20):
            t = 0.75 - 0.4 * (x - 14) / 5 + (0.1 if y % 6 == 0 else 0)
            put(img, x + sway, y, dither(cloth, t, x, y))
        if 8 <= y <= 18 and y % 3 != 2:
            put(img, 16 + sway, y, C("#fff4ea"))           # the painted characters
            put(img, 17 + sway, y, C("#fff4ea") if y % 3 == 0 else cloth[2])
    edge(img)
    return img


def weapon_rack():
    """A wooden rack of katana and a spear. 16 by 16."""
    img = canvas(16)
    for x in (2, 13):
        column(img, x, x + 1, 4, 15, WOOD)
    for y in (6, 11):
        for x in range(2, 15):
            put(img, x, y, dither(WOOD, 0.7, x, y))
    steel = [C("#5a5868"), C("#9a98a8"), C("#d8d6e4")]
    for i, x in enumerate((5, 8, 11)):
        for y in range(3, 14):
            put(img, x, y, steel[2] if y < 10 else (C("#1a1418") if y < 13 else C("#a8242a")))
        put(img, x - 1, 10, GOLD[2] if i != 2 else GOLD[1])
        put(img, x + 1, 10, GOLD[2])
    for y in range(0, 15):
        put(img, 14 + (y < 3), y, steel[1] if y < 3 else WOOD[2])   # the spear
    edge(img)
    return img


def straw_dummy():
    """A wara ningyo: a straw training dummy on a post, a cross-bar for arms. 16 by 16."""
    img = canvas(16)
    straw = [C("#5a4422"), C("#7a5e30"), C("#9c7c40"), C("#bc9c58"), C("#d8bc78")]
    column(img, 7, 8, 9, 15, WOOD)
    for x in range(3, 13):
        put(img, x, 6, dither(WOOD, 0.6, x, 6))
    from .sprites import ball
    ball(img, 8, 9, 3.2, 4.0, straw, noise=0.25, seed=4)
    ball(img, 8, 3.5, 2.4, 2.4, straw, noise=0.25, seed=5)
    for x in range(5, 12):
        put(img, x, 9, C("#a8242a"))                         # a red cord round it
    edge(img)
    return img


def taiko():
    """A taiko drum on its stand, its skin pale, its body lacquered red with brass studs. 16 by 16."""
    img = canvas(16)
    from .sprites import LACQUER, ball
    for x, y0 in ((3, 9), (12, 9)):
        column(img, x, x + 1, y0, 15, WOOD)
    ball(img, 8, 9, 5.5, 4.5, LACQUER, seed=2)
    skin = [C("#c8b490"), C("#e0d0b0"), C("#f4ead4")]
    for y in range(6, 13):
        for x in range(4, 13):
            if ((x - 8) / 3.6) ** 2 + ((y - 9) / 3.0) ** 2 <= 1:
                put(img, x, y, dither(skin, 0.75 - 0.3 * (x - 4) / 8, x, y))
    for a in range(0, 360, 45):
        put(img, int(8 + math.cos(math.radians(a)) * 4.6), int(9 + math.sin(math.radians(a)) * 3.8), GOLD[2])
    edge(img)
    return img
