"""Builds the website's wiki (website/wiki/) into <site>/wiki: one page per guide, in the site's look,
with a sidebar, links between the pages and a search over all of them.

    python tools/build_wiki.py [--out build/site]      # tools/build_site.py runs it too

- The guides are Markdown, website/wiki/<page>.md, written by hand for players. A front matter
  block names the page (title, nav order, sprite, summary). The Markdown is the plain subset in
  `markdown()`: ## and ### headings, paragraphs, - and 1. lists, > notes, | tables, `code`,
  **bold**, *italics*, [links](page.html) and inline HTML such as <kbd>.
- `{{gen:<name>}}` on a line of its own is replaced by a section built from the game's data
  (GENERATORS below: the dungeon list, the classes, the commands, the Oryx chain, the shops, the
  pets and more), and `{{v:<name>}}` in the text by a number read from the code (VALUES), so the
  pages stay true as the game changes. Run `python tools/wiki_data.py` to see what the data says.
- The sidebar groups pages: one with `group: <name>` in its front matter heads a group of that
  name and opens on itself; pages with `parent: <its slug>` are listed under it while one of the
  group is showing.
- The Gear statistics page (gear-statistics.md) takes its tables from tools/wiki_balance.py.
- The gear pages (wiki/gear/, one per kind of item, and wiki/classes/, one per class) are built
  whole from the game's data by tools/wiki_gear.py, with the item sprites (wiki/icons.css and
  wiki/sheets/).
- Other files in website/wiki/ (wiki.js) are copied as they are. The search index is written to
  wiki/search-index.js, from every page's sections and every item.
"""
import argparse
import hashlib
import html
import json
import os
import re
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wiki_data as wd  # noqa: E402
import wiki_balance  # noqa: E402
import wiki_gear  # noqa: E402

ROOT = wd.ROOT
SRC = os.path.join(ROOT, "website", "wiki")

# The words for each command a regular player can use: the list itself (and who can use what)
# comes from the code, so a new command shows up with "(no description yet)" until it's added here.
COMMAND_HELP = {
    "commands": "Lists every command you can use.",
    "nexus": "Back to the Nexus from anywhere (the <kbd>R</kbd> key does the same).",
    "realm": "To the busiest open realm.",
    "godlands": "To the Godlands, the realm's richest and most dangerous part. Works from anywhere: outside a realm it takes you to the busiest one first.",
    "vault": "To your vault.",
    "guildhall": "To your guild's hall.",
    "arena": "To the arena.",
    "gear": "Free starter gear for your class, and a couple of boosters. See below for its limits.",
    "shop": "Opens a shop: weapons, abilities, armor, rings, consumables, keys and pets for fame. With no name it lists the shops.",
    "sellfame": "Selling items for fame is now done at the Divine Forge in the Nexus; this points you to it.",
    "forge": "Forging is now done at the Divine Forge in the Nexus; this points you to it.",
    "forgelist": "The recipes are in the Divine Forge's window, in the Nexus; this points you to it.",
    "trade": "Asks a player to trade: <code>/trade &lt;name&gt;</code>. Needs 2 rank stars; clicking a player's name and choosing Trade doesn't.",
    "tell": "Whispers to a player: <code>/tell &lt;name&gt; &lt;message&gt;</code> (or press <kbd>Tab</kbd>).",
    "guild": "Says something in guild chat (or press <kbd>G</kbd>).",
    "party": "Says something in party chat (or press <kbd>P</kbd>).",
    "pinvite": "Invites a player to your party (and makes the party if you have none).",
    "paccept": "Joins a party you were invited to: <code>/paccept &lt;party id&gt;</code>.",
    "pleave": "Leaves your party.",
    "pclose": "Closes the party you lead.",
    "premove": "Removes a player from the party you lead.",
    "pinfo": "Shows your party.",
    "pinviteworld": "Invites your party to the dungeon you're in.",
    "pjoin": "Joins the dungeon your party leader invited you to.",
    "invite": "Invites a player to your guild (officers and up).",
    "guildkick": "Removes a player from your guild.",
    "guildwho": "Lists your guild's members who are online.",
    "who": "Lists the players in your world.",
    "swho": "Lists the players on the whole server.",
    "where": "Says where a player is: <code>/where &lt;name&gt;</code>.",
    "online": "How many players are online.",
    "servers": "The world servers and how many players each has.",
    "world": "Which world you're in and how many players are there.",
    "teleport": "Teleports you to a player in the same world: <code>/tp &lt;name&gt;</code>.",
    "position": "Your position on the map.",
    "boosts": "Your loot and experience boosts and how long they last.",
    "getquest": "Says which monster your quest arrow points at.",
    "stats": "Your character's stats.",
    "rules": "The server's rules.",
    "ignore": "Hides a player's chat: <code>/ignore &lt;name&gt;</code>; <code>/unignore</code> shows it again.",
    "unignore": "Shows an ignored player's chat again.",
    "lock": "Locks a player (they show first in your lists); <code>/unlock</code> undoes it.",
    "unlock": "Unlocks a player.",
    "afk": "Pauses you while no monster is near (not in arenas). Again to unpause.",
    "pause": "The same as /afk.",
    "arenas": "Lists the arena games running now.",
    "leaderboard": "The best Battle Arena waves.",
    "gleaderboard": "The guilds with the most fame.",
    "uptime": "How long the server has been up.",
    "restartwhen": "When the world server next restarts.",
    "removepet": "Sends your pet away.",
    "fixpots": "Repairs your potion storage if it shows nothing.",
    "checkid": "Shows your account and player ids (for bug reports).",
    "nothing": "Does nothing.",
}
# commands players see in the list but that don't do anything useful for them
COMMAND_HIDDEN = {"join", "imnotguest", "deltatime", "sell"}


# --- Markdown ------------------------------------------------------------------------------------
def slugify(text):
    text = re.sub(r"<[^>]+>", "", text)
    text = html.unescape(text).lower()
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-") or "section"


def inline(text):
    """Inline Markdown in a line of text, which may hold HTML already."""
    # escape what isn't HTML: a & that isn't an entity, a < that doesn't start a tag
    text = re.sub(r"&(?!#?\w+;)", "&amp;", text)
    text = re.sub(r"<(?![/a-zA-Z!])", "&lt;", text)
    codes = []

    def keep(m):
        codes.append("<code>%s</code>" % m.group(1).replace("<", "&lt;"))
        return "\x00%d\x00" % (len(codes) - 1)
    text = re.sub(r"`([^`]+)`", keep, text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![*\w])\*([^*\s][^*]*)\*(?!\w)", r"<em>\1</em>", text)
    text = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", r'<a href="\2">\1</a>', text)
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)


