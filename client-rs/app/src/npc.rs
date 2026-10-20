//! The NPCs of the Nexus and the guild hall, and the windows they open (the AIR client's
//! `CharacterChangerPanel`, `ReskinPanel`, `NameChangerPanel`, `Guild*Panel` and
//! `PotionsPanel`, and their frames): the Armoire's skins, choosing a name, creating or leaving
//! a guild, the guild board and member list, potion storage, and the Nexus's arena leaderboard.
//!
//! One window is open at a time, over the world. It takes the mouse and the keyboard until it
//! closes (Escape, its close button, or a click outside it).

use crate::hud::{Layout, Ui};
use crate::theme::{self, color, Kind};
use bsk_net::account::{ArenaRun, GuildMember};
use bsk_world::GameObj;
use winit::keyboard::KeyCode;

/// Account and guild stats (`StatData`).
pub mod stat {
    pub const NUM_STARS: u8 = 30;
    pub const NAME_CHOSEN: u8 = 56;
    pub const GUILD_NAME: u8 = 62;
    pub const GUILD_RANK: u8 = 63;
}

/// Changing a chosen name (`ChooseNameHandler.NAME_CHANGE_FAME`, fame; the AIR client's was
/// 100 gold), `GuildRegisterPanel`'s price (fame), and potion storage's upgrade
/// (`DbAccount.PotionStorageUpgradeCost`, fame).
pub const NAME_CHANGE_PRICE: i32 = 50_000;
pub const GUILD_PRICE: i32 = 1000;
pub const STORAGE_UPGRADE_PRICE: i32 = 500;

/// An NPC with a panel, by its object class.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum NpcKind {
    CharacterChanger,
    /// The Armoire (`ReskinVendor`).
    Reskin,
    NameChanger,
    GuildRegister,
    GuildBoard,
    GuildChronicle,
    PotionStorage,
    /// `MarketNPC`: the marketplace.
    Market,
    /// The Arena Leaderboard trophy in the Nexus's south yard: the best arena runs.
    ArenaLeaderboard,
    /// The Divine Forge in the Nexus's south yard: forging, and selling items for fame.
    Forge,
}

impl NpcKind {
    pub fn of_class(class: &str) -> Option<Self> {
        Some(match class {
            "CharacterChanger" => Self::CharacterChanger,
            "ReskinVendor" => Self::Reskin,
            "NameChanger" => Self::NameChanger,
            "GuildRegister" => Self::GuildRegister,
            "GuildBoard" => Self::GuildBoard,
            "GuildChronicle" => Self::GuildChronicle,
            "PotionStorage" => Self::PotionStorage,
            "MarketNPC" => Self::Market,
            "ArenaLeaderboard" => Self::ArenaLeaderboard,
            "Forge" => Self::Forge,
            _ => return None,
        })
    }
}

/// `GuildUtil.rankToString`.
pub fn rank_name(rank: i32) -> &'static str {
    match rank {
        r if r >= 40 => "Founder",
        r if r >= 30 => "Leader",
        r if r >= 20 => "Officer",
        r if r >= 10 => "Member",
        _ => "Initiate",
    }
}

fn int(me: &GameObj, id: u8) -> i32 {
    me.stat_int(id).unwrap_or(0)
}

fn guild(me: &GameObj) -> &str {
    me.stat_str(stat::GUILD_NAME).unwrap_or("")
}

/// What the interact area shows for an NPC: a line under its name, and the button (its label
/// and look), if there is one to press.
pub fn interact_text(kind: NpcKind, me: &GameObj, rank_req: i32) -> (String, Option<(String, Kind)>) {
    let open = |line: &str, button: &str| (line.to_owned(), Some((button.to_owned(), Kind::Primary)));
    match kind {
        NpcKind::CharacterChanger => open("Change characters", "Change"),
        NpcKind::Reskin => open("Change your skin", "Choose"),
        NpcKind::NameChanger => {
            if int(me, stat::NUM_STARS) < rank_req {
                (format!("Rank {rank_req} needed"), None)
            } else if int(me, stat::NAME_CHOSEN) != 0 {
                let name = me.name().unwrap_or("");
                (format!("Your name is {name}"), Some((format!("Change  ·  {} fame", crate::hud::group_digits(NAME_CHANGE_PRICE)), Kind::Primary)))
            } else {
                open("Choose your account name", "Choose")
            }
        }
        NpcKind::GuildRegister => {
            let g = guild(me);
            if g.is_empty() {
                (String::from("Create a guild"), Some((format!("Create  ·  {GUILD_PRICE} fame"), Kind::Primary)))
            } else {
                (format!("{} of {g}", rank_name(int(me, stat::GUILD_RANK))), Some(("Renounce".to_owned(), Kind::Danger)))
            }
        }
        NpcKind::GuildBoard => open("The guild's notices", "View"),
        NpcKind::GuildChronicle => open("The guild's members", "View"),
        NpcKind::PotionStorage => open("Store stat potions", "Open"),
        NpcKind::Market => open("Buy and sell items", "Manage"),
        NpcKind::ArenaLeaderboard => open("The arena's best runs", "View"),
        NpcKind::Forge => open("Forge items, or sell them for fame", "Open"),
    }
}

