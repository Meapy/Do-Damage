//! Byte-exact tests against layouts written out by hand from the server's C# code.

use super::*;

/// Big-endian byte builder for expected frames.
#[derive(Default)]
struct B(Vec<u8>);

impl B {
    fn u8(mut self, v: u8) -> Self {
        self.0.push(v);
        self
    }
    fn i16(mut self, v: i16) -> Self {
        self.0.extend(v.to_be_bytes());
        self
    }
    fn i32(mut self, v: i32) -> Self {
        self.0.extend(v.to_be_bytes());
        self
    }
    fn f32(mut self, v: f32) -> Self {
        self.0.extend(v.to_be_bytes());
        self
    }
    fn utf(mut self, s: &str) -> Self {
        self.0.extend((s.len() as u16).to_be_bytes());
        self.0.extend(s.as_bytes());
        self
    }
    fn frame(self, id: u8) -> Vec<u8> {
        let mut out = ((self.0.len() + 5) as u32).to_be_bytes().to_vec();
        out.push(id);
        out.extend(self.0);
        out
    }
}

#[test]
fn hello_matches_hello_handler() {
    let msg = ClientMessage::Hello(Hello {
        build_version: "1.0".into(),
        game_id: -2,
        guid: "abc".into(),
        password: "pw".into(),
        key_time: -1,
        key: vec![9, 8].into(),
        map_json: LongString("{}".into()),
    });
    let expected = B::default()
        .utf("1.0")
        .i32(-2)
        .utf("abc")
        .utf("pw")
        .i32(-1)
        .i16(2)
        .u8(9)
        .u8(8)
        .i32(2)
        .u8(b'{')
        .u8(b'}')
        .frame(16);
    assert_eq!(msg.to_frame(), expected);
}

#[test]
fn move_matches_move_handler() {
    let msg = ClientMessage::Move(Move {
        tick_id: 7,
        time: 1000,
        new_position: WorldPos { x: 1.5, y: 2.5 },
        records: vec![MoveRecord { time: 990, x: 1.0, y: 2.0 }].into(),
    });
    let expected = B::default().i32(7).i32(1000).f32(1.5).f32(2.5).i16(1).i32(990).f32(1.0).f32(2.0).frame(4);
    assert_eq!(msg.to_frame(), expected);
}

#[test]
fn update_ack_is_an_empty_body() {
    assert_eq!(ClientMessage::UpdateAck(UpdateAck {}).to_frame(), vec![0, 0, 0, 5, 10]);
}

#[test]
fn update_with_string_and_int_stats() {
    let body = B::default()
        .i16(1)
        .i16(3)
        .i16(4)
        .i32(0x1234) // tile
        .i16(1)
        .i32(0x030e) // object type
        .i32(77) // object id
        .f32(10.5)
        .f32(20.5)
        .i16(3)
        .u8(31) // name: a string
        .utf("Tester")
        .u8(0) // max hp: an int
        .i32(700)
        .u8(122) // first item data slot: a string
        .utf("{}")
        .i16(1)
        .i32(99) // drop
        .0;
    let msg = ServerMessage::decode(9, &body).unwrap();
    let ServerMessage::Update(u) = &msg else { panic!("{msg:?}") };
    assert_eq!(u.tiles[0], GroundTile { x: 3, y: 4, tile_type: 0x1234 });
    let obj = &u.new_objs[0];
    assert_eq!(obj.object_type, 0x030e);
    assert_eq!(obj.status.object_id, 77);
    assert_eq!(obj.status.pos, WorldPos { x: 10.5, y: 20.5 });
    assert_eq!(obj.status.stats[0], StatData { id: 31, value: StatValue::Str("Tester".into()) });
    assert_eq!(obj.status.stats[1], StatData { id: 0, value: StatValue::Int(700) });
    assert_eq!(obj.status.stats[2], StatData { id: 122, value: StatValue::Str("{}".into()) });
    assert_eq!(*u.drops, vec![99]);
    // And back to the same bytes.
    let mut w = wire::Writer::default();
    msg.encode_body(&mut w);
    assert_eq!(w.buf, body);
}

