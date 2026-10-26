# Changelog

What changed in the game, in plain words for players: the client, the servers and the content.
The game shows this in its Changelog window. Newest first.

Every change goes under `## Unreleased` at the top as soon as it lands. Publishing a client
version (`python tools/build_client_rs.py --publish`) needs a section for that version and
refuses while an `Unreleased` one is left, so rename it to `## <version> (<date>)` then. The
version number itself is in `client-rs/Cargo.toml`.

Keep each version short: changes go under `### ` group headings (Oryx 5, Arena, Items, Game and so
on), one line per change players notice, summed up rather than item by item (the wiki has the
details). Add to a group's line when a change adds to it, rather than adding another line.

## Unreleased

### Classes
- Summoner: summons are much smaller, the top tiers most of all (the Dragon and Oryx were three
  times a player's size, now a little over one), and so are their shots, so six of them no longer
  hide the boss.

### Divine Forge
- An ingredient taken from your vault shows a little vault chest in its corner instead of the word
  "vault", and its tooltip says how many your vault holds.

### Fixes
- Janus leaves the Oryx's Chamber portal beside his loot bags instead of under them, where it
  was hidden and couldn't be entered, and no Realm Portal beside it.
- The chat name colour you pick in the quest log now stays with you in every world and after
  you log out, instead of going back to the usual colour when you change world.
- In the browser, opening the Graveyard (and the market's time left on offers) no longer freezes
  the game until you refresh the page.

### Wiki
- A new Loot Simulation page: pick Oryx 3, 4 or 5, set your share of the damage and your loot
  boosts, and see every drop's chance, the bags a kill gives, and roll as many kills as you like.

## 2.2.0 (2026-10-25)

### Quests and achievements
- Quests: every day you get three daily quests, every week three weekly ones and every month three
  monthly ones (slay monsters or gods, clear dungeons, defeat realm events or Oryx, beat Arena
  waves, earn fame and more), each worth fame and some potions or keys, plus a bonus for finishing
  them all. Don't like one? Swap it, once a period. They reset at midnight UTC.
- A daily reward for logging in: a 28-day track with bigger rewards every seventh day. Missing a
  day never loses your place.
- Achievements: 22 of them, each with Bronze, Silver, Gold and Ember tiers, for everything from
  kills and dungeons to trades and white bags. Each tier gives fame and points; points raise your
  rank, and ranks unlock chat name colours. Your kills, fame and levels from before count.
- Press J, or the new Quests button beside the Nexus button, to open the quest log; the button
  shows how many rewards are waiting, and a popup tells you when you finish something. The
  Tinkerer and the Chronicler by the Nexus spawn keep your quests and your deeds. /quests lists it
  all in the chat.

### Lair of Draconis
- The Lair of Draconis is open. The Rock Dragon drops its portal (and the Lair of Shaitan's half
  the time), and the Draconis Key opens it: it's in the Nexus Bazaar's key stalls for 300 fame. Step onto a dragon's soul by the altar to face Nikao,
  Pyyr, Limoz or Feargus in its lair; pop the loot balloon it leaves and the way back opens. Slay
  all four and the altar opens the Ivory Wyvern, and half the time the Elder Lair too.
- The Elder Lair of Draconis is the same lair, darker, with Elder dragons: twice the health, harder
  hits and faster attacks, and better loot, including a potion that maxes the dragon's stat and its
  Elder armour or bow. Before, it was Doomed Realms' unfinished map.
- Each dragon drops its stat potions, top-tier gear and a relic; the four relics forge into an Elder
  Draconis Key at the Divine Forge.

### Manor of the Immortals
- The Manor is a whole house now: you arrive in the Foyer and fight through its gallery, library,
  kennels, crypt, chapel and dining hall (a different set and order each visit) to Lord Ruthven's
  hall, instead of starting next to him. The Elder Manor is built the same way.
- Lord Ruthven has a full fight: he wakes when you come near, stalks you with daggers, rises to fire
  rings of shots, and turns into a swarm of bats that sends swarmers after you, faster and with more
  daggers at 60% and 25% health. He leaves his Coffin, which lets out bats and Nosferatu as you break
  it and holds the loot: the Manor's special items, Attack and Dexterity potions and tiered gear.
  The Elder Manor's Coffin can also drop a Potion of Max Attack.
- The Manor's bats only bite up close, and its Nosferatu and Rock Bombs fire properly.

### Lair of Shaitan and the Avatar
- The Lair of Shaitan has a new arena: a rocky island in a sea of lava, made by Aurusenth. The fight
  can be won now: Shaitan's hands could never be hurt before. His loot balloon drops its loot (it
  dropped nothing) and the way out, and the Skull of Endless Torment drops 1 time in 100, not every
  time.
- The Avatar of the Forgotten King always fights in its own arena, wherever it appears: four Killer
  Pillars round it (there were eight, half of them out of place) and the blobomb makers its "Blobs,
  attack!" phase sets off. The Tablet of the King's Avatar drops 1 time in 200, was 1 in 20.

### Items
- The Doomed Realms classes' tier 6 abilities (Pearl of Wasted Blood, Halo of God's Blessing, Light
  Turret, Dragon Scroll, Nature Cannon, Jacket of Sorrows) drop in the same bag as other tier 6
  abilities again, not a white one.
- A pass over items that were far stronger than the best tiered gear. Each is now a little better
  than the top tier of its kind, with its own mix of stats: the Elder armours (Raw Hope, Angel's
  Hope, Wings of Natural Bloodlust, Scaled Robe and the three Elder Draconis ones, which had 100 to
  200 Defense), the America Armor, the Coral Medallion, Oceans Gift, Ring of the Abyss and Reforged
  Forgotten Crown, the Lightning Bow, Sword of the Abyss, Sword of the Colossus and the Battle for
  the Nexus weapons (Robobow, KoalaPOW, Doctor Swordsworth), the Scepter of Zeus (650 damage to 6
  enemies, was 4,000 to 20), the pD Scepter, the Seal of the Fallen Angel (6-second auras, were
  15), the Helm of the Pumpkin and the red and blue bee quivers. Items you already have change too.
