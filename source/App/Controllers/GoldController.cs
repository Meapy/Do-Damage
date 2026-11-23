using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.database;
using Shared.isc;
using Shared.isc.data;
using Shared.utils;

namespace App.Controllers
{
    /// <summary>
    /// The gold store (dodmg.com/gold, website/gold.html): a player signs in, picks a pack and
    /// pays with PayPal; the gold lands on the account at once, in game too.
    ///
    /// 1. store: the packs, the currency and PayPal's client id for its buttons.
    /// 2. signin: name (or email) and password, once; a store session token from then on.
    /// 3. order: PayPal makes an order for the pack, at the price set here (the page never says
    ///    how much), and it's recorded as the account's (GoldStore.AddOrder).
    /// 4. The buyer approves it in PayPal's window; capture takes the money and pays the gold,
    ///    after checking with PayPal that the capture completed, for the order's amount and
    ///    currency, for this account and pack (GoldStore.Credit, once only).
    /// 5. paypal-webhook: PayPal's own word, for a buyer who closed the page after approving, a
    ///    payment that was pending, and refunds and chargebacks (which take the gold back). Its
    ///    body only says which order or capture to look up: everything is asked of PayPal again.
    ///
    /// Answers are JSON: {"error": "..."} with status 400 when something is refused.
    /// </summary>
    [ApiController]
    [Route("gold")]
    [RequestSizeLimit(MaxRequestBytes)]
    public class GoldController : ControllerBase
    {
        private const int MaxRequestBytes = 60 * 1024;

        private readonly CoreService _core;

        public GoldController(CoreService core)
        {
            _core = core;
        }

        private IActionResult Json(object value, int status = 200) =>
            new ContentResult
            {
                Content = JsonConvert.SerializeObject(value),
                ContentType = "application/json",
                StatusCode = status
            };

        private IActionResult Error(string message, int status = 400) => Json(new { error = message }, status);

        private static object Pack(GoldPack p) => new { p.id, p.gold, p.bonus, price = p.PriceText, p.best };

        private GoldPack[] Packs => GoldStore.Packs(_core.Config.serverSettings.paypal.packs);

        private object History(int accountId) =>
            GoldStore.History(_core.Database.Conn, accountId).Select(o => new
            {
                order = o.OrderId,
                gold = o.Gold,
                price = o.Price,
                currency = o.Currency,
                status = o.Status,
                at = (o.Credited ?? o.Created).ToString("o"),
                takenBack = o.TakenBack
            }).ToArray();

        private object Me(int accountId)
        {
            var acc = _core.Database.GetAccount(accountId);
            return new
            {
                name = acc?.Name ?? "",
                gold = GoldStore.Balance(_core.Database.Conn, accountId),
                purchases = History(accountId)
            };
        }

        // the session's account, or null with the error sent
        private int? Session(string token, out IActionResult refused)
        {
            refused = null;
            var id = GoldStore.SessionAccount(_core.Database.Conn, token);
            if (id == null)
                refused = Error("Your session ended: sign in again.", 401);
            return id;
        }

        [HttpGet("store")]
        public IActionResult Store()
        {
            var pp = _core.PayPal;
            return Json(new
            {
                enabled = pp.Enabled,
                clientId = pp.Enabled ? pp.ClientId : "",
                currency = pp.Currency,
                sandbox = pp.Mode != "live",
                packs = Packs.Select(Pack).ToArray()
            });
        }

        [HttpPost("signin")]
        public IActionResult SignIn([FromForm] string guid, [FromForm] string password)
        {
            var status = _core.Database.Verify(guid?.Trim(), password, out var acc);
            if (status != DbLoginStatus.OK)
                return Error(status.GetInfo(), 401);
            if (acc.Banned)
                return Error("This account is banned.", 403);
            var token = GoldStore.NewSession(_core.Database.Conn, acc.AccountId);
            var me = JObject.FromObject(Me(acc.AccountId));
            me["token"] = token;
            return Json(me);
        }

        [HttpPost("me")]
        public IActionResult MeNow([FromForm] string token)
        {
            var id = Session(token, out var refused);
            return id == null ? refused : Json(Me(id.Value));
        }

        [HttpPost("signout")]
        public IActionResult SignOut([FromForm] string token)
        {
            GoldStore.EndSession(_core.Database.Conn, token);
            return Json(new { ok = true });
        }

