//! `/fame/replay`: a death's replay, as the world server recorded it
//! (`source/WorldServer/core/replay/ReplayRecorder.cs`): base64 of a gzipped recording.
//!
//! The recording, big-endian: `BSKR`, a version byte, then who died and where (account, character,
//! name, class, the world's name, its map's name and size, its background, the player's object
//! id, the killer, when, ms since the Unix epoch, and how long it runs, ms). Then a snapshot of
//! what was in sight at its start (its length, then messages: id, length, body), and the records
//! from there on: when (ms from the start), what kind, its length, then it. A message record is
//! the message's id and body as the player got it; a hit record is what the server counted of a
//! hit the player took; the last record is the death.

use base64::Engine;
use std::io::Read;

/// What dealt a hit (`Hurt::from`).
pub mod from {
    pub const SHOT: u8 = 0;
    pub const HIT: u8 = 1;
    pub const GROUND: u8 = 2;
    pub const AIR: u8 = 3;
}

/// A hit's flags (`Hurt::flags`).
pub mod flag {
    /// It ignored defense.
    pub const PIERCE: u8 = 1;
    /// The Pirate's luck: it missed.
    pub const DODGED: u8 = 2;
    /// Stacked on a hit just before (only what it dealt beyond it counted).
    pub const STACKED: u8 = 4;
    /// Taken while invulnerable: no harm done.
    pub const NO_HARM: u8 = 8;
}

/// A hit the player took, as the server counted it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Hurt {
    pub from: u8,
    /// The shooter's or hitter's object type, or the ground's tile type.
    pub source_type: u16,
    /// The shooter's or hitter's object id (0 for the ground and the air).
    pub source_id: i32,
    /// Its damage before defense, and what it took off.
    pub raw: i32,
    pub dealt: i32,
    /// The health left after it, of the most.
    pub hp: i32,
    pub max_hp: i32,
    pub flags: u8,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Record {
    /// A message the player got: its id and body.
    Message(u8, Vec<u8>),
    Hurt(Hurt),
    /// The death, by whom.
    Death(String),
}

/// A death's replay.
#[derive(Clone, Debug, PartialEq, Eq, Default)]
pub struct Replay {
    pub account_id: i32,
    pub char_id: i32,
    pub name: String,
    pub object_type: u16,
    pub world: String,
    pub map: String,
    pub size: i16,
    pub background: u8,
    /// The player's object id in it.
    pub target_id: i32,
    pub killer: String,
    /// When they died, ms since the Unix epoch.
    pub died_at: i64,
    /// How long it runs, ms.
    pub duration: i32,
    /// What was in sight at its start: messages (id, body).
    pub snapshot: Vec<(u8, Vec<u8>)>,
    /// Then everything after it, by time (ms from the start).
    pub records: Vec<(i32, Record)>,
    /// The recording as the server keeps it (gzipped): what a downloaded replay file holds.
    pub zipped: Vec<u8>,
}

impl Replay {
    /// The hits taken, with when (ms from the start).
    pub fn hurts(&self) -> impl Iterator<Item = (i32, &Hurt)> {
        self.records.iter().filter_map(|(t, r)| match r {
            Record::Hurt(h) => Some((*t, h)),
            _ => None,
        })
    }
}

const KIND_MESSAGE: u8 = 0;
const KIND_HURT: u8 = 1;
const KIND_DEATH: u8 = 2;

struct Cursor<'a> {
    b: &'a [u8],
    i: usize,
}

impl<'a> Cursor<'a> {
    fn take(&mut self, n: usize) -> Result<&'a [u8], String> {
        let end = self.i.checked_add(n).filter(|&e| e <= self.b.len()).ok_or("the replay ends too soon")?;
        let s = &self.b[self.i..end];
        self.i = end;
        Ok(s)
    }
    fn u8(&mut self) -> Result<u8, String> {
        Ok(self.take(1)?[0])
    }
    fn u16(&mut self) -> Result<u16, String> {
        Ok(u16::from_be_bytes(self.take(2)?.try_into().unwrap()))
    }
    fn i16(&mut self) -> Result<i16, String> {
        Ok(i16::from_be_bytes(self.take(2)?.try_into().unwrap()))
    }
    fn i32(&mut self) -> Result<i32, String> {
        Ok(i32::from_be_bytes(self.take(4)?.try_into().unwrap()))
    }
    fn i64(&mut self) -> Result<i64, String> {
        Ok(i64::from_be_bytes(self.take(8)?.try_into().unwrap()))
    }
    fn len(&mut self) -> Result<usize, String> {
        usize::try_from(self.i32()?).map_err(|_| "a bad length in the replay".to_owned())
    }
    fn string(&mut self) -> Result<String, String> {
        let n = self.u16()? as usize;
        Ok(String::from_utf8_lossy(self.take(n)?).into_owned())
    }
    fn done(&self) -> bool {
        self.i >= self.b.len()
    }
}

