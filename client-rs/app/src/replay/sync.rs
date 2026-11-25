//! Lining a replay's hits up with the shots that dealt them.
//!
//! The recording is the server's view, and the server hears of the player late: where they stand
//! comes with their moves, and a hit with their client's word that a shot struck, while the shots
//! themselves are recorded as the server fires them. Played as recorded, the player trails their
//! own picture by their ping and a tick or so: a shot meets the place they were a moment before,
//! and the hit's number comes when the server heard of it, not when the shot reached them.
//!
//! So the player is moved along the places the server had for them (`Track`), each shown that
//! much earlier (`Sync::lead`, found per replay as what brings the shots that hurt them closest
//! to them), and each hit is shown when its shot meets them, with the shot that dealt it, which
//! vanishes on them there. Monsters' other shots fly through them: on their screen they missed.

use bsk_data::GameData;
use bsk_net::replay::{from, Hurt};
use bsk_proto::ServerMessage;
use bsk_world::Projectile;

/// The leads tried, ms: from none to a slow connection's.
const MAX_LEAD: i32 = 400;
const LEAD_STEP: usize = 10;
/// The lead with no shots to judge it by: about a tick and a near server's ping.
const DEFAULT_LEAD: i32 = 120;
/// How far from the hit's record its shot may meet the player, before and after, ms.
const SEARCH_BEFORE: i32 = 900;
const SEARCH_AFTER: i32 = 300;
/// The shot's places are looked at this often, ms.
const SAMPLE_MS: i32 = 8;
/// A shot this far off the player at its closest (tiles, along either axis) wasn't the one; and
/// how far off one counts at most when judging a lead.
const MATCH_REACH: f64 = 1.0;
const SCORE_CAP: f64 = 1.5;
/// Shots this far off at the default lead are left out of the rest (tiles).
const PRUNE_REACH: f64 = 5.0;
/// Past their last place they carry on as they moved over this long before it (ms).
const CARRY_JUDGE_MS: i32 = 200;

/// The places the server had for the player, by when they came (ms from the start).
#[derive(Default)]
pub struct Track {
    /// When, where, and whether they were put there (a Goto: no gliding into it).
    samples: Vec<(i32, f64, f64, bool)>,
}

impl Track {
    pub fn is_empty(&self) -> bool {
        self.samples.is_empty()
    }

    fn push(&mut self, at: i32, x: f64, y: f64, jump: bool) {
        // a place again at the same moment (a Goto and its tick): the later stands
        if let Some(last) = self.samples.last_mut().filter(|s| s.0 == at) {
            *last = (at, x, y, last.3 || jump);
            return;
        }
        self.samples.push((at, x, y, jump));
    }

    /// Carries on past the last place `ms` more, as they were moving: the server never hears
    /// where they went in their last moments (the lead), so they'd stand still before dying.
    fn carry_on(&mut self, ms: i32) {
        let n = self.samples.len();
        if n < 2 {
            return;
        }
        let (t1, x1, y1, _) = self.samples[n - 1];
        let back = self.samples.iter().rposition(|s| s.0 <= t1 - CARRY_JUDGE_MS).unwrap_or(0);
        let (t0, x0, y0, _) = self.samples[back];
        // put somewhere lately: no telling
        if t1 <= t0 || self.samples[back + 1..].iter().any(|s| s.3) {
            return;
        }
        let k = ms as f64 / (t1 - t0) as f64;
        self.samples.push((t1 + ms, x1 + (x1 - x0) * k, y1 + (y1 - y0) * k, false));
    }

    /// Where they were at `t` ms, between the places either side of it.
    pub fn at(&self, t: f64) -> Option<(f64, f64)> {
        let i = self.samples.partition_point(|s| (s.0 as f64) <= t);
        let after = self.samples.get(i);
        let Some(&(at, x, y, _)) = i.checked_sub(1).and_then(|i| self.samples.get(i)) else {
            return after.map(|s| (s.1, s.2));
        };
        match after {
            Some(&(bt, bx, by, jump)) if !jump && bt > at => {
                let k = (t - at as f64) / (bt - at) as f64;
                Some((x + (bx - x) * k, y + (by - y) * k))
            }
            _ => Some((x, y)),
        }
    }
}

/// A monster's shot in the recording.
struct Shot {
    at: i32,
    projectile: Projectile,
}

/// How the replay is lined up.
pub struct Sync {
    /// How much earlier than they came the player's places are shown, ms.
    pub lead: i32,
    pub track: Track,
    /// For each hit (as `Viewer::hurts`): when it shows, ms from the start, and the shot that
    /// dealt it (its shooter and bullet id), when found.
    pub hurts: Vec<(i32, Option<(i32, i32)>)>,
}

