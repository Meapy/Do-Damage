//! A game in progress: the world server connection, the session that keeps it alive, and the
//! world it describes. Changing worlds (portals, escaping to the nexus) is a Reconnect: a new
//! connection with the key the server handed out.

use crate::i18n::{tf, tn, tr};
use bsk_data::{GameData, ObjectClass};
use bsk_net::{GameSocket, SocketEvent, Target};
use bsk_proto::{
    AcceptTrade, Buy, CancelTrade, ChangeTrade, ClientMessage, EditAccountList, EnemyHit, Escape, GroundDamage, GuildInvite, Height,
    InvDrop, JoinGuild, JoinParty, List16, PartyInvite, RequestTrade, Teleport, TradeItem, InvSwap, OtherHit, PlayerHit, PlayerShoot, PlayerText,
    Reconnect, ServerMessage, SlotObject, SquareHit, UseItem, UsePortal, ViewRadius, WorldPos, ChangeGuildRank, ChooseName,
    CreateGuild, GuildRemove, Reskin, UseStorage,
};
use bsk_session::{Event, HelloParams, Join, Session};
use bsk_world::{Hit, MoveInput, Projectile, World};

/// The AIR client's default camera angle (`Parameters`: 7π/4).
pub const DEFAULT_CAMERA_ANGLE: f64 = 7.0 * std::f64::consts::PI / 4.0;
/// What Z resets the camera to: no turn, north at the top.
pub const RESET_CAMERA_ANGLE: f64 = 0.0;
/// `Parameters.PLAYER_ROTATE_SPEED`, radians per millisecond.
const ROTATE_SPEED: f64 = 0.003;
/// `Map.MIN_VIEW_RADIUS` (the server's default sight) and `MAX_VIEW_RADIUS`, in tiles.
const MIN_VIEW_RADIUS: i32 = 15;
const MAX_VIEW_RADIUS: i32 = 40;

pub struct Game {
    socket: GameSocket,
    session: Session,
    target: Target,
    /// What Hello needs again on a reconnect.
    hello: HelloParams,
    pub char_id: i32,
    pub world: Option<World>,
    /// What to show while there's no world yet, or why the game ended.
    pub status: String,
    /// The round trip to the world server, ms, as its last Ping said (none from older servers).
    pub ping: Option<i32>,
    pub ended: bool,
    /// The character died (the game ended with its death).
    pub died: bool,
    /// The death's last moments in the world (`DYING_MS` of them) before the game ends: the world
    /// holds still and fades while the death is told.
    pub dying: Option<Dying>,
    /// When a white bag last landed near us (its flash round the view, `whitebag`).
    pub white_bag_at: Option<i32>,
    /// How many worlds we've entered (each `MapInfo`): a new one clears the windows' searches.
    pub maps_entered: u32,
    /// How many times we've changed worlds.
    pub reconnects: u32,
    pub camera_angle: f64,
    /// The time of the last `update`, for what happens between them (`say`).
    now: i32,
    out: Vec<ClientMessage>,
    /// The sight radius the server uses for us, and when we last changed it.
    sent_view_radius: i32,
    view_radius_sent_at: i32,
    /// Text rising over objects: damage numbers and the server's notifications.
    pub floaters: Vec<crate::view::Floater>,
    /// Speech balloons, one per speaker.
    pub bubbles: Vec<crate::view::Bubble>,
    /// The market's answers, for its window.
    pub market_events: Vec<crate::market::Event>,
    /// The Divine Forge's answers, for its window.
    pub forge_events: Vec<crate::forge::Event>,
    /// The vault window's answer (`VaultContents`), for main to hand over.
    pub vault_contents: Option<bsk_proto::VaultContents>,
    /// The Collection's (`Collection`) and the Pen Keeper's (`PetPen`) answers, likewise.
    pub collection: Option<bsk_proto::Collection>,
    pub pet_pen: Option<bsk_proto::PetPen>,
    /// Quests, daily rewards and achievements, for the quest log (`quests.rs`).
    pub quest_events: Vec<crate::quests::Event>,
    /// The community goals and their banners, for their window (`community.rs`).
    pub community_events: Vec<crate::community::Event>,
    /// The server's chat lines held back while a community claim is answered and its loot rolls
    /// play on the reel, so the chat doesn't tell what they land on first (`claim_chat`).
    claim_hold: Option<ClaimHold>,
    /// Sound effects to play: (name, volume, is it a weapon's shot).
    pub sounds: Vec<(String, f32, bool)>,
    /// The map's music (`MapInfo.music`, `SwitchMusic`).
    pub music: Option<String>,
    /// Our level last tick, to hear a level up.
    last_level: i32,
    /// Damage we've done to quest monsters and chests, by object (`Parameters.DamageCounter`).
    pub dealt: std::collections::HashMap<i32, i32>,
    /// Who hit the quest and our own damage, as the server counts them, for the quest it's
    /// about.
    pub quest_damage: Option<QuestDamage>,
    /// The last bosses (quests) we hit that died, newest first (`BOSS_HISTORY` of them).
    pub boss_history: Vec<BossFight>,
    /// The character's fame records by id (`FameStats`: kills, dungeons, shots...), as the
    /// server last sent them.
    pub fame_stats: std::collections::HashMap<u8, i32>,
    /// Who we've whispered with, newest last (Tab cycles through them).
    pub tellers: Vec<String>,
    /// The arena's wave and clock (`ArenaMenu`): wave, seconds, since when, running.
    pub arena: Option<(i32, i32, i32, bool)>,
    /// The Arena's votes (`ArenaVotes`), once asked for in this world (the first `ImminentArenaWave`
    /// asks).
    pub arena_votes: Option<bsk_proto::ArenaVotes>,
    arena_asked: bool,
    /// Staff's `/autoaim`: shots go at the nearest enemy. Only the server turns it on, with a line
    /// from `*AutoAim*` (a name no player can have); `--aim auto` asks for it.
    pub auto_aim: bool,
    /// `MapInfo.showDisplays`: the map shows your rank at the top right (the Nexus, the Vault...).
    pub show_displays: bool,
    /// Sparks, effects and area attacks.
    pub particles: crate::particles::Particles,
    /// Cherry blossom falling over the Nexus garden's forest (`<Petals/>` grounds).
    pub petals: crate::petals::Petals,
    /// The quest's object (`QuestObjId`), for the arrow and the boss bar.
    pub quest: Option<i32>,
    /// The maps seen of the last few worlds left (by game id, name and size; the squares known),
    /// so going back to one (a realm after the Nexus) shows the map as it was.
    seen_maps: Vec<(MapKey, Vec<(u32, u16)>)>,
    /// The current world's key in `seen_maps`.
    map_key: MapKey,
    /// The realm's events, side quests and waystones with its count to close (`RealmEvents`), for
    /// the events list at the top left and the minimap; none outside a realm.
    pub realm_events: Option<bsk_proto::RealmEvents>,
    /// The Nexus's places, for the minimap (`NexusPlaces`; none elsewhere).
    pub nexus_places: Vec<bsk_proto::NexusPlace>,
    /// The latest `NameResult` and `GuildResult`, for the window that asked.
    pub name_result: Option<(bool, String)>,
    pub guild_result: Option<(bool, String)>,
    pub trade: Option<Trade>,
    /// The latest invitation, and when it came.
    pub invite: Option<(Invite, i32)>,
    /// Someone's /visit waiting for our Yes or No (`VisitRequest`), in the corner.
    pub visit: Option<VisitAsk>,
    /// The pings showing in this world (`PingShow`), and whether the server knows pings.
    pub pings: crate::pings::Marks,
    /// `AccountList` 0 and 1: locked (starred) and ignored players' account ids.
    pub locked: std::collections::HashSet<String>,
    pub ignored: std::collections::HashSet<String>,
    next_teleport: i32,
    /// A held ability (`MultiPhase`: the ninja stars) whose key is down: its item. Its release
    /// (`release_ability`) throws it.
    held_ability: Option<i32>,
    /// A teleport just asked for: when, and `next_teleport` before it, put back if the server
    /// refuses it (`teleport_refused`), so a teleport that never happened costs no cooldown.
    teleport_asked: Option<(i32, i32)>,
    /// Dungeon keys the server says we have (`GlobalNotification`), as colours.
    pub keys: Vec<u32>,
    /// When merchants last spoke (`Merchant.update`'s balloons every 5 s), and which message.
    merchant_talk: std::collections::HashMap<i32, (i32, usize)>,
    /// When the ability (Space) can be used again (`Player.nextAltAttack_`).
    next_ability: i32,
    /// What we sent, by message name (logged when the game closes).
    pub counts: std::collections::BTreeMap<&'static str, u32>,
    /// Chat, oldest first (kept across worlds, as the AIR client's TextBox is).
    pub chat: Vec<crate::hud::ChatLine>,
    /// A new version is ready: the next portal's Reconnect is kept in `held` for it, not
    /// followed (see `update.rs`).
    pub hold_reconnect: bool,
    pub held: Option<Reconnect>,
    /// This connection follows a portal handed over by the version before: if its key no
    /// longer works, go to the Nexus instead.
    pub fallback_to_nexus: bool,
    /// The connection dropped while we played, and we're trying to get back in (to the Nexus).
    lost: Option<Lost>,
    /// The server refused or ended this connection itself (a Failure): its closing is final.
    refused: bool,
}

/// A connection that dropped in the game: how many times we've tried to get back, and when the
/// next try is due (none while one is under way).
struct Lost {
    tries: u32,
    next_at: Option<i32>,
}

/// How long to wait before each try at getting back in after a dropped connection, ms: about two
/// minutes in all, long enough for a restarting server or the account lock of the dropped
/// connection ("Account in Use") to clear.
const RECONNECT_DELAYS: [i32; 12] = [500, 1000, 2000, 3000, 5000, 8000, 10_000, 15_000, 15_000, 20_000, 20_000, 20_000];

/// Someone who hit a boss: their name, class (0: not known) and damage.
#[derive(Clone, Debug)]
pub struct Hitter {
    pub name: String,
    pub class: u16,
    pub damage: i32,
    /// Assist credit: what their healing and buffing counts for in the loot.
    pub assist: i32,
}

/// Who hit the quest, as the server last said, with the quest's name and type as we last saw
/// them (it may be gone by the time it dies).
#[derive(Clone, Debug)]
pub struct QuestDamage {
    pub object_id: i32,
    pub name: String,
    pub object_type: u16,
    pub hitters: Vec<Hitter>,
    pub mine: i32,
    pub mine_assist: i32,
}