/// The eight stat potions storage keeps, in its order (`UseStorage`'s kind): name, the potion
/// and its greater form, the bar's colour.
pub const POTIONS: [(&str, i32, i32, u32); 8] = [
    ("Life", 2793, 9070, 0x5EDDDD),
    ("Mana", 2794, 9071, 0xFFEA55),
    ("Attack", 2591, 9064, 0xE87EE8),
    ("Defense", 2592, 9065, 0x9A9A9A),
    ("Speed", 2593, 9066, 0x70E08C),
    ("Dexterity", 2636, 9069, 0xFD9D3E),
    ("Vitality", 2612, 9067, 0xF53434),
    ("Wisdom", 2613, 9068, 0x77B5F2),
];

/// Which text a prompt asks for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Prompt {
    /// `ChooseNameFrame`: letters only, at most 10.
    Name,
    /// `CreateGuildFrame`: letters and spaces, at most 20.
    Guild,
}

impl Prompt {
    fn max(self) -> usize {
        match self {
            Prompt::Name => 10,
            Prompt::Guild => 20,
        }
    }

    fn allows(self, c: char) -> bool {
        c.is_ascii_alphabetic() || (self == Prompt::Guild && c == ' ')
    }
}

/// A window an NPC opened.
#[derive(Clone, Debug)]
pub enum Window {
    /// The Armoire: the class's skins (0 is the class's own look).
    Skins { class: u16, skins: Vec<u16> },
    Prompt { prompt: Prompt, input: String, error: Option<String>, busy: bool },
    /// Leave the guild?
    Renounce,
    /// The board's text once it has come, and the text being edited.
    Board { text: Option<String>, edit: Option<String>, error: Option<String> },
    Members { list: Option<(String, Vec<GuildMember>)>, page: usize, error: Option<String> },
    Potions { counts: Option<(Vec<i32>, i32)>, error: Option<String> },
    /// `/arena/leaderboard`, best first, once it has come.
    Leaderboard { runs: Option<Vec<ArenaRun>>, error: Option<String> },
}

/// What the player did in a window.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    Reskin(i32),
    ChooseName(String),
    CreateGuild(String),
    /// Leave the guild (`GuildRemove` with our own name).
    Renounce,
    SaveBoard(String),
    ChangeRank(String, i32),
    Remove(String),
    /// `UseStorage`: potion kind 0-7, action (0 store, 1 take, 2 drink, 3 drink to the
    /// maximum, 4 upgrade).
    Storage(u8, u8),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Hit {
    Close,
    Skin(u16),
    Submit,
    Edit,
    Page(i32),
    Promote(usize),
    Demote(usize),
    Remove(usize),
    Storage(u8, u8),
}

/// The open window and its buttons as last drawn.
#[derive(Default)]
pub struct Npcs {
    pub window: Option<Window>,
    rect: Option<[f32; 4]>,
    buttons: Vec<(Hit, [f32; 4])>,
    /// When to fetch the stored potions again, after using the storage.
    pub reload_at: Option<i32>,
}

/// Rows of the member list per page.
const MEMBER_ROWS: usize = 10;

/// Runs the leaderboard shows: the best in its own card, then the rest in rows.
pub const LEADERBOARD_RUNS: usize = 10;

