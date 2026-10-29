using Microsoft.AspNetCore.Mvc;
using Shared.champions;
using Shared.quests;
using System;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("champions")]
    public class ChampionsController : ControllerBase
    {
        private readonly CoreService _core;

        public ChampionsController(CoreService core)
        {
            _core = core;
        }

        /// The Hall of Champions (ChampionsService's latest ranking, from memory): each board's top
        /// 10 with their characters, its rewards, and the named player's own place on it even off
        /// the list. The Nexus's Champions Monument and its statues in the Rust client.
        [HttpPost("board")]
        public void Board([FromForm] string name)
        {
            var s = _core.Champions.Current;
            var now = QuestPeriods.Unix(DateTime.UtcNow);
            if (s == null)
            {
                Response.CreateXml(new XElement("Champions", new XAttribute("pending", 1), new XAttribute("now", now)).ToString());
                return;
            }
            var xml = new XElement("Champions",
                new XAttribute("now", now),
                new XAttribute("updated", QuestPeriods.Unix(s.Updated)),
                new XAttribute("next", QuestPeriods.Unix(_core.Champions.Next)),
                new XAttribute("weekReset", QuestPeriods.Unix(s.WeekReset)),
                new XAttribute("lootCap", ChampionBoards.LootCap),
                ChampionBoards.All.Select(def =>
                {
                    var ranked = s.Boards[(int)def.Tab];
                    var board = new XElement("Board",
                        new XAttribute("id", def.Id),
                        new XAttribute("name", def.Name),
                        new XAttribute("weekly", def.Weekly ? 1 : 0),
                        new XAttribute("unit", def.Unit),
                        new XAttribute("count", ranked.Count),
                        new XElement("Description", def.Description),
                        Enumerable.Range(0, 3).Select(t => new XElement("Reward",
                            new XAttribute("places", ChampionBoards.TierPlaces(t)),
                            new XAttribute("color", def.Reward is ChampionReward.Glow or ChampionReward.NameColor ? (int)def.ValueAt(t) : 0),
                            def.RewardText(t))),
                        ranked.Take(ChampionBoards.Places).Select(Entry));
                    var mine = string.IsNullOrEmpty(name) ? null : ranked.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (mine != null)
                        board.Add(new XElement("Me", new XAttribute("rank", mine.Rank), new XAttribute("value", mine.Value)));
                    return board;
                }));
            Response.CreateXml(xml.ToString());
        }

        private static XElement Entry(ChampionRanked r) => new XElement("Entry",
            new XAttribute("rank", r.Rank),
            new XAttribute("name", r.Name ?? ""),
            new XAttribute("value", r.Value),
            new XAttribute("objectType", r.ObjectType),
            new XAttribute("skin", r.Skin),
            new XAttribute("tex1", r.Tex1),
            new XAttribute("tex2", r.Tex2),
            new XAttribute("level", r.Level));
    }
}
