//! A Monk's Chi (the server's `Player.Flow.cs`): every third punch that lands builds one, up to
//! five, and the Flowing Strike spends them all. The server sends anyone's count in the `CHI`
//! stat; this draws it as orbs of golden light circling the Monk at chest height, passing behind
//! them and in front. A new one bursts in with a flash; all five, they spin faster, joined by a
//! ring of light, and pulse: the Monk is ready to Flow. Spent, the last of them flares outward
//! (the strike's own look is `particles/flow.rs`).

use bsk_render::{Renderer, Tint};
use std::collections::HashMap;
use std::f32::consts::TAU;

/// The most Chi (the server's `Player.ChiMax`).
pub const MAX: i32 = 5;
/// How long after a strike its spent orbs are still drawn, ms.
pub const SPENT_MS: i32 = 380;
const GOLD: u32 = 0xFFD86A;
const WHITE_GOLD: u32 = 0xFFF6D8;
/// How long a new orb's flash and a spent ring's flare last, ms.
const FLASH_MS: i32 = 320;

/// Each Monk's Chi as we last saw it: the count, when it last rose, and when it was last spent
/// (with how many).
#[derive(Default)]
pub struct ChiMemory {
    by: HashMap<i32, Seen>,
}

#[derive(Clone, Copy, Debug, Default)]
struct Seen {
    count: i32,
    rose: i32,
    spent: i32,
    spent_count: i32,
    seen: i32,
}

impl ChiMemory {
    /// Player `id` has `count` Chi now.
    pub fn see(&mut self, id: i32, count: i32, now: i32) {
        let count = count.clamp(0, MAX);
        let s = self.by.entry(id).or_insert(Seen { count, rose: -10_000, spent: -10_000, ..Default::default() });
        if count > s.count {
            s.rose = now;
        } else if count == 0 && s.count >= 2 {
            // all of it at once: the strike spent it (one fading leaves one at a time)
            s.spent = now;
            s.spent_count = s.count;
        }
        s.count = count;
        s.seen = now;
    }

    /// Forgets those not seen for a while.
    pub fn prune(&mut self, now: i32) {
        self.by.retain(|_, s| now - s.seen < 5000);
    }

    /// (since it last rose, since it was last spent and how many) for player `id`.
    pub fn look(&self, id: i32, now: i32) -> (i32, i32, i32) {
        self.by.get(&id).map_or((10_000, 10_000, 0), |s| (now - s.rose, now - s.spent, s.spent_count))
    }
}

fn tri(r: &mut Renderer, pts: [[f32; 2]; 3], rgb: u32, a: f32) {
    if a > 0.005 {
        let white = r.white();
        r.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, a.min(1.0)));
    }
}

/// A soft glow, brightest in its middle.
fn glow(r: &mut Renderer, x: f32, y: f32, rad: f32, rgb: u32, a: f32) {
    if a <= 0.005 {
        return;
    }
    let white = r.white();
    let mid = Tint::color(rgb, a.min(1.0)).mul;
    let edge = Tint::color(rgb, 0.0).mul;
    const N: usize = 14;
    for i in 0..N {
        let (a0, a1) = (i as f32 * TAU / N as f32, (i + 1) as f32 * TAU / N as f32);
        r.triangle_colors(white, [[x, y], [x + rad * a0.cos(), y + rad * a0.sin()], [x + rad * a1.cos(), y + rad * a1.sin()]], [[0.5, 0.5]; 3], [mid, edge, edge]);
    }
}

fn disc(r: &mut Renderer, x: f32, y: f32, rad: f32, rgb: u32, a: f32) {
    const N: usize = 10;
    for i in 0..N {
        let (a0, a1) = (i as f32 * TAU / N as f32, (i + 1) as f32 * TAU / N as f32);
        tri(r, [[x, y], [x + rad * a0.cos(), y + rad * a0.sin()], [x + rad * a1.cos(), y + rad * a1.sin()]], rgb, a);
    }
}

