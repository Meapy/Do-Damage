//! A one-line text field's editing, as a desktop text box does it: a cursor, a selection, words,
//! the clipboard, and undo.
//!
//! Keys: Left/Right (Ctrl: a word), Home/End, Shift with any of them to select, Backspace and
//! Delete (Ctrl: a word), Ctrl+A to select all, Ctrl+C/X/V to copy, cut and paste (also
//! Ctrl+Insert, Shift+Delete and Shift+Insert), Ctrl+Z to undo and Ctrl+Y or Ctrl+Shift+Z to redo.

use winit::keyboard::KeyCode;

#[derive(Debug, Clone, Default, PartialEq)]
pub struct LineEdit {
    pub text: String,
    /// Where the cursor is, in characters.
    pub cursor: usize,
    /// The other end of the selection, if any (in characters).
    pub anchor: Option<usize>,
    /// The most characters it holds.
    pub max: usize,
    undo: Vec<(String, usize)>,
    redo: Vec<(String, usize)>,
}

impl LineEdit {
    /// `text` with the cursor at its end.
    pub fn new(text: impl Into<String>, max: usize) -> Self {
        let text: String = text.into();
        let cursor = text.chars().count();
        LineEdit { text, cursor, anchor: None, max, undo: Vec::new(), redo: Vec::new() }
    }

    fn len(&self) -> usize {
        self.text.chars().count()
    }

    /// The byte offset of character `i`.
    fn byte(&self, i: usize) -> usize {
        self.text.char_indices().nth(i).map_or(self.text.len(), |(b, _)| b)
    }

    /// The selected characters (start, end), if any are.
    pub fn selection(&self) -> Option<(usize, usize)> {
        let a = self.anchor?;
        (a != self.cursor).then(|| (a.min(self.cursor), a.max(self.cursor)))
    }

    pub fn selected_text(&self) -> Option<&str> {
        let (a, b) = self.selection()?;
        Some(&self.text[self.byte(a)..self.byte(b)])
    }

    fn remember(&mut self) {
        if self.undo.last().is_none_or(|(t, _)| *t != self.text) {
            self.undo.push((self.text.clone(), self.cursor));
            if self.undo.len() > 100 {
                self.undo.remove(0);
            }
        }
        self.redo.clear();
    }

    /// Remove the selection; true if there was one.
    fn delete_selection(&mut self) -> bool {
        let Some((a, b)) = self.selection() else { return false };
        let (ba, bb) = (self.byte(a), self.byte(b));
        self.text.replace_range(ba..bb, "");
        self.cursor = a;
        self.anchor = None;
        true
    }

    /// Type or paste `s` at the cursor, over the selection; control characters are left out
    /// and the text is kept to `max`.
    pub fn insert(&mut self, s: &str) {
        let s: String = s.chars().map(|c| if c == '\n' || c == '\t' { ' ' } else { c }).filter(|c| !c.is_control()).collect();
        if s.is_empty() && self.selection().is_none() {
            return;
        }
        self.remember();
        self.delete_selection();
        let room = self.max.saturating_sub(self.len());
        let s: String = s.chars().take(room).collect();
        let at = self.byte(self.cursor);
        self.text.insert_str(at, &s);
        self.cursor += s.chars().count();
        self.anchor = None;
    }

    /// Where a word starts before `i`, or ends after it.
    fn word_left(&self, i: usize) -> usize {
        let chars: Vec<char> = self.text.chars().collect();
        let mut j = i;
        while j > 0 && chars[j - 1].is_whitespace() {
            j -= 1;
        }
        while j > 0 && !chars[j - 1].is_whitespace() {
            j -= 1;
        }
        j
    }

    fn word_right(&self, i: usize) -> usize {
        let chars: Vec<char> = self.text.chars().collect();
        let mut j = i;
        while j < chars.len() && !chars[j].is_whitespace() {
            j += 1;
        }
        while j < chars.len() && chars[j].is_whitespace() {
            j += 1;
        }
        j
    }

    /// Move the cursor to `to`, selecting from where it was if `select`.
    fn go(&mut self, to: usize, select: bool) {
        if select {
            self.anchor.get_or_insert(self.cursor);
        } else {
            self.anchor = None;
        }
        self.cursor = to.min(self.len());
    }

