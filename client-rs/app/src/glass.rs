//! The Scrying Glass on the Nexus spawn's north side (the world server's
//! `core/spectate/SpectatorHub.cs`): a great glass in a stone frame that shows a player out
//! adventuring, live, picked at random by the glass and changed every so often.
//!
//! While the glass is in view we ask to watch (`Spectate` 1) and the server sends who it shows
//! (`SpectateInfo`) and what that player sees (`SpectateFeed`: the same `Update`s, `NewTick`s,
//! shots and effects their own client gets, wrapped), which we keep as a little world of its own
//! (`Picture`) and draw inside the glass, in its place among what stands round it (`view::Scene.glass`).
//!
//! Its Watch button opens the big view (`Spectate` 2): the picture over most of the screen, arrows
//! (and the arrow keys) to step through everyone who can be watched (3, 4), and beside it a chat
//! of the watchers' own (`SpectateSay`, `SpectateChat`). Closing it (5) gives the choice back to
//! the glass. Players keep themselves off it with `/glass off`.

use crate::ground::Ground;
use crate::hud::{Layout, Ui};
use crate::i18n::{tf, tn, tr};
use crate::redraw::Redrawer;
use crate::sprites::Sprites;
use crate::text::{Fonts, Text};
use crate::textedit::LineEdit;
use crate::theme::{self, color};
use crate::view::{self, Camera, Scene, Show};
use bsk_data::GameData;
use bsk_proto::ServerMessage;
use bsk_render::{Renderer, Tint};
use bsk_world::{GameObj, MoveInput, Projectile, World};
use winit::keyboard::KeyCode;

/// How many tiles the picture shows from top to bottom: in the big view, on the glass itself.
const BIG_TILES: f64 = 15.0;
const SMALL_TILES: f64 = 9.0;
/// The glass's screen in the world, in tiles: its width and height, and how far its foot stands
/// over the ground on its two posts.
const SCREEN_W: f64 = 4.6;
const SCREEN_H: f64 = 2.6;
const SCREEN_LIFT: f64 = 0.9;
/// We watch while the glass is this near (tiles): about as far as a window shows.
const WATCH_RANGE: f64 = 24.0;
/// How quickly the glass's camera catches up with the watched player (ms to close about two
/// thirds of the gap), and how far they must jump for it to cut straight there, tiles.
const CAMERA_EASE_MS: f64 = 110.0;
const CAMERA_JUMP: f64 = 4.0;
/// How long the picture ripples when it changes, ms.
const RIPPLE_MS: i32 = 650;
/// Chat lines kept, and the most typed in one.
const CHAT_KEEP: usize = 100;
const CHAT_MAX: usize = 200;

/// The frame's stone and trim.
const STONE_DARK: u32 = 0x1A1329;
const STONE: u32 = 0x2E2448;
const STONE_LIGHT: u32 = 0x4A3A6A;
const TRIM: u32 = 0xC9973A;
const MIST: u32 = 0x6C4FC0;

/// The world the glass shows: whom (their object id in it) and where.
pub struct Picture {
    serial: u16,
    target_id: i32,
    name: String,
    world_name: String,
    object_type: u16,
    world: World,
    ground: Ground,
    particles: crate::particles::Particles,
    /// Where the camera looks: it follows the watched player a little behind, so the picture
    /// glides through the turns and stops of their walk rather than jerking with each tick.
    camera: Option<(f64, f64)>,
}

