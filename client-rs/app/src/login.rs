//! Talking to the account server, and remembering the login.
//!
//! Desktop: the account server is `--account <url>`, else the `BSK_ACCOUNT` environment
//! variable, else the address built in (`BSK_ACCOUNT` at build time, or this PC's
//! `http://127.0.0.1:8080`). An address ending in `/api` is a site's gateway (`https://<site>/api`
//! through its Caddy): the game then goes over its WebSocket bridge, `wss://<site>/ws`, so that
//! both are encrypted. Any other address is the account server itself, and the game goes by TCP
//! straight to the world server. `--credentials <file>` (with `email:` and `password:` lines, like
//! test-account.local.txt) signs in straight away, for tests.
//! Browser: everything goes through the gateway that serves the page (`api/` and `ws/`); a page
//! may set `window.bskLogin = {email, password}` to sign in straight away.

use bsk_net::account::CharList;
use bsk_net::{account, http, Pending, Target};

/// Where the account server and world servers are reached.
#[derive(Clone, Debug)]
pub struct Endpoint {
    /// Account server base URL, without a trailing slash.
    pub account: String,
    /// The gateway's WebSocket base: always in a browser, and on the desktop for a site.
    pub gateway: Option<String>,
}

impl Endpoint {
    pub fn char_list(&self, email: &str, password: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/char/list", self.account), &[("guid", email), ("password", password)])
    }

    /// `/account/register`: a new account with this email, password and name; or, from a guest
    /// (its made-up login), the guest's account under them, characters and all.
    pub fn register(&self, email: &str, password: &str, name: &str, guest: Option<(&str, &str)>) -> Pending<http::Response> {
        let (guid, old_password) = guest.unwrap_or((email, ""));
        http::post_form(
            &format!("{}/account/register", self.account),
            &[("guid", guid), ("password", old_password), ("newGuid", email), ("newPassword", password), ("name", name)],
        )
    }

    /// `/account/guest`: a new guest account's made-up login.
    pub fn guest(&self) -> Pending<http::Response> {
        http::post_form(&format!("{}/account/guest", self.account), &[])
    }

    /// `/char/delete`.
    pub fn delete_char(&self, email: &str, password: &str, char_id: i32) -> Pending<http::Response> {
        let id = char_id.to_string();
        http::post_form(
            &format!("{}/char/delete", self.account),
            &[("guid", email), ("password", password), ("charId", &id)],
        )
    }

    /// `/account/purchaseCharSlot`.
    pub fn buy_slot(&self, email: &str, password: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/account/purchaseCharSlot", self.account), &[("guid", email), ("password", password)])
    }

    /// `/account/purchaseSkin`.
    pub fn buy_skin(&self, email: &str, password: &str, skin: u16) -> Pending<http::Response> {
        let t = skin.to_string();
        http::post_form(&format!("{}/account/purchaseSkin", self.account), &[("guid", email), ("password", password), ("skinType", &t)])
    }

    /// `/account/changePassword`.
    pub fn change_password(&self, email: &str, password: &str, new_password: &str) -> Pending<http::Response> {
        http::post_form(
            &format!("{}/account/changePassword", self.account),
            &[("guid", email), ("password", password), ("newPassword", new_password)],
        )
    }

    /// `/fame/list`: the legends for "week", "month" or "all".
    pub fn legends(&self, timespan: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/fame/list", self.account), &[("timespan", timespan)])
    }

    /// `/fame/recent`: the server's latest deaths, newest first.
    pub fn recent_deaths(&self) -> Pending<http::Response> {
        http::post_form(&format!("{}/fame/recent", self.account), &[("count", "20")])
    }

    /// `/char/fame`: a dead character's fame summary.
    pub fn char_fame(&self, account_id: i32, char_id: i32) -> Pending<http::Response> {
        let (a, c) = (account_id.to_string(), char_id.to_string());
        http::post_form(&format!("{}/char/fame", self.account), &[("accountId", &a), ("charId", &c)])
    }

    pub fn guild_board(&self, email: &str, password: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/guild/getBoard", self.account), &[("guid", email), ("password", password)])
    }

    pub fn set_guild_board(&self, email: &str, password: &str, board: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/guild/setBoard", self.account), &[("guid", email), ("password", password), ("board", board)])
    }

    pub fn guild_members(&self, email: &str, password: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/guild/listMembers", self.account), &[("guid", email), ("password", password)])
    }

    /// `/arena/leaderboard`: the best `count` arena runs.
    pub fn arena_leaderboard(&self, count: usize) -> Pending<http::Response> {
        let n = count.to_string();
        http::post_form(&format!("{}/arena/leaderboard", self.account), &[("count", &n)])
    }

