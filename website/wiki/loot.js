// The wiki's Loot Simulation page: the bosses' drops from tools/wiki_loot.py (the page's #loot-data),
// shown with your chances and rolled kill by kill the way the server does (Loots.cs, Loot.Handle):
// every row is its own roll for each player who did more than its threshold of the damage, at its
// chance times 1 + damage share + boosts (at most sure); a tier row picks one item of its pool by
// weight; a player's items go into bags of 8, every bag taking the colour of the best item's bag.
(function () {
  "use strict";
  var root = document.getElementById("loot-sim");
  var src = document.getElementById("loot-data");
  if (!root || !src) return;
  var DATA = JSON.parse(src.textContent);
  var ITEMS = DATA.items, RANK = DATA.bagRank, BAGNAME = DATA.bagNames, BAGICO = DATA.bagIcons;
  var BOSS = {};
  DATA.bosses.forEach(function (b) { BOSS[b.key] = b; });
  var BOOSTS = [
    { id: "event", label: "Loot event", v: DATA.boosts.event, on: DATA.boosts.event > 0 },
    { id: "weekend", label: "Weekend", v: DATA.boosts.weekend, on: false },
    { id: "ld", label: "Loot Drop Potion", v: DATA.boosts.ld, on: false },
    { id: "legend", label: "In the Legends", v: DATA.boosts.legend || 0, on: false }
  ].filter(function (b) { return b.v > 0; });
  var KINDS = { Weapon: "weapon", Ability: "ability", Armor: "armor", Ring: "ring" };
  var state = { boss: DATA.bosses[DATA.bosses.length - 1].key, view: "sim", share: 0.2, boosts: {},
                kills: 10, seed: 1 };
  BOOSTS.forEach(function (b) { state.boosts[b.id] = b.on; });
  var hash = location.hash.slice(1).split("-");
  if (BOSS[hash[0]]) state.boss = hash[0];
  if (["sim", "drops", "bags"].indexOf(hash[1]) >= 0) state.view = hash[1];

  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]; });
  }
  function mult() {
    return BOOSTS.reduce(function (s, b) { return s + (state.boosts[b.id] ? b.v : 0); }, 1 + state.share);
  }
  // the server compares the share rounded to 4 places, strictly over the threshold
  function open(r) { return r.thr < Math.round(state.share * 1e4) / 1e4; }
  function eff(r) { return open(r) ? Math.min(1, r.p * mult()) : 0; }
  function pct(p) {
    if (p >= 0.995) return "100%";
    if (p >= 0.1) return Math.round(p * 100) + "%";
    if (p >= 0.01) return (Math.round(p * 1000) / 10) + "%";
    return (Math.round(p * 10000) / 100) + "%";
  }
  function oneIn(p) { return p > 0 && p < 0.1 ? "1 in " + Math.round(1 / p).toLocaleString() : ""; }
  function barW(p) { return p <= 0 ? 0 : Math.max(2, Math.min(100, (Math.log10(p) + 3) / 3 * 100)); }
  function poolTotal(r) { return r.pool.reduce(function (s, x) { return s + x[1]; }, 0); }
  function label(r) { return "T" + r.tier + " " + (KINDS[r.type] || r.type.toLowerCase()); }

  function ico(id, title) {
    var it = ITEMS[id];
    return '<span class="ls-ico" title="' + esc(title || (it ? it.name : id)) + '">' + (it ? it.ico : "") + "</span>";
  }
  function name(id) {
    var it = ITEMS[id];
    if (!it) return esc(id);
    return it.href ? '<a href="' + esc(it.href) + '">' + esc(it.name) + "</a>" : esc(it.name);
  }
  function bagIco(b) {
    return '<span class="ls-bag" title="' + esc(BAGNAME[b]) + ' bag">' + (BAGICO[b] || "") + "</span>";
  }

  // --- what a kill gives one player ------------------------------------------------------------
  function rng(seed) {   // mulberry32: the same seed gives the same run while you move the controls
    return function () {
      seed |= 0; seed = seed + 0x6D2B79F5 | 0;
      var t = Math.imul(seed ^ seed >>> 15, 1 | seed);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
  }
  function rollKill(rows, rand) {
    var got = [];
    rows.forEach(function (r) {
      // the server draws the roll, then the item, for every row a player qualifies for
      if (!open(r)) return;
      var c = rand(), pick = r.id;
      if (r.kind === "tier") {
        var w = rand() * poolTotal(r);
        pick = r.pool[r.pool.length - 1][0];
        for (var i = 0; i < r.pool.length; i++) if ((w -= r.pool[i][1]) < 0) { pick = r.pool[i][0]; break; }
      }
      if (c < Math.min(1, r.p * mult())) got.push({ id: pick, row: r });
    });
    var colour = got.reduce(function (b, x) { return RANK[ITEMS[x.id].bag] > RANK[b] ? ITEMS[x.id].bag : b; }, 0);
    var bags = [];
    for (var i = 0; i < got.length; i += 8) bags.push(got.slice(i, i + 8));
    return { got: got, colour: colour, bags: bags };
  }

  // [(bag, probability)] of what a row gives when it hits
  function outcomes(r) {
    if (r.kind !== "tier") return [[ITEMS[r.id].bag, 1]];
    var total = poolTotal(r), m = {};
    r.pool.forEach(function (x) { var b = ITEMS[x[0]].bag; m[b] = (m[b] || 0) + x[1] / total; });
    return Object.keys(m).map(function (b) { return [+b, m[b]]; });
  }
  var byRank = RANK.map(function (_, b) { return b; }).sort(function (a, b) { return RANK[a] - RANK[b]; });
  function bagStats(rows) {
    var items = {}, atLeast = {}, colour = {};
    byRank.forEach(function (b) { items[b] = 0; atLeast[b] = 1; });
    rows.forEach(function (r) {
      var e = eff(r);
      outcomes(r).forEach(function (o) { items[o[0]] += e * o[1]; atLeast[o[0]] *= 1 - e * o[1]; });
    });
    byRank.forEach(function (b) { atLeast[b] = 1 - atLeast[b]; });
    // the colour of a kill's bags: the chance that the best item's bag is b
    var below = rows.reduce(function (s, r) { return s * (1 - eff(r)); }, 1);
    byRank.forEach(function (b) {
      var upTo = rows.reduce(function (s, r) {
        var better = outcomes(r).filter(function (o) { return RANK[o[0]] > RANK[b]; })
          .reduce(function (a, o) { return a + o[1]; }, 0);
        return s * (1 - eff(r) * better);
      }, 1);
      colour[b] = upTo - below; below = upTo;
    });
    // how many items a kill gives: every row its own coin
    var dist = [1];
    rows.forEach(function (r) {
      var e = eff(r), next = new Array(dist.length + 1).fill(0);
      dist.forEach(function (p, n) { next[n] += p * (1 - e); next[n + 1] += p * e; });
      dist = next;
    });
    var bags = [0, 0, 0, 0], mean = 0, meanBags = 0;
    dist.forEach(function (p, n) {
      bags[Math.min(3, Math.ceil(n / 8))] += p; mean += p * n; meanBags += p * Math.ceil(n / 8);
    });
    return { items: items, atLeast: atLeast, colour: colour, bags: bags, mean: mean, meanBags: meanBags };
  }

  // --- the simulator -------------------------------------------------------------------------
  var STAT = /^Potion of (Attack|Defense|Speed|Dexterity|Vitality|Wisdom)$/;
  var CATS = [
    ["stat", "Stat potions", function (x) { return STAT.test(x.id); }],
    ["lifemana", "Life and mana potions", function (x) { return /Potion of (Life|Mana)$/.test(x.id); }],
    ["oryx", "Oryx and Power potions", function (x) { return x.id === "Potion of Oryx" || x.id === "Potion of Power"; }],
    ["tiered", "Tiered gear", function (x) { return x.row.kind === "tier"; }],
    ["special", "Untiered and special gear", function (x) { return ITEMS[x.id].gear; }],
    ["other", "Shards, scrolls and the rest", function () { return true; }]
  ];
  function catOf(x) { for (var i = 0; i < CATS.length; i++) if (CATS[i][2](x)) return CATS[i][0]; }
  function tile(v, l) { return '<div class="ls-stat"><div class="v">' + v + '</div><div class="l">' + l + "</div></div>"; }

  function simView(b) {
    var rand = rng(state.seed), kills = [];
    for (var k = 0; k < state.kills; k++) kills.push(rollKill(b.rows, rand));
    var st = bagStats(b.rows), all = [], count = {}, pile = {};
    kills.forEach(function (k) { all = all.concat(k.got); });
    all.forEach(function (x) {
      var c = catOf(x); count[c] = (count[c] || 0) + 1;
      (pile[x.id] = pile[x.id] || { id: x.id, n: 0 }).n++;
    });
    var expect = {};
    b.rows.forEach(function (r) {
      var c = r.kind === "tier" ? "tiered" : catOf({ id: r.id, row: r });
      expect[c] = (expect[c] || 0) + eff(r);
    });
    var bagsDropped = kills.reduce(function (s, k) { return s + k.bags.length; }, 0);
    var white = kills.filter(function (k) { return k.colour === 6; }).length;
    var n = state.kills;
    var tiles = tile(all.length, "items<br>average " + (st.mean * n).toFixed(1)) +
      tile(bagsDropped, "bags<br>average " + (st.meanBags * n).toFixed(1)) +
      tile(white, "kills with a white bag<br>average " + ((st.colour[6] || 0) * n).toFixed(1));
    var cats = CATS.filter(function (c) { return count[c[0]] || expect[c[0]]; }).map(function (c) {
      return tile(count[c[0]] || 0, c[1] + "<br>average " + ((expect[c[0]] || 0) * n).toFixed(1));
    }).join("");
    var sorted = Object.keys(pile).map(function (id) { return pile[id]; }).sort(function (a, c) {
      return RANK[ITEMS[c.id].bag] - RANK[ITEMS[a.id].bag] || c.n - a.n;
    });
    var pileHtml = sorted.map(function (x) {
      return '<div class="ls-pi">' + ico(x.id) + '<span class="ls-cnt">' + x.n + "</span>" +
        '<span class="ls-pn">' + name(x.id) + "</span></div>";
    }).join("");
    var killHtml = kills.slice(0, 100).map(function (k, i) {
      var bags = k.bags.length ? k.bags.map(function (bag) {
        var slots = "";
        for (var j = 0; j < 8; j++) slots += bag[j] ? ico(bag[j].id) : '<span class="ls-ico empty"></span>';
        return '<div class="ls-kbag">' + bagIco(k.colour) + '<div class="ls-kslots">' + slots + "</div></div>";
      }).join("") : '<span class="note">nothing</span>';
      return '<div class="ls-kill"><div class="ls-kn">Kill ' + (i + 1) + "<span>" + k.got.length +
        " items</span></div>" + bags + "</div>";
    }).join("");
    return '<div class="ls-simctl"><span class="ls-lbl">Kills</span>' +
      [1, 10, 50, 100].map(function (c) {
        return '<button type="button" class="ls-tab" data-kills="' + c + '" aria-pressed="' + (c === n) + '">' + c + "</button>";
      }).join("") +
      '<input type="number" id="ls-kills" min="1" max="1000" value="' + n + '" aria-label="Number of kills">' +
      '<button type="button" class="ls-roll" id="ls-roll">Roll again</button></div>' +
      '<div class="ls-stats">' + tiles + "</div>" +
      "<h3>By kind</h3><div class=\"ls-stats\">" + cats + "</div>" +
      "<h3>Everything from " + n + " kill" + (n > 1 ? "s" : "") + "</h3>" +
      '<div class="ls-pile">' + (pileHtml || '<span class="note">Nothing dropped.</span>') + "</div>" +
      "<h3>Kill by kill" + (n > 100 ? " (the first 100)" : "") + "</h3><div class=\"ls-kills\">" + killHtml + "</div>";
  }

  // --- the drop table ---------------------------------------------------------------------------
  function rateCell(r) {
    var e = eff(r);
    if (!open(r)) {
      return '<td class="ls-rate dim">locked<span>needs over ' + Math.round(r.thr * 100) + "% of the damage</span></td>";
    }
    var sub = oneIn(e) || "base " + pct(r.p);
    if (r.kind === "tier") {
      var each = e / r.pool.length;
      sub = "a given item: 1 in " + Math.round(1 / each).toLocaleString();
    }
    return '<td class="ls-rate">' + pct(e) + "<span>" + sub + '</span><div class="ls-bar"><i style="width:' + barW(e) + '%"></i></div></td>';
  }
  function itemCell(r) {
    if (r.kind === "tier") {
      var weighted = r.pool.some(function (x) { return x[1] !== 1; });
      return '<td><div class="ls-row"><div><div class="ls-nm">' + label(r) + '</div><div class="ls-sub">one of ' +
        r.pool.length + (weighted ? ", weighted" : "") + '</div><div class="ls-pool">' +
        r.pool.map(function (x) { return ico(x[0], ITEMS[x[0]].name + (x[1] !== 1 ? " (" + x[1] + " as likely)" : "")); }).join("") +
        "</div></div></div></td>";
    }
    var it = ITEMS[r.id];
    return '<td><div class="ls-row">' + ico(r.id) + '<div><div class="ls-nm">' + name(r.id) + '</div><div class="ls-sub">' +
      bagIco(it.bag) + " " + BAGNAME[it.bag] + " bag</div></div></div></td>";
  }
  function dropsView(b) {
    var groups = [], seen = {};
    b.rows.forEach(function (r) { if (!seen[r.thr]) { seen[r.thr] = 1; groups.push(r.thr); } });
    var body = groups.map(function (thr) {
      var list = b.rows.filter(function (r) { return r.thr === thr; });
      var head = '<tr class="ls-group' + (open(list[0]) ? "" : " locked") + '"><td colspan="2">' +
        (thr > 0 ? "Over " + Math.round(thr * 100) + "% of the damage" : "Anyone who hit it") +
        "<small>" + list.length + " rolls</small></td></tr>";
      return head + list.map(function (r) { return "<tr>" + itemCell(r) + rateCell(r) + "</tr>"; }).join("");
    }).join("");
    return '<div class="table-wrap"><table class="ls-table"><thead><tr><th>Item</th><th class="ls-rate">Your chance a kill</th></tr></thead><tbody>' +
      body + "</tbody></table></div>";
  }

  // --- the bags -------------------------------------------------------------------------------
  function bagsView(b) {
    var s = bagStats(b.rows);
    var used = byRank.filter(function (g) { return s.items[g] > 0; }).reverse();
    function line(l, v) { return "<tr><td>" + l + '</td><td class="ls-rate">' + v + "</td></tr>"; }
    var html = '<tr class="ls-group"><td colspan="2">Bags on the ground<small>one player, one kill</small></td></tr>' +
      line("Items, on average", s.mean.toFixed(1) + "<span>" + s.meanBags.toFixed(2) + " bags</span>") +
      line("1 bag (1 to 8 items)", pct(s.bags[1])) + line("2 bags (9 to 16 items)", pct(s.bags[2])) +
      line("3 or more bags", pct(s.bags[3])) +
      '<tr class="ls-group"><td colspan="2">The colour of the kill\'s bags<small>every bag of a kill takes its best item\'s colour</small></td></tr>';
    used.filter(function (g) { return s.colour[g] > 0.0005; }).forEach(function (g) {
      html += line(bagIco(g) + " " + BAGNAME[g], pct(s.colour[g]));
    });
    html += '<tr class="ls-group"><td colspan="2">Items by their own bag<small>how many a kill gives, and the chance of at least one</small></td></tr>';
    used.forEach(function (g) {
      html += line(bagIco(g) + " " + BAGNAME[g] + " items", s.items[g].toFixed(2) + "<span>" + pct(s.atLeast[g]) + " for at least one</span>");
    });
    return '<div class="table-wrap"><table class="ls-table"><tbody>' + html + "</tbody></table></div>";
  }

  // --- the page: the controls are drawn once (a slider redrawn under the finger stops dragging),
  // the rest again on every change -------------------------------------------------------------
  root.innerHTML = '<div class="ls-bosses">' + DATA.bosses.map(function (x) {
    return '<button type="button" class="ls-boss" data-boss="' + x.key + '">' + x.ico + '<span><span class="n">' +
      esc(x.short) + '</span><span class="s">' + esc(x.title) + "<br>" + esc(x.world) + "</span></span></button>";
  }).join("") + "</div>" +
    '<div class="ls-controls"><label class="ls-share">Your damage <input type="range" id="ls-share" min="1" max="100" value="' +
    Math.round(state.share * 100) + '"><output id="ls-share-out"></output></label><div class="ls-boosts">' +
    BOOSTS.map(function (x) {
      return '<label class="ls-chk"><input type="checkbox" data-boost="' + x.id + '"' + (state.boosts[x.id] ? " checked" : "") +
        "><span>" + x.label + " +" + Math.round(x.v * 100) + "%</span></label>";
    }).join("") + '</div><div class="ls-mult" id="ls-mult"></div></div>' +
    '<p class="ls-head" id="ls-head"></p><div class="ls-tabs">' +
    [["sim", "Simulator"], ["drops", "Drop chances"], ["bags", "Bags per kill"]].map(function (t) {
      return '<button type="button" class="ls-tab" data-view="' + t[0] + '">' + t[1] + "</button>";
    }).join("") + '</div><div class="ls-view" id="ls-view"></div>';

  function render() {
    var b = BOSS[state.boss];
    root.querySelectorAll("[data-boss]").forEach(function (x) { x.setAttribute("aria-pressed", x.dataset.boss === state.boss); });
    root.querySelectorAll("[data-view]").forEach(function (x) { x.setAttribute("aria-pressed", x.dataset.view === state.view); });
    document.getElementById("ls-share-out").textContent = Math.round(state.share * 100) + "%";
    document.getElementById("ls-mult").innerHTML = "chances &times;<b>" + mult().toFixed(2) + "</b>";
    document.getElementById("ls-head").innerHTML = "<b>" + esc(b.title) + "</b> &middot; " + esc(b.world) + " &middot; " +
      b.hp.toLocaleString() + " health &middot; " + b.rows.length + " rolls a kill";
    document.getElementById("ls-view").innerHTML =
      state.view === "drops" ? dropsView(b) : state.view === "bags" ? bagsView(b) : simView(b);
  }
  function go() { history.replaceState(null, "", "#" + state.boss + "-" + state.view); render(); }

  root.addEventListener("click", function (e) {
    var t = e.target.closest("button");
    if (!t) return;
    if (t.dataset.boss) state.boss = t.dataset.boss;
    else if (t.dataset.view) state.view = t.dataset.view;
    else if (t.dataset.kills) state.kills = +t.dataset.kills;
    else if (t.id === "ls-roll") state.seed = Math.floor(Math.random() * 1e9);
    else return;
    go();
  });
  root.addEventListener("input", function (e) {
    if (e.target.id === "ls-share") { state.share = e.target.value / 100; render(); }
  });
  root.addEventListener("change", function (e) {
    if (e.target.dataset.boost) { state.boosts[e.target.dataset.boost] = e.target.checked; render(); }
    else if (e.target.id === "ls-kills") {
      state.kills = Math.max(1, Math.min(1000, Math.round(+e.target.value) || 10)); render();
    }
  });
  render();
})();
