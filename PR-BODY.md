Release 2.5.9: Oryx 4 is invulnerable far less often (deploys the world server and the website; nobody is disconnected)

## For players

- **Dungeons:** Oryx 4 in his Inferno is invulnerable about half his fight (was four fifths): 2.5 s of warning before each attack (was 4 s), shields shut half as long and open twice as long (his usual 3.5 s shut / 1 s open is now 1.75 s / 2 s), and no shield through whole parts of his attacks (Minion Toss, the Artifact Dance's last stretch, Circle Combat's and Bullet Dance's shielded turns, the last spell of Make Minions and the Hadouken now open and shut again). Oryx 1 fights as before.

## Details

### Oryx 4 invulnerable less often

- **What changed:** players said Oryx 4 "goes invisible" (his health bar hides while he's invulnerable) far too often. Measured from his states, Oryx 1's fight as Oryx 4 ran it kept him invulnerable about 80% of the time. `OryxSpec` (the settings an Oryx fights Oryx 1's fight with) gains `ShutFor` and `OpenFor` (scales on every shield spell's shut and open times) and `Guarded` (whether the in-attack invulnerable stretches stay: CC grenade turns 0 and 3-5, BD turns 2-5, MT 1-3 and MT Watch, AD Attack 3, and the endless last "Shut Again" of Make Minions and the Hadouken, which unguarded shut for a while and open again). Oryx 4 sets `Pause = 2500` (was 4000), `ShutFor = 0.5`, `OpenFor = 2.0`, `Guarded = false`; Oryx 1 keeps the defaults, so he's unchanged. The short set-up moments (CC/BD set-up, Chase Course and White Planet's start and throw, MT 0, his last stand's start) stay invulnerable. New helpers `O1ShieldOnce`, `O1Guard`, `O1Guarded`; `O1Shield`, `O1Grenades` and `O1Bullets` take the spec.
- **Files:** `source/WorldServer/logic/db/BehaviorDb.OryxChamber.cs`, `source/WorldServer/logic/db/BehaviorDb.OryxDoomed.cs`, `client-rs/CHANGELOG.md`, `website/wiki/endgame.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected) and the website (changelog, wiki). Nothing in Redis.
- **Not done or known to be off:** the share of time invulnerable is worked out from the state timings, not measured in a full kill; the arena version of Oryx 4 (his old cycle) isn't touched.

## Checks

- **Oryx 4:** world server builds; `BehaviorStatesTests` pass. On an isolated stack (slot 130: Windows had ports 1973-2172 reserved, so the usual slots couldn't bind), the bot fought a `/spawn`ed Oryx 4 in a vault for 3.5 minutes with `--fight --shots`: he woke, went through Make Minions, Orbitals, White Planet, Artifact Dance, Minion Toss and the Hadouken, took damage between taunts, and the world server logged no exceptions.
