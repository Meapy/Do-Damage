"""What the Discord bot and the server setup share, with nothing but the standard library: the
settings file, the releases and their changelog, the game's status and the wiki search, and the
messages made from them (as Discord's JSON, so a dry run can print exactly what would be sent).
"""
import json
import os
import re
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SITE = "https://dodmg.com"
NAME = "Do Damage"
INVITE = "https://discord.gg/FVhqJe8CG"
COLOUR = 0xE0533D  # the site's ember red
USER_AGENT = "DoDamageBot (https://dodmg.com, 1)"
API = "https://discord.com/api/v10"


def load_env(path):
    """KEY=value lines (# comments, optional quotes) into a dict. Never printed anywhere."""
    out = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, _, value = line.partition("=")
            out[key.strip()] = value.strip().strip('"').strip("'")
    return out


def version_key(v):
    return tuple(int(p) for p in re.findall(r"\d+", v))


# --- releases -----------------------------------------------------------------------------
# A release is (version, date, [(group, [item, ...]), ...]): the same shape tools/build_site.py
# reads from the changelog.

def release_from_latest(path):
    """The newest release from a published latest.json (dist/client/<os>/latest.json): its
    changelog is the one the release was published with. None when there's no such file."""
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return None
    entry = next((e for e in data.get("changelog", []) if e.get("version") == data.get("version")), None)
    if entry is None:
        return data["version"], data.get("date", ""), []
    changes = entry.get("changes", [])
    starts = entry.get("groups") or [["", 0]]
    if starts[0][1] != 0:
        starts = [["", 0]] + starts
    groups = []
    for i, (title, start) in enumerate(starts):
        end = starts[i + 1][1] if i + 1 < len(starts) else len(changes)
        if changes[start:end]:
            groups.append((title, changes[start:end]))
    return data["version"], entry.get("date") or data.get("date", ""), groups


def releases_from_changelog(path=os.path.join(ROOT, "client-rs", "CHANGELOG.md")):
    """Every named release in client-rs/CHANGELOG.md, newest first (Unreleased left out)."""
    with open(path, encoding="utf-8") as f:
        text = f.read()
    out = []
    for block in re.split(r"^## ", text, flags=re.M)[1:]:
        head, _, body = block.partition("\n")
        m = re.match(r"([\d.]+)\s*(?:\((.*?)\))?", head.strip())
        if not m:
            continue
        groups = [("", [])]
        for line in body.splitlines():
            items = groups[-1][1]
            if line.startswith("### "):
                groups.append((line[4:].strip(), []))
            elif line.startswith("- "):
                items.append(line[2:].strip())
            elif line.startswith("  ") and items:
                items[-1] += " " + line.strip()
        out.append((m.group(1), m.group(2) or "", [g for g in groups if g[1]]))
    return out


def release_messages(release, site=SITE, role_id=None):
    """The messages that announce a release in #updates: one embed each, as many as the changelog
    needs (an embed holds 4096 characters, a message 6000). Only the first pings the role."""
    version, date, groups = release
    link = "%s/changelog.html#v%s" % (site, version)
    blocks = []
    for title, items in groups:
        lines = (["**%s**" % title] if title else []) + ["• " + i for i in items]
        blocks.append("\n".join(lines))
    pages, page = [], ""
    for block in blocks:
        while len(block) > 3900:  # one huge group: split it at a line
            cut = block.rfind("\n", 0, 3900)
            cut = cut if cut > 0 else 3900
            blocks_head, block = block[:cut], block[cut:].lstrip("\n")
            pages, page = _add(pages, page, blocks_head)
        pages, page = _add(pages, page, block)
    if page:
        pages.append(page)
    if not pages:
        pages = ["The changes are on the changelog."]
    messages = []
    for i, text in enumerate(pages):
        embed = {"description": text, "color": COLOUR}
        if i == 0:
            embed["title"] = "%s %s is out" % (NAME, version)
            embed["url"] = link
        if i == len(pages) - 1:
            embed["description"] += ("\n\n[Full changelog](%s) · [Play in your browser](%s/play/) · "
                                     "the desktop client updates itself" % (link, site))
            embed["footer"] = {"text": "Released %s" % date if date else NAME}
        msg = {"embeds": [embed], "allowed_mentions": {"parse": [], "roles": []}}
        if i == 0 and role_id:
            msg["content"] = "<@&%s>" % role_id
            msg["allowed_mentions"]["roles"] = [str(role_id)]
        messages.append(msg)
    return messages


