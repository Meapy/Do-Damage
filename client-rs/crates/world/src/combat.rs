//! Projectiles and hits (`Projectile.as`, `Player.shoot`/`doShoot`, `GameObject.damageWithDefense`),
//! with AS3's arithmetic. The world reports what the client must tell the server as [`Hit`]s.

use crate::condition::{self, has};
use crate::{World, NO_TILE};
use bsk_data::imprint;
use bsk_data::{GameData, ObjectClass, ProjectileDesc};
use std::collections::HashSet;
use std::f64::consts::PI;

const MIN_ATTACK_FREQ: f64 = 0.0015;
const MAX_ATTACK_FREQ: f64 = 0.008;
const MIN_ATTACK_MULT: f64 = 0.5;
const MAX_ATTACK_MULT: f64 = 2.0;
/// `GameObject.radius_`: every object is hit within half a tile.
const HIT_RADIUS: f64 = 0.5;
/// How long a monster we think our shot killed waits for the server to remove it. The server
/// can deal less than we reckoned (it turned invulnerable or healed before the shot arrived, its
/// damage roll differs from ours, or it works out defense differently); kept marked dead, it
/// could no longer be shot and its own shots were dropped, so after this it's a target again.
const UNCONFIRMED_KILL_MS: i32 = 1000;

const STAT_HP: u8 = 1;
const STAT_INVENTORY_0: u8 = 8;
/// `INVDATA0`: the weapon's item data (its imprints).
const STAT_INVENTORY_DATA_0: u8 = 122;
const STAT_ATTACK: u8 = 20;
const STAT_DEFENSE: u8 = 21;
const STAT_DEXTERITY: u8 = 28;

#[derive(Debug, Clone)]
pub struct Projectile {
    pub owner_id: i32,
    pub bullet_id: i32,
    /// The weapon or monster that fired it, and which of its projectiles.
    pub container_type: u16,
    pub bullet_type: i32,
    pub angle: f64,
    pub start_time: i32,
    pub start_x: f64,
    pub start_y: f64,
    pub speed_mul: f64,
    pub damage: i32,
    /// Monsters' projectiles hurt players; players' hurt monsters.
    pub damages_players: bool,
    pub x: f64,
    pub y: f64,
    pub z: f64,
    /// How long it flies, when the weapon's imprints change it (else its description's), and
    /// whether they stop it piercing.
    pub life: Option<i32>,
    pub no_pierce: bool,
    hit: HashSet<i32>,
}

/// What a projectile did that the server must hear about.
#[derive(Debug, Clone, PartialEq)]
pub enum Hit {
    /// A monster's projectile hit us: PlayerHit. `pierced`: it ignores defense (`ArmorPiercing`).
    Player { bullet_id: i32, owner_id: i32, damage: i32, pierced: bool },
    /// Our projectile hit a monster: EnemyHit (`kill` if it should die of it).
    Enemy { time: i32, bullet_id: i32, target_id: i32, kill: bool, damage: i32, pierced: bool },
    /// A monster's projectile hit something else (another player, an obstacle): OtherHit.
    Other { time: i32, bullet_id: i32, owner_id: i32, target_id: i32 },
    /// A monster's projectile left the map or hit empty ground: SquareHit.
    Square { time: i32, bullet_id: i32, owner_id: i32 },
    /// We stood on damaging ground: GroundDamage.
    Ground { time: i32, x: f64, y: f64, damage: i32 },
}

/// Where a projectile struck, for the sparks it throws (`HitEffect`, `ExplosionEffect`).
#[derive(Debug, Clone, PartialEq)]
pub struct Impact {
    pub x: f64,
    pub y: f64,
    /// The object struck (sparks in its colours, an explosion if it died of it); `None` for an
    /// obstacle, sparks in the projectile's colours.
    pub target: Option<i32>,
    pub kill: bool,
    /// The object (by id) the projectile looks like, its direction and speed.
    pub look: String,
    pub angle: f64,
    pub speed: i32,
    /// Whose pet fired it, for a pet's shot (drawn faintly, as the options say).
    pub pet_owner: Option<i32>,
}

