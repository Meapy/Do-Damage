//! The Crucible's climb on screen (`Crucible` messages, the server's `CrucibleWorld`): the floor
//! tracker at the top (five floors as diamonds joined by a line, the climb's clock, the
//! Threshold's and the rests' countdowns), and its moments, each animated over the world:
//!
//! - the ascent: a rush of light streaking down past us as we rise, the screen washed in the next
//!   floor's colour at its height (when the server moves us), its numeral in the middle;
//! - a floor's title card: letterbox bars slide in, the numeral and the floor's name with a line
//!   drawn out under it, its champion's portrait and name;
//! - a phase: a slash of the floor's colour sweeping across, the champion's name over the phase's;
//! - a floor cleared, and the victory: rays turning behind the words, sparks thrown out.
//!
//! The sky (`cruciblesky.rs`) takes the floor and the ascent from here (`sky_state`).

use crate::hud::{object_icon, Layout, Ui};
use crate::i18n::{mark, tf, tr};
use crate::theme::{self, color};
use bsk_proto::Crucible;
use std::f32::consts::TAU;

/// `Crucible.kind`.
pub mod kind {
    pub const LOBBY: u8 = 0;
    pub const ASCEND: u8 = 1;
    pub const FLOOR: u8 = 2;
    pub const CUE: u8 = 3;
    pub const CLEARED: u8 = 4;
    pub const REST: u8 = 5;
    pub const VICTORY: u8 = 6;
    pub const STATUS: u8 = 7;
}

pub const FLOORS: u8 = 5;
/// How long each moment shows (ms).
pub const ASCEND_MS: i32 = 2800;
const TITLE_MS: i32 = 5200;
const CUE_MS: i32 = 3000;
const CLEARED_MS: i32 = 4500;
const VICTORY_MS: i32 = 9000;

/// Each floor's colour (0 the Threshold): its banners, its diamond, its sky's embers.
pub const FLOOR_COLOR: [u32; 6] = [0xD8B06A, 0xFF9A2A, 0x46C2B4, 0xFF6A14, 0x4AF0FF, 0xFFD04A];

/// The floors' names, as the server's `CrucibleRules` has them.
const FLOOR_NAMES: [&str; 6] = [
    mark("The Threshold"),
    mark("The Iron Bastion"),
    mark("The Drowned Sanctum"),
    mark("The Ember Foundry"),
    mark("The Clockwork Orrery"),
    mark("The Apex"),
];

/// The champions' phases (`CrucibleCues` on the server): never renumbered.
const CUES: [&str; 24] = [
    "",
    mark("Hammerfall"),
    mark("Lockdown"),
    mark("Grinding Blades"),
    mark("Iron Tempest"),
    mark("Tidal Lanes"),
    mark("Rising Tide"),
    mark("Whirlpool"),
    mark("Maelstrom"),
    mark("Lava Veins"),
    mark("Meteor Storm"),
    mark("The Crucible Pours"),
    mark("Molten Core"),
    mark("Hour Hands"),
    mark("Midnight Chime"),
    mark("Time Fracture"),
    mark("Reversal"),
    mark("Sovereign's Decree"),
    mark("Echo of Iron"),
    mark("Echo of the Tide"),
    mark("Echo of the Forge"),
    mark("Echo of the Hours"),
    mark("The Crucible Breaks"),
    mark("Unbound"),
];

pub fn floor_name(floor: u8) -> &'static str {
    FLOOR_NAMES.get(floor as usize).map(|n| tr(n)).unwrap_or("")
}

/// A phase's name in the player's language, else the server's English.
pub fn cue_name(cue: u8, english: &str) -> String {
    match CUES.get(cue as usize) {
        Some(n) if !n.is_empty() => tr(n).to_owned(),
        _ => english.to_owned(),
    }
}

fn roman(n: u8) -> &'static str {
    ["", "I", "II", "III", "IV", "V"].get(n as usize).copied().unwrap_or("")
}

pub fn floor_color(floor: u8) -> u32 {
    FLOOR_COLOR[(floor as usize).min(FLOOR_COLOR.len() - 1)]
}

