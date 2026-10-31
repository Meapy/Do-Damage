//! The look shared by the HUD and the screens, the same as the website's: dark violet glass
//! panels with soft shadows, rounded corners and gentle gradients, an ember accent, and buttons,
//! fields and slots that answer the mouse. Everything takes the caller's scale `s`.

use crate::hud::Ui;
use bsk_render::Renderer;

/// The palette: the website's (website/style.css), a pixel night of dark violets with ember for
/// what can be pressed and gold for what's precious.
pub mod color {
    /// Behind everything on the screens outside the game.
    pub const BACKGROUND: u32 = 0x0C0915;
    pub const PANEL_TOP: u32 = 0x1F1733;
    pub const PANEL_BOTTOM: u32 = 0x161025;
    pub const BORDER: u32 = 0x2C2244;
    pub const BORDER_HI: u32 = 0x44365F;
    pub const TEXT: u32 = 0xF1ECFF;
    pub const TEXT_DIM: u32 = 0xB3A9CC;
    pub const TEXT_MUTED: u32 = 0x7A7096;
    /// Ember: the main thing to do, and what's switched on.
    pub const ACCENT: u32 = 0xFF7A3D;
    pub const ACCENT_HI: u32 = 0xFFA36B;
    pub const ACCENT_TEXT: u32 = 0x1F0A02;
    pub const GOLD: u32 = 0xFFC65C;
    pub const DANGER: u32 = 0xFF3D5A;
    pub const GOOD: u32 = 0x5BE3A0;
    pub const SLOT_TOP: u32 = 0x2A2042;
    pub const SLOT_BOTTOM: u32 = 0x1A1329;
    pub const FIELD: u32 = 0x0C0915;
    pub const FIELD_BOTTOM: u32 = 0x150F24;
    pub const TRACK: u32 = 0x08060F;
    /// Quiet buttons, and switches and toggles that are off.
    pub const SECONDARY: u32 = 0x2E2448;
    /// Small buttons and chips at rest, and under the mouse.
    pub const CHIP: u32 = 0x221A36;
    pub const CHIP_HI: u32 = 0x4A3A6A;
    /// Inner boxes inside a panel (cards, lists), top and bottom.
    pub const INSET_TOP: u32 = 0x1A1430;
    pub const INSET_BOTTOM: u32 = 0x120C20;
    /// An inner box that's lit (chosen, or on offer), top and bottom.
    pub const WARM_TOP: u32 = 0x2A1A1C;
    pub const WARM_BOTTOM: u32 = 0x1A0F12;
    /// An empty slot's picture of what goes in it.
    pub const SILHOUETTE: u32 = 0x3A2E55;
}

fn rgba(rgb: u32, a: f32) -> [f32; 4] {
    let [r, g, b] = bsk_render::rgb_f32(rgb);
    [r, g, b, a]
}

fn mix(a: [f32; 4], b: [f32; 4], t: f32) -> [f32; 4] {
    std::array::from_fn(|i| a[i] + (b[i] - a[i]) * t)
}

/// Lighter (`k` > 0) or darker (`k` < 0) by a fraction.
pub fn shade(rgb: u32, k: f32) -> u32 {
    let [r, g, b] = bsk_render::rgb_f32(rgb);
    let f = |c: f32| -> u32 {
        let v = if k >= 0.0 { c + (1.0 - c) * k } else { c * (1.0 + k) };
        (v.clamp(0.0, 1.0) * 255.0).round() as u32
    };
    (f(r) << 16) | (f(g) << 8) | f(b)
}

/// Points around a rounded rectangle, clockwise from the top-left corner's arc.
fn outline(r: [f32; 4], radius: f32) -> Vec<[f32; 2]> {
    let [x, y, w, h] = r;
    let rad = radius.min(w / 2.0).min(h / 2.0).max(0.0);
    const STEPS: usize = 5;
    let corners = [
        (x + rad, y + rad, std::f32::consts::PI),
        (x + w - rad, y + rad, -std::f32::consts::FRAC_PI_2),
        (x + w - rad, y + h - rad, 0.0),
        (x + rad, y + h - rad, std::f32::consts::FRAC_PI_2),
    ];
    let mut pts = Vec::with_capacity(4 * (STEPS + 1));
    for (cx, cy, start) in corners {
        for i in 0..=STEPS {
            let a = start + std::f32::consts::FRAC_PI_2 * i as f32 / STEPS as f32;
            pts.push([cx + rad * a.cos(), cy + rad * a.sin()]);
        }
    }
    pts
}

