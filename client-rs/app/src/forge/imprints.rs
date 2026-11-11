//! The Divine Workbench's Imprints tab, where the Enchanter opens it. The player's gear
//! (equipped, in the inventory, and the Enthroned gear in the vault) on the left, with the
//! imprints each carries; on the right, the picked piece, its four imprint slots (red Offensive,
//! green Defensive, purple Misc, light blue Apotheosis) and the imprints of the colour shown that
//! fit it, each with what it does, its price in shards and a button to put it on
//! (`bsk_data::imprint`); the Apotheosis of Balance with the three stats picked for it, a click
//! on each choosing the next. With nothing picked (or the picked piece clicked again) every
//! imprint of the colour shows, with what it fits. The list scrolls with the wheel. The server
//! checks everything (`ImprintHandlers.cs`). Apotheosis imprints go on gear in the inventory only.

use super::{inside, Act, Category, Forge, Hit, Tab, ENCHANTER, FRAME_MS};
use crate::hud::Ui;
use crate::i18n::{tf, tn, tr};
use crate::theme::{self, color, Kind as ButtonKind};
use bsk_data::imprint::{self, Kind};
use bsk_data::GameData;

/// The stats the Apotheosis of Balance picks from, by index (as `imprint::choice` counts them).
const STAT_SHORT: [&str; 8] = ["HP", "MP", "ATT", "DEF", "SPD", "DEX", "VIT", "WIS"];
/// What it starts with picked: Attack raised, Speed and Wisdom lowered.
const DEFAULT_CHOICE: [u8; 3] = [2, 4, 7];

/// The piece of gear picked: in an inventory slot (0-3 equipped), or in a vault chest's slot.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Pick {
    Inv(u8),
    Vault(u16, u8),
}

impl Forge {
    /// Opens the window as the Enchanter; it asks for the forge storage's shards.
    pub fn show_enchanter(&mut self, data: &GameData) -> Option<Act> {
        self.open = true;
        self.anvil = false;
        self.tab = Tab::Imprints;
        self.imp_asked = true;
        self.message = None;
        self.imp_pending = false;
        if self.kinds.is_empty() {
            self.kinds = super::slot_categories(data);
        }
        Some(Act::ImprintShards)
    }

    /// Whether an item takes imprints: Enthroned gear only (`<Ascended/>`, worn as a weapon,
    /// ability, armour or ring).
    pub(super) fn imprintable(&self, data: &GameData, item: i32) -> bool {
        let (cat, _) = self.category_of(data, item as u16);
        cat != Category::Other && cat != Category::All && data.objects.get(&(item as u16)).is_some_and(|d| d.xml.has("Ascended"))
    }

    /// An imprint's price centred in `r`: the number, then the icon of the shard it's paid in (its
    /// colour's first; the line at the foot names the others).
    fn draw_price(&self, ui: &mut Ui, cost: i32, kind: Kind, r: [f32; 4], s: f32) {
        let n = cost.to_string();
        let size = (12.0 * s).round().max(9.0);
        let nw = ui.measure(&n, size, true);
        // an 8 by 8 shard at 40 is 16 pixels a side, the height of `r` at scale 1
        let icon = 16.0 * s;
        let gap = 2.0 * s;
        let x = r[0] + (r[2] - nw - gap - icon) / 2.0;
        ui.label(&n, x, r[1] + (r[3] - size * 1.2) / 2.0, size, true, color::GOLD);
        if let Some(&shard) = kind.shards().first().and_then(|id| ui.data.object_ids.get(*id)) {
            ui.item_icon(shard as i32, 40, x + nw + gap + icon / 2.0, r[1] + r[3] / 2.0, s);
        }
    }

    /// How many shards that pay for an imprint of this colour the storage and the inventory hold.
    fn shards_for(&self, data: &GameData, kind: Kind) -> (i32, i32) {
        let types: Vec<u16> = kind.shards().iter().filter_map(|id| data.object_ids.get(*id).copied()).collect();
        let stored = types.iter().map(|&t| self.stored(t)).sum();
        let held = self.inventory.iter().filter(|(_, t)| types.contains(&(*t as u16))).count() as i32;
        (stored, held)
    }

