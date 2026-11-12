using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// The raid bosses' and the Ocean Trench's own Empowered phases: two each, one below 70% health
    /// and one below 40%, on top of the phases every Empowered boss has (<see cref="EmpoweredPhases"/>).
    /// Their shots are the boss's signature shots (Arena.Sig0 on, SIGNATURE_SHOTS in
    /// tools/build_resurrection.py), their damage the soul bolt's times a factor.
    /// </summary>
    public static class EmpoweredSignatures
    {
        public static IEnumerable<SoulPhase> For(string boss) => boss switch
        {
            "Elder shtrs Bridge Sentinel" => BridgeSentinel(),
            "Elder shtrs Twilight Archmage" => TwilightArchmage(),
            "Elder shtrs The Forgotten King" => ForgottenKing(),
            "Elder LH Marble Colossus" => MarbleColossus(),
            "Elder LH Malus 2" => Malus(),
            "Elder LH Void Entity" => VoidEntity(),
            "Elder Tomb Defender" => Bes(),
            "Elder Tomb Support" => Nut(),
            "Elder Tomb Attacker" => Geb(),
            "Elder NM Green Dragon God" => Limoz(),
            "Elder NM Blue Dragon God" => Nikao(),
            "Elder NM Red Dragon God" => Pyyr(),
            "Elder NM Black Dragon God" => Feargus(),
            "Elder lod Ivory Wyvern" => IvoryWyvern(),
            "Elder Thessal the Mermaid Goddess" => Thessal(),
            _ => Array.Empty<SoulPhase>(),
        };

        /// <summary>The bosses with signature phases (for the tests and the wiki).</summary>
        public static readonly string[] Bosses =
        {
            "Elder shtrs Bridge Sentinel", "Elder shtrs Twilight Archmage", "Elder shtrs The Forgotten King",
            "Elder LH Marble Colossus", "Elder LH Malus 2", "Elder LH Void Entity",
            "Elder Tomb Defender", "Elder Tomb Support", "Elder Tomb Attacker",
            "Elder NM Green Dragon God", "Elder NM Blue Dragon God", "Elder NM Red Dragon God", "Elder NM Black Dragon God",
            "Elder lod Ivory Wyvern", "Elder Thessal the Mermaid Goddess",
        };

        private const double High = 0.7, Low = 0.4;

        // --- The Elder Shatters ---------------------------------------------------------------

        private const uint ShattersWhite = 0xFFE8F0FF, ShattersBlue = 0xFF5B8CFF, TwilightViolet = 0xFFB36BFF, RoyalGold = 0xFFFFD34F;

        private static SoulPhase[] BridgeSentinel() => new[]
        {
            // the bridge gives way under the players, half its stones and then the other half
            new SoulPhase
            {
                Name = "Collapsing Bridge", Below = High, Color = ShattersWhite,
                Line = "This bridge was my charge. Now it is your grave!",
                Every = 10500, Busy = 3600,
                Run = a =>
                {
                    const double cell = 2.6;
                    a.Effect(EffectType.Earthquake, ShattersWhite);
                    for (var half = 0; half < 2; half++)
                        for (var gx = -3; gx <= 3; gx++)
                            for (var gy = -3; gy <= 3; gy++)
                            {
                                if (((gx + gy) & 1) != half || gx * gx + gy * gy > 11)
                                    continue;
                                var at = new Position(a.Boss.X + (float)(gx * cell), a.Boss.Y + (float)(gy * cell));
                                if (!a.Walkable(at))
                                    continue;
                                var h = half;
                                a.Later(h * 1700, () => a.Blast(at, cell * 0.62, 1500, 1.3, ShattersWhite));
                            }
                },
            },
            // two beams of shuriken sweep round it, white bullets pulsing out between them
            new SoulPhase
            {
                Name = "Sentinel's Gaze", Below = Low, Color = ShattersBlue,
                Line = "Nothing crosses. NOTHING.",
                Every = 12000, Busy = 4400,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    var dir = a.Rng.Next(2) == 0 ? 1 : -1;
                    a.Gather(ShattersBlue, 4);
                    a.Repeat(22, 180, i =>
                    {
                        a.Shoot(Arena.Sig0, start + dir * Arena.Deg(9 * i), 2, Math.PI);
                        if (i % 5 == 0)
                            a.Ring(Arena.Sig1, 8, start + i);
                    }, 500);
                },
            },
        };

        private static SoulPhase[] TwilightArchmage() => new[]
        {
            // four nexuses flare round the Archmage and burst into shuriken, then four more between them
            new SoulPhase
            {
                Name = "Twilight Nexus", Below = High, Color = TwilightViolet,
                Line = "Dusk and dawn, bend to my will!",
                Every = 10000, Busy = 3400,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    for (var wave = 0; wave < 2; wave++)
                    {
                        var w = wave;
                        a.Later(w * 1500, () =>
                        {
                            for (var k = 0; k < 4; k++)
                            {
                                var at = a.At(start + (k + w * 0.5) * Math.PI / 2, 6);
                                var color = w == 0 ? TwilightViolet : ShattersBlue;
                                a.Lightning(at, color);
                                a.Blast(at, 1.6, 1200, 1.4, color, landed: () => a.RingFrom(at, Arena.Sig0, 8, w * Math.PI / 8));
                            }
                        });
                    }
                },
            },
            // a cage of runes round every player, then the heart of the cage bursts: get out between the runes
            new SoulPhase
            {
                Name = "Arcane Prison", Below = Low, Color = ShattersBlue,
                Line = "Stay. Right. There.",
                Every = 12000, Busy = 3600,
                Run = a =>
                {
                    foreach (var p in a.Targets())
                    {
                        var at = Arena.P(p);
                        var off = a.RandomAngle();
                        for (var k = 0; k < 8; k++)
                            a.Blast(Arena.At(at, off + k * Math.PI / 4, 3), 1.0, 1300, 1.2, TwilightViolet, ConditionEffectIndex.Confused, 1200);
                        a.Later(1300, () => a.Blast(at, 2.4, 1300, 2.0, ShattersBlue, ConditionEffectIndex.Quiet, 1500,
                            landed: () => a.ShootFrom(at, Arena.Sig1, Arena.AngleBetween(at, a.Pos) + Math.PI, 3, Arena.Deg(20))));
                    }
                },
            },
        };

        private static SoulPhase[] ForgottenKing() => new[]
        {
            // the King's decree falls on every player and shatters into a crown of fire
            new SoulPhase
            {
                Name = "Royal Decree", Below = High, Color = RoyalGold,
                Line = "By royal decree: DIE.",
                Every = 9500, Busy = 2800,
                Run = a =>
                {
                    a.Wave(3, RoyalGold);
                    foreach (var p in a.Targets())
                    {
                        var at = Arena.P(p);
                        a.Throw(at, RoyalGold);
                        a.Blast(at, 2.4, 1800, 1.6, RoyalGold, ConditionEffectIndex.Quiet, 1500,
                            landed: () => a.RingFrom(at, Arena.Sig1, 6, a.RandomAngle()));
                    }
                },
            },
            // four pillars of the forgotten court rise round him and fire at whoever is nearest
            new SoulPhase
            {
                Name = "The Last Court", Below = Low, Color = ShattersWhite,
                Line = "My court will see you judged!",
                Every = 13000, Busy = 5200,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    var pillars = Enumerable.Range(0, 4).Select(k => a.At(start + k * Math.PI / 2, 7.5)).ToArray();
                    foreach (var at in pillars)
                    {
                        a.Mark(at, 1.3, 4800, RoyalGold, 1);
                        a.Lightning(at, RoyalGold);
                    }
                    a.Repeat(11, 420, i =>
                    {
                        foreach (var at in pillars)
                        {
                            var target = a.Targets().OrderBy(p => Math.Abs(p.X - at.X) + Math.Abs(p.Y - at.Y)).FirstOrDefault();
                            if (target != null)
                                a.ShootFrom(at, Arena.Sig0, Arena.AngleBetween(at, Arena.P(target)), 3, Arena.Deg(14));
                        }
                    }, 600);
                },
            },
        };

        // --- The Elder Lost Halls, Cultist Hideout and Void -----------------------------------

        private const uint Marble = 0xFFF2EEE6, MarbleGold = 0xFFE9C46A, CultRed = 0xFFE0283C, CultFire = 0xFFFF7A1A, VoidPurple = 0xFF6A2BD9, VoidDeep = 0xFF2A0B5E;

        private static SoulPhase[] MarbleColossus() => new[]
        {
            // the halls split open in a cross, then in an X, the crack racing outwards
            new SoulPhase
            {
                Name = "Earthsplitter", Below = High, Color = Marble,
                Line = "The halls themselves obey me!",
                Every = 10000, Busy = 3800,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    a.Effect(EffectType.Earthquake, Marble);
                    for (var turn = 0; turn < 2; turn++)
                    {
                        var t = turn;
                        a.Later(t * 1800, () =>
                        {
                            for (var arm = 0; arm < 4; arm++)
                            {
                                var angle = start + arm * Math.PI / 2 + t * Math.PI / 4;
                                for (var step = 0; step < 7; step++)
                                {
                                    var at = a.At(angle, 2 + step * 1.7);
                                    var last = step == 6;
                                    a.Blast(at, 1.15, 1100 + step * 140, 1.3, last ? MarbleGold : Marble,
                                        landed: last ? () => a.ShootFrom(at, Arena.Sig0, angle, 5, Arena.Deg(25)) : null);
                                }
                            }
                        });
                    }
                },
            },
            // its wheels spiral out in four arms, turning back halfway; don't stand at its feet
            new SoulPhase
            {
                Name = "Colossal Wheel", Below = Low, Color = MarbleGold,
                Line = "Turn, turn, and grind them to dust!",
                Every = 12000, Busy = 4800,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    a.Blast(a.Pos, 3.2, 1200, 2.0, MarbleGold, ConditionEffectIndex.Stunned, 800);
                    a.Repeat(24, 170, i =>
                    {
                        var turn = i < 12 ? i : 24 - i;
                        a.Shoot(Arena.Sig1, start + Arena.Deg(11 * turn), 4, Math.PI / 2);
                    }, 1200);
                },
            },
        };

        private static SoulPhase[] Malus()
        {
            var ritual = new List<Enemy>();
            return new[]
            {
                // a pentagram of sigils lit by lightning, each loosing a bolt at the nearest player
                new SoulPhase
                {
                    Name = "Cult Sigils", Below = High, Color = CultRed,
                    Line = "The circle is drawn. The blood will flow.",
                    Every = 10500, Busy = 3000,
                    Run = a =>
                    {
                        var start = a.RandomAngle();
                        for (var k = 0; k < 5; k++)
                        {
                            var at = a.At(start + k * Math.PI * 2 / 5, 7);
                            // the star's lines: each sigil to the one two along
                            var next = a.At(start + (k + 2) * Math.PI * 2 / 5, 7);
                            a.Lightning(at, CultRed);
                            for (var s = 1; s < 4; s++)
                                a.Blast(new Position(at.X + (next.X - at.X) * s / 4, at.Y + (next.Y - at.Y) * s / 4), 0.9, 1700, 1.0, CultRed);
                            a.Blast(at, 1.6, 1400, 1.5, CultRed, landed: () =>
                            {
                                var target = a.Targets().OrderBy(p => Math.Abs(p.X - at.X) + Math.Abs(p.Y - at.Y)).FirstOrDefault();
                                if (target != null)
                                    a.ShootFrom(at, Arena.Sig0, Arena.AngleBetween(at, Arena.P(target)), 3, Arena.Deg(10));
                            });
                        }
                    },
                },
                // Malus shields himself while three cultist souls chant: each one still standing
                // when the chant ends heals him; kill them all and his armour breaks
                new SoulPhase
                {
                    Name = "Blood Moon Ritual", Below = Low, Color = CultFire,
                    Line = "Brothers! Sisters! Give me your blood!",
                    Every = 17000, Busy = 7200,
                    Run = a =>
                    {
                        const int chant = 6500;
                        a.Shield(chant);
                        a.Gather(CultFire, 6);
                        a.Effect(EffectType.Flashing, CultRed, new Position(0.4f, 15));
                        a.Notify("Kill the cultist souls!", CultFire);
                        ritual.Clear();
                        var start = a.RandomAngle();
                        ritual.AddRange(a.Empowered.SummonSoulsAt(a.Boss,
                            Enumerable.Range(0, 3).Select(k => a.At(start + k * Math.PI * 2 / 3, 6)), 0.02));
                        a.Repeat(6, 1000, i => a.Ring(Arena.Bolt, 10, i * Math.PI / 10), 500);
                        a.Later(chant, () =>
                        {
                            var left = ritual.Where(s => s.World == a.World && !s.Dead).ToList();
                            ritual.Clear();
                            if (left.Count == 0)
                            {
                                a.Notify("The ritual is broken!", BossResurrection.SoulGreen);
                                a.Boss.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, 5000);
                                a.Say("No... my children...");
                                return;
                            }
                            foreach (var s in left)
                            {
                                a.Lightning(Arena.P(s), CultRed);
                                a.World.LeaveWorld(s);
                            }
                            a.Heal(0.025 * left.Count);
                            a.Say("Their blood is MINE!");
                            a.Ring(Arena.Sig1, 24, a.RandomAngle());
                            a.Later(350, () => a.Ring(Arena.Sig1, 24, a.RandomAngle()));
                        });
                    },
                },
            };
        }

        private static SoulPhase[] VoidEntity() => new[]
        {
            // three rings of the void close in on it, outermost first; stand between them
            new SoulPhase
            {
                Name = "Event Horizon", Below = High, Color = VoidPurple,
                Line = "There is no escape from the pull of the void.",
                Every = 11000, Busy = 4000,
                Run = a =>
                {
                    a.Gather(VoidPurple, 10);
                    var radii = new[] { 10.5, 7.0, 3.5 };
                    for (var r = 0; r < radii.Length; r++)
                    {
                        var n = (int)(radii[r] * 2.3) + 4;
                        var off = a.RandomAngle();
                        for (var k = 0; k < n; k++)
                            a.Blast(a.At(off + k * Math.PI * 2 / n, radii[r]), 1.35, 1400 + r * 700, 1.4,
                                r == 2 ? VoidDeep : VoidPurple, ConditionEffectIndex.Darkness, 1500);
                    }
                    a.Later(3600, () => a.Ring(Arena.Sig0, 18, a.RandomAngle()));
                },
            },
            // a rift opens under one player and collapses, rings of the void spilling out of it
            new SoulPhase
            {
                Name = "Singularity", Below = Low, Color = VoidDeep,
                Line = "Collapse.",
                Every = 12500, Busy = 4200,
                Run = a =>
                {
                    var targets = a.Targets();
                    if (targets.Count == 0)
                        return;
                    var at = Arena.P(targets[a.Rng.Next(targets.Count)]);
                    a.Lightning(at, VoidPurple);
                    a.Blast(at, 4.2, 2400, 2.2, VoidDeep, ConditionEffectIndex.Darkness, 2000, style: 1, landed: () =>
                    {
                        a.Repeat(3, 400, i => a.RingFrom(at, Arena.Sig1, 10, i * Math.PI / 15));
                    });
                    a.Repeat(6, 400, i => a.Shoot(Arena.Sig0, a.AngleTo(at) + Arena.Deg(i * 60), 2, Arena.Deg(180)));
                },
            },
        };

        // --- The Elder Tomb of the Ancients --------------------------------------------------

        private const uint Sand = 0xFFE3C07A, Silver = 0xFFD8DDE6, NightBlue = 0xFF3B5BDB, StarWhite = 0xFFCFE3FF, SunFire = 0xFFFF9A2E;

        private static SoulPhase[] Bes() => new[]
        {
            // Bes raises his shield and pulses out rings of silver shields, each a little turned
            new SoulPhase
            {
                Name = "Shield Wall", Below = High, Color = Silver,
                Line = "The tomb is sealed. You shall not pass!",
                Every = 10000, Busy = 3800,
                Run = a =>
                {
                    a.Boss.ApplyConditionEffect(ConditionEffectIndex.Armored, 3600);
                    var start = a.RandomAngle();
                    a.Repeat(5, 750, i =>
                    {
                        a.Wave(2 + i, Silver);
                        a.Ring(Arena.Sig0, 12, start + i * Arena.Deg(15));
                    });
                },
            },
            // he hurls his shield at every player; the sand breaks armour, and a wave of fire follows
            new SoulPhase
            {
                Name = "Wrath of Bes", Below = Low, Color = Sand,
                Line = "FEEL THE WRATH OF THE PROTECTOR!",
                Every = 11000, Busy = 3200,
                Run = a =>
                {
                    foreach (var p in a.Targets())
                    {
                        var at = Arena.P(p);
                        a.Throw(at, Sand);
                        a.Blast(at, 3, 1600, 1.6, Sand, ConditionEffectIndex.ArmorBroken, 3000);
                    }
                    a.Later(1700, () =>
                    {
                        a.Effect(EffectType.Earthquake, Sand);
                        a.Wave(6, Sand);
                        a.Ring(Arena.Sig1, 16, a.RandomAngle());
                    });
                },
            },
        };

        private static SoulPhase[] Nut() => new[]
        {
            // the sky falls: stars land one after another round the players
            new SoulPhase
            {
                Name = "Starfall", Below = High, Color = StarWhite,
                Line = "The night sky weeps for you.",
                Every = 9500, Busy = 3600,
                Run = a =>
                {
                    var spots = a.Targets().Select(Arena.P).ToList();
                    while (spots.Count < 14)
                    {
                        var at = a.At(a.RandomAngle(), 2 + a.Rng.NextDouble() * 8);
                        if (a.Walkable(at))
                            spots.Add(at);
                        else if (a.Rng.Next(4) == 0)
                            break;
                    }
                    for (var j = 0; j < spots.Count; j++)
                    {
                        var at = spots[j];
                        a.Later(j * 150, () =>
                        {
                            a.Throw(at, StarWhite);
                            a.Blast(at, 1.6, 1300, 1.2, StarWhite, ConditionEffectIndex.Slowed, 1000);
                        });
                    }
                },
            },
            // six stars orbit her, firing ice out along their paths
            new SoulPhase
            {
                Name = "Celestial Orbit", Below = Low, Color = NightBlue,
                Line = "Every star in my sky turns against you.",
                Every = 12000, Busy = 5200,
                Run = a =>
                {
                    var start = a.RandomAngle();
                    a.Gather(NightBlue, 5);
                    a.Repeat(20, 240, i =>
                    {
                        if (i % 4 == 0)
                            a.Wave(5, NightBlue);
                        for (var k = 0; k < 6; k++)
                        {
                            var angle = start + k * Math.PI / 3 + Arena.Deg(9 * i);
                            var at = a.At(angle, 5);
                            a.ShootFrom(at, k % 2 == 0 ? Arena.Sig0 : Arena.Sig1, angle + Math.PI / 2);
                        }
                    }, 300);
                },
            },
        };

        private static SoulPhase[] Geb() => new[]
        {
            // the earth splits from Geb to every player and past them, fire bursting from the far end
            new SoulPhase
            {
                Name = "Fissures", Below = High, Color = Sand,
                Line = "The earth answers its god!",
                Every = 10000, Busy = 3400,
                Run = a =>
                {
                    a.Effect(EffectType.Earthquake, Sand);
                    foreach (var p in a.Targets())
                    {
                        var angle = a.AngleTo(Arena.P(p));
                        var length = Math.Min(14, p.DistTo(a.Boss) + 4);
                        var steps = (int)(length / 1.5);
                        for (var s = 1; s <= steps; s++)
                        {
                            var at = a.At(angle, s * 1.5);
                            var last = s == steps;
                            a.Blast(at, 1.05, 900 + s * 110, 1.3, last ? SunFire : Sand,
                                landed: last ? () => a.RingFrom(at, Arena.Sig0, 6, angle) : null);
                        }
                    }
                },
            },
            // slow suns rolled at the players burst into fireballs where they stop
            new SoulPhase
            {
                Name = "Sun Disk", Below = Low, Color = SunFire,
                Line = "Burn beneath the sun of Geb!",
                Every = 11000, Busy = 3600,
                Run = a =>
                {
                    const int life = 2400;
                    const double reach = 22 * life / 10000.0;
                    var targets = a.Targets().OrderBy(p => p.DistTo(a.Boss)).Take(4).ToList();
                    var angles = targets.Count > 0 ? targets.Select(p => a.AngleTo(Arena.P(p))).ToList()
                        : Enumerable.Range(0, 3).Select(k => k * Math.PI * 2 / 3).ToList();
                    a.Wave(3, SunFire);
                    foreach (var angle in angles)
                    {
                        var end = a.At(angle, reach);
                        a.Shoot(Arena.Sig1, angle);
                        a.Mark(end, 2.2, life, SunFire);
                        a.Later(life, () =>
                        {
                            a.Effect(EffectType.AreaBlast, SunFire, new Position(2.2f, 0));
                            a.RingFrom(end, Arena.Sig0, 10, angle);
                        });
                    }
                },
            },
        };

        // --- The Lair of Elder Draconis ------------------------------------------------------

        private const uint Venom = 0xFF5BE04A, Azure = 0xFF3FA9FF, Crimson = 0xFFFF4A2A, Obsidian = 0xFF5A4A78;

        private static SoulPhase[] Limoz() => new[]
        {
            // spore mines settle round the players and burst, sickening, spines flying from them
            new SoulPhase
            {
                Name = "Spore Mines", Below = High, Color = Venom,
                Line = "Breathe deep, little ones.",
                Every = 10500, Busy = 4000,
                Run = a =>
                {
                    foreach (var p in a.Targets())
                        for (var k = 0; k < 3; k++)
                        {
                            var at = Arena.At(Arena.P(p), a.RandomAngle(), 1 + a.Rng.NextDouble() * 3);
                            a.Throw(at, Venom);
                            a.Mark(at, 0.8, 3200, Venom);
                            a.Later(1600, () => a.Blast(at, 2.3, 1600, 1.3, Venom, ConditionEffectIndex.Sick, 2500,
                                landed: () => a.RingFrom(at, Arena.Sig0, 8, a.RandomAngle())));
                        }
                },
            },
            // rings of thorns erupt outward from Limoz, each thorn throwing a spine
            new SoulPhase
            {
                Name = "Thornburst", Below = Low, Color = Venom,
                Line = "The forest grows from your corpses!",
                Every = 11000, Busy = 3400,
                Run = a =>
                {
                    var radii = new[] { 3.0, 6.0, 9.0 };
                    for (var r = 0; r < radii.Length; r++)
                    {
                        var n = 6 + r * 5;
                        var off = a.RandomAngle();
                        for (var k = 0; k < n; k++)
                        {
                            var angle = off + k * Math.PI * 2 / n;
                            var at = a.At(angle, radii[r]);
                            a.Blast(at, 1.2, 1200 + r * 600, 1.2, Venom, ConditionEffectIndex.Sick, 1500,
                                landed: () => a.ShootFrom(at, Arena.Sig0, angle));
                        }
                    }
                    a.Later(2600, () => a.Ring(Arena.Sig1, 20, a.RandomAngle()));
                },
            },
        };

        private static SoulPhase[] Nikao() => new[]
        {
            // three tidal rings roll out from Nikao, each broken in three places to swim through
            new SoulPhase
            {
                Name = "Tidal Surge", Below = High, Color = Azure,
                Line = "The tide comes for you!",
                Every = 10500, Busy = 3400,
                Run = a =>
                {
                    a.Repeat(3, 800, w =>
                    {
                        const int n = 40;
                        var gaps = new HashSet<int>();
                        var first = a.Rng.Next(n);
                        for (var g = 0; g < 3; g++)
                            for (var j = 0; j < 4; j++)
                                gaps.Add((first + g * n / 3 + j) % n);
                        a.Wave(3, Azure);
                        var off = a.RandomAngle();
                        for (var k = 0; k < n; k++)
                            if (!gaps.Contains(k))
                                a.Shoot(Arena.Sig0, off + k * Math.PI * 2 / n);
                    });
                },
            },
            // the deep freezes under every player and bursts into storms
            new SoulPhase
            {
                Name = "Frozen Deep", Below = Low, Color = Azure,
                Line = "Sink, and be still.",
                Every = 11000, Busy = 3000,
                Run = a =>
                {
                    foreach (var p in a.Targets())
                    {
                        var at = Arena.P(p);
                        var off = a.RandomAngle();
                        a.Blast(at, 2.6, 1800, 1.4, Azure, ConditionEffectIndex.Slowed, 2500,
                            landed: () => a.ShootFrom(at, Arena.Sig1, off, 4, Math.PI / 2));
                    }
                },
            },
        };

        private static SoulPhase[] Pyyr() => new[]
        {
            // meteors rain round the players, each bursting into flame
            new SoulPhase
            {
                Name = "Meteor Shower", Below = High, Color = Crimson,
                Line = "Let the sky burn!",
                Every = 10000, Busy = 3800,
                Run = a =>
                {
                    var spots = a.Targets().Select(Arena.P).ToList();
                    for (var tries = 0; spots.Count < 10 && tries < 30; tries++)
                    {
                        var at = a.At(a.RandomAngle(), 3 + a.Rng.NextDouble() * 6);
                        if (a.Walkable(at))
                            spots.Add(at);
                    }
                    for (var j = 0; j < spots.Count; j++)
                    {
                        var at = spots[j];
                        a.Throw(at, Crimson);
                        a.Blast(at, 1.8, 1500 + j * 160, 1.3, Crimson, ConditionEffectIndex.ArmorBroken, 1500,
                            landed: () => a.RingFrom(at, Arena.Sig0, 5, a.RandomAngle()));
                    }
                },
            },
            // he breathes fire in a sweeping cone at the nearest player, fireballs in the flames
            new SoulPhase
            {
                Name = "Dragon's Breath", Below = Low, Color = Crimson,
                Line = "BURN!",
                Every = 11000, Busy = 3000,
                Run = a =>
                {
                    var target = a.Nearest();
                    var aim = target != null ? a.AngleTo(Arena.P(target)) : a.RandomAngle();
                    var dir = a.Rng.Next(2) == 0 ? 1 : -1;
                    a.Gather(Crimson, 3);
                    a.Repeat(16, 130, i =>
                    {
                        var angle = aim + dir * Arena.Deg(-50 + i * 100 / 15.0);
                        a.Shoot(Arena.Sig0, angle, 3, Arena.Deg(8));
                        if (i % 5 == 0)
                            a.Shoot(Arena.Sig1, angle);
                    }, 400);
                },
            },
        };

        private static SoulPhase[] Feargus() => new[]
        {
            // a roar that turns those close by to stone, then a ring of black shot
            new SoulPhase
            {
                Name = "Petrifying Roar", Below = High, Color = Obsidian,
                Line = "Look upon me... and be still.",
                Every = 11000, Busy = 3200,
                Run = a =>
                {
                    a.Gather(Obsidian, 6);
                    a.Blast(a.Pos, 5.5, 1900, 1.4, Obsidian, ConditionEffectIndex.Petrify, 1200, landed: () =>
                    {
                        a.Effect(EffectType.Earthquake, Obsidian);
                        a.Ring(Arena.Sig0, 20, a.RandomAngle());
                        a.Later(300, () => a.Ring(Arena.Sig0, 20, a.RandomAngle()));
                    });
                },
            },
            // its wings spread either side and close on the nearest player like shears
            new SoulPhase
            {
                Name = "Obsidian Wings", Below = Low, Color = Obsidian,
                Line = "There is no light under my wings.",
                Every = 11500, Busy = 3600,
                Run = a =>
                {
                    var target = a.Nearest();
                    if (target == null)
                        return;
                    var aim = a.AngleTo(Arena.P(target));
                    var left = a.At(aim + Math.PI / 2, 4.5);
                    var right = a.At(aim - Math.PI / 2, 4.5);
                    a.Mark(left, 1.2, 3000, Obsidian, 1);
                    a.Mark(right, 1.2, 3000, Obsidian, 1);
                    a.Repeat(9, 300, i =>
                    {
                        var to = Arena.P(target);
                        var spread = Arena.Deg(40 - i * 5);
                        a.ShootFrom(left, Arena.Sig1, Arena.AngleBetween(left, to) - spread);
                        a.ShootFrom(right, Arena.Sig1, Arena.AngleBetween(right, to) + spread);
                    }, 300);
                },
            },
        };

        private static SoulPhase[] IvoryWyvern() => new[]
        {
            // the four dragons' breaths in turn: venom, frost, flame and shadow
            new SoulPhase
            {
                Name = "Prismatic Breath", Below = High, Color = 0xFFFFFFFF,
                Line = "Four dragons. One will. Yours ends here.",
                Every = 10500, Busy = 4000,
                Run = a =>
                {
                    var colors = new[] { Venom, Azure, Crimson, Obsidian };
                    var start = a.RandomAngle();
                    a.Repeat(4, 800, k =>
                    {
                        a.Wave(3 + k, colors[k]);
                        a.Ring(Arena.Sig0 + k, 16, start + k * Math.PI / 32);
                        a.Later(250, () => a.Ring(Arena.Sig0 + k, 16, start + k * Math.PI / 32 + Math.PI / 16));
                    });
                },
            },
            // judgement hunts every player: three marks where they stand, then a great one
            new SoulPhase
            {
                Name = "Wyrm's Judgement", Below = Low, Color = 0xFFFFFFFF,
                Line = "Run. It changes nothing.",
                Every = 11500, Busy = 4200,
                Run = a =>
                {
                    var colors = new[] { Venom, Azure, Crimson, Obsidian };
                    foreach (var p in a.Targets())
                    {
                        var player = p;
                        for (var k = 0; k < 3; k++)
                        {
                            var c = colors[k];
                            a.Later(k * 650, () =>
                            {
                                if (player.World != a.World)
                                    return;
                                a.Blast(Arena.P(player), 1.8, 1100, 1.2, c);
                            });
                        }
                        a.Later(1950, () =>
                        {
                            if (player.World != a.World)
                                return;
                            var at = Arena.P(player);
                            a.Throw(at, 0xFFFFFFFF);
                            a.Blast(at, 3.2, 1300, 2.0, colors[3], ConditionEffectIndex.Slowed, 1500, landed: () =>
                            {
                                for (var k = 0; k < 4; k++)
                                    a.RingFrom(at, Arena.Sig0 + k, 3, k * Math.PI / 6);
                            });
                        });
                    }
                },
            },
        };

        // --- The Elder Ocean Trench ----------------------------------------------------------

        private const uint Sea = 0xFF1FB5C9, Foam = 0xFFBFF6FF, Abyss = 0xFF0B4F7A;

        private static SoulPhase[] Thessal() => new[]
        {
            // walls of tridents sweep across the trench from one side, the other, then across
            new SoulPhase
            {
                Name = "Riptide", Below = High, Color = Sea,
                Line = "The sea takes back what is hers!",
                Every = 10500, Busy = 4600,
                Run = a =>
                {
                    var target = a.Nearest();
                    var from = target != null ? a.AngleTo(Arena.P(target)) + Math.PI : a.RandomAngle();
                    var dirs = new[] { from, from + Math.PI, from + Math.PI / 2 };
                    a.Repeat(3, 1500, w =>
                    {
                        var d = dirs[w];
                        var centre = a.At(d, 11);
                        a.Effect(EffectType.Concentrate, Sea, centre, new Position(centre.X + 3, centre.Y));
                        a.Wall(centre, d + Math.PI, 17, 1.3, Arena.Sig0, a.Gap(17, 3));
                    });
                },
            },
            // whirlpools open round the players and churn three times, throwing out storms
            new SoulPhase
            {
                Name = "Drowning Depths", Below = Low, Color = Abyss,
                Line = "Come down to the deep with me...",
                Every = 12000, Busy = 4200,
                Run = a =>
                {
                    var spots = a.Targets().Select(p => Arena.At(Arena.P(p), a.RandomAngle(), a.Rng.NextDouble() * 1.5)).Take(5).ToList();
                    if (spots.Count == 0)
                        return;
                    foreach (var at in spots)
                    {
                        a.Throw(at, Foam);
                        for (var turn = 0; turn < 3; turn++)
                        {
                            var t = turn;
                            a.Later(t * 1000, () => a.Blast(at, 2.5, 1400, 1.0, t == 2 ? Abyss : Sea,
                                ConditionEffectIndex.Slowed, 1000, style: 1,
                                landed: () => a.RingFrom(at, Arena.Sig1, 6, t * Math.PI / 9)));
                        }
                    }
                },
            },
        };
    }
}
