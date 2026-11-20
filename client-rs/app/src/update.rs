//! Keeping the game up to date (desktop).
//!
//! A packaged build (the executable with `assets/` beside it) that was built with the update key
//! (`build_client_rs.py --publish`) looks at `<account server>/client/windows/latest.json` when it
//! starts and every five minutes after. When the published files differ from the ones beside the
//! executable, it downloads the ones that changed in the background, checks each against the
//! release's SHA-256 (the release itself is signed: see `bsk_update`), and puts them in place.
//! The running executable can't be overwritten on Windows, but it can be renamed: it's moved
//! aside (`betterskillys.exe.old-…`) and deleted the next time the game starts.
//!
//! The game keeps running the old version until a good moment to switch: the next portal (or
//! now, on the character list). Then it starts the new version with `--resume`, hands it what it
//! needs to carry on over a pipe (the login, the portal's destination and key, the window, the
//! chat), and waits. The new version loads with its window hidden, connects, shows itself where
//! the old window was, and says "shown"; the old one then exits. If the new one fails, the old
//! one goes through the portal itself.

use bsk_update::{FileEntry, Release};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::io::{BufRead, Read, Write};
use std::path::{Path, PathBuf};
use std::sync::{mpsc, Arc, Mutex};
use std::time::{Duration, Instant, SystemTime};

/// How often to look for a new version.
const CHECK_EVERY: Duration = Duration::from_secs(5 * 60);
/// The platform's releases.
const PLATFORM: &str = "windows";
/// How long the new version may take to show itself before the old one gives up on it.
const HANDOVER_TIMEOUT: Duration = Duration::from_secs(12);

#[derive(Clone, Debug, PartialEq)]
pub enum Status {
    /// Up to date, or not looked yet.
    Idle,
    Downloading { version: String, done: u64, total: u64 },
    /// A new version's files are in place; it takes over at the next portal.
    Ready { version: String },
    /// The last look failed (the server has no releases, a download broke...); tried again later.
    Failed(String),
}

#[derive(Default)]
struct Shared {
    status: Option<Status>,
    /// The newest release seen (its changelog shows what's coming).
    latest: Option<Release>,
}

pub struct Updater {
    shared: Arc<Mutex<Shared>>,
    /// The executable's name when the game started (before any update moved it aside).
    exe: PathBuf,
}

/// The folder of a packaged build: the executable's, with `assets/manifest.json` in it.
fn install_dir() -> Option<PathBuf> {
    let dir = std::env::current_exe().ok()?.parent()?.to_path_buf();
    dir.join("assets").join("manifest.json").is_file().then_some(dir)
}

/// The key releases must be signed with, built in by `build_client_rs.py`.
fn key() -> Option<&'static str> {
    option_env!("BSK_UPDATE_KEY").filter(|k| !k.is_empty())
}

impl Updater {
    /// Start looking for updates from this account server, when this build can update itself.
    pub fn start(account: &str) -> Option<Updater> {
        let exe = std::env::current_exe().ok()?;
        let dir = install_dir();
        let (Some(key), Some(dir)) = (key(), dir) else {
            log::info!("updates: off (not a published build)");
            return None;
        };
        if std::env::args().any(|a| a == "--no-update") {
            log::info!("updates: off (--no-update)");
            return None;
        }
        let shared = Arc::new(Mutex::new(Shared::default()));
        let base = format!("{}/client/", account.trim_end_matches('/'));
        let thread_shared = shared.clone();
        std::thread::spawn(move || {
            remove_old(&dir);
            let mut hashes = HashCache::default();
            loop {
                if let Err(e) = check(&base, &dir, key, &thread_shared, &mut hashes) {
                    log::warn!("updates: {e}");
                    let mut s = thread_shared.lock().unwrap();
                    if !matches!(s.status, Some(Status::Ready { .. })) {
                        s.status = Some(Status::Failed(e));
                    }
                }
                std::thread::sleep(CHECK_EVERY);
                remove_old(&dir);
            }
        });
        Some(Updater { shared, exe })
    }

    pub fn status(&self) -> Status {
        self.shared.lock().unwrap().status.clone().unwrap_or(Status::Idle)
    }

    pub fn latest(&self) -> Option<Release> {
        self.shared.lock().unwrap().latest.clone()
    }
}

fn agent() -> ureq::Agent {
    bsk_net::tls::agent_builder().timeout_connect(Duration::from_secs(10)).timeout_read(Duration::from_secs(30)).build()
}

fn get(agent: &ureq::Agent, url: &str) -> Result<Vec<u8>, String> {
    let response = agent.get(url).call().map_err(|e| match e {
        ureq::Error::Status(404, _) => format!("{url}: not published"),
        e => format!("{url}: {e}"),
    })?;
    let mut bytes = Vec::new();
    response.into_reader().read_to_end(&mut bytes).map_err(|e| format!("{url}: {e}"))?;
    Ok(bytes)
}

