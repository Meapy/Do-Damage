//! The game data: ground tiles and objects from the XML files the manifest lists, read the way
//! the AIR client reads them. Later files replace earlier entries of the same type, as there.

pub mod animation;
pub mod imprint;
pub mod ground;
pub mod model;
pub mod object;
pub mod texture;
pub mod xml;

pub use animation::{AnimationData, Animations};
pub use ground::{Animate, AnimateKind, Ground};
pub use model::{Model, ModelFace};
pub use object::{EffectProps, ObjectClass, ObjectDesc, ParticleDesc, ProjectileDesc};
pub use texture::{TextureData, TextureRef};

use bsk_assets::{AssetSource, Manifest};
use std::collections::HashMap;

#[derive(Default)]
pub struct GameData {
    pub grounds: rustc_hash::FxHashMap<u16, Ground>,
    pub objects: rustc_hash::FxHashMap<u16, ObjectDesc>,
    pub object_ids: rustc_hash::FxHashMap<String, u16>,
    pub ground_ids: HashMap<String, u16>,
    /// 3D models by name (`<Model>` in object XML).
    pub models: HashMap<String, Model>,
    /// Particles by id (particles.xml), for objects' `XMLEffect`s.
    pub particles: HashMap<String, ParticleDesc>,
    /// The classes players can pick (`<Player/>` objects, not `<AdminOnly/>`), by type: the item
    /// tooltips list which of them use an item.
    pub classes: Vec<u16>,
}

impl GameData {
    /// Read every ground and object file of the manifest.
    pub async fn load(source: &AssetSource, manifest: &Manifest) -> Result<Self, String> {
        // All at once: in a browser each is a download.
        let (grounds, objects) = futures::join!(
            futures::future::join_all(manifest.ground_xml.iter().map(|f| source.load(f))),
            futures::future::join_all(manifest.object_xml.iter().map(|f| source.load(f)))
        );
        let mut data = GameData::default();
        for (file, bytes) in manifest.ground_xml.iter().zip(grounds) {
            data.add_grounds(&text(file, bytes)?).map_err(|e| format!("{file}: {e}"))?;
        }
        for (file, bytes) in manifest.object_xml.iter().zip(objects) {
            data.add_objects(&text(file, bytes)?).map_err(|e| format!("{file}: {e}"))?;
        }
        // particles.xml: what objects' XMLEffects give off.
        if !manifest.particles.is_empty() {
            let bytes = source.load(&manifest.particles).await;
            match text(&manifest.particles, bytes).and_then(|t| elements(&t, "Particle")) {
                Ok(nodes) => {
                    for p in nodes {
                        data.particles.insert(p.attr("id").unwrap_or("").to_owned(), ParticleDesc::from_xml(&p));
                    }
                }
                Err(e) => log::warn!("{}: {e}", manifest.particles),
            }
        }
        let models = futures::future::join_all(manifest.models.iter().map(|m| source.load(&m.file))).await;
        for (def, bytes) in manifest.models.iter().zip(models) {
            match Model::parse(&text(&def.file, bytes)?) {
                Ok(m) => {
                    data.models.insert(def.name.clone(), m);
                }
                // Model3D.parseFromOBJ skips a broken model with a trace; so do we.
                Err(e) => log::warn!("{}: {e}", def.file),
            }
        }
        Ok(data)
    }

    pub fn add_grounds(&mut self, xml: &str) -> Result<(), String> {
        for node in elements(xml, "Ground")? {
            let g = Ground::from_xml(&node);
            self.ground_ids.insert(g.id.clone(), g.tile_type);
            self.grounds.insert(g.tile_type, g);
        }
        Ok(())
    }

    pub fn add_objects(&mut self, xml: &str) -> Result<(), String> {
        for node in elements(xml, "Object")? {
            // Pets are kept apart in the AIR client (petXMLDataLibrary_), not as objects.
            if node.has("PetBehavior") || node.has("PetAbility") {
                continue;
            }
            let o = ObjectDesc::from_xml(&node);
            self.classes.retain(|&t| t != o.object_type);
            if o.player && !node.has("AdminOnly") {
                let at = self.classes.partition_point(|&t| t < o.object_type);
                self.classes.insert(at, o.object_type);
            }
            self.object_ids.insert(o.id.clone(), o.object_type);
            self.objects.insert(o.object_type, o);
        }
        Ok(())
    }
}

fn text(file: &str, bytes: bsk_assets::Result<Vec<u8>>) -> Result<String, String> {
    let bytes = bytes.map_err(|e| e.to_string())?;
    // Some files start with a UTF-8 byte order mark, and a few hold stray non-UTF-8 bytes that
    // Flash reads past; replace those rather than reject the file.
    let bytes = bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(&bytes);
    if std::str::from_utf8(bytes).is_err() {
        log::warn!("{file}: not valid UTF-8; bad bytes replaced");
    }
    Ok(String::from_utf8_lossy(bytes).into_owned())
}

/// The top-level children named `name` of an XML document.
fn elements(xml: &str, name: &str) -> Result<Vec<xml::Element>, String> {
    let doc = roxmltree::Document::parse_with_options(
        xml,
        roxmltree::ParsingOptions { allow_dtd: true, ..Default::default() },
    )
    .map_err(|e| e.to_string())?;
    Ok(doc.root_element().children().filter(|n| n.has_tag_name(name)).map(xml::Element::from_node).collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Reads the real data files from the repository.
    #[test]
    fn real_data_loads() {
        let source = AssetSource::locate().expect("run inside the repository");
        let manifest = pollster::block_on(source.manifest()).unwrap();
        let data = pollster::block_on(GameData::load(&source, &manifest)).unwrap();
        assert!(data.grounds.len() > 300, "{} grounds", data.grounds.len());
        assert!(data.objects.len() > 3000, "{} objects", data.objects.len());

        // Spot checks against Ground.xml and Players.xml.
        let water = &data.grounds[&0x17];
        assert_eq!(water.id, "Black Water Light");
        assert_eq!(water.animate.kind, AnimateKind::Wave);
        assert!(water.sink && (water.speed - 0.7).abs() < 1e-9 && water.blend_priority == 2);
        let wizard = &data.objects[&data.object_ids["Wizard"]];
        assert_eq!(wizard.class, ObjectClass::Player);
        assert!(matches!(&wizard.texture.main, TextureRef::Animated { set, .. } if set == "players"));
        assert!(data.models.len() > 40, "{} models", data.models.len());
        assert!(!data.models["Four High Wall"].faces.is_empty());
    }
}
