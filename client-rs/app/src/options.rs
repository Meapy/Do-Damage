//! Options (`Options`, `Parameters`): key bindings, graphics and sound, kept between runs (in
//! `settings.txt` beside the remembered login, or the browser's storage).
//!
//! The window opens from the Escape menu or its key (O). On the Controls tab, click a binding
//! and press the key to use instead (Escape cancels, Backspace clears it).

use crate::audio::SoundSettings;
use crate::hud::{Layout, Ui};
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
    /// `switchTabs`: the inventory or the backpack.
    SwitchTabs,
    /// `toggleCentering`: the player in the middle of the view, or a little below.
    Center,
    /// `togglePerformanceStats`: the frame rate on screen.
    Stats,
    /// Chat commands: `/vault`, `/realm`, `/ghall`, `/pinviteworld`, `/pjoin`.
    Vault,
    Realm,
    GuildHall,
    PartyInviteWorld,
    PartyJoinWorld,
}

/// Every binding: what, its label, its name in the file, its default key.
pub const BINDS: [(Bind, &str, &str, Option<KeyCode>); 39] = [
    (Bind::MoveUp, "Move up", "moveUp", Some(KeyCode::KeyW)),
    (Bind::MoveLeft, "Move left", "moveLeft", Some(KeyCode::KeyA)),
    (Bind::MoveDown, "Move down", "moveDown", Some(KeyCode::KeyS)),
    (Bind::MoveRight, "Move right", "moveRight", Some(KeyCode::KeyD)),
    (Bind::RotateLeft, "Rotate left", "rotateLeft", Some(KeyCode::KeyQ)),
    (Bind::RotateRight, "Rotate right", "rotateRight", Some(KeyCode::KeyE)),
    (Bind::ResetCamera, "Reset camera", "resetCamera", Some(KeyCode::KeyZ)),
    (Bind::Ability, "Use ability", "useSpecial", Some(KeyCode::Space)),
    (Bind::Interact, "Interact", "interact", Some(KeyCode::Digit0)),
    (Bind::Nexus, "Nexus", "escapeToNexus", Some(KeyCode::KeyR)),
    (Bind::Autofire, "Autofire", "autofireToggle", Some(KeyCode::KeyI)),
    (Bind::HealthPotion, "Health potion", "useHealthPotion", Some(KeyCode::KeyF)),
    (Bind::MagicPotion, "Magic potion", "useMagicPotion", Some(KeyCode::KeyV)),
    (Bind::Slot(1), "Item 1", "useInvSlot1", Some(KeyCode::Digit1)),
    (Bind::Slot(2), "Item 2", "useInvSlot2", Some(KeyCode::Digit2)),
    (Bind::Slot(3), "Item 3", "useInvSlot3", Some(KeyCode::Digit3)),
    (Bind::Slot(4), "Item 4", "useInvSlot4", Some(KeyCode::Digit4)),
    (Bind::Slot(5), "Item 5", "useInvSlot5", Some(KeyCode::Digit5)),
    (Bind::Slot(6), "Item 6", "useInvSlot6", Some(KeyCode::Digit6)),
    (Bind::Slot(7), "Item 7", "useInvSlot7", Some(KeyCode::Digit7)),
    (Bind::Slot(8), "Item 8", "useInvSlot8", Some(KeyCode::Digit8)),
    (Bind::Chat, "Chat", "chat", Some(KeyCode::Enter)),
    (Bind::Command, "Command", "chatCommand", Some(KeyCode::Slash)),
    (Bind::Tell, "Whisper", "tell", Some(KeyCode::Tab)),
    (Bind::GuildChat, "Guild chat", "guildChat", Some(KeyCode::KeyG)),
    (Bind::PartyChat, "Party chat", "partyChat", Some(KeyCode::KeyP)),
    (Bind::ChatUp, "Chat history up", "scrollChatUp", Some(KeyCode::PageUp)),
    (Bind::ChatDown, "Chat history down", "scrollChatDown", Some(KeyCode::PageDown)),
    (Bind::ZoomIn, "Minimap zoom in", "miniMapZoomIn", Some(KeyCode::Equal)),
    (Bind::ZoomOut, "Minimap zoom out", "miniMapZoomOut", Some(KeyCode::Minus)),
    (Bind::Options, "Options", "options", Some(KeyCode::KeyO)),
    (Bind::SwitchTabs, "Inventory / backpack", "switchTabs", Some(KeyCode::KeyB)),
    (Bind::Center, "Center on player", "toggleCentering", Some(KeyCode::KeyX)),
    (Bind::Stats, "Show frame rate", "togglePerformanceStats", Some(KeyCode::KeyL)),
    (Bind::Vault, "Go to the vault", "reconVault", None),
    (Bind::Realm, "Go to a realm", "reconRealm", None),
    (Bind::GuildHall, "Go to the guild hall", "reconGuildHall", None),
    (Bind::PartyInviteWorld, "Invite the world to party", "partyInviteWorld", None),
    (Bind::PartyJoinWorld, "Join a world party", "partyJoinWorld", None),
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
];

