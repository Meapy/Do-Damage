// The site's live parts: the server status from the account server's app/status (through the
// gateway's /api), the visitor's own download first, a warning when the browser can't run the
// game, and the pixel sprites. The 3D night is scene.js.
(function () {
  "use strict";

  // --- Server status ------------------------------------------------------------------------
  // POST api/app/serverList answers <Servers><Servers><Server>... with UsageText "3/85" (players
  // and room), or "- NEW!" for a server that just started; <Error> when none are up.
  var status = document.getElementById("status");
  var statusText = document.getElementById("status-text");

  function parseServers(xml) {
    var doc = new DOMParser().parseFromString(xml, "application/xml");
    var servers = [];
    var nodes = doc.getElementsByTagName("Server");
    for (var i = 0; i < nodes.length; i++) {
      var get = function (tag) {
        var el = nodes[i].getElementsByTagName(tag)[0];
        return el ? el.textContent : "";
      };
      if (/^true$/i.test(get("AdminOnly"))) continue;
      var m = /^(\d+)\s*\/\s*(\d+)$/.exec(get("UsageText"));
      servers.push({ name: get("Name"), players: m ? +m[1] : 0 });
    }
    return servers;
  }

  function showStatus(servers) {
    if (!status) return;
    status.classList.remove("online", "offline");
    if (servers === null) {
      statusText.textContent = "Server status unavailable";
      return;
    }
    if (servers.length === 0) {
      status.classList.add("offline");
      statusText.textContent = "The server is down for a moment";
      return;
    }
    var players = servers.reduce(function (n, s) { return n + s.players; }, 0);
    status.classList.add("online");
    statusText.textContent = "Online · " + players + (players === 1 ? " player" : " players") + " in the realm";
  }

  // GET api/app/status answers { online, players, peak24h, days: [{ date, up }] }: the last 7 UTC
  // days, oldest first, up the fraction of the day the realm was up (null: no data yet).
  var stats = document.getElementById("stats");
  var MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function showStats(s) {
    if (!stats) return;
    if (!s) {
      stats.hidden = true;
      return;
    }
    document.getElementById("stat-players").textContent = s.players;
    document.getElementById("stat-peak").textContent = s.peak24h;
    var bars = document.getElementById("stat-days");
    bars.textContent = "";
    (s.days || []).forEach(function (d) {
      var bar = document.createElement("span");
      var p = d.up;
      bar.className = "day " + (p == null ? "none" : p >= 0.99 ? "up" : p > 0 ? "part" : "down");
      var parts = String(d.date).split("-");
      var label = +parts[2] + " " + MONTHS[+parts[1] - 1];
      var tip = label + ": " + (p == null ? "no data yet" : (Math.floor(p * 1000) / 10) + "% up");
      bar.setAttribute("data-tip", tip);
      bar.setAttribute("aria-label", tip);
      bar.setAttribute("role", "img");
      bar.tabIndex = 0;
      bars.appendChild(bar);
    });
    stats.hidden = false;
  }

  function refreshStatus() {
    fetch("api/app/status", { cache: "no-store" })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
      .then(function (s) {
        showStatus(s.online ? [{ name: "", players: s.players }] : []);
        showStats(s);
      })
      .catch(function (code) {
        showStats(null);
        if (code !== 404) {
          showStatus(null);
          return;
        }
        // An account server without app/status: just the count, from the server list.
        fetch("api/app/serverList", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: "" })
          .then(function (r) { return r.ok ? r.text() : Promise.reject(r.status); })
          .then(function (text) { showStatus(parseServers(text)); })
          .catch(function () { showStatus(null); });
      });
  }

  if (status) {
    refreshStatus();
    setInterval(function () {
      if (!document.hidden) refreshStatus();
    }, 60000);
  }

  // --- Downloads: the visitor's system first --------------------------------------------------
  var ua = navigator.userAgent;
  var os = /Windows/i.test(ua) ? "windows" : /Mac OS X|Macintosh/i.test(ua) ? "macos" : /Linux|X11/i.test(ua) && !/Android/i.test(ua) ? "linux" : "";
  var list = document.getElementById("dl-list");
  if (list && os) {
    var mine = list.querySelector('[data-os="' + os + '"]');
    if (mine) {
      mine.classList.add("mine");
      list.insertBefore(mine, list.firstChild);
      var main = document.getElementById("download-main");
      if (main) main.textContent = "Download for " + mine.getAttribute("data-label");
    }
  }

  // --- Can this browser run the game? -------------------------------------------------------
  var note = document.getElementById("play-note");
  if (note) {
    var touchOnly = matchMedia("(pointer: coarse)").matches && !matchMedia("(any-pointer: fine)").matches;
    var webgl2 = false;
    try { webgl2 = !!document.createElement("canvas").getContext("webgl2"); } catch (e) { /* none */ }
    if (!webgl2) {
      note.textContent = "Your browser doesn't have WebGL 2, which the game needs. Update it, turn on hardware acceleration, or download the client.";
      note.classList.add("warn");
    } else if (touchOnly) {
      note.textContent = "The game needs a keyboard and mouse: open this page on a computer to play.";
      note.classList.add("warn");
    }
  }

  // --- Pixel sprites --------------------------------------------------------------------------
  // <i class="sprite" data-sprite="sword" data-px="3"> becomes an SVG of the grid below, each
  // character one pixel of data-px CSS pixels ("." is clear).
  var INK = {
    o: "#120c1e", s: "#e6ebf7", S: "#8e9ab8", g: "#ffc65c", G: "#d98a2b", b: "#8a4b2a", B: "#5c2f1c",
    R: "#ff4d6a", r: "#a3203a", w: "#fff6e6", W: "#e9dcc4", c: "#a86b3c", e: "#ff7a3d", p: "#3b1d6e",
    P: "#7c4ddb", L: "#b99bff", d: "#ff9ad5", k: "#120c1e", y: "#ffe27a", Y: "#ffb83d", u: "#7fd4ff",
    v: "#5b3cb0"
  };
  var SPRITES = {
    sword: [
      "..........oo",
      ".........oso",
      "........osSo",
      ".......osSo.",
      "......osSo..",
      "..o..osSo...",
      "..gooSSo....",
      "...gSSo.....",
      "...bgGo.....",
      "..bo.oGo....",
      ".bo...o.....",
      "gg.........."
    ],
    crown: [
      "g....g....g",
      "gg..ggg..gg",
      "ggg.ggg.ggg",
      "ggggggggggg",
      "GGGGGGGGGGG",
      "GgRgguggRgG",
      "GGGGGGGGGGG"
    ],
    potion: [
      "....oooo....",
      "....occo....",
      ".....oo.....",
      "....owwo....",
      "...oRRRRo...",
      "..oRwRRRRo..",
      "..owRRRRRo..",
      "..oRRRRRro..",
      "..orRRRrro..",
      "...orrrro...",
      "....oooo...."
    ],
    skull: [
      "...oooooo...",
      "..owwwwwwo..",
      ".owwwwwwwWo.",
      ".owwwwwwwWo.",
      ".owooWWoowo.",
      ".oweoWWeowo.",
      ".owwwWoWWWo.",
      "..oWwoowWo..",
      "..owowowWo..",
      "...oooooo..."
    ],
    portal: [
      "....pppp....",
      "..ppPPPPpp..",
      ".pPPLLLLPPp.",
      ".pPLLwwLLPp.",
      "pPLLwddwLLPp",
      "pPLwdkkdwLPp",
      "pPLwdkkdwLPp",
      "pPLLwddwLLPp",
      ".pPLLwwLLPp.",
      ".pPPLLLLPPp.",
      "..ppPPPPpp..",
      "....pppp...."
    ],
    chest: [
      "..oooooooo..",
      ".obbbbbbbbo.",
      ".oBBBBBBBBo.",
      ".oggggggggo.",
      ".obbbggbbbo.",
      ".oBBgkkgBBo.",
      ".obbbggbbbo.",
      ".oBBBBBBBBo.",
      ".oggggggggo.",
      "..oooooooo.."
    ],
    shield: [
      ".oooooooooo.",
      ".oeeeeggvvo.",
      ".oeeeegvvvo.",
      ".oeeeegvvvo.",
      ".oggggggggo.",
      ".ovvvvgeeeo.",
      ".ovvvvgeeeo.",
      "..ovvvgeeo..",
      "..ovvvgeeo..",
      "...ovvgeo...",
      "....oggo....",
      ".....oo....."
    ],
    anvil: [
      ".....e......",
      "....eYe..e..",
      "....eyYe.eY.",
      "oooooooooooo",
      "osssssssssSo",
      ".oSSSSSSSSo.",
      "..ooSSSSoo..",
      "....oSSo....",
      "...oSSSSo...",
      "..oSSSSSSo..",
      "..oooooooo.."
    ],
    bolt: [
      "......oooo",
      ".....oyyyo",
      "....oyyYo.",
      "...oyyYo..",
      "..oyyYoooo",
      ".oyyyyyyyo",
      ".ooooyyYo.",
      "....oyYo..",
      "...oyYo...",
      "..oyYo....",
      ".oyo......",
      ".oo......."
    ],
    paw: [
      "..oo....oo..",
      ".oWwo..oWwo.",
      ".oWWo..oWWo.",
      "o.oo.oo.oo.o",
      "oWo.oWwo.oWo",
      "oWo.oWWo.oWo",
      ".o.oooooo.o.",
      "..oWwwwwWo..",
      ".oWwwwwwwWo.",
      ".oWWwwwwWWo.",
      "..oWWWWWWo..",
      "...oooooo..."
    ],
    gem: [
      "...oooooo...",
      "..oLwLLPPo..",
      ".oLwLLLPPPo.",
      "oooooooooooo",
      "oLwLLLLPPPvo",
      ".oLLLLPPPvo.",
      "..oLLLPPvo..",
      "...oLPPvo...",
      "....oPvo....",
      ".....oo....."
    ],
    banner: [
      "oo..........",
      "ogoooooooo..",
      "ogeeeeeeeeo.",
      "ogeYYeeYYeo.",
      "ogeeYYYYeeo.",
      "ogeeeYYeeeo.",
      "ogeeeeeeeeo.",
      "ogeeeooeeeo.",
      "ogeeo..oeeo.",
      "ogoo....oo..",
      "og..........",
      "og..........",
      "oo.........."
    ],
    globe: [
      "...oooooo...",
      "..ouuuuuuo..",
      ".ouuGGuuuuo.",
      "ouuGGGGuuGuo",
      "ouGGGGuuGGuo",
      "ouuGGuuuuuuo",
      "ouuuuuuGGGuo",
      "ouuuuuGGGGuo",
      ".ouuuuuGGuo.",
      "..ouuuuuuo..",
      "...oooooo..."
    ],
    coin: [
      "..oooooo..",
      ".oyyyyyYo.",
      "oyywyyyYGo",
      "oywyYYyYGo",
      "oyyYyyGyGo",
      "oyyYyyGyGo",
      "oyyyGGyYGo",
      "oyYyyyYYGo",
      ".oGGGGGGo.",
      "..oooooo.."
    ],
    coins: [
      ".oooooooo........",
      "oyywyyyyYo.......",
      "oGGGGGGGGoooooo..",
      "oYyyyyyyoyyyyyYo.",
      "oGGGGGGoyywyyyYGo",
      "oYyyyyyoywyYYyYGo",
      "oGGGGGGoyyYyyGyGo",
      "oYyyyyyoyyYyyGyGo",
      "oGGGGGGoyyyGGyYGo",
      "oYyyyyyoyYyyyYYGo",
      "oGGGGGGGoGGGGGGo.",
      ".oooooooooooooo.."
    ]
  };

  function sprite(name, px) {
    var rows = SPRITES[name];
    if (!rows) return null;
    var w = rows[0].length, h = rows.length, rects = "";
    for (var y = 0; y < h; y++) {
      for (var x = 0; x < w; x++) {
        var c = rows[y].charAt(x);
        if (INK[c]) rects += '<rect x="' + x + '" y="' + y + '" width="1" height="1" fill="' + INK[c] + '"/>';
      }
    }
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + w + " " + h + '" width="' + w * px +
      '" height="' + h * px + '" aria-hidden="true">' + rects + "</svg>";
  }

  // for pages that draw sprites of their own (gold.js)
  window.ddSprite = sprite;

  // --- Patch notes on the home page --------------------------------------------------------
  // A long release folds to its first lines under a fade, with a button to open it all.
  var notes = document.querySelectorAll(".notes .release");
  for (var n = 0; n < notes.length; n++) {
    var rel = notes[n];
    if (rel.scrollHeight < 520) continue;
    rel.classList.add("folded");
    var more = document.createElement("button");
    more.type = "button";
    more.className = "more";
    more.textContent = "Show all";
    more.setAttribute("aria-expanded", "false");
    more.addEventListener("click", function (r, b) {
      return function () {
        var open = r.classList.toggle("folded") === false;
        b.textContent = open ? "Show less" : "Show all";
        b.setAttribute("aria-expanded", String(open));
        if (!open) r.scrollIntoView({ block: "nearest" });
      };
    }(rel, more));
    rel.appendChild(more);
  }

  var marks = document.querySelectorAll(".sprite[data-sprite]");
  for (var i = 0; i < marks.length; i++) {
    var svg = sprite(marks[i].getAttribute("data-sprite"), +marks[i].getAttribute("data-px") || 3);
    if (svg) marks[i].innerHTML = svg;
  }

  // the sprites as data, for scene.js to build them out of voxels
  window.ddSprites = { rows: SPRITES, ink: INK };

  // --- The menu on phones: the top bar's links fold behind a button ---------------------------
  var bar = document.querySelector(".bar");
  var links = bar && bar.querySelector(".links");
  if (bar && links) {
    links.id = links.id || "site-links";
    var menu = document.createElement("button");
    menu.type = "button";
    menu.className = "menu-btn";
    menu.setAttribute("aria-label", "Menu");
    menu.setAttribute("aria-expanded", "false");
    menu.setAttribute("aria-controls", links.id);
    menu.innerHTML = "<span></span><span></span><span></span>";
    bar.appendChild(menu);
    var setMenu = function (open) {
      bar.classList.toggle("open", open);
      menu.setAttribute("aria-expanded", String(open));
    };
    menu.addEventListener("click", function () { setMenu(!bar.classList.contains("open")); });
    links.addEventListener("click", function (e) { if (e.target.closest("a")) setMenu(false); });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape") setMenu(false); });
  }

  // --- Screenshots: a lightbox over the page, with arrows, keys and swipes ---------------------
  // Each .shot links to its full size picture, so without the script a click still opens it.
  function gallery(shots) {
    if (!shots.length || typeof HTMLDialogElement !== "function") return;
    var items = shots.map(function (li) {
      var cap = li.querySelector(".cap");
      return {
        link: li.querySelector("a"),
        alt: li.querySelector("img").alt,
        title: cap ? cap.querySelector("strong").textContent : "",
        text: cap ? cap.lastChild.textContent.trim() : ""
      };
    });

    var box = document.createElement("dialog");
    box.className = "lightbox";
    box.setAttribute("aria-label", "Screenshot");
    box.innerHTML =
      '<figure><img alt=""></figure>' +
      '<p class="lb-cap"><strong></strong><span></span><span class="lb-count"></span></p>' +
      '<button class="lb-btn lb-close" type="button" aria-label="Close">×</button>' +
      '<button class="lb-btn lb-prev" type="button" aria-label="Previous screenshot">‹</button>' +
      '<button class="lb-btn lb-next" type="button" aria-label="Next screenshot">›</button>';
    document.body.appendChild(box);
    var img = box.querySelector("img");
    var capTitle = box.querySelector(".lb-cap strong");
    var capText = box.querySelector(".lb-cap span");
    var count = box.querySelector(".lb-count");
    var at = 0;

    function show(i) {
      at = (i + items.length) % items.length;
      var it = items[at];
      img.removeAttribute("src"); // else the last picture shows under the new caption while this one loads
      img.src = it.link.href;
      img.alt = it.alt;
      capTitle.textContent = it.title;
      capText.textContent = it.text;
      count.textContent = (at + 1) + " / " + items.length;
      // the next one is likely wanted soon
      new Image().src = items[(at + 1) % items.length].link.href;
    }
    function close() { box.close(); }

    items.forEach(function (it, i) {
      it.link.addEventListener("click", function (e) {
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.button) return; // a new tab, as asked
        e.preventDefault();
        show(i);
        box.showModal();
        document.body.classList.add("lb-open");
      });
    });
    box.addEventListener("close", function () {
      document.body.classList.remove("lb-open");
      items[at].link.focus();
    });
    box.querySelector(".lb-close").addEventListener("click", close);
    box.querySelector(".lb-prev").addEventListener("click", function () { show(at - 1); });
    box.querySelector(".lb-next").addEventListener("click", function () { show(at + 1); });
    // a click on the dark around the picture closes it
    box.addEventListener("click", function (e) {
      if (e.target === box || e.target.tagName === "FIGURE") close();
    });
    box.addEventListener("keydown", function (e) {
      if (e.key === "ArrowLeft") show(at - 1);
      else if (e.key === "ArrowRight") show(at + 1);
    });
    var touchX = null;
    box.addEventListener("touchstart", function (e) { touchX = e.touches[0].clientX; }, { passive: true });
    box.addEventListener("touchend", function (e) {
      if (touchX === null) return;
      var dx = e.changedTouches[0].clientX - touchX;
      touchX = null;
      if (Math.abs(dx) > 50) show(at + (dx < 0 ? 1 : -1));
    });
  }

  gallery(Array.prototype.slice.call(document.querySelectorAll(".gallery .shot")));

  // For tests: window.bskSite.parseServers(xml)
  window.bskSite = { parseServers: parseServers, showStatus: showStatus, showStats: showStats };
})();
