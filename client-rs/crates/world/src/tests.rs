use super::*;
use bsk_proto::{GroundTile, ObjectStatus, WorldPos};

const FLOOR: u16 = 1;
const WATER: u16 = 2;
const WALL: u16 = 0x10;
const PLAYER: u16 = 0x20;

fn data() -> GameData {
    let mut d = GameData::default();
    d.add_grounds(
        r#"<Grounds>
            <Ground type="0x01" id="Floor"><Texture><File>t</File><Index>0</Index></Texture></Ground>
            <Ground type="0x02" id="Water"><NoWalk/></Ground>
            <Ground type="0xff" id="Empty"><NoWalk/></Ground>
        </Grounds>"#,
    )
    .unwrap();
    d.add_objects(
        r#"<Objects>
            <Object type="0x10" id="Wall"><Class>Wall</Class><Static/><OccupySquare/><FullOccupy/></Object>
            <Object type="0x20" id="Hero"><Class>Player</Class><Player/></Object>
        </Objects>"#,
    )
    .unwrap();
    d
}

fn obj(id: i32, object_type: u16, x: f32, y: f32, speed: i32) -> bsk_proto::ObjectData {
    let stats = vec![StatData { id: 22, value: StatValue::Int(speed) }];
    ObjectData { object_type: object_type as i32, status: ObjectStatus { object_id: id, pos: WorldPos { x, y }, stats: stats.into() } }
}

/// A 10Ã—10 floor with a wall at (6, 5) and water at (5, 7); our player at (5.5, 5.5).
fn world(d: &GameData) -> World {
    let mut w = World::new(10, 10, "test", 1);
    let mut tiles = Vec::new();
    for y in 0..10 {
        for x in 0..10 {
            tiles.push(GroundTile { x, y, tile_type: if (x, y) == (5, 7) { WATER } else { FLOOR } as i32 });
        }
    }
    let update = Update {
        tiles: tiles.into(),
        new_objs: vec![obj(1, WALL, 6.5, 5.5, 0), obj(2, PLAYER, 5.5, 5.5, 75)].into(),
        drops: Default::default(),
    };
    w.apply_update(&update, d, 0);
    w.me = Some(2);
    w
}

fn walk(w: &mut World, d: &GameData, x: f64, y: f64, ms: i32) -> (f64, f64) {
    let input = MoveInput { x, y, camera_angle: 0.0 };
    for t in 0..ms / 10 {
        w.update(t * 10, 10, &input, d);
    }
    let p = w.player().unwrap();
    (p.x, p.y)
}

#[test]
fn speed_formula() {
    // 75 speed: the maximum, 0.0096 tiles/ms; 0 speed: 0.004.
    assert!((move_speed(75, [0, 0], 1.0) - 0.0096).abs() < 1e-12);
    assert!((move_speed(0, [0, 0], 1.0) - 0.004).abs() < 1e-12);
    // Slowed ignores the stat.
    assert!((move_speed(75, [1 << 3, 0], 0.5) - 0.002).abs() < 1e-12);
    // Speedy: Ã—1.35.
    assert!((move_speed(0, [1 << 14, 0], 1.0) - 0.0054).abs() < 1e-12);
}

#[test]
fn walking_east_stops_at_the_wall() {
    let d = data();
    let mut w = world(&d);
    let (x, y) = walk(&mut w, &d, 1.0, 0.0, 1000);
    // The wall in square 6 is FullOccupy, so isValidPosition refuses any x past the middle of
    // our square: we can't get closer than half a tile to it.
    assert_eq!((x, y), (5.5, 5.5));
    // From further away we walk up to that same point.
    let mut w = world(&d);
    w.apply_goto(2, 3.2, 5.5, 0, &d);
    let (x, _) = walk(&mut w, &d, 1.0, 0.0, 1000);
    assert_eq!(x, 5.5);
}

#[test]
fn walking_west_is_free() {
    let d = data();
    let mut w = world(&d);
    let (x, _) = walk(&mut w, &d, -1.0, 0.0, 200);
    // 200 ms at 0.0096 tiles/ms.
    assert!((x - (5.5 - 1.92)).abs() < 1e-9, "x = {x}");
}

#[test]
fn water_blocks_walking_south() {
    let d = data();
    let mut w = world(&d);
    let (_, y) = walk(&mut w, &d, 0.0, 1.0, 1000);
    assert!((y - 6.99).abs() < 1e-9, "y = {y}");
}

#[test]
fn short_steps_into_water_never_back_off() {
    // 1 ms steps (a high frame rate), heading south and a little east: we slide along the
    // water's edge without being pushed back from it and creeping up again every frame.
    let d = data();
    let mut w = world(&d);
    w.apply_goto(2, 5.05, 6.7, 0, &d);
    let input = MoveInput { x: 0.3, y: 1.0, camera_angle: 0.0 };
    let mut last = w.player().unwrap().y;
    for t in 0..200 {
        w.update(t, 1, &input, &d);
        let y = w.player().unwrap().y;
        assert!(y >= last && y < 7.0, "y went from {last} to {y} at {t} ms");
        last = y;
    }
    assert!(last > 6.98, "y = {last}");
}

