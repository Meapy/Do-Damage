namespace Shared.isc.data
{
    // A /visit or /summon between two world servers (WorldServer core/RemoteVisits.cs). The
    // visitor is the player who asked, on the server that sends the Ask; the target is the one
    // asked, on the server it goes to.
    public enum VisitMsgKind
    {
        // visitor's server -> target's: show the target this request (RequestId: the visitor's
        // server's own id for it, sent back in every answer)
        Ask,
        // target's server -> visitor's: the request is up on the target's screen (WorldName: where)
        Asked,
        // target's server -> visitor's: no request after all, or a Yes that can't be kept (Text: why)
        Refused,
        // target's server -> visitor's: the target said No
        No,
        // visitor's server -> target's: /visit cancel or /summon cancel
        Withdraw,
        // target's server -> visitor's: the target said Yes to a summon; the visitor's server
        // checks the summoner's world still takes them and answers with a Go
        Accept,
        // either way: send your player Mover into world WorldId here, with the reconnect Key
        // this server keeps for them (Text: told to them first)
        Go,
        // either way: tell player Mover Text (Error: as an error)
        Tell,
        // an admin's /visit: the target's server lets them straight in (a Go)
        AdminVisit,
    }

    public struct VisitMsg
    {
        public VisitMsgKind Kind;
        public bool Summon;
        public int RequestId;
        public int Visitor;
        public string VisitorName;
        public int Target;
        public string TargetName;
        public int Mover;
        public int WorldId;
        public string WorldName;
        public string Key;
        public string Text;
        public bool Error;
    }
}
