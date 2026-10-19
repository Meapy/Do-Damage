//! The HUD (`HUDView`) and chat (`TextBox`), laid out for whatever size the window is.
//!
//! The AIR client's HUD is a 200×600 panel beside a 600×600 view. Here the panel keeps its
//! look, with room for stats, potions and the interact area (bags, portals) below the
//! inventory, scaled by one factor (`Layout::s`) chosen from the window's width and height. It
//! sits at the right edge and the world fills the rest. Everything is worked out each frame
//! from the current size, so resizing or going fullscreen just works.
//!
//! Items move the AIR client's way (`ItemGridMediator`): drag between slots to swap, onto the
//! world to drop, double-click to equip or use (or take from a bag), shift-click to use.

use crate::redraw::{Dye, Redrawer};
use crate::sprites::{SrcRect, Sprites};
use crate::theme::{self, color, Kind};
use crate::text::{Fonts, Style, Text};
use bsk_data::{GameData, ObjectClass, ObjectDesc, TextureData, TextureRef};
use bsk_render::{Quad, Renderer, Tint};
use bsk_world::{GameObj, World};
use std::collections::HashMap;

/// Where things go for one window size.
#[derive(Clone, Copy, Debug)]
pub struct Layout {
    pub w: f32,
    pub h: f32,
    /// HUD units (the AIR client's pixels) to screen pixels.
    pub s: f32,
    /// The panel's left edge; the world is drawn left of it.
    pub panel_x: f32,
}

/// The panel's design size.
const PANEL_W: f32 = 200.0;
const PANEL_H: f32 = 700.0;

impl Layout {
    pub fn new(w: u32, h: u32) -> Self {
        let (w, h) = (w.max(1) as f32, h.max(1) as f32);
        // The whole panel fits the height, and takes at most 28% of the width.
        let s = (h / PANEL_H).min(w * 0.28 / PANEL_W).clamp(0.45, 4.0);
        Layout { w, h, s, panel_x: (w - PANEL_W * s).round() }
    }

    /// Width of the world's part of the window.
    pub fn view_w(&self) -> f32 {
        self.panel_x
    }

    /// Pixels per tile: the AIR client's 50, so a bigger window shows more of the world (as
    /// the AIR client does), times the player's zoom; only very tall windows (over 1440
    /// pixels, like 4K) draw it bigger, so it doesn't turn tiny.
    pub fn map_scale(&self, zoom: f32) -> f64 {
        50.0 * zoom as f64 * (self.h as f64 / 1440.0).max(1.0)
    }

    /// Where the player stands (`Camera`'s screen rects): centred across the view, a little
    /// below the middle (`centered`) or three quarters of the way down,
    /// on a whole pixel so we're drawn on the same pixels every frame (`view::draw_object`).
    pub fn camera_point(&self, centered: bool) -> (f64, f64) {
        let down = if centered { 13.0 / 24.0 } else { 3.0 / 4.0 };
        ((self.view_w() as f64 / 2.0).round(), (self.h as f64 * down).round())
    }

    /// A point on the panel, from HUD units.
    fn at(&self, x: f32, y: f32) -> (f32, f32) {
        (self.panel_x + x * self.s, y * self.s)
    }

    /// A rectangle on the panel (x, y, w, h), from HUD units.
    fn rect(&self, x: f32, y: f32, w: f32, h: f32) -> [f32; 4] {
        let (px, py) = self.at(x, y);
        [px, py, w * self.s, h * self.s]
    }

    /// A font size in HUD units, on the screen (never unreadably small).
    pub fn font(&self, size: f32) -> f32 {
        (size * self.s).round().max(9.0)
    }

    /// Whether a screen point is on the panel.
    pub fn on_panel(&self, x: f64, y: f64) -> bool {
        x as f32 >= self.panel_x && (y as f32) < self.h
    }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// What drawing the HUD needs.
pub struct Ui<'a> {
    pub renderer: &'a mut Renderer,
    pub sprites: &'a Sprites,
    pub data: &'a GameData,
    pub redrawer: &'a mut Redrawer,
    pub fonts: &'a Fonts,
    pub text: &'a mut Text,
}

impl Ui<'_> {
    pub(crate) fn rect(&mut self, x: f32, y: f32, w: f32, h: f32, rgb: u32, alpha: f32) {
        let white = self.renderer.white();
        self.renderer.sprite(white, Quad::rect(x, y, w, h), Tint::color(rgb, alpha));
    }



    /// Interface text: crisp, with a soft shadow rather than the world's heavy glow.
    pub(crate) fn label(&mut self, s: &str, x: f32, y: f32, size: f32, bold: bool, color: u32) {
        self.text.draw_ui(self.fonts, self.renderer, s, x, y, Style { size, bold, color });
    }

    /// See `Text::digit_middle`.
    pub(crate) fn digit_middle(&self, size: f32, bold: bool) -> f32 {
        self.text.digit_middle(self.fonts, size, bold)
    }

    pub(crate) fn measure(&mut self, s: &str, size: f32, bold: bool) -> f32 {
        self.text.measure(self.fonts, self.renderer, s, Style { size, bold, color: 0 })
    }

    /// Like `measure`, with emotes as pictures.
    fn measure_rich(&mut self, s: &str, size: f32) -> f32 {
        crate::emotes::measure(self.sprites, self.fonts, self.text, self.renderer, s, Style { size, bold: false, color: 0 })
    }

    /// Like `label`, with emotes as pictures.
    fn label_rich(&mut self, s: &str, x: f32, y: f32, size: f32, color: u32, now: i32) {
        crate::emotes::draw(self.sprites, self.fonts, self.text, self.renderer, s, x, y, Style { size, bold: false, color }, now);
    }

    fn src(&self, tex: &TextureData) -> Option<SrcRect> {
        match &tex.pick(0).main {
            TextureRef::Image { set, index } => self.sprites.tile_src(set, *index),
            TextureRef::Animated { set, index } => self.sprites.char_src(set, *index, 0, 0),
            _ => None,
        }
    }

    /// `getRedrawnTextureFromType(type, size, true)`, centred on (cx, cy) at `scale`.
    ///
    /// Redrawn at the size it shows (so pixel art stays sharp at any scale), and centred on
    /// the sprite itself: the canvas has room for the outline above and beside the sprite but
    /// hardly any below.
    pub(crate) fn item_icon(&mut self, item: i32, size: i32, cx: f32, cy: f32, scale: f32) {
        let Some(desc) = self.data.objects.get(&(item as u16)) else { return };
        let Some(src) = self.src(&desc.texture) else { return };
        let size = if src.h == 16 { size / 2 } else { size };
        let size = ((size as f32 * scale).round() as i32).max(1);
        let dye = crate::redraw::item_dye(self.sprites, desc);
        let Some(icon) = self.redrawer.get(self.sprites, self.renderer, src, false, size, dye) else { return };
        let (w, h) = (icon.w as f32, icon.h as f32);
        let lift = (crate::redraw::PAD as f32 - 1.0) / 2.0;
        self.renderer.sprite(icon.rect, Quad::rect((cx - w / 2.0).round(), (cy - h / 2.0 - lift).round(), w, h), Tint::NONE);
    }

    /// A sheet's tile redrawn with its outline at `size` (100 = normal), centred on (cx, cy):
    /// padded below as much as above, so the tile itself is in the middle.
    pub(crate) fn tile_icon(&mut self, set: &str, index: u32, size: i32, cx: f32, cy: f32, scale: f32) {
        let Some(src) = self.sprites.tile_src(set, index) else { return };
        let size = if src.h == 16 { size / 2 } else { size };
        let size = ((size as f32 * scale).round() as i32).max(1);
        let Some(icon) = self.redrawer.get_padded(self.sprites, self.renderer, src, false, size, None, true) else { return };
        let (w, h) = (icon.w as f32, icon.h as f32);
        self.renderer.sprite(icon.rect, Quad::rect((cx - w / 2.0).round(), (cy - h / 2.0).round(), w, h), Tint::NONE);
    }

    /// A tile drawn as one flat colour (`greyColorFilter`), keeping its shape.
    fn silhouette(&mut self, set: &str, index: u32, x: f32, y: f32, side: f32, rgb: u32) {
        if let Some(rect) = self.sprites.tile(set, index) {
            let [r, g, b] = bsk_render::rgb_f32(rgb);
            // Colour multiplied to zero, then the flat colour added: the shape in one colour.
            self.renderer.sprite(rect, Quad::rect(x, y, side, side), Tint { mul: [0.0, 0.0, 0.0, 1.0], add: [r, g, b] });
        }
    }
}

/// `GraphicsUtil.drawCutEdgeRect`: a rectangle with the chosen corners (top left, top right,
/// bottom right, bottom left) cut off at 45°.
pub fn cut_rect(renderer: &mut Renderer, r: [f32; 4], cut: f32, cuts: [bool; 4], rgb: u32, alpha: f32) {
    let [x, y, w, h] = r;
    let mut pts: Vec<[f32; 2]> = Vec::with_capacity(8);
    let corners = [
        ([x, y + cut], [x + cut, y], [x, y]),
        ([x + w - cut, y], [x + w, y + cut], [x + w, y]),
        ([x + w, y + h - cut], [x + w - cut, y + h], [x + w, y + h]),
        ([x + cut, y + h], [x, y + h - cut], [x, y + h]),
    ];
    for (i, (a, b, c)) in corners.into_iter().enumerate() {
        if cuts[i] {
            pts.push(a);
            pts.push(b);
        } else {
            pts.push(c);
        }
    }
    let white = renderer.white();
    let tint = Tint::color(rgb, alpha);
    for k in 1..pts.len() - 1 {
        renderer.triangle(white, [pts[0], pts[k], pts[k + 1]], [[0.5, 0.5]; 3], tint);
    }
}

/// Minimap colours, worked out once per type (`GroundLibrary.getColor`,
/// `GameObject.getColor`: the `<Color>`, else the texture's most common colour).
#[derive(Default)]
pub struct MapColors {
    tiles: HashMap<u16, u32>,
    /// `tiles` by tile type (u32::MAX: not worked out yet).
    tile_fast: Vec<u32>,
    objects: HashMap<u16, u32>,
}

fn most_common_color(img: &image::RgbaImage) -> u32 {
    let mut counts: HashMap<u32, u32> = HashMap::new();
    for p in img.pixels() {
        if p[3] != 0 {
            *counts.entry(u32::from_be_bytes([p[3], p[0], p[1], p[2]])).or_default() += 1;
        }
    }
    // Ties go to the larger ARGB value, as BitmapUtil.mostCommonColor does.
    counts.into_iter().max_by_key(|&(c, n)| (n, c)).map_or(0, |(c, _)| c & 0xFF_FFFF)
}

impl MapColors {
    fn tile(&mut self, ui: &Ui, tile: u16) -> u32 {
        // Looked up for every minimap cell, every frame: by index once known.
        if self.tile_fast.is_empty() {
            self.tile_fast = vec![u32::MAX; 1 << 16];
        }
        let fast = self.tile_fast[tile as usize];
        if fast != u32::MAX {
            return fast;
        }
        let c = *self.tiles.entry(tile).or_insert_with(|| {
            let Some(g) = ui.data.grounds.get(&tile) else { return 0 };
            g.color.unwrap_or_else(|| ui.src(&g.texture).map_or(0, |r| most_common_color(&ui.sprites.pixels(r))))
        });
        self.tile_fast[tile as usize] = c & 0xFF_FFFF;
        c
    }

    fn object(&mut self, ui: &Ui, object_type: u16) -> u32 {
        *self.objects.entry(object_type).or_insert_with(|| {
            let Some(d) = ui.data.objects.get(&object_type) else { return 0 };
            // Walls show their top (Wall.getColor).
            let tex = if d.class.is_wall() { d.top.as_ref().unwrap_or(&d.texture) } else { &d.texture };
            d.color.unwrap_or_else(|| ui.src(tex).map_or(0, |r| most_common_color(&ui.sprites.pixels(r))))
        })
    }
}

/// Stat ids (`StatData`).
pub mod stat {
    pub const MAX_HP: u8 = 0;
    pub const HP: u8 = 1;
    /// What items add to the maximum HP and MP.
    pub const MAX_HP_BOOST: u8 = 46;
    pub const MAX_MP_BOOST: u8 = 47;
    pub const MAX_MP: u8 = 3;
    pub const MP: u8 = 4;
    pub const NEXT_LEVEL_EXP: u8 = 5;
    pub const EXP: u8 = 6;
    pub const LEVEL: u8 = 7;
    pub const INVENTORY_0: u8 = 8;
    pub const ATTACK: u8 = 20;
    pub const DEFENSE: u8 = 21;
    pub const SPEED: u8 = 22;
    pub const VITALITY: u8 = 26;
    pub const WISDOM: u8 = 27;
    pub const DEXTERITY: u8 = 28;
    pub const TEX1: u8 = 32;
    pub const TEX2: u8 = 33;
    pub const ATTACK_BOOST: u8 = 48;
    pub const DEFENSE_BOOST: u8 = 49;
    pub const SPEED_BOOST: u8 = 50;
    pub const VITALITY_BOOST: u8 = 51;
    pub const WISDOM_BOOST: u8 = 52;
    pub const DEXTERITY_BOOST: u8 = 53;
    pub const CURR_FAME: u8 = 57;
    pub const NEXT_CLASS_QUEST_FAME: u8 = 58;
    pub const HEALTH_POTION_STACK: u8 = 65;
    pub const MAGIC_POTION_STACK: u8 = 66;
    pub const BACKPACK_0: u8 = 67;
    pub const HAS_BACKPACK: u8 = 75;
    pub const TEXTURE: u8 = 76;
}

/// The stat holding inventory slot `slot` (0-11 the inventory, 12-19 the backpack).
pub fn slot_stat(slot: u8) -> u8 {
    if slot < 12 { stat::INVENTORY_0 + slot } else { stat::BACKPACK_0 + slot - 12 }
}

/// The health and magic potions kept in stacks (`PotionInventoryModel`): item, slot id.
pub const POTIONS: [(i32, u8); 2] = [(2594, 254), (2595, 255)];

/// The most potions a stack holds.
pub const MAX_STACKED: i32 = 6;

/// The minimap's ground where nothing is known.
const MINIMAP_FILL: u32 = 0x1B1B1B;

/// What a slot holds.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SlotKind {
    /// One of the four equipment slots, with the class's slot type.
    Equip(i32),
    /// Inventory or backpack, with its number key (1-8).
    Inventory(u8),
    /// A bag's.
    Container,
    /// A potion stack (F, V), holding `count`.
    Potion(i32),
    /// For sale (tooltips only): no hints about using it.
    Shop,
}

/// One slot on the panel.
#[derive(Clone, Copy, Debug)]
pub struct Slot {
    pub owner: i32,
    pub index: u8,
    pub item: i32,
    pub kind: SlotKind,
    rect: [f32; 4],
}

impl Slot {
    pub fn is(&self, other: &Slot) -> bool {
        self.owner == other.owner && self.index == other.index
    }
}

