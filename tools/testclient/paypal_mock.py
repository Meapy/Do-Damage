"""A stand-in for PayPal's REST API, for testing the gold store on an isolated stack with no money
moving (source/App/PayPal.cs talks to it as it would to PayPal).

    python tools/testclient/paypal_mock.py [--port 18099]
    python tools/testclient/iso_stack.py start --paypal http://127.0.0.1:18099

It keeps orders in memory and answers what the store asks: a token, making an order, looking an
order or a capture up, and capturing. What the buyer does on PayPal's own page is done here:

    POST /mock/approve/<order id>       the buyer approved the order (payer: ?payer=<email>)
    POST /mock/pending/<order id>       the next capture of this order comes back PENDING
    POST /mock/complete/<capture id>    a pending capture cleared
    POST /mock/refund/<capture id>      the payment was refunded (REVERSED: ?reversed=1)
    GET  /mock/orders                   everything it holds, as JSON

tools/testclient/gold_store_test.py runs the whole flow against it.
"""
import argparse
import itertools
import json
import random
import string
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

ORDERS = {}
CAPTURES = {}
REQUESTS = {}  # PayPal-Request-Id -> the answer it got (the same request again gets the same)
LOCK = threading.Lock()
COUNTER = itertools.count(1)


def new_id(prefix=""):
    return prefix + "".join(random.choice(string.ascii_uppercase + string.digits) for _ in range(17 - len(prefix)))


def order_json(o):
    unit = dict(o["unit"])
    if o.get("capture"):
        c = CAPTURES[o["capture"]]
        unit["payments"] = {"captures": [{
            "id": c["id"], "status": c["status"], "custom_id": unit.get("custom_id"),
            "amount": unit["amount"] and {"currency_code": unit["amount"]["currency_code"], "value": unit["amount"]["value"]},
        }]}
    out = {"id": o["id"], "status": o["status"], "intent": "CAPTURE", "purchase_units": [unit]}
    if o.get("payer"):
        out["payer"] = {"email_address": o["payer"], "payer_id": "MOCKPAYER"}
    return out


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print("paypal-mock:", fmt % args, flush=True)

    def send(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def error(self, code, issue, text):
        self.send(code, {"name": "UNPROCESSABLE_ENTITY" if code == 422 else "RESOURCE_NOT_FOUND",
                         "message": text, "details": [{"issue": issue, "description": text}]})

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def authorized(self):
        auth = self.headers.get("Authorization", "")
        if auth.startswith("Bearer mock-token"):
            return True
        self.send(401, {"error": "invalid_token"})
        return False

    def do_GET(self):
        url = urlparse(self.path)
        parts = url.path.strip("/").split("/")
        with LOCK:
            if url.path == "/mock/orders":
                return self.send(200, {"orders": [order_json(o) for o in ORDERS.values()], "captures": CAPTURES})
            if not self.authorized():
                return
            if parts[:3] == ["v2", "checkout", "orders"] and len(parts) == 4:
                o = ORDERS.get(parts[3])
                return self.send(200, order_json(o)) if o else self.error(404, "INVALID_RESOURCE_ID", "no such order")
            if parts[:3] == ["v2", "payments", "captures"] and len(parts) == 4:
                c = CAPTURES.get(parts[3])
                return self.send(200, {"id": c["id"], "status": c["status"]}) if c else self.error(404, "INVALID_RESOURCE_ID", "no such capture")
        self.send(404, {})

    def do_POST(self):
        url = urlparse(self.path)
        q = parse_qs(url.query)
        parts = url.path.strip("/").split("/")
        raw = self.body()
        with LOCK:
            if url.path == "/v1/oauth2/token":
                if not self.headers.get("Authorization", "").startswith("Basic "):
                    return self.send(401, {"error": "invalid_client"})
                return self.send(200, {"access_token": "mock-token", "token_type": "Bearer", "expires_in": 32400})

            if parts[0] == "mock" and len(parts) == 3:
                kind, key = parts[1], parts[2]
                if kind in ("approve", "pending"):
                    o = ORDERS.get(key)
                    if not o:
                        return self.error(404, "INVALID_RESOURCE_ID", "no such order")
                    if kind == "approve":
                        o["status"] = "APPROVED"
                        o["payer"] = q.get("payer", ["buyer@example.com"])[0]
                    else:
                        o["pending"] = True
                    return self.send(200, order_json(o))
                c = CAPTURES.get(key)
                if not c:
                    return self.error(404, "INVALID_RESOURCE_ID", "no such capture")
                if kind == "complete":
                    c["status"] = "COMPLETED"
                elif kind == "refund":
                    c["status"] = "REVERSED" if q.get("reversed") else "REFUNDED"
                return self.send(200, c)

            if not self.authorized():
                return
            rid = self.headers.get("PayPal-Request-Id")
            if rid and rid in REQUESTS:
                return self.send(*REQUESTS[rid])

            if url.path == "/v2/checkout/orders":
                req = json.loads(raw or b"{}")
                unit = req["purchase_units"][0]
                assert req.get("intent") == "CAPTURE", "the store always captures"
                assert unit["amount"]["value"] == unit["items"][0]["unit_amount"]["value"], "item total and amount differ"
                o = {"id": new_id(), "status": "CREATED", "unit": unit}
                ORDERS[o["id"]] = o
                answer = (201, order_json(o))
            elif parts[:3] == ["v2", "checkout", "orders"] and len(parts) == 5 and parts[4] == "capture":
                o = ORDERS.get(parts[3])
                if not o:
                    return self.error(404, "INVALID_RESOURCE_ID", "no such order")
                if o["status"] == "COMPLETED":
                    return self.error(422, "ORDER_ALREADY_CAPTURED", "already captured")
                if o["status"] != "APPROVED":
                    return self.error(422, "ORDER_NOT_APPROVED", "the payer hasn't approved the order")
                cid = new_id("C")
                CAPTURES[cid] = {"id": cid, "status": "PENDING" if o.get("pending") else "COMPLETED", "order": o["id"]}
                o["capture"] = cid
                o["status"] = "COMPLETED"
                answer = (201, order_json(o))
            else:
                return self.send(404, {})
            if rid:
                REQUESTS[rid] = answer
            self.send(*answer)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=18099)
    args = ap.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print("paypal-mock: listening on http://127.0.0.1:%d" % args.port, flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
