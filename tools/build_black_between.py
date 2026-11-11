"""Builds the Black Between, the Mysterious One's dungeon: the Mysterious Voice of Realm 2 at last
in person, who tries those who silenced a realm against his daughter, his son and then himself.
Meant to be harder than Oryx 5, for players in full Enthroned gear.

    python tools/build_black_between.py [--preview <folder>]

The map, from south to north:

- the Constellation Approach: a causeway of astral stone four tiles wide across the void, where
  players arrive (Spawn region), with Starbound Sentinels drifting over it (harmless, as Oryx 5's
  clouds: their stars do no damage and nothing hits them). No portal out: the Mysterious One drops
  one when he falls;
- the Observatory: a disc of astral stone floating in the void (PLATFORM_R tiles in radius) with a
  rim of dressed stone banded in gold and star pylons round it. The Observatory Heart, invisible,
  stands at its centre: the fights order it to raise the walls and turn the floor, so they come
  and go with its states (BehaviorDb.BlackBetween.cs). The Rift of the Between is on the north
  rim, and the Mysterious One (his herald, at first) beside it.

Every fight happens on the platform. The void is empty sky: no ground at all, so the client draws
its starry void sky there (`<Background>8</Background>` in Dungeons.xml, the Rust client's
voidsky.rs) and hangs rock under the platform's and the causeway's edges (islands.rs, with the
`BB Underside` grounds), as under the Nexus islands. The Black Between ground is that void come up
through the floor, which can be walked on but burns.

Outputs (generated, don't edit):
- the map, source/Shared/resources/worlds/Dungeons/The Black Between/The Black Between.jm;
- BlackBetweenGrounds.xml and BlackBetweenObjects.xml, in source/Shared/resources/xml/custom and
  the client's custom/xmls;
- the sheets BlackBetween8x8.png, BlackBetween16x16.png, BlackBetween32x32.png,
  BlackBetweenChars8x8.png and BlackBetweenChars32x32.png in the client's custom/images
  (registered for the Rust client by tools/gen_rs_manifest.py).
The art is drawn by tools/black_between/bosses.py (the three bosses and their forms, 32x32) and
small.py (everything else). Type codes are kept stable in tools/black_between/types.json.
Needs Pillow.
"""
import base64
import json
import math
import os
import re
import sys
import xml.etree.ElementTree as ET
import zlib

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from black_between import bosses, small  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "The Black Between",
                       "The Black Between.jm")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "BlackBetweenGrounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "BlackBetweenGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "BlackBetweenObjects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "BlackBetweenObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "black_between", "types.json")
GROUND_TYPES = (0x8D00, 0x8D20)
OBJECT_TYPES = (0x8D20, 0x8E00)
SHEET8 = ("blackBetween8x8", "BlackBetween8x8.png")
SHEET16 = ("blackBetween16x16", "BlackBetween16x16.png")
SHEET32 = ("blackBetween32x32", "BlackBetween32x32.png")
CHARS8 = ("blackBetweenChars8x8", "BlackBetweenChars8x8.png")
CHARS32 = ("blackBetweenChars32x32", "BlackBetweenChars32x32.png")
VOID = None         # the void: no ground at all (the client's sky shows there)


# ------------------------------------------------------------------------------------------------
# types and sheets

class Types:
    """Type codes kept stable between runs."""

    def __init__(self):
        self.types = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, key, span):
        if key not in self.types:
            used = {int(v, 16) for v in self.types.values()}
            self.types[key] = "0x%04X" % next(t for t in range(*span) if t not in used)
        return self.types[key]

    def save(self):
        os.makedirs(os.path.dirname(TYPES), exist_ok=True)
        with open(TYPES, "w", newline="\n") as f:
            json.dump(self.types, f, indent=1, sort_keys=True)
            f.write("\n")