/// `Slot` types (`SlotType` in item XML).
pub fn slot_type_name(t: i32) -> &'static str {
    match t {
        0 => "Any",
        1 => "Sword",
        2 => "Dagger",
        3 => "Bow",
        4 => "Tome",
        5 => "Shield",
        6 => "Leather Armor",
        7 => "Armor",
        8 => "Wand",
        9 => "Accessory",
        10 => "Potion",
        11 => "Spell",
        12 => "Holy Seal",
        13 => "Cloak",
        14 => "Robe",
        15 => "Quiver",
        16 => "Helm",
        17 => "Staff",
        18 => "Poison",
        19 => "Skull",
        20 => "Trap",
        21 => "Orb",
        22 => "Prism",
        23 => "Scepter",
        24 => "Shuriken",
        _ => "Item",
    }
}

/// `Slot.drawBackground`: the silhouette an empty equipment slot shows.
fn slot_type_icon(t: i32) -> Option<(&'static str, u32)> {
    Some(match t {
        1 => ("lofiObj5", 48),
        2 => ("lofiObj5", 96),
        3 => ("lofiObj5", 80),
        4 => ("lofiObj6", 80),
        5 => ("lofiObj6", 112),
        6 => ("lofiObj5", 0),
        7 => ("lofiObj5", 32),
        8 => ("lofiObj5", 64),
        9 => ("lofiObj", 44),
        11 => ("lofiObj6", 64),
        12 => ("lofiObj6", 160),
        13 => ("lofiObj6", 32),
        14 => ("lofiObj5", 16),
        15 => ("lofiObj6", 48),
        16 => ("lofiObj6", 96),
        17 => ("lofiObj5", 112),
        18 => ("lofiObj6", 128),
        19 => ("lofiObj6", 0),
        20 => ("lofiObj6", 16),
        21 => ("lofiObj6", 144),
        22 => ("lofiObj6", 176),
        23 => ("lofiObj6", 192),
        _ => return None,
    })
}

/// The class's `<SlotTypes>` (the first four are the equipment slots).
pub fn class_slot_types(class: Option<&ObjectDesc>) -> Vec<i32> {
    class
        .and_then(|c| c.xml.text_of("SlotTypes"))
        .map(|s| s.split(',').map(|t| t.trim().parse().unwrap_or(0)).collect())
        .unwrap_or_default()
}

/// An item's `<SlotType>`.
pub fn item_slot_type(data: &GameData, item: i32) -> i32 {
    data.objects.get(&(item as u16)).map_or(0, |d| d.xml.int("SlotType", 0))
}

/// Whether a slot of kind `kind` can take `item` (`canHoldItem`): equipment only its type.
pub fn can_hold(data: &GameData, kind: SlotKind, item: i32) -> bool {
    match kind {
        _ if item <= 0 => true,
        SlotKind::Equip(t) => t == 0 || item_slot_type(data, item) == t,
        SlotKind::Potion(_) | SlotKind::Shop => false,
        _ => true,
    }
}

/// The inventory page showing.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Page {
    #[default]
    Inventory,
    Backpack,
}

/// What can be clicked that isn't a slot.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Button {
    Nexus,
    Page(Page),
    /// Enter the portal.
    Enter(i32),
    /// Buy from the shop.
    Buy(i32),
    /// The NPC's button (open its window, or go to the character list).
    Npc(crate::npc::NpcKind),
    /// A nearby player in the party list: opens the player menu.
    Member(i32),
    /// An option of the player menu, for that player.
    Menu(MenuOpt, i32),
    /// The minimap's zoom: in (-1) or out (+1).
    Zoom(i32),
    /// Answer the invitation.
    InviteYes,
    InviteNo,
    /// One of our items in the trade window; Accept; Cancel.
    TradeSlot(usize),
    TradeAccept,
    TradeCancel,
}

/// `PlayerMenu`'s options.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MenuOpt {
    Teleport,
    GuildInvite,
    Lock,
    Unlock,
    Whisper,
    Trade,
    Party,
    Ignore,
    Unignore,
}

impl MenuOpt {
    fn label(self) -> &'static str {
        match self {
            MenuOpt::Teleport => "Teleport",
            MenuOpt::GuildInvite => "Invite to guild",
            MenuOpt::Lock => "Lock",
            MenuOpt::Unlock => "Unlock",
            MenuOpt::Whisper => "Whisper",
            MenuOpt::Trade => "Trade",
            MenuOpt::Party => "Invite to party",
            MenuOpt::Ignore => "Ignore",
            MenuOpt::Unignore => "Unignore",
        }
    }
}

/// What the social parts of the HUD need from the game.
pub struct Social<'a> {
    pub trade: Option<&'a crate::game::Trade>,
    pub invite: Option<&'a crate::game::Invite>,
    pub locked: &'a std::collections::HashSet<String>,
    pub ignored: &'a std::collections::HashSet<String>,
    pub now: i32,
}

/// A player's account id (`ACCOUNT_ID_STAT`), as the account lists hold it.
fn account_id(o: &GameObj) -> Option<String> {
    o.stat_str(38).map(str::to_owned).or_else(|| o.stat_int(38).map(|v| v.to_string()))
}

/// `Party.update`: up to six other players within 50 tiles, locked ones first, then nearest.
fn party_members<'a>(world: &'a World, data: &GameData, locked: &std::collections::HashSet<String>) -> Vec<&'a GameObj> {
    let Some(me) = world.player() else { return Vec::new() };
    let mut players: Vec<(bool, f64, &GameObj)> = world
        .objects
        .values()
        .filter(|o| o.id != me.id && data.objects.get(&o.object_type).is_some_and(|d| d.player))
        .map(|o| {
            let starred = account_id(o).is_some_and(|a| locked.contains(&a));
            (starred, (o.x - me.x).hypot(o.y - me.y), o)
        })
        .filter(|(starred, dist, _)| *starred || *dist <= 50.0)
        .collect();
    players.sort_by(|a, b| b.0.cmp(&a.0).then(a.1.total_cmp(&b.1)));
    players.into_iter().take(6).map(|(_, _, o)| o).collect()
}

/// The thing beside us the interact area shows (`InteractPanel`): a bag or a portal.
#[derive(Clone, Debug)]
pub struct Interact {
    pub id: i32,
    pub name: String,
    pub kind: InteractKind,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum InteractKind {
    Container,
    Portal,
    /// `SellableObject` (merchants, guild merchants, closed vault chests): what it sells (-1
    /// when it sells itself), the price and currency (0 gold, 1 fame, 2 guild fame), the rank
    /// it needs.
    Shop { item: i32, price: i32, currency: i32, rank_req: i32 },
    /// An NPC with a panel (the Armoire, the guild register...), and the rank it asks for.
    Npc { kind: crate::npc::NpcKind, rank_req: i32 },
}

/// Merchandise stats (`StatData`).
pub mod merch {
    pub const TYPE: u8 = 34;
    pub const PRICE: u8 = 36;
    pub const CURRENCY: u8 = 40;
    pub const COUNT: u8 = 42;
    pub const MINS_LEFT: u8 = 43;
    pub const DISCOUNT: u8 = 44;
    pub const RANK_REQ: u8 = 45;
}

/// Whether objects of this description are sold where they stand.
pub fn is_shop(d: &ObjectDesc) -> bool {
    d.class == ObjectClass::Merchant || matches!(&d.class, ObjectClass::Other(c) if c == "GuildMerchant" || c == "ClosedVaultChest")
}

/// `GameSprite.updateNearestInteractive`: the closest bag, portal or shop within a tile.
pub fn interact_target(world: &World, data: &GameData) -> Option<Interact> {
    let me = world.player()?;
    world
        .objects
        .values()
        .filter_map(|o| {
            let d = data.objects.get(&o.object_type)?;
            let kind = if matches!(d.class, ObjectClass::Container | ObjectClass::OneWayContainer) {
                InteractKind::Container
            } else if matches!(d.class, ObjectClass::Portal | ObjectClass::GuildHallPortal) {
                InteractKind::Portal
            } else if is_shop(d) {
                let stat = |id: u8| o.stat_int(id).unwrap_or(0);
                let item = if d.class == ObjectClass::Merchant { o.stat_int(merch::TYPE).unwrap_or(-1) } else { -1 };
                InteractKind::Shop { item, price: stat(merch::PRICE), currency: stat(merch::CURRENCY), rank_req: stat(merch::RANK_REQ) }
            } else if let Some(kind) = match &d.class {
                ObjectClass::Other(c) => crate::npc::NpcKind::of_class(c),
                _ => None,
            } {
                InteractKind::Npc { kind, rank_req: o.stat_int(merch::RANK_REQ).unwrap_or(0) }
            } else {
                return None;
            };
            let dist = (o.x - me.x).hypot(o.y - me.y);
            (dist < 1.0).then_some((dist, o, d, kind))
        })
        .min_by(|a, b| a.0.total_cmp(&b.0))
        .map(|(_, o, d, kind)| Interact {
            id: o.id,
            // Bags and shops by their type's name (or the item's), portals by their name.
            name: match kind {
                // Bags by kind: "Loot Bag", not "Loot Bag 0" (the tier the id carries).
                InteractKind::Container => d.display_id.trim_end_matches(|c: char| c.is_ascii_digit()).trim_end().to_owned(),
                InteractKind::Npc { .. } => d.display_id.clone(),
                InteractKind::Shop { item, .. } if item > 0 => {
                    data.objects.get(&(item as u16)).map_or_else(|| d.display_id.clone(), |i| i.display_id.clone())
                }
                InteractKind::Shop { .. } => d.display_id.clone(),
                InteractKind::Portal => o.name().map(str::to_owned).unwrap_or_else(|| d.display_id.clone()),
            },
            kind,
        })
}

/// A price as text: "120 gold".
pub fn price_text(price: i32, currency: i32) -> String {
    let what = match currency {
        1 => "fame",
        2 => "guild fame",
        _ => "gold",
    };
    format!("{price} {what}")
}

/// HUD state kept between frames.
#[derive(Default)]
pub struct HudState {
    pub page: Page,
    /// A slot pressed but not yet dragged, and where the press was.
    press: Option<(Slot, (f64, f64))>,
    /// The slot whose item is being dragged.
    pub drag: Option<Slot>,
    /// The last click on a slot, for double clicks.
    last_click: Option<(i32, u8, i32)>,
    /// The player menu: for whom, and where it opened.
    menu: Option<(i32, (f32, f32))>,
    /// Which of the minimap's zoom levels (`MiniMap.zoomIndex`): 0 is the closest.
    pub minimap_zoom: usize,
    /// The key bound to the Nexus, for its button.
    pub nexus_key: String,
    /// The keys bound to the health and magic potions, for their stacks.
    pub potion_keys: [String; 2],
}

/// The minimap's design size (HUD units).
const MINIMAP_SIDE: f32 = 186.0;

/// `MiniMap.zoomLevels`: pixels a tile, halving from 4 until the whole map nearly fits.
pub fn minimap_levels(world: &World) -> Vec<f32> {
    let min = (MINIMAP_SIDE / world.width.max(1) as f32).max(MINIMAP_SIDE / world.height.max(1) as f32);
    let mut levels = Vec::new();
    let mut z = 4.0;
    while z > min && levels.len() < 8 {
        levels.push(z);
        z /= 2.0;
    }
    levels.push(min.min(4.0));
    levels
}

/// Where the minimap shows the world this frame.
#[derive(Clone, Copy, Debug)]
pub struct MiniView {
    pub rect: [f32; 4],
    /// Screen pixels a tile.
    pub zoom: f32,
    /// The world point at the centre.
    pub cx: f64,
    pub cy: f64,
}

impl MiniView {
    pub fn new(lay: &Layout, world: &World, me: &GameObj, zoom_index: usize) -> Self {
        let (x0, y0) = lay.at(7.0, 7.0);
        let side = MINIMAP_SIDE * lay.s;
        let levels = minimap_levels(world);
        let level = levels[zoom_index.min(levels.len() - 1)];
        // Keep the map's edge from leaving a gap: the view stops at the map's sides.
        let span = (MINIMAP_SIDE / level) as f64;
        let clamp = |c: f64, size: i32| if size as f64 <= span { size as f64 / 2.0 } else { c.clamp(span / 2.0, size as f64 - span / 2.0) };
        MiniView { rect: [x0, y0, side, side], zoom: level * lay.s, cx: clamp(me.x, world.width), cy: clamp(me.y, world.height) }
    }