#[test]
fn map_info_matches_server() {
    let body = B::default()
        .i16(256)
        .i16(128)
        .utf("Nexus")
        .utf("{nexus}")
        .i32(-559038737) // seed 0xDEADBEEF
        .u8(1)
        .u8(2)
        .u8(1)
        .u8(0)
        .utf("Nexus")
        .u8(0)
        .u8(1)
        .0;
    let ServerMessage::MapInfo(m) = ServerMessage::decode(23, &body).unwrap() else { panic!() };
    assert_eq!((m.width, m.height), (256, 128));
    assert_eq!(m.display_name, "{nexus}");
    assert_eq!(m.seed, 0xDEADBEEF);
    assert!(m.allow_player_teleport && !m.show_displays && m.disable_abilities && !m.disable_shooting);
}

#[test]
fn enemy_shoot_spread_is_optional() {
    let short = B::default().i32(1).i32(2).u8(3).f32(4.0).f32(5.0).f32(0.5).i32(60);
    let ServerMessage::EnemyShoot(e) = ServerMessage::decode(51, &short.0).unwrap() else { panic!() };
    assert_eq!(e.spread, None);
    let full = B::default().i32(1).i32(2).u8(3).f32(4.0).f32(5.0).f32(0.5).i32(60).u8(5).f32(0.25);
    let ServerMessage::EnemyShoot(e) = ServerMessage::decode(51, &full.0).unwrap() else { panic!() };
    assert_eq!(e.spread, Some(ShotSpread { num_shots: 5, angle_inc: 0.25 }));
    assert_eq!(e.starting_pos, WorldPos { x: 4.0, y: 5.0 });
}

#[test]
fn server_player_shoot_bullet_type_is_optional() {
    let short = B::default().i32(1).i32(2).i32(3).f32(4.0).f32(5.0).f32(0.5).i32(60);
    let ServerMessage::ServerPlayerShoot(s) = ServerMessage::decode(7, &short.0).unwrap() else { panic!() };
    assert_eq!(s.bullet_type, None);
    let full = B::default().i32(1).i32(2).i32(3).f32(4.0).f32(5.0).f32(0.5).i32(60).u8(2);
    let ServerMessage::ServerPlayerShoot(s) = ServerMessage::decode(7, &full.0).unwrap() else { panic!() };
    assert_eq!((s.bullet_type, s.damage, s.container_type), (Some(2), 60, 3));
}

#[test]
fn damage_has_a_byte_counted_effect_list() {
    let body = B::default().i32(5).u8(2).u8(3).u8(9).i32(120).u8(1).i32(4).i32(6).u8(0).0;
    let ServerMessage::Damage(d) = ServerMessage::decode(8, &body).unwrap() else { panic!() };
    assert_eq!(*d.effects, vec![3, 9]);
    assert_eq!((d.damage_amount, d.kill, d.bullet_id, d.object_id, d.pierce), (120, true, 4, 6, false));
}

#[test]
fn truncated_message_is_an_error() {
    let body = B::default().i32(1).0;
    assert!(ServerMessage::decode(1, &body).is_err());
}

#[test]
fn unknown_ids_are_kept() {
    let msg = ServerMessage::decode(200, &[1, 2, 3]).unwrap();
    assert_eq!(msg, ServerMessage::Unknown { id: 200, body: vec![1, 2, 3] });
    assert_eq!(msg.to_frame(), vec![0, 0, 0, 8, 200, 1, 2, 3]);
}

#[test]
fn frames_split_across_reads() {
    let a = ServerMessage::Ping(Ping { serial: 1, round_trip_time: 2 }).to_frame();
    let b = ServerMessage::QuestObjId(QuestObjId { object_id: 3 }).to_frame();
    let stream: Vec<u8> = a.iter().chain(&b).copied().collect();
    for split in 0..stream.len() {
        let mut dec = FrameDecoder::default();
        let mut got = Vec::new();
        for chunk in [&stream[..split], &stream[split..]] {
            dec.push(chunk);
            while let Some((id, body)) = dec.next_frame().unwrap() {
                got.push(ServerMessage::decode(id, body).unwrap());
            }
        }
        assert_eq!(got.len(), 2, "split at {split}");
        assert_eq!(got[1], ServerMessage::QuestObjId(QuestObjId { object_id: 3 }));
    }
}

