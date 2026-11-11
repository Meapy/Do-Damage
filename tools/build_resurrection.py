"""Builds the boss resurrection: the Necromantic Relic, the remains Elder bosses leave, their
Empowered forms and the souls that come with them.

    python tools/build_resurrection.py [--sounds] [--preview <png>]

When an Elder dungeon's boss dies it leaves its remains, circled by escaping souls
(core/resurrection/BossResurrection.cs). The souls call out, by name, whoever nearby carries a
Necromantic Relic (an Ethereal drop of the same bosses). Used beside the remains, the relic raises
the boss again in its Empowered form: the same fight, bigger, harder, faster, with the souls' own
attacks on top and better loot.

Writes, from the game's data:
- `Resurrection8x8.png` and `Resurrection16x16.png` (client/src/kabam/rotmg/assets/custom/images,
  `resurrection8x8` and `resurrection16x16`, which tools/gen_rs_manifest.py registers): the relic,
  the soul bolt, a wisp, the ritual's soul and the Wailing Soul.
- `Resurrection.xml`, in source/Shared/resources/xml/custom and the client's custom/xmls: the relic,
  the soul bolt, the ritual's soul, the Wailing Soul, and for each boss in BOSSES its remains (its
  own art, lying down, `<Corpse/>`, with the `Souls` effect) and its Empowered form (a copy of the
  boss, a fifth bigger, with the soul bolt as projectile SOUL_BOLT and the souls around it).
- `source/Shared/resources/data/resurrection.json`: what the server needs per boss: its remains,
  its Empowered form, its name as the souls say it, the soul bolt's damage (from the boss's own
  shots) and the Empowered form's loot (the boss's own drops, from tools/wiki_data.py, at better
  chances).
- with `--sounds`, the ritual's chant and blast (source/Shared/resources/web/sfx/monster/
  necromantic_chant.mp3 and necromantic_blast.mp3), synthesised here and encoded with ffmpeg.

Type codes 0x8C00 to 0x8CFF are kept stable in tools/resurrection/types.json: characters store
items by type code, so never renumber them. Needs Pillow.
"""
import argparse
import copy
import json
import math
import os
import random
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import wave
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import rework_buff_auras  # noqa: E402
import wiki_data  # noqa: E402

XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
ASSETS = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom")
SHEET8 = os.path.join(ASSETS, "images", "Resurrection8x8.png")
SHEET16 = os.path.join(ASSETS, "images", "Resurrection16x16.png")
OUTPUTS = [os.path.join(XML_DIR, "custom", "Resurrection.xml"), os.path.join(ASSETS, "xmls", "Resurrection.xml")]
CONFIG = os.path.join(ROOT, "source", "Shared", "resources", "data", "resurrection.json")
SFX = os.path.join(ROOT, "source", "Shared", "resources", "web", "sfx", "monster")
TYPES = os.path.join(ROOT, "tools", "resurrection", "types.json")
TYPE_RANGE = (0x8C00, 0x8D00)

RELIC = "Necromantic Relic"
SOUL_BOLT = "Necromantic Soul Bolt"
RITUAL_SOUL = "Necromantic Ritual Soul"
WAILING_SOUL = "Wailing Soul"
# the projectile id the Empowered forms fire their soul bolts with (BossResurrection.SoulBolt)
SOUL_BOLT_ID = 90
RELIC_CHANCE = 0.05         # each player past RELIC_THRESHOLD of the damage, per boss
RELIC_THRESHOLD = 0.02
CORPSE_SECONDS = 75         # how long the remains wait for a relic

# Each Elder dungeon's boss (or bosses: the Tomb's three, Draconis' four dragons), by object id.
BOSSES = [
    ("Elder Dreadstump the Pirate King", "Elder Pirate Cave"),
    ("Elder Arachna the Spider Queen", "Elder Spider Den"),
    ("Elder Mixcoatl the Masked God", "Elder Jungle"),
    ("Elder Stheno the Snake Queen", "Elder Snake Pit"),
    ("Elder Septavius the Ghost God", "Elder Undead Lair"),
    ("Elder Limon the Sprite God", "Elder Sprite World"),
    ("Elder Archdemon Malphas", "Elder Abyss"),
    ("Elder Lord Ruthven", "Elder Manor"),
    ("Elder Masked Party God", "Elder Beach Zone"),
    ("Elder Davy Jones", "Elder Davy Jones' Locker"),
    ("Elder NM Green Dragon God", "Lair of Elder Draconis"),
    ("Elder NM Blue Dragon God", "Lair of Elder Draconis"),
    ("Elder NM Red Dragon God", "Lair of Elder Draconis"),
    ("Elder NM Black Dragon God", "Lair of Elder Draconis"),
    ("Elder Thessal the Mermaid Goddess", "Elder Ocean Trench"),
    ("Elder Tomb Defender", "Elder Tomb of the Ancients"),
    ("Elder Tomb Support", "Elder Tomb of the Ancients"),
    ("Elder Tomb Attacker", "Elder Tomb of the Ancients"),
    ("Elder lod Ivory Wyvern", "The Elder Ivory Wyvern"),
]

