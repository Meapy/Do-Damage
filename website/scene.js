// The home page's hero, drawn with Three.js (vendor/three.min.js): Oryx the Ascended God in his
// Elysium, his minions, and a party of six players fighting him, all built in cubes, one per pixel of
// their sprites in the game with the game's black outline round them (data-boss on the hero's
// hidden <i data-host> list, from tools/build_wiki.py's stage_bosses). The fight runs on a loop of
// about a minute and a half: his Wardens circling him, the Judgement's three patterns (the Radiant
// Cross, the Firmament, the Divine Hunt), the Call of the Host, the Transcendence, the Ascension's
// halo, the Apotheosis (his gold sprite) and Wrath, the Last Judgement with the lava closing in, the
// Concession, where he falls back through every Oryx he was, and his fall. His shots, their speeds,
// spreads and timings are the server's (BehaviorDb.Elysium.cs, ORYX_SHOTS in tools/build_elysium.py),
// flown a little slower and the phases shortened. The players strafe and dodge, shoot him and his
// minions (damage numbers where they hit) and use their abilities; his Host walk about and shoot,
// the Noble and Cardinal rise beneath him in the Hunt, and his splats of Divine Ichor hatch Ichor
// Sprites. His taunts show beside him as the game's chat would.
// Nothing here is needed to read the page: without WebGL his sprite stands in the hero, with reduced
// motion one still frame is drawn, and nothing is drawn while the hero is off screen.
(function () {
  "use strict";
  var T = window.THREE;
  var stageEl = document.querySelector(".hero-stage");
  var art = stageEl && stageEl.querySelector("[data-boss]");
  if (!T || !stageEl || !art) return;

  var reduce = matchMedia("(prefers-reduced-motion: reduce)");
  var canvas = document.createElement("canvas");
  var renderer;
  try {
    renderer = new T.WebGLRenderer({ canvas: canvas, antialias: true, powerPreference: "high-performance" });
  } catch (e) {
    return;
  }
  stageEl.appendChild(canvas);
  document.documentElement.classList.add("has-scene");

  var INK = 0x0c0915;
  var pr = Math.min(devicePixelRatio || 1, 1.75);
  var quality = 1; // lowered when frames are slow
  renderer.setClearColor(INK, 1);
  var scene = new T.Scene();
  scene.fog = new T.Fog(INK, 110, 260);
  var camera = new T.PerspectiveCamera(30, 1, 0.5, 500);
  var D2R = Math.PI / 180;
  var TILE = 2.6;   // one of the game's tiles, in scene units (the figures stand taller than in the game)
  var SLOW = 0.7;   // shots fly at 70% of the game's speed, as far as in the game
  var ORYX_H = 16;  // how tall he stands; the others by their Size against his (220), an 8-pixel
                    // sprite half as tall as a 16-pixel one, as the game draws them

  // --- Light: soft from above, a key from the front left, a violet rim from behind, a glow below.
  // Kept low enough that a sprite's face shows its own colours, as the game draws them.
  scene.add(new T.HemisphereLight(0xffffff, 0x2a1d44, 0.55));
  var key = new T.DirectionalLight(0xfff4e6, 0.75);
  key.position.set(-0.8, 1.2, 2);
  scene.add(key);
  var rim = new T.DirectionalLight(0x8b5cf6, 1.6);
  rim.position.set(1.6, 0.6, -1.6);
  scene.add(rim);
  var pool = new T.PointLight(0x7fb4ff, 1.4, 50, 1.6);
  scene.add(pool);

  // --- The floor: dark stone, a grid of faint points, soft shadows ------------------------------
  var floor = new T.Mesh(new T.PlaneGeometry(500, 500), new T.MeshLambertMaterial({ color: 0x150f26 }));
  floor.rotation.x = -Math.PI / 2;
  scene.add(floor);

  function radial(stops) {
    var c = document.createElement("canvas");
    c.width = c.height = 128;
    var g = c.getContext("2d"), gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    stops.forEach(function (s) { gr.addColorStop(s[0], s[1]); });
    g.fillStyle = gr;
    g.fillRect(0, 0, 128, 128);
    return new T.CanvasTexture(c);
  }
  var shadowMat = new T.MeshBasicMaterial({
    map: radial([[0, "rgba(0,0,0,.8)"], [0.6, "rgba(0,0,0,.3)"], [1, "rgba(0,0,0,0)"]]), transparent: true, depthWrite: false });
  var shadowGeo = new T.PlaneGeometry(1, 1);
  function shadowOf(w) {
    var m = new T.Mesh(shadowGeo, shadowMat);
    m.rotation.x = -Math.PI / 2;
    m.position.y = 0.02;
    m.scale.set(w, w * 0.5, 1);
    return m;
  }
  var glowTex = radial([[0, "rgba(255,255,255,1)"], [0.35, "rgba(255,255,255,.45)"], [1, "rgba(255,255,255,0)"]]);
  function glow(color, size) {
    var s = new T.Sprite(new T.SpriteMaterial({ map: glowTex, color: color, blending: T.AdditiveBlending, depthWrite: false, transparent: true }));
    s.scale.set(size, size, 1);
    return s;
  }

  var dotsG = new T.BufferGeometry(), dots = [];
  for (var gx = -90; gx <= 90; gx += 3.5) for (var gz = -80; gz <= 60; gz += 3.5) dots.push(gx, 0.03, gz);
  dotsG.setAttribute("position", new T.Float32BufferAttribute(dots, 3));
  scene.add(new T.Points(dotsG, new T.PointsMaterial({ color: 0x44365f, size: 0.16, sizeAttenuation: true, transparent: true, opacity: 0.8 })));

  // --- Sprites into cubes ------------------------------------------------------------------------------
  var sheets = {}, pending = 0;
  function sheet(url, done) {
    var s = sheets[url];
    if (!s) {
      s = sheets[url] = { img: new Image(), ready: false, waiting: [] };
      s.img.onload = function () { s.ready = true; s.waiting.forEach(function (f) { f(s.img); }); s.waiting = []; };
      s.img.onerror = function () { s.waiting.forEach(function (f) { f(null); }); s.waiting = []; };
      s.img.src = url;
    }
    if (s.ready) done(s.img); else s.waiting.push(done);
  }
  function spot(el) {
    if (!el) return null;
    var p = el.getAttribute("data-boss").split(" ");
    return { url: p[0], x: +p[1], y: +p[2], w: +p[3], h: +p[4] };
  }
  var m4 = new T.Matrix4(), p3 = new T.Vector3(), s3 = new T.Vector3(), q4 = new T.Quaternion();
  var cubeGeo = new T.BoxGeometry(1, 1, 1);
  var lineMat = new T.MeshBasicMaterial({ color: 0x000000 });
  // one cube per opaque pixel, flat like the sprite, and the game's 1-pixel black outline round it
  // a little behind; standing on y = 0, centred on the frame. Returns a group of the two.
  function voxels(px, w, h, height, mat) {
    var solid = [], x, y, cells = [], edge = [], bottom = 0;
    for (y = 0; y < h; y++) for (x = 0; x < w; x++) solid[y * w + x] = px[(y * w + x) * 4 + 3] > 40;
    function at(x, y) { return x >= 0 && y >= 0 && x < w && y < h && solid[y * w + x]; }
    for (y = -1; y <= h; y++) for (x = -1; x <= w; x++) {
      if (at(x, y)) { cells.push([x, y]); bottom = Math.max(bottom, y); }
      else if (at(x - 1, y) || at(x + 1, y) || at(x, y - 1) || at(x, y + 1)) edge.push([x, y]);
    }
    if (!cells.length) return null;
    bottom += 1; // the outline's row under the feet
    var unit = height / (h + 1), depth = unit * 1.6;
    var face = new T.InstancedMesh(cubeGeo, mat, cells.length), col = new T.Color();
    cells.forEach(function (c, n) {
      var k = (c[1] * w + c[0]) * 4;
      m4.compose(p3.set((c[0] - w / 2 + 0.5) * unit, (bottom - c[1] + 0.5) * unit, 0), q4, s3.set(unit, unit, depth));
      face.setMatrixAt(n, m4);
      face.setColorAt(n, col.setRGB(px[k] / 255, px[k + 1] / 255, px[k + 2] / 255));
    });
    var line = new T.InstancedMesh(cubeGeo, lineMat, edge.length);
    edge.forEach(function (c, n) {
      m4.compose(p3.set((c[0] - w / 2 + 0.5) * unit, (bottom - c[1] + 0.5) * unit, -unit * 0.1), q4, s3.set(unit, unit, depth * 0.9));
      line.setMatrixAt(n, m4);
    });
    var g = new T.Group();
    g.add(face, line);
    return g;
  }
  function cut(s, col, height, mat, done) {
    pending++;
    sheet(s.url, function (img) {
      pending--;
      if (!img) return;
      var c = document.createElement("canvas");
      c.width = s.w; c.height = s.h;
      var g = c.getContext("2d");
      g.drawImage(img, s.x + col * s.w, s.y, s.w, s.h, 0, 0, s.w, s.h);
      var px;
      try { px = g.getImageData(0, 0, s.w, s.h).data; } catch (e) { px = null; }
      done(px ? voxels(px, s.w, s.h, height, mat) : null);
    });
  }
  // the same cubes again, for one more of a kind (the instances shared), in its own material
  function copyOf(tpl, mat) {
    var g = new T.Group();
    tpl.children.forEach(function (m, i) {
      var c = new T.InstancedMesh(m.geometry, i === 0 && mat ? mat : m.material, m.count);
      c.instanceMatrix = m.instanceMatrix;
      if (m.instanceColor) c.instanceColor = m.instanceColor;
      g.add(c);
    });
    return g;
  }
  function spriteMat() { return new T.MeshLambertMaterial({ color: 0xffffff, emissive: 0x000000, transparent: true }); }

  // --- Oryx and every form he takes ---------------------------------------------------------------
  var oryx = new T.Group();   // where he stands; moves in the Hunt
  scene.add(oryx);
  var body = new T.Group();   // turned a little with the mouse, swells on a change of form
  oryx.add(body);
  var oShadow = shadowOf(12);
  scene.add(oShadow);
  var mat = spriteMat();

  var A = spot(art);
  function cell(col, row) { return { url: A.url, x: A.x + col * 16, y: A.y + row * 16, w: 16, h: 16 }; }
  function past(n) { return spot(document.querySelector("#descent .stop:nth-child(" + n + ") [data-boss]")); }
  // his sheet (elysiumOryx16x16): row 0 as he is, row 1 his Apotheosis in gold; column 0 standing,
  // 1 and 2 walking, 4 and 5 his attack. His past selves are the stage cards' sprites.
  var FORMS = {
    asc: [cell(0, 0), ORYX_H], ascW1: [cell(1, 0), ORYX_H], ascW2: [cell(2, 0), ORYX_H], ascAtk: [cell(5, 0), ORYX_H],
    gold: [cell(0, 1), ORYX_H], goldW1: [cell(1, 1), ORYX_H], goldW2: [cell(2, 1), ORYX_H], goldAtk: [cell(5, 1), ORYX_H],
    o4: [past(5), 15], o3: [past(4), 15], o2: [past(3), 12], o1: [past(2), 10]
  };
  var meshes = {};
  Object.keys(FORMS).forEach(function (name) {
    var f = FORMS[name][0];
    if (!f) return;
    cut(f, 0, FORMS[name][1], mat, function (g) {
      if (!g) return;
      g.visible = false;
      meshes[name] = g;
      body.add(g);
    });
  });

  var form = "asc", gold = false, attackUntil = 0, swell = 0, oMoving = false, oGone = false;
  function showForm() {
    var want = form;
    if (form === "asc") {
      var base = gold ? "gold" : "asc";
      want = clock < attackUntil ? base + "Atk" : oMoving ? base + (Math.floor(clock * 6) % 2 ? "W1" : "W2") : base;
      if (!meshes[want]) want = base;
    }
    if (!meshes[want]) want = "asc";
    Object.keys(meshes).forEach(function (k) { meshes[k].visible = !oGone && k === want; });
  }
  function setForm(f) { form = f; swell = 1; }
  function attack() { attackUntil = clock + 0.28; }

  // --- Everyone else: his minions and the players -----------------------------------------------------
  // each kind: its frames (stand, walk, walk, attack) as cubes and its height
  var KINDS = {};
  Array.prototype.forEach.call(stageEl.querySelectorAll("[data-host]"), function (el) {
    var s = spot(el), name = el.getAttribute("data-host");
    if (name === "Oryx the Ascended God") return;
    var k = KINDS[name] = { frames: [], h: ORYX_H * (+el.getAttribute("data-size") || 100) / 220 * (s.h / 16), anim: el.hasAttribute("data-anim") };
    (k.anim ? [0, 1, 2, 4] : [0]).forEach(function (col, i) {
      cut(s, col, k.h, null, function (g) { if (g) k.frames[i] = g; });
    });
  });

  // how each minion fights: its shots' colour, how many and how wide, how often, how fast it walks,
  // how many hits it takes
  var WAYS = {
    "Ascended Warden": { c: [0.55, 0.8, 1], n: 3, spr: 12, every: 2, speed: 80, life: 1800, walk: 0, hp: 22 },
    "Ascended Officer": { c: [1, 0.75, 0.3], n: 2, spr: 10, every: 1.6, speed: 90, life: 1500, walk: 0.5, hp: 26 },
    "Ascended Cleric": { c: [0.5, 0.8, 1], n: 1, spr: 0, every: 1.2, speed: 70, life: 2000, walk: 0.35, hp: 26 },
    "Ascended Aristocrat": { c: [0.75, 0.5, 1], n: 3, spr: 20, every: 2.2, speed: 75, life: 1700, walk: 0.4, hp: 26 },
    "Ascended Sergeant": { c: [1, 1, 1], n: 4, spr: 8, every: 2.4, speed: 100, life: 1200, walk: 0.6, hp: 26 },
    "Ascended Noble": { c: [1, 0.82, 0.4], n: 5, spr: 15, every: 1.8, speed: 80, life: 1600, walk: 0.45, hp: 18 },
    "Ascended Cardinal": { c: [1, 0.3, 0.4], n: 3, spr: 25, every: 1.5, speed: 85, life: 1600, walk: 0.4, hp: 18 },
    "Ichor Sprite": { c: [1, 0.85, 0.35], n: 1, spr: 0, every: 1, speed: 110, life: 900, walk: 0.9, hp: 6 },
    "Divine Ichor": { c: [1, 0.85, 0.35], n: 0, every: 99, walk: 0, hp: 0 }
  };
  // the party: where each stands round him (degrees, 90 in front; tiles out), its shots, its ability
  var PARTY = {
    Wizard: { a: 80, r: 8, c: [0.8, 0.55, 1], n: 2, spr: 8, speed: 150, every: 0.45, abil: "bomb", cd: 6 },
    Archer: { a: 130, r: 7.5, c: [0.7, 1, 0.55], n: 1, spr: 0, speed: 180, every: 0.5, abil: "arrow", cd: 7 },
    Priest: { a: 105, r: 9.5, c: [1, 0.92, 0.45], n: 1, spr: 0, speed: 150, every: 0.55, abil: "heal", cd: 8 },
    Knight: { a: 52, r: 4.5, c: [0.62, 0.8, 1], n: 1, spr: 0, speed: 120, every: 0.6, abil: "bash", cd: 8 },
    Warrior: { a: 158, r: 4.5, c: [1, 0.45, 0.35], n: 1, spr: 0, speed: 120, every: 0.5, abil: "berserk", cd: 9 },
    Rogue: { a: 22, r: 6.5, c: [0.88, 0.88, 0.95], n: 1, spr: 0, speed: 150, every: 0.4, abil: "cloak", cd: 9 }
  };

  var mobs = [], party = [];
  function make(name, x, z, mat) {
    var k = KINDS[name];
    if (!k || !k.frames[0]) return null;
    var g = new T.Group(), parts = [];
    k.frames.forEach(function (f, i) { var m = copyOf(f, mat); m.visible = i === 0; parts[i] = m; g.add(m); });
    var sh = shadowOf(k.h * 0.8);
    scene.add(g); scene.add(sh);
    g.position.set(x, 0, z);
    return { name: name, k: k, g: g, parts: parts, sh: sh, x: x, z: z, mat: mat, atk: 0, moving: false };
  }
  // a minion appears at (x, z), thrown from `from` when given; it lives `life` seconds at most
  function summon(name, x, z, life, opts) {
    var m = make(name, x, z, mobMat);
    if (!m) return null;
    m.way = WAYS[name] || WAYS["Ascended Officer"];
    m.tx = x; m.tz = z; m.born = clock; m.life = life; m.dying = -1; m.hp = m.way.hp;
    m.cool = 0.8 + Math.random() * 1.5;
    m.orbit = opts && opts.orbit; m.angle = opts && opts.angle || 0;
    m.from = opts && opts.from ? { x: opts.from.x, z: opts.from.z } : null;
    m.hatch = opts && opts.hatch;
    mobs.push(m);
    return m;
  }
  var mobMat = new T.MeshLambertMaterial({ color: 0xffffff });
  function kill(m) { if (m.dying < 0) { m.dying = 0; burst(m.x, m.z, m.way.c, 14); } }
  function clearMobs() { mobs.forEach(function (m) { scene.remove(m.g); scene.remove(m.sh); }); mobs = []; }
  function alive(m) { return m.dying < 0 && m.hp > 0 && (!m.from || clock - m.born > 0.8); }
  function frameOf(m, age) {
    // attacking, walking (two steps), or standing
    var fr = m.parts.length > 1 ? (m.atk > 0 && m.parts[3] ? 3 : m.moving ? (Math.floor(age * 7) % 2 ? 1 : 2) : 0) : 0;
    for (var j = 0; j < m.parts.length; j++) if (m.parts[j]) m.parts[j].visible = j === fr;
  }

  function stepMobs(dt) {
    for (var i = mobs.length - 1; i >= 0; i--) {
      var m = mobs[i], age = clock - m.born, w = m.way;
      if (m.dying >= 0) {
        m.dying += dt;
        var s = Math.max(0, 1 - m.dying * 3);
        m.g.scale.set(s, s, s);
        m.sh.scale.set(m.k.h * 0.8 * s, m.k.h * 0.4 * s, 1);
        if (m.dying > 0.35) { scene.remove(m.g); scene.remove(m.sh); mobs.splice(i, 1); }
        continue;
      }
      if (age > m.life) {
        if (m.hatch) { kill(m); summon("Ichor Sprite", m.x, m.z, 9); flash(0xffd060, 0.4); }
        else kill(m);
        continue;
      }
      if (m.from && age < 0.8) {
        // thrown: an arc from where he stood
        var f = age / 0.8;
        m.g.position.set(m.from.x + (m.x - m.from.x) * f, Math.sin(f * Math.PI) * 9, m.from.z + (m.z - m.from.z) * f);
        continue;
      }
      var target = nearestPlayer(m.x, m.z);
      if (m.orbit) {
        // the Wardens circle him, 6 tiles out
        m.angle += dt * 20 * D2R;
        m.tx = oryx.position.x + Math.cos(m.angle) * 6 * TILE;
        m.tz = oryx.position.z + Math.sin(m.angle) * 6 * TILE;
      } else if (w.walk > 0 && (Math.hypot(m.tx - m.x, m.tz - m.z) < 0.5 || Math.random() < dt * 0.3)) {
        // wander about the arena, drifting towards the party
        var r = (2 + Math.random() * 7) * TILE, a = Math.random() * Math.PI * 2;
        var cx = target ? home.x * 0.5 + target.x * 0.5 : home.x, cz = target ? home.z * 0.5 + target.z * 0.5 : home.z;
        var to = inArena({ x: cx + Math.cos(a) * r, z: cz + Math.sin(a) * r * 0.7 }, 1);
        m.tx = to.x; m.tz = to.z;
      }
      var dx = m.tx - m.x, dz = m.tz - m.z, d = Math.hypot(dx, dz), v = (m.orbit ? 2 : w.walk * 3.5) * TILE * dt;
      m.moving = d > 0.05 && (w.walk > 0 || m.orbit);
      if (m.moving) { var st = Math.min(1, v / d); m.x += dx * st; m.z += dz * st; }
      var hop = !m.k.anim ? Math.abs(Math.sin(age * (m.name === "Ichor Sprite" ? 9 : 2))) * (m.name === "Ichor Sprite" ? 1.2 : 0.4) : 0;
      m.g.position.set(m.x, hop, m.z);
      m.sh.position.set(m.x, 0.02, m.z);
      var s0 = Math.min(1, age * 4);
      m.g.scale.set(s0, s0, s0);
      m.cool -= dt;
      if (w.n > 0 && target && m.cool <= 0) {
        m.cool = w.every * (0.85 + Math.random() * 0.3);
        spread(w.n, w.spr, Math.atan2(target.z - m.z, target.x - m.x) / D2R, w.speed, w.life, w.c, 85, m, true);
        m.atk = 0.25;
      }
      m.atk -= dt;
      frameOf(m, age);
    }
  }

  // --- The party ----------------------------------------------------------------------------------------
  function gatherParty() {
    if (party.length) return;
    Object.keys(PARTY).forEach(function (name, i) {
      var p = PARTY[name], a = p.a * D2R;
      var m = make(name, home.x + Math.cos(a) * p.r * TILE, home.z + Math.sin(a) * p.r * TILE, spriteMat());
      if (!m) return;
      m.p = p; m.cool = Math.random() * p.every; m.abil = 2 + Math.random() * p.cd; m.seed = i * 1.7;
      m.dx = 0; m.dz = 0; m.look = 0; m.target = null; m.retarget = 0; m.cloak = 0; m.age = 0;
      party.push(m);
    });
  }
  function nearestPlayer(x, z) {
    var best = null, bd = 1e9;
    party.forEach(function (m) { var d = Math.hypot(m.x - x, m.z - z); if (d < bd && m.cloak <= 0) { bd = d; best = m; } });
    return best;
  }
  var DODGE = 1.6 * TILE;
  function stepParty(dt) {
    var ox = oryx.position.x, oz = oryx.position.z, P = PHASES[phase] || [];
    party.forEach(function (m) {
      var p = m.p;
      m.age += dt;
      // where it wants to stand: its place round him, strafing; the hunted one runs
      var hunted = huntOn && prey === m;
      var a = (p.a + Math.sin(m.age * 0.6 + m.seed) * 8) * D2R, r = (p.r + Math.sin(m.age * 1.1 + m.seed) * 0.8 + (hunted ? 4 : 0)) * TILE;
      var tx = ox + Math.cos(a) * r, tz = oz + Math.sin(a) * r;
      if (lavaTo > 0) {
        // the lava: everyone crowds in from the rim
        var lx = tx - home.x, lz = tz - home.z, ld = Math.hypot(lx, lz), lim = lavaR - 1.5 * TILE;
        if (ld > lim) { tx = home.x + lx / ld * lim; tz = home.z + lz / ld * lim; }
      }
      // dodging: a step sideways from the nearest of his shots coming its way
      m.look -= dt;
      if (m.look <= 0) {
        m.look = 0.12;
        for (var i = 0; i < MAX; i++) {
          var s = shots[i];
          if (!s.on || !s.boss) continue;
          var rx = m.x - s.x, rz = m.z - s.z;
          if (rx * rx + rz * rz > DODGE * DODGE * 4) continue;
          if (rx * s.vx + rz * s.vz <= 0) continue; // moving away
          var sv = Math.hypot(s.vx, s.vz) || 1, side = (rx * -s.vz + rz * s.vx) / sv >= 0 ? 1 : -1;
          m.dx = -s.vz / sv * side * DODGE; m.dz = s.vx / sv * side * DODGE;
          break;
        }
      }
      m.dx *= Math.pow(0.15, dt); m.dz *= Math.pow(0.15, dt);
      tx += m.dx; tz += m.dz;
      // room to stand: pushed off the others (2.5 tiles apart) and off his body (3.5 tiles)
      party.forEach(function (o) {
        if (o === m) return;
        var ax = m.x - o.x, az = m.z - o.z, ad = Math.hypot(ax, az) || 0.01, room = 2.5 * TILE;
        if (ad < room) { tx += ax / ad * (room - ad) * 1.5; tz += az / ad * (room - ad) * 1.5; }
      });
      var bx = tx - ox, bz = tz - oz, bd = Math.hypot(bx, bz) || 0.01;
      if (bd < 3.5 * TILE && !oGone) { tx = ox + bx / bd * 3.5 * TILE; tz = oz + bz / bd * 3.5 * TILE; }
      var to = inArena({ x: tx, z: tz }, 0);
      tx = to.x; tz = to.z;
      var dx = tx - m.x, dz = tz - m.z, d = Math.hypot(dx, dz), v = 4 * TILE * dt;
      m.moving = d > 0.3;
      if (d > 0.01) { var st = Math.min(1, v / d); m.x += dx * st; m.z += dz * st; }
      m.g.position.set(m.x, 0, m.z);
      m.sh.position.set(m.x, 0.02, m.z);
      // facing him: the sprites face right, so those to his right turn round
      m.g.scale.x = m.x > ox ? -1 : 1;
      // choose what to shoot: his Wardens first, sometimes one of his Host, else him
      m.retarget -= dt;
      if (m.retarget <= 0 || (m.target && m.target !== "oryx" && !alive(m.target))) {
        m.retarget = 1 + Math.random();
        var foes = mobs.filter(function (f) { return alive(f) && f.way.hp > 0; });
        var wardens = foes.filter(function (f) { return f.name === "Ascended Warden"; });
        m.target = wardens.length ? wardens[Math.floor(Math.random() * wardens.length)]
          : foes.length && Math.random() < 0.45 ? foes[Math.floor(Math.random() * foes.length)] : oGone ? null : "oryx";
      }
      var tgt = m.target === "oryx" ? { x: ox, z: oz, r: 3.2, oryx: true } : m.target && alive(m.target) ? { x: m.target.x, z: m.target.z, r: 1.4, mob: m.target } : null;
      m.cool -= dt;
      if (tgt && m.cool <= 0 && m.cloak <= 0.5) {
        m.cool = p.every;
        var ang = Math.atan2(tgt.z - m.z, tgt.x - m.x) / D2R, dist = Math.hypot(tgt.z - m.z, tgt.x - m.x) - tgt.r;
        if (dist > 0) for (var j = 0; j < p.n; j++) {
          shootAt(m.x, m.z, ang - p.spr * (p.n - 1) / 2 + j * p.spr, p.speed, dist, p.c, 85, hitter(tgt));
        }
        m.atk = 0.2;
      }
      m.atk -= dt;
      // its ability, now and then
      m.abil -= dt;
      if (m.abil <= 0 && tgt) { m.abil = p.cd * (0.8 + Math.random() * 0.4); ability(m, tgt); }
      if (m.cloak > 0) { m.cloak -= dt; }
      m.mat.opacity = m.cloak > 0 ? 0.3 : 1;
      m.mat.emissive.setRGB(m.heal > 0 ? 0.1 : 0, m.heal > 0 ? 0.5 : 0, m.heal > 0 ? 0.15 : 0);
      if (m.heal > 0) m.heal -= dt;
      frameOf(m, m.age);
    });
  }
  function hitter(tgt) {
    return function (x, z) {
      sparks(x, z);
      if (tgt.mob) {
        if (alive(tgt.mob) && --tgt.mob.hp <= 0) kill(tgt.mob);
        if (Math.random() < 0.18) number(x, 4, z, 150 + Math.floor(Math.random() * 400), false);
      } else if (tgt.oryx && !oGone) {
        if (invulnerable()) { if (Math.random() < 0.08) number(x, ORYX_H * 0.7, z, 0, true); }
        else if (Math.random() < 0.14) number(x + (Math.random() - 0.5) * 8, ORYX_H * (0.55 + Math.random() * 0.4), z, 900 + Math.floor(Math.random() * 2600), false);
        hitT = 1;
      }
    };
  }
  function ability(m, tgt) {
    var p = m.p;
    switch (p.abil) {
      case "bomb": // the spell bomb bursts on its target
        setTimeout(function () { burst(tgt.x, tgt.z, p.c, 20); number(tgt.x, 6, tgt.z, 2400 + Math.floor(Math.random() * 1600), false); }, 350);
        pulse(tgt.x, tgt.z, 0xb08cff, 2.2, 0.5);
        break;
      case "arrow": // a heavy arrow from the quiver
        shootAt(m.x, m.z, Math.atan2(tgt.z - m.z, tgt.x - m.x) / D2R, 160, Math.hypot(tgt.z - m.z, tgt.x - m.x) - tgt.r, [0.8, 1, 0.6], 220, hitter(tgt));
        break;
      case "heal": // the tome heals everyone near
        pulse(m.x, m.z, 0x5be3a0, 6, 0.7);
        party.forEach(function (o) { if (Math.hypot(o.x - m.x, o.z - m.z) < 9 * TILE) o.heal = 0.6; });
        break;
      case "bash": // the shield stuns what it hits
        pulse(tgt.x, tgt.z, 0xffffff, 2, 0.4);
        burst(tgt.x, tgt.z, [1, 1, 1], 10);
        break;
      case "berserk": // the helm: a red ring of rage
        pulse(m.x, m.z, 0xff4a3a, 4, 0.6);
        m.cool = 0; p.every = 0.3; setTimeout(function () { p.every = 0.5; }, 3000);
        break;
      case "cloak": // the cloak: unseen for a moment
        m.cloak = 2.5;
        break;
    }
    m.atk = 0.3;
  }

  // --- The shots: a pool of glowing squares that fly over the floor -------------------------------
  var MAX = 3600;
  var bPos = new Float32Array(MAX * 3), bCol = new Float32Array(MAX * 3), bSize = new Float32Array(MAX);
  var shots = [];
  for (var n = 0; n < MAX; n++) shots.push({ on: false, x: 0, z: 0, vx: 0, vz: 0, t: 0, life: 1, c: null, s: 1, boss: false, end: null });
  var bGeo = new T.BufferGeometry();
  bGeo.setAttribute("position", new T.BufferAttribute(bPos, 3));
  bGeo.setAttribute("aColor", new T.BufferAttribute(bCol, 3));
  bGeo.setAttribute("aSize", new T.BufferAttribute(bSize, 1));
  var bUni = { uScale: { value: 600 } };
  var bullets = new T.Points(bGeo, new T.ShaderMaterial({
    uniforms: bUni, transparent: true, depthWrite: false, blending: T.AdditiveBlending,
    vertexShader:
      "attribute vec3 aColor; attribute float aSize; uniform float uScale; varying vec3 vC; varying float vF;" +
      "void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); vC = aColor;" +
      "vF = clamp((260.0 + mv.z) / 110.0, 0.0, 1.0);" +
      "gl_PointSize = aSize * uScale / -mv.z; gl_Position = projectionMatrix * mv; }",
    fragmentShader:
      "varying vec3 vC; varying float vF;" +
      "void main(){ vec2 p = abs(gl_PointCoord - 0.5);" +
      "float core = step(max(p.x, p.y), 0.13);" +             // a square shot, as the game draws them
      "float glow = smoothstep(0.5, 0.0, length(gl_PointCoord - 0.5)) * 0.55;" +
      "float a = (core + glow) * vF; gl_FragColor = vec4(mix(vC, vec3(1.0), core * 0.6) * a, a); }"
  }));
  bullets.frustumCulled = false;
  scene.add(bullets);

  // the colours of his shots (ORYX_SHOTS) and his helpers' (FIGHT, tools/build_elysium.py)
  var LANCE = [0.5, 0.78, 1], SUN = [1, 0.74, 0.28], SPIKE = [1, 0.95, 0.78], BEAM = [0.55, 0.72, 1],
    WAVE = [1, 0.25, 0.38], STILL = [0.45, 0.72, 1], WOUND = [1, 0.72, 0.3], SWIFT = [1, 1, 1],
    BLIGHT = [0.72, 0.45, 1], ORB = [0.62, 0.86, 1], SPEAR = [1, 0.93, 0.62];
  var SHOT_Y = 1.4, free = 0;
  // speed in the game's units (tenths of a tile a second), lifetime in milliseconds, size in %;
  // `boss`: one of his side's, which the players dodge; `end`: called where it stops
  function fire(x, z, deg, speed, life, c, size, boss, end) {
    for (var tries = 0; tries < MAX; tries++) {
      var s = shots[free];
      free = (free + 1) % MAX;
      if (s.on) continue;
      var v = speed / 10 * TILE * SLOW, a = deg * D2R;
      s.on = true; s.t = 0; s.life = life / 1000 / SLOW;
      s.x = x; s.z = z; s.vx = Math.cos(a) * v; s.vz = Math.sin(a) * v;
      s.c = c; s.s = (size || 100) / 100; s.boss = boss !== false; s.end = end || null;
      return;
    }
  }
  // a player's shot, flying just as far as its target
  function shootAt(x, z, deg, speed, dist, c, size, end) {
    fire(x, z, deg, speed, dist / (speed / 10 * TILE) * 1000, c, size, false, end);
  }
  // ShootFormation: count directions round him, each a set of (forward, sideways) offsets in tiles
  function formation(count, offsets, deg, speed, life, c, size) {
    var ox = oryx.position.x, oz = oryx.position.z;
    for (var k = 0; k < count; k++) {
      var a = (deg + k * 360 / count) * D2R, fx = Math.cos(a), fz = Math.sin(a);
      for (var i = 0; i < offsets.length; i += 2) {
        var f = offsets[i] * TILE, l = offsets[i + 1] * TILE;
        fire(ox + fx * f - fz * l, oz + fz * f + fx * l, deg + k * 360 / count, speed, life, c, size);
      }
    }
  }
  // Shoot: count shots spr degrees apart, centred on deg, from him or from `from`
  function spread(count, spr, deg, speed, life, c, size, from) {
    var x = from ? from.x : oryx.position.x, z = from ? from.z : oryx.position.z;
    for (var i = 0; i < count; i++) fire(x, z, deg - spr * (count - 1) / 2 + i * spr, speed, life, c, size);
  }
  function burst(x, z, c, n) { for (var i = 0; i < n; i++) fire(x, z, i * 360 / n + Math.random() * 10, 70, 420, c, 75, false); }
  function sparks(x, z) { for (var i = 0; i < 3; i++) fire(x, z, Math.random() * 360, 40, 200, [1, 1, 1], 55, false); }
  var LANES = [0, -0.55, 0, 0.55], ARROW = [0.7, 0, 0, -0.5, 0, 0.5, -0.7, -1.0, -0.7, 1.0];

  function moveShots(dt) {
    for (var i = 0; i < MAX; i++) {
      var s = shots[i];
      if (s.on) {
        s.t += dt;
        s.x += s.vx * dt; s.z += s.vz * dt;
        if (s.t > s.life) { s.on = false; if (s.end) { var f = s.end; s.end = null; f(s.x, s.z); } }
      }
      bPos[i * 3] = s.x; bPos[i * 3 + 1] = s.on ? SHOT_Y : -999; bPos[i * 3 + 2] = s.z;
      if (s.on) {
        var fade = Math.min(1, s.t * 8, (s.life - s.t) * 6 + (s.end ? 1 : 0));
        bCol[i * 3] = s.c[0] * fade; bCol[i * 3 + 1] = s.c[1] * fade; bCol[i * 3 + 2] = s.c[2] * fade;
        bSize[i] = s.s * 2.4;
      }
    }
    bGeo.attributes.position.needsUpdate = true;
    bGeo.attributes.aColor.needsUpdate = true;
    bGeo.attributes.aSize.needsUpdate = true;
  }

  // --- Damage numbers, rising where a shot hits -----------------------------------------------------
  var nums = [];
  for (n = 0; n < 28; n++) {
    var nc = document.createElement("canvas");
    nc.width = 160; nc.height = 48;
    var nt = new T.CanvasTexture(nc);
    var ns = new T.Sprite(new T.SpriteMaterial({ map: nt, transparent: true, depthWrite: false, depthTest: false }));
    ns.visible = false;
    ns.renderOrder = 10;
    scene.add(ns);
    nums.push({ s: ns, c: nc, tex: nt, t: 0, on: false });
  }
  var numFree = 0;
  function number(x, y, z, value, immune) {
    var u = nums[numFree];
    numFree = (numFree + 1) % nums.length;
    var g = u.c.getContext("2d");
    g.clearRect(0, 0, 160, 48);
    g.font = "bold 30px Silkscreen, monospace";
    g.textAlign = "center";
    g.lineWidth = 6;
    g.strokeStyle = "#000";
    var text = immune ? "Immune" : "-" + value.toLocaleString("en");
    g.strokeText(text, 80, 34);
    g.fillStyle = immune ? "#c6b4ff" : "#ff4d4d";
    g.fillText(text, 80, 34);
    u.tex.needsUpdate = true;
    u.s.position.set(x + (Math.random() - 0.5) * 3, y, z + 1);
    u.s.scale.set(6, 1.8, 1);
    u.s.visible = true; u.on = true; u.t = 0; u.y = y;
  }
  function stepNumbers(dt) {
    nums.forEach(function (u) {
      if (!u.on) return;
      u.t += dt;
      u.s.position.y = u.y + u.t * 5;
      u.s.material.opacity = Math.max(0, 1 - Math.max(0, u.t - 0.5) / 0.5);
      if (u.t > 1) { u.on = false; u.s.visible = false; }
    });
  }

  // --- His helpers: the Firmament's stars, the halo, rings on the floor and the lava ----------------
  var starGeo = new T.OctahedronGeometry(0.75);
  var stars = [];
  function raiseStars(transcended) {
    dropStars();
    // three rings of four along the diagonals, 3.5, 6 and 8.5 tiles out (SpawnRing)
    [[3.5, STILL, 55, 2000, 0], [6, WOUND, 55, 2000, 0.6], transcended ? [8.5, BLIGHT, 70, 1700, 1.2] : [8.5, SWIFT, 150, 900, 1.2]]
      .forEach(function (ring) {
        for (var k = 0; k < 4; k++) {
          var c = new T.Color(ring[1][0], ring[1][1], ring[1][2]);
          var g = new T.Group();
          g.add(new T.Mesh(starGeo, new T.MeshBasicMaterial({ color: c })));
          g.add(glow(c, 4));
          scene.add(g);
          stars.push({ g: g, r: ring[0] * TILE, a: 45 + k * 90, c: ring[1], speed: ring[2], life: ring[3], off: ring[4], born: clock });
        }
      });
  }
  function dropStars() { stars.forEach(function (s) { scene.remove(s.g); }); stars = []; }

  var halo = [];
  function raiseHalo() {
    dropHalo();
    for (var k = 0; k < 8; k++) {
      var g = new T.Group();
      g.add(new T.Mesh(new T.SphereGeometry(0.9, 12, 8), new T.MeshBasicMaterial({ color: 0xdff0ff })));
      g.add(glow(0x7fb4ff, 6));
      scene.add(g);
      halo.push({ g: g, a: k * 45, born: clock });
    }
  }
  function dropHalo() { halo.forEach(function (h) { scene.remove(h.g); }); halo = []; }

  // a ring on the floor: his bombs (they land after `delay`), the players' abilities
  var rings = [];
  var ringGeo = new T.RingGeometry(0.82, 1, 40);
  function pulse(x, z, color, tiles, dur, delay) {
    var m = new T.Mesh(ringGeo, new T.MeshBasicMaterial({ color: color, transparent: true, depthWrite: false, side: T.DoubleSide }));
    m.rotation.x = -Math.PI / 2;
    m.position.set(x, 0.06, z);
    m.visible = false;
    scene.add(m);
    rings.push({ m: m, t: -(delay || 0), r: tiles * TILE, dur: dur });
  }
  function bomb(x, z) { pulse(x, z, 0xe8c060, 2.5, 0.8, 0.7); }

  var lava = null, lavaTo = 0, lavaR = 12 * TILE;
  function setLava(inner) {
    if (!lava) {
      lava = new T.Mesh(new T.RingGeometry(0.97, 1, 72, 1), new T.MeshBasicMaterial({ color: 0xff5a1f, transparent: true, opacity: 0, depthWrite: false }));
      lava.rotation.x = -Math.PI / 2;
      lava.position.y = 0.05;
      scene.add(lava);
    }
    lavaTo = inner;
  }

  // --- The fight -------------------------------------------------------------------------------------
  var home = new T.Vector3(), aim = new T.Vector3(); // where he stands; the player he's after
  var prey = null, huntOn = false, hitT = 0;
  var CROSS_TURN = 15, SIEGE_TURN = 11.25;
  function angleTo(p) { return Math.atan2(p.z - oryx.position.z, p.x - oryx.position.x) / D2R; }
  // the arena's edge, so the fight stays in the picture: an ellipse round his place, 13 tiles across
  // either side, 6 behind and 7 in front (`pad` tiles less)
  function inArena(v, pad) {
    var dx = (v.x - home.x) / ((13 - (pad || 0)) * TILE), dz = (v.z - home.z) / (((v.z > home.z ? 7 : 6) - (pad || 0)) * TILE);
    var d = Math.sqrt(dx * dx + dz * dz);
    if (d > 1) { v.x = home.x + (v.x - home.x) / d; v.z = home.z + (v.z - home.z) / d; }
    return v;
  }
  // the time a timer of this period (and offset) went off between t0 and t1, or -1
  function tick(t0, t1, period, off) {
    off = off || 0;
    if (t1 < off) return -1;
    var k = Math.floor((t1 - off) / period);
    return k * period + off > t0 ? k : -1;
  }

  function cross(period) {
    return function (t0, t1) {
      var k;
      if ((k = tick(t0, t1, period, 0)) >= 0) {
        formation(4, LANES, CROSS_TURN * k, 110, 1600, LANCE, 100);
        formation(4, ARROW, 45 + CROSS_TURN * k, 90, 1800, SUN, 100);
        attack();
      }
      if ((k = tick(t0, t1, period, period / 2)) >= 0) {
        formation(4, LANES, 45 + CROSS_TURN * k, 110, 1600, LANCE, 100);
        formation(4, ARROW, CROSS_TURN * k, 90, 1800, SUN, 100);
        attack();
      }
      keepOff(6, period < 2 ? 0.8 : 0.7);
    };
  }
  function firmament(transcended) {
    return function (t0, t1) {
      if (t0 < 2.5 && t1 >= 2.5) { raiseStars(transcended); attack(); }
      goHome();
    };
  }
  var PREY = ["{}! I have chosen you.", "{}... you are judged.", "Run, {}. It changes nothing.", "Your god comes for you, {}!"];
  function hunt(transcended, minions) {
    return function (t0, t1, len) {
      var a = angleTo(aim);
      huntOn = t1 > len / 2;
      if (tick(t0, t1, 1.5, 0) >= 0) {
        // the shotgun in front: an arrowhead of spikes, fast in the middle, slower out to the sides
        fire(oryx.position.x, oryx.position.z, a, 120, 800, SPIKE, 130);
        spread(2, 30, a, 95, 950, SPIKE, 120);
        spread(2, 60, a, 75, 1100, SPIKE, 110);
        attack();
      }
      // the fan behind him: slowing beams, or bleeding waves once he has transcended
      if (tick(t0, t1, 0.9, 0.45) >= 0) spread(9, 14, a + 180, 70, 1600, transcended ? WAVE : BEAM, transcended ? 120 : 110);
      if (tick(t0, t1, transcended ? 2 : 2.6, 0.8) >= 0) bomb(aim.x + (Math.random() - 0.5) * 7 * TILE, aim.z + (Math.random() - 0.5) * 5 * TILE);
      // his Host rise beneath him (once he has transcended): a Noble, then a Cardinal
      if (minions && t0 < 0.6 && t1 >= 0.6) summon("Ascended Noble", oryx.position.x - 2, oryx.position.z + 1, 14);
      if (minions && t0 < len / 2 + 1.6 && t1 >= len / 2 + 1.6) summon("Ascended Cardinal", oryx.position.x + 2, oryx.position.z + 1, 12);
      // halfway, he stops, names one of the party and hunts them
      if (t0 < len / 2 && t1 >= len / 2) {
        prey = party[Math.floor(Math.random() * party.length)] || null;
        flash(0xffe070);
        if (prey) say(PREY[Math.floor(Math.random() * PREY.length)].replace("{}", prey.name));
      }
      if (t1 > len / 2 && t1 < len / 2 + 1.5) return; // the Pause: still, invulnerable
      chase(transcended ? 0.6 : 0.85, 2);
    };
  }
  function wrath(t0, t1) {
    var a = angleTo(aim);
    // the Cross, aimed: lances at a player along four lines, arrowheads between
    if (tick(t0, t1, 1.3, 0) >= 0) { formation(4, LANES, a, 110, 1600, LANCE, 100); attack(); }
    if (tick(t0, t1, 1.3, 0.65) >= 0) formation(4, ARROW, a + 45, 90, 1800, SUN, 100);
    chase(0.75, 3);
  }
  function siege(t0, t1, len) {
    var k;
    goHome();
    // the lava comes in a sixth of the way at a time from the rim (16.5 tiles to 9.5 in the game; the
    // arena here is drawn smaller)
    if ((k = tick(t0, t1, len / 7, 0.5)) >= 0) setLava((11.5 - Math.min(6, k) * 5 / 6) * TILE);
    if (tick(t0, t1, 2.8, 1.5) >= 0) { fire(oryx.position.x, oryx.position.z, angleTo(aim), 40, 4200, SPEAR, 220); attack(); }
    // the Radiant Cross as it began, slowed right down, each volley turned a little further
    if ((k = tick(t0, t1, 2.6, 0)) >= 0) {
      formation(4, LANES, SIEGE_TURN * k, 30, 6000, LANCE, 100);
      formation(4, ARROW, 45 + SIEGE_TURN * k, 26, 6500, SUN, 100);
    }
    if ((k = tick(t0, t1, 2.6, 1.3)) >= 0) {
      formation(4, LANES, 22.5 + SIEGE_TURN * k, 30, 6000, LANCE, 100);
      formation(4, ARROW, 67.5 + SIEGE_TURN * k, 26, 6500, SUN, 100);
    }
  }
  function wardens(t0, t1, len) {
    goHome();
    // untouchable while they live, he fires lances at the party; the last Warden falls by the end
    if (tick(t0, t1, 2, 1) >= 0) { spread(3, 12, angleTo(aim), 110, 1600, LANCE, 100); attack(); }
    if (t1 > len - 0.3) mobs.forEach(function (m) { if (m.name === "Ascended Warden") kill(m); });
  }
  function callHost() {
    // thrown 7 tiles out on the diagonals (TossObject)
    ["Ascended Officer", "Ascended Cleric", "Ascended Aristocrat", "Ascended Sergeant"].forEach(function (name, i) {
      var a = (45 + i * 90) * D2R;
      summon(name, home.x + Math.cos(a) * 7 * TILE, home.z + Math.sin(a) * 7 * TILE * 0.8, 30, { from: oryx.position });
    });
  }
  // below 90%, every second pattern he throws two splats of Divine Ichor, which hatch Ichor Sprites
  var patterns = 0;
  function splats() {
    if (++patterns % 2) return;
    for (var i = 0; i < 2; i++) {
      var a = Math.random() * Math.PI * 2, r = (4 + Math.random() * 5) * TILE;
      summon("Divine Ichor", oryx.position.x + Math.cos(a) * r, oryx.position.z + Math.sin(a) * r * 0.7, 2.4, { from: oryx.position, hatch: true });
    }
  }
  function idle() { goHome(); }
  function fallen(t0, t1) {
    // he is gone; the party gathers where he fell
    if (t0 === 0) {
      burst(oryx.position.x, oryx.position.z, [1, 0.95, 0.7], 40);
      burst(oryx.position.x, oryx.position.z, [0.6, 0.8, 1], 28);
      flash(0xffffff);
    }
  }

  // [what it's called, seconds, his words on entering, flash colour, what it does each frame,
  //  what it does on entering, a pattern (he throws splats every second one, once below 90%),
  //  whether he can be hurt]
  var PHASES = [
    ["Wardens", 9, "My Holy Gates open. Come, then, and kneel... if my Wardens let you.", 0, wardens, function () {
      oGone = false; gold = false; setForm("asc"); clearMobs(); patterns = 0; setLava(0); flash(0xffffff);
      for (var i = 0; i < 4; i++) summon("Ascended Warden", home.x, home.z, 60, { orbit: true, angle: i * Math.PI / 2 });
    }],
    ["Awakening", 3, "You struck down my Wardens. My gates are shut: you stand in MY heaven now.", 0xffe070, idle],
    ["Awakening", 2.5, "Kneel, and be JUDGED!", 0, idle],
    ["Judgement · the Radiant Cross", 7, null, 0, cross(2), null, false, true],
    ["Judgement · the Firmament", 9, null, 0, firmament(false), null, false, true],
    ["Judgement · the Divine Hunt", 8, null, 0, hunt(false, false), null, true, true],
    ["Call of the Host", 3.5, "Ascended Host! Descend, and defend your god!", 0xffe070, idle, callHost],
    ["Judgement Unbound · the Radiant Cross", 7, null, 0, cross(2), null, true, true],
    ["Judgement Unbound · the Divine Hunt", 7, null, 0, hunt(false, false), null, true, true],
    ["Transcendence", 3.5, "I... have... TRANSCENDED! Rise, mortals, whole again, so you may be broken anew!", 0xffffff, idle, function () {
      party.forEach(function (m) { m.heal = 1.2; pulse(m.x, m.z, 0x5be3a0, 1.6, 0.8); });
    }],
    ["Transcendent · the Radiant Cross", 6, null, 0, cross(1.6), null, true, true],
    ["Transcendent · the Firmament", 9, null, 0, firmament(true), null, true, true],
    ["Ascension", 3, "My halo rises! Ring this heaven, and let none stand outside its light!", 0xffe070, idle, raiseHalo],
    ["Ascendant · the Divine Hunt", 9, null, 0, hunt(true, true), null, true, true],
    ["Apotheosis", 3.5, "Behold my APOTHEOSIS! I am the light, and the light does not die!", 0xffe070, idle, function () { gold = true; setForm("asc"); }],
    ["Wrath · the Cross", 6, null, 0, wrath, null, true, true],
    ["Wrath · the Divine Hunt", 8, null, 0, hunt(true, true), null, true, true],
    ["Last Judgement", 13, "ENOUGH! The heavens themselves shall be your prison!", 0xffffff, siege, function () {
      dropHalo(); mobs.forEach(function (m) { if (m.name !== "Ascended Warden") kill(m); });
    }],
    ["Concession", 1.8, "I... concede. Strike, then, if you are worthy.", 0xffffff, idle, function () { gold = false; setForm("asc"); setLava(0); }],
    ["Concession · Oryx 4", 1.3, null, 0xff4400, idle, function () { setForm("o4"); }],
    ["Concession · Oryx 3", 1.3, null, 0x4060ff, idle, function () { setForm("o3"); }],
    ["Concession · Oryx 2", 1.3, null, 0xa01010, idle, function () { setForm("o2"); }],
    ["Concession · Oryx 1", 3.5, "...and in the end, I am only Oryx.", 0x6a4cff, idle, function () { setForm("o1"); }, false, true],
    ["Cast down", 4, null, 0, fallen, function () {
      oGone = true;
      announce("The party cast down Oryx the Ascended God in his Elysium! The Realm is free... for now.");
    }]
  ];
  var phase = -1, pTime = 0;
  function invulnerable() { var P = PHASES[phase]; return !P || !P[7]; }
  var phaseEl = document.getElementById("hero-phase"), sayEl = document.getElementById("hero-say"), sayTimer = 0;
  function say(text) {
    if (!sayEl) return;
    sayEl.innerHTML = "<b>Oryx the Ascended God:</b> ";
    sayEl.appendChild(document.createTextNode(text));
    sayEl.classList.add("on");
    sayTimer = 5;
  }
  function announce(text) {
    if (!sayEl) return;
    sayEl.textContent = text;
    sayEl.classList.add("on");
    sayTimer = 4;
  }
  function enter(i) {
    var P = PHASES[i];
    phase = i; pTime = 0; huntOn = false;
    if (P[0].indexOf("Firmament") < 0) dropStars();
    if (P[5]) P[5]();
    if (P[6]) splats();
    if (P[3]) flash(P[3]);
    if (P[2]) say(P[2]);
    // a new mark for his aimed attacks
    if (!prey || Math.random() < 0.5) prey = party[Math.floor(Math.random() * party.length)] || null;
    if (phaseEl) phaseEl.textContent = "Stage VI · " + P[0];
  }
  function runFight(dt) {
    if (phase < 0) enter(0);
    var P = PHASES[phase], t0 = pTime;
    pTime += dt;
    P[4](t0, pTime, P[1]);
    if (pTime >= P[1]) enter((phase + 1) % PHASES.length);
  }

  // moving: back to his place, after a player, or keeping a distance from them
  var walk = new T.Vector3(), stepDt = 0;
  function moveTo(x, z, speed) {
    walk.set(x - oryx.position.x, 0, z - oryx.position.z);
    var d = walk.length(), step = speed * 3.5 * TILE * stepDt;
    if (d > 0.05) { oryx.position.addScaledVector(walk, Math.min(1, step / d)); oMoving = true; inArena(oryx.position, 3); }
  }
  function goHome() { moveTo(home.x, home.z, 1.5); }
  function chase(speed, stop) {
    walk.set(oryx.position.x - aim.x, 0, oryx.position.z - aim.z);
    var d = walk.length();
    if (d > stop * TILE) moveTo(aim.x + walk.x / d * stop * TILE, aim.z + walk.z / d * stop * TILE, speed);
  }
  function keepOff(tiles, speed) {
    walk.set(oryx.position.x - aim.x, 0, oryx.position.z - aim.z);
    var d = walk.length() || 1;
    moveTo(aim.x + walk.x / d * tiles * TILE, aim.z + walk.z / d * tiles * TILE, speed * 0.6);
  }

  var flashCol = new T.Color(), flashT = 0;
  function flash(hex, amount) { flashCol.setHex(hex); flashT = amount || 1; }

  // --- Dust in the air --------------------------------------------------------------------------
  var DUST = 200, dPos = new Float32Array(DUST * 3), dust = [];
  for (n = 0; n < DUST; n++) dust.push({ x: (Math.random() - 0.5) * 140, y: Math.random() * 40, z: (Math.random() - 0.7) * 100, v: 0.2 + Math.random() * 0.6 });
  var dGeo = new T.BufferGeometry();
  dGeo.setAttribute("position", new T.BufferAttribute(dPos, 3));
  var motes = new T.Points(dGeo, new T.PointsMaterial({ color: 0xc6b4ff, size: 0.12, transparent: true, opacity: 0.6, depthWrite: false }));
  motes.frustumCulled = false;
  scene.add(motes);

  // --- Layout: beside the words on wide screens, above them on narrow ones -------------------------
  var wide = true, look = new T.Vector3(), DIST = 112;
  function place() {
    var w = stageEl.clientWidth || innerWidth, h = stageEl.clientHeight || innerHeight, aspect = w / h;
    wide = aspect > 1.05;
    var halfW = Math.tan(15 * Math.PI / 180) * DIST * aspect;
    var was = home.clone();
    // in the middle of the page; on smaller wide screens a little right, so the party keeps off the title
    home.set(wide ? halfW * Math.max(0, Math.min(0.28, (2000 - w) / 2000)) : 0, 0, wide ? 0 : -6);
    oryx.position.add(home).sub(was);
    party.forEach(function (m) { m.x += home.x - was.x; m.z += home.z - was.z; });
    renderer.setPixelRatio(pr * quality);
    renderer.setSize(w, h, false);
    camera.aspect = aspect;
    camera.fov = wide ? 30 : 44;
    // narrow: the picture moves up, so the fight is in the room left above the words
    // wide: in the middle of the page, a little up so the words sit under the fight
    if (wide) camera.setViewOffset(w, h, 0, h * 0.1, w, h); else camera.setViewOffset(w, h, 0, Math.min(h * 0.3, h / 2 - 190), w, h);
    camera.updateProjectionMatrix();
    bUni.uScale.value = h * pr * quality * 0.9 * (wide ? 1 : 0.7);
  }

  var mouse = { x: 0, y: 0, sx: 0, sy: 0 };
  addEventListener("pointermove", function (e) {
    mouse.x = e.clientX / innerWidth * 2 - 1;
    mouse.y = e.clientY / innerHeight * 2 - 1;
  }, { passive: true });
  var hero = stageEl.parentNode;

  // --- The loop ------------------------------------------------------------------------------------
  var still = reduce.matches, running = false, visible = true, last = 0, clock = 0, frames = 0, slow = 0, waited = 0;

  function step(dt) {
    stepDt = dt;
    clock += dt;
    oMoving = false;
    // everything drawn before the fight starts (or 4 s, whatever came)
    if (phase < 0) {
      waited += dt;
      if ((pending > 0 || !meshes.asc) && waited < 4) return;
      if (!meshes.asc) return;
      gatherParty();
    }
    // the player he's after
    if (prey && party.indexOf(prey) < 0) prey = null;
    var tgt = prey || party[0];
    if (tgt) aim.set(tgt.x, 0, tgt.z); else aim.set(home.x, 0, home.z + 9 * TILE);
    runFight(dt);
    stepMobs(dt);
    stepParty(dt);

    stars.forEach(function (s) {
      var age = clock - s.born, a = (s.a + age * 25) * D2R; // they turn round him at 25 degrees a second
      s.g.position.set(home.x + Math.cos(a) * s.r, 2.2 + Math.min(1, age) * 1.2, home.z + Math.sin(a) * s.r);
      s.g.children[0].rotation.y += dt * 2;
      var k, j;
      if ((k = tick(age - dt, age, 1.8, s.off)) >= 0) for (j = 0; j < 4; j++) fire(s.g.position.x, s.g.position.z, 15 * k + j * 90, s.speed, s.life, s.c, 90);
      if ((k = tick(age - dt, age, 1.8, s.off + 0.25)) >= 0) for (j = 0; j < 4; j++) fire(s.g.position.x, s.g.position.z, 15 * k + j * 90, s.speed, s.life, s.c, 90);
    });
    halo.forEach(function (h) {
      var age = clock - h.born, a = (h.a + age * 6) * D2R, R = 11 * TILE;
      h.g.position.set(home.x + Math.cos(a) * R, Math.min(1, age / 1.5) * 3.5, home.z + Math.sin(a) * R);
      // firing in at the arena's middle
      if (tick(age - dt, age, 2.4, 1.5 + (h.a / 45) * 0.15) >= 0) spread(3, 20, a / D2R + 180, 55, 2400, ORB, 110, h.g.position);
    });
    for (var b = rings.length - 1; b >= 0; b--) {
      var ri = rings[b];
      ri.t += dt;
      if (ri.t < 0) continue;
      ri.m.visible = true;
      var r = ri.r * Math.min(1, ri.t * 4 / ri.dur + 0.2);
      ri.m.scale.set(r, r, r);
      ri.m.material.opacity = Math.max(0, 1 - ri.t / ri.dur);
      if (ri.t > ri.dur) { scene.remove(ri.m); rings.splice(b, 1); }
    }
    if (lava) {
      var outer = 12 * TILE, want = lavaTo || 11.9 * TILE;
      lavaR += (want - lavaR) * Math.min(1, dt * 2);
      lava.geometry.dispose();
      lava.geometry = new T.RingGeometry(lavaR / outer, 1, 72, 1);
      lava.scale.set(outer, outer, 1);
      lava.position.x = home.x; lava.position.z = home.z;
      lava.material.opacity += ((lavaTo > 0 ? 0.75 + Math.sin(clock * 3) * 0.1 : 0) - lava.material.opacity) * Math.min(1, dt * 3);
    }
    moveShots(dt);
    stepNumbers(dt);
    if (sayTimer > 0 && (sayTimer -= dt) <= 0 && sayEl) sayEl.classList.remove("on");
  }

  function frame(t) {
    var dt = Math.min(0.05, (t - (last || t)) / 1000);
    last = t;
    if (!still) step(dt);
    var e = still ? 1 : 1 - Math.exp(-dt * 3);
    mouse.sx += (mouse.x - mouse.sx) * e;
    mouse.sy += (mouse.y - mouse.sy) * e;

    showForm();
    swell = Math.max(0, swell - dt * 2.5);
    body.scale.setScalar(1 + swell * 0.18);
    body.position.y = 1 + Math.sin(clock * 0.9) * 0.4;
    // a little turn only: a flat sprite reads best facing you
    body.rotation.y = mouse.sx * 0.22 + Math.sin(clock * 0.35) * 0.08;
    flashT = Math.max(0, flashT - dt * 1.6);
    hitT = Math.max(0, hitT - dt * 8);
    mat.emissive.copy(flashCol).multiplyScalar(flashT * 0.8).addScalar(hitT * 0.06);
    pool.color.set(gold ? 0xffc65c : lavaTo > 0 ? 0xff6a2a : 0x7fb4ff);
    pool.intensity = 1.4 + Math.sin(clock * 2.1) * 0.25 + flashT * 2.5;
    pool.position.set(oryx.position.x, 5, oryx.position.z + 5);
    oShadow.position.set(oryx.position.x, 0.02, oryx.position.z);
    oShadow.visible = !oGone;

    for (var i = 0; i < DUST; i++) {
      var d = dust[i];
      if (!still) { d.y += d.v * dt; if (d.y > 40) d.y = 0; }
      dPos[i * 3] = d.x + Math.sin(clock * 0.3 + i) * 0.8; dPos[i * 3 + 1] = d.y; dPos[i * 3 + 2] = d.z;
    }
    dGeo.attributes.position.needsUpdate = true;

    // the camera stands back and above, drifts with the mouse, and lifts as you scroll away
    var scroll = Math.min(1, scrollY / (innerHeight || 800));
    camera.position.set(mouse.sx * 3, (wide ? 46 : 58) + scroll * 10 - mouse.sy * 1.5, (wide ? DIST : 132) + scroll * 14);
    look.set(0, wide ? 6 : 4, 0);
    camera.lookAt(look);
    renderer.render(scene, camera);

    if (!still && ++frames > 40) {
      if (dt > 1 / 28) slow++; else slow = Math.max(0, slow - 1);
      if (slow > 45 && quality > 0.5) { quality -= 0.25; slow = 0; place(); }
      frames = 41;
    }
    if (!still && visible && !document.hidden) requestAnimationFrame(frame); else running = false;
  }
  function start() {
    if (running || still || !visible) return;
    running = true;
    last = 0;
    requestAnimationFrame(frame);
  }
  function draw() { last = 0; frame(performance.now()); }

  if ("IntersectionObserver" in window) {
    new IntersectionObserver(function (en) {
      visible = en[0].isIntersecting;
      if (visible) start();
    }).observe(hero);
  }
  addEventListener("resize", function () { place(); if (still) draw(); });
  document.addEventListener("visibilitychange", function () { if (!document.hidden) start(); });
  if (reduce.addEventListener) reduce.addEventListener("change", function () { still = reduce.matches; if (still) stillFrame(); else start(); });

  place();
  oryx.position.copy(home);
  // with reduced motion: one still frame once everything is drawn, a few seconds into the fight
  function stillFrame() {
    if (pending > 0) { setTimeout(stillFrame, 200); return; }
    for (var s0 = 0; s0 < 30 * 16 && phase < 3; s0++) step(1 / 30);
    for (s0 = 0; s0 < 45; s0++) step(1 / 30);
    if (sayEl) sayEl.classList.remove("on");
    draw();
  }
  if (still) stillFrame(); else start();

  // for tests: jump to a phase by (part of) its name, and what's going on
  window.dodScene = {
    renderer: renderer, scene: scene, camera: camera, draw: draw,
    go: function (name) {
      for (var i = 0; i < PHASES.length; i++) if (PHASES[i][0].indexOf(name) >= 0) { enter(i); return PHASES[i][0]; }
      return null;
    },
    state: function () {
      return { pending: pending, forms: Object.keys(meshes), kinds: Object.keys(KINDS).filter(function (k) { return KINDS[k].frames[0]; }),
        phase: phase >= 0 ? PHASES[phase][0] : null, quality: quality, running: running,
        shots: shots.filter(function (s) { return s.on; }).length, mobs: mobs.map(function (m) { return m.name; }),
        party: party.map(function (m) { return m.name; }), prey: prey && prey.name, stars: stars.length, halo: halo.length };
    }
  };
})();
