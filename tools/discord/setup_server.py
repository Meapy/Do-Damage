"""Lays out the Do Damage Discord server: categories, channels, the Updates role, who may write where, and
the bot's name and picture in the server, and its welcome, getting-started and FAQ messages (from tools/discord/messages/). It only adds and
arranges: it never deletes a channel, role or message. Run it again after changing the layout or a
message; what's already right is left alone.

    python tools/discord/setup_server.py --env deploy/discord.local.env            # show the plan
    python tools/discord/setup_server.py --env deploy/discord.local.env --apply    # do it
    python tools/discord/setup_server.py --env ... --apply --refresh-messages      # also edit the
                                                                                    # bot's posted messages

An existing channel with the same name (emoji and separators aside, so "📢┃updates" is updates) is
moved into its category and given the layout's permissions, keeping its messages and any other
permissions it has. --apply writes the ids the bot needs (DISCORD_GUILD_ID, DISCORD_UPDATES_CHANNEL,
DISCORD_STATUS_CHANNEL, DISCORD_UPDATES_ROLE, and DISCORD_POSTED_UP_TO: the newest release in the
changelog, already announced) into the --env file, beside the token.
"""
import argparse
import base64
import os
import re
import sys

import dodmg

GUILD_ID = 1561742431999893594  # Do Damage (from the invite)
MESSAGES = os.path.join(dodmg.HERE, "messages")
ICON = os.path.join(dodmg.ROOT, "client-rs", "app", "icons", "icon-256.png")  # the game's crown

# permission bits (discord.com/developers/docs/topics/permissions)
ADD_REACTIONS = 1 << 6
VIEW = 1 << 10
SEND = 1 << 11
MANAGE_MESSAGES = 1 << 13
EMBED_LINKS = 1 << 14
ATTACH_FILES = 1 << 15
READ_HISTORY = 1 << 16
CONNECT = 1 << 20
SPEAK = 1 << 21
MANAGE_THREADS = 1 << 34
CREATE_PUBLIC_THREADS = 1 << 35
CREATE_PRIVATE_THREADS = 1 << 36
SEND_IN_THREADS = 1 << 38
WRITE = SEND | SEND_IN_THREADS | CREATE_PUBLIC_THREADS | CREATE_PRIVATE_THREADS

TEXT, VOICE, CATEGORY = 0, 2, 4

ROLES = [
    # name, colour, shown apart in the member list, mentionable by anyone, permissions
    ("Updates", 0xF1C40F, False, False, 0),
]
# the server's own roles that may write in the read-only channels
STAFF = ["Owner", "Developer"]

# (category, [(channel, type, topic, read_only, message file)])
LAYOUT = [
    ("INFO", [
        ("welcome", TEXT, "Welcome to Do Damage: what it is, where to play, and the rules.", True, "welcome.md"),
        ("announcements", TEXT, "News from the staff.", True, None),
        ("updates", TEXT, "Every game update, posted by the bot when it's out. /updates to get pinged.", True, None),
        ("server-status", TEXT, "Is the game up? Players online, kept up to date by the bot.", True, None),
    ]),
    ("GETTING STARTED", [
        ("getting-started", TEXT, "How to start playing: browser or download, your account, /gear, the wiki.", True, "getting-started.md"),
        ("faq", TEXT, "Answers to the questions everyone asks. Ask in #help if yours isn't here.", True, "faq.md"),
    ]),
    ("COMMUNITY", [
        ("general", TEXT, "Talk about anything Do Damage.", False, None),
        ("trading", TEXT, "Buy, sell and swap items. Trade in game with /trade <name>.", False, None),
        ("looking-for-group", TEXT, "Find people for dungeons, Oryx and the Arena.", False, None),
        ("screenshots", TEXT, "Show off your loot, your deaths and your best runs.", False, None),
        ("suggestions", TEXT, "Ideas for the game. One idea per message, so people can react to it.", False, None),
        ("bot-commands", TEXT, "/status, /online, /wiki, /play and /updates.", False, None),
    ]),
    ("SUPPORT", [
        ("help", TEXT, "Stuck, can't sign in, something not working? Ask here.", False, None),
        ("bug-reports", TEXT, "Found a bug? Say what happened, where, your character and browser or desktop, with a screenshot.", False, None),
    ]),
    ("VOICE", [
        ("Lobby", VOICE, None, False, None),
        ("Gaming", VOICE, None, False, None),
        ("Dungeon run", VOICE, None, False, None),
    ]),
]

# channels the server had before, used in place of the layout's (the messages' {#name} still use
# the layout's names)
ALIASES = {"screenshots": ["loot-and-screenshots"], "bug-reports": ["bug-report"]}


