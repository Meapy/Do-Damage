"""Strengthen the assassin's poisons.

    python tools/rework_poisons.py

Every poison (slot type 18) with a PoisonGrenade ability gets a bigger cloud (radius x1.25),
more damage (impact and poison damage x1.5) and a throw that reaches twice as far
(`throwRange="28"`, read by Player.AEPoisonGrenade; other abilities keep the 14-tile cap).

Edits the server's and the client's copies of the XML in place, as text. The `throwRange`
attribute marks a poison as done, so running it again changes nothing.
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

RADIUS = 1.25
DAMAGE = 1.5
THROW_RANGE = 28

OBJECT = re.compile(r"<Object [^>]*>.*?</Object>", re.S)
POISON = re.compile(r"<Activate([^>]*)>PoisonGrenade</Activate>")


def num(v):
    return ("%.3f" % v).rstrip("0").rstrip(".")


def scale_attr(attrs, name, factor, whole):
    def one(m):
        v = float(m.group(1)) * factor
        return '%s="%s"' % (name, str(int(v + 0.5)) if whole else num(v))
    return re.sub(r'\b%s="([\d.]+)"' % name, one, attrs)


def rework(block):
    if not re.search(r"<SlotType>18</SlotType>", block):
        return block

    def one(m):
        attrs = m.group(1)
        if "throwRange=" in attrs:
            return m.group(0)
        attrs = scale_attr(attrs, "radius", RADIUS, False)
        attrs = scale_attr(attrs, "totalDamage", DAMAGE, True)
        attrs = scale_attr(attrs, "impactDamage", DAMAGE, True)
        return '<Activate%s throwRange="%d">PoisonGrenade</Activate>' % (attrs, THROW_RANGE)

    return POISON.sub(one, block)


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
            print(f"{os.path.relpath(path, ROOT)}: {rework_file(path)} poisons")


if __name__ == "__main__":
    main()