/// A run's time: "12:05", "1:02:05", or a dash for runs from before times were kept.
pub fn run_time(seconds: i32) -> String {
    if seconds < 0 {
        return "—".to_owned();
    }
    let (h, m, s) = (seconds / 3600, seconds / 60 % 60, seconds % 60);
    if h > 0 { format!("{h}:{m:02}:{s:02}") } else { format!("{m}:{s:02}") }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

impl Npcs {
    pub fn open(&mut self, window: Window) {
        self.window = Some(window);
        self.rect = None;
        self.buttons.clear();
    }

    pub fn close(&mut self) {
        *self = Npcs::default();
    }

    /// A click: inside the window it may do something; outside, it closes the window.
    pub fn click(&mut self, pos: (f64, f64), me: Option<&GameObj>) -> Option<Act> {
        self.window.as_ref()?;
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.close();
            return None;
        }
        let hit = self.buttons.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h)?;
        if hit == Hit::Close || (hit == Hit::Submit && matches!(self.window, Some(Window::Renounce))) {
            self.close();
            return (hit == Hit::Submit).then_some(Act::Renounce);
        }
        let my_rank = me.map_or(0, |m| int(m, stat::GUILD_RANK));
        match (hit, self.window.as_mut()?) {
            (Hit::Skin(skin), _) => Some(Act::Reskin(skin as i32)),
            (Hit::Submit, Window::Prompt { prompt, input, busy, error }) => submit(*prompt, input, busy, error),
            (Hit::Edit, Window::Board { text, edit, .. }) => {
                if edit.is_none() {
                    *edit = Some(text.clone().unwrap_or_default());
                }
                None
            }
            (Hit::Submit, Window::Board { text, edit, .. }) => {
                let board = edit.take()?;
                *text = Some(board.clone());
                Some(Act::SaveBoard(board))
            }
            (Hit::Page(d), Window::Members { page, list, .. }) => {
                let n = list.as_ref().map_or(0, |l| l.1.len());
                let pages = n.div_ceil(MEMBER_ROWS).max(1) as i32;
                *page = (*page as i32 + d).clamp(0, pages - 1) as usize;
                None
            }
            (Hit::Promote(i) | Hit::Demote(i) | Hit::Remove(i), Window::Members { list: Some((_, members)), .. }) => {
                let m = members.get(i)?;
                match hit {
                    Hit::Promote(_) => Some(Act::ChangeRank(m.name.clone(), (m.rank + 10).min(my_rank))),
                    Hit::Demote(_) => Some(Act::ChangeRank(m.name.clone(), (m.rank - 10).max(0))),
                    _ => Some(Act::Remove(m.name.clone())),
                }
            }
            (Hit::Storage(kind, action), _) => Some(Act::Storage(kind, action)),
            _ => None,
        }
    }

    /// A key, while the window is open. Escape closes it; prompts and the board take text.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>) -> Option<Act> {
        let enter = matches!(code, KeyCode::Enter | KeyCode::NumpadEnter);
        match self.window.as_mut()? {
            Window::Board { edit: edit @ Some(_), .. } if code == KeyCode::Escape => *edit = None,
            _ if code == KeyCode::Escape => self.close(),
            Window::Renounce if enter => {
                self.close();
                return Some(Act::Renounce);
            }
            Window::Prompt { prompt, input, busy, error } => {
                if enter {
                    return submit(*prompt, input, busy, error);
                } else if code == KeyCode::Backspace {
                    input.pop();
                } else {
                    for c in text.unwrap_or("").chars().filter(|&c| prompt.allows(c)) {
                        if input.chars().count() < prompt.max() {
                            input.push(c);
                        }
                    }
                }
            }
            Window::Board { edit: Some(edit), .. } => {
                if enter {
                    edit.push('\n');
                } else if code == KeyCode::Backspace {
                    edit.pop();
                } else {
                    edit.extend(text.unwrap_or("").chars().filter(|c| !c.is_control()));
                }
                // GuildBoardWindow's limit.
                while edit.chars().count() > 1024 {
                    edit.pop();
                }
            }
            _ => {}
        }
        None
    }

    /// The server's answer to a name or a guild: close on success, else show why.
    pub fn answer(&mut self, prompt: Prompt, success: bool, why: &str) {
        if let Some(Window::Prompt { prompt: p, busy, error, .. }) = &mut self.window {
            if *p == prompt {
                if success {
                    self.close();
                } else {
                    *busy = false;
                    *error = Some(if why.is_empty() { "That didn't work.".to_owned() } else { why.to_owned() });
                }
            }
        }
    }

    /// Draw the window, centred over the world.
    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.buttons.clear();
        let Some(window) = &self.window else {
            self.rect = None;
            return;
        };
        let s = lay.s;
        let (w, h) = match window {
            Window::Skins { skins, .. } => (420.0, 120.0 + 112.0 * skins.len().div_ceil(4).max(1) as f32),
            Window::Prompt { .. } => (360.0, 230.0),
            Window::Renounce => (360.0, 180.0),
            Window::Board { .. } => (440.0, 400.0),
            // As tall as the list (paged past MEMBER_ROWS).
            Window::Members { list, .. } => {
                let n = list.as_ref().map_or(0, |l| l.1.len());
                let rows = n.clamp(3, MEMBER_ROWS);
                (440.0, 104.0 + 30.0 * rows as f32 + if n > MEMBER_ROWS { 64.0 } else { 0.0 })
            }
            Window::Potions { .. } => (480.0, 112.0 + 38.0 * 8.0 + 56.0),
            // the best run's card, then a row for each of the rest (room for three while loading)
            Window::Leaderboard { runs, .. } => {
                let rest = runs.as_ref().map_or(3, |r| r.len().saturating_sub(1)).min(LEADERBOARD_RUNS - 1);
                (540.0, 74.0 + 118.0 + 30.0 * rest as f32 + 10.0)
            }
        };
        // Never bigger than the view.
        let k = (lay.view_w() * 0.94 / (w * s)).min(lay.h * 0.94 / (h * s)).min(1.0);
        let s = s * k;
        let (w, h) = (w * s, h * s);
        let r = [((lay.view_w() - w) / 2.0).round(), ((lay.h - h) / 2.0).round(), w, h];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let title = match window {
            Window::Skins { .. } => "The Armoire".to_owned(),
            Window::Prompt { prompt: Prompt::Name, .. } => "Choose a name".to_owned(),
            Window::Prompt { prompt: Prompt::Guild, .. } => "Create a guild".to_owned(),
            Window::Renounce => "Leave the guild".to_owned(),
            Window::Board { .. } => "Guild board".to_owned(),
            Window::Members { list, .. } => list.as_ref().map_or("Guild members".to_owned(), |l| l.0.clone()),
            Window::Potions { .. } => "Potion storage".to_owned(),
            Window::Leaderboard { .. } => "Arena Leaderboard".to_owned(),
        };
        ui.label(&title, r[0] + 20.0 * s, r[1] + 16.0 * s, font(19.0), true, color::TEXT);
        // The close button, top right.
        let cb = [r[0] + r[2] - 40.0 * s, r[1] + 12.0 * s, 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.buttons.push((Hit::Close, cb));
        let body = [r[0] + 20.0 * s, r[1] + 54.0 * s, r[2] - 40.0 * s, r[3] - 74.0 * s];

        let mut buttons = Vec::new();
        match window {
            Window::Skins { class, skins } => {
                let current = me.map_or(0, |m| int(m, crate::hud::stat::TEXTURE)) as u16;
                ui.label("Pick a look for this character.", body[0], body[1], font(12.0), false, color::TEXT_DIM);
                let cell = (body[2] / 4.0).floor();
                for (i, &skin) in skins.iter().enumerate() {
                    let cr = [body[0] + (i % 4) as f32 * cell + 3.0 * s, body[1] + 26.0 * s + (i / 4) as f32 * 112.0 * s, cell - 6.0 * s, 104.0 * s];
                    let on = skin == current;
                    let over = inside(cr, mouse);
                    let edge = if on { color::ACCENT } else if over { color::BORDER_HI } else { color::BORDER };
                    theme::frame(ui.renderer, cr, 7.0 * s, s.max(1.0), (edge, 1.0), ((color::SLOT_TOP, 1.0), (color::SLOT_BOTTOM, 1.0)));
                    let desc = ui.data.objects.get(if skin == 0 { class } else { &skin });
                    if let Some(desc) = desc {
                        let (t1, t2) = me.map_or((0, 0), |m| (int(m, crate::hud::stat::TEX1), int(m, crate::hud::stat::TEX2)));
                        crate::screens::portrait(ui, desc, t1, t2, cr[0] + cr[2] / 2.0, cr[1] + 42.0 * s, s);
                        let name = if skin == 0 { "Classic".to_owned() } else { desc.display_id.clone() };
                        let ns = font(11.0);
                        let name = fit(ui, &name, ns, cr[2] - 8.0 * s);
                        let nw = ui.measure(&name, ns, true);
                        ui.label(&name, cr[0] + (cr[2] - nw) / 2.0, cr[1] + cr[3] - 24.0 * s, ns, true, if on { color::ACCENT_HI } else { color::TEXT });
                    }
                    if !on {
                        self.buttons.push((Hit::Skin(skin), cr));
                    }
                }
            }
            Window::Prompt { prompt, input, error, busy } => {
                let hint = match prompt {
                    Prompt::Name => match me {
                        Some(m) if int(m, stat::NAME_CHOSEN) != 0 => format!("Letters only, at most 10. Costs {} fame.", crate::hud::group_digits(NAME_CHANGE_PRICE)),
                        _ => "Letters only, at most 10. Choose carefully!".to_owned(),
                    },
                    Prompt::Guild => format!("Letters and spaces, at most 20. Costs {GUILD_PRICE} fame."),
                };
                ui.label(&hint, body[0], body[1], font(12.0), false, color::TEXT_DIM);
                let fr = [body[0], body[1] + 30.0 * s, body[2], 40.0 * s];
                ui.field_box(fr, true, s);
                let fs = font(16.0);
                ui.label(input, fr[0] + 12.0 * s, fr[1] + (fr[3] - fs * 1.22) / 2.0, fs, false, color::TEXT);
                if (now / 500) % 2 == 0 && !busy {
                    let cx = fr[0] + 12.0 * s + ui.measure(input, fs, false) + s;
                    ui.rect(cx, fr[1] + 9.0 * s, (1.5 * s).max(1.0), fr[3] - 18.0 * s, color::ACCENT, 1.0);
                }
                if let Some(e) = error {
                    ui.label(e, body[0], fr[1] + fr[3] + 10.0 * s, font(12.0), true, color::DANGER);
                }
                let label = match (prompt, busy) {
                    (_, true) => "Wait…",
                    (Prompt::Name, _) => "Choose",
                    (Prompt::Guild, _) => "Create",
                };
                buttons.push((Hit::Submit, label.to_owned(), Kind::Primary));
            }
            Window::Renounce => {
                let g = me.map_or("", guild).to_owned();
                ui.label(&format!("Leave {g}?"), body[0], body[1], font(15.0), true, color::TEXT);
                ui.label("You'll need an invitation to come back.", body[0], body[1] + 26.0 * s, font(12.0), false, color::TEXT_DIM);
                buttons.push((Hit::Submit, "Renounce".to_owned(), Kind::Danger));
            }
            Window::Board { text, edit, error } => {
                let box_r = [body[0], body[1], body[2], body[3] - 50.0 * s];
                ui.field_box(box_r, edit.is_some(), s);
                let ts = font(13.0);
                let shown = edit.as_deref().or(text.as_deref());
                match shown {
                    None => ui.label("Loading…", box_r[0] + 12.0 * s, box_r[1] + 10.0 * s, ts, false, color::TEXT_MUTED),
                    Some(t) if t.trim().is_empty() && edit.is_none() => {
                        ui.label("The board is empty.", box_r[0] + 12.0 * s, box_r[1] + 10.0 * s, ts, false, color::TEXT_MUTED)
                    }
                    Some(t) => {
                        let lines = wrap(ui, t, ts, box_r[2] - 24.0 * s);
                        let line_h = ts * 1.3;
                        let room = ((box_r[3] - 20.0 * s) / line_h).floor().max(1.0) as usize;
                        // While editing, keep the end (where typing goes) in sight.
                        let skip = if edit.is_some() { lines.len().saturating_sub(room) } else { 0 };
                        let mut y = box_r[1] + 10.0 * s;
                        for line in lines.iter().skip(skip).take(room) {
                            ui.label(line, box_r[0] + 12.0 * s, y, ts, false, color::TEXT);
                            y += line_h;
                        }
                        if edit.is_some() && (now / 500) % 2 == 0 {
                            let last = lines.last().map_or("", String::as_str);
                            let cx = box_r[0] + 12.0 * s + ui.measure(last, ts, false) + s;
                            ui.rect(cx, y - line_h, (1.5 * s).max(1.0), ts * 1.15, color::ACCENT, 1.0);
                        }
                    }
                }
                if let Some(e) = error {
                    ui.label(e, body[0], body[1] + body[3] - 30.0 * s, font(12.0), true, color::DANGER);
                }
                // Officers and up may write on it.
                if me.is_some_and(|m| int(m, stat::GUILD_RANK) >= 20) && text.is_some() {
                    if edit.is_some() {
                        buttons.push((Hit::Submit, "Save".to_owned(), Kind::Primary));
                    } else {
                        buttons.push((Hit::Edit, "Edit".to_owned(), Kind::Secondary));
                    }
                }
            }
            Window::Members { list, page, error } => {
                let my_rank = me.map_or(0, |m| int(m, stat::GUILD_RANK));
                let my_name = me.and_then(|m| m.name()).unwrap_or("").to_owned();
                let ts = font(12.5);
                match list {
                    None => {
                        let t = error.as_deref().unwrap_or("Loading…");
                        ui.label(t, body[0], body[1], ts, false, if error.is_some() { color::DANGER } else { color::TEXT_MUTED });
                    }
                    Some((_, members)) => {
                        let small = font(10.5);
                        for (x, label) in [(0.0, "NAME"), (150.0, "RANK"), (240.0, "FAME")] {
                            ui.label(label, body[0] + x * s, body[1], small, true, color::TEXT_MUTED);
                        }
                        for (row, (i, m)) in members.iter().enumerate().skip(page * MEMBER_ROWS).take(MEMBER_ROWS).enumerate() {
                            let y = body[1] + 20.0 * s + row as f32 * 30.0 * s;
                            let rr = [body[0] - 6.0 * s, y, body[2] + 12.0 * s, 28.0 * s];
                            if row % 2 == 0 {
                                theme::fill(ui.renderer, rr, 4.0 * s, 0xFFFFFF, 0.03);
                            }
                            let ty = y + (28.0 * s - ts * 1.22) / 2.0;
                            let me_row = m.name == my_name;
                            ui.label(&m.name, body[0], ty, ts, true, if me_row { color::ACCENT_HI } else { color::TEXT });
                            ui.label(rank_name(m.rank), body[0] + 150.0 * s, ty, ts, false, color::TEXT_DIM);
                            ui.label(&m.fame.to_string(), body[0] + 240.0 * s, ty, ts, false, color::TEXT_DIM);
                            // Officers manage those below them (MemberListLine).
                            if my_rank >= 20 && m.rank < my_rank && !me_row {
                                let mut x = body[0] + body[2];
                                for (hit, label, kind, ok) in [
                                    (Hit::Remove(i), "×", Kind::Danger, true),
                                    (Hit::Demote(i), "-", Kind::Secondary, m.rank > 0),
                                    (Hit::Promote(i), "+", Kind::Secondary, m.rank + 10 < my_rank),
                                ] {
                                    x -= 30.0 * s;
                                    if ok {
                                        let br = [x, y + 2.0 * s, 26.0 * s, 24.0 * s];
                                        ui.button(br, label, kind, inside(br, mouse), s * 0.85);
                                        self.buttons.push((hit, br));
                                    }
                                }
                            }
                        }
                        let pages = members.len().div_ceil(MEMBER_ROWS).max(1);
                        if pages > 1 {
                            let t = format!("Page {} of {pages}", page + 1);
                            ui.label(&t, body[0], body[1] + body[3] - 30.0 * s, font(12.0), false, color::TEXT_DIM);
                            buttons.push((Hit::Page(1), "Next".to_owned(), Kind::Secondary));
                            buttons.push((Hit::Page(-1), "Back".to_owned(), Kind::Secondary));
                        }
                        if let Some(e) = error {
                            ui.label(e, body[0], body[1] + body[3] - 30.0 * s, font(12.0), true, color::DANGER);
                        }
                    }
                }
            }
            Window::Potions { counts, error } => {
                let ts = font(12.5);
                match counts {
                    None => {
                        let t = error.as_deref().unwrap_or("Loading…");
                        ui.label(t, body[0], body[1], ts, false, if error.is_some() { color::DANGER } else { color::TEXT_MUTED });
                    }
                    Some((counts, capacity)) => {
                        ui.label(&format!("Holds {capacity} of each"), body[0], body[1], font(12.0), false, color::TEXT_DIM);
                        for (k, (name, potion, _, c)) in POTIONS.iter().enumerate() {
                            let y = body[1] + 24.0 * s + k as f32 * 38.0 * s;
                            let count = counts.get(k).copied().unwrap_or(0);
                            let icon = [body[0], y, 34.0 * s, 34.0 * s];
                            ui.slot_box(icon, None, false, s);
                            ui.item_icon(*potion, 80, icon[0] + icon[2] / 2.0, icon[1] + icon[3] / 2.0, s * 0.8);
                            ui.label(name, body[0] + 42.0 * s, y + 1.0 * s, ts, true, color::TEXT);
                            // How full, as a bar.
                            let bar = [body[0] + 42.0 * s, y + 21.0 * s, 110.0 * s, 8.0 * s];
                            theme::fill(ui.renderer, bar, 4.0 * s, color::TRACK, 1.0);
                            let f = (count as f32 / (*capacity).max(1) as f32).clamp(0.0, 1.0);
                            if f > 0.0 {
                                theme::fill(ui.renderer, [bar[0], bar[1], (bar[2] * f).max(bar[3]), bar[3]], 4.0 * s, *c, 1.0);
                            }
                            let n = format!("{count}/{capacity}");
                            let small = font(11.0);
                            ui.label(&n, bar[0] + bar[2] + 8.0 * s, y + 16.0 * s, small, true, color::TEXT_DIM);
                            let mut x = body[0] + body[2];
                            for (action, label) in [(3u8, "Max"), (2, "Drink"), (1, "Take"), (0, "Store")] {
                                let bw = 50.0 * s;
                                x -= bw + 4.0 * s;
                                let br = [x, y + 3.0 * s, bw, 28.0 * s];
                                let kind = if action == 0 { Kind::Primary } else { Kind::Secondary };
                                ui.button(br, label, kind, inside(br, mouse), s * 0.8);
                                self.buttons.push((Hit::Storage(k as u8, action), br));
                            }
                        }
                        buttons.push((Hit::Storage(0, 4), format!("Upgrade  ·  {STORAGE_UPGRADE_PRICE} fame"), Kind::Secondary));
                        if let Some(e) = error {
                            ui.label(e, body[0], body[1] + body[3] - 30.0 * s, font(12.0), true, color::DANGER);
                        }
                    }
                }
            }
            Window::Leaderboard { runs, error } => {
                let ts = font(12.5);
                match runs {
                    None => {
                        let t = error.as_deref().unwrap_or("Loading…");
                        ui.label(t, body[0], body[1], ts, false, if error.is_some() { color::DANGER } else { color::TEXT_MUTED });
                    }
                    Some(runs) if runs.is_empty() => {
                        ui.label("No runs yet. Survive waves in the Battle Arena or /arena to get on the board.", body[0], body[1], ts, false, color::TEXT_MUTED);
                    }
                    Some(runs) => draw_runs(ui, runs, body, s, me.and_then(|m| m.name()).unwrap_or("")),
                }
            }
        }
        // The window's own buttons, right-aligned along the bottom.
        let mut x = r[0] + r[2] - 20.0 * s;
        for (hit, label, kind) in buttons {
            let bw = (ui.measure(&label, (15.0 * s).round().max(10.0), true) + 40.0 * s).max(110.0 * s);
            x -= bw;
            let br = [x, r[1] + r[3] - 56.0 * s, bw, 38.0 * s];
            ui.button(br, &label, kind, inside(br, mouse), s);
            self.buttons.push((hit, br));
            x -= 10.0 * s;
        }
    }
}

