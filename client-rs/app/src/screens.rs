//! The screens outside the game (the AIR client's `TitleView`/`AccountScreen`,
//! `CharacterSelectionAndNewsScreen`, `NewCharacterScreen`): sign in or register, then pick,
//! make or delete a character and choose a server. They're drawn over the night sky (`sky.rs`) in the
//! shared theme, scaled to the window and laid out from its shape each frame. Also the small
//! menu Escape opens in the game.

use crate::hud::Ui;
use crate::redraw::Dye;
use crate::sky;
use crate::stars;
use crate::textedit::LineEdit;
use crate::theme::{self, color, Kind};
use bsk_data::{ObjectClass, ObjectDesc, TextureRef};
use bsk_net::account::{CharList, CharSummary, FameSummary, Legend, RecentDeath};
use bsk_render::{Quad, Tint};
use winit::keyboard::KeyCode;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Screen {
    /// The main menu: Play, sign in or register, options, the changelog, quit.
    Home,
    Login,
    Register,
    /// Waiting on the account server.
    Busy,
    Chars,
    NewChar,
    /// A class's page: its skins, then Create.
    Class(u16),
    Legends,
    /// The account's dead characters (`Graveyard`).
    Graveyard,
    /// Account settings: change the password.
    Account,
    /// How a dead character's fame was counted.
    Death,
    /// The world servers to play on (`ServersScreen`).
    Servers,
}

/// The text fields, by index.
pub const EMAIL: usize = 0;
pub const PASSWORD: usize = 1;
pub const NAME: usize = 2;
pub const CONFIRM: usize = 3;
pub const NEW_PASSWORD: usize = 4;
pub const NEW_CONFIRM: usize = 5;

/// The legends' time spans: (id for the server, label). The last, "recent", is the server's
/// latest deaths (`/fame/recent`) rather than a time span of `/fame/list`.
pub const TIMESPANS: [(&str, &str); 4] = [("week", "This week"), ("month", "This month"), ("all", "All time"), ("recent", "Recent Deaths")];
/// The Recent Deaths tab's place in `TIMESPANS`.
pub const RECENT: usize = 3;

/// What a confirmation dialog is asking about.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Confirm {
    Delete(i32),
    BuySlot,
    BuySkin(u16),
}

/// What can be clicked.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Target {
    Field(usize),
    SignIn,
    ToRegister,
    Register,
    ToLogin,
    Remember,
    Char(i32),
    Delete(i32),
    ConfirmYes,
    ConfirmNo,
    NewChar,
    Class(u16),
    Skin(u16),
    Create,
    BuySlot,
    ToLegends,
    ToGraveyard,
    /// A grave: that dead character's fame summary.
    Grave(i32),
    ToAccount,
    Timespan(usize),
    /// The legends' scrollbar: a click jumps there, a drag scrolls.
    LegendsBar,
    /// A row of Recent Deaths (account, character): that death's fame summary.
    Fallen(i32, i32),
    SavePassword,
    Back,
    ServerPrev,
    ServerNext,
    ToServers,
    /// A row of the Servers screen: play on that server.
    Server(usize),
    Logout,
    /// The Changelog window.
    Changelog,
    /// Home's buttons.
    Play,
    ToHome,
    ToOptions,
    Quit,
}

/// What the player asked for.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Action {
    SignIn,
    Register,
    Play(i32),
    /// Make a character of this class, with this skin (0 for none).
    Create(u16, u16),
    Delete(i32),
    Logout,
    BuySlot,
    BuySkin(u16),
    ChangePassword,
    /// Ask for the legends of this time span.
    Legends(&'static str),
    /// Show how a dead character's fame was counted.
    ViewDeath(i32),
    /// The same for anyone's character (account, character), from Recent Deaths.
    ViewDeathOf(i32, i32),
    /// Open the Changelog window.
    Changelog,
    /// Open the Options window.
    Options,
    /// Play without an account.
    PlayAsGuest,
    /// Close the game.
    Quit,
}

pub struct Front {
    pub screen: Screen,
    /// Email, password, name, confirm password, new password, confirm new password.
    pub fields: [LineEdit; 6],
    focus: usize,
    pub remember: bool,
    /// A line under the form or list: (text, is it an error).
    pub message: Option<(String, bool)>,
    /// What the Busy screen says.
    pub busy: String,
    pub list: Option<CharList>,
    /// Which of the list's servers to play on.
    pub server: usize,
    /// What a confirmation dialog is open for.
    confirm: Option<Confirm>,
    /// The skin picked on a class's page (0: the class's own look).
    pub skin: u16,
    /// The legends shown, and which time span.
    pub legends: Option<Vec<Legend>>,
    pub timespan: usize,
    /// The server's latest deaths, newest first (the Recent Deaths tab).
    pub recent: Option<Vec<RecentDeath>>,
    /// The legends' first row shown, when they don't all fit.
    legends_row: usize,
    /// The legends' scrollbar last frame: its track, the rows and how many fit.
    legends_bar: Option<([f32; 4], usize, usize)>,
    /// The scrollbar's thumb is being dragged, held this far below its top.
    legends_grab: Option<f32>,
    /// The last death's fame summary.
    pub death: Option<FameSummary>,
    /// The summary was opened from the graveyard (Continue goes back there).
    pub death_from_graveyard: bool,
    /// The summary was opened from Recent Deaths (Continue goes back to the legends).
    pub death_from_legends: bool,
    /// Clickable areas from the last frame.
    targets: Vec<(Target, [f32; 4])>,
    /// Where each field's characters were drawn last frame: (field, first character shown,
    /// the x of each edge between the characters shown), to put the cursor where it's clicked.
    field_edges: Vec<(usize, usize, Vec<f32>)>,
    /// Where the account server is, shown small on the login screen.
    pub endpoint_label: String,
    /// A button was clicked since this was last cleared (for its sound).
    pub clicked: bool,
    /// The version (and any update in progress), bottom right; a click opens the changelog.
    pub version_line: String,
    /// The character list's first row shown, when it's longer than the window.
    chars_row: usize,
    /// A remembered login is being checked in the background (Home says so).
    pub signing_in: bool,
    /// Signed in as this device's guest: Register saves their progress.
    pub guest: bool,
    /// The night sky behind it all.
    sky: crate::sky::Sky,
}

impl Default for Front {
    fn default() -> Self {
        Front {
            screen: Screen::Home,
            fields: std::array::from_fn(|_| LineEdit::new("", 128)),
            focus: EMAIL,
            remember: true,
            message: None,
            busy: String::new(),
            list: None,
            server: 0,
            confirm: None,
            skin: 0,
            legends: None,
            timespan: 0,
            recent: None,
            legends_row: 0,
            legends_bar: None,
            legends_grab: None,
            death: None,
            death_from_graveyard: false,
            death_from_legends: false,
            clicked: false,
            targets: Vec::new(),
            field_edges: Vec::new(),
            endpoint_label: String::new(),
            version_line: String::new(),
            chars_row: 0,
            signing_in: false,
            guest: false,
            sky: Default::default(),
        }
    }
}

/// The screens' scale: their design is about 1000×720.
pub fn scale(w: f32, h: f32) -> f32 {
    (h / 720.0).min(w / 1000.0).clamp(0.5, 3.0)
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// The classes a new character can be (`ObjectLibrary.playerChars_`), in type order.
pub fn player_classes(data: &bsk_data::GameData) -> Vec<&ObjectDesc> {
    let mut classes: Vec<_> = data.objects.values().filter(|d| d.player && d.class == ObjectClass::Player).collect();
    classes.sort_by_key(|d| d.object_type);
    classes
}

/// A skin a class can wear, as the new-character page shows it.
pub struct SkinInfo<'a> {
    pub desc: &'a ObjectDesc,
    pub owned: bool,
    /// The class level it needs, when the account hasn't reached it.
    pub needs_level: Option<i32>,
    /// Only given out (`<UnlockSpecial>`), never sold.
    pub special: bool,
    pub cost: i32,
}

/// The skins of `class` that can be chosen (not `<NoSkinSelect>`), in type order.
pub fn class_skins<'a>(data: &'a bsk_data::GameData, class: u16, list: &CharList) -> Vec<SkinInfo<'a>> {
    let best = list.class_stats.get(&class).map_or(0, |s| s.0);
    let mut skins: Vec<SkinInfo> = data
        .objects
        .values()
        .filter(|d| d.xml.has("Skin") && !d.xml.has("NoSkinSelect"))
        .filter(|d| bsk_data::xml::as3_int(d.xml.text_of("PlayerClassType").unwrap_or("0")) as u16 == class)
        .map(|d| {
            let level = d.xml.int("UnlockLevel", 0);
            SkinInfo {
                desc: d,
                owned: list.owned_skins.contains(&d.object_type),
                needs_level: (level > best).then_some(level),
                special: d.xml.has("UnlockSpecial"),
                // SkinDesc: 300 gold unless the skin says.
                cost: d.xml.int("Cost", 300),
            }
        })
        .collect();
    skins.sort_by_key(|k| k.desc.object_type);
    skins
}

/// A tier colour for an item's slot edge: white tiers, gold for the top ones, purple for
/// untiered, orange for sets, red for Oryx 5's Ascended gear.
pub fn tier_edge(data: &bsk_data::GameData, item: i32) -> Option<u32> {
    let d = data.objects.get(&(item as u16))?;
    let xml = &d.xml;
    if xml.has("Ascended") {
        return Some(0xE5383B);
    }
    if ["Admin", "Ethereal", "Legendary"].iter().any(|t| xml.has(t)) {
        return Some(0xFFC800);
    }
    if xml.has("Consumable") || xml.has("Treasure") {
        return None;
    }
    if let Some(t) = xml.text_of("Tier") {
        let tier: i32 = t.trim().parse().unwrap_or(0);
        return Some(match tier {
            0..=5 => 0x8A93A6,
            6..=9 => 0xC9D2E3,
            10..=11 => 0x7FB2FF,
            _ => 0xFFC800,
        });
    }
    if xml.attr("setType").is_some() {
        return Some(0xFF9900);
    }
    xml.has("SlotType").then_some(0xB070FF)
}

impl Front {
    fn field_is_secret(i: usize) -> bool {
        matches!(i, PASSWORD | CONFIRM | NEW_PASSWORD | NEW_CONFIRM)
    }

