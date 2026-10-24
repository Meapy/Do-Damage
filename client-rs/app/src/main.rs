//! betterSkillys client: the desktop executable and, built for wasm32, the browser version.

// Release builds on Windows open without a console window behind the game.
#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod audio;
mod changelog;
mod connected;
mod emotes;
mod game;
mod ground;
mod hud;
mod login;
mod forge;
mod market;
mod particles;
mod prof;
mod npc;
mod options;
mod redraw;
mod screens;
mod signs;
#[allow(dead_code)] // lookups the game screens will use
mod sky;
mod sprites;
mod stars;
mod text;
mod textedit;
mod theme;
#[cfg(not(target_arch = "wasm32"))]
mod update;
mod version;
mod view;
#[cfg(target_arch = "wasm32")]
mod web;

use bsk_assets::AssetSource;
use bsk_data::GameData;
use bsk_net::Pending;
use bsk_render::Renderer;
use bsk_session::{HelloParams, Join, NEXUS_GAME_ID};
use game::Game;
use sprites::Sprites;
use std::collections::HashSet;
use std::sync::Arc;
use web_time::Instant;
use winit::application::ApplicationHandler;
use winit::event::{ElementState, WindowEvent};
use winit::event_loop::{ActiveEventLoop, EventLoop, EventLoopProxy};
use winit::keyboard::{KeyCode, PhysicalKey};
use winit::window::{Window, WindowId};

/// The game's name. Not "betterSkillys": that's the AIR client's window, which tools must never touch.
const TITLE: &str = "Do Damage";

struct Loaded {
    renderer: Renderer,
    sprites: Sprites,
    data: GameData,
    redrawer: redraw::Redrawer,
    fonts: text::Fonts,
    text: text::Text,
    ground: ground::Ground,
}

enum UserEvent {
    Ready(Box<Loaded>),
    Failed(String),
}

/// Where we are: the screens before the game, or in it.
enum Phase {
    Menu,
    Playing(Game),
}

/// What we asked the account server.
enum Request {
    /// The character list; play the first living character when it comes (tests).
    CharList { auto_play: bool },
    Register,
    Delete,
    BuySlot,
    BuySkin(u16),
    /// The new password, kept for when it succeeds.
    ChangePassword(String),
    Legends,
    /// The server's latest deaths (the Legends screen's Recent Deaths).
    RecentDeaths,
    /// A dead character's fame summary.
    Fame,
    /// A new guest account.
    Guest,
}

/// What an NPC's window asked the account server.
enum NpcRequest {
    Board,
    SetBoard,
    Members,
    Potions,
    Leaderboard,
}

struct App {
    proxy: EventLoopProxy<UserEvent>,
    window: Option<Arc<Window>>,
    loaded: Option<Box<Loaded>>,
    phase: Phase,
    started: Instant,
    last_frame: i32,
    keys: HashSet<KeyCode>,
    fps_frames: u32,
    fps_since: Instant,
    /// The frame rate last measured, for the on-screen display.
    fps_shown: f32,
    /// When the last frame started (the max frame rate; desktop only).
    #[cfg_attr(target_arch = "wasm32", allow(dead_code))]
    frame_start: Instant,
    /// --bench: no frame cap or vsync, and timings in the log.
    bench: Option<Bench>,
    frame_start_bench: Instant,
    /// Wheel movement not yet a whole notch (the minimap's zoom).
    wheel: f32,
    last_title: String,
    /// `--shot <file> --shot-after <seconds>`: save one frame as PNG, then quit (desktop).
    shot: Option<(String, f32)>,
    quit: bool,
    /// Scripted key holds (`--hold`), and when the first one started.
    holds: Vec<(KeyCode, i32)>,
    holds_from: Option<i32>,
    last_scripted: Option<KeyCode>,
    walk_to: Option<(f64, f64)>,
    /// The points after it (`--walk-to x,y;x,y`).
    walk_more: Vec<(f64, f64)>,
    /// The mouse on the window, and whether the left button is down.
    mouse: (f64, f64),
    mouse_down: bool,
    /// The I key: keep firing toward the mouse.
    autofire: bool,
    /// `--aim <degrees>`: where scripted fire (`--hold f:<ms>`) aims, on the screen; `None`
    /// (`--aim auto`) aims at the nearest enemy.
    script_aim: Option<f64>,
    /// Keys pressed since the last frame (for one-shot actions).
    pressed: Vec<KeyCode>,
    /// The chat line being typed (Enter opens it), and lines to send.
    chat_input: Option<textedit::LineEdit>,
    chat_send: Vec<String>,
    /// Page Up: the chat's history, so many rows back (`TextBox.showMax_`).
    chat_scroll: Option<usize>,
    /// Tab's place in the list of people we've whispered with (`TextBox.currentTeller_`).
    teller: usize,
    /// Players' names in the chat as last drawn, and where (a click opens their menu).
    chat_names: Vec<(String, [f32; 4])>,
    /// `--say <text>` (desktop tests): lines still to send, one per world we join (see `say_arg`).
    say: Vec<String>,
    /// The join (`reconnects`) the last --say line went out in, and when.
    say_sent_in: Option<(u32, i32)>,
    /// Whether --say was given (walking waits for the world it may move us to).
    say_given: Option<()>,
    alt_held: bool,
    shift_held: bool,
    ctrl_held: bool,
    /// Right button presses since the last frame, and where (Ctrl+right-click breaks items down).
    right_clicks: Vec<(f64, f64)>,
    /// `--mouse` was given: ignore the real cursor and its buttons.
    mouse_fixed: bool,
    /// `--mouse-script` (tests): (ms after joining, down/up/move, where), oldest first.
    mouse_script: Vec<(i32, MouseStep, (f64, f64))>,
    map_colors: hud::MapColors,
    hud: hud::HudState,
    /// The pointer is hidden for the crosshair.
    cursor_hidden: bool,
    /// Left button presses (true) and releases since the last frame, and where.
    clicks: Vec<(bool, (f64, f64))>,
    /// The screens before the game.
    front: screens::Front,
    endpoint: Option<login::Endpoint>,
    /// The signed-in account's email and password.
    account: Option<(String, String)>,
    /// Signed in from --credentials (a test): the remembered login is left as it is.
    test_login: bool,
    /// Signed in with this device's guest login (`login::guest_login`).
    guest: bool,
    request: Option<(Request, Pending<bsk_net::http::Response>)>,
    /// Keys typed on the screens: (key, text).
    /// Keys for the screens: the key, the text it types, and whether Ctrl and Shift were held.
    front_keys: Vec<(KeyCode, Option<String>, bool, bool)>,
    /// The Escape menu in the game, and its buttons last frame.
    game_menu: bool,
    menu_buttons: Vec<(screens::MenuChoice, [f32; 4])>,
    /// The NPC window open in the game, what it asked the account server, and keys typed
    /// into it.
    npcs: npc::Npcs,
    npc_request: Option<(NpcRequest, Pending<bsk_net::http::Response>)>,
    /// What the signs over objects in the world say (the arena leaderboard's best run).
    signs: signs::Signs,
    npc_keys: Vec<(KeyCode, Option<String>)>,
    /// Sound effects and music, once we know the account server.
    audio: Option<audio::Audio>,
    /// The marketplace window.
    market: market::Market,
    /// The Divine Forge's window, and what it asked of the server on opening.
    forge: forge::Forge,
    forge_acts: Vec<forge::Act>,
    /// Key bindings, graphics and sound settings, and their window.
    settings: options::Settings,
    options_ui: options::OptionsUi,
    /// `--type <text>` (tests): typed into the first NPC prompt that opens, then Enter.
    type_text: Option<String>,
    /// `--npc <class>` (tests): open that NPC's window once in the game, wherever we stand.
    npc_test: Option<String>,
    /// `--effect <type>` (tests): show that effect on us every half second, and when it last did.
    effect_test: Option<(u8, i32)>,
    /// The Changelog window and the "updated" note.
    changelog: changelog::ChangelogUi,
    /// The "updated" note was looked at for this run.
    noted_version: bool,
    /// Updates (desktop): looking for them, the new version taking over, and what was said.
    #[cfg(not(target_arch = "wasm32"))]
    updater: Option<update::Updater>,
    #[cfg(not(target_arch = "wasm32"))]
    handover: Option<update::Handover>,
    #[cfg(not(target_arch = "wasm32"))]
    handover_failed: bool,
    #[cfg(not(target_arch = "wasm32"))]
    update_told: String,
    /// `--resume`: what the version before handed over, and the window to show once it's set up.
    #[cfg(not(target_arch = "wasm32"))]
    resume: Option<update::Resume>,
    #[cfg(not(target_arch = "wasm32"))]
    show_window: Option<update::WindowState>,
}

/// Open the window of the NPC we stand by. True for the character changer: back to the
/// character list.
#[allow(clippy::too_many_arguments)]
fn npc_open(kind: npc::NpcKind, game: &Game, npcs: &mut npc::Npcs, market: &mut market::Market, forge: &mut forge::Forge, forge_acts: &mut Vec<forge::Act>, list: Option<&bsk_net::account::CharList>, data: &bsk_data::GameData) -> bool {
    use npc::{NpcKind, Prompt, Window};
    let Some(me) = game.world.as_ref().and_then(|w| w.player()) else { return false };
    let window = match kind {
        NpcKind::CharacterChanger => return true,
        NpcKind::Market => {
            market.show(data);
            return false;
        }
        NpcKind::Forge => {
            forge_acts.extend(forge.show(data));
            return false;
        }
        NpcKind::Reskin => {
            let class = me.object_type;
            let mut skins = vec![0];
            if let Some(list) = list {
                skins.extend(screens::class_skins(data, class, list).iter().filter(|k| k.owned).map(|k| k.desc.object_type));
            }
            Window::Skins { class, skins }
        }
        NpcKind::NameChanger => Window::Prompt { prompt: Prompt::Name, input: String::new(), error: None, busy: false },
        NpcKind::GuildRegister => {
            if me.stat_str(npc::stat::GUILD_NAME).unwrap_or("").is_empty() {
                Window::Prompt { prompt: Prompt::Guild, input: String::new(), error: None, busy: false }
            } else {
                Window::Renounce
            }
        }
        NpcKind::GuildBoard => Window::Board { text: None, edit: None, error: None },
        NpcKind::GuildChronicle => Window::Members { list: None, page: 0, error: None },
        NpcKind::PotionStorage => Window::Potions { counts: None, error: None },
        NpcKind::ArenaLeaderboard => Window::Leaderboard { runs: None, error: None },
    };
    let fetch = matches!(window, Window::Board { .. } | Window::Members { .. } | Window::Potions { .. } | Window::Leaderboard { .. });
    npcs.open(window);
    if fetch {
        npcs.reload_at = Some(0);
    }
    false
}

/// Carry out what the player did in an NPC's window. Returns a board to save, if any.
fn npc_act(act: npc::Act, game: &mut Game, npcs: &mut npc::Npcs, now: i32) -> Option<String> {
    use npc::Act;
    let me = game.world.as_ref().and_then(|w| w.player()).and_then(|p| p.name()).unwrap_or("").to_owned();
    log::info!("npc: {act:?}");
    match act {
        Act::Reskin(skin) => {
            game.reskin(skin);
            npcs.close();
        }
        Act::ChooseName(name) => game.choose_name(&name),
        Act::CreateGuild(name) => game.create_guild(&name),
        Act::Renounce => game.guild_remove(&me),
        Act::SaveBoard(board) => return Some(board),
        Act::ChangeRank(name, rank) => {
            game.change_guild_rank(&name, rank);
            npcs.reload_at = Some(now + 600);
        }
        Act::Remove(name) => {
            game.guild_remove(&name);
            npcs.reload_at = Some(now + 600);
        }
        Act::Storage(kind, action) => {
            game.use_storage(kind, action);
            // PotionsPanel fetches the counts again a moment later.
            npcs.reload_at = Some(now + 800);
        }
    }
    None
}

#[cfg(not(target_arch = "wasm32"))]
fn shot_args() -> Option<(String, f32)> {
    let args: Vec<String> = std::env::args().collect();
    let value = |name: &str| args.iter().position(|a| a == name).and_then(|i| args.get(i + 1)).cloned();
    Some((value("--shot")?, value("--shot-after").and_then(|s| s.parse().ok()).unwrap_or(5.0)))
}

#[cfg(target_arch = "wasm32")]
fn shot_args() -> Option<(String, f32)> {
    None
}

/// A chat line being typed, starting from `text` (TextBox's limit: 128 characters).
fn chat_line(text: impl Into<String>) -> textedit::LineEdit {
    textedit::LineEdit::new(text, 128)
}

/// Frame timings for --bench.
struct Bench {
    since: Instant,
    frames: u32,
    build: f64,
    total: f64,
}

impl Default for Bench {
    fn default() -> Self {
        Bench { since: Instant::now(), frames: 0, build: 0.0, total: 0.0 }
    }
}

