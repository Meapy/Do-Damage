"""Rework the mystic orbs: no more stasis; they shred armour instead.

    python tools/rework_orbs.py

Every orb (slot type 21) whose ability was a StasisBlast loses it for an ArmorShred: enemies
within its radius (3 tiles of the aim, as the stasis was) lose a share of their defense for a
while (Enemy.ShredArmor on the server). By tier, 2% for 5 seconds at T0 up to 8% for 12 seconds
at T8; an untiered orb counts as the tier its stasis lasted for (3 seconds at T0, half a second
more a tier). Every orb then has a cooldown of at least 2 seconds.

Edits the server's and the client's copies of the XML in place, as text, so nothing else in the
files moves. Running it again changes nothing. tools/port_doomed.py runs it after it writes
DoomedRealms.xml, so the Doomed Realms orbs keep it.
"""
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = [
    os.path.join(ROOT, "source", "Shared", "resources", "xml", "prod", "EmbeddedData_EquipCXML.xml"),
    os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "prod", "xmls", "Equip.xml"),
    os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom", "DoomedRealms.xml"),
    os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "xmls", "DoomedRealms.xml"),
]

# by tier, 0 to 8: the share of defense taken (percent) and for how long (seconds)
SHRED = [2, 3, 4, 4, 5, 6, 7, 7, 8]
SECONDS = [5, 6, 7, 8, 9, 9, 10, 11, 12]
COOLDOWN = 2

OBJECT = re.compile(r"<Object [^>]*>.*?</Object>", re.S)
STASIS = re.compile(r'([ \t]*)<Activate([^>]*)>StasisBlast</Activate>')


def tier_of(block, stasis_attrs):
    m = re.search(r"<Tier>(\d+)</Tier>", block)
    if m:
        return min(int(m.group(1)), 8)
    m = re.search(r'duration="([\d.]+)"', stasis_attrs)
    seconds = float(m.group(1)) if m else 3
    return max(0, min(8, round((seconds - 3) * 2)))


def rework(block):
    if not re.search(r"<SlotType>21</SlotType>", block):
        return block
    m = STASIS.search(block)
    if m:
        indent, attrs = m.group(1), m.group(2)
        tier = tier_of(block, attrs)
        radius = re.search(r'radius="([\d.]+)"', attrs)
        shred = f'{indent}<Activate amount="{SHRED[tier]}" duration="{SECONDS[tier]}" radius="{radius.group(1) if radius else 3}">ArmorShred</Activate>'
        block = block[:m.start()] + shred + block[m.end():]
    # at least the base cooldown (the Orb of Aether keeps its 5 seconds)
    cd = re.search(r"<Cooldown>([^<]*)</Cooldown>", block)
    if cd:
        if float(cd.group(1)) < COOLDOWN:
            block = block[:cd.start()] + f"<Cooldown>{COOLDOWN}</Cooldown>" + block[cd.end():]
    else:
        at = re.search(r"([ \t]*)<MpCost>[^<]*</MpCost>", block) or re.search(r"([ \t]*)<SlotType>21</SlotType>", block)
        block = block[:at.end()] + f"\n{at.group(1)}<Cooldown>{COOLDOWN}</Cooldown>" + block[at.end():]
    return block


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
            print(f"{os.path.relpath(path, ROOT)}: {rework_file(path)} orbs")


if __name__ == "__main__":
    main()
