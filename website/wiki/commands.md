---
title: Commands
nav: 2
parent: getting-started
sprite: bolt
eyebrow: Getting started
summary: Every chat command a player can use, read from the game's code.
---
Type a command in chat (<kbd>Enter</kbd>), starting with a slash. `/commands` in game lists the
ones you can use. This list is taken from the server's code, so it's the same list.

## Every player command

{{gen:commands}}

## /gear in detail

`/gear` equips starter gear for your class: a weapon, an ability, armor and a ring from about half
way up the regular tiers, never above tier {{v:gear_max_tier}}, and puts {{v:gear_boosters}} in
your backpack. It works once per character, only in the Nexus and only below level
{{v:gear_level}}. It never replaces an untiered item or anything as good or better, and the
boosters and whatever it replaces go to your inventory, which needs room for them.
[Classes](classes.html) shows what each class gets.

## Forging

The **Divine Forge** combines items into new ones, Doomed Realms style. It stands in the middle of
the class memorial, in the Nexus's south yard west of the arena portals. Stand by it and open its
window: browse the recipes by what they make (weapons, abilities, armor, rings, the rest, then by
type), search them, or show only what you can make with what you carry. Pick a recipe, put its
ingredients in from your inventory, and forge it for fame; the new item takes the first
ingredient's place in your inventory. Every tiered weapon, ability, armor and ring can be forged
from two of the tier below, for at most 2,000 fame. Wearing a Forge Amulet halves the price and unlocks a few
advanced recipes, but it breaks when used. `/forge` and `/forgelist` now just point you there.

One recipe worth knowing: a **Potion of Oryx** is forged from one of each of the eight stat
potions.

## Shops and selling

- `/shop` lists the shops and `/shop <name>` opens one: weapons, abilities, armor, rings,
  consumables, dyes, keys and pets, for fame.
- The Divine Forge's **Sell for fame** tab buys items: it shows your inventory with what it pays
  for each one, and you pick what to sell. (`/sellfame` points you there.)
- The marketplace (through a portal in your vault) sells to other players for fame.
  See [Items](items.html#trading-and-the-marketplace).