/// Carry out what the player did on the HUD (`ItemGridMediator`).
/// Returns a chat line to start typing (Whisper), if any.
fn apply_action(game: &mut Game, panel: &hud::Panel, action: hud::Action, now: i32, data: &bsk_data::GameData) -> Option<String> {
    use hud::{Action, Button, SlotKind};
    let slot_of = |s: &hud::Slot| (s.owner, s.index, s.item);
    // The first empty inventory slot (nextAvailableInventorySlot), as a swap target.
    let free = |game: &Game| -> Option<(i32, u8, i32)> {
        let me = game.world.as_ref()?.player()?;
        let backpack = me.stat_int(hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
        let last = if backpack { 20 } else { 12 };
        (4..last).find(|&i| me.stat_int(hud::slot_stat(i)).unwrap_or(-1) <= 0).map(|i| (me.id, i, -1))
    };
    match action {
        Action::Swap(a, b) => game.inv_swap(now, slot_of(&a), slot_of(&b)),
        Action::Use(s) => {
            if let SlotKind::Potion(_) = s.kind {
                game.use_potion(now, if s.index == hud::POTIONS[0].1 { 0 } else { 1 });
            } else if matches!(s.kind, SlotKind::Inventory(_) | SlotKind::Container) {
                game.use_item(now, s.owner, s.index, s.item, data);
            }
        }
        Action::EquipOrUse(s) => match s.kind {
            SlotKind::Potion(_) => game.use_potion(now, if s.index == hud::POTIONS[0].1 { 0 } else { 1 }),
            // From a bag: into the inventory, else use it (equipOrUseContainer).
            SlotKind::Container => match free(game) {
                Some(to) => game.inv_swap(now, slot_of(&s), to),
                None => {
                    game.use_item(now, s.owner, s.index, s.item, data);
                }
            },
            _ => {
                // getMatchingSlotIndex: the equipment slot of this item's type.
                let item_type = hud::item_slot_type(data, s.item);
                let target = panel
                    .slots
                    .iter()
                    .find(|e| matches!(e.kind, SlotKind::Equip(t) if t == item_type && t != 0) && !e.is(&s));
                match (target, s.kind) {
                    (Some(e), SlotKind::Inventory(_)) => game.inv_swap(now, slot_of(&s), slot_of(e)),
                    _ => {
                        game.use_item(now, s.owner, s.index, s.item, data);
                    }
                }
            }
        },
        // Onto the ground: into the bag we stand on if it has room, else a new bag.
        Action::Drop(s) => {
            if let SlotKind::Potion(_) = s.kind {
                return None;
            }
            let bag_slot = panel.slots.iter().find(|b| b.kind == SlotKind::Container && b.item <= 0);
            match bag_slot {
                Some(b) => game.inv_swap(now, slot_of(&s), slot_of(b)),
                None => game.inv_drop(s.owner, s.index, s.item),
            }
        }
        Action::Take(s) => {
            if let Some(to) = free(game) {
                game.inv_swap(now, slot_of(&s), to);
            }
        }
        Action::Stack(s) => game.add_to_stack(now, slot_of(&s)),
        Action::Button(Button::Nexus) => game.escape(),
        Action::Button(Button::Enter(id)) => game.use_portal(id),
        Action::Button(Button::Buy(id)) => game.buy(id),
        Action::Button(Button::Menu(opt, id)) => {
            let name = game.world.as_ref().and_then(|w| w.objects.get(&id)).and_then(|o| o.name()).unwrap_or("").to_owned();
            log::info!("player menu: {opt:?} {name}");
            match opt {
                hud::MenuOpt::Teleport => game.teleport(id, now),
                hud::MenuOpt::GuildInvite => game.guild_invite(&name),
                hud::MenuOpt::Lock => game.edit_list(0, true, id),
                hud::MenuOpt::Unlock => game.edit_list(0, false, id),
                hud::MenuOpt::Whisper => return Some(format!("/tell {name} ")),
                hud::MenuOpt::Trade => game.request_trade(&name),
                hud::MenuOpt::Party => game.party_invite(&name),
                hud::MenuOpt::Ignore => game.edit_list(1, true, id),
                hud::MenuOpt::Unignore => game.edit_list(1, false, id),
            }
        }
        Action::Button(Button::InviteYes) => game.answer_invite(true),
        Action::Button(Button::InviteNo) => game.answer_invite(false),
        Action::Button(Button::TradeSlot(i)) => game.trade_toggle(i, now),
        Action::Button(Button::TradeAccept) => game.trade_accept(now),
        Action::Button(Button::TradeCancel) => game.trade_cancel(),
        Action::Button(Button::Member(_)) => {}
        Action::Button(Button::Page(_) | Button::Npc(_) | Button::Zoom(_) | Button::CardTab(_) | Button::History | Button::HistoryBoss(_)) => {}
    }
    None
}

#[derive(Clone, Copy, Debug)]
#[cfg_attr(target_arch = "wasm32", allow(dead_code))] // scripted input is desktop-only
enum MouseStep {
    Down,
    Up,
    Move,
    /// A right-click.
    Right,
}

/// `--mouse-script "down@1500:x,y;move@1600:x,y;up@1700:x,y"` (desktop tests): left-button
/// steps at times (ms) after joining, for drags and clicks on the HUD; `right` right-clicks.
fn mouse_script_arg() -> Vec<(i32, MouseStep, (f64, f64))> {
    #[cfg(not(target_arch = "wasm32"))]
    {
        let args: Vec<String> = std::env::args().collect();
        let Some(script) = args.iter().position(|a| a == "--mouse-script").and_then(|i| args.get(i + 1)) else { return Vec::new() };
        let mut steps: Vec<_> = script
            .split(';')
            .filter_map(|step| {
                let (kind, rest) = step.trim().split_once('@')?;
                let (ms, pos) = rest.split_once(':')?;
                let (x, y) = pos.split_once(',')?;
                let kind = match kind {
                    "down" => MouseStep::Down,
                    "up" => MouseStep::Up,
                    "move" => MouseStep::Move,
                    "right" => MouseStep::Right,
                    _ => return None,
                };
                Some((ms.parse().ok()?, kind, (x.parse().ok()?, y.parse().ok()?)))
            })
            .collect();
        steps.sort_by_key(|s| s.0);
        steps
    }
    #[cfg(target_arch = "wasm32")]
    Vec::new()
}

/// `--mouse <x>,<y>` (desktop tests): where the mouse starts, in window pixels (for tooltips
/// and aiming in screenshots).
fn mouse_arg() -> Option<(f64, f64)> {
    #[cfg(not(target_arch = "wasm32"))]
    {
        let args: Vec<String> = std::env::args().collect();
        let v = args.iter().position(|a| a == "--mouse").and_then(|i| args.get(i + 1));
        if let Some((x, y)) = v.and_then(|v| v.split_once(',')) {
            return Some((x.parse().ok()?, y.parse().ok()?));
        }
    }
    None
}

/// The window's icon: the website's Do Damage crown (tools/draw_client_icon.py), built in. The small
/// one is the title bar's, the big one the taskbar's (on Windows they're set apart).
#[cfg(not(target_arch = "wasm32"))]
fn window_icon(big: bool) -> Option<winit::window::Icon> {
    let png: &[u8] = if big { include_bytes!("../icons/icon-256.png") } else { include_bytes!("../icons/icon-32.png") };
    let img = image::load_from_memory(png).ok()?.to_rgba8();
    let (w, h) = img.dimensions();
    winit::window::Icon::from_rgba(img.into_raw(), w, h).ok()
}

/// `--say <text>` (desktop tests): chat lines or commands to send once in the game. Lines are
/// split on `;;`; each goes out once we join another world or 3 s after the one before, so
/// `--say "/dungeon X;;/god"` opens a dungeon and then turns on god mode inside it.
fn say_arg() -> Vec<String> {
    text_arg("--say").map_or_else(Vec::new, |s| s.split(";;").map(|l| l.trim().to_string()).filter(|l| !l.is_empty()).collect())
}

/// A flag's text argument (desktop tests).
fn text_arg(name: &str) -> Option<String> {
    #[cfg(not(target_arch = "wasm32"))]
    {
        let args: Vec<String> = std::env::args().collect();
        args.iter().position(|a| a == name).and_then(|i| args.get(i + 1)).cloned()
    }
    #[cfg(target_arch = "wasm32")]
    {
        let _ = name;
        None
    }
}

/// `--walk-to <x>,<y>[;<x>,<y>...]`: walk straight to each world point in turn once in the
/// game (desktop tests).
fn walk_to_arg() -> Vec<(f64, f64)> {
    let point = |p: &str| -> Option<(f64, f64)> {
        let (x, y) = p.split_once(',')?;
        Some((x.trim().parse().ok()?, y.trim().parse().ok()?))
    };
    text_arg("--walk-to").map_or_else(Vec::new, |v| v.split(';').filter_map(point).collect())
}

/// `--say`'s lines, with `/autoaim` first for `--aim auto`: aiming at the nearest enemy is staff's
/// `/autoaim`, so it only works on a staff account once the server has said yes.
fn say_lines() -> Vec<String> {
    let mut lines = say_arg();
    if aim_arg().is_none() {
        lines.insert(0, "/autoaim".to_string());
    }
    lines
}

/// `--aim <degrees>` (desktop tests): the screen direction scripted fire aims at, or `auto` for
/// the nearest enemy (staff only: see `say_lines`).
fn aim_arg() -> Option<f64> {
    #[cfg(not(target_arch = "wasm32"))]
    {
        let args: Vec<String> = std::env::args().collect();
        match args.iter().position(|a| a == "--aim").and_then(|i| args.get(i + 1)) {
            Some(v) if v == "auto" => None,
            v => Some(v.and_then(|v| v.parse::<f64>().ok()).unwrap_or(0.0).to_radians()),
        }
    }
    #[cfg(target_arch = "wasm32")]
    Some(0.0)
}

/// For `--aim auto`: the world angle from the player to the nearest live enemy, if any.
fn nearest_enemy_angle(world: &bsk_world::World, data: &bsk_data::GameData) -> Option<f64> {
    let me = world.player()?;
    world
        .objects
        .values()
        .filter(|o| !o.dead && data.objects.get(&o.object_type).is_some_and(|d| d.enemy))
        .map(|o| (o.x - me.x, o.y - me.y))
        .min_by(|a, b| a.0.hypot(a.1).total_cmp(&b.0.hypot(b.1)))
        .map(|(dx, dy)| dy.atan2(dx))
}

/// `--hold <key>:<ms>` (repeatable): keys to hold one after another once in the game, for
/// testing movement without touching the window (desktop).
fn hold_args() -> Vec<(KeyCode, i32)> {
    #[cfg(not(target_arch = "wasm32"))]
    {
        let args: Vec<String> = std::env::args().collect();
        args.windows(2)
            .filter(|w| w[0] == "--hold")
            .filter_map(|w| {
                let (key, ms) = w[1].split_once(':')?;
                let code = match key {
                    "w" => KeyCode::KeyW,
                    "a" => KeyCode::KeyA,
                    "s" => KeyCode::KeyS,
                    "d" => KeyCode::KeyD,
                    "q" => KeyCode::KeyQ,
                    "e" => KeyCode::KeyE,
                    "r" => KeyCode::KeyR,
                    "f" => KeyCode::F23, // fire toward --aim
                    "0" => KeyCode::Digit0,
                    "1" => KeyCode::Digit1,
                    "2" => KeyCode::Digit2,
                    "3" => KeyCode::Digit3,
                    "4" => KeyCode::Digit4,
                    "5" => KeyCode::Digit5,
                    "6" => KeyCode::Digit6,
                    "7" => KeyCode::Digit7,
                    "8" => KeyCode::Digit8,
                    "space" => KeyCode::Space,
                    "hp" => KeyCode::KeyF,
                    "mp" => KeyCode::KeyV,
                    "i" => KeyCode::F22, // autofire on or off, whatever key it's bound to
                    "o" => KeyCode::KeyO,
                    "z" => KeyCode::KeyZ,
                    "-" => KeyCode::F24, // a pause: holds nothing
                    _ => return None,
                };
                Some((code, ms.parse().ok()?))
            })
            .collect()
    }
    #[cfg(target_arch = "wasm32")]
    Vec::new()
}

async fn load(window: Arc<Window>) -> Result<Box<Loaded>, String> {
    let size = window.inner_size();
    let mut renderer = Renderer::new(window, size.width, size.height).await?;
    let source = AssetSource::locate().map_err(|e| e.to_string())?;
    log::info!("assets: {}", source.describe());
    let manifest = source.manifest().await.map_err(|e| e.to_string())?;
    let t = Instant::now();
    // The sprites, the game data and the fonts download together (in a browser they're 500 files).
    let (sprites, data, (regular, bold)) = futures::join!(
        Sprites::load(&source, &manifest, &mut renderer),
        GameData::load(&source, &manifest),
        async { futures::join!(source.load(&manifest.fonts.regular), source.load(&manifest.fonts.bold)) }
    );
    let (sprites, data) = (sprites?, data?);
    log::info!(
        "loaded {} sheets ({} atlas pages), {} grounds, {} objects in {} ms",
        manifest.sheet_files().len(),
        renderer.atlas_pages(),
        data.grounds.len(),
        data.objects.len(),
        t.elapsed().as_millis()
    );
    let fonts = text::Fonts::new(regular.map_err(|e| e.to_string())?, bold.map_err(|e| e.to_string())?)?;
    Ok(Box::new(Loaded {
        renderer,
        sprites,
        data,
        redrawer: Default::default(),
        fonts,
        text: Default::default(),
        ground: Default::default(),
    }))
}

impl App {
    fn now(&self) -> i32 {
        self.started.elapsed().as_millis() as i32
    }

    /// After loading: sign in with what we were given or remember, else show the login screen.
    fn start(&mut self) {
        self.phase = Phase::Menu;
        match login::startup() {
            Ok(start) => {
                self.front.endpoint_label = format!("Account server: {}", start.endpoint.account);
                self.audio = Some(audio::Audio::new(&start.endpoint.account));
                #[cfg(not(target_arch = "wasm32"))]
                {
                    self.updater = update::Updater::start(&start.endpoint.account);
                }
                self.endpoint = Some(start.endpoint);
                #[cfg(not(target_arch = "wasm32"))]
                if let Some(resume) = self.resume.take() {
                    self.resume_from(resume);
                    return;
                }
                if let Some((email, password)) = start.login {
                    self.front.set_field(screens::EMAIL, email);
                    self.front.set_field(screens::PASSWORD, password);
                    // Desktop tests sign in and go straight into the game, leaving the player's
                    // remembered login alone.
                    self.front.remember = false;
                    self.test_login = true;
                    let stay = cfg!(not(target_arch = "wasm32")) && std::env::args().any(|a| a == "--stay");
                    self.sign_in(cfg!(not(target_arch = "wasm32")) && !stay);
                } else if let Some((email, password)) = login::remembered() {
                    self.front.set_field(screens::EMAIL, email);
                    self.front.set_field(screens::PASSWORD, password);
                    self.front.show(screens::Screen::Home);
                    self.sign_in_quietly();
                } else if let Some((guid, password)) = login::guest_login() {
                    self.front.show(screens::Screen::Home);
                    self.sign_in_as_guest(guid, password, false);
                } else {
                    self.front.show(screens::Screen::Home);
                }
            }
            Err(e) => {
                self.front.message = Some((e, true));
                self.front.show(screens::Screen::Home);
            }
        }
    }

    fn sign_in(&mut self, auto_play: bool) {
        let (email, password) = (self.front.fields[screens::EMAIL].text.trim().to_owned(), self.front.fields[screens::PASSWORD].text.clone());
        if let Some(problem) = login::check_sign_in(&email, &password) {
            self.front.message = Some((problem.into(), true));
            self.front.show(screens::Screen::Login);
            return;
        }
        let Some(endpoint) = &self.endpoint else { return };
        self.request = Some((Request::CharList { auto_play }, endpoint.char_list(&email, &password)));
        self.account = Some((email, password));
        self.guest = false;
        self.front.busy = "Signing in…".into();
        self.front.message = None;
        self.front.show(screens::Screen::Busy);
    }

    /// Play without an account: this device's guest login, or a new one from the server.
    fn play_as_guest(&mut self) {
        if let Some((guid, password)) = login::guest_login() {
            self.sign_in_as_guest(guid, password, true);
            return;
        }
        let Some(endpoint) = &self.endpoint else { return };
        self.request = Some((Request::Guest, endpoint.guest()));
        self.front.busy = "Making you a guest account…".into();
        self.front.message = None;
        self.front.show(screens::Screen::Busy);
    }

    /// Sign in with a guest login: on to the characters (`busy`), or quietly behind the menu.
    fn sign_in_as_guest(&mut self, guid: String, password: String, busy: bool) {
        let Some(endpoint) = &self.endpoint else { return };
        self.request = Some((Request::CharList { auto_play: false }, endpoint.char_list(&guid, &password)));
        self.account = Some((guid, password));
        self.guest = true;
        self.front.message = None;
        if busy {
            self.front.busy = "Entering as a guest…".into();
            self.front.show(screens::Screen::Busy);
        } else {
            self.front.signing_in = true;
        }
    }

    /// Check the remembered login while the main menu shows (Play then goes straight to the
    /// characters).
    fn sign_in_quietly(&mut self) {
        let (email, password) = (self.front.fields[screens::EMAIL].text.trim().to_owned(), self.front.fields[screens::PASSWORD].text.clone());
        let Some(endpoint) = &self.endpoint else { return };
        if login::check_sign_in(&email, &password).is_some() {
            return;
        }
        self.request = Some((Request::CharList { auto_play: false }, endpoint.char_list(&email, &password)));
        self.account = Some((email, password));
        self.guest = false;
        self.front.signing_in = true;
    }

    /// Carry on where the version before left off (`--resume`): signed in, and through the
    /// portal it was taking.
    #[cfg(not(target_arch = "wasm32"))]
    fn resume_from(&mut self, r: update::Resume) {
        log::info!("carrying on from version {}", r.from_version);
        let now = self.now();
        if !r.email.is_empty() {
            self.front.set_field(screens::EMAIL, r.email.clone());
            self.front.set_field(screens::PASSWORD, r.password.clone());
            self.account = Some((r.email.clone(), r.password.clone()));
            // The remembered login is the old version's business: left as it is.
            self.test_login = true;
        }
        self.show_window = Some(r.window.clone());
        let chat: Vec<hud::ChatLine> = r
            .chat
            .iter()
            .map(|c| hud::ChatLine {
                name: c.name.clone(),
                recipient: c.recipient.clone(),
                text: c.text.clone(),
                name_color: c.name_color,
                text_color: c.text_color,
                stars: c.stars,
                at: now - c.age,
            })
            .collect();
        match (r.game, &self.endpoint, &self.account) {
            (Some(g), Some(endpoint), Some((email, password))) => {
                let hello = HelloParams {
                    build_version: bsk_net::login::BUILD_VERSION.into(),
                    game_id: g.game_id,
                    guid: bsk_net::login::encrypt(email),
                    password: bsk_net::login::encrypt(password),
                    key_time: g.key_time,
                    key: g.key,
                };
                let mut game = Game::start(endpoint.target(&g.host, g.port), hello, Join::Load { char_id: g.char_id });
                game.fallback_to_nexus = true;
                game.status = format!("Entering {}…", g.name);
                game.chat = chat;
                if let Some(a) = self.settings.camera_angle {
                    game.camera_angle = a;
                }
                self.phase = Phase::Playing(game);
                self.front.show(screens::Screen::Chars);
                // The character list behind the game, for the screens and the NPCs.
                self.refresh();
            }
            (_, _, Some(_)) => self.sign_in(false),
            _ => self.front.show(screens::Screen::Home),
        }
    }

    /// `--resume`: put the window where the old one was, draw into it, then show it and tell the
    /// old version to go.
    #[cfg(not(target_arch = "wasm32"))]
    fn show_resumed_window(&mut self) {
        let (Some(ws), Some(window)) = (self.show_window.take(), self.window.clone()) else { return };
        window.set_outer_position(winit::dpi::PhysicalPosition::new(ws.x, ws.y));
        if ws.width > 0 && ws.height > 0 {
            let _ = window.request_inner_size(winit::dpi::PhysicalSize::new(ws.width, ws.height));
        }
        if ws.maximized {
            window.set_maximized(true);
        }
        if ws.fullscreen {
            window.set_fullscreen(Some(winit::window::Fullscreen::Borderless(None)));
        }
        let size = window.inner_size();
        if let Some(loaded) = &mut self.loaded {
            loaded.renderer.resize(size.width, size.height);
        }
        self.frame();
        window.set_visible(true);
        window.focus_window();
        update::announce_shown();
    }

    /// Updates (desktop): say when one is ready, switch to it at a good moment (the next
    /// portal, or now on the character list), and see the switch through.
    #[cfg(not(target_arch = "wasm32"))]
    fn advance_update(&mut self) {
        let now = self.now();
        let Some(updater) = &self.updater else { return };
        if let update::Status::Ready { version } = updater.status() {
            if self.update_told != version {
                self.update_told = version.clone();
                if let Phase::Playing(game) = &mut self.phase {
                    game.add_chat("", "", &format!("Version {version} is ready: it starts when you next take a portal."), 0, 0, -1, now);
                }
            }
            if self.handover.is_none() && !self.handover_failed {
                match &mut self.phase {
                    Phase::Playing(game) => game.hold_reconnect = true,
                    Phase::Menu => {
                        if self.front.at_rest() && self.request.is_none() && !self.changelog.open {
                            self.start_handover(None);
                        }
                    }
                }
            }
        }
        // A portal kept for the new version.
        if self.handover.is_none() {
            let held = match &self.phase {
                Phase::Playing(game) => game.held.as_ref().map(|r| {
                    let (host, port) = game.reconnect_target(r);
                    update::ResumeGame { host, port, game_id: r.game_id, key_time: r.key_time, key: r.key.0.clone(), char_id: game.char_id, name: r.name.clone() }
                }),
                Phase::Menu => None,
            };
            if held.is_some() {
                self.start_handover(held);
            }
        }
        let Some(handover) = &mut self.handover else { return };
        match handover.poll() {
            update::HandoverState::Waiting => {}
            update::HandoverState::Shown => {
                log::info!("updates: the new version took over");
                self.quit = true;
            }
            update::HandoverState::Failed(why) => {
                log::warn!("updates: {why}");
                self.handover = None;
                self.handover_failed = true;
                if let Phase::Playing(game) = &mut self.phase {
                    game.hold_reconnect = false;
                    if let Some(r) = game.held.take() {
                        game.fallback_to_nexus = true;
                        game.reconnect(&r);
                    }
                    game.add_chat("*Error*", "", "The new version couldn't start; it will the next time you open the game.", 0, 0, -1, now);
                }
            }
        }
    }

    /// Start the new version, handing it the login, the window, the chat and (in the game) the
    /// portal to go through.
    #[cfg(not(target_arch = "wasm32"))]
    fn start_handover(&mut self, game: Option<update::ResumeGame>) {
        let (Some(updater), Some(window)) = (&self.updater, &self.window) else { return };
        let now = self.now();
        let position = window.outer_position().unwrap_or_default();
        let size = window.inner_size();
        let chat = match &self.phase {
            Phase::Playing(g) => g
                .chat
                .iter()
                .map(|c| update::ResumeChat {
                    name: c.name.clone(),
                    recipient: c.recipient.clone(),
                    text: c.text.clone(),
                    name_color: c.name_color,
                    text_color: c.text_color,
                    stars: c.stars,
                    age: now - c.at,
                })
                .collect(),
            Phase::Menu => Vec::new(),
        };
        let (email, password) = self.account.clone().unwrap_or_default();
        let resume = update::Resume {
            from_version: version::VERSION.into(),
            email,
            password,
            game,
            window: update::WindowState {
                x: position.x,
                y: position.y,
                width: size.width,
                height: size.height,
                maximized: window.is_maximized(),
                fullscreen: window.fullscreen().is_some(),
            },
            chat,
        };
        match updater.hand_over(&resume) {
            Ok(h) => self.handover = Some(h),
            Err(e) => {
                log::warn!("updates: {e}");
                self.handover_failed = true;
                if let Phase::Playing(game) = &mut self.phase {
                    game.hold_reconnect = false;
                    if let Some(r) = game.held.take() {
                        game.reconnect(&r);
                    }
                }
            }
        }
    }

    /// The changelog to show (the newest release's when one has been seen, else this build's),
    /// and a newer version that's ready to take over.
    fn changelog_entries(&self) -> (Vec<bsk_update::Entry>, Option<String>) {
        #[cfg(not(target_arch = "wasm32"))]
        if let Some(updater) = &self.updater {
            let coming = match updater.status() {
                update::Status::Ready { version } => Some(version),
                _ => None,
            };
            if let Some(latest) = updater.latest().filter(|l| !l.changelog.is_empty()) {
                return (latest.changelog, coming);
            }
            return (version::changelog(), coming);
        }
        (version::changelog(), None)
    }

    /// The version line on the screens, with any update under way.
    fn version_line(&self) -> String {
        let label = version::label();
        #[cfg(not(target_arch = "wasm32"))]
        if let Some(updater) = &self.updater {
            return match updater.status() {
                update::Status::Downloading { version, done, total } => {
                    format!("{label}  ·  downloading {version} ({}%)", done * 100 / total.max(1))
                }
                update::Status::Ready { version } => format!("{label}  ·  {version} is ready"),
                _ => format!("{label}  ·  Changelog"),
            };
        }
        format!("{label}  ·  Changelog")
    }

    /// Once a version, when the screens or the game first show: a note that it's new.
    fn note_version(&mut self, now: i32) {
        if self.noted_version {
            return;
        }
        let ready = match &self.phase {
            Phase::Menu => matches!(self.front.screen, screens::Screen::Chars | screens::Screen::Home),
            Phase::Playing(game) => game.world.as_ref().is_some_and(|w| w.player().is_some()),
        };
        if !ready {
            return;
        }
        self.noted_version = true;
        if self.settings.seen_version != version::VERSION {
            let first = self.settings.seen_version.is_empty();
            self.settings.seen_version = version::VERSION.into();
            self.settings.save();
            let text = if first { format!("You're playing version {}", version::VERSION) } else { format!("Updated to version {}", version::VERSION) };
            self.changelog.note(text, now);
        }
    }

    /// Ask for the character list again (after a game, or a deletion).
    fn refresh(&mut self) {
        if let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) {
            self.request = Some((Request::CharList { auto_play: false }, endpoint.char_list(email, password)));
        }
    }

    fn front_action(&mut self, action: screens::Action) {
        use screens::{Action, Screen};
        match action {
            Action::SignIn => self.sign_in(false),
            Action::Register => {
                let f = &self.front.fields;
                let (name, email, password) = (f[screens::NAME].text.trim().to_owned(), f[screens::EMAIL].text.trim().to_owned(), f[screens::PASSWORD].text.clone());
                if let Some(p) = login::check_register(&name, &email, &password, &f[screens::CONFIRM].text) {
                    self.front.message = Some((p.into(), true));
                    return;
                }
                let Some(endpoint) = &self.endpoint else { return };
                let guest = self.account.as_ref().filter(|_| self.guest).map(|(g, p)| (g.as_str(), p.as_str()));
                self.request = Some((Request::Register, endpoint.register(&email, &password, &name, guest)));
                self.front.busy = if guest.is_some() { "Saving your progress…" } else { "Creating your account…" }.into();
                self.front.message = None;
                self.front.show(Screen::Busy);
            }
            Action::Play(id) => self.play(Join::Load { char_id: id }),
            Action::Create(class, skin) => self.play(Join::Create { class_type: class, skin_type: skin }),
            Action::BuySlot => {
                let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) else { return };
                self.request = Some((Request::BuySlot, endpoint.buy_slot(email, password)));
            }
            Action::BuySkin(skin) => {
                let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) else { return };
                self.request = Some((Request::BuySkin(skin), endpoint.buy_skin(email, password, skin)));
            }
            Action::ChangePassword => {
                let f = &self.front.fields;
                let (current, new) = (f[screens::PASSWORD].text.clone(), f[screens::NEW_PASSWORD].text.clone());
                let problem = if current.is_empty() || new.is_empty() {
                    Some("Fill in every field.")
                } else if new != f[screens::NEW_CONFIRM].text {
                    Some("The new passwords don't match.")
                } else if new.chars().count() < login::MIN_PASSWORD {
                    Some("Passwords need at least 6 characters.")
                } else {
                    None
                };
                if let Some(p) = problem {
                    self.front.message = Some((p.into(), true));
                    return;
                }
                let (Some(endpoint), Some((email, _))) = (&self.endpoint, &self.account) else { return };
                self.request = Some((Request::ChangePassword(new.clone()), endpoint.change_password(email, &current, &new)));
                self.front.message = None;
            }
            Action::ViewDeath(char_id) => {
                let account_id = self.front.list.as_ref().map(|l| l.account_id);
                if let (Some(endpoint), Some(account_id)) = (&self.endpoint, account_id) {
                    self.request = Some((Request::Fame, endpoint.char_fame(account_id, char_id)));
                    self.front.death_from_graveyard = true;
                    self.front.death_from_legends = false;
                    self.front.busy = "Reading the gravestone…".into();
                    self.front.show(screens::Screen::Busy);
                }
            }
            Action::ViewDeathOf(account_id, char_id) => {
                if let Some(endpoint) = &self.endpoint {
                    self.request = Some((Request::Fame, endpoint.char_fame(account_id, char_id)));
                    self.front.death_from_graveyard = false;
                    self.front.death_from_legends = true;
                    self.front.busy = "Reading the gravestone…".into();
                    self.front.show(screens::Screen::Busy);
                }
            }
            Action::Legends(span) => {
                if let Some(endpoint) = &self.endpoint {
                    self.front.legends = None;
                    self.front.recent = None;
                    self.request = Some(if span == screens::TIMESPANS[screens::RECENT].0 {
                        (Request::RecentDeaths, endpoint.recent_deaths())
                    } else {
                        (Request::Legends, endpoint.legends(span))
                    });
                }
            }
            Action::Delete(id) => {
                let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) else { return };
                self.request = Some((Request::Delete, endpoint.delete_char(email, password, id)));
            }
            Action::Logout => self.logout(),
            Action::Changelog => self.changelog.show(),
            Action::Options => self.options_ui.show(),
            Action::PlayAsGuest => self.play_as_guest(),
            Action::Quit => self.quit = true,
        }
    }

    fn logout(&mut self) {
        self.account = None;
        // A guest's login stays on this device: Play brings them back.
        self.guest = false;
        self.front.guest = false;
        self.front.list = None;
        self.front.set_field(screens::PASSWORD, String::new());
        self.front.message = None;
        login::remember(None);
        self.front.show(screens::Screen::Home);
    }

    /// Into the game with this character (or a new one), on the chosen server.
    fn play(&mut self, join: Join) {
        let (Some(endpoint), Some((email, password)), Some(list)) = (&self.endpoint, &self.account, &self.front.list) else { return };
        let Some(server) = list.servers.get(self.front.server).or(list.servers.first()) else {
            self.front.message = Some(("The account server lists no world servers.".into(), true));
            return;
        };
        log::info!("{join:?} on {} ({}:{})", server.name, server.host, server.port);
        let hello = HelloParams {
            build_version: bsk_net::login::BUILD_VERSION.into(),
            game_id: NEXUS_GAME_ID,
            guid: bsk_net::login::encrypt(email),
            password: bsk_net::login::encrypt(password),
            key_time: -1,
            key: vec![],
        };
        let mut game = Game::start(endpoint.target(&server.host, server.port), hello, join);
        // The camera as it was left (Parameters' cameraAngle).
        if let Some(a) = self.settings.camera_angle {
            game.camera_angle = a;
        }
        self.phase = Phase::Playing(game);
        self.hud = Default::default();
        self.chat_input = None;
        self.game_menu = false;
        self.npcs.close();
        self.npc_request = None;
        self.keys.clear();
    }

    /// Leave the game for the character list, saying why.
    fn leave_game(&mut self, why: Option<String>) {
        let mut death = None;
        if let Phase::Playing(game) = &mut self.phase {
            if game.died {
                death = Some(game.char_id);
            }
            game.close();
        }
        self.phase = Phase::Menu;
        self.game_menu = false;
        self.npcs.close();
        self.npc_request = None;
        self.mouse_down = false;
        self.front.message = why.map(|w| (w, true));
        self.front.show(screens::Screen::Chars);
        // After a death: the fame summary first (FameView), then the list.
        let account_id = self.front.list.as_ref().map(|l| l.account_id);
        self.front.death_from_graveyard = false;
        self.front.death_from_legends = false;
        if let (Some(char_id), Some(account_id), Some(endpoint)) = (death, account_id, &self.endpoint) {
            self.request = Some((Request::Fame, endpoint.char_fame(account_id, char_id)));
            self.front.busy = "Counting your fame…".into();
            self.front.message = None;
            self.front.show(screens::Screen::Busy);
        } else {
            self.refresh();
        }
    }

    /// The NPC window's side of things: the server's answers to names and guilds, fetching
    /// what the window shows, and what the account server sent back.
    fn advance_npcs(&mut self) {
        let now = self.now();
        let Phase::Playing(game) = &mut self.phase else { return };
        if let Some((ok, why)) = game.name_result.take() {
            self.npcs.answer(npc::Prompt::Name, ok, &why);
        }
        if let Some((ok, why)) = game.guild_result.take() {
            self.npcs.answer(npc::Prompt::Guild, ok, &why);
        }
        let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) else { return };
        // The signs over objects: fetched while one is near, and taken when they come.
        if self.signs.want_arena(now) {
            self.signs.request = Some(endpoint.arena_leaderboard(1));
        }
        if let Some(response) = self.signs.request.as_ref().and_then(|p| p.take()) {
            self.signs.request = None;
            if let Ok(runs) = login::reached(response).and_then(|x| bsk_net::account::parse_arena_runs(&x)) {
                self.signs.set_arena(&runs);
            }
        }
        if self.npcs.reload_at.is_some_and(|t| now >= t) && self.npc_request.is_none() {
            self.npcs.reload_at = None;
            self.npc_request = match self.npcs.window {
                Some(npc::Window::Board { .. }) => Some((NpcRequest::Board, endpoint.guild_board(email, password))),
                Some(npc::Window::Members { .. }) => Some((NpcRequest::Members, endpoint.guild_members(email, password))),
                Some(npc::Window::Potions { .. }) => Some((NpcRequest::Potions, endpoint.potions(email, password))),
                Some(npc::Window::Leaderboard { .. }) => Some((NpcRequest::Leaderboard, endpoint.arena_leaderboard(npc::LEADERBOARD_RUNS))),
                _ => None,
            };
        }
        let Some((_, pending)) = &self.npc_request else { return };
        let Some(response) = pending.take().map(login::reached) else { return };
        let Some((kind, _)) = self.npc_request.take() else { return };
        use bsk_net::account as acc;
        match (kind, &mut self.npcs.window) {
            (NpcRequest::Board, Some(npc::Window::Board { text, error, .. })) => match response.and_then(|b| acc::text_answer(&b)) {
                Ok(b) => *text = Some(b),
                Err(e) => *error = Some(e),
            },
            (NpcRequest::SetBoard, Some(npc::Window::Board { error, .. })) => {
                if let Err(e) = response.and_then(|b| acc::text_answer(&b)) {
                    *error = Some(e);
                }
            }
            (NpcRequest::Members, Some(npc::Window::Members { list, error, page })) => match response.and_then(|x| acc::parse_guild(&x)) {
                Ok(l) => {
                    *page = (*page).min(l.1.len().saturating_sub(1) / 10);
                    (*list, *error) = (Some(l), None);
                }
                Err(e) => *error = Some(e),
            },
            (NpcRequest::Potions, Some(npc::Window::Potions { counts, error })) => match response.and_then(|x| acc::parse_potions(&x)) {
                Ok(c) => (*counts, *error) = (Some(c), None),
                Err(e) => *error = Some(e),
            },
            (NpcRequest::Leaderboard, Some(npc::Window::Leaderboard { runs, error })) => match response.and_then(|x| acc::parse_arena_runs(&x)) {
                Ok(r) => {
                    self.signs.set_arena(&r);
                    (*runs, *error) = (Some(r), None);
                }
                Err(e) => *error = Some(e),
            },
            _ => {}
        }
    }

    /// Move through the sign-in steps as their answers arrive.
    fn advance(&mut self) {
        self.advance_npcs();
        // A game that ended (death, disconnect) goes back to the character list.
        if let Phase::Playing(game) = &self.phase {
            if game.ended && self.shot.is_none() {
                let why = game.status.clone();
                self.leave_game(Some(why));
            }
        }
        let Some((_, pending)) = &self.request else { return };
        let Some(response) = pending.take() else { return };
        let Some((kind, _)) = self.request.take() else { return };
        use screens::Screen;
        self.front.signing_in = false;
        match kind {
            Request::CharList { auto_play } => match login::parse_char_list(response) {
                // Older account servers answered an unknown email with a made-up guest list
                // (account 0) that couldn't play: that's no sign-in.
                Ok(list) if list.account_id <= 0 && self.front.list.is_none() => {
                    self.account = None;
                    self.front.message = Some(("No account uses that email.".into(), true));
                    if self.front.screen != Screen::Home {
                        self.front.show(Screen::Login);
                    }
                }
                Ok(list) => {
                    if let Some((email, password)) = &self.account {
                        if !self.test_login && !self.guest {
                            login::remember(self.front.remember.then_some((email.as_str(), password.as_str())));
                        }
                    }
                    // The server chosen last time (preferredServer), else the one showing.
                    self.front.server = list
                        .servers
                        .iter()
                        .position(|s| s.name == self.settings.preferred_server)
                        .unwrap_or(self.front.server)
                        .min(list.servers.len().saturating_sub(1));
                    let first = list.chars.iter().find(|c| !c.dead).map(|c| c.id);
                    self.front.list = Some(list);
                    self.front.guest = self.guest;
                    if matches!(self.phase, Phase::Menu) {
                        if self.front.screen == Screen::Busy || self.front.screen == Screen::Login {
                            self.front.show(Screen::Chars);
                        }
                        match (auto_play, first) {
                            (true, Some(id)) => self.play(Join::Load { char_id: id }),
                            // A test account with no one living: a new Wizard.
                            (true, None) => self.play(Join::Create { class_type: 0x030e, skin_type: 0 }),
                            _ => {}
                        }
                    }
                }
                Err(e) => {
                    // A guest login the server no longer knows (its data was reset): forget it,
                    // and Play makes a new one.
                    if self.guest && self.front.list.is_none() {
                        login::keep_guest(None);
                        self.guest = false;
                    }
                    if self.front.list.is_none() {
                        self.account = None;
                        // A remembered login that failed behind the main menu says so there.
                        if self.front.screen != Screen::Home {
                            self.front.show(Screen::Login);
                        }
                    }
                    self.front.message = Some((e, true));
                }
            },
            Request::Register => match login::parse_success(response) {
                Ok(()) => {
                    // A guest's progress is the new account's now.
                    if self.guest {
                        login::keep_guest(None);
                    }
                    self.sign_in(false);
                }
                Err(e) => {
                    self.front.message = Some((e, true));
                    self.front.show(Screen::Register);
                }
            },
            Request::Delete => match login::parse_success(response) {
                Ok(()) => self.refresh(),
                Err(e) => self.front.message = Some((e, true)),
            },
            Request::BuySlot => match login::parse_success(response) {
                Ok(()) => {
                    self.front.message = Some(("You have a new character slot.".into(), false));
                    self.refresh();
                }
                Err(e) => self.front.message = Some((e, true)),
            },
            Request::BuySkin(skin) => match login::parse_success(response) {
                Ok(()) => {
                    self.front.skin = skin;
                    self.front.message = Some(("Skin bought.".into(), false));
                    self.refresh();
                }
                Err(e) => self.front.message = Some((e, true)),
            },
            Request::ChangePassword(new) => match login::parse_success(response) {
                Ok(()) => {
                    if let Some((email, password)) = &mut self.account {
                        *password = new.clone();
                        if login::remembered().is_some() {
                            login::remember(Some((email, &new)));
                        }
                    }
                    self.front.set_field(screens::PASSWORD, new);
                    self.front.message = Some(("Password changed.".into(), false));
                    self.front.show(Screen::Chars);
                }
                Err(e) => self.front.message = Some((e, true)),
            },
            Request::Legends => match login::reached(response).and_then(|xml| bsk_net::account::parse_fame_list(&xml)) {
                Ok(list) => self.front.legends = Some(list),
                Err(e) => {
                    self.front.legends = Some(Vec::new());
                    self.front.message = Some((e, true));
                }
            },
            Request::RecentDeaths => match login::reached(response).and_then(|xml| bsk_net::account::parse_recent_deaths(&xml)) {
                Ok(list) => self.front.recent = Some(list),
                Err(e) => {
                    self.front.recent = Some(Vec::new());
                    self.front.message = Some((e, true));
                }
            },
            Request::Guest => match login::parse_guest(response) {
                Ok((guid, password)) => {
                    login::keep_guest(Some((&guid, &password)));
                    self.sign_in_as_guest(guid, password, true);
                }
                Err(e) => {
                    self.front.message = Some((e, true));
                    self.front.show(Screen::Home);
                }
            },
            Request::Fame => {
                match login::reached(response).and_then(|xml| bsk_net::account::parse_char_fame(&xml)) {
                    Ok(summary) => {
                        self.front.death = Some(summary);
                        self.front.show(Screen::Death);
                        let now = self.now();
                        if let Some(a) = &mut self.audio {
                            a.play("death_screen", 1.0, false, now);
                        }
                    }
                    Err(e) => {
                        self.front.message = Some((format!("You died. ({e})"), true));
                        self.front.show(Screen::Chars);
                    }
                }
                // The list behind it, for afterwards.
                self.refresh();
            }
        }
    }

    /// Play what the game and the screens asked for; follow the map's music.
    fn update_audio(&mut self, now: i32) {
        let Some(audio) = &mut self.audio else { return };
        audio.settings = self.settings.sound;
        // Scripted input (tests) counts as the player doing something.
        if self.holds_from.is_some() {
            audio.wake();
        }
        if std::mem::take(&mut self.front.clicked) {
            audio.play("button_click", 1.0, false, now);
        }
        match &mut self.phase {
            Phase::Playing(game) => {
                for (name, volume, shot) in game.sounds.drain(..) {
                    audio.play(&name, volume, shot, now);
                }
                if let Some(m) = game.music.take() {
                    audio.music(&m);
                }
            }
            Phase::Menu => audio.music(""),
        }
        audio.update(now);
    }

    /// `TextBox.insertTellPrefix`: "/tell " and the next person back (Shift: forward).
    fn tell_prefix(&mut self) -> String {
        let Phase::Playing(game) = &self.phase else { return "/tell ".into() };
        let n = game.tellers.len();
        if n == 0 {
            return "/tell ".into();
        }
        let t = self.teller as i64 + if self.shift_held { 1 } else { -1 };
        self.teller = t.clamp(0, n as i64 - 1) as usize;
        format!("/tell {} ", game.tellers[self.teller])
    }

    /// Page Up: show the whole recent chat, then older pages; Page Down comes back.
    fn scroll_chat(&mut self, up: bool) {
        const PAGE: usize = 7;
        self.chat_scroll = match (self.chat_scroll, up) {
            (None, true) => Some(0),
            (Some(n), true) => Some(n + PAGE),
            (Some(0), false) | (None, false) => None,
            (Some(n), false) => Some(n.saturating_sub(PAGE)),
        };
    }

    fn held(&self, codes: &[KeyCode]) -> bool {
        codes.iter().any(|c| self.keys.contains(c) || self.scripted_key(self.now()) == Some(*c))
    }

    /// The key a `--hold` script holds at `now`, counted from when we joined the world.
    fn scripted_key(&self, now: i32) -> Option<KeyCode> {
        let mut t = now - self.holds_from?;
        for &(code, ms) in &self.holds {
            if t < ms {
                return Some(code);
            }
            t -= ms;
        }
        None
    }

    fn frame(&mut self) {
        let now = self.now();
        let dt = (now - self.last_frame).clamp(0, 200);
        self.last_frame = now;
        prof::start();
        if let Some(text) = textedit::clipboard::take_pasted() {
            match (&mut self.chat_input, &self.phase) {
                (Some(input), _) => input.insert(&text),
                (None, Phase::Menu) => self.front.paste(&text),
                _ => {}
            }
        }
        self.advance();
        #[cfg(not(target_arch = "wasm32"))]
        self.advance_update();
        self.note_version(now);
        self.front.version_line = self.version_line();
        // What the Changelog window shows, when it's open.
        let (cl_entries, cl_coming) = if self.changelog.open { self.changelog_entries() } else { (Vec::new(), None) };
        // The Changelog window takes the clicks while it's open; the note takes its own.
        if self.changelog.open {
            for (down, pos) in std::mem::take(&mut self.clicks) {
                if down {
                    self.changelog.click(pos);
                }
            }
            self.mouse_down = false;
        } else {
            let changelog = &mut self.changelog;
            self.clicks.retain(|&(down, pos)| !(down && changelog.click(pos)));
        }
        prof::mark("advance");
        self.update_audio(now);

        let mut keys = (0.0, 0.0);
        let bound = |b: options::Bind, alt: Option<KeyCode>| -> Vec<KeyCode> { self.settings.key(b).into_iter().chain(alt).collect() };
        let (left, right, up, down) = (
            bound(options::Bind::MoveLeft, Some(KeyCode::ArrowLeft)),
            bound(options::Bind::MoveRight, Some(KeyCode::ArrowRight)),
            bound(options::Bind::MoveUp, Some(KeyCode::ArrowUp)),
            bound(options::Bind::MoveDown, Some(KeyCode::ArrowDown)),
        );
        let (rot_l, rot_r, reset) = (bound(options::Bind::RotateLeft, None), bound(options::Bind::RotateRight, None), bound(options::Bind::ResetCamera, None));
        if self.held(&left) {
            keys.0 -= 1.0;
        }
        if self.held(&right) {
            keys.0 += 1.0;
        }
        if self.held(&up) {
            keys.1 -= 1.0;
        }
        if self.held(&down) {
            keys.1 += 1.0;
        }
        let rotate = if self.settings.rotation { self.held(&rot_r) as i32 as f64 - self.held(&rot_l) as i32 as f64 } else { 0.0 };
        let reset_angle = self.held(&reset);

        // Scripted walking (--walk-to), then the --hold script.
        // With --say (a command that may move us, like /shop), walk once we've arrived and every
        // line has gone out.
        let walk_ready = self.say_given.is_none_or(|_| {
            self.say.is_empty() && matches!(&self.phase, Phase::Playing(g) if g.reconnects > 0 && g.world.is_some())
        });
        if let (Some((tx, ty)), Phase::Playing(game), true) = (self.walk_to, &self.phase, walk_ready) {
            if let Some(me) = game.world.as_ref().and_then(|w| w.player()) {
                let (dx, dy) = (tx - me.x, ty - me.y);
                if dx.hypot(dy) < 0.15 {
                    self.walk_to = (!self.walk_more.is_empty()).then(|| self.walk_more.remove(0));
                } else {
                    let rel = dy.atan2(dx) - game.camera_angle;
                    keys = (rel.cos(), rel.sin());
                }
            }
        }
        // A mouse script can also start on the character list or the main menu (tests of the screens).
        if self.holds_from.is_none() && !self.mouse_script.is_empty() && matches!(self.phase, Phase::Menu) && matches!(self.front.screen, screens::Screen::Chars | screens::Screen::Home) {
            self.holds_from = Some(now);
        }
        if self.holds_from.is_none() && !(self.holds.is_empty() && self.mouse_script.is_empty()) && self.walk_to.is_none() {
            if let Phase::Playing(game) = &self.phase {
                if game.world.as_ref().is_some_and(|w| w.player().is_some()) {
                    self.holds_from = Some(now);
                }
            }
        }
        // Scripted mouse steps that are due.
        if let Some(from) = self.holds_from {
            while self.mouse_script.first().is_some_and(|s| now - from >= s.0) {
                let (_, step, pos) = self.mouse_script.remove(0);
                self.mouse = pos;
                match step {
                    MouseStep::Down => self.clicks.push((true, pos)),
                    MouseStep::Up => self.clicks.push((false, pos)),
                    MouseStep::Move => {}
                    MouseStep::Right => self.right_clicks.push(pos),
                }
            }
        }
        // A scripted key starting counts as a press.
        let scripted = self.scripted_key(now);
        if scripted != self.last_scripted {
            match scripted {
                Some(KeyCode::F22) => {
                    self.autofire = !self.autofire;
                    log::info!("autofire {}", if self.autofire { "on" } else { "off" });
                }
                Some(k) => self.pressed.push(k),
                None => {}
            }
            self.last_scripted = scripted;
        }
        let pressed = std::mem::take(&mut self.pressed);
        let scripted_fire = self.scripted_key(now) == Some(KeyCode::F23);
        let wants_fire_scripted = self.autofire || scripted_fire;
        let (mouse, script_aim) = (self.mouse, self.script_aim);
        let Some(loaded) = self.loaded.as_mut() else { return };
        let Loaded { renderer, sprites, data, redrawer, fonts, text, .. } = &mut **loaded;
        renderer.set_vsync(!(self.settings.unlock_fps || self.bench.is_some()));
        #[cfg(not(target_arch = "wasm32"))]
        if self.settings.max_fps > 0 && self.bench.is_none() {
            let frame = std::time::Duration::from_secs_f64(1.0 / self.settings.max_fps as f64);
            let spent = self.frame_start.elapsed();
            if spent < frame {
                std::thread::sleep(frame - spent);
            }
            self.frame_start = Instant::now();
        }
        let lay = hud::Layout::new(renderer.size().0, renderer.size().1);
        let mut ability = false;
        let mut panel = None;
        let mut to_chars = false;
        let mut jitter = (0.0, 0.0);
        let show = view::Show {
            shadows: self.settings.shadows,
            hp_bars: self.settings.hp_bars,
            bubbles: self.settings.bubbles,
            ally_shots: self.settings.ally_shots,
            pet_shots: self.settings.pet_shots,
            pet_shot_look: self.settings.pet_shot_look,
            player_alpha: self.settings.player_alpha,
            pet_alpha: self.settings.pet_alpha,
            damage_numbers: self.settings.damage_numbers,
        };
        if matches!(self.phase, Phase::Menu) {
            // The crosshair is only for the game: however it was left (a death, a disconnect,
            // the Escape menu), the pointer shows again here.
            hide_pointer(&self.window, &mut self.cursor_hidden, false, "menu");
            let mut actions = Vec::new();
            for (code, text, ctrl, shift) in std::mem::take(&mut self.front_keys) {
                actions.extend(self.front.key(code, text.as_deref(), ctrl, shift));
            }
            for (down, pos) in std::mem::take(&mut self.clicks) {
                if !down {
                    self.front.release(pos);
                    if self.options_ui.release() {
                        self.settings.save();
                    }
                    continue;
                }
                if self.options_ui.open {
                    if self.options_ui.click(pos, &mut self.settings) {
                        self.settings.save();
                    }
                } else {
                    actions.extend(self.front.click(pos));
                }
            }
            self.options_ui.drag(mouse, &mut self.settings);
            self.front.drag(mouse);
            // Its "Return home" means nothing out here.
            self.options_ui.go_home = false;
            renderer.begin();
            let (w, h) = renderer.size();
            let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
            self.front.draw(&mut ui, w as f32, h as f32, mouse, now);
            self.options_ui.in_game = false;
            self.options_ui.draw(&mut ui, &lay, &self.settings, mouse);
            let s = screens::scale(w as f32, h as f32);
            self.changelog.draw_note(&mut ui, 16.0 * s, 16.0 * s, s, mouse, now);
            self.changelog.draw(&mut ui, w as f32, h as f32, &cl_entries, version::VERSION, cl_coming.as_deref(), mouse);
            self.finish_frame(now);
            for action in actions {
                self.front_action(action);
            }
            // Keep the server chosen (preferredServer).
            let chosen = self.front.list.as_ref().and_then(|l| l.servers.get(self.front.server)).map(|s| s.name.clone());
            if let Some(name) = chosen.filter(|n| *n != self.settings.preferred_server) {
                self.settings.preferred_server = name;
                self.settings.save();
            }
            return;
        }
        // The Escape menu takes the clicks while it's open.
        if self.game_menu {
            let mut choice = None;
            for (down, pos) in std::mem::take(&mut self.clicks) {
                if down {
                    choice = choice.or(screens::menu_click(&self.menu_buttons, pos));
                }
            }
            self.mouse_down = false;
            match choice {
                Some(screens::MenuChoice::Resume) => self.game_menu = false,
                Some(screens::MenuChoice::Nexus) => {
                    self.game_menu = false;
                    if let Phase::Playing(game) = &mut self.phase {
                        game.escape();
                    }
                }
                Some(screens::MenuChoice::Options) => {
                    self.game_menu = false;
                    self.options_ui.show();
                }
                Some(screens::MenuChoice::Changelog) => {
                    self.game_menu = false;
                    self.changelog.show();
                }
                Some(screens::MenuChoice::Home) => {
                    self.leave_game(None);
                    return;
                }
                Some(screens::MenuChoice::Logout) => {
                    self.leave_game(None);
                    self.logout();
                    return;
                }
                None => {}
            }
        }
        let Some(loaded) = self.loaded.as_mut() else { return };
        let Loaded { renderer, sprites, data, redrawer, fonts, text, ground, .. } = &mut **loaded;
        if let Phase::Playing(game) = &mut self.phase {
            // The panel as it stands, for clicks and number keys.
            let social = hud::Social {
                trade: game.trade.as_ref(),
                invite: game.invite.as_ref().map(|(i, _)| i),
                locked: &game.locked,
                ignored: &game.ignored,
                now,
                quest: game.quest,
                history: &game.boss_history,
                fame: &game.fame_stats,
            };
            let built = game.world.as_ref().map(|w| hud::Panel::new(&lay, w, data, &self.hud, &social));
            let mut actions = Vec::new();
            use options::Bind;
            for key in pressed {
                // The bound key, or a few fixed extras (numpad 0, + and -).
                let bind = self.settings.bind_of(key).or(match key {
                    KeyCode::Numpad0 => Some(Bind::Interact),
                    KeyCode::NumpadAdd => Some(Bind::ZoomIn),
                    KeyCode::NumpadSubtract => Some(Bind::ZoomOut),
                    _ => None,
                });
                match bind {
                    // Interact with what we stand by (enter the portal, buy from the shop).
                    Some(Bind::Interact) => match built.as_ref().and_then(|p| p.interact.clone()) {
                        Some(hud::Interact { id, kind: hud::InteractKind::Portal, .. }) => game.use_portal(id),
                        Some(hud::Interact { id, kind: hud::InteractKind::Shop { .. }, .. }) => game.buy(id),
                        Some(hud::Interact { kind: hud::InteractKind::Npc { kind, .. }, .. }) => {
                            to_chars |= npc_open(kind, game, &mut self.npcs, &mut self.market, &mut self.forge, &mut self.forge_acts, self.front.list.as_ref(), data);
                            self.keys.clear();
                        }
                        _ => {
                            if let Some((id, _)) = game.nearest_portal(data) {
                                game.use_portal(id);
                            }
                        }
                    },
                    Some(Bind::Nexus) => {
                        if game.map_name() == "Nexus" {
                            game.add_chat("*Error*", "", "You're already in the Nexus.", 0, 0, -1, now);
                        } else {
                            game.escape();
                        }
                    }
                    Some(Bind::SwitchTabs) => {
                        self.hud.page = if self.hud.page == hud::Page::Inventory { hud::Page::Backpack } else { hud::Page::Inventory };
                    }
                    Some(Bind::Center) => {
                        self.settings.centered = !self.settings.centered;
                        self.settings.save();
                    }
                    Some(Bind::Stats) => {
                        self.settings.show_fps = !self.settings.show_fps;
                        self.settings.save();
                    }
                    // MapUserInput's shortcuts for chat commands.
                    Some(b @ (Bind::Vault | Bind::Realm | Bind::GuildHall | Bind::PartyInviteWorld | Bind::PartyJoinWorld)) => {
                        let (command, here) = match b {
                            Bind::Vault => ("/vault", "Vault"),
                            Bind::Realm => ("/realm", "Realm"),
                            Bind::GuildHall => ("/ghall", "GuildHall"),
                            Bind::PartyInviteWorld => ("/pinviteworld", ""),
                            _ => ("/pjoin", ""),
                        };
                        if !here.is_empty() && game.map_name() == here {
                            game.add_chat("*Error*", "", &format!("You're already in the {}.", if here == "GuildHall" { "Guild Hall" } else { here }), 0, 0, -1, now);
                        } else {
                            game.say(command);
                        }
                    }
                    Some(Bind::Autofire) => self.autofire = !self.autofire,
                    Some(Bind::ZoomOut) => self.hud.minimap_zoom += 1,
                    Some(Bind::ZoomIn) => self.hud.minimap_zoom = self.hud.minimap_zoom.saturating_sub(1),
                    Some(Bind::Ability) => ability = true,
                    Some(Bind::HealthPotion) => game.use_potion(now, 0),
                    Some(Bind::MagicPotion) => game.use_potion(now, 1),
                    Some(Bind::Options) => {
                        self.options_ui.show();
                        self.keys.clear();
                    }
                    // Equip or use what's in that inventory slot (MapUserInput.useItem).
                    Some(Bind::Slot(n)) => {
                        if let Some(p) = &built {
                            if let Some(slot) = p.slots.iter().find(|s| s.kind == hud::SlotKind::Inventory(n)) {
                                if slot.item > 0 {
                                    actions.push(hud::Action::EquipOrUse(*slot));
                                }
                            }
                        }
                    }
                    _ => {}
                }
            }
            if game.world.as_ref().is_some_and(|w| w.player().is_some()) {
                if let Some(kind) = self.npc_test.take().and_then(|c| npc::NpcKind::of_class(&c)) {
                    to_chars |= npc_open(kind, game, &mut self.npcs, &mut self.market, &mut self.forge, &mut self.forge_acts, self.front.list.as_ref(), data);
                }
            }
            // The options window takes the mouse while it's open.
            if self.options_ui.open {
                for (down, pos) in std::mem::take(&mut self.clicks) {
                    if if down { self.options_ui.click(pos, &mut self.settings) } else { self.options_ui.release() } {
                        self.settings.save();
                    }
                }
                self.options_ui.drag(self.mouse, &mut self.settings);
                self.mouse_down = false;
            }
            game.particles.disabled = !self.settings.particles;
            game.particles.hide_pets = !self.settings.pet_effects;
            game.particles.pet_shots = (self.settings.pet_shot_look, self.settings.pet_shots);
            // Options' "Return home": out to the main menu.
            if std::mem::take(&mut self.options_ui.go_home) {
                self.leave_game(None);
                return;
            }
            // The market window: its clicks, and the server's answers.
            let mut market_acts = Vec::new();
            if self.market.open {
                for (down, pos) in std::mem::take(&mut self.clicks) {
                    if down {
                        market_acts.extend(self.market.click(pos));
                    }
                }
                self.mouse_down = false;
            }
            for e in std::mem::take(&mut game.market_events) {
                market_acts.extend(self.market.event(e));
            }
            for act in market_acts {
                game.market(act);
            }
            // The forge window: the same.
            let mut forge_acts = std::mem::take(&mut self.forge_acts);
            if self.forge.open {
                for (down, pos) in std::mem::take(&mut self.clicks) {
                    if down {
                        forge_acts.extend(self.forge.click(pos, data));
                    }
                }
                self.mouse_down = false;
            }
            for e in std::mem::take(&mut game.forge_events) {
                self.forge.event(e, data);
            }
            for act in forge_acts {
                game.forge(act);
            }
            // --type: into the forge's search once its recipes came.
            if self.forge.open && self.forge.loaded() {
                if let Some(t) = self.type_text.take() {
                    self.forge.key(KeyCode::KeyA, Some(&t), data);
                }
            }
            // An NPC's window takes the keyboard and the mouse while it's open.
            // --type: into a prompt (and Enter), or onto the board being edited.
            match &self.npcs.window {
                Some(npc::Window::Prompt { .. }) => {
                    if let Some(t) = self.type_text.take() {
                        self.npc_keys.push((KeyCode::KeyA, Some(t)));
                        self.npc_keys.push((KeyCode::Enter, None));
                    }
                }
                Some(npc::Window::Board { edit: Some(_), .. }) => {
                    if let Some(t) = self.type_text.take() {
                        self.npc_keys.push((KeyCode::KeyA, Some(t)));
                    }
                }
                _ => {}
            }
            let mut npc_acts = Vec::new();
            for (code, text) in std::mem::take(&mut self.npc_keys) {
                npc_acts.extend(self.npcs.key(code, text.as_deref()));
            }
            if self.npcs.window.is_some() {
                let me = game.world.as_ref().and_then(|w| w.player());
                for (down, pos) in std::mem::take(&mut self.clicks) {
                    if down {
                        npc_acts.extend(self.npcs.click(pos, me));
                    }
                }
                self.mouse_down = false;
            }
            for act in npc_acts {
                if let Some(board) = npc_act(act, game, &mut self.npcs, now) {
                    if let (Some(endpoint), Some((email, password))) = (&self.endpoint, &self.account) {
                        self.npc_request = Some((NpcRequest::SetBoard, endpoint.set_guild_board(email, password, &board)));
                    }
                }
            }
            if let Some(p) = &built {
                p.moved(&mut self.hud, self.mouse);
                // Ctrl+right-click: break the item down (ItemGridMediator.onRightCtrlClick).
                // A right-click on a nearby player keeps their card open.
                for pos in std::mem::take(&mut self.right_clicks) {
                    if !self.ctrl_held && p.right_click(&mut self.hud, &lay, pos) {
                        continue;
                    }
                    if let (true, Some(slot)) = (self.ctrl_held, p.slot_at(pos)) {
                        if slot.item > 0 && matches!(slot.kind, hud::SlotKind::Inventory(_) | hud::SlotKind::Container) {
                            game.breakdown(now, slot.owner, slot.index);
                        }
                    }
                }
                for (down, pos) in std::mem::take(&mut self.clicks) {
                    // Ctrl+click: to the first free slot of the other page (onCtrlClick).
                    if let (true, true, Some(slot)) = (down, self.ctrl_held, p.slot_at(pos)) {
                        if slot.item > 0 && matches!(slot.kind, hud::SlotKind::Inventory(_)) {
                            let me = game.world.as_ref().and_then(|w| w.player());
                            let backpack = me.is_some_and(|m| m.stat_int(hud::stat::HAS_BACKPACK).unwrap_or(0) != 0);
                            let other = if slot.index < 12 { 12..20 } else { 4..12 };
                            let free = me.and_then(|m| other.clone().find(|&i| m.stat_int(hud::slot_stat(i)).unwrap_or(-1) <= 0));
                            if let (true, Some(to), Some(id)) = (backpack, free, me.map(|m| m.id)) {
                                game.inv_swap(now, (slot.owner, slot.index, slot.item), (id, to, -1));
                            }
                            self.mouse_down = false;
                            continue;
                        }
                    }
                    // A player's name in the chat: their menu if they're here, else a whisper.
                    let (px, py) = (pos.0 as f32, pos.1 as f32);
                    let name = self.chat_names.iter().find(|(_, r)| px >= r[0] && py >= r[1] && px < r[0] + r[2] && py < r[1] + r[3]);
                    if let (true, Some((name, _))) = (down, name) {
                        let here = game.world.as_ref().and_then(|w| {
                            w.objects.values().find(|o| Some(o.id) != w.me && o.name() == Some(name.as_str()) && data.objects.get(&o.object_type).is_some_and(|d| d.player))
                        });
                        match here {
                            Some(o) => self.hud.open_menu(o.id, pos),
                            None => self.chat_input = Some(chat_line(format!("/tell {name} "))),
                        }
                        self.mouse_down = false;
                        continue;
                    }
                    if down {
                        let taken = self.chat_input.is_none() && p.press(&mut self.hud, &lay, pos, self.shift_held, now, &mut actions);
                        self.mouse_down = !taken;
                    } else {
                        self.mouse_down = false;
                        p.release(&mut self.hud, &lay, data, pos, &mut actions);
                    }
                }
                for action in actions {
                    if let hud::Action::Button(hud::Button::Npc(kind)) = action {
                        to_chars |= npc_open(kind, game, &mut self.npcs, &mut self.market, &mut self.forge, &mut self.forge_acts, self.front.list.as_ref(), data);
                        self.keys.clear();
                        continue;
                    }
                    if let Some(line) = apply_action(game, p, action, now, data) {
                        self.chat_input = Some(chat_line(line));
                    }
                }
            } else {
                for (down, _) in std::mem::take(&mut self.clicks) {
                    self.mouse_down = down && !lay.on_panel(self.mouse.0, self.mouse.1);
                }
            }
            panel = built;
            if let Some(world) = &game.world {
                self.hud.minimap_zoom = self.hud.minimap_zoom.min(hud::minimap_levels(world).len() - 1);
            }
            for line in self.chat_send.drain(..) {
                game.say(&line);
            }
            if !self.say.is_empty()
                && self.say_sent_in.is_none_or(|(join, at)| join != game.reconnects || now - at >= 3000)
                && game.world.as_ref().is_some_and(|w| w.player().is_some())
            {
                game.say(&self.say.remove(0));
                self.say_sent_in = Some((game.reconnects, now));
            }
            prof::mark("pre");
            game.poll(now, data);
            prof::mark("poll");
            if reset_angle {
                game.camera_angle = game::RESET_CAMERA_ANGLE;
            }
            game.update(now, dt, keys, rotate, data);
            prof::mark("update");
            // Keep the camera's turn once it stops turning (cameraAngle).
            if rotate == 0.0 && self.settings.camera_angle != Some(game.camera_angle) {
                self.settings.camera_angle = Some(game.camera_angle);
                self.settings.save();
            }
            if let (Some((kind, last)), Some(world)) = (&mut self.effect_test, &game.world) {
                if now - *last >= 500 {
                    *last = now;
                    game.particles.test_effect(*kind, world, now);
                }
            }
            game.particles.realize(sprites, data);
            if let Some(world) = &game.world {
                game.particles.object_effects(world, data, sprites, dt);
            }
            jitter = game.particles.jitter(now);
            game.merchant_balloons(now, data);
            prof::mark("effects");
        }

        let wants_fire = (self.mouse_down && self.hud.drag.is_none()) || wants_fire_scripted;
        // Over the HUD's boxes (and the interact area while it shows), not the world.
        let on_hud = |pos: (f64, f64)| panel.as_ref().map_or(lay.on_panel(pos.0, pos.1), |p| p.on_hud(&lay, pos));
        renderer.begin();
        let (_, h) = renderer.size();
        let status = match &self.phase {
            Phase::Menu => String::new(),
            Phase::Playing(game) => game.status.clone(),
        };
        let mut view_radius = None;
        let mut fire = None;
        let mut ability_at = None;
        match &self.phase {
            Phase::Menu => {}
            Phase::Playing(game) => {
                if let Some(world) = &game.world {
                    if let Some(me) = world.player() {
                        // Freer layout than the AIR client's 600×600 view beside its HUD: the
                        // world fills the window left of the panel, the player centred in it a
                        // little below the middle, the map scaled to the space (hud::Layout).
                        let scale = lay.map_scale(self.settings.zoom);
                        let (cx, cy) = lay.camera_point(self.settings.centered);
                        let camera = view::Camera::new(me.x + jitter.0, me.y + jitter.1, game.camera_angle, scale, cx, cy).snapped();
                        // The farthest corner of the view from us, in tiles.
                        let vw = lay.view_w() as f64;
                        let far = [(0.0, 0.0), (vw, 0.0), (0.0, h as f64), (vw, h as f64)]
                            .iter()
                            .map(|(x, y)| (x - camera.cx).hypot(y - camera.cy) / scale)
                            .fold(0.0, f64::max);
                        view_radius = Some(far);
                        // Firing: toward the mouse from where we stand on the screen (MapUserInput),
                        // or toward --aim for scripted fire. With staff's /autoaim on (and only
                        // then, for --aim auto too), at the nearest enemy, or the mouse if there's none.
                        if wants_fire {
                            let auto = game.auto_aim.then(|| nearest_enemy_angle(world, data)).flatten();
                            fire = match (scripted_fire, script_aim) {
                                (true, Some(screen_angle)) => Some(game.camera_angle + screen_angle),
                                (true, None) => auto,
                                _ if on_hud(mouse) => None,
                                _ => auto.or(Some(game.camera_angle + (mouse.1 - camera.cy).atan2(mouse.0 - camera.cx))),
                            };
                        }
                        let conditions = world.player().map_or([0, 0], |p| p.conditions());
                        let has = |e| bsk_world::condition::has(conditions, e);
                        // Unstable (MapUserInput): shots and abilities go anywhere.
                        let wild = |n: u32| {
                            let h = (now as u32).wrapping_mul(2_654_435_761) ^ n.wrapping_mul(40_503);
                            (h.wrapping_mul(2_246_822_519) >> 8) as f64 / (1u32 << 24) as f64
                        };
                        let unstable = has(bsk_world::condition::UNSTABLE);
                        if unstable && fire.is_some() {
                            fire = Some(wild(1) * std::f64::consts::TAU);
                        }
                        if ability {
                            let (mx, my) = if unstable {
                                (camera.cx + wild(2) * 600.0 - 300.0, camera.cy + wild(3) * 600.0 - 325.0)
                            } else {
                                mouse
                            };
                            let (x, y) = camera.to_world(mx, my);
                            let angle = game.camera_angle + (my - camera.cy).atan2(mx - camera.cx);
                            ability_at = Some((x, y, angle));
                        }
                        let hallucinating = has(bsk_world::condition::HALLUCINATING);
                        let scene = view::Scene { sprites, data, world, camera, time: now, floaters: &game.floaters, bubbles: &game.bubbles, particles: &game.particles, show, hallucinating };
                        prof::mark("pre-world");
                        view::draw(&scene, renderer, redrawer, ground, fonts, text);
                        // The world through a filter (Map.getFilterIndex): paused, else blind, else
                        // drunk (a swaying blur).
                        let filter = if has(bsk_world::condition::PAUSED) {
                            Some(bsk_render::ScreenFilter::Grey)
                        } else if has(bsk_world::condition::BLIND) {
                            Some(bsk_render::ScreenFilter::Blind)
                        } else if has(bsk_world::condition::DRUNK) {
                            let d = 20.0 + 10.0 * (now as f32 / 1000.0).sin();
                            Some(bsk_render::ScreenFilter::Blur(d / 4.0 * (scale / view::TILE_PX) as f32))
                        } else {
                            None
                        };
                        renderer.world_done(filter);
                        let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
                        self.signs.draw(&mut ui, world, &camera);
                        hud::draw_quest(&mut ui, &lay, world, &camera, game.quest, &game.keys, &game.dealt, game.quest_damage.as_ref(), &game.boss_history, &self.hud, mouse);
                        prof::mark("quest");
                    }
                    if let Some(p) = &panel {
                        let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
                        let social = hud::Social {
                            trade: game.trade.as_ref(),
                            invite: game.invite.as_ref().map(|(i, _)| i),
                            locked: &game.locked,
                            ignored: &game.ignored,
                            now,
                            quest: game.quest,
                            history: &game.boss_history,
                            fame: &game.fame_stats,
                        };
                        self.hud.nexus_key = options::key_label(self.settings.key(options::Bind::Nexus));
                        self.hud.interact_key = self.settings.key(options::Bind::Interact).map_or(String::new(), |k| options::key_label(Some(k)));
                        self.hud.tabs_key = self.settings.key(options::Bind::SwitchTabs).map_or(String::new(), |k| options::key_label(Some(k)));
                        self.hud.potion_keys = [
                            options::key_label(self.settings.key(options::Bind::HealthPotion)),
                            options::key_label(self.settings.key(options::Bind::MagicPotion)),
                        ];
                        prof::mark("pre-panel");
                        hud::draw_panel(&mut ui, &lay, world, game.camera_angle, &mut self.map_colors, p, &self.hud, &social, mouse);
                        prof::mark("panel");
                    }
                }
                let me = game.world.as_ref().and_then(|w| w.player()).and_then(|p| p.name()).unwrap_or("").to_owned();
                let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
                prof::mark("pre-chat");
                self.chat_names = hud::draw_chat(&mut ui, &lay, &game.chat, &me, self.chat_input.as_ref(), self.chat_scroll, now);
                if let Some(p) = game.world.as_ref().and_then(|w| w.player()) {
                    // The frame rate and the ping (L), under the gold and fame.
                    let fps = self.settings.show_fps.then(|| match game.ping {
                        Some(ms) => format!("{:.0} fps  ·  {ms} ms", self.fps_shown),
                        None => format!("{:.0} fps", self.fps_shown),
                    });
                    hud::draw_credits(&mut ui, &lay, p, game.show_displays && game.arena.is_none(), fps.as_deref());
                }
                if let Some(arena) = game.arena {
                    hud::draw_arena(&mut ui, &lay, arena, now);
                }
            }
        }
        if let (Some(far), Phase::Playing(game)) = (view_radius, &mut self.phase) {
            game.want_view_radius(far, now);
            if let Some(angle) = fire {
                game.shoot(now, angle, data);
            }
            if let Some((x, y, angle)) = ability_at {
                game.use_ability(now, x, y, angle, data);
            }
        }
        if !status.is_empty() {
            let style = text::Style { size: 18.0, bold: true, color: 0xFFFFFF };
            text.draw_centered(fonts, renderer, &status, lay.camera_point(true).0 as f32, h as f32 / 2.0 + 40.0, style);
        }
        // The dragged item and tooltips go over everything.
        if let (Phase::Playing(game), Some(p)) = (&self.phase, &panel) {
            if let Some(world) = &game.world {
                let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
                let social = hud::Social {
                    trade: game.trade.as_ref(),
                    invite: game.invite.as_ref().map(|(i, _)| i),
                    locked: &game.locked,
                    ignored: &game.ignored,
                    now,
                    quest: game.quest,
                    history: &game.boss_history,
                    fame: &game.fame_stats,
                };
                hud::draw_windows(&mut ui, &lay, world, p, &social, mouse);
                self.npcs.draw(&mut ui, &lay, world.player(), mouse, now);
                self.market.draw(&mut ui, &lay, world.player(), mouse, now);
                self.forge.draw(&mut ui, &lay, world.player(), mouse, now);
                prof::mark("chat..windows");
                hud::draw_overlay(&mut ui, &lay, world, p, &self.hud, mouse);
                prof::mark("overlay");
            }
        }
        // The crosshair, over the world while nothing else has the mouse; the pointer hides
        // while it shows.
        let crosshair = self.settings.crosshair
            && matches!(&self.phase, Phase::Playing(g) if g.world.is_some())
            && !on_hud(mouse)
            && !self.game_menu
            && !self.options_ui.open
            && !self.changelog.open
            && !self.market.open
            && !self.forge.open
            && self.npcs.window.is_none()
            && self.hud.drag.is_none();
        if crosshair {
            draw_crosshair(renderer, mouse, lay.s);
        }
        hide_pointer(&self.window, &mut self.cursor_hidden, crosshair, "game");
        if self.game_menu {
            let (w, h) = renderer.size();
            let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
            self.menu_buttons = screens::draw_game_menu(&mut ui, w as f32, h as f32, mouse);
        }
        if self.options_ui.open {
            let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
            self.options_ui.in_game = true;
            self.options_ui.draw(&mut ui, &lay, &self.settings, mouse);
        }
        {
            let (w, h) = renderer.size();
            let mut ui = hud::Ui { renderer: &mut *renderer, sprites, data, redrawer: &mut *redrawer, fonts, text: &mut *text };
            self.changelog.draw_note(&mut ui, 12.0 * lay.s, 12.0 * lay.s, lay.s, mouse, now);
            self.changelog.draw(&mut ui, w as f32, h as f32, &cl_entries, version::VERSION, cl_coming.as_deref(), mouse);
        }
        self.finish_frame(now);
        if to_chars {
            self.leave_game(None);
        }
    }

    /// Present the frame: the --shot capture, and the title's frame rate.
    fn finish_frame(&mut self, now: i32) {
        let status = match &self.phase {
            Phase::Menu => String::new(),
            Phase::Playing(game) => game.status.clone(),
        };
        let Some(loaded) = self.loaded.as_mut() else { return };
        let renderer = &mut loaded.renderer;
        if let Some((_, after)) = &self.shot {
            if now as f32 >= after * 1000.0 && !renderer.capture_next_frame() {
                log::error!("--shot: this surface can't be read back");
                self.quit = true;
            }
        }
        prof::mark("rest");
        let built = self.frame_start_bench.elapsed();
        let queued = renderer.queued();
        if let Err(e) = renderer.present(0x000000) {
            log::warn!("present: {e}");
        }
        prof::mark("present");
        // --bench: where the time goes, every two seconds.
        if let Some(b) = &mut self.bench {
            b.frames += 1;
            b.build += built.as_secs_f64();
            b.total += self.frame_start_bench.elapsed().as_secs_f64();
            if b.since.elapsed().as_secs_f64() >= 2.0 {
                let n = b.frames as f64;
                log::info!(
                    "bench: {:.0} fps, build {:.3} ms, present {:.3} ms, {} quads, {} tri verts, {} draws",
                    n / b.since.elapsed().as_secs_f64(),
                    b.build / n * 1000.0,
                    (b.total - b.build) / n * 1000.0,
                    queued.0,
                    queued.1,
                    renderer.last_draws
                );
                log::info!("bench: {}", prof::report(b.frames));
                *b = Bench::default();
            }
        }
        self.frame_start_bench = Instant::now();
        if let Some(image) = renderer.take_capture() {
            let path = self.shot.take().map(|s| s.0).unwrap_or_default();
            match image.save(&path) {
                Ok(()) => log::info!("saved {path}"),
                Err(e) => log::error!("{path}: {e}"),
            }
            self.quit = true;
        }

        self.fps_frames += 1;
        let elapsed = self.fps_since.elapsed().as_secs_f32();
        if elapsed >= 0.5 {
            self.fps_shown = self.fps_frames as f32 / elapsed;
            let mut title = format!("{TITLE} - {:.0} fps, {} draws", self.fps_shown, renderer.last_draws);
            if !status.is_empty() {
                title += &format!(" - {status}");
            }
            if title != self.last_title {
                if let Some(window) = &self.window {
                    window.set_title(&title);
                }
                if !status.is_empty() && !self.last_title.ends_with(&status) {
                    log::info!("{status}");
                }
                self.last_title = title;
            }
            self.fps_frames = 0;
            self.fps_since = Instant::now();
        }
    }
}

