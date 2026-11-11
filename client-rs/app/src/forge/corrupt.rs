//! The Divine Workbench as the Cosmic Anvil, which the Mysterious One leaves in the Black Between:
//! its one tab, Corrupt. The player's gear (equipped and in the inventory) on the left, the
//! Enthroned pieces bright; on the right, the picked piece, its four corruption values (damage,
//! range, rate of fire, stats; each -6 to +6, rate of fire counted twice) as bars round a middle
//! line, how far the last corruption moved each, and a button spending an Unstable Essence from
//! the inventory to corrupt it again. The server rolls and checks everything (`CorruptHandler.cs`,
//! `Shared/resources/Corruption.cs`).

use super::{inside, Act, Forge, Hit};
use crate::hud::Ui;
use crate::i18n::{tf, tr};
use crate::theme::{self, color, Kind as ButtonKind};
use bsk_data::GameData;

/// The item that pays: one from the inventory each time.
pub const ESSENCE: &str = "Unstable Essence";

impl Forge {
    /// Opens the window as the Cosmic Anvil.
    pub fn show_anvil(&mut self, data: &GameData) -> Option<Act> {
        self.open = true;
        self.anvil = true;
        self.tab = super::Tab::Corrupt;
        self.message = None;
        self.cor_pending = false;
        self.cor_last = None;
        if self.kinds.is_empty() {
            self.kinds = super::slot_categories(data);
        }
        None
    }

    /// How many Unstable Essences the inventory holds.
    fn essences(&self, data: &GameData) -> usize {
        let Some(&t) = data.object_ids.get(ESSENCE) else { return 0 };
        self.inventory.iter().filter(|(_, i)| *i == t as i32).count()
    }

    /// Asks to corrupt the picked piece, if there's an essence to pay with.
    pub(super) fn corrupt(&mut self, data: &GameData) -> Option<Act> {
        let slot = self.cor_pick?;
        if self.cor_pending {
            return None;
        }
        if self.essences(data) == 0 {
            self.message = Some((tr("You need an Unstable Essence.").to_owned(), true));
            return None;
        }
        self.cor_pending = true;
        self.message = None;
        Some(Act::Corrupt(slot))
    }

    pub(super) fn draw_corrupt(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        // the picked piece may have moved or gone
        if let Some(p) = self.cor_pick {
            if !self.gear.iter().any(|g| g.0 == p && self.imprintable(ui.data, g.1)) {
                self.cor_pick = None;
            }
        }
        let left = [body[0], body[1], 270.0 * s, body[3]];
        let right = [left[0] + left[2] + 20.0 * s, body[1], body[2] - left[2] - 20.0 * s, body[3]];

        // The gear: equipped, then the inventory (and the backpack).
        let cell = 48.0 * s;
        let gap = 7.0 * s;
        let cols = 4;
        let mut y = left[1];
        let gear = self.gear.clone();
        let groups: [(&str, Vec<u8>); 2] = [
            (tr("Equipped"), (0..4).collect()),
            (tr("Inventory"), (4..if self.backpack { 20 } else { 12 }).collect()),
        ];
        for (label, slots) in groups {
            ui.label(label, left[0], y, font(13.0), true, color::TEXT_DIM);
            y += 20.0 * s;
            for (n, slot) in slots.iter().enumerate() {
                let sr = [left[0] + (n % cols) as f32 * (cell + gap), y + (n / cols) as f32 * (cell + gap), cell, cell];
                let found = gear.iter().find(|g| g.0 == *slot).map(|g| (g.1, g.2));
                let picked = self.cor_pick == Some(*slot);
                let usable = found.is_some_and(|g| self.imprintable(ui.data, g.0));
                let over = inside(sr, mouse);
                let edge = if picked { Some(crate::hud::CORRUPTED) } else { found.and_then(|g| crate::screens::tier_edge(ui.data, g.0)) };
                ui.slot_box(sr, edge, over && usable, s);
                let Some((item, imps)) = found else { continue };
                ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s * sr[2] / (54.0 * s));
                let (c, _) = self.corruption_of(*slot);
                crate::hud::draw_item_marks(ui, imps, c != [0; 4], sr, s);
                if !usable {
                    theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
                }
                if over {
                    self.hover_item = Some(item);
                    self.hover_imprints = imps;
                }
                if usable {
                    self.hits.push((Hit::CorItem(*slot), sr));
                }
            }
            y += slots.len().div_ceil(cols) as f32 * (cell + gap) + 8.0 * s;
        }