/// A boss we hit that died, and who hit it.
#[derive(Clone, Debug)]
pub struct BossFight {
    pub object_id: i32,
    pub name: String,
    pub object_type: u16,
    pub max_hp: i32,
    pub hitters: Vec<Hitter>,
    pub mine: i32,
    pub mine_assist: i32,
    /// Our name then.
    pub me: String,
}

/// How many of the last bosses are kept.
pub const BOSS_HISTORY: usize = 3;

/// A trade in progress (`TradePanel`): both sides' items and what each offers.
#[derive(Clone, Debug)]
pub struct Trade {
    pub partner: String,
    pub mine: Vec<TradeItem>,
    pub theirs: Vec<TradeItem>,
    pub my_offer: Vec<bool>,
    pub their_offer: Vec<bool>,
    /// They accepted these offers; we did.
    pub they_accepted: bool,
    pub i_accepted: bool,
    /// When either side last changed its offer: accepting waits a moment after.
    pub changed_at: i32,
}

/// How long accepting waits after an offer changes, ms.
pub const TRADE_ACCEPT_DELAY: i32 = 3000;

impl Trade {
    /// `TradePanel.checkTrade`: whether each side has room for what it gets.
    pub fn space_ok(&self) -> (bool, bool) {
        let count = |offer: &[bool]| offer.iter().filter(|&&o| o).count() as i32;
        let empty = |items: &[TradeItem]| items.iter().skip(4).filter(|i| i.item <= 0).count() as i32;
        let (mine, theirs) = (count(&self.my_offer), count(&self.their_offer));
        (theirs - mine - empty(&self.mine) <= 0, mine - theirs - empty(&self.theirs) <= 0)
    }
}

/// Something someone asked of us, answered in the interact area.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Invite {
    Trade { name: String },
    Party { name: String, party_id: i32 },
    Guild { name: String, guild: String },
}

/// Someone asking to join us where we are, or asking us to come to them (`VisitRequest`): the
/// request's id, who, where, and when it runs out (the game's clock, ms). Answered with
/// `VisitAnswer`; it never blocks play.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct VisitAsk {
    pub id: i32,
    pub name: String,
    pub world: String,
    /// A /summon: we'd go to their `world`, rather than them coming to ours.
    pub summon: bool,
    pub until: i32,
    /// How long it was given (ms), for the bar running down.
    pub total: i32,
}

/// `Character.damage`: a character's hurt sound, or its death sound when it dies of it.
fn hurt_sound(data: &GameData, object_type: u16, dead: bool) -> Option<String> {
    let d = data.objects.get(&object_type)?;
    if !(d.class == ObjectClass::Character || d.player) {
        return None;
    }
    let (tag, fallback) = if dead { ("DeathSound", "monster/default_death") } else { ("HitSound", "monster/default_hit") };
    Some(d.xml.text_of(tag).map_or(fallback, str::trim).to_owned())
}

/// How long the death is shown in the world before the fame summary, ms.
pub const DYING_MS: i32 = 4200;
/// From when a click or key may skip the rest of it, ms (so the shot that was being fired
/// doesn't skip it).
pub const DYING_SKIP_MS: i32 = 900;

/// The character's death, as it plays out in the world.
pub struct Dying {
    /// When it died (the game's clock).
    pub at: i32,
    /// Who killed it, as the server says.
    pub killed_by: String,
    /// The end was asked for early (a click or a key).
    pub skipped_at: Option<i32>,
}

impl Dying {
    /// How far into the death we are, ms (a skip jumps to the closing fade).
    pub fn elapsed(&self, now: i32) -> i32 {
        let t = now - self.at;
        match self.skipped_at {
            Some(at) => t.max(DYING_MS - DYING_FADE_MS + (now - at)),
            None => t,
        }
    }
}

/// The closing fade to black, ms.
pub const DYING_FADE_MS: i32 = 700;

/// `Player.MS_BETWEEN_TELEPORT`.
const TELEPORT_COOLDOWN: i32 = 10_000;
/// `UseItem`'s use types for a held ability (the server's `START_USE` and `END_USE`).
const USE_START: u8 = 1;
const USE_END: u8 = 2;
/// How long after a teleport the server's refusal of it is looked for (ms).
const TELEPORT_REFUSAL_MS: i32 = 3_000;

/// How many chat lines to keep.
const CHAT_KEEP: usize = 500;

/// How long a community claim's chat lines wait for its answer before they go anyway (ms).
const CLAIM_HOLD_MS: i32 = 5000;

/// The server's chat lines held from a community claim until its reel has played: the lines,
/// when the holding started (the first check after the claim), and whether it's been answered.
struct ClaimHold {
    lines: Vec<bsk_proto::Text>,
    since: Option<i32>,
    answered: bool,
}

/// A world's map, to know it again: its game id, name and size.
type MapKey = (i32, String, i32, i32);
/// How many worlds' maps are kept after leaving them.
const SEEN_MAPS: usize = 3;

impl Game {
    /// Connect and load a character, or create one (`Join::Create`).
    pub fn start(target: Target, hello: HelloParams, join: Join) -> Self {
        let socket = GameSocket::connect(&target);
        let char_id = match join {
            Join::Load { char_id } => char_id,
            Join::Create { .. } => -1,
        };
        let (session, first) = Session::start(clone_hello(&hello), join);
        let mut game = Self {
            socket,
            session,
            status: tf!("Connecting to {host}:{port}…", host = target.host, port = target.port),
            ping: None,
            target,
            hello,
            char_id,
            world: None,
            ended: false,
            died: false,
            dying: None,
            white_bag_at: None,
            maps_entered: 0,
            reconnects: 0,
            camera_angle: DEFAULT_CAMERA_ANGLE,
            now: 0,
            out: Vec::new(),
            sent_view_radius: MIN_VIEW_RADIUS,
            view_radius_sent_at: i32::MIN / 2,
            floaters: Vec::new(),
            bubbles: Vec::new(),
            tellers: Vec::new(),
            sounds: Vec::new(),
            market_events: Vec::new(),
            forge_events: Vec::new(),
            vault_contents: None,
            collection: None,
            pet_pen: None,
            quest_events: Vec::new(),
            community_events: Vec::new(),
            claim_hold: None,
            music: None,
            last_level: 0,
            dealt: Default::default(),
            quest_damage: None,
            boss_history: Vec::new(),
            fame_stats: Default::default(),
            arena: None,
            arena_votes: None,
            arena_asked: false,
            auto_aim: false,
            show_displays: false,
            particles: Default::default(),
            petals: Default::default(),
            quest: None,
            seen_maps: Vec::new(),
            map_key: (0, String::new(), 0, 0),
            realm_events: None,
            nexus_places: Vec::new(),
            name_result: None,
            guild_result: None,
            trade: None,
            invite: None,
            visit: None,
            pings: Default::default(),
            locked: Default::default(),
            ignored: Default::default(),
            next_teleport: 0,
            held_ability: None,
            teleport_asked: None,
            keys: Vec::new(),
            merchant_talk: Default::default(),
            next_ability: 0,
            counts: Default::default(),
            chat: Vec::new(),
            hold_reconnect: false,
            held: None,
            fallback_to_nexus: false,
            lost: None,
            refused: false,
        };
        game.socket.send(&first);
        game
    }

    /// `GameServerConnection.onReconnect`: connect again, to another server if the message
    /// names one, with its game id and key.
    pub fn reconnect(&mut self, r: &Reconnect) {
        self.reconnects += 1;
        self.socket.close();
        if !r.host.is_empty() {
            self.target.host = r.host.clone();
            self.target.port = r.port as u16;
        }
        self.hello.game_id = r.game_id;
        self.hello.key_time = r.key_time;
        self.hello.key = r.key.0.clone();
        self.refused = false;
        self.socket = GameSocket::connect(&self.target);
        let (session, first) = Session::start(clone_hello(&self.hello), Join::Load { char_id: self.char_id });
        self.session = session;
        self.socket.send(&first);
        self.remember_map();
        self.world = None;
        // a trade never follows us into another world: the server ends it when we leave, but its
        // TradeDone can be lost in the switch (both players taking the same portal)
        self.trade = None;
        self.sent_view_radius = MIN_VIEW_RADIUS;
        self.status = format!("Entering {}…", r.name);
    }

    /// Keeps the map seen of the world being left (`seen_maps`), the oldest let go past SEEN_MAPS.
    fn remember_map(&mut self) {
        let Some(old) = &self.world else { return };
        self.seen_maps.retain(|(k, _)| *k != self.map_key);
        self.seen_maps.push((self.map_key.clone(), old.known_squares()));
        if self.seen_maps.len() > SEEN_MAPS {
            self.seen_maps.remove(0);
        }
    }

    /// Connect again, to the Nexus.
    fn go_to_nexus(&mut self) {
        let nexus = Reconnect {
            name: "Nexus".into(),
            host: String::new(),
            port: self.target.port as i32,
            game_id: bsk_session::NEXUS_GAME_ID,
            key_time: -1,
            key: Default::default(),
        };
        self.reconnect(&nexus);
    }

    /// The connection closed without the server refusing it (a server or the network went
    /// away): if we were in the game, schedule a try at getting back in, to the Nexus. Returns
    /// whether one is scheduled; if not, the game ends as before.
    fn retry_after_drop(&mut self, now: i32) -> bool {
        let playing = self.world.as_ref().is_some_and(|w| w.me.is_some());
        let tries = match &self.lost {
            Some(lost) => lost.tries,
            None if playing && !self.died && !self.refused => 0,
            None => return false,
        };
        let Some(delay) = RECONNECT_DELAYS.get(tries as usize) else {
            self.lost = None;
            return false;
        };
        self.socket.close();
        self.remember_map();
        self.world = None;
        self.trade = None;
        self.invite = None;
        self.status = tr("Connection lost: getting you back in…").into();
        self.lost = Some(Lost { tries: tries + 1, next_at: Some(now + delay) });
        true
    }

    /// Where a Reconnect goes: its own server, or this one when it names none.
    #[cfg_attr(target_arch = "wasm32", allow(dead_code))] // updates are desktop-only
    pub fn reconnect_target(&self, r: &Reconnect) -> (String, u16) {
        if r.host.is_empty() {
            (self.target.host.clone(), self.target.port)
        } else {
            (r.host.clone(), r.port as u16)
        }
    }

