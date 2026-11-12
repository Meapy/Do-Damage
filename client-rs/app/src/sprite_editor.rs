//! The Sprite Editor (the main menu's "Sprite Editor"): players draw a sprite, a new look for an
//! item or the art for an item still to be made, and send it in; admins review what's sent in.
//!
//! It grew out of the crosshair painter (Options, Crosshair): a grid to click or drag on, with a
//! pencil, an eraser, a fill and a colour picker, mirrored drawing, undo, at 8×8 or 16×16 (the
//! game's item sizes). Picking an item starts from its sprite as the game draws it.
//!
//! The account server keeps what's sent in (`SpritesController`, `/sprites/...`): My sprites
//! lists the player's own and what became of them, and admins get a Review tab to approve or turn
//! them down. Approved sprites go into the game with the next release:
//! `tools/community_sprites.py` draws them into the community sheets, points the items at them
//! and credits the player in the item's tooltip (`<SpriteBy>`).

use crate::hud::{self, Ui};
use crate::i18n::{mark, tf, tn, tr};
use crate::login::{self, Endpoint};
use crate::textedit::LineEdit;
use crate::theme::{self, color, Kind};
use bsk_net::account::{self as acct, SpriteList, SpriteSub};
use bsk_net::{http, Pending};
use winit::keyboard::KeyCode;

/// The sizes a sprite can be: the game's item sprites are 8×8, a few 16×16.
pub const SIZES: [usize; 2] = [8, 16];

/// What a new item can be: the name the server keeps, and its label.
pub const SLOTS: [(&str, &str); 6] = [
    ("weapon", mark("sprites::Weapon")),
    ("ability", mark("sprites::Ability")),
    ("armor", mark("sprites::Armour")),
    ("ring", mark("sprites::Ring")),
    ("consumable", mark("sprites::Consumable")),
    ("other", mark("sprites::Other")),
];

/// The colours offered, in the game's own look: greys, then the colours from dark to light.
pub const PALETTE: [u32; 40] = [
    0x000000, 0x2B2B2B, 0x545454, 0x7F7F7F, 0xA8A8A8, 0xD4D4D4, 0xFFFFFF, 0xF1E3C6,
    0x4A0B0B, 0x8E1414, 0xD62828, 0xFF5A5A, 0x5C2A0A, 0xA64B12, 0xF07A1A, 0xFFB061,
    0x5A4A08, 0xA88A0E, 0xF2CF1D, 0xFFF07A, 0x123D12, 0x1F7A24, 0x37C23E, 0x8FF08A,
    0x0B3B3F, 0x177D82, 0x2BCFD6, 0x9AF5F2, 0x0E1E5C, 0x1F3FB0, 0x4A7BFF, 0x9DBBFF,
    0x2C0F52, 0x5A22A6, 0x9B4DFF, 0xD2A6FF, 0x5C0E3C, 0xA81C6C, 0xFF4FB0, 0xFFA8D8,
];

/// The drawing: `size` × `size` pixels, row by row, 0 for empty, else `0xFF_RR_GG_BB`.
#[derive(Clone, Debug, PartialEq)]
pub struct Canvas {
    pub size: usize,
    pub px: Vec<u32>,
}

impl Canvas {
    pub fn new(size: usize) -> Self {
        Canvas { size, px: vec![0; size * size] }
    }

    pub fn get(&self, x: usize, y: usize) -> u32 {
        self.px[y * self.size + x]
    }

    /// True if it changed.
    pub fn set(&mut self, x: usize, y: usize, v: u32) -> bool {
        let p = &mut self.px[y * self.size + x];
        let changed = *p != v;
        *p = v;
        changed
    }

    /// The pixels joined to (x, y) (sideways, not corner to corner) of its colour become `v`.
    pub fn fill(&mut self, x: usize, y: usize, v: u32) -> bool {
        let from = self.get(x, y);
        if from == v {
            return false;
        }
        let n = self.size;
        let mut todo = vec![(x, y)];
        while let Some((x, y)) = todo.pop() {
            if self.get(x, y) != from {
                continue;
            }
            self.set(x, y, v);
            if x > 0 {
                todo.push((x - 1, y));
            }
            if x + 1 < n {
                todo.push((x + 1, y));
            }
            if y > 0 {
                todo.push((x, y - 1));
            }
            if y + 1 < n {
                todo.push((x, y + 1));
            }
        }
        true
    }

    /// The same drawing at another size: drawn twice as big going up, every other pixel going down.
    pub fn resized(&self, size: usize) -> Canvas {
        let mut out = Canvas::new(size);
        for y in 0..size {
            for x in 0..size {
                out.px[y * size + x] = self.get(x * self.size / size, y * self.size / size);
            }
        }
        out
    }

    pub fn is_empty(&self) -> bool {
        self.px.iter().all(|&p| p == 0)
    }

    /// The colours used, in the order they first come (at most `max`).
    pub fn colours(&self, max: usize) -> Vec<u32> {
        let mut out = Vec::new();
        for &p in &self.px {
            if p != 0 && !out.contains(&p) {
                out.push(p);
                if out.len() == max {
                    break;
                }
            }
        }
        out
    }

