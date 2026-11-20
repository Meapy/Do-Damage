using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.database;

namespace App
{
    /// <summary>
    /// PayPal's Orders API (v2), as the gold store uses it: make an order for a pack, capture it
    /// once the buyer approved it on PayPal's page, and look an order or capture up. What PayPal
    /// answers is what counts: the page and the webhook only say which order to look at.
    /// https://developer.paypal.com/docs/api/orders/v2/
    /// </summary>
    public sealed class PayPal
    {
        public sealed class PayPalException : Exception
        {
            public readonly HttpStatusCode Status;
            public readonly string Issue;
            public PayPalException(HttpStatusCode status, string issue, string message) : base(message)
            {
                Status = status;
                Issue = issue;
            }
        }

        /// <summary>What a capture (or a look at a captured order) came to.</summary>
        public sealed class Capture
        {
            public string OrderId;
            public string OrderStatus;    // COMPLETED once paid, APPROVED before the capture
            public string CaptureId;
            public string CaptureStatus;  // COMPLETED, PENDING, DECLINED, REFUNDED...
            public string Amount;
            public string Currency;
            public string CustomId;       // "<account>:<pack>", as the order was made
            public string PayeeEmail;
            public string PayerEmail;
        }

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly PayPalSettings _settings;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private string _token;
        private DateTime _tokenUntil;

        public PayPal(PayPalSettings settings)
        {
            _settings = settings;
        }

        public bool Enabled => _settings.Enabled;
        public string ClientId => _settings.ClientId;
        public string Currency => _settings.Currency;
        public string Mode => _settings.Mode;

        // An access token, kept until a minute before PayPal says it expires.
        private async Task<string> Token()
        {
            if (_token != null && DateTime.UtcNow < _tokenUntil)
                return _token;
            await _tokenLock.WaitAsync();
            try
            {
                if (_token != null && DateTime.UtcNow < _tokenUntil)
                    return _token;
                using var req = new HttpRequestMessage(HttpMethod.Post, _settings.ApiBase + "/v1/oauth2/token")
                {
                    Content = new FormUrlEncodedContent(new[] { new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials") })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.ClientId + ":" + _settings.Secret)));
                using var res = await Http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    throw new PayPalException(res.StatusCode, "AUTHENTICATION_FAILURE", "PayPal refused the store's keys (" + (int)res.StatusCode + ")");
                var json = JObject.Parse(body);
                _token = (string)json["access_token"];
                _tokenUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, (int?)json["expires_in"] ?? 300) - 60);
                return _token;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<JObject> Send(HttpMethod method, string path, JObject body = null, string requestId = null)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var req = new HttpRequestMessage(method, _settings.ApiBase + path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token());
                req.Headers.Add("Prefer", "return=representation");
                // the same id again is the same request to PayPal: a retry never makes or captures twice
                if (requestId != null)
                    req.Headers.Add("PayPal-Request-Id", requestId);
                if (body != null || method == HttpMethod.Post)
                    req.Content = new StringContent((body ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using var res = await Http.SendAsync(req);
                var text = await res.Content.ReadAsStringAsync();
                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null; // expired early: once more with a new token
                    continue;
                }
                if (res.IsSuccessStatusCode)
                    return text.Length == 0 ? new JObject() : JObject.Parse(text);
                string issue = null, message = null;
                try
                {
                    var err = JObject.Parse(text);
                    issue = (string)err.SelectToken("details[0].issue") ?? (string)err["name"];
                    message = (string)err.SelectToken("details[0].description") ?? (string)err["message"];
                }
                catch (Newtonsoft.Json.JsonException) { }
                throw new PayPalException(res.StatusCode, issue ?? "", message ?? $"PayPal answered {(int)res.StatusCode}");
            }
        }

        /// <summary>Makes the order for a pack bought by an account; its PayPal id.</summary>
        public async Task<string> CreateOrder(GoldPack pack, int accountId, string accountName, string requestId)
        {
            var currency = _settings.Currency;
            var amount = new JObject { ["currency_code"] = currency, ["value"] = pack.PriceText };
            var unit = new JObject
            {
                ["reference_id"] = pack.id,
                ["custom_id"] = accountId + ":" + pack.id,
                ["invoice_id"] = "DD-" + requestId,
                ["description"] = $"{pack.gold:N0} gold for {accountName}",
                ["soft_descriptor"] = "DODAMAGE GOLD",
                ["amount"] = new JObject
                {
                    ["currency_code"] = currency,
                    ["value"] = pack.PriceText,
                    ["breakdown"] = new JObject { ["item_total"] = amount.DeepClone() }
                },
                ["items"] = new JArray(new JObject
                {
                    ["name"] = $"{pack.gold:N0} Gold",
                    ["description"] = $"Do Damage gold for the account {accountName}",
                    ["sku"] = "gold-" + pack.id,
                    ["quantity"] = "1",
                    ["category"] = "DIGITAL_GOODS",
                    ["unit_amount"] = amount.DeepClone()
                })
            };
            if (_settings.PayeeEmail.Length > 0)
                unit["payee"] = new JObject { ["email_address"] = _settings.PayeeEmail };
            var order = new JObject
            {
                ["intent"] = "CAPTURE",
                ["purchase_units"] = new JArray(unit),
                ["application_context"] = new JObject
                {
                    ["brand_name"] = _settings.brandName ?? "Do Damage",
                    ["shipping_preference"] = "NO_SHIPPING",
                    ["user_action"] = "PAY_NOW"
                }
            };
            var res = await Send(HttpMethod.Post, "/v2/checkout/orders", order, "create-" + requestId);
            return (string)res["id"];
        }

        /// <summary>Captures an approved order (takes the money). Captured before: how that went.</summary>
        public async Task<Capture> CaptureOrder(string orderId)
        {
            try
            {
                return Read(await Send(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/capture", null, "capture-" + orderId));
            }
            catch (PayPalException e) when (e.Issue == "ORDER_ALREADY_CAPTURED")
            {
                return await GetOrder(orderId);
            }
        }

        public async Task<Capture> GetOrder(string orderId) =>
            Read(await Send(HttpMethod.Get, $"/v2/checkout/orders/{orderId}"));

        /// <summary>A capture's status now (REFUNDED, PARTIALLY_REFUNDED, REVERSED, COMPLETED...).</summary>
        public async Task<string> CaptureStatus(string captureId) =>
            (string)(await Send(HttpMethod.Get, $"/v2/payments/captures/{captureId}"))["status"];

        private static Capture Read(JObject order)
        {
            var unit = order.SelectToken("purchase_units[0]");
            var capture = unit?.SelectToken("payments.captures[0]");
            return new Capture
            {
                OrderId = (string)order["id"],
                OrderStatus = (string)order["status"],
                CaptureId = (string)capture?["id"],
                CaptureStatus = (string)capture?["status"],
                Amount = (string)(capture?.SelectToken("amount.value") ?? unit?.SelectToken("amount.value")),
                Currency = (string)(capture?.SelectToken("amount.currency_code") ?? unit?.SelectToken("amount.currency_code")),
                CustomId = (string)(capture?["custom_id"] ?? unit?["custom_id"]),
                PayeeEmail = (string)unit?.SelectToken("payee.email_address"),
                PayerEmail = (string)order.SelectToken("payer.email_address"),
            };
        }

        /// <summary>Whether two PayPal amounts are the same money ("9.99" and "9.990").</summary>
        public static bool SameAmount(string a, string b) =>
            decimal.TryParse(a, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && decimal.TryParse(b, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var y)
            && x == y;
    }
}
