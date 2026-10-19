//! Text in the AIR client's fonts (Myriad Pro), rasterised glyph by glyph into the atlas. Each
//! glyph has a white fill and a black glow, drawn in two passes so one glyph's glow never covers
//! its neighbour; the fill takes the text colour as a tint.

use ab_glyph::{Font, FontArc, PxScale, ScaleFont};
use bsk_render::{AtlasRect, Quad, Renderer, Tint};
use std::collections::HashMap;

/// Room around each glyph for its glow.
const PAD: i32 = 3;

pub struct Fonts {
    regular: FontArc,
    bold: FontArc,
}

impl Fonts {
    pub fn new(regular: Vec<u8>, bold: Vec<u8>) -> Result<Self, String> {
        let load = |b: Vec<u8>| FontArc::try_from_vec(b).map_err(|e| e.to_string());
        Ok(Self { regular: load(regular)?, bold: load(bold)? })
    }

    fn font(&self, bold: bool) -> &FontArc {
        if bold { &self.bold } else { &self.regular }
    }
}

/// `SimpleText` sizes are em sizes in pixels; ab_glyph scales by ascent-to-descent height.
fn scale(font: &FontArc, size: f32) -> PxScale {
    let em = font.units_per_em().unwrap_or(1000.0);
    PxScale::from(size * font.height_unscaled() / em)
}

#[derive(Clone, Copy)]
struct Glyph {
    /// (fill, glow) in the atlas, with the offset of their top-left from the pen position on
    /// the baseline. `None` for blank glyphs (spaces).
    images: Option<(AtlasRect, AtlasRect, [f32; 2])>,
    advance: f32,
}

#[derive(Clone, Copy, PartialEq, Eq, Hash)]
struct Key {
    bold: bool,
    size: u32,
    ch: char,
}

/// The glow: `GlowFilter(black, alpha 1, blur 3, strength 2)` as a 3×3 box blur doubled.
#[derive(Clone, Copy)]
pub struct Style {
    pub size: f32,
    pub bold: bool,
    pub color: u32,
}

#[derive(Default)]
pub struct Text {
    glyphs: HashMap<Key, Glyph>,
}

impl Text {
    fn glyph(&mut self, fonts: &Fonts, renderer: &mut Renderer, key: Key) -> Glyph {
        if let Some(g) = self.glyphs.get(&key) {
            return *g;
        }
        let font = fonts.font(key.bold);
        let scaled = font.as_scaled(scale(font, key.size as f32));
        let id = font.glyph_id(key.ch);
        let advance = scaled.h_advance(id);
        let glyph = id.with_scale(scaled.scale());
        let images = font.outline_glyph(glyph).and_then(|outlined| {
            let bounds = outlined.px_bounds();
            let (w, h) = (bounds.width() as i32 + 2 * PAD, bounds.height() as i32 + 2 * PAD);
            let mut coverage = vec![0f32; (w * h) as usize];
            outlined.draw(|x, y, c| {
                let i = ((y as i32 + PAD) * w + x as i32 + PAD) as usize;
                if let Some(v) = coverage.get_mut(i) {
                    *v = c.min(1.0);
                }
            });
            let fill = image::RgbaImage::from_fn(w as u32, h as u32, |x, y| {
                image::Rgba([255, 255, 255, (coverage[(y as i32 * w + x as i32) as usize] * 255.0).round() as u8])
            });
            let glow = image::RgbaImage::from_fn(w as u32, h as u32, |x, y| {
                let mut sum = 0.0;
                for dy in -1..=1 {
                    for dx in -1..=1 {
                        let (sx, sy) = (x as i32 + dx, y as i32 + dy);
                        if sx >= 0 && sy >= 0 && sx < w && sy < h {
                            sum += coverage[(sy * w + sx) as usize];
                        }
                    }
                }
                let a = (sum / 9.0 * 2.0).min(1.0);
                image::Rgba([0, 0, 0, (a * 255.0).round() as u8])
            });
            let fill_rect = renderer.add_image(&fill)?;
            let glow_rect = renderer.add_image(&glow)?;
            Some((fill_rect, glow_rect, [bounds.min.x - PAD as f32, bounds.min.y - PAD as f32]))
        });
        let g = Glyph { images, advance };
        self.glyphs.insert(key, g);
        g
    }

    /// How far below a line's top (the `y` given to `draw`) the middle of its digits sits, to
    /// line a number up with an icon beside it.
    pub fn digit_middle(&self, fonts: &Fonts, size: f32, bold: bool) -> f32 {
        let size = size.round();
        let font = fonts.font(bold);
        let scaled = font.as_scaled(scale(font, size));
        let glyph = scaled.scaled_glyph('0');
        let mid = font.outline_glyph(glyph).map_or(-scaled.ascent() * 0.35, |o| {
            let b = o.px_bounds();
            (b.min.y + b.max.y) / 2.0
        });
        scaled.ascent() + mid
    }