impl Picture {
    /// One of the watched player's messages, as `Game::handle` takes ours.
    fn apply(&mut self, msg: &ServerMessage, now: i32, data: &GameData) {
        let world = &mut self.world;
        match msg {
            ServerMessage::Update(u) => world.apply_update(u, data, now),
            ServerMessage::NewTick(t) => world.apply_new_tick(t, now),
            ServerMessage::Layers(l) => world.set_layers(l.height as f64, &l.tiles),
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
                if let Some(owner) = world.objects.get_mut(&e.owner_id) {
                    owner.note_attack(now, e.angle as f64 + inc * (num - 1) as f64 / 2.0);
                }
            }
            ServerMessage::ServerPlayerShoot(s) => {
                if world.objects.get(&s.owner_id).is_some_and(|o| !o.dead) {
                    let (x, y) = (s.starting_pos.x as f64, s.starting_pos.y as f64);
                    let p = Projectile::new(s.owner_id, s.bullet_id, s.container_type as u16, s.bullet_type.unwrap_or(0) as i32, s.angle as f64, now, x, y, 1.0, s.damage, false);
                    world.add_projectile(p);
                }
            }
            ServerMessage::AllyShoot(a) => {
                let Some(owner) = world.objects.get_mut(&a.owner_id).filter(|o| !o.dead) else { return };
                owner.note_attack(now, a.angle as f64);
                let (x, y) = (owner.x, owner.y);
                world.add_projectile(Projectile::new(a.owner_id, a.bullet_id, a.container_type as u16, 0, a.angle as f64, now, x, y, 1.0, 0, false));
            }
            ServerMessage::ShowEffect(e) => self.particles.show_effect(e, world, now),
            ServerMessage::Aoe(a) => self.particles.aoe(a.pos.x as f64, a.pos.y as f64, a.radius as f64, a.color),
            _ => {}
        }
    }

    fn target(&self) -> Option<&GameObj> {
        self.world.objects.get(&self.target_id)
    }

    /// The camera `dt` ms on, toward the watched player: eased, and straight there when they
    /// jumped (a teleport, a new floor) or it has nowhere yet.
    fn follow(&mut self, dt: i32) {
        let Some((tx, ty)) = self.target().map(|t| (t.x, t.y)) else { return };
        self.camera = Some(match self.camera {
            Some((x, y)) if (tx - x).hypot(ty - y) < CAMERA_JUMP => {
                let k = 1.0 - (-(dt.max(0) as f64) / CAMERA_EASE_MS).exp();
                (x + (tx - x) * k, y + (ty - y) * k)
            }
            _ => (tx, ty),
        });
    }
}

/// A line of the watchers' chat.
struct ChatLine {
    kind: u8,
    name: String,
    stars: i32,
    name_color: i32,
    text: String,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    Prev,
    Next,
    /// Stay on the one shown, or let the glass pick again.
    Stay,
}

/// The glass: the picture, and the big view.
#[derive(Default)]
pub struct Glass {
    picture: Option<Picture>,
    status: Option<bsk_proto::SpectateStatus>,
    /// When the picture last changed (the ripple).
    changed_at: i32,
    /// We've asked to watch.
    watching: bool,
    /// The big view is open.
    pub open: bool,
    chat: Vec<ChatLine>,
    /// Rows scrolled up from the newest.
    chat_scroll: usize,
    input: LineEdit,
    out: Vec<u8>,
    said: Vec<String>,
    hits: Vec<(Hit, [f32; 4])>,
    rect: Option<[f32; 4]>,
    /// When we last looked for the glass in the world, and the answer.
    looked_at: Option<i32>,
    near: bool,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// How the picture shows things: everyone's shots in full (they're all someone else's), no
/// damage numbers.
pub fn picture_show(show: Show) -> Show {
    Show { others_shots: 1.0, player_alpha: 1.0, damage_numbers: false, others_damage: false, ..show }
}

impl Glass {
    /// A new connection: the server has forgotten us.
    pub fn reset(&mut self) {
        let chat = std::mem::take(&mut self.chat);
        *self = Glass { chat, ..Glass::default() };
    }

    /// The messages to send (`Game::glass`).
    pub fn take_out(&mut self) -> (Vec<u8>, Vec<String>) {
        (std::mem::take(&mut self.out), std::mem::take(&mut self.said))
    }

    /// Opens the big view (the glass's Watch button).
    pub fn show(&mut self) {
        if !self.open {
            self.open = true;
            self.watching = true;
            self.chat_scroll = 0;
            self.out.push(2);
        }
    }

    pub fn close(&mut self) {
        if self.open {
            self.open = false;
            self.out.push(5);
        }
    }