def markdown(text, generate):
    """The wiki's Markdown to HTML; generate(name) gives a {{gen:name}} section's HTML."""
    out = []
    lines = text.split("\n")
    i = 0
    used = set()

    def heading(level, title):
        ident = slugify(title)
        n = 2
        base = ident
        while ident in used:
            ident = "%s-%d" % (base, n)
            n += 1
        used.add(ident)
        return '<h%d id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>%s</h%d>' % (
            level, ident, ident, inline(title), level)

    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if not s:
            i += 1
            continue
        m = re.match(r"\{\{gen:([\w-]+)\}\}$", s)
        if m:
            out.append(generate(m.group(1), used))
            i += 1
            continue
        m = re.match(r"(#{2,4})\s+(.*)$", s)
        if m:
            out.append(heading(len(m.group(1)), m.group(2)))
            i += 1
            continue
        if s.startswith(">"):
            block = []
            while i < len(lines) and lines[i].strip().startswith(">"):
                block.append(lines[i].strip()[1:].strip())
                i += 1
            paras = " ".join(block).split("  ")
            out.append('<aside class="note">%s</aside>' % "".join("<p>%s</p>" % inline(p) for p in paras if p))
            continue
        if s.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            head, body = rows[0], [r for r in rows[1:] if not all(re.match(r"^:?-+:?$", c) for c in r)]
            out.append('<div class="table-wrap"><table><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>' % (
                "".join("<th>%s</th>" % inline(c) for c in head),
                "".join("<tr>%s</tr>" % "".join("<td>%s</td>" % inline(c) for c in r) for r in body)))
            continue
        m = re.match(r"(-|\d+\.)\s+", s)
        if m:
            ordered = m.group(1) != "-"
            items = []
            while i < len(lines):
                t = lines[i]
                mm = re.match(r"\s*(-|\d+\.)\s+(.*)$", t)
                if mm and not t.startswith("    "):
                    items.append(mm.group(2))
                elif t.startswith("  ") and t.strip() and items:
                    items[-1] += " " + t.strip()
                else:
                    break
                i += 1
            tag = "ol" if ordered else "ul"
            out.append("<%s>%s</%s>" % (tag, "".join("<li>%s</li>" % inline(x) for x in items), tag))
            continue
        para = [s]
        i += 1
        while i < len(lines) and lines[i].strip() and not re.match(r"(#{2,4}\s|>|\||-\s|\d+\.\s|\{\{gen:)", lines[i].strip()):
            para.append(lines[i].strip())
            i += 1
        out.append("<p>%s</p>" % inline(" ".join(para)))
    return "\n".join(out)


def front_matter(text):
    meta = {}
    if text.startswith("---"):
        head, _, text = text[3:].partition("\n---")
        for line in head.strip().splitlines():
            k, _, v = line.partition(":")
            meta[k.strip()] = v.strip()
        text = text.lstrip("\n")
    return meta, text


# --- Generated sections --------------------------------------------------------------------------
def esc(s):
    return html.escape(str(s), quote=True)


def pct(c):
    if c is None:
        return ""
    if c >= 1:
        return "always"
    v = c * 100
    return ("%.1f%%" % v).replace(".0%", "%") if v >= 1 else ("%.2g%%" % v)


def nice(ids, d, limit=None):
    names = []
    for i in ids:
        n = d.name(i)
        if n not in names:
            names.append(n)
    if limit and len(names) > limit:
        return ", ".join(names[:limit]) + " and %d more" % (len(names) - limit)
    return ", ".join(names)


# objects that drop a portal for an event, named for players by the event
WHO = {"Hermit portal maker": "Hermit God", "Encounter Altar": "The Statues", "Tomb Boss Anchor": "the Tomb's bosses"}


def who(d, m):
    """A monster's name for players; Oryx's stages that share a display name are numbered."""
    if m in WHO:
        return WHO[m]
    st = re.match(r"Oryx the Mad God (\d)$", m)
    return "Oryx %s" % st.group(1) if st and d.name(m) == "Oryx the Mad God" else d.name(m)


def with_article(name):
    """"an Oryx's Castle Key", "an XP Booster", "a Loot Drop Potion"."""
    return ("an " if re.match(r"[AEIOU]|X[A-Z]", name) else "a ") + name


def unique(drops, d):
    """(monster, chance) pairs, likeliest first, one per name players see."""
    out, seen = [], set()
    for m, c in sorted(drops, key=lambda s: -(s[1] or 0)):
        if who(d, m) not in seen:
            seen.add(who(d, m))
            out.append((m, c))
    return out


def joined(parts):
    parts = [p for p in parts if p]
    if len(parts) <= 1:
        return "".join(parts)
    return ", ".join(parts[:-1]) + " and " + parts[-1]


HIDDEN_MONSTER = re.compile(r"Admin|Test(?!Chicken)|XP\b|Spawner|spawner|Controller|Event Hive Corpse|Loot Balloon|Loot Chest|Treasure Cache")
# bosses a script brings in, which the dungeon's map doesn't place
BOSS_OVERRIDE = {"Tomb of the Ancients": "Tomb Defender", "Elder Tomb of the Ancients": "Elder Tomb Defender",
                 "Ice Cave": "ic Esben the Unwilling", "The Shatters": "shtrs The Forgotten King",
                 "Haunted Cemetery": "Ghost of Skuld", "Cultist Hideout": "LH Malus",
                 "The Ivory Wyvern": "LOD Ivory Wyvern", "Lair of Shaitan": "md1 Head of Shaitan",
                 "Woodland Labyrinth": "Murderous Megamoth", "Magic Woods": "MW Fountain Spirit",
                 "Cave of A Thousand Treasures": "Golden Oryx Effigy"}
BOSS_PROPS = re.compile(r"Spawner|spawner|Wall|Sarcophagus|Present|Chest|Anchor|Gate|Tooky|Totem$")
EQUIP_SLOTS = set(wd.SLOT_NAMES)
ORYX_CHAIN = ["Oryx's Castle", "Oryx's Chamber", "Chicken Chamber", "Wine Cellar", "Oryx's Sanctuary",
              "Oryx's Inferno", "Doomed Cellar"]
SKIP_WORLDS = {"Admin Room", "Admin Arena", "Shop", "?????", "Lair of Shaitan New Auru", "Battle Arena",
               "Free Battle Arena"}
# difficulty from the boss's health: the upper bound of each of six steps
DIFFICULTY = [6000, 20000, 45000, 90000, 160000]
DIFFICULTY_WORDS = ["Beginner", "Easy", "Medium", "Hard", "Very hard", "Endgame"]