    pub fn potions(&self, email: &str, password: &str) -> Pending<http::Response> {
        http::post_form(&format!("{}/account/grabPotions", self.account), &[("guid", email), ("password", password)])
    }

    pub fn target(&self, host: &str, port: u16) -> Target {
        Target { host: host.to_owned(), port, gateway: self.gateway.clone() }
    }
}

pub fn parse_char_list(response: http::Response) -> Result<CharList, String> {
    account::parse_char_list(&reached(response)?).map_err(|e| explain(&e))
}

pub fn parse_success(response: http::Response) -> Result<(), String> {
    account::success(&reached(response)?).map_err(|e| explain(&e))
}

/// The answer's body, or why the account server couldn't be asked, in words for players (the
/// raw error, with its URL, goes to the log).
pub fn reached(response: http::Response) -> Result<String, String> {
    response.map_err(|e| {
        log::warn!("account server: {e}");
        unreachable_text(&e)
    })
}

/// A failed request (no answer, or an error status) in words for players.
pub fn unreachable_text(e: &str) -> String {
    // ureq says "<url>: status code 503", the browser "HTTP 503".
    let code = e.rsplit_once("status code ").or_else(|| e.rsplit_once("HTTP ")).map(|(_, c)| c.trim());
    match code {
        Some(c) => format!("The server couldn't do that right now (error {c}). Try again in a minute."),
        None => "Can't reach the server. Check your internet connection and try again.".into(),
    }
}

/// The account server's `<Error>` text in plain words: today's and older servers' wording.
pub fn explain(e: &str) -> String {
    let l = e.trim().trim_end_matches('.').to_ascii_lowercase();
    let text = match l.as_str() {
        "no account uses that email" => "No account uses that email. Check it, or create an account.",
        "wrong password" => "Wrong password. Check it and try again.",
        "bad login" | "account credentials not valid" | "email or password is incorrect" => "Wrong email or password.",
        "duplicated name" => "That name is taken. Choose another.",
        "duplicate email" => "An account already uses that email. Sign in instead.",
        "invalid email" => "That isn't a valid email address.",
        "invalid name" => "That name can't be used. Names are up to 10 letters, A to Z.",
        "account in use" => "That account is in the game right now. Try again in a moment.",
        "account banned" => "This account is banned.",
        "" => "Something went wrong. Try again.",
        _ => return e.trim().to_owned(),
    };
    text.to_owned()
}

/// A new guest's login (`<Guest><GUID>..</GUID><Password>..</Password></Guest>`).
pub fn parse_guest(response: http::Response) -> Result<(String, String), String> {
    let xml = reached(response)?;
    if let Some(e) = account::error_text(&xml) {
        return Err(explain(&e));
    }
    // Made-up logins are letters, digits, '-' and '_': no escaping to undo.
    let field = |name: &str| {
        let start = xml.find(&format!("<{name}>"))? + name.len() + 2;
        let end = start + xml[start..].find(&format!("</{name}>"))?;
        Some(xml[start..end].trim().to_owned())
    };
    match (field("GUID"), field("Password")) {
        (Some(g), Some(p)) if !g.is_empty() && !p.is_empty() => Ok((g, p)),
        _ => Err("The server didn't make a guest account. Try again, or register.".into()),
    }
}

/// This device's guest login, kept apart from "Remember me" so a guest's characters stay theirs
/// until they register (which keeps them) or the device forgets it.
pub fn guest_login() -> Option<(String, String)> {
    let text = store::load("guest")?;
    let mut lines = text.lines();
    let (g, p) = (lines.next()?.trim().to_owned(), lines.next()?.trim().to_owned());
    (!g.is_empty() && !p.is_empty()).then_some((g, p))
}

pub fn keep_guest(login: Option<(&str, &str)>) {
    match login {
        Some((g, p)) => store::save("guest", Some(&format!("{g}\n{p}\n"))),
        None => store::save("guest", None),
    }
}

/// What's wrong with a sign-in form, before asking the server.
pub fn check_sign_in(email: &str, password: &str) -> Option<&'static str> {
    if email.is_empty() && password.is_empty() {
        Some("Enter your email and password.")
    } else if email.is_empty() {
        Some("Enter your email.")
    } else if password.is_empty() {
        Some("Enter your password.")
    } else {
        None
    }
}

/// The fewest characters a new password may have.
pub const MIN_PASSWORD: usize = 6;