    /// One of the server's Scrying Glass messages.
    pub fn message(&mut self, msg: &ServerMessage, now: i32, data: &GameData) {
        match msg {
            ServerMessage::SpectateInfo(i) => {
                self.changed_at = now;
                if i.target_id == 0 {
                    self.picture = None;
                    return;
                }
                log::info!("scrying glass: {} in {} ({})", i.name, i.world, i.map);
                let size = (i.size as i32).max(1);
                let mut world = World::new(size, size, &i.world, 0);
                world.background = i.background;
                world.watched = Some(i.target_id);
                self.picture = Some(Picture {
                    serial: i.serial,
                    target_id: i.target_id,
                    name: i.name.clone(),
                    world_name: i.world.clone(),
                    object_type: i.object_type,
                    world,
                    ground: Ground::default(),
                    particles: Default::default(),
                    camera: None,
                });
            }
            ServerMessage::SpectateFeed(f) => {
                let Some(p) = self.picture.as_mut().filter(|p| p.serial == f.serial) else { return };
                match ServerMessage::decode(f.kind, &f.body.0) {
                    Ok(inner) => p.apply(&inner, now, data),
                    Err(e) => log::warn!("scrying glass: message {} didn't read: {e}", f.kind),
                }
            }
            ServerMessage::SpectateStatus(s) => {
                log::debug!("scrying glass: {} of {}, {} watching, picked {}", s.index, s.count, s.watchers, s.picked);
                self.status = Some(s.clone());
            }
            ServerMessage::SpectateChat(c) => {
                if self.chat_scroll > 0 {
                    self.chat_scroll += 1;
                }
                self.chat.push(ChatLine { kind: c.kind, name: c.name.clone(), stars: c.stars, name_color: c.name_color, text: c.text.clone() });
                if self.chat.len() > CHAT_KEEP {
                    self.chat.remove(0);
                }
            }
            _ => {}
        }
    }

    /// Each frame: watch while the glass is near us in the Nexus, and move the picture on.
    pub fn tick(&mut self, world: Option<&World>, map: &str, data: &GameData, now: i32, dt: i32) {
        // Between worlds (a portal's reconnect, a dropped connection): the new connection's
        // server knows nothing of our watching, and takes nothing from us before our player is
        // in, so forget it all and ask again once we're there.
        if !world.is_some_and(|w| w.player().is_some()) {
            if self.watching || self.open || !self.out.is_empty() || !self.said.is_empty() {
                self.reset();
            }
            return;
        }
        if self.looked_at.is_none_or(|t| now - t >= 500 || now < t) {
            self.looked_at = Some(now);
            self.near = map == "Nexus"
                && world.is_some_and(|w| {
                    let Some(me) = w.player() else { return false };
                    w.objects.values().any(|o| {
                        data.objects.get(&o.object_type).is_some_and(view::is_glass) && (o.x - me.x).hypot(o.y - me.y) <= WATCH_RANGE
                    })
                });
        }
        let want = self.near || (self.open && map == "Nexus");
        if want != self.watching {
            self.watching = want;
            self.out.push(if want { 1 } else { 0 });
            if !want {
                self.picture = None;
                self.status = None;
                self.open = false;
            }
        }
        if let Some(p) = &mut self.picture {
            p.world.update(now, dt, &MoveInput::default(), data);
            p.world.update_projectiles(now, data);
            p.particles.impacts(&mut p.world);
            p.particles.update(&p.world, now, dt);
            p.follow(dt);
        }
    }

    /// The picture in `rect`, `tiles` tall, or the glass's mist while there's none.
    #[allow(clippy::too_many_arguments)]
    fn draw_picture(&mut self, r: &mut Renderer, rd: &mut Redrawer, fonts: &Fonts, text: &mut Text, sprites: &Sprites, data: &GameData, show: Show, rect: [f32; 4], tiles: f64, now: i32) {
        r.fill(rect[0], rect[1], rect[2], rect[3], 0x05030A, 1.0);
        let shown = self.picture.as_mut().and_then(|p| {
            let target = p.target()?;
            let scale = rect[3] as f64 / tiles;
            let (cx, cy) = ((rect[0] + rect[2] / 2.0) as f64, (rect[1] + rect[3] * 0.56) as f64);
            let z = if p.world.levels.is_some() { view::height_of(&p.world, data, target) } else { 0.0 };
            let (x, y) = p.camera.unwrap_or((target.x, target.y));
            let camera = Camera::new(x, y, 0.0, scale, cx, cy).at_height(z).snapped();
            let roof = view::roof(&p.world, data, &camera);
            let scene = Scene {
                sprites,
                data,
                world: &p.world,
                camera,
                time: now,
                floaters: &[],
                bubbles: &[],
                particles: &p.particles,
                show,
                hallucinating: false,
                roof,
                me_alpha: 1.0,
                inset: Some(rect),
                glass: None,
            };
            view::draw(&scene, r, rd, &mut p.ground, fonts, text);
            Some(())
        });
        if shown.is_none() {
            mist(r, rect, now, 1.0);
        }
        // the glass's sheen, and a ripple of mist when the picture changes
        let since = now - self.changed_at;
        if (0..RIPPLE_MS).contains(&since) {
            mist(r, rect, now, 1.0 - since as f32 / RIPPLE_MS as f32);
        }
        sheen(r, rect);
    }

