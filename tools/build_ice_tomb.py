"""Builds the Ice Tomb: the rooms its random layout joins, and everything of its own (floors, walls,
monsters, the three Seal Keepers, the Frozen Court's three and their shots, scenery, pets).

    python tools/build_ice_tomb.py [--preview <folder>]

A tomb of the frozen dead under the Lost Lands. Every visit, its RandomLayout (Dungeons.xml) picks
some of the rooms drawn here and joins them anew (as the Tomb of the Ancients'); the spawn, the
three seal rooms and the Sealed Door are always in it, each seal room and the door ending a
branch. The rooms:

- the Crypt: rows of Frozen Sarcophagi that crack open as someone comes near, a Rimebound Mummy
  walking out of each;
- the Hall of Revenants: two rows of pillars, the old court's knights still standing guard and a
  Glacier Golem;
- the Frozen Lake: thin ice over black water. It cracks under whoever stands still on it and
  breaks into Frigid Water, freezing over again a while later; Rime Serpents wait under it;
- the Hound Den: snowdrifts and a pack of Frostfang Hounds;
- the Aurora Shrine: a ring of the aurora inlaid in the floor, wisps of it healing the tomb's dead;
- the Gargoyle Gallery: Icicle Gargoyles that never move, Frost Wraiths between them;
- the Chasm: a drop into the dark across the room. As someone comes to its edge, a bridge of ice
  freezes across it ahead of them, square by square (IceBridge);
- the Scarab Nest: ice crystals and a swarm of Glacial Scarabs.
- the three seal rooms: a Seal of Winter in each (Warden, Oracle, Reaver), warded while its keeper
  lives (the Rime Colossus, the Aurora Matron, the Frostfang Alpha);
- the Sealed Door: three Seal Lanterns, each lit as its seal breaks. With all three broken, the
  door's wall opens on the dark, and the Frozen Court assembles out of it (IceCourt.cs): a causeway
  of ice freezing out across the dark and a great round hall rising at its end, where Kaldrun,
  Ysolde and Hrimgar stand frozen round the Heart of Winter.

The tomb keeps dark round its layout for the Court (<RandomLayout margin="56">). Monsters and the
rest: BehaviorDb.IceTomb.cs. The rooms stand on a grid joined by long narrow halls only so the
server can tell them apart.

Outputs (generated, don't edit): the map, source/Shared/resources/worlds/Dungeons/Ice Tomb.jm;
IceTombGrounds.xml and IceTombObjects.xml in source/Shared/resources/xml/custom and the client's
custom/xmls; the sheets IceTomb8x8.png, IceTomb16x16.png, IceTombChars16x16.png and
IceTombChars32x32.png in the client's custom/images (registered for the Rust client by
tools/gen_rs_manifest.py). Art: tools/ice_tomb/art.py. Type codes stay in tools/ice_tomb/types.json.
Needs Pillow.
"""
import base64
import json
import math
import os
import random
import sys
import xml.etree.ElementTree as ET
import zlib

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ice_tomb import art  # noqa: E402
from black_between.small import pet_icon  # noqa: E402
from build_black_between import Grid, Chars, sub, texture, shot, foe, write_xml  # noqa: E402
import boss_pets  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XML_DIR = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
IMAGES = os.path.join(CLIENT, "custom", "images")
OUT_MAP = os.path.join(ROOT, "source", "Shared", "resources", "worlds", "Dungeons", "Ice Tomb.jm")
OUT_XML = {
    "grounds": [os.path.join(XML_DIR, "custom", "IceTombGrounds.xml"),
                os.path.join(CLIENT, "custom", "xmls", "IceTombGrounds.xml")],
    "objects": [os.path.join(XML_DIR, "custom", "IceTombObjects.xml"),
                os.path.join(CLIENT, "custom", "xmls", "IceTombObjects.xml")],
}
TYPES = os.path.join(ROOT, "tools", "ice_tomb", "types.json")
GROUND_TYPES = (0xCA00, 0xCA40)
OBJECT_TYPES = (0xCA40, 0xCC00)
SHEET8 = ("iceTomb8x8", "IceTomb8x8.png")
SHEET16 = ("iceTomb16x16", "IceTomb16x16.png")
CHARS16 = ("iceTombChars16x16", "IceTombChars16x16.png")
CHARS32 = ("iceTombChars32x32", "IceTombChars32x32.png")


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


# ------------------------------------------------------------------------------------------------
# grounds: name -> (minimap colour, extras)

FLOW = ("Flow", {"dx": ".25"})
WAVE = ("Wave", {"dx": "0.3", "dy": "0.3"})
GROUNDS = {
    "Ice Tomb Floor": ("0x4A6684", {}),
    "Ice Tomb Inlay": ("0x34496A", {}),
    "Rime Runner": ("0x26346A", {}),
    "Snowdrift": ("0xD6E2F0", {"Speed": 0.85}),
    "Court Marble": ("0xB8C6D8", {}),
    "Court Rim": ("0x5E7AA0", {}),
    "Court Sigil": ("0x3AC8A0", {}),
    "Lake Ice": ("0x4680AC", {}),
    # the lake about to break under you (harmless: get off it)
    "Cracking Ice": ("0x9CD0F0", {}),
    # broken through: it slows you to a crawl and the cold burns
    "Frigid Water": ("0x0E2444", {"Speed": 0.45, "Sink": None, "MinDamage": 45, "MaxDamage": 60, "Animate": FLOW}),
    "Rime Warning": ("0x8CC8EA", {}),
    # the Court's phases (each walkable, each burning)
    "Avalanche": ("0xF0F6FF", {"MinDamage": 75, "MaxDamage": 90}),
    "Aurora Light": ("0x4AE8A4", {"MinDamage": 70, "MaxDamage": 85}),
    "Blizzard": ("0xDCECF8", {"MinDamage": 65, "MaxDamage": 80, "Speed": 0.7, "Animate": FLOW}),
    "Frost Fissure": ("0x1A3A5E", {"MinDamage": 80, "MaxDamage": 95}),
    "Frozen Scar": ("0x2C4A6A", {}),
    "Polar Dark": ("0x1C1638", {"MinDamage": 80, "MaxDamage": 100}),
    "Permafrost": ("0xA8DCF6", {"MinDamage": 75, "MaxDamage": 90}),
    "Ice Floe": ("0xBCE0F4", {}),
    "Ice Floe Rim": ("0xE8F6FF", {}),
    "Ice Tomb Wall Floor": ("0x1A2436", {}),
    "Glacier Floor": ("0x44729A", {}),
    "Tomb Mosaic": ("0x4A6488", {}),
    "Bone Floor": ("0x4A5262", {}),
    "Tomb Abyss": ("0x04080E", {"NoWalk": None}),
    "Ice Bridge": ("0x8CC4E8", {}),
    "Ice Bridge Rim": ("0xDCF6FF", {}),
}


def grounds(types, sheet):
    tiles = art.tiles()
    out = []
    for name, (colour, extra) in GROUNDS.items():
        e = ET.Element("Ground", {"type": types.get(name, GROUND_TYPES), "id": name})
        rt = sub(e, "RandomTexture")
        for img in tiles[name]:
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
# monsters: name -> (health, defence, size, flags, [projectiles]). Flags: "quest" (a keeper, the
# quest arrow points at it), "static" (it never moves), "immune" (no stun, paralysis, petrify,
# slow or daze). The projectiles' order is the behaviours'.
SLOW = ("Slowed", 1.5)
WAVY = dict(amplitude=0.7, frequency=1.4)
RIBBON = dict(amplitude=1.0, frequency=1.1)

