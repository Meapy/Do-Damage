using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Shared;
using Shared.database;
using Shared.database.account;
using Shared.utils;

namespace App.Controllers
{
    /// <summary>
    /// The Sprite Editor (the client's main menu): players send in sprites they drew, a new look
    /// for an item or the art for a new one, and see what became of them; admins review them
    /// (queue, review). The approved ones are public (approved), for tools/community_sprites.py to
    /// take into the repository on the release branch. Storage: Shared.database.SpriteSubmissions.
    /// </summary>
    [ApiController]
    [Route("sprites")]
    // A sprite is at most 2 KB of text: nothing sent here needs more than a few, so anything
    // bigger is refused before it's read.
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = MaxRequestBytes, KeyLengthLimit = 64, ValueCountLimit = 16)]
    public class SpritesController : ControllerBase
    {
        private const int MaxRequestBytes = 16 * 1024;

        /// <summary>The public list, kept for a little while so it can't be used to keep Redis busy.</summary>
        private static readonly TimeSpan ApprovedCacheFor = TimeSpan.FromSeconds(30);
        private static readonly object ApprovedLock = new object();
        private static string _approvedXml;
        private static DateTime _approvedAt;

        private readonly CoreService _core;

        public SpritesController(CoreService core)
        {
            _core = core;
        }

        private DbAccount SignedIn(string guid, string password)
        {
            var status = _core.Database.Verify(guid, password, out var acc);
            if (status != DbLoginStatus.OK)
            {
                Response.CreateError(status.GetInfo());
                return null;
            }
            return acc;
        }

        /// <summary>The player's address: behind this machine's own proxy (the gateway, Caddy), the one it forwarded.</summary>
        private string ClientIp()
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            var forwarded = Request.Headers["X-Forwarded-For"].ToString();
            if (ip != null && System.Net.IPAddress.IsLoopback(ip) && !string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();
            return ip?.ToString() ?? "";
        }

        private DbAccount Admin(string guid, string password)
        {
            var acc = SignedIn(guid, password);
            if (acc == null)
                return null;
            if (!SpriteSubmissions.IsAdmin(acc))
            {
                Response.CreateError("Only admins can review sprites.");
                return null;
            }
            return acc;
        }

        /// <summary>A submission as the client and the tool read it. pixels: with its sprite.</summary>
        private static XElement Element(SpriteSubmissions.Submission s, bool pixels = true) =>
            new XElement("Sprite",
                new XAttribute("id", s.Id),
                new XElement("Status", s.Status),
                new XElement("Kind", s.Kind),
                new XElement("Item", s.Item),
                new XElement("Name", s.Name),
                new XElement("Slot", s.Slot),
                new XElement("Note", s.Note),
                new XElement("Author", s.Author),
                new XElement("Size", s.Size),
                pixels ? new XElement("Pixels", s.Pixels) : null,
                new XElement("Review", s.Review),
                new XElement("ReviewedBy", s.ReviewedBy),
                new XElement("Created", s.Created),
                new XElement("Reviewed", s.Reviewed));

        /// <summary>The account's own submissions, newest first, and whether it may review.</summary>
        [HttpPost("mine")]
        public void Mine([FromForm] string guid, [FromForm] string password)
        {
            var acc = SignedIn(guid, password);
            if (acc == null)
                return;
            var list = SpriteSubmissions.Mine(_core.Database.Conn, acc.AccountId);
            Response.CreateXml(new XElement("Sprites",
                new XAttribute("admin", SpriteSubmissions.IsAdmin(acc)),
                new XAttribute("maxPending", SpriteSubmissions.MaxPendingPerAccount),
                list.Select(s => Element(s))).ToString());
        }

        /// <summary>
        /// A sprite sent in: for the item named item, or (item empty) for a new item named name,
        /// of kind slot. size is 8 or 16; pixels its size × size pixels as RRGGBBAA hex, row by row.
        /// </summary>
        [HttpPost("submit")]
        public void Submit([FromForm] string guid, [FromForm] string password, [FromForm] string item, [FromForm] string name,
            [FromForm] string slot, [FromForm] string note, [FromForm] int size, [FromForm] string pixels)
        {
            var acc = SignedIn(guid, password);
            if (acc == null)
                return;
            if (_core.Database.IsIpBanned(ClientIp()))
            {
                Response.CreateError("This account can't send in sprites.");
                return;
            }
            string itemId = null;
            if (!string.IsNullOrWhiteSpace(item) && item.Length <= 128)
            {
                var data = _core.Resources.GameData;
                if (!data.IdToObjectType.TryGetValue(item.Trim(), out var type) || !data.Items.ContainsKey(type))
                {
                    Response.CreateError("There's no such item.");
                    return;
                }
                itemId = data.ObjectTypeToId[type];
            }
            var status = SpriteSubmissions.Submit(_core.Database.Conn, acc, ClientIp(), itemId, name, slot, note, size, pixels, out var made);
            switch (status)
            {
                case SpriteSubmissions.SubmitStatus.OK:
                    _core.Logger.Info($"Sprite {made.Id} sent in by <{acc.Name}-{acc.AccountId}>: {(made.Kind == SpriteSubmissions.KindItem ? made.Item : "new item " + made.Name)}");
                    Response.CreateXml(Element(made).ToString());
                    return;
                case SpriteSubmissions.SubmitStatus.Guest:
                    Response.CreateError("Register first: a guest can't send in sprites.");
                    return;
                case SpriteSubmissions.SubmitStatus.BadSprite:
                    Response.CreateError("Draw something first.");
                    return;
                case SpriteSubmissions.SubmitStatus.NoName:
                    Response.CreateError("Give the new item a name.");
                    return;
                case SpriteSubmissions.SubmitStatus.BadSlot:
                    Response.CreateError("Pick what kind of item it is.");
                    return;
                case SpriteSubmissions.SubmitStatus.TooSoon:
                    Response.CreateError("Wait a few seconds before sending another.");
                    return;
                case SpriteSubmissions.SubmitStatus.Banned:
                    Response.CreateError("This account can't send in sprites.");
                    return;
                case SpriteSubmissions.SubmitStatus.TooMany:
                    _core.Logger.Warn($"Sprite refused for <{acc.Name}-{acc.AccountId}> from {ClientIp().ForLog()}: too many sent lately");
                    Response.CreateError("You've sent in a lot of sprites lately. Try again tomorrow.");
                    return;
                case SpriteSubmissions.SubmitStatus.TooManyPending:
                    Response.CreateError("Too many of your sprites are waiting for review already.");
                    return;
                default:
                    Response.CreateError("Too many sprites are waiting for review. Try again later.");
                    return;
            }
        }

        /// <summary>Takes back one of the account's own that's still waiting for review.</summary>
        [HttpPost("withdraw")]
        public void Withdraw([FromForm] string guid, [FromForm] string password, [FromForm] int id)
        {
            var acc = SignedIn(guid, password);
            if (acc == null)
                return;
            if (!SpriteSubmissions.Withdraw(_core.Database.Conn, acc, id))
            {
                Response.CreateError("It isn't waiting for review any more.");
                return;
            }
            Response.CreateSuccess();
        }

        /// <summary>Admins: what's waiting for review (oldest first), and what's approved for the next release.</summary>
        [HttpPost("queue")]
        public void Queue([FromForm] string guid, [FromForm] string password)
        {
            if (Admin(guid, password) == null)
                return;
            var db = _core.Database.Conn;
            Response.CreateXml(new XElement("Queue",
                new XElement("Pending", SpriteSubmissions.PendingList(db).Select(s => Element(s))),
                new XElement("Approved", SpriteSubmissions.ApprovedList(db).Select(s => Element(s)))).ToString());
        }

        /// <summary>Admins: approve, reject (why, in note) or put back to waiting ("pending") one submission.</summary>
        [HttpPost("review")]
        public void Review([FromForm] string guid, [FromForm] string password, [FromForm] int id, [FromForm] string verdict, [FromForm] string note)
        {
            var admin = Admin(guid, password);
            if (admin == null)
                return;
            verdict = (verdict ?? "").Trim().ToLowerInvariant();
            if (!SpriteSubmissions.Review(_core.Database.Conn, admin, id, verdict, note))
            {
                Response.CreateError("That sprite can't be changed any more.");
                return;
            }
            ApprovedChanged();
            _core.Logger.Info($"Sprite {id} {verdict} by <{admin.Name}-{admin.AccountId}>");
            var sub = SpriteSubmissions.Get(_core.Database.Conn, id);
            Response.CreateXml(Element(sub).ToString());
        }

        /// <summary>
        /// Everything approved and not yet in a release, oldest approval first: what
        /// tools/community_sprites.py takes into the repository. Public, as the art is about to be.
        /// </summary>
        [HttpGet("approved")]
        [HttpPost("approved")]
        public void Approved()
        {
            string xml;
            lock (ApprovedLock)
            {
                if (_approvedXml == null || DateTime.UtcNow - _approvedAt > ApprovedCacheFor)
                {
                    _approvedXml = new XElement("Approved",
                        SpriteSubmissions.ApprovedList(_core.Database.Conn).Select(s => Element(s))).ToString();
                    _approvedAt = DateTime.UtcNow;
                }
                xml = _approvedXml;
            }
            Response.CreateXml(xml);
        }

        /// <summary>A review changed what's approved: the public list is worked out afresh.</summary>
        private static void ApprovedChanged()
        {
            lock (ApprovedLock)
                _approvedXml = null;
        }
    }
}
