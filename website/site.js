// The site's live parts: the server status from the account server's server list (through the
// gateway's /api), the visitor's own download first, a warning when the browser can't run the
// game, the pixel sprites, and the hero's night sky.
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

  function refreshStatus() {
    fetch("api/app/serverList", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: "" })
      .then(function (r) { return r.ok ? r.text() : Promise.reject(r.status); })
      .then(function (text) { showStatus(parseServers(text)); })
      .catch(function () { showStatus(null); });
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

  var marks = document.querySelectorAll(".sprite[data-sprite]");
  for (var i = 0; i < marks.length; i++) {
    var svg = sprite(marks[i].getAttribute("data-sprite"), +marks[i].getAttribute("data-px") || 3);
    if (svg) marks[i].innerHTML = svg;
  }

  // --- The hero's night sky -------------------------------------------------------------------
  // Drawn at a third of the screen's resolution and scaled up without smoothing, so everything is
  // chunky pixels: banded sky, twinkling stars, a moon, shooting stars, a castle skyline with lit
  // windows, and embers rising from below. Still when the visitor asks for reduced motion.
  var canvas = document.getElementById("sky");
  if (canvas && canvas.getContext) sky(canvas);

  function sky(canvas) {
    var ctx = canvas.getContext("2d");
    var still = matchMedia("(prefers-reduced-motion: reduce)").matches;
    var W = 0, H = 0, stars = [], windows = [], embers = [], meteor = null, land = null, last = 0;

    function rng(seed) {
      return function () {
        seed = (seed * 1664525 + 1013904223) >>> 0;
        return seed / 4294967296;
      };
    }

    function build() {
      var px = innerWidth < 640 ? 2 : 3;
      W = Math.ceil(canvas.clientWidth / px);
      H = Math.ceil(canvas.clientHeight / px);
      if (!W || !H) return;
      canvas.width = W;
      canvas.height = H;

      var r = rng(7);
      stars = [];
      for (var n = Math.round(W * H / 90); n > 0; n--) {
        stars.push({ x: Math.floor(r() * W), y: Math.floor(Math.pow(r(), 1.4) * H * .72), big: r() < .08,
          phase: r() * 6.28, speed: .6 + r() * 1.8, tint: r() < .15 ? "#ffd99a" : r() < .3 ? "#c6b4ff" : "#ffffff" });
      }

      // The skyline, drawn once into its own canvas: far hills, a town of towers, the castle.
      land = document.createElement("canvas");
      land.width = W;
      land.height = H;
      var g = land.getContext("2d");
      windows = [];
      var base = H - 1;

      // far hills
      g.fillStyle = "#24163a";
      for (var x = 0; x < W; x++) {
        var hgt = H * .2 + Math.sin(x / 23) * 6 + Math.sin(x / 9 + 1) * 3 + Math.sin(x / 61 + 2) * 10;
        g.fillRect(x, Math.round(base - hgt), 1, Math.round(hgt) + 1);
      }

      // the town: blocks with battlements and the odd spire
      r = rng(11);
      g.fillStyle = "#170e26";
      var cx = Math.round(W * .5), cw = Math.max(46, Math.round(Math.min(W * .3, 120)));
      for (x = -4; x < W;) {
        var bw = 6 + Math.floor(r() * 12), bh = Math.round(H * (.08 + r() * .14));
        if (x + bw > cx - cw / 2 - 4 && x < cx + cw / 2 + 4) bh = Math.round(bh * .7);
        var top = base - bh;
        g.fillRect(x, top, bw, bh + 1);
        for (var k = 0; k < bw; k += 2) g.fillRect(x + k, top - 2, 1, 2);
        if (r() < .3) {
          var sx = x + Math.floor(bw / 2);
          for (var s = 0; s < 8; s++) g.fillRect(sx - Math.floor((8 - s) / 3), top - 10 + s, 1 + 2 * Math.floor((8 - s) / 3), 1);
          g.fillRect(sx, top - 13, 1, 3);
        }
        for (var wy = top + 3; wy < base - 3; wy += 4) {
          for (var wx = x + 2; wx < x + bw - 2; wx += 3) {
            if (r() < .16) windows.push({ x: wx, y: wy, w: 1, h: 2, phase: r() * 6.28, flick: r() < .3 });
          }
        }
        x += bw + Math.floor(r() * 3);
      }

      // the castle in the middle: a keep, two towers, a gate with a glow
      g.fillStyle = "#100a1c";
      var keepH = Math.round(Math.min(H * .2, 70)), keepTop = base - keepH;
      var kx = cx - Math.round(cw / 2);
      g.fillRect(kx, keepTop + 10, cw, keepH);
      for (k = 0; k < cw; k += 4) g.fillRect(kx + k, keepTop + 6, 2, 4);
      for (k = 6; k < cw - 6; k += 7) {
        windows.push({ x: kx + k, y: keepTop + 18, w: 2, h: 3, phase: k, flick: true });
      }
      var tw = Math.max(8, Math.round(cw * .16));
      [kx - tw + 2, kx + cw - 2, cx - Math.round(tw / 2)].forEach(function (tx, idx) {
        var th = idx === 2 ? keepH + 18 : keepH + 6, tt = base - th;
        g.fillRect(tx, tt, tw, th + 1);
        for (var y = 0; y < tw; y++) {
          var half = Math.max(0, Math.round((y / tw) * (tw / 2 + 1)));
          g.fillRect(tx + Math.floor(tw / 2) - half, tt - tw + y, half * 2, 1);
        }
        g.fillRect(tx + Math.floor(tw / 2) - 1, tt - tw - 4, 1, 4);
        windows.push({ x: tx + Math.floor(tw / 2) - 1, y: tt + 6, w: 2, h: 3, phase: idx * 2, flick: true, hot: true });
      });
      g.fillStyle = "#ff7a3d";
      windows.push({ x: cx - 4, y: base - 12, w: 8, h: 12, phase: 0, flick: true, hot: true, gate: true });

      // the near ground, the page's own colour
      g.fillStyle = "#0c0915";
      for (x = 0; x < W; x++) {
        var gh = 3 + Math.round(Math.sin(x / 13) * 1.5 + Math.sin(x / 5) * .8);
        g.fillRect(x, base - gh, 1, gh + 1);
      }

      embers = [];
      for (n = Math.round(W / 14); n > 0; n--) embers.push(ember(true));
    }

    function ember(anywhere) {
      return { x: Math.random() * W, y: anywhere ? H * (.5 + Math.random() * .5) : H + 2,
        v: .08 + Math.random() * .22, sway: Math.random() * 6.28, life: 1 };
    }

    function band(y0, y1, colours) {
      // a gradient in flat steps, like an old palette
      var steps = colours.length, h = (y1 - y0) / steps;
      for (var i = 0; i < steps; i++) {
        ctx.fillStyle = colours[i];
        ctx.fillRect(0, Math.floor(y0 + i * h), W, Math.ceil(h) + 1);
      }
    }

    function moon(mx, my, rad) {
      for (var y = -rad - 3; y <= rad + 3; y++) {
        for (var x = -rad - 3; x <= rad + 3; x++) {
          var d = Math.sqrt(x * x + y * y);
          if (d <= rad) {
            var lit = x + y * .4 < rad * .5;
            ctx.fillStyle = lit ? "#fbeed2" : "#d9c39c";
            ctx.fillRect(mx + x, my + y, 1, 1);
          } else if (d <= rad + 3 && (x + y) % 2 === 0) {
            ctx.fillStyle = "rgba(255, 220, 160, .14)";
            ctx.fillRect(mx + x, my + y, 1, 1);
          }
        }
      }
      ctx.fillStyle = "#cdb48a";
      ctx.fillRect(mx - 2, my - 3, 2, 2);
      ctx.fillRect(mx + 2, my + 1, 3, 2);
      ctx.fillRect(mx - 4, my + 3, 2, 1);
    }

    function frame(t) {
      var dt = Math.min(64, t - (last || t));
      last = t;
      var s = t / 1000;

      band(0, H * .8, ["#06040c", "#0a0716", "#0f0a1f", "#150d29", "#1c1033", "#26123b", "#33143f", "#45183f"]);
      band(H * .8, H, ["#5a1d3e", "#6e2438"]);

      for (var i = 0; i < stars.length; i++) {
        var st = stars[i];
        var a = still ? .8 : .45 + .55 * Math.max(0, Math.sin(s * st.speed + st.phase));
        ctx.globalAlpha = a;
        ctx.fillStyle = st.tint;
        ctx.fillRect(st.x, st.y, 1, 1);
        if (st.big && a > .8) {
          ctx.globalAlpha = a * .5;
          ctx.fillRect(st.x - 1, st.y, 3, 1);
          ctx.fillRect(st.x, st.y - 1, 1, 3);
        }
      }
      ctx.globalAlpha = 1;

      moon(Math.round(W * .82), Math.round(H * .17), Math.max(5, Math.round(Math.min(W, H) * .045)));

      if (!still) {
        if (!meteor && Math.random() < dt / 7000) {
          meteor = { x: W * (.1 + Math.random() * .6), y: H * Math.random() * .25, len: 10 + Math.random() * 10, t: 0 };
        }
        if (meteor) {
          meteor.t += dt / 1000;
          var mx = meteor.x + meteor.t * W * .5, my = meteor.y + meteor.t * W * .22;
          for (var k = 0; k < meteor.len; k++) {
            ctx.globalAlpha = (1 - k / meteor.len) * Math.max(0, 1 - meteor.t * 1.4);
            ctx.fillStyle = k < 2 ? "#ffffff" : "#ffd99a";
            ctx.fillRect(Math.round(mx - k * 2.2), Math.round(my - k), 1, 1);
          }
          ctx.globalAlpha = 1;
          if (meteor.t > .75) meteor = null;
        }
      }

      if (land) ctx.drawImage(land, 0, 0);

      for (i = 0; i < windows.length; i++) {
        var w = windows[i];
        var glow = w.flick && !still ? .7 + .3 * Math.sin(s * 3 + w.phase) * Math.sin(s * 7.3 + w.phase) : 1;
        ctx.globalAlpha = glow;
        ctx.fillStyle = w.gate ? "#ff7a3d" : w.hot ? "#ff9a4d" : "#ffc65c";
        if (w.gate) {
          ctx.fillRect(w.x + 2, w.y, w.w - 4, 1);
          ctx.fillRect(w.x + 1, w.y + 1, w.w - 2, 1);
          ctx.fillRect(w.x, w.y + 2, w.w, w.h - 2);
          ctx.globalAlpha = glow * .25;
          ctx.fillRect(w.x - 3, w.y - 2, w.w + 6, w.h + 2);
        } else {
          ctx.fillRect(w.x, w.y, w.w, w.h);
        }
      }
      ctx.globalAlpha = 1;

      for (i = 0; i < embers.length; i++) {
        var e = embers[i];
        if (!still) {
          e.y -= e.v * dt / 16;
          e.sway += dt / 900;
          e.life = Math.min(1, (e.y / H - .35) * 2.2);
          if (e.life <= 0) embers[i] = e = ember(false);
        }
        ctx.globalAlpha = Math.max(0, e.life);
        ctx.fillStyle = e.life > .6 ? "#ffb066" : "#ff5a2a";
        ctx.fillRect(Math.round(e.x + Math.sin(e.sway) * 3), Math.round(e.y), 1, 1);
      }
      ctx.globalAlpha = 1;

      if (!still) requestAnimationFrame(frame);
    }

    var timer;
    addEventListener("resize", function () {
      clearTimeout(timer);
      timer = setTimeout(function () {
        build();
        if (still) frame(0);
      }, 120);
    });
    build();
    if (still) frame(0); else requestAnimationFrame(frame);
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
  window.bskSite = { parseServers: parseServers, showStatus: showStatus };
})();
