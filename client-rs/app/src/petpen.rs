//! The Pen Keeper's window (the world server's `Player.PetPen.cs`), at the pet pen east of the
//! Arena court. Every pet hatches at level 1 and grows as he feeds it, up to its size's last level
//! (`petlevels`): the window shows the pet out, its level and the food to its next, what it does
//! now and at its last level, and what in the inventory it would eat and what for. Pick an item and
//! feed it, or pick a pet stone and put that pet in (the pet out goes back into its stone, with its
//! food). He keeps the account's Shards of the Mad God (given in his window, or used anywhere),
//! feeds them to the pet, or trades 25 of them for an Ambrosia. Feeding costs fame, more as the pet
//! grows (`petlevels::fame_cost`): the window says what before you feed. He feeds only pets of the
//! sizes the account's pen takes: Tiny at first, each bigger size bought with fame or gold. A pet
//! at its last level can be grown to the next size, for fame or gold, and levels on (at 100% until
//! it's Huge, then up to 120%), climbing towards what pets of its new size do. A Huge pet at its
//! last level can be tuned: pick the stat box to make stronger, then the one to take it from, 5% a
//! step for fame (from 2.6.7). A step is paid only on a second click on the same button, which
//! the first click turns into its price: players clicked through tens of thousands of fame trying
//! the buttons out (2.6.9).
//!
//! The window asks as it opens and after each act (`PetPenAct`); the server answers with the pet
//! as it is (`PetPen`).

use crate::hud::{group_digits, Layout, Line, Ui};
use crate::i18n::{tf, tr};
use crate::petlevels;
use crate::theme::{self, color, Kind};
use bsk_world::GameObj;
use winit::keyboard::KeyCode;

/// `PetPenAct`'s acts.
pub const ASK: u8 = 0;
pub const TAKE_BACK: u8 = 1;
pub const PUT_IN: u8 = 2;
pub const SHARDS: u8 = 4;
/// 25 of the shards he keeps for an Ambrosia (from 2.5.1).
pub const AMBROSIA: u8 = 6;
/// Feed the pet the item in a slot, or the shards he keeps (from 2.6.4).
pub const FEED: u8 = 7;
pub const FEED_KEPT: u8 = 8;
/// Grow the pen to take the next size of pets, for fame or for gold (from 2.6.4).
pub const PEN_FAME: u8 = 9;
pub const PEN_GOLD: u8 = 10;
/// Grow the pet out, at its last level, to the next size, for fame or for gold (from 2.6.5).
pub const GROW_FAME: u8 = 11;
pub const GROW_GOLD: u8 = 12;
/// Grow the pet out, at its last level, with the Growth Tonic in a slot (from 2.6.6).
pub const TONIC: u8 = 13;
/// Tune a Huge pet at its last level: a step from stat `slot / 3` to stat `slot % 3` (0 damage,
/// 1 HP, 2 MP; from 2.6.7).
pub const TUNE: u8 = 14;

const SHARD: &str = "Shard of the Mad God x";

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    Act(u8, u8),
    Pick(u8),
    /// A stat box, picked to make stronger by tuning.
    TuneUp(usize),
    /// A tuning step (`TUNE`'s slot): the first click asks, the second pays.
    TuneStep(u8),
}

/// The Pen Keeper's window.
#[derive(Default)]
pub struct PetPen {
    pub open: bool,
    state: Option<bsk_proto::PetPen>,
    /// What the last act came to: the server's words, whether it worked, and whether the pet grew.
    message: Option<(String, bool, bool)>,
    /// The inventory slot picked, and its item then.
    picked: Option<(u8, i32)>,
    /// The pet out's stone (its type, and its stone's), found once.
    stone: Option<(u16, i32)>,
    /// The stat picked to make stronger by tuning (0 damage, 1 HP, 2 MP).
    tune_up: Option<usize>,
    /// The tuning step clicked once, waiting for the click that pays for it.
    tune_armed: Option<u8>,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

fn size_name(size: &str) -> &str {
    crate::hud::pet_size_name(size)
}

/// A heal as "22 every 2s".
fn heal_text(amount: i16, ms: i16) -> String {
    let secs = ms as f64 / 1000.0;
    tf!("{amount} every {secs}s", amount, secs)
}

/// One thing in the inventory he can use: its slot and item, the food it is to the pet, and
/// whether it's a pet stone.
struct Food {
    slot: u8,
    item: i32,
    food: i32,
    stone: bool,
    xp: i32,
    /// The sizes a stone's pet was grown, and how it's tuned.
    grown: i32,
    tune: [i32; 3],
    /// The Realm Pass's: a treat fed without fame, or a Growth Tonic.
    free: bool,
    tonic: bool,
}

impl PetPen {
    /// Opens the window: ask the server for the pet.
    pub fn show(&mut self) -> u8 {
        self.open = true;
        self.message = None;
        self.picked = None;
        self.tune_armed = None;
        ASK
    }

