using WorldServer.logic.behaviors;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Wailing Souls an Empowered boss sends after the players (core/resurrection/Empowered.cs,
        // which sets their health and damage by the boss): they drift at the nearest player,
        // shooting slowing soul bolts, and fade after 14 seconds. The Empowered bosses themselves
        // take their Elder boss's behaviour (BossResurrection.Initialize).
        private _ Resurrection = () => Behav()
            .Init("Wailing Soul",
                new State(
                    new Prioritize(
                        new Follow(1.0, acquireRange: 16, range: 2),
                        new Wander(0.4)),
                    new Shoot(10, count: 3, shootAngle: 16, projectileIndex: 0, predictive: 0.4, coolDown: new Cooldown(1700, 300)),
                    new Decay(14000)));
    }
}