    fn fields_on_screen(&self) -> &'static [usize] {
        match self.screen {
            Screen::Login => &[EMAIL, PASSWORD],
            Screen::Register => &[NAME, EMAIL, PASSWORD, CONFIRM],
            Screen::Account => &[PASSWORD, NEW_PASSWORD, NEW_CONFIRM],
            _ => &[],
        }
    }

    /// Nothing under way that switching to a new version would lose: the character list with no
    /// dialog open, or the sign-in form with nothing typed.
    #[cfg_attr(target_arch = "wasm32", allow(dead_code))] // updates are desktop-only
    pub fn at_rest(&self) -> bool {
        self.confirm.is_none()
            && match self.screen {
                Screen::Chars | Screen::Home => true,
                Screen::Login => self.fields[EMAIL].text.is_empty() && self.fields[PASSWORD].text.is_empty(),
                _ => false,
            }
    }

    /// Show a screen, focusing its first field.
    /// The mouse wheel, `notches` up (positive) or down: scrolls the character list or the
    /// legends a row a notch.
    pub fn wheel(&mut self, notches: i32) {
        if self.screen == Screen::Chars && self.confirm.is_none() {
            self.chars_row = (self.chars_row as i32 - notches).max(0) as usize;
        }
        if self.screen == Screen::Legends {
            self.legends_row = (self.legends_row as i32 - notches).max(0) as usize;
        }
    }

    /// The left button let go at `pos`: ends a scrollbar drag there.
    pub fn release(&mut self, pos: (f64, f64)) {
        self.drag(pos);
        self.legends_grab = None;
    }

    /// The mouse is at `pos`: scrolls the legends while their scrollbar is held.
    pub fn drag(&mut self, pos: (f64, f64)) {
        let (Some(grab), Some((track, rows, visible))) = (self.legends_grab, self.legends_bar) else { return };
        if self.screen != Screen::Legends || visible >= rows {
            self.legends_grab = None;
            return;
        }
        let thumb_h = track[3] * visible as f32 / rows as f32;
        let k = ((pos.1 as f32 - grab - track[1]) / (track[3] - thumb_h).max(1.0)).clamp(0.0, 1.0);
        self.legends_row = (k * (rows - visible) as f32).round() as usize;
    }

    pub fn show(&mut self, screen: Screen) {
        self.screen = screen;
        self.confirm = None;
        if screen == Screen::Account {
            for f in [PASSWORD, NEW_PASSWORD, NEW_CONFIRM] {
                self.set_field(f, String::new());
            }
        }
        if let Some(&first) = self.fields_on_screen().first() {
            // Signing in with an email already typed: start at the password.
            self.focus = if screen == Screen::Login && !self.fields[EMAIL].text.is_empty() { PASSWORD } else { first };
        }
    }

    /// Put `text` in a field, with the cursor at its end.
    pub fn set_field(&mut self, i: usize, text: String) {
        self.fields[i] = LineEdit::new(text, 128);
    }

    /// Text the browser pasted: into the field being typed in.
    #[cfg_attr(not(target_arch = "wasm32"), allow(dead_code))]
    pub fn paste(&mut self, text: &str) {
        if self.fields_on_screen().contains(&self.focus) {
            self.fields[self.focus].insert(text);
        }
    }

    /// What a copy or cut key would take from the field being typed in (never a password).
    #[cfg_attr(not(target_arch = "wasm32"), allow(dead_code))]
    pub fn copyable(&self) -> Option<&str> {
        if self.confirm.is_some() || !self.fields_on_screen().contains(&self.focus) || Self::field_is_secret(self.focus) {
            return None;
        }
        self.fields[self.focus].selected_text()
    }

    /// A key pressed (with the text it types, if any, and whether Ctrl and Shift are held).
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, ctrl: bool, shift: bool) -> Option<Action> {
        if self.confirm.is_some() {
            match code {
                KeyCode::Escape => self.confirm = None,
                KeyCode::Enter | KeyCode::NumpadEnter => return self.confirmed(),
                _ => {}
            }
            return None;
        }
        let fields = self.fields_on_screen();
        match code {
            // Shift+Tab goes back a field.
            KeyCode::Tab if !fields.is_empty() => {
                let i = fields.iter().position(|&f| f == self.focus).unwrap_or(0);
                let n = fields.len();
                self.focus = fields[if shift { (i + n - 1) % n } else { (i + 1) % n }];
            }
            KeyCode::Enter | KeyCode::NumpadEnter => match self.screen {
                Screen::Login => return Some(Action::SignIn),
                Screen::Register => return Some(Action::Register),
                Screen::Account => return Some(Action::ChangePassword),
                Screen::Death if self.death_from_graveyard => self.show(Screen::Graveyard),
                Screen::Death if self.death_from_legends => self.show(Screen::Legends),
                Screen::Death => self.show(Screen::Chars),
                _ => {}
            },
            KeyCode::Escape => match self.screen {
                Screen::Login | Screen::Register | Screen::Chars => {
                    self.message = None;
                    self.show(Screen::Home);
                }
                Screen::Death if self.death_from_graveyard => self.show(Screen::Graveyard),
                Screen::Death if self.death_from_legends => self.show(Screen::Legends),
                Screen::NewChar | Screen::Legends | Screen::Graveyard | Screen::Account | Screen::Death | Screen::Servers => self.show(Screen::Chars),
                Screen::Class(_) => self.show(Screen::NewChar),
                _ => {}
            },
            // A password can't be copied or cut out of its field, as in any desktop text box.
            KeyCode::KeyC | KeyCode::KeyX | KeyCode::Insert if ctrl && Self::field_is_secret(self.focus) => {}
            KeyCode::Delete if shift && !ctrl && Self::field_is_secret(self.focus) => {}
            _ if fields.contains(&self.focus) => {
                self.fields[self.focus].key(code, text, ctrl, shift);
            }
            _ => {}
        }
        None
    }

    /// A left click.
    pub fn click(&mut self, pos: (f64, f64)) -> Option<Action> {
        let target = self.targets.iter().rev().find(|(_, r)| inside(*r, pos)).map(|(t, _)| *t)?;
        self.clicked |= !matches!(target, Target::Field(_));
        let servers = self.list.as_ref().map_or(0, |l| l.servers.len());
        match target {
            Target::Field(i) => {
                self.focus = i;
                // The cursor goes to the edge between characters nearest the click.
                if let Some((_, first, edges)) = self.field_edges.iter().find(|(f, _, _)| *f == i) {
                    let x = pos.0 as f32;
                    let k = edges.iter().enumerate().min_by(|a, b| (a.1 - x).abs().total_cmp(&(b.1 - x).abs())).map_or(0, |(k, _)| k);
                    let field = &mut self.fields[i];
                    field.cursor = first + k;
                    field.anchor = None;
                }
            }
            Target::SignIn => return Some(Action::SignIn),
            Target::Register => return Some(Action::Register),
            Target::ToRegister => {
                self.message = None;
                self.show(Screen::Register);
            }
            Target::ToLogin => {
                self.message = None;
                self.show(Screen::Login);
            }
            Target::Back => match self.screen {
                Screen::Class(_) => self.show(Screen::NewChar),
                Screen::Death if self.death_from_graveyard => self.show(Screen::Graveyard),
                Screen::Death if self.death_from_legends => self.show(Screen::Legends),
                _ => self.show(Screen::Chars),
            },
            Target::ToGraveyard => self.show(Screen::Graveyard),
            Target::Grave(id) => return Some(Action::ViewDeath(id)),
            Target::Fallen(account, id) => return Some(Action::ViewDeathOf(account, id)),
            Target::Remember => self.remember = !self.remember,
            Target::Char(id) => return Some(Action::Play(id)),
            Target::Delete(id) => self.confirm = Some(Confirm::Delete(id)),
            Target::ConfirmYes => return self.confirmed(),
            Target::ConfirmNo => self.confirm = None,
            Target::NewChar => self.show(Screen::NewChar),
            Target::Class(t) => {
                self.skin = 0;
                self.show(Screen::Class(t));
            }
            Target::Skin(t) => {
                let owned = self.list.as_ref().is_some_and(|l| l.owned_skins.contains(&t));
                if t == 0 || owned {
                    self.skin = t;
                } else {
                    self.confirm = Some(Confirm::BuySkin(t));
                }
            }
            Target::Create => {
                if let Screen::Class(class) = self.screen {
                    return Some(Action::Create(class, self.skin));
                }
            }
            Target::BuySlot => self.confirm = Some(Confirm::BuySlot),
            Target::ToLegends => {
                self.legends_row = 0;
                self.show(Screen::Legends);
                return Some(Action::Legends(TIMESPANS[self.timespan].0));
            }
            Target::ToAccount => {
                self.message = None;
                self.show(Screen::Account);
            }
            Target::Timespan(i) => {
                self.timespan = i;
                self.legends = None;
                self.recent = None;
                self.legends_row = 0;
                return Some(Action::Legends(TIMESPANS[i].0));
            }
            Target::LegendsBar => {
                self.clicked = false;
                if let Some((track, rows, visible)) = self.legends_bar.filter(|b| b.2 < b.1) {
                    let thumb_h = track[3] * visible as f32 / rows as f32;
                    let thumb_y = track[1] + (track[3] - thumb_h) * self.legends_row as f32 / (rows - visible) as f32;
                    // Taken by the thumb, it's held where it was taken; a click on the track
                    // brings the thumb's middle there.
                    let y = pos.1 as f32;
                    self.legends_grab = Some(if (thumb_y..thumb_y + thumb_h).contains(&y) { y - thumb_y } else { thumb_h / 2.0 });
                    self.drag(pos);
                }
            }
            Target::SavePassword => return Some(Action::ChangePassword),
            Target::ServerPrev if servers > 0 => self.server = (self.server + servers - 1) % servers,
            Target::ServerNext if servers > 0 => self.server = (self.server + 1) % servers,
            Target::ServerPrev | Target::ServerNext => {}
            Target::ToServers => self.show(Screen::Servers),
            Target::Server(i) => {
                self.server = i.min(servers.saturating_sub(1));
                self.show(Screen::Chars);
            }
            Target::Logout => return Some(Action::Logout),
            Target::Changelog => return Some(Action::Changelog),
            // Signed in (or a guest): the characters; else play as a guest.
            Target::Play => {
                self.message = None;
                if self.list.is_some() {
                    self.show(Screen::Chars);
                } else {
                    return Some(Action::PlayAsGuest);
                }
            }
            Target::ToHome => {
                self.message = None;
                self.show(Screen::Home);
            }
            Target::ToOptions => return Some(Action::Options),
            Target::Quit => return Some(Action::Quit),
        }
        None
    }

    /// The confirmation's answer: yes.
    fn confirmed(&mut self) -> Option<Action> {
        match self.confirm.take()? {
            Confirm::Delete(id) => Some(Action::Delete(id)),
            Confirm::BuySlot => Some(Action::BuySlot),
            Confirm::BuySkin(t) => Some(Action::BuySkin(t)),
        }
    }

    /// Draw the current screen.
    pub fn draw(&mut self, ui: &mut Ui, w: f32, h: f32, mouse: (f64, f64), now: i32) {
        self.targets.clear();
        self.field_edges.clear();
        let s = scale(w, h);
        self.sky.draw(ui, w, h, s, now);
        draw_shade(ui, w, h, self.screen);
        match self.screen {
            Screen::Login | Screen::Register => draw_logo(ui, w, h, s, Self::card_rect(w, h, 380.0 * s, 420.0 * s)[0]),
            Screen::Home => {}
            Screen::Busy => draw_logo(ui, w, h, s, w),
            _ => {}
        }
        match self.screen {
            Screen::Home => self.draw_home(ui, w, h, s, mouse, now),
            Screen::Login => self.draw_login(ui, w, h, s, mouse, now),
            Screen::Register => self.draw_register(ui, w, h, s, mouse, now),
            Screen::Busy => {
                let (cx, cy) = (w / 2.0, h * 0.74);
                spinner(ui, cx, cy, 18.0 * s, now, s);
                let size = (18.0 * s).round();
                let tw = ui.measure(&self.busy, size, true);
                ui.label(&self.busy, cx - tw / 2.0, cy + 30.0 * s, size, true, color::TEXT);
            }
            Screen::Chars => self.draw_chars(ui, w, h, s, mouse),
            Screen::NewChar => self.draw_new_char(ui, w, h, s, mouse),
            Screen::Class(class) => self.draw_class(ui, w, h, s, class, mouse),
            Screen::Legends => self.draw_legends(ui, w, h, s, mouse),
            Screen::Graveyard => self.draw_graveyard(ui, w, h, s, mouse),
            Screen::Account => self.draw_account(ui, w, h, s, mouse, now),
            Screen::Death => self.draw_death(ui, w, h, s, mouse),
            Screen::Servers => self.draw_servers(ui, w, h, s, mouse),
        }
        // The version, bottom right: opens the changelog.
        if !self.version_line.is_empty() && self.screen != Screen::Busy {
            let size = (12.0 * s).round().max(9.0);
            let line = self.version_line.clone();
            let lw = ui.measure(&line, size, true);
            let r = [w - lw - 14.0 * s, h - size * 1.7, lw, size * 1.3];
            ui.label(&line, r[0], r[1], size, true, if inside(r, mouse) { color::ACCENT_HI } else { color::TEXT_MUTED });
            self.targets.push((Target::Changelog, r));
        }
        if self.confirm.is_some() {
            self.draw_confirm(ui, w, h, s, mouse);
        }
    }

    fn button(&mut self, ui: &mut Ui, target: Target, r: [f32; 4], label: &str, kind: Kind, s: f32, mouse: (f64, f64)) {
        ui.button(r, label, kind, inside(r, mouse), s);
        self.targets.push((target, r));
    }

    /// A text link: `lead` in grey, then `label` in gold.
    fn link(&mut self, ui: &mut Ui, target: Target, cx: f32, y: f32, lead: &str, label: &str, size: f32, mouse: (f64, f64)) {
        let lw = ui.measure(lead, size, false);
        let tw = ui.measure(label, size, true);
        let x = cx - (lw + tw) / 2.0;
        let r = [x + lw, y, tw, size * 1.3];
        ui.label(lead, x, y, size, false, color::TEXT_DIM);
        ui.label(label, x + lw, y, size, true, if inside(r, mouse) { color::ACCENT_HI } else { color::ACCENT });
        self.targets.push((target, r));
    }

    #[allow(clippy::too_many_arguments)]
    fn field(&mut self, ui: &mut Ui, i: usize, label: &str, placeholder: &str, x: f32, y: f32, width: f32, s: f32, now: i32) -> f32 {
        let size = (13.0 * s).round().max(9.0);
        ui.label(label, x, y, size, true, color::TEXT_DIM);
        let r = [x, y + 20.0 * s, width, 38.0 * s];
        let focused = self.focus == i;
        ui.field_box(r, focused, s);
        let field = &self.fields[i];
        let fsize = (16.0 * s).round().max(10.0);
        let ty = r[1] + (r[3] - fsize * 1.22) / 2.0;
        let (left, room) = (r[0] + 12.0 * s, width - 24.0 * s);
        if field.text.is_empty() && !focused {
            ui.label(placeholder, left, ty, fsize, false, color::TEXT_MUTED);
        } else {
            let chars: Vec<char> = if Self::field_is_secret(i) { vec!['•'; field.text.chars().count()] } else { field.text.chars().collect() };
            let width_of = |ui: &mut Ui, a: usize, b: usize| ui.measure(&chars[a..b].iter().collect::<String>(), fsize, false);
            // Scroll so the cursor shows: the first character drawn is the furthest left that
            // keeps it in the box, then as many as fit after it.
            let cursor = field.cursor.min(chars.len());
            let mut first = 0;
            while first < cursor && width_of(ui, first, cursor) > room {
                first += 1;
            }
            let mut last = cursor;
            while last < chars.len() && width_of(ui, first, last + 1) <= room {
                last += 1;
            }
            let edges: Vec<f32> = (first..=last).map(|k| left + width_of(ui, first, k)).collect();
            if let (true, Some((a, b))) = (focused, field.selection()) {
                let (a, b) = (a.clamp(first, last), b.clamp(first, last));
                if b > a {
                    theme::fill(ui.renderer, [edges[a - first], ty - 1.0 * s, edges[b - first] - edges[a - first], fsize * 1.25], 2.0 * s, color::ACCENT, 0.35);
                }
            }
            let shown: String = chars[first..last].iter().collect();
            ui.label(&shown, left, ty, fsize, false, color::TEXT);
            if focused && (now / 530) % 2 == 0 {
                let x = edges[cursor - first];
                theme::fill(ui.renderer, [x, ty, (1.5 * s).max(1.0), fsize * 1.2], 0.0, color::TEXT, 1.0);
            }
            self.field_edges.push((i, first, edges));
        }
        self.targets.push((Target::Field(i), r));
        y + 72.0 * s
    }

    fn message_line(&self, ui: &mut Ui, cx: f32, y: f32, max_w: f32, s: f32) {
        if let Some((text, error)) = &self.message {
            let size = (14.0 * s).round().max(9.0);
            // Wrapped to the width, so a long message stays on the screen.
            let lines = wrap(ui, text, size, max_w - 24.0 * s);
            let tw = lines.iter().map(|l| ui.measure(l, size, true)).fold(0.0, f32::max);
            let line_h = size * 1.3;
            let c = if *error { color::DANGER } else { color::GOOD };
            let r = [cx - tw / 2.0 - 12.0 * s, y - 4.0 * s, tw + 24.0 * s, line_h * lines.len() as f32 + 8.0 * s];
            // Dark under the tint, so it reads over the bright parts of the sky.
            theme::fill(ui.renderer, r, 6.0 * s, color::PANEL_BOTTOM, 0.92);
            theme::fill(ui.renderer, r, 6.0 * s, c, 0.16);
            for (i, line) in lines.iter().enumerate() {
                let lw = ui.measure(line, size, true);
                ui.label(line, cx - lw / 2.0, y + i as f32 * line_h, size, true, theme::shade(c, 0.35));
            }
        }
    }

    /// Where the sign-in and register cards go: right of the art's middle on wide windows,
    /// below the logo on tall ones.
    fn card_rect(w: f32, h: f32, bw: f32, bh: f32) -> [f32; 4] {
        if w >= 1.3 * h {
            let x = (w * 0.79 - bw / 2.0).min(w - bw - 24.0);
            [x, ((h - bh) / 2.0).max(16.0), bw, bh]
        } else {
            [(w - bw) / 2.0, (h - bh - 28.0).max(h * 0.45).min(h - bh - 8.0), bw, bh]
        }
    }

    /// The main menu: the game's name, who's signed in, then a column of buttons.
    fn draw_home(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64), now: i32) {
        let name = crate::TITLE;
        let cell = (w * 0.7 / sky::logo_width(name, 1.0)).min(11.0 * s).floor().max(2.0);
        let top = (h * 0.12).max(16.0 * s);
        let lh = sky::logo(ui, name, w / 2.0, top, cell);
        let size = (16.0 * s).round().max(10.0);
        let tagline = "A Realm of the Mad God private server";
        let tw = ui.measure(tagline, size, false);
        ui.label(tagline, (w - tw) / 2.0, top + lh + 8.0 * s, size, false, color::TEXT);
        let bw = 320.0 * s;
        let (bh, gap) = (46.0 * s, 12.0 * s);
        let x = (w - bw) / 2.0;
        let mut y = top + lh + 56.0 * s;
        // Who's signed in, if anyone.
        let who = match (&self.list, self.signing_in) {
            (Some(list), _) if self.guest => Some(format!("Playing as a guest ({})", list.name)),
            (Some(list), _) => Some(format!("Signed in as {}", list.name)),
            (None, true) => Some("Signing in\u{2026}".to_owned()),
            _ => None,
        };
        let ws = (14.0 * s).round().max(9.0);
        if let Some(who) = who {
            let ww = ui.measure(&who, ws, false);
            ui.label(&who, (w - ww) / 2.0, y, ws, false, color::TEXT_DIM);
        }
        y += ws * 1.6 + 6.0 * s;
        let half = (bw - gap) / 2.0;
        self.button(ui, Target::Play, [x, y, bw, bh + 6.0 * s], "Play", Kind::Primary, s, mouse);
        y += bh + 6.0 * s + gap;
        if self.list.is_some() && self.guest {
            self.button(ui, Target::ToRegister, [x, y, half, bh], "Save progress", Kind::Secondary, s, mouse);
            self.button(ui, Target::ToLogin, [x + half + gap, y, half, bh], "Sign in", Kind::Secondary, s, mouse);
        } else if self.list.is_some() {
            self.button(ui, Target::Logout, [x, y, bw, bh], "Log out", Kind::Secondary, s, mouse);
        } else {
            self.button(ui, Target::ToLogin, [x, y, half, bh], "Sign in", Kind::Secondary, s, mouse);
            self.button(ui, Target::ToRegister, [x + half + gap, y, half, bh], "Register", Kind::Secondary, s, mouse);
        }
        y += bh + gap;
        self.button(ui, Target::ToOptions, [x, y, half, bh], "Options", Kind::Secondary, s, mouse);
        self.button(ui, Target::Changelog, [x + half + gap, y, half, bh], "Changelog", Kind::Secondary, s, mouse);
        y += bh + gap;
        // A browser tab is closed by the browser.
        if cfg!(not(target_arch = "wasm32")) {
            self.button(ui, Target::Quit, [x, y, bw, bh], "Quit", Kind::Secondary, s, mouse);
            y += bh + gap;
        }
        if self.list.is_none() && self.message.is_none() {
            let hint = "No account needed: Play starts you as a guest. Register any time to keep your progress.";
            let hs = (13.0 * s).round().max(9.0);
            let hw = ui.measure(hint, hs, false).min(w - 32.0);
            ui.label(hint, (w - hw) / 2.0, y + 4.0 * s, hs, false, color::TEXT_DIM);
        }
        self.message_line(ui, w / 2.0, y + 8.0 * s, (w - 32.0).min(560.0 * s), s);
        let _ = now;
        let small = (12.0 * s).round().max(9.0);
        ui.label(&self.endpoint_label, 12.0 * s, h - small * 1.7, small, false, color::TEXT_MUTED);
    }

    /// "Back" to the main menu, top left of the sign-in and register screens.
    fn back_home(&mut self, ui: &mut Ui, s: f32, mouse: (f64, f64)) {
        self.button(ui, Target::ToHome, [16.0 * s, 16.0 * s, 110.0 * s, 38.0 * s], "\u{2039} Back", Kind::Secondary, s, mouse);
    }

    fn draw_login(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64), now: i32) {
        self.back_home(ui, s, mouse);
        let r = Self::card_rect(w, h, 380.0 * s, 420.0 * s);
        let [x, y, bw, bh] = r;
        theme::panel(ui.renderer, r, s);
        let pad = 28.0 * s;
        ui.label("Welcome back", x + pad, y + 24.0 * s, (26.0 * s).round(), true, color::TEXT);
        ui.label("Sign in to continue your adventure", x + pad, y + 60.0 * s, (14.0 * s).round(), false, color::TEXT_DIM);
        let mut fy = y + 96.0 * s;
        fy = self.field(ui, EMAIL, "Email", "you@example.com", x + pad, fy, bw - 2.0 * pad, s, now);
        fy = self.field(ui, PASSWORD, "Password", "Your password", x + pad, fy, bw - 2.0 * pad, s, now);
        // Remember me, as a switch.
        let size = (14.0 * s).round().max(9.0);
        let sw = [x + pad, fy + 2.0 * s, 36.0 * s, 20.0 * s];
        theme::fill(ui.renderer, sw, sw[3] / 2.0, if self.remember { color::ACCENT } else { color::SECONDARY }, 1.0);
        let knob_x = if self.remember { sw[0] + sw[2] - sw[3] } else { sw[0] };
        theme::fill(ui.renderer, [knob_x + 2.0 * s, sw[1] + 2.0 * s, sw[3] - 4.0 * s, sw[3] - 4.0 * s], sw[3], 0xFFFFFF, 1.0);
        ui.label("Remember me", sw[0] + sw[2] + 10.0 * s, sw[1] + 1.0 * s, size, false, color::TEXT_DIM);
        self.targets.push((Target::Remember, [sw[0], sw[1], 160.0 * s, sw[3]]));
        self.button(ui, Target::SignIn, [x + pad, fy + 40.0 * s, bw - 2.0 * pad, 44.0 * s], "Sign in", Kind::Primary, s, mouse);
        theme::fill(ui.renderer, [x + pad, y + bh - 54.0 * s, bw - 2.0 * pad, s.max(1.0)], 0.0, color::BORDER, 1.0);
        self.link(ui, Target::ToRegister, x + bw / 2.0, y + bh - 40.0 * s, "New here?  ", "Create an account", size, mouse);
        self.message_line(ui, x + bw / 2.0, y + bh + 16.0 * s, bw, s);
        let small = (12.0 * s).round().max(9.0);
        ui.label(&self.endpoint_label, 12.0 * s, h - small * 1.7, small, false, color::TEXT_MUTED);
    }

    fn draw_register(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64), now: i32) {
        self.back_home(ui, s, mouse);
        let r = Self::card_rect(w, h, 380.0 * s, 520.0 * s);
        let [x, y, bw, bh] = r;
        theme::panel(ui.renderer, r, s);
        let pad = 28.0 * s;
        let title = if self.guest && self.list.is_some() { "Save your progress" } else { "Create your account" };
        ui.label(title, x + pad, y + 24.0 * s, (24.0 * s).round(), true, color::TEXT);
        let mut fy = y + 66.0 * s;
        for (i, label, hint) in [
            (NAME, "Name", "Up to 10 letters"),
            (EMAIL, "Email", "you@example.com"),
            (PASSWORD, "Password", "Choose a password"),
            (CONFIRM, "Confirm password", "Type it again"),
        ] {
            fy = self.field(ui, i, label, hint, x + pad, fy, bw - 2.0 * pad, s, now);
        }
        self.button(ui, Target::Register, [x + pad, fy + 4.0 * s, bw - 2.0 * pad, 44.0 * s], "Create account", Kind::Primary, s, mouse);
        let size = (14.0 * s).round().max(9.0);
        self.link(ui, Target::ToLogin, x + bw / 2.0, y + bh - 36.0 * s, "Have an account?  ", "Sign in", size, mouse);
        self.message_line(ui, x + bw / 2.0, y + bh + 16.0 * s, bw, s);
    }

    fn draw_chars(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let Some(list) = self.list.clone() else { return };
        // The top bar: who and their rank, the buttons, then their fame and gold at the right.
        let bar = [0.0, 0.0, w, 68.0 * s];
        theme::rounded(ui.renderer, bar, 0.0, (color::PANEL_TOP, 0.92), (color::PANEL_BOTTOM, 0.92));
        theme::fill(ui.renderer, [0.0, bar[3] - s.max(1.0), w, s.max(1.0)], 0.0, color::BORDER, 1.0);
        let pad = 24.0 * s;
        ui.label(&list.name, pad, 12.0 * s, (24.0 * s).round(), true, color::TEXT);
        let size = (14.0 * s).round();
        let x = pad;
        // The rank (`AccountScreen.rankText`): the class quests done, and its star.
        let classes = stars::class_count(ui.data);
        let rank = stars::account_stars(&list);
        ui.label("Rank", x, 42.0 * s, size, false, color::TEXT_DIM);
        let rx = x + ui.measure("Rank", size, false) + 6.0 * s;
        let v = rank.to_string();
        ui.label(&v, rx, 42.0 * s, size, true, color::TEXT);
        let vw = ui.measure(&v, size, true);
        stars::icon(ui.renderer, rx + vw + 11.0 * s, 42.0 * s + size * 0.55, s, stars::color(rank, classes));
        let rank_r = [x, 38.0 * s, rx + vw + 20.0 * s - x, size * 1.6];
        // Fame and gold (`CreditDisplay`), one above the other, right-aligned in the corner.
        let money = (16.0 * s).round();
        let mut money_w: f32 = 0.0;
        for (i, (label, value, c)) in [("Fame", list.fame, color::ACCENT), ("Gold", list.credits, 0xFFE27A)].into_iter().enumerate() {
            let v = value.to_string();
            let vw = ui.measure(&v, money, true);
            let lw = ui.measure(label, size, false);
            let y = 10.0 * s + i as f32 * 25.0 * s;
            ui.label(&v, w - pad - vw, y, money, true, c);
            ui.label(label, w - pad - vw - 6.0 * s - lw, y + (money - size) * 0.6, size, false, color::TEXT_DIM);
            money_w = money_w.max(vw + 6.0 * s + lw);
        }
        let bw = 104.0 * s;
        let right = w - pad - money_w - 22.0 * s;
        let buttons = [
            (Target::ToHome, "Main menu"),
            if self.guest { (Target::ToRegister, "Register") } else { (Target::ToAccount, "Account") },
            (Target::ToServers, "Servers"),
            (Target::ToLegends, "Legends"),
            (Target::ToGraveyard, "Graveyard"),
            (Target::Changelog, "Changelog"),
        ];
        for (i, (target, label)) in buttons.into_iter().enumerate() {
            let bx = right - (i + 1) as f32 * bw - i as f32 * 8.0 * s;
            self.button(ui, target, [bx, 16.0 * s, bw, 36.0 * s], label, Kind::Secondary, s, mouse);
        }

        // Characters, then a card for a new one while there's room, else one to buy a slot. Rows
        // that don't fit scroll, a row a wheel notch.
        let (cw, ch, gap) = (380.0 * s, 88.0 * s, 10.0 * s);
        let room = (list.chars.len() as i32) < list.max_num_chars;
        let count = list.chars.len() + 1;
        let cols = (((w - 2.0 * pad - 20.0 * s + gap) / (cw + gap)).floor() as usize).clamp(1, 4).min(count.max(1));
        let rows = count.div_ceil(cols);
        let total_w = cols as f32 * (cw + gap) - gap;
        let x0 = (w - total_w) / 2.0;
        let area_top = bar[3] + 60.0 * s;
        let area_h = (h - 120.0 * s - area_top).max(ch);
        let visible = (((area_h + gap) / (ch + gap)).floor() as usize).clamp(1, rows);
        self.chars_row = self.chars_row.min(rows - visible);
        let first = self.chars_row;
        let y0 = area_top.max(area_top + (area_h - visible as f32 * (ch + gap) + gap) / 2.0);
        let title = "Choose your character";
        let ts = (22.0 * s).round();
        let tw = ui.measure(title, ts, true);
        ui.label(title, (w - tw) / 2.0, y0 - 40.0 * s, ts, true, color::TEXT);
        let shown = |i: usize| (first..first + visible).contains(&(i / cols));
        let cell = |i: usize| [x0 + (i % cols) as f32 * (cw + gap), y0 + (i / cols - first) as f32 * (ch + gap), cw, ch];
        for (i, c) in list.chars.iter().enumerate().filter(|(i, _)| shown(*i)) {
            let best = list.class_stats.get(&c.object_type).map_or(0, |cs| cs.1);
            self.char_card(ui, c, best, cell(i), s, mouse);
        }
        // A scrollbar beside the cards when some are out of sight.
        if visible < rows {
            let track = [x0 + total_w + 10.0 * s, y0, 5.0 * s, visible as f32 * (ch + gap) - gap];
            theme::fill(ui.renderer, track, 2.5 * s, color::TRACK, 0.9);
            let thumb_h = track[3] * visible as f32 / rows as f32;
            let thumb_y = track[1] + (track[3] - thumb_h) * first as f32 / (rows - visible) as f32;
            theme::fill(ui.renderer, [track[0], thumb_y, track[2], thumb_h], 2.5 * s, color::ACCENT, 0.9);
        }
        let last = list.chars.len();
        if !room && shown(last) {
            let r = cell(last);
            let over = inside(r, mouse) && self.confirm.is_none();
            let edge = if over { color::ACCENT } else { color::BORDER };
            theme::frame(ui.renderer, r, 10.0 * s, s.max(1.0), (edge, 0.9), ((color::INSET_TOP, 0.6), (color::INSET_BOTTOM, 0.6)));
            let (price, fame) = (list.next_slot_price, list.next_slot_currency == 1);
            let label = "Buy another character slot";
            let size = (17.0 * s).round();
            let lw = ui.measure(label, size, true);
            ui.label(label, r[0] + (r[2] - lw) / 2.0, r[1] + r[3] / 2.0 - size * 1.25, size, true, if over { color::ACCENT_HI } else { color::TEXT });
            let cost = format!("{price} {}", if fame { "fame" } else { "gold" });
            let cs = (15.0 * s).round();
            let cw = ui.measure(&cost, cs, true);
            let afford = if fame { list.fame >= price } else { list.credits >= price };
            ui.label(&cost, r[0] + (r[2] - cw) / 2.0, r[1] + r[3] / 2.0 + 4.0 * s, cs, true, if afford { color::ACCENT } else { color::DANGER });
            self.targets.push((Target::BuySlot, r));
        }
        if room && shown(last) {
            let r = cell(last);
            let over = inside(r, mouse) && self.confirm.is_none();
            let edge = if over { color::ACCENT } else { color::BORDER };
            theme::frame(ui.renderer, r, 10.0 * s, s.max(1.0), (edge, 0.9), ((color::INSET_TOP, 0.6), (color::INSET_BOTTOM, 0.6)));
            let plus = (30.0 * s).round();
            let label = "New character";
            let size = (17.0 * s).round();
            let lw = ui.measure(label, size, true);
            let pw = ui.measure("+", plus, true);
            let total = pw + 12.0 * s + lw;
            let cx = r[0] + (r[2] - total) / 2.0;
            let c = if over { color::ACCENT_HI } else { color::TEXT_DIM };
            ui.label("+", cx, r[1] + (r[3] - plus * 1.22) / 2.0, plus, true, c);
            ui.label(label, cx + pw + 12.0 * s, r[1] + (r[3] - size * 1.22) / 2.0, size, true, c);
            self.targets.push((Target::NewChar, r));
        }
        let bottom = y0 + visible as f32 * (ch + gap);
        // The server: arrows to switch when there's a choice; the pill opens the Servers screen.
        if let Some(server) = list.servers.get(self.server.min(list.servers.len().saturating_sub(1))) {
            let size = (15.0 * s).round();
            let label = format!("Server: {}", server.name);
            let lw = ui.measure(&label, size, true);
            let pill_w = lw + if list.servers.len() > 1 { 90.0 * s } else { 36.0 * s };
            let pill = [(w - pill_w) / 2.0, bottom + 10.0 * s, pill_w, 34.0 * s];
            let over = inside(pill, mouse) && self.confirm.is_none();
            theme::frame(ui.renderer, pill, pill[3] / 2.0, s.max(1.0), (if over { color::ACCENT } else { color::BORDER }, 1.0), ((color::PANEL_TOP, 0.9), (color::PANEL_BOTTOM, 0.9)));
            ui.label(&label, pill[0] + (pill[2] - lw) / 2.0, pill[1] + (pill[3] - size * 1.22) / 2.0, size, true, color::TEXT);
            self.targets.push((Target::ToServers, pill));
            if list.servers.len() > 1 {
                for (t, arrow, ax) in [(Target::ServerPrev, "‹", pill[0] + 10.0 * s), (Target::ServerNext, "›", pill[0] + pill[2] - 26.0 * s)] {
                    let r = [ax - 6.0 * s, pill[1], 28.0 * s, pill[3]];
                    let big = (22.0 * s).round();
                    ui.label(arrow, ax, pill[1] + (pill[3] - big * 1.22) / 2.0, big, true, if inside(r, mouse) { color::ACCENT_HI } else { color::ACCENT });
                    self.targets.push((t, r));
                }
            }
        }
        self.message_line(ui, w / 2.0, bottom + 60.0 * s, w * 0.8, s);
        let hint = if visible < rows { "Click a character to play; scroll for more" } else { "Click a character to play" };
        let hs = (13.0 * s).round();
        let hw = ui.measure(hint, hs, false);
        ui.label(hint, (w - hw) / 2.0, h - hs * 2.2, hs, false, color::TEXT_MUTED);
        if inside(rank_r, mouse) && self.confirm.is_none() {
            rank_tooltip(ui, rank, classes, (rank_r[0], rank_r[1] + rank_r[3] + 6.0 * s), s);
        }
    }

    /// `CurrentCharacterRect`. `best` is the best fame reached with the character's class.
    fn char_card(&mut self, ui: &mut Ui, c: &CharSummary, best: i32, r: [f32; 4], s: f32, mouse: (f64, f64)) {
        let over = inside(r, mouse) && self.confirm.is_none();
        let r = if over { [r[0], r[1] - 2.0 * s, r[2], r[3]] } else { r };
        theme::shadow(ui.renderer, r, 10.0 * s, if over { 16.0 * s } else { 9.0 * s }, if over { 0.55 } else { 0.4 });
        let edge = if over { color::ACCENT } else { color::BORDER_HI };
        theme::frame(ui.renderer, r, 10.0 * s, s.max(1.0), (edge, 1.0), ((color::PANEL_TOP, 0.95), (color::PANEL_BOTTOM, 0.97)));
        self.targets.push((Target::Char(c.id), r));
        // The portrait in a dark well.
        let well = [r[0] + 10.0 * s, r[1] + 10.0 * s, r[3] - 20.0 * s, r[3] - 20.0 * s];
        theme::rounded(ui.renderer, well, 8.0 * s, (color::FIELD, 0.9), (color::FIELD_BOTTOM, 0.9));
        let class = ui.data.objects.get(&c.object_type);
        let skin = (c.skin != 0).then(|| ui.data.objects.get(&(c.skin as u16))).flatten();
        if let Some(d) = skin.or(class) {
            portrait(ui, d, c.tex1, c.tex2, well[0] + well[2] / 2.0, well[1] + well[3] / 2.0, s * 0.85);
        }
        let tx = well[0] + well[2] + 12.0 * s;
        let name = class.map_or("Unknown", |d| d.display_id.as_str());
        ui.label(name, tx, r[1] + 9.0 * s, (18.0 * s).round(), true, color::TEXT);
        let size = (13.0 * s).round();
        let mut x = tx;
        for (label, value, col) in [("Level", c.level, color::TEXT), ("Fame", c.fame, color::ACCENT)] {
            ui.label(label, x, r[1] + 35.0 * s, size, false, color::TEXT_DIM);
            x += ui.measure(label, size, false) + 5.0 * s;
            let v = value.to_string();
            ui.label(&v, x, r[1] + 35.0 * s, size, true, col);
            x += ui.measure(&v, size, true) + 12.0 * s;
        }
        // What it wears, edged by tier.
        let side = 30.0 * s;
        for (k, &item) in c.equipment.iter().take(4).enumerate() {
            let sx = r[0] + r[2] - 12.0 * s - (4 - k) as f32 * (side + 4.0 * s) + 4.0 * s;
            let sr = [sx, r[1] + 26.0 * s, side, side];
            let edge = if item > 0 { tier_edge(ui.data, item) } else { None };
            ui.slot_box(sr, edge, false, s);
            if item > 0 {
                ui.item_icon(item, 80, sr[0] + side / 2.0, sr[1] + side / 2.0, s * 0.64);
                let imprints = bsk_data::imprint::parse(c.item_datas.get(k).map_or("", String::as_str));
                crate::hud::draw_imprint_marks(ui, imprints, sr[0] + 3.0 * s, sr[1] + 3.0 * s, s * 0.8);
            }
        }
        // The class quest it's working on (`makeTagline`): its fame of the next one's.
        let size = (12.0 * s).round();
        let qy = r[1] + r[3] - 24.0 * s;
        let quest = match stars::next_star_fame(best, c.fame) {
            Some(next) => format!("Class Quest: {} of {next} Fame", c.fame),
            None => "Class Quests completed".to_owned(),
        };
        theme::star(ui.renderer, tx + 7.0 * s, qy + size * 0.6, 7.0 * s, stars::GREY);
        ui.label(&quest, tx + 18.0 * s, qy, size, false, stars::GREY);
        // Delete, top right, only under the mouse.
        let d = [r[0] + r[2] - 30.0 * s, r[1] + 6.0 * s, 24.0 * s, 24.0 * s];
        if over || inside(d, mouse) {
            let over_d = inside(d, mouse);
            theme::fill(ui.renderer, d, 12.0 * s, if over_d { color::DANGER } else { color::SECONDARY }, 0.95);
            theme::cross(ui.renderer, d[0] + d[2] / 2.0, d[1] + d[3] / 2.0, 4.5 * s, (2.0 * s).max(1.5), 0xFFFFFF);
            self.targets.push((Target::Delete(c.id), d));
        }
    }

    fn draw_new_char(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let Some(list) = self.list.clone() else { return };
        let classes = player_classes(ui.data);
        let pad = 24.0 * s;
        let title = "Choose a class";
        let ts = (26.0 * s).round();
        let tw = ui.measure(title, ts, true);
        ui.label(title, (w - tw) / 2.0, pad, ts, true, color::TEXT);
        // `CreateNewCharacterRect`'s tagline: the class quests still to do.
        let left = stars::max_stars(classes.len() as i32) - stars::account_stars(&list);
        let sub = if left > 0 { format!("{left} class quests not yet completed") } else { "Every class quest completed".to_owned() };
        let subs = (13.0 * s).round();
        let sw = ui.measure(&sub, subs, false);
        ui.label(&sub, (w - sw) / 2.0, pad + 34.0 * s, subs, false, color::TEXT_DIM);
        // Tiles shrink until every class fits between the title and the Back button.
        let top = pad + 56.0 * s;
        let room = h - top - 70.0 * s;
        let mut k = s;
        let (mut cols, mut rows);
        loop {
            let (tw, th, gap) = (128.0 * k, 156.0 * k, 12.0 * k);
            cols = (((w - 2.0 * pad + gap) / (tw + gap)).floor() as usize).clamp(1, 12);
            rows = classes.len().div_ceil(cols);
            if rows as f32 * (th + gap) <= room || k <= s * 0.45 {
                break;
            }
            k *= 0.93;
        }
        let (tw_, th_, gap) = (128.0 * k, 156.0 * k, 12.0 * k);
        let x0 = (w - (cols as f32 * (tw_ + gap) - gap)) / 2.0;
        let y0 = top.max(top + (room - rows as f32 * (th_ + gap)) / 2.0);
        for (i, d) in classes.iter().enumerate() {
            let r = [x0 + (i % cols) as f32 * (tw_ + gap), y0 + (i / cols) as f32 * (th_ + gap), tw_, th_];
            // Every class is open: the classes' `<UnlockLevel>` requirements don't apply here.
            let over = inside(r, mouse);
            let r = if over { [r[0], r[1] - 3.0 * k, r[2], r[3]] } else { r };
            theme::shadow(ui.renderer, r, 10.0 * k, if over { 14.0 * k } else { 8.0 * k }, 0.45);
            let edge = if over { color::ACCENT } else { color::BORDER_HI };
            theme::frame(ui.renderer, r, 10.0 * k, k.max(1.0), (edge, 1.0), ((color::PANEL_TOP, 0.95), (color::PANEL_BOTTOM, 0.97)));
            let well = [r[0] + 14.0 * k, r[1] + 12.0 * k, r[2] - 28.0 * k, r[2] - 28.0 * k];
            theme::rounded(ui.renderer, well, 8.0 * k, (color::FIELD, 0.85), (color::FIELD_BOTTOM, 0.85));
            portrait(ui, d, 0, 0, well[0] + well[2] / 2.0, well[1] + well[3] / 2.0, k * 1.15);
            // Its class quests, along the bottom of the well.
            let done = stars::num_stars(list.class_stats.get(&d.object_type).map_or(0, |cs| cs.1));
            let sk = k * 0.8;
            stars::row(ui.renderer, well[0] + (well[2] - stars::row_width(sk)) / 2.0, well[1] + well[3] - 9.0 * k, sk, done);
            // The name, then one line under it: the skins.
            let size = (15.0 * k).round().max(9.0);
            let ny = well[1] + well[3] + 6.0 * k;
            let nw = ui.measure(&d.display_id, size, true);
            ui.label(&d.display_id, r[0] + (tw_ - nw) / 2.0, ny, size, true, color::TEXT);
            let small = (11.0 * k).round().max(8.0);
            let ly = ny + size * 1.3;
            self.targets.push((Target::Class(d.object_type), r));
            let n = class_skins(ui.data, d.object_type, &list).len();
            if n > 0 {
                let t = format!("{} skin{}", n, if n == 1 { "" } else { "s" });
                let tw = ui.measure(&t, small, false);
                ui.label(&t, r[0] + (tw_ - tw) / 2.0, ly, small, false, color::TEXT_MUTED);
            }
        }
        self.button(ui, Target::Back, [(w - 140.0 * s) / 2.0, h - 58.0 * s, 140.0 * s, 40.0 * s], "Back", Kind::Secondary, s, mouse);
        self.message_line(ui, w / 2.0, h - 92.0 * s, w * 0.8, s);
    }

    fn draw_confirm(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let Some(confirm) = self.confirm else { return };
        let list = self.list.clone().unwrap_or_default();
        let (title, detail, yes, kind) = match confirm {
            Confirm::Delete(id) => {
                let what = list
                    .chars
                    .iter()
                    .find(|c| c.id == id)
                    .map(|c| format!("{} (level {})", ui.data.objects.get(&c.object_type).map_or("character", |d| d.display_id.as_str()), c.level))
                    .unwrap_or_else(|| "this character".into());
                (format!("Delete {what}?"), "Its items and fame are gone for good.".to_owned(), "Delete", Kind::Danger)
            }
            Confirm::BuySlot => {
                let cur = if list.next_slot_currency == 1 { "fame" } else { "gold" };
                (format!("Buy a character slot for {} {cur}?", list.next_slot_price), "You'll have room for one more character.".to_owned(), "Buy", Kind::Primary)
            }
            Confirm::BuySkin(t) => {
                let skin = ui.data.objects.get(&t);
                let name = skin.map_or("this skin", |d| d.display_id.as_str());
                let cost = skin.map_or(300, |d| d.xml.int("Cost", 300));
                (format!("Buy {name} for {cost} gold?"), format!("You have {} gold.", list.credits), "Buy", Kind::Primary)
            }
        };
        // Only the dialog's buttons answer while it's up.
        self.targets.clear();
        ui.rect(0.0, 0.0, w, h, 0x000000, 0.55);
        let tsize = (20.0 * s).round();
        let width = (ui.measure(&title, tsize, true) + 52.0 * s).max(440.0 * s).min(w - 32.0);
        let r = [(w - width) / 2.0, (h - 170.0 * s) / 2.0, width, 170.0 * s];
        theme::panel(ui.renderer, r, s);
        ui.label(&title, r[0] + 26.0 * s, r[1] + 24.0 * s, tsize, true, color::TEXT);
        ui.label(&detail, r[0] + 26.0 * s, r[1] + 58.0 * s, (14.0 * s).round(), false, color::TEXT_DIM);
        let by = r[1] + r[3] - 62.0 * s;
        self.button(ui, Target::ConfirmNo, [r[0] + r[2] - 290.0 * s, by, 128.0 * s, 40.0 * s], "Cancel", Kind::Secondary, s, mouse);
        self.button(ui, Target::ConfirmYes, [r[0] + r[2] - 152.0 * s, by, 128.0 * s, 40.0 * s], yes, kind, s, mouse);
    }

    /// A class's page: its look in each skin, pick one (or buy it), then Create.
    fn draw_class(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, class: u16, mouse: (f64, f64)) {
        let Some(list) = self.list.clone() else { return };
        let Some(desc) = ui.data.objects.get(&class) else { return };
        let skins = class_skins(ui.data, class, &list);
        let pad = 24.0 * s;
        // The class, large, in the chosen skin.
        let left = [pad.max(w * 0.06), 90.0 * s, 300.0 * s, h - 180.0 * s];
        theme::panel(ui.renderer, left, s);
        let ts = (28.0 * s).round();
        ui.label(&desc.display_id, left[0] + 22.0 * s, left[1] + 18.0 * s, ts, true, color::TEXT);
        let shown = if self.skin != 0 { ui.data.objects.get(&self.skin).unwrap_or(desc) } else { desc };
        let well = [left[0] + 22.0 * s, left[1] + 64.0 * s, left[2] - 44.0 * s, left[2] - 44.0 * s];
        theme::rounded(ui.renderer, well, 10.0 * s, (color::FIELD, 0.9), (color::INSET_TOP, 0.9));
        portrait(ui, shown, 0, 0, well[0] + well[2] / 2.0, well[1] + well[3] / 2.0, s * 3.0);
        let skin_name = if self.skin != 0 { shown.display_id.clone() } else { "Classic".to_owned() };
        let ss = (16.0 * s).round();
        let sw = ui.measure(&skin_name, ss, true);
        ui.label(&skin_name, well[0] + (well[2] - sw) / 2.0, well[1] + well[3] + 10.0 * s, ss, true, color::ACCENT);
        // The class quests (`ClassToolTip`): those done, the bests, and the next goal.
        let (best_level, best_fame) = list.class_stats.get(&class).copied().unwrap_or((0, 0));
        let done = stars::num_stars(best_fame);
        let qy = well[1] + well[3] + 42.0 * s;
        stars::row(ui.renderer, left[0] + 22.0 * s, qy + 8.0 * s, s, done);
        let size = (13.0 * s).round();
        let qx = left[0] + 22.0 * s + stars::row_width(s) + 10.0 * s;
        ui.label(&format!("{done} of {} Class Quests", stars::STARS.len()), qx, qy, size, true, color::TEXT);
        ui.label(&format!("Best level {best_level}, best fame {best_fame}"), left[0] + 22.0 * s, qy + 22.0 * s, size, false, 0x5EB531);
        let mut dy = qy + 40.0 * s;
        if let Some(next) = stars::next_star_fame(best_fame, 0) {
            ui.label(&format!("Next goal: earn {next} fame with a {}", desc.display_id), left[0] + 22.0 * s, dy, size, false, 0xFC8642);
            dy += 18.0 * s;
        }
        if let Some(d) = desc.xml.lines_of("Description") {
            let mut y = dy + 12.0 * s;
            // Each of its lines wrapped on its own; an empty one is a gap between paragraphs.
            for para in d.lines() {
                let mut line = String::new();
                for word in para.split_whitespace() {
                    let next = if line.is_empty() { word.to_owned() } else { format!("{line} {word}") };
                    if ui.measure(&next, size, false) > left[2] - 44.0 * s && !line.is_empty() {
                        ui.label(&line, left[0] + 22.0 * s, y, size, false, color::TEXT_DIM);
                        y += size * 1.3;
                        line = word.to_owned();
                    } else {
                        line = next;
                    }
                }
                ui.label(&line, left[0] + 22.0 * s, y, size, false, color::TEXT_DIM);
                y += size * 1.3;
            }
        }
        self.button(ui, Target::Create, [left[0] + 22.0 * s, left[1] + left[3] - 62.0 * s, left[2] - 44.0 * s, 44.0 * s], "Create character", Kind::Primary, s, mouse);

        // The skins: the class's own look, then each skin.
        let x0 = left[0] + left[2] + 24.0 * s;
        let ts2 = (20.0 * s).round();
        ui.label("Skins", x0, left[1] + 4.0 * s, ts2, true, color::TEXT);
        let gold = format!("{} gold", list.credits);
        let gs = (14.0 * s).round();
        let skins_w = ui.measure("Skins", ts2, true);
        ui.label(&gold, x0 + skins_w + 16.0 * s, left[1] + 9.0 * s, gs, true, 0xFFE27A);
        let (tw_, th_, gap) = (104.0 * s, 128.0 * s, 10.0 * s);
        let cols = (((w - x0 - pad + gap) / (tw_ + gap)).floor() as usize).max(1);
        let mut entries: Vec<(u16, &ObjectDesc, bool, Option<String>)> = vec![(0, desc, true, None)];
        for k in &skins {
            let note = if k.owned {
                None
            } else if let Some(level) = k.needs_level {
                Some(format!("Level {level}"))
            } else if k.special {
                Some("Special".to_owned())
            } else {
                Some(format!("{} gold", k.cost))
            };
            entries.push((k.desc.object_type, k.desc, k.owned, note));
        }
        let max_rows = (((left[3] - 40.0 * s) / (th_ + gap)).floor() as usize).max(1);
        for (i, (t, d, owned, note)) in entries.iter().take(cols * max_rows).enumerate() {
            let r = [x0 + (i % cols) as f32 * (tw_ + gap), left[1] + 40.0 * s + (i / cols) as f32 * (th_ + gap), tw_, th_];
            let selected = self.skin == *t;
            let buyable = !owned && note.as_deref().is_some_and(|n| n.ends_with("gold"));
            let usable = *owned || *t == 0;
            let over = inside(r, mouse) && (usable || buyable) && self.confirm.is_none();
            let edge = if selected { color::ACCENT } else if over { color::ACCENT_HI } else { color::BORDER };
            theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (edge, 1.0), ((color::PANEL_TOP, 0.95), (color::PANEL_BOTTOM, 0.97)));
            portrait(ui, d, 0, 0, r[0] + r[2] / 2.0, r[1] + 46.0 * s, s * 1.3);
            let name = if *t == 0 { "Classic" } else { d.display_id.as_str() };
            let size = (12.0 * s).round().max(8.0);
            let mut label = name.to_owned();
            while ui.measure(&label, size, true) > r[2] - 8.0 * s && label.len() > 3 {
                label.pop();
            }
            let nw = ui.measure(&label, size, true);
            ui.label(&label, r[0] + (r[2] - nw) / 2.0, r[1] + 88.0 * s, size, true, if usable { color::TEXT } else { color::TEXT_DIM });
            if let Some(n) = note {
                let small = (11.0 * s).round().max(8.0);
                let w2 = ui.measure(n, small, true);
                let c = if buyable { if list.credits >= 300 { color::ACCENT } else { color::DANGER } } else { 0xFFB070 };
                ui.label(n, r[0] + (r[2] - w2) / 2.0, r[1] + 106.0 * s, small, true, c);
            }
            if usable || buyable {
                self.targets.push((Target::Skin(*t), r));
            }
        }
        self.button(ui, Target::Back, [x0, h - 70.0 * s, 120.0 * s, 40.0 * s], "Back", Kind::Secondary, s, mouse);
        self.message_line(ui, w / 2.0, h - 26.0 * s, w * 0.8, s);
    }

    /// The legends: the most famous characters this week, month or ever; or the server's latest
    /// deaths, each opening its fame summary.
    fn draw_legends(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        self.legends_bar = None;
        let pad = 24.0 * s;
        let ts = (26.0 * s).round();
        let tw = ui.measure("Legends", ts, true);
        ui.label("Legends", (w - tw) / 2.0, pad, ts, true, color::TEXT);
        // The time spans, as pills.
        let n = TIMESPANS.len() as f32;
        let pw = (130.0 * s).min((w - 32.0 - (n - 1.0) * 10.0 * s) / n);
        let x0 = (w - n * pw - (n - 1.0) * 10.0 * s) / 2.0;
        for (i, (_, label)) in TIMESPANS.iter().enumerate() {
            let r = [x0 + i as f32 * (pw + 10.0 * s), pad + 46.0 * s, pw, 30.0 * s];
            let on = i == self.timespan;
            let over = inside(r, mouse);
            theme::fill(ui.renderer, r, r[3] / 2.0, if on { color::ACCENT } else if over { color::SECONDARY } else { color::CHIP }, 0.95);
            let size = (14.0 * s).round();
            let lw = ui.measure(label, size, true);
            ui.label(label, r[0] + (r[2] - lw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
            self.targets.push((Target::Timespan(i), r));
        }
        let list_r = [(w - 760.0 * s).max(16.0) / 2.0, pad + 92.0 * s, (760.0 * s).min(w - 32.0), h - pad * 2.0 - 160.0 * s];
        theme::panel(ui.renderer, list_r, s);
        let deaths = self.timespan == RECENT;
        let rows: Option<Vec<LegendRow>> = if deaths {
            self.recent.as_ref().map(|d| d.iter().take(20).map(LegendRow::death).collect())
        } else {
            self.legends.as_ref().map(|l| l.iter().take(20).map(LegendRow::legend).collect())
        };
        match &rows {
            None => spinner(ui, list_r[0] + list_r[2] / 2.0, list_r[1] + list_r[3] / 2.0, 16.0 * s, ui_time(), s),
            Some(rows) if rows.is_empty() => {
                let t = if deaths { "No one has died yet." } else { "No legends yet. Be the first!" };
                let size = (16.0 * s).round();
                let tw = ui.measure(t, size, true);
                ui.label(t, list_r[0] + (list_r[2] - tw) / 2.0, list_r[1] + list_r[3] / 2.0, size, true, color::TEXT_DIM);
            }
            Some(rows) => {
                // Up to 20, as many as fit; the rest scroll, a row a wheel notch or by the
                // scrollbar, with a line under them saying how many more.
                let row_h = 50.0 * s;
                let fit = (((list_r[3] - 16.0 * s) / row_h).floor().max(1.0) as usize).min(rows.len());
                let more = fit < rows.len();
                self.legends_row = self.legends_row.min(rows.len() - fit);
                let first = self.legends_row;
                let bar_w = if more { 14.0 * s } else { 0.0 };
                // A narrow list leaves out the equipment, so the names keep their room.
                let gear = list_r[2] - bar_w >= 560.0 * s;
                for (i, l) in rows.iter().enumerate().skip(first).take(fit) {
                    let y = list_r[1] + 8.0 * s + (i - first) as f32 * row_h;
                    let r = [list_r[0] + 8.0 * s, y, list_r[2] - 16.0 * s - bar_w, row_h - 4.0 * s];
                    let over = l.target.is_some() && self.legends_grab.is_none() && inside(r, mouse);
                    if over || i % 2 == 0 {
                        theme::fill(ui.renderer, r, 6.0 * s, 0xFFFFFF, if over { 0.08 } else { 0.03 });
                    }
                    // The legends are ranked; the deaths aren't, so their characters stand
                    // further left.
                    let left = if deaths { 0.0 } else { 40.0 * s };
                    if !deaths {
                        let rank = format!("{}", i + 1);
                        let rs = (18.0 * s).round();
                        let rc = match i {
                            0 => 0xFFD24A,
                            1 => 0xD8DDE6,
                            2 => 0xE0975A,
                            _ => color::TEXT_MUTED,
                        };
                        ui.label(&rank, r[0] + 12.0 * s, r[1] + (r[3] - rs * 1.22) / 2.0, rs, true, rc);
                    }
                    let class = ui.data.objects.get(&l.object_type);
                    let skin = (l.skin != 0).then(|| ui.data.objects.get(&(l.skin as u16))).flatten();
                    if let Some(d) = skin.or(class) {
                        portrait(ui, d, l.tex1, l.tex2, r[0] + 30.0 * s + left, r[1] + r[3] / 2.0, s * 0.8);
                    }
                    let ns = (16.0 * s).round();
                    ui.label(&l.name, r[0] + 60.0 * s + left, r[1] + 4.0 * s, ns, true, color::TEXT);
                    let class_name = class.map_or("", |d| d.display_id.as_str());
                    let fame = format!("{}", l.total_fame);
                    let fs = (18.0 * s).round();
                    let fw = ui.measure(&fame, fs, true);
                    match &l.death {
                        None => {
                            ui.label(class_name, r[0] + 60.0 * s + left, r[1] + 24.0 * s, (12.0 * s).round(), false, color::TEXT_DIM);
                            for (k, &item) in l.equipment.iter().take(if gear { 4 } else { 0 }).enumerate() {
                                let side = 32.0 * s;
                                let sr = [r[0] + r[2] - 270.0 * s + k as f32 * (side + 4.0 * s), r[1] + (r[3] - side) / 2.0, side, side];
                                ui.slot_box(sr, if item > 0 { tier_edge(ui.data, item) } else { None }, false, s);
                                if item > 0 {
                                    ui.item_icon(item, 80, sr[0] + side / 2.0, sr[1] + side / 2.0, s * 0.7);
                                    let imprints = bsk_data::imprint::parse(l.item_datas.get(k).map_or("", String::as_str));
                                    crate::hud::draw_imprint_marks(ui, imprints, sr[0] + 3.0 * s, sr[1] + 3.0 * s, s * 0.8);
                                }
                            }
                            ui.label(&fame, r[0] + r[2] - fw - 14.0 * s, r[1] + (r[3] - fs * 1.22) / 2.0, fs, true, color::ACCENT);
                        }
                        Some((level, killer, world, ago)) => {
                            // Beside the name its level; under it the class, what killed it and
                            // where; on the right the fame, and how long ago.
                            let lx = r[0] + 60.0 * s + left + ui.measure(&l.name, ns, true) + 8.0 * s;
                            let ls = (13.0 * s).round();
                            ui.label(&format!("Lv {level}"), lx, r[1] + 4.0 * s + (ns - ls) * 0.9, ls, false, color::TEXT_MUTED);
                            let by = if killer.is_empty() { "something" } else { killer.as_str() };
                            let mut how = format!("{class_name} · killed by {by}");
                            if !world.is_empty() {
                                how.push_str(&format!(" in {world}"));
                            }
                            let hs = (12.0 * s).round();
                            let room = r[2] - 60.0 * s - left - 120.0 * s;
                            if ui.measure(&how, hs, false) > room {
                                while how.chars().count() > 4 && ui.measure(&format!("{how}…"), hs, false) > room {
                                    how.pop();
                                }
                                how = format!("{}…", how.trim_end());
                            }
                            ui.label(&how, r[0] + 60.0 * s + left, r[1] + 25.0 * s, hs, false, color::TEXT_DIM);
                            ui.label(&fame, r[0] + r[2] - fw - 14.0 * s, r[1] + 4.0 * s, fs, true, color::ACCENT);
                            let when = time_ago(*ago);
                            let ws = (12.0 * s).round();
                            let ww = ui.measure(&when, ws, false);
                            ui.label(&when, r[0] + r[2] - ww - 14.0 * s, r[1] + 26.0 * s, ws, false, color::TEXT_MUTED);
                        }
                    }
                    if let Some(t) = l.target {
                        self.targets.push((t, r));
                    }
                }
                if more {
                    // Under the list, which rows these are and that there's more below.
                    let below = rows.len() - first - fit;
                    let hint = if below > 0 {
                        format!("{} to {} of {} · scroll for {below} more", first + 1, first + fit, rows.len())
                    } else {
                        format!("{} to {} of {}", first + 1, first + fit, rows.len())
                    };
                    let hs = (12.0 * s).round().max(9.0);
                    let hw = ui.measure(&hint, hs, false);
                    // In the panel under the rows when there's room, else just under the panel.
                    let (rows_end, panel_end) = (list_r[1] + 8.0 * s + fit as f32 * row_h, list_r[1] + list_r[3]);
                    let room = panel_end - rows_end - 4.0 * s;
                    let hy = if room >= hs * 1.3 { rows_end + (room - hs * 1.22) / 2.0 } else { panel_end + 3.0 * s };
                    ui.label(&hint, (w - hw) / 2.0, hy, hs, false, if below > 0 { color::TEXT_DIM } else { color::TEXT_MUTED });
                    let track = [list_r[0] + list_r[2] - 8.0 * s - bar_w + 4.0 * s, list_r[1] + 8.0 * s, 6.0 * s, fit as f32 * row_h - 4.0 * s];
                    let hit = [track[0] - 6.0 * s, track[1], track[2] + 12.0 * s, track[3]];
                    let hot = self.legends_grab.is_some() || inside(hit, mouse);
                    theme::fill(ui.renderer, track, 3.0 * s, color::TRACK, 0.9);
                    let thumb_h = track[3] * fit as f32 / rows.len() as f32;
                    let thumb_y = track[1] + (track[3] - thumb_h) * first as f32 / (rows.len() - fit) as f32;
                    theme::fill(ui.renderer, [track[0], thumb_y, track[2], thumb_h], 3.0 * s, if hot { color::ACCENT_HI } else { color::ACCENT }, 0.9);
                    self.targets.push((Target::LegendsBar, hit));
                    self.legends_bar = Some((track, rows.len(), fit));
                }
            }
        }
        self.button(ui, Target::Back, [(w - 140.0 * s) / 2.0, h - 66.0 * s, 140.0 * s, 40.0 * s], "Back", Kind::Secondary, s, mouse);
    }

    /// `Graveyard`: the account's dead characters, newest first; each opens its fame summary.
    fn draw_graveyard(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let pad = 24.0 * s;
        let ts = (26.0 * s).round();
        let tw = ui.measure("Graveyard", ts, true);
        ui.label("Graveyard", (w - tw) / 2.0, pad, ts, true, color::TEXT);
        let graves: Vec<_> = self.list.as_ref().map_or_else(Vec::new, |l| l.news.iter().filter(|n| n.death_char_id().is_some()).cloned().collect());
        let list_r = [(w - 640.0 * s).max(16.0) / 2.0, pad + 56.0 * s, (640.0 * s).min(w - 32.0), h - pad * 2.0 - 124.0 * s];
        theme::panel(ui.renderer, list_r, s);
        if graves.is_empty() {
            let t = "No one has fallen yet.";
            let size = (16.0 * s).round();
            let tw = ui.measure(t, size, true);
            ui.label(t, list_r[0] + (list_r[2] - tw) / 2.0, list_r[1] + list_r[3] / 2.0, size, true, color::TEXT_DIM);
        }
        let now = web_time::SystemTime::now().duration_since(web_time::UNIX_EPOCH).map_or(0, |d| d.as_secs() as i64);
        let row_h = 58.0 * s;
        let fit = ((list_r[3] - 16.0 * s) / row_h).floor().max(1.0) as usize;
        for (i, g) in graves.iter().take(fit).enumerate() {
            let r = [list_r[0] + 8.0 * s, list_r[1] + 8.0 * s + i as f32 * row_h, list_r[2] - 16.0 * s, row_h - 6.0 * s];
            let over = inside(r, mouse);
            theme::fill(ui.renderer, r, 6.0 * s, 0xFFFFFF, if over { 0.08 } else if i % 2 == 0 { 0.03 } else { 0.0 });
            // A tombstone.
            let (gx, gy) = (r[0] + 14.0 * s, r[1] + 9.0 * s);
            theme::fill(ui.renderer, [gx, gy, 26.0 * s, 32.0 * s], 11.0 * s, color::TEXT_MUTED, 1.0);
            theme::fill(ui.renderer, [gx + 11.0 * s, gy + 7.0 * s, 4.0 * s, 16.0 * s], 1.0, color::BORDER, 1.0);
            theme::fill(ui.renderer, [gx + 6.0 * s, gy + 12.0 * s, 14.0 * s, 4.0 * s], 1.0, color::BORDER, 1.0);
            ui.label(&g.title, r[0] + 56.0 * s, r[1] + 7.0 * s, (16.0 * s).round(), true, color::TEXT);
            ui.label(&g.tagline, r[0] + 56.0 * s, r[1] + 29.0 * s, (13.0 * s).round(), false, color::ACCENT);
            let days = (now - g.date).max(0) / 86_400;
            let when = match days {
                0 => "today".to_owned(),
                1 => "yesterday".to_owned(),
                d => format!("{d} days ago"),
            };
            let ws = (12.0 * s).round();
            let ww = ui.measure(&when, ws, false);
            ui.label(&when, r[0] + r[2] - ww - 14.0 * s, r[1] + (r[3] - ws * 1.22) / 2.0, ws, false, color::TEXT_MUTED);
            if let Some(id) = g.death_char_id() {
                self.targets.push((Target::Grave(id), r));
            }
        }
        self.button(ui, Target::Back, [(w - 140.0 * s) / 2.0, h - 66.0 * s, 140.0 * s, 40.0 * s], "Back", Kind::Secondary, s, mouse);
    }

    /// `ServersScreen`: the world servers with how full each is; a click picks one and goes back
    /// to the characters.
    fn draw_servers(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let pad = 24.0 * s;
        let ts = (26.0 * s).round();
        let tw = ui.measure("Servers", ts, true);
        ui.label("Servers", (w - tw) / 2.0, pad, ts, true, color::TEXT);
        let servers = self.list.as_ref().map_or_else(Vec::new, |l| l.servers.clone());
        let list_r = [(w - 640.0 * s).max(16.0) / 2.0, pad + 56.0 * s, (640.0 * s).min(w - 32.0), h - pad * 2.0 - 124.0 * s];
        theme::panel(ui.renderer, list_r, s);
        if servers.is_empty() {
            let t = "The account server lists no world servers.";
            let size = (16.0 * s).round();
            let tw = ui.measure(t, size, true);
            ui.label(t, list_r[0] + (list_r[2] - tw) / 2.0, list_r[1] + list_r[3] / 2.0, size, true, color::TEXT_DIM);
        }
        let chosen = self.server.min(servers.len().saturating_sub(1));
        let row_h = 58.0 * s;
        let fit = ((list_r[3] - 16.0 * s) / row_h).floor().max(1.0) as usize;
        for (i, server) in servers.iter().take(fit).enumerate() {
            let r = [list_r[0] + 8.0 * s, list_r[1] + 8.0 * s + i as f32 * row_h, list_r[2] - 16.0 * s, row_h - 6.0 * s];
            let over = inside(r, mouse);
            let edge = if i == chosen { color::ACCENT } else if over { color::BORDER_HI } else { color::BORDER };
            theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (edge, 1.0), ((color::SLOT_TOP, if over { 0.95 } else { 0.8 }), (color::SLOT_BOTTOM, 0.9)));
            let ns = (18.0 * s).round();
            let name = if server.admin_only { format!("{} (admin)", server.name) } else { server.name.clone() };
            ui.label(&name, r[0] + 18.0 * s, r[1] + (r[3] - ns * 1.22) / 2.0, ns, true, color::TEXT);
            // As `ServerBox`: Full at 100%, Crowded from 66%, with the player count.
            let (status, c) = if server.usage >= 1.0 {
                ("Full", color::DANGER)
            } else if server.usage >= 0.66 {
                ("Crowded", 0xFC8642)
            } else {
                ("Normal", color::GOOD)
            };
            let status = if server.usage_text.is_empty() { status.to_owned() } else { format!("{status} {}", server.usage_text) };
            let ss = (16.0 * s).round();
            let sw = ui.measure(&status, ss, true);
            ui.label(&status, r[0] + r[2] - sw - 18.0 * s, r[1] + (r[3] - ss * 1.22) / 2.0, ss, true, c);
            self.targets.push((Target::Server(i), r));
        }
        self.button(ui, Target::Back, [(w - 140.0 * s) / 2.0, h - 66.0 * s, 140.0 * s, 40.0 * s], "Back", Kind::Secondary, s, mouse);
    }

    /// Account settings: change the password.
    fn draw_account(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64), now: i32) {
        let r = [(w - 380.0 * s) / 2.0, ((h - 420.0 * s) / 2.0).max(16.0), 380.0 * s, 420.0 * s];
        let [x, y, bw, bh] = r;
        theme::panel(ui.renderer, r, s);
        let pad = 28.0 * s;
        ui.label("Account", x + pad, y + 24.0 * s, (24.0 * s).round(), true, color::TEXT);
        let email = self.fields[EMAIL].text.clone();
        ui.label(&email, x + pad, y + 58.0 * s, (14.0 * s).round(), false, color::TEXT_DIM);
        let mut fy = y + 90.0 * s;
        fy = self.field(ui, PASSWORD, "Current password", "Your password", x + pad, fy, bw - 2.0 * pad, s, now);
        fy = self.field(ui, NEW_PASSWORD, "New password", "At least 6 characters", x + pad, fy, bw - 2.0 * pad, s, now);
        fy = self.field(ui, NEW_CONFIRM, "Confirm new password", "Type it again", x + pad, fy, bw - 2.0 * pad, s, now);
        let half = (bw - 2.0 * pad - 10.0 * s) / 2.0;
        self.button(ui, Target::Back, [x + pad, fy + 4.0 * s, half, 42.0 * s], "Back", Kind::Secondary, s, mouse);
        self.button(ui, Target::SavePassword, [x + pad + half + 10.0 * s, fy + 4.0 * s, half, 42.0 * s], "Change password", Kind::Primary, s, mouse);
        self.message_line(ui, x + bw / 2.0, y + bh + 16.0 * s, bw, s);
    }

    /// `FameView`: the dead character, how their fame was counted, and some of what they did.
    fn draw_death(&mut self, ui: &mut Ui, w: f32, h: f32, s: f32, mouse: (f64, f64)) {
        let Some(sum) = self.death.clone() else {
            self.show(Screen::Chars);
            return;
        };
        let r = [(w - 520.0 * s) / 2.0, 30.0 * s, 520.0 * s, h - 60.0 * s];
        theme::panel(ui.renderer, r, s);
        let class = ui.data.objects.get(&sum.object_type);
        let pad = 28.0 * s;
        let head = if self.death_from_graveyard || self.death_from_legends { "In memory" } else { "You died" };
        ui.label(head, r[0] + pad, r[1] + 20.0 * s, (30.0 * s).round(), true, color::DANGER);
        let who = format!("{}, level {}, killed by {}", class.map_or("Your character", |d| d.display_id.as_str()), sum.level, if sum.killed_by.is_empty() { "something" } else { &sum.killed_by });
        ui.label(&who, r[0] + pad, r[1] + 62.0 * s, (15.0 * s).round(), false, color::TEXT_DIM);
        if let Some(d) = class {
            portrait(ui, d, 0, 0, r[0] + r[2] - 70.0 * s, r[1] + 50.0 * s, s * 1.6);
        }
        // What it carried: its equipment, a gap, its inventory, and under them the backpack when
        // it had anything there; the mouse over an item shows its tooltip.
        let mut y = r[1] + 96.0 * s;
        let mut hovered = None;
        if !sum.equipment.is_empty() {
            let side = 34.0 * s;
            let gap = 4.0 * s;
            let rows: Vec<Vec<usize>> = {
                let mut rows = vec![(0..12).collect::<Vec<_>>()];
                if sum.equipment.iter().skip(12).any(|&t| t > 0) {
                    rows.push((12..sum.equipment.len().min(20)).collect());
                }
                rows
            };
            for (n, row) in rows.iter().enumerate() {
                for (k, &slot) in row.iter().enumerate() {
                    // the equipment apart from the inventory; the backpack lines up under it
                    let extra = if n == 0 && k >= 4 { 10.0 * s } else if n > 0 { 4.0 * (side + gap) + 10.0 * s } else { 0.0 };
                    let sr = [r[0] + pad + extra + k as f32 * (side + gap), y, side, side];
                    let item = sum.equipment.get(slot).copied().unwrap_or(-1);
                    ui.slot_box(sr, if item > 0 { tier_edge(ui.data, item) } else { None }, false, s);
                    if item > 0 {
                        ui.item_icon(item, 80, sr[0] + side / 2.0, sr[1] + side / 2.0, s * 0.72);
                        let imprints = bsk_data::imprint::parse(sum.item_datas.get(slot).map_or("", String::as_str));
                        crate::hud::draw_imprint_marks(ui, imprints, sr[0] + 3.0 * s, sr[1] + 3.0 * s, s * 0.8);
                        if inside(sr, mouse) {
                            hovered = Some(item);
                        }
                    }
                }
                y += side + gap;
            }
            y += 8.0 * s;
        }
        // The fame: base, the bonuses, the total.
        let size = (15.0 * s).round();
        let row = |ui: &mut Ui, label: &str, value: String, c: u32, bold: bool, y: &mut f32| {
            ui.label(label, r[0] + pad, *y, size, bold, color::TEXT_DIM);
            let vw = ui.measure(&value, size, true);
            ui.label(&value, r[0] + r[2] - pad - vw, *y, size, true, c);
            *y += size * 1.45;
        };
        row(ui, "Base fame", sum.base_fame.to_string(), color::TEXT, false, &mut y);
        let room = ((r[1] + r[3] - 230.0 * s - y) / (size * 1.45)).floor().max(0.0) as usize;
        for (desc, fame) in sum.bonuses.iter().take(room.min(12)) {
            row(ui, desc, format!("+{fame}"), color::GOOD, false, &mut y);
        }
        theme::fill(ui.renderer, [r[0] + pad, y + 2.0 * s, r[2] - 2.0 * pad, s.max(1.0)], 0.0, color::BORDER, 1.0);
        y += 10.0 * s;
        let big = (20.0 * s).round();
        ui.label("Total fame", r[0] + pad, y, big, true, color::TEXT);
        let total = sum.total_fame.to_string();
        let tw = ui.measure(&total, big, true);
        ui.label(&total, r[0] + r[2] - pad - tw, y, big, true, color::ACCENT);
        y += big * 1.8;
        // A few of the stats.
        let stat = |k: &str| sum.stats.iter().find(|(n, _)| n == k).map_or(0, |(_, v)| *v);
        let shots = stat("Shots");
        let accuracy = if shots > 0 { format!("{}%", stat("ShotsThatDamage") * 100 / shots) } else { "-".into() };
        let stats = [
            ("Monsters killed", stat("MonsterKills").to_string()),
            ("Gods killed", stat("GodKills").to_string()),
            ("Accuracy", accuracy),
            ("Quests completed", stat("QuestsCompleted").to_string()),
            ("Tiles uncovered", stat("TilesUncovered").to_string()),
            ("Minutes played", stat("MinutesActive").to_string()),
        ];
        let small = (13.0 * s).round();
        let colw = (r[2] - 2.0 * pad) / 2.0;
        for (i, (label, value)) in stats.iter().enumerate() {
            let x = r[0] + pad + (i % 2) as f32 * colw;
            let yy = y + (i / 2) as f32 * small * 1.6;
            if yy + small > r[1] + r[3] - 70.0 * s {
                break;
            }
            ui.label(label, x, yy, small, false, color::TEXT_MUTED);
            let vw = ui.measure(value, small, true);
            ui.label(value, x + colw - vw - 16.0 * s, yy, small, true, color::TEXT);
        }
        self.button(ui, Target::Back, [r[0] + (r[2] - 180.0 * s) / 2.0, r[1] + r[3] - 60.0 * s, 180.0 * s, 42.0 * s], "Continue", Kind::Primary, s, mouse);
        if let Some(item) = hovered {
            let lay = crate::hud::Layout::new(w as u32, h as u32);
            let lines = crate::hud::item_tooltip(ui.data, item, None, crate::hud::SlotKind::Shop);
            crate::hud::draw_tooltip(ui, &lay, &lines, Some(item), tier_edge(ui.data, item), mouse);
        }
    }
}

