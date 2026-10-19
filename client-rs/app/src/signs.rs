//! Signs: small information cards that float over objects in the world while the player is
//! near, like a speech balloon that stays (and never goes to the chat). The Nexus's Arena
//! Leaderboard trophy shows the best run on one.
//!
//! A sign is a title and a few lines (`Sign`), drawn by `draw` over any object; to give
//! another object one, fetch what it should say into `Signs` and add it to `Signs::draw`.

use crate::hud::Ui;
use crate::theme::{self, color};
use bsk_data::ObjectClass;
use bsk_net::account::ArenaRun;
use bsk_world::{GameObj, World};

/// Signs show for objects at most this many tiles from the player.
pub const RANGE: f64 = 11.0;

/// How often a sign's contents are fetched again while one is in range, ms.
const REFRESH_MS: i32 = 60_000;

/// One line of a sign: wrapped to the card's width, centred.
pub struct Line {
    pub text: String,
    /// In world pixels at the normal zoom (the speech balloons' text is 14).
    pub size: f32,
    pub bold: bool,
    pub color: u32,
    /// At most this many lines once wrapped; the last is cut with an ellipsis.
    pub max_lines: usize,
}

impl Line {
    pub fn new(text: impl Into<String>, size: f32, bold: bool, color: u32) -> Self {
        Line { text: text.into(), size, bold, color, max_lines: 1 }
    }

    pub fn wrapped(mut self, max_lines: usize) -> Self {
        self.max_lines = max_lines;
        self
    }
}

/// A card's contents: a small title in the accent colour over a rule, then the lines.
pub struct Sign {
    pub title: String,
    pub accent: u32,
    pub lines: Vec<Line>,
}

/// The widest a card's text runs, in world pixels at the normal zoom.
const MAX_W: f32 = 210.0;

/// Draw `sign` over the object whose feet are at (px, py) on the screen, its tail pointing
/// down at it; `k` is the world's scale (1 at the normal zoom), `size_pct` the object's size.
pub fn draw(ui: &mut Ui, sign: &Sign, px: f32, py: f32, size_pct: f32, k: f32) {
    let pad = 12.0 * k;
    let title_size = (11.0 * k).round().max(8.0);
    let max_w = MAX_W * k;

    // Wrap every line first: the card is as wide as its widest line.
    let mut rows: Vec<(String, f32, bool, u32)> = Vec::new();
    for line in &sign.lines {
        let size = (line.size * k).round().max(8.0);
        let mut wrapped = wrap(ui, &line.text, size, line.bold, max_w);
        if wrapped.len() > line.max_lines {
            wrapped.truncate(line.max_lines);
            let last = wrapped.last_mut().unwrap();
            *last = fit(ui, &format!("{last} …"), size, line.bold, max_w);
        }
        rows.extend(wrapped.into_iter().map(|t| (t, size, line.bold, line.color)));
    }
    let title_w = ui.measure(&sign.title, title_size, true);
    let text_w = rows.iter().map(|(t, size, bold, _)| ui.measure(t, *size, *bold)).fold(title_w, f32::max);
    let gap = 3.0 * k;
    let body_h: f32 = rows.iter().map(|(_, size, _, _)| size * 1.25).sum::<f32>() + gap * rows.len().saturating_sub(1) as f32;
    let (w, h) = (text_w + 2.0 * pad, pad + title_size * 1.3 + 8.0 * k + body_h + pad * 0.8);

    // Above the object's picture, as the speech balloons sit.
    let bottom = py - 40.0 * size_pct * k - 2.0 * k - 18.0 * k;
    let r = [(px - w / 2.0).round(), (bottom - h).round(), w.round(), h.round()];
    let radius = 7.0 * k;
    theme::shadow(ui.renderer, r, radius, 10.0 * k, 0.45);
    theme::frame(ui.renderer, r, radius, k.max(1.0), (sign.accent, 0.85), ((color::PANEL_TOP, 0.95), (color::PANEL_BOTTOM, 0.97)));
    theme::fill(ui.renderer, [r[0] + radius, r[1] + k.max(1.0), r[2] - 2.0 * radius, 2.0 * k], 0.0, sign.accent, 0.55);
    // The tail, pointing down at the object.
    let white = ui.renderer.white();
    let tail = r[1] + r[3] - k;
    let tri = |c: u32, a: f32| bsk_render::Tint::color(c, a);
    ui.renderer.triangle(white, [[px - 8.0 * k, tail], [px + 8.0 * k, tail], [px, tail + 10.0 * k]], [[0.5, 0.5]; 3], tri(sign.accent, 0.85));
    ui.renderer.triangle(white, [[px - 6.5 * k, tail - k], [px + 6.5 * k, tail - k], [px, tail + 7.5 * k]], [[0.5, 0.5]; 3], tri(color::PANEL_BOTTOM, 1.0));

    let cx = r[0] + r[2] / 2.0;
    let mut y = r[1] + pad * 0.85;
    ui.label(&sign.title, cx - title_w / 2.0, y, title_size, true, sign.accent);
    y += title_size * 1.3 + 3.0 * k;
    theme::fill(ui.renderer, [r[0] + pad, y, r[2] - 2.0 * pad, k.max(1.0)], 0.0, color::BORDER_HI, 0.9);
    y += 5.0 * k;
    for (t, size, bold, c) in &rows {
        let tw = ui.measure(t, *size, *bold);
        ui.label(t, cx - tw / 2.0, y, *size, *bold, *c);
        y += size * 1.25 + gap;
    }
}

