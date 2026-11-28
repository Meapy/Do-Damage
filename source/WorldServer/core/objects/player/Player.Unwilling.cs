using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.core.objects
{
    // The Scroll of the Unwilling's power (RelicPower "Unwilling", Resurrection.xml): Esben, who
    // was made to serve, makes others serve. An enemy that dies within ThrallReach tiles of one of
    // its Summoner's summons rises as an Unwilling Thrall, an ally that fights for ThrallMs and
    // then shatters into ice: ShatterDamage, past defense, to every enemy within ShatterRadius,
    // slowing them. MaxThralls at a time (a new one shatters the oldest at once), and one every
    // ThrallGapMs at most, so a pack dying at once raises a few, not a crowd. Called from
    // Enemy.Death for every player in the world holding it.
    partial class Player
    {
        private const float ThrallReach = 4;
        private const int ThrallMs = 8000;
        private const int MaxThralls = 3;
        private const int ThrallGapMs = 500;
        private const int ShatterDamage = 250;
        private const float ShatterRadius = 2.5f;
        private const uint ThrallFrost = 0xFF8FD8FF;
        private const string ThrallId = "Unwilling Thrall";

        private readonly List<Entity> _thralls = new List<Entity>();
        private long _thrallReadyAt;

        // an enemy died in this player's world
        public void UnwillingDeath(Enemy victim)
        {
            if (Power(1) != "Unwilling" || World == null || victim.World != World || Health <= 0 || Now < _thrallReadyAt)
                return;
            if (!victim.ObjectDesc.Enemy)
                return;
            _summons.RemoveAll(s => s.Dead || s.World != World);
            if (!_summons.Any(s => s.DistTo(victim) <= ThrallReach))
                return;
            _thrallReadyAt = Now + ThrallGapMs;

            _thralls.RemoveAll(t => t.World != World);
            if (_thralls.Count >= MaxThralls)
            {
                var oldest = _thralls[0];
                _thralls.RemoveAt(0);
                Shatter(oldest);
            }
            var at = PosOf(victim);
            var thrall = SpawnAlly(ThrallId, at, 0);
            if (thrall == null)
                return;
            _thralls.Add(thrall);
            RelicFx(Fx.ThrallRise, thrall, at, 1, ThrallFrost, ThrallMs);
            var world = World;
            world.StartNewTimer(ThrallMs, (w, t) =>
            {
                if (_thralls.Remove(thrall))
                    Shatter(thrall);
            });
        }

        // the thrall falls apart: a burst of ice where it stands
        private void Shatter(Entity thrall)
        {
            var world = thrall.World;
            if (world == null)
                return;
            var at = PosOf(thrall);
            world.LeaveWorld(thrall);
            if (World != world)
                return;
            RelicFx(Fx.ThrallShatter, this, at, ShatterRadius, ThrallFrost);
            world.StartNewTimer(0, (w, t) =>
            {
                if (World != w)
                    return;
                foreach (var e in EnemiesNear(at.X, at.Y, ShatterRadius))
                {
                    if (e.HasConditionEffect(ConditionEffectIndex.Invincible) || e.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                        continue;
                    e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 2000);
                    RelicDamage(e, ShatterDamage, ref t, true);
                }
            });
        }
    }
}
