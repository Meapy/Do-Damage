using System.Collections.Generic;
using Shared;
using Shared.resources;
using WorldServer.core.net.stats;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // Lingering at the grave (the Rust client from 2.5.13, GRAVE and GRAVE_INFO): a player who
        // dies isn't sent away at once. The death is recorded as before (the character, its fame,
        // the gravestone, the announcement), then the player stays in the world as a ghost: seen
        // by everyone as one (StatDataType.Ghost), floating through walls, watching and chatting,
        // until they go on to the death screen (by closing the connection), GhostMs runs out, or
        // anything would move them to another world. A ghost can't be hit, seen by enemies,
        // credited with damage or loot, traded with or teleported to, and sends nothing but its
        // moves, its chat and the acks the server needs (GhostMessages). Clients that never said
        // they can (GRAVE 255: the AIR client, older Rust clients) are disconnected at death, as
        // before.

        // How long a ghost may stay, ms.
        public const int GhostMs = 10 * 60 * 1000;

        // The messages a ghost's client may still send: the rest are dropped unread.
        private static readonly HashSet<MessageId> GhostMessages = new HashSet<MessageId>
        {
            MessageId.MOVE,
            MessageId.PONG,
            MessageId.UPDATEACK,
            MessageId.GOTOACK,
            MessageId.SHOOTACK,
            MessageId.AOEACK,
            MessageId.PLAYERTEXT,
            MessageId.VIEW_RADIUS,
            MessageId.HEIGHT,
            MessageId.GRAVE,
            MessageId.PING_MARK, // a ghost can still point things out to those it watches over
            MessageId.EDITACCOUNTLIST,
            MessageId.QUEST_DAMAGE_SUBSCRIBE,
            MessageId.QUEST_DAMAGE_FULL_SUBSCRIBE,
        };

        // The commands a ghost may use (by CommandName): talking, and looking who's about.
        public static readonly HashSet<string> GhostCommands = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "tell", "party", "guild", "who", "online", "ignore", "unignore", "position", "servers", "help", "uptime",
        };

        // The client can stay at its grave (GRAVE 255, once in each world).
        public bool CanLinger { get; private set; }

        // Dead, and lingering as a ghost.
        public bool Ghost { get; private set; }

        // When the ghost's time runs out (the world's clock, ms).
        private long _ghostUntil;

        public void AllowLinger() => CanLinger = true;

        // Nothing takes hold of a ghost: no enemy's or ally's condition.
        protected override bool CanApplyCondition(ConditionEffectIndex effect) => !Ghost && base.CanApplyCondition(effect);

        public static bool GhostMayDo(MessageId id) => GhostMessages.Contains(id);

        // Death's last step for a client that can linger: stay as a ghost instead of being
        // disconnected. False if this world can't keep one (the death then disconnects).
        private bool TryLinger(string killer)
        {
            if (!CanLinger || World == null || Client == null)
                return false;

            Ghost = true;
            Dead = false; // stays in the world (World removes dead players)
            _ghostUntil = -1;

            if (TradeTarget != null)
                CancelTrade();
            foreach (ConditionEffectIndex effect in System.Enum.GetValues(typeof(ConditionEffectIndex)))
                if (effect != ConditionEffectIndex.Nothing && HasConditionEffect(effect))
                    RemoveCondition(effect);
            Health = Stats[0];
            Mana = Stats[1];
            // a censer's ward dies with them
            Ward = 0;
            WardMax = 0;
            _wardUntil = 0;

            InvokeStatChange(StatDataType.Ghost, 1);
            Client.SendPacket(new DeathMessage(AccountId, Client.Character.CharId, killer));
            Client.SendPacket(new GraveInfo(GhostMs));
            StaticLogger.Instance.Info($"{Name} died to {killer} in {World.IdName} and lingers as a ghost");
            return true;
        }

        // A ghost's tick: only its time running out.
        private void GhostTick(TickTime time)
        {
            if (_ghostUntil < 0)
                _ghostUntil = time.TotalElapsedMs + GhostMs;
            else if (time.TotalElapsedMs >= _ghostUntil)
                Client.Disconnect("Ghost faded");
        }

        // A ghost is never moved to another world (its character is dead): it goes on to its
        // death screen instead.
        public bool LeaveAsGhost()
        {
            if (!Ghost)
                return false;
            Client.Disconnect("Ghost left the world");
            return true;
        }
    }
}
