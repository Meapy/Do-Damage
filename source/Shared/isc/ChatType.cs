namespace Shared.isc
{
    public enum ChatType
    {
        Tell,
        Guild,
        Info,
        Party,
        HelpCall,
        /// <summary>/all: to every player on every world server.</summary>
        All,
        /// <summary>A new Marketplace listing: to every player on every world server.</summary>
        Market
    }
}
