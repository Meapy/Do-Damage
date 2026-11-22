"""Builds the Crucible, the boss-only tower over the Arena: five floors floating one above another in
an ember-lit sky, each with a champion of its own, climbed in one run.

    python tools/build_crucible.py [--preview <folder>]

The map, from south to north (the tower is climbed northwards; each floor is 70 tiles from the
next, out of sight of it):

- the Threshold: a small round landing with four braziers, where players arrive (Spawn region)
  and wait for the climb to begin;
- I, the Iron Bastion: a star fort of iron flagstones, a square keep with a round bastion at
  each corner (an iron pillar in each) and a gate notched into each side, a square of
  ember-bronze inlaid round its middle (Ferrogrim);
- II, the Drowned Sanctum: an atoll of sea-glass mosaic with a ragged coast, tide pools of
  shallows across it, five coral isles that stay dry when the tide comes in (the middle one and
  four at (+-7, +-7): her Flood keeps those), coral spires along its shore (Nerissa);
- III, the Ember Foundry: a forge hall in a cross, four arms of iron plate out from a square
  crucible, slag channels down the arms and braziers at their ends; when its rim crumbles the
  arms fall away (Pyrrhax);
- IV, the Clockwork Orrery: three rings with open sky between them, joined by spokes, a hub, a
  middle ring and the dial with its twelve numerals, orrery pylons on the spokes (Chronarch);
- V, the Apex: a star of marble with eight points, inlaid with the tower's sigil, four
  obelisks banded in the floors' colours on four of its points (Vaelith, the Crucible Sovereign).

Between each floor and the next a way up winds through the sky (WAYS), from one's northern edge
to the next one's southern edge, five squares wide, made of the floor it leads to: its ground and
edge, a line of its light down the middle, its pillars on ledges off the sides. Its squares are
marked `Crucible Way`: CrucibleWorld takes the ways and every floor above the Threshold away as the
world opens and builds them as the climb walks up, letting them fall behind it.

No floor reaches further than 17 squares from its middle (CrucibleWorld.FloorReach, the snapshot
it puts the floor back from); the floor painters (logic/behaviors/Crucible.cs) only repaint
ground that can be walked on and leave what stands on it, so the floors' holes and pillars stay.

Between them is open sky: no ground at all, so the Rust client draws its Crucible sky there
(`<Background>9</Background>`, cruciblesky.rs) and hangs rock under the floors' edges (islands.rs,
with the `Crucible Underside` grounds). An invisible Heart stands at each floor's centre: the
champions order it to change the floor (BehaviorDb.Crucible.cs), so all of it is centred on the
floor and comes and goes with its states. The run itself (the climb, the champions' arrivals, the
ascents between floors) is CrucibleWorld.cs, which reads each floor's centre from its Heart.

Outputs (generated, don't edit):
- the map, source/Shared/resources/worlds/Dungeons/The Crucible/The Crucible.jm;
- CrucibleGrounds.xml and CrucibleObjects.xml, in source/Shared/resources/xml/custom and the
  client's custom/xmls;
- the sheets Crucible8x8.png, Crucible16x16.png, Crucible32x32.png, CrucibleChars16x16.png and
  CrucibleChars32x32.png in the client's custom/images (registered for the Rust client by
  tools/gen_rs_manifest.py).
The art is drawn by tools/crucible/bosses.py (the champions, 32x32) and small.py (everything
else). Type codes are kept stable in tools/crucible/types.json (0xC600-0xC7FF). Needs Pillow.
"""
import base64
import json
import math
import os
import sys
import xml.etree.ElementTree as ET
import zlib

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from crucible import bosses, small  # noqa: E402
from black_between.bosses import spectral  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "The Crucible", "The Crucible.jm")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "CrucibleGrounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "CrucibleGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "CrucibleObjects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "CrucibleObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "crucible", "types.json")
GROUND_TYPES = (0xC600, 0xC640)
OBJECT_TYPES = (0xC640, 0xC800)
SHEET8 = ("crucible8x8", "Crucible8x8.png")
SHEET16 = ("crucible16x16", "Crucible16x16.png")
SHEET32 = ("crucible32x32", "Crucible32x32.png")
CHARS16 = ("crucibleChars16x16", "CrucibleChars16x16.png")
CHARS32 = ("crucibleChars32x32", "CrucibleChars32x32.png")


# ------------------------------------------------------------------------------------------------
# types and sheets (as tools/build_black_between.py)

class Types:
    def __init__(self):
        self.types = json.load(open(TYPES)) if os.path.exists(TYPES) else {}

    def get(self, key, span):
        if key not in self.types:
            used = {int(v, 16) for v in self.types.values()}
            self.types[key] = "0x%04X" % next(t for t in range(*span) if t not in used)
        return self.types[key]

    def save(self):
        with open(TYPES, "w", newline="\n") as f:
            json.dump(self.types, f, indent=1, sort_keys=True)
            f.write("\n")


class Grid:
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
    """A character sheet: a row of seven cells each (stand, walk 1, walk 2, unused, attack 1,
    attack 2, the attack's second cell)."""

    def __init__(self, size):
        self.size, self.rows = size, []

    def add(self, frames, attack):
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


def foe(pid, obj, damage, speed, life, size=100, effects=(), amplitude=None, frequency=None, piercing=False):
    """An enemy's shot: it goes on through whoever it hits (MultiHit), so each player has to dodge it."""
    p = ET.Element("Projectile", {"id": str(pid)})
    sub(p, "ObjectId", obj)
    sub(p, "Damage", damage)
    sub(p, "Speed", speed)
    sub(p, "LifetimeMS", life)
    sub(p, "Size", size)
    if amplitude is not None:
        sub(p, "Amplitude", amplitude)
        sub(p, "Frequency", frequency)
    sub(p, "MultiHit")
    if piercing:
        sub(p, "ArmorPiercing")
    for effect, duration in effects:
        sub(p, "ConditionEffect", effect, duration=duration)
    return p


