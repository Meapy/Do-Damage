"""The wiki's Realm Pass page tables (website/wiki/realm-pass.md): the seasons, every tier's free and
Gilded rewards, what earns pass XP, the Wayfarer's Cache, the auras, name colours and pet effects, read from
the game's own data (source/Shared/resources/data/pass.json, which the world server's PassTracker
reads), so the page stays true when the pass is tuned. build_wiki.py calls it for {{gen:pass_*}}
and {{v:pass_*}}.
"""
import json
import os

import wiki_data as wd
from wiki_quests import esc

PATH = os.path.join(wd.ROOT, "source", "Shared", "resources", "data", "pass.json")
GILD = "#FFD24A"

# what earns pass XP: its key in pass.json's "xp", and how the page says it
SOURCES = [
    ("login", "The daily reward, collected"),
    ("questDaily", "A daily quest claimed"),
    ("questWeekly", "A weekly quest claimed"),
    ("questMonthly", "A monthly quest claimed"),
    ("bonusDaily", "The daily bonus (all daily quests) claimed"),
    ("bonusWeekly", "The weekly bonus claimed"),
    ("bonusMonthly", "The monthly bonus claimed"),
    ("firstDungeon", "Your first dungeon of the day (UTC), on top of the dungeon's own"),
    ("dungeon", "A dungeon cleared"),
    ("elder", "An Elder or Empowered boss defeated"),
    ("boss", "A boss (a realm event, a dungeon's) defeated"),
    ("oryx", "Oryx defeated"),
    ("realmClosed", "A realm closed with you in it"),
    ("arenaWave", "An Arena wave survived"),
]


# what each aura looks like (the Rust client draws them, client-rs/app/src/auras.rs), by its id
AURA_TEXT = {
    1: "Snow drifts down around you, and your steps leave frost on the ground",
    2: "A ring of ice crystals turns at your feet under falling snow",
    3: "Embers rise from you and glow in your footsteps",
    4: "A slow-beating ring of fire, with sparks thrown off",
    5: "Twinkling stars gather around you and trail behind",
    6: "A turning constellation of joined stars beneath you",
}

# what each pet effect looks like (client-rs/app/src/petfx.rs), by its id
PET_FX_TEXT = {
    1: "Frosty paw prints behind your pet, and snowflakes drifting round it",
    2: "Five ice shards wheeling over your pet's head, a ring of frost beneath it",
    3: "Embers rising off your pet over a warm glow",
    4: "Two wings of fire beating behind your pet, shaking off sparks",
    5: "Stars twinkling in and out round your pet",
    6: "Three little moons circling your pet",
    7: "A rainbow trail behind your pet, and sparkles of every colour",
}


def load():
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)


