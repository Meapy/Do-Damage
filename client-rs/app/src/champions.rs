//! The Hall of Champions: the living's leaderboard in the Nexus, beside the Legends (the dead's).
//! The account server ranks every account on six boards every few minutes (its
//! `ChampionsController`, `Shared/champions`), and each board's top 10 hold a reward of its own
//! kind while they stay there (#1, #2-3 and #4-10 a step each), which the world server applies.
//!
//! Here: the window the Champions Monument opens (an NPC window, `npc::Window::Champions`): a tab
//! per board, its rewards, the podium of its top three with their characters, the rest of its top
//! 10, and the viewer's own place even off the list. And the Hall itself in the world: each
//! board's champion standing on their plinth round the monument (`Hall::draw`).

use crate::hud::Ui;
use crate::theme::{self, color};
use bsk_data::{ObjectClass, ObjectDesc, TextureRef};
use bsk_net::account::{ChampionBoard, ChampionEntry, Champions};
use bsk_render::{Quad, Tint};
use bsk_world::{GameObj, World};

pub const GOLD: u32 = 0xFFD24A;
pub const SILVER: u32 = 0xD3DEEA;
pub const BRONZE: u32 = 0xE0965A;

/// Places that hold a reward, and that the board lists (`ChampionBoards.Places`).
pub const PLACES: i32 = 10;

/// The plinths' tops, in their sprite's pixels above its foot (tools/draw_champions.py).
const PLINTH_TOP: f32 = 10.0;

/// Statues show their champions within this many tiles; the board is fetched again this often
/// while one is near, ms.
const RANGE: f64 = 16.0;
const REFRESH_MS: i32 = 60_000;

/// A place's colour: gold, silver or bronze for the three tiers, muted below.
pub fn tier_color(rank: i32) -> u32 {
    match rank {
        1 => GOLD,
        2 | 3 => SILVER,
        4..=PLACES => BRONZE,
        _ => color::TEXT_MUTED,
    }
}

/// A board's value as it reads: "Wave 31", "12,402 fame".
pub fn value_text(board: &ChampionBoard, value: i64) -> String {
    let n = crate::hud::group_digits(value.clamp(0, i32::MAX as i64) as i32);
    if board.unit == "wave" { format!("Wave {n}") } else { format!("{n} {}", board.unit) }
}

/// "just now", "4 min ago", "2 h ago".
pub fn ago(seconds: i64) -> String {
    match seconds.max(0) {
        s if s < 60 => "just now".to_owned(),
        s if s < 3600 => format!("{} min ago", s / 60),
        s => format!("{} h ago", s / 3600),
    }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// The object a champion's character is drawn from: their skin, else their class.
fn look<'a>(ui: &Ui<'a>, e: &ChampionEntry) -> Option<&'a ObjectDesc> {
    if e.object_type == 0 {
        return None;
    }
    let data = ui.data;
    (e.skin != 0).then(|| data.objects.get(&(e.skin as u16))).flatten().or_else(|| data.objects.get(&e.object_type))
}

/// The window's state (inside `npc::Window::Champions`).
#[derive(Clone, Debug, Default)]
pub struct View {
    pub data: Option<Champions>,
    pub tab: usize,
    /// A board to show once the data comes (a statue's, `--champions`), by id.
    pub want: Option<String>,
    pub error: Option<String>,
    /// When (game ms) `data` came: the countdowns run on from its `now`.
    pub received: i32,
}

impl View {
    pub fn at(board: Option<&str>) -> Self {
        View { want: board.map(str::to_owned), ..View::default() }
    }

    pub fn set(&mut self, data: Champions, now: i32) {
        if let Some(i) = self.want.as_deref().and_then(|w| data.boards.iter().position(|b| b.id.eq_ignore_ascii_case(w) || b.name.eq_ignore_ascii_case(w))) {
            self.tab = i;
            self.want = None;
        }
        self.tab = self.tab.min(data.boards.len().saturating_sub(1));
        (self.data, self.error, self.received) = (Some(data), None, now);
    }

