//! Object sprites as the AIR client draws them (`TextureRedrawer.redraw` with
//! `GlowRedrawer.outlineGlow`): scaled up ×5 at size 100 on a padded canvas, with a soft black
//! outline. Results are cached in the atlas.

use crate::sprites::{Sprites, SrcRect};
use bsk_render::{AtlasRect, Renderer};
use image::{GenericImageView, Rgba, RgbaImage};

/// `TextureRedrawer.magic`: padding left, right and above the sprite, for the outline.
pub const PAD: u32 = 12;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
struct Key {
    src: SrcRect,
    mirror: bool,
    size: i32,
    dye: Option<Dye>,
    pad_bottom: bool,
    glow: u32,
}

/// Cloth and dye on a masked sprite (players): the mask's red channel takes `tex1`, its green
/// channel `tex2` (the TEX1/TEX2 stats).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct Dye {
    pub mask: SrcRect,
    pub tex1: i32,
    pub tex2: i32,
}

/// `TextureRedrawer.getTexture`: the high byte of a dye id says what it is (1: a colour,
/// 4/5/9/10: a cloth pattern from that textile set), the low 24 bits which.
fn dye_texture(sprites: &Sprites, id: i32) -> Option<RgbaImage> {
    if id == 0 {
        return None;
    }
    let (kind, value) = ((id >> 24) & 0xFF, (id & 0xFF_FFFF) as u32);
    let set = match kind {
        1 => {
            let [r, g, b] = [(value >> 16) as u8, (value >> 8) as u8, value as u8];
            return Some(RgbaImage::from_pixel(1, 1, Rgba([r, g, b, 255])));
        }
        4 => "textile4x4",
        5 => "textile5x5",
        9 => "textile9x9",
        10 => "textile10x10",
        _ => return None,
    };
    sprites.tile_src(set, value).map(|r| sprites.pixels(r))
}

/// `TextureRedrawer.retexture` (its pixel shader): the sprite at ×5, where the mask is red or
/// green replaced by the dye textures (tiled at ×5 pixel scale), scaled by the mask's amount.
fn retexture(src: &RgbaImage, mask: &RgbaImage, tex1: Option<&RgbaImage>, tex2: Option<&RgbaImage>) -> RgbaImage {
    let (w, h) = src.dimensions();
    RgbaImage::from_fn(w * 5, h * 5, |x, y| {
        let mut p = *src.get_pixel(x / 5, y / 5);
        let m = if x / 5 < mask.width() && y / 5 < mask.height() { *mask.get_pixel(x / 5, y / 5) } else { Rgba([0; 4]) };
        for (amount, tex) in [(m[0], tex1), (m[1], tex2)] {
            if let (true, Some(t)) = (amount > 0, tex) {
                let c = t.get_pixel(x % t.width(), y % t.height());
                for i in 0..3 {
                    p[i] = (c[i] as u32 * amount as u32 / 255) as u8;
                }
            }
        }
        p
    })
}

/// An item's own dye (`getRedrawnTextureFromType`): its Tex1 and Tex2 over its mask, as cloth
/// and dyes show in slots, shops and bags.
pub fn item_dye(sprites: &Sprites, desc: &bsk_data::ObjectDesc) -> Option<Dye> {
    let (set, index) = desc.texture.mask.as_ref()?;
    let mask = sprites.tile_src(set, *index)?;
    let tex = |tag: &str| desc.xml.text_of(tag).map_or(0, |v| bsk_data::xml::as3_int(v.trim()));
    Some(Dye { mask, tex1: tex("Tex1"), tex2: tex("Tex2") })
}

