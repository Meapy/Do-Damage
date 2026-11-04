---
title: Pets
nav: 10
sprite: potion
eyebrow: Companions
summary: Every pet you can get, its size, how hard it hits, what else it does and where it drops, and the items that summon a helper for a while.
---
## How pets work

A **pet generator** is an item. Use it and that pet joins your character: it follows you
everywhere, fights for you and heals you. A character has one pet at a time.

**A generator is used up when you use it**, but you never lose a pet: using another generator
while you have one gives you back your current pet's generator (in your inventory, or your Gift
Chest when it's full), and `/removepet` sends your pet away and gives you its generator back too.
Using the generator of the pet you already have does nothing, and keeps the generator.

### Sizes

Every pet is **Tiny**, **Small**, **Medium** or **Large**, and each size hits about twice as hard as
the one below it. The size decides how much it heals, how hard it hits and how big it looks next to
you, and the generator's tooltip says which it is.

| Size | Heals HP and MP | Damage a second | Looks | Which pets |
|---|---|---|---|---|
| Tiny | 15 each, every 2 seconds | about 50 | about half your height | the plain critters: cats, pups, foxes, frogs, snails, penguins, birds... |
| Small | 20 each, every 2 seconds | about 125 | a little over half your height | the rarer critters and the odd ones: wolves, lions, the Karate Penguins, the Leprechaun, Lil' Ghost... |
| Medium | 30 each, every 2 seconds | about 250 | a little shorter than you | monsters, heroes and gods' look-alikes, and the named pets |
| Large | 45 each, every 1.5 seconds | 500 to 600 | a little taller than you | the boss pets |

The Tomb of the Ancients' Bes, Nut and Geb hit hardest of the Large pets (about 600 a second), then
Lil' Thessal, Horrific Creation and Pancookie (about 550).

Every Oryx has a **Lil' Oryx** of his own, drawn as him and shooting his shots. Each Oryx drops
his generator in a white bag (1%), and the generator is used up when used. They all heal 60 each
every 1.5 seconds, and grow stronger along the chain:

| Lil' Oryx | Dropped by | Damage a second |
|---|---|---|
| Lil' Oryx | Oryx the Mad God, in his Chamber | about 1,000 |
| Lil' Wine Cellar Oryx | Oryx the Mad God, in his Wine Cellar | about 1,500 |
| Lil' Celestial Oryx | Oryx the Celestial Mad God | about 2,000 |
| Lil' Infernal Oryx | Oryx the Infernal Mad God | about 2,500 |
| Lil' Ascended Oryx | Oryx the Ascended God | about 3,000 |

Until 27 November 2026, every account can claim a free Lil' Oryx from the quest log's Daily
Rewards tab.

### What they do

- **Every pet fights its own way.** The critters scratch, bite, spit, peck, throw bones, eggs,
  acorns or coins, and some do more: the snails' and the Snowy Owl's shots slow, the Grey Pup's
  bite, the Grey Fox's and the Bee's sting make enemies bleed, and a few hit everything around their
  target: the Lion's roar, the Elephant's stomp (it slows), the Skunk's stink cloud (enemies can't
  heal) and the Jelly's zap (it stops them for a moment). The monsters and heroes have their own
  shots too: the Companion Ninja's stars, the Beholder's ring of eye beams, the Grim Reaper's
  scythe that comes back, King Slime's slowing splash, Shroom's sickening spores and more.
- **Boss pets and monster pets** shoot what the monster they look like shoots: Bes, Nut and Geb,
  Lil' Thessal, the Grand Sphinx and the other boss pets fire their boss's projectiles in its
  patterns, the Lil' Puppet Master throws jester bolts and zombie blasts, Lil' Davy Jones slashes
  and spins slowing ghost blades, Lil' Esben freezes everything round its target, Lil' Medusa's
  gaze stops enemies for a moment, Lil' Werewolf's bite bleeds them, and so on.
- **Pets that shoot** fire real projectiles, sent as your own shots: their hits count as yours for
  loot, experience and quests. So do their melee hits, area hits and bleeds.
- **Some pets help you** besides healing: Geb heals you over time for 2 seconds every 5, the
  Companion Priest for 2 seconds every 8, Limon makes you speedy for 2 seconds every 5, the Support
  Bot armored for 2 seconds every 10, Horrific Creation makes you invulnerable for 1 second every
  10, and Lil' Malphas, Lil' Cube and Skull Shrine give you +15 Vitality, Wisdom and Attack for 5
  seconds of every 10.
- **Some pets' hits do more**: they break armor (Bes, Cerberus Skull, Lil' Crystal Prisoner, Oni
  Warlord), slow (Scootaloo, Lil' Forgotten King, Lil' Ent), weaken (the Kythal pets, Nightmare,
  Abyssal Krakenling, Kitsune Spirit), paralyze for a moment (Nut, Lil' Medusa) or make enemies
  **bleed**: damage every second for a few seconds, ignoring defense (Zenco, the Sharkman, the Grim
  Reaper, the Doomed Nature Beast, Ember Matriarch, Ember Wyrmling...). A new bleed from your pet
  starts the old one again rather than adding to it.

The generator's tooltip shows the pet's size, its damage a second on one enemy, how much it heals
and how often, and anything else it does ("Also").

Besides generators, some items **summon a helper** beside you instead: a drake from an egg,
Santa's reindeer, a firecracker. They're listed under [Summoned helpers](#summoned-helpers).

## Where to get one

- **Every pet drops**, from the monster it belongs with, in a **white bag** of your own: the
  critters from the realm's animals and beasts (the cats from the were-cats, the foxes from the
  wargs, the penguins in the Ice Cave...), the Small pets from the gods, heroes and dungeon monsters
  they're like (the Karate Penguins from Kage Kami, the Leprechaun from the Lucky Ent God, the Bee
  from the Hive's Queen Bee...), and the Medium and Large pets from the god, boss or event they
  look like. A dungeon boss's pet drops twice as often in its Elder dungeon. Each pet below says
  what drops it and how often.
- The **Nexus Bazaar** sells the {{v:pet_bazaar}} Tiny pets' generators, for {{v:pet_price}} fame
  each, and `/shop pets` has them too. No other pet is sold.

Other players' pets, and their shots, can be faded or hidden in Options, Visuals.

## Pet generators

{{v:pet_count}} pets, by size (the Lil' Oryxes first, then Large to Tiny), then strongest first.
"Damage a second" is on one enemy.

{{gen:pets}}

## Summoned helpers

{{v:summon_count}} items that bring a helper beside you where you use them, rather than giving you
a pet that stays. Some are abilities a class wears, and their gear page has their numbers.

{{gen:pet_summons}}