class Gen:
    """The generated sections, built once from the game's data."""

    def __init__(self):
        self.d = wd.Data()
        self.idx = self.d.drops_index()
        self.bazaar = self.d.bazaar()
        self.portals = self.d.portals()
        self.keys = self.d.keys()
        self.incant = self.d.incantations()
        self.bot = wd.support_bot()
        self.realm = wd.realm()
        self.account = wd.account()
        self.gear = wd.gear_rules()
        self.stars = wd.star_fames()
        self._dungeons = None
        self._equip = None

    @property
    def equip(self):
        """The gear pages and their items (tools/wiki_gear.py)."""
        if self._equip is None:
            self._equip = wiki_gear.Gear(self, sys.modules[__name__])
        return self._equip

    def gen_gear(self, used):
        return self.equip.overview(used)

    # --- the Divine Forge (wiki_data.forge_recipes, as ForgeRecipes.cs builds them) -----------
    @property
    def forge(self):
        if getattr(self, "_forge", None) is None:
            self._forge = self.d.forge_recipes()
        return self._forge

    def forge_item(self, oid, count=1):
        """An item's icon and name, linked to its gear page entry, with "2 ×" for a pair."""
        eq = self.equip
        it = next((i for i in eq.items if i["id"] == oid), None)
        ico = eq.icon(it) if it else eq.sprite(self.d.texture(oid), wiki_gear.SPECIAL_EDGE)
        name = esc(self.d.name(oid))
        href = eq.href.get(oid)
        if href:
            name = '<a href="%s">%s</a>' % (href, name)
        n = '<span class="fr-n">%d &times;</span>' % count if count > 1 else ""
        return '<span class="fr-item">%s%s<span class="fr-name">%s</span></span>' % (n, ico, name)

    def gen_forge_banner(self, used):
        return ('<figure class="banner"><img src="forge.webp" width="1040" height="700" alt="The Divine Forge\'s '
                'window: the sword recipes, with Skysplitter Sword picked: two Archon Swords and 800 fame">'
                '<figcaption>The Divine Forge\'s window, forging a Skysplitter Sword (T11) from two Archon '
                'Swords (T10)</figcaption></figure>')

    def gen_forge_example(self, used):
        r = next((r for r in self.forge if r["result"] == "Skysplitter Sword" and r["chain"]), None) or \
            next(r for r in self.forge if r["chain"])
        return '<p class="fr-recipe">%s<span class="fr-arrow">&rarr;</span>%s<span class="fr-fame">%s fame</span></p>' % (
            self.forge_item(r["items"][0], 2), self.forge_item(r["result"]), "{:,}".format(r["fame"]))

    def gen_forge_tiers(self, used):
        """The two-for-one upgrades' fame by tier, a row for each set of kinds that share a price list."""
        eq = self.equip
        ladders = {}
        for r in self.forge:
            if r["chain"]:
                o = self.d.objects[r["result"]]
                ladders.setdefault(o["slot"], {})[o["tier"]] = r["fame"]
        order = [s for cat in ("weapons", "armor", "abilities", "rings") for s in eq.kinds.get(cat, [])]
        order += sorted(s for s in ladders if s not in order)
        rows = []
        for slot in order:
            if slot not in ladders:
                continue
            ladder = ladders[slot]
            row = next((r for r in rows if r[1] == ladder), None)
            if row:
                row[0].append(slot)
            else:
                rows.append(([slot], ladder))
        top = max(t for _, lad in rows for t in lad)
        head = "<th>Kind</th>" + "".join("<th>T%d</th>" % t for t in range(1, top + 1))
        body = []
        for slots, lad in rows:
            names = ", ".join('<a href="gear/%s">%s</a>' % (eq.kind_file(s), esc(wiki_gear.plural(eq.kind_name(s))))
                              for s in slots)
            cells = "".join("<td>%s</td>" % ("{:,}".format(lad[t]) if t in lad else "&ndash;")
                            for t in range(1, top + 1))
            body.append('<tr><th scope="row">%s</th>%s</tr>' % (names, cells))
        return ('<div class="table-wrap wide fr-tiers"><table><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>'
                % (head, "".join(body)))

    def gen_forge_special(self, used):
        """The other recipes, by what they make: the Doomed Realms forge's own."""
        eq = self.equip
        cat_of = {i["id"]: i["category"] for i in eq.items}
        groups = [("weapons", "Weapons"), ("abilities", "Abilities"), ("armor", "Armor"), ("rings", "Rings"),
                  (None, "Potions and the rest")]
        slot_order = [s for cat, _ in groups if cat for s in eq.kinds.get(cat, [])]
        out = []
        for cat, title in groups:
            rs = [r for r in self.forge if not r["chain"] and cat_of.get(r["result"]) == cat]
            if not rs:
                continue
            o = self.d.objects
            rs.sort(key=lambda r: (slot_order.index(o[r["result"]]["slot"]) if o[r["result"]]["slot"] in slot_order
                                   else 99, r["fame"], r["result"]))
            rows = []
            for r in rs:
                counts = {}
                for i in r["items"]:
                    counts[i] = counts.get(i, 0) + 1
                ing = "".join(self.forge_item(i, n) for i, n in counts.items())
                note = ' <span class="tag" style="--c:%s">Forge Amulet</span>' % wiki_gear.SPECIAL_EDGE if r["advanced"] else ""
                rows.append('<tr><td>%s%s</td><td class="fr-ing">%s</td><td class="fr-fame">%s</td></tr>' % (
                    self.forge_item(r["result"]), note, ing, "{:,}".format(r["fame"])))
            out.append('<h3 id="%s">%s</h3><div class="table-wrap fr-list"><table><thead><tr><th>Makes</th>'
                       '<th>From</th><th>Fame</th></tr></thead><tbody>%s</tbody></table></div>' % (
                           "forge-" + slugify(title), esc(title), "".join(rows)))
            used.add("forge-" + slugify(title))
        return "\n".join(out)

    # --- the Gear statistics page (tools/wiki_balance.py) ------------------------------------
    @property
    def balance(self):
        if getattr(self, "_balance", None) is None:
            self._balance = wiki_balance.Balance(self)
        return self._balance

    def gen_tier_tables(self, used):
        return self.balance.tier_tables(used)

    def gen_class_dps(self, used):
        return self.balance.class_dps(used)

    def gen_class_ehp(self, used):
        return self.balance.class_ehp(used)

    def gen_class_totals(self, used):
        return self.balance.class_totals(used)

    def item_links(self, ids, up="", limit=None):
        """Item names, each a link to its entry on the gear pages."""
        out, seen = [], set()
        for i in ids:
            n = self.d.name(i)
            if n in seen:
                continue
            seen.add(n)
            href = self.equip.href.get(i)
            out.append('<a href="%s%s">%s</a>' % (up, href, esc(n)) if href else esc(n))
        if limit and len(out) > limit:
            return ", ".join(out[:limit]) + " and %d more" % (len(out) - limit)
        return ", ".join(out)

    # values for {{v:name}} in the guides
    def values(self):
        a, b, g = self.account, self.bot, self.gear
        gift = self.d.objects.get("XP Gift", {})
        crate = wd.mystery_crate()
        return {
            "forge_recipes": "{:,}".format(len(self.forge)), "forge_chains": "{:,}".format(sum(r["chain"] for r in self.forge)),
            "forge_cap": "{:,}".format(wd.Data.FORGE_TIERED_CAP), "name_change": "{:,}".format(wd.name_change_fame()),
            "char_slots": a["char_slots"], "vault_chests": a["vault_chests"],
            "slot_cost": "%d %s" % (a["slot_cost"], a["slot_currency"]),
            "vault_chest_price": a["vault_chest_price"], "potion_stack": a["potion_stack"],
            "storage_base": a["storage_base"], "storage_step": a["storage_step"], "storage_cost": a["storage_cost"],
            "gear_level": g["max_level"], "gear_max_tier": g["max_tier"],
            "gear_boosters": joined([with_article(self.d.name(b))
                                     for b in g["boosters"]]) or "nothing else",
            "xp_boost": "%d%%" % round((wd.xp_boost() - 1) * 100) if wd.xp_boost() else "more",
            "bot_gifts": b["gifts"], "bot_minutes": "%g" % b["gift_minutes"], "bot_talk": "%g" % b["talk_seconds"],
            "bot_portal_offset": "%g" % (b["gift_minutes"] / 2), "bot_cleared": "%g" % b["cleared_seconds"],
            "gift_xp": int(gift.get("hp", 0) / 10),
            "realm_close": self.realm["close_after"],
            "level20_xp": "{:,}".format(wd.level_exp(20)),
            "stars": ", ".join(str(s) for s in self.stars),
            "star_count": len(self.stars),
            "class_count": len(self.d.classes()),
            "max_stars": len(self.d.classes()) * len(self.stars),
            "pet_price": self.d.pet_generator_price,
            "crate_items": len(crate),
            "dungeon_count": len(self.dungeons()),
            "pet_count": len([p for p in self.d.pets()]),
            "pet_bazaar": len(self.d.bazaar_pets() & {p["item"] for p in self.d.pets()}),
            "summon_count": len([s for s in self.d.summons() if "Test" not in self.d.objects[s["item"]]["file"]]),
            "yard_count": len(self.d.yard_pets()),
            "yard_families": len({p["family"] for p in self.d.yard_pets()}),
            "skin_unlockers": len([o for o in self.d.objects.values() if o["item"]
                                   and any(a["effect"] == "UnlockPetSkin" for a in o["activate_attrs"])]),
        }

    def __call__(self, name, used):
        fn = getattr(self, "gen_" + name.replace("-", "_"), None)
        if fn is None:
            raise SystemExit("wiki: no generator for {{gen:%s}}" % name)
        return fn(used)

    # --- commands ----------------------------------------------------------------------------
    def gen_commands(self, used):
        rows = []
        missing = []
        staff = 0
        for name, alias, rank, path in wd.commands():
            if rank != "Regular":
                staff += 1
                continue
            if name in COMMAND_HIDDEN:
                continue
            words = COMMAND_HELP.get(name)
            if not words:
                missing.append(name)
                words = "(no description yet)"
            al = ' <span class="dim">or /%s</span>' % esc(alias) if alias else ""
            rows.append('<tr id="cmd-%s"><td><code>/%s</code>%s</td><td>%s</td></tr>' % (esc(name), esc(name), al, words))
        if missing:
            print("wiki: commands without a description in COMMAND_HELP: " + ", ".join(missing))
        return ('<div class="table-wrap"><table class="cmds"><thead><tr><th>Command</th><th>What it does</th></tr></thead>'
                '<tbody>%s</tbody></table></div><p class="dim">%d more commands are for supporters and staff.</p>'
                % ("".join(rows), staff))

    def gen_bot_tips(self, used):
        return "<ol class=\"tips\">%s</ol>" % "".join("<li>%s</li>" % esc(t) for t in self.bot["tips"])

    # --- the realm ---------------------------------------------------------------------------
    def gen_godlands(self, used):
        return '<ul class="chips">%s</ul>' % "".join("<li>%s</li>" % esc(self.d.name(g)) for g in self.realm["gods"])

    def gen_realm_events(self, used):
        names = {"shtrs Defense System": "Avatar of the Forgotten King", "EH Event Hive": "Killer Bee Nest",
                 "Garnet Statue": "The Statues"}
        items = []
        for e in self.realm["events"]:
            n = names.get(e) or self.d.name(e)
            ps = [p for p, lst in self.idx.items() if any(m == e for m, _ in lst)
                  and self.d.objects.get(p, {}).get("class") == "Portal" and p not in ("Realm Portal",)]
            # what the event leads to: the dungeon portals it (or its boss) drops
            dungeons = sorted({self.d.objects[p]["dungeon"] for p in ps if self.d.objects[p].get("dungeon")})
            tail = " (can open: %s)" % ", ".join(esc(x) for x in dungeons) if dungeons else ""
            items.append("<li><strong>%s</strong>%s</li>" % (esc(n), tail))
        return '<ul class="cols">%s</ul>' % "".join(items)

    # --- dungeons ----------------------------------------------------------------------------
    def dungeons(self):
        if self._dungeons is not None:
            return self._dungeons
        d = self.d
        bot = set(self.bot["portals"])
        out = []
        locked_by = self.idx.get("Locked Wine Cellar Portal", [])
        for wid, w in d.worlds.items():
            if wid in SKIP_WORLDS:
                continue
            portals = self.portals.get(wid, [])
            sources = []
            for p in portals:
                for m, c in self.idx.get(p, []):
                    if HIDDEN_MONSTER.search(m) or c == 0:
                        continue
                    if not any(x == m for x, _ in sources):
                        sources.append((m, c))
            keys = []
            for p in portals:
                for k in self.keys.get(p, []):
                    if re.search(r"Guild|PermaKey|Enchanted|^icpk|^pdpk|Halloween", k):
                        continue
                    kd = [(m, c) for m, c in self.idx.get(k, []) if not HIDDEN_MONSTER.search(m) and c]
                    price = self.bazaar.get(k)
                    if kd or price:
                        keys.append((k, kd, price))
            enchanted = [(k, self.bazaar[k][0]) for p in portals for k in self.keys.get(p, [])
                         if "Enchanted" in k and k in self.bazaar]
            special = []
            unlock = []
            for p in portals:
                for item, areas in self.incant.get(p, []):
                    if "Incantation" not in d.name(item):
                        continue
                    bosses = [m for m, _ in locked_by if any(m in d.map_objects(d.worlds[a]) or m in self.bosses_of(a)
                                                             for a in areas if a in d.worlds)]
                    unlock.append((item, areas, bosses))
            if wid == "Oryx's Castle":
                special.append("Opens for everyone in a realm when it closes, once enough of its events are defeated: %s."
                               % self.realm["close_after"])
            on_bot = any(p in bot for p in portals)
            if not (sources or keys or special or unlock):
                continue
            boss, hp = self.boss(wid)
            loot = self.notable_loot(wid)
            group = ("oryx" if wid in ORYX_CHAIN else "elder" if wid.startswith("Elder") or "Elder" in wid
                     else "doomed" if "DoomedRealms" in w["file"] else "realm")
            out.append({
                "id": wid, "name": w["name"], "group": group, "sources": sources, "keys": keys,
                "enchanted": enchanted, "special": special, "unlock": unlock, "bot": on_bot,
                "boss": boss, "hp": hp, "loot": loot, "random": w["random"],
                "level": self.level(hp),
            })
        out.sort(key=lambda x: (x["level"] or 99, x["hp"], x["name"]))
        self._dungeons = out
        return out

    def bosses_of(self, wid):
        w = self.d.worlds.get(wid)
        if not w:
            return []
        objs = self.d.map_objects(w)
        found = [o for o in objs if self.d.objects.get(o, {}).get("quest")]
        for g in w["goals"]:
            if g not in found:
                found.append(g)
        return found

    def boss(self, wid):
        """The dungeon's boss: the toughest quest monster on its map with a loot table, else the
        toughest monster with one. Props that can't be hurt (a million health) don't count."""
        def real(o):
            obj = self.d.objects.get(o, {})
            hp = self.d.boss_health(o)
            return obj.get("enemy") and 0 < hp < 500000 and not BOSS_PROPS.search(o)

        def loots(o):
            return any(k in ("item", "tier") for k, _, _ in self.d.drops(o))
        if BOSS_OVERRIDE.get(wid) in self.d.objects:
            return BOSS_OVERRIDE[wid], self.d.boss_health(BOSS_OVERRIDE[wid])
        quests = [o for o in self.bosses_of(wid) if real(o)]
        placed = [o for o in self.d.map_objects(self.d.worlds[wid]) if real(o)]
        for cands in ([o for o in quests if loots(o)], [o for o in placed if loots(o)], quests):
            if cands:
                best = max(cands, key=self.d.boss_health)
                return best, self.d.boss_health(best)
        return None, 0

    def level(self, hp):
        if not hp:
            return 0
        for i, top in enumerate(DIFFICULTY):
            if hp < top:
                return i + 1
        return len(DIFFICULTY) + 1

    def notable_loot(self, wid):
        items = []
        boss = self.boss(wid)[0]
        for b in ([boss] if boss else []) + self.bosses_of(wid):
            for kind, oid, chance in self.d.drops(b):
                if kind != "item":
                    continue
                o = self.d.objects.get(oid)
                if not o or not o["item"]:
                    continue
                notable = (o["tier"] < 0 and o["slot"] in EQUIP_SLOTS) or "Incantation" in oid
                if notable and oid not in [i for i, _ in items]:
                    items.append((oid, chance))
        items.sort(key=lambda x: -(x[1] or 0))
        return items

    def gen_dungeons(self, used):
        groups = [("realm", "Realm dungeons", "Opened by monsters in the realm, by keys, or by the Support Bot in the Nexus."),
                  ("elder", "Elder dungeons", "Darker, harder versions of the regular dungeons, with Elder monsters worth twice the experience. Their bosses drop the regular loot and Elder drops."),
                  ("doomed", "Doomed Realms dungeons and minigames", "Ported from Doomed Realms. Most open with a key from the Nexus Bazaar."),
                  ("oryx", "The Oryx chain", "Oryx's own dungeons, one after the other. See <a href=\"endgame.html\">the endgame</a>.")]
        parts = ['<div class="filter"><label for="dg-filter">Filter</label><input id="dg-filter" type="search" '
                 'placeholder="Name, boss, key or item" data-filter=".dg" autocomplete="off"></div>',
                 '<p class="legend"><span class="danger" style="--d:1"></span> difficulty, from the boss\'s health: '
                 + ", ".join("%d %s" % (i + 1, w) for i, w in enumerate(DIFFICULTY_WORDS)) + ".</p>"]
        for key, title, blurb in groups:
            ds = [x for x in self.dungeons() if x["group"] == key]
            if not ds:
                continue
            gid = slugify(title)
            used.add(gid)
            parts.append('<h2 id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>%s</h2><p>%s</p>'
                         % (gid, gid, esc(title), blurb))
            if key == "oryx":
                ds.sort(key=lambda x: ORYX_CHAIN.index(x["id"]))
            parts.append('<div class="dgs">')
            for x in ds:
                parts.append(self.dungeon_card(x, used))
            parts.append("</div>")
        return "\n".join(parts)

    def dungeon_card(self, x, used):
        d = self.d
        ident = "d-" + slugify(x["id"])
        used.add(ident)
        rows = []
        src = []
        sources = unique(x["sources"], d)
        for m, c in sources[:5]:
            src.append("%s%s" % (esc(who(d, m)), " (%s)" % pct(c) if c and c < 1 else ""))
        more = len(sources) - 5
        if more > 0:
            src.append("%d more" % more)
        if x["special"]:
            rows.append(("Opens", " ".join(esc(s) for s in x["special"])))
        for item, areas, bosses in x["unlock"]:
            beaten = " or ".join(esc(who(d, b)) for b in bosses) or "The boss"
            name = d.name(item)
            rows.append(("Opens", "%s leaves a locked portal behind (%s). Use %s<strong>%s</strong> beside it." % (
                beaten, joined([esc(a) for a in areas]), with_article(name)[:-len(name)], esc(name))))
        if src:
            rows.append(("Portal from", ", ".join(src)))
        if x["bot"]:
            rows.append(("Nexus", "The Support Bot opens it sometimes"))
        for k, kd, price in x["keys"]:
            bits = []
            if price:
                bits.append("Nexus Bazaar, %d fame" % price[0])
            if kd:
                kd = unique(kd, d)
                names = ["%s (%s)" % (esc(who(d, m)), pct(c)) for m, c in kd[:3]]
                if len(kd) > 3:
                    names.append("%d more" % (len(kd) - 3))
                bits.append("drops from " + ", ".join(names))
            rows.append(("Key", "<strong>%s</strong>: %s" % (esc(d.name(k)), "; ".join(bits))))
        for k, price in x["enchanted"]:
            rows.append(("Enchanted key", "%s, %d fame (also gives a Mystery ST Crate)" % (esc(d.name(k)), price)))
        if x["boss"]:
            rows.append(("Boss", "%s, %s health" % (esc(who(d, x["boss"])), "{:,}".format(x["hp"]))))
        if x["loot"]:
            rows.append(("Notable loot", self.item_links([i for i, _ in x["loot"]], limit=8)))
        if x["random"]:
            rows.append(("Layout", "New every run"))
        lvl = x["level"]
        pips = ('<span class="danger" style="--d:%d" role="img" aria-label="Difficulty %d of 6: %s" title="%s"></span>'
                % (lvl, lvl, DIFFICULTY_WORDS[lvl - 1], DIFFICULTY_WORDS[lvl - 1]) if lvl else "")
        search = " ".join([x["name"], d.name(x["boss"] or "")] + [d.name(m) for m, _ in x["sources"]]
                          + [d.name(k) for k, _, _ in x["keys"]] + [d.name(i) for i, _ in x["loot"]])
        return ('<article class="dg" id="%s" data-search="%s"><header><h3>%s</h3>%s</header>'
                '<dl>%s</dl></article>' % (ident, esc(search.lower()), esc(x["name"]), pips,
                                         "".join("<div><dt>%s</dt><dd>%s</dd></div>" % r for r in rows)))

    # --- the Oryx chain ----------------------------------------------------------------------
    def gen_oryx_chain(self, used):
        d = self.d
        rows = []
        for i, wid in enumerate(ORYX_CHAIN):
            x = next((y for y in self.dungeons() if y["id"] == wid), None)
            if not x:
                continue
            how = []
            if x["special"]:
                how.append("A realm closes")
            for item, areas, bosses in x["unlock"]:
                how.append("%s, in %s" % (esc(d.name(item)), joined([esc(a) for a in areas])))
            if not how and x["sources"]:
                how.append("Portal from " + joined([esc(who(d, m)) for m, _ in x["sources"]]))
            for k, kd, price in x["keys"]:
                bits = []
                if kd:
                    bits.append("from " + joined(["%s (%s)" % (esc(who(d, m)), pct(c)) for m, c in kd[:3]]))
                if price:
                    bits.append("%d fame in the Bazaar" % price[0])
                how.append("%s (%s)" % (esc(with_article(d.name(k))), ", ".join(bits)))
            how = how[:1] + ["or " + h for h in how[1:]]
            if how:
                how[0] = how[0][0].upper() + how[0][1:]
            rows.append("<tr><td>%s</td><td><a href=\"dungeons.html#d-%s\">%s</a></td><td>%s</td><td>%s</td><td>%s</td></tr>" % (
                "" if wid == "Chicken Chamber" else "", slugify(wid), esc(x["name"]), esc(who(d, x["boss"]) if x["boss"] else ""),
                "{:,}".format(x["hp"]), ", ".join(how)))
        # stage numbers, with the Chicken Chamber as a side door of stage 2
        out, n = [], 0
        for wid, r in zip([w for w in ORYX_CHAIN if any(y["id"] == w for y in self.dungeons())], rows):
            if wid == "Chicken Chamber":
                r = r.replace("<td></td>", "<td>%d, side door</td>" % n, 1)
            else:
                n += 1
                r = r.replace("<td></td>", "<td>%d</td>" % n, 1)
            out.append(r)
        return ('<div class="table-wrap"><table class="chain"><thead><tr><th>Stage</th><th>Dungeon</th><th>Boss</th>'
                '<th>Health (solo)</th><th>How to get in</th></tr></thead><tbody>%s</tbody></table></div>' % "".join(out))

    def gen_incantations(self, used):
        d = self.d
        rows = []
        def stage(portal):
            world = d.objects.get(portal, {}).get("dungeon")
            return ORYX_CHAIN.index(world) if world in ORYX_CHAIN else 99
        for portal, lst in sorted(self.incant.items(), key=lambda kv: stage(kv[0])):
            for item, areas in lst:
                if "Incantation" not in d.name(item):
                    continue  # the Wine Cellar Key: an old key that now works like the incantation
                src = sorted([(m, c) for m, c in self.idx.get(item, []) if not HIDDEN_MONSTER.search(m) and c],
                             key=lambda s: -(s[1] or 0))
                price = self.bazaar.get(item)
                where = ["%s (%s)" % (esc(who(d, m)), pct(c)) for m, c in src[:6]]
                if len(src) > 6:
                    where.append("%d more" % (len(src) - 6))
                if price:
                    where.append("the Nexus Bazaar (%d fame)" % price[0])
                rows.append("<tr><td><strong>%s</strong></td><td>%s</td><td>%s</td><td>%s</td></tr>" % (
                    esc(d.name(item)), joined([esc(a) for a in areas]), esc(d.name(d.objects[portal]["dungeon"])
                                                                          if portal in d.objects else portal),
                    ", ".join(where)))
        return ('<div class="table-wrap"><table><thead><tr><th>Incantation</th><th>Use it in</th><th>Opens</th>'
                '<th>Where to get it</th></tr></thead><tbody>%s</tbody></table></div>' % "".join(rows))

    # --- items -------------------------------------------------------------------------------
    def gen_tiers(self, used):
        return '<ul class="swatches">%s</ul>' % "".join(
            '<li><span class="swatch" style="--c:%s"></span>%s</li>' % (c, esc(label)) for label, c in wd.tier_colours())

    def gen_stat_potions(self, used):
        rows = []
        for oid, o in self.d.objects.items():
            if not oid.startswith("Potion of ") or not o["item"] or re.search(r"Max |\(SB\)", oid):
                continue
            inc = [a for a in o["activate_attrs"] if a["effect"] == "IncrementStat"]
            if len(inc) != 1:
                continue
            stat = wd.STATS.get(wd._int(inc[0].get("stat")), "?")
            price = self.bazaar.get(oid)
            rows.append((stat, "<tr><td>%s</td><td>%s %s</td><td>%s</td></tr>" % (
                esc(o["name"]), "%+d" % wd._int(inc[0].get("amount")), esc(stat),
                "%d fame" % price[0] if price else "")))
        order = list(wd.STATS.values())
        rows.sort(key=lambda r: order.index(r[0]) if r[0] in order else 99)
        seen, body = set(), []
        for stat, r in rows:
            if r not in seen:
                seen.add(r)
                body.append(r)
        return ('<div class="table-wrap"><table><thead><tr><th>Potion</th><th>Raises</th><th>Nexus Bazaar</th></tr></thead>'
                '<tbody>%s</tbody></table></div>' % "".join(body))

    def gen_shop(self, used):
        groups = {}
        for item, (price, shop) in self.bazaar.items():
            groups.setdefault(shop, []).append((item, price))
        out = []
        for shop in ("Specials", "Consumables"):
            out.append("<h4>%s</h4><ul class=\"price-list\">%s</ul>" % (esc(shop), "".join(
                "<li><span>%s</span><span>%d fame</span></li>" % (esc(self.d.name(i)), p) for i, p in groups.get(shop, []))))
        return '<div class="shops">%s</div>' % "".join(out)

    def gen_crate(self, used):
        crate = wd.mystery_crate()
        total = sum(w for _, w in crate) or 1
        merged = {}
        sets = [i for i, _ in crate if re.search(r"ST\d$", i)]
        for i, w in crate:
            key = "A set piece (one of %d)" % len(sets) if i in sets else self.d.name(i)
            merged[key] = merged.get(key, 0) + w
        return '<ul class="price-list">%s</ul>' % "".join(
            "<li><span>%s</span><span>%s</span></li>" % (esc(n), ("%.1f%%" % (100 * w / total)).replace(".0%", "%"))
            for n, w in sorted(merged.items(), key=lambda x: -x[1]))

    def gen_enchanted(self, used):
        ks = sorted([(i, p) for i, (p, s) in self.bazaar.items() if s == "Enchanted Keys"], key=lambda x: x[1])
        return '<ul class="price-list cols">%s</ul>' % "".join(
            "<li><span>%s</span><span>%d fame</span></li>" % (esc(i), p) for i, p in ks)

    def gen_sets(self, used):
        d = self.d
        rows = []
        for s in d.sets():
            pieces = [p for p in s["pieces"] if p in d.objects]
            if len(pieces) != 4 or "Legacy" in s["id"]:
                continue
            src = []
            for p in pieces:
                for m, c in self.idx.get(p, []):
                    if not HIDDEN_MONSTER.search(m) and c and d.name(m) not in src:
                        src.append(d.name(m))
            if not src:
                continue
            rows.append("<tr><td><strong>%s</strong></td><td>%s</td><td>%s</td></tr>" % (
                esc(s["id"]), esc(nice(pieces, d)), esc(", ".join(src[:4]))))
        return ('<div class="table-wrap"><table><thead><tr><th>Set</th><th>Pieces</th><th>Dropped by</th></tr></thead>'
                '<tbody>%s</tbody></table></div>' % "".join(rows))

    # the pets: their cards have sprites, so they're built with the gear pages (tools/wiki_gear.py)
    def gen_pets(self, used):
        return self.equip.pet_generators(used)

    def gen_pet_summons(self, used):
        return self.equip.pet_summons(used)

    def gen_pet_yard(self, used):
        return self.equip.pet_yard(used)

    # --- classes -----------------------------------------------------------------------------
    def gen_classes(self, used):
        d = self.d
        cards = []
        labels = [("MaxHitPoints", "HP"), ("MaxMagicPoints", "MP"), ("Attack", "Att"), ("Defense", "Def"),
                  ("Speed", "Spd"), ("Dexterity", "Dex"), ("HpRegen", "Vit"), ("MpRegen", "Wis")]
        for doomed in (False, True):
            ident = "doomed-realms-classes" if doomed else "the-classic-classes"
            used.add(ident)
            cards.append('<h2 id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>%s</h2>' % (
                ident, ident, "Doomed Realms classes" if doomed else "The classic classes"))
            cards.append('<div class="classes">')
            for c in d.classes():
                if c["doomed"] != doomed:
                    continue
                cid = "c-" + slugify(c["name"])
                used.add(cid)
                gear = []
                for t in c["slots"]:
                    g = d.gear_items(t)
                    gear.append(esc(g["name"]) if g else "&mdash;")
                start = nice([s for s in c["start"] if s], d)
                maxes = "".join("<li><span>%s</span>%s</li>" % (l, c["stats"][k][1]) for k, l in labels if k in c["stats"])
                desc = esc(c["desc"].split("\n")[0]) if len(c["desc"].strip(" .")) > 2 else ""
                cards.append(
                    '<article class="cls" id="%s"><h3>%s</h3><p class="cls-desc">%s</p>'
                    '<dl><div><dt>Weapon</dt><dd>%s</dd></div><div><dt>Ability</dt><dd>%s</dd></div>'
                    '<div><dt>Armor</dt><dd>%s</dd></div><div><dt>Starts with</dt><dd>%s</dd></div>'
                    '<div><dt>/gear gives</dt><dd>%s</dd></div></dl>'
                    '<p class="maxes-head">Highest stats</p><ul class="maxes">%s</ul>'
                    '<p class="cls-more"><a href="classes/%s">Every weapon, ability, armor and ring it can use</a></p></article>' % (
                        cid, esc(c["name"]), desc, esc(c["weapon"]), esc(c["ability"]), esc(c["armor"]),
                        start or "&mdash;", ", ".join(gear), maxes, self.equip.class_file(c)))
            cards.append("</div>")
        return "\n".join(cards)


