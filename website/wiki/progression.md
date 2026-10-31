---
title: Getting stronger
nav: 3
parent: getting-started
sprite: potion
eyebrow: Progress
summary: Levels and experience, fame and rank stars, stat potions and maxing, the realm, the Godlands, quests and events, and what happens when you die.
---
## Levels and experience

Killing monsters gives experience to every player nearby, not only the one who landed the last
shot. Levels go from 1 to **20**; it takes {{v:level20_xp}} experience to reach 20. Each level
raises your stats a little.

- One kill gives at most a tenth of what your next level needs, or half if it's your quest
  monster. That's why low-level characters gain more from a crowd of weak monsters than from one
  big one.
- **Elder** monsters (in the Elder dungeons) give twice the experience, and twice that cap.
- An **XP Booster** gives {{v:xp_boost}} more experience until level 20 (`/boosts` shows yours).
  `/gear` gives every new character one.
- The Support Bot's **XP gifts** in the Nexus are free experience: see
  [Getting started](getting-started.html#the-support-bot).

## Fame, rank stars and class quests

Every 1,000 experience a character earns is one **fame** on that character, and experience keeps
counting after level 20. At level 20 every kill gives 10% more experience, so more fame. A kill
usually gives at most a tenth of a level's experience (half for your quest), but at level 20 a
boss (Oryx and his chain, realm events, dungeon bosses) gives its whole experience twice over, up
to 50 fame: Oryx 2 gives about 15 fame. At level 20 the Arena and the Admin Arena give three
quarters of the usual experience for a kill, so three quarters of the fame (levelling there is as
fast as anywhere). When the character dies, its fame, plus bonuses (for your equipment, for
beating your best with that class and so on), is added to your account. Account fame is what you
spend in the Nexus Bazaar, the shops, the marketplace and on upgrades.

Each class has {{v:star_count}} **class quests**: having a character of that class reach
{{v:stars}} fame. Fame counts while the character is alive, so you don't have to die to complete
them. Every class quest you complete is one **rank star**, so with {{v:class_count}} classes there
are {{v:max_stars}} stars in all. Your star's colour moves through nine colours as you go:
light blue, blue, violet, red, red-orange, orange, gold, pale yellow and, with every class quest
done, white.

- The character screen shows your rank (hover over it for the colours) and each character's next
  class quest.
- Your star shows next to your name in chat and over your head.
- Trading with `/trade` needs 2 stars.

## Stat potions and maxing

Each class has a highest value for each stat. The [Classes](classes.html) page lists them.
Stat potions raise one stat for good:

{{gen:stat-potions}}

A character with every stat at its highest is "maxed" (8/8). A potion drunk on a maxed stat is
wasted, so keep spares in your **potion storage**, in your vault: it holds {{v:storage_base}} of
each potion to start, and each upgrade ({{v:storage_cost}} fame) adds {{v:storage_step}} more. From
its window you can store, take out, drink, or drink just enough to max a stat.

**Infernal potions**, forged at the Divine Forge from an Infernal shard and 100 of a stat's potions
(see [Forge](forge.html#forge-shards-and-the-forge-storage)), go further: drunk with that stat
maxed, one adds what the stat's own potion adds past your class's highest (5 Life or Mana, 1 of
the others), up to five of them: 5 Defense, say, or 25 Life over. A stat past its highest shows in
orange rather than gold, and stays there.

A **Potion of Max** Life, Mana, Attack, Defense, Speed, Dexterity, Vitality or Wisdom (from the
Elder dungeons' bosses; Max Life from the Elder Tomb's) fills that stat to your class's highest at
once; if it's already there, it puts 6 of that stat's potions into your potion storage, as many as
fit. If that storage is full, the potion isn't drunk and stays with you.

A **Potion of Oryx** counts as all eight potions at once. It's rare from the deepest Oryx stages
and can be forged from one of each stat potion (at the Divine Forge). For a stat that's already maxed, its
potion goes to your potion storage instead (the message says which). If that storage is full, the
Potion of Oryx isn't drunk at all: it stays, and the message says which storage to empty.

Health and magic potions (not stat potions) stack up to {{v:potion_stack}} in the HP and MP slots
next to your inventory; drag one there or double-click it. <kbd>F</kbd> and <kbd>V</kbd> drink
them.

## The realm

A realm is the big shared map where most of the game happens. You arrive on its beaches, and the
monsters get stronger the further inland you go: from the shore, through the lowlands, midlands and
highlands, up to the grey **Godlands** in the middle.

Dungeon portals drop from many realm monsters; take them. Each realm portal in the Nexus (the caverns in the shrine's floor, north-west of the spawn) shows
how many players are inside, and `/realm` takes you to the busiest one.

[Realm 2](realm-2.html) is another kind of realm, always open from the Nexus and made afresh each time,
with a land of its own for every realm event.

### The Godlands

The Godlands are the realm's highest ground and its richest. The gods that live there:

{{gen:godlands}}

They drop the realm's best gear and stat potions, and several drop dungeon portals.
`/godlands` takes you straight there from anywhere (outside a realm, to the busiest realm first).
Be at least level 20 and bring friends.

### Quests and the quest arrow

Your **quest** is a strong monster near your level. An arrow at the edge of the screen points at
it, and it shows at the top left of the screen with its health and the five players doing the most
damage to it.
Killing it gives a bigger share of experience. `/getquest` names it.

### Realm events

Every so often a realm event appears and is announced to the realm: a boss or a group of them,
worth more loot than anything around it. They're the realm's main quests.

{{gen:realm-events}}

Events scale with the players around them. An event boss has 60% of its health when 1 or 2
players fight it, and 20% more for each extra player who comes near, up to three times its health;
it never loses health it has gained, and one below its full health gives that share of the
experience. The next event appears 15 seconds after the last is defeated, out of sight but near a
player. Busy realms run two events at once from 6 players and three from 12, and an event nobody
fights for 10 minutes lets another one come. The Avatar of the Forgotten King, who opens the
Shatters, only comes as an event to a realm that has had 5 or more players, but every realm has one
waiting from the start in its arena in the mountains, ringed by four Killer Pillars.

Each event killed counts towards closing the realm, which takes {{v:realm_close}}; the chat shows
the count (for example "[3/8]"). The count, the realm's name and the events up carry on through
server updates and restarts. Then Oryx closes
the realm, and a little later everyone still inside is sent to **Oryx's Castle**, the start of the
[Oryx chain](endgame.html).

### Loot

Monsters drop loot in bags. The bag's colour says how good it is, from brown up to white, the
rarest, which is announced to everyone. Bags of good loot are soulbound: they're yours alone, and
you need to have done enough of the damage to get one. Doing more of the damage also raises your
chance at every drop.

**Healers and buffers get loot too.** Healing players with your ability while they fight, or
buffing them (Berserk, Damaging, a tome's or seal's Healing, a seal's boost and so on), gives you
**assist credit** on whatever they hit: half of each hit they land while your buff lasts, or for up
to 5 seconds after a heal. Debuffing a monster yourself (Curse, Slow, Paralyze and the like) gives
you a quarter of the hits other players land on it while the debuff lasts. Assist credit counts
like damage for drops and soulbound bags, on top of any damage you do yourself, but:

- it counts for at most a fifth of the monster's damage, and never takes you past its top damage
  dealer;
- healing someone at full health, or yourself, gives nothing, and a small heal gives less than a
  big one (healing a tenth of their health or more gives the full 5 seconds);
- it only comes while you're actually helping: standing near a group gives nothing;
- the damage dealers' own shares and loot don't change.

The boss panel at the top left shows each player's assist credit in green (+1,850) beside their
damage, counted as it counts for loot, and their share includes it.

- On weekends every drop is 30% more likely.
- A **Loot Drop Potion** (Nexus Bazaar) raises your drop chances for a while.

## The Arena

The Arena is waves of monsters that get harder as you go: realm monsters first, then the
Godlands gods (from wave 5), dungeon bosses (10), harder dungeon bosses (15), Elder dungeon bosses
(20), Oryx (30) and Oryx's Court (40), where the courts of Oryx 3, 4 and 5 fight beside Oryx 3, 4
or 5. Each wave has a boss of its stage, a few of its monsters come from the stage before, and
from wave 30 an Elder dungeon boss joins Oryx's. The chat says which wave has begun and its stage.

- **Oryx in the Arena** appears in the middle and is invulnerable for his first 5 seconds. He has
  less health than in his own dungeon and only fights his first phase, over and over: no calls
  for help, shields or rage.

- **The ring:** you arrive in the forest outside a walled ring and walk in through one of its four
  gateways while the countdown runs. When the game starts the gates shut, and anyone still outside
  or in a gateway is moved just inside. Mind the lava inside the walls.
- **Where:** the **Arena** portal, a sand pit sunk into the floor of the Nexus's shrine of the
  realm portals (north-west of the spawn, west of Realm 2's vortex), or `/arena` from anywhere: both lead
  to the same game. Everyone who goes in before a game's countdown ends plays that game. Once a
  game has started, the portal (and `/arena`) opens a new one. `/arenas` lists the games running.
- **Less fame** in the Arena and the Admin Arena. The Arena pays fame after every wave: five times
  the wave alone, or half that in a group (wave 10 pays 50 alone, 25 each in a group), half of
  what it used to. At level 20 kills there give three quarters of the usual experience, so three
  quarters of the fame on your character, and of the death bonuses that come from it. Levelling below 20 is as fast as anywhere, and the
  quest and achievement rewards for Arena waves are unchanged.
- **Loot is halved** in the Arena and the Admin Arena, since losing there costs nothing. A boss (a
  wave's boss, or Oryx) drops loot as often as anywhere else, but half the items, rounded up: if
  you'd have got 4 items you get 2, and 1 stays 1. Oryx 3, 4 and 5 also drop each item half as
  often, so they give about a quarter of their usual loot. Any other monster drops each item half
  as often. Loot boosts (weekend, Loot Drop Potion, events, the Legends) still count on top.
- **Between waves** you rest for 5 seconds, 15 from wave 10 and 20 from wave 20. Everyone is
  invulnerable for the first 2.5 seconds of each wave, 5 when an Oryx comes.
- **Voting** (the panel under the wave box, before the first wave and between waves):
  - **Next wave** starts the next wave (or the game) in 3 seconds once at least half of the players
    have pressed it: 1 of 1 or 2, 2 of 3 or 4. Press it again to take your vote back. Typing **GO**
    in chat counts as pressing it. If not enough vote, the rest runs out and the wave starts anyway.
  - **+1, +2, +5** combine waves into one fight: +5 brings the next five waves' monsters all at
    once. It takes more than half of the players (2 of 2, 2 of 3, 3 of 4), and the fight brings the
    most waves that many picked at least, so with picks of +5, +2 and none it's 2 waves. Alone, you
    decide. Your pick stays from break to break until you change it (+1 is no combining).
  - A combined fight pays each wave's fame and counts each wave for quests and achievements, the
    same as playing them one by one: it only saves time. Waves combine up to wave 29; from wave 30
    (Oryx) they come one at a time.
- **The Arena Chests:** two chests stand just south of the middle. Anyone in that Arena can put
  items in or take them out, and they don't vanish like loot bags. They're gone with the Arena,
  though: anything still inside when everyone has left is lost. Soulbound items can't go in.
- **Losing doesn't kill you.** A hit that would kill you in the Arena sends you to the Nexus at full
  health, with your gear and fame: no grave, and it doesn't count as a death.
- **The leaderboard:** the golden trophy beside the Arena portal shows the best run (its wave,
  how long it took and who took part) as you walk by; stand on it for the top ten, solo runs and
  group runs on their own tabs. `/leaderboard` lists both. The best solo and group players also
  stand in the [Hall of Champions](champions.html).

## Dying

Death is permanent. When a character dies, it's gone, with everything it was wearing and carrying,
and a gravestone marks the spot. What you keep:

- its **fame**, added to your account with bonuses;
- everything in your **vault**, your potion storage and your gift chests;
- your rank stars and class quests.

The Legends screen (on the character screen) has the top characters and a **Recent Deaths** tab:
the last 20 characters to die, with what killed them and where. A player with a character in the
top 20 of the week, the month or all time is a legend: their characters glow red in the game and
get a 1% loot boost (`/boosts` shows it), from the next time they enter a world. Pushed off every
top 20, they lose both the same way.

The living have their own boards: the [Hall of Champions](champions.html) in the Nexus, whose top 10s
hold rewards of their own.

So store what you can't afford to lose before a risky run, and don't take your best gear
somewhere you aren't ready for.