    pub fn to_map(self, x: f64, y: f64) -> (f32, f32) {
        let [x0, y0, side, _] = self.rect;
        (x0 + side / 2.0 + ((x - self.cx) as f32) * self.zoom, y0 + side / 2.0 + ((y - self.cy) as f32) * self.zoom)
    }
}

/// What the player did on the HUD.
#[derive(Clone, Copy, Debug)]
pub enum Action {
    Swap(Slot, Slot),
    /// Double click: equip it, or use it (`equipOrUseInventory`); from a bag, take it.
    EquipOrUse(Slot),
    /// Shift-click: use it.
    Use(Slot),
    /// Dragged onto the world.
    Drop(Slot),
    /// Dragged from a bag onto the panel: into the first free slot.
    Take(Slot),
    /// A health or magic potion into its stack (`addToPotionStack`).
    Stack(Slot),
    Button(Button),
}

/// Everything clickable on the panel this frame.
pub struct Panel {
    pub slots: Vec<Slot>,
    buttons: Vec<(Button, [f32; 4])>,
    pub interact: Option<Interact>,
    has_backpack: bool,
    /// A shop's merchandise icon (for its tooltip), and the item.
    shop_icon: Option<([f32; 4], i32)>,
    /// The trade window and the player menu, when up (clicks inside them stay there).
    trade_rect: Option<[f32; 4]>,
    menu_rect: Option<[f32; 4]>,
    /// The nearby players listed, and the invitation shown.
    members: Vec<(i32, String, i32, bool)>,
    /// Other players on the minimap: id, name, where.
    minimap_players: Vec<(i32, String, (f32, f32))>,
    minimap: Option<MiniView>,
}

const DOUBLE_CLICK_MS: i32 = 350;

impl Panel {
    pub fn new(lay: &Layout, world: &World, data: &GameData, state: &HudState, social: &Social) -> Self {
        let page = state.page;
        let mut panel = Panel {
            slots: Vec::new(),
            buttons: Vec::new(),
            interact: None,
            has_backpack: false,
            shop_icon: None,
            trade_rect: None,
            menu_rect: None,
            members: Vec::new(),
            minimap_players: Vec::new(),
            minimap: None,
        };
        let Some(me) = world.player() else { return panel };
        let item = |o: &GameObj, slot: u8| o.stat_int(slot_stat(slot)).unwrap_or(-1);
        let types = class_slot_types(data.objects.get(&me.object_type));
        for i in 0..4u8 {
            panel.slots.push(Slot {
                owner: me.id,
                index: i,
                item: item(me, i),
                kind: SlotKind::Equip(types.get(i as usize).copied().unwrap_or(0)),
                rect: lay.rect(14.0 + 44.0 * i as f32, 307.0, 40.0, 40.0),
            });
        }
        panel.has_backpack = me.stat_int(stat::HAS_BACKPACK).unwrap_or(0) != 0;
        let page = if panel.has_backpack { page } else { Page::Inventory };
        let first = if page == Page::Backpack { 12 } else { 4 };
        for i in 0..8u8 {
            panel.slots.push(Slot {
                owner: me.id,
                index: first + i,
                item: item(me, first + i),
                kind: SlotKind::Inventory(i + 1),
                rect: lay.rect(14.0 + 44.0 * (i % 4) as f32, 381.0 + 44.0 * (i / 4) as f32, 40.0, 40.0),
            });
        }
        if panel.has_backpack {
            panel.buttons.push((Button::Page(Page::Inventory), lay.rect(14.0, 355.0, 84.0, 20.0)));
            panel.buttons.push((Button::Page(Page::Backpack), lay.rect(102.0, 355.0, 84.0, 20.0)));
        }
        for (k, (potion, slot_id)) in POTIONS.iter().enumerate() {
            let count = me.stat_int(if k == 0 { stat::HEALTH_POTION_STACK } else { stat::MAGIC_POTION_STACK }).unwrap_or(0);
            panel.slots.push(Slot {
                owner: me.id,
                index: *slot_id,
                item: *potion,
                kind: SlotKind::Potion(count),
                rect: lay.rect(14.0 + 44.0 * k as f32, 538.0, 40.0, 40.0),
            });
        }
        panel.buttons.push((Button::Nexus, lay.rect(106.0, 544.0, 82.0, 28.0)));
        // The minimap: zoom buttons in its corner, and the players on it.
        let mini = MiniView::new(lay, world, me, state.minimap_zoom);
        panel.minimap = Some(mini);
        panel.buttons.push((Button::Zoom(-1), lay.rect(171.0, 11.0, 18.0, 18.0)));
        panel.buttons.push((Button::Zoom(1), lay.rect(171.0, 31.0, 18.0, 18.0)));
        for o in world.objects.values() {
            if o.id == me.id || !data.objects.get(&o.object_type).is_some_and(|d| d.player) {
                continue;
            }
            let (px, py) = mini.to_map(o.x, o.y);
            if inside(mini.rect, (px as f64, py as f64)) {
                panel.minimap_players.push((o.id, o.name().unwrap_or("?").to_owned(), (px, py)));
            }
        }
        // InteractPanel: a bag's eight slots, or a portal's Enter button.
        panel.interact = interact_target(world, data);
        if let Some(target) = &panel.interact {
            if target.kind == InteractKind::Container {
                if let Some(bag) = world.objects.get(&target.id) {
                    for i in 0..8u8 {
                        panel.slots.push(Slot {
                            owner: bag.id,
                            index: i,
                            item: item(bag, i),
                            kind: SlotKind::Container,
                            rect: lay.rect(14.0 + 44.0 * (i % 4) as f32, 612.0 + 44.0 * (i / 4) as f32, 40.0, 40.0),
                        });
                    }
                }
            } else if target.kind == InteractKind::Portal {
                panel.buttons.push((Button::Enter(target.id), lay.rect(40.0, 624.0, 120.0, 34.0)));
            } else if let InteractKind::Npc { kind, rank_req } = target.kind {
                if crate::npc::interact_text(kind, me, rank_req).1.is_some() {
                    panel.buttons.push((Button::Npc(kind), lay.rect(16.0, 650.0, 168.0, 34.0)));
                }
            } else {
                panel.buttons.push((Button::Buy(target.id), lay.rect(16.0, 656.0, 168.0, 32.0)));
                if let InteractKind::Shop { item, .. } = target.kind {
                    panel.shop_icon = Some((lay.rect(76.0, 610.0, 48.0, 44.0), item));
                }
            }
        } else if social.invite.is_some() {
            // An invitation waits in the interact area (TradeRequestPanel, PartyInvitePanel...).
            panel.buttons.push((Button::InviteYes, lay.rect(16.0, 656.0, 82.0, 30.0)));
            panel.buttons.push((Button::InviteNo, lay.rect(102.0, 656.0, 82.0, 30.0)));
        } else {
            // PartyPanel: the players around us.
            for (i, o) in party_members(world, data, social.locked).into_iter().enumerate() {
                let starred = account_id(o).is_some_and(|a| social.locked.contains(&a));
                let name = o.name().unwrap_or("?").to_owned();
                panel.members.push((o.id, name, o.stat_int(stat::LEVEL).unwrap_or(0), starred));
                panel.buttons.push((Button::Member(o.id), lay.rect(12.0, 608.0 + 14.5 * i as f32, 176.0, 14.0)));
            }
        }

        // The trade window, in the middle of the view.
        if let Some(trade) = social.trade {
            let s = lay.s;
            let (w, h) = (440.0 * s, 290.0 * s);
            let r = [(lay.view_w() - w) / 2.0, (lay.h - h) / 2.0, w, h];
            panel.trade_rect = Some(r);
            for i in 0..trade.mine.len().min(12) {
                let sr = [r[0] + 20.0 * s + (i % 4) as f32 * 46.0 * s, r[1] + 70.0 * s + (i / 4) as f32 * 46.0 * s, 42.0 * s, 42.0 * s];
                panel.buttons.push((Button::TradeSlot(i), sr));
            }
            panel.buttons.push((Button::TradeCancel, [r[0] + r[2] - 252.0 * s, r[1] + r[3] - 52.0 * s, 110.0 * s, 36.0 * s]));
            panel.buttons.push((Button::TradeAccept, [r[0] + r[2] - 130.0 * s, r[1] + r[3] - 52.0 * s, 110.0 * s, 36.0 * s]));
        }

        // The player menu, where it was opened.
        if let Some((id, at)) = state.menu {
            if let (Some(p), Some(me)) = (world.objects.get(&id), world.player()) {
                let mut opts = vec![MenuOpt::Teleport];
                let my_rank = me.stat_int(63).unwrap_or(0);
                if my_rank >= 20 && p.stat_str(62).unwrap_or("").is_empty() {
                    opts.push(MenuOpt::GuildInvite);
                }
                let acc = account_id(p);
                opts.push(if acc.as_ref().is_some_and(|a| social.locked.contains(a)) { MenuOpt::Unlock } else { MenuOpt::Lock });
                opts.extend([MenuOpt::Whisper, MenuOpt::Trade, MenuOpt::Party]);
                opts.push(if acc.as_ref().is_some_and(|a| social.ignored.contains(a)) { MenuOpt::Unignore } else { MenuOpt::Ignore });
                let s = lay.s;
                let (w, row) = (160.0 * s, 28.0 * s);
                let h = row * opts.len() as f32 + 34.0 * s;
                let x = at.0.min(lay.w - w - 4.0).max(4.0);
                let y = at.1.min(lay.h - h - 4.0).max(4.0);
                panel.menu_rect = Some([x, y, w, h]);
                for (i, o) in opts.into_iter().enumerate() {
                    panel.buttons.push((Button::Menu(o, id), [x + 6.0 * s, y + 30.0 * s + i as f32 * row, w - 12.0 * s, row - 2.0 * s]));
                }
            }
        }
        panel
    }

    /// The players on the minimap within a few pixels of `pos` (`MiniMap`'s tooltip).
    fn minimap_players_at(&self, pos: (f64, f64), s: f32) -> Vec<&(i32, String, (f32, f32))> {
        let reach = 6.0 * s;
        self.minimap_players
            .iter()
            .filter(|(_, _, (x, y))| (*x - pos.0 as f32).abs() <= reach && (*y - pos.1 as f32).abs() <= reach)
            .collect()
    }

    pub fn slot_at(&self, pos: (f64, f64)) -> Option<Slot> {
        self.slots.iter().find(|s| inside(s.rect, pos)).copied()
    }

    fn button_at(&self, pos: (f64, f64)) -> Option<Button> {
        self.buttons.iter().find(|(_, r)| inside(*r, pos)).map(|(b, _)| *b)
    }

    /// The left button went down at `pos`. True if the HUD took it (so the game doesn't fire).
    pub fn press(&self, state: &mut HudState, lay: &Layout, pos: (f64, f64), shift: bool, now: i32, actions: &mut Vec<Action>) -> bool {
        // The player menu: an option, or a click anywhere else closes it.
        if let Some(r) = self.menu_rect {
            state.menu = None;
            // Only the menu's own options: it lies over the panel's buttons.
            let option = self.buttons.iter().find(|(b, r)| matches!(b, Button::Menu(..)) && inside(*r, pos));
            if let Some((b, _)) = option {
                actions.push(Action::Button(*b));
            }
            return inside(r, pos) || lay.on_panel(pos.0, pos.1);
        }
        // The trade window keeps its clicks.
        if let Some(r) = self.trade_rect {
            if inside(r, pos) {
                let hit = self.buttons.iter().find(|(b, br)| {
                    matches!(b, Button::TradeSlot(_) | Button::TradeAccept | Button::TradeCancel) && inside(*br, pos)
                });
                if let Some((b, _)) = hit {
                    actions.push(Action::Button(*b));
                }
                return true;
            }
        }
        if let Some(Button::Member(id)) = self.button_at(pos) {
            state.menu = Some((id, (pos.0 as f32, pos.1 as f32)));
            return true;
        }
        if let Some(Button::Zoom(d)) = self.button_at(pos) {
            state.minimap_zoom = (state.minimap_zoom as i32 + d).max(0) as usize;
            return true;
        }
        // A player on the minimap: their menu.
        if let Some((id, _, _)) = self.minimap_players_at(pos, lay.s).first() {
            state.menu = Some((*id, (pos.0 as f32, pos.1 as f32)));
            return true;
        }
        if let Some(slot) = self.slot_at(pos) {
            if slot.item > 0 {
                if shift {
                    actions.push(Action::Use(slot));
                } else if state.last_click.is_some_and(|(o, i, t)| o == slot.owner && i == slot.index && now - t < DOUBLE_CLICK_MS) {
                    let stackable = POTIONS.iter().any(|p| p.0 == slot.item) && !matches!(slot.kind, SlotKind::Potion(_) | SlotKind::Shop);
                    actions.push(if stackable { Action::Stack(slot) } else { Action::EquipOrUse(slot) });
                    state.last_click = None;
                } else {
                    state.last_click = Some((slot.owner, slot.index, now));
                    state.press = Some((slot, pos));
                }
            }
            return true;
        }
        if let Some(button) = self.button_at(pos) {
            if let Button::Page(p) = button {
                state.page = p;
            }
            actions.push(Action::Button(button));
            return true;
        }
        lay.on_panel(pos.0, pos.1)
    }

    /// The mouse moved: a press becomes a drag once it has gone a few pixels.
    pub fn moved(&self, state: &mut HudState, pos: (f64, f64)) {
        if let Some((slot, from)) = state.press {
            if (pos.0 - from.0).hypot(pos.1 - from.1) > 4.0 {
                state.drag = Some(slot);
                state.press = None;
            }
        }
    }

    /// The left button came up at `pos`.
    pub fn release(&self, state: &mut HudState, lay: &Layout, data: &GameData, pos: (f64, f64), actions: &mut Vec<Action>) {
        state.press = None;
        let Some(from) = state.drag.take() else { return };
        if let Some(to) = self.slot_at(pos) {
            if to.is(&from) {
                return;
            }
            if let SlotKind::Potion(_) = to.kind {
                if from.item == to.item && !matches!(from.kind, SlotKind::Potion(_) | SlotKind::Shop) {
                    actions.push(Action::Stack(from));
                }
                return;
            }
            let fits = can_hold(data, to.kind, from.item) && can_hold(data, from.kind, to.item);
            if fits && !matches!(from.kind, SlotKind::Potion(_)) {
                actions.push(Action::Swap(from, to));
            }
        } else if !lay.on_panel(pos.0, pos.1) {
            if from.kind != SlotKind::Container {
                actions.push(Action::Drop(from));
            }
        } else if from.kind == SlotKind::Container {
            actions.push(Action::Take(from));
        }
    }
}

/// The panel: minimap, portrait and name, bars, equipment, inventory, stats, potions, the
/// interact area.
#[allow(clippy::too_many_arguments)]
pub fn draw_panel(ui: &mut Ui, lay: &Layout, world: &World, camera_angle: f64, colors: &mut MapColors, panel: &Panel, state: &HudState, social: &Social, mouse: (f64, f64)) {
    let s = lay.s;
    let (px, pw) = (lay.panel_x, lay.w - lay.panel_x);
    // A soft shadow cast onto the world, the body, and a light edge.
    for i in 0..8 {
        let a = 0.22 * (1.0 - i as f32 / 8.0);
        ui.rect(px - (i + 1) as f32 * 2.0 * s, 0.0, 2.0 * s, lay.h, 0x000000, a);
    }
    theme::rounded(ui.renderer, [px, 0.0, pw, lay.h], 0.0, (color::PANEL_TOP, 0.98), (color::PANEL_BOTTOM, 0.99));
    ui.rect(px, 0.0, s.max(1.0), lay.h, color::BORDER_HI, 0.7);
    let Some(me) = world.player() else { return };
    let mini = panel.minimap.unwrap_or_else(|| MiniView::new(lay, world, me, state.minimap_zoom));
    draw_minimap(ui, lay, world, me, camera_angle, colors, &mini);
    // Where we are, at the minimap's foot: the realm's (or map's) name.
    if !world.name.is_empty() {
        let size = lay.font(11.0);
        let [mx, my, _, mh] = mini.rect;
        ui.label(&world.name, mx + 5.0 * s, my + mh - size * 1.3 - 2.0 * s, size, true, color::TEXT);
    }
    // Zoom in and out, in the corner.
    let levels = minimap_levels(world).len();
    for (button, r) in &panel.buttons {
        let Button::Zoom(d) = button else { continue };
        let usable = if *d < 0 { state.minimap_zoom > 0 } else { state.minimap_zoom + 1 < levels };
        let over = usable && inside(*r, mouse);
        theme::fill(ui.renderer, *r, 4.0 * s, if over { color::CHIP_HI } else { color::CHIP }, if usable { 0.9 } else { 0.45 });
        let label = if *d < 0 { "+" } else { "-" };
        let size = lay.font(13.0);
        let w = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if usable { color::TEXT } else { color::TEXT_MUTED });
    }

    // The portrait, name, and class and level.
    draw_portrait(ui, lay, me);
    let (x, y) = lay.at(44.0, 199.0);
    let name = me.name().unwrap_or("").to_owned();
    ui.label(&name, x, y, lay.font(17.0), true, color::TEXT);
    let stat = |id: u8| me.stat_int(id).unwrap_or(0);
    let level = stat(stat::LEVEL);
    let class = ui.data.objects.get(&me.object_type).map_or("", |d| d.display_id.as_str()).to_owned();
    let (x, y) = lay.at(44.0, 217.0);
    ui.label(&format!("{class}  ·  Level {level}"), x, y, lay.font(11.0), false, color::TEXT_DIM);