/// A row of the Legends screen: a legend, or one of Recent Deaths.
struct LegendRow {
    name: String,
    object_type: u16,
    tex1: i32,
    tex2: i32,
    skin: i32,
    equipment: Vec<i32>,
    item_datas: Vec<String>,
    total_fame: i32,
    /// A death's level, killer, world and seconds ago.
    death: Option<(i32, String, String, i64)>,
    target: Option<Target>,
}

impl LegendRow {
    fn legend(l: &Legend) -> Self {
        LegendRow {
            name: l.name.clone(),
            object_type: l.object_type,
            tex1: l.tex1,
            tex2: l.tex2,
            skin: l.skin,
            equipment: l.equipment.clone(),
            item_datas: l.item_datas.clone(),
            total_fame: l.total_fame,
            death: None,
            target: None,
        }
    }

    fn death(d: &RecentDeath) -> Self {
        LegendRow {
            name: d.name.clone(),
            object_type: d.object_type,
            tex1: d.tex1,
            tex2: d.tex2,
            skin: d.skin,
            equipment: Vec::new(),
            item_datas: Vec::new(),
            total_fame: d.total_fame,
            death: Some((d.level, d.killer.clone(), d.world.clone(), d.ago)),
            target: Some(Target::Fallen(d.account_id, d.char_id)),
        }
    }
}

