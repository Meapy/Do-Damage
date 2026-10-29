using System;
using System.Linq;
using Shared.champions;
using Shared.database.guild;
using Shared.resources;
using WorldServer.core.champions;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // The guild at a glance: its hall, fame, place on the Hall of Champions, vault and boost.
        internal class GuildInfoCommand : Command
        {
            public override string CommandName => "guildinfo";
            public override string Alias => "ginfo";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var account = player.Client.Account;
                var db = player.GameServer.Database;
                var guild = account.GuildId > 0 ? db.GetGuild(account.GuildId) : null;
                if (guild == null || guild.IsNull)
                {
                    player.SendError("You are not in a guild. Make one at the Guild Register in the Nexus, or ask a guild to invite you.");
                    return false;
                }
                var level = GuildHalls.HallOf(guild.Level);
                player.SendInfo($"{guild.Name}: Guild Hall {level} of {GuildHalls.Levels}, {guild.Members.Length} members. You're {(GuildHalls.RankName(account.GuildRank) is var r && r == "Initiate" ? "an" : "a")} {GuildHalls.RankName(account.GuildRank)}.");
                player.SendSubtext($"   Guild fame: {guild.Fame:N0} to spend, {guild.TotalFame:N0} earned (the fame of every character that died in the guild).");

                var board = ChampionCache.Board;
                foreach (var tab in new[] { ChampionTab.GuildLiving, ChampionTab.GuildDead })
                {
                    var def = ChampionBoards.Of(tab);
                    var place = board != null && board.Boards.Count > (int)tab
                        ? board.Boards[(int)tab].FirstOrDefault(e => string.Equals(e.Name, guild.Name, StringComparison.OrdinalIgnoreCase))
                        : null;
                    player.SendSubtext(place != null
                        ? $"   Hall of Champions: #{place.Rank} on {def.Name} with {place.Value:N0} fame ({def.RewardText(ChampionBoards.TierOf(place.Rank))} for every member)."
                        : $"   Hall of Champions: not in {def.Name}'s top {ChampionBoards.Places} yet ({def.Description[(def.Description.IndexOf(':') + 2)..].TrimEnd('.')}).");
                }

                var stored = DbGuildVault.Stored(db.Conn, guild.Id);
                player.SendSubtext($"   Guild vault: {stored} of {GuildHalls.VaultSlots(level)} slots used ({GuildHalls.VaultChests(level)} chests in the hall). {(GuildHalls.MayTake(account.GuildRank) ? "You can put items in and take them out." : "As an Initiate you can put items in; members take them out.")} /gvault shows who moved what.");
                player.SendSubtext($"   Every member gets +{Math.Round(LootBoosts.Guild * 100, 1)}% loot.");
                var price = GuildHalls.UpgradePrice(level);
                if (price > 0)
                    player.SendSubtext($"   Next: Guild Hall {level + 1} for {price:N0} guild fame ({GuildHalls.Describe(level + 1)}). A leader buys it in the hall.");
                player.SendSubtext("   /ghall goes to the hall, /g talks to the guild, /gwho lists who's online.");
                return true;
            }
        }

        // The guild vault's log: who put in or took out what, the newest first.
        internal class GuildVaultCommand : Command
        {
            public override string CommandName => "guildvault";
            public override string Alias => "gvault";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var account = player.Client.Account;
                if (account.GuildId <= 0)
                {
                    player.SendError("You are not in a guild.");
                    return false;
                }
                var count = int.TryParse(args, out var n) ? Math.Clamp(n, 1, DbGuildVault.LogKept) : 15;
                var log = DbGuildVault.ReadLog(player.GameServer.Database.Conn, account.GuildId, count);
                if (log.Count == 0)
                {
                    player.SendInfo("Nothing has gone in or out of the guild vault yet. Its chests are in the guild hall (/ghall).");
                    return true;
                }
                player.SendInfo($"The guild vault, the last {log.Count} moves (/gvault <count> for more):");
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var (t, name, put, item) in log)
                    player.SendSubtext($"   {Ago(now - t)}: {name} {(put ? "put in" : "took")} {item}");
                return true;
            }

            private static string Ago(long s) => s < 60 ? "just now" : s < 3600 ? $"{s / 60} min ago" : s < 86400 ? $"{s / 3600} h ago" : $"{s / 86400} d ago";
        }
    }
}
