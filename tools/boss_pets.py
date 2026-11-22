"""The boss pets of the hardest dungeons, written into each dungeon's objects by its build script
(tools/build_black_between.py, tools/build_crucible.py): a little copy of each boss that follows
its owner and fights as the boss does, and its generator, which the boss drops (1 in 100 once
Loots.PetDropRate halves it).

A pet is drawn with its boss's own row of the dungeon's character sheet, and its alternate forms
with the boss's (the pet takes one on as the boss does, for its signature attack); its generator's
icon is the boss shrunk to 16x16 (black_between.small.pet_icon). Its attacks are its behaviour,
next to its boss's in BehaviorDb.BlackBetween.cs and BehaviorDb.Crucible.cs: the numbers in a
spec (its damage a second and what else it does) are what that behaviour does, so change
both together.

Damage a second is what it does to one boss-sized enemy: shots within 8 degrees of where a volley
aims all hit, a ring hits with one, a turning ring or spiral with its share of a full turn (23
degrees, a boss at 5 tiles), each counted for the share of the time its phase lasts; a nova hits
for its average. Every one is "Oryx" sized, as the Lil' Oryxes are: no Mystery Pet Stone gives
one, and it grows to level 30 as it's fed (Shared.pets.PetLevels).
"""
import xml.etree.ElementTree as ET

from pet_healing import healing


def _sub(parent, tag, text=None, **attrs):
    e = ET.SubElement(parent, tag, {k: str(v) for k, v in attrs.items()})
    if text is not None:
        e.text = str(text)
    return e


def _animated(parent, file, row):
    t = _sub(parent, "AnimatedTexture")
    _sub(t, "File", file)
    _sub(t, "Index", row)
    return t


def shot(pid, obj, damage, speed, life, size=100, effects=(), multi=False, amplitude=None, frequency=None):
    """One of a pet's <Projectile>s (multi: it goes on through every enemy it hits)."""
    p = ET.Element("Projectile", {"id": str(pid)})
    _sub(p, "ObjectId", obj)
    _sub(p, "Damage", damage)
    _sub(p, "Speed", speed)
    _sub(p, "LifetimeMS", life)
    _sub(p, "Size", size)
    if amplitude is not None:
        _sub(p, "Amplitude", amplitude)
        _sub(p, "Frequency", frequency)
    if multi:
        _sub(p, "MultiHit")
    for effect, duration in effects:
        _sub(p, "ConditionEffect", effect, duration=duration)
    return p


def pet_objects(type_of, name, spec, chars_file, row, alt_rows, icon_file, icon_index):
    """The pet and its generator: type_of(name) gives an object's type code; row is the boss's row
    of chars_file, alt_rows its forms' rows; icon_index the generator's icon on icon_file (16x16).

    spec: size (as drawn), dps, effects (what else it does, for the tooltip), description, shots
    (its <Projectile>s). Its healing is its own, as every pet's is (tools/pet_healing.py)."""
    pet = ET.Element("Object", {"type": type_of(name), "id": name})
    _sub(pet, "Class", "Character")
    _animated(pet, chars_file, row)
    for k, alt in enumerate(alt_rows, start=1):
        _animated(_sub(pet, "AltTexture", id=k), chars_file, alt)
    _sub(pet, "NoMiniMap")
    for p in spec["shots"]:
        pet.append(p)
    _sub(pet, "Size", spec["size"])
    _sub(pet, "ShadowSize", 100)

    gen_name = name + " Generator"
    gen = ET.Element("Object", {"type": type_of(gen_name), "id": gen_name})
    _sub(gen, "DisplayId", name)
    _sub(gen, "Class", "Equipment")
    _sub(gen, "Item")
    t = _sub(gen, "Texture")
    _sub(t, "File", icon_file)
    _sub(t, "Index", "0x%02x" % icon_index)
    _sub(gen, "SlotType", 10)
    _sub(gen, "Soulbound")
    _sub(gen, "Consumable")
    _sub(gen, "Description", spec["description"])
    _sub(gen, "BagType", 6)
    _sub(gen, "Sound", "spell/light_heal")
    attrs = {"objectId": name, "cooldown": 10, "petDps": spec["dps"], "petSize": "Oryx"}
    if spec.get("effects"):
        attrs["petEffects"] = "; ".join(spec["effects"])
    hp, hp_ms, mp, mp_ms = healing(name, "Oryx", spec["dps"])
    attrs.update(petHp=hp, petHpMs=hp_ms, petMp=mp, petMpMs=mp_ms)
    _sub(gen, "Activate", "PermaPet", **attrs)
    _sub(gen, "InvUse")
    return [pet, gen]
