//! Publish a client build for players' clients to update from. `tools/build_client_rs.py
//! --publish` runs it; see `bsk_update` for what gets written.
//!
//! ```text
//! bsk-publish key --key <file>
//!     Print the public key (hex) for the signing key in <file>, making one if there's none.
//! bsk-publish release --key <file> --from <build dir> --to <publish dir> --changelog <md>
//!                     --version <x.y.z> --commit <hash> [--platform windows] [--known <file>]
//!     Publish the build: its files as blobs, then the signed latest.json. The previous
//!     versions' lists stay beside it as <version>.json, for `rollback`. --known lists the
//!     files the server already has ("<path> <size>" lines, relative to the publish dir, as
//!     deploy/deploy.py writes them): those blobs aren't written again, so <publish dir> can
//!     hold only what's new.
//! bsk-publish rollback --to <publish dir> --version <x.y.z> [--platform windows]
//!     Make an earlier published version the latest again.
//! ```

use bsk_update::{FileEntry, Release};
use std::io::Write;
use std::path::{Path, PathBuf};

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let value = |name: &str| args.iter().position(|a| a == name).and_then(|i| args.get(i + 1)).cloned();
    let need = |name: &str| value(name).unwrap_or_else(|| fail(&format!("{name} is needed")));
    let platform = value("--platform").unwrap_or_else(|| "windows".into());
    match args.first().map(String::as_str) {
        Some("key") => println!("{}", bsk_update::public_key(&key(Path::new(&need("--key"))))),
        Some("release") => release(
            &key(Path::new(&need("--key"))),
            Path::new(&need("--from")),
            Path::new(&need("--to")),
            Path::new(&need("--changelog")),
            &need("--version"),
            &need("--commit"),
            &platform,
            value("--known").map(|f| known(Path::new(&f))).unwrap_or_default(),
        ),
        Some("rollback") => rollback(Path::new(&need("--to")), &need("--version"), &platform),
        _ => fail("usage: bsk-publish key|release|rollback (see the source for the options)"),
    }
}

/// `--known`: the files already published elsewhere, with their sizes.
fn known(path: &Path) -> std::collections::HashMap<String, u64> {
    let text = std::fs::read_to_string(path).unwrap_or_else(|e| fail(&format!("{}: {e}", path.display())));
    text.lines()
        .filter_map(|l| {
            let (name, size) = l.trim().rsplit_once(' ')?;
            Some((name.to_owned(), size.parse().ok()?))
        })
        .collect()
}

fn fail(why: &str) -> ! {
    eprintln!("bsk-publish: {why}");
    std::process::exit(1)
}

/// The signing key's seed from `path`, made (and written there) if the file isn't there.
fn key(path: &Path) -> [u8; 32] {
    if let Ok(text) = std::fs::read_to_string(path) {
        return bsk_update::unhex(&text).and_then(|k| k.try_into().ok()).unwrap_or_else(|| fail(&format!("{}: not a key", path.display())));
    }
    let mut seed = [0u8; 32];
    getrandom::getrandom(&mut seed).unwrap_or_else(|e| fail(&e.to_string()));
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    std::fs::write(path, bsk_update::hex(&seed)).unwrap_or_else(|e| fail(&format!("{}: {e}", path.display())));
    eprintln!("made a new signing key: {} (keep it; clients only take releases signed with it)", path.display());
    seed
}

/// Every file under `dir`, as (path relative to it with forward slashes, full path), sorted.
fn walk(dir: &Path) -> Vec<(String, PathBuf)> {
    fn go(base: &Path, dir: &Path, out: &mut Vec<(String, PathBuf)>) {
        let Ok(entries) = std::fs::read_dir(dir) else { return };
        for e in entries.flatten() {
            let p = e.path();
            if p.is_dir() {
                go(base, &p, out);
            } else if let Ok(rel) = p.strip_prefix(base) {
                out.push((rel.to_string_lossy().replace('\\', "/"), p));
            }
        }
    }
    let mut out = Vec::new();
    go(dir, dir, &mut out);
    out.sort();
    out
}