class Grid:
    """A sheet of equal cells, filled in order."""

    def __init__(self, cell, cols):
        self.cell, self.cols, self.cells = cell, cols, []

    def add(self, img):
        assert img.size[0] <= self.cell and img.size[1] <= self.cell, img.size
        self.cells.append(img)
        return len(self.cells) - 1

    def write(self, path):
        rows = max(1, (len(self.cells) + self.cols - 1) // self.cols)
        sheet = Image.new("RGBA", (self.cols * self.cell, rows * self.cell), (0, 0, 0, 0))
        for i, img in enumerate(self.cells):
            ox = (i % self.cols) * self.cell + (self.cell - img.size[0]) // 2
            oy = (i // self.cols) * self.cell + (self.cell - img.size[1])
            sheet.paste(img, (ox, oy))
        sheet.save(path)


class Chars:
    """A character sheet: one row of seven frames each (stand, walk 1, walk 2, unused, attack 1,
    attack 2, the attack's second cell)."""

    def __init__(self, size):
        self.size, self.rows = size, []

    def add(self, frames, attack):
        """frames: the idle frames (one to three, cycled through the stand and walk cells)."""
        f = list(frames) + [frames[0]] * (3 - len(frames))
        self.rows.append([f[0], f[1], f[2], f[0], f[1], attack])
        return len(self.rows) - 1

    def write(self, path):
        s = self.size
        sheet = Image.new("RGBA", (s * 7, s * max(1, len(self.rows))), (0, 0, 0, 0))
        for r, frames in enumerate(self.rows):
            for i, img in enumerate(frames):
                sheet.paste(img, (i * s, r * s))
        sheet.save(path)


def sub(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, {k.rstrip("_"): str(v) for k, v in attrs.items()})
    if text is not None:
        e.text = str(text)
    return e


def texture(parent, file, index, tag="Texture"):
    t = sub(parent, tag)
    sub(t, "File", file)
    sub(t, "Index", index if tag == "AnimatedTexture" else "0x%02x" % index)
    return t


def shot(pid, obj, damage, speed, life, size=100, effects=(), multi=False, amplitude=None, frequency=None, piercing=False):
    p = ET.Element("Projectile", {"id": str(pid)})
    sub(p, "ObjectId", obj)
    sub(p, "Damage", damage)
    sub(p, "Speed", speed)
    sub(p, "LifetimeMS", life)
    sub(p, "Size", size)
    if amplitude is not None:
        sub(p, "Amplitude", amplitude)
        sub(p, "Frequency", frequency)
    if multi:
        sub(p, "MultiHit")
    if piercing:
        sub(p, "ArmorPiercing")
    for effect, duration in effects:
        sub(p, "ConditionEffect", effect, duration=duration)
    return p


def foe(pid, obj, damage, speed, life, size=100, **kw):
    """An enemy's shot: it goes on through whoever it hits (MultiHit), so a group can't stand a
    couple of players in front to take the hits for everyone else: each player has to dodge it."""
    return shot(pid, obj, damage, speed, life, size, multi=True, **kw)


# ------------------------------------------------------------------------------------------------
# grounds: name -> (variants, minimap colour, extras)

GROUNDS = {
    "Astral Stone": ("0x2A2442", {}),
    "Platform Rim": ("0x4A4070", {}),
    # Vesper's ward on the floor: burns, the Veil's damage per hit
    "Veil of Dusk": ("0x5A1A90", {"MinDamage": 140, "MaxDamage": 170}),
    # the void come up through the floor: walkable, but it burns
    "Black Between": ("0x08051A", {"MinDamage": 160, "MaxDamage": 190}),
    "Constellation Path": ("0x8A80D0", {}),
    # never on the map: the rock the client hangs under the platform's edges, and its band under
    # the floor (islands.rs)
    "BB Underside Rock": ("0x1A1430", {}),
    "BB Underside Rim": ("0x2E2648", {}),
    # where the Mysterious One steps into Oryx's Elysium (harmless)
    "Void Scar": ("0x1A1036", {}),
}


def grounds(types, sheet):
    out = []
    for name, (colour, extra) in GROUNDS.items():
        e = ET.Element("Ground", {"type": types.get(name, GROUND_TYPES), "id": name})
        rt = sub(e, "RandomTexture")
        for img in small.TILES[name]:
            texture(rt, SHEET8[0], sheet.add(img))
        for k, v in extra.items():
            sub(e, k, v)
        sub(e, "Color", colour)
        out.append(e)
    return out


# ------------------------------------------------------------------------------------------------
# objects

# The bosses and their forms (32x32, BehaviorDb.BlackBetween.cs): name -> (art, size, health,
# defence, flags, alt textures (art names), [projectiles]); the projectiles' order is the
# behaviours'. Health and defence here are the base: BossStats sets them in the fight.
# shots that weave as they fly (Vesper's aimed bolts, as the Void Entity's), and rings that bow
# (half a wave over their flight): (Amplitude, Frequency)
WAVE = (1.0, 1.5)
ARC = (1.2, 0.5)

BOSSES = {
    # the father, who comes out of the Rift: his herald does the talking at first and watches the
    # first two trials from the Rift; the Mysterious One himself comes down for the third
    "Mysterious One Herald": ("The Mysterious One", 100, 420000, 110, "herald", [], []),
    # in Oryx's Elysium when Oryx 5 falls: he steps out of his Rift and invites them in
    "Mysterious One Visitant": ("The Mysterious One", 100, 420000, 110, "herald", [], []),
    "The Mysterious One": ("The Mysterious One", 100, 504000, 110, "boss", [], [
        foe(0, "Dusk Bolt", 200, 90, 2400, amplitude=WAVE[0], frequency=WAVE[1]),
        foe(1, "Umbral Orb", 170, 55, 4000, 110, effects=[("Darkness", 2)], amplitude=ARC[0], frequency=ARC[1]),
        foe(2, "Dawn Crescent", 230, 70, 2600, 130, effects=[("Armor Broken", 2)]),
        foe(3, "Sunspark", 180, 80, 2200, effects=[("Blind", 1)]),
        foe(4, "Eclipse Orb", 210, 40, 6000, 120, effects=[("Darkness", 2), ("Armor Broken", 2)]),
        foe(5, "Eclipse Orb", 190, 45, 5500, 110, effects=[("Darkness", 1.5)]),
        foe(6, "Dawn Sword", 400, 160, 2400, 200),
    ]),
    "Vesper, the Dusk Star": ("Vesper, the Dusk Star", 85, 288000, 80, "boss", [], [
        foe(0, "Dusk Bolt", 190, 95, 2300, effects=[("Silenced", 1.5)], amplitude=WAVE[0], frequency=WAVE[1]),
        # her rings darken and slow (the slow as her Gravity Well's)
        foe(1, "Umbral Orb", 160, 50, 4200, 110, effects=[("Darkness", 1.5), ("Slowed", 1.5)], amplitude=ARC[0], frequency=ARC[1]),
    ]),
    # her copies' bolts in the Veil: light, but through any armour
    "Vesper's Veil Copy": ("Vesper's Veil Copy", 85, 100000, 0, "helper", [], [
        foe(0, "Dusk Bolt", 50, 80, 2200, amplitude=WAVE[0], frequency=WAVE[1], piercing=True),
    ]),
    "Spectral Vesper": ("Spectral Vesper", 85, 100000, 0, "helper", [], [
        foe(0, "Dusk Bolt", 180, 80, 2200, amplitude=WAVE[0], frequency=WAVE[1]),
    ]),
    "Aurel, the Dawn Blade": ("Aurel, the Dawn Blade", 85, 336000, 100, "boss",
                              ["Aurel, Solar Cyclone", "Aurel, Gauntlet Form"], [
        foe(0, "Dawn Crescent", 230, 75, 2600, 130, effects=[("Armor Broken", 2)]),
        foe(1, "Sunspark", 180, 85, 2400, effects=[("Blind", 1)]),
        foe(2, "Sunspark", 170, 65, 2200),
        foe(3, "Dawn Crescent", 220, 60, 2400, 120),
        # the Sunwheel's spiral, and his sword flying across the arena (the Sword Lanes)
        foe(4, "Dawn Crescent", 200, 80, 1800, 110),
        foe(5, "Dawn Sword", 400, 160, 2400, 200),
        # in his sister's manner: a fan of sunsparks that weave as they fly, and a ring of crescents
        # that bow as they spread and break armour
        foe(6, "Sunspark", 180, 95, 2300, amplitude=WAVE[0], frequency=WAVE[1]),
        foe(7, "Dawn Crescent", 190, 50, 4200, 120, effects=[("Armor Broken", 2)],
             amplitude=ARC[0], frequency=ARC[1]),
    ]),
    # the echo of his leap: him to the life, landing on someone else while he lands on you (which
    # is which changes each time)
    "Aurel's Echo": ("Aurel, the Dawn Blade", 85, 100000, 100, "helper", [], [
        foe(0, "Dawn Crescent", 230, 75, 2600, 130, effects=[("Armor Broken", 2)]),
        foe(1, "Sunspark", 180, 85, 2400, effects=[("Blind", 1)]),
        foe(2, "Sunspark", 180, 95, 2300, amplitude=WAVE[0], frequency=WAVE[1]),
    ]),
    # the second tornado of his Solar Cyclone, crossing each dash of his at right angles
    "Aurel's Twin Cyclone": ("Aurel, Solar Cyclone", 85, 100000, 0, "helper", [], [
        foe(0, "Sunspark", 170, 65, 2200),
    ]),
    "Spectral Aurel": ("Spectral Aurel", 85, 100000, 0, "helper", ["Spectral Cyclone"], [
        foe(0, "Sunspark", 170, 65, 2200),
    ]),
}
# like Oryx 3 to 5, none of them can be stunned, paralysed, petrified, slowed or dazed
# who sounds like whom when hit and when they fall (web/sfx/monster/<voice>_hit.mp3, _death.mp3)
FAMILY_SOUND = {
    "Vesper, the Dusk Star": "vesper", "Spectral Vesper": "vesper",
    "Aurel, the Dawn Blade": "aurel", "Aurel's Echo": "aurel", "Spectral Aurel": "aurel",
    "The Mysterious One": "mysterious", "Mysterious One Herald": "mysterious", "Mysterious One Visitant": "mysterious",
}
IMMUNE = {"The Mysterious One", "Mysterious One Herald", "Mysterious One Visitant", "Vesper, the Dusk Star", "Aurel, the Dawn Blade"}
DISPLAY = {"Mysterious One Herald": "The Mysterious One", "Mysterious One Visitant": "The Mysterious One", "Aurel's Twin Cyclone": "Aurel, the Dawn Blade",
           "Aurel's Echo": "Aurel, the Dawn Blade"}

# minions (8x8): name -> (health, defence, size, [projectiles])
MINIONS = {}
# the Starbound Sentinels over the causeway: scenery that drifts (not enemies, and they don't
# shoot), as Oryx 5's clouds drift over his Landing
SENTINEL_SHOTS = []

# invisible helpers: name -> [projectiles]; not enemies, nothing hits them
HELPERS = {
    # the centre of the arena, which raises the walls and turns the floor for the fights
    "Observatory Heart": [],
    # the points of Vesper's Gravity Well, round the platform's edge, firing in at its middle
    "Gravity Point": [foe(0, "Umbral Orb", 150, 45, 3400, 110, effects=[("Slowed", 1)])],
}


def projectile(types, sheet, name, img, file=SHEET8[0]):
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", "Projectile")
    texture(e, file, sheet.add(img))
    return e


# name -> (slot type, MP cost, cooldown s, [(stat, amount)], relic power, description, [activates],
# [projectiles], extra XML tags)
STAT = {"HP": 0, "MP": 3, "ATT": 20, "DEF": 21, "SPD": 22, "DEX": 28, "VIT": 26, "WIS": 27}
# a number and its unit stay on one line in the game's tooltips
def _nb(text):
    return re.sub(r"(\d) (s|tiles)\b", "\\1\u00a0\\2", text)


ITEM_SPECS = {
    "Quiver of the Solar Cyclone": (15, 100, 5, [("ATT", 6), ("DEX", 4)], "SolarCyclone",
        _nb("Aurel's Solar Cyclone, bound in a quiver. Its arrow is a whirling tornado that throws sunsparks "
            "to either side as it flies; where it strikes an enemy or comes down, it bursts for 2,000 to "
            "everything round it and throws eight sunsparks of 300 each."),
        [("Shoot", {})],
        [shot(0, "Cyclone Arrow", 340, 90, 1600, 120), shot(1, "Sunspark", 300, 90, 900)], {}),
    "Trinity Trap": (20, 110, 0, [("ATT", 6), ("HP", 25), ("MP", 25)], "TrinityTrap",
        _nb("Thrown, it opens three times where it lands, each marked on the ground as it comes: Aurel's "
            "lightning (300, paralysed for 2 s), Vesper's veil (300, slowed for 4 s) and their father's rift "
            "(200, and 450 more as it bleeds them over 3 s)."),
        [], [], {}),
    "Helm of the Dawn Blade": (16, 90, 3, [("HP", 60), ("MP", 60), ("DEF", 7), ("ATT", 7)], "DawnHelm",
        _nb("Berserk for everyone near you and speed for you, as the finest helms give. Aurel's three swords "
            "rise from its crest and fly where you aim, 1,200 each, and every one that strikes heals you for "
            "6% of the damage it does."),
        [("ConditionEffectAura", {"effect": "Berserk", "duration": 6.5, "range": 7.5}),
         ("ConditionEffectSelf", {"effect": "Speedy", "duration": 6.5}), ("Shoot", {})],
        [shot(0, "Dawn Sword", 1200, 140, 900, 120, multi=True)],
        {"NumProjectiles": 3, "ArcGap": 14}),
    "Prism of the Dusk Veil": (22, 85, 0, [("WIS", 8), ("HP", 60), ("MP", 60)], "DuskVeil",
        _nb("Vesper's veil, cut into a prism. Every decoy it leaves is one of her copies: while it lasts it "
            "casts her three weaving dusk bolts at the nearest enemy, 320 each, once a second."),
        [("Decoy", {"duration": 11, "speed": 1.3, "distance": 9}), ("Teleport", {"maxDistance": 13})],
        [shot(0, "Dusk Bolt", 320, 95, 1600, amplitude=WAVE[0], frequency=WAVE[1])], {}),
    "Cloak of the Planar Rift": (13, 100, 1.5, [("SPD", 7), ("DEX", 7)], "PlanarRift",
        _nb("A step through the Between of up to 13 tiles, unseen for a while after. Where you land, the "
            "ground bursts for 400 as when Aurel lands, ten sunsparks fly out (250 each, through any armour) "
            "and you are armored for 2 s."),
        [("ConditionEffectSelf", {"effect": "Invisible", "duration": 5.5}), ("Teleport", {"maxDistance": 13})],
        [shot(0, "Sunspark", 250, 110, 700)], {}),
    "Vial of Cascading Dusk": (18, 90, 0, [("ATT", 3), ("WIS", 3)], "CascadeDusk",
        _nb("Thrown, it splashes where it lands for 200, and 1,500 more over 6 s. The dusk spreads: four "
            "splashes round it for half that, then four more beyond for half again, each a shade darker."),
        [], [], {}),
}


def gear_item(types, sheet, name, spec):
    slot, mp, cooldown, stats, power, text, activates, shots, extra = spec
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", "Equipment")
    sub(e, "Item")
    texture(e, SHEET8[0], sheet.add(small.item_icon(name)))
    sub(e, "SlotType", slot)
    sub(e, "Description", text)
    for stat, amount in stats:
        sub(e, "ActivateOnEquip", "IncrementStat", stat=STAT[stat], amount=amount)
    sub(e, "Usable")
    sub(e, "MpCost", mp)
    if cooldown:
        sub(e, "Cooldown", cooldown)
    for effect, attrs in activates:
        sub(e, "Activate", effect, **attrs)
    for k, v in extra.items():
        sub(e, k, v)
    for p in shots:
        e.append(p)
    # the cloak's sunsparks pass through armour
    if name == "Cloak of the Planar Rift":
        sub(e.find("Projectile"), "ArmorPiercing")
    sub(e, "RelicPower", power)
    sub(e, "BagType", 6)
    sub(e, "FameBonus", 5)
    return e


def objects(types, s8, s16, s32, c8, c32):
    out = []
    # shots
    for name, fn in small.SHOTS.items():
        out.append(projectile(types, s8, name, fn()))
    out.append(projectile(types, s16, "Dawn Sword", bosses.dawn_sword_shot(), SHEET16[0]))

    # the bosses' art, a row each
    sprites = bosses.all_sprites()
    sprites["Spectral Vesper"] = [bosses.spectral(f) for f in sprites["Vesper, the Dusk Star"]]
    sprites["Spectral Aurel"] = [bosses.spectral(f, (255, 222, 120)) for f in sprites["Aurel, the Dawn Blade"]]
    sprites["Spectral Cyclone"] = [bosses.spectral(f, (255, 222, 120)) for f in sprites["Aurel, Solar Cyclone"]]
    rows = {}

    def row(art):
        if art not in rows:
            fr = sprites[art]
            if art == "Vesper's Veil Copy":
                rows[art] = c32.add(fr, fr[0])
            elif art in ("Aurel, Solar Cyclone", "Spectral Cyclone"):
                rows[art] = c32.add(fr[:3], fr[3])
            elif art == "Aurel, Gauntlet Form":
                rows[art] = c32.add([fr[0]], fr[1])
            else:
                rows[art] = c32.add(fr[:3], fr[3])
        return rows[art]

    for name, (art, size, hp, defense, kind, alts, shots) in BOSSES.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        if name in DISPLAY:
            sub(e, "DisplayId", DISPLAY[name])
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, CHARS32[0], row(art), "AnimatedTexture")
        for k, alt in enumerate(alts, start=1):
            holder = sub(e, "AltTexture", id=k)
            texture(holder, CHARS32[0], row(alt), "AnimatedTexture")
        # the family's own sounds (black_between/sound.py), the rest the ghost god's
        voice = FAMILY_SOUND.get(name)
        sub(e, "HitSound", "monster/%s_hit" % voice if voice else "monster/ghost_god_hit")
        sub(e, "DeathSound", "monster/%s_death" % voice if voice else "monster/ghost_god_death")
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", hp)
        sub(e, "Defense", defense)
        if kind == "boss":
            sub(e, "Quest")
            sub(e, "Level", 20)
            sub(e, "God")
            sub(e, "ShowName")
        if kind in ("boss", "herald"):
            sub(e, "NoArticle")
        sub(e, "StasisImmune")
        if name in IMMUNE:
            for flag in ("StunImmune", "ParalyzeImmune", "PetrifyImmune", "SlowedImmune", "DazedImmune"):
                sub(e, flag)
        if kind != "boss":
            sub(e, "XpMult", 0)
        for p in shots:
            e.append(p)
        out.append(e)

    # minions
    art = small.minions()
    for name, (hp, defense, size, shots) in MINIONS.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, CHARS8[0], c8.add([art[name][0]], art[name][1]), "AnimatedTexture")
        sub(e, "HitSound", "monster/ghost_god_hit")
        sub(e, "DeathSound", "monster/ghost_god_death")
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", hp)
        sub(e, "Defense", defense)
        for p in shots:
            e.append(p)
        out.append(e)

    # the Starbound Sentinels: drifting scenery
    sentinel = small.minions()["Starbound Sentinel"]
    e = ET.Element("Object", {"type": types.get("Starbound Sentinel", OBJECT_TYPES), "id": "Starbound Sentinel"})
    sub(e, "Class", "GameObject")
    first = s8.add(sentinel[0])
    texture(e, SHEET8[0], first)
    anim = sub(e, "Animation", prob="1.0")
    for idx in (first, s8.add(sentinel[1])):
        texture(sub(anim, "Frame", time="0.4"), SHEET8[0], idx)
    sub(e, "Size", 120)
    sub(e, "NoMiniMap")
    for p in SENTINEL_SHOTS:
        e.append(p)
    out.append(e)

    # star pylons round the rim, a crystal of dusk or dawn glowing at each tip
    for name, frames in (("Dusk Pylon", (0, 2)), ("Dawn Pylon", (1, 3))):
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "GameObject")
        first = s8.add(small.pylon(frames[0]))
        texture(e, SHEET8[0], first)
        anim = sub(e, "Animation", prob="1.0")
        for idx in (first, s8.add(small.pylon(frames[1]))):
            texture(sub(anim, "Frame", time="0.5"), SHEET8[0], idx)
        sub(e, "Size", 150)
        sub(e, "Static")
        sub(e, "OccupySquare")
        sub(e, "EnemyOccupySquare")
        out.append(e)

    # Vesper's dusk mine: invulnerable, bursts and is gone
    e = ET.Element("Object", {"type": types.get("Dusk Mine", OBJECT_TYPES), "id": "Dusk Mine"})
    sub(e, "Class", "Character")
    sub(e, "Enemy")
    first = s8.add(small.dusk_mine(0))
    texture(e, SHEET8[0], first)
    anim = sub(e, "Animation", prob="1.0")
    for i, idx in enumerate((first, s8.add(small.dusk_mine(1)))):
        texture(sub(anim, "Frame", time="0.2"), SHEET8[0], idx)
    sub(e, "Size", 110)
    sub(e, "MaxHitPoints", 1000)
    sub(e, "Defense", 0)
    sub(e, "StasisImmune")
    sub(e, "XpMult", 0)
    e.append(foe(0, "Dusk Bolt", 170, 70, 2000))
    out.append(e)

    # walls of light: Vesper's veil and Aurel's dawnlight (16x16, shimmering), blocking
    for name, kind in (("Veil Wall", "veil"), ("Dawnlight Wall", "dawn")):
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "GameObject")
        first = s16.add(small.wall_of_light(kind, 0))
        texture(e, SHEET16[0], first)
        anim = sub(e, "Animation", prob="1.0")
        for idx in (first, s16.add(small.wall_of_light(kind, 1)), s16.add(small.wall_of_light(kind, 2))):
            texture(sub(anim, "Frame", time="0.15"), SHEET16[0], idx)
        sub(e, "Size", 100)
        sub(e, "ShadowSize", 0)
        sub(e, "Static")
        sub(e, "OccupySquare")
        sub(e, "FullOccupy")
        sub(e, "EnemyOccupySquare")
        out.append(e)

    # the Rift of the Between, on the north rim: a galactic portal, turning
    e = ET.Element("Object", {"type": types.get("Rift of the Between", OBJECT_TYPES), "id": "Rift of the Between"})
    sub(e, "Class", "GameObject")
    first = s16.add(small.rift(0))
    texture(e, SHEET16[0], first)
    anim = sub(e, "Animation", prob="1.0")
    rift_cells = [first] + [s16.add(small.rift(f)) for f in (1, 2, 3)]
    for idx in rift_cells:
        texture(sub(anim, "Frame", time="0.12"), SHEET16[0], idx)
    sub(e, "Size", 220)
    sub(e, "ShadowSize", 0)
    sub(e, "Static")
    out.append(e)

    # the Rift open, in Oryx's Elysium when Oryx 5 falls (BehaviorDb.Elysium.cs): the way in
    e = ET.Element("Object", {"type": types.get("Rift of the Between (Open)", OBJECT_TYPES), "id": "Rift of the Between (Open)"})
    sub(e, "DisplayId", "The Black Between")
    sub(e, "Class", "Portal")
    sub(e, "IntergamePortal")
    sub(e, "DungeonName", "The Black Between")
    texture(e, SHEET16[0], rift_cells[0])
    anim = sub(e, "Animation", prob="1.0")
    for idx in rift_cells:
        texture(sub(anim, "Frame", time="0.12"), SHEET16[0], idx)
    sub(e, "Size", 220)
    sub(e, "ShadowSize", 0)
    out.append(e)

    # the Cosmic Anvil, which he leaves where he fades: corrupts Enthroned gear (CorruptHandler.cs)
    e = ET.Element("Object", {"type": types.get("Cosmic Anvil", OBJECT_TYPES), "id": "Cosmic Anvil"})
    sub(e, "Class", "CosmicAnvil")
    first = s16.add(small.cosmic_anvil(0))
    texture(e, SHEET16[0], first)
    anim = sub(e, "Animation", prob="1.0")
    for idx in (first, s16.add(small.cosmic_anvil(1))):
        texture(sub(anim, "Frame", time="0.5"), SHEET16[0], idx)
    sub(e, "Size", 130)
    sub(e, "Static")
    sub(e, "OccupySquare")
    sub(e, "EnemyOccupySquare")
    out.append(e)

    # Unstable Essence: the Mysterious One's drop, which the Cosmic Anvil corrupts Enthroned gear with
    e = ET.Element("Object", {"type": types.get("Unstable Essence", OBJECT_TYPES), "id": "Unstable Essence"})
    sub(e, "Class", "Equipment")
    sub(e, "Item")
    texture(e, SHEET8[0], s8.add(small.unstable_essence()))
    sub(e, "SlotType", 10)
    # what it does is for players to find out
    sub(e, "Description", "A phial of the Between, never still. The Cosmic Anvil knows what to do with it.")
    sub(e, "BagType", 6)
    out.append(e)

    # the family's gear, the Mysterious One's white bags (BehaviorDb.BlackBetween.cs): what each does
    # beyond its numbers is its <RelicPower> (Player.BetweenPowers.cs)
    cyclone_arrow = projectile(types, s16, "Cyclone Arrow", small.whirl(), SHEET16[0])
    # it whirls as it flies (the client spins a projectile by its Rotation)
    sub(cyclone_arrow, "Rotation", 70)
    out.append(cyclone_arrow)
    for name, spec in ITEM_SPECS.items():
        out.append(gear_item(types, s8, name, spec))

    # the Rift once he's gone: a locked portal ("Part II: Coming Soon", BehaviorDb.BlackBetween.cs)
    e = ET.Element("Object", {"type": types.get("Rift of the Between: Part II", OBJECT_TYPES), "id": "Rift of the Between: Part II"})
    sub(e, "Class", "Portal")
    sub(e, "LockedPortal")
    texture(e, SHEET16[0], rift_cells[0])
    anim = sub(e, "Animation", prob="1.0")
    for idx in rift_cells:
        texture(sub(anim, "Frame", time="0.12"), SHEET16[0], idx)
    sub(e, "Size", 220)
    sub(e, "ShadowSize", 0)
    out.append(e)

    # invisible helpers
    for name, shots in HELPERS.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "GameObject")
        texture(e, "invisible", 0)
        sub(e, "NoMiniMap")
        sub(e, "ShadowSize", 0)
        sub(e, "Size", 0)
        for p in shots:
            e.append(p)
        out.append(e)
    return out


