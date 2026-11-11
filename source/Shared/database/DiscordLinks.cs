using StackExchange.Redis;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Shared.database.account;

namespace Shared.database
{
    public enum DiscordLinkStatus
    {
        OK,
        // no such code (mistyped, used or run out)
        BadCode,
        // too many wrong codes from this Discord account lately
        TooManyTries,
        // the game account is linked to a Discord account already
        AccountLinked,
        // the Discord account is linked to another game account
        DiscordLinked
    }

    /// <summary>
    /// A game account linked to a Discord account, so the Discord bot can act for its owner
    /// (reset the password, show the account). The player asks the account server for a code in
    /// the game's menu and gives it to the bot (/link); only the bot, which knows the shared
    /// secret, can redeem it.
    ///
    /// In Redis: the account's discordId and discordName fields, the hash discord.accounts
    /// (Discord id to account id), the set discord.rewarded (the Discord ids that have had the
    /// first link's reward), the set discord.unlinked (Discord ids unlinked in the game, until
    /// the bot has taken their Verified role and nickname away), and, for a few minutes each, discord.code.&lt;code&gt; (the account
    /// that asked), discord.codeFor.&lt;account&gt;, discord.tries.&lt;Discord id&gt; and
    /// discord.reset.&lt;account&gt;.
    /// </summary>
    public static class DiscordLinks
    {
        // no I, L, O, 0 or 1: a code is read off the game and typed into Discord
        public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 6;
        public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(10);
        // the first link's reward, into the account's gift chest; once per account and per Discord account
        public const string RewardItem = "Potion of Maxy";
        // wrong codes one Discord account may try within TriesWindow
        public const int MaxTries = 5;
        public static readonly TimeSpan TriesWindow = TimeSpan.FromMinutes(10);
        // one password reset per account in this long
        public static readonly TimeSpan ResetEvery = TimeSpan.FromMinutes(10);

        private const string Accounts = "discord.accounts";
        private const string Rewarded = "discord.rewarded";
        private const string Unlinked = "discord.unlinked";

        public static string NewCode()
        {
            var chars = new char[CodeLength];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            return new string(chars);
        }