/// The climb as we know it, and its moments (each with when it began, our clock).
#[derive(Default)]
pub struct Climb {
    pub floor: u8,
    pub cleared: u8,
    /// When the climb began, on our clock.
    started: Option<i32>,
    /// The Threshold's or a rest's countdown: seconds left as of `at`, and which.
    countdown: Option<(i32, i32, u8)>,
    ascend: Option<(u8, u8, i32)>,
    title: Option<(u8, u16, i32)>,
    cue: Option<(u8, u8, u16, String, i32)>,
    cleared_banner: Option<(u8, i32, i32)>,
    victory: Option<(i32, i32)>,
    /// When the climb's clock stopped (the victory), its seconds.
    stopped: Option<i32>,
}

impl Climb {
    pub fn on_message(&mut self, m: &Crucible, now: i32) {
        match m.kind {
            kind::LOBBY => {
                self.floor = 0;
                self.countdown = (m.seconds > 0).then_some((m.seconds, now, kind::LOBBY));
            }
            kind::ASCEND => {
                self.countdown = None;
                if self.started.is_none() {
                    self.started = Some(now);
                }
                self.ascend = Some((self.floor, m.floor, now));
            }
            kind::FLOOR => {
                self.floor = m.floor;
                self.started = Some(now - m.seconds * 1000);
                self.title = Some((m.floor, m.champion, now));
            }
            kind::CUE => self.cue = Some((m.cue, m.floor, m.champion, m.text.clone(), now)),
            kind::CLEARED => {
                self.cleared = self.cleared.max(m.floor);
                self.cleared_banner = Some((m.floor, m.seconds, now));
                self.cue = None;
            }
            kind::REST => self.countdown = (m.seconds > 0).then_some((m.seconds, now, kind::REST)),
            kind::VICTORY => {
                self.cleared = FLOORS;
                self.victory = Some((m.seconds, now));
                self.stopped = Some(m.seconds);
                self.cleared_banner = None;
                self.countdown = None;
            }
            kind::STATUS => {
                self.floor = m.floor;
                self.cleared = m.floor.saturating_sub(1);
                if m.floor > 0 {
                    self.started = Some(now - m.seconds * 1000);
                }
            }
            _ => {}
        }
    }

    /// For the sky: the floor whose colours it wears, and how far through an ascent we are
    /// (from-floor, to-floor, 0 to 1), if one is running.
    pub fn sky(&self, now: i32) -> (u8, Option<(u8, u8, f32)>) {
        match self.ascend {
            Some((from, to, at)) if now - at < ASCEND_MS => (to, Some((from, to, (now - at) as f32 / ASCEND_MS as f32))),
            _ => (self.floor, None),
        }
    }

    fn clock(&self, now: i32) -> Option<i32> {
        self.stopped.or_else(|| self.started.map(|s| ((now - s) / 1000).max(0)))
    }

    /// Everything over the world: the tracker, then whichever moments are running.
    pub fn draw(&self, ui: &mut Ui, lay: &Layout, now: i32) {
        self.draw_tracker(ui, lay, now);
        if let Some((from, to, at)) = self.ascend {
            draw_ascend(ui, lay, from, to, now - at);
        }
        if let Some((floor, champion, at)) = self.title {
            draw_title(ui, lay, floor, champion, now - at);
        }
        if let Some((cue, floor, champion, english, at)) = &self.cue {
            draw_cue(ui, lay, *cue, *floor, *champion, english, now - at);
        }
        if let Some((floor, secs, at)) = self.cleared_banner {
            draw_cleared(ui, lay, floor, secs, now - at);
        }
        if let Some((secs, at)) = self.victory {
            draw_victory(ui, lay, secs, now - at);
        }
    }