# ------------------------------------------------------------------------------------------------
# grounds: name -> (minimap colour, extras); the art is small.TILES

FLOW = ("Flow", {"dx": ".35"})
WAVE = ("Wave", {"dx": "0.3", "dy": "0.3"})
GROUNDS = {
    "Threshold Stone": ("0x3A3640", {}),
    "Threshold Rim": ("0x4E4858", {}),
    "Bastion Flagstone": ("0x3C414C", {}),
    "Bastion Rim": ("0x565E6E", {}),
    "Bastion Inlay": ("0x6A3A1A", {}),
    "Grinding Blade": ("0xC4482C", {"MinDamage": 110, "MaxDamage": 140}),
    "Molten Grate": ("0xD8600E", {"MinDamage": 130, "MaxDamage": 160}),
    "Sanctum Mosaic": ("0x1F5A60", {}),
    "Sanctum Rim": ("0x2C6A6A", {}),
    "Coral Isle": ("0x7A4A5A", {}),
    "Sanctum Shallows": ("0x2E96A2", {"Animate": WAVE}),
    # the flood: deep water slows you and grinds at you; the surge hits hard
    "Deep Tide": ("0x0E4466", {"Speed": 0.55, "Sink": None, "MinDamage": 40, "MaxDamage": 60, "Animate": FLOW}),
    "Surging Tide": ("0x5AC8E0", {"MinDamage": 150, "MaxDamage": 180, "Animate": FLOW}),
    "Foundry Plate": ("0x2E2624", {}),
    "Foundry Rim": ("0x4A3A30", {}),
    "Forge Lava": ("0xFF6A14", {"MinDamage": 160, "MaxDamage": 200, "Animate": WAVE}),
    "Cooling Slag": ("0x4A2418", {}),
    "Orrery Dial": ("0x3A3248", {}),
    "Orrery Rim": ("0x7A4E18", {}),
    "Dial Numeral": ("0xC08A30", {}),
    "Chime Light": ("0xF2D282", {"MinDamage": 140, "MaxDamage": 170}),
    "Hour Hand": ("0xB01818", {"MinDamage": 150, "MaxDamage": 180}),
    "Apex Marble": ("0xC8C4D4", {}),
    "Apex Rim": ("0xD8A428", {}),
    "Apex Sigil": ("0xFFE68A", {}),
    "Sovereign Flame": ("0xFFD04A", {"MinDamage": 170, "MaxDamage": 210, "Animate": WAVE}),
    # where something is about to come up through the floor, or the floor is about to fall away:
    # harmless, a warning
    "Crumbling Floor": ("0x3A3036", {}),
    "Crucible Warning": ("0x8A4A1E", {}),
    "Crucible Underside Rock": ("0x2A2230", {}),
    "Crucible Underside Rim": ("0x4A3E50", {}),
}
# the warning's art: the floor glowing ember-orange in stripes, harmless
small.TILES["Crucible Warning"] = (small.pattern(["#5a2a14", "#8a4a1e", "#c86a24"], lambda x, y, s: (x + y + s) % 3), 2)


def grounds(types, sheet):
    art = small.tiles()
    out = []
    for name, (colour, extra) in GROUNDS.items():
        e = ET.Element("Ground", {"type": types.get(name, GROUND_TYPES), "id": name})
        rt = sub(e, "RandomTexture")
        for img in art[name]:
            texture(rt, SHEET8[0], sheet.add(img))
        for k, v in extra.items():
            if k == "Animate":
                sub(e, "Animate", v[0], **v[1])
            elif v is None:
                sub(e, k)
            else:
                sub(e, k, v)
        sub(e, "Color", colour)
        out.append(e)
    return out


# ------------------------------------------------------------------------------------------------
# objects