# --- Pages ---------------------------------------------------------------------------------------
TEMPLATE = """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{head_title}</title>
  <meta name="description" content="{summary}">
  <meta name="theme-color" content="#0c0915">
  <link rel="icon" href="{site}favicon.svg" type="image/svg+xml">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Silkscreen:wght@400;700&family=Inter:wght@400;500;600;700&display=swap">
  <link rel="stylesheet" href="{site}style.css?v={{{{BUILD}}}}">
  <link rel="stylesheet" href="{wiki}icons.css?v=@INDEX@">
</head>
<body class="sub-page wiki" data-wiki="{wiki}">
  <header class="bar">
    <a class="logo" href="{site}"><i class="sprite" data-sprite="crown" data-px="2"></i><span>{{{{NAME}}}}</span></a>
    <nav class="links">
      <a href="{site}#descent">The descent</a>
      <a href="{site}#get">Get the game</a>
      <a href="{home}" aria-current="page">Wiki</a>
      <a href="{site}changelog.html">Changelog</a>
    </nav>
    <a class="bar-link" href="{home}" aria-current="page">Wiki</a>
    <a class="pill-play" href="{site}play/">Play now</a>
  </header>

  <div class="wiki-layout">
    <aside class="wiki-side">
      <div class="wiki-search" role="search">
        <label class="sr-only" for="wiki-q">Search the wiki</label>
        <input id="wiki-q" type="search" placeholder="Search the wiki" autocomplete="off" spellcheck="false">
        <ol class="wiki-results" id="wiki-results" hidden></ol>
      </div>
      <details class="wiki-nav" open>
        <summary>Wiki pages</summary>
        <nav aria-label="Wiki pages"><ul>{nav}</ul></nav>
      </details>
    </aside>

    <main class="wiki-main">
      <header class="wiki-head">
        <p class="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        <p class="sub">{summary}</p>
      </header>
      {toc}
      <article class="prose">
{body}
      </article>
      <nav class="wiki-pager">{pager}</nav>
    </main>
  </div>

  <footer class="foot">
    <p><span class="mono">{{{{NAME}}}} {{{{VERSION}}}}</span> · <a href="{site}play/">Play</a> · <a href="{home}">Wiki</a> · <a href="{site}changelog.html">Changelog</a></p>
    <p class="dim">Built from the game's own data on every update. Found a mistake? <a href="https://github.com/Meapy/Do-Damage/issues">Tell us on GitHub</a>.</p>
    <p class="dim">A fan-made private server, not affiliated with DECA Games or Kabam. Realm of the Mad God is a trademark of its owners.</p>
  </footer>

  <script src="{site}site.js?v={{{{BUILD}}}}"></script>
  <script src="{wiki}search-index.js?v=@INDEX@"></script>
  <script src="{wiki}wiki.js?v={{{{BUILD}}}}"></script>
</body>
</html>
"""


