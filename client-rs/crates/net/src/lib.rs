//! Talking to the servers, the same way on the desktop and in a browser:
//!
//! - [`http::post_form`]: account server requests (login, character list...).
//! - [`GameSocket`]: the world server connection. A WebSocket to the gateway (`client-rs/gateway`),
//!   which forwards the bytes to the world server: always in a browser, and on the desktop when
//!   the account server is a site's `/api` (wss, through its Caddy). Otherwise TCP straight to the
//!   world server.
//! - [`login::encrypt`]: the RSA encryption Hello needs for the account name and password.
//!
//! Nothing here blocks: requests and sockets are polled once per frame.

pub mod account;
pub mod http;
pub mod login;
pub mod replay;
mod socket;
#[cfg(not(target_arch = "wasm32"))]
pub mod tls;

pub use socket::{GameSocket, SocketEvent, Target};

/// A result that arrives later. Poll it with `take`.
pub struct Pending<T> {
    inner: std::sync::Arc<std::sync::Mutex<Option<T>>>,
}

impl<T> Pending<T> {
    fn new() -> (Self, std::sync::Arc<std::sync::Mutex<Option<T>>>) {
        let slot = std::sync::Arc::new(std::sync::Mutex::new(None));
        (Self { inner: slot.clone() }, slot)
    }

    /// The result, once it has arrived (and only once).
    pub fn take(&self) -> Option<T> {
        self.inner.lock().unwrap().take()
    }
}
