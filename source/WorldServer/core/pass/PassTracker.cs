using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.champions;
using Shared.pass;
using Shared.quests;
using StackExchange.Redis;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing;
using WorldServer.networking.packets.outgoing.pass;
using WorldServer.utils;

namespace WorldServer.core.pass
{
    // A player's Realm Pass (Shared.pass.PassBook) in the game: XP from what they do (QuestTracker
    // passes on its events and claims), the pass window's data and answers, the Gilded track
    // bought with gold, and the rewards paid. The state is the account's "pass" field (JSON),
    // saved after every claim and purchase, every 30 s while it changes, and when the player
    // leaves (Client.Save). One world server holds an account at a time, so nothing else writes it.
    public sealed class PassTracker
    {
        // PASS_ACT's kinds
        public const byte REQUEST = 0, CLAIM = 1, UNLOCK = 2, AURA = 3, COLOR = 4, CACHE = 5;

        // the relic looks (ShowEffect 41's pos2.y) for a tier reached and the Gilded track bought
        // (the Rust client's particles/relic.rs)
        private const int FxTierUp = 46, FxGilded = 47;

        private static PassData _data;
        private static readonly object DataLock = new object();

        public static PassData Data(GameServer server)
        {
            lock (DataLock)
            {
                if (_data != null)
                    return _data;
                var path = $"{server.Resources.ResourcePath}/data/pass.json";
                try
                {
                    _data = PassData.Load(path);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Realm Pass: can't read {path}: {e.Message}");
                    _data = new PassData();
                }
                var game = server.Resources.GameData;
                foreach (var problem in _data.Validate(game.IdToObjectType.ContainsKey, id => SkinOf(game, id) != null))
                    StaticLogger.Instance.Warn($"Realm Pass: {problem}");
                var now = QuestTracker.Now;
                StaticLogger.Instance.Info($"Realm Pass: {_data.Seasons.Count} seasons, now {_data.At(now)?.Name ?? "none"}");
                return _data;
            }
        }

        // A skin by its id ("Frostcrown Archmage").
        public static Shared.resources.SkinDesc SkinOf(Shared.resources.XmlData game, string id) =>
            game.Skins.Values.FirstOrDefault(s => s.ObjectId == id);

        private readonly Player _player;
        private readonly Shared.database.account.DbAccount _account;
        public PassBook Book { get; private set; }
        // the client asked for the pass window's data (it's a Rust client that knows the pass)
        public bool Subscribed { get; private set; }
        private bool _dirty;
        private float _saveIn = 30f, _checkIn = 15f;
        private DateTime _lastAct;
        private readonly Random _rng = new Random();

        public PassTracker(Player player)
        {
            _player = player;
            _account = player.Client.Account;
            Book = new PassBook(Data(player.GameServer), PassState.FromJson(_account.Pass));
            Refresh();
            _player.Aura = Book.WornAura;
        }

        private bool Connected => _player.Client?.Account == _account;

        // A guest, the test world, or a player who has left: no pass.
        private bool Off => !Connected || _account.Guest || _player.World is TestWorld;

        // Moves to the season running now; what an ended one left unclaimed is paid.
        private void Refresh()
        {
            var before = Book.State.Season;
            var left = Book.Refresh(QuestTracker.Now, _rng);
            if (Book.State.Season != before)
                _dirty = true;
            if (left.Count == 0 || _account.Guest)
                return;
            var old = before == null ? "last season" : Book.DefOf(before)?.Name ?? "last season";
            Save();
            Pay(left, $"what {old} left unclaimed");
            _player.SendInfo($"<Realm Pass> {old} has ended: the rewards you had reached and not claimed are yours now.");
            StaticLogger.Instance.Info($"Realm Pass: {_player.Name} got what {before} left: {string.Join(", ", left)}");
        }

