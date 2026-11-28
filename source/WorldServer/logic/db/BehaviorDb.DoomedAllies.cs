// Summons spawned by the abilities of the classes ported from Doomed
// Realms, as betterSkillys allies. (The Mechanic's turrets fire by themselves: Turret.cs.)
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ DoomedAllies = () => Behav()
            .Init("Bunny Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Dragon Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(15, 5, shootAngle: 5, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Drake Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 3, shootAngle: 10, projectileIndex: 0, coolDown: 1200), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Oryx Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(15, 5, shootAngle: 5, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Sheep Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Snake Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Wasp Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Wisp Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            .Init("Wolf Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))))
            // the Scroll of the Unwilling's shade (Resurrection.xml): Esben's fans of icicles and,
            // every 2.4 s, his boomerang; it keeps its distance rather than charging in
            .Init("Unwilling Shade Summon", new State(
                new State("attack",
                    new AllyCharge(1, 11, 3.5),
                    new AllyShoot(11, 5, shootAngle: 10, projectileIndex: 0, predictive: 0.3, coolDown: 900),
                    new AllyShoot(9, 1, projectileIndex: 1, predictive: 0.5, coolDown: 2400, coolDownOffset: 1200),
                    new NoEntityWithinAllyTransition(11, false, "follow")),
                new State("follow", new AllyFollow(1.1, 10, 1.5), new EntityWithinAllyTransition(11, "attack"))))
            // the thralls the scroll's power raises (Player.Unwilling.cs): they rush the nearest
            // enemy and slash at it until they shatter
            .Init("Unwilling Thrall", new State(
                new State("attack",
                    new AllyCharge(1.3, 9, 1),
                    new AllyShoot(7, 1, projectileIndex: 0, coolDown: 600),
                    new NoEntityWithinAllyTransition(9, false, "follow")),
                new State("follow", new AllyFollow(1.2, 9, 1.5), new EntityWithinAllyTransition(9, "attack"))));
    }
}