    // Experience (fame at level 20), HP, MP; HP and MP with what items add, and gold when the
    // base is at the class's maximum (as the stats below show it).
    let class_max = |tag: &str| {
        ui.data.objects.get(&me.object_type).and_then(|c| c.xml.child(tag)).and_then(|e| e.attr("max")).and_then(|m| m.parse::<i32>().ok())
    };
    let (hp_boost, mp_boost) = (stat(stat::MAX_HP_BOOST), stat(stat::MAX_MP_BOOST));
    let hp_maxed = class_max("MaxHitPoints").is_some_and(|m| stat(stat::MAX_HP) - hp_boost >= m);
    let mp_maxed = class_max("MaxMagicPoints").is_some_and(|m| stat(stat::MAX_MP) - mp_boost >= m);
    let bars = [
        if level < 20 {
            ("XP".to_owned(), stat(stat::EXP), stat(stat::NEXT_LEVEL_EXP), 0x6FAF2E, 0, false)
        } else {
            ("Fame".to_owned(), stat(stat::CURR_FAME), stat(stat::NEXT_CLASS_QUEST_FAME), 0xF07A1E, 0, false)
        },
        ("HP".to_owned(), stat(stat::HP), stat(stat::MAX_HP), 0xE0403E, hp_boost, hp_maxed),
        ("MP".to_owned(), stat(stat::MP), stat(stat::MAX_MP), 0x4C7BE8, mp_boost, mp_maxed),
    ];
    for (i, (label, value, max, c, boost, maxed)) in bars.into_iter().enumerate() {
        let (bx, by) = lay.at(12.0, 236.0 + 22.0 * i as f32);
        status_bar(ui, lay, bx, by, &label, value, max, c, boost, maxed);
    }

    // Sections: equipment, inventory, stats, potions.
    let section = |ui: &mut Ui, r: [f32; 4]| {
        theme::frame(ui.renderer, r, 7.0 * s, s.max(1.0), (color::BORDER, 0.9), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
    };
    section(ui, lay.rect(8.0, 302.0, 184.0, 50.0));
    section(ui, lay.rect(8.0, 376.0, 184.0, 94.0));
    section(ui, lay.rect(8.0, 474.0, 184.0, 60.0));
    section(ui, lay.rect(8.0, 538.0 - 5.0, 184.0, 50.0));

    // Inventory and backpack tabs, as pills.
    for (button, r) in &panel.buttons {
        let Button::Page(p) = button else { continue };
        let on = *p == state.page;
        let over = inside(*r, mouse);
        let bg = if on { color::ACCENT } else if over { color::SECONDARY } else { color::CHIP };
        theme::fill(ui.renderer, *r, r[3] / 2.0, bg, 1.0);
        let label = if *p == Page::Inventory { "Inventory" } else { "Backpack" };
        let size = lay.font(11.0);
        let w = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
    }

    // The interact area's box goes under its slots (a bag's).
    if panel.interact.is_some() {
        let r = lay.rect(8.0, 588.0, 184.0, 108.0);
        theme::frame(ui.renderer, r, 7.0 * s, s.max(1.0), (color::ACCENT, 0.55), ((color::WARM_TOP, 0.92), (color::WARM_BOTTOM, 0.92)));
    }
    let hover_target = if state.drag.is_some() { panel.slot_at(mouse) } else { None };
    for slot in &panel.slots {
        let dragged = state.drag.is_some_and(|d| d.is(slot));
        let target = hover_target.is_some_and(|h| h.is(slot));
        draw_slot(ui, lay, slot, dragged, target, inside(slot.rect, mouse), &state.potion_keys);
    }

    // Stats, two columns of three: gold at the class maximum, the boost beside in green.
    let class_desc = ui.data.objects.get(&me.object_type);
    let max_of = |tag: &str| class_desc.and_then(|c| c.xml.child(tag)).and_then(|e| e.attr("max")).and_then(|m| m.parse::<i32>().ok());
    let stats = [
        ("ATT", stat::ATTACK, stat::ATTACK_BOOST, "Attack"),
        ("DEF", stat::DEFENSE, stat::DEFENSE_BOOST, "Defense"),
        ("SPD", stat::SPEED, stat::SPEED_BOOST, "Speed"),
        ("DEX", stat::DEXTERITY, stat::DEXTERITY_BOOST, "Dexterity"),
        ("VIT", stat::VITALITY, stat::VITALITY_BOOST, "HpRegen"),
        ("WIS", stat::WISDOM, stat::WISDOM_BOOST, "MpRegen"),
    ];
    for (i, (label, id, boost_id, tag)) in stats.into_iter().enumerate() {
        let (value, boost) = (stat(id), stat(boost_id));
        let (tx, ty) = lay.at(16.0 + 90.0 * (i % 2) as f32, 479.0 + 17.0 * (i / 2) as f32);
        let size = lay.font(12.0);
        ui.label(label, tx, ty, size, true, color::TEXT_MUTED);
        let maxed = max_of(tag).is_some_and(|m| value - boost >= m);
        let c = if maxed { 0xFFD24A } else { color::TEXT };
        let room = 50.0 * s;
        let value_s = value.to_string();
        let boost_s = if boost != 0 { format!(" {boost:+}") } else { String::new() };
        let mut vs = lay.font(13.0);
        let mut bs = lay.font(10.0);
        let full = ui.measure(&value_s, vs, true) + ui.measure(&boost_s, bs, true);
        if full > room {
            let k = room / full;
            vs = (vs * k).floor().max(8.0);
            bs = (bs * k).floor().max(7.0);
        }
        let vx = tx + 30.0 * s;
        ui.label(&value_s, vx, ty + (size - vs) * 0.6, vs, true, c);
        if boost != 0 {
            let bx = vx + ui.measure(&value_s, vs, true);
            ui.label(&boost_s, bx, ty + (size - bs) * 0.9, bs, true, if boost > 0 { color::GOOD } else { color::DANGER });
        }
    }

    // Nothing to interact with: the invitation, or the players around us.
    if panel.interact.is_none() {
        let r = lay.rect(8.0, 588.0, 184.0, 108.0);
        if let Some(invite) = social.invite {
            theme::frame(ui.renderer, r, 7.0 * s, s.max(1.0), (color::ACCENT, 0.7), ((color::WARM_TOP, 0.92), (color::WARM_BOTTOM, 0.92)));
            let (title, detail) = match invite {
                crate::game::Invite::Trade { name } => (name.clone(), "wants to trade with you".to_owned()),
                crate::game::Invite::Party { name, .. } => (name.clone(), "invites you to a party".to_owned()),
                crate::game::Invite::Guild { name, guild } => (name.clone(), format!("invites you to {guild}")),
            };
            let size = lay.font(14.0);
            let tw = ui.measure(&title, size, true);
            ui.label(&title, lay.panel_x + (PANEL_W * s - tw) / 2.0, 600.0 * s, size, true, color::ACCENT_HI);
            let small = lay.font(11.0);
            let dw = ui.measure(&detail, small, false);
            ui.label(&detail, lay.panel_x + (PANEL_W * s - dw) / 2.0, 622.0 * s, small, false, color::TEXT_DIM);
        } else if !panel.members.is_empty() {
            theme::frame(ui.renderer, r, 7.0 * s, s.max(1.0), (color::BORDER, 0.9), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
            ui.label("Nearby players", lay.panel_x + 14.0 * s, 591.0 * s, lay.font(11.0), true, color::TEXT_MUTED);
            for (id, name, level, starred) in &panel.members {
                let br = panel.buttons.iter().find(|(b, _)| *b == Button::Member(*id)).map(|(_, r)| *r).unwrap_or([0.0; 4]);
                if inside(br, mouse) {
                    theme::fill(ui.renderer, br, 3.0 * s, 0xFFFFFF, 0.07);
                }
                let size = lay.font(11.5);
                let y = br[1] + (br[3] - size * 1.22) / 2.0;
                // A starred (locked) player: a gold star before the name.
                let mut x = br[0] + 4.0 * s;
                if *starred {
                    theme::star(ui.renderer, x + size * 0.4, br[1] + br[3] / 2.0, size * 0.42, 0xFFD24A);
                    x += size * 0.95;
                }
                ui.label(name, x, y, size, true, if *starred { 0xFFD24A } else { color::TEXT });
                let lv = format!("{level}");
                let lw = ui.measure(&lv, size, false);
                ui.label(&lv, br[0] + br[2] - lw - 4.0 * s, y, size, false, color::TEXT_MUTED);
            }
        }
    }

    // The interact area's title: what we stand by.
    if let Some(target) = &panel.interact {
        let size = lay.font(13.0);
        let w = ui.measure(&target.name, size, true);
        ui.label(&target.name, lay.panel_x + (PANEL_W * s - w) / 2.0, 592.0 * s, size, true, color::ACCENT_HI);
        // An NPC: what it does (or who we are to it).
        if let InteractKind::Npc { kind, rank_req } = target.kind {
            let (line, _) = crate::npc::interact_text(kind, me, rank_req);
            let small = lay.font(11.5);
            let lw = ui.measure(&line, small, false);
            ui.label(&line, lay.panel_x + (PANEL_W * s - lw) / 2.0, 618.0 * s, small, false, color::TEXT_DIM);
        }
        // A shop: what it sells, and the rank it asks for.
        if let InteractKind::Shop { rank_req, .. } = target.kind {
            if let Some((r, item)) = panel.shop_icon {
                ui.slot_box(r, if item > 0 { crate::screens::tier_edge(ui.data, item) } else { None }, inside(r, mouse), s);
                let shop_type = world.objects.get(&target.id).map(|o| o.object_type as i32).unwrap_or(-1);
                ui.item_icon(if item > 0 { item } else { shop_type }, 80, r[0] + r[2] / 2.0, r[1] + r[3] / 2.0, s);
            }
            if rank_req > 0 {
                let t = format!("Rank {rank_req} needed");
                let small = lay.font(10.0);
                let tw = ui.measure(&t, small, true);
                ui.label(&t, lay.panel_x + (PANEL_W * s - tw) / 2.0, 643.0 * s, small, true, 0xFFB070);
            }
        }
    }

    // Buttons: Nexus, and Enter for a portal.
    for (button, r) in &panel.buttons {
        let (label, kind) = match button {
            Button::Nexus => (format!("Nexus  {}", state.nexus_key), Kind::Secondary),
            Button::Enter(_) => ("Enter  0".to_owned(), Kind::Primary),
            Button::Buy(_) => match panel.interact.as_ref().map(|i| i.kind) {
                Some(InteractKind::Shop { price, currency, .. }) => (format!("Buy  ·  {}", price_text(price, currency)), Kind::Primary),
                _ => continue,
            },
            Button::Npc(_) => match panel.interact.as_ref().map(|i| i.kind) {
                Some(InteractKind::Npc { kind, rank_req }) => match crate::npc::interact_text(kind, me, rank_req).1 {
                    Some(b) => b,
                    None => continue,
                },
                _ => continue,
            },
            Button::InviteYes => (if matches!(social.invite, Some(crate::game::Invite::Trade { .. })) { "Trade" } else { "Join" }.to_owned(), Kind::Primary),
            Button::InviteNo => ("Decline".to_owned(), Kind::Secondary),
            _ => continue,
        };
        ui.button(*r, &label, kind, inside(*r, mouse), s);
    }
}

fn draw_slot(ui: &mut Ui, lay: &Layout, slot: &Slot, dragged: bool, target: bool, hover: bool, potion_keys: &[String; 2]) {
    let s = lay.s;
    let [x, y, w, h] = slot.rect;
    let empty = slot.item <= 0 || dragged || matches!(slot.kind, SlotKind::Potion(0));
    let edge = if target {
        Some(color::ACCENT)
    } else if !empty && !matches!(slot.kind, SlotKind::Potion(_)) {
        crate::screens::tier_edge(ui.data, slot.item)
    } else {
        None
    };
    ui.slot_box(slot.rect, edge, hover, s);
    if empty {
        if let SlotKind::Equip(t) = slot.kind {
            if let Some((set, index)) = slot_type_icon(t) {
                ui.silhouette(set, index, x + 6.0 * s, y + 6.0 * s, 28.0 * s, color::SILHOUETTE);
            }
        }
    }
    if !empty {
        ui.item_icon(slot.item, 80, x + w / 2.0, y + h / 2.0, s);
    } else if let SlotKind::Potion(_) = slot.kind {
        // An empty stack shows its potion as a shadow.
        if let Some(src) = ui.data.objects.get(&(slot.item as u16)).and_then(|d| ui.src(&d.texture)) {
            if let Some(rect) = ui.sprites.atlas(src) {
                let add = bsk_render::rgb_f32(color::SILHOUETTE);
                ui.renderer.sprite(rect, Quad::rect(x + 6.0 * s, y + 6.0 * s, 28.0 * s, 28.0 * s), Tint { mul: [0.0, 0.0, 0.0, 1.0], add });
            }
        }
    }
    let small = lay.font(10.0);
    match slot.kind {
        // The number key, small in the corner.
        SlotKind::Inventory(key) => ui.label(&key.to_string(), x + 4.0 * s, y + 2.0 * s, small, true, color::TEXT_MUTED),
        SlotKind::Potion(count) => {
            let key = &potion_keys[if slot.index == POTIONS[0].1 { 0 } else { 1 }];
            ui.label(key, x + 4.0 * s, y + 2.0 * s, small, true, color::TEXT_MUTED);
            let c = count.to_string();
            let bw = ui.measure(&c, small, true) + 8.0 * s;
            let bg = if count > 0 { color::ACCENT } else { color::SECONDARY };
            let fg = if count > 0 { color::ACCENT_TEXT } else { color::TEXT_DIM };
            theme::fill(ui.renderer, [x + w - bw - 2.0 * s, y + h - small * 1.3 - 2.0 * s, bw, small * 1.3], small * 0.65, bg, 0.95);
            ui.label(&c, x + w - bw + 2.0 * s, y + h - small * 1.3 - 2.0 * s, small, true, fg);
        }
        _ => {}
    }
    if !empty && !matches!(slot.kind, SlotKind::Potion(_)) {
        let doses = ui.data.objects.get(&(slot.item as u16)).and_then(|d| d.xml.text_of("Doses").or(d.xml.text_of("Quantity")).map(str::to_owned));
        if let Some(n) = doses {
            let nw = ui.measure(&n, small, true);
            ui.label(&n, x + w - nw - 4.0 * s, y + h - small * 1.3, small, true, color::TEXT);
        }
    }
}

/// The player's portrait (`Player.getPortrait`): the class (or skin) standing, dyed.
fn draw_portrait(ui: &mut Ui, lay: &Layout, me: &GameObj) {
    let skin = me.stat_int(stat::TEXTURE).filter(|&t| t != 0).and_then(|t| ui.data.objects.get(&(t as u16)));
    let Some(desc) = skin.or_else(|| ui.data.objects.get(&me.object_type)) else { return };
    let TextureRef::Animated { set, index } = &desc.texture.pick(0).main else { return };
    let Some(src) = ui.sprites.char_src(set, *index, 0, 0) else { return };
    let dye = ui.sprites.char_mask_src(set, *index, 0, 0).map(|mask| Dye {
        mask,
        tex1: me.stat_int(stat::TEX1).unwrap_or(0),
        tex2: me.stat_int(stat::TEX2).unwrap_or(0),
    });
    // Redrawn at the size it shows, centred on the sprite (see `item_icon`).
    let size = ((60.0 * lay.s * 0.8).round() as i32).max(1);
    let Some(icon) = ui.redrawer.get(ui.sprites, ui.renderer, src, false, size, dye) else { return };
    let (cx, cy) = lay.at(22.0, 213.0);
    let (w, h) = (icon.w as f32, icon.h as f32);
    let lift = (crate::redraw::PAD as f32 - 1.0) / 2.0;
    ui.renderer.sprite(icon.rect, Quad::rect((cx - w / 2.0).round(), (cy - h / 2.0 - lift).round(), w, h), Tint::NONE);
}

/// A status bar: a rounded dark track, a shaded fill, the label at the left and the numbers at
/// the right.
/// `boost` is what items add to the maximum, shown after it in dark green (red if negative);
/// `maxed` turns the numbers gold.
#[allow(clippy::too_many_arguments)]
fn status_bar(ui: &mut Ui, lay: &Layout, x: f32, y: f32, label: &str, value: i32, max: i32, c: u32, boost: i32, maxed: bool) {
    let s = lay.s;
    let (w, h) = (176.0 * s, 18.0 * s);
    let r = [x, y, w, h];
    theme::frame(ui.renderer, r, 5.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::TRACK, 1.0), (theme::shade(color::TRACK, 0.08), 1.0)));
    let frac = if max > 0 { (value as f32 / max as f32).clamp(0.0, 1.0) } else { 0.0 };
    let inner = [x + s, y + s, (w - 2.0 * s) * frac, h - 2.0 * s];
    if inner[2] > 1.0 {
        theme::rounded(ui.renderer, inner, 4.0 * s, (theme::shade(c, 0.25), 1.0), (theme::shade(c, -0.2), 1.0));
        theme::fill(ui.renderer, [inner[0] + 2.0 * s, inner[1] + s, (inner[2] - 4.0 * s).max(0.0), inner[3] * 0.35], 2.0 * s, 0xFFFFFF, 0.14);
    }
    let size = lay.font(11.0);
    let ty = y + (h - size * 1.22) / 2.0;
    ui.label(label, x + 6.0 * s, ty, size, true, color::TEXT);
    let (v, m) = (format!("{value} / "), max.to_string());
    let b = if boost != 0 { format!(" {boost:+}") } else { String::new() };
    let small = lay.font(9.5);
    let (vw, mw, bw) = (ui.measure(&v, size, true), ui.measure(&m, size, true), ui.measure(&b, small, true));
    let mut tx = x + w - vw - mw - bw - 6.0 * s;
    ui.label(&v, tx, ty, size, true, if maxed { 0xFFD24A } else { color::TEXT });
    tx += vw;
    ui.label(&m, tx, ty, size, true, if maxed { 0xFFD24A } else { color::TEXT });
    tx += mw;
    if boost != 0 {
        // Dark, so it reads over the bar's colour.
        ui.label(&b, tx, y + (h - small * 1.22) / 2.0, small, true, if boost > 0 { 0x0F5A22 } else { 0x6A0F0F });
    }
}

