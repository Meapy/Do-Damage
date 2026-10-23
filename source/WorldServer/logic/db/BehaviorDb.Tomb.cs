#region

using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

#endregion

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Tomb = () => TombOfTheAncients(Behav(), false);
        private _ ElderTomb = () => TombOfTheAncients(Behav(), true);

        // The Tomb of the Ancients and the Elder Tomb share these behaviours. The Elder one uses the
        // Elder version of each monster that has one, attacks a third more often, and adds the
        // Elder drops (Doomed Realms') to the regular loot.
        private static Ctor TombOfTheAncients(Ctor db, bool elder)
        {
            // the monster a behaviour refers to, and the one it defines (none for a monster with no
            // Elder version, whose regular behaviour the Elder Tomb uses as it is)
            string N(string id) => elder && InitGameData.IdToObjectType.ContainsKey("Elder " + id) ? "Elder " + id : id;
            string E(string id) => !elder ? id : N(id) != id ? N(id) : null;
            int Cd(int ms) => elder ? ms * 3 / 4 : ms;
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            return db
            .Init(E("Tomb Defender"),
                new State(
                    new ScaleHP2(20),
                    new State("idle",
                        new Taunt(true, "THIS WILL NOW BE YOUR TOMB!"),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new HpLessTransition(.989, "weakning")
                        ),
                    new State("weakning",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Taunt(true, "Impudence! I am an Immortal, I needn't waste time on you!"),
                        new Shoot(50, 20, projectileIndex: 3, coolDown: Cd(6000)),
                        new State("blue shield 1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 1")
                            ),
                        new State("unset blue shield 1"),
                        new HpLessTransition(.979, "active")
                        ),
                    new State("active",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Shoot(50, 8, 45, 2, 0, 0, coolDown: Cd(1000)),
                        new Shoot(50, 3, 120, 1, 0, 0, coolDown: Cd(5000)),
                        new Shoot(50, 5, 72, 0, 0, 0, coolDown: Cd(5000)),
                        new HpLessTransition(.7, "boomerang")
                        ),
                    new State("boomerang",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "Nut, disable our foes!"),
                        new Shoot(50, 1, projectileIndex: 0, coolDown: Cd(3000)),
                        new Shoot(50, 8, projectileIndex: 2, coolDown: Cd(1000)),
                        new Shoot(50, 3, 15, 1, coolDown: Cd(3000)),
                        new Shoot(50, 2, 90, 1, coolDown: Cd(3000)),
                        new State("blue shield 2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 2")
                            ),
                        new State("unset blue shield 2"),
                        new HpLessTransition(.55, "double shot")

                        ),
                    new State("double shot",
                        new Taunt(true, "Geb, eradicate these cretins from our tomb!"),
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(50, 8, projectileIndex: 2, coolDown: Cd(1000)),
                        new Shoot(50, 2, 10, 0, coolDown: Cd(3000)),
                        new Shoot(50, 4, 15, 1, coolDown: Cd(3000)),
                        new Shoot(50, 2, 90, 1, coolDown: Cd(3000)),
                        new HpLessTransition(.4, "artifacts")
                        ),
                    new State("artifacts",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Taunt(true, "Nut, let them wish they were dead!"),
                        new Prioritize(
                            new Orbit(3, 7, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(50, 8, projectileIndex: 2, coolDown: Cd(1000)),
                        new Shoot(50, 2, 10, 0, coolDown: Cd(3000)),
                        new Shoot(50, 4, 15, 1, coolDown: Cd(3000)),
                        new Shoot(50, 2, 90, 1, coolDown: Cd(3000)),
                        new Spawn(N("Pyramid Artifact 1"), 1, 0),
                        new Spawn(N("Pyramid Artifact 2"), 1, 0),
                        new State("blue shield 3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 3")
                            ),
                        new State("unset blue shield 3"),
                        new HpLessTransition(.25, "artifacts 2")
                        ),
                    new State("artifacts 2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Taunt(true, "My artifacts shall prove my wall of defense is impenetrable!"),
                        new Prioritize(
                            new Orbit(3, 7, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(50, 8, projectileIndex: 2, coolDown: Cd(1000)),
                        new Shoot(50, 3, 10, 0, coolDown: Cd(3000)),
                        new Shoot(50, 5, 15, 1, coolDown: Cd(3000)),
                        new Shoot(50, 2, 80, 1, coolDown: Cd(3000)),
                        new Shoot(50, 2, 90, 1, coolDown: Cd(3000)),
                        new Spawn(N("Pyramid Artifact 3"), 1, 0),
                        new State("blue shield 4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 4")
                            ),
                        new State("unset blue shield 4"),
                        new HpLessTransition(.06, "rage")
                        ),
                    new State("rage",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Taunt(true, "The end of your path is here!"),
                        new Follow(1.5, 10),
                        new Wander(0.3),
                        new Flash(0xfFF0000, 1, 9000001),
                        new Shoot(50, 10, 10, 4, coolDown: Cd(750), coolDownOffset: 750),
                        new Shoot(50, 5, 10, 4, angleOffset: 180, coolDown: Cd(500), coolDownOffset: 500),
                        new Shoot(50, 1, projectileIndex: 0, coolDown: Cd(1000)),
                        new Shoot(50, 3, 15, 1, coolDown: Cd(2000)),
                        new Shoot(50, 2, 90, 1, coolDown: Cd(2000)),
                        new Spawn(N("Pyramid Artifact 1"), 1, 0),
                        new Spawn(N("Pyramid Artifact 2"), 1, 0),
                        new Spawn(N("Pyramid Artifact 3"), 1, 0),
                        new State("blue shield 5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 5")
                            ),
                        new State("unset blue shield 5")
                        )
                    ),
                new SeasonalThreshold("winter", 0.01,
                    new ItemLoot("Frimarra", 0.009)
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 0.5),
                    new ItemLoot("Tomb of the Ancients Key", 0.01, 0, 0.03)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Ring of the Pyramid", 0.009),
                    new ItemLoot("Tome of Holy Protection", 0.009)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Jade Beetle", 0.009),
                    new ItemLoot("Potion of Max Life", 0.01),
                    new ItemLoot("Angel's Hope", 0.01),
                    new ItemLoot("Pyramid Artifact Generator", 0.03),
                    new ItemLoot("Large Egyptian Cloth", 0.5),
                    new ItemLoot("Small Egyptian Cloth", 0.5)
                    )
                )
            .Init(E("Tomb Support"),
                new State(
                    new ScaleHP2(20),
                    new State("idle",
                        new Taunt(true, "ENOUGH OF YOUR VANDALISM!"),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Orbit(4, 5, target: N("Tomb Boss Anchor"), radiusVariance: 0.5),
                        new HpLessTransition(.9875, "weakning")
                        ),
                    new State("weakning",
                        new Prioritize(
                            new Orbit(4, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt("Impudence! I am an immortal, I needn't take your seriously."),
                        new Shoot(50, 20, projectileIndex: 7, coolDown: Cd(6000), coolDownOffset: 2000),
                        new State("blue shield 1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 1")
                            ),
                        new State("unset blue shield 1"),
                        new HpLessTransition(.97875, "active")
                        ),
                    new State("active",
                        new Prioritize(
                            new Orbit(4, 4, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(20, 1, projectileIndex: 5, coolDown: Cd(1000)),
                        new Shoot(12, 3, 120, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 4, 90, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 5, 72, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 6, 60, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new State("blue shield 2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 2")
                            ),
                        new State("unset blue shield 2"),
                        new HpLessTransition(.9, "boomerang")
                        ),
                    new State("boomerang",
                        new Prioritize(
                            new Orbit(4, 6, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "Bes, protect me at once!"),
                        new Shoot(20, 1, projectileIndex: 5, coolDown: Cd(1000)),
                        new Shoot(20, 1, projectileIndex: 6, coolDown: Cd(3000)),
                        new Shoot(12, 3, 120, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 4, 90, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 5, 72, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 6, 60, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new HpLessTransition(.7, "paralyze")
                        ),
                    new State("paralyze",
                        new Prioritize(
                            new Orbit(4, 7, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "Geb, eradicate these cretins from our tomb!"),
                        new Shoot(20, 1, projectileIndex: 5, coolDown: Cd(1000)),
                        new Shoot(20, 1, projectileIndex: 6, coolDown: Cd(3000)),
                        new Shoot(999, 2, 10, 8, 0, 180, coolDown: Cd(1000)),
                        new Shoot(12, 3, 120, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 4, 90, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 5, 72, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 6, 60, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new HpLessTransition(.5, "artifacts")
                        ),
                    new State("artifacts",
                        new Prioritize(
                            new Orbit(4, 4, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "My artifacts shall make your lethargic lives end much more swiftly!"),
                        new Shoot(20, 1, projectileIndex: 5, coolDown: Cd(1000)),
                        new Shoot(20, 1, projectileIndex: 6, coolDown: Cd(3000)),
                        new Shoot(12, 3, 120, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 4, 90, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 5, 72, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 6, 60, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new Spawn(N("Sphinx Artifact 1"), 1, 0),
                        new Spawn(N("Sphinx Artifact 2"), 1, 0),
                        new State("blue shield 3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 3")
                            ),
                        new State("unset blue shield 3"),
                        new HpLessTransition(.3, "double shoot")
                        ),
                    new State("double shoot",
                        new Prioritize(
                            new Orbit(4, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(20, 2, 15, 5, coolDown: Cd(1000)),
                        new Shoot(20, 2, 15, 6, coolDown: Cd(3000)),
                        new Shoot(12, 3, 120, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 4, 90, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 5, 72, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 6, 60, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new Spawn(N("Sphinx Artifact 3"), 1, 0),
                        new State("blue shield 4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 4")
                            ),
                        new State("unset blue shield 4"),
                        new HpLessTransition(.06, "rage")
                        ),
                    new State("rage",
                        new Taunt(true, "This cannot be! You shall not succeed!"),
                        new Follow(1.5, 15),
                        new Wander(0.3),
                        new Flash(0xfFF0000, 1, 9000001),
                        new Shoot(20, 1, projectileIndex: 5, coolDown: Cd(1000)),
                        new Shoot(20, 1, 15, 0, coolDown: Cd(750)),
                        new Shoot(12, 4, 90, 1, 0, 0, coolDown: Cd(2500), coolDownOffset: 1000),
                        new Shoot(12, 5, 72, 2, 0, 0, coolDown: Cd(2500), coolDownOffset: 1500),
                        new Shoot(12, 6, 60, 3, 0, 0, coolDown: Cd(2500), coolDownOffset: 2000),
                        new Shoot(12, 8, 45, 4, 0, 0, coolDown: Cd(2500), coolDownOffset: 2500),
                        new Shoot(999, 6, 10, 8, angleOffset: 180, coolDown: Cd(500)),
                        new Spawn(N("Sphinx Artifact 1"), 1, 0),
                        new State("blue shield 5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 5")
                            ),
                        new State("unset blue shield 5")
                        )
                    ),
                new SeasonalThreshold("winter", 0.01,
                    new ItemLoot("Enchanted Ice Shard", 0.009)
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Tomb of the Ancients Key", 0.01, 0, 0.03),
                    new ItemLoot("Ring of the Sphinx", 0.009)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Jade Beetle", 0.009),
                    new ItemLoot("Wine Cellar Incantation", 0.01),
                    new ItemLoot("Potion of Max Life", 0.01),
                    new ItemLoot("Wings of Natural Bloodlust", 0.01),
                    new ItemLoot("Large Egyptian Cloth", 0.5),
                    new ItemLoot("Small Egyptian Cloth", 0.5)
                    )
                )
            .Init(E("Tomb Attacker"),
                new State(
                    new ScaleHP2(20),
                    new State("idle",
                        new Taunt(true, "ENOUGH OF YOUR VANDALISM!"),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new HpLessTransition(.988, "weakning")
                        ),
                    new State("weakning",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(50, 20, projectileIndex: 3, coolDown: Cd(6000), coolDownOffset: 2000),
                        new State("blue shield 1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 1")
                            ),
                        new State("unset blue shield 1"),
                        new HpLessTransition(.9788, "active")
                        ),
                    new State("active",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Shoot(14, 2, 10, 2, coolDown: Cd(500)),
                        new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(2000)),
                        new State("Grenade 1",
                            new Grenade(3, 160, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 2")
                            ),
                        new State("Grenade 2",
                            new Grenade(4, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 1")
                            ),
                        new HpLessTransition(.72, "lets dance")
                        ),
                    new State("lets dance",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "Bes, protect me at once!"),
                        new Shoot(14, 1, projectileIndex: 2, coolDown: Cd(500)),
                        new Shoot(14, 2, 90, 2, coolDown: Cd(1000)),
                        new Shoot(14, 2, 90, 2, angleOffset: 270, coolDown: Cd(1000)),
                        new Shoot(11 + 1 / 5, 8, 45, 1, 0, coolDown: Cd(5000)),
                        new Shoot(12, 2, 45, 0, coolDown: Cd(1500)),
                        new Shoot(99, 1, projectileIndex: 4, coolDown: Cd(500)),
                        new Spawn(N("Scarab"), 3, 0, coolDown: Cd(10000)),
                        new State("blue shield 2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 2")
                            ),
                        new State("unset blue shield 2",
                            new TimedTransition(3000, "Grenade 3")
                            ),
                        new State("Grenade 3",
                            new Grenade(3, 160, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 4")
                            ),
                        new State("Grenade 4",
                            new Grenade(4, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 3")
                            ),
                        new HpLessTransition(.675, "more muthafucka")
                        ),
                    new State("more muthafucka",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "Nut, disable our foes!"),
                        new Spawn(N("Scarab"), 3, 0, coolDown: Cd(10000)),
                        new Shoot(14, 2, 10, 2, coolDown: Cd(500)),
                        new Shoot(14, 1, projectileIndex: 2, coolDown: Cd(500)),
                        new Shoot(14, 2, 90, 2, coolDown: Cd(1000)),
                        new Shoot(14, 2, 90, 2, angleOffset: 270, coolDown: Cd(1000)),
                        new Shoot(11 + 1 / 5, 10, 36, 1, 0, coolDown: Cd(5000)),
                        new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(2000)),
                        new Shoot(12, 2, 45, 0, coolDown: Cd(2000)),
                        new Shoot(99, 1, projectileIndex: 4, coolDown: Cd(500)),
                        new Shoot(99, 1, projectileIndex: 4, angleOffset: 90, coolDown: Cd(500)),
                        new State("Grenade 5",
                            new Grenade(3, 160, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 6")
                            ),
                        new State("Grenade 6",
                            new Grenade(4, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 5")
                            ),
                        new HpLessTransition(.4, "artifacts")
                        ),
                    new State("artifacts",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "My artifacts shall destroy you from your soul your flesh!"),
                        new Spawn(N("Scarab"), 3, 0, coolDown: Cd(10000)),
                        new Shoot(14, 2, 10, 2, coolDown: Cd(500)),
                        new Shoot(14, 1, projectileIndex: 2, coolDown: Cd(500)),
                        new Shoot(14, 2, 90, 2, coolDown: Cd(1000)),
                        new Shoot(14, 2, 90, 2, angleOffset: 270, coolDown: Cd(1000)),
                        new Shoot(11 + 1 / 5, 10, 36, 1, 0, coolDown: Cd(5000)),
                        new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(2000)),
                        new Shoot(12, 2, 45, 0, coolDown: Cd(2000)),
                        new Shoot(99, 1, projectileIndex: 4, coolDown: Cd(500)),
                        new Shoot(99, 1, projectileIndex: 4, angleOffset: 90, coolDown: Cd(500)),
                        new Spawn(N("Nile Artifact 1"), 1, 0),
                        new Spawn(N("Nile Artifact 2"), 1, 0),
                        new State("blue shield 3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 3")
                            ),
                        new State("unset blue shield 3",
                            new TimedTransition(3000, "Grenade 7")
                            ),
                        new State("Grenade 7",
                            new Grenade(5, 45, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 8")
                            ),
                        new State("Grenade 8",
                            new Grenade(4, 100, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 9")
                            ),
                        new State("Grenade 9",
                            new Grenade(3, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 7")
                            ),
                        new HpLessTransition(.2, "artifacts 2")
                        ),
                    new State("artifacts 2",
                        new Prioritize(
                            new Orbit(3, 5, 10, N("Tomb Boss Anchor"), speedVariance: .2, radiusVariance: .5)),
                        new Taunt(true, "My artifacts shall destroy you from your soul your flesh!"),
                        new Spawn(N("Scarab"), 3, 0, coolDown: Cd(10000)),
                        new Shoot(14, 2, 10, 2, coolDown: Cd(500)),
                        new Shoot(14, 1, projectileIndex: 2, coolDown: Cd(500)),
                        new Shoot(14, 2, 90, 2, coolDown: Cd(1000)),
                        new Shoot(14, 2, 90, 2, angleOffset: 270, coolDown: Cd(1000)),
                        new Shoot(11 + 1 / 5, 10, 36, 1, 0, coolDown: Cd(5000)),
                        new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(2000)),
                        new Shoot(12, 2, 45, 0, coolDown: Cd(2000)),
                        new Shoot(99, 1, projectileIndex: 4, coolDown: Cd(500)),
                        new Shoot(99, 1, projectileIndex: 4, angleOffset: 90, coolDown: Cd(500)),
                        new Spawn(N("Nile Artifact 3"), 1, 0),
                        new State("blue shield 4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 4")
                            ),
                        new State("unset blue shield 4",
                            new TimedTransition(3000, "Grenade 10")
                            ),
                        new State("Grenade 10",
                            new Grenade(5, 45, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 11")
                            ),
                        new State("Grenade 11",
                            new Grenade(4, 100, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 12")
                            ),
                        new State("Grenade 12",
                            new Grenade(3, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 10")
                            ),
                        new HpLessTransition(.06, "rage")
                        ),
                    new State("rage",
                        new Taunt(true, "This cannot be! You shall not succeed!"),
                        new Flash(0xfFF0000, 1, 9000001),
                        new StayBack(1, 6),
                        new Shoot(11 + 1 / 5, 10, 36, 1, 0, coolDown: Cd(5000)),
                        new Shoot(14, 2, 10, 2, coolDown: Cd(500)),
                        new Shoot(14, 1, projectileIndex: 2, coolDown: Cd(500)),
                        new Shoot(14, 2, 90, 2, coolDown: Cd(1000)),
                        new Shoot(14, 2, 90, 2, angleOffset: 270, coolDown: Cd(1000)),
                        new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(2000)),
                        new Shoot(12, 2, 45, 0, coolDown: Cd(2000)),
                        new Spawn(N("Scarab"), 3, 0, coolDown: Cd(10000)),
                        new Spawn(N("Nile Artifact 1"), 1, 0),
                        new State("blue shield 5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new TimedTransition(3000, "unset blue shield 5")
                            ),
                        new State("unset blue shield 5",
                            new TimedTransition(3000, "Grenade 13")
                            ),
                        new State("Grenade 13",
                            new Grenade(3, 150, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 14")
                            ),
                        new State("Grenade 14",
                            new Grenade(4, 120, 10, coolDown: Cd(1500)),
                            new TimedTransition(1500, "Grenade 13")
                            )
                        )
                    ),
                new SeasonalThreshold("winter", 0.01,
                    new ItemLoot("Ring of the Northern Light", 0.009)
                    ),
                new Threshold(0.01,
                    new ItemLoot("Tomb of the Ancients Key", 0.01, 0, 0.03),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Ring of the Nile", 0.009)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Jade Beetle", 0.009),
                    new ItemLoot("Wine Cellar Incantation", 0.01),
                    new ItemLoot("Potion of Max Life", 0.01),
                    new ItemLoot("Raw Hope", 0.01),
                    new ItemLoot("Large Egyptian Cloth", 0.01),
                    new ItemLoot("Small Egyptian Cloth", 0.01)
                    )
                )
            //Minions
            .Init(E("Pyramid Artifact 1"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Defender"), radiusVariance: 0.5)
                        ),
                    new Follow(0.85, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(3, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Defender"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Pyramid Artifact 2"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Defender"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(3, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Defender"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Pyramid Artifact 3"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Defender"), radiusVariance: 0.5)
                        ),
                    new Follow(0.85, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(3, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Defender"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Sphinx Artifact 1"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Support"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Support"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Sphinx Artifact 2"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Support"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Support"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Sphinx Artifact 3"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Support"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Support"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Nile Artifact 1"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Attacker"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Attacker"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Nile Artifact 2"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Attacker"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Attacker"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            .Init(E("Nile Artifact 3"),
                new State(
                    new Prioritize(
                        new Orbit(3, 2, target: N("Tomb Attacker"), radiusVariance: 0.5)
                        ),
                    new Follow(2, range: 1, duration: 5000, coolDown: Cd(0)),
                    new Shoot(12, 3, 120, coolDown: Cd(2500)),
                    new EntityNotExistsTransition(N("Tomb Attacker"), 50, "die"),
                    new State("alive"),
                    new State("die",
                        new Suicide()
                        )
                    )
                )
            // the statues wake once every sarcophagus in the tomb has been destroyed
            .Init(E("Tomb Defender Statue"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Init",
                        new TimedTransition(2000, "Dormant")
                        ),
                    new State("Dormant",
                        new EntitiesNotExistsTransition(1000, "ItsGoTime", N("Inactive Sarcophagus"), N("Active Sarcophagus"))
                        ),
                    new State("ItsGoTime",
                        new Transform(N("Tomb Defender"))
                        )
                    ))
            // the statues wake once every sarcophagus in the tomb has been destroyed
            .Init(E("Tomb Support Statue"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Init",
                        new TimedTransition(2000, "Dormant")
                        ),
                    new State("Dormant",
                        new EntitiesNotExistsTransition(1000, "ItsGoTime", N("Inactive Sarcophagus"), N("Active Sarcophagus"))
                        ),
                    new State("ItsGoTime",
                        new Transform(N("Tomb Support"))
                        )
                    ))
            // the statues wake once every sarcophagus in the tomb has been destroyed
            .Init(E("Tomb Attacker Statue"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Init",
                        new TimedTransition(2000, "Dormant")
                        ),
                    new State("Dormant",
                        new EntitiesNotExistsTransition(1000, "ItsGoTime", N("Inactive Sarcophagus"), N("Active Sarcophagus"))
                        ),
                    new State("ItsGoTime",
                        new Transform(N("Tomb Attacker"))
                        )
                    ))
            // each sarcophagus opens once its own priests (within 15 tiles; the nearest other
            // sarcophagus is over 28 tiles away) are dead
            .Init(E("Inactive Sarcophagus"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Init",
                        new TimedTransition(2000, "Guarded")
                        ),
                    new State("Guarded",
                        new EntitiesNotExistsTransition(15, "activate", N("Beam Priest"), N("Beam Priestess"))
                        ),
                    new State("activate",
                        new Transform(N("Active Sarcophagus"))
                        )
                    ))
            .Init(E("Scarab"),
                new State(
                    new NoPlayerWithinTransition(7, "Idle"),
                    new PlayerWithinTransition(7, "Chase"),
                    new State("Idle",
                        new Wander(.1)
                        ),
                    new State("Chase",
                        new Follow(1.5, 7, 0),
                        new Shoot(3, projectileIndex: 1, coolDown: Cd(500))
                        )
                    )
                )
            .Init(E("Active Sarcophagus"),
                new State(
                    // three bursts as it opens, then it waits to be broken
                    new State("burst 1",
                        new Flash(0xFFD700, 0.25, 4),
                        new Shoot(50, 8, 10, 0, coolDown: Cd(9999999)),
                        new TimedTransition(500, "burst 2")
                        ),
                    new State("burst 2",
                        new Shoot(50, 8, 10, 0, coolDown: Cd(9999999)),
                        new TimedTransition(500, "burst 3")
                        ),
                    new State("burst 3",
                        new Shoot(50, 8, 10, 0, coolDown: Cd(9999999)),
                        new TimedTransition(500, "idle")
                        ),
                    new State("idle")
                    ),
                new ItemLoot("Magic Potion", 0.002),
                new ItemLoot("Health Potion", 0.15),
                new ItemLoot("Mask of Anubis", 0.01),
                new Threshold(0.32,
                    new ItemLoot("Tincture of Mana", 0.15),
                    new ItemLoot("Tincture of Dexterity", 0.15),
                    new ItemLoot("Tincture of Life", 0.15)
                    ),
                ElderLoot(0,
                    new ItemLoot("Elder Tomb Key", 0.09),
                    new ItemLoot("Magic Potion", 0.15)
                    )
                )
            .Init(E("Eagle Sentry"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.7, 7, 0),
                        new Shoot(25, 12, projectileIndex: 1, coolDown: Cd(3000))
                        )
                    )
                )

            .Init(E("Bloated Mummy"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.6, 7, 0),
                        new Reproduce(N("Scarab"), 10, 3, 3000),
                        new Shoot(25, 22, projectileIndex: 0, coolDown: Cd(2250))
                        )
                    )
                )

            .Init(E("Lion Archer"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.4, 7, 0),
                        new Shoot(25, 3, projectileIndex: 1, coolDown: Cd(1250)),
                        new Shoot(25, 1, projectileIndex: 3, fixedAngle: 0, coolDown: Cd(6000)),
                        new Shoot(25, 1, projectileIndex: 3, fixedAngle: 90, coolDown: Cd(6000)),
                        new Shoot(25, 1, projectileIndex: 3, fixedAngle: 180, coolDown: Cd(6000)),
                        new Shoot(25, 1, projectileIndex: 3, fixedAngle: 270, coolDown: Cd(6000))
                        )
                    )
                )

            .Init(E("Jackal Warrior"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.9, 7, 0),

                        new Shoot(25, 1, shootAngle: 25, projectileIndex: 0, coolDown: Cd(1250))
                        )
                    )
                )

            .Init(E("Jackal Assassin"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.9, 7, 0),

                        new Shoot(25, 1, shootAngle: 25, projectileIndex: 0, coolDown: Cd(1250))
                        )
                    )
                )

            .Init(E("Jackal Veteran"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.9, 7, 0),

                        new Shoot(25, 1, shootAngle: 25, projectileIndex: 0, coolDown: Cd(1250))
                        )
                    )
                )

            .Init(E("Jackal Lord"),
                new State(
                    new NoPlayerWithinTransition(12, "Idle"),
                    new PlayerWithinTransition(12, "Chase"),
                    new State("Idle",
                        new Wander(.03)
                        ),
                    new State("Chase",
                        new Follow(.9, 7, 0),
                        new Reproduce(N("Jackal Warrior"), 10, 2, 10000),
                        new Reproduce(N("Jackal Veteran"), 10, 1, 10000),
                        new Reproduce(N("Jackal Assassin"), 10, 1, 10000),
                        new Shoot(25, 4, shootAngle: 25, projectileIndex: 0, coolDown: Cd(1250))
                        )
                    )
                )

            .Init(E("Beam Priest"),
                new State(
                    new State("weakning",
                        new Orbit(.4, 6, target: N("Inactive Sarcophagus"), radiusVariance: 0.5),
                        new Shoot(50, 3, projectileIndex: 1, coolDown: Cd(3500)),
                        new Shoot(50, 6, projectileIndex: 0, coolDown: Cd(7210))

                        )
                    )
                )
            .Init(E("Tomb Thunder Turret"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2500, "Spin")
                        ),
                    new State("Spin",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2000, "Pause"),
                        new State("Quadforce1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 0, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce2")
                            ),
                        new State("Quadforce2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 15, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce3")
                            ),
                        new State("Quadforce3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 30, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce4")
                            ),
                        new State("Quadforce4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 45, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce5")
                            ),
                        new State("Quadforce5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 60, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce6")
                            ),
                        new State("Quadforce6",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 75, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce7")
                            ),
                        new State("Quadforce7",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 90, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce8")
                            ),
                        new State("Quadforce8",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 105, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce1")
                            )
                        ),
                    new State("Pause",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(5000, "Spin")
                        )
                    )
                )

            .Init(E("Tomb Fire Turret"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2500, "Spin")
                        ),
                    new State("Spin",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2000, "Pause"),
                        new State("Quadforce1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 0, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce2")
                            ),
                        new State("Quadforce2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 15, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce3")
                            ),
                        new State("Quadforce3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 30, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce4")
                            ),
                        new State("Quadforce4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 45, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce5")
                            ),
                        new State("Quadforce5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 60, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce6")
                            ),
                        new State("Quadforce6",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 75, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce7")
                            ),
                        new State("Quadforce7",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 90, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce8")
                            ),
                        new State("Quadforce8",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 105, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce1")
                            )
                        ),
                    new State("Pause",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(5000, "Spin")
                        )
                    )
                )

            .Init(E("Tomb Frost Turret"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2500, "Spin")
                        ),
                    new State("Spin",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(2000, "Pause"),
                        new State("Quadforce1",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 0, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce2")
                            ),
                        new State("Quadforce2",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 15, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce3")
                            ),
                        new State("Quadforce3",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 30, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce4")
                            ),
                        new State("Quadforce4",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 45, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce5")
                            ),
                        new State("Quadforce5",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 60, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce6")
                            ),
                        new State("Quadforce6",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 75, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce7")
                            ),
                        new State("Quadforce7",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 90, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce8")
                            ),
                        new State("Quadforce8",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                            new Shoot(0, projectileIndex: 0, count: 5, shootAngle: 60, fixedAngle: 105, coolDown: Cd(300)),
                            new TimedTransition(150, "Quadforce1")
                            )
                        ),
                    new State("Pause",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new TimedTransition(5000, "Spin")
                        )
                    )
                )

            .Init(E("Beam Priestess"),
                new State(
                    new State("weakning",
                        new Prioritize(
                            new Orbit(.6, 9, target: N("Inactive Sarcophagus"), radiusVariance: 0.5)
                            ),
                        new Shoot(50, 6, projectileIndex: 1, coolDown: Cd(3500)),
                        new Shoot(50, 2, projectileIndex: 0, coolDown: Cd(7210))

                        )
                    )
                )
            // the bosses orbit this anchor; it drops the exit portal once they are all dead
            .Init(E("Tomb Boss Anchor"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new DropPortalOnDeath("Realm Portal", 100),
                    new DropPortalOnDeath(elder ? "Realm Portal" : "Elder Tomb Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new State("Init",
                        new TimedTransition(2000, "Idle")
                        ),
                    new State("Idle",
                        new EntitiesNotExistsTransition(300, "Death", N("Tomb Support"), N("Tomb Attacker"), N("Tomb Defender"),
                            N("Active Sarcophagus"), N("Tomb Defender Statue"), N("Tomb Support Statue"), N("Tomb Attacker Statue"))
                        ),
                    new State("Death",
                        new Suicide()
                        )
                    )
            );
        }
    }
}