/// A condition effect's icon (`ConditionEffect.getIconsFromBit`): the tile at ×`scale`, ringed by
/// one hard white pixel and one dark one, so it stays sharp at any size.
fn effect_icon(src: &RgbaImage, scale: u32) -> RgbaImage {
    let (w, h) = src.dimensions();
    let pad = 2;
    let (cw, ch) = (w * scale + 2 * pad, h * scale + 2 * pad);
    let solid = |x: i64, y: i64| -> bool {
        if x < pad as i64 || y < pad as i64 {
            return false;
        }
        let (sx, sy) = ((x - pad as i64) / scale as i64, (y - pad as i64) / scale as i64);
        sx < w as i64 && sy < h as i64 && src.get_pixel(sx as u32, sy as u32)[3] > 0
    };
    RgbaImage::from_fn(cw, ch, |x, y| {
        let (x, y) = (x as i64, y as i64);
        if solid(x, y) {
            let p = src.get_pixel(((x - pad as i64) / scale as i64) as u32, ((y - pad as i64) / scale as i64) as u32);
            return *p;
        }
        let near = |r: i64| (-r..=r).any(|dy| (-r..=r).any(|dx| solid(x + dx, y + dy)));
        if near(1) {
            Rgba([255, 255, 255, 255])
        } else if near(2) {
            Rgba([0, 0, 0, 150])
        } else {
            Rgba([0, 0, 0, 0])
        }
    })
}

fn mirrored(img: &RgbaImage) -> RgbaImage {
    image::imageops::flip_horizontal(img)
}

/// A redrawn sprite: where it is in the atlas, and its canvas size in pixels.
#[derive(Debug, Clone, Copy)]
pub struct Redrawn {
    pub rect: AtlasRect,
    pub w: u32,
    pub h: u32,
}

#[derive(Default)]
pub struct Redrawer {
    cache: rustc_hash::FxHashMap<Key, Option<Redrawn>>,
    icons: rustc_hash::FxHashMap<(SrcRect, u32), Option<Redrawn>>,
    shadow: Option<AtlasRect>,
}

impl Redrawer {
    /// The shadow under objects (`GameObject.drawShadow`): a radial gradient from 50% to 0%
    /// alpha, white, to be tinted with the shadow colour.
    pub fn shadow(&mut self, renderer: &mut Renderer) -> Option<AtlasRect> {
        if self.shadow.is_none() {
            const N: u32 = 64;
            let img = RgbaImage::from_fn(N, N, |x, y| {
                let (dx, dy) = ((x as f32 + 0.5) / N as f32 * 2.0 - 1.0, (y as f32 + 0.5) / N as f32 * 2.0 - 1.0);
                let r = (dx * dx + dy * dy).sqrt();
                Rgba([255, 255, 255, (0.5 * (1.0 - r).max(0.0) * 255.0).round() as u8])
            });
            self.shadow = renderer.add_image(&img);
        }
        self.shadow
    }

    /// A condition effect's icon from `src`, at ×`scale`.
    pub fn icon(&mut self, sprites: &Sprites, renderer: &mut Renderer, src: SrcRect, scale: u32) -> Option<Redrawn> {
        *self.icons.entry((src, scale)).or_insert_with(|| {
            let image = effect_icon(&sprites.pixels(src), scale);
            let (w, h) = image.dimensions();
            renderer.add_image(&image).map(|rect| Redrawn { rect, w, h })
        })
    }

    /// `src` scaled for `size` (100 = normal), mirrored left-right if asked, dyed if given, with
    /// its outline.
    pub fn get(
        &mut self,
        sprites: &Sprites,
        renderer: &mut Renderer,
        src: SrcRect,
        mirror: bool,
        size: i32,
        dye: Option<Dye>,
    ) -> Option<Redrawn> {
        self.get_padded(sprites, renderer, src, mirror, size, dye, false)
    }

    /// Like `get`; `pad_bottom` pads below as much as above, centring the sprite (projectiles).
    #[allow(clippy::too_many_arguments)]
    pub fn get_padded(
        &mut self,
        sprites: &Sprites,
        renderer: &mut Renderer,
        src: SrcRect,
        mirror: bool,
        size: i32,
        dye: Option<Dye>,
        pad_bottom: bool,
    ) -> Option<Redrawn> {
        self.redrawn(sprites, renderer, Key { src, mirror, size, dye, pad_bottom, glow: 0 })
    }

