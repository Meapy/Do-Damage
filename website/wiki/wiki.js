// The wiki's live parts: the search over every page (window.WIKI_INDEX, written by
// tools/build_wiki.py and loaded by palette.js the first time someone searches), the sections that
// fold, the long tables' filters and sorting, the filters on the dungeon, pet and gear lists, the tabs
// on the class gear pages, the page menu on phones, the page's parts in the page list following the
// reader, and the button back to the top.
(function () {
  "use strict";

  // --- The page menu: open beside the page on wide screens, folded on narrow ones -----------------
  var nav = document.querySelector(".wiki-nav");
  var narrow = matchMedia("(max-width: 900px)");
  function fold() { if (nav) nav.open = !narrow.matches; }
  fold();
  if (narrow.addEventListener) narrow.addEventListener("change", fold);
  // its sections fold too: this page's starts open, and the others the reader opened stay open
  var OPENED = "dd-wiki-open";
  var opened = {};
  try { opened = JSON.parse(localStorage.getItem(OPENED) || "{}") || {}; } catch (e) { opened = {}; }
  document.querySelectorAll(".nav-sec[data-sec]").forEach(function (sec) {
    var id = sec.getAttribute("data-sec");
    if (opened[id]) sec.open = true;
    sec.addEventListener("toggle", function () {
      if (sec.classList.contains("here")) return;
      if (sec.open) opened[id] = 1; else delete opened[id];
      try { localStorage.setItem(OPENED, JSON.stringify(opened)); } catch (e) {}
    });
  });
  // the list scrolls on its own beside the page: start it at this page
  var here = nav && nav.querySelector("[aria-current]");
  var navBox = nav && nav.querySelector("nav");
  if (here && navBox && !narrow.matches && navBox.scrollHeight > navBox.clientHeight) {
    var at = here.getBoundingClientRect().top - navBox.getBoundingClientRect().top;
    navBox.scrollTop = Math.max(0, at - navBox.clientHeight / 3);
  }

  // --- Search ---------------------------------------------------------------------------------
  // The box beside every page and the big one on the wiki's home work the same way.
  var index = window.WIKI_INDEX || null;
  var waiting = [];
  function need() {
    if (index) return;
    var got = function (all) { index = all; waiting.forEach(function (f) { f(); }); waiting = []; };
    if (window.ddWikiIndex) { window.ddWikiIndex(got); return; }
    var s = document.createElement("script");  // palette.js didn't load: fetch it here
    s.src = root + "search-index.js";
    s.onload = function () { got(window.WIKI_INDEX || []); };
    document.head.appendChild(s);
  }
  // the index's links are from the wiki's root; pages in gear/ and classes/ are one folder down
  // derive only the needed prefix from location instead of reading it from DOM text
  var path = (window.location && window.location.pathname) || "";
  var root = /\/wiki\/(?:gear|classes)\//.test(path) ? "../" : "";

  // words that say nothing about what's wanted ("how do I get potions" looks for "potions")
  var STOP = " a an and are can do does for from get how i in is it me my of on or the to what where which who why with you your ";
  function words(q) {
    return q.toLowerCase().replace(/[^\w\s'-]+/g, " ").split(/\s+/).filter(function (w) {
      return (w.length > 1 || /\d/.test(w)) && STOP.indexOf(" " + w + " ") < 0;
    });
  }
  // "potions" finds "potion", "dungeons" finds "dungeon"
  function stem(w) { return w.length > 4 && /s$/.test(w) && !/ss$/.test(w) ? w.replace(/(es|s)$/, "") : w; }

  function score(entry, terms) {
    var head = entry.h.toLowerCase(), page = entry.p.toLowerCase(), text = entry.t.toLowerCase();
    var total = 0, missed = 0;
    // the words together, as typed, count most
    var phrase = terms.join(" ");
    if (terms.length > 1) {
      if (head.indexOf(phrase) >= 0) total += 20;
      if (text.indexOf(phrase) >= 0) total += 10;
    }
    if (head === phrase) total += 30;
    for (var i = 0; i < terms.length; i++) {
      var t = stem(terms[i]), s = 0;
      if (head.indexOf(t) >= 0) s += head.indexOf(t) === 0 ? 12 : 8;
      if (page.indexOf(t) >= 0) s += 2;
      var at = text.indexOf(t);
      if (at >= 0) s += 3 + Math.min(4, text.split(t).length - 1);
      if (!s) missed++;
      total += s;
    }
    // every word should be found; one missing out of three or more is let off, ranked lower
    if (missed && (terms.length < 3 || missed > 1)) return 0;
    return missed ? total / 2 : total;
  }

  function snippet(text, terms) {
    var low = text.toLowerCase(), at = -1;
    for (var i = 0; i < terms.length && at < 0; i++) at = low.indexOf(stem(terms[i]));
    var start = Math.max(0, at - 50);
    var cut = text.slice(start, start + 150);
    return (start > 0 ? "…" : "") + cut + (start + 150 < text.length ? "…" : "");
  }

  // a span of the text with the words found in <mark>, built as nodes so no text is read as HTML
  function mark(cls, text, terms) {
    var span = document.createElement("span");
    span.className = cls;
    var found = terms.map(stem).sort(function (a, b) { return b.length - a.length; })
      .map(function (t) { return t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); });
    var parts = found.length ? text.split(new RegExp("(" + found.join("|") + ")", "i")) : [text];
    parts.forEach(function (part, i) {
      if (!part) return;
      if (i % 2) {
        var m = document.createElement("mark");
        m.textContent = part;
        span.appendChild(m);
      } else {
        span.appendChild(document.createTextNode(part));
      }
    });
    return span;
  }

  function searchBox(input, list) {
    var picked = -1;
    function show() {
      var terms = words(input.value);
      list.innerHTML = "";
      picked = -1;
      if (!terms.length) { list.hidden = true; return; }
      if (!index) {
        waiting.push(show);
        need();
        if (index) return;  // it was loaded already, and show() has run again with it
        var wait = document.createElement("li");
        wait.className = "none";
        wait.textContent = "Loading the search…";
        list.appendChild(wait);
        list.hidden = false;
        return;
      }
      var hits = [];
      index.forEach(function (e) {
        var s = score(e, terms);
        if (s) hits.push({ e: e, s: s });
      });
      hits.sort(function (a, b) { return b.s - a.s; });
      hits = hits.slice(0, 12);
      if (!hits.length) {
        var none = document.createElement("li");
        none.className = "none";
        none.textContent = "Nothing found. Try one word, like a dungeon, item, class or command.";
        list.appendChild(none);
      }
      hits.forEach(function (h) {
        var li = document.createElement("li");
        var a = document.createElement("a");
        a.href = root + h.e.u;
        a.appendChild(mark("r-head", h.e.h, terms));
        a.appendChild(mark("r-page", h.e.p, []));
        a.appendChild(mark("r-text", snippet(h.e.t, terms), terms));
        li.appendChild(a);
        list.appendChild(li);
      });
      list.hidden = false;
    }

    function move(step) {
      var links = list.querySelectorAll("a");
      if (!links.length) return;
      picked = (picked + step + links.length) % links.length;
      links.forEach(function (l, i) { l.classList.toggle("on", i === picked); });
      links[picked].scrollIntoView({ block: "nearest" });
    }

    input.addEventListener("input", show);
    input.addEventListener("focus", function () { need(); if (input.value) show(); });
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { move(1); e.preventDefault(); }
      else if (e.key === "ArrowUp") { move(-1); e.preventDefault(); }
      else if (e.key === "Enter") {
        var links = list.querySelectorAll("a");
        var go = links[picked >= 0 ? picked : 0];
        if (go) location.href = go.href;
      } else if (e.key === "Escape") { input.value = ""; show(); input.blur(); }
    });
    document.addEventListener("click", function (e) {
      if (!list.parentNode.contains(e.target)) list.hidden = true;
    });
    return show;
  }

  // the home page's big box, and its "try" words that fill it
  var homeQ = document.getElementById("wiki-home-q");
  if (homeQ) {
    var homeShow = searchBox(homeQ, document.getElementById("wiki-home-results"));
    document.querySelectorAll("[data-try]").forEach(function (b) {
      b.addEventListener("click", function () {
        homeQ.value = b.getAttribute("data-try");
        homeQ.focus();
        homeShow();
      });
    });
  }

  var input = document.getElementById("wiki-q");
  var list = document.getElementById("wiki-results");
  if (input && list) {
    var showSide = searchBox(input, list);
    // "/" jumps to the search, as on many sites (the home page's big box, on the home page)
    document.addEventListener("keydown", function (e) {
      if (e.key === "/" && !/INPUT|TEXTAREA/.test(document.activeElement.tagName)) {
        var box = homeQ && homeQ.getBoundingClientRect().bottom > 0 ? homeQ : input;
        if (box === input && narrow.matches && nav) nav.open = true;
        box.focus();
        e.preventDefault();
      }
    });
    var q = new URLSearchParams(location.search).get("q");
    if (q) { input.value = q; showSide(); }
  }

  // --- "On this page": the section being read is marked, and the list keeps it in view -------------
  var heads = [], byId = {};
  document.querySelectorAll(".nav-toc a[href^='#']").forEach(function (a) {
    var h = document.getElementById(decodeURIComponent(a.getAttribute("href").slice(1)));
    if (h) { byId[h.id] = a; heads.push(h); }
  });
  var lit = null, queued = false;
  function markToc() {
    queued = false;
    // the last heading above a line a third of the way down the window
    var line = innerHeight / 3, now = null;
    for (var i = 0; i < heads.length; i++) {
      if (heads[i].getBoundingClientRect().top <= line) now = heads[i]; else break;
    }
    var a = now ? byId[now.id] : null;
    if (a === lit) return;
    if (lit) lit.removeAttribute("aria-current");
    lit = a;
    if (!a) return;
    a.setAttribute("aria-current", "true");
    var box = navBox;
    if (box && box.scrollHeight > box.clientHeight) {
      var top = a.getBoundingClientRect().top - box.getBoundingClientRect().top;
      if (top < 0 || top > box.clientHeight - 30) box.scrollTop += top - box.clientHeight / 3;
    }
  }

  // --- Back to the top, once the page has been scrolled a long way ---------------------------------
  var toTop = document.createElement("button");
  toTop.type = "button";
  toTop.className = "to-top";
  toTop.setAttribute("aria-label", "Back to the top");
  toTop.textContent = "↑";
  toTop.hidden = true;
  toTop.addEventListener("click", function () {
    var still = matchMedia("(prefers-reduced-motion: reduce)").matches;
    scrollTo({ top: 0, behavior: still ? "auto" : "smooth" });
  });
  document.body.appendChild(toTop);

  addEventListener("scroll", function () {
    toTop.hidden = scrollY < innerHeight * 1.5;
    if (heads.length && !queued) { queued = true; requestAnimationFrame(markToc); }
  }, { passive: true });
  if (heads.length) markToc();

  // --- Tabs on a class's gear page: one panel at a time, picked by the address's #weapon, #ability...
  // Without the script every panel shows, one under the other. --------------------------------------
  var panels = document.querySelectorAll(".tab-panel");
  var tabs = document.querySelectorAll(".tabs-in a[href^='#']");
  function showPanel(id, scroll) {
    var target = id && document.getElementById(id);
    var panel = target && (target.classList.contains("tab-panel") ? target : target.closest(".tab-panel"));
    if (!panel) panel = panels[0];
    panels.forEach(function (p) { p.hidden = p !== panel; });
    tabs.forEach(function (t) { t.setAttribute("aria-selected", t.getAttribute("href") === "#" + panel.id ? "true" : "false"); });
    if (scroll && target && target !== panel) target.scrollIntoView();
  }
  if (panels.length) {
    showPanel(location.hash.slice(1), true);
    tabs.forEach(function (t) {
      t.addEventListener("click", function (e) {
        e.preventDefault();
        var id = t.getAttribute("href").slice(1);
        history.replaceState(null, "", "#" + id);
        showPanel(id, false);
      });
    });
    addEventListener("hashchange", function () { showPanel(location.hash.slice(1), true); });
  }

  // --- Sections that fold: each of a page's sections is a panel that opens and closes, and a long
  // page starts as an outline of them (the short ones open), with a line on what each holds. Inside, a
  // run of tables or card lists under their own headings becomes a list that opens one at a time, and a
  // long table shows its first rows, with a filter and columns that sort. What the reader opens stays
  // open on their next visit; a link, a search result or the browser's find opens what it points into.
  // Without the script the page is as written, every section open. ----------------------------------
  var article = document.querySelector("article.prose");
  var folds = [];
  var FOLDS = "dd-wiki-folds", here = location.pathname.replace(/\/index\.html$/, "/");
  var saved = {};
  try { saved = (JSON.parse(localStorage.getItem(FOLDS) || "{}") || {})[here] || {}; } catch (e) { saved = {}; }
  function remember(id, open) {
    try {
      var all = JSON.parse(localStorage.getItem(FOLDS) || "{}") || {};
      (all[here] = all[here] || {})[id] = open ? 1 : 0;
      localStorage.setItem(FOLDS, JSON.stringify(all));
    } catch (e) {}
  }
  function count(n, one, many) { return n.toLocaleString("en") + " " + (n === 1 ? one : many); }
  function isHead(node, upTo) { return node.nodeType === 1 && /^H[2-6]$/.test(node.tagName) && +node.tagName[1] <= upTo; }

  // what a section or group holds, in a few words: "153 entries", "61 rows", "3 tables", "5 topics"
  function holds(body) {
    var bits = [];
    var cards = body.querySelectorAll(".it, .dg, .cls, .nx-card").length;
    var tables = body.querySelectorAll(".table-wrap table");
    var topics = [].filter.call(body.querySelectorAll("h3"), function (h) { return !h.closest(".it, .dg, .cls, .nx-card"); }).length;
    if (cards) bits.push(count(cards, "entry", "entries"));
    if (tables.length === 1) bits.push(count(tables[0].tBodies[0] ? tables[0].tBodies[0].rows.length : 0, "row", "rows"));
    else if (tables.length && tables.length > topics) bits.push(count(tables.length, "table", "tables"));
    if (topics > 1) bits.push(count(topics, "topic", "topics"));
    return bits;
  }
  // the section's first sentence, shown while it's closed
  function gist(body) {
    var p = body.querySelector(":scope > p, :scope > ul > li");
    if (!p) return "";
    var t = p.textContent.replace(/\s+/g, " ").trim();
    // whole sentences, enough to say what the section is about
    var m = t.match(/^.{80,}?[.!?](?=\s|$)/) || t.match(/^.{24,}?[.!?](?=\s|$)/);
    t = m ? m[0] : t;
    return t.length > 170 ? t.slice(0, 168).replace(/\s+\S*$/, "") + "…" : t;
  }
  // a heading and what follows it, up to the next heading as big or bigger, into <details>
  function wrapFold(head, cls, lead) {
    var level = +head.tagName[1];
    var d = document.createElement("details");
    d.className = cls;
    var sum = document.createElement("summary");
    var body = document.createElement("div");
    body.className = cls + "-body";
    head.parentNode.insertBefore(d, head);
    sum.appendChild(head);
    var node = d.nextSibling;
    while (node && !isHead(node, level)) {
      var next = node.nextSibling;
      body.appendChild(node);
      node = next;
    }
    var info = document.createElement("span");
    info.className = cls + "-info";
    holds(body).forEach(function (b) {
      var chip = document.createElement("span");
      chip.textContent = b;
      info.appendChild(chip);
    });
    sum.appendChild(info);
    if (lead) {
      var g = gist(body);
      if (g) {
        var p = document.createElement("span");
        p.className = cls + "-gist";
        p.textContent = g;
        sum.appendChild(p);
      }
    }
    d.appendChild(sum);
    d.appendChild(body);
    return d;
  }
  function heavy(node) {
    return node.nodeType === 1 && (node.matches(".table-wrap, .its, .dgs, .shops, .nx, .its-group") ||
      !!node.querySelector(".table-wrap, .its, .dgs"));
  }
  // three or more headings at one level, most of them over a table or a card list: a list that opens
  // one at a time; then the same a level down inside each
  function accordion(box, level) {
    if (level > 5) return;
    var heads = [].filter.call(box.children, function (c) { return c.tagName === "H" + level; });
    if (heads.length >= 3) {
      var withData = heads.filter(function (h) {
        for (var n = h.nextSibling; n && !isHead(n, level); n = n.nextSibling) if (heavy(n)) return true;
        return false;
      });
      if (withData.length >= 3 && withData.length * 3 >= heads.length * 2) {
        heads.forEach(function (h) { accordion(wrapFold(h, "acc", false).lastChild, level + 1); });
        return;
      }
    }
    accordion(box, level + 1);
  }

  // --- a long table: its first rows, a filter, and columns that sort ---------------------------------
  var SHOW_ROWS = 8;
  function numberIn(text) {
    var m = text.replace(/,/g, "").match(/[-+−]?\d+(\.\d+)?/);
    return m ? parseFloat(m[0].replace("−", "-")) : null;
  }
  function tableTools(wrap) {
    var t = wrap.querySelector("table");
    var tb = t && t.tBodies[0];
    if (!tb || !t.tHead || t.classList.contains("q-track") || wrap.classList.contains("fr-tiers")) return;
    var rows = [].slice.call(tb.rows);
    if (rows.length < 6 || t.querySelector("[rowspan], [colspan]")) return;
    var inAcc = !!wrap.closest(".acc");
    var limit = rows.length > SHOW_ROWS + 4 && !inAcc;
    var query = [];
    var bar = null, more = null, shown = null, box = null;

    function lay() {
      var n = 0, matched = 0;
      [].forEach.call(tb.rows, function (r) {
        var hit = !query.length || query.every(function (q) { return r._text.indexOf(q) >= 0; });
        if (hit) matched++;
        var show = hit && (!limit || query.length || wrap.classList.contains("all") || n < SHOW_ROWS);
        if (hit) n++;
        r.hidden = !show;
      });
      if (shown) shown.textContent = query.length ? matched + " of " + rows.length : count(rows.length, "row", "rows");
      if (more) more.hidden = !limit || !!query.length || wrap.classList.contains("all");
    }
    rows.forEach(function (r, i) { r._at = i; r._text = r.textContent.toLowerCase(); });

    if (rows.length >= 12) {
      bar = document.createElement("div");
      bar.className = "tbl-tools";
      box = document.createElement("input");
      box.type = "search";
      box.placeholder = "Filter these rows";
      box.setAttribute("aria-label", "Filter the table's rows");
      shown = document.createElement("span");
      shown.className = "tbl-count";
      bar.appendChild(box);
      bar.appendChild(shown);
      wrap.parentNode.insertBefore(bar, wrap);
      box.addEventListener("input", function () { query = words(box.value); lay(); });
    }
    if (limit) {
      more = document.createElement("button");
      more.type = "button";
      more.className = "tbl-more";
      more.textContent = "Show all " + rows.length + " rows";
      more.addEventListener("click", function () { wrap.classList.add("all"); lay(); });
      wrap.parentNode.insertBefore(more, wrap.nextSibling);
    }
    // a click on a column's head sorts by it: down, up, then back as written
    [].forEach.call(t.tHead.rows[0].cells, function (th, col) {
      var nums = rows.filter(function (r) { return r.cells[col] && numberIn(r.cells[col].textContent) !== null; }).length;
      var numeric = nums * 2 > rows.length;
      th.classList.add("sortable");
      th.tabIndex = 0;
      th.title = "Sort by " + th.textContent.trim();
      function sort() {
        var now = th.getAttribute("aria-sort");
        var next = now === "descending" ? "ascending" : now === "ascending" ? null : "descending";
        [].forEach.call(t.tHead.rows[0].cells, function (c) { c.removeAttribute("aria-sort"); });
        var order = rows.slice();
        if (next) {
          var sign = next === "ascending" ? 1 : -1;
          order.sort(function (a, b) {
            var x = a.cells[col] ? a.cells[col].textContent.trim() : "", y = b.cells[col] ? b.cells[col].textContent.trim() : "";
            var c;
            if (numeric) {
              var nx = numberIn(x), ny = numberIn(y);
              c = (nx === null ? -Infinity : nx) - (ny === null ? -Infinity : ny);
            } else c = x.localeCompare(y, "en", { numeric: true });
            return c * sign || a._at - b._at;
          });
          th.setAttribute("aria-sort", next);
        }
        order.forEach(function (r) { tb.appendChild(r); });
        lay();
      }
      th.addEventListener("click", function (e) { if (!e.target.closest("a")) sort(); });
      th.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { sort(); e.preventDefault(); } });
    });
    lay();
  }

  function foldKey(f) { var h = f.querySelector("summary > :is(h2, h3)"); return h.id || h.textContent.trim(); }

  if (article && !document.body.classList.contains("wiki-home")) {
    // the page's sections, and on a gear page each group of items (its <section class="its-group">,
    // with an <h3> in a class's tab)
    [].slice.call(article.querySelectorAll(":scope > h2, :scope .its-group > h2, :scope .tab-panel > .its-group > h3:first-child")).forEach(function (c) {
      var f = wrapFold(c, "fold", true);
      f.open = true;
      folds.push(f);
    });
    folds.forEach(function (f) { accordion(f.lastChild, 3); });
    if (!folds.length) accordion(article, 2);
    article.querySelectorAll(".table-wrap").forEach(tableTools);
    // a filter box at the end of a section, over the lists of the sections after it, stands between
    // them, where it's seen with every section closed
    article.querySelectorAll("details.fold .filter").forEach(function (fb) {
      var input = fb.querySelector("input[data-filter]");
      var f = fb.closest("details.fold");
      if (input && !f.querySelector(input.getAttribute("data-filter"))) f.parentNode.insertBefore(fb, f.nextSibling);
    });

    // a long page starts as an outline of its sections, each with its first line; a short one open
    var tall = article.offsetHeight > innerHeight * 2.2;
    folds.forEach(function (f) {
      var id = foldKey(f);
      // (a class's tabs are each an outline too, though only one is shown)
      f.open = id in saved ? !!saved[id] : f.closest(".tab-panel") ? false : !tall || !f.lastChild.offsetHeight;
      f.firstChild.addEventListener("click", function (e) {
        if (e.target.closest("a")) return;
        setTimeout(function () { remember(id, f.open); }, 0);
      });
    });

    if (folds.length > 1) {
      var bar = document.createElement("div");
      bar.className = "fold-bar";
      var label = document.createElement("span");
      label.textContent = panels.length ? "Sections" : count(folds.length, "section", "sections");
      bar.appendChild(label);
      [["Open all", true], ["Close all", false]].forEach(function (b) {
        var btn = document.createElement("button");
        btn.type = "button";
        btn.textContent = b[0];
        btn.addEventListener("click", function () {
          folds.forEach(function (f) { f.open = b[1]; remember(foldKey(f), b[1]); });
        });
        bar.appendChild(btn);
      });
      var top = folds[0];
      while (top.parentNode !== article) top = top.parentNode;
      article.insertBefore(bar, top);
    }
  }

  // a link, a search result or the page list pointing into a closed section opens it
  function reveal(id, scroll) {
    var t = id && document.getElementById(id);
    if (!t) return;
    for (var d = t.closest("details"); d; d = d.parentElement && d.parentElement.closest("details")) d.open = true;
    if (t.tagName === "DETAILS") t.open = true;
    var w = t.closest(".table-wrap");
    if (w) { w.classList.add("all"); t.hidden = false; }
    if (scroll) t.scrollIntoView();
  }
  if (location.hash) reveal(decodeURIComponent(location.hash.slice(1)), true);
  addEventListener("hashchange", function () { reveal(decodeURIComponent(location.hash.slice(1)), true); });
  document.addEventListener("click", function (e) {
    var a = e.target.closest && e.target.closest("a[href^='#']");
    if (a && a.getAttribute("href").length > 1 && a.getAttribute("href") === location.hash)
      reveal(decodeURIComponent(a.getAttribute("href").slice(1)), true);
  });
  addEventListener("beforeprint", function () { document.querySelectorAll("details").forEach(function (d) { d.open = true; }); });

  // --- Filters on long lists: <input data-filter="selector"> hides what doesn't match ----------------
  document.querySelectorAll("input[data-filter]").forEach(function (box) {
    var items = document.querySelectorAll(box.getAttribute("data-filter"));
    box.addEventListener("input", function () {
      var terms = words(box.value);
      items.forEach(function (el) {
        var hay = el.getAttribute("data-search") || el.textContent.toLowerCase();
        el.hidden = !terms.every(function (t) { return hay.indexOf(t) >= 0; });
      });
      // a heading whose cards are all hidden goes too
      document.querySelectorAll(".dgs").forEach(function (group) {
        var any = group.querySelector(".dg:not([hidden])");
        group.hidden = !any;
        var h = group.previousElementSibling, p = h;
        if (p && p.tagName === "P") { p.hidden = !any; h = p.previousElementSibling; }
        if (h && h.tagName === "H2") h.hidden = !any;
      });
      // and so does a group of items with nothing left in it
      document.querySelectorAll(".its-group").forEach(function (group) {
        group.hidden = !group.querySelector(".it:not([hidden])");
      });
      // a section or group holding matches opens while the filter is on, and one with none goes away;
      // cleared, each goes back as it was
      var sel = box.getAttribute("data-filter");
      document.querySelectorAll("details.fold, details.acc").forEach(function (d) {
        if (!d.querySelector(sel)) return;
        if (terms.length) {
          var any = !!d.querySelector(sel + ":not([hidden])");
          d.hidden = !any && !d.contains(box);
          if (any) {
            if (!d.hasAttribute("data-was")) d.setAttribute("data-was", d.open ? "1" : "0");
            d.open = true;
          }
        } else {
          d.hidden = false;
          if (d.hasAttribute("data-was")) { d.open = d.getAttribute("data-was") === "1"; d.removeAttribute("data-was"); }
        }
      });
    });
  });

  // --- Kind chips: <button data-cat="x" data-cat-for="id"> sets data-cat on that element, and the
  // page's CSS shows only the cards of that kind (the Necromantic items) ------------------------------
  document.querySelectorAll("button[data-cat-for]").forEach(function (chip) {
    chip.addEventListener("click", function () {
      var box = document.getElementById(chip.getAttribute("data-cat-for"));
      if (!box) return;
      box.setAttribute("data-cat", chip.getAttribute("data-cat"));
      chip.parentNode.querySelectorAll("button[data-cat-for]").forEach(function (c) {
        c.setAttribute("aria-pressed", c === chip ? "true" : "false");
      });
    });
  });
})();
