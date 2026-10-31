//! The NPCs of the Nexus and the guild hall, and the windows they open (the AIR client's
//! `CharacterChangerPanel`, `ReskinPanel`, `NameChangerPanel`, `Guild*Panel` and
//! `PotionsPanel`, and their frames): the Armoire's skins, choosing a name, creating or leaving
//! a guild, the guild board and member list, potion storage, the Nexus's arena leaderboard, and
//! its Hall of Champions (drawn by `champions.rs`).
//!
//! One window is open at a time, over the world. It takes the mouse and the keyboard until it
//! closes (Escape, its close button, or a click outside it).

use crate::hud::{Layout, Ui};
use crate::theme::{self, color, Kind};
use bsk_net::account::{ArenaRun, Guild};
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
/// 100 gold), `GuildRegisterPanel`'s price (fame; the Guild Register sends its own, 0 during a
/// free guilds event, and older servers none), and potion storage's upgrade
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
    /// The Divine Forge in the Nexus's forge hall: forging, and selling items for fame.
    Forge,
    /// The Enchanter beside it: imprinting gear.
    Enchanter,
    /// The Tinkerer by the Nexus spawn: the quest log's quests.
    QuestMaster,
    /// The Chronicler by the Nexus spawn: the quest log's daily rewards and achievements.
    Chronicler,
    /// The Champions Monument south of the Nexus spawn: the Hall of Champions' boards.
    ChampionsBoard,
    /// A champion's statue round it: the Hall, at its board.
    ChampionStatue,
    /// The Herald in the Nexus's leaderboard court, west of the spawn: the server's week.
    Herald,
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
            "Enchanter" => Self::Enchanter,
            "QuestMaster" => Self::QuestMaster,
            "Chronicler" => Self::Chronicler,
            "ChampionsBoard" => Self::ChampionsBoard,
            "ChampionStatue" => Self::ChampionStatue,
            "Herald" => Self::Herald,
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

/// What making a guild costs, from the price the Guild Register sent: `0` is free.
pub fn guild_price_text(price: Option<i32>) -> String {
    match price.unwrap_or(GUILD_PRICE) {
        0 => "free".to_owned(),
        p => format!("{} fame", crate::hud::group_digits(p)),
    }
}

/// What the interact area shows for an NPC: a line under its name, and the button (its label
/// and look), if there is one to press. `price`: what the NPC sends (the Guild Register's).
pub fn interact_text(kind: NpcKind, me: &GameObj, rank_req: i32, price: Option<i32>) -> (String, Option<(String, Kind)>) {
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
                (String::from("Create a guild"), Some((format!("Create  ·  {}", guild_price_text(price)), Kind::Primary)))
            } else {
                (format!("{} of {g}", rank_name(int(me, stat::GUILD_RANK))), Some(("Renounce".to_owned(), Kind::Danger)))
            }
        }
        NpcKind::GuildBoard => open("The guild's notices", "View"),
        NpcKind::GuildChronicle => open("The guild's members", "View"),
        NpcKind::PotionStorage => open("Store stat potions", "Open"),
        NpcKind::Market => open("Buy and sell items", "Manage"),
        NpcKind::ArenaLeaderboard => open("The arena's best runs", "View"),
        NpcKind::Forge | NpcKind::Enchanter => open("The Divine Workbench", "Open"),
        NpcKind::QuestMaster => open("Daily, weekly and monthly quests", "Quests"),
        NpcKind::Chronicler => open("Daily rewards and achievements", "Open"),
        NpcKind::ChampionsBoard => open("The living's best, and their rewards", "View"),
        NpcKind::ChampionStatue => open("A champion of the Hall", "View"),
        NpcKind::Herald => open("This week in the realm", "View"),
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
    Members { list: Option<Guild>, page: usize, error: Option<String> },
    Potions { counts: Option<(Vec<i32>, i32)>, error: Option<String> },
    /// `/arena/leaderboard`, best first, once it has come: solo runs or group runs.
    Leaderboard { solo: bool, runs: Option<Vec<ArenaRun>>, error: Option<String> },
    /// `/champions/board`: the Hall of Champions.
    Champions(Box<crate::champions::View>),
    /// `/stats/weekly`: the server's week, once it has come.
    Weekly { stats: Option<bsk_net::account::WeeklyStats>, error: Option<String> },
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
    Tab(usize),
    Solo(bool),
}

