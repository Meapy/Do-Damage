"""Pet behaviours for tools/port_doomed.py.

Reads the Doomed Realms pet definitions (wserver/logic/db/BehaviorDb.Pets.cs and HealPets.cs) and
writes source/WorldServer/logic/db/BehaviorDb.DoomedPets.cs for the ported pets:
  * PetFollow (betterSkillys' follow, which also teleports the pet back when it is 20+ tiles away)
  * PetHeal for PetsHealingHP / PetsHealingMP, with the same amounts and cooldowns
  * PetStrike for PetAttack and ThrowAttackPet, which hit the nearest enemy directly
  * PetShoot for the projectile attacks (PetSimpleAttack, PetMultiAttack, PetRingAttack,
    PetPredictiveMultiAttack), with the same projectile, count, spread and offset
A pet drawn as a boss (it has a betterSkillys boss's sprite: a <Quest/> monster with projectiles,
or one in EXTRA_BOSSES) shoots like that boss instead: the boss's projectiles replace the pet's in
DoomedRealms.xml, and each distinct Shoot of the boss's behaviour becomes a PetShoot (count,
spread, offset, projectile and cooldown; aimed at the nearest enemy, as every pet shot is).
betterSkillys' own permanent pets, which had no behaviour, get the follow.
Pets are sorted into sizes (TINY_PETS, SMALL_PETS, MEDIUM_PETS, LARGE_PETS, ORYX_PETS), which set
their healing and damage.

Run on its own (python tools/port_doomed_pets.py), it applies what's set by hand (apply_pet_changes:
sizes, PET_ATTACKS, PET_ABILITIES, PET_PROJECTILES...) to the files the last port wrote, without the
Doomed Realms repo.
"""

import copy
import glob
import math
import os
import re
import xml.etree.ElementTree as ET
from xml.sax import saxutils

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PET_BEHAVIOR_FILES = ["BehaviorDb.Pets.cs", "BehaviorDb.HealPets.cs"]
PET_BEHAVIORS_CS = os.path.join(ROOT, "source", "WorldServer", "logic", "db", "BehaviorDb.DoomedPets.cs")
CS_USINGS = ("using Shared.resources;\nusing WorldServer.core.net.stats;\nusing WorldServer.logic.behaviors;\n"
             "using WorldServer.logic.behaviors.doomed;\n\n")
ITEM_XMLS = [os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealms.xml"),
             os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "xmls", "DoomedRealms.xml")]
# shots this close to the aim count towards a pet's damage on one enemy (the others of a spread miss
# a small enemy: measured on the vault's Training Dummy, 15 counted twice what landed)
HIT_CONE = 5.0
BEHAVIOR_DIR = os.path.join(ROOT, "source", "WorldServer", "logic", "db")
# generated here or by the other port scripts: no betterSkillys bosses in them
GENERATED_CS = {"BehaviorDb.DoomedPets.cs", "BehaviorDb.DoomedAllies.cs", "BehaviorDb.DoomedMonsters.cs"}
# bosses without a <Quest/> flag
EXTRA_BOSSES = {"Horrific Creation"}
# a boss pet takes at most this many of the boss's attacks, in the order its behaviour lists them
MAX_BOSS_ATTACKS = 4
# a boss pet's fastest attack, ms (Mama Megamoth fires every 50)
MIN_COOLDOWN = 300
# Pet sizes: every sized pet heals its owner's HP and MP by the same amount, replacing Doomed Realms'
# healing (name: (amount, every how many ms)). Its attacks are as before.
PET_SIZES = {"Tiny": (15, 2000), "Small": (20, 2000), "Medium": (30, 2000), "Large": (45, 1500),
             "Oryx": (60, 1500)}
# the pet shop's critters: the plain animals, the only pets sold (SOLD_SIZE); each drops too
TINY_PETS = {
    "Black Cat (Doomed)", "Grey Cat (Doomed)", "Orange Cat (Doomed)", "Tan Cat (Doomed)", "Yellow Cat (Doomed)",
    "White Cat (Doomed)", "Brown Pup (Doomed)", "Grey Pup (Doomed)", "Blue Snail (Doomed)", "Purple Snail (Doomed)",
    "Brown Fox (Doomed)", "Grey Fox (Doomed)", "Green Frog (Doomed)", "Purple Frog (Doomed)", "Penguin (Doomed)",
    "Green Penguin (Doomed)", "Grass Penguin (Doomed)", "Purple Penguin", "Sheepdog (Doomed)", "Panda (Doomed)",
    "Bluebird (Doomed)", "Chick (Doomed)", "Pig (Doomed)", "Squirrel (Doomed)", "Bat (Doomed)", "Toucan (Doomed)",
    "Mallard (Doomed)", "Duck (Doomed)", "Turtle (Doomed)", "Raven (Doomed)", "Crab (Doomed)", "Hamster (Doomed)",
    "Dragonfly (Doomed)", "Turkey (Doomed)", "Bunnyy", "Beach Ball", "Valentine", "White Bag Pet",
}
# the rarer critters, the fighters and the odd ones: drops only
SMALL_PETS = {
    "Golden Pup (Doomed)", "Golden Sheepdog (Doomed)", "Grey Wolf (Doomed)", "Black Wolf (Doomed)", "Lion (Doomed)",
    "White Lion (Doomed)", "Karate Penguin Red", "Karate Penguin Blue", "Karate Penguin Yellow",
    "Karate Penguin Purple", "Robobuddy (Doomed)", "Sea Slurp (Doomed)", "Beer Slurp", "Lil' Ghost (Doomed)",
    "Leprechaun (Doomed)", "Skunk (Doomed)", "Eagle (Doomed)", "Jelly (Doomed)", "Bee (Doomed)", "Elf (Doomed)",
    "Dino (Doomed)", "Elephant (Doomed)", "Demon Frog (Doomed)", "Gargoyle (Doomed)", "Gummy Bear (Doomed)",
    "Lil' Cyclops (Doomed)", "Snowy Owl (Doomed)", "Baby (Doomed)", "Baby Dragon (Doomed)", "Monkeyn",
    "Sea Horsee", "Kirbyy", "Pentaract Eyee",
}
# godland and dungeon monsters, heroes, the players' own named pets and other striking sprites
MEDIUM_PETS = {
    "Angell", "Ninjaa", "Knightt", "Paladinn", "Priestt", "sharkmann", "Pumpkinnnnn", "Beholderr", "Drakee",
    "Reddemonn", "Phoenixx", "Djinnn", "Reaperr", "Tornadoo", "Gyaradoss", "pumpkmann", "Zombie Penguin",
    "Skeletal Guardian Pet", "Sea Maree", "Pyramid Artifact P", "KingSlime", "Josh's Pet", "Dallas's Pet",
    "Astro's Griffin", "Aceaku's Pet", "Lunar's Phoenix", "Support Bot", "Knight Pet",
    # pets that shoot a plain pattern of their own
    "Kythal the Necromancer", "Kythal the Skull", "Zenco The Undead God", "Shroom (Doomed)", "Cerberus Skull",
    "Twilight Sparkle",
}
# bosses, the stronger and more unique pets (most have Elder versions): these, and any pet drawn as a
# boss (find_bosses), which shoots that boss's patterns
LARGE_PETS = {
    "Gebb", "Bess", "Nutt", "Thessal Pet", "Dinosaur's Pet", "Skull Shrine P", "Cubee", "Septaviuss", "Malphass",
    "Sthenos", "Horrificc", "Limonn", "Coill Snake", "Scootaloo", "sphinxxx", "Doomed Nature Beast",
    # the strongest of the pets with shots of their own
    "Cookiee", "Nightmareee",
}
# the Lil' Oryxes, one for each Oryx (port_doomed.LIL_ORYXES), a little above the boss pets (the one
# Lil' Oryx was an admin pet, 200 every 0.8 s)
ORYX_PETS = {"Lil' Oryx (Doomed)", "Lil' Wine Cellar Oryx", "Lil' Celestial Oryx", "Lil' Infernal Oryx",
             "Lil' Ascended Oryx"}
