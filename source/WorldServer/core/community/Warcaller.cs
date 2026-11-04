using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.community
{
    // The Warcaller at the north point of the Nexus spawn's island (tools/build_nexus.py:
    // WARCALLER, keep the two in step): the Rust client opens the community goals at him. He
    // greets each player who comes near with what waits for them (at most every GREET_AGAIN
    // seconds), and every CALL_EVERY seconds rallies everyone in sight with how a goal stands.
    public sealed class Warcaller
    {
        public const string ID = "The Warcaller";
        private const float X = 68.5f, Y = 53.5f;
        private const float GREET_RANGE = 4.5f;
        private const float GREET_AGAIN = 180f;
        private const float CALL_RANGE = 14f;
        private const float CALL_EVERY = 75f;

        private readonly World _world;
        private Entity _npc;
        private readonly Dictionary<int, float> _greeted = new Dictionary<int, float>();
        private float _clock, _check, _call = 20f;
        private int _calls;

        public Warcaller(World world) => _world = world;

        public void Init()
        {
            for (var r = 0; r <= 3 && _npc == null; r++)
                for (var dy = -r; dy <= r && _npc == null; dy++)
                    for (var dx = -r; dx <= r && _npc == null; dx++)
                        if (_world.IsPassable(X + dx, Y + dy, true))
                            _npc = _world.CreateNewEntity(ID, X + dx, Y + dy);
        }

        public void Update(ref TickTime time)
        {
            if (_npc == null)
                return;
            _clock += time.DeltaTime;
            _call -= time.DeltaTime;
            if (_call <= 0)
            {
                _call = CALL_EVERY;
                Rally();
            }
            _check -= time.DeltaTime;
            if (_check > 0)
                return;
            _check = 0.5f;
            foreach (var k in _greeted.Where(g => g.Value <= _clock).Select(g => g.Key).ToList())
                _greeted.Remove(k);
            foreach (var player in _world.Players.Values)
            {
                if (_greeted.ContainsKey(player.AccountId) || player.SqDistTo(_npc) > GREET_RANGE * GREET_RANGE || player.Client?.Account == null)
                    continue;
                _greeted[player.AccountId] = _clock + GREET_AGAIN;
                Say(player, Greeting(player));
            }
        }

        private void Say(Player player, string line) =>
            player.Client.SendPacket(new Text() { Name = CommunityGoals.NPC_NAME, ObjectId = _npc.Id, NumStars = -1, BubbleTime = 6, Recipient = "", Txt = line });

        // Each goal in turn: how far the realm has come, and how far to the next tier.
        private void Rally()
        {
            var running = CommunityGoals.Running;
            var near = _world.Players.Values.Where(p => p.Client?.Account != null && p.SqDistTo(_npc) <= CALL_RANGE * CALL_RANGE).ToList();
            if (running.Count == 0 || near.Count == 0)
                return;
            var rs = running[_calls++ % running.Count];
            var line = Standing(rs);
            foreach (var p in near)
                Say(p, line);
        }

        private static string Standing(CommunityGoals.RunState rs)
        {
            var data = CommunityGoals.Data;
            var def = rs.Run.Def;
            var goal = rs.Goal;
            var stage = data.Stage(def, goal, rs.Total);
            var next = data.NextAt(def, goal, stage);
            if (next < 0)
                return $"\"{def.Title}\": {rs.Total:N0} of {goal:N0}, every {data.BonusName} level taken! Songs will be sung of all {rs.Participants:N0} of you!";
            var left = next - rs.Total;
            var nextName = data.StageName(def, stage);
            if (stage >= data.Tiers(def))
                return $"\"{def.Title}\" is done, {rs.Total * 100 / Math.Max(1, goal)}% and rising! {left:N0} more for {nextName}. Don't stop now!";
            var cry = rs.Participants == 0
                ? "Who will be first to answer the call?"
                : $"{rs.Participants:N0} have answered the call.";
            return $"\"{def.Title}\": {rs.Total:N0} of {goal:N0}! {left:N0} more for {nextName}. {cry}";
        }

        private static string Greeting(Player player)
        {
            if (player.Client.Account.Guest)
                return "Register your account, and fight with the realm for its rewards!";
            var running = CommunityGoals.Running;
            if (running.Count == 0)
                return "The realm rests this week. Come back when the horn sounds again.";
            var waiting = CommunityGoals.WaitingFor(player);
            if (waiting > 0)
                return waiting == 1
                    ? $"{player.Name}! The realm's victory is yours too: a reward waits. Claim it!"
                    : $"{player.Name}! The realm's victories are yours too: {waiting} rewards wait. Claim them!";
            var rs = running.FirstOrDefault(r => CommunityGoals.ShareOf(player, r) == 0);
            if (rs != null)
                return $"{player.Name}, the realm needs you: {rs.Run.Def.Describe(rs.Goal)} Do your part and share the spoils!";
            var left = EndsIn(running[0]);
            return $"Well fought, {player.Name}! You've done your part in every goal this week. {left}";
        }

        private static string EndsIn(CommunityGoals.RunState rs)
        {
            var end = rs.Run.End - QuestTracker.Now;
            var span = end.TotalDays >= 1 ? $"{(int)end.TotalDays}d {end.Hours}h" : end.TotalHours >= 1 ? $"{(int)end.TotalHours}h {end.Minutes}m" : $"{Math.Max(0, end.Minutes)} minutes";
            return $"The week's goals end in {span}.";
        }
    }
}
