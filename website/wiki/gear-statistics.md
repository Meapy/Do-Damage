---
title: Gear statistics
nav: 9
parent: classes
sprite: sword
eyebrow: Every tier
summary: Every tiered weapon, ability, armor and ring side by side, with what each tier adds over the one below, each class's damage a second with each weapon tier, each class's effective health with each armor tier, and each class's damage measured on the Training Dummy.
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

## Measured on the Training Dummy

The tables above are worked out from the game's data. These numbers were measured in the game,
on the Training Dummy beside the vault spawn, which tells you the damage you did in the 5 seconds
after your first hit. The dummy has **no defense**, so these are higher than the tables above,
which are against 50 defense.

How each class was measured:

- Level 20 with every stat maxed (`/max`), the class's best tiered weapon, ability and armor, and
  the **Reforged Forgotten Crown** as the ring (the same ring for every class).
- Standing on the dummy, so every shot and ability hits it.
- **Weapon only**: firing for 5 seconds. **With 3 abilities**: firing for 5 seconds again, using
  the ability 3 times, with magic potions between casts so mana never runs out.
- **Change** is how much the 3 abilities add to the weapon alone.

Things to know when reading it:

- Each number is one 5-second run, and runs differ by a few percent, so a change of about 5% or
  less is within that.
- Abilities with a cooldown (Rogue, Warrior, Bard, Blademaster and Pirate) can be used only once
  in 5 seconds.
- Damage that lands after the 5 seconds isn't counted: poison, traps, turrets and summons that
  keep hitting afterwards do more than shown.
- Abilities that heal, buff or hide you (Priest, Rogue, Bard, King, Father) add little or
  nothing against the dummy alone. Trickster's and Monk's abilities teleport you off the dummy,
  so they have no number with abilities.
- Ninja's star was tapped, not held.
- Pirate has no tiered cannon, so it used the Doom Cannon.

| Class | Weapon | Ability | Armor | Weapon only | With 3 abilities | Change |
|---|---|---|---|---:|---:|---:|
| Rogue | Dagger of Dire Hatred | Cloak of Emptiness | Leviathan Armor | 3,429 | 3,269 | −5% |
| Archer | Bow of Deep Enchantment | Quiver of Gaia | Leviathan Armor | 4,791 | 5,377 | +12% |
| Wizard | Staff of the Fundamental Core | Spell of Galactic Creation | Robe of the Ancient Intellect | 4,030 | 5,817 | +44% |
| Priest | Wand of Retribution | Tome of Sacred Thought | Robe of the Ancient Intellect | 3,052 | 3,056 | 0% |
| Warrior | Sword of Majesty | Helm of Imperial Right | Annihilation Armor | 4,447 | 5,616 | +26% |
| Knight | Sword of Majesty | Shield of Belligerence | Annihilation Armor | 3,514 | 6,107 | +74% |
| Paladin | Sword of Majesty | Seal of Righteous Victory | Annihilation Armor | 3,814 | 4,700 | +23% |
| Assassin | Dagger of Dire Hatred | Grimhydra Poison | Leviathan Armor | 3,443 | 3,907 | +13% |
| Necromancer | Staff of the Fundamental Core | Sacrilege Skull | Robe of the Ancient Intellect | 3,084 | 3,144 | +2% |
| Huntress | Bow of Deep Enchantment | Mantlecrusher Trap | Leviathan Armor | 4,625 | 5,197 | +12% |
| Mystic | Staff of the Fundamental Core | Realitylapse Orb | Robe of the Ancient Intellect | 2,638 | 4,024 | +53% |
| Trickster | Dagger of Dire Hatred | Prism of Mindscapes | Leviathan Armor | 3,665 | – | – |
| Sorcerer | Wand of Retribution | Scepter of Luminous Winds | Robe of the Ancient Intellect | 3,677 | 3,861 | +5% |
| Ninja | Kusanagi | Circle of Unity | Leviathan Armor | 4,203 | 4,215 | 0% |
| Samurai | Kusanagi | Paragon Wakizashi | Annihilation Armor | 3,613 | 4,768 | +32% |
| Bard | Bow of Deep Enchantment | Nether Lute | Robe of the Ancient Intellect | 4,414 | 4,366 | −1% |
| Blademaster *(Doomed Realms)* | Eclipse Blades | Jacket of Bloodrut Nature | Leviathan Armor | 3,173 | 4,795 | +51% |
| Druid *(Doomed Realms)* | Wand of Retribution | Pearl of Total Chaos | Robe of the Ancient Intellect | 3,077 | 3,458 | +12% |
| Mechanic *(Doomed Realms)* | Dagger of Dire Hatred | Skystorm Turret | Leviathan Armor | 3,675 | 4,478 | +22% |
| Ascendant *(Doomed Realms)* | Bow of Deep Enchantment | Halo of the Archangel | Leviathan Armor | 3,869 | 3,924 | +1% |
| Summoner *(Doomed Realms)* | Staff of the Fundamental Core | Scroll of the Mad God | Robe of the Ancient Intellect | 3,506 | 3,646 | +4% |
| King *(Doomed Realms)* | Staff of the Fundamental Core | Tome of Sacred Thought | Robe of the Ancient Intellect | 4,701 | 4,688 | 0% |
| Palemaster *(Doomed Realms)* | Staff of the Fundamental Core | Sacrilege Skull | Robe of the Ancient Intellect | 3,663 | 3,821 | +4% |
| Pirate *(Doomed Realms)* | Sword of Majesty | Doom Cannon | Leviathan Armor | 5,202 | 5,348 | +3% |
| Sharklord *(Doomed Realms)* | Staff of the Fundamental Core | Tsunami | Robe of the Ancient Intellect | 3,877 | 5,396 | +39% |
| Snowman *(Doomed Realms)* | Dagger of Dire Hatred | Seal of Righteous Victory | Leviathan Armor | 3,580 | 4,594 | +28% |
| Father *(Doomed Realms)* | Wand of Retribution | Tome of Sacred Thought | Robe of the Ancient Intellect | 3,364 | 3,407 | +1% |
| Adept *(Doomed Realms)* | Dagger of Dire Hatred | Spell of Galactic Creation | Leviathan Armor | 3,196 | 4,900 | +53% |
| Monk *(Doomed Realms)* | Fists of the Eternal Master | Heart of the Fountains | Leviathan Armor | 4,754 | – | – |
