//! Sound effects and music (`SoundEffectLibrary`, `Music`): MP3s the account server serves at
//! `/sfx/<name>.mp3` and `/music/<name>.mp3`, fetched once and kept.
//!
//! The output opens on the first key or click, since browsers only allow sound after the
//! player has done something. A sound asked for before it has arrived plays when it does, if
//! that's soon enough to still make sense.
//!
//! Effects are decoded once and mixed through a limiter, and only so many play at once: a big
//! fight asks for dozens of hit sounds a second, which otherwise add up past full scale and
//! clip, swinging the sound between too loud and too quiet.

use bsk_net::Pending;
use rodio::buffer::SamplesBuffer;
use rodio::source::{LimitSettings, Source, Zero};
use std::collections::HashMap;
use std::io::Cursor;
use std::sync::Arc;

/// How long a sound may wait for its file before it's too late to play, ms.
const LATE_MS: i32 = 600;
/// The same effect again this soon after it started is dropped: a volley hitting several
/// enemies at once is one sound, not several stacked into one loud one, ms.
const SAME_GAP_MS: i32 = 45;
/// At most this many copies of one effect play at once.
const MAX_SAME: usize = 3;
/// At most this many effects play at once.
const MAX_VOICES: usize = 16;

/// The player's sound settings (`Parameters`: playSFX, playPewPew, playMusic, musicVolume).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SoundSettings {
    pub sfx: bool,
    /// Weapons' shot sounds, apart from the other effects.
    pub shots: bool,
    pub music: bool,
    /// Every sound and the music, on top of their own volumes.
    pub master_volume: f32,
    pub sfx_volume: f32,
    pub music_volume: f32,
}

impl Default for SoundSettings {
    fn default() -> Self {
        SoundSettings { sfx: true, shots: true, music: true, master_volume: 1.0, sfx_volume: 0.8, music_volume: 0.5 }
    }
}

impl SoundSettings {
    /// How loud effects and music play, with the master volume.
    fn effects(&self) -> f32 {
        self.master_volume * self.sfx_volume
    }

    fn music_level(&self) -> f32 {
        self.master_volume * self.music_volume
    }
}

enum Clip {
    Loading(Pending<Result<Vec<u8>, String>>),
    /// Music, decoded as it plays.
    Ready(Arc<[u8]>),
    /// An effect, decoded once, and how long it lasts (ms).
    Sound(SamplesBuffer, i32),
    Missing,
}

/// Decode a whole effect up front, so playing it costs the audio thread nothing.
fn decode(bytes: Vec<u8>) -> Option<(SamplesBuffer, i32)> {
    let decoder = rodio::Decoder::new(Cursor::new(bytes)).ok()?;
    let (channels, rate) = (decoder.channels(), decoder.sample_rate());
    let samples: Vec<f32> = decoder.collect();
    if samples.is_empty() || channels == 0 || rate == 0 {
        return None;
    }
    let ms = (samples.len() as u64 * 1000 / (channels as u64 * rate as u64)) as i32;
    Some((SamplesBuffer::new(channels, rate, samples), ms.max(1)))
}

/// The effects playing, to keep a fight from stacking up more than the mix can take.
#[derive(Default)]
struct Voices {
    /// (path, started, ends), ms.
    live: Vec<(String, i32, i32)>,
}

impl Voices {
    /// Whether `path`, lasting `len` ms, may start now; counted as playing if so.
    fn admit(&mut self, path: &str, len: i32, now: i32) -> bool {
        self.live.retain(|(_, _, ends)| *ends > now);
        let same = self.live.iter().filter(|(p, _, _)| p == path);
        if same.clone().count() >= MAX_SAME || same.clone().any(|(_, at, _)| now - at < SAME_GAP_MS) {
            return false;
        }
        if self.live.len() >= MAX_VOICES {
            return false;
        }
        self.live.push((path.to_owned(), now, now + len));
        true
    }
}

pub struct Audio {
    /// The account server (`.../sfx/...` is under it).
    base: String,
    clips: HashMap<String, Clip>,
    /// Sounds waiting for their file: (path, volume, asked at).
    waiting: Vec<(String, f32, i32)>,
    output: Option<rodio::OutputStream>,
    /// Where effects go: mixed together, then limited, then out.
    effects: Option<rodio::mixer::Mixer>,
    voices: Voices,
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
            effects: None,
            voices: Voices::default(),
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
                let config = stream.config();
                let (effects, mix) = rodio::mixer::mixer(config.channel_count(), config.sample_rate());
                // Keeps the mix alive while nothing plays (an empty mixer ends).
                effects.add(Zero::new(config.channel_count(), config.sample_rate()));
                stream.mixer().add(mix.limit(LimitSettings::gaming()));
                self.effects = Some(effects);
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
        for (path, clip) in self.clips.iter_mut() {
            if let Clip::Loading(p) = clip {
                if let Some(r) = p.take() {
                    *clip = match r {
                        Ok(bytes) if path.starts_with("sfx/") => match decode(bytes) {
                            Some((sound, ms)) => Clip::Sound(sound, ms),
                            None => {
                                log::debug!("sound: can't decode {path}");
                                Clip::Missing
                            }
                        },
                        Ok(bytes) => Clip::Ready(bytes.into()),
                        Err(e) => {
                            log::debug!("sound: {e}");
                            Clip::Missing
                        }
                    };
                }
            }
        }
        let Some(effects) = &self.effects else { return };
        let volume = self.settings.effects();
        let (clips, voices) = (&self.clips, &mut self.voices);
        self.waiting.retain(|(path, v, at)| match clips.get(path) {
            Some(Clip::Sound(sound, ms)) => {
                if now - at <= LATE_MS && voices.admit(path, *ms, now) {
                    log::debug!("sound: {path}");
                    effects.add(sound.clone().amplify(v * volume));
                }
                false
            }
            Some(Clip::Loading(_)) => now - at <= LATE_MS,
            _ => false,
        });

        // Music: start the wanted piece once it's here; stop when none is wanted.
        let wanted = if self.settings.music && self.settings.music_level() > 0.0 { self.music_wanted.clone() } else { String::new() };
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
                        sink.set_volume(self.settings.music_level());
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
            sink.set_volume(self.settings.music_level());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_volley_on_many_enemies_is_one_sound() {
        let mut v = Voices::default();
        assert!(v.admit("sfx/hit", 300, 0));
        assert!(!v.admit("sfx/hit", 300, 0));
        assert!(!v.admit("sfx/hit", 300, 20));
        assert!(v.admit("sfx/other", 300, 20));
        assert!(v.admit("sfx/hit", 300, 60));
    }

    #[test]
    fn copies_and_voices_are_capped_until_they_end() {
        let mut v = Voices::default();
        for i in 0..MAX_SAME as i32 {
            assert!(v.admit("sfx/hit", 1000, i * 100));
        }
        assert!(!v.admit("sfx/hit", 1000, 500));
        assert!(v.admit("sfx/hit", 1000, 1050), "the first copy has ended");

        let mut v = Voices::default();
        for i in 0..MAX_VOICES {
            assert!(v.admit(&format!("sfx/{i}"), 1000, 0));
        }
        assert!(!v.admit("sfx/new", 1000, 10));
        assert!(v.admit("sfx/new", 1000, 1000));
    }
}