/// The recording, once unzipped.
pub fn parse(bytes: &[u8]) -> Result<Replay, String> {
    let mut c = Cursor { b: bytes, i: 0 };
    if c.take(4)? != b"BSKR" {
        return Err("not a replay".into());
    }
    let version = c.u8()?;
    if version != 1 {
        return Err(format!("a replay of a newer kind ({version})"));
    }
    let mut r = Replay {
        account_id: c.i32()?,
        char_id: c.i32()?,
        name: c.string()?,
        object_type: c.u16()?,
        world: c.string()?,
        map: c.string()?,
        size: c.i16()?,
        background: c.u8()?,
        target_id: c.i32()?,
        killer: c.string()?,
        died_at: c.i64()?,
        duration: c.i32()?,
        ..Default::default()
    };
    let n = c.len()?;
    let mut snap = Cursor { b: c.take(n)?, i: 0 };
    while !snap.done() {
        let id = snap.u8()?;
        let n = snap.len()?;
        r.snapshot.push((id, snap.take(n)?.to_vec()));
    }
    while !c.done() {
        let at = c.i32()?;
        let kind = c.u8()?;
        let n = c.len()?;
        let mut body = Cursor { b: c.take(n)?, i: 0 };
        let rec = match kind {
            KIND_MESSAGE => {
                let id = body.u8()?;
                Record::Message(id, body.b[body.i..].to_vec())
            }
            KIND_HURT => Record::Hurt(Hurt {
                from: body.u8()?,
                source_type: body.u16()?,
                source_id: body.i32()?,
                raw: body.i32()?,
                dealt: body.i32()?,
                hp: body.i32()?,
                max_hp: body.i32()?,
                flags: body.u8()?,
            }),
            KIND_DEATH => Record::Death(body.string()?),
            // a kind a newer server keeps: passed over
            _ => continue,
        };
        r.records.push((at, rec));
    }
    Ok(r)
}

/// `/fame/replay`'s answer: `<Replay>base64</Replay>`, or the server's error (`No replay` when
/// none was kept).
pub fn parse_answer(xml: &str) -> Result<Replay, String> {
    if let Some(e) = crate::account::error_text(xml) {
        return Err(e);
    }
    let doc = roxmltree::Document::parse(xml).map_err(|e| e.to_string())?;
    let text: String = doc.root_element().text().unwrap_or("").chars().filter(|c| !c.is_whitespace()).collect();
    let zipped = base64::engine::general_purpose::STANDARD.decode(text).map_err(|e| e.to_string())?;
    read_file(zipped)
}

/// The extension of a downloaded replay (the server's gzipped recording, as is).
pub const FILE_EXTENSION: &str = "bskreplay";

/// A replay file: the gzipped recording, as `/fame/replay` hands it out and the Download
/// button saves it.
pub fn read_file(zipped: Vec<u8>) -> Result<Replay, String> {
    if !zipped.starts_with(&[0x1F, 0x8B]) {
        return Err("not a replay file".into());
    }
    let mut bytes = Vec::new();
    flate2::read::GzDecoder::new(&zipped[..]).read_to_end(&mut bytes).map_err(|e| e.to_string())?;
    let mut r = parse(&bytes)?;
    r.zipped = zipped;
    Ok(r)
}

