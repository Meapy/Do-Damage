using Shared;
using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;

namespace WorldServer.core
{
    // A server-wide event that makes some enemies hit softer (serverSettings.damageEvent and
    // damageEventEnemies): their shots, bombs and grenades deal that much less damage, and so do
    // those of everything they summon. The Nexus tells players when it's on.
    public sealed class DamageEvent
    {
        private readonly HashSet<ushort> Enemies = new HashSet<ushort>();
        private readonly double Multiplier = 1;

        public string Message { get; }

        public DamageEvent(ServerSettings settings, XmlData gameData)
        {
            if (settings.damageEvent <= 0 || settings.damageEventEnemies == null)
                return;

            var names = new List<string>();
            foreach (var id in settings.damageEventEnemies)
            {
                if (!gameData.IdToObjectType.TryGetValue(id, out var type))
                {
                    Console.WriteLine($"[DamageEvent] No enemy called \"{id}\"");
                    continue;
                }
                Enemies.Add(type);
                var desc = gameData.ObjectDescs[type];
                names.Add(desc.DisplayId ?? desc.IdName);
            }
            if (Enemies.Count == 0)
                return;

            Multiplier = 1 - Math.Min(settings.damageEvent, 1);
            var who = names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
            Message = $"The {who} event is active: their damage is {Math.Round(settings.damageEvent * 100, 0)}% lower.";
        }

        public bool Active => Enemies.Count > 0;

        // Whether the event names enemies of this type.
        public bool Names(ushort enemyType) => Enemies.Contains(enemyType);

        // The damage this enemy deals while the event is on: one it names, or anything their
        // behaviours brought in (Entity.DamageEventSummon); not the same monsters placed by the map.
        public int Apply(Entity host, int damage) =>
            host != null && (host.DamageEventSummon || Names(host.ObjectType)) ? (int)Math.Round(damage * Multiplier) : damage;
    }
}