    /// A sprite as the game's sheet has it: solid where it's at least half opaque.
    pub fn from_image(img: &image::RgbaImage) -> Option<Canvas> {
        let size = img.width() as usize;
        if img.height() as usize != size || !SIZES.contains(&size) {
            return None;
        }
        let px = img.pixels().map(|p| if p[3] >= 0x80 { 0xFF00_0000 | (p[0] as u32) << 16 | (p[1] as u32) << 8 | p[2] as u32 } else { 0 }).collect();
        Some(Canvas { size, px })
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Tab {
    #[default]
    Draw,
    Mine,
    Review,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Tool {
    #[default]
    Pencil,
    Eraser,
    Fill,
    Picker,
}

const TOOLS: [(Tool, &str, &str); 4] = [
    (Tool::Pencil, mark("sprites::Pencil"), "B"),
    (Tool::Eraser, mark("sprites::Eraser"), "E"),
    (Tool::Fill, mark("sprites::Fill"), "G"),
    (Tool::Picker, mark("sprites::Pick colour"), "I"),
];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Field {
    Search,
    Name,
    Note,
    Hex,
    Reason,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Back,
    Tab(Tab),
    ForItem,
    ForNew,
    Field(Field),
    Pick(u16),
    Cell([f32; 4]),
    Tool(Tool),
    Mirror,
    Undo,
    Redo,
    Clear,
    Restore,
    Size(usize),
    Swatch(u32),
    Slot(usize),
    Submit,
    SignIn,
    Refresh,
    /// Load a submission's sprite (and what it's for) into the drawing.
    Copy(i32),
    Withdraw(i32),
    Approve(i32),
    Reject(i32),
    Reopen(i32),
}

enum Req {
    Mine,
    Submit,
    Withdraw,
    Queue,
    Review(&'static str),
}

/// What the editor asks of the app.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Out {
    /// Back to the main menu.
    Back,
    /// To the sign-in screen.
    SignIn,
}

pub struct SpriteEditor {
    pub tab: Tab,
    canvas: Canvas,
    undo: Vec<Canvas>,
    redo: Vec<Canvas>,
    tool: Tool,
    colour: u32,
    mirror: bool,
    /// A new look for this item (its type), or a new item (`new_item`).
    item: Option<u16>,
    new_item: bool,
    /// The item's sprite is to be loaded into the drawing at the next frame (it needs the sheets).
    load_item: bool,
    /// The item's sprite as the game has it, to start again from and to compare with.
    original: Option<Canvas>,
    /// The item's id (as the server knows it), and one to pick by id at the next frame (a
    /// submission opened in the drawing).
    item_id: String,
    wanted_item: Option<String>,
    /// The wanted item's sprite goes into the drawing too.
    load_wanted: bool,
    /// Items by id, for the Review tab's current sprites (made when first needed).
    by_id: Option<std::collections::HashMap<String, u16>>,
    search: LineEdit,
    name: LineEdit,
    note: LineEdit,
    hex: LineEdit,
    reason: LineEdit,
    focus: Option<Field>,
    slot: usize,
    /// The items the search finds (by type), and the search they're for.
    matches: Vec<u16>,
    matches_for: Option<String>,
    list_row: usize,
    /// The rows shown in My sprites or Review start here.
    rows_from: usize,
    /// Dragging on the grid: the colour the cells become (0 erases).
    painting: Option<(u32, [f32; 4])>,
    /// Signed in: where, and as whom; and whether as a guest (who can't send in).
    pub login: Option<(Endpoint, String, String)>,
    pub guest: bool,
    mine: Option<SpriteList>,
    queue: Option<(Vec<SpriteSub>, Vec<SpriteSub>)>,
    /// A line under the drawing: (text, is it an error).
    message: Option<(String, bool)>,
    pending: Option<(Req, Pending<http::Response>)>,
    hits: Vec<(Hit, [f32; 4])>,
    /// Asked the server for My sprites since it was opened (it says whether this is an admin).
    asked: bool,
    /// A click on a button that makes a sound (taken by the app).
    pub clicked: bool,
}

impl Default for SpriteEditor {
    fn default() -> Self {
        SpriteEditor {
            tab: Tab::Draw,
            canvas: Canvas::new(8),
            undo: Vec::new(),
            redo: Vec::new(),
            tool: Tool::Pencil,
            colour: 0xFF00_0000 | PALETTE[10],
            mirror: false,
            item: None,
            new_item: false,
            load_item: false,
            original: None,
            item_id: String::new(),
            wanted_item: None,
            load_wanted: false,
            by_id: None,
            search: LineEdit::new("", 40),
            name: LineEdit::new("", 32),
            note: LineEdit::new("", 200),
            hex: LineEdit::new("", 7),
            reason: LineEdit::new("", 200),
            focus: None,
            slot: 0,
            matches: Vec::new(),
            matches_for: None,
            list_row: 0,
            rows_from: 0,
            painting: None,
            login: None,
            guest: false,
            mine: None,
            queue: None,
            message: None,
            pending: None,
            hits: Vec::new(),
            asked: false,
            clicked: false,
        }
    }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// The cell under `pos` of an `n` × `n` grid drawn in `grid`.
fn grid_cell(grid: [f32; 4], n: usize, pos: (f64, f64)) -> Option<(usize, usize)> {
    if !inside(grid, pos) {
        return None;
    }
    let col = ((pos.0 as f32 - grid[0]) / grid[2] * n as f32) as usize;
    let row = ((pos.1 as f32 - grid[1]) / grid[3] * n as f32) as usize;
    Some((col.min(n - 1), row.min(n - 1)))
}

/// `#rrggbb` (the # optional) as a colour.
pub fn parse_hex(text: &str) -> Option<u32> {
    let t = text.trim().trim_start_matches('#');
    (t.len() == 6).then(|| u32::from_str_radix(t, 16).ok()).flatten().map(|v| 0xFF00_0000 | v)
}

/// What the server answered, in the player's language when it's one of the Sprite Editor's.
fn server_text(e: String) -> String {
    let line = match e.as_str() {
        "Draw something first." => tr("Draw something first."),
        "There's no such item." => tr("There's no such item."),
        "Give the new item a name." => tr("Give the new item a name."),
        "Pick what kind of item it is." => tr("Pick what kind of item it is."),
        "Wait a few seconds before sending another." => tr("Wait a few seconds before sending another."),
        "Too many of your sprites are waiting for review already." => tr("Too many of your sprites are waiting for review already."),
        "Too many sprites are waiting for review. Try again later." => tr("Too many sprites are waiting for review. Try again later."),
        "You've sent in a lot of sprites lately. Try again tomorrow." => tr("You've sent in a lot of sprites lately. Try again tomorrow."),
        "Register first: a guest can't send in sprites." => tr("Register first: a guest can't send in sprites."),
        "This account can't send in sprites." => tr("This account can't send in sprites."),
        "It isn't waiting for review any more." => tr("It isn't waiting for review any more."),
        "Only admins can review sprites." => tr("Only admins can review sprites."),
        "That sprite can't be changed any more." => tr("That sprite can't be changed any more."),
        _ => return e,
    };
    line.to_owned()
}

/// A sprite's pixels drawn `side` square at (x, y), outlined in black as the game draws items
/// (`outline`), else on a dark well.
fn draw_pixels(ui: &mut Ui, px: &[u32], size: usize, x: f32, y: f32, side: f32, outline: bool) {
    if size == 0 || px.len() != size * size {
        return;
    }
    let cell = side / size as f32;
    if outline {
        let o = (cell * 0.22).max(1.0);
        for (i, &p) in px.iter().enumerate() {
            if p != 0 {
                let (cx, cy) = (x + (i % size) as f32 * cell, y + (i / size) as f32 * cell);
                ui.rect(cx - o, cy - o, cell + 2.0 * o, cell + 2.0 * o, 0x000000, 1.0);
            }
        }
    }
    for (i, &p) in px.iter().enumerate() {
        if p != 0 {
            let (cx, cy) = (x + (i % size) as f32 * cell, y + (i / size) as f32 * cell);
            // a hair over, so no seam shows between cells at fractional scales
            ui.rect(cx, cy, cell + 0.5, cell + 0.5, p & 0xFF_FFFF, 1.0);
        }
    }
}

/// An item's sprite as the game has it, when it's one the editor can draw (8×8 or 16×16).
fn item_sprite(ui: &Ui, item: u16) -> Option<Canvas> {
    let desc = ui.data.objects.get(&item)?;
    let src = ui.src(&desc.texture)?;
    Canvas::from_image(&ui.sprites.pixels(src))
}

/// The items players can pick, by their name, for the search.
fn is_item(d: &bsk_data::ObjectDesc) -> bool {
    d.xml.has("Item") && d.xml.has("SlotType")
}

fn item_name(ui: &Ui, item: u16) -> String {
    ui.data.objects.get(&item).map_or_else(String::new, |d| if d.display_id.is_empty() { d.id.clone() } else { d.display_id.clone() })
}

fn slot_label(slot: &str) -> &'static str {
    match slot {
        "weapon" => tr("sprites::Weapon"),
        "ability" => tr("sprites::Ability"),
        "armor" => tr("sprites::Armour"),
        "ring" => tr("sprites::Ring"),
        "consumable" => tr("sprites::Consumable"),
        _ => tr("sprites::Other"),
    }
}

/// What became of a submission, and its colour.
fn status_line(s: &SpriteSub) -> (String, u32) {
    match s.status.as_str() {
        "pending" => (tr("Waiting for review").to_owned(), color::GOLD),
        "approved" => (tr("Approved: in the game with the next update").to_owned(), color::GOOD),
        "released" => (tr("In the game").to_owned(), color::GOOD),
        "withdrawn" => (tr("Withdrawn").to_owned(), color::TEXT_MUTED),
        _ if s.review.is_empty() => (tr("Turned down").to_owned(), color::DANGER),
        _ => (tf!("Turned down: {why}", why = s.review), color::DANGER),
    }
}

impl SpriteEditor {
    /// Opened from the main menu: on the drawing, asking the server for the player's own (which
    /// also says whether they may review).
    pub fn open(&mut self) {
        self.tab = Tab::Draw;
        self.message = None;
        self.focus = None;
        self.asked = false;
        self.mine = None;
        self.queue = None;
        self.rows_from = 0;
    }

    /// `--screen sprites-mine`, `sprites-review` or `sprites-item:<item id>` (tests): that tab,
    /// or the drawing started from that item's sprite.
    pub fn test_screen(&mut self, screen: &str) {
        match screen {
            "sprites-mine" => self.tab = Tab::Mine,
            "sprites-review" => self.tab = Tab::Review,
            _ => {
                if let Some(id) = screen.strip_prefix("sprites-item:") {
                    self.wanted_item = Some(id.to_owned());
                    self.load_wanted = true;
                }
            }
        }
    }

    /// The account may review what's sent in (the server says, with My sprites).
    pub fn is_admin(&self) -> bool {
        self.mine.as_ref().is_some_and(|m| m.admin)
    }

    fn signed_in(&self) -> Option<(&Endpoint, &str, &str)> {
        self.login.as_ref().map(|(e, a, p)| (e, a.as_str(), p.as_str()))
    }

    fn ask(&mut self, req: Req) {
        if self.pending.is_some() {
            return;
        }
        let Some((endpoint, email, password)) = self.signed_in() else { return };
        let p = match &req {
            Req::Mine => endpoint.sprites_mine(email, password),
            Req::Queue => endpoint.sprite_queue(email, password),
            // the others are asked with their arguments (submit, withdraw, review)
            _ => return,
        };
        self.pending = Some((req, p));
    }

    fn snapshot(&mut self) {
        self.undo.push(self.canvas.clone());
        if self.undo.len() > 100 {
            self.undo.remove(0);
        }
        self.redo.clear();
    }

    fn set_canvas(&mut self, c: Canvas) {
        if c != self.canvas {
            self.snapshot();
            self.canvas = c;
        }
    }

    /// Paint a cell (and its mirror) with `v`. True if anything changed.
    fn paint(&mut self, x: usize, y: usize, v: u32) -> bool {
        let n = self.canvas.size;
        let mut changed = self.canvas.set(x, y, v);
        if self.mirror {
            changed |= self.canvas.set(n - 1 - x, y, v);
        }
        changed
    }

    /// The app's frame: takes the server's answer, when one has come.
    pub fn update(&mut self) {
        if !self.asked && self.login.is_some() && self.pending.is_none() {
            self.asked = true;
            self.ask(Req::Mine);
        }
        if self.tab == Tab::Review && self.queue.is_none() && self.pending.is_none() && self.is_admin() {
            self.ask(Req::Queue);
        }
        let Some((_, pending)) = &self.pending else { return };
        let Some(response) = pending.take() else { return };
        let Some((req, _)) = self.pending.take() else { return };
        let body = login::reached(response);
        match req {
            Req::Mine => match body.and_then(|b| acct::parse_sprite_list(&b)) {
                Ok(list) => self.mine = Some(list),
                Err(e) => {
                    // an older server, or not reachable: the editor still draws
                    log::info!("sprites: {e}");
                    self.mine = Some(SpriteList::default());
                }
            },
            Req::Submit => match body.and_then(|b| acct::parse_sprite(&b)) {
                Ok(s) => {
                    self.message = Some((tr("Sent in! The admins will look at it soon: follow it under My sprites.").to_owned(), false));
                    if let Some(m) = &mut self.mine {
                        m.sprites.insert(0, s);
                    }
                }
                Err(e) => self.message = Some((server_text(e), true)),
            },
            Req::Withdraw => match body.and_then(|b| acct::success(&b)) {
                Ok(()) => {
                    self.message = Some((tr("Withdrawn.").to_owned(), false));
                    self.mine = None;
                    self.asked = false;
                }
                Err(e) => self.message = Some((server_text(e), true)),
            },
            Req::Queue => match body.and_then(|b| acct::parse_sprite_queue(&b)) {
                Ok(q) => self.queue = Some(q),
                Err(e) => {
                    self.queue = Some((Vec::new(), Vec::new()));
                    self.message = Some((server_text(e), true));
                }
            },
            Req::Review(verdict) => match body.and_then(|b| acct::parse_sprite(&b)) {
                Ok(s) => {
                    let line = match verdict {
                        "approved" => tf!("Approved #{id}. It goes into the game with the next update.", id = s.id),
                        "rejected" => tf!("Turned down #{id}.", id = s.id),
                        _ => tf!("#{id} is waiting for review again.", id = s.id),
                    };
                    self.message = Some((line, false));
                    self.reason = LineEdit::new("", 200);
                    self.queue = None;
                }
                Err(e) => {
                    self.message = Some((server_text(e), true));
                    self.queue = None;
                }
            },
        }
    }

    fn submit(&mut self) {
        if self.pending.is_some() {
            return;
        }
        if self.canvas.is_empty() {
            self.message = Some((tr("Draw something first.").to_owned(), true));
            return;
        }
        if !self.new_item && self.item.is_none() {
            self.message = Some((tr("Pick the item it's for, or choose A new item.").to_owned(), true));
            return;
        }
        if self.new_item && self.name.text.trim().is_empty() {
            self.message = Some((tr("Give the new item a name.").to_owned(), true));
            return;
        }
        if self.guest {
            self.message = Some((tr("Register first: a guest can't send in sprites.").to_owned(), true));
            return;
        }
        if !self.new_item && self.original.as_ref() == Some(&self.canvas) {
            self.message = Some((tr("That's the sprite it has now: change something first.").to_owned(), true));
            return;
        }
        let item_id = self.item_id.clone();
        let Some((endpoint, email, password)) = self.signed_in() else {
            self.message = Some((tr("Sign in to send in your sprite.").to_owned(), true));
            return;
        };
        let pixels = acct::sprite_pixels_text(&self.canvas.px);
        let (item, name, slot) = if self.new_item { ("", self.name.text.trim(), SLOTS[self.slot].0) } else { (item_id.as_str(), "", "") };
        let p = endpoint.sprite_submit(email, password, item, name, slot, self.note.text.trim(), self.canvas.size, &pixels);
        self.pending = Some((Req::Submit, p));
        self.message = Some((tr("Sending\u{2026}").to_owned(), false));
    }

    fn review(&mut self, id: i32, verdict: &'static str) {
        if self.pending.is_some() {
            return;
        }
        let note = if verdict == "rejected" { self.reason.text.trim().to_owned() } else { String::new() };
        let Some((endpoint, email, password)) = self.signed_in() else { return };
        let p = endpoint.sprite_review(email, password, id, verdict, &note);
        self.pending = Some((Req::Review(verdict), p));
    }

    fn submission(&self, id: i32) -> Option<&SpriteSub> {
        let mine = self.mine.iter().flat_map(|m| m.sprites.iter());
        let queue = self.queue.iter().flat_map(|q| q.0.iter().chain(q.1.iter()));
        mine.chain(queue).find(|s| s.id == id)
    }

    /// The mouse wheel: scrolls the item list, or the rows of My sprites and Review.
    pub fn wheel(&mut self, notches: i32) {
        let row = if self.tab == Tab::Draw { &mut self.list_row } else { &mut self.rows_from };
        *row = (*row as i32 - notches).max(0) as usize;
    }

    /// Text the browser pasted: into the field being typed in.
    #[cfg_attr(not(target_arch = "wasm32"), allow(dead_code))]
    pub fn paste(&mut self, text: &str) {
        if let Some(f) = self.focus {
            self.field(f).insert(text);
            self.typed(f);
        }
    }

    fn field(&mut self, f: Field) -> &mut LineEdit {
        match f {
            Field::Search => &mut self.search,
            Field::Name => &mut self.name,
            Field::Note => &mut self.note,
            Field::Hex => &mut self.hex,
            Field::Reason => &mut self.reason,
        }
    }

    /// A field's text changed.
    fn typed(&mut self, f: Field) {
        match f {
            Field::Search => self.list_row = 0,
            Field::Hex => {
                if let Some(c) = parse_hex(&self.hex.text) {
                    self.colour = c;
                }
            }
            _ => {}
        }
    }

    /// A key pressed (with the text it types, and whether Ctrl and Shift are held).
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, ctrl: bool, shift: bool) -> Option<Out> {
        if let Some(f) = self.focus {
            match code {
                KeyCode::Escape => self.focus = None,
                KeyCode::Enter | KeyCode::NumpadEnter => self.focus = None,
                KeyCode::Tab => {
                    let order: &[Field] = match self.tab {
                        Tab::Review => &[Field::Reason],
                        _ if self.new_item => &[Field::Name, Field::Note, Field::Hex],
                        _ => &[Field::Search, Field::Note, Field::Hex],
                    };
                    let i = order.iter().position(|&o| o == f).unwrap_or(0);
                    let n = order.len();
                    self.focus = Some(order[if shift { (i + n - 1) % n } else { (i + 1) % n }]);
                }
                _ => {
                    if self.field(f).key(code, text, ctrl, shift) {
                        self.typed(f);
                    }
                }
            }
            return None;
        }
        match code {
            KeyCode::Escape => return Some(Out::Back),
            KeyCode::KeyZ if ctrl && shift => self.redo_step(),
            KeyCode::KeyZ if ctrl => self.undo_step(),
            KeyCode::KeyY if ctrl => self.redo_step(),
            _ if ctrl || self.tab != Tab::Draw => {}
            KeyCode::KeyB | KeyCode::KeyP => self.tool = Tool::Pencil,
            KeyCode::KeyE => self.tool = Tool::Eraser,
            KeyCode::KeyG => self.tool = Tool::Fill,
            KeyCode::KeyI => self.tool = Tool::Picker,
            KeyCode::KeyM => self.mirror = !self.mirror,
            _ => {}
        }
        None
    }

    fn undo_step(&mut self) {
        if let Some(c) = self.undo.pop() {
            self.redo.push(std::mem::replace(&mut self.canvas, c));
        }
    }

    fn redo_step(&mut self) {
        if let Some(c) = self.redo.pop() {
            self.undo.push(std::mem::replace(&mut self.canvas, c));
        }
    }

    /// A left click.
    pub fn click(&mut self, pos: (f64, f64)) -> Option<Out> {
        let hit = self.hits.iter().rev().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h);
        let Some(hit) = hit else {
            self.focus = None;
            return None;
        };
        if !matches!(hit, Hit::Field(_)) {
            self.focus = None;
        }
        self.clicked |= !matches!(hit, Hit::Field(_) | Hit::Cell(_));
        match hit {
            Hit::Back => return Some(Out::Back),
            Hit::SignIn => return Some(Out::SignIn),
            Hit::Tab(t) => {
                self.tab = t;
                self.rows_from = 0;
                self.message = None;
                if t == Tab::Review {
                    self.queue = None;
                }
            }
            Hit::ForItem => self.new_item = false,
            Hit::ForNew => self.new_item = true,
            Hit::Field(f) => {
                self.focus = Some(f);
                let e = self.field(f);
                e.cursor = e.text.chars().count();
                e.anchor = None;
            }
            Hit::Pick(t) => {
                self.item = Some(t);
                self.load_item = true;
                self.message = None;
            }
            Hit::Cell(grid) => {
                let (x, y) = grid_cell(grid, self.canvas.size, pos)?;
                match self.tool {
                    Tool::Picker => {
                        let c = self.canvas.get(x, y);
                        if c != 0 {
                            self.colour = c;
                            self.hex = LineEdit::new(format!("#{:06x}", c & 0xFF_FFFF), 7);
                        }
                        self.tool = Tool::Pencil;
                    }
                    Tool::Fill => {
                        let before = self.canvas.clone();
                        if self.canvas.fill(x, y, self.colour) {
                            self.undo.push(before);
                            self.redo.clear();
                        }
                    }
                    Tool::Pencil | Tool::Eraser => {
                        // A click on a cell of the pencil's colour empties it, as the crosshair
                        // painter does; a drag paints what the first cell became.
                        let v = if self.tool == Tool::Eraser || self.canvas.get(x, y) == self.colour { 0 } else { self.colour };
                        let before = self.canvas.clone();
                        if self.paint(x, y, v) {
                            self.undo.push(before);
                            self.redo.clear();
                        }
                        self.painting = Some((v, grid));
                    }
                }
            }
            Hit::Tool(t) => self.tool = t,
            Hit::Mirror => self.mirror = !self.mirror,
            Hit::Undo => self.undo_step(),
            Hit::Redo => self.redo_step(),
            Hit::Clear => {
                let n = self.canvas.size;
                self.set_canvas(Canvas::new(n));
            }
            Hit::Restore => {
                if let Some(c) = self.original.clone() {
                    self.set_canvas(c);
                }
            }
            Hit::Size(n) => {
                if n != self.canvas.size {
                    let c = self.canvas.resized(n);
                    self.set_canvas(c);
                }
            }
            Hit::Swatch(c) => {
                self.colour = c;
                self.hex = LineEdit::new(format!("#{:06x}", c & 0xFF_FFFF), 7);
                if self.tool != Tool::Fill {
                    self.tool = Tool::Pencil;
                }
            }
            Hit::Slot(i) => self.slot = i,
            Hit::Submit => self.submit(),
            Hit::Refresh => {
                self.mine = None;
                self.asked = false;
                self.queue = None;
            }
            Hit::Copy(id) => {
                if let Some(s) = self.submission(id).cloned() {
                    if s.pixels.len() == s.size * s.size && s.size > 0 {
                        self.set_canvas(Canvas { size: s.size, px: s.pixels.clone() });
                    }
                    self.new_item = s.kind == "new";
                    if self.new_item {
                        self.name = LineEdit::new(s.name.clone(), 32);
                        self.slot = SLOTS.iter().position(|x| x.0 == s.slot).unwrap_or(0);
                        self.original = None;
                    } else {
                        // found by its id at the next frame (it needs the game data)
                        self.wanted_item = Some(s.item.clone());
                    }
                    self.tab = Tab::Draw;
                    self.message = None;
                }
            }
            Hit::Withdraw(id) => {
                if self.pending.is_none() {
                    if let Some((endpoint, email, password)) = self.signed_in() {
                        let p = endpoint.sprite_withdraw(email, password, id);
                        self.pending = Some((Req::Withdraw, p));
                    }
                }
            }
            Hit::Approve(id) => self.review(id, "approved"),
            Hit::Reject(id) => self.review(id, "rejected"),
            Hit::Reopen(id) => self.review(id, "pending"),
        }
        None
    }

    /// The mouse while the button is down: a drag paints.
    pub fn drag(&mut self, pos: (f64, f64)) {
        let Some((v, grid)) = self.painting else { return };
        if let Some((x, y)) = grid_cell(grid, self.canvas.size, pos) {
            self.paint(x, y, v);
        }
    }

    pub fn release(&mut self) {
        self.painting = None;
    }
}

/// An edge `width` wide just inside `r` (the theme's frame fills its box with the edge's colour).
fn edge(ui: &mut Ui, r: [f32; 4], width: f32, rgb: u32, alpha: f32) {
    ui.rect(r[0], r[1], r[2], width, rgb, alpha);
    ui.rect(r[0], r[1] + r[3] - width, r[2], width, rgb, alpha);
    ui.rect(r[0], r[1] + width, width, r[3] - 2.0 * width, rgb, alpha);
    ui.rect(r[0] + r[2] - width, r[1] + width, width, r[3] - 2.0 * width, rgb, alpha);
}

/// `text` cut to `width` at `size`, with an ellipsis when it was cut.
fn fit(ui: &mut Ui, text: &str, size: f32, bold: bool, width: f32) -> String {
    if ui.measure(text, size, bold) <= width {
        return text.to_owned();
    }
    let mut chars: Vec<char> = text.chars().collect();
    while !chars.is_empty() {
        chars.pop();
        let cut: String = chars.iter().collect::<String>() + "\u{2026}";
        if ui.measure(&cut, size, bold) <= width {
            return cut;
        }
    }
    String::new()
}

/// A line of My sprites or Review: a heading, a note that there's nothing, or a submission.
enum Line {
    Head(String),
    Empty,
    Waiting(SpriteSub),
    Approved(SpriteSub),
}

impl SpriteEditor {
    #[allow(clippy::too_many_arguments)]
    fn button(&mut self, ui: &mut Ui, hit: Hit, r: [f32; 4], label: &str, kind: Kind, s: f32, mouse: (f64, f64)) {
        ui.button(r, label, kind, inside(r, mouse), s);
        self.hits.push((hit, r));
    }

