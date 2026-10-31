using Shared;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // Standing still in the Nexus for serverSettings.nexusAfkMinutes sends the player back to
        // the character list, saved, so idlers don't hold a place or count as online. Only
        // walking resets it (MoveHandler); admins are never sent away.
        private readonly NexusAfk _nexusAfk = new NexusAfk();

        public void MovedForAfk() => _nexusAfk.Moved();

        private void TickNexusAfk(TickTime time)
        {
            if (World is not NexusWorld)
            {
                _nexusAfk.Moved();
                return;
            }
            if (Client?.Account == null || Client.Account.Admin)
                return;
            var settings = GameServer.Configuration.serverSettings;
            if (!_nexusAfk.Tick(time.ElapsedMsDelta, NexusAfk.LimitMs(settings)))
                return;
            StaticLogger.Instance.Info($"{Name} stood still in the Nexus for {_nexusAfk.StillMs / 1000} s: back to the character list");
            // a Failure ends the connection, which saves the character; the Rust client takes it
            // as final (no reconnecting) and shows the message on the character list
            Client.SendFailure(NexusAfk.Message(settings), FailureMessage.MessageWithDisconnect);
        }
    }
}