def key(name):
    """A channel's name without emoji, separators or case: "📢┃Updates" and "updates" match."""
    return re.sub(r"[^a-z0-9]+", "", name.lower())


def message_embed(name, ids):
    """A message file: its first line (# Title) is the embed's title, the rest its text, with
    {#channel} made a link to that channel."""
    with open(os.path.join(MESSAGES, name), encoding="utf-8") as f:
        text = f.read().strip()
    title, _, body = text.partition("\n")
    link = lambda m: "<#%s>" % ids[m.group(1)] if not str(ids.get(m.group(1), "<")).startswith("<") else "#" + m.group(1)
    body = re.sub(r"\{#([\w-]+)\}", link, body.strip())
    return {"title": title.lstrip("# ").strip(), "description": body, "color": dodmg.COLOUR}


def write_env(path, values):
    """Sets KEY=value lines in the settings file, keeping the rest (the token) as it is."""
    with open(path, encoding="utf-8-sig") as f:
        lines = f.read().splitlines()
    for k, v in values.items():
        line = "%s=%s" % (k, v)
        for i, old in enumerate(lines):
            if old.split("=", 1)[0].strip() == k:
                lines[i] = line
                break
        else:
            lines.append(line)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--env", required=True, help="the bot's settings file, with DISCORD_TOKEN")
    ap.add_argument("--guild", type=int, default=GUILD_ID)
    ap.add_argument("--apply", action="store_true", help="make the changes (without it: only show them)")
    ap.add_argument("--refresh-messages", action="store_true", help="edit the bot's posted messages to the files' text")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    env = dodmg.load_env(args.env)
    if not env.get("DISCORD_TOKEN"):
        sys.exit("no DISCORD_TOKEN in %s" % args.env)
    rest = dodmg.Rest(env["DISCORD_TOKEN"])
    g = args.guild
    apply = args.apply
    me = rest("GET", "/users/@me")
    try:
        guild = rest("GET", "/guilds/%d" % g)
    except RuntimeError as e:
        sys.exit("the bot isn't in that server (%s): invite it first (SETUP-LOCAL.md, Discord)" % e)
    print("server: %s; bot: %s%s" % (guild["name"], me["username"], "" if apply else "  (plan only: add --apply)"))
    everyone = str(g)  # the @everyone role has the server's id
    reason = "Do Damage server layout (tools/discord/setup_server.py)"

    def do(what, method, path, body=None):
        print(("  " if apply else "  would ") + what)
        return rest(method, path, body, reason) if apply else None

    # --- the bot's profile in this server only (its name elsewhere stays the application's)
    member = rest("GET", "/guilds/%d/members/%s" % (g, me["id"]))
    if member.get("nick") != dodmg.NAME or not member.get("avatar"):
        with open(ICON, "rb") as f:
            avatar = "data:image/png;base64," + base64.b64encode(f.read()).decode()
        do("set the bot's name here to %s, with the game's crown as its picture" % dodmg.NAME,
           "PATCH", "/guilds/%d/members/@me" % g, {"nick": dodmg.NAME, "avatar": avatar})
    else:
        print("bot profile: already %s" % dodmg.NAME)

    # --- roles
    roles = {r["name"].lower(): r for r in rest("GET", "/guilds/%d/roles" % g)}
    role_ids = {}
    for name, colour, hoist, mentionable, perms in ROLES:
        r = roles.get(name.lower())
        if r:
            role_ids[name] = r["id"]
            print("  role %s: already there" % name)
            continue
        made = do("make role %s" % name, "POST", "/guilds/%d/roles" % g,
                  {"name": name, "color": colour, "hoist": hoist, "mentionable": mentionable,
                   "permissions": str(perms)})
        role_ids[name] = made["id"] if made else "<new %s>" % name

    # --- categories and channels
    channels = rest("GET", "/guilds/%d/channels" % g)
    by_key = {}
    for c in channels:
        by_key.setdefault((c["type"] == CATEGORY, key(c["name"])), c)
    order = []
    ids = {}
    posts = []
    for cat_name, items in LAYOUT:
        cat = by_key.get((True, key(cat_name)))
        if cat:
            print("category %s: already there" % cat_name)
        else:
            cat = do("make category %s" % cat_name, "POST", "/guilds/%d/channels" % g,
                     {"name": cat_name, "type": CATEGORY}) or {"id": "<new %s>" % cat_name}
            print("category %s" % cat_name)
        order.append(cat["id"])
        for name, kind, topic, read_only, msg in items:
            overwrites = []
            if read_only:
                overwrites = [
                    {"id": everyone, "type": 0, "allow": str(ADD_REACTIONS | VIEW | READ_HISTORY), "deny": str(WRITE)},
                ] + [{"id": roles[n.lower()]["id"], "type": 0, "allow": str(WRITE), "deny": "0"}
                     for n in STAFF if n.lower() in roles] + [
                    {"id": me["id"], "type": 1, "allow": str(VIEW | SEND | EMBED_LINKS | READ_HISTORY | MANAGE_MESSAGES), "deny": "0"},
                ]
            c = next((by_key[(False, key(n))] for n in [name] + ALIASES.get(name, []) if (False, key(n)) in by_key), None)
            if c is None:
                body = {"name": name, "type": kind, "parent_id": cat["id"],
                        "permission_overwrites": [o for o in overwrites if not o["id"].startswith("<")]}
                if topic:
                    body["topic"] = topic
                c = do("make #%s%s" % (name, " (read-only)" if read_only else ""), "POST",
                       "/guilds/%d/channels" % g, body) or {"id": "<new %s>" % name, "name": name, "parent_id": cat["id"]}
            else:
                print("  #%s: already there%s" % (c["name"], "" if c.get("parent_id") == cat["id"] else ", moving it into %s" % cat_name))
                change = {}
                if c.get("parent_id") != cat["id"]:
                    change["parent_id"] = cat["id"]
                if topic and kind == TEXT and not c.get("topic"):
                    change["topic"] = topic
                if change:
                    do("  update #%s: %s" % (c["name"], ", ".join(change)), "PATCH", "/channels/%s" % c["id"], change)
                have = {o["id"]: o for o in c.get("permission_overwrites", [])}
                for o in overwrites:
                    h = have.get(o["id"])
                    if h and int(h["allow"]) & int(o["allow"]) == int(o["allow"]) and int(h["deny"]) & int(o["deny"]) == int(o["deny"]):
                        continue
                    allow = (int(h["allow"]) if h else 0) & ~int(o["deny"]) | int(o["allow"])
                    deny = (int(h["deny"]) if h else 0) & ~int(o["allow"]) | int(o["deny"])
                    if not o["id"].startswith("<"):
                        do("  set #%s's permissions for %s" % (c["name"], "@everyone" if o["id"] == everyone else o["id"]),
                           "PUT", "/channels/%s/permissions/%s" % (c["id"], o["id"]),
                           {"type": o["type"], "allow": str(allow), "deny": str(deny)})
            order.append(c["id"])
            ids[name] = c["id"]
            if msg:
                posts.append((c, msg))
    # --- the messages, once every channel they link to is there
    for c, msg in posts:
        post_message(rest, apply, do, me, c, message_embed(msg, ids), msg, args.refresh_messages)

    # --- order: the layout's categories and channels first, in the layout's order
    placed = set(order)
    if apply:
        order += [c["id"] for c in sorted(channels, key=lambda c: c["position"]) if c["id"] not in order]
        rest("PATCH", "/guilds/%d/channels" % g, [{"id": i, "position": n} for n, i in enumerate(order)], reason)
        print("arranged the categories and channels in order")
        write_env(args.env, {"DISCORD_GUILD_ID": g, "DISCORD_UPDATES_CHANNEL": ids["updates"],
                             "DISCORD_STATUS_CHANNEL": ids["server-status"],
                             "DISCORD_UPDATES_ROLE": role_ids["Updates"],
                             "DISCORD_POSTED_UP_TO": dodmg.releases_from_changelog()[0][0]})
        print("wrote the ids into %s" % args.env)
    left = [c["name"] for c in channels if c["id"] not in placed]
    if left:
        print("not in the layout (left as they are, below it): %s" % ", ".join(left))


def post_message(rest, apply, do, me, channel, embed, name, refresh):
    old = None
    if not str(channel["id"]).startswith("<"):
        for m in rest("GET", "/channels/%s/messages?limit=50" % channel["id"]):
            if m["author"]["id"] == me["id"] and m.get("embeds") and m["embeds"][0].get("title") == embed["title"]:
                old = m
    if old is None:
        do("  post %s in #%s" % (name, channel.get("name", "?")), "POST", "/channels/%s/messages" % channel["id"],
           {"embeds": [embed], "allowed_mentions": {"parse": []}})
    elif refresh and old["embeds"][0].get("description") != embed["description"]:
        do("  edit %s in #%s" % (name, channel["name"]), "PATCH", "/channels/%s/messages/%s" % (channel["id"], old["id"]),
           {"embeds": [embed]})
    else:
        print("  %s: already posted" % name)


if __name__ == "__main__":
    main()
