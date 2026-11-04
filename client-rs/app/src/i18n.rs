//! The interface in the player's language. English is written in the code; each other language
//! is a table in `app/lang/<code>.json` from the English text to its own, built into the client.
//!
//! - `tr("Play")`: a fixed line.
//! - `tf!("Level {level}", level)` or `tf!("{a} of {b}", a = x, b = y)`: a line with values in it,
//!   which a language may put in another order.
//! - `tn!(count, "{n} day ago|{n} days ago")`: a line that depends on a number, its English
//!   forms (one, other) split by `|`. A language gives as many forms as its grammar has
//!   (`Language::plural`), in the same order as CLDR's: Russian one|few|many ("1 день",
//!   "2 дня", "5 дней").
//! - `mark("Move up")`: a line kept in a table (a `const`), translated where it's drawn.
//! - `tr("key::Enter")`: the same English said of something else (the Enter key, not the
//!   portal's Enter button) gets its own line: a `<context>::` before it, which English drops.
//!
//! The language is the one picked in Options, else the system's (Windows' display language,
//! `LANG` elsewhere, the browser's), else English. A line a table lacks stays in English; the
//! tests make sure none does.

use std::collections::HashMap;
use std::fmt::Display;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::OnceLock;

pub struct Language {
    /// As kept in the settings (`language=de`), and the table's file name.
    pub code: &'static str,
    /// Its own name for itself, as its speakers look for it in a list.
    pub native: &'static str,
    /// Its name in English, which the tables translate ("German" is "Allemand" in French).
    pub english: &'static str,
    table: &'static str,
    /// Which of a line's forms a number takes.
    pub plural: fn(u64) -> usize,
    /// What goes between a number's thousands: "12,402", "12.402", "12 402".
    pub thousands: &'static str,
}

/// One form for 1, another for the rest (English, German, Spanish...).
fn one_other(n: u64) -> usize {
    (n != 1) as usize
}

/// French and Brazilian Portuguese: 0 and 1 take the singular.
fn zero_one_other(n: u64) -> usize {
    (n > 1) as usize
}

/// Russian and Ukrainian: 1, 21, 31 (one); 2-4, 22-24 (few); the rest, 11-14 among them (many).
fn east_slavic(n: u64) -> usize {
    match (n % 10, n % 100) {
        (1, h) if h != 11 => 0,
        (2..=4, h) if !(12..=14).contains(&h) => 1,
        _ => 2,
    }
}

/// Polish: only 1 itself is singular; 2-4, 22-24 (few); the rest (many).
fn polish(n: u64) -> usize {
    match (n, n % 10, n % 100) {
        (1, _, _) => 0,
        (_, 2..=4, h) if !(12..=14).contains(&h) => 1,
        _ => 2,
    }
}

/// Turkish counts with the singular ("3 gün"); a table may give one form for every number.
fn one_form(_: u64) -> usize {
    0
}

pub const LANGUAGES: [Language; 11] = [
    Language { code: "en", native: "English", english: mark("English"), table: "{}", plural: one_other, thousands: "," },
    Language { code: "de", native: "Deutsch", english: mark("German"), table: include_str!("../lang/de.json"), plural: one_other, thousands: "." },
    Language { code: "es", native: "Español", english: mark("Spanish"), table: include_str!("../lang/es.json"), plural: one_other, thousands: "." },
    Language { code: "fr", native: "Français", english: mark("French"), table: include_str!("../lang/fr.json"), plural: zero_one_other, thousands: "\u{a0}" },
    Language { code: "it", native: "Italiano", english: mark("Italian"), table: include_str!("../lang/it.json"), plural: one_other, thousands: "." },
    Language { code: "nl", native: "Nederlands", english: mark("Dutch"), table: include_str!("../lang/nl.json"), plural: one_other, thousands: "." },
    Language { code: "pl", native: "Polski", english: mark("Polish"), table: include_str!("../lang/pl.json"), plural: polish, thousands: "\u{a0}" },
    Language { code: "pt", native: "Português (Brasil)", english: mark("Portuguese (Brazil)"), table: include_str!("../lang/pt.json"), plural: zero_one_other, thousands: "." },
    Language { code: "ru", native: "Русский", english: mark("Russian"), table: include_str!("../lang/ru.json"), plural: east_slavic, thousands: "\u{a0}" },
    Language { code: "tr", native: "Türkçe", english: mark("Turkish"), table: include_str!("../lang/tr.json"), plural: one_form, thousands: "." },
    Language { code: "uk", native: "Українська", english: mark("Ukrainian"), table: include_str!("../lang/uk.json"), plural: east_slavic, thousands: "\u{a0}" },
];