    fn draw_tracker(&self, ui: &mut Ui, lay: &Layout, now: i32) {
        let s = lay.s;
        let cx = (lay.view_w() / 2.0).round();
        let w = 250.0 * s;
        let h = 58.0 * s;
        let r = [(cx - w / 2.0).round(), (8.0 * s).round(), w.round(), h.round()];
        theme::fill(ui.renderer, r, 8.0 * s, 0x0B0814, 0.7);
        let glow = floor_color(self.floor);
        theme::fill(ui.renderer, [r[0] + 20.0 * s, r[1], r[2] - 40.0 * s, 2.0 * s], 1.0 * s, glow, 0.9);
        // the kicker and the clock
        let ks = lay.font(10.0);
        let kick = tr("THE CRUCIBLE");
        ui.label(kick, r[0] + 10.0 * s, r[1] + 6.0 * s, ks, true, color::GOLD);
        if let Some(t) = self.clock(now) {
            let txt = format!("{}:{:02}", t / 60, t % 60);
            let tw = ui.measure(&txt, ks, true);
            ui.label(&txt, r[0] + r[2] - 10.0 * s - tw, r[1] + 6.0 * s, ks, true, color::TEXT);
        }
        // five diamonds joined by a line: cleared gold, the current one pulsing in its colour
        let y = r[1] + 27.0 * s;
        let x0 = r[0] + 30.0 * s;
        let gap = (r[2] - 60.0 * s) / (FLOORS - 1) as f32;
        ui.renderer.fill(x0, y - 1.0 * s, gap * (FLOORS - 1) as f32, 2.0 * s, 0x3A3050, 1.0);
        let done = gap * (self.cleared as f32 - 1.0).max(0.0);
        if self.cleared > 0 {
            ui.renderer.fill(x0, y - 1.0 * s, done, 2.0 * s, color::GOLD, 1.0);
        }
        let pulse = 0.5 + 0.5 * ((now as f32) / 300.0).sin();
        for f in 1..=FLOORS {
            let x = x0 + gap * (f - 1) as f32;
            let (fill, a, size) = if f <= self.cleared {
                (color::GOLD, 1.0, 6.0)
            } else if f == self.floor {
                (floor_color(f), 0.65 + 0.35 * pulse, 7.0 + 1.5 * pulse)
            } else {
                (0x4A4060, 1.0, 5.0)
            };
            if f == self.floor && f > self.cleared {
                diamond(ui, x, y, (size + 4.0) * s, floor_color(f), 0.25 * pulse);
            }
            diamond(ui, x, y, size * s, fill, a);
            let n = roman(f);
            let ns = lay.font(8.0);
            let nw = ui.measure(n, ns, true);
            ui.label_alpha(n, (x - nw / 2.0).round(), y + 8.0 * s, ns, true, if f <= self.cleared || f == self.floor { color::TEXT } else { color::TEXT_MUTED }, 1.0);
        }
        // the countdown, or the floor's name
        let ls = lay.font(9.5);
        let line = match self.countdown {
            Some((secs, at, k)) => {
                let left = (secs - (now - at) / 1000).max(0);
                if k == kind::LOBBY {
                    tf!("The climb begins in {seconds} s · say GO", seconds = left)
                } else {
                    tf!("Ascending in {seconds} s · say GO", seconds = left)
                }
            }
            None if self.floor == 0 => tr("Waiting for climbers").to_owned(),
            None => floor_name(self.floor).to_owned(),
        };
        let line = crate::npc::fit(ui, &line, ls, r[2] - 16.0 * s);
        let lw = ui.measure(&line, ls, false);
        ui.label(&line, (cx - lw / 2.0).round(), r[1] + r[3] - ls * 1.35, ls, false, color::TEXT_DIM);
    }
}

fn diamond(ui: &mut Ui, x: f32, y: f32, r: f32, rgb: u32, a: f32) {
    let white = ui.renderer.white();
    let tint = bsk_render::Tint::color(rgb, a.clamp(0.0, 1.0));
    ui.renderer.triangle(white, [[x, y - r], [x + r, y], [x, y + r]], [[0.5, 0.5]; 3], tint);
    ui.renderer.triangle(white, [[x, y - r], [x, y + r], [x - r, y]], [[0.5, 0.5]; 3], tint);
}

fn ease_out(t: f32) -> f32 {
    1.0 - (1.0 - t.clamp(0.0, 1.0)).powi(3)
}

/// In over `fade_in`, out over `fade_out` at the end of `life`.
fn fade(age: i32, life: i32, fade_in: i32, fade_out: i32) -> f32 {
    if age < 0 || age >= life {
        0.0
    } else if age < fade_in {
        age as f32 / fade_in as f32
    } else if age > life - fade_out {
        (life - age) as f32 / fade_out as f32
    } else {
        1.0
    }
}

fn centred(ui: &mut Ui, text: &str, cx: f32, y: f32, size: f32, bold: bool, rgb: u32, a: f32, max_w: f32) {
    let text = crate::npc::fit(ui, text, size, max_w);
    let w = ui.measure(&text, size, bold);
    // a dark copy under it, so it reads over anything
    ui.label_alpha(&text, (cx - w / 2.0).round() + 2.0, y.round() + 2.0, size, bold, 0x000000, a * 0.6);
    ui.label_alpha(&text, (cx - w / 2.0).round(), y.round(), size, bold, rgb, a);
}

