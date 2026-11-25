//! Death replays: the last seconds a character saw before it died, as the world server recorded
//! them (`core/replay/ReplayRecorder.cs`, read by `bsk_net::replay`), played back from the death's
//! summary (the graveyard, the legends' Recent Deaths, and your own death).
//!
//! The recording is what the Scrying Glass shows of a player (`glass.rs`): a snapshot of what was
//! in sight, then the `Update`s, `NewTick`s, shots and effects their client got, so it plays as
//! a little world of its own, on its own clock, which pauses, runs slower or faster, and goes
//! back (built again from the snapshot and run to the moment). With it are the hits the player
//! took as the server counted them: each shows as a damage number on them as it lands, a mark on
//! the timeline and a line in the list beside the picture (what dealt it, its damage before and
//! after defense, the health left), so a death can be checked against what was seen.
//!
//! Its Download button saves the recording as a file (`bsk_net::replay::FILE_EXTENSION`): into
//! Downloads on the desktop, a download in a browser. Dropped on the window (or named with
//! `--replay <file>`), a saved one plays again, long after the server has let it go.

use crate::ground::Ground;
use crate::hud::{stat, Ui};
use crate::i18n::{tf, tn, tr};
use crate::theme::{self, color, Kind};
use crate::view::{self, Camera, Floater, Scene, Show};
use bsk_data::GameData;
use bsk_net::replay::{flag, from, Hurt, Record, Replay};
use bsk_proto::{ServerMessage, StatValue};
use bsk_world::{MoveInput, Projectile, World};
use winit::keyboard::KeyCode;

/// The picture's clock at the replay's start (the world's times are this plus ms into it).
const CLOCK: i32 = 100_000;
/// How far the world is moved at a time, ms (as a frame).
const STEP_MS: i32 = 16;
/// How many tiles the picture shows from top to bottom.
const TILES: f64 = 16.0;
/// The speeds it plays at.
const SPEEDS: [f64; 5] = [0.25, 0.5, 1.0, 2.0, 4.0];
const NORMAL_SPEED: usize = 2;
/// The arrow keys step this far, ms.
const STEP_KEY_MS: f64 = 1000.0;
/// A hit's line, clicked, plays from this long before it, ms.
const LEAD_MS: f64 = 1500.0;
/// How quickly the camera catches up with the player (ms to close about two thirds of the gap),
/// and how far they must jump for it to cut straight there, tiles.
const CAMERA_EASE_MS: f64 = 110.0;
const CAMERA_JUMP: f64 = 4.0;

/// What happens when.
enum Event {
    Message(ServerMessage),
    /// One of `Viewer::hurts`.
    Hurt(usize),
    Death,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Play,
    Speed,
    Download,
    Back,
    Timeline,
    Row(usize),
}

/// What the viewer asks of the app.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Out {
    Back,
}

pub struct Viewer {
    pub file: Replay,
    snapshot: Vec<ServerMessage>,
    events: Vec<(i32, Event)>,
    hurts: Vec<(i32, Hurt)>,
    /// Where the death is, ms from the start.
    end: i32,
    /// How far in we are, ms, and how far the world has been moved.
    t: f64,
    stepped: i32,
    /// The next event to happen.
    next: usize,
    playing: bool,
    speed: usize,
    world: Option<World>,
    ground: Ground,
    particles: crate::particles::Particles,
    floaters: Vec<Floater>,
    camera: Option<(f64, f64)>,
    hits: Vec<(Hit, [f32; 4])>,
    /// The timeline is held (dragging through it).
    scrubbing: bool,
    timeline: [f32; 4],
    /// Rows of the hit list scrolled up from its end.
    scroll: usize,
    out: Vec<Out>,
    pub clicked: bool,
    /// What the Download button did: where it was saved, or why it wasn't (an error).
    saved: Option<(String, bool)>,
}

