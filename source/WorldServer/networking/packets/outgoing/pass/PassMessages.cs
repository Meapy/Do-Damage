using System.Collections.Generic;
using Shared;

namespace WorldServer.networking.packets.outgoing.pass
{
    // A Realm Pass reward as the window shows it: fame (kind 0, amount), an item (1: its type and
    // how many), gold (3), a skin (4: its type, its class's type, its name), an aura (5: its id, colour and name), a name colour (6: the same),
    // character slots (7) or a pet effect (8: its id, colour and name; since 2.6.6).
    public sealed class PassRewardInfo
    {
        public const byte KindFame = 0, KindItem = 1, KindGold = 3, KindSkins = 4, KindAura = 5, KindColor = 6, KindSlot = 7, KindPetFx = 8;

        public byte Kind;
        public ushort Item;
        public int Amount;
        public string Name = "";

        public void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.Write(Item);
            wtr.Write(Amount);
            wtr.WriteUTF16(Name ?? "");
        }

        public static void WriteList(NetworkWriter wtr, List<PassRewardInfo> list)
        {
            wtr.Write((byte)list.Count);
            foreach (var r in list)
                r.Write(wtr);
        }
    }

    public sealed class PassTierInfo
    {
        public List<PassRewardInfo> Free = new List<PassRewardInfo>();
        public List<PassRewardInfo> Gilded = new List<PassRewardInfo>();
        public bool FreeClaimed, GildedClaimed;
    }

    // An aura, name colour or pet effect: its id, name, colour, and whether the account has it.
    public sealed class PassCosmeticInfo
    {
        public byte Id;
        public string Name = "";
        public int Color;
        public bool Owned;
    }

    // The pass window (PassTracker.SendData): the season and the player's place in it, every
    // tier's rewards and claims, the cache's possible rewards, what earns XP, the cosmetics, and
    // the next season. Off-season (Active false) the season's fields are empty.
    public sealed class PassDataMessage : OutgoingMessage
    {
        public int Now;
        public bool Active;
        public string SeasonKey = "", SeasonName = "", Theme = "";
        public int Accent, Dark, Light;
        public int StartsAt, EndsAt;
        public int Xp, XpPerTier, BonusXp, Price, Gold;
        public bool Gilded;
        public byte Momentum, PaceTier;
        public bool FirstDungeonReady;
        public int CachesOpened;
        public List<PassTierInfo> Tiers = new List<PassTierInfo>();
        public List<PassRewardInfo> Cache = new List<PassRewardInfo>();
        public List<(byte Id, int Amount)> Sources = new List<(byte, int)>();
        public int DungeonStep;
        public List<PassCosmeticInfo> Auras = new List<PassCosmeticInfo>();
        public byte Aura;
        public List<PassCosmeticInfo> Colors = new List<PassCosmeticInfo>();
        public byte Color;
        public string NextName = "", NextTheme = "";
        public int NextStartsAt;
        // what a Gilded pass adds to the community goals' loot: Overdrive levels of luck, rolls a stage
        public byte CommunityLuck, CommunityRolls;
        // the [Gilded] chat title shown (PassAct 6 turns it on and off; older clients leave it unread)
        public bool ShowTitle = true;
        // the pet effects and the one worn (since 2.6.6; PassAct 7 puts one on, older clients leave
        // them unread)
        public List<PassCosmeticInfo> PetEffects = new List<PassCosmeticInfo>();
        public byte PetEffect;

        public override MessageId MessageId => MessageId.PASS_DATA;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Now);
            wtr.Write(Active);
            wtr.WriteUTF16(SeasonKey);
            wtr.WriteUTF16(SeasonName);
            wtr.WriteUTF16(Theme);
            wtr.Write(Accent);
            wtr.Write(Dark);
            wtr.Write(Light);
            wtr.Write(StartsAt);
            wtr.Write(EndsAt);
            wtr.Write(Xp);
            wtr.Write(XpPerTier);
            wtr.Write(BonusXp);
            wtr.Write(Price);
            wtr.Write(Gold);
            wtr.Write(Gilded);
            wtr.Write(Momentum);
            wtr.Write(PaceTier);
            wtr.Write(FirstDungeonReady);
            wtr.Write(CachesOpened);
            wtr.Write((byte)Tiers.Count);
            foreach (var t in Tiers)
            {
                PassRewardInfo.WriteList(wtr, t.Free);
                PassRewardInfo.WriteList(wtr, t.Gilded);
                wtr.Write(t.FreeClaimed);
                wtr.Write(t.GildedClaimed);
            }
            PassRewardInfo.WriteList(wtr, Cache);
            wtr.Write((byte)Sources.Count);
            foreach (var (id, amount) in Sources)
            {
                wtr.Write(id);
                wtr.Write(amount);
            }
            wtr.Write(DungeonStep);
            WriteCosmetics(wtr, Auras);
            wtr.Write(Aura);
            WriteCosmetics(wtr, Colors);
            wtr.Write(Color);
            wtr.WriteUTF16(NextName);
            wtr.WriteUTF16(NextTheme);
            wtr.Write(NextStartsAt);
            wtr.Write(CommunityLuck);
            wtr.Write(CommunityRolls);
            wtr.Write(ShowTitle);
            WriteCosmetics(wtr, PetEffects);
            wtr.Write(PetEffect);
        }

        private static void WriteCosmetics(NetworkWriter wtr, List<PassCosmeticInfo> list)
        {
            wtr.Write((byte)list.Count);
            foreach (var c in list)
            {
                wtr.Write(c.Id);
                wtr.WriteUTF16(c.Name ?? "");
                wtr.Write(c.Color);
                wtr.Write(c.Owned);
            }
        }
    }

    // Pass XP gained (Shared.pass.PassSource), the total and tier now, and whether Momentum added to it.
    public sealed class PassXp : OutgoingMessage
    {
        public int Gained;
        public byte Source;
        public int Xp;
        public byte Tier;
        public bool Momentum;

        public override MessageId MessageId => MessageId.PASS_XP;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Gained);
            wtr.Write(Source);
            wtr.Write(Xp);
            wtr.Write(Tier);
            wtr.Write(Momentum);
        }
    }

    // The answer to PASS_ACT: a code the client words (Shared.pass.PassCode) with a tier or count,
    // the English, and the rewards it paid (a claim's, or what caches rolled).
    public sealed class PassResult : OutgoingMessage
    {
        public byte Code;
        public bool Success;
        public int Count;
        public string Message = "";
        public List<PassRewardInfo> Rewards = new List<PassRewardInfo>();

        public override MessageId MessageId => MessageId.PASS_RESULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Code);
            wtr.Write(Success);
            wtr.Write(Count);
            wtr.WriteUTF16(Message ?? "");
            PassRewardInfo.WriteList(wtr, Rewards);
        }
    }
}