/// `Trig.boundToPI`.
pub fn bound_to_pi(a: f64) -> f64 {
    let mut a = a;
    while a < -PI {
        a += 2.0 * PI;
    }
    while a > PI {
        a -= 2.0 * PI;
    }
    a
}

/// `GameObject.damageWithDefense`; AS3 truncates to int at each assignment.
pub fn damage_with_defense(damage: i32, defense: i32, armor_piercing: bool, conditions: [u32; 2]) -> i32 {
    let mut def = defense;
    if armor_piercing || has(conditions, condition::ARMOR_BROKEN) {
        def = 0;
    } else if has(conditions, condition::ARMORED) {
        def = (def as f64 * 1.5) as i32;
    }
    if has(conditions, condition::EXPOSED) {
        def -= 20;
    }
    let min = damage * 2 / 20;
    let mut d = min.max(damage - def);
    if has(conditions, condition::INVULNERABLE) {
        d = 0;
    }
    if has(conditions, condition::PETRIFIED) {
        d = (d as f64 * 0.9) as i32;
    }
    if has(conditions, condition::CURSE) {
        d = (d as f64 * 1.25) as i32;
    }
    d
}

impl Projectile {
    #[allow(clippy::too_many_arguments)]
    pub fn new(owner_id: i32, bullet_id: i32, container_type: u16, bullet_type: i32, angle: f64, start_time: i32, x: f64, y: f64, speed_mul: f64, damage: i32, damages_players: bool) -> Self {
        Projectile {
            owner_id,
            bullet_id,
            container_type,
            bullet_type,
            angle: bound_to_pi(angle),
            start_time,
            start_x: x,
            start_y: y,
            speed_mul,
            damage,
            damages_players,
            x,
            y,
            z: 0.5,
            life: None,
            no_pierce: false,
            hit: HashSet::new(),
        }
    }

    /// `Projectile.positionAt`.
    pub fn position_at(&self, desc: &ProjectileDesc, elapsed: i32) -> (f64, f64) {
        let (mut x, mut y) = (self.start_x, self.start_y);
        let elapsed = elapsed as f64;
        let lifetime = desc.lifetime as f64;
        let mut dist = elapsed * (desc.speed as f64 / 10000.0) * self.speed_mul;
        let phase = if self.bullet_id.rem_euclid(2) == 0 { 0.0 } else { PI };
        if desc.wavy {
            let theta = self.angle + PI / 64.0 * (phase + 6.0 * PI * elapsed / 1000.0).sin();
            x += dist * theta.cos();
            y += dist * theta.sin();
        } else if desc.parametric {
            let t = elapsed / lifetime * 2.0 * PI;
            let px = t.sin() * if self.bullet_id.rem_euclid(2) != 0 { 1.0 } else { -1.0 };
            let py = (2.0 * t).sin() * if self.bullet_id.rem_euclid(4) < 2 { 1.0 } else { -1.0 };
            let (sin, cos) = self.angle.sin_cos();
            x += (px * cos - py * sin) * desc.magnitude;
            y += (px * sin + py * cos) * desc.magnitude;
        } else {
            if desc.boomerang {
                let halfway = lifetime * ((desc.speed as f64 * self.speed_mul) / 10000.0) / 2.0;
                if dist > halfway {
                    dist = halfway - (dist - halfway);
                }
            }
            x += dist * self.angle.cos();
            y += dist * self.angle.sin();
            if desc.amplitude != 0.0 {
                let deflection = desc.amplitude * (phase + elapsed / lifetime * desc.frequency * 2.0 * PI).sin();
                x += deflection * (self.angle + PI / 2.0).cos();
                y += deflection * (self.angle + PI / 2.0).sin();
            }
        }
        (x, y)
    }
}