/// How long ago, from seconds: "just now", "5 min ago", "3 h ago", "2 days ago".
fn time_ago(secs: i64) -> String {
    match secs.max(0) {
        0..=59 => "just now".into(),
        s @ 60..=3599 => format!("{} min ago", s / 60),
        s @ 3600..=86_399 => format!("{} h ago", s / 3600),
        s if s < 2 * 86_400 => "yesterday".into(),
        s => format!("{} days ago", s / 86_400),
    }
}

/// Over the sky: darker behind lists, so they read easily, and a little darker at the top.
fn draw_shade(ui: &mut Ui, w: f32, h: f32, screen: Screen) {
    let dark = match screen {
        Screen::Home | Screen::Login | Screen::Register | Screen::Busy => 0.0,
        Screen::Death => 0.6,
        _ => 0.4,
    };
    if dark > 0.0 {
        ui.rect(0.0, 0.0, w, h, color::BACKGROUND, dark);
    }
    theme::rounded(ui.renderer, [0.0, 0.0, w, h * 0.2], 0.0, (0x000000, 0.35), (0x000000, 0.0));
}

/// The game's name in pixel letters with a line under it, as the website's heading: in the
/// space left of `card_x` (the sign-in card) on a wide window, else across the top.
fn draw_logo(ui: &mut Ui, w: f32, h: f32, s: f32, card_x: f32) {
    let name = crate::TITLE;
    let tagline = "A Realm of the Mad God private server";
    let wide = w >= 1.3 * h && card_x < w;
    let (cx, room, top) = if wide {
        (card_x / 2.0, card_x - 64.0 * s, h * 0.3)
    } else if card_x < w {
        (w / 2.0, w * 0.86, h * 0.1)
    } else {
        (w / 2.0, w * 0.7, h * 0.3)
    };
    let cell = (room / sky::logo_width(name, 1.0)).min(10.0 * s).floor().max(2.0);
    let small = (11.0 * s).round().max(9.0);
    let over = "ENTER";
    let ow = ui.measure(over, small, true);
    ui.label(over, cx - ow / 2.0, top - small * 2.0, small, true, color::TEXT_DIM);
    let lh = sky::logo(ui, name, cx, top, cell);
    let size = (16.0 * s).round().max(10.0);
    let tw = ui.measure(tagline, size, false);
    ui.label(tagline, cx - tw / 2.0, top + lh + 8.0 * s, size, false, color::TEXT);
}

