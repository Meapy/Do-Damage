//! The Pen Keeper's window (the world server's `Player.PetPen.cs`), at the pet pen east of the
//! Arena court: the pet you have out, taking it back into its stone, putting in the pet of a stone
//! from the inventory, and raising its size: Small to Medium for a Superburger, Medium to Large for
//! an Ambrosia. He keeps the account's Shards of the Mad God (given in his window, or used
//! anywhere) and trades 25 of them for an Ambrosia. A raised pet keeps its look and its shots; it
//! hits and heals as the size it's raised to.
//!
//! The window asks as it opens and after each act (`PetPenAct`); the server answers with the pet
//! as it is (`PetPen`).

use crate::hud::{Layout, Ui};
use crate::i18n::{tf, tr};
use crate::theme::{self, color, Kind};
use bsk_world::GameObj;
use winit::keyboard::KeyCode;

/// `PetPenAct`'s acts.
pub const ASK: u8 = 0;
pub const TAKE_BACK: u8 = 1;
pub const PUT_IN: u8 = 2;
pub const TO_MEDIUM: u8 = 3;
pub const SHARDS: u8 = 4;
pub const TO_LARGE: u8 = 5;
/// 25 of the shards he keeps for an Ambrosia (from 2.5.1).
pub const AMBROSIA: u8 = 6;

const BURGER: &str = "Superburger";
const AMBROSIA_ID: &str = "Ambrosia";
const SHARD: &str = "Shard of the Mad God x";

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    Act(u8),
    Stone(u8),
}

/// The Pen Keeper's window.
#[derive(Default)]
pub struct PetPen {
    pub open: bool,
    state: Option<bsk_proto::PetPen>,
    /// What the last act came to: the server's words, and whether it worked.
    message: Option<(String, bool)>,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover: Option<i32>,
    /// The sizes the Pen Keeper raised the hovered stone's pet by (0 for none).
    hover_raise: u8,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

fn size_name(size: u8) -> &'static str {
    match size {
        0 => tr("Tiny"),
        1 => tr("Small"),
        2 => tr("Medium"),
        3 => tr("Large"),
        _ => "?",
    }
}

/// Whether an item gives a pet (a `PermaPet` stone).
fn is_pet_stone(data: &bsk_data::GameData, item: i32) -> bool {
    data.objects.get(&(item as u16)).is_some_and(|d| d.xml.children_named("Activate").any(|a| a.text.trim() == "PermaPet"))
}

impl PetPen {
    /// Opens the window: ask the server for the pet.
    pub fn show(&mut self) -> u8 {
        self.open = true;
        self.message = None;
        ASK
    }

    pub fn reset(&mut self) {
        *self = PetPen::default();
    }

    /// The server's answer.
    pub fn answer(&mut self, p: bsk_proto::PetPen) {
        if !p.message.is_empty() {
            self.message = Some((p.message.clone(), p.ok));
        }
        self.state = Some(p);
    }

