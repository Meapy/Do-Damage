//! Options (`Options`, `Parameters`): key bindings, graphics and sound, kept between runs (in
//! `settings.txt` beside the remembered login, or the browser's storage).
//!
//! The window opens from the Escape menu or its key (O). On the Controls tab, click a binding
//! and press the key to use instead, or the mouse's side button 4 or 5 (Escape cancels, Backspace
//! clears it).

use crate::audio::SoundSettings;
use crate::crosshair::{self, Look, Style};
use crate::hud::{Layout, Ui};
use crate::i18n::{self, mark, tr};
use crate::theme::{self, color, Kind};
use winit::keyboard::KeyCode;

/// Something a key does (`Parameters.setDefaultKey`).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Bind {
    MoveUp,
    MoveLeft,
    MoveDown,
    MoveRight,
    RotateLeft,
    RotateRight,
    ResetCamera,
    Ability,
    Interact,
    Nexus,
    Autofire,
    HealthPotion,
    MagicPotion,
    /// Inventory slots 1-8.
    Slot(u8),
    Chat,
    Command,
    Tell,
    GuildChat,
    PartyChat,
    ChatUp,
    ChatDown,
    ZoomIn,
    ZoomOut,
    Options,
    /// The quest log: quests, daily rewards and achievements.
    Quests,
    /// The vault window: every vault chest's items at once.
    VaultView,
    /// `switchTabs`: the inventory or the backpack.
    SwitchTabs,
    /// `toggleCentering`: the player in the middle of the view, or a little below.
    Center,
    /// `togglePerformanceStats`: the frame rate on screen.
    Stats,
    /// Held: the items in the loot bags nearby, over each bag.
    BagPreview,
    /// Chat commands: `/vault`, `/realm`, `/ghall`, `/pinviteworld`, `/pjoin`.
    Vault,
    Realm,
    GuildHall,
    PartyInviteWorld,
    PartyJoinWorld,
}

/// Every binding: what, its label, its name in the file, its default key.
pub const BINDS: [(Bind, &str, &str, Option<KeyCode>); 42] = [
    (Bind::MoveUp, mark("Move up"), "moveUp", Some(KeyCode::KeyW)),
    (Bind::MoveLeft, mark("Move left"), "moveLeft", Some(KeyCode::KeyA)),
    (Bind::MoveDown, mark("Move down"), "moveDown", Some(KeyCode::KeyS)),
    (Bind::MoveRight, mark("Move right"), "moveRight", Some(KeyCode::KeyD)),
    (Bind::RotateLeft, mark("Rotate left"), "rotateLeft", Some(KeyCode::KeyQ)),
    (Bind::RotateRight, mark("Rotate right"), "rotateRight", Some(KeyCode::KeyE)),
    (Bind::ResetCamera, mark("Reset camera"), "resetCamera", Some(KeyCode::KeyZ)),
    (Bind::Ability, mark("Use ability"), "useSpecial", Some(KeyCode::Space)),
    (Bind::Interact, mark("Interact"), "interact", Some(KeyCode::Digit0)),
    (Bind::Nexus, mark("Nexus"), "escapeToNexus", Some(KeyCode::KeyR)),
    (Bind::Autofire, mark("Autofire"), "autofireToggle", Some(KeyCode::KeyI)),
    (Bind::HealthPotion, mark("Health potion"), "useHealthPotion", Some(KeyCode::KeyF)),
    (Bind::MagicPotion, mark("Magic potion"), "useMagicPotion", Some(KeyCode::KeyV)),
    (Bind::Slot(1), mark("Item 1"), "useInvSlot1", Some(KeyCode::Digit1)),
    (Bind::Slot(2), mark("Item 2"), "useInvSlot2", Some(KeyCode::Digit2)),
    (Bind::Slot(3), mark("Item 3"), "useInvSlot3", Some(KeyCode::Digit3)),
    (Bind::Slot(4), mark("Item 4"), "useInvSlot4", Some(KeyCode::Digit4)),
    (Bind::Slot(5), mark("Item 5"), "useInvSlot5", Some(KeyCode::Digit5)),
    (Bind::Slot(6), mark("Item 6"), "useInvSlot6", Some(KeyCode::Digit6)),
    (Bind::Slot(7), mark("Item 7"), "useInvSlot7", Some(KeyCode::Digit7)),
    (Bind::Slot(8), mark("Item 8"), "useInvSlot8", Some(KeyCode::Digit8)),
    (Bind::Chat, mark("Chat"), "chat", Some(KeyCode::Enter)),
    (Bind::Command, mark("Command"), "chatCommand", Some(KeyCode::Slash)),
    (Bind::Tell, mark("Whisper"), "tell", Some(KeyCode::Tab)),
    (Bind::GuildChat, mark("Guild chat"), "guildChat", Some(KeyCode::KeyG)),
    (Bind::PartyChat, mark("Party chat"), "partyChat", Some(KeyCode::KeyP)),
    (Bind::ChatUp, mark("Chat history up"), "scrollChatUp", Some(KeyCode::PageUp)),
    (Bind::ChatDown, mark("Chat history down"), "scrollChatDown", Some(KeyCode::PageDown)),
    (Bind::ZoomIn, mark("Minimap zoom in"), "miniMapZoomIn", Some(KeyCode::Equal)),
    (Bind::ZoomOut, mark("Minimap zoom out"), "miniMapZoomOut", Some(KeyCode::Minus)),
    (Bind::Options, mark("Options"), "options", Some(KeyCode::KeyO)),
    (Bind::Quests, mark("Quest log"), "questLog", Some(KeyCode::KeyJ)),
    (Bind::VaultView, mark("Vault contents"), "vaultView", Some(KeyCode::KeyK)),
    (Bind::SwitchTabs, mark("Inventory / backpack"), "switchTabs", Some(KeyCode::KeyB)),
    (Bind::Center, mark("Center on player"), "toggleCentering", Some(KeyCode::KeyX)),
    (Bind::Stats, mark("Show frame rate"), "togglePerformanceStats", Some(KeyCode::KeyL)),
    (Bind::BagPreview, mark("Show loot bags' items (hold)"), "bagPreview", Some(KeyCode::ShiftLeft)),
    (Bind::Vault, mark("Go to the vault"), "reconVault", None),
    (Bind::Realm, mark("Go to a realm"), "reconRealm", None),
    (Bind::GuildHall, mark("Go to the guild hall"), "reconGuildHall", None),
    (Bind::PartyInviteWorld, mark("Invite the world to party"), "partyInviteWorld", None),
    (Bind::PartyJoinWorld, mark("Join a world party"), "partyJoinWorld", None),
];

/// The keys that can be bound, for reading the file back.
const KEYS: &[KeyCode] = &[
    KeyCode::KeyA, KeyCode::KeyB, KeyCode::KeyC, KeyCode::KeyD, KeyCode::KeyE, KeyCode::KeyF, KeyCode::KeyG,
    KeyCode::KeyH, KeyCode::KeyI, KeyCode::KeyJ, KeyCode::KeyK, KeyCode::KeyL, KeyCode::KeyM, KeyCode::KeyN,
    KeyCode::KeyO, KeyCode::KeyP, KeyCode::KeyQ, KeyCode::KeyR, KeyCode::KeyS, KeyCode::KeyT, KeyCode::KeyU,
    KeyCode::KeyV, KeyCode::KeyW, KeyCode::KeyX, KeyCode::KeyY, KeyCode::KeyZ,
    KeyCode::Digit0, KeyCode::Digit1, KeyCode::Digit2, KeyCode::Digit3, KeyCode::Digit4, KeyCode::Digit5,
    KeyCode::Digit6, KeyCode::Digit7, KeyCode::Digit8, KeyCode::Digit9,
    KeyCode::Numpad0, KeyCode::Numpad1, KeyCode::Numpad2, KeyCode::Numpad3, KeyCode::Numpad4, KeyCode::Numpad5,
    KeyCode::Numpad6, KeyCode::Numpad7, KeyCode::Numpad8, KeyCode::Numpad9, KeyCode::NumpadAdd,
    KeyCode::NumpadSubtract, KeyCode::NumpadMultiply, KeyCode::NumpadDivide, KeyCode::NumpadDecimal, KeyCode::NumpadEnter,
    KeyCode::F1, KeyCode::F2, KeyCode::F3, KeyCode::F4, KeyCode::F5, KeyCode::F6, KeyCode::F7, KeyCode::F8,
    KeyCode::F9, KeyCode::F10, KeyCode::F12,
    KeyCode::ArrowUp, KeyCode::ArrowDown, KeyCode::ArrowLeft, KeyCode::ArrowRight,
    KeyCode::Space, KeyCode::Tab, KeyCode::Enter, KeyCode::Backquote, KeyCode::Minus, KeyCode::Equal,
    KeyCode::BracketLeft, KeyCode::BracketRight, KeyCode::Backslash, KeyCode::Semicolon, KeyCode::Quote,
    KeyCode::Comma, KeyCode::Period, KeyCode::Slash, KeyCode::ShiftLeft, KeyCode::ShiftRight,
    KeyCode::ControlLeft, KeyCode::ControlRight, KeyCode::AltLeft, KeyCode::AltRight, KeyCode::CapsLock,
    KeyCode::PageUp, KeyCode::PageDown, KeyCode::Home, KeyCode::End, KeyCode::Insert, KeyCode::Delete,
    KeyCode::BrowserBack, KeyCode::BrowserForward,
];

/// The mouse buttons that bind as keys, and the key each stands for: the side buttons 4 and 5,
/// which the systems already treat as the browser's Back and Forward. A keyboard's own Back and
/// Forward keys do the same.
pub const MOUSE_KEYS: [(winit::event::MouseButton, KeyCode); 2] = [
    (winit::event::MouseButton::Back, KeyCode::BrowserBack),
    (winit::event::MouseButton::Forward, KeyCode::BrowserForward),
];

/// The key a mouse button binds as, if it binds (`MOUSE_KEYS`).
pub fn mouse_key(button: winit::event::MouseButton) -> Option<KeyCode> {
    MOUSE_KEYS.iter().find(|(b, _)| *b == button).map(|&(_, k)| k)
}

fn key_name(k: KeyCode) -> String {
    format!("{k:?}")
}

fn parse_key(name: &str) -> Option<KeyCode> {
    KEYS.iter().copied().find(|k| key_name(*k) == name)
}

