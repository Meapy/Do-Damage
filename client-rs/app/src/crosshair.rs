//! The crosshair over the world: its styles, colours and size (Options, Crosshair), and the
//! player's own, painted on a grid there.
//!
//! Every style is a set of whole pixels round the mouse, edged in black so it shows on any
//! ground: worked out pixel by pixel and drawn as runs along each row, so it stays sharp at any
//! size and no edge is drawn twice.

use crate::i18n::mark;
use crate::theme::color;

/// The player's own crosshair is this many cells square, the middle one on the mouse.
pub const GRID: usize = 15;

/// How big the crosshair can be drawn (`Look::size`).
pub const SIZE_MIN: f32 = 0.5;
pub const SIZE_MAX: f32 = 2.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Style {
    /// Four arms round a gap, and a dot.
    #[default]
    Classic,
    /// The arms without the dot.
    Cross,
    Dot,
    Circle,
    CircleDot,
    /// The arms joined in the middle.
    Plus,
    /// The arms turned to the corners, and a dot.
    Diagonal,
    /// A point below the mouse, and a dot.
    Chevron,
    /// Painted by the player (`Look::custom`).
    Custom,
}

/// Every style, in Options' order: its name in the settings and its label.
pub const STYLES: [(Style, &str, &str); 9] = [
    (Style::Classic, "Classic", mark("crosshair::Classic")),
    (Style::Cross, "Cross", mark("crosshair::Cross")),
    (Style::Dot, "Dot", mark("crosshair::Dot")),
    (Style::Circle, "Circle", mark("crosshair::Circle")),
    (Style::CircleDot, "CircleDot", mark("crosshair::Circle and dot")),
    (Style::Plus, "Plus", mark("crosshair::Plus")),
    (Style::Diagonal, "Diagonal", mark("crosshair::Diagonal")),
    (Style::Chevron, "Chevron", mark("crosshair::Chevron")),
    (Style::Custom, "Custom", mark("crosshair::Your own")),
];

impl Style {
    pub fn name(self) -> &'static str {
        STYLES.iter().find(|s| s.0 == self).map_or("Classic", |s| s.1)
    }

    pub fn from_name(name: &str) -> Option<Self> {
        STYLES.iter().find(|s| s.1.eq_ignore_ascii_case(name)).map(|s| s.0)
    }
}

/// The colours offered: the arms' and the dot's. The first is the game's own, white with an
/// ember dot.
pub const COLOURS: [(u32, u32); 8] = [
    (0xFFFFFF, color::ACCENT),
    (0xFFFFFF, 0xFFFFFF),
    (color::ACCENT, color::ACCENT),
    (0x5BFF7A, 0x5BFF7A),
    (0x45E8FF, 0x45E8FF),
    (0xFFE14C, 0xFFE14C),
    (0xFF5CD6, 0xFF5CD6),
    (0xFF3D3D, 0xFF3D3D),
];

/// How the crosshair looks, as Options keeps it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Look {
    pub style: Style,
    /// One of `COLOURS`.
    pub colour: usize,
    /// Times its usual size (`SIZE_MIN` to `SIZE_MAX`).
    pub size: f32,
    /// The player's own: a row a number, bit `c` the cell in column `c`.
    pub custom: [u16; GRID],
}

/// The player's own until they paint one: a diamond round a dot.
const DEFAULT_CUSTOM: [u16; GRID] = {
    let mut g = [0u16; GRID];
    let rows: [(usize, u16); 9] = [
        (3, 1 << 7),
        (4, 1 << 6 | 1 << 8),
        (5, 1 << 5 | 1 << 9),
        (6, 1 << 4 | 1 << 10),
        (7, 1 << 3 | 1 << 7 | 1 << 11),
        (8, 1 << 4 | 1 << 10),
        (9, 1 << 5 | 1 << 9),
        (10, 1 << 6 | 1 << 8),
        (11, 1 << 7),
    ];
    let mut i = 0;
    while i < rows.len() {
        g[rows[i].0] = rows[i].1;
        i += 1;
    }
    g
};

impl Default for Look {
    fn default() -> Self {
        Look { style: Style::Classic, colour: 0, size: 1.0, custom: DEFAULT_CUSTOM }
    }
}

impl Look {
    pub fn cell(&self, row: usize, col: usize) -> bool {
        self.custom[row] >> col & 1 == 1
    }

    pub fn set_cell(&mut self, row: usize, col: usize, on: bool) {
        if on {
            self.custom[row] |= 1 << col;
        } else {
            self.custom[row] &= !(1 << col);
        }
    }

    /// The player's own as kept: four hex digits a row.
    pub fn custom_text(&self) -> String {
        self.custom.iter().map(|r| format!("{r:04X}")).collect()
    }

    /// Read back; `None` for anything but `GRID` rows of four hex digits.
    pub fn parse_custom(text: &str) -> Option<[u16; GRID]> {
        if text.len() != GRID * 4 || !text.is_ascii() {
            return None;
        }
        let mut g = [0u16; GRID];
        for (i, row) in g.iter_mut().enumerate() {
            *row = u16::from_str_radix(&text[i * 4..i * 4 + 4], 16).ok()? & ((1 << GRID) - 1);
        }
        Some(g)
    }
}

/// What a pixel round the mouse is.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Px {
    None,
    Edge,
    Arm,
    Dot,
}