    pub fn reset(&mut self) {
        *self = PetPen::default();
    }

    /// The server's answer.
    pub fn answer(&mut self, p: bsk_proto::PetPen) {
        let before = self.state.as_ref().filter(|s| s.pet == p.pet).and_then(|s| s.level).map(|l| l.level);
        let grew = matches!((before, p.level), (Some(b), Some(l)) if l.level > b);
        if !p.message.is_empty() {
            self.message = Some((p.message.clone(), p.ok, grew));
        }
        if p.ok && !p.message.is_empty() {
            // what was picked was used (fed, or put in)
            self.picked = None;
        }
        self.state = Some(p);
        // a step is asked for afresh after each answer
        self.tune_armed = None;
    }

    /// A press of the left button: an act to send (with an inventory slot), if any.
    pub fn click(&mut self, pos: (f64, f64)) -> Option<(u8, u8)> {
        match self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h) {
            Some(Hit::Close) => self.open = false,
            Some(Hit::Act(a, slot)) => return Some((a, slot)),
            Some(Hit::TuneUp(k)) => {
                self.tune_up = if self.tune_up == Some(k) { None } else { Some(k) };
                self.tune_armed = None;
            }
            Some(Hit::TuneStep(slot)) => {
                if self.tune_armed == Some(slot) {
                    self.tune_armed = None;
                    return Some((TUNE, slot));
                }
                self.tune_armed = Some(slot);
            }
            Some(Hit::Pick(slot)) => {
                self.picked = match self.picked {
                    Some((s, _)) if s == slot => None,
                    // its item is filled in as it's drawn
                    _ => Some((slot, 0)),
                };
            }
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

    /// The pet's stone (the item whose PermaPet gives it).
    fn stone_of(&mut self, data: &bsk_data::GameData, pet: u16) -> Option<i32> {
        if pet == 0 {
            return None;
        }
        if self.stone.map(|(p, _)| p) != Some(pet) {
            let id = data.objects.get(&pet).map(|d| d.id.clone()).unwrap_or_default();
            let stone = data
                .objects
                .iter()
                .find(|(_, d)| d.xml.children_named("Activate").any(|a| a.text.trim() == "PermaPet" && a.attr("objectId") == Some(id.as_str())))
                .map_or(0, |(&t, _)| t as i32);
            self.stone = Some((pet, stone));
        }
        self.stone.map(|(_, s)| s).filter(|&s| s > 0)
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64)) {
        self.hits.clear();
        if !self.open {
            self.rect = None;
            return;
        }
        // a Huge pet at its last level can be tuned (an older server says nothing of it)
        let tune = self.state.as_ref().filter(|p| p.pet != 0).and_then(|p| p.tune).filter(|t| t.fame > 0);
        if tune.is_none() {
            self.tune_up = None;
        }
        if self.tune_up.is_none() {
            self.tune_armed = None;
        }
        let tune_h = if tune.is_some() { 44.0 } else { 0.0 };
        let (dw, dh) = (600.0, 694.0 + tune_h);
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

        let (x0, mut y) = (r[0] + 22.0 * s, r[1] + 62.0 * s);
        let w = r[2] - 44.0 * s;
        let state = self.state.clone();
        let pet = state.as_ref().map_or(0, |p| p.pet);
        let stone = self.stone_of(ui.data, pet);
        let level = state.as_ref().and_then(|p| p.level).filter(|_| pet != 0);
        let growing = level.is_some_and(|l| l.max_level > 1 && l.level < l.max_level);
        // the pen (sent with or without a pet): the biggest pets he feeds, and what the next costs
        let pen = state.as_ref().and_then(|p| p.level).unwrap_or_default();
        let pen_size = pen.pen_size as usize;
        // the pet's size as grown (an older server: its stone's), and what growing it costs
        let grow = state.as_ref().and_then(|p| p.grow).filter(|_| pet != 0);
        let pet_size = match grow {
            Some(g) => (g.pet_size as usize).lt(&petlevels::SIZES.len()).then_some(g.pet_size as usize),
            None => stone.and_then(|st| petlevels::stone(ui.data, st)).and_then(|a| petlevels::size_index(a.attr("petSize"))),
        };
        let grow = grow.filter(|g| g.fame > 0 && pet_size.is_some_and(|i| i + 1 < petlevels::SIZES.len()));
        let fed_here = pet_size.is_none_or(|i| i <= pen_size);
        // whether he'll feed the pet out
        let feedable = growing && fed_here;
        // the account's fame and gold, and what feeding `food` costs
        let fame = me.and_then(|m| m.stat_int(39)).unwrap_or(0);
        let gold = me.and_then(|m| m.stat_int(35)).unwrap_or(0);
        let cost = |food: i32| level.map_or(0, |l| petlevels::fame_cost(l.xp, food, l.max_level as i32));

        // the pet out: its look, name, size and level, and the food to its next level
        let card = [x0, y, w, 128.0 * s];
        theme::frame(ui.renderer, card, 10.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::WARM_TOP, 1.0), (color::WARM_BOTTOM, 1.0)));
        match &state {
            Some(p) if p.pet != 0 => {
                let (icx, icy) = (card[0] + 64.0 * s, card[1] + card[3] / 2.0);
                let disc = [icx - 50.0 * s, icy - 50.0 * s, 100.0 * s, 100.0 * s];
                theme::fill(ui.renderer, disc, 50.0 * s, color::GOLD, if growing { 0.06 } else { 0.12 });
                crate::hud::object_icon(ui, p.pet, icx, icy, 80.0 * s);
                let tx = card[0] + 132.0 * s;
                let tw = card[0] + card[2] - 18.0 * s - tx;
                let name = ui.data.objects.get(&p.pet).map_or(String::new(), |d| d.display_id.clone());
                ui.label_fit(&name, tx, card[1] + 14.0 * s, tw, font(18.0), true, color::TEXT);
                let size = pet_size.map_or("", |i| size_name(petlevels::SIZES[i]));
                match level {
                    Some(l) if l.max_level > 1 => {
                        let line = tf!("{size} · Level {level} of {max}", size, level = l.level, max = l.max_level);
                        ui.label(&line, tx, card[1] + 42.0 * s, font(14.0), true, color::ACCENT);
                        // the food bar: this level's share, gold; full and green at its last level
                        let bar = [tx, card[1] + 70.0 * s, tw, 14.0 * s];
                        theme::fill(ui.renderer, bar, 7.0 * s, color::TRACK, 1.0);
                        let span = (l.xp_next - l.xp_level).max(1);
                        let part = if growing { ((l.xp - l.xp_level) as f32 / span as f32).clamp(0.0, 1.0) } else { 1.0 };
                        if part > 0.0 {
                            let fw = (bar[2] * part).max(bar[3]);
                            theme::rounded(ui.renderer, [bar[0], bar[1], fw, bar[3]], 7.0 * s, (if growing { color::GOLD } else { color::GOOD }, 1.0), (theme::shade(if growing { color::ACCENT } else { color::GOOD }, 0.75), 1.0));
                        }
                        // the whole climb, thin under it
                        let all = [tx, bar[1] + bar[3] + 4.0 * s, tw, 3.0 * s];
                        theme::fill(ui.renderer, all, 1.5 * s, color::TRACK, 1.0);
                        let last = petlevels::xp_at(l.max_level as i32).max(1);
                        theme::fill(ui.renderer, [all[0], all[1], all[2] * (l.xp as f32 / last as f32).clamp(0.0, 1.0), all[3]], 1.5 * s, color::GOLD, 0.6);
                        let (text, c) = if let (false, Some(g), Some(i)) = (growing, grow, pet_size) {
                            // at its last level and able to grow: to the next size, for fame or gold
                            let bw = 100.0 * s;
                            for (k, (act, label, on)) in [
                                (GROW_FAME, tf!("{fame} fame", fame = group_digits(g.fame)), fame >= g.fame),
                                (GROW_GOLD, tf!("{gold} gold", gold = group_digits(g.gold)), gold >= g.gold),
                            ]
                            .into_iter()
                            .enumerate()
                            {
                                let br = [tx + tw - (2 - k) as f32 * bw - (1 - k) as f32 * 6.0 * s, card[1] + 94.0 * s, bw, 26.0 * s];
                                ui.button(br, &label, if on { Kind::Primary } else { Kind::Secondary }, on && inside(br, mouse), s * 0.62);
                                if on {
                                    self.hits.push((Hit::Act(act, 0), br));
                                }
                            }
                            let line = tf!("Grow it to a {size} pet:", size = size_name(petlevels::SIZES[i + 1]));
                            ui.label_fit(&line, tx, card[1] + 99.0 * s, tw - 2.0 * bw - 14.0 * s, font(12.5), true, color::GOLD);
                            (String::new(), color::GOOD)
                        } else if !growing && tune.is_some() {
                            (tr("At its last level: tune it below to suit you.").to_owned(), color::GOOD)
                        } else if !growing {
                            (tr("At its last level: as strong as it gets.").to_owned(), color::GOOD)
                        } else if !fed_here {
                            (tf!("Your pen only takes {size} pets: grow it to feed this one.", size = size_name(petlevels::SIZES[pen_size])), color::DANGER)
                        } else {
                            (tf!("{have} / {need} food to level {next}", have = group_digits(l.xp - l.xp_level), need = group_digits(span), next = l.level + 1), color::TEXT_DIM)
                        };
                        if !text.is_empty() {
                            ui.label_fit(&text, tx, card[1] + 96.0 * s, tw, font(12.5), false, c);
                        }
                    }
                    _ => {
                        ui.label(size, tx, card[1] + 42.0 * s, font(14.0), true, color::ACCENT);
                        ui.label_fit(tr("This pet doesn't grow."), tx, card[1] + 70.0 * s, tw, font(12.5), false, color::TEXT_MUTED);
                    }
                }
            }
            Some(_) => ui.label(tr("You have no pet out. Pick a pet stone below to put one in."), card[0] + 22.0 * s, card[1] + 54.0 * s, font(14.0), false, color::TEXT_MUTED),
            None => ui.label(tr("Asking the Pen Keeper..."), card[0] + 22.0 * s, card[1] + 54.0 * s, font(14.0), false, color::TEXT_MUTED),
        }
        y += card[3] + 12.0 * s;