# The champions (32x32, BehaviorDb.Crucible.cs): name -> (art, size, health, defence, alt textures,
# [projectiles]). Health and defence here are the base: BossStats sets them in the fight.
ARMOR_BROKEN = ("Armor Broken", 2)
CHAMPIONS = {
    "Ferrogrim, the Iron Warden": ("Ferrogrim, the Iron Warden", 120, 150000, 60, ["Ferrogrim, Iron Tempest"], [
        foe(0, "Iron Shard", 140, 80, 2200),
        foe(1, "Hammer Wave", 190, 55, 3000, 160, effects=[ARMOR_BROKEN]),
        foe(2, "Iron Shard", 120, 60, 2600),
        foe(3, "Hammer Wave", 160, 70, 2400, 140),
    ]),
    "Nerissa, the Tidecaller": ("Nerissa, the Tidecaller", 115, 180000, 50, ["Nerissa, Maelstrom"], [
        foe(0, "Trident Bolt", 160, 95, 2200),
        foe(1, "Tide Orb", 140, 50, 3600, 120, effects=[("Slowed", 1.5)], amplitude=1.0, frequency=0.6),
        foe(2, "Bubble", 110, 40, 4000, 110, effects=[("Slowed", 1)]),
        foe(3, "Trident Bolt", 150, 120, 1800),
    ]),
    "Pyrrhax, the Forge Tyrant": ("Pyrrhax, the Forge Tyrant", 125, 210000, 70, ["Pyrrhax, Molten Core"], [
        foe(0, "Magma Ball", 200, 60, 2800, 150),
        foe(1, "Cinder", 140, 100, 1800),
        foe(2, "Cinder", 130, 70, 2400),
        foe(3, "Magma Ball", 160, 45, 3600, 120),
    ]),
    "Chronarch, the Hour Thief": ("Chronarch, the Hour Thief", 115, 240000, 80, ["Chronarch, Reversal"], [
        foe(0, "Gear Shot", 140, 60, 3000, 110),
        foe(1, "Hand Blade", 170, 110, 2000),
        foe(2, "Chrono Orb", 150, 45, 4000, 120, effects=[("Slowed", 2)]),
        foe(3, "Gear Shot", 130, 75, 2600),
    ]),
    "Vaelith, the Crucible Sovereign": ("Vaelith, the Crucible Sovereign", 125, 360000, 90,
                                        ["Vaelith, Ascendant", "Vaelith, Unbound"], [
        foe(0, "Sovereign Lance", 200, 120, 2200),
        foe(1, "Flame Feather", 160, 70, 3000, 110, amplitude=1.2, frequency=0.5),
        foe(2, "Sovereign Star", 150, 60, 3400),
        foe(3, "Void Ember", 190, 55, 3800, 120, effects=[("Darkness", 1.5), ARMOR_BROKEN]),
        foe(4, "Flame Feather", 170, 130, 1600),
    ]),
}
# copies that fight beside a champion: name -> (art, size, [projectiles])
ECHOES = {
    # Chronarch's echoes in his Time Fracture: him to the life, but untouchable
    "Hour Echo": ("Hour Echo", 115, [foe(0, "Hand Blade", 150, 100, 2000), foe(1, "Gear Shot", 120, 60, 2600)]),
    # the fallen champions' ghosts on the Apex
    "Echo of Ferrogrim": ("Echo of Ferrogrim", 110, [foe(0, "Hammer Wave", 170, 55, 3000, 150)]),
    "Echo of Nerissa": ("Echo of Nerissa", 110, [foe(0, "Tide Orb", 150, 50, 3600, 120, effects=[("Slowed", 1)])]),
    "Echo of Pyrrhax": ("Echo of Pyrrhax", 110, [foe(0, "Magma Ball", 170, 55, 3000, 140)]),
    "Echo of Chronarch": ("Echo of Chronarch", 110, [foe(0, "Chrono Orb", 150, 45, 4000, 120, effects=[("Slowed", 1.5)])]),
}
# minions (16x16): name -> (health, defence, size, static, [projectiles])
MINIONS = {
    "Iron Sentinel": (9000, 40, 110, True, [foe(0, "Iron Shard", 110, 70, 2400)]),
    "Bastion Banner": (6000, 30, 120, True, [foe(0, "Iron Shard", 90, 50, 2000)]),
    "Riptide Eel": (4000, 20, 100, False, [foe(0, "Bubble", 100, 60, 1600, effects=[("Slowed", 1)])]),
    "Molten Golem": (7000, 30, 110, False, [foe(0, "Cinder", 110, 80, 1400)]),
    "Clockwork Sentry": (5000, 35, 100, False, [foe(0, "Gear Shot", 110, 70, 2000)]),
}
IMMUNE = ("StunImmune", "ParalyzeImmune", "PetrifyImmune", "SlowedImmune", "DazedImmune")
# who sounds like whom (existing sounds)
SOUNDS = {
    "Ferrogrim, the Iron Warden": "monster/golems_hit", "Nerissa, the Tidecaller": "monster/snake_queen_hit",
    "Pyrrhax, the Forge Tyrant": "monster/demons_hit", "Chronarch, the Hour Thief": "monster/golems_hit",
    "Vaelith, the Crucible Sovereign": "monster/ghost_god_hit",
}
# the floors' Hearts (invisible): the champions order them to change the floor
HEARTS = ["Bastion Heart", "Sanctum Heart", "Foundry Heart", "Orrery Heart", "Apex Heart"]
# scenery: name -> (art frames, size, blocks)
SCENERY = {
    "Bastion Pillar": ([small.pillar("iron", f) for f in range(2)], 140, True),
    "Coral Spire": ([small.pillar("coral", f) for f in range(3)], 130, True),
    "Foundry Brazier": ([small.pillar("forge", f) for f in range(4)], 130, True),
    "Orrery Pylon": ([small.pillar("orrery", f) for f in range(4)], 130, True),
    "Apex Obelisk": ([small.pillar("apex", f) for f in range(2)], 150, True),
    "Threshold Brazier": ([small.brazier(f) for f in range(3)], 120, True),
}


