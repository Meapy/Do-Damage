//! Field encoding, the same as Flash's `IDataInput`/`IDataOutput` and the server's
//! `NetworkReader`/`NetworkWriter`: big-endian numbers, booleans as one byte, strings as UTF-8
//! with a 16-bit length.

#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum Error {
    #[error("message ended early: needed {needed} more bytes at offset {at}")]
    Short { at: usize, needed: usize },
    #[error("negative length {0}")]
    NegativeLength(i64),
}

pub type Result<T> = std::result::Result<T, Error>;

pub struct Reader<'a> {
    data: &'a [u8],
    pos: usize,
}

impl<'a> Reader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    pub fn remaining(&self) -> usize {
        self.data.len() - self.pos
    }

    pub fn bytes(&mut self, n: usize) -> Result<&'a [u8]> {
        if self.remaining() < n {
            return Err(Error::Short { at: self.pos, needed: n - self.remaining() });
        }
        let out = &self.data[self.pos..self.pos + n];
        self.pos += n;
        Ok(out)
    }

    fn array<const N: usize>(&mut self) -> Result<[u8; N]> {
        Ok(self.bytes(N)?.try_into().expect("length checked"))
    }
}

#[derive(Default)]
pub struct Writer {
    pub buf: Vec<u8>,
}

impl Writer {
    pub fn bytes(&mut self, b: &[u8]) {
        self.buf.extend_from_slice(b);
    }
}

/// A value with a fixed wire encoding.
pub trait Wire: Sized {
    fn read(r: &mut Reader) -> Result<Self>;
    fn write(&self, w: &mut Writer);
}

macro_rules! wire_number {
    ($($t:ty),*) => {$(
        impl Wire for $t {
            fn read(r: &mut Reader) -> Result<Self> {
                Ok(<$t>::from_be_bytes(r.array()?))
            }
            fn write(&self, w: &mut Writer) {
                w.bytes(&self.to_be_bytes());
            }
        }
    )*};
}
wire_number!(u8, i8, u16, i16, u32, i32, f32, f64);

impl Wire for bool {
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(u8::read(r)? != 0)
    }
    fn write(&self, w: &mut Writer) {
        (*self as u8).write(w);
    }
}

/// `readUTF`/`WriteUTF16`: a 16-bit byte count, then UTF-8. Invalid UTF-8 is replaced, not
/// rejected, as Flash does.
impl Wire for String {
    fn read(r: &mut Reader) -> Result<Self> {
        let len = u16::read(r)? as usize;
        Ok(String::from_utf8_lossy(r.bytes(len)?).into_owned())
    }
    fn write(&self, w: &mut Writer) {
        let bytes = self.as_bytes();
        let len = bytes.len().min(u16::MAX as usize);
        (len as u16).write(w);
        w.bytes(&bytes[..len]);
    }
}

/// A string with a 32-bit byte count (`WriteUTF32`; Hello's map JSON, File's contents).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct LongString(pub String);

impl Wire for LongString {
    fn read(r: &mut Reader) -> Result<Self> {
        let len = read_len(i32::read(r)? as i64)?;
        Ok(LongString(String::from_utf8_lossy(r.bytes(len)?).into_owned()))
    }
    fn write(&self, w: &mut Writer) {
        (self.0.len() as i32).write(w);
        w.bytes(self.0.as_bytes());
    }
}

/// The rest of a message, as it is: another message's body inside this one (`SpectateFeed`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Rest(pub Vec<u8>);

impl Wire for Rest {
    fn read(r: &mut Reader) -> Result<Self> {
        Ok(Rest(r.bytes(r.remaining())?.to_vec()))
    }
    fn write(&self, w: &mut Writer) {
        w.bytes(&self.0);
    }
}

fn read_len(n: i64) -> Result<usize> {
    if n < 0 { Err(Error::NegativeLength(n)) } else { Ok(n as usize) }
}

macro_rules! list_type {
    ($(#[$m:meta])* $name:ident, $len:ty) => {
        $(#[$m])*
        #[derive(Debug, Clone, Default, PartialEq)]
        pub struct $name<T>(pub Vec<T>);

        impl<T> std::ops::Deref for $name<T> {
            type Target = Vec<T>;
            fn deref(&self) -> &Vec<T> {
                &self.0
            }
        }

        impl<T> std::ops::DerefMut for $name<T> {
            fn deref_mut(&mut self) -> &mut Vec<T> {
                &mut self.0
            }
        }

        impl<T> From<Vec<T>> for $name<T> {
            fn from(v: Vec<T>) -> Self {
                Self(v)
            }
        }

        impl<T: Wire> Wire for $name<T> {
            fn read(r: &mut Reader) -> Result<Self> {
                let n = read_len(<$len>::read(r)? as i64)?;
                // Don't trust the count for the allocation: each item takes at least one byte.
                let mut items = Vec::with_capacity(n.min(r.remaining()));
                for _ in 0..n {
                    items.push(T::read(r)?);
                }
                Ok(Self(items))
            }
            fn write(&self, w: &mut Writer) {
                (self.0.len() as $len).write(w);
                for item in &self.0 {
                    item.write(w);
                }
            }
        }
    };
}
list_type!(
    /// A list with an unsigned 8-bit count.
    List8, u8
);
list_type!(
    /// A list with a signed 16-bit count (`readShort`).
    List16, i16
);

/// A trailing field that older servers leave out: read only if bytes remain.
impl<T: Wire> Wire for Option<T> {
    fn read(r: &mut Reader) -> Result<Self> {
        if r.remaining() == 0 { Ok(None) } else { T::read(r).map(Some) }
    }
    fn write(&self, w: &mut Writer) {
        if let Some(v) = self {
            v.write(w);
        }
    }
}

/// Declares a struct whose fields are read and written in order.
#[macro_export]
macro_rules! wire_struct {
    ($(#[$m:meta])* pub struct $name:ident { $($(#[$fm:meta])* pub $field:ident : $ty:ty),* $(,)? }) => {
        $(#[$m])*
        #[derive(Debug, Clone, Default, PartialEq)]
        pub struct $name { $($(#[$fm])* pub $field: $ty),* }

        impl $crate::wire::Wire for $name {
            #[allow(unused_variables)]
            fn read(r: &mut $crate::wire::Reader) -> $crate::wire::Result<Self> {
                // Struct expressions evaluate their fields in the order written.
                Ok(Self { $($field: $crate::wire::Wire::read(r)?),* })
            }
            #[allow(unused_variables)]
            fn write(&self, w: &mut $crate::wire::Writer) {
                $($crate::wire::Wire::write(&self.$field, w);)*
            }
        }
    };
}