/// Rays turning round (cx, cy), out to `reach`, in `rgb`.
fn rays(ui: &mut Ui, cx: f32, cy: f32, reach: f32, rgb: u32, a: f32, turn: f32, count: usize) {
    let white = ui.renderer.white();
    let [r, g, b] = bsk_render::rgb_f32(rgb);
    for i in 0..count {
        let a0 = turn + i as f32 / count as f32 * TAU;
        let w = TAU / count as f32 * 0.32;
        let p = |ang: f32| [cx + reach * ang.cos(), cy + reach * ang.sin()];
        ui.renderer.triangle_colors(white, [[cx, cy], p(a0 - w), p(a0 + w)], [[0.5, 0.5]; 3], [[r, g, b, a], [r, g, b, 0.0], [r, g, b, 0.0]]);
    }
}

/// The ascent: the screen washes over in the next floor's colour as streaks of light rush down
/// past us; at its height (halfway) the server moves us up, hidden under it.
fn draw_ascend(ui: &mut Ui, lay: &Layout, from: u8, to: u8, age: i32) {
    if !(0..ASCEND_MS).contains(&age) {
        return;
    }
    let t = age as f32 / ASCEND_MS as f32;
    // up to nearly opaque at the middle, then clearing
    let wash = (1.0 - (2.0 * t - 1.0).abs()).powf(0.7);
    let (w, h) = (lay.view_w(), lay.h);
    let c0 = floor_color(from);
    let c1 = floor_color(to);
    let tint = if t < 0.5 { c0 } else { c1 };
    ui.renderer.fill(0.0, 0.0, w, h, 0x08060E, wash * 0.85);
    ui.renderer.fill(0.0, 0.0, w, h, tint, wash * 0.18);
    // streaks rushing downwards, faster as it peaks
    let white = ui.renderer.white();
    let [r, g, b] = bsk_render::rgb_f32(tint);
    let mut seed = 0x9E37_79B9u32;
    let mut rnd = || {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        (seed % 10_000) as f32 / 10_000.0
    };
    let speed = 0.4 + 2.6 * wash;
    for _ in 0..70 {
        let x = rnd() * w;
        let len = (40.0 + rnd() * 160.0) * lay.s * (0.4 + wash);
        let phase = rnd();
        let y = ((phase + t * speed * (0.6 + rnd())) % 1.0) * (h + len) - len;
        let thick = (1.0 + rnd() * 2.5) * lay.s;
        let a = (0.25 + 0.6 * rnd()) * wash;
        ui.renderer.triangle_colors(white, [[x, y], [x + thick, y], [x + thick, y + len]], [[0.5, 0.5]; 3], [[r, g, b, 0.0], [r, g, b, 0.0], [r, g, b, a]]);
        ui.renderer.triangle_colors(white, [[x, y], [x + thick, y + len], [x, y + len]], [[0.5, 0.5]; 3], [[r, g, b, 0.0], [r, g, b, a], [r, g, b, a]]);
    }
    // the floor coming, in the middle
    let a = fade(age, ASCEND_MS, 500, 700) * wash.max(0.3);
    let cx = w / 2.0;
    let ks = lay.font(14.0);
    let big = lay.font(44.0);
    let cy = h * 0.42;
    centred(ui, tr("ASCENDING"), cx, cy - ks * 2.2, ks, true, color::TEXT_DIM, a, w * 0.9);
    centred(ui, roman(to), cx, cy, big, true, c1, a, w * 0.9);
}