impl Viewer {
    pub fn new(file: Replay) -> Self {
        let decode = |id: u8, body: &[u8]| match ServerMessage::decode(id, body) {
            Ok(m) => Some(m),
            Err(e) => {
                log::warn!("replay: message {id} didn't read: {e}");
                None
            }
        };
        let snapshot = file.snapshot.iter().filter_map(|(id, b)| decode(*id, b)).collect();
        let mut events = Vec::with_capacity(file.records.len());
        let mut hurts = Vec::new();
        let mut end = file.duration;
        for (at, r) in &file.records {
            match r {
                Record::Message(id, b) => {
                    if let Some(m) = decode(*id, b) {
                        events.push((*at, Event::Message(m)));
                    }
                }
                Record::Hurt(h) => {
                    events.push((*at, Event::Hurt(hurts.len())));
                    hurts.push((*at, h.clone()));
                }
                Record::Death(_) => {
                    end = *at;
                    events.push((*at, Event::Death));
                }
            }
        }
        // in time order (the recording is, but a message from another thread may be a ms out)
        events.sort_by_key(|e| e.0);
        log::info!("replay of {} ({} s, {} messages, {} hits)", file.name, end / 1000, events.len(), hurts.len());
        Viewer {
            file,
            snapshot,
            events,
            hurts,
            end: end.max(1),
            t: 0.0,
            stepped: 0,
            next: 0,
            playing: true,
            speed: NORMAL_SPEED,
            world: None,
            ground: Ground::default(),
            particles: Default::default(),
            floaters: Vec::new(),
            camera: None,
            hits: Vec::new(),
            scrubbing: false,
            timeline: [0.0; 4],
            scroll: 0,
            out: Vec::new(),
            clicked: false,
            saved: None,
        }
    }

    pub fn take_out(&mut self) -> Vec<Out> {
        std::mem::take(&mut self.out)
    }

    /// The world as it was at the start.
    fn rebuild(&mut self, data: &GameData) {
        let size = (self.file.size as i32).max(1);
        let mut world = World::new(size, size, &self.file.world, 0);
        world.background = self.file.background;
        world.watched = Some(self.file.target_id);
        for m in &self.snapshot {
            apply(&mut world, &mut self.particles, &mut self.floaters, m, CLOCK, data, true);
        }
        self.world = Some(world);
        self.particles.clear();
        self.floaters.clear();
        self.camera = None;
        self.stepped = 0;
        self.next = 0;
    }

    /// Moves the world to `to` ms in, everything that happened on the way happening in turn;
    /// `quiet` (going back) leaves out the sparks and numbers, which wouldn't be seen.
    fn run_to(&mut self, to: i32, data: &GameData, quiet: bool) {
        while let Some((at, _)) = self.events.get(self.next).filter(|e| e.0 <= to) {
            let at = *at;
            self.step_world(at, data, quiet);
            let Some(world) = self.world.as_mut() else { return };
            let now = CLOCK + at;
            match &self.events[self.next].1 {
                Event::Message(m) => apply(world, &mut self.particles, &mut self.floaters, m, now, data, quiet),
                Event::Hurt(i) => {
                    let h = &self.hurts[*i].1;
                    let id = self.file.target_id;
                    // the health the server left them with: their bar shows it at once
                    if let Some(o) = world.objects.get_mut(&id) {
                        o.stats.insert(stat::HP, StatValue::Int(h.hp.max(0)));
                    }
                    if !quiet && h.flags & flag::NO_HARM == 0 {
                        if h.flags & flag::DODGED != 0 {
                            self.floaters.push(Floater { object_id: id, text: tr("Lucky!").to_owned(), color: color::GOOD & 0xFF_FFFF, at: now, life: 1000, others: false });
                        } else {
                            self.floaters.push(Floater::hit(id, h.dealt, h.flags & flag::PIERCE != 0, now));
                            self.particles.damaged(world, id, h.hp <= 0);
                        }
                    }
                }
                Event::Death => {
                    if let Some(o) = world.objects.get(&self.file.target_id) {
                        let (x, y) = (o.x, o.y);
                        if !quiet {
                            self.particles.damaged(world, o.id, true);
                            self.particles.aoe(x, y, 2.5, 0xE04040);
                        }
                    }
                }
            }
            self.next += 1;
        }
        self.step_world(to, data, quiet);
    }

    fn step_world(&mut self, to: i32, data: &GameData, quiet: bool) {
        let Some(world) = self.world.as_mut() else { return };
        while self.stepped < to {
            let s = (self.stepped + STEP_MS).min(to);
            let (now, dt) = (CLOCK + s, s - self.stepped);
            world.update(now, dt, &MoveInput::default(), data);
            world.update_projectiles(now, data);
            self.particles.impacts(world);
            if !quiet {
                self.particles.update(world, now, dt);
            }
            self.stepped = s;
        }
        if quiet {
            world.impacts.clear();
        }
    }