# --- art ---------------------------------------------------------------------------------------
# The relic after the design's own: a lump of dark stone, mossy on top, a soul glowing through it.
# The souls are its teal glow.
PALETTE = {
    "O": (0x34, 0x57, 0x14), "o": (0x1D, 0x32, 0x1A), "g": (0x0D, 0x42, 0x07),
    "S": (0x3D, 0x3A, 0x53), "s": (0x26, 0x24, 0x33), "P": (0x6B, 0x4F, 0x6F),
    "T": (0x14, 0x8C, 0x5E), "t": (0x2F, 0xD2, 0x9A), "W": (0x7D, 0xFF, 0xCF), "w": (0xFF, 0xFF, 0xFF),
    "d": (0x0B, 0x4A, 0x33), "K": (0x10, 0x10, 0x16),
}
SPRITES8 = [
    # 0: the Necromantic Relic
    ["..O..O..",
     ".OgOOgO.",
     ".oOSSOo.",
     "oSSPSSSo",
     "sSStTSSs",
     "sStWWtSs",
     ".sSTtSs.",
     "..ssss.."],
    # 1: the soul bolt, flying right
    ["........",
     "..d..tt.",
     ".dTttWWt",
     "dTtWWwwW",
     ".dTttWWt",
     "..d..tt.",
     "........",
     "........"],
    # 2: a wisp
    ["........",
     "...t....",
     "..tWt...",
     ".tWwWt..",
     "..tWt...",
     "...t....",
     "........",
     "........"],
]
SPRITES16 = [
    # 0: the ritual's soul, a skull of soul-fire above the remains
    ["................",
     ".....tttttt.....",
     "...ttWWWWWWtt...",
     "..tWWWWWWWWWWt..",
     "..tWWWWWWWWWWt..",
     ".tWKKKWWWWKKKWt.",
     ".tWKwKdWWdKwKWt.",
     ".tWKKKdWWdKKKWt.",
     ".tWWddWKKWddWWt.",
     "..tWWWWKKWWWWt..",
     "..tTWKWKWKWKTt..",
     "...tTKtKtKtTt...",
     "....dTtTtTtd....",
     ".....dTtTd......",
     "......dTd.......",
     ".......d........"],
    # 1: the Wailing Soul
    ["................",
     "......tttt......",
     "....ttWWWWtt....",
     "...tWWWWWWWWt...",
     "...tWKKWWKKWt...",
     "...tWKwWWKwWt...",
     "..ttWWWWWWWWtt..",
     ".tWtWWWKKWWWtWt.",
     ".tWttWWKKWWttWt.",
     "..t.tWWKKWWt.t..",
     "....tWWWWWWt....",
     "....tTWWWWTt....",
     "....tTTWWTTt....",
     "...tT.tTTt.Tt...",
     "...d..dTTd..d...",
     ".......dd......."],
]