        [HttpPost("order")]
        public async Task<IActionResult> Order([FromForm] string token, [FromForm] string pack)
        {
            var pp = _core.PayPal;
            if (!pp.Enabled)
                return Error("The gold store is closed for now.", 503);
            var id = Session(token, out var refused);
            if (id == null)
                return refused;
            var chosen = Packs.FirstOrDefault(p => p.id == pack);
            if (chosen == null)
                return Error("That pack isn't sold.");
            var acc = _core.Database.GetAccount(id.Value);
            if (acc == null || acc.Banned)
                return Error("This account can't buy gold.", 403);
            var db = _core.Database.Conn;
            if (!GoldStore.MayMakeOrder(db, id.Value))
                return Error("That's a lot of orders in an hour: wait a little and try again.", 429);

            var requestId = Guid.NewGuid().ToString("N");
            try
            {
                var orderId = await pp.CreateOrder(chosen, id.Value, acc.Name, requestId);
                if (!GoldStore.ValidOrderId(orderId))
                    return Error("PayPal didn't make the order: try again.", 502);
                GoldStore.AddOrder(db, orderId, id.Value, chosen, pp.Currency, DateTime.UtcNow);
                _core.Logger.Info($"Gold: order {orderId} for <{acc.Name}-{acc.AccountId}>, {chosen.gold} gold for {chosen.PriceText} {pp.Currency}");
                return Json(new { orderId });
            }
            catch (Exception e) when (e is PayPal.PayPalException || e is System.Net.Http.HttpRequestException || e is TaskCanceledException)
            {
                _core.Logger.Error($"Gold: PayPal refused an order for <{acc.Name}-{acc.AccountId}>: {e.Message}");
                return Error("PayPal couldn't be reached: try again in a moment.", 502);
            }
        }

        [HttpPost("capture")]
        public async Task<IActionResult> Capture([FromForm] string token, [FromForm] string orderId)
        {
            var id = Session(token, out var refused);
            if (id == null)
                return refused;
            var order = GoldStore.GetOrder(_core.Database.Conn, orderId);
            if (order == null || order.AccountId != id.Value)
                return Error("That order isn't one of yours.");

            try
            {
                var outcome = await Settle(order, capture: true);
                return Json(new
                {
                    status = outcome.Status,
                    gold = order.Gold,
                    balance = outcome.Balance,
                    message = outcome.Message,
                    purchases = History(id.Value)
                }, outcome.Status == "failed" || outcome.Status == "declined" ? 400 : 200);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException)
            {
                _core.Logger.Error($"Gold: capturing {orderId} failed: {e.Message}".ForLog());
                return Error("PayPal couldn't be reached. If you paid, your gold arrives by itself within minutes.", 502);
            }
        }

        private sealed class Outcome
        {
            public string Status;   // credited, pending, declined, failed
            public int Balance;
            public string Message;
        }

        /// <summary>
        /// Captures the order (when it's approved and capture is true) or looks it up, checks what
        /// PayPal says against the order, and pays the gold when the money is in.
        /// </summary>
        private async Task<Outcome> Settle(GoldOrder order, bool capture)
        {
            var pp = _core.PayPal;
            var db = _core.Database.Conn;
            if (order.Status == GoldStore.Credited)
                return new Outcome { Status = "credited", Balance = GoldStore.Balance(db, order.AccountId), Message = "Already paid in." };
            if (order.Status == GoldStore.Refunded)
                return new Outcome { Status = "failed", Message = "This order was refunded." };

            PayPal.Capture c;
            try
            {
                c = await pp.GetOrder(order.OrderId);
                if (capture && c.OrderStatus == "APPROVED")
                    c = await pp.CaptureOrder(order.OrderId);
            }
            catch (PayPal.PayPalException e)
            {
                _core.Logger.Warn($"Gold: PayPal on order {order.OrderId}: {e.Issue} {e.Message}".ForLog());
                if (e.Issue == "INSTRUMENT_DECLINED")
                    return new Outcome { Status = "declined", Message = "PayPal declined that payment method: choose another one." };
                return new Outcome { Status = "failed", Message = "PayPal didn't take the payment: " + e.Message };
            }

            // what PayPal says must be this order, exactly
            var expected = order.AccountId + ":" + order.Pack;
            if (c.OrderId != order.OrderId || c.CustomId != expected || !PayPal.SameAmount(c.Amount, order.Price) || c.Currency != order.Currency)
            {
                _core.Logger.Error($"Gold: order {order.OrderId} doesn't match PayPal's: {c.CustomId} {c.Amount} {c.Currency}, expected {expected} {order.Price} {order.Currency}".ForLog());
                return new Outcome { Status = "failed", Message = "That payment doesn't match the order: nothing was paid in. Contact us on Discord." };
            }

            if (c.CaptureStatus == "COMPLETED")
            {
                var r = GoldStore.Credit(db, order.OrderId, c.CaptureId, c.PayerEmail, DateTime.UtcNow, out var balance);
                switch (r)
                {
                    case GoldStore.CreditResult.Credited:
                        _core.Logger.Info($"Gold: paid {order.Gold} gold into <{_core.Database.GetAccount(order.AccountId)?.Name}-{order.AccountId}> (order {order.OrderId}, capture {c.CaptureId}, {order.Price} {order.Currency})".ForLog());
                        _core.ISManager.Publish(Channel.Gold, new GoldMsg { AccountId = order.AccountId, Gold = order.Gold });
                        return new Outcome { Status = "credited", Balance = balance };
                    case GoldStore.CreditResult.AlreadyCredited:
                        return new Outcome { Status = "credited", Balance = balance, Message = "Already paid in." };
                    case GoldStore.CreditResult.Refunded:
                        return new Outcome { Status = "failed", Message = "This order was refunded." };
                    default:
                        _core.Logger.Error($"Gold: order {order.OrderId} was paid but couldn't be credited ({r})".ForLog());
                        return new Outcome { Status = "failed", Message = "Paid, but the gold couldn't be added: contact us on Discord with your order id." };
                }
            }
            if (c.CaptureStatus == "PENDING")
            {
                GoldStore.MarkPending(db, order.OrderId, c.CaptureId);
                _core.Logger.Info($"Gold: order {order.OrderId} is pending at PayPal (capture {c.CaptureId})".ForLog());
                return new Outcome { Status = "pending", Balance = GoldStore.Balance(db, order.AccountId),
                    Message = "PayPal is still processing the payment: your gold arrives by itself once it clears." };
            }
            if (c.OrderStatus == "CREATED" || c.OrderStatus == "SAVED" || c.OrderStatus == "PAYER_ACTION_REQUIRED")
                return new Outcome { Status = "failed", Message = "The payment wasn't approved in PayPal." };
            return new Outcome { Status = "failed", Message = $"PayPal says the payment is {(c.CaptureStatus ?? c.OrderStatus ?? "unknown").ToLowerInvariant()}." };
        }