#[test]
fn our_walk_frames_keep_going_in_a_0_ms_frame() {
    let d = data();
    let mut w = world(&d);
    let input = MoveInput { x: -1.0, y: 0.0, camera_angle: 0.0 };
    w.update(0, 5, &input, &d);
    w.update(5, 0, &input, &d);
    assert!(w.player().unwrap().moving);
    w.update(5, 5, &MoveInput { x: 0.0, y: 0.0, camera_angle: 0.0 }, &d);
    assert!(!w.player().unwrap().moving);
}

#[test]
fn quick_shots_still_show_both_attack_frames() {
    let d = data();
    let mut w = world(&d);
    let me = w.me.unwrap();
    let o = w.objects.get_mut(&me).unwrap();
    // A shot every 60 ms, far quicker than a pass through the frames: sampled every 10 ms, the
    // second frame (past half way) still shows, and for at least half the quickest pass at a time.
    let (mut second, mut run, mut longest) = (0, 0, 0);
    for t in (1000..3000).step_by(10) {
        if t % 60 == 40 {
            o.note_attack(t, 0.0);
        }
        let p = o.attack_phase(t).expect("attacking");
        if p >= 0.5 {
            second += 1;
            run += 10;
        } else {
            longest = longest.max(run);
            run = 0;
        }
    }
    assert!(second > 50, "second frame shown {second} times");
    assert!(longest >= MIN_ANIM_CYCLE / 2, "longest second frame {longest} ms");
    // Shots slower than a pass start it over each time, as before.
    let o = w.objects.get_mut(&me).unwrap();
    o.note_attack(10_000, 0.0);
    o.note_attack(10_400, 0.0);
    assert_eq!(o.attack_phase(10_400), Some(0.0));
    assert!(o.attack_phase(10_400 + ATTACK_PERIOD).is_none());
}

#[test]
fn others_glide_to_their_tick_position() {
    let d = data();
    let mut w = world(&d);
    let mut other = obj(3, PLAYER, 2.0, 2.0, 0);
    other.status.stats = Default::default();
    w.apply_update(&Update { new_objs: vec![other].into(), ..Default::default() }, &d, 0);
    let status = ObjectStatus { object_id: 3, pos: WorldPos { x: 3.0, y: 2.0 }, stats: Default::default() };
    w.apply_new_tick(&NewTick { tick_id: 1, tick_time: 200, statuses: vec![status].into() }, 1000);
    // The glide takes 1.3 ticks (260 ms).
    w.update(1130, 130, &MoveInput::default(), &d);
    let o = &w.objects[&3];
    assert!((o.x - 2.5).abs() < 1e-9 && o.moving, "halfway after half the glide: {}", o.x);
    w.update(1260, 130, &MoveInput::default(), &d);
    assert!((w.objects[&3].x - 3.0).abs() < 1e-9);
    // A late tick: it waits at the target instead of running past it.
    w.update(1400, 140, &MoveInput::default(), &d);
    let o = &w.objects[&3];
    assert!((o.x - 3.0).abs() < 1e-9 && !o.moving, "{}", o.x);
}

#[test]
fn walkers_keep_their_walk_frames_through_a_late_tick() {
    let d = data();
    let mut w = world(&d);
    let mut other = obj(3, PLAYER, 2.0, 2.0, 0);
    other.status.stats = Default::default();
    w.apply_update(&Update { new_objs: vec![other].into(), ..Default::default() }, &d, 0);
    let to = |x: f32| ObjectStatus { object_id: 3, pos: WorldPos { x, y: 2.0 }, stats: Default::default() };
    w.apply_new_tick(&NewTick { tick_id: 1, tick_time: 100, statuses: vec![to(2.5)].into() }, 1000);
    // Its glide ends at 1130 and the next tick is late: it waits at 2.5, still walking.
    w.update(1180, 50, &MoveInput::default(), &d);
    let o = &w.objects[&3];
    assert!((o.x - 2.5).abs() < 1e-9 && o.moving, "{} {}", o.x, o.moving);
    assert!((o.walk_speed - 0.005).abs() < 1e-9, "{}", o.walk_speed);
    // A tick that leaves it where it is: it stands at once.
    w.apply_new_tick(&NewTick { tick_id: 2, tick_time: 100, statuses: vec![to(2.5)].into() }, 1190);
    w.update(1191, 1, &MoveInput::default(), &d);
    assert!(!w.objects[&3].moving);
}