    /// The glass standing in the world (`view::Scene.glass`): its posts and frame over its
    /// square, and the picture in it.
    #[allow(clippy::too_many_arguments)]
    pub fn draw_in_world(&mut self, r: &mut Renderer, rd: &mut Redrawer, fonts: &Fonts, text: &mut Text, sprites: &Sprites, data: &GameData, show: Show, obj: &GameObj, cam: &Camera, z: f64, now: i32) {
        let [fx, fy] = cam.to_screen(obj.x, obj.y, z);
        let tile = cam.scale as f32;
        let (w, h) = (SCREEN_W as f32 * tile, SCREEN_H as f32 * tile);
        let screen = [(fx - w / 2.0).round(), (fy - SCREEN_LIFT as f32 * tile - h).round(), w.round(), h.round()];
        let px = (tile / 8.0).max(1.0).round();
        // a shadow on the ground, the posts up to the frame, a slab at their feet
        let shadow = Tint::color(0x000000, 0.28);
        if let Some(blob) = rd.shadow(r).filter(|_| show.shadows) {
            r.sprite(blob, bsk_render::Quad::rect(fx - w * 0.42, fy - tile * 0.18, w * 0.84, tile * 0.36), shadow);
        }
        for side in [-1.0f32, 1.0] {
            let x = (fx + side * w * 0.32 - px).round();
            let top = screen[1] + screen[3];
            r.fill(x, top, 2.0 * px, fy - top, STONE, 1.0);
            r.fill(x, top, px, fy - top, STONE_LIGHT, 1.0);
            r.fill(x - px, fy - 2.0 * px, 4.0 * px, 2.0 * px, STONE_DARK, 1.0);
        }
        frame(r, screen, px, now);
        if self.open {
            // the big view shows it: the glass itself only clouds, rather than drawing it twice
            r.fill(screen[0], screen[1], screen[2], screen[3], 0x05030A, 1.0);
            mist(r, screen, now, 1.0);
            sheen(r, screen);
        } else {
            self.draw_picture(r, rd, fonts, text, sprites, data, show, screen, SMALL_TILES, now);
        }
        // who it shows, along its foot
        if let Some(p) = &self.picture {
            let size = (h * 0.09).clamp(9.0, 13.0).round();
            let mut ui = Ui { renderer: r, sprites, data, redrawer: rd, fonts, text };
            let line = format!("{} · {}", p.name, p.world_name);
            let lw = ui.measure(&line, size, true).min(screen[2] - 8.0 * px);
            let (lx, ly) = (screen[0] + (screen[2] - lw) / 2.0, screen[1] + screen[3] - size * 1.35 - px);
            ui.rect(lx - 3.0 * px, ly - px, lw + 6.0 * px, size * 1.3 + 2.0 * px, 0x000000, 0.55);
            ui.label_fit(&line, lx, ly, lw, size, true, color::TEXT);
        }
    }

    pub fn scroll(&mut self, rows: i32) {
        self.chat_scroll = (self.chat_scroll as i32 + rows).clamp(0, self.chat.len() as i32) as usize;
    }