fn key_name(k: KeyCode) -> String {
    format!("{k:?}")
}

fn parse_key(name: &str) -> Option<KeyCode> {
    KEYS.iter().copied().find(|k| key_name(*k) == name)
}

/// A key as the player knows it: "W", "1", "Space", "Page Up".
pub fn key_label(k: Option<KeyCode>) -> String {
    let Some(k) = k else { return "None".into() };
    let name = key_name(k);
    if let Some(c) = name.strip_prefix("Key").or_else(|| name.strip_prefix("Digit")) {
        return c.to_owned();
    }
    match k {
        KeyCode::ArrowUp => "Up".into(),
        KeyCode::ArrowDown => "Down".into(),
        KeyCode::ArrowLeft => "Left".into(),
        KeyCode::ArrowRight => "Right".into(),
        KeyCode::PageUp => "Page Up".into(),
        KeyCode::PageDown => "Page Down".into(),
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
    Bubbles,
    AllyShots,
    PetShots,
    PetEffects,
    Crosshair,
    DamageNumbers,
    Rotation,
    Centered,
    ShowFps,
    UnlockFps,
    Sfx,
    Shots,
    Music,
}

/// What's shown in the world.
const VISUALS: [(Toggle, &str); 9] = [
    (Toggle::Particles, "Particles and effects"),
    (Toggle::Shadows, "Shadows"),
    (Toggle::HpBars, "Health bars"),
    (Toggle::Bubbles, "Speech balloons"),
    (Toggle::AllyShots, "Other players' shots"),
    (Toggle::PetShots, "Other players' pets' shots"),
    (Toggle::PetEffects, "Other players' pets' effects"),
    (Toggle::Crosshair, "Crosshair"),
    (Toggle::DamageNumbers, "Damage numbers"),
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

/// The camera and the display.
const GRAPHICS: [(Toggle, &str); 4] = [
    (Toggle::Rotation, "Camera rotation"),
    (Toggle::Centered, "Center on player"),
    (Toggle::ShowFps, "Show frame rate"),
    (Toggle::UnlockFps, "Unlock frame rate (no vsync)"),
];

const SOUNDS: [(Toggle, &str); 3] = [(Toggle::Sfx, "Sound effects"), (Toggle::Shots, "Weapon sounds"), (Toggle::Music, "Music")];

#[derive(Clone, Debug, PartialEq)]
pub struct Settings {
    keys: Vec<Option<KeyCode>>,
    pub sound: SoundSettings,
    pub particles: bool,
    pub shadows: bool,
    pub hp_bars: bool,
    pub bubbles: bool,
    pub ally_shots: bool,
    /// Other players' pets' projectiles, and their pets' particle effects.
    pub pet_shots: bool,
    pub pet_effects: bool,
    /// How every pet's shots look.
    pub pet_shot_look: PetShots,
    /// A crosshair at the mouse over the world, instead of the pointer.
    pub crosshair: bool,
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
    /// The most frames a second (0: no limit).
    pub max_fps: u32,
    /// State kept as the AIR client keeps it in its options (`cameraAngle`,
    /// `preferredServer`): the camera's turn, and the server last chosen.
    pub camera_angle: Option<f64>,
    pub preferred_server: String,
    /// The last version the player was told about (the "updated" note shows once a version).
    pub seen_version: String,
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

impl Default for Settings {
    fn default() -> Self {
        Settings {
            keys: BINDS.iter().map(|b| b.3).collect(),
            sound: SoundSettings::default(),
            particles: true,
            shadows: true,
            hp_bars: true,
            bubbles: true,
            ally_shots: true,
            pet_shots: true,
            pet_effects: true,
            pet_shot_look: PetShots::Subtle,
            crosshair: true,
            player_alpha: 1.0,
            pet_alpha: 1.0,
            damage_numbers: true,
            rotation: true,
            centered: true,
            show_fps: false,
            zoom: 1.0,
            unlock_fps: false,
            max_fps: 0,
            camera_angle: None,
            preferred_server: String::new(),
            seen_version: String::new(),
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
            Toggle::Bubbles => &mut self.bubbles,
            Toggle::AllyShots => &mut self.ally_shots,
            Toggle::PetShots => &mut self.pet_shots,
            Toggle::PetEffects => &mut self.pet_effects,
            Toggle::Crosshair => &mut self.crosshair,
            Toggle::DamageNumbers => &mut self.damage_numbers,
            Toggle::Rotation => &mut self.rotation,
            Toggle::Centered => &mut self.centered,
            Toggle::ShowFps => &mut self.show_fps,
            Toggle::UnlockFps => &mut self.unlock_fps,
            Toggle::Sfx => &mut self.sound.sfx,
            Toggle::Shots => &mut self.sound.shots,
            Toggle::Music => &mut self.sound.music,
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
        flag("textBubbles", self.bubbles);
        flag("allyShots", self.ally_shots);
        flag("petShots", self.pet_shots);
        flag("petEffects", self.pet_effects);
        flag("crosshair", self.crosshair);
        flag("damageNumbers", self.damage_numbers);
        flag("allowRotation", self.rotation);
        flag("cameraCentered", self.centered);
        flag("showStatistics", self.show_fps);
        flag("unlockFps", self.unlock_fps);
        flag("playSFX", self.sound.sfx);
        flag("playPewPew", self.sound.shots);
        flag("playMusic", self.sound.music);
        out += &format!("masterVolume={}\nsfxVolume={}\nmusicVolume={}\n", self.sound.master_volume, self.sound.sfx_volume, self.sound.music_volume);
        out += &format!("worldZoom={}\n", self.zoom);
        out += &format!("playerOpacity={}\npetOpacity={}\n", self.player_alpha, self.pet_alpha);
        out += &format!("maxFps={}\n", self.max_fps);
        out += &format!("petShotLook={}\n", self.pet_shot_look.name());
        if let Some(a) = self.camera_angle {
            out += &format!("cameraAngle={a}\n");
        }
        if !self.preferred_server.is_empty() {
            out += &format!("preferredServer={}\n", self.preferred_server);
        }
        if !self.seen_version.is_empty() {
            out += &format!("seenVersion={}\n", self.seen_version);
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
                "hpBars" => s.hp_bars = on,
                "textBubbles" => s.bubbles = on,
                "allyShots" => s.ally_shots = on,
                "petShots" => s.pet_shots = on,
                "petEffects" => s.pet_effects = on,
                "crosshair" => s.crosshair = on,
                "playerOpacity" => s.player_alpha = vol().unwrap_or(1.0),
                "petOpacity" => s.pet_alpha = vol().unwrap_or(1.0),
                "petShotLook" => s.pet_shot_look = PetShots::from_name(value).unwrap_or_default(),
                "damageNumbers" => s.damage_numbers = on,
                "allowRotation" => s.rotation = on,
                "cameraCentered" => s.centered = on,
                "showStatistics" => s.show_fps = on,
                "unlockFps" => s.unlock_fps = on,
                "playSFX" => s.sound.sfx = on,
                "playPewPew" => s.sound.shots = on,
                "playMusic" => s.sound.music = on,
                "masterVolume" => s.sound.master_volume = vol().unwrap_or(s.sound.master_volume),
                "sfxVolume" => s.sound.sfx_volume = vol().unwrap_or(s.sound.sfx_volume),
                "musicVolume" => s.sound.music_volume = vol().unwrap_or(s.sound.music_volume),
                "worldZoom" => s.zoom = value.parse::<f32>().map_or(1.0, |z| z.clamp(ZOOM_MIN, ZOOM_MAX)),
                "maxFps" => s.max_fps = value.parse().unwrap_or(0),
                "cameraAngle" => s.camera_angle = value.parse().ok().filter(|a: &f64| a.is_finite()),
                "preferredServer" => s.preferred_server = value.to_owned(),
                "seenVersion" => s.seen_version = value.to_owned(),
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
        s
    }

    pub fn save(&self) {
        crate::login::store::save("settings", Some(&self.to_text()));
        self.note_keys();
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
    Graphics,
    Visuals,
    Sound,
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
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Bind(Bind),
    Toggle(Toggle),
    /// A slider (a volume, the zoom...) and its track.
    Slider(Slider, [f32; 4]),
    Defaults,
    Close,
    Home,
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
    }
}

impl OptionsUi {
    pub fn show(&mut self) {
        self.open = true;
        self.listening = None;
    }

    /// A click. True if the settings changed (save them).
    pub fn click(&mut self, pos: (f64, f64), settings: &mut Settings) -> bool {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.open = false;
            self.listening = None;
            return false;
        }
        let Some((hit, _)) = self.hits.iter().find(|(_, r)| inside(*r, pos)).copied() else { return false };
        self.listening = None;
        match hit {
            Hit::Tab(t) => self.tab = t,
            Hit::Bind(b) => self.listening = Some(b),
            Hit::Toggle(t) => {
                let v = settings.toggle(t);
                *v = !*v;
                return true;
            }
            Hit::Slider(slider, track) => {
                set_slider(slider, track, pos.0, settings);
                self.dragging = Some((slider, track));
                return true;
            }
            Hit::Defaults => {
                let d = Settings::default();
                match self.tab {
                    Tab::Controls => settings.keys = d.keys,
                    Tab::Graphics => {
                        for (t, _) in GRAPHICS {
                            *settings.toggle(t) = *d.clone().toggle(t);
                        }
                        settings.zoom = d.zoom;
                        settings.max_fps = d.max_fps;
                    }
                    Tab::Visuals => {
                        for (t, _) in VISUALS {
                            *settings.toggle(t) = *d.clone().toggle(t);
                        }
                        settings.player_alpha = d.player_alpha;
                        settings.pet_alpha = d.pet_alpha;
                        settings.pet_shot_look = d.pet_shot_look;
                    }
                    Tab::Sound => settings.sound = d.sound,
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
        let Some((slider, track)) = self.dragging.filter(|_| self.open) else { return false };
        let before = settings.clone();
        set_slider(slider, track, pos.0, settings);
        *settings != before
    }

    /// The button came up. True if a slider was let go (save the settings).
    pub fn release(&mut self) -> bool {
        self.dragging.take().is_some()
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
            return;
        }
        let (w, h) = (lay.w, lay.h);
        let rows = BINDS.len().div_ceil(2) as f32;
        let (dw, dh) = (620.0, 176.0 + rows * 30.0);
        let s = (lay.s * (w * 0.94 / (dw * lay.s)).min(h * 0.94 / (dh * lay.s)).min(1.0)).max(0.4);
        ui.rect(0.0, 0.0, w, h, 0x000000, 0.45);
        let r = [((w - dw * s) / 2.0).round(), ((h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label("Options", r[0] + 22.0 * s, r[1] + 16.0 * s, font(22.0), true, color::TEXT);
        // Tabs, as pills.
        let mut x = r[0] + 150.0 * s;
        for (tab, label) in [(Tab::Controls, "Controls"), (Tab::Graphics, "Graphics"), (Tab::Visuals, "Visuals"), (Tab::Sound, "Sound")] {
            let tr = [x, r[1] + 18.0 * s, 104.0 * s, 28.0 * s];
            let on = tab == self.tab;
            let bg = if on { color::ACCENT } else if inside(tr, mouse) { color::SECONDARY } else { color::CHIP };
            theme::fill(ui.renderer, tr, tr[3] / 2.0, bg, 1.0);
            let ts = font(13.0);
            let lw = ui.measure(label, ts, true);
            ui.label(label, tr[0] + (tr[2] - lw) / 2.0, tr[1] + (tr[3] - ts * 1.22) / 2.0, ts, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
            self.hits.push((Hit::Tab(tab), tr));
            x += tr[2] + 8.0 * s;
        }
        let body = [r[0] + 22.0 * s, r[1] + 66.0 * s, r[2] - 44.0 * s, r[3] - 130.0 * s];
        match self.tab {
            Tab::Controls => {
                let colw = (body[2] - 20.0 * s) / 2.0;
                let half = BINDS.len().div_ceil(2);
                for (i, (b, label, _, _)) in BINDS.iter().enumerate() {
                    let (col, row) = (i / half, i % half);
                    let y = body[1] + row as f32 * 30.0 * s;
                    let x = body[0] + col as f32 * (colw + 20.0 * s);
                    ui.label(label, x, y + 5.0 * s, font(13.0), false, color::TEXT_DIM);
                    let kr = [x + colw - 110.0 * s, y, 110.0 * s, 26.0 * s];
                    let listening = self.listening == Some(*b);
                    let edge = if listening { color::ACCENT } else if inside(kr, mouse) { color::BORDER_HI } else { color::BORDER };
                    theme::frame(ui.renderer, kr, 5.0 * s, s.max(1.0), (edge, 1.0), ((color::FIELD, 1.0), (color::FIELD, 1.0)));
                    let text = if listening { "Press a key…".to_owned() } else { key_label(settings.key(*b)) };
                    let ks = font(12.5);
                    let kw = ui.measure(&text, ks, true);
                    ui.label(&text, kr[0] + (kr[2] - kw) / 2.0, kr[1] + (kr[3] - ks * 1.22) / 2.0, ks, true, if listening { color::ACCENT_HI } else { color::TEXT });
                    self.hits.push((Hit::Bind(*b), kr));
                }
            }
            Tab::Graphics | Tab::Visuals | Tab::Sound => {
                let list: &[(Toggle, &str)] = match self.tab {
                    Tab::Graphics => &GRAPHICS,
                    Tab::Visuals => &VISUALS,
                    _ => &SOUNDS,
                };
                // The sliders first, then the switches under them.
                let mut y = body[1];
                let pct = |a: f32| format!("{}%", (a * 100.0).round() as i32);
                let sliders: Vec<(Slider, &str, f32, String)> = if self.tab == Tab::Sound {
                    let snd = settings.sound;
                    vec![
                        (Slider::Master, "Master volume", snd.master_volume, pct(snd.master_volume)),
                        (Slider::Sfx, "Effects volume", snd.sfx_volume, pct(snd.sfx_volume)),
                        (Slider::Music, "Music volume", snd.music_volume, pct(snd.music_volume)),
                    ]
                } else if self.tab == Tab::Visuals {
                    let look = settings.pet_shot_look;
                    let lv = PetShots::ALL.iter().position(|&p| p == look).unwrap_or(1) as f32 / (PetShots::ALL.len() - 1) as f32;
                    vec![
                        (Slider::PlayerAlpha, "Other players", settings.player_alpha, pct(settings.player_alpha)),
                        (Slider::PetAlpha, "Other players' pets", settings.pet_alpha, pct(settings.pet_alpha)),
                        (Slider::PetShots, "Pets' shots", lv, look.name().to_owned()),
                    ]
                } else {
                    // Lower shows more of the world (the mouse wheel over the world does it too).
                    let v = (settings.zoom - ZOOM_MIN) / (ZOOM_MAX - ZOOM_MIN);
                    let i = MAX_FPS.iter().position(|&f| f == settings.max_fps).unwrap_or(0);
                    let fv = i as f32 / (MAX_FPS.len() - 1) as f32;
                    let fps = if settings.max_fps == 0 { "No limit".to_owned() } else { settings.max_fps.to_string() };
                    vec![(Slider::Zoom, "World zoom", v, pct(settings.zoom)), (Slider::MaxFps, "Max frame rate", fv, fps)]
                };
                for (slider, label, v, value) in sliders {
                    ui.label(label, body[0] + 10.0 * s, y + 4.0 * s, font(14.0), false, color::TEXT);
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
                    ui.label(label, row[0] + 10.0 * s, row[1] + 8.0 * s, font(14.0), false, color::TEXT);
                    let on = *settings.clone().toggle(*t);
                    // A switch.
                    let sw = [row[0] + row[2] - 56.0 * s, row[1] + 7.0 * s, 44.0 * s, 20.0 * s];
                    theme::fill(ui.renderer, sw, sw[3] / 2.0, if on { color::ACCENT } else { color::SECONDARY }, 1.0);
                    let knob = sw[3] - 4.0 * s;
                    let kx = if on { sw[0] + sw[2] - knob - 2.0 * s } else { sw[0] + 2.0 * s };
                    theme::fill(ui.renderer, [kx, sw[1] + 2.0 * s, knob, knob], knob / 2.0, 0xFFFFFF, 1.0);
                    self.hits.push((Hit::Toggle(*t), row));
                }
            }
        }
        let hint = if self.tab == Tab::Controls { "Click a key, then press the new one. Backspace clears it." } else { "Changes apply at once." };
        ui.label(hint, r[0] + 22.0 * s, r[1] + r[3] - 80.0 * s, font(12.0), false, color::TEXT_MUTED);
        let bw = 120.0 * s;
        let done = [r[0] + r[2] - 22.0 * s - bw, r[1] + r[3] - 56.0 * s, bw, 38.0 * s];
        ui.button(done, "Done", Kind::Primary, inside(done, mouse), s);
        self.hits.push((Hit::Close, done));
        let reset = [done[0] - 10.0 * s - 150.0 * s, done[1], 150.0 * s, 38.0 * s];
        ui.button(reset, "Defaults", Kind::Secondary, inside(reset, mouse), s);
        self.hits.push((Hit::Defaults, reset));
        // Home: back to the Nexus.
        if self.in_game {
            let home = [r[0] + 22.0 * s, done[1], 150.0 * s, 38.0 * s];
            ui.button(home, "Return home", Kind::Secondary, inside(home, mouse), s);
            self.hits.push((Hit::Home, home));
        }
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
        s.player_alpha = 0.35;
        s.pet_alpha = 0.0;
        s.pet_shot_look = PetShots::Hidden;
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