# the white bags (1% each from the champion of their floor): name -> what the item is. The
# numbers in the tooltips are the server's (Player.CruciblePowers.cs, and the turret's slam in
# BehaviorDb.Crucible.cs): change both together. "activate": the item's own <Activate>s (text,
# attributes); "power": its <RelicPower>; "stats": (stat, amount) on equip
WHITES = {
    "Ferrogrim's Bastion Turret": dict(
        slot=32, sound="spell/light_heal", mp=150, cooldown=0, power="BastionTurret",
        stats=[(0, 60), (3, 60), (21, 6), (28, 4)],
        activate=[("BuildTower", dict(objectId="Bastion Turret", duration=20))],
        description="Ferrogrim's own sentinel, folded down for a Mechanic to raise. It fires three iron shards at a "
                    "time, and every 3 seconds it slams the ground: the enemies round it are struck and their armor "
                    "broken, and allies beside it are Armored while it holds.",
        info=[("Turret", "20 secs: 3 shards of 160 every 0.7 secs, to 10 sqrs"),
              ("Slam", "every 3 secs: 380 to 3.5 sqrs, armor broken for 2 secs; allies within it Armored")]),
    "Nerissa's Tidepearl": dict(
        slot=30, sound="spell/fire_spray", mp=100, cooldown=0, power="Tidepearl",
        stats=[(0, 60), (3, 60), (26, 8), (27, 4)],
        activate=[("PearlAbility", dict(amount=200, objectId="Tidal Substitute"))],
        description="A pearl of the Drowned Sanctum's sea. It calls up a substitute that hurls five of Nerissa's "
                    "tridents and heals you, and the tide answers: three waves roll out from you, each striking "
                    "your enemies and washing over your allies.",
        info=[("Substitute", "5 tridents of 230; heals you 200"),
              ("Tide", "3 waves, 0.45 secs apart, to 5.5 sqrs: 220 to enemies, heals allies 60")]),
    "Pyrrhax's Meteor Mortar": dict(
        slot=5005, sound="weapon/cannon_shot", mp=120, cooldown=0, power="MeteorMortar",
        stats=[(0, 60), (3, 60), (20, 4), (28, 6)],
        activate=[],
        description="The Forge Tyrant's mortar. Its shell arcs to where you aim and bursts in a crater of slag, and "
                    "the sky follows it down: four meteors fall round the crater, while the slag burns on in it.",
        info=[("Shell", "to 9 sqrs: 750 to 3 sqrs, paralyzed for 1.5 secs"),
              ("Meteors", "4 round it, each 280 to 1.6 sqrs"),
              ("Slag", "90 every 0.5 secs to 2.5 sqrs, for 3 secs")]),
    "Chronarch's Coat of Stolen Hours": dict(
        slot=37, sound="spell/fire_nova", mp=110, cooldown=0, power="StolenHours",
        stats=[(0, 60), (3, 60), (26, 4), (22, 4)],
        activate=[("ConditionEffectAura", dict(effect="Armored", duration=5, range=8)),
                  ("ConditionEffectSelf", dict(effect="Damaging", duration=5)),
                  ("StatBoostSelf", dict(stat=20, amount=10, duration=6))],
        description="The Hour Thief's coat, its clock still running. Time stops round you: your enemies slow, you "
                    "quicken, and the clock's hand sweeps twice round you, cutting all it passes.",
        info=[("Stolen Hours", "enemies within 6 sqrs slowed and you Speedy for 3 secs"),
              ("Clock Hand", "2 sweeps of 0.8 secs, 320 to each enemy within 6 sqrs a sweep")]),
    "Vaelith's Final Aria": dict(
        slot=28, sound="spell/major_heal", mp=120, cooldown=6, power="FinalAria",
        stats=[(0, 60), (3, 60), (20, 5), (28, 5)],
        activate=[("ConditionEffectAura", dict(duration=7, effect="Inspired", range=8))],
        description="The Sovereign's own lute, strung with the light of the Apex. Its chord inspires your allies, "
                    "and the aria rings on: four verses, each a wider ring of light from you, harder than the last, "
                    "and the last a crescendo that breaks armor and drives allies into a fury.",
        info=[("Party Effect", "Inspired within 8 sqrs for 7 secs"),
              ("Verses", "4, every 0.6 secs, following you: 250 to 6 sqrs, 320 to 7, 400 to 8"),
              ("Crescendo", "700 to 9 sqrs, armor broken for 3 secs; allies within 9 sqrs berserk for 3 secs")]),
}

# the white bags' allies: the turret (Ferrogrim's sentinel) and the pearl's substitute (unseen)
ALLIES = {
    "Bastion Turret": dict(sentinel=True, shot=("Iron Shard", 160, 120, 800, False)),
    "Tidal Substitute": dict(sentinel=False, shot=("Trident Bolt", 230, 160, 800, True)),
}


def projectile(types, sheet, name, img):
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", "Projectile")
    texture(e, SHEET8[0], sheet.add(img))
    if name in ("Gear Shot", "Sovereign Star"):
        sub(e, "Rotation", 60)  # they spin as they fly
    return e


def animated(e, sheet, file, frames, time):
    first = sheet.add(frames[0])
    texture(e, file, first)
    anim = sub(e, "Animation", prob="1.0")
    for idx in [first] + [sheet.add(f) for f in frames[1:]]:
        texture(sub(anim, "Frame", time=str(time)), file, idx)


