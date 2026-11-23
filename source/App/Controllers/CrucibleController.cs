using Microsoft.AspNetCore.Mvc;
using Shared.database.leaderboard;
using System;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("crucible")]
    public class CrucibleController : ControllerBase
    {
        private readonly CoreService _core;

        public CrucibleController(CoreService core)
        {
            _core = core;
        }

        /// The best Crucible climbs (DbCrucibleRuns), best first: the Nexus's Crucible Leaderboard
        /// in the Rust client. Each run has the floors it cleared (5: conquered), its time in seconds
        /// and when it ended (unix ms), whether a group began it, then its players (those there
        /// when its last floor fell who never fell). `players` "solo" or "group" gives only
        /// those climbs; without it, all of them.
        [HttpPost("leaderboard")]
        public void Leaderboard([FromForm] string count, [FromForm] string players)
        {
            var n = int.TryParse(count, out var c) ? Math.Clamp(c, 1, 50) : 10;
            var db = _core.Database.Conn;
            var runs = players == "solo" || players == "group" ? DbCrucibleRuns.Top(db, n, players == "solo") : DbCrucibleRuns.Top(db, n);
            var xml = new XElement("CrucibleRuns",
                new XAttribute("floors", Shared.crucible.CrucibleRules.Floors.Count),
                runs.Select(r => new XElement("Run",
                    new XAttribute("floors", r.Floors),
                    new XAttribute("seconds", r.Seconds),
                    new XAttribute("time", r.Time),
                    new XAttribute("group", r.Group ? 1 : 0),
                    r.Players.Select(p => new XElement("Player", p)))));
            Response.CreateXml(xml.ToString());
        }
    }
}