def plain(fragment):
    text = re.sub(r"<(script|style)[^>]*>.*?</\1>", " ", fragment, flags=re.S)
    text = re.sub(r"<[^>]+>", " ", text)
    return re.sub(r"\s+", " ", html.unescape(text)).strip()


def sections(page_title, url, body):
    """Search entries: one per h2 and h3, with its text."""
    out = []
    parts = re.split(r'(<h[23] id="[^"]+">.*?</h[23]>)', body, flags=re.S)
    head, anchor = page_title, ""
    out.append({"p": page_title, "h": page_title, "u": url, "t": plain(parts[0])[:900]})
    for part in parts[1:]:
        m = re.match(r'<h[23] id="([^"]+)">(.*?)</h[23]>', part, re.S)
        if m:
            anchor, head = m.group(1), plain(m.group(2)).lstrip("#").strip()
            continue
        # a dungeon card or a class card makes its own entry
        cards = re.findall(r'<article class="(?:dg|cls|it)" id="([^"]+)".*?<h3>(.*?)</h3>(.*?)</article>', part, re.S)
        # a pet yard card has no heading: its name is its first span
        cards += [(cid, name, rest) for cid, name, rest in re.findall(
            r'<article class="pet" id="([^"]+)".*?<span class="kind-name">(.*?)</span>(.*?)</article>', part, re.S)]
        for cid, name, rest in cards:
            out.append({"p": page_title, "h": plain(name), "u": url + "#" + cid, "t": plain(rest)[:500]})
        if cards:
            part = re.sub(r'<article class="(?:dg|cls|it|pet)".*?</article>', " ", part, flags=re.S)
        out.append({"p": page_title, "h": head, "u": url + "#" + anchor, "t": plain(part)[:900]})
    return [e for e in out if e["t"] or e["h"]]


