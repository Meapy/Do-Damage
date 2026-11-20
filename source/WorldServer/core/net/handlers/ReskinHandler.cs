using Shared;
using System;
using System.Linq;
using WorldServer.networking;
using WorldServer.core.worlds;

namespace WorldServer.core.net.handlers
{
    internal class ReskinHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.RESKIN;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var skinType = (ushort)rdr.ReadInt32();

            if (client.Player == null)
                return;

            var player = client.Player;
            var gameData = client.GameServer.Resources.GameData;

            client.Account.Reload("skins"); // get newest skin data

            var ownedSkins = client.Account.Skins;
            var currentClass = player.ObjectType;

            var skinData = gameData.Skins;
            var skinSize = 100;

            if (skinType != 0)
            {
                skinData.TryGetValue(skinType, out var skinDesc);

                if (skinDesc == null)
                {
                    player.SendError("Unknown skin type.");
                    return;
                }

                if (skinDesc.PlayerClassType != currentClass)
                {
                    player.SendError("Skin is for different class.");
                    return;
                }

                // The Armoire sells the skins the account lacks, as the character list does
                // (/account/purchaseSkin): not the Realm Pass's, and only once the class has
                // reached the skin's level. Older clients only ever send skins they own.
                if (!ownedSkins.Contains(skinType) && !Buy(client, skinDesc))
                    return;

                skinSize = skinDesc.Size;
            }

            // set skin
            player.Skin = skinType;
            player.Size = skinSize;
        }

        private static bool Buy(Client client, Shared.resources.SkinDesc skinDesc)
        {
            var player = client.Player;
            var acc = client.Account;

            if (skinDesc.PassSkin)
            {
                player.SendError("Only the Realm Pass gives this skin.");
                return false;
            }

            var best = player.FameCounter?.ClassStats?[skinDesc.PlayerClassType].BestLevel ?? 0;
            if (skinDesc.UnlockLevel > Math.Max(best, player.Level))
            {
                player.SendError($"Reach level {skinDesc.UnlockLevel} with this class to buy this skin.");
                return false;
            }

            acc.Reload("credits");
            if (skinDesc.Cost > acc.Credits || !client.GameServer.Database.PurchaseSkin(acc, skinDesc.Type, skinDesc.Cost))
            {
                player.Credits = acc.Credits;
                player.SendError("Not enough gold for this skin.");
                return false;
            }

            player.Credits = acc.Credits;
            player.SendInfo($"Skin bought for {skinDesc.Cost} gold.");
            return true;
        }
    }
}
