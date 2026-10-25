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
counting after level 20. When the character dies, its fame, plus bonuses (for your equipment, for
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

Dungeon portals drop from many realm monsters; take them. Each realm portal in the Nexus shows how
many players are inside, and `/realm` takes you to the busiest one.

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
the count (for example "[3/8]"). Then Oryx closes
the realm, and a little later everyone still inside is sent to **Oryx's Castle**, the start of the
[Oryx chain](endgame.html).

### Loot

Monsters drop loot in bags. The bag's colour says how good it is, from brown up to white, the
rarest, which is announced to everyone. Bags of good loot are soulbound: they're yours alone, and
you need to have done enough of the damage to get one. Doing more of the damage also raises your
chance at every drop.

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
- **Where:** the **Arena** portal in the Nexus's south yard, or `/arena` from anywhere: both lead
  to the same game. Everyone who goes in before a game's countdown ends plays that game. Once a
  game has started, the portal (and `/arena`) opens a new one. `/arenas` lists the games running.
- **Fame:** the Arena pays fame after every wave (ten times the wave, halved in a group).
- **Loot is halved** in the Arena and the Admin Arena, since losing there costs nothing. A boss (a
  wave's boss, or Oryx) drops loot as often as anywhere else, but half the items, rounded up: if
  you'd have got 4 items you get 2, and 1 stays 1. Oryx 3, 4 and 5 also drop each item half as
  often, so they give about a quarter of their usual loot. Any other monster drops each item half
  as often. Loot boosts (weekend, Loot Drop Potion, events) still count on top.
- **Between waves** you rest for 5 seconds, 15 from wave 10 and 20 from wave 20. Type **GO** in
  chat to start the next wave (or the game) in 3 seconds instead, once per countdown. Everyone is
  invulnerable for the first 2.5 seconds of each wave, 5 when an Oryx comes.
- **Losing doesn't kill you.** A hit that would kill you in the Arena sends you to the Nexus at full
  health, with your gear and fame: no grave, and it doesn't count as a death.
- **The leaderboard:** the golden trophy beside the Arena portal shows the best run (its wave,
  how long it took and who took part) as you walk by; stand on it for the top ten. `/leaderboard`
  lists them too.

## Dying

Death is permanent. When a character dies, it's gone, with everything it was wearing and carrying,
and a gravestone marks the spot. What you keep:

- its **fame**, added to your account with bonuses;
- everything in your **vault**, your potion storage and your gift chests;
- your rank stars and class quests.

The Legends screen (on the character screen) has the top characters and a **Recent Deaths** tab:
the last 20 characters to die, with what killed them and where. A player with a character in the
top 20 of the week, the month or all time is a legend, and their characters glow red in the game
(from the next time they enter a world).

So store what you can't afford to lose before a risky run, and don't take your best gear
somewhere you aren't ready for.
