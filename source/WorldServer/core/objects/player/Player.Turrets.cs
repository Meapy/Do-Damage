using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Mechanic's turrets (Turret.cs). A blueprint
    // (<Activate objectId duration range amount totalDamage radius allyDamage allyShots allyMs>BuildTower</Activate>)
    // throws a crate to where it was aimed, at most `range` tiles off (UseItem refuses a spot that
    // isn't open floor, before the mana is spent), and the turret stands where it lands
    // TurretThrowMs later. At most `amount` stand at once: one more blows up the oldest.
    // Ferrogrim's Bastion Turret (RelicPower BastionTurret, the Crucible's white bag) is one too,
    // in its forge's colour and with his slam (Turret.Slam, the numbers in
    // Player.CruciblePowers.cs), raised where it lands with the Crucible's look. A BuildTower
    // without allyMs (none now) would be an ally of its own at the player's feet (SpawnAlly).
    public partial class Player
    {
        private const int TurretThrowMs = 350;
        private const float TurretRange = 8;
        private const int MaxTurrets = 3;

        private readonly List<Turret> _turrets = new List<Turret>();

        private static bool IsMechanicTurret(ActivateEffect eff) => eff.AllyMs > 0;

        // where a blueprint aimed at `target` throws its turret
        private Position TurretSpot(Position target, ActivateEffect eff) =>
            ClampAbilityTarget(target, eff.Range > 0 ? eff.Range : TurretRange);

        private void AEBuildTower(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            if (!IsMechanicTurret(eff))
            {
                SpawnAlly(eff.ObjectId, new Position(X, Y), eff.DurationMS > 0 ? eff.DurationMS : 10000);
                return;
            }
            if (eff.ObjectId == null || !GameServer.Resources.GameData.IdToObjectType.TryGetValue(eff.ObjectId, out var type))
            {
                SendError($"{eff.ObjectId} doesn't exist on this server.");
                return;
            }

            var spot = TurretSpot(target, eff);
            var bastion = item.RelicPower == "BastionTurret";
            var color = bastion ? BastionIron : Turret.ColorOf(item);

            // one past the most that may stand: the oldest blows up
            _turrets.RemoveAll(t => t.Gone);
            var max = eff.Amount > 0 ? eff.Amount : MaxTurrets;
            while (_turrets.Count >= max)
            {
                var oldest = _turrets[0];
                _turrets.RemoveAt(0);
                oldest.Detonate(ref time);
            }

            var turret = new Turret(this, type, eff, color);
            if (bastion)
                turret.SetSlam(new Turret.Slam(BastionSlamRadius, BastionSlamDamage, BastionSlamMs, BastionBrokenMs,
                    BastionLook, BastionIron));
            _turrets.Add(turret);

            // the crate flying from the Mechanic to the spot (turret.rs, look 54)
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = spot,
                Pos2 = new Position(0, Turret.LOOK_THROW),
                Color = new ARGB(color),
                Duration = TurretThrowMs,
                SourceId = Id,
            }, this);

            var world = World;
            var z = Z;
            world.StartNewTimer(TurretThrowMs, (w, t) =>
            {
                if (turret.Gone || World != w || Dead)
                {
                    turret.Cancel();
                    return;
                }
                turret.Move(spot.X, spot.Y);
                turret.Z = z;
                w.EnterWorld(turret);
                if (bastion)
                    MortarFx(BastionLook, spot, BastionSlamRadius, 0, color: BastionIron);
            });
        }
    }
}
