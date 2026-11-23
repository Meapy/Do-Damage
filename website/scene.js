// The home page's hero, drawn with Three.js (vendor/three.min.js): Oryx the Mad God built in cubes,
// one per pixel of his sprite in the game (the hero's <i data-boss="<sheet> x y w h">, written by
// tools/build_wiki.py), thicker towards the middle so he has a body when he turns. He floats over a
// dark floor in a red glow and fires his patterns around himself: rings, spirals and double rings,
// as in the fight. He follows the mouse, and a click on the hero makes him fire at once.
// Nothing here is needed to read the page: without WebGL the sprite itself stands in the hero, with
// reduced motion one still frame is drawn, and nothing is drawn while the hero is off screen.
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

  var INK = 0x0b0a0a;
  var pr = Math.min(devicePixelRatio || 1, 1.75);
  var quality = 1; // lowered when frames are slow
  renderer.setClearColor(INK, 1);
  var scene = new T.Scene();
  scene.fog = new T.Fog(INK, 85, 210);
  var camera = new T.PerspectiveCamera(30, 1, 0.5, 400);

  // --- Light: a warm key from the upper left, a red rim from behind, red from the floor ----------
  scene.add(new T.HemisphereLight(0xd9d0c2, 0x1c0d0b, 0.5));
  var key = new T.DirectionalLight(0xfff1dc, 1.6);
  key.position.set(-1.2, 1.4, 1.6);
  scene.add(key);
  var rim = new T.DirectionalLight(0xff3b2b, 2.4);
  rim.position.set(1.6, 0.5, -1.6);
  scene.add(rim);
  var pool = new T.PointLight(0xff3a26, 2.2, 46, 1.6);
  scene.add(pool);

  // --- The floor: dark stone, a grid of faint points, and a soft shadow under him ---------------
  var floor = new T.Mesh(new T.PlaneGeometry(400, 400), new T.MeshStandardMaterial({ color: 0x110f0e, roughness: 0.92, metalness: 0 }));
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
  var shadow = new T.Mesh(new T.PlaneGeometry(1, 1), new T.MeshBasicMaterial({
    map: radial([[0, "rgba(0,0,0,.85)"], [0.6, "rgba(0,0,0,.35)"], [1, "rgba(0,0,0,0)"]]), transparent: true, depthWrite: false }));
  shadow.rotation.x = -Math.PI / 2;
  shadow.position.y = 0.02;
  scene.add(shadow);

  var dotsG = new T.BufferGeometry(), dots = [];
  for (var gx = -60; gx <= 60; gx += 3) for (var gz = -60; gz <= 30; gz += 3) dots.push(gx, 0.03, gz);
  dotsG.setAttribute("position", new T.Float32BufferAttribute(dots, 3));
  var grid = new T.Points(dotsG, new T.PointsMaterial({ color: 0x4a403b, size: 0.16, sizeAttenuation: true, transparent: true, opacity: 0.8 }));
  scene.add(grid);

  // --- Oryx, from his sprite ---------------------------------------------------------------------
  var oryx = new T.Group();
  scene.add(oryx);
  var body = new T.Group(); // turned by the mouse inside oryx, which is placed and bobs
  oryx.add(body);
  var SIZE = 20; // how tall he stands, in scene units
  var m4 = new T.Matrix4();

  function build(px, w, h) {
    // keep the opaque pixels, and for each how far it is from the edge of the silhouette
    var solid = [], depth = new Int16Array(w * h).fill(-1), queue = [];
    for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) {
      var o = (y * w + x) * 4;
      solid[y * w + x] = px[o + 3] > 40;
    }
    for (y = 0; y < h; y++) for (x = 0; x < w; x++) {
      if (!solid[y * w + x]) continue;
      var edge = x === 0 || y === 0 || x === w - 1 || y === h - 1 ||
        !solid[y * w + x - 1] || !solid[y * w + x + 1] || !solid[(y - 1) * w + x] || !solid[(y + 1) * w + x];
      if (edge) { depth[y * w + x] = 0; queue.push(y * w + x); }
    }
    for (var q = 0; q < queue.length; q++) {
      var i = queue[q], cx = i % w, cy = (i - cx) / w;
      [[1, 0], [-1, 0], [0, 1], [0, -1]].forEach(function (d) {
        var nx = cx + d[0], ny = cy + d[1];
        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
        var n = ny * w + nx;
        if (solid[n] && depth[n] < 0) { depth[n] = depth[i] + 1; queue.push(n); }
      });
    }
    var cells = [], x0 = w, x1 = 0, y0 = h, y1 = 0;
    for (y = 0; y < h; y++) for (x = 0; x < w; x++) {
      if (!solid[y * w + x]) continue;
      var k = (y * w + x) * 4;
      cells.push({ x: x, y: y, d: depth[y * w + x], r: px[k], g: px[k + 1], b: px[k + 2] });
      x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y);
    }
    if (!cells.length) return;
    var cw = x1 - x0 + 1, ch = y1 - y0 + 1, unit = SIZE / ch;
    var mesh = new T.InstancedMesh(new T.BoxGeometry(unit * 0.94, unit * 0.94, unit),
      new T.MeshStandardMaterial({ color: 0xffffff, roughness: 0.55, metalness: 0.12 }), cells.length);
    var col = new T.Color(), s3 = new T.Vector3(), p3 = new T.Vector3(), q4 = new T.Quaternion();
    cells.forEach(function (c, n) {
      var thick = 1.2 + Math.min(c.d, 5) * 0.85; // a rounded body: thin at the outline, deep in the middle
      m4.compose(p3.set((c.x - x0 - cw / 2 + 0.5) * unit, (ch - (c.y - y0) - 0.5) * unit, 0), q4, s3.set(1, 1, thick));
      mesh.setMatrixAt(n, m4);
      // the sheets' near-black outline lifted a little, so his edge still reads against the night
      col.setRGB(Math.max(c.r, 30) / 255, Math.max(c.g, 24) / 255, Math.max(c.b, 24) / 255);
      mesh.setColorAt(n, col);
    });
    body.add(mesh);
    ready = true;
    if (still) draw();
  }

  var ready = false;
  (function load() {
    var p = art.getAttribute("data-boss").split(" ");
    var img = new Image();
    img.onload = function () {
      var w = +p[3], h = +p[4], c = document.createElement("canvas");
      c.width = w; c.height = h;
      var g = c.getContext("2d");
      g.drawImage(img, +p[1], +p[2], w, h, 0, 0, w, h);
      try { build(g.getImageData(0, 0, w, h).data, w, h); } catch (e) { /* a tainted sheet: no Oryx */ }
    };
    img.src = p[0];
  })();

  // --- His shots: a pool of glowing squares that fly out from him -------------------------------
  var MAX = 1400;
  var bPos = new Float32Array(MAX * 3), bCol = new Float32Array(MAX * 3), bSize = new Float32Array(MAX);
  var shots = [];
  for (var n = 0; n < MAX; n++) shots.push({ on: false, x: 0, y: 0, z: 0, vx: 0, vz: 0, t: 0 });
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
      "vF = clamp((210.0 + mv.z) / 90.0, 0.0, 1.0);" +
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

  var RED = [1, 0.27, 0.2], BONE = [0.96, 0.9, 0.82], GOLD = [1, 0.74, 0.4];
  var free = 0;
  function fire(angle, speed, y, c, size) {
    for (var tries = 0; tries < MAX; tries++) {
      var s = shots[free];
      free = (free + 1) % MAX;
      if (!s.on) {
        s.on = true; s.t = 0;
        s.x = oryx.position.x; s.y = y; s.z = oryx.position.z;
        s.vx = Math.cos(angle) * speed; s.vz = Math.sin(angle) * speed;
        s.c = c; s.s = size;
        return;
      }
    }
  }
  function ring(count, speed, y, c, offset, size) {
    for (var i = 0; i < count; i++) fire(offset + i / count * Math.PI * 2, speed, y, c, size || 1);
  }

  // the patterns he cycles through, each a few seconds long
  var pattern = 0, pTime = 0, pStep = 0, spin = 0;
  var PATTERNS = [
    { len: 2.6, every: 0.65, step: function (k, y) { ring(30, 10, y, k % 2 ? BONE : RED, k * 0.1); } },
    { len: 2.4, every: 0.07, step: function (k, y) { spin += 0.21; for (var a = 0; a < 4; a++) fire(spin + a * Math.PI / 2, 12, y, a % 2 ? GOLD : RED, 0.9); } },
    { len: 2.8, every: 0.9, step: function (k, y) { ring(22, 8, y, RED, 0); ring(22, 11, y - 0.3, BONE, Math.PI / 22, 0.8); } },
    { len: 2.2, every: 0.11, step: function (k, y) { spin -= 0.37; fire(spin, 15, y, BONE, 1.1); fire(spin + Math.PI, 15, y, BONE, 1.1); fire(-spin * 0.7, 9, y - 0.3, RED, 0.8); } }
  ];
  function shoot(dt) {
    var P = PATTERNS[pattern], y = 1.4;
    pTime += dt;
    while (pStep * P.every <= pTime && pTime < P.len) { P.step(pStep, y); pStep++; }
    if (pTime > P.len + 0.6) { pattern = (pattern + 1) % PATTERNS.length; pTime = 0; pStep = 0; }
  }

  function moveShots(dt) {
    for (var i = 0; i < MAX; i++) {
      var s = shots[i];
      if (s.on) {
        s.t += dt;
        s.x += s.vx * dt; s.z += s.vz * dt;
        if (s.t > 7) s.on = false;
      }
      bPos[i * 3] = s.x; bPos[i * 3 + 1] = s.on ? s.y : -999; bPos[i * 3 + 2] = s.z;
      if (s.on) {
        var fade = Math.min(1, s.t * 6);
        bCol[i * 3] = s.c[0] * fade; bCol[i * 3 + 1] = s.c[1] * fade; bCol[i * 3 + 2] = s.c[2] * fade;
        bSize[i] = s.s * 2.6;
      }
    }
    bGeo.attributes.position.needsUpdate = true;
    bGeo.attributes.aColor.needsUpdate = true;
    bGeo.attributes.aSize.needsUpdate = true;
  }

  // --- Dust in the air --------------------------------------------------------------------------
  var DUST = 220, dPos = new Float32Array(DUST * 3), dust = [];
  for (n = 0; n < DUST; n++) dust.push({ x: (Math.random() - 0.5) * 120, y: Math.random() * 40, z: (Math.random() - 0.7) * 90, v: 0.2 + Math.random() * 0.6 });
  var dGeo = new T.BufferGeometry();
  dGeo.setAttribute("position", new T.BufferAttribute(dPos, 3));
  var motes = new T.Points(dGeo, new T.PointsMaterial({ color: 0x8a7d72, size: 0.12, transparent: true, opacity: 0.7, depthWrite: false }));
  motes.frustumCulled = false;
  scene.add(motes);

  // --- Layout: beside the words on wide screens, above them on narrow ones -------------------------
  var wide = true, look = new T.Vector3(), DIST = 90;
  function place() {
    var w = stageEl.clientWidth || innerWidth, h = stageEl.clientHeight || innerHeight, aspect = w / h;
    wide = aspect > 1.05;
    // wide: in the right half, clear of the words; narrow: in the middle, above them
    var halfW = Math.tan(15 * Math.PI / 180) * DIST * aspect;
    oryx.position.set(wide ? halfW * 0.5 : 0, 3.5, 0);
    oryx.scale.setScalar(wide ? 1 : 0.66);
    renderer.setPixelRatio(pr * quality);
    renderer.setSize(w, h, false);
    camera.aspect = aspect;
    camera.fov = wide ? 30 : 34;
    // narrow: the picture moves up, so he stands in the room left above the words
    if (wide) camera.clearViewOffset(); else camera.setViewOffset(w, h, 0, Math.min(h * 0.3, h / 2 - 190), w, h);
    camera.updateProjectionMatrix();
    bUni.uScale.value = h * pr * quality * 0.9;
    shadow.position.x = oryx.position.x;
    shadow.scale.set(SIZE * 1.1, SIZE * 0.55, 1);
    pool.position.set(oryx.position.x, 4, 4);
  }

  var mouse = { x: 0, y: 0, sx: 0, sy: 0 };
  addEventListener("pointermove", function (e) {
    mouse.x = e.clientX / innerWidth * 2 - 1;
    mouse.y = e.clientY / innerHeight * 2 - 1;
  }, { passive: true });
  var hero = stageEl.parentNode;
  hero.addEventListener("pointerdown", function (e) {
    if (e.target.closest("a, button, input, .stats")) return;
    var y = 1.4;
    ring(40, 14, y, GOLD, Math.random(), 1.1);
    ring(40, 10, y - 0.3, RED, Math.random(), 0.9);
    if (still) { moveShots(0.4); draw(); }
  });

  // --- The loop ------------------------------------------------------------------------------------
  var still = reduce.matches, running = false, visible = true, last = 0, clock = 0, frames = 0, slow = 0;

  function frame(t) {
    var dt = Math.min(0.05, (t - (last || t)) / 1000);
    last = t;
    if (!still) clock += dt;
    var e = still ? 1 : 1 - Math.exp(-dt * 3);
    mouse.sx += (mouse.x - mouse.sx) * e;
    mouse.sy += (mouse.y - mouse.sy) * e;

    var scroll = Math.min(1, scrollY / (innerHeight || 800));
    oryx.position.y = 3.5 + Math.sin(clock * 0.9) * 0.6;
    body.rotation.y = mouse.sx * 0.55 + Math.sin(clock * 0.35) * 0.18 - (wide ? 0.12 : 0);
    body.rotation.x = mouse.sy * 0.08;
    pool.intensity = 2 + Math.sin(clock * 2.1) * 0.35;

    if (!still && ready) shoot(dt);
    if (!still) moveShots(dt);
    for (var i = 0; i < DUST; i++) {
      var d = dust[i];
      if (!still) { d.y += d.v * dt; if (d.y > 40) d.y = 0; }
      dPos[i * 3] = d.x + Math.sin(clock * 0.3 + i) * 0.8; dPos[i * 3 + 1] = d.y; dPos[i * 3 + 2] = d.z;
    }
    dGeo.attributes.position.needsUpdate = true;

    // the camera stands back and a little above, drifts with the mouse, and lifts as you scroll away
    var ox = oryx.position.x;
    camera.position.set(mouse.sx * 3, (wide ? 30 : 34) + scroll * 10 - mouse.sy * 1.5, (wide ? DIST : 100) + scroll * 14);
    look.set(0, wide ? 9 : 10, 0);
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
  if (reduce.addEventListener) reduce.addEventListener("change", function () { still = reduce.matches; if (still) draw(); else start(); });

  place();
  if (still) {
    // one still frame, with a ring of his shots already in the air
    ring(30, 10, 1.4, RED, 0);
    ring(30, 7, 1.2, BONE, 0.1, 0.8);
    moveShots(1.6);
    draw();
  } else {
    start();
  }

  window.dodScene = { renderer: renderer, scene: scene, camera: camera, draw: draw,
    state: function () { return { ready: ready, quality: quality, running: running, shots: shots.filter(function (s) { return s.on; }).length }; } };
})();
