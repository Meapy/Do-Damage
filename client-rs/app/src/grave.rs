//! Lingering at the grave: after the death has been told (death.rs), a server that keeps us
//! (`GraveInfo`) lets us stay in the world as a ghost. The red of the death lifts into a cold,
//! moonlit sight (the world's `Spirit` filter, main.rs), we rise pale and see-through with soul
//! motes drifting up round us, and we can drift through walls, follow the players still in
//! sight, and chat, until Continue (or the ghost's time running out) fades into the death's
//! summary. The server keeps a ghost from doing anything else (Player.Grave.cs).

use crate::death;
use crate::game::{Grave, GRAVE_LEAVE_MS};
use crate::hud::{Layout, Ui};
use crate::i18n::{tf, tr};
use crate::theme::{self, color, Kind};

/// The ghost's colours: its sight's darkness at the edges, its motes, the banner's light.
const NIGHT: u32 = 0x0A0822;
const MOTE: u32 = 0xC6D6FF;
const SPECTRAL: u32 = 0xA9BEFF;
const SPECTRAL_DIM: u32 = 0x8C9AC8;

/// How long the death's red takes to lift as we rise, ms.
const LIFT_MS: i32 = 1100;
/// When the banner comes in after rising, and over how long, ms.
const BANNER_AT: i32 = 250;
const BANNER_MS: i32 = 600;
/// How many soul motes drift up round us.
const MOTES: u32 = 18;

/// What the banner's buttons do.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Act {
    /// Follow the player before (-1) or after (1) the one followed; 0 stops following, 2 starts
    /// with the nearest.
    Follow(i32),
    /// On to the death's summary.
    Continue,
}

/// The banner's buttons as last drawn, for the clicks.
#[derive(Default)]
pub struct GraveUi {
    pub buttons: Vec<(Act, [f32; 4])>,
}

impl GraveUi {
    /// What a click at `(x, y)` presses, if anything.
    pub fn click(&self, (x, y): (f64, f64)) -> Option<Act> {
        let (x, y) = (x as f32, y as f32);
        self.buttons.iter().find(|(_, r)| x >= r[0] && x < r[0] + r[2] && y >= r[1] && y < r[1] + r[3]).map(|(a, _)| *a)
    }

    pub fn on(&self, pos: (f64, f64)) -> bool {
        self.click(pos).is_some()
    }
}

fn ease(u: f32) -> f32 {
    1.0 - (1.0 - u.clamp(0.0, 1.0)).powi(3)
}

fn phase(t: i32, from: i32, span: i32) -> f32 {
    ((t - from) as f32 / span.max(1) as f32).clamp(0.0, 1.0)
}

/// A small hash of `i` and `salt`, 0 to 1, so each mote keeps its own path.
fn hash(i: u32, salt: u32) -> f32 {
    let mut h = i.wrapping_mul(0x9E37_79B9) ^ salt.wrapping_mul(0x85EB_CA6B);
    h ^= h >> 15;
    h = h.wrapping_mul(0x2C1B_3C6D);
    h ^= h >> 12;
    (h & 0xFFFF) as f32 / 65535.0
}

/// How opaque we're drawn: fading in as we rise from where we fell.
pub fn me_alpha(g: &Grave, now: i32) -> f32 {
    ease(phase(g.risen(now), 150, 900))
}