/// The orbs round a Monk standing at (cx, cy) on the screen (their feet), `tile` pixels a tile:
/// `count` of them, and the last spent flaring out. `look` is `ChiMemory::look`'s.
#[allow(clippy::too_many_arguments)]
pub fn orbs(r: &mut Renderer, cx: f32, cy: f32, tile: f32, count: i32, look: (i32, i32, i32), now: i32, alpha: f32) {
    let (since_rose, since_spent, spent) = look;
    let ms = now as f32;
    // the last spent: a ring of them flaring out and fading
    if since_spent < SPENT_MS && spent > 0 {
        let k = since_spent as f32 / SPENT_MS as f32;
        let rad = (0.55 + 0.9 * k) * tile;
        for i in 0..spent {
            let a = i as f32 * TAU / spent as f32 + ms * 0.004;
            let (x, y) = (cx + rad * a.cos(), cy - 0.45 * tile + 0.4 * rad * a.sin());
            glow(r, x, y, 0.22 * tile * (1.0 - k), GOLD, 0.7 * (1.0 - k) * alpha);
            disc(r, x, y, 0.05 * tile * (1.0 - k), 0xFFFFFF, (1.0 - k) * alpha);
        }
    }
    let count = count.clamp(0, MAX);
    if count == 0 {
        return;
    }
    let full = count == MAX;
    let spin = ms * if full { 0.0042 } else { 0.0022 };
    let pulse = if full { 0.5 + 0.5 * (ms * 0.008).sin() } else { 0.0 };
    let (rx, ry) = (0.58 * tile, 0.24 * tile);
    let mid = cy - 0.42 * tile;
    // all five: a ring of light joining them, and a glow at the Monk's heart
    if full {
        let n = 36;
        for i in 0..n {
            let (a0, a1) = (i as f32 * TAU / n as f32, (i + 1) as f32 * TAU / n as f32);
            let p = |a: f32, k: f32| [cx + rx * k * a.cos(), mid + ry * k * a.sin()];
            let al = (0.16 + 0.12 * pulse) * alpha;
            tri(r, [p(a0, 0.93), p(a0, 1.07), p(a1, 1.07)], GOLD, al);
            tri(r, [p(a0, 0.93), p(a1, 1.07), p(a1, 0.93)], GOLD, al);
        }
        glow(r, cx, mid, 0.5 * tile, GOLD, (0.12 + 0.1 * pulse) * alpha);
    }
    for i in 0..count {
        let a = spin + i as f32 * TAU / MAX as f32;
        let depth = a.sin();
        let bob = 0.03 * tile * (ms * 0.005 + i as f32 * 1.3).sin();
        let (x, y) = (cx + rx * a.cos(), mid + ry * depth + bob);
        let near = 0.6 + 0.4 * (depth * 0.5 + 0.5);
        // the newest bursting in
        let fresh = if i == count - 1 && since_rose < FLASH_MS { 1.0 - since_rose as f32 / FLASH_MS as f32 } else { 0.0 };
        let size = (0.085 + 0.02 * pulse + 0.12 * fresh) * tile;
        // a tail of light behind it
        for k in 1..4 {
            let b = a - k as f32 * 0.17;
            let (tx, ty) = (cx + rx * b.cos(), mid + ry * b.sin() + bob);
            disc(r, tx, ty, size * (0.6 - 0.12 * k as f32), GOLD, 0.35 * near * alpha / k as f32);
        }
        glow(r, x, y, size * 3.2, GOLD, (0.4 + 0.25 * pulse + 0.5 * fresh) * near * alpha);
        disc(r, x, y, size, GOLD, near * alpha);
        disc(r, x - size * 0.25, y - size * 0.25, size * 0.5, WHITE_GOLD, near * alpha);
        if fresh > 0.0 {
            // a cross of light where it appeared
            let len = 0.3 * tile * fresh;
            tri(r, [[x - len, y], [x, y - 0.03 * tile], [x + len, y]], 0xFFFFFF, fresh * alpha);
            tri(r, [[x - len, y], [x, y + 0.03 * tile], [x + len, y]], 0xFFFFFF, fresh * alpha);
            tri(r, [[x, y - len], [x - 0.03 * tile, y], [x, y + len]], 0xFFFFFF, fresh * alpha);
            tri(r, [[x, y - len], [x + 0.03 * tile, y], [x, y + len]], 0xFFFFFF, fresh * alpha);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn remembers_rising_and_spending() {
        let mut m = ChiMemory::default();
        m.see(7, 0, 0);
        m.see(7, 1, 100);
        assert_eq!(m.look(7, 150).0, 50, "it rose");
        m.see(7, 5, 200);
        m.see(7, 0, 300);
        let (_, spent, n) = m.look(7, 350);
        assert_eq!((spent, n), (50, 5), "all five spent at once");
        // one fading at a time isn't a strike
        m.see(8, 1, 0);
        m.see(8, 0, 100);
        assert_eq!(m.look(8, 150).2, 0);
        m.prune(10_000);
        assert_eq!(m.look(7, 10_000), (10_000, 10_000, 0));
    }
}