/// `text` broken into lines no wider than `max_w` (bold), at spaces where it can, else
/// anywhere (a long address).
fn wrap(ui: &mut Ui, text: &str, size: f32, max_w: f32) -> Vec<String> {
    let mut lines = Vec::new();
    let mut line = String::new();
    for word in text.split_whitespace() {
        let next = if line.is_empty() { word.to_owned() } else { format!("{line} {word}") };
        if ui.measure(&next, size, true) <= max_w || line.is_empty() && ui.measure(word, size, true) <= max_w {
            line = next;
            continue;
        }
        if !line.is_empty() {
            lines.push(std::mem::take(&mut line));
        }
        // A word wider than the line on its own: cut it.
        for c in word.chars() {
            line.push(c);
            if ui.measure(&line, size, true) > max_w && line.chars().count() > 1 {
                line.pop();
                lines.push(std::mem::replace(&mut line, c.to_string()));
            }
        }
    }
    if !line.is_empty() || lines.is_empty() {
        lines.push(line);
    }
    lines
}

fn ui_time() -> i32 {
    thread_local!(static START: web_time::Instant = web_time::Instant::now());
    START.with(|t| t.elapsed().as_millis() as i32)
}

/// Dots chasing round a circle.
fn spinner(ui: &mut Ui, cx: f32, cy: f32, radius: f32, now: i32, s: f32) {
    const DOTS: i32 = 10;
    let head = (now / 90) % DOTS;
    for i in 0..DOTS {
        let a = i as f32 / DOTS as f32 * std::f32::consts::TAU - std::f32::consts::FRAC_PI_2;
        let age = (head - i).rem_euclid(DOTS) as f32 / DOTS as f32;
        let d = 5.0 * s;
        let (x, y) = (cx + radius * a.cos(), cy + radius * a.sin());
        theme::fill(ui.renderer, [x - d / 2.0, y - d / 2.0, d, d], d / 2.0, color::ACCENT, 1.0 - age * 0.85);
    }
}

