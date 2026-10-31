---
title: Guilds
nav: 4
sprite: shield
eyebrow: Play together
summary: Guilds: the loot, experience and stats every member gets from the guild hall, the shared guild vault, the four guild halls and their upgrades, the two guild boards in the Hall of Champions, ranks and commands.
---
A **guild** is a band of players with its own chat, its own hall and a vault they share. Being in
one is worth it from the first day: **every member gets +1% loot**, everywhere, and each bigger hall
the guild buys gives every member more (see [the buffs](#the-halls-buffs)).

## Making or joining one

- **Make one** at the **Guild Register**, the notice board beside the Nexus's **Guild Hall** up the village's cross street, for 1,000 fame: give it a
  name and you're its Founder. During the **guild event, until the end of 30 November 2026 (UTC)**,
  making one is free; the Guild Register shows the price of the day.
- **Join one** when someone in it invites you (`/invite <name>`, officers and up): accept the
  invitation that pops up, or type `/join <guild>`.
- **Leave** at the Guild Register (Renounce). A guild with nobody left in it is gone.

## Ranks

| Rank | Can |
|---|---|
| **Initiate** | chat, use the hall, **put items into** the guild vault |
| **Member** | also **take items out** of the vault |
| **Officer** | also invite players, promote and demote those below them, remove them, and write on the guild board |
| **Leader** | also buy the next guild hall |
| **Founder** | everything; made the guild |

New members start as Initiates, so nobody can empty the vault on their first day: an officer
promotes them once they're trusted. Officers and up manage ranks in the **Guild Chronicle**'s window
(the `+`, `-` and `×` beside each member).

## The guild hall

The door of the Nexus's Guild Hall (up its steps; it tells you if you're not in a guild) and in your vault leads to your guild's
own hall, and `/ghall` takes you there from anywhere. Everyone in the guild shares it. There are four
halls, each bigger than the last and each a shape of its own:

| Hall | Shape | Vault | What else it has | Upgrade to it |
|---|---|---|---|---|
| **1** | an octagonal lodge | 2 chests (16 slots) | the guild board, the Guild Chronicle, portals to the Nexus and your vault | (where every guild starts) |
| **2** | a cross-shaped hall | 4 chests (32) | a **training yard** of Training Dummies, which show your damage | 10,000 guild fame |
| **3** | a star-fort keep | 6 chests (48) | the guild's **quarters**, with the **Armoire** and the **Potion Storage**, and a fountain garden | 30,000 guild fame |
| **4** | a citadel | 8 chests (64) | a **throne room** with the **Divine Workbench** (the Divine Forge and the Enchanter) | 75,000 guild fame |

### The halls' buffs

Whatever hall the guild has, **every member gets its buffs**, wherever they are (not only in the
hall), from the moment it's bought:

| Hall | Loot | Experience | Stats |
|---|---|---|---|
| **1** | +1% | | |
| **2** | +1.5% | +5% | +20 health and mana |
| **3** | +2% | +10% | +40 health and mana, +2 vitality and wisdom |
| **4** | +3% | +15% | +60 health and mana, +4 vitality and wisdom |

The experience counts at level 20 too, where it becomes fame. The stats come on top of your
gear's, like a ring's, even past your class's maximum. `/boosts` shows what you get.

A **Leader** or the Founder buys the next hall at the **Guild Hall Upgrade** on the dais at the hall's
north end. Everyone in the hall moves into the new one at once, and the vault comes along: its items
stay in the same chests, with new empty chests added.

### Guild fame

The upgrades are paid in **guild fame**: whenever a guild member's character dies, its fame is
added to the guild's. The Guild Chronicle shows how much the guild has to spend and how much it
has earned in all.

## The guild vault

The **Vault Chests** in the hall are the guild's shared vault. Any member can put an item in; Members
and above can take items out (Initiates only put in). Soulbound items can't go in, as with any shared
chest. Every move is written down: `/gvault` lists who put in or took out what, the newest first
(`/gvault 50` for more).

The vault keeps its items for good, through restarts, deploys and hall upgrades.

## On the Hall of Champions

Two of the [Hall of Champions](champions.html)' boards rank guilds, and **every member of a guild in
the top 10 holds its reward**: +2% loot at #1, +1% at #2-3 and +0.5% at #4-10, on each board.

- **Guild Living**: the fame of the members' **living** characters together, counting only members
  who have played in the **last two weeks** (the others show as "away" in the Guild Chronicle).
- **Guild Dead**: the fame of every character that **died** in the guild **this year**. It starts
  afresh on **1 January** (UTC).

The boards' loot boosts count towards the Hall's +4% cap, and the guild hall's own (1% to 3%) comes
on top.

## The Guild Chronicle

The **Guild Chronicle** in the hall (and in your vault) opens the guild's window: its hall and buffs
(and the next hall's), its places
on the two guild boards, its guild fame, how full the vault is, the next hall's price, and every
member with their rank, the guild fame they've given and their living fame.

## Commands

| Command | Does |
|---|---|
| `/g <message>` (or <kbd>G</kbd>) | guild chat |
| `/ghall` | to your guild hall, from anywhere |
| `/ginfo` | the guild at a glance: hall, buffs, guild fame, board places, vault, next hall |
| `/gvault [count]` | the vault's log: who put in or took out what |
| `/gwho` | the guild's members who are online |
| `/invite <name>` | invites a player (officers and up) |
| `/join <guild>` | joins a guild that invited you |
| `/guildkick <name>` | removes a member (officers and up) |
