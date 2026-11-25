using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.database.character;
using System;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("fame")]
    public class FameController : ControllerBase
    {
        private readonly CoreService _core;

        public FameController(CoreService core)
        {
            _core = core;
        }

        [HttpPost("list")]
        public void List([FromForm] string accountId, [FromForm] string charId, [FromForm] string timespan)
        {
            DbChar character = null;
            if (accountId != null)
                character = _core.Database.LoadCharacter(int.Parse(accountId), int.Parse(charId));

            // todo figure this one out to use the actual query stuff isntead of timespan only
            var list = FameList.FromDb(_core, timespan);
            Response.CreateXml(list.ToXml().ToString());
        }

        /// A death's replay (Shared/database/DeathReplays.cs), base64 of the world server's gzipped
        /// recording, for the client's replay viewer; anyone may watch one, as anyone may read a
        /// death's summary (/char/fame).
        [HttpPost("replay")]
        public void Replay([FromForm] string accountId, [FromForm] string charId)
        {
            if (!int.TryParse(accountId, out var acc) || !int.TryParse(charId, out var chr))
            {
                Response.CreateError("Invalid character");
                return;
            }
            var data = Shared.database.DeathReplays.Load(_core.Database.Conn, acc, chr);
            if (data == null)
            {
                Response.CreateError("No replay");
                return;
            }
            Response.CreateXml(new XElement("Replay", Convert.ToBase64String(data)).ToString());
        }

        /// The server's latest deaths, newest first (the Legends screen's Recent Deaths).
        [HttpPost("recent")]
        public void Recent([FromForm] string count)
        {
            var n = int.TryParse(count, out var c) ? c : 20;
            var now = DateTime.UtcNow.ToUnixTimestamp();
            var deaths = _core.Database.GetRecentDeaths(n);
            var xml = new XElement("RecentDeaths",
                deaths.Select(d => new XElement("Death",
                    new XAttribute("accountId", d.AccountId),
                    new XAttribute("charId", d.CharId),
                    new XElement("Name", d.Name ?? ""),
                    new XElement("ObjectType", d.ObjectType),
                    new XElement("Texture", d.Skin),
                    new XElement("Tex1", d.Tex1),
                    new XElement("Tex2", d.Tex2),
                    new XElement("Level", d.Level),
                    new XElement("TotalFame", d.TotalFame),
                    new XElement("Killer", d.Killer ?? ""),
                    new XElement("World", d.World ?? ""),
                    new XElement("Time", d.Time),
                    new XElement("Ago", Math.Max(0, now - d.Time))
                )));
            Response.CreateXml(xml.ToString());
        }
    }
}