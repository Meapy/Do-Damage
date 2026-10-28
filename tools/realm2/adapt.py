"""Realm 2 monsters adapted from the game's own art (tools/build_realm2.py draws them): a monster of
another dungeon close to the one wanted, its seven frames recoloured into the land's palette and,
where the land needs it, touched up pixel by pixel.

A recolouring is a list of rules, the first that takes a pixel wins:

    (selector, ramp)        selector: "grey" (little colour), "dark" (near black, the inner
                            lines), "light" (near white), ("lum", lo, hi) (a lightness from 0 to
                            1), or a hue range in degrees (lo, hi), which may wrap round 360
                            (340, 20); ramp: colours from dark to light

The pixels a rule takes keep their order of brightness, spread over the ramp (the darkest of many
shades get its first colour, the brightest its last) and up or down it as they're light or dark, so
the shading of the original carries over. Pixels no rule takes
keep their colour.

Edits are pixel maps over the stand frame, as art.py's ('.' leaves a pixel, ' ' clears it), put on
every frame at the same place unless a frame is named.
"""
import colorsys
import json
import os
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "client", "src")
MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")

_sheets = None
_objects = None
_images = {}


def _load():
    global _sheets, _objects
    if _sheets is not None:
        return
    m = json.load(open(MANIFEST, encoding="utf-8"))
    _sheets = {s["name"]: s for s in m["animated_chars"]}
    _objects = {}
    for f in m["object_xml"]:
        if "Realm2" in f:
            continue
        try:
            root = ET.parse(os.path.join(SRC, f)).getroot()
        except (ET.ParseError, OSError):
            continue
        for o in root.iter("Object"):
            if o.find("AnimatedTexture") is not None:
                _objects.setdefault(o.get("id"), o)


def _index(text):
    text = text.strip()
    return int(text, 16) if text.lower().startswith("0x") else int(text)


def strip(object_id):
    """The seven frames of another monster, as one image (its width is seven frames)."""
    _load()
    o = _objects.get(object_id)
    if o is None:
        raise SystemExit("adapt: no animated monster %r" % object_id)
    t = o.find("AnimatedTexture")
    s = _sheets[t.findtext("File")]
    path = os.path.join(SRC, s["file"])
    if path not in _images:
        _images[path] = Image.open(path).convert("RGBA")
    img = _images[path]
    cols = img.width // s["sheet_w"]
    i = _index(t.findtext("Index"))
    x, y = (i % cols) * s["sheet_w"], (i // cols) * s["sheet_h"]
    return img.crop((x, y, x + s["sheet_w"], y + s["sheet_h"]))


def _hsl(c):
    r, g, b = (v / 255.0 for v in c[:3])
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    return h * 360, s, l


def _takes(selector, c):
    h, s, l = _hsl(c)
    if selector == "dark":
        return l < 0.12
    if selector == "light":
        return l > 0.86 and s < 0.5
    if selector == "grey":
        return s < 0.16 or (max(c[:3]) - min(c[:3])) < 26
    if selector[0] == "lum":
        return selector[1] <= l < selector[2]
    lo, hi = selector
    if s < 0.16 or (max(c[:3]) - min(c[:3])) < 26:
        return False
    return lo <= h < hi if lo <= hi else (h >= lo or h < hi)


def _brightness(c):
    r, g, b = c[:3]
    return 0.3 * r + 0.55 * g + 0.15 * b


def recolour(img, rules, contrast=1.0):
    """The image with each rule's pixels mapped onto its ramp by their brightness among them."""
    px = img.load()
    groups = [[] for _ in rules]
    for y in range(img.height):
        for x in range(img.width):
            c = px[x, y]
            if c[3] == 0:
                continue
            for k, (sel, _) in enumerate(rules):
                if _takes(sel, c):
                    groups[k].append((x, y))
                    break
    out = img.copy()
    po = out.load()
    for (sel, ramp), pts in zip(rules, groups):
        if not pts:
            continue
        ramp = [_rgb(c) for c in ramp]
        values = sorted(set(_brightness(px[p]) for p in pts))
        rank = {v: (i / (len(values) - 1) if len(values) > 1 else 0.5) for i, v in enumerate(values)}
        lo, hi = values[0], values[-1]
        # a colour with few shades, or close ones, isn't stretched over the whole ramp: its spread
        # counts as at least 90 of 255, and it sits up or down the ramp as it's light or dark
        span = max(hi - lo, 90.0)
        centre = (lo + hi) / 2
        lift = (centre / 255.0 - 0.5) * 0.6
        for p in pts:
            v = _brightness(px[p])
            f = 0.5 + (v - centre) / span + lift
            if len(values) >= 4:
                # with several shades, half by rank, so every shade keeps a step of its own
                f = 0.5 * f + 0.5 * rank[v]
            f = min(1.0, max(0.0, 0.5 + (f - 0.5) * contrast))
            c = ramp[min(len(ramp) - 1, int(f * len(ramp)))]
            po[p] = c + (px[p][3],)
    return out


def _rgb(c):
    if isinstance(c, tuple):
        return c[:3]
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))


def edit(img, rows, pal, at=(0, 0), frames=None, size=None):
    """Pixel maps put over frames of a strip (all, or those numbered in `frames`)."""
    out = img.copy()
    size = size or img.height
    po = out.load()
    for f in (frames if frames is not None else range(img.width // size)):
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch == ".":
                    continue
                X, Y = f * size + at[0] + x, at[1] + y
                if not (0 <= X < out.width and 0 <= Y < out.height):
                    continue
                po[X, Y] = (0, 0, 0, 0) if ch == " " else _rgb(pal[ch]) + (255,)
    return out


def adapt(spec, palette):
    """A monster's strip from its recipe: {"from": id, "rules": [...], "edits": [(rows, at, frames)],
    "contrast": 1.0}. Ramp colours may be palette letters."""
    def colour(c):
        return palette[c] if isinstance(c, str) and c in palette else c
    rules = [(sel, [colour(c) for c in ramp]) for sel, ramp in spec["rules"]]
    img = recolour(strip(spec["from"]), rules, spec.get("contrast", 1.0))
    pal = {k: (v if isinstance(v, str) else "#%02x%02x%02x" % v) for k, v in palette.items()}
    pal.update(spec.get("colours", {}))
    for rows, at, frames in spec.get("edits", []):
        img = edit(img, rows, pal, at, frames)
    return img
