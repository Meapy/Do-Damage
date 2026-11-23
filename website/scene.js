// The site's 3D backdrop, drawn with Three.js (vendor/three.min.js): a night over a castle town, and
// below it Oryx's six stages as glowing portals. The camera follows the page: the home page flies
// forward through the castle gate and from portal to portal as you read "Six stages down"; every
// other page keeps a calm drifting sky. Nothing here is needed to read the site: without WebGL the
// page keeps its plain dark background, and with reduced motion the scene is drawn still.
(function () {
  "use strict";
  var T = window.THREE;
  if (!T) return;

  var calm = document.body.classList.contains("sub-page");
  var reduce = matchMedia("(prefers-reduced-motion: reduce)");
  var canvas = document.createElement("canvas");
  canvas.id = "scene";
  canvas.setAttribute("aria-hidden", "true");
  var renderer;
  try {
    renderer = new T.WebGLRenderer({ canvas: canvas, antialias: false, powerPreference: "low-power" });
  } catch (e) {
    return;
  }
  document.body.insertBefore(canvas, document.body.firstChild);
  document.documentElement.classList.add("has-scene");

  var pr = Math.min(devicePixelRatio || 1, 1.5);
  var quality = 1; // lowered when the frame rate is poor
  renderer.setPixelRatio(pr);
  var scene = new T.Scene();
  scene.fog = new T.FogExp2(0x0a0614, 0.0062);
  var camera = new T.PerspectiveCamera(58, 1, 0.5, 1800);

  function rng(seed) {
    return function () {
      seed = (seed * 1664525 + 1013904223) >>> 0;
      return seed / 4294967296;
    };
  }
  var rand = rng(20261122);

  // --- Soft glowing points: stars, windows, embers, portal sparks --------------------------------
  var uniforms = { uTime: { value: 0 }, uScale: { value: 600 } };
  function pointsMaterial(fixed) {
    return new T.ShaderMaterial({
      uniforms: uniforms,
      transparent: true, depthWrite: false, blending: T.AdditiveBlending,
      vertexShader:
        "attribute float aSize; attribute float aPhase; attribute float aSpeed; attribute vec3 aColor;" +
        "uniform float uTime; uniform float uScale; varying vec3 vC; varying float vA;" +
        "void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0);" +
        "float tw = 0.65 + 0.35 * sin(uTime * aSpeed + aPhase);" +
        "vA = tw; vC = aColor;" +
        (fixed ? "gl_PointSize = aSize * uScale * 0.0025;"
               : "gl_PointSize = clamp(aSize * uScale / -mv.z, 1.0, 40.0);") +
        "gl_Position = projectionMatrix * mv; }",
      fragmentShader:
        "varying vec3 vC; varying float vA;" +
        "void main(){ float d = length(gl_PointCoord - 0.5);" +
        "float a = smoothstep(0.5, 0.0, d); gl_FragColor = vec4(vC * a * vA, a * vA); }"
    });
  }
  function points(n, fill, fixed) {
    var pos = new Float32Array(n * 3), size = new Float32Array(n), phase = new Float32Array(n),
      speed = new Float32Array(n), col = new Float32Array(n * 3), c = new T.Color();
    for (var i = 0; i < n; i++) {
      var o = fill(i);
      pos[i * 3] = o.x; pos[i * 3 + 1] = o.y; pos[i * 3 + 2] = o.z;
      size[i] = o.s; phase[i] = rand() * 6.28; speed[i] = o.sp || 0.6 + rand() * 2;
      c.set(o.c);
      col[i * 3] = c.r; col[i * 3 + 1] = c.g; col[i * 3 + 2] = c.b;
    }
    var g = new T.BufferGeometry();
    g.setAttribute("position", new T.BufferAttribute(pos, 3));
    g.setAttribute("aSize", new T.BufferAttribute(size, 1));
    g.setAttribute("aPhase", new T.BufferAttribute(phase, 1));
    g.setAttribute("aSpeed", new T.BufferAttribute(speed, 1));
    g.setAttribute("aColor", new T.BufferAttribute(col, 3));
    var p = new T.Points(g, pointsMaterial(fixed));
    p.frustumCulled = false;
    return p;
  }

  // --- Sky: a gradient dome, the moon and the stars; all follow the camera ------------------------
  var sky = new T.Group();
  scene.add(sky);
  var domeU = { top: { value: new T.Color(0x05030b) }, mid: { value: new T.Color(0x1a0f33) }, glow: { value: new T.Color(0x6a2446) } };
  var dome = new T.Mesh(new T.SphereGeometry(1200, 24, 16), new T.ShaderMaterial({
    side: T.BackSide, depthWrite: false, fog: false, uniforms: domeU,
    vertexShader: "varying float vY; void main(){ vY = normalize(position).y; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }",
    fragmentShader: "uniform vec3 top; uniform vec3 mid; uniform vec3 glow; varying float vY;" +
      "void main(){ float h = clamp(vY, -0.2, 1.0);" +
      "vec3 c = mix(glow, mid, smoothstep(0.0, 0.28, h)); c = mix(c, top, smoothstep(0.2, 0.85, h));" +
      "c = mix(c * 0.35, c, smoothstep(-0.2, 0.0, h)); gl_FragColor = vec4(c, 1.0); }"
  }));
  sky.add(dome);

  sky.add(points(calm ? 1400 : 2600, function () {
    var u = rand() * 2 - 1, a = rand() * 6.283, r = Math.sqrt(1 - u * u), d = 900;
    var y = Math.abs(u) * 0.95 + 0.05; // stars sit above the horizon
    var cc = rand();
    return { x: Math.cos(a) * r * d, y: y * d, z: Math.sin(a) * r * d, s: rand() < 0.06 ? 3.4 : 1.1 + rand() * 1.2,
      c: cc < 0.15 ? 0xffd99a : cc < 0.3 ? 0xc6b4ff : 0xffffff };
  }, true));

  function glowTexture(inner, outer) {
    var c = document.createElement("canvas");
    c.width = c.height = 128;
    var g = c.getContext("2d"), grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    grad.addColorStop(0, inner);
    grad.addColorStop(1, outer);
    g.fillStyle = grad;
    g.fillRect(0, 0, 128, 128);
    return new T.CanvasTexture(c);
  }
  var moon = new T.Mesh(new T.SphereGeometry(34, 32, 24), new T.MeshBasicMaterial({ color: 0xfbeed2, fog: false }));
  moon.position.set(300, 280, -820);
  sky.add(moon);
  var halo = new T.Sprite(new T.SpriteMaterial({ map: glowTexture("rgba(255,226,170,.55)", "rgba(255,226,170,0)"),
    blending: T.AdditiveBlending, depthWrite: false, fog: false }));
  halo.scale.set(360, 360, 1);
  halo.position.copy(moon.position);
  sky.add(halo);

  // --- The town and the castle ------------------------------------------------------------------
  var ground = new T.Mesh(new T.PlaneGeometry(2400, 2400), new T.MeshBasicMaterial({ color: 0x0a0713 }));
  ground.rotation.x = -Math.PI / 2;
  scene.add(ground);

  var HOME = !calm;
  var boxes = [], roofs = [], lights = [];
  function building(x, z, w, h, d, roof) {
    boxes.push({ x: x, y: h / 2, z: z, w: w, h: h, d: d });
    if (roof) roofs.push({ x: x, y: h + roof / 2, z: z, r: Math.min(w, d) * 0.78, h: roof });
    var rows = Math.floor(h / 3.2), cols = Math.max(1, Math.floor(w / 2.6));
    for (var ry = 1; ry < rows; ry++) {
      for (var cx = 0; cx < cols; cx++) {
        if (rand() < 0.2) {
          lights.push({ x: x - w / 2 + (cx + 0.5) * (w / cols), y: ry * 3.2, z: z + d / 2 + 0.1,
            s: 0.9, c: rand() < 0.2 ? 0xff9a4d : 0xffc65c, sp: rand() < 0.3 ? 5 + rand() * 4 : 0.3 });
        }
      }
    }
  }
  if (HOME) {
    for (var x = -420; x < 420; x += 6 + rand() * 7) {
      if (Math.abs(x) < 17) continue; // the road to the gate stays clear
      for (var layer = 0; layer < 3; layer++) {
        if (rand() < 0.35) continue;
        var w = 5 + rand() * 7, h = 5 + Math.pow(rand(), 1.7) * (10 + layer * 9);
        building(x + rand() * 4, -62 - layer * 34 - rand() * 12, w, h, 5 + rand() * 6, rand() < 0.35 ? 4 + rand() * 8 : 0);
      }
    }
    // the castle: a wall with two keeps either side of the gate
    [-1, 1].forEach(function (side) {
      building(side * 17, -120, 10, 40, 10, 18);
      building(side * 32, -120, 20, 17, 6, 0);
      building(side * 52, -120, 20, 17, 6, 0);
      building(side * 70, -120, 10, 30, 10, 14);
      for (var k = 0; k < 8; k++) boxes.push({ x: side * (24 + k * 5.2), y: 18.2, z: -120, w: 2.2, h: 2.4, d: 6 });
      for (var wy = 10; wy < 38; wy += 8) {
        lights.push({ x: side * 17, y: wy, z: -114.9, s: 1.4, c: 0xff9a4d, sp: 4 });
      }
    });
    boxes.push({ x: 0, y: 27, z: -120, w: 24, h: 3, d: 8 }); // the lintel over the gate
  } else {
    for (var cx2 = -300; cx2 < 300; cx2 += 8 + rand() * 9) {
      if (Math.abs(cx2) < 30) continue;
      building(cx2, -90 - rand() * 90, 6 + rand() * 8, 8 + Math.pow(rand(), 1.8) * 40, 6 + rand() * 6, rand() < 0.3 ? 5 + rand() * 8 : 0);
    }
  }

  var shade = new T.Color();
  var mesh = new T.InstancedMesh(new T.BoxGeometry(1, 1, 1), new T.MeshBasicMaterial({ color: 0xffffff }), boxes.length);
  var m4 = new T.Matrix4(), q = new T.Quaternion(), v3 = new T.Vector3(), s3 = new T.Vector3();
  boxes.forEach(function (b, i) {
    m4.compose(v3.set(b.x, b.y, b.z), q, s3.set(b.w, b.h, b.d));
    mesh.setMatrixAt(i, m4);
    var k = 0.05 + rand() * 0.07;
    mesh.setColorAt(i, shade.setRGB(k * 0.8, k * 0.55, k * 1.5));
  });
  scene.add(mesh);
  if (roofs.length) {
    var rm = new T.InstancedMesh(new T.ConeGeometry(1, 1, 4), new T.MeshBasicMaterial({ color: 0xffffff }), roofs.length);
    var qy = new T.Quaternion().setFromAxisAngle(new T.Vector3(0, 1, 0), Math.PI / 4);
    roofs.forEach(function (r, i) {
      m4.compose(v3.set(r.x, r.y, r.z), qy, s3.set(r.r, r.h, r.r));
      rm.setMatrixAt(i, m4);
      rm.setColorAt(i, shade.setRGB(0.06, 0.03, 0.1));
    });
    scene.add(rm);
  }
  scene.add(points(lights.length, function (i) {
    var l = lights[i];
    return { x: l.x, y: l.y, z: l.z, s: l.s, c: l.c, sp: l.sp };
  }));

  // --- Embers drifting up ------------------------------------------------------------------------
  var EMBERS = calm ? 90 : 260, emberData = [];
  var embers = points(EMBERS, function (i) {
    emberData.push({ x: (rand() - 0.5) * 90, y: rand() * 40, z: (rand() - 0.5) * 120, v: 0.8 + rand() * 2.2, sw: rand() * 6.28 });
    return { x: 0, y: 0, z: 0, s: 0.34 + rand() * 0.3, c: rand() < 0.5 ? 0xffb066 : 0xff5a2a, sp: 1 + rand() * 3 };
  });
  scene.add(embers);
  var emberPos = embers.geometry.attributes.position;

  // --- The six stages: a portal each, violet turning to blood -------------------------------------
  var STAGES = 6, PALETTE = [0x8b5cf6, 0x9a55e6, 0xc2559f, 0xff6a5a, 0xff7a3d, 0xff3d5a];
  var ringZ = function (i) { return -170 - i * 56; };
  var rings = [];
  var RELICS = ["shield", "sword", "potion", "portal", "bolt", "skull"];

  // The site's pixel sprites (site.js, window.ddSprites) built out of cubes: one cube per pixel,
  // two deep, lit from the moon's side so the faces read as 3D. Returns a group spun about its middle.
  scene.add(new T.AmbientLight(0xb8a8ff, 0.55));
  var sun = new T.DirectionalLight(0xffe2b0, 0.9);
  sun.position.set(0.6, 0.8, 1);
  scene.add(sun);
  function voxels(name, unit) {
    var data = window.ddSprites, rows = data && data.rows[name];
    if (!rows) return null;
    var cells = [];
    for (var y = 0; y < rows.length; y++) {
      for (var x = 0; x < rows[y].length; x++) {
        var ch = rows[y].charAt(x);
        if (data.ink[ch]) cells.push({ x: x, y: y, c: data.ink[ch] });
      }
    }
    var w = rows[0].length, h = rows.length;
    var im = new T.InstancedMesh(new T.BoxGeometry(unit * 0.96, unit * 0.96, unit * 2),
      new T.MeshLambertMaterial({ color: 0xffffff, emissive: 0x1a0c10 }), cells.length);
    var col = new T.Color();
    cells.forEach(function (c, i) {
      m4.makeTranslation((c.x - w / 2 + 0.5) * unit, (h / 2 - c.y - 0.5) * unit, 0);
      im.setMatrixAt(i, m4);
      im.setColorAt(i, col.set(c.c));
    });
    var g = new T.Group();
    g.add(im);
    return g;
  }

  if (HOME) {
    for (var i = 0; i < STAGES; i++) {
      var R = i === STAGES - 1 ? 16 : 11, col = PALETTE[i];
      var g = new T.Group();
      g.position.set(0, R + 1, ringZ(i));
      var outer = new T.Mesh(new T.TorusGeometry(R, 0.42, 8, 72), new T.MeshBasicMaterial({ color: col, transparent: true, fog: true }));
      var inner = new T.Mesh(new T.TorusGeometry(R * 0.8, 0.14, 6, 64), new T.MeshBasicMaterial({ color: 0xffffff, transparent: true, fog: true }));
      var disc = new T.Mesh(new T.CircleGeometry(R * 0.96, 48), new T.MeshBasicMaterial({ color: col, transparent: true, opacity: 0.07, depthWrite: false, blending: T.AdditiveBlending, side: T.DoubleSide }));
      g.add(outer, inner, disc);
      var sparkCol = new T.Color(col).lerp(new T.Color(0xffffff), 0.35).getHex();
      var sparks = points(110, function () {
        var a = rand() * 6.283, rr = R * (0.9 + rand() * 0.25);
        return { x: Math.cos(a) * rr, y: Math.sin(a) * rr, z: (rand() - 0.5) * 3, s: 0.45 + rand() * 0.4, c: sparkCol, sp: 1 + rand() * 3 };
      });
      g.add(sparks);
      var core = null;
      if (i === STAGES - 1) {
        core = new T.Mesh(new T.IcosahedronGeometry(9, 1), new T.MeshBasicMaterial({ color: 0xff3d5a, wireframe: true, transparent: true }));
        g.add(core);
      }
      var relic = voxels(RELICS[i], i === STAGES - 1 ? 0.95 : 0.6);
      if (relic) g.add(relic);
      scene.add(g);
      rings.push({ g: g, outer: outer, inner: inner, disc: disc, sparks: sparks, core: core, relic: relic, glow: 0.3, R: R });
    }
    // a road of light from the gate down through the portals
    var grid = new T.GridHelper(800, 130, 0x5b3cb0, 0x2a1a4a);
    grid.position.set(0, 0.05, -545);
    grid.material.transparent = true;
    grid.material.opacity = 0.35;
    scene.add(grid);
  }

  // --- State: where the camera is going --------------------------------------------------------------
  var mouse = { x: 0, y: 0, sx: 0, sy: 0 };
  addEventListener("pointermove", function (e) {
    mouse.x = e.clientX / innerWidth * 2 - 1;
    mouse.y = e.clientY / innerHeight * 2 - 1;
  }, { passive: true });

  var descent = document.getElementById("descent");
  var stops = descent ? Array.prototype.slice.call(descent.querySelectorAll(".stop")) : [];
  var stage = -1;      // the stop being read, or -1 outside the section
  var phase = "before"; // before, in, after
  var camZ = 34, camX = 0, camY = 7, tz = 34, tx = 0, ty = 7;
  var skyTints = [
    [0x05030b, 0x1a0f33, 0x6a2446], [0x05030b, 0x1d1036, 0x5a2a6e], [0x07030d, 0x24103a, 0x7a2a6a],
    [0x0a030a, 0x2a0f32, 0x8a2a4a], [0x0c0306, 0x330c26, 0x94301e], [0x0e0204, 0x3a0a18, 0xa81c2a]
  ];
  var fogCols = [0x0a0614, 0x0c0618, 0x10061a, 0x14061a, 0x170616, 0x1c0510];
  var tint = 0; // 0..5, eased

  function aim() {
    var vh = innerHeight || 800, y = scrollY;
    var wide = innerWidth > 900;
    if (!HOME || !descent) {
      tz = 34 - Math.min(y, 6000) * 0.012;
      tx = 0; ty = 8;
      phase = "before"; stage = -1;
      return;
    }
    var r = descent.getBoundingClientRect();
    if (r.top > vh * 0.55) {
      phase = "before"; stage = -1;
      var f = Math.min(1, y / (vh * 0.9));
      tz = 34 - f * 18; ty = 7 + f * 4; tx = 0;
      tint = tint; // set below
    } else if (r.bottom < vh * 0.45) {
      phase = "after"; stage = -1;
      tz = ringZ(STAGES - 1) + 120; ty = 30; tx = 0;
    } else {
      phase = "in";
      var seen = -1;
      for (var k = 0; k < stops.length; k++) {
        var b = stops[k].getBoundingClientRect();
        if (b.top < vh * 0.55) seen = k;
      }
      stage = Math.max(0, seen);
      var R0 = rings[stage].R;
      tz = ringZ(stage) + R0 * (wide ? 2.9 : 3.6); ty = wide ? R0 + 1 : R0 * 0.25; tx = wide ? -R0 * 0.75 : 0;
    }
  }

  function mark() {
    stops.forEach(function (s, k) { s.classList.toggle("active", k === stage); });
  }

  function resize() {
    var w = innerWidth || 1, h = innerHeight || 1;
    renderer.setPixelRatio(pr * quality);
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.fov = w < 700 ? 70 : 58;
    camera.updateProjectionMatrix();
    uniforms.uScale.value = h * pr * quality * 0.55;
  }

  // --- The loop ------------------------------------------------------------------------------------
  var last = 0, clock = 0, frames = 0, slow = 0, lastStage = -2;
  var still = reduce.matches, running = false;
  var cA = new T.Color(), cB = new T.Color();

  function frame(t) {
    var dt = Math.min(0.1, (t - (last || t)) / 1000);
    last = t;
    if (!still) clock += dt;
    uniforms.uTime.value = clock;
    aim();
    if (stage !== lastStage) { lastStage = stage; mark(); }

    var ease = still ? 1 : 1 - Math.exp(-dt * 2.4);
    camZ += (tz - camZ) * ease;
    camY += (ty - camY) * ease;
    camX += (tx - camX) * ease;
    mouse.sx += (mouse.x - mouse.sx) * (still ? 1 : 1 - Math.exp(-dt * 3));
    mouse.sy += (mouse.y - mouse.sy) * (still ? 1 : 1 - Math.exp(-dt * 3));
    var bob = still ? 0 : Math.sin(clock * 0.35) * 0.35;
    camera.position.set(camX + mouse.sx * 2.6, camY + bob - mouse.sy * 1.2, camZ);
    camera.lookAt((phase === "in" ? camX : camX * 0.35) + mouse.sx * 4, camY + (phase === "in" ? 1 : phase === "after" ? 14 : 9) - mouse.sy * 2, camZ - 60);

    sky.position.copy(camera.position);
    ground.position.set(camera.position.x, 0, camera.position.z);

    // the sky and fog warm towards blood the deeper you go
    var want = !HOME ? 0 : phase === "before" ? 0 : phase === "after" ? 3 : stage;
    tint += (want - tint) * (still ? 1 : 1 - Math.exp(-dt * 1.6));
    var lo = Math.max(0, Math.min(STAGES - 2, Math.floor(tint))), f2 = tint - lo;
    ["top", "mid", "glow"].forEach(function (name, n) {
      domeU[name].value.set(skyTints[lo][n]).lerp(cB.set(skyTints[lo + 1][n]), Math.min(1, f2));
    });
    scene.fog.color.set(fogCols[lo]).lerp(cA.set(fogCols[lo + 1]), Math.min(1, f2));
    scene.background = scene.fog.color;

    // embers rise around the camera
    for (var i = 0; i < EMBERS; i++) {
      var e = emberData[i];
      if (!still) {
        e.y += e.v * dt * 2.2;
        e.sw += dt;
      }
      if (e.y > 46) e.y = 0;
      emberPos.setXYZ(i, camera.position.x + e.x + Math.sin(e.sw) * 1.6, e.y, camera.position.z - 20 + e.z);
    }
    emberPos.needsUpdate = true;

    for (var r = 0; r < rings.length; r++) {
      var ring = rings[r], on = r === stage, past = phase === "after" || (phase === "in" && r < stage);
      var goal = on ? 1 : past ? 0.5 : 0.28;
      ring.glow += (goal - ring.glow) * (still ? 1 : 1 - Math.exp(-dt * 3));
      var pulse = on && !still ? 1 + Math.sin(clock * 3) * 0.04 : 1;
      ring.g.rotation.z += still ? 0 : dt * (0.12 + r * 0.04);
      ring.outer.material.opacity = 0.35 + ring.glow * 0.65;
      ring.inner.material.opacity = 0.1 + ring.glow * 0.5;
      ring.disc.material.opacity = 0.03 + ring.glow * 0.14;
      ring.g.scale.setScalar(pulse * (0.96 + ring.glow * 0.06));
      if (ring.relic) {
        ring.relic.rotation.z = -ring.g.rotation.z;
        ring.relic.rotation.y = still ? 0.5 : clock * 0.8 + r;
      }
      if (ring.core) {
        ring.core.rotation.x += still ? 0 : dt * 0.7;
        ring.core.rotation.y += still ? 0 : dt * 0.9;
        ring.core.material.opacity = 0.2 + ring.glow * 0.8;
      }
    }

    renderer.render(scene, camera);

    // too slow: draw fewer pixels
    if (!still && ++frames > 30) {
      if (dt > 1 / 24) slow++; else slow = Math.max(0, slow - 1);
      if (slow > 40 && quality > 0.55) {
        quality -= 0.2;
        slow = 0;
        resize();
      }
      frames = 31;
    }
    if (!still && !document.hidden) requestAnimationFrame(frame); else running = false;
  }

  function start() {
    if (running) return;
    running = true;
    last = 0;
    requestAnimationFrame(frame);
  }
  function draw() { if (still) { last = 0; frame(performance.now()); } }

  addEventListener("resize", function () { resize(); draw(); });
  addEventListener("scroll", function () { if (still) draw(); }, { passive: true });
  document.addEventListener("visibilitychange", function () { if (!document.hidden && !still) start(); });
  var onMotion = function () { still = reduce.matches; if (still) draw(); else start(); };
  if (reduce.addEventListener) reduce.addEventListener("change", onMotion);

  resize();
  // the first frame jumps to where the page is, instead of flying there
  aim(); camZ = tz; camY = ty; camX = tx; tint = HOME && phase === "after" ? 3 : HOME && phase === "in" ? stage : 0;
  if (still) draw(); else start();

  // --- Cards rise into view as the page scrolls --------------------------------------------------
  if ("IntersectionObserver" in window) {
    var seen = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) { en.target.classList.add("in"); seen.unobserve(en.target); }
      });
    }, { rootMargin: "0px 0px -6% 0px" });
    document.querySelectorAll(".tile, .way, .faq details, .keys > div, .shot, .notes .release").forEach(function (el, i) {
      el.classList.add("reveal");
      el.style.setProperty("--i", i % 4);
      seen.observe(el);
    });
  }

  // for tests and screenshots: put the camera where the page is now and draw at once
  function jump() {
    aim();
    camZ = tz; camY = ty; camX = tx;
    tint = phase === "after" ? 3 : phase === "in" ? stage : 0;
    rings.forEach(function (rg, k) { rg.glow = k === stage ? 1 : 0.3; });
    var was = still, run = running;
    var keep = last; still = true; last = 0; frame(performance.now()); still = was; running = run; last = keep;
  }

  window.dodScene = { jump: jump, renderer: renderer, camera: camera, scene: scene, state: function () { return { stage: stage, phase: phase, z: camZ, quality: quality }; } };
})();