def objects(types, s8, s16, s32, c16, c32):
    out = []
    for name, img in small.shots().items():
        out.append(projectile(types, s8, name, img))

    sprites = bosses.all_sprites()
    sprites.update(bosses.echoes(sprites))
    sprites["Hour Echo"] = [spectral(f, (255, 220, 140)) for f in sprites["Chronarch, the Hour Thief"]]
    rows = {}

    def row(art):
        if art not in rows:
            fr = sprites[art]
            rows[art] = c32.add(fr[:3], fr[3])
        return rows[art]

    for name, (art, size, hp, defense, alts, shots) in CHAMPIONS.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, CHARS32[0], row(art), "AnimatedTexture")
        for k, alt in enumerate(alts, start=1):
            texture(sub(e, "AltTexture", id=k), CHARS32[0], row(alt), "AnimatedTexture")
        hit = SOUNDS.get(name, "monster/ghost_god_hit")
        sub(e, "HitSound", hit)
        sub(e, "DeathSound", hit.replace("_hit", "_death"))
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", hp)
        sub(e, "Defense", defense)
        sub(e, "Quest")
        sub(e, "HealthBarBoss")
        sub(e, "Level", 20)
        sub(e, "God")
        sub(e, "ShowName")
        sub(e, "NoArticle")
        sub(e, "StasisImmune")
        for flag in IMMUNE:
            sub(e, flag)
        for p in shots:
            e.append(p)
        out.append(e)

    for name, (art, size, shots) in ECHOES.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, CHARS32[0], row(art), "AnimatedTexture")
        sub(e, "HitSound", "monster/ghost_god_hit")
        sub(e, "DeathSound", "monster/ghost_god_death")
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", 100000)
        sub(e, "Defense", 0)
        sub(e, "StasisImmune")
        for flag in IMMUNE:
            sub(e, flag)
        sub(e, "XpMult", 0)
        for p in shots:
            e.append(p)
        out.append(e)

    art = small.minions()
    minion_rows = {}
    for name, (hp, defense, size, static, shots) in MINIONS.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        frames, attack = art[name]
        minion_rows[name] = c16.add(frames, attack)
        texture(e, CHARS16[0], minion_rows[name], "AnimatedTexture")
        sub(e, "HitSound", "monster/golems_hit" if static else "monster/greater_pit_snakes_hit")
        sub(e, "DeathSound", "monster/golems_death" if static else "monster/greater_pit_snakes_death")
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", hp)
        sub(e, "Defense", defense)
        sub(e, "StasisImmune")
        # a turret stays put by its behaviour, not by <Static/>: the server's area damage (World.AOE,
        # /smite, the abilities') skips a Static enemy
        if static:
            sub(e, "StunImmune")
            sub(e, "ParalyzeImmune")
        sub(e, "XpMult", 0.5)
        for p in shots:
            e.append(p)
        out.append(e)

    for name, (frames, size, blocks) in SCENERY.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "GameObject")
        animated(e, s16, SHEET16[0], frames, 0.35)
        sub(e, "Size", size)
        sub(e, "Static")
        if blocks:
            sub(e, "OccupySquare")
            sub(e, "EnemyOccupySquare")
        out.append(e)

    # the white bags: each its class's ability (WHITES), what it does beyond its numbers its
    # <RelicPower> (Player.CruciblePowers.cs)
    for name, w in WHITES.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Equipment")
        sub(e, "Item")
        texture(e, SHEET8[0], s8.add(small.item_icon(name)))
        sub(e, "SlotType", w["slot"])
        sub(e, "Description", w["description"])
        extra = sub(e, "ExtraTooltipData")
        for k, v in w["info"]:
            sub(extra, "EffectInfo", name=k, description=v)
        sub(e, "Sound", w["sound"])
        sub(e, "Usable")
        for text, attrs in w["activate"]:
            sub(e, "Activate", text, **attrs)
        for stat, amount in w["stats"]:
            sub(e, "ActivateOnEquip", "IncrementStat", stat=stat, amount=amount)
        sub(e, "MpCost", w["mp"])
        if w["cooldown"]:
            sub(e, "Cooldown", w["cooldown"])
        sub(e, "RelicPower", w["power"])
        sub(e, "BagType", 6)
        sub(e, "FameBonus", 6)
        sub(e, "Soulbound")
        out.append(e)
    for name, a in ALLIES.items():
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        if a["sentinel"]:
            texture(e, CHARS16[0], minion_rows["Iron Sentinel"], "AnimatedTexture")
            sub(e, "Size", 90)
        else:
            texture(e, "invisible", 0)
        shot, damage, speed, life, multi = a["shot"]
        pr = sub(e, "Projectile", id="0")
        sub(pr, "ObjectId", shot)
        sub(pr, "Speed", speed)
        sub(pr, "Damage", damage)
        sub(pr, "LifetimeMS", life)
        if multi:
            sub(pr, "MultiHit")
            sub(pr, "PassesCover")
        sub(e, "NoMiniMap")
        out.append(e)

    # Ferrogrim's grates, risen from the floor in his Lockdown: walls (BehaviorDb.Crucible.cs)
    e = ET.Element("Object", {"type": types.get("Bastion Grate", OBJECT_TYPES), "id": "Bastion Grate"})
    sub(e, "Class", "GameObject")
    animated(e, s16, SHEET16[0], [small.grate(f) for f in range(3)], 0.2)
    sub(e, "Size", 100)
    sub(e, "ShadowSize", 0)
    sub(e, "Static")
    sub(e, "OccupySquare")
    sub(e, "FullOccupy")
    sub(e, "EnemyOccupySquare")
    out.append(e)

    # the way up, at a cleared floor's centre (CrucibleWorld): harmless, turning
    e = ET.Element("Object", {"type": types.get("Ascension Sigil", OBJECT_TYPES), "id": "Ascension Sigil"})
    sub(e, "Class", "GameObject")
    animated(e, s16, SHEET16[0], [small.sigil(f) for f in range(4)], 0.12)
    sub(e, "Size", 320)
    sub(e, "ShadowSize", 0)
    sub(e, "DrawOnGround")
    sub(e, "NoMiniMap")
    out.append(e)

    # the portal in the Nexus, across Realm 2's vortex from the Arena's pit
    # (DoomedWorlds.AddCruciblePortal): sunk into the floor as the Nexus's other portals are, four
    # tiles across, its name under it; the Rust client adds its embers (relic.rs `champion_auras`)
    e = ET.Element("Object", {"type": types.get("Crucible Portal", OBJECT_TYPES), "id": "Crucible Portal"})
    sub(e, "DisplayId", "The Crucible")
    sub(e, "Class", "Portal")
    sub(e, "IntergamePortal")
    sub(e, "DungeonName", "The Crucible")
    sub(e, "ShowName")
    animated(e, s32, SHEET32[0], [small.portal(f) for f in range(small.PORTAL_FRAMES)], 0.11)
    sub(e, "Size", 100)
    sub(e, "DrawOnGround")
    sub(e, "GroundSize", 4)
    sub(e, "ShadowSize", 0)
    out.append(e)

    for name in HEARTS:
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "GameObject")
        texture(e, "invisible", 0)
        sub(e, "NoMiniMap")
        sub(e, "ShadowSize", 0)
        sub(e, "Size", 0)
        out.append(e)
    return out


# ------------------------------------------------------------------------------------------------
# the map

W, H = 48, 460
CX = 24
# (heart, centre row, reach, shape, floor, rim): each floor's shape is `SHAPES[shape]`, true for
# the squares (dx, dy) from its middle that are floor
THRESHOLD = (CX, 440, 8)
FLOORS = [
    ("Bastion Heart", 370, 17, "fort", "Bastion Flagstone", "Bastion Rim"),
    ("Sanctum Heart", 300, 17, "atoll", "Sanctum Mosaic", "Sanctum Rim"),
    ("Foundry Heart", 230, 17, "cross", "Foundry Plate", "Foundry Rim"),
    ("Orrery Heart", 160, 17, "orrery", "Orrery Dial", "Orrery Rim"),
    ("Apex Heart", 88, 17, "star", "Apex Marble", "Apex Rim"),
]


def _fort(dx, dy):
    # a square keep, a round bastion at each corner, a gate notched into the middle of each side
    ax, ay = abs(dx), abs(dy)
    if math.hypot(ax - 11, ay - 11) <= 4.6:
        return True
    if max(ax, ay) > 11.5:
        return False
    gate = min(ax, ay) <= 1.5 and max(ax, ay) >= 10
    return not gate


