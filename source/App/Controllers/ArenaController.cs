using Microsoft.AspNetCore.Mvc;
using Shared.database.leaderboard;
using System;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("arena")]
    public class ArenaController : ControllerBase
    {
        private readonly CoreService _core;

        public ArenaController(CoreService core)
        {
            _core = core;
        }

        /// The best arena runs of a season (DbArenaRuns), best first: the Nexus's Arena Leaderboard
        /// in the Rust client. `season` picks one (ArenaSeasons; the current one without it, as older
        /// clients ask). The answer says which season it is and the current one, and lists every
        /// season (its number, name, and when it began and ended, unix ms, 0 for unknown or not
        /// yet), then the runs. Seconds is -1 for runs from before times were kept; from Season 2
        /// on a run also has the Empowered bosses it slew and the round it ended in. `players`
        /// "solo" or "group" gives only those runs; without it (older clients), all of them.
        [HttpPost("leaderboard")]
        public void Leaderboard([FromForm] string count, [FromForm] string players, [FromForm] string season)
        {
            var n = int.TryParse(count, out var c) ? Math.Clamp(c, 1, 50) : 10;
            var db = _core.Database.Conn;
            var picked = ArenaSeasons.Parse(season);
            var runs = players == "solo" || players == "group" ? DbArenaRuns.Top(db, picked, n, players == "solo") : DbArenaRuns.Top(db, picked, n);
            var spans = ArenaSeasons.Spans(db);
            var xml = new XElement("ArenaRuns",
                new XAttribute("season", picked.Number),
                new XAttribute("current", ArenaSeasons.Current.Number),
                ArenaSeasons.All.Select((s, i) => new XElement("Season",
                    new XAttribute("number", s.Number),
                    new XAttribute("name", s.Name),
                    new XAttribute("start", spans[i].Start),
                    new XAttribute("end", spans[i].End))),
                runs.Select(r => new XElement("Run",
                    new XAttribute("wave", r.Wave),
                    new XAttribute("seconds", r.Seconds),
                    new XAttribute("time", r.Time),
                    new XAttribute("arena", r.Arena ?? ""),
                    new XAttribute("empowered", r.Empowered),
                    new XAttribute("fell", r.FellTo ?? ""),
                    r.Players.Select(p => new XElement("Player", p)))));
            Response.CreateXml(xml.ToString());
        }
    }
}