    /// Goes to `t` ms in: on from here, or built again from the start to go back.
    fn seek(&mut self, t: f64, data: &GameData) {
        let t = t.clamp(0.0, self.end as f64);
        if (t as i32) < self.stepped || self.world.is_none() {
            self.rebuild(data);
        }
        self.t = t;
        let quiet = true;
        self.run_to(t as i32, data, quiet);
        self.particles.clear();
        self.floaters.clear();
        self.camera = None;
    }

    /// Each frame: plays on by `dt` real ms.
    pub fn tick(&mut self, data: &GameData, dt: i32) {
        if self.world.is_none() {
            self.rebuild(data);
        }
        if self.playing && !self.scrubbing {
            self.t = (self.t + dt.max(0) as f64 * SPEEDS[self.speed]).min(self.end as f64);
            if self.t >= self.end as f64 {
                self.playing = false;
            }
        }
        let to = self.t as i32;
        self.run_to(to, data, false);
        let now = CLOCK + self.stepped;
        self.floaters.retain(|f| now - f.at < f.life);
        // the camera after the player
        let target = self.world.as_ref().and_then(|w| w.objects.get(&self.file.target_id)).map(|o| (o.x, o.y));
        if let Some((tx, ty)) = target {
            let dt = if self.playing { dt.max(0) as f64 * SPEEDS[self.speed] } else { 1e9 };
            self.camera = Some(match self.camera {
                Some((x, y)) if (tx - x).hypot(ty - y) < CAMERA_JUMP => {
                    let k = 1.0 - (-dt / CAMERA_EASE_MS).exp();
                    (x + (tx - x) * k, y + (ty - y) * k)
                }
                _ => (tx, ty),
            });
        }
    }

    fn toggle(&mut self, data: &GameData) {
        if !self.playing && self.t >= self.end as f64 {
            self.seek(0.0, data);
        }
        self.playing = !self.playing;
    }

    pub fn key(&mut self, code: KeyCode, data: &GameData) {
        match code {
            KeyCode::Escape | KeyCode::Backspace => self.out.push(Out::Back),
            KeyCode::Space | KeyCode::KeyK => self.toggle(data),
            KeyCode::ArrowLeft | KeyCode::KeyJ => self.seek(self.t - STEP_KEY_MS, data),
            KeyCode::ArrowRight | KeyCode::KeyL => self.seek(self.t + STEP_KEY_MS, data),
            KeyCode::ArrowUp => self.speed = (self.speed + 1).min(SPEEDS.len() - 1),
            KeyCode::ArrowDown => self.speed = self.speed.saturating_sub(1),
            KeyCode::Home => self.seek(0.0, data),
            KeyCode::End => self.seek(self.end as f64, data),
            _ => {}
        }
    }

    pub fn click(&mut self, pos: (f64, f64), data: &GameData) {
        let Some(&(hit, _)) = self.hits.iter().find(|(_, r)| inside(*r, pos)) else { return };
        self.clicked = true;
        match hit {
            Hit::Play => self.toggle(data),
            Hit::Speed => self.speed = (self.speed + 1) % SPEEDS.len(),
            Hit::Back => self.out.push(Out::Back),
            Hit::Download => {
                self.saved = Some(match save(&self.file) {
                    Ok(path) => (tf!("Saved to {path}", path), false),
                    Err(e) => (tf!("The replay couldn't be saved ({e}).", e), true),
                });
            }
            Hit::Timeline => {
                self.scrubbing = true;
                self.scrub(pos, data);
            }
            Hit::Row(i) => {
                let at = self.hurts[i].0 as f64;
                self.seek(at - LEAD_MS, data);
                self.playing = true;
            }
        }
    }

    pub fn release(&mut self) {
        self.scrubbing = false;
    }

    /// The mouse is at `pos`: while the timeline is held, it goes there.
    pub fn drag(&mut self, pos: (f64, f64), data: &GameData) {
        if self.scrubbing {
            self.scrub(pos, data);
        }
    }