#[test]
fn a_new_tick_glides_on_from_where_it_is() {
    let d = data();
    let mut w = world(&d);
    let mut other = obj(3, PLAYER, 2.0, 2.0, 0);
    other.status.stats = Default::default();
    w.apply_update(&Update { new_objs: vec![other].into(), ..Default::default() }, &d, 0);
    let to = |x: f32| ObjectStatus { object_id: 3, pos: WorldPos { x, y: 2.0 }, stats: Default::default() };
    w.apply_new_tick(&NewTick { tick_id: 1, tick_time: 100, statuses: vec![to(3.0)].into() }, 1000);
    w.update(1065, 65, &MoveInput::default(), &d);
    let mid = w.objects[&3].x;
    // The next tick comes early, and another without this object: no jump either time.
    w.apply_new_tick(&NewTick { tick_id: 2, tick_time: 100, statuses: vec![to(4.0)].into() }, 1065);
    w.update(1066, 1, &MoveInput::default(), &d);
    assert!((w.objects[&3].x - mid).abs() < 0.02, "{mid} -> {}", w.objects[&3].x);
    w.apply_new_tick(&NewTick { tick_id: 3, tick_time: 100, statuses: vec![].into() }, 1100);
    w.update(1101, 35, &MoveInput::default(), &d);
    let x = w.objects[&3].x;
    assert!(x > mid && x < 4.0, "still gliding toward 4: {x}");
}

#[test]
fn our_own_status_moves_nothing() {
    let d = data();
    let mut w = world(&d);
    let status = ObjectStatus {
        object_id: 2,
        pos: WorldPos { x: 1.0, y: 1.0 },
        stats: vec![StatData { id: 22, value: StatValue::Int(10) }].into(),
    };
    w.apply_new_tick(&NewTick { tick_id: 1, tick_time: 200, statuses: vec![status].into() }, 100);
    let p = w.player().unwrap();
    assert_eq!((p.x, p.y), (5.5, 5.5));
    assert_eq!(p.stat_int(22), Some(10));
}

#[test]
fn dropped_objects_free_their_square() {
    let d = data();
    let mut w = world(&d);
    assert!(w.occupant(6, 5).is_some());
    w.apply_update(&Update { drops: vec![1].into(), ..Default::default() }, &d, 0);
    assert!(w.occupant(6, 5).is_none());
    let (x, _) = walk(&mut w, &d, 1.0, 0.0, 100);
    assert!(x > 6.0, "the wall is gone, so we cross into square 6: {x}");
}

#[test]
fn a_steady_walker_never_stops_or_backs_up_when_ticks_come_late() {
    let d = data();
    let mut w = world(&d);
    let mut other = obj(3, PLAYER, 2.0, 2.0, 0);
    other.status.stats = Default::default();
    w.apply_update(&Update { new_objs: vec![other].into(), ..Default::default() }, &d, 0);
    // The server moves it 0.4 tiles a 100 ms tick; each tick reaches us 0 to 25 ms late.
    let late = [0, 25, 5, 20, 0, 15, 25, 10, 0, 20];
    let mut next = 0;
    let mut last_x = 2.0;
    for frame in 0..60 {
        let now = 1000 + frame * 16;
        while next < late.len() && 1000 + next as i32 * 100 + late[next] <= now {
            let x = 2.4 + 0.4 * next as f32;
            let status = ObjectStatus { object_id: 3, pos: WorldPos { x, y: 2.0 }, stats: Default::default() };
            w.apply_new_tick(&NewTick { tick_id: next as i32 + 1, tick_time: 100, statuses: vec![status].into() }, now);
            next += 1;
        }
        w.update(now, 16, &MoveInput::default(), &d);
        let x = w.objects[&3].x;
        if frame > 0 && next < late.len() {
            assert!(x > last_x, "frame {frame}: stopped or went back ({last_x} -> {x})");
        }
        last_x = x;
    }
}

#[test]
fn a_kill_the_server_never_confirms_is_taken_back() {
    let mut d = data();
    d.add_objects(
        r#"<Objects>
            <Object type="0x30" id="Pirate"><Class>Character</Class><Enemy/></Object>
            <Object type="0x40" id="Slasher"><Projectile id="0"><ObjectId>Blade</ObjectId><Speed>100</Speed><LifetimeMS>1000</LifetimeMS><Damage>10</Damage></Projectile></Object>
        </Objects>"#,
    )
    .unwrap();
    let mut w = world(&d);
    // A monster with 10 health two tiles north of us; a 10-damage shot looks like a kill.
    let mut pirate = obj(3, 0x30, 5.5, 3.5, 0);
    pirate.status.stats = vec![StatData { id: 1, value: StatValue::Int(10) }].into();
    w.apply_update(&Update { new_objs: vec![pirate].into(), ..Default::default() }, &d, 0);
    let shoot = |w: &mut World, id: i32, at: i32| {
        w.add_projectile(combat::Projectile::new(2, id, 0x40, 0, -std::f64::consts::FRAC_PI_2, at, 5.5, 5.5, 1.0, 10, false));
        let mut hits = Vec::new();
        for t in (at..at + 400).step_by(10) {
            hits.extend(w.update_projectiles(t, &d));
        }
        hits
    };
    let hits = shoot(&mut w, 1, 0);
    assert!(matches!(hits[..], [combat::Hit::Enemy { target_id: 3, kill: true, .. }]), "{hits:?}");
    assert!(w.objects[&3].dead);
    // While we wait for the server to remove it, shots pass through it.
    assert!(shoot(&mut w, 2, 500).is_empty());
    // The server never removed it (it dealt less than we thought): it can be shot again.
    let hits = shoot(&mut w, 3, 1500);
    assert!(matches!(hits[..], [combat::Hit::Enemy { target_id: 3, .. }]), "{hits:?}");
}
