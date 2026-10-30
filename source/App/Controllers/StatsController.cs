using Microsoft.AspNetCore.Mvc;
using Shared.database.leaderboard;
using Shared.quests;
using System;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("stats")]
    public class StatsController : ControllerBase
    {
        private readonly CoreService _core;

        public StatsController(CoreService core)
        {
            _core = core;
        }

        /// The server's week so far (DbWeeklyStats): the Herald's window in the Nexus, in the Rust
        /// client. Each count, the monsters killed most (by type, most first), when the week began
        /// and when it starts afresh (unix seconds).
        [HttpPost("weekly")]
        public void Weekly()
        {
            var now = DateTime.UtcNow;
            var (counts, top) = DbWeeklyStats.Read(_core.Database.Conn, 5);
            var xml = new XElement("WeeklyStats",
                new XAttribute("now", QuestPeriods.Unix(now)),
                new XAttribute("start", QuestPeriods.Unix(DbWeeklyStats.WeekStart(now))),
                new XAttribute("reset", QuestPeriods.Unix(QuestPeriods.NextReset(QuestPeriod.Weekly, now))),
                DbWeeklyStats.All.Select(s => new XElement("Stat", new XAttribute("id", s), new XAttribute("value", counts[s]))),
                top.Select(k => new XElement("Killed", new XAttribute("type", k.Type), new XAttribute("count", k.Kills))));
            Response.CreateXml(xml.ToString());
        }
    }
}
