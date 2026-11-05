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

/// The colours the player's own is painted in, each square its own (`Look::custom`).
pub const PALETTE: [u32; 10] = [0xFFFFFF, color::ACCENT, 0xFF3D3D, 0xFFE14C, 0x5BFF7A, 0x45E8FF, 0x4C7DFF, 0xA45CFF, 0xFF5CD6, 0x101010];

/// The player's own drawing: each square empty (0) or a `PALETTE` colour (its index plus one).
pub type Drawing = [[u8; GRID]; GRID];

/// How the crosshair looks, as Options keeps it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Look {
    pub style: Style,
    /// One of `COLOURS`, for the styles (the player's own has its own colours).
    pub colour: usize,
    /// Times its usual size (`SIZE_MIN` to `SIZE_MAX`).
    pub size: f32,
    pub custom: Drawing,
}

/// The player's own until they paint one: a white diamond round an ember dot.
const DEFAULT_CUSTOM: Drawing = {
    let mut g = [[0u8; GRID]; GRID];
    let cells: [(usize, usize); 16] = [
        (3, 7), (4, 6), (4, 8), (5, 5), (5, 9), (6, 4), (6, 10), (7, 3),
        (7, 11), (8, 4), (8, 10), (9, 5), (9, 9), (10, 6), (10, 8), (11, 7),
    ];
    let mut i = 0;
    while i < cells.len() {
        g[cells[i].0][cells[i].1] = 1;
        i += 1;
    }
    g[7][7] = 2;
    g
};

impl Default for Look {
    fn default() -> Self {
        Look { style: Style::Classic, colour: 0, size: 1.0, custom: DEFAULT_CUSTOM }
    }
}

impl Look {
    /// The `PALETTE` colour of a square of the player's own, or `None` where it's empty.
    pub fn cell(&self, row: usize, col: usize) -> Option<usize> {
        self.custom[row][col].checked_sub(1).map(usize::from)
    }

    pub fn set_cell(&mut self, row: usize, col: usize, paint: Option<usize>) {
        self.custom[row][col] = paint.map_or(0, |p| p.min(PALETTE.len() - 1) as u8 + 1);
    }

    /// The player's own as kept: which squares are filled (four hex digits a row, bit `c` the
    /// square in column `c`), then each filled square's colour (a hex digit each, row by row).
    pub fn custom_text(&self) -> String {
        let mut mask = String::new();
        let mut colours = String::new();
        for row in &self.custom {
            let bits = row.iter().enumerate().filter(|(_, &c)| c != 0).fold(0u16, |b, (i, _)| b | 1 << i);
            mask += &format!("{bits:04X}");
            for &c in row.iter().filter(|&&c| c != 0) {
                colours += &format!("{:X}", c - 1);
            }
        }
        mask + &colours
    }

    /// A code to share it by: `DD`, the style and the colour (a digit each), the size in percent
    /// (two hex digits) and, for the player's own, the drawing (`custom_text`). The game's own
    /// crosshair is `DD0064`.
    pub fn code(&self) -> String {
        let style = STYLES.iter().position(|s| s.0 == self.style).unwrap_or(0);
        let size = (self.size * 100.0).round().clamp(0.0, 255.0) as u8;
        let mut code = format!("DD{style}{}{size:02X}", self.colour.min(COLOURS.len() - 1));
        if self.style == Style::Custom {
            code += &self.custom_text();
        }
        code
    }

    /// The look a shared code gives, or `None` if it isn't one. Spaces and dashes in it don't
    /// matter, nor does case. A code for one of the styles keeps `self`'s own drawing.
    pub fn from_code(&self, text: &str) -> Option<Look> {
        let code: String = text.chars().filter(|c| !c.is_whitespace() && *c != '-').collect::<String>().to_ascii_uppercase();
        let rest = code.strip_prefix("DD")?;
        if rest.len() < 4 || !rest.is_ascii() {
            return None;
        }
        let style = STYLES.get(rest[..1].parse::<usize>().ok()?)?.0;
        let colour = rest[1..2].parse::<usize>().ok().filter(|&c| c < COLOURS.len())?;
        let size = u8::from_str_radix(&rest[2..4], 16).ok()? as f32 / 100.0;
        let custom = match (style, &rest[4..]) {
            (Style::Custom, grid) => Look::parse_custom(grid)?,
            (_, "") => self.custom,
            _ => return None,
        };
        Some(Look { style, colour, size: size.clamp(SIZE_MIN, SIZE_MAX), custom })
    }

