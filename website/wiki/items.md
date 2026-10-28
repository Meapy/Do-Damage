---
title: Items
nav: 7
parent: classes
sprite: chest
eyebrow: Gear and stuff
summary: Item tiers and their colours, UT and ST items, keys and mystery boxes, your vault and potion storage, trading, the marketplace and the shops.
---
## Tiers and their colours

Weapons, armor, abilities and rings come in **tiers**: the higher the number, the better. Tiered
items drop from monsters all over the realm and from dungeon bosses; the tier is in the item's
name in its tooltip. Weapons and armor go much higher than rings and abilities, so a tier 4 ring
is already a good one.

The edge of an item's slot in your inventory shows what it is:

{{gen:tiers}}

Hover over an item for its tooltip: what it does, its numbers, and how they compare with what
you're wearing (better in green, worse in red).

The [Gear](gear.html) pages list every weapon, ability, armor and ring in the game, by kind and
by class, with their numbers and where they come from.

## UT and ST items

- **Untiered (UT)** items have no tier. They come from specific bosses, each with its own effect
  or stats, and they're often better than tiered items of the same kind, or different in a way
  that suits a class. The [Dungeons](dungeons.html) page lists each boss's notable drops.
- **Set (ST)** items belong to a set of four for one class (weapon, ability, armor and ring).
  **None can be found for now**: no monster drops them and nothing sells them. They're future
  additions, like the untiered items with no source yet ([Gear](gear.html)).
- **Reward** items are given by the staff (their description says "Rewarded by DODMG"): nothing
  drops or sells them.

> **Set bonuses don't work yet.** The game's data lists extra stats for wearing two, three or all
> four pieces of a set, but this server doesn't apply them. Each piece's own stats count as
> normal.

## Loot bags

Loot drops in bags. Their colour says how rare the best thing inside is, from brown for common
loot up to **white**, the rarest: a white bag is announced to everyone. Bags with
good loot are **soulbound**: only you can see and open them, and you need to have done enough of
the monster's damage to get one. The Oryx set, the tier 16 armours, Oryx 4's and Oryx 5's untiered items and
the Sword of Dark Enchantment drop in white bags; Potions of Oryx and Potions of Power in their own special bag.

## Potions

- **Health and magic potions** heal you. They stack up to {{v:potion_stack}} in the HP and MP slots;
  <kbd>F</kbd> and <kbd>V</kbd> drink them.
- **Stat potions** raise a stat for good, up to your class's highest. See
  [Getting stronger](progression.html#stat-potions-and-maxing).
- **Potion storage**, in your vault, keeps stat potions safe: {{v:storage_base}} of each to start,
  {{v:storage_step}} more per upgrade ({{v:storage_cost}} fame).

## Your vault

Your vault is a cellar under the castle, yours alone: `/vault` or the Vault portal west of the
Nexus spawn. In it:

- **Vault chests**, eight slots each, that keep items safe when a character dies. A new account has
  {{v:vault_chests}}; more cost {{v:vault_chest_price}} fame each (click a closed chest in the
  vault), or use a Vault Chest Unlocker.
- **Gift chests**, where things sent to you arrive, such as marketplace purchases. You can take
  items out but not put them in.
- The **Potion Storage**, the Armoire and the Guild Chronicle.
- A portal to the **marketplace**.
- A **Training Dummy** beside the spawn. Shoot it and, 5 seconds after your first hit, the chat
  tells you (and only you) your damage and damage a second, your pet's included. It never dies and
  gives no experience. [Gear statistics](gear-statistics.html) has every class's numbers on it.

**The vault window:** in your vault, the chest button beside Quests in the side bar, or K (it can
be rebound in Options), opens every vault chest at once, numbered as they stand from the spawn
outwards. Drag items between chests, or between a chest and your inventory beside the window; a
click sends an item across (from a chest to your inventory, or from your inventory into the first
free chest slot). Type to search by name: chests with no match are hidden and the window says
which chests hold it. A right-click marks an item's chest with a gold ring and its number.

The Nexus also has four **shared vault chests** in its south yard. Anyone can use them, so treat
them as a place to hand things to friends, not to store them.

A **Backpack** (Nexus Bazaar, 500 fame) gives a character eight more inventory slots; <kbd>B</kbd>
switches between them. Like everything a character carries, it's lost when the character dies.

## Trading and the marketplace

**Trading:** click a player's name (in the nearby list, on the minimap or in chat) and choose
Trade, or type `/trade <name>` (which needs 2 rank stars). Both of you put items in and accept;
your backpack's items show in the trade window too, and what you get can go into your backpack
when your inventory is full. Almost every item can be traded, sold and dropped for other players,
Soulbound ones included. Only admin items and a few overpowered or test items can't: their
tooltips say "Untradeable".

**The marketplace** sells items to other players for fame, even when you're offline. Its portal is
in your vault. Inside, use the market to:

- **search** what's on offer and buy it: what you buy arrives in your vault's gift chests;
- **sell**: list items for a price in fame, for 1 to 24 hours. When one sells, you get the price
  less a 5% tax, and a message says so;
- see **your offers** and take back what hasn't sold.

Untradeable items can't be listed. You can't trade, drop items or go to your vault while you're in
the marketplace.

## The shops

Every shop here sells for fame; there's no way to earn gold.

- The **Nexus Bazaar**, the market wards either side of the Nexus spawn: stat potions, consumables,
  specials, dungeon keys, Elder and Doomed keys, pet generators
  ({{v:pet_price}} fame) and cloth and dyes. Its stalls rotate through each shop's items. Guests
  can't buy.
- `/shop` opens the Doomed Realms shops: weapons, abilities, armor, rings, consumables, dyes, keys
  and pets.
- The Divine Forge in the Nexus buys items for fame (its Sell for fame tab).

The Nexus Bazaar's specials and consumables:

{{gen:shop}}

Dungeon keys and their prices are on the [Dungeons](dungeons.html) page, and stat potions under
[Getting stronger](progression.html#stat-potions-and-maxing).

## Enchanted Keys and mystery boxes

An **Enchanted Key** opens its dungeon like the regular key **and** gives you a **Mystery ST
Crate**, into your inventory (or your gift chests if it's full). The Nexus Bazaar doesn't sell them
any more, as set (ST) pieces can't be found for now; one you already have still works.

Using a **Mystery ST Crate** gives one of these, with these chances:

{{gen:crate}}

- The **Mystery ST Chest** gives any set (ST) piece.
- The Bazaar's **Mystery Dyes** give a random clothing or accessory dye.
- **Lucky Clovers**, the **Loot Drop Potion** and **XP Boosters** start a boost when used
  (`/boosts` shows yours). The **Loot Tier Potion** doesn't do anything yet.

Items whose effect the server can't carry out yet (some boxes, pet skins and the like) say "This
item doesn't work yet." and stay in your inventory, instead of being used up for nothing.

## Keys

A **dungeon key** opens a portal to its dungeon where you use it. Every dungeon that has a key
lists it on the [Dungeons](dungeons.html) page, with where it drops and its price in the Bazaar.
**Incantations** are different: they open the locked portal Oryx 3 or Oryx 4 leaves behind, and
only beside it ([The Oryx chain](endgame.html#incantations)).