# --- the Empowered bosses' untiered items ------------------------------------------------------
# One each, a different kind of item for each so every class finds some: built from the top tier
# of its slot (the base), its damage, healing and stats times `k` (0.9 to 1.5: the strongest give
# something up), with stats of its own on top and, for some weapons, a condition its shots put on
# what they hit. Its sprite is the base's, recoloured in the boss's colours (dark, middle, light),
# with a glint of soul-fire.
# (boss, item, base, k, extra stats {stat id: amount}, shot condition (effect, seconds) or None,
#  colours, description)
BASES = {}     # boss -> its item's base, filled from UTS below
STRENGTH = {}  # boss -> its item's k
STAT = {"hp": 0, "mp": 3, "att": 20, "def": 21, "spd": 22, "vit": 26, "wis": 27, "dex": 28}
UTS = [
    ("Elder Dreadstump the Pirate King", "Dreadstump's Drowned Cutlass", "Sword of Ascension", 1.25, {"dex": 4}, None,
     ((40, 30, 20), (150, 110, 50), (255, 220, 120)),
     "The Pirate King's blade, hauled up from the deep with him. It still drips seawater, and it still cuts."),
    ("Elder Arachna the Spider Queen", "Widowfang", "Dagger of Divine Wrath", 1.2, {"spd": 3}, ("Slowed", 1.5),
     ((30, 10, 30), (110, 40, 120), (230, 160, 255)),
     "A fang of the Spider Queen raised from the dead. Its venom slows whatever it pierces."),
    ("Elder Mixcoatl the Masked God", "Quiver of the Feathered Serpent", "Quiver of Gaia", 1.3, {"dex": 4}, None,
     ((10, 40, 20), (40, 150, 90), (200, 255, 170)),
     "Mixcoatl's own quiver, its arrows fletched with the plumes of the feathered serpent."),
    ("Elder Stheno the Snake Queen", "Gorgon's Gaze", "Bow of the Firmament", 1.35, {"spd": -4}, ("Slowed", 2),
     ((20, 40, 10), (110, 160, 40), (230, 255, 140)),
     "Strung with the hair of a gorgon twice dead. Its arrows strike like her stare: heavy, and slowing."),
    ("Elder Septavius the Ghost God", "Phylactery of the Ghost God", "Sacrilege Skull", 1.4, {"wis": 5}, None,
     ((20, 20, 40), (90, 90, 160), (210, 210, 255)),
     "Septavius kept his soul in this skull. He has no more use for it; you do."),
    ("Elder Limon the Sprite God", "Prism of the Fallen Sprite", "Prism of Mindscapes", 1.2, {"spd": 5}, None,
     ((10, 30, 40), (40, 170, 190), (200, 255, 255)),
     "The light of a sprite god, bent back from the dead through a shard of crystal."),
    ("Elder Archdemon Malphas", "Archdemon's Soulfire Staff", "Staff of Endless Energy", 1.3, {"att": 4, "hp": -40}, None,
     ((40, 0, 0), (170, 30, 20), (255, 170, 90)),
     "Malphas burned souls to light his way out of the abyss. This staff still burns them, yours a little too."),
    ("Elder Lord Ruthven", "Sanguine Wand of Ruthven", "Wand of Absolution", 1.25, {"vit": 6}, None,
     ((40, 0, 10), (150, 10, 40), (255, 120, 140)),
     "Lord Ruthven's wand, carved from a bone of his first victim. It thirsts."),
    ("Elder Masked Party God", "Masquerade Robe", "Robe of the Nile", 1.2, {"spd": 4, "dex": 3}, None,
     ((40, 10, 40), (210, 60, 160), (255, 220, 90)),
     "Worn for the last dance of a god twice dead. Nobody inside it stays still for long."),
    ("Elder Davy Jones", "Ring of the Drowned Captain", "Ring of Absolute Magic", 1.1, {"att": 4, "def": 4}, None,
     ((0, 20, 30), (20, 110, 120), (170, 255, 230)),
     "Davy Jones' signet, pulled from his finger in the locker. The sea gives back nothing for free."),
    ("Elder NM Green Dragon God", "Veridian Wyrmhide", "Sphinx Leather Armor", 1.15, {"vit": 5}, None,
     ((0, 40, 0), (30, 160, 40), (190, 255, 150)),
     "Hide of Limoz, risen and slain again. The venom in its scales keeps its wearer's wounds clean."),
    ("Elder NM Blue Dragon God", "Azure Tempest Seal", "Seal of Righteous Victory", 1.3, {"spd": 3}, None,
     ((0, 10, 50), (30, 80, 210), (170, 210, 255)),
     "Nikao's storm, sealed in sapphire. Raise it and the sky answers."),
    ("Elder NM Red Dragon God", "Wyrmfire Katana", "Ame-no-Habakiri", 1.3, {"att": 4, "def": -5}, None,
     ((50, 0, 0), (210, 50, 20), (255, 200, 80)),
     "Forged in Pyyr's own fire the second time he fell. It burns hot enough to scorch its wielder."),
    ("Elder NM Black Dragon God", "Obsidian Wyrmplate", "Armor of the Pyramid", 1.3, {"hp": 60, "spd": -3}, None,
     ((10, 10, 15), (70, 60, 90), (190, 170, 230)),
     "Plates of Feargus' obsidian scales: heavy, cold, and all but unbreakable."),
    ("Elder Thessal the Mermaid Goddess", "Orb of the Drowned Tide", "Realitylapse Orb", 1.25, {"wis": 4}, None,
     ((0, 30, 50), (30, 150, 200), (190, 250, 255)),
     "Thessal's pearl, grown dark in the depths of death. The tide inside it pulls at the world."),
    ("Elder Tomb Defender", "Bulwark of Bes", "Shield of Belligerence", 1.35, {"def": 4, "spd": -3}, None,
     ((40, 30, 0), (190, 150, 40), (255, 240, 170)),
     "Bes guarded the tomb for millennia, and then once more. His shield remembers every blow."),
    ("Elder Tomb Support", "Tome of the Night Sky", "Tome of Sacred Thought", 1.3, {"wis": 6}, None,
     ((10, 0, 40), (80, 40, 170), (220, 200, 255)),
     "Nut's book of the stars, its pages the night itself. Its light mends what the dark has broken."),
    ("Elder Tomb Attacker", "Crown of Geb", "Helm of Imperial Right", 1.25, {"vit": 5}, None,
     ((30, 20, 0), (130, 100, 40), (220, 200, 140)),
     "The earth god's crown, heavy as a mountain. Its wearer fights as if the ground itself were behind them."),
    ("Elder lod Ivory Wyvern", "Scepter of the Ivory Wyvern", "Scepter of Luminous Winds", 1.5, {"hp": 80}, None,
     ((40, 40, 40), (200, 200, 190), (255, 255, 245)),
     "Carved from the Ivory Wyvern's horn after its second death. Its lightning leaps further than any mortal's."),
]
for _u in UTS:
    BASES[_u[0]], STRENGTH[_u[0]] = _u[2], _u[3]