    /// Handle everything the server sent since the last frame.
    pub fn poll(&mut self, now: i32, data: &GameData) {
        // A portal kept for the new version: this connection is closed, and its closing isn't
        // the game ending.
        if self.held.is_some() {
            return;
        }
        // Dead: the world holds still (nothing more is read: the server is done with us) until
        // the death has been shown.
        if let Some(d) = &self.dying {
            if d.elapsed(now) >= DYING_MS {
                self.ended = true;
            }
            return;
        }
        if let Some(Lost { tries, next_at: Some(at) }) = &self.lost {
            if now < *at {
                return;
            }
            let tries = *tries;
            log::info!("reconnecting to the Nexus (try {tries})");
            self.go_to_nexus();
            self.status = tr("Connection lost: getting you back in…").into();
            self.lost = Some(Lost { tries, next_at: None });
        }
        while let Some(event) = self.socket.poll() {
            match event {
                SocketEvent::Connected => {
                    if self.lost.is_none() {
                        self.status = tr("Connected, entering the game…").into();
                    }
                }
                SocketEvent::Closed(why) => {
                    log::info!("disconnected: {why}");
                    if self.retry_after_drop(now) {
                        break;
                    }
                    // The server said why it ended this connection (a Failure): keep its words,
                    // not the socket's closing. A connection that never opened: say so plainly,
                    // not with the socket's error and address.
                    self.status = if self.refused && !self.status.is_empty() {
                        std::mem::take(&mut self.status)
                    } else if why.starts_with("can't connect") || why.contains("/ws/") {
                        tr("Can't reach the game server. Check your internet connection and try again.").into()
                    } else {
                        tf!("Disconnected: {why}", why)
                    };
                    self.ended = true;
                }
                SocketEvent::Message(msg) => self.handle(&msg, now, data),
            }
            // dead: nothing after the death is read (the connection closing would end it now)
            if self.ended || self.dying.is_some() {
                break;
            }
        }
    }

    /// The quest's damage as the server sent it, with its name and type while we can see it.
    fn set_quest_damage(&mut self, object_id: i32, object_type: Option<u16>, hitters: Vec<Hitter>, mine: (i32, i32), data: &GameData) {
        let seen = self.world.as_ref().and_then(|w| w.objects.get(&object_id)).map(|o| {
            let desc = data.objects.get(&o.object_type);
            let name = o.name().map(str::to_owned).or_else(|| desc.map(|d| d.display_id.clone())).unwrap_or_default();
            (name, o.object_type)
        });
        let kept = self.quest_damage.as_ref().filter(|q| q.object_id == object_id).map(|q| (q.name.clone(), q.object_type));
        // Gone already (it died the moment we heard of it): named by its type, as the server
        // says it.
        let named = object_type.map(|t| (data.objects.get(&t).map(|d| d.display_id.clone()).unwrap_or_default(), t));
        let (name, object_type) = seen.or(kept).or(named).unwrap_or_default();
        self.quest_damage = Some(QuestDamage { object_id, name, object_type, hitters, mine: mine.0, mine_assist: mine.1 });
    }