MINIONS = {
    "Rimebound Mummy": (3200, 20, 110, (), [
        shot(0, "Rime Shard", 95, 60, 1700, effects=[SLOW]),
        shot(1, "Tomb Snowflake", 70, 45, 2300)]),
    "Frost Wraith": (2200, 8, 100, (), [
        shot(0, "Wraith Wisp", 85, 70, 1900, effects=[("Darkness", 1)], **WAVY)]),
    "Glacial Scarab": (900, 25, 80, (), [
        shot(0, "Frost Fang", 65, 90, 450)]),
    "Frostfang Hound": (1600, 10, 100, (), [
        shot(0, "Frost Fang", 90, 110, 500, effects=[("Bleeding", 1.5)])]),
    "Glacier Golem": (7000, 35, 150, (), [
        shot(0, "Ice Boulder", 140, 45, 2400, 140),
        shot(1, "Rime Shard", 80, 70, 1500)]),
    "Icicle Gargoyle": (4000, 30, 120, ("static",), [
        shot(0, "Hailstone", 100, 55, 2600, 110),
        shot(1, "Tomb Snowflake", 60, 40, 3200)]),
    "Aurora Wisp": (1400, 0, 90, (), [
        shot(0, "Aurora Spark", 70, 80, 1600)]),
    "Frozen Revenant": (4500, 40, 110, (), [
        shot(0, "Glacial Lance", 120, 100, 900, 110),
        shot(1, "Steel Shard", 80, 60, 1300)]),
    # under the ice it can't be hurt; it surfaces to fire (AltTexture 1: below)
    "Rime Serpent": (3500, 15, 110, (), [
        shot(0, "Frost Bolt", 100, 65, 1900, effects=[("Slowed", 1)]),
        shot(1, "Tomb Snowflake", 60, 50, 2100)]),
}
# the three Seal Keepers (16x16, drawn large)
KEEPERS = {
    "Rime Colossus": (30000, 40, 210, ("quest", "immune"), [
        foe(0, "Ice Boulder", 160, 50, 2600, 140),
        foe(1, "Steel Shard", 115, 70, 1700, effects=[("Armor Broken", 2)]),
        foe(2, "Tomb Snowflake", 85, 45, 3000)]),
    "Aurora Matron": (24000, 20, 200, ("quest", "immune"), [
        foe(0, "Aurora Ribbon", 130, 70, 2100, **RIBBON),
        foe(1, "Violet Ribbon", 120, 60, 2300, effects=[("Quiet", 2)], **RIBBON),
        foe(2, "Aurora Spark", 95, 55, 2600)]),
    "Frostfang Alpha": (26000, 25, 200, ("quest", "immune"), [
        foe(0, "Frost Fang", 150, 120, 650, effects=[("Bleeding", 2)]),
        foe(1, "Rime Shard", 110, 80, 1500, effects=[SLOW]),
        foe(2, "Tomb Snowflake", 85, 45, 3000)]),
}
# the Frozen Court (32x32): name -> (art, alt arts, health, defence, display, [projectiles]).
# Health and defence here are the base: BossStats sets them in the fight.
COURT = {
    "Kaldrun, the Rime Warden": ("Kaldrun, the Rime Warden", ["Kaldrun, Permafrost", "Kaldrun Encased"], 90000, 55, None, [
        foe(0, "Glacial Lance", 220, 110, 1300, 130, effects=[("Armor Broken", 2)]),
        foe(1, "Steel Shard", 160, 60, 2600),
        foe(2, "Rime Shard", 180, 80, 2100, effects=[SLOW]),
        foe(3, "Ice Boulder", 200, 40, 3600, 150),
        foe(4, "Tomb Snowflake", 140, 48, 3400)]),
    "Ysolde, the Aurora Oracle": ("Ysolde, the Aurora Oracle", ["Ysolde, Polar Night", "Ysolde Encased"], 70000, 20, None, [
        foe(0, "Aurora Ribbon", 170, 80, 2300, **RIBBON),
        foe(1, "Violet Ribbon", 160, 70, 2500, effects=[("Quiet", 2)], **RIBBON),
        foe(2, "Aurora Spark", 150, 55, 3300),
        foe(3, "Frozen Soul", 190, 42, 3800, 120, effects=[("Darkness", 1.5)]),
        foe(4, "Tomb Snowflake", 120, 40, 3600)]),
    "Hrimgar, the Frost Reaver": ("Hrimgar, the Frost Reaver", ["Hrimgar, Shattered", "Hrimgar Encased"], 80000, 35, None, [
        foe(0, "Frost Axe", 240, 100, 950, 130, effects=[("Bleeding", 2)]),
        foe(1, "Frost Fang", 170, 120, 750),
        foe(2, "Hailstone", 190, 50, 2900, 120),
        foe(3, "Rime Shard", 170, 90, 1800, effects=[SLOW]),
        foe(4, "Tomb Snowflake", 130, 45, 3400)]),
    # the fallen's shades: invulnerable, each keeps one pattern of its own going for the rest of the
    # fight (they're the dead returned to help the living: XpMult 0, no loot)
    "Kaldrun's Shade": ("Kaldrun Shade", [], 100000, 0, "Kaldrun, the Rime Warden", [
        foe(0, "Frozen Soul", 150, 50, 3000, 110)]),
    "Ysolde's Shade": ("Ysolde Shade", [], 100000, 0, "Ysolde, the Aurora Oracle", [
        foe(0, "Frozen Soul", 140, 45, 3200, 110)]),
    "Hrimgar's Shade": ("Hrimgar Shade", [], 100000, 0, "Hrimgar, the Frost Reaver", [
        foe(0, "Frozen Soul", 150, 55, 2800, 110)]),
}
BOSSES = ("Kaldrun, the Rime Warden", "Ysolde, the Aurora Oracle", "Hrimgar, the Frost Reaver")
# who sounds like whom (existing sounds)
SOUNDS = {
    "Rimebound Mummy": "monster/mummies", "Frost Wraith": "monster/ghosts", "Glacial Scarab": "monster/scarabs",
    "Frostfang Hound": "monster/wolves", "Glacier Golem": "monster/stone_walls", "Icicle Gargoyle": "monster/stone_walls",
    "Aurora Wisp": "monster/ghosts", "Frozen Revenant": "monster/orcs", "Rime Serpent": "monster/snakes",
}