    /// An editing key. `text` is what it types. True if the field used it (Enter, Escape and
    /// the like are left to the caller).
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, ctrl: bool, shift: bool) -> bool {
        match code {
            KeyCode::ArrowLeft => {
                let to = match (ctrl, self.selection()) {
                    (true, _) => self.word_left(self.cursor),
                    (false, Some((a, _))) if !shift => a,
                    _ => self.cursor.saturating_sub(1),
                };
                self.go(to, shift);
            }
            KeyCode::ArrowRight => {
                let to = match (ctrl, self.selection()) {
                    (true, _) => self.word_right(self.cursor),
                    (false, Some((_, b))) if !shift => b,
                    _ => self.cursor + 1,
                };
                self.go(to, shift);
            }
            KeyCode::Home => self.go(0, shift),
            KeyCode::End => self.go(self.len(), shift),
            KeyCode::Backspace => {
                if self.selection().is_none() && self.cursor > 0 {
                    let from = if ctrl { self.word_left(self.cursor) } else { self.cursor - 1 };
                    self.anchor = Some(from);
                }
                if self.selection().is_some() {
                    self.remember();
                    self.delete_selection();
                }
            }
            KeyCode::Delete if shift && !ctrl => self.cut(),
            KeyCode::Delete => {
                if self.selection().is_none() && self.cursor < self.len() {
                    let to = if ctrl { self.word_right(self.cursor) } else { self.cursor + 1 };
                    self.anchor = Some(to);
                }
                if self.selection().is_some() {
                    self.remember();
                    self.delete_selection();
                }
            }
            KeyCode::Insert if ctrl => self.copy(),
            KeyCode::Insert if shift => self.paste(),
            KeyCode::KeyA if ctrl => {
                self.anchor = Some(0);
                self.cursor = self.len();
            }
            KeyCode::KeyC if ctrl => self.copy(),
            KeyCode::KeyX if ctrl => self.cut(),
            KeyCode::KeyV if ctrl => self.paste(),
            KeyCode::KeyZ if ctrl && shift => self.redo(),
            KeyCode::KeyZ if ctrl => self.undo(),
            KeyCode::KeyY if ctrl => self.redo(),
            _ if ctrl => return false,
            _ => match text {
                Some(t) if t.chars().any(|c| !c.is_control()) => self.insert(t),
                _ => return false,
            },
        }
        true
    }

    /// Whether the key copies or cuts.
    #[cfg_attr(not(target_arch = "wasm32"), allow(dead_code))]
    pub fn copies(code: KeyCode, ctrl: bool, shift: bool) -> bool {
        match code {
            KeyCode::KeyC | KeyCode::KeyX | KeyCode::Insert => ctrl,
            KeyCode::Delete => shift && !ctrl,
            _ => false,
        }
    }

    fn copy(&self) {
        if let Some(t) = self.selected_text() {
            clipboard::set(t);
        }
    }

    fn cut(&mut self) {
        if let Some(t) = self.selected_text() {
            clipboard::set(t);
            self.remember();
            self.delete_selection();
        }
    }

    fn paste(&mut self) {
        if let Some(t) = clipboard::get() {
            self.insert(&t);
        }
    }

    fn undo(&mut self) {
        if let Some((text, cursor)) = self.undo.pop() {
            self.redo.push((std::mem::replace(&mut self.text, text), self.cursor));
            self.cursor = cursor.min(self.len());
            self.anchor = None;
        }
    }

    fn redo(&mut self) {
        if let Some((text, cursor)) = self.redo.pop() {
            self.undo.push((std::mem::replace(&mut self.text, text), self.cursor));
            self.cursor = cursor.min(self.len());
            self.anchor = None;
        }
    }
}