    /// The server's clock now, unix seconds.
    pub fn server_now(&self, now: i32) -> i64 {
        self.data.as_ref().map_or(0, |d| d.now + ((now - self.received).max(0) / 1000) as i64)
    }

    /// The tab to the left (-1) or right (1), round the ends.
    pub fn step(&mut self, d: i32) {
        let n = self.data.as_ref().map_or(0, |d| d.boards.len()) as i32;
        if n > 0 {
            self.tab = (self.tab as i32 + d).rem_euclid(n) as usize;
        }
    }
}

/// A tab chip: the board's name, and the viewer's place on it while it holds a reward.
#[allow(clippy::too_many_arguments)]
fn chip(ui: &mut Ui, r: [f32; 4], label: &str, on: bool, hover: bool, size: f32, mine: Option<i32>, s: f32) {
    let fill = if on { color::ACCENT } else if hover { color::SECONDARY } else { color::CHIP };
    theme::fill(ui.renderer, r, r[3] / 2.0, fill, 1.0);
    let lw = ui.measure(label, size, true);
    let badge = mine.map(|m| format!("#{m}"));
    let bs = (10.5 * s).round().max(8.0);
    let bw = badge.as_ref().map_or(0.0, |b| ui.measure(b, bs, true) + 10.0 * s + 4.0 * s);
    let x = r[0] + (r[2] - lw - bw) / 2.0;
    ui.label(label, x, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
    if let (Some(b), Some(m)) = (badge, mine) {
        let br = [x + lw + 4.0 * s, r[1] + (r[3] - 16.0 * s) / 2.0, bw - 4.0 * s, 16.0 * s];
        theme::fill(ui.renderer, br, br[3] / 2.0, tier_color(m), 1.0);
        let tw = ui.measure(&b, bs, true);
        ui.label(&b, br[0] + (br[2] - tw) / 2.0, br[1] + (br[3] - bs * 1.22) / 2.0, bs, true, color::ACCENT_TEXT);
    }
}

/// The window's size, in interface units at scale 1.
pub const SIZE: (f32, f32) = (900.0, 680.0);

/// Draw the window's insides in the panel `r` (the NPC window draws the panel, its title and
/// the close button); returns the tabs, for clicks.
pub fn draw(ui: &mut Ui, view: &View, r: [f32; 4], s: f32, me: &str, mouse: (f64, f64), now: i32) -> Vec<(usize, [f32; 4])> {
    let font = |size: f32| (size * s).round().max(9.0);
    let mut hits = Vec::new();
    let x0 = r[0] + 22.0 * s;
    let w = r[2] - 44.0 * s;
    let Some(data) = view.data.as_ref().filter(|d| !d.pending && !d.boards.is_empty()) else {
        let (t, c) = match (&view.error, &view.data) {
            (Some(e), _) => (e.clone(), color::DANGER),
            (None, Some(_)) => ("The boards are being ranked. Look again in a minute.".to_owned(), color::TEXT_MUTED),
            (None, None) => ("Reading the names on the monument…".to_owned(), color::TEXT_MUTED),
        };
        ui.label(&t, x0, r[1] + 70.0 * s, font(14.0), false, c);
        return hits;
    };
    let server_now = view.server_now(now);

    // the tabs, a row under the title, smaller if they'd run over
    let mut cs = font(13.0);
    let tabs_x0 = x0;
    let tabs_w = w;
    let mine = |b: &ChampionBoard| b.me.map(|m| m.0).filter(|&m| (1..=PLACES).contains(&m));
    let gap = 6.0 * s;
    let (widths, total) = loop {
        let widths: Vec<f32> = data.boards.iter().map(|b| ui.measure(&b.name, cs, true) + if mine(b).is_some() { 58.0 } else { 26.0 } * s).collect();
        let total = widths.iter().sum::<f32>() + gap * (widths.len() - 1) as f32;
        if total <= tabs_w || cs <= 9.0 {
            break (widths, total);
        }
        cs -= 1.0;
    };
    let mut tx = (tabs_x0 + ((tabs_w - total) / 2.0).max(0.0)).round();
    for (i, (b, bw)) in data.boards.iter().zip(widths).enumerate() {
        let tr = [tx, r[1] + 52.0 * s, bw, 30.0 * s];
        chip(ui, tr, &b.name, i == view.tab, inside(tr, mouse), cs, mine(b), s);
        hits.push((i, tr));
        tx += bw + gap;
    }

    let board = &data.boards[view.tab.min(data.boards.len() - 1)];
    let mut y = r[1] + 98.0 * s;

    // what it ranks, and its period
    let period = if board.weekly {
        format!("Starts afresh in {}", crate::quests::countdown(data.week_reset - server_now))
    } else {
        "All time".to_owned()
    };
    let ps = font(12.5);
    let pw = ui.measure(&period, ps, true);
    let pr = [x0 + w - pw - 22.0 * s, y - 2.0 * s, pw + 22.0 * s, 22.0 * s];
    theme::fill(ui.renderer, pr, pr[3] / 2.0, if board.weekly { color::WARM_TOP } else { color::CHIP }, 1.0);
    ui.label(&period, pr[0] + 11.0 * s, pr[1] + (pr[3] - ps * 1.22) / 2.0, ps, true, if board.weekly { color::ACCENT_HI } else { color::TEXT_DIM });
    let desc = crate::npc::fit(ui, &board.description, font(13.0), pr[0] - x0 - 12.0 * s);
    ui.label(&desc, x0, y, font(13.0), false, color::TEXT_DIM);
    y += 30.0 * s;

    // the rewards: one tile per tier
    let rw = (w - 2.0 * 10.0 * s) / 3.0;
    for (t, (places, rgb, text)) in board.rewards.iter().take(3).enumerate() {
        let tile = [x0 + t as f32 * (rw + 10.0 * s), y, rw, 46.0 * s];
        let edge = [GOLD, SILVER, BRONZE][t];
        theme::frame(ui.renderer, tile, 8.0 * s, s.max(1.0), (edge, 0.75), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
        let medal = [tile[0] + 10.0 * s, tile[1] + 9.0 * s, 46.0 * s, 28.0 * s];
        theme::rounded(ui.renderer, medal, medal[3] / 2.0, (theme::shade(edge, 0.2), 1.0), (theme::shade(edge, -0.25), 1.0));
        let ms = font(12.0);
        let mw = ui.measure(places, ms, true);
        ui.label(places, medal[0] + (medal[2] - mw) / 2.0, medal[1] + (medal[3] - ms * 1.22) / 2.0, ms, true, color::ACCENT_TEXT);
        let text_c = if *rgb != 0 { *rgb } else { color::TEXT };
        let ts = font(13.0);
        let tx = medal[0] + medal[2] + 10.0 * s;
        let text = crate::npc::fit(ui, text, ts, tile[0] + tile[2] - tx - 8.0 * s);
        ui.label(&text, tx, tile[1] + (tile[3] - ts * 1.22) / 2.0, ts, true, text_c);
    }
    y += 58.0 * s;

    // the podium: #2, #1, #3
    let entry = |rank: i32| board.entries.iter().find(|e| e.rank == rank);
    let (cw1, cw2) = (300.0 * s, 250.0 * s);
    let (ch1, ch2) = (176.0 * s, 150.0 * s);
    let cx = x0 + w / 2.0;
    for (rank, cr) in [
        (2, [cx - cw1 / 2.0 - 14.0 * s - cw2, y + ch1 - ch2, cw2, ch2]),
        (1, [cx - cw1 / 2.0, y, cw1, ch1]),
        (3, [cx + cw1 / 2.0 + 14.0 * s, y + ch1 - ch2, cw2, ch2]),
    ] {
        podium(ui, board, entry(rank), rank, cr, me, s, now);
    }
    y += ch1 + 12.0 * s;

    // the rest of the top 10
    let row_h = 26.0 * s;
    let rs = font(12.5);
    for (i, rank) in (4..=PLACES).enumerate() {
        let ry = y + i as f32 * row_h;
        let rr = [x0 - 6.0 * s, ry, w + 12.0 * s, row_h - 2.0 * s];
        let e = entry(rank);
        let mine = e.is_some_and(|e| e.name.eq_ignore_ascii_case(me));
        if mine {
            theme::fill(ui.renderer, rr, 5.0 * s, color::ACCENT, 0.16);
        } else if i % 2 == 0 {
            theme::fill(ui.renderer, rr, 5.0 * s, 0xFFFFFF, 0.03);
        }
        let ty = ry + (row_h - 2.0 * s - rs * 1.22) / 2.0;
        ui.label(&format!("#{rank}"), x0, ty, rs, true, BRONZE);
        let Some(e) = e else {
            ui.label("—", x0 + 74.0 * s, ty, rs, false, color::TEXT_MUTED);
            continue;
        };
        if let Some(desc) = look(ui, e) {
            crate::screens::portrait(ui, desc, e.tex1, e.tex2, x0 + 52.0 * s, ry + row_h / 2.0, 0.55 * s);
        }
        ui.label(&e.name, x0 + 74.0 * s, ty, rs, true, if mine { color::ACCENT_HI } else { color::TEXT });
        if let Some(c) = class_line(ui, e) {
            ui.label(&c, x0 + 230.0 * s, ty, font(11.5), false, color::TEXT_MUTED);
        }
        let v = value_text(board, e.value);
        let vw = ui.measure(&v, rs, true);
        ui.label(&v, x0 + w - vw, ty, rs, true, color::TEXT_DIM);
    }
    y += (PLACES - 3) as f32 * row_h + 8.0 * s;

    // the viewer's own place
    let bar = [x0, y, w, 40.0 * s];
    let held = board.me.filter(|m| m.0 <= PLACES);
    theme::frame(
        ui.renderer,
        bar,
        8.0 * s,
        s.max(1.0),
        (if held.is_some() { color::GOLD } else { color::BORDER_HI }, 1.0),
        ((if held.is_some() { color::WARM_TOP } else { color::SLOT_TOP }, 1.0), (if held.is_some() { color::WARM_BOTTOM } else { color::SLOT_BOTTOM }, 1.0)),
    );
    let bs = font(13.5);
    let by = bar[1] + (bar[3] - bs * 1.22) / 2.0;
    let you = "You";
    ui.label(you, bar[0] + 14.0 * s, by, bs, true, color::ACCENT_HI);
    let tx = bar[0] + 14.0 * s + ui.measure(you, bs, true) + 14.0 * s;
    let (text, c) = match board.me {
        Some((rank, value)) if rank <= PLACES => {
            let tier = if rank == 1 { 0 } else if rank <= 3 { 1 } else { 2 };
            let reward = board.rewards.get(tier).map_or("", |r| r.2.as_str());
            (format!("#{rank}  ·  {}  ·  {reward}: yours while you hold the place", value_text(board, value)), color::GOOD)
        }
        Some((rank, value)) => {
            let goal = entry(PLACES).map(|e| format!(" Reach {} for a reward.", value_text(board, e.value + 1))).unwrap_or_default();
            (format!("#{rank} of {}  ·  {}.{goal}", board.count, value_text(board, value)), color::TEXT)
        }
        None => (format!("Not on this board yet. {}", hint(board)), color::TEXT_DIM),
    };
    let text = crate::npc::fit(ui, &text, bs, bar[0] + bar[2] - tx - 12.0 * s);
    ui.label(&text, tx, by, bs, true, c);

    // the small print
    let foot = format!(
        "Rewards last while you hold the place. Board loot boosts add up to at most +{}%, the Legends' +1% on top.  ·  Ranked {}, again in {}.",
        (data.loot_cap * 100.0).round(),
        ago(server_now - data.updated),
        crate::quests::countdown(data.next - server_now)
    );
    let fs = font(11.0);
    let foot = crate::npc::fit(ui, &foot, fs, w);
    ui.label(&foot, x0, r[1] + r[3] - 26.0 * s, fs, false, color::TEXT_MUTED);
    hits
}

/// How to get on a board, for the viewer who isn't.
fn hint(board: &ChampionBoard) -> &'static str {
    match board.id.as_str() {
        "fame" => "Earn fame on a character that stays alive.",
        "stars" => "Each class's best character earns stars at 20, 150, 400, 800 and 2,000 fame.",
        "achievements" => "Claim achievement tiers in the quest log (J).",
        "arena" => "Survive waves in the Arena (/arena) with your name on the run.",
        "weeklyFame" => "Earn fame this week: anything counts.",
        "weeklyDungeons" => "Clear dungeons this week: be near a boss when the last one falls.",
        _ => "",
    }
}

/// "Level 20 Wizard".
fn class_line(ui: &Ui, e: &ChampionEntry) -> Option<String> {
    let class = ui.data.objects.get(&e.object_type)?;
    Some(format!("Level {} {}", e.level.max(1), class.display_id))
}

/// One place on the podium: its card edged in the tier's colour, the champion's character, name
/// and value; an empty place says so.
#[allow(clippy::too_many_arguments)]
fn podium(ui: &mut Ui, board: &ChampionBoard, e: Option<&ChampionEntry>, rank: i32, cr: [f32; 4], me: &str, s: f32, now: i32) {
    let font = |size: f32| (size * s).round().max(9.0);
    let edge = tier_color(rank);
    let mine = e.is_some_and(|e| e.name.eq_ignore_ascii_case(me));
    if rank == 1 && e.is_some() {
        // a slow shimmer behind the champion's card
        let pulse = ((now as f32 / 700.0).sin() + 1.0) / 2.0;
        let g = 3.0 * s + pulse * 4.0 * s;
        theme::fill(ui.renderer, [cr[0] - g, cr[1] - g, cr[2] + 2.0 * g, cr[3] + 2.0 * g], 12.0 * s + g, GOLD, 0.10 + 0.08 * pulse);
    }
    theme::frame(ui.renderer, cr, 10.0 * s, (1.5 * s).max(1.0), (edge, if e.is_some() { 0.95 } else { 0.4 }), ((color::WARM_TOP, 0.9), (color::INSET_BOTTOM, 1.0)));
    // the place, top left
    let badge = [cr[0] + 10.0 * s, cr[1] + 10.0 * s, 34.0 * s, 22.0 * s];
    theme::rounded(ui.renderer, badge, badge[3] / 2.0, (theme::shade(edge, 0.2), 1.0), (theme::shade(edge, -0.25), 1.0));
    let t = format!("#{rank}");
    let bs = font(12.5);
    let bw = ui.measure(&t, bs, true);
    ui.label(&t, badge[0] + (badge[2] - bw) / 2.0, badge[1] + (badge[3] - bs * 1.22) / 2.0, bs, true, color::ACCENT_TEXT);
    let mid = cr[0] + cr[2] / 2.0;
    let Some(e) = e else {
        let t = "Unclaimed";
        let ts = font(13.0);
        let tw = ui.measure(t, ts, true);
        ui.label(t, mid - tw / 2.0, cr[1] + cr[3] / 2.0 - ts / 2.0, ts, true, color::TEXT_MUTED);
        return;
    };
    let big = rank == 1;
    let fig = if big { 2.0 } else { 1.6 } * s;
    if let Some(desc) = look(ui, e) {
        // a soft light under the figure
        let lw = 70.0 * fig / 2.0;
        theme::fill(ui.renderer, [mid - lw / 2.0, cr[1] + (if big { 82.0 } else { 70.0 }) * s, lw, 10.0 * s], 5.0 * s, edge, 0.18);
        crate::screens::portrait(ui, desc, e.tex1, e.tex2, mid, cr[1] + (if big { 54.0 } else { 46.0 }) * s, fig);
    }
    let ns = font(if big { 17.0 } else { 15.0 });
    let name = crate::npc::fit(ui, &e.name, ns, cr[2] - 20.0 * s);
    let nw = ui.measure(&name, ns, true);
    let mut ty = cr[1] + cr[3] - (if big { 72.0 } else { 64.0 }) * s;
    ui.label(&name, mid - nw / 2.0, ty, ns, true, if mine { color::ACCENT_HI } else { edge });
    ty += ns * 1.3;
    let v = value_text(board, e.value);
    let vs = font(if big { 14.0 } else { 13.0 });
    let vw = ui.measure(&v, vs, true);
    ui.label(&v, mid - vw / 2.0, ty, vs, true, color::TEXT);
    ty += vs * 1.35;
    if let Some(c) = class_line(ui, e) {
        let cs = font(11.0);
        let cw = ui.measure(&c, cs, false);
        ui.label(&c, mid - cw / 2.0, ty, cs, false, color::TEXT_MUTED);
    }
}

fn class_of<'a>(o: &GameObj, data: &'a bsk_data::GameData) -> Option<&'a str> {
    match &data.objects.get(&o.object_type)?.class {
        ObjectClass::Other(c) => Some(c.as_str()),
        _ => None,
    }
}

