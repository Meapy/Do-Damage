//! Structures shared by several messages (`kabam/rotmg/messaging/impl/data` in the AIR client).

use crate::wire::{List16, List8, Reader, Result, Wire, Writer};
use crate::wire_struct;

wire_struct! {
    /// A position in tiles.
    #[derive(Copy)]
    pub struct WorldPos {
        pub x: f32,
        pub y: f32,
    }
}

wire_struct! {
    pub struct GroundTile {
        pub x: i16,
        pub y: i16,
        pub tile_type: i32,
    }
}

wire_struct! {
    /// An item slot: the container object, the slot index and the item type in it.
    #[derive(Copy)]
    pub struct SlotObject {
        pub object_id: i32,
        pub slot_id: u8,
        pub object_type: i32,
    }
}

wire_struct! {
    #[derive(Copy)]
    pub struct MoveRecord {
        pub time: i32,
        pub x: f32,
        pub y: f32,
    }
}

wire_struct! {
    /// One of a quest's top damage dealers (`QuestDamage`).
    pub struct DamageShare {
        pub name: String,
        pub damage: i32,
    }
}

wire_struct! {
    /// Someone who hit the quest, with their class (`QuestDamageFull`).
    pub struct DamageHit {
        pub name: String,
        pub class: u16,
        pub damage: i32,
    }
}

wire_struct! {
    /// One of the character's fame records (`FameStats`): its id and count.
    pub struct FameStat {
        pub id: u8,
        pub value: i32,
    }
}

wire_struct! {
    /// A forge recipe (`ForgeData`): the item it makes, its fame, whether it needs a Forge
    /// Amulet, and the ingredients.
    pub struct ForgeRecipe {
        pub result: u16,
        pub fame: i32,
        pub advanced: bool,
        pub items: List8<u16>,
    }
}

wire_struct! {
    /// What the forge pays for an item (`ForgeData`).
    pub struct ForgePrice {
        pub item: u16,
        pub fame: i32,
    }
}

wire_struct! {
    /// One of a realm's events (`RealmEvents`): an event that counts toward closing it (kind 0), a
    /// side quest (1, the Lost Sentry) or a Realm 2 waystone to teleport to (2), with where it is.
    pub struct RealmEventEntry {
        pub kind: u8,
        pub object_id: i32,
        pub object_type: u16,
        pub name: String,
        pub x: f32,
        pub y: f32,
    }
}

wire_struct! {
    /// A player's votes in the Arena between waves (`ArenaVotes`): whether they want the next
    /// wave now, and how many waves they want the next fight to combine (0 for no pick).
    pub struct ArenaVoter {
        pub object_id: i32,
        pub name: String,
        pub next: bool,
        pub pick: u8,
    }
}

wire_struct! {
    /// An item in the account's vault (`ImprintVault`, the Enthroned ones; `VaultContents`, all):
    /// its chest and slot, its type and its imprints (Offensive, Defensive, Misc; 0 for none).
    pub struct VaultGear {
        pub chest: u16,
        pub slot: u8,
        pub item: u16,
        pub offensive: u8,
        pub defensive: u8,
        pub misc: u8,
    }
}

wire_struct! {
    /// A vault or gift chest (`VaultContents`): its object id and where it stands, in our own
    /// vault (-1 for all three elsewhere), and whether it's a gift chest.
    pub struct VaultChest {
        pub id: i32,
        pub x: f32,
        pub y: f32,
        pub gift: bool,
    }
}

wire_struct! {
    /// A forge shard in the forge storage (`ForgeShards`), or an item in the vault (`ForgeVault`),
    /// and how many are there.
    pub struct ForgeShard {
        pub item: u16,
        pub count: i32,
    }
}

wire_struct! {
    /// A quest's, bonus's, daily reward's or achievement tier's reward (`QuestsData`): fame (kind
    /// 0, `amount`) or an item (kind 1, its type and how many).
    #[derive(Copy)]
    pub struct QuestReward {
        pub kind: u8,
        pub item: u16,
        pub amount: i32,
    }
}

wire_struct! {
    /// An achievement rank (`QuestsData`): its name, the points it needs, and the chat name
    /// colour it unlocks (0: none).
    pub struct QuestRankInfo {
        pub name: String,
        pub points: i32,
        pub color: i32,
    }
}

wire_struct! {
    /// A quest period (0 daily, 1 weekly, 2 monthly): when it resets (Unix seconds), the swaps
    /// left, and its bonus for claiming all its quests.
    pub struct QuestPeriodInfo {
        pub period: u8,
        pub resets_at: i32,
        pub rerolls_left: u8,
        pub bonus_claimed: bool,
        pub bonus: List8<QuestReward>,
    }
}

wire_struct! {
    /// One of the player's quests.
    pub struct QuestInfo {
        pub period: u8,
        pub id: String,
        pub title: String,
        pub text: String,
        pub progress: i32,
        pub goal: i32,
        pub claimed: bool,
        pub rewards: List8<QuestReward>,
    }
}

