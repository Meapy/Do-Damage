//! `--fight`: the bot plays as a player does, with the client's own game logic (`bsk-world`):
//! it keeps the world the server describes, circles the nearest enemy and shoots it with its
//! weapon at the weapon's rate of fire, uses its ability when it has the mana, and tells the
//! server what its shots and the enemies' hit, as the client does. For load tests that look like
//! real fights: projectiles, hits, abilities and damage, not just bots standing about.

use bsk_data::GameData;
use bsk_proto::{
    ClientMessage, EnemyHit, GroundDamage, OtherHit, PlayerHit, PlayerShoot, ServerMessage, SlotObject, SquareHit, UseItem,
    WorldPos,
};
use bsk_world::{Hit, MoveInput, Projectile, World};

const STAT_MP: u8 = 4;
const STAT_INVENTORY_0: u8 = 8;

/// At most how far (tiles) the bot keeps from what it fights, circling it; nearer for a weapon
/// that doesn't reach so far (swords, daggers).
const RANGE: f64 = 6.0;
/// Enemies further than this are left alone.
const SEEK: f64 = 14.0;

pub struct Fighter {
    world: Option<World>,
    next_ability: i32,
    /// which way round it circles, flipped now and then
    turn: f64,
    turned_at: i32,
    pub shots: u32,
    pub hits: u32,
    pub abilities: u32,
}

impl Fighter {
    pub fn new() -> Self {
        Self { world: None, next_ability: 0, turn: 1.0, turned_at: 0, shots: 0, hits: 0, abilities: 0 }
    }

    /// A Reconnect: the old world is gone (the client drops it too). Fighting on with it sent shots
    /// on the new connection before the server had a player for it, which it refuses.
    pub fn reset(&mut self) {
        self.world = None;
    }

    pub fn map(&mut self, width: i32, height: i32, name: &str, seed: u32) {
        self.world = Some(World::new(width, height, name, seed));
    }

    pub fn joined(&mut self, object_id: i32) {
        if let Some(w) = &mut self.world {
            w.me = Some(object_id);
        }
    }

    /// After the session answered a message: a Move went out (the world's walk check needs it).
    pub fn moved(&mut self, data: &GameData) {
        if let Some(w) = &mut self.world {
            w.on_move(data);
        }
    }

    /// The world's part of a server message, as the client's `Game` does it.
    pub fn handle(&mut self, msg: &ServerMessage, now: i32, data: &GameData) {
        let Some(world) = &mut self.world else { return };
        match msg {
            ServerMessage::Update(u) => world.apply_update(u, data, now),
            ServerMessage::NewTick(t) => world.apply_new_tick(t, now),
            ServerMessage::Goto(g) => world.apply_goto(g.object_id, g.pos.x as f64, g.pos.y as f64, now, data),
            ServerMessage::EnemyShoot(e) => {
                let Some(owner) = world.objects.get(&e.owner_id).filter(|o| !o.dead) else { return };
                let container = owner.object_type;
                let (num, inc) = e.spread.map_or((1, 0.0), |s| (s.num_shots as i32, s.angle_inc as f64));
                for i in 0..num {
                    let angle = e.angle as f64 + inc * i as f64;
                    let (x, y) = (e.starting_pos.x as f64, e.starting_pos.y as f64);
                    world.add_projectile(Projectile::new(e.owner_id, e.bullet_id + i, container, e.bullet_type as i32, angle, now, x, y, 1.0, e.damage, true));
                }
            }
            ServerMessage::ServerPlayerShoot(s) => {
                if world.objects.get(&s.owner_id).is_some_and(|o| !o.dead) {
                    let (x, y) = (s.starting_pos.x as f64, s.starting_pos.y as f64);
                    world.add_projectile(Projectile::new(s.owner_id, s.bullet_id, s.container_type as u16, s.bullet_type.unwrap_or(0) as i32, s.angle as f64, now, x, y, 1.0, s.damage, false));
                }
            }
            _ => {}
        }
    }

