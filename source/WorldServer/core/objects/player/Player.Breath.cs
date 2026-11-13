using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core.worlds;

namespace WorldServer.core.objects
{
    // Breath underwater (RealmEye, Ocean Trench and Cnidarian Reef): in these dungeons a player's
    // air runs down; standing on a bubbling Ocean Vent fills it again, and once it's gone they lose
    // 100 HP a second, defence or not, and die of suffocation. The Reef drains it fastest. The air,
    // 0-100, goes to the client as OxygenBar (-1 out of the water), which the Rust client draws
    // under the player's HP bar.
    public partial class Player
    {
        // how long a full breath lasts in each underwater dungeon, in seconds
        private static readonly Dictionary<string, double> Underwater = new Dictionary<string, double>
        {
            ["Ocean Trench"] = 16,
            ["Elder Ocean Trench"] = 14,
            ["Cnidarian Reef"] = 9,
        };

        // a vent fills the air in two seconds; suffocating costs 10 HP every 100 ms
        private const double RefillPerSecond = 50;
        private const int SuffocationDamage = 10;
        private const int SuffocationEveryMs = 100;
        private const float VentReach = 1f;

        // each world's vents, found once (they're part of its map and never move)
        private static readonly ConditionalWeakTable<World, List<(float X, float Y)>> Vents = new ConditionalWeakTable<World, List<(float X, float Y)>>();

        private int _suffocationMs;

        public static double AirSeconds(World world) =>
            world != null && Underwater.TryGetValue(world.IdName, out var s) ? s : 0;

        private static List<(float X, float Y)> VentsOf(World world)
        {
            if (Vents.TryGetValue(world, out var found) && found.Count > 0)
                return found;
            var vents = world.StaticObjects.Values.Cast<Entity>().Concat(world.Enemies.Values)
                .Where(e => e.ObjectDesc?.IdName == "Ocean Vent")
                .Select(e => (e.X, e.Y)).ToList();
            Vents.AddOrUpdate(world, vents);
            return vents;
        }

        private void HandleOxygen(TickTime time)
        {
            var air = AirSeconds(World);
            // held while paused or invincible (staff's god mode), as ground damage is
            if (air <= 0 || Dead || HasConditionEffect(ConditionEffectIndex.Paused) || HasConditionEffect(ConditionEffectIndex.Invincible))
                return;
            var seconds = time.ElapsedMsDelta / 1000.0;
            var onVent = VentsOf(World).Any(v => Math.Abs(v.X - X) < VentReach && Math.Abs(v.Y - Y) < VentReach
                && (v.X - X) * (v.X - X) + (v.Y - Y) * (v.Y - Y) < VentReach * VentReach);
            if (onVent)
            {
                _suffocationMs = 0;
                Breath = Math.Min(100, Math.Max(0, Breath) + RefillPerSecond * seconds);
                return;
            }
            if (Breath > 0)
            {
                Breath = Math.Max(0, Breath - 100 / air * seconds);
                return;
            }
            _suffocationMs += time.ElapsedMsDelta;
            while (_suffocationMs >= SuffocationEveryMs && !Dead)
            {
                _suffocationMs -= SuffocationEveryMs;
                Health -= SuffocationDamage;
                if (Health <= 0)
                    Death("suffocation");
            }
        }
    }
}