/// `RankToolTip`: the stars earned, how to earn more, and which colour each range of stars
/// shows, in a box with its top left at `at` (kept inside the window).
pub fn rank_tooltip(ui: &mut Ui, rank: i32, classes: i32, at: (f32, f32), s: f32) {
    let size = (13.0 * s).round();
    let line_h = 20.0 * s;
    let (w, h) = (250.0 * s, 64.0 * s + line_h * stars::COLORS.len() as f32);
    let (sw, sh) = ui.renderer.size();
    let r = [at.0.min(sw as f32 - w - 4.0).max(0.0), at.1.min(sh as f32 - h - 4.0).max(0.0), w, h];
    theme::shadow(ui.renderer, r, 8.0 * s, 12.0 * s, 0.5);
    theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (color::BORDER_HI, 0.9), ((color::PANEL_TOP, 0.97), (color::PANEL_BOTTOM, 0.98)));
    let (x, y) = (r[0] + 10.0 * s, r[1] + 8.0 * s);
    let earned = format!("You have earned {rank}");
    ui.label(&earned, x, y, size, true, stars::GREY);
    let ew = ui.measure(&earned, size, true);
    theme::star(ui.renderer, x + ew + 10.0 * s, y + size * 0.62, 6.5 * s, stars::GREY);
    ui.label("You can earn more by completing Class Quests.", x, y + 22.0 * s, (11.5 * s).round(), false, color::TEXT_DIM);
    let ly = y + 46.0 * s;
    theme::fill(ui.renderer, [x, ly - 4.0 * s, w - 20.0 * s, s.max(1.0)], 0.0, color::BORDER, 1.0);
    // LegendLine: a coloured star, the range, then a grey star.
    // the same steps as `stars::color`: the most stars split into ninths, the last up to the most
    let last = stars::COLORS.len() as i32 - 1;
    let ranges: Vec<(u32, String)> = (0..=last)
        .map(|i| {
            let (lo, hi) = (stars::step_start(i, classes), stars::step_start(i + 1, classes) - 1);
            (stars::COLORS[i as usize], format!(": {lo} - {}", if i == last { hi + 1 } else { hi }))
        })
        .collect();
    for (i, (c, text)) in ranges.iter().enumerate() {
        let ry = ly + 4.0 * s + i as f32 * line_h;
        theme::star(ui.renderer, x + 7.0 * s, ry + size * 0.62, 6.5 * s, *c);
        ui.label(text, x + 15.0 * s, ry, size, true, stars::GREY);
        let tw = ui.measure(text, size, true);
        theme::star(ui.renderer, x + 24.0 * s + tw, ry + size * 0.62, 6.5 * s, stars::GREY);
    }
}