    /// A rounded chip: lit when picked, brighter under the mouse (as the screens' chips).
    #[allow(clippy::too_many_arguments)]
    fn chip(&mut self, ui: &mut Ui, hit: Hit, r: [f32; 4], label: &str, on: bool, s: f32, mouse: (f64, f64)) {
        let over = inside(r, mouse);
        let (bg, fg) = if on { (color::ACCENT, color::ACCENT_TEXT) } else if over { (color::CHIP_HI, color::TEXT) } else { (color::CHIP, color::TEXT_DIM) };
        theme::fill(ui.renderer, r, r[3] / 2.0, bg, 0.95);
        if !on {
            theme::frame(ui.renderer, r, r[3] / 2.0, s.max(1.0), (color::BORDER, 0.9), ((bg, 0.0), (bg, 0.0)));
        }
        let size = ui.fit_size(label, r[2] - 10.0 * s, (13.0 * s).round().max(9.0), true);
        let tw = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - tw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, fg);
        self.hits.push((hit, r));
    }

    fn text_field(&mut self, ui: &mut Ui, f: Field, r: [f32; 4], placeholder: &str, s: f32, now: i32) {
        let focused = self.focus == Some(f);
        ui.field_box(r, focused, s);
        let size = (14.0 * s).round().max(9.0);
        let inner = [r[0] + 10.0 * s, r[1], r[2] - 20.0 * s, r[3]];
        let e = match f {
            Field::Search => &self.search,
            Field::Name => &self.name,
            Field::Note => &self.note,
            Field::Hex => &self.hex,
            Field::Reason => &self.reason,
        };
        if focused {
            hud::draw_line_edit(ui, e, inner, size, placeholder, now);
        } else {
            let shown = if e.text.is_empty() { placeholder.to_owned() } else { fit(ui, &e.text, size, false, inner[2]) };
            let c = if e.text.is_empty() { color::TEXT_MUTED } else { color::TEXT };
            ui.label(&shown, inner[0], r[1] + (r[3] - size * 1.22) / 2.0, size, false, c);
        }
        self.hits.push((Hit::Field(f), r));
    }

