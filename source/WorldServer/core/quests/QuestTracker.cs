using System;
using System.Collections.Generic;
using System.Linq;
using Shared.quests;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing.quests;
using WorldServer.utils;

namespace WorldServer.core.quests
{
    // A player's quests, daily login rewards and achievements (Shared.quests.QuestBook) in the
    // game: it counts what the player does (the hooks call Event), sends the quest window what it
    // shows and what changes, pays the rewards it claims, and keeps the state on the account
    // (DbAccount.Progress): saved after every claim, every 30 s while it changes, and when the
    // player leaves (Client.Save). One world server holds an account at a time (its lock), so
    // nothing else writes the field meanwhile.
    public sealed class QuestTracker
    {
        public const byte CLAIM_QUEST = 0, CLAIM_BONUS = 1, CLAIM_LOGIN = 2, CLAIM_ACHIEVEMENT = 3, SWAP = 4, COLOR = 5, CLAIM_GIFT = 6;

        private static QuestData _data;
        private static readonly object DataLock = new object();

        // Staff testing only (/questadmin time): hours added to the quests' clock on this server,
        // to try resets and the daily rewards without waiting.
        public static double ClockOffsetHours;
        public static DateTime Now => DateTime.UtcNow.AddHours(ClockOffsetHours);

        public static QuestData Data(GameServer gameServer)
        {
            lock (DataLock)
            {
                if (_data != null)
                    return _data;
                var path = $"{gameServer.Resources.ResourcePath}/data/quests.json";
                try
                {
                    _data = QuestData.Load(path);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Quests: can't read {path}: {e.Message}");
                    _data = new QuestData();
                }
                var items = gameServer.Resources.GameData.IdToObjectType;
                foreach (var problem in _data.Validate(items.ContainsKey))
                    StaticLogger.Instance.Warn($"Quests: {problem}");
                StaticLogger.Instance.Info($"Quests: {_data.Quests.Count} quests, {_data.Login.Count} daily rewards, {_data.Achievements.Count} achievements");
                return _data;
            }
        }

        private readonly Player _player;
        public QuestBook Book { get; private set; }
        // the client asked for the quest window's data (QUESTS_REQUEST): it gets the updates
        public bool Subscribed { get; private set; }
        private bool _dirty;
        private readonly Dictionary<(byte, int), int> _pending = new Dictionary<(byte, int), int>();
        private float _flushIn = 1f, _checkIn = 10f, _saveIn = 30f;

        public QuestTracker(Player player)
        {
            _player = player;
            var acc = player.Client.Account;
            Book = new QuestBook(Data(player.GameServer), QuestState.FromJson(acc.Progress), acc.AccountId);
            if (!Book.State.Seeded)
                Seed();
            if (Book.Refresh(Now))
                _dirty = true;
            // the rank colour chosen in the quest log, in every world (the account holds it too, but
            // a rank's colour can change in the data)
            if (Book.NameColor != 0 && Book.NameColor != player.ColorNameChat)
                player.ColorNameChat = acc.ColorNameChat = Book.NameColor;
            // what the character already has counts (a maxed character, classes at 20)
            Event(new QuestEvent(QuestEvents.MaxedStats) { Value = player.GetMaxedStats() });
            CheckClasses();
        }

        // Can't play the quests: a guest, or the test world.
        private bool Off => _player.Client.Account.Guest || _player.World is TestWorld;

        // Fills in achievements from the account's earlier play, once: its characters' kills, god
        // and Oryx kills, fame and levels, and its classes at level 20.
        private void Seed()
        {
            var acc = _player.Client.Account;
            var db = _player.GameServer.Database;
            long kills = 0, gods = 0, oryx = 0, fame = 0, levels = 0;
            try
            {
                foreach (var id in db.GetAliveCharacters(acc).Concat(db.GetDeadCharacters(acc)).Distinct())
                {
                    var chr = new Shared.database.character.DbChar(acc, id);
                    if (chr.IsNull)
                        continue;
                    if (chr.FameStats != null)
                    {
                        var stats = Shared.FameStats.Read(chr.FameStats);
                        kills += stats.MonsterAssists + stats.GodAssists;
                        gods += stats.GodAssists;
                        oryx += stats.OryxKills;
                    }
                    fame += chr.Fame;
                    levels += Math.Max(0, chr.Level - 1);
                }
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Quests: couldn't read {acc.Name}'s characters for achievements: {e.Message}");
            }
            int Cap(long v) => (int)Math.Min(v, int.MaxValue);
            Book.Seed("kills", Cap(kills));
            Book.Seed("gods", Cap(gods));
            Book.Seed("oryx", Cap(oryx));
            Book.Seed("fame", Cap(fame));
            Book.Seed("levels", Cap(levels));
            Book.State.Seeded = true;
            _dirty = true;
            StaticLogger.Instance.Info($"Quests: {acc.Name}'s achievements start from {kills} kills, {gods} gods, {oryx} Oryx, {fame} fame, {levels} levels");
        }