# each item's power (Player.RelicPowers.cs): its id, and its name and what it does for the tooltip
POWERS = {
    "Dreadstump's Drowned Cutlass": ("Broadside", "Broadside", "Every 6th hit, a cannonball bursts on the target for 3 times the hit to every enemy within 2 tiles"),
    "Widowfang": ("Venom", "Widow's Venom", "Hits build venom on an enemy; the 5th bursts it for 650 damage, ignoring defense"),
    "Quiver of the Feathered Serpent": ("SerpentsWind", "Serpent's Wind", "Using it gives +10 dexterity for 4 seconds"),
    "Gorgon's Gaze": ("StoneGaze", "Stone Gaze", "Hits on an enemy above 75% health deal half as much again, ignoring defense"),
    "Phylactery of the Ghost God": ("SoulHarvest", "Soul Harvest", "Kills store souls (5 at most); using it bursts them at the target for 90 damage each and heals you 40 each"),
    "Prism of the Fallen Sprite": ("Phase", "Phase", "Using it makes you untouchable for 1 second and speedy for 2 (every 8 seconds at most)"),
    "Archdemon's Soulfire Staff": ("Hellbrand", "Hellbrand", "Hits have a 15% chance to curse the enemy for 2 seconds: it takes more damage from everyone"),
    "Sanguine Wand of Ruthven": ("Bloodthirst", "Bloodthirst", "Heals you 4% of the damage your shots deal, up to 40 health a second"),
    "Masquerade Robe": ("Masquerade", "Masquerade", "When shot, a 20% chance to turn invisible and speedy for 2 seconds (every 10 seconds at most)"),
    "Ring of the Drowned Captain": ("DavysBargain", "Davy's Bargain", "Below a quarter of your health, heals you half your health and makes you untouchable for 1.5 seconds (once a minute)"),
    "Veridian Wyrmhide": ("ToxicScales", "Toxic Scales", "When shot, enemies within 3 tiles burn for 240 over 3 seconds (every 2 seconds at most)"),
    "Azure Tempest Seal": ("Stormcall", "Stormcall", "Using it calls lightning on up to 4 enemies near the target, 350 damage each"),
    "Wyrmfire Katana": ("Ignition", "Ignition", "Every 3rd hit sets the enemy alight, burning for half the hit over 2 seconds"),
    "Obsidian Wyrmplate": ("ObsidianShell", "Obsidian Shell", "A shell that takes the whole of the next shot, back 8 seconds after it breaks"),
    "Orb of the Drowned Tide": ("Undertow", "Undertow", "Using it slows enemies within 3.5 tiles of the target for 3 seconds (its armour shred does the rest)"),
    "Bulwark of Bes": ("GuardiansOath", "Guardian's Oath", "Using it gives everyone within 5 tiles +10 defense for 4 seconds"),
    "Tome of the Night Sky": ("Starlight", "Starlight", "Using it gives everyone within 6 tiles +8 attack for 4 seconds"),
    "Crown of Geb": ("Earthshaker", "Earthshaker", "Using it shakes the earth: 400 damage to every enemy within 4 tiles"),
    "Scepter of the Ivory Wyvern": ("WyvernsFury", "Wyvern's Fury", "Half a second after using it, a second storm strikes the 3 enemies nearest the target for 300 each"),
}
UT_CHANCE = 0.06            # each player past the threshold, before the boosts
# attributes of an ability's effects that are its strength (times k); durations grow by half as much
STRENGTH_ATTRS = ("amount", "totalDamage", "impactDamage", "heal", "damage", "healAmount", "decrDamage", "wisDamageBase")
DURATION_ATTRS = ("duration", "durationMS")
DROP_TAGS = ("Tier", "DisplayId", "Description", "Soulbound", "BagType", "FameBonus", "feedPower", "Forge", "ForgeShard")


def manifest_sets():
    m = json.load(open(os.path.join(ROOT, "client-rs", "assets", "manifest.json")))
    return {s["name"]: s for s in m["image_sets"]}