/// Hide the pointer (for the crosshair) or show it again, logging the change and where.
fn hide_pointer(window: &Option<Arc<Window>>, hidden: &mut bool, hide: bool, place: &str) {
    if hide != *hidden {
        *hidden = hide;
        log::info!("pointer {} ({place})", if hide { "hidden" } else { "shown" });
        if let Some(window) = window {
            window.set_cursor_visible(!hide);
        }
    }
}

/// The crosshair at the mouse: four short arms around a gap and a dot in the accent colour, each
/// edged in black so it shows on any ground. Whole pixels, so it stays sharp.
fn draw_crosshair(renderer: &mut bsk_render::Renderer, mouse: (f64, f64), s: f32) {
    let (cx, cy) = (mouse.0.round() as f32, mouse.1.round() as f32);
    let u = s.max(0.75);
    let (gap, len, t) = ((4.0 * u).round(), (7.0 * u).round(), (2.0 * u).round().max(2.0));
    let half = (t / 2.0).floor();
    let arms = [
        [cx - gap - len, cy - half, len, t],
        [cx + gap, cy - half, len, t],
        [cx - half, cy - gap - len, t, len],
        [cx - half, cy + gap, t, len],
    ];
    let dot = [cx - half, cy - half, t, t];
    for r in arms.iter().chain([&dot]) {
        renderer.fill(r[0] - 1.0, r[1] - 1.0, r[2] + 2.0, r[3] + 2.0, 0x000000, 0.75);
    }
    for r in arms {
        renderer.fill(r[0], r[1], r[2], r[3], 0xFFFFFF, 0.95);
    }
    renderer.fill(dot[0], dot[1], dot[2], dot[3], theme::color::ACCENT, 1.0);
}

