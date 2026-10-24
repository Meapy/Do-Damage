//! `client-rs/CHANGELOG.md`: what changed in each version, for players.
//!
//! ```text
//! ## 1.1.0 (2026-10-15)
//! ### A group
//! - A change, in plain words.
//! - Another one, which may go on
//!   over more than one line.
//! ```
//!
//! Anything before the first `## ` heading (a title, a note for whoever edits it) is ignored. A
//! `### ` heading starts a group of the version's changes (`Entry::groups`); clients from before
//! groups read the same changes as one list.

use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
pub struct Entry {
    pub version: String,
    /// YYYY-MM-DD, or empty.
    pub date: String,
    pub changes: Vec<String>,
    /// The version's groups, in order: each one's title and its first change in `changes` (it
    /// runs to the next group's). Changes before the first group belong to none.
    #[serde(default)]
    pub groups: Vec<(String, usize)>,
}

/// Every version in the file, in the file's order (newest first).
pub fn parse(text: &str) -> Vec<Entry> {
    let mut out: Vec<Entry> = Vec::new();
    for line in text.lines() {
        let trimmed = line.trim();
        if let Some(head) = trimmed.strip_prefix("## ") {
            let (version, date) = match head.split_once('(') {
                Some((v, d)) => (v.trim(), d.trim_end_matches(')').trim()),
                None => (head.trim(), ""),
            };
            out.push(Entry { version: version.to_owned(), date: date.to_owned(), ..Default::default() });
            continue;
        }
        let Some(entry) = out.last_mut() else { continue };
        if let Some(group) = trimmed.strip_prefix("### ") {
            entry.groups.push((group.trim().to_owned(), entry.changes.len()));
        } else if let Some(change) = trimmed.strip_prefix("- ").or_else(|| trimmed.strip_prefix("* ")) {
            entry.changes.push(change.trim().to_owned());
        } else if !trimmed.is_empty() && line.starts_with(char::is_whitespace) {
            // A change carried on to the next line.
            if let Some(last) = entry.changes.last_mut() {
                last.push(' ');
                last.push_str(trimmed);
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    #[test]
    fn parses_versions_and_wrapped_lines() {
        let text = "# Changelog\n\nIntro.\n\n## 1.1.0 (2026-10-15)\n- One.\n- Two goes\n  on here.\n\n## 1.0.0\n- First.\n";
        let e = super::parse(text);
        assert_eq!(e.len(), 2);
        assert_eq!((e[0].version.as_str(), e[0].date.as_str()), ("1.1.0", "2026-10-15"));
        assert_eq!(e[0].changes, vec!["One.", "Two goes on here."]);
        assert_eq!((e[1].version.as_str(), e[1].date.as_str(), e[1].changes.len()), ("1.0.0", "", 1));
        assert!(e[0].groups.is_empty());
    }

    #[test]
    fn parses_groups() {
        let text = "## Unreleased
- Loose.
### Arena
- One.
- Two.
### Items
- Three.
";
        let e = super::parse(text);
        assert_eq!(e[0].changes, vec!["Loose.", "One.", "Two.", "Three."]);
        assert_eq!(e[0].groups, vec![("Arena".to_owned(), 1), ("Items".to_owned(), 3)]);
    }
}