- The Chancellor's Cranium and Diplomatic Robe (and the Turncoat Cape and Crystallised Mist) go off on
  any shot that hits for over their threshold before your defense: in good armour they hardly ever
  went off before.

### Oryx 5
- The Holy Gates shut behind you when Oryx the Ascended God wakes and stay shut until he dies;
  anyone standing in them is moved inside. In his Last Judgement the corridor below the arena fills
  with gates (anyone hiding there is moved out) and lances sweep across its mouth, so there's no
  safe corner left.
- When he picks his patterns at random, he never picks the same one three times in a row.
- Only Oryx 5 drops Ascended shards: the Void Entity no longer does.

### Oryx 3 and 4
- From 80% health the four pillars in Oryx 3's Sanctum fire his shots too: armour-piercing pairs in
  at the middle, and now and then a spear at the nearest player, softer than his own and stronger
  below half health.
- Oryx 3's Sanctum gate shuts behind you when he wakes and opens when he dies; anyone standing in it
  is moved inside.
- Oryx 4's red butterflies (the Infernal Elements) hit for 20, through armour.
- Janus the Doorwarden's door opens only once both Stone Guardians are dead and a quarter of the
  Castle's haunted Suits of Armor are broken; Janus tells you when the armour is all that holds it.

### Divine Forge
- The Enthroned Robe is forged from the Robe of Absolution (with an Ascended Armor Shard), not from
  any tier 16 armour.
  
### Fixes
- Summoner scrolls show their summon's damage (a shot, how often, and a second), and that up to 6
  can be out at once; Mechanic turrets show how often they fire and their damage a second. The
  wiki's scroll and turret pages show the same.
- Every item's tooltip says which classes can use it ("Usable by Wizard, Adept"), or "Usable by
  every class" for rings.
- A monster that another monster moves on to its next phase drops the shields of the phase before.
  Some stayed unhittable for good (Shaitan's hands among them).

## 2.1.1 (2026-10-25)

### Classes
- A balance pass over the Doomed Realms classes. Stronger: Blademaster (highest Attack 45, was 35;
  Defense 25, was 15), Adept (Attack 60, was 50; Defense 25, was 20), Snowman (Defense 25, was 20;
  Dexterity 70, was 65), Mechanic (health 700, was 650) and Ascendant (Attack 70, was 60). Weaker:
  King (highest Attack 75 and Dexterity 70, were 85 and 85) and Sharklord (Defense 25, was 30;
  health 750, was 800). Maxed Kings and Sharklords keep what is still under their new highest.
- Their abilities too: from T6 up, every Doomed Realms class's ability gives +20/+40/+60 health and
  mana, as every other class's does. Druid pearls cost what a tome of their tier costs (up to 95
  mana, was 160), the Pirate's T5-T8 cannons hit twice as hard (one ball against the quiver's three
  arrows), and the Dragon and Oryx scrolls and Tidal Wave give Vitality and Wisdom like the rest.
  The Fountain Torrent heals
  335 (was 320, the same as the tier below), and the Obsidian Blades fire at 1.3 times the normal
  rate (was twice, more than the next two tiers of blades).
- Summoners can keep up to 6 summons out (a seventh sends the oldest away). Each summon now fires
  a third as often, but every shot of a volley counts: the Dragon's and Oryx's five-shot volleys
  only ever hurt for one shot before. The T0-T5 summons hit much harder (the Bunny 70 a shot, was
  20; the Wisp 100, was 65), so a full set of six adds about a quarter to a half to your damage
  at every tier. Other helpers that fire several shots at once land all of them now too.

### Fixes
- Drinking or taking potions from a loot bag on top of a portal no longer sends you through the
  portal: a click meant for the bag can't press the portal's Enter button when the bag empties,
  and the interact key only enters a portal when its Enter button is the one showing.

## 2.1.0 (2026-10-25)

### Arena
- The Arena is fought in the Admin Arena's walled ring. You arrive in the forest outside and walk
  in through one of its four open gateways during the countdown; when the game starts, the gates
  shut, and anyone still outside or in a gateway is moved just inside.
- Loot is halved in the Arena and the Admin Arena: bosses still drop loot as often but half as many
  items (rounded up), and other monsters drop each item half as often. Oryx 3, 4 and 5 get both, so
  about a quarter of their usual loot.

### Items
- A Potion of Max drunk on a stat that's already maxed now puts 6 of that stat's potions into your
  potion storage, as many as fit, instead of 100 or more past its limit. With that storage full,
  the potion stays with you.

### Divine Forge
- The forge now uses what's in your vault: ingredients you don't carry are taken from your vault
  chests (not the shared Nexus chests), and the forge window marks each one that comes from your
  vault with how many it holds. With every ingredient from your vault and a full inventory, the
  forged item goes into your vault.

### Game
- The server does about half the work in busy realms, uses a third of the memory, and stops for
  memory clean-up far less, so ping is lower and steadier and big fights, the Arena above all, lag less. In the realm,
  monsters more than about 16 tiles from everyone now wait until someone comes closer. Fixed a
  server crash when several players dropped at the same moment.

## 2.0.2 (2026-10-24)

### Game
- In the browser, the chat and sign-in boxes now copy, cut and paste with Ctrl+C, X and V (Cmd on
  a Mac), as on the desktop, and the browser keeps its own reload, tab and zoom keys.
