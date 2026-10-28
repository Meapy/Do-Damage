//! Every message of the game protocol. Ids and layouts match `GameServerConnection.as` and the
//! world server's `MessageIds.cs`, `networking/packets/outgoing` and `core/net/handlers`.

use crate::data::*;
use crate::wire::{List16, List8, LongString, Reader, Result, Wire, Writer};
use crate::wire_struct;

/// Declares the message structs, plus an enum of them with id-based decoding and encoding.
macro_rules! messages {
    ($(#[$em:meta])* pub enum $enum:ident {
        $($id:literal => $name:ident { $($field:ident : $ty:ty),* $(,)? }),* $(,)?
    }) => {
        $(
            wire_struct! { pub struct $name { $(pub $field: $ty),* } }
            impl From<$name> for $enum {
                fn from(m: $name) -> Self { $enum::$name(m) }
            }
        )*

        $(#[$em])*
        #[derive(Debug, Clone, PartialEq)]
        pub enum $enum {
            $($name($name),)*
            /// A message id this client doesn't know, kept as raw bytes.
            Unknown { id: u8, body: Vec<u8> },
        }

        impl $enum {
            pub fn id(&self) -> u8 {
                match self {
                    $($enum::$name(_) => $id,)*
                    $enum::Unknown { id, .. } => *id,
                }
            }

            pub fn name(&self) -> &'static str {
                match self {
                    $($enum::$name(_) => stringify!($name),)*
                    $enum::Unknown { .. } => "Unknown",
                }
            }

            /// Decode a message body. Bytes left over after the known fields are ignored, as
            /// the AIR client does, so a server can append fields.
            pub fn decode(id: u8, body: &[u8]) -> Result<Self> {
                let mut r = Reader::new(body);
                Ok(match id {
                    $($id => $enum::$name(<$name as Wire>::read(&mut r)?),)*
                    _ => $enum::Unknown { id, body: body.to_vec() },
                })
            }

            /// The message body (without the frame header).
            pub fn encode_body(&self, w: &mut Writer) {
                match self {
                    $($enum::$name(m) => m.write(w),)*
                    $enum::Unknown { body, .. } => w.bytes(body),
                }
            }
        }
    };
}

wire_struct! {
    /// EnemyShoot's tail: how many bullets and the angle between them.
    #[derive(Copy)]
    pub struct ShotSpread {
        pub num_shots: u8,
        pub angle_inc: f32,
    }
}

/// Pic: a raw ARGB image.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct PicData {
    pub width: i32,
    pub height: i32,
    pub argb: Vec<u8>,
}

impl Wire for PicData {
    fn read(r: &mut Reader) -> Result<Self> {
        let width = i32::read(r)?;
        let height = i32::read(r)?;
        let len = (width.max(0) as usize).saturating_mul(height.max(0) as usize).saturating_mul(4);
        Ok(Self { width, height, argb: r.bytes(len)?.to_vec() })
    }
    fn write(&self, w: &mut Writer) {
        self.width.write(w);
        self.height.write(w);
        w.bytes(&self.argb);
    }
}