    /// Width of a line in pixels.
    pub fn measure(&mut self, fonts: &Fonts, renderer: &mut Renderer, text: &str, style: Style) -> f32 {
        let size = style.size.round() as u32;
        text.chars().map(|ch| self.glyph(fonts, renderer, Key { bold: style.bold, size, ch }).advance).sum()
    }

    /// Draw a line with its top-left at (x, y).
    pub fn draw(&mut self, fonts: &Fonts, renderer: &mut Renderer, text: &str, x: f32, y: f32, style: Style) {
        let size = style.size.round() as u32;
        let font = fonts.font(style.bold);
        let ascent = font.as_scaled(scale(font, size as f32)).ascent();
        let baseline = (y + ascent).round();
        let placed = self.place(fonts, renderer, text, x, baseline, size, style.bold);
        for &(_, glow, gx, gy) in &placed {
            renderer.sprite(glow, Quad::rect(gx, gy, glow.w as f32, glow.h as f32), Tint::NONE);
        }
        let [r, gc, b] = bsk_render::rgb_f32(style.color);
        let tint = Tint { mul: [r, gc, b, 1.0], add: [0.0; 3] };
        for &(fill, _, gx, gy) in &placed {
            renderer.sprite(fill, Quad::rect(gx, gy, fill.w as f32, fill.h as f32), tint);
        }
    }

    /// Lay out a line: each glyph on a whole pixel, so none is blurred across two.
    #[allow(clippy::too_many_arguments)]
    fn place(
        &mut self,
        fonts: &Fonts,
        renderer: &mut Renderer,
        text: &str,
        x: f32,
        baseline: f32,
        size: u32,
        bold: bool,
    ) -> Vec<(AtlasRect, AtlasRect, f32, f32)> {
        let mut placed = Vec::with_capacity(text.len());
        let mut pen = x.round();
        for ch in text.chars() {
            let g = self.glyph(fonts, renderer, Key { bold, size, ch });
            if let Some((fill, glow, off)) = g.images {
                placed.push((fill, glow, (pen + off[0]).round(), baseline + off[1]));
            }
            pen += g.advance;
        }
        placed
    }

    /// Interface text: crisp glyphs with a soft one-pixel shadow (none under dark text, which
    /// sits on light buttons), instead of the glow that keeps world text readable.
    pub fn draw_ui(&mut self, fonts: &Fonts, renderer: &mut Renderer, text: &str, x: f32, y: f32, style: Style) {
        let size = style.size.round() as u32;
        let font = fonts.font(style.bold);
        let ascent = font.as_scaled(scale(font, size as f32)).ascent();
        let baseline = (y + ascent).round();
        let placed = self.place(fonts, renderer, text, x, baseline, size, style.bold);
        let [r, gc, b] = bsk_render::rgb_f32(style.color);
        let light = 0.299 * r + 0.587 * gc + 0.114 * b > 0.45;
        if light {
            let shadow = Tint { mul: [0.0, 0.0, 0.0, 0.55], add: [0.0; 3] };
            let drop = (size as f32 / 14.0).round().max(1.0);
            for &(fill, _, gx, gy) in &placed {
                renderer.sprite(fill, Quad::rect(gx, gy + drop, fill.w as f32, fill.h as f32), shadow);
            }
        }
        let tint = Tint { mul: [r, gc, b, 1.0], add: [0.0; 3] };
        for &(fill, _, gx, gy) in &placed {
            renderer.sprite(fill, Quad::rect(gx, gy, fill.w as f32, fill.h as f32), tint);
        }
    }

    /// Draw a line centred on `cx`.
    pub fn draw_centered(&mut self, fonts: &Fonts, renderer: &mut Renderer, text: &str, cx: f32, y: f32, style: Style) {
        let w = self.measure(fonts, renderer, text, style);
        self.draw(fonts, renderer, text, cx - w / 2.0, y, style);
    }
}

#[cfg(test)]
mod glyph_tests {
    use ab_glyph::{Font, FontArc};

    /// Every special character the interface draws as text is in Myriad Pro (a missing one
    /// shows as an empty box). It has no heavy X (U+2715) or star (U+2605): those are drawn as
    /// shapes (`theme::cross`, `theme::star`).
    #[test]
    fn interface_characters_exist() {
        let root = concat!(env!("CARGO_MANIFEST_DIR"), "/../../client/src/com/company/ui/fonts/");
        for file in ["MyriadPro.otf", "MyriadProBold.otf"] {
            let font = FontArc::try_from_vec(std::fs::read(format!("{root}{file}")).unwrap()).unwrap();
            let missing: Vec<char> = "\u{b7}\u{2026}\u{2014}\u{2013}\u{2019}\u{d7}".chars().filter(|&c| font.glyph_id(c).0 == 0).collect();
            assert!(missing.is_empty(), "{file} lacks {missing:?}");
        }
    }
}
