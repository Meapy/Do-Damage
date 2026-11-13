"""Take the sprites players drew in the Sprite Editor, once admins approve them, into the game.

    python tools/community_sprites.py list   [--server https://dodmg.com/api]
    python tools/community_sprites.py apply  [--server ...] [--only 12,15] [--dry-run] [--commit]
    python tools/community_sprites.py rework
    python tools/community_sprites.py check  [--server ...] [--offline]

Players draw in the client's Sprite Editor (main menu) and send their sprites to the account
server (SpritesController): a new look for an item, or the art for an item still to be made.
Admins approve or reject them in the editor's Review tab. `apply` fetches what's approved
(`<server>/sprites/approved`, public) and, for each one not in the catalog yet:

  * draws it into the next free cell of the community sheet of its size,
    client/src/kabam/rotmg/assets/custom/images/CommunitySprites8x8.png or 16x16 (a row of 16
    cells is added when the sheet is full; cells are never reused);
  * adds it to the catalog, source/Shared/resources/data/community_sprites.json (who drew it,
    which cell, what for);
  * for a new look: points the item's <Texture> at that cell in every copy of the XML (client
    and server), as text, so nothing else in the files moves;
  * puts a line in client-rs/CHANGELOG.md (under Unreleased, Items) and its part in PR-BODY.md.

`--commit` then commits exactly those files. Run it in a worktree rebased onto the open release
branch (AGENTS.md, Git, "Release branches"), and bring the commit in as any other work: the
sprites reach players with that release. The catalog ships with the servers, so once the release
is deployed the account server shows those submissions as in the game, and stops listing them
as approved.

`rework` points the items at their cells again from the catalog: tools/port_doomed.py and
tools/make_elder.py run it after they regenerate their XML, so a regeneration keeps the players'
sprites. Running it again changes nothing.

`check` is one of the release checks (AGENTS.md, Git, "Releasing"): it fails while sprites approved
on the server haven't been taken in, or while the catalog, the sheets and the XML are out of step
(an empty cell, an item gone, an item no longer pointed at its sprite or credited). `--offline`
checks only the repository.

Everything the server sends is checked again here as untrusted: ids, sizes and pixels must be
well formed, an item must exist in the XML, and names are cleaned before they go into Markdown.
"""
import argparse
import datetime
import glob
import html
import json
import os
import re
import subprocess
import sys
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMAGES = os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "custom", "images")
CATALOG = os.path.join(ROOT, "source", "Shared", "resources", "data", "community_sprites.json")
CHANGELOG = os.path.join(ROOT, "client-rs", "CHANGELOG.md")
PR_BODY = os.path.join(ROOT, "PR-BODY.md")
XML_GLOBS = [
    os.path.join(ROOT, "client", "src", "kabam", "rotmg", "assets", "**", "xmls", "*.xml"),
    os.path.join(ROOT, "source", "Shared", "resources", "xml", "**", "*.xml"),
]
DEFAULT_SERVER = "https://dodmg.com/api"

# by size: the sheet's name in the manifest (tools/gen_rs_manifest.py) and its file
SHEETS = {
    8: ("communitySprites8x8", os.path.join(IMAGES, "CommunitySprites8x8.png")),
    16: ("communitySprites16x16", os.path.join(IMAGES, "CommunitySprites16x16.png")),
}
COLUMNS = 16
SLOTS = {"weapon", "ability", "armor", "ring", "consumable", "other"}

OBJECT = re.compile(r'<Object\b[^>]*?\bid="([^"]*)"[^>]*>.*?</Object>', re.S)
TEXTURE = re.compile(r"<Texture>(.*?)</Texture>", re.S)
ANIMATED = re.compile(r"<AnimatedTexture>.*?</AnimatedTexture>", re.S)

CHANGELOG_PREFIX = "- New sprites drawn by players in the Sprite Editor: "
PR_PLAYERS_PREFIX = "- **Player sprites:** new looks drawn by players in the Sprite Editor: "
PR_DETAILS_HEAD = "### Player sprites (tools/community_sprites.py)"


# ---- the catalog --------------------------------------------------------------------------------

def load_catalog():
    if not os.path.exists(CATALOG):
        return {"sprites": []}
    with open(CATALOG, encoding="utf-8") as f:
        cat = json.load(f)
    cat.setdefault("sprites", [])
    return cat


