using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    internal class DropPortalOnDeath : Behavior
    {
        private readonly float _probability;
        private readonly ushort _target;
        private readonly int? _timeout;
        private readonly float xAdjustment;
        private readonly float yAdjustment;
        private readonly string _name;
        private readonly string _lockedHint;

        // name: shown instead of the portal's own (a locked portal says where it leads);
        // lockedHint: what a player trying to enter it while locked is told
        public DropPortalOnDeath(string target, double probability = 1, int? timeout = null, float xAdjustment = 0, float yAdjustment = 0,
            string name = null, string lockedHint = null)
        {
            _target = GetObjType(target);
            _name = name;
            _lockedHint = lockedHint;
            _probability = (float)probability;
            this.xAdjustment = xAdjustment;
            this.yAdjustment = yAdjustment;
            _timeout = timeout; // a value of 0 means never timeout,
            // null means use xml timeout,
            // a value means override xml timeout with that value (in seconds)
        }

        public DropPortalOnDeath(string target, int probability = 1, int dropDelaySec = 0, float XAdjustment = 0, float YAdjustment = 0, int? timeout = null)
        {
            _target = GetObjType(target);
            _probability = probability;

            xAdjustment = XAdjustment;
            yAdjustment = YAdjustment;

            _timeout = timeout;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var owner = host.World;

            if (owner.IdName.Contains("Arena") || host.Spawned)
                return;

            if (Random.NextDouble() < _probability)
            {
                var manager = host.GameServer;
                var gameData = manager.Resources.GameData;
                var timeoutTime = _timeout == null ? gameData.Portals[_target].Timeout : _timeout.Value;
                var entity = Entity.Resolve(manager, _target);
                // beside the boss, so its loot bags don't cover the portal; on the other side when
                // that spot is a wall (a boss that chases players can die beside one)
                var (x, y) = (host.X + xAdjustment, host.Y + yAdjustment);
                if (!owner.IsPassable(x, y) && owner.IsPassable(host.X - xAdjustment, host.Y - yAdjustment))
                    (x, y) = (host.X - xAdjustment, host.Y - yAdjustment);
                entity.Move(x, y);
                if (_name != null)
                    entity.Name = _name;
                if (entity is Portal portal && _lockedHint != null)
                    portal.LockedHint = _lockedHint;
                owner.EnterWorld(entity);

                // in a dungeon the portals a monster drops (the realm portal, an Elder dungeon) stay
                // open; only those in a realm close, or the realm would fill up with them
                if (timeoutTime != 0 && owner.IsRealm)
                    owner.StartNewTimer(timeoutTime * 1000, (world, t) => //default portal close time * 1000
                    {
                        try { world.LeaveWorld(entity); }
                        //couldn't remove portal, Owner became null. Should be fixed with RealmManager implementation
                        catch { Console.WriteLine("Couldn't despawn portal."); }
                    });
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
