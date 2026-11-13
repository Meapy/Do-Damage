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

The **Divine Forge** in the Nexus's smithy, on the market island west of the spawn, combines items into new ones: two of any tiered
weapon, ability, armor or ring make one of the next tier, and Doomed Realms' own recipes make
untiered items and a few potions (a **Potion of Oryx** from one of each of the eight stat
potions). `/forge` takes you there from anywhere. [The Divine Forge](forge.html) has every
recipe and what it costs.

## Visiting a friend

`/visit <name>` asks a friend to let you join them where they are: a dungeon, a stage of Oryx's
chain, the Arena or any other instance a portal leads to. `/summon <name>` works the other way
round: it asks a friend to come to the instance *you're* in. Either way they see a small box in
the top right corner of their screen, "*Name* wants to visit you" or "*Name* wants to summon
you", with **Yes** and **No**; it doesn't stop them moving, shooting or using their ability, and
only the mouse answers it, or they type `/yes` or `/no`. If they say Yes, the one moving goes
straight there, invulnerable for their first 3 seconds; if they don't answer in 30 seconds it counts as No. `/visit cancel` (or
`/summon cancel`) takes your request back.

From a dungeon or instance, `/help` sends summon requests to eligible online players currently in
the Nexus or a realm. `/phelp` and `/ghelp` limit the call to online party or guild members in
those worlds. The `/help` call is announced in chat to everyone online across the world servers and
can be used once a minute; `/phelp` and `/ghelp` still only message their recipients. Requests go
to players on the same world server and follow the usual summon rules. The chat link sends
`/visit <name>` to ask permission to join you, and the message names the dungeon that needs help.
You can ask multiple players at once; each recipient can accept their request, and
`/summon cancel` takes back all outstanding help requests.

- Each player gets into an instance this way **once**, and never back into one they've already
  been in, on any of their characters: dying in a dungeon and visiting (or being summoned) your
  way back in doesn't work.
- Realms, the Nexus, vaults, guild halls and the Daily Quest Room can't be visited or summoned
  into, and nor can a full instance or one made for a single player. For realms, take a realm
  portal from the Nexus or use `/realm2`.
- You can have one `/visit` or `/summon` request waiting at a time; a help call can ask several
  players at once. Wait 10 seconds between calls; a player is asked by one player at a time, and
  after a No (or no answer) you wait a minute before asking them again.
- On a game older than version 2.3.7 there's no box: your friend sees the request in chat and
  answers with `/yes` or `/no` (on 2.3.7 itself, summons come in chat too).

## Shops and selling

- `/shop` lists the shops and `/shop <name>` opens one: weapons, abilities, armor, rings,
  consumables, dyes, keys and pets, for fame. `/shop keys` has every dungeon's key, regular,
  Doomed Realms and Elder (the Elder Ivory Wyvern, Shatters and Lost Halls keys are sold only
  there), and the Oryx's Inferno and Elysium incantations; not the Oryx's Castle and Chamber keys.
  It has more keys than stalls, so the stalls take turns, a few minutes each.
- The Divine Forge's **Sell for fame** tab buys items: it shows your inventory with what it pays
  for each one, and you pick what to sell. (`/sellfame` points you there.)
- The [Marketplace](marketplace.html) sells to other players for fame, from the Market Broker in the
  market square of the Nexus's market island. `/market` takes you there from anywhere, and
  `/sell <slot> <price> [days]` lists an item from your inventory (slots 1-8, 9-16 for the
  backpack) without opening the window.