def save_catalog(cat):
    with open(CATALOG, "w", encoding="utf-8", newline="\n") as f:
        json.dump(cat, f, indent=2, ensure_ascii=False)
        f.write("\n")


# ---- what the server sends (untrusted) ----------------------------------------------------------

def clean(text, limit):
    """One line of plain text: no control or markup characters, nothing Markdown acts on."""
    text = "".join(" " if ord(c) < 32 or ord(c) == 127 else c for c in (text or ""))
    text = re.sub(r"[<>&`*_\[\]|\\#@]", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:limit].strip()


def fetch(server):
    parts = urllib.parse.urlparse(server)
    local = parts.hostname in ("localhost", "127.0.0.1", "::1")
    if parts.scheme != "https" and not (parts.scheme == "http" and local):
        sys.exit(f"Use an https address (or http on this PC): {server}")
    url = server.rstrip("/") + "/sprites/approved"
    req = urllib.request.Request(url, headers={"User-Agent": "community-sprites"})
    with urllib.request.urlopen(req, timeout=30) as r:
        body = r.read(4 * 1024 * 1024)
    return parse(body.decode("utf-8"))


def parse(text):
    """The approved submissions in the server's answer, each checked; the bad ones are reported and left out."""
    root = ET.fromstring(text)
    if root.tag != "Approved":
        sys.exit(f"Not a list of approved sprites: <{root.tag}>")
    out = []
    for el in root.findall("Sprite"):
        def get(tag):
            return (el.findtext(tag) or "").strip()
        try:
            sid = int(el.get("id", ""))
            size = int(get("Size"))
        except ValueError:
            print("  skipped: a sprite without a proper id or size")
            continue
        pixels = get("Pixels").lower()
        kind = get("Kind")
        problem = None
        if sid <= 0:
            problem = "bad id"
        elif size not in SHEETS:
            problem = f"size {size}"
        elif len(pixels) != size * size * 8 or not re.fullmatch(r"[0-9a-f]*", pixels):
            problem = "bad pixels"
        elif kind not in ("item", "new"):
            problem = f"kind {kind!r}"
        elif kind == "new" and get("Slot") not in SLOTS:
            problem = "bad kind of item"
        if problem:
            print(f"  skipped #{sid}: {problem}")
            continue
        out.append({
            "id": sid,
            "kind": kind,
            # an item's id is matched exactly against the XML, never written anywhere as is
            "item": get("Item")[:128] if kind == "item" else "",
            "name": clean(get("Name"), 32) if kind == "new" else "",
            "slot": get("Slot") if kind == "new" else "",
            "note": clean(get("Note"), 200),
            "author": clean(get("Author"), 32) or "a player",
            "approvedBy": clean(get("ReviewedBy"), 32),
            "size": size,
            "pixels": pixels,
        })
    return out


# ---- the sheets ---------------------------------------------------------------------------------

