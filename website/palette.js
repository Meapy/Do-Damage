// Quick search on every page: press Ctrl+K (or / outside a text box), or the Search button in the top
// bar. It reads the wiki's search index (wiki/search-index.js, loaded the first time anyone searches,
// here or in the wiki's own box) and opens the page you pick. The site's address is taken from where
// this script itself was loaded.
(function () {
  "use strict";
  var me = document.currentScript;
  var base = me && me.src ? me.src.replace(/[^\/]*$/, "") : "";
  // the index's address, with its own ?v= (the page gives it); the wiki's own search uses it too
  function safeIndexUrl(raw, fallback) {
    if (!raw) return fallback;
    try {
      var u = new URL(raw, document.baseURI);
      if ((u.protocol === "http:" || u.protocol === "https:") && u.origin === window.location.origin) {
        return u.href;
      }
    } catch (e) {}
    return fallback;
  }
  var indexUrl = safeIndexUrl(me && me.getAttribute("data-index"), base + "wiki/search-index.js");
  var dialog, input, list, index = null, picked = -1, waiting = [], loading = false;

  // window.ddWikiIndex(callback): loads the search index once, the first time anyone searches
  window.ddWikiIndex = function (done) {
    if (window.WIKI_INDEX) { done(window.WIKI_INDEX); return; }
    waiting.push(done);
    if (loading) return;
    loading = true;
    var s = document.createElement("script");
    s.src = indexUrl;
    s.onload = function () { var all = waiting; waiting = []; all.forEach(function (f) { f(window.WIKI_INDEX || []); }); };
    s.onerror = function () { loading = false; };
    document.head.appendChild(s);
  };
  if (typeof HTMLDialogElement !== "function") return;  // the wiki's own box still searches

  function words(q) {
    return q.toLowerCase().replace(/[^\w\s'-]+/g, " ").split(/\s+/).filter(function (w) { return w.length > 1 || /\d/.test(w); });
  }
  function score(e, terms) {
    var head = e.h.toLowerCase(), page = e.p.toLowerCase(), text = e.t.toLowerCase(), total = 0;
    if (terms.length > 1) {
      var phrase = terms.join(" ");
      if (head.indexOf(phrase) >= 0) total += 20;
      if (text.indexOf(phrase) >= 0) total += 10;
    }
    for (var i = 0; i < terms.length; i++) {
      var t = terms[i], s = 0;
      if (head.indexOf(t) >= 0) s += head.indexOf(t) === 0 ? 12 : 8;
      if (page.indexOf(t) >= 0) s += 2;
      if (text.indexOf(t) >= 0) s += 3;
      if (!s) return 0;
      total += s;
    }
    return total;
  }
  function node(cls, text) {
    var s = document.createElement("span");
    s.className = cls;
    s.textContent = text;
    return s;
  }
  function snippet(text, terms) {
    var low = text.toLowerCase(), at = -1;
    for (var i = 0; i < terms.length && at < 0; i++) at = low.indexOf(terms[i]);
    var start = Math.max(0, at - 40);
    return (start ? "…" : "") + text.slice(start, start + 120);
  }

  function render() {
    var terms = words(input.value);
    list.textContent = "";
    picked = -1;
    if (!terms.length) {
      var hint = document.createElement("li");
      hint.className = "p-hint";
      hint.textContent = "Type an item, boss, dungeon, class or command.";
      list.appendChild(hint);
      return;
    }
    if (!index) {
      var wait = document.createElement("li");
      wait.className = "p-hint";
      wait.textContent = "Loading the wiki…";
      list.appendChild(wait);
      return;
    }
    var hits = [];
    index.forEach(function (e) { var s = score(e, terms); if (s) hits.push({ e: e, s: s }); });
    hits.sort(function (a, b) { return b.s - a.s; });
    hits.slice(0, 10).forEach(function (h) {
      var li = document.createElement("li"), a = document.createElement("a");
      a.href = base + "wiki/" + h.e.u;
      var head = node("p-head", h.e.h);
      head.appendChild(node("p-page", h.e.p));
      a.appendChild(head);
      a.appendChild(node("p-text", snippet(h.e.t, terms)));
      li.appendChild(a);
      list.appendChild(li);
    });
    if (!hits.length) {
      var none = document.createElement("li");
      none.className = "p-none";
      none.textContent = "Nothing found. Try fewer or other words.";
      list.appendChild(none);
    }
  }

  function load() {
    if (!index) window.ddWikiIndex(function (all) { index = all; if (dialog && dialog.open) render(); });
  }

  function move(step) {
    var links = list.querySelectorAll("a");
    if (!links.length) return;
    picked = (picked + step + links.length) % links.length;
    for (var i = 0; i < links.length; i++) links[i].classList.toggle("on", i === picked);
    links[picked].scrollIntoView({ block: "nearest" });
  }

  function open() {
    if (!dialog) {
      dialog = document.createElement("dialog");
      dialog.className = "palette";
      dialog.setAttribute("aria-label", "Search the wiki");
      dialog.innerHTML = '<input type="search" placeholder="Search the wiki…" autocomplete="off" spellcheck="false" aria-label="Search the wiki"><ol></ol>';
      document.body.appendChild(dialog);
      input = dialog.querySelector("input");
      list = dialog.querySelector("ol");
      input.addEventListener("input", render);
      input.addEventListener("keydown", function (e) {
        if (e.key === "ArrowDown") { move(1); e.preventDefault(); }
        else if (e.key === "ArrowUp") { move(-1); e.preventDefault(); }
        else if (e.key === "Enter") {
          var links = list.querySelectorAll("a");
          var go = links[picked >= 0 ? picked : 0];
          if (go) location.href = go.href;
        }
      });
      dialog.addEventListener("click", function (e) { if (e.target === dialog) dialog.close(); });
    }
    load();
    input.value = "";
    render();
    if (!dialog.open) dialog.showModal();
    input.focus();
  }

  // the button, added to the top bar before "Play now"
  var bar = document.querySelector(".bar");
  var play = bar && bar.querySelector(".pill-play");
  if (bar && play) {
    var b = document.createElement("button");
    b.type = "button";
    b.className = "quick";
    b.innerHTML = 'Search <kbd>Ctrl</kbd><kbd>K</kbd>';
    b.setAttribute("aria-label", "Search the wiki");
    b.addEventListener("click", open);
    bar.insertBefore(b, play);
  }
  document.addEventListener("keydown", function (e) {
    var typing = /^(INPUT|TEXTAREA|SELECT)$/.test((e.target.tagName || "")) || e.target.isContentEditable;
    if ((e.key === "k" || e.key === "K") && (e.ctrlKey || e.metaKey)) { e.preventDefault(); open(); }
    // on the wiki, "/" goes to the search box beside the page (wiki.js)
    else if (e.key === "/" && !typing && !e.ctrlKey && !e.metaKey && !document.getElementById("wiki-q")) { e.preventDefault(); open(); }
  });
})();