    fn heading(ui: &mut Ui, text: &str, x: f32, y: f32, s: f32) {
        ui.label(text, x, y, (13.0 * s).round().max(9.0), true, color::TEXT_DIM);
    }

    /// The editor over the menu's sky.
    pub fn draw(&mut self, ui: &mut Ui, w: f32, h: f32, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        let s = crate::screens::scale(w, h);
        self.resolve(ui);

        self.button(ui, Hit::Back, [16.0 * s, 16.0 * s, 110.0 * s, 38.0 * s], "\u{2039} Back", Kind::Secondary, s, mouse);
        let title = tr("Sprite Editor");
        let ts = (26.0 * s).round();
        let tw = ui.measure(title, ts, true);
        ui.label(title, (w - tw) / 2.0, 18.0 * s, ts, true, color::TEXT);

        // (not before the server has said whether this account may review)
        if self.tab == Tab::Review && self.mine.is_some() && !self.is_admin() {
            self.tab = Tab::Draw;
        }
        let mut tabs = vec![(Tab::Draw, tr("sprites::Draw")), (Tab::Mine, tr("My sprites"))];
        if self.is_admin() {
            tabs.push((Tab::Review, tr("sprites::Review")));
        }
        let (cw, ch, cg) = (150.0 * s, 32.0 * s, 10.0 * s);
        let total = tabs.len() as f32 * cw + (tabs.len() - 1) as f32 * cg;
        for (i, (t, label)) in tabs.into_iter().enumerate() {
            let r = [(w - total) / 2.0 + i as f32 * (cw + cg), 62.0 * s, cw, ch];
            let on = self.tab == t;
            self.chip(ui, Hit::Tab(t), r, label, on, s, mouse);
        }

        let bw = (w - 32.0).min(1000.0 * s);
        let body = [(w - bw) / 2.0, 108.0 * s, bw, h - 108.0 * s - 44.0 * s];
        match self.tab {
            Tab::Draw => self.draw_tab(ui, body, s, mouse, now),
            Tab::Mine => self.mine_tab(ui, body, s, mouse),
            Tab::Review => self.review_tab(ui, body, s, mouse, now),
        }

        // the message, under it all
        if let Some((text, error)) = self.message.clone() {
            let size = (14.0 * s).round().max(9.0);
            let line = fit(ui, &text, size, true, w - 48.0);
            let lw = ui.measure(&line, size, true);
            let c = if error { color::DANGER } else { color::GOOD };
            let r = [(w - lw) / 2.0 - 12.0 * s, h - 36.0 * s, lw + 24.0 * s, size * 1.3 + 8.0 * s];
            theme::fill(ui.renderer, r, 6.0 * s, color::PANEL_BOTTOM, 0.92);
            theme::fill(ui.renderer, r, 6.0 * s, c, 0.16);
            ui.label(&line, (w - lw) / 2.0, r[1] + 4.0 * s, size, true, theme::shade(c, 0.35));
        }
    }

