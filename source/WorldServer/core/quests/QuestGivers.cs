using System;
using System.Collections.Generic;
using System.Linq;
using Shared.quests;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.quests
{
    // The quest NPCs by the Nexus spawn: the Tinkerer (daily, weekly and monthly quests) and the
    // Chronicler (the daily reward and achievements). The Rust client opens the quest window at
    // their tab when you walk up to them; they greet each player who comes near with what waits
    // for them, at most every GREET_AGAIN seconds. The Herald stands with them, at the rug's other
    // corner, and opens the server's week (DbWeeklyStats); he greets no one. Placed here like the Support Bot (NexusGuide),
    // on either side of the spawn rug (tools/build_castle_maps.py: CENTRE (48, 42), the fountains
    // four tiles out on the diagonals), so the map needn't change.
    public sealed class QuestGivers
    {
        private const float GREET_RANGE = 4.5f;
        private const float GREET_AGAIN = 180f;

        private static readonly (string Id, string Name, float X, float Y)[] Npcs =
        {
            ("The Tinkerer", "#The Tinkerer", 42.5f, 44.5f),
            ("The Chronicler", "#The Chronicler", 54.5f, 44.5f),
            ("The Herald", "#The Herald", 42.5f, 40.5f),
        };

        private readonly World _world;
        private readonly List<(Entity Npc, string Name)> _npcs = new List<(Entity, string)>();
        private readonly Dictionary<(int, int), float> _greeted = new Dictionary<(int, int), float>();
        private float _clock, _check;

        public QuestGivers(World world) => _world = world;

        public void Init()
        {
            foreach (var (id, name, x, y) in Npcs)
            {
                var (sx, sy) = FindSpot(x, y);
                var npc = _world.CreateNewEntity(id, sx, sy);
                if (npc != null)
                    _npcs.Add((npc, name));
            }
        }

        public void Update(ref TickTime time)
        {
            _clock += time.DeltaTime;
            _check -= time.DeltaTime;
            if (_check > 0)
                return;
            _check = 0.5f;
            foreach (var k in _greeted.Where(g => g.Value <= _clock).Select(g => g.Key).ToList())
                _greeted.Remove(k);
            for (var i = 0; i < _npcs.Count; i++)
            {
                var (npc, name) = _npcs[i];
                if (i > 1)
                    continue;
                foreach (var player in _world.Players.Values)
                {
                    if (_greeted.ContainsKey((i, player.Id)) || player.SqDistTo(npc) > GREET_RANGE * GREET_RANGE || player.Quests == null)
                        continue;
                    _greeted[(i, player.Id)] = _clock + GREET_AGAIN;
                    var line = i == 0 ? TinkererLine(player) : ChroniclerLine(player);
                    player.Client.SendPacket(new Text() { Name = name, ObjectId = npc.Id, NumStars = -1, BubbleTime = 5, Recipient = "", Txt = line });
                }
            }
        }

        private static string TinkererLine(Player player)
        {
            if (player.Client.Account.Guest)
                return "Register your account, and I'll have work for you.";
            var book = player.Quests.Book;
            var now = QuestTracker.Now;
            book.Refresh(now);
            var done = book.Current().Count(q => !q.Slot.Claimed && q.Slot.Progress >= q.Def.Goal);
            if (done > 0)
                return done == 1
                    ? $"{player.Name}! One of your quests is done. Come, claim your reward!"
                    : $"{player.Name}! {done} of your quests are done. Come, claim your rewards!";
            var bonus = Enum.GetValues(typeof(QuestPeriod)).Cast<QuestPeriod>().FirstOrDefault(book.BonusReady);
            if (book.BonusReady(bonus))
                return $"Every {QuestData.Name(bonus)} quest done? Splendid! Your bonus is waiting.";
            var open = book.Current().Count(q => q.Period == QuestPeriod.Daily && !q.Slot.Claimed);
            var reset = Span(QuestPeriods.NextReset(QuestPeriod.Daily, now) - now);
            return open == 0
                ? $"All done for today, {player.Name}! New quests in {reset}."
                : $"I have work for you, {player.Name}: {open} daily quest{(open == 1 ? "" : "s")}, new ones in {reset}. Press J or talk to me.";
        }

        private static string ChroniclerLine(Player player)
        {
            if (player.Client.Account.Guest)
                return "Your deeds go unrecorded, guest. Register, and I'll write them down.";
            var book = player.Quests.Book;
            var now = QuestTracker.Now;
            if (book.LoginReady(now))
                return $"Welcome back, {player.Name}! Day {book.State.Login.Step % Math.Max(1, book.Data.Login.Count) + 1} of your rewards is waiting.";
            var tiers = book.Data.Achievements.Count(a => book.State.Achievements.TryGetValue(a.Id, out var st) && QuestBook.Earned(a, st.Progress) > st.Claimed);
            if (tiers > 0)
                return $"I've written down {tiers} new deed{(tiers == 1 ? "" : "s")} of yours, {player.Name}. Claim {(tiers == 1 ? "its" : "their")} rewards!";
            var points = book.Points;
            var rank = book.Data.Ranks.Count == 0 ? "" : book.Data.Ranks[book.RankOf(points)].Name;
            return $"{rank} {player.Name}, {points} points of deeds. Your next daily reward comes in {Span(QuestPeriods.NextReset(QuestPeriod.Daily, now) - now)}.";
        }

        private static string Span(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes} minutes" : "a moment";

        // the spot, or the nearest open tile to it if the map has changed
        private (float X, float Y) FindSpot(float x, float y)
        {
            for (var r = 0; r <= 3; r++)
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                        if (_world.IsPassable(x + dx, y + dy, true))
                            return (x + dx, y + dy);
            return (x, y);
        }
    }
}