class Pass:
    def __init__(self, gen):
        self.gen = gen
        self.p = load()

    def cosmetic(self, kind, cid):
        for c in self.p.get(kind, []):
            if c["id"] == cid:
                return c
        return None

    def reward(self, r):
        if r.get("item"):
            return self.gen.forge_item(r["item"], r.get("count", 1))
        if r.get("fame"):
            return '<span class="q-fame"><b>{:,}</b> fame</span>'.format(r["fame"])
        if r.get("gold"):
            return '<span class="q-fame" style="color:%s"><b>{:,}</b> gold</span>'.format(r["gold"]) % GILD
        if r.get("skin"):
            return '<span class="q-fame" style="color:#8FD0FF"><b>%s</b> skin</span>' % esc(r["skin"])
        if r.get("aura"):
            a = self.cosmetic("auras", r["aura"]) or {}
            return '<span class="q-fame" style="color:%s"><b>%s</b> aura</span>' % (self.color(a), esc(a.get("name", "?")))
        if r.get("color"):
            c = self.cosmetic("colors", r["color"]) or {}
            return '<span class="q-fame" style="color:%s"><b>%s</b> name colour</span>' % (self.color(c), esc(c.get("name", "?")))
        if r.get("petFx"):
            c = self.cosmetic("petEffects", r["petFx"]) or {}
            return '<span class="q-fame" style="color:%s"><b>%s</b> pet effect</span>' % (self.color(c), esc(c.get("name", "?")))
        if r.get("slot"):
            n = r["slot"]
            return '<span class="q-fame"><b>%d</b> character slot%s</span>' % (n, "" if n == 1 else "s")
        return "-"

    @staticmethod
    def color(c):
        v = (c.get("color") or "").replace("0x", "#")
        return v if v.startswith("#") else "#F1ECFF"

    def rewards(self, rs):
        return '<span class="q-rewards">%s</span>' % " ".join(self.reward(r) for r in rs) if rs else '<span class="dim">-</span>'

    def seasons(self):
        """The dated seasons, then what runs in the months after them."""
        rows = []
        for s in self.p.get("seasons", []):
            if not s.get("from"):
                continue
            rows.append('<tr><td><b style="color:%s">%s</b></td><td>%s to %s</td></tr>'
                        % (s.get("accent", "#FFC65C").replace("0x", "#"), esc(s["name"]), esc(s["from"]), esc(s["until"])))
        rot = ", ".join(esc(next((s["name"] for s in self.p["seasons"] if s["id"] == r), r)) for r in self.p.get("rotation", []))
        if rot:
            rows.append('<tr><td><b>After them</b></td><td>A season a calendar month, in turn: %s (each a new round, II, III...)</td></tr>' % rot)
        return ('<div class="table-wrap"><table><thead><tr><th>Season</th><th>Runs (UTC)</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def tracks(self):
        """Every season's tiers: the free reward and the Gilded one."""
        out = []
        for s in self.p.get("seasons", []):
            free, gilded = s.get("free", []), s.get("gilded", [])
            rows = []
            for t in range(self.p.get("tiers", 30)):
                f = free[t] if t < len(free) else []
                g = gilded[t] if t < len(gilded) else []
                star = any(r.get("skin") or r.get("aura") or r.get("petFx") for r in f + g)
                rows.append('<tr%s><td><b>%d</b></td><td>%s</td><td>%s</td></tr>'
                            % (' class="q-ready"' if star else "", t + 1, self.rewards(f), self.rewards(g)))
            out.append('<h3 id="track-%s">%s</h3>\n<div class="table-wrap"><table><thead><tr><th>Tier</th><th>Free</th>'
                       '<th style="color:%s">Gilded</th></tr></thead><tbody>%s</tbody></table></div>'
                       % (esc(s["id"]), esc(s["name"]), GILD, "".join(rows)))
        return "\n\n".join(out)

    def xp(self):
        x = self.p.get("xp", {})
        rows = []
        for key, what in SOURCES:
            v = x.get(key, 0)
            if not v:
                continue
            amount = "+{:,}".format(v)
            if key == "dungeon" and x.get("dungeonStep"):
                amount += ', +%d more for each step of difficulty' % x["dungeonStep"]
            rows.append("<tr><td>%s</td><td><b>%s</b></td></tr>" % (esc(what), amount))
        if x.get("famePer"):
            rows.append("<tr><td>Fame earned (by any character)</td><td><b>1 for every %d fame</b></td></tr>" % x["famePer"])
        return ('<div class="table-wrap"><table><thead><tr><th>What you do</th><th>Pass XP</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def cache(self):
        lines = self.p.get("cache", [])
        total = sum(l.get("weight", 1) for l in lines) or 1
        rows = []
        for l in sorted(lines, key=lambda l: -l.get("weight", 1)):
            pct = l.get("weight", 1) / total * 100
            rows.append("<tr><td>%s</td><td>%s</td></tr>" % (self.rewards(l.get("rewards", [])), ("%.1f" % pct).rstrip("0").rstrip(".") + "%"))
        return ('<div class="table-wrap"><table><thead><tr><th>A cache holds</th><th>Chance</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def looks(self):
        rows = []
        for a in self.p.get("auras", []):
            rows.append('<tr><td><b style="color:%s">%s</b></td><td>Aura</td><td>%s</td></tr>' % (self.color(a), esc(a["name"]), esc(AURA_TEXT.get(a["id"], ""))))
        for c in self.p.get("colors", []):
            rows.append('<tr><td><b style="color:%s">%s</b></td><td>Name colour</td><td>Your name in chat</td></tr>' % (self.color(c), esc(c["name"])))
        for c in self.p.get("petEffects", []):
            rows.append('<tr><td><b style="color:%s">%s</b></td><td>Pet effect</td><td>%s</td></tr>' % (self.color(c), esc(c["name"]), esc(PET_FX_TEXT.get(c["id"], ""))))
        return ('<div class="table-wrap"><table><thead><tr><th>Look</th><th>Kind</th><th>What it is</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def values(self):
        p = self.p
        com = p.get("community", {})
        return {
            "pass_price": "{:,}".format(p.get("price", 1000)), "pass_tiers": p.get("tiers", 30),
            "pass_tier_xp": "{:,}".format(p.get("xpPerTier", 1000)), "pass_cache_xp": "{:,}".format(p.get("bonusXp", 1500)),
            "pass_pace": round(p.get("pace", 0.75) * 100), "pass_momentum": p.get("momentum", 50),
            "pass_repeat_fame": "{:,}".format(p.get("repeatFame", 250)),
            "pass_luck": com.get("luck", 1), "pass_rolls": com.get("rolls", 1),
            "pass_rally_mult": com.get("rallyMultiplier", 2),
            "pass_auras": len(p.get("auras", [])), "pass_colors": len(p.get("colors", [])),
            "pass_pet_effects": len(p.get("petEffects", [])),
        }