    /// One frame: walk, shoot, use the ability, move the projectiles. Returns the new position for
    /// the session's Moves (if it moved) and the messages to send.
    pub fn frame(&mut self, now: i32, dt: i32, last_move: i32, data: &GameData, out: &mut Vec<ClientMessage>) -> Option<WorldPos> {
        let world = self.world.as_mut()?;
        let me = world.player()?;
        let (mx, my, id) = (me.x, me.y, me.id);
        let mp = me.stat_int(STAT_MP).unwrap_or(0);
        let ability = me.stat_int(STAT_INVENTORY_0 + 1).unwrap_or(-1);
        // most of the weapon's reach (speed is tenths of a tile a second, lifetime ms)
        let weapon = me.stat_int(STAT_INVENTORY_0).unwrap_or(-1);
        let reach = data
            .objects
            .get(&(weapon as u16))
            .and_then(|d| d.projectiles.get(&0))
            .map_or(RANGE, |p| p.speed as f64 * p.lifetime as f64 / 10000.0);
        let range = (reach * 0.7).clamp(1.5, RANGE);
        // the nearest enemy in reach
        let target = world
            .objects
            .values()
            .filter(|o| !o.dead && data.objects.get(&o.object_type).is_some_and(|d| d.enemy))
            .map(|o| (o.x, o.y, (o.x - mx).hypot(o.y - my)))
            .filter(|t| t.2 < SEEK)
            .min_by(|a, b| a.2.total_cmp(&b.2));

        // walk: round the target at RANGE, or a slow circle when there's none
        if now - self.turned_at > 7000 {
            self.turn = -self.turn;
            self.turned_at = now;
        }
        let (ix, iy) = match target {
            Some((tx, ty, d)) => {
                let (ux, uy) = ((tx - mx) / d.max(0.01), (ty - my) / d.max(0.01));
                let radial = ((d - range) / range).clamp(-1.0, 1.0);
                (ux * radial - uy * self.turn, uy * radial + ux * self.turn)
            }
            None => {
                let a = now as f64 / 4000.0;
                (a.cos(), a.sin())
            }
        };
        let len = ix.hypot(iy).max(1e-6);
        let (mut dx, mut dy) = (ix / len, iy / len);
        // as a player would, keep off ground that hurts (the Arena's lava hurts even in god mode):
        // the nearest turn of the way it wanted whose next tile is safe
        let hurts = |x: f64, y: f64| {
            world.tile(x.floor() as i32, y.floor() as i32).and_then(|t| data.grounds.get(&t)).is_some_and(|g| g.max_damage > 0)
        };
        for turn in [0.0f64, 0.6, -0.6, 1.2, -1.2, 1.8, -1.8, std::f64::consts::PI] {
            let (c, s) = (turn.cos(), turn.sin());
            let (tx, ty) = (dx * c - dy * s, dx * s + dy * c);
            if !hurts(mx + tx, my + ty) && !hurts(mx + tx * 0.5, my + ty * 0.5) {
                (dx, dy) = (tx, ty);
                break;
            }
        }
        let input = MoveInput { x: dx, y: dy, camera_angle: 0.0 };
        let moved = world.update(now, dt, &input, data).map(|(x, y)| WorldPos { x: x as f32, y: y as f32 });

        if let Some((tx, ty, _)) = target {
            let angle = (ty - my).atan2(tx - mx);
            for p in world.shoot(now, angle, last_move, data) {
                self.shots += 1;
                out.push(shot(now, &p));
            }
            // the ability at the target, when it's ready and there's the mana
            if now >= self.next_ability && ability > 0 {
                if let Some(xml) = data.objects.get(&(ability as u16)).map(|d| &d.xml) {
                    if xml.has("Usable") && xml.int("MpCost", 0) <= mp {
                        self.next_ability = now + ((xml.number("Cooldown", 0.5) * 1000.0) as i32).max(500);
                        self.abilities += 1;
                        out.push(
                            UseItem {
                                time: now,
                                slot_object: SlotObject { object_id: id, slot_id: 1, object_type: ability },
                                item_use_pos: WorldPos { x: tx as f32, y: ty as f32 },
                                use_type: 1,
                            }
                            .into(),
                        );
                        if xml.children_named("Activate").any(|a| a.text.trim() == "Shoot") {
                            for p in world.shoot_ability(now, ability, angle, last_move, data) {
                                out.push(shot(now, &p));
                            }
                        }
                    }
                }
            }
        }

        for hit in world.update_projectiles(now, data) {
            self.hits += 1;
            out.push(match hit {
                Hit::Player { bullet_id, owner_id, .. } => PlayerHit { bullet_id, object_id: owner_id }.into(),
                Hit::Enemy { time, bullet_id, target_id, kill, .. } => EnemyHit { time, bullet_id, target_id, kill }.into(),
                Hit::Other { time, bullet_id, owner_id, target_id } => OtherHit { time, bullet_id, object_id: owner_id, target_id }.into(),
                Hit::Square { time, bullet_id, owner_id } => SquareHit { time, bullet_id, object_id: owner_id }.into(),
                Hit::Ground { time, x, y, .. } => GroundDamage { time, position: WorldPos { x: x as f32, y: y as f32 } }.into(),
            });
        }
        moved
    }
}

fn shot(now: i32, p: &Projectile) -> ClientMessage {
    PlayerShoot {
        time: now,
        bullet_id: p.bullet_id,
        container_type: p.container_type as i32,
        starting_pos: WorldPos { x: p.start_x as f32, y: p.start_y as f32 },
        angle: p.angle as f32,
    }
    .into()
}
