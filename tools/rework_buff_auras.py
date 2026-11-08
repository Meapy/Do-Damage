"""Widen the party buffs: every class ability that buffs or heals the players around you reaches
further.

    python tools/rework_buff_auras.py

The Warrior's helms, the Bard's lutes and the Blademaster's jackets reached 4 to 4.5 tiles at every
tier, about half a screen, so a party had to stand on top of each other to get Berserk, Inspired or
Armored. Each ability's party effects (ConditionEffectAura, StatBoostAura, HealNova, Halo, a
GenericActivate aimed at players, the tomes' cleanses) now reach at least a floor by tier, and
nothing that reached further already is shortened:

- effects without useWisMod (helms, lutes, jackets, the Monk's fountains, the Ascendant's halos):
  6 tiles at T0 up to 8 at T8, 7 when untiered;
- effects scaled by wisdom (seals, tomes): 5 tiles at T0 up to 7 at T8, 6.5 when untiered,
  before wisdom (75 wisdom makes that half as much again).

The Snake Charmer Pungi, the one lute that only buffed the bard, shares its Speedy and dexterity
with the party, as the other lutes do.

Edits the server's and the client's copies of the XML in place, as text, so nothing else in the
files moves. Running it again changes nothing. tools/port_doomed.py, tools/build_resurrection.py
and tools/build_ascended_items.py run it after they write their XML, so their items keep it.
"""
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVER = os.path.join(ROOT, "source", "Shared", "resources", "xml")
CLIENT = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets")
FILES = [
    os.path.join(SERVER, "prod", "EmbeddedData_EquipCXML.xml"),
    os.path.join(CLIENT, "prod", "xmls", "Equip.xml"),
    os.path.join(SERVER, "custom", "CustomItems.xml"),
    os.path.join(CLIENT, "custom", "xmls", "CustomItems.xml"),
    os.path.join(SERVER, "custom", "DoomedRealms.xml"),
    os.path.join(CLIENT, "custom", "xmls", "DoomedRealms.xml"),
    os.path.join(SERVER, "custom", "Resurrection.xml"),
    os.path.join(CLIENT, "custom", "xmls", "Resurrection.xml"),
]

# the abilities that buff the party: Warrior helm, Paladin seal, Priest tome, Bard lute,
# Blademaster jacket, Monk fountain, Ascendant halo
SLOTS = {"16", "12", "4", "28", "37", "130", "126"}
PARTY = {"ConditionEffectAura", "StatBoostAura", "HealNova", "Halo", "ClearConditionEffectAura",
         "RemoveNegativeConditions"}

# the least range, by tier 0 to 8, and for an untiered item
PLAIN = [6, 6, 6.5, 6.5, 7, 7, 7.5, 7.5, 8]
PLAIN_UT = 7
WIS = [5, 5, 5.5, 5.5, 6, 6, 6.5, 6.5, 7]
WIS_UT = 6.5

OBJECT = re.compile(r"<Object [^>]*>.*?</Object>", re.S)
ACTIVATE = re.compile(r"<Activate( [^>]*)>([A-Za-z]+)</Activate>")
RANGE = re.compile(r'(\srange=")([\d.]+)(")')

# the Snake Charmer Pungi: its two self buffs become the party's
PUNGI_SELF = [
    ('<Activate duration="4" effect="Speedy">ConditionEffectSelf</Activate>',
     '<Activate duration="4" effect="Speedy" range="{r}">ConditionEffectAura</Activate>'),
    ('<Activate stat="21" amount="8" duration="2.5">StatBoostSelf</Activate>',
     '<Activate stat="21" amount="8" duration="2.5" range="{r}" noStack="true">StatBoostAura</Activate>'),
]


def fmt(v):
    return str(int(v)) if v == int(v) else str(v)


def floor_for(tier, wis):
    if tier is None:
        return WIS_UT if wis else PLAIN_UT
    return (WIS if wis else PLAIN)[min(tier, 8)]


def is_party(name, attrs):
    if name in PARTY:
        return not (name == "StatBoostAura" and re.search(r'amount="-', attrs))
    return name == "GenericActivate" and 'target="player"' in attrs


def rework(block):
    slot = re.search(r"<SlotType>(\d+)</SlotType>", block)
    if not slot or slot.group(1) not in SLOTS:
        return block
    m = re.search(r"<Tier>(\d+)</Tier>", block)
    tier = int(m.group(1)) if m else None

    if re.search(r'id="Snake Charmer Pungi"', block):
        for old, new in PUNGI_SELF:
            block = block.replace(old, new.format(r=fmt(PLAIN_UT)))

    def one(a):
        attrs, name = a.group(1), a.group(2)
        if not is_party(name, attrs) or not RANGE.search(attrs):
            return a.group(0)
        least = floor_for(tier, 'useWisMod="true"' in attrs)
        attrs = RANGE.sub(lambda r: r.group(1) + fmt(max(float(r.group(2)), least)) + r.group(3), attrs)
        return f"<Activate{attrs}>{name}</Activate>"

    return ACTIVATE.sub(one, block)


def rework_file(path):
    with open(path, "rb") as f:
        raw = f.read()
    text = raw.decode("utf-8-sig")
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    count = 0

    def one(m):
        nonlocal count
        new = rework(m.group(0))
        count += new != m.group(0)
        return new

    out = OBJECT.sub(one, text)
    if crlf:
        out = out.replace("\n", "\r\n")
    data = (b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + out.encode("utf-8")
    if data != raw:
        with open(path, "wb") as f:
            f.write(data)
    return count


def main():
    for path in FILES:
        if os.path.exists(path):
            print(f"{os.path.relpath(path, ROOT)}: {rework_file(path)} abilities")


if __name__ == "__main__":
    main()
