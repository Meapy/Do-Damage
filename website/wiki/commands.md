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

The **Divine Forge** in the Nexus's south yard combines items into new ones: two of any tiered
weapon, ability, armor or ring make one of the next tier, and Doomed Realms' own recipes make
untiered items and a few potions (a **Potion of Oryx** from one of each of the eight stat
potions). `/forge` takes you there from anywhere. [The Divine Forge](forge.html) has every
recipe and what it costs.

## Shops and selling

- `/shop` lists the shops and `/shop <name>` opens one: weapons, abilities, armor, rings,
  consumables, dyes, keys and pets, for fame.
- The Divine Forge's **Sell for fame** tab buys items: it shows your inventory with what it pays
  for each one, and you pick what to sell. (`/sellfame` points you there.)
- The marketplace (through a portal in your vault) sells to other players for fame.
  See [Items](items.html#trading-and-the-marketplace).
