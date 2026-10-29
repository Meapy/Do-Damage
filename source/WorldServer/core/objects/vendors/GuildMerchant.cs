using Shared.database.guild;
using Shared.resources;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.objects.vendors
{
    // The Guild Hall Upgrade in each hall but the last (Guild Hall Upgrade 1-3, 0x0736-0x0738):
    // a guild leader buys the next hall with guild fame (GuildHalls.UpgradePrice). Everyone in the
    // hall moves into the new one at once, its vault with them.
    internal class GuildMerchant : SellableObject
    {
        private readonly int _level;
        private static readonly ushort[] HallTypes = { 0x0736, 0x0737, 0x0738 };

        public GuildMerchant(GameServer manager, ushort objType) : base(manager, objType)
        {
            Currency = CurrencyType.GuildFame;
            Price = int.MaxValue; // just in case for some reason the type isn't found
            RankRequired = 0;

            for (var i = 0; i < HallTypes.Length; i++)
                if (objType == HallTypes[i])
                {
                    _level = i + 1;
                    Price = GuildHalls.UpgradePrice(_level);
                }
        }

        public override void Buy(Player player)
        {
            var db = player.GameServer.Database;
            var account = db.GetAccount(player.AccountId);
            var guild = account == null ? null : db.GetGuild(account.GuildId);

            if (guild == null || guild.IsNull || !(player.World is GuildHallWorld hall) || hall.GuildId != guild.Id)
            {
                Refuse(player, "This isn't your guild's hall.");
                return;
            }
            if (!GuildHalls.MayUpgrade(account.GuildRank))
            {
                Refuse(player, "Only the guild's leaders can upgrade its hall.");
                return;
            }
            var level = GuildHalls.HallOf(guild.Level);
            if (level != _level || level >= GuildHalls.Levels)
            {
                Refuse(player, "This hall has already been upgraded.");
                return;
            }
            if (guild.Fame < Price)
            {
                Refuse(player, $"Not enough guild fame: the upgrade costs {Price:N0}, the guild has {guild.Fame:N0}.", 9);
                return;
            }

            if (!db.ChangeGuildLevel(guild, level))
            {
                Refuse(player, "The upgrade failed. Nothing was spent.");
                return;
            }
            db.UpdateGuildFame(guild, -Price);

            player.Client.SendPacket(new networking.packets.outgoing.BuyResultMessage
            {
                ResultString = $"Guild Hall {level + 1}! Moving everyone in.",
                Result = 0
            });
            GuildHallWorld.Announce(player.GameServer, guild.Id,
                $"{player.Name} upgraded {guild.Name}'s hall to Guild Hall {level + 1}: {GuildHalls.Describe(level + 1)}.");
            hall.MoveTo(level + 1);
        }

        private static void Refuse(Player player, string why, int result = 1) =>
            player.Client.SendPacket(new networking.packets.outgoing.BuyResultMessage { ResultString = why, Result = result });
    }
}