/// Send what the prompt holds, once: it waits for the server's answer.
fn submit(prompt: Prompt, input: &str, busy: &mut bool, error: &mut Option<String>) -> Option<Act> {
    let name = input.trim().to_owned();
    if name.is_empty() || *busy {
        return None;
    }
    (*busy, *error) = (true, None);
    Some(if prompt == Prompt::Name { Act::ChooseName(name) } else { Act::CreateGuild(name) })
}

/// The leaderboard's runs in `body`: the best one in a gold-edged card (its wave, time, arena
/// and everyone who took part), then the rest a row each. The viewer's own name stands out.
fn draw_runs(ui: &mut Ui, runs: &[ArenaRun], body: [f32; 4], s: f32, me: &str) {
    const GOLD: u32 = 0xFFD24A;
    let font = |size: f32| (size * s).round().max(9.0);
    let names = |r: &ArenaRun| if r.players.is_empty() { "Unknown".to_owned() } else { r.players.join(", ") };
    let mine = |r: &ArenaRun| r.players.iter().any(|p| p == me);

    // the best run
    let best = &runs[0];
    let card = [body[0], body[1], body[2], 108.0 * s];
    theme::frame(ui.renderer, card, 8.0 * s, s.max(1.0), (GOLD, 0.9), ((color::SLOT_TOP, 1.0), (color::SLOT_BOTTOM, 1.0)));
    let (x, mut y) = (card[0] + 16.0 * s, card[1] + 12.0 * s);
    let big = font(22.0);
    ui.label("#1", x, y, big, true, GOLD);
    let wave = format!("Wave {}", best.wave);
    let wx = x + ui.measure("#1", big, true) + 14.0 * s;
    ui.label(&wave, wx, y, big, true, color::TEXT);
    let tx = wx + ui.measure(&wave, big, true) + 14.0 * s;
    let small = font(13.0);
    let detail = if best.arena.is_empty() { run_time(best.seconds) } else { format!("{}  ·  {}", run_time(best.seconds), best.arena) };
    ui.label(&detail, tx, y + (big - small) * 0.8, small, false, color::TEXT_DIM);
    y += big * 1.35;
    ui.label("Time taken and the players who took part", x, y, font(10.5), true, color::TEXT_MUTED);
    y += font(10.5) * 1.5;
    let ps = font(12.5);
    let lines = wrap(ui, &names(best), ps, card[2] - 32.0 * s);
    let shown = lines.len().min(2);
    for (i, line) in lines.iter().take(shown).enumerate() {
        let line = if i + 1 == shown && lines.len() > shown { fit(ui, &format!("{line} …"), ps, card[2] - 32.0 * s) } else { line.clone() };
        ui.label(&line, x, y, ps, false, if mine(best) { color::ACCENT_HI } else { color::TEXT });
        y += ps * 1.3;
    }

    // the rest
    let ts = font(12.5);
    let top = card[1] + card[3] + 10.0 * s;
    for (i, r) in runs.iter().enumerate().skip(1).take(LEADERBOARD_RUNS - 1) {
        let ry = top + (i - 1) as f32 * 30.0 * s;
        let rr = [body[0] - 6.0 * s, ry, body[2] + 12.0 * s, 28.0 * s];
        if i % 2 == 1 {
            theme::fill(ui.renderer, rr, 4.0 * s, 0xFFFFFF, 0.03);
        }
        let ty = ry + (28.0 * s - ts * 1.22) / 2.0;
        let c = if mine(r) { color::ACCENT_HI } else { color::TEXT };
        ui.label(&format!("#{}", i + 1), body[0], ty, ts, true, color::TEXT_DIM);
        ui.label(&format!("Wave {}", r.wave), body[0] + 40.0 * s, ty, ts, true, c);
        ui.label(&run_time(r.seconds), body[0] + 122.0 * s, ty, ts, false, color::TEXT_DIM);
        let px = body[0] + 190.0 * s;
        let who = fit(ui, &names(r), ts, body[0] + body[2] - px);
        ui.label(&who, px, ty, ts, false, c);
    }
}