/// A class's standing frame, dyed, centred at (cx, cy).
pub fn portrait(ui: &mut Ui, desc: &ObjectDesc, tex1: i32, tex2: i32, cx: f32, cy: f32, s: f32) {
    let TextureRef::Animated { set, index } = &desc.texture.pick(0).main else { return };
    let Some(src) = ui.sprites.char_src(set, *index, 0, 0) else { return };
    let dye = ui.sprites.char_mask_src(set, *index, 0, 0).map(|mask| Dye { mask, tex1, tex2 });
    // Redrawn at the size it shows, so the pixels stay sharp.
    let size = ((100.0 * s).round() as i32).max(1);
    let Some(img) = ui.redrawer.get(ui.sprites, ui.renderer, src, false, size, dye) else { return };
    let (iw, ih) = (img.w as f32, img.h as f32);
    // The redrawn image has room for the outline above and beside it: centre the sprite.
    let lift = (crate::redraw::PAD as f32 - 1.0) / 2.0;
    ui.renderer.sprite(img.rect, Quad::rect((cx - iw / 2.0).round(), (cy - ih / 2.0 - lift).round(), iw, ih), Tint::NONE);
}

/// The menu Escape opens in the game.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MenuChoice {
    Resume,
    /// Back to the Nexus.
    Nexus,
    Options,
    Changelog,
    /// Out of the game, to the main menu (the character list).
    Home,
    Logout,
}

