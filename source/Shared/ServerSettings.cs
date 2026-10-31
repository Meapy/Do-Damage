using System;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

namespace Shared
{
    public class ServerSettings
    {
        public bool supporterOnly { get; set; } = false;
        public double lootEvent { get; set; } = 0; // .5 gives 50%
        public double expEvent { get; set; } = 0;
        public double wkndBoost { get; set; } = 0.30;
        // A damage event: the enemies named (object ids) deal this much less damage, .2 is 20%
        // (WorldServer/core/DamageEvent.cs). 0: none.
        public double damageEvent { get; set; } = 0;
        public string[] damageEventEnemies { get; set; } = new string[] { };
        public string logFolder { get; set; } = "undefined";
        public int maxConnections { get; set; } = 0;
        public int maxPlayers { get; set; } = 0;
        public int maxRealms { get; set; } = 1;
        public string resourceFolder { get; set; } = "undefined";
        // Where the published game client is (tools/build_client_rs.py --publish), served at
        // /client/ for players' clients to update from. Empty: dist/client in the checkout the
        // server runs from.
        public string clientFolder { get; set; } = "";
        public int restartTime { get; set; } = 0;
        // The first world id is worldIdBase + 1. Each world server behind the proxy gets its own
        // range (1000000 for slot a, 2000000 for b), so the proxy knows which one has a world.
        public int worldIdBase { get; set; } = 0;
        public string version { get; set; } = "undefined";
        // An event's maps, such as "Halloween": a world whose map is "<map>.jm" plays
        // "<map> <mapEvent>.jm" instead where there is one (tools/build_nexus.py and the other map builders; none has event copies now). Empty: none.
        public string mapEvent { get; set; } = "";
        // The free guilds event: making a guild costs nothing until this moment, in UTC
        // ("2026-12-01T00:00:00Z"; Shared/database/guild/GuildPrice.cs). Empty: none.
        public string freeGuildsUntil { get; set; } = "";
        // Timed events, each with its name, start, end and effects (Shared/resources/ServerEvents.cs),
        // such as Halloween Sweet Treats' doubled potion drops. Each runs only between its dates.
        public resources.ServerEvent[] events { get; set; } = new resources.ServerEvent[] { };
        public int[] whitelist { get; set; } = new int[] { };
        // A player who stands still in the Nexus this long is saved and sent back to the
        // character list (Shared/NexusAfk.cs); admins are exempt. 0: never.
        public double nexusAfkMinutes { get; set; } = 20;
    }
}
