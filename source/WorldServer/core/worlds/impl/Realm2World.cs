using System;
using System.IO;
using Shared.resources;
using WorldServer.core.worlds.realm2;
using WorldServer.utils;

namespace WorldServer.core.worlds.impl
{
    /// <summary>
    /// Realm 2 (SETUP-LOCAL.md, "Realm 2"): a realm made afresh each time its key opens one, with
    /// an area for every realm event (Realm2Generator). It closes as a realm does, once its events
    /// are defeated (RealmManager with a layout), but the Nexus doesn't list it.
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
            var (tiles, layout) = new Realm2Generator(GameServer.Resources.GameData, seed).Generate();
            Layout = layout;
            RealmManager.Layout = layout;
            FromWorldMap(new MemoryStream(Realm2Generator.Export(tiles)));
            StaticLogger.Instance.Info($"Realm 2 {Id}: seed {seed}, {layout.Width}x{layout.Height}, {string.Join(" > ", layout.Areas.ConvertAll(a => a.Kind))} in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            return true;
        }
    }
}