        /// <summary>
        /// PayPal's webhook (developer.paypal.com, the REST app's Webhooks: https://play.dodmg.com/api/gold/paypal-webhook
        /// with the events below). The body isn't trusted: it only names an order or a capture,
        /// which is then looked up at PayPal with the store's own keys.
        /// </summary>
        [HttpPost("paypal-webhook")]
        public async Task<IActionResult> Webhook()
        {
            var pp = _core.PayPal;
            if (!pp.Enabled)
                return Ok();
            string text;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                text = await reader.ReadToEndAsync();
            JObject ev;
            try { ev = JObject.Parse(text); }
            catch (JsonException) { return BadRequest(); }

            var type = (string)ev["event_type"] ?? "";
            var resource = ev["resource"] as JObject ?? new JObject();
            try
            {
                switch (type)
                {
                    case "CHECKOUT.ORDER.APPROVED":
                    case "CHECKOUT.ORDER.COMPLETED":
                        await SettleFromWebhook((string)resource["id"], capture: true);
                        break;
                    case "PAYMENT.CAPTURE.COMPLETED":
                    case "PAYMENT.CAPTURE.PENDING":
                        await SettleFromWebhook((string)resource.SelectToken("supplementary_data.related_ids.order_id"), capture: false);
                        break;
                    case "PAYMENT.CAPTURE.REFUNDED":
                    case "PAYMENT.CAPTURE.REVERSED":
                        // REVERSED's resource is the capture; REFUNDED's is the refund, whose "up" link is the capture
                        var captureId = type == "PAYMENT.CAPTURE.REVERSED" ? (string)resource["id"] : CaptureOfRefund(resource);
                        await TakeBack(captureId);
                        break;
                }
            }
            catch (Exception e) when (e is PayPal.PayPalException || e is System.Net.Http.HttpRequestException || e is TaskCanceledException)
            {
                // PayPal sends it again later when it isn't answered with 2xx
                _core.Logger.Warn($"Gold: webhook {type} not handled yet: {e.Message}".ForLog());
                return StatusCode(503);
            }
            return Ok();
        }

        private async Task SettleFromWebhook(string orderId, bool capture)
        {
            var order = GoldStore.GetOrder(_core.Database.Conn, orderId);
            if (order == null)
                return; // not the store's (another sale on the same PayPal account), or long expired
            var outcome = await Settle(order, capture);
            _core.Logger.Info($"Gold: webhook settled order {orderId}: {outcome.Status}".ForLog());
        }

        private static string CaptureOfRefund(JObject refund)
        {
            var up = (refund["links"] as JArray)?.FirstOrDefault(l => (string)l["rel"] == "up");
            var href = (string)up?["href"];
            return href?.TrimEnd('/').Split('/').LastOrDefault();
        }

        private async Task TakeBack(string captureId)
        {
            if (string.IsNullOrEmpty(captureId) || !captureId.All(char.IsLetterOrDigit))
                return;
            var status = await _core.PayPal.CaptureStatus(captureId);
            if (status != "REFUNDED" && status != "REVERSED")
            {
                if (status == "PARTIALLY_REFUNDED")
                    _core.Logger.Warn($"Gold: capture {captureId} was partly refunded: the gold stays, decide by hand".ForLog());
                return;
            }
            var orderId = GoldStore.TakeBack(_core.Database.Conn, captureId, status.ToLowerInvariant(), DateTime.UtcNow, out var taken, out var accountId);
            if (orderId == null)
                return;
            _core.Logger.Warn($"Gold: order {orderId} {status.ToLowerInvariant()}: took {taken} gold back from account {accountId}");
            if (taken > 0)
                _core.ISManager.Publish(Channel.Gold, new GoldMsg { AccountId = accountId, Gold = -taken });
        }
    }
}