    /// Asks for an imprint on the picked item, if it fits and shards can pay for it.
    pub(super) fn imprint(&mut self, id: u8, data: &GameData) -> Option<Act> {
        let pick = self.imp_pick?;
        let (item, current) = self.picked_gear(pick)?;
        let imp = imprint::get(id as i64)?;
        let slot_type = data.objects.get(&(item as u16)).map_or(0, |d| d.xml.int("SlotType", 0));
        let (stored, held) = self.shards_for(data, imp.kind);
        let choice = self.choice();
        let guarded = imprint::id_of(current[Kind::Misc as usize]) == imprint::GUARDIAN_ANGEL;
        let same = if imp.picks() {
            current[imp.kind as usize] == imprint::with_choice(id, choice[0], [choice[1], choice[2]])
        } else {
            imprint::id_of(current[imp.kind as usize]) == id
        };
        self.message = Some(if self.imp_pending {
            return None;
        } else if !imp.fits(slot_type) {
            (tf!("{imprint} doesn't fit this item.", imprint = imp.name), true)
        } else if same {
            (tf!("It already carries {imprint}.", imprint = imp.name), true)
        } else if guarded && imp.kind != Kind::Misc {
            (tr("Guardian Angel takes no other imprints.").to_owned(), true)
        } else if imp.kind == Kind::Apotheosis && matches!(pick, Pick::Vault(..)) {
            (tr("Take it out of your vault first: Apotheosis imprints go on gear in your inventory.").to_owned(), true)
        } else if stored + held < imp.cost() {
            (tn!(imp.cost(), "You need {n} shard to pay for it.|You need {n} shards to pay for it."), true)
        } else {
            self.imp_pending = true;
            self.message = None;
            return Some(match pick {
                Pick::Inv(slot) => Act::Imprint(slot, id, imp.picks().then_some(choice)),
                Pick::Vault(chest, slot) => Act::ImprintVault(chest, slot, id),
            });
        });
        None
    }

    /// The stats picked for the Apotheosis of Balance (raised, lowered, lowered): three different
    /// ones, else the first pick's.
    fn choice(&self) -> [u8; 3] {
        let [a, b, c] = self.imp_choice;
        if a < 8 && b < 8 && c < 8 && a != b && a != c && b != c { self.imp_choice } else { DEFAULT_CHOICE }
    }

    /// The next stat for one of the Apotheosis of Balance's picks (0 raised, 1 and 2 lowered),
    /// past the ones the other two hold.
    pub(super) fn next_choice(&mut self, k: usize) {
        let mut c = self.choice();
        let mut n = c[k];
        loop {
            n = (n + 1) % 8;
            if (0..3).all(|j| j == k || c[j] != n) {
                break;
            }
        }
        c[k] = n;
        self.imp_choice = c;
        self.message = None;
    }

    /// The picked piece of gear and its imprints, if it's still there.
    fn picked_gear(&self, pick: Pick) -> Option<(i32, [u32; 4])> {
        match pick {
            Pick::Inv(slot) => self.gear.iter().find(|g| g.0 == slot).map(|g| (g.1, g.2)),
            Pick::Vault(chest, slot) => self.vault_gear.iter().find(|g| (g.0, g.1) == (chest, slot)).map(|g| (g.2, g.3)),
        }
    }