    fn handle(&mut self, msg: &ServerMessage, now: i32, data: &GameData) {
        // The session answers first (a NewTick's Move goes out before its statuses apply, as
        // GameServerConnection.onNewTick does).
        let event = self.session.handle(msg, now, &mut self.out);
        // Newer servers put the recent round trip in each Ping, -1 until they've measured one
        // (older ones sent a lifetime average of half of it).
        if let ServerMessage::Ping(p) = msg {
            self.ping = (p.round_trip_time >= 0).then_some(p.round_trip_time);
        }
        // Shots from an owner we don't know (or that died) are acknowledged with -1, as
        // GameServerConnection.onEnemyShoot / onServerPlayerShoot do.
        let unknown_owner = |id: i32| self.world.as_ref().and_then(|w| w.objects.get(&id)).is_none_or(|o| o.dead);
        let owner = match msg {
            ServerMessage::EnemyShoot(e) => Some(e.owner_id),
            ServerMessage::ServerPlayerShoot(s) => Some(s.owner_id),
            _ => None,
        };
        if owner.is_some_and(unknown_owner) {
            for m in self.out.iter_mut() {
                if let ClientMessage::ShootAck(ack) = m {
                    ack.time = -1;
                }
            }
        }
        let sent_move = self.out.iter().any(|m| matches!(m, ClientMessage::Move(_)));
        // on stacked floors, the height we walk at goes just before the Move
        if let Some(world) = self.world.as_ref().filter(|w| sent_move && w.levels.is_some()) {
            let at = self.out.iter().position(|m| matches!(m, ClientMessage::Move(_))).unwrap_or(0);
            self.out.insert(at, Height { z: world.my_z() as f32 }.into());
        }
        for m in self.out.drain(..) {
            self.socket.send(&m);
        }
        if let Some(world) = &mut self.world {
            if sent_move {
                world.on_move(data);
            }
        }
        match event {
            Some(Event::MapInfo(info)) => {
                self.remember_map();
                self.maps_entered += 1;
                self.held_ability = None;
                // the last world's quest: its id is some other object here (a building, a wall),
                // whose picture the quest arrow drew at the view's edge until a new quest came
                self.quest = None;
                self.white_bag_at = None;
                self.map_key = (self.hello.game_id, info.name.clone(), info.width as i32, info.height as i32);
                let mut world = World::new(info.width as i32, info.height as i32, &info.display_name, info.seed);
                world.background = info.background;
                log::info!("map {} ({}x{}), background {}", info.name, info.width, info.height, info.background);
                if let Some((_, squares)) = self.seen_maps.iter().find(|(k, _)| *k == self.map_key) {
                    world.restore_squares(squares);
                }
                self.world = Some(world);
                self.particles.clear();
                self.arena = None;
                self.arena_votes = None;
                self.arena_asked = false;
                self.pings.clear();
                self.show_displays = info.show_displays;
                self.music = Some(info.music.clone());
                self.last_level = 0;
                self.dealt.clear();
                self.status = tf!("Entering {world}…", world = info.display_name);
            }
            Some(Event::Joined { object_id, char_id }) => {
                if let Some(lost) = self.lost.take() {
                    log::info!("back in after a dropped connection ({} tries)", lost.tries);
                    self.add_chat("", "", tr("The connection dropped: you're back in the Nexus."), 0, 0, -1, now);
                }
                // A new character has its id now; reconnects load it.
                self.char_id = char_id;
                if let Some(w) = &mut self.world {
                    w.me = Some(object_id);
                }
                // The quest's top damage dealers, from now on.
                self.quest_damage = None;
                self.fallback_to_nexus = false;
                self.socket.send(&bsk_proto::QuestDamageSubscribe {}.into());
                self.socket.send(&bsk_proto::QuestDamageFullSubscribe {}.into());
                self.socket.send(&bsk_proto::FameStatsSubscribe {}.into());
                // a realm's events, for the events list (a world that isn't a realm sends none)
                self.realm_events = None;
                self.socket.send(&bsk_proto::RealmEventsSubscribe {}.into());
                // the Nexus's places, for the minimap
                self.nexus_places.clear();
                if self.map_name() == "Nexus" {
                    self.socket.send(&bsk_proto::NexusPlacesRequest {}.into());
                }
                // quests: the HUD's badge counts what waits, so ask in every world
                self.socket.send(&bsk_proto::QuestsRequest {}.into());
                // community goals: to say when one of their rewards waits, and for their banners
                self.socket.send(&bsk_proto::CommunityRequest {}.into());
                // we can show visit and summon requests, in the corner
                self.socket.send(&bsk_proto::VisitAnswer { id: 0, answer: 255 }.into());
                self.socket.send(&bsk_proto::VisitAnswer { id: 0, answer: 254 }.into());
                // we can show pings (a server that knows them answers, and only then do we ping)
                self.socket.send(&bsk_proto::PingMark { kind: crate::pings::HELLO, x: 0.0, y: 0.0, z: 0.0, target_id: 0 }.into());
                self.status.clear();
            }
            Some(Event::Failure { description, .. }) => {
                // A handed-over portal whose key ran out: the Nexus instead.
                if std::mem::take(&mut self.fallback_to_nexus) && self.world.as_ref().is_none_or(|w| w.me.is_none()) {
                    log::info!("{description}: going to the Nexus instead");
                    self.go_to_nexus();
                    return;
                }
                // Getting back in after a dropped connection: the server may not have let go of
                // the old one yet. Try again in a moment (when this connection's Closed comes).
                if self.lost.is_some() && description.starts_with("Account in Use") {
                    log::info!("{description}: trying again");
                    self.socket.close();
                    return;
                }
                self.lost = None;
                self.refused = true;
                self.status = description;
            }
            Some(Event::Reconnect(r)) => {
                if self.hold_reconnect {
                    // Kept for the new version, which goes through the portal instead.
                    self.socket.close();
                    self.world = None;
                    self.status = format!("Entering {}…", r.name);
                    self.held = Some(r);
                    return;
                }
                self.reconnect(&r);
                return;
            }
            None => {}
        }
        match msg {
            ServerMessage::QuestObjId(q) => self.quest = Some(q.object_id),
            ServerMessage::QuestDamage(d) => {
                let hitters = d.top.iter().map(|s| Hitter { name: s.name.clone(), class: 0, damage: s.damage, assist: 0 }).collect();
                self.set_quest_damage(d.object_id, None, hitters, (d.mine, 0), data);
            }
            ServerMessage::FameStats(f) => self.fame_stats = f.stats.iter().map(|s| (s.id, s.value)).collect(),
            ServerMessage::QuestDamageFull(d) => {
                let mut hitters: Vec<Hitter> = d.hitters.iter().map(|h| Hitter { name: h.name.clone(), class: h.class, damage: h.damage, assist: 0 }).collect();
                // assist credit: on the damage dealer's row, or a row of its own for a helper
                // who never hit it; then most of both first
                for a in d.assists.iter().flat_map(|a| a.iter()) {
                    match hitters.iter_mut().find(|h| h.name == a.name) {
                        Some(h) => h.assist = a.damage,
                        None => hitters.push(Hitter { name: a.name.clone(), class: a.class, damage: 0, assist: a.damage }),
                    }
                }
                hitters.sort_by_key(|h| std::cmp::Reverse(h.damage + h.assist));
                let mine_assist = d.mine_assist.unwrap_or(0);
                self.set_quest_damage(d.object_id, Some(d.object_type), hitters, (d.mine, mine_assist), data);
                // It died: kept if we hit it or helped.
                if d.dead && (d.mine > 0 || mine_assist > 0) {
                    if let Some(q) = self.quest_damage.clone().filter(|q| q.object_id == d.object_id) {
                        self.boss_history.retain(|b| b.object_id != q.object_id);
                        let me = self.world.as_ref().and_then(|w| w.player()).and_then(|p| p.name()).unwrap_or("").to_owned();
                        self.boss_history.insert(0, BossFight { object_id: q.object_id, name: q.name, object_type: q.object_type, max_hp: d.max_hp, hitters: q.hitters, mine: d.mine, mine_assist, me });
                        self.boss_history.truncate(BOSS_HISTORY);
                    }
                }
            }
            ServerMessage::TradeRequested(t) => {
                self.invite = Some((Invite::Trade { name: t.name.clone() }, now));
                self.add_chat("", "", &tf!("{name} wants to trade with you.", name = t.name), 0, 0, -1, now);
            }
            ServerMessage::TradeStart(t) => {
                self.invite = None;
                self.trade = Some(Trade {
                    partner: t.your_name.clone(),
                    my_offer: t.my_items.0.iter().map(|i| i.included).collect(),
                    their_offer: t.your_items.0.iter().map(|i| i.included).collect(),
                    mine: t.my_items.0.clone(),
                    theirs: t.your_items.0.clone(),
                    they_accepted: false,
                    i_accepted: false,
                    changed_at: now,
                });
            }
            ServerMessage::TradeChanged(c) => {
                if let Some(trade) = &mut self.trade {
                    trade.their_offer = c.offer.0.clone();
                    (trade.they_accepted, trade.i_accepted, trade.changed_at) = (false, false, now);
                }
            }
            ServerMessage::TradeAccepted(a) => {
                if let Some(trade) = &mut self.trade {
                    trade.they_accepted = a.my_offer.0 == trade.my_offer && a.your_offer.0 == trade.their_offer;
                }
            }
            ServerMessage::TradeDone(d) => {
                self.trade = None;
                // GameServerConnection.onTradeDone: a server line, whatever the code.
                self.add_chat("", "", &d.description, 0, 0, -1, now);
            }
            ServerMessage::InvitedToParty(p) => self.invite = Some((Invite::Party { name: p.name.clone(), party_id: p.party_id }, now)),
            ServerMessage::InvitedToGuild(g) => {
                self.invite = Some((Invite::Guild { name: g.name.clone(), guild: g.guild_name.clone() }, now));
            }
            ServerMessage::NameResult(n) => {
                if n.success {
                    self.add_chat("", "", tr("Your name has been changed."), 0, 0, -1, now);
                }
                self.name_result = Some((n.success, n.error_text.clone()));
            }
            ServerMessage::GuildResult(g) => {
                self.guild_result = Some((g.success, g.error_text.clone()));
                if !g.error_text.is_empty() {
                    self.add_chat(if g.success { "" } else { "*Error*" }, "", &g.error_text, 0, 0, -1, now);
                }
            }
            ServerMessage::AccountList(l) => {
                let ids: std::collections::HashSet<String> = l.account_ids.0.iter().cloned().collect();
                match l.account_list_id {
                    0 => self.locked = ids,
                    1 => self.ignored = ids,
                    _ => {}
                }
            }
            ServerMessage::BuyResult(b) => {
                let name = if b.result == 0 { "" } else { "*Error*" };
                self.add_chat(name, "", &b.result_string, 0, 0, -1, now);
            }
            ServerMessage::GlobalNotification(n) => {
                let key = match n.text.as_str() {
                    "yellow" => Some(0xF5D33C),
                    "red" => Some(0xE04040),
                    "green" => Some(0x4CC94C),
                    "purple" => Some(0xA060E0),
                    _ => None,
                };
                if let Some(k) = key {
                    if !self.keys.contains(&k) {
                        self.keys.push(k);
                    }
                }
            }
            _ => {}
        }
        if let ServerMessage::Death(d) = msg {
            if d.char_id == self.char_id {
                log::info!("died, killed by {}", d.killed_by);
                self.status = tf!("You died, killed by {by}.", by = d.killed_by);
                self.died = true;
                self.dying = Some(Dying { at: now, killed_by: d.killed_by.clone(), skipped_at: None });
                // Our death in the world: a burst of our colours and a ring of red where we fell,
                // and our class's death cry.
                if let Some(world) = &self.world {
                    if let Some(me) = world.player() {
                        let (x, y, t) = (me.x, me.y, me.object_type);
                        self.particles.damaged(world, me.id, true);
                        self.particles.aoe(x, y, 2.5, 0xE04040);
                        if let Some(sound) = hurt_sound(data, t, true) {
                            self.sounds.push((sound, 1.0, false));
                        }
                    }
                }
                return;
            }
        }
        if let ServerMessage::Text(t) = msg {
            if t.name == "*AutoAim*" {
                self.auto_aim = t.text == "on";
                return;
            }
            if t.name == "*Error*" {
                self.teleport_refused(&t.text, now);
            }
            if self.is_ignored(t.object_id) {
                return;
            }
            // a little monster's quip: a balloon over it only, not a chat line
            let bubble_only = t.recipient == "*Bubble*";
            // the server's own lines wait while a claim's rolls are shown (players' chat doesn't)
            let held = !bubble_only && t.num_stars < 0 && t.recipient.is_empty() && self.claim_hold.is_some();
            if held {
                if let Some(h) = &mut self.claim_hold {
                    h.lines.push(t.clone());
                }
            } else if !bubble_only {
                self.add_chat(&t.name, &t.recipient, &t.text, t.name_color, t.text_color, t.num_stars, now);
            }
            // GameServerConnection.onText: a balloon over the speaker, coloured by who it is.
            let speaker = self.world.as_ref().and_then(|w| w.objects.get(&t.object_id));
            if let (Some(o), true) = (speaker, t.object_id >= 0 && t.bubble_time > 0) {
                let enemy = data.objects.get(&o.object_type).is_some_and(|d| d.enemy);
                // a monster's quip is an enemy's balloon; anyone else's aside (a Nexus villager's,
                // NexusVillagers) a plain one
                use crate::options::bubble;
                let (colors, kind) = if enemy {
                    ([0x561F1C, 0xFC8642, 0xCDC0BF], bubble::MONSTERS)
                } else if bubble_only {
                    ([0xE1DEDC, 0xFFFFFF, 0x545454], bubble::NPCS)
                } else if t.recipient == "*Guild*" {
                    ([0x3E8A00, 0xA6FF5D, 0xD3F8CC], bubble::GUILD)
                } else if t.recipient == "*Party*" {
                    ([0x78A0FF, 0x5A7DD1, 0xD9E4FF], bubble::PARTY)
                } else if !t.recipient.is_empty() {
                    ([0x260AB6, 0x00F0FF, 0xD3CCF7], bubble::WHISPERS)
                } else {
                    ([0xE1DEDC, 0xFFFFFF, 0x545454], bubble::PLAYERS)
                };
                self.bubbles.retain(|b| b.object_id != t.object_id);
                self.bubbles.push(crate::view::Bubble {
                    object_id: t.object_id,
                    text: t.text.clone(),
                    colors,
                    kind,
                    at: now,
                    life: t.bubble_time as i32 * 1000,
                });
            }
        }
        let Some(world) = &mut self.world else { return };
        match msg {
            ServerMessage::Update(u) => {
                // Container.addTo: a loot bag landing within 10 tiles.
                // A white bag rings its own bell and lights up the edges of the view (whitebag.rs).
                if let Some(me) = world.player() {
                    let mut near_loot = false;
                    let mut white = false;
                    for o in &u.new_objs.0 {
                        let Some(d) = data.objects.get(&(o.object_type as u16)) else { continue };
                        if d.class == ObjectClass::Container && d.xml.has("Loot")
                            && (o.status.pos.x as f64 - me.x).hypot(o.status.pos.y as f64 - me.y) < 10.0
                        {
                            near_loot = true;
                            white |= crate::whitebag::BAGS.contains(&d.id.as_str());
                        }
                    }
                    if white {
                        self.sounds.push((crate::whitebag::SOUND.into(), 1.0, false));
                        self.white_bag_at = Some(now);
                    } else if near_loot {
                        self.sounds.push(("loot_appears".into(), 1.0, false));
                    }
                }
                world.apply_update(u, data, now);
            }
            ServerMessage::NewTick(t) => {
                world.apply_new_tick(t, now);
                // Player.levelUp: our level went up.
                let level = world.player().and_then(|p| p.stat_int(crate::hud::stat::LEVEL)).unwrap_or(0);
                if self.last_level > 0 && level > self.last_level {
                    self.sounds.push(("level_up".into(), 1.0, false));
                }
                self.last_level = level;
            }
            ServerMessage::InvResult(r) if r.result != 0 => self.sounds.push(("error".into(), 1.0, false)),
            ServerMessage::PlaySound(p) => {
                // GameObject.playSound: the object's <Sound id="n">.
                let name = world.objects.get(&p.owner_id).and_then(|o| data.objects.get(&o.object_type)).and_then(|d| {
                    d.xml.children_named("Sound").find(|s| s.attr_int("id", -1) == p.sound_id as i32).map(|s| s.text.trim().to_owned())
                });
                if let Some(name) = name {
                    self.sounds.push((name, 1.0, false));
                }
            }
            ServerMessage::SwitchMusic(m) => self.music = Some(m.music.clone()),
            ServerMessage::MarketListings(m) => self.market_events.push(crate::market::Event::Listings(m.clone())),
            ServerMessage::MarketResult(r) => self.market_events.push(crate::market::Event::Result(r.clone())),
            ServerMessage::ForgeData(d) => self.forge_events.push(crate::forge::Event::Data(
                d.recipes.0.clone(),
                d.prices.0.clone(),
                d.storage_costs.as_ref().map_or_else(Vec::new, |c| c.0.clone()),
            )),
            ServerMessage::ForgeResult(r) => self.forge_events.push(crate::forge::Event::Result {
                sold: r.sold,
                success: r.success,
                item: r.item,
                fame: r.fame,
                message: r.message.clone(),
            }),
            ServerMessage::ForgeShards(s) => {
                self.forge_events.push(crate::forge::Event::Shards(s.shards.0.clone(), s.potions.as_ref().map_or_else(Vec::new, |p| p.0.clone())))
            }
            ServerMessage::ForgeVault(v) => self.forge_events.push(crate::forge::Event::Vault(v.items.0.clone())),
            ServerMessage::VaultContents(v) => self.vault_contents = Some(v.clone()),
            ServerMessage::Collection(c) => self.collection = Some(c.clone()),
            ServerMessage::PetPen(p) => self.pet_pen = Some(p.clone()),
            ServerMessage::ImprintVault(v) => self.forge_events.push(crate::forge::Event::ImprintVault(v.items.0.clone())),
            ServerMessage::RealmEvents(r) => self.realm_events = Some(r.clone()),
            ServerMessage::NexusPlaces(n) => {
                log::debug!("nexus places: {:?}", n.places.iter().map(|p| (p.name.as_str(), p.x as i32, p.y as i32, p.icon)).collect::<Vec<_>>());
                self.nexus_places = n.places.0.clone();
            }
            ServerMessage::CorruptResult(r) => self.forge_events.push(crate::forge::Event::Corrupt {
                success: r.success,
                message: r.message.clone(),
                changes: [r.damage as i8 as i32, r.range as i8 as i32, r.rate as i8 as i32, r.stats as i8 as i32],
                reversed: r.reversed,
            }),
            ServerMessage::ImprintResult(r) => {
                self.forge_events.push(crate::forge::Event::Imprint { success: r.success, message: r.message.clone() })
            }
            ServerMessage::QuestsData(d) => self.quest_events.push(crate::quests::Event::Data(Box::new(d.clone()))),
            ServerMessage::QuestProgress(p) => self.quest_events.push(crate::quests::Event::Progress(p.changes.0.clone())),
            ServerMessage::QuestResult(r) => self.quest_events.push(crate::quests::Event::Result(r.success, r.message.clone())),
            ServerMessage::QuestNotice(n) => self.quest_events.push(crate::quests::Event::Notice(n.kind, n.title.clone(), n.text.clone())),
            ServerMessage::CommunityData(d) => self.community_events.push(crate::community::Event::Data(Box::new(d.clone()))),
            ServerMessage::CommunityNotice(n) => {
                self.community_events.push(crate::community::Event::Notice(n.kind, n.title.clone(), n.text.clone()));
                let rolls = n.rolls.as_ref().filter(|r| !r.is_empty());
                if let Some(rolls) = rolls {
                    self.community_events.push(crate::community::Event::Rolls(rolls.to_vec()));
                }
                // the claim's answer: with rolls, its lines wait for the reel; else they go now
                if let Some(h) = self.claim_hold.as_mut().filter(|_| n.kind <= 1) {
                    h.answered = true;
                    if rolls.is_none() {
                        self.claim_chat(false, now);
                    }
                }
            }
            ServerMessage::Goto(g) => world.apply_goto(g.object_id, g.pos.x as f64, g.pos.y as f64, now, data),
            ServerMessage::Layers(l) => {
                log::info!("floors: {} squares above the map, {} tiles apart", l.tiles.len(), l.height);
                world.set_layers(l.height as f64, &l.tiles)
            }
            ServerMessage::EnemyShoot(e) => {
                let Some(owner) = world.objects.get(&e.owner_id).filter(|o| !o.dead) else { return };
                let container = owner.object_type;
                let (num, inc) = e.spread.map_or((1, 0.0), |s| (s.num_shots as i32, s.angle_inc as f64));
                for i in 0..num {
                    let angle = e.angle as f64 + inc * i as f64;
                    let (x, y) = (e.starting_pos.x as f64, e.starting_pos.y as f64);
                    let p = Projectile::new(e.owner_id, e.bullet_id + i, container, e.bullet_type as i32, angle, now, x, y, 1.0, e.damage, true);
                    world.add_projectile(p);
                }
                if let Some(owner) = world.objects.get_mut(&e.owner_id) {
                    owner.note_attack(now, e.angle as f64 + inc * (num - 1) as f64 / 2.0);
                }
            }
            ServerMessage::ServerPlayerShoot(s) => {
                if world.objects.get(&s.owner_id).is_some_and(|o| !o.dead) {
                    let (x, y) = (s.starting_pos.x as f64, s.starting_pos.y as f64);
                    let p = Projectile::new(s.owner_id, s.bullet_id, s.container_type as u16, s.bullet_type.unwrap_or(0) as i32, s.angle as f64, now, x, y, 1.0, s.damage, false);
                    world.add_projectile(p);
                }
            }
            ServerMessage::AllyShoot(a) => {
                let Some(owner) = world.objects.get_mut(&a.owner_id).filter(|o| !o.dead) else { return };
                owner.note_attack(now, a.angle as f64);
                let (x, y) = (owner.x, owner.y);
                let p = Projectile::new(a.owner_id, a.bullet_id, a.container_type as u16, 0, a.angle as f64, now, x, y, 1.0, 0, false);
                world.add_projectile(p);
            }
            ServerMessage::Notification(n) => {
                // CharacterStatusText: two seconds over the object.
                self.floaters.push(crate::view::Floater {
                    object_id: n.object_id,
                    text: n.text.clone(),
                    color: n.color & 0xFF_FFFF,
                    at: now,
                    life: 2000,
                    others: false,
                });
            }
            ServerMessage::ArenaVotes(v) => self.arena_votes = Some(v.clone()),
            ServerMessage::PingShow(p) => self.pings.show(p, now),
            ServerMessage::VisitRequest(v) => {
                if v.seconds == 0 {
                    // taken back or run out
                    if self.visit.as_ref().is_some_and(|a| a.id == v.id) {
                        self.visit = None;
                    }
                } else {
                    let total = v.seconds as i32 * 1000;
                    let summon = v.summon == Some(true);
                    self.visit = Some(VisitAsk { id: v.id, name: v.name.clone(), world: v.world.clone(), summon, until: now + total, total });
                    let line = if summon {
                        tf!("{name} wants to summon you to their {world}: answer Yes or No in the corner, or type /yes or /no.", name = v.name, world = v.world)
                    } else {
                        tf!("{name} wants to visit you: answer Yes or No in the corner, or type /yes or /no.", name = v.name)
                    };
                    self.add_chat("", "", &line, 0, 0, -1, now);
                }
            }
            ServerMessage::ImminentArenaWave(w) => {
                // the Arena's votes, from now on (an older server counts it as one unknown message)
                if !std::mem::replace(&mut self.arena_asked, true) {
                    self.send(bsk_proto::ArenaVote { action: 255 }.into());
                }
                // ArenaMenu.onWaveInfo: state 1 starts the clock; otherwise it's set.
                let running = self.arena.is_some_and(|a| a.3);
                self.arena = Some(match (w.current_state, self.arena) {
                    (1, Some((_, secs, since, true))) => (w.wave, secs, since, true),
                    (1, Some((_, secs, _, false))) => (w.wave, secs, now, true),
                    (1, None) => (w.wave, 0, now, true),
                    _ => (w.wave, w.current_runtime / 1000, now, running),
                });
            }
            ServerMessage::Aoe(a) => self.particles.aoe(a.pos.x as f64, a.pos.y as f64, a.radius as f64, a.color),
            ServerMessage::ShowEffect(e) => {
                // another player's spell, hidden with their shots (Options, Visuals)
                let others = self.particles.hide_allies
                    && Some(e.target_object_id) != world.me
                    && world.objects.get(&e.target_object_id).and_then(|o| data.objects.get(&o.object_type)).is_some_and(|d| d.player);
                if !others {
                    self.particles.show_effect(&e, world, now);
                }
            }
            ServerMessage::Damage(d) => {
                // The server never sends us our own shots' hits (we show those as they strike), so
                // one credited to us is our ability's (poison, lightning, a trap...): show it too.
                self.particles.damaged(world, d.target_id, d.kill);
                if let Some(s) = world.objects.get(&d.target_id).and_then(|o| hurt_sound(data, o.object_type, d.kill)) {
                    self.sounds.push((s, 1.0, false));
                }
                if d.damage_amount > 0 {
                    // purple when it ignored defense (an imprint's burst, poison); another player's
                    // hit on someone else, marked to hide with Options, Gameplay
                    let others = Some(d.object_id) != world.me && Some(d.target_id) != world.me;
                    self.floaters.push(crate::view::Floater { others, ..crate::view::Floater::hit(d.target_id, d.damage_amount, d.pierce, now) });
                }
            }
            _ => {}
        }
    }

