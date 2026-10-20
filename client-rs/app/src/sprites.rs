//! Sprite sheets, looked up the way the AIR client's `AssetLibrary` and `AnimatedChars` do: by
//! set name and tile index. Each sheet is in the GPU atlas (for drawing as is, like ground
//! tiles) and kept in memory (for redrawing, like outlined object sprites).

use bsk_assets::{AssetSource, Facing, Manifest};
use bsk_render::{AtlasRect, Renderer};
use std::collections::HashMap;

/// A rectangle of one sheet, in its pixels.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct SrcRect {
    pub sheet: u16,
    pub x: u16,
    pub y: u16,
    pub w: u16,
    pub h: u16,
}

/// One sheet file. A sheet bigger than an atlas page is stored as a grid of pieces, cut on
/// column and row boundaries that every tile size registered for the file shares.
pub struct Sheet {
    pub image: image::RgbaImage,
    piece_w: u32,
    piece_h: u32,
    columns: u32,
    /// Row by row.
    pieces: Vec<AtlasRect>,
}

impl Sheet {
    /// The atlas rectangle for a pixel rectangle of the sheet, or `None` if it's outside the
    /// sheet or crosses a piece edge.
    pub fn rect(&self, x: u32, y: u32, w: u32, h: u32) -> Option<AtlasRect> {
        if x + w > self.image.width() || y + h > self.image.height() {
            return None;
        }
        let (px, py) = (x / self.piece_w, y / self.piece_h);
        let piece = self.pieces.get((py * self.columns + px) as usize)?;
        let (left, top) = (x - px * self.piece_w, y - py * self.piece_h);
        (left + w <= piece.w as u32 && top + h <= piece.h as u32).then(|| piece.sub(left, top, w, h))
    }
}

pub struct ImageSet {
    sheet: Option<usize>,
    pub tile_w: u32,
    pub tile_h: u32,
    cols: u32,
    pub count: u32,
}

pub struct CharSheet {
    sheet: usize,
    mask: Option<usize>,
    pub tile_w: u32,
    pub tile_h: u32,
    pub block_w: u32,
    pub block_h: u32,
    blocks_per_row: u32,
    pub count: u32,
    pub facing: Facing,
}

impl CharSheet {
    /// Rows of 7 frames per character: 1 (right only), 2 (+ down) or 3 (+ up).
    pub fn rows(&self) -> u32 {
        (self.block_h / self.tile_h.max(1)).max(1)
    }
}

#[derive(Default)]
pub struct Sprites {
    sheets: Vec<Sheet>,
    sets: rustc_hash::FxHashMap<String, ImageSet>,
    chars: rustc_hash::FxHashMap<String, CharSheet>,
    /// Emotes by the text that turns into them.
    pub emotes: HashMap<String, bsk_assets::EmoteDef>,
}

impl Sprites {
    /// Tile `index` of image set `set`.
    pub fn tile_src(&self, set: &str, index: u32) -> Option<SrcRect> {
        let s = self.sets.get(set)?;
        if index >= s.count {
            return None;
        }
        Some(src(s.sheet?, (index % s.cols) * s.tile_w, (index / s.cols) * s.tile_h, s.tile_w, s.tile_h))
    }

    /// Frame `frame` (0..7) of row `row` of character `index` in an animated sheet. A sheet with
    /// fewer frames (a single picture, like some Doomed Realms pets) walks in its standing frame
    /// and has no attack frames.
    pub fn char_src(&self, name: &str, index: u32, row: u32, frame: u32) -> Option<SrcRect> {
        let c = self.chars.get(name)?;
        if index >= c.count || row >= c.rows() || frame >= 7 {
            return None;
        }
        let frame = if (frame + 1) * c.tile_w <= c.block_w {
            frame
        } else if frame < 4 {
            0
        } else {
            return None;
        };
        let bx = (index % c.blocks_per_row) * c.block_w;
        let by = (index / c.blocks_per_row) * c.block_h;
        Some(src(c.sheet, bx + frame * c.tile_w, by + row * c.tile_h, c.tile_w, c.tile_h))
    }