/// The language in use, an index into `LANGUAGES`.
static CURRENT: AtomicUsize = AtomicUsize::new(0);

static TABLES: [OnceLock<HashMap<&'static str, &'static str>>; LANGUAGES.len()] = [const { OnceLock::new() }; LANGUAGES.len()];

fn table(i: usize) -> &'static HashMap<&'static str, &'static str> {
    TABLES[i].get_or_init(|| {
        let parsed: HashMap<String, String> = serde_json::from_str(LANGUAGES[i].table).unwrap_or_else(|e| {
            log::warn!("language {}: {e}", LANGUAGES[i].code);
            HashMap::new()
        });
        // Built once and kept for the run, so lines can be handed out as `&'static str`.
        parsed.into_iter().map(|(k, v)| (&*Box::leak(k.into_boxed_str()), &*Box::leak(v.into_boxed_str()))).collect()
    })
}

pub fn current() -> &'static Language {
    &LANGUAGES[CURRENT.load(Ordering::Relaxed)]
}

/// The language with this code ("de", or "de-AT", "de_DE.UTF-8" as systems write them).
pub fn find(code: &str) -> Option<usize> {
    let lower = code.trim().to_ascii_lowercase();
    let primary = lower.split(['-', '_', '.', '@']).next().unwrap_or("");
    LANGUAGES.iter().position(|l| l.code == primary)
}

/// Use the language picked (a code from the settings), or the system's when none is. A
/// `--lang <code>` on the command line wins, for checking a language's layout.
pub fn apply(picked: Option<&str>) {
    let i = arg().or_else(|| picked.and_then(find)).or_else(system).unwrap_or(0);
    let before = CURRENT.swap(i, Ordering::Relaxed);
    if before != i {
        log::info!("language: {}", LANGUAGES[i].code);
    }
}

#[cfg(not(target_arch = "wasm32"))]
fn arg() -> Option<usize> {
    let args: Vec<String> = std::env::args().collect();
    args.iter().position(|a| a == "--lang").and_then(|i| args.get(i + 1)).and_then(|c| find(c))
}

#[cfg(target_arch = "wasm32")]
fn arg() -> Option<usize> {
    None
}

/// The system's language, if the client has it.
pub fn system() -> Option<usize> {
    #[cfg(target_arch = "wasm32")]
    {
        let nav = web_sys::window()?.navigator();
        let langs = nav.languages();
        (0..langs.length()).filter_map(|i| langs.get(i).as_string()).chain(nav.language()).find_map(|c| find(&c))
    }
    #[cfg(windows)]
    {
        windows_language()
    }
    #[cfg(not(any(windows, target_arch = "wasm32")))]
    {
        ["LC_ALL", "LC_MESSAGES", "LANG"].iter().filter_map(|v| std::env::var(v).ok()).find(|v| !v.is_empty() && v != "C" && v != "POSIX").and_then(|v| find(&v))
    }
}

#[cfg(windows)]
fn windows_language() -> Option<usize> {
    // The primary language of Windows' display language (a LANGID's low 10 bits).
    let id = unsafe { windows_sys::Win32::Globalization::GetUserDefaultUILanguage() } & 0x3FF;
    let code = match id {
        0x07 => "de",
        0x09 => "en",
        0x0A => "es",
        0x0C => "fr",
        0x10 => "it",
        0x13 => "nl",
        0x15 => "pl",
        0x16 => "pt",
        0x19 => "ru",
        0x1F => "tr",
        0x22 => "uk",
        _ => return None,
    };
    find(code)
}

/// A line in the language in use.
pub fn tr(en: &str) -> &str {
    let i = CURRENT.load(Ordering::Relaxed);
    let found = if i == 0 { None } else { table(i).get(en).copied() };
    found.unwrap_or_else(|| en.split_once("::").map_or(en, |(_, line)| line))
}