    /// `stars` is the speaker's rank, or -1 for none (the server's own lines).
    #[allow(clippy::too_many_arguments)]
    pub fn add_chat(&mut self, name: &str, recipient: &str, text: &str, name_color: i32, text_color: i32, stars: i32, now: i32) {
        log::debug!("chat {name}: {text}"); // scripted tests read results from here (RUST_LOG=debug)
        // TextBox.tellers_: who we last whispered with, newest last.
        let me = self.world.as_ref().and_then(|w| w.player()).and_then(|p| p.name()).unwrap_or("").to_owned();
        let other = if !recipient.is_empty() && recipient == me {
            Some(name)
        } else if !recipient.is_empty() && name == me && !recipient.starts_with('*') {
            Some(recipient)
        } else {
            None
        };
        if let Some(who) = other.filter(|w| !w.is_empty()) {
            if self.tellers.last().is_none_or(|l| l != who) {
                self.tellers.push(who.to_owned());
            }
        }
        self.chat.push(crate::hud::ChatLine {
            name: name.to_owned(),
            recipient: recipient.to_owned(),
            text: text.to_owned(),
            name_color,
            text_color,
            stars,
            at: now,
        });
        if self.chat.len() > CHAT_KEEP {
            self.chat.drain(..self.chat.len() - CHAT_KEEP);
        }
    }

    /// Send a chat line or command (`PlayerText`); the server echoes what others should see.
    /// `/floors`, on stacked floors, stays here: it turns every floor above the lowest
    /// see-through or back, to see what is under them.
    pub fn say(&mut self, text: &str) {
        let text = text.trim();
        if let Some(world) = self.world.as_mut().filter(|w| w.levels.is_some() && text.eq_ignore_ascii_case("/floors")) {
            world.see_through = !world.see_through;
            let line = if world.see_through { "The floors are see-through: you can see what is under them." } else { "The floors are solid again." };
            let now = self.now;
            self.add_chat("", "", line, 0, 0, -1, now);
            return;
        }
        if !text.is_empty() {
            self.socket.send(&PlayerText { text: text.to_owned() }.into());
        }
    }

