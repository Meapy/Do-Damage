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
        /// client. Seconds is -1 for runs from before times were kept.
        [HttpPost("leaderboard")]
        public void Leaderboard([FromForm] string count)
        {
            var n = int.TryParse(count, out var c) ? Math.Clamp(c, 1, 50) : 10;
            var runs = DbArenaRuns.Top(_core.Database.Conn, n);
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
