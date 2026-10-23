using System.Linq;
using System.Text;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.logic.transitions;

namespace WorldServer.logic.behaviors
{
    internal class AnnounceOnDeath : Behavior
    {
        public static readonly string PLAYER_COUNT = "{COUNT}";
        public static readonly string PLAYER_LIST = "{PL_LIST}";
        private readonly string _message;

        public AnnounceOnDeath(string msg) => _message = msg;

        public override void OnDeath(Entity host, ref TickTime time)
        {
            // nor in the arenas, where Oryx 5 falling isn't the Realm set free
            if (host.Spawned || host.World is TestWorld || ArenaTransition.InArena(host))
                return;

            var owner = host.World;
            var sb = new StringBuilder();
            var players = owner.Players.Values.Where(_ => _.Client != null).ToArray();
            for (var i = 0; i < players.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                _ = sb.Append(players[i].Name);
            }

            var playerList = sb.ToString();
            var playerCount = owner.Players.Count(p => p.Value.Client != null).ToString();
            var announcement = _message.Replace(PLAYER_COUNT, playerCount).Replace(PLAYER_LIST, playerList);
            host.GameServer.ChatManager.ServerAnnounce(announcement);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