#[test]
fn bad_frame_length_is_an_error() {
    let mut dec = FrameDecoder::default();
    dec.push(&[0, 0, 0, 2, 1]);
    assert!(dec.next_frame().is_err());
}

#[test]
fn every_client_message_round_trips() {
    let msgs: Vec<ClientMessage> = vec![
        Create { class_type: 782, skin_type: 0 }.into(),
        PlayerShoot { time: 1, bullet_id: 2, container_type: 3, starting_pos: WorldPos { x: 1.0, y: 2.0 }, angle: 0.5 }
            .into(),
        InvSwap {
            time: 1,
            position: WorldPos::default(),
            slot_object1: SlotObject { object_id: 1, slot_id: 4, object_type: -1 },
            slot_object2: SlotObject { object_id: 2, slot_id: 5, object_type: 0xa00 },
        }
        .into(),
        AcceptTrade { my_offer: vec![true, false].into(), your_offer: vec![false].into() }.into(),
        MarketAdd { slots: vec![4, 5].into(), price: 100, currency: 0, hours: 24 }.into(),
        PlayerText { text: "héllo /tell".into() }.into(),
        ViewRadius { radius: 22 }.into(),
        Forge { result: 0x497a, slots: vec![4, 13].into() }.into(),
        ForgeSell { slots: vec![6].into() }.into(),
    ];
    for msg in msgs {
        let frame = msg.to_frame();
        let back = ClientMessage::decode(frame[4], &frame[5..]).unwrap();
        assert_eq!(back, msg);
    }
}

#[test]
fn forge_messages_match_forge_handlers() {
    // ForgeData.Write: a short count of recipes (result, fame, advanced, a byte count of
    // ingredients), then a short count of prices (item, fame).
    let body = B::default().i16(1).i16(0x0a0f).i32(500).u8(1).u8(2).i16(0x0a01).i16(0x0a02).i16(1).i16(0x0a01).i32(30).0;
    let ServerMessage::ForgeData(d) = ServerMessage::decode(96, &body).unwrap() else { panic!() };
    assert_eq!((d.recipes[0].result, d.recipes[0].fame, d.recipes[0].advanced), (0x0a0f, 500, true));
    assert_eq!(*d.recipes[0].items, vec![0x0a01, 0x0a02]);
    assert_eq!((d.prices[0].item, d.prices[0].fame), (0x0a01, 30));
    // ForgeResult.Write: sold, success, item, fame, message.
    let body = B::default().u8(0).u8(1).i16(0x0a0f).i32(250).utf("").0;
    let ServerMessage::ForgeResult(r) = ServerMessage::decode(99, &body).unwrap() else { panic!() };
    assert_eq!((r.sold, r.success, r.item, r.fame), (false, true, 0x0a0f, 250));
    // ForgeHandler reads the result, then a byte count of slots.
    let frame = ClientMessage::from(Forge { result: 0x0a0f, slots: vec![4, 5].into() }).to_frame();
    assert_eq!(frame, B::default().i16(0x0a0f).u8(2).u8(4).u8(5).frame(97));
    // ForgeShards.Write: a short count of shards (item, count); ForgeWithdrawHandler reads the item.
    let body = B::default().i16(1).i16(0x5844).i32(7).0;
    let ServerMessage::ForgeShards(s) = ServerMessage::decode(100, &body).unwrap() else { panic!() };
    assert_eq!((s.shards[0].item, s.shards[0].count), (0x5844, 7));
    let frame = ClientMessage::from(ForgeWithdraw { item: 0x5844 }).to_frame();
    assert_eq!(frame, B::default().i16(0x5844).frame(101));
    // ForgeVaultItems.Write: a short count of items (item, count).
    let body = B::default().i16(1).i16(0x0a01).i32(3).0;
    let ServerMessage::ForgeVault(v) = ServerMessage::decode(102, &body).unwrap() else { panic!() };
    assert_eq!((v.items[0].item, v.items[0].count), (0x0a01, 3));
}

