Release 2.6.9: death replays line up. Deploys the client and website; nobody is disconnected.

## For players

### Death replays
- **Hits land where they hit:** the shot that hurt the character now meets them on screen, the damage number and its sparks coming as it strikes, and the monsters' shots that missed fly past them as they did on the player's screen. The character no longer trails behind the shots (about a tenth to a quarter of a second): they're shown where they really were, walking smoothly along their path instead of gliding from tick to tick. Replays already saved play this way too.

## Details

### Death replays line up

- **What and why:** reported from a real replay (an Elder Undead Lair death): the shots that dealt damage didn't line up with the character. The recording is the server's view: the player's place comes with their moves, a tick or so late and a ping behind, and their hits when their client reported them, while monsters' shots are recorded as the server fires them. Played as recorded, the player trailed the shots by their ping and a tick, and the client's glide added another tick, so a shot crossed the place they had been a moment before, and the numbers came at the wrong moment. Measured on the sample: the drawn player met the shots that hurt them up to half a second after the hit.
- **Client:** `app/src/replay/sync.rs` (new) works out, once per replay, how much earlier the player's recorded places are shown (the lead, 0 to 400 ms, the one that brings the shots that hurt them closest to them; 100 ms on the sample) and, for each hit from a shot, which of its shooter's shots dealt it and when it met them; the hits are then shown at that moment, in the order the server counted them (each shooter's shots reordered if matched the other way round), and the rest (unmatched shots, the ground) as much earlier as the matched ones were. Past the last recorded place the player carries on as they were moving, since the server never hears of their last moments. `app/src/replay.rs`: the player is placed along their recorded places each step (no glide), facing and walking from their path; monsters' shots fly through them (`World::untouchable`, `crates/world`), and the shot matched to a hit ends on them with sparks thrown its way; a tick from before a hit already shown doesn't put the health back up. The hit list and timeline use the times shown.
- **Redeploy:** client only. Nothing on the server or in the replay format changed, so replays already saved line up too. Nobody is disconnected.
- **Wiki:** `website/wiki/progression.md` (death replays: a hit lands as its shot strikes).
- **Not done:** the player's own shots are still drawn as the server heard of them (a ping late against the monsters); hits a monster deals by touching them keep the server's time.

## Checks

- Death replays line up: `cargo test -p bsk-client` 131 passed, with `finds_the_lead_and_the_shot_that_hit` (a walking player the server hears of 150 ms late: the lead found, the shot that hit picked over one fired away, the hit shown when it met them) and `track_glides_between_places_and_jumps_to_gotos`; `cargo test -p bsk-world`; `cargo check --target wasm32-unknown-unknown -p bsk-client`. The reported replay (`lines_up_a_saved_replay`, 22 s, 8 hits from shots): all 8 matched to their shots, each shown within 30 ms of when the server counted it, in order; before, the drawn player met those shots up to 500 ms off. The client played it with `--replay` (the log: "the player 100 ms early, 8 of 8 hits with their shots").