- Legends glow red again, as they did in the old client, for you and everyone around you, and
  rare, epic and legendary monsters glow green, purple and pink, with their rank by their name
  on the quest bar.
- In dungeons and vaults, what you do reaches the server up to a tenth of a second sooner when
  nobody is in the Nexus or realm they were opened from, and the ping shown there is no longer
  about 100 ms too high. Effects from the Ghostly Prism, Chaotic Scripture and other ally items
  no longer linger in the world after they go off.
- The Nexus Bazaar no longer sells the Christmas Key. A Christmas Key you already have still opens
  Christmas Oryx's Chamber.

## 2.0.1 (2026-10-24)

### Oryx 5: Oryx the Ascended God
- Oryx's Elysium is open: use an Oryx's Elysium Incantation by the locked portal Oryx 4 drops, and
  the Nexus Bazaar sells it again. The Void Entity on the Pyramid now fights back and drops loot,
  and the Ascended Wardens round Oryx 5 shoot.

## 2.0.0 (2026-10-24)
- Do Damage 2.0: the whole Oryx chain is rebuilt, with Oryx's Inferno for Oryx 4 and Oryx's
  Elysium for Oryx 5, one tougher Arena, the Divine Forge in the Nexus, and every weapon, armour,
  ability and pet reworked.

### Oryx 5: Oryx the Ascended God
- Oryx 5 has ascended: he is now Oryx the Ascended God, in white and gold with a spear, and his
  fight is all new: his Radiant Cross, the Firmament, the Divine Hunt, his Host at 80%, his
  Transcendence at 60%, his Apotheosis at 20% and the Last Judgement at 10%. When he concedes he
  falls back through every Oryx he was and leaves a Nexus Portal.
- A new dungeon: the Doomed Cellar is now Oryx's Elysium, a holy island of gold, marble and
  turquoise sea. His Ascended Host guards the roads, a guardian waits at the Oasis and at the
  Pyramid (now fallen to the Void, with the Void Entity), and twelve Trees of Doom hold the Holy
  Gates to his temple shut. It stays sealed while his guardians and loot are finished.
- New items from him: a T15 of every weapon, three T16 armours (Hide of the Firmament, Armor of
  Ascension, Robe of Absolution, which replace the Sphinx Leather Armor, Armor of the Pyramid and
  Robe of the Nile you have), the Elysian Staff, the reworked Sword of Dark Enchantment, two
  Ascended Gems and the Ascended forge shards.
- The first Ascended items: the Enthroned set, forged from his top tier and an Ascended shard.
  They're placeholders with no numbers yet.

### Oryx 3 and Oryx 4
- New names: Oryx the Celestial Mad God (Oryx 3) and Oryx the Infernal Mad God (Oryx 4). Their
  taunts go to the chat only, so no speech bubble hides their attacks.
- Oryx 3 moves more: he chases, backs away and dashes back to the middle of his Sanctum.
- The Hash Cellar is now Oryx's Inferno, rebuilt in fire: a Burning Path through four chambers,
  guarded by the new Infernal Court, and a round arena with lava pools. Oryx 4 burns, calls his
  Infernal Court and fires hellfire.
- New gateways and incantation seals for Oryx's Sanctuary, Inferno and Elysium. Incantations you
  have still work.
- Oryx 4's items are Infernal now (the Brimstone Poison, the Cloak of Bloodshed, the Bloodletter,
  which makes enemies bleed, and the Gems), and he drops the Infernal forge shards.
- The Oryx 4 and Oryx 5 event: while it's on, they and everything they bring hit 20% softer.

### Arena
- One Arena: the Battle Arena is now just the Arena, and the Free Battle Arena and the public
  arena are gone. Its portal stands in the Nexus's south yard, and `/arena` takes you into the
  same game from anywhere. It pays fame after every wave as before, and older runs on the
  leaderboard count as the Arena's.
- Tougher waves: Oryx 2 to 5 fight in it (with less health and only their first phase), and from
  wave 40 Oryx's Court comes with them.
- Fairer starts: everyone is invulnerable for the first 2.5 seconds of each wave, and bosses no
  longer wall monsters in.
- The wave and time box at the top works.
- Oryx 3, 4 and 5 drop everything at half their usual chances here.

### Items
- Weapons rebalanced: the top tiers give stats and reach further, and wands and katanas hit harder.
  The Blademaster gets T13 and T14 Dual Blades, and the Monk gets tiered fists (T0 to T14) with
  their own pictures.
- Armour rebalanced: leather adds Speed, robes add mana and health, and the top heavy armours add
  health and Vitality.
- Abilities: every class has one ability at each tier from T0 to T8, and all of them drop. Most
  kinds are stronger or do more: quivers, tomes, helms, seals, prisms, lutes, skulls, scepters
  and poisons. Shurikens throw their star, and the Pirate's cannons drop.
- Untiered items have new drops, mostly from Oryx 3, 4 and 5, the Ice Tomb, Esben, Belladonna and
  the Elder bosses. Many have new numbers, and the abilities that did nothing (Oryx's Escutcheon,
  Genesis, Chaotic Scripture, Battalion Banner, Hivemaster Helm, Marble Seal) work. Legendary,
  Ascended and Ethereal items are plain untiered items until they're reworked.
- Gone: set (ST) items, Enchanted Keys in the Bazaar, the old forge materials, the Forge and
  Burning Amulets, the Ring of Oryx and the T20 armours stop dropping, and several overpowered,
  donor and seasonal items are staff rewards only now. Ones you have still work.
- More drops: T9 weapons and T10 armour from the Godlands gods.
- Potions: the Potion of Max Life is much rarer and stores 30 health when you're maxed, and a
  Potion of Oryx is no longer drunk halfway.
