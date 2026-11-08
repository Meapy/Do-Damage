use serde::Deserialize;

/// The registrations of the AIR client's `AssetLoader`, in the same order.
#[derive(Debug, Clone, Deserialize)]
pub struct Manifest {
    /// `AssetLibrary.addImageSet`: a sheet cut into `w`×`h` tiles, numbered left to right, top to
    /// bottom. A later set with the same name replaces an earlier one.
    pub image_sets: Vec<ImageSetDef>,
    /// `AnimatedChars.add`: character sheets, one `sheet_w`×`sheet_h` block per character.
    pub animated_chars: Vec<AnimatedCharDef>,
    pub models: Vec<ModelDef>,
    pub particles: String,
    pub ground_xml: Vec<String>,
    pub object_xml: Vec<String>,
    pub region_xml: Vec<String>,
    pub fonts: Fonts,
    /// Images for the screens outside the game.
    #[serde(default)]
    pub screens: Screens,
    /// A hash of each file's contents (path -> hash), which the web build adds
    /// (build_client_rs.py --web) so browsers ask for each file by its own version and keep it
    /// across builds until it changes.
    #[serde(default)]
    pub versions: std::collections::HashMap<String, String>,
    /// `Emotes`: words drawn as pictures in chat and speech balloons.
    #[serde(default)]
    pub emotes: Vec<EmoteDef>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct EmoteDef {
    /// The text that turns into it, like `:)` or `:kappa:`.
    pub code: String,
    /// (image set, index) of each frame; more than one is animated.
    pub frames: Vec<(String, u32)>,
    #[serde(default = "one")]
    pub scale: f32,
}

fn one() -> f32 {
    1.0
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct Screens {
    /// The title screen's background.
    #[serde(default)]
    pub title: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ImageSetDef {
    pub name: String,
    /// `None` for a set of fully transparent tiles ("invisible").
    pub file: Option<String>,
    pub w: u32,
    pub h: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Facing {
    Right,
    Down,
}

#[derive(Debug, Clone, Deserialize)]
pub struct AnimatedCharDef {
    pub name: String,
    pub file: String,
    pub mask: Option<String>,
    pub w: u32,
    pub h: u32,
    pub sheet_w: u32,
    pub sheet_h: u32,
    pub dir: Facing,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ModelDef {
    pub name: String,
    pub file: String,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Fonts {
    pub regular: String,
    pub bold: String,
}

impl Manifest {
    pub fn parse(bytes: &[u8]) -> crate::Result<Self> {
        serde_json::from_slice(bytes)
            .map_err(|source| crate::Error::Json { path: "manifest.json".into(), source })
    }

    /// Every sprite sheet file (images and masks), each once, in first-use order.
    pub fn sheet_files(&self) -> Vec<&str> {
        let mut seen = std::collections::HashSet::new();
        let sets = self.image_sets.iter().filter_map(|s| s.file.as_deref());
        let chars = self
            .animated_chars
            .iter()
            .flat_map(|c| std::iter::once(c.file.as_str()).chain(c.mask.as_deref()));
        sets.chain(chars).filter(|f| seen.insert(*f)).collect()
    }
}