fn write(path: &Path, bytes: &[u8]) {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir).unwrap_or_else(|e| fail(&format!("{}: {e}", dir.display())));
    }
    // Written beside it, then renamed over it: a client never reads half a file.
    let tmp = path.with_extension("tmp");
    std::fs::write(&tmp, bytes).and_then(|_| std::fs::rename(&tmp, path)).unwrap_or_else(|e| fail(&format!("{}: {e}", path.display())));
}

fn release(
    seed: &[u8; 32],
    from: &Path,
    to: &Path,
    changelog: &Path,
    version: &str,
    commit: &str,
    platform: &str,
    known: std::collections::HashMap<String, u64>,
) {
    let text = std::fs::read_to_string(changelog).unwrap_or_else(|e| fail(&format!("{}: {e}", changelog.display())));
    let entries = bsk_update::changelog::parse(&text);
    if !entries.iter().any(|e| e.version == version) {
        fail(&format!("{} has no \"## {version}\" section: say what changed before publishing", changelog.display()));
    }
    if entries.iter().any(|e| e.version.eq_ignore_ascii_case("Unreleased")) {
        fail(&format!("{} still has an \"## Unreleased\" section: make it \"## {version} (<date>)\" or merge it into that section", changelog.display()));
    }
    let files: Vec<FileEntry> = walk(from)
        .into_iter()
        .map(|(rel, full)| {
            let bytes = std::fs::read(&full).unwrap_or_else(|e| fail(&format!("{}: {e}", full.display())));
            let sha256 = bsk_update::sha256_hex(&bytes);
            let mut entry = FileEntry { path: rel, sha256, size: bytes.len() as u64, gz_size: 0 };
            let blob = to.join(entry.blob());
            if let Some(&size) = known.get(&entry.blob()).filter(|_| !blob.is_file()) {
                // already on the server: the size its clients download
                entry.gz_size = size;
                return entry;
            }
            if !blob.is_file() {
                let mut gz = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::best());
                gz.write_all(&bytes).unwrap();
                write(&blob, &gz.finish().unwrap());
            }
            entry.gz_size = std::fs::metadata(&blob).map_or(0, |m| m.len());
            entry
        })
        .collect();
    if files.is_empty() {
        fail(&format!("{}: no files", from.display()));
    }
    let release = Release {
        version: version.to_owned(),
        commit: commit.to_owned(),
        date: today(),
        platform: platform.to_owned(),
        files,
        changelog: entries,
    };
    let json = serde_json::to_vec_pretty(&release).unwrap();
    let sig = bsk_update::sign(&json, seed);
    let dir = to.join(platform);
    // This version's list, kept for rolling back to, then the latest. A client that reads the
    // new list with the old signature in between refuses it and looks again later.
    write(&dir.join(format!("{version}.json")), &json);
    write(&dir.join(format!("{version}.json.sig")), sig.as_bytes());
    write(&dir.join("latest.json"), &json);
    write(&dir.join("latest.json.sig"), sig.as_bytes());
    let total: u64 = release.files.iter().map(|f| f.gz_size).sum();
    println!("published {version} ({commit}): {} files, {} KB to download in all, in {}", release.files.len(), total / 1024, dir.display());
}

fn rollback(to: &Path, version: &str, platform: &str) {
    let dir = to.join(platform);
    let json = std::fs::read(dir.join(format!("{version}.json"))).unwrap_or_else(|_| fail(&format!("{version} was never published here")));
    let sig = std::fs::read(dir.join(format!("{version}.json.sig"))).unwrap_or_else(|e| fail(&e.to_string()));
    write(&dir.join("latest.json"), &json);
    write(&dir.join("latest.json.sig"), &sig);
    println!("{version} is the latest again");
}

/// Today's date (UTC), YYYY-MM-DD.
fn today() -> String {
    let days = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_secs() / 86_400) as i64;
    // Days since 1970-01-01 to a civil date (Howard Hinnant's algorithm).
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = yoe + era * 400 + i64::from(m <= 2);
    format!("{y:04}-{m:02}-{d:02}")
}
