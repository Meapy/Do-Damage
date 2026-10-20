---
title: Gear statistics
nav: 9
parent: classes
sprite: sword
eyebrow: Every tier
summary: Every tiered weapon, ability, armor and ring side by side, with what each tier adds over the one below, each class's damage a second with each weapon tier, and each class's effective health with each armor tier.
---
Every tiered item in the game, side by side, so you can see what each tier adds over the one
below. The numbers are read from the game's own data, so they stay true as items change. For
where each item comes from, and the untiered and set items, see [Gear](gear.html).

## How to read the tables

- **Avg** is the middle of a weapon's damage range, for one shot.
- **Output** is Avg × shots × rate of fire: a weapon's damage over time before your Attack and
  Dexterity, handy for comparing tiers of the same kind.
- **+/−** is the change from the tier below (the average of that tier, when two items share it).
- **Range** is in tiles.
- **Points** adds up every stat an item gives.

## Tiered gear

{{gen:tier-tables}}

## Damage a second by class

What each class does with each tier of its weapon, wearing the same tier of its armor, at its
highest **Attack** and **Dexterity**. The Attack and Dexterity on the weapon and the armor count
too (leather armor's Dexterity, robes' Attack). Against a monster with **50 defense**, with every
shot hitting. Hover a number to see the weapon and armor.

How it works out:

- A shot does its rolled damage × (0.5 + 1.5 × Attack ÷ 75).
- A monster takes that less its defense, but always at least a tenth of it. Dual Blades ignore
  defense, so they always do their full damage.
- You fire (1.5 + 6.5 × Dexterity ÷ 75) times a second, times the weapon's rate of fire.

Abilities, rings, pets and effects such as Damaging and Berserk aren't counted. There's no T0
armor, so the T0 column has none.

{{gen:class-dps}}

## Effective health by class

How much damage each class can take before dying, at its highest **health** and **defense**,
with each tier of its armor and nothing else: no ring, no pet, no healing. Armor that gives
health counts that too. Hover a number to see the armor.

Defense takes a flat amount off every hit, but a hit always does at least 15% of its damage, so
defense counts for more against small hits than against big ones. Effective health is your
health × the hit ÷ the damage you take from it.

{{gen:class-ehp}}

## Class totals at the top tier

Every class with its best tiered gear, a T14 weapon and T15 armor (no weapon goes past T14),
worked out as in the two tables above: **DPS** against 50 defense and **EHP** against hits of 100.

- **Per tile** divides DPS and EHP by the weapon's range in tiles: what a class gets for each
  tile of reach.
- **Score** puts DPS, EHP and range together, so every class can be compared on one number:
  √(DPS × EHP × range), as a share of the best class's. Range counts, but less and less (twice
  the reach is worth about 41% more), which leaves the kinds of weapon about even, so what stands
  out is the class. A class that hits very hard but dies fast scores lower than one that is fair
  at both. Abilities aren't counted, so read it as a guide.
- **vs top** is how far each class is below the best class in that column (bold).

{{gen:class-totals}}
