using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The relic powers: what the Empowered bosses' items (tools/build_resurrection.py,
    // <RelicPower>) do beyond their numbers, while equipped. A weapon's work on its shots' hits
    // (RelicHit, from EnemyHit), an ability's when it's used (RelicAbility), an armour's when its
    // wearer is shot (RelicHurt, RelicAbsorb) and a ring's on its own (RelicTick). Their extra hits
    // go through Enemy.Damage, which never sets off another power.
    partial class Player
    {
        private int _relicHits;                         // Broadside, Ignition: weapon hits counted
        private readonly Dictionary<int, int> _venom = new Dictionary<int, int>();  // Widowfang: stacks per enemy
        private int _souls;                             // Phylactery: souls stored by kills
        private long _relicReadyAt;                     // the armour's own cooldown
        private long _ringReadyAt;                      // Ring of the Drowned Captain
        private long _phaseReadyAt;                     // Prism of the Fallen Sprite
        private long _shellReadyAt;                     // Obsidian Wyrmplate
        private bool _shell;
        private bool _twilightIce;                      // Twilight Cataclysm: the next cast is ice
        private long _forgottenUntil;                   // Shroud of the Forgotten King
        private long _bulwarkReadyAt;                   // Bracer of the Elder Guardian
        private int _horizonHits;                       // Bow of the Event Horizon: hits counted
        private float _stanceX, _stanceY;               // Lodestone of the Halls: where its wearer stands,
        private long _stanceSince;                      // and since when
        private bool _stanceOn;
        private long _entropyReadyAt;                   // The Hollow Sourcestone
        private int _lifestealThisSecond;
        private long _lifestealSecond;
        private long _hitFxAt;                          // the next a hit's own look may show
        [ThreadStatic] private static bool _inRelic;

        // The looks of the Necromantic items' powers (ShowEffect Relic, drawn by the Rust client:
        // client-rs/app/src/particles/relic.rs, the same numbers). Sent beside the older effects,
        // which clients before 2.5.4 still show.
        private enum Fx : byte
        {
            Cannon = 1,         // Broadside: a cannonball bursting on the target, fire, smoke and spray
            Venom = 2,          // Widowfang: a splash of venom, its puddle and bubbles
            SerpentWind = 3,    // Quiver: feathered wind round its user (ms)
            StoneGaze = 4,      // Gorgon's Gaze: stone chips off a hit enemy
            SoulGather = 5,     // Phylactery: a soul flying from a kill to its keeper
            SoulRelease = 6,    // Phylactery: the stored souls bursting at the target
            Phase = 7,          // Prism: shimmering out of the world (ms)
            Hellbrand = 8,      // Soulfire Staff: a burning brand under the enemy (ms)
            Blood = 9,          // Sanguine Wand: blood drawn from the enemy to its wielder
            Masquerade = 10,    // Masquerade Robe: a puff of smoke and confetti
            Davy = 11,          // Ring of the Drowned Captain: the sea pulling its wearer back
            ToxicScales = 12,   // Veridian Wyrmhide: a cloud of venom off the scales
            SkyBolt = 13,       // Azure Tempest Seal, Scepter of the Ivory Wyvern: lightning from the sky
            Ignition = 14,      // Wyrmfire Katana: flames on the enemy
            ShellReady = 15,    // Obsidian Wyrmplate: shards closing round its wearer
            ShellBreak = 16,    // Obsidian Wyrmplate: the shell shattering
            Undertow = 17,      // Orb of the Drowned Tide: a whirlpool (ms)
            Oath = 18,          // Bulwark of Bes: a golden ward on each player it reaches
            Starlight = 19,     // Tome of the Night Sky: stars falling on each player it reaches
            Earthshaker = 20,   // Crown of Geb: the ground cracking, rocks thrown up
            Blobomb = 21,       // Snare of the Bridge Sentinel: the blobomb's splatter
            Fire = 22,          // Twilight Cataclysm: a pillar of flame
            Ice = 23,           // Twilight Cataclysm: ice spikes and frost
            Forgotten = 24,     // Shroud of the Forgotten King: a red mist round its wearer (ms)
            Pillar = 25,        // Seal of the Risen Colossus: a marble pillar striking up
            Pentagram = 26,     // Skull of the Final Ritual: the star drawn on the ground (ms)
            Eruption = 27,      // Skull of the Final Ritual: its heart erupting
            Singularity = 28,   // Bow of the Event Horizon: the black hole (ms)
            // 29: the Chaotic Scripture's Chaos Beam, sent by AllyAOE (BehaviorDb.Allies.cs)
        }

        // on: the object it follows (an aura) or flies to (a soul, blood); at: where it is
        private void RelicFx(Fx look, Entity on, Position at, float radius, uint color, int ms = 0)
        {
            if (World == null || on == null)
                return;
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = on.Id, Pos1 = at,
                Pos2 = new Position(radius, (int)look), Color = new ARGB(color), Duration = ms, SourceId = Id,
            }, ref at);
        }

        private Position Here => new Position(X, Y);

        private static Position PosOf(Entity e) => new Position(e.X, e.Y);

        // a look shown on many hits, at most every `ms`
        private bool HitFxDue(int ms)
        {
            if (Now < _hitFxAt)
                return false;
            _hitFxAt = Now + ms;
            return true;
        }

        private string Power(int slot) => Inventory[slot]?.RelicPower;

        private static long Now => Environment.TickCount64;

        private void RelicEffect(Entity at, EffectType type, uint color, Position p1 = default, Position p2 = default) =>
            World?.BroadcastIfVisible(new ShowEffect { EffectType = type, TargetObjectId = at.Id, Pos1 = p1, Pos2 = p2, Color = new ARGB(color), SourceId = Id }, at);

        private void RelicNote(string text, uint color) =>
            World?.BroadcastIfVisible(new Notification { ObjectId = Id, Message = text, Color = new ARGB(color) }, this);

        private void RelicDamage(Enemy e, int dmg, ref TickTime time, bool noDef)
        {
            if (e.Dead || e.World == null || dmg <= 0)
                return;
            _inRelic = true;
            try
            {
                e.Damage(this, ref time, dmg, noDef);
            }
            finally
            {
                _inRelic = false;
            }
        }

        private List<Enemy> EnemiesNear(float x, float y, float radius) =>
            World.EnemiesCollision.HitTest(x, y, radius).OfType<Enemy>()
                .Where(e => !e.Dead && e.ObjectDesc.Enemy && MathF.Sqrt((e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y)) <= radius).ToList();

        // a weapon shot hit an enemy for dmg (after defense)
        private void RelicHit(Enemy enemy, int dmg, bool killed, ref TickTime time)
        {
            if (killed)
                RelicKill(enemy);
            if (_inRelic || World == null)
                return;
            // forgotten by the Realm (the Shroud's ability): a fifth more, past its defense
            if (!killed && Now < _forgottenUntil && Power(1) == "Forgotten")
                RelicDamage(enemy, Math.Max(1, dmg / 5), ref time, true);
            // the Blood Pact pays its wearer near death: 15% more, past its defense
            if (!killed && Power(3) == "BloodPact" && Health > 0 && Health < Stats[0] / 2)
                RelicDamage(enemy, Math.Max(1, dmg * 15 / 100), ref time, true);
            switch (Power(0))
            {
                // every sixth hit, a cannonball bursts on the target: three times the hit to all round it
                case "Broadside":
                    if (++_relicHits % 6 != 0)
                        break;
                    RelicEffect(enemy, EffectType.AreaBlast, 0xFFFFA040, new Position(2, 0));
                    RelicFx(Fx.Cannon, this, PosOf(enemy), 2, 0xFFFFA040);
                    foreach (var e in EnemiesNear(enemy.X, enemy.Y, 2))
                        RelicDamage(e, dmg * 3, ref time, false);
                    break;
                // venom builds on each enemy hit; at five it bursts, past its defense
                case "Venom":
                    if (killed)
                    {
                        _venom.Remove(enemy.Id);
                        break;
                    }
                    _venom.TryGetValue(enemy.Id, out var stacks);
                    if (++stacks < 5)
                    {
                        _venom[enemy.Id] = stacks;
                        break;
                    }
                    _venom.Remove(enemy.Id);
                    if (_venom.Count > 50)
                        _venom.Clear();
                    RelicEffect(enemy, EffectType.Diffuse, 0xFFB050FF, new Position(enemy.X, enemy.Y), new Position(enemy.X + 1.5f, enemy.Y));
                    RelicFx(Fx.Venom, enemy, PosOf(enemy), 1.5f, 0xFF70E040);
                    RelicDamage(enemy, 650, ref time, true);
                    break;
                // the gorgon's stare: half as much again on an enemy still above three quarters health
                case "StoneGaze":
                    if (!killed && enemy.MaxHealth > 0 && enemy.Health > enemy.MaxHealth * 0.75)
                    {
                        if (HitFxDue(350))
                            RelicFx(Fx.StoneGaze, enemy, PosOf(enemy), 0.8f, 0xFFA8B0A0);
                        RelicDamage(enemy, dmg / 2, ref time, true);
                    }
                    break;
                // a hit sometimes brands the enemy: cursed, it takes more from everyone, for 2 s
                case "Hellbrand":
                    if (!killed && Random.Shared.NextDouble() < 0.15)
                    {
                        enemy.ApplyConditionEffect(ConditionEffectIndex.Curse, 2000);
                        RelicEffect(enemy, EffectType.Potion, 0xFFFF3020);
                        RelicFx(Fx.Hellbrand, enemy, PosOf(enemy), 1.2f, 0xFFFF3020, 2000);
                    }
                    break;
                // a share of each hit back as health, at most 40 a second
                case "Bloodthirst":
                    if (Now / 1000 != _lifestealSecond)
                    {
                        _lifestealSecond = Now / 1000;
                        _lifestealThisSecond = 0;
                    }
                    var heal = Math.Min(Math.Max(1, dmg * 4 / 100), 40 - _lifestealThisSecond);
                    if (heal > 0 && Health < Stats[0])
                    {
                        _lifestealThisSecond += heal;
                        Health = Math.Min(Stats[0], Health + heal);
                        if (HitFxDue(300))
                            RelicFx(Fx.Blood, this, PosOf(enemy), 0.5f, 0xFFD01030);
                    }
                    break;
                // every tenth hit tears a singularity open on the enemy: six pulses, half a second
                // apart, each past the defense of everything within 3 tiles, which it slows
                case "Singularity":
                    if (++_horizonHits % 10 != 0)
                        break;
                    var hole = new Position(enemy.X, enemy.Y);
                    Mark(hole, 3, 3000, 0xFF8040FF);
                    RelicFx(Fx.Singularity, this, hole, 3, 0xFF8040FF, 3200);
                    RelicNote("Singularity", 0xFFB080FF);
                    for (var n = 1; n <= 6; n++)
                        World.StartNewTimer(n * 500, (w, t) =>
                        {
                            if (World != w)
                                return;
                            RelicEffect(this, EffectType.Concentrate, 0xFF8040FF, hole, new Position(hole.X + 3, hole.Y));
                            foreach (var e in EnemiesNear(hole.X, hole.Y, 3))
                            {
                                e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 800);
                                RelicDamage(e, 120, ref t, true);
                            }
                        });
                    break;
                // every third hit sets the enemy alight: half the hit again, burning over 2 s
                case "Ignition":
                    if (!killed && ++_relicHits % 3 == 0)
                    {
                        enemy.Bleed(this, dmg / 2, 2000);
                        if (HitFxDue(250))
                            RelicFx(Fx.Ignition, enemy, PosOf(enemy), 0.8f, 0xFFFF6020, 2000);
                    }
                    break;
            }
        }

        // anything of ours killed an enemy
        public void RelicKill(Entity victim = null)
        {
            if (Power(1) == "SoulHarvest" && _souls < 5)
            {
                _souls++;
                if (victim != null)
                    RelicFx(Fx.SoulGather, this, PosOf(victim), 0.5f, 0xFF7DFFCF);
                RelicNote($"Souls {_souls}/5", 0xFF7DFFCF);
            }
            // the Eclipsed Gemstone drinks each kill
            if (Power(3) == "Eclipse" && Mana < Stats[1])
                Mana = Math.Min(Stats[1], Mana + 15);
        }

        // the ability was used, at pos
        private void RelicAbility(Position pos, ref TickTime time)
        {
            if (World == null)
                return;
            // the Remembered Crown: the king's will with every ability
            if (Power(3) == "KingsWill")
                TimedStatBoost(20, 8, 3000);
            switch (Power(1))
            {
                // four marble pillars round its user, north, east, south and west, for 5 s: each
                // strikes what stands within 2.5 tiles of it once a second
                case "MarblePillars":
                    var pillars = new[] { new Position(X, Y - 3), new Position(X + 3, Y), new Position(X, Y + 3), new Position(X - 3, Y) };
                    foreach (var p in pillars)
                    {
                        Mark(p, 2.5f, 5000, 0xFFE8E8FF);
                        RelicFx(Fx.Pillar, this, p, 2.5f, 0xFFE8E8FF);
                    }
                    RelicNote("Marble Pillars", 0xFFE8E8FF);
                    for (var n = 1; n <= 5; n++)
                        World.StartNewTimer(n * 1000, (w, t) =>
                        {
                            if (World != w)
                                return;
                            foreach (var p in pillars)
                            {
                                Burst(p, 2.5f, 0xFFE8E8FF);
                                RelicFx(Fx.Pillar, this, p, 2.5f, 0xFFE8E8FF);
                                foreach (var e in EnemiesNear(p.X, p.Y, 2.5f))
                                    RelicDamage(e, 150, ref t, false);
                            }
                        });
                    break;
                // a pentagram on the target: its five points burst in turn, 300 ms apart, then its
                // heart erupts past the defense of what stands in it, and curses it
                case "Pentagram":
                    var star = pos;
                    Mark(star, 3, 2000, 0xFFFF2020);
                    RelicFx(Fx.Pentagram, this, star, 2.5f, 0xFFFF2020, 1900);
                    for (var n = 0; n < 5; n++)
                    {
                        var a = (-90 + n * 144) * MathF.PI / 180;   // (the star's own order: every second point)
                        var point = new Position(star.X + 2.5f * MathF.Cos(a), star.Y + 2.5f * MathF.Sin(a));
                        World.StartNewTimer(300 + n * 300, (w, t) =>
                        {
                            if (World != w)
                                return;
                            Burst(point, 1.5f, 0xFFFF4020);
                            foreach (var e in EnemiesNear(star.X, star.Y, 3))
                                RelicDamage(e, 120, ref t, false);
                        });
                    }
                    World.StartNewTimer(1900, (w, t) =>
                    {
                        if (World != w)
                            return;
                        Burst(star, 3, 0xFFFF0000);
                        RelicFx(Fx.Eruption, this, star, 3, 0xFFFF2020);
                        foreach (var e in EnemiesNear(star.X, star.Y, 3))
                        {
                            e.ApplyConditionEffect(ConditionEffectIndex.Curse, 3000);
                            RelicDamage(e, 400, ref t, true);
                        }
                    });
                    break;
                // a Blobomb, thrown with the trap: it lands in a second and bursts a second later
                case "Blobomb":
                    var bomb = pos;
                    Throw(bomb, 0xFFE0E0FF);
                    Mark(bomb, 3, 2000, 0xFFE0E0FF);
                    World.StartNewTimer(2000, (w, t) =>
                    {
                        if (World != w)
                            return;
                        Burst(bomb, 3, 0xFFFFFFFF);
                        RelicFx(Fx.Blobomb, this, bomb, 3, 0xFFE8E8FF);
                        foreach (var e in EnemiesNear(bomb.X, bomb.Y, 3))
                            RelicDamage(e, 700, ref t, false);
                    });
                    break;
                // fire, then ice, turn about: a burn on everything round the target, then a
                // freezing hit that slows it
                case "FireAndIce":
                    _twilightIce = !_twilightIce;
                    if (_twilightIce)   // (this cast is fire: the flag says what comes next)
                    {
                        Burst(pos, 3.5f, 0xFFFF7020);
                        RelicFx(Fx.Fire, this, pos, 3.5f, 0xFFFF7020);
                        foreach (var e in EnemiesNear(pos.X, pos.Y, 3.5f))
                            e.Bleed(this, 450, 3000);
                        RelicNote("Fire", 0xFFFF7020);
                    }
                    else
                    {
                        Burst(pos, 3.5f, 0xFF60C8FF);
                        RelicFx(Fx.Ice, this, pos, 3.5f, 0xFF60C8FF);
                        foreach (var e in EnemiesNear(pos.X, pos.Y, 3.5f))
                        {
                            e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 3000);
                            RelicDamage(e, 250, ref time, false);
                        }
                        RelicNote("Ice", 0xFF60C8FF);
                    }
                    break;
                // out of the Realm's memory for 5 s: RelicHit adds to every shot meanwhile
                case "Forgotten":
                    _forgottenUntil = Now + 5000;
                    RelicFx(Fx.Forgotten, this, Here, 1, 0xFFE04040, 5000);
                    RelicNote("Forgotten", 0xFFE04040);
                    break;
                // faster hands for 4 s
                case "SerpentsWind":
                    TimedStatBoost(28, 10, 4000);
                    RelicFx(Fx.SerpentWind, this, Here, 1, 0xFF40E0A0, 4000);
                    break;
                // the stored souls burst at the target and mend their keeper
                case "SoulHarvest":
                    if (_souls == 0)
                        break;
                    RelicEffect(this, EffectType.Diffuse, 0xFF7DFFCF, pos, new Position(pos.X + 4, pos.Y));
                    RelicFx(Fx.SoulRelease, this, pos, 4, 0xFF7DFFCF, _souls);
                    foreach (var e in EnemiesNear(pos.X, pos.Y, 4))
                        RelicDamage(e, 90 * _souls, ref time, true);
                    Player.HealDiscrete(this, 40 * _souls, false);
                    _souls = 0;
                    break;
                // a moment out of the world: untouchable for 1 s, every 8 s at most
                case "Phase":
                    if (Now < _phaseReadyAt)
                        break;
                    _phaseReadyAt = Now + 8000;
                    ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 1000);
                    ApplyConditionEffect(ConditionEffectIndex.Speedy, 2000);
                    RelicNote("Phased", 0xFF80FFFF);
                    RelicFx(Fx.Phase, this, Here, 1, 0xFF80FFFF, 1000);
                    break;
                // the storm strikes up to four enemies round the target
                case "Stormcall":
                    foreach (var e in EnemiesNear(pos.X, pos.Y, 6).OrderBy(e => e.DistTo(pos.X, pos.Y)).Take(4))
                    {
                        World.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Lightning, TargetObjectId = Id, Pos1 = new Position(e.X, e.Y), Pos2 = new Position(90, 0), Color = new ARGB(0xFF60A0FF) }, this);
                        RelicFx(Fx.SkyBolt, e, PosOf(e), 1, 0xFF60A0FF);
                        RelicDamage(e, 350, ref time, false);
                    }
                    break;
                // the undertow drags at everything round the target: slowed (its armour is the orb's
                // ArmorShred's, as the other orbs', not broken)
                case "Undertow":
                    RelicEffect(this, EffectType.Concentrate, 0xFF40C0FF, pos, new Position(pos.X + 3.5f, pos.Y));
                    RelicFx(Fx.Undertow, this, pos, 3.5f, 0xFF40C0FF, 1500);
                    foreach (var e in EnemiesNear(pos.X, pos.Y, 3.5f))
                    {
                        e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 3000);
                    }
                    break;
                // the guardian's oath: +10 defense to everyone within 5 tiles for 4 s
                case "GuardiansOath":
                    foreach (var p in World.Players.Values.Where(p => p.DistTo(this) <= 5))
                    {
                        p.TimedStatBoost(21, 10, 4000);
                        RelicFx(Fx.Oath, p, PosOf(p), p == this ? 5 : 1, 0xFFFFD050, 4000);
                    }
                    break;
                // starlight: +8 attack to everyone within 6 tiles for 4 s
                case "Starlight":
                    foreach (var p in World.Players.Values.Where(p => p.DistTo(this) <= 6))
                    {
                        p.TimedStatBoost(20, 8, 4000);
                        RelicFx(Fx.Starlight, p, PosOf(p), p == this ? 6 : 1, 0xFFB0C8FF, 4000);
                    }
                    break;
                // the earth shakes round its wearer: 400 to everything within 4 tiles
                case "Earthshaker":
                    RelicEffect(this, EffectType.AreaBlast, 0xFFC09040, new Position(4, 0));
                    RelicFx(Fx.Earthshaker, this, Here, 4, 0xFFC09040, 1200);
                    foreach (var e in EnemiesNear(X, Y, 4))
                        RelicDamage(e, 400, ref time, false);
                    break;
                // the wyvern's fury: half a second later, a second storm on the three nearest the target
                case "WyvernsFury":
                    var at = pos;
                    World.StartNewTimer(500, (w, t) =>
                    {
                        if (World != w)
                            return;
                        foreach (var e in EnemiesNear(at.X, at.Y, 6).OrderBy(e => e.DistTo(at.X, at.Y)).Take(3))
                        {
                            w.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Lightning, TargetObjectId = Id, Pos1 = new Position(e.X, e.Y), Pos2 = new Position(120, 0), Color = new ARGB(0xFFFFFFF0) }, this);
                            RelicFx(Fx.SkyBolt, e, PosOf(e), 1.3f, 0xFFFFF0C0);
                            RelicDamage(e, 300, ref t, false);
                        }
                    });
                    break;
            }
        }

        // shot for rawDamage (before defense)
        private void RelicHurt(int rawDamage)
        {
            if (World == null || Health <= 0)
                return;
            switch (Power(2))
            {
                // a fifth of the time, gone from sight and quick, 2 s (every 10 s at most)
                case "Masquerade":
                    if (Now < _relicReadyAt || Random.Shared.NextDouble() >= 0.2)
                        break;
                    _relicReadyAt = Now + 10000;
                    ApplyConditionEffect(ConditionEffectIndex.Invisible, 2000);
                    ApplyConditionEffect(ConditionEffectIndex.Speedy, 2000);
                    RelicNote("Masquerade!", 0xFFFF60C0);
                    RelicFx(Fx.Masquerade, this, Here, 1.5f, 0xFFFF60C0);
                    break;
                // venomous scales: enemies within 3 tiles burn for 240 over 3 s (every 2 s at most)
                case "ToxicScales":
                    if (Now < _relicReadyAt)
                        break;
                    _relicReadyAt = Now + 2000;
                    RelicEffect(this, EffectType.AreaBlast, 0xFF30C040, new Position(3, 0));
                    RelicFx(Fx.ToxicScales, this, Here, 3, 0xFF30C040);
                    foreach (var e in EnemiesNear(X, Y, 3))
                        e.Bleed(this, 240, 3000);
                    break;
            }
        }

        // before a shot's damage lands: the Obsidian Wyrmplate's shell takes all of one
        private int RelicAbsorb(int dmg)
        {
            if (Power(2) != "ObsidianShell" || !_shell)
                return dmg;
            _shell = false;
            _shellReadyAt = Now + 8000;
            RelicNote("Absorbed", 0xFFB0A0E0);
            RelicFx(Fx.ShellBreak, this, Here, 1.5f, 0xFF9080C0);
            return 0;
        }

        private void RelicTick()
        {
            if (World == null)
                return;
            if (Power(2) == "ObsidianShell")
            {
                if (!_shell && Now >= _shellReadyAt)
                {
                    _shell = true;
                    RelicEffect(this, EffectType.Flashing, 0xFF9080C0, new Position(0.3f, 2));
                    RelicFx(Fx.ShellReady, this, Here, 1.5f, 0xFF9080C0, 700);
                }
            }
            else
                _shell = false;

            // the halls' lodestone: 1.5 s without a step and its wearer stands as marble, armored
            // until they move
            if (Power(3) == "MarbleStance")
            {
                if (MathF.Abs(X - _stanceX) > 0.05f || MathF.Abs(Y - _stanceY) > 0.05f)
                {
                    _stanceX = X;
                    _stanceY = Y;
                    _stanceSince = Now;
                    _stanceOn = false;
                }
                else if (Now - _stanceSince >= 1500 && Health > 0)
                {
                    if (!_stanceOn)
                    {
                        _stanceOn = true;
                        RelicNote("Marble Stance", 0xFFE8E8FF);
                        RelicEffect(this, EffectType.Flashing, 0xFFE8E8FF, new Position(0.25f, 2));
                    }
                    // (kept up a tick at a time, so a step ends it)
                    ApplyConditionEffect(ConditionEffectIndex.Armored, 400);
                }
            }
            else
                _stanceOn = false;

            // the Hollow Sourcestone eats at the nearest enemy: its armor broken for 2 s, every 5 s
            if (Power(3) == "Entropy" && Now >= _entropyReadyAt && Health > 0)
            {
                var near = EnemiesNear(X, Y, 6).Where(e => !e.HasConditionEffect(ConditionEffectIndex.Invincible) && !e.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                    .OrderBy(e => e.DistTo(X, Y)).FirstOrDefault();
                if (near != null)
                {
                    _entropyReadyAt = Now + 5000;
                    near.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, 2000);
                    RelicEffect(near, EffectType.Potion, 0xFF8040FF);
                }
            }

            // the Elder Guardian's bracer braces: under half health, +12 defense for 5 s, every 20 s
            if (Power(3) == "Bulwark" && Health > 0 && Health < Stats[0] / 2 && Now >= _bulwarkReadyAt)
            {
                _bulwarkReadyAt = Now + 20000;
                TimedStatBoost(21, 12, 5000);
                RelicNote("Bulwark", 0xFFD0D8FF);
                RelicEffect(this, EffectType.Flashing, 0xFFD0D8FF, new Position(0.25f, 4));
            }

            // Davy's bargain: at a quarter of its health, its wearer is pulled back: half their
            // health and a moment untouchable, once a minute
            if (Power(3) == "DavysBargain" && Health > 0 && Health < Stats[0] / 4 && Now >= _ringReadyAt)
            {
                _ringReadyAt = Now + 60000;
                Player.HealDiscrete(this, Stats[0] / 2, false);
                ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 1500);
                RelicNote("Davy's Bargain", 0xFF40E0C0);
                RelicEffect(this, EffectType.AreaBlast, 0xFF40E0C0, new Position(3, 0));
                RelicFx(Fx.Davy, this, Here, 3, 0xFF40E0C0, 1500);
            }
        }
    }
}