fn wrap(ui: &mut Ui, text: &str, size: f32, bold: bool, width: f32) -> Vec<String> {
    let mut lines = vec![String::new()];
    for word in text.split(' ') {
        let line = lines.last_mut().unwrap();
        let candidate = if line.is_empty() { word.to_owned() } else { format!("{line} {word}") };
        if !line.is_empty() && ui.measure(&candidate, size, bold) > width {
            lines.push(word.to_owned());
        } else {
            *line = candidate;
        }
    }
    lines
}

fn fit(ui: &mut Ui, text: &str, size: f32, bold: bool, width: f32) -> String {
    let mut t = text.to_owned();
    while ui.measure(&t, size, bold) > width && t.chars().count() > 2 {
        let keep: String = t.chars().take(t.chars().count().saturating_sub(3)).collect();
        t = format!("{} …", keep.trim_end_matches(" …").trim_end());
    }
    t
}

/// The arena leaderboard's sign: the best run's wave and time, and who took part.
pub fn arena_sign(best: Option<&ArenaRun>) -> Sign {
    const GOLD: u32 = 0xFFD24A;
    let lines = match best {
        None => vec![
            Line::new("No runs yet", 15.0, true, color::TEXT),
            Line::new("Be the first: type /arena", 12.0, false, color::TEXT_DIM),
        ],
        Some(run) => {
            let names = if run.players.is_empty() { "Unknown".to_owned() } else { run.players.join(", ") };
            let mut lines = vec![
                Line::new(format!("Wave {}  ·  {}", run.wave, crate::npc::run_time(run.seconds)), 17.0, true, color::TEXT),
                Line::new(names, 13.0, true, GOLD).wrapped(2),
            ];
            if !run.arena.is_empty() {
                lines.push(Line::new(run.arena.clone(), 11.0, false, color::TEXT_MUTED));
            }
            lines
        }
    };
    Sign { title: "ARENA RECORD".to_owned(), accent: GOLD, lines }
}

fn is_arena_board(world_obj: &GameObj, data: &bsk_data::GameData) -> bool {
    data.objects.get(&world_obj.object_type).is_some_and(|d| matches!(&d.class, ObjectClass::Other(c) if c == "ArenaLeaderboard"))
}

/// What the signs say, fetched from the account server while their objects are near.
#[derive(Default)]
pub struct Signs {
    /// The arena's best run: `None` until fetched, `Some(None)` when there are no runs.
    pub arena: Option<Option<ArenaRun>>,
    /// When the arena's sign may be fetched again.
    next_arena: i32,
    /// Whether an arena board was in range at the last frame drawn.
    arena_near: bool,
    pub request: Option<bsk_net::Pending<bsk_net::http::Response>>,
}

impl Signs {
    /// Whether to fetch the arena's best run now (a board was in range at the last frame and
    /// the last fetch is stale); marks it fetched.
    pub fn want_arena(&mut self, now: i32) -> bool {
        if self.request.is_some() || now < self.next_arena || !self.arena_near {
            return false;
        }
        self.next_arena = now + REFRESH_MS;
        true
    }

    /// Take the leaderboard's runs (from the sign's own fetch or the leaderboard window's).
    pub fn set_arena(&mut self, runs: &[ArenaRun]) {
        self.arena = Some(runs.first().cloned());
    }

    /// The signs over the objects in range (and note which are, for `want_arena`).
    pub fn draw(&mut self, ui: &mut Ui, world: &World, cam: &crate::view::Camera) {
        self.arena_near = false;
        let Some(me) = world.player() else { return };
        let k = (cam.scale / crate::view::TILE_PX) as f32;
        for o in world.objects.values() {
            if (o.x - me.x).hypot(o.y - me.y) > RANGE || !is_arena_board(o, ui.data) {
                continue;
            }
            self.arena_near = true;
            let Some(best) = &self.arena else { continue };
            let [px, py] = cam.to_screen(o.x, o.y, 0.0);
            let size_pct = o.stat_int(2).unwrap_or(100) as f32 / 100.0;
            draw(ui, &arena_sign(best.as_ref()), px, py, size_pct, k);
        }
    }
}