- Conditions do what they say: poisons put on their conditions, Silenced stops your ability, and
  bleeding enemies show the bleed icon.

### Divine Forge
- The Divine Forge stands in the Nexus's south yard, west of the Arena portal: browse and search
  its recipes, forge, and sell items for fame. `/forge`, `/forgelist` and `/sellfame` point to it.
- Every tiered item forges from two of the tier below, for at most 2,000 fame, and no untiered
  recipe costs more than 3,000. Look swaps cost 100 fame, and one-item recipes work.
- Forge shards go into a forge storage your whole account shares (its Shards tab). There are
  new UT and Highest tier filters, and an Enchant tab comes later.
- It no longer makes overpowered, seasonal or never-dropped items.

### Classes and pets
- Class changes: the Sharklord's highest Attack is 70 and the Knight's highest Defense 35. Dual
  Blades ignore defense (hits that do show their damage in purple, and tooltips say so), the
  Blademaster's jackets give Attack, and new Monks start with their
  gear.
- Pets come in three sizes, Small, Medium and Large, drawn to scale. Every pet heals your health
  and mana, many help in new ways, and boss pets shoot their boss's attacks.
- A Training Dummy in your vault tells you your damage a second.

### Game
- Character sprites keep animating at high dexterity and speed.
- Right-click a player in the Nearby players list to keep their card open and see their items.
- Item tooltips show what an item does. NPC buttons and the inventory tab show their keys. Status
  icons are tidier, with no names written over characters.
- Legends show characters' Doomed Realms gear again.
- A Portal of Cowardice takes you back the way you came.
- The game's window and taskbar icon is the Do Damage crown.