/// The open window and its buttons as last drawn.
#[derive(Default)]
pub struct Npcs {
    pub window: Option<Window>,
    rect: Option<[f32; 4]>,
    buttons: Vec<(Hit, [f32; 4])>,
    /// When to fetch the stored potions again, after using the storage.
    pub reload_at: Option<i32>,
    /// The price the Guild Register sent when its window was opened.
    pub guild_price: Option<i32>,
}

/// Rows of the member list per page.
const MEMBER_ROWS: usize = 10;

/// The guild window's summary above its members, unscaled.
const GUILD_HEAD: f32 = 88.0;

fn thousands(n: i64) -> String {
    crate::hud::group_digits(n.clamp(0, i32::MAX as i64) as i32)
}

/// The guild's summary at the top of its window: its hall and place on the Hall of Champions, then
/// its guild fame, vault and loot boost, and the next hall. Returns its height.
fn guild_head(ui: &mut Ui, g: &Guild, body: [f32; 4], s: f32, font: impl Fn(f32) -> f32) -> f32 {
    if g.level == 0 {
        return 0.0; // an older server: the members only
    }
    let (x, y) = (body[0], body[1]);
    let hall = format!("Guild Hall {} of 4", g.level);
    ui.label(&hall, x, y, font(14.0), true, color::GOLD);
    let hw = ui.measure(&hall, font(14.0), true);
    let on = |board: Option<(i32, i64)>, name: &str| match board {
        Some((rank, fame)) if rank <= 10 => format!("#{rank} {name} ({})", thousands(fame)),
        Some((_, fame)) => format!("{name} {}", thousands(fame)),
        None => format!("{name} -"),
    };
    let place = format!("Hall of Champions: {}  ·  {}", on(g.living, "Living"), on(g.dead, "Dead"));
    let pw = (body[2] - hw - 16.0 * s).max(0.0);
    let place = fit(ui, &place, font(11.5), pw);
    ui.label(&place, x + hw + 16.0 * s, y + 2.0 * s, font(11.5), false, color::TEXT_DIM);
    // the hall's buffs for every member (an older server sends none: it gave 1% loot)
    let buffs = if g.buffs.is_empty() { "+1% loot".to_owned() } else { short_buffs(&g.buffs) };
    let buffs = fit(ui, &format!("Every member: {buffs}"), font(11.5), body[2]);
    ui.label(&buffs, x, y + 22.0 * s, font(11.5), true, color::GOOD);
    let line = format!(
        "Guild fame {} ({} earned)  ·  Vault {}/{}",
        thousands(g.fame),
        thousands(g.total_fame),
        g.vault_used,
        g.vault_slots
    );
    let line = fit(ui, &line, font(11.5), body[2]);
    ui.label(&line, x, y + 40.0 * s, font(11.5), false, color::TEXT);
    let next = match (g.next_price > 0, g.next_buffs.is_empty()) {
        (true, false) => format!("Next hall, {} guild fame: {}", thousands(g.next_price), short_buffs(&g.next_buffs)),
        (true, true) => format!("Next hall: {} guild fame, bought by a leader in the hall.  /ginfo  /gvault", thousands(g.next_price)),
        _ => "The guild has the grandest hall.  /ginfo  /gvault".to_owned(),
    };
    let next = fit(ui, &next, font(11.0), body[2]);
    ui.label(&next, x, y + 58.0 * s, font(11.0), false, color::TEXT_MUTED);
    GUILD_HEAD * s
}