    /// Like `get`, ringed by a glow in `glow` (0xRRGGBB, 0 for none): a player's GLOW_COLOR, red
    /// for a legend.
    #[allow(clippy::too_many_arguments)]
    pub fn get_glowing(
        &mut self,
        sprites: &Sprites,
        renderer: &mut Renderer,
        src: SrcRect,
        mirror: bool,
        size: i32,
        dye: Option<Dye>,
        glow: u32,
    ) -> Option<Redrawn> {
        self.redrawn(sprites, renderer, Key { src, mirror, size, dye, pad_bottom: false, glow })
    }

    fn redrawn(&mut self, sprites: &Sprites, renderer: &mut Renderer, key: Key) -> Option<Redrawn> {
        let Key { src, mirror, size, dye, pad_bottom, glow } = key;
        let dye = dye.filter(|d| d.tex1 != 0 || d.tex2 != 0);
        let key = Key { dye, ..key };
        *self.cache.entry(key).or_insert_with(|| {
            let mut image = match dye {
                None if pad_bottom => redraw_padded(&sprites.pixels(src), mirror, size, 5.0, PAD),
                None => redraw(&sprites.pixels(src), mirror, size, 5.0),
                Some(d) => {
                    // TextureRedrawer.resize with a mask: retexture at ×5, then size / 5.
                    let (mut pixels, mut mask) = (sprites.pixels(src), sprites.pixels(d.mask));
                    if mirror {
                        (pixels, mask) = (mirrored(&pixels), mirrored(&mask));
                    }
                    let (t1, t2) = (dye_texture(sprites, d.tex1), dye_texture(sprites, d.tex2));
                    let dyed = retexture(&pixels, &mask, t1.as_ref(), t2.as_ref());
                    redraw(&dyed, false, size / 5, 5.0)
                }
            };
            if glow != 0 {
                add_glow(&mut image, glow);
            }
            let (w, h) = image.dimensions();
            renderer.add_image(&image).map(|rect| Redrawn { rect, w, h })
        })
    }
}

/// `TextureRedrawer.resize` then `GlowRedrawer.outlineGlow(texture, 0)`.
pub fn redraw(src: &impl GenericImageView<Pixel = Rgba<u8>>, mirror: bool, size: i32, scale: f64) -> RgbaImage {
    redraw_padded(src, mirror, size, scale, 1)
}

/// `redraw` with `bottom` pixels of padding below the sprite (1 normally, `PAD` for
/// `resize`'s padBottom).
pub fn redraw_padded(src: &impl GenericImageView<Pixel = Rgba<u8>>, mirror: bool, size: i32, scale: f64, bottom: u32) -> RgbaImage {
    let (w, h) = src.dimensions();
    let factor = scale * (size as f64 / 100.0);
    let sw = (factor * w as f64) as u32;
    let sh = (factor * h as f64) as u32;
    let mut out = RgbaImage::new(sw + 2 * PAD, sh + bottom + PAD);
    if sw > 0 && sh > 0 {
        for y in 0..sh {
            // Nearest neighbour, sampling each target pixel's centre, as Flash's draw does.
            let sy = (((y as f64 + 0.5) * h as f64 / sh as f64) as u32).min(h - 1);
            for x in 0..sw {
                let sx = (((x as f64 + 0.5) * w as f64 / sw as f64) as u32).min(w - 1);
                let sx = if mirror { w - 1 - sx } else { sx };
                out.put_pixel(x + PAD, y + PAD, src.get_pixel(sx, sy));
            }
        }
    }
    outline(&mut out);
    out
}

