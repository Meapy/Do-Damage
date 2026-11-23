using System;
using System.Collections.Generic;
using System.Linq;
using Shared.quests;

namespace Shared.pass
{
    // Where pass XP came from (PASS_XP, and the window's "How to earn" list); the client words each.
    public enum PassSource : byte
    {
        QuestDaily = 0, QuestWeekly = 1, QuestMonthly = 2,
        BonusDaily = 3, BonusWeekly = 4, BonusMonthly = 5,
        Login = 6, FirstDungeon = 7, Dungeon = 8, Boss = 9, Oryx = 10, RealmClosed = 11, ArenaWave = 12, Fame = 13,
        Elder = 14, Staff = 15,
    }

    // What a pass request came to (PASS_RESULT): the client words each code, with the tier or
    // count; Message is the English for anything else.
    public enum PassCode : byte
    {
        Claimed = 0, ClaimedAll = 1, Unlocked = 2, CacheOpened = 3, AuraWorn = 4, AuraOff = 5, ColorWorn = 6, ColorOff = 7, TitleShown = 8, TitleHidden = 9,
        PetFxWorn = 10, PetFxOff = 11,
        NoSeason = 20, Guest = 21, NotReached = 22, AlreadyClaimed = 23, NothingToClaim = 24, NotEnoughGold = 25,
        AlreadyGilded = 26, PriceChanged = 27, NotOwned = 28, NoCache = 29, Failed = 30, NotHere = 31, Busy = 32, NotGilded = 33,
    }

    public sealed class PassClaim
    {
        public bool Ok;
        public PassCode Code;
        // the tier claimed, or how many tiers or caches
        public int Count;
        public string Message;
        public List<PassReward> Rewards = new List<PassReward>();

        public static PassClaim Fail(PassCode code, string message, int count = 0) => new PassClaim { Ok = false, Code = code, Message = message, Count = count };
    }

    // XP added: how much (Momentum included), the tiers and caches before and after.
    public readonly struct PassGain
    {
        public readonly int Gained;
        public readonly int TierBefore, TierAfter;
        public readonly bool Momentum;

        public PassGain(int gained, int before, int after, bool momentum)
        {
            Gained = gained;
            TierBefore = before;
            TierAfter = after;
            Momentum = momentum;
        }

        public bool TierUp => TierAfter > TierBefore;
    }

    // The rules, without the game: the season, XP and Momentum, tiers, claims and caches, and the
    // cosmetics owned. The world server (PassTracker) feeds it, takes the gold and pays.
    public sealed class PassBook
    {
        public readonly PassData Data;
        public readonly PassState State;
        public Season Season { get; private set; }

        public PassBook(PassData data, PassState state)
        {
            Data = data;
            State = state;
        }

        // Moves to the season running at `now`. When one has ended, what was reached and never
        // claimed comes back to be paid (caches rolled with `rng`): nobody loses a reward to the
        // calendar. Cosmetics stay; XP, claims and Gilded start afresh.
        public List<PassReward> Refresh(DateTime now, Random rng)
        {
            var leftover = new List<PassReward>();
            var season = Data.At(now);
            if (season == null)
            {
                Season = null;
                return leftover;
            }
            if (Season == null || Season.Key != season.Key)
                Season = season;
            if (State.Season == season.Key)
            {
                Unmark(season);
                return leftover;
            }
            if (State.Season != null)
            {
                var old = DefOf(State.Season);
                if (old != null)
                {
                    var tier = TierOf(State.Xp);
                    for (var t = 0; t < tier; t++)
                    {
                        if (!State.FreeTaken(t))
                            leftover.AddRange(old.FreeAt(t));
                        if (State.Gilded && !State.GildedTaken(t))
                            leftover.AddRange(old.GildedAt(t));
                    }
                    for (var c = State.CachesOpened; c < CachesOf(State.Xp); c++)
                        leftover.AddRange(RollCache(rng));
                }
            }
            State.Season = season.Key;
            State.Xp = 0;
            State.Gilded = false;
            State.FreeClaimed = 0;
            State.GildedClaimed = 0;
            State.CachesOpened = 0;
            State.FameCarry = 0;
            // a season begun afresh has no marks from before its tiers were filled
            State.FilledFor = season.Key;
            return Grant(leftover);
        }

        // Once a season: the marks on free tiers filled since they were set (SeasonDef.Filled) are
        // cleared, so their new rewards wait to be claimed.
        private void Unmark(Season season)
        {
            if (State.FilledFor == season.Key)
                return;
            foreach (var t in season.Def.Filled)
                if (t >= 1 && t <= Data.Tiers)
                    State.FreeClaimed &= ~(1L << (t - 1));
            State.FilledFor = season.Key;
        }