def build(out_root, values=None):
    """Writes <out_root>/wiki. `values` fills {{NAME}} and the like as build_site.py does."""
    values = values or {}
    out = os.path.join(out_root, "wiki")
    os.makedirs(out, exist_ok=True)
    gen = Gen()
    v = {k: str(x) for k, x in gen.values().items()}

    pages = []
    for name in sorted(os.listdir(SRC)):
        path = os.path.join(SRC, name)
        if name.endswith(".md"):
            with open(path, encoding="utf-8") as f:
                meta, text = front_matter(f.read())
            slug = name[:-3]
            pages.append((int(meta.get("nav", 99)), slug, meta, text))
        elif os.path.isfile(path):
            shutil.copy2(path, os.path.join(out, name))
    pages.sort()

    def fill(text):
        def val(m):
            if m.group(1) not in v:
                raise SystemExit("wiki: no value for {{v:%s}}" % m.group(1))
            return v[m.group(1)]
        return re.sub(r"\{\{v:(\w+)\}\}", val, text)

    def render(url, current, title, summary, body, pager, eyebrow, toc=""):
        """A page at wiki/<url>; `current` is the guide it's marked under in the page list."""
        up = "../" * url.count("/")
        def item(s2, m2, label=None):
            u2 = up + ("./" if s2 == "index" else s2 + ".html")
            cur = ' aria-current="page"' if s2 == current else ""
            return '<a href="%s"%s><i class="sprite" data-sprite="%s" data-px="2"></i>%s</a>' % (
                u2, cur, esc(m2.get("sprite", "portal")), esc(label or m2.get("title", s2)))

        # A page with `group:` heads a group named that, and opens on itself; the pages with
        # `parent: <it>` are its sub-pages, listed under it while one of the group is showing.
        nav = []
        for _, s2, m2, _ in pages:
            if m2.get("parent"):
                continue
            kids = [(s3, m3) for _, s3, m3, _ in pages if m3.get("parent") == s2]
            if not kids:
                nav.append("<li>%s</li>" % item(s2, m2))
                continue
            here = current == s2 or any(s3 == current for s3, _ in kids)
            sub = "".join("<li>%s</li>" % item(s3, m3) for s3, m3 in kids) if here else ""
            nav.append('<li class="group%s">%s%s</li>' % (" open" if here else "", item(s2, m2, m2.get("group")),
                                                          '<ul class="sub">%s</ul>' % sub if sub else ""))
        head_title = "{{NAME}} wiki" if url == "index.html" else title + ": {{NAME}} wiki"
        page = TEMPLATE.format(head_title=head_title, title=title, summary=esc(summary), nav="".join(nav), toc=toc,
                               body=body, pager=pager, eyebrow=eyebrow, site=up + "../", wiki=up, home=up or "./")
        return re.sub(r"\{\{(\w+)\}\}", lambda m: values.get(m.group(1), m.group(0)), page)

    index = []
    written = []
    for n, (order, slug, meta, text) in enumerate(pages):
        url = "index.html" if slug == "index" else slug + ".html"
        body = markdown(fill(text), gen)
        title = fill(meta.get("title", slug))
        summary = fill(meta.get("summary", ""))
        heads = re.findall(r'<h2 id="([^"]+)"><a[^>]*>#</a>(.*?)</h2>', body)
        toc = ""
        if len(heads) > 2:
            toc = '<nav class="toc" aria-label="On this page"><p>On this page</p><ul>%s</ul></nav>' % "".join(
                '<li><a href="#%s">%s</a></li>' % (h, t) for h, t in heads)
        prev_p = pages[n - 1] if n > 0 else None
        next_p = pages[n + 1] if n + 1 < len(pages) else None
        pager = ""
        if prev_p:
            pager += '<a class="prev" href="%s"><span>Previous</span>%s</a>' % (
                "./" if prev_p[1] == "index" else prev_p[1] + ".html", esc(prev_p[2].get("title", "")))
        if next_p:
            pager += '<a class="next" href="%s"><span>Next</span>%s</a>' % (next_p[1] + ".html", esc(next_p[2].get("title", "")))
        written.append((url, render(url, slug, esc(title), summary, body, pager, esc(meta.get("eyebrow", "Wiki")), toc)))
        index.extend(sections(title, url, body))

    # the gear pages: one per kind of item and one per class (tools/wiki_gear.py)
    for url, current, title, eyebrow, summary, body, pager, entries in gen.equip.pages():
        os.makedirs(os.path.dirname(os.path.join(out, url)), exist_ok=True)
        written.append((url, render(url, current, esc(title), summary, body, pager, eyebrow)))
        index.extend(entries)
    icons = gen.equip.write_assets(out)

    # the index changes with the game's data even when website/ doesn't, so it has its own ?v=
    data = json.dumps(index, ensure_ascii=False, separators=(",", ":"))
    stamp = hashlib.sha1((data + icons).encode("utf-8")).hexdigest()[:10]
    with open(os.path.join(out, "search-index.js"), "w", encoding="utf-8", newline="\n") as f:
        f.write("// The wiki's search index, written by tools/build_wiki.py\nwindow.WIKI_INDEX = %s;\n" % data)
    for url, page in written:
        with open(os.path.join(out, url.replace("/", os.sep)), "w", encoding="utf-8", newline="\n") as f:
            f.write(page.replace("@INDEX@", stamp))
    print("wiki: %d pages, %d dungeons, %d gear items on %d gear pages, %d search entries" % (
        len(pages), len(gen.dungeons()), len(gen.equip.items), len(written) - len(pages), len(index)))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=os.path.join(ROOT, "build", "site"))
    args = ap.parse_args()
    build(os.path.abspath(args.out), {"NAME": "Do Damage", "BUILD": "dev", "VERSION": ""})


if __name__ == "__main__":
    main()
