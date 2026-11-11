"""What the Discord bot and the server setup share, with nothing but the standard library: the
settings file, the releases and their changelog, the game's status and the wiki search, and the
messages made from them (as Discord's JSON, so a dry run can print exactly what would be sent).
"""
import hashlib
import hmac
import json
import os
import re
import urllib.error
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
    changelog is the one the release was published with. None when there's no such file. path may
    be an https address, where the bot runs on another machine than the game
    (https://play.dodmg.com/api/client/windows/latest.json)."""
    try:
        if path.startswith(("http://", "https://")):
            req = urllib.request.Request(path, headers={"User-Agent": USER_AGENT, "Cache-Control": "no-cache"})
            with urllib.request.urlopen(req, timeout=20) as f:
                data = json.loads(f.read().decode("utf-8"))
        else:
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


# --- linked game accounts --------------------------------------------------------------------
# A member links their Discord account to their game account with a code from the game's menu
# (/link), and can then see the account, reset its password and unlink it. The account server does
# the work (source/App/Controllers/DiscordController.cs); the bot proves itself with a secret.

def link_secret(env):
    """What the bot and the account server share: BSK_LINK_SECRET when set, else made from the
    bot's token, which deploy/deploy.py also has when it writes the account server's settings (so
    there's nothing more to set up, and a new token changes both at the next deploy). '' without
    either: no linking."""
    secret = env.get("BSK_LINK_SECRET", "").strip()
    token = env.get("DISCORD_TOKEN", "").strip()
    if secret or not token:
        return secret
    return hmac.new(token.encode(), b"dodmg account linking", hashlib.sha256).hexdigest()


class Accounts:
    """The account server's Discord requests. Each returns (answer, error): the answer's fields as
    a dict, or the server's error text ("Not linked", "Bad code"...; "unreachable" when it didn't
    answer)."""

    def __init__(self, base, secret):
        self.base, self.secret = base.rstrip("/"), secret

    def __call__(self, what, member_id, **fields):
        if not self.secret:
            return None, "not set up"
        data = urllib.parse.urlencode(dict(fields, secret=self.secret, discordId=str(member_id))).encode()
        req = urllib.request.Request("%s/discord/%s" % (self.base, what), data=data, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(req, timeout=15) as f:
                body = f.read()
        except urllib.error.HTTPError as e:
            body = e.read()  # the account server's errors come with a status and an <Error> body
        except OSError:
            return None, "unreachable"
        try:
            root = ET.fromstring(body.decode("utf-8"))
        except (ET.ParseError, UnicodeDecodeError):
            return None, "unreachable"
        if root.tag == "Error":
            return None, (root.text or "").strip() or "unreachable"
        return {c.tag: (c.text or "") for c in root}, None


LINK_HOW = ("**To link your game account:** open %s, sign in, and on the main menu choose "
            "**Link Discord**. It shows a 6-character code: give it to me with `/link code:<the code>` "
            "within 10 minutes. You become **Verified** here under your game name, and the first time "
            "you get a **Potion of Maxy**." % NAME)


def link_error(error):
    """What a member is told when a linked-account command fails, in plain words."""
    return {
        "not set up": "Account linking isn't set up here yet.",
        "Discord linking isn't set up on this server.": "Account linking isn't set up here yet.",
        "unreachable": "I couldn't reach the game just now. Try again in a minute.",
        "Not linked": "Your Discord isn't linked to a game account yet.\n" + LINK_HOW,
        "Bad code": "That code didn't work. A code lasts 10 minutes and works once: get a new one "
                    "in the game (main menu, **Link Discord**) and try again.",
        "Too many tries": "Too many wrong codes. Wait 10 minutes, then try again with a new code from the game.",
        "Account linked": "That game account is linked to a Discord account already. Unlink it "
                          "first: in the game, main menu, **Discord**, **Unlink**.",
        "Discord linked": "Your Discord is linked to a game account already (`/account` shows it). "
                          "`/unlink` first if you want to link another.",
        "Too soon": "That account's password was reset in the last 10 minutes. Use the one you were given, "
                    "or wait a little and ask again.",
    }.get(error, "The game said no: %s" % error)


VERIFIED = {
    # what happened to the member's Verified role and nickname, said after the command's answer
    "marked": "You're **Verified** here now, and your nickname is your game name.",
    "unmarked": "Your Verified role and nickname are gone with it.",
    "refused": "I couldn't change your role or nickname: Discord doesn't let me change the server's owner or "
               "anyone ranked above me.",
}


def link_reply(what, answer, error, verified=None):
    """The (private) answer to /link, /unlink, /account and /resetpassword, as text. verified:
    what the bot did with the member's Verified role and nickname (a key of VERIFIED), if anything."""
    text = _link_reply(what, answer, error)
    return text + "\n" + VERIFIED[verified] if verified in VERIFIED else text


def _link_reply(what, answer, error):
    if error:
        return link_error(error)
    name = answer.get("Name", "?")
    if what == "link":
        reward = ("\nA **%s** is waiting in your vault's gift chest. Thanks for linking!" % answer["Reward"]
                  if answer.get("Reward") else "")
        return ("Linked to **%s**.%s\nIf you ever lose your password, `/resetpassword` here gets you back in. "
                "`/account` shows the account, `/unlink` undoes this." % (name, reward))
    if what == "unlink":
        return "Unlinked from **%s**. You can link again any time with a new code from the game." % name
    if what == "account":
        lines = ["Your Discord is linked to **%s**." % name,
                 "Fame: %s (%s earned in all)" % (answer.get("Fame", "0"), answer.get("TotalFame", "0")),
                 "Characters alive: %s" % answer.get("Characters", "0")]
        if answer.get("Guild"):
            lines.append("Guild: %s" % answer["Guild"])
        return "\n".join(lines)
    if what == "resetPassword":
        return ("**%s** has a new password. Sign in with:\n"
                "Email: `%s`\nPassword: `%s`\n"
                "Then choose your own: on the character list, **Account**, change the password. "
                "Only you can see this message." % (name, answer.get("Email", "?"), answer.get("Password", "?")))
    return "Done."


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
