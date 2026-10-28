using System;
using System.IO;
using Shared.resources;
using WorldServer.core.structures;
using WorldServer.core.worlds.realm2;
using WorldServer.utils;

namespace WorldServer.core.worlds.impl
{
    /// <summary>
    /// Realm 2 (SETUP-LOCAL.md, "Realm 2"): a realm made afresh each time its key opens one, with
    /// the Godlands, and a land for each realm event drawn into it when the event comes
    /// (Realm2Generator, Realm2EventPainter). It closes as a realm does, once its events are
    /// defeated (RealmManager with a layout). The Nexus keeps one open behind a portal beside the
    /// realms' (RealmPortalMonitor), but doesn't list it among them (no Realm Portals lead here).
    /// </summary>
    public sealed class Realm2World : RealmWorld
    {
        public Realm2Layout Layout { get; private set; }

        public override bool ListedInNexus => false;

        public Realm2World(GameServer gameServer, int id, WorldResource resource) : base(gameServer, id, resource)
        {
        }

        public override bool LoadMapFromData(WorldResource worldResource)
        {
            var seed = Random.Shared.Next();
            var started = DateTime.UtcNow;
            var (canvas, layout) = new Realm2Generator(GameServer.Resources.GameData, seed).Generate();
            Layout = layout;
            RealmManager.Layout = layout;
            FromWorldMap(new MemoryStream(canvas.Export()));
            StaticLogger.Instance.Info($"Realm 2 {Id}: seed {seed}, {layout.Width}x{layout.Height}, the Godlands {layout.Godlands.Tiles} tiles, in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            return true;
        }

        // beside the newest waystone once the realm has one, so returning players needn't walk the
        // whole way out again; on the shore before that
        public override Position GetPlayerSpawnPosition() => RealmManager.ArrivalPoint() ?? base.GetPlayerSpawnPosition();
    }
}