    fn scrub(&mut self, pos: (f64, f64), data: &GameData) {
        let r = self.timeline;
        if r[2] <= 0.0 {
            return;
        }
        let k = ((pos.0 as f32 - r[0]) / r[2]).clamp(0.0, 1.0) as f64;
        let t = k * self.end as f64;
        if (t - self.t).abs() >= 1.0 {
            self.seek(t, data);
        }
    }

    pub fn wheel(&mut self, notches: i32) {
        self.scroll = (self.scroll as i32 + notches).clamp(0, self.hurts.len() as i32) as usize;
    }

    /// Fills the window: the picture, the hits beside it, the controls under it.
    pub fn draw(&mut self, ui: &mut Ui, show: Show, w: f32, h: f32, mouse: (f64, f64)) {
        self.hits.clear();
        let s = crate::screens::scale(w, h);
        let pad = (14.0 * s).round();
        ui.rect(0.0, 0.0, w, h, color::BACKGROUND, 1.0);

        // who, and how they died
        let class = ui.data.objects.get(&self.file.object_type).map(|d| d.display_id.clone()).unwrap_or_default();
        let ts = (22.0 * s).round();
        ui.label_fit(tr("Death replay"), pad, pad, w * 0.5, ts, true, color::TEXT);
        let sub = tf!("{name} ({class}), killed by {killer} in {world}", name = self.file.name, class = class, killer = self.file.killer, world = self.file.world);
        ui.label_fit(&sub, pad, pad + ts * 1.25, w - 2.0 * pad, 14.0 * s, false, color::TEXT_DIM);
        let top = pad + ts * 1.25 + 24.0 * s;

        let controls_h = (52.0 * s).round();
        let bottom = h - pad - controls_h - 10.0 * s;
        let pw = (310.0 * s).min(w * 0.34).round();
        let list = [w - pad - pw, top, pw, bottom - top];
        let pic = [pad, top, list[0] - pad - 10.0 * s, bottom - top];

        self.draw_picture(ui, show, pic, s);
        self.draw_hits(ui, list, s, mouse);
        self.draw_controls(ui, [pad, h - pad - controls_h, w - 2.0 * pad, controls_h], s, mouse);
    }

    fn draw_picture(&mut self, ui: &mut Ui, show: Show, r: [f32; 4], s: f32) {
        ui.rect(r[0], r[1], r[2], r[3], 0x05030A, 1.0);
        let now = CLOCK + self.stepped;
        let data = ui.data;
        if let (Some(world), Some((x, y))) = (self.world.as_ref(), self.camera) {
            let scale = r[3] as f64 / TILES;
            let (cx, cy) = ((r[0] + r[2] / 2.0) as f64, (r[1] + r[3] * 0.56) as f64);
            let z = world.objects.get(&self.file.target_id).filter(|_| world.levels.is_some()).map_or(0.0, |t| view::height_of(world, data, t));
            let camera = Camera::new(x, y, 0.0, scale, cx, cy).at_height(z).snapped();
            let roof = view::roof(world, data, &camera);
            let scene = Scene {
                sprites: ui.sprites,
                data,
                world,
                camera,
                time: now,
                floaters: &self.floaters,
                bubbles: &[],
                particles: &self.particles,
                show: Show { others_shots: 1.0, player_alpha: 1.0, damage_numbers: true, others_damage: true, ..show },
                hallucinating: false,
                roof,
                me_alpha: 1.0,
                inset: Some(r),
                glass: None,
            };
            view::draw(&scene, ui.renderer, ui.redrawer, &mut self.ground, ui.fonts, ui.text);
        }
        // their health, top left
        let target = self.world.as_ref().and_then(|w| w.objects.get(&self.file.target_id));
        let (hp, max) = target.map_or((0, 0), |o| (o.stat_int(stat::HP).unwrap_or(0), o.stat_int(stat::MAX_HP).unwrap_or(0)));
        let dead = self.t >= self.end as f64;
        let hp = if dead { 0 } else { hp.max(0) };
        let bw = (220.0 * s).min(r[2] - 20.0 * s);
        let b = [r[0] + 10.0 * s, r[1] + 10.0 * s, bw, 20.0 * s];
        theme::fill(ui.renderer, [b[0] - 3.0 * s, b[1] - 3.0 * s, b[2] + 6.0 * s, b[3] + 6.0 * s], 6.0 * s, 0x000000, 0.55);
        theme::fill(ui.renderer, b, 4.0 * s, color::TRACK, 1.0);
        if max > 0 {
            let k = (hp as f32 / max as f32).clamp(0.0, 1.0);
            theme::fill(ui.renderer, [b[0], b[1], b[2] * k, b[3]], 4.0 * s, 0xE03434, 1.0);
        }
        let line = tf!("{hp} / {max} HP", hp, max);
        let size = 12.0 * s;
        let lw = ui.measure(&line, size, true);
        ui.label(&line, b[0] + (b[2] - lw) / 2.0, b[1] + (b[3] - size * 1.22) / 2.0, size, true, color::TEXT);
        // the end: who did it
        if dead {
            ui.rect(r[0], r[1], r[2], r[3], 0x400000, 0.35);
            let size = (26.0 * s).round();
            let line = tf!("Killed by {by}", by = self.file.killer);
            let lw = ui.measure(&line, size, true).min(r[2] - 20.0 * s);
            ui.rect(r[0] + (r[2] - lw) / 2.0 - 14.0 * s, r[1] + r[3] * 0.2 - 8.0 * s, lw + 28.0 * s, size * 1.3 + 16.0 * s, 0x000000, 0.6);
            ui.label_fit(&line, r[0] + (r[2] - lw) / 2.0, r[1] + r[3] * 0.2, lw, size, true, color::DANGER);
        }
    }

