//! The Enchanter's window: the forge window in its other mode, its one tab, Imprints. The
//! player's gear (equipped and in the inventory) on the left, with the imprints each carries;
//! the picked piece on the right, its three imprint slots (red Offensive, green Defensive, purple
//! Misc, and a fourth colour coming soon), and the imprints of the colour shown, each with what it
//! does, what it fits and a button to put it on for its shard (`bsk_data::imprint`). The server
//! checks everything (`ImprintHandlers.cs`).

use super::{inside, locked_chip, Act, Category, Forge, Hit, Tab, ENCHANTER, FRAME_MS};
use crate::hud::Ui;
use crate::theme::{self, color, Kind as ButtonKind};
use bsk_data::imprint::{self, Kind};
use bsk_data::GameData;

/// The colour tabs: the three imprint kinds, then the one coming soon.
const COMING_SOON: usize = 3;

impl Forge {
    /// Opens the window as the Enchanter; it asks for the forge storage's shards.
    pub fn show_enchanter(&mut self, data: &GameData) -> Option<Act> {
        self.open = true;
        self.enchanter = true;
        self.tab = Tab::Imprints;
        self.message = None;
        self.imp_pending = false;
        if self.kinds.is_empty() {
            self.kinds = super::slot_categories(data);
        }
        Some(Act::ImprintShards)
    }

    /// Whether an item is gear that takes imprints (a class wears it as weapon, ability, armour
    /// or ring).
    fn imprintable(&self, data: &GameData, item: i32) -> bool {
        let (cat, _) = self.category_of(data, item as u16);
        cat != Category::Other && cat != Category::All && !data.objects.get(&(item as u16)).is_some_and(|d| d.xml.has("Consumable"))
    }

    /// How many shards that pay for an imprint of this colour the storage and the inventory hold.
    fn shards_for(&self, data: &GameData, kind: Kind) -> (i32, i32) {
        let types: Vec<u16> = kind.cost().iter().filter_map(|id| data.object_ids.get(*id).copied()).collect();
        let stored = types.iter().map(|&t| self.stored(t)).sum();
        let held = self.inventory.iter().filter(|(_, t)| types.contains(&(*t as u16))).count() as i32;
        (stored, held)
    }

    /// Asks for an imprint on the picked item, if it fits and a shard can pay for it.
    pub(super) fn imprint(&mut self, id: u8, data: &GameData) -> Option<Act> {
        let slot = self.imp_pick?;
        let &(_, item, current) = self.gear.iter().find(|g| g.0 == slot)?;
        let imp = imprint::get(id as i32)?;
        let slot_type = data.objects.get(&(item as u16)).map_or(0, |d| d.xml.int("SlotType", 0));
        let (stored, held) = self.shards_for(data, imp.kind);
        self.message = Some(if self.imp_pending {
            return None;
        } else if !imp.fits(slot_type) {
            (format!("{} doesn't fit this item.", imp.name), true)
        } else if current[imp.kind as usize] == id {
            (format!("It already carries {}.", imp.name), true)
        } else if stored + held <= 0 {
            (format!("You need an {}.", imp.kind.cost().join(" or an ")), true)
        } else {
            self.imp_pending = true;
            self.message = None;
            return Some(Act::Imprint(slot, id));
        });
        None
    }