/// `MiniMap.draw` at its first zoom (4 units a tile), north up, the player's arrow turned with
/// the camera. Static objects that fill their square show their colour over the ground.
fn draw_minimap(ui: &mut Ui, lay: &Layout, world: &World, me: &GameObj, camera_angle: f64, colors: &mut MapColors, mini: &MiniView) {
    let [x0, y0, side, _] = mini.rect;
    let e = 3.0 * lay.s;
    theme::shadow(ui.renderer, [x0, y0, side, side], 6.0 * lay.s, 6.0 * lay.s, 0.35);
    theme::frame(ui.renderer, [x0 - e, y0 - e, side + 2.0 * e, side + 2.0 * e], 7.0 * lay.s, lay.s.max(1.0), (color::BORDER_HI, 1.0), ((MINIMAP_FILL, 1.0), (MINIMAP_FILL, 1.0)));
    // Zoomed far out, a cell covers several tiles (each at least two pixels wide).
    let step = ((2.0 / mini.zoom).ceil() as i32).max(1);
    let zoom = mini.zoom * step as f32;
    let half = (side / 2.0 / mini.zoom).ceil() as i32 + step;
    let (cx, cy) = (mini.cx, mini.cy);
    let to_map = |x: f64, y: f64| mini.to_map(x, y);
    let (tx, ty) = ((cx.floor() as i32).div_euclid(step) * step, (cy.floor() as i32).div_euclid(step) * step);
    // Static objects that fill their square, by cell of the box (u32::MAX: none).
    let cells = (2 * half / step + 1) as usize;
    let mut marks = vec![u32::MAX; cells * cells];
    let cell_of = |x: i32, y: i32| -> Option<usize> {
        let (dx, dy) = (x - (tx - half), y - (ty - half));
        (dx >= 0 && dy >= 0 && dx % step == 0 && dy % step == 0 && (dx / step) < cells as i32 && (dy / step) < cells as i32)
            .then(|| (dy / step) as usize * cells + (dx / step) as usize)
    };
    for o in world.objects.values() {
        if let Some(d) = ui.data.objects.get(&o.object_type) {
            if d.is_static && d.occupy_square && !d.no_mini_map {
                if let Some(i) = cell_of(o.x.floor() as i32, o.y.floor() as i32) {
                    marks[i] = colors.object(ui, o.object_type);
                }
            }
        }
    }
    // Each row as runs of one colour, one rectangle a run.
    for (row, y) in (ty - half..=ty + half).step_by(step as usize).enumerate() {
        let mut run: Option<([f32; 4], u32)> = None;
        for (col, x) in (tx - half..=tx + half).step_by(step as usize).enumerate() {
            let cell = world.tile(x, y).and_then(|tile| {
                let (px, py) = to_map(x as f64, y as f64);
                // Whole tiles only, clipped to the box.
                let (l, t) = (px.max(x0), py.max(y0));
                let (r, b) = ((px + zoom).min(x0 + side), (py + zoom).min(y0 + side));
                if r <= l || b <= t {
                    return None;
                }
                let mark = marks.get(row * cells + col).copied().unwrap_or(u32::MAX);
                let color = if mark != u32::MAX { mark } else { colors.tile(ui, tile) };
                Some(([l, t, r, b], color))
            });
            match (&mut run, cell) {
                (Some((rr, rc)), Some((c, color))) if *rc == color && rr[1] == c[1] && rr[3] == c[3] && rr[2] == c[0] => rr[2] = c[2],
                (_, next) => {
                    if let Some(([l, t, r, b], color)) = run {
                        ui.rect(l, t, r - l, b - t, color, 1.0);
                    }
                    run = next;
                }
            }
        }
        if let Some(([l, t, r, b], color)) = run {
            ui.rect(l, t, r - l, b - t, color, 1.0);
        }
    }
    // Players yellow, enemies red, other characters their colour, portals blue; at the box's
    // edge when out of it.
    for o in world.objects.values() {
        if Some(o.id) == world.me {
            continue;
        }
        let Some(d) = ui.data.objects.get(&o.object_type) else { continue };
        if d.no_mini_map {
            continue;
        }
        let color = if d.player {
            0xFFFF00
        } else if d.class == ObjectClass::Character {
            if d.enemy { 0xFF0000 } else { colors.object(ui, o.object_type) }
        } else if matches!(d.class, ObjectClass::Portal | ObjectClass::GuildHallPortal) {
            0x0000FF
        } else {
            continue;
        };
        let (px, py) = to_map(o.x, o.y);
        let d2 = 2.0 * lay.s;
        let (px, py) = (px.clamp(x0 + d2, x0 + side - d2), py.clamp(y0 + d2, y0 + side - d2));
        ui.rect(px - d2, py - d2, 2.0 * d2, 2.0 * d2, color, 1.0);
    }
    // The player: lofiInterface 54 tinted blue, pointing the way the camera faces.
    let (px, py) = to_map(me.x, me.y);
    if let Some(arrow) = ui.sprites.tile("lofiInterface", 54) {
        let (w, h) = (8.0 * lay.s, 32.0 * lay.s);
        let quad = Quad::rotated(px, py, w, h, camera_angle as f32);
        ui.renderer.sprite(arrow, quad, Tint { mul: [0.0, 0.0, 1.0, 1.0], add: [0.0; 3] });
    } else {
        ui.rect(px - 2.0 * lay.s, py - 2.0 * lay.s, 4.0 * lay.s, 4.0 * lay.s, 0x0000FF, 1.0);
    }
}

/// Drawn over everything: the item being dragged, and the tooltip for what's under the mouse.
pub fn draw_overlay(ui: &mut Ui, lay: &Layout, world: &World, panel: &Panel, state: &HudState, mouse: (f64, f64)) {
    let (mx, my) = (mouse.0 as f32, mouse.1 as f32);
    if let Some(drag) = state.drag {
        ui.item_icon(drag.item, 80, mx, my, lay.s);
        return;
    }
    // A nearby player in the list: who they are, their health, magic and gear (PlayerToolTip).
    let member = panel.buttons.iter().find_map(|(b, r)| match b {
        Button::Member(id) if inside(*r, mouse) => Some(*id),
        _ => None,
    });
    if let Some(p) = member.and_then(|id| world.objects.get(&id)) {
        draw_player_card(ui, lay, p, mouse);
        return;
    }
    // Players under the mouse on the minimap (`PlayerGroupToolTip`): one is shown in full.
    let near = panel.minimap_players_at(mouse, lay.s);
    if let [(id, _, _)] = near.as_slice() {
        if let Some(p) = world.objects.get(id) {
            draw_player_card(ui, lay, p, mouse);
            return;
        }
    }
    if !near.is_empty() {
        let mut lines = vec![Line::new("Players", 12.0, true, color::TEXT_DIM)];
        lines.extend(near.iter().map(|(_, name, _)| Line::new(name, 14.0, true, 0xFFFF00)));
        lines.push(Line::new("Click for options", 11.0, false, color::TEXT_MUTED));
        draw_tooltip(ui, lay, &lines, None, None, mouse);
        return;
    }
    if let Some((r, item)) = panel.shop_icon {
        if item > 0 && inside(r, mouse) {
            let lines = item_tooltip(ui.data, item, world.player(), SlotKind::Shop);
            draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
            return;
        }
    }
    let Some(slot) = panel.slot_at(mouse) else { return };
    let me = world.player();
    let lines = if slot.item > 0 {
        item_tooltip(ui.data, slot.item, me, slot.kind)
    } else {
        let what = match slot.kind {
            SlotKind::Equip(t) => slot_type_name(t),
            _ => "item",
        };
        vec![Line::new(&format!("Empty {what} Slot"), 14.0, false, 0x9B9B9B)]
    };
    let accent = if slot.item > 0 { crate::screens::tier_edge(ui.data, slot.item) } else { None };
    draw_tooltip(ui, lay, &lines, if slot.item > 0 { Some(slot.item) } else { None }, accent, mouse);
}

/// `PlayerToolTip`: a player's name, guild, level and class, health and magic, and the four
/// items they wear, in a box beside the mouse.
fn draw_player_card(ui: &mut Ui, lay: &Layout, p: &GameObj, mouse: (f64, f64)) {
    let s = lay.s;
    let (w, h) = (230.0 * s, 150.0 * s);
    let pad = 10.0 * s;
    let (mut x0, mut y0) = (mouse.0 as f32 + 16.0 * s, mouse.1 as f32 + 8.0 * s);
    if x0 + w > lay.w {
        x0 = mouse.0 as f32 - w - 8.0 * s;
    }
    if y0 + h > lay.h {
        y0 = (lay.h - h).max(0.0);
    }
    let r = [x0.max(0.0), y0, w, h];
    theme::shadow(ui.renderer, r, 8.0 * s, 12.0 * s, 0.5);
    theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (color::BORDER_HI, 0.9), ((color::PANEL_TOP, 0.97), (color::PANEL_BOTTOM, 0.98)));
    let (x, y) = (r[0] + pad, r[1] + pad);
    let stat = |id: u8| p.stat_int(id).unwrap_or(0);
    let name = p.name().unwrap_or("?");
    let chosen = stat(56) != 0;
    ui.label(name, x, y, lay.font(15.0), true, if chosen { 0xFCDF00 } else { 0xFFFFFF });
    // Their rank (`RankText`), at the top right.
    let stars = stat(30);
    if stars >= 0 {
        let t = stars.to_string();
        let size = lay.font(13.0);
        let tw = ui.measure(&t, size, true);
        let sx = r[0] + w - pad - 8.0 * s;
        crate::stars::icon(ui.renderer, sx, y + 9.0 * s, s, crate::stars::color(stars, crate::stars::class_count(ui.data)));
        ui.label(&t, sx - 12.0 * s - tw, y + 1.0 * s, size, true, crate::stars::GREY);
    }
    let class = ui.data.objects.get(&p.object_type).map_or("", |d| d.display_id.as_str()).to_owned();
    let info = format!("Level {} {class}", stat(stat::LEVEL));
    ui.label(&info, x, y + 20.0 * s, lay.font(11.5), false, color::TEXT_DIM);
    let mut by = y + 36.0 * s;
    if let Some(guild) = p.stat_str(62).filter(|g| !g.is_empty()) {
        ui.label(guild, x, by, lay.font(11.0), true, 0xA6FF5D);
        by += 15.0 * s;
    }
    let bar = |ui: &mut Ui, y: f32, label: &str, value: i32, max: i32, c: u32| {
        let br = [x, y, w - 2.0 * pad, 14.0 * s];
        theme::fill(ui.renderer, br, 4.0 * s, color::TRACK, 1.0);
        let frac = if max > 0 { (value as f32 / max as f32).clamp(0.0, 1.0) } else { 0.0 };
        if frac > 0.0 {
            theme::fill(ui.renderer, [br[0], br[1], br[2] * frac, br[3]], 4.0 * s, c, 1.0);
        }
        let size = lay.font(10.0);
        let ty = y + (br[3] - size * 1.22) / 2.0;
        ui.label(label, br[0] + 5.0 * s, ty, size, true, color::TEXT);
        let t = format!("{value} / {max}");
        let tw = ui.measure(&t, size, true);
        ui.label(&t, br[0] + br[2] - tw - 5.0 * s, ty, size, true, color::TEXT);
    };
    bar(ui, by, "HP", stat(stat::HP), stat(stat::MAX_HP), 0xE03434);
    bar(ui, by + 18.0 * s, "MP", stat(stat::MP), stat(stat::MAX_MP), 0x3A6FE0);
    // The equipment, in slots.
    let sy = by + 40.0 * s;
    for i in 0..4u8 {
        let sr = [x + i as f32 * 44.0 * s, sy, 40.0 * s, 40.0 * s];
        let item = p.stat_int(slot_stat(i)).unwrap_or(-1);
        ui.slot_box(sr, if item > 0 { crate::screens::tier_edge(ui.data, item) } else { None }, false, s);
        if item > 0 {
            ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
        }
    }
}

/// One line of a tooltip.
pub struct Line {
    text: String,
    size: f32,
    bold: bool,
    color: u32,
}

impl Line {
    fn new(text: &str, size: f32, bold: bool, color: u32) -> Self {
        Line { text: text.to_owned(), size, bold, color }
    }
}

const TOOLTIP_W: f32 = 230.0;
const ATTR: u32 = 0xB3B3B3;
const VALUE: u32 = 0xFFFF8F;

fn tier_tag(xml: &bsk_data::xml::Element) -> Option<(String, u32)> {
    let hidden = ["Consumable", "NoTierTag", "Treasure", "PetFood"];
    if hidden.iter().any(|t| xml.has(t)) {
        return None;
    }
    let color = [("Admin", 0xFC65D0), ("Ethereal", 0xF61D1D), ("Ascended", 0x1BF7DD), ("Legendary", 0xFFC800)]
        .iter()
        .find(|(t, _)| xml.has(t))
        .map(|(_, c)| *c);
    if let Some(tier) = xml.text_of("Tier") {
        Some((format!("T{}", tier.trim()), color.unwrap_or(0xFFFFFF)))
    } else if xml.attr("setType").is_some() {
        Some(("ST".into(), color.unwrap_or(0xF2FF00)))
    } else {
        Some(("UT".into(), color.unwrap_or(0xE22BC9)))
    }
}

