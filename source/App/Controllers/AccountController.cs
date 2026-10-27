using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shared;
using Shared.database;
using Shared.database.account;
using Shared.resources;
using Shared.utils;

namespace App.Controllers
{
    [ApiController]
    [Route("account")]
    public class AccountController : ControllerBase
    {
        private readonly CoreService _core;

        public AccountController(CoreService core)
        {
            _core = core;
        }

        [HttpPost("purchaseSkin")]
        public void PurcahseSkin([FromForm] string guid, [FromForm] string password, [FromForm] string skinType)
        {
            var db = _core.Database;

            var status = db.Verify(guid, password, out var acc);
            if (status == DbLoginStatus.OK)
            {
                var type = (ushort)Utils.GetInt(skinType);
                var skinDesc = _core.Resources.GameData.Skins[type];
                var classStats = _core.Database.ReadClassStats(acc);

                if (skinDesc.UnlockLevel > classStats[skinDesc.PlayerClassType].BestLevel || skinDesc.Cost > acc.Credits)
                {
                    Response.CreateError("Failed to purchase skin");
                    return;
                }

                db.PurchaseSkin(acc, type, skinDesc.Cost);
                Response.CreateSuccess();
                return;
            }
            Response.CreateError(status.GetInfo());
        }

        [HttpPost("purchaseCharSlot")]
        public async Task PurchaseCharSlot([FromForm] string guid, [FromForm] string password)
        {
            var db = _core.Database;

            var status = db.Verify(guid, password, out var acc);
            if (status != DbLoginStatus.OK)
            {
                Response.CreateError(status.GetInfo());
                return;
            }

            using (var accountLock = db.LockAccount(acc))
            {
                if (!accountLock.HasLock)
                {
                    Response.CreateError("Account in use");
                    return;
                }

                var currency = _core.Resources.Settings.NewAccounts.SlotCurrency;
                var price = _core.Resources.Settings.NewAccounts.SlotCost;

                if (currency == CurrencyType.Gold && acc.Credits < price || currency == CurrencyType.Fame && acc.Fame < price)
                {
                    Response.CreateError("Insufficient funds");
                    return;
                }

                var trans = db.Conn.CreateTransaction();

                var t1 = db.UpdateCurrency(acc, -price, currency, trans);

                trans.AddCondition(Condition.HashEqual(acc.Key, "maxCharSlot", acc.MaxCharSlot));

                // queued commands only complete once the transaction runs, so they can't be awaited before it
                _ = trans.HashIncrementAsync(acc.Key, "maxCharSlot");

                if (!await trans.ExecuteAsync())
                {
                    Response.CreateError("Purchase failed, please try again");
                    return;
                }

                await t1;
                Response.CreateSuccess();
            }
        }

        // removed so that people cant call the api and get shit for free
        //[HttpPost("handleDono")]
        //public void HandleDono([FromForm] string accountId, [FromForm] string secret, [FromForm] string type, [FromForm] string amountDonated)
        //{
        //    if (secret == "NEW_SECRET_KEY_HERE_TODO_SOMEONE_DONT_LET_THEM_KNOW_YOUR_NEXT_MOVE_OK")
        //    {
        //        var amount = int.Parse(amountDonated);

        //        var acc = _core.Database.GetAccount(int.Parse(accountId));
        //        if (type == "rank")
        //        {
        //            var rank = new DbRank(acc.Database, acc.AccountId);
        //            rank.NewAmountDonated += amount;
        //            rank.Flush();
        //            Response.CreateSuccess();
        //        }
        //        else if (type == "gold")
        //        {
        //            acc.Credits += amount;
        //            acc.TotalCredits += amount;
        //            acc.FlushAsync();
        //            Response.CreateSuccess();
        //        }
        //        return;
        //    }
        //    Response.CreateError("Internal Server Error.");
        //}

        private const string PasswordRule = "Passwords need at least 6 characters.";

        private static bool IsValidPassword(string password) => password != null && password.Length >= 6;

        [HttpPost("changePassword")]
        public void ChangePassword([FromForm] string guid, [FromForm] string password, [FromForm] string newPassword)
        {
            var status = _core.Database.Verify(guid, password, out var acc);
            if (status == DbLoginStatus.OK)
            {
                if (!IsValidPassword(newPassword))
                {
                    Response.CreateError(PasswordRule);
                    return;
                }

                _core.Database.ChangePassword(guid, newPassword);
                _core.Logger.Info($"Password changed for <{acc.Name}-{acc.AccountId}>");
                Response.CreateSuccess();
                return;
            }
            Response.CreateError(status.GetInfo());
        }

