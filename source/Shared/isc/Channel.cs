using System.ComponentModel;

namespace Shared.isc
{
    public enum Channel
    {
        [Description("Network")]
        Network,

        [Description("Control")]
        Control,

        [Description("Chat")]
        Chat,

        [Description("Restart")]
        Restart,

        [Description("Announce")]
        Announce,

        [Description("Kick")]
        Kick,

        // a Marketplace sale or expiry for an account: the server it plays on updates its fame
        // and tells it (Marketplace.OnMarketMsg)
        [Description("Market")]
        Market
    }
}