    /// A press of the left button: an act to send (with an inventory slot), if any.
    pub fn click(&mut self, pos: (f64, f64)) -> Option<(u8, u8)> {
        match self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h) {
            Some(Hit::Close) => self.open = false,
            Some(Hit::Act(a)) => return Some((a, 0)),
            Some(Hit::Stone(slot)) => return Some((PUT_IN, slot)),
            None => {
                if self.rect.is_some_and(|r| !inside(r, pos)) {
                    self.open = false;
                }
            }
        }
        None
    }

    pub fn key(&mut self, code: KeyCode) {
        if code == KeyCode::Escape {
            self.open = false;
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64)) {
        self.hits.clear();
        self.hover = None;
        self.hover_raise = 0;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (560.0, 500.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let mid = r[1] + 32.0 * s;
        let ts = font(22.0);
        let title = ui.data.objects.values().find(|d| d.id == "The Pen Keeper").map_or("The Pen Keeper".to_owned(), |d| d.display_id.clone());
        ui.label(&title, r[0] + 22.0 * s, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        let (x0, mut y) = (r[0] + 22.0 * s, r[1] + 64.0 * s);
        let w = r[2] - 44.0 * s;
        // the pet out now
        let card = [x0, y, w, 96.0 * s];
        theme::frame(ui.renderer, card, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
        let state = self.state.clone();
        match &state {
            Some(p) if p.pet != 0 => {
                crate::hud::object_icon(ui, p.pet, card[0] + 48.0 * s, card[1] + card[3] / 2.0, 64.0 * s);
                let name = ui.data.objects.get(&p.pet).map_or(String::new(), |d| d.display_id.clone());
                ui.label(&name, card[0] + 96.0 * s, card[1] + 18.0 * s, font(17.0), true, color::TEXT);
                let size = if p.size != p.base_size && p.base_size != 255 {
                    tf!("{from}, raised to {to}", from = size_name(p.base_size), to = size_name(p.size))
                } else {
                    size_name(p.size).to_owned()
                };
                ui.label(&size, card[0] + 96.0 * s, card[1] + 46.0 * s, font(14.0), true, color::ACCENT);
            }
            Some(_) => ui.label(tr("You have no pet out."), card[0] + 20.0 * s, card[1] + 38.0 * s, font(14.0), false, color::TEXT_MUTED),
            None => ui.label(tr("Asking the Pen Keeper..."), card[0] + 20.0 * s, card[1] + 38.0 * s, font(14.0), false, color::TEXT_MUTED),
        }
        // the shards he keeps for the account, whatever pet is out
        if let Some(p) = &state {
            let kept = tf!("The Pen Keeper keeps {n} of your shards", n = p.shards);
            let kw = ui.measure(&kept, font(12.5), false);
            ui.label(&kept, card[0] + card[2] - 16.0 * s - kw, card[1] + card[3] - 26.0 * s, font(12.5), false, color::TEXT_DIM);
        }
        y += card[3] + 14.0 * s;

        // what's in the inventory for him
        let mut stones: Vec<(u8, i32)> = Vec::new();
        let (mut burgers, mut shards, mut ambrosia) = (0, 0, 0);
        if let Some(me) = me {
            let backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
            for slot in 4..if backpack { 20 } else { 12 } {
                let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
                let Some(desc) = (item > 0).then(|| ui.data.objects.get(&(item as u16))).flatten() else { continue };
                if desc.id == BURGER {
                    burgers += 1;
                } else if desc.id == AMBROSIA_ID {
                    ambrosia += 1;
                } else if let Some(n) = desc.id.strip_prefix(SHARD).and_then(|n| n.parse::<i32>().ok()) {
                    shards += n;
                } else if is_pet_stone(ui.data, item) {
                    stones.push((slot, item));
                }
            }
        }

        // the five acts, two by two
        let (size, has_pet, kept, needed) = state.as_ref().map_or((255, false, 0, 25), |p| (p.size, p.pet != 0, p.shards, p.shards_needed as i32));
        let bh = 32.0 * s;
        let bw = (w - 8.0 * s) / 2.0;
        let acts = [
            (TAKE_BACK, tr("Take your pet back").to_owned(), has_pet),
            (SHARDS, tf!("Give shards ({n} with you)", n = shards), shards > 0),
            (TO_MEDIUM, tf!("Raise to Medium ({n} Superburger)", n = burgers), has_pet && size == 1 && burgers > 0),
            (TO_LARGE, tf!("Raise to Large ({n} Ambrosia)", n = ambrosia), has_pet && size == 2 && ambrosia > 0),
            (AMBROSIA, tf!("Trade {needed} shards for an Ambrosia", needed), kept >= needed),
        ];
        for (k, (act, label, on)) in acts.iter().enumerate() {
            let br = [x0 + (k % 2) as f32 * (bw + 8.0 * s), y + (k / 2) as f32 * (bh + 8.0 * s), bw, bh];
            ui.button(br, label, if *on { Kind::Primary } else { Kind::Secondary }, *on && inside(br, mouse), s * 0.72);
            if *on {
                self.hits.push((Hit::Act(*act), br));
            }
        }
        y += acts.len().div_ceil(2) as f32 * (bh + 8.0 * s) + 10.0 * s;
        ui.label_fit(tr("Raising keeps your pet's look and its shots: it grows bigger, and hits and heals harder."), x0, y, w, font(12.0), false, color::TEXT_MUTED);
        y += 26.0 * s;

        // the pet stones in the inventory: a click puts that pet in
        ui.label(tr("Click a pet stone to put that pet in:"), x0, y, font(13.0), true, color::TEXT_DIM);
        y += 22.0 * s;
        let (cell, gap) = (44.0 * s, 6.0 * s);
        if stones.is_empty() {
            ui.label(tr("You have no pet stones with you."), x0, y + 12.0 * s, font(12.5), false, color::TEXT_MUTED);
        }
        for (k, &(slot, item)) in stones.iter().enumerate() {
            let cols = ((w + gap) / (cell + gap)).floor().max(1.0) as usize;
            let sr = [x0 + (k % cols) as f32 * (cell + gap), y + (k / cols) as f32 * (cell + gap), cell, cell];
            let over = inside(sr, mouse);
            ui.slot_box(sr, crate::screens::tier_edge(ui.data, item), over, s);
            ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
            let raised = me.and_then(|m| crate::hud::raised_pet_size(ui.data, item, m, slot));
            let raise = me.map_or(0, |m| crate::hud::pet_raise(m, slot));
            if let Some(size) = raised {
                // the raise, on the stone: the size's first letter in the corner
                let tag = &size[..size.char_indices().nth(1).map_or(size.len(), |(i, _)| i)];
                ui.label(tag, sr[0] + sr[2] - 12.0 * s, sr[1] + 2.0 * s, font(11.0), true, color::GOLD);
            }
            if over {
                self.hover = Some(item);
                self.hover_raise = raise;
            }
            self.hits.push((Hit::Stone(slot), sr));
        }
        if let Some((m, ok)) = &self.message {
            ui.label_fit(m, x0, r[1] + r[3] - 34.0 * s, w, font(13.0), true, if *ok { color::GOOD } else { color::DANGER });
        }
        if let Some(item) = self.hover {
            let lines = crate::hud::item_tooltip_raised(ui.data, item, me, crate::hud::SlotKind::Shop, [0; 4], self.hover_raise);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }
}
