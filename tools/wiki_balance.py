"""The wiki's Gear statistics page (website/wiki/gear-statistics.md), built with the rest by
tools/build_wiki.py from the game's data:

- {{gen:tier-tables}}: every tiered item, one table per kind, lowest tier first, with what changes
  from the tier below (damage, output, MP, defense, stat points).
- {{gen:class-dps}}: each class's damage a second with each tier of its weapon and the same tier of
  its armor, at its highest Attack and Dexterity, against a target with 50 defense.
- {{gen:class-ehp}}: each class's effective health at its highest health and defense, with each
  tier of its armor and nothing else.

The formulas are the world server's (StatsManager): a shot's damage is its roll (MinDamage up to
MaxDamage - 1, ClientRandom.NextIntRange) times 0.5 + 1.5 * Attack / 75; shots a second are
(0.0015 + 0.0065 * Dexterity / 75) * 1000 * RateOfFire; a monster takes the damage less its defense
but at least a tenth (DamageWithDefense), a player at least 15% (GetDefenseDamage).
"""
import wiki_data as wd

ATT, DEF, SPD, DEX, HP = 20, 21, 22, 28, 0
TARGET_DEF = 50
HITS = (100, 200)
TIERS = range(0, 15)        # weapons: T0-T14
ARMOR_TIERS = range(0, 16)  # armor: T0-T15 (T16 and up are Doomed Realms pieces that don't drop)
STAT_ORDER = ["HP", "MP", "Att", "Def", "Spd", "Vit", "Wis", "Dex"]
STAT = {0: "HP", 3: "MP", 20: "Att", 21: "Def", 22: "Spd", 26: "Vit", 27: "Wis", 28: "Dex"}


def stats(it):
    out = {}
    for a in it["el"].findall("ActivateOnEquip"):
        if (a.text or "").strip() == "IncrementStat":
            s = wd._int(a.get("stat"), -1)
            out[s] = out.get(s, 0) + wd._int(a.get("amount"), 0)
    return out


def weapon(it):
    """(min, max, shots, rate of fire, range, ignores defense) of an item that shoots, or None."""
    el = it["el"]
    p = el.find("Projectile")
    if p is None:
        return None
    lo = wd._int(wd._text(p, "MinDamage") or wd._text(p, "Damage"), 0)
    hi = wd._int(wd._text(p, "MaxDamage") or wd._text(p, "Damage"), lo)
    speed, life = float(wd._text(p, "Speed") or 0), float(wd._text(p, "LifetimeMS") or 0)
    return (lo, hi, wd._int(wd._text(el, "NumProjectiles"), 1), float(wd._text(el, "RateOfFire") or 1), speed * life / 10000,
            p.find("ArmorPiercing") is not None)


def attack_mult(att):
    return 0.5 + att / 75 * 1.5


def shots_a_second(dex, rof):
    return (0.0015 + dex / 75 * 0.0065) * 1000 * rof


def dps(lo, hi, shots, rof, att, dex, defense=TARGET_DEF):
    """Damage a second against `defense`, every shot hitting: the mean over the server's rolls."""
    mult = attack_mult(att)
    rolls = [lo] if hi <= lo else range(lo, hi)
    per = sum(int(max(int(r * mult) * 0.1, int(r * mult) - defense)) for r in rolls) / len(rolls)
    return per * shots * shots_a_second(dex, rof)


def taken(hit, defense):
    return max(hit * 0.15, hit - defense)