impl ApplicationHandler<UserEvent> for App {
    fn resumed(&mut self, event_loop: &ActiveEventLoop) {
        if self.window.is_some() {
            return;
        }
        let attributes = Window::default_attributes().with_title(TITLE);
        #[cfg(not(target_arch = "wasm32"))]
        let attributes = {
            // `--size WxH` (tests): the window's size in physical pixels.
            let args: Vec<String> = std::env::args().collect();
            let size = args.iter().position(|a| a == "--size").and_then(|i| args.get(i + 1)).and_then(|v| {
                let (w, h) = v.split_once('x')?;
                Some((w.parse::<u32>().ok()?, h.parse::<u32>().ok()?))
            });
            match size {
                Some((w, h)) => attributes.with_inner_size(winit::dpi::PhysicalSize::new(w, h)),
                None => attributes.with_inner_size(winit::dpi::LogicalSize::new(1280.0, 800.0)),
            }
            .with_window_icon(window_icon(false))
            // A scripted test run opens behind the window in use, so it doesn't take the keyboard.
            .with_active(!args.iter().any(|a| matches!(a.as_str(), "--shot" | "--hold" | "--mouse-script")))
        };
        #[cfg(target_os = "windows")]
        let attributes = {
            use winit::platform::windows::WindowAttributesExtWindows;
            attributes.with_taskbar_icon(window_icon(true))
        };
        // In a browser the page's CSS sizes the canvas.
        #[cfg(target_arch = "wasm32")]
        let attributes = {
            use winit::platform::web::WindowAttributesExtWebSys;
            attributes.with_append(true)
        };
        // Resuming from the version before: hidden until it's ready to take over.
        #[cfg(not(target_arch = "wasm32"))]
        let attributes = attributes.with_visible(self.resume.is_none());
        let window = Arc::new(event_loop.create_window(attributes).expect("create window"));
        self.window = Some(window.clone());

        let proxy = self.proxy.clone();
        let task = async move {
            let event = match load(window).await {
                Ok(loaded) => UserEvent::Ready(loaded),
                Err(e) => UserEvent::Failed(e),
            };
            let _ = proxy.send_event(event);
        };
        #[cfg(not(target_arch = "wasm32"))]
        pollster::block_on(task);
        #[cfg(target_arch = "wasm32")]
        wasm_bindgen_futures::spawn_local(task);
    }

