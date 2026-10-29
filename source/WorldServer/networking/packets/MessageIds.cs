using System;

[Flags]
public enum MessageId : byte
{
    FAILURE = 0,
    CREATE_SUCCESS = 1,
    CREATE = 2,
    PLAYERSHOOT = 3,
    MOVE = 4,
    PLAYERTEXT = 5,
    TEXT = 6,
    SERVERPLAYERSHOOT = 7,
    DAMAGE = 8,
    UPDATE = 9,
    UPDATEACK = 10,
    NOTIFICATION = 11,
    NEWTICK = 12,
    INVSWAP = 13,
    USEITEM = 14,
    SHOWEFFECT = 15,
    HELLO = 16,
    GOTO = 17,
    INVDROP = 18,
    INVRESULT = 19,
    RECONNECT = 20,
    PING = 21,
    PONG = 22,
    MAPINFO = 23,
    LOAD = 24,
    PIC = 25,
    SETCONDITION = 26,
    TELEPORT = 27,
    USEPORTAL = 28,
    DEATH = 29,
    BUY = 30,
    BUYRESULT = 31,
    AOE = 32,
    GROUNDDAMAGE = 33,
    PLAYERHIT = 34,
    ENEMYHIT = 35,
    AOEACK = 36,
    SHOOTACK = 37,
    OTHERHIT = 38,
    SQUAREHIT = 39,
    GOTOACK = 40,
    EDITACCOUNTLIST = 41,
    ACCOUNTLIST = 42,
    QUESTOBJID = 43,
    CHOOSENAME = 44,
    NAMERESULT = 45,
    CREATEGUILD = 46,
    GUILDRESULT = 47,
    GUILDREMOVE = 48,
    GUILDINVITE = 49,
    ALLYSHOOT = 50,
    ENEMYSHOOT = 51,
    REQUESTTRADE = 52,
    TRADEREQUESTED = 53,
    TRADESTART = 54,
    CHANGETRADE = 55,
    TRADECHANGED = 56,
    ACCEPTTRADE = 57,
    CANCELTRADE = 58,
    TRADEDONE = 59,
    TRADEACCEPTED = 60,
    CLIENTSTAT = 61,
    CHECKCREDITS = 62,
    ESCAPE = 63,
    FILE = 64,
    INVITEDTOGUILD = 65,
    JOINGUILD = 66,
    CHANGEGUILDRANK = 67,
    PLAYSOUND = 68,
    GLOBAL_NOTIFICATION = 69,
    RESKIN = 70,
    PARTY_INVITE = 71,
    INVITED_TO_PARTY = 72,
    JOIN_PARTY = 73,
    USE_STORAGE = 74,
    SWITCH_MUSIC = 75,
    MARKET_SEARCH = 76,
    MARKET_SEARCH_RESULT = 77,
    MARKET_BUY = 78,
    MARKET_BUY_RESULT = 79,
    MARKET_ADD = 80,
    MARKET_ADD_RESULT = 81,
    MARKET_REMOVE = 82,
    MARKET_REMOVE_RESULT = 83,
    MARKET_MY_OFFERS = 84,
    MARKET_MY_OFFERS_RESULT = 85,
    BREAKDOWN_SLOT = 86,
    IMMINENT_ARENA_WAVE = 87,
    VIEW_RADIUS = 88,
    // The client asks for its quest's top damage dealers (once a connection); the AIR client
    // never asks, so it never gets QUEST_DAMAGE.
    QUEST_DAMAGE_SUBSCRIBE = 89,
    QUEST_DAMAGE = 90,
    // The same for everyone who hit the quest, with their classes, its maximum health and
    // whether it has died (the client keeps the bosses it fought).
    QUEST_DAMAGE_FULL_SUBSCRIBE = 91,
    QUEST_DAMAGE_FULL = 92,
    // The client asks for its character's fame records (kills, dungeons, accuracy...), and gets
    // them every few seconds while they change.
    FAME_STATS_SUBSCRIBE = 93,
    FAME_STATS = 94,
    // The Divine Forge's window (Rust client): the client asks for the recipes and sell prices
    // (FORGE_DATA), forges a recipe from inventory slots or sells slots for fame, and gets
    // FORGE_RESULT for each. The AIR client never sends them.
    FORGE_DATA_REQUEST = 95,
    FORGE_DATA = 96,
    FORGE = 97,
    FORGE_SELL = 98,
    FORGE_RESULT = 99,
    // The forge storage's shards (after FORGE_DATA_REQUEST and when they change), and the
    // client taking one out into the inventory. An older client ignores FORGE_SHARDS.
    FORGE_SHARDS = 100,
    FORGE_WITHDRAW = 101,
    // What the account's vault holds that the forge can take (after FORGE_DATA_REQUEST and each
    // forging). An older client ignores it.
    FORGE_VAULT = 102,
    // Quests, daily login rewards and achievements (Rust client): the client asks for them
    // (QUESTS_REQUEST), gets them all (QUESTS_DATA) and from then on what changes
    // (QUEST_PROGRESS) and popups (QUEST_NOTICE); it claims rewards, swaps a quest or picks a
    // name colour (QUEST_CLAIM) and gets QUEST_RESULT. The server sends none of these to a client
    // that never asked, so the AIR client and older Rust clients never get them.
    QUESTS_REQUEST = 103,
    QUESTS_DATA = 104,
    QUEST_CLAIM = 105,
    QUEST_RESULT = 106,
    QUEST_NOTICE = 107,
    QUEST_PROGRESS = 108,
    // The Enchanter's window (Rust client, ImprintHandlers.cs): the client asks for the forge
    // storage's shards (answered with FORGE_SHARDS), imprints an inventory slot's item and gets
    // IMPRINT_RESULT. The AIR client and older Rust clients never send them.
    IMPRINT_REQUEST = 109,
    IMPRINT = 110,
    IMPRINT_RESULT = 111,
    // The Enthroned gear in the account's vault (after IMPRINT_REQUEST and each vault imprint), and
    // the client imprinting one of them by chest and slot.
    IMPRINT_VAULT = 112,
    IMPRINT_VAULT_APPLY = 113,
    // The vault window (Rust client, VaultContentsHandler.cs): every item in the account's vault
    // chests, by chest and slot, and where the chests stand when the player is in their vault.
    // Read-only. The AIR client and older Rust clients never ask.
    VAULT_CONTENTS_REQUEST = 114,
    VAULT_CONTENTS = 115,
    // A realm's events (Rust client, RealmManager.Events.cs): the client asks for them once in each
    // world (REALM_EVENTS_SUBSCRIBE), and in a realm gets the running events, its side quests and
    // Realm 2's beacons with the count to close it (REALM_EVENTS) whenever they change; it picks
    // the one its quest arrow follows (QUEST_PICK, 0 for the nearest as before). The AIR client
    // and older Rust clients never ask, so they're never sent REALM_EVENTS.
    REALM_EVENTS_SUBSCRIBE = 116,
    REALM_EVENTS = 117,
    QUEST_PICK = 118,
    // The Arena's votes between waves (Rust client, DoomedBattleArena): the client votes to start
    // the next wave or to combine waves (ARENA_VOTE, 255 to subscribe), and gets everyone's votes
    // (ARENA_VOTES) whenever they change. The AIR client and older Rust clients never subscribe;
    // typing GO in chat still counts as a vote to start.
    ARENA_VOTE = 119,
    ARENA_VOTES = 120,
    // The Marketplace window (Rust client 2.3.3, MarketHandlers.cs): the client asks for the
    // listings with the version it has (MARKET_BROWSE), and gets them when they changed, with its
    // Marketplace box and history every time (MARKET_LISTINGS); it lists an item from the
    // inventory or the vault (MARKET_SELL), buys, takes back or collects (MARKET_ACT), and each
    // of those is answered by MARKET_RESULT. Older clients use MARKET_SEARCH to MARKET_MY_OFFERS.
    MARKET_BROWSE = 121,
    MARKET_LISTINGS = 122,
    MARKET_SELL = 123,
    MARKET_ACT = 124,
    MARKET_RESULT = 125,
    // From the world proxy (tools/world_proxy.py), before the client's first message: the
    // client's address. Only taken on a connection from this PC; clients never send it.
    PROXY_INFO = 250
}