    /// Where a hit came from, in words.
    fn source(&self, data: &GameData, h: &Hurt) -> String {
        let object = || data.objects.get(&h.source_type).map(|d| if d.display_id.is_empty() { d.id.clone() } else { d.display_id.clone() }).unwrap_or_default();
        match h.from {
            from::SHOT => tf!("Shot by {name}", name = object()),
            from::HIT => tf!("Hit by {name}", name = object()),
            from::GROUND => {
                let ground = data.grounds.get(&h.source_type).map(|g| if g.display_id.is_empty() { g.id.clone() } else { g.display_id.clone() }).unwrap_or_default();
                tf!("Hurt by {ground}", ground)
            }
            from::AIR => tr("Ran out of air").to_owned(),
            _ => tr("Something unknown").to_owned(),
        }
    }

    fn draw_hits(&mut self, ui: &mut Ui, r: [f32; 4], s: f32, mouse: (f64, f64)) {
        theme::panel(ui.renderer, r, s);
        let pad = 12.0 * s;
        let hs = (15.0 * s).round();
        let total: i32 = self.hurts.iter().map(|(_, h)| h.dealt).sum();
        ui.label_fit(tr("Hits taken"), r[0] + pad, r[1] + pad, r[2] * 0.5, hs, true, color::TEXT);
        let count = tn!(self.hurts.len(), "{n} hit|{n} hits");
        let sum = tf!("{count}, {damage} damage", count, damage = total);
        let sw = ui.measure(&sum, 12.0 * s, false).min(r[2] * 0.5 - pad);
        ui.label_fit(&sum, r[0] + r[2] - pad - sw, r[1] + pad + 2.0 * s, sw, 12.0 * s, false, color::TEXT_DIM);
        let y0 = r[1] + pad + hs * 1.6;
        let row_h = (46.0 * s).round();
        let fit = (((r[1] + r[3] - pad - y0) / row_h).floor() as usize).max(1);
        if self.hurts.is_empty() {
            ui.label_fit(tr("No hits were recorded."), r[0] + pad, y0, r[2] - 2.0 * pad, 13.0 * s, false, color::TEXT_MUTED);
            return;
        }
        // the newest at the foot, scrolled up by the wheel
        let n = self.hurts.len();
        self.scroll = self.scroll.min(n.saturating_sub(fit));
        let last = n - self.scroll;
        let first = last.saturating_sub(fit);
        let data = ui.data;
        // the hit last landed, by the playhead
        let current = self.hurts.iter().rposition(|(at, _)| *at as f64 <= self.t);
        for (row, i) in (first..last).enumerate() {
            let (at, h) = &self.hurts[i];
            let y = y0 + row as f32 * row_h;
            let rr = [r[0] + pad * 0.5, y, r[2] - pad, row_h - 4.0 * s];
            let killing = i == n - 1 && h.hp <= 0;
            let future = (*at as f64) > self.t;
            let over = inside(rr, mouse);
            let bg = if Some(i) == current { color::CHIP_HI } else if over { color::SECONDARY } else { color::CHIP };
            // the killing blow framed in red, its inside as the others'
            let fill = theme::shade(bg, -0.35);
            if killing {
                theme::frame(ui.renderer, rr, 5.0 * s, (1.5 * s).max(1.0), (color::DANGER, 1.0), ((bg, 1.0), (fill, 1.0)));
            } else {
                theme::fill(ui.renderer, rr, 5.0 * s, bg, if future { 0.5 } else { 0.95 });
            }
            let alpha = if future { 0.5 } else { 1.0 };
            let x = rr[0] + 8.0 * s;
            let ls = 13.0 * s;
            let when = tf!("{s} s", s = format!("{:.1}", (*at - self.end) as f64 / 1000.0));
            ui.label_alpha(&when, x, rr[1] + 5.0 * s, 11.0 * s, false, color::TEXT_MUTED, alpha);
            let ww = 46.0 * s;
            // the damage, right
            let (num, num_color) = if h.flags & flag::DODGED != 0 {
                (tr("Missed").to_owned(), color::GOOD)
            } else if h.flags & flag::NO_HARM != 0 {
                ("0".to_owned(), color::TEXT_DIM)
            } else {
                (format!("-{}", h.dealt), if h.flags & flag::PIERCE != 0 { view::PIERCED_COLOR } else { color::DANGER })
            };
            let nw = ui.measure(&num, 15.0 * s, true);
            ui.label_alpha(&num, rr[0] + rr[2] - 8.0 * s - nw, rr[1] + 5.0 * s, 15.0 * s, true, num_color, alpha);
            let src = self.source(data, h);
            let tw = rr[2] - 16.0 * s - ww - nw - 14.0 * s;
            ui.label_fit(&src, x + ww, rr[1] + 4.0 * s, tw, ls, true, if killing { color::DANGER } else { color::TEXT });
            // the details under it
            let mut detail = vec![tf!("{hp} / {max} HP left", hp = h.hp.max(0), max = h.max_hp)];
            if killing {
                detail.insert(0, tr("killing blow").to_owned());
            }
            if h.from != from::AIR && h.raw != h.dealt && h.flags & (flag::DODGED | flag::NO_HARM) == 0 {
                detail.push(tf!("{raw} before defense", raw = h.raw));
            }
            if h.flags & flag::PIERCE != 0 {
                detail.push(tr("ignores defense").to_owned());
            }
            if h.flags & flag::STACKED != 0 {
                detail.push(tr("stacked on the hit before").to_owned());
            }
            if h.flags & flag::NO_HARM != 0 {
                detail.push(tr("invulnerable").to_owned());
            }
            ui.label_fit(&detail.join(" · "), x + ww, rr[1] + 4.0 * s + ls * 1.3, rr[2] - 16.0 * s - ww, 11.0 * s, false, color::TEXT_DIM);
            self.hits.push((Hit::Row(i), rr));
        }
    }

