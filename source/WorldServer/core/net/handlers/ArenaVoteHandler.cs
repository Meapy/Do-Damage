using Shared;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // A vote in the Arena between waves (the Rust client's Arena panel): 0 to start the next wave,
    // 1 to 5 to combine that many waves into the next fight, 255 to get ARENA_VOTES from now on.
    // Anywhere else it does nothing.
    public class ArenaVoteHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.ARENA_VOTE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var action = rdr.ReadByte();
            if (client.Player?.World is DoomedBattleArena arena)
                arena.Vote(client.Player, action);
        }
    }
}