/// Files' SHA-256 by path, kept while their size and time don't change.
#[derive(Default)]
struct HashCache(HashMap<PathBuf, (u64, Option<SystemTime>, String)>);

impl HashCache {
    fn sha256(&mut self, path: &Path) -> Option<String> {
        let meta = std::fs::metadata(path).ok()?;
        let stamp = (meta.len(), meta.modified().ok());
        if let Some((len, time, sha)) = self.0.get(path) {
            if (*len, *time) == stamp {
                return Some(sha.clone());
            }
        }
        let sha = bsk_update::sha256_hex(&std::fs::read(path).ok()?);
        self.0.insert(path.to_path_buf(), (stamp.0, stamp.1, sha.clone()));
        Some(sha)
    }
}

/// Look at the published release, and bring the files beside the executable up to it.
fn check(base: &str, dir: &Path, key: &str, shared: &Mutex<Shared>, hashes: &mut HashCache) -> Result<(), String> {
    let agent = agent();
    let list = bsk_update::latest_path(PLATFORM);
    let json = get(&agent, &format!("{base}{list}"))?;
    let sig = get(&agent, &format!("{base}{list}.sig"))?;
    let release = bsk_update::verify(&json, &String::from_utf8_lossy(&sig), key)?;
    shared.lock().unwrap().latest = Some(release.clone());
    let changed: Vec<&FileEntry> = release.files.iter().filter(|f| hashes.sha256(&dir.join(&f.path)).as_deref() != Some(f.sha256.as_str())).collect();
    if changed.is_empty() {
        return Ok(());
    }
    let total: u64 = changed.iter().map(|f| f.gz_size).sum();
    log::info!("updates: {} has {} changed files ({} KB to download)", release.version, changed.len(), total / 1024);
    let set_status = |status: Status| shared.lock().unwrap().status = Some(status);
    let ready = matches!(shared.lock().unwrap().status, Some(Status::Ready { .. }));
    let mut done = 0;
    let staging = dir.join(".update");
    for f in &changed {
        if !ready {
            set_status(Status::Downloading { version: release.version.clone(), done, total });
        }
        let staged = staging.join(&f.path);
        // Already downloaded by an earlier look that didn't finish.
        if hashes.sha256(&staged).as_deref() != Some(f.sha256.as_str()) {
            let gz = get(&agent, &format!("{base}{}", f.blob()))?;
            let mut bytes = Vec::with_capacity(f.size as usize);
            flate2::read::GzDecoder::new(&gz[..]).read_to_end(&mut bytes).map_err(|e| format!("{}: {e}", f.path))?;
            if bsk_update::sha256_hex(&bytes) != f.sha256 {
                return Err(format!("{}: the download doesn't match the release", f.path));
            }
            if let Some(parent) = staged.parent() {
                std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
            }
            std::fs::write(&staged, &bytes).map_err(|e| format!("{}: {e}", staged.display()))?;
        }
        done += f.gz_size;
    }
    // Everything's here: into place.
    for f in &changed {
        replace(&dir.join(&f.path), &staging.join(&f.path)).map_err(|e| format!("{}: {e}", f.path))?;
    }
    // Asset files the release no longer has.
    let wanted: std::collections::HashSet<&str> = release.files.iter().map(|f| f.path.as_str()).collect();
    for (rel, full) in walk(&dir.join("assets"), "assets") {
        if !wanted.contains(rel.as_str()) {
            let _ = std::fs::remove_file(full);
        }
    }
    let _ = std::fs::remove_dir_all(&staging);
    log::info!("updates: {} is in place", release.version);
    set_status(Status::Ready { version: release.version.clone() });
    Ok(())
}

/// Every file under `dir`, as (`prefix`/relative path with forward slashes, full path).
fn walk(dir: &Path, prefix: &str) -> Vec<(String, PathBuf)> {
    let mut out = Vec::new();
    let Ok(entries) = std::fs::read_dir(dir) else { return out };
    for e in entries.flatten() {
        let p = e.path();
        let name = format!("{prefix}/{}", e.file_name().to_string_lossy());
        if p.is_dir() {
            out.extend(walk(&p, &name));
        } else {
            out.push((name, p));
        }
    }
    out
}

/// Put `staged` where `target` is. A file in use (the running executable) is moved aside first.
fn replace(target: &Path, staged: &Path) -> std::io::Result<()> {
    if let Some(parent) = target.parent() {
        std::fs::create_dir_all(parent)?;
    }
    match std::fs::rename(staged, target) {
        Ok(()) => Ok(()),
        Err(_) if target.exists() => {
            let stamp = SystemTime::now().duration_since(SystemTime::UNIX_EPOCH).map_or(0, |d| d.as_millis());
            let mut aside = target.as_os_str().to_owned();
            aside.push(format!(".old-{stamp}"));
            std::fs::rename(target, &aside)?;
            std::fs::rename(staged, target)
        }
        Err(e) => Err(e),
    }
}

