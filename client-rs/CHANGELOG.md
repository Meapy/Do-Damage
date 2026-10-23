# Changelog

What changed in the game, in plain words for players: the client, the servers and the content.
The game shows this in its Changelog window. Newest first.

Every change goes under `## Unreleased` at the top as soon as it lands. Publishing a client
version (`python tools/build_client_rs.py --publish`) needs a section for that version and
refuses while an `Unreleased` one is left, so rename it to `## <version> (<date>)` then. The
version number itself is in `client-rs/Cargo.toml`.

## Unreleased
- Untiered daggers: Esben drops An Icicle, Oryx the Celestial Mad God (Oryx 3) Avarice, the
  Crystal Prisoner the Crystal Dagger, and the monsters in Oryx 3's, Oryx 4's and Oryx 5's
  dungeons now and then the Bounty Hunter's Dagger. Oryx 3 no longer drops the Doom Dirk or the
  Flying Doom Daggers.
- The Corruption Cutter is now the Infernal Bloodletter and drops from Oryx the Infernal Mad God
  (Oryx 4): each hit makes the enemy bleed for 120 damage over 3 seconds.
- Soul Tipped Dirk: 120-230 damage at 90% rate of fire (was 405-1045 at 100%), and its shots weave
  from side to side.
- The Divine Forge makes the Reforged Dirk of Cronus (two Dirks of Cronus) and the Dagger of the
  Terrible Talon (two Obsidian Daggers), and turns a Dirk of Cronus into a Phantasm Dirk or back
  for 100 fame (they have the same numbers, a different look). The Dirk of Cronus is no longer
  sold in the shop.
- The Legendary, Ascended and Ethereal daggers are ordinary untiered daggers for now, until
  they're reworked.
- Untiered rings now drop where they belong. Oryx the Celestial Mad God (Oryx 3) can drop the
  Battalion Banner, Chancellor's Cranium, Collector's Monocle, Divine Coronation and the
  Celestial God's Horn (the Exalted God's Horn, renamed). Oryx the Infernal Mad God (Oryx 4) can
  drop the Burning Amulet and the Infernal Gems of Adoration and Tenderness. Oryx the Ascended God
  (Oryx 5) can drop the Forge Amulet instead of the Crown. The Elder Tomb's bosses drop the Jade
  Beetle, Pit Vipers the Crystal Bone Ring, Esben his Wedding Ring, the Royal Cnidarian the Mask of
  Cnidaria, Sarcophagi the Mask of Anubis (now in a white bag), Candyland's bosses the Candy Ring,
  its Swoll and Beefy Fairies the Fairy Ring, the Ghost of Skuld the Amulet of Dispersion, Oryx the
  Christmas God the Oryxmas Ornaments, Elder Thessal the Coral Medallion, Elder Limon the Necklace
  of Light and Elder Archdemon Malphas the Ring of the Blood Harvest. The Magical Lodestone drops
  far more often (2%).
- Ring changes: Infernal Gem of Adoration +5 Attack and +5 Dexterity on top of its Health and
  Vitality; Infernal Gem of Tenderness 10 Wisdom and +10 Speed with its Mana; Mask of Cnidaria 200
  Mana; Mask of Lightning +50 Health and +5 Dexterity; Ring of Decades 200 Health; Ring of the
  Burning Sun +8 Vitality; Ring of the Abyss 120 Health and 10 Defense (was 500 and 24); Gold
  Medal 45 Dexterity, 25 Vitality, 25 Speed and 50 Health.
- Santa's Hat drops less often from Oryx the Christmas God (5%), and its description now says what
  it does: a blow that would kill you breaks the hat instead and sends you to the Nexus.
- The Legendary, Ascended and Ethereal rings are ordinary untiered rings for now, until they're
  reworked. The medals and the America Ring are rewards from the staff ("Rewarded by DODMG"). The
  Reforged Forgotten Crown is made at the Divine Forge from two Forgotten Crowns (6,000 fame).