messages! {
    /// Messages from the world server to the client.
    pub enum ServerMessage {
        0 => Failure { error_id: i32, description: String },
        1 => CreateSuccess { object_id: i32, char_id: i32 },
        6 => Text {
            name: String, object_id: i32, num_stars: i32, bubble_time: u8, recipient: String,
            text: String, clean_text: String, name_color: i32, text_color: i32,
        },
        7 => ServerPlayerShoot {
            bullet_id: i32, owner_id: i32, container_type: i32, starting_pos: WorldPos, angle: f32, damage: i32,
            // Which of the container's projectiles; older servers leave it out (projectile 0).
            bullet_type: Option<u8>,
        },
        8 => Damage {
            target_id: i32, effects: List8<u8>, damage_amount: i32, kill: bool, bullet_id: i32, object_id: i32,
            pierce: bool,
        },
        9 => Update { tiles: List16<GroundTile>, new_objs: List16<ObjectData>, drops: List16<i32> },
        11 => Notification { object_id: i32, text: String, color: u32, player_id: i32 },
        12 => NewTick { tick_id: i32, tick_time: i32, statuses: List16<ObjectStatus> },
        15 => ShowEffect {
            effect_type: u8, target_object_id: i32, pos1: WorldPos, pos2: WorldPos, color: u32, duration: i32,
            object_type: u16,
        },
        17 => Goto { object_id: i32, pos: WorldPos },
        19 => InvResult { result: i32 },
        20 => Reconnect { name: String, host: String, port: i32, game_id: i32, key_time: i32, key: List16<u8> },
        21 => Ping { serial: i32, round_trip_time: i32 },
        23 => MapInfo {
            width: i16, height: i16, name: String, display_name: String, seed: u32, background: u8,
            difficulty: u8, allow_player_teleport: bool, show_displays: bool, music: String,
            disable_shooting: bool, disable_abilities: bool,
        },
        25 => Pic { pic: PicData },
        29 => Death { account_id: i32, char_id: i32, killed_by: String },
        31 => BuyResult { result: i32, result_string: String },
        32 => Aoe {
            pos: WorldPos, radius: f32, damage: u16, effect: u8, duration: f32, orig_type: u16, color: u32,
        },
        42 => AccountList { account_list_id: i32, account_ids: List16<String> },
        43 => QuestObjId { object_id: i32 },
        45 => NameResult { success: bool, error_text: String },
        47 => GuildResult { success: bool, error_text: String },
        50 => AllyShoot { bullet_id: i32, owner_id: i32, container_type: i32, angle: f32 },
        51 => EnemyShoot {
            bullet_id: i32, owner_id: i32, bullet_type: u8, starting_pos: WorldPos, angle: f32, damage: i32,
            spread: Option<ShotSpread>,
        },
        53 => TradeRequested { name: String },
        54 => TradeStart { my_items: List16<TradeItem>, your_name: String, your_items: List16<TradeItem> },
        56 => TradeChanged { offer: List16<bool> },
        59 => TradeDone { code: i32, description: String },
        60 => TradeAccepted { my_offer: List16<bool>, your_offer: List16<bool> },
        61 => ClientStat { name: String, value: i32 },
        64 => File { filename: String, contents: LongString },
        65 => InvitedToGuild { name: String, guild_name: String },
        68 => PlaySound { owner_id: i32, sound_id: u8 },
        69 => GlobalNotification { kind: i32, text: String },
        72 => InvitedToParty { name: String, party_id: i32 },
        75 => SwitchMusic { music: String },
        77 => MarketSearchResult { results: List16<MarketData>, description: String },
        79 => MarketBuyResult { code: i32, description: String, offer_id: i32 },
        81 => MarketAddResult { code: i32, description: String },
        83 => MarketRemoveResult { code: i32, description: String },
        85 => MarketMyOffersResult { results: List16<MarketData> },
        87 => ImminentArenaWave { current_runtime: i32, wave: i32, current_state: i32 },
        // Sent only to clients that asked (QuestDamageSubscribe): the quest's top damage
        // dealers, most first, and our own damage.
        90 => QuestDamage { object_id: i32, top: List8<DamageShare>, mine: i32 },
        // For clients that asked (QuestDamageFullSubscribe), instead: everyone who hit the
        // quest, most first, its type and maximum health, whether it has just died, and our
        // damage.
        // For clients that asked (FameStatsSubscribe): the character's fame records, every few
        // seconds while they change.
        94 => FameStats { stats: List8<FameStat> },
        92 => QuestDamageFull { object_id: i32, object_type: u16, max_hp: i32, dead: bool, hitters: List8<DamageHit>, mine: i32 },
        // The Divine Forge's recipes and what it pays for items (ForgeDataRequest).
        96 => ForgeData { recipes: List16<ForgeRecipe>, prices: List16<ForgePrice> },
        // The answer to Forge or ForgeSell: the item forged (0 for a sale), the fame paid or
        // gained, and what went wrong.
        99 => ForgeResult { sold: bool, success: bool, item: u16, fame: i32, message: String },
        // The forge storage's shards (after ForgeDataRequest, and whenever they change).
        100 => ForgeShards { shards: List16<ForgeShard> },
        // What the account's vault holds that the forge can take, by item type (after
        // ForgeDataRequest and each forging).
        102 => ForgeVault { items: List16<ForgeShard> },
        // Quests, daily login rewards and achievements, after QuestsRequest, a claim or a reset:
        // the time (Unix seconds), the rank, points and chosen name colour (255: none), the
        // ranks, each period's reset and bonus, the quests, the daily reward track, the
        // achievements and the gifts open now (which older servers leave out).
        104 => QuestsData {
            now: i32, rank: u8, points: i32, color_rank: u8, ranks: List8<QuestRankInfo>,
            periods: List8<QuestPeriodInfo>, quests: List8<QuestInfo>, login_day: u8, login_ready: bool,
            login_streak: i32, login_total: i32, login_next_at: i32, login_days: List8<LoginDayInfo>,
            achievements: List8<AchievementInfo>, gifts: Option<List8<QuestGiftInfo>>,
        },
        // The answer to QuestClaim.
        106 => QuestResult { success: bool, message: String },
        // A popup: a quest done (0), an achievement earned (1), a new rank (3), a bonus (4).
        107 => QuestNotice { kind: u8, title: String, text: String },
        // Progress that changed since QuestsData, by place in its lists.
        108 => QuestProgress { changes: List16<QuestChange> },
        // The answer to Imprint: whether it worked, the inventory slot, what went wrong.
        111 => ImprintResult { success: bool, slot: u8, message: String },
        // The Enthroned gear in the account's vault (after ImprintRequest and each vault imprint;
        // empty while we're in the vault).
        112 => ImprintVault { items: List16<VaultGear> },
        // A realm's events, for clients that asked (RealmEventsSubscribe), whenever they change:
        // how many are defeated and how many close it, the one our quest arrow follows (0: the
        // nearest), and the running events, side quests and waystones.
        115 => RealmEvents { defeated: i32, needed: i32, picked: i32, entries: List8<RealmEventEntry> },
    }
}