/// Works out `Sync` for the player `target` from the replay's messages (the snapshot's at 0) and
/// its hits, up to the death at `end`.
pub fn sync<'a>(target: i32, messages: impl Iterator<Item = (i32, &'a ServerMessage)>, hurts: &[(i32, Hurt)], end: i32, data: &GameData) -> Sync {
    let mut track = Track::default();
    let mut shots = Vec::new();
    for (at, m) in messages {
        match m {
            ServerMessage::Update(u) => {
                if let Some(o) = u.new_objs.iter().find(|o| o.status.object_id == target) {
                    track.push(at, o.status.pos.x as f64, o.status.pos.y as f64, true);
                }
            }
            ServerMessage::NewTick(t) => {
                if let Some(s) = t.statuses.iter().find(|s| s.object_id == target) {
                    track.push(at, s.pos.x as f64, s.pos.y as f64, false);
                }
            }
            ServerMessage::Goto(g) if g.object_id == target => track.push(at, g.pos.x as f64, g.pos.y as f64, true),
            ServerMessage::EnemyShoot(e) => {
                let (num, inc) = e.spread.map_or((1, 0.0), |s| (s.num_shots as i32, s.angle_inc as f64));
                for i in 0..num {
                    let angle = e.angle as f64 + inc * i as f64;
                    // its container (the shooter's type) is the hit's, filled in when judged
                    let p = Projectile::new(e.owner_id, e.bullet_id + i, 0, e.bullet_type as i32, angle, at, e.starting_pos.x as f64, e.starting_pos.y as f64, 1.0, e.damage, true);
                    shots.push(Shot { at, projectile: p });
                }
            }
            _ => {}
        }
    }
    // the samples by time (the recording is, a message from another thread may be a ms out)
    track.samples.sort_by_key(|s| s.0);
    track.carry_on(MAX_LEAD);

    // each shot hit's candidates: its shooter's shots that may have reached them about then
    let mut cands: Vec<Vec<usize>> = Vec::with_capacity(hurts.len());
    for (at, h) in hurts {
        let mut c = Vec::new();
        if h.from == from::SHOT && !track.is_empty() {
            for (i, s) in shots.iter().enumerate() {
                if s.projectile.owner_id != h.source_id || s.at > at + SEARCH_AFTER {
                    continue;
                }
                let Some(desc) = desc(data, h.source_type, &s.projectile) else { continue };
                if s.at + desc.lifetime < at - SEARCH_BEFORE {
                    continue;
                }
                if closest(&s.projectile, s.at, desc, *at, &track, DEFAULT_LEAD).is_some_and(|(d, _)| d <= PRUNE_REACH) {
                    c.push(i);
                }
            }
        }
        cands.push(c);
    }

    // the lead that brings those shots closest to them
    let score = |lead: i32| -> f64 {
        let mut sum = 0.0;
        for ((at, h), c) in hurts.iter().zip(&cands) {
            if c.is_empty() {
                continue;
            }
            let best = c
                .iter()
                .filter_map(|&i| {
                    let s = &shots[i];
                    closest(&s.projectile, s.at, desc(data, h.source_type, &s.projectile)?, *at, &track, lead).map(|(d, _)| d)
                })
                .fold(f64::MAX, f64::min);
            sum += best.min(SCORE_CAP);
        }
        // a hair toward the usual, between leads that fit as well
        sum + (lead - DEFAULT_LEAD).abs() as f64 * 1e-5
    };
    let judged = cands.iter().any(|c| !c.is_empty());
    let lead = if judged { (0..=MAX_LEAD).step_by(LEAD_STEP).min_by(|a, b| score(*a).total_cmp(&score(*b))).unwrap_or(DEFAULT_LEAD) } else { DEFAULT_LEAD };

    // each hit's shot at that lead (one shot one hit, unless it pierces), and when it met them
    let mut used: Vec<(i32, i32)> = Vec::new();
    let mut out: Vec<(i32, Option<(i32, i32)>)> = Vec::with_capacity(hurts.len());
    let mut early = Vec::new();
    for ((at, h), c) in hurts.iter().zip(&cands) {
        let mut best: Option<(f64, f64, (i32, i32))> = None;
        for &i in c {
            let p = &shots[i].projectile;
            let Some(desc) = desc(data, h.source_type, p) else { continue };
            let key = (p.owner_id, p.bullet_id);
            if !desc.multi_hit && used.contains(&key) {
                continue;
            }
            if let Some((d, t)) = closest(p, shots[i].at, desc, *at, &track, lead) {
                if d <= MATCH_REACH && best.is_none_or(|b| d < b.0) {
                    best = Some((d, t, key));
                }
            }
        }
        match best {
            Some((_, t, key)) => {
                used.push(key);
                let t = (t.round() as i32).clamp(0, end);
                early.push(at - t);
                out.push((t, Some(key)));
            }
            None => out.push((*at, None)),
        }
    }
    // each shooter's shots that struck, in the order the server counted their hits (two a moment
    // apart may have been matched the other way round, and the health would go back up between)
    let mut shooters: Vec<i32> = hurts.iter().map(|h| h.1.source_id).collect();
    shooters.sort_unstable();
    shooters.dedup();
    for shooter in shooters {
        let found: Vec<usize> = (0..out.len()).filter(|&i| out[i].1.is_some() && hurts[i].1.source_id == shooter).collect();
        let mut struck: Vec<(i32, Option<(i32, i32)>)> = found.iter().map(|&i| out[i]).collect();
        struck.sort_by_key(|s| s.0);
        for (&i, s) in found.iter().zip(struck) {
            out[i] = s;
        }
    }
    // the hits found no shot for (and the ground's, which their client also tells of) come as
    // much earlier as the others did
    early.sort_unstable();
    let shift = early.get(early.len() / 2).copied().unwrap_or(0).max(0);
    for ((at, h), o) in hurts.iter().zip(out.iter_mut()) {
        if o.1.is_none() && (h.from == from::SHOT || h.from == from::GROUND) {
            o.0 = (at - shift).clamp(0, end);
        }
    }
    // and never before the one counted before them
    for i in 1..out.len() {
        out[i].0 = out[i].0.max(out[i - 1].0);
    }
    Sync { lead, track, hurts: out }
}

