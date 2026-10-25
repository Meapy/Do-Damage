using Shared.resources;
using WorldServer.core.objects;
using WorldServer.networking;

namespace WorldServer.core.worlds.impl
{
    public class RealmWorld : World
    {
        public bool Closed { get; private set; }
        public RealmManager RealmManager { get; private set; }

        /// <summary>A realm the Nexus lists, opens portals to and sends Realm Portals to: not Realm 2, which a key opens.</summary>
        public virtual bool ListedInNexus => true;

        public RealmWorld(GameServer gameServer, int id, WorldResource resource) : base(gameServer, id, resource)
        {
            RealmManager = new RealmManager(this);
            IsRealm = true;
        }

        public void SetMaxPlayers(int capacity) => MaxPlayers = capacity;

        public override void Init()
        {
            base.Init();
            RealmManager.Init();
            if (ListedInNexus)
                SetPieces.ApplySetPieces(this);
        }

        public override bool AllowedAccess(Client client) => !Closed || client.Account.Admin;

        protected override void UpdateLogic(ref TickTime time)
        {
            if (ListedInNexus)
            {
                if (IsPlayersMax())
                    GameServer.WorldManager.Nexus.PortalMonitor.ClosePortal(Id);
                else if (!GameServer.WorldManager.Nexus.PortalMonitor.PortalIsOpen(Id))
                    GameServer.WorldManager.Nexus.PortalMonitor.OpenPortal(Id);
            }

            RealmManager.Update(ref time);
            base.UpdateLogic(ref time);

            // players who typed /godlands outside the realm land in the godlands once they're in
            foreach (var player in Players.Values)
                commands.player.GodlandsCommand.Arrived(player, time);
        }

        protected override bool WakesBySight => true;

        public override void AddToWorld(Entity entity)
        {
            base.AddToWorld(entity);
            if (entity is Enemy enemy)
                RealmManager.OnEnemyAdded(enemy);
        }

        public void EnemyKilled(Enemy enemy, Player killer)
        {
            if (!enemy.Spawned)
                RealmManager.OnEnemyKilled(enemy, killer);
        }

        public bool CloseRealm()
        {
            if (Closed)
                return false;
            Closed = true;
            RealmManager.DisableSpawning = true;
            RealmManager.CurrentState = RealmState.Emptying;
            if (ListedInNexus)
                GameServer.WorldManager.Nexus.PortalMonitor.RemovePortal(Id);
            FlagForClose();
            return true;
        }
    }
}
