//! Headless test client: logs in to an account server, joins the nexus with the first living
//! character and keeps the connection alive, answering the server as the real client does.
//! Use it against the isolated test servers only.
//!
//!     bsk-bot --account http://127.0.0.1:8082 --credentials <file> [--seconds 60] [--char <id>]
//!             [--gateway ws://127.0.0.1:8095/ws] [--say "/godlands;;/god"] [--wander <tiles>]
//!
//! The credentials file has `email: ...` and `password: ...` lines (test-account.local.txt).
//! With `--gateway`, the game connection goes through the gateway's WebSocket bridge, as the
//! browser client's does. It follows the server's Reconnects (a world change, or a rolling
//! deploy moving it to the new world server) as the client does, each through a new connection.
//! The exit code is 0 if it stayed connected for the whole time.
//!
//! `--say` sends chat lines or commands (split by `;;`), one 3 s after each world is joined or
//! 3 s after the last. `--wander` walks the bot round a circle of that radius about where it
//! joined each world, so several bots keep parts of a realm awake for load tests.


use bsk_net::{account, http, login, GameSocket, SocketEvent, Target};
use bsk_proto::{ClientMessage, PlayerText, ServerMessage, WorldPos};
use bsk_session::{Event, HelloParams, Join, Phase, Session, NEXUS_GAME_ID};
use std::collections::BTreeMap;
use std::time::{Duration, Instant};

struct Args {
    account: String,
    credentials: String,
    seconds: u64,
    char_id: Option<i32>,
    gateway: Option<String>,
    say: Vec<String>,
    wander: f32,
}

fn parse_args() -> Result<Args, String> {
    let mut args =
        Args { account: String::new(), credentials: String::new(), seconds: 60, char_id: None, gateway: None, say: Vec::new(), wander: 0.0 };
    let mut it = std::env::args().skip(1);
    while let Some(a) = it.next() {
        let mut value = || it.next().ok_or(format!("{a} needs a value"));
        match a.as_str() {
            "--account" => args.account = value()?,
            "--credentials" => args.credentials = value()?,
            "--seconds" => args.seconds = value()?.parse().map_err(|e| format!("--seconds: {e}"))?,
            "--char" => args.char_id = Some(value()?.parse().map_err(|e| format!("--char: {e}"))?),
            "--gateway" => args.gateway = Some(value()?),
            "--say" => args.say = value()?.split(";;").map(|l| l.trim().to_owned()).filter(|l| !l.is_empty()).collect(),
            "--wander" => args.wander = value()?.parse().map_err(|e| format!("--wander: {e}"))?,
            _ => return Err(format!("unknown argument {a}")),
        }
    }
    if args.account.is_empty() || args.credentials.is_empty() {
        return Err("usage: bsk-bot --account <url> --credentials <file> [--seconds N] [--char ID]".into());
    }
    Ok(args)
}

fn read_credentials(path: &str) -> Result<(String, String), String> {
    let text = std::fs::read_to_string(path).map_err(|e| format!("{path}: {e}"))?;
    let field = |name: &str| {
        text.lines()
            .find_map(|l| l.strip_prefix(name).and_then(|r| r.strip_prefix(':')).map(|v| v.trim().to_owned()))
            .ok_or(format!("{path}: no '{name}:' line"))
    };
    Ok((field("email")?, field("password")?))
}

fn wait<T>(pending: bsk_net::Pending<T>) -> T {
    loop {
        if let Some(v) = pending.take() {
            return v;
        }
        std::thread::sleep(Duration::from_millis(5));
    }
}