/// A rounded rectangle shaded from `top` to `bottom` (colour, alpha).
pub fn rounded(renderer: &mut Renderer, r: [f32; 4], radius: f32, top: (u32, f32), bottom: (u32, f32)) {
    if r[2] <= 0.0 || r[3] <= 0.0 {
        return;
    }
    let (t, b) = (rgba(top.0, top.1), rgba(bottom.0, bottom.1));
    let at = |p: [f32; 2]| mix(t, b, ((p[1] - r[1]) / r[3]).clamp(0.0, 1.0));
    let pts = outline(r, radius);
    let centre = [r[0] + r[2] / 2.0, r[1] + r[3] / 2.0];
    let white = renderer.white();
    for i in 0..pts.len() {
        let (a, b2) = (pts[i], pts[(i + 1) % pts.len()]);
        renderer.triangle_colors(white, [centre, a, b2], [[0.5, 0.5]; 3], [at(centre), at(a), at(b2)]);
    }
}

/// A flat rounded rectangle.
pub fn fill(renderer: &mut Renderer, r: [f32; 4], radius: f32, rgb: u32, alpha: f32) {
    rounded(renderer, r, radius, (rgb, alpha), (rgb, alpha));
}

/// A soft shadow under a rectangle, darkest close in.
pub fn shadow(renderer: &mut Renderer, r: [f32; 4], radius: f32, size: f32, alpha: f32) {
    const LAYERS: usize = 6;
    for i in (1..=LAYERS).rev() {
        let e = size * i as f32 / LAYERS as f32;
        let a = alpha / LAYERS as f32 * 1.4;
        fill(renderer, [r[0] - e, r[1] - e + size * 0.35, r[2] + 2.0 * e, r[3] + 2.0 * e], radius + e, 0x000000, a);
    }
}

/// A rounded frame: `border` around the inside of `r`, `width` wide.
pub fn frame(renderer: &mut Renderer, r: [f32; 4], radius: f32, width: f32, border: (u32, f32), inside: ((u32, f32), (u32, f32))) {
    rounded(renderer, r, radius, border, (shade(border.0, -0.25), border.1));
    let w = width;
    rounded(renderer, [r[0] + w, r[1] + w, r[2] - 2.0 * w, r[3] - 2.0 * w], (radius - w).max(0.0), inside.0, inside.1);
}

