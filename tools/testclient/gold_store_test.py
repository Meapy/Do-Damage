"""The gold store end to end, on an isolated stack and the PayPal stand-in: no money moves.

    python tools/testclient/paypal_mock.py --port 18099
    python tools/testclient/iso_stack.py --slot N start --paypal http://127.0.0.1:18099
    python tools/testclient/gold_store_test.py --account http://127.0.0.1:<8081+N> --mock http://127.0.0.1:18099

It makes a throwaway account and buys with it: a payment paid in once however often it's
captured; a pending payment paid in when it clears; a buyer who closes the page after approving
(PayPal's webhook pays it in); a refund taking the gold back; and what must be refused (another
account's order, an order not yet approved, an unknown pack, a stale session).
"""
import argparse
import json
import random
import string
import sys
import urllib.error
import urllib.parse
import urllib.request

FAILS = []


def call(base, path, data=None, raw=None):
    body = raw if raw is not None else (urllib.parse.urlencode(data).encode() if data is not None else None)
    req = urllib.request.Request(base.rstrip("/") + "/" + path, data=body, method="POST" if body is not None else "GET")
    if raw is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            text = r.read().decode()
            code = r.status
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        code = e.code
    try:
        return code, json.loads(text) if text else {}
    except ValueError:
        return code, {"text": text}