    /// A key while the big view is open: it types into the chat, but for Escape, Enter (send),
    /// Page Up and Down (the chat) and the arrows (the players, while nothing is typed).
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, ctrl: bool, shift: bool) {
        match code {
            KeyCode::Escape => self.close(),
            KeyCode::Enter | KeyCode::NumpadEnter => {
                let line = std::mem::take(&mut self.input.text);
                self.input = LineEdit::new("", CHAT_MAX);
                if !line.trim().is_empty() {
                    self.said.push(line.trim().to_owned());
                    self.chat_scroll = 0;
                }
            }
            KeyCode::ArrowLeft if self.input.text.is_empty() => self.out.push(4),
            KeyCode::ArrowRight if self.input.text.is_empty() => self.out.push(3),
            KeyCode::PageUp => self.scroll(5),
            KeyCode::PageDown => self.scroll(-5),
            _ => {
                if self.input.max == 0 {
                    self.input = LineEdit::new("", CHAT_MAX);
                }
                self.input.key(code, text, ctrl, shift);
            }
        }
    }

    /// Text pasted into the big view's chat.
    pub fn paste(&mut self, text: &str) {
        if self.input.max == 0 {
            self.input = LineEdit::new("", CHAT_MAX);
        }
        self.input.insert(text);
    }

    /// `--type`: a line said in the chat, as if typed and sent.
    pub fn say(&mut self, text: &str) {
        self.paste(text);
        self.key(KeyCode::Enter, None, false, false);
    }