/// What `style` puts at pixel (`x`, `y`) from the mouse (the mouse at the corner between four
/// pixels), drawn `k` times its usual size.
fn shape(look: &Look, k: f32, x: i32, y: i32) -> Px {
    let gap = (4.0 * k).round() as i32;
    let len = (7.0 * k).round().max(2.0) as i32;
    let t = (2.0 * k).round().max(2.0) as i32;
    let half = t / 2;
    let band = |v: i32| (-half..t - half).contains(&v);
    let arms = |gap: i32| {
        let reach = |v: i32| (gap..gap + len).contains(&v) || (-gap - len..-gap).contains(&v);
        (band(y) && reach(x)) || (band(x) && reach(y))
    };
    let dot = band(x) && band(y);
    // The pixel's middle, for the round styles.
    let (fx, fy) = (x as f32 + 0.5, y as f32 + 0.5);
    let d = (fx * fx + fy * fy).sqrt();
    let ring = || {
        let r = 7.0 * k;
        (d - r).abs() <= t as f32 / 2.0
    };
    let arm = |on: bool| if on { Px::Arm } else { Px::None };
    match look.style {
        Style::Classic if dot => Px::Dot,
        Style::Classic | Style::Cross => arm(arms(gap)),
        Style::Dot => arm(d <= (2.5 * k).max(1.5)),
        Style::Circle => arm(ring()),
        Style::CircleDot if dot => Px::Dot,
        Style::CircleDot => arm(ring()),
        Style::Plus => arm(arms(0) || dot),
        Style::Diagonal | Style::Chevron if dot => Px::Dot,
        Style::Diagonal => {
            let (ax, ay) = (fx.abs(), fy.abs());
            let along = (ax + ay) / std::f32::consts::SQRT_2;
            arm((ax - ay).abs() <= t as f32 * 0.75 && along >= gap as f32 && along <= (gap + len) as f32)
        }
        Style::Chevron => {
            let ax = fx.abs();
            arm(fy > 0.0 && (ax - fy).abs() <= t as f32 * 0.75 && fy >= gap as f32 * 0.75 && fy <= (gap + len) as f32 * 0.8)
        }
        Style::Custom => {
            let c = (2.0 * k).round().max(1.0) as i32;
            let o = -(c * GRID as i32) / 2;
            let (col, row) = ((x - o).div_euclid(c), (y - o).div_euclid(c));
            let inside = (0..GRID as i32).contains(&col) && (0..GRID as i32).contains(&row);
            arm(inside && look.cell(row as usize, col as usize))
        }
    }
}

/// How far from the mouse any style reaches, in pixels, at `k`.
fn reach(k: f32) -> i32 {
    (16.0 * k).ceil() as i32 + 3
}

/// The crosshair at `at`, `k` times its usual size (the game passes the HUD's scale times
/// `Look::size`).
pub fn draw(renderer: &mut bsk_render::Renderer, at: (f32, f32), k: f32, look: &Look) {
    let (cx, cy) = (at.0.round() as i32, at.1.round() as i32);
    let n = reach(k);
    let side = (2 * n + 1) as usize;
    let mut px = vec![Px::None; side * side];
    for y in -n..=n {
        for x in -n..=n {
            px[(y + n) as usize * side + (x + n) as usize] = shape(look, k, x, y);
        }
    }
    // The black edge: every empty pixel beside a drawn one, corners too.
    let filled = |x: i32, y: i32| x >= -n && y >= -n && x <= n && y <= n && matches!(px[(y + n) as usize * side + (x + n) as usize], Px::Arm | Px::Dot);
    let mut edged = px.clone();
    for y in -n..=n {
        for x in -n..=n {
            let i = (y + n) as usize * side + (x + n) as usize;
            if px[i] == Px::None && (-1..=1).any(|dy| (-1..=1).any(|dx| filled(x + dx, y + dy))) {
                edged[i] = Px::Edge;
            }
        }
    }
    let (arm, dot) = COLOURS[look.colour.min(COLOURS.len() - 1)];
    for y in -n..=n {
        let row = &edged[(y + n) as usize * side..(y + n + 1) as usize * side];
        let mut x = 0;
        while x < side {
            let kind = row[x];
            let start = x;
            while x < side && row[x] == kind {
                x += 1;
            }
            let (rgb, alpha) = match kind {
                Px::None => continue,
                Px::Edge => (0x000000, 0.75),
                Px::Arm => (arm, 0.95),
                Px::Dot => (dot, 1.0),
            };
            let left = cx + start as i32 - n;
            renderer.fill(left as f32, (cy + y) as f32, (x - start) as f32, 1.0, rgb, alpha);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn custom_round_trip() {
        let mut look = Look::default();
        look.set_cell(0, 14, true);
        look.set_cell(7, 7, false);
        let back = Look::parse_custom(&look.custom_text()).unwrap();
        assert_eq!(back, look.custom);
        assert!(Look::parse_custom("12").is_none());
        assert!(Look::parse_custom(&"ZZZZ".repeat(GRID)).is_none());
        for (s, name, _) in STYLES {
            assert_eq!(Style::from_name(name), Some(s));
        }
    }

    #[test]
    fn every_style_draws_round_the_mouse() {
        for (style, _, _) in STYLES {
            for k in [0.5, 1.0, 3.0] {
                let look = Look { style, ..Look::default() };
                let n = reach(k);
                let drawn = (-n..=n).flat_map(|y| (-n..=n).map(move |x| (x, y))).filter(|&(x, y)| shape(&look, k, x, y) != Px::None).count();
                assert!(drawn > 0, "{style:?} at {k}");
                // Nothing reaches the edge, where its black edge would be cut off.
                for i in -n..=n {
                    for (x, y) in [(i, -n), (i, n), (-n, i), (n, i)] {
                        assert!(shape(&look, k, x, y) == Px::None, "{style:?} at {k} reaches {x},{y}");
                    }
                }
            }
        }
    }
}
