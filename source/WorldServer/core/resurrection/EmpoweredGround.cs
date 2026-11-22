using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// A bigger arena for the Empowered Forgotten King. His island in the Elder Shatters is ringed by
    /// magma and walls, and his lava traps eat the floor as he weakens; raised, he throws the ring
    /// open instead: the floor spreads out from him in waves to both sides (the magma and the broken
    /// walls become the same cobblestone as the island, so there is no seam) and his lava traps
    /// are gone, so the room stays as big as it was made.
    /// </summary>
    internal static class EmpoweredGround
    {
        private const string King = "Elder shtrs The Forgotten King";
        private const string Magma = "Elder Hot Lava";
        private const string Floor = "Elder Dark Cobblestone";
        private const string Walls = "Elder shtrs Shattered Walls";

        // tiles each way from the King: far to both sides, a little north (the walls of his entrance
        // stop it) and south down the bridge
        private const int Sideways = 30, North = 9, South = 19;
        // the floor takes this long to reach the edge: about the time he spends rising
        private const int SpreadMs = 2200, Waves = 11;

        public static void Widen(Enemy boss, BossInfo info)
        {
            // the Marble Colossus's hall is built again bigger (ColossusArena)
            if (ColossusArena.Grows(info) && boss.World != null)
            {
                ColossusArena.Grow(boss);
                return;
            }
            if (info?.Boss != King || boss.World == null)
                return;
            var world = boss.World;
            var data = world.GameServer.Resources.GameData;
            if (!data.IdToTileType.TryGetValue(Floor, out var floor))
                return;
            var magma = new HashSet<ushort>();
            foreach (var kv in data.IdToTileType)
                if (kv.Key == Magma || kv.Key.StartsWith(Magma + " Lit"))
                    magma.Add(kv.Value);
            data.IdToObjectType.TryGetValue(Walls, out var wallType);

            // his lava traps: the arena no longer shrinks
            foreach (var trap in world.Enemies.Values.Where(e => e.ObjectDesc?.IdName is "Elder shtrs king lava1" or "Elder shtrs king lava2").ToList())
                world.LeaveWorld(trap);

            var map = world.Map;
            float cx = boss.X, cy = boss.Y;
            var waves = new List<(int X, int Y)>[Waves];
            for (var i = 0; i < Waves; i++)
                waves[i] = new List<(int X, int Y)>();
            var reach = Math.Sqrt(Sideways * Sideways + South * South);
            for (var y = Math.Max(1, (int)cy - North); y <= (int)cy + South; y++)
                for (var x = (int)cx - Sideways; x <= (int)cx + Sideways; x++)
                {
                    if (!map.Contains(x, y))
                        continue;
                    var tile = map[x, y];
                    var wall = wallType != 0 && tile.ObjType == wallType;
                    if (!wall && (tile.ObjType != 0 || !magma.Contains(tile.TileId)))
                        continue;
                    var wave = (int)(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / reach * Waves);
                    waves[Math.Min(Waves - 1, wave)].Add((x, y));
                }

            // (no Earthquake: clients before 2.5.6 shake for good until the world changes)
            for (var i = 1; i <= 3; i++)
                world.StartNewTimer(i * SpreadMs / 4, (w, t) =>
                    BossResurrection.Effect(boss, EffectType.AreaBlast, 0xFFFFD34F, new Position(Sideways * 0.6f, 0)));
            for (var i = 0; i < Waves; i++)
            {
                var tiles = waves[i];
                if (tiles.Count == 0)
                    continue;
                world.StartNewTimer(i * SpreadMs / Waves, (w, t) =>
                {
                    foreach (var (x, y) in tiles)
                    {
                        var tile = w.Map[x, y];
                        tile.TileId = floor;
                        if (tile.ObjType != 0)
                            tile.ObjType = 0;
                        if (tile.ObjId == 0)
                            tile.ObjId = w.GetNextEntityId();
                        tile.UpdateCount++;
                    }
                    // sent now, not as each player next steps onto another square
                    foreach (var p in w.Players.Values)
                        p.UpdateTiles();
                });
            }
        }
    }
}