# the bosses' pets (tools/boss_pets.py): name -> (boss, [forms], spec); behaviours beside the
# bosses' in BehaviorDb.IceTomb.cs (change the numbers in both). Each boss drops its own, 1 in 100.
# Damage a second, as boss_pets.py counts it:
PETS = {
    # glacial lances that break armour, iron shard fans, his slam on the enemy and round it, and
    # every 10 s Permafrost: a spiral of snowflakes. 450 / 1.4 s + 3 x 160 / 1.2 s + 1,300 / 4 s
    # + 3 x 23/360 x 220 / 0.3 s x 0.4
    "Lil' Kaldrun": ("Kaldrun, the Rime Warden", ["Kaldrun, Permafrost"], dict(
        size=50, dps=1137,
        effects=["Glacial lances break armor for 2s", "Avalanche strikes all enemies within 2.5 tiles of its target",
                 "Permafrost: a spiral of snowflakes every 10s", "Makes you Armored for 2s every 10s"],
        description="The Rime Warden, glacier and all, frozen down to a size that follows you about. He still won't let anyone past.",
        shots=[
            boss_pets.shot(0, "Glacial Lance", 450, 110, 900, 110, effects=[("Armor Broken", 2)], multi=True),
            boss_pets.shot(1, "Steel Shard", 160, 90, 1000, 90),
            boss_pets.shot(2, "Tomb Snowflake", 220, 70, 1200, 100, multi=True),
        ])),
    # aurora ribbons, a turning ring of sparks (quieting), the aurora's heal on you, and every 10 s
    # Polar Night: a spiral of violet ribbons. 3 x 230 / 1.1 s + 8 x 23/360 x 260 / 2.4 s + 3 x 23/360 x 240 / 0.3 s x 0.4
    "Lil' Ysolde": ("Ysolde, the Aurora Oracle", ["Ysolde, Polar Night"], dict(
        size=50, dps=1004,
        effects=["Sparks Quiet for 1.5s", "Heals you a little more than other pets",
                 "Polar Night: a spiral of violet ribbons every 10s", "Makes you Healing for 2s every 10s"],
        description="The Aurora Oracle, veil and mantle, light enough to carry. The northern lights follow her still.",
        shots=[
            boss_pets.shot(0, "Aurora Ribbon", 230, 90, 1000, 100, multi=True, **RIBBON),
            boss_pets.shot(1, "Aurora Spark", 260, 60, 1500, 90, effects=[("Quiet", 1.5)], multi=True),
            boss_pets.shot(2, "Violet Ribbon", 240, 80, 1100, 100, multi=True, **RIBBON),
        ])),
    # thrown axes that bleed, fang volleys, a hail of stones on the enemy, and every 10 s the
    # Shattering: a spiral of axes. 2 x 380 / 1 s + 3 x 150 / 0.9 s + 1,200 / 3.5 s + 3 x 23/360 x 260 / 0.3 s x 0.4
    "Lil' Hrimgar": ("Hrimgar, the Frost Reaver", ["Hrimgar, Shattered"], dict(
        size=50, dps=1365,
        effects=["Axes make enemies Bleed for 2s", "Hailstorm strikes all enemies within 2 tiles of its target",
                 "The Shattering: a spiral of axes every 10s", "Makes you Berserk for 2s every 10s"],
        description="The Frost Reaver, both axes and the wolf's pelt, cut down to a pup's size. He is no calmer for it.",
        shots=[
            boss_pets.shot(0, "Frost Axe", 380, 100, 900, 110, effects=[("Bleeding", 2)], multi=True),
            boss_pets.shot(1, "Frost Fang", 150, 120, 700, 90),
            boss_pets.shot(2, "Frost Axe", 260, 90, 1000, 100, multi=True),
        ])),
}


def projectile(types, sheet, name, img, file=SHEET8[0], rotation=None):
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", "Projectile")
    texture(e, file, sheet.add(img))
    if rotation:
        sub(e, "Rotation", rotation)
    return e


def animated_static(types, sheet, name, frames, time, size=100, blocks=True, full=False, file=SHEET16[0],
                    cls="GameObject", extra=(), static=True):
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", cls)
    cells = [sheet.add(f) for f in frames]
    texture(e, file, cells[0])
    if len(cells) > 1:
        anim = sub(e, "Animation", prob="1.0")
        for idx in cells:
            texture(sub(anim, "Frame", time=time), file, idx)
    sub(e, "Size", size)
    if static:
        sub(e, "Static")
    if blocks:
        sub(e, "OccupySquare")
        sub(e, "EnemyOccupySquare")
    if full:
        sub(e, "FullOccupy")
    for tag in extra:
        sub(e, tag)
    return e, cells


def invisible(types, name):
    e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
    sub(e, "Class", "GameObject")
    texture(e, "invisible", 0)
    sub(e, "NoMiniMap")
    sub(e, "ShadowSize", 0)
    sub(e, "Size", 0)
    return e