    fn draw_controls(&mut self, ui: &mut Ui, r: [f32; 4], s: f32, mouse: (f64, f64)) {
        let gap = 10.0 * s;
        let bh = (40.0 * s).round();
        let y = r[1] + (r[3] - bh) / 2.0;
        let mut x = r[0];
        let play = [x, y, 104.0 * s, bh];
        let label = if self.playing { tr("Pause") } else if self.t >= self.end as f64 { tr("Again") } else { tr("Resume") };
        ui.button(play, label, Kind::Primary, inside(play, mouse), s);
        self.hits.push((Hit::Play, play));
        x += play[2] + gap;
        let speed = [x, y, 64.0 * s, bh];
        let sl = match SPEEDS[self.speed] {
            v if v < 1.0 => format!("{v}×"),
            v => format!("{}×", v as i32),
        };
        ui.button(speed, &sl, Kind::Secondary, inside(speed, mouse), s);
        self.hits.push((Hit::Speed, speed));
        x += speed[2] + gap;
        let back = [r[0] + r[2] - 104.0 * s, y, 104.0 * s, bh];
        ui.button(back, tr("Back"), Kind::Secondary, inside(back, mouse), s);
        self.hits.push((Hit::Back, back));
        let download = [back[0] - gap - 120.0 * s, y, 120.0 * s, bh];
        ui.button(download, tr("Download"), Kind::Secondary, inside(download, mouse), s);
        self.hits.push((Hit::Download, download));
        let back = download;
        // the time, before the death
        let ts = 13.0 * s;
        let time = tf!("{s} s", s = format!("{:.1}", (self.t - self.end as f64) / 1000.0));
        let tw = 64.0 * s;
        let twm = ui.measure(&time, ts, true);
        ui.label(&time, back[0] - gap - twm, y + (bh - ts * 1.22) / 2.0, ts, true, color::TEXT);
        // the timeline
        let line = [x + 6.0 * s, y, back[0] - gap - tw - x - 12.0 * s, bh];
        self.timeline = line;
        self.hits.push((Hit::Timeline, line));
        let mid = line[1] + line[3] / 2.0;
        let th = 6.0 * s;
        theme::fill(ui.renderer, [line[0], mid - th / 2.0, line[2], th], th / 2.0, color::TRACK, 1.0);
        let k = (self.t / self.end as f64).clamp(0.0, 1.0) as f32;
        theme::fill(ui.renderer, [line[0], mid - th / 2.0, line[2] * k, th], th / 2.0, color::ACCENT, 1.0);
        // a mark for each hit, taller the more it took
        for (at, h) in &self.hurts {
            let hx = line[0] + line[2] * (*at as f32 / self.end as f32).clamp(0.0, 1.0);
            let part = if h.max_hp > 0 { (h.dealt as f32 / h.max_hp as f32).clamp(0.0, 1.0) } else { 0.0 };
            let mh = line[3] * (0.3 + 0.7 * (part * 3.0).min(1.0)) / 2.0;
            ui.rect((hx - s).round(), mid - mh, (2.0 * s).max(1.0), 2.0 * mh, if h.flags & flag::PIERCE != 0 { view::PIERCED_COLOR } else { color::DANGER }, 0.9);
        }
        // the death at the end, the playhead
        ui.rect(line[0] + line[2] - 2.0 * s, line[1], 3.0 * s, line[3], color::DANGER, 1.0);
        let px = line[0] + line[2] * k;
        let hot = self.scrubbing || inside(line, mouse);
        let rad = if hot { 8.0 * s } else { 6.5 * s };
        theme::fill(ui.renderer, [px - rad, mid - rad, 2.0 * rad, 2.0 * rad], rad, color::TEXT, 1.0);
        // the keys, under the timeline
        // under it, the keys, or what Download did
        let (note, c) = match &self.saved {
            Some((t, err)) => (t.as_str(), if *err { color::DANGER } else { color::GOOD }),
            None => (tr("Space: play or pause · Arrows: step and speed · Click a hit to see it"), color::TEXT_MUTED),
        };
        ui.label_fit(note, line[0], line[1] + line[3] + 1.0 * s, line[2], 10.5 * s, false, c);
    }
}