/// A floor's title card: letterbox bars, its numeral, its name with a line drawn out under it,
/// and its champion.
fn draw_title(ui: &mut Ui, lay: &Layout, floor: u8, champion: u16, age: i32) {
    if !(0..TITLE_MS).contains(&age) {
        return;
    }
    let (w, h, s) = (lay.view_w(), lay.h, lay.s);
    let bars = ease_out(age as f32 / 500.0) * fade(age, TITLE_MS, 1, 700);
    let bar_h = h * 0.11 * bars;
    ui.renderer.fill(0.0, 0.0, w, bar_h, 0x000000, 0.92);
    ui.renderer.fill(0.0, h - bar_h, w, bar_h, 0x000000, 0.92);
    let glow = floor_color(floor);
    ui.renderer.fill(0.0, bar_h - 2.0 * s, w, 2.0 * s, glow, 0.7 * bars);
    ui.renderer.fill(0.0, h - bar_h, w, 2.0 * s, glow, 0.7 * bars);

    let a = fade(age - 200, TITLE_MS - 200, 400, 800);
    let cx = w / 2.0;
    let top = h * 0.2;
    let ks = lay.font(15.0);
    let ts = lay.font(34.0);
    centred(ui, &tf!("FLOOR {floor}", floor = roman(floor)), cx, top, ks, true, glow, a, w * 0.9);
    let name = floor_name(floor);
    centred(ui, name, cx, top + ks * 1.6, ts, true, color::TEXT, a, w * 0.92);
    // the line drawn out from the middle
    let line = ease_out((age - 350) as f32 / 700.0) * (w * 0.32).min(380.0 * s);
    let ly = top + ks * 1.6 + ts * 1.3;
    ui.renderer.fill(cx - line, ly, line * 2.0, 2.0 * s, glow, a);
    diamond(ui, cx - line, ly + 1.0 * s, 3.5 * s, glow, a);
    diamond(ui, cx + line, ly + 1.0 * s, 3.5 * s, glow, a);
    // the champion: portrait and name, a beat later
    let ca = fade(age - 900, TITLE_MS - 900, 500, 800);
    if ca > 0.0 && champion != 0 {
        let portrait = 52.0 * s;
        let py = ly + 14.0 * s + portrait / 2.0;
        theme::fill(ui.renderer, [cx - portrait * 0.7, py - portrait * 0.7, portrait * 1.4, portrait * 1.4], portrait * 0.7, 0x0B0814, 0.6 * ca);
        object_icon(ui, champion, cx, py, portrait);
        let champ = ui.data.objects.get(&champion).map(|d| d.display_id.clone()).unwrap_or_default();
        centred(ui, &champ, cx, py + portrait * 0.75, lay.font(16.0), true, color::GOLD, ca, w * 0.9);
    }
}

/// A phase begins: a slash of the floor's colour sweeps across, the champion's name above the
/// phase's.
fn draw_cue(ui: &mut Ui, lay: &Layout, cue: u8, floor: u8, champion: u16, english: &str, age: i32) {
    if !(0..CUE_MS).contains(&age) {
        return;
    }
    let (w, h, s) = (lay.view_w(), lay.h, lay.s);
    let glow = floor_color(floor);
    let a = fade(age, CUE_MS, 1, 700);
    let sweep = ease_out(age as f32 / 450.0);
    let cy = h * 0.3;
    let band = 54.0 * s;
    let x1 = w * sweep;
    let slant = 30.0 * s;
    let white = ui.renderer.white();
    let [r, g, b] = bsk_render::rgb_f32(0x0B0814);
    let [gr, gg, gb] = bsk_render::rgb_f32(glow);
    // the dark band, slanted at its leading edge
    let top = cy - band / 2.0;
    let bot = cy + band / 2.0;
    let dark = [r, g, b, 0.78 * a];
    // the slant stops at the view's edge, short of the side panel
    ui.renderer.triangle_colors(white, [[0.0, top], [(x1 + slant).min(w), top], [x1, bot]], [[0.5, 0.5]; 3], [dark; 3]);
    ui.renderer.triangle_colors(white, [[0.0, top], [x1, bot], [0.0, bot]], [[0.5, 0.5]; 3], [dark; 3]);
    // its edges in the floor's colour, brightest at the leading edge
    for (y0, y1) in [(top - 2.0 * s, top), (bot, bot + 2.0 * s)] {
        ui.renderer.triangle_colors(white, [[0.0, y0], [x1, y0], [x1, y1]], [[0.5, 0.5]; 3], [[gr, gg, gb, 0.0], [gr, gg, gb, a], [gr, gg, gb, a]]);
        ui.renderer.triangle_colors(white, [[0.0, y0], [x1, y1], [0.0, y1]], [[0.5, 0.5]; 3], [[gr, gg, gb, 0.0], [gr, gg, gb, a], [gr, gg, gb, 0.0]]);
    }
    let ta = fade(age - 250, CUE_MS - 250, 300, 700);
    let cx = w / 2.0;
    let champ = ui.data.objects.get(&champion).map(|d| d.display_id.clone()).unwrap_or_default();
    let ks = lay.font(11.0);
    let ts = lay.font(24.0);
    centred(ui, &champ.to_uppercase(), cx, top + 5.0 * s, ks, true, glow, ta, w * 0.9);
    centred(ui, &cue_name(cue, english).to_uppercase(), cx, top + 5.0 * s + ks * 1.4, ts, true, color::TEXT, ta, w * 0.9);
}

