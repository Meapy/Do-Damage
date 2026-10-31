using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.worlds.impl
{
    public class RealmWorld : World
    {
        public bool Closed { get; private set; }
        public RealmManager RealmManager { get; private set; }

        /// <summary>A realm the Nexus lists, opens portals to and sends Realm Portals to: not Realm 2, which a key opens.</summary>
        public virtual bool ListedInNexus => true;

        /// <summary>
        /// A portal in the Nexus leads here (every listed realm, and the Nexus's own Realm 2), so it
        /// stays up while nobody is in it, until it closes (Realm 2's resource doesn't persist).
        /// </summary>
        public bool HasNexusPortal
        {
            get => _hasNexusPortal;
            set
            {
                _hasNexusPortal = value;
                if (value)
                    Persist = true;
            }
        }
        private bool _hasNexusPortal;

        public RealmWorld(GameServer gameServer, int id, WorldResource resource) : base(gameServer, id, resource)
        {
            RealmManager = new RealmManager(this);
            IsRealm = true;
        }

        public void SetMaxPlayers(int capacity) => MaxPlayers = capacity;

        /// <summary>Set before Init: the snapshot it opens again from (RealmSnapshots), null for a new realm.</summary>
        public RealmSnapshot Restoring;

        public override void Init()
        {
            base.Init();
            RealmManager.Init();
            if (ListedInNexus)
                SetPieces.ApplySetPieces(this);
            // a snapshot that doesn't fit only leaves the realm as far as it got
            if (Restoring != null)
                try
                {
                    RealmManager.SetPiecesPlaced(PendingEntities);
                    RealmManager.Restore(Restoring);
                }
                catch (System.Exception e)
                {
                    WorldServer.utils.StaticLogger.Instance.Error($"{DisplayName}: couldn't open it again from its snapshot: {e}");
                }
            Restoring = null;
        }

        public override bool AllowedAccess(Client client) => !Closed || client.Account.Admin;

        protected override void UpdateLogic(ref TickTime time)
        {
            if (HasNexusPortal)
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
            WeeklyStats.Count(Shared.database.leaderboard.DbWeeklyStats.RealmsClosed);
            RealmManager.SnapshotClosed();
            RealmManager.DisableSpawning = true;
            RealmManager.CurrentState = RealmState.Emptying;
            if (HasNexusPortal)
                GameServer.WorldManager.Nexus.PortalMonitor.RemovePortal(Id);
            FlagForClose();
            return true;
        }
    }
}
