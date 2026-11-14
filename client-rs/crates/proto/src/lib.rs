//! The betterSkillys game protocol: message types, their encoding, and framing. No I/O here, so
//! the same code serves the desktop client, the browser client, the bot and tests.

pub mod data;
pub mod frame;
pub mod messages;
pub mod wire;

pub use data::*;
pub use frame::{FrameDecoder, FrameError};
pub use messages::*;
pub use wire::{List16, List8, LongString, Rest};

#[cfg(test)]
mod tests;
