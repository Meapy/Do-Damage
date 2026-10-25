---
title: Loot Simulation
nav: 5
sprite: chest
eyebrow: What drops
summary: Roll kills of Oryx 3, Oryx 4 and Oryx 5 and see what you'd get, with your share of the damage and your loot boosts: every drop's chance, the bags a kill gives, and a simulator. Built from the game's data.
---
What **Oryx 3**, **Oryx 4** and **Oryx 5** drop, read straight from the server, and a simulator
that rolls their kills the way the server does. Pick a boss, set how much of the damage you do and
which loot boosts you have, then roll.

{{gen:loot_sim}}

## How loot is rolled

- **Every row is its own roll**, for each player, every kill. Rolling a Potion of Life doesn't
  change your chance of a Potion of Defense.
- **Your damage share counts.** Each chance is multiplied by 1 + your share of the damage + your
  boosts: the Loot Drop Potion, the weekend boost and any loot event. A 20% share makes every chance
  1.2 times as high. A chance never goes over 100%.
- **Thresholds:** a row only rolls for you once your share of the damage is over its threshold:
  1% for the sure drops, 3% for the potions and rare items, 5% for the gear.
- **Tier rows** (a T14 weapon, a T6 ability) give one item of that tier and kind when they hit,
  each about as likely as the others; the Penetrating Blast Spell is a quarter as likely as a T6
  ability.
- **Bags:** your items from a kill go into bags of 8, and every bag from that kill takes the
  colour of the best item in it, so one white-bag item turns its potions' bag white too.
- The seasonal drops bosses add in January and May aren't shown, nor the Arena, where Oryx 3, 4
  and 5 drop about a quarter of their usual loot.