    /// The dye mask for a character frame, if the sheet has one.
    pub fn char_mask_src(&self, name: &str, index: u32, row: u32, frame: u32) -> Option<SrcRect> {
        let c = self.chars.get(name)?;
        let mut r = self.char_src(name, index, row, frame)?;
        r.sheet = c.mask? as u16;
        Some(r)
    }

    pub fn char_sheet(&self, name: &str) -> Option<&CharSheet> {
        self.chars.get(name)
    }

    pub fn set(&self, name: &str) -> Option<&ImageSet> {
        self.sets.get(name)
    }

    /// Where a source rectangle is in the atlas.
    pub fn atlas(&self, r: SrcRect) -> Option<AtlasRect> {
        self.sheets.get(r.sheet as usize)?.rect(r.x as u32, r.y as u32, r.w as u32, r.h as u32)
    }

    pub fn tile(&self, set: &str, index: u32) -> Option<AtlasRect> {
        self.atlas(self.tile_src(set, index)?)
    }

    /// A copy of the pixels of a source rectangle.
    pub fn pixels(&self, r: SrcRect) -> image::RgbaImage {
        image::imageops::crop_imm(&self.sheets[r.sheet as usize].image, r.x as u32, r.y as u32, r.w as u32, r.h as u32)
            .to_image()
    }

    /// `MaskedImage.amountTransparent() == 1`: nothing visible.
    pub fn is_blank(&self, r: SrcRect) -> bool {
        let img = &self.sheets[r.sheet as usize].image;
        (r.y..r.y + r.h).all(|y| (r.x..r.x + r.w).all(|x| img.get_pixel(x as u32, y as u32)[3] == 0))
    }

    /// Fetch every sheet in the manifest (all at once, which matters in a browser) and put it in
    /// the atlas.
    pub async fn load(source: &AssetSource, manifest: &Manifest, renderer: &mut Renderer) -> Result<Self, String> {
        let files = manifest.sheet_files();
        let t = web_time::Instant::now();
        let fetched = futures::future::join_all(files.iter().map(|f| source.load(f))).await;
        let fetch_ms = t.elapsed().as_millis();
        let (mut decode, mut upload) = (std::time::Duration::ZERO, std::time::Duration::ZERO);

        // Each file is cut into pieces whose sides are multiples of every tile and block size
        // registered for it, so no tile straddles two pieces.
        let mut sizes: Vec<(&str, u32, u32)> = Vec::new();
        for s in &manifest.image_sets {
            if let Some(f) = &s.file {
                sizes.push((f, s.w, s.h));
            }
        }
        for c in &manifest.animated_chars {
            for f in std::iter::once(&c.file).chain(&c.mask) {
                sizes.extend([(f.as_str(), c.sheet_w, c.sheet_h), (f.as_str(), c.w, c.h)]);
            }
        }
        let mut unit: HashMap<&str, (u32, u32)> = HashMap::new();
        for (file, w, h) in sizes {
            let u = unit.entry(file).or_insert((1, 1));
            *u = (lcm(u.0, w.max(1)), lcm(u.1, h.max(1)));
        }

        let mut sprites = Sprites::default();
        let mut index_of: HashMap<&str, usize> = HashMap::new();
        let max = renderer.max_image_size();
        for (file, bytes) in files.iter().zip(fetched) {
            let bytes = bytes.map_err(|e| e.to_string())?;
            let t = web_time::Instant::now();
            let image = bsk_assets::decode_image(file, &bytes).map_err(|e| e.to_string())?;
            decode += t.elapsed();
            let t = web_time::Instant::now();
            let (width, height) = image.dimensions();
            let (uw, uh) = unit[file];
            if uw > max || uh > max {
                return Err(format!("{file}: a {uw}×{uh} tile doesn't fit an atlas page"));
            }
            let (piece_w, piece_h) = ((max / uw) * uw, (max / uh) * uh);
            let columns = width.div_ceil(piece_w);
            let mut pieces = Vec::new();
            for y in (0..height).step_by(piece_h as usize) {
                for x in (0..width).step_by(piece_w as usize) {
                    let (w, h) = (piece_w.min(width - x), piece_h.min(height - y));
                    let piece = image::imageops::crop_imm(&image, x, y, w, h).to_image();
                    pieces.push(renderer.add_image(&piece).ok_or_else(|| format!("{file}: atlas full"))?);
                }
            }
            upload += t.elapsed();
            index_of.insert(file, sprites.sheets.len());
            sprites.sheets.push(Sheet { image, piece_w, piece_h, columns, pieces });
        }
        log::info!("sheets: fetch {fetch_ms} ms, decode {} ms, atlas {} ms", decode.as_millis(), upload.as_millis());

        for s in &manifest.image_sets {
            let sheet = s.file.as_deref().map(|f| index_of[f]);
            let (cols, count) = match sheet {
                Some(i) => {
                    let img = &sprites.sheets[i].image;
                    let cols = img.width() / s.w;
                    (cols, cols * (img.height() / s.h))
                }
                None => (1, 1),
            };
            sprites.sets.insert(s.name.clone(), ImageSet { sheet, tile_w: s.w, tile_h: s.h, cols, count });
        }
        for c in &manifest.animated_chars {
            let sheet = index_of[c.file.as_str()];
            let img = &sprites.sheets[sheet].image;
            let blocks_per_row = (img.width() / c.sheet_w).max(1);
            let count = blocks_per_row * (img.height() / c.sheet_h);
            sprites.chars.insert(
                c.name.clone(),
                CharSheet {
                    sheet,
                    mask: c.mask.as_deref().map(|m| index_of[m]),
                    tile_w: c.w,
                    tile_h: c.h,
                    block_w: c.sheet_w,
                    block_h: c.sheet_h,
                    blocks_per_row,
                    count,
                    facing: c.dir,
                },
            );
        }
        sprites.emotes = manifest.emotes.iter().map(|e| (e.code.clone(), e.clone())).collect();
        Ok(sprites)
    }
}