def check(what, ok, detail=""):
    print(("  ok   " if ok else "  FAIL ") + what + ("" if ok else "  (%s)" % detail), flush=True)
    if not ok:
        FAILS.append(what)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--account", required=True, help="the test stack's account server")
    ap.add_argument("--mock", default="http://127.0.0.1:18099", help="paypal_mock.py")
    args = ap.parse_args()
    acc, mock = args.account, args.mock

    def gold(path, **data):
        return call(acc, "gold/" + path, data)

    def webhook(event):
        return call(acc, "gold/paypal-webhook", raw=json.dumps(event).encode())

    code, store = call(acc, "gold/store")
    check("the store is open on the stand-in", code == 200 and store.get("enabled") and store.get("sandbox"), store)
    packs = {p["id"]: p for p in store.get("packs", [])}
    check("it sells the default packs", set(packs) >= {"pouch", "sack", "chest"}, list(packs))

    # two throwaway accounts
    names = []
    for _ in range(2):
        name = "Gold" + "".join(random.choice(string.ascii_lowercase) for _ in range(6))
        email = name.lower() + "@example.com"
        code, r = call(acc, "account/register", {"guid": email, "newGuid": email, "newPassword": "test-gold-1", "name": name})
        check("registered " + name, code == 200, r)
        names.append((name, email))
    code, me = gold("signin", guid=names[0][1], password="wrong-password")
    check("a wrong password is refused", code == 401, me)
    code, me = gold("signin", guid=names[0][1], password="test-gold-1")
    check("signed in", code == 200 and me.get("token") and me["name"] == names[0][0], me)
    token, start = me["token"], me["gold"]
    _, other = gold("signin", guid=names[1][1], password="test-gold-1")

    # 1. a plain purchase, captured twice and announced by the webhook too: paid in once
    code, r = gold("order", token=token, pack="chest")
    check("an order is made", code == 200 and r.get("orderId"), r)
    order = r["orderId"]
    code, r = gold("capture", token=token, orderId=order)
    check("capturing before the buyer approved pays nothing", code == 400 and r.get("status") == "failed", r)
    code, r = gold("capture", token=other["token"], orderId=order)
    check("another account can't capture it", code == 400, r)
    call(mock, "mock/approve/" + order, {"payer": "buyer@example.com"})
    code, r = gold("capture", token=token, orderId=order)
    check("the capture pays 1,100 gold in", code == 200 and r.get("status") == "credited" and r.get("balance") == start + 1100, r)
    code, r = gold("capture", token=token, orderId=order)
    check("capturing again pays nothing more", r.get("status") == "credited" and r.get("balance") == start + 1100, r)
    _, orders = call(mock, "mock/orders")
    capture = next(o for o in orders["orders"] if o["id"] == order)["purchase_units"][0]["payments"]["captures"][0]["id"]
    code, _ = webhook({"event_type": "PAYMENT.CAPTURE.COMPLETED", "resource": {"id": capture, "supplementary_data": {"related_ids": {"order_id": order}}}})
    _, me = gold("me", token=token)
    check("the webhook for it pays nothing more", code == 200 and me["gold"] == start + 1100, me)
    check("it's in the purchases", me["purchases"] and me["purchases"][0]["order"] == order and me["purchases"][0]["status"] == "credited", me)

    # 2. a pending payment: paid in when it clears
    _, r = gold("order", token=token, pack="pouch")
    pending = r["orderId"]
    call(mock, "mock/pending/" + pending, {})
    call(mock, "mock/approve/" + pending, {})
    code, r = gold("capture", token=token, orderId=pending)
    check("a pending payment says so and pays nothing yet", r.get("status") == "pending" and r.get("balance") == start + 1100, r)
    _, orders = call(mock, "mock/orders")
    pcap = next(o for o in orders["orders"] if o["id"] == pending)["purchase_units"][0]["payments"]["captures"][0]["id"]
    call(mock, "mock/complete/" + pcap, {})
    webhook({"event_type": "PAYMENT.CAPTURE.COMPLETED", "resource": {"id": pcap, "supplementary_data": {"related_ids": {"order_id": pending}}}})
    _, me = gold("me", token=token)
    check("it's paid in when PayPal says it cleared", me["gold"] == start + 1200, me)

    # 3. the buyer approved and closed the page: PayPal's webhook captures and pays it in
    _, r = gold("order", token=token, pack="sack")
    closed = r["orderId"]
    call(mock, "mock/approve/" + closed, {})
    webhook({"event_type": "CHECKOUT.ORDER.APPROVED", "resource": {"id": closed, "status": "APPROVED"}})
    _, me = gold("me", token=token)
    check("an approved order the page never captured is paid in by the webhook", me["gold"] == start + 1725, me)

    # 4. a webhook lying about an order changes nothing (PayPal is asked)
    _, r = gold("order", token=token, pack="vault")
    unpaid = r["orderId"]
    webhook({"event_type": "PAYMENT.CAPTURE.COMPLETED", "resource": {"id": "FAKE", "supplementary_data": {"related_ids": {"order_id": unpaid}}}})
    webhook({"event_type": "PAYMENT.CAPTURE.REFUNDED", "resource": {"id": "R1", "links": [{"rel": "up", "href": "https://x/v2/payments/captures/" + capture}]}})
    _, me = gold("me", token=token)
    check("a made-up webhook pays and takes nothing", me["gold"] == start + 1725, me)

    # 5. a refund takes the gold back
    call(mock, "mock/refund/" + capture, {})
    webhook({"event_type": "PAYMENT.CAPTURE.REFUNDED", "resource": {"id": "R1", "links": [{"rel": "up", "href": "https://x/v2/payments/captures/" + capture}]}})
    webhook({"event_type": "PAYMENT.CAPTURE.REFUNDED", "resource": {"id": "R1", "links": [{"rel": "up", "href": "https://x/v2/payments/captures/" + capture}]}})
    _, me = gold("me", token=token)
    check("a refund takes its 1,100 back, once", me["gold"] == start + 625, me)
    check("the purchase shows refunded", any(p["order"] == order and p["status"] == "refunded" for p in me["purchases"]), me)

    # 6. what's refused
    code, r = gold("order", token=token, pack="nope")
    check("an unknown pack is refused", code == 400, r)
    code, r = gold("capture", token=token, orderId="../../account.1")
    check("a made-up order id is refused", code == 400, r)
    gold("signout", token=token)
    code, r = gold("me", token=token)
    check("a signed-out session is refused", code == 401, r)
    code, r = gold("order", token="0" * 48, pack="chest")
    check("a made-up session is refused", code == 401, r)

    print("%d failed" % len(FAILS) if FAILS else "all passed")
    sys.exit(1 if FAILS else 0)


if __name__ == "__main__":
    main()