# Set by hand, after the port (apply_pet_changes):
# A pet's attacks, in place of the ones the port found, as written in BehaviorDb.DoomedPets.cs:
# PetShoot(range, count, degrees between shots, offset, projectile id, cooldown ms), each volley aimed
# at the pet's target; PetStrike(min, max, cooldown ms, range, bleed damage, bleed ms).
PET_ATTACKS = {
    # Grand Sphinx's three phases (the port took 4 shots from the first, all the same Fireball):
    # Fireballs, Zedshots, Hellfire
    "sphinxxx": ["new PetShoot(12, 1, 0, 0, 0, 800)", "new PetShoot(12, 3, 10, 0, 0, 1000)",
                 "new PetShoot(10, 3, 10, 0, 1, 2000)", "new PetShoot(10, 2, 4, 0, 2, 1500)"],
    # Archdemon Malphas' bullets, then its big bullet and shields, aimed (its pair of shields flew 72
    # degrees apart, both wide of the target, and its heaviest shots weren't used)
    "Malphass": ["new PetShoot(10, 2, 20, 0, 0, 1000)", "new PetShoot(8, 1, 0, 0, 0, 1000)",
                 "new PetShoot(12, 1, 0, 0, 3, 1500)", "new PetShoot(12, 2, 10, 0, 4, 1500)"],
    # Tomb Support's, and its small paralysing Ice Magic, spread so not every one flies at the target
    "Nutt": ["new PetShoot(15, 20, 18, 0, 7, 1000)", "new PetShoot(15, 1, 0, 0, 5, 1000)",
             "new PetShoot(12, 3, 120, 0, 1, 1000)", "new PetShoot(12, 4, 90, 0, 2, 1000)",
             "new PetShoot(12, 3, 25, 0, 8, 1500)"],
    # without the ring of 12 White Balls
    "Horrificc": ["new PetShoot(12, 3, 15, 0, 1, 1500)", "new PetShoot(10, 8, 45, 0, 5, 3000)",
                  "new PetShoot(12, 1, 0, 0, 6, 800)"],
    # Moth Dust, and every fifth shot a fast slowing wave
    "Scootaloo": ["new PetShoot(10, 1, 0, 0, 0, 300)", "new PetShoot(10, 1, 0, 0, 1, 1500)"],
    # its zap bleeds the enemy for 300 over 4 seconds
    "Doomed Nature Beast": ["new PetStrike(90, 200, 4000, 10, 300, 4000)"],
    # three skulls and a big shot (Zenco's bleeds); Kythal the Skull, Shroom
    # and Cerberus Skull have kits of their own (PET_KITS)
    "Kythal the Necromancer": ["new PetShoot(10, 3, 10, 0, 0, 1000)", "new PetShoot(10, 1, 0, 0, 1, 2000)"],
    "Zenco The Undead God": ["new PetShoot(10, 3, 10, 0, 0, 1000)", "new PetShoot(10, 1, 0, 0, 1, 2000)"],
    # three shots in an arc, like a bow
    "Nightmareee": ["new PetShoot(10, 3, 8, 0, 0, 1000)"],
    # The Lil' Oryxes: each Oryx's own volleys, aimed, no farther than his shots reach.
    # Oryx 1: White Bolts, White Flames, Green Stars and White Superblasts
    "Lil' Oryx (Doomed)": ["new PetShoot(12, 3, 15, 0, 2, 900)", "new PetShoot(8, 3, 12, 0, 1, 1000)",
                           "new PetShoot(12, 4, 10, 0, 4, 1200)", "new PetShoot(12, 3, 20, 0, 16, 2000)"],
    # Oryx 2's Attack phase, without its minions
    "Lil' Wine Cellar Oryx": ["new PetShoot(12, 8, 45, 0, 0, 1500)", "new PetShoot(12, 3, 10, 0, 1, 1000)",
                              "new PetShoot(5, 3, 10, 0, 2, 1000)", "new PetShoot(12, 2, 10, 0, 3, 1000)",
                              "new PetShoot(12, 3, 10, 0, 4, 1000)", "new PetShoot(12, 2, 10, 0, 5, 1000)",
                              "new PetShoot(12, 3, 10, 0, 6, 1000)"],
    # Oryx 3: White Big Bolts, then his Exalted Big Missiles, Big Spikes and Missiles
    "Lil' Celestial Oryx": ["new PetShoot(12, 3, 12, 0, 8, 1600)", "new PetShoot(12, 5, 14, 0, 30, 1200)",
                            "new PetShoot(12, 3, 8, 0, 31, 900)", "new PetShoot(12, 2, 20, 0, 43, 800)"],
    # Oryx 4: Hellfire Bolts and Stars, White Flames and White Superblasts
    "Lil' Infernal Oryx": ["new PetShoot(12, 3, 12, 0, 2, 700)", "new PetShoot(12, 3, 10, 0, 4, 1000)",
                           "new PetShoot(8, 3, 12, 0, 1, 1000)", "new PetShoot(12, 5, 10, 0, 16, 1600)"],
    # Oryx 5: Azure Lances, Sunfire Missiles, Divine Spikes and his Divine Spear
    "Lil' Ascended Oryx": ["new PetShoot(12, 3, 12, 0, 0, 1000)", "new PetShoot(12, 3, 10, 0, 1, 900)",
                           "new PetShoot(9, 5, 8, 0, 2, 1200)", "new PetShoot(12, 1, 0, 0, 7, 2000)"],
}
# What a pet does for its owner, besides healing: PetOwnerEffect(condition, ms, cooldown ms) and
# PetOwnerBoost(stat, amount, ms, cooldown ms)
PET_ABILITIES = {
    "Gebb": ["new PetOwnerEffect(ConditionEffectIndex.Healing, 2000, 5000)"],
    "Limonn": ["new PetOwnerEffect(ConditionEffectIndex.Speedy, 2000, 5000)"],
    "Horrificc": ["new PetOwnerEffect(ConditionEffectIndex.Invulnerable, 1000, 10000)"],
    "Malphass": ["new PetOwnerBoost(StatDataType.Vitality, 15, 5000, 10000)"],
    "Cubee": ["new PetOwnerBoost(StatDataType.Wisdom, 15, 5000, 10000)"],
    "Skull Shrine P": ["new PetOwnerBoost(StatDataType.Attack, 15, 5000, 10000)"],
}
# The pet's projectiles copied from a betterSkillys boss (without their condition effects), in place
# of its own
PET_BOSS_PROJECTILES = {"Lil' Oryx (Doomed)": "Oryx the Mad God 1", "Lil' Wine Cellar Oryx": "Oryx the Mad God 2",
                        "Lil' Celestial Oryx": "Oryx the Mad God 3", "Lil' Infernal Oryx": "Oryx the Mad God 4",
                        "Lil' Ascended Oryx": "Oryx the Ascended God"}
# A boss pet's damage per second on one enemy (behaviour_dps, with its PET_ATTACKS): its boss's
# projectiles' damage is scaled to it. The Lil' Oryxes rise with their Oryx.
PET_DPS = {"Lil' Oryx (Doomed)": 1000, "Lil' Wine Cellar Oryx": 1500, "Lil' Celestial Oryx": 2000,
           "Lil' Infernal Oryx": 2500, "Lil' Ascended Oryx": 3000}
# Projectiles changed by hand: (pet, projectile id) -> what to set: damage, speed, lifetime (ms),
# object (its sprite), effects (condition, seconds) on the enemy hit, bleed (damage, seconds), multihit.
# A projectile the pet doesn't have is made from these.
PET_PROJECTILES = {
    # the Tomb bosses' ring of spinners did no damage
    ("Gebb", 3): {"damage": 30}, ("Bess", 3): {"damage": 30}, ("Nutt", 7): {"damage": 30},
    ("Bess", 1): {"effects": [("Armor Broken", 2)]},
    ("Nutt", 8): {"effects": [("Paralyzed", 0.25)]},
    # Moth Dust flew 1 tile
    ("Scootaloo", 0): {"damage": 80, "speed": 50, "lifetime": 1600, "multihit": False},
    ("Scootaloo", 1): {"object": "Cyan Fairy Wave", "damage": 100, "speed": 120, "lifetime": 900,
                       "effects": [("Slowed", 1)], "multihit": False},
    ("Kythal the Necromancer", 0): {"damage": 70},
    # no pet weakens enemies (Weak cut their shots' damage, too strong on a pet): the Kythals', the
    # Abyssal Krakenling's, the Kitsune Spirit's and the Nightmare's Weak are gone
    ("Kythal the Necromancer", 1): {"damage": 120, "effects": []},
    ("Nightmareee", 0): {"effects": []},
    ("Zenco The Undead God", 0): {"damage": 70},
    ("Zenco The Undead God", 1): {"damage": 120, "bleed": (120, 3)},
}


# Each size's damage a second on one enemy (behaviour_dps): every sized pet's damage (its shots', its
# strikes' and novas', and their bleeds) is scaled to it (scale_pets), unless PET_DPS gives its own.
SIZE_DPS = {"Tiny": 50, "Small": 125, "Medium": 250, "Large": 500}
PET_DPS.update({
    # the Tomb's three gods, the hardest of the boss pets to get
    "Bess": 600, "Gebb": 600, "Nutt": 600,
    "Thessal Pet": 550, "Horrificc": 550, "Cookiee": 550, "Dinosaur's Pet": 550,
})


def shot(sprite, count=1, spread=0, cooldown=1000, weight=1.0, speed=100, life=800, **flags):
    """A kit's PetShoot: count of the sprite's shots, spread degrees apart, every cooldown ms, aimed.
    weight is a shot's damage against the kit's others (scale_pets sets the real numbers); flags:
    size, multihit, pierce (armor piercing), wavy, boomerang, amplitude, frequency, effects
    [(condition, seconds)], bleed (weight, seconds)."""
    # an even volley spread wide either side of the aim flies past a single enemy: keep it within
    # HIT_CONE of the aim, so every shot of it hits
    if count % 2 == 0 and spread * count < 360 and spread / 2 > HIT_CONE:
        spread = 2 * HIT_CONE - 2
    return dict(kind="shoot", sprite=sprite, count=count, spread=spread, cooldown=cooldown, weight=weight,
                speed=speed, life=life, **flags)


def strike(cooldown, weight=1.0, range=8, bleed=None):
    """A kit's PetStrike: a hit on the nearest enemy every cooldown ms; bleed (weight, ms)."""
    return dict(kind="strike", cooldown=cooldown, weight=weight, range=range, bleed=bleed)


def nova(cooldown, weight=1.0, radius=2.5, effect=None, ms=0, color=0xffff0000, range=9):
    """A kit's PetNova: a burst on the nearest enemy hitting all within radius, with a condition."""
    return dict(kind="nova", cooldown=cooldown, weight=weight, radius=radius, effect=effect, ms=ms,
                color=color, range=range)


