// The wiki's live parts: the search over every page (window.WIKI_INDEX, written by
// tools/build_wiki.py and loaded by palette.js the first time someone searches), the filters on the
// dungeon, pet and gear lists, the tabs on the class gear pages, the page menu on phones, the
// "On this page" list following the reader, and the button back to the top.
(function () {
  "use strict";

  // --- The page menu: open beside the page on wide screens, folded on narrow ones -----------------
  var nav = document.querySelector(".wiki-nav");
  var narrow = matchMedia("(max-width: 900px)");
  function fold() { if (nav) nav.open = !narrow.matches; }
  fold();
  if (narrow.addEventListener) narrow.addEventListener("change", fold);
  // the list scrolls on its own beside the page: start it at this page
  var here = nav && nav.querySelector("[aria-current]");
  var navBox = nav && nav.querySelector("nav");
  if (here && navBox && !narrow.matches && navBox.scrollHeight > navBox.clientHeight) {
    var at = here.getBoundingClientRect().top - navBox.getBoundingClientRect().top;
    navBox.scrollTop = Math.max(0, at - navBox.clientHeight / 3);
  }

  // --- Search ---------------------------------------------------------------------------------
  var input = document.getElementById("wiki-q");
  var list = document.getElementById("wiki-results");
  var index = window.WIKI_INDEX || null;
  var picked = -1;
  function need() {
    if (index) return;
    var got = function (all) { index = all; if (input && input.value) show(); };
    if (window.ddWikiIndex) { window.ddWikiIndex(got); return; }
    var s = document.createElement("script");  // palette.js didn't load: fetch it here
    s.src = root + "search-index.js";
    s.onload = function () { got(window.WIKI_INDEX || []); };
    document.head.appendChild(s);
  }
  // the index's links are from the wiki's root; pages in gear/ and classes/ are one folder down
  // (only ever "" or "../" steps: anything else in the attribute is not used as a link)
  var root = document.body.getAttribute("data-wiki") || "";
  if (!/^(\.\.\/)*$/.test(root)) root = "";

  function words(q) {
    return q.toLowerCase().replace(/[^\w\s'-]+/g, " ").split(/\s+/).filter(function (w) { return w.length > 1 || /\d/.test(w); });
  }

  function score(entry, terms) {
    var head = entry.h.toLowerCase(), page = entry.p.toLowerCase(), text = entry.t.toLowerCase();
    var total = 0;
    // the words together, as typed, count most
    var phrase = terms.join(" ");
    if (terms.length > 1) {
      if (head.indexOf(phrase) >= 0) total += 20;
      if (text.indexOf(phrase) >= 0) total += 10;
    }
    for (var i = 0; i < terms.length; i++) {
      var t = terms[i], s = 0;
      if (head.indexOf(t) >= 0) s += head.indexOf(t) === 0 ? 12 : 8;
      if (page.indexOf(t) >= 0) s += 2;
      var at = text.indexOf(t);
      if (at >= 0) s += 3 + Math.min(4, text.split(t).length - 1);
      if (!s) return 0;  // every word must be found somewhere
      total += s;
    }
    return total;
  }

  function snippet(text, terms) {
    var low = text.toLowerCase(), at = -1;
    for (var i = 0; i < terms.length && at < 0; i++) at = low.indexOf(terms[i]);
    var start = Math.max(0, at - 50);
    var cut = text.slice(start, start + 150);
    return (start > 0 ? "…" : "") + cut + (start + 150 < text.length ? "…" : "");
  }

  // a span of the text with the words found in <mark>, built as nodes so no text is read as HTML
  function mark(cls, text, terms) {
    var span = document.createElement("span");
    span.className = cls;
    var found = terms.slice().sort(function (a, b) { return b.length - a.length; })
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

  function show() {
    var terms = words(input.value);
    list.innerHTML = "";
    picked = -1;
    if (!terms.length) { list.hidden = true; return; }
    if (!index) {
      need();
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
      none.textContent = "Nothing found. Try fewer or other words.";
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

  if (input && list) {
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
      if (!e.target.closest(".wiki-search")) list.hidden = true;
    });
    // "/" jumps to the search, as on many sites
    document.addEventListener("keydown", function (e) {
      if (e.key === "/" && document.activeElement !== input && !/INPUT|TEXTAREA/.test(document.activeElement.tagName)) {
        input.focus();
        e.preventDefault();
      }
    });
    var q = new URLSearchParams(location.search).get("q");
    if (q) { input.value = q; show(); }
  }

  // --- "On this page": the section being read is marked, and the list keeps it in view -------------
  var heads = [], byId = {};
  document.querySelectorAll(".toc a[href^='#']").forEach(function (a) {
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
    var box = a.closest("ul");
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