        // Classes with a character at level 20, ever (the account's class stats, and this one).
        public void CheckClasses()
        {
            var stats = _player.FameCounter?.ClassStats;
            if (stats == null)
                return;
            var classes = new HashSet<ushort>();
            foreach (var key in stats.AllKeys)
                if (ushort.TryParse(key.ToString(), out var type) && stats[type].BestLevel >= 20)
                    classes.Add(type);
            if (_player.Level >= 20)
                classes.Add(_player.ObjectType);
            Event(new QuestEvent(QuestEvents.Classes) { Value = classes.Count });
        }

        public void Event(QuestEvent e)
        {
            if (Off)
                return;
            var notices = new List<QuestNotice>();
            var changes = Book.Apply(e, notices);
            if (changes.Count == 0)
                return;
            _dirty = true;
            foreach (var c in changes)
                _pending[(c.Kind, c.Index)] = c.Progress;
            foreach (var n in notices)
                Notify(n);
        }

        private void Notify(QuestNotice n)
        {
            if (Subscribed)
                _player.Client.SendPacket(new QuestNoticeMessage() { Kind = n.Kind, Title = n.Title, Text = n.Text });
            else
                _player.SendInfo($"{n.Title} {n.Text}");
        }

        public void Tick(ref TickTime time)
        {
            var dt = time.DeltaTime;
            _flushIn -= dt;
            if (_flushIn <= 0)
            {
                _flushIn = 1f;
                if (Subscribed && _pending.Count > 0)
                    _player.Client.SendPacket(new QuestProgress() { Changes = _pending.Select(p => new QuestChange(p.Key.Item1, p.Key.Item2, p.Value)).ToList() });
                _pending.Clear();
            }
            _checkIn -= dt;
            if (_checkIn <= 0)
            {
                // a reset at midnight: new quests, and the new day's reward
                _checkIn = 10f;
                if (Book.Refresh(Now))
                {
                    _dirty = true;
                    if (Subscribed)
                        SendData();
                }
            }
            _saveIn -= dt;
            if (_saveIn <= 0)
            {
                _saveIn = 30f;
                if (_dirty)
                    Save();
            }
        }

        // Onto the account, and to the database.
        public void Save()
        {
            Store();
            _player.Client.Account.FlushAsync();
        }

        // Onto the account only (Client.Save flushes it).
        public void Store()
        {
            if (Off && !_dirty)
                return;
            _player.Client.Account.Progress = Book.State.ToJson();
            _dirty = false;
        }

        // Staff testing (/questadmin reset): everything afresh, achievements not seeded again.
        public void ResetState()
        {
            Book = new QuestBook(Book.Data, new QuestState { Seeded = true }, _player.Client.Account.AccountId);
            Book.Refresh(Now);
            _pending.Clear();
            Save();
            if (Subscribed)
                SendData();
        }

        public void Subscribe()
        {
            Subscribed = true;
            if (Book.Refresh(Now))
                _dirty = true;
            _pending.Clear();
            SendData();
        }

        public int ReadyCount => Off ? 0 : Book.ReadyCount(Now);

        private DateTime _lastClaim;