    /// A press of the left button while the big view is open: outside it closes it.
    pub fn click(&mut self, pos: (f64, f64)) {
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h);
        log::debug!("scrying glass: click at {pos:?} on {hit:?}");
        match hit {
            Some(Hit::Close) => self.close(),
            Some(Hit::Prev) => self.out.push(4),
            Some(Hit::Next) => self.out.push(3),
            Some(Hit::Stay) => {
                let picked = self.status.as_ref().is_some_and(|s| s.picked);
                self.out.push(if picked { 7 } else { 6 });
            }
            None => {
                if self.rect.is_some_and(|r| !inside(r, pos)) {
                    self.close();
                }
            }
        }
    }

    /// The big view, over most of the world.
    pub fn draw_big(&mut self, ui: &mut Ui, lay: &Layout, show: Show, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        if !self.open {
            self.rect = None;
            return;
        }
        let s = lay.s;
        let vw = lay.view_w();
        let r = [(vw * 0.025).round(), (lay.h * 0.035).round(), (vw * 0.95).round(), (lay.h * 0.93).round()];
        self.rect = Some(r);
        theme::shadow(ui.renderer, r, 16.0 * s, 24.0 * s, 0.6);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let pad = 16.0 * s;

        // the title, who's shown and where, their place in the list, how many watch, close
        let mid = r[1] + 28.0 * s;
        let ts = font(21.0);
        ui.label(tr("Scrying Glass"), r[0] + pad, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);
        let mut x = r[0] + pad + ui.measure(tr("Scrying Glass"), ts, true) + 22.0 * s;
        let ns = font(15.0);
        if let Some(p) = &self.picture {
            if p.object_type != 0 {
                crate::hud::object_icon(ui, p.object_type, x + 12.0 * s, mid, 26.0 * s);
                x += 30.0 * s;
            }
            ui.label(&p.name, x, (mid - ui.digit_middle(ns, true)).round(), ns, true, color::TEXT);
            x += ui.measure(&p.name, ns, true) + 10.0 * s;
            let world = tf!("in {world}", world = p.world_name);
            ui.label(&world, x, (mid - ui.digit_middle(ns, false)).round(), ns, false, color::TEXT_DIM);
        }
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));
        let ss = font(13.0);
        let mut right = cb[0] - 14.0 * s;
        if let Some(st) = &self.status {
            let watching = tn!(st.watchers as i64, "{n} watching|{n} watching", n = st.watchers);
            let w = ui.measure(&watching, ss, true);
            ui.label(&watching, right - w, (mid - ui.digit_middle(ss, true)).round(), ss, true, color::TEXT_DIM);
            right -= w + 18.0 * s;
            if st.count > 0 && st.index > 0 {
                let place = tf!("{n} of {count}", n = st.index, count = st.count);
                let w = ui.measure(&place, ss, true);
                ui.label(&place, right - w, (mid - ui.digit_middle(ss, true)).round(), ss, true, color::ACCENT_HI);
            }
        }

        // the picture on the left in its frame, the watchers' chat on the right
        let top = r[1] + 56.0 * s;
        let chat_w = (r[2] * 0.27).clamp(240.0 * s, 380.0 * s);
        let foot = 30.0 * s;
        let frame_px = (3.0 * s).round().max(2.0);
        let pic = [r[0] + pad + 3.0 * frame_px, top + 3.0 * frame_px, r[2] - chat_w - 3.0 * pad - 6.0 * frame_px, r[1] + r[3] - top - pad - foot - 6.0 * frame_px];
        frame(ui.renderer, pic, frame_px, now);
        let pic_show = picture_show(show);
        self.draw_picture(ui.renderer, ui.redrawer, ui.fonts, ui.text, ui.sprites, ui.data, pic_show, pic, BIG_TILES, now);
        let count = self.status.as_ref().map_or(0, |s| s.count);
        if self.picture.is_none() {
            let (a, b) = (tr("No one is out adventuring right now."), tr("The glass clears when someone heads out."));
            let (as_, bs) = (font(18.0), font(13.0));
            let (aw, bw) = (ui.measure(a, as_, true), ui.measure(b, bs, false));
            let cy = pic[1] + pic[3] / 2.0;
            ui.label(a, pic[0] + (pic[2] - aw) / 2.0, cy - as_ * 1.3, as_, true, color::TEXT);
            ui.label(b, pic[0] + (pic[2] - bw) / 2.0, cy + 4.0 * s, bs, false, color::TEXT_DIM);
        }
        // the arrows, at the picture's sides
        if count > 1 || self.picture.is_some() {
            let rad = 26.0 * s;
            for (hit, cx) in [(Hit::Prev, pic[0] + 16.0 * s + rad), (Hit::Next, pic[0] + pic[2] - 16.0 * s - rad)] {
                let cy = pic[1] + pic[3] / 2.0;
                let br = [cx - rad, cy - rad, 2.0 * rad, 2.0 * rad];
                let over = inside(br, mouse);
                theme::fill(ui.renderer, br, rad, if over { color::ACCENT } else { 0x000000 }, if over { 0.95 } else { 0.5 });
                let k = rad * 0.42;
                let d = if hit == Hit::Prev { -1.0 } else { 1.0 };
                let tip = [cx + d * k, cy];
                let pts = [[cx - d * k * 0.6, cy - k], tip, [cx - d * k * 0.6, cy + k]];
                let white = ui.renderer.white();
                ui.renderer.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(if over { color::ACCENT_TEXT } else { color::TEXT }, 1.0));
                self.hits.push((hit, br));
            }
        }
        // under the picture: stay on them (or let the glass pick again), who chose them, the keys
        let fy = pic[1] + pic[3] + 3.0 * frame_px + 8.0 * s;
        let picked = self.status.as_ref().is_some_and(|s| s.picked);
        let mut wx = pic[0];
        if self.picture.is_some() {
            let label = if picked { tr("Let the glass pick") } else { tr("Stay on this player") };
            let bs = (12.0 * s).round().max(9.0);
            let bw = ui.measure(label, bs, true) + 28.0 * s;
            let br = [pic[0], fy - 5.0 * s, bw, 24.0 * s];
            let kind = if picked { theme::Kind::Secondary } else { theme::Kind::Primary };
            ui.button(br, label, kind, inside(br, mouse), s * 0.8);
            self.hits.push((Hit::Stay, br));
            wx += bw + 12.0 * s;
        }
        let who = if picked { tr("Staying on them: the glass won't switch until you let it pick.") } else { tr("The glass picks someone new every little while.") };
        ui.label_fit(who, wx, fy, pic[0] + pic[2] * 0.62 - wx, ss, false, color::TEXT_MUTED);
        let keys = tr("Arrows switch · Enter chats · Esc closes");
        let kw = ui.measure(keys, ss, false).min(pic[2] * 0.38);
        ui.label_fit(keys, pic[0] + pic[2] - kw, fy, kw, ss, false, color::TEXT_MUTED);

        self.draw_chat(ui, [r[0] + r[2] - pad - chat_w, top, chat_w, r[1] + r[3] - top - pad], s, now);
    }

    /// The watchers' chat: its lines, newest at the foot, over the line being typed.
    fn draw_chat(&mut self, ui: &mut Ui, r: [f32; 4], s: f32, now: i32) {
        theme::rounded(ui.renderer, r, 10.0 * s, (color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0));
        let pad = 12.0 * s;
        let hs = (14.0 * s).round().max(9.0);
        ui.label(tr("Watchers' chat"), r[0] + pad, r[1] + pad, hs, true, color::TEXT);
        let size = (13.0 * s).round().max(9.0);
        let line_h = (size * 1.35).round();
        let field = [r[0] + pad, r[1] + r[3] - pad - 32.0 * s, r[2] - 2.0 * pad, 32.0 * s];
        ui.field_box(field, true, s);
        if self.input.max == 0 {
            self.input = LineEdit::new("", CHAT_MAX);
        }
        let inner = [field[0] + 10.0 * s, field[1], field[2] - 20.0 * s, field[3]];
        crate::hud::draw_line_edit(ui, &self.input, inner, size, tr("Say something to the other watchers…"), now);

        // the lines, wrapped to the column: only the newest, as many as fit above those scrolled
        // past, newest last
        let width = r[2] - 2.0 * pad;
        let classes = crate::stars::class_count(ui.data);
        let top = r[1] + pad + hs * 1.6;
        let fit = (((field[1] - 8.0 * s - top) / line_h).floor().max(1.0)) as usize;
        let mut rows: Vec<(Option<(i32, String, u32)>, String, u32)> = Vec::new();
        let mut first = self.chat.len();
        let mut wanted = 0;
        while first > 0 && wanted < fit + self.chat_scroll {
            first -= 1;
            // each line is a row at least, so this many lines fill the column
            wanted += 1;
        }
        for line in &self.chat[first..] {
            match line.kind {
                0 => {
                    let name_rgb = match line.name_color & 0xFF_FFFF {
                        0 | 0x123456 => color::GOLD,
                        c => c as u32,
                    };
                    let lead = format!("{}: ", line.name);
                    let lead_w = ui.measure(&lead, size, true) + 18.0 * s;
                    let wrapped = crate::screens::wrap(ui, &line.text, size, width - lead_w);
                    for (i, row) in wrapped.into_iter().enumerate() {
                        let head = (i == 0).then(|| (line.stars, lead.clone(), name_rgb));
                        rows.push((head, row, color::TEXT));
                    }
                }
                kind => {
                    let text = if kind == 1 { tf!("{name} is watching.", name = line.name) } else { tf!("{name} stopped watching.", name = line.name) };
                    for row in crate::screens::wrap(ui, &text, size, width) {
                        rows.push((None, row, color::TEXT_MUTED));
                    }
                }
            }
        }
        self.chat_scroll = self.chat_scroll.min(rows.len().saturating_sub(fit));
        let end = rows.len() - self.chat_scroll;
        let start = end.saturating_sub(fit);
        if rows.is_empty() {
            let hint = tr("Nobody has said anything yet.");
            ui.label_fit(hint, r[0] + pad, top, width, size, false, color::TEXT_MUTED);
        }
        let mut y = field[1] - 8.0 * s - (end - start) as f32 * line_h;
        for (head, text, rgb) in &rows[start..end] {
            let mut x = r[0] + pad;
            if let Some((stars, lead, name_rgb)) = head {
                let k = size / 16.0;
                crate::stars::icon(ui.renderer, x + 8.0 * k, y + size * 0.62, k, crate::stars::color(*stars, classes));
                x += 18.0 * s;
                ui.label(lead, x, y, size, true, *name_rgb);
                x += ui.measure(lead, size, true);
            } else if !matches!(rgb, &color::TEXT_MUTED) {
                // a wrapped row lines up under the text
                x += 18.0 * s;
            }
            ui.label(text, x, y, size, false, *rgb);
            y += line_h;
        }
        if self.chat_scroll > 0 {
            let more = tr("Newer lines below (Page Down)");
            ui.label_fit(more, r[0] + pad, field[1] - 4.0 * s - line_h, width, (size * 0.9).round(), true, color::ACCENT_HI);
        }
    }
}