        /// <summary>A code as typed (any case, with spaces or dashes) as it's stored, or null if it can't be one.</summary>
        public static string CleanCode(string typed)
        {
            if (typed == null)
                return null;
            var code = new string(typed.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            return code.Length == CodeLength && code.All(c => CodeAlphabet.Contains(c)) ? code : null;
        }

        /// <summary>A Discord account's id (a snowflake: digits only), or null.</summary>
        public static string CleanId(string id)
        {
            id = id?.Trim();
            return !string.IsNullOrEmpty(id) && id.Length <= 24 && id.All(c => c >= '0' && c <= '9') ? id : null;
        }

        /// <summary>A Discord name as kept and shown: no control characters, at most 40 characters.</summary>
        public static string CleanName(string name)
        {
            name = new string((name ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>' && c != '&').ToArray()).Trim();
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        /// <summary>Whether the secret a request gave is the configured one. No secret configured: never.</summary>
        public static bool SecretMatches(string configured, string given)
        {
            if (string.IsNullOrEmpty(configured) || given == null)
                return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(given));
        }

        /// <summary>A password for a reset: 12 letters and digits, easy to copy out of Discord.</summary>
        public static string NewPassword()
        {
            const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[12];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(chars);
        }

        /// <summary>A new code for the account, replacing any it asked for before.</summary>
        public static string StartCode(Database db, DbAccount acc)
        {
            var conn = db.Conn;
            var mine = "discord.codeFor." + acc.AccountId;
            var old = (string)conn.StringGet(mine);
            if (old != null)
                conn.KeyDelete("discord.code." + old);
            string code;
            do
                code = NewCode();
            while (!conn.StringSet("discord.code." + code, acc.AccountId, CodeLife, When.NotExists));
            conn.StringSet(mine, code, CodeLife);
            return code;
        }

        /// <summary>The game account linked to a Discord account, or null.</summary>
        public static DbAccount AccountOf(Database db, string discordId)
        {
            var id = (int?)db.Conn.HashGet(Accounts, discordId);
            if (id == null)
                return null;
            var acc = db.GetAccount(id.Value);
            // the account's own field decides: a stale entry of the hash links nothing
            return acc != null && acc.DiscordId == discordId ? acc : null;
        }

        /// <summary>
        /// Redeems a code for a Discord account. rewarded: this was the first link of both the
        /// game account and the Discord account, and the reward (rewardItem, RewardItem's type) is
        /// in the gift chest.
        /// </summary>
        public static DiscordLinkStatus Link(Database db, string code, string discordId, string discordName, ushort? rewardItem, out DbAccount acc, out bool rewarded)
        {
            acc = null;
            rewarded = false;
            var conn = db.Conn;
            var triesKey = "discord.tries." + discordId;
            if ((int?)conn.StringGet(triesKey) >= MaxTries)
                return DiscordLinkStatus.TooManyTries;

            code = CleanCode(code);
            var accountId = code == null ? null : (int?)conn.StringGet("discord.code." + code);
            var found = accountId == null ? null : db.GetAccount(accountId.Value);
            if (found == null)
            {
                if (conn.StringIncrement(triesKey) == 1)
                    conn.KeyExpire(triesKey, TriesWindow);
                return DiscordLinkStatus.BadCode;
            }

            if (!string.IsNullOrEmpty(found.DiscordId))
                return DiscordLinkStatus.AccountLinked;
            if (AccountOf(db, discordId) != null)
                return DiscordLinkStatus.DiscordLinked;

            // Both sides at once, and only while the code is still this account's and neither
            // side got linked meanwhile.
            var tran = conn.CreateTransaction();
            tran.AddCondition(Condition.StringEqual("discord.code." + code, found.AccountId));
            tran.AddCondition(Condition.HashNotExists(found.Key, "discordId"));
            _ = tran.HashSetAsync(found.Key, new[] { new HashEntry("discordId", discordId), new HashEntry("discordName", discordName ?? "") });
            _ = tran.HashSetAsync(Accounts, discordId, found.AccountId);
            _ = tran.KeyDeleteAsync("discord.code." + code);
            _ = tran.KeyDeleteAsync("discord.codeFor." + found.AccountId);
            _ = tran.SetRemoveAsync(Unlinked, discordId);
            if (!tran.Execute())
                return DiscordLinkStatus.BadCode;
            conn.KeyDelete(triesKey);

            // The reward, once per game account and once per Discord account, so linking again
            // (or the same Discord to account after account) gives nothing more.
            if (rewardItem is ushort item
                && !conn.SetContains(Rewarded, discordId)
                && conn.HashSet(found.Key, "discordReward", DateTime.UtcNow.ToString("o"), When.NotExists))
            {
                conn.SetAdd(Rewarded, discordId);
                for (var i = 0; i < 10 && !rewarded; i++)
                    rewarded = db.AddGift(found, item);
                if (!rewarded)
                {
                    // the gift chest kept changing under us: let a later link try again
                    conn.HashDelete(found.Key, "discordReward");
                    conn.SetRemove(Rewarded, discordId);
                }
            }

            found.Reload();
            acc = found;
            return DiscordLinkStatus.OK;
        }

        /// <summary>
        /// Unlinks the account from its Discord account. False: it wasn't linked. tellBot: the bot
        /// didn't ask for this (the player did, in the game), so the Discord id is kept for it to
        /// find (UnlinkedInGame) and take the member's Verified role and nickname away.
        /// </summary>
        public static bool Unlink(Database db, DbAccount acc, bool tellBot)
        {
            acc.Reload("discordId");
            var discordId = acc.DiscordId;
            if (string.IsNullOrEmpty(discordId))
                return false;
            var conn = db.Conn;
            var tran = conn.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(acc.Key, "discordId", discordId));
            _ = tran.HashDeleteAsync(acc.Key, new RedisValue[] { "discordId", "discordName" });
            _ = tran.HashDeleteAsync(Accounts, discordId);
            if (tellBot)
                _ = tran.SetAddAsync(Unlinked, discordId);
            if (!tran.Execute())
                return false;
            acc.Reload();
            return true;
        }

        /// <summary>
        /// The Discord ids unlinked in the game that the bot hasn't dealt with yet. done: the ones
        /// it has, which are forgotten, as are any linked again since.
        /// </summary>
        public static string[] UnlinkedInGame(Database db, string[] done)
        {
            var conn = db.Conn;
            foreach (var id in done.Select(CleanId).Where(id => id != null))
                conn.SetRemove(Unlinked, id);
            var ids = conn.SetMembers(Unlinked).Select(v => (string)v).ToArray();
            foreach (var id in ids.Where(id => AccountOf(db, id) != null))
                conn.SetRemove(Unlinked, id);
            return ids.Where(id => AccountOf(db, id) == null).ToArray();
        }

        /// <summary>
        /// Gives the account a new password (returned), or null when it had one within ResetEvery.
        /// The count of wrong passwords starts again, so the owner can sign in at once.
        /// </summary>
        public static string ResetPassword(Database db, DbAccount acc)
        {
            if (!db.Conn.StringSet("discord.reset." + acc.AccountId, 1, ResetEvery, When.NotExists))
                return null;
            var password = NewPassword();
            db.ChangePassword(acc.UUID, password);
            db.ClearLoginFails(acc.UUID);
            return password;
        }
    }
}