def _atoll(dx, dy):
    # a disc whose coast wanders in and out
    r, a = math.hypot(dx, dy), math.atan2(dy, dx)
    coast = 14.2 + 1.5 * math.sin(3 * a + 0.6) + 1.0 * math.sin(5 * a + 2.1) + 0.6 * math.sin(9 * a)
    return r <= min(16.6, coast)


def _cross(dx, dy):
    # a square crucible in the middle, four arms out of it, their ends rounded
    ax, ay = abs(dx), abs(dy)
    if max(ax, ay) <= 8.5:
        return True
    arm = (ax <= 4.5 and ay <= 15.5) or (ay <= 4.5 and ax <= 15.5)
    if not arm:
        return False
    far = max(ax, ay)
    return far <= 12.5 or math.hypot(min(ax, ay), far - 12.5) <= 4.6


def _orrery(dx, dy):
    # a hub, a middle ring and the dial, sky between them, joined by spokes (four inside, six out)
    r, a = math.hypot(dx, dy), math.atan2(dy, dx)
    if r > 15.5:
        return False
    gap1, gap2 = 5.6 <= r <= 7.2, 11.2 <= r <= 12.6
    if not gap1 and not gap2:
        return True
    step = math.pi / 2 if gap1 else math.pi / 3
    off = math.pi / 4 if gap1 else 0.0
    k = (a - off) / step
    return abs(k - round(k)) * step * r <= 1.6


def _star(dx, dy):
    # eight points of marble round a disc
    r, a = math.hypot(dx, dy), math.atan2(dy, dx)
    return r <= 10.6 + 5.8 * abs(math.cos(4 * a)) ** 3


SHAPES = {"fort": _fort, "atoll": _atoll, "cross": _cross, "orrery": _orrery, "star": _star}


# The ways up between them (CrucibleWorld builds each square by square ahead of whoever walks it,
# and lets it fall behind the last of them): each winds from the floor below's northern edge to
# the next one's southern edge, made of the floor it leads to, a line of that floor's light down
# its middle and that floor's pillars on ledges off its sides. Per way, the floor it leads to:
# (ground, edge (None: none), the light down its middle, the pillars), and its course: how it
# winds ("wave": an S of `turns` half-bends `swing` squares out; "zigzag": the same in straight
# runs), how wide (squares from its middle line).
WAYS = [
    (("Bastion Flagstone", "Bastion Rim", "Bastion Inlay", "Bastion Pillar"), ("wave", 1, 8.0), 2.5),
    (("Sanctum Mosaic", "Sanctum Rim", "Sanctum Shallows", "Coral Spire"), ("wave", 2, 10.0), 2.5),
    (("Foundry Plate", "Foundry Rim", "Cooling Slag", "Foundry Brazier"), ("zigzag", 3, 9.0), 2.5),
    (("Orrery Dial", None, "Dial Numeral", "Orrery Pylon"), ("wave", 2, 12.0), 2.5),
    (("Apex Marble", "Apex Rim", "Apex Sigil", "Apex Obelisk"), ("wave", 1, 13.0), 3.0),
]
# the ways' straight runs out of a floor and into the next: from its edge to this many squares
# from its middle, so they wind only clear of both
WAY_CLEAR = 19
WAY_LIGHT_EVERY = 3.0   # squares along the middle line between its lights
WAY_LEDGE_EVERY = 11.0  # and between the ledges with pillars, side by side in turn


class Map:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]
        self.ways = []  # each way's middle line, (x, y) from the floor below up (for scripted walks)

    def place(self, x, y, name):
        assert self.ground[y][x] is not None and self.obj[y][x] is None, (name, x, y)
        self.obj[y][x] = name


def inside(shape, dx, dy, r):
    if shape == "circle":
        return math.hypot(dx, dy) <= r + 0.3
    if shape == "octagon":
        return max(abs(dx), abs(dy)) <= r and abs(dx) + abs(dy) <= r * 1.42
    # a square with its corners rounded off
    return max(abs(dx), abs(dy)) <= r and math.hypot(dx, dy) <= r * 1.22