/// The board a statue stands for (its name, from the map), or none for the monument.
pub fn statue_board<'a>(o: &'a GameObj, data: &bsk_data::GameData) -> Option<&'a str> {
    (class_of(o, data) == Some("ChampionStatue")).then(|| o.name()).flatten()
}

/// The Hall in the world: what the statues show, fetched from the account server while one is
/// near (and taken from the window's fetches too).
#[derive(Default)]
pub struct Hall {
    pub data: Option<Champions>,
    next: i32,
    near: bool,
    pub request: Option<bsk_net::Pending<bsk_net::http::Response>>,
}

impl Hall {
    /// Whether to fetch the boards now (a statue was in range at the last frame and the last
    /// fetch is stale); marks it fetched.
    pub fn want(&mut self, now: i32) -> bool {
        if self.request.is_some() || now < self.next || !self.near {
            return false;
        }
        self.next = now + REFRESH_MS;
        true
    }

    pub fn set(&mut self, data: &Champions, now: i32) {
        if !data.pending {
            self.data = Some(data.clone());
            self.next = now + REFRESH_MS;
        }
    }

    /// Each statue's champion standing on its plinth, ringed in gold, with a plaque above:
    /// the board, the champion's name and value.
    pub fn draw(&mut self, ui: &mut Ui, world: &World, cam: &crate::view::Camera) {
        self.near = false;
        let Some(me) = world.player() else { return };
        let k = (cam.scale / crate::view::TILE_PX) as f32;
        let mut statues: Vec<&GameObj> = world
            .objects
            .values()
            .filter(|o| (o.x - me.x).hypot(o.y - me.y) <= RANGE && statue_board(o, ui.data).is_some())
            .collect();
        if statues.is_empty() {
            return;
        }
        self.near = true;
        // the farther first, so the nearer stand in front
        statues.sort_by(|a, b| a.y.total_cmp(&b.y));
        for o in statues {
            let id = statue_board(o, ui.data).unwrap_or("");
            let board = self.data.as_ref().and_then(|d| d.boards.iter().find(|b| b.id == id));
            let [px, py] = cam.to_screen(o.x, o.y, 0.0);
            let top = py - PLINTH_TOP * 5.0 * k;
            let champion = board.and_then(|b| b.entries.first());
            let mut head = top - 12.0 * k;
            if let Some(e) = champion {
                if let Some(h) = figure(ui, e, px, top, k) {
                    head = top - h;
                }
            }
            let Some(board) = board else { continue };
            plaque(ui, board, champion, px, head - 4.0 * k, k);
        }
    }
}