    fn user_event(&mut self, event_loop: &ActiveEventLoop, event: UserEvent) {
        match event {
            UserEvent::Ready(loaded) => {
                self.loaded = Some(loaded);
                // The page's loading screen can go (`window.bskReady()`, web/index.html).
                #[cfg(target_arch = "wasm32")]
                if let Some(window) = web_sys::window() {
                    use wasm_bindgen::JsCast;
                    if let Ok(f) = js_sys::Reflect::get(&window, &wasm_bindgen::JsValue::from_str("bskReady")) {
                        if let Some(f) = f.dyn_ref::<js_sys::Function>() {
                            let _ = f.call0(&wasm_bindgen::JsValue::NULL);
                        }
                    }
                }
                if let Some(window) = &self.window {
                    let size = window.inner_size();
                    self.loaded.as_mut().unwrap().renderer.resize(size.width, size.height);
                    window.request_redraw();
                }
                self.start();
                #[cfg(not(target_arch = "wasm32"))]
                self.show_resumed_window();
            }
            UserEvent::Failed(e) => {
                log::error!("startup failed: {e}");
                event_loop.exit();
            }
        }
    }

    fn window_event(&mut self, event_loop: &ActiveEventLoop, _id: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => {
                if let Phase::Playing(game) = &mut self.phase {
                    game.close();
                }
                event_loop.exit();
            }
            WindowEvent::Resized(size) => {
                if let Some(loaded) = &mut self.loaded {
                    loaded.renderer.resize(size.width, size.height);
                }
            }
            WindowEvent::ModifiersChanged(m) => {
                self.alt_held = m.state().alt_key();
                self.shift_held = m.state().shift_key();
                // Cmd on a Mac edits text as Ctrl does elsewhere.
                self.ctrl_held = m.state().control_key()
                    || (cfg!(any(target_arch = "wasm32", target_os = "macos")) && m.state().super_key());
            }
            WindowEvent::KeyboardInput { event, .. } => {
                let PhysicalKey::Code(code) = event.physical_key else { return };
                if let Some(a) = &mut self.audio {
                    a.wake();
                }
                let pressed = event.state == ElementState::Pressed;
                // F11 or Alt+Enter: fullscreen (borderless) and back.
                if pressed && !event.repeat && (code == KeyCode::F11 || (self.alt_held && code == KeyCode::Enter)) {
                    if let Some(window) = &self.window {
                        let full = window.fullscreen().is_some();
                        window.set_fullscreen((!full).then_some(winit::window::Fullscreen::Borderless(None)));
                    }
                    return;
                }
                if self.changelog.open {
                    if pressed {
                        self.changelog.key(code);
                    }
                    self.keys.remove(&code);
                    return;
                }
                if self.options_ui.open && matches!(self.phase, Phase::Menu) {
                    if pressed && self.options_ui.key(code, &mut self.settings) {
                        self.settings.save();
                    }
                    return;
                }
                if matches!(self.phase, Phase::Menu) {
                    // The page's copy or cut event comes straight after the key, before the frame
                    // that handles it, so the text goes to the clipboard now.
                    #[cfg(target_arch = "wasm32")]
                    if pressed && textedit::LineEdit::copies(code, self.ctrl_held, self.shift_held) {
                        if let Some(text) = self.front.copyable() {
                            textedit::clipboard::set(text);
                        }
                    }
                    if pressed {
                        self.front_keys.push((code, event.text.as_ref().map(|t| t.to_string()), self.ctrl_held, self.shift_held));
                    }
                    return;
                }
                // Typing a chat line: keys go to the text, not the game.
                if let Some(input) = &mut self.chat_input {
                    if pressed {
                        match code {
                            KeyCode::Enter | KeyCode::NumpadEnter => {
                                let line = std::mem::take(&mut input.text);
                                if !line.trim().is_empty() {
                                    self.chat_send.push(line);
                                }
                                self.chat_input = None;
                            }
                            KeyCode::Escape => self.chat_input = None,
                            KeyCode::Tab => {
                                let line = self.tell_prefix();
                                self.chat_input = Some(chat_line(line));
                            }
                            KeyCode::PageUp | KeyCode::PageDown => self.scroll_chat(code == KeyCode::PageUp),
                            _ => {
                                input.key(code, event.text.as_deref(), self.ctrl_held, self.shift_held);
                            }
                        }
                    }
                    return;
                }
                if self.market.open && matches!(self.phase, Phase::Playing(_)) {
                    if pressed {
                        if let Some(l) = &self.loaded {
                            self.market.key(code, event.text.as_deref(), &l.data);
                        }
                    }
                    self.keys.remove(&code);
                    return;
                }
                if self.forge.open && matches!(self.phase, Phase::Playing(_)) {
                    if pressed {
                        if let Some(l) = &self.loaded {
                            self.forge.key(code, event.text.as_deref(), &l.data);
                        }
                    }
                    self.keys.remove(&code);
                    return;
                }
                if self.options_ui.open && matches!(self.phase, Phase::Playing(_)) {
                    if pressed && self.options_ui.key(code, &mut self.settings) {
                        self.settings.save();
                    }
                    self.keys.remove(&code);
                    return;
                }
                if self.npcs.window.is_some() && matches!(self.phase, Phase::Playing(_)) {
                    if pressed {
                        self.npc_keys.push((code, event.text.as_ref().map(|t| t.to_string())));
                    }
                    self.keys.remove(&code);
                    return;
                }
                if pressed && !event.repeat && code == KeyCode::Escape {
                    if let Phase::Playing(game) = &mut self.phase {
                        if game.trade.is_some() {
                            game.trade_cancel();
                            return;
                        }
                    }
                    self.game_menu = !self.game_menu;
                    self.keys.clear();
                    return;
                }
                if pressed && !event.repeat && matches!(self.phase, Phase::Playing(_)) {
                    use options::Bind;
                    let bind = self.settings.bind_of(code).or((code == KeyCode::NumpadEnter).then_some(Bind::Chat));
                    if matches!(bind, Some(Bind::ChatUp | Bind::ChatDown)) {
                        self.scroll_chat(bind == Some(Bind::ChatUp));
                        return;
                    }
                    if bind != Some(Bind::Tell) {
                        if let Phase::Playing(game) = &self.phase {
                            self.teller = game.tellers.len();
                        }
                    }
                    // TextBox's keys: chat, a command, a tell (Tab cycles who to), guild and party.
                    let open = match bind {
                        Some(Bind::Chat) => Some(String::new()),
                        Some(Bind::Command) => Some("/".to_owned()),
                        Some(Bind::Tell) => Some(self.tell_prefix()),
                        Some(Bind::GuildChat) => Some("/g ".to_owned()),
                        Some(Bind::PartyChat) => Some("/p ".to_owned()),
                        _ => None,
                    };
                    if open.is_some() {
                        self.chat_input = open.map(chat_line);
                        self.keys.clear();
                        self.mouse_down = false;
                        return;
                    }
                }
                {
                    match event.state {
                        ElementState::Pressed => {
                            if !event.repeat {
                                self.pressed.push(code);
                            }
                            self.keys.insert(code);
                        }
                        ElementState::Released => {
                            self.keys.remove(&code);
                        }
                    }
                }
            }
            WindowEvent::Focused(false) => {
                self.keys.clear();
                self.mouse_down = false;
            }
            // A --mouse test position stays put whatever the real cursor does.
            WindowEvent::CursorMoved { position, .. } if !self.mouse_fixed => self.mouse = (position.x, position.y),
            // The mouse wheel zooms the minimap, a level a notch, when no window is open.
            WindowEvent::MouseWheel { delta, .. } if self.changelog.open => {
                let lines = match delta {
                    winit::event::MouseScrollDelta::LineDelta(_, y) => y,
                    winit::event::MouseScrollDelta::PixelDelta(p) => (p.y / 40.0) as f32,
                };
                self.changelog.wheel(lines.round() as i32);
            }
            // On the character list it scrolls the characters.
            WindowEvent::MouseWheel { delta, .. } if matches!(self.phase, Phase::Menu) => {
                let lines = match delta {
                    winit::event::MouseScrollDelta::LineDelta(_, y) => y,
                    winit::event::MouseScrollDelta::PixelDelta(p) => (p.y / 40.0) as f32,
                };
                self.wheel += lines;
                let notches = self.wheel.trunc();
                self.wheel -= notches;
                self.front.wheel(notches as i32);
            }
            WindowEvent::MouseWheel { delta, .. } if self.forge.open && matches!(self.phase, Phase::Playing(_)) => {
                // The forge's recipes scroll a row a notch.
                let lines = match delta {
                    winit::event::MouseScrollDelta::LineDelta(_, y) => y,
                    winit::event::MouseScrollDelta::PixelDelta(p) => (p.y / 40.0) as f32,
                };
                self.wheel += lines;
                let notches = self.wheel.trunc();
                self.wheel -= notches;
                self.forge.scroll(-notches as i32);
            }
            WindowEvent::MouseWheel { delta, .. } => {
                let free = matches!(self.phase, Phase::Playing(_))
                    && !(self.game_menu || self.options_ui.open || self.market.open || self.npcs.window.is_some());
                if free {
                    let lines = match delta {
                        winit::event::MouseScrollDelta::LineDelta(_, y) => y,
                        winit::event::MouseScrollDelta::PixelDelta(p) => (p.y / 40.0) as f32,
                    };
                    self.wheel += lines;
                    while self.wheel >= 1.0 {
                        self.wheel -= 1.0;
                        self.hud.minimap_zoom = self.hud.minimap_zoom.saturating_sub(1);
                    }
                    while self.wheel <= -1.0 {
                        self.wheel += 1.0;
                        self.hud.minimap_zoom += 1;
                    }
                }
            }
            WindowEvent::MouseInput { .. } if self.mouse_fixed => {}
            WindowEvent::MouseInput { state: ElementState::Pressed, button: winit::event::MouseButton::Right, .. } => {
                self.right_clicks.push(self.mouse);
            }
            WindowEvent::MouseInput { state, button: winit::event::MouseButton::Left, .. } => {
                if let Some(a) = &mut self.audio {
                    a.wake();
                }
                // Handled in the frame, where the HUD's layout is known.
                self.clicks.push((state == ElementState::Pressed, self.mouse));
            }
            WindowEvent::RedrawRequested => {
                self.frame();
                if self.quit {
                    if let Phase::Playing(game) = &mut self.phase {
                        if let Some(me) = game.world.as_ref().and_then(|w| w.player()) {
                            log::info!("player at ({:.2}, {:.2}); {}", me.x, me.y, if game.ended { &game.status } else { "connected" });
                        }
                        game.close();
                    }
                    event_loop.exit();
                    return;
                }
                if let Some(window) = &self.window {
                    window.request_redraw();
                }
            }
            _ => {}
        }
    }
}