/// A floor cleared: rays behind the words, its time.
fn draw_cleared(ui: &mut Ui, lay: &Layout, floor: u8, secs: i32, age: i32) {
    if !(0..CLEARED_MS).contains(&age) {
        return;
    }
    let (w, h, s) = (lay.view_w(), lay.h, lay.s);
    let a = fade(age, CLEARED_MS, 300, 900);
    let cx = w / 2.0;
    let cy = h * 0.3;
    rays(ui, cx, cy, 170.0 * s * ease_out(age as f32 / 600.0), color::GOLD, 0.22 * a, age as f32 / 2400.0, 14);
    let ts = lay.font(30.0);
    let ss = lay.font(13.0);
    centred(ui, tr("FLOOR CLEARED"), cx, cy - ts * 0.7, ts, true, color::GOLD, a, w * 0.9);
    let line = tf!("{floor} in {time}", floor = floor_name(floor), time = format!("{}:{:02}", secs / 60, secs % 60));
    centred(ui, &line, cx, cy + ts * 0.65, ss, false, color::TEXT, a, w * 0.9);
}

/// The Sovereign has fallen: the whole tower's colours in turning rays, sparks thrown out, the
/// climb's time.
fn draw_victory(ui: &mut Ui, lay: &Layout, secs: i32, age: i32) {
    if !(0..VICTORY_MS).contains(&age) {
        return;
    }
    let (w, h, s) = (lay.view_w(), lay.h, lay.s);
    let a = fade(age, VICTORY_MS, 400, 1500);
    let cx = w / 2.0;
    let cy = h * 0.32;
    // a white flash as it begins
    let flash = (1.0 - age as f32 / 600.0).max(0.0);
    if flash > 0.0 {
        ui.renderer.fill(0.0, 0.0, w, h, 0xFFFFFF, flash * 0.55);
    }
    let grow = ease_out(age as f32 / 900.0);
    for (i, c) in FLOOR_COLOR[1..].iter().enumerate() {
        rays(ui, cx, cy, (220.0 + 30.0 * i as f32) * s * grow, *c, 0.12 * a, age as f32 / (3000.0 + 700.0 * i as f32) * if i % 2 == 0 { 1.0 } else { -1.0 }, 10);
    }
    // sparks flying out in the floors' colours
    let mut seed = 0x1234_5678u32;
    let mut rnd = || {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        (seed % 10_000) as f32 / 10_000.0
    };
    let t = age as f32 / 1000.0;
    for i in 0..60 {
        let ang = rnd() * TAU;
        let speed = (60.0 + rnd() * 220.0) * s;
        let delay = rnd() * 1.5;
        let life = 1.6 + rnd() * 1.4;
        let k = ((t - delay) / life).clamp(0.0, 1.0);
        if k <= 0.0 || k >= 1.0 {
            continue;
        }
        let d = speed * k * (2.0 - k);
        let (x, y) = (cx + ang.cos() * d, cy + ang.sin() * d + 40.0 * s * k * k);
        let size = (2.0 + 2.0 * rnd()) * s * (1.0 - k * 0.5);
        ui.renderer.fill(x - size / 2.0, y - size / 2.0, size, size, FLOOR_COLOR[1 + i % 5], (1.0 - k) * a);
    }
    let ts = lay.font(36.0);
    let ss = lay.font(15.0);
    centred(ui, tr("THE CRUCIBLE IS CONQUERED"), cx, cy - ts * 0.8, ts, true, color::GOLD, a, w * 0.94);
    centred(ui, &tf!("Climbed in {time}", time = format!("{}:{:02}", secs / 60, secs % 60)), cx, cy + ts * 0.6, ss, true, color::TEXT, a, w * 0.9);
}
