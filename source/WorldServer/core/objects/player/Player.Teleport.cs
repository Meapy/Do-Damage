using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    partial class Player
    {
        private int _canTpCooldownTime;

        public bool CanTeleport() => _canTpCooldownTime <= 0;

        public void Teleport(TickTime time, int objId, bool ignoreRestrictions = false)
        {
            var obj = World.GetEntity(objId);
            if (obj == null)
            {
                SendError("Target does not exist.");
                return;
            }

            if (!ignoreRestrictions)
            {
                if (Id == objId)
                {
                    SendInfo("You are already at yourself, and always will be!");
                    return;
                }

                if (!World.AllowTeleport && !Client.Account.Admin)
                {
                    SendError("Cannot teleport here.");
                    return;
                }

                if (HasConditionEffect(ConditionEffectIndex.Paused))
                {
                    SendError("Cannot teleport while paused.");
                    return;
                }

                if (obj is not Player && !IsBeacon(obj))
                {
                    SendError("Can only teleport to players.");
                    return;
                }

                if (obj.HasConditionEffect(ConditionEffectIndex.Invisible))
                {
                    SendError("Cannot teleport to an invisible player.");
                    return;
                }

                if (obj.HasConditionEffect(ConditionEffectIndex.Paused))
                {
                    SendError("Cannot teleport to a paused player.");
                    return;
                }

                if (obj is Player p && p.IsHidden)
                {
                    SendError("Target does not exist.");
                    return;
                }

                if (!CanTeleport())
                {
                    SendError("Too soon to teleport again!");
                    return;
                }
            }

            ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 2500);
            ApplyConditionEffect(ConditionEffectIndex.Stunned, 2500);
            // a Realm 2 waystone stands in the way: beside it; on stacked floors, on their floor (at the
            // Sky Bar, not under it)
            TeleportPosition(time, new Position(obj.X, IsBeacon(obj) ? obj.Y + 1.5f : obj.Y), ignoreRestrictions, obj.Z);
        }

        private bool IsBeacon(Entity obj) => World is RealmWorld realm && realm.RealmManager.IsBeacon(obj);

        /// <summary>On stacked floors, where a teleport put us and the height there, until the client's
        /// Move arrives there: its Height before then is the one it had before (HeightHandler).</summary>
        public (float X, float Y, float Z)? TeleportHeight { get; set; }

        public void TeleportPosition(TickTime time, float x, float y, bool ignoreRestrictions = false, float? z = null) => TeleportPosition(time, new Position(x, y), ignoreRestrictions, z);
        /// <summary>On stacked floors, `z` is the height to land near (whoever we go to's), else our
        /// own: the ground of the floor there within reach of it.</summary>
        public void TeleportPosition(TickTime time, Position position, bool ignoreRestrictions = false, float? z = null)
        {
            if (!ignoreRestrictions)
            {
                if (!CanTeleport())
                {
                    SendError("Too soon to teleport again!");
                    return;
                }

                _canTpCooldownTime = 10000;
                ResetNewbiePeriod();
                FameCounter.Teleport();
            }

            // on stacked floors, onto the ground there: kept until the client's Move gets there
            // (HeightHandler), as the client may not know that floor's squares yet. Our height goes to
            // the client even when it hasn't changed: after a Goto it takes the server's (World.apply_goto)
            if (World.Levels != null)
            {
                Z = World.PlaceZ(position.X, position.Y, z ?? Z);
                TeleportHeight = (position.X, position.Y, Z);
                InvokeStatChange(StatDataType.Height, HeightStat);
            }

            var tpPkts = new OutgoingMessage[]
            {
                new GotoMessage(Id, position),
                new ShowEffect()
                {
                    EffectType = EffectType.Teleport,
                    TargetObjectId = Id,
                    Pos1 = position,
                    Color = new ARGB(0xFFFFFFFF)
                }
            };

            World.ForeachPlayer(_ =>
            {
                _.AwaitGotoAck(time.TotalElapsedMs);
                _.Client.SendPackets(tpPkts);
            });

            UpdateTiles();
        }

        /// <summary>Moves the player at once with none of a teleport's show: no flash, no cooldown, not
        /// counted. For a jump nobody should notice (the Stair Tower's looping stairs, StairTowerWorld).</summary>
        public void Shift(TickTime time, Position position)
        {
            var goTo = new GotoMessage(Id, position);
            World.ForeachPlayer(_ =>
            {
                _.AwaitGotoAck(time.TotalElapsedMs);
                _.Client.SendPacket(goTo);
            });
            UpdateTiles();
        }
    }
}