/// The glass's stone frame round `screen`, `px` its pixel: dark stone, a light inner edge, gold
/// corners and an ember gem atop that glows slowly.
fn frame(r: &mut Renderer, screen: [f32; 4], px: f32, now: i32) {
    let [x, y, w, h] = screen;
    let t = 3.0 * px;
    r.fill(x - t - px, y - t - px, w + 2.0 * (t + px), h + 2.0 * (t + px), 0x000000, 0.35);
    r.fill(x - t, y - t, w + 2.0 * t, h + 2.0 * t, STONE_DARK, 1.0);
    r.fill(x - t + px, y - t + px, w + 2.0 * t - 2.0 * px, px, STONE_LIGHT, 1.0);
    r.fill(x - t + px, y - t + px, px, h + 2.0 * t - 2.0 * px, STONE, 1.0);
    r.fill(x + w + t - 2.0 * px, y - t + px, px, h + 2.0 * t - 2.0 * px, STONE, 1.0);
    r.fill(x - t + px, y + h + t - 2.0 * px, w + 2.0 * t - 2.0 * px, px, STONE, 1.0);
    // the glass's edge, lit violet
    r.fill(x - px, y - px, w + 2.0 * px, px, MIST, 0.9);
    r.fill(x - px, y + h, w + 2.0 * px, px, MIST, 0.6);
    r.fill(x - px, y, px, h, MIST, 0.75);
    r.fill(x + w, y, px, h, MIST, 0.75);
    for (cx, cy) in [(x - t, y - t), (x + w + t - 2.0 * px, y - t), (x - t, y + h + t - 2.0 * px), (x + w + t - 2.0 * px, y + h + t - 2.0 * px)] {
        r.fill(cx, cy, 2.0 * px, 2.0 * px, TRIM, 1.0);
    }
    let glow = 0.55 + 0.45 * ((now as f32 / 900.0).sin() * 0.5 + 0.5);
    let (gx, gy) = ((x + w / 2.0 - 2.0 * px).round(), y - t - 2.0 * px);
    r.fill(gx - px, gy - px, 6.0 * px, 4.0 * px, color::ACCENT, 0.18 * glow);
    r.fill(gx, gy, 4.0 * px, 2.0 * px, TRIM, 1.0);
    r.fill(gx + px, gy, 2.0 * px, 2.0 * px, color::ACCENT, glow);
}

