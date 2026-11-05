using System;
using System.Linq;
using Shared;
using Shared.database.character.inventory;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class Give : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "give";

            // only letters and digits, in lower case: "mystery key gold" finds "Mystery Key (Gold)"
            private static string Loose(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

            // an item named loosely: the same letters and digits as its id or name, else the only item
            // whose id or name has them in it; else what it might be is suggested
            private static bool FindLoosely(Player player, string name, out ushort objType)
            {
                objType = 0;
                var want = Loose(name);
                var items = player.GameServer.Resources.GameData.Items.Values;
                if (want.Length == 0)
                {
                    player.SendError("Give what?");
                    return false;
                }
                var exact = items.FirstOrDefault(i => Loose(i.ObjectId) == want || Loose(i.DisplayName ?? "") == want);
                if (exact != null)
                {
                    objType = exact.ObjectType;
                    return true;
                }
                var some = items.Where(i => Loose(i.ObjectId).Contains(want) || Loose(i.DisplayName ?? "").Contains(want)).Take(6).ToList();
                if (some.Count == 1)
                {
                    objType = some[0].ObjectType;
                    return true;
                }
                player.SendError(some.Count == 0
                    ? $"Unable to find item: {name}!"
                    : $"Which one? {string.Join(", ", some.Take(5).Select(i => i.ObjectId))}{(some.Count > 5 ? "..." : "")}");
                return false;
            }

            protected override bool Process(Player player, TickTime time, string args)
            {
                var gameData = player.GameServer.Resources.GameData;
                var splitArgs = args.Split(' ');
                int totalAmount = 1;
                string itemName = args;

                if (splitArgs.Length > 1 && int.TryParse(splitArgs[0], out int requestedAmount))
                {
                    totalAmount = requestedAmount;
                    itemName = string.Join(" ", splitArgs.Skip(1));
                }

                // by display name, else by id: some names belong to an object that isn't an item too
                // (Prismimic, an event chest's)
                if (!gameData.DisplayIdToObjectType.TryGetValue(itemName, out ushort objType) || !gameData.Items.ContainsKey(objType))
                {
                    if (!gameData.IdToObjectType.TryGetValue(itemName, out objType) && !FindLoosely(player, itemName, out objType))
                        return false;
                }

                if (!gameData.Items.ContainsKey(objType))
                {
                    player.SendError($"Unable to find item: {itemName}!");
                    return false;
                }

                var item = gameData.Items[objType];

                if (!gameData.IdToObjectType.TryGetValue("Soulbound Loot Bag", out var bagType))
                {
                    bagType = 0x0503;
                }

                int remaining = totalAmount;
                Random rand = new Random();

                while (remaining > 0)
                {
                    int amountInThisBag = Math.Min(remaining, 8);

                    // Ustawiono czas życia na 30000 ms (30 sekund)
                    var container = new Container(player.GameServer, bagType, 30000, true);

                    for (int j = 0; j < amountInThisBag; j++)
                    {
                        if (item.Quantity > 0 && item.QuantityLimit > 0)
                        {
                            container.Inventory.Data[j] = new ItemData()
                            {
                                Stack = item.Quantity,
                                MaxStack = item.QuantityLimit
                            };
                        }
                        container.Inventory[j] = item;
                    }

                    container.BagOwners = new int[] { player.AccountId };

                    // Rozrzut 0.8 dla lepszej widoczności oddzielnych worków
                    float offsetX = (float)((rand.NextDouble() * 2 - 1) * 0.8);
                    float offsetY = (float)((rand.NextDouble() * 2 - 1) * 0.8);

                    container.Move(player.X + offsetX, player.Y + offsetY);
                    container.Z = player.Z;
                    player.World.EnterWorld(container);

                    remaining -= amountInThisBag;
                }

                player.SendInfo($"Gave {totalAmount}x {itemName} in Soulbound Loot Bag(s) (30s lifetime)!");
                return true;
            }
        }
    }
}