/// Draw the in-game menu; returns the buttons for clicks.
pub fn draw_game_menu(ui: &mut Ui, w: f32, h: f32, mouse: (f64, f64)) -> Vec<(MenuChoice, [f32; 4])> {
    let s = scale(w, h);
    ui.rect(0.0, 0.0, w, h, 0x000000, 0.5);
    let r = [(w - 320.0 * s) / 2.0, (h - 420.0 * s) / 2.0, 320.0 * s, 420.0 * s];
    theme::panel(ui.renderer, r, s);
    let size = (22.0 * s).round();
    let tw = ui.measure("Paused", size, true);
    ui.label("Paused", r[0] + (r[2] - tw) / 2.0, r[1] + 20.0 * s, size, true, color::TEXT);
    let hint = "Esc to close";
    let hs = (12.0 * s).round();
    let hw = ui.measure(hint, hs, false);
    ui.label(hint, r[0] + (r[2] - hw) / 2.0, r[1] + 50.0 * s, hs, false, color::TEXT_MUTED);
    let mut out = Vec::new();
    for (i, (choice, label, kind)) in [
        (MenuChoice::Resume, "Back to the game", Kind::Primary),
        (MenuChoice::Nexus, "Nexus", Kind::Secondary),
        (MenuChoice::Options, "Options", Kind::Secondary),
        (MenuChoice::Changelog, "Changelog", Kind::Secondary),
        (MenuChoice::Home, "Return home", Kind::Secondary),
        (MenuChoice::Logout, "Log out", Kind::Danger),
    ]
    .into_iter()
    .enumerate()
    {
        let b = [r[0] + 30.0 * s, r[1] + 80.0 * s + i as f32 * 54.0 * s, r[2] - 60.0 * s, 42.0 * s];
        ui.button(b, label, kind, inside(b, mouse), s);
        out.push((choice, b));
    }
    out
}

pub fn menu_click(buttons: &[(MenuChoice, [f32; 4])], pos: (f64, f64)) -> Option<MenuChoice> {
    buttons.iter().find(|(_, r)| inside(*r, pos)).map(|(c, _)| *c)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn typing_and_tab() {
        let mut f = Front::default();
        f.show(Screen::Login);
        f.key(KeyCode::KeyA, Some("a@b"), false, false);
        f.key(KeyCode::Tab, None, false, false);
        f.key(KeyCode::KeyX, Some("pw"), false, false);
        f.key(KeyCode::Backspace, None, false, false);
        assert_eq!(f.fields[EMAIL].text, "a@b");
        assert_eq!(f.fields[PASSWORD].text, "p");
        // Shift+Tab back to the email; Home, Shift+End selects it all, and typing replaces it.
        f.key(KeyCode::Tab, None, false, true);
        f.key(KeyCode::Home, None, false, false);
        f.key(KeyCode::End, None, false, true);
        assert_eq!(f.fields[EMAIL].selected_text(), Some("a@b"));
        f.key(KeyCode::KeyC, Some("c"), false, false);
        assert_eq!(f.fields[EMAIL].text, "c");
        // Ctrl+A then Backspace empties it.
        f.key(KeyCode::KeyA, None, true, false);
        f.key(KeyCode::Backspace, None, false, false);
        assert_eq!(f.fields[EMAIL].text, "");
        assert_eq!(f.key(KeyCode::Enter, None, false, false), Some(Action::SignIn));
        // Escape from Register goes back to the main menu, whose Play asks to sign in first.
        f.show(Screen::Register);
        f.key(KeyCode::Escape, None, false, false);
        assert_eq!(f.screen, Screen::Home);
    }

    #[test]
    fn cards_fit_wide_and_tall_windows() {
        for (w, h) in [(1920.0, 1080.0), (800.0, 1000.0), (640.0, 480.0)] {
            let s = scale(w, h);
            let r = Front::card_rect(w, h, 380.0 * s, 420.0 * s);
            assert!(r[0] >= 0.0 && r[0] + r[2] <= w + 0.5, "{w}×{h}: {r:?}");
            assert!(r[1] >= 0.0, "{w}×{h}: {r:?}");
        }
    }
}