/// Over the world, under the HUD: the death's red lifting, the cold dark at the edges of the
/// view, and the soul motes rising round us at `me` (where we stand on the screen).
pub fn draw_world(ui: &mut Ui, g: &Grave, me: (f64, f64), lay: &Layout, now: i32) {
    let t = g.risen(now);
    let view = [0.0, 0.0, lay.column()[0], lay.h];
    let (w, h) = (view[2], view[3]);
    let (cx, cy) = (me.0 as f32, me.1 as f32);
    let short = w.min(h);
    let s = lay.s;
    ui.renderer.set_clip(Some(view));

    // The cold creeping in at the edges, breathing slowly.
    let come = ease(phase(t, 0, 1400));
    let breathe = 0.04 * (now as f32 / 1300.0).sin();
    death::vignette(ui.renderer, (cx, cy), (w, h), short * (0.30 + breathe), short * 0.78, NIGHT, (0.62 + breathe) * come);

    // The death's red and darkness, lifting.
    let lift = 1.0 - ease(phase(t, 0, LIFT_MS));
    if lift > 0.0 {
        death::vignette(ui.renderer, (cx, cy), (w, h), short * 0.2, short * 0.5, death::BLOOD, 0.88 * lift);
        ui.rect(0.0, 0.0, w, h, 0x000000, 0.3 * lift);
    }

    // As we rise: a pale ring spreading from where we fell.
    let ring = phase(t, 0, 900);
    if ring < 1.0 {
        let r = (18.0 + 90.0 * ease(ring)) * s;
        let a = 0.45 * (1.0 - ring);
        let seg = 40;
        let white = ui.renderer.white();
        let [cr, cg, cb] = bsk_render::rgb_f32(MOTE);
        let band = 3.0 * s * (1.0 - ring) + s;
        let ry = 0.5; // flattened onto the ground
        for i in 0..seg {
            let a0 = i as f32 / seg as f32 * std::f32::consts::TAU;
            let a1 = (i + 1) as f32 / seg as f32 * std::f32::consts::TAU;
            let p = |rad: f32, ang: f32| [cx + rad * ang.cos(), cy + rad * ang.sin() * ry];
            let (i0, i1, o0, o1) = (p(r - band, a0), p(r - band, a1), p(r + band, a0), p(r + band, a1));
            let clear = [cr, cg, cb, 0.0];
            let lit = [cr, cg, cb, a];
            ui.renderer.triangle_colors(white, [i0, i1, o1], [[0.5, 0.5]; 3], [clear, clear, lit]);
            ui.renderer.triangle_colors(white, [i0, o1, o0], [[0.5, 0.5]; 3], [clear, lit, lit]);
        }
    }

    // Soul motes drifting up round us, each on its own slow path.
    let motes = ease(phase(t, 200, 1200));
    if motes > 0.0 {
        for i in 0..MOTES {
            let period = 2400.0 + 1600.0 * hash(i, 1);
            let u = ((now as f32 + period * hash(i, 2)) % period) / period;
            let spread = (14.0 + 22.0 * hash(i, 3)) * s;
            let side = if hash(i, 4) < 0.5 { -1.0 } else { 1.0 };
            let x = cx + side * spread * (0.35 + 0.65 * hash(i, 5)) + (u * 7.0 + i as f32).sin() * 5.0 * s;
            let y = cy - 6.0 * s - u * (60.0 + 40.0 * hash(i, 6)) * s;
            let size = (1.2 + 2.2 * hash(i, 7)) * s;
            let a = (u * std::f32::consts::PI).sin() * 0.55 * motes;
            theme::fill(ui.renderer, [x - size * 2.0, y - size * 2.0, size * 4.0, size * 4.0], size * 2.0, MOTE, a * 0.25);
            theme::fill(ui.renderer, [x - size / 2.0, y - size / 2.0, size, size], size / 2.0, 0xFFFFFF, a);
        }
    }
    ui.renderer.set_clip(None);
}

/// A small arrow pointing left (`dir` -1) or right (1), centred in `r`.
fn arrow(ui: &mut Ui, r: [f32; 4], dir: f32, rgb: u32, s: f32) {
    let (cx, cy) = (r[0] + r[2] / 2.0, r[1] + r[3] / 2.0);
    let (hw, hh) = (4.0 * s, 6.0 * s);
    let white = ui.renderer.white();
    let [cr, cg, cb] = bsk_render::rgb_f32(rgb);
    let c = [cr, cg, cb, 1.0];
    let pts = [[cx + dir * hw, cy], [cx - dir * hw, cy - hh], [cx - dir * hw, cy + hh]];
    ui.renderer.triangle_colors(white, pts, [[0.5, 0.5]; 3], [c; 3]);
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && x < r[0] + r[2] && y >= r[1] && y < r[1] + r[3]
}