/// What's wrong with a registration form, before asking the server (which checks the same).
pub fn check_register(name: &str, email: &str, password: &str, confirm: &str) -> Option<&'static str> {
    if name.is_empty() {
        Some("Choose a name.")
    } else if name.len() > 10 || !name.chars().all(|c| c.is_ascii_alphabetic()) {
        Some("Names are up to 10 letters, A to Z, with no spaces.")
    } else if email.is_empty() {
        Some("Enter your email.")
    } else if !valid_email(email) {
        Some("That isn't a valid email address.")
    } else if password.chars().count() < MIN_PASSWORD {
        Some("Passwords need at least 6 characters.")
    } else if password != confirm {
        Some("The passwords don't match.")
    } else {
        None
    }
}

/// Roughly what the server's `Utils.IsValidEmail` accepts: something@something.tld, no spaces.
pub fn valid_email(email: &str) -> bool {
    let Some((user, domain)) = email.split_once('@') else { return false };
    !user.is_empty()
        && !domain.contains('@')
        && !email.chars().any(char::is_whitespace)
        && domain.split('.').count() >= 2
        && domain.split('.').all(|p| !p.is_empty())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn errors_in_plain_words() {
        assert_eq!(explain("Bad Login"), "Wrong email or password.");
        assert_eq!(explain("Duplicated name"), "That name is taken. Choose another.");
        assert_eq!(explain("Something new"), "Something new");
        assert!(unreachable_text("http://1.2.3.4:8080/char/list: Connection Failed: Connect error").starts_with("Can't reach the server"));
        assert!(unreachable_text("http://x/char/list: status code 502").contains("error 502"));
        assert!(unreachable_text("HTTP 500").contains("error 500"));
    }

    #[cfg(not(target_arch = "wasm32"))]
    #[test]
    fn gateway_from_account() {
        assert_eq!(gateway_for("https://dodmg.com/api").as_deref(), Some("wss://dodmg.com/ws"));
        assert_eq!(gateway_for("http://127.0.0.1:8095/api").as_deref(), Some("ws://127.0.0.1:8095/ws"));
        assert_eq!(gateway_for("http://dodmg.com:8080"), None);
    }

    #[test]
    fn form_checks() {
        assert!(valid_email("a.b@example.com"));
        for bad in ["", "ab", "a@b", "a@b.", "@b.com", "a b@c.com", "a@b@c.com"] {
            assert!(!valid_email(bad), "{bad}");
        }
        assert_eq!(check_register("Bob", "bob@x.com", "secret1", "secret1"), None);
        assert!(check_register("Bob1", "bob@x.com", "secret1", "secret1").is_some());
        assert!(check_register("Bob", "bob@x.com", "short", "short").is_some());
        assert!(check_register("Bob", "bob@x.com", "secret1", "secret2").is_some());
        assert_eq!(check_sign_in("", "pw"), Some("Enter your email."));
    }

    #[test]
    fn guest_answer() {
        let ok = Ok("<Guest><GUID>guest-ab</GUID><Password>pw</Password><Name>Darq</Name></Guest>".to_owned());
        assert_eq!(parse_guest(ok), Ok(("guest-ab".into(), "pw".into())));
        assert!(parse_guest(Ok("<Error>Too many new guests from here.</Error>".into())).unwrap_err().starts_with("Too many"));
    }
}

/// What the command line or page asked for.
pub struct Startup {
    pub endpoint: Endpoint,
    /// Sign in with these straight away (tests, or a page that collected them).
    pub login: Option<(String, String)>,
}

#[cfg(not(target_arch = "wasm32"))]
pub fn startup() -> Result<Startup, String> {
    let mut account = None;
    let mut credentials = None;
    let mut it = std::env::args().skip(1);
    while let Some(a) = it.next() {
        match a.as_str() {
            "--account" => account = it.next(),
            // Read by main: sign in with --credentials but stay on the character list.
            "--stay" | "--bench" | "--resume" | "--no-update" => {}
            "--credentials" => credentials = it.next(),
            // Read by main (screenshots, scripted input).
            "--shot" | "--shot-after" | "--hold" | "--walk-to" | "--aim" | "--say" | "--type" | "--npc" | "--quests" | "--effect" | "--size" | "--mouse" | "--mouse-script" => {
                it.next();
            }
            _ => return Err(format!("unknown argument {a}")),
        }
    }
    let account = account
        .or_else(|| std::env::var("BSK_ACCOUNT").ok())
        .unwrap_or_else(|| option_env!("BSK_ACCOUNT").unwrap_or("http://127.0.0.1:8080").to_owned());
    let login = match credentials {
        Some(path) => {
            let text = std::fs::read_to_string(&path).map_err(|e| format!("{path}: {e}"))?;
            let field = |name: &str| {
                text.lines()
                    .find_map(|l| l.strip_prefix(name).and_then(|r| r.strip_prefix(':')).map(|v| v.trim().to_owned()))
                    .ok_or(format!("{path}: no '{name}:' line"))
            };
            Some((field("email")?, field("password")?))
        }
        None => None,
    };
    let account = account.trim_end_matches('/').to_owned();
    let gateway = gateway_for(&account);
    log::info!("account server {account}, game {}", gateway.as_deref().unwrap_or("over TCP"));
    Ok(Startup { endpoint: Endpoint { account, gateway }, login })
}