fn stat_name(id: i32) -> &'static str {
    match id {
        0 => "Maximum HP",
        3 => "Maximum MP",
        20 => "Attack",
        21 => "Defense",
        22 => "Speed",
        26 => "Vitality",
        27 => "Wisdom",
        28 => "Dexterity",
        _ => "Stat",
    }
}

/// `EquipmentToolTip`, the parts that matter: title and tier, description, weapon numbers,
/// ability costs, stat bonuses, and how to use it.
/// What an item's tooltip compares with another's (`EquipmentToolTip`'s comparisons).
#[derive(Default)]
struct ItemNumbers {
    /// An item was there to compare with.
    present: bool,
    shots: Option<f64>,
    damage: Option<f64>,
    range: Option<f64>,
    rate_of_fire: Option<f64>,
    mp_cost: Option<f64>,
    cooldown: Option<f64>,
    fame_bonus: Option<f64>,
    boosts: Vec<(i32, i32)>,
}

fn item_numbers(desc: &ObjectDesc) -> ItemNumbers {
    let xml = &desc.xml;
    let mut n = ItemNumbers { present: true, ..Default::default() };
    if let Some(p) = desc.projectiles.get(&0) {
        n.shots = Some(desc.num_projectiles() as f64);
        n.damage = Some((p.min_damage + p.max_damage) as f64 / 2.0);
        n.range = Some(p.speed as f64 * p.lifetime as f64 / 10000.0);
        n.rate_of_fire = Some(desc.rate_of_fire());
    }
    if xml.has("Usable") {
        n.mp_cost = xml.text_of("MpCost").and_then(|c| c.trim().parse().ok());
        n.cooldown = Some(xml.number("Cooldown", 0.5));
    }
    n.fame_bonus = xml.text_of("FameBonus").and_then(|f| f.trim().parse().ok());
    n.boosts = xml
        .children_named("ActivateOnEquip")
        .filter(|a| a.text.trim() == "IncrementStat")
        .map(|b| (b.attr_int("stat", -1), b.attr_int("amount", 0)))
        .collect();
    n
}

/// Better than what's worn: green; worse: red; the same, or nothing to compare: plain.
const BETTER: u32 = 0x00FF00;
const WORSE: u32 = 0xFF0000;

fn compare(mine: Option<f64>, worn: Option<f64>, higher_is_better: bool) -> u32 {
    match (mine, worn) {
        (Some(a), Some(b)) if (a - b).abs() > 1e-9 => {
            if (a > b) == higher_is_better {
                BETTER
            } else {
                WORSE
            }
        }
        _ => VALUE,
    }
}

/// The item worn in the slot this item would go in, if it's another item.
fn worn_for(data: &GameData, item: i32, me: Option<&GameObj>, kind: SlotKind) -> Option<i32> {
    if matches!(kind, SlotKind::Equip(_)) {
        return None;
    }
    let me = me?;
    let slot_type = data.objects.get(&(item as u16))?.xml.int("SlotType", 0);
    let types = class_slot_types(data.objects.get(&me.object_type));
    let i = types.iter().take(4).position(|&t| t == slot_type && t != 0)?;
    me.stat_int(slot_stat(i as u8)).filter(|&w| w > 0 && w != item)
}

pub fn item_tooltip(data: &GameData, item: i32, me: Option<&GameObj>, kind: SlotKind) -> Vec<Line> {
    let Some(desc) = data.objects.get(&(item as u16)) else { return vec![Line::new("Unknown item", 14.0, true, 0xFFFFFF)] };
    let xml = &desc.xml;
    let mut out = Vec::new();
    // Compared with what's worn in its slot.
    let mine = item_numbers(desc);
    let worn = worn_for(data, item, me, kind).and_then(|w| data.objects.get(&(w as u16))).map(item_numbers).unwrap_or_default();
    let title_color = [("Admin", 0xFF8BDE), ("Ethereal", 0xFF5A5A), ("Ascended", 0x66FFF0), ("Legendary", 0xFFE066)]
        .iter()
        .find(|(t, _)| xml.has(t))
        .map_or(0xFFFFFF, |(_, c)| *c);
    out.push(Line::new(&desc.display_id, 16.0, true, title_color));
    if let Some((tag, color)) = tier_tag(xml) {
        out.push(Line::new(&tag, 14.0, true, color));
    }
    if let Some(d) = xml.lines_of("Description") {
        // One line each, an empty one between paragraphs.
        for l in d.lines() {
            out.push(Line::new(l.trim(), if l.trim().is_empty() { 6.0 } else { 13.0 }, false, ATTR));
        }
    }
    let mut attr = |label: &str, value: String, color: u32| out.push(Line::new(&format!("{label}: {value}"), 13.0, false, color));
    if let Some(p) = desc.projectiles.get(&0) {
        attr("Shots", desc.num_projectiles().to_string(), compare(mine.shots, worn.shots, true));
        let dmg = if p.min_damage == p.max_damage { p.min_damage.to_string() } else { format!("{} - {}", p.min_damage, p.max_damage) };
        attr("Damage", dmg, compare(mine.damage, worn.damage, true));
        let range = p.speed as f64 * p.lifetime as f64 / 10000.0;
        attr("Range", format!("{}", (range * 100.0).round() / 100.0), compare(mine.range, worn.range, true));
        if [1, 2, 3, 8, 17, 24].contains(&xml.int("SlotType", 0)) {
            attr("Rate of Fire", format!("{:.0}%", desc.rate_of_fire() * 100.0), compare(mine.rate_of_fire, worn.rate_of_fire, true));
        }
        for e in &p.effects {
            attr("Shot effect", e.clone(), VALUE);
        }
    }
    if xml.has("Usable") {
        if let Some(cost) = xml.text_of("MpCost") {
            attr("MP Cost", cost.trim().to_owned(), compare(mine.mp_cost, worn.mp_cost, false));
        }
        attr("Cooldown", format!("{} secs", xml.number("Cooldown", 0.5)), compare(mine.cooldown, worn.cooldown, false));
    }
    if let Some(doses) = xml.text_of("Doses") {
        attr("Doses", doses.trim().to_owned(), VALUE);
    }
    if let Some(fame) = xml.text_of("FameBonus") {
        attr("Fame Bonus", format!("{}%", fame.trim()), compare(mine.fame_bonus, worn.present.then(|| worn.fame_bonus.unwrap_or(0.0)), true));
    }
    if !mine.boosts.is_empty() {
        out.push(Line::new("On Equip:", 13.0, false, ATTR));
        let comparing = worn.present;
        for &(stat, amount) in &mine.boosts {
            let color = if comparing {
                // Against what the worn item gives of the same stat (none: 0).
                let other = worn.boosts.iter().filter(|b| b.0 == stat).map(|b| b.1).sum::<i32>();
                match amount.cmp(&other) {
                    std::cmp::Ordering::Greater => BETTER,
                    std::cmp::Ordering::Less => WORSE,
                    std::cmp::Ordering::Equal => VALUE,
                }
            } else if amount < 0 {
                WORSE
            } else {
                BETTER
            };
            out.push(Line::new(&format!("    {amount:+} {}", stat_name(stat)), 13.0, false, color));
        }
    }
    for a in xml.children_named("Activate") {
        let effect = a.text.trim();
        if effect == "PermaPet" {
            // The port writes the pet's damage on one enemy and its healing here.
            out.push(Line::new("Gives a permanent pet", 13.0, false, VALUE));
            if let Some(dps) = a.attr("petDps") {
                out.push(Line::new(&format!("    Pet damage: {dps} per second"), 13.0, false, VALUE));
            }
            for (key, verb, what) in [("petHp", "heals", "HP"), ("petMp", "restores", "MP")] {
                if let Some(amount) = a.attr(key) {
                    let secs = a.attr_number(&format!("{key}Ms"), 0.0) / 1000.0;
                    out.push(Line::new(&format!("    Pet {verb} {amount} {what} every {secs}s"), 13.0, false, VALUE));
                }
            }
            continue;
        }
        // Bullet and shot activations some items mark as not for the tooltip.
        if a.attr("ignoreOnTooltip") == Some("true") {
            continue;
        }
        out.push(Line::new(&activate_detail(a), 13.0, false, VALUE));
    }
    // Restrictions.
    let mut note = |t: &str, c: u32| out.push(Line::new(t, 13.0, false, c));
    if xml.has("Consumable") {
        note("Consumed with use", ATTR);
    }
    if kind == SlotKind::Shop {
        // Nothing to do with it until it's bought.
    } else if xml.has("Usable") && matches!(kind, SlotKind::Equip(_)) {
        note("Press [Space] in the world to use", 0xFFFFFF);
    } else if xml.has("Consumable") || xml.has("InvUse") {
        note("Double-click or shift-click to use", 0xFFFFFF);
    } else if kind == SlotKind::Container {
        note("Double-click to take", ATTR);
    } else if !matches!(kind, SlotKind::Equip(_)) && xml.has("SlotType") && xml.int("SlotType", 0) != 10 {
        note("Double-click to equip", ATTR);
    }
    if xml.has("Soulbound") {
        note("Soulbound", ATTR);
    }
    if let Some(me) = me {
        let slot_type = xml.int("SlotType", 0);
        let equips = class_slot_types(data.objects.get(&me.object_type));
        if (1..=24).contains(&slot_type) && slot_type != 10 && !equips.iter().take(4).any(|&t| t == slot_type) {
            let class = data.objects.get(&me.object_type).map_or("you", |c| c.display_id.as_str());
            note(&format!("Not usable by {class}"), 0xFC8642);
        }
    }
    out
}

/// What an `<Activate>` does, in words, with its numbers (`EquipmentToolTip`'s effect lines). Every
/// ability the items use is covered; anything else shows its name with spaces.
fn activate_detail(a: &bsk_data::xml::Element) -> String {
    let effect = a.text.trim();
    let num = |k: &str| a.attr_number(k, 0.0);
    let has = |k: &str| a.attr(k).is_some();
    let text = |k: &str| a.attr(k).unwrap_or("").to_owned();
    // Most abilities have a radius in "radius", some in "range".
    let area = || if has("radius") { num("radius") } else { num("range") };
    let mut s = match effect {
        "Heal" => format!("Heals {} HP", num("amount")),
        "Magic" => format!("Restores {} MP", num("amount")),
        "HealNova" => format!("Heals {} HP to you and allies within {} sqrs", num("amount"), num("range")),
        "MagicNova" => format!("Restores {} MP to you and allies within {} sqrs", num("amount"), num("range")),
        "IncrementStat" => format!("Permanently +{} {}", a.attr_int("amount", 0), stat_name(a.attr_int("stat", -1))),
        // Poison ignores defense (the server's `PoisonEnemy`).
        "PoisonGrenade" => {
            let mut s = format!("Poison: {} damage over {} secs, within {} sqrs", num("totalDamage"), num("duration"), num("radius"));
            if num("impactDamage") > 0.0 {
                s += &format!(", plus {} on impact", num("impactDamage"));
            }
            s
        }
        "VampireBlast" => format!("Steals {} HP from each enemy within {} sqrs, healing you and allies nearby", num("totalDamage"), num("radius")),
        "Trap" => format!(
            "Trap: {} damage within {} sqrs, and {} for {} secs",
            num("totalDamage"),
            num("radius"),
            a.attr("condEffect").unwrap_or("Slowed"),
            if has("condDuration") { num("condDuration") } else { 5.0 }
        ),
        "Lightning" => {
            let mut s = format!("Lightning: {} damage, to up to {} enemies", num("totalDamage"), num("maxTargets"));
            if num("decrDamage") > 0.0 {
                s += &format!(", {} less for each after the first", num("decrDamage"));
            }
            s
        }
        "StasisBlast" => format!("Puts enemies within {} sqrs in stasis for {} secs", if has("radius") { num("radius") } else { 3.0 }, num("duration")),
        "Decoy" | "MultiDecoy" => format!("A decoy that draws enemies for {} secs", num("duration")),
        "Teleport" => format!("Teleports you up to {} sqrs", num("maxDistance")),
        "StatBoostSelf" => format!("+{} {} for {} secs", num("amount"), stat_name(a.attr_int("stat", -1)), num("duration")),
        "StatBoostAura" => format!("+{} {} to you and allies within {} sqrs for {} secs", num("amount"), stat_name(a.attr_int("stat", -1)), num("range"), num("duration")),
        "ConditionEffectSelf" => format!("{} for {} secs", text("effect"), num("duration")),
        "ConditionEffectAura" => format!("{} to you and allies within {} sqrs for {} secs", text("effect"), num("range"), num("duration")),
        "GenericActivate" => {
            let who = if a.attr("target") == Some("enemy") { "enemies" } else { "allies" };
            format!("{} to {who} within {} sqrs for {} secs", text("effect"), num("range"), num("duration"))
        }
        "Halo" => format!("Heals {} HP to allies within {} sqrs, for {} secs", num("amount"), num("range"), num("duration")),
        "BulletNova" => format!("Fires a ring of {} shots", if has("numShots") { num("numShots") } else { 20.0 }),
        "BulletCreate" => format!("Fires {} shots", num("numShots")),
        "Shoot" => "Fires its shots".to_owned(),
        "SamuraiAbility" | "DazeBlast" | "TomeDamage" => format!("{} damage within {} sqrs", num("totalDamage"), area()),
        "ObjectToss" => format!("Throws a {}", text("objectId")),
        "MonsterToss" => format!("Throws a {} to fight for you", text("objectId")),
        "Summon" => format!("Summons a {} for {} secs", text("objectId"), num("duration")),
        "BuildTower" => format!("Builds a {} for {} secs ({} damage)", text("objectId"), num("duration"), num("totalDamage")),
        "PearlAbility" => format!("Summons a {}", text("objectId")),
        "Pet" => format!("Summons the pet {}", text("objectId")),
        "RemoveNegativeConditions" => format!("Removes negative conditions from allies within {} sqrs", num("range")),
        "RemoveNegativeConditionsSelf" => "Removes your negative conditions".to_owned(),
        "ClearConditionEffectAura" => format!("Removes {} from allies within {} sqrs", text("effect"), num("range")),
        "ClearConditionEffectSelf" => format!("Removes {}", text("effect")),
        "Fame" => format!("+{} fame", num("amount")),
        "Gold" => format!("+{} gold", num("amount")),
        "CreatePortal" | "Create" => format!("Opens {}", text("id")),
        "Exchange" => format!("Gives you a {}", text("id")),
        "MysteryDyes" => "Gives you one of the items listed".to_owned(),
        "Backpack" => "Gives you a backpack".to_owned(),
        "UnlockSkin" => "Unlocks a skin".to_owned(),
        "UnlockPetSkin" => "Unlocks a pet skin".to_owned(),
        _ => {
            // "VampireBlast" -> "Vampire Blast"
            let mut words = String::new();
            for (i, c) in effect.chars().enumerate() {
                if i > 0 && c.is_uppercase() {
                    words.push(' ');
                }
                words.push(c);
            }
            words
        }
    };
    if let (Some(cond), false) = (a.attr("condEffect"), effect == "Trap") {
        s += &format!(", and {cond} for {} secs", num("condDuration"));
    }
    if a.attr("useWisMod") == Some("true") {
        s += " (raised by Wisdom)";
    }
    s
}

