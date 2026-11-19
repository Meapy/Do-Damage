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
        Market,

        // a /visit or /summon between two world servers, such as the old and the new one of a
        // hand-over (WorldServer RemoteVisits)
        [Description("Visit")]
        Visit,

        // the gold store paid or took back gold (App GoldController): the server the account
        // plays on updates its gold and tells it (WorldServer GoldNews)
        [Description("Gold")]
        Gold
    }
}
