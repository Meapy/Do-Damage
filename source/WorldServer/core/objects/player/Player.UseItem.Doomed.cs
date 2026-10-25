using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // Abilities of the classes ported from Doomed Realms (realm/entities/player/Player.UseItem.cs there).
    public partial class Player
    {
        // Turrets, summons and pearl substitutes fight for the player like betterSkillys allies
        // (their behaviours are in logic/db/BehaviorDb.DoomedAllies.cs).
        private Entity SpawnAlly(string objectId, Position at, int lifetimeMs)
        {
            var ally = Resolve(GameServer, objectId);
            if (ally == null)
            {
                SendError($"{objectId} doesn't exist on this server.");
                return null;
            }
            ally.AllyOwnerId = Id;
            ally.GivesNoXp = true;
            ally.ApplyConditionEffect(ConditionEffectIndex.Invincible, -1);
            ally.Move(at.X, at.Y);
            World.EnterWorld(ally);
            if (lifetimeMs > 0)
                World.StartNewTimer(lifetimeMs, (world, t) => world.LeaveWorld(ally));
            return ally;
        }

        // Druid pearls: a substitute that fires a volley at nearby enemies, and a heal for the druid.
        private void AEPearlAbility(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            SpawnAlly(eff.ObjectId, new Position(X, Y), 5000);
            if (eff.Amount > 0)
                ActivateHealHp(this, eff.Amount);
        }

        // Mechanic turrets: stay where they are built and shoot nearby enemies for the duration.
        private void AEBuildTower(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            SpawnAlly(eff.ObjectId, new Position(X, Y), eff.DurationMS > 0 ? eff.DurationMS : 10000);
        }

        // Summoner scrolls: "<objectId> Summon" follows the player and attacks for the duration. One
        // at a time: a new summon sends the last one away, or a summon lasting 90-120 s for 100 MP
        // could be stacked several deep.
        private Entity _summon;

        private void AESummon(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            if (_summon != null && _summon.World == World && !_summon.Dead)
                World.LeaveWorld(_summon);
            _summon = SpawnAlly(eff.ObjectId + " Summon", new Position(X, Y), eff.DurationMS > 0 ? eff.DurationMS : 30000);
        }

        // Throws a monster (hostile, as in Doomed Realms) that appears at the target after 1.5 s.
        private void AEMonsterToss(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            if (MathsUtils.DistSqr(target.X, target.Y, X, Y) > MAX_ABILITY_DIST_SQR)
                return;
            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Throw,
                Color = new ARGB(0xff000000),
                TargetObjectId = Id,
                Pos1 = target
            }, this);
            World.StartNewTimer(1500, (world, t) =>
            {
                var monster = Resolve(GameServer, eff.ObjectId);
                if (monster == null)
                    return;
                monster.Move(target.X, target.Y);
                world.EnterWorld(monster);
            });
        }

        // Disco Lights: a random buff (Damaging, Speedy, Berserk or Healing) for everyone nearby.
        private void AEPartyAOE(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            ConditionEffectIndex effect;
            uint color;
            switch (Random.Shared.Next(4))
            {
                case 0: effect = ConditionEffectIndex.Damaging; color = 0xffff0000; break;
                case 1: effect = ConditionEffectIndex.Speedy; color = 0xff00ff00; break;
                case 2: effect = ConditionEffectIndex.Berserk; color = 0xffffd800; break;
                default: effect = ConditionEffectIndex.Healing; color = 0xff00ffff; break;
            }
            this.AOE(eff.Range, true, player => player.ApplyConditionEffect(effect, eff.DurationMS));
            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                TargetObjectId = Id,
                Color = new ARGB(color),
                Pos1 = new Position() { X = eff.Range }
            }, this);
        }

        // Potion of Mini / Scroll Prototype: a Doomed Realms joke item that sets every base stat to 1.
        private void AEMiniPot(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            for (var i = 0; i < 8; i++)
                Stats.Base[i] = 1;
            SendInfo("You feel very small.");
        }

        // Ascendant halos: a healing circle left where it was cast.
        private void AEHalo(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            var halo = new Halo(this, eff.Range, eff.Amount, eff.DurationMS > 0 ? eff.DurationMS : 8000);
            halo.Move(X, Y);
            World.EnterWorld(halo);
        }

        // Tome of Noble Assault: damages every enemy around the player.
        private void AETomeDamage(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            this.AOE(eff.Range, false, enemy =>
            {
                if (enemy is Enemy e)
                    e.Damage(this, ref time, eff.TotalDamage, false);
            });
            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                TargetObjectId = Id,
                Color = new ARGB(0xff00ff00),
                Pos1 = new Position() { X = eff.Range }
            }, this);
        }

        // Prism of Inception: several decoys running off in random directions.
        private void AEMultiDecoy(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            for (var i = 0; i < Math.Max(1, eff.Amount); i++)
            {
                var angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                var decoy = new Decoy(this, eff.DurationMS, angle, new Position(X, Y), 4.5f, 7);
                decoy.Move(X, Y);
                World.EnterWorld(decoy);
            }
        }

        // Samurai cuts: dash through the target (landing half the distance beyond it), damaging and
        // bleeding enemies around the target, then being paralyzed for duration2.
        private void AESamuraiAbility(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            if (MathsUtils.DistSqr(target.X, target.Y, X, Y) > MAX_ABILITY_DIST_SQR)
                return;
            var dest = new Position(target.X + (target.X - X) / 2, target.Y + (target.Y - Y) / 2);
            if (!World.IsPassable(dest.X, dest.Y))
                return;

            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Diffuse,
                Color = new ARGB(0xffff0000),
                TargetObjectId = Id,
                Pos1 = target,
                Pos2 = new Position(target.X + eff.Radius, target.Y)
            }, this);
            World.AOE(target, eff.Radius, false, enemy =>
            {
                if (enemy is Enemy e)
                    e.Damage(this, ref time, eff.TotalDamage, false, false,
                        new ConditionEffect(ConditionEffectIndex.Bleeding, eff.DurationMS > 0 ? eff.DurationMS : 3000));
            });
            TeleportPosition(time, dest, true);
            if (eff.DurationMS2 > 0)
                ApplyConditionEffect(ConditionEffectIndex.Paralyzed, eff.DurationMS2);
        }
    }
}
