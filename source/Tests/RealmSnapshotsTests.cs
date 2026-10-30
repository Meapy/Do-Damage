using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.realm2;
using Xunit;

namespace Tests
{
    // Realm state across world servers (RealmSnapshots): what's read back, what's thrown away, and
    // that Realm 2's map and lands come out the same from the seeds a snapshot keeps.
    public class RealmSnapshotsTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        [Fact]
        public void ReadsBackWhatItWrites()
        {
            var s = new RealmSnapshot
            {
                Key = "k1", Name = "Zenith", Realm2 = true, At = 1000, By = "a", Events = 7, Seed = 42,
                Running = { new RealmSnapshotEvent { Name = "Cube God", X = 10, Y = 20 } },
                Upcoming = new List<string> { "Pentaract", "Ghost Ship" },
                Lands = new List<RealmSnapshotLand> { new RealmSnapshotLand { Key = "cube", X = 300, Y = 200, Radius = 52, Seed = 9, LinkX = 1, LinkY = 2, Pool = true, Sentry = true } },
                Waystones = new List<RealmSnapshotPoint> { new RealmSnapshotPoint { X = 301, Y = 201 } },
                SinceBeacon = 1, Voiced = true,
            };
            var back = RealmSnapshots.Read(RealmSnapshots.Write(s));
            Assert.Equal(RealmSnapshots.Write(s), RealmSnapshots.Write(back));
            Assert.Equal(7, back.Events);
            Assert.Equal("cube", back.Lands.Single().Key);
            Assert.Equal(2, back.Lands.Single().LinkY);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"Events\":3}")]             // no key
        [InlineData("{\"Key\":\"k\",\"Events\":\"many\"}")]
        public void UnreadableIsNothing(string json) => Assert.Null(RealmSnapshots.Read(json));

        [Fact]
        public void OldSnapshotsAreStale()
        {
            const long now = 1_000_000;
            Assert.True(RealmSnapshots.Fresh(new RealmSnapshot { Key = "k", At = now - 30 }, now));
            Assert.False(RealmSnapshots.Fresh(new RealmSnapshot { Key = "k", At = now - 11 * 60 }, now));
            Assert.False(RealmSnapshots.Fresh(new RealmSnapshot { Key = "k", At = now + 3600 }, now));  // a clock gone wrong
            Assert.False(RealmSnapshots.Fresh(null, now));
        }

        [Fact]
        public void TheClosedMarkIsWhatTheSaveScriptLooksFor()
        {
            // RealmSnapshots' Lua never writes over a field holding this text
            Assert.Contains("\"Closed\":true", RealmSnapshots.Write(new RealmSnapshot { Key = "k", Closed = true }));
        }

        [Fact]
        public void Realm2ComesOutTheSameFromItsSeeds()
        {
            var data = Res.Value.GameData;
            var (a, layoutA) = new Realm2Generator(data, 1234).Generate();
            var (b, layoutB) = new Realm2Generator(data, 1234).Generate();
            Assert.Equal(a.Export(), b.Export());
            Assert.Equal(layoutA.Areas.Count, layoutB.Areas.Count);

            var theme = Realm2Themes.Events.First();
            var at = layoutA.Areas.First(x => x.Kind == Realm2Themes.Godlands).Center;
            byte[] Land()
            {
                var painter = new Realm2EventPainter(data, 77, theme, at, 51);
                painter.Paint((x, y) => layoutA.AreaAt(x, y) != null, null, false);
                return painter.Canvas.Export();
            }
            Assert.Equal(Land(), Land());
        }
    }
}
