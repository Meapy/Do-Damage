using WorldServer.core.net.handlers;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.inventory;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.objects
{
    partial class Player
    {
        // Walking over a loot bag takes its Health and Magic Potions onto the potion stacks, as
        // dragging them onto a stack would, until the stacks are full. Only plain loot bags the
        // player may take from (theirs or everyone's, within a tile): never vault, gift or
        // special chests, nor anything in the Marketplace or during a trade.
        private void AutoPickupPotions()
        {
            if (Dead || TradeTarget != null || World is DailyQuestWorld)
                return;
            if (HealthPotionStack.Count >= HealthPotionStack.MaxCount && MagicPotionStack.Count >= MagicPotionStack.MaxCount)
                return;

            foreach (var bag in World.Containers.Values)
            {
                if (bag.GetType() != typeof(Container) || bag.DbLink != null || bag.ObjectType == 0x504)
                    continue;
                if (!IMessageHandler.CanTakeFrom(this, bag))
                    continue;

                // take what fits, then count it onto the stacks once the bag has given it up
                var trans = bag.Inventory.CreateTransaction();
                var hp = 0;
                var mp = 0;
                for (var slot = 0; slot < trans.Length; slot++)
                {
                    var item = trans[slot];
                    if (item == null)
                        continue;
                    if (item == HealthPotionStack.Item && HealthPotionStack.Count + hp < HealthPotionStack.MaxCount)
                        hp++;
                    else if (item == MagicPotionStack.Item && MagicPotionStack.Count + mp < MagicPotionStack.MaxCount)
                        mp++;
                    else
                        continue;
                    trans[slot] = null;
                }
                if (hp + mp == 0 || !inventory.Inventory.Execute(trans))
                    continue;
                HealthPotionStack.Count += hp;
                MagicPotionStack.Count += mp;
            }
        }
    }
}
