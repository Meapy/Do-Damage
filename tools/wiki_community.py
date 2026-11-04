"""The wiki's Community Goals page tables (website/wiki/community-goals.md): the goals that can come
up, their tiers and the top's prize, read from the game's own data
(source/Shared/resources/data/community.json, which the world server's CommunityGoals reads), so
the page stays true when the goals are tuned. build_wiki.py calls it for {{gen:community_*}} and
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


def load():
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)


class Community:
    def __init__(self, gen):
        self.gen = gen
        self.c = load()

    def tier_names(self):
        return self.c.get("tierNames", ["Spark", "Flame", "Blaze", "Inferno"])

    def tier_label(self, t):
        names = self.tier_names()
        name = names[t] if t < len(names) else "Tier %d" % (t + 1)
        return '<b style="color:%s">%s</b>' % (TIER_COLORS[min(t, len(TIER_COLORS) - 1)], esc(name))

    def rewards(self, rs):
        return self.gen.quests.rewards(rs)

    def goals(self):
        """Every goal the week can draw: what the server must do, what each player must add to
        share the rewards, and the rewards of each tier."""
        shares = self.c.get("tierShares", [0.25, 0.5, 0.75, 1])
        rows = []
        for g in self.c["goals"]:
            note = ' <span class="dim">(drawn less often)</span>' if g.get("weight", 1) < 1 else ""
            tiers = g.get("tiers") or self.c.get("defaultTiers", [])
            lines = []
            for t, rs in enumerate(tiers[:len(shares)]):
                at = max(1, math.ceil(g["goal"] * shares[t]))
                lines.append('%s <span class="dim">at %s</span>: %s' % (self.tier_label(t), "{:,}".format(at), self.rewards(rs)))
            minimum = g.get("minimum", 1)
            share = "{:,}".format(minimum) if minimum > 1 else "1"
            rows.append("<tr><td><b>%s</b>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>"
                        % (esc(g["title"]), note, esc(fill(g["text"], g["goal"])), share, "<br>".join(lines)))
        return ('<div class="table-wrap"><table><thead><tr><th>Goal</th><th>What the realm does</th>'
                "<th>Your share to claim</th><th>Tiers and rewards</th></tr></thead><tbody>%s</tbody></table></div>" % "".join(rows))

    def tiers(self):
        """The tiers (where each is reached), what a goal gives unless it has its own, and the top's
        prize."""
        shares = self.c.get("tierShares", [0.25, 0.5, 0.75, 1])
        defaults = self.c.get("defaultTiers", [])
        rows = []
        for t, share in enumerate(shares):
            rs = defaults[t] if t < len(defaults) else []
            rows.append("<tr><td>%s</td><td>%d%% of the goal</td><td>%s</td></tr>" % (self.tier_label(t), round(share * 100), self.rewards(rs)))
        rows.append("<tr><td><b>Top %d</b></td><td>The goal's top contributors, once it ends</td><td>%s</td></tr>"
                    % (self.c.get("topCount", 3), self.rewards(self.c.get("defaultTop", []))))
        return ('<div class="table-wrap"><table><thead><tr><th>Tier</th><th>Reached at</th><th>Usual reward</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def values(self):
        c = self.c
        fame = sum(r.get("fame", 0) for tier in c.get("defaultTiers", []) for r in tier)
        return {
            "community_slots": c.get("slots", 3), "community_top": c.get("topCount", 3),
            "community_claim_weeks": c.get("claimWeeks", 1), "community_goals": len(c["goals"]),
            "community_tier_fame": "{:,}".format(fame),
        }