        theme::frame(ui.renderer, right, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
        let pad = 16.0 * s;
        let (x0, mut y) = (right[0] + pad, right[1] + pad);
        let inner_w = right[2] - 2.0 * pad;
        let essence = ui.data.object_ids.get(ESSENCE).copied();
        let Some((slot, item, imps)) = self.cor_pick.and_then(|p| self.gear.iter().find(|g| g.0 == p).copied()) else {
            // nothing picked: what the anvil does
            if let Some(t) = essence {
                ui.item_icon(t as i32, 80, x0 + 26.0 * s, y + 24.0 * s, s);
            }
            ui.label(tr("Cosmic Anvil"), x0 + 62.0 * s, y + 6.0 * s, font(17.0), true, color::TEXT);
            ui.label(tr("Pick Enthroned gear on the left to corrupt it."), x0 + 62.0 * s, y + 30.0 * s, font(11.5), false, color::TEXT_MUTED);
            y += 80.0 * s;
            let rules = tr("No one knows quite what an Unstable Essence will do. Try one and see.");
            for (k, l) in crate::npc::wrap(ui, rules, font(12.0), inner_w).iter().enumerate() {
                ui.label(l, x0, y + k as f32 * 17.0 * s, font(12.0), false, color::TEXT_DIM);
            }
            self.draw_essences(ui, right, s);
            return;
        };

        // the piece: its icon, name, and where it is
        let ib = [x0, y, 56.0 * s, 56.0 * s];
        ui.slot_box(ib, crate::screens::tier_edge(ui.data, item), false, s);
        ui.item_icon(item, 80, ib[0] + ib[2] / 2.0, ib[1] + ib[3] / 2.0, s);
        let (current, reversed) = self.corruption_of(slot);
        crate::hud::draw_item_marks(ui, imps, current != [0; 4], ib, s);
        let desc = ui.data.objects.get(&(item as u16));
        let name = desc.map_or(String::new(), |d| d.display_id.clone());
        let slot_type = desc.map_or(0, |d| d.xml.int("SlotType", 0));
        ui.label(&name, ib[0] + ib[2] + 12.0 * s, y + 6.0 * s, font(17.0), true, color::TEXT);
        let where_ = if slot < 4 { tr("equipped") } else { tr("in your inventory") };
        ui.label(&tf!("{slot}, {where_}", slot = super::slot_name(slot_type), where_), ib[0] + ib[2] + 12.0 * s, y + 30.0 * s, font(12.0), false, color::TEXT_DIM);
        y += 80.0 * s;

        // the four values: a bar each, round a middle line, -6 to +6
        let rows = [(tr("Damage"), "%", 1), (tr("Range"), "%", 1), (tr("Rate of Fire"), "%", 2), (tr("Stats"), "", 1)];
        let label_w = 120.0 * s;
        let num_w = 70.0 * s;
        let bar_w = inner_w - label_w - num_w - 10.0 * s;
        for (k, (label, unit, mult)) in rows.into_iter().enumerate() {
            let v = current[k];
            let colour = if v > 0 { color::GOOD } else if v < 0 { color::DANGER } else { color::TEXT_MUTED };
            ui.label(label, x0, y + 2.0 * s, font(13.0), true, color::TEXT);
            let bar = [x0 + label_w, y + 5.0 * s, bar_w, 12.0 * s];
            theme::fill(ui.renderer, bar, 4.0 * s, 0x000000, 0.35);
            let mid = bar[0] + bar[2] / 2.0;
            let len = bar[2] / 2.0 * v.abs() as f32 / 6.0;
            if v != 0 {
                let r = if v > 0 { [mid, bar[1], len, bar[3]] } else { [mid - len, bar[1], len, bar[3]] };
                theme::fill(ui.renderer, r, 3.0 * s, colour, 0.9);
            }
            theme::fill(ui.renderer, [mid - 0.5 * s, bar[1] - 2.0 * s, s.max(1.0), bar[3] + 4.0 * s], 0.0, color::TEXT_DIM, 1.0);
            let mut text = format!("{:+}{unit}", v * mult);
            if let Some(d) = self.cor_last.filter(|_| !self.cor_pending).map(|l| l[k]).filter(|d| *d != 0) {
                text.push_str(&format!("  ({:+})", d * mult));
            }
            ui.label(&text, x0 + label_w + bar_w + 10.0 * s, y + 2.0 * s, font(13.0), true, colour);
            y += 30.0 * s;
        }
        if reversed {
            ui.label(tr("Reversed: its shots fly from the end of their range back to you."), x0, y + 4.0 * s, font(12.0), true, crate::hud::CORRUPTED);
        }

        // the button, under the values
        let bw = 150.0 * s;
        let br = [x0 + (inner_w - bw) / 2.0, right[1] + right[3] - 84.0 * s, bw, 30.0 * s];
        let label = if self.cor_pending { "..." } else { tr("Corrupt") };
        ui.button(br, label, ButtonKind::Primary, inside(br, mouse), s * 0.9);
        self.hits.push((Hit::CorApply, br));
        self.draw_essences(ui, right, s);
    }

    /// The foot: how many Unstable Essences the inventory holds.
    fn draw_essences(&self, ui: &mut Ui, right: [f32; 4], s: f32) {
        let n = self.essences(ui.data);
        let t = tf!("Unstable Essence in your inventory: {n}", n);
        let size = (11.5 * s).round().max(9.0);
        ui.label(&t, right[0] + 16.0 * s, right[1] + right[3] - 34.0 * s, size, false, if n > 0 { color::TEXT_DIM } else { color::DANGER });
    }

    /// A slot's item's corruption and whether it's reversed, as the inventory shows it now.
    fn corruption_of(&self, slot: u8) -> ([i32; 4], bool) {
        self.gear_corruption.iter().find(|g| g.0 == slot).map_or(([0; 4], false), |g| (g.1, g.2))
    }
}
