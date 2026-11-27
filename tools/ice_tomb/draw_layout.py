"""Draws an Ice Tomb layout with its real tiles and sprites (4 px a square), to see what the random
layout makes without playing:

    set BSK_LAYOUT_DUMP=<folder>
    dotnet test source\\Tests --filter IceTombTests
    python tools/ice_tomb/draw_layout.py <out.png> <folder>/seed0.json

IceTombTests writes seed0.json .. seed5.json there (each square's ground and object). Walls are drawn
by their tops, everything else by its first texture."""
import json
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
IMG = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images")
XML = os.path.join(ROOT, "source", "Shared", "resources", "xml", "custom")
S = 4
SHEETS = {"iceTomb8x8": ("IceTomb8x8.png", 8), "iceTomb16x16": ("IceTomb16x16.png", 16),
          "iceTombChars16x16": ("IceTombChars16x16.png", 16), "iceTombChars32x32": ("IceTombChars32x32.png", 32)}
sheets = {k: (Image.open(os.path.join(IMG, f)).convert("RGBA"), n) for k, (f, n) in SHEETS.items()}


def cell(file, index, animated):
    if file not in sheets:
        return None
    sh, n = sheets[file]
    x, y = (0, index * n) if animated else ((index % (sh.size[0] // n)) * n, (index // (sh.size[0] // n)) * n)
    return sh.crop((x, y, x + n, y + n))


def first_tex(el):
    for tag, anim in (("Texture", False), ("AnimatedTexture", True)):
        t = el.find(tag)
        if t is None:
            t = el.find("RandomTexture/" + tag)
        if t is not None:
            return cell(t.findtext("File"), int(t.findtext("Index"), 0), anim)
    return None


def main(out, path):
    grounds = {e.get("id"): first_tex(e) for e in ET.parse(os.path.join(XML, "IceTombGrounds.xml")).getroot()}
    objs = {}
    for e in ET.parse(os.path.join(XML, "IceTombObjects.xml")).getroot():
        top = e.find("Top")
        objs[e.get("id")] = (first_tex(e), first_tex(top) if top is not None else None, e.findtext("Class"),
                             int(e.findtext("Size") or 100))
    lay = json.load(open(path, encoding="utf-8"))
    w, h = lay["width"], lay["height"]
    img = Image.new("RGBA", (w * S, h * S), (6, 12, 28, 255))
    later = []
    for y in range(h):
        for x in range(w):
            g = grounds.get(lay["ground"][y][x])
            if g is not None:
                img.alpha_composite(g.resize((S, S), Image.NEAREST), (x * S, y * S))
            if lay["objs"][y][x]:
                later.append((x, y, lay["objs"][y][x]))
    for x, y, oid in later:
        tex, top, cls, size = objs.get(oid, (None, None, None, 100))
        if cls == "Wall":
            img.alpha_composite((top or tex).resize((S, S), Image.NEAREST), (x * S, y * S))
        elif tex is not None:
            n = int(S * (tex.size[0] / 8) * size / 100)
            img.alpha_composite(tex.resize((n, n), Image.NEAREST), (max(0, int(x * S + S / 2 - n / 2)), max(0, int(y * S + S - n))))
    img.convert("RGB").save(out)
    print(out, img.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
