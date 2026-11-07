using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// What a raisable boss leaves where it fell: its body, lying down, circled by its escaping souls
    /// (the client draws both: the object's <c>&lt;Corpse/&gt;</c> and its <c>Souls</c> effect). The
    /// souls call out, by name and in turn, the players near who carry a Necromantic Relic, so the
    /// whole group knows who could raise it. Unraised, the souls scatter after
    /// <see cref="BossResurrection.RemainsMs"/>. A relic used beside it starts the ritual: three
    /// seconds of souls gathering into a skull of soul-fire over the body, then a blast, and the boss
    /// stands again in its Empowered form.
    /// </summary>
    public sealed class BossRemains : StaticObject
    {
        private const string Souls = "Escaping Souls";
        private const int WhisperMs = 7000;
        private const int AmbientMs = 13000;
        private const int RitualMs = 3000;

        public BossInfo Info { get; }
        public bool Rising { get; private set; }

        private int _left;
        private int _whisperIn = 2500;
        private int _turn;
        private readonly HashSet<int> _called = new HashSet<int>();
        private int _ritual;
        private int _nextPull;
        private int _bolt;
        private StaticObject _soul;
        private string _raiser;

        private static readonly string[] Calls =
        {
            "We hear your power, {player}... Use the Necromantic Relic! Bring our master back!",
            "{player}, the artifact in your hands can wake the titan... We beg you!",
            "{player}... the relic sings to us. Raise {boss}!",
            "Closer, {player}... lay the relic upon our master's bones!",
            "Do not let {boss} rest, {player}! The relic... use it!",
            "We feel it, {player}... the relic burns so bright. Give {boss} back to us!",
        };
        private static readonly string[] Fading =
        {
            "Hurry, {player}! We are fading... the relic, NOW!",
            "{player}... we cannot hold on much longer... raise {boss}!",
        };
        private static readonly string[] Ambient =
        {
            "Master... master, rise...",
            "Is there no one... to bring {boss} back?",
            "Cold... so cold... a relic... we need a relic...",
            "We will not let go... not yet...",
        };

        public BossRemains(GameServer manager, BossInfo info)
            : base(manager, info.RemainsType, null, true, false, false)
        {
            Info = info;
            _left = BossResurrection.RemainsMs;
        }

        public override void Tick(ref TickTime time)
        {
            base.Tick(ref time);
            if (World == null || Dead)
                return;

            if (Rising)
            {
                RitualTick(time.ElapsedMsDelta);
                return;
            }

            _left -= time.ElapsedMsDelta;
            if (_left <= 0)
            {
                Scatter();
                return;
            }

            _whisperIn -= time.ElapsedMsDelta;
            if (_whisperIn > 0)
                return;

            var holders = World.Players.Values.Where(p => p.World == World && p.DistTo(this) <= 25 && BossResurrection.Holds(p))
                .OrderBy(p => p.Id).ToList();
            if (holders.Count == 0)
            {
                Say(Pick(Ambient), null, false);
                _whisperIn = AmbientMs;
                return;
            }

            var holder = holders[_turn++ % holders.Count];
            Say(Pick(_left < 15000 ? Fading : Calls), holder.Name, true);
            // the first call to each shows over their head too, for everyone to see
            if (_called.Add(holder.Id))
                BossResurrection.Notify(holder, "The souls call to you", BossResurrection.SoulGreen);
            _whisperIn = WhisperMs;
        }

        private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

        private void Say(string line, string player, bool chat) =>
            BossResurrection.Say(this, Souls, line.Replace("{player}", player ?? "").Replace("{boss}", Info.Name), chat);

        // no relic in time: the souls drift off and the body goes
        private void Scatter()
        {
            Say("The souls scatter into the dark... {boss} sleeps for good.", null, false);
            BossResurrection.Effect(this, EffectType.Diffuse, BossResurrection.SoulTeal, new Position(X, Y), new Position(X + 3, Y));
            World.LeaveWorld(this);
        }

        public void BeginRitual(Player raiser)
        {
            if (Rising || World == null)
                return;
            Rising = true;
            _raiser = raiser.Name;
            _ritual = 0;
            _nextPull = 0;
            _bolt = 0;

            if (BossResurrection.RitualSoulType != 0)
            {
                _soul = new StaticObject(GameServer, BossResurrection.RitualSoulType, null, true, false, false);
                _soul.Move(X, Y + 0.05f);
                _soul.Z = Z;
                World.EnterWorld(_soul);
            }

            BossResurrection.Sound(this, 0);
            BossResurrection.Effect(this, EffectType.Flashing, BossResurrection.SoulViolet, new Position(0.25f, 12));
            BossResurrection.Notify(raiser, $"Raises {Info.Name}!", BossResurrection.SoulViolet);
            Say("YES! {player} answers us! Rise, {boss}... RISE!", raiser.Name, true);
            var msg = $"{raiser.Name} has used a Necromantic Relic: {Info.Name} is rising!";
            World.ForeachPlayer(p => p.SendInfo(msg));
        }

        private static readonly int[] Bolts = { 700, 1500, 2300 };

        private void RitualTick(int dt)
        {
            _ritual += dt;
            // the souls rush in from all round
            if (_ritual >= _nextPull && _ritual < RitualMs)
            {
                var r = 2.5f + 2f * _ritual / RitualMs;
                BossResurrection.Effect(this, EffectType.Concentrate, BossResurrection.SoulGreen, new Position(X, Y), new Position(X + r, Y));
                _nextPull += 300;
            }
            // soul-fire strikes down from the skull into the body
            if (_bolt < Bolts.Length && _ritual >= Bolts[_bolt])
            {
                _bolt++;
                if (_soul?.World != null)
                    BossResurrection.Effect(_soul, EffectType.Lightning, BossResurrection.SoulGreen, new Position(X, Y), new Position(110, 0));
            }
            if (_ritual >= RitualMs)
                Rise();
        }

        private void Rise()
        {
            var world = World;
            var pos = new Position(X, Y);
            BossResurrection.Sound(this, 1);
            BossResurrection.Effect(this, EffectType.AreaBlast, BossResurrection.SoulGreen, new Position(6, 0));
            BossResurrection.Effect(this, EffectType.Diffuse, BossResurrection.SoulViolet, pos, new Position(X + 4.5f, Y));
            // (no Earthquake: the client's shake builds for good until the world changes)
            BossResurrection.Effect(this, EffectType.Trap, BossResurrection.SoulViolet, new Position(5, 0));

            if (_soul?.World != null)
                world.LeaveWorld(_soul);
            world.LeaveWorld(this);

            if (Entity.Resolve(GameServer, Info.EmpoweredType) is not Enemy boss)
                return;
            boss.Empowered = new Empowered(Info);
            boss.Move(pos.X, pos.Y);
            boss.Z = Z;
            world.EnterWorld(boss);
            boss.Empowered.Risen(boss, _raiser);
        }
    }
}