messages! {
    /// Messages from the client to the world server.
    pub enum ClientMessage {
        2 => Create { class_type: u16, skin_type: u16 },
        3 => PlayerShoot { time: i32, bullet_id: i32, container_type: i32, starting_pos: WorldPos, angle: f32 },
        4 => Move { tick_id: i32, time: i32, new_position: WorldPos, records: List16<MoveRecord> },
        5 => PlayerText { text: String },
        10 => UpdateAck {},
        13 => InvSwap { time: i32, position: WorldPos, slot_object1: SlotObject, slot_object2: SlotObject },
        14 => UseItem { time: i32, slot_object: SlotObject, item_use_pos: WorldPos, use_type: u8 },
        16 => Hello {
            build_version: String, game_id: i32, guid: String, password: String, key_time: i32,
            key: List16<u8>, map_json: LongString,
        },
        18 => InvDrop { slot_object: SlotObject },
        22 => Pong { serial: i32, time: i32 },
        24 => Load { char_id: i32 },
        27 => Teleport { object_id: i32 },
        28 => UsePortal { object_id: i32 },
        30 => Buy { object_id: i32 },
        33 => GroundDamage { time: i32, position: WorldPos },
        34 => PlayerHit { bullet_id: i32, object_id: i32 },
        35 => EnemyHit { time: i32, bullet_id: i32, target_id: i32, kill: bool },
        36 => AoeAck { time: i32, position: WorldPos },
        37 => ShootAck { time: i32 },
        38 => OtherHit { time: i32, bullet_id: i32, object_id: i32, target_id: i32 },
        39 => SquareHit { time: i32, bullet_id: i32, object_id: i32 },
        40 => GotoAck { time: i32 },
        41 => EditAccountList { account_list_id: i32, add: bool, object_id: i32 },
        44 => ChooseName { name: String },
        46 => CreateGuild { name: String },
        48 => GuildRemove { name: String },
        49 => GuildInvite { name: String },
        52 => RequestTrade { name: String },
        55 => ChangeTrade { offer: List16<bool> },
        57 => AcceptTrade { my_offer: List16<bool>, your_offer: List16<bool> },
        58 => CancelTrade {},
        63 => Escape {},
        66 => JoinGuild { guild_name: String },
        67 => ChangeGuildRank { name: String, guild_rank: i32 },
        70 => Reskin { skin_id: i32 },
        71 => PartyInvite { name: String },
        73 => JoinParty { leader: String, party_id: i32 },
        74 => UseStorage { kind: u8, action: u8 },
        76 => MarketSearch { item_type: i32 },
        78 => MarketBuy { id: i32 },
        80 => MarketAdd { slots: List8<u8>, price: i32, currency: i32, hours: i32 },
        82 => MarketRemove { id: i32 },
        84 => MarketMyOffers {},
        86 => BreakdownSlot { time: i32, slot: u8, object_id: i32 },
        88 => ViewRadius { radius: i32 },
        // Ask for QuestDamage (once a connection; a server without it ignores it).
        89 => QuestDamageSubscribe {},
        // Ask for QuestDamageFull instead (a server without it ignores it, and still sends
        // QuestDamage for the first).
        91 => QuestDamageFullSubscribe {},
        // Ask for FameStats (a server without it ignores it).
        93 => FameStatsSubscribe {},
        // The Divine Forge's window: ask for its recipes and prices, forge the recipe making
        // `result` from these inventory slots, or sell these slots for fame (an older server
        // ignores all three).
        95 => ForgeDataRequest {},
        97 => Forge { result: u16, slots: List8<u8> },
        98 => ForgeSell { slots: List8<u8> },
        // Take a shard out of the forge storage into the inventory (an older server doesn't
        // know it: send it only once ForgeShards came).
        101 => ForgeWithdraw { item: u16 },
        // Ask for QuestsData, and from then on QuestProgress and QuestNotice (an older server
        // ignores it, and the window stays empty).
        103 => QuestsRequest {},
        // Claim a quest (kind 0, its id), a period's bonus (1, "daily"...), the daily reward (2),
        // an achievement's tiers (3, its id), swap a quest (4, its id) or pick a name colour (5,
        // the rank, -1 for none).
        105 => QuestClaim { kind: u8, id: String },
        // The Enchanter's window: ask for the forge storage's shards (answered with ForgeShards),
        // and imprint the item in an inventory slot (an older server doesn't know either).
        109 => ImprintRequest {},
        110 => Imprint { slot: u8, imprint: u8 },
        // Imprint an Enthroned item in the vault, by chest and slot.
        113 => ImprintVaultApply { chest: u16, slot: u8, imprint: u8 },
        // Ask for RealmEvents in this world (an older server ignores it, and none come).
        114 => RealmEventsSubscribe {},
        // The event our quest arrow should follow (an object id from RealmEvents), 0 for the
        // nearest. Only sent once RealmEvents came, so never to a server without it.
        116 => QuestPick { object_id: i32 },
    }
}
