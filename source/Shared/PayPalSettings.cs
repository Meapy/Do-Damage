using System;
using Shared.database;

namespace Shared
{
    /// <summary>
    /// The gold store's PayPal settings (serverSettings.paypal in the account server's server.json).
    /// Each can come from the environment instead (BSK_PAYPAL_CLIENT_ID, BSK_PAYPAL_SECRET,
    /// BSK_PAYPAL_MODE, BSK_PAYPAL_PAYEE). Never commit the secret: deploy/deploy.py writes it
    /// into the server's copy from the git-ignored deploy/paypal.local.json.
    /// </summary>
    public sealed class PayPalSettings
    {
        // "live" takes real money; anything else (empty too) is "sandbox", PayPal's test site
        // (developer.paypal.com)
        public string mode { get; set; } = "";
        // the REST app's keys (developer.paypal.com, Apps & Credentials)
        public string clientId { get; set; } = "";
        public string secret { get; set; } = "";
        // the PayPal account the money goes to. Empty: the account that owns the REST app.
        public string payeeEmail { get; set; } = "";
        // The store sells in euros, €1 for 100 gold (GoldStore.DefaultPacks); until 2.6.9 it was US
        // dollars. A "currency" left in an older config is ignored: the packs' prices are euros.
        public const string StoreCurrency = "EUR";
        // the shop's name on PayPal's pages and the buyer's receipt
        public string brandName { get; set; } = "Do Damage";
        // null or empty: GoldStore.DefaultPacks
        public GoldPack[] packs { get; set; } = null;
        // PayPal's API, when it's neither of its own (a test stand-in); empty: from the mode
        public string apiBase { get; set; } = "";

        public string Mode => Pick(mode, "BSK_PAYPAL_MODE").ToLowerInvariant() == "live" ? "live" : "sandbox";
        public string ClientId => Pick(clientId, "BSK_PAYPAL_CLIENT_ID");
        public string Secret => Pick(secret, "BSK_PAYPAL_SECRET");
        public string PayeeEmail => Pick(payeeEmail, "BSK_PAYPAL_PAYEE");
        public string Currency => StoreCurrency;
        public bool Enabled => ClientId.Length > 0 && Secret.Length > 0;

        public string ApiBase =>
            !string.IsNullOrWhiteSpace(apiBase) ? apiBase.Trim().TrimEnd('/')
            : Mode == "live" ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";

        private static string Pick(string configured, string env)
        {
            var value = string.IsNullOrWhiteSpace(configured) ? Environment.GetEnvironmentVariable(env) : configured;
            return value?.Trim() ?? "";
        }
    }
}