        public void Claim(byte kind, string id)
        {
            // each claim sends the whole window back: a client can't flood the server with them
            if ((DateTime.UtcNow - _lastClaim).TotalMilliseconds < 150)
                return;
            _lastClaim = DateTime.UtcNow;
            var now = Now;
            ClaimResult result;
            if (_player.Client.Account.Guest)
                result = ClaimResult.Fail("Register your account to collect rewards.");
            else if (_player.World is TestWorld)
                result = ClaimResult.Fail("Not here.");
            else
                result = kind switch
                {
                    CLAIM_QUEST => Book.ClaimQuest(id, now),
                    CLAIM_BONUS => Enum.TryParse<QuestPeriod>(id, true, out var p) ? Book.ClaimBonus(p, now) : ClaimResult.Fail("No such bonus."),
                    CLAIM_LOGIN => Book.ClaimLogin(now),
                    CLAIM_ACHIEVEMENT => Book.ClaimAchievement(id),
                    SWAP => Book.Reroll(id, now),
                    COLOR => int.TryParse(id, out var rank) ? Book.ChooseColor(rank) : ClaimResult.Fail("No such rank."),
                    CLAIM_GIFT => Book.ClaimGift(id, now),
                    _ => ClaimResult.Fail("Unknown request."),
                };
            if (result.Ok)
            {
                if (kind == COLOR)
                {
                    var acc = _player.Client.Account;
                    _player.ColorNameChat = acc.ColorNameChat = Book.NameColor;
                }
                // recorded before anything is paid, so a claim is never paid twice
                Save();
                Pay(result.Rewards);
                StaticLogger.Instance.Info($"Quests: {_player.Name} {Describe(kind)} {id}: {string.Join(", ", result.Rewards)}");
            }
            _player.Client.SendPacket(new QuestResult() { Success = result.Ok, Message = result.Message });
            foreach (var n in result.Notices)
                Notify(n);
            if (Subscribed)
                SendData();
        }

        private static string Describe(byte kind) => kind switch
        {
            CLAIM_QUEST => "claimed quest",
            CLAIM_BONUS => "claimed the bonus",
            CLAIM_LOGIN => "collected the daily reward",
            CLAIM_ACHIEVEMENT => "claimed achievement",
            SWAP => "swapped quest",
            CLAIM_GIFT => "claimed gift",
            _ => "chose name colour",
        };

        // Fame to the account; items to the inventory, or the vault's gift chest when it's full.
        private void Pay(List<RewardDef> rewards)
        {
            var fame = rewards.Sum(r => r.Fame);
            if (fame > 0)
            {
                DoomedWorlds.GiveFame(_player, fame);
                DoomedWorlds.Notify(_player, 0xFFFFC65C, $"+{fame} Fame");
            }
            var data = _player.GameServer.Resources.GameData;
            var gifts = new List<ushort>();
            foreach (var r in rewards.Where(r => r.Item != null))
            {
                if (!data.IdToObjectType.TryGetValue(r.Item, out var type) || !data.Items.TryGetValue(type, out var item))
                    continue;
                for (var i = 0; i < Math.Max(1, r.Count); i++)
                {
                    var slot = _player.Inventory.GetAvailableInventorySlot(item);
                    if (slot != -1)
                        _player.Inventory[slot] = item;
                    else
                        gifts.Add(type);
                }
            }
            if (gifts.Count > 0)
            {
                _player.GameServer.Database.AddGifts(_player.Client.Account, gifts);
                _player.SendInfo(gifts.Count == 1
                    ? "Your inventory is full, so the reward went to the gift chest in your vault."
                    : $"Your inventory is full, so {gifts.Count} rewards went to the gift chest in your vault.");
            }
        }

