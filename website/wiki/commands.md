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

`/forge` combines items into new ones, Doomed Realms style. Put the ingredients in your inventory,
type `/forge` with their slots (1 to 8, or 9 to 16 for the backpack), and the game says what you'd
make and what it costs; then `/yes` or `/no`. `/forgelist` shows what you can make with what you
have, `/forgelist <item>` searches the recipes and `/forgelist all` lists every one. Wearing a
Forge Amulet halves the price and unlocks a few advanced recipes, but it breaks when used.

One recipe worth knowing: a **Potion of Oryx** is forged from one of each of the eight stat
potions.

## Shops and selling

- `/shop` lists the shops and `/shop <name>` opens one: weapons, abilities, armor, rings,
  consumables, dyes, keys and pets, for fame.
- `/sellfame <slot> ...` sells items for fame.
- The marketplace (through a portal in your vault) sells to other players for fame.
  See [Items](items.html#trading-and-the-marketplace).