class Balance:
    def __init__(self, gen):
        self.g, self.h = gen, gen.equip.h
        self.d = gen.d
        self.tiered = [it for it in gen.equip.items if it["group"] == "tiered" and it["tier"] >= 0]
        self.classes = [c for c in self.d.classes() if c["slots"] and c["slots"][0]]

    # --- helpers ---------------------------------------------------------------------------------
    def esc(self, s):
        return self.h.esc(s)

    def link(self, it):
        href = self.g.equip.href.get(it["id"])
        return '<a href="%s">%s</a>' % (href, self.esc(it["name"])) if href else self.esc(it["name"])

    def by_tier(self, slot):
        """The tiered item of each tier for a slot type: the one that drops with the tier drops, else
        the first (a joke or boss item can share a tier with the real one)."""
        out = {}
        for it in sorted(self.tiered, key=lambda i: (i["tier_loot"] is None, i["type"])):
            if it["slot"] == slot:
                out.setdefault(it["tier"], it)
        return out

    @staticmethod
    def deltas(rows, f):
        """Each row's change from the nearest lower tier (the mean of that tier's values)."""
        by = {}
        for r in rows:
            v = f(r)
            if v is not None:
                by.setdefault(r["tier"], []).append(v)
        tiers = sorted(by)
        out = []
        for r in rows:
            v = f(r)
            i = tiers.index(r["tier"]) if r["tier"] in by else -1
            if v is None or i <= 0:
                out.append(None)
                continue
            prev = sum(by[tiers[i - 1]]) / len(by[tiers[i - 1]])
            out.append((v - prev, (v - prev) / prev * 100 if prev else None))
        return out

    @staticmethod
    def fmt(v):
        return ("%.2f" % v).rstrip("0").rstrip(".") if isinstance(v, float) else str(v)

    def delta(self, x, pct=False):
        if not x:
            return '<span class="dim">&ndash;</span>'
        d, p = x
        cls = "up" if d > 0.005 else "down" if d < -0.005 else "dim"
        s = ("+" if d > 0 else "&minus;" if d < 0 else "&plusmn;") + self.fmt(round(abs(d), 2))
        if pct and p is not None:
            s += ' <span class="dim">(%s%d%%)</span>' % ("+" if p >= 0 else "&minus;", abs(round(p)))
        return '<span class="%s">%s</span>' % (cls, s)

    def chips(self, it):
        s = stats(it)
        text = ", ".join("%+d %s" % (s[k], STAT[k]) for k in sorted(s, key=lambda k: STAT_ORDER.index(STAT.get(k, "HP")))
                         if k in STAT and s[k])
        return self.esc(text) if text else '<span class="dim">&ndash;</span>'

    def table(self, head, rows, cls="balance"):
        th = "".join('<th%s>%s</th>' % (' class="n"' if n else "", t) for t, n in head)
        return ('<div class="table-wrap wide"><table class="%s"><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>'
                % (cls, th, "".join(rows)))

    def heading(self, used, level, text, ident):
        used.add(ident)
        return '<h%d id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>%s</h%d>' % (
            level, ident, ident, self.esc(text), level)

    # --- tier tables -----------------------------------------------------------------------------
    def tier_tables(self, used):
        out = []
        for cat, title, _, _ in [("weapons", "Weapons", 0, 0), ("abilities", "Abilities", 0, 0),
                                 ("armor", "Armor", 0, 0), ("rings", "Rings", 0, 0)]:
            items = [it for it in self.tiered if it["category"] == cat]
            if not items:
                continue
            out.append(self.heading(used, 3, title, "tiers-" + cat))
            for slot in sorted({it["slot"] for it in items}):
                rows = sorted([it for it in items if it["slot"] == slot], key=lambda i: (i["tier"], i["type"]))
                worn = ", ".join(c["name"] for c in self.d.classes() if slot in c["slots"]) or "every class"
                out.append('<h4 id="tiers-%s">%s <span class="dim">&middot; %d items &middot; %s</span></h4>' % (
                    slot, self.esc(wd.SLOT_NAMES.get(slot, "Slot %d" % slot)), len(rows), self.esc(worn)))
                used.add("tiers-%d" % slot)
                out.append({"weapons": self.weapon_table, "abilities": self.ability_table}.get(
                    cat, lambda r: self.stat_table(r, cat == "armor"))(rows))
        return "\n".join(out)

    def weapon_table(self, rows):
        w = {it["id"]: weapon(it) for it in rows}
        avg = lambda it: (w[it["id"]][0] + w[it["id"]][1]) / 2 if w[it["id"]] else None
        output = lambda it: avg(it) * w[it["id"]][2] * w[it["id"]][3] if w[it["id"]] else None
        da, do = self.deltas(rows, avg), self.deltas(rows, output)
        body = []
        for i, it in enumerate(rows):
            lo, hi, shots, rof, rng, _ = w[it["id"]] or (0, 0, 1, 1, 0, False)
            body.append('<tr><td class="t">%d</td><td>%s</td><td class="n">%s</td><td class="n">%s</td><td class="n">%s</td>'
                        '<td class="n">%d</td><td class="n">%s</td><td class="n">%s</td><td class="n">%s</td><td class="n">%s</td>'
                        '<td>%s</td></tr>' % (
                            it["tier"], self.link(it), "%d&ndash;%d" % (lo, hi) if lo != hi else lo, self.fmt(avg(it) or 0),
                            self.delta(da[i], True), shots, self.fmt(rof), self.fmt(round(rng, 2)), self.fmt(output(it) or 0),
                            self.delta(do[i], True), self.chips(it)))
        return self.table([("T", 0), ("Item", 0), ("Damage", 1), ("Avg", 1), ("Avg +/&minus;", 1), ("Shots", 1),
                           ("Rate", 1), ("Range", 1), ("Output", 1), ("Output +/&minus;", 1), ("Stat bonuses", 0)], body)

    def ability_table(self, rows):
        mp = lambda it: wd._int(wd._text(it["el"], "MpCost"), 0) or None
        dm = self.deltas(rows, mp)
        body = []
        for i, it in enumerate(rows):
            w = weapon(it)
            dmg = ("%d&ndash;%d" % (w[0], w[1]) + (" &times; %d" % w[2] if w[2] > 1 else "")) if w and w[1] else ""
            what = "; ".join(x for x in (dmg, self.esc(self.g.equip.effects(it))) if x)
            body.append('<tr><td class="t">%d</td><td>%s</td><td class="n">%s</td><td class="n">%s</td><td class="wide">%s</td><td>%s</td></tr>'
                        % (it["tier"], self.link(it), mp(it) or "&ndash;", self.delta(dm[i]), what or "&ndash;", self.chips(it)))
        return self.table([("T", 0), ("Item", 0), ("MP", 1), ("MP +/&minus;", 1), ("What it does", 0), ("Stat bonuses", 0)], body)

    def stat_table(self, rows, armor):
        total = lambda it: sum(stats(it).values())
        dd, dt = self.deltas(rows, lambda it: stats(it).get(DEF, 0)), self.deltas(rows, total)
        body = []
        for i, it in enumerate(rows):
            cells = ['<td class="t">%d</td><td>%s</td>' % (it["tier"], self.link(it))]
            if armor:
                cells.append('<td class="n">%d</td><td class="n">%s</td>' % (stats(it).get(DEF, 0), self.delta(dd[i])))
            cells.append('<td>%s</td><td class="n">%d</td><td class="n">%s</td>' % (self.chips(it), total(it), self.delta(dt[i])))
            body.append("<tr>%s</tr>" % "".join(cells))
        head = [("T", 0), ("Item", 0)] + ([("Def", 1), ("Def +/&minus;", 1)] if armor else []) + \
               [("Stat bonuses", 0), ("Points", 1), ("Points +/&minus;", 1)]
        return self.table(head, body)

    # --- classes ---------------------------------------------------------------------------------
    def class_dps(self, used):
        head = [("Class", 0), ("Weapon", 0), ("Att", 1), ("Dex", 1)] + [("T%d" % t, 1) for t in TIERS]
        body = []
        for c in self.classes:
            slot = c["slots"][0]
            att, dex = c["stats"].get("Attack", (0, 0))[1], c["stats"].get("Dexterity", (0, 0))[1]
            items = self.by_tier(slot)
            # with the armor of the same tier: its Attack and Dexterity count too
            armors = self.by_tier(c["slots"][2]) if len(c["slots"]) > 2 else {}
            cells = []
            for t in TIERS:
                it = items.get(t)
                w = weapon(it) if it else None
                if not w or not w[1]:
                    cells.append('<td class="n dim">&ndash;</td>')
                    continue
                s, arm = stats(it), armors.get(t)
                a = stats(arm) if arm else {}
                v = dps(w[0], w[1], w[2], w[3], att + s.get(ATT, 0) + a.get(ATT, 0), dex + s.get(DEX, 0) + a.get(DEX, 0),
                        0 if w[5] else TARGET_DEF)
                cells.append('<td class="n" title="%s%s">%d</td>' % (
                    self.esc(it["name"]), " + " + self.esc(arm["name"]) if arm else "", round(v)))
            body.append('<tr><td>%s</td><td>%s</td><td class="n">%d</td><td class="n">%d</td>%s</tr>' % (
                self.esc(c["name"]), self.esc(wd.SLOT_NAMES.get(slot, "")), att, dex, "".join(cells)))
        return self.table(head, body, "balance grid")

    def top_gear(self, c):
        """The class's best tiered weapon (up to T14) and armor (up to T15)."""
        weapons = {t: it for t, it in self.by_tier(c["slots"][0]).items() if t in TIERS}
        armors = {t: it for t, it in self.by_tier(c["slots"][2]).items() if t in ARMOR_TIERS} if len(c["slots"]) > 2 else {}
        return (weapons[max(weapons)] if weapons else None), (armors[max(armors)] if armors else None)

    def class_totals(self, used):
        """Each class at the top tier (T14 weapon, T15 armor): damage a second, effective health, both
        per tile of the weapon's range, and how each compares with the best class."""
        hit = HITS[0]
        rows = []
        for c in self.classes:
            it, arm = self.top_gear(c)
            w = weapon(it) if it else None
            if not w or not w[1]:
                continue
            s, a = stats(it), stats(arm) if arm else {}
            att = c["stats"].get("Attack", (0, 0))[1] + s.get(ATT, 0) + a.get(ATT, 0)
            dex = c["stats"].get("Dexterity", (0, 0))[1] + s.get(DEX, 0) + a.get(DEX, 0)
            hp = c["stats"].get("MaxHitPoints", (0, 0))[1] + s.get(HP, 0) + a.get(HP, 0)
            df = c["stats"].get("Defense", (0, 0))[1] + s.get(DEF, 0) + a.get(DEF, 0)
            v = dps(w[0], w[1], w[2], w[3], att, dex, 0 if w[5] else TARGET_DEF)
            e = hp * hit / int(taken(hit, df))
            rows.append(dict(c=c, it=it, arm=arm, rng=w[4], dps=v, ehp=e, dpr=v / w[4], epr=e / w[4]))
        top = {k: max(r[k] for r in rows) for k in ("dps", "ehp", "dpr", "epr")}
        # one number to compare every class by: the square root of DPS x EHP x range, as a share of the
        # best class's. Range counts with diminishing returns (twice the reach is worth about 41%
        # more): of the powers of range tried, the square root leaves the weapon kinds' averages the
        # most even (about 1.2 times between highest and lowest), so what stands out is the class.
        # Strong at one of DPS and EHP and weak at the other scores lower than fair at both.
        for r in rows:
            r["raw"] = (r["dps"] * r["ehp"] * r["rng"]) ** 0.5
        best = max(r["raw"] for r in rows)
        for r in rows:
            r["score"] = 100 * r["raw"] / best
        top["score"] = 100

        def vs(r, k):
            d = (r[k] / top[k] - 1) * 100
            if abs(d) < 0.5:
                return '<td class="n"><b>top</b></td>'
            return '<td class="n"><span class="down">&minus;%d%%</span></td>' % round(-d)

        def cell(r, k, fmt="%d"):
            text = fmt % r[k]
            return '<td class="n">%s</td>' % ("<b>%s</b>" % text if r[k] == top[k] else text)

        head = [("Class", 0), ("Weapon", 0), ("Armor", 0), ("Range", 1), ("DPS", 1), ("vs top", 1), ("EHP", 1),
                ("EHP vs top", 1), ("DPS per tile", 1), ("vs top", 1), ("EHP per tile", 1), ("vs top", 1),
                ("Score", 1), ("vs top", 1)]
        body = []
        for r in rows:
            body.append("<tr><td>%s</td><td>%s</td><td>%s</td><td class=\"n\">%s</td>%s%s%s%s%s%s%s%s%s%s</tr>" % (
                self.esc(r["c"]["name"]), self.link(r["it"]), self.link(r["arm"]) if r["arm"] else "&ndash;",
                self.fmt(round(r["rng"], 2)), cell(r, "dps"), vs(r, "dps"), cell(r, "ehp"), vs(r, "ehp"),
                cell(r, "dpr"), vs(r, "dpr"), cell(r, "epr"), vs(r, "epr"), cell(r, "score"), vs(r, "score")))
        return self.table(head, body, "balance grid")

    def class_ehp(self, used):
        out = []
        tiers = sorted({it["tier"] for it in self.tiered if it["category"] == "armor" and it["tier"] in ARMOR_TIERS})
        for hit in HITS:
            out.append('<p class="table-caption">Against hits of <b>%d</b> damage:</p>' % hit)
            head = [("Class", 0), ("Armor", 0), ("HP", 1), ("Def", 1), ("No armor", 1)] + [("T%d" % t, 1) for t in tiers]
            body = []
            for c in self.classes:
                slot = c["slots"][2] if len(c["slots"]) > 2 else 0
                hp, df = c["stats"].get("MaxHitPoints", (0, 0))[1], c["stats"].get("Defense", (0, 0))[1]
                ehp = lambda h, d: h * hit / int(taken(hit, d))
                items = self.by_tier(slot)
                cells = []
                for t in tiers:
                    it = items.get(t)
                    if not it:
                        cells.append('<td class="n dim">&ndash;</td>')
                        continue
                    s = stats(it)
                    cells.append('<td class="n" title="%s: %+d Def%s">%d</td>' % (
                        self.esc(it["name"]), s.get(DEF, 0), ", %+d HP" % s[HP] if s.get(HP) else "",
                        round(ehp(hp + s.get(HP, 0), df + s.get(DEF, 0)))))
                body.append('<tr><td>%s</td><td>%s</td><td class="n">%d</td><td class="n">%d</td><td class="n">%d</td>%s</tr>' % (
                    self.esc(c["name"]), self.esc(wd.SLOT_NAMES.get(slot, "")), hp, df, round(ehp(hp, df)), "".join(cells)))
            out.append(self.table(head, body, "balance grid"))
        return "\n".join(out)
