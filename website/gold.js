// The Gold Store (gold.html): sign in, pick a pack, pay with PayPal's buttons. The account
// server does the selling (source/App/Controllers/GoldController.cs): it sets the prices, makes the
// PayPal order, checks the payment with PayPal and pays the gold in. This page only shows it.
(function () {
  "use strict";

  // dodmg.com forwards /api to the game's account server. On this PC, ?api=<address> points the
  // page at a test server instead (only when the page itself is local).
  var API = "api/gold/";
  var params = new URLSearchParams(location.search);
  var local = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
  if (local && params.get("api")) API = params.get("api").replace(/\/?$/, "/");
  // ?mockpay=<tools/testclient/paypal_mock.py's address>: a button standing in for PayPal's, which
  // approves the order at the stand-in (a test stack started with --paypal), local pages only
  var mockPay = local && params.get("mockpay") ? params.get("mockpay").replace(/\/?$/, "/") : null;

  var $ = function (id) { return document.getElementById(id); };
  var state = { store: null, token: null, me: null, pack: null, sdk: null, busy: false };
  var reduced = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;

  function remember(token) {
    state.token = token;
    try {
      if (token) sessionStorage.setItem("gold.token", token);
      else sessionStorage.removeItem("gold.token");
    } catch (e) { /* private window: signed in for this visit only */ }
  }
  try { state.token = sessionStorage.getItem("gold.token"); } catch (e) { state.token = null; }

  function request(path, data) {
    var init = data ? { method: "POST", body: new URLSearchParams(data) } : {};
    return fetch(API + path, init).then(function (r) {
      return r.text().then(function (text) {
        var json;
        try { json = JSON.parse(text); } catch (e) { json = { error: "The game's server didn't answer (" + r.status + "). Try again in a moment." }; }
        if (!r.ok && !json.status) {
          var err = new Error(json.error || "Something went wrong.");
          err.status = r.status;
          throw err;
        }
        return json;
      });
    });
  }

  function money(price) {
    var cur = state.store ? state.store.currency : "USD";
    try {
      return new Intl.NumberFormat(undefined, { style: "currency", currency: cur, currencyDisplay: "narrowSymbol" }).format(+price);
    } catch (e) {
      return price + " " + cur;
    }
  }
  function number(n) { return (+n || 0).toLocaleString(); }
  function sprite(name, px) {
    var i = document.createElement("i");
    i.className = "sprite";
    i.innerHTML = window.ddSprite ? window.ddSprite(name, px) || "" : "";
    return i;
  }
  function error(el, text) { $(el).textContent = text || ""; }

  // --- The account -----------------------------------------------------------------------

  function showMe(me) {
    state.me = me;
    var signed = !!me;
    $("signin").hidden = signed;
    $("who").hidden = !signed;
    $("step-account").classList.toggle("done", signed);
    if (signed) {
      $("who-name").textContent = me.name;
      countTo($("who-gold"), me.gold);
      showHistory(me.purchases || []);
    } else {
      $("history").hidden = true;
    }
    showSummary();
  }

  $("signin").addEventListener("submit", function (e) {
    e.preventDefault();
    var form = e.target;
    var button = form.querySelector("button");
    button.disabled = true;
    error("signin-error");
    request("signin", { guid: form.guid.value.trim(), password: form.password.value })
      .then(function (me) {
        form.password.value = "";
        remember(me.token);
        showMe(me);
      })
      .catch(function (err) { error("signin-error", err.message); })
      .then(function () { button.disabled = false; });
  });

  $("signout").addEventListener("click", function () {
    if (state.token) request("signout", { token: state.token }).catch(function () {});
    remember(null);
    showMe(null);
  });

  function refreshMe() {
    if (!state.token) return Promise.resolve(showMe(null));
    return request("me", { token: state.token })
      .then(showMe)
      .catch(function (err) {
        if (err.status === 401) remember(null);
        showMe(null);
      });
  }

  // --- The packs ---------------------------------------------------------------------------

  function showPacks(packs) {
    var box = $("packs");
    box.innerHTML = "";
    var cheapest = packs.reduce(function (m, p) { return Math.min(m, p.price / p.gold); }, Infinity);
    packs.forEach(function (p, i) {
      var b = document.createElement("button");
      b.type = "button";
      b.className = "gold-pack" + (p.best ? " best" : "");
      b.setAttribute("role", "radio");
      b.setAttribute("aria-checked", "false");
      b.dataset.id = p.id;
      if (p.best) {
        var ribbon = document.createElement("span");
        ribbon.className = "gold-ribbon";
        ribbon.textContent = "Most popular";
        b.appendChild(ribbon);
      }
      var art = sprite(i >= 2 ? "coins" : "coin", i >= 3 ? 5 : i >= 2 ? 4 : 3 + i);
      art.classList.add("gold-art");
      b.appendChild(art);
      var gold = document.createElement("span");
      gold.className = "gold-pack-gold";
      gold.textContent = number(p.gold);
      b.appendChild(gold);
      var unit = document.createElement("span");
      unit.className = "gold-pack-unit";
      unit.textContent = "gold";
      b.appendChild(unit);
      // the bonus, and what the pack would cost at the base rate ($1 for 100 gold)
      var base = p.gold - p.bonus;
      var percent = base > 0 ? Math.round((p.bonus / base) * 100) : 0;
      var bonus = document.createElement("span");
      bonus.className = "gold-bonus";
      bonus.textContent = p.bonus > 0 ? "+" + number(p.bonus) + " bonus (+" + percent + "%)" :" ";
      if (!p.bonus) bonus.classList.add("none");
      b.appendChild(bonus);
      var price = document.createElement("span");
      price.className = "gold-price";
      // struck through: the pack's usual price during a discount, else its gold at the base rate
      var usual = p.was || (p.bonus > 0 ? (p.gold / 100).toFixed(2) : null);
      if (usual) {
        var was = document.createElement("s");
        was.className = "gold-was";
        was.textContent = money(usual);
        price.appendChild(was);
      }
      price.appendChild(document.createTextNode(money(p.price)));
      b.appendChild(price);
      var per = p.price / p.gold;
      var each = document.createElement("span");
      each.className = "gold-each " + (per <= cheapest * 1.001 && packs.length > 1 ? "good" : "dim");
      each.textContent = (per * 100 < 0.995 ? "Save " + Math.round((1 - per * 100) * 100) + "% · " : "") +
        money((per * 100).toFixed(2)) + " per 100";
      b.appendChild(each);
      b.addEventListener("click", function () { choose(p); });
      box.appendChild(b);
    });
    var best = packs.filter(function (p) { return p.best; })[0];
    choose(best || packs[0]);
  }

  function choose(p) {
    state.pack = p;
    var cards = $("packs").children;
    for (var i = 0; i < cards.length; i++) {
      var on = cards[i].dataset.id === p.id;
      cards[i].classList.toggle("chosen", on);
      cards[i].setAttribute("aria-checked", on ? "true" : "false");
      cards[i].tabIndex = on ? 0 : -1;
    }
    error("pay-error");
    showSummary();
  }

  // arrow keys move between the packs, as in any radio group
  $("packs").addEventListener("keydown", function (e) {
    if (!state.store) return;
    var packs = state.store.packs, i = packs.indexOf(state.pack);
    if (e.key === "ArrowRight" || e.key === "ArrowDown") i = (i + 1) % packs.length;
    else if (e.key === "ArrowLeft" || e.key === "ArrowUp") i = (i + packs.length - 1) % packs.length;
    else return;
    e.preventDefault();
    choose(packs[i]);
    $("packs").children[i].focus();
  });

  function showSummary() {
    var box = $("summary");
    box.innerHTML = "";
    var p = state.pack;
    if (!p) return;
    var line = document.createElement("p");
    line.className = "gold-total";
    line.appendChild(sprite("coin", 2));
    var what = document.createElement("span");
    what.textContent = number(p.gold) + " gold";
    line.appendChild(what);
    var cost = document.createElement("b");
    if (p.was) {
      var was = document.createElement("s");
      was.className = "gold-was";
      was.textContent = money(p.was);
      cost.appendChild(was);
    }
    cost.appendChild(document.createTextNode(money(p.price)));
    line.appendChild(cost);
    box.appendChild(line);
    var who = document.createElement("p");
    who.className = "dim";
    if (state.me) {
      who.textContent = "For " + state.me.name + ": " + number(state.me.gold) + " → " + number(state.me.gold + p.gold) + " gold.";
    } else {
      who.textContent = "Sign in first, so the gold goes to the right account.";
    }
    box.appendChild(who);
    $("paypal-buttons").classList.toggle("locked", !state.me);
  }

  // --- History -------------------------------------------------------------------------------

  var STATUS = { credited: "Paid in", pending: "Waiting for PayPal", refunded: "Refunded" };

  function showHistory(list) {
    var rows = $("history-rows");
    rows.innerHTML = "";
    $("history").hidden = list.length === 0;
    list.forEach(function (o) {
      var tr = document.createElement("tr");
      var date = new Date(o.at);
      [
        isNaN(date) ? "" : date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }),
        number(o.gold),
        o.price ? new Intl.NumberFormat(undefined, { style: "currency", currency: o.currency || "USD", currencyDisplay: "narrowSymbol" }).format(+o.price) : "",
        (STATUS[o.status] || o.status) + (o.takenBack ? " (" + number(o.takenBack) + " taken back)" : ""),
        o.order
      ].forEach(function (text, i) {
        var td = document.createElement("td");
        td.textContent = text;
        if (i === 3) td.className = "st-" + o.status;
        if (i === 4) td.className = "mono dim";
        tr.appendChild(td);
      });
      rows.appendChild(tr);
    });
  }

  // --- PayPal ----------------------------------------------------------------------------------

  // PayPal's Buttons as far as this page uses them, paying at the stand-in
  function mockButtons() {
    return {
      Buttons: function (opts) {
        return {
          render: function (selector) {
            var b = document.createElement("button");
            b.type = "button";
            b.className = "btn btn-ember btn-big gold-mockpay";
            b.textContent = "Pay (test stand-in)";
            b.addEventListener("click", function () {
              var refused = false;
              opts.onClick({}, { reject: function () { refused = true; }, resolve: function () {} });
              if (refused) return;
              opts.createOrder()
                .then(function (id) {
                  return fetch(mockPay + "mock/approve/" + id, { method: "POST", mode: "no-cors" })
                    .then(function () { return opts.onApprove({ orderID: id }, { restart: function () {} }); });
                })
                .catch(function () {});
            });
            document.querySelector(selector).appendChild(b);
          }
        };
      }
    };
  }

  function loadPayPal(store) {
    if (mockPay) return Promise.resolve(mockButtons());
    return new Promise(function (resolve, reject) {
      var s = document.createElement("script");
      s.src = "https://www.paypal.com/sdk/js?" + new URLSearchParams({
        "client-id": store.clientId,
        currency: store.currency,
        intent: "capture",
        components: "buttons",
        "disable-funding": "paylater,credit"
      });
      s.setAttribute("data-page-type", "checkout");
      s.onload = function () { resolve(window.paypal); };
      s.onerror = function () { reject(new Error("PayPal didn't load: a blocker may be stopping it. Allow paypal.com and reload.")); };
      document.head.appendChild(s);
    });
  }

  function renderButtons(paypal) {
    paypal.Buttons({
      style: { layout: "vertical", shape: "rect", color: "gold", label: "pay", height: 48 },
      onClick: function (data, actions) {
        error("pay-error");
        if (!state.me) {
          error("pay-error", "Sign in first (step 1), so the gold goes to your account.");
          $("signin").guid.focus();
          return actions.reject();
        }
        if (!state.pack) return actions.reject();
        return actions.resolve();
      },
      createOrder: function () {
        return request("order", { token: state.token, pack: state.pack.id })
          .then(function (r) { return r.orderId; })
          .catch(function (err) {
            if (err.status === 401) { remember(null); showMe(null); }
            error("pay-error", err.message);
            throw err;
          });
      },
      onApprove: function (data, actions) {
        state.busy = true;
        $("step-pay").classList.add("working");
        return request("capture", { token: state.token, orderId: data.orderID })
          .then(function (r) {
            if (r.status === "declined") {
              error("pay-error", r.message);
              return actions.restart();
            }
            if (r.purchases) showHistory(r.purchases);
            if (r.status === "credited") {
              if (state.me) state.me.gold = r.balance;
              countTo($("who-gold"), r.balance);
              showSummary();
              celebrate(r.gold, r.balance);
            } else if (r.status === "pending") {
              error("pay-error", r.message);
            } else {
              error("pay-error", r.message || "The payment didn't go through.");
            }
          })
          .catch(function (err) { error("pay-error", err.message); })
          .then(function () {
            state.busy = false;
            $("step-pay").classList.remove("working");
          });
      },
      onCancel: function () { error("pay-error", "Payment cancelled: nothing was charged."); },
      onError: function () {
        if (!$("pay-error").textContent) error("pay-error", "PayPal hit a problem. Nothing was charged: try again.");
      }
    }).render("#paypal-buttons");
  }

  // --- Thank you -------------------------------------------------------------------------------

  function countTo(el, target) {
    var from = +String(el.textContent).replace(/[^\d]/g, "") || 0;
    if (reduced || from === target) { el.textContent = number(target); return; }
    var start = performance.now(), ms = 900;
    (function step(now) {
      var t = Math.min(1, (now - start) / ms);
      var eased = 1 - Math.pow(1 - t, 3);
      el.textContent = number(Math.round(from + (target - from) * eased));
      if (t < 1) requestAnimationFrame(step);
    })(start);
  }

  function celebrate(gold, balance) {
    $("done-gold").textContent = "0";
    $("done-balance").textContent = number(balance - gold);
    $("done").hidden = false;
    document.body.classList.add("gold-celebrating");
    $("done-close").focus({ preventScroll: true });
    countTo($("done-gold"), gold);
    setTimeout(function () { countTo($("done-balance"), balance); }, 450);
    if (!reduced) coinRain($("done-coins"), Math.min(160, 30 + Math.round(gold / 20)));
  }

  function closeDone() {
    $("done").hidden = true;
    document.body.classList.remove("gold-celebrating");
  }
  $("done-close").addEventListener("click", closeDone);
  document.addEventListener("keydown", function (e) { if (e.key === "Escape" && !$("done").hidden) closeDone(); });

  // pixel coins falling and spinning, drawn from the coin sprite
  function coinRain(canvas, count) {
    var dpr = window.devicePixelRatio || 1;
    var w = canvas.clientWidth, h = canvas.clientHeight;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    var ctx = canvas.getContext("2d");
    ctx.scale(dpr, dpr);
    ctx.imageSmoothingEnabled = false;
    var img = new Image();
    img.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(window.ddSprite("coin", 1));
    var coins = [];
    for (var i = 0; i < count; i++) {
      coins.push({
        x: Math.random() * w,
        y: -20 - Math.random() * h * 0.8,
        vy: 2 + Math.random() * 3.5,
        vx: (Math.random() - 0.5) * 1.2,
        size: 3 + Math.floor(Math.random() * 3),
        spin: Math.random() * Math.PI * 2,
        rate: 0.08 + Math.random() * 0.12
      });
    }
    var start = performance.now();
    img.onload = function () {
      (function frame(now) {
        if ($("done").hidden) { ctx.clearRect(0, 0, w, h); return; }
        ctx.clearRect(0, 0, w, h);
        var alive = 0;
        coins.forEach(function (c) {
          c.vy += 0.06;
          c.y += c.vy;
          c.x += c.vx;
          c.spin += c.rate;
          if (c.y < h + 20) alive++;
          var s = c.size * 10, squash = Math.max(0.15, Math.abs(Math.cos(c.spin)));
          ctx.drawImage(img, c.x - (s * squash) / 2, c.y - s / 2, s * squash, s);
        });
        if (alive && now - start < 6000) requestAnimationFrame(frame);
        else ctx.clearRect(0, 0, w, h);
      })(start);
    };
  }

  // --- A discount -------------------------------------------------------------------------------

  // the banner counts down to the discount's end, then the packs go back to their usual prices
  // (the server prices an order itself, so a page left open can't buy at the old price)
  var dealTimer = null;
  function showDiscount(d) {
    clearInterval(dealTimer);
    var box = $("gold-deal");
    var ends = d ? Date.parse(d.ends) : NaN;
    if (!d || !(ends > Date.now())) { box.hidden = true; return; }
    function tick() {
      var left = Math.max(0, Math.floor((ends - Date.now()) / 1000));
      if (!left) {
        clearInterval(dealTimer);
        box.hidden = true;
        request("store").then(function (store) {
          var chosen = state.pack && state.pack.id;
          state.store.packs = store.packs || [];
          showPacks(state.store.packs);
          var again = state.store.packs.filter(function (p) { return p.id === chosen; })[0];
          if (again) choose(again);
        }).catch(function () {});
        return;
      }
      var h = Math.floor(left / 3600), m = Math.floor(left / 60) % 60, s = left % 60;
      var pad = function (n) { return (n < 10 ? "0" : "") + n; };
      box.innerHTML = "";
      box.appendChild(document.createTextNode(d.name + ": every pack " + d.percentOff + "% off, for "));
      var clock = document.createElement("b");
      clock.textContent = h + ":" + pad(m) + ":" + pad(s);
      box.appendChild(clock);
      box.appendChild(document.createTextNode(" more."));
    }
    box.hidden = false;
    tick();
    dealTimer = setInterval(tick, 1000);
  }

  // --- Start -------------------------------------------------------------------------------------

  request("store")
    .then(function (store) {
      state.store = store;
      $("gold-sandbox").hidden = !(store.enabled && store.sandbox);
      showPacks(store.packs || []);
      showDiscount(store.enabled ? store.discount : null);
      refreshMe();
      if (!store.enabled) {
        $("gold-closed").hidden = false;
        $("step-pay").classList.add("closed");
        return;
      }
      return loadPayPal(store).then(renderButtons);
    })
    .catch(function (err) {
      $("gold-closed").hidden = false;
      $("gold-closed").textContent = err.message || "The gold store can't be reached right now. Try again in a moment.";
      $("step-pay").classList.add("closed");
    });

  // a purchase made in another tab, or gold spent in game meanwhile
  document.addEventListener("visibilitychange", function () {
    if (document.visibilityState === "visible" && state.me && !state.busy) refreshMe();
  });
})();