        // A season's definition by the key a state holds: its id, or "<id>-yyyy-MM".
        public SeasonDef DefOf(string key) =>
            Data.Seasons.Where(s => key == s.Id || key.StartsWith(s.Id + "-")).OrderByDescending(s => s.Id.Length).FirstOrDefault();

        public bool Active => Season != null && State.Season == Season.Key;

        public int TierOf(int xp) => Data.XpPerTier <= 0 ? 0 : Math.Min(Data.Tiers, xp / Data.XpPerTier);
        public int Tier => TierOf(State.Xp);

        // XP into the tier being worked on (past the last: towards the next cache), and what it needs.
        public int XpIntoTier => Tier < Data.Tiers ? State.Xp - Tier * Data.XpPerTier : Data.BonusXp <= 0 ? 0 : (State.Xp - Data.Tiers * Data.XpPerTier) % Data.BonusXp;
        public int XpForTier => Tier < Data.Tiers ? Data.XpPerTier : Data.BonusXp;

        public int CachesOf(int xp)
        {
            var past = xp - Data.Tiers * Data.XpPerTier;
            return past < 0 || Data.BonusXp <= 0 ? 0 : past / Data.BonusXp;
        }

        public int CachesReady => Math.Max(0, CachesOf(State.Xp) - State.CachesOpened);

        // The tier the season's pace has reached by `now`: every tier by Pace of the season.
        public double PaceTier(DateTime now)
        {
            if (Season == null)
                return 0;
            var span = (Season.EndsAt - Season.StartsAt).TotalSeconds * Data.Pace;
            var k = span <= 0 ? 1 : (now - Season.StartsAt).TotalSeconds / span;
            return Data.Tiers * Math.Clamp(k, 0, 1);
        }

        // Momentum: behind the pace (by a whole tier), XP comes Data.Momentum percent faster.
        public bool Momentum(DateTime now) => Active && Tier < Data.Tiers && Tier + 1 <= (int)Math.Floor(PaceTier(now));

        public PassGain Gain(int xp, DateTime now)
        {
            if (!Active || xp <= 0)
                return new PassGain(0, Tier, Tier, false);
            var momentum = Momentum(now);
            var gained = momentum ? xp + (int)Math.Round(xp * Data.Momentum / 100.0) : xp;
            var before = Tier;
            State.Xp = (int)Math.Min((long)State.Xp + gained, int.MaxValue / 2);
            return new PassGain(gained, before, Tier, momentum);
        }

        // The XP each thing is worth, before Momentum.
        public int QuestXp(QuestPeriod p) => p switch { QuestPeriod.Daily => Data.Xp.QuestDaily, QuestPeriod.Weekly => Data.Xp.QuestWeekly, _ => Data.Xp.QuestMonthly };
        public int BonusXpOf(QuestPeriod p) => p switch { QuestPeriod.Daily => Data.Xp.BonusDaily, QuestPeriod.Weekly => Data.Xp.BonusWeekly, _ => Data.Xp.BonusMonthly };
        public int DungeonXp(int difficulty) => Data.Xp.Dungeon + Data.Xp.DungeonStep * Math.Max(0, difficulty);

        // The first dungeon cleared on a UTC day pays its bonus once.
        public bool FirstDungeonReady(DateTime now) => State.FirstDungeonDay < QuestPeriods.DayKey(now);

        public bool TakeFirstDungeon(DateTime now)
        {
            if (!Active || !FirstDungeonReady(now))
                return false;
            State.FirstDungeonDay = QuestPeriods.DayKey(now);
            return true;
        }

        // Base fame earned: the pass XP it's worth now, the rest carried to the next.
        public int FameXp(int fame)
        {
            if (!Active || fame <= 0 || Data.Xp.FamePer <= 0)
                return 0;
            var total = State.FameCarry + fame;
            State.FameCarry = total % Data.Xp.FamePer;
            return total / Data.Xp.FamePer;
        }

        public bool FreeReady(int t) => t < Tier && !State.FreeTaken(t) && Season.Def.FreeAt(t).Count > 0;
        public bool GildedReady(int t) => State.Gilded && t < Tier && !State.GildedTaken(t) && Season.Def.GildedAt(t).Count > 0;