/// Saves the replay's file: into the Downloads folder (or the home folder), returning where.
#[cfg(not(target_arch = "wasm32"))]
fn save(file: &Replay) -> Result<String, String> {
    use std::path::PathBuf;
    let home = std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from);
    let dir = home.as_ref().map(|h| h.join("Downloads")).filter(|d| d.is_dir()).or(home).unwrap_or_else(|| PathBuf::from("."));
    let path = dir.join(file.file_name());
    std::fs::write(&path, &file.zipped).map_err(|e| e.to_string())?;
    log::info!("replay saved to {}", path.display());
    Ok(path.display().to_string())
}

/// In a browser: the file as a download, which the browser puts where it puts downloads.
#[cfg(target_arch = "wasm32")]
fn save(file: &Replay) -> Result<String, String> {
    use wasm_bindgen::JsCast;
    let err = |e: wasm_bindgen::JsValue| format!("{e:?}");
    let name = file.file_name();
    let bytes = js_sys::Uint8Array::from(&file.zipped[..]);
    let blob = web_sys::Blob::new_with_u8_array_sequence(&js_sys::Array::of1(&bytes)).map_err(err)?;
    let url = web_sys::Url::create_object_url_with_blob(&blob).map_err(err)?;
    let document = web_sys::window().and_then(|w| w.document()).ok_or("no page")?;
    let a: web_sys::HtmlAnchorElement = document.create_element("a").map_err(err)?.dyn_into().map_err(|_| "no link".to_owned())?;
    a.set_href(&url);
    a.set_download(&name);
    a.click();
    let _ = web_sys::Url::revoke_object_url(&url);
    Ok(name)
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// One of the recorded messages, as the Scrying Glass's picture takes them (`glass::Picture`),
/// with damage numbers and words over others.
fn apply(world: &mut World, particles: &mut crate::particles::Particles, floaters: &mut Vec<Floater>, msg: &ServerMessage, now: i32, data: &GameData, quiet: bool) {
    match msg {
        ServerMessage::Update(u) => world.apply_update(u, data, now),
        ServerMessage::NewTick(t) => world.apply_new_tick(t, now),
        ServerMessage::Layers(l) => world.set_layers(l.height as f64, &l.tiles),
        ServerMessage::Goto(g) => world.apply_goto(g.object_id, g.pos.x as f64, g.pos.y as f64, now, data),
        ServerMessage::EnemyShoot(e) => {
            let Some(owner) = world.objects.get(&e.owner_id).filter(|o| !o.dead) else { return };
            let container = owner.object_type;
            let (num, inc) = e.spread.map_or((1, 0.0), |s| (s.num_shots as i32, s.angle_inc as f64));
            for i in 0..num {
                let angle = e.angle as f64 + inc * i as f64;
                let (x, y) = (e.starting_pos.x as f64, e.starting_pos.y as f64);
                world.add_projectile(Projectile::new(e.owner_id, e.bullet_id + i, container, e.bullet_type as i32, angle, now, x, y, 1.0, e.damage, true));
            }
            if let Some(owner) = world.objects.get_mut(&e.owner_id) {
                owner.note_attack(now, e.angle as f64 + inc * (num - 1) as f64 / 2.0);
            }
        }
        ServerMessage::ServerPlayerShoot(s) => {
            if world.objects.get(&s.owner_id).is_some_and(|o| !o.dead) {
                let (x, y) = (s.starting_pos.x as f64, s.starting_pos.y as f64);
                let mut p = Projectile::new(s.owner_id, s.bullet_id, s.container_type as u16, s.bullet_type.unwrap_or(0) as i32, s.angle as f64, now, x, y, 1.0, s.damage, false);
                if data.objects.get(&(s.container_type as u16)).is_some_and(|d| d.xml.has("TurretHead")) {
                    p.z = crate::particles::turret::HEAD_Z;
                }
                world.add_projectile(p);
            }
        }
        ServerMessage::AllyShoot(a) => {
            let Some(owner) = world.objects.get_mut(&a.owner_id).filter(|o| !o.dead) else { return };
            owner.note_attack(now, a.angle as f64);
            let (x, y) = (owner.x, owner.y);
            world.add_projectile(Projectile::new(a.owner_id, a.bullet_id, a.container_type as u16, 0, a.angle as f64, now, x, y, 1.0, 0, false));
        }
        ServerMessage::ShowEffect(e) if !quiet => particles.show_effect(e, world, now),
        ServerMessage::Aoe(a) if !quiet => particles.aoe(a.pos.x as f64, a.pos.y as f64, a.radius as f64, a.color),
        ServerMessage::Damage(d) if !quiet => {
            particles.damaged(world, d.target_id, d.kill);
            if d.damage_amount > 0 {
                floaters.push(Floater { others: true, ..Floater::hit(d.target_id, d.damage_amount, d.pierce, now) });
            }
        }
        ServerMessage::Notification(n) if !quiet => {
            let text = if n.text == "Lucky!" { tr("Lucky!").to_owned() } else { crate::particles::cache::callout(&n.text).map_or_else(|| n.text.clone(), str::to_owned) };
            floaters.push(Floater { object_id: n.object_id, text, color: n.color & 0xFF_FFFF, at: now, life: 2000, others: false });
        }
        _ => {}
    }
}
