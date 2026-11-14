using System.Linq;
using Newtonsoft.Json;
using Shared;
using Shared.isc.data;
using Xunit;

namespace Tests
{
    // Visits between world servers (WorldServer RemoteVisits): what goes across Redis.
    public class CrossServerVisitTests
    {
        [Fact]
        public void PlayerListsComeAcrossInTheServersPings()
        {
            var info = new ServerInfo { name = "Europe 2", type = ServerType.World, crossVisits = true };
            info.playerList.Add(new PlayerInfo { AccountId = 7, Name = "Bob", WorldInstance = 1000012, WorldName = "Snake Pit", WorldDisplay = "Snake Pit" });
            var back = JsonConvert.DeserializeObject<ServerInfo>(JsonConvert.SerializeObject(info));
            var bob = back.playerList.Cast<PlayerInfo>().Single();
            Assert.Equal(7, bob.AccountId);
            Assert.Equal(1000012, bob.WorldInstance);
            Assert.Equal("Snake Pit", bob.WorldDisplay);
            Assert.True(back.crossVisits);
        }

        [Fact]
        public void AnOlderServerDoesNotTakeVisits()
        {
            var back = JsonConvert.DeserializeObject<ServerInfo>("{\"name\":\"Europe 2\",\"type\":1,\"playerList\":[]}");
            Assert.False(back.crossVisits);
        }

        [Fact]
        public void VisitMessagesComeAcrossWhole()
        {
            var m = new VisitMsg { Kind = VisitMsgKind.Go, Mover = 3, WorldId = 2000005, WorldName = "Abyss of Demons", Key = "AAEC", RequestId = 9, Target = 4 };
            var back = JsonConvert.DeserializeObject<VisitMsg>(JsonConvert.SerializeObject(m));
            Assert.Equal(VisitMsgKind.Go, back.Kind);
            Assert.Equal(2000005, back.WorldId);
            Assert.Equal("AAEC", back.Key);
            Assert.Equal(9, back.RequestId);
        }

        [Fact]
        public void TheAskersSideWaitsLongerForAnAnswerFromAnotherServer()
        {
            var book = new VisitBook();
            Assert.Null(book.Ask(1, "V1", 2, "T2", 0, 0, out var r, graceMs: 3000));
            Assert.Empty(book.TakeTimedOut(1, VisitBook.TimeoutMs + 1000));
            Assert.Same(r, book.Answer(2, r.Id, true, VisitBook.TimeoutMs + 2000));
        }
    }
}
