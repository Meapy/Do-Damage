using System;
using System.Linq;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // Pings (the Rust client's middle mouse button, PING_MARK): a mark on the map that the
        // player's party and guild in the same world see wherever they are, and everyone else
        // within PingNear tiles of the player or the mark. Only clients that said they can show
        // pings (PING_MARK 255) get them; a hidden staff member's go to their party alone.

        // The kinds: look here, danger, go here, loot, help.
        public const byte PingKinds = 5;
        // How near (tiles) a player who is neither party nor guild must be to see a ping.
        public const float PingNear = 20;
        // At most PingBurst pings in PingWindowMs, each at least PingGapMs after the last.
        public const int PingBurst = 4;
        public const long PingWindowMs = 5000;
        public const long PingGapMs = 250;

        // The client can show pings (PING_MARK 255, the Rust client from 2.3.10).
        public bool WantsPings { get; private set; }
        // When the last PingBurst pings went (the world's clock, ms), oldest first.
        private readonly long[] _pingTimes = new long[PingBurst];
        private int _pingCount;

        // The client can show pings: say the server knows them, so it starts sending its own.
        public void ShowPings()
        {
            WantsPings = true;
            Client.SendPacket(new PingShow { PlayerId = Id, Name = Name, Kind = 255 });
        }

        // Whether a ping now (`now`, the world's clock) keeps within the limits; if so it counts.
        public bool TakePing(long now) => TakePing(_pingTimes, ref _pingCount, now);

        // The limit on its own, for the tests: `times` the last pings, oldest first.
        public static bool TakePing(long[] times, ref int count, long now)
        {
            if (count > 0 && now - times[count - 1] < PingGapMs)
                return false;
            if (count == times.Length)
            {
                if (now - times[0] < PingWindowMs)
                    return false;
                Array.Copy(times, 1, times, 0, times.Length - 1);
                count--;
            }
            times[count++] = now;
            return true;
        }

        // Who `from` is to `to`: 3 themselves, 2 the same party, 1 the same guild, 0 anyone else.
        public static byte PingRelation(Player from, Player to)
        {
            if (from == to)
                return 3;
            var party = from.Client?.Account?.PartyId ?? 0;
            if (party > 0 && party == (to.Client?.Account?.PartyId ?? 0))
                return 2;
            if (from.InGuild && string.Equals(from.Guild, to.Guild, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        // A ping from this player: kind 0-4 at (x, y) on the floor at height z, marking object
        // `target` (0: none). Out of the map, of the limits or of the kinds, it goes nowhere.
        public void Ping(byte kind, float x, float y, float z, int target, long now)
        {
            var world = World;
            if (world?.Map == null || kind >= PingKinds || !WantsPings)
                return;
            if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 || x >= world.Map.Width || y >= world.Map.Height)
                return;
            z = float.IsFinite(z) && world.Levels != null ? Math.Clamp(z, 0, world.Levels.Count * world.LevelHeight) : 0;
            // an object marked follows it (where the server has it); one that isn't here marks nothing
            if (target != 0)
            {
                var e = world.GetEntity(target);
                if (e == null)
                    target = 0;
                else
                    (x, y) = (e.X, e.Y);
            }
            if (!TakePing(now))
                return;
            var near = PingNear * PingNear;
            foreach (var other in world.Players.Values)
            {
                if (!other.WantsPings || other.Client == null)
                    continue;
                var relation = PingRelation(this, other);
                if (relation < 3 && (other.Client.Account?.IgnoreList ?? Array.Empty<int>()).Contains(AccountId))
                    continue;
                if (IsHidden && relation < 2)
                    continue;
                if (relation == 0 && other.SqDistTo(this) > near && other.SqDistTo(x, y) > near)
                    continue;
                other.Client.SendPacket(new PingShow
                {
                    PlayerId = Id,
                    Name = Name,
                    Kind = kind,
                    X = x,
                    Y = y,
                    Z = z,
                    TargetId = target,
                    Relation = relation,
                });
            }
        }
    }
}