impl Replay {
    /// A name for its file: who, and which character.
    pub fn file_name(&self) -> String {
        let name: String = self.name.chars().filter(|c| c.is_ascii_alphanumeric()).collect();
        format!("{}-{}-{}.{FILE_EXTENSION}", if name.is_empty() { "replay" } else { &name }, self.account_id, self.char_id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    /// A recording as the server writes it (`ReplayRecorder.Finish`).
    fn recording() -> Vec<u8> {
        let mut b = b"BSKR\x01".to_vec();
        let s = |b: &mut Vec<u8>, t: &str| {
            b.extend((t.len() as u16).to_be_bytes());
            b.extend(t.as_bytes());
        };
        b.extend(2i32.to_be_bytes());
        b.extend(7i32.to_be_bytes());
        s(&mut b, "Tester");
        b.extend(0x0300u16.to_be_bytes());
        s(&mut b, "Realm of the Mad God");
        s(&mut b, "Realm");
        b.extend(2048i16.to_be_bytes());
        b.push(0);
        b.extend(1234i32.to_be_bytes());
        s(&mut b, "Pirate");
        b.extend(1_700_000_000_000i64.to_be_bytes());
        b.extend(15_000i32.to_be_bytes());
        // a snapshot of one message (Update 42, a three-byte body)
        let snap = [&[42u8][..], &3i32.to_be_bytes(), &[1, 2, 3]].concat();
        b.extend((snap.len() as i32).to_be_bytes());
        b.extend(&snap);
        // a NewTick at 100 ms
        b.extend(100i32.to_be_bytes());
        b.push(KIND_MESSAGE);
        b.extend(3i32.to_be_bytes());
        b.extend([10, 9, 8]);
        // a hit at 14 000 ms
        b.extend(14_000i32.to_be_bytes());
        b.push(KIND_HURT);
        let hurt = [&[0u8][..], &0x0600u16.to_be_bytes(), &55i32.to_be_bytes(), &120i32.to_be_bytes(), &100i32.to_be_bytes(), &(-20i32).to_be_bytes(), &700i32.to_be_bytes(), &[1]].concat();
        b.extend((hurt.len() as i32).to_be_bytes());
        b.extend(&hurt);
        // a kind unknown here, passed over
        b.extend(14_500i32.to_be_bytes());
        b.push(9);
        b.extend(2i32.to_be_bytes());
        b.extend([0, 0]);
        // the death
        b.extend(15_000i32.to_be_bytes());
        b.push(KIND_DEATH);
        b.extend(8i32.to_be_bytes());
        s(&mut b, "Pirate");
        b
    }

    #[test]
    fn reads_a_recording() {
        let r = parse(&recording()).unwrap();
        assert_eq!((r.account_id, r.char_id, r.name.as_str(), r.object_type, r.target_id), (2, 7, "Tester", 0x0300, 1234));
        assert_eq!((r.world.as_str(), r.map.as_str(), r.size, r.killer.as_str(), r.duration), ("Realm of the Mad God", "Realm", 2048, "Pirate", 15_000));
        assert_eq!(r.snapshot, vec![(42, vec![1, 2, 3])]);
        assert_eq!(r.records.len(), 3);
        assert_eq!(r.records[0], (100, Record::Message(10, vec![9, 8])));
        let (at, h) = r.hurts().next().unwrap();
        assert_eq!((at, h.from, h.source_type, h.source_id, h.raw, h.dealt, h.hp, h.max_hp, h.flags), (14_000, from::SHOT, 0x0600, 55, 120, 100, -20, 700, flag::PIERCE));
        assert_eq!(r.records[2], (15_000, Record::Death("Pirate".into())));
    }

    #[test]
    fn reads_the_answer() {
        let mut gz = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::default());
        gz.write_all(&recording()).unwrap();
        let b64 = base64::engine::general_purpose::STANDARD.encode(gz.finish().unwrap());
        let r = parse_answer(&format!("<Replay>{b64}</Replay>")).unwrap();
        assert_eq!(r.name, "Tester");
        // the file a download saves reads the same
        let again = read_file(r.zipped.clone()).unwrap();
        assert_eq!(again, r);
        assert_eq!(r.file_name(), "Tester-2-7.bskreplay");
        assert!(read_file(recording()).is_err());
        assert_eq!(parse_answer("<Error>No replay</Error>"), Err("No replay".into()));
        assert!(parse(b"BSKR\x01\x00").is_err());
    }
}