/// A key as the player knows it: "W", "1", "Space", "Page Up".
pub fn key_label(k: Option<KeyCode>) -> String {
    let Some(k) = k else { return tr("key::None").into() };
    let name = key_name(k);
    if let Some(c) = name.strip_prefix("Key").or_else(|| name.strip_prefix("Digit")) {
        return c.to_owned();
    }
    match k {
        KeyCode::ArrowUp => tr("key::Up").into(),
        KeyCode::ArrowDown => tr("key::Down").into(),
        KeyCode::ArrowLeft => tr("key::Left").into(),
        KeyCode::ArrowRight => tr("key::Right").into(),
        KeyCode::PageUp => tr("key::Page Up").into(),
        KeyCode::PageDown => tr("key::Page Down").into(),
        KeyCode::Space => tr("key::Space").into(),
        KeyCode::Enter => tr("key::Enter").into(),
        KeyCode::NumpadEnter => tr("key::Num Enter").into(),
        KeyCode::Tab => tr("key::Tab").into(),
        KeyCode::CapsLock => tr("key::Caps Lock").into(),
        KeyCode::Home => tr("key::Home").into(),
        KeyCode::End => tr("key::End").into(),
        KeyCode::Insert => tr("key::Insert").into(),
        KeyCode::Delete => tr("key::Delete").into(),
        KeyCode::ShiftLeft => tr("key::Shift L").into(),
        KeyCode::ShiftRight => tr("key::Shift R").into(),
        KeyCode::ControlLeft => tr("key::Ctrl L").into(),
        KeyCode::ControlRight => tr("key::Ctrl R").into(),
        KeyCode::AltLeft => tr("key::Alt L").into(),
        KeyCode::AltRight => tr("key::Alt R").into(),
        KeyCode::BrowserBack => tr("key::Mouse 4").into(),
        KeyCode::BrowserForward => tr("key::Mouse 5").into(),
        KeyCode::Minus => "-".into(),
        KeyCode::Equal => "=".into(),
        KeyCode::Slash => "/".into(),
        KeyCode::Backquote => "`".into(),
        KeyCode::Comma => ",".into(),
        KeyCode::Period => ".".into(),
        KeyCode::Semicolon => ";".into(),
        KeyCode::Quote => "'".into(),
        KeyCode::BracketLeft => "[".into(),
        KeyCode::BracketRight => "]".into(),
        KeyCode::Backslash => "\\".into(),
        _ => name.replace("Numpad", "Num ").replace("Left", " L").replace("Right", " R"),
    }
}

/// What can be switched on and off on the Graphics, Visuals and Sound tabs, by name in the file.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Toggle {
    Particles,
    Shadows,
    HpBars,
    AllyShots,
    PetShots,
    PetEffects,
    Crosshair,
    DamageNumbers,
    Rotation,
    Centered,
    ShowFps,
    UnlockFps,
    Fullscreen,
    Glow,
    Vivid,
    Hdr,
    Sfx,
    Shots,
    Music,
    PingSounds,
    AutoDrink,
    OthersDamage,
}

/// What's shown in the world (and the speech balloons' list, `BUBBLE_KINDS`, above them).
const VISUALS: [(Toggle, &str); 8] = [
    (Toggle::Particles, mark("Particles and effects")),
    (Toggle::Shadows, mark("Shadows")),
    (Toggle::HpBars, mark("Health bars")),
    (Toggle::AllyShots, mark("Other players' shots and spells")),
    (Toggle::PetShots, mark("Other players' pets' shots")),
    (Toggle::PetEffects, mark("Other players' pets' effects")),
    (Toggle::Crosshair, mark("Crosshair")),
    (Toggle::DamageNumbers, mark("Damage numbers")),
];

/// How pets' shots (yours and other players') are drawn. Their hits and damage are the same
/// whichever: only the look changes.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum PetShots {
    /// As big and as solid as the pet's XML makes them (a boss pet's are its boss's).
    Normal,
    /// Smaller and see-through, so they don't hide the fight.
    #[default]
    Subtle,
    Hidden,
}

impl PetShots {
    /// In the slider's order, left to right.
    const ALL: [PetShots; 3] = [PetShots::Hidden, PetShots::Subtle, PetShots::Normal];

    fn name(self) -> &'static str {
        match self {
            PetShots::Normal => "Normal",
            PetShots::Subtle => "Subtle",
            PetShots::Hidden => "Hidden",
        }
    }

    /// As the slider shows it.
    fn label(self) -> &'static str {
        match self {
            PetShots::Normal => tr("Normal"),
            PetShots::Subtle => tr("Subtle"),
            PetShots::Hidden => tr("Hidden"),
        }
    }

    fn from_name(name: &str) -> Option<Self> {
        PetShots::ALL.into_iter().find(|p| p.name().eq_ignore_ascii_case(name))
    }

    /// How big (times their own size) and how opaque a pet's shots are drawn: other players'
    /// pets' fainter than your own.
    pub fn look(self, mine: bool) -> (f32, f32) {
        match self {
            PetShots::Normal => (1.0, 1.0),
            PetShots::Subtle if mine => (0.6, 0.55),
            PetShots::Subtle => (0.6, 0.35),
            PetShots::Hidden => (0.0, 0.0),
        }
    }
}

/// The camera and the display. The browser leaves fullscreen to the page (F11 still asks it).
const GRAPHICS: [(Toggle, &str); 8] = [
    (Toggle::Glow, mark("Glow on bright light")),
    (Toggle::Vivid, mark("Vivid colours")),
    (Toggle::Hdr, mark("HDR (Windows HDR displays)")),
    (Toggle::Rotation, mark("Camera rotation")),
    (Toggle::Centered, mark("Center on player")),
    (Toggle::ShowFps, mark("Show frame rate")),
    (Toggle::UnlockFps, mark("Unlock frame rate (no vsync)")),
    (Toggle::Fullscreen, mark("Borderless fullscreen (F11)")),
];

/// How the game plays for you.
const GAMEPLAY: [(Toggle, &str); 2] = [
    (Toggle::AutoDrink, mark("Drink stat potions from loot bags")),
    (Toggle::OthersDamage, mark("Other players' damage numbers")),
];

/// Whose speech balloons show (`Settings::bubbles`, a bit each): the Visuals tab's list.
pub mod bubble {
    pub const PLAYERS: u8 = 1;
    pub const GUILD: u8 = 2;
    pub const PARTY: u8 = 4;
    pub const WHISPERS: u8 = 8;
    pub const MONSTERS: u8 = 16;
    pub const NPCS: u8 = 32;
    pub const ALL: u8 = 63;
}

/// The speech balloons' list, in its order: each kind and its name.
const BUBBLE_KINDS: [(u8, &str); 6] = [
    (bubble::PLAYERS, mark("Players' chat")),
    (bubble::GUILD, mark("Guild chat")),
    (bubble::PARTY, mark("Party chat")),
    (bubble::WHISPERS, mark("Whispers")),
    (bubble::MONSTERS, mark("Monsters")),
    (bubble::NPCS, mark("Characters in town")),
];

const SOUNDS: [(Toggle, &str); 4] =
    [(Toggle::Sfx, mark("Sound effects")), (Toggle::Shots, mark("Weapon sounds")), (Toggle::Music, mark("Music")), (Toggle::PingSounds, mark("Ping sounds"))];

/// Whose pings show (pings.rs): our own always do.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum PingsFrom {
    Nobody,
    /// Our party's and our guild's.
    Friends,
    /// Everyone's the server sends: those near us or the ping, and our party and guild.
    #[default]
    Everyone,
}

impl PingsFrom {
    /// In the slider's order, left to right.
    const ALL: [PingsFrom; 3] = [PingsFrom::Nobody, PingsFrom::Friends, PingsFrom::Everyone];