    /// Fire toward the world angle `angle` (mouse held). Sends a PlayerShoot per projectile.
    pub fn shoot(&mut self, now: i32, angle: f64, data: &GameData) {
        if self.session.map.as_ref().is_some_and(|m| m.disable_shooting) {
            return;
        }
        let last_move = self.session.last_move_time().unwrap_or(now);
        let Some(world) = &mut self.world else { return };
        let shots = world.shoot(now, angle, last_move, data);
        if let Some(p) = shots.first() {
            // Player.doShoot: the weapon's sound.
            if let Some(s) = data.objects.get(&p.container_type).and_then(|d| d.xml.text_of("OldSound")) {
                self.sounds.push((s.trim().to_owned(), 0.75, true));
            }
        }
        for p in shots {
            let msg = PlayerShoot {
                time: now,
                bullet_id: p.bullet_id,
                container_type: p.container_type as i32,
                starting_pos: WorldPos { x: p.start_x as f32, y: p.start_y as f32 },
                angle: p.angle as f32,
            };
            *self.counts.entry("PlayerShoot").or_default() += 1;
            self.socket.send(&msg.into());
        }
    }

    /// Move the projectiles and tell the server what they hit.
    fn update_projectiles(&mut self, now: i32, data: &GameData) {
        let Some(world) = &mut self.world else { return };
        for hit in world.update_projectiles(now, data) {
            let msg: ClientMessage = match hit {
                // a shot stacked on one that just hit us adds no sound, and no number unless it
                // deals more than that one did
                Hit::Player { stacked: true, bullet_id, owner_id, damage, pierced } => {
                    if damage > 0 {
                        if let Some(me) = world.me {
                            self.floaters.push(crate::view::Floater::hit(me, damage, pierced, now));
                        }
                    }
                    PlayerHit { bullet_id, object_id: owner_id }.into()
                }
                Hit::Player { bullet_id, owner_id, damage, pierced, .. } => {
                    if let Some(s) = world.player().and_then(|p| hurt_sound(data, p.object_type, false)) {
                        self.sounds.push((s, 1.0, false));
                    }
                    if let Some(me) = world.me {
                        self.floaters.push(crate::view::Floater::hit(me, damage, pierced, now));
                    }
                    PlayerHit { bullet_id, object_id: owner_id }.into()
                }
                Hit::Enemy { time, bullet_id, target_id, kill, damage, pierced } => {
                    let counted = world.objects.get(&target_id).and_then(|o| data.objects.get(&o.object_type)).is_some_and(|d| d.xml.has("Quest") || d.xml.has("Chest"));
                    if counted {
                        *self.dealt.entry(target_id).or_default() += damage;
                    }
                    if let Some(s) = world.objects.get(&target_id).and_then(|o| hurt_sound(data, o.object_type, kill)) {
                        self.sounds.push((s, 1.0, false));
                    }
                    self.floaters.push(crate::view::Floater::hit(target_id, damage, pierced, now));
                    EnemyHit { time, bullet_id, target_id, kill }.into()
                }
                Hit::Other { time, bullet_id, owner_id, target_id } => {
                    OtherHit { time, bullet_id, object_id: owner_id, target_id }.into()
                }
                Hit::Square { time, bullet_id, owner_id } => SquareHit { time, bullet_id, object_id: owner_id }.into(),
                Hit::Ground { time, x, y, damage } => {
                    if let Some(me) = world.me {
                        self.floaters.push(crate::view::Floater::damage(me, damage, now));
                    }
                    GroundDamage { time, position: WorldPos { x: x as f32, y: y as f32 } }.into()
                }
            };
            *self.counts.entry(msg.name()).or_default() += 1;
            self.socket.send(&msg);
        }
        self.floaters.retain(|f| now - f.at < f.life);
        self.bubbles.retain(|b| now - b.at < b.life);
    }

    /// Advance the world by `dt` ms with the keys held; `rotate` is -1, 0 or 1.
    pub fn update(&mut self, now: i32, dt: i32, keys: (f64, f64), rotate: f64, data: &GameData) {
        self.now = now;
        // the dead don't walk or turn
        let (keys, rotate) = if self.dying.is_some() { ((0.0, 0.0), 0.0) } else { (keys, rotate) };
        self.camera_angle += dt as f64 * ROTATE_SPEED * rotate;
        let Some(world) = &mut self.world else { return };
        let input = MoveInput { x: keys.0, y: keys.1, camera_angle: self.camera_angle };
        if let Some((x, y)) = world.update(now, dt, &input, data) {
            self.session.set_position(WorldPos { x: x as f32, y: y as f32 }, now);
        }
        self.update_projectiles(now, data);
        if let Some(world) = &mut self.world {
            self.particles.impacts(world);
            self.particles.update(world, now, dt);
            let everywhere = crate::petals::map_strength(self.session.map.as_ref().map_or("", |m| m.name.as_str()));
            self.petals.update(world, data, now, dt, everywhere);
        }
    }

    /// `GameSprite`'s VIEW_RADIUS: ask the server to send as far as the screen reaches
    /// (`far` tiles from us), 15 to 40, at most once a second when it changes.
    pub fn want_view_radius(&mut self, far: f64, now: i32) {
        let radius = (far.ceil() as i32).clamp(MIN_VIEW_RADIUS, MAX_VIEW_RADIUS);
        let in_game = self.world.as_ref().is_some_and(|w| w.player().is_some());
        if in_game && radius != self.sent_view_radius && now - self.view_radius_sent_at >= 1000 {
            self.socket.send(&ViewRadius { radius }.into());
            self.sent_view_radius = radius;
            self.view_radius_sent_at = now;
        }
    }

    fn send(&mut self, msg: ClientMessage) {
        *self.counts.entry(msg.name()).or_default() += 1;
        self.socket.send(&msg);
    }

    /// Ping (the middle mouse button), if the server knows pings and its limits allow one now.
    /// False when it didn't go.
    pub fn ping(&mut self, mark: bsk_proto::PingMark, now: i32) -> bool {
        if !self.pings.ready || self.world.as_ref().is_none_or(|w| w.me.is_none()) || !self.pings.take(now) {
            return false;
        }
        log::info!("ping {} at ({:.1}, {:.1}) on {}", mark.kind, mark.x, mark.y, mark.target_id);
        self.send(mark.into());
        true
    }

    /// Set an inventory slot locally until the server's update arrives (the AIR client changes
    /// `equipment_` as it sends).
    fn predict(&mut self, owner: i32, slot: u8, item: i32) {
        // The potion stacks (254, 255) count themselves.
        if crate::hud::slot_stat(slot) == crate::hud::stat::NONE {
            return;
        }
        if let Some(o) = self.world.as_mut().and_then(|w| w.objects.get_mut(&owner)) {
            o.stats.insert(crate::hud::slot_stat(slot), bsk_proto::StatValue::Int(item));
        }
    }

    /// `GameServerConnection.invSwap`.
    pub fn inv_swap(&mut self, now: i32, a: (i32, u8, i32), b: (i32, u8, i32)) {
        let Some(me) = self.world.as_ref().and_then(|w| w.player()) else { return };
        let position = WorldPos { x: me.x as f32, y: me.y as f32 };
        let msg = InvSwap {
            time: now,
            position,
            slot_object1: SlotObject { object_id: a.0, slot_id: a.1, object_type: a.2 },
            slot_object2: SlotObject { object_id: b.0, slot_id: b.1, object_type: b.2 },
        };
        self.send(msg.into());
        self.predict(a.0, a.1, b.2);
        self.predict(b.0, b.1, a.2);
        self.sounds.push(("inventory_move_item".into(), 1.0, false));
    }

    /// `ItemGridMediator.addToPotionStack`: a health or magic potion from a slot into its stack
    /// (slot 254 or 255 of the player).
    pub fn add_to_stack(&mut self, now: i32, from: (i32, u8, i32)) {
        let Some((k, (_, stack_slot))) = crate::hud::POTIONS.iter().enumerate().find(|(_, p)| p.0 == from.2) else { return };
        let Some(me) = self.world.as_ref().and_then(|w| w.player()) else { return };
        let (id, position) = (me.id, WorldPos { x: me.x as f32, y: me.y as f32 });
        let count_stat = if k == 0 { crate::hud::stat::HEALTH_POTION_STACK } else { crate::hud::stat::MAGIC_POTION_STACK };
        let count = me.stat_int(count_stat).unwrap_or(0);
        // A full stack takes no more (PotionModel.maxPotionCount).
        if count >= crate::hud::MAX_STACKED {
            self.sounds.push(("error".into(), 1.0, false));
            return;
        }
        let msg = InvSwap {
            time: now,
            position,
            slot_object1: SlotObject { object_id: from.0, slot_id: from.1, object_type: from.2 },
            slot_object2: SlotObject { object_id: id, slot_id: *stack_slot, object_type: -1 },
        };
        self.send(msg.into());
        self.predict(from.0, from.1, -1);
        if let Some(p) = self.world.as_mut().and_then(|w| w.objects.get_mut(&id)) {
            p.stats.insert(count_stat, bsk_proto::StatValue::Int(count + 1));
        }
        self.sounds.push(("inventory_move_item".into(), 1.0, false));
    }

    /// `useItem_new`: use a consumable (or InvUse item) in a slot. False if it can't be used.
    pub fn use_item(&mut self, now: i32, owner: i32, slot: u8, item: i32, data: &GameData) -> bool {
        let Some(xml) = data.objects.get(&(item as u16)).map(|d| &d.xml) else { return false };
        if !(xml.has("Consumable") || xml.has("InvUse")) {
            self.sounds.push(("error".into(), 1.0, false));
            return false;
        }
        self.sounds.push(("use_potion".into(), 1.0, false));
        let consumable = xml.has("Consumable");
        let msg = UseItem {
            time: now,
            slot_object: SlotObject { object_id: owner, slot_id: slot, object_type: item },
            item_use_pos: WorldPos { x: 0.0, y: 0.0 },
            use_type: 0,
        };
        self.send(msg.into());
        if consumable {
            self.predict(owner, slot, -1);
        }
        true
    }

