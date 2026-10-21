using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // On entering its state, marks a random player within range as the host's target (so Follow,
    // Shoot and the rest go after them, not the nearest) and names them in chat: one of `taunts`,
    // with {PLAYER} for their name. The mark lasts until the state is left, or the player is.
    internal class HuntRandomPlayer : Behavior
    {
        private readonly double _range;
        private readonly string[] _taunts;

        public HuntRandomPlayer(double range, params string[] taunts)
        {
            _range = range;
            _taunts = taunts;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var players = host.GetNearestEntities(_range, null).OfType<Player>().ToList();
            if (players.Count == 0)
                return;

            var prey = players[Random.Next(players.Count)];
            host.AttackTarget = prey;
            if (_taunts.Length == 0)
                return;

            var name = host.ObjectDesc.DisplayId ?? host.ObjectDesc.IdName;
            host.World.Broadcast(new Text()
            {
                Name = "#" + name,
                ObjectId = host.Id,
                NumStars = -1,
                BubbleTime = 0,
                Recipient = "",
                Txt = _taunts[Random.Next(_taunts.Length)].Replace("{PLAYER}", prey.Name)
            });
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var prey = host.AttackTarget;
            if (prey != null && (prey.World != host.World || prey.Dead))
                host.AttackTarget = null;
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state) => host.AttackTarget = null;
    }
}