/// A hall's buffs as the server words them, in the stats panel's short names to fit the window:
/// "+40 health and mana" is "+40 HP and MP".
fn short_buffs(text: &str) -> String {
    [("experience", "XP"), ("health", "HP"), ("mana", "MP"), ("vitality", "VIT"), ("wisdom", "WIS")]
        .iter()
        .fold(text.to_owned(), |s, (long, short)| s.replace(long, short))
}

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
                let n = list.as_ref().map_or(0, |l| l.members.len());
                let pages = n.div_ceil(MEMBER_ROWS).max(1) as i32;
                *page = (*page as i32 + d).clamp(0, pages - 1) as usize;
                None
            }
            (Hit::Promote(i) | Hit::Demote(i) | Hit::Remove(i), Window::Members { list: Some(guild), .. }) => {
                let m = guild.members.get(i)?;
                match hit {
                    Hit::Promote(_) => Some(Act::ChangeRank(m.name.clone(), (m.rank + 10).min(my_rank))),
                    Hit::Demote(_) => Some(Act::ChangeRank(m.name.clone(), (m.rank - 10).max(0))),
                    _ => Some(Act::Remove(m.name.clone())),
                }
            }
            (Hit::Storage(kind, action), _) => Some(Act::Storage(kind, action)),
            (Hit::Tab(i), Window::Champions(view)) => {
                view.tab = i;
                None
            }
            (Hit::Solo(on), Window::Leaderboard { solo, runs, error }) => {
                if *solo != on {
                    (*solo, *runs, *error) = (on, None, None);
                    self.reload_at = Some(0);
                }
                None
            }
            _ => None,
        }
    }

    /// A key, while the window is open. Escape closes it; prompts and the board take text.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>) -> Option<Act> {
        let enter = matches!(code, KeyCode::Enter | KeyCode::NumpadEnter);
        match self.window.as_mut()? {
            Window::Board { edit: edit @ Some(_), .. } if code == KeyCode::Escape => *edit = None,
            _ if code == KeyCode::Escape => self.close(),
            Window::Champions(view) if matches!(code, KeyCode::ArrowLeft | KeyCode::ArrowRight) => {
                view.step(if code == KeyCode::ArrowLeft { -1 } else { 1 });
            }
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
            // The guild's summary, then as tall as the list (paged past MEMBER_ROWS).
            Window::Members { list, .. } => {
                let n = list.as_ref().map_or(0, |l| l.members.len());
                let rows = n.clamp(3, MEMBER_ROWS);
                (500.0, 104.0 + GUILD_HEAD + 30.0 * rows as f32 + if n > MEMBER_ROWS { 64.0 } else { 0.0 })
            }
            Window::Potions { .. } => (480.0, 112.0 + 38.0 * 8.0 + 56.0),
            // the best run's card, then a row for each of the rest (room for three while loading)
            Window::Leaderboard { runs, .. } => {
                let rest = runs.as_ref().map_or(3, |r| r.len().saturating_sub(1)).min(LEADERBOARD_RUNS - 1);
                (540.0, 74.0 + 44.0 + 118.0 + 30.0 * rest as f32 + 10.0)
            }
            Window::Champions(_) => crate::champions::SIZE,
            // three rows of three cards, then the most slain
            Window::Weekly { .. } => (600.0, 74.0 + 28.0 + 3.0 * 96.0 + 112.0),
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
            Window::Members { list, .. } => list.as_ref().map_or("Guild members".to_owned(), |l| l.name.clone()),
            Window::Potions { .. } => "Potion storage".to_owned(),
            Window::Leaderboard { .. } => "Arena Leaderboard".to_owned(),
            Window::Champions(_) => "Hall of Champions".to_owned(),
            Window::Weekly { .. } => "This week in the realm".to_owned(),
        };
        let title_color = if matches!(window, Window::Champions(_) | Window::Weekly { .. }) { color::GOLD } else { color::TEXT };
        ui.label(&title, r[0] + 20.0 * s, r[1] + 16.0 * s, font(19.0), true, title_color);
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
                    Prompt::Guild => match self.guild_price.unwrap_or(GUILD_PRICE) {
                        0 => "Letters and spaces, at most 20. Free during the guild event!".to_owned(),
                        _ => format!("Letters and spaces, at most 20. Costs {}.", guild_price_text(self.guild_price)),
                    },
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
                    Some(guild) => {
                        let members = &guild.members;
                        let small = font(10.5);
                        let top = body[1] + guild_head(ui, guild, body, s, font);
                        for (x, label) in [(0.0, "NAME"), (150.0, "RANK"), (230.0, "GAVE"), (300.0, "LIVING")] {
                            ui.label(label, body[0] + x * s, top, small, true, color::TEXT_MUTED);
                        }
                        for (row, (i, m)) in members.iter().enumerate().skip(page * MEMBER_ROWS).take(MEMBER_ROWS).enumerate() {
                            let y = top + 20.0 * s + row as f32 * 30.0 * s;
                            let rr = [body[0] - 6.0 * s, y, body[2] + 12.0 * s, 28.0 * s];
                            if row % 2 == 0 {
                                theme::fill(ui.renderer, rr, 4.0 * s, 0xFFFFFF, 0.03);
                            }
                            let ty = y + (28.0 * s - ts * 1.22) / 2.0;
                            let me_row = m.name == my_name;
                            ui.label(&m.name, body[0], ty, ts, true, if me_row { color::ACCENT_HI } else { color::TEXT });
                            ui.label(rank_name(m.rank), body[0] + 150.0 * s, ty, ts, false, color::TEXT_DIM);
                            ui.label(&thousands(m.fame as i64), body[0] + 230.0 * s, ty, ts, false, color::TEXT_DIM);
                            let alive = if m.away { "away".to_owned() } else { m.alive.map_or("-".to_owned(), thousands) };
                            ui.label(&alive, body[0] + 300.0 * s, ty, ts, false, color::TEXT_DIM);
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
            Window::Leaderboard { solo, runs, error } => {
                let ts = font(12.5);
                // the Solo and Group tabs, then the runs under them
                let cs = font(13.0);
                let mut tx = body[0];
                for (on, label) in [(true, "Solo"), (false, "Group")] {
                    let tr = [tx, body[1], ui.measure(label, cs, true) + 36.0 * s, 30.0 * s];
                    crate::champions::chip(ui, tr, label, *solo == on, inside(tr, mouse), cs, None, s);
                    self.buttons.push((Hit::Solo(on), tr));
                    tx += tr[2] + 6.0 * s;
                }
                let body = [body[0], body[1] + 44.0 * s, body[2], body[3] - 44.0 * s];
                match runs {
                    None => {
                        let t = error.as_deref().unwrap_or("Loading…");
                        ui.label(t, body[0], body[1], ts, false, if error.is_some() { color::DANGER } else { color::TEXT_MUTED });
                    }
                    Some(runs) if runs.is_empty() => {
                        let t = if *solo { "No solo runs yet. Survive waves in the Arena (/arena) alone to get on the board." } else { "No group runs yet. Survive waves in the Arena (/arena) with others to get on the board." };
                        ui.label(t, body[0], body[1], ts, false, color::TEXT_MUTED);
                    }
                    Some(runs) => draw_runs(ui, runs, body, s, me.and_then(|m| m.name()).unwrap_or("")),
                }
            }
            Window::Champions(view) => {
                let name = me.and_then(|m| m.name()).unwrap_or("");
                let guild = me.and_then(|m| m.stat_str(stat::GUILD_NAME)).unwrap_or("");
                for (i, tr) in crate::champions::draw(ui, view, r, s, name, guild, mouse, now) {
                    self.buttons.push((Hit::Tab(i), tr));
                }
            }
            Window::Weekly { stats, error } => match stats {
                None => {
                    let t = error.as_deref().unwrap_or("Loading…");
                    ui.label(t, body[0], body[1], font(12.5), false, if error.is_some() { color::DANGER } else { color::TEXT_MUTED });
                }
                Some(w) => draw_week(ui, w, body, s),
            },
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

/// The Herald's counts (`DbWeeklyStats`, in its order): the id, the card's icon, its label.
const WEEK_CARDS: [(&str, u32, &str); 9] = [
    ("dungeons", crate::hud::ui_icon::DUNGEON, "Dungeons cleared"),
    ("chainsCompleted", crate::hud::ui_icon::CROWN, "Oryx chains completed"),
    ("chainsFailed", crate::hud::ui_icon::CROWN_BROKEN, "Oryx chains failed"),
    ("deaths", crate::hud::ui_icon::GRAVE, "Heroes fallen"),
    ("kills", crate::hud::ui_icon::SWORDS, "Monsters slain"),
    ("events", crate::hud::ui_icon::BANNER, "Realm events defeated"),
    ("realmsClosed", crate::hud::ui_icon::TOWER, "Realms conquered"),
    ("potions", crate::hud::ui_icon::POTION, "Potions drunk"),
    ("forged", crate::hud::ui_icon::ANVIL, "Items forged"),
];

/// The server's week in `body`: when it starts afresh, a card for each count (its icon, the
/// number large, what it counts), then the monsters slain most, the first in a gold-edged card.
fn draw_week(ui: &mut Ui, w: &bsk_net::account::WeeklyStats, body: [f32; 4], s: f32) {
    const GOLD: u32 = 0xFFD24A;
    let font = |size: f32| (size * s).round().max(9.0);
    // the reset, by the hourglass
    let left = (w.reset - w.now).max(0);
    let when = if left >= 86_400 { format!("{}d {}h", left / 86_400, left % 86_400 / 3600) } else { format!("{}h {}m", left / 3600, left % 3600 / 60) };
    let line = format!("Everyone's deeds together, since Monday. A new week begins in {when}.");
    crate::hud::ui_icon(ui, crate::hud::ui_icon::HOURGLASS, body[0] + 9.0 * s, body[1] + 9.0 * s, 18.0 * s);
    ui.label(&line, body[0] + 24.0 * s, body[1] + 1.0 * s, font(12.0), false, color::TEXT_DIM);
    let top = body[1] + 28.0 * s;
    let gap = 8.0 * s;
    let cw = (body[2] - 2.0 * gap) / 3.0;
    let ch = 88.0 * s;
    for (i, (id, icon, label)) in WEEK_CARDS.iter().enumerate() {
        let cr = [body[0] + (i % 3) as f32 * (cw + gap), top + (i / 3) as f32 * (ch + gap), cw, ch];
        theme::frame(ui.renderer, cr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::SLOT_TOP, 1.0), (color::SLOT_BOTTOM, 1.0)));
        crate::hud::ui_icon(ui, *icon, cr[0] + 26.0 * s, cr[1] + 30.0 * s, 32.0 * s);
        let value = crate::hud::group_digits(w.get(id).clamp(0, i32::MAX as i64) as i32);
        let vs = font(22.0);
        ui.label(&value, cr[0] + 50.0 * s, cr[1] + 14.0 * s, vs, true, color::TEXT);
        let ls = font(11.0);
        ui.label(label, cr[0] + 12.0 * s, cr[1] + ch - ls * 1.22 - 10.0 * s, ls, true, color::TEXT_MUTED);
    }
    // the most slain: the first big, the next few beside it
    let y = top + 3.0 * (ch + gap) + 4.0 * s;
    let card = [body[0], y, body[2], 96.0 * s];
    theme::frame(ui.renderer, card, 8.0 * s, s.max(1.0), (GOLD, 0.9), ((color::SLOT_TOP, 1.0), (color::SLOT_BOTTOM, 1.0)));
    crate::hud::ui_icon(ui, crate::hud::ui_icon::SKULL, card[0] + 18.0 * s, card[1] + 18.0 * s, 20.0 * s);
    ui.label("Most slain", card[0] + 32.0 * s, card[1] + 10.0 * s, font(12.0), true, GOLD);
    let name = |t: u16| ui.data.objects.get(&t).map_or_else(|| "Unknown".to_owned(), |d| d.display_id.clone());
    let Some(&(first, kills)) = w.killed.first() else {
        ui.label("Nothing slain yet this week.", card[0] + 16.0 * s, card[1] + 44.0 * s, font(12.0), false, color::TEXT_MUTED);
        return;
    };
    crate::hud::object_icon(ui, first, card[0] + 44.0 * s, card[1] + 60.0 * s, 44.0 * s);
    let fname = name(first);
    ui.label(&fname, card[0] + 76.0 * s, card[1] + 40.0 * s, font(15.0), true, color::TEXT);
    let k = format!("{} slain", crate::hud::group_digits(kills.clamp(0, i32::MAX as i64) as i32));
    ui.label(&k, card[0] + 76.0 * s, card[1] + 62.0 * s, font(12.0), true, GOLD);
    // the runners-up, a row each at the right
    let rx = card[0] + card[2] * 0.55;
    for (i, &(t, n)) in w.killed.iter().skip(1).take(4).enumerate() {
        let ry = card[1] + 12.0 * s + i as f32 * 20.0 * s;
        crate::hud::object_icon(ui, t, rx + 8.0 * s, ry + 8.0 * s, 16.0 * s);
        let text = format!("{}  ·  {}", name(t), crate::hud::group_digits(n.clamp(0, i32::MAX as i64) as i32));
        ui.label(&text, rx + 22.0 * s, ry, font(11.0), false, color::TEXT_DIM);
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
pub(crate) fn fit(ui: &mut Ui, text: &str, size: f32, width: f32) -> String {
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
    fn guilds_show_the_registers_price() {
        assert_eq!(guild_price_text(None), "1,000 fame");
        assert_eq!(guild_price_text(Some(1000)), "1,000 fame");
        assert_eq!(guild_price_text(Some(0)), "free");
    }

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
        assert_eq!(NpcKind::of_class("Enchanter"), Some(NpcKind::Enchanter));
        assert_eq!(NpcKind::of_class("QuestMaster"), Some(NpcKind::QuestMaster));
        assert_eq!(NpcKind::of_class("Chronicler"), Some(NpcKind::Chronicler));
        assert_eq!(NpcKind::of_class("ChampionsBoard"), Some(NpcKind::ChampionsBoard));
        assert_eq!(NpcKind::of_class("ChampionStatue"), Some(NpcKind::ChampionStatue));
        assert_eq!(NpcKind::of_class("SkillTree"), None);
    }
}