    /// Loads what the last click asked for that needs the game's data or sheets.
    fn resolve(&mut self, ui: &mut Ui) {
        if let Some(id) = self.wanted_item.take() {
            let found = ui.data.objects.values().find(|d| d.id == id).map(|d| d.object_type);
            if let Some(t) = found {
                self.item = Some(t);
                self.item_id = id;
                self.original = item_sprite(ui, t);
                if std::mem::take(&mut self.load_wanted) {
                    self.new_item = false;
                    if let Some(c) = self.original.clone() {
                        self.set_canvas(c);
                    }
                }
            }
        }
        if std::mem::take(&mut self.load_item) {
            if let Some(t) = self.item {
                self.item_id = ui.data.objects.get(&t).map_or_else(String::new, |d| d.id.clone());
                self.original = item_sprite(ui, t);
                match self.original.clone() {
                    Some(c) => self.set_canvas(c),
                    None => self.message = Some((tr("This item's sprite isn't 8×8 or 16×16: start from an empty one.").to_owned(), true)),
                }
            }
        }
        if self.matches_for.as_deref() != Some(self.search.text.as_str()) {
            let q = self.search.text.trim().to_lowercase();
            let mut found: Vec<(String, u16)> = ui
                .data
                .objects
                .values()
                .filter(|d| is_item(d))
                .map(|d| (if d.display_id.is_empty() { d.id.clone() } else { d.display_id.clone() }, d.object_type))
                .filter(|(name, _)| q.is_empty() || name.to_lowercase().contains(&q))
                .collect();
            found.sort();
            self.matches = found.into_iter().map(|f| f.1).collect();
            self.matches_for = Some(self.search.text.clone());
        }
    }