wire_struct! {
    /// A day of the daily reward track.
    pub struct LoginDayInfo {
        pub milestone: bool,
        pub rewards: List8<QuestReward>,
    }
}

wire_struct! {
    /// An achievement: its tiers' goals, the tiers claimed, the progress, and the next tier's
    /// points and rewards.
    pub struct AchievementInfo {
        pub id: String,
        pub category: String,
        pub title: String,
        pub text: String,
        pub claimed: u8,
        pub progress: i32,
        pub goals: List8<i32>,
        pub next_points: i32,
        pub next_rewards: List8<QuestReward>,
    }
}

wire_struct! {
    /// A gift for a while (`QuestsData`): every account claims it once, until `ends_at` (Unix
    /// seconds).
    pub struct QuestGiftInfo {
        pub id: String,
        pub title: String,
        pub text: String,
        pub ends_at: i32,
        pub claimed: bool,
        pub rewards: List8<QuestReward>,
    }
}

wire_struct! {
    /// Progress that changed (`QuestProgress`): a quest (kind 0) or an achievement (1) by its
    /// place in `QuestsData`'s lists.
    #[derive(Copy)]
    pub struct QuestChange {
        pub kind: u8,
        pub index: u16,
        pub progress: i32,
    }
}

wire_struct! {
    pub struct TradeItem {
        pub item: i32,
        pub slot_type: i32,
        pub tradeable: bool,
        pub included: bool,
        pub item_data: String,
    }
}

wire_struct! {
    pub struct MarketData {
        pub id: i32,
        pub item_type: u16,
        pub seller_name: String,
        pub seller_id: i32,
        pub currency: i32,
        pub price: i32,
        pub start_time: i32,
        pub time_left: i32,
        pub item_data: String,
    }
}

wire_struct! {
    /// A Marketplace listing (`MarketListings`): its id, the item, the price in fame, the
    /// seller, when it started and ends (Unix time), the stack's count (0 for one item) and the
    /// imprints (Offensive, Defensive, Misc).
    pub struct MarketListing {
        pub id: i32,
        pub item_type: u16,
        pub price: i32,
        pub seller: String,
        pub start: i32,
        pub end: i32,
        pub stack: i16,
        pub offensive: u8,
        pub defensive: u8,
        pub misc: u8,
    }
}

wire_struct! {
    /// An item waiting in our Marketplace box: its id there, the item as a listing has it, why
    /// (1 bought, 2 taken back, 3 ended unsold) and when.
    pub struct MarketBoxItem {
        pub box_id: i32,
        pub item_type: u16,
        pub stack: i16,
        pub offensive: u8,
        pub defensive: u8,
        pub misc: u8,
        pub why: u8,
        pub time: i32,
    }
}

wire_struct! {
    /// A line of our Marketplace history: what happened (1 listed, 2 sold, 3 bought, 4 taken
    /// back, 5 ended), the item, the price, what we got for a sale after the tax, the buyer or
    /// seller, and when.
    pub struct MarketHistory {
        pub kind: u8,
        pub item_type: u16,
        pub price: i32,
        pub net: i32,
        pub other: String,
        pub time: i32,
    }
}

/// Stat ids (`StatData.as`, `StatDataType.cs`). Only the ones the protocol itself needs; the
/// game model names the rest.
pub mod stat {
    pub const NAME: u8 = 31;
    pub const GUILD_NAME: u8 = 62;
    /// `INVDATA0`..`INVDATA11` and `BACKPACKDATA0`..`BACKPACKDATA7`: per-slot item data (JSON).
    pub const ITEM_DATA: std::ops::RangeInclusive<u8> = 122..=141;

    /// Whether the value of this stat is a string rather than an int, as the AIR client decides.
    pub fn is_string(id: u8) -> bool {
        id == NAME || id == GUILD_NAME || ITEM_DATA.contains(&id)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum StatValue {
    Int(i32),
    Str(String),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StatData {
    pub id: u8,
    pub value: StatValue,
}

impl Default for StatData {
    fn default() -> Self {
        Self { id: 0, value: StatValue::Int(0) }
    }
}

impl Wire for StatData {
    fn read(r: &mut Reader) -> Result<Self> {
        let id = u8::read(r)?;
        let value = if stat::is_string(id) { StatValue::Str(String::read(r)?) } else { StatValue::Int(i32::read(r)?) };
        Ok(Self { id, value })
    }
    fn write(&self, w: &mut Writer) {
        self.id.write(w);
        match &self.value {
            StatValue::Int(v) => v.write(w),
            StatValue::Str(s) => s.write(w),
        }
    }
}

wire_struct! {
    /// An object's id, position and changed stats.
    pub struct ObjectStatus {
        pub object_id: i32,
        pub pos: WorldPos,
        pub stats: List16<StatData>,
    }
}

wire_struct! {
    pub struct ObjectData {
        pub object_type: i32,
        pub status: ObjectStatus,
    }
}
