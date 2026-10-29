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

        /// The best arena runs (DbArenaRuns), best first: the Nexus's Arena Leaderboard in the Rust
        /// client. Seconds is -1 for runs from before times were kept. `players` "solo" or "group"
        /// gives only those runs; without it (older clients), all of them.
        [HttpPost("leaderboard")]
        public void Leaderboard([FromForm] string count, [FromForm] string players)
        {
            var n = int.TryParse(count, out var c) ? Math.Clamp(c, 1, 50) : 10;
            var db = _core.Database.Conn;
            var runs = players == "solo" || players == "group" ? DbArenaRuns.Top(db, n, players == "solo") : DbArenaRuns.Top(db, n);
            var xml = new XElement("ArenaRuns",
                runs.Select(r => new XElement("Run",
                    new XAttribute("wave", r.Wave),
                    new XAttribute("seconds", r.Seconds),
                    new XAttribute("time", r.Time),
                    new XAttribute("arena", r.Arena ?? ""),
                    r.Players.Select(p => new XElement("Player", p)))));
            Response.CreateXml(xml.ToString());
        }
    }
}