        [HttpPost("verify")]
        public void Verify([FromForm] string guid, [FromForm] string password, [FromForm] string secret)
        {
            var status = _core.Database.Verify(guid, password, out DbAccount acc);
            if (status == DbLoginStatus.OK)
            {
                Response.CreateXml(Account.FromDb(_core, acc).ToXml().ToString());
                return;
            }
         
            Response.CreateError(status.GetInfo());
        }

        [HttpPost("grabPotions")]
        public void GrabPotions([FromForm] string guid, [FromForm] string password, [FromForm] string secret)
        {
            var status = _core.Database.Verify(guid, password, out DbAccount acc);
            if (status == DbLoginStatus.OK)
            {
                Response.CreateXml(Account.FromDb(_core, acc).PotionsToXml().ToString());
                return;
            }

            Response.CreateError(status.GetInfo());
        }

        [HttpPost("setName")]
        public void SetName()
        {
            Response.CreateError("Unknown Error");
        }
        
        /// <summary>
        /// A new guest account, for playing without registering: a made-up login the client keeps
        /// on its device. Registering later with it (register's guid and password) keeps everything.
        /// </summary>
        [HttpPost("guest")]
        public void Guest()
        {
            if (!GuestLimit.Allow(ClientIp()))
            {
                Response.CreateError("Too many new guests from here. Try again later, or register.");
                return;
            }

            var guid = "guest-" + Guid.NewGuid().ToString("N");
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
            var status = _core.Database.Register(guid, password, true, out var acc);
            if (status != DbRegisterStatus.OK)
            {
                Response.CreateError(status.GetInfo());
                return;
            }

            // A name of its own (the world server only lets named accounts in, and finds players
            // by name): "Guest" and four random letters.
            var nameKey = Database.NAME_LOCK;
            string nameLock = null;
            try
            {
                while ((nameLock = _core.Database.AcquireLock(nameKey)) == null) ;
                string guestName;
                do
                {
                    var letters = RandomNumberGenerator.GetBytes(4).Select(b => (char)('a' + b % 26));
                    guestName = "Guest" + char.ToUpperInvariant(letters.First()) + new string(letters.Skip(1).ToArray());
                }
                while (_core.Database.Conn.HashExists("names", guestName.ToUpperInvariant()));
                while (!_core.Database.RenameIGN(acc, guestName, nameLock)) ;
            }
            finally
            {
                if (nameLock != null)
                    _core.Database.ReleaseLock(nameKey, nameLock);
            }

            _core.Logger.Info($"Guest account {acc.AccountId} ({acc.Name}) for {ClientIp()}");
            Response.CreateXml(new XElement("Guest",
                new XElement("GUID", guid),
                new XElement("Password", password),
                new XElement("Name", acc.Name)).ToString());
        }

        /// <summary>The player's address: behind this PC's own proxy (the gateway, Caddy), the one it forwarded.</summary>
        private string ClientIp()
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            var forwarded = Request.Headers["X-Forwarded-For"].ToString();
            if (ip != null && IPAddress.IsLoopback(ip) && !string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();
            return ip?.ToString() ?? "";
        }

        /// <summary>At most 10 new guests an hour from one address, and 300 an hour in all.</summary>
        private static readonly HourlyLimit GuestLimit = new(10, 300);

        /// <summary>At most 5 registrations an hour from one address, and 100 an hour in all.</summary>
        private static readonly HourlyLimit RegisterLimit = new(5, 100);

        private sealed class HourlyLimit
        {
            private readonly Dictionary<string, List<DateTime>> ByIp = new();
            private readonly List<DateTime> All = new();
            private readonly int PerIp;
            private readonly int Total;

            public HourlyLimit(int perIp, int total)
            {
                PerIp = perIp;
                Total = total;
            }

            public bool Allow(string ip)
            {
                var now = DateTime.UtcNow;
                lock (All)
                {
                    All.RemoveAll(t => now - t > TimeSpan.FromHours(1));
                    foreach (var key in ByIp.Keys.ToList())
                    {
                        ByIp[key].RemoveAll(t => now - t > TimeSpan.FromHours(1));
                        if (ByIp[key].Count == 0)
                            ByIp.Remove(key);
                    }

                    if (!ByIp.TryGetValue(ip, out var times))
                        ByIp[ip] = times = new List<DateTime>();
                    if (times.Count >= PerIp || All.Count >= Total)
                        return false;
                    times.Add(now);
                    All.Add(now);
                    return true;
                }
            }
        }