fn desc<'a>(data: &'a GameData, container: u16, p: &Projectile) -> Option<&'a bsk_data::ProjectileDesc> {
    data.objects.get(&container)?.projectiles.get(&p.bullet_type)
}

/// How close the shot fired at `fired` comes to the player (shown `lead` ms early) around the hit
/// recorded at `hurt`, tiles along either axis, and when (ms from the start).
fn closest(p: &Projectile, fired: i32, desc: &bsk_data::ProjectileDesc, hurt: i32, track: &Track, lead: i32) -> Option<(f64, f64)> {
    let from = (hurt - SEARCH_BEFORE - fired).max(0);
    let to = (hurt + SEARCH_AFTER - fired).min(desc.lifetime);
    let mut best: Option<(f64, f64)> = None;
    let mut e = from;
    while e <= to {
        let (x, y) = p.position_at(desc, e);
        let t = (fired + e) as f64;
        let (px, py) = track.at(t + lead as f64)?;
        let d = (x - px).abs().max((y - py).abs());
        if best.is_none_or(|b| d < b.0) {
            best = Some((d, t));
        }
        e += SAMPLE_MS;
    }
    best
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn track_glides_between_places_and_jumps_to_gotos() {
        let mut t = Track::default();
        t.push(0, 10.0, 10.0, true);
        t.push(100, 11.0, 10.0, false);
        t.push(200, 20.0, 20.0, true);
        t.push(300, 21.0, 20.0, false);
        assert_eq!(t.at(-50.0), Some((10.0, 10.0)));
        assert_eq!(t.at(50.0), Some((10.5, 10.0)));
        // up to a Goto it waits where it was, then is there at once
        assert_eq!(t.at(150.0), Some((11.0, 10.0)));
        assert_eq!(t.at(200.0), Some((20.0, 20.0)));
        assert_eq!(t.at(250.0), Some((20.5, 20.0)));
        assert_eq!(t.at(1000.0), Some((21.0, 20.0)));
        assert_eq!(Track::default().at(0.0), None);
    }

    #[test]
    fn finds_the_lead_and_the_shot_that_hit() {
        use bsk_proto::{EnemyShoot, NewTick, ObjectData, ObjectStatus, Update, WorldPos};
        let mut data = GameData::default();
        data.add_objects(
            r#"<Objects><Object type="0x30" id="Gunner"><Class>Character</Class><Enemy/>
               <Projectile id="0"><ObjectId>Blade</ObjectId><Speed>100</Speed><LifetimeMS>1000</LifetimeMS></Projectile></Object></Objects>"#,
        )
        .unwrap();
        // they walk up at 5 tiles a second; the server has each place 150 ms late
        let truly = |c: i32| (10.0, 5.0 + 0.005 * c as f64);
        let status = |id: i32, (x, y): (f64, f64)| ObjectStatus { object_id: id, pos: WorldPos { x: x as f32, y: y as f32 }, stats: Default::default() };
        let mut messages = vec![(0, ServerMessage::Update(Update { tiles: Default::default(), new_objs: vec![ObjectData { object_type: 0x0300, status: status(1, truly(-150)) }].into(), drops: Default::default() }))];
        for s in (100..=2000).step_by(100) {
            messages.push((s, ServerMessage::NewTick(NewTick { tick_id: s / 100, tick_time: 100, statuses: vec![status(1, truly(s - 150))].into() })));
        }
        // a shot across their path that meets them at 1500, and one fired away from them
        let shot = |bullet_id: i32, angle: f32| ServerMessage::EnemyShoot(EnemyShoot { bullet_id, owner_id: 7, bullet_type: 0, starting_pos: WorldPos { x: 5.0, y: 12.5 }, angle, damage: 50, spread: None });
        messages.push((1000, shot(1, 0.0)));
        messages.push((1000, shot(2, std::f32::consts::PI)));
        messages.sort_by_key(|m| m.0);
        let hurt = Hurt { from: from::SHOT, source_type: 0x30, source_id: 7, raw: 50, dealt: 40, hp: 60, max_hp: 100, flags: 0 };
        // the server heard of the hit 60 ms after it
        let s = sync(1, messages.iter().map(|(at, m)| (*at, m)), &[(1560, hurt)], 2000, &data);
        // (to the step the leads are tried at, and the shot's places looked at)
        assert!((s.lead - 150).abs() <= LEAD_STEP as i32, "lead {}", s.lead);
        assert_eq!(s.hurts[0].1, Some((7, 1)));
        assert!((s.hurts[0].0 - 1500).abs() <= SAMPLE_MS, "shown at {}", s.hurts[0].0);
        // shown that much early, they're about where they truly were
        let (x, y) = s.track.at(1500.0 + s.lead as f64).unwrap();
        assert!((x - 10.0).abs() < 1e-3 && (y - 12.5).abs() <= 0.005 * LEAD_STEP as f64 + 1e-3, "at {x}, {y}");
    }

    /// A saved replay (`BSK_REPLAY=<file>`), with the game's data: how its hits line up.
    #[cfg(not(target_arch = "wasm32"))]
    #[test]
    #[ignore]
    fn lines_up_a_saved_replay() {
        let path = std::env::var("BSK_REPLAY").expect("BSK_REPLAY=<a .bskreplay>");
        let r = bsk_net::replay::read_file(std::fs::read(path).unwrap()).unwrap();
        let source = bsk_assets::AssetSource::locate().unwrap();
        let manifest = pollster::block_on(source.manifest()).unwrap();
        let data = pollster::block_on(GameData::load(&source, &manifest)).unwrap();
        let decode = |id: u8, b: &[u8]| ServerMessage::decode(id, b).ok();
        let snapshot: Vec<ServerMessage> = r.snapshot.iter().filter_map(|(id, b)| decode(*id, b)).collect();
        let mut messages: Vec<(i32, ServerMessage)> = Vec::new();
        let mut hurts = Vec::new();
        for (at, rec) in &r.records {
            match rec {
                bsk_net::replay::Record::Message(id, b) => messages.extend(decode(*id, b).map(|m| (*at, m))),
                bsk_net::replay::Record::Hurt(h) => hurts.push((*at, h.clone())),
                _ => {}
            }
        }
        messages.sort_by_key(|m| m.0);
        let all = snapshot.iter().map(|m| (0, m)).chain(messages.iter().map(|(at, m)| (*at, m)));
        let s = sync(r.target_id, all, &hurts, r.duration, &data);
        println!("lead {} ms", s.lead);
        for ((at, h), (shown, shot)) in hurts.iter().zip(&s.hurts) {
            println!("  hit at {at:6} ({} from {}): shown at {shown:6} ({:+} ms), shot {shot:?}", h.dealt, h.source_id, shown - at);
        }
        let shots = hurts.iter().filter(|h| h.1.from == from::SHOT).count();
        assert_eq!(s.hurts.iter().filter(|h| h.1.is_some()).count(), shots, "every shot's hit found its shot");
        assert!(s.hurts.windows(2).all(|w| w[0].0 <= w[1].0), "in the order they were counted");
    }
}