    /// One slot of gear on the left: the item, its imprints, picked or not, dimmed if it can't
    /// take imprints.
    #[allow(clippy::too_many_arguments)]
    fn gear_slot(&mut self, ui: &mut Ui, sr: [f32; 4], found: Option<(i32, [u32; 4])>, pick: Pick, s: f32, mouse: (f64, f64)) {
        let picked = self.imp_pick == Some(pick);
        let usable = found.is_some_and(|g| self.imprintable(ui.data, g.0));
        let over = inside(sr, mouse);
        let edge = if picked { Some(color::GOLD) } else { found.and_then(|g| crate::screens::tier_edge(ui.data, g.0)) };
        ui.slot_box(sr, edge, over && usable, s);
        let Some((item, imps)) = found else { return };
        ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s * sr[2] / (54.0 * s));
        crate::hud::draw_imprint_marks(ui, imps, sr, s);
        if !usable {
            theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
        }
        if over {
            self.hover_item = Some(item);
            self.hover_imprints = imps;
        }
        if usable {
            self.hits.push((Hit::ImpItem(pick), sr));
        }
    }

    pub(super) fn draw_imprints(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        // the picked item may have moved or gone
        if let Some(p) = self.imp_pick {
            if !self.picked_gear(p).is_some_and(|g| self.imprintable(ui.data, g.0)) {
                self.imp_pick = None;
            }
        }
        let left = [body[0], body[1], 270.0 * s, body[3]];
        let right = [left[0] + left[2] + 20.0 * s, body[1], body[2] - left[2] - 20.0 * s, body[3]];

        // The gear: equipped, then the inventory (and the backpack), then the vault's Enthroned gear.
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
                self.gear_slot(ui, sr, found, Pick::Inv(*slot), s, mouse);
            }
            y += slots.len().div_ceil(cols) as f32 * (cell + gap) + 8.0 * s;
        }
        // the vault: its Enthroned gear only, as many rows as fit
        ui.label(tr("Vault"), left[0], y, font(13.0), true, color::TEXT_DIM);
        y += 20.0 * s;
        let vault = self.vault_gear.clone();
        if vault.is_empty() {
            ui.label(tr("No Enthroned gear in your vault."), left[0], y, font(11.5), false, color::TEXT_MUTED);
        }
        let vcols = 5;
        let vcell = 46.0 * s;
        let vgap = (left[2] - vcols as f32 * vcell) / (vcols - 1) as f32;
        let rows = (((left[1] + left[3] - y + vgap) / (vcell + vgap)).floor() as usize).max(1);
        let room = rows * vcols;
        let shown = if vault.len() > room { room - 1 } else { vault.len() };
        for (n, &(chest, slot, item, imps)) in vault.iter().take(shown).enumerate() {
            let sr = [left[0] + (n % vcols) as f32 * (vcell + vgap), y + (n / vcols) as f32 * (vcell + vgap), vcell, vcell];
            self.gear_slot(ui, sr, Some((item, imps)), Pick::Vault(chest, slot), s, mouse);
        }
        if shown < vault.len() {
            let n = shown;
            let sr = [left[0] + (n % vcols) as f32 * (vcell + vgap), y + (n / vcols) as f32 * (vcell + vgap), vcell, vcell];
            let t = format!("+{}", vault.len() - shown);
            let w = ui.measure(&t, font(13.0), true);
            ui.label(&t, sr[0] + (vcell - w) / 2.0, sr[1] + vcell / 2.0 - 8.0 * s, font(13.0), true, color::TEXT_DIM);
        }

        theme::frame(ui.renderer, right, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
        let frame = ENCHANTER + ((now / FRAME_MS).rem_euclid(6)) as u32;
        let picked = self.imp_pick.and_then(|p| self.picked_gear(p).map(|g| (p, g)));
        let pad = 16.0 * s;
        let (x0, mut y) = (right[0] + pad, right[1] + pad);
        let inner_w = right[2] - 2.0 * pad;
        let (slot_type, current) = match picked {
            Some((pick, (item, current))) => {
                // The item: its icon, name and kind, the Enchanter beside it, and the way back to
                // every imprint.
                let ib = [x0, y, 56.0 * s, 56.0 * s];
                ui.slot_box(ib, crate::screens::tier_edge(ui.data, item), false, s);
                ui.item_icon(item, 80, ib[0] + ib[2] / 2.0, ib[1] + ib[3] / 2.0, s);
                crate::hud::draw_imprint_marks(ui, current, ib, s);
                let desc = ui.data.objects.get(&(item as u16));
                let name = desc.map_or(String::new(), |d| d.display_id.clone());
                let slot_type = desc.map_or(0, |d| d.xml.int("SlotType", 0));
                ui.label(&name, ib[0] + ib[2] + 12.0 * s, y + 6.0 * s, font(17.0), true, color::TEXT);
                let where_ = match pick {
                    Pick::Inv(slot) if slot < 4 => tr("equipped"),
                    Pick::Inv(_) => tr("in your inventory"),
                    Pick::Vault(..) => tr("in your vault"),
                };
                ui.label(&tf!("{slot}, {where_}", slot = super::slot_name(slot_type), where_), ib[0] + ib[2] + 12.0 * s, y + 30.0 * s, font(12.0), false, color::TEXT_DIM);
                ui.tile_icon(super::SHEET, frame, 64, right[0] + right[2] - 44.0 * s, y + 20.0 * s, s);
                let all = tr("Show all imprints");
                let cs = font(11.0);
                let cw = ui.measure(all, cs, true) + 18.0 * s;
                let cr = [right[0] + right[2] - 84.0 * s - cw, y + 4.0 * s, cw, 22.0 * s];
                self.chip(ui, cr, all, false, mouse, cs);
                self.hits.push((Hit::ImpClear, cr));
                (Some(slot_type), current)
            }
            None => {
                // nothing picked: every imprint, to look them over
                ui.tile_icon(super::SHEET, frame, 64, x0 + 26.0 * s, y + 24.0 * s, s);
                ui.label(tr("All imprints"), x0 + 62.0 * s, y + 6.0 * s, font(17.0), true, color::TEXT);
                let hint = tr("Pick Enthroned gear on the left to imprint it. Imprints work only while it's equipped.");
                ui.label(hint, x0 + 62.0 * s, y + 30.0 * s, font(11.5), false, color::TEXT_MUTED);
                (None, [0u32; 4])
            }
        };
        // below the Enchanter's picture, which stands taller than the header's text
        y += 94.0 * s;

        // The colours: what each slot holds.
        let tw = (inner_w - 3.0 * 8.0 * s) / 4.0;
        for (k, held) in current.iter().copied().enumerate() {
            let tr = [x0 + k as f32 * (tw + 8.0 * s), y, tw, 50.0 * s];
            let kind = Kind::ALL[k];
            let on = self.imp_kind == k;
            theme::fill(ui.renderer, tr, 8.0 * s, if on { kind.color() } else { color::CHIP }, if on { 0.35 } else { 1.0 });
            if inside(tr, mouse) && !on {
                theme::fill(ui.renderer, tr, 8.0 * s, 0xFFFFFF, 0.06);
            }
            theme::fill(ui.renderer, [tr[0], tr[1], 6.0 * s, tr[3]], 3.0 * s, kind.color(), 1.0);
            ui.label(crate::i18n::tr(kind.name()), tr[0] + 14.0 * s, tr[1] + 7.0 * s, font(12.5), true, kind.color());
            // what the picked piece holds there, or how many imprints the colour has
            let holds = match slot_type {
                Some(_) => imprint::get(held as i64).map_or(crate::i18n::tr("Empty").to_owned(), |i| i.name.to_owned()),
                None => tn!(imprint::ALL.iter().filter(|i| i.kind == kind).count(), "{n} imprint|{n} imprints"),
            };
            ui.label(&holds, tr[0] + 14.0 * s, tr[1] + 27.0 * s, font(11.5), false, if held > 0 || slot_type.is_none() { color::TEXT } else { color::TEXT_MUTED });
            self.hits.push((Hit::ImpKind(k), tr));
        }
        y += 62.0 * s;

        let kind = Kind::ALL[self.imp_kind.min(Kind::ALL.len() - 1)];
        // The imprints of this colour: with a piece picked only those that fit it, each with what
        // it does and the button; with none, all of them and what they fit. From the scrolled-to
        // row, as many as fit, a bar at the side when there are more; when they all fit, centred
        // in the space between the colours and the price.
        let bw = 96.0 * s;
        let foot = right[1] + right[3] - 34.0 * s;
        let list: Vec<&imprint::Imprint> =
            imprint::ALL.iter().filter(|i| i.kind == kind && slot_type.is_none_or(|t| i.fits(t))).collect();
        self.imp_count = list.len();
        self.imp_scroll = self.imp_scroll.min(list.len().saturating_sub(1));
        // beside the button and the price under it (the price shows with nothing picked too)
        let text_w = inner_w - bw - 16.0 * s - 12.0 * s;
        if list.is_empty() {
            let t = match kind {
                Kind::Offensive => tr("No Offensive imprint fits this item."),
                Kind::Defensive => tr("No Defensive imprint fits this item."),
                Kind::Misc => tr("No Misc imprint fits this item."),
                Kind::Apotheosis => tr("No Apotheosis imprint fits this item."),
            };
            ui.label(&t, x0, y + 8.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        let top = y;
        let mut rows = Vec::new();
        let mut used = 0.0;
        let choosing = |imp: &imprint::Imprint| imp.picks() && slot_type.is_some();
        for imp in list.iter().skip(self.imp_scroll) {
            let lines = crate::npc::wrap(ui, imp.description, font(11.5), text_w);
            let h = (28.0 + 15.0 * lines.len() as f32 + if choosing(imp) { 30.0 } else { 0.0 }) * s;
            if top + used + h > foot {
                break;
            }
            used += h + 6.0 * s;
            rows.push((imp, lines, h));
        }
        let last = self.imp_scroll + rows.len();
        if self.imp_scroll == 0 && last == list.len() {
            y += ((foot - top - used) / 2.0).max(0.0);
        }
        for (imp, lines, h) in rows {
            let has = imprint::id_of(current[kind as usize]) == imp.id && slot_type.is_some() && !imp.picks();
            let row = [x0, y, inner_w - 10.0 * s, h];
            theme::fill(ui.renderer, row, 6.0 * s, 0x000000, 0.22);
            theme::fill(ui.renderer, [row[0], row[1], 4.0 * s, row[3]], 2.0 * s, kind.color(), 1.0);
            ui.label(imp.name, x0 + 12.0 * s, y + 6.0 * s, font(13.0), true, kind.color());
            let fw = ui.measure(imp.name, font(13.0), true);
            ui.label(&format!("·  {}", imp.fits_text()), x0 + 18.0 * s + fw, y + 7.5 * s, font(11.0), false, color::TEXT_MUTED);
            for (k, l) in lines.iter().enumerate() {
                ui.label(l, x0 + 12.0 * s, y + 25.0 * s + k as f32 * 15.0 * s, font(11.5), false, color::TEXT_DIM);
            }
            // the Apotheosis of Balance's picks: a chip for each, a click choosing the next stat
            if choosing(imp) {
                let c = self.choice();
                let mut cx = x0 + 12.0 * s;
                let cy = y + 28.0 * s + 15.0 * lines.len() as f32 * s;
                for (k, label) in [tr("Raise"), tr("Lower"), tr("Lower")].into_iter().enumerate() {
                    let t = format!("{label}: {}", tr(STAT_SHORT[c[k] as usize]));
                    let cs = font(11.0);
                    let cw = ui.measure(&t, cs, true) + 18.0 * s;
                    let cr = [cx, cy, cw, 22.0 * s];
                    self.chip(ui, cr, &t, false, mouse, cs);
                    self.hits.push((Hit::ImpChoice(k), cr));
                    cx += cw + 6.0 * s;
                }
            }
            // the button and its price under it, both inside the row (with nothing picked, the
            // price alone): the number and the shard it's paid in, its colour's first
            let (btn_h, price_h) = (24.0 * s, 16.0 * s);
            let pair = if slot_type.is_some() { btn_h + 2.0 * s + price_h } else { price_h };
            let br = [row[0] + row[2] - bw - 8.0 * s, y + (h - pair) / 2.0, bw, btn_h];
            let price_y = if slot_type.is_some() { br[1] + btn_h + 2.0 * s } else { br[1] };
            self.draw_price(ui, imp.cost(), kind, [br[0], price_y, bw, price_h], s);
            if has {
                let t = tr("Carried");
                let w = ui.measure(t, font(12.0), true);
                ui.label(t, br[0] + (bw - w) / 2.0, br[1] + 4.0 * s, font(12.0), true, color::GOOD);
            } else if slot_type.is_some() {
                let label = if self.imp_pending { "..." } else if current[kind as usize] > 0 { tr("Replace") } else { tr("Imprint") };
                ui.button(br, label, ButtonKind::Primary, inside(br, mouse), s * 0.8);
                self.hits.push((Hit::ImpApply(imp.id), br));
            }
            y += h + 6.0 * s;
        }
        // the scroll bar: where the rows shown are among them all
        if self.imp_scroll > 0 || last < list.len() {
            let track = [right[0] + right[2] - pad + 2.0 * s, top, 4.0 * s, foot - top - 6.0 * s];
            theme::fill(ui.renderer, track, 2.0 * s, 0x000000, 0.4);
            let n = list.len().max(1) as f32;
            let h = (track[3] * (last - self.imp_scroll) as f32 / n).max(12.0 * s);
            let at = (track[3] - h) * (self.imp_scroll as f32 / (n - (last - self.imp_scroll) as f32).max(1.0)).min(1.0);
            theme::fill(ui.renderer, [track[0], track[1] + at, track[2], h], 2.0 * s, kind.color(), 0.9);
        }
        // What pays, and how much of it there is.
        let (stored, held) = self.shards_for(ui.data, kind);
        let shards = if kind == Kind::Apotheosis { tr("any Ascended shards").to_owned() } else { kind.shards().join(tr(" or ")) };
        let cost = tf!("Paid in {shard}  ·  you have {stored} in forge storage, {held} in your inventory", shard = shards, stored, held);
        ui.label_fit(&cost, x0, foot + 8.0 * s, inner_w, font(11.5), false, if stored + held > 0 { color::TEXT_DIM } else { color::DANGER });
    }
}