/// The gateway's WebSocket base for an account address that is a gateway's `/api`
/// (`https://example.org/api` -> `wss://example.org/ws`).
#[cfg(not(target_arch = "wasm32"))]
fn gateway_for(account: &str) -> Option<String> {
    let base = account.strip_suffix("/api")?;
    let ws = if let Some(rest) = base.strip_prefix("https://") {
        format!("wss://{rest}")
    } else {
        format!("ws://{}", base.strip_prefix("http://")?)
    };
    Some(format!("{ws}/ws"))
}

#[cfg(target_arch = "wasm32")]
pub fn startup() -> Result<Startup, String> {
    use wasm_bindgen::JsValue;
    let window = web_sys::window().ok_or("no window")?;
    let location = window.location();
    let host = location.host().map_err(|e| format!("{e:?}"))?;
    let ws = if location.protocol().map_err(|e| format!("{e:?}"))? == "https:" { "wss" } else { "ws" };
    let login = js_sys::Reflect::get(&window, &JsValue::from_str("bskLogin")).ok().filter(|v| !v.is_undefined() && !v.is_null());
    let login = login.map(|l| {
        let get = |k: &str| js_sys::Reflect::get(&l, &JsValue::from_str(k)).ok().and_then(|v| v.as_string()).unwrap_or_default();
        (get("email"), get("password"))
    });
    Ok(Startup { endpoint: Endpoint { account: "api".into(), gateway: Some(format!("{ws}://{host}/ws")) }, login })
}

/// What's kept on this computer (or in this browser) between runs, by name: the login
/// ("Remember me") and the settings, as the AIR client keeps them in its shared object.
#[cfg(not(target_arch = "wasm32"))]
pub mod store {
    use std::path::PathBuf;

    fn path(name: &str) -> Option<PathBuf> {
        let base = if cfg!(windows) {
            std::env::var_os("APPDATA").map(PathBuf::from)
        } else if cfg!(target_os = "macos") {
            std::env::var_os("HOME").map(|h| PathBuf::from(h).join("Library/Application Support"))
        } else {
            std::env::var_os("XDG_CONFIG_HOME")
                .map(PathBuf::from)
                .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))
        };
        base.map(|b| b.join("betterSkillys-rs").join(format!("{name}.txt")))
    }

    pub fn load(name: &str) -> Option<String> {
        std::fs::read_to_string(path(name)?).ok()
    }

    pub fn save(name: &str, text: Option<&str>) {
        let Some(path) = path(name) else { return };
        match text {
            Some(t) => {
                if let Some(dir) = path.parent() {
                    let _ = std::fs::create_dir_all(dir);
                }
                // Written beside it and moved over it, so a crash never leaves half a file.
                let tmp = path.with_extension("tmp");
                if let Err(e) = std::fs::write(&tmp, t).and_then(|_| std::fs::rename(&tmp, &path)) {
                    log::warn!("{}: {e}", path.display());
                }
            }
            None => {
                let _ = std::fs::remove_file(path);
            }
        }
    }
}

#[cfg(target_arch = "wasm32")]
pub mod store {
    fn storage() -> Option<web_sys::Storage> {
        web_sys::window()?.local_storage().ok()?
    }

    pub fn load(name: &str) -> Option<String> {
        storage()?.get_item(&format!("betterSkillys-rs.{name}")).ok()?
    }

    pub fn save(name: &str, text: Option<&str>) {
        let Some(s) = storage() else { return };
        let key = format!("betterSkillys-rs.{name}");
        let _ = match text {
            Some(t) => s.set_item(&key, t),
            None => s.remove_item(&key),
        };
    }
}

/// The remembered (email, password), if any.
pub fn remembered() -> Option<(String, String)> {
    let text = store::load("login")?;
    let mut lines = text.lines();
    let email = lines.next()?.to_owned();
    let password = lines.next().unwrap_or("").to_owned();
    (!email.is_empty()).then_some((email, password))
}

/// Keep this login for next time, or forget it (`None`).
pub fn remember(login: Option<(&str, &str)>) {
    match login {
        Some((email, password)) => store::save("login", Some(&format!("{email}\n{password}\n"))),
        None => store::save("login", None),
    }
}