        [HttpPost("register")]
        public void Register([FromForm] string guid, [FromForm] string newGuid, [FromForm] string newPassword, [FromForm] string name, [FromForm] string password)
        {
            // A guest registering (guid and password are its made-up login): the guest account
            // becomes this one, characters and all.
            var claiming = !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(guid) && !guid.Equals(newGuid, StringComparison.OrdinalIgnoreCase);

            //if (_core.IsProduction()) // old code
            //{
            //    if (_core.Config.serverInfo.requireSecret && secret != "69420")
            //    {
            //        _core.Logger.Warn($"{newGuid} Tried to register without secret");
            //        Response.CreateError("Internal Server Error.");
            //        return;
            //    }

            //    if (_core.Resources.Settings.NewAccounts.TestingRegister)
            //    {
            //        Response.CreateError("Testers Only");
            //        return;
            //    }
            //}

            if (Database.GuestNames.Contains(name, StringComparer.InvariantCultureIgnoreCase) || !IsValidName(name))
            {
                Response.CreateError("Invalid name");
                return;
            }

            foreach (var i in Database.BlackListedNames)
                if (name.ToLower() == i.ToLower())
                {
                    Response.CreateError("Invalid name");
                    return;
                }

            if (!Utils.IsValidEmail(newGuid))
            {
                Response.CreateError("Invalid email");
                return;
            }

            if (!IsValidPassword(newPassword))
            {
                Response.CreateError(PasswordRule);
                return;
            }

            if (!RegisterLimit.Allow(ClientIp()))
            {
                Response.CreateError("Too many new accounts from here. Try again later.");
                return;
            }

            var key = Database.REG_LOCK;
            var lockToken = "";
            try
            {
                while ((lockToken = _core.Database.AcquireLock(key)) == null) ;

                string nameLockToken = null;

                var nameKey = Database.NAME_LOCK;

                try
                {
                    while ((nameLockToken = _core.Database.AcquireLock(nameKey)) == null) ;

                    if (_core.Database.Conn.HashExists("names", name.ToUpperInvariant()))
                    {
                        Response.CreateError("Duplicated name");
                        return;
                    }

                    if (claiming)
                    {
                        ClaimGuest(guid, password, newGuid, newPassword, name, lockToken, nameLockToken);
                        return;
                    }

                    var s = _core.Database.Register(newGuid, newPassword, false, out var acc);
                    if (s == DbRegisterStatus.OK)
                    {
                        while (!_core.Database.RenameIGN(acc, name, nameLockToken)) ;

                        Response.CreateSuccess();
                    }
                    else
                        Response.CreateError(s.GetInfo());
                }
                finally
                {
                    if (nameLockToken != null)
                        _core.Database.ReleaseLock(nameKey, nameLockToken);
                }

            }
            finally
            {
                if (lockToken != null)
                    _core.Database.ReleaseLock(key, lockToken);
            }

            void ClaimGuest(string guestGuid, string guestPassword, string email, string newPw, string newName, string regLock, string nameLock)
            {
                var db = _core.Database;
                if (db.Verify(guestGuid, guestPassword, out var acc) != DbLoginStatus.OK || !acc.Guest)
                {
                    Response.CreateError("Only a guest account can be registered this way");
                    return;
                }

                using (var accountLock = db.LockAccount(acc))
                {
                    if (!accountLock.HasLock)
                    {
                        Response.CreateError("Account in use");
                        return;
                    }

                    if (!db.RenameUUID(acc, email, regLock))
                    {
                        Response.CreateError(DbRegisterStatus.UsedName.GetInfo());
                        return;
                    }

                    db.ChangePassword(email, newPw);
                    acc.Guest = false;
                    while (!db.RenameIGN(acc, newName, nameLock)) ;
                    _core.Logger.Info($"Guest account {acc.AccountId} registered as {newName}");
                    Response.CreateSuccess();
                }
            }

            bool IsValidName(string text)
            {
                var nonDup = new Regex(@"([a-zA-z]{2,})\1{1,}");
                var alpha = new Regex(@"^[A-Za-z]{1,10}$");
                return !(nonDup.Matches(text).Count > 0) && alpha.Matches(text).Count > 0;
            }
        }
    }
}