        // The quest window's data.
        public void SendData()
        {
            var now = Now;
            var data = Book.Data;
            var types = _player.GameServer.Resources.GameData.IdToObjectType;
            List<QuestReward> Rewards(IEnumerable<RewardDef> defs) => defs
                .Select(r => r.Item == null
                    ? new QuestReward { Kind = 0, Amount = r.Fame }
                    : new QuestReward { Kind = 1, Item = types.TryGetValue(r.Item, out var t) ? t : (ushort)0, Amount = Math.Max(1, r.Count) })
                .Where(r => r.Kind == 0 || r.Item != 0)
                .ToList();
            var points = Book.Points;
            var msg = new QuestsData()
            {
                Now = QuestPeriods.Unix(now),
                Rank = (byte)Book.RankOf(points),
                Points = points,
                ColorRank = (byte)(Book.State.ColorRank < 0 ? 255 : Book.State.ColorRank),
                Ranks = data.Ranks.Select(r => new QuestRank { Name = r.Name, Points = r.Points, Color = r.ColorValue }).ToList(),
            };
            foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
            {
                var ps = Book.State.Of(p);
                msg.Periods.Add(new QuestPeriodInfo
                {
                    Period = (byte)p,
                    ResetsAt = QuestPeriods.Unix(QuestPeriods.NextReset(p, now)),
                    RerollsLeft = (byte)Math.Max(0, data.RerollsOf(p) - ps.Rerolls),
                    BonusClaimed = ps.BonusClaimed,
                    Bonus = Rewards(data.BonusOf(p)),
                });
            }
            foreach (var (p, slot, def) in Book.Current())
                msg.Quests.Add(new QuestInfo
                {
                    Period = (byte)p,
                    Id = def.Id,
                    Title = def.Title,
                    Text = def.Describe(),
                    Progress = slot.Progress,
                    Goal = def.Goal,
                    Claimed = slot.Claimed,
                    Rewards = Rewards(def.Rewards),
                });
            var login = Book.State.Login;
            msg.LoginDay = (byte)(data.Login.Count == 0 ? 0 : login.Step % data.Login.Count);
            msg.LoginReady = Book.LoginReady(now);
            // a streak only lasts while it's kept up: yesterday's or today's claim
            msg.LoginStreak = login.LastDay >= QuestPeriods.DayKey(now) - 1 ? login.Streak : 0;
            msg.LoginTotal = login.Total;
            msg.LoginNextAt = QuestPeriods.Unix(QuestPeriods.NextReset(QuestPeriod.Daily, now));
            for (var d = 0; d < data.Login.Count; d++)
                msg.LoginDays.Add((data.IsMilestone(d + 1), Rewards(data.Login[d])));
            foreach (var a in data.Achievements)
            {
                var st = Book.AchievementOf(a.Id);
                var next = Math.Min(st.Claimed, a.Tiers.Length - 1);
                msg.Achievements.Add(new AchievementInfo
                {
                    Id = a.Id,
                    Category = a.Category,
                    Title = a.Title,
                    Text = a.Describe(a.Tiers.Length == 0 ? 0 : a.Tiers[Math.Max(0, next)]),
                    Claimed = (byte)st.Claimed,
                    Progress = st.Progress,
                    Goals = a.Tiers.ToList(),
                    NextPoints = st.Claimed < a.Tiers.Length ? data.TierPointsOf(st.Claimed) : 0,
                    NextRewards = st.Claimed < a.Tiers.Length ? Rewards(data.TierRewardsOf(st.Claimed)) : new List<QuestReward>(),
                });
            }
            foreach (var g in Book.OpenGifts(now))
                msg.Gifts.Add(new QuestGiftInfo
                {
                    Id = g.Id,
                    Title = g.Title,
                    Text = g.Text ?? "",
                    EndsAt = QuestPeriods.Unix(g.EndsAt),
                    Claimed = Book.GiftClaimed(g),
                    Rewards = Rewards(g.Rewards),
                });
            _player.Client.SendPacket(msg);
        }

        // Whether a monster's death counts: not in the Nexus, vaults or minigames (the Arena has
        // its own wave quests; its kills give half loot), not one staff spawned, and only monsters
        // that give experience (the Nexus's XP gifts are out anyway).
        public static bool KillCounts(Enemy enemy) =>
            enemy.World is not (NexusWorld or VaultWorld or TestWorld or DoomedMinigame)
            && !enemy.Spawned && !enemy.GivesNoXp && enemy.ObjectDesc.ExpMultiplier > 0;

        public static QuestEvent KillEvent(Enemy enemy)
        {
            var d = enemy.ObjectDesc;
            return new QuestEvent(QuestEvents.Kill)
            {
                Name = d.IdName,
                God = d.God,
                Boss = d.Quest,
                Oryx = d.Oryx,
                Cube = d.Cube,
                RealmEvent = d.Quest && !d.God && enemy.World is RealmWorld,
            };
        }

        // A dungeon cleared (World.BossDied, once its last boss is down): it counts for each player
        // still in it who was near one of its bosses when it died.
        public static void DungeonCleared(World world)
        {
            var data = Data(world.GameServer);
            if (data.NotDungeons.Contains(world.IdName))
                return;
            var credited = world.Players.Values.Where(p => world.BossCredit.Contains(p.AccountId)).ToList();
            if (credited.Count == 0)
                return;
            var e = new QuestEvent(QuestEvents.Dungeon)
            {
                Name = world.IdName,
                Difficulty = data.DifficultyOf(world.StrongestBossHp),
                Elder = world.IdName.Contains("Elder"),
                Group = credited.Count > 1,
            };
            foreach (var p in credited)
                p.Quests?.Event(e);
        }
    }
}
