"""The Nexus's villagers and bees: 8 by 8 characters facing right, as the game's monsters are, in
strips of seven frames (stand, two walking steps, unused, two attack frames and the attack's
second cell). Villagers walk between the village's houses and stop to talk (NexusVillagers on the
world server); the bees drift over the flowers."""
from PIL import Image

from .px import hexc as C, picture

SKIN = {"s": C("#f2c8a0"), "S": C("#c8946c"), "e": C("#2a1a1a")}

# Each: rows standing, then the two steps' bottom rows (legs). A row is 8 pixels.
VILLAGERS = {
    # a monk in saffron, prayer beads at his chest
    "NG Villager Monk": ({**SKIN, "o": C("#d06a1e"), "O": C("#f09a3a"), "b": C("#5a2a1a"), "k": C("#3a2418")}, [
        "...ss...",
        "..ssse..",
        "...ss...",
        "..oOOo..",
        "..OboOs.",
        "..oOOo..",
        "..oooo..",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
    # a lady in a pink kimono with a red obi, her hair up with a pin
    "NG Villager Kimono": ({**SKIN, "h": C("#1e1418"), "y": C("#f6d36a"), "p": C("#d86a98"), "P": C("#f6a6c6"),
                            "r": C("#b02a2e"), "w": C("#fff2f6")}, [
        "..hhy...",
        "..hhhh..",
        "..hsse..",
        "...ss...",
        "..pPPp..",
        "..rrrr..",
        "..pPwp..",
        "..pPPp..",
    ], ["..pP.Pp.", ".pPPp..."]),
    # a farmer under a straw kasa, in indigo
    "NG Villager Farmer": ({**SKIN, "y": C("#d8b468"), "Y": C("#a8843e"), "b": C("#2e3e6e"), "B": C("#46609a"),
                            "k": C("#3a2a1e")}, [
        "...yy...",
        ".yYYyyy.",
        "..ssse..",
        "...ss...",
        "..bBBb..",
        "..bBBbs.",
        "...bb...",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
    # a shrine maiden: white haori, red hakama
    "NG Villager Miko": ({**SKIN, "h": C("#241820"), "w": C("#f2f0f0"), "W": C("#c8c4cc"), "r": C("#c82a2e"),
                          "R": C("#e84a42"), "t": C("#f2f0f0")}, [
        "..hhh...",
        ".hhhht..",
        ".hhsse..",
        "..hss...",
        "..wWWw..",
        "..wWWws.",
        "..rRRr..",
        "..r..r..",
    ], [".r...r..", "..rRr..."]),
    # an old sage with a white beard and a tall staff
    "NG Villager Sage": ({**SKIN, "w": C("#f2f2f0"), "W": C("#c8c8cc"), "g": C("#5a6a7a"), "G": C("#7a8a9a"),
                          "t": C("#8a5e3c"), "T": C("#c89a5a")}, [
        "..ww...T",
        "..wsse.t",
        "..wWWs.t",
        "..gWWgst",
        "..gGGg.t",
        "..gGGg.t",
        "..gGGg.t",
        "..g..g.t",
    ], [".g...g.t", "..gGg..t"]),
    # a merchant carrying a bundle wrapped in a furoshiki
    "NG Villager Merchant": ({**SKIN, "h": C("#2a1a14"), "n": C("#5a3a24"), "N": C("#7e5434"), "f": C("#2e7a5a"),
                              "F": C("#4aa07a"), "k": C("#2a1a14")}, [
        "...hh...",
        "..hsse..",
        "...ss...",
        "fFnNNn..",
        "fFnNNns.",
        "ffnNNn..",
        "..nnnn..",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
    # a child in yellow, running about
    "NG Villager Child": ({**SKIN, "h": C("#3a2418"), "y": C("#f2c03a"), "Y": C("#ffe07a"), "k": C("#3a2418")}, [
        "........",
        "...hh...",
        "..hsse..",
        "...ss...",
        "..yYYy..",
        "..yYYys.",
        "...yy...",
        "...k.k..",
    ], ["..k...k.", "...kk..."]),
}

BEE = ({"w": C("#e8f4ff"), "W": C("#b4d0e8"), "y": C("#f6c83a"), "Y": C("#ffe27a"), "k": C("#2a1e14")}, [
    "........",
    "...ww...",
    "..wWww..",
    "..ykyYk.",
    ".kYkyYy.",
    "..ykyk..",
    "........",
    "........",
], [
    "........",
    "........",
    "..wwW...",
    ".wykyYk.",
    ".kYkyYy.",
    "..ykyk..",
    "........",
    "........",
])


def strip(pal, rows, steps):
    """The seven frames: stand, step one, step two (each with its own legs and a bob), unused,
    and the attack frames, which villagers never use, as the stand."""
    stand = picture(rows, pal, 8, 8)
    frames = [stand]
    for legs in steps:
        f = picture(rows[:-1] + [legs], pal, 8, 8)
        frames.append(f)
    frames += [stand, stand, stand, Image.new("RGBA", (8, 8))]
    out = Image.new("RGBA", (56, 8))
    for i, f in enumerate(frames):
        out.paste(f, (i * 8, 0))
    return out


def bee_strip():
    pal, up, down = BEE
    a, b = picture(up, pal, 8, 8), picture(down, pal, 8, 8)
    out = Image.new("RGBA", (56, 8))
    for i, f in enumerate([a, b, a, a, a, b, Image.new("RGBA", (8, 8))]):
        out.paste(f, (i * 8, 0))
    return out