        // Rewards waiting: each tier with something to claim, and each cache.
        public int ReadyCount => !Active ? 0 : Enumerable.Range(0, Data.Tiers).Count(t => FreeReady(t) || GildedReady(t)) + CachesReady;

        // A tier's rewards (both tracks, as far as the account has them); -1 every tier reached.
        public PassClaim Claim(int tier)
        {
            if (!Active)
                return PassClaim.Fail(PassCode.NoSeason, "There's no season running.");
            if (tier < 0)
            {
                var result = new PassClaim { Ok = true, Code = PassCode.ClaimedAll };
                for (var t = 0; t < Tier; t++)
                    if (Take(t, result.Rewards))
                        result.Count++;
                if (result.Count == 0)
                    return PassClaim.Fail(PassCode.NothingToClaim, "Nothing waits to be claimed.");
                result.Message = $"{result.Count} tier{(result.Count == 1 ? "" : "s")} claimed!";
                result.Rewards = Grant(result.Rewards);
                return result;
            }
            if (tier >= Data.Tiers)
                return PassClaim.Fail(PassCode.NotReached, "There's no such tier.", tier + 1);
            if (tier >= Tier)
                return PassClaim.Fail(PassCode.NotReached, $"Reach tier {tier + 1} first.", tier + 1);
            var claim = new PassClaim { Ok = true, Code = PassCode.Claimed, Count = tier + 1, Message = $"Tier {tier + 1} claimed!" };
            if (!Take(tier, claim.Rewards))
                return PassClaim.Fail(PassCode.AlreadyClaimed, "You already claimed that tier.", tier + 1);
            claim.Rewards = Grant(claim.Rewards);
            return claim;
        }

        // Marks what's ready on a tier claimed, adding its rewards; false if nothing was.
        private bool Take(int t, List<PassReward> into)
        {
            var took = false;
            if (FreeReady(t))
            {
                State.FreeClaimed |= 1L << t;
                into.AddRange(Season.Def.FreeAt(t));
                took = true;
            }
            if (GildedReady(t))
            {
                State.GildedClaimed |= 1L << t;
                into.AddRange(Season.Def.GildedAt(t));
                took = true;
            }
            // a tier with nothing on a track is marked too, so it never shows as waiting
            if (took && t < Tier)
            {
                if (Season.Def.FreeAt(t).Count == 0)
                    State.FreeClaimed |= 1L << t;
                if (State.Gilded && Season.Def.GildedAt(t).Count == 0)
                    State.GildedClaimed |= 1L << t;
            }
            return took;
        }

        public PassClaim OpenCaches(Random rng)
        {
            if (!Active)
                return PassClaim.Fail(PassCode.NoSeason, "There's no season running.");
            var n = CachesReady;
            if (n == 0)
                return PassClaim.Fail(PassCode.NoCache, $"Every {Data.BonusXp:N0} pass XP past tier {Data.Tiers} is a Wayfarer's Cache.");
            var result = new PassClaim { Ok = true, Code = PassCode.CacheOpened, Count = n, Message = n == 1 ? "Wayfarer's Cache opened!" : $"{n} Wayfarer's Caches opened!" };
            for (var i = 0; i < n; i++)
                result.Rewards.AddRange(RollCache(rng));
            State.CachesOpened += n;
            result.Rewards = Grant(result.Rewards);
            return result;
        }

        private List<PassReward> RollCache(Random rng)
        {
            var lines = Data.Cache.Where(l => l.Weight > 0).ToList();
            if (lines.Count == 0)
                return new List<PassReward>();
            var roll = rng.NextDouble() * lines.Sum(l => l.Weight);
            foreach (var l in lines)
            {
                roll -= l.Weight;
                if (roll < 0)
                    return l.Rewards.ToList();
            }
            return lines[^1].Rewards.ToList();
        }

        // Records the Gilded track bought (the tracker has taken the gold).
        public PassClaim CanUnlock(int price)
        {
            if (!Active)
                return PassClaim.Fail(PassCode.NoSeason, "There's no season running.");
            if (State.Gilded)
                return PassClaim.Fail(PassCode.AlreadyGilded, "Your pass is Gilded already.");
            if (price != Data.Price)
                return PassClaim.Fail(PassCode.PriceChanged, $"The Gilded pass costs {Data.Price:N0} gold.");
            return new PassClaim { Ok = true, Code = PassCode.Unlocked, Message = $"{Season.Name}: Gilded!" };
        }

        public void MarkGilded()
        {
            State.Gilded = true;
            if (!State.GildedSeasons.Contains(Season.Key))
                State.GildedSeasons.Add(Season.Key);
        }

