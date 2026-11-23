---
title: Pets
nav: 26
section: Classes and gear
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

A pet heals you wherever you are in the same world, however far it has fallen behind. If you go up
or down a floor (the Sky Bar's stair, a teleport), it comes to you on your floor.

### Levels and the Pen Keeper

**Every pet starts at level 1 and grows as you feed it**, up to its size's last level: 10 for a
Tiny pet, 15 Small, 20 Medium, 25 Large and 30 Huge (the Lil' Oryxes and the boss pets). Along
the way its damage goes from 60% of its kit's to 100%, its healing to 140% of what it heals at
level 1, and it grows a little bigger. **Only Huge pets go further**: up to 120% damage and 160%
healing at level 30, a quarter bigger. A pet keeps its level and size in its generator when you
take it back, and pets from before levels came back with their food (a raised one at its last
level).

**Every pet does 90%** of the damage, healing and bleeding its kit and level make
(since 2.6.4): the numbers on this page, in the tooltips and in the Pen Keeper's window are what
pets really do.

The **Pen Keeper** stands at the gate of the **pet pen**, east of the Herald and the Warcaller on the
Arena island; pets of every kind come and go in the pen behind him. His window shows your pet's
level, the food to its next level, and what it does now and at its last level. Talk to him to:

- **Feed your pet**: pick anything in your inventory or backpack and he says how much food it is
  and what level it takes your pet to, then feed it (a whole stack at once). Pets eat:
  - **treats**: a Superburger 1,500, an Ambrosia 5,000, a Shard of the Mad God 200;
  - **untiered gear** by how rare its bag is: brown 150, pink 200, purple 300, cyan 600, blue
    800, up to 1,500 for the rarer bags, a white bag's 2,000, and Ascended gear 3,000;
  - **tiered gear**: 20 and 15 a tier (abilities and rings 20 and 30 a tier);
  - **potions**: a stat potion 60, a Greater one 150, Life and Mana 200;
  - **other pets' generators**, by size: Tiny 150, Small 400, Medium 1,200, Large 3,000, Huge
    8,000, and half the food a fed one holds.
- **Feeding costs fame**, more the stronger your pet is: 3 for each 100 food it eats at level 1,
  15% more at each level after, and only for the food it uses. Taking a pet all the way costs
  about 185 fame for a Tiny one, 690 Small, 1,780 Medium, 3,710 Large and 6,820 Huge.
- **Grow your pet** once it's at its last level: it becomes the next size up, keeps its level and
  levels on to that size's last. Tiny to Small costs 1,000 fame or 100 gold, Small to Medium 3,000
  or 200, Medium to Large 8,500 or 450, and Large to Huge 25,000 or 1,000. It never gets weaker
  for it: it stays at 100% through Small, Medium and Large, and once it's Huge it climbs on to
  120% at level 30. It's drawn the new size's height, and your pen must take its new size for him
  to feed it.
- **Grow your pen** to feed bigger pets. Every account's pen feeds Tiny pets; each bigger size is
  bought once, for fame or gold: Small 2,000 fame or 200 gold, Medium 5,000 or 400, Large 10,000
  or 700, and Huge (the Lil' Oryxes and the boss pets) 20,000 or 1,000. Any pet can still be put
  in and fights and heals at its level; only feeding needs the pen.
- **Put in a pet**: pick a pet generator, then "Put this pet in". Your pet goes back into its own
  generator, with its level.
- **Take your pet back** into its generator (as `/removepet` does).
- **Give him your Shards of the Mad God.** He keeps them for your whole account (using a stack of
  shards anywhere hands it to him too), feeds them to your pet when you ask, or trades 25 of them
  for an Ambrosia. Ambrosia also comes from the Mystery Box, the death spin and the community goals.

A fed pet's generator shows its level in the corner, and its tooltip shows its size, level, damage and
healing now (in green once it's grown) and at its last level.

### Sizes

Every pet is **Tiny**, **Small**, **Medium**, **Large** or **Huge** (the Lil' Oryxes and the boss
pets), and the Pen Keeper can grow a pet at its last level into the next size. The size decides how hard it hits, how much it heals, its last level and how big it
looks next to you, and the generator's tooltip says which it is. **Every pet heals its own way**:
its size gives about how much HP it heals a second at level 1, a pet that hits hard for its size
heals a little less and a gentle one more, healers (priests, angels, fairies, unicorns...) heal half
again as much, and each pet heals more or less often than the next. **MP is about half its HP**: a
pet restores roughly half as much MP a second as it heals HP (the mages, wisps and djinns a little
more), never more than two thirds of it.

| Size | HP a second at level 1 | Damage a second at level 1 | Last level | Looks | Which pets |
|---|---|---|---|---|---|
| Tiny | about 4 | about 27 | 10 | half your height | the plain critters: cats, pups, foxes, frogs, snails, penguins, birds... |
| Small | about 5.4 | about 68 | 15 | three quarters of your height | the rarer critters and the odd ones: wolves, lions, the Karate Penguins, the Leprechaun, Lil' Ghost... |
| Medium | about 8 | about 135 | 20 | as tall as you | monsters, heroes and gods' look-alikes, and the named pets |
| Large | about 14 | 270 to 325 | 25 | nearly half again as tall as you | the boss pets |
| Huge | about 20 | 540 to 1,620 | 30 | as the Large pets | the Lil' Oryxes, the Black Between's and the Crucible's bosses |

Each pet's card under [Pet generators](#pet-generators) gives its own numbers, at level 1 and at
its last.

The Tomb of the Ancients' Bes, Nut and Geb hit hardest of the Large pets (about 540 a second at
their last level, about 650 grown Huge at level 30), then Lil' Thessal, Horrific Creation and
Pancookie (about 490).

Every Oryx has a **Lil' Oryx** of his own, drawn as him and shooting his shots. Each Oryx drops
his generator in a white bag (1%), and the generator is used up when used. They grow stronger along
the chain (damage a second at level 30; half that at level 1):

| Lil' Oryx | Dropped by | Damage a second |
|---|---|---|
| Lil' Oryx | Oryx the Mad God, in his Chamber | about 1,080 |
| Lil' Wine Cellar Oryx | Oryx the Mad God, in his Wine Cellar | about 1,620 |
| Lil' Celestial Oryx | Oryx the Celestial Mad God | about 2,160 |
| Lil' Infernal Oryx | Oryx the Infernal Mad God | about 2,700 |
| Lil' Ascended Oryx | Oryx the Ascended God | about 3,240 |

Until 27 November 2026, every account can claim a free Lil' Oryx from the quest log's Daily
Rewards tab.

The bosses of the two hardest places do the same: each of [The Black Between](dungeons.html)'s
three bosses and [The Crucible](crucible.html)'s five champions has a **1% chance** to drop a
little copy of itself, in a white bag. It fights as its boss does, with the boss's own shots in
its patterns (weaving volleys, rings that turn, its blast on the enemy and everything round it),
and the champions' pets take on their champion's last form every 10 seconds to throw out its
spiral. Each is as strong as a Lil' Oryx (damage a second at level 30 below; half that at level
1), and the champions' pets help you as well:

| Pet | Dropped by | Damage a second | Also |
|---|---|---|---|
| Lil' Ferrogrim | Ferrogrim, the Iron Warden | about 1,080 | breaks armor; makes you Armored for 2 s every 10 s |
| Lil' Nerissa | Nerissa, the Tidecaller | about 1,170 | slows; heals you over 2 s every 8 s |
| Lil' Pyrrhax | Pyrrhax, the Forge Tyrant | about 1,260 | makes you Berserk for 2 s every 10 s |
| Lil' Chronarch | Chronarch, the Hour Thief | about 1,440 | slows; makes you Speedy for 2 s every 8 s |
| Lil' Vaelith | Vaelith, the Crucible Sovereign | about 1,980 | breaks armor; makes you Damaging for 2 s every 10 s |
| Lil' Vesper | Vesper, the Dusk Star | about 2,430 | slows |
| Lil' Aurel | Aurel, the Dawn Blade | about 2,790 | breaks armor; the Sunwheel every 9.5 s |
| Lil' Mysterious One | The Mysterious One | about 3,240 | breaks armor; his black hole |

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
  Bot armored for 2 seconds every 10 and the Nightmare for 2 seconds every 5, Horrific Creation
  makes you invulnerable for 1 second every 10, and Lil' Malphas, Lil' Cube and Skull Shrine give you +15 Vitality, Wisdom and Attack for 5
  seconds of every 10.
- **Some pets' hits do more**: they break armor (Bes, Cerberus Skull, Lil' Crystal Prisoner, Oni
  Warlord), slow (Scootaloo, Lil' Forgotten King, Lil' Ent), paralyze for a moment (Nut, Lil'
  Medusa) or make enemies
  **bleed**: damage every second for a few seconds, ignoring defense (Zenco, the Sharkman, the Grim
  Reaper, the Doomed Nature Beast, Ember Matriarch, Ember Wyrmling...). A new bleed from your pet
  starts the old one again rather than adding to it.

The generator's tooltip shows the pet's size and level, its damage a second on one enemy, how much
it heals and how often (now, and up to its last level), and anything else it does ("Also").

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

Other players' pets, and their shots (with the rest of their shots), can be faded or hidden in
Options, Visuals, Other players; "Pets' shots" under World makes every pet's shots subtle or hides
them.

## Pet generators

{{v:pet_count}} pets, by size (the Lil' Oryxes first, then Large to Tiny), then strongest first.
"Damage a second" is on one enemy.

{{gen:pets}}

## Summoned helpers

{{v:summon_count}} items that bring a helper beside you where you use them, rather than giving you
a pet that stays. Some are abilities a class wears, and their gear page has their numbers.

{{gen:pet_summons}}
