Release 2.5.9: Oryx 4 is armoured instead of invulnerable, and right click can be bound (deploys the client, the world server and the website; nobody is disconnected)

## For players

- **Dungeons:** Oryx 4 is never invulnerable once awake, in his Inferno or an arena: his shields, warnings and the arena's wake and Doom Ring make him Armored (defense doubled, 50 to 100) instead, and for about half his Inferno fight (was invulnerable four fifths): 2.5 s of warning before each attack (was 4 s), shields up half as long and down twice as long (his usual 3.5 s shut / 1 s open is now 1.75 s / 2 s), and no shield through whole parts of his attacks (Minion Toss, the Artifact Dance's last stretch, Circle Combat's and Bullet Dance's shielded turns, the last spell of Make Minions and the Hadouken now open and shut again). Oryx 1 fights as before.
- **Player sprites:** new looks drawn by players in the Sprite Editor: Archdemon's Soulfire Staff (by Joseph).
- **Controls:** the right mouse button can be bound in Options, Controls like any key (shown as "Right click"), for example to Use ability, to cast at the cursor. It acts as the key only over the world, with no window or chat open; right clicks on the HUD (pinning a player's card, Ctrl+right-click breaking an item down, marking a vault chest) work as before.

## Details

### Right click as a bindable key

- **What changed:** the right mouse button binds as the `ContextMenu` key (`options::RIGHT_CLICK`, saved as `ContextMenu` in the settings file, labelled "Right click"), as the side buttons bind as BrowserBack/Forward. `App::right_button` handles the button: while Options is waiting for a key it binds; otherwise it always queues the usual right click, and, only when something is bound to it, the game is being played, no window, menu or chat is open and the mouse is left of the HUD bar, it also presses the key. Letting go always releases it, so a held ability (the ninja stars) is thrown wherever the button comes up. Scripted `right@` mouse steps go through the same path. The rebinding prompt now reads "Press a key, right click or mouse 4/5…" (translated in all 10 tables, with the new "Right click" label).
- **Files:** `client-rs/app/src/main.rs`, `client-rs/app/src/options.rs`, `client-rs/app/lang/*.json`, `client-rs/CHANGELOG.md`, `website/wiki/getting-started.md`.
- **Redeploy:** the client (and the website for the changelog and wiki). Nothing on the server, nothing in Redis.
- **Not done or known to be off:** not tried in game: the binding is covered by a unit test, the in-game press isn't. A keyboard's Menu key (`ContextMenu`) does the same as right click.

### Oryx 4 armoured instead of invulnerable

- **What changed:** players said Oryx 4 "goes invisible" (his health bar hides while he's invulnerable) far too often. Measured from his states, Oryx 1's fight as Oryx 4 ran it kept him invulnerable about 80% of the time. `OryxSpec` (the settings an Oryx fights Oryx 1's fight with) gains `ShutFor` and `OpenFor` (scales on every shield spell's shut and open times) and `Guarded` (whether the in-attack invulnerable stretches stay: CC grenade turns 0 and 3-5, BD turns 2-5, MT 1-3 and MT Watch, AD Attack 3, and the endless last "Shut Again" of Make Minions and the Hadouken, which unguarded shut for a while and open again). A fourth setting, `Shield`, is the effect all of the fight's shields, warnings and set-up moments give (`Invulnerable` by default); Oryx 4 sets it to `Armored`, so once awake he's never invulnerable. His "Awaken" (4 s as you come near) is armoured too; only "Dormant", with nobody within 15 tiles, stays invulnerable. In an arena, his Doom Ring and his wake (`ArenaWake` gains an effect parameter, invulnerable for the other Oryxes) are armoured. Oryx 4 sets `Pause = 2500` (was 4000), `ShutFor = 0.5`, `OpenFor = 2.0`, `Guarded = false`; Oryx 1 keeps the defaults, so he's unchanged. The short set-up moments (CC/BD set-up, Chase Course and White Planet's start and throw, MT 0, his last stand's start) stay shielded (armoured for Oryx 4). New helpers `O1ShieldOnce`, `O1Guard`, `O1Guarded`; `O1Shield`, `O1Chooser`, `O1Grenades` and `O1Bullets` take the spec. New test `Oryx4IsNeverInvulnerableOnceAwake` fails if any of his states but "Dormant" makes him invulnerable.
- **Files:** `source/WorldServer/logic/db/BehaviorDb.OryxChamber.cs`, `source/WorldServer/logic/db/BehaviorDb.OryxDoomed.cs`, `source/WorldServer/logic/db/BehaviorDb.OryxArena.cs`, `source/Tests/BehaviorStatesTests.cs`, `client-rs/CHANGELOG.md`, `website/wiki/endgame.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected) and the website (changelog, wiki). Nothing in Redis.
- **Not done or known to be off:** the share of time invulnerable is worked out from the state timings, not measured in a full kill; Armored doubles his defense to 100, so weak weapons do little while he's shielded (the minimum damage still lands); in an arena only his wake and Doom Ring changed, his cycle's timings didn't.

### Player sprites (tools/community_sprites.py)

Sprites players drew in the Sprite Editor and admins approved, taken in by `tools/community_sprites.py apply`: drawn into the community sheets (`CommunitySprites8x8.png`, `CommunitySprites16x16.png`), listed in `source/Shared/resources/data/community_sprites.json`, and each new look's item pointed at its cell in both XML copies. Redeploy the client (sheets, XML) and the servers (XML, the catalog, which marks them as in the game); nobody is disconnected, no Redis change.

- #17: `Archdemon's Soulfire Staff`, drawn by `Joseph`, approved by `Meapy`: `communitySprites8x8` `0x5`.

## Checks

- **Oryx 4:** world server builds; `BehaviorStatesTests` pass. On an isolated stack (slot 130: Windows had ports 1973-2172 reserved, so the usual slots couldn't bind), the bot fought a `/spawn`ed Oryx 4 in a vault for 3.5 minutes with `--fight --shots`: he woke, went through Make Minions, Orbitals, White Planet, Artifact Dance, Minion Toss and the Hadouken, took damage between taunts, and the world server logged no exceptions. After the switch to Armored, `BehaviorStatesTests` (with the new Oryx 4 test) pass, and a second 3-minute bot fight ran through Make Minions, Circle Combat, White Planet, Artifact Dance and Orbitals with no exceptions; he lost 20k health in the first 17 s (8k before, when his warnings made him invulnerable).
- **Right click:** `cargo test -p bsk-client` passes (100, with the new `right_click_binds_and_is_kept` and the translation checks); `tools/build_wiki.py` builds. Not tried in a running client.
- Player sprites: `python tools/community_sprites.py apply` drew them into the sheets and pointed the items at them; check them in a `--shot` of the client.