def disc(m, cy, r, shape, floor, rim):
    if shape in SHAPES:
        fn = SHAPES[shape]
        for y in range(cy - r, cy + r + 1):
            for x in range(CX - r, CX + r + 1):
                dx, dy = x - CX, y - cy
                if not fn(dx, dy):
                    continue
                assert max(abs(dx), abs(dy)) <= 17, (shape, dx, dy)
                edge = any(not fn(dx + ex, dy + ey) for ex, ey in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                # the orrery's rings edged only on the outside (its rim stands up: on every ring
                # it walled the floor into a maze)
                if shape == "orrery" and math.hypot(dx, dy) < 14.5:
                    edge = False
                m.ground[y][x] = rim if edge else floor
        return
    for y in range(cy - r - 1, cy + r + 2):
        for x in range(CX - r - 1, CX + r + 2):
            dx, dy = x - CX, y - cy
            if not inside(shape, dx, dy, r):
                continue
            edge = any(not inside(shape, dx + ex, dy + ey, r) for ex, ey in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            m.ground[y][x] = rim if edge else floor


def ring_points(cy, radius, count, offset=0.0):
    pts = []
    for k in range(count):
        a = 2 * math.pi * k / count + offset
        pts.append((round(CX + math.cos(a) * radius), round(cy + math.sin(a) * radius)))
    return pts


def build_map():
    m = Map()
    # the Threshold
    tx, ty, tr = THRESHOLD
    disc(m, ty, tr, "circle", "Threshold Stone", "Threshold Rim")
    for y in range(ty - 3, ty + 4):
        for x in range(tx - 3, tx + 4):
            if math.hypot(x - tx, y - ty) <= 3.2:
                m.region[y][x] = "Spawn"
    for (x, y) in ring_points(ty, 6, 4, math.pi / 4):
        m.place(x, y, "Threshold Brazier")

    for (heart, cy, r, shape, floor, rim) in FLOORS:
        disc(m, cy, r, shape, floor, rim)
        if heart == "Bastion Heart":
            # an inlaid square of ember-bronze round the middle, the keep's corners marked with it,
            # an iron pillar in each bastion and either side of each gate
            for y in range(cy - 6, cy + 7):
                for x in range(CX - 6, CX + 7):
                    if max(abs(x - CX), abs(y - cy)) == 6:
                        m.ground[y][x] = "Bastion Inlay"
            for sx in (-1, 1):
                for sy in (-1, 1):
                    for k in range(3):
                        m.ground[cy + sy * (9 - k)][CX + sx * 9] = "Bastion Inlay"
                        m.ground[cy + sy * 9][CX + sx * (9 - k)] = "Bastion Inlay"
                    m.place(CX + sx * 12, cy + sy * 12, "Bastion Pillar")
            for (gx, gy) in ((0, -1), (0, 1), (-1, 0), (1, 0)):
                for side in (-1, 1):
                    m.place(CX + gx * 10 + gy * 3 * side, cy + gy * 10 + gx * 3 * side, "Bastion Pillar")
        elif heart == "Sanctum Heart":
            # tide pools of shallows across the atoll, its coast washed with them too
            for y in range(cy - r, cy + r + 1):
                for x in range(CX - r, CX + r + 1):
                    if m.ground[y][x] != floor:
                        continue
                    dx, dy = x - CX, y - cy
                    pool = (math.sin(dx * 0.7 + 1.3) + math.sin(dy * 0.8 - 0.4) + math.sin((dx + dy) * 0.45)) > 1.55
                    if pool and math.hypot(dx, dy) > 4:
                        m.ground[y][x] = "Sanctum Shallows"
            # five coral isles: the middle and four at (+-7, +-7) (dry when the tide rises: her Flood)
            for (ix, iy, ir) in [(CX, cy, 2.6)] + [(CX + sx * 7, cy + sy * 7, 2.2) for sx in (-1, 1) for sy in (-1, 1)]:
                for y in range(int(iy - 3), int(iy + 4)):
                    for x in range(int(ix - 3), int(ix + 4)):
                        if math.hypot(x - ix, y - iy) <= ir and m.ground[y][x] is not None:
                            m.ground[y][x] = "Coral Isle"
            # coral spires along the shore, where the coast reaches out
            for k in range(7):
                a = 2 * math.pi * k / 7 + 0.35
                for d in (13, 12, 11):
                    x, y = round(CX + math.cos(a) * d), round(cy + math.sin(a) * d)
                    if m.ground[y][x] in (floor, "Sanctum Shallows") and m.obj[y][x] is None:
                        m.place(x, y, "Coral Spire")
                        break
        elif heart == "Foundry Heart":
            # slag channels down the middle of each arm and the crucible's diagonals, braziers at
            # the arms' ends and the crucible's corners
            for y in range(cy - r, cy + r + 1):
                for x in range(CX - r, CX + r + 1):
                    dx, dy = x - CX, y - cy
                    if m.ground[y][x] != floor:
                        continue
                    channel = (dx == 0 and 10 <= abs(dy) <= 14) or (dy == 0 and 10 <= abs(dx) <= 14)
                    diag = abs(dx) == abs(dy) and 3 <= abs(dx) <= 7
                    if channel or diag:
                        m.ground[y][x] = "Cooling Slag"
            for (x, y) in ring_points(cy, 14, 4, 0):
                for side in (-1, 1):
                    bx, by = (x + side * 3, y) if x == CX else (x, y + side * 3)
                    m.place(bx, by, "Foundry Brazier")
            for sx in (-1, 1):
                for sy in (-1, 1):
                    m.place(CX + sx * 7, cy + sy * 7, "Foundry Brazier")
        elif heart == "Orrery Heart":
            # the dial's twelve numerals, a ring of them round the hub, pylons where the spokes cross the
            # sky (beside them, so the way stays open)
            for (x, y) in ring_points(cy, 14, 12, -math.pi / 2):
                m.ground[y][x] = "Dial Numeral"
            for y in range(cy - 5, cy + 6):
                for x in range(CX - 5, CX + 6):
                    if abs(math.hypot(x - CX, y - cy) - 4) < 0.5:
                        m.ground[y][x] = "Dial Numeral"
            for (x, y) in ring_points(cy, 9.2, 4, math.pi / 4):
                m.place(x, y, "Orrery Pylon")
        else:
            # the tower's sigil inlaid in gold round the middle, out along each point
            for y in range(cy - r, cy + r + 1):
                for x in range(CX - r, CX + r + 1):
                    dx, dy = x - CX, y - cy
                    d = math.hypot(dx, dy)
                    if d < 1 or d > 14 or m.ground[y][x] != floor:
                        continue
                    a = math.atan2(dy, dx)
                    spike = 9 * (0.45 + 0.55 * abs(math.cos(4 * a)) ** 6)
                    point = abs(math.sin(4 * a)) * d < 0.6 and d > 9.5
                    if abs(d - spike) < 0.7 or abs(d - 4) < 0.45 or point:
                        m.ground[y][x] = "Apex Sigil"
            for (x, y) in ring_points(cy, 14, 4, math.pi / 4):
                m.place(x, y, "Apex Obelisk")
        m.place(CX, cy, heart)
    platforms = [(THRESHOLD[1], None)] + [(f[1], f) for f in FLOORS]
    for i, look in enumerate(WAYS):
        way(m, platforms[i][0], platforms[i + 1][0], *look)
    return m


def _edge_row(m, cy, north):
    """The floor's last square at x = CX going north (or south) from its middle, within 17."""
    rows = range(cy - 17, cy + 1) if north else range(cy + 17, cy - 1, -1)
    for y in rows:
        if m.ground[y][CX] is not None:
            return y
    raise AssertionError(("no floor at the middle", cy))


def way(m, low_cy, high_cy, look, course, width):
    """A way from the floor whose middle is row `low_cy` up (north) to the one at `high_cy`."""
    ground, rim, light, pillar = look
    kind, turns, swing = course
    start, end = _edge_row(m, low_cy, True), _edge_row(m, high_cy, False)
    a, b = low_cy - WAY_CLEAR, high_cy + WAY_CLEAR
    assert start > a > b > end, (start, a, b, end)

    def offset(y):
        if y >= a or y <= b:
            return 0.0
        t = (a - y) / (a - b)
        wave = math.sin(math.pi * turns * t)
        if kind == "zigzag":
            # straight runs, their corners just rounded off
            return swing * math.asin(0.97 * wave) / math.asin(0.97)
        return swing * math.sin(math.pi * turns * t)

    # the middle line, finely, and its length so far at each point
    line, run = [], 0.0
    y = start - 0.5
    while y > end + 0.5:
        x = CX + 0.5 + offset(y)
        if line:
            run += math.hypot(x - line[-1][0], y - line[-1][1])
        line.append((x, y, run))
        y -= 0.05
    squares = set()
    for (x, y, _) in line:
        for sy in range(int(y - width - 1), int(y + width + 2)):
            for sx in range(int(x - width - 1), int(x + width + 2)):
                if math.hypot(sx + 0.5 - x, sy + 0.5 - y) <= width and m.ground[sy][sx] is None:
                    squares.add((sx, sy))
    # ledges off its sides for the pillars, clear of its ends
    ledges = []
    side, next_at = 1, WAY_LEDGE_EVERY * 0.7
    for i, (x, y, r) in enumerate(line):
        if r < next_at or not (b + 2 < y < a - 2):
            continue
        next_at = r + WAY_LEDGE_EVERY
        j = min(i + 10, len(line) - 1)
        dx, dy = line[j][0] - x, line[j][1] - y
        n = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / n * side, dx / n * side
        px, py = x + nx * (width + 1.4), y + ny * (width + 1.4)
        ledges.append((int(px), int(py)))
        for sy in range(int(py) - 2, int(py) + 3):
            for sx in range(int(px) - 2, int(px) + 3):
                if math.hypot(sx + 0.5 - px, sy + 0.5 - py) <= 1.5 and m.ground[sy][sx] is None:
                    squares.add((sx, sy))
        side = -side
    for (x, y) in squares:
        assert 0 < x < W - 1, ("a way off the map", x, y)
        m.ground[y][x] = ground
        m.region[y][x] = "Crucible Way"
    for (x, y) in squares:
        if rim and any((x + ex, y + ey) not in squares and m.ground[y + ey][x + ex] is None
                       for ex, ey in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            m.ground[y][x] = rim
    # its lights down the middle
    k = WAY_LIGHT_EVERY
    for (x, y, r) in line:
        if r >= k:
            k += WAY_LIGHT_EVERY
            if (int(x), int(y)) in squares:
                m.ground[int(y)][int(x)] = light
    for (x, y) in ledges:
        if (x, y) in squares and m.obj[y][x] is None:
            m.place(x, y, pillar)
    m.ways.append([(x, y) for (x, y, _) in line])
    # it touches the floors only at its ends, never beside its bends
    for (x, y) in squares:
        if b < y < a:
            for ex, ey in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                g = m.ground[y + ey][x + ex]
                assert g is None or (x + ex, y + ey) in squares or m.region[y + ey][x + ex] == "Crucible Way", ("a way beside a floor", x, y)


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


def preview(m, out):
    """Each floor at 8 pixels a tile in its own ground's colours, and the sheets enlarged."""
    os.makedirs(out, exist_ok=True)
    colours = {name: int(c, 16) for name, (c, _) in GROUNDS.items()}
    img = Image.new("RGB", (W * 8, H * 8), (20, 14, 24))
    for y in range(H):
        for x in range(W):
            g = m.ground[y][x]
            if g:
                c = colours[g]
                img.paste(((c >> 16) & 255, (c >> 8) & 255, c & 255), (x * 8, y * 8, x * 8 + 8, y * 8 + 8))
            if m.obj[y][x]:
                img.paste((255, 210, 74), (x * 8 + 2, y * 8 + 2, x * 8 + 6, y * 8 + 6))
    img.save(os.path.join(out, "crucible_map.png"))
    for fname in (SHEET8[1], SHEET16[1], CHARS16[1], CHARS32[1]):
        sheet = Image.open(os.path.join(IMAGES, fname))
        k = 6 if "8x8" in fname else 4
        bg = Image.new("RGBA", sheet.size, (40, 36, 56, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.size[0] * k, sheet.size[1] * k), Image.NEAREST).save(os.path.join(out, fname))


def main():
    types = Types()
    s8, s16, s32 = Grid(8, 16), Grid(16, 8), Grid(32, 8)
    c16, c32 = Chars(16), Chars(32)
    m = build_map()
    g = grounds(types, s8)
    o = objects(types, s8, s16, s32, c16, c32)
    os.makedirs(IMAGES, exist_ok=True)
    s8.write(os.path.join(IMAGES, SHEET8[1]))
    s16.write(os.path.join(IMAGES, SHEET16[1]))
    s32.write(os.path.join(IMAGES, SHEET32[1]))
    c16.write(os.path.join(IMAGES, CHARS16[1]))
    c32.write(os.path.join(IMAGES, CHARS32[1]))
    write_xml(OUT_XML["grounds"], "GroundTypes", g,
              "Generated by tools/build_crucible.py: the Crucible's floors. Do not edit.")
    write_xml(OUT_XML["objects"], "Objects", o,
              "Generated by tools/build_crucible.py: the Crucible's champions, minions, scenery, "
              "portal, hearts and shots. Do not edit.")
    save_map(m)
    types.save()
    known = {e.get("id") for e in g + o} | {None}
    used = {t for row in m.ground for t in row} | {t for row in m.obj for t in row if t}
    assert used <= known, used - known
    print("wrote %s (%dx%d), %d grounds, %d objects" % (OUT_MAP, W, H, len(g), len(o)))
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        preview(m, out)
        print("preview in", out)


if __name__ == "__main__":
    main()
