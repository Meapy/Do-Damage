//! 3D models (`Model3D.parseFromOBJ`): OBJ text with vertices, texture coordinates and faces.
//! Faces whose material starts with "Solid" are filled with the object's colour instead of its
//! texture; those whose material starts with "Glow" give off their own light, so they're drawn
//! unshaded (lit windows and lanterns: the Nexus garden's models, tools/build_nexus_garden.py). Faces are kept in `ModelFace3D.compare` order (lowest first), which is the order
//! they're drawn in.

#[derive(Debug, Clone, PartialEq)]
pub struct ModelFace {
    /// Indices into `Model::vertices`, a polygon.
    pub indices: Vec<usize>,
    pub textured: bool,
    /// Lit from within: drawn at full brightness whatever way it faces.
    pub glow: bool,
}

#[derive(Debug, Clone, PartialEq, Default)]
pub struct Model {
    /// Position (x, y, z) and texture coordinate (u, v) of each distinct `v/vt` pair.
    pub vertices: Vec<([f64; 3], [f64; 2])>,
    pub faces: Vec<ModelFace>,
}

impl Model {
    pub fn parse(obj: &str) -> Result<Model, String> {
        let mut vs: Vec<[f64; 3]> = Vec::new();
        let mut vts: Vec<[f64; 2]> = Vec::new();
        let mut keys: Vec<String> = Vec::new();
        let mut key_index = std::collections::HashMap::new();
        let mut faces: Vec<(Vec<String>, Option<String>)> = Vec::new();
        let mut material: Option<String> = None;
        let num = |s: &str| s.parse::<f64>().map_err(|e| format!("{s}: {e}"));
        for line in obj.lines() {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            let mut fields = line.split_whitespace();
            let Some(kind) = fields.next() else { continue };
            let rest: Vec<&str> = fields.collect();
            match kind {
                "v" if rest.len() == 3 => vs.push([num(rest[0])?, num(rest[1])?, num(rest[2])?]),
                "vt" if rest.len() == 2 => vts.push([num(rest[0])?, num(rest[1])?]),
                "f" if rest.len() >= 3 => {
                    for k in &rest {
                        if !key_index.contains_key(*k) {
                            key_index.insert(k.to_string(), keys.len());
                            keys.push(k.to_string());
                        }
                    }
                    faces.push((rest.iter().map(|k| k.to_string()).collect(), material.clone()));
                }
                "usemtl" if rest.len() == 1 => material = Some(rest[0].to_owned()),
                "v" | "vt" | "f" | "usemtl" => return Err(format!("bad line: {line}")),
                _ => {}
            }
        }
        let mut model = Model::default();
        for key in &keys {
            let mut parts = key.split('/');
            let vi: usize = parts.next().unwrap_or("").parse().map_err(|_| format!("bad face vertex {key}"))?;
            let pos = *vs.get(vi.wrapping_sub(1)).ok_or(format!("no vertex {vi}"))?;
            let uv = match parts.next().filter(|t| !t.is_empty()) {
                Some(t) => {
                    let ti: usize = t.parse().map_err(|_| format!("bad face uv {key}"))?;
                    *vts.get(ti.wrapping_sub(1)).ok_or(format!("no uv {ti}"))?
                }
                None => [0.0, 0.0],
            };
            model.vertices.push((pos, uv));
        }
        for (keys, mtl) in faces {
            let indices = keys.iter().map(|k| key_index[k]).collect();
            let textured = mtl.as_ref().is_none_or(|m| !m.starts_with("Solid"));
            let glow = mtl.as_ref().is_some_and(|m| m.starts_with("Glow"));
            model.faces.push(ModelFace { indices, textured, glow });
        }
        // ModelFace3D.compare: by lowest z, then highest z. The sort is stable, as is
        // Array.sort for equal keys in practice.
        let vertices = &model.vertices;
        let z_range = |f: &ModelFace| {
            let zs = f.indices.iter().map(|&i| vertices[i].0[2]);
            let lo = zs.clone().fold(f64::MAX, f64::min);
            // Number.MIN_VALUE is the smallest positive number, not the most negative.
            let hi = zs.fold(f64::MIN_POSITIVE, f64::max);
            (lo, hi)
        };
        let mut keyed: Vec<((f64, f64), ModelFace)> =
            std::mem::take(&mut model.faces).into_iter().map(|f| (z_range(&f), f)).collect();
        keyed.sort_by(|a, b| a.0 .0.total_cmp(&b.0 .0).then(a.0 .1.total_cmp(&b.0 .1)));
        model.faces = keyed.into_iter().map(|(_, f)| f).collect();
        Ok(model)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cube_top_and_bottom() {
        let obj = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\n\
                   vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n\
                   usemtl Top\nf 5/1 6/2 7/3 8/4\nusemtl Solid_Bottom\nf 1/1 2/2 3/3 4/4\n";
        let m = Model::parse(obj).unwrap();
        assert_eq!(m.vertices.len(), 8);
        // The bottom (z = 0) sorts before the top, and is solid-coloured.
        assert_eq!(m.faces.len(), 2);
        assert!(!m.faces[0].textured && m.faces[1].textured);
        assert_eq!(m.vertices[m.faces[1].indices[2]], ([1.0, 1.0, 1.0], [1.0, 1.0]));
    }
}
