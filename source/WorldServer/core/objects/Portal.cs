using System.Collections.Generic;
using Shared;
using Shared.resources;
using WorldServer.core.net.stats;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    public class Portal : StaticObject
    {
        public World WorldInstance;

        /// <summary>Players waiting while its world is being built (UsePortalHandler), or null.</summary>
        public System.Collections.Generic.List<Player> Opening;

        private StatTypeValue<bool> _usable;
        public readonly PortalDesc PortalDescr;
        public bool GuildHallPortal;

        public Portal(GameServer manager, ushort objType) : base(manager, ValidatePortal(manager, objType), null, false, true, false)
        {
            _usable = new StatTypeValue<bool>(this, StatDataType.PortalUsable, true);

            PortalDescr = manager.Resources.GameData.Portals[ObjectType];
            Locked = PortalDescr.Locked;
            
            // any door to the guild's hall: the old portal, or the Nexus garden's Guild Hall door
            GuildHallPortal = PortalDescr.IdName == "Guild Hall Portal" || ObjectDesc.Class == "GuildHallPortal";
        }

        public bool Locked { get; private set; }
        // what a player is told when they try to enter this portal while it is locked
        public string LockedHint { get; set; }
        public bool Usable { get => _usable.GetValue(); set => _usable.SetValue(value); }

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            if(!GuildHallPortal)
                stats[StatDataType.PortalUsable] = Usable ? 1 : 0;

            base.ExportStats(stats, isOtherPlayer);
        }

        private static ushort ValidatePortal(GameServer manager, ushort objType)
        {
            var portals = manager.Resources.GameData.Portals;

            if (!portals.ContainsKey(objType))
            {
                StaticLogger.Instance.Warn($"Portal {objType.To4Hex()} does not exist. Using Portal of Cowardice.");

                objType = 0x0703; // default to Portal of Cowardice
            }

            return objType;
        }
    }
}