    pub(super) fn draw_imprints(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        // the picked item may have moved or gone
        if let Some(p) = self.imp_pick {
            if !self.gear.iter().any(|g| g.0 == p && self.imprintable(ui.data, g.1)) {
                self.imp_pick = None;
            }
        }
        let left = [body[0], body[1], 270.0 * s, body[3]];
        let right = [left[0] + left[2] + 20.0 * s, body[1], body[2] - left[2] - 20.0 * s, body[3]];

        // The gear: equipped, then the inventory (and the backpack).
        let cell = 54.0 * s;
        let gap = 8.0 * s;
        let cols = 4;
        let mut y = left[1];
        let gear = self.gear.clone();
        let groups: [(&str, Vec<u8>); 2] = [
            ("Equipped", (0..4).collect()),
            ("Inventory", (4..if self.backpack { 20 } else { 12 }).collect()),
        ];
        for (label, slots) in groups {
            ui.label(label, left[0], y, font(13.0), true, color::TEXT_DIM);
            y += 22.0 * s;
            for (n, slot) in slots.iter().enumerate() {
                let sr = [left[0] + (n % cols) as f32 * (cell + gap), y + (n / cols) as f32 * (cell + gap), cell, cell];
                let found = gear.iter().find(|g| g.0 == *slot).copied();
                let picked = self.imp_pick == Some(*slot);
                let usable = found.is_some_and(|g| self.imprintable(ui.data, g.1));
                let over = inside(sr, mouse);
                let edge = if picked { Some(color::GOLD) } else { found.and_then(|g| crate::screens::tier_edge(ui.data, g.1)) };
                ui.slot_box(sr, edge, over && usable, s);
                if let Some((_, item, imps)) = found {
                    ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
                    crate::hud::draw_imprint_marks(ui, imps, sr[0] + 3.0 * s, sr[1] + 3.0 * s, s);
                    if !usable {
                        theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
                    }
                    if over {
                        self.hover_item = Some(item);
                        self.hover_imprints = imps;
                    }
                    if usable {
                        self.hits.push((Hit::ImpItem(*slot), sr));
                    }
                }
            }
            y += slots.len().div_ceil(cols) as f32 * (cell + gap) + 10.0 * s;
        }

        theme::frame(ui.renderer, right, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
        let frame = ENCHANTER + ((now / FRAME_MS).rem_euclid(6)) as u32;
        let Some(&(slot, item, current)) = self.imp_pick.and_then(|p| gear.iter().find(|g| g.0 == p)) else {
            let (cx, cy) = (right[0] + right[2] / 2.0, right[1] + right[3] * 0.45);
            ui.tile_icon(super::SHEET, frame, 110, cx, cy, s);
            for (k, t) in ["Pick a piece of gear to imprint.", "Imprints work only while the item is equipped."].iter().enumerate() {
                let fs = font(if k == 0 { 15.0 } else { 12.0 });
                let w = ui.measure(t, fs, k == 0);
                ui.label(t, cx - w / 2.0, right[1] + 24.0 * s + k as f32 * 24.0 * s, fs, k == 0, if k == 0 { color::TEXT } else { color::TEXT_MUTED });
            }
            return;
        };
        let pad = 16.0 * s;
        let (x0, mut y) = (right[0] + pad, right[1] + pad);
        let inner_w = right[2] - 2.0 * pad;
        // The item: its icon, name and kind, the Enchanter beside it.
        let ib = [x0, y, 56.0 * s, 56.0 * s];
        ui.slot_box(ib, crate::screens::tier_edge(ui.data, item), false, s);
        ui.item_icon(item, 80, ib[0] + ib[2] / 2.0, ib[1] + ib[3] / 2.0, s);
        crate::hud::draw_imprint_marks(ui, current, ib[0] + 3.0 * s, ib[1] + 3.0 * s, s);
        let desc = ui.data.objects.get(&(item as u16));
        let name = desc.map_or(String::new(), |d| d.display_id.clone());
        let slot_type = desc.map_or(0, |d| d.xml.int("SlotType", 0));
        ui.label(&name, ib[0] + ib[2] + 12.0 * s, y + 6.0 * s, font(17.0), true, color::TEXT);
        let where_ = if slot < 4 { "equipped" } else { "in your inventory" };
        ui.label(&format!("{}, {where_}", super::slot_name(slot_type)), ib[0] + ib[2] + 12.0 * s, y + 30.0 * s, font(12.0), false, color::TEXT_DIM);
        ui.tile_icon(super::SHEET, frame, 64, right[0] + right[2] - 44.0 * s, y + 20.0 * s, s);
        y += 70.0 * s;

        // The colours: what each slot holds, and the one coming soon.
        let tw = (inner_w - 3.0 * 8.0 * s) / 4.0;
        for (k, held) in current.iter().copied().chain([0]).enumerate() {
            let tr = [x0 + k as f32 * (tw + 8.0 * s), y, tw, 50.0 * s];
            if k == COMING_SOON {
                locked_chip(ui, [tr[0], tr[1] + 11.0 * s, tr[2], 28.0 * s], "Coming soon", font(11.5), s);
                self.hits.push((Hit::ImpKind(k), tr));
                continue;
            }
            let kind = Kind::ALL[k];
            let on = self.imp_kind == k;
            theme::fill(ui.renderer, tr, 8.0 * s, if on { kind.color() } else { color::CHIP }, if on { 0.35 } else { 1.0 });
            if inside(tr, mouse) && !on {
                theme::fill(ui.renderer, tr, 8.0 * s, 0xFFFFFF, 0.06);
            }
            theme::fill(ui.renderer, [tr[0], tr[1], 6.0 * s, tr[3]], 3.0 * s, kind.color(), 1.0);
            ui.label(kind.name(), tr[0] + 14.0 * s, tr[1] + 7.0 * s, font(12.5), true, kind.color());
            let holds = imprint::get(held as i32).map_or("Empty", |i| i.name);
            ui.label(holds, tr[0] + 14.0 * s, tr[1] + 27.0 * s, font(11.5), false, if held > 0 { color::TEXT } else { color::TEXT_MUTED });
            self.hits.push((Hit::ImpKind(k), tr));
        }
        y += 62.0 * s;

        if self.imp_kind == COMING_SOON {
            let t = "More imprints are coming in a later update.";
            ui.label(t, x0, y + 8.0 * s, font(13.0), false, color::TEXT_MUTED);
            return;
        }
        let kind = Kind::ALL[self.imp_kind];
        // The imprints of this colour: those that fit the item first, with what they do and the
        // button; then the rest, a line each with what they fit.
        let bw = 96.0 * s;
        let text_w = inner_w - bw - 16.0 * s;
        let foot = right[1] + right[3] - 34.0 * s;
        let mut list: Vec<&imprint::Imprint> = imprint::ALL.iter().filter(|i| i.kind == kind).collect();
        list.sort_by_key(|i| !i.fits(slot_type));
        for imp in list {
            let fits = imp.fits(slot_type);
            let lines = if fits { crate::npc::wrap(ui, imp.description, font(11.5), text_w - 12.0 * s) } else { Vec::new() };
            let h = if fits { (28.0 + 15.0 * lines.len() as f32) * s } else { 26.0 * s };
            if y + h > foot {
                break;
            }
            let has = current[kind as usize] == imp.id;
            let row = [x0, y, inner_w, h];
            theme::fill(ui.renderer, row, 6.0 * s, 0x000000, 0.22);
            theme::fill(ui.renderer, [row[0], row[1], 4.0 * s, row[3]], 2.0 * s, kind.color(), if fits { 1.0 } else { 0.35 });
            let nc = if fits { kind.color() } else { color::TEXT_MUTED };
            ui.label(imp.name, x0 + 12.0 * s, y + 6.0 * s, font(13.0), true, nc);
            let fw = ui.measure(imp.name, font(13.0), true);
            ui.label(&format!("·  {}", imp.fits_text()), x0 + 18.0 * s + fw, y + 7.5 * s, font(11.0), false, color::TEXT_MUTED);
            for (k, l) in lines.iter().enumerate() {
                ui.label(l, x0 + 12.0 * s, y + 25.0 * s + k as f32 * 15.0 * s, font(11.5), false, if fits { color::TEXT_DIM } else { color::TEXT_MUTED });
            }
            let br = [row[0] + row[2] - bw - 8.0 * s, y + (h - 28.0 * s) / 2.0, bw, 28.0 * s];
            if has {
                let t = "Carried";
                let w = ui.measure(t, font(12.0), true);
                ui.label(t, br[0] + (bw - w) / 2.0, br[1] + 6.0 * s, font(12.0), true, color::GOOD);
            } else if fits {
                let label = if self.imp_pending { "..." } else if current[kind as usize] > 0 { "Replace" } else { "Imprint" };
                ui.button(br, label, ButtonKind::Primary, inside(br, mouse), s * 0.8);
                self.hits.push((Hit::ImpApply(imp.id), br));
            }
            y += h + 6.0 * s;
        }
        // The price, and what can pay it.
        let (stored, held) = self.shards_for(ui.data, kind);
        let cost = format!(
            "Costs 1 {}  ·  you have {} in forge storage, {} in your inventory",
            kind.cost().join(" or "),
            stored,
            held
        );
        ui.label(&cost, x0, foot + 8.0 * s, font(11.5), false, if stored + held > 0 { color::TEXT_DIM } else { color::DANGER });
    }
}