    /// Read back (`custom_text`); `None` unless it's exactly that.
    pub fn parse_custom(text: &str) -> Option<Drawing> {
        if text.len() < GRID * 4 || !text.is_ascii() {
            return None;
        }
        let (mask, colours) = text.split_at(GRID * 4);
        let mut colours = colours.chars();
        let mut g = [[0u8; GRID]; GRID];
        for (i, row) in g.iter_mut().enumerate() {
            let bits = u16::from_str_radix(&mask[i * 4..i * 4 + 4], 16).ok()?;
            if bits >> GRID != 0 {
                return None;
            }
            for (col, cell) in row.iter_mut().enumerate() {
                if bits >> col & 1 == 1 {
                    let c = colours.next()?.to_digit(16)? as usize;
                    if c >= PALETTE.len() {
                        return None;
                    }
                    *cell = c as u8 + 1;
                }
            }
        }
        colours.next().is_none().then_some(g)
    }
}

/// What a pixel round the mouse is.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Px {
    None,
    Edge,
    Arm,
    Dot,
    /// A square of the player's own, in its `PALETTE` colour.
    Paint(u32),
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
            match if inside { look.cell(row as usize, col as usize) } else { None } {
                Some(p) => Px::Paint(PALETTE[p]),
                None => Px::None,
            }
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
    let filled = |x: i32, y: i32| x >= -n && y >= -n && x <= n && y <= n && !matches!(px[(y + n) as usize * side + (x + n) as usize], Px::None | Px::Edge);
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
                Px::Paint(rgb) => (rgb, 1.0),
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
        look.set_cell(0, 14, Some(9));
        look.set_cell(7, 7, None);
        look.set_cell(14, 0, Some(4));
        let text = look.custom_text();
        assert_eq!(Look::parse_custom(&text), Some(look.custom));
        // Each square keeps its own colour.
        let back = Look { custom: Look::parse_custom(&text).unwrap(), ..look };
        assert_eq!((back.cell(0, 14), back.cell(14, 0), back.cell(3, 7), back.cell(7, 7)), (Some(9), Some(4), Some(0), None));
        assert!(Look::parse_custom("12").is_none());
        assert!(Look::parse_custom(&"ZZZZ".repeat(GRID)).is_none());
        // A colour short, one too many, or one not in the palette.
        assert!(Look::parse_custom(&text[..text.len() - 1]).is_none());
        assert!(Look::parse_custom(&format!("{text}0")).is_none());
        assert!(Look::parse_custom(&format!("{}F", &text[..text.len() - 1])).is_none());
        for (s, name, _) in STYLES {
            assert_eq!(Style::from_name(name), Some(s));
        }
    }

    #[test]
    fn share_codes() {
        let mine = Look::default();
        assert_eq!(mine.code(), "DD0064");
        // A style's code keeps the drawing already there; the player's own brings its own.
        let mut theirs = Look { style: Style::Custom, colour: 3, size: 1.5, custom: [[0; GRID]; GRID] };
        theirs.set_cell(7, 7, Some(2));
        theirs.set_cell(7, 8, Some(5));
        let code = theirs.code();
        assert_eq!(code.len(), 6 + GRID * 4 + 2);
        assert_eq!(mine.from_code(&code), Some(theirs));
        assert_eq!(mine.from_code(&format!(" {}-{} ", &code[..6], &code[6..].to_lowercase())), Some(theirs));
        let dot = Look { style: Style::Dot, colour: 7, size: 2.0, ..theirs };
        assert_eq!(dot.code(), "DD27C8");
        assert_eq!(mine.from_code("DD27C8"), Some(Look { custom: mine.custom, ..dot }));
        for bad in ["", "DD", "XX0064", "DD9064", "DD0864", "DD00ZZ", "DD0064FFFF", "DD8064", "DD80641234"] {
            assert_eq!(mine.from_code(bad), None, "{bad}");
        }
        // An empty drawing is a drawing.
        assert!(mine.from_code(&format!("DD8064{}", "0000".repeat(GRID))).is_some());
        // Out of range sizes are brought into range.
        assert_eq!(mine.from_code("DD00FF").unwrap().size, SIZE_MAX);
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