/// A champion's character standing at (px, feet), facing out, bigger than life and ringed in
/// gold; returns its height on the screen.
fn figure(ui: &mut Ui, e: &ChampionEntry, px: f32, feet: f32, k: f32) -> Option<f32> {
    let desc = look(ui, e)?;
    let TextureRef::Animated { set, index } = &desc.texture.pick(0).main else { return None };
    // row 2 faces the viewer when the sheet has it (right, left, down, up)
    let src = ui.sprites.char_src(set, *index, 2, 0).or_else(|| ui.sprites.char_src(set, *index, 0, 0))?;
    let mask = ui.sprites.char_mask_src(set, *index, 2, 0).or_else(|| ui.sprites.char_mask_src(set, *index, 0, 0));
    let dye = mask.map(|mask| crate::redraw::Dye { mask, tex1: e.tex1, tex2: e.tex2 });
    let img = ui.redrawer.get_glowing(ui.sprites, ui.renderer, src, false, 140, dye, GOLD)?;
    let (w, h) = (img.w as f32 * k, img.h as f32 * k);
    ui.renderer.sprite(img.rect, Quad::rect((px - w / 2.0).round(), (feet - h).round(), w, h), Tint::NONE);
    // the canvas has room for the outline above the sprite: the head is lower than its top
    Some(h - crate::redraw::PAD as f32 * k * 0.6)
}

