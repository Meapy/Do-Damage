// The wiki's live parts: the search over every page (window.WIKI_INDEX, written by
// tools/build_wiki.py), the filters on the dungeon, pet and gear lists, the tabs on the class gear
// pages, and the page menu on phones.
(function () {
  "use strict";

  // --- The page menu: open beside the page on wide screens, folded on narrow ones -----------------
  var nav = document.querySelector(".wiki-nav");
  var narrow = matchMedia("(max-width: 900px)");
  function fold() { if (nav) nav.open = !narrow.matches; }
  fold();
  if (narrow.addEventListener) narrow.addEventListener("change", fold);

  // --- Search ---------------------------------------------------------------------------------
  var input = document.getElementById("wiki-q");
  var list = document.getElementById("wiki-results");
  var index = window.WIKI_INDEX || [];
  var picked = -1;
  // the index's links are from the wiki's root; pages in gear/ and classes/ are one folder down
  var root = document.body.getAttribute("data-wiki") || "";

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

  function mark(text, terms) {
    var div = document.createElement("div");
    div.textContent = text;
    var safe = div.innerHTML;
    terms.forEach(function (t) {
      var re = new RegExp("(" + t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + ")", "ig");
      safe = safe.replace(re, "<mark>$1</mark>");
    });
    return safe;
  }

  function show() {
    var terms = words(input.value);
    list.innerHTML = "";
    picked = -1;
    if (!terms.length) { list.hidden = true; return; }
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
      a.innerHTML = '<span class="r-head">' + mark(h.e.h, terms) + '</span><span class="r-page">' +
        mark(h.e.p, []) + '</span><span class="r-text">' + mark(snippet(h.e.t, terms), terms) + "</span>";
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
    input.addEventListener("focus", function () { if (input.value) show(); });
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
})();