        // Auras, name colours and pet effects become the account's (the first aura and pet effect
        // are worn at once); one it has already pays fame instead. Other rewards pass as they are.
        public List<PassReward> Grant(List<PassReward> rewards)
        {
            var paid = new List<PassReward>();
            foreach (var r in rewards)
            {
                if (r.Aura > 0)
                {
                    if (State.Auras.Contains(r.Aura))
                    {
                        paid.Add(new PassReward { Fame = Data.RepeatFame });
                        continue;
                    }
                    State.Auras.Add(r.Aura);
                    if (State.Aura == 0)
                        State.Aura = r.Aura;
                }
                else if (r.Color > 0)
                {
                    if (State.Colors.Contains(r.Color))
                    {
                        paid.Add(new PassReward { Fame = Data.RepeatFame });
                        continue;
                    }
                    State.Colors.Add(r.Color);
                }
                else if (r.PetFx > 0)
                {
                    if (State.PetEffects.Contains(r.PetFx))
                    {
                        paid.Add(new PassReward { Fame = Data.RepeatFame });
                        continue;
                    }
                    State.PetEffects.Add(r.PetFx);
                    if (State.PetEffect == 0)
                        State.PetEffect = r.PetFx;
                }
                paid.Add(r);
            }
            return paid;
        }

        public PassClaim WearAura(int id)
        {
            if (id == 0)
            {
                State.Aura = 0;
                return new PassClaim { Ok = true, Code = PassCode.AuraOff, Message = "You wear no aura." };
            }
            if (!State.Auras.Contains(id) || Data.Aura(id) == null)
                return PassClaim.Fail(PassCode.NotOwned, "You don't have that aura.");
            State.Aura = id;
            return new PassClaim { Ok = true, Code = PassCode.AuraWorn, Count = id, Message = $"You wear {Data.Aura(id).Name}." };
        }

        public PassClaim WearColor(int id)
        {
            if (id == 0)
            {
                State.Color = 0;
                return new PassClaim { Ok = true, Code = PassCode.ColorOff, Message = "Your name has its usual colour again." };
            }
            if (!State.Colors.Contains(id) || Data.Color(id) == null)
                return PassClaim.Fail(PassCode.NotOwned, "You don't have that name colour.");
            State.Color = id;
            return new PassClaim { Ok = true, Code = PassCode.ColorWorn, Count = id, Message = $"Your name shows in {Data.Color(id).Name}." };
        }

        public PassClaim WearPetEffect(int id)
        {
            if (id == 0)
            {
                State.PetEffect = 0;
                return new PassClaim { Ok = true, Code = PassCode.PetFxOff, Message = "Your pet wears no effect." };
            }
            if (!State.PetEffects.Contains(id) || Data.PetEffect(id) == null)
                return PassClaim.Fail(PassCode.NotOwned, "You don't have that pet effect.");
            State.PetEffect = id;
            return new PassClaim { Ok = true, Code = PassCode.PetFxWorn, Count = id, Message = $"Your pet wears {Data.PetEffect(id).Name}." };
        }

        // The Gilded pass's [Gilded] title in chat, shown or hidden (only while the pass is Gilded).
        public PassClaim ShowTitle(bool show)
        {
            if (!Active || !State.Gilded)
                return PassClaim.Fail(PassCode.NotGilded, "Only a Gilded pass has the chat title.");
            State.HideTitle = !show;
            return show
                ? new PassClaim { Ok = true, Code = PassCode.TitleShown, Message = "Your [Gilded] title shows in chat." }
                : new PassClaim { Ok = true, Code = PassCode.TitleHidden, Message = "Your [Gilded] title is hidden in chat." };
        }

        // The community goals' loot while the pass is Gilded: Overdrive levels of luck, and rolls more.
        public (int Luck, int Rolls) CommunityBoost => Active && State.Gilded ? (Math.Max(0, Data.Community.Luck), Math.Max(0, Data.Community.Rolls)) : (0, 0);

        // The worn aura (0: none, or one the data no longer has).
        public int WornAura => State.Aura > 0 && Data.Aura(State.Aura) != null && State.Auras.Contains(State.Aura) ? State.Aura : 0;

        // The worn pet effect (0: none, or one the data no longer has).
        public int WornPetEffect => State.PetEffect > 0 && Data.PetEffect(State.PetEffect) != null && State.PetEffects.Contains(State.PetEffect) ? State.PetEffect : 0;
    }
}