/// The outline: `GlowFilter(black, 0.8 alpha, blur 1.4, strength 255)` saturates every pixel
/// next to a visible one (diagonals included) to 80% black, behind the sprite.
fn outline(img: &mut RgbaImage) {
    const GLOW: f32 = 0.8;
    let (w, h) = img.dimensions();
    let alpha: Vec<u8> = img.pixels().map(|p| p[3]).collect();
    let at = |x: i32, y: i32| -> u8 {
        if x < 0 || y < 0 || x >= w as i32 || y >= h as i32 { 0 } else { alpha[(y as u32 * w + x as u32) as usize] }
    };
    for y in 0..h as i32 {
        for x in 0..w as i32 {
            let mut near = 0u8;
            for dy in -1..=1 {
                for dx in -1..=1 {
                    near = near.max(at(x + dx, y + dy));
                }
            }
            if near == 0 {
                continue;
            }
            // Sprite over glow: out = src + glow * (1 - src alpha).
            let glow_a = GLOW * (near as f32 / 255.0).min(1.0).ceil();
            let p = img.get_pixel_mut(x as u32, y as u32);
            let sa = p[3] as f32 / 255.0;
            let oa = sa + glow_a * (1.0 - sa);
            if oa <= 0.0 {
                continue;
            }
            for c in 0..3 {
                // The glow is black, so only the sprite's colour remains, rescaled.
                p[c] = ((p[c] as f32 * sa) / oa).round().min(255.0) as u8;
            }
            p[3] = (oa * 255.0).round() as u8;
        }
    }
}

/// `GlowRedrawer.outlineGlow` with a glow colour, after the black outline: a hard ring of the
/// colour (`GlowFilter(colour, 0.8 alpha, blur 2, strength 255)`), then a soft halo
/// (`GlowFilter(colour, 0.3 alpha, blur 12, strength 2)`), each behind what is already drawn.
fn add_glow(img: &mut RgbaImage, color: u32) {
    let rgb = [(color >> 16) as u8, (color >> 8) as u8, color as u8];
    glow_filter(img, rgb, 0.8, 2.0, 255.0);
    glow_filter(img, rgb, 0.3, 12.0, 2.0);
}

/// Flash's `GlowFilter` at low quality (one box blur pass, `blur` pixels wide) on the outside
/// only: the blurred alpha times `strength`, capped at 1, times `alpha`, in `rgb`, behind the
/// image.
fn glow_filter(img: &mut RgbaImage, rgb: [u8; 3], alpha: f32, blur: f32, strength: f32) {
    let (w, h) = (img.width() as usize, img.height() as usize);
    let a: Vec<f32> = img.pixels().map(|p| p[3] as f32 / 255.0).collect();
    let b = box_blur(&box_blur(&a, w, h, blur, 1, w), w, h, blur, w, 1);
    for (i, p) in img.pixels_mut().enumerate() {
        // Flash works in 8 bits: a faint blur is still a pixel, and strength saturates it.
        let g = ((b[i] * 255.0).round() / 255.0 * strength).min(1.0) * alpha;
        if g <= 0.0 {
            continue;
        }
        let sa = p[3] as f32 / 255.0;
        let oa = sa + g * (1.0 - sa);
        for c in 0..3 {
            p[c] = ((p[c] as f32 * sa + rgb[c] as f32 * g * (1.0 - sa)) / oa).round().min(255.0) as u8;
        }
        p[3] = (oa * 255.0).round() as u8;
    }
}