        // What happened in the game (QuestTracker.Event passes everything on).
        public void Event(in QuestEvent e)
        {
            if (Off || !Book.Active)
                return;
            var xp = Book.Data.Xp;
            switch (e.Type)
            {
                case QuestEvents.Dungeon:
                    Gain(PassSource.Dungeon, Book.DungeonXp(e.Difficulty));
                    if (e.Elder)
                        Gain(PassSource.Elder, xp.Elder);
                    if (Book.TakeFirstDungeon(QuestTracker.Now))
                        Gain(PassSource.FirstDungeon, xp.FirstDungeon);
                    break;
                case QuestEvents.Kill when e.Oryx:
                    Gain(PassSource.Oryx, xp.Oryx);
                    break;
                case QuestEvents.Kill when e.Boss:
                    Gain(PassSource.Boss, xp.Boss);
                    break;
                case QuestEvents.RealmClosed:
                    Gain(PassSource.RealmClosed, xp.RealmClosed);
                    break;
                case QuestEvents.ArenaWave:
                    Gain(PassSource.ArenaWave, xp.ArenaWave);
                    break;
                case QuestEvents.Fame:
                    Gain(PassSource.Fame, Book.FameXp(e.Amount));
                    break;
            }
        }

        // A claim in the quest log went through (QuestTracker.Claim): a quest, a bonus, the daily reward.
        public void QuestClaimed(byte kind, QuestPeriod period)
        {
            if (Off || !Book.Active)
                return;
            switch (kind)
            {
                case QuestTracker.CLAIM_QUEST:
                    Gain((PassSource)((int)PassSource.QuestDaily + (int)period), Book.QuestXp(period));
                    break;
                case QuestTracker.CLAIM_BONUS:
                    Gain((PassSource)((int)PassSource.BonusDaily + (int)period), Book.BonusXpOf(period));
                    break;
                case QuestTracker.CLAIM_LOGIN:
                    Gain(PassSource.Login, Book.Data.Xp.Login);
                    break;
            }
        }

        public void Gain(PassSource source, int xp)
        {
            if (xp <= 0 || Off)
                return;
            var g = Book.Gain(xp, QuestTracker.Now);
            if (g.Gained <= 0)
                return;
            _dirty = true;
            if (Subscribed)
                _player.Client.SendPacket(new PassXp { Gained = g.Gained, Source = (byte)source, Xp = Book.State.Xp, Tier = (byte)Book.Tier, Momentum = g.Momentum });
            if (g.TierUp)
            {
                Effect(FxTierUp, Book.Season.Def.Accent);
                if (!Subscribed)
                    _player.SendInfo($"<Realm Pass> Tier {g.TierAfter} reached!");
                StaticLogger.Instance.Info($"Realm Pass: {_player.Name} reached tier {g.TierAfter} of {Book.Season.Key}");
            }
        }

