//! The browser's own keys. winit cancels every key press on the canvas, so the page's scrolling,
//! Tab and the browser's menu keys stay out of the game; but that also stops the clipboard keys
//! from making the page's paste, copy and cut events, and takes Ctrl+R, F5 and the like from the
//! player. Those few keys are handed back to the browser here; the game still sees them.

use std::cell::RefCell;
use wasm_bindgen::closure::Closure;
use wasm_bindgen::JsCast;

thread_local! {
    // The keys the game's controls use (their `KeyboardEvent.code`s), which F5 and F12 give way to.
    static BOUND: RefCell<Vec<String>> = const { RefCell::new(Vec::new()) };
}

/// The keys the player's controls use, so a key bound in the game isn't also the browser's.
pub fn keys_in_use(keys: impl Iterator<Item = winit::keyboard::KeyCode>) {
    // winit's key codes are named after the page's.
    let keys = keys.map(|k| format!("{k:?}")).collect();
    BOUND.with(|b| *b.borrow_mut() = keys);
}

/// Whether the browser should still do what a key press does by itself.
fn browser_key(e: &web_sys::KeyboardEvent) -> bool {
    let code = e.code();
    let code = code.as_str();
    // Ctrl, or Cmd on a Mac; Ctrl with Alt is AltGr, which types.
    if (e.ctrl_key() || e.meta_key()) && !e.alt_key() {
        return match code {
            // the clipboard, through the page's copy, cut and paste events (textedit.rs)
            "KeyC" | "KeyX" | "KeyV" | "Insert" => true,
            // reload, a new tab or window, close, the address bar, switching tabs, zoom
            "KeyR" | "KeyT" | "KeyN" | "KeyW" | "KeyL" | "Tab" | "PageUp" | "PageDown" => true,
            "Equal" | "Minus" | "Digit0" | "NumpadAdd" | "NumpadSubtract" | "Numpad0" => true,
            // the developer tools
            "KeyI" | "KeyJ" => e.shift_key(),
            _ => false,
        };
    }
    match code {
        // paste and cut
        "Insert" | "Delete" => e.shift_key(),
        // reload and the developer tools, unless the controls use them
        "F5" | "F12" => !BOUND.with(|b| b.borrow().iter().any(|k| k == code)),
        _ => false,
    }
}

/// Watch key presses before winit does, and keep winit from cancelling the browser's keys.
pub fn listen() {
    let Some(window) = web_sys::window() else { return };
    let keep = js_sys::Function::new_no_args("");
    let on_key = Closure::<dyn FnMut(web_sys::KeyboardEvent)>::new(move |e: web_sys::KeyboardEvent| {
        if browser_key(&e) {
            // winit's canvas listener calls `event.preventDefault()`, which is looked up on the
            // event: this one does nothing, for this event only.
            let _ = js_sys::Reflect::set(&e, &"preventDefault".into(), &keep);
        }
    });
    // In the capture phase on the window, so it runs before the canvas's own listeners.
    let _ = window.add_event_listener_with_callback_and_bool("keydown", on_key.as_ref().unchecked_ref(), true);
    on_key.forget();
}
