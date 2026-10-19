//! Sound effects and music (`SoundEffectLibrary`, `Music`): MP3s the account server serves at
//! `/sfx/<name>.mp3` and `/music/<name>.mp3`, fetched once and kept.
//!
//! The output opens on the first key or click, since browsers only allow sound after the
//! player has done something. A sound asked for before it has arrived plays when it does, if
//! that's soon enough to still make sense.

use bsk_net::Pending;
use std::collections::HashMap;
use std::io::Cursor;
use std::sync::Arc;

/// How long a sound may wait for its file before it's too late to play, ms.
const LATE_MS: i32 = 600;

/// The player's sound settings (`Parameters`: playSFX, playPewPew, playMusic, musicVolume).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SoundSettings {
    pub sfx: bool,
    /// Weapons' shot sounds, apart from the other effects.
    pub shots: bool,
    pub music: bool,
    pub master_volume: f32,
    pub sfx_volume: f32,
    pub music_volume: f32,
}

impl Default for SoundSettings {
    fn default() -> Self {
        SoundSettings {
            sfx: true,
            shots: true,
            music: true,
            master_volume: 1.0,
            sfx_volume: 0.8,
            music_volume: 0.5,
        }
    }
}

impl SoundSettings {
    fn effects_output_volume(&self) -> f32 {
        self.master_volume * self.sfx_volume
    }

    fn music_output_volume(&self) -> f32 {
        self.master_volume * self.music_volume
    }
}

enum Clip {
    Loading(Pending<Result<Vec<u8>, String>>),
    Ready(Arc<[u8]>),
    Missing,
}

pub struct Audio {
    /// The account server (`.../sfx/...` is under it).
    base: String,
    clips: HashMap<String, Clip>,
    /// Sounds waiting for their file: (path, volume, asked at).
    waiting: Vec<(String, f32, i32)>,
    output: Option<rodio::OutputStream>,
    /// Whether opening the output was tried (it isn't tried again after failing).
    tried: bool,
    music: Option<rodio::Sink>,
    /// The music asked for, and the music playing.
    music_wanted: String,
    music_playing: String,
    pub settings: SoundSettings,
}

impl Audio {
    pub fn new(base: &str) -> Self {
        Audio {
            base: base.trim_end_matches('/').to_owned(),
            clips: HashMap::new(),
            waiting: Vec::new(),
            output: None,
            tried: false,
            music: None,
            music_wanted: String::new(),
            music_playing: String::new(),
            settings: SoundSettings::default(),
        }
    }

    /// The player did something: sound may start now (browsers need this).
    pub fn wake(&mut self) {
        if self.tried {
            return;
        }
        self.tried = true;
        match rodio::OutputStreamBuilder::open_default_stream() {
            Ok(mut stream) => {
                stream.log_on_drop(false);
                self.output = Some(stream);
            }
            Err(e) => log::warn!("no sound: {e}"),
        }
    }

    fn fetch(&mut self, path: &str) {
        if !self.clips.contains_key(path) {
            let url = format!("{}/{path}.mp3", self.base);
            self.clips.insert(path.to_owned(), Clip::Loading(bsk_net::http::get_bytes(&url)));
        }
    }

    /// `SoundEffectLibrary.play`: a sound effect by name (`monster/default_hit`). `shot` marks
    /// weapon sounds, which have their own setting.
    pub fn play(&mut self, name: &str, volume: f32, shot: bool, now: i32) {
        let on = if shot { self.settings.shots } else { self.settings.sfx };
        if !on || name.is_empty() || self.output.is_none() {
            return;
        }
        let path = format!("sfx/{name}");
        self.fetch(&path);
        self.waiting.push((path, volume, now));
    }

    /// `Music.load`: the music for this map ("" for none).
    pub fn music(&mut self, name: &str) {
        self.music_wanted = name.to_owned();
    }

    /// Start what's arrived; keep the music in step with what's wanted.
    pub fn update(&mut self, now: i32) {
        for clip in self.clips.values_mut() {
            if let Clip::Loading(p) = clip {
                if let Some(r) = p.take() {
                    *clip = match r {
                        Ok(bytes) => Clip::Ready(bytes.into()),
                        Err(e) => {
                            log::debug!("sound: {e}");
                            Clip::Missing
                        }
                    };
                }
            }
        }
        let Some(output) = &self.output else { return };
        let volume = self.settings.effects_output_volume();
        let clips = &self.clips;
        self.waiting.retain(|(path, v, at)| match clips.get(path) {
            Some(Clip::Ready(bytes)) => {
                if now - at <= LATE_MS {
                    log::debug!("sound: {path}");
                    if let Ok(source) = rodio::Decoder::new(Cursor::new(bytes.clone())) {
                        let sink = rodio::Sink::connect_new(output.mixer());
                        sink.set_volume(v * volume);
                        sink.append(source);
                        sink.detach();
                    }
                }
                false
            }
            Some(Clip::Loading(_)) => now - at <= LATE_MS,
            _ => false,
        });

        // Music: start the wanted piece once it's here; stop when none is wanted.
        let music_volume = self.settings.music_output_volume();
        let wanted = if self.settings.music && music_volume > 0.0 { self.music_wanted.clone() } else { String::new() };
        if wanted != self.music_playing {
            if let Some(sink) = self.music.take() {
                sink.stop();
            }
            self.music_playing.clear();
            if !wanted.is_empty() {
                let path = format!("music/{wanted}");
                self.fetch(&path);
                if let (Some(Clip::Ready(bytes)), Some(output)) = (self.clips.get(&path), &self.output) {
                    if let Ok(source) = rodio::Decoder::new_looped(Cursor::new(bytes.clone())) {
                        let sink = rodio::Sink::connect_new(output.mixer());
                        sink.set_volume(music_volume);
                        sink.append(source);
                        self.music = Some(sink);
                        log::debug!("music: {wanted}");
                        self.music_playing = wanted;
                    }
                } else if matches!(self.clips.get(&path), Some(Clip::Missing)) {
                    // No such music: don't ask again.
                    self.music_playing = wanted;
                }
            }
        }
        if let Some(sink) = &self.music {
            sink.set_volume(music_volume);
        }
    }
}
