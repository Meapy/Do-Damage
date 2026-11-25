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
            .Init("Wolf Summon", new State(new State("attack", new AllyCharge(1, 10, 1), new AllyShoot(10, 1, projectileIndex: 0, coolDown: 1000), new NoEntityWithinAllyTransition(10, false, "follow")), new State("follow", new AllyFollow(1, 10, 1.5), new EntityWithinAllyTransition(10, "attack"))));
    }
}