    /// `breakdownSlot` (Ctrl+right-click): break the item in a slot down. Only slots the
    /// server allows (it disconnects for others): our inventory and backpack, or a bag's.
    pub fn breakdown(&mut self, now: i32, owner: i32, slot: u8) {
        let mine = self.world.as_ref().and_then(|w| w.me) == Some(owner);
        if (mine && !(4..20).contains(&slot)) || (!mine && slot >= 8) {
            return;
        }
        self.send(bsk_proto::BreakdownSlot { time: now, slot, object_id: owner }.into());
    }

    /// `invDrop`: put it on the ground.
    pub fn inv_drop(&mut self, owner: i32, slot: u8, item: i32) {
        self.send(InvDrop { slot_object: SlotObject { object_id: owner, slot_id: slot, object_type: item } }.into());
        self.predict(owner, slot, -1);
    }

    /// `Player.useAltWeapon` (Space): the ability in slot 1, aimed at a world point; `angle`
    /// is the world direction of the mouse, for abilities that shoot.
    pub fn use_ability(&mut self, now: i32, x: f64, y: f64, angle: f64, data: &GameData) {
        if self.session.map.as_ref().is_some_and(|m| m.disable_abilities) {
            return;
        }
        let Some(me) = self.world.as_ref().and_then(|w| w.player()) else { return };
        let item = me.stat_int(crate::hud::slot_stat(1)).unwrap_or(-1);
        let mp = me.stat_int(crate::hud::stat::MP).unwrap_or(0);
        // Silenced (the Infernal Brimstone Poison's user): no ability, as the server refuses it
        let paused = [bsk_world::condition::PAUSED, bsk_world::condition::SILENCED].iter().any(|&c| bsk_world::condition::has(me.conditions(), c));
        let id = me.id;
        let Some(xml) = data.objects.get(&(item as u16)).map(|d| &d.xml) else { return };
        if now < self.next_ability {
            return;
        }
        // Player.useAltWeapon: an error, or not enough mana.
        if paused || !xml.has("Usable") {
            self.sounds.push(("error".into(), 1.0, false));
            return;
        }
        if xml.int("MpCost", 0) > mp {
            self.sounds.push(("no_mana".into(), 1.0, false));
            return;
        }
        // A held ability (the ninja stars): the key going down starts it (its effect while held,
        // mana draining); letting go throws it (`release_ability`), as `Player.useAltWeapon` did.
        if xml.has("MultiPhase") {
            if self.held_ability.is_some() {
                return;
            }
            self.held_ability = Some(item);
            self.next_ability = now + (xml.number("Cooldown", 0.5) * 1000.0) as i32;
            let msg = UseItem {
                time: now,
                slot_object: SlotObject { object_id: id, slot_id: 1, object_type: item },
                item_use_pos: WorldPos { x: x as f32, y: y as f32 },
                use_type: USE_START,
            };
            self.send(msg.into());
            return;
        }
        self.next_ability = now + (xml.number("Cooldown", 0.5) * 1000.0) as i32;
        let shoots = xml.children_named("Activate").any(|a| a.text.trim() == "Shoot");
        let msg = UseItem {
            time: now,
            slot_object: SlotObject { object_id: id, slot_id: 1, object_type: item },
            item_use_pos: WorldPos { x: x as f32, y: y as f32 },
            use_type: 1,
        };
        self.send(msg.into());
        if shoots {
            let last_move = self.session.last_move_time().unwrap_or(now);
            let Some(world) = &mut self.world else { return };
            for p in world.shoot_ability(now, item, angle, last_move, data) {
                let msg = PlayerShoot {
                    time: now,
                    bullet_id: p.bullet_id,
                    container_type: p.container_type as i32,
                    starting_pos: WorldPos { x: p.start_x as f32, y: p.start_y as f32 },
                    angle: p.angle as f32,
                };
                self.send(msg.into());
            }
        }
    }

    /// Whether a held ability's key is down (`use_ability` started a `MultiPhase` one).
    pub fn ability_held(&self) -> bool {
        self.held_ability.is_some()
    }

    /// The held ability's key came up: it ends, and with the mana for it (`MpEndCost`) its
    /// projectiles fly toward `angle` (`Player.useAltWeapon`'s release).
    pub fn release_ability(&mut self, now: i32, x: f64, y: f64, angle: f64, data: &GameData) {
        let Some(item) = self.held_ability.take() else { return };
        let Some(me) = self.world.as_ref().and_then(|w| w.player()) else { return };
        let (id, mp) = (me.id, me.stat_int(crate::hud::stat::MP).unwrap_or(0));
        let Some(xml) = data.objects.get(&(item as u16)).map(|d| &d.xml) else { return };
        let throws = mp >= xml.int("MpEndCost", 0) && xml.has("Projectile");
        let msg = UseItem {
            time: now,
            slot_object: SlotObject { object_id: id, slot_id: 1, object_type: item },
            item_use_pos: WorldPos { x: x as f32, y: y as f32 },
            use_type: USE_END,
        };
        self.send(msg.into());
        if !throws || self.session.map.as_ref().is_some_and(|m| m.disable_abilities) {
            return;
        }
        let last_move = self.session.last_move_time().unwrap_or(now);
        let Some(world) = &mut self.world else { return };
        for p in world.shoot_ability(now, item, angle, last_move, data) {
            let msg = PlayerShoot {
                time: now,
                bullet_id: p.bullet_id,
                container_type: p.container_type as i32,
                starting_pos: WorldPos { x: p.start_x as f32, y: p.start_y as f32 },
                angle: p.angle as f32,
            };
            self.send(msg.into());
        }
    }

    /// F and V: a health or magic potion from the stacks (`UseBuyPotionCommand`).
    pub fn use_potion(&mut self, now: i32, which: usize) {
        let Some(me) = self.world.as_ref().and_then(|w| w.player()) else { return };
        let stack = if which == 0 { crate::hud::stat::HEALTH_POTION_STACK } else { crate::hud::stat::MAGIC_POTION_STACK };
        if me.stat_int(stack).unwrap_or(0) <= 0 {
            return;
        }
        let (item, slot) = crate::hud::POTIONS[which];
        self.sounds.push(("use_potion".into(), 1.0, false));
        let msg = UseItem {
            time: now,
            slot_object: SlotObject { object_id: me.id, slot_id: slot, object_type: item },
            item_use_pos: WorldPos { x: me.x as f32, y: me.y as f32 },
            use_type: 1,
        };
        self.send(msg.into());
    }

    /// A player's account id (`ACCOUNT_ID_STAT`), as the account lists hold it.
    pub fn account_id(&self, object_id: i32) -> Option<String> {
        let o = self.world.as_ref()?.objects.get(&object_id)?;
        o.stat_str(38).map(str::to_owned).or_else(|| o.stat_int(38).map(|v| v.to_string()))
    }

    fn is_ignored(&self, object_id: i32) -> bool {
        self.account_id(object_id).is_some_and(|a| self.ignored.contains(&a))
    }

    pub fn request_trade(&mut self, name: &str) {
        self.send(RequestTrade { name: name.to_owned() }.into());
    }

    /// Include or leave out one of our items (`TradeInventory.onSlotClick`).
    pub fn trade_toggle(&mut self, slot: usize, now: i32) {
        let Some(trade) = &mut self.trade else { return };
        let Some(item) = trade.mine.get(slot) else { return };
        if !item.tradeable || item.item <= 0 {
            return;
        }
        trade.my_offer[slot] = !trade.my_offer[slot];
        (trade.they_accepted, trade.i_accepted, trade.changed_at) = (false, false, now);
        let offer = List16(trade.my_offer.clone());
        self.send(ChangeTrade { offer }.into());
    }

    pub fn trade_accept(&mut self, now: i32) {
        let Some(trade) = &mut self.trade else { return };
        let (a, b) = trade.space_ok();
        if !(a && b) || now - trade.changed_at < TRADE_ACCEPT_DELAY {
            return;
        }
        trade.i_accepted = true;
        let msg = AcceptTrade { my_offer: List16(trade.my_offer.clone()), your_offer: List16(trade.their_offer.clone()) };
        self.send(msg.into());
    }

    pub fn trade_cancel(&mut self) {
        if self.trade.take().is_some() {
            self.send(CancelTrade {}.into());
        }
    }

    /// `Player.teleportTo`: once every 10 s.
    pub fn teleport(&mut self, object_id: i32, now: i32) {
        if now < self.next_teleport {
            let secs = (self.next_teleport - now + 999) / 1000;
            self.add_chat("*Error*", "", &tn!(secs, "You can't teleport for {n} more second.|You can't teleport for {n} more seconds."), 0, 0, -1, now);
            return;
        }
        self.teleport_asked = Some((now, self.next_teleport));
        self.next_teleport = now + TELEPORT_COOLDOWN;
        self.send(Teleport { object_id }.into());
    }

    /// The server's error `text`: if it refuses the teleport just asked for (an invisible or
    /// paused player, a world without teleports), the cooldown it started is put back. "Too soon"
    /// keeps it: the server's own cooldown is running.
    fn teleport_refused(&mut self, text: &str, now: i32) {
        const REFUSALS: [&str; 2] = ["Cannot teleport", "Target does not exist"];
        if let Some((at, before)) = self.teleport_asked {
            if now - at < TELEPORT_REFUSAL_MS && REFUSALS.iter().any(|r| text.starts_with(r)) {
                self.next_teleport = before;
                self.teleport_asked = None;
            }
        }
    }

    /// A vote in the Arena (`ArenaVote`): 0 for the next wave, 1 to 5 to combine that many waves;
    /// only once the server has sent its votes.
    pub fn arena_vote(&mut self, action: u8) {
        if self.arena_votes.as_ref().is_some_and(|v| v.open) {
            self.send(bsk_proto::ArenaVote { action }.into());
        }
    }

    /// Answer the visit request in the corner (`VisitAnswer`).
    pub fn answer_visit(&mut self, yes: bool) {
        if let Some(v) = self.visit.take() {
            self.send(bsk_proto::VisitAnswer { id: v.id, answer: yes as u8 }.into());
        }
    }

    /// Hide the visit request once it has run out (the server tells the visitor then).
    pub fn expire_visit(&mut self, now: i32) {
        if self.visit.as_ref().is_some_and(|v| now >= v.until) {
            self.visit = None;
        }
    }

    /// The event our quest arrow follows (`QuestPick`): that one, or back to the nearest (0).
    pub fn pick_quest(&mut self, object_id: i32) {
        if self.realm_events.is_some() {
            self.send(bsk_proto::QuestPick { object_id }.into());
        }
    }

