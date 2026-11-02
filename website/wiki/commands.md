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
way up the regular tiers, never above tier {{v:gear_max_tier}}, and uses {{v:gear_boosters}} for
you at once, so they start working straight away. It works once per character, only in the Nexus
and only below level {{v:gear_level}}. It never replaces an untiered item or anything as good or
better, and whatever it replaces goes to your inventory, which needs room for it. The Divine
Forge doesn't take starter gear: two of it don't make the next tier.
[Classes](classes.html) shows what each class gets.

## Forging

The **Divine Forge** in the Nexus's forge court, in the village east of the spawn, combines items into new ones: two of any tiered
weapon, ability, armor or ring make one of the next tier, and Doomed Realms' own recipes make
untiered items and a few potions (a **Potion of Oryx** from one of each of the eight stat
potions). `/forge` takes you there from anywhere. [The Divine Forge](forge.html) has every
recipe and what it costs.

## Visiting a friend

`/visit <name>` asks a friend to let you join them where they are: a dungeon, a stage of Oryx's
chain, the Arena or any other instance a portal leads to. They see a small box in the top right corner of their screen,
"*Name* wants to visit you", with **Yes** and **No**; it doesn't stop them moving, shooting or
using their ability, and only the mouse answers it, or they type `/yes` or `/no`. If they say Yes you go straight to them; if
they don't answer in 30 seconds it counts as No. `/visit cancel` takes your request back.

- You can visit each instance **once**, and never back into one you've already been in, on any of
  your characters: dying in a dungeon and visiting your way back in doesn't work.
- Realms, the Nexus, vaults, guild halls and the Daily Quest Room can't be visited, and nor can
  a full instance or one made for a single player. For realms, take a realm portal from the Nexus or use `/realm2`.
- You can have one request waiting at a time and ask again after 10 seconds; a player is asked by
  one visitor at a time, and after a No (or no answer) you wait a minute before asking them again.
- On a game older than version 2.3.7 there's no box: your friend sees the request in chat and
  answers with `/yes` or `/no`.

## Shops and selling

- `/shop` lists the shops and `/shop <name>` opens one: weapons, abilities, armor, rings,
  consumables, dyes, keys and pets, for fame.
- The Divine Forge's **Sell for fame** tab buys items: it shows your inventory with what it pays
  for each one, and you pick what to sell. (`/sellfame` points you there.)
- The [Marketplace](marketplace.html) sells to other players for fame, from the Market Broker in the
  market of the Nexus's village. `/market` takes you there from anywhere, and
  `/sell <slot> <price> [days]` lists an item from your inventory (slots 1-8, 9-16 for the
  backpack) without opening the window.
