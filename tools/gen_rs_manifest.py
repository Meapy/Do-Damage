"""Write client-rs/assets/manifest.json: the asset list of the AIR client, for the Rust client.

Usage:
    python tools/gen_rs_manifest.py

The AIR client embeds its sprite sheets, 3D models and game data with [Embed] tags and registers
them in code (AssetLoader.as, DoomedAssets.as, EmbeddedData.as). This script reads those files and
writes the same registrations as JSON, so the Rust client loads exactly what the AIR client loads,
from the same files. Run it again whenever those files change; don't edit the JSON by hand.

Sheets, characters and game data only the Rust client uses (`RS_ONLY_SETS`, `RS_ONLY_CHARS`,
`RS_ONLY_GROUND_XML`, `RS_ONLY_OBJECT_XML`: art made after the AIR client was retired) are added
after the AIR client's.

All paths in the manifest are relative to client/src.
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "client", "src")
OUT = os.path.join(ROOT, "client-rs", "assets", "manifest.json")

ASSET_LOADER = "com/company/assembleegameclient/util/AssetLoader.as"
EMBEDDED_ASSETS = "kabam/rotmg/assets/EmbeddedAssets.as"
EMBEDDED_DATA = "kabam/rotmg/assets/EmbeddedData.as"
DOOMED_ASSETS = "kabam/rotmg/assets/doomed/DoomedAssets.as"
EMOTES = "kabam/rotmg/emotes/Emotes.as"
EMOTES_XML = "kabam/rotmg/assets/custom/xmls/EmotesXML.xml"
# (name, file, cell width, cell height): sheets the AIR client doesn't register
RS_ONLY_SETS = [
    # the Divine Forge (tools/draw_forge_sprite.py)
    ("customAssets32x32", "kabam/rotmg/assets/custom/images/CustomAssets32x32.png", 32, 32),
    # Oryx's Inferno's burnt sanctuary tiles and portal (tools/build_hash_cellar.py)
    ("hashCellar8x8", "kabam/rotmg/assets/custom/images/HashCellar8x8.png", 8, 8),
    ("hashCellar16x16", "kabam/rotmg/assets/custom/images/HashCellar16x16.png", 16, 16),
    ("hashCellar32x32", "kabam/rotmg/assets/custom/images/HashCellar32x32.png", 32, 32),
    # Oryx's Elysium's marble and gold tiles, gates, portals and divine shots (tools/build_elysium.py)
    ("elysium8x8", "kabam/rotmg/assets/custom/images/Elysium8x8.png", 8, 8),
    ("elysium16x16", "kabam/rotmg/assets/custom/images/Elysium16x16.png", 16, 16),
    ("elysium32x32", "kabam/rotmg/assets/custom/images/Elysium32x32.png", 32, 32),
    # the gateways of Oryx's Inferno and Oryx's Elysium, and the Oryx chain's incantations
    # (tools/draw_oryx_gates.py)
    ("oryxGates32x32", "kabam/rotmg/assets/custom/images/OryxGates32x32.png", 32, 32),
    ("oryxIncantations16x16", "kabam/rotmg/assets/custom/images/OryxIncantations16x16.png", 16, 16),
    # the T15 weapons, their T14s in Oryx 5's colours, and the T16 armours, their T15s in them
    # (tools/draw_t15_weapons.py)
    ("t15Weapons8x8", "kabam/rotmg/assets/custom/images/T15Weapons8x8.png", 8, 8),
    ("t16Armors8x8", "kabam/rotmg/assets/custom/images/T16Armors8x8.png", 8, 8),
    # the forge shards and Oryx 5's Ascended pieces (tools/build_ascended_items.py)
    ("ascendedItems8x8", "kabam/rotmg/assets/custom/images/AscendedItems8x8.png", 8, 8),
    # ability shots recoloured for items of their own (tools/draw_ability_fx.py)
    ("abilityFx8x8", "kabam/rotmg/assets/custom/images/AbilityFx8x8.png", 8, 8),
]
# (name, file, frame width, frame height, sheet width, sheet height, direction): characters
RS_ONLY_CHARS = [
    # Oryx's Inferno's Infernal Court and Oryx 4 (tools/build_hash_cellar.py)
    ("hashCellarChars16x16", "kabam/rotmg/assets/custom/images/HashCellarChars16x16.png", 16, 16, 112, 16, "right"),
    ("hashCellarOryx16x16", "kabam/rotmg/assets/custom/images/HashCellarOryx16x16.png", 16, 16, 112, 16, "down"),
    # Oryx's Elysium's Ascended Host and guardians, and Oryx 5 (tools/build_elysium.py)
    ("elysiumChars16x16", "kabam/rotmg/assets/custom/images/ElysiumChars16x16.png", 16, 16, 112, 16, "right"),
    ("elysiumOryx16x16", "kabam/rotmg/assets/custom/images/ElysiumOryx16x16.png", 16, 16, 112, 16, "down"),
]
# game data only the Rust client loads
RS_ONLY_GROUND_XML = ["kabam/rotmg/assets/custom/xmls/HashCellarGrounds.xml",
                      "kabam/rotmg/assets/custom/xmls/ElysiumGrounds.xml"]
RS_ONLY_OBJECT_XML = ["kabam/rotmg/assets/custom/xmls/HashCellarObjects.xml",
                      "kabam/rotmg/assets/custom/xmls/ElysiumObjects.xml"]
ADD_EMOTE = re.compile(r'addEmote\("((?:[^"\\]|\\.)*)",\s*AssetLibrary\.getImageFromSet\("(\w+)",\s*(\d+)\)(?:,\s*([\d.]+))?')

EMBED_VAR = re.compile(
    r'\[Embed\(source="([^"]+)"[^\]]*\)\]\s*(?:public|private)\s+static\s+(?:var|const)\s+(\w+)\s*:\s*Class')
EMBED_CLASS = re.compile(r'\[Embed\(source="([^"]+)"[^\]]*\)\]\s*public\s+class\s+(\w+)')
ALIAS = re.compile(r'static\s+(?:var|const)\s+(\w+)\s*:\s*Class\s*=\s*(\w+)\s*;')
IMPORT = re.compile(r'import\s+([\w.]+)\s*;')
IMAGE_SET = re.compile(
    r'AssetLibrary\.addImageSet\("(\w+)",\s*new\s+([\w.]+)\(\)\.bitmapData,\s*(\d+),\s*(\d+)\)')
INVISIBLE = re.compile(r'AssetLibrary\.addImageSet\("(\w+)",\s*new\s+BitmapDataSpy\((\d+),\s*(\d+)')
ANIM_CHARS = re.compile(
    r'AnimatedChars\.add\("(\w+)",\s*new\s+([\w.]+)\(\)\.bitmapData,\s*(null|new\s+[\w.]+\(\)\.bitmapData),'
    r'\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*AnimatedChar\.(RIGHT|DOWN)\)')
MODEL = re.compile(r'"([^"]+)"\s*:\s*new\s+(\w+)\(\)')


def read(rel):
    with open(os.path.join(SRC, rel), encoding="utf-8", errors="replace") as f:
        return f.read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def norm(path):
    return os.path.relpath(path, SRC).replace("\\", "/")


class Scope:
    """The embedded files a class can name: its own [Embed] vars, plus aliases to embed classes."""

    def __init__(self, rel):
        self.rel = rel
        self.dir = os.path.dirname(os.path.join(SRC, rel))
        text = read(rel)
        self.files = {name: norm(os.path.join(self.dir, src)) for src, name in EMBED_VAR.findall(text)}
        code = strip_comments(text)
        imports = {imp.rsplit(".", 1)[1]: imp for imp in IMPORT.findall(code)}
        package_dir = os.path.dirname(rel)
        for alias, target in ALIAS.findall(code):
            path = embed_class_file(imports.get(target), package_dir, target)
            if path:
                self.files[alias] = path

    def resolve(self, expr, owner):
        """`new EmbeddedAssets.foo()` or `new foo()` inside `owner` -> file path."""
        name = expr.split(".")[-1]
        if "." in expr and expr.split(".")[0] != owner:
            raise ValueError(f"{self.rel}: can't resolve {expr}")
        if name not in self.files:
            raise ValueError(f"{self.rel}: no embedded file for {expr}")
        return self.files[name]


def embed_class_file(qualified, package_dir, name):
    """The file a class-level [Embed] (class X extends BitmapAsset) points at."""
    candidates = []
    if qualified:
        candidates.append(qualified.replace(".", "/") + ".as")
    candidates.append(f"{package_dir}/{name}.as")
    for sub in ("custom/images", "custom/objects", "custom/xmls"):
        candidates.append(f"kabam/rotmg/assets/{sub}/{name}.as")
    for rel in candidates:
        full = os.path.join(SRC, rel)
        if os.path.exists(full):
            m = EMBED_CLASS.search(read(rel))
            if m and m.group(2) == name:
                return norm(os.path.join(os.path.dirname(full), m.group(1)))
    return None


def image_sets(code, scope, owner):
    sets = [{"name": n, "file": scope.resolve(e, owner), "w": int(w), "h": int(h)}
            for n, e, w, h in IMAGE_SET.findall(code)]
    sets += [{"name": n, "file": None, "w": int(w), "h": int(h)} for n, w, h in INVISIBLE.findall(code)]
    return sets


def animated_chars(code, scope, owner):
    chars = []
    for name, expr, mask, w, h, sw, sh, direction in ANIM_CHARS.findall(code):
        mask_file = None
        if mask != "null":
            mask_file = scope.resolve(re.match(r"new\s+([\w.]+)", mask).group(1), owner)
        chars.append({"name": name, "file": scope.resolve(expr, owner), "mask": mask_file,
                      "w": int(w), "h": int(h), "sheet_w": int(sw), "sheet_h": int(sh),
                      "dir": direction.lower()})
    return chars


def data_files(code, scope, array):
    m = re.search(r"static\s+const\s+" + array + r"\s*:\s*Array\s*=\s*\[(.*?)\]", code, re.S)
    if not m:
        raise ValueError(f"EmbeddedData.as: no {array}")
    return [scope.resolve(name, "EmbeddedData") for name in re.findall(r"new\s+(\w+)\(\)", m.group(1))]


def emotes():
    """`Emotes.load`: the XML's emotes and animated emotes, then the ones added in code (a later
    code replaces an earlier one). Each is the text that turns into it, its frames and scale."""
    import xml.etree.ElementTree as ET
    found = {}
    root = ET.fromstring(read(EMOTES_XML))
    for e in root.iter("Emote"):
        tex = e.find("Texture")
        found[":" + e.get("id") + ":"] = {"frames": [[tex.findtext("File"), int(tex.findtext("Index"), 0)]],
                                           "scale": float(e.get("scale") or 1)}
    for e in root.iter("AnimatedEmote"):
        frames = [[f.findtext("File"), int(f.findtext("Index"), 0)] for f in e.iter("Frame")]
        found[":" + e.get("id") + ":"] = {"frames": frames, "scale": float(e.get("scale") or 1)}
    code = strip_comments(read(EMOTES))
    for text, sheet, index, scale in ADD_EMOTE.findall(code):
        text = text.replace("\\'", "'").replace('\\"', '"')
        found[text] = {"frames": [[sheet, int(index)]], "scale": float(scale or 1)}
    return [{"code": k, **v} for k, v in found.items()]


def main():
    assets = Scope(EMBEDDED_ASSETS)
    doomed = Scope(DOOMED_ASSETS)
    data = Scope(EMBEDDED_DATA)

    loader = strip_comments(read(ASSET_LOADER))
    doomed_code = strip_comments(read(DOOMED_ASSETS))
    assets_code = strip_comments(read(EMBEDDED_ASSETS))
    data_code = strip_comments(read(EMBEDDED_DATA))

    models_block = re.search(r"models_\s*:\s*Object\s*=\s*\{(.*?)\}", assets_code, re.S).group(1)

    manifest = {
        "_generated_by": "tools/gen_rs_manifest.py; paths are relative to client/src",
        # The AIR client registers in this order; later registrations of a name replace earlier ones.
        "image_sets": image_sets(loader, assets, "EmbeddedAssets") + image_sets(doomed_code, doomed, "DoomedAssets")
                      + [{"name": n, "file": f, "w": w, "h": h} for n, f, w, h in RS_ONLY_SETS],
        "animated_chars": animated_chars(loader, assets, "EmbeddedAssets")
                          + animated_chars(doomed_code, doomed, "DoomedAssets")
                          + [{"name": n, "file": f, "mask": None, "w": w, "h": h, "sheet_w": sw, "sheet_h": sh, "dir": d}
                             for n, f, w, h, sw, sh, d in RS_ONLY_CHARS],
        "models": [{"name": n, "file": assets.resolve(e, "EmbeddedAssets")} for n, e in MODEL.findall(models_block)],
        "particles": assets.resolve("particlesEmbed", "EmbeddedAssets"),
        "ground_xml": data_files(data_code, data, "groundFiles") + RS_ONLY_GROUND_XML,
        "object_xml": data_files(data_code, data, "objectFiles") + RS_ONLY_OBJECT_XML,
        "region_xml": data_files(data_code, data, "regionFiles"),
        "fonts": {"regular": "com/company/ui/fonts/MyriadPro.otf", "bold": "com/company/ui/fonts/MyriadProBold.otf"},
        # The title screen's background (TitleView's NewHomeScreen).
        "screens": {"title": "kabam/rotmg/ui/view/ProjectOryx1.png"},
        # Emotes: words in chat and speech balloons drawn as pictures.
        "emotes": emotes(),
    }

    missing = []
    for section in ("image_sets", "animated_chars", "models"):
        for entry in manifest[section]:
            for key in ("file", "mask"):
                if entry.get(key) and not os.path.exists(os.path.join(SRC, entry[key])):
                    missing.append(entry[key])
    for section in ("ground_xml", "object_xml", "region_xml"):
        missing += [p for p in manifest[section] if not os.path.exists(os.path.join(SRC, p))]
    missing += [p for p in manifest["screens"].values() if not os.path.exists(os.path.join(SRC, p))]
    if missing:
        sys.exit("missing files:\n  " + "\n  ".join(sorted(set(missing))))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")
    print(f"{norm(OUT) if OUT.startswith(SRC) else os.path.relpath(OUT, ROOT)}: "
          f"{len(manifest['image_sets'])} image sets, {len(manifest['animated_chars'])} animated sheets, "
          f"{len(manifest['models'])} models, {len(manifest['ground_xml'])} ground, "
          f"{len(manifest['object_xml'])} object, {len(manifest['region_xml'])} region files, "
          f"{len(manifest['emotes'])} emotes")


if __name__ == "__main__":
    main()