/// A tooltip box beside the mouse, kept inside the window: the item's icon and title on top,
/// then the lines wrapped to the box.
fn draw_tooltip(ui: &mut Ui, lay: &Layout, lines: &[Line], icon: Option<i32>, accent: Option<u32>, mouse: (f64, f64)) {
    let s = lay.s;
    let w = TOOLTIP_W * s;
    let pad = 10.0 * s;
    let inner = w - 2.0 * pad;
    let icon_w = if icon.is_some() { 44.0 * s } else { 0.0 };
    // Wrap every line; the first ones (title, tier) sit beside the icon.
    let mut rows: Vec<(String, f32, bool, u32, f32)> = Vec::new();
    let mut y: f32 = 0.0;
    for (i, line) in lines.iter().enumerate() {
        let size = lay.font(line.size);
        let indent = if i < 2 { icon_w } else { 0.0 };
        if i == 2 {
            y = y.max(icon_w) + 4.0 * s;
        }
        for part in wrap(ui, &[(line.text.clone(), line.color)], size, inner - indent, 0.0) {
            let text: String = part.into_iter().map(|(t, _)| t).collect();
            rows.push((text, size, line.bold, line.color, y));
            y += size * 1.25;
        }
    }
    let h = y.max(icon_w) + 2.0 * pad;
    let (mut x0, mut y0) = (mouse.0 as f32 + 16.0 * s, mouse.1 as f32 + 8.0 * s);
    if x0 + w > lay.w {
        x0 = mouse.0 as f32 - w - 8.0 * s;
    }
    if y0 + h > lay.h {
        y0 = (lay.h - h).max(0.0);
    }
    x0 = x0.max(0.0);
    let r = [x0, y0, w, h];
    theme::shadow(ui.renderer, r, 8.0 * s, 12.0 * s, 0.5);
    let edge = accent.unwrap_or(color::BORDER_HI);
    theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (edge, 0.9), ((color::PANEL_TOP, 0.97), (color::PANEL_BOTTOM, 0.98)));
    if let Some(c) = accent {
        theme::rounded(ui.renderer, [x0 + s, y0 + s, w - 2.0 * s, 40.0 * s], 7.0 * s, (c, 0.22), (c, 0.0));
    }
    if let Some(item) = icon {
        ui.item_icon(item, 60, x0 + pad + icon_w / 2.0 - 2.0 * s, y0 + pad + icon_w / 2.0 - 2.0 * s, s * 0.9);
    }
    for (i, (text, size, bold, color, ry)) in rows.iter().enumerate() {
        let indent = if i < 2 && icon.is_some() && *ry < icon_w { icon_w } else { 0.0 };
        ui.label(text, x0 + pad + indent, y0 + pad + ry, *size, *bold, *color);
    }
}

/// The trade window (`TradePanel`) and the player menu (`PlayerMenu`), over the view.
pub fn draw_windows(ui: &mut Ui, lay: &Layout, world: &World, panel: &Panel, social: &Social, mouse: (f64, f64)) {
    let s = lay.s;
    if let (Some(r), Some(trade)) = (panel.trade_rect, social.trade) {
        theme::panel(ui.renderer, r, s);
        let title = format!("Trading with {}", trade.partner);
        ui.label(&title, r[0] + 20.0 * s, r[1] + 16.0 * s, lay.font(18.0), true, color::TEXT);
        let (my_room, their_room) = trade.space_ok();
        let mut hovered = None;
        for (items, offer, x0, mine) in [(&trade.mine, &trade.my_offer, r[0] + 20.0 * s, true), (&trade.theirs, &trade.their_offer, r[0] + 230.0 * s, false)] {
            let (label, note, note_c) = if mine {
                let (n, c) = if !my_room {
                    ("Not enough space!", color::DANGER)
                } else if trade.i_accepted {
                    ("Trade accepted!", color::GOOD)
                } else {
                    ("Click the items to offer", color::TEXT_DIM)
                };
                ("Your offer", n, c)
            } else {
                let (n, c) = if !their_room {
                    ("Not enough space!", color::DANGER)
                } else if trade.they_accepted {
                    ("Trade accepted!", color::GOOD)
                } else {
                    ("Choosing items…", color::TEXT_DIM)
                };
                ("Their offer", n, c)
            };
            ui.label(label, x0, r[1] + 48.0 * s, lay.font(13.0), true, color::TEXT_DIM);
            for (i, item) in items.iter().take(12).enumerate() {
                let sr = [x0 + (i % 4) as f32 * 46.0 * s, r[1] + 70.0 * s + (i / 4) as f32 * 46.0 * s, 42.0 * s, 42.0 * s];
                let on = offer.get(i).copied().unwrap_or(false);
                let over = inside(sr, mouse);
                let clickable = mine && item.tradeable && item.item > 0;
                let edge = if on { Some(color::GOOD) } else if item.item > 0 { crate::screens::tier_edge(ui.data, item.item) } else { None };
                ui.slot_box(sr, edge, over && clickable, s);
                if on {
                    theme::fill(ui.renderer, [sr[0] + s, sr[1] + s, sr[2] - 2.0 * s, sr[3] - 2.0 * s], 4.0 * s, color::GOOD, 0.18);
                }
                if item.item > 0 {
                    let dim = !item.tradeable;
                    ui.item_icon(item.item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
                    if dim {
                        theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.5);
                    }
                    if over {
                        hovered = Some(item.item);
                    }
                }
            }
            ui.label(note, x0, r[1] + 212.0 * s, lay.font(12.0), true, note_c);
        }
        // Accept waits a moment after any change, then needs room on both sides.
        let wait = (crate::game::TRADE_ACCEPT_DELAY - (social.now - trade.changed_at)).max(0);
        for (b, br) in &panel.buttons {
            match b {
                Button::TradeCancel => ui.button(*br, "Cancel", Kind::Secondary, inside(*br, mouse), s),
                Button::TradeAccept => {
                    let ready = wait == 0 && my_room && their_room && !trade.i_accepted;
                    let label = if trade.i_accepted { "Accepted".to_owned() } else if wait > 0 { format!("Accept ({})", (wait + 999) / 1000) } else { "Accept".to_owned() };
                    ui.button(*br, &label, if ready { Kind::Primary } else { Kind::Secondary }, ready && inside(*br, mouse), s);
                }
                _ => {}
            }
        }
        if let Some(item) = hovered {
            let lines = item_tooltip(ui.data, item, world.player(), SlotKind::Shop);
            draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }
    if let Some(r) = panel.menu_rect {
        theme::shadow(ui.renderer, r, 8.0 * s, 10.0 * s, 0.5);
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::PANEL_TOP, 0.98), (color::PANEL_BOTTOM, 0.98)));
        let name = panel
            .buttons
            .iter()
            .find_map(|(b, _)| if let Button::Menu(_, id) = b { Some(*id) } else { None })
            .and_then(|id| world.objects.get(&id))
            .and_then(|o| o.name())
            .unwrap_or("")
            .to_owned();
        ui.label(&name, r[0] + 10.0 * s, r[1] + 7.0 * s, lay.font(14.0), true, color::ACCENT_HI);
        for (b, br) in &panel.buttons {
            let Button::Menu(opt, _) = b else { continue };
            let over = inside(*br, mouse);
            if over {
                theme::fill(ui.renderer, *br, 4.0 * s, color::ACCENT, 0.22);
            }
            let size = lay.font(13.0);
            let c = if matches!(opt, MenuOpt::Ignore) { 0xFF8A80 } else { color::TEXT };
            ui.label(opt.label(), br[0] + 8.0 * s, br[1] + (br[3] - size * 1.22) / 2.0, size, false, c);
        }
    }
}

/// `QuestArrow` and `BossHealthBar`: the quest's health along the top of the view, and an arrow
/// at the view's edge pointing to it when it's off screen. Then the dungeon keys (`KeysView`).
/// The dungeon keys, the boss bar (with the share of its health we took, `DamageCounter`) and
/// the quest arrow.
#[allow(clippy::too_many_arguments)]
pub fn draw_quest(
    ui: &mut Ui,
    lay: &Layout,
    world: &World,
    camera: &crate::view::Camera,
    quest: Option<i32>,
    keys: &[u32],
    dealt: &HashMap<i32, i32>,
    top: Option<&(i32, Vec<(String, i32)>, i32)>,
) {
    let s = lay.s;
    for (i, &c) in keys.iter().enumerate() {
        // A key: a ring and a shaft, in the key's colour.
        let (x, y) = (12.0 * s + i as f32 * 26.0 * s, 12.0 * s);
        theme::fill(ui.renderer, [x - 3.0 * s, y - 3.0 * s, 24.0 * s, 24.0 * s], 6.0 * s, 0x000000, 0.45);
        theme::fill(ui.renderer, [x, y + 4.0 * s, 10.0 * s, 10.0 * s], 5.0 * s, c, 1.0);
        theme::fill(ui.renderer, [x + 8.0 * s, y + 7.0 * s, 10.0 * s, 4.0 * s], 1.0, c, 1.0);
        theme::fill(ui.renderer, [x + 14.0 * s, y + 11.0 * s, 3.0 * s, 4.0 * s], 1.0, c, 1.0);
    }
    let Some(q) = quest.and_then(|id| world.objects.get(&id)) else { return };
    let Some(desc) = ui.data.objects.get(&q.object_type) else { return };
    let (hp, max) = (q.stat_int(stat::HP).unwrap_or(0), q.stat_int(stat::MAX_HP).unwrap_or(0));
    let vw = lay.view_w();
    // The bar.
    if max > 0 {
        let (w, h) = (320.0 * s, 20.0 * s);
        let r = [(vw - w) / 2.0 + 20.0 * s, 26.0 * s, w, h];
        theme::shadow(ui.renderer, r, 6.0 * s, 8.0 * s, 0.4);
        theme::frame(ui.renderer, r, 6.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::TRACK, 0.9), (theme::shade(color::TRACK, 0.1), 0.9)));
        let frac = (hp as f32 / max as f32).clamp(0.0, 1.0);
        if frac > 0.0 {
            let c = if frac > 0.5 { 0x3FC04A } else if frac > 0.25 { 0xE0B030 } else { 0xE0403E };
            let inner = [r[0] + s, r[1] + s, (r[2] - 2.0 * s) * frac, r[3] - 2.0 * s];
            theme::rounded(ui.renderer, inner, 5.0 * s, (theme::shade(c, 0.25), 1.0), (theme::shade(c, -0.2), 1.0));
        }
        let size = lay.font(12.0);
        let t = format!("{hp} / {max}");
        let tw = ui.measure(&t, size, true);
        ui.label(&t, r[0] + (r[2] - tw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, color::TEXT);
        let name = q.name().unwrap_or(&desc.display_id).to_owned();
        let ns = lay.font(13.0);
        let nw = ui.measure(&name, ns, true);
        ui.label(&name, r[0] + (r[2] - nw) / 2.0, r[1] - ns * 1.35, ns, true, color::ACCENT_HI);
        // Who did the most of it (the server's count, when it sends one), then how much we did
        // (BossHealthBar.setDamageInflicted).
        let pct = |d: i32| (d.clamp(0, max) as f32 / max as f32 * 100.0).floor() as i32;
        let top = top.filter(|t| t.0 == q.id);
        let me = world.player().and_then(|p| p.name()).unwrap_or("");
        let mut rows: Vec<(String, u32)> = top
            .map(|t| {
                t.1.iter()
                    .enumerate()
                    .map(|(i, (name, d))| (format!("{}. {name}  {}%", i + 1, pct(*d)), if name == me { color::ACCENT_HI } else { color::TEXT }))
                    .collect()
            })
            .unwrap_or_default();
        let mine = top.map(|t| t.2).filter(|&d| d > 0).or_else(|| dealt.get(&q.id).copied().filter(|&d| d > 0));
        if let Some(d) = mine {
            rows.push((format!("You: {}%", pct(d)), color::ACCENT_HI));
        }
        if !rows.is_empty() {
            let ds = lay.font(11.0);
            let row_h = ds * 1.35;
            let tw = rows.iter().map(|(t, _)| ui.measure(t, ds, true)).fold(0.0, f32::max);
            let tag = [r[0] + (r[2] - tw) / 2.0 - 8.0 * s, r[1] + r[3] + 3.0 * s, tw + 16.0 * s, row_h * rows.len() as f32 + 4.0 * s];
            theme::fill(ui.renderer, tag, 8.0 * s, 0x000000, 0.5);
            for (i, (t, c)) in rows.iter().enumerate() {
                let w = ui.measure(t, ds, true);
                ui.label(t, r[0] + (r[2] - w) / 2.0, tag[1] + 2.0 * s + row_h * i as f32 + ds * 0.05, ds, true, *c);
            }
        }
        if let Some(src) = ui.src(&desc.texture) {
            if let Some(icon) = ui.redrawer.get(ui.sprites, ui.renderer, src, false, 60, None) {
                let (iw, ih) = (icon.w as f32 * s, icon.h as f32 * s);
                ui.renderer.sprite(icon.rect, Quad::rect(r[0] - iw - 2.0 * s, r[1] + r[3] / 2.0 - ih / 2.0, iw, ih), Tint::NONE);
            }
        }
    }
    // The arrow, when it's off screen.
    let [px, py] = camera.to_screen(q.x, q.y, 0.0);
    let margin = 26.0 * s;
    let (l, t, r, b) = (margin, margin + 60.0 * s, vw - margin, lay.h - margin);
    if px >= l && px <= r && py >= t && py <= b {
        return;
    }
    let (cx, cy) = (camera.cx as f32, camera.cy as f32);
    let (dx, dy) = (px - cx, py - cy);
    // Where the line from us to it leaves the inset view.
    let mut k = 1.0f32;
    if dx > 0.0 { k = k.min((r - cx) / dx); }
    if dx < 0.0 { k = k.min((l - cx) / dx); }
    if dy > 0.0 { k = k.min((b - cy) / dy); }
    if dy < 0.0 { k = k.min((t - cy) / dy); }
    let (ax, ay) = (cx + dx * k, cy + dy * k);
    let a = dy.atan2(dx);
    let (ca, sa) = (a.cos(), a.sin());
    let tri = |len: f32, wide: f32| -> [[f32; 2]; 3] {
        [[ax + ca * len, ay + sa * len], [ax - sa * wide, ay + ca * wide], [ax + sa * wide, ay - ca * wide]]
    };
    let white = ui.renderer.white();
    ui.renderer.triangle(white, tri(16.0 * s, 11.0 * s), [[0.5, 0.5]; 3], Tint::color(0x5A1010, 1.0));
    ui.renderer.triangle(white, tri(13.0 * s, 8.5 * s), [[0.5, 0.5]; 3], Tint::color(0xF98441, 1.0));
    if let Some(src) = ui.src(&desc.texture) {
        if let Some(icon) = ui.redrawer.get(ui.sprites, ui.renderer, src, false, 50, None) {
            let (iw, ih) = (icon.w as f32 * s, icon.h as f32 * s);
            let (ix, iy) = (ax - ca * 26.0 * s - iw / 2.0, ay - sa * 26.0 * s - ih / 2.0);
            ui.renderer.sprite(icon.rect, Quad::rect(ix, iy, iw, ih), Tint::NONE);
        }
    }
}

