//! `--world-state <dir>`: connecting players straight to the world server their Hello belongs
//! to, as the world proxy (`tools/world_proxy.py`) would, instead of through it. The proxy is a
//! Python relay: every message through it costs it a read and a write, so on a slow shared vCPU
//! it took as much CPU as the world server's own networking. Browser and https players (most of
//! them) now skip it; desktop clients on TCP 2051 still go through it.
//!
//! The routing is the proxy's (see the top of world_proxy.py), read from the same state file
//! (`<dir>/state.json`, which `rolling_restart.py` rewrites on a deploy):
//! - a Hello's game id from 1000000 belongs to world server slot a, from 2000000 to slot b, so a
//!   player changing worlds inside the old server during a hand-over stays there;
//! - the Nexus (-2), other negative ids and anything else go to the active server;
//! - a world server still starting gets no connections.
//!
//! When the state can't be read or no world server answers, the connection goes to the proxy
//! as before.

use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, SystemTime};
use tokio::net::TcpStream;

pub const HELLO: u8 = 16;
pub const PROXY_INFO: u8 = 250;
/// The largest first message taken (the proxy's limit too).
pub const MAX_FIRST: usize = 1 << 20;

#[derive(Clone, Default)]
struct WorldState {
    front_port: u16,
    active: String,
    /// slot -> port, of the world servers that take connections (not starting)
    ports: Vec<(String, u16)>,
}

pub struct Router {
    path: PathBuf,
    cached: Mutex<(Option<SystemTime>, WorldState)>,
}

impl Router {
    pub fn new(dir: &str) -> Router {
        Router { path: Path::new(dir).join("state.json"), cached: Mutex::new((None, WorldState::default())) }
    }

    /// The state, read again when the file has changed. A file being rewritten keeps the last.
    fn state(&self) -> WorldState {
        let mut cached = self.cached.lock().unwrap();
        let mtime = std::fs::metadata(&self.path).and_then(|m| m.modified()).ok();
        if mtime.is_some() && mtime != cached.0 {
            if let Some(st) = std::fs::read_to_string(&self.path).ok().and_then(|t| parse_state(&t)) {
                *cached = (mtime, st);
            }
        }
        cached.1.clone()
    }

    /// Whether `target` (the address a bridge would connect to) is the proxy's own port.
    pub fn is_proxy(&self, target: &str) -> bool {
        let st = self.state();
        st.front_port != 0 && target.rsplit_once(':').and_then(|(_, p)| p.parse::<u16>().ok()) == Some(st.front_port)
    }

    /// A connection to the world server for a Hello with this game id (or another first message,
    /// `None`), and its slot: the slot its id belongs to, else the active one.
    pub async fn connect(&self, game_id: Option<i32>) -> Option<(TcpStream, String)> {
        let st = self.state();
        let port = |slot: &str| st.ports.iter().find(|(s, _)| s == slot).map(|(_, p)| *p);
        let mut tried = Vec::new();
        for slot in [route(&st, game_id), st.active.clone()] {
            let Some(p) = port(&slot) else { continue };
            if tried.contains(&slot) {
                continue;
            }
            tried.push(slot.clone());
            match tokio::time::timeout(Duration::from_secs(5), TcpStream::connect(("127.0.0.1", p))).await {
                Ok(Ok(tcp)) => return Some((tcp, slot)),
                _ => log::warn!("world server {slot} (port {p}) didn't answer"),
            }
        }
        None
    }
}

fn parse_state(text: &str) -> Option<WorldState> {
    let v: serde_json::Value = serde_json::from_str(text).ok()?;
    let ports = v
        .get("backends")?
        .as_object()?
        .iter()
        .filter(|(_, b)| !b.get("starting").and_then(|s| s.as_bool()).unwrap_or(false))
        .filter_map(|(slot, b)| Some((slot.clone(), u16::try_from(b.get("port")?.as_u64()?).ok()?)))
        .collect();
    Some(WorldState {
        front_port: u16::try_from(v.get("front_port")?.as_u64()?).ok()?,
        active: v.get("active")?.as_str()?.to_owned(),
        ports,
    })
}