    pub fn party_invite(&mut self, name: &str) {
        self.send(PartyInvite { name: name.to_owned() }.into());
    }

    pub fn guild_invite(&mut self, name: &str) {
        self.send(GuildInvite { name: name.to_owned() }.into());
    }

    /// Answer the invitation: yes or no.
    pub fn answer_invite(&mut self, yes: bool) {
        let Some((invite, _)) = self.invite.take() else { return };
        if !yes {
            return;
        }
        let msg: ClientMessage = match invite {
            Invite::Trade { name } => RequestTrade { name }.into(),
            Invite::Party { name, party_id } => JoinParty { leader: name, party_id }.into(),
            Invite::Guild { guild, .. } => JoinGuild { guild_name: guild }.into(),
        };
        self.send(msg);
    }

    /// Lock (list 0) or ignore (list 1) a player, or undo it (`EditAccountList`).
    pub fn edit_list(&mut self, list: i32, add: bool, object_id: i32) {
        self.send(EditAccountList { account_list_id: list, add, object_id }.into());
        if let Some(id) = self.account_id(object_id) {
            let set = if list == 0 { &mut self.locked } else { &mut self.ignored };
            if add {
                set.insert(id);
            } else {
                set.remove(&id);
            }
        }
    }

    /// The map's name (`Map.name_`): "Nexus", "Vault"...
    pub fn map_name(&self) -> &str {
        self.session.map.as_ref().map_or("", |m| m.name.as_str())
    }

    /// Ask the market (`marketSearch`, `marketBuy`, `marketAdd`, `marketRemove`,
    /// `marketMyOffers`).
    pub fn market(&mut self, act: crate::market::Act) {
        use crate::market::Act;
        let msg: ClientMessage = match act {
            Act::Browse(version) => bsk_proto::MarketBrowse { version }.into(),
            Act::Sell { from_vault, chest, slot, price, hours } => bsk_proto::MarketSell { from_vault, chest, slot, price, hours }.into(),
            // MarketActHandler: 1 buy, 2 take back, 3 collect from the box, 4 buy a listing slot
            Act::Buy(id, price) => bsk_proto::MarketAct { action: 1, id, price }.into(),
            Act::Cancel(id) => bsk_proto::MarketAct { action: 2, id, price: 0 }.into(),
            Act::Collect(id) => bsk_proto::MarketAct { action: 3, id, price: 0 }.into(),
            Act::Vault => bsk_proto::VaultContentsRequest {}.into(),
            Act::BuySlot(price) => bsk_proto::MarketAct { action: 4, id: 0, price }.into(),
        };
        self.send(msg);
    }

    /// Ask the Divine Forge (`ForgeHandlers.cs`).
    pub fn forge(&mut self, act: crate::forge::Act) {
        use crate::forge::Act;
        let msg: ClientMessage = match act {
            Act::Data => bsk_proto::ForgeDataRequest {}.into(),
            Act::Forge(result, slots) => bsk_proto::Forge { result, slots: bsk_proto::List8(slots) }.into(),
            Act::Sell(slots) => bsk_proto::ForgeSell { slots: bsk_proto::List8(slots) }.into(),
            Act::Withdraw(item) => bsk_proto::ForgeWithdraw { item }.into(),
            Act::ImprintShards => bsk_proto::ImprintRequest {}.into(),
            Act::Imprint(slot, imprint, choice) => {
                let [raised, lowered, lowered2] = choice.unwrap_or([255; 3]);
                bsk_proto::Imprint { slot, imprint, raised, lowered, lowered2 }.into()
            }
            Act::ImprintVault(chest, slot, imprint) => bsk_proto::ImprintVaultApply { chest, slot, imprint }.into(),
            Act::Corrupt(slot) => bsk_proto::Corrupt { slot }.into(),
        };
        self.send(msg);
    }

    /// Ask for the Collection (`CollectionHandler.cs`).
    pub fn collection_request(&mut self) {
        self.send(bsk_proto::CollectionRequest {}.into());
    }

    /// Ask the Pen Keeper, or have him act (`PetPenActHandler.cs`).
    pub fn pet_pen(&mut self, action: u8, slot: u8) {
        self.send(bsk_proto::PetPenAct { action, slot }.into());
    }

    /// Ask for the vault window's contents (`VaultContentsHandler.cs`), or move an item for it.
    pub fn vault(&mut self, act: crate::vault::Act, now: i32) {
        match act {
            crate::vault::Act::Request => self.send(bsk_proto::VaultContentsRequest {}.into()),
            crate::vault::Act::Swap(a, b) => self.inv_swap(now, a, b),
        }
    }

    /// Ask the quest log's server side (`QuestTracker.cs`).
    pub fn quest(&mut self, act: crate::quests::Act) {
        use crate::quests::Act;
        let msg: ClientMessage = match act {
            Act::Request => bsk_proto::QuestsRequest {}.into(),
            Act::Claim(kind, id) => bsk_proto::QuestClaim { kind, id }.into(),
        };
        self.send(msg);
    }

    /// Ask the community goals' server side (`CommunityGoals.cs`).
    pub fn community(&mut self, act: crate::community::Act) {
        use crate::community::Act;
        let msg: ClientMessage = match act {
            Act::Request => bsk_proto::CommunityRequest {}.into(),
            Act::Claim(key, tier) => {
                // its chat lines (what was paid, a lucky roll) wait for the reel
                if self.claim_hold.is_none() {
                    self.claim_hold = Some(ClaimHold { lines: Vec::new(), since: None, answered: false });
                }
                bsk_proto::CommunityClaim { key, tier }.into()
            }
        };
        self.send(msg);
    }

    /// The chat lines held for a claim: let them go once it's answered and its reel has played
    /// (`rolling` false: every roll landed, or the window shut), or if no answer comes in
    /// `CLAIM_HOLD_MS`.
    pub fn claim_chat(&mut self, rolling: bool, now: i32) {
        let Some(h) = &mut self.claim_hold else { return };
        let since = *h.since.get_or_insert(now);
        let go = if h.answered { !rolling } else { now - since >= CLAIM_HOLD_MS };
        if !go {
            return;
        }
        for t in self.claim_hold.take().map(|h| h.lines).unwrap_or_default() {
            self.add_chat(&t.name, &t.recipient, &t.text, t.name_color, t.text_color, t.num_stars, now);
        }
    }

    /// The Armoire: wear this skin (0: the class's own look).
    pub fn reskin(&mut self, skin_id: i32) {
        self.send(Reskin { skin_id }.into());
    }

    pub fn choose_name(&mut self, name: &str) {
        self.send(ChooseName { name: name.to_owned() }.into());
    }

    pub fn create_guild(&mut self, name: &str) {
        self.send(CreateGuild { name: name.to_owned() }.into());
    }

    /// Remove someone from the guild (ourselves: leave it).
    pub fn guild_remove(&mut self, name: &str) {
        self.send(GuildRemove { name: name.to_owned() }.into());
    }

    pub fn change_guild_rank(&mut self, name: &str, guild_rank: i32) {
        self.send(ChangeGuildRank { name: name.to_owned(), guild_rank }.into());
    }

    /// Potion storage: kind 0-7, action 0 store, 1 take, 2 drink, 3 drink to the maximum, 4
    /// upgrade.
    pub fn use_storage(&mut self, kind: u8, action: u8) {
        self.send(UseStorage { kind, action }.into());
    }

    /// `Buy`: buy what the shop sells.
    pub fn buy(&mut self, object_id: i32) {
        self.send(Buy { object_id }.into());
    }

    /// `Merchant.update`: every 5 s a merchant in view says what's special about it ("New!",
    /// "2 left!", "20% off!").
    pub fn merchant_balloons(&mut self, now: i32, data: &GameData) {
        let Some(world) = &self.world else { return };
        for o in world.objects.values() {
            let Some(d) = data.objects.get(&o.object_type) else { continue };
            if d.class != ObjectClass::Merchant {
                continue;
            }
            let stat = |id: u8| o.stat_int(id);
            let mut lines: Vec<(String, [u32; 3])> = Vec::new();
            match stat(crate::hud::merch::MINS_LEFT) {
                Some(i32::MAX) => lines.push((tr("New!").into(), [0xE6E6E6, 0xFFFFFF, 0x5A8025])),
                Some(m @ 0..=5) => {
                    let t = match m {
                        0 => tr("Going soon!").to_owned(),
                        m => tn!(m, "Going in {n} min!|Going in {n} mins!"),
                    };
                    lines.push((t, [0x5B2626, 0xFC8642, 0xFC8642]));
                }
                _ => {}
            }
            if let Some(c @ 1..=2) = stat(crate::hud::merch::COUNT) {
                lines.push((tn!(c, "{n} left!|{n} left!"), [0x5B2626, 0xFC8642, 0xFC8642]));
            }
            if let Some(dsc) = stat(crate::hud::merch::DISCOUNT).filter(|&d| d > 0) {
                lines.push((tf!("{dsc}% off!", dsc), [0x608033, 0xFFFF8F, 0xFFFF8F]));
            }
            if lines.is_empty() {
                continue;
            }
            let entry = self.merchant_talk.entry(o.id).or_insert((now - 5000, 0));
            if now - entry.0 < 5000 {
                continue;
            }
            entry.1 = (entry.1 + 1) % lines.len();
            entry.0 = now;
            let (text, colors) = lines[entry.1].clone();
            self.bubbles.retain(|b| b.object_id != o.id);
            self.bubbles.push(crate::view::Bubble { object_id: o.id, text, colors, kind: crate::options::bubble::NPCS, at: now, life: 3000 });
        }
    }

    pub fn use_portal(&mut self, object_id: i32) {
        self.socket.send(&UsePortal { object_id }.into());
    }

    /// Back to the nexus (the R key). Not from the nexus itself: the server disconnects for that.
    pub fn escape(&mut self) {
        if self.session.map.as_ref().is_some_and(|m| m.name != "Nexus") {
            self.socket.send(&Escape {}.into());
        }
    }

    pub fn close(&mut self) {
        if !self.counts.is_empty() {
            log::info!("sent: {:?}", self.counts);
        }
        self.socket.close();
    }
}

fn clone_hello(h: &HelloParams) -> HelloParams {
    HelloParams {
        build_version: h.build_version.clone(),
        game_id: h.game_id,
        guid: h.guid.clone(),
        password: h.password.clone(),
        key_time: h.key_time,
        key: h.key.clone(),
    }
}
