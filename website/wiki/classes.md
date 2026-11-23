---
title: Classes
nav: 20
section: Classes and gear
sprite: shield
eyebrow: Who to play
summary: Every class with its weapon, ability, armor, starting gear, what /gear gives it, and its highest stats.
---
Every class is open from the start. A class uses one kind of weapon, one kind of ability and one
kind of armor, and every class wears a ring. Its **highest stats** are how far stat potions can
take it: a character with all eight at their highest is maxed.

The stats: **HP** and **MP** (health and magic), **Att** (damage), **Def** (takes damage off each
hit), **Spd** (how fast you move), **Dex** (how fast you shoot), **Vit** (health regeneration) and
**Wis** (magic regeneration).

**Support classes** (the Priest's tomes, the Paladin's seals, the Warrior's helms, the
Necromancer's skulls, the Mystic's cursing orbs, the Ascendant's halos, the Father's censers and
Disco Lights) earn loot by helping: healing, buffing and shielding other players (an Ascendant
earns it for every player standing in its halo's forcefield, a Father for every player under his
ward), or debuffing the monsters they fight, gives you
assist credit on those monsters, so you get a share of their loot without shooting them
([Progression](progression.html#loot) has the rules). A Priest's tome gives all its buffs
(Armored, Speedy, Invisible, stat boosts) to the players within its heal range as well as the
Priest; its drawbacks, such as a Curse, stay on the Priest. A tome's speed buff is **Swift**, not
Speedy: half Speedy's speed boost (+17.5% movement instead of +35%), for the time the tome says.

**Party buffs reach well past your own spot.** A Warrior's helm, a Bard's lute and a
Blademaster's jacket reach 6 tiles at T0 and half a tile further every other tier, up to 8 at T8
(7 for an untiered one). A Paladin's seal and a Priest's tome reach 5 tiles at T0 up to 7 at T8
(6.5 untiered), and wisdom from 50 up stretches that further, by half again at 75. Abilities that
already reached further, such as many untiered seals and tomes, keep their range. Each item's
card on its gear page gives its range.

**The sword classes: Frontline.** The Warrior, Knight, Paladin and Pirate fight a few tiles from
what they're hitting, so they share a passive, **Frontline**:

- They take **5% less damage** from every hit, after defense (and after an Ascendant's
  forcefield), including damaging ground.
- Their sword **heals them 1.5%** of the damage it deals, at most 40 health a second, shown over
  them about once a second. Being Sick stops it.
- A **Paladin's** sword heals every ally within 4 tiles as much as it heals him.
- A **Pirate** has a 1 in 10 chance that a shot misses him entirely: no damage and none of its
  effects, and "Lucky!" over his head.

Tiered swords **pierce**, hitting every enemy in a line, and every sword flies straight (some
untiered ones used to wave, loop round the player or come back), over the same range as before.
Their abilities each add something: **Warrior helms** make you Armored for 1 second (T0–T2),
1.5 (T3–T5) or 2 (T6–T8 and the Enthroned Helm); **Knight shields** make you Armored for 1.5,
2 or 2.5 seconds, and their shots leave enemies **Exposed** (20 defense off) for 3 seconds as well
as stunning them, so a shield still helps against a boss that can't be stunned; **Paladin
seals** heal everyone in their range at once, 60 at T0 up to 180 at T8 (15 more each tier),
raised by wisdom like a tome's heal. **Armored** doubles your defense, and armor-piercing shots
don't get through it.

The **newer classes** (Mechanic, Ascendant, Summoner, Druid, Sharklord, Blademaster, King,
Palemaster, Father, Adept, Monk, Pirate and Snowman) have abilities of their own: turrets, summons, halos, pearls and more. A Summoner can keep up to six summons out at a time; they're drawn small (the Dragon and Oryx about a player's size) so they don't hide what you're fighting. Where a class's own kind of item has no tiers,
`/gear` leaves that slot alone.

**The Mechanic's turrets.** A Mechanic's turret blueprint throws a crate to where you aim, up to
8 tiles away, and it unfolds into a turret that turns to the nearest enemy in reach (about 9
tiles), leads it as it moves and keeps to it. Up to **three** stand at once: a fourth makes the
oldest blow up. While you're within 7 tiles of a turret it's **overclocked**, linked to you by a
cable of light, and fires half again as often; the higher tiers fire pairs, and some pierce
through enemies. When its time runs out (12 seconds at T0 up to 20 at T8) it **blows up**,
hitting every enemy round it (150 damage at T0 up to 550 at T8), a ring warning of it for its
last 3 seconds. A blueprint can be used once a second; the War Turret fires little shots very
fast. The Armoire sells the Mechanic four skins at 200 gold (the Steamwright, the Scrapper, the
Arcwright and the Clockwork Mechanic) and the 16 by 16 Titan Artificer at 800.

**The Ascendant's forcefields.** An Ascendant's halo leaves a forcefield where it's cast, and every
player standing in it takes a share less damage from each hit. Standing in several at once stacks
them: the strongest counts in full, the next at half, the next at a quarter and so on, so two 10%
fields take 15% off and three 17%, up to 90% at most.

**The Father's ward.** The Father swings a **censer** (Tin Censer at T0 up to the Censer of the
Last Rite at T8, every tier forged from two of the one below). Its smoke hardens into a grey
**ward** over him and every ally within its range: a shield of so many points, shown in grey on
the HP bar and as a shell of steel plates round the player, that takes the damage of every hit
(after defense) before health does. It lasts until it's spent, when it breaks, or until its time
is up. Wisdom adds to it as it does to a tome's heal. A new ward over one still up stacks on it
at half strength: two wards of 170 give 255, three 297 (the strongest counts in full, each other at
half the one before), never less than the new ward alone, and it lasts as long as the newest; a censer can be swung once every 4 seconds. Damage over time,
such as bleeding, goes straight through it. When a ward from the Censer of the Last Rite breaks,
its shards burst out and hit every enemy within 3 tiles for 400.

{{gen:classes}}