/// The slot a Hello with this game id goes to.
fn route(st: &WorldState, game_id: Option<i32>) -> String {
    if let Some(id) = game_id.filter(|&id| id > 0) {
        let slot = match id / 1_000_000 {
            1 => Some("a"),
            2 => Some("b"),
            _ => None,
        };
        if let Some(slot) = slot.filter(|s| st.ports.iter().any(|(p, _)| p == s)) {
            return slot.to_owned();
        }
    }
    st.active.clone()
}

/// The length of the first whole message in `buf` (its 4-byte length includes itself), once it
/// has all come, or an error for a length no message has.
pub fn first_len(buf: &[u8]) -> Result<Option<usize>, ()> {
    if buf.len() < 5 {
        return Ok(None);
    }
    let len = i32::from_be_bytes([buf[0], buf[1], buf[2], buf[3]]);
    if !(5..=MAX_FIRST as i32).contains(&len) {
        return Err(());
    }
    Ok((buf.len() >= len as usize).then_some(len as usize))
}

/// The game id in a Hello (a whole message: length, id, then the build version string and the
/// game id), or None for any other message.
pub fn hello_game_id(msg: &[u8]) -> Option<i32> {
    if msg.get(4) != Some(&HELLO) {
        return None;
    }
    let body = &msg[5..];
    let n = i16::from_be_bytes([*body.first()?, *body.get(1)?]);
    let at = 2 + usize::try_from(n).ok()?;
    Some(i32::from_be_bytes(body.get(at..at + 4)?.try_into().ok()?))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn state() -> WorldState {
        parse_state(r#"{"front_port": 2051, "bind": "0.0.0.0", "active": "b",
            "backends": {"a": {"port": 12051, "pid": 1}, "b": {"port": 22051, "pid": 2}}}"#)
        .unwrap()
    }

    fn hello(game_id: i32) -> Vec<u8> {
        let mut body = vec![HELLO];
        body.extend_from_slice(&5i16.to_be_bytes());
        body.extend_from_slice(b"2.2.4");
        body.extend_from_slice(&game_id.to_be_bytes());
        body.extend_from_slice(&[0; 8]);
        let mut msg = ((body.len() + 4) as i32).to_be_bytes().to_vec();
        msg.extend(body);
        msg
    }

    #[test]
    fn routes_as_the_proxy_does() {
        let st = state();
        assert_eq!(route(&st, Some(-2)), "b"); // the Nexus: the active server
        assert_eq!(route(&st, Some(1_000_005)), "a"); // a world of slot a's stays there
        assert_eq!(route(&st, Some(2_000_001)), "b");
        assert_eq!(route(&st, Some(3_000_000)), "b");
        assert_eq!(route(&st, None), "b");
    }

    #[test]
    fn a_starting_server_gets_nothing() {
        let st = parse_state(r#"{"front_port": 2051, "active": "a",
            "backends": {"a": {"port": 12051}, "b": {"port": 22051, "starting": true}}}"#)
        .unwrap();
        assert_eq!(st.ports, vec![("a".to_owned(), 12051)]);
        assert_eq!(route(&st, Some(2_000_001)), "a");
    }

    #[test]
    fn reads_the_hello() {
        let msg = hello(1_000_007);
        assert_eq!(first_len(&msg[..3]), Ok(None));
        assert_eq!(first_len(&msg[..msg.len() - 1]), Ok(None));
        assert_eq!(first_len(&msg), Ok(Some(msg.len())));
        assert_eq!(hello_game_id(&msg), Some(1_000_007));
        assert_eq!(first_len(&[0, 0, 0, 1, 16]), Err(()));
        let mut other = msg.clone();
        other[4] = 1;
        assert_eq!(hello_game_id(&other), None);
    }
}
