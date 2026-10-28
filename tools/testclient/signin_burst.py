"""Many players signing in at once, against a test account server (never the live one).

    python tools/testclient/signin_burst.py --port 8088 [--players 40] [--timeout 20] [--rounds 3]

Makes --players guest accounts (each from its own X-Forwarded-For address, past the per-address
limit; only works from this PC), then has them all ask for their character list (/char/list) at
the same moment, as the clients do when everyone reconnects after a restart. A request that takes
longer than --timeout seconds is given up and sent again, as a client would. A probe asks for
/app/status every half second meanwhile, so a stall shows as a gap.

Prints each player's time to a character list, the probe's slowest answers and the requests the
server took in each second.
"""
import argparse
import os
import statistics
import threading
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET


def post(base, path, form, timeout, ip=None):
    data = urllib.parse.urlencode(form).encode()
    req = urllib.request.Request(base + path, data=data)
    if ip:
        req.add_header("X-Forwarded-For", ip)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode()


def guests(base, n, cache):
    if os.path.exists(cache):
        rows = [l.split() for l in open(cache) if l.strip()]
        if len(rows) >= n:
            return rows[:n]
    rows = []
    for i in range(n):
        xml = ET.fromstring(post(base, "/account/guest", {}, 30, ip="10.77.%d.%d" % (i // 250, i % 250 + 1)))
        rows.append((xml.findtext("GUID"), xml.findtext("Password")))
    with open(cache, "w") as f:
        f.writelines("%s %s\n" % r for r in rows)
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--players", type=int, default=40)
    ap.add_argument("--timeout", type=float, default=20)
    ap.add_argument("--rounds", type=int, default=1, help="bursts, one after the other")
    args = ap.parse_args()
    if args.port in (8080, 443, 80):
        raise SystemExit("not the live account server")
    base = "http://127.0.0.1:%d" % args.port
    cache = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "build",
                         "signin_burst_%d.txt" % args.port)
    accounts = guests(base, args.players, cache)

    for rnd in range(args.rounds):
        done = threading.Event()
        probe = []

        def prober():
            while not done.is_set():
                t = time.time()
                try:
                    urllib.request.urlopen(base + "/app/status", timeout=60).read()
                    probe.append(time.time() - t)
                except Exception:
                    probe.append(float("inf"))
                time.sleep(0.5)

        results = [None] * len(accounts)
        answered = []
        start = threading.Barrier(len(accounts) + 1)

        def player(i, guid, password):
            start.wait()
            t0 = time.time()
            tries = 0
            while True:
                tries += 1
                t = time.time()
                try:
                    body = post(base, "/char/list", {"guid": guid, "password": password}, args.timeout)
                    answered.append(time.time())
                    ok = body.startswith("<Chars")
                    results[i] = (time.time() - t0, tries, ok)
                    return
                except Exception:
                    if time.time() - t0 > 300:
                        results[i] = (float("inf"), tries, False)
                        return

        threads = [threading.Thread(target=player, args=(i, g, p)) for i, (g, p) in enumerate(accounts)]
        for t in threads:
            t.start()
        pt = threading.Thread(target=prober)
        pt.start()
        t0 = time.time()
        start.wait()
        for t in threads:
            t.join()
        done.set()
        pt.join()

        times = sorted(r[0] for r in results)
        retried = sum(1 for r in results if r[1] > 1)
        failed = sum(1 for r in results if not r[2])
        q = lambda p: times[min(len(times) - 1, int(p * len(times)))]
        print("round %d: %d players, all answered in %.1f s; median %.2f s, p90 %.2f s, max %.2f s; "
              "%d retried (%d tries in all), %d not a character list"
              % (rnd + 1, len(times), time.time() - t0, statistics.median(times), q(0.9), times[-1],
                 retried, sum(r[1] for r in results), failed))
        print("  probe /app/status: %d asks, median %.3f s, max %.2f s"
              % (len(probe), statistics.median(probe), max(probe)))
        per = {}
        for a in answered:
            per[int(a - t0)] = per.get(int(a - t0), 0) + 1
        print("  answers per second:", " ".join("%ds:%d" % (k, per[k]) for k in sorted(per)))
        time.sleep(3)


if __name__ == "__main__":
    main()
