using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.database.character;
using Shared.database;
using Shared.database.leaderboard;
using Shared.utils;
using System;
using System.Collections.Concurrent;
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
        /// death's summary (/char/fame). With `saved`, a death saved's instead (/fame/saved).
        [HttpPost("replay")]
        public void Replay([FromForm] string accountId, [FromForm] string charId, [FromForm] string saved)
        {
            byte[] data;
            if (saved != null)
            {
                if (!long.TryParse(saved, out var id))
                {
                    Response.CreateError("Invalid save");
                    return;
                }
                data = Shared.database.DeathReplays.LoadSaved(_core.Database.Conn, id);
            }
            else if (!int.TryParse(accountId, out var acc) || !int.TryParse(charId, out var chr))
            {
                Response.CreateError("Invalid character");
                return;
            }
            else
                data = Shared.database.DeathReplays.Load(_core.Database.Conn, acc, chr);
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

        /// The Scrying Glass's Replays tab: the server's 20 latest deaths and deaths saved
        /// together, newest first, each named as its comments are (ReplayComments: `d.<account>.<char>`,
        /// `s.<save>`), with whether its replay is kept and how many comments it has.
        [HttpPost("feed")]
        public void Feed([FromForm] string count)
        {
            var n = Math.Clamp(int.TryParse(count, out var c) ? c : 20, 1, 30);
            var now = DateTime.UtcNow.ToUnixTimestamp();
            var db = _core.Database.Conn;
            var deaths = DbRecentDeaths.Get(db, DbRecentDeaths.Max).Select(d => (Time: d.Time, Death: d, Save: (DbSavedDeath)null));
            var saves = DbSavedDeaths.Get(db, DbSavedDeaths.Max).Select(d => (Time: d.Time, Death: (DbRecentDeath)null, Save: d));
            var rows = deaths.Concat(saves).OrderByDescending(r => r.Time).Take(n).ToArray();
            var xml = new XElement("Replays", rows.Select(r =>
            {
                var saved = r.Save != null;
                var name = saved ? $"s.{r.Save.Id}" : $"d.{r.Death.AccountId}.{r.Death.CharId}";
                var kept = saved ? r.Save.HasReplay && db.KeyExists(DeathReplays.SavedKey(r.Save.Id)) : db.KeyExists(DeathReplays.Key(r.Death.AccountId, r.Death.CharId));
                return new XElement("Replay",
                    new XAttribute("kind", saved ? "saved" : "death"),
                    new XAttribute("id", name),
                    new XElement("Name", (saved ? r.Save.Name : r.Death.Name) ?? ""),
                    new XElement("ObjectType", saved ? r.Save.ObjectType : r.Death.ObjectType),
                    new XElement("Texture", saved ? r.Save.Skin : r.Death.Skin),
                    new XElement("Tex1", saved ? r.Save.Tex1 : r.Death.Tex1),
                    new XElement("Tex2", saved ? r.Save.Tex2 : r.Death.Tex2),
                    new XElement("Level", saved ? r.Save.Level : r.Death.Level),
                    new XElement("Fame", saved ? r.Save.Fame : r.Death.TotalFame),
                    new XElement("Killer", (saved ? r.Save.Killer : r.Death.Killer) ?? ""),
                    new XElement("World", (saved ? r.Save.World : r.Death.World) ?? ""),
                    new XElement("SaverType", saved ? r.Save.SaverType : 0),
                    new XElement("Saver", (saved ? r.Save.Saver : null) ?? ""),
                    new XElement("Angel", saved && r.Save.Angel),
                    new XElement("HasReplay", kept),
                    new XElement("Comments", ReplayComments.Count(db, name)),
                    new XElement("Ago", Math.Max(0, now - r.Time)));
            }));
            Response.CreateXml(xml.ToString());
        }

        /// A replay's comments, oldest first (ReplayComments), with the time since each.
        [HttpPost("comments")]
        public void Comments([FromForm] string replay)
        {
            if (!ReplayComments.Valid(replay))
            {
                Response.CreateError("Invalid replay");
                return;
            }
            Response.CreateXml(CommentsXml(replay).ToString());
        }

        private XElement CommentsXml(string replay)
        {
            var now = DateTime.UtcNow.ToUnixTimestamp();
            return new XElement("Comments",
                new XAttribute("replay", replay),
                ReplayComments.Get(_core.Database.Conn, replay).Select(e => new XElement("Comment",
                    new XAttribute("accountId", e.AccountId),
                    new XElement("Name", e.Name ?? ""),
                    new XElement("Text", e.Text ?? ""),
                    new XElement("Ago", Math.Max(0, now - e.Time)))));
        }

        // when each account last commented: one comment every few seconds
        private static readonly ConcurrentDictionary<int, long> LastComment = new ConcurrentDictionary<int, long>();
        private const long CommentEveryMs = 5000;

        /// Comment on a replay, signed in (not a guest, not muted): answers with its comments.
        [HttpPost("comment")]
        public void Comment([FromForm] string guid, [FromForm] string password, [FromForm] string replay, [FromForm] string text)
        {
            var status = _core.Database.Verify(guid, password, out var acc);
            if (status != DbLoginStatus.OK)
            {
                Response.CreateError(status.GetInfo());
                return;
            }
            if (acc.Guest)
            {
                Response.CreateError("Make an account to comment");
                return;
            }
            if (acc.IP != null && _core.Database.IsMuted(acc.IP).Result)
            {
                Response.CreateError("You are muted");
                return;
            }
            var line = ReplayComments.Clean(text);
            if (line == null || !ReplayComments.Valid(replay))
            {
                Response.CreateError("Nothing to post");
                return;
            }
            var now = Environment.TickCount64;
            if (LastComment.TryGetValue(acc.AccountId, out var last) && now - last < CommentEveryMs)
            {
                Response.CreateError("Slow down a little");
                return;
            }
            var comment = new ReplayComment { AccountId = acc.AccountId, Name = acc.Name, Text = line, Time = DateTime.UtcNow.ToUnixTimestamp() };
            if (!ReplayComments.Add(_core.Database.Conn, replay, comment))
            {
                Response.CreateError("No replay");
                return;
            }
            LastComment[acc.AccountId] = now;
            _core.Logger.Info($"<{acc.Name}-{acc.AccountId}> commented on replay {replay}: {line}");
            Response.CreateXml(CommentsXml(replay).ToString());
        }

        /// The server's latest deaths saved, newest first (the Legends screen's Deaths Saved):
        /// characters that took a killing blow wearing an amulet that resurrects (or a ring's
        /// Guardian Angel) and lived. `total` is how many there have been.
        [HttpPost("saved")]
        public void Saved([FromForm] string count)
        {
            var n = int.TryParse(count, out var c) ? c : DbSavedDeaths.Max;
            var now = DateTime.UtcNow.ToUnixTimestamp();
            var db = _core.Database.Conn;
            var saves = DbSavedDeaths.Get(db, n);
            var xml = new XElement("SavedDeaths",
                new XAttribute("total", DbSavedDeaths.Count(db)),
                saves.Select(d => new XElement("Save",
                    new XAttribute("id", d.Id),
                    new XAttribute("accountId", d.AccountId),
                    new XAttribute("charId", d.CharId),
                    new XElement("Name", d.Name ?? ""),
                    new XElement("ObjectType", d.ObjectType),
                    new XElement("Texture", d.Skin),
                    new XElement("Tex1", d.Tex1),
                    new XElement("Tex2", d.Tex2),
                    new XElement("Level", d.Level),
                    new XElement("Fame", d.Fame),
                    new XElement("Maxed", d.Maxed),
                    new XElement("Killer", d.Killer ?? ""),
                    new XElement("World", d.World ?? ""),
                    new XElement("SaverType", d.SaverType),
                    new XElement("Saver", d.Saver ?? ""),
                    new XElement("Angel", d.Angel),
                    new XElement("Blow", d.Blow),
                    new XElement("HpBefore", d.HpBefore),
                    new XElement("MaxHp", d.MaxHp),
                    new XElement("Replay", d.HasReplay),
                    new XElement("Time", d.Time),
                    new XElement("Ago", Math.Max(0, now - d.Time))
                )));
            Response.CreateXml(xml.ToString());
        }
    }
}