def recolour(cell, colours):
    """The base sprite in the boss's colours: each pixel's brightness mapped along dark, middle,
    light, its brightest pixel a glint of soul-fire."""
    dark, mid, light = colours
    out = cell.copy()
    px = [(x, y) for y in range(cell.height) for x in range(cell.width) if cell.getpixel((x, y))[3]]
    if not px:
        return out
    lum = {p: sum(c * w for c, w in zip(cell.getpixel(p)[:3], (0.3, 0.59, 0.11))) / 255 for p in px}
    lo, hi = min(lum.values()), max(lum.values())
    for p, l in lum.items():
        t = (l - lo) / (hi - lo) if hi > lo else 0.5
        a, b, f = (dark, mid, t * 2) if t < 0.5 else (mid, light, t * 2 - 1)
        out.putpixel(p, tuple(int(a[i] + (b[i] - a[i]) * f) for i in range(3)) + (cell.getpixel(p)[3],))
    out.putpixel(max(lum, key=lambda p: (lum[p], -p[1])), PALETTE["W"] + (255,))
    return out


def scale_item(item, k, extra, condition):
    def scaled(v, f):
        n = float(v) * f
        return str(int(round(n))) if float(v).is_integer() else str(round(n, 2))
    for p in item.iter("Projectile"):
        for tag in ("MinDamage", "MaxDamage", "Damage"):
            e = p.find(tag)
            if e is not None:
                e.text = scaled(e.text, k)
        if condition:
            el(p, "ConditionEffect", condition[0], duration=condition[1])
    for a in item.findall("Activate"):
        for attr in STRENGTH_ATTRS:
            if a.get(attr) is not None:
                a.set(attr, scaled(a.get(attr), k))
        for attr in DURATION_ATTRS:
            if a.get(attr) is not None:
                a.set(attr, scaled(a.get(attr), 1 + (k - 1) / 2))
    stats = {}
    for e in item.findall("ActivateOnEquip"):
        if e.text == "IncrementStat":
            stats[int(e.get("stat"))] = int(e.get("amount"))
            item.remove(e)
    stats = {s: int(round(v * k)) for s, v in stats.items()}
    for name, v in extra.items():
        stats[STAT[name]] = stats.get(STAT[name], 0) + v
    for s, v in stats.items():
        if v:
            el(item, "ActivateOnEquip", "IncrementStat", stat=s, amount=v)


def build_uts(objects, types, root, first_cell):
    """The Empowered bosses' items into root, their sprites as cells first_cell on; returns the
    sprites and {boss: item}."""
    sets = manifest_sets()
    cells, by_boss = [], {}
    for n, (boss, name, base_id, k, extra, condition, colours, desc) in enumerate(UTS):
        base = objects.get(base_id)
        if base is None:
            raise SystemExit("No base item %s for %s" % (base_id, name))
        assert 0.9 <= k <= 1.5, name
        tex = base.find("Texture")
        s = sets[tex.findtext("File")]
        index = int(tex.findtext("Index"), 0)
        img = Image.open(os.path.join(ROOT, "client", "src", s["file"])).convert("RGBA")
        cols = img.width // s["w"]
        x0, y0 = (index % cols) * s["w"], (index // cols) * s["h"]
        cell = img.crop((x0, y0, x0 + s["w"], y0 + s["h"]))
        if cell.size != (8, 8):
            cell = cell.resize((8, 8), Image.NEAREST)
        cells.append(recolour(cell, colours))

        item = copy.deepcopy(base)
        item.set("type", types.get(name))
        item.set("id", name)
        for tag in DROP_TAGS:
            for e in item.findall(tag):
                item.remove(e)
        tex = item.find("Texture")
        tex.find("File").text = "resurrection8x8"
        tex.find("Index").text = str(first_cell + n)
        scale_item(item, k, extra, condition)
        power, title, what = POWERS[name]
        el(item, "RelicPower", power)
        info = item.find("ExtraTooltipData")
        if info is None:
            info = el(item, "ExtraTooltipData")
        el(info, "EffectInfo", name=title, description=what)
        el(item, "Description", desc + " Dropped by the Empowered " + name_of(objects[boss]) + ".")
        el(item, "BagType", 6)
        el(item, "FameBonus", 7)
        el(item, "feedPower", 1200)
        el(item, "Soulbound")
        root.append(item)
        by_boss[boss] = name
    return cells, by_boss


def sheet(sprites, cell, extra=()):
    img = Image.new("RGBA", (cell * (len(sprites) + len(extra)), cell), (0, 0, 0, 0))
    for k, im in enumerate(extra):
        img.paste(im, ((len(sprites) + k) * cell, 0))
    for k, rows in enumerate(sprites):
        assert len(rows) == cell and all(len(r) == cell for r in rows), (k, cell)
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch != ".":
                    img.putpixel((k * cell + x, y), PALETTE[ch] + (255,))
    return img


# --- data --------------------------------------------------------------------------------------
def load_objects():
    objects = {}
    own = {os.path.abspath(p) for p in OUTPUTS}
    for dirpath, _, files in os.walk(XML_DIR):
        for f in sorted(files):
            path = os.path.join(dirpath, f)
            if not f.endswith(".xml") or os.path.abspath(path) in own:
                continue
            text = re.sub(r"<\?xml[^>]*\?>", "", open(path, encoding="utf-8-sig", errors="replace").read())
            try:
                root = ET.fromstring("<r>" + text + "</r>")
            except ET.ParseError:
                continue
            for o in root.iter("Object"):
                if o.get("id"):
                    objects[o.get("id")] = o
    return objects


class Types:
    def __init__(self):
        self.map = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, oid):
        if oid not in self.map:
            used = {int(v, 16) for v in self.map.values()}
            free = next(t for t in range(*TYPE_RANGE) if t not in used)
            self.map[oid] = "0x%04X" % free
        return self.map[oid]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w", newline="\n") as f:
            json.dump(dict(sorted(self.map.items(), key=lambda kv: int(kv[1], 16))), f, indent=1)
            f.write("\n")