def _add(pages, page, block):
    if page and len(page) + 2 + len(block) > 3900:
        return pages + [page], block
    return pages, (page + "\n\n" + block if page else block)


def release_title(version):
    """The first embed's title, which the bot looks for in #updates before posting again."""
    return "%s %s is out" % (NAME, version)


# --- the game's status ----------------------------------------------------------------------

def _get(url, data=None, timeout=10):
    req = urllib.request.Request(url, data=data, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as f:
        return f.read().decode("utf-8")


def server_list(site=SITE):
    """[(name, players, room)] from the account server's app/serverList (admin-only ones left
    out); [] when no world server is up."""
    xml = _get(site + "/api/app/serverList", data=b"")
    out = []
    for s in ET.fromstring(xml).iter("Server"):
        if (s.findtext("AdminOnly") or "").lower() == "true":
            continue
        m = re.match(r"(\d+)\s*/\s*(\d+)", s.findtext("UsageText") or "")
        out.append((s.findtext("Name") or "?", int(m.group(1)) if m else 0, int(m.group(2)) if m else 0))
    return out


def fetch_status(site=SITE):
    """{online, players, peak24h (None if unknown), uptime (7 days' average, None if unknown),
    servers [(name, players, room)] or None}. Raises when the site can't be reached at all."""
    status = None
    try:
        status = json.loads(_get(site + "/api/app/status"))
    except Exception:
        pass
    try:
        servers = server_list(site)
    except Exception:
        if status is None:
            raise
        servers = None
    if status is None:
        return {"online": bool(servers), "players": sum(s[1] for s in servers),
                "peak24h": None, "uptime": None, "servers": servers}
    days = [d["up"] for d in status.get("days", []) if d.get("up") is not None]
    return {"online": status.get("online", False), "players": status.get("players", 0),
            "peak24h": status.get("peak24h"), "uptime": sum(days) / len(days) if days else None,
            "servers": servers}


def status_embed(status, site=SITE, live=False):
    """The /status answer, and the message the bot keeps up to date in #server-status (live)."""
    if status is None:
        return {"title": "%s: status unavailable" % NAME, "color": 0x888888,
                "description": "The website didn't answer. Try again in a minute."}
    if not status["online"]:
        return {"title": "%s is down for a moment" % NAME, "color": 0xC0392B, "url": site,
                "description": "The world server is restarting or updating. It's usually back within a minute or two."}
    fields = [{"name": "Players online", "value": str(status["players"]), "inline": True}]
    if status["peak24h"] is not None:
        fields.append({"name": "24-hour peak", "value": str(status["peak24h"]), "inline": True})
    if status["uptime"] is not None:
        fields.append({"name": "Uptime (7 days)", "value": "%.1f%%" % (status["uptime"] * 100), "inline": True})
    embed = {"title": "%s is online" % NAME, "color": 0x2ECC71, "url": site, "fields": fields,
             "description": "[Play in your browser](%s/play/) · [Download](%s/#get)" % (site, site)}
    if live:
        embed["footer"] = {"text": "Updated every couple of minutes"}
    return embed


def online_embed(status, site=SITE):
    if status is None or not status["online"]:
        return status_embed(status, site)
    servers = status["servers"] or []
    lines = ["**%s**: %d player%s" % (n, p, "" if p == 1 else "s") for n, p, _ in servers]
    return {"title": "%d player%s online" % (status["players"], "" if status["players"] == 1 else "s"),
            "color": 0x2ECC71, "url": site, "description": "\n".join(lines) or "Nobody's in yet."}


# --- the wiki -------------------------------------------------------------------------------

def wiki_index(site=SITE):
    """The wiki's search index: [{p: page, h: heading, u: url under /wiki/, t: text}]."""
    js = _get(site + "/wiki/search-index.js", timeout=15)
    return json.loads(js[js.index("["):js.rindex("]") + 1])


def wiki_search(index, term, limit=5):
    """The best matches for a term, like the wiki's own search: every word must appear; a match
    in a heading or page title counts most."""
    words = [w for w in re.findall(r"\w+", term.lower()) if w]
    if not words:
        return []
    scored = []
    for e in index:
        head = (e.get("p", "") + " " + e.get("h", "")).lower()
        text = e.get("t", "").lower()
        if not all(w in head or w in text for w in words):
            continue
        score = sum(10 if w in e.get("h", "").lower() else 4 if w in head else 1 for w in words)
        if e.get("h", "").lower() == term.lower().strip():
            score += 20
        if "#i-" in e.get("u", ""):
            score -= 5  # an item: guides and dungeons first
        scored.append((-score, len(text), e))
    scored.sort(key=lambda s: (s[0], s[1]))
    return [s[2] for s in scored[:limit]]


def wiki_embed(term, results, site=SITE):
    search = "%s/wiki/?q=%s" % (site, urllib.parse.quote(term))
    if not results:
        return {"title": "Nothing in the wiki for \"%s\"" % term[:100], "url": search, "color": COLOUR,
                "description": "Try another word, or [search the wiki](%s)." % search}
    lines = []
    for e in results:
        title = e["p"] if e["h"] == e["p"] else "%s: %s" % (e["p"], e["h"])
        snippet = e.get("t", "")
        snippet = snippet[:140].rsplit(" ", 1)[0] + "…" if len(snippet) > 140 else snippet
        lines.append("**[%s](%s/wiki/%s)**\n%s" % (title, site, e["u"], snippet))
    lines.append("[More results](%s)" % search)
    return {"title": "Wiki: %s" % term[:200], "url": search, "color": COLOUR, "description": "\n\n".join(lines)}


def play_embed(site=SITE):
    return {"title": "Play %s" % NAME, "url": site, "color": COLOUR, "description": "\n".join([
        "**In your browser:** %s/play/ (nothing to install)" % site,
        "**Desktop** (Windows, Linux, macOS): %s/#get (it updates itself)" % site,
        "**New here?** %s/wiki/getting-started.html" % site,
        "**The wiki:** %s/wiki/" % site,
        "**Changelog:** %s/changelog.html" % site,
        "",
        "It's free: one account works in both, and every shop takes fame you earn by playing.",
    ])}


# --- Discord's REST API (for the setup script and one-off posts) ------------------------------

class Rest:
    def __init__(self, token):
        self._auth = "Bot " + token

    def __call__(self, method, path, body=None, reason=None):
        import time
        import urllib.error
        data = None if body is None else json.dumps(body).encode()
        headers = {"Authorization": self._auth, "User-Agent": USER_AGENT}
        if data is not None:
            headers["Content-Type"] = "application/json"
        if reason:
            headers["X-Audit-Log-Reason"] = urllib.parse.quote(reason)
        for _ in range(5):
            req = urllib.request.Request(API + path, data=data, method=method, headers=headers)
            try:
                with urllib.request.urlopen(req, timeout=20) as f:
                    raw = f.read()
                    return json.loads(raw) if raw else None
            except urllib.error.HTTPError as e:
                if e.code == 429:
                    time.sleep(float(json.loads(e.read() or b"{}").get("retry_after", 1)) + 0.2)
                    continue
                raise RuntimeError("%s %s: %d %s" % (method, path, e.code, e.read().decode()[:300])) from None
        raise RuntimeError("%s %s: rate limited" % (method, path))