        // A look round the player that everyone near sees.
        private void Effect(int look, string color)
        {
            if (_player.World == null)
                return;
            var at = new Position(_player.X, _player.Y);
            var rgb = (uint)PassCosmetic.ParseColor(color);
            _player.World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = _player.Id, Pos1 = at,
                Pos2 = new Position(3f, look), Color = new ARGB(0xFF000000 | rgb), Duration = 2200, SourceId = _player.Id,
            }, ref at);
        }

        public void Tick(ref TickTime time)
        {
            if (!Connected)
                return;
            var dt = time.DeltaTime;
            _checkIn -= dt;
            if (_checkIn <= 0)
            {
                _checkIn = 15f;
                var season = Book.State.Season;
                Refresh();
                if (Book.State.Season != season && Subscribed)
                    SendData();
            }
            _saveIn -= dt;
            if (_saveIn <= 0)
            {
                _saveIn = 30f;
                if (_dirty)
                    Save();
            }
        }

        public void Save()
        {
            Store();
            _account.FlushAsync();
        }

        public void Store()
        {
            if (Off && !_dirty)
                return;
            _account.Pass = Book.State.ToJson();
            _dirty = false;
        }

        // Staff testing (/passadmin reset): this season afresh, cosmetics kept unless asked.
        public void ResetState(bool everything)
        {
            var old = Book.State;
            var state = new PassState();
            if (!everything)
            {
                state.Auras = old.Auras;
                state.Aura = old.Aura;
                state.Colors = old.Colors;
                state.Color = old.Color;
            }
            Book = new PassBook(Book.Data, state);
            Book.Refresh(QuestTracker.Now, _rng);
            _player.Aura = Book.WornAura;
            Save();
            if (Subscribed)
                SendData();
        }

        public int ReadyCount => Off ? 0 : Book.ReadyCount;

        // The Gilded pass's boost to the community goals' loot (CommunityGoals.Claim).
        public (int Luck, int Rolls) CommunityBoost
        {
            get
            {
                if (Off)
                    return (0, 0);
                Book.Refresh(QuestTracker.Now, _rng);
                return Book.CommunityBoost;
            }
        }

        public void Act(byte kind, int value)
        {
            if (!Connected)
                return;
            // each answer sends the whole window back: a client can't flood the server with them
            if (kind != REQUEST && (DateTime.UtcNow - _lastAct).TotalMilliseconds < 150)
                return;
            _lastAct = DateTime.UtcNow;
            if (kind == REQUEST)
            {
                Subscribed = true;
                Refresh();
                SendData();
                return;
            }
            PassClaim result;
            if (_account.Guest)
                result = PassClaim.Fail(PassCode.Guest, "Register your account to play the Realm Pass.");
            else if (_player.World is TestWorld)
                result = PassClaim.Fail(PassCode.NotHere, "Not here.");
            else if (_player.Ghost)
                result = PassClaim.Fail(PassCode.NotHere, "Not while you're a ghost.");
            else
            {
                Refresh();
                result = kind switch
                {
                    CLAIM => Book.Claim(value),
                    CACHE => Book.OpenCaches(_rng),
                    UNLOCK => Unlock(value),
                    AURA => Book.WearAura(value),
                    COLOR => Book.WearColor(value),
                    _ => PassClaim.Fail(PassCode.Failed, "Unknown request."),
                };
            }
            if (result.Ok)
            {
                if (kind == AURA)
                    _player.Aura = Book.WornAura;
                if (kind == COLOR)
                    ApplyColor();
                // recorded before anything is paid, so nothing is paid twice
                Save();
                if (result.Rewards.Count > 0)
                    Pay(result.Rewards, Describe(kind, result));
                StaticLogger.Instance.Info($"Realm Pass: {_player.Name} {Describe(kind, result)}{(result.Rewards.Count > 0 ? ": " + string.Join(", ", result.Rewards) : "")}");
            }
            _player.Client.SendPacket(new PassResult
            {
                Code = (byte)result.Code,
                Success = result.Ok,
                Count = result.Count,
                Message = result.Message ?? "",
                Rewards = kind == CACHE || kind == CLAIM ? Rewards(result.Rewards) : new List<PassRewardInfo>(),
            });
            if (Subscribed)
                SendData();
        }

        private string Describe(byte kind, PassClaim r) => kind switch
        {
            CLAIM => r.Code == PassCode.ClaimedAll ? $"claimed {r.Count} tiers of {Book.Season.Key}" : $"claimed tier {r.Count} of {Book.Season.Key}",
            CACHE => $"opened {r.Count} cache{(r.Count == 1 ? "" : "s")}",
            UNLOCK => $"bought the Gilded pass of {Book.Season.Key}",
            AURA => $"wears aura {Book.State.Aura}",
            _ => $"wears name colour {Book.State.Color}",
        };

        // The chat name colour: the pass's colour worn, or back to the quest log's rank colour.
        private void ApplyColor()
        {
            var c = Book.Data.Color(Book.State.Color);
            if (c != null)
            {
                _account.ColorNameChat = c.ColorValue;
                _account.NameFrom = LookChoice.NameChosen;
                if (_player.Quests != null)
                {
                    _player.Quests.Book.State.ColorRank = -1;
                    _player.Quests.Save();
                }
            }
            else
                _account.ColorNameChat = _player.Quests?.Book.NameColor ?? 0;
            _player.UpdateNameColor();
        }

        // A rank colour picked in the quest log's Customize: the pass's colour is no longer worn.
        public void ColorChosenElsewhere()
        {
            if (Book.State.Color == 0)
                return;
            Book.State.Color = 0;
            _dirty = true;
        }

        // The Gilded track: the gold taken and the unlock recorded together, in one transaction
        // that only goes through if the gold hasn't changed meanwhile.
        private PassClaim Unlock(int price)
        {
            var check = Book.CanUnlock(price);
            if (!check.Ok)
                return check;
            var db = _player.GameServer.Database;
            _account.Reload("credits");
            var gold = _account.Credits;
            if (gold < Book.Data.Price)
                return PassClaim.Fail(PassCode.NotEnoughGold, $"The Gilded pass costs {Book.Data.Price:N0} gold; you have {gold:N0}.", gold);
            Book.MarkGilded();
            var json = Book.State.ToJson();
            var trans = db.Conn.CreateTransaction();
            trans.AddCondition(Condition.HashEqual(_account.Key, "credits", gold));
            var left = trans.HashIncrementAsync(_account.Key, "credits", -Book.Data.Price);
            _ = trans.HashSetAsync(_account.Key, "pass", json);
            bool ok;
            try
            {
                ok = trans.Execute();
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Error($"Realm Pass: {_player.Name}'s unlock failed: {e.Message}");
                ok = false;
            }
            if (!ok)
            {
                Book.State.Gilded = false;
                Book.State.GildedSeasons.Remove(Book.Season.Key);
                return PassClaim.Fail(PassCode.Busy, "Your gold changed just then: try again.");
            }
            var now = (int)left.Result;
            _account.StoredCredits(now);
            _account.Pass = json;
            _player.Credits = now;
            Effect(FxGilded, "0xFFD24A");
            _player.World?.BroadcastIfVisible(new Notification { ObjectId = _player.Id, Color = new ARGB(0xFFFFD24A), Message = "Gilded!" }, _player);
            return check;
        }

        // Fame and gold to the account, items to the vault's gift chest, skins to the account's,
        // character slots; auras and name colours were recorded by the book.
        private void Pay(List<PassReward> rewards, string why)
        {
            var player = _player;
            var db = player.GameServer.Database;
            var game = player.GameServer.Resources.GameData;
            var fame = rewards.Sum(r => r.Fame);
            if (fame > 0)
            {
                DoomedWorlds.GiveFame(player, fame, $"the Realm Pass ({why})");
                DoomedWorlds.Notify(player, 0xFFFFC65C, $"+{fame:N0} Fame");
            }
            var gold = rewards.Sum(r => r.Gold);
            if (gold > 0)
            {
                var now = (int)db.Conn.HashIncrement(_account.Key, "credits", gold);
                _account.StoredCredits(now);
                player.Credits = now;
                DoomedWorlds.Notify(player, 0xFFFFD24A, $"+{gold:N0} Gold");
                StaticLogger.Instance.Info($"Gold: <{player.Name} {player.AccountId}> got {gold} gold from the Realm Pass ({why})");
            }
            var slots = rewards.Sum(r => r.Slot);
            if (slots > 0)
            {
                var max = (int)db.Conn.HashIncrement(_account.Key, "maxCharSlot", slots);
                _account.Reload("maxCharSlot");
                player.SendInfo($"<Realm Pass> {(slots == 1 ? "A character slot is" : $"{slots} character slots are")} yours: you have {max} now.");
            }
            var gifts = new List<ushort>();
            foreach (var r in rewards.Where(r => r.Item != null))
                if (game.IdToObjectType.TryGetValue(r.Item, out var type) && game.Items.ContainsKey(type))
                    for (var i = 0; i < Math.Max(1, r.Count); i++)
                        gifts.Add(type);
            if (gifts.Count > 0)
            {
                db.AddGifts(_account, gifts);
                var names = string.Join(", ", rewards.Where(r => r.Item != null).GroupBy(r => r.Item).Select(g =>
                {
                    var n = game.IdToObjectType.TryGetValue(g.Key, out var t) && game.Items.TryGetValue(t, out var item) ? item.DisplayName : g.Key;
                    var c = g.Sum(r => Math.Max(1, r.Count));
                    return c == 1 ? n : $"{n} x{c}";
                }));
                player.SendInfo($"{(gifts.Count == 1 ? "Your reward went" : $"Your {gifts.Count} rewards went")} to the gift chest in your vault: {names}.");
            }
            foreach (var r in rewards.Where(r => r.Skin != null))
            {
                var skin = SkinOf(game, r.Skin);
                if (skin == null)
                    continue;
                _account.Reload("skins");
                var owned = _account.Skins.ToList();
                if (owned.Contains(skin.Type))
                {
                    DoomedWorlds.GiveFame(player, Book.Data.RepeatFame, "the Realm Pass (a skin owned already)");
                    DoomedWorlds.Notify(player, 0xFFFFC65C, $"+{Book.Data.RepeatFame:N0} Fame");
                    continue;
                }
                owned.Add(skin.Type);
                _account.Skins = owned.ToArray();
                _account.FlushAsync();
                var cls = game.ObjectDescs.TryGetValue(skin.PlayerClassType, out var cd) ? cd.DisplayId ?? cd.IdName : "character";
                player.SendInfo($"<Realm Pass> The {r.Skin} skin is yours: wear it on a {cls} at the Armoire in the Nexus, or pick it for a new {cls}.");
            }
            foreach (var r in rewards.Where(r => r.Aura > 0))
                player.SendInfo($"<Realm Pass> A new aura: {Book.Data.Aura(r.Aura)?.Name}. Wear it from the Looks tab of the Realm Pass (Y).");
            foreach (var r in rewards.Where(r => r.Color > 0))
                player.SendInfo($"<Realm Pass> A new name colour: {Book.Data.Color(r.Color)?.Name}. Wear it from the Looks tab of the Realm Pass (Y).");
        }

        // Rewards as the window shows them (QuestReward's kinds, and the pass's own).
        private List<PassRewardInfo> Rewards(IEnumerable<PassReward> defs)
        {
            var game = _player.GameServer.Resources.GameData;
            var list = new List<PassRewardInfo>();
            foreach (var r in defs)
            {
                if (r.Fame > 0)
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindFame, Amount = r.Fame });
                else if (r.Gold > 0)
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindGold, Amount = r.Gold });
                else if (r.Item != null && game.IdToObjectType.TryGetValue(r.Item, out var t))
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindItem, Item = t, Amount = Math.Max(1, r.Count) });
                else if (r.Skin != null)
                {
                    // the skin's type, and its class's
                    var skin = SkinOf(game, r.Skin);
                    if (skin != null)
                        list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindSkins, Item = skin.Type, Amount = skin.PlayerClassType, Name = r.Skin });
                }
                else if (r.Aura > 0)
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindAura, Item = (ushort)r.Aura, Amount = Book.Data.Aura(r.Aura)?.ColorValue ?? 0, Name = Book.Data.Aura(r.Aura)?.Name ?? "" });
                else if (r.Color > 0)
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindColor, Item = (ushort)r.Color, Amount = Book.Data.Color(r.Color)?.ColorValue ?? 0, Name = Book.Data.Color(r.Color)?.Name ?? "" });
                else if (r.Slot > 0)
                    list.Add(new PassRewardInfo { Kind = PassRewardInfo.KindSlot, Amount = r.Slot });
            }
            return list;
        }

        // The pass window's data.
        public void SendData()
        {
            if (!Connected)
                return;
            var now = QuestTracker.Now;
            var book = Book;
            var data = book.Data;
            var msg = new PassDataMessage { Now = QuestPeriods.Unix(now), Price = data.Price, Gold = _player.Credits };
            var season = book.Season;
            if (season != null && book.Active)
            {
                var def = season.Def;
                msg.Active = true;
                msg.SeasonKey = season.Key;
                msg.SeasonName = season.Name;
                msg.Theme = def.Theme ?? "";
                msg.Accent = PassCosmetic.ParseColor(def.Accent);
                msg.Dark = PassCosmetic.ParseColor(def.Dark);
                msg.Light = PassCosmetic.ParseColor(def.Light);
                msg.StartsAt = QuestPeriods.Unix(season.StartsAt);
                msg.EndsAt = QuestPeriods.Unix(season.EndsAt);
                msg.Xp = book.State.Xp;
                msg.XpPerTier = data.XpPerTier;
                msg.BonusXp = data.BonusXp;
                msg.Gilded = book.State.Gilded;
                msg.Momentum = (byte)(book.Momentum(now) ? data.Momentum : 0);
                msg.PaceTier = (byte)Math.Floor(book.PaceTier(now));
                msg.FirstDungeonReady = book.FirstDungeonReady(now);
                msg.CachesOpened = book.State.CachesOpened;
                for (var t = 0; t < data.Tiers; t++)
                    msg.Tiers.Add(new PassTierInfo
                    {
                        Free = Rewards(def.FreeAt(t)),
                        Gilded = Rewards(def.GildedAt(t)),
                        FreeClaimed = book.State.FreeTaken(t),
                        GildedClaimed = book.State.GildedTaken(t),
                    });
                msg.Cache = Rewards(data.Cache.OrderByDescending(l => l.Weight).SelectMany(l => l.Rewards));
            }
            var xp = data.Xp;
            msg.Sources = new List<(byte, int)>
            {
                ((byte)PassSource.QuestDaily, xp.QuestDaily), ((byte)PassSource.QuestWeekly, xp.QuestWeekly), ((byte)PassSource.QuestMonthly, xp.QuestMonthly),
                ((byte)PassSource.BonusDaily, xp.BonusDaily), ((byte)PassSource.Login, xp.Login), ((byte)PassSource.FirstDungeon, xp.FirstDungeon),
                ((byte)PassSource.Dungeon, xp.Dungeon), ((byte)PassSource.Elder, xp.Elder), ((byte)PassSource.Boss, xp.Boss),
                ((byte)PassSource.Oryx, xp.Oryx), ((byte)PassSource.RealmClosed, xp.RealmClosed), ((byte)PassSource.ArenaWave, xp.ArenaWave),
                ((byte)PassSource.Fame, xp.FamePer),
            };
            msg.DungeonStep = xp.DungeonStep;
            foreach (var a in data.Auras)
                msg.Auras.Add(new PassCosmeticInfo { Id = (byte)a.Id, Name = a.Name ?? "", Color = a.ColorValue, Owned = book.State.Auras.Contains(a.Id) });
            msg.Aura = (byte)book.WornAura;
            foreach (var c in data.Colors)
                msg.Colors.Add(new PassCosmeticInfo { Id = (byte)c.Id, Name = c.Name ?? "", Color = c.ColorValue, Owned = book.State.Colors.Contains(c.Id) });
            msg.Color = (byte)(data.Color(book.State.Color) != null ? book.State.Color : 0);
            msg.CommunityLuck = (byte)Math.Max(0, data.Community.Luck);
            msg.CommunityRolls = (byte)Math.Max(0, data.Community.Rolls);
            var next = data.After(now);
            if (next != null)
            {
                msg.NextName = next.Name;
                msg.NextTheme = next.Def.Theme ?? "";
                msg.NextStartsAt = QuestPeriods.Unix(next.StartsAt);
            }
            _player.Client.SendPacket(msg);
        }
    }
}
