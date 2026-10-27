using NLog;
using Shared;
using Shared.resources;
using System;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    public class PlayerShootHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.PLAYERSHOOT;

        protected static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var time = rdr.ReadInt32();
            var bulletId = rdr.ReadInt32();
            var containerType = rdr.ReadInt32();
            var startingPosition = Position.Read(rdr);
            var angle = rdr.ReadSingle();

            var player = client.Player;
            var gameData = player.GameServer.Resources.GameData;
            // a Battle Mage bolt beside the wand's shot (Player.Imprints.cs): not an item
            if (gameData.IdToObjectType.TryGetValue(Player.BattleMageBoltId, out var boltType) && containerType == boltType)
            {
                HandleBolt(client, player, time, bulletId, startingPosition, angle);
                return;
            }
            if (!gameData.Items.TryGetValue((ushort)containerType, out var item))
            {
                client.Disconnect("Attempting to shoot a invalid item", true);
                return;
            }

            var slot = -1;
            var searchRange = client.Account.Admin ? player.Inventory.Length : 2; // Admin przeszukuje wszystko

            for (var i = 0; i < searchRange; i++)
                if (player.Inventory[i] != null && player.Inventory[i].ObjectType == containerType)
                {
                    slot = i;
                    break;
                }

            if (slot == -1)
            {
                client.Disconnect("Attempting to shoot a item that they dont have", true);
                return;
            }

            // every weapon shot the client sends counts toward its volleys, taken or not, as the
            // client counts every one it fires
            if (slot == 0)
                player.NoteWeaponShot(time);
            var desc = player.ShotProjectile(item, slot);

            var newBulletId = player.GetNextBulletId();
            if (newBulletId != bulletId && client.Account.Admin == false) // Jeśli nie jesteś adminem, blokuj
            {
                Log.Warn($"{player.Name} ({player.ObjectDesc.DisplayId ?? player.ObjectDesc.IdName}) has desynced. [bID: {bulletId}, nbID: {newBulletId}");
                player.SkipShotRoll(desc);
                return;
            }

            // Jeśli jesteś adminem i wystąpił desync, musimy zrównać licznik serwera z klientem
            if (newBulletId != bulletId && client.Account.Admin)
            {
                while (newBulletId != bulletId)
                    newBulletId = player.GetNextBulletId();
            }

            // Rate of fire and projectile count checks; admins are exempt, as they can fire
            // items from any inventory slot (see searchRange above). A refused shot still uses up
            // the damage roll the client made for it (SkipShotRoll).
            if (!client.Account.Admin)
            {
                if (!player.IsValidShootTime(time, item, slot))
                {
#if DEBUG
                    Log.Warn($"Invalid shoot time: Time: {time} Rof: {item.RateOfFire}");
#endif
                    player.SkipShotRoll(desc);
                    return;
                }

                if (!player.IsValidNumShots(time, item, slot))
                {
#if DEBUG
                    Log.Warn($"Invalid number of shots: Real NumProjectiles: {item.NumProjectiles}");
#endif
                    //client.Disconnect("Invalid numver of shots detected", true);
                    player.SkipShotRoll(desc);
                    return;
                }
            }

            if (player.Inventory[slot] == null || player.Inventory[slot].ObjectType != containerType)
            {
                client.Disconnect($"Invalid item: {(slot == 0 ? "Weapon" : "Ability")} {player.Inventory[slot].ObjectType} != {containerType}", true);
                return;
            }

            if (slot == 0 && player.World.DisableShooting)
            {
                client.Disconnect("Attempting to shoot in a disabled world", true);
                return;
            }

            if (slot == 1 && player.World.DisableAbilities)
            {
                client.Disconnect("Attempting to activate ability in a disabled world", true);
                return;
            }

            player.PlayerShoot(time, newBulletId, startingPosition, angle, slot, desc);
        }

        private static void HandleBolt(Client client, Player player, int time, int bulletId, Position start, float angle)
        {
            var desc = player.BattleMageBolt(time);
            var newBulletId = player.GetNextBulletId();
            if (newBulletId != bulletId && !client.Account.Admin)
            {
                Log.Warn($"{player.Name} has desynced on a Battle Mage bolt. [bID: {bulletId}, nbID: {newBulletId}");
                player.SkipShotRoll(desc ?? player.BattleMageBoltDesc);
                return;
            }
            while (newBulletId != bulletId)
                newBulletId = player.GetNextBulletId();
            if (desc == null || player.World.DisableShooting)
            {
                // the client rolled its damage all the same
                player.SkipShotRoll(player.BattleMageBoltDesc);
                return;
            }
            player.BattleMageShoot(time, newBulletId, start, angle, desc);
        }
    }
}