/// The glass clouded: violet mist drifting across it, `alpha` thick.
fn mist(r: &mut Renderer, rect: [f32; 4], now: i32, alpha: f32) {
    let [x, y, w, h] = rect;
    let bands = 9;
    let t = now as f32 / 1000.0;
    for i in 0..bands {
        let f = i as f32 / bands as f32;
        let wave = (t * 0.7 + f * 5.0).sin() * 0.5 + 0.5;
        let by = y + h * f;
        let rgb = if i % 2 == 0 { MIST } else { 0x3A2A70 };
        r.fill(x, by, w, h / bands as f32 + 1.0, rgb, alpha * (0.22 + 0.3 * wave));
        // a brighter wisp drifting along the band
        let wx = x + w * ((t * (0.08 + 0.03 * i as f32) + f * 0.7) % 1.0);
        let ww = (w * 0.25).min(x + w - wx);
        if ww > 0.0 {
            r.fill(wx, by + h / bands as f32 * 0.3, ww, h / bands as f32 * 0.4, 0xB8A4FF, alpha * 0.18 * wave);
        }
    }
}

/// A faint light across the glass, top left to the middle.
fn sheen(r: &mut Renderer, rect: [f32; 4]) {
    let [x, y, w, h] = rect;
    let white = r.white();
    let tint = Tint::color(0xFFFFFF, 0.025);
    let (a, b) = (w * 0.18, w * 0.32);
    r.triangle(white, [[x + a, y], [x + b, y], [x + b - h * 0.5, y + h]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[x + a, y], [x + b - h * 0.5, y + h], [x + a - h * 0.5, y + h]], [[0.5, 0.5]; 3], tint);
}
