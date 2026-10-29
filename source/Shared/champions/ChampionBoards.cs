using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Shared.champions
{
    // The Hall of Champions: the living's leaderboard in the Nexus, beside the Legends (the dead's).
    // Each board ranks accounts by one thing, and its top 10 hold a reward of the board's own kind
    // while they stay there: #1, #2-3 and #4-10 each get a step of it. The account server ranks
    // everyone every few minutes (ChampionSweep) and writes who holds what into Redis
    // (DbChampions); the world servers read that and apply it (ChampionCache, Player.Champion).
    // This file is only the rules, so dotnet test source\Tests covers them.
    public enum ChampionTab : byte { Fame = 0, Stars = 1, Achievements = 2, Arena = 3, WeeklyFame = 4, WeeklyDungeons = 5 }

    public enum ChampionReward : byte
    {
        Loot,           // a loot boost everywhere (Value: a share, 0.03 is +3%)
        Glow,           // a glow around the character (Value: 0xRRGGBB), unless they chose their own
        NameColor,      // the name's colour in chat (Value: 0xRRGGBB), over the rank colour picked
        Experience,     // more experience, which at level 20 is fame (Value: a share)
        DungeonLoot,    // a loot boost in dungeons, anywhere but the realms (Value: a share)
        ArenaFame,      // more fame from the Arena's waves (Value: a share)
    }

    public sealed class ChampionBoardDef
    {
        public ChampionTab Tab;
        public string Id;
        public string Name;
        public bool Weekly;
        // what the value counts, after the number: "fame", "stars", "points", "dungeons"
        public string Unit;
        // one line on the board saying what it ranks
        public string Description;
        public ChampionReward Reward;
        // the reward's value for each tier: #1, #2-3, #4-10
        public double[] Values;

        public double ValueAt(int tier) => tier >= 0 && tier < Values.Length ? Values[tier] : 0;

        // what a tier's reward says, on the board and in chat
        public string RewardText(int tier)
        {
            var v = ValueAt(tier);
            var metal = ChampionBoards.TierMetal(tier);
            return Reward switch
            {
                ChampionReward.Loot => $"+{Percent(v)} loot",
                ChampionReward.Glow => $"{metal} glow",
                ChampionReward.NameColor => $"{metal} name in chat",
                ChampionReward.Experience => $"+{Percent(v)} experience and fame",
                ChampionReward.DungeonLoot => $"+{Percent(v)} loot in dungeons",
                ChampionReward.ArenaFame => $"+{Percent(v)} fame from Arena waves",
                _ => "",
            };
        }

        public static string Percent(double share) => (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    public static class ChampionBoards
    {
        // places that hold a reward, and that the board lists
        public const int Places = 10;

        // champion loot boosts (every board's, together) never add up to more than this; the
        // Legends' 1% (LootBoosts.Legend) comes on top
        public const double LootCap = 0.04;

        // how often the account server ranks everyone again
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        public const int Gold = 0xFFD24A, Silver = 0xD3DEEA, Bronze = 0xE0965A;

        public static readonly ChampionBoardDef[] All =
        {
            new ChampionBoardDef
            {
                Tab = ChampionTab.Fame, Id = "fame", Name = "Fame", Unit = "fame",
                Description = "The most fame on a living character: each account's best.",
                Reward = ChampionReward.Loot, Values = new[] { 0.03, 0.02, 0.01 },
            },
            new ChampionBoardDef
            {
                Tab = ChampionTab.Stars, Id = "stars", Name = "Stars", Unit = "stars",
                Description = "Class stars: up to five for each class, from its best character's fame.",
                Reward = ChampionReward.Glow, Values = new double[] { Gold, Silver, Bronze },
            },
            new ChampionBoardDef
            {
                Tab = ChampionTab.Achievements, Id = "achievements", Name = "Achievements", Unit = "points",
                Description = "Achievement points, from the tiers claimed in the quest log.",
                Reward = ChampionReward.NameColor, Values = new double[] { Gold, Silver, Bronze },
            },
            new ChampionBoardDef
            {
                Tab = ChampionTab.Arena, Id = "arena", Name = "Arena", Unit = "wave",
                Description = "The best Arena run you took part in: its wave, then the quicker run.",
                Reward = ChampionReward.ArenaFame, Values = new[] { 0.15, 0.10, 0.05 },
            },
            new ChampionBoardDef
            {
                Tab = ChampionTab.WeeklyFame, Id = "weeklyFame", Name = "Weekly Fame", Weekly = true, Unit = "fame",
                Description = "Fame earned this week, on all your characters together.",
                Reward = ChampionReward.Experience, Values = new[] { 0.10, 0.06, 0.03 },
            },
            new ChampionBoardDef
            {
                Tab = ChampionTab.WeeklyDungeons, Id = "weeklyDungeons", Name = "Weekly Dungeons", Weekly = true, Unit = "dungeons",
                Description = "Dungeons cleared this week: be near a boss when the last one falls.",
                Reward = ChampionReward.DungeonLoot, Values = new[] { 0.03, 0.02, 0.01 },
            },
        };

        public static ChampionBoardDef Of(ChampionTab tab) => All[(int)tab];

        public static ChampionBoardDef ById(string id) =>
            All.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

        // 0 for #1, 1 for #2-3, 2 for #4-10, -1 otherwise
        public static int TierOf(int rank) => rank switch
        {
            1 => 0,
            2 or 3 => 1,
            >= 4 and <= Places => 2,
            _ => -1,
        };

        public static string TierMetal(int tier) => tier switch { 0 => "Gold", 1 => "Silver", 2 => "Bronze", _ => "" };

        public static string TierPlaces(int tier) => tier switch { 0 => "#1", 1 => "#2-3", 2 => "#4-10", _ => "" };

        // Ranks the candidates: the highest value first, a tie to the higher tiebreak, then to the
        // older account (the lower id). Nothing at 0 is ranked. Every place is its own (1, 2, 3...).
        public static List<ChampionRanked> Rank(IEnumerable<ChampionRanked> candidates) =>
            candidates.Where(c => c.Value > 0)
                .OrderByDescending(c => c.Value).ThenByDescending(c => c.Tiebreak).ThenBy(c => c.AccountId)
                .Select((c, i) => { c.Rank = i + 1; return c; })
                .ToList();

        // Who holds a reward on which boards: each account in any top 10, with its places.
        public static Dictionary<int, ChampionPlaces> Holders(IReadOnlyList<List<ChampionRanked>> boards)
        {
            var places = new Dictionary<int, ChampionPlaces>();
            for (var b = 0; b < boards.Count && b < All.Length; b++)
                foreach (var r in boards[b].Where(r => r.Rank <= Places))
                {
                    places.TryGetValue(r.AccountId, out var p);
                    places[r.AccountId] = p.With((ChampionTab)b, r.Rank);
                }
            return places;
        }
    }

    // One account on a board.
    public sealed class ChampionRanked
    {
        public int AccountId;
        public string Name;
        public long Value;
        public long Tiebreak;
        public int Rank;
        // the character shown beside the name (the account's best living one; ObjectType 0 if none)
        public ushort ObjectType;
        public int Skin, Tex1, Tex2, Level;
    }

    // An account's places on the boards (0 where it isn't in the top 10), and what they give.
    public struct ChampionPlaces : IEquatable<ChampionPlaces>
    {
        private readonly byte[] _ranks;

        private ChampionPlaces(byte[] ranks) { _ranks = ranks; }

        public static readonly ChampionPlaces None = default;

        public int RankOn(ChampionTab tab) => _ranks == null ? 0 : _ranks[(int)tab];

        public bool Any => _ranks != null && _ranks.Any(r => r != 0);

        public ChampionPlaces With(ChampionTab tab, int rank)
        {
            var ranks = _ranks != null ? (byte[])_ranks.Clone() : new byte[ChampionBoards.All.Length];
            ranks[(int)tab] = (byte)Math.Clamp(rank, 0, ChampionBoards.Places);
            return new ChampionPlaces(ranks);
        }

        // the value of a board's reward at this account's place there (0 if none)
        public double ValueOf(ChampionTab tab) => ChampionBoards.Of(tab).ValueAt(ChampionBoards.TierOf(RankOn(tab)));

        // The loot boost the boards give, in a dungeon or not, capped at LootCap.
        public double Loot(bool dungeon)
        {
            var total = 0.0;
            foreach (var b in ChampionBoards.All)
                if (b.Reward == ChampionReward.Loot || (dungeon && b.Reward == ChampionReward.DungeonLoot))
                    total += ValueOf(b.Tab);
            return Math.Min(total, ChampionBoards.LootCap);
        }

        public double Experience => Sum(ChampionReward.Experience);

        public double ArenaFame => Sum(ChampionReward.ArenaFame);

        public int Glow => (int)Best(ChampionReward.Glow);

        public int NameColor => (int)Best(ChampionReward.NameColor);

        private double Sum(ChampionReward kind)
        {
            var total = 0.0;
            foreach (var b in ChampionBoards.All)
                if (b.Reward == kind)
                    total += ValueOf(b.Tab);
            return total;
        }

        // a colour reward from the best place among the boards giving that kind
        private double Best(ChampionReward kind)
        {
            var best = (Rank: int.MaxValue, Value: 0.0);
            foreach (var b in ChampionBoards.All)
            {
                var r = RankOn(b.Tab);
                if (b.Reward == kind && r > 0 && r < best.Rank)
                    best = (r, ValueOf(b.Tab));
            }
            return best.Value;
        }

        // A line per board held: "#2 on Weekly Fame: +6% experience and fame".
        public IEnumerable<string> Describe()
        {
            foreach (var b in ChampionBoards.All)
            {
                var r = RankOn(b.Tab);
                if (r > 0)
                    yield return $"#{r} on {b.Name}: {b.RewardText(ChampionBoards.TierOf(r))}";
            }
        }

        // "0:1,3:5": board index and place, for Redis's champions:rewards hash
        public string Encode()
        {
            if (_ranks == null)
                return "";
            var sb = new StringBuilder();
            for (var i = 0; i < _ranks.Length; i++)
                if (_ranks[i] != 0)
                {
                    if (sb.Length > 0)
                        sb.Append(',');
                    sb.Append(i).Append(':').Append(_ranks[i]);
                }
            return sb.ToString();
        }

        public static ChampionPlaces Decode(string s)
        {
            var p = None;
            if (string.IsNullOrEmpty(s))
                return p;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out var tab) && int.TryParse(kv[1], out var rank)
                    && tab >= 0 && tab < ChampionBoards.All.Length && rank > 0)
                    p = p.With((ChampionTab)tab, rank);
            }
            return p;
        }

        public bool Equals(ChampionPlaces other)
        {
            for (var i = 0; i < ChampionBoards.All.Length; i++)
                if (RankOn((ChampionTab)i) != other.RankOn((ChampionTab)i))
                    return false;
            return true;
        }

        public override bool Equals(object obj) => obj is ChampionPlaces p && Equals(p);

        public override int GetHashCode() => Encode().GetHashCode();
    }
}