fn run(args: Args) -> Result<(), String> {
    let (email, password) = read_credentials(&args.credentials)?;
    let base = args.account.trim_end_matches('/');

    let xml = wait(http::post_form(&format!("{base}/char/list"), &[("guid", &email), ("password", &password)]))?;
    let list = account::parse_char_list(&xml)?;
    // with no living character (a fresh test account), a new Wizard
    let ch = match args.char_id {
        Some(id) => Some(list.chars.iter().find(|c| c.id == id).ok_or(format!("no character {id}"))?),
        None => list.chars.iter().find(|c| !c.dead),
    };
    let server = list.servers.first().ok_or("the account server lists no world servers")?;
    match ch {
        Some(ch) => log::info!(
            "character {} (type 0x{:04x}, level {}), server {} at {}:{}",
            ch.id, ch.object_type, ch.level, server.name, server.host, server.port
        ),
        None => log::info!("no living character: a new Wizard, server {} at {}:{}", server.name, server.host, server.port),
    }
    let mut char_id = ch.map(|c| c.id);
    let join = |char_id: Option<i32>| match char_id {
        Some(char_id) => Join::Load { char_id },
        None => Join::Create { class_type: 0x030e, skin_type: 0 },
    };

    // Through the gateway the same way the desktop client goes (wss with BSK_CA, as in its tests).
    let mut target = Target { host: server.host.clone(), port: server.port, gateway: args.gateway.clone() };
    let mut socket = GameSocket::connect(&target);
    let started = Instant::now();
    let now = || started.elapsed().as_millis() as i32;
    let (guid, password) = (login::encrypt(&email), login::encrypt(&password));
    let hello = |game_id: i32, key_time: i32, key: Vec<u8>| HelloParams {
        build_version: login::BUILD_VERSION.into(),
        game_id,
        guid: guid.clone(),
        password: password.clone(),
        key_time,
        key,
    };
    let (mut session, first) = Session::start(hello(NEXUS_GAME_ID, -1, vec![]), join(char_id));
    socket.send(&first);

    let mut received: BTreeMap<&'static str, u32> = BTreeMap::new();
    let mut sent: BTreeMap<&'static str, u32> = BTreeMap::new();
    let mut objects = std::collections::HashSet::new();
    let mut out: Vec<ClientMessage> = Vec::new();
    let deadline = started + Duration::from_secs(args.seconds);
    let mut next_report = started + Duration::from_secs(10);
    // The round trips the server measured (each Ping carries its recent average), this report.
    let mut rtts: Vec<i32> = Vec::new();
    // Gaps between NewTicks as they arrive here (100 ms when nothing holds them up), this report.
    let mut last_tick: Option<Instant> = None;
    let mut tick_gaps: Vec<u128> = Vec::new();
    let mut reconnects = 0;
    // --say: the lines still to send and when the next may go; --wander: since when it walks
    let mut say = args.say.iter();
    let mut next_say: Option<Instant> = None;
    let mut circle: Option<Instant> = None;
    let mut last_step = Instant::now();

    let result = loop {
        if Instant::now() >= deadline {
            break Ok(());
        }
        if next_say.is_some_and(|t| Instant::now() >= t) {
            next_say = None;
            if let Some(line) = say.next() {
                log::info!("saying {line}");
                socket.send(&PlayerText { text: line.clone() }.into());
                next_say = Some(Instant::now() + Duration::from_secs(3));
            }
        }
        if args.wander > 0.0 && last_step.elapsed() >= Duration::from_millis(100) {
            last_step = Instant::now();
            if let (Some(since), Some(p)) = (circle, session.position) {
                // 3 tiles a second, turning so a lap is a circle of that radius; stepping from
                // where we are follows the server's teleports (Goto)
                let angle = since.elapsed().as_secs_f32() * 3.0 / args.wander;
                let pos = WorldPos { x: p.x - 0.3 * angle.sin(), y: p.y + 0.3 * angle.cos() };
                session.set_position(pos, now());
            }
        }
        let Some(event) = socket.poll() else {
            std::thread::sleep(Duration::from_millis(2));
            if Instant::now() >= next_report {
                next_report += Duration::from_secs(10);
                let ping = match (rtts.iter().min(), rtts.iter().max()) {
                    (Some(lo), Some(hi)) => {
                        format!(", ping {lo}-{hi} ms (avg {})", rtts.iter().sum::<i32>() / rtts.len() as i32)
                    }
                    _ => String::new(),
                };
                rtts.clear();
                tick_gaps.sort_unstable();
                let gaps = match tick_gaps.last() {
                    Some(max) => format!(
                        ", tick gaps p50 {} p90 {} max {max} ms, {} over 150",
                        tick_gaps[tick_gaps.len() / 2],
                        tick_gaps[tick_gaps.len() * 9 / 10],
                        tick_gaps.iter().filter(|&&g| g > 150).count()
                    ),
                    None => String::new(),
                };
                tick_gaps.clear();
                log::info!(
                    "{:>3} s: {} ticks, {} objects seen{ping}{gaps}",
                    started.elapsed().as_secs(),
                    received.get("NewTick").unwrap_or(&0),
                    objects.len()
                );
            }
            continue;
        };
        match event {
            SocketEvent::Connected => log::info!("connected"),
            SocketEvent::Closed(why) => break Err(format!("disconnected after {} ms: {why}", now())),
            SocketEvent::Message(msg) => {
                *received.entry(msg.name()).or_default() += 1;
                match &*msg {
                    ServerMessage::Ping(p) if p.round_trip_time >= 0 => rtts.push(p.round_trip_time),
                    ServerMessage::NewTick(_) => {
                        if let Some(last) = last_tick.replace(Instant::now()) {
                            tick_gaps.push(last.elapsed().as_millis());
                        }
                    }
                    ServerMessage::Update(u) => {
                        objects.extend(u.new_objs.iter().map(|o| o.status.object_id));
                        for o in u.new_objs.iter() {
                            log::trace!("object {} type 0x{:04x} at ({}, {})", o.status.object_id, o.object_type, o.status.pos.x, o.status.pos.y);
                        }
                        if let Some(me) = u.new_objs.iter().find(|o| Some(o.status.object_id) == session.object_id) {
                            let stats: Vec<String> =
                                me.status.stats.iter().map(|s| format!("{}={:?}", s.id, s.value)).collect();
                            log::debug!("our stats: {}", stats.join(" "));
                        }
                    }
                    ServerMessage::Text(t) if !t.text.is_empty() => {
                        log::info!("chat <{}> {}", t.name, t.text)
                    }
                    _ => {}
                }
                match session.handle(&msg, now(), &mut out) {
                    Some(Event::MapInfo(m)) => log::info!("map {} ({}×{})", m.name, m.width, m.height),
                    Some(Event::Joined { object_id, char_id: id }) => {
                        log::info!("joined as object {object_id}");
                        char_id = Some(id);
                        next_say = Some(Instant::now() + Duration::from_secs(3));
                        circle = None;
                    }
                    Some(Event::Failure { error_id, description }) => {
                        break Err(format!("failure {error_id}: {description}"))
                    }
                    Some(Event::Reconnect(r)) => {
                        log::info!("reconnecting to {} (game {}{})", r.name, r.game_id,
                            if r.host.is_empty() { String::new() } else { format!(" at {}:{}", r.host, r.port) });
                        socket.close();
                        if !r.host.is_empty() {
                            target.host = r.host.clone();
                            target.port = r.port as u16;
                        }
                        socket = GameSocket::connect(&target);
                        last_tick = None;
                        let (s, first) = Session::start(hello(r.game_id, r.key_time, r.key.0.clone()), join(char_id));
                        session = s;
                        out.clear();
                        socket.send(&first);
                        reconnects += 1;
                        continue;
                    }
                    None => {}
                }
                if args.wander > 0.0 && circle.is_none() && session.phase == Phase::InGame {
                    circle = Some(Instant::now());
                }
                for m in out.drain(..) {
                    *sent.entry(m.name()).or_default() += 1;
                    socket.send(&m);
                }
            }
        }
    };

    log::info!("received: {received:?}");
    log::info!("sent: {sent:?}");
    if session.phase != Phase::InGame {
        return Err(result.err().unwrap_or_else(|| format!("never got into the game ({:?})", session.phase)));
    }
    result?;
    log::info!("stayed connected for {} s at {:?} ({reconnects} world changes)", args.seconds, session.position);
    socket.close();
    Ok(())
}

fn main() {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info")).init();
    let code = match parse_args().and_then(run) {
        Ok(()) => 0,
        Err(e) => {
            log::error!("{e}");
            1
        }
    };
    std::process::exit(code);
}