    fn draw_tab(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let [bx, by, bw, bh] = body;
        let gap = 16.0 * s;
        let (lw, rw) = (270.0 * s, 236.0 * s);
        let pad = 14.0 * s;

        // ---- left: what it's for ----
        theme::panel(ui.renderer, [bx, by, lw, bh], s);
        let (x, inner_w) = (bx + pad, lw - 2.0 * pad);
        let mut y = by + pad;
        Self::heading(ui, tr("What it's for"), x, y, s);
        y += 22.0 * s;
        let half = (inner_w - 8.0 * s) / 2.0;
        let new_item = self.new_item;
        self.chip(ui, Hit::ForItem, [x, y, half, 30.0 * s], tr("An item's new look"), !new_item, s, mouse);
        self.chip(ui, Hit::ForNew, [x + half + 8.0 * s, y, half, 30.0 * s], tr("A new item"), new_item, s, mouse);
        y += 42.0 * s;
        let note_top = by + bh - pad - 34.0 * s - 22.0 * s;
        if !new_item {
            self.text_field(ui, Field::Search, [x, y, inner_w, 34.0 * s], tr("Search items"), s, now);
            y += 42.0 * s;
            let row_h = 34.0 * s;
            let rows = (((note_top - 22.0 * s) - y) / row_h).floor().max(1.0) as usize;
            self.list_row = self.list_row.min(self.matches.len().saturating_sub(rows));
            let shown: Vec<u16> = self.matches.iter().skip(self.list_row).take(rows).copied().collect();
            if shown.is_empty() {
                ui.label(tr("No items match."), x + 4.0 * s, y + 6.0 * s, (13.0 * s).round().max(9.0), false, color::TEXT_MUTED);
            }
            for (i, t) in shown.into_iter().enumerate() {
                let r = [x, y + i as f32 * row_h, inner_w, row_h - 2.0 * s];
                let on = self.item == Some(t);
                if on || inside(r, mouse) {
                    theme::fill(ui.renderer, r, 5.0 * s, if on { color::ACCENT } else { color::CHIP_HI }, if on { 0.28 } else { 0.6 });
                }
                ui.item_icon(t as i32, 30, r[0] + 16.0 * s, r[1] + r[3] / 2.0, s);
                let size = (13.0 * s).round().max(9.0);
                let name = item_name(ui, t);
                let name = fit(ui, &name, size, on, r[2] - 40.0 * s);
                ui.label(&name, r[0] + 36.0 * s, r[1] + (r[3] - size * 1.22) / 2.0, size, on, if on { color::TEXT } else { color::TEXT_DIM });
                self.hits.push((Hit::Pick(t), r));
            }
            if self.matches.len() > rows {
                let small = (11.0 * s).round().max(8.0);
                let more = tf!("{from}-{to} of {all}: scroll for more", from = self.list_row + 1, to = (self.list_row + rows).min(self.matches.len()), all = self.matches.len());
                ui.label(&more, x, note_top - 18.0 * s, small, false, color::TEXT_MUTED);
            }
        } else {
            Self::heading(ui, tr("Its name"), x, y, s);
            y += 20.0 * s;
            self.text_field(ui, Field::Name, [x, y, inner_w, 34.0 * s], tr("Name the item"), s, now);
            y += 46.0 * s;
            Self::heading(ui, tr("What kind of item"), x, y, s);
            y += 22.0 * s;
            for (i, (_, label)) in SLOTS.iter().enumerate() {
                let r = [x + (i % 2) as f32 * (half + 8.0 * s), y + (i / 2) as f32 * 38.0 * s, half, 30.0 * s];
                let on = self.slot == i;
                self.chip(ui, Hit::Slot(i), r, tr(label), on, s, mouse);
            }
            y += 3.0 * 38.0 * s + 8.0 * s;
            let hs = (12.0 * s).round().max(9.0);
            for line in crate::screens::wrap(ui, tr("The admins choose which new items to make. Approved art waits for its item, credited to you."), hs, inner_w) {
                ui.label(&line, x, y, hs, false, color::TEXT_MUTED);
                y += hs * 1.35;
            }
        }
        Self::heading(ui, tr("A note for the admins (optional)"), x, note_top, s);
        self.text_field(ui, Field::Note, [x, note_top + 22.0 * s, inner_w, 34.0 * s], tr("What it is, ideas for it\u{2026}"), s, now);

        // ---- middle: the drawing ----
        let mid = [bx + lw + gap, by, bw - lw - rw - 2.0 * gap, bh];
        theme::panel(ui.renderer, mid, s);
        let n = self.canvas.size;
        let what = if new_item {
            if self.name.text.trim().is_empty() { tr("A new item").to_owned() } else { tf!("New item: {name}", name = self.name.text.trim()) }
        } else {
            match self.item {
                Some(t) => tf!("New look for {name}", name = item_name(ui, t)),
                None => tr("Pick an item on the left").to_owned(),
            }
        };
        let hs = (15.0 * s).round();
        let what = fit(ui, &what, hs, true, mid[2] - 2.0 * pad);
        let ww = ui.measure(&what, hs, true);
        ui.label(&what, mid[0] + (mid[2] - ww) / 2.0, by + pad, hs, true, color::TEXT);
        let tools_h = 2.0 * 38.0 * s + 10.0 * s;
        let room = (mid[2] - 2.0 * pad).min(mid[3] - 2.0 * pad - 30.0 * s - tools_h).max(64.0);
        let side = (room / n as f32).floor() * n as f32;
        let grid = [(mid[0] + (mid[2] - side) / 2.0).round(), (by + pad + 30.0 * s).round(), side, side];
        let cell = side / n as f32;
        theme::frame(ui.renderer, [grid[0] - 4.0 * s, grid[1] - 4.0 * s, grid[2] + 8.0 * s, grid[3] + 8.0 * s], 6.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
        // empty cells in a checkerboard, so empty reads as empty
        for row in 0..n {
            for col in 0..n {
                let c = if (row + col) % 2 == 0 { 0x1C1630 } else { 0x261F3E };
                ui.rect(grid[0] + col as f32 * cell, grid[1] + row as f32 * cell, cell, cell, c, 1.0);
            }
        }
        let px = self.canvas.px.clone();
        draw_pixels(ui, &px, n, grid[0], grid[1], side, false);
        for k in 1..n {
            let o = (k as f32 * cell).round();
            ui.rect(grid[0] + o, grid[1], 1.0, side, 0x000000, 0.35);
            ui.rect(grid[0], grid[1] + o, side, 1.0, 0x000000, 0.35);
        }
        if self.mirror {
            ui.rect(grid[0] + side / 2.0 - 1.0, grid[1], 2.0, side, color::ACCENT, 0.8);
        }
        if let Some((cx, cy)) = grid_cell(grid, n, mouse) {
            let mut cells = vec![cx];
            if self.mirror {
                cells.push(n - 1 - cx);
            }
            for c in cells {
                edge(ui, [grid[0] + c as f32 * cell, grid[1] + cy as f32 * cell, cell, cell], (2.0 * s).max(1.0), 0xFFFFFF, 0.8);
            }
        }
        self.hits.push((Hit::Cell(grid), grid));

        // tools, under the drawing
        let mut ty = grid[1] + side + 14.0 * s;
        let tw = (mid[2] - 2.0 * pad - 4.0 * 6.0 * s) / 5.0;
        for (i, (t, label, key)) in TOOLS.iter().enumerate() {
            let r = [mid[0] + pad + i as f32 * (tw + 6.0 * s), ty, tw, 30.0 * s];
            let on = self.tool == *t;
            self.chip(ui, Hit::Tool(*t), r, &format!("{} ({key})", tr(label)), on, s, mouse);
        }
        let mirror = self.mirror;
        self.chip(ui, Hit::Mirror, [mid[0] + pad + 4.0 * (tw + 6.0 * s), ty, tw, 30.0 * s], tr("Mirror (M)"), mirror, s, mouse);
        ty += 38.0 * s;
        let bw2 = (mid[2] - 2.0 * pad - 5.0 * 6.0 * s) / 6.0;
        let at = |i: usize| [mid[0] + pad + i as f32 * (bw2 + 6.0 * s), ty, bw2, 30.0 * s];
        self.button(ui, Hit::Undo, at(0), tr("Undo"), Kind::Secondary, s, mouse);
        self.button(ui, Hit::Redo, at(1), tr("Redo"), Kind::Secondary, s, mouse);
        self.button(ui, Hit::Clear, at(2), tr("sprites::Clear"), Kind::Secondary, s, mouse);
        if self.original.is_some() && !new_item {
            self.button(ui, Hit::Restore, at(3), tr("Start over"), Kind::Secondary, s, mouse);
        }
        for (k, &size) in SIZES.iter().enumerate() {
            let on = self.canvas.size == size;
            self.chip(ui, Hit::Size(size), at(4 + k), &format!("{size}\u{00D7}{size}"), on, s, mouse);
        }

        // ---- right: colours, preview, send ----
        let right = [bx + bw - rw, by, rw, bh];
        theme::panel(ui.renderer, right, s);
        let (x, inner_w) = (right[0] + pad, rw - 2.0 * pad);
        let mut y = by + pad;
        Self::heading(ui, tr("Colour"), x, y, s);
        y += 22.0 * s;
        let sw = 34.0 * s;
        ui.rect(x, y, sw, sw, self.colour & 0xFF_FFFF, 1.0);
        edge(ui, [x, y, sw, sw], s.max(1.0), 0xFFFFFF, 0.6);
        if self.focus != Some(Field::Hex) && parse_hex(&self.hex.text) != Some(self.colour) {
            self.hex = LineEdit::new(format!("#{:06x}", self.colour & 0xFF_FFFF), 7);
        }
        self.text_field(ui, Field::Hex, [x + sw + 8.0 * s, y, inner_w - sw - 8.0 * s, sw], "#rrggbb", s, now);
        y += sw + 10.0 * s;
        let cols = 8;
        let step = inner_w / cols as f32;
        let sq = step - 4.0 * s;
        for (i, &c) in PALETTE.iter().enumerate() {
            let r = [x + (i % cols) as f32 * step, y + (i / cols) as f32 * step, sq, sq];
            self.swatch(ui, c, r, s, mouse);
        }
        y += (PALETTE.len() / cols) as f32 * step + 8.0 * s;
        let used = self.canvas.colours(16);
        if !used.is_empty() {
            Self::heading(ui, tr("In your sprite"), x, y, s);
            y += 20.0 * s;
            for (i, &c) in used.iter().enumerate() {
                let r = [x + (i % cols) as f32 * step, y + (i / cols) as f32 * step, sq, sq];
                self.swatch(ui, c & 0xFF_FFFF, r, s, mouse);
            }
            y += used.len().div_ceil(cols) as f32 * step + 6.0 * s;
        }

        // the preview, as the game draws items: outlined, in a slot
        let send_top = by + bh - pad - 42.0 * s;
        let slot = 64.0 * s;
        let py = (send_top - 22.0 * s - slot - 30.0 * s).max(y);
        Self::heading(ui, tr("In the game"), x, py, s);
        let py = py + 22.0 * s;
        let mut sx = x;
        let original = if new_item { None } else { self.original.clone() };
        let previews = [(tr("Now"), original.map(|o| (o.px, o.size))), (tr("Yours"), Some((px, n)))];
        for (label, sprite) in previews {
            let Some((pixels, size)) = sprite else { continue };
            let r = [sx, py, slot, slot];
            ui.slot_box(r, None, false, s);
            let side = (slot * if size == 8 { 0.62 } else { 0.7 } / size as f32).floor() * size as f32;
            draw_pixels(ui, &pixels, size, (r[0] + (slot - side) / 2.0).round(), (r[1] + (slot - side) / 2.0).round(), side, true);
            let ls = (12.0 * s).round().max(9.0);
            let lw = ui.measure(label, ls, false);
            ui.label(label, r[0] + (slot - lw) / 2.0, r[1] + slot + 4.0 * s, ls, false, color::TEXT_DIM);
            sx += slot + 14.0 * s;
        }

        let send = [x, send_top, inner_w, 42.0 * s];
        if self.login.is_none() {
            self.button(ui, Hit::SignIn, send, tr("Sign in to send it in"), Kind::Secondary, s, mouse);
        } else {
            let sending = self.pending.as_ref().is_some_and(|p| matches!(p.0, Req::Submit));
            let label = if sending { tr("Sending\u{2026}") } else { tr("Send in for review") };
            self.button(ui, Hit::Submit, send, label, Kind::Primary, s, mouse);
        }
    }

    fn swatch(&mut self, ui: &mut Ui, c: u32, r: [f32; 4], s: f32, mouse: (f64, f64)) {
        let c = 0xFF00_0000 | c;
        ui.rect(r[0], r[1], r[2], r[3], c & 0xFF_FFFF, 1.0);
        let lit = self.colour == c;
        let (rgb, a) = if lit { (0xFFFFFF, 1.0) } else if inside(r, mouse) { (0xFFFFFF, 0.7) } else { (0x000000, 0.5) };
        edge(ui, r, if lit { (2.0 * s).max(2.0) } else { s.max(1.0) }, rgb, a);
        self.hits.push((Hit::Swatch(c), r));
    }

    /// A submission's row: its sprite (and the item's now), what it's for, by whom or how it stands.
    fn row(&mut self, ui: &mut Ui, sub: &SpriteSub, r: [f32; 4], s: f32, with_author: bool, buttons_w: f32) {
        theme::fill(ui.renderer, r, 6.0 * s, color::INSET_TOP, 0.9);
        let slot = r[3] - 12.0 * s;
        let well = [r[0] + 6.0 * s, r[1] + 6.0 * s, slot, slot];
        ui.slot_box(well, None, false, s);
        if sub.size > 0 {
            let side = (slot * 0.66 / sub.size as f32).floor() * sub.size as f32;
            draw_pixels(ui, &sub.pixels, sub.size, (well[0] + (slot - side) / 2.0).round(), (well[1] + (slot - side) / 2.0).round(), side, true);
        }
        let mut tx = well[0] + slot + 10.0 * s;
        let mut item = None;
        if sub.kind == "item" {
            if self.by_id.is_none() {
                self.by_id = Some(ui.data.objects.values().map(|d| (d.id.clone(), d.object_type)).collect());
            }
            item = self.by_id.as_ref().and_then(|m| m.get(&sub.item)).copied();
            if let Some(t) = item {
                // the item as it looks now, beside the new look
                ui.item_icon(t as i32, 40, tx + 16.0 * s, r[1] + r[3] / 2.0, s);
                ui.label("\u{2039}", tx + 36.0 * s, r[1] + r[3] / 2.0 - 10.0 * s, (16.0 * s).round(), true, color::TEXT_MUTED);
                tx += 54.0 * s;
            }
        }
        let width = (r[0] + r[2] - buttons_w - tx - 8.0 * s).max(40.0);
        let big = (14.0 * s).round().max(9.0);
        let small = (12.0 * s).round().max(9.0);
        let title = if sub.kind == "new" {
            tf!("New item: {name} ({kind})", name = sub.name, kind = slot_label(&sub.slot))
        } else {
            let name = item.map_or_else(|| sub.item.clone(), |t| item_name(ui, t));
            tf!("New look for {name}", name = name)
        };
        let title = fit(ui, &format!("#{}  {title}", sub.id), big, true, width);
        ui.label(&title, tx, r[1] + 7.0 * s, big, true, color::TEXT);
        let (status, c) = status_line(sub);
        let second = if with_author { tf!("by {name}", name = sub.author) } else { status };
        let second = fit(ui, &second, small, false, width);
        ui.label(&second, tx, r[1] + 7.0 * s + big * 1.35, small, false, if with_author { color::GOLD } else { c });
        if !sub.note.is_empty() {
            let note = fit(ui, &format!("\u{201C}{}\u{201D}", sub.note), small, false, width);
            ui.label(&note, tx, r[1] + 7.0 * s + big * 1.35 + small * 1.35, small, false, color::TEXT_MUTED);
        }
    }

    /// My sprites: what the player sent in and what became of each.
    fn mine_tab(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let [bx, by, bw, bh] = body;
        theme::panel(ui.renderer, body, s);
        let pad = 16.0 * s;
        let size = (14.0 * s).round().max(9.0);
        if self.login.is_none() {
            let line = tr("Sign in to send in sprites and follow them here.");
            let lw = ui.measure(line, size, false);
            ui.label(line, bx + (bw - lw) / 2.0, by + bh / 2.0 - 40.0 * s, size, false, color::TEXT_DIM);
            self.button(ui, Hit::SignIn, [bx + (bw - 200.0 * s) / 2.0, by + bh / 2.0, 200.0 * s, 40.0 * s], tr("Sign in"), Kind::Primary, s, mouse);
            return;
        }
        let Some(list) = self.mine.clone() else {
            ui.label(tr("Loading\u{2026}"), bx + pad, by + pad, size, false, color::TEXT_DIM);
            return;
        };
        let waiting = list.sprites.iter().filter(|x| x.status == "pending").count();
        let head = tn!(waiting, "{n} waiting for review (at most {max} at once)|{n} waiting for review (at most {max} at once)", max = list.max_pending);
        ui.label(&head, bx + pad, by + pad, size, true, color::TEXT_DIM);
        self.button(ui, Hit::Refresh, [bx + bw - pad - 120.0 * s, by + pad - 6.0 * s, 120.0 * s, 30.0 * s], tr("Refresh"), Kind::Secondary, s, mouse);
        if list.sprites.is_empty() {
            let line = tr("Nothing sent in yet: draw something on the Draw tab and send it in.");
            ui.label(line, bx + pad, by + pad + 44.0 * s, size, false, color::TEXT_MUTED);
            return;
        }
        let top = by + pad + 36.0 * s;
        let row_h = 70.0 * s;
        let rows = ((by + bh - pad - top) / (row_h + 6.0 * s)).floor().max(1.0) as usize;
        self.rows_from = self.rows_from.min(list.sprites.len().saturating_sub(rows));
        let bw_ = 130.0 * s;
        for (i, sub) in list.sprites.iter().skip(self.rows_from).take(rows).enumerate() {
            let r = [bx + pad, top + i as f32 * (row_h + 6.0 * s), bw - 2.0 * pad, row_h];
            self.row(ui, sub, r, s, false, 2.0 * (bw_ + 8.0 * s));
            let b = |k: f32| [r[0] + r[2] - (k + 1.0) * (bw_ + 8.0 * s), r[1] + (row_h - 32.0 * s) / 2.0, bw_, 32.0 * s];
            self.button(ui, Hit::Copy(sub.id), b(0.0), tr("Draw a copy"), Kind::Secondary, s, mouse);
            if sub.status == "pending" {
                self.button(ui, Hit::Withdraw(sub.id), b(1.0), tr("Withdraw"), Kind::Danger, s, mouse);
            }
        }
    }

    /// Review (admins): approve or turn down what's waiting; take back an approval before the release.
    fn review_tab(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let [bx, by, bw, bh] = body;
        theme::panel(ui.renderer, body, s);
        let pad = 16.0 * s;
        let size = (14.0 * s).round().max(9.0);
        let small = (12.0 * s).round().max(9.0);
        let mut y = by + pad;
        Self::heading(ui, tr("Why it's turned down (the player sees this)"), bx + pad, y, s);
        self.button(ui, Hit::Refresh, [bx + bw - pad - 120.0 * s, y - 4.0 * s, 120.0 * s, 30.0 * s], tr("Refresh"), Kind::Secondary, s, mouse);
        y += 22.0 * s;
        self.text_field(ui, Field::Reason, [bx + pad, y, bw - 2.0 * pad - 130.0 * s, 34.0 * s], tr("Optional: too dark, doesn't fit the item\u{2026}"), s, now);
        y += 44.0 * s;
        let hint = tr("Approved sprites go into the game with the next update, and the item's tooltip credits whoever drew it.");
        for line in crate::screens::wrap(ui, hint, small, bw - 2.0 * pad) {
            ui.label(&line, bx + pad, y, small, false, color::TEXT_MUTED);
            y += small * 1.35;
        }
        y += 6.0 * s;
        let Some((waiting, approved)) = self.queue.clone() else {
            ui.label(tr("Loading\u{2026}"), bx + pad, y, size, false, color::TEXT_DIM);
            return;
        };
        let mut lines = vec![Line::Head(tn!(waiting.len(), "Waiting for review ({n})|Waiting for review ({n})"))];
        if waiting.is_empty() {
            lines.push(Line::Empty);
        }
        lines.extend(waiting.into_iter().map(Line::Waiting));
        lines.push(Line::Head(tn!(approved.len(), "Approved for the next update ({n})|Approved for the next update ({n})")));
        lines.extend(approved.into_iter().map(Line::Approved));
        let row_h = 70.0 * s;
        let bottom = by + bh - pad;
        self.rows_from = self.rows_from.min(lines.len().saturating_sub(1));
        let bw_ = 116.0 * s;
        for line in lines.into_iter().skip(self.rows_from) {
            let h = match line {
                Line::Head(_) | Line::Empty => 28.0 * s,
                _ => row_h + 6.0 * s,
            };
            if y + h > bottom + 1.0 {
                break;
            }
            let r = [bx + pad, y, bw - 2.0 * pad, row_h];
            let b = |k: f32| [r[0] + r[2] - (k + 1.0) * (bw_ + 8.0 * s), r[1] + (row_h - 32.0 * s) / 2.0, bw_, 32.0 * s];
            match line {
                Line::Head(text) => ui.label(&text, bx + pad, y + 6.0 * s, size, true, color::TEXT),
                Line::Empty => ui.label(tr("Nothing is waiting for review."), bx + pad, y + 4.0 * s, small, false, color::TEXT_MUTED),
                Line::Waiting(sub) => {
                    self.row(ui, &sub, r, s, true, 3.0 * (bw_ + 8.0 * s));
                    self.button(ui, Hit::Approve(sub.id), b(0.0), tr("Approve"), Kind::Primary, s, mouse);
                    self.button(ui, Hit::Reject(sub.id), b(1.0), tr("Turn down"), Kind::Danger, s, mouse);
                    self.button(ui, Hit::Copy(sub.id), b(2.0), tr("Open"), Kind::Secondary, s, mouse);
                }
                Line::Approved(sub) => {
                    self.row(ui, &sub, r, s, true, 2.0 * (bw_ + 8.0 * s));
                    self.button(ui, Hit::Reopen(sub.id), b(0.0), tr("Take back"), Kind::Secondary, s, mouse);
                    self.button(ui, Hit::Copy(sub.id), b(1.0), tr("Open"), Kind::Secondary, s, mouse);
                }
            }
            y += h;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const R: u32 = 0xFFFF_0000;
    const B: u32 = 0xFF00_00FF;

    #[test]
    fn fill_stays_inside_its_colour() {
        let mut c = Canvas::new(8);
        // a wall down column 3
        for y in 0..8 {
            c.set(3, y, R);
        }
        assert!(c.fill(0, 0, B));
        assert_eq!(c.get(2, 7), B);
        assert_eq!(c.get(3, 4), R);
        assert_eq!(c.get(4, 0), 0);
        assert!(!c.fill(0, 0, B));
    }

    #[test]
    fn sizes_and_colours() {
        let mut c = Canvas::new(8);
        c.set(1, 1, R);
        c.set(7, 7, B);
        let big = c.resized(16);
        assert_eq!((big.get(2, 2), big.get(3, 3), big.get(15, 15)), (R, R, B));
        assert_eq!(big.resized(8), c);
        assert_eq!(c.colours(16), vec![R, B]);
        assert!(Canvas::new(16).is_empty());
        assert_eq!(parse_hex("#ff0000"), Some(R));
        assert_eq!(parse_hex("00f"), None);
    }

    #[test]
    fn pixels_round_trip_through_the_server_text() {
        let mut c = Canvas::new(8);
        c.set(0, 0, R);
        c.set(5, 2, 0xFF12_3456);
        let text = acct::sprite_pixels_text(&c.px);
        assert_eq!(acct::parse_sprite_pixels(&text, 8).unwrap(), c.px);
    }

    #[test]
    fn mirrored_painting_and_undo() {
        let mut e = SpriteEditor { mirror: true, ..Default::default() };
        e.snapshot();
        assert!(e.paint(1, 2, R));
        assert_eq!((e.canvas.get(1, 2), e.canvas.get(6, 2)), (R, R));
        e.undo_step();
        assert!(e.canvas.is_empty());
        e.redo_step();
        assert_eq!(e.canvas.get(6, 2), R);
    }

    #[test]
    fn known_server_errors_are_translated_and_others_kept() {
        assert_eq!(server_text("Draw something first.".into()), "Draw something first.");
        assert_eq!(server_text("Something new".into()), "Something new");
    }
}
