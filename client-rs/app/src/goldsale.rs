//! A gold discount's banner: entering the Nexus while the Gold Store has a discount on (Black
//! Friday's half price), at most once every 10 minutes an account, the server sends
//! `GlobalNotification` kind 1, "<percent off>|<seconds left>|<its name>"
//! (`GlobalNotificationMessage.GoldSaleBanner`). It slides in at the top middle, counts the time
//! down while it shows, and a click on it opens the Gold Store in the browser.

use crate::hud::{Layout, Ui};
use crate::i18n::{tn, tr};
use crate::theme::{self, color};

/// `GlobalNotification`'s kind for a gold discount.
pub const KIND: i32 = 1;

const SHOW_MS: i32 = 12_000;
const FADE_MS: i32 = 400;

/// A discount the server told us of.
#[derive(Debug, Clone, PartialEq)]
pub struct Notice {
    pub percent: u32,
    pub seconds_left: i32,
    pub name: String,
}

/// The message's text, "<percent off>|<seconds left>|<its name>"; None if it isn't one.
pub fn parse(text: &str) -> Option<Notice> {
    let mut parts = text.splitn(3, '|');
    let percent = parts.next()?.trim().parse::<u32>().ok().filter(|p| (1..100).contains(p))?;
    let seconds_left = parts.next()?.trim().parse::<i32>().ok().filter(|s| *s > 0)?;
    let name = parts.next().unwrap_or("").trim().to_string();
    Some(Notice { percent, seconds_left, name })
}

/// "All gold is 50% off for the next 21 hours!": whole hours, rounded up, then minutes in the
/// last hour.
pub fn line(percent: u32, ms_left: i64) -> String {
    let ms_left = ms_left.max(0);
    let hours = (ms_left + 3_599_999) / 3_600_000;
    if hours > 1 {
        tn!(hours, "All gold is {percent}% off for the next {n} hour!|All gold is {percent}% off for the next {n} hours!", percent)
    } else {
        let minutes = ((ms_left + 59_999) / 60_000).max(1);
        tn!(minutes, "All gold is {percent}% off for the next {n} minute!|All gold is {percent}% off for the next {n} minutes!", percent)
    }
}

struct Shown {
    notice: Notice,
    since: i32,
    // when the discount ends, on our clock
    ends: i32,
}

#[derive(Default)]
pub struct GoldSale {
    shown: Option<Shown>,
    hit: Option<[f32; 4]>,
}

impl GoldSale {
    pub fn show(&mut self, notice: Notice, now: i32) {
        let ends = now.saturating_add(notice.seconds_left.saturating_mul(1000));
        self.shown = Some(Shown { notice, since: now, ends });
    }

    /// Whether a point is on the banner (so a click there doesn't shoot).
    pub fn on(&self, pos: (f64, f64)) -> bool {
        self.hit.is_some_and(|r| crate::hud::inside(r, pos))
    }

    /// A click on the banner: it goes, and the Gold Store opens (true).
    pub fn click(&mut self, pos: (f64, f64)) -> bool {
        if !self.on(pos) {
            return false;
        }
        self.shown = None;
        self.hit = None;
        true
    }

    /// Where the banner ends below `top` (the next banners go under it).
    pub fn end(&self, lay: &Layout, top: f32) -> f32 {
        if self.shown.is_some() { top + 92.0 * lay.s } else { top }
    }

    /// At the top middle, under the quest log's popups (`top`): it slides in, waits, fades out.
    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, top: f32, now: i32) {
        self.hit = None;
        let Some(b) = &self.shown else { return };
        let age = now - b.since;
        if age >= SHOW_MS || now >= b.ends {
            self.shown = None;
            return;
        }
        let s = lay.s;
        let fade = (age as f32 / FADE_MS as f32).min((SHOW_MS - age) as f32 / FADE_MS as f32).clamp(0.0, 1.0);
        let (w, h) = (470.0 * s, 84.0 * s);
        let slide = (1.0 - fade) * -16.0 * s;
        let r = [((lay.view_w() - w) / 2.0).round(), (top + slide).round(), w, h];
        let accent = color::GOLD;
        let pulse = 0.5 + 0.5 * ((now as f32) / 220.0).sin();
        theme::shadow(ui.renderer, r, 10.0 * s, 12.0 * s, 0.45 * fade);
        let g = (2.0 + pulse * 3.0) * s;
        theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], 12.0 * s + g, accent, 0.14 * fade);
        theme::frame(ui.renderer, r, 10.0 * s, (1.5 * s).max(1.0), (accent, fade), ((color::WARM_TOP, 0.97 * fade), (color::PANEL_BOTTOM, 0.97 * fade)));
        // the emblem: the HUD's gold coin in a ring of gold
        let (cx, cy) = (r[0] + 34.0 * s, r[1] + h / 2.0);
        theme::fill(ui.renderer, [cx - 21.0 * s, cy - 21.0 * s, 42.0 * s, 42.0 * s], 21.0 * s, accent, 0.25 * fade);
        if fade > 0.5 {
            ui.tile_icon("lofiObj3", 225, 40, cx, cy, s * 1.2);
            let x = r[0] + 66.0 * s;
            let tw = w - 78.0 * s;
            let tsz = (15.0 * s).round().max(9.0);
            let title = if b.notice.name.is_empty() { tr("Gold Store").to_string() } else { b.notice.name.clone() };
            let title = crate::npc::fit(ui, &title, tsz, tw);
            ui.label(&title, x, r[1] + 10.0 * s, tsz, true, accent);
            let bs = (13.0 * s).round().max(9.0);
            let text = crate::npc::fit(ui, &line(b.notice.percent, i64::from(b.ends - now)), bs, tw);
            ui.label(&text, x, r[1] + 33.0 * s, bs, true, color::TEXT);
            let hs = (11.0 * s).round().max(8.0);
            let hint = crate::npc::fit(ui, tr("Click to open the Gold Store."), hs, tw);
            ui.label(&hint, x, r[1] + 56.0 * s, hs, false, color::TEXT_DIM);
        }
        self.hit = Some(r);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_servers_notice() {
        assert_eq!(parse("50|75600|Black Friday"), Some(Notice { percent: 50, seconds_left: 75600, name: "Black Friday".into() }));
        assert_eq!(parse("50|60|"), Some(Notice { percent: 50, seconds_left: 60, name: String::new() }));
        // a dungeon key's colour, an ended or a made-up discount: no banner
        assert_eq!(parse("yellow"), None);
        assert_eq!(parse("50|0|Black Friday"), None);
        assert_eq!(parse("0|600|x"), None);
        assert_eq!(parse("100|600|x"), None);
    }

    #[test]
    fn counts_hours_then_minutes() {
        assert_eq!(line(50, 21 * 3_600_000), "All gold is 50% off for the next 21 hours!");
        assert_eq!(line(50, 20 * 3_600_000 + 1), "All gold is 50% off for the next 21 hours!");
        assert_eq!(line(50, 3_600_000 + 1), "All gold is 50% off for the next 2 hours!");
        assert_eq!(line(50, 3_600_000), "All gold is 50% off for the next 60 minutes!");
        assert_eq!(line(50, 61_000), "All gold is 50% off for the next 2 minutes!");
        assert_eq!(line(50, 1), "All gold is 50% off for the next 1 minute!");
    }
}
