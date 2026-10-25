"""The wiki's Quests page tables (website/wiki/quests.md): the quest pools, the bonuses, the daily
reward track, the achievements, their tiers and the ranks, all read from the game's own data
(source/Shared/resources/data/quests.json, which the world server's QuestTracker reads), so the page
stays true when the rewards are tuned. build_wiki.py calls it for {{gen:quests_*}} and
{{v:quests_*}}.
"""
import html
import json
import os

import wiki_data as wd

PATH = os.path.join(wd.ROOT, "source", "Shared", "resources", "data", "quests.json")
PERIODS = ["daily", "weekly", "monthly"]
TIER_COLORS = ["#CD8A4E", "#C9D2E3", "#FFC65C", "#FF7A3D"]


def esc(s):
    return html.escape(str(s), quote=True)


def fill(text, n):
    """A text for a goal, as QuestData.Fill writes it: {n}, and the plural endings {s} and {es}."""
    return (text or "").replace("{n}", "{:,}".format(n)).replace("{s}", "" if n == 1 else "s").replace("{es}", "" if n == 1 else "es")


def load():
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)


class Quests:
    def __init__(self, gen):
        self.gen = gen
        self.q = load()

    def reward(self, r):
        if r.get("item"):
            return self.gen.forge_item(r["item"], r.get("count", 1))
        return '<span class="q-fame"><b>{:,}</b> fame</span>'.format(r.get("fame", 0))

    def rewards(self, rs):
        return '<span class="q-rewards">%s</span>' % " ".join(self.reward(r) for r in rs) if rs else "-"

    def pool(self, period):
        rows = []
        for q in self.q["quests"]:
            if q.get("period", "daily") != period:
                continue
            note = ' <span class="dim">(drawn less often)</span>' if q.get("weight", 1) < 1 else ""
            rows.append("<tr><td><b>%s</b>%s</td><td>%s</td><td>%s</td></tr>"
                        % (esc(q["title"]), note, esc(fill(q["text"], q["goal"])), self.rewards(q.get("rewards", []))))
        return ('<div class="table-wrap"><table><thead><tr><th>Quest</th><th>What to do</th><th>Reward</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def bonuses(self):
        rows = []
        for p in PERIODS:
            rows.append("<tr><td><b>%s bonus</b></td><td>Claim all %d %s quests</td><td>%s</td></tr>"
                        % (p.capitalize(), self.q["slots"].get(p, 3), p, self.rewards(self.q["bonus"].get(p, []))))
        return ('<div class="table-wrap"><table><thead><tr><th>Bonus</th><th>How</th><th>Reward</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def login(self):
        days = self.q["login"]
        milestones = set(self.q.get("loginMilestones", []))
        rows = []
        for week in range(0, len(days), 7):
            cells = []
            for d in range(week, min(week + 7, len(days))):
                star = " &#9733;" if d + 1 in milestones else ""
                cls = ' class="q-milestone"' if d + 1 in milestones else ""
                cells.append("<td%s><span class=\"dim\">Day %d%s</span><br>%s</td>" % (cls, d + 1, star, self.rewards(days[d])))
            rows.append("<tr>%s</tr>" % "".join(cells))
        return '<div class="table-wrap"><table class="q-track"><tbody>%s</tbody></table></div>' % "".join(rows)

    def achievements(self):
        names = self.q.get("tierNames", ["Bronze", "Silver", "Gold", "Ember"])
        out = []
        cats = []
        for a in self.q["achievements"]:
            if a["category"] not in cats:
                cats.append(a["category"])
        for cat in cats:
            rows = []
            for a in [a for a in self.q["achievements"] if a["category"] == cat]:
                goals = " / ".join('<span style="color:%s">%s</span>' % (TIER_COLORS[i % 4], "{:,}".format(g)) for i, g in enumerate(a["tiers"]))
                rows.append("<tr><td><b>%s</b></td><td>%s</td><td>%s</td></tr>"
                            % (esc(a["title"]), esc(a["text"].replace("{n}", "N").replace("{s}", "s").replace("{es}", "es")), goals))
            out.append('<h3 id="ach-%s">%s</h3><div class="table-wrap"><table><thead><tr><th>Achievement</th><th>What counts</th>'
                       "<th>%s</th></tr></thead><tbody>%s</tbody></table></div>" % (esc(cat.lower()), esc(cat), " / ".join(names), "".join(rows)))
        return "".join(out)

    def tiers(self):
        names = self.q.get("tierNames", [])
        rows = []
        for i, pts in enumerate(self.q["tierPoints"]):
            rewards = self.q["tierRewards"][min(i, len(self.q["tierRewards"]) - 1)]
            rows.append('<tr><td><b style="color:%s">%s</b></td><td>%d points</td><td>%s</td></tr>'
                        % (TIER_COLORS[i % 4], esc(names[i] if i < len(names) else "Tier %d" % (i + 1)), pts, self.rewards(rewards)))
        return ('<div class="table-wrap"><table><thead><tr><th>Tier</th><th>Points</th><th>Reward</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def ranks(self):
        rows = []
        for r in self.q["ranks"]:
            color = r.get("color")
            swatch = ('<span class="q-swatch" style="background:#%s"></span> <span style="color:#%s">%s</span>'
                      % (color[2:], color[2:], esc(r["name"]))) if color else '<span class="dim">your usual colour</span>'
            rows.append("<tr><td><b>%s</b></td><td>%s</td><td>%s</td></tr>" % (esc(r["name"]), "{:,}".format(r["points"]), swatch))
        return ('<div class="table-wrap"><table><thead><tr><th>Rank</th><th>Points</th><th>Chat name colour</th></tr></thead>'
                "<tbody>%s</tbody></table></div>" % "".join(rows))

    def values(self):
        q = self.q
        login_fame = sum(r.get("fame", 0) for day in q["login"] for r in day)
        tiers = len(q["tierPoints"])
        return {
            "quests_daily": q["slots"].get("daily", 3), "quests_weekly": q["slots"].get("weekly", 3),
            "quests_monthly": q["slots"].get("monthly", 3), "quests_swaps": q["rerolls"].get("daily", 1),
            "quests_login_days": len(q["login"]), "quests_login_fame": "{:,}".format(login_fame),
            "quests_achievements": len(q["achievements"]), "quests_tiers": tiers,
            "quests_max_points": "{:,}".format(len(q["achievements"]) * sum(q["tierPoints"])),
            "quests_pool": len(q["quests"]),
        }
