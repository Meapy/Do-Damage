using System;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.worlds;
using WorldServer.core.worlds.realm2;
using Xunit;

namespace Tests
{
    // Realm 2's lands and the realm events they bring: everything a land names is in the game's
    // data (a missing id only shows as a warning in the log and a gap in the land), and the count to
    // close a Realm 2 leaves some lands out, so each one has a different mix.
    public class RealmEventsTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        [Fact]
        public void EveryLandNamesWhatTheGameHas()
        {
            var data = Res.Value.GameData;
            var missing = Realm2Themes.Events.SelectMany(t =>
                    new[] { t.Event, t.Wall }
                        .Concat(t.Decor.Select(d => d.Id))
                        .Concat(t.Mobs.Select(m => m.Id))
                        .Concat(t.Quests)
                        .Where(id => !data.IdToObjectType.ContainsKey(id))
                        .Concat(new[] { t.Ground, t.Patches, t.Arena, t.Pools }
                            .Where(id => id != null && !data.IdToTileType.ContainsKey(id)))
                        .Select(id => $"{t.Key}: {id}"))
                .ToList();
            Assert.True(missing.Count == 0, "Not in the game data:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void LandsAreUniqueAndSomeAreLeftOut()
        {
            Assert.Equal(Realm2Themes.Events.Length, Realm2Themes.Events.Select(t => t.Key).Distinct().Count());
            Assert.Equal(Realm2Themes.Events.Length, Realm2Themes.Events.Select(t => t.Event).Distinct().Count());
            Assert.InRange(Realm2Themes.LandsToClose, 1, Realm2Themes.Events.Length - 1);
        }

        [Fact]
        public void TheNewEventsAreQuestsOfLevelTwenty()
        {
            var data = Res.Value.GameData;
            foreach (var id in new[] { "Gilded Thief", "Starborn Colossus", "Blight Mother" })
            {
                var desc = data.ObjectDescs[data.IdToObjectType[id]];
                Assert.True(desc.Quest, id);
                Assert.Equal(20, desc.Level);
                Assert.Contains(Realm2Themes.Events, t => t.Event == id);
            }
        }
    }
}
