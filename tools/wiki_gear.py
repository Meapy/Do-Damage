"""The wiki's gear pages, which tools/build_wiki.py builds with the rest: every weapon, ability, armor
and ring in the game's XML, with its sprite, its numbers and where it comes from.

- wiki/gear/<kind>.html: one page per kind of item (sword, spell, robe...; the rings are
  gear/rings.html), its items grouped as the client colours their slots: tiered (by tier),
  untiered (UT), future additions (untiered items with no source yet), set pieces (ST), Legendary,
  Ascended, Ethereal, rewards, admin and test items. Tabs on top lead to the four categories and the
  kinds in the one shown.
- wiki/gear/weapons.html, abilities.html and armor.html: a category's kinds.
- wiki/classes/<class>.html: one page per class, a tab each for the weapons, abilities, armor and
  rings it can use (#weapon, #ability, #armor, #ring).
- wiki/icons.css and wiki/sheets/: the client's own sprite sheets (client/src, through the Rust
  client's manifest) and a class per sheet, so each icon is a crop of its sheet.
- The landing page is the guide website/wiki/gear.md, whose {{gen:gear}} is `overview()`.
- The Pets guide (website/wiki/pets.md) takes its lists from here too, for the sprites:
  `pet_generators()`, `pet_summons()` and `pet_yard()`.

A category is where the classes wear the slot type (a class's slots are weapon, ability, armor,
ring), so a Wakizashi is an ability, as it is for the Samurai.
"""
import os
import re
import shutil
import struct

import wiki_data as wd

CATEGORIES = [("weapons", "Weapons", "Weapon", "weapon"), ("abilities", "Abilities", "Ability", "ability"),
              ("armor", "Armor", "Armor", "armor"), ("rings", "Rings", "Ring", "ring")]
GROUPS = [
    ("tiered", "Tiered", "T", "Drop from monsters all over the game, by tier: the higher the tier, the better."),
    ("untiered", "Untiered (UT)", "UT", "Each comes from particular bosses, with numbers or an effect of its own."),
    ("future", "Future additions", "UT", "Untiered items that nothing drops, sells or forges yet: they'll get a source in a later update."),
    ("set", "Set pieces (ST): future additions", "ST",
     "One piece of a set of four for one class: weapon, ability, armor and ring. None can be found for now."),
    ("legendary", "Legendary", "Legendary", "Rare untiered items with the gold slot edge."),
    ("ascended", "Ascended", "Ascended", "Rare untiered items with the gold slot edge."),
    ("ethereal", "Ethereal", "Ethereal", "Rare untiered items with the gold slot edge."),
    ("reward", "Reward items", "Reward", "Given by the staff as rewards: nothing drops or sells them."),
    ("admin", "Admin items", "Admin", "Made for staff and testing."),
    ("test", "Test items", "Test", "Left in the game's data from testing."),
]
GROUP_NAMES = {g: (title, tag) for g, title, tag, _ in GROUPS}
# groups a kind's page shows even with no items in them: the kinds whose untiered items have been
# reworked, whose special items are plain untiered ones for now
REWORKED = {"legendary", "ascended", "ethereal"}
ALWAYS_SHOWN = {"Ring": REWORKED, "Dagger": REWORKED, "Bow": REWORKED}
PLURAL = {"Staff": "Staves", "Dual Blades": "Dual Blades", "Shuriken": "Shuriken", "Wakizashi": "Wakizashi",
          "Leather armor": "Leather armor", "Heavy armor": "Heavy armor"}
STAT = {0: "HP", 3: "MP", 20: "Att", 21: "Def", 22: "Spd", 26: "Vit", 27: "Wis", 28: "Dex"}
STAT_ORDER = list(STAT.values())
ICON = 48  # px: 8px tiles at 6x, 16px tiles at 3x
SPECIAL_EDGE = "#FFC800"


def plural(kind):
    return PLURAL.get(kind, kind + "s")


def num(v, default=None):
    try:
        f = float(v)
    except (TypeError, ValueError):
        return default
    return "%g" % round(f, 2)


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24])



def _heal_row(amount, what, ms):
    """A pet's heal as a row of its card's healing block: ('HP', '<b>10-60</b> every 3s
    &middot; <b>12</b> a second') for "10-60" (or "76") each time, every `ms`; None when it
    doesn't heal that."""
    nums = [int(x) for x in re.findall(r"\d+", amount or "")][:2]
    if not nums or not any(nums):
        return None
    value = "<b>%s</b>" % "&ndash;".join(str(n) for n in nums)
    if ms > 0:
        per_second = sum(nums) / len(nums) / (ms / 1000)
        value += ' every %gs <span class="sep">&middot;</span> <b>%s</b> a second' % (
            ms / 1000, "%.0f" % per_second if per_second >= 1 else "%.1f" % per_second)
    return what, value