/// Marks a line kept in a table for translating where it's drawn (`tr`): it's the line itself.
pub const fn mark(en: &'static str) -> &'static str {
    en
}

/// A line with its `{name}`s filled in.
pub fn fill(line: &str, args: &[(&str, &dyn Display)]) -> String {
    let mut out = String::with_capacity(line.len() + 16);
    let mut rest = line;
    while let Some(open) = rest.find('{') {
        out += &rest[..open];
        let after = &rest[open + 1..];
        let value = after.find('}').and_then(|close| args.iter().find(|(n, _)| *n == &after[..close]).map(|(_, v)| (v, close)));
        match value {
            Some((v, close)) => {
                out += &v.to_string();
                rest = &after[close + 1..];
            }
            None => {
                out.push('{');
                rest = after;
            }
        }
    }
    out + rest
}

/// The form of a line (its forms split by `|`) that `n` takes in the language in use.
pub fn plural(en: &str, n: u64) -> &str {
    let line = tr(en);
    let forms: Vec<&str> = line.split('|').collect();
    let i = if std::ptr::eq(line, en) { one_other(n) } else { (current().plural)(n) };
    forms[i.min(forms.len() - 1)]
}

/// `tf!("{name} joined", name)`, `tf!("{a} of {b}", a = x, b = 3)`: `fill(tr(line), ...)`.
macro_rules! tf {
    ($line:literal $(, $name:ident $(= $val:expr)?)* $(,)?) => {
        $crate::i18n::fill($crate::i18n::tr($line), &[$((stringify!($name), &$crate::i18n::tf!(@value $name $(= $val)?) as &dyn ::std::fmt::Display)),*])
    };
    (@value $name:ident = $val:expr) => { $val };
    (@value $name:ident) => { $name };
}

/// `tn!(count, "{n} day|{n} days")`, with more `{name}`s as `tf!` takes them: the form the
/// count takes, `{n}` the count (or an `n = ...` given, such as the count with its digits
/// grouped).
macro_rules! tn {
    ($count:expr, $line:literal $(, $name:ident $(= $val:expr)?)* $(,)?) => {{
        let count = $count;
        $crate::i18n::fill(
            $crate::i18n::plural($line, (count as i64).unsigned_abs()),
            &[$((stringify!($name), &$crate::i18n::tf!(@value $name $(= $val)?) as &dyn ::std::fmt::Display),)* ("n", &count as &dyn ::std::fmt::Display)],
        )
    }};
}

pub(crate) use {tf, tn};

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;

    /// Every line the code asks for (`tr`, `tf!`, `tn!`, `mark`), read from the source.
    fn lines_in_code() -> BTreeSet<String> {
        let mut out = BTreeSet::new();
        let src = concat!(env!("CARGO_MANIFEST_DIR"), "/src");
        let mut dirs = vec![std::path::PathBuf::from(src)];
        while let Some(d) = dirs.pop() {
            for e in std::fs::read_dir(d).unwrap().flatten() {
                let p = e.path();
                if p.is_dir() {
                    dirs.push(p);
                } else if p.extension().is_some_and(|x| x == "rs") {
                    let text = std::fs::read_to_string(&p).unwrap();
                    let text = text.split("#[cfg(test)]").next().unwrap();
                    // comments (the examples above) aren't lines the code asks for
                    let code: String = text.lines().filter(|l| !l.trim_start().starts_with("//")).map(|l| l.to_owned() + "\n").collect();
                    out.extend(literals(&code));
                }
            }
        }
        out
    }

    /// The string literals right after `tr(`, `mark(`, `tf!(` and `tn!(<count>, ` (not after a
    /// path: `prof::mark` is the profiler's).
    fn literals(text: &str) -> Vec<String> {
        let mut out = Vec::new();
        for start in ["tr(", "mark(", "tf!("] {
            for (at, _) in text.match_indices(start) {
                let before = text[..at].chars().next_back();
                if before.is_some_and(|c| c.is_alphanumeric() || c == '_') || text[..at].ends_with("prof::") {
                    continue;
                }
                // The line may start on the next one, as rustfmt puts a long one.
                let rest = text[at + start.len()..].trim_start();
                if rest.starts_with('"') {
                    out.push(literal(rest));
                }
            }
        }
        for (at, _) in text.match_indices("tn!(") {
            let rest = &text[at + 4..];
            if let Some(q) = rest.find(", \"") {
                if !rest[..q].contains('\n') || rest[..q].lines().count() < 3 {
                    out.push(literal(&rest[q + 2..]));
                }
            }
        }
        out.retain(|l| !l.is_empty() && l != "{}");
        out
    }

    /// A Rust string literal at the start of `s` (its opening quote), unescaped.
    fn literal(s: &str) -> String {
        let mut out = String::new();
        let mut chars = s[1..].chars();
        while let Some(c) = chars.next() {
            match c {
                '"' => break,
                '\\' => match chars.next() {
                    Some('n') => out.push('\n'),
                    Some('u') => {
                        let hex: String = chars.by_ref().skip(1).take_while(|&c| c != '}').collect();
                        out.push(char::from_u32(u32::from_str_radix(&hex, 16).unwrap()).unwrap());
                    }
                    Some('\n') => {
                        // A line continued: the next line's leading spaces go.
                        let rest: String = chars.clone().collect();
                        let skip = rest.len() - rest.trim_start().len();
                        for _ in 0..rest[..skip].chars().count() {
                            chars.next();
                        }
                    }
                    Some(c) => out.push(c),
                    None => break,
                },
                c => out.push(c),
            }
        }
        out
    }

    fn names(line: &str) -> BTreeSet<&str> {
        let mut out = BTreeSet::new();
        let mut rest = line;
        while let Some(open) = rest.find('{') {
            let after = &rest[open + 1..];
            let Some(close) = after.find('}') else { break };
            out.insert(&after[..close]);
            rest = &after[close + 1..];
        }
        out
    }

    /// Every table has every line the code uses, with the same `{name}`s, the forms its
    /// language needs, and only characters the interface's fonts have.
    #[test]
    fn tables_are_complete() {
        let wanted = lines_in_code();
        assert!(wanted.len() > 100, "found only {} lines", wanted.len());
        let root = concat!(env!("CARGO_MANIFEST_DIR"), "/../../client/src/com/company/ui/fonts/");
        let fonts: Vec<ab_glyph::FontArc> = ["MyriadPro.otf", "MyriadProBold.otf"]
            .iter()
            .map(|f| ab_glyph::FontArc::try_from_vec(std::fs::read(format!("{root}{f}")).unwrap()).unwrap())
            .collect();
        let mut problems = Vec::new();
        for (i, lang) in LANGUAGES.iter().enumerate().skip(1) {
            let t = table(i);
            assert!(!t.is_empty(), "{}.json didn't load", lang.code);
            let forms = [7, 1, 2, 3, 4, 5, 11, 12, 21, 22, 25, 101].iter().map(|&n| (lang.plural)(n)).max().unwrap() + 1;
            for en in &wanted {
                let Some(line) = t.get(en.as_str()) else {
                    problems.push(format!("{}: missing {en:?}", lang.code));
                    continue;
                };
                let plural = en.contains('|');
                for form in line.split('|') {
                    let (a, b) = (names(en.split('|').last().unwrap()), names(form));
                    // A form may leave out the count ("one day" rather than "1 day").
                    if a != b && !(plural && a.iter().filter(|n| **n != "n").eq(b.iter().filter(|n| **n != "n"))) {
                        problems.push(format!("{}: {en:?} has {b:?}, not {a:?}", lang.code));
                    }
                }
                let n = line.split('|').count();
                if plural && n != forms && n != 1 {
                    problems.push(format!("{}: {en:?} needs {forms} forms, has {n}", lang.code));
                }
                if !plural && n != 1 {
                    problems.push(format!("{}: {en:?} has a `|`", lang.code));
                }
                for c in line.chars().filter(|c| !c.is_control() && *c != '|') {
                    if fonts.iter().any(|f| ab_glyph::Font::glyph_id(f, c).0 == 0) {
                        problems.push(format!("{}: {en:?} has {c:?}, which the fonts lack", lang.code));
                    }
                }
            }
            for en in t.keys() {
                if !wanted.contains(*en) {
                    problems.push(format!("{}: {en:?} isn't used any more", lang.code));
                }
            }
        }
        assert!(problems.is_empty(), "{} problems:\n{}", problems.len(), problems.join("\n"));
    }

    #[test]
    fn plural_rules() {
        let forms = |f: fn(u64) -> usize| [0, 1, 2, 4, 5, 11, 12, 21, 22, 25, 111, 112].map(f);
        assert_eq!(forms(east_slavic), [2, 0, 1, 1, 2, 2, 2, 0, 1, 2, 2, 2]);
        assert_eq!(forms(polish), [2, 0, 1, 1, 2, 2, 2, 2, 1, 2, 2, 2]);
        assert_eq!(forms(zero_one_other), [0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]);
        assert_eq!(forms(one_other), [1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]);
    }

    #[test]
    fn filling_and_finding() {
        assert_eq!(fill("{a} of {b}, {a}", &[("a", &1), ("b", &"two")]), "1 of two, 1");
        assert_eq!(fill("{x} stays {", &[]), "{x} stays {");
        assert_eq!(find("de_DE.UTF-8"), Some(1));
        assert_eq!(find("pt-BR"), find("pt"));
        assert_eq!(find("xx"), None);
        assert_eq!(literal("\"a \\\"b\\\" \\u{2026}\" rest"), "a \"b\" \u{2026}");
        let level = 5;
        assert_eq!(tf!("Level {level}", level), "Level 5");
        assert_eq!(tn!(3, "{n} day ago|{n} days ago"), "3 days ago");
        assert_eq!(tn!(1, "{n} day ago|{n} days ago"), "1 day ago");
    }
}
