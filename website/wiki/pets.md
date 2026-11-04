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
everywhere, fights for you and heals you. A character has one pet at a time.

**A generator is used up when you use it**, but you never lose a pet: using another generator
while you have one gives you back your current pet's generator (in your inventory, or your Gift
Chest when it's full), and `/removepet` sends your pet away and gives you its generator back too.
Using the generator of the pet you already have does nothing, and keeps the generator.

### Sizes

Every pet is **Small**, **Medium** or **Large**. The size decides how much it heals, how hard it
hits and how big it looks next to you, and the generator's tooltip says which it is.

| Size | Heals HP and MP | Damage a second | Looks | Which pets |
|---|---|---|---|---|
| Small | 15 each, every 2 seconds | about 50 | about half your height | the pet shop's critters (cats, pups, penguins, frogs...) and pets like them |
| Medium | 30 each, every 2 seconds | about 200 | a little shorter than you | monsters, heroes and the named pets |
| Large | 45 each, every 1.5 seconds | 400 to 550 | a little taller than you | the boss pets, and Pancookie, Nightmare and the Doomed Nature Beast |

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
- **Pets that shoot** fire real projectiles, sent as your own shots: their hits count as yours for
  loot, experience and quests. So do their melee hits, area hits and bleeds.
- **Boss pets**, pets that look like a boss (Bes, Nut, Geb, the Cube God, Limon, Malphas,
  Septavius, Stheno, Thessal, the Grand Sphinx, Horrific Creation and others), shoot that boss's
  projectiles in its patterns. They're the strongest and most unusual pets.
- **Some pets help you** besides healing: Geb heals you over time for 2 seconds every 5, the
  Companion Priest for 2 seconds every 8, Limon makes you speedy for 2 seconds every 5, the Support
  Bot armored for 2 seconds every 10, Horrific Creation makes you invulnerable for 1 second every
  10, and Lil' Malphas, Lil' Cube and Skull Shrine give you +15 Vitality, Wisdom and Attack for 5
  seconds of every 10.
- **Some pets' hits do more**: Bes's orange boomerangs break armor, as does Cerberus Skull's big
  skull, Nut's small ice shots paralyze for a moment, the Kythal pets' and Nightmare's shots weaken,
  Scootaloo's waves slow, and Zenco, the Sharkman, the Grim Reaper and the Doomed Nature Beast make
  enemies **bleed**: damage every second for a few seconds, ignoring defense. A new bleed from your
  pet starts the old one again rather than adding to it.

The generator's tooltip shows the pet's size, its damage a second on one enemy, how much it heals
and how often, and anything else it does ("Also").

Besides generators, some items **summon a helper** beside you instead: a drake from an egg,
Santa's reindeer, a firecracker. They're listed under [Summoned helpers](#summoned-helpers).

## Where to get one

- The **Nexus Bazaar** sells the {{v:pet_bazaar}} Small pets' generators, for {{v:pet_price}}
  fame each, and `/shop pets` has them too. No other pet is sold.
- **Every other pet drops**, from the monster it belongs with, in a **white bag** of your own:
  each boss's look-alike from that boss (Bes, Nut and Geb in the Tomb of the Ancients, Lil'
  Septavius in the Undead Lair, Lil' Stheno in the Snake Pit, Limon in Sprite World, Lil' Thessal
  in the Ocean Trench, Horrific Creation in the Mad Lab, the Lil' Cube, Skull Shrine and Sphinx
  from their realm events...), and the monsters and heroes from where they live (the Lil' Beholder
  and Lil' Djinn from the godlands' gods, the Sharkman and Sea Mare from Thessal, the Grim Reaper
  and the pumpkin pets from the Haunted Cemetery, the companions from the Lord of the Lost Lands,
  the Hermit God and Daichi...). A dungeon boss's pet drops twice as often in its Elder dungeon.
  Each pet below says what drops it and how often.

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