/// "m:ss".
fn clock(ms: i32) -> String {
    let secs = (ms.max(0) + 999) / 1000;
    format!("{}:{:02}", secs / 60, secs % 60)
}

/// The banner at the top of the view: how we died, that we linger, who we follow (and the
/// arrows to pick another), and Continue. `following` is the name of the player we follow, if
/// any; `others` how many players we could follow.
pub fn draw(ui: &mut Ui, out: &mut GraveUi, g: &Grave, following: Option<&str>, others: usize, lay: &Layout, mouse: (f64, f64), now: i32) {
    out.buttons.clear();
    let t = g.risen(now);
    let come = ease(phase(t, BANNER_AT, BANNER_MS));
    if come <= 0.0 || g.leaving_at.is_some() {
        return;
    }
    let s = lay.s;
    let view_w = lay.column()[0];
    let w = (470.0 * s).min(view_w - 24.0 * s);
    let h = 146.0 * s;
    let x = ((view_w - w) / 2.0).round();
    let y = (12.0 * s - (1.0 - come) * 30.0 * s).round();
    let r = [x, y, w, h];
    let radius = 10.0 * s;
    theme::shadow(ui.renderer, r, radius, 18.0 * s, 0.5 * come);
    theme::frame(ui.renderer, r, radius, s.max(1.0), (0x4A4F86, 0.9 * come), ((0x161633, 0.92 * come), (0x0E0C22, 0.95 * come)));
    // the faint light of the beyond along its top
    theme::fill(ui.renderer, [x + radius, y + s.max(1.0), w - 2.0 * radius, s.max(1.0)], 0.0, SPECTRAL, 0.3 * come);
    let glow = 0.5 + 0.5 * (now as f32 / 900.0).sin();
    theme::fill(ui.renderer, [x + w * 0.3, y + s.max(1.0), w * 0.4, s.max(1.0)], 0.0, 0xFFFFFF, (0.15 + 0.2 * glow) * come);

    let rise = |rgb: u32| theme::shade(rgb, -(1.0 - come));
    let mid = x + w / 2.0;

    // "YOU DIED", between two thin lines
    let head = tr("You died").to_uppercase();
    let size = ui.fit_size(&head, w - 120.0 * s, (24.0 * s).round(), true);
    let tw = ui.measure(&head, size, true);
    let ty = y + 12.0 * s;
    ui.label(&head, mid - tw / 2.0, ty, size, true, rise(color::DANGER));
    let line_y = ty + size * 0.62;
    let lw = 48.0 * s;
    theme::fill(ui.renderer, [mid - tw / 2.0 - 12.0 * s - lw, line_y, lw, s.max(1.0)], 0.0, color::DANGER, 0.55 * come);
    theme::fill(ui.renderer, [mid + tw / 2.0 + 12.0 * s, line_y, lw, s.max(1.0)], 0.0, color::DANGER, 0.55 * come);

    // who did it
    let killer = if g.killed_by.is_empty() { tr("something").to_owned() } else { g.killed_by.clone() };
    let by = tf!("Killed by {by}", by = killer);
    let size = ui.fit_size(&by, w - 32.0 * s, (14.0 * s).round(), true);
    let bw = ui.measure(&by, size, true);
    let by_y = ty + 30.0 * s;
    ui.label(&by, mid - bw / 2.0, by_y, size, true, rise(color::TEXT_DIM));

    // that we linger, and for how long
    let left = clock(g.until - now);
    let note = tf!("Your ghost lingers for {time}: drift, watch and chat.", time = left);
    let size = ui.fit_size(&note, w - 32.0 * s, (12.0 * s).round().max(9.0), false);
    let nw = ui.measure(&note, size, false);
    let note_y = by_y + 22.0 * s;
    ui.label(&note, mid - nw / 2.0, note_y, size, false, rise(SPECTRAL));

    // the row: follow (with its arrows) and Continue
    let row_y = y + h - 46.0 * s;
    let bh = 32.0 * s;
    let pad = 14.0 * s;
    let cont_w = (130.0 * s).min(w * 0.36);
    let cont = [x + w - pad - cont_w, row_y, cont_w, bh];
    let follow_x = x + pad;
    let follow_w = cont[0] - 10.0 * s - follow_x;
    if others == 0 {
        // no one to follow: say so, quietly
        let line = tr("No one else is in sight.");
        let size = ui.fit_size(line, follow_w, (13.0 * s).round(), false);
        ui.label(line, follow_x + 4.0 * s, row_y + (bh - size * 1.22) / 2.0, size, false, rise(SPECTRAL_DIM));
    } else {
        let aw = 28.0 * s;
        let prev = [follow_x, row_y, aw, bh];
        let next = [follow_x + follow_w - aw, row_y, aw, bh];
        let pill = [prev[0] + aw + 6.0 * s, row_y, follow_w - 2.0 * aw - 12.0 * s, bh];
        for (r, dir, act) in [(prev, -1.0, Act::Follow(-1)), (next, 1.0, Act::Follow(1))] {
            let hover = inside(r, mouse);
            theme::fill(ui.renderer, r, 6.0 * s, if hover { color::CHIP_HI } else { color::CHIP }, 0.95 * come);
            arrow(ui, r, dir, rise(if hover { 0xFFFFFF } else { SPECTRAL }), s);
            out.buttons.push((act, r));
        }
        let hover = inside(pill, mouse);
        let on = following.is_some();
        let (top, bottom) = if on { (0x2C3266, 0x1D2148) } else if hover { (0x2A2450, 0x1E1838) } else { (0x201A3A, 0x17122B) };
        theme::frame(ui.renderer, pill, 6.0 * s, s.max(1.0), (if on { SPECTRAL } else { color::BORDER_HI }, (if on { 0.75 } else { 0.9 }) * come), ((top, come), (bottom, come)));
        let label = match following {
            Some(name) => tf!("Following {name}", name),
            None => tr("Follow a player").to_owned(),
        };
        let size = ui.fit_size(&label, pill[2] - 16.0 * s, (13.0 * s).round(), true);
        let lw = ui.measure(&label, size, true);
        ui.label(&label, pill[0] + (pill[2] - lw) / 2.0, pill[1] + (bh - size * 1.22) / 2.0, size, true, rise(if on { 0xFFFFFF } else { color::TEXT }));
        out.buttons.push((if on { Act::Follow(0) } else { Act::Follow(2) }, pill));
    }
    let hover = inside(cont, mouse);
    ui.button(cont, tr("Continue"), Kind::Primary, hover, s);
    out.buttons.push((Act::Continue, cont));
}

/// Over everything: Continue (or the ghost's time running out) fading into the screens' night.
pub fn draw_leaving(ui: &mut Ui, g: &Grave, (w, h): (f32, f32), now: i32) {
    let Some(at) = g.leaving_at else { return };
    let u = phase(now - at, 0, GRAVE_LEAVE_MS);
    ui.rect(0.0, 0.0, w, h, color::BACKGROUND, u * u * (3.0 - 2.0 * u));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clock_rounds_up() {
        assert_eq!(clock(600_000), "10:00");
        assert_eq!(clock(59_001), "1:00");
        assert_eq!(clock(1), "0:01");
        assert_eq!(clock(-5), "0:00");
    }

    #[test]
    fn hash_stays_in_range() {
        for i in 0..200 {
            let v = hash(i, 3);
            assert!((0.0..=1.0).contains(&v));
        }
    }
}
