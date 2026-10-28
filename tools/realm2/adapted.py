"""Realm 2 monsters drawn from the game's own art (adapt.py): for each, the monster it starts from
and how its colours go into its land's. They replace art.py's pictures of the same name, 16 by 16
and seven frames as their originals.

Ramps run dark to light; a letter is the land's palette's colour (art.py).
"""

# a few ramps more than one land uses
SLATE = ["#16181e", "#262a32", "#3a3f4a", "#5a6070", "#848c9e", "#b4bccc"]
GHOST_GREEN = ["#14402e", "#227a52", "#3ac080", "#88f0b8", "#d4ffe8"]
GOLD = ["#5a3a08", "#9a6a10", "#d8a020", "#f8d860", "#fff4b8"]
BLOOD = ["#240606", "#4a0e0e", "#721616", "#a02620", "#c8423a"]
EMBER = ["#a03808", "#e06010", "#ffa030", "#ffe080"]
ROBE = ["#0e0a0c", "#1a1214", "#2a1c1e", "#3e2628", "#5a2c2c", "#8a2a24"]   # cultists' black, bled red at the edges
LOST_STONE = ["#2a3140", "#3e4858", "#566278", "#76829a", "#a0aac0", "#d0d8e6"]

ADAPTED = {
    # --- the Forgotten Kingdom: the Shatters' mages and guardians, in its slate, violet and teal
    "R2 Forgotten Mage": ("kingdom", {
        "from": "Oryx Cleric",
        "rules": [((330, 30), ["#1a1030", "#2e1c52", "#4a2e80", "#6a46a8", "#9474d8", "#c0a8f4"]),
                  ((30, 75), ["#127068", "#20a8a0", "#5ce0d4", "#b8fff4"]),
                  ("grey", SLATE)],
    }),
    "R2 Shattered Knight": ("kingdom", {
        "from": "shtrs Royal Guardian J",
        "rules": [((330, 40), ["#106862", "#20a8a0", "#5ce0d4", "#b8fff4"]),
                  ("grey", SLATE), ("dark", SLATE[:2])],
    }),
    "R2 Stone Gargoyle": ("kingdom", {
        "from": "Arena Statue Left",
        "rules": [((15, 75), ["#1c1e24", "#2c3038", "#3e434e", "#585e6c", "#767e90", "#9aa2b4"]),
                  ((330, 15), ["#20a8a0", "#90fff0"]),
                  ("grey", ["#141418", "#22242c", "#363a46", "#4e5462", "#6a7284"])],
    }),
    "R2 Forgotten Archmage": ("kingdom", {
        "from": "Oryx Deacon",
        "rules": [((240, 330), ["#140c26", "#24163e", "#3a2460", "#5a3a96", "#8462c8", "#b898f0"]),
                  ((20, 80), ["#106862", "#20a8a0", "#5ce0d4", "#b8fff4"]),
                  ("grey", SLATE)],
    }),
    "R2 King's Champion": ("kingdom", {
        "from": "Oryx Officer",
        "rules": [((330, 25), ["#1e1236", "#3a2460", "#5a3a96", "#8462c8"]),
                  ((25, 80), ["#127068", "#20a8a0", "#5ce0d4", "#b8fff4"]),
                  ((180, 270), SLATE[1:]), ("grey", SLATE)],
    }),
    # --- the Lost Lands: its grey stone and pale blue light
    "R2 Lost Sentry": ("lost", {
        "from": "LH Marble Defender",
        "rules": [("grey", LOST_STONE), ((0, 360), ["#3a8ab0", "#a8ecff"])],
    }),
    "R2 Ruin Golem": ("lost", {
        "from": "Ruins Boss Golem",
        "rules": [((0, 360), LOST_STONE[1:] + ["#eef2f8"]), ("grey", LOST_STONE[1:])],
        "contrast": 1.2,
    }),
    "R2 Wailing Wraith": ("lost", {
        "from": "Vengeful Spirit",
        "rules": [((300, 60), ["#2a7ab0", "#a8ecff"]),
                  ("grey", ["#2a4a5a", "#3e6a7c", "#5a92a6", "#8cc4d6", "#c8f0fc"])],
    }),
    "R2 Forsaken Archer": ("lost", {
        "from": "LH Dirge",
        "rules": [("grey", ["#1a2030", "#2a3140", "#3e4858", "#5a6478", "#8a94a8", "#c0cad8"]),
                  ((0, 360), ["#2a6a8a", "#3a8ab0", "#7ad0f0", "#c8f4ff"])],
    }),
    # --- the Wreck Coast: drowned pirates in its sea-green light
    "R2 Drowned Buccaneer": ("ship", {
        "from": "Zombie Pirate Sr",
        "rules": [((70, 175), GHOST_GREEN),
                  ((330, 30), ["#16201e", "#22302c", "#34443e", "#4c6058"]),
                  ((30, 70), ["#3c2c20", "#6c5038", "#a8885c"]),
                  ("grey", ["#5c7060", "#9ab0a0", "#d0e4d8"])],
    }),
    "R2 Bone Gunner": ("ship", {
        "from": "Ghost Pirate Sr",
        "rules": [((180, 270), ["#3e5a4c", "#6e9a82", "#a8d4b8", "#e0fff0"]),
                  ((330, 30), ["#16201e", "#22302c", "#34443e", "#4c6058"]),
                  ((30, 70), ["#3c2c20", "#6c5038", "#a8885c"]),
                  ((300, 330), ["#3ac080", "#b8ffd0"]),
                  ("grey", ["#34443a", "#5c7060", "#9ab0a0"])],
    }),
    "R2 Wreck Wraith": ("ship", {
        "from": "Ghost God",
        "rules": [((150, 260), GHOST_GREEN), ("grey", ["#88f0b8", "#d4ffe8"])],
    }),
    "R2 Phantom Sloop": ("ship", {
        "from": "Ghost Ship",
        "rules": [(("lum", 0.5, 1.01), ["#2a8a5a", "#5cd096", "#a8f8cc", "#e0fff0"]),
                  ((0, 360), ["#140f0c", "#241c14", "#36291e", "#4a3a2a", "#5e4a34"])],
    }),
    # --- the Tidepools
    "R2 Ancient Snapper": ("hermit", {
        "from": "Giant Crab",
        "rules": [((345, 12), ["#0a2a28", "#123e3a", "#1e5a50", "#2e7a68", "#4aa088"]),
                  ((12, 60), ["#6a2416", "#a04030", "#d06848", "#f0a080", "#ffd0b8"])],
    }),
    # --- the Sunken Tombs (never the Sand Scourge: the owner keeps its sprite)
    "R2 Tomb Sentinel": ("sphinx", {
        "from": "Jackal Lord",
        "rules": [((70, 170), GOLD),
                  ((190, 270), ["#081830", "#0e2a5a", "#16408a", "#2a6ac0", "#6aa6f0"]),
                  ((20, 70), ["#7a5410", "#c89428", "#f0cc58", "#fff0a0"])],
    }),
    "R2 Tomb Mummy": ("sphinx", {
        "from": "Ruins Elemental",
        "rules": [((0, 90), ["#3a2e1e", "#6a5a40", "#9a8a66", "#c8b894", "#ece2c8"]),
                  ("grey", ["#4a3c28", "#a8966c", "#f0e8d0"])],
    }),
    "R2 Jackal Guard": ("sphinx", {
        "from": "Werewolf",
        "rules": [((300, 70), ["#120e16", "#1e1a26", "#2e2a3a", "#443e52", "#c89428", "#f0cc58"]),
                  ("grey", ["#1e1a26", "#443e52", "#80b8ff"])],
    }),
    "R2 Sun Obelisk": ("sphinx", {
        "from": "Boss Totem",
        "rules": [((60, 170), ["#0e2a5a", "#16408a", "#2a6ac0", "#6aa6f0"]),
                  ((330, 30), ["#d8a020", "#fff0a0"]),
                  ("grey", ["#4a3414", "#6a4a1c", "#9a7838", "#c8a860", "#e8d090"])],
    }),
    # --- the Bonefields
    "R2 Shrine Cultist": ("skull", {
        "from": "LH Malus",
        "rules": [((330, 22), ROBE), ((22, 75), EMBER), ("grey", ["#8a7a60", "#cabb98", "#f4ecd8"])],
    }),
    "R2 Shrine Keeper": ("skull", {
        "from": "Phylactery Bearer",
        "rules": [((330, 22), ROBE),
                  ((70, 170), ["#8a7a60", "#cabb98", "#f4ecd8", "#ffc040"]),
                  ((22, 70), EMBER), ("grey", ["#5a4c38", "#8a7a60", "#cabb98"])],
    }),
    # --- the Jade Garden: its jade and blossom
    "R2 Oni Mask": ("garden", {
        "from": "Mixcoatl the Masked God",
        "rules": [((230, 300), ["#4a0a10", "#7a1220", "#a81c2c", "#d83a3a", "#f07060"]),
                  ((300, 360), ["#1e0608", "#3a0a0e"]),
                  ((0, 25), ["#1e0608", "#3a0a0e", "#5a1018"]),
                  ((160, 230), ["#1e3e1e", "#2a9a70", "#90f0c0"]),
                  ((25, 75), GOLD[1:])],
    }),
    "R2 Komainu": ("garden", {
        "from": "Hellhound",
        "rules": [((300, 40), ["#d8a020", "#fff0a0"]),
                  ("dark", ["#4a5450", "#5a6662"]),
                  ("grey", ["#5a6662", "#7a8a84", "#a0b0a8", "#c8d6ce", "#eef6f0"])],
    }),
    "R2 Kitsune Spirit": ("garden", {
        "from": "Ultimate Squirrel",
        "rules": [((0, 60), ["#8a3a5a", "#b05878", "#e890b0", "#ffd8e8", "#fff6fa"]),
                  ("grey", ["#2a9a70", "#90f0c0"])],
    }),
    "R2 Lantern Wisp": ("garden", {
        "from": "vlntns Bella Bud 1",
        "rules": [((300, 40), ["#8a3a5a", "#b05878", "#e890b0", "#ffd8e8"]),
                  ((60, 170), ["#1e3e1e", "#3c6c34", "#74a050", "#a8d070"])],
    }),
    "R2 Jade Golem": ("garden", {
        "from": "Earth Elemental",
        "rules": [((60, 170), ["#8a3a5a", "#b05878", "#e890b0", "#ffd8e8"]),
                  ((0, 60), ["#0e2a20", "#163e2e", "#1e5a40", "#2a8a5a", "#5cc08a", "#a8f0c8"]),
                  ("grey", ["#0e2a20", "#1e5a40", "#5cc08a", "#a8f0c8"])],
    }),
    "R2 Oni Warlord": ("garden", {
        "from": "Brimstone Major",
        "rules": [((340, 22), ["#3a0810", "#6a1018", "#9a1a24", "#c83030", "#ec6050"]),
                  ((22, 75), GOLD[1:]),
                  ("grey", ["#3a2030", "#b05878", "#e890b0", "#ffd8e8"])],
    }),
    # --- the Honey Meadow
    "R2 Worker Bee": ("bees", {
        "from": "EH SLBY",
        "rules": [((20, 75), ["#6a3c08", "#a86410", "#e09a20", "#f8c838", "#fff09a"])],
    }),
    "R2 Hornet Drone": ("bees", {
        "from": "EH King Bee",
        "rules": [((0, 60), ["#5a1406", "#9a2a0a", "#d84c10", "#f88a20", "#ffc860"])],
    }),
    "R2 Royal Guard Bee": ("bees", {
        "from": "EH Yellow Guardian Bee",
        "rules": [((330, 25), ["#2a1040", "#4a2070", "#7038a8", "#a070e0"]),
                  ((25, 75), GOLD)],
    }),
    "R2 Hornet Queen": ("bees", {
        "from": "TH Queen Bee",
        "rules": [((40, 75), ["#6a1406", "#b02c0c", "#e85818", "#ff9a30"]),
                  ((15, 40), GOLD[1:])],
    }),
    # --- the Dragon's Roost: whelps, wyverns, kobolds and a hoard
    "R2 Drakeling": ("dragon", {
        "from": "Horned Drake",
        "rules": [((75, 170), ["#3a0c06", "#6a1608", "#a02a10", "#d04818", "#f08a3a"]),
                  ((40, 75), ["#6a4210", "#a87020", "#e0a838", "#fff0a0"])],
    }),
    "R2 Ember Wyrm": ("dragon", {
        "from": "LOD Ivory Wyvern",
        "contrast": 1.3,
        "rules": [((0, 80), ["#3a1c0c", "#6a3414", "#a05820", "#d08a38", "#f0c070", "#fff0c0"]),
                  ("grey", ["#2a1c18", "#4e3a30", "#7a6050"])],
    }),
    "R2 Magma Salamander": ("dragon", {
        "from": "Demon Tribesman",
        "rules": [((330, 15), ["#4a1806", "#7a2c0c", "#b04a14", "#e07a28", "#f8b050"]),
                  ((15, 75), ["#3a2414", "#5e4026", "#8a6440", "#b89268"])],
    }),
    "R2 Obsidian Golem": ("dragon", {
        "from": "Treasure Enemy",
        "rules": [((40, 75), GOLD), ((0, 40), ["#2a1408", "#4a2810", "#6e4020", "#9a6034"])],
    }),
    "R2 Wyrm Matriarch": ("dragon", {
        "from": "NM Red Dragon God",
        "rules": [((330, 25), ["#3a1406", "#6a2808", "#a04c10", "#d08020", "#f0b040", "#ffe08a"]),
                  ((25, 75), ["#fff0a0", "#ffffff"])],
    }),
    "R2 Elder Drake": ("dragon", {
        "from": "Black Dragon God",
        "rules": [((200, 320), ["#1e1418", "#32242a", "#4e3a42", "#6e5660", "#94788a", "#c0a4b0"]),
                  ((320, 60), EMBER), ("grey", ["#32242a", "#6e5660", "#94788a"])],
        "contrast": 1.2,
    }),
}
