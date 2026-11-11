//! The character's death as it plays out in the world, before the fame summary: a red flash, the
//! world greying (main.rs) and darkening round where we fell, the death told in big letters with
//! who did it, then a fade into the screens' night.

use crate::game::{Dying, DYING_FADE_MS, DYING_MS, DYING_SKIP_MS};
use crate::hud::Ui;
use crate::i18n::{tf, tr};
use crate::theme::{self, color};
use bsk_render::Renderer;

/// The vignette's red, deep and dark.
const BLOOD: u32 = 0x2A0008;

/// Eased out: quick at first, settling.
fn ease(u: f32) -> f32 {
    1.0 - (1.0 - u.clamp(0.0, 1.0)).powi(3)
}

/// How far `t` is through `from`..`from + span`, 0 to 1.
fn phase(t: i32, from: i32, span: i32) -> f32 {
    ((t - from) as f32 / span.max(1) as f32).clamp(0.0, 1.0)
}

/// `rgb` faded in by `a` over a dark ground (text can't be see-through, so it rises from black).
fn rise(rgb: u32, a: f32) -> u32 {
    theme::shade(rgb, -(1.0 - a.clamp(0.0, 1.0)))
}

/// A darkness closing in on `(cx, cy)`: clear within `inner`, `alpha` of `rgb` from `outer` out
/// to past the window's corners.
pub fn vignette(renderer: &mut Renderer, (cx, cy): (f32, f32), (w, h): (f32, f32), inner: f32, outer: f32, rgb: u32, alpha: f32) {
    if alpha <= 0.0 {
        return;
    }
    let far = [(0.0, 0.0), (w, 0.0), (0.0, h), (w, h)].iter().map(|&(x, y): &(f32, f32)| (x - cx).hypot(y - cy)).fold(0.0, f32::max) + 4.0;
    let [r, g, b] = bsk_render::rgb_f32(rgb);
    let clear = [r, g, b, 0.0];
    let dark = [r, g, b, alpha];
    let white = renderer.white();
    const STEPS: usize = 48;
    let at = |radius: f32, i: usize| {
        let a = i as f32 / STEPS as f32 * std::f32::consts::TAU;
        [cx + radius * a.cos(), cy + radius * a.sin()]
    };
    for i in 0..STEPS {
        let (i0, i1) = (at(inner, i), at(inner, i + 1));
        let (m0, m1) = (at(outer, i), at(outer, i + 1));
        let (o0, o1) = (at(far.max(outer), i), at(far.max(outer), i + 1));
        // the soft edge, then solid out to the corners
        renderer.triangle_colors(white, [i0, i1, m1], [[0.5, 0.5]; 3], [clear, clear, dark]);
        renderer.triangle_colors(white, [i0, m1, m0], [[0.5, 0.5]; 3], [clear, dark, dark]);
        renderer.triangle_colors(white, [m0, m1, o1], [[0.5, 0.5]; 3], [dark; 3]);
        renderer.triangle_colors(white, [m0, o1, o0], [[0.5, 0.5]; 3], [dark; 3]);
    }
}

/// How the camera shakes as we die, in tiles: a jolt that settles in under half a second.
pub fn shake(d: &Dying, now: i32) -> (f64, f64) {
    let t = d.elapsed(now);
    if !(0..450).contains(&t) {
        return (0.0, 0.0);
    }
    let a = 0.14 * (1.0 - t as f64 / 450.0).powi(2);
    (a * (t as f64 * 0.11).sin(), a * (t as f64 * 0.17).cos())
}

/// Draw the death over everything: `me` is where we stand on the screen.
pub fn draw(ui: &mut Ui, d: &Dying, me: (f64, f64), (w, h): (f32, f32), now: i32) {
    let t = d.elapsed(now);
    let s = crate::screens::scale(w, h);
    let (cx, cy) = (me.0 as f32, me.1 as f32);
    let short = w.min(h);

    // The world darkens and reddens, closing in on where we fell.
    let close = ease(phase(t, 0, 1600));
    vignette(ui.renderer, (cx, cy), (w, h), short * (0.42 - 0.22 * close), short * (0.75 - 0.25 * close), BLOOD, 0.88 * close);
    ui.rect(0.0, 0.0, w, h, 0x000000, 0.3 * close);
    // the moment it hits: a red flash
    let flash = 1.0 - phase(t, 0, 380);
    if flash > 0.0 {
        ui.rect(0.0, 0.0, w, h, color::DANGER, 0.32 * flash * flash);
    }

    // "YOU DIED", settling into place as it brightens, a line drawn out under it.
    let mid = w / 2.0;
    let top = h * 0.36;
    let title = phase(t, 320, 650);
    if title > 0.0 {
        let e = ease(title);
        let head = tr("You died").to_uppercase();
        let base = (64.0 * s).round();
        let size = ui.fit_size(&head, w - 48.0 * s, (base * (1.0 + 0.3 * (1.0 - e))).round(), true);
        let tw = ui.measure(&head, size, true);
        ui.label(&head, mid - tw / 2.0, top + (base - size) * 0.5, size, true, rise(color::DANGER, e));
        let lw = (380.0 * s).min(w - 48.0 * s) * ease(phase(t, 520, 700));
        let ly = top + base * 1.35;
        theme::fill(ui.renderer, [mid - lw / 2.0, ly, lw, (2.0 * s).max(1.0)], s, color::DANGER, 0.75);
        // and its bright middle
        theme::fill(ui.renderer, [mid - lw / 6.0, ly, lw / 3.0, (2.0 * s).max(1.0)], s, 0xFFB0B8, 0.6);
    }

    // Who did it.
    let by = phase(t, 900, 500);
    if by > 0.0 {
        let killer = if d.killed_by.is_empty() { tr("something").to_owned() } else { d.killed_by.clone() };
        let line = tf!("Killed by {by}", by = killer);
        let size = ui.fit_size(&line, w - 48.0 * s, (22.0 * s).round(), true);
        let lw = ui.measure(&line, size, true);
        let y = top + 64.0 * s * 1.35 + 18.0 * s + 6.0 * s * (1.0 - ease(by));
        ui.label(&line, mid - lw / 2.0, y, size, true, rise(color::TEXT, ease(by) * 0.92));
    }

    // That it can be skipped, once it can.
    let hint = phase(t, DYING_SKIP_MS + 500, 400);
    if hint > 0.0 && d.skipped_at.is_none() {
        let line = tr("Click to skip");
        let size = (13.0 * s).round();
        let lw = ui.measure(line, size, true);
        let pulse = 0.85 + 0.15 * (now as f32 / 420.0).sin();
        let y = top + 64.0 * s * 1.35 + 62.0 * s;
        // on a dark pill, to read over light ground
        let pill = [mid - lw / 2.0 - 14.0 * s, y - 5.0 * s, lw + 28.0 * s, size * 1.25 + 10.0 * s];
        theme::fill(ui.renderer, pill, pill[3] / 2.0, 0x000000, 0.45 * hint);
        ui.label(line, mid - lw / 2.0, y, size, true, rise(color::TEXT_DIM, hint * pulse));
    }

    // At the end, into the night of the screens behind.
    let out = phase(t, DYING_MS - DYING_FADE_MS, DYING_FADE_MS);
    if out > 0.0 {
        ui.rect(0.0, 0.0, w, h, color::BACKGROUND, out * out * (3.0 - 2.0 * out));
    }
}