class Gear:
    """`h` is build_wiki itself, for its helpers (slugify, esc, pct, who, unique, HIDDEN_MONSTER)."""

    def __init__(self, gen, h):
        self.gen, self.h, self.d = gen, h, gen.d
        self.items = self.d.gear()
        self.classes = self.d.classes()
        self.shop = {}
        for shop, item, price, currency in wd.shop_items():
            self.shop.setdefault(item, []).append((shop, price, currency))
        self._edges = self._edge_colours()
        # the kinds of each category, in the order the classes wear them
        self.kinds = {c[0]: [] for c in CATEGORIES}
        cat_of = {}
        for it in self.items:
            cat_of.setdefault(it["slot"], it["category"])
        for c in self.classes:
            for t in c["slots"]:
                if t in cat_of and t not in self.kinds[cat_of[t]]:
                    self.kinds[cat_of[t]].append(t)
        for t in sorted(cat_of):
            if t not in self.kinds[cat_of[t]]:
                self.kinds[cat_of[t]].append(t)
        self.wearers = {}
        for c in self.classes:
            for t in c["slots"]:
                self.wearers.setdefault(t, []).append(c)
        self.tier_loot = self._tier_loot()
        self.starts = {}
        self.gear_gives = {}
        for c in self.classes:
            for s in c["start"]:
                if s:
                    self.starts.setdefault(s, []).append(c["name"])
            for t in c["slots"]:
                g = self.d.gear_items(t)
                if g:
                    self.gear_gives.setdefault(g["id"], []).append(c["name"])
        # the forge's own recipes for untiered items (not the two-of-the-tier-below chains)
        self.forged = {}
        for r in self.gen.forge:
            if not r["chain"]:
                self.forged.setdefault(r["result"], []).append(r)
        # untiered items with no source yet are future additions
        for it in self.items:
            if it["group"] == "untiered" and not self.sources(it):
                it["group"] = "future"
        self.by_slot = {}
        for it in self.items:
            self.by_slot.setdefault(it["slot"], []).append(it)
        order = [g for g, _, _, _ in GROUPS]
        for lst in self.by_slot.values():
            lst.sort(key=lambda it: (order.index(it["group"]), it["tier"] if it["group"] == "tiered" else 0,
                                     it["doomed"], it["name"].lower()))
        self.sheets = {}  # sheet name -> (css class, file, tile w, tile h, sheet w, sheet h)
        self.href = {}    # item id -> its place on its kind's page, from the wiki's root
        for slot, lst in self.by_slot.items():
            used = set()
            for it in lst:
                it["anchor"] = self._anchor(it["id"], used)
                self.href[it["id"]] = "gear/%s#%s" % (self.kind_file(slot), it["anchor"])

    # --- names and places ---------------------------------------------------------------------
    def _anchor(self, oid, used):
        a = base = "i-" + self.h.slugify(oid)
        n = 2
        while a in used:
            a, n = "%s-%d" % (base, n), n + 1
        used.add(a)
        return a

    def kind_name(self, slot):
        return wd.SLOT_NAMES[slot]

    def kind_file(self, slot):
        cat = self.by_slot[slot][0]["category"]
        if len(self.kinds[cat]) == 1:
            return cat + ".html"
        return self.h.slugify(self.kind_name(slot)) + ".html"

    def category_file(self, cat):
        kinds = self.kinds[cat]
        return self.kind_file(kinds[0]) if len(kinds) == 1 else cat + ".html"

    def class_file(self, c):
        return self.h.slugify(c["name"]) + ".html"

    def _edge_colours(self):
        """The slot edge colours from the client (wiki_data.tier_colours)."""
        tiers, out = [], {}
        for label, colour in wd.tier_colours():
            m = re.match(r"Tier (\d+) to (\d+)", label)
            if m:
                tiers.append((int(m.group(1)), int(m.group(2)), colour))
            elif re.match(r"Tier (\d+) and up", label):
                out["top"] = colour
            elif "(ST)" in label:
                out["set"] = colour
            elif "(UT)" in label:
                out["untiered"] = colour
            else:
                out["special"] = colour
        out["tiers"] = tiers
        return out

    def edge(self, it):
        e = self._edges
        if it["group"] == "tiered":
            for lo, hi, c in e["tiers"]:
                if lo <= it["tier"] <= hi:
                    return c
            return e.get("top", SPECIAL_EDGE)
        if it["group"] in ("set", "untiered"):
            return e.get(it["group"], SPECIAL_EDGE)
        if it["group"] in ("test", "future", "reward"):
            return e.get("untiered", SPECIAL_EDGE)
        return e.get("special", SPECIAL_EDGE)

    def tag(self, it):
        if it["group"] == "tiered":
            return "T%d" % it["tier"]
        return GROUP_NAMES[it["group"]][1]

    # --- where items come from ----------------------------------------------------------------
    def _tier_loot(self):
        """{(item type, tier): [monster, ...]}: who drops which tier, weakest first."""
        out = {}
        for m in self.d.monsters():
            if self.h.HIDDEN_MONSTER.search(m):
                continue
            for kind, what, _ in self.d.drops(m):
                if kind == "tier":
                    lst = out.setdefault(what, [])
                    if m not in lst:
                        lst.append(m)
        for lst in out.values():
            lst.sort(key=lambda m: (self.d.objects.get(m, {}).get("hp", 0), m))
        return out

    def sources(self, it):
        """What says where the item comes from: [(label, html)]."""
        h, d, oid = self.h, self.d, it["id"]
        rows = []
        drops = h.unique([(m, c) for m, c in self.gen.idx.get(oid, []) if not h.HIDDEN_MONSTER.search(m) and c], d)
        if drops:
            names = ["%s (%s)" % (h.esc(h.who(d, m)), h.pct(c)) for m, c in drops[:4]]
            if len(drops) > 4:
                names.append("%d more" % (len(drops) - 4))
            rows.append(("Drops from", ", ".join(names)))
        if it["tier_loot"]:
            ms = self.tier_loot.get((it["tier_loot"], it["tier"]), [])
            names = []
            for m in ms:
                n = h.who(d, m)
                if n not in names:
                    names.append(n)
            if len(names) == 1:
                rows.append(("Tier loot", h.esc(names[0])))
            elif names:
                rows.append(("Tier loot", "%d monsters, such as %s" % (len(names), h.esc(", ".join(names[:3])))))
        for shop, price, currency in self.shop.get(oid, []):
            rows.append(("Shop", "<code>/shop %s</code>, %d %s" % (h.esc(shop), price, h.esc(currency))))
        if oid in self.gen.bazaar:
            price, shop = self.gen.bazaar[oid]
            rows.append(("Shop", "Nexus Bazaar (%s), %d fame" % (h.esc(shop), price)))
        for r in self.forged.get(oid, []):
            counts = {}
            for i in r["items"]:
                counts[i] = counts.get(i, 0) + 1
            items = " + ".join(("%d &times; " % n if n > 1 else "") + h.esc(d.name(i)) for i, n in counts.items())
            rows.append(("Divine Forge", "%s, %s fame%s" % (items, "{:,}".format(r["fame"]),
                                                             " (needs a Forge Amulet)" if r["advanced"] else "")))
        if oid in self.starts:
            rows.append(("Starting gear", h.esc(", ".join(self.starts[oid]))))
        if oid in self.gear_gives:
            rows.append(("<code>/gear</code>", "Gives it to the " + h.esc(", ".join(self.gear_gives[oid]))))
        return rows

    # --- what items do ------------------------------------------------------------------------
    def numbers(self, it):
        """The item's main numbers: [html]."""
        el = it["el"]
        out = []
        proj = el.find("Projectile")
        acts = [(a.text or "").strip() for a in el.findall("Activate")]
        weapon = it["category"] == "weapons"
        if proj is not None and (weapon or any(a in ("Shoot", "BulletNova", "BulletCreate") for a in acts)):
            lo = wd._int(wd._text(proj, "MinDamage") or wd._text(proj, "Damage"), 0)
            hi = wd._int(wd._text(proj, "MaxDamage") or wd._text(proj, "Damage"), lo)
            shots = wd._int(wd._text(el, "NumProjectiles"), 1)
            if hi:
                dmg = "%d&ndash;%d" % (lo, hi) if lo != hi else "%d" % lo
                out.append("Damage <b>%s</b>%s" % (dmg, " &times; %d" % shots if shots > 1 else ""))
            speed = float(wd._text(proj, "Speed") or 0)
            life = float(wd._text(proj, "LifetimeMS") or 0)
            if speed and life:
                out.append("Range <b>%g</b>" % round(speed * life / 10000, 1))
            rof = float(wd._text(el, "RateOfFire") or 1)
            if weapon and abs(rof - 1) > 1e-6:
                out.append("Rate of fire <b>%d%%</b>" % round(rof * 100))
            flags = [w for tag, w in (("MultiHit", "hits several"), ("PassesCover", "through walls"),
                                      ("ArmorPiercing", "ignores defense")) if proj.find(tag) is not None]
            for c in proj.findall("ConditionEffect"):
                flags.append("%s %ss" % ((c.text or "").strip(), num(c.get("duration"), "?")))
            bleed = proj.find("Bleed")
            if bleed is not None:
                flags.append("bleeds %s over %ss" % ((bleed.text or "").strip(), num(bleed.get("duration"), "?")))
            if flags:
                out.append('<span class="dim">%s</span>' % self.h.esc(", ".join(flags)))
        mp = wd._text(el, "MpCost")
        if mp:
            out.append("MP <b>%s</b>" % self.h.esc(mp))
        cd = num(wd._text(el, "Cooldown"))
        if cd and el.find("Usable") is not None:
            out.append("Cooldown <b>%ss</b>" % cd)
        return out

    def on_equip(self, it):
        stats = {}
        for a in it["el"].findall("ActivateOnEquip"):
            if (a.text or "").strip() == "IncrementStat":
                s = STAT.get(wd._int(a.get("stat"), -1))
                if s:
                    stats[s] = stats.get(s, 0) + wd._int(a.get("amount"), 0)
        return ", ".join("%+d %s" % (stats[s], s) for s in STAT_ORDER if stats.get(s))

    def effects(self, it):
        out = []
        for a in it["el"].findall("Activate"):
            t = self.effect(a)
            if t and t not in out:
                out.append(t)
        return "; ".join(out)

    def effect(self, a):
        e = (a.text or "").strip()
        g = a.get
        if g("ignoreOnTooltip") in ("true", "True"):
            return None

        def n(k, default="?"):
            return num(g(k), default)

        def within(k="range"):
            return " within %s tiles" % n(k) if g(k) else ""
        cond = " and %s for %ss" % (g("condEffect"), n("condDuration")) if g("condEffect") else ""
        name = self.d.name(g("objectId")) if g("objectId") else ""
        stat = STAT.get(wd._int(g("stat"), -1), "stat")
        if e == "HealNova":
            return "heals %s HP%s" % (n("amount"), within())
        if e == "ConditionEffectSelf":
            return "%s for %ss" % (g("effect") or "an effect", n("duration"))
        if e == "ConditionEffectAura":
            return "%s for %ss to allies%s" % (g("effect") or "an effect", n("duration"), within())
        if e == "GenericActivate":
            who = "enemies" if (g("target") or "").lower().startswith("enem") else "allies"
            return "%s for %ss to %s%s" % (g("effect") or "an effect", n("duration"), who, within())
        if e == "StatBoostSelf":
            return "%+d %s for %ss" % (wd._int(g("amount")), stat, n("duration"))
        if e == "StatBoostAura":
            return "%+d %s for %ss to allies%s" % (wd._int(g("amount")), stat, n("duration"), within())
        if e == "VampireBlast":
            heal = ", heals %s HP" % n("heal") if g("heal") else ""
            return "%s damage%s%s%s" % (n("totalDamage"), within("radius"), cond, heal)
        if e == "Trap":
            return "a trap: %s damage%s%s" % (n("totalDamage"), within("radius"), cond)
        if e == "PoisonGrenade":
            return "poison: %s damage over %ss%s" % (n("totalDamage"), n("duration", "3"), within("radius"))
        if e == "StasisBlast":
            return "stasis for %ss" % n("duration")
        if e == "Decoy":
            return "a decoy for %ss" % n("duration")
        if e == "MultiDecoy":
            return "%s decoys for %ss" % (n("amount"), n("duration"))
        if e == "Teleport":
            return "teleports you" + (" up to %s tiles" % n("maxDistance") if g("maxDistance") else "")
        if e == "Lightning":
            return "lightning: %s damage to up to %s enemies%s" % (n("totalDamage"), n("maxTargets"), cond)
        if e == "BulletNova":
            return "a ring of %s shots" % n("numShots", "20")
        if e == "BulletCreate":
            return "%s shots" % n("numShots", "1")
        if e == "Shoot":
            return "fires its shot"
        if e == "Halo":
            return "a halo: heals %s HP%s for %ss" % (n("amount"), within(), n("duration"))
        if e == "TomeDamage":
            return "%s damage%s" % (n("totalDamage"), within())
        if e == "SamuraiAbility":
            return "%s damage%s" % (n("totalDamage"), within("radius"))
        if e == "DazeBlast":
            return "dazes enemies" + within("radius")
        if e == "RemoveNegativeConditions":
            return "removes bad effects" + within()
        if e == "ClearConditionEffectAura":
            return "clears %s%s" % (g("effect") or "effects", within())
        if e == "BuildTower":
            return "builds a turret" + (" for %ss" % n("duration") if g("duration") else "")
        if e in ("Summon", "Pet", "ObjectToss", "MonsterToss") and name:
            verb = "throws" if "Toss" in e else "summons"
            return "%s %s" % (verb, name) + (" for %ss" % n("duration") if g("duration") else "")
        if e == "SelfTransform":
            return "changes your look for %ss" % n("duration")
        return re.sub(r"(?<!^)(?=[A-Z])", " ", e).lower() if e else None

    # --- an item ------------------------------------------------------------------------------
    def icon(self, it):
        return self.sprite(it["texture"], self.edge(it))

    def sprite(self, texture, edge):
        """A sprite in a slot with the given edge colour; texture is (sheet name, index) or None."""
        cls = style = ""
        if texture:
            sheet, index = texture
            s = self.sheets.get(sheet)
            if s is None and sheet in self._image_sets:
                path, tw, th, bw, bh = self._image_sets[sheet]
                full = os.path.join(wd.ROOT, "client", "src", path.replace("/", os.sep))
                if os.path.isfile(full):
                    sw, sh = png_size(full)
                    s = self.sheets[sheet] = ("s%d" % len(self.sheets), full, tw, th, sw, sh, bw, bh)
            if s:
                cls, _, tw, th, sw, sh, bw, bh = s
                cols = max(1, sw // bw)
                scale = max(1, ICON // max(tw, th))
                style = ' style="background-position:-%dpx -%dpx"' % ((index % cols) * bw * scale,
                                                                     (index // cols) * bh * scale)
        if not cls:
            return '<span class="ico" style="--c:%s"></span>' % edge
        return '<span class="ico" style="--c:%s"><i class="%s"%s></i></span>' % (edge, cls, style)

    def card(self, it):
        h = self.h
        rows = []
        nums = self.numbers(it)
        if nums:
            rows.append('<p class="it-nums">%s</p>' % " <span class=\"sep\">&middot;</span> ".join(nums))
        fx = self.effects(it)
        if fx:
            rows.append('<p class="it-line"><span>On use</span> %s</p>' % h.esc(fx[0].upper() + fx[1:]))
        eq = self.on_equip(it)
        if eq:
            rows.append('<p class="it-line it-eq"><span>On equip</span> %s</p>' % h.esc(eq))
        desc = it["desc"].strip()
        if desc:
            rows.append('<p class="it-desc">%s</p>' % h.esc(desc))
        src = self.sources(it)
        flags = []
        if it["soulbound"]:
            flags.append("Soulbound")
        if it["doomed"]:
            flags.append("From Doomed Realms")
        if src or flags:
            rows.append('<dl class="it-src">%s%s</dl>' % (
                "".join("<div><dt>%s</dt><dd>%s</dd></div>" % r for r in src),
                '<div class="it-flags"><dd>%s</dd></div>' % " &middot; ".join(flags) if flags else ""))
        search = " ".join([it["name"], it["id"], self.tag(it), it["kind"], GROUP_NAMES[it["group"]][0]]
                          + [re.sub(r"<[^>]+>", "", v) for _, v in src])
        return ('<article class="it" id="%s" data-search="%s">%s<div class="it-body"><h3><span>%s</span>'
                '<span class="tag" style="--c:%s">%s</span></h3>%s</div></article>' % (
                    it["anchor"], h.esc(search.lower()), self.icon(it), h.esc(it["name"]), self.edge(it),
                    h.esc(self.tag(it)), "".join(rows)))

    def item_text(self, it):
        """The item's words for the search."""
        bits = [self.tag(it), it["kind"]]
        nums = ", ".join(self.numbers(it)).replace("&ndash;", "-").replace("&times;", "x")
        if nums:
            bits.append(re.sub(r"<[^>]+>", "", nums))
        for x in (self.effects(it), self.on_equip(it), it["desc"]):
            if x:
                bits.append(x)
        return re.sub(r"\s+", " ", ". ".join(bits)).strip()[:220]

    # --- lists --------------------------------------------------------------------------------
    def groups(self, slot, level, prefix=""):
        """A kind's items under a heading per group."""
        h = self.h
        parts = []
        lst = self.by_slot.get(slot, [])
        shown = ALWAYS_SHOWN.get(lst[0]["kind"] if lst else None, set())
        for g, title, _, blurb in GROUPS:
            its = [it for it in lst if it["group"] == g]
            if not its and g not in shown:
                continue
            ident = prefix + g
            parts.append('<section class="its-group"><h%d id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>'
                         '%s <span class="count">%d</span></h%d><p class="dim">%s</p><div class="its">%s</div></section>'
                         % (level, ident, ident, h.esc(title), len(its), level, h.esc(blurb),
                            "".join(self.card(it) for it in its)))
        return "\n".join(parts)

    def counts(self, slot):
        lst = self.by_slot.get(slot, [])
        bits = []
        for g, _, tag, _ in GROUPS:
            n = sum(1 for it in lst if it["group"] == g)
            if n:
                bits.append("%d %s" % (n, {"tiered": "tiered", "future": "future additions"}.get(g, tag)))
        return " &middot; ".join(bits)

    def best(self, slot):
        """The item that stands for a kind: its top tier, else its first item."""
        lst = self.by_slot.get(slot, [])
        tiered = [it for it in lst if it["group"] == "tiered" and not it["doomed"]] or \
                 [it for it in lst if it["group"] == "tiered"]
        return max(tiered, key=lambda it: it["tier"]) if tiered else (lst[0] if lst else None)

    def worn_by(self, slot, up=""):
        return ", ".join('<a href="%sclasses/%s">%s</a>' % (up, self.class_file(c), self.h.esc(c["name"]))
                         for c in self.wearers.get(slot, []))

    def tabs(self, cat=None, slot=None, by_class=False):
        """The tab rows on a gear page (all of them are in wiki/gear or wiki/classes)."""
        h = self.h
        top = ['<a href="../gear.html">Overview</a>']
        for c, title, _, _ in CATEGORIES:
            cur = ' aria-current="page"' if c == cat else ""
            top.append('<a href="../gear/%s"%s>%s</a>' % (self.category_file(c), cur, title))
        top.append('<a href="../gear.html#by-class"%s>By class</a>' % (' aria-current="page"' if by_class else ""))
        out = '<nav class="tabs" aria-label="Gear">%s</nav>' % "".join(top)
        if cat and len(self.kinds[cat]) > 1:
            sub = []
            for t in self.kinds[cat]:
                cur = ' aria-current="page"' if t == slot else ""
                sub.append('<a href="%s"%s>%s <span>%d</span></a>' % (
                    self.kind_file(t), cur, h.esc(plural(self.kind_name(t))), len(self.by_slot.get(t, []))))
            out += '<nav class="tabs tabs-sub" aria-label="%s">%s</nav>' % (
                h.esc(dict((c[0], c[1]) for c in CATEGORIES)[cat]), "".join(sub))
        return out

    def filter(self, ident, what):
        return ('<div class="filter"><label for="%s">Filter</label><input id="%s" type="search" placeholder="%s" '
                'data-filter=".it" autocomplete="off"></div>' % (ident, ident, what))

    def kind_cards(self, cat, up=""):
        h = self.h
        cards = []
        for t in self.kinds[cat]:
            b = self.best(t)
            cards.append('<a class="kind" href="%sgear/%s">%s<span class="kind-name">%s</span>'
                         '<span class="kind-n">%s</span><span class="kind-who">%s</span></a>' % (
                             up, self.kind_file(t), self.icon(b) if b else "", h.esc(plural(self.kind_name(t))),
                             self.counts(t), h.esc(", ".join(c["name"] for c in self.wearers.get(t, []))
                                                   or "No class wears these")))
        return '<div class="kinds">%s</div>' % "".join(cards)

    # --- the landing page, {{gen:gear}} in gear.md ----------------------------------------------
    def overview(self, used):
        h = self.h
        parts = []
        for cat, title, _, _ in CATEGORIES:
            used.add(cat)
            n = sum(len(self.by_slot.get(t, [])) for t in self.kinds[cat])
            parts.append('<h2 id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>%s <span class="count">%d</span></h2>'
                         % (cat, cat, title, n))
            parts.append(self.kind_cards(cat))
        used.add("by-class")
        parts.append('<h2 id="by-class"><a class="anchor" href="#by-class" aria-hidden="true">#</a>By class</h2>'
                     '<p>Everything one class can use, on one page: a tab each for its weapons, abilities, armor and rings.</p>')
        rows = []
        for c in self.classes:
            kinds = [plural(self.kind_name(t)) for t in c["slots"][:3] if t in self.by_slot]
            b = self.best(c["slots"][0]) if c["slots"] and c["slots"][0] in self.by_slot else None
            rows.append('<a class="kind" href="classes/%s">%s<span class="kind-name">%s</span><span class="kind-n">%s</span></a>' % (
                self.class_file(c), self.icon(b) if b else "", h.esc(c["name"]), h.esc(", ".join(kinds))))
        parts.append('<div class="kinds">%s</div>' % "".join(rows))
        return "\n".join(parts)

    # --- pets, on the Pets guide (pets.md) ----------------------------------------------------
    def _item_sources(self, oid):
        """Drops and shops of a non-gear item: [(label, html)]."""
        h, d = self.h, self.d
        rows = []
        drops = h.unique([(m, c) for m, c in self.gen.idx.get(oid, []) if not h.HIDDEN_MONSTER.search(m) and c], d)
        if drops:
            names = ["%s (%s)" % (h.esc(h.who(d, m)), h.pct(c)) for m, c in drops[:4]]
            if len(drops) > 4:
                names.append("%d more" % (len(drops) - 4))
            rows.append(("Drops from", ", ".join(names)))
        for shop, price, currency in self.shop.get(oid, []):
            rows.append(("Shop", "<code>/shop %s</code>, %d %s" % (h.esc(shop), price, h.esc(currency))))
        if oid in self.gen.bazaar:
            price, shop = self.gen.bazaar[oid]
            rows.append(("Shop", "Nexus Bazaar (%s), %d fame" % (h.esc(shop), price)))
        return rows

    def _pet_card(self, ident, texture, edge, name, tag, lines, desc, src, flags, search):
        h = self.h
        rows = list(lines)
        if desc:
            rows.append('<p class="it-desc">%s</p>' % h.esc(desc))
        if src or flags:
            rows.append('<dl class="it-src">%s%s</dl>' % (
                "".join("<div><dt>%s</dt><dd>%s</dd></div>" % r for r in src),
                '<div class="it-flags"><dd>%s</dd></div>' % " &middot; ".join(flags) if flags else ""))
        return ('<article class="it" id="%s" data-search="%s">%s<div class="it-body"><h3><span>%s</span>'
                '<span class="tag" style="--c:%s">%s</span></h3>%s</div></article>' % (
                    ident, h.esc(search.lower()), self.sprite(texture, edge), h.esc(name), edge, h.esc(tag),
                    "".join(rows)))

    def _filter_box(self, ident, what, target):
        return ('<div class="filter"><label for="%s">Filter</label><input id="%s" type="search" placeholder="%s" '
                'data-filter="%s" autocomplete="off"></div>' % (ident, ident, what, target))

    def pet_generators(self, used):
        """Every PermaPet item: the pet it gives, its numbers and where to get it."""
        h, d = self.h, self.d
        sold = d.bazaar_pets()
        price = d.pet_generator_price
        edge = self._edges.get("untiered", SPECIAL_EDGE)
        ids = set()
        cards = []
        # by size, the biggest first, then the strongest
        order = {"Admin": 0, "Large": 1, "Medium": 2, "Small": 3}
        for p in sorted(d.pets(), key=lambda p: (order.get(p["size"], 4), -p["dps"], p["name"].lower())):
            oid = p["item"]
            lines = ['<p class="it-nums">Damage a second <b>%d</b></p>' % p["dps"]] if p["dps"] else []
            pet = d.name(p["pet"]) if p["pet"] else ""
            if pet and pet != p["name"]:
                lines.append('<p class="it-line"><span>Pet</span> %s</p>' % h.esc(pet))
            # healing, its own block: what one heal restores, how often, and about how much a second
            heals = [r for r in (_heal_row(p["hp"], "HP", p["hpms"]), _heal_row(p["mp"], "MP", p["mpms"])) if r]
            if heals:
                lines.append('<dl class="it-heal"><span>Heals</span>%s</dl>' % "".join(
                    '<div class="%s"><dt>%s</dt><dd>%s</dd></div>' % (what.lower(), what, value) for what, value in heals))
            for effect in p["effects"]:
                lines.append('<p class="it-line"><span>Also</span> %s</p>' % h.esc(effect))
            src = self._item_sources(oid)
            if oid in sold and price and not any("Nexus Bazaar" in v for _, v in src):
                src.append(("Shop", "Nexus Bazaar (pets), %d fame" % price))
            flags = [x for x, on in (("Soulbound", d.objects[oid]["soulbound"]), ("From Doomed Realms", p["doomed"])) if on]
            search = " ".join([p["name"], pet, "pet generator", p["size"]] + p["effects"] + [re.sub(r"<[^>]+>", "", v) for _, v in src])
            tag = " · ".join(x for x in (p["size"], "%d DPS" % p["dps"] if p["dps"] else "") if x) or "Pet"
            cards.append(self._pet_card(self._anchor("pet-" + oid, ids), d.texture(p["pet"]) or d.texture(oid), edge,
                                        p["name"], tag, lines,
                                        d.objects[oid]["desc"].strip(), src, flags, search))
        return (self._filter_box("pet-filter", "Pet, monster or shop", "#pet-list .it")
                + '<div class="its" id="pet-list">%s</div>' % "".join(cards))

    def pet_summons(self, used):
        """Every item that summons a helper beside you (the Pet effect)."""
        h, d = self.h, self.d
        sold = d.bazaar_pets()
        ids = set()
        cards = []
        gear_ids = {it["id"]: it for it in self.items}
        for s in sorted(d.summons(), key=lambda s: d.name(s["item"]).lower()):
            oid = s["item"]
            o = d.objects[oid]
            if "Test" in o["file"]:
                continue
            it = gear_ids.get(oid)
            what = d.name(s["summons"])
            lines = ['<p class="it-line"><span>Summons</span> %s</p>' % h.esc(what)]
            src = self._item_sources(oid)
            if oid in sold and d.pet_generator_price and not any("Nexus Bazaar" in v for _, v in src):
                src.append(("Shop", "Nexus Bazaar (pets), %d fame" % d.pet_generator_price))
            if it:
                src.insert(0, ("Gear", '<a href="%s">%s, on the %s page</a>' % (
                    self.href[oid], h.esc(it["kind"]), h.esc(plural(it["kind"])))))
            flags = [x for x, on in (("Soulbound", o["soulbound"]), ("Used up when used", o["consumable"])) if on]
            search = " ".join([o["name"], what, "summon"] + [re.sub(r"<[^>]+>", "", v) for _, v in src])
            cards.append(self._pet_card(self._anchor("summon-" + oid, ids), d.texture(oid),
                                        self.edge(it) if it else self._edges.get("untiered", SPECIAL_EDGE),
                                        o["name"], self.tag(it) if it else "Item", lines, o["desc"].strip(), src,
                                        flags, search))
        return (self._filter_box("summon-filter", "Item or what it summons", "#summon-list .it")
                + '<div class="its" id="summon-list">%s</div>' % "".join(cards))

    def pet_yard(self, used):
        """The pet yard's pets (EmbeddedData_PetsCXML), by family."""
        h = self.h
        tiers = self._edges["tiers"]
        rarity_edge = {"Common": tiers[0][2] if tiers else SPECIAL_EDGE,
                       "Rare": tiers[2][2] if len(tiers) > 2 else SPECIAL_EDGE,
                       "Divine": self._edges.get("special", SPECIAL_EDGE)}
        order = ["Common", "Rare", "Divine"]
        fams = {}
        for p in self.d.yard_pets():
            fams.setdefault(p["family"] or "? ? ? ?", []).append(p)
        parts = [self._filter_box("yard-filter", "Pet, family, rarity or ability", "#yard .pet"), '<div id="yard">']
        ids = set()
        for fam in sorted(fams, key=lambda f: (not re.search(r"\w", f), f)):
            lst = sorted(fams[fam], key=lambda p: (order.index(p["rarity"]) if p["rarity"] in order else 9,
                                                   p["name"].lower()))
            title = fam if re.search(r"\w", fam) else "Mystery family (? ? ? ?)"
            ident = "family-" + h.slugify(title)
            used.add(ident)
            cards = []
            for p in lst:
                edge = rarity_edge.get(p["rarity"], SPECIAL_EDGE)
                search = " ".join([p["name"], fam, p["rarity"], p["ability"]])
                cards.append('<article class="pet" id="%s" data-search="%s">%s<span class="kind-name">%s</span>'
                             '<span class="kind-n"><span class="tag" style="--c:%s">%s</span></span>'
                             '<span class="kind-who">%s</span></article>' % (
                                 self._anchor("yard-" + p["id"], ids), h.esc(search.lower()),
                                 self.sprite(self.d.texture(p["skin"]) or self.d.texture(p["id"]), edge),
                                 h.esc(p["name"]), edge,
                                 h.esc(p["rarity"] or "?"), h.esc(p["ability"])))
            parts.append('<section class="its-group"><h3 id="%s"><a class="anchor" href="#%s" aria-hidden="true">#</a>'
                         '%s <span class="count">%d</span></h3><div class="kinds">%s</div></section>' % (
                             ident, ident, h.esc(title), len(lst), "".join(cards)))
        parts.append("</div>")
        return "\n".join(parts)

    # --- the pages ----------------------------------------------------------------------------
    def pages(self):
        """[(path under wiki/, nav page it belongs to, title, eyebrow, summary, body, pager,
        [search entries])]."""
        h = self.h
        out = []
        order = [t for c in CATEGORIES for t in self.kinds[c[0]]]
        for cat, title, single, _ in CATEGORIES:
            if len(self.kinds[cat]) > 1:
                body = self.tabs(cat) + self.kind_cards(cat, "../")
                n = sum(len(self.by_slot.get(t, [])) for t in self.kinds[cat])
                summary = "Every kind of %s in the game, %d items in all." % (single.lower(), n)
                out.append(("gear/" + cat + ".html", "gear", title, "Gear", summary, body, "",
                            [{"p": "Gear", "h": title, "u": "gear/" + cat + ".html", "t": summary}]))
        for i, t in enumerate(order):
            kind = self.kind_name(t)
            lst = self.by_slot[t]
            cat = lst[0]["category"]
            who = self.worn_by(t, "../")
            body = [self.tabs(cat, t),
                    '<p class="kind-head">%s%s</p>' % (self.counts(t), " &middot; Worn by " + who if who else ""),
                    self.filter("gear-filter", "Name, tier, boss or shop"),
                    self.groups(t, 2)]
            path = "gear/" + self.kind_file(t)
            title = plural(kind)
            summary = "Every %s in the game, %d in all: tiered, untiered, set pieces and the rest, with their numbers and where they drop." % (
                kind.lower(), len(lst))
            pager = self.pager(order, i, lambda s: self.kind_file(s), lambda s: plural(self.kind_name(s)))
            entries = [{"p": "Gear", "h": title, "u": path, "t": summary}]
            for it in lst:
                entries.append({"p": title, "h": it["name"], "u": path + "#" + it["anchor"], "t": self.item_text(it)})
            eyebrow = "Gear &middot; " + dict((c[0], c[1]) for c in CATEGORIES)[cat]
            out.append((path, "gear", title, eyebrow, summary, "\n".join(body), pager, entries))
        for i, c in enumerate(self.classes):
            out.append(self.class_page(c, i))
        return out

    def pager(self, seq, i, file_of, name_of):
        h = self.h
        p = ""
        if i > 0:
            p += '<a class="prev" href="%s"><span>Previous</span>%s</a>' % (file_of(seq[i - 1]), h.esc(name_of(seq[i - 1])))
        if i + 1 < len(seq):
            p += '<a class="next" href="%s"><span>Next</span>%s</a>' % (file_of(seq[i + 1]), h.esc(name_of(seq[i + 1])))
        return p

    def class_page(self, c, i):
        h = self.h
        name = c["name"]
        tabs, panels = [], []
        for n, (cat, _, single, key) in enumerate(CATEGORIES):
            t = c["slots"][n] if n < len(c["slots"]) else None
            if t not in self.by_slot:
                continue
            kind = self.kind_name(t)
            label = single if cat == "rings" else "%s: %s" % (single, kind)
            tabs.append('<a href="#%s" role="tab" aria-controls="%s">%s <span>%d</span></a>' % (
                key, key, h.esc(label), len(self.by_slot[t])))
            panels.append('<section class="tab-panel" id="%s" role="tabpanel"><h2>%s <a class="more" href="../gear/%s">'
                          'Open the %s page</a></h2>%s</section>' % (
                              key, h.esc(plural(kind)), self.kind_file(t), h.esc(plural(kind)),
                              self.groups(t, 3, key + "-")))
        start = h.nice([s for s in c["start"] if s], self.d)
        desc = c["desc"].split("\n")[0] if len(c["desc"].strip(" .")) > 2 else ""
        intro = '<p class="kind-head">%s%s <a href="../classes.html#c-%s">Stats and more on the Classes page</a>.</p>' % (
            h.esc(desc) + " " if desc else "", "Starts with %s." % h.esc(start) if start else "", h.slugify(name))
        body = "\n".join([self.tabs(by_class=True), intro,
                          '<div class="tabs tabs-in" role="tablist" aria-label="%s gear">%s</div>' % (h.esc(name), "".join(tabs)),
                          self.filter("class-filter", "Name, tier, boss or shop"),
                          '<div class="tab-panels">%s</div>' % "".join(panels)])
        kinds = [plural(self.kind_name(t)).lower() for t in c["slots"][:3] if t in self.by_slot]
        summary = "Every weapon, ability, armor and ring a %s can use: %s and rings." % (name, ", ".join(kinds))
        path = "classes/" + self.class_file(c)
        pager = self.pager(self.classes, i, self.class_file, lambda x: x["name"])
        return (path, "classes", "%s gear" % name, "Gear &middot; By class", summary, body, pager,
                [{"p": "Gear by class", "h": "%s gear" % name, "u": path, "t": summary}])

    # --- files --------------------------------------------------------------------------------
    @property
    def _image_sets(self):
        if not hasattr(self, "_sets"):
            self._sets = wd.image_sets()
        return self._sets

    def write_assets(self, out):
        """wiki/sheets/<sheet>.png and wiki/icons.css; call after the pages are built, which picks
        the sheets."""
        os.makedirs(os.path.join(out, "sheets"), exist_ok=True)
        rules = []
        for name, (cls, full, tw, th, sw, sh, _, _) in sorted(self.sheets.items(), key=lambda kv: kv[1][0]):
            shutil.copy2(full, os.path.join(out, "sheets", name + ".png"))
            scale = max(1, ICON // max(tw, th))
            rules.append(".%s{background-image:url(sheets/%s.png);background-size:%dpx %dpx;width:%dpx;height:%dpx}" % (
                cls, name, sw * scale, sh * scale, tw * scale, th * scale))
        css = "/* The item sprites on the wiki's gear pages, written by tools/wiki_gear.py */\n" + "\n".join(rules) + "\n"
        with open(os.path.join(out, "icons.css"), "w", encoding="utf-8", newline="\n") as f:
            f.write(css)
        return css
