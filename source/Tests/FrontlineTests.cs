using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Shared.resources;
using WorldServer.core.objects;
using Xunit;

namespace Tests
{
    public class FrontlineTests
    {
        private static string Xml(string dir, string file) => Path.Combine(AppContext.BaseDirectory, "resources", "xml", dir, file);

        [Fact]
        public void TheCutTakesFivePercentRounded()
        {
            Assert.Equal(95, Player.FrontlineCut(100));
            Assert.Equal(152, Player.FrontlineCut(160)); // 8 off
            Assert.Equal(9, Player.FrontlineCut(10));    // 0.5 off rounds to 1
            Assert.Equal(0, Player.FrontlineCut(0));
            Assert.Equal(1, Player.FrontlineCut(1));
        }

        [Fact]
        public void LeechPaysOnceASecond()
        {
            var l = new FrontlineLeech();
            Assert.Equal(15, l.Add(1000, 0));  // 1.5% of 1,000
            Assert.Equal(0, l.Add(1000, 500)); // owed, not yet
            Assert.Equal(15, l.Add(0, 1000));  // paid on the next hit a second on
        }

        [Fact]
        public void LeechNeverPaysMoreThanFortyASecond()
        {
            var l = new FrontlineLeech();
            var healed = 0;
            // a hit of 4,000 every 100 ms for ten seconds: 600 a second owed
            for (var t = 0; t <= 10_000; t += 100)
                healed += l.Add(4000, t);
            Assert.InRange(healed, 400, 440);
        }

        [Fact]
        public void SmallHitsAddUp()
        {
            var l = new FrontlineLeech();
            var healed = 0;
            // 100 damage every 100 ms: 15 a second
            for (var t = 0; t <= 10_000; t += 100)
                healed += l.Add(100, t);
            Assert.InRange(healed, 140, 155);
        }

        [Fact]
        public void TheSwordClassesAreFrontline()
        {
            var players = XElement.Load(Xml("prod", "EmbeddedData_PlayersCXML.xml")).Elements("Object")
                .Concat(XElement.Load(Xml("custom", "DoomedRealms.xml")).Elements("Object"))
                .Where(o => o.Element("Player") != null)
                .Where(o => o.Element("SlotTypes").Value.Split(',')[0].Trim() == Player.SwordSlotType.ToString())
                .Select(o => o.Attribute("id").Value)
                .OrderBy(i => i)
                .ToArray();
            Assert.Equal(new[] { "Knight", "Paladin", "Pirate (Doomed)", "Warrior" }, players);
        }
    }
}
