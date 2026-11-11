using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using WorldServer.logic.loot;

namespace WorldServer.logic
{
    // Dungeons whose behaviours build both the regular and the Elder version (Pirate Cave, Snake
    // Pit, ...) share these. The Elder version uses the Elder monster wherever there is one, attacks
    // a third more often, and adds each monster's Doomed Realms Elder drops to its regular loot.
    partial class BehaviorDb
    {
        // the monster a behaviour refers to
        private static string ElderOr(string id, bool elder) =>
            elder && InitGameData.IdToObjectType.ContainsKey("Elder " + id) ? "Elder " + id : id;

        // the monster a behaviour defines: in the Elder version its Elder monster, or none for a
        // monster with no Elder version (which keeps its regular behaviour as it is) or one listed in
        // keepDoomed (whose Elder version keeps its Doomed Realms behaviour)
        private static string ElderDefines(string id, bool elder, ICollection<string> keepDoomed = null)
        {
            if (!elder)
                return id;
            if (keepDoomed != null && keepDoomed.Contains(id))
                return null;
            var name = ElderOr(id, true);
            return name != id ? name : null;
        }

        private static int ElderCooldown(int ms, bool elder) => elder ? ms * 3 / 4 : ms;

        // the grounds a behaviour changes (the Shatters' bridges and lava): in the Elder version
        // their Elder copies, with the copies a brazier lights ("Elder x Lit 1" on) when lit is set
        private static string[] ElderGrounds(string[] grounds, bool elder, bool lit)
        {
            if (!elder)
                return grounds;
            var tiles = InitGameData.IdToTileType;
            var list = new List<string>();
            foreach (var g in grounds)
            {
                var name = "Elder " + g;
                if (!tiles.ContainsKey(name))
                {
                    list.Add(g);
                    continue;
                }
                list.Add(name);
                for (var n = 1; lit && tiles.ContainsKey($"{name} Lit {n}"); n++)
                    list.Add($"{name} Lit {n}");
            }
            return list.ToArray();
        }

        // the Doomed Realms Elder drops of a monster, in its Elder version
        private static MobDrops ElderDrops(string id, bool elder) => new LootSet(elder ? DoomedDrops("Elder " + id) : new MobDrops[0]);

        private static MobDrops[] DoomedDrops(string id)
        {
            var field = typeof(doomed.DoomedMonsterDefs).GetField("M_" + Regex.Replace(id, @"\W", "_"), BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null) is doomed.Definition def ? doomed.DoomedSetup.Drops(id, def) : new MobDrops[0];
        }
    }
}

namespace WorldServer.logic.loot
{
    // several drop lists as one
    public class LootSet : MobDrops
    {
        public LootSet(params MobDrops[] drops)
        {
            foreach (var d in drops)
                d.Populate(LootDefs);
        }
    }
}