def el(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, {k: str(v) for k, v in attrs.items()})
    if text is not None:
        e.text = str(text)
    return e


def name_of(boss):
    """The boss's name as players read it, without "Elder"."""
    display = boss.findtext("DisplayId") or boss.get("id")
    return re.sub(r"^Elder ", "", display)


def soul_damage(boss):
    """The soul bolt's damage: about the boss's middling shot."""
    dmg = sorted(int(p.findtext("Damage") or 0) for p in boss.iter("Projectile") if int(p.findtext("Damage") or 0) > 0)
    return max(20, int(dmg[len(dmg) // 2] * 0.9)) if dmg else 60


def souls(parent, rate, min_r, max_r, speed, rise, life, size=5, z=0.15):
    el(parent, "Effect", "Souls", color="0x7DFFCF", color2="0x148C5E", rate=rate, minRadius=min_r, maxRadius=max_r,
       speed=speed, rise=rise, life=life, lifeVariance=round(life * 0.25, 2), size=size, zOffset=z)


def build(objects, types):
    root = ET.Element("Objects")
    root.text = "\n\t"

    relic = el(root, "Object", type=types.get(RELIC), id=RELIC)
    el(relic, "Class", "Equipment")
    el(relic, "Item")
    tex = el(relic, "Texture")
    el(tex, "File", "resurrection8x8")
    el(tex, "Index", 0)
    el(relic, "SlotType", 10)
    el(relic, "Ethereal")
    el(relic, "Description", "A dark, glowing artifact pulsing with unstable soul energy. Used beside the remains of a fallen "
                             "Elder boss, still circled by its escaping souls, it raises that boss again in its Empowered "
                             "form: stronger, faster and richer in loot.")
    el(relic, "ExtraTooltipData")
    info = relic.find("ExtraTooltipData")
    el(info, "EffectInfo", name="On use", description="Raises a fallen Elder boss in its Empowered form (within 3 tiles)")
    el(relic, "Sound", "use_key")
    el(relic, "Consumable")
    el(relic, "BagType", 9)
    el(relic, "Activate", "Resurrect", radius=3)
    el(relic, "feedPower", 500)

    bolt = el(root, "Object", type=types.get(SOUL_BOLT), id=SOUL_BOLT)
    el(bolt, "Class", "Projectile")
    tex = el(bolt, "Texture")
    el(tex, "File", "resurrection8x8")
    el(tex, "Index", 1)

    ritual = el(root, "Object", type=types.get(RITUAL_SOUL), id=RITUAL_SOUL)
    el(ritual, "Class", "GameObject")
    tex = el(ritual, "Texture")
    el(tex, "File", "resurrection16x16")
    el(tex, "Index", 0)
    el(ritual, "Size", 140)
    el(ritual, "Z", 1.2)
    el(ritual, "ShadowSize", 60)
    el(ritual, "NoMiniMap")
    souls(ritual, 30, 0.2, 1.4, 0.11, 1.4, 1.4, size=6, z=0.4)

    wail = el(root, "Object", type=types.get(WAILING_SOUL), id=WAILING_SOUL)
    el(wail, "Class", "Character")
    el(wail, "Enemy")
    tex = el(wail, "Texture")
    el(tex, "File", "resurrection16x16")
    el(tex, "Index", 1)
    el(wail, "Size", 80)
    el(wail, "MaxHitPoints", 1000)
    el(wail, "Defense", 10)
    el(wail, "Flying")
    el(wail, "HitSound", "monster/ghosts_hit")
    el(wail, "DeathSound", "monster/ghosts_death")
    el(wail, "Exp", 50)
    el(wail, "StasisImmune")
    souls(wail, 8, 0.1, 0.6, 0.06, 0.6, 0.9, size=4, z=0.3)
    p = el(wail, "Projectile", id=0)
    el(p, "ObjectId", SOUL_BOLT)
    el(p, "Speed", 60)
    el(p, "Damage", 100)
    el(p, "Size", 70)
    el(p, "LifetimeMS", 1800)
    el(p, "ConditionEffect", "Slowed", duration=1.5)

    ut_cells, uts = build_uts(objects, types, root, len(SPRITES8))
    config = []
    data = wiki_data.Data()
    for boss_id, dungeon in BOSSES:
        boss = objects.get(boss_id)
        if boss is None:
            raise SystemExit("No object data for %s" % boss_id)
        name = name_of(boss)
        size = int(boss.findtext("Size") or 100)

        corpse_id = boss_id + " Remains"
        corpse = el(root, "Object", type=types.get(corpse_id), id=corpse_id)
        el(corpse, "DisplayId", "Remains of " + name)
        el(corpse, "Class", "GameObject")
        for t in ("Texture", "AnimatedTexture", "RandomTexture"):
            if boss.find(t) is not None:
                corpse.append(copy.deepcopy(boss.find(t)))
        el(corpse, "Size", size)
        el(corpse, "Corpse")
        el(corpse, "NoMiniMap")
        el(corpse, "ShadowSize", 0)
        el(corpse, "Sound", "monster/necromantic_chant", id=0)
        el(corpse, "Sound", "monster/necromantic_blast", id=1)
        souls(corpse, 12, 0.5 * size / 100, 1.6 * size / 100 + 0.6, 0.035, 0.35, 2.6)

        emp_id = "Empowered " + boss_id
        emp = copy.deepcopy(boss)
        emp.set("type", types.get(emp_id))
        emp.set("id", emp_id)
        disp = emp.find("DisplayId")
        if disp is None:
            disp = ET.Element("DisplayId")
            emp.insert(0, disp)
        disp.text = "Empowered " + name
        se = emp.find("Size")
        if se is None:
            se = el(emp, "Size", size)
        se.text = str(round(size * 1.2))
        for e in emp.findall("Effect"):
            emp.remove(e)
        souls(emp, 10, 0.4 * size / 100, 1.2 * size / 100 + 0.5, 0.05, 0.6, 1.8, size=5, z=0.3)
        for t in ("StasisImmune", "ParalyzeImmune", "StunImmune", "DazedImmune"):
            if emp.find(t) is None:
                el(emp, t)
        dmg = soul_damage(boss)
        p = el(emp, "Projectile", id=SOUL_BOLT_ID)
        el(p, "ObjectId", SOUL_BOLT)
        el(p, "Speed", 55)
        el(p, "Damage", dmg)
        el(p, "Size", 80)
        el(p, "LifetimeMS", 2600)
        el(p, "MultiHit")
        p = el(emp, "Projectile", id=SOUL_BOLT_ID + 1)
        el(p, "ObjectId", SOUL_BOLT)
        el(p, "Speed", 90)
        el(p, "Damage", round(dmg * 0.7))
        el(p, "Size", 65)
        el(p, "LifetimeMS", 1600)
        el(p, "ConditionEffect", "Slowed", duration=2)
        root.append(emp)

        # the boss's own item drops, rolled again by the Empowered form: twice as likely (ten points
        # more at most), and a Potion of Max at four times its chance (a tenth at least, a quarter
        # at most)
        loot = []
        for kind, item, chance in data.drops(boss_id):
            if kind != "item" or not chance or item == RELIC or any(item == i for i, _ in loot):
                continue
            if item.startswith("Potion of Max "):
                loot.append((item, round(min(0.25, max(0.10, chance * 4)), 4)))
            else:
                loot.append((item, round(min(chance * 2, chance + 0.1, 1.0), 4)))
        loot.append((uts[boss_id], UT_CHANCE))
        config.append({"boss": boss_id, "dungeon": dungeon, "name": name, "corpse": corpse_id, "empowered": emp_id,
                       "soulDamage": dmg, "loot": [[i, c] for i, c in loot],
                       # for the wiki: its item, the top-tier item it's built on, and how much stronger
                       "item": uts[boss_id], "itemBase": BASES[boss_id], "itemStrength": STRENGTH[boss_id]})

    ET.indent(root, "\t")
    xml = '<?xml version="1.0" encoding="utf-8"?>\n<!-- Generated by tools/build_resurrection.py: do not edit. -->\n' + ET.tostring(root, encoding="unicode") + "\n"
    cfg = {"relic": RELIC, "relicChance": RELIC_CHANCE, "relicThreshold": RELIC_THRESHOLD, "corpseSeconds": CORPSE_SECONDS,
           "ritualSoul": RITUAL_SOUL, "wailingSoul": WAILING_SOUL, "soulBolt": SOUL_BOLT_ID, "bosses": config}
    return xml, cfg, ut_cells


# --- sound -------------------------------------------------------------------------------------
RATE = 22050


def chant():
    """Three seconds of low voices: a few detuned, breathy "oh"s swelling under a drone."""
    n = int(RATE * 3.0)
    rnd = random.Random(7)
    voices = [(55.0, 0.0), (55.4, 1.1), (82.4, 2.3), (110.2, 0.7), (65.4, 1.9)]
    # an "oh" formant: peaks near 400 and 800 Hz, by additive harmonics weighted near them
    out = []
    noise = 0.0
    for i in range(n):
        t = i / RATE
        env = min(1.0, t / 1.6) * (1.0 if t < 2.6 else max(0.0, (3.0 - t) / 0.4))
        trem = 0.75 + 0.25 * math.sin(2 * math.pi * 3.1 * t)
        s = 0.0
        for f0, ph in voices:
            f = f0 * (1 + 0.004 * math.sin(2 * math.pi * 4.7 * t + ph))
            for h in range(1, 18):
                fh = f * h
                w = math.exp(-((fh - 420) / 160) ** 2) + 0.6 * math.exp(-((fh - 820) / 220) ** 2) + 0.25 / h
                s += w * math.sin(2 * math.pi * fh * t + ph * h)
        noise = 0.97 * noise + 0.03 * (rnd.random() * 2 - 1)
        out.append((s / 9.0 + noise * 0.6) * env * trem)
    return out


def blast():
    """A dark pulse: a falling sub boom, a crack of noise and a shimmering tail."""
    n = int(RATE * 2.2)
    rnd = random.Random(11)
    out = []
    phase = 0.0
    lp = 0.0
    for i in range(n):
        t = i / RATE
        f = 30 + 110 * math.exp(-t * 6)
        phase += 2 * math.pi * f / RATE
        boom = math.sin(phase) * math.exp(-t * 2.2)
        lp = 0.8 * lp + 0.2 * (rnd.random() * 2 - 1)
        crack = lp * math.exp(-t * 9) * 1.4
        shimmer = sum(math.sin(2 * math.pi * fr * t) for fr in (660, 990, 1320)) / 3 * 0.18 * math.exp(-t * 2.5) * min(1, t * 8)
        out.append(boom + crack + shimmer)
    return out


def write_sound(samples, path):
    peak = max(abs(s) for s in samples) or 1
    tmp = tempfile.mkdtemp()
    try:
        wav = os.path.join(tmp, "s.wav")
        with wave.open(wav, "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes(b"".join(struct.pack("<h", int(s / peak * 0.89 * 32767)) for s in samples))
        ffmpeg = shutil.which("ffmpeg")
        if not ffmpeg:
            raise SystemExit("--sounds needs ffmpeg on the PATH")
        subprocess.run([ffmpeg, "-loglevel", "error", "-y", "-i", wav, "-ac", "1", "-ar", "44100", "-b:a", "96k", path], check=True)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--sounds", action="store_true", help="also write the ritual's sounds (needs ffmpeg)")
    ap.add_argument("--preview", help="write the sprites, eight times larger, here")
    args = ap.parse_args()

    types = Types()
    xml, cfg, ut_cells = build(load_objects(), types)
    s8, s16 = sheet(SPRITES8, 8, ut_cells), sheet(SPRITES16, 16)
    s8.save(SHEET8)
    s16.save(SHEET16)
    if args.preview:
        prev = Image.new("RGBA", (max(s8.width * 8, s16.width * 8), 8 * 8 + 16 * 8 + 8), (60, 60, 70, 255))
        prev.alpha_composite(s8.resize((s8.width * 8, 64), Image.NEAREST), (0, 0))
        prev.alpha_composite(s16.resize((s16.width * 8, 128), Image.NEAREST), (0, 72))
        prev.save(args.preview)

    for path in OUTPUTS:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(xml)
        # the party buffs reach further (tools/rework_buff_auras.py)
        rework_buff_auras.rework_file(path)
    with open(CONFIG, "w", encoding="utf-8", newline="\n") as f:
        json.dump(cfg, f, indent=1)
        f.write("\n")
    types.save()
    if args.sounds:
        write_sound(chant(), os.path.join(SFX, "necromantic_chant.mp3"))
        write_sound(blast(), os.path.join(SFX, "necromantic_blast.mp3"))
    print("resurrection: %d bosses, %d types" % (len(cfg["bosses"]), len(types.map)))


if __name__ == "__main__":
    main()