/// The system clipboard (text only).
pub mod clipboard {
    // In the browser: text the page pasted, for the frame to put into the field being typed in;
    // text the field copied or cut, for the page's copy or cut event that follows the key; and
    // when the last paste event came (ms), to tell whether the browser sent one.
    #[cfg(target_arch = "wasm32")]
    thread_local! {
        static WEB: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) };
        static COPIED: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) };
        static PASTED_AT: std::cell::Cell<f64> = const { std::cell::Cell::new(f64::MIN) };
        // Each text copied is numbered, and the copy event notes the last one it took.
        static COPIES: std::cell::Cell<(u32, u32)> = const { std::cell::Cell::new((0, 0)) };
        // The last text copied and when (ms): the sign-in screens copy it again at the frame.
        static LAST_COPY: std::cell::RefCell<(String, f64)> = const { std::cell::RefCell::new((String::new(), f64::MIN)) };
    }

    #[cfg(windows)]
    pub fn get() -> Option<String> {
        use windows_sys::Win32::System::DataExchange::{CloseClipboard, GetClipboardData, OpenClipboard};
        use windows_sys::Win32::System::Memory::{GlobalLock, GlobalUnlock};
        const CF_UNICODETEXT: u32 = 13;
        // SAFETY: the clipboard is opened and closed here, and the handle it gives is only read
        // while locked; the text ends at its first NUL.
        unsafe {
            if OpenClipboard(0) == 0 {
                return None;
            }
            let handle = GetClipboardData(CF_UNICODETEXT);
            let mut out = None;
            if handle != 0 {
                let ptr = GlobalLock(handle as _) as *const u16;
                if !ptr.is_null() {
                    let mut n = 0;
                    while *ptr.add(n) != 0 {
                        n += 1;
                    }
                    out = Some(String::from_utf16_lossy(std::slice::from_raw_parts(ptr, n)));
                    GlobalUnlock(handle as _);
                }
            }
            CloseClipboard();
            out
        }
    }

    #[cfg(windows)]
    pub fn set(text: &str) {
        use windows_sys::Win32::System::DataExchange::{CloseClipboard, EmptyClipboard, OpenClipboard, SetClipboardData};
        use windows_sys::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalUnlock, GMEM_MOVEABLE};
        const CF_UNICODETEXT: u32 = 13;
        let wide: Vec<u16> = text.encode_utf16().chain(Some(0)).collect();
        // SAFETY: the block is allocated for the whole string and its NUL, filled while locked,
        // then handed to the clipboard, which owns it from then on.
        unsafe {
            if OpenClipboard(0) == 0 {
                return;
            }
            EmptyClipboard();
            let mem = GlobalAlloc(GMEM_MOVEABLE, wide.len() * 2);
            if !mem.is_null() {
                let ptr = GlobalLock(mem) as *mut u16;
                if !ptr.is_null() {
                    std::ptr::copy_nonoverlapping(wide.as_ptr(), ptr, wide.len());
                    GlobalUnlock(mem);
                    SetClipboardData(CF_UNICODETEXT, mem as isize);
                }
            }
            CloseClipboard();
        }
    }

    /// In the browser the text comes with the page's paste event, after the key: see
    /// `take_pasted`. A browser that sends none (Safari, with nothing selected on the page) is
    /// asked for the clipboard instead, which it may first ask the player about.
    #[cfg(target_arch = "wasm32")]
    pub fn get() -> Option<String> {
        use wasm_bindgen::JsCast;
        let window = web_sys::window()?;
        // The sign-in screens take their keys at the next frame, after the paste event.
        let asked = js_sys::Date::now() - 500.0;
        let check = wasm_bindgen::closure::Closure::once_into_js(move || {
            if PASTED_AT.with(|p| p.get()) >= asked {
                return;
            }
            let Some(window) = web_sys::window() else { return };
            let read = wasm_bindgen_futures::JsFuture::from(window.navigator().clipboard().read_text());
            wasm_bindgen_futures::spawn_local(async move {
                if let Some(text) = read.await.ok().and_then(|t| t.as_string()) {
                    WEB.with(|w| *w.borrow_mut() = Some(text));
                }
            });
        });
        // The paste event comes straight after the key press; this runs after both.
        let _ = window.set_timeout_with_callback_and_timeout_and_arguments_0(check.unchecked_ref(), 50);
        None
    }

    /// Listen for the page's paste events (Ctrl+V, Shift+Insert) and fill in its copy and cut
    /// events (Ctrl+C and X, Ctrl+Insert, Shift+Delete) with the field's text.
    #[cfg(target_arch = "wasm32")]
    pub fn listen() {
        use wasm_bindgen::JsCast;
        let Some(document) = web_sys::window().and_then(|w| w.document()) else { return };
        let on_paste = wasm_bindgen::closure::Closure::<dyn FnMut(web_sys::ClipboardEvent)>::new(|e: web_sys::ClipboardEvent| {
            PASTED_AT.with(|p| p.set(js_sys::Date::now()));
            if let Some(text) = e.clipboard_data().and_then(|d| d.get_data("text/plain").ok()) {
                WEB.with(|w| *w.borrow_mut() = Some(text));
            }
            e.prevent_default();
        });
        let _ = document.add_event_listener_with_callback("paste", on_paste.as_ref().unchecked_ref());
        on_paste.forget();
        let on_copy = wasm_bindgen::closure::Closure::<dyn FnMut(web_sys::ClipboardEvent)>::new(|e: web_sys::ClipboardEvent| {
            if let Some(text) = COPIED.with(|c| c.borrow_mut().take()) {
                if let Some(data) = e.clipboard_data() {
                    if data.set_data("text/plain", &text).is_ok() {
                        e.prevent_default();
                        COPIES.with(|c| c.set((c.get().0, c.get().0)));
                    }
                }
            }
        });
        for event in ["copy", "cut"] {
            let _ = document.add_event_listener_with_callback(event, on_copy.as_ref().unchecked_ref());
        }
        on_copy.forget();
    }

    /// Text the page pasted since last asked. Text copied for a copy event that never came is
    /// dropped too, so a later one doesn't take it.
    #[cfg(target_arch = "wasm32")]
    pub fn take_pasted() -> Option<String> {
        COPIED.with(|c| c.borrow_mut().take());
        WEB.with(|w| w.borrow_mut().take())
    }

    #[cfg(not(target_arch = "wasm32"))]
    pub fn take_pasted() -> Option<String> {
        None
    }

    /// The page's copy or cut event, which follows the key, takes the text; the async clipboard
    /// is the fallback for a browser that sends none.
    #[cfg(target_arch = "wasm32")]
    pub fn set(text: &str) {
        use wasm_bindgen::JsCast;
        let now = js_sys::Date::now();
        let (last, at) = LAST_COPY.with(|l| std::mem::replace(&mut *l.borrow_mut(), (text.to_string(), now)));
        if last == text && now - at < 1000.0 {
            return;
        }
        COPIED.with(|c| *c.borrow_mut() = Some(text.to_string()));
        let n = COPIES.with(|c| {
            let n = c.get().0.wrapping_add(1);
            c.set((n, c.get().1));
            n
        });
        let Some(window) = web_sys::window() else { return };
        let text = text.to_string();
        let fallback = wasm_bindgen::closure::Closure::once_into_js(move || {
            if COPIES.with(|c| c.get().1) == n {
                return;
            }
            let Some(window) = web_sys::window() else { return };
            // A browser may refuse it (no permission): the text just isn't copied.
            thread_local! {
                static IGNORE: wasm_bindgen::closure::Closure<dyn FnMut(wasm_bindgen::JsValue)> =
                    wasm_bindgen::closure::Closure::new(|_| ());
            }
            IGNORE.with(|ignore| {
                let _ = window.navigator().clipboard().write_text(&text).catch(ignore);
            });
        });
        let _ = window.set_timeout_with_callback_and_timeout_and_arguments_0(fallback.unchecked_ref(), 0);
    }

    #[cfg(not(any(windows, target_arch = "wasm32")))]
    pub fn get() -> Option<String> {
        None
    }

    #[cfg(not(any(windows, target_arch = "wasm32")))]
    pub fn set(_text: &str) {}
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn editing() {
        let mut e = LineEdit::new("hello world", 20);
        assert!(e.key(KeyCode::ArrowLeft, None, true, false));
        assert_eq!(e.cursor, 6);
        e.key(KeyCode::End, None, false, true);
        assert_eq!(e.selected_text(), Some("world"));
        e.insert("there");
        assert_eq!(e.text, "hello there");
        e.key(KeyCode::Backspace, None, true, false);
        assert_eq!(e.text, "hello ");
        e.key(KeyCode::KeyZ, None, true, false);
        assert_eq!(e.text, "hello there");
        e.key(KeyCode::KeyY, None, true, false);
        assert_eq!(e.text, "hello ");
        e.key(KeyCode::KeyA, None, true, false);
        assert_eq!(e.selection(), Some((0, 6)));
        e.insert("ünïcödé and more than twenty");
        assert_eq!(e.text.chars().count(), 20);
        e.key(KeyCode::Home, None, false, false);
        e.key(KeyCode::Delete, None, false, false);
        assert!(e.text.starts_with("nï"));
    }
}