/// Delete files moved aside by earlier updates, once nothing runs them.
fn remove_old(dir: &Path) {
    let Ok(entries) = std::fs::read_dir(dir) else { return };
    for e in entries.flatten() {
        if e.file_name().to_string_lossy().contains(".old-") {
            let _ = std::fs::remove_file(e.path());
        }
    }
}

/// What the new version needs to carry on where the old one is.
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct Resume {
    /// The version handing over.
    pub from_version: String,
    /// The signed-in account (empty: none).
    pub email: String,
    pub password: String,
    /// In the game: the world to go into.
    pub game: Option<ResumeGame>,
    pub window: WindowState,
    /// The chat so far, oldest first.
    pub chat: Vec<ResumeChat>,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct ResumeGame {
    pub host: String,
    pub port: u16,
    /// The portal's destination and key (`Reconnect`).
    pub game_id: i32,
    pub key_time: i32,
    pub key: Vec<u8>,
    pub char_id: i32,
    /// The world's name, for "Entering…".
    pub name: String,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct WindowState {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
    pub maximized: bool,
    pub fullscreen: bool,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct ResumeChat {
    pub name: String,
    pub recipient: String,
    pub text: String,
    pub name_color: i32,
    pub text_color: i32,
    /// The speaker's rank, or -1 for none (and from versions before 1.1.1, which didn't send it).
    #[serde(default = "no_stars")]
    pub stars: i32,
    /// The speaker's title (`ChatLine::title`); none from versions before 2.6.2.
    #[serde(default)]
    pub title: u8,
    /// How long ago it arrived, ms.
    pub age: i32,
}

fn no_stars() -> i32 {
    -1
}

/// The new version starting, as the old one sees it.
pub struct Handover {
    child: std::process::Child,
    lines: mpsc::Receiver<String>,
    started: Instant,
}

pub enum HandoverState {
    Waiting,
    /// The new version is showing: time to go.
    Shown,
    Failed(String),
}

impl Updater {
    /// Start the new version and hand it `resume`.
    pub fn hand_over(&self, resume: &Resume) -> Result<Handover, String> {
        let mut args = vec!["--resume".to_owned()];
        // The account server it was told to use, if not the built-in one.
        let given: Vec<String> = std::env::args().collect();
        if let Some(i) = given.iter().position(|a| a == "--account") {
            args.extend(given.get(i..i + 2).unwrap_or_default().iter().cloned());
        }
        let mut child = std::process::Command::new(&self.exe)
            .args(&args)
            .stdin(std::process::Stdio::piped())
            .stdout(std::process::Stdio::piped())
            .spawn()
            .map_err(|e| format!("{}: {e}", self.exe.display()))?;
        // The new version may bring itself to the front once it shows.
        #[cfg(windows)]
        unsafe {
            windows_sys::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow(child.id());
        }
        let mut stdin = child.stdin.take().ok_or("no pipe to the new version")?;
        let line = serde_json::to_string(resume).map_err(|e| e.to_string())? + "\n";
        std::thread::spawn(move || {
            let _ = stdin.write_all(line.as_bytes());
        });
        let stdout = child.stdout.take().ok_or("no pipe from the new version")?;
        let (tx, lines) = mpsc::channel();
        std::thread::spawn(move || {
            for line in std::io::BufReader::new(stdout).lines().map_while(Result::ok) {
                if tx.send(line).is_err() {
                    break;
                }
            }
        });
        log::info!("updates: started the new version (pid {})", child.id());
        Ok(Handover { child, lines, started: Instant::now() })
    }
}

impl Handover {
    pub fn poll(&mut self) -> HandoverState {
        while let Ok(line) = self.lines.try_recv() {
            if line.trim() == "shown" {
                return HandoverState::Shown;
            }
        }
        if let Ok(Some(status)) = self.child.try_wait() {
            return HandoverState::Failed(format!("the new version stopped ({status})"));
        }
        if self.started.elapsed() > HANDOVER_TIMEOUT {
            let _ = self.child.kill();
            return HandoverState::Failed("the new version took too long to start".into());
        }
        HandoverState::Waiting
    }
}

/// `--resume`: what the old version handed over (one line on stdin).
pub fn read_resume() -> Option<Resume> {
    if !std::env::args().any(|a| a == "--resume") {
        return None;
    }
    let mut line = String::new();
    std::io::stdin().lock().read_line(&mut line).ok()?;
    match serde_json::from_str(&line) {
        Ok(r) => Some(r),
        Err(e) => {
            log::warn!("--resume: {e}");
            None
        }
    }
}

/// Tell the old version we're showing.
pub fn announce_shown() {
    let mut out = std::io::stdout().lock();
    let _ = writeln!(out, "shown");
    let _ = out.flush();
}