/// A box blur `width` pixels wide along one axis (`step` apart, `stride` between lines): whole
/// pixels in the middle, a part of one at each end.
fn box_blur(a: &[f32], w: usize, h: usize, width: f32, step: usize, stride: usize) -> Vec<f32> {
    let full = ((width - 1.0) / 2.0).floor().max(0.0) as i64;
    let edge = (width - (2 * full + 1) as f32) / 2.0;
    let (len, lines) = if step == 1 { (w, h) } else { (h, w) };
    let mut out = vec![0.0; a.len()];
    for line in 0..lines {
        let at = |i: i64| -> f32 {
            if i < 0 || i >= len as i64 { 0.0 } else { a[line * stride + i as usize * step] }
        };
        for i in 0..len as i64 {
            let mut sum = (edge > 0.0).then(|| (at(i - full - 1) + at(i + full + 1)) * edge).unwrap_or(0.0);
            for d in -full..=full {
                sum += at(i + d);
            }
            out[line * stride + i as usize * step] = sum / width;
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn canvas_and_outline() {
        // A single opaque red pixel in an 8×8 sprite at size 100: 40×40 scaled, on a
        // (40 + 24) × (40 + 13) canvas, with the red block at (12, 12) and black around it.
        let mut src = RgbaImage::new(8, 8);
        src.put_pixel(0, 0, Rgba([255, 0, 0, 255]));
        let out = redraw(&src, false, 100, 5.0);
        assert_eq!(out.dimensions(), (64, 53));
        assert_eq!(*out.get_pixel(12, 12), Rgba([255, 0, 0, 255]));
        assert_eq!(*out.get_pixel(16, 16), Rgba([255, 0, 0, 255]));
        assert_eq!(out.get_pixel(17, 12)[3], 204, "outline right of the block");
        assert_eq!(out.get_pixel(11, 11)[3], 204, "outline diagonal");
        assert_eq!(out.get_pixel(18, 12)[3], 0, "only one pixel wide");
        // Mirrored, the block moves to the right edge of the sprite.
        let out = redraw(&src, true, 100, 5.0);
        assert_eq!(*out.get_pixel(12 + 39, 12), Rgba([255, 0, 0, 255]));
        // Size 50 halves it.
        assert_eq!(redraw(&src, false, 50, 5.0).dimensions(), (20 + 24, 20 + 13));
    }

    #[test]
    fn glow_rings_the_outline() {
        // The red block from canvas_and_outline: black outline at x = 17, then a hard 80% ring
        // of the glow colour at x = 18, fading to nothing a few pixels further out.
        let mut src = RgbaImage::new(8, 8);
        src.put_pixel(0, 0, Rgba([255, 0, 0, 255]));
        let mut out = redraw(&src, false, 100, 5.0);
        add_glow(&mut out, 0x00FF00);
        assert_eq!(*out.get_pixel(14, 14), Rgba([255, 0, 0, 255]), "sprite untouched");
        let outline = out.get_pixel(17, 14);
        assert!(outline[1] < 60 && outline[3] > 204, "outline stays dark: {outline:?}");
        let ring = out.get_pixel(18, 14);
        assert!(ring[1] == 255 && ring[0] == 0 && ring[3] >= 204, "ring: {ring:?}");
        let halo = out.get_pixel(21, 14);
        assert!(halo[1] == 255 && halo[3] > 0 && halo[3] < ring[3], "halo: {halo:?}");
        assert_eq!(out.get_pixel(30, 14)[3], 0, "nothing far out");
    }

    #[test]
    fn dyes_follow_the_mask() {
        // Two grey pixels; the mask marks the first red (full) and the second green (half).
        let mut src = RgbaImage::from_pixel(2, 1, Rgba([128, 128, 128, 255]));
        src.put_pixel(1, 0, Rgba([128, 128, 128, 200]));
        let mut mask = RgbaImage::new(2, 1);
        mask.put_pixel(0, 0, Rgba([255, 0, 0, 255]));
        mask.put_pixel(1, 0, Rgba([0, 128, 0, 255]));
        let red = RgbaImage::from_pixel(1, 1, Rgba([255, 0, 0, 255]));
        let blue = RgbaImage::from_pixel(1, 1, Rgba([0, 0, 255, 255]));
        let out = retexture(&src, &mask, Some(&red), Some(&blue));
        assert_eq!(out.dimensions(), (10, 5));
        assert_eq!(*out.get_pixel(2, 2), Rgba([255, 0, 0, 255]));
        assert_eq!(*out.get_pixel(7, 2), Rgba([0, 0, 128, 200]), "half-strength blue, alpha kept");
        // Without a second dye, the green part keeps its colour.
        assert_eq!(*retexture(&src, &mask, Some(&red), None).get_pixel(7, 2), Rgba([128, 128, 128, 200]));
    }
}