- The Crown, God Rushing, Gods Path, Fallen Crown, Amulet of Someone, Badge of the Proud Hero,
  Creator's Ring and Test Horn are staff-only items: they no longer drop and the shops don't sell
  them. The America Ring and the Amulet of Dispersion left the shops too, and the Crusader's Lost
  Relic no longer drops.
- Set (ST) items no longer drop anywhere, and the Nexus Bazaar no longer sells Enchanted Keys (the
  other way to get them). Enchanted Keys and Mystery ST Crates you already have still work.
- Wiki: untiered items nothing drops yet are listed as Future additions, the staff's rewards have
  their own section, and items the Divine Forge makes say so.
- Arenas: the wave and time box at the top now counts the time in the public arena (it stayed at
  00:00), and shows in the Battle Arena and Free Battle Arena too.
- Arenas: Oryx 2, 3, 4 and 5 have arena versions. They appear in the middle, stay invulnerable for
  their first 5 seconds (and you for 5 seconds too), have less health, and only fight their first
  phase. Oryx 3 is back in the Oryx waves, and from wave 40 a new stage, Oryx's Court, brings the
  courts of Oryx 3, 4 and 5 with Oryx 3, 4 or 5 as its boss. Beating an Oryx in an arena no longer
  tells the whole server.
- Oryx 4 and Oryx 5 event: while it's on, Oryx the Infernal Mad God and Oryx the Ascended God
  deal 20% less damage, and so does everything they bring into their fights (their summoned
  court, elements and moons, stars, Halo Orbs, Pillars of Heaven, Divine Ichor and Ichor
  Sprites). The monsters in the rooms on the way hit as hard as ever. The Nexus tells you when
  you arrive if it's on.
- Legends: characters wearing Doomed Realms gear show all their items again, not empty slots.
- Oryx the Ascended God: in his Last Judgement every second volley of his slowed Radiant Cross is
  turned to fall between the last one's lines (and they come a touch further apart), so you have
  to keep moving. When he concedes he falls back through every Oryx he was, from Oryx 4 down to a
  small Oryx 1, before he can be hurt, and he now leaves a Nexus Portal instead of a Realm Portal.
- New looks for the way to the end of the Oryx chain: the Oryx's Sanctuary, Oryx's Inferno and
  Oryx's Elysium Incantations are seals in each stage's colours (a gold crescent on navy, a flame,
  a winged star); Oryx's Inferno is entered through a horned maw of charred rock round a turning
  vortex of fire, and Oryx's Elysium through a marble temple door with a swirl of light. Their
  locked portals are the same gateways, still and dark.
- Oryx the Ascended God: below 90% health, every second time he changes pattern he throws two
  splats of Divine Ichor, which hatch Ichor Sprites: they have little health, and their shots
  barely hurt but slow you.