fn main() {
    #[cfg(not(target_arch = "wasm32"))]
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info,wgpu_core=warn,wgpu_hal=warn"))
        .init();
    #[cfg(target_arch = "wasm32")]
    {
        std::panic::set_hook(Box::new(console_error_panic_hook::hook));
        textedit::clipboard::listen();
        web::listen();
        let _ = console_log::init_with_level(log::Level::Info);
    }

    let event_loop = EventLoop::<UserEvent>::with_user_event().build().expect("event loop");
    let app = App {
        proxy: event_loop.create_proxy(),
        window: None,
        loaded: None,
        phase: Phase::Menu,
        started: Instant::now(),
        last_frame: 0,
        keys: HashSet::new(),
        fps_frames: 0,
        fps_since: Instant::now(),
        fps_shown: 0.0,
        frame_start: Instant::now(),
        bench: std::env::args().any(|a| a == "--bench").then(|| {
            prof::enable();
            Bench::default()
        }),
        frame_start_bench: Instant::now(),
        wheel: 0.0,
        last_title: String::new(),
        shot: shot_args(),
        quit: false,
        holds: hold_args(),
        holds_from: None,
        last_scripted: None,
        pressed: Vec::new(),
        chat_input: None,
        chat_scroll: None,
        teller: 0,
        chat_names: Vec::new(),
        chat_send: Vec::new(),
        say: say_lines(),
        say_sent_in: None,
        say_given: (!say_arg().is_empty()).then_some(()),
        alt_held: false,
        shift_held: false,
        ctrl_held: false,
        right_clicks: Vec::new(),
        hud: Default::default(),
        cursor_hidden: false,
        clicks: Vec::new(),
        map_colors: Default::default(),
        walk_to: walk_to_arg().first().copied(),
        walk_more: walk_to_arg().into_iter().skip(1).collect(),
        mouse: mouse_arg().unwrap_or((0.0, 0.0)),
        mouse_fixed: mouse_arg().is_some() || !mouse_script_arg().is_empty(),
        mouse_script: mouse_script_arg(),
        mouse_down: false,
        autofire: false,
        script_aim: aim_arg(),
        front: Default::default(),
        endpoint: None,
        account: None,
        test_login: false,
        guest: false,
        request: None,
        front_keys: Vec::new(),
        game_menu: false,
        menu_buttons: Vec::new(),
        npcs: Default::default(),
        npc_request: None,
        signs: signs::Signs::default(),
        npc_keys: Vec::new(),
        audio: None,
        settings: options::Settings::load(),
        options_ui: Default::default(),
        market: Default::default(),
        forge: Default::default(),
        forge_acts: Vec::new(),
        type_text: text_arg("--type"),
        npc_test: text_arg("--npc"),
        effect_test: text_arg("--effect").and_then(|t| t.parse().ok()).map(|t| (t, i32::MIN / 2)),
        changelog: Default::default(),
        noted_version: false,
        #[cfg(not(target_arch = "wasm32"))]
        updater: None,
        #[cfg(not(target_arch = "wasm32"))]
        handover: None,
        #[cfg(not(target_arch = "wasm32"))]
        handover_failed: false,
        #[cfg(not(target_arch = "wasm32"))]
        update_told: String::new(),
        #[cfg(not(target_arch = "wasm32"))]
        resume: update::read_resume(),
        #[cfg(not(target_arch = "wasm32"))]
        show_window: None,
    };
    #[cfg(not(target_arch = "wasm32"))]
    {
        let mut app = app;
        event_loop.run_app(&mut app).expect("event loop");
    }
    #[cfg(target_arch = "wasm32")]
    {
        use winit::platform::web::EventLoopExtWebSys;
        event_loop.spawn_app(app);
    }
}