impl World {
    /// The projectile description for a projectile, from its container.
    pub fn projectile_desc<'a>(&self, p: &Projectile, data: &'a GameData) -> Option<&'a ProjectileDesc> {
        data.objects.get(&p.container_type)?.projectiles.get(&p.bullet_type)
    }

    /// Whether a pet (or an ally) fired it: its shots are its owner's, fired from the pet (a
    /// Character), where a player's come from a weapon or ability.
    pub fn pet_shot(&self, p: &Projectile, data: &GameData) -> bool {
        !p.damages_players && data.objects.get(&p.container_type).is_some_and(|c| c.class == ObjectClass::Character)
    }

    pub fn add_projectile(&mut self, p: Projectile) {
        self.projectiles.push(p);
    }

    /// `Player.attackFrequency` (shots per millisecond, before the weapon's rate of fire).
    fn attack_frequency(&self, dexterity: i32, conditions: [u32; 2]) -> f64 {
        if has(conditions, condition::DAZED) {
            return MIN_ATTACK_FREQ;
        }
        let mut f = MIN_ATTACK_FREQ + dexterity as f64 / 75.0 * (MAX_ATTACK_FREQ - MIN_ATTACK_FREQ);
        if has(conditions, condition::BERSERK) {
            f *= 1.25;
        }
        f
    }

    /// `Player.attackMultiplier`.
    fn attack_multiplier(&self, attack: i32, conditions: [u32; 2]) -> f64 {
        if has(conditions, condition::WEAK) {
            return MIN_ATTACK_MULT;
        }
        let mut m = MIN_ATTACK_MULT + attack as f64 / 75.0 * (MAX_ATTACK_MULT - MIN_ATTACK_MULT);
        if has(conditions, condition::DAMAGING) {
            m *= 1.25;
        }
        m
    }

    /// `Player.shoot` toward the world angle `angle` at `time`: the projectiles fired (to send
    /// as PlayerShoot), or none if it's too soon, we can't, or there's no weapon.
    /// `last_move` is when the last Move went out: shots more than 600 ms after it do no damage.
    pub fn shoot(&mut self, time: i32, angle: f64, last_move: i32, data: &GameData) -> Vec<Projectile> {
        let Some(me) = self.me else { return vec![] };
        let Some(player) = self.objects.get(&me) else { return vec![] };
        let conditions = player.conditions();
        if has(conditions, condition::STUNNED) || has(conditions, condition::PAUSED) || has(conditions, condition::PETRIFIED) {
            return vec![];
        }
        let weapon_type = player.stat_int(STAT_INVENTORY_0).unwrap_or(-1);
        let Some(weapon) = (weapon_type > 0).then(|| data.objects.get(&(weapon_type as u16))).flatten() else {
            return vec![];
        };
        // imprints work on Enthroned gear only, as the server has it
        let imprints = if weapon.xml.has("Ascended") {
            imprint::Shot::of(imprint::parse(player.stat_str(STAT_INVENTORY_DATA_0).unwrap_or("")), weapon.xml.int("SlotType", 0), weapon.num_projectiles())
        } else {
            imprint::Shot::default()
        };
        let attack_period = 1.0 / self.attack_frequency(player.stat_int(STAT_DEXTERITY).unwrap_or(0), conditions)
            * (1.0 / (weapon.rate_of_fire() * imprints.rate_mult));
        if (time as f64) < player.attack_start as f64 + attack_period {
            return vec![];
        }
        let att_mult = self.attack_multiplier(player.stat_int(STAT_ATTACK).unwrap_or(0), conditions);
        // a weapon whose volleys alternate fires its projectiles in turn
        self.weapon_volleys += 1;
        let alternate = weapon.xml.has("AlternateProjectiles") && weapon.projectiles.len() > 1;
        let bullet_type = if alternate { (self.weapon_volleys - 1).rem_euclid(weapon.projectiles.len() as i32) } else { 0 };
        let mut shots = self.do_shoot_with(time, weapon_type, bullet_type, angle, att_mult, last_move, data, Some(&imprints));
        // the Battle Mage's bolts: two beside the shot, at 40% of the wand's rate
        if imprints.battle_mage && self.last_bolts.is_none_or(|t| time as f64 >= t as f64 + attack_period / imprint::BATTLE_MAGE_RATE) {
            if let Some(&bolt) = data.object_ids.get(imprint::BATTLE_MAGE_BOLT) {
                self.last_bolts = Some(time);
                let arc = imprint::BATTLE_MAGE_ARC * PI / 180.0;
                for a in [angle - arc, angle + arc] {
                    shots.extend(self.bolt(time, bolt, a, last_move, data));
                }
            }
        }
        if let Some(player) = self.objects.get_mut(&me) {
            player.note_attack(time, angle);
        }
        shots
    }

    /// A Battle Mage bolt: its object's projectile, its damage rolled as the server rolls it, with
    /// no attack multiplier.
    fn bolt(&mut self, time: i32, bolt: u16, angle: f64, last_move: i32, data: &GameData) -> Option<Projectile> {
        let me = self.me?;
        let player = self.objects.get(&me)?;
        let desc = data.objects.get(&bolt)?.projectiles.get(&0)?;
        let (px, py) = (player.x, player.y);
        let speed_mult = if has(player.conditions(), condition::INSPIRED) { 1.25 } else { 1.0 };
        let bullet_id = self.next_bullet_id;
        self.next_bullet_id += 1;
        let mut damage = self.random.next_int_range(desc.min_damage as u32, desc.max_damage as u32) as i32;
        if time > last_move + 600 {
            damage = 0;
        }
        let p = Projectile::new(me, bullet_id, bolt, 0, angle, time, px + angle.cos() * 0.3, py + angle.sin() * 0.3, speed_mult, damage, false);
        self.projectiles.push(p.clone());
        Some(p)
    }

    /// The shots of an ability that has `<Activate>Shoot</Activate>` (`Player.useAltWeapon`):
    /// the item's projectiles. The AIR client leaves out the attack multiplier here, but the
    /// server (`Player.PlayerShoot`) applies it to every shot, so we do too.
    pub fn shoot_ability(&mut self, time: i32, item_type: i32, angle: f64, last_move: i32, data: &GameData) -> Vec<Projectile> {
        let Some(player) = self.player() else { return vec![] };
        let att_mult = self.attack_multiplier(player.stat_int(STAT_ATTACK).unwrap_or(0), player.conditions());
        self.do_shoot_with(time, item_type, 0, angle, att_mult, last_move, data, None)
    }

    /// `Player.doShoot`: the item's projectiles fanned around `angle`, each rolling its damage.
    /// The weapon's imprints, if any, change their damage (on the roll, as the server does), their
    /// number, how far they fly and whether they pierce.
    #[allow(clippy::too_many_arguments)]
    fn do_shoot_with(
        &mut self,
        time: i32,
        item_type: i32,
        bullet_type: i32,
        angle: f64,
        att_mult: f64,
        last_move: i32,
        data: &GameData,
        imprints: Option<&imprint::Shot>,
    ) -> Vec<Projectile> {
        let Some(me) = self.me else { return vec![] };
        let Some(player) = self.objects.get(&me) else { return vec![] };
        let Some(item) = data.objects.get(&(item_type as u16)) else { return vec![] };
        let Some(desc) = item.projectiles.get(&bullet_type) else { return vec![] };
        let (px, py) = (player.x, player.y);
        let speed_mult = if has(player.conditions(), condition::INSPIRED) { 1.25 } else { 1.0 };
        let (min, max) = (desc.min_damage, desc.max_damage);
        let num = imprints.map_or(item.num_projectiles(), |i| i.num_projectiles(item.num_projectiles()));
        let arc_gap = item.arc_gap_degrees() * PI / 180.0;
        let mut a = angle - arc_gap * (num - 1) as f64 / 2.0;
        let mut shots = Vec::new();
        for _ in 0..num {
            let bullet_id = self.next_bullet_id;
            self.next_bullet_id += 1;
            let roll = self.random.next_int_range(min as u32, max as u32) as i32;
            let roll = imprints.map_or(roll, |i| i.damage(roll));
            // uint × Number assigned to an int: truncated.
            let mut damage = (roll as f64 * att_mult) as i32;
            if time > last_move + 600 {
                damage = 0;
            }
            let (sx, sy) = (px + angle.cos() * 0.3, py + angle.sin() * 0.3);
            let mut p = Projectile::new(me, bullet_id, item_type as u16, bullet_type, a, time, sx, sy, speed_mult, damage, false);
            if let Some(i) = imprints {
                let life = i.lifetime(desc.speed, desc.lifetime);
                p.life = (life != desc.lifetime).then_some(life);
                p.no_pierce = i.no_pierce;
            }
            shots.push(p.clone());
            self.projectiles.push(p);
            a += arc_gap;
        }
        shots
    }

    /// Move every projectile to `time`; drop the finished ones; report hits. Also ground damage.
    pub fn update_projectiles(&mut self, time: i32, data: &GameData) -> Vec<Hit> {
        let mut hits = Vec::new();
        for o in self.objects.values_mut() {
            if o.dead && time - o.dead_at > UNCONFIRMED_KILL_MS {
                o.dead = false;
            }
        }
        let mut projectiles = std::mem::take(&mut self.projectiles);
        projectiles.retain_mut(|p| self.step_projectile(p, time, data, &mut hits));
        self.projectiles = projectiles;
        self.ground_damage(time, data, &mut hits);
        hits
    }

    /// `Projectile.update`: false when the projectile is done.
    fn step_projectile(&mut self, p: &mut Projectile, time: i32, data: &GameData, hits: &mut Vec<Hit>) -> bool {
        let Some(desc) = self.projectile_desc(p, data) else { return false };
        let elapsed = time - p.start_time;
        if elapsed > p.life.unwrap_or(desc.lifetime) {
            return false;
        }
        let (x, y) = p.position_at(desc, elapsed);
        let inside = x >= 0.0 && y >= 0.0 && x < self.width as f64 && y < self.height as f64;
        let (sx, sy) = (x as i32, y as i32);
        if !inside || self.tile(sx, sy).unwrap_or(NO_TILE) == NO_TILE {
            if p.damages_players {
                hits.push(Hit::Square { time, bullet_id: p.bullet_id, owner_id: p.owner_id });
            }
            return false;
        }
        p.x = x;
        p.y = y;
        // Blocked by what stands on the square.
        if let Some(obstacle) = self.occupant(sx, sy) {
            if let Some(od) = data.objects.get(&obstacle.object_type) {
                let damages_enemies = !p.damages_players;
                if (!od.enemy || !damages_enemies) && (od.enemy_occupy_square || (!desc.passes_cover && od.occupy_square)) {
                    if p.damages_players {
                        hits.push(Hit::Other { time, bullet_id: p.bullet_id, owner_id: p.owner_id, target_id: obstacle.id });
                    } else {
                        let pet_owner = self.pet_shot(p, data).then_some(p.owner_id);
                        self.impacts.push(Impact { x, y, target: None, kill: false, look: desc.object_id.clone(), angle: p.angle, speed: desc.speed, pet_owner });
                    }
                    return false;
                }
            }
        }
        let Some(target_id) = self.hit_target(p, x, y, desc, data) else { return true };
        let me = self.me;
        let target = &self.objects[&target_id];
        let tdesc = data.objects.get(&target.object_type);
        let target_is_enemy = tdesc.is_some_and(|d| d.enemy);
        let send = (me.is_some() && p.damages_players) || (target_is_enemy && Some(p.owner_id) == me);
        let pet_owner = self.pet_shot(p, data).then_some(p.owner_id);
        let impact = Impact { x, y, target: Some(target_id), kill: false, look: desc.object_id.clone(), angle: p.angle, speed: desc.speed, pet_owner };
        self.impacts.push(impact);
        if send {
            let defense = target.stat_int(STAT_DEFENSE).unwrap_or_else(|| tdesc.map_or(0, |d| d.xml.int("Defense", 0)));
            let dmg = damage_with_defense(p.damage, defense, desc.armor_piercing, target.conditions());
            let dead = target.stat_int(STAT_HP).is_some_and(|hp| hp <= dmg);
            if Some(target_id) == me {
                hits.push(Hit::Player { bullet_id: p.bullet_id, owner_id: p.owner_id, damage: dmg, pierced: desc.armor_piercing });
            } else if target_is_enemy {
                hits.push(Hit::Enemy { time, bullet_id: p.bullet_id, target_id, kill: dead, damage: dmg, pierced: desc.armor_piercing });
                if let Some(i) = self.impacts.last_mut() {
                    i.kill = dead;
                }
                if dead {
                    if let Some(t) = self.objects.get_mut(&target_id) {
                        t.dead = true;
                        t.dead_at = time;
                    }
                }
            } else if !desc.multi_hit || p.no_pierce {
                hits.push(Hit::Other { time, bullet_id: p.bullet_id, owner_id: p.owner_id, target_id });
            }
        }
        if desc.multi_hit && !p.no_pierce {
            p.hit.insert(target_id);
            true
        } else {
            false
        }
    }

    /// `Projectile.getHit`: us if we're in reach, otherwise the nearest target in reach.
    fn hit_target(&self, p: &Projectile, x: f64, y: f64, desc: &ProjectileDesc, data: &GameData) -> Option<i32> {
        let mut best: Option<(f64, i32)> = None;
        for go in self.objects.values() {
            let (dx, dy) = ((go.x - x).abs(), (go.y - y).abs());
            if dx > HIT_RADIUS || dy > HIT_RADIUS {
                continue;
            }
            if desc.multi_hit && p.hit.contains(&go.id) {
                continue;
            }
            let c = go.conditions();
            if go.dead || has(c, condition::INVINCIBLE) || has(c, condition::PAUSED) || has(c, condition::STASIS) {
                continue;
            }
            let Some(gd) = data.objects.get(&go.object_type) else { continue };
            if (!p.damages_players && gd.enemy) || (p.damages_players && gd.player) {
                if Some(go.id) == self.me {
                    return Some(go.id);
                }
                let dist = (dx * dx + dy * dy).sqrt();
                if best.is_none_or(|(d, _)| dist < d) {
                    best = Some((dist, go.id));
                }
            }
        }
        best.map(|(_, id)| id)
    }

    /// `Player.update`'s ground damage: every 500 ms on damaging ground, unless invincible or
    /// protected by what stands there.
    fn ground_damage(&mut self, time: i32, data: &GameData, hits: &mut Vec<Hit>) {
        let Some(me) = self.player() else { return };
        let (x, y) = (me.x, me.y);
        let (sx, sy) = (x as i32, y as i32);
        let Some(ground) = self.tile(sx, sy).and_then(|t| data.grounds.get(&t)) else { return };
        if ground.max_damage <= 0 || has(me.conditions(), condition::INVINCIBLE) {
            return;
        }
        let protected = self
            .occupant(sx, sy)
            .and_then(|o| data.objects.get(&o.object_type))
            .is_some_and(|d| d.xml.has("ProtectFromGroundDamage"));
        let last = self.last_ground_damage.get(&(sx, sy)).copied().unwrap_or(i32::MIN / 2);
        if protected || last + 500 >= time {
            return;
        }
        let damage = self.random.next_int_range(ground.min_damage as u32, ground.max_damage as u32) as i32;
        self.last_ground_damage.insert((sx, sy), time);
        hits.push(Hit::Ground { time, x, y, damage });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn desc() -> ProjectileDesc {
        ProjectileDesc {
            bullet_type: 0,
            object_id: "Blade".into(),
            lifetime: 1000,
            speed: 100,
            size: -1,
            min_damage: 10,
            max_damage: 20,
            effects: vec![],
            multi_hit: false,
            passes_cover: false,
            armor_piercing: false,
            wavy: false,
            parametric: false,
            boomerang: false,
            amplitude: 0.0,
            frequency: 1.0,
            magnitude: 3.0,
        }
    }

    fn proj(bullet_id: i32, angle: f64) -> Projectile {
        Projectile::new(1, bullet_id, 0, 0, angle, 0, 10.0, 10.0, 1.0, 0, false)
    }

    fn close(a: (f64, f64), b: (f64, f64)) -> bool {
        (a.0 - b.0).abs() < 1e-9 && (a.1 - b.1).abs() < 1e-9
    }

    #[test]
    fn straight_path() {
        // Speed 100 = 0.01 tiles/ms.
        assert!(close(proj(1, 0.0).position_at(&desc(), 500), (15.0, 10.0)));
        assert!(close(proj(1, PI / 2.0).position_at(&desc(), 1000), (10.0, 20.0)));
    }

    #[test]
    fn boomerang_comes_back() {
        let d = ProjectileDesc { boomerang: true, ..desc() };
        // Halfway is 5 tiles out (at 500 ms); at 750 ms it's back to 2.5.
        assert!(close(proj(1, 0.0).position_at(&d, 500), (15.0, 10.0)));
        assert!(close(proj(1, 0.0).position_at(&d, 750), (12.5, 10.0)));
    }

    #[test]
    fn wavy_and_parametric_alternate_by_bullet_id() {
        let w = ProjectileDesc { wavy: true, ..desc() };
        let even = proj(2, 0.0).position_at(&w, 100);
        let odd = proj(1, 0.0).position_at(&w, 100);
        assert!((even.1 - 10.0) * (odd.1 - 10.0) < 0.0, "opposite sides: {even:?} {odd:?}");
        let pm = ProjectileDesc { parametric: true, ..desc() };
        // At a quarter of its life: sin(π/2) = 1 forward (sign by id), sin(π) = 0 sideways.
        let p = proj(1, 0.0).position_at(&pm, 250);
        assert!(close(p, (13.0, 10.0)), "{p:?}");
        let p = proj(2, 0.0).position_at(&pm, 250);
        assert!(close(p, (7.0, 10.0)), "{p:?}");
    }

    #[test]
    fn amplitude_deflects_sideways() {
        let d = ProjectileDesc { amplitude: 1.0, frequency: 1.0, ..desc() };
        // At a quarter of the lifetime the sine peaks: one tile to the side.
        let p = proj(2, 0.0).position_at(&d, 250);
        assert!(close(p, (12.5, 11.0)), "{p:?}");
    }

    #[test]
    fn defense_rules() {
        assert_eq!(damage_with_defense(100, 30, false, [0, 0]), 70);
        // At least 10% gets through.
        assert_eq!(damage_with_defense(100, 95, false, [0, 0]), 10);
        assert_eq!(damage_with_defense(100, 95, true, [0, 0]), 100);
        // Armored: defence ×1.5 (truncated): 45.
        assert_eq!(damage_with_defense(100, 30, false, [1 << (condition::ARMORED - 1), 0]), 55);
        // Curse: ×1.25.
        assert_eq!(damage_with_defense(100, 0, false, [0, 1 << (condition::CURSE - 32)]), 125);
    }
}
