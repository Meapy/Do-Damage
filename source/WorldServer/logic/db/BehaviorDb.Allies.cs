using Shared.resources;
using System.Net.Http.Headers;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Allies = () => Behav()
        // the Ghostly Prism's bomb: after 2.4 s it bursts for 500 within 6 tiles
        .Init("Spirit Prism Bomb",
            new State(
                new TimedTransition(2400, "explode"),
                new State("0"),
                new State("explode",
                    new AllyAOE(500, 500, 6, 0x8fd8ff),
                    new Suicide()
                    )
                )
            )
        // the Genesis Spell's four portals (2.5 tiles out from the cursor each way): for 4 s each zaps
        // the nearest enemy within 6 tiles for 250 every 0.8 s
        .Init("Genesis Spell Portal 1", GenesisPortal())
        .Init("Genesis Spell Portal 2", GenesisPortal())
        .Init("Genesis Spell Portal 3", GenesisPortal())
        .Init("Genesis Spell Portal 4", GenesisPortal())
        // Oryx's Escutcheon's beams (three, 0.8 s apart): each falls from the sky for 850-950 within
        // 2.25 tiles, stunning for 1.4 s. (The helpers that are GameObjects go with Decay: Suicide is
        // for characters.)
        .Init("Oryx's Escutcheon Effect 1",
            new State(
                new AllyAOE(850, 950, 2.25f, 0xffe066, 1000000, effect: ConditionEffectIndex.Stunned, effectMs: 1400, beam: true),
                new Decay(500)
                )
            )
        // the Chaotic Scripture's Chaos Beam on the cursor: 1800-2200 within 2.5 tiles, ignoring defense
        .Init("Chaotic Scripture Effect",
            new State(
                new TimedTransition(100, "strike"),
                new State("0"),
                new State("strike",
                    new AllyAOE(1800, 2200, 2.5f, 0xb44cff, beam: true),
                    new Suicide()
                    )
                )
            )
        // the Battalion Banner's banner: for 4 s, 400-600 a second within 6 tiles, and Damaging to the
        // players within 3
        .Init("Battalion Banner Effect",
            new State(
                new AllyAOE(400, 600, 6, 0xf5f512, 1000),
                new ConditionAura(3, ConditionEffectIndex.Damaging, 1000),
                new Decay(4000)
                )
            )
        // the Hivemaster Helm's bees: for 6 s they stay by their summoner and sting the nearest enemy
        // within 4 tiles for 120 every 0.6 s; the red one's stings curse half the time
        .Init("EH Ability Bee 1", HiveBee(ConditionEffectIndex.Curse))
        .Init("EH Ability Bee 2", HiveBee(null))
        .Init("EH Ability Bee 3", HiveBee(null))
        // the Marble Seal's eye: for 4.5 s, Armored, Damaging and Healing (2 s, renewed) within 4 tiles
        .Init("Eye of Marble",
            new State(
                new ConditionAura(4, ConditionEffectIndex.Armored, 2000),
                new ConditionAura(4, ConditionEffectIndex.Damaging, 2000),
                new ConditionAura(4, ConditionEffectIndex.Healing, 2000),
                new Decay(4500)
                )
            )
        // the Fool's Prism's (and Trick White Bag's) fake bag goes after 40 s
        .Init("Fake White Bag", new State(new Decay(40000)))
        .Init("Divinity Effect",
            new State(
                new TimedTransition(100, "suicide"),
                new State("0"),
                new State("suicide",
                    new AllyAOE(600, 800, 2.25f, 0xffff00),
                    new Suicide()
                    )
                )
            )
        .Init("Killer Shroom",
            new State(
                new Decay(4800),
                new AllyAOE(1000, 1000, 3, 0xff1919, 1200),
                new State("0",
                    new SetAltTexture(0),
                    new TimedTransition(500, "1")
                    ),
                new State("1",
                    new SetAltTexture(1),
                    new TimedTransition(500, "0")
                    ),
                new State("suicide",
                    new Suicide()
                    )
                )
            )
        .Init("Pirate Ally",
            new State(
                new TimedTransition(5000, "suicide"),
                new State("attack",
                    new AllyCharge(1, 10, 1),
                    new AllyShoot(10, 3, 30, projectileIndex: 0, coolDown: 500),
                    new NoEntityWithinAllyTransition(10, false, "follow")
                    ),
                new State("follow",
                    new AllyFollow(1, 10, 1.5),
                    new EntityWithinAllyTransition(10, "attack")
                    ),
                new State("suicide",
                    new Suicide()
                    )
                )
            )
        .Init("Admin Cat",
            new State(
                new TimedTransition(60000, "suicide"),
                new State("attack",
                    new AllyCharge(1, 10, 1),
                    new AllyShoot(10, 3, 30, projectileIndex: 0, coolDown: 500),
                    new NoEntityWithinAllyTransition(10, false, "follow")
                    ),
                new State("follow",
                    new AllyFollow(1, 10, 1.5),
                    new EntityWithinAllyTransition(10, "attack")
                    ),
                new State("suicide",
                    new Suicide()
                    )
                )
            )
        .Init("CR Friendly Cnidarian",
            new State(
                new TimedTransition(3000, "suicide"),
                new AllyAOE(600, 600, 2, 0xffa447, 1000),
                new State("0",
                    new SetAltTexture(0),
                    new TimedTransition(1000, "1")
                    ),
                new State("1",
                    new SetAltTexture(1),
                    new TimedTransition(1000, "0")
                    ),
                new State("suicide",
                    new Decay(0)
                    )
                )
            )
        .Init("Cranium Effect",
            new State(
                new State("0",
                    new AllyDamage(150, 5, 200),
                    new AllyFollow(1, 10, 1),
                    new AllyLightning(250, 0x9F2B68, 1000),
                    new TimedTransition(5000, "suicide")
                    ),
                new State("suicide",
                    new Suicide()
                    )
                )
            )
        .Init("Gambler's Fate Effect",
            new State(
                new ActAsDecoy(),
                new State("0",
                    new SetAltTexture(0),
                    new TimedTransition(250, "1")
                    ),
                new State("1",
                    new SetAltTexture(1),
                    new TimedTransition(250, "2")
                    ),
                new State("2",
                    new SetAltTexture(2),
                    new TimedTransition(250, "3")
                    ),
                new State("3",
                    new SetAltTexture(3),
                    new TimedRandomTransition(250, false, "0", "4")
                    ),
                new State("4",
                    new SetAltTexture(4),
                    new TimedTransition(250, "5")
                    ),
                new State("5",
                    new SetAltTexture(5),
                    new TimedTransition(250, "6")
                    ),
                new State("6",
                    new SetAltTexture(6),
                    new TimedTransition(250, "7")
                    ),
                new State("7",
                    new SetAltTexture(7),
                     new TimedRandomTransition(250, false, "4", "decay")
                    ),
                new State("decay",
                    new SetAltTexture(4),
                    new Suicide()
                    )
                )
            );

        private static State GenesisPortal() => new State(
            new AllyZap(6, 250, 250, 800, 0xc800ff),
            new Decay(4000)
            );

        private static State HiveBee(ConditionEffectIndex? sting) => new State(
            new AllyFollow(1.4, 10, 1.5),
            new AllyZap(4, 120, 120, 600, 0xffd030, sting, 4000, 0.5),
            new Decay(6000)
            );
    }
}