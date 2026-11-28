using WorldServer.utils;
using Shared.database.party;
using Shared.resources;
using System.Linq;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        private void AnnounceDeath(string killer)
        {
            var maxed = GetMaxedStats();
            var deathMessage = $"{Name} ({maxed}/8, {Client.Character.Fame}) has been killed by {killer}!";

            if (maxed >= 6 || Fame >= 1000)
            {
                var worlds = GameServer.WorldManager.GetWorlds();
                foreach (var world in worlds)
                    world.ForeachPlayer(_ => _.DeathNotif(deathMessage));
                return;
            }

            var pGuild = Client.Account.GuildId;

            // guild case, only for level 20
            if (pGuild > 0 && Level == 20)
            {
                var worlds = GameServer.WorldManager.GetWorlds();
                foreach (var world in worlds)
                    world.ForeachPlayer(_ =>
                    {
                        if (_.Client.Account.GuildId == pGuild)
                            _.DeathNotif(deathMessage);
                    });

                World.ForeachPlayer(_ =>
                {
                    if (_.Client.Account.GuildId != pGuild)
                        _.DeathNotif(deathMessage);
                });
            }
            else
                // guild less case
                World.ForeachPlayer(_ => _.DeathNotif(deathMessage));
        }

        private void GenerateGravestone(bool phantomDeath = false)
        {
            var playerDesc = GameServer.Resources.GameData.Classes[ObjectType];

            var maxed = playerDesc.Stats.Where((t, i) => Stats.Base[i] >= t.MaxValue).Count();

            ushort objType;
            int? time = null;
            switch (maxed)
            {
                case 8: objType = 0x072d; break;
                case 7: objType = 0x072c; break;
                case 6: objType = 0x072b; break;
                case 5: objType = 0x072a; break;
                case 4: objType = 0x0729; break;
                case 3: objType = 0x0728; break;
                case 2: objType = 0x0727; break;
                case 1: objType = 0x0726; break;
                default:
                    objType = 0x0725;
                    time = 300000;
                    if (Level < 20)
                    {
                        objType = 0x0724;
                        time = 60000;
                    }
                    if (Level <= 1)
                    {
                        objType = 0x0723;
                        time = 30000;
                    }
                    break;
            }

            var deathMessage = Name + " (" + maxed + "/8, " + Client.Character.Fame + ")";

            var obj = new StaticObject(GameServer, objType, time, true, true, false);
            obj.Move(X, Y);
            obj.Z = Z;
            obj.Name = (!phantomDeath) ? deathMessage : $"{Name} got rekt";
            World.EnterWorld(obj);
        }

        public void Death(string killer, bool rekt = false)
        {
            if (Client.State == ProtocolState.Disconnected || Dead || Ghost)
                return;
            Dead = true;

            if (HandleTestWorld())
                return;

            if (HandleDefeat(killer))
                return;

            if (rekt)
            {
                GenerateGravestone(true);
                ReconnectToNexus();
                return;
            }

            if (TryResurrect(out var saver, out var angel))
            {
                RecordSave(killer, saver, angel);
                ReconnectToNexus();
                return;
            }

            // the replay of the death, before the death's record (a legend's is kept for good)
            var replayData = Replay.Finish(killer);
            if (replayData != null)
                Shared.database.DeathReplays.Save(GameServer.Database.Conn, AccountId, Client.Character.CharId, replayData);

            SaveToCharacter();
            WeeklyStats.Count(Shared.database.leaderboard.DbWeeklyStats.Deaths);
            var rewards = RollDeathRewards();
            GameServer.Database.Death(GameServer.Resources.GameData, Client.Account, Client.Character, FameCounter.Stats, killer, World.GetDisplayName(), rewards);

            GenerateGravestone();
            AnnounceDeath(killer);

            // a client that can stays as a ghost at its grave, watching and chatting
            if (TryLinger(killer))
                return;

            Client.SendPacket(new DeathMessage(AccountId, Client.Character.CharId, killer));
            Client.Disconnect("Death");
        }

        private bool HandleTestWorld()
        {
            if (World is not TestWorld)
                return false;
            GenerateGravestone();
            ReconnectToNexus();
            return true;
        }

        // in a world that sends the defeated to the Nexus (arenas): the character, its gear and
        // fame stay, with no grave, death record or announcement
        // the Gilded pass's Arena save is spent (a player object lasts one world visit, so it's once per game)
        private bool _arenaSaved;

        private bool HandleDefeat(string killer)
        {
            if (!World.DefeatToNexus)
                return false;
            // the Gilded Realm Pass saves its holder once a game in the Arena: back on their feet, not sent out
            if (World is worlds.impl.doomed.DoomedBattleArena && !_arenaSaved && Pass != null && Pass.IsGilded)
            {
                _arenaSaved = true;
                Dead = false;
                Health = Stats[0];
                Mana = Stats[1];
                ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 3000);
                SendInfo("<Realm Pass> The Gilded pass saves you from defeat, this once.");
                StaticLogger.Instance.Info($"{Name} was saved from defeat by {killer} in {World.IdName} by the Gilded Realm Pass");
                return true;
            }
            StaticLogger.Instance.Info($"{Name} was defeated by {killer} in {World.IdName} and sent to the Nexus");
            if (World is worlds.impl.crucible.CrucibleWorld climb)
                climb.Fell(this); // off the climb's place on the leaderboard
            SendInfo(World is worlds.impl.crucible.CrucibleWorld
                ? "You fell in the Crucible and were sent back to the Nexus."
                : "You were defeated in the arena and sent back to the Nexus.");
            ReconnectToNexus();
            return true;
        }

        private void ReconnectToNexus()
        {
            Health = Stats[0];
            Mana = Stats[1];

            Client.Reconnect(new Reconnect()
            {
                Host = "",
                Port = GameServer.Configuration.serverInfo.port,
                GameId = World.NEXUS_ID,
                Name = "Nexus"
            });

            var party = DbPartySystem.Get(Client.Account.Database, Client.Account.PartyId);
            if (party != null && party.PartyLeader.Item1 == Client.Account.Name && party.PartyLeader.Item2 == Client.Account.AccountId)
                party.WorldId = -1;
        }

        // what saved the player (an amulet that resurrects, or the ring whose Guardian Angel did),
        // given up for it
        private bool TryResurrect(out Item saver, out bool angel)
        {
            angel = false;
            for (int slot = 0; slot < 4; slot++)
            {
                var item = Inventory[slot];
                if (item == null || !item.Resurrects)
                    continue;
                Inventory[slot] = null;
                World.ForeachPlayer(_ => _.SendInfo($"{Name}'s {item.DisplayName} breaks and they disappear"));
                saver = item;
                return true;
            }
            // a ring with Guardian Angel (Player.Imprints.cs) saves its wearer the same way, and is lost
            if (HasImprint(3, Imprint.GuardianAngel))
            {
                var ring = Inventory[3];
                Inventory[3] = null;
                Inventory.Data[3] = null;
                World.ForeachPlayer(_ => _.SendInfo($"{Name}'s guardian angel carries them away, and their {ring.DisplayName} is lost"));
                saver = ring;
                angel = true;
                return true;
            }
            saver = null;
            return false;
        }

        // A death saved: its replay, ending with what saved them, and its line in the Legends'
        // Deaths Saved (Shared/database/leaderboard/DbSavedDeaths.cs)
        private void RecordSave(string killer, Item saver, bool angel)
        {
            try
            {
                var db = GameServer.Database.Conn;
                var blow = Replay.LastHit;
                var name = saver?.DisplayName ?? "";
                var replayData = Replay.Finish(killer, ((ushort)(saver?.ObjectType ?? 0), name));
                var id = Shared.database.leaderboard.DbSavedDeaths.NextId(db);
                if (replayData != null)
                    Shared.database.DeathReplays.SaveSaved(db, id, replayData);
                Shared.database.leaderboard.DbSavedDeaths.Insert(db, new Shared.database.leaderboard.DbSavedDeath
                {
                    Id = id,
                    AccountId = AccountId,
                    CharId = Client.Character.CharId,
                    Name = Name,
                    ObjectType = ObjectType,
                    Skin = Skin,
                    Tex1 = Texture1,
                    Tex2 = Texture2,
                    Level = Level,
                    Fame = Fame,
                    Maxed = GetMaxedStats(),
                    Killer = killer,
                    World = World.GetDisplayName(),
                    SaverType = (ushort)(saver?.ObjectType ?? 0),
                    Saver = name,
                    Angel = angel,
                    Blow = blow.Dealt,
                    HpBefore = blow.HpBefore,
                    MaxHp = blow.MaxHp,
                    HasReplay = replayData != null,
                    Time = Shared.Utils.ToUnixTimestamp(System.DateTime.UtcNow)
                });
                if (replayData != null)
                    SendInfo("Your close call was recorded: watch it in Legends, Deaths Saved.");
            }
            catch (System.Exception e)
            {
                StaticLogger.Instance.Error($"{Name}'s saved death not recorded: {e}");
            }
        }
    }
}