def objects(types, s8, s16, c16, c32):
    out = []
    for name, fn in art.SHOTS.items():
        out.append(projectile(types, s8, name, fn(), rotation=360 if name in ("Frost Axe", "Tomb Snowflake") else None))

    # the walls: blocks of old ice, frosted on top; some with a sconce of blue flame
    for name, sconce in (("Ice Tomb Wall", False), ("Ice Tomb Sconce Wall", True), ("Glacier Wall", None)):
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Wall")
        if sconce is None:
            rt = sub(e, "RandomTexture")
            for k in range(3):
                texture(rt, SHEET8[0], s8.add(art.glacier_face(k)))
        elif sconce:
            texture(e, SHEET8[0], s8.add(art.wall_face(1, True, 0)))
        else:
            rt = sub(e, "RandomTexture")
            for k in range(3):
                texture(rt, SHEET8[0], s8.add(art.wall_face(k)))
        top = sub(e, "Top")
        texture(top, SHEET8[0], s8.add(art.wall_top(0)))
        sub(e, "HitSound", "monster/stone_walls_hit")
        sub(e, "DeathSound", "monster/stone_walls_death")
        for tag in ("Static", "FullOccupy", "OccupySquare", "EnemyOccupySquare", "BlocksSight"):
            sub(e, tag)
        out.append(e)

    # scenery
    for name, frames, time, size, blocks in (
            ("Frozen Pillar", [art.pillar(0)], None, 100, True),
            ("Frost Brazier", [art.brazier(f) for f in range(3)], "0.15", 110, True),
            ("Ice Crystals", [art.crystals(0), art.crystals(1)], "0.6", 100, False),
            ("Burial Urn", [art.urn()], None, 90, True),
            ("Frozen Statue", [art.statue()], None, 120, True),
            ("Bones", [art.bones()], None, 90, False),
            ("Icefall", [art.icefall(f) for f in range(3)], "0.3", 130, True),
            ("Warden's Throne", [art.throne("warden")], None, 140, True),
            ("Oracle's Throne", [art.throne("oracle")], None, 140, True),
            ("Reaver's Throne", [art.throne("reaver")], None, 140, True)):
        e, _ = animated_static(types, s16, name, frames, time, size, blocks)
        out.append(e)
    # walls of ice raised in the Court (Kaldrun's Glacial Bulwark)
    e, _ = animated_static(types, s16, "Court Ice Wall", [art.ice_wall(f) for f in range(3)], "0.25", 100, True, True)
    sub(e, "ShadowSize", 0)
    out.append(e)

    # the Seal Lanterns by the Sealed Door: dark, then lit (the map's square changes to the lit one)
    # as their seal breaks
    for who, name in (("warden", "Warden's Lantern"), ("oracle", "Oracle's Lantern"), ("reaver", "Reaver's Lantern")):
        e, _ = animated_static(types, s16, name, [art.lantern(who)], None, 110, True)
        out.append(e)
        e, _ = animated_static(types, s16, name + " Lit", [art.lantern(who, True, f) for f in (0, 1)], "0.3", 110, True)
        sub(e, "DisplayId", name)
        out.append(e)

    # the sarcophagi of the crypts (an entity, for its behaviour): sealed, cracking (1), open (2)
    e, _ = animated_static(types, s16, "Frozen Sarcophagus", [art.sarcophagus(0)], None, 120, True, static=False)
    for k in (1, 2):
        texture(sub(e, "AltTexture", id=k), SHEET16[0], s16.add(art.sarcophagus(k)))
    out.append(e)

    # the chasms' mist: invisible, on every square of a chasm, so the layout keeps both sides of it
    # one room (<RandomLayout keep>); the bridge clears it where it freezes
    out.append(invisible(types, "Chasm Mist"))
    sub(out[-1], "Static")
    # the behaviours' hosts: the lake's, the chasm's, the Sealed Door's (a Goal), the Court's
    for name in ("Lake Heart", "Chasm Heart", "Sealed Door", "Court Heart"):
        out.append(invisible(types, name))

    def character(name, sheet, row, size, hp, defense, shots, flags=(), alts=(), display=None, sound=None):
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        if display:
            sub(e, "DisplayId", display)
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, sheet, row, "AnimatedTexture")
        for k, alt_row in enumerate(alts, start=1):
            texture(sub(e, "AltTexture", id=k), sheet, alt_row, "AnimatedTexture")
        sub(e, "HitSound", (sound or "monster/ghost_god") + "_hit")
        sub(e, "DeathSound", (sound or "monster/ghost_god") + "_death")
        sub(e, "Size", size)
        sub(e, "MaxHitPoints", hp)
        sub(e, "Defense", defense)
        if "quest" in flags:
            sub(e, "Quest")
            sub(e, "Level", 20)
            sub(e, "ShowName")
        if "god" in flags:
            sub(e, "God")
            sub(e, "NoArticle")
        if "static" in flags:
            sub(e, "Static")
        sub(e, "StasisImmune")
        if "immune" in flags:
            for flag in ("StunImmune", "ParalyzeImmune", "PetrifyImmune", "SlowedImmune", "DazedImmune"):
                sub(e, flag)
        if "noxp" in flags:
            sub(e, "XpMult", 0)
        for p in shots:
            e.append(p)
        return e

    # the tomb's dead
    art16 = art.minions()
    rows16 = {name: c16.add(frames, attack) for name, (frames, attack) in art16.items()}
    for name, (hp, defense, size, flags, shots) in MINIONS.items():
        alts = [rows16["Rime Serpent Below"]] if name == "Rime Serpent" else []
        e = character(name, CHARS16[0], rows16[name], size, hp, defense, shots, flags, alts, sound=SOUNDS.get(name))
        # the Aurora Wisps mend the tomb's dead (HealGroup)
        sub(e, "Group", "Ice Tomb Dead")
        out.append(e)
    for name, (hp, defense, size, flags, shots) in KEEPERS.items():
        out.append(character(name, CHARS16[0], rows16[name], size, hp, defense, shots, flags))

    # breakables: the seals (warded 0, bare 1, cracked 2), the Rime Pillars, the Heart of Winter
    seal_cells = [s16.add(art.seal(0, f)) for f in range(4)]
    for name in ("Seal of the Warden", "Seal of the Oracle", "Seal of the Reaver"):
        e = ET.Element("Object", {"type": types.get(name, OBJECT_TYPES), "id": name})
        sub(e, "Class", "Character")
        sub(e, "Enemy")
        texture(e, SHEET16[0], seal_cells[0])
        anim = sub(e, "Animation", prob="1.0")
        for idx in seal_cells:
            texture(sub(anim, "Frame", time="0.2"), SHEET16[0], idx)
        for k in (1, 2):
            texture(sub(e, "AltTexture", id=k), SHEET16[0], s16.add(art.seal(k)))
        for tag, v in (("Size", 150), ("MaxHitPoints", 12000), ("Defense", 0)):
            sub(e, tag, v)
        for tag in ("Static", "StasisImmune", "OccupySquare", "EnemyOccupySquare"):
            sub(e, tag)
        sub(e, "XpMult", 0)
        sub(e, "HitSound", "monster/stone_walls_hit")
        sub(e, "DeathSound", "monster/stone_walls_death")
        out.append(e)
    e = ET.Element("Object", {"type": types.get("Rime Pillar", OBJECT_TYPES), "id": "Rime Pillar"})
    sub(e, "Class", "Character")
    sub(e, "Enemy")
    texture(e, SHEET16[0], s16.add(art.rime_pillar(0)))
    for tag, v in (("Size", 130), ("MaxHitPoints", 6000), ("Defense", 0), ("XpMult", 0)):
        sub(e, tag, v)
    for tag in ("Static", "StasisImmune"):
        sub(e, tag)
    sub(e, "HitSound", "monster/stone_walls_hit")
    sub(e, "DeathSound", "monster/stone_walls_death")
    out.append(e)
    e = ET.Element("Object", {"type": types.get("Heart of Winter", OBJECT_TYPES), "id": "Heart of Winter"})
    sub(e, "Class", "Character")
    sub(e, "Enemy")
    heart = [s16.add(art.heart_of_winter(0, f)) for f in (0, 1)]
    texture(e, SHEET16[0], heart[0])
    anim = sub(e, "Animation", prob="1.0")
    for idx in heart:
        texture(sub(anim, "Frame", time="0.4"), SHEET16[0], idx)
    for k in range(1, 6):
        texture(sub(e, "AltTexture", id=k), SHEET16[0], s16.add(art.heart_of_winter(k)))
    for tag, v in (("Size", 170), ("MaxHitPoints", 20000), ("Defense", 0), ("XpMult", 0)):
        sub(e, tag, v)
    for tag in ("Static", "StasisImmune", "ShowName"):
        sub(e, tag)
    sub(e, "HitSound", "monster/stone_walls_hit")
    sub(e, "DeathSound", "monster/stone_walls_death")
    out.append(e)

    # the Frozen Court
    sprites = art.court_sprites()
    for b in BOSSES:
        sprites[b.split(",")[0] + " Encased"] = [art.encased(sprites[b][0])] * 4
    sprites["Kaldrun Shade"] = art.spectral_court(sprites["Kaldrun, the Rime Warden"], (170, 200, 255))
    sprites["Ysolde Shade"] = art.spectral_court(sprites["Ysolde, the Aurora Oracle"], (170, 255, 220))
    sprites["Hrimgar Shade"] = art.spectral_court(sprites["Hrimgar, the Frost Reaver"], (200, 235, 255))
    rows = {}

    def row(a):
        if a not in rows:
            fr = sprites[a]
            rows[a] = c32.add(fr[:3], fr[3])
        return rows[a]

    for name, (a, alts, hp, defense, display, shots) in COURT.items():
        boss = name in BOSSES
        flags = ("quest", "god", "immune") if boss else ("noxp", "immune")
        out.append(character(name, CHARS32[0], row(a), 100 if boss else 90, hp, defense, shots, flags,
                             [row(x) for x in alts], display))

    # the bosses' pets and their generators
    for name, (boss, forms, spec) in PETS.items():
        icon = s16.add(pet_icon(sprites[boss][0]))
        out += boss_pets.pet_objects(lambda n: types.get(n, OBJECT_TYPES), name, spec, CHARS32[0], row(boss),
                                     [row(f) for f in forms], SHEET16[0], icon)
    return out


# ------------------------------------------------------------------------------------------------
# the map

CELL, COLS, ROWS = 44, 4, 5
W, H = CELL * COLS, CELL * ROWS
HALL = 1.5   # half the width of the halls between cells (3 tiles)

FLOOR, INLAY, RUNNER, DRIFT = "Ice Tomb Floor", "Ice Tomb Inlay", "Rime Runner", "Snowdrift"
MARBLE, RIM, SIGIL = "Court Marble", "Court Rim", "Court Sigil"
GLACIER, MOSAIC, BONEF = "Glacier Floor", "Tomb Mosaic", "Bone Floor"
LAKE, ABYSS, WALL_GROUND = "Lake Ice", "Tomb Abyss", "Ice Tomb Wall Floor"
WALL, SCONCE, ICEWALL = "Ice Tomb Wall", "Ice Tomb Sconce Wall", "Glacier Wall"
WALKABLE = (FLOOR, INLAY, RUNNER, DRIFT, MARBLE, RIM, SIGIL, LAKE, GLACIER, MOSAIC, BONEF)
CAVE_FLOORS = (GLACIER, DRIFT, LAKE)

