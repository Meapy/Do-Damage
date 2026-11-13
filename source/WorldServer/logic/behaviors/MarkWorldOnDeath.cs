using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Marks the world (World.Marks) when the host dies. Once every mark in `all` is there, `spawn`
    // appears, once per world, on one of the world's spawn tiles, saying `taunt` to everyone in it:
    // the Candyland's Cupcake, which comes to the starting room after each of its five kinds of boss
    // has fallen at least once (RealmEye, Candyland Hunting Grounds). `deathTaunt`: what the host
    // says to everyone in the world as it dies.
    internal class MarkWorldOnDeath : Behavior
    {
        private readonly string _mark;
        private readonly string[] _all;
        private readonly string _spawn;
        private readonly string _taunt;
        private readonly string _deathTaunt;

        public MarkWorldOnDeath(string mark, string[] all = null, string spawn = null, string taunt = null, string deathTaunt = null)
        {
            _deathTaunt = deathTaunt;
            _mark = mark;
            _all = all;
            _spawn = spawn;
            _taunt = taunt;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            if (world == null)
                return;
            world.Marks.Add(_mark);
            if (_deathTaunt != null)
                world.Broadcast(Say(host, _deathTaunt));
            if (_spawn == null || _all == null || !_all.All(world.Marks.Contains) || !world.Marks.Add("spawned " + _spawn))
                return;

            var points = world.GetSpawnPoints();
            if (points.Length == 0)
                return;
            var at = points[Random.Next(points.Length)].Key;
            var entity = Entity.Resolve(host.GameServer, _spawn);
            if (entity == null)
                return;
            entity.Move(at.X + 0.5f, at.Y + 0.5f);
            world.EnterWorld(entity);
            if (_taunt != null)
                world.Broadcast(Say(entity, _taunt));
        }

        private static Text Say(Entity who, string text) =>
            new Text() { Name = "#" + (who.ObjectDesc.DisplayId ?? who.ObjectDesc.IdName), ObjectId = who.Id, NumStars = -1, BubbleTime = 3, Recipient = "", Txt = text };
    }
}