# ------------------------------------------------------------------------------------------------
# the map

W, H = 48, 72
CX, CY = 24, 22               # the Observatory's centre (the Observatory Heart's tile)
PLATFORM_R = 17               # its floor's radius; the rim is the last tile
DIAMOND = 8                   # how far the inlaid diamond's points (and the cross's arms) reach
CAUSEWAY = (22, 25)           # the approach's columns
CAUSEWAY_END = 68             # its southern end, where the Nexus Portal is
SPAWN_ROWS = (61, 66)
SENTINELS = [(23, 46), (24, 53), (23, 59)]
PYLONS = 12                   # round the rim, every 30 degrees but by the Rift and the causeway
RIFT = (CX, CY - PLATFORM_R)          # on the rim, standing up into the void behind it
HERALD = (CX, CY - PLATFORM_R + 2)    # just in front of it


class Map:
    def __init__(self):
        self.ground = [[VOID] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def place(self, x, y, name):
        assert self.ground[y][x] is not None and self.obj[y][x] is None, (name, x, y)
        self.obj[y][x] = name


def tile_dist(x, y):
    return math.hypot(x + 0.5 - (CX + 0.5), y + 0.5 - (CY + 0.5))


def build_map():
    m = Map()
    for y in range(H):
        for x in range(W):
            d = tile_dist(x, y)
            if d <= PLATFORM_R - 1:
                m.ground[y][x] = "Astral Stone"
            elif d <= PLATFORM_R:
                m.ground[y][x] = "Platform Rim"
    # the inlaid lines: the diamond and the cross
    # the causeway, from the rim south to the spawn; plain floor across its mouth, where the rim
    # would stand across the path
    for y in range(CY + PLATFORM_R - 3, CY + PLATFORM_R + 1):
        for x in range(CAUSEWAY[0] + 1, CAUSEWAY[1]):
            m.ground[y][x] = "Astral Stone"
    for y in range(CY + PLATFORM_R + 1, CAUSEWAY_END + 1):
        for x in range(CAUSEWAY[0], CAUSEWAY[1] + 1):
            m.ground[y][x] = "Astral Stone"
    for y in range(*SPAWN_ROWS):
        for x in range(CAUSEWAY[0] + 1, CAUSEWAY[1]):
            m.region[y][x] = "Spawn"
    for (x, y) in SENTINELS:
        m.place(x, y, "Starbound Sentinel")
    m.place(*RIFT, "Rift of the Between")
    m.place(*HERALD, "Mysterious One Herald")
    m.place(CX, CY, "Observatory Heart")
    for k in range(PYLONS):
        a = 2 * math.pi * k / PYLONS + math.pi / PYLONS
        x, y = round(CX + 0.5 + math.cos(a) * (PLATFORM_R - 0.6) - 0.5), round(CY + 0.5 + math.sin(a) * (PLATFORM_R - 0.6) - 0.5)
        if abs(x - CX) <= 3 and (y < CY or y > CY):
            continue
        if m.ground[y][x] is None or m.obj[y][x]:
            continue
        m.obj[y][x] = "Dusk Pylon" if k % 2 else "Dawn Pylon"
    return m


def save_map(m):
    entries, index, out = [], {}, bytearray()
    for y in range(H):
        for x in range(W):
            t = {"ground": m.ground[y][x]} if m.ground[y][x] else {}
            if m.obj[y][x]:
                t["objs"] = [{"id": m.obj[y][x]}]
            if m.region[y][x]:
                t["regions"] = [{"id": m.region[y][x]}]
            key = json.dumps(t, sort_keys=True)
            if key not in index:
                index[key] = len(entries)
                entries.append(t)
            i = index[key]
            out += bytes((i >> 8, i & 0xff))
    jm = {"width": W, "height": H, "dict": entries,
          "data": base64.b64encode(zlib.compress(bytes(out), 9)).decode("ascii")}
    os.makedirs(os.path.dirname(OUT_MAP), exist_ok=True)
    with open(OUT_MAP, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


def write_xml(paths, root_tag, elements, comment):
    root = ET.Element(root_tag)
    for e in elements:
        root.append(e)
    ET.indent(root, "\t")
    text = "<!-- %s -->\n%s\n" % (comment, ET.tostring(root, encoding="unicode"))
    for path in paths:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)


def preview(m, out, s8, s16, c8, c32):
    """The map at 8 pixels a tile, and the sheets enlarged, for checking by eye."""
    os.makedirs(out, exist_ok=True)
    colour = {"Astral Stone": (42, 36, 66), "Platform Rim": (74, 64, 112), None: (6, 4, 14)}
    img = Image.new("RGB", (W * 8, H * 8))
    for y in range(H):
        for x in range(W):
            c = colour[m.ground[y][x]]
            for yy in range(8):
                for xx in range(8):
                    img.putpixel((x * 8 + xx, y * 8 + yy), c)
            if m.obj[y][x]:
                for yy in range(2, 6):
                    for xx in range(2, 6):
                        img.putpixel((x * 8 + xx, y * 8 + yy), (255, 210, 74))
    img.save(os.path.join(out, "black_between_map.png"))
    for fname in (SHEET8[1], SHEET16[1], CHARS8[1], CHARS32[1], SHEET32[1]):
        sheet = Image.open(os.path.join(IMAGES, fname))
        k = 6 if "8x8" in fname else 4
        bg = Image.new("RGBA", sheet.size, (40, 36, 56, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.size[0] * k, sheet.size[1] * k), Image.NEAREST).save(os.path.join(out, fname))


def main():
    types = Types()
    s8, s16, s32 = Grid(8, 16), Grid(16, 8), Grid(32, 8)
    c8, c32 = Chars(8), Chars(32)
    m = build_map()
    g = grounds(types, s8)
    o = objects(types, s8, s16, s32, c8, c32)

    os.makedirs(IMAGES, exist_ok=True)
    s8.write(os.path.join(IMAGES, SHEET8[1]))
    s16.write(os.path.join(IMAGES, SHEET16[1]))
    s32.write(os.path.join(IMAGES, SHEET32[1]))
    c8.write(os.path.join(IMAGES, CHARS8[1]))
    c32.write(os.path.join(IMAGES, CHARS32[1]))
    write_xml(OUT_XML["grounds"], "GroundTypes", g,
              "Generated by tools/build_black_between.py: the Black Between's floor. Do not edit.")
    write_xml(OUT_XML["objects"], "Objects", o,
              "Generated by tools/build_black_between.py: the Black Between's bosses, minions, walls, "
              "rift, helpers and shots. Do not edit.")
    save_map(m)
    types.save()
    known = {e.get("id") for e in g + o} | {None}
    used = {t for row in m.ground for t in row} | {t for row in m.obj for t in row if t}
    assert used <= known, used - known
    print("wrote %s (%dx%d), %d grounds, %d objects" % (OUT_MAP, W, H, len(g), len(o)))
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        preview(m, out, s8, s16, c8, c32)
        print("preview in", out)


if __name__ == "__main__":
    main()