#[test]
fn quest_messages_match_quest_handlers() {
    // QuestsData.Write: now, rank, points, colour rank, ranks (name, points, colour), periods
    // (period, resets, swaps left, bonus claimed, bonus), quests (period, id, title, text,
    // progress, goal, claimed, rewards), the login track (day, ready, streak, total, next, days
    // (milestone, rewards)), achievements (id, category, title, text, claimed, progress, goals,
    // next points, next rewards). A reward is kind, item, amount.
    let reward = |b: B, kind: u8, item: i16, amount: i32| b.u8(kind).i16(item).i32(amount);
    let mut b = B::default().i32(1_800_000_000).u8(1).i32(120).u8(255);
    b = b.u8(1).utf("Adventurer").i32(100).i32(0xCD8A4E);
    b = b.u8(1).u8(0).i32(1_800_003_600).u8(1).u8(0).u8(1);
    b = reward(b, 1, 0x0c69, 1);
    b = b.u8(1).u8(0).utf("d_gods").utf("Godslayer's Errand").utf("Defeat 40 gods.").i32(12).i32(40).u8(0).u8(1);
    b = reward(b, 0, 0, 150);
    b = b.u8(3).u8(1).i32(4).i32(9).i32(1_800_003_600).u8(1).u8(1).u8(1);
    b = reward(b, 0, 0, 50);
    b = b.u8(1).utf("oryx").utf("Combat").utf("Oryx's Bane").utf("Help defeat Oryx 10 times.").u8(1).i32(3).u8(2).i32(1).i32(10).i32(20).u8(1);
    b = reward(b, 0, 0, 100);
    let ServerMessage::QuestsData(d) = ServerMessage::decode(104, &b.0).unwrap() else { panic!() };
    assert_eq!((d.now, d.rank, d.points, d.color_rank), (1_800_000_000, 1, 120, 255));
    assert_eq!((d.ranks[0].name.as_str(), d.ranks[0].color), ("Adventurer", 0xCD8A4E));
    assert_eq!((d.periods[0].rerolls_left, d.periods[0].bonus[0].item), (1, 0x0c69));
    assert_eq!((d.quests[0].id.as_str(), d.quests[0].progress, d.quests[0].goal, d.quests[0].rewards[0].amount), ("d_gods", 12, 40, 150));
    assert_eq!((d.login_day, d.login_ready, d.login_streak, d.login_total), (3, true, 4, 9));
    assert!(d.login_days[0].milestone);
    assert_eq!((d.achievements[0].claimed, d.achievements[0].progress, &*d.achievements[0].goals), (1, 3, &vec![1, 10]));
    assert_eq!(d.achievements[0].next_points, 20);
    // QuestResult, QuestNoticeMessage, QuestProgress (a short count of kind, index, progress).
    let ServerMessage::QuestResult(r) = ServerMessage::decode(106, &B::default().u8(1).utf("Done!").0).unwrap() else { panic!() };
    assert!(r.success && r.message == "Done!");
    let ServerMessage::QuestNotice(n) = ServerMessage::decode(107, &B::default().u8(1).utf("Achievement earned!").utf("x").0).unwrap() else { panic!() };
    assert_eq!((n.kind, n.title.as_str()), (1, "Achievement earned!"));
    let ServerMessage::QuestProgress(p) = ServerMessage::decode(108, &B::default().i16(1).u8(1).i16(5).i32(77).0).unwrap() else { panic!() };
    assert_eq!((p.changes[0].kind, p.changes[0].index, p.changes[0].progress), (1, 5, 77));
    // QuestClaimHandler reads a byte, then a string; QuestsRequest is empty.
    let frame = ClientMessage::from(QuestClaim { kind: 3, id: "oryx".into() }).to_frame();
    assert_eq!(frame, B::default().u8(3).utf("oryx").frame(105));
    assert_eq!(ClientMessage::from(QuestsRequest {}).to_frame(), B::default().frame(103));
}