fn src(sheet: usize, x: u32, y: u32, w: u32, h: u32) -> SrcRect {
    SrcRect { sheet: sheet as u16, x: x as u16, y: y as u16, w: w as u16, h: h as u16 }
}

fn gcd(a: u32, b: u32) -> u32 {
    if b == 0 { a } else { gcd(b, a % b) }
}

fn lcm(a: u32, b: u32) -> u32 {
    a / gcd(a, b) * b
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rect_finds_the_piece_holding_a_tile() {
        // A 300×250 sheet cut into 128×96 pieces: 3 columns, 3 rows.
        let mut pieces = Vec::new();
        for row in 0..3u16 {
            for col in 0..3u16 {
                let w = if col == 2 { 44 } else { 128 };
                let h = if row == 2 { 58 } else { 96 };
                pieces.push(AtlasRect { page: row * 3 + col, x: 100, y: 200, w, h });
            }
        }
        let image = image::RgbaImage::new(300, 250);
        let sheet = Sheet { image, piece_w: 128, piece_h: 96, columns: 3, pieces };
        assert_eq!(sheet.rect(0, 0, 16, 16), Some(AtlasRect { page: 0, x: 100, y: 200, w: 16, h: 16 }));
        assert_eq!(sheet.rect(144, 112, 16, 16), Some(AtlasRect { page: 4, x: 116, y: 216, w: 16, h: 16 }));
        assert_eq!(sheet.rect(256, 192, 32, 32), Some(AtlasRect { page: 8, x: 100, y: 200, w: 32, h: 32 }));
        assert_eq!(sheet.rect(120, 0, 16, 16), None, "crosses a column edge");
        assert_eq!(sheet.rect(0, 240, 16, 16), None, "outside the sheet");
    }

    #[test]
    fn lcm_of_tile_sizes() {
        assert_eq!(lcm(8, 16), 16);
        assert_eq!(lcm(16, 24), 48);
        assert_eq!(lcm(40, 8), 40);
    }
}