/// The small card over a statue: the board's name, its champion and their value.
fn plaque(ui: &mut Ui, board: &ChampionBoard, champion: Option<&ChampionEntry>, px: f32, bottom: f32, k: f32) {
    let ts = (9.5 * k).round().max(8.0);
    let ns = (13.0 * k).round().max(9.0);
    let vs = (10.5 * k).round().max(8.0);
    let title = board.name.to_uppercase();
    let (name, value) = match champion {
        Some(e) => (e.name.clone(), value_text(board, e.value)),
        None => ("Unclaimed".to_owned(), "Be the first".to_owned()),
    };
    let w = [ui.measure(&title, ts, true), ui.measure(&name, ns, true), ui.measure(&value, vs, false)].into_iter().fold(0.0, f32::max) + 18.0 * k;
    let h = ts * 1.25 + ns * 1.25 + vs * 1.25 + 10.0 * k;
    let r = [(px - w / 2.0).round(), (bottom - h).round(), w.round(), h.round()];
    theme::shadow(ui.renderer, r, 6.0 * k, 8.0 * k, 0.4);
    theme::frame(ui.renderer, r, 6.0 * k, k.max(1.0), (GOLD, if champion.is_some() { 0.8 } else { 0.35 }), ((color::PANEL_TOP, 0.92), (color::PANEL_BOTTOM, 0.95)));
    let mut y = r[1] + 5.0 * k;
    for (t, size, bold, c) in [
        (&title, ts, true, color::GOLD),
        (&name, ns, true, if champion.is_some() { GOLD } else { color::TEXT_MUTED }),
        (&value, vs, false, color::TEXT_DIM),
    ] {
        let tw = ui.measure(t, size, bold);
        ui.label(t, px - tw / 2.0, y, size, bold, c);
        y += size * 1.25;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_net::account::ChampionBoard;

    fn data() -> Champions {
        let board = |id: &str| ChampionBoard { id: id.into(), name: id.into(), ..ChampionBoard::default() };
        Champions { now: 1000, boards: vec![board("fame"), board("stars"), board("arena")], ..Champions::default() }
    }

    #[test]
    fn opens_at_the_board_asked_for() {
        let mut v = View::at(Some("arena"));
        v.set(data(), 0);
        assert_eq!(v.tab, 2);
        assert!(v.want.is_none());
        // tabs go round the ends
        v.step(1);
        assert_eq!(v.tab, 0);
        v.step(-1);
        assert_eq!(v.tab, 2);
        // an unknown board leaves the first
        let mut v = View::at(Some("nothing"));
        v.set(data(), 0);
        assert_eq!(v.tab, 0);
    }

    #[test]
    fn the_clock_runs_on_from_the_answer() {
        let mut v = View::default();
        v.set(data(), 5_000);
        assert_eq!(v.server_now(65_000), 1060);
    }

    #[test]
    fn values_and_places_read_well() {
        let mut b = ChampionBoard { unit: "fame".into(), ..ChampionBoard::default() };
        assert_eq!(value_text(&b, 12402), "12,402 fame");
        b.unit = "wave".into();
        assert_eq!(value_text(&b, 31), "Wave 31");
        assert_eq!((tier_color(1), tier_color(3), tier_color(10)), (GOLD, SILVER, BRONZE));
        assert_eq!(tier_color(11), color::TEXT_MUTED);
        assert_eq!(ago(30), "just now");
        assert_eq!(ago(300), "5 min ago");
    }
}