    fn name(self) -> &'static str {
        match self {
            PingsFrom::Nobody => "Nobody",
            PingsFrom::Friends => "Friends",
            PingsFrom::Everyone => "Everyone",
        }
    }

    /// As the slider shows it.
    fn label(self) -> &'static str {
        match self {
            PingsFrom::Nobody => tr("Nobody"),
            PingsFrom::Friends => tr("Party and guild"),
            PingsFrom::Everyone => tr("Everyone"),
        }
    }

    fn from_name(name: &str) -> Option<Self> {
        PingsFrom::ALL.into_iter().find(|p| p.name().eq_ignore_ascii_case(name))
    }

    /// Whether a ping from someone who is this to us shows (`PingShow.relation`: 0 someone
    /// nearby, 1 our guild, 2 our party, 3 ourselves).
    pub fn shows(self, relation: u8) -> bool {
        match self {
            PingsFrom::Nobody => relation == 3,
            PingsFrom::Friends => relation >= 1,
            PingsFrom::Everyone => true,
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Settings {
    keys: Vec<Option<KeyCode>>,
    pub sound: SoundSettings,
    pub particles: bool,
    pub shadows: bool,
    pub hp_bars: bool,
    /// Whose speech balloons show: `bubble`'s bits (none, all, or some: only guild and party).
    pub bubbles: u8,
    /// Other players' projectiles, and their spells' effects.
    pub ally_shots: bool,
    /// Damage numbers from other players' hits (on a boss, say), so it's clear when we hit.
    pub others_damage: bool,
    /// Other players' pets' projectiles, and their pets' particle effects.
    pub pet_shots: bool,
    pub pet_effects: bool,
    /// How every pet's shots look.
    pub pet_shot_look: PetShots,
    /// Whose pings show, and whether they make a sound.
    pub pings_from: PingsFrom,
    pub ping_sounds: bool,
    /// The stat potions in a loot bag you stand on are drunk by themselves (`auto_drink` in main.rs).
    pub auto_drink: bool,
    /// A crosshair at the mouse over the world, instead of the pointer.
    pub crosshair: bool,
    /// Its style, colour and size, and the player's own (the Crosshair tab).
    pub crosshair_look: Look,
    /// How opaque other players, and other players' pets, are drawn (0 to 1).
    pub player_alpha: f32,
    pub pet_alpha: f32,
    pub damage_numbers: bool,
    pub rotation: bool,
    /// `centerOnPlayer`: the player a little below the middle of the view (on), or three
    /// quarters of the way down so more of what's ahead shows (off).
    pub centered: bool,
    /// The frame rate on screen.
    pub show_fps: bool,
    /// Draw as many frames as the computer can, not waiting for the display (vsync off).
    pub unlock_fps: bool,
    /// How big the world is drawn: 1 is the AIR client's 50 pixels a tile; less shows more.
    pub zoom: f32,
    /// How big the chat is drawn, its text, width and height together (1: as it was).
    pub chat_size: f32,
    /// How dark the glass behind the chat is (0: none, 1: as it was).
    pub chat_alpha: f32,
    /// The most frames a second (0: no limit).
    pub max_fps: u32,
    /// The window covers its monitor, with no border (desktop; `Fullscreen::Borderless`).
    pub fullscreen: bool,
    /// A glow round bright colours in the world (shots, effects, portals, lava).
    pub glow: bool,
    /// Livelier colours and a little contrast in the world, its edges darker.
    pub vivid: bool,
    /// Draw for an HDR display, the glow brighter than white (desktop). It follows Windows'
    /// HDR setting until the player picks (`hdr_chosen`), and only the pick is kept.
    pub hdr: bool,
    pub hdr_chosen: bool,
    /// State kept as the AIR client keeps it in its options (`cameraAngle`,
    /// `preferredServer`): the camera's turn, and the server last chosen.
    pub camera_angle: Option<f64>,
    pub preferred_server: String,
    /// The last version the player was told about (the "updated" note shows once a version).
    pub seen_version: String,
    /// The language picked in Options (`i18n::LANGUAGES`' code), or the system's when empty.
    pub language: String,
    /// Lines this version doesn't know (from a newer or older one), kept as they were, and
    /// keys it couldn't read, kept as written.
    extra: Vec<String>,
    raw_keys: Vec<Option<String>>,
}

/// The frame rate limits offered (0: none).
pub const MAX_FPS: [u32; 8] = [0, 30, 60, 120, 144, 165, 240, 360];

/// The world zoom's range (`Settings::zoom`).
pub const ZOOM_MIN: f32 = 0.9;
pub const ZOOM_MAX: f32 = 1.3;

/// The chat size's range (`Settings::chat_size`).
pub const CHAT_MIN: f32 = 0.5;
pub const CHAT_MAX: f32 = 1.5;

impl Default for Settings {
    fn default() -> Self {
        Settings {
            keys: BINDS.iter().map(|b| b.3).collect(),
            sound: SoundSettings::default(),
            particles: true,
            shadows: true,
            hp_bars: true,
            bubbles: bubble::ALL,
            ally_shots: true,
            others_damage: false,
            pet_shots: true,
            pet_effects: true,
            pet_shot_look: PetShots::Subtle,
            pings_from: PingsFrom::Everyone,
            ping_sounds: true,
            auto_drink: false,
            crosshair: true,
            crosshair_look: Look::default(),
            player_alpha: 1.0,
            pet_alpha: 1.0,
            damage_numbers: true,
            rotation: true,
            centered: true,
            show_fps: false,
            zoom: 1.0,
            chat_size: 1.0,
            chat_alpha: 1.0,
            unlock_fps: false,
            max_fps: 0,
            fullscreen: false,
            glow: true,
            vivid: true,
            hdr: crate::hdr::windows_hdr().is_some(),
            hdr_chosen: false,
            camera_angle: None,
            preferred_server: String::new(),
            seen_version: String::new(),
            language: String::new(),
            extra: Vec::new(),
            raw_keys: vec![None; BINDS.len()],
        }
    }
}

impl Settings {
    /// The key for `b`, if it has one.
    pub fn key(&self, b: Bind) -> Option<KeyCode> {
        BINDS.iter().position(|x| x.0 == b).and_then(|i| self.keys[i])
    }

    /// What `code` does, if anything.
    pub fn bind_of(&self, code: KeyCode) -> Option<Bind> {
        self.keys.iter().position(|k| *k == Some(code)).map(|i| BINDS[i].0)
    }

    fn set_key(&mut self, b: Bind, code: Option<KeyCode>) {
        // A key does one thing: it leaves whatever had it.
        if let Some(c) = code {
            for k in &mut self.keys {
                if *k == Some(c) {
                    *k = None;
                }
            }
        }
        if let Some(i) = BINDS.iter().position(|x| x.0 == b) {
            self.keys[i] = code;
            self.raw_keys[i] = None;
        }
    }

    fn toggle(&mut self, t: Toggle) -> &mut bool {
        match t {
            Toggle::Particles => &mut self.particles,
            Toggle::Shadows => &mut self.shadows,
            Toggle::HpBars => &mut self.hp_bars,
            Toggle::AllyShots => &mut self.ally_shots,
            Toggle::PetShots => &mut self.pet_shots,
            Toggle::PetEffects => &mut self.pet_effects,
            Toggle::Crosshair => &mut self.crosshair,
            Toggle::DamageNumbers => &mut self.damage_numbers,
            Toggle::Rotation => &mut self.rotation,
            Toggle::Centered => &mut self.centered,
            Toggle::ShowFps => &mut self.show_fps,
            Toggle::UnlockFps => &mut self.unlock_fps,
            Toggle::Fullscreen => &mut self.fullscreen,
            Toggle::Glow => &mut self.glow,
            Toggle::Vivid => &mut self.vivid,
            Toggle::Hdr => &mut self.hdr,
            Toggle::Sfx => &mut self.sound.sfx,
            Toggle::Shots => &mut self.sound.shots,
            Toggle::Music => &mut self.sound.music,
            Toggle::PingSounds => &mut self.ping_sounds,
            Toggle::AutoDrink => &mut self.auto_drink,
            Toggle::OthersDamage => &mut self.others_damage,
        }
    }

    /// As kept: `name=value` lines.
    pub fn to_text(&self) -> String {
        let mut out = String::new();
        out += "version=1\n";
        for (i, (_, _, name, _)) in BINDS.iter().enumerate() {
            let value = match (self.keys[i], &self.raw_keys[i]) {
                (Some(k), _) => key_name(k),
                (None, Some(raw)) => raw.clone(),
                (None, None) => String::new(),
            };
            out += &format!("{name}={value}\n");
        }
        let mut flag = |n: &str, v: bool| out += &format!("{n}={}\n", v as u8);
        flag("particles", self.particles);
        flag("shadows", self.shadows);
        flag("hpBars", self.hp_bars);
        flag("allyShots", self.ally_shots);
        flag("petShots", self.pet_shots);
        flag("petEffects", self.pet_effects);
        flag("crosshair", self.crosshair);
        flag("damageNumbers", self.damage_numbers);
        flag("allowRotation", self.rotation);
        flag("cameraCentered", self.centered);
        flag("showStatistics", self.show_fps);
        flag("unlockFps", self.unlock_fps);
        flag("fullscreen", self.fullscreen);
        flag("glow", self.glow);
        flag("vividColours", self.vivid);
        if self.hdr_chosen {
            flag("hdr", self.hdr);
        }
        flag("playSFX", self.sound.sfx);
        flag("playPewPew", self.sound.shots);
        flag("playMusic", self.sound.music);
        flag("pingSounds", self.ping_sounds);
        flag("autoDrinkPotions", self.auto_drink);
        flag("othersDamageNumbers", self.others_damage);
        out += &format!("speechBalloons={}
", self.bubbles);
        out += &format!("masterVolume={}\nsfxVolume={}\nmusicVolume={}\n", self.sound.master_volume, self.sound.sfx_volume, self.sound.music_volume);
        out += &format!("worldZoom={}\n", self.zoom);
        out += &format!("chatSize={}\nchatBackground={}\n", self.chat_size, self.chat_alpha);
        out += &format!("playerOpacity={}\npetOpacity={}\n", self.player_alpha, self.pet_alpha);
        out += &format!("maxFps={}\n", self.max_fps);
        out += &format!("petShotLook={}\n", self.pet_shot_look.name());
        out += &format!("pingsFrom={}\n", self.pings_from.name());
        let look = &self.crosshair_look;
        out += &format!("crosshairStyle={}\ncrosshairColour={}\ncrosshairSize={}\n", look.style.name(), look.colour, look.size);
        out += &format!("crosshairCustom={}\n", look.custom_text());
        if let Some(a) = self.camera_angle {
            out += &format!("cameraAngle={a}\n");
        }
        if !self.preferred_server.is_empty() {
            out += &format!("preferredServer={}\n", self.preferred_server);
        }
        if !self.seen_version.is_empty() {
            out += &format!("seenVersion={}\n", self.seen_version);
        }
        if !self.language.is_empty() {
            out += &format!("language={}\n", self.language);
        }
        for line in &self.extra {
            out += line;
            out += "\n";
        }
        out
    }

    pub fn from_text(text: &str) -> Self {
        let mut s = Settings::default();
        // Bindings the file sets; the others keep their defaults unless the file gave
        // their key to something else.
        let mut set = vec![false; BINDS.len()];
        for line in text.lines() {
            let Some((name, value)) = line.split_once('=') else { continue };
            let (name, value) = (name.trim(), value.trim());
            if let Some(i) = BINDS.iter().position(|b| b.2 == name) {
                set[i] = true;
                s.keys[i] = parse_key(value);
                if s.keys[i].is_none() && !value.is_empty() {
                    s.raw_keys[i] = Some(value.to_owned());
                }
                continue;
            }
            let on = value == "1";
            let vol = || value.parse::<f32>().ok().map(|v| v.clamp(0.0, 1.0));
            match name {
                "particles" => s.particles = on,
                "shadows" => s.shadows = on,
                "glow" => s.glow = on,
                "vividColours" => s.vivid = on,
                "hdr" => (s.hdr, s.hdr_chosen) = (on, true),
                "hpBars" => s.hp_bars = on,
                // before 2.5.1 one switch for every balloon
                "textBubbles" => s.bubbles = if on { bubble::ALL } else { 0 },
                "speechBalloons" => s.bubbles = value.parse::<u8>().map_or(bubble::ALL, |m| m & bubble::ALL),
                "othersDamageNumbers" => s.others_damage = on,
                "allyShots" => s.ally_shots = on,
                "petShots" => s.pet_shots = on,
                "petEffects" => s.pet_effects = on,
                "crosshair" => s.crosshair = on,
                "crosshairStyle" => s.crosshair_look.style = Style::from_name(value).unwrap_or_default(),
                "crosshairColour" => s.crosshair_look.colour = value.parse().ok().filter(|&c| c < crosshair::COLOURS.len()).unwrap_or(0),
                "crosshairSize" => s.crosshair_look.size = value.parse::<f32>().map_or(1.0, |z| z.clamp(crosshair::SIZE_MIN, crosshair::SIZE_MAX)),
                "crosshairCustom" => {
                    if let Some(g) = Look::parse_custom(value) {
                        s.crosshair_look.custom = g;
                    }
                }
                "playerOpacity" => s.player_alpha = vol().unwrap_or(1.0),
                "petOpacity" => s.pet_alpha = vol().unwrap_or(1.0),
                "petShotLook" => s.pet_shot_look = PetShots::from_name(value).unwrap_or_default(),
                "damageNumbers" => s.damage_numbers = on,
                "allowRotation" => s.rotation = on,
                "cameraCentered" => s.centered = on,
                "showStatistics" => s.show_fps = on,
                "unlockFps" => s.unlock_fps = on,
                "fullscreen" => s.fullscreen = on,
                "playSFX" => s.sound.sfx = on,
                "playPewPew" => s.sound.shots = on,
                "playMusic" => s.sound.music = on,
                "pingSounds" => s.ping_sounds = on,
                "autoDrinkPotions" => s.auto_drink = on,
                "pingsFrom" => s.pings_from = PingsFrom::from_name(value).unwrap_or_default(),
                "masterVolume" => s.sound.master_volume = vol().unwrap_or(s.sound.master_volume),
                "sfxVolume" => s.sound.sfx_volume = vol().unwrap_or(s.sound.sfx_volume),
                "musicVolume" => s.sound.music_volume = vol().unwrap_or(s.sound.music_volume),
                "worldZoom" => s.zoom = value.parse::<f32>().map_or(1.0, |z| z.clamp(ZOOM_MIN, ZOOM_MAX)),
                "chatSize" => s.chat_size = value.parse::<f32>().map_or(1.0, |z| z.clamp(CHAT_MIN, CHAT_MAX)),
                "chatBackground" => s.chat_alpha = value.parse::<f32>().ok().filter(|a| a.is_finite()).map_or(1.0, |a| a.clamp(0.0, 1.0)),
                "maxFps" => s.max_fps = value.parse().unwrap_or(0),
                "cameraAngle" => s.camera_angle = value.parse().ok().filter(|a: &f64| a.is_finite()),
                "preferredServer" => s.preferred_server = value.to_owned(),
                "seenVersion" => s.seen_version = value.to_owned(),
                "language" => s.language = value.to_owned(),
                "version" => {}
                // Not this version's: kept for the version that wrote it.
                _ => s.extra.push(line.to_owned()),
            }
        }
        let taken: Vec<KeyCode> = (0..BINDS.len()).filter(|&i| set[i]).filter_map(|i| s.keys[i]).collect();
        for i in 0..BINDS.len() {
            if !set[i] && s.keys[i].is_some_and(|k| taken.contains(&k)) {
                s.keys[i] = None;
            }
        }
        s
    }

    pub fn load() -> Self {
        let s = crate::login::store::load("settings").map_or_else(Settings::default, |t| Settings::from_text(&t));
        s.note_keys();
        crate::i18n::apply(Some(&s.language).filter(|l| !l.is_empty()).map(|l| l.as_str()));
        s
    }

    pub fn save(&self) {
        crate::login::store::save("settings", Some(&self.to_text()));
        self.note_keys();
        crate::i18n::apply(Some(&self.language).filter(|l| !l.is_empty()).map(|l| l.as_str()));
    }

    /// In the browser, the keys the controls use stay the game's (web.rs).
    fn note_keys(&self) {
        #[cfg(target_arch = "wasm32")]
        crate::web::keys_in_use(self.keys.iter().flatten().copied());
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Tab {
    #[default]
    Controls,
    Gameplay,
    Graphics,
    Visuals,
    Crosshair,
    Sound,
    Language,
}

/// A slider on the Graphics, Visuals or Sound tab.
#[derive(Clone, Copy, Debug, PartialEq)]
enum Slider {
    Master,
    Sfx,
    Music,
    Zoom,
    MaxFps,
    PlayerAlpha,
    PetAlpha,
    PetShots,
    ChatSize,
    ChatAlpha,
    Pings,
    CrosshairSize,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Bind(Bind),
    Toggle(Toggle),
    /// The Crosshair tab: a style, a colour (`crosshair::COLOURS`), a cell of the player's own
    /// (row, column) on the grid drawn in the rectangle, and clearing it.
    Style(Style),
    Colour(usize),
    /// The colour to paint the player's own in (`crosshair::PALETTE`).
    Brush(usize),
    Cell(usize, usize, [f32; 4]),
    ClearCustom,
    /// The crosshair's share code to the clipboard, and a code from it.
    CopyCode,
    PasteCode,
    /// A language (`i18n::LANGUAGES`), or `None` for the system's.
    Language(Option<usize>),
    /// A slider (a volume, the zoom...) and its track.
    Slider(Slider, [f32; 4]),
    /// The speech balloons' list: its box (opens and closes it), a kind in it (`bubble`'s bit),
    /// and the list itself round them (a click there does nothing).
    BubbleMenu,
    BubbleKind(u8),
    BubbleList,
    Defaults,
    Close,
    Home,
}

/// What sharing a crosshair last did.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ShareNote {
    Copied,
    /// The browser is still handing the clipboard over.
    Waiting,
    Pasted,
    NotACode,
}

/// The Options window.
#[derive(Default)]
pub struct OptionsUi {
    pub open: bool,
    tab: Tab,
    /// The binding waiting for its new key.
    listening: Option<Bind>,
    /// The slider held down, and its track: it follows the mouse until the button comes up.
    dragging: Option<(Slider, [f32; 4])>,
    /// Painting the player's own crosshair: what the cells dragged over become, and the grid.
    painting: Option<(Option<usize>, [f32; 4])>,
    /// The colour the player's own is painted in (`crosshair::PALETTE`).
    brush: usize,
    /// What the last Copy code or Paste code did, shown under them.
    share_note: Option<ShareNote>,
    /// The speech balloons' list is open (the Visuals tab).
    bubble_menu: bool,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    /// "Return home" was pressed: back to the Nexus.
    pub go_home: bool,
    /// Opened in the game (not the main menu), where "Return home" means something.
    pub in_game: bool,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// What the speech balloons' box says: everyone's, none, or the kinds ticked.
fn bubble_summary(mask: u8) -> String {
    match mask & bubble::ALL {
        bubble::ALL => tr("Everyone").to_owned(),
        0 => tr("None").to_owned(),
        m => BUBBLE_KINDS.iter().filter(|(bit, _)| m & bit != 0).map(|(_, l)| tr(l)).collect::<Vec<_>>().join(", "),
    }
}

/// A small triangle pointing down (or up, `open`) at (cx, cy), `r` from its middle to a corner.
fn caret(ui: &mut Ui, cx: f32, cy: f32, r: f32, open: bool) {
    let white = ui.renderer.white();
    let c = bsk_render::rgb_f32(color::TEXT_DIM);
    let col = [c[0], c[1], c[2], 1.0];
    let d = if open { -1.0 } else { 1.0 };
    let pts = [[cx - r, cy - d * r * 0.5], [cx + r, cy - d * r * 0.5], [cx, cy + d * r * 0.6]];
    ui.renderer.triangle_colors(white, pts, [[0.5, 0.5]; 3], [col; 3]);
}

/// A slider set to where the mouse is along its track (in steps of 5%, or one frame rate limit).
fn set_slider(slider: Slider, track: [f32; 4], x: f64, settings: &mut Settings) {
    let f = ((x as f32 - track[0]) / track[2]).clamp(0.0, 1.0);
    let v = (f * 20.0).round() / 20.0;
    match slider {
        Slider::Master => settings.sound.master_volume = v,
        Slider::Music => settings.sound.music_volume = v,
        Slider::Sfx => settings.sound.sfx_volume = v,
        Slider::Zoom => settings.zoom = ZOOM_MIN + v * (ZOOM_MAX - ZOOM_MIN),
        Slider::PlayerAlpha => settings.player_alpha = v,
        Slider::PetAlpha => settings.pet_alpha = v,
        Slider::MaxFps => settings.max_fps = MAX_FPS[(f * (MAX_FPS.len() - 1) as f32).round() as usize],
        Slider::PetShots => settings.pet_shot_look = PetShots::ALL[(f * (PetShots::ALL.len() - 1) as f32).round() as usize],
        Slider::ChatSize => settings.chat_size = CHAT_MIN + v * (CHAT_MAX - CHAT_MIN),
        Slider::ChatAlpha => settings.chat_alpha = v,
        Slider::Pings => settings.pings_from = PingsFrom::ALL[(f * (PingsFrom::ALL.len() - 1) as f32).round() as usize],
        Slider::CrosshairSize => settings.crosshair_look.size = crosshair::SIZE_MIN + v * (crosshair::SIZE_MAX - crosshair::SIZE_MIN),
    }
}

/// A round colour swatch (`pair`: its colour and its middle's), ringed when picked.
fn swatch(ui: &mut Ui, r: [f32; 4], s: f32, (a, d): (u32, u32), picked: bool, hover: bool) {
    let rad = r[2] / 2.0;
    if picked {
        theme::fill(ui.renderer, [r[0] - 4.0 * s, r[1] - 4.0 * s, r[2] + 8.0 * s, r[3] + 8.0 * s], rad + 4.0 * s, color::ACCENT, 1.0);
    } else if hover {
        theme::fill(ui.renderer, [r[0] - 3.0 * s, r[1] - 3.0 * s, r[2] + 6.0 * s, r[3] + 6.0 * s], rad + 3.0 * s, color::BORDER_HI, 1.0);
    }
    // Edged so black and white both show on the panel.
    theme::fill(ui.renderer, [r[0] - 1.0, r[1] - 1.0, r[2] + 2.0, r[3] + 2.0], rad + 1.0, if a < 0x202020 { 0x7A7096 } else { 0x000000 }, 1.0);
    theme::fill(ui.renderer, r, rad, a, 1.0);
    if d != a {
        let dot = r[2] * 0.4;
        theme::fill(ui.renderer, [r[0] + (r[2] - dot) / 2.0, r[1] + (r[3] - dot) / 2.0, dot, dot], dot / 2.0, d, 1.0);
    }
}

/// The cell of the player's own crosshair under `pos`, on the grid drawn in `grid`.
fn grid_cell(grid: [f32; 4], pos: (f64, f64)) -> Option<(usize, usize)> {
    if !inside(grid, pos) {
        return None;
    }
    let n = crosshair::GRID as f32;
    let col = ((pos.0 as f32 - grid[0]) / grid[2] * n) as usize;
    let row = ((pos.1 as f32 - grid[1]) / grid[3] * n) as usize;
    Some((row.min(crosshair::GRID - 1), col.min(crosshair::GRID - 1)))
}

/// Paint a cell of the player's own crosshair, which then shows. True if anything changed.
fn paint(settings: &mut Settings, (row, col): (usize, usize), paint: Option<usize>) -> bool {
    let look = &mut settings.crosshair_look;
    let before = *look;
    look.set_cell(row, col, paint);
    look.style = Style::Custom;
    *look != before
}

impl OptionsUi {
    pub fn show(&mut self) {
        self.open = true;
        self.listening = None;
    }

    /// Text pasted while the window is open (Paste code, or Ctrl+V in the browser): on the
    /// Crosshair tab, a shared crosshair's code. True if the settings changed.
    pub fn paste(&mut self, text: &str, settings: &mut Settings) -> bool {
        if !self.open || self.tab != Tab::Crosshair {
            return false;
        }
        match settings.crosshair_look.from_code(text) {
            Some(look) => {
                self.share_note = Some(ShareNote::Pasted);
                let changed = look != settings.crosshair_look;
                settings.crosshair_look = look;
                changed
            }
            None => {
                self.share_note = Some(ShareNote::NotACode);
                false
            }
        }
    }

    /// `--options <tab>` (tests): the window open at that tab (`controls`, `gameplay`,
    /// `graphics`, `visuals`, `crosshair`, `sound`, `language`) from the start.
    pub fn opened_at(tab: Option<String>) -> Self {
        let mut ui = OptionsUi::default();
        let tabs = [Tab::Controls, Tab::Gameplay, Tab::Graphics, Tab::Visuals, Tab::Crosshair, Tab::Sound, Tab::Language];
        if let Some(t) = tab.and_then(|name| tabs.into_iter().find(|t| format!("{t:?}").eq_ignore_ascii_case(&name))) {
            ui.tab = t;
            ui.show();
        }
        ui
    }

    /// A click. True if the settings changed (save them).
    pub fn click(&mut self, pos: (f64, f64), settings: &mut Settings) -> bool {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.open = false;
            self.listening = None;
            return false;
        }
        let Some((hit, _)) = self.hits.iter().find(|(_, r)| inside(*r, pos)).copied() else {
            self.bubble_menu = false;
            return false;
        };
        self.listening = None;
        if !matches!(hit, Hit::BubbleMenu | Hit::BubbleKind(_) | Hit::BubbleList) {
            self.bubble_menu = false;
        }
        match hit {
            Hit::BubbleMenu => self.bubble_menu = !self.bubble_menu,
            Hit::BubbleKind(bit) => {
                settings.bubbles ^= bit;
                return true;
            }
            Hit::BubbleList => {}
            Hit::Tab(t) => {
                self.tab = t;
                self.share_note = None;
            }
            Hit::Bind(b) => self.listening = Some(b),
            Hit::Language(l) => {
                settings.language = l.map(|i| i18n::LANGUAGES[i].code.to_owned()).unwrap_or_default();
                return true;
            }
            Hit::Toggle(t) => {
                settings.hdr_chosen |= t == Toggle::Hdr;
                let v = settings.toggle(t);
                *v = !*v;
                return true;
            }
            Hit::Slider(slider, track) => {
                set_slider(slider, track, pos.0, settings);
                self.dragging = Some((slider, track));
                return true;
            }
            Hit::Style(style) => {
                settings.crosshair_look.style = style;
                return true;
            }
            Hit::Brush(b) => self.brush = b,
            Hit::Colour(c) => {
                settings.crosshair_look.colour = c;
                return true;
            }
            Hit::Cell(row, col, grid) => {
                // A drag paints what the first cell became.
                // A square already in the brush's colour is emptied; any other takes the colour.
                let brush = Some(self.brush).filter(|&b| settings.crosshair_look.cell(row, col) != Some(b));
                paint(settings, (row, col), brush);
                self.painting = Some((brush, grid));
                return true;
            }
            Hit::ClearCustom => {
                settings.crosshair_look.custom = [[0; crosshair::GRID]; crosshair::GRID];
                settings.crosshair_look.style = Style::Custom;
                return true;
            }
            Hit::CopyCode => {
                crate::textedit::clipboard::set(&settings.crosshair_look.code());
                self.share_note = Some(ShareNote::Copied);
            }
            Hit::PasteCode => {
                // On the desktop the clipboard is read at once; the browser hands it over in a
                // frame or two (`paste`, from the frame's pasted text).
                self.share_note = Some(if cfg!(target_arch = "wasm32") { ShareNote::Waiting } else { ShareNote::NotACode });
                if let Some(text) = crate::textedit::clipboard::get() {
                    return self.paste(&text, settings);
                }
            }
            Hit::Defaults => {
                let d = Settings::default();
                match self.tab {
                    Tab::Controls => settings.keys = d.keys,
                    Tab::Gameplay => {
                        for (t, _) in GAMEPLAY {
                            *settings.toggle(t) = *d.clone().toggle(t);
                        }
                    }
                    Tab::Graphics => {
                        // The window stays as it is.
                        for (t, _) in GRAPHICS.into_iter().filter(|(t, _)| *t != Toggle::Fullscreen) {
                            *settings.toggle(t) = *d.clone().toggle(t);
                        }
                        settings.zoom = d.zoom;
                        settings.max_fps = d.max_fps;
                        settings.hdr_chosen = false;
                    }
                    Tab::Visuals => {
                        for (t, _) in VISUALS {
                            *settings.toggle(t) = *d.clone().toggle(t);
                        }
                        settings.player_alpha = d.player_alpha;
                        settings.pet_alpha = d.pet_alpha;
                        settings.pet_shot_look = d.pet_shot_look;
                        settings.pings_from = d.pings_from;
                        settings.bubbles = d.bubbles;
                    }
                    Tab::Sound => {
                        settings.sound = d.sound;
                        settings.ping_sounds = d.ping_sounds;
                    }
                    // The player's own drawing stays.
                    Tab::Crosshair => settings.crosshair_look = Look { custom: settings.crosshair_look.custom, ..d.crosshair_look },
                    Tab::Language => settings.language.clear(),
                }
                return true;
            }
            Hit::Close => self.open = false,
            Hit::Home => {
                self.open = false;
                self.go_home = true;
            }
        }
        false
    }

    /// The mouse while the button is down: a held slider follows it. True if the settings
    /// changed (they're saved when the button comes up).
    pub fn drag(&mut self, pos: (f64, f64), settings: &mut Settings) -> bool {
        if let Some((on, grid)) = self.painting.filter(|_| self.open) {
            return grid_cell(grid, pos).is_some_and(|cell| paint(settings, cell, on));
        }
        let Some((slider, track)) = self.dragging.filter(|_| self.open) else { return false };
        let before = settings.clone();
        set_slider(slider, track, pos.0, settings);
        *settings != before
    }

    /// The button came up. True if a slider or the painting was let go (save the settings).
    pub fn release(&mut self) -> bool {
        let painted = self.painting.take().is_some();
        self.dragging.take().is_some() || painted
    }

    /// A key while the window is open. True if the settings changed.
    pub fn key(&mut self, code: KeyCode, settings: &mut Settings) -> bool {
        match (self.listening.take(), code) {
            (Some(_), KeyCode::Escape) => false,
            (Some(b), KeyCode::Backspace) => {
                settings.set_key(b, None);
                true
            }
            (Some(b), c) if KEYS.contains(&c) => {
                settings.set_key(b, Some(c));
                true
            }
            (Some(b), _) => {
                self.listening = Some(b);
                false
            }
            (None, KeyCode::Escape) => {
                self.open = false;
                false
            }
            (None, c) if Some(c) == settings.key(Bind::Options) => {
                self.open = false;
                false
            }
            _ => false,
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, settings: &Settings, mouse: (f64, f64)) {
        self.hits.clear();
        if !self.open {
            self.rect = None;
            self.dragging = None;
            self.painting = None;
            return;
        }
        let (w, h) = (lay.w, lay.h);
        let rows = BINDS.len().div_ceil(2) as f32;
        let title = tr("Options");
        let tabs = [(Tab::Controls, tr("Controls")), (Tab::Gameplay, tr("Gameplay")), (Tab::Graphics, tr("Graphics")), (Tab::Visuals, tr("Visuals")), (Tab::Crosshair, tr("Crosshair")), (Tab::Sound, tr("Sound")), (Tab::Language, tr("Language"))];
        // The tabs sit beside the title where they fit at a readable size (as the window is drawn
        // at its full size), else on a row of their own under it, the window that much taller.
        let beside = 620.0 - 44.0 - (ui.measure(title, 22.0, true) + 24.0).max(128.0) - 8.0 * (tabs.len() - 1) as f32;
        let own_row = tabs.iter().map(|(_, l)| (ui.measure(l, 11.0, true) + 16.0).max(60.0)).sum::<f32>() > beside;
        let below = if own_row { 40.0 } else { 0.0 };
        let (dw, dh) = (620.0, 176.0 + rows * 30.0 + below);
        let s = (lay.s * (w * 0.94 / (dw * lay.s)).min(h * 0.94 / (dh * lay.s)).min(1.0)).max(0.4);
        ui.rect(0.0, 0.0, w, h, 0x000000, 0.45);
        let r = [((w - dw * s) / 2.0).round(), ((h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label(title, r[0] + 22.0 * s, r[1] + 16.0 * s, font(22.0), true, color::TEXT);
        // Tabs, as pills as wide as their names; smaller type if a language's names are long.
        let start = if own_row { r[0] + 22.0 * s } else { (r[0] + 22.0 * s + ui.measure(title, font(22.0), true) + 24.0 * s).max(r[0] + 150.0 * s) };
        let tabs_y = r[1] + (if own_row { 58.0 } else { 18.0 }) * s;
        let gap = 8.0 * s;
        let room = r[0] + r[2] - 22.0 * s - start - gap * (tabs.len() - 1) as f32;
        // Narrower pills first, then smaller type.
        let mut pad = 14.0 * s;
        let mut ts = font(13.0);
        let widths = |ui: &mut Ui, ts: f32, pad: f32| tabs.map(|(_, l)| (ui.measure(l, ts, true) + 2.0 * pad).max(60.0 * s));
        let mut ws = widths(ui, ts, pad);
        while ws.iter().sum::<f32>() > room && ts > 8.0 {
            if pad > 8.0 * s {
                pad -= s;
            } else {
                ts -= 1.0;
            }
            ws = widths(ui, ts, pad);
        }
        let mut x = start;
        for ((tab, label), tw) in tabs.into_iter().zip(ws) {
            let tr = [x, tabs_y, tw, 28.0 * s];
            let on = tab == self.tab;
            let bg = if on { color::ACCENT } else if inside(tr, mouse) { color::SECONDARY } else { color::CHIP };
            theme::fill(ui.renderer, tr, tr[3] / 2.0, bg, 1.0);
            let lw = ui.measure(label, ts, true);
            ui.label(label, tr[0] + (tr[2] - lw) / 2.0, tr[1] + (tr[3] - ts * 1.22) / 2.0, ts, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
            self.hits.push((Hit::Tab(tab), tr));
            x += tr[2] + gap;
        }
        let body = [r[0] + 22.0 * s, r[1] + (66.0 + below) * s, r[2] - 44.0 * s, r[3] - (130.0 + below) * s];
        match self.tab {
            Tab::Controls => {
                let colw = (body[2] - 20.0 * s) / 2.0;
                let half = BINDS.len().div_ceil(2);
                for (i, (b, label, _, _)) in BINDS.iter().enumerate() {
                    let (col, row) = (i / half, i % half);
                    let y = body[1] + row as f32 * 30.0 * s;
                    let x = body[0] + col as f32 * (colw + 20.0 * s);
                    ui.label_fit(tr(label), x, y + 5.0 * s, colw - 118.0 * s, font(13.0), false, color::TEXT_DIM);
                    let kr = [x + colw - 110.0 * s, y, 110.0 * s, 26.0 * s];
                    let listening = self.listening == Some(*b);
                    let edge = if listening { color::ACCENT } else if inside(kr, mouse) { color::BORDER_HI } else { color::BORDER };
                    theme::frame(ui.renderer, kr, 5.0 * s, s.max(1.0), (edge, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
                    let text = if listening { tr("Press a key or mouse 4/5…").to_owned() } else { key_label(settings.key(*b)) };
                    let ks = font(12.5);
                    let kw = ui.measure(&text, ks, true);
                    ui.label(&text, kr[0] + (kr[2] - kw) / 2.0, kr[1] + (kr[3] - ks * 1.22) / 2.0, ks, true, if listening { color::ACCENT_HI } else { color::TEXT });
                    self.hits.push((Hit::Bind(*b), kr));
                }
            }
            Tab::Language => {
                // The system's language first, then each language by its own name, with its
                // name in the language in use beside it.
                let picked = Some(&settings.language).filter(|l| !l.is_empty()).and_then(|l| i18n::find(l));
                let system = i18n::system().unwrap_or(0);
                let mut rows = vec![(None, tr("Same as the system"), i18n::LANGUAGES[system].native.to_owned())];
                for (i, l) in i18n::LANGUAGES.iter().enumerate() {
                    let other = tr(l.english);
                    rows.push((Some(i), l.native, if other == l.native { String::new() } else { other.to_owned() }));
                }
                let colw = (body[2] - 20.0 * s) / 2.0;
                let per_col = rows.len().div_ceil(2);
                for (n, (lang, name, aside)) in rows.into_iter().enumerate() {
                    let (col, line) = (n / per_col, n % per_col);
                    let row = [body[0] + col as f32 * (colw + 20.0 * s), body[1] + line as f32 * 46.0 * s, colw, 40.0 * s];
                    let on = lang == picked;
                    let edge = if on { color::ACCENT } else if inside(row, mouse) { color::BORDER_HI } else { color::BORDER };
                    let bg = if on { color::CHIP } else { color::FIELD };
                    theme::frame(ui.renderer, row, 7.0 * s, s.max(1.0), (edge, 1.0), ((bg, 1.0), (bg, 1.0)));
                    let (ns, a) = (font(15.0), font(12.0));
                    let aw = if aside.is_empty() { 0.0 } else { ui.measure(&aside, a, false) };
                    ui.label_fit(name, row[0] + 14.0 * s, row[1] + (row[3] - ns * 1.22) / 2.0, row[2] - 40.0 * s - aw, ns, true, if on { color::ACCENT_HI } else { color::TEXT });
                    if !aside.is_empty() {
                        ui.label(&aside, row[0] + row[2] - 14.0 * s - aw, row[1] + (row[3] - a * 1.22) / 2.0, a, false, color::TEXT_MUTED);
                    }
                    self.hits.push((Hit::Language(lang), row));
                }
            }
            Tab::Crosshair => self.draw_crosshair_tab(ui, lay, body, s, settings, mouse),
            Tab::Graphics | Tab::Visuals | Tab::Sound | Tab::Gameplay => {
                let list: Vec<(Toggle, &str)> = match self.tab {
                    Tab::Gameplay => GAMEPLAY.to_vec(),
                    Tab::Graphics => GRAPHICS
                        .into_iter()
                        .filter(|(t, _)| !cfg!(target_arch = "wasm32") || !matches!(t, Toggle::Fullscreen | Toggle::Hdr))
                        .collect(),
                    Tab::Visuals => VISUALS.to_vec(),
                    _ => SOUNDS.to_vec(),
                };
                // The sliders first, then the switches under them.
                let mut y = body[1];
                let pct = |a: f32| format!("{}%", (a * 100.0).round() as i32);
                let sliders: Vec<(Slider, &str, f32, String)> = if self.tab == Tab::Sound {
                    let snd = settings.sound;
                    vec![
                        (Slider::Master, tr("Master volume"), snd.master_volume, pct(snd.master_volume)),
                        (Slider::Sfx, tr("Effects volume"), snd.sfx_volume, pct(snd.sfx_volume)),
                        (Slider::Music, tr("Music volume"), snd.music_volume, pct(snd.music_volume)),
                    ]
                } else if self.tab == Tab::Visuals {
                    let look = settings.pet_shot_look;
                    let lv = PetShots::ALL.iter().position(|&p| p == look).unwrap_or(1) as f32 / (PetShots::ALL.len() - 1) as f32;
                    let from = settings.pings_from;
                    let pv = PingsFrom::ALL.iter().position(|&p| p == from).unwrap_or(2) as f32 / (PingsFrom::ALL.len() - 1) as f32;
                    vec![
                        (Slider::PlayerAlpha, tr("Other players"), settings.player_alpha, pct(settings.player_alpha)),
                        (Slider::PetAlpha, tr("Other players' pets"), settings.pet_alpha, pct(settings.pet_alpha)),
                        (Slider::PetShots, tr("Pets' shots"), lv, look.label().to_owned()),
                        (Slider::ChatSize, tr("Chat size"), (settings.chat_size - CHAT_MIN) / (CHAT_MAX - CHAT_MIN), pct(settings.chat_size)),
                        (Slider::ChatAlpha, tr("Chat background"), settings.chat_alpha, pct(settings.chat_alpha)),
                        (Slider::Pings, tr("Others' pings"), pv, from.label().to_owned()),
                    ]
                } else {
                    // Lower shows more of the world (the mouse wheel over the world does it too).
                    let v = (settings.zoom - ZOOM_MIN) / (ZOOM_MAX - ZOOM_MIN);
                    let i = MAX_FPS.iter().position(|&f| f == settings.max_fps).unwrap_or(0);
                    let fv = i as f32 / (MAX_FPS.len() - 1) as f32;
                    let fps = if settings.max_fps == 0 { tr("No limit").to_owned() } else { settings.max_fps.to_string() };
                    vec![(Slider::Zoom, tr("World zoom"), v, pct(settings.zoom)), (Slider::MaxFps, tr("Max frame rate"), fv, fps)]
                };
                let sliders = if self.tab == Tab::Gameplay { Vec::new() } else { sliders };
                for (slider, label, v, value) in sliders {
                    ui.label_fit(label, body[0] + 10.0 * s, y + 4.0 * s, 186.0 * s, font(14.0), false, color::TEXT);
                    let track = [body[0] + 200.0 * s, y + 9.0 * s, body[2] - 280.0 * s, 8.0 * s];
                    theme::fill(ui.renderer, track, 4.0 * s, color::TRACK, 1.0);
                    theme::fill(ui.renderer, [track[0], track[1], track[2] * v, track[3]], 4.0 * s, color::ACCENT, 1.0);
                    let knob = 16.0 * s;
                    theme::fill(ui.renderer, [track[0] + track[2] * v - knob / 2.0, track[1] - 4.0 * s, knob, knob], knob / 2.0, 0xFFFFFF, 1.0);
                    ui.label(&value, track[0] + track[2] + 16.0 * s, y + 4.0 * s, font(13.0), true, color::TEXT_DIM);
                    self.hits.push((Hit::Slider(slider, track), [track[0] - 8.0 * s, y, track[2] + 16.0 * s, 26.0 * s]));
                    y += 40.0 * s;
                }
                y += 6.0 * s;
                // The speech balloons: a box saying whose show, opening a list to tick them in.
                let mut bubble_list = None;
                if self.tab == Tab::Visuals {
                    ui.label_fit(tr("Speech balloons"), body[0] + 10.0 * s, y + 8.0 * s, 186.0 * s, font(14.0), false, color::TEXT);
                    let bx = [body[0] + 200.0 * s, y + 3.0 * s, 280.0 * s, 28.0 * s];
                    let hot = self.bubble_menu || inside(bx, mouse);
                    theme::frame(ui.renderer, bx, 6.0 * s, s.max(1.0), (if hot { color::BORDER_HI } else { color::BORDER }, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
                    ui.label_fit(&bubble_summary(settings.bubbles), bx[0] + 10.0 * s, bx[1] + 6.0 * s, bx[2] - 40.0 * s, font(13.0), false, color::TEXT);
                    caret(ui, bx[0] + bx[2] - 18.0 * s, bx[1] + bx[3] / 2.0, 5.0 * s, self.bubble_menu);
                    self.hits.push((Hit::BubbleMenu, bx));
                    bubble_list = Some([bx[0], bx[1] + bx[3] + 2.0 * s, bx[2]]);
                    y += 40.0 * s;
                }
                // A long list of switches goes in two columns.
                let cols = if list.len() > 6 { 2 } else { 1 };
                let per_col = list.len().div_ceil(cols);
                let colw = (body[2] - 20.0 * s * (cols - 1) as f32) / cols as f32;
                for (i, (t, label)) in list.iter().enumerate() {
                    let (col, line) = (i / per_col, i % per_col);
                    let row = [body[0] + col as f32 * (colw + 20.0 * s), y + line as f32 * 38.0 * s, colw, 34.0 * s];
                    if inside(row, mouse) {
                        theme::fill(ui.renderer, row, 6.0 * s, 0xFFFFFF, 0.05);
                    }
                    ui.label_fit(tr(label), row[0] + 10.0 * s, row[1] + 8.0 * s, row[2] - 76.0 * s, font(14.0), false, color::TEXT);
                    let on = *settings.clone().toggle(*t);
                    // A switch.
                    let sw = [row[0] + row[2] - 56.0 * s, row[1] + 7.0 * s, 44.0 * s, 20.0 * s];
                    theme::fill(ui.renderer, sw, sw[3] / 2.0, if on { color::ACCENT } else { color::SECONDARY }, 1.0);
                    let knob = sw[3] - 4.0 * s;
                    let kx = if on { sw[0] + sw[2] - knob - 2.0 * s } else { sw[0] + 2.0 * s };
                    theme::fill(ui.renderer, [kx, sw[1] + 2.0 * s, knob, knob], knob / 2.0, 0xFFFFFF, 1.0);
                    self.hits.push((Hit::Toggle(*t), row));
                }
                // the list over the switches, its rows taking clicks before them
                if let Some([lx, ly, lw]) = bubble_list.filter(|_| self.bubble_menu) {
                    let rh = 30.0 * s;
                    let list = [lx, ly, lw, rh * BUBBLE_KINDS.len() as f32 + 8.0 * s];
                    theme::frame(ui.renderer, list, 6.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
                    let mut rows = vec![];
                    for (i, (bit, label)) in BUBBLE_KINDS.into_iter().enumerate() {
                        let row = [lx + 4.0 * s, ly + 4.0 * s + i as f32 * rh, lw - 8.0 * s, rh];
                        if inside(row, mouse) {
                            theme::fill(ui.renderer, row, 4.0 * s, 0xFFFFFF, 0.06);
                        }
                        let on = settings.bubbles & bit != 0;
                        let bs = 16.0 * s;
                        let tick = [row[0] + 8.0 * s, row[1] + (rh - bs) / 2.0, bs, bs];
                        theme::fill(ui.renderer, tick, 4.0 * s, if on { color::ACCENT } else { color::SECONDARY }, 1.0);
                        if on {
                            let inner = 6.0 * s;
                            theme::fill(ui.renderer, [tick[0] + (bs - inner) / 2.0, tick[1] + (bs - inner) / 2.0, inner, inner], inner / 2.0, 0xFFFFFF, 1.0);
                        }
                        ui.label_fit(tr(label), tick[0] + bs + 10.0 * s, row[1] + 7.0 * s, row[2] - bs - 30.0 * s, font(13.0), false, color::TEXT);
                        rows.push((Hit::BubbleKind(bit), row));
                    }
                    rows.push((Hit::BubbleList, list));
                    self.hits.splice(0..0, rows);
                }
            }
        }
        let hint = match self.tab {
            Tab::Controls => tr("Click a key, then press the new one. Backspace clears it."),
            Tab::Crosshair => tr("Pick a colour under the grid, then click or drag on it. Click a square of that colour to empty it."),
            Tab::Gameplay => tr("Stat potions are drunk from the bag you stand on; a maxed stat's go to your potion storage."),
            _ => tr("Changes apply at once."),
        };
        ui.label_fit(hint, r[0] + 22.0 * s, r[1] + r[3] - 80.0 * s, r[2] - 44.0 * s, font(12.0), false, color::TEXT_MUTED);
        let bw = 120.0 * s;
        let done = [r[0] + r[2] - 22.0 * s - bw, r[1] + r[3] - 56.0 * s, bw, 38.0 * s];
        ui.button(done, tr("Done"), Kind::Primary, inside(done, mouse), s);
        self.hits.push((Hit::Close, done));
        let reset = [done[0] - 10.0 * s - 150.0 * s, done[1], 150.0 * s, 38.0 * s];
        ui.button(reset, tr("Defaults"), Kind::Secondary, inside(reset, mouse), s);
        self.hits.push((Hit::Defaults, reset));
        // Home: back to the Nexus.
        if self.in_game {
            let home = [r[0] + 22.0 * s, done[1], 150.0 * s, 38.0 * s];
            ui.button(home, tr("Return home"), Kind::Secondary, inside(home, mouse), s);
            self.hits.push((Hit::Home, home));
        }
    }

    /// The Crosshair tab: the grid for the player's own on the left; on the right how it looks
    /// over the ground, the styles, the colours and the size.
    fn draw_crosshair_tab(&mut self, ui: &mut Ui, lay: &Layout, body: [f32; 4], s: f32, settings: &Settings, mouse: (f64, f64)) {
        let look = settings.crosshair_look;
        let font = |size: f32| (size * s).round().max(9.0);
        // The player's own.
        let n = crosshair::GRID;
        let cell = (17.0 * s).floor().max(6.0);
        ui.label_fit(tr("crosshair::Your own"), body[0], body[1], cell * n as f32, font(15.0), true, color::TEXT);
        let grid = [body[0], body[1] + 28.0 * s, cell * n as f32, cell * n as f32];
        theme::frame(ui.renderer, [grid[0] - 4.0 * s, grid[1] - 4.0 * s, grid[2] + 8.0 * s, grid[3] + 8.0 * s], 6.0 * s, s.max(1.0), (if look.style == Style::Custom { color::ACCENT } else { color::BORDER }, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
        let hover = grid_cell(grid, mouse);
        let mid = n / 2;
        for row in 0..n {
            for col in 0..n {
                let r = [grid[0] + col as f32 * cell + 1.0, grid[1] + row as f32 * cell + 1.0, cell - 2.0, cell - 2.0];
                let (rgb, alpha) = if let Some(p) = look.cell(row, col) {
                    (crosshair::PALETTE[p], 1.0)
                } else if row == mid || col == mid {
                    // The middle lines, so it's clear where the mouse will be.
                    (color::CHIP_HI, 0.55)
                } else {
                    (color::SECONDARY, 0.6)
                };
                ui.renderer.fill(r[0], r[1], r[2], r[3], rgb, alpha);
                if hover == Some((row, col)) {
                    ui.renderer.fill(r[0], r[1], r[2], r[3], 0xFFFFFF, 0.25);
                }
                self.hits.push((Hit::Cell(row, col, grid), [grid[0] + col as f32 * cell, grid[1] + row as f32 * cell, cell, cell]));
            }
        }
        // The brush: the colour the squares clicked take.
        let bs = 20.0 * s;
        let by = grid[1] + grid[3] + 16.0 * s;
        let step = (grid[2] - bs) / (crosshair::PALETTE.len() - 1) as f32;
        for (i, rgb) in crosshair::PALETTE.into_iter().enumerate() {
            let r = [grid[0] + i as f32 * step, by, bs, bs];
            swatch(ui, r, s, (rgb, rgb), self.brush == i, inside(r, mouse));
            self.hits.push((Hit::Brush(i), [r[0] - 3.0 * s, r[1] - 3.0 * s, r[2] + 6.0 * s, r[3] + 6.0 * s]));
        }
        let clear = [grid[0], by + bs + 16.0 * s, 130.0 * s, 34.0 * s];
        ui.button(clear, tr("Clear"), Kind::Secondary, inside(clear, mouse), s);
        self.hits.push((Hit::ClearCustom, clear));
        // Sharing: the code of the crosshair in use, to copy, and pasting one from a friend.
        let mut y = clear[1] + clear[3] + 26.0 * s;
        ui.label_fit(tr("Share"), grid[0], y, grid[2], font(15.0), true, color::TEXT);
        y += 26.0 * s;
        let bw = (grid[2] - 10.0 * s) / 2.0;
        let copy = [grid[0], y, bw, 34.0 * s];
        let paste = [grid[0] + bw + 10.0 * s, y, bw, 34.0 * s];
        ui.button(copy, tr("Copy code"), Kind::Secondary, inside(copy, mouse), s);
        ui.button(paste, tr("Paste code"), Kind::Secondary, inside(paste, mouse), s);
        self.hits.push((Hit::CopyCode, copy));
        self.hits.push((Hit::PasteCode, paste));
        y += 34.0 * s + 10.0 * s;
        // The code itself, so it can be read out or typed where there's no clipboard; a long
        // one (the player's own) over several lines.
        let code = look.code();
        let cs = font(11.0);
        let per = code.len().div_ceil(code.len().div_ceil(36));
        for line in code.as_bytes().chunks(per).filter_map(|c| std::str::from_utf8(c).ok()) {
            ui.label_fit(line, grid[0], y, grid[2], cs, true, color::TEXT_DIM);
            y += cs * 1.3;
        }
        if let Some(note) = self.share_note {
            let (text, rgb) = match note {
                ShareNote::Copied => (tr("Code copied: paste it to a friend."), color::GOOD),
                ShareNote::Waiting => (tr("Reading the clipboard…"), color::TEXT_MUTED),
                ShareNote::Pasted => (tr("Crosshair pasted."), color::GOOD),
                ShareNote::NotACode => (tr("The clipboard has no crosshair code."), color::DANGER),
            };
            ui.label_fit(text, grid[0], y + 4.0 * s, grid[2], font(12.0), false, rgb);
        }

        let rx = grid[0] + grid[2] + 26.0 * s;
        let rw = body[0] + body[2] - rx;
        // How it looks in the game, over dark ground and light.
        let shown = [rx, body[1], rw, 112.0 * s];
        theme::fill(ui.renderer, shown, 8.0 * s, 0x27331F, 1.0);
        theme::fill(ui.renderer, [shown[0] + shown[2] / 2.0, shown[1], shown[2] / 2.0, shown[3]], 8.0 * s, 0x9C9478, 1.0);
        ui.renderer.fill(shown[0] + shown[2] / 2.0, shown[1], 8.0 * s, shown[3], 0x9C9478, 1.0);
        let k = lay.s.max(0.75) * look.size;
        for x in [0.25, 0.75] {
            crosshair::draw(ui.renderer, (shown[0] + shown[2] * x, shown[1] + shown[3] / 2.0), k, &look);
        }
        // The styles, each with a small picture of itself.
        let (cols, gap) = (3, 8.0 * s);
        let cw = (rw - gap * (cols - 1) as f32) / cols as f32;
        let ch = 58.0 * s;
        let top = shown[1] + shown[3] + 14.0 * s;
        for (i, (style, _, label)) in crosshair::STYLES.into_iter().enumerate() {
            let c = [rx + (i % cols) as f32 * (cw + gap), top + (i / cols) as f32 * (ch + gap), cw, ch];
            let on = look.style == style;
            let edge = if on { color::ACCENT } else if inside(c, mouse) { color::BORDER_HI } else { color::BORDER };
            theme::frame(ui.renderer, c, 7.0 * s, s.max(1.0), (edge, 1.0), ((if on { color::CHIP } else { color::FIELD }, 1.0), (color::FIELD, 1.0)));
            let small = Look { style, ..look };
            crosshair::draw(ui.renderer, (c[0] + c[2] / 2.0, c[1] + 21.0 * s), (0.9 * s).max(0.6), &small);
            let ls = font(11.5);
            let text = tr(label);
            let lw = ui.measure(text, ls, false).min(c[2] - 8.0 * s);
            ui.label_fit(text, c[0] + (c[2] - lw) / 2.0, c[1] + c[3] - ls * 1.22 - 6.0 * s, c[2] - 8.0 * s, ls, false, if on { color::ACCENT_HI } else { color::TEXT_DIM });
            self.hits.push((Hit::Style(style), c));
        }
        // The styles' colour; the player's own keeps the colours it was painted in.
        let rows = crosshair::STYLES.len().div_ceil(cols) as f32;
        let mut y = top + rows * (ch + gap) + 6.0 * s;
        let sw = 26.0 * s;
        if look.style == Style::Custom {
            ui.label_fit(tr("Your own keeps the colours you paint it in."), rx, y + 24.0 * s, rw, font(12.0), false, color::TEXT_MUTED);
            y += 24.0 * s;
        } else {
            ui.label(tr("Colour"), rx, y, font(14.0), false, color::TEXT);
            y += 24.0 * s;
            let step = ((rw - sw) / (crosshair::COLOURS.len() - 1) as f32).min(sw + 12.0 * s);
            for (i, pair) in crosshair::COLOURS.into_iter().enumerate() {
                let r = [rx + 4.0 * s + i as f32 * step, y, sw, sw];
                swatch(ui, r, s, pair, look.colour == i, inside(r, mouse));
                self.hits.push((Hit::Colour(i), [r[0] - 4.0 * s, r[1] - 4.0 * s, r[2] + 8.0 * s, r[3] + 8.0 * s]));
            }
        }
        // The size.
        y += sw + 18.0 * s;
        ui.label_fit(tr("Size"), rx, y + 4.0 * s, 70.0 * s, font(14.0), false, color::TEXT);
        let track = [rx + 80.0 * s, y + 9.0 * s, rw - 140.0 * s, 8.0 * s];
        let v = (look.size - crosshair::SIZE_MIN) / (crosshair::SIZE_MAX - crosshair::SIZE_MIN);
        theme::fill(ui.renderer, track, 4.0 * s, color::TRACK, 1.0);
        theme::fill(ui.renderer, [track[0], track[1], track[2] * v, track[3]], 4.0 * s, color::ACCENT, 1.0);
        let knob = 16.0 * s;
        theme::fill(ui.renderer, [track[0] + track[2] * v - knob / 2.0, track[1] - 4.0 * s, knob, knob], knob / 2.0, 0xFFFFFF, 1.0);
        ui.label(&format!("{}%", (look.size * 100.0).round() as i32), track[0] + track[2] + 14.0 * s, y + 4.0 * s, font(13.0), true, color::TEXT_DIM);
        self.hits.push((Hit::Slider(Slider::CrosshairSize, track), [track[0] - 8.0 * s, y, track[2] + 16.0 * s, 26.0 * s]));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sliders_drag() {
        let mut ui = OptionsUi { open: true, ..Default::default() };
        let mut s = Settings::default();
        let track = [100.0, 0.0, 200.0, 8.0];
        // Not held: moving does nothing.
        assert!(!ui.drag((150.0, 0.0), &mut s));
        ui.dragging = Some((Slider::PlayerAlpha, track));
        assert!(ui.drag((150.0, 0.0), &mut s));
        assert_eq!(s.player_alpha, 0.25);
        // Past the end it stops at the end; the same place again changes nothing.
        assert!(ui.drag((900.0, 0.0), &mut s));
        assert_eq!(s.player_alpha, 1.0);
        assert!(!ui.drag((901.0, 0.0), &mut s));
        assert!(ui.release());
        assert!(!ui.drag((100.0, 0.0), &mut s), "let go");
        assert!(!ui.release());
        set_slider(Slider::MaxFps, track, 300.0, &mut s);
        assert_eq!(s.max_fps, *MAX_FPS.last().unwrap());
        assert_eq!(s.pet_shot_look, PetShots::Subtle);
        set_slider(Slider::PetShots, track, 100.0, &mut s);
        assert_eq!(s.pet_shot_look, PetShots::Hidden);
        set_slider(Slider::PetShots, track, 300.0, &mut s);
        assert_eq!(s.pet_shot_look, PetShots::Normal);
        set_slider(Slider::ChatSize, track, 100.0, &mut s);
        assert_eq!(s.chat_size, CHAT_MIN);
        set_slider(Slider::ChatSize, track, 300.0, &mut s);
        assert_eq!(s.chat_size, CHAT_MAX);
        set_slider(Slider::ChatAlpha, track, 150.0, &mut s);
        assert_eq!(s.chat_alpha, 0.25);
        set_slider(Slider::Pings, track, 200.0, &mut s);
        assert_eq!(s.pings_from, PingsFrom::Friends);
        assert!(s.pings_from.shows(2) && s.pings_from.shows(1) && !s.pings_from.shows(0));
        set_slider(Slider::Pings, track, 100.0, &mut s);
        assert!(s.pings_from.shows(3) && !s.pings_from.shows(2), "our own always show");
    }

    #[test]
    fn settings_round_trip() {
        let mut s = Settings::default();
        assert_eq!(s.key(Bind::MoveUp), Some(KeyCode::KeyW));
        assert_eq!(s.bind_of(KeyCode::Digit3), Some(Bind::Slot(3)));
        // Binding W to the ability takes it from moving up.
        s.set_key(Bind::Ability, Some(KeyCode::KeyW));
        assert_eq!(s.key(Bind::MoveUp), None);
        s.particles = false;
        s.sound.music_volume = 0.25;
        s.sound.master_volume = 0.6;
        s.zoom = 1.2;
        s.max_fps = 144;
        s.camera_angle = Some(1.5);
        s.preferred_server = "Europe".into();
        s.pet_shots = false;
        s.crosshair = false;
        s.crosshair_look = Look { style: Style::Custom, colour: 4, size: 1.75, ..Look::default() };
        s.crosshair_look.set_cell(0, 0, Some(3));
        s.player_alpha = 0.35;
        s.pet_alpha = 0.0;
        s.pet_shot_look = PetShots::Hidden;
        s.fullscreen = true;
        s.chat_size = 0.75;
        s.chat_alpha = 0.4;
        s.language = "de".into();
        s.pings_from = PingsFrom::Nobody;
        s.ping_sounds = false;
        let back = Settings::from_text(&s.to_text());
        assert_eq!(back, s);
        // Lines another version wrote stay, and its keys don't take a key this one gives.
        let text = "someFutureOption=7\nmoveUp=KeyQ\nlater=KeySomethingNew\nnexus=\n";
        let t = Settings::from_text(text);
        assert_eq!(t.key(Bind::MoveUp), Some(KeyCode::KeyQ));
        assert_eq!(t.key(Bind::RotateLeft), None, "Q went to moving up");
        assert!(t.to_text().contains("someFutureOption=7\n"));
        assert_eq!(key_label(Some(KeyCode::KeyW)), "W");
        assert_eq!(key_label(Some(KeyCode::Digit0)), "0");
        assert_eq!(key_label(Some(KeyCode::PageUp)), "Page Up");
    }

    #[test]
    fn painting_a_crosshair() {
        let mut ui = OptionsUi { open: true, ..Default::default() };
        let mut s = Settings::default();
        s.crosshair_look.custom = [[0; crosshair::GRID]; crosshair::GRID];
        // A grid of 10-pixel cells at (100, 100), and the brush's colours beside it.
        let grid = [100.0, 100.0, 150.0, 150.0];
        ui.hits = vec![(Hit::Cell(0, 0, grid), [100.0, 100.0, 10.0, 10.0]), (Hit::Brush(4), [0.0, 0.0, 10.0, 10.0]), (Hit::Brush(2), [0.0, 20.0, 10.0, 10.0])];
        // A click paints a square in the brush's colour (white at first), a drag carries on.
        assert!(ui.click((105.0, 105.0), &mut s));
        assert_eq!(s.crosshair_look.style, Style::Custom);
        assert_eq!(s.crosshair_look.cell(0, 0), Some(0));
        assert!(ui.drag((125.0, 105.0), &mut s));
        assert_eq!(s.crosshair_look.cell(0, 2), Some(0));
        assert!(!ui.drag((125.0, 105.0), &mut s), "already painted");
        assert!(!ui.drag((400.0, 105.0), &mut s), "off the grid");
        assert!(ui.release());
        assert!(!ui.drag((135.0, 105.0), &mut s), "let go");
        // Another colour paints over one square without touching the rest.
        assert!(!ui.click((5.0, 5.0), &mut s), "picking a colour changes nothing");
        assert!(ui.click((105.0, 105.0), &mut s));
        ui.release();
        assert_eq!((s.crosshair_look.cell(0, 0), s.crosshair_look.cell(0, 2)), (Some(4), Some(0)));
        // A square already in the brush's colour is emptied, and so is what the drag passes in
        // any colour.
        assert!(ui.click((105.0, 105.0), &mut s));
        assert_eq!(s.crosshair_look.cell(0, 0), None);
        ui.drag((125.0, 105.0), &mut s);
        ui.release();
        assert_eq!(s.crosshair_look.custom, [[0; crosshair::GRID]; crosshair::GRID]);
        ui.click((5.0, 25.0), &mut s);
        ui.click((105.0, 105.0), &mut s);
        assert_eq!(s.crosshair_look.cell(0, 0), Some(2));
    }

    #[test]
    fn pasting_a_shared_crosshair() {
        let mut ui = OptionsUi::opened_at(Some("visuals".into()));
        let mut s = Settings::default();
        // Only the Crosshair tab takes codes.
        assert!(!ui.paste("DD27C8", &mut s));
        assert_eq!(ui.share_note, None);
        ui.tab = Tab::Crosshair;
        assert!(ui.paste("DD27C8", &mut s));
        assert_eq!((s.crosshair_look.style, s.crosshair_look.colour, s.crosshair_look.size), (Style::Dot, 7, 2.0));
        assert_eq!(ui.share_note, Some(ShareNote::Pasted));
        assert!(!ui.paste("hello there", &mut s));
        assert_eq!(ui.share_note, Some(ShareNote::NotACode));
        assert_eq!(s.crosshair_look.style, Style::Dot, "unchanged");
    }

    #[test]
    fn listening_for_a_key() {
        let mut ui = OptionsUi::default();
        let mut s = Settings::default();
        ui.show();
        ui.listening = Some(Bind::Nexus);
        assert!(ui.key(KeyCode::KeyN, &mut s));
        assert_eq!(s.key(Bind::Nexus), Some(KeyCode::KeyN));
        ui.listening = Some(Bind::Nexus);
        assert!(!ui.key(KeyCode::Escape, &mut s));
        assert_eq!(s.key(Bind::Nexus), Some(KeyCode::KeyN));
        assert!(ui.open);
        ui.key(KeyCode::Escape, &mut s);
        assert!(!ui.open);
    }
}