### Wiki
- New pages for the Divine Forge and Gear statistics (every class's damage on the Training Dummy).
  The Pets page shows sizes and healing, and the guides are up to date.
- A wider layout on big screens, a shorter page list, and more complete drop lists.

## 1.3.3 (2026-10-21)
- Changing your name at the Name Changer now costs 50,000 fame instead of 100 gold. Choosing
  your first name is still free.
- The portal's Enter button shows the key you set for Interact in the options, not always 0.
- The arena trophy in the Nexus now shows the record over it as you walk by: the best run's
  wave, how long it took and who took part, on a card that floats above the trophy (it doesn't
  go in the chat). Stand on the trophy for the full top ten, as before.
- The camera now centres you in the part of the screen you can see, left of the bar down the
  right, instead of the middle of the whole window, which put you a little to the right.
- Joining an arena game that has already started now tells you so ("That game has already
  started") instead of disconnecting you. Staff who /summon a player into an arena, or /visit
  one, now get them (or themselves) in even after the game has started.
- The wiki has a Gear section: every weapon, ability, armor and ring in the game, with its
  picture, its numbers, what it does and where it comes from (bosses, tier drops and shops).
  Each kind of item has its own page, with tabs for the others, sorted into tiered, UT, ST,
  Legendary, Ascended, Ethereal and admin items. Each class has its own page too, with a tab
  each for the weapons, abilities, armor and rings it can use (for example
  dodmg.com/wiki/classes/wizard.html).
- The wiki's Pets page now has every pet, with pictures: all the pet generators with their
  damage, healing and where to get them (which Bazaar and /shop shops sell them and who drops
  them), the items that summon a helper for a while (drake eggs, Santa's reindeer and more),
  and the classic pet yard's pets by family and rarity.

## 1.3.2 (2026-10-20)
- Arena waves mix in monsters from earlier stages: a couple of each wave's monsters come from
  the stage before, and Godlands monsters keep turning up in every stage after the Godlands.
  From wave 30, an Elder dungeon boss joins the wave's Oryx-stage boss. Chat now says which
  wave has begun, and its stage (for example "Wave 12 has begun (Dungeons).").
- Nearby players (bottom right): each row's name, star and character now line up in the
  row's middle, the rows are further apart so characters no longer touch, and the list sits in
  the middle of its box. It shows up to four players, to make that room (it was five).
- A golden trophy between the arena portals in the Nexus shows the Arena Leaderboard: the best
  run with its wave, how long it took and everyone who took part, then the next nine. Runs in
  the public arena (/arena) count too now, and /leaderboard shows each run's time.
- Rank stars have nine colours instead of five, spread evenly from no stars to every class
  done: light blue, blue, violet, red, red-orange, orange, gold, pale yellow and, at the very
  top, white.
- Almost every item can now be traded, sold on the market and dropped for other players,
  Soulbound ones included. Only admin items and a few overpowered or test items can't; their
  tooltips say "Untradeable".
- Arenas: the wait between waves grows as you go, from 5 seconds to 15 at wave 10 and 20 from
  wave 20 on. Type GO in chat to start the next wave (or the game) in 3 seconds instead, once
  per countdown.
- Arenas have far more kinds of monsters, getting harder as the waves go on: realm monsters
  first, then the Godlands gods, dungeon bosses, Elder dungeon bosses and finally Oryx. The
  Battle Arena and the public arena now use the same waves.
- Blademaster, Druid, Mechanic, Ascendant, Summoner, Monk and Sharklord gear now drops like
  everyone else's: wherever a monster can drop, say, a tier 3 ability, it can now be a
  Mechanic's tier 3 turret. Before, these classes' gear never dropped at all.

## 1.3.1 (2026-10-20)
- The status effect icons above a character no longer jump to the side while it shoots.
- Item tooltips are easier to read: each stat's name and its value are in different colours.
  What an ability does now comes right under the description, its damage first (for a poison,
  the impact and the poison damage on their own lines), then the item's stats with its damage
  first.
- Comparing an item with the one you wear: green and red now say better or worse than what
  you're wearing in that slot, with the difference beside it, and bonuses you'd lose are listed.
  Hovering the item you already wear (or a copy of it, or one your class can't use) shows its
  numbers without green or red.
- Your abilities' damage now shows over what they hit: poison (its first hit and every tick),
  traps, lightning, tomes and the rest. Only other players could see those numbers before.
- A poison with no first-hit damage no longer sends a hit of 0.
- Trading now includes your backpack: its items show in two more rows of the trade window,
  you can offer them, and items you get can go into your backpack when your inventory is full.
- Fixed: a traded item could lose its extra details (what makes that copy of it special).
- Nearby players: each player's rank star now shows in their rank colour, as in chat, lined up
  with their name, with their character and class beside it.

## 1.3.0 (2026-10-19)
- A new look for the screen: the world fills the whole window, with one bar down the right.
  At its top, the minimap square in the corner, then your experience, HP and MP; at its foot,
  the Nexus button (the Nexus portal and its key), your equipment, your inventory with two
  small buttons to switch to your backpack, your potions, and a space for bags you stand on,
  portals, shops and invitations. Your gold, fame and frame rate sit beside the minimap.
- Under your HP and MP, your character card: your name, class and level, and two tabs, your
  stats or your records (kills, god and Oryx kills, quests, dungeons, accuracy, potions drunk
  and time played, as they stand), filling the bar down to the Nexus button.
- The boss you're after shows at the top left: its picture, name and health, with the five
  players who did the most damage just under it: their class, exact damage and share.
- With no boss around, a small menu button at the top left opens your last 3 bosses. Click one
  to see everyone who hit it, with their class, damage and share.
- Potions in the potion bar can come back out: drag one onto an empty inventory slot.

## 1.2.9 (2026-10-19)
- The Support Bot in the Nexus has a new tip about Elder dungeons and their better loot.
- Every Oryx is worth killing now, and each stage pays more than the last. Every Oryx always
  drops a Potion of Attack and Defense (Oryx 5 Speed and Dexterity too), with more stat potions
  on top: about 4 from Oryx 1 up to about 8 from Oryx 5. Oryx 2 always drops Life, Oryx 3 and
  later Life and Mana, and Oryx 5 two of each.
- Better gear down the chain: a tier higher at each stage, and tier 7 abilities and rings from
  Oryx 3, 4 and 5. Oryx 5 is the only place to find tier 8 ones.
- Rarer prizes are easier to find: Potion of Oryx is nearly twice as likely from Oryx 3, 4 and 5,
  Oryx 3 can now drop the Oryx set and drops Oryx's Escutcheon more often, and Oryx 4 and 5 drop
  the Oryx set about twice as often. Oryx 2 can drop the Doom Bow.
- Everyone who does a fair share of the damage gets the full loot: 5% of the damage is enough for
  gear and the Oryx set (it was 10%), and 3% for the extra stat potions.
- The Stone Guardians, Janus, the Giant Oryx Chicken and Oryx's court in the Sanctuary drop more
  stat potions too.
- Your rank and its star moved to the top right, above your gold and fame. Each number there
  now lines up with the middle of its icon.
- Options: the sliders now come first on each tab, above the switches.
- A new Master volume slider on the Sound tab turns all sound and music up or down together.
- Resetting the camera (Z) now always turns it back to north at the top.

## 1.2.8 (2026-10-19)
- Realms are much easier to close with a few people. A realm closes after 8 events for 1 to 3
  players, one more for each extra player, up to the classic 25 for 20 or more. The count in
  chat shows how many your realm needs ("[3/8]").
- Event bosses (Cube God, Pentaract, the Sphinx, the Hermit God, Ghost Ship and the rest) have
  60% of their health when 1 or 2 players fight them, and 20% more for each extra player who
  comes near, up to three times their health for big groups. A boss never loses health it has
  already gained, and one with less health gives less experience.
- Events come quicker: the next one appears 15 seconds after the last is defeated, out of sight
  but near a player instead of anywhere in the realm. Busy realms get two events at once from 6
  players and three from 12.
- An event nobody fights for 10 minutes no longer holds up the realm: another one comes along.
  The Shatters' Defense System only appears once a realm has had 5 or more players.
- The Killer Bee Nest counts as one event, not three.
- Realms run lighter: monsters far out of sight now rest until someone comes near.
- Losing in an arena no longer kills your character. In the Battle Arena, the Free Battle Arena,
  the /arena arena and the Admin Arena, a hit that would kill you sends you back to the Nexus at
  full health instead, with your gear and fame. There's no grave, and it doesn't count as a death.
- The treasure chest at the end of the Puppet Master's Theatre opens faster: it has half the
  health it had and much less armour, so it takes less than half as long to break open. It
  drops the same loot.
- Pets' shots are drawn smaller and see-through, so a boss pet's bullets no longer cover the
  screen, and other players' pets' shots are fainter than your own. They hit just as hard as
  before. Options, Visuals, "Pets' shots" switches between Normal (as before), Subtle (the new
  default) and Hidden.

## 1.2.7 (2026-10-18)
- The website has a wiki (dodmg.com/wiki): how to get started, how to get stronger, the Oryx
  chain and its incantations, every dungeon with where its portal or key comes from, how hard it
  is and what it drops, items, pets, every class with what /gear gives it, all the commands, and
  answers to common questions. Search it from the box at the top of the sidebar.
- /gear also gives you an XP Booster (50% more experience until level 20 or death) and a Loot Drop
  Potion (more loot for 30 minutes), into your backpack.
- The Support Bot's dungeon portal closes once its dungeon has been cleared, and the bot says
  so. If nobody clears it, it's still replaced by a new dungeon after ten minutes. Players
  already inside a dungeon are never sent out when its portal closes.
- The Legends screen has a Recent Deaths tab: the last 20 characters to die on the server,
  newest first, with their level, fame, what killed them, where and how long ago. Click one to
  see how its fame was counted.
- Every class can be played from the start: you no longer need to level another class first.

## 1.2.6 (2026-10-18)
- The Support Bot in the Nexus now says how many XP gifts are left and when the next ones come,
  between its tips. Every ten minutes, five minutes after the XP gifts, it also opens a portal
  to a random dungeon next to the spawn, open until the next one. It also tells you where to
  find the game's code on GitHub.
- The Legends screen shows the top 20 characters now. When they don't all fit, scroll the list
  with the mouse wheel or drag its scrollbar; a line under it says how many more there are.
- Everyone can type /gear now for a set of starter gear for their class (a tier 7 weapon, tier 8
  armor, a tier 4 ability and ring). It works once per character, in the Nexus, below level 20,
  and only upgrades: better items you're wearing stay, and the ones it replaces go to your
  backpack, so keep a few slots free.
- A Support Bot stands by the Nexus spawn with tips for new players: /gear, /godlands, the
  realm, your vault, the shops and more. Every ten minutes it puts out 100 XP gifts around the
  spawn to shoot for experience; a new batch only comes once every gift of the last one is gone.
- /godlands works from anywhere now: outside a realm it takes you to a realm and drops you
  straight into its Godlands.
- New dungeon: the Chicken Chamber. A giant chicken has taken over Oryx's Chamber: fight
  through a coop of chickens and eggs (break the eggs before they hatch), then pluck the Giant
  Oryx Chicken for potions, gear and a chance at the Chicken Leg of Doom, Anatis Staff or
  Apocalypse Feather. It opens the way to Oryx's Wine Cellar, like Oryx's own Chamber. The key
  drops from Janus and is sold in the Nexus Bazaar.
- The website has screenshots from the game: a realm fight in the Godlands, two dungeons, the
  Nexus, the character list and Oryx in the Wine Cellar. Click one to see it full size.

## 1.2.5 (2026-10-18)
- The damage numbers over monsters are right again. After using an ability while firing (a
  quiver or shield), every number the game showed was off for the rest of your stay in that
  world, and you could see monsters "die" that weren't dead. Ability shots fired while you're
  shooting, and the shots right after them, now also do their damage; they were sometimes
  ignored.
- Fixed monsters that were sometimes left alive after your shot looked like it killed them:
  your shots went straight through them, they stopped shooting, and only abilities could finish
  them off. They can now be shot again a moment later.
- Item tooltips and class descriptions break their lines and paragraphs properly, instead of
  showing stray "\n" marks in the text (a crate's list of rewards is one item per line again).

## 1.2.4 (2026-10-17)
- Enchanted Keys now give the Mystery ST Crate they promise when you open their dungeon, and
  the crate gives you one of the items it lists. Mystery Dyes from the Nexus shop now give a
  dye, and the Mystery ST Chest an ST item. Items the game can't use yet now stay in your
  inventory instead of disappearing.
- Server updates no longer disconnect anyone, in the browser or the desktop game: you move to
  the updated server the next time you change worlds.
- If your connection drops while you play, the game now takes you back to the Nexus by itself
  instead of sending you to the character list (it keeps trying for about two minutes).
- The monsters that got their behaviour in 1.2.3 now drop loot to match: the heroes (Red Demon,
  Phoenix, Oasis Giant, Ghost King, Deathmage) tiered weapons, armor, rings, abilities and
  potions, and their minions potions and the odd low-tier item.

## 1.2.3 (2026-10-17)
- Monsters that never had any behaviour now move, attack and taunt: the Red Demon, the Phoenix
  Lord (which turns into an egg when it dies), the Lich, the Ent Ancient, the Ghost King and the
  Oasis Giant with their minions, the golems, skeletons, mummies and ghosts, the Abyss lava bomb,
  Bonegrind the Butcher and the Beer God, among about 80 others.
- You can now see the Beer God in the high forest: it was invisible, with only its health bar
  showing.
- Smoother walking: your character no longer flickers to standing or shakes by a pixel while
  you walk (most noticeable with the frame rate unlocked), sliding along a wall no longer shakes
  the view, monsters and other players keep walking instead of flickering to standing between
  server updates, and legs move at a pace that matches how fast each character walks, as in
  the original game.
- The mouse pointer no longer stays hidden on the menus after you die or get disconnected while
  the crosshair is showing.
- Randomly built dungeons are shorter: each run now comes out small (3 in 10 runs), medium (6 in
  10) or large (1 in 10, about the size they were before), with shorter corridors in the small
  and medium ones.
- Every dungeon boss now leaves a Realm Portal behind when it dies, and portals that bosses drop
  in a dungeon (the Realm Portal, an Elder dungeon) stay open instead of closing after a while.

## 1.2.2 (2026-10-17)
- Fixed being disconnected from the game with "Overlapped I/O operation is in progress (os
  error 997)" on some Windows PCs.
- The Nexus is rebuilt as the courtyard of a castle, much smaller than before: you arrive by the
  fountains, the realm portals are in the keep's great hall just north, and the shops are in two
  market wards either side behind colonnades. The Vault and guild portals are beside the spawn;
  the arena portals, the Wine Cellar, the class memorial and the shared chests are in the south
  yard.
- Your Vault is now a cellar under the castle, with your chests in its great hall and your gifts
  in the gallery below.
- Halloween has come to the castle: jack-o'-lanterns, green candles, fallen leaves, dead trees
  and a witch's cauldron in the Nexus and your Vault, until the end of the season.
- The server is called Europe in the server list instead of Localhost.
- You no longer sometimes enter the realm right beside the Avatar of the Forgotten King's arena.
- Bosses fought alone have their normal health: the extra health for a group now starts with
  the second player, not the first.
- Dr Terrible (25,000 health, was 55,000) and the Horrific Creation (35,000, was 60,000) in the
  Mad Lab, and the Ghost of Skuld at the end of the Haunted Cemetery (50,000, was 130,000), are
  much quicker to kill. Dr Terrible's and the Creation's armour is a little lower too.
- A crosshair marks where you aim over the world (the pointer hides while it shows). Options,
  Visuals, "Crosshair" turns it off.
- Options has a new Visuals tab for what you see in the world: particles, shadows, health bars,
  speech balloons, shots, damage numbers and the crosshair. Graphics keeps the camera, zoom and
  frame rate.
- New Visuals options: hide other players' pets' shots, hide other players' pets' effects, and
  sliders to make other players and other players' pets see-through (or hide them at 0%).
- The sliders in Options can be dragged: hold the button and slide, instead of clicking where
  you want them.
- Moving looks steadier: the ground and characters stay on whole pixels, so their pixel art no
  longer shimmers or wobbles as the view follows you.

## 1.2.1 (2026-10-16)
- The desktop game now signs in and plays over an encrypted connection (https), like the
  browser version, so your password and what you do in game can't be read on the way to the
  server. Your game switches over by itself when it updates.
- The Native White Demons in the Abyss of Demons now chase you and shoot back instead of standing still.
- Passwords are stored more safely on the server. Your password doesn't change, and nothing
  needs doing.
- After 10 wrong passwords for one account, signing in to it is blocked for 15 minutes, so
  nobody can keep guessing.

## 1.2.0 (2026-10-16)
- Signing in with an email that has no account, or with the wrong password, now stops at the
  sign-in screen with a message. It used to show an empty character list that couldn't play.
- Clearer messages when signing in or creating an account fails: wrong password, unknown
  email, name or email already taken, an email that isn't valid, a password that's too short
  (6 characters at least), or no connection to the server. Long messages now wrap instead of
  running off the screen.
- The sign-in, register and account boxes work like normal text boxes: Ctrl+A, copy, cut and
  paste, undo, the arrow keys, Home and End, Shift to select, Shift+Tab to go back a box, and a
  click puts the cursor where you click. Passwords can't be copied out.
- The game opens on a main menu: Play, Sign in, Register, Options, Changelog and Quit. If you
  stay signed in, it says who you are and Play takes you straight to your characters. Options
  can be changed before you play, and Escape or "Main menu" brings you back to it.
- Play without an account: Play on the main menu starts you as a guest with a name of your own.
  Your characters stay on this computer or browser; Register ("Save progress") at any time
  turns them into a full account, characters, items and fame included. Guests can't buy from
  the Nexus merchants.
- Monsters and other players move smoothly: they no longer stop and start, slide past where
  they are and back, or jump when the server's updates arrive a little late or bunched up.
- Effects that flash "forever" (some monsters' glows) no longer stop at once.
- Less stutter on the server: opening a dungeon no longer freezes the realm (and every dungeon
  opened from it) for up to a second, the server's memory clean-ups no longer pause the whole
  game, and moves and shots in dungeons and vaults are handled at once instead of up to a tenth
  of a second later.
- Pets that look like a boss now fight like it: Bes, Nut, Geb, the Cube God, Limon, Malphas,
  Septavius, Stheno, Thessal, the Grand Sphinx, Horrific Creation and the other boss pets shoot
  that boss's projectiles in its patterns (without its stuns and other effects). Their item
  tooltips show the new damage.
- Less lag around Assassins and thrown items: every poison vial and every thrown object left an
  invisible marker on the map for 25 minutes, and they piled up for everyone nearby. They now
  vanish once the throw lands. Huntress traps land when the throw does.
- Ability tooltips show what the ability does and its numbers for every class: the
  Necromancer's skull, the Huntress's trap, the Mystic's orb, the Sorcerer's scepter, auras,
  boosts, decoys, teleports and the rest, instead of just a code name like "VampireBlast".
- With the frame rate on (L), the ping to the server shows beside it.
- Playing in the browser: a loading screen with a progress bar shows how much of the game is
  left to download. The download is about a third of the size, the game's files load all at
  once, and your browser keeps them, so the next visit starts much faster.

## 1.1.7.1 (2026-10-16)
- The game has moved to its own server at dodmg.com, so it's up even when the old host's PC is
  off. Your client switches over by itself, and your account, characters and vault came along.
- The game has a website, dodmg.com: play straight from your browser, download the desktop
  client, see how many players are online, look up the controls and read every change.

## 1.1.7 (2026-10-16)
- The game is now called Do Damage: the window and the downloads say so. Your settings and
  saved login carry over.
- A new look to match the website: the sign-in and character screens open on a pixel night sky
  over a castle, with the game's name in pixel letters, and every window, button and tooltip,
  in the menus and in the game, is now dark violet with ember-orange buttons.

## 1.1.6 (2026-10-16)
- Server updates and the 2-hourly restart no longer kick anyone. When an update is waiting, a
  chat message says so: you keep playing where you are (dungeons included) and switch to the
  new version the next time you go to the Nexus, or after an hour at the latest.
- The Wine Cellar Key no longer opens the Wine Cellar from anywhere: like a Wine Cellar
  Incantation, it unlocks the portal Oryx 1 leaves behind. The Nexus Bazaar now sells the Wine
  Cellar Incantation (125 fame) instead of the key.
- Using an item where it can't be used (an incantation away from its locked portal, anything
  without enough mana or while trading) no longer makes it vanish from your inventory: it stays
  where it was, and a potion goes back on its stack.

## 1.1.5 (2026-10-16)
- Cloth and dye items (and a few dungeon objects) show their real names instead of codes like "{textiles.Small_Vine_Cloth}".
- The fight against Oryx now goes all the way: when a realm closes you can follow him from his
  Castle through his Chamber (Oryx 1), his Wine Cellar (Oryx 2), the new Oryx's Sanctuary
  (Oryx 3), the Hash Cellar (Oryx 4) and finally the Doomed Cellar (Oryx 5). Each Oryx opens
  the way to the next, and each kill is announced to the whole server.
- Oryx's Sanctuary is a new dungeon: fight through his nobles and his court until the Sanctum's
  gate opens, then face Oryx 3. Get far away when he shouts "KNEEL!", and kill his three
  Protectors at half health.
- Every stage is harder than the one before, with more health, defense and damage, and better
  loot: Oryx 4 and 5 can drop the Oryx set (Ring of Oryx, the Oryx armours, Oryx Tooth Sword
  and more).
- Oryx 1 no longer gets stuck in his second dance, Oryx 2 calls his guards at half health, and
  Oryx 4 and 5 have new fights. Oryx 5 can't be hurt until his Trees of Doom are cut down.
- The Stone Guardians in Oryx's Castle now use their ring attacks (they never fired before), but
  have less health and defense.
- In Oryx's dungeons the quest arrow points at Oryx, and the portals he drops sit beside his loot
  instead of under it. The Realm Portal after each Oryx stays open for two minutes.
- Oryx's loot was rebalanced to rise with each stage: more stat potions, Life and Mana and
  higher-tier gear the further you go. Potion of Oryx and the Oryx set are now real rarities
  (Oryx 3 to 5), and Oryx 3 can drop Oryx's Escutcheon.
- The Oryx set, Doom Poison and Sword of Dark Enchantment now drop in white bags and are announced
  to everyone; Potion of Oryx and Potion of Power drop in the special bag.
- Each Oryx now leaves a locked portal behind. To follow him, someone has to use the right
  incantation beside it: a Wine Cellar Incantation after Oryx 1, an Oryx's Sanctuary Incantation
  after Oryx 2, a Hash Cellar Incantation after Oryx 3 and a Doomed Cellar Incantation after
  Oryx 4. Incantations drop from the stage before (and the Wine Cellar one from several dungeon
  bosses), and the Nexus Bazaar sells the last three. The Hash and Doomed Cellar Incantations no
  longer open their dungeons from anywhere.
- The 30% weekend loot boost only applies on weekends now. It was quietly applied every day,
  so drops on weekdays are a little lower than before.

## 1.1.4 (2026-10-15)
- Assassin poisons work properly: they ignore the monster's defense, deal their full damage over
  the time they list, and can finish a monster off. Before, a poisoned monster could be left
  alive on 0 health. Other abilities that deal damage can now finish monsters off too.
- Poison tooltips show how much damage the poison does, over how long, how wide it spreads and
  what it deals on impact.
- Pets that fired projectiles in Doomed Realms (Kythal, Cookiee, Bess, Nutt, Sphinx, Twilight
  Sparkle and others) shoot their real projectiles again, with their own spreads and rings,
  instead of a thin red line. Their hits count as yours for loot, experience and quests.
- Pet generator tooltips show what the pet does: its damage per second on one enemy and how
  much HP and MP it restores and how often.
- The shared vault chests in the nexus keep what's in them when the server restarts.

## 1.1.3.1 (2026-10-15)
- In the game, your gold and fame show at the top right of the screen again, with the frame
  rate under them.

## 1.1.3 (2026-10-15)
- Walking onto a square you can't stand on (it happened in the Sprite World) no longer
  disconnects you: the server puts you back where you were.
- A Servers screen again: the Servers button on the character screen (or the "Server:" pill under
  your characters) lists the world servers and how full each is. Click one to play on it.

## 1.1.2 (2026-10-15)
- The character list is more compact, fitting up to four characters a row, and scrolls with the
  mouse wheel when you have more than fit on the screen.
- Your fame and gold are back in the top right corner of the character screen.

## 1.1.1 (2026-10-15)
- Rank stars are back. The character screen shows your rank (hover over it to see which colour
  each range of stars earns) and, on each character, its progress toward the next class quest.
- Choosing a class shows its five class quests and how many you have left in total; a class's
  page shows your best level and fame with it and the fame for its next quest.
- In the Nexus and other safe maps your rank shows at the top left, chat messages show the
  speaker's star, and hovering over a nearby player shows their rank.

## 1.1.0 (2026-10-15)
- The game now keeps itself up to date: new versions download in the background while you play and
  switch over the next time you take a portal. No more zips to download.
- A Changelog window (on the character screen, in the Escape menu, or click the version number)
  shows what's new.
- Item tooltips compare with what you're wearing: better numbers in green, worse in red.
- The chat box edits like a normal text box: arrow keys, Home and End, Shift to select, Ctrl+A,
  Ctrl+C, Ctrl+X, Ctrl+V, and Ctrl+Z to undo.
- Under a quest's health bar: the top 3 damage dealers and your own share.
- Status effects show on players and enemies, with sharper icons and their names rising when
  you gain one; drunk, blind, paused and others change the view as they should.
- Other players' names, coloured for your party and guild, with a star for their rank.
- Hover over a nearby player for their details.
- Potions stack up to 6 in the HP and MP slots; drop or double-click a potion to add it.
- The Nexus fountains heal you when you stand by them.
- The realm's name and player count show over the minimap; the mouse wheel zooms the minimap.
- World zoom between 90% and 130%, a frame rate limit and an unlocked frame rate, and a much
  faster game (often over 1000 frames a second).
- Return home goes back to the main menu; settings and your login are kept between versions.
- The delete button on character cards shows its X again.

## 1.0.0 (2026-10-15)
- The first version of the new client for Windows: the whole game, from signing in to trading,
  guilds, the marketplace, sound and options.