def draw(sheet_file, index, size, pixels):
    from PIL import Image
    img = Image.open(sheet_file).convert("RGBA")
    rows_needed = index // COLUMNS + 1
    if img.height < rows_needed * size or img.width < COLUMNS * size:
        grown = Image.new("RGBA", (max(img.width, COLUMNS * size), rows_needed * size), (0, 0, 0, 0))
        grown.paste(img, (0, 0))
        img = grown
    x0, y0 = (index % COLUMNS) * size, (index // COLUMNS) * size
    for i in range(size * size):
        px = pixels[i * 8:i * 8 + 8]
        r, g, b, a = (int(px[k:k + 2], 16) for k in (0, 2, 4, 6))
        img.putpixel((x0 + i % size, y0 + i // size), (r, g, b, 255) if a >= 0x80 else (0, 0, 0, 0))
    img.save(sheet_file)


def next_index(cat, sheet):
    used = [s["index"] for s in cat["sprites"] if s.get("sheet") == sheet]
    return max(used) + 1 if used else 0


# ---- the XML ------------------------------------------------------------------------------------

def xml_files():
    files = []
    for g in XML_GLOBS:
        files += glob.glob(g, recursive=True)
    return sorted(set(files))


def item_ids():
    ids = set()
    for path in xml_files():
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for m in OBJECT.finditer(f.read()):
                ids.add(html.unescape(m.group(1)))
    return ids


MANIFEST = os.path.join(ROOT, "client-rs", "assets", "manifest.json")


def current_pixels(item, size):
    """The item's sprite now, scaled to size (nearest), as RGBA rows; None when it can't be found."""
    from PIL import Image
    try:
        for path in xml_files():
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                text = f.read()
            for m in OBJECT.finditer(text):
                if html.unescape(m.group(1)) != item:
                    continue
                t = TEXTURE.search(m.group(0))
                if not t:
                    return None
                sheet = re.search(r"<File>([^<]*)</File>", t.group(1)).group(1).strip()
                index = int(re.search(r"<Index>([^<]*)</Index>", t.group(1)).group(1).strip(), 0)
                with open(MANIFEST, encoding="utf-8") as mf:
                    entry = next(e for e in json.load(mf)["image_sets"] if e["name"] == sheet)
                img = Image.open(os.path.join(ROOT, "client", "src", entry["file"])).convert("RGBA")
                w, h = entry["w"], entry["h"]
                cols = img.width // w
                x0, y0 = (index % cols) * w, (index // cols) * w
                return img.crop((x0, y0, x0 + w, y0 + h)).resize((size, size), Image.NEAREST)
    except Exception:  # an odd texture (remote, animated, missing sheet): can't tell, so don't refuse
        return None
    return None


def unchanged(s):
    """True when a new look for an item is its current sprite, pixel for pixel (solid or empty as drawn)."""
    if s["kind"] != "item":
        return False
    now = current_pixels(s["item"], s["size"])
    if now is None:
        return False
    for i in range(s["size"] * s["size"]):
        px = s["pixels"][i * 8:i * 8 + 8]
        r, g, b, a = (int(px[k:k + 2], 16) for k in (0, 2, 4, 6))
        nr, ng, nb, na = now.getpixel((i % s["size"], i // s["size"]))
        if (a >= 0x80) != (na >= 0x80) or (a >= 0x80 and (r, g, b) != (nr, ng, nb)):
            return False
    return True


def looks(cat):
    """Each item's look from the catalog: the last approved one for it wins."""
    out = {}
    for s in cat["sprites"]:
        if s.get("kind") == "item" and s.get("item"):
            out[s["item"]] = (s["sheet"], s["index"], clean(s.get("author", ""), 32))
    return out


SPRITE_BY = re.compile(r"[ \t]*<SpriteBy>[^<]*</SpriteBy>\n?")


def retexture(block, sheet, index, author=""):
    """The block's <Texture> pointed at the cell, and <SpriteBy> (the tooltip's credit) after it.
    An item drawn from an <AnimatedTexture> (the Elder Shatters Key) gets a plain <Texture> in its
    place: a player's look is one still frame."""
    if not TEXTURE.search(block):
        a = ANIMATED.search(block)
        if a:
            # indented as the element it replaces
            line_start = block.rfind("\n", 0, a.start()) + 1
            indent = re.match(r"[ \t]*", block[line_start:]).group(0)
            inner = indent + ("  " if indent.startswith(" ") else "\t")
            texture = (f"<Texture>\n{inner}<File>{sheet}</File>\n{inner}<Index>0x{index:x}</Index>\n"
                       f"{indent}</Texture>")
            block = block[:a.start()] + texture + block[a.end():]
    m = TEXTURE.search(block)
    if not m or "<RandomTexture" in block[:m.start()]:
        return block
    inner = m.group(1)
    new = re.sub(r"<File>[^<]*</File>", f"<File>{sheet}</File>", inner, count=1)
    new = re.sub(r"<Index>[^<]*</Index>", f"<Index>0x{index:x}</Index>", new, count=1)
    block = block[:m.start(1)] + new + block[m.end(1):]
    block = SPRITE_BY.sub("", block)
    if author:
        end = block.index("</Texture>", m.start()) + len("</Texture>")
        line_start = block.rfind("\n", 0, m.start()) + 1
        indent = re.match(r"[ \t]*", block[line_start:]).group(0)
        block = block[:end] + f"\n{indent}<SpriteBy>{html.escape(author, quote=False)}</SpriteBy>" + block[end:]
    return block


def rework_file(path, cat=None, write=True):
    """Points every item the catalog has a look for at its cell, in one XML file. Returns how many changed
    (with write=False, how many would change; the file is left alone)."""
    want = looks(cat or load_catalog())
    if not want:
        return 0
    with open(path, "rb") as f:
        raw = f.read()
    # most files name none of the items: leave them alone
    if not any(html.escape(i, quote=True).encode("utf-8") in raw or i.encode("utf-8") in raw for i in want):
        return 0
    # a few older files aren't UTF-8: Latin-1 reads and writes any byte back as it was
    try:
        text, encoding = raw.decode("utf-8-sig"), "utf-8"
    except UnicodeDecodeError:
        text, encoding = raw.decode("latin-1"), "latin-1"
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    count = 0

    def one(m):
        nonlocal count
        look = want.get(html.unescape(m.group(1)))
        if not look:
            return m.group(0)
        new = retexture(m.group(0), *look)
        count += new != m.group(0)
        return new

    out = OBJECT.sub(one, text)
    if crlf:
        out = out.replace("\n", "\r\n")
    bom = b"\xef\xbb\xbf" if encoding == "utf-8" and raw.startswith(b"\xef\xbb\xbf") else b""
    data = bom + out.encode(encoding)
    if data != raw and write:
        with open(path, "wb") as f:
            f.write(data)
    return count


def rework_all(cat=None):
    cat = cat or load_catalog()
    changed = []
    for path in xml_files():
        if rework_file(path, cat):
            changed.append(path)
    return changed


# ---- the changelog and PR-BODY.md ---------------------------------------------------------------

def read_text(path):
    with open(path, "rb") as f:
        raw = f.read().decode("utf-8")
    return raw.replace("\r\n", "\n"), "\r\n" in raw


def write_text(path, text, crlf):
    with open(path, "w", encoding="utf-8", newline="\r\n" if crlf else "\n") as f:
        f.write(text)


def add_to_line(lines, prefix, names):
    """Adds names to the line starting with prefix (it ends with a full stop). True if found."""
    for i, line in enumerate(lines):
        if line.startswith(prefix):
            lines[i] = line.rstrip().rstrip(".") + ", " + ", ".join(names) + "."
            return True
    return False


def update_changelog(names):
    text, crlf = read_text(CHANGELOG)
    lines = text.split("\n")
    new_line = CHANGELOG_PREFIX + ", ".join(names) + "."
    try:
        start = lines.index("## Unreleased")
    except ValueError:
        first = next(i for i, l in enumerate(lines) if l.startswith("## "))
        lines[first:first] = ["## Unreleased", "", "### Items", new_line, ""]
        write_text(CHANGELOG, "\n".join(lines), crlf)
        return
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    # only Unreleased's own line: a released section's (the last version's sprites) stays as it was
    unreleased = lines[start:end]
    if add_to_line(unreleased, CHANGELOG_PREFIX, names):
        lines[start:end] = unreleased
        write_text(CHANGELOG, "\n".join(lines), crlf)
        return
    items = next((i for i in range(start + 1, end) if lines[i].strip() == "### Items"), None)
    if items is None:
        lines[start + 1:start + 1] = ["", "### Items", new_line]
    else:
        at = items + 1
        while at < end and lines[at].startswith(("- ", "  ")):
            at += 1
        lines.insert(at, new_line)
    write_text(CHANGELOG, "\n".join(lines), crlf)


def section(lines, heading):
    """(start, end) of a `## ` section's lines, or None."""
    if heading not in lines:
        return None
    start = lines.index(heading)
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    return start, end


def update_pr_body(players, details, check):
    if not os.path.exists(PR_BODY):
        print("  no PR-BODY.md: add the release's part by hand")
        return
    text, crlf = read_text(PR_BODY)
    lines = text.split("\n")
    if players and not add_to_line(lines, PR_PLAYERS_PREFIX, players):
        s = section(lines, "## For players")
        if s:
            at = s[1]
            while at > s[0] + 1 and not lines[at - 1].strip():
                at -= 1
            new = [PR_PLAYERS_PREFIX + ", ".join(players) + "."]
            if at == s[0] + 1:  # an empty section: keep a blank line under its heading
                new.insert(0, "")
            lines[at:at] = new
    if PR_DETAILS_HEAD in lines:
        at = lines.index(PR_DETAILS_HEAD) + 1
        while at < len(lines) and not lines[at].startswith("#"):
            at += 1
        while not lines[at - 1].strip():
            at -= 1
        lines[at:at] = details
    else:
        s = section(lines, "## Details")
        part = ["", PR_DETAILS_HEAD, "",
                "Sprites players drew in the Sprite Editor and admins approved, taken in by "
                "`tools/community_sprites.py apply`: drawn into the community sheets "
                "(`CommunitySprites8x8.png`, `CommunitySprites16x16.png`), listed in "
                "`source/Shared/resources/data/community_sprites.json`, and each new look's item "
                "pointed at its cell in both XML copies. Redeploy the client (sheets, XML) and the "
                "servers (XML, the catalog, which marks them as in the game); nobody is "
                "disconnected, no Redis change.", ""] + details
        if s:
            at = s[1]
            while not lines[at - 1].strip():
                at -= 1
            lines[at:at] = part
        else:
            lines += ["", "## Details"] + part
    s = section(lines, "## Checks")
    if s and not any(check.split(":")[0] in l for l in lines[s[0]:s[1]]):
        at = s[1]
        while not lines[at - 1].strip():
            at -= 1
        lines.insert(at, check)
    write_text(PR_BODY, "\n".join(lines), crlf)


# ---- commands -----------------------------------------------------------------------------------

def describe(s):
    if s["kind"] == "item":
        return f"#{s['id']} new look for {s['item']} by {s['author']}"
    return f"#{s['id']} new item {s['name']} ({s['slot']}) by {s['author']}"


def cmd_list(args):
    approved = fetch(args.server)
    done = {s["id"] for s in load_catalog()["sprites"]}
    waiting = [s for s in approved if s["id"] not in done]
    if not waiting:
        print("Nothing approved is waiting to be taken in.")
    for s in waiting:
        print("  " + describe(s))
    for s in approved:
        if s["id"] in done:
            print(f"  (#{s['id']} is in the catalog already: it reaches players with the next release)")


def cmd_apply(args):
    approved = parse(open(args.from_file, encoding="utf-8").read()) if args.from_file else fetch(args.server)
    cat = load_catalog()
    done = {s["id"] for s in cat["sprites"]}
    only = {int(x) for x in args.only.split(",")} if args.only else None
    todo = [s for s in approved if s["id"] not in done and (only is None or s["id"] in only)]
    if not todo:
        print("Nothing approved is waiting to be taken in.")
        return
    known = item_ids()
    today = datetime.date.today().isoformat()
    players, details, touched = [], [], {CATALOG}
    for s in todo:
        if s["kind"] == "item" and s["item"] not in known:
            print(f"  skipped {describe(s)}: no item with that id in the XML")
            continue
        if unchanged(s):
            print(f"  skipped {describe(s)}: it's the item's sprite as it is now; turn it down in the Review tab")
            continue
        sheet, file = SHEETS[s["size"]]
        index = next_index(cat, sheet)
        print(f"  {describe(s)} -> {sheet} 0x{index:x}")
        if args.dry_run:
            continue
        draw(file, index, s["size"], s["pixels"])
        touched.add(file)
        entry = {k: s[k] for k in ("id", "kind", "item", "name", "slot", "note", "author", "approvedBy", "size")}
        entry.update(sheet=sheet, index=index, added=today)
        cat["sprites"].append(entry)
        if s["kind"] == "item":
            players.append(f"{s['item']} (by {s['author']})")
            details.append(f"- #{s['id']}: `{s['item']}`, drawn by `{s['author']}`, approved by "
                           f"`{s['approvedBy'] or '?'}`: `{sheet}` `0x{index:x}`.")
        else:
            details.append(f"- #{s['id']}: art for a new item, `{s['name']}` ({s['slot']}), drawn by "
                           f"`{s['author']}`: `{sheet}` `0x{index:x}`, not used by any item yet."
                           + (f" Their note: {s['note'].rstrip('.')}." if s["note"] else ""))
    if args.dry_run or not details:
        return
    save_catalog(cat)
    touched.update(rework_all(cat))
    if players:
        update_changelog(players)
        touched.add(CHANGELOG)
    update_pr_body(players, details, "- Player sprites: `python tools/community_sprites.py apply` drew them into the "
                                     "sheets and pointed the items at them; check them in a `--shot` of the client.")
    touched.add(PR_BODY)
    rel = sorted(os.path.relpath(p, ROOT) for p in touched if os.path.exists(p))
    print("Changed:\n  " + "\n  ".join(rel))
    if args.commit:
        subprocess.check_call(["git", "add", "--"] + rel, cwd=ROOT)
        msg = "Player sprites: " + "; ".join(describe(s) for s in todo if s["id"] in {e["id"] for e in cat["sprites"]})
        subprocess.check_call(["git", "commit", "-m", msg[:300], "--"] + rel, cwd=ROOT)
    else:
        print("Look them over (a --shot of the client), then commit those files onto the release branch.")


def cmd_rework(args):
    changed = rework_all()
    for p in changed:
        print("  " + os.path.relpath(p, ROOT))
    print(f"{len(changed)} files changed")


def problems_in_repo(cat):
    """What's out of step between the catalog, the sheets and the XML (empty when all is well)."""
    from PIL import Image
    out = []
    known = item_ids()
    sheets = {}
    for s in cat["sprites"]:
        size = next((k for k, (name, _) in SHEETS.items() if name == s.get("sheet")), None)
        if size is None:
            out.append(f"#{s.get('id')}: unknown sheet {s.get('sheet')!r}")
            continue
        if size not in sheets:
            sheets[size] = Image.open(SHEETS[size][1]).convert("RGBA")
        img, i = sheets[size], s.get("index", -1)
        x0, y0 = (i % COLUMNS) * size, (i // COLUMNS) * size
        if i < 0 or x0 + size > img.width or y0 + size > img.height \
                or img.crop((x0, y0, x0 + size, y0 + size)).getbbox() is None:
            out.append(f"#{s.get('id')}: its cell {s['sheet']} 0x{i:x} is empty or missing from the sheet")
        if s.get("kind") == "item" and s.get("item") not in known:
            out.append(f"#{s.get('id')}: no item {s.get('item')!r} in the XML any more")
    for path in xml_files():
        if rework_file(path, cat, write=False):
            out.append(f"{os.path.relpath(path, ROOT)}: an item isn't pointed at its player sprite "
                       "(or lost its <SpriteBy>); run `rework`")
    # an item `rework` can't point (no <Texture> it understands) would pass the check above unseen
    for item, (sheet, index, _) in looks(cat).items():
        for path in xml_files():
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                text = f.read()
            for m in OBJECT.finditer(text):
                if html.unescape(m.group(1)) != item:
                    continue
                t = TEXTURE.search(m.group(0))
                f_, i_ = (re.search(r"<File>([^<]*)</File>", t.group(1)), re.search(r"<Index>([^<]*)</Index>", t.group(1))) if t else (None, None)
                if not (f_ and i_ and f_.group(1).strip() == sheet and int(i_.group(1).strip(), 0) == index):
                    out.append(f"{os.path.relpath(path, ROOT)}: {item!r} doesn't show its player sprite "
                               f"({sheet} 0x{index:x}); `rework` can't point its texture")
    return out


def cmd_check(args):
    """For the release checks: fails when the repository is out of step with the catalog, or when
    sprites approved on the server haven't been taken in yet."""
    cat = load_catalog()
    bad = problems_in_repo(cat)
    for p in bad:
        print("  " + p)
    waiting, same = [], []
    if not args.offline:
        try:
            approved = fetch(args.server)
        except Exception as e:  # say why and fail: a release shouldn't skip this silently
            print(f"Couldn't ask {args.server} what's approved ({e}); "
                  "rerun with --offline to check only the repository.")
            sys.exit(1)
        done = {s["id"] for s in cat["sprites"]}
        waiting = [s for s in approved if s["id"] not in done]
        same = [s for s in waiting if unchanged(s)]
        for s in waiting:
            if s in same:
                print("  approved, but the item's sprite as it is now (turn it down in the Review tab): " + describe(s))
            else:
                print("  approved, not taken in: " + describe(s))
    if bad or waiting:
        if len(waiting) > len(same):
            print("Take them in with `python tools/community_sprites.py apply --commit` on the release branch.")
        sys.exit(1)
    print(f"Player sprites in order: {len(cat['sprites'])} in the catalog"
          + ("" if args.offline else ", nothing approved waiting") + ".")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("command", choices=["list", "apply", "rework", "check"])
    ap.add_argument("--offline", action="store_true", help="check: only the repository, don't ask the server")
    ap.add_argument("--server", default=DEFAULT_SERVER, help="the account server (its /api address on a site)")
    ap.add_argument("--from-file", help="apply: read the approved list from this file instead of the server")
    ap.add_argument("--only", help="apply: only these submission ids, comma-separated")
    ap.add_argument("--dry-run", action="store_true", help="apply: say what would change, change nothing")
    ap.add_argument("--commit", action="store_true", help="apply: commit the changed files")
    args = ap.parse_args()
    {"list": cmd_list, "apply": cmd_apply, "rework": cmd_rework, "check": cmd_check}[args.command](args)


if __name__ == "__main__":
    main()