/// Cut a line to fit `width`, with an ellipsis.
fn fit(ui: &mut Ui, text: &str, size: f32, width: f32) -> String {
    if ui.measure(text, size, true) <= width {
        return text.to_owned();
    }
    let mut t: String = text.to_owned();
    while !t.is_empty() && ui.measure(&format!("{t}…"), size, true) > width {
        t.pop();
    }
    format!("{t}…")
}

/// Lines of `text` that fit `width`: its own line breaks, then breaks between words.
pub fn wrap(ui: &mut Ui, text: &str, size: f32, width: f32) -> Vec<String> {
    let mut out = Vec::new();
    for para in text.split('\n') {
        let mut line = String::new();
        for word in para.split(' ') {
            let candidate = if line.is_empty() { word.to_owned() } else { format!("{line} {word}") };
            if !line.is_empty() && ui.measure(&candidate, size, false) > width {
                out.push(std::mem::take(&mut line));
                line = word.to_owned();
            } else {
                line = candidate;
            }
        }
        out.push(line);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn prompts_filter_text() {
        let mut npcs = Npcs::default();
        npcs.open(Window::Prompt { prompt: Prompt::Name, input: String::new(), error: None, busy: false });
        npcs.key(KeyCode::KeyA, Some("Ab c1defghijklm"));
        let Some(Window::Prompt { input, .. }) = &npcs.window else { panic!() };
        assert_eq!(input, "Abcdefghij");
        assert_eq!(npcs.key(KeyCode::Enter, None), Some(Act::ChooseName("Abcdefghij".into())));
        // Waiting for the answer: Enter again does nothing; a failure shows why.
        assert_eq!(npcs.key(KeyCode::Enter, None), None);
        npcs.answer(Prompt::Name, false, "Duplicated name");
        let Some(Window::Prompt { error, busy, .. }) = &npcs.window else { panic!() };
        assert_eq!((error.as_deref(), *busy), (Some("Duplicated name"), false));
        npcs.answer(Prompt::Name, true, "");
        assert!(npcs.window.is_none());

        npcs.open(Window::Prompt { prompt: Prompt::Guild, input: String::new(), error: None, busy: false });
        npcs.key(KeyCode::KeyA, Some("The Order!"));
        assert_eq!(npcs.key(KeyCode::Enter, None), Some(Act::CreateGuild("The Order".into())));
    }

    #[test]
    fn ranks_and_classes() {
        assert_eq!(rank_name(0), "Initiate");
        assert_eq!(rank_name(20), "Officer");
        assert_eq!(rank_name(40), "Founder");
        assert_eq!(NpcKind::of_class("ReskinVendor"), Some(NpcKind::Reskin));
        assert_eq!(NpcKind::of_class("Forge"), Some(NpcKind::Forge));
        assert_eq!(NpcKind::of_class("SkillTree"), None);
    }
}
