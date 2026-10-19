# Changelog

What changed in the game, in plain words for players: the client, the servers and the content.
The game shows this in its Changelog window. Newest first.

Every change goes under `## Unreleased` at the top as soon as it lands. Publishing a client
version (`python tools/build_client_rs.py --publish`) needs a section for that version and
refuses while an `Unreleased` one is left, so rename it to `## <version> (<date>)` then. The
version number itself is in `client-rs/Cargo.toml`.

## Unreleased
- The Support Bot in the Nexus has a new tip about Elder dungeons and their better loot.
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