/// A glass panel: shadow, a light edge, a dark body, a faint highlight along the top.
pub fn panel(renderer: &mut Renderer, r: [f32; 4], s: f32) {
    let radius = 10.0 * s;
    shadow(renderer, r, radius, 16.0 * s, 0.45);
    frame(renderer, r, radius, s.max(1.0), (color::BORDER_HI, 0.9), ((color::PANEL_TOP, 0.93), (color::PANEL_BOTTOM, 0.96)));
    fill(renderer, [r[0] + radius, r[1] + s.max(1.0), r[2] - 2.0 * radius, s.max(1.0)], 0.0, 0xFFFFFF, 0.08);
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Kind {
    /// Ember, for the main thing to do.
    Primary,
    /// Quiet.
    Secondary,
    /// Red, for what can't be undone.
    Danger,
}

impl Ui<'_> {
    /// A button: rounded, shaded, brighter under the mouse.
    pub fn button(&mut self, r: [f32; 4], label: &str, kind: Kind, hover: bool, s: f32) {
        let radius = 6.0 * s;
        let (base, text) = match kind {
            Kind::Primary => (color::ACCENT, color::ACCENT_TEXT),
            Kind::Secondary => (color::SECONDARY, color::TEXT),
            Kind::Danger => (color::DANGER, 0xFFFFFF),
        };
        let base = if hover { shade(base, 0.15) } else { base };
        shadow(self.renderer, r, radius, 4.0 * s, 0.35);
        rounded(self.renderer, r, radius, (shade(base, 0.18), 1.0), (shade(base, -0.18), 1.0));
        fill(self.renderer, [r[0] + radius * 0.6, r[1] + s.max(1.0), r[2] - radius * 1.2, s.max(1.0)], 0.0, 0xFFFFFF, 0.25);
        let size = (15.0 * s).round().max(10.0);
        let tw = self.measure(label, size, true);
        let ty = r[1] + (r[3] - size * 1.22) / 2.0;
        // Dark text on gold reads better without the glow; draw it twice for weight.
        self.label(label, r[0] + (r[2] - tw) / 2.0, ty, size, true, text);
    }

    /// A button with its key after the label, on a small keycap (Enter, an NPC's button): the key
    /// a little smaller and set apart, so "Choose" and "V" don't read as one word.
    pub fn button_key(&mut self, r: [f32; 4], label: &str, key: &str, kind: Kind, hover: bool, s: f32) {
        if key.is_empty() {
            self.button(r, label, kind, hover, s);
            return;
        }
        self.button(r, "", kind, hover, s);
        let text = match kind {
            Kind::Primary => color::ACCENT_TEXT,
            Kind::Secondary => color::TEXT,
            Kind::Danger => 0xFFFFFF,
        };
        let size = (15.0 * s).round().max(10.0);
        let ks = (12.0 * s).round().max(9.0);
        let tw = self.measure(label, size, true);
        let kw = self.measure(key, ks, true);
        let (pad, gap) = (5.0 * s, 8.0 * s);
        let cap_w = kw + 2.0 * pad;
        let cap_h = ks * 1.22 + 4.0 * s;
        let x = r[0] + (r[2] - tw - gap - cap_w) / 2.0;
        let ty = r[1] + (r[3] - size * 1.22) / 2.0;
        self.label(label, x, ty, size, true, text);
        let cap = [x + tw + gap, r[1] + (r[3] - cap_h) / 2.0, cap_w, cap_h];
        fill(self.renderer, cap, 4.0 * s, 0x000000, 0.28);
        frame(self.renderer, cap, 4.0 * s, s.max(1.0), (0xFFFFFF, 0.35), ((0x000000, 0.0), (0x000000, 0.0)));
        self.label(key, cap[0] + pad, cap[1] + 2.0 * s, ks, true, if kind == Kind::Primary { 0xFFF3D6 } else { color::GOLD });
    }

    /// A text field's box; the caller draws the text.
    pub fn field_box(&mut self, r: [f32; 4], focused: bool, s: f32) {
        let radius = 5.0 * s;
        if focused {
            fill(self.renderer, [r[0] - 3.0 * s, r[1] - 3.0 * s, r[2] + 6.0 * s, r[3] + 6.0 * s], radius + 3.0 * s, color::ACCENT, 0.18);
        }
        let border = if focused { color::ACCENT } else { color::BORDER };
        frame(self.renderer, r, radius, s.max(1.0), (border, 1.0), ((color::FIELD, 0.95), (shade(color::FIELD, 0.06), 0.95)));
    }

    /// An item slot: a shaded well, its edge in the item's tier colour when it holds one.
    pub fn slot_box(&mut self, r: [f32; 4], edge: Option<u32>, hover: bool, s: f32) {
        let radius = 5.0 * s;
        let border = edge.unwrap_or(color::BORDER);
        let (top, bottom) = if hover { (shade(color::SLOT_TOP, 0.12), shade(color::SLOT_BOTTOM, 0.12)) } else { (color::SLOT_TOP, color::SLOT_BOTTOM) };
        frame(self.renderer, r, radius, s.max(1.0), (border, if edge.is_some() { 0.85 } else { 1.0 }), ((top, 1.0), (bottom, 1.0)));
        // A faint glow of the tier colour inside.
        if let Some(c) = edge {
            rounded(self.renderer, [r[0] + s, r[1] + r[3] * 0.45, r[2] - 2.0 * s, r[3] * 0.55 - s], radius, (c, 0.0), (c, 0.16));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shading() {
        assert_eq!(shade(0x808080, 0.0), 0x808080);
        assert_eq!(shade(0x000000, 1.0), 0xFFFFFF);
        assert_eq!(shade(0xFFFFFF, -1.0), 0x000000);
        let pts = outline([0.0, 0.0, 100.0, 50.0], 10.0);
        assert!(pts.iter().all(|p| p[0] >= -0.01 && p[0] <= 100.01 && p[1] >= -0.01 && p[1] <= 50.01));
    }
}

/// An X (a close or delete mark) centred on (cx, cy): two bars `half` long each way from the
/// centre, `thick` wide. Drawn, because the font has no such character.
pub fn cross(renderer: &mut Renderer, cx: f32, cy: f32, half: f32, thick: f32, rgb: u32) {
    let white = renderer.white();
    let len = half * 2.0 * std::f32::consts::SQRT_2;
    for angle in [std::f32::consts::FRAC_PI_4, -std::f32::consts::FRAC_PI_4] {
        renderer.sprite(white, bsk_render::Quad::rotated(cx, cy, len, thick, angle), bsk_render::Tint::color(rgb, 1.0));
    }
}

/// A five-pointed star centred on (cx, cy), `outer` from the centre to a point. Drawn, because
/// the font has no star character.
pub fn star(renderer: &mut Renderer, cx: f32, cy: f32, outer: f32, rgb: u32) {
    let white = renderer.white();
    let inner = outer * 0.4;
    let point = |i: i32| {
        let a = -std::f32::consts::FRAC_PI_2 + i as f32 * std::f32::consts::PI / 5.0;
        let r = if i % 2 == 0 { outer } else { inner };
        [cx + r * a.cos(), cy + r * a.sin()]
    };
    let tint = bsk_render::Tint::color(rgb, 1.0);
    for i in 0..10 {
        renderer.triangle(white, [[cx, cy], point(i), point(i + 1)], [[0.5, 0.5]; 3], tint);
    }
}