# Pets' own attacks, in place of the plain strike (50-70 every 3 s) every critter and monster pet had,
# so that no two pets fight alike: their shots (with sprites of their own, written as the pet's
# projectiles 0, 1...), strikes and novas. Their damage is scaled to their size's (SIZE_DPS).
PET_KITS = {
    # Small: the pet shop's critters
    "Black Cat (Doomed)": [shot("Violet Claw", 2, 12, 800, speed=110, life=600)],
    "Grey Cat (Doomed)": [shot("Steel Claw", cooldown=600, speed=120, life=550)],
    "Orange Cat (Doomed)": [shot("Fire Claw", cooldown=700, life=650)],
    "Tan Cat (Doomed)": [shot("Brown Claw", 3, 14, 1100, life=600)],
    "Yellow Cat (Doomed)": [shot("Rageclaw Proj", cooldown=900, life=700, multihit=True)],
    "White Cat (Doomed)": [shot("Cat Cavalcade", cooldown=1200, speed=80, life=1000, wavy=True)],
    # a bone that comes back, a bite that bleeds, gold
    "Brown Pup (Doomed)": [shot("Bone Proj", cooldown=900, speed=90, life=900, boomerang=True)],
    "Grey Pup (Doomed)": [strike(1500, range=6, bleed=(0.6, 3000))],
    "Golden Pup (Doomed)": [shot("Gold Bullet", 2, 10, 900)],
    # slow pearls that slow
    "Blue Snail (Doomed)": [shot("Blue Pearl", cooldown=1400, speed=50, life=1600, effects=[("Slowed", 1)])],
    "Purple Snail (Doomed)": [shot("Purple Pearl", cooldown=1400, speed=50, life=1600, wavy=True,
                                   effects=[("Slowed", 1)])],
    "Grey Wolf (Doomed)": [shot("Wereshot", 2, 16, 1000)],
    "Black Wolf (Doomed)": [shot("Wereblast", cooldown=1300, life=900, multihit=True)],
    "Brown Fox (Doomed)": [shot("Swift Mote", 3, 8, 1000, speed=160, life=500)],
    "Grey Fox (Doomed)": [shot("Wounding Mote", cooldown=1100, bleed=(0.5, 2))],
    # tongues and spit
    "Green Frog (Doomed)": [shot("Venom Flick", cooldown=800, speed=140, life=450)],
    "Purple Frog (Doomed)": [shot("Purple Spit", 2, 20, 1000, speed=80)],
    "Demon Frog (Doomed)": [shot("Demon Fire Blast", cooldown=1200, life=900)],
    # a roar round the enemy, and a holy wave through them
    "Lion (Doomed)": [nova(2500, radius=2.5, color=0xffffaa33)],
    "White Lion (Doomed)": [shot("Holy Wave", cooldown=1300, multihit=True, pierce=True)],
    "Penguin (Doomed)": [shot("Snowball Bullet", cooldown=900, speed=90)],
    "Green Penguin (Doomed)": [shot("Icicle Shot", 2, 8, 900)],
    "Grass Penguin (Doomed)": [shot("Leaf Bolt", 2, 18, 1000, wavy=True)],
    "Purple Penguin": [shot("Ice Triangle", 3, 12, 1200)],
    "Karate Penguin Red": [shot("Katana Slash", cooldown=700, speed=120, life=450)],
    "Karate Penguin Blue": [shot("Small Blue Shuriken", 3, 15, 1100)],
    "Karate Penguin Yellow": [shot("Yellow Swipe", 2, 25, 900, speed=110, life=450)],
    "Karate Penguin Purple": [shot("Purple Shuriken", cooldown=600, speed=130)],
    # herding sheep at it
    "Sheepdog (Doomed)": [shot("SheepProj", cooldown=1000, speed=70, life=1200)],
    "Golden Sheepdog (Doomed)": [shot("SheepProj", 3, 20, 1600, speed=70, life=1200)],
    "Panda (Doomed)": [shot("Stick Throw", cooldown=1000, speed=80, life=1000)],
    # birds: feathers, eggs, fruit
    "Bluebird (Doomed)": [shot("Feather", 2, 10, 800, speed=130, life=550)],
    "Chick (Doomed)": [shot("Dyed Eggs", cooldown=1200, speed=60, life=1300)],
    "Eagle (Doomed)": [shot("Apo Feather", cooldown=1000, speed=200, life=500, pierce=True)],
    "Raven (Doomed)": [shot("Avian Bolt", 2, 30, 1000)],
    "Toucan (Doomed)": [shot("Mango", cooldown=1100, speed=80)],
    "Mallard (Doomed)": [shot("Mallard Proj", cooldown=900)],
    "Duck (Doomed)": [shot("Bubble", 2, 14, 1000, speed=70, life=1100, wavy=True)],
    "Snowy Owl (Doomed)": [shot("Eternal Snowflake Shot", cooldown=1200, effects=[("Slowed", 1)])],
    "Turkey (Doomed)": [shot("Turkey Feather", 3, 10, 1200)],
    # bugs: spikes, a sting that bleeds, a sonic boom
    "Dragonfly (Doomed)": [shot("Insectile Spike", 3, 6, 1100, speed=170, life=450)],
    "Bee (Doomed)": [shot("Stinger", cooldown=700, speed=150, life=500, bleed=(0.4, 2))],
    "Bat (Doomed)": [shot("Sonic Bat Boom", cooldown=1100, speed=90, multihit=True)],
    "Pig (Doomed)": [shot("Goo Attack", cooldown=1000, speed=70)],
    "Sea Slurp (Doomed)": [shot("Slurp Magic", cooldown=900, wavy=True)],
    "Squirrel (Doomed)": [shot("Acorn", 2, 12, 900, speed=90)],
    "Lil' Ghost (Doomed)": [shot("Ghost Proj", cooldown=1000, speed=70, life=1300, wavy=True, multihit=True)],
    # coins, and now and then a lucky clover
    "Leprechaun (Doomed)": [shot("Golden Coin", 3, 10, 1300), shot("4-Leaf Clover", cooldown=4000, weight=2)],
    # a stink cloud that makes enemies sick (they can't heal)
    "Skunk (Doomed)": [nova(2500, radius=2.5, effect="Sick", ms=2000, color=0xff66cc33)],
    "Turtle (Doomed)": [shot("Green Spinner", cooldown=1500, speed=50, life=1600, multihit=True, boomerang=True)],
    # a zap that stops them for a moment
    "Jelly (Doomed)": [nova(2000, radius=2, effect="Paralyzed", ms=200, color=0xff66ccff)],
    "Elf (Doomed)": [shot("Leaf Arrow", cooldown=800, speed=150, life=650, pierce=True)],
    "Crab (Doomed)": [shot("Coral Spike", 2, 30, 900, life=500)],
    "Hamster (Doomed)": [shot("Cream Shot", 3, 12, 1300)],
    "Dino (Doomed)": [shot("Flame Breath", 3, 10, 1300, speed=90, life=450)],
    # a stomp round the enemy that slows
    "Elephant (Doomed)": [nova(3000, radius=3, effect="Slowed", ms=1000, color=0xffaaaaaa)],
    "Gargoyle (Doomed)": [shot("Big Stone bolt", cooldown=1600, speed=60, life=1300)],
    "Gummy Bear (Doomed)": [shot("Hard Candy Stake", 2, 12, 1000)],
    "Lil' Cyclops (Doomed)": [shot("Eye Laser", cooldown=1100, speed=160, life=550, multihit=True)],
    "Baby (Doomed)": [shot("Toy Attack", cooldown=900, speed=80)],
    "Baby Dragon (Doomed)": [shot("Dragon Attack Red Small Fireball", cooldown=900)],
    "Robobuddy (Doomed)": [shot("Robot Attack", 2, 8, 800, speed=140, life=500)],
    # Small pets of betterSkillys' own and the event critters
    "Beach Ball": [shot("Beach Ball Bullet", cooldown=1200, speed=60, life=1400, boomerang=True)],
    "Beer Slurp": [shot("Beer Bottle", cooldown=1100, speed=70)],
    "Valentine": [shot("Valentine Heart Bullet", 2, 20, 1100, wavy=True)],
    "Bunnyy": [shot("BB Carrot Shot", 2, 10, 900)],
    "White Bag Pet": [shot("Glittering Prizes", cooldown=1200)],
    "Monkeyn": [shot("Fruitpocalypse", cooldown=1000, speed=80)],
    "Sea Horsee": [shot("Sea Horse Spinner", cooldown=1100, multihit=True)],
    "Kirbyy": [shot("Pink Star", cooldown=700, speed=120)],
    "Pentaract Eyee": [shot("Black Eye", cooldown=1000, speed=110)],
    # Medium: monsters and heroes
    "Angell": [shot("Angelic Bolt", 3, 10, 1000, speed=140, life=650)],
    "Ninjaa": [shot("Ninja Star Basic", 3, 12, 900, speed=150, life=550),
               shot("Ninja Star Doom", cooldown=3000, weight=2.5, speed=150, life=600)],
    "Knightt": [strike(1200, 1.5, range=6), shot("Knight DPS Bolt", cooldown=2000, weight=1.5, multihit=True)],
    "Paladinn": [shot("Holy Wave", 2, 10, 1200, multihit=True, pierce=True)],
    "Priestt": [shot("Cultist Light Shot", 2, 10, 1000)],
    "sharkmann": [shot("Fishman Spear", 2, 8, 1000, speed=130, life=650, bleed=(0.5, 3))],
    "Pumpkinnnnn": [shot("Orange Pumpkin Blast", cooldown=1300, multihit=True)],
    "pumpkmann": [shot("Pumpkin Wave", 3, 15, 1200, wavy=True)],
    # a ring of five eye beams, one at the enemy, and a quick laser
    "Beholderr": [shot("Dragon Eye Laser", 5, 72, 1500, speed=140, life=600, multihit=True),
                  shot("Eye Laser", cooldown=1000, speed=160, life=550)],
    "Drakee": [shot("Dragon Attack Red Firewave", 2, 14, 1100)],
    "Reddemonn": [shot("Demonic Wave", cooldown=1100, multihit=True, pierce=True)],
    "Phoenixx": [shot("Fireball", 3, 12, 1000), nova(4000, 1.5, radius=2.5, color=0xffff6600)],
    "Djinnn": [shot("Lightning Spiral", 2, 20, 900, wavy=True)],
    # a scythe that comes back and bleeds
    "Reaperr": [shot("Student Scythe", cooldown=1300, multihit=True, boomerang=True, bleed=(0.6, 3))],
    "Tornadoo": [shot("Storm Swirl", 4, 10, 1100, speed=80, life=1000, multihit=True, amplitude=0.6, frequency=1)],
    "Gyaradoss": [shot("Water Storm", 3, 12, 1000), nova(3500, 1.2, radius=2.5, color=0xff3399ff)],
    "Zombie Penguin": [shot("Zombie Shot", 2, 10, 1000, effects=[("Sick", 2)])],
    "Skeletal Guardian Pet": [shot("Skeleton Attack", 2, 10, 900)],
    "Sea Maree": [shot("Wavess", cooldown=1000, multihit=True, wavy=True), shot("Trident", cooldown=2000, weight=1.5)],
    "Pyramid Artifact P": [shot("Ancient Magic", 3, 15, 1100)],
    # a slime splash that slows, and a blob
    "KingSlime": [nova(2000, radius=2.5, effect="Slowed", ms=1000, color=0xff44dd44),
                  shot("Green Blob", cooldown=1500, speed=70)],
    "Josh's Pet": [shot("binary", 3, 8, 1000, speed=140)],
    "Dallas's Pet": [shot("Fish Shot", 2, 12, 900)],
    "Astro's Griffin": [shot("Feather Bolt", 3, 10, 1000, speed=150)],
    "Aceaku's Pet": [shot("Static Star", cooldown=1100, effects=[("Paralyzed", 0.2)])],
    "Lunar's Phoenix": [shot("Gold Fireball", 2, 12, 1000)],
    "Support Bot": [shot("K.I.D.D. Force Energy 1.0", cooldown=1000)],
    "Knight Pet": [shot("Sword Bullet", cooldown=700, speed=130)],
    # a ring of six skulls, one at the enemy, and a big one
    "Kythal the Skull": [shot("Skull Proj 1", 6, 60, 1200), shot("Skull Proj 2", cooldown=2000, weight=1.7)],
    # spores, and a cloud of them that makes enemies sick
    "Shroom (Doomed)": [shot("Small Mush Proj 1", 3, 10, 1000),
                        nova(3000, 1.2, radius=2.5, effect="Sick", ms=2000, color=0xffcc66ff)],
    # three heads, and a big skull that breaks armor
    "Cerberus Skull": [shot("Small skul Proj 1", 3, 20, 1000),
                       shot("Large skul Proj 1", cooldown=2500, weight=2.5, effects=[("Armor Broken", 1)])],
}
PET_ABILITIES.update({
    # the healers among the companions
    "Priestt": ["new PetOwnerEffect(ConditionEffectIndex.Healing, 2000, 8000)"],
    "Support Bot": ["new PetOwnerEffect(ConditionEffectIndex.Armored, 2000, 10000)"],
    # a Large pet's ability, as Gebb's and Limon's: armored for 2 s of every 5
    "Nightmareee": ["new PetOwnerEffect(ConditionEffectIndex.Armored, 2000, 5000)"],
})
# Pet generators: each is used up when used (<Consumable/>), and drops in a white bag of its own
# (<BagType>6</BagType>, <Soulbound/>: a private bag). The pet stays with the character.
GENERATOR_BAG = 6
# Each scaled pet's projectiles' damage before scale_pets (pet -> projectile id -> tag -> damage),
# written the first time a pet is scaled, so that scaling again gives the same numbers. Kits and the
# Lil' Oryxes are made afresh on every run and aren't kept here.
BASE_DAMAGE_JSON = os.path.join(ROOT, "tools", "pet_base_damage.json")
# Only the Tiny pets' generators are sold (MerchantLists, port_doomed_economy.unsold_pet); every pet
# drops (SETUP-LOCAL.md, "Pets: single use, drops and attacks").
SOLD_SIZE = "Tiny"
# Generators' names, where several shared one
GENERATOR_NAMES = {
    "Karate Penguin Red Generator": "Red Karate Penguin", "Karate Penguin Blue Generator": "Blue Karate Penguin",
    "Karate Penguin Yellow Generator": "Yellow Karate Penguin",
    "Karate Penguin Purple Generator": "Purple Karate Penguin", "Robobuddy Generator": "Robobuddy",
    "Snowman Generator": "Snowman", "Skeletal Guardian Generator": "Skeletal Guardian",
    "Lunar's Phoenix Generator": "Lunar Phoenix", "Twilight Generator": "Twilight Phoenix",
    "Kythal the Necromancer Generator": "Kythal the Necromancer", "Kythal the Skull Generator": "Kythal the Skull",
}
# betterSkillys' own pets, added by write_new_pets to both copies of Permapets (the pet and its
# "... Generator") and to BehaviorDb.DoomedPets.cs: (pet id, size, drawn as this object, the
# generator's description). Their type ids follow from their place in the list (NEW_PET_TYPE + 2 *
# place for the pet, one more for its generator), so only ever add to the end. Each drops from the
# monster it's drawn as (the bosses' loot in BehaviorDb.*.cs).
NEW_PET_TYPE = 0x6c00
NEW_PETS = [
    ("Lil' Puppet Master", "Large", "The Puppet Master", "Pulls the strings of a little theatre of its own."),
    ("Lil' Davy Jones", "Large", "Davy Jones", "Still owes the sea a soul or two."),
    ("Lil' Crystal Prisoner", "Large", "Crystal Prisoner", "Out of its crystal at last, and in no mood to go back."),
    ("Lil' Forgotten King", "Large", "shtrs The Forgotten King", "Remembers every one of your enemies."),
    ("Lil' Esben", "Large", "ic Esben the Unwilling", "Unwilling to do anything but freeze your foes."),
    ("Lil' Megamoth", "Large", "Murderous Megamoth", "The murderous kind, drawn to the light of battle."),
    ("Abyssal Krakenling", "Large", "R2 Abyssal Kraken", "Brought up from the deep, still dripping ink."),
    ("Ember Matriarch", "Large", "R2 Wyrm Matriarch", "Mother of wyrms, and fiercely protective."),
    ("Lil' Medusa", "Medium", "Medusa", "Don't look it in the eyes. Your enemies will."),
    ("Lil' Ent", "Medium", "Ent God", "Slow to anger, slower to forgive."),
    ("Lil' Flying Brain", "Medium", "Flying Brain", "Thinks for you, and fights for you too."),
    ("Lil' Ghost Ship", "Medium", "Ghost Ship", "Sails the air with its cannons loaded."),
    ("Lil' Werewolf", "Medium", "Werewolf", "Every night is a full moon for this one."),
    ("Kitsune Spirit", "Medium", "R2 Kitsune Spirit", "A fox spirit of the eastern shrines, sly and quick."),
    ("Jade Golem", "Medium", "R2 Jade Golem", "Carved from a single jade, and just as hard."),
    ("Ember Wyrmling", "Medium", "R2 Ember Wyrm", "A young wyrm, all fire and no patience."),
    ("Oni Warlord", "Medium", "R2 Oni Warlord", "A demon general, sworn to your banner."),
    ("Lantern Wisp", "Medium", "R2 Lantern Wisp", "Lights the way, and burns what's in it."),
]
for _pet, _size, _look, _desc in NEW_PETS:
    {"Large": LARGE_PETS, "Medium": MEDIUM_PETS}[_size].add(_pet)