        // what it does now, and at its last level
        let stats_h = 66.0 * s;
        if let Some(l) = level {
            let steps = tune.map_or([0; 3], |t| [t.damage as i32, t.hp as i32, t.mp as i32]);
            // under each: its last level's while it grows; once tunable, how it's tuned
            let at_last = |v: String, k: usize| match steps[k] {
                _ if growing => (tf!("Level {max}: {value}", max = l.max_level, value = v), color::TEXT_DIM),
                0 => (tr("Its best").to_owned(), color::GOOD),
                n => (tf!("Tuned {pct}", pct = format!("{:+}%", n * petlevels::TUNE_STEP)), if n > 0 { color::GOOD } else { color::DANGER }),
            };
            let boxes = [
                (tr("Damage"), tf!("{dps} a second", dps = group_digits(l.dps)), at_last(group_digits(l.dps_max), 0), color::ACCENT_HI),
                (tr("Heals HP"), if l.hp > 0 { heal_text(l.hp, l.hp_ms) } else { "-".to_owned() }, at_last(l.hp_max.to_string(), 1), color::GOOD),
                (tr("Restores MP"), if l.mp > 0 { heal_text(l.mp, l.mp_ms) } else { "-".to_owned() }, at_last(l.mp_max.to_string(), 2), 0x6FA8FF),
            ];
            let bw = (w - 16.0 * s) / 3.0;
            for (k, (label, now, (last, last_c), tint)) in boxes.iter().enumerate() {
                let br = [x0 + k as f32 * (bw + 8.0 * s), y, bw, stats_h];
                // a tunable pet's boxes are picked to make that stronger: gold round the pick
                let up = self.tune_up == Some(k);
                if tune.is_some() {
                    if up {
                        theme::fill(ui.renderer, [br[0] - 2.0 * s, br[1] - 2.0 * s, br[2] + 4.0 * s, br[3] + 4.0 * s], 10.0 * s, color::GOLD, 0.9);
                    }
                    self.hits.push((Hit::TuneUp(k), br));
                }
                let edge = if tune.is_some() && inside(br, mouse) && !up { color::BORDER_HI } else { color::BORDER };
                theme::frame(ui.renderer, br, 8.0 * s, s.max(1.0), (edge, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
                theme::fill(ui.renderer, [br[0] + 10.0 * s, br[1] + 10.0 * s, 3.0 * s, 46.0 * s], 1.5 * s, *tint, 0.9);
                let tx = br[0] + 20.0 * s;
                let tw = br[2] - 28.0 * s;
                ui.label_fit(label, tx, br[1] + 8.0 * s, tw, font(11.5), true, color::TEXT_MUTED);
                ui.label_fit(now, tx, br[1] + 24.0 * s, tw, font(16.0), true, color::TEXT);
                ui.label_fit(last, tx, br[1] + 46.0 * s, tw, font(11.0), false, *last_c);
            }
            // tuning: what to make stronger is picked above; what it's taken from here
            if let Some(t) = tune {
                let ty = y + stats_h + 8.0 * s;
                let row = [x0, ty, w, 36.0 * s];
                theme::frame(ui.renderer, row, 8.0 * s, s.max(1.0), (if self.tune_up.is_some() { color::GOLD } else { color::BORDER }, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
                let ly = row[1] + (row[3] - font(12.5) * 1.22) / 2.0;
                let step = petlevels::TUNE_STEP;
                match self.tune_up {
                    None => {
                        let line = tf!("Tune it: pick what to make stronger ({step}% a step, {fame} fame).", step, fame = group_digits(t.fame));
                        ui.label_fit(&line, row[0] + 14.0 * s, ly, row[2] - 28.0 * s, font(12.5), true, color::TEXT_DIM);
                    }
                    Some(up) => {
                        let names = [tr("Damage"), tr("HP healing"), tr("MP healing")];
                        let bw = 112.0 * s;
                        let mut bx = row[0] + row[2] - 6.0 * s;
                        for from in (0..3).rev().filter(|&k| k != up) {
                            bx -= bw;
                            let br = [bx, row[1] + 4.0 * s, bw, row[3] - 8.0 * s];
                            let on = steps[up] < petlevels::TUNE_MAX && steps[from] > -petlevels::TUNE_MAX && fame >= t.fame;
                            let slot = (from * 3 + up) as u8;
                            if !on && self.tune_armed == Some(slot) {
                                self.tune_armed = None;
                            }
                            if self.tune_armed == Some(slot) {
                                let pay = tf!("Pay {fame} fame", fame = group_digits(t.fame));
                                ui.button(br, &pay, Kind::Danger, inside(br, mouse), s * 0.62);
                            } else {
                                ui.button(br, names[from], if on { Kind::Primary } else { Kind::Secondary }, on && inside(br, mouse), s * 0.62);
                            }
                            if on {
                                self.hits.push((Hit::TuneStep(slot), br));
                            }
                            bx -= 6.0 * s;
                        }
                        let line = match up {
                            _ if self.tune_armed.is_some() => tf!("Each step costs {fame} fame. Click again to pay:", fame = group_digits(t.fame)),
                            0 => tf!("More damage, {step}% taken from:", step),
                            1 => tf!("More HP healing, {step}% taken from:", step),
                            _ => tf!("More MP healing, {step}% taken from:", step),
                        };
                        let c = if self.tune_armed.is_some() {
                            color::DANGER
                        } else if steps[up] >= petlevels::TUNE_MAX {
                            color::TEXT_MUTED
                        } else {
                            color::GOLD
                        };
                        ui.label_fit(&line, row[0] + 14.0 * s, ly, bx - row[0] - 20.0 * s, font(12.5), true, c);
                    }
                }
            }
        }
        y += stats_h + 14.0 * s + tune_h;

        // what's in the inventory for him: food and pet stones, best food first
        let mut foods: Vec<Food> = Vec::new();
        let mut shards = 0;
        if let Some(me) = me {
            let backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
            for slot in 4..if backpack { 20 } else { 12 } {
                let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
                let Some(desc) = (item > 0).then(|| ui.data.objects.get(&(item as u16))).flatten() else { continue };
                let json = me.stat_str(crate::hud::slot_data_stat(slot)).unwrap_or("");
                if let Some(n) = desc.id.strip_prefix(SHARD) {
                    let q = desc.xml.int("Quantity", 0);
                    shards += if q > 0 { q } else { n.trim().parse::<i32>().unwrap_or(1).max(1) };
                }
                let stone = petlevels::stone(ui.data, item).is_some();
                let food = petlevels::slot_food(ui.data, item, json);
                let tonic = desc.id == petlevels::GROWTH_TONIC;
                if food > 0 || stone || tonic {
                    let xp = if stone { petlevels::stone_xp(ui.data, item, json) } else { 0 };
                    let grown = if stone { petlevels::stone_grown(json) } else { 0 };
                    let tune = if stone { petlevels::stone_tune(json) } else { [0; 3] };
                    foods.push(Food { slot, item, food, stone, xp, grown, tune, free: petlevels::treat_free(&desc.id), tonic });
                }
            }
        }
        foods.sort_by_key(|f| (f.stone, !f.tonic, -f.food, f.slot));
        // what was picked is still there, or nothing is
        self.picked = self.picked.and_then(|(slot, _)| foods.iter().find(|f| f.slot == slot).map(|f| (slot, f.item)));

        let header = if feedable { tr("Feed your pet, or put in another:") } else { tr("Your pet stones and food:") };
        ui.label(header, x0, y, font(13.0), true, color::TEXT_DIM);
        y += 22.0 * s;
        let (cell, gap) = (46.0 * s, 6.0 * s);
        let cols = ((w + gap) / (cell + gap)).floor().max(1.0) as usize;
        let grid_h = 2.0 * cell + gap;
        if foods.is_empty() {
            ui.label_fit(tr("Nothing in your inventory for him: pets eat gear, potions, treats and other pets' stones."), x0, y + 36.0 * s, w, font(12.5), false, color::TEXT_MUTED);
        }
        let mut hover: Option<(i32, i32, crate::hud::PetStone)> = None;
        for (k, f) in foods.iter().enumerate().take(cols * 2) {
            let sr = [x0 + (k % cols) as f32 * (cell + gap), y + (k / cols) as f32 * (cell + gap), cell, cell];
            let over = inside(sr, mouse);
            let picked = self.picked.is_some_and(|(slot, _)| slot == f.slot);
            if picked {
                theme::fill(ui.renderer, [sr[0] - 3.0 * s, sr[1] - 3.0 * s, sr[2] + 6.0 * s, sr[3] + 6.0 * s], 8.0 * s, color::GOLD, 0.85);
            }
            ui.slot_box(sr, crate::screens::tier_edge(ui.data, f.item), over || picked, s);
            ui.item_icon(f.item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
            if f.stone {
                // a stone's pet's level, in the corner
                let lv = petlevels::level_for(f.xp, petlevels::stone_max_level(ui.data, f.item, f.grown));
                if lv > 1 {
                    let tag = lv.to_string();
                    let tw = ui.measure(&tag, font(10.5), true);
                    ui.label(&tag, sr[0] + sr[2] - 4.0 * s - tw, sr[1] + 2.0 * s, font(10.5), true, color::GOLD);
                }
            }
            if over {
                hover = Some((f.item, f.food, (f.xp, f.grown, f.tune)));
            }
            self.hits.push((Hit::Pick(f.slot), sr));
        }
        y += grid_h + 10.0 * s;

        // what was picked, and what it would do
        let bar = [x0, y, w, 50.0 * s];
        theme::frame(ui.renderer, bar, 8.0 * s, s.max(1.0), (if self.picked.is_some() { color::GOLD } else { color::BORDER }, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
        let bh = 32.0 * s;
        let by = bar[1] + (bar[3] - bh) / 2.0;
        match self.picked.and_then(|(slot, _)| foods.iter().find(|f| f.slot == slot)) {
            Some(f) => {
                ui.item_icon(f.item, 60, bar[0] + 26.0 * s, bar[1] + bar[3] / 2.0, s);
                let feed_w = 96.0 * s;
                let put_w = if f.stone { 120.0 * s } else { 0.0 };
                let feed_r = [bar[0] + bar[2] - 10.0 * s - feed_w, by, feed_w, bh];
                let put_r = [feed_r[0] - 8.0 * s - put_w, by, put_w, bh];
                let tx = bar[0] + 50.0 * s;
                let tw = (if f.stone { put_r[0] } else { feed_r[0] }) - 10.0 * s - tx;
                let name = ui.data.objects.get(&(f.item as u16)).map_or(String::new(), |d| d.display_id.clone());
                ui.label_fit(&name, tx, bar[1] + 7.0 * s, tw, font(13.0), true, color::TEXT);
                // the Realm Pass's treats cost no fame
                let price = if f.free { 0 } else { cost(f.food) };
                let what = match level {
                    _ if f.tonic => match pet_size {
                        _ if pet == 0 => (tr("Put a pet in first.").to_owned(), color::TEXT_MUTED),
                        None => (tr("This pet doesn't grow.").to_owned(), color::TEXT_MUTED),
                        Some(i) if !petlevels::tonic_grows(i) => (tr("A Growth Tonic only grows Tiny, Small and Medium pets.").to_owned(), color::TEXT_MUTED),
                        Some(_) if grow.is_none() => (tr("Feed your pet to its last level first.").to_owned(), color::TEXT_MUTED),
                        Some(i) => (tf!("Grows your pet to a {size} pet, for free.", size = size_name(petlevels::SIZES[i + 1])), color::GOLD),
                    },
                    Some(l) if feedable && f.food > 0 => {
                        let last = petlevels::xp_at(l.max_level as i32);
                        let after = petlevels::level_for((l.xp + f.food).min(last), l.max_level as i32);
                        let gain = if l.xp + f.food > last {
                            tf!("+{food} food, to its last level ({spare} to spare)", food = group_digits(f.food), spare = group_digits(l.xp + f.food - last))
                        } else if after > l.level as i32 {
                            tf!("+{food} food, up to level {after}", food = group_digits(f.food), after)
                        } else {
                            tf!("+{food} food", food = group_digits(f.food))
                        };
                        let line = if f.free { tf!("{gain}, without fame", gain) } else { tf!("{gain} for {fame} fame", gain, fame = group_digits(price)) };
                        (line, if price > fame { color::DANGER } else if l.xp + f.food > last { color::GOLD } else { color::GOOD })
                    }
                    _ if f.food == 0 => (tr("Pets won't eat this.").to_owned(), color::TEXT_MUTED),
                    _ if pet == 0 => (tr("Put a pet in to feed it.").to_owned(), color::TEXT_MUTED),
                    _ if growing => (tr("Grow your pen to feed this pet.").to_owned(), color::TEXT_MUTED),
                    _ => (tr("Your pet doesn't need more food.").to_owned(), color::TEXT_MUTED),
                };
                ui.label_fit(&what.0, tx, bar[1] + 27.0 * s, tw, font(12.0), false, what.1);
                if f.tonic {
                    let can_grow = pet != 0 && grow.is_some() && pet_size.is_some_and(petlevels::tonic_grows);
                    ui.button(feed_r, tr("Grow it"), if can_grow { Kind::Primary } else { Kind::Secondary }, can_grow && inside(feed_r, mouse), s * 0.75);
                    if can_grow {
                        self.hits.push((Hit::Act(TONIC, f.slot), feed_r));
                    }
                } else {
                    let can_feed = feedable && f.food > 0 && price <= fame;
                    ui.button(feed_r, tr("Feed"), if can_feed { Kind::Primary } else { Kind::Secondary }, can_feed && inside(feed_r, mouse), s * 0.75);
                    if can_feed {
                        self.hits.push((Hit::Act(FEED, f.slot), feed_r));
                    }
                }
                if f.stone {
                    ui.button(put_r, tr("Put this pet in"), Kind::Secondary, inside(put_r, mouse), s * 0.72);
                    self.hits.push((Hit::Act(PUT_IN, f.slot), put_r));
                }
            }
            None => {
                let hint = if foods.is_empty() { tr("Bring him something to feed your pet.") } else { tr("Pick something to feed your pet, or a pet stone to put that pet in.") };
                ui.label_fit(hint, bar[0] + 16.0 * s, bar[1] + (bar[3] - font(12.5) * 1.22) / 2.0, bar[2] - 32.0 * s, font(12.5), false, color::TEXT_MUTED);
            }
        }
        y += bar[3] + 12.0 * s;

        // the pen: the sizes it takes lit, and growing it to the next
        let pen_r = [x0, y, w, 76.0 * s];
        theme::frame(ui.renderer, pen_r, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
        ui.label(tr("Your pen feeds:"), pen_r[0] + 14.0 * s, pen_r[1] + 10.0 * s, font(12.5), true, color::TEXT_DIM);
        let chips_x = pen_r[0] + 14.0 * s;
        let chip_w = 62.0 * s;
        for (i, name) in petlevels::SIZES.iter().enumerate() {
            let cr = [chips_x + i as f32 * (chip_w + 5.0 * s), pen_r[1] + 36.0 * s, chip_w, 28.0 * s];
            let on = i <= pen_size;
            let here = pet_size == Some(i);
            // the pet out's size ringed: green if he feeds it, red if not
            if here {
                let ring = 2.0 * s;
                theme::fill(ui.renderer, [cr[0] - ring, cr[1] - ring, cr[2] + 2.0 * ring, cr[3] + 2.0 * ring], 7.0 * s, if on { color::GOOD } else { color::DANGER }, 1.0);
                theme::fill(ui.renderer, cr, 6.0 * s, color::INSET_BOTTOM, 1.0);
            }
            theme::fill(ui.renderer, cr, 6.0 * s, if on { color::GOOD } else { color::CHIP }, if on { 0.22 } else { 1.0 });
            let label = size_name(name);
            let lw = ui.measure(label, font(11.5), true).min(cr[2] - 6.0 * s);
            ui.label_fit(label, cr[0] + (cr[2] - lw) / 2.0, cr[1] + (cr[3] - font(11.5) * 1.22) / 2.0, cr[2] - 6.0 * s, font(11.5), true, if on { color::GOOD } else { color::TEXT_MUTED });
        }
        let grow_x = chips_x + 5.0 * (chip_w + 5.0 * s) + 6.0 * s;
        let grow_w = (pen_r[0] + pen_r[2] - 10.0 * s - grow_x - 6.0 * s) / 2.0;
        if pen_size + 1 < petlevels::SIZES.len() && pen.pen_fame > 0 {
            for (k, (act, label, on)) in [
                (PEN_FAME, tf!("{fame} fame", fame = group_digits(pen.pen_fame)), fame >= pen.pen_fame),
                (PEN_GOLD, tf!("{gold} gold", gold = group_digits(pen.pen_gold)), gold >= pen.pen_gold),
            ]
            .into_iter()
            .enumerate()
            {
                let br = [grow_x + k as f32 * (grow_w + 6.0 * s), pen_r[1] + 34.0 * s, grow_w, 32.0 * s];
                ui.button(br, &label, if on { Kind::Primary } else { Kind::Secondary }, on && inside(br, mouse), s * 0.66);
                if on {
                    self.hits.push((Hit::Act(act, 0), br));
                }
            }
            let next = tf!("Grow it to {size} pets:", size = size_name(petlevels::SIZES[pen_size + 1]));
            ui.label_fit(&next, grow_x, pen_r[1] + 10.0 * s, pen_r[0] + pen_r[2] - 10.0 * s - grow_x, font(12.0), true, color::TEXT_DIM);
        } else if state.is_some() {
            ui.label_fit(tr("Every pet, big or small."), grow_x, pen_r[1] + 42.0 * s, pen_r[0] + pen_r[2] - 14.0 * s - grow_x, font(12.0), true, color::GOOD);
        }
        y += pen_r[3] + 12.0 * s;

        // the shards he keeps, and the rest of his work
        let (kept, needed) = state.as_ref().map_or((0, 25), |p| (p.shards, p.shards_needed as i32));
        let kept_line = tf!("He keeps {n} of your Shards of the Mad God", n = group_digits(kept));
        ui.label(&kept_line, x0, y, font(12.5), true, color::TEXT_DIM);
        let money = tf!("{fame} fame · {gold} gold", fame = group_digits(fame), gold = group_digits(gold));
        let mw = ui.measure(&money, font(12.5), true);
        ui.label(&money, x0 + w - mw, y, font(12.5), true, 0xF07A1E);
        y += 20.0 * s;
        let bw = (w - 8.0 * s) / 2.0;
        let acts = [
            (TAKE_BACK, tr("Take your pet back").to_owned(), pet != 0),
            (FEED_KEPT, tf!("Feed it his {n} shards ({fame} fame)", n = kept, fame = group_digits(cost(kept * 200))), feedable && kept > 0 && cost(kept * 200) <= fame),
            (
                SHARDS,
                if feedable { tf!("Feed it your {n} shards ({fame} fame)", n = shards, fame = group_digits(cost(shards * 200))) } else { tf!("Give him your shards ({n} with you)", n = shards) },
                shards > 0 && (!feedable || cost(shards * 200) <= fame),
            ),
            (AMBROSIA, tf!("Trade {needed} shards for an Ambrosia", needed), kept >= needed),
        ];
        for (k, (act, label, on)) in acts.iter().enumerate() {
            let br = [x0 + (k % 2) as f32 * (bw + 8.0 * s), y + (k / 2) as f32 * (bh + 8.0 * s), bw, bh];
            ui.button(br, label, if *on { Kind::Primary } else { Kind::Secondary }, *on && inside(br, mouse), s * 0.7);
            if *on {
                self.hits.push((Hit::Act(*act, 0), br));
            }
        }

        if let Some((m, ok, grew)) = &self.message {
            let my = r[1] + r[3] - 32.0 * s;
            let mx = if *grew {
                theme::star(ui.renderer, x0 + 8.0 * s, my + 8.0 * s, 8.0 * s, color::GOLD);
                x0 + 22.0 * s
            } else {
                x0
            };
            let c = if *grew { color::GOLD } else if *ok { color::GOOD } else { color::DANGER };
            ui.label_fit(m, mx, my, w - (mx - x0), font(13.0), true, c);
        }
        if let Some((item, food, xp)) = hover {
            let mut lines = crate::hud::item_tooltip_pet(ui.data, item, me, crate::hud::SlotKind::Shop, [0; 4], xp);
            if food > 0 {
                lines.push(Line::gap());
                lines.push(Line::new(&tf!("Food for your pet: +{food}", food = group_digits(food)), 13.0, true, color::GOOD));
                if ui.data.objects.get(&(item as u16)).is_some_and(|d| petlevels::treat_free(&d.id)) {
                    lines.push(Line::new(tr("Fed without fame."), 12.0, false, color::GOLD));
                }
            }
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }
}
