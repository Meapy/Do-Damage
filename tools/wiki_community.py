"""The wiki's Community Goals page tables (website/wiki/community-goals.md): the goals that can come
up, their tiers, Overdrive levels, top prizes and loot, read from the game's own data
(source/Shared/resources/data/community.json, which the world server's CommunityGoals reads), so
the page stays true when the goals are tuned. The loot's odds are worked out as
Shared/community/CommunityData.Odds does. build_wiki.py calls it for {{gen:community_*}} and
{{v:community_*}}.
"""
import json
import math
import os

import wiki_data as wd
from wiki_quests import esc, fill

PATH = os.path.join(wd.ROOT, "source", "Shared", "resources", "data", "community.json")
# the tiers' colours in the game's window: Spark, Flame, Blaze, Inferno
TIER_COLORS = ["#FFC65C", "#FFA36B", "#FF7A3D", "#FF3D5A"]
# the loot's rarities' colours in the game's window (as the death spin's)
RARITY_COLORS = ["#B3A9CC", "#5BE3A0", "#4FA3FF", "#B86BFF", "#FFC65C", "#FFFFFF"]
OVERDRIVE_COLOR = "#FFC65C"


def load():
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)


def roman(n):
    out = ""
    for v, r in ((10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")):
        while n >= v:
            out += r
            n -= v
    return out


def chance(c):
    pct = c * 100
    if pct >= 10:
        return "%d%%" % round(pct)
    if pct >= 0.1:
        return ("%.1f" % pct).rstrip("0").rstrip(".") + "%"
    return "&lt;0.1%"


class Community:
    def __init__(self, gen):
        self.gen = gen
        self.c = load()

    def tier_names(self):
        return self.c.get("tierNames", ["Spark", "Flame", "Blaze", "Inferno"])

    def bonus_name(self):
        return self.c.get("bonusName", "Overdrive")

    def bonus_tiers(self):
        return self.c.get("bonusTiers", 8)

    def bonus_step(self):
        return self.c.get("bonusStep", 0.25)

    def rarities(self):
        return self.c.get("rarities", ["common", "uncommon", "rare", "epic", "legendary", "divine"])

    def tier_label(self, t):
        names = self.tier_names()
        name = names[t] if t < len(names) else "Tier %d" % (t + 1)
        return '<b style="color:%s">%s</b>' % (TIER_COLORS[min(t, len(TIER_COLORS) - 1)], esc(name))

    def overdrive_label(self, text=None):
        return '<b style="color:%s">%s</b>' % (OVERDRIVE_COLOR, esc(text or self.bonus_name()))

    def reward(self, r):
        n = r.get("roll", 0)
        if n > 0:
            return ('<span class="q-fame" style="color:%s"><b>%d</b> loot roll%s</span>'
                    % (RARITY_COLORS[3], n, "" if n == 1 else "s"))
        return self.gen.quests.reward(r)

    def rewards(self, rs):
        return '<span class="q-rewards">%s</span>' % " ".join(self.reward(r) for r in rs) if rs else "-"

    def rarity_of(self, e):
        names = [r.lower() for r in self.rarities()]
        r = (e.get("rarity") or "common").lower()
        return names.index(r) if r in names else 0

    def rarity_label(self, i):
        names = self.rarities()
        name = names[i] if i < len(names) else "?"
        return '<b style="color:%s">%s</b>' % (RARITY_COLORS[min(i, len(RARITY_COLORS) - 1)], esc(name.capitalize()))

    def odds(self, g, level):
        """Each loot entry's chance a roll at an Overdrive level (0 for a tier's), as
        CommunityData.Odds: its weight times 1 + lift x level, over the sum."""
        lift = self.c.get("rarityLift", [0, 0.1, 0.25, 0.4, 0.6, 0.8])
        ws = []
        for e in g.get("loot", []):
            r = self.rarity_of(e)
            k = lift[r] if r < len(lift) else (lift[-1] if lift else 0)
            ws.append(max(0, e.get("weight", 1)) * (1 + k * max(0, level)))
        total = sum(ws)
        return [w / total if total else 0 for w in ws]

    def goal_rewards(self, g, key, default):
        rs = g.get(key)
        return rs if rs else self.c.get(default, [])

    def goals(self):
        """Every goal the week can draw: what the server must do, what each player must add to
        share the rewards, each tier's rewards, each Overdrive level's and the top's prize."""
        shares = self.c.get("tierShares", [0.25, 0.5, 0.75, 1])
        rows = []
        for g in self.c["goals"]:
            note = ' <span class="dim">(drawn less often)</span>' if g.get("weight", 1) < 1 else ""
            tiers = g.get("tiers") or self.c.get("defaultTiers", [])
            lines = []
            for t, rs in enumerate(tiers[:len(shares)]):
                at = max(1, math.ceil(g["goal"] * shares[t]))
                lines.append('%s <span class="dim">at %s</span>: %s' % (self.tier_label(t), "{:,}".format(at), self.rewards(rs)))
            lines.append('%s <span class="dim">each level</span>: %s' % (self.overdrive_label(), self.rewards(self.goal_rewards(g, "bonus", "defaultBonus"))))
            lines.append('<b>Top %d</b>: %s' % (self.c.get("topCount", 3), self.rewards(self.goal_rewards(g, "top", "defaultTop"))))
            minimum = g.get("minimum", 1)
            share = "{:,}".format(minimum) if minimum > 1 else "1"
            rows.append('<tr id="goal-%s"><td><b>%s</b>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>'
                        % (esc(g["id"]), esc(g["title"]), note, esc(fill(g["text"], g["goal"])), share, "<br>".join(lines)))
        return ('<div class="table-wrap"><table><thead><tr><th>Goal</th><th>What the realm does (to begin with)</th>'
                "<th>Your share to claim</th><th>Rewards</th></tr></thead><tbody>%s</tbody></table></div>" % "".join(rows))

    def tiers(self):
        """The tiers (where each is reached), the Overdrive levels past the goal, and the top's
        prize."""
        shares = self.c.get("tierShares", [0.25, 0.5, 0.75, 1])
        rows = []
        for t, share in enumerate(shares):
            rows.append("<tr><td>%s</td><td>%d%% of the goal</td><td>Its own rewards, for everyone who helped</td></tr>" % (self.tier_label(t), round(share * 100)))
        step = round(self.bonus_step() * 100)
        last = round((1 + self.bonus_step() * self.bonus_tiers()) * 100)
        rows.append("<tr><td>%s</td><td>Every %d%% more past the goal, %s to %s (%d%%)</td><td>The goal's bonus again, each level, for everyone who helped</td></tr>"
                    % (self.overdrive_label("%s I to %s" % (self.bonus_name(), roman(self.bonus_tiers()))), step,
                       roman(1), roman(self.bonus_tiers()), last))
        rows.append("<tr><td><b>Top %d</b></td><td>The goal's top contributors, once it ends</td><td>The goal's top prize</td></tr>"
                    % self.c.get("topCount", 3))
        return ('<div class="table-wrap"><table><thead><tr><th>Tier</th><th>Reached at</th><th>Gives</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def loot(self):
        """Each goal's loot: what its rolls draw from, rarest first, with the chance a roll at a
        tier and at the highest Overdrive level (where the top's prize rolls)."""
        top = self.bonus_tiers()
        out = []
        for g in self.c["goals"]:
            entries = g.get("loot", [])
            if not entries:
                continue
            base, best = self.odds(g, 0), self.odds(g, top)
            order = sorted(range(len(entries)), key=lambda i: (-self.rarity_of(entries[i]), base[i]))
            rows = []
            for i in order:
                e = entries[i]
                rows.append("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>"
                            % (self.gen.forge_item(e["item"], e.get("count", 1)), self.rarity_label(self.rarity_of(e)), chance(base[i]), chance(best[i])))
            out.append('<h3 id="loot-%s">%s</h3>\n<div class="table-wrap"><table><thead><tr><th>Item</th><th>Rarity</th><th>A roll at a tier</th>'
                       "<th>At %s %s and for the top</th></tr></thead><tbody>%s</tbody></table></div>"
                       % (esc(g["id"]), esc(g["title"]), esc(self.bonus_name()), roman(top), "".join(rows)))
        return "\n\n".join(out)

    def values(self):
        c = self.c
        adapt = c.get("adapt", {})
        return {
            "community_slots": c.get("slots", 3), "community_top": c.get("topCount", 3),
            "community_claim_weeks": c.get("claimWeeks", 1),
            "community_early_hours": "{:g}".format(c.get("early", {}).get("minHours", 6)), "community_goals": len(c["goals"]),
            "community_bonus_name": self.bonus_name(), "community_bonus_levels": self.bonus_tiers(),
            "community_bonus_step": round(self.bonus_step() * 100),
            "community_bonus_max": round((1 + self.bonus_step() * self.bonus_tiers()) * 100),
            "community_bonus_last": roman(self.bonus_tiers()),
            "community_adapt_target": round(adapt.get("target", 1.2) * 100),
            "community_adapt_up": round((adapt.get("maxStep", 1.35) - 1) * 100),
            "community_adapt_down": round((1 - adapt.get("minStep", 0.75)) * 100),
            "community_announce": c.get("announceFrom", "legendary").capitalize(),
        }
