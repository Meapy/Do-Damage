//! Ground tiles (`GroundProperties.as`, `GroundLibrary.as`).

use crate::object::display_name;
use crate::texture::TextureData;
use crate::xml::Element;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum AnimateKind {
    #[default]
    None,
    Wave,
    Flow,
}

#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct Animate {
    pub kind: AnimateKind,
    pub dx: f64,
    pub dy: f64,
}

impl Animate {
    fn from_xml(xml: &Element) -> Self {
        let kind = match xml.text.as_str() {
            "Wave" => AnimateKind::Wave,
            "Flow" => AnimateKind::Flow,
            _ => AnimateKind::None,
        };
        Animate { kind, dx: xml.attr_number("dx", 0.0), dy: xml.attr_number("dy", 0.0) }
    }
}

#[derive(Debug, Clone)]
pub struct Ground {
    pub tile_type: u16,
    pub id: String,
    pub display_id: String,
    pub texture: TextureData,
    pub no_walk: bool,
    pub min_damage: i32,
    pub max_damage: i32,
    pub animate: Animate,
    pub blend_priority: i32,
    pub composite_priority: i32,
    pub speed: f64,
    pub x_offset: f64,
    pub y_offset: f64,
    pub slide_amount: f64,
    pub push: bool,
    pub sink: bool,
    pub sinking: bool,
    /// `<Lift>`: how high, in tiles, this ground lifts what stands on it (the Nexus garden's
    /// arched bridges: the deck is drawn by a model, the players walk up and over it).
    pub lift: f64,
    pub random_offset: bool,
    pub edge: Option<TextureData>,
    pub corner: Option<TextureData>,
    pub inner_corner: Option<TextureData>,
    pub same_type_edge_mode: bool,
    pub top: Option<(TextureData, Animate)>,
    /// `<Color>`: the minimap colour, if given (otherwise the texture's most common colour).
    pub color: Option<u32>,
    pub xml: Element,
}

impl Ground {
    pub fn from_xml(xml: &Element) -> Self {
        let tex = |name: &str| xml.child(name).map(TextureData::from_xml);
        let edge = tex("Edge");
        Ground {
            tile_type: xml.attr_int("type", 0) as u16,
            id: xml.attr("id").unwrap_or("").to_owned(),
            display_id: display_name(xml.text_of("DisplayId"), xml.attr("id")),
            texture: TextureData::from_xml(xml),
            no_walk: xml.has("NoWalk"),
            min_damage: xml.int("MinDamage", 0),
            max_damage: xml.int("MaxDamage", 0),
            animate: xml.child("Animate").map(Animate::from_xml).unwrap_or_default(),
            blend_priority: xml.int("BlendPriority", -1),
            composite_priority: xml.int("CompositePriority", 0),
            speed: xml.number("Speed", 1.0),
            x_offset: xml.number("XOffset", 0.0),
            y_offset: xml.number("YOffset", 0.0),
            slide_amount: xml.number("SlideAmount", 0.0),
            push: xml.has("Push"),
            sink: xml.has("Sink"),
            sinking: xml.has("Sinking"),
            lift: xml.number("Lift", 0.0),
            random_offset: xml.has("RandomOffset"),
            corner: edge.as_ref().and_then(|_| tex("Corner")),
            inner_corner: edge.as_ref().and_then(|_| tex("InnerCorner")),
            edge,
            same_type_edge_mode: xml.has("SameTypeEdgeMode"),
            top: xml.child("Top").map(|t| {
                (TextureData::from_xml(t), xml.child("TopAnimate").map(Animate::from_xml).unwrap_or_default())
            }),
            color: xml.child("Color").map(|c| crate::xml::as3_int(&c.text) as u32),
            xml: xml.clone(),
        }
    }
}

const LOOKUP: [i64; 7] = [26171, 44789, 20333, 70429, 98257, 59393, 33961];

/// `Square.hash`: which random texture a tile at (x, y) shows. AS3 multiplies as a double, so
/// the product doesn't wrap.
pub fn tile_hash(x: i32, y: i32) -> i32 {
    let l = LOOKUP[((x + y).rem_euclid(7)) as usize];
    let val = ((x << 16) | y) ^ 81397550;
    ((val as i64 * l) % 65535) as i32
}

#[cfg(test)]
mod tests {
    #[test]
    fn tile_hash_matches_as3() {
        // (0,0): (0 ^ 81397550) * 26171 % 65535, computed in doubles.
        assert_eq!(super::tile_hash(0, 0), ((81397550i64 * 26171) % 65535) as i32);
        // (1,2): LOOKUP[3]; (3,4): 3 + 4 wraps to LOOKUP[0].
        assert_eq!(super::tile_hash(1, 2), ((((1 << 16) | 2) ^ 81397550) as i64 * 70429 % 65535) as i32);
        assert_eq!(super::tile_hash(3, 4), ((((3 << 16) | 4) ^ 81397550) as i64 * 26171 % 65535) as i32);
    }
}