/// One chat line (`TextBoxLine`).
#[derive(Debug, Clone)]
pub struct ChatLine {
    pub name: String,
    pub recipient: String,
    pub text: String,
    pub name_color: i32,
    pub text_color: i32,
    /// The speaker's rank (`TextBoxLine.rankIcon_`), or -1 for none.
    pub stars: i32,
    /// When it arrived, ms.
    pub at: i32,
}

impl ChatLine {
    /// `getTextBlock`: the pieces and their colours. `me` is our name (for "To:" lines).
    pub fn pieces(&self, me: &str) -> Vec<(String, u32)> {
        let mut name = self.name.clone();
        let mut name_color = if self.name_color == 0x12_3456 {
            0x00FE00
        } else if self.name_color != 0 {
            self.name_color as u32
        } else {
            0x00FF00
        };
        let mut text_color = if self.text_color != 0 { self.text_color as u32 } else { 0xFFFFFF };
        match self.name.as_str() {
            "" => text_color = 0xFFFF00,
            "&" => text_color = 0xCCBB00,
            "*Client*" => text_color = 0x0000FF,
            "*Help*" => text_color = 0xFF5B05,
            "*Error*" => text_color = 0xFF0000,
            _ => {}
        }
        if matches!(self.name.as_str(), "" | "&" | "*Client*" | "*Help*" | "*Error*") {
            name.clear();
        }
        if let Some(rest) = self.name.strip_prefix('#') {
            name = rest.to_owned();
            name_color = 0xFFA800;
        }
        if let Some(rest) = self.name.strip_prefix('@') {
            name = rest.to_owned();
            (name_color, text_color) = (0xFFFF00, 0xFFFF00);
        }
        let mut out = Vec::new();
        match self.recipient.as_str() {
            "" => {}
            "*Guild*" => (name_color, text_color) = (0xA6FF5D, 0xA6FF5D),
            "*Party*" => (name_color, text_color) = (0x78A0FF, 0x78A0FF),
            to => {
                (name_color, text_color) = (0x00F0FF, 0x00F0FF);
                if self.name == me {
                    out.push(("To: ".to_owned(), 0x00F0FF));
                    name = to.to_owned();
                }
            }
        }
        if !name.is_empty() {
            out.push((format!("<{name}> "), name_color));
        }
        out.push((self.text.clone(), text_color));
        out
    }
}

/// A line being typed: its text, scrolled to keep the cursor in sight, the selection behind it
/// and a blinking cursor, in `r` (x, y, width, height).
pub fn draw_line_edit(ui: &mut Ui, e: &crate::textedit::LineEdit, r: [f32; 4], size: f32, placeholder: &str, now: i32) {
    let ty = r[1] + (r[3] - size * 1.22) / 2.0;
    if e.text.is_empty() {
        ui.label(placeholder, r[0], ty, size, false, color::TEXT_MUTED);
    }
    let chars: Vec<(usize, char)> = e.text.char_indices().collect();
    let byte = |i: usize| chars.get(i).map_or(e.text.len(), |c| c.0);
    // The first character shown: as far left as keeps the cursor inside the box.
    let mut start = 0;
    while start < e.cursor && ui.measure(&e.text[byte(start)..byte(e.cursor)], size, false) > r[2] - 4.0 {
        start += 1;
    }
    let mut end = chars.len();
    while end > e.cursor && ui.measure(&e.text[byte(start)..byte(end)], size, false) > r[2] {
        end -= 1;
    }
    let x_of = |ui: &mut Ui, i: usize| r[0] + ui.measure(&e.text[byte(start)..byte(i.clamp(start, end))], size, false);
    if let Some((a, b)) = e.selection() {
        let (xa, xb) = (x_of(ui, a), x_of(ui, b));
        if xb > xa {
            ui.rect(xa, ty - 1.0, xb - xa, size * 1.3, color::ACCENT, 0.45);
        }
    }
    ui.label(&e.text[byte(start)..byte(end)], r[0], ty, size, false, color::TEXT);
    if (now / 530) % 2 == 0 {
        let x = x_of(ui, e.cursor);
        ui.rect(x.round(), ty, (size / 14.0).max(1.0).round(), size * 1.2, color::TEXT, 1.0);
    }
}

/// A wrapped chat row: the rank star leading it (a line's first row only), and its pieces.
type ChatRow = (Option<i32>, Vec<(String, u32)>);

/// How long a chat line stays up while the input is closed, ms.
const CHAT_SHOW_MS: i32 = 30_000;

impl HudState {
    /// Open the player menu for `id` at `pos` (a name clicked in the chat).
    pub fn open_menu(&mut self, id: i32, pos: (f64, f64)) {
        self.menu = Some((id, (pos.0 as f32, pos.1 as f32)));
    }
}

/// The chat box at the bottom left of the view: the newest lines (all of them, `scroll` rows
/// back, after Page Up), wrapped to fit, and the line being typed. Returns where the players'
/// names are, for clicks.
pub fn draw_chat(ui: &mut Ui, lay: &Layout, lines: &[ChatLine], me: &str, input: Option<&crate::textedit::LineEdit>, scroll: Option<usize>, now: i32) -> Vec<(String, [f32; 4])> {
    let mut names = Vec::new();
    let s = lay.s;
    let size = lay.font(14.0);
    let line_h = (size * 1.3).round();
    let margin = 10.0 * s;
    let max_w = (lay.view_w() - 2.0 * margin - 16.0 * s).min(620.0 * s).max(120.0);
    let max_rows = ((lay.h * 0.4) / line_h).floor().max(3.0) as usize;
    let mut bottom = lay.h - margin;
    if let Some(input) = input {
        let r = [margin, bottom - line_h - 12.0 * s, max_w + 16.0 * s, line_h + 12.0 * s];
        ui.field_box(r, true, s);
        draw_line_edit(ui, input, [r[0] + 8.0 * s, r[1], max_w, r[3]], size, "Say something, or /help", now);
        bottom = r[1] - 6.0 * s;
    }
    // Wrap the newest lines into rows, newest at the bottom. A speaker's rank star leads the
    // line's first row.
    let classes = crate::stars::class_count(ui.data);
    let star_k = size / 15.0;
    let star_w = 19.0 * star_k;
    let mut rows: Vec<ChatRow> = Vec::new();
    for line in lines.iter().rev() {
        if input.is_none() && scroll.is_none() && now - line.at > CHAT_SHOW_MS {
            break;
        }
        let star = (line.stars >= 0).then_some(line.stars);
        let wrapped = wrap(ui, &line.pieces(me), size, max_w, if star.is_some() { star_w } else { 0.0 });
        for (i, row) in wrapped.into_iter().enumerate().rev() {
            rows.push((if i == 0 { star } else { None }, row));
        }
        if rows.len() >= max_rows + scroll.unwrap_or(0) {
            break;
        }
    }
    let skip = scroll.unwrap_or(0).min(rows.len().saturating_sub(max_rows.min(rows.len())));
    rows.drain(..skip);
    rows.truncate(max_rows);
    if rows.is_empty() {
        return names;
    }
    // A dark glass behind the lines, so they read over any ground.
    let widest = rows
        .iter()
        .map(|(star, row)| row.iter().map(|(p, _)| ui.measure_rich(p, size)).sum::<f32>() + if star.is_some() { star_w } else { 0.0 })
        .fold(0.0, f32::max);
    let back_h = rows.len() as f32 * line_h + 10.0 * s;
    let strong = input.is_some() || scroll.is_some();
    theme::fill(ui.renderer, [margin, bottom - back_h, widest + 16.0 * s, back_h], 6.0 * s, 0x000000, if strong { 0.55 } else { 0.32 });
    if skip > 0 {
        let t = format!("{skip} newer lines below (Page Down)");
        let small = lay.font(11.0);
        ui.label(&t, margin + 8.0 * s, bottom - back_h - small * 1.4, small, true, color::TEXT_DIM);
    }
    for (i, (star, row)) in rows.iter().enumerate() {
        let y = bottom - 5.0 * s - line_h * (i as f32 + 1.0);
        let mut x = margin + 8.0 * s;
        if let Some(stars) = *star {
            crate::stars::icon(ui.renderer, x + 8.0 * star_k, y + line_h / 2.0, star_k, crate::stars::color(stars, classes));
            x += star_w;
        }
        for (piece, c) in row {
            let w = ui.measure_rich(piece, size);
            // A player's name ("<Name> "; not NPCs' or announcements'): clickable.
            if let Some(name) = piece.strip_prefix('<').and_then(|p| p.strip_suffix("> ")) {
                if !matches!(*c, 0xFFA800 | 0xFFFF00) && !name.is_empty() && name != me {
                    names.push((name.to_owned(), [x, y, w, line_h]));
                }
            }
            ui.label_rich(piece, x, y, size, *c, now);
            x += w;
        }
    }
    names
}

/// `CreditDisplay`: your gold and fame, each before its icon, at the top right of the view,
/// under your rank and its star (`GameSprite.rankText_`) in maps that show it. Each count is
/// centred on its icon by the middle of its digits. Returns how far down it reaches.
pub fn draw_credits(ui: &mut Ui, lay: &Layout, me: &GameObj, show_rank: bool) -> f32 {
    let s = lay.s;
    let size = lay.font(16.0);
    let right = lay.view_w() - 8.0 * s;
    let row_h = 24.0 * s;
    let icon_x = right - 12.0 * s;
    let text_mid = ui.digit_middle(size, true);
    let count = |ui: &mut Ui, value: i32, cy: f32, color: u32| {
        let t = value.to_string();
        let tw = ui.measure(&t, size, true);
        ui.label(&t, right - 24.0 * s - tw, (cy - text_mid).round(), size, true, color);
    };
    let mut cy = 6.0 * s + row_h / 2.0;
    // NUM_STARS_STAT, with the star in its rank's colour.
    if let Some(stars) = me.stat_int(30).filter(|&n| n >= 0 && show_rank) {
        let classes = crate::stars::class_count(ui.data);
        // A star reaches `outer` above its centre but only 0.81 of that below, so it's drawn a
        // little low to look centred.
        let outer = 8.5 * s;
        theme::star(ui.renderer, icon_x, cy + outer * 0.095, outer, crate::stars::color(stars, classes));
        count(ui, stars, cy, crate::stars::GREY);
        cy += row_h;
    }
    // CREDITS_STAT and FAME_STAT (the account's fame), with lofiObj3's coin and fame icons.
    for (value, icon) in [(me.stat_int(35).unwrap_or(0), 225), (me.stat_int(39).unwrap_or(0), 224)] {
        ui.tile_icon("lofiObj3", icon, 40, icon_x, cy, s);
        count(ui, value, cy, 0xFFFFFF);
        cy += row_h;
    }
    cy - row_h / 2.0
}

/// `ArenaMenu`: the wave and the time, at the top left of the view.
pub fn draw_arena(ui: &mut Ui, lay: &Layout, arena: (i32, i32, i32, bool), now: i32) {
    let s = lay.s;
    let (wave, secs, since, running) = arena;
    let secs = secs + if running { (now - since).max(0) / 1000 } else { 0 };
    let r = [10.0 * s, 10.0 * s, 170.0 * s, 58.0 * s];
    theme::panel(ui.renderer, r, s);
    let small = lay.font(10.5);
    let big = lay.font(19.0);
    let cols = [("WAVE", wave.to_string()), ("TIME", format!("{:02}:{:02}", secs / 60, secs % 60))];
    for (i, (label, value)) in cols.iter().enumerate() {
        let cx = r[0] + r[2] * (i as f32 + 0.5) / 2.0;
        let lw = ui.measure(label, small, true);
        ui.label(label, cx - lw / 2.0, r[1] + 8.0 * s, small, true, color::TEXT_MUTED);
        let vw = ui.measure(value, big, true);
        ui.label(value, cx - vw / 2.0, r[1] + 24.0 * s, big, true, if i == 0 { color::ACCENT_HI } else { color::TEXT });
    }
}

/// Break coloured pieces into rows no wider than `max_w`, at spaces where possible; the first
/// row starts `indent` in.
fn wrap(ui: &mut Ui, pieces: &[(String, u32)], size: f32, max_w: f32, indent: f32) -> Vec<Vec<(String, u32)>> {
    let mut rows = vec![Vec::new()];
    let mut x = indent;
    for (piece, color) in pieces {
        for word in piece.split_inclusive(' ') {
            let w = ui.measure_rich(word, size);
            if x + w > max_w && x > 0.0 {
                rows.push(Vec::new());
                x = 0.0;
            }
            // A word longer than a row is cut wherever it runs out.
            let mut rest = word;
            while ui.measure_rich(rest, size) > max_w {
                let mut cut = 0;
                for (i, c) in rest.char_indices() {
                    if ui.measure_rich(&rest[..i + c.len_utf8()], size) > max_w {
                        break;
                    }
                    cut = i + c.len_utf8();
                }
                let cut = cut.max(rest.chars().next().map_or(1, |c| c.len_utf8()));
                rows.last_mut().unwrap().push((rest[..cut].to_owned(), *color));
                rows.push(Vec::new());
                rest = &rest[cut..];
                x = 0.0;
            }
            if !rest.is_empty() {
                rows.last_mut().unwrap().push((rest.to_owned(), *color));
                x += ui.measure_rich(rest, size);
            }
        }
    }
    rows
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn layout_fits_any_window() {
        for (w, h) in [(800, 600), (1920, 1080), (3840, 2160), (640, 1200), (1200, 400), (320, 240)] {
            let l = Layout::new(w, h);
            let panel_w = l.w - l.panel_x;
            assert!(PANEL_H * l.s <= h as f32 + 0.5 || l.s == 0.45, "{w}×{h}: panel too tall");
            assert!(panel_w <= w as f32 * 0.28 + 1.0 || l.s == 0.45, "{w}×{h}: panel too wide");
            assert!(l.view_w() > 0.0);
        }
    }

    #[test]
    fn chat_colours() {
        let line = |name: &str, recipient: &str| ChatLine {
            name: name.into(),
            recipient: recipient.into(),
            text: "hi".into(),
            name_color: 0,
            text_color: 0,
            stars: -1,
            at: 0,
        };
        assert_eq!(line("Bob", "").pieces("Me"), vec![("<Bob> ".into(), 0x00FF00), ("hi".into(), 0xFFFFFF)]);
        assert_eq!(line("", "").pieces("Me"), vec![("hi".into(), 0xFFFF00)]);
        assert_eq!(line("#Oryx", "").pieces("Me")[0], ("<Oryx> ".into(), 0xFFA800));
        // A tell we sent shows who it went to.
        assert_eq!(line("Me", "Bob").pieces("Me")[0], ("To: ".into(), 0x00F0FF));
    }

    #[test]
    fn slot_stats() {
        assert_eq!(slot_stat(0), 8);
        assert_eq!(slot_stat(11), 19);
        assert_eq!(slot_stat(12), 67);
        assert_eq!(slot_stat(19), 74);
    }
}
