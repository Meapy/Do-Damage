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
        private int _lifestealThisSecond;
        private long _lifestealSecond;
        [ThreadStatic] private static bool _inRelic;

        private string Power(int slot) => Inventory[slot]?.RelicPower;

        private static long Now => Environment.TickCount64;

        private void RelicEffect(Entity at, EffectType type, uint color, Position p1 = default, Position p2 = default) =>
            World?.BroadcastIfVisible(new ShowEffect { EffectType = type, TargetObjectId = at.Id, Pos1 = p1, Pos2 = p2, Color = new ARGB(color) }, at);

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
                RelicKill();
            if (_inRelic || World == null)
                return;
            switch (Power(0))
            {
                // every sixth hit, a cannonball bursts on the target: three times the hit to all round it
                case "Broadside":
                    if (++_relicHits % 6 != 0)
                        break;
                    RelicEffect(enemy, EffectType.AreaBlast, 0xFFFFA040, new Position(2, 0));
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
                    RelicDamage(enemy, 650, ref time, true);
                    break;
                // the gorgon's stare: half as much again on an enemy still above three quarters health
                case "StoneGaze":
                    if (!killed && enemy.MaxHealth > 0 && enemy.Health > enemy.MaxHealth * 0.75)
                        RelicDamage(enemy, dmg / 2, ref time, true);
                    break;
                // a hit sometimes brands the enemy: cursed, it takes more from everyone, for 2 s
                case "Hellbrand":
                    if (!killed && Random.Shared.NextDouble() < 0.15)
                    {
                        enemy.ApplyConditionEffect(ConditionEffectIndex.Curse, 2000);
                        RelicEffect(enemy, EffectType.Potion, 0xFFFF3020);
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
                    }
                    break;
                // every third hit sets the enemy alight: half the hit again, burning over 2 s
                case "Ignition":
                    if (!killed && ++_relicHits % 3 == 0)
                        enemy.Bleed(this, dmg / 2, 2000);
                    break;
            }
        }

        // anything of ours killed an enemy
        public void RelicKill()
        {
            if (Power(1) == "SoulHarvest" && _souls < 5)
            {
                _souls++;
                RelicNote($"Souls {_souls}/5", 0xFF7DFFCF);
            }
        }

        // the ability was used, at pos
        private void RelicAbility(Position pos, ref TickTime time)
        {
            if (World == null)
                return;
            switch (Power(1))
            {
                // faster hands for 4 s
                case "SerpentsWind":
                    TimedStatBoost(28, 10, 4000);
                    break;
                // the stored souls burst at the target and mend their keeper
                case "SoulHarvest":
                    if (_souls == 0)
                        break;
                    RelicEffect(this, EffectType.Diffuse, 0xFF7DFFCF, pos, new Position(pos.X + 4, pos.Y));
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
                    break;
                // the storm strikes up to four enemies round the target
                case "Stormcall":
                    foreach (var e in EnemiesNear(pos.X, pos.Y, 6).OrderBy(e => e.DistTo(pos.X, pos.Y)).Take(4))
                    {
                        World.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Lightning, TargetObjectId = Id, Pos1 = new Position(e.X, e.Y), Pos2 = new Position(90, 0), Color = new ARGB(0xFF60A0FF) }, this);
                        RelicDamage(e, 350, ref time, false);
                    }
                    break;
                // the undertow drags at everything round the target: slowed and its armour broken
                case "Undertow":
                    RelicEffect(this, EffectType.Concentrate, 0xFF40C0FF, pos, new Position(pos.X + 3.5f, pos.Y));
                    foreach (var e in EnemiesNear(pos.X, pos.Y, 3.5f))
                    {
                        e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 3000);
                        e.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, 3000);
                    }
                    break;
                // the guardian's oath: +10 defense to everyone within 5 tiles for 4 s
                case "GuardiansOath":
                    foreach (var p in World.Players.Values.Where(p => p.DistTo(this) <= 5))
                        p.TimedStatBoost(21, 10, 4000);
                    break;
                // starlight: +8 attack to everyone within 6 tiles for 4 s
                case "Starlight":
                    foreach (var p in World.Players.Values.Where(p => p.DistTo(this) <= 6))
                        p.TimedStatBoost(20, 8, 4000);
                    break;
                // the earth shakes round its wearer: 400 to everything within 4 tiles
                case "Earthshaker":
                    RelicEffect(this, EffectType.AreaBlast, 0xFFC09040, new Position(4, 0));
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
                    break;
                // venomous scales: enemies within 3 tiles burn for 240 over 3 s (every 2 s at most)
                case "ToxicScales":
                    if (Now < _relicReadyAt)
                        break;
                    _relicReadyAt = Now + 2000;
                    RelicEffect(this, EffectType.AreaBlast, 0xFF30C040, new Position(3, 0));
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
                }
            }
            else
                _shell = false;

            // Davy's bargain: at a quarter of its health, its wearer is pulled back: half their
            // health and a moment untouchable, once a minute
            if (Power(3) == "DavysBargain" && Health > 0 && Health < Stats[0] / 4 && Now >= _ringReadyAt)
            {
                _ringReadyAt = Now + 60000;
                Player.HealDiscrete(this, Stats[0] / 2, false);
                ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 1500);
                RelicNote("Davy's Bargain", 0xFF40E0C0);
                RelicEffect(this, EffectType.AreaBlast, 0xFF40E0C0, new Position(3, 0));
            }
        }
    }
}
