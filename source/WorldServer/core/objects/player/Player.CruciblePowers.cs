using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Crucible's white bags (tools/build_crucible.py, WHITES; 1% each from its champion):
    // Ferrogrim's Bastion Turret (Mechanic), Nerissa's Tidepearl (Druid), Pyrrhax's Meteor Mortar
    // (Pirate), Chronarch's Coat of Stolen Hours (Blademaster) and Vaelith's Final Aria (Bard).
    // What each does beyond its numbers, by its <RelicPower>: CrucibleAbility runs when the ability
    // is used (the turret is a Mechanic's turret, Player.Turrets.cs, slamming as Turret.Slam). The numbers here
    // are the ones their tooltips give: change both together.
    partial class Player
    {
        // Vaelith's Final Aria's look (ShowEffect Relic, drawn by the Rust client from 2.6.4:
        // client-rs/app/src/particles/relic.rs, look 48): a verse ringing out from the player to
        // its radius, objectType which verse (0 to 3, the last the crescendo)
        private const int AriaLook = 48;
        private const uint AriaGold = 0xFFFFD24A, AriaViolet = 0xFFC070FF;

        // the verses: (radius, damage), every AriaVerseMs, following the player; the last the
        // crescendo (its tooltip, in build_crucible.py's WHITES, says the same)
        private static readonly (float Radius, int Damage)[] AriaVerses = { (6, 250), (7, 320), (8, 400), (9, 700) };
        private const int AriaVerseMs = 600, AriaCrescendoMs = 3000;

        // Pyrrhax's Meteor Mortar's looks (client-rs relic.rs, looks 49 and 50): the shell's flight
        // from the player to where it lands over `ms`, and a blast (objectType 0 the shell landing,
        // its pool burning `ms`; 1 a meteor falling onto it over `ms`)
        private const int MortarShot = 49, MortarBlast = 50;
        private const uint MortarFire = 0xFFFF6A14;
        private const int MortarFlightMs = 700;
        // the shell: its blast's radius and damage (enemies paralysed for MortarStunMs)
        private const float MortarRadius = 3;
        private const int MortarDamage = 750, MortarStunMs = 1500;
        // then the meteors, one every MortarMeteorEveryMs onto a spot within MortarScatter of the
        // crater, each falling for MortarFallMs (its spot marked) and striking what's within MortarMeteorRadius
        private const int MortarMeteors = 4, MortarMeteorEveryMs = 300, MortarFallMs = 450, MortarMeteorDamage = 280;
        private const float MortarScatter = 3.5f, MortarMeteorRadius = 1.6f;
        // and the pool of slag left in the crater, burning what's in it every MortarPoolEveryMs
        private const int MortarPoolTicks = 6, MortarPoolEveryMs = 500, MortarPoolDamage = 90;
        private const float MortarPoolRadius = 2.5f;

        // the Bastion Turret raised where it lands (look 51, objectType 0, Player.Turrets.cs) and its
        // slam (objectType 1, Turret.cs): every BastionSlamMs, BastionSlamDamage to the enemies
        // within BastionSlamRadius, their armor broken for BastionBrokenMs, the players in it Armored
        public const int BastionLook = 51;
        public const uint BastionIron = 0xFFFF9A2A;
        private const float BastionSlamRadius = 3.5f;
        private const int BastionSlamDamage = 380, BastionSlamMs = 3000, BastionBrokenMs = 2000;

        // Nerissa's Tidepearl (look 52, objectType the wave): waves of the tide rolling out from
        // where its pearl lands, every TideEveryMs, each to TideRadius: TideDamage to enemies, TideHeal to allies
        private const int TideLook = 52, TideWaves = 3, TideEveryMs = 450, TideDamage = 220, TideHeal = 60;
        private const float TideRadius = 5.5f;
        private const uint TideColor = 0xFF46C2B4;

        // Chronarch's Coat of Stolen Hours (look 53): time stops round the blademaster (enemies within
        // HoursRadius slowed, the blademaster hasted, for HoursMs), and the clock's hand sweeps round
        // twice, HoursSweepMs each, cutting each enemy within reach once a sweep for HoursDamage
        private const int HoursLook = 53, HoursMs = 3000, HoursSweeps = 2, HoursSweepMs = 800, HoursSteps = 12, HoursDamage = 320;
        private const float HoursRadius = 6;
        private const uint HoursColor = 0xFF4AF0FF;

        private void CrucibleAbility(Position pos, ref TickTime time)
        {
            if (World == null)
                return;
            var item = Inventory[1];
            switch (item?.RelicPower)
            {
                // the chord (the Inspired aura is the item's own Activate), then the verses
                case "FinalAria":
                    for (var i = 0; i < AriaVerses.Length; i++)
                    {
                        var verse = i;
                        if (verse == 0)
                            AriaVerse(verse, ref time);
                        else
                            After(verse * AriaVerseMs, (w, t) => AriaVerse(verse, ref t));
                    }
                    break;
                // the shell arcs to where it's aimed (9 tiles at most), its landing marked; it
                // bursts, meteors rain round the crater, and a pool of slag burns on in it
                // the turret is the item's BuildTower (Player.Turrets.cs), raised where it lands
                case "BastionTurret":
                    break;
                // (Nerissa's Tidepearl is a Druid's pearl, its TidePearl; its tide rolls out where
                // it lands: NerissaTide, from Player.TidePearls.cs)
                // the jacket's buffs are its own Activates; then the stolen hours
                case "StolenHours":
                {
                    var at = new Position(X, Y);
                    MortarFx(HoursLook, at, HoursRadius, HoursSweeps * HoursSweepMs, color: HoursColor);
                    ApplyConditionEffect(ConditionEffectIndex.Speedy, HoursMs);
                    foreach (var e in EnemiesNear(X, Y, HoursRadius))
                        e.ApplyConditionEffect(ConditionEffectIndex.Slowed, HoursMs);
                    // the hand, from 12 o'clock (north) clockwise: step k cuts the sector it crosses
                    for (var sweep = 0; sweep < HoursSweeps; sweep++)
                        for (var k = 0; k < HoursSteps; k++)
                        {
                            var step = k;
                            After(sweep * HoursSweepMs + (k + 1) * HoursSweepMs / HoursSteps, (w, t) => HoursCut(at, step, ref t));
                        }
                    break;
                }
                case "MeteorMortar":
                {
                    var to = ClampAbilityTarget(pos, 9);
                    MortarFx(MortarShot, to, 0, MortarFlightMs);
                    Mark(to, MortarRadius, MortarFlightMs, MortarFire);
                    After(MortarFlightMs, (w, t) => MortarLands(to, ref t));
                    break;
                }
            }
        }

        private void MortarFx(int look, Position at, float radius, int ms, int objectType = 0, uint color = MortarFire) =>
            World?.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at, Pos2 = new Position(radius, look),
                Color = new ARGB(color), Duration = ms, ObjectType = (ushort)objectType, SourceId = Id,
            }, ref at);

        private void NerissaTide(Position at, ref TickTime time)
        {
            for (var i = 0; i < TideWaves; i++)
            {
                var wave = i;
                if (wave == 0)
                    TideWave(at, wave, ref time);
                else
                    After(wave * TideEveryMs, (w, t) => TideWave(at, wave, ref t));
            }
        }

        private void TideWave(Position at, int wave, ref TickTime time)
        {
            if (World == null || Dead)
                return;
            MortarFx(TideLook, at, TideRadius, 0, wave, TideColor);
            foreach (var e in EnemiesNear(at.X, at.Y, TideRadius))
                RelicDamage(e, TideDamage, ref time, false);
            foreach (var p in World.Players.Values)
                if (!p.Dead && p.DistTo(at.X, at.Y) <= TideRadius)
                    ActivateHealHp(p, TideHeal);
            HealTurrets(at.X, at.Y, TideRadius, TideHeal);
        }

        // the hand's step `step` (of HoursSteps, from north clockwise): the enemies within reach in
        // the sector it has just crossed
        private void HoursCut(Position at, int step, ref TickTime time)
        {
            if (World == null)
                return;
            var width = 360.0 / HoursSteps;
            foreach (var e in EnemiesNear(at.X, at.Y, HoursRadius))
            {
                // degrees clockwise from north (y grows southward)
                var deg = (Math.Atan2(e.X - at.X, -(e.Y - at.Y)) * 180 / Math.PI + 360) % 360;
                if (deg >= step * width && deg < (step + 1) * width)
                    RelicDamage(e, HoursDamage, ref time, false);
            }
        }

        private void MortarLands(Position at, ref TickTime time)
        {
            var poolMs = MortarPoolTicks * MortarPoolEveryMs;
            MortarFx(MortarBlast, at, MortarRadius, poolMs);
            Crucible.ShakeNear(World, at);
            foreach (var e in EnemiesNear(at.X, at.Y, MortarRadius))
            {
                RelicDamage(e, MortarDamage, ref time, false);
                if (!e.Dead)
                    e.ApplyConditionEffect(ConditionEffectIndex.Paralyzed, MortarStunMs);
            }
            for (var i = 0; i < MortarMeteors; i++)
            {
                var a = Random.Shared.NextDouble() * Math.PI * 2;
                var d = MortarScatter * Math.Sqrt(0.25 + 0.75 * Random.Shared.NextDouble());
                var spot = new Position(at.X + (float)(Math.Cos(a) * d), at.Y + (float)(Math.Sin(a) * d));
                After(250 + i * MortarMeteorEveryMs, (w, t) =>
                {
                    Mark(spot, MortarMeteorRadius, MortarFallMs, MortarFire);
                    MortarFx(MortarBlast, spot, MortarMeteorRadius, MortarFallMs, 1);
                });
                After(250 + i * MortarMeteorEveryMs + MortarFallMs, (w, t) =>
                {
                    foreach (var e in EnemiesNear(spot.X, spot.Y, MortarMeteorRadius))
                        RelicDamage(e, MortarMeteorDamage, ref t, false);
                });
            }
            for (var n = 1; n <= MortarPoolTicks; n++)
                After(n * MortarPoolEveryMs, (w, t) =>
                {
                    foreach (var e in EnemiesNear(at.X, at.Y, MortarPoolRadius))
                        RelicDamage(e, MortarPoolDamage, ref t, false);
                });
        }

        private void AriaVerse(int verse, ref TickTime time)
        {
            if (World == null || Dead)
                return;
            var (radius, damage) = AriaVerses[verse];
            var last = verse == AriaVerses.Length - 1;
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at, Pos2 = new Position(radius, AriaLook),
                Color = new ARGB(last ? AriaViolet : AriaGold), Duration = last ? AriaCrescendoMs : 0, ObjectType = (ushort)verse, SourceId = Id,
            }, ref at);
            foreach (var e in EnemiesNear(X, Y, radius))
            {
                RelicDamage(e, damage, ref time, false);
                if (last && !e.Dead)
                    e.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, AriaCrescendoMs);
            }
            if (!last)
                return;
            foreach (var p in World.Players.Values)
                if (!p.Dead && p.DistTo(X, Y) <= radius)
                    p.ApplyConditionEffect(ConditionEffectIndex.Berserk, AriaCrescendoMs);
        }
    }
}