START = (1, 4)
DOOR = (2, 0)
SEALS = {(0, 0): ("Seal of the Warden", "Rime Colossus", "fortress"),
         (3, 1): ("Seal of the Oracle", "Aurora Matron", "rings"),
         (0, 3): ("Seal of the Reaver", "Frostfang Alpha", "lair")}
KINDS = ["crypt", "lake", "chasm", "shrine", "den", "gallery", "revenants", "nest", "crevasse", "ossuary",
         "crypt", "chasm", "lake", "gallery", "den", "shrine"]


class Map:
    def __init__(self):
        self.ground = [[None] * W for _ in range(H)]
        self.obj = [[None] * W for _ in range(H)]
        self.region = [[None] * W for _ in range(H)]

    def put(self, x, y, ground, only=None):
        x, y = int(x), int(y)
        if 0 <= x < W and 0 <= y < H and (only is None or self.ground[y][x] in only):
            self.ground[y][x] = ground

    def disc(self, cx, cy, r, ground, only=None):
        for y in range(max(0, int(cy - r - 1)), min(H, int(cy + r + 2))):
            for x in range(max(0, int(cx - r - 1)), min(W, int(cx + r + 2))):
                if math.hypot(x - cx, y - cy) <= r and (only is None or self.ground[y][x] in only):
                    self.ground[y][x] = ground

    def rect(self, x0, y0, x1, y1, ground, only=None):
        for y in range(max(0, int(y0)), min(H, int(y1) + 1)):
            for x in range(max(0, int(x0)), min(W, int(x1) + 1)):
                if only is None or self.ground[y][x] in only:
                    self.ground[y][x] = ground

    def polygon(self, cx, cy, r, sides, ground, turn=0.0, only=None):
        seg = math.pi * 2 / sides
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                a = (math.atan2(y - cy, x - cx) - turn) % seg - seg / 2
                if math.hypot(x - cx, y - cy) * math.cos(a) <= r * math.cos(seg / 2):
                    self.put(x, y, ground, only)

    def blob(self, rnd, cx, cy, r, ground, rough=0.22, only=None):
        """An organic shape: a disc whose edge wanders (a few sine waves of random phase)."""
        waves = [(rnd.randint(2, 6), rnd.random() * math.tau, rnd.uniform(0.3, 1.0)) for _ in range(3)]
        norm = sum(a for _, _, a in waves)
        for y in range(int(cy - r * 1.4 - 1), int(cy + r * 1.4 + 2)):
            for x in range(int(cx - r * 1.4 - 1), int(cx + r * 1.4 + 2)):
                ang = math.atan2(y - cy, x - cx)
                k = 1 + rough * sum(a * math.sin(f * ang + p) for f, p, a in waves) / norm
                if math.hypot(x - cx, y - cy) <= r * k:
                    self.put(x, y, ground, only)

    def hall(self, a, b, ground):
        (x0, y0), (x1, y1) = a, b
        n = int(math.hypot(x1 - x0, y1 - y0) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for yy in range(int(y - HALL), int(y + HALL) + 1):
                for xx in range(int(x - HALL), int(x + HALL) + 1):
                    if abs(xx - x) <= HALL and abs(yy - y) <= HALL and self.ground[yy][xx] is None:
                        self.ground[yy][xx] = ground

    def walls(self):
        """Wall round every floor: glacier ice round the caves, the tomb's dressed blocks round the
        rest (a sconce of blue flame now and then on a wall that faces a room from the north)."""
        found = []
        for y in range(H):
            for x in range(W):
                if self.ground[y][x] is not None:
                    continue
                near = [self.ground[y + dy][x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                        if 0 <= x + dx < W and 0 <= y + dy < H and self.ground[y + dy][x + dx] not in (None, ABYSS)]
                if near:
                    found.append((x, y, sum(g in CAVE_FLOORS for g in near) * 2 > len(near)))
        for x, y, cave in found:
            self.ground[y][x] = WALL_GROUND
            south = y + 1 < H and self.ground[y + 1][x] in WALKABLE
            if cave:
                self.obj[y][x] = ICEWALL
            else:
                self.obj[y][x] = SCONCE if south and (x * 7 + y * 3) % 9 == 0 else WALL

    def free(self, x, y, grounds=WALKABLE):
        x, y = int(round(x)), int(round(y))
        return 0 <= x < W and 0 <= y < H and self.ground[y][x] in grounds and self.obj[y][x] is None \
            and self.region[y][x] is None

    def place(self, x, y, obj, grounds=WALKABLE):
        x, y = int(round(x)), int(round(y))
        assert self.free(x, y, grounds), (x, y, obj, self.ground[y][x], self.obj[y][x])
        self.obj[y][x] = obj

    def try_place(self, x, y, obj, grounds=WALKABLE, clear=0):
        """Place it if the spot is free (and, with `clear`, walkable floor all round it that far, so
        nothing that blocks walls off a way through)."""
        if not self.free(x, y, grounds):
            return False
        xi, yi = int(round(x)), int(round(y))
        for dy in range(-clear, clear + 1):
            for dx in range(-clear, clear + 1):
                if not (0 <= xi + dx < W and 0 <= yi + dy < H) or self.ground[yi + dy][xi + dx] not in WALKABLE:
                    return False
        self.place(x, y, obj, grounds)
        return True


def centre(cell):
    return cell[0] * CELL + CELL // 2, cell[1] * CELL + CELL // 2


def scatter(m, rnd, cx, cy, r, monsters, grounds=WALKABLE):
    """Monsters round a spot, each on the first free square of a few tried."""
    for monster in monsters:
        for _ in range(60):
            a = rnd.random() * math.tau
            d = r * rnd.uniform(0.1, 0.8)
            if m.try_place(cx + math.cos(a) * d, cy + math.sin(a) * d, monster, grounds):
                break


def ring(m, cx, cy, r, n, obj, turn=0.0, clear=1):
    for k in range(n):
        a = turn + k * math.tau / n
        m.try_place(cx + math.cos(a) * r, cy + math.sin(a) * r, obj, clear=clear)


# Each room: carve(m, rnd, cx, cy) lays its floor (before the walls) and returns furnish(), which
# puts its things in (after). Every room's narrowest way through is wider than the layout's
# `passage` (4), so the layout takes it as one room.

def crypt(m, rnd, cx, cy):
    """The Crypt: a cross of a nave and a transept, a mosaic floor, an altar where they meet;
    sarcophagi stand along the nave and at the arms' ends."""
    flip = rnd.random() < 0.5
    L, nw, T, tw = 15, 4, 10, 3       # half-lengths and half-widths

    def R(x0, y0, x1, y1, g):
        if flip:
            m.rect(cx + y0, cy + x0, cx + y1, cy + x1, g)
        else:
            m.rect(cx + x0, cy + y0, cx + x1, cy + y1, g)
    R(-L, -nw, L, nw, MOSAIC)
    R(-tw, -T, tw, T, MOSAIC)
    R(-L, -1, L, 1, RUNNER)
    m.disc(cx, cy, 2.5, SIGIL)

    def at(a, b):
        return (cx + b, cy + a) if flip else (cx + a, cy + b)

    def furnish():
        m.place(*at(0, 0), "Burial Urn")
        for a in range(-L + 2, L - 1, 3):
            if abs(a) <= tw + 1:
                continue
            m.try_place(*at(a, -nw), "Frozen Sarcophagus")
            m.try_place(*at(a, nw), "Frozen Sarcophagus")
        for b in (-T, T):
            m.try_place(*at(-1, b), "Frozen Sarcophagus")
            m.try_place(*at(1, b), "Frozen Sarcophagus")
        for a in (-L, L):
            m.try_place(*at(a, -nw), "Frost Brazier")
            m.try_place(*at(a, nw), "Frost Brazier")
        scatter(m, rnd, *at(0, 0), 4, ["Frost Wraith"])
    return furnish


def revenants(m, rnd, cx, cy):
    """The Hall of Revenants: a long hall ending in an apse, lined with knights frozen in ice;
    those that still move stand guard in the apse."""
    side = rnd.choice((-1, 1))
    m.rect(cx - 14, cy - 5, cx + 8, cy + 5, MOSAIC)
    m.disc(cx + 8, cy, 7.5, MOSAIC)
    if side < 0:   # mirror: the apse at the west end
        for y in range(cy - 9, cy + 10):
            row = m.ground[y][cx - 16:cx + 17]
            m.ground[y][cx - 16:cx + 17] = row[::-1]
    m.rect(cx - 14, cy - 1, cx + 14, cy + 1, RUNNER, only=(MOSAIC,))
    ax = cx + 9 * side
    m.disc(ax, cy, 3.5, INLAY)

    def furnish():
        for x in range(cx - 12, cx + 6):
            xx = x if side > 0 else 2 * cx - x
            if (x - cx) % 3 == 0:
                m.try_place(xx, cy - 4, "Frozen Statue")
                m.try_place(xx, cy + 4, "Frozen Statue")
        for k in (-1, 1):
            m.try_place(ax + 3 * side, cy + 5 * k, "Frost Brazier")
        scatter(m, rnd, ax, cy, 4, ["Frozen Revenant", "Frozen Revenant", "Glacier Golem"])
        scatter(m, rnd, cx - 4 * side, cy, 4, ["Frozen Revenant"])
    return furnish


def lake(m, rnd, cx, cy):
    """The Frozen Lake: a cave round a lake of thin ice, an island or two, snow on its shores and a
    waterfall frozen as it fell. The ice cracks under whoever stands still on it."""
    m.blob(rnd, cx, cy, 15, GLACIER, rough=0.18)
    m.blob(rnd, cx, cy, 11, LAKE, rough=0.25)
    isles = []
    for _ in range(rnd.randint(1, 2)):
        a = rnd.random() * math.tau
        ix, iy = cx + math.cos(a) * 5.5, cy + math.sin(a) * 5.5
        m.disc(ix, iy, 2.2, GLACIER, only=(LAKE,))
        isles.append((ix, iy))
    for _ in range(4):
        a = rnd.random() * math.tau
        m.disc(cx + math.cos(a) * 13, cy + math.sin(a) * 13, rnd.uniform(1.5, 2.8), DRIFT, only=(GLACIER,))

    def furnish():
        m.place(cx, cy, "Lake Heart", (LAKE,))
        for (ix, iy) in isles:
            m.try_place(ix, iy, "Ice Crystals", (GLACIER,))
        # the icefall against the lake's northern shore
        for x in range(cx - 4, cx + 5):
            for y in range(cy - 19, cy):
                if m.ground[y][x] == GLACIER and m.ground[y - 1][x] in (None, WALL_GROUND):
                    if x % 2 == 0:
                        m.try_place(x, y, "Icefall", (GLACIER,))
                    break
        for k in range(3):
            a = rnd.random() * math.tau
            m.try_place(cx + math.cos(a) * 6, cy + math.sin(a) * 6, "Rime Serpent", (LAKE,))
    return furnish


def den(m, rnd, cx, cy):
    """The Hound Den: a cave of three lobes, deep in snow, the pack's leavings about."""
    lobes = [(cx + math.cos(a) * 6, cy + math.sin(a) * 6) for a in [rnd.random() * math.tau + k * math.tau / 3 for k in range(3)]]
    m.blob(rnd, cx, cy, 7, GLACIER, rough=0.3)
    for (lx, ly) in lobes:
        m.blob(rnd, lx, ly, rnd.uniform(6, 8), GLACIER, rough=0.3)
    for (lx, ly) in lobes:
        m.blob(rnd, lx, ly, 3.2, DRIFT, rough=0.4, only=(GLACIER,))

    def furnish():
        scatter(m, rnd, cx, cy, 6, ["Frostfang Hound"] * 5)
        for (lx, ly) in lobes:
            for _ in range(2):
                m.try_place(lx + rnd.randint(-3, 3), ly + rnd.randint(-3, 3), "Bones")
    return furnish


def shrine(m, rnd, cx, cy):
    """The Aurora Shrine: an octagon with the aurora's eight-pointed star laid in its floor, a ring
    of pillars, crystals at its heart; wisps of the aurora tend the tomb's dead."""
    m.polygon(cx, cy, 12, 8, MOSAIC, turn=math.pi / 8)
    m.polygon(cx, cy, 7, 4, SIGIL)
    m.polygon(cx, cy, 7, 4, SIGIL, turn=math.pi / 4)
    m.polygon(cx, cy, 4.5, 8, INLAY, turn=math.pi / 8)

    def furnish():
        m.place(cx, cy, "Ice Crystals")
        ring(m, cx, cy, 9.5, 8, "Frozen Pillar", turn=math.pi / 8)
        ring(m, cx, cy, 5.5, 4, "Frost Brazier", turn=math.pi / 4, clear=0)
        scatter(m, rnd, cx, cy, 8, ["Aurora Wisp"] * 4 + ["Frost Wraith"])
    return furnish


def gallery(m, rnd, cx, cy):
    """The Gargoyle Gallery: a hall turned on its corner round a pit into the dark, gargoyles
    crouched at its points and at the pit's lip."""
    m.polygon(cx, cy, 13, 4, FLOOR)
    m.polygon(cx, cy, 9, 4, INLAY)
    m.polygon(cx, cy, 4.5, 4, ABYSS)

    def furnish():
        for (dx, dy) in ((0, -11), (11, 0), (0, 11), (-11, 0)):
            m.try_place(cx + dx, cy + dy, "Icicle Gargoyle")
        for (dx, dy) in ((0, -7), (7, 0), (0, 7), (-7, 0)):
            m.try_place(cx + dx, cy + dy, "Frost Brazier")
        scatter(m, rnd, cx, cy, 10, ["Frost Wraith", "Frost Wraith", "Frost Wraith"])
    return furnish


def chasm(m, rnd, cx, cy, across=True):
    """The Chasm: two cliffs of the glacier with a drop into the dark between, wider in places
    than others; the Chasm Heart in its middle freezes a bridge across as someone comes to either
    edge. The drop stops short of the room's sides, so the layout never puts a door into it (and no
    hall comes in along it)."""
    hw = 13
    m.blob(rnd, cx, cy, 14, GLACIER, rough=0.1)
    phase = rnd.random() * math.tau
    for y in range(cy - 20, cy + 21):
        for x in range(cx - 20, cx + 21):
            off, side = (y - cy, x - cx) if across else (x - cx, y - cy)
            half = 3 + 1.4 * math.sin(side * 0.35 + phase) + 0.6 * math.sin(side * 0.9 + phase * 2)
            half = max(half, 3 if abs(side) <= 2 else 1.5)   # the bridge's lane is always 7 deep
            if abs(off) <= half and m.ground[y][x] is not None:
                m.ground[y][x] = ABYSS if abs(side) <= hw - 4 else None
    for k in (-1, 1):
        sx, sy = (cx, cy + k * 9) if across else (cx + k * 9, cy)
        m.disc(sx, sy, 3, FLOOR, only=(GLACIER,))

    def furnish():
        for y in range(cy - 20, cy + 21):
            for x in range(cx - 20, cx + 21):
                if m.ground[y][x] == ABYSS and not (x == cx and y == cy):
                    m.obj[y][x] = "Chasm Mist"
        m.obj[cy][cx] = "Chasm Heart"
        for k in (-1, 1):
            sx, sy = (cx, cy + k * 9) if across else (cx + k * 9, cy)
            scatter(m, rnd, sx, sy, 5, ["Glacial Scarab", "Glacial Scarab", "Icicle Gargoyle"])
            for j in (-1, 1):
                m.try_place(sx + (j * 3 if across else 0), sy + (0 if across else j * 3), "Frost Brazier")
    return furnish


def nest(m, rnd, cx, cy):
    """The Scarab Nest: chambers of glacier ice running into each other like a honeycomb, crystals
    growing out of their walls."""
    cells = [(cx, cy)]
    for k in range(6):
        a = k * math.tau / 6 + rnd.uniform(-0.2, 0.2)
        if rnd.random() < 0.8:
            cells.append((cx + math.cos(a) * 8, cy + math.sin(a) * 8))
    for (x, y) in cells:
        m.blob(rnd, x, y, rnd.uniform(4.8, 5.6), GLACIER, rough=0.15)
    m.disc(cx, cy, 2.5, INLAY)

    def furnish():
        for (x, y) in cells[1:]:
            a = math.atan2(y - cy, x - cx)
            m.try_place(x + math.cos(a) * 3.5, y + math.sin(a) * 3.5, "Ice Crystals", (GLACIER,))
        scatter(m, rnd, cx, cy, 10, ["Glacial Scarab"] * 7 + ["Glacier Golem"])
    return furnish


def crevasse(m, rnd, cx, cy):
    """The Crevasse: a winding crack in the glacier, wide and narrow by turns, icefalls along it."""
    a = rnd.random() * math.tau
    p0 = (cx + math.cos(a) * 14, cy + math.sin(a) * 14)
    p2 = (cx - math.cos(a) * 14, cy - math.sin(a) * 14)
    p1 = (cx + math.cos(a + math.pi / 2) * 9, cy + math.sin(a + math.pi / 2) * 9)
    pts = []
    for i in range(29):
        t = i / 28
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]
        r = 3.6 + 1.6 * math.sin(t * 9 + a)
        m.disc(x, y, r, GLACIER)
        pts.append((x, y, r))
    for (x, y, r) in pts[::7]:
        m.disc(x, y, r * 0.6, DRIFT, only=(GLACIER,))

    def furnish():
        for (x, y, r) in pts[3::6]:
            m.try_place(x + r - 1, y, "Icefall", (GLACIER, DRIFT))
        for (x, y, r) in pts[::9]:
            m.try_place(x, y - r + 1, "Ice Crystals", (GLACIER, DRIFT))
        x, y, _ = pts[14]
        scatter(m, rnd, x, y, 4, ["Glacier Golem", "Frost Wraith"])
        x, y, _ = pts[5]
        scatter(m, rnd, x, y, 3, ["Frost Wraith"])
        x, y, _ = pts[23]
        scatter(m, rnd, x, y, 3, ["Frost Wraith", "Glacial Scarab"])
    return furnish


def ossuary(m, rnd, cx, cy):
    """The Ossuary: a ring of a room round a sealed block of the dead, its floor laid with bones;
    the sarcophagi in its corners walk."""
    m.rect(cx - 12, cy - 12, cx + 12, cy + 12, BONEF)
    m.rect(cx - 5, cy - 5, cx + 5, cy + 5, None)   # the sealed block (walled by walls())
    for k in range(4):
        m.disc(cx + (12 if k % 2 else -12), cy + (12 if k // 2 else -12), 3, None)

    def furnish():
        for (dx, dy) in ((-8, -8), (8, -8), (-8, 8), (8, 8)):
            m.try_place(cx + dx, cy + dy, "Frozen Sarcophagus")
        for _ in range(10):
            m.try_place(cx + rnd.randint(-11, 11), cy + rnd.randint(-11, 11), "Bones")
        for (dx, dy) in ((0, -9), (9, 0), (0, 9), (-9, 0)):
            m.try_place(cx + dx, cy + dy, "Burial Urn", clear=1)
        scatter(m, rnd, cx, cy - 9, 3, ["Rimebound Mummy"])
        scatter(m, rnd, cx, cy + 9, 3, ["Rimebound Mummy", "Frost Wraith"])
    return furnish


ROOMS = {"crypt": crypt, "revenants": revenants, "lake": lake, "den": den, "shrine": shrine,
         "gallery": gallery, "nest": nest, "crevasse": crevasse, "ossuary": ossuary}


def seal_room(m, rnd, cx, cy, seal, keeper, style):
    """A seal room, each after its Court member: the Warden's a square keep of mosaic with
    statues in rows; the Oracle's round, the aurora in rings about the seal; the Reaver's a lair in
    the glacier, deep in snow and bones."""
    if style == "fortress":
        m.rect(cx - 12, cy - 12, cx + 12, cy + 12, MOSAIC)
        for (dx, dy) in ((-12, -12), (12, -12), (-12, 12), (12, 12)):
            m.rect(cx + dx - 2, cy + dy - 2, cx + dx + 2, cy + dy + 2, None)
        m.rect(cx - 10, cy - 10, cx + 10, cy + 10, RIM, only=(MOSAIC,))
        m.rect(cx - 9, cy - 9, cx + 9, cy + 9, MOSAIC)
        m.rect(cx - 3, cy - 3, cx + 3, cy + 3, INLAY)
    elif style == "rings":
        m.disc(cx, cy, 12.5, MARBLE)
        for r in (4.5, 8.5):
            for y in range(cy - 13, cy + 14):
                for x in range(cx - 13, cx + 14):
                    if abs(math.hypot(x - cx, y - cy) - r) <= 0.6:
                        m.put(x, y, SIGIL)
        m.disc(cx, cy, 2.5, INLAY)
    else:
        m.blob(rnd, cx, cy, 13, GLACIER, rough=0.25)
        for _ in range(5):
            a = rnd.random() * math.tau
            m.disc(cx + math.cos(a) * 9, cy + math.sin(a) * 9, rnd.uniform(2, 3.5), DRIFT, only=(GLACIER,))
        m.disc(cx, cy, 3, BONEF)

    def furnish():
        m.place(cx, cy, seal)
        m.place(cx, cy + 6, keeper)
        if style == "fortress":
            for x in range(cx - 7, cx + 8, 7):
                for y in (cy - 7, cy + 7):
                    m.try_place(x, y, "Frozen Statue")
            for (dx, dy) in ((-6, 0), (6, 0)):
                m.try_place(cx + dx, cy + dy, "Frost Brazier")
            scatter(m, rnd, cx, cy, 8, ["Frozen Revenant", "Frozen Revenant"])
        elif style == "rings":
            ring(m, cx, cy, 10.5, 8, "Ice Crystals", clear=0)
            ring(m, cx, cy, 6.5, 4, "Frost Brazier", turn=math.pi / 4, clear=0)
            scatter(m, rnd, cx, cy, 8, ["Aurora Wisp", "Aurora Wisp", "Frost Wraith"])
        else:
            for _ in range(8):
                m.try_place(cx + rnd.randint(-9, 9), cy + rnd.randint(-9, 9), "Bones", (GLACIER, DRIFT))
            scatter(m, rnd, cx, cy, 9, ["Frostfang Hound", "Frostfang Hound", "Frostfang Hound"])
    return furnish


def door_room(m, cx, cy):
    """The Sealed Door: a great hall, its three Seal Lanterns along the far wall, dark until their
    seals break; statues of the old court line the runner. With all three broken the hall's wall
    opens on the dark (IceCourt.cs)."""
    hw, hh = 12, 9
    m.rect(cx - hw, cy - hh, cx + hw, cy + hh, MOSAIC)
    m.rect(cx - hw + 2, cy - hh + 2, cx + hw - 2, cy + hh - 2, MARBLE)
    m.rect(cx - 1, cy - hh, cx + 1, cy + hh, RUNNER)
    m.disc(cx, cy, 3, SIGIL)

    def furnish():
        m.place(cx, cy, "Sealed Door")
        for k, name in enumerate(("Warden's Lantern", "Oracle's Lantern", "Reaver's Lantern")):
            m.place(cx - 6 + k * 6, cy - hh + 1, name)
        for (x, y) in ((cx - hw + 1, cy - hh + 1), (cx + hw - 1, cy - hh + 1), (cx - hw + 1, cy + hh - 1), (cx + hw - 1, cy + hh - 1)):
            m.place(x, y, "Frost Brazier")
        for y in range(cy - hh + 4, cy + hh - 1, 4):
            m.try_place(cx - 4, y, "Frozen Statue")
            m.try_place(cx + 4, y, "Frozen Statue")
    return furnish


def stair(m, cx, cy):
    """The Frozen Stair, where the tomb is entered: a hall stepping down, band by band, wider at
    each landing; returns where players arrive."""
    for k in range(7):
        y0 = cy + 9 - k * 3
        m.rect(cx - 5 - k // 2, y0 - 2, cx + 5 + k // 2, y0, FLOOR if k % 2 else INLAY)
    m.rect(cx - 1, cy - 11, cx + 1, cy + 9, RUNNER)
    return cx, cy + 6


def build_map():
    m = Map()
    rnd = random.Random(4711)
    cells = [(c, r) for r in range(ROWS) for c in range(COLS)]
    fillers = [c for c in cells if c not in (START, DOOR) and c not in SEALS]
    kinds = {cell: KINDS[i % len(KINDS)] for i, cell in enumerate(fillers)}
    across = {}
    for i, cell in enumerate(c for c in fillers if kinds[c] == "chasm"):
        across[cell] = i % 2 == 0

    def joins(a, b):
        # no hall comes into a chasm room along its drop
        horizontal = a[1] == b[1]
        return all(not (c in across and across[c] == horizontal) for c in (a, b))
    for r in range(ROWS):
        for c in range(COLS - 1):
            if joins((c, r), (c + 1, r)):
                m.hall(centre((c, r)), centre((c + 1, r)), FLOOR)
    for c in range(COLS):
        for r in range(ROWS - 1):
            if joins((c, r), (c, r + 1)):
                m.hall(centre((c, r)), centre((c, r + 1)), FLOOR)
    furnish = []
    sx, sy = stair(m, *centre(START))
    furnish.append(door_room(m, *centre(DOOR)))
    for cell, (seal, keeper, style) in SEALS.items():
        furnish.append(seal_room(m, rnd, *centre(cell), seal, keeper, style))
    for cell in fillers:
        if kinds[cell] == "chasm":
            furnish.append(chasm(m, rnd, *centre(cell), across[cell]))
        else:
            furnish.append(ROOMS[kinds[cell]](m, rnd, *centre(cell)))
    m.walls()
    for ddx in (-1, 0, 1):
        for ddy in (-1, 0, 1):
            m.region[sy + ddy][sx + ddx] = "Spawn"
    scx, scy = centre(START)
    for k in range(1, 6, 2):
        y0 = scy + 9 - k * 3
        m.try_place(scx - 5 - k // 2, y0 - 1, "Frost Brazier")
        m.try_place(scx + 5 + k // 2, y0 - 1, "Frost Brazier")
    for f in furnish:
        f()
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
    with open(OUT_MAP, "w", encoding="utf-8", newline="") as f:
        json.dump(jm, f, separators=(",", ":"))


def preview(m, out):
    os.makedirs(out, exist_ok=True)
    colour = {None: (0, 0, 0)}
    for name, (c, _) in GROUNDS.items():
        v = int(c, 16)
        colour[name] = ((v >> 16) & 255, (v >> 8) & 255, v & 255)
    img = Image.new("RGB", (W * 4, H * 4))
    for y in range(H):
        for x in range(W):
            c = colour[m.ground[y][x]]
            o = m.obj[y][x]
            if o in (WALL, SCONCE, ICEWALL):
                c = (90, 110, 140)
            for yy in range(4):
                for xx in range(4):
                    img.putpixel((x * 4 + xx, y * 4 + yy), c)
            if o and o not in (WALL, SCONCE, ICEWALL, "Chasm Mist"):
                img.putpixel((x * 4 + 1, y * 4 + 1), (255, 60, 60))
                img.putpixel((x * 4 + 2, y * 4 + 2), (255, 60, 60))
    img.save(os.path.join(out, "ice_tomb_map.png"))
    for fname in (SHEET8[1], SHEET16[1], CHARS16[1], CHARS32[1]):
        sheet = Image.open(os.path.join(IMAGES, fname))
        k = 6 if "8x8" in fname else 3
        bg = Image.new("RGBA", sheet.size, (40, 44, 60, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.size[0] * k, sheet.size[1] * k), Image.NEAREST).save(os.path.join(out, fname))


def main():
    types = Types()
    s8, s16 = Grid(8, 16), Grid(16, 8)
    c16, c32 = Chars(16), Chars(32)
    m = build_map()
    g = grounds(types, s8)
    o = objects(types, s8, s16, c16, c32)
    os.makedirs(IMAGES, exist_ok=True)
    s8.write(os.path.join(IMAGES, SHEET8[1]))
    s16.write(os.path.join(IMAGES, SHEET16[1]))
    c16.write(os.path.join(IMAGES, CHARS16[1]))
    c32.write(os.path.join(IMAGES, CHARS32[1]))
    write_xml(OUT_XML["grounds"], "GroundTypes", g,
              "Generated by tools/build_ice_tomb.py: the Ice Tomb's floors. Do not edit.")
    write_xml(OUT_XML["objects"], "Objects", o,
              "Generated by tools/build_ice_tomb.py: the Ice Tomb's walls, monsters, keepers, seals, "
              "the Frozen Court, scenery, shots and pets. Do not edit.")
    save_map(m)
    types.save()
    known = {e.get("id") for e in g + o} | {None}
    used = {t for row in m.ground for t in row} | {t for row in m.obj for t in row if t}
    assert used <= known, used - known
    monsters = sum(1 for row in m.obj for t in row if t in MINIONS or t in KEEPERS)
    print("wrote %s (%dx%d), %d grounds, %d objects, %d monsters" % (OUT_MAP, W, H, len(g), len(o), monsters))
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        preview(m, out)
        print("preview in", out)


if __name__ == "__main__":
    main()