# their attacks, with the shots of the monster each is drawn as
PET_KITS.update({
    "Lil' Puppet Master": [shot("Jester Bolt", 3, 15, 1000, speed=120, life=900),
                           shot("Lightning Spiral", 2, 8, 1400, weight=1.2, wavy=True),
                           shot("Big Zombie Blast", cooldown=2500, weight=3, speed=90, life=1100, multihit=True)],
    "Lil' Davy Jones": [shot("Ghost Boss Slash", 3, 15, 1000, speed=110, life=700),
                        shot("Ghost Boss Spinner", cooldown=1800, weight=2, life=1200, boomerang=True,
                             effects=[("Slowed", 1)])],
    "Lil' Crystal Prisoner": [shot("Purple Star", 8, 45, 1500, weight=0.5), shot("Blue Star", cooldown=800),
                              shot("Silver Shield", cooldown=2500, weight=2.5, effects=[("Armor Broken", 1.5)])],
    "Lil' Forgotten King": [shot("White Bullet", cooldown=700, speed=140, life=700),
                            shot("Fire Bullet", 3, 15, 1200, bleed=(0.5, 3)),
                            shot("Ice Bullet", 3, 15, 1600, effects=[("Slowed", 1.5)])],
    "Lil' Esben": [shot("Ice Burst", 5, 18, 1200, speed=90),
                   shot("Grey Boomerang", cooldown=1600, weight=2, life=1100, boomerang=True),
                   nova(4000, 2, radius=3, effect="Slowed", ms=1500, color=0xff88ccff)],
    "Lil' Megamoth": [shot("Fire Boomerang", 2, 8, 1000, life=1000, boomerang=True),
                      shot("Yellow Energy Shot", cooldown=600, speed=130, life=650, pierce=True)],
    "Abyssal Krakenling": [shot("Deep Sea Blast", 3, 15, 1100, wavy=True),
                           shot("Black Missile", cooldown=1800, weight=2, speed=140),
                           nova(5000, 2.5, radius=3.5, color=0xff302050)],
    "Ember Matriarch": [shot("Dragon Attack Red Firewave", 3, 15, 1200, speed=90, life=1000, multihit=True),
                        shot("Fire Bolt", cooldown=700, speed=140, life=700, bleed=(0.4, 3))],
    "Lil' Medusa": [shot("Green Bolt", 3, 15, 1000),
                    nova(4500, 1.5, radius=2.5, effect="Paralyzed", ms=500, color=0xff44aa44)],
    "Lil' Ent": [shot("Fire Bolt", 2, 6, 1000, speed=80, life=1000),
                 nova(3500, 1.5, radius=2, effect="Slowed", ms=1000, color=0xff2a7a2a)],
    "Lil' Flying Brain": [shot("Pink Bolt", 5, 72, 1000), strike(2500, weight=1.5)],
    "Lil' Ghost Ship": [shot("Cannonball", 2, 6, 1000, speed=90, life=900),
                        shot("Giant Cannonball", cooldown=3000, weight=3, speed=70, life=1200, multihit=True)],
    "Lil' Werewolf": [shot("Wereblast", 3, 15, 1100), strike(2000, weight=1.5, range=5, bleed=(0.6, 3000))],
    "Kitsune Spirit": [shot("Cyan Magic", 3, 15, 1000, wavy=True),
                       shot("Pink Bolt", cooldown=1500, weight=1.5, speed=90, life=1300, boomerang=True)],
    "Jade Golem": [shot("Crystal Green Shard", cooldown=900, pierce=True, multihit=True),
                   shot("Green Bolt", 3, 15, 1600, weight=0.8)],
    "Ember Wyrmling": [shot("Dragon Attack Red Spinning Fireball", 2, 8, 1000, bleed=(0.3, 2)),
                       nova(4000, 1.5, radius=2, color=0xffff6600)],
    "Oni Warlord": [shot("Red Fire", 3, 15, 900),
                    shot("Silver Shield", cooldown=2200, weight=2, effects=[("Armor Broken", 1)])],
    "Lantern Wisp": [shot("Fire Bullet", 3, 15, 1000, wavy=True),
                     nova(5000, 2, radius=3, color=0xffffaa33)],
})
# The pet drawn as another object's sprite (its generator's)
PET_TEXTURES = {"Scootaloo": "Scootaloo Generator"}
# How big each size is drawn, in a character's heights (a character is an 8x8 sprite at Size 100): a
# pet's <Size> is set from how much of its sprite's frame the picture covers (pet_draw_sizes).
# The steps are wide enough that a pet's size shows at a glance; the server draws a raised pet at the
# height of the size it's raised to (Player.PetPen.cs SizeHeights, which keeps these numbers).
SIZE_HEIGHTS = {"Tiny": 0.5, "Small": 0.75, "Medium": 1.0, "Large": 1.4, "Oryx": 1.4}
# where pets' <Size> is written: the port's XML and betterSkillys' own pets
PET_XMLS = ITEM_XMLS + [os.path.join(ROOT, "source", "Shared", "resources", "xml", "prod", "EmbeddedData_PermapetsCXML.xml"),
                        os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "prod", "xmls", "Permapets.xml")]
# the sprite sheets' files and cell sizes (tools/gen_rs_manifest.py), paths relative to client/src
MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")
# Shoot's parameters in order (logic/behaviors/Shoot.cs)
SHOOT_PARAMS = ["radius", "count", "shootAngle", "projectileIndex", "fixedAngle", "rotateAngle", "angleOffset",
                "defaultAngle", "predictive", "coolDownOffset", "coolDown"]


def texture_key(o):
    """(sheet, index) of an object's sprite, with the "dr" prefix of the copied sheets taken off."""
    for tag in ("AnimatedTexture", "Texture"):
        t = o.find(tag)
        if t is not None and t.findtext("File"):
            f = t.findtext("File").strip()
            if f.startswith("dr") and len(f) > 2:
                f = f[2].lower() + f[3:]
            i = t.findtext("Index", "0").strip()
            return f, int(i, 16) if i.lower().startswith("0x") else int(i)
    return None


def find_bosses(pets, chosen, bs):
    """pet id -> the betterSkillys boss it is drawn as."""
    by_sprite = {}
    for oid, o in bs.items():
        boss = o.find("Enemy") is not None and o.findall("Projectile") and (
            o.find("Quest") is not None or oid in EXTRA_BOSSES)
        if boss and texture_key(o):
            by_sprite.setdefault(texture_key(o), []).append(oid)
    found = {}
    for pet in pets:
        candidates = by_sprite.get(texture_key(chosen[pet]), [])
        # the plain boss before its Elder or second ("Deux") forms
        candidates.sort(key=lambda b: (b.startswith("Elder "), "Deux" in b, len(b), b))
        if candidates:
            found[pet] = candidates[0]
    return found


def number(text):
    """A Shoot argument's value: a number or simple arithmetic ("360 / 9"), None for anything else."""
    text = text.strip().rstrip("f")
    m = re.match(r"new Cooldown\(\s*(\d+)", text)
    if m:
        return float(m.group(1))
    if re.fullmatch(r"[\d.\s+\-*/()]+", text):
        try:
            return float(eval(text, {"__builtins__": {}}))
        except Exception:
            return None
    return None


