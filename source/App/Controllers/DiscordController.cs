using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Xml.Linq;
using Shared;
using Shared.database;
using Shared.database.account;

namespace App.Controllers
{
    /// <summary>
    /// What the Discord bot (tools/discord/bot.py) asks for its members: linking a Discord account
    /// to a game account with the code the game's menu gave (account/discordCode), and then that
    /// account's summary, a password reset and unlinking. Every request carries the secret the bot
    /// shares with this server (CoreService.DiscordSecret); without one configured they all fail.
    /// The Discord ids come from Discord through the bot, so they are who they say.
    /// </summary>
    [ApiController]
    [Route("discord")]
    public class DiscordController : ControllerBase
    {
        private readonly CoreService _core;

        public DiscordController(CoreService core)
        {
            _core = core;
        }

        // the bot's request: the right secret and a Discord id
        private bool FromBot(string secret, ref string discordId)
        {
            if (_core.DiscordSecret.Length == 0)
            {
                Response.CreateError("Discord linking isn't set up on this server.");
                return false;
            }
            if (!DiscordLinks.SecretMatches(_core.DiscordSecret, secret))
            {
                _core.Logger.Warn($"A Discord request with the wrong secret from {HttpContext.Connection.RemoteIpAddress}");
                Response.CreateError("Not allowed.");
                return false;
            }
            discordId = DiscordLinks.CleanId(discordId);
            if (discordId == null)
            {
                Response.CreateError("No Discord account given.");
                return false;
            }
            return true;
        }

        private DbAccount Linked(string discordId)
        {
            var acc = DiscordLinks.AccountOf(_core.Database, discordId);
            if (acc == null)
                Response.CreateError("Not linked");
            return acc;
        }

        [HttpPost("link")]
        public void Link([FromForm] string secret, [FromForm] string discordId, [FromForm] string discordName, [FromForm] string code)
        {
            if (!FromBot(secret, ref discordId))
                return;

            var reward = _core.Resources.GameData.IdToObjectType.TryGetValue(DiscordLinks.RewardItem, out var item) ? item : (ushort?)null;
            var status = DiscordLinks.Link(_core.Database, code, discordId, DiscordLinks.CleanName(discordName), reward, out var acc, out var rewarded);
            switch (status)
            {
                case DiscordLinkStatus.OK:
                    _core.Logger.Info($"Discord {discordId} linked to <{acc.Name}-{acc.AccountId}>{(rewarded ? ", with the first link's reward" : "")}");
                    Response.CreateXml(new XElement("Linked",
                        new XElement("Name", acc.Name),
                        rewarded ? new XElement("Reward", DiscordLinks.RewardItem) : null).ToString());
                    return;
                case DiscordLinkStatus.TooManyTries:
                    Response.CreateError("Too many tries");
                    return;
                case DiscordLinkStatus.AccountLinked:
                    Response.CreateError("Account linked");
                    return;
                case DiscordLinkStatus.DiscordLinked:
                    Response.CreateError("Discord linked");
                    return;
                default:
                    Response.CreateError("Bad code");
                    return;
            }
        }

        [HttpPost("unlink")]
        public void Unlink([FromForm] string secret, [FromForm] string discordId)
        {
            if (!FromBot(secret, ref discordId))
                return;
            var acc = Linked(discordId);
            if (acc == null)
                return;
            if (!DiscordLinks.Unlink(_core.Database, acc, tellBot: false))
            {
                Response.CreateError("Not linked");
                return;
            }
            _core.Logger.Info($"Discord {discordId} unlinked from <{acc.Name}-{acc.AccountId}>");
            Response.CreateXml(new XElement("Unlinked", new XElement("Name", acc.Name)).ToString());
        }

        /// <summary>
        /// The Discord ids whose accounts were unlinked in the game, for the bot to take their
        /// Verified role and nickname away; done (ids, comma-separated) are those it has dealt with.
        /// </summary>
        [HttpPost("unlinked")]
        public void UnlinkedInGame([FromForm] string secret, [FromForm] string discordId, [FromForm] string done)
        {
            if (!FromBot(secret, ref discordId))
                return;
            var ids = DiscordLinks.UnlinkedInGame(_core.Database, (done ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries));
            Response.CreateXml(new XElement("Unlinked", new XElement("Ids", string.Join(",", ids))).ToString());
        }

        [HttpPost("account")]
        public void AccountOf([FromForm] string secret, [FromForm] string discordId)
        {
            if (!FromBot(secret, ref discordId))
                return;
            var acc = Linked(discordId);
            if (acc == null)
                return;
            var guild = acc.GuildId > 0 ? _core.Database.GetGuild(acc.GuildId) : null;
            Response.CreateXml(new XElement("Account",
                new XElement("Name", acc.Name),
                new XElement("Fame", acc.Fame),
                new XElement("TotalFame", acc.TotalFame),
                new XElement("Characters", _core.Database.GetAliveCharacters(acc).Count()),
                guild != null ? new XElement("Guild", guild.Name) : null).ToString());
        }

        /// <summary>
        /// A new password for the linked account, for a player who lost theirs: the bot shows it,
        /// with the email to sign in with, to that member only.
        /// </summary>
        [HttpPost("resetPassword")]
        public void ResetPassword([FromForm] string secret, [FromForm] string discordId)
        {
            if (!FromBot(secret, ref discordId))
                return;
            var acc = Linked(discordId);
            if (acc == null)
                return;
            var password = DiscordLinks.ResetPassword(_core.Database, acc);
            if (password == null)
            {
                Response.CreateError("Too soon");
                return;
            }
            _core.Logger.Info($"Password reset for <{acc.Name}-{acc.AccountId}> by Discord {discordId}");
            Response.CreateXml(new XElement("Reset",
                new XElement("Name", acc.Name),
                new XElement("Email", acc.UUID),
                new XElement("Password", password)).ToString());
        }
    }
}