- Oryx's Elysium's sea and beaches are redrawn: smooth turquoise shallows, a deeper reef and the
  open sea beyond, wet sand at the waterline and soft sand with the odd shell, all blending into
  each other. Soft white clouds drift over the Landing and out over the sea (just scenery: they
  can't be hit).
- Oryx the Ascended God: the minions he summons can't be hurt and hold their fire for their first
  two seconds, so they don't land on you shooting; the Firmament's stars vanish as soon as it ends;
  the columns in his arena are gone; his Halo Orbs (below 20%) now circle the edge of the arena and
  fire in at its middle; and in his Last Judgement he also fires his Radiant Cross, slowed right
  down, so the arena fills up.
- Oryx the Ascended God (Oryx 5) has a whole new fight. Judgement: the Radiant Cross (twin lances
  and gold arrowheads while he keeps his distance), the Firmament (a turning cross of stars round
  him that slow you on touch) and the Divine Hunt (he chases, shotgun in front and a slowing fan
  behind, throws bleeding bombs, and every ten seconds picks a player by name to hunt). At 80% he
  calls his Host. At 60% he Transcends, heals everyone to full and comes back stronger. At 20% his
  Apotheosis: he turns gold, Halo Orbs circle him, and he chases without rest. At 10% the Last
  Judgement: the gates shut and Pillars of Heaven fire across the arena while he throws his spear,
  until he concedes. Oryx's Elysium is still sealed while the rest of his rework is finished.
- Oryx 5 has ascended: he is now Oryx the Ascended God, in white and gold armour with a spear,
  and his Doomed Cellar is now Oryx's Elysium, a holy island of gold, marble and turquoise sea. The
  road forks round the island past his Ascended Host (Oryx 3's court, ascended, with new attacks)
  to the Oasis and the Pyramid, each with a guardian. Cut down the twelve Trees of Doom (three on
  each road, and three more from each guardian) to open the Holy Gates to his temple, then kill the
  three Wardens circling him to wake him. Oryx's Elysium stays sealed while the rest of his
  rework (his guardians and loot) is finished.
- The Doomed Cellar Incantation is now the Oryx's Elysium Incantation (any you already have still
  work), and the locked portal Oryx 4 leaves behind is the Elysium's gateway, pale and still.
- The locked portals Oryx 2 and Oryx 3 leave behind now look like the gateways they open into,
  dark and still: grey stone for Oryx's Sanctuary, charred red for Oryx's Inferno. They used to
  be the plain locked portal every stage shared.
- The Hash Cellar is now Oryx's Inferno, and its incantation the Oryx's Inferno Incantation
  (any you already have still work). Using it beside the portal Oryx 3 leaves now opens a burning,
  swirling gateway down into the Inferno, a fiery version of the Sanctuary's portal.
- Oryx 3 and Oryx 4 have names of their own: Oryx the Celestial Mad God in his Sanctuary, and
  Oryx the Infernal Mad God in Oryx's Inferno.
- Oryx 3 and Oryx 4's taunts now show only in chat, with no speech bubble over them hiding their
  attacks.
- Oryx the Mad God 3 now starts in the middle of his Sanctum and moves more: he chases you
  while firing missiles, backs away while throwing bombs, and turns invulnerable to dash back to
  the middle before his Kneel ring and his spinners. He charges faster while leaving his spinning
  wheels, still firing at you, and his Protectors now always circle him.
- Oryx's Inferno (Oryx 4) is rebuilt in the red of fire and doom: new tiles and walls, a winding
  Burning Path through four chambers, and a slightly larger round arena with a couple of lava
  pools, where Oryx now waits in the middle. Oryx 4 himself burns: fiery horns and embers in his
  armour. Outside the walls a dark molten sea glows instead of black.
- Oryx 4 now calls his Infernal Court to his side (Infernal Officers and Brimstone Majors) instead
  of Oryx's minions and Knights of Doom. His blue Guardian Elements are now red Infernal Elements,
  which circle him properly: four close and four further out, evenly spaced. His first phase's
  white bolts and green stars are now hellfire.
- New monsters guard Oryx's Inferno's path, the Infernal Court: the Infernal Officer, Brimstone
  Major, Cinder Cleric and Doom Cardinal, Oryx 3's court with Oryx 2's minions' tricks, a little
  tougher, with better drops.
- Forging an ability from two of the tier below now costs the same for every kind of ability,
  as rings do: 30 fame for tier 1, then 70, 90, 200, 400, 500, 1,000 and 2,000 for tier 8. Some
  abilities cost more for a lower tier before (2,000 for tier 6 but 1,000 for tier 7).
- The item icons on the wiki's Divine Forge page sit in the middle of their slots.
- The wiki's guides are up to date with the last week's changes: a new Arenas section (the waves
  and their stages, GO, fame, the leaderboard, and that losing doesn't kill you), the screen's
  layout and the Visuals options, how realm events scale with players, the Oryx loot as it is now,
  trading Soulbound items and your backpack, the nine rank star colours, the Potion of Max Life,
  the name change price, Recent Deaths and the Portal of Cowardice.
- The wiki has a page for the Divine Forge: how to use it, what every tier upgrade costs, and
  every other recipe with its ingredients.
- The wiki's gear pages now list what the Elder monsters drop; they were missing.
- The Divine Forge's filters name the Doomed Realms classes' items (Dual Blades, Fists, Cannon and
  the rest) instead of showing "Item".
- The wiki's gear pages now count the Godlands gods and the many dungeon bosses that share their
  drop lists among the monsters that drop each tier; they were missing before.
- Pirate cannons now drop: they had no tier, so no monster ever dropped one. Rusty to Wave Cannon
  are tiers 0 to 5, and Nature, Shade and Fragment Cannon are tiers 6, 7 and 8, with more damage
  and Dexterity, Vitality and Wisdom than before (they were copies of the Wave Cannon). They drop
  wherever other abilities of their tier do, and the forge makes them from the tier below.
- Tier 9 weapons and tier 10 armor can now drop outside the Tomb's treasure cave: the godlands
  gods drop them now and then, as do the Beer God and the Natural Slime God (the Beer God also drops
  tier 9 armor). The treasure cave's Treasure Pot now drops tier 5 armor too (it skipped it) and
  tier 10 armor.
- Status effects no longer write their names over players and monsters when they start; the
  status icons show them.
- The Divine Forge: a god's anvil and hearth now stands in the middle of the class memorial, in
  the Nexus's south yard west of the arena portals, on a floor of gold. Stand by it and open its
  window to forge: browse the recipes by weapons, abilities, armor, rings and the rest (then by
  sword, bow, tome and so on), search them, or show only what you can make now. Pick one to see
  its ingredients and price, put the items in, and forge it. Its Sell for fame tab shows your
  inventory with what the forge pays for each item under it; pick items and sell them. `/forge`,
  `/forgelist` and `/sellfame` now point you to the forge.
- Every tiered weapon, ability, armor and ring can now be forged from two of the tier below (two
  Rings of Attack make a Ring of Greater Attack), and forging a tiered item never costs more than
  2,000 fame. The forge lists untiered items first, then tiered ones from the highest tier down.
- The forge no longer makes overpowered items like the Crown, seasonal drops, or items no monster
  drops (such as the Potion of Maxy and the forge-only spells and tomes).
- Tiered abilities rebalanced. Quivers do 10% more damage, and from T5 they shoot 3 arrows in a
  bow's arc. Tomes from T5 also give you +2 Attack and +2 Dexterity for 4 seconds. Helms can be
  used again straight away, which restarts their buffs. Shields do 10% less damage. Poisons do
  15% more damage. Skulls do 15% more damage and give +2 Attack at T5, up to +8 at T8. Scepters
  do 20% more damage, and 35% more from T6. The T8 trap gives +7 Attack (it gave less than T7's).
- Seals T7 and T8 also give you +2 and +4 Speed for 4.5 seconds. Using a seal again while its
  health boost lasts now restarts the boost; before, it added half the boost again on top.
- Prisms T7 and T8 make you Damaging and Armored for 4 and 6 seconds. Lutes T7 and T8 give you
  +5 and +10 Dexterity while their Defense boost lasts.
- Shurikens now throw their star when used, for 40 MP (T0) up to 150 MP (T8). Before, using one
  only made the Ninja Speedy and never threw anything.
- Pets now come in three sizes, shown on the pet's tooltip. Every pet heals both your HP and MP:
  Small pets (the pet shop's critters) 15 every 2 seconds, Medium pets (monsters, heroes and the
  named pets) 30 every 2 seconds, and Large pets (the bosses) 45 every 1.5 seconds. Their attacks
  are unchanged for now. Lil' Oryx is now an admin pet and can't be traded.
- A pet's size now shows how big it is drawn: Small pets are a bit over half a character's
  height, Medium pets a little shorter than a character, and Large pets a little taller. Boss pets
  like Nut were twice a character's height.
- Large pets are now the boss pets, which shoot their boss's attacks, and Pancookie and
  Nightmare. Other pets with plain shots of their own (Kythal, Zenco, Shroom, Cerberus Skull) are
  Medium.
- Pets help in new ways: Geb heals you over 2 seconds every 5, Limon makes you speedy for 2
  seconds every 5, Horrific Creation makes you invulnerable for 1 second every 10, and Lil'
  Malphas, Lil' Cube and Skull Shrine give +15 Vitality, Wisdom and Attack for 5 seconds of every
  10. Bes's orange boomerangs break armor, Nut also throws small shots that paralyze for a
  moment, the Kythal pets' big shot weakens, and Zenco's and the Doomed Nature Beast's attacks make
  enemies bleed. Nightmare shoots 3 shots in an arc, Scootaloo's dust reaches much further and
  every fifth shot is a fast wave that slows, and Lil' Oryx shoots like Oryx the Mad God. Kythal,
  Zenco, Shroom and Cerberus Skull do less damage, and Horrific Creation no longer shoots its ring
  of white balls. A pet generator's tooltip lists what else its pet does.
- The wiki's Pets page lists pets by size, with what each one does besides damage and healing.
- Dinosaur's Thessal and Phoenix are no longer sold. If you have one, you keep it.
- The Tomb pets' (Bes, Geb, Nut) ring of spinners now does a little damage. The Sphinx pet now
  uses all three of the Grand Sphinx's shots, and Lil' Malphas aims its shields and fires its
  big bullet.
- Cookie, Reaper and Aceaku's Pet, Cookie's shots, and a few other Doomed Realms sprites drawn
  from a single picture showed as a thin sliver; they now show whole.
- Your vault has a Training Dummy beside the spawn. Shoot it and, 5 seconds after your first hit,
  the chat tells you (and only you) the damage you did and your damage a second (DPS). Keep
  shooting for another reading. It never dies and gives no experience.
- The wiki's Gear statistics page has every class's damage measured on the Training Dummy, with
  its best tiered gear: with the weapon alone, and with 3 uses of its ability.
- A Potion of Oryx is no longer drunk halfway: if a stat it raises is already maxed and that stat's
  potion storage is full, it now does nothing and stays, where before it raised your other stats
  and stayed to be drunk again. The message says which storage is full, and a Potion of Oryx's
  overflow now says which stat's potion went to storage.
- Bosses that build their lair where they appear (the Grand Sphinx and others) no longer do it in
  the arenas, where its walls could shut monsters in where nobody could kill them.
- Right-click a player in the Nearby players list (or on the minimap) to keep their card open,
  then hover the items they wear to see each one's tooltip. Right-click again, or click
  anywhere else, to close it.
- The Sharklord's highest Attack is 70 (was 80), and it starts with 13 (was 15); it still gains 1 to
  2 a level. Sharklords above 70 come down to it.
- Dual Blades ignore enemies' defense: every hit does its full damage. Hits that ignore defense
  show their damage in purple instead of red, and the weapon's tooltip says "Ignores defense".
  Dual Blades and fists show their rate of fire in the tooltip too.
- The Blademaster's jackets from T3 up also give it Attack for a few seconds when used: +3 for 3
  seconds at T3 up to +15 for 8 seconds at T8, shown as Att Boost over the character.
- Stat boost icons over a character are smaller, and every status icon keeps a little space
  around it, so a boost no longer covers the icons beside it.
- Abilities that boost one of your stats for a while now show the right status: a Dexterity,
  Vitality or Wisdom boost showed as another stat's.
- Wands hit about a third harder on every tier and give +3 Attack and +3 Dexterity on every tier,
  on top of their other bonuses: a Priest with the best gear now does about 2,000 damage a second.
- The Knight's highest Defense is 35 (was 40) and its highest health 750 (was 770). Knights
  above the new highest come down to it.
- Armor rebalance (T1 to T15). Leather armor that gives Dexterity now gives Speed too, half as
  much (rounded up): +1 at T4 up to +4 at T15. Every robe gives 20 more MP and health equal to half
  its MP: +20 MP and +10 HP at T1 up to +95 MP and +48 HP at T15. Heavy armor gives +15 health a
  tier from T11 (+75 at T15) and +2 Vitality a tier from T12 (+8 at T15).
- The Monk's fists have their own pictures, one for each tier and coloured after its name, from
  plain Cloth Hand Wraps to the gold and cyan Fists of the Eternal Master.
- New Monks start with Cloth Hand Wraps and a Fountain Droplet, as other classes start with their
  first tier, and /gear now gives a Monk Serpent Fang Gauntlets and a Fountain Stream (it gave the
  Monk no weapon or ability before).
- The Doomed Cellar (Oryx the Mad God 5) is sealed for now while his loot is reworked: a Doomed
  Cellar Incantation won't open his portal and isn't used up if you try, and the shops stop selling
  it until he's back. Hold on to any you have.
- Weapon rebalance. The top tiers of Swords, Daggers, Bows, Wands, Staves, Katanas and Dual Blades
  now give stats: +2 a tier from T11 (+8 at T14) and +2 a tier from T12 (+6 at T14). Swords and
  Katanas give Defense and Attack, Daggers and Bows Dexterity and Attack, Wands Attack and
  Dexterity, Staves Defense, and Dual Blades Defense and Speed.
- T13 and T14 weapons reach further: Swords 4 tiles, Daggers 6, Bows 7.2, Katanas 5.25. Every Wand
  now reaches 8 tiles (was 9) and hits 10% harder; Katanas hit 10% harder on every tier; Dual
  Blades all reach 5 tiles. Bows with several arrows spread them 10% less.
- New Dual Blades for the Blademaster at T13 (Sovereign Blades) and T14 (Eclipse Blades). Rage
  Claw Blades fire at the normal rate now, with damage between the tiers either side of them.
- The Monk has tiered fists at last, T0 to T14, from Cloth Hand Wraps to Fists of the Eternal
  Master, dropping like every other tiered weapon. Fists of Broken Bones is now an untiered boss
  drop instead of a T1 item.
- Every class's ability now has one item at each tier from T0 to T8, and all of them drop. Extra
  copies of the T7 and T8 abilities (and the Samurai's slashes) that never dropped are no longer
  tiered. New: T7 and T8 halos for the Ascendant, a T8 scroll for the Summoner, fountains at every
  tier for the Monk, and oceanic spells at every tier for the Sharklord. Power of the Fountains is
  now T7. The Doomed Realms classes' T7 and T8 abilities drop in your own bag, like everyone else's.
- The wiki has a Gear statistics page: every tiered item side by side with what each tier adds, each
  class's damage a second with each weapon tier, each class's effective health with each armor
  tier, and a table of every class at the top tier: its damage, its health, both per tile of its
  weapon's range, and how far it is from the best class.
- The wiki uses more of a big screen: on wide monitors its pages grow wider, so the big tables show
  more columns before you scroll sideways and the dungeon, class and item cards sit three or more
  to a row. Text stays at a comfortable reading width.
- The wiki's page list is shorter: Commands, Getting stronger and The Oryx chain sit under Getting
  started, and Items, Gear, Gear statistics and Pets under Characters, which opens on Classes.
- Skins keep animating at high dexterity and speed: shooting very fast used to freeze a skin
  on its first attack frame, and running very fast could make its walk look stuck.
- Hovering the inventory and backpack buttons shows the key that switches between them in the
  tooltip, centred under the name, instead of on the button.
- Arenas: everyone is invulnerable for 2.5 seconds as each wave starts, so monsters that appear
  right on top of you can't kill you before you can move.
- The Potion of Max Life still fills your health to your class's maximum, but if it's already
  maxed it now adds 30 health to your potion storage (it used to add 1,000). It's also much
  rarer (1 in 100 from each Elder Tomb boss, was 1 in 10) and drops in a white bag.
- The wiki's Pets page shows how much each pet heals and restores (for example 10-60 HP every
  3 seconds, about 12 a second); healing pets showed nothing before.
- A Portal of Cowardice takes you back the way you came: to a realm from a dungeon opened in a
  realm, and to the Nexus from one opened in the Nexus or in another dungeon (it always went to
  a realm).
- Hovering the inventory or backpack tab shows the key that switches between them, on the tab.

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
