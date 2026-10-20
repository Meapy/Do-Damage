---
title: Pets
nav: 10
parent: classes
sprite: potion
eyebrow: Gear and stuff
summary: Every pet in the game: the pet generators and what each pet does, the items that summon a helper for a while, and the pet yard's pets.
---
## How pets work

A **pet generator** is an item. Use it and that pet joins your character: it follows you
everywhere, fights for you and heals you. A character has one pet at a time; using another
generator swaps it, and the generator isn't used up, so you can switch back. `/removepet` sends
your pet away.

### Sizes

Every pet is **Small**, **Medium** or **Large**. The size decides how much it heals and how big it
looks next to you, and the generator's tooltip says which it is.

| Size | Heals HP and MP | Looks | Which pets |
|---|---|---|---|
| Small | 15 each, every 2 seconds | about half your height | the pet shop's critters (cats, pups, penguins, frogs...) and pets like them |
| Medium | 30 each, every 2 seconds | a little shorter than you | monsters, heroes, the named pets, and pets with plain shots of their own (Kythal, Zenco, Shroom, Cerberus Skull) |
| Large | 45 each, every 1.5 seconds | a little taller than you | the boss pets, and Pancookie and Nightmare |

Lil' Oryx is an admin pet: it heals 200 each every 0.8 seconds, shoots like Oryx the Mad God and
can't be traded.

### What they do

- **Pets that shoot** fire real projectiles, sent as your own shots: their hits count as yours for
  loot, experience and quests. So do their melee hits and bleeds.
- **Boss pets**, pets that look like a boss (Bes, Nut, Geb, the Cube God, Limon, Malphas,
  Septavius, Stheno, Thessal, the Grand Sphinx, Horrific Creation and others), shoot that boss's
  projectiles in its patterns. They're the strongest and most unusual pets.
- **Some pets help you** besides healing: Geb heals you over time for 2 seconds every 5, Limon
  makes you speedy for 2 seconds every 5, Horrific Creation makes you invulnerable for 1 second
  every 10, and Lil' Malphas, Lil' Cube and Skull Shrine give you +15 Vitality, Wisdom and Attack
  for 5 seconds of every 10.
- **Some pets' hits do more**: Bes's orange boomerangs break armor, Nut's small ice shots
  paralyze for a moment, the Kythal pets' and Nightmare's shots weaken, Scootaloo's waves slow, and
  Zenco and the Doomed Nature Beast make enemies **bleed**: damage every second for a few seconds,
  ignoring defense. A new bleed from your pet starts the old one again rather than adding to it.

The generator's tooltip shows the pet's size, its damage a second on one enemy, how much it heals
and how often, and anything else it does ("Also").

Besides generators, some items **summon a helper** beside you instead: a drake from an egg,
Santa's reindeer, a firecracker. They're listed under [Summoned helpers](#summoned-helpers).

## Where to get one

- The **Nexus Bazaar** sells {{v:pet_bazaar}} of the pet generators, for {{v:pet_price}} fame
  each.
- `/shop` has a pet shop.
- Some monsters drop generators: each pet below says which.

Other players' pets, and their shots, can be faded or hidden in Options, Visuals.

## Pet generators

{{v:pet_count}} pets, by size (Large first), then strongest first. "Damage a second" is on one enemy.

{{gen:pets}}

## Summoned helpers

{{v:summon_count}} items that bring a helper beside you where you use them, rather than giving you
a pet that stays. Some are abilities a class wears, and their gear page has their numbers.

{{gen:pet_summons}}

## The pet yard's pets

The game's data also has the classic pet yard's {{v:yard_count}} pets, in {{v:yard_families}}
families, and {{v:skin_unlockers}} items that unlock their skins.

> **The pet yard isn't open on this server.** These pets can't be hatched or fed, and using a
> skin unlocker says "This item doesn't work yet." without using it up. They're listed here so
> you know what they are.

{{gen:pet_yard}}