def boss_attacks(boss):
    """The distinct Shoots in the boss's behaviour: (radius, count, angle, offset, index, cooldown)."""
    for path in sorted(glob.glob(os.path.join(BEHAVIOR_DIR, "*.cs"))):
        if os.path.basename(path) in GENERATED_CS:
            continue
        text = re.sub(r"/\*.*?\*/", "", open(path, encoding="utf-8-sig", errors="replace").read(), flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        m = re.search(r'\.Init\(\s*(?:\w+\(\s*)?"%s"' % re.escape(boss), text)
        if not m:
            continue
        nxt = re.search(r"\.Init\(", text[m.end():])
        block = text[m.start():m.end() + nxt.start() if nxt else len(text)]
        attacks, seen = [], set()
        for c in re.finditer(r"new Shoot\(", block):
            inner = block[c.end():balanced(block, c.end() - 1) - 1]
            args, depth, cur = [], 0, ""
            for ch in inner:
                if ch == "," and depth == 0:
                    args.append(cur)
                    cur = ""
                    continue
                depth += ch in "([" and 1 or ch in ")]" and -1 or 0
                cur += ch
            args.append(cur)
            values = {}
            for n, arg in enumerate(args):
                named = re.match(r"\s*(\w+)\s*:\s*(.*)$", arg, re.S)
                if named:
                    values[named.group(1)] = number(named.group(2))
                elif n < len(SHOOT_PARAMS):
                    values[SHOOT_PARAMS[n]] = number(arg)
            count = int(values.get("count") or 1)
            angle = values.get("shootAngle")
            if angle is None:
                angle = 360.0 / count if count > 1 else 0.0
            index = int(values.get("projectileIndex") or 0)
            offset = values.get("angleOffset") or 0.0
            # Cooldown.Normalize; at most one volley every MIN_COOLDOWN ms (each shot is a message)
            cooldown = max(int(values.get("coolDown") or 0) or 1000, MIN_COOLDOWN)
            radius = values.get("radius") or 0.0
            key = (count, round(angle, 2), round(offset, 2), index)
            if key not in seen:
                seen.add(key)
                attacks.append((radius, count, angle, offset, index, cooldown))
        return attacks[:MAX_BOSS_ATTACKS]
    return []


def boss_projectiles(boss_obj):
    """The boss's projectiles for its pet: the same sprites, speed, size, path and damage, but without
    their condition effects (a pet stunning or paralysing bosses would be too strong)."""
    out = []
    for p in boss_obj.findall("Projectile"):
        p = copy.deepcopy(p)
        for e in p.findall("ConditionEffect"):
            p.remove(e)
        p.tail = None
        out.append(p)
    return out


def find_object(text, oid):
    """The match of an object's element in an XML file's text (groups: its start tag, its body, the
    end tag's line), or None."""
    # the id as written: apostrophes are usually left as they are
    for pid in (saxutils.escape(oid, {'"': "&quot;"}), saxutils.escape(oid, {'"': "&quot;", "'": "&apos;"})):
        m = re.search(r'(<Object [^>]*id="%s">)(.*?)(\r?\n[ \t]*</Object>)' % re.escape(pid), text, re.S)
        if m:
            return m
    return None


def write_boss_projectiles(projectiles):
    """Puts each pet's projectiles in place of its own in the XML that defines it (both copies of
    DoomedRealms.xml, or of betterSkillys' Permapets)."""
    found = set()
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        for pet, projs in projectiles.items():
            m = find_object(text, pet)
            if not m:
                continue
            found.add(pet)
            nl = "\r\n" if "\r\n" in m.group(2) else "\n"
            # the file's indent: its objects' children are one step in, their children two
            child = re.match(r"\r?\n([ \t]*)<", m.group(2)).group(1)
            outer = m.group(3).lstrip("\r\n")[:-len("</Object>")]
            step = child[len(outer):] or "\t"
            body = re.sub(r"\s*<Projectile\b.*?</Projectile>", "", m.group(2), flags=re.S)
            new = ""
            for p in projs:
                ET.indent(p, space=step, level=0)
                new += nl + child + ET.tostring(p, encoding="unicode").replace("\n", nl + child)
            # after the sprite and flags, where the pets' own projectiles were
            cut = body.find("<Size>")
            cut = body.rfind(nl, 0, cut) if cut >= 0 else len(body)
            body = body[:cut] + new + body[cut:]
            text = text[:m.start()] + m.group(1) + body + m.group(3) + text[m.end():]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    for pet in sorted(set(projectiles) - found):
        print("  %s: not found" % pet)


def balanced(text, start):
    """Index just past the parenthesis that closes the one at text[start]."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return i + 1
    return len(text)


def projectile_damage(obj, index):
    for p in obj.iter("Projectile"):
        if int(p.get("id", "0")) == index:
            dmg = p.findtext("Damage")
            if dmg:
                return int(dmg), int(dmg)
            lo, hi = p.findtext("MinDamage"), p.findtext("MaxDamage")
            if lo and hi:
                return int(lo), int(hi)
    return None


def parse_attack(call):
    a = re.match(r"new PetAttack\((\d+),\s*(\d+),\s*([\d.]+)\)", call)
    if a:
        return ("direct", int(a.group(1)), int(a.group(2)), float(a.group(3)))
    a = re.match(r"ThrowAttackPet\.Instance\(([\d.]+),\s*([\d.]+),\s*(\d+),\s*(\d+)\)", call)
    if a:
        return ("direct", int(a.group(3)), int(a.group(4)), float(a.group(2)))
    a = re.match(r"(Pet(?:Simple|Multi|Ring|PredictiveMulti)Attack)\.Instance\((.*)\)$", call, re.S)
    if not a:
        return None
    kind = a.group(1)
    # Doomed Realms' parameters. Angles are radians, mostly written "<degrees>*(float)Math.PI/180".
    names = {
        "PetSimpleAttack": ["radius", "projectileIndex"],
        "PetMultiAttack": ["radius", "angle", "numShot", "offset", "projectileIndex"],
        "PetPredictiveMultiAttack": ["radius", "angle", "numShot", "predictive", "projectileIndex"],
        "PetRingAttack": ["count", "radius", "offset", "projectileIndex"],
    }[kind]
    args = {}
    for n, arg in enumerate(x.strip() for x in a.group(2).split(",")):
        named = re.match(r"(\w+)\s*:\s*(.*)$", arg, re.S)
        if named:
            args[named.group(1)] = named.group(2)
        elif n < len(names):
            args[names[n]] = arg

    def number(key, default):
        m = re.match(r"\s*(-?[\d.]+)", args.get(key, ""))
        return float(m.group(1)) if m else default

    def degrees(key):
        if "PI" in args.get(key, ""):
            return number(key, 0.0)
        return math.degrees(number(key, 0.0))

    index = int(number("projectileIndex", 0))
    if kind == "PetRingAttack":
        # a ring starts at the target; PetShoot centres its spread on it
        count = max(1, int(number("count", 1)))
        step = 360.0 / count
        return ("projectile", index, count, number("radius", 10.0), step, degrees("offset") + step * (count - 1) / 2)
    count = max(1, int(number("numShot", 1)))
    offset = degrees("offset") if kind == "PetMultiAttack" else 0.0
    return ("projectile", index, count, number("radius", 10.0), degrees("angle"), offset)


def parse_pet_behaviors(dr_root):
    """Doomed Realms pet definitions: list of ((type min, type max), definition)."""
    defs = []
    for name in PET_BEHAVIOR_FILES:
        path = os.path.join(dr_root, "wserver", "logic", "db", name)
        text = re.sub(r"/\*.*?\*/", "", open(path, encoding="utf-8-sig").read(), flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        starts = [m.start() for m in re.finditer(r"\.Init(?:Many)?\(", text)]
        for n, s in enumerate(starts):
            block = text[s:starts[n + 1] if n + 1 < len(starts) else len(text)]
            many = re.match(r"\.InitMany\((0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+)", block)
            one = re.match(r"\.Init\((0x[0-9a-fA-F]+)", block)
            if not many and not one:
                continue
            lo = int((many or one).group(1), 16)
            hi = int(many.group(2), 16) if many else lo
            d = {"hp": None, "mp": None, "attacks": [], "many": bool(many)}
            for c in re.finditer(r"Cooldown\.Instance\(", block):
                inner = block[c.end():balanced(block, c.end() - 1) - 1]
                m = re.match(r"\s*(\d+)\s*,\s*(.*)$", inner, re.S)
                if not m:
                    continue
                ms, call = int(m.group(1)), m.group(2).strip()
                h = re.match(r"new PetsHealing(HP|MP)\((\d+),\s*(\d+)\)", call)
                if h:
                    d["hp" if h.group(1) == "HP" else "mp"] = (ms, int(h.group(2)), int(h.group(3)))
                    continue
                attack = parse_attack(call)
                if attack:
                    d["attacks"].append((ms,) + attack)
            defs.append(((lo, hi), d))
    return defs


def cs_string(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def write_pet_behaviors(dr_root, dr, chosen, bs, types):
    given = {a.get("objectId") for o in chosen.values() for a in o.iter("Activate") if a.text == "PermaPet"}
    pets = {oid for oid, o in chosen.items() if o.findtext("Class") == "Character" and oid in given}
    by_dr_type = {int(chosen[oid].get("type"), 16): oid for oid in pets}
    behaviors = {}
    # ranges (InitMany) first, single definitions override them
    for (lo, hi), d in sorted(parse_pet_behaviors(dr_root), key=lambda x: not x[1]["many"]):
        for t in range(lo, hi + 1):
            if t in by_dr_type:
                behaviors[by_dr_type[t]] = d
    existing = set()
    for f in glob.glob(os.path.join(ROOT, "source", "WorldServer", "logic", "db", "*.cs")):
        if os.path.abspath(f) != os.path.abspath(PET_BEHAVIORS_CS):
            existing |= set(re.findall(r'\.Init\("([^"]+)"', open(f, encoding="utf-8-sig", errors="replace").read()))
    bs_pets = {a.get("objectId") for o in bs.values() for a in o.iter("Activate")
               if a.text == "PermaPet" and a.get("objectId") in bs} - existing

    bosses = find_bosses(pets, chosen, bs)
    boss_projs = {}
    lines = []
    for oid in sorted(pets, key=lambda p: types[p]):
        d = behaviors.get(oid, {"hp": None, "mp": None, "attacks": []})
        shooter = chosen[oid]
        boss = bosses.get(oid)
        attacks = boss_attacks(boss) if boss else []
        if attacks:
            # the boss's shots instead of the pet's own attacks
            shooter = bs[boss]
            boss_projs[oid] = boss_projectiles(shooter)
            d = dict(d, attacks=[(cooldown, "projectile", index, count, min(max(radius, 8.0), 15.0) if radius else 10.0, angle, offset)
                                 for radius, count, angle, offset, index, cooldown in attacks])
        parts = ["new PetFollow()"]
        for key, mana in (("hp", "false"), ("mp", "true")):
            if d[key]:
                ms, lo, hi = d[key]
                parts.append("new PetHeal(%s, %d, %d, %d)" % (mana, lo, hi, ms))
        for attack in d["attacks"]:
            ms, kind = attack[0], attack[1]
            if kind == "direct":
                lo, hi, rng = attack[2], attack[3], attack[4]
                parts.append("new PetStrike(%d, %d, %d, %g)" % (lo, hi, ms, rng))
            elif projectile_damage(shooter, attack[2]):
                index, count, rng, angle, offset = attack[2:]
                parts.append("new PetShoot(%g, %d, %g, %g, %d, %d)" % (
                    rng, count, round(angle, 2), round(offset % 360, 2), index, ms))
        comment = "  // shoots like %s" % boss if oid in boss_projs else ""
        lines.append("            .Init(%s, new State(%s))%s" % (cs_string(oid), ", ".join(parts), comment))
    for oid in sorted(bs_pets):
        lines.append("            .Init(%s, new State(new PetFollow()))" % cs_string(oid))

    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("// Generated by tools/port_doomed.py (port_doomed_pets.py). Do not edit.\n")
        f.write("// Permanent pets: the Doomed Realms pets with their healing and attacks (PetHeal, PetStrike, PetShoot),\n")
        f.write("// and betterSkillys' own permanent pets, which had no behaviour, with a follow. Pets drawn as a\n")
        f.write("// boss shoot that boss's projectiles in its patterns (port_doomed_pets.find_bosses).\n")
        f.write(CS_USINGS)
        f.write("namespace WorldServer.logic\n{\n    partial class BehaviorDb\n    {\n")
        f.write("        private _ DoomedPets = () => Behav()\n")
        f.write("\n".join(lines) + ";\n")
        f.write("    }\n}\n")
    write_boss_projectiles(boss_projs)
    apply_pet_changes(bs)
    for pet in sorted(boss_projs):
        print("  %s shoots like %s" % (pet, bosses[pet]))
    return len(pets), len(bs_pets)


def pet_size(oid, boss):
    """The pet's size (a PET_SIZES key), or None to keep its Doomed Realms healing."""
    for size, pets in (("Tiny", TINY_PETS), ("Small", SMALL_PETS), ("Medium", MEDIUM_PETS), ("Large", LARGE_PETS),
                       ("Oryx", ORYX_PETS)):
        if oid in pets:
            return size
    return "Large" if boss else None


def load_bs():
    """betterSkillys' objects (every XML but the port's), by id."""
    objects = {}
    for f in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
        if os.path.abspath(f) == os.path.abspath(ITEM_XMLS[0]):
            continue
        try:
            for o in ET.parse(f).getroot().iter("Object"):
                objects.setdefault(o.get("id"), o)
        except ET.ParseError:
            pass
    return objects


def set_child(el, tag, value):
    child = el.find(tag)
    if child is None:
        child = ET.SubElement(el, tag)
    child.text = str(value)


def change_projectile(p, change):
    """Sets PET_PROJECTILES' changes on a <Projectile> element."""
    if "object" in change:
        set_child(p, "ObjectId", change["object"])
    if "damage" in change:
        for tag in ("MinDamage", "MaxDamage"):
            if p.find(tag) is not None:
                p.remove(p.find(tag))
        set_child(p, "Damage", change["damage"])
    if "speed" in change:
        set_child(p, "Speed", change["speed"])
    if "lifetime" in change:
        set_child(p, "LifetimeMS", change["lifetime"])
    if "effects" in change:
        for e in p.findall("ConditionEffect"):
            p.remove(e)
        for name, seconds in change["effects"]:
            ET.SubElement(p, "ConditionEffect", duration="%g" % seconds).text = name
    if "bleed" in change:
        for e in p.findall("Bleed"):
            p.remove(e)
        ET.SubElement(p, "Bleed", duration="%g" % change["bleed"][1]).text = str(change["bleed"][0])
    if change.get("multihit") is False:
        for e in p.findall("MultiHit"):
            p.remove(e)


def load_pet_objects():
    """The objects of the server's copies of PET_XMLS (the port's XML and Permapets), by id."""
    objects = {}
    for path in (PET_XMLS[0], PET_XMLS[2]):
        for o in ET.parse(path).getroot().iter("Object"):
            objects.setdefault(o.get("id"), o)
    return objects


def kit_projectiles(kit):
    """The <Projectile> elements of a PET_KITS kit's shots, ids 0, 1... in order (a shot's damage is
    100 times its weight until scale_pets scales it)."""
    projs = []
    for a in kit:
        if a["kind"] != "shoot":
            continue
        p = ET.Element("Projectile", id=str(len(projs)))
        ET.SubElement(p, "ObjectId").text = a["sprite"]
        ET.SubElement(p, "Damage").text = str(round(100 * a["weight"]))
        ET.SubElement(p, "Speed").text = str(a["speed"])
        ET.SubElement(p, "LifetimeMS").text = str(a["life"])
        if a.get("size"):
            ET.SubElement(p, "Size").text = str(a["size"])
        for flag, tag in (("multihit", "MultiHit"), ("pierce", "ArmorPiercing"), ("wavy", "Wavy"),
                          ("boomerang", "Boomerang")):
            if a.get(flag):
                ET.SubElement(p, tag)
        if a.get("amplitude"):
            ET.SubElement(p, "Amplitude").text = "%g" % a["amplitude"]
            ET.SubElement(p, "Frequency").text = "%g" % a.get("frequency", 1)
        for name, seconds in a.get("effects", []):
            ET.SubElement(p, "ConditionEffect", duration="%g" % seconds).text = name
        if a.get("bleed"):
            ET.SubElement(p, "Bleed", duration="%g" % a["bleed"][1]).text = str(round(100 * a["bleed"][0]))
        projs.append(p)
    return projs


def kit_parts(kit):
    """A PET_KITS kit as the pet's behaviours (PetShoot, PetStrike, PetNova), before scale_pets."""
    parts, index = [], 0
    for a in kit:
        dmg = 100 * a["weight"]
        if a["kind"] == "shoot":
            # as far as the shot flies
            rng = max(4.0, min(15.0, a["speed"] * a["life"] / 10000.0))
            parts.append("new PetShoot(%g, %d, %g, 0, %d, %d)" % (round(rng, 1), a["count"], a["spread"], index,
                                                                 a["cooldown"]))
            index += 1
        elif a["kind"] == "strike":
            lo, hi = round(dmg * 0.85), round(dmg * 1.15)
            if a["bleed"]:
                parts.append("new PetStrike(%d, %d, %d, %g, %d, %d)" % (lo, hi, a["cooldown"], a["range"],
                                                                       round(100 * a["bleed"][0]), a["bleed"][1]))
            else:
                parts.append("new PetStrike(%d, %d, %d, %g)" % (lo, hi, a["cooldown"], a["range"]))
        else:
            lo, hi = round(dmg * 0.85), round(dmg * 1.15)
            effect = "ConditionEffectIndex.%s" % a["effect"] if a["effect"] else "null"
            parts.append("new PetNova(%d, %d, %d, %g, %g, %s, %d, 0x%08x)" % (
                lo, hi, a["cooldown"], a["range"], a["radius"], effect, a["ms"], a["color"]))
    return parts


def write_pet_projectiles(bs):
    """PET_BOSS_PROJECTILES, PET_KITS' shots, then PET_PROJECTILES, into the XML defining each pet."""
    pets = set(PET_BOSS_PROJECTILES) | {pet for pet, _ in PET_PROJECTILES} | set(PET_KITS)
    objects = load_pet_objects()
    new = {}
    for pet in sorted(pets):
        if pet not in objects:
            print("  %s: not found" % pet)
            continue
        if pet in PET_KITS:
            projs = kit_projectiles(PET_KITS[pet])
        elif pet in PET_BOSS_PROJECTILES:
            # a boss the port made (Oryx 4) is in its own XML
            boss = PET_BOSS_PROJECTILES[pet]
            projs = boss_projectiles(bs[boss] if boss in bs else objects[boss])
            if pet in PET_DPS and pet in PET_ATTACKS:
                scale_damage(projs, PET_ATTACKS[pet], PET_DPS[pet])
        else:
            projs = [copy.deepcopy(p) for p in objects[pet].findall("Projectile")]
        by_id = {int(p.get("id", "0")): p for p in projs}
        for (owner, index), change in sorted(PET_PROJECTILES.items()):
            if owner != pet:
                continue
            if index not in by_id:
                by_id[index] = ET.Element("Projectile", id=str(index))
                projs.append(by_id[index])
            change_projectile(by_id[index], change)
        for p in projs:
            p.text = p.tail = None
            for c in p:
                c.tail = None
        new[pet] = projs
    write_boss_projectiles(new)


def scale_damage(projs, attacks, dps):
    """Scales the projectiles' damage so the attacks deal about `dps` a second on one enemy."""
    holder = ET.Element("Object")
    holder.extend(projs)
    now = behaviour_dps(attacks, holder)
    if not now:
        return
    for p in projs:
        for tag in ("Damage", "MinDamage", "MaxDamage"):
            d = p.find(tag)
            if d is not None and d.text:
                d.text = str(max(1, round(int(d.text) * dps / now)))


def write_pet_textures():
    """Draws each pet in PET_TEXTURES as its generator's sprite, in both copies of DoomedRealms.xml."""
    for path in ITEM_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        for pet, source in PET_TEXTURES.items():
            m = re.search(r'<Object [^>]*\sid="%s">.*?</Object>' % re.escape(saxutils.escape(source)), text, re.S)
            tex = m and re.search(r"<(Animated)?Texture>.*?</(Animated)?Texture>", m.group(0), re.S)
            p = re.search(r'<Object [^>]*\sid="%s">.*?</Object>' % re.escape(saxutils.escape(pet)), text, re.S)
            if not tex or not p:
                print("  %s: no sprite from %s in %s" % (pet, source, os.path.basename(path)))
                continue
            obj = re.sub(r"<(Animated)?Texture>.*?</(Animated)?Texture>", lambda _: tex.group(0), p.group(0), count=1, flags=re.S)
            text = text[:p.start()] + obj + text[p.end():]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def behaviour_dps(parts, obj):
    """A pet's damage per second on one enemy, from its PetShoots (with their projectiles' damage and
    bleeds, as many shots as fly at the target) and PetStrikes (with their bleeds)."""
    dps = 0.0
    for args in re.findall(r"new PetNova\(([^)]*)\)", ", ".join(parts)):
        a = [x.strip() for x in args.split(",")]
        dps += (float(a[0]) + float(a[1])) / 2 * 1000 / float(a[2])
    for kind, args in re.findall(r"new (PetShoot|PetStrike)\(([^)]*)\)", ", ".join(parts)):
        a = [float(x) for x in args.split(",")]
        if kind == "PetStrike":
            dps += (a[0] + a[1]) / 2 * 1000 / a[2]
            if len(a) > 5 and a[4]:
                dps += a[4] * 1000 / max(a[2], a[5])
            continue
        index, cooldown = int(a[4]), a[5]
        damage = obj is not None and projectile_damage(obj, index)
        if not damage:
            continue
        on_target = shots_on_target(int(a[1]), a[2], a[3])
        dps += (damage[0] + damage[1]) / 2 * on_target * 1000 / cooldown
        bleed = next((p.find("Bleed") for p in obj.findall("Projectile") if int(p.get("id", "0")) == index), None)
        if on_target and bleed is not None:
            dps += int(bleed.text) * 1000 / max(cooldown, float(bleed.get("duration")) * 1000)
    return dps


def pet_effects(parts, obj):
    """What a pet does besides its damage and healing, in words for its tooltip: its abilities, and
    the conditions and bleeds its attacks put on enemies."""
    out = []

    def seconds(ms):
        return "%gs" % (float(ms) / 1000)

    for kind, args in re.findall(r"new (PetOwnerEffect|PetOwnerBoost|PetShoot|PetStrike|PetNova)\(([^)]*)\)", ", ".join(parts)):
        a = [x.strip() for x in args.split(",")]
        if kind == "PetNova":
            what = "Hits all enemies within %g tiles of its target" % float(a[4])
            if a[5] != "null" and int(a[6]):
                what += ", %s for %s" % (re.sub(r"(?<=[a-z])(?=[A-Z])", " ", a[5].split(".")[-1]), seconds(a[6]))
            out.append(what)
        elif kind == "PetOwnerEffect":
            out.append("%s on you for %s every %s" % (a[0].split(".")[-1], seconds(a[1]), seconds(a[2])))
        elif kind == "PetOwnerBoost":
            out.append("+%s %s for %s every %s" % (a[1], a[0].split(".")[-1], seconds(a[2]), seconds(a[3])))
        elif kind == "PetStrike" and len(a) > 5 and int(a[4]):
            out.append("Bleeds %s over %s" % (a[4], seconds(a[5])))
        elif kind == "PetShoot" and obj is not None:
            proj = next((p for p in obj.findall("Projectile") if p.get("id", "0") == a[4]), None)
            if proj is None:
                continue
            for e in proj.findall("ConditionEffect"):
                out.append("%s on hit for %ss" % (re.sub(r"(?<=[a-z])(?=[A-Z])", " ", e.text.strip()), e.get("duration")))
            bleed = proj.find("Bleed")
            if bleed is not None:
                out.append("Bleeds %s over %ss" % (bleed.text, bleed.get("duration")))
    return list(dict.fromkeys(out))


NEW_PETS_START = "<!-- betterSkillys' new pets: written by tools/port_doomed_pets.py (NEW_PETS) -->"
NEW_PETS_END = "<!-- the end of the new pets -->"


def write_new_pets():
    """NEW_PETS' pets and generators into both copies of Permapets (between NEW_PETS_START and
    NEW_PETS_END, written afresh each time: apply_pet_changes then adds their shots, numbers and
    sizes), each drawn as the monster it's named for, and a follow for each pet in
    BehaviorDb.DoomedPets.cs (apply_pet_changes gives it its kit)."""
    looks, taken = {}, set()
    own = os.path.normcase(os.path.abspath(PET_XMLS[2]))
    for path in glob.glob(os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"), recursive=True):
        try:
            for o in ET.parse(path).getroot().iter("Object"):
                looks.setdefault(o.get("id"), o)
                if os.path.normcase(os.path.abspath(path)) != own:
                    taken.add(o.get("id"))
        except ET.ParseError:
            continue
    # an id is the object's name everywhere (behaviours, loot): a pet named as a monster would take its place
    for pet, _, _, _ in NEW_PETS:
        if pet in taken or pet + " Generator" in taken:
            raise SystemExit("NEW_PETS: %s is already the id of another object; name it otherwise" % pet)
    for path in PET_XMLS[2:]:
        text = open(path, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in text else "\n"
        outer, child = "   ", "      "
        empty = "/>" if "<Item/>" in text else " />"

        def written(el):
            el = copy.deepcopy(el)
            for e in el.iter():
                e.tail = None
                if e.text and not e.text.strip():
                    e.text = None
            ET.indent(el, space="   ", level=0)
            return child + ET.tostring(el, encoding="unicode").replace("\n", nl + child)

        block = [outer + NEW_PETS_START]
        for place, (pet, size, look, desc) in enumerate(NEW_PETS):
            src = looks.get(look)
            if src is None:
                raise SystemExit("NEW_PETS: %s is drawn as %s, which isn't in the game's XML" % (pet, look))
            sprite = [e for e in src if e.tag in ("Texture", "AnimatedTexture", "RandomTexture", "Animation")]
            icon = [e for e in sprite if e.tag != "Animation"][:1]
            attr = saxutils.quoteattr(pet)
            block += [outer + '<Object type="0x%04x" id=%s>' % (NEW_PET_TYPE + 2 * place, attr),
                      child + "<Class>Character</Class>"]
            block += [written(e) for e in sprite]
            block += [child + "<NoMiniMap%s" % empty, child + "<Size>100</Size>",
                      child + "<ShadowSize>100</ShadowSize>", outer + "</Object>"]
            gen = saxutils.quoteattr(pet + " Generator")
            block += [outer + '<Object type="0x%04x" id=%s>' % (NEW_PET_TYPE + 2 * place + 1, gen),
                      child + "<DisplayId>%s</DisplayId>" % saxutils.escape(pet),
                      child + "<Class>Equipment</Class>", child + "<Item%s" % empty]
            block += [written(e) for e in icon]
            block += [child + "<SlotType>10</SlotType>", child + "<Soulbound%s" % empty,
                      child + "<Consumable%s" % empty,
                      child + "<Description>%s</Description>" % saxutils.escape(desc),
                      child + "<BagType>%d</BagType>" % GENERATOR_BAG, child + "<Sound>spell/light_heal</Sound>",
                      child + '<Activate objectId=%s cooldown="10">PermaPet</Activate>' % attr,
                      child + "<InvUse%s" % empty, outer + "</Object>"]
        block.append(outer + NEW_PETS_END)
        block = nl.join(block)
        if NEW_PETS_START in text:
            start = text.rindex(nl, 0, text.index(NEW_PETS_START)) + len(nl)
            end = text.index(NEW_PETS_END) + len(NEW_PETS_END)
            text = text[:start] + block + text[end:]
        else:
            close = text.rindex("</Objects>")
            text = text[:close] + block + nl + text[close:]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)

    with open(PET_BEHAVIORS_CS, encoding="utf-8", newline="") as f:
        cs = f.read()
    nl = "\r\n" if "\r\n" in cs else "\n"
    missing = [pet for pet, _, _, _ in NEW_PETS if '.Init(%s,' % cs_string(pet) not in cs]
    if missing:
        last = cs.rindex(".Init(")
        end = cs.index(";", cs.index(nl, last) - 2)
        indent = cs[cs.rindex(nl, 0, last) + len(nl):last]
        cs = cs[:end] + "".join(nl + indent + '.Init(%s, new State(new PetFollow()))' % cs_string(pet)
                                for pet in missing) + cs[end:]
        with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="") as f:
            f.write(cs)


def apply_pet_changes(bs=None):
    """What's set by hand, after the port: the sizes' healing, PET_ATTACKS, PET_KITS, PET_ABILITIES and
    the pets' projectiles and sprites, into BehaviorDb.DoomedPets.cs and the XML defining each pet;
    each sized pet's damage scaled to its size's (scale_pets); then the generators' rules
    (write_generator_rules), each pet's tooltip numbers (annotate_generators) and drawn size. It runs
    at the end of the port, and on its own (without the Doomed Realms repo) on the files the last
    port wrote."""
    write_new_pets()
    write_pet_projectiles(bs or load_bs())
    write_pet_textures()
    objects = load_pet_objects()
    with open(PET_BEHAVIORS_CS, encoding="utf-8", newline="") as f:
        text = f.read()
    pet_ids = {m.replace('\\"', '"') for m in re.findall(r'\.Init\("((?:[^"\\]|\\.)*)"', text)}
    base = base_damages(objects, pet_ids)
    final, sizes, factors = {}, {}, {}
    attack = ("new PetShoot(", "new PetStrike(", "new PetNova(")

    def change(m):
        oid = m.group(1).replace('\\"', '"').replace("\\\\", "\\")
        parts = [p for p in re.split(r", (?=new )", m.group(2))
                 if not p.startswith(("new PetHeal(", "new PetOwnerEffect(", "new PetOwnerBoost("))]
        if oid in PET_KITS:
            parts = [p for p in parts if not p.startswith(attack)] + kit_parts(PET_KITS[oid])
        elif oid in PET_ATTACKS:
            parts = [p for p in parts if not p.startswith(attack)] + PET_ATTACKS[oid]
        parts += PET_ABILITIES.get(oid, [])
        size = pet_size(oid, "boss" if "// shoots like" in m.group(3) else None)
        if size:
            amount, ms = PET_SIZES[size]
            parts[1:1] = ["new PetHeal(%s, %d, %d, %d)" % (mana, amount, amount, ms) for mana in ("false", "true")]
            # its damage, scaled to its size's
            target = PET_DPS.get(oid) or SIZE_DPS.get(size)
            now = behaviour_dps(parts, objects.get(oid))
            if target and now and abs(target / now - 1) > 0.02:
                factors[oid] = target / now
                parts = [scale_part(p, factors[oid]) for p in parts]
        sizes[oid] = size
        final[oid] = (parts, m.group(2))
        return '.Init("%s", new State(%s))%s' % (m.group(1), ", ".join(parts), m.group(3))

    text = re.sub(r'\.Init\("((?:[^"\\]|\\.)*)", new State\((.*)\)\)((?:  //[^\r\n]*)?)', change, text)
    # the usings the abilities need
    head = text.index("namespace ")
    nl = "\r\n" if "\r\n" in text else "\n"
    usings_start = text.index("using ")
    text = text[:usings_start] + CS_USINGS.replace("\n", nl) + text[head:]
    with open(PET_BEHAVIORS_CS, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    scale_pets(factors, base)

    objects = load_pet_objects()
    stats = {}
    for oid, (parts, before) in final.items():
        stat = stats[oid] = {}
        size = sizes[oid]
        if size:
            amount, ms = PET_SIZES[size]
            stat.update(size=size, hp=(amount, amount, ms), mp=(amount, amount, ms))
        else:
            for h in re.finditer(r"new PetHeal\((true|false), (\d+), (\d+), (\d+)\)", before):
                stat["mp" if h.group(1) == "true" else "hp"] = (int(h.group(2)), int(h.group(3)), int(h.group(4)))
        dps = behaviour_dps(parts, objects.get(oid))
        if dps:
            stat["dps"] = round(dps)
        effects = pet_effects(parts, objects.get(oid))
        if effects:
            stat["effects"] = effects
    write_generator_rules(stats)
    annotate_generators(stats)
    write_pet_draw_sizes({oid: s["size"] for oid, s in stats.items() if s.get("size")})
    sized = sum(1 for s in sizes.values() if s)
    unsized = sorted(oid for oid in stats if sizes[oid] is None)
    print("%d pets sized (%d damage scaled); keeping their own healing: %s"
          % (sized, len(factors), ", ".join(unsized) or "none"))


def scale_part(part, factor):
    """A PetStrike's or PetNova's damage (and a strike's bleed) times factor; other parts as they are."""
    m = re.match(r"new (PetStrike|PetNova)\((.*)\)$", part)
    if not m:
        return part
    a = [x.strip() for x in m.group(2).split(",")]
    which = [0, 1, 4] if m.group(1) == "PetStrike" else [0, 1]
    for i in which:
        if i < len(a) and re.fullmatch(r"\d+", a[i]) and (i < 4 or int(a[i])):
            a[i] = str(max(1, round(int(a[i]) * factor)))
    return "new %s(%s)" % (m.group(1), ", ".join(a))


def base_damages(objects, pets):
    """Each scaled pet's unscaled projectile damage (BASE_DAMAGE_JSON), with the pet's objects set to
    it: pet -> projectile id -> {tag: damage}. A pet not kept yet is kept as it is now; a damage set
    in PET_PROJECTILES is its base."""
    import json
    try:
        kept = json.load(open(BASE_DAMAGE_JSON, encoding="utf-8"))
    except (OSError, ValueError):
        kept = {}
    fresh = set(PET_KITS) | set(PET_BOSS_PROJECTILES)
    out = {}
    kept = {oid: v for oid, v in kept.items() if oid in pets}
    for oid, obj in objects.items():
        if oid not in pets or oid in fresh or not obj.findall("Projectile"):
            continue
        pet = kept.get(oid)
        if pet is None:
            pet = {}
            for p in obj.findall("Projectile"):
                tags = {tag: int(p.findtext(tag)) for tag in ("Damage", "MinDamage", "MaxDamage", "Bleed")
                        if (p.findtext(tag) or "").strip().isdigit()}
                if tags:
                    pet[p.get("id", "0")] = tags
        for (owner, index), change in PET_PROJECTILES.items():
            if owner == oid and "damage" in change:
                pet[str(index)] = dict(pet.get(str(index), {}), Damage=change["damage"])
                for tag in ("MinDamage", "MaxDamage"):
                    pet[str(index)].pop(tag, None)
            if owner == oid and "bleed" in change:
                pet.setdefault(str(index), {})["Bleed"] = change["bleed"][0]
        out[oid] = pet
        for p in obj.findall("Projectile"):
            for tag, value in pet.get(p.get("id", "0"), {}).items():
                if p.find(tag) is not None:
                    p.find(tag).text = str(value)
    kept = {oid: v for oid, v in kept.items() if oid not in fresh}
    kept.update(out)
    with open(BASE_DAMAGE_JSON, "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(sorted(kept.items())), f, indent=1, sort_keys=True)
        f.write("\n")
    return out


def scale_pets(factors, base):
    """Each pet's projectiles' damage and bleeds: its base (base_damages, or for a kit what's written
    now) times its factor (pet id -> factor), in the XML defining it."""
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        for pet in set(factors) | set(base):
            factor = factors.get(pet, 1.0)
            m = find_object(text, pet)
            if not m:
                continue
            kept = base.get(pet)

            def projectile(pm):
                ids = re.match(r'<Projectile\b[^>]*\bid="(\d+)"', pm.group(0))
                own = kept.get(ids.group(1) if ids else "0", {}) if kept is not None else {}

                def times(d):
                    tag = re.match(r"<(\w+)", d.group(1)).group(1)
                    value = own.get(tag, int(d.group(2))) if kept is not None else int(d.group(2))
                    return d.group(1) + str(max(1, round(value * factor))) + d.group(3)

                out = re.sub(r"(<(?:Damage|MinDamage|MaxDamage)>)(\d+)(</(?:Damage|MinDamage|MaxDamage)>)", times,
                             pm.group(0))
                return re.sub(r"(<Bleed\b[^>]*>)(\d+)(</Bleed>)", times, out)

            body = re.sub(r"<Projectile\b.*?</Projectile>", projectile, m.group(2), flags=re.S)
            text = text[:m.start(2)] + body + text[m.end(2):]
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def write_generator_rules(stats):
    """Every sized pet's generator (stats: pet id -> its numbers) is used up when used (<Consumable/>)
    and drops in a private white bag (<BagType>GENERATOR_BAG</BagType>, <Soulbound/>), in PET_XMLS."""
    pets = {oid for oid, s in stats.items() if s.get("size")}
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        empty = "/>" if re.search(r"<\w+/>", text) and not re.search(r"<\w+ />", text) else " />"

        def rules(m):
            obj = m.group(0)
            oid = re.match(r'<Object [^>]*id="([^"]*)"', obj)
            name = GENERATOR_NAMES.get(saxutils.unescape(oid.group(1), {"&apos;": "'"})) if oid else None
            if name:
                obj = re.sub(r"<DisplayId>[^<]*</DisplayId>", lambda _: "<DisplayId>%s</DisplayId>" % saxutils.escape(name),
                             obj, count=1)
            a = re.search(r'<Activate objectId="([^"]*)"[^>]*>PermaPet</Activate>', obj)
            if not a or saxutils.unescape(a.group(1), {"&quot;": '"', "&apos;": "'"}) not in pets:
                return obj
            nl = "\r\n" if "\r\n" in obj else "\n"
            indent = re.search(r"\n([ \t]*)<SlotType>", obj).group(1)
            if re.search(r"<BagType>", obj):
                obj = re.sub(r"<BagType>[^<]*</BagType>", "<BagType>%d</BagType>" % GENERATOR_BAG, obj)
            else:
                obj = re.sub(r"(</Description>|<Description\s*/>)", r"\1" + nl.replace("\\", "\\\\") + indent
                             + "<BagType>%d</BagType>" % GENERATOR_BAG, obj, count=1)
            for tag in ("Consumable", "Soulbound"):
                if not re.search(r"<%s\s*/>" % tag, obj):
                    obj = re.sub(r"(<SlotType>[^<]*</SlotType>)", lambda s: s.group(1) + nl + indent + "<%s%s" % (tag, empty),
                                 obj, count=1)
            return obj

        text = re.sub(r'<Object [^>]*>.*?</Object>', rules, text, flags=re.S)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def sprite_extent(obj, sheets, anims):
    """The longer side, in pixels, of what an object's sprite covers in its (first) frame; None when
    it has no picture."""
    from PIL import Image
    anim = obj.find("AnimatedTexture")
    tex = anim if anim is not None else obj.find("Texture")
    if tex is None:
        return None
    sheet = (anims if anim is not None else sheets).get(tex.findtext("File", "").strip())
    if not sheet or not sheet.get("file"):
        return None
    index = int(tex.findtext("Index", "0").strip(), 0)
    image = Image.open(os.path.join(ROOT, "client", "src", sheet["file"])).convert("RGBA")
    if anim is not None:
        # the character's block (as the Rust client's Sprites::char_src finds it); its first frame,
        # the square at the start (a 16x8 sheet's second half is for the attack frame), stands
        w = h = sheet["h"]
        per_row = max(1, image.width // sheet["sheet_w"])
        x, y = index % per_row * sheet["sheet_w"], index // per_row * sheet["sheet_h"]
    else:
        w, h = sheet["w"], sheet["h"]
        cols = image.width // w
        x, y = index % cols * w, index // cols * h
    box = image.crop((x, y, x + w, y + h)).getchannel("A").getbbox()
    return max(box[2] - box[0], box[3] - box[1]) if box else None


def pet_draw_size(extent, size):
    """<Size> drawing a sprite covering extent pixels at its size's height, to the nearest 5."""
    return int(round(SIZE_HEIGHTS[size] * 8 / extent * 100 / 5.0)) * 5


def write_pet_draw_sizes(sizes):
    """Sets each sized pet's <Size> (sizes: pet id -> size) in PET_XMLS, so that pets of a size look
    about as big as one another, whatever their sprite's resolution. Measuring the sprites needs
    Pillow; without it the sizes are left as they are."""
    import json
    try:
        import PIL  # noqa: F401
    except ImportError:
        print("  Pillow isn't installed (pip install pillow): pets' drawn sizes left as they are")
        return
    manifest = json.load(open(MANIFEST, encoding="utf-8"))
    sheets = {s["name"]: s for s in manifest["image_sets"]}
    anims = {s["name"]: s for s in manifest["animated_chars"]}
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()
        objects = {o.get("id"): o for o in ET.fromstring(text.encode("utf-8")).iter("Object")}

        def resize(m):
            oid = saxutils.unescape(m.group(2), {"&quot;": '"', "&apos;": "'"})
            obj = objects.get(oid)
            if oid not in sizes or obj is None or obj.findtext("Class") != "Character":
                return m.group(0)
            extent = sprite_extent(obj, sheets, anims)
            if not extent:
                return m.group(0)
            new = "<Size>%d</Size>" % pet_draw_size(extent, sizes[oid])
            # the object's own <Size>, not its projectiles'
            return re.sub(r"(<Projectile\b.*?</Projectile>)|<Size>[^<]*</Size>",
                          lambda s: s.group(1) or new, m.group(0), flags=re.S)

        text = re.sub(r'(<Object [^>]*\sid="([^"]*)"[^>]*>)(.*?)</Object>', resize, text, flags=re.S)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


def shots_on_target(count, angle, offset):
    """How many shots of a PetShoot volley fly at the enemy it aims at."""
    start = -angle * (count - 1) / 2 + offset
    # PetShoot turns a full ring of an even count half a step, so one shot flies at the enemy
    if count % 2 == 0 and angle * count >= 359.9:
        start += angle / 2
    return sum(1 for i in range(count) if abs((start + angle * i + 180) % 360 - 180) <= HIT_CONE)


def annotate_generators(stats):
    """Puts each pet's damage per second (on one enemy) and healing on the PermaPet activates of
    the items that give it, for the item tooltips: petDps, petSize, petEffects (what else it does,
    "; " between), petHp / petMp ("min-max") and
    petHpMs / petMpMs (every how many milliseconds)."""
    for path in PET_XMLS:
        text = open(path, encoding="utf-8", newline="").read()

        def annotate(m):
            stat = stats.get(saxutils.unescape(m.group(1), {"&quot;": '"', "&apos;": "'"}), {})
            attrs = re.sub(r'\s+pet\w+="[^"]*"', "", m.group(2))
            if "dps" in stat:
                attrs += ' petDps="%d"' % stat["dps"]
            if "size" in stat:
                attrs += ' petSize="%s"' % stat["size"]
            if "effects" in stat:
                attrs += ' petEffects=%s' % saxutils.quoteattr("; ".join(stat["effects"]))
            for key, name in (("hp", "Hp"), ("mp", "Mp")):
                if key in stat:
                    lo, hi, ms = stat[key]
                    attrs += ' pet%s="%s" pet%sMs="%d"' % (name, lo if lo == hi else "%d-%d" % (lo, hi), name, ms)
            return '<Activate objectId="%s"%s>PermaPet</Activate>' % (m.group(1), attrs)

        text = re.sub(r'<Activate objectId="([^"]*)"([^>]*)>PermaPet</Activate>', annotate, text)
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)


if __name__ == "__main__":
    apply_pet_changes()
