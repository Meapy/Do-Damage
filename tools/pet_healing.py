"""Every pet's own healing (2.6.4): what each pet restores at level 1, written into its stone's
PermaPet (petHp, petHpMs, petMp, petMpMs; Shared.pets.PetLevels grows it to 160% by its last level).

    python tools/pet_healing.py [--check]

Before, every pet of a size healed the same, as much MP as HP. Now each pet heals its own way:
- its size gives the HP a second at level 1: Tiny 4.5, Small 6, Medium 9, Large 16, Oryx 22;
- a pet that hits hard for its size heals less, a gentle one more (by the square root of how its
  damage a second, petDps, compares with its size's: 50, 125, 250, 500, 2,000), from 75% to 130%;
- healers (priests, angels, fairies, unicorns...) heal half again as much HP;
- each pet's own roll (from its id, so it stays put) moves its HP 20% either way, and its MP a
  second is about half its HP's: 40% to 60% (the mages, wisps, djinns and such 50% to 65%), never
  more than 65%;
- how often it heals: every 1.5 to 3 s for HP, 2 to 4 s for MP (its own roll too).

It rewrites the stones in place, in both copies of DoomedRealms.xml and Permapets (and keeps every
other attribute and the files' line endings); the boss pets' stones are generated
(tools/boss_pets.py uses `healing`). --check only says what would change. Unsized pets (test pets,
the snowballs) are left alone.
"""
import hashlib
import math
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = [
    "source/Shared/resources/xml/custom/DoomedRealms.xml",
    "client/src/kabam/rotmg/assets/custom/xmls/DoomedRealms.xml",
    "source/Shared/resources/xml/prod/EmbeddedData_PermapetsCXML.xml",
    "client/src/kabam/rotmg/assets/prod/xmls/Permapets.xml",
]

SIZES = ["Tiny", "Small", "Medium", "Large", "Oryx"]
HP_A_SECOND = [4.5, 6, 9, 16, 22]
DPS_NORM = [50, 125, 250, 500, 2000]
HP_MS = [1500, 2000, 2500, 3000]
MP_MS = [2000, 2500, 3000, 4000]
MP_SHARE_MAX = 0.65
HEALERS = re.compile(r"priest|healer|nurse|medic|angel|fairy|unicorn|cleric|saint|nature|mother|support", re.I)
MAGES = re.compile(r"wizard|mage|sorcer|mana|arcane|wisp|djinn|genie|warlock|witch|necromancer|mystic|oracle|spirit|star", re.I)


def _rolls(pet):
    """Three steady numbers in [0, 1) from the pet's id."""
    h = hashlib.sha256(pet.encode("utf-8")).digest()
    return [int.from_bytes(h[i:i + 4], "big") / 2 ** 32 for i in (0, 4, 8)]


def healing(pet, size, dps):
    """(petHp, petHpMs, petMp, petMpMs) at level 1 for the pet named `pet` (its object id)."""
    s = SIZES.index(size)
    roll_hp, roll_mp, roll_ms = _rolls(pet)
    ratio = max(dps, 1) / DPS_NORM[s]
    per_second = HP_A_SECOND[s] * min(1.3, max(0.75, ratio ** -0.5)) * (0.8 + 0.4 * roll_hp)
    if HEALERS.search(pet):
        per_second *= 1.5
    share = 0.5 + 0.15 * roll_mp if MAGES.search(pet) else 0.4 + 0.2 * roll_mp
    hp_ms = HP_MS[int(roll_ms * len(HP_MS))]
    mp_ms = MP_MS[int((roll_ms * 7.31) % 1 * len(MP_MS))]
    hp = max(3, round(per_second * hp_ms / 1000))
    mp = max(2, round(per_second * share * mp_ms / 1000))
    # never more MP a second than 65% of its HP, whatever the rounding did
    while mp * 1000 / mp_ms > hp * 1000 / hp_ms * MP_SHARE_MAX and mp > 1:
        mp -= 1
    return hp, hp_ms, mp, mp_ms


ACTIVATE = re.compile(r"<Activate\b([^>]*)>\s*PermaPet\s*</Activate>")


def _attr(attrs, name):
    m = re.search(r'\b%s="([^"]*)"' % name, attrs)
    return m.group(1) if m else None


def _set(attrs, name, value):
    if re.search(r'\b%s="' % name, attrs):
        return re.sub(r'\b(%s)="[^"]*"' % name, lambda m: '%s="%s"' % (m.group(1), value), attrs)
    return attrs + ' %s="%s"' % (name, value)


def rewrite(text):
    changed = []

    def fix(m):
        attrs = m.group(1)
        pet, size = _attr(attrs, "objectId"), _attr(attrs, "petSize")
        if not pet or size not in SIZES:
            return m.group(0)
        hp, hp_ms, mp, mp_ms = healing(pet, size, int(_attr(attrs, "petDps") or 0))
        new = attrs
        for k, v in (("petHp", hp), ("petHpMs", hp_ms), ("petMp", mp), ("petMpMs", mp_ms)):
            new = _set(new, k, v)
        if new != attrs:
            changed.append((pet, size, hp, hp_ms, mp, mp_ms))
        return m.group(0).replace(attrs, new, 1)

    return ACTIVATE.sub(fix, text), changed


def main():
    check = "--check" in sys.argv
    for rel in FILES:
        path = os.path.join(ROOT, rel)
        raw = open(path, "rb").read()
        crlf = b"\r\n" in raw
        text = raw.decode("utf-8-sig").replace("\r\n", "\n")
        new, changed = rewrite(text)
        print("%s: %d stones %s" % (rel, len(changed), "would change" if check else "rewritten"))
        if changed and not check:
            out = new.replace("\n", "\r\n") if crlf else new
            bom = raw.startswith(b"\xef\xbb\xbf")
            open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + out.encode("utf-8"))


if __name__ == "__main__":
    main()
