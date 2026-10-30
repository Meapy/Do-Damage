//! Objects: players, monsters, items, walls, portals... (`ObjectProperties.as`, `ObjectLibrary.as`).

use crate::texture::TextureData;
use crate::xml::Element;

/// The game object class an entry is drawn and handled as (`ObjectLibrary.TYPE_MAP`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ObjectClass {
    GameObject,
    Player,
    Character,
    Wall,
    ConnectedWall,
    CaveWall,
    DoubleWall,
    Container,
    OneWayContainer,
    Portal,
    GuildHallPortal,
    Projectile,
    Equipment,
    Merchant,
    Sign,
    SpiderWeb,
    Stalagmite,
    /// Everything else, by name (shops, NPCs, boards...): drawn as a plain object for now.
    Other(String),
}

impl ObjectClass {
    fn parse(s: &str) -> Self {
        match s {
            "" | "GameObject" => Self::GameObject,
            "Player" => Self::Player,
            "Character" => Self::Character,
            "Wall" => Self::Wall,
            "ConnectedWall" => Self::ConnectedWall,
            "CaveWall" => Self::CaveWall,
            "DoubleWall" => Self::DoubleWall,
            "Container" => Self::Container,
            "OneWayContainer" => Self::OneWayContainer,
            "Portal" => Self::Portal,
            "GuildHallPortal" => Self::GuildHallPortal,
            "Projectile" => Self::Projectile,
            "Equipment" => Self::Equipment,
            "Merchant" => Self::Merchant,
            "Sign" => Self::Sign,
            "SpiderWeb" => Self::SpiderWeb,
            "Stalagmite" => Self::Stalagmite,
            other => Self::Other(other.to_owned()),
        }
    }

    /// Drawn as a 3D block (walls) rather than a sprite.
    pub fn is_wall(&self) -> bool {
        matches!(self, Self::Wall | Self::ConnectedWall | Self::CaveWall | Self::DoubleWall)
    }
}

/// `<Projectile id="n">` of a weapon or monster (`ProjectileProperties.as`). AS3 reads Speed,
/// LifetimeMS and Size as ints.
#[derive(Debug, Clone, PartialEq)]
pub struct ProjectileDesc {
    pub bullet_type: i32,
    /// The object (by id) whose texture the projectile shows.
    pub object_id: String,
    pub lifetime: i32,
    pub speed: i32,
    /// -1: use the container's size.
    pub size: i32,
    pub min_damage: i32,
    pub max_damage: i32,
    /// Condition effect names it inflicts.
    pub effects: Vec<String>,
    pub multi_hit: bool,
    pub passes_cover: bool,
    pub armor_piercing: bool,
    pub wavy: bool,
    pub parametric: bool,
    pub boomerang: bool,
    pub amplitude: f64,
    pub frequency: f64,
    pub magnitude: f64,
}

impl ProjectileDesc {
    fn from_xml(xml: &Element) -> Self {
        let (min_damage, max_damage) = if xml.has("Damage") {
            (xml.int("Damage", 0), xml.int("Damage", 0))
        } else {
            (xml.int("MinDamage", 0), xml.int("MaxDamage", 0))
        };
        ProjectileDesc {
            bullet_type: xml.attr_int("id", 0),
            object_id: xml.text_of("ObjectId").unwrap_or("").to_owned(),
            lifetime: xml.int("LifetimeMS", 0),
            speed: xml.int("Speed", 0),
            size: if xml.has("Size") { crate::xml::to_int32(xml.number("Size", 0.0)) } else { -1 },
            min_damage,
            max_damage,
            effects: xml.children_named("ConditionEffect").map(|c| c.text.clone()).collect(),
            multi_hit: xml.has("MultiHit"),
            passes_cover: xml.has("PassesCover"),
            armor_piercing: xml.has("ArmorPiercing"),
            wavy: xml.has("Wavy"),
            parametric: xml.has("Parametric"),
            boomerang: xml.has("Boomerang"),
            amplitude: xml.number("Amplitude", 0.0),
            frequency: xml.number("Frequency", 1.0),
            magnitude: xml.number("Magnitude", 3.0),
        }
    }
}

#[derive(Debug, Clone)]
pub struct ObjectDesc {
    pub object_type: u16,
    pub id: String,
    pub display_id: String,
    pub class: ObjectClass,
    pub texture: TextureData,
    /// `<Top>`: a wall's top face.
    pub top: Option<TextureData>,
    /// `<Model>`: the name of a 3D model.
    pub model: Option<String>,
    pub shadow_size: i32,
    pub player: bool,
    pub enemy: bool,
    pub draw_on_ground: bool,
    /// `<GroundSize>`: how many tiles across a `DrawOnGround` object lies, centred on it (1: its
    /// own square). The Nexus garden's realm portals are sunk into the floor this way.
    pub ground_size: f64,
    pub draw_under: bool,
    pub occupy_square: bool,
    pub full_occupy: bool,
    pub enemy_occupy_square: bool,
    pub is_static: bool,
    pub no_mini_map: bool,
    pub z: f64,
    pub flying: bool,
    pub color: Option<u32>,
    pub show_name: bool,
    pub dont_face_attacks: bool,
    pub dont_face_movement: bool,
    pub min_size: i32,
    pub max_size: i32,
    pub size_step: i32,
    pub angle_correction: f64,
    pub rotation: f64,
    pub shadow_color: u32,
    pub projectiles: std::collections::HashMap<i32, ProjectileDesc>,
    /// `<Animation>`s, played now and then over the texture.
    pub animations: Vec<crate::animation::AnimationData>,
    /// `<Effect>`: particles the object gives off (a fountain's spray, a torch's sparks).
    pub effect: Option<EffectProps>,
    pub xml: Element,
}

/// `EffectProperties`: an object's `<Effect>`. Numbers left out (or zero, as the AIR client
/// reads them with `||`) take its defaults.
#[derive(Debug, Clone, PartialEq)]
pub struct EffectProps {
    /// Which effect: `CustomParticles`, `XMLEffect`, `Circle`, `ExplosionComplex`...
    pub id: String,
    /// `XMLEffect`'s particle (from particles.xml), and how often it comes, seconds.
    pub particle: String,
    pub cooldown: f64,
    pub color: u32,
    pub color2: u32,
    /// Particles a second.
    pub rate: f64,
    pub speed: f64,
    pub speed_variance: f64,
    pub spread: f64,
    /// Seconds.
    pub life: f64,
    pub life_variance: f64,
    /// Pixels.
    pub size: i32,
    pub friction: f64,
    /// Upward speed, tiles a second, and its change a second.
    pub rise: f64,
    pub rise_variance: f64,
    pub rise_acc: f64,
    pub range_x: i32,
    pub range_y: i32,
    pub z_offset: f64,
    pub min_radius: f64,
    pub max_radius: f64,
    pub amount: i32,
    /// An image instead of a square of colour.
    pub bitmap: Option<(String, u32)>,
}

impl EffectProps {
    pub fn from_xml(xml: &Element) -> Self {
        // `Number(x) || default`: missing, unreadable or zero all give the default.
        let num = |name: &str, default: f64| xml.attr(name).and_then(|v| v.trim().parse::<f64>().ok()).filter(|v| *v != 0.0).unwrap_or(default);
        let int = |name: &str, default: i32| xml.attr(name).map(crate::xml::as3_int).filter(|v| *v != 0).unwrap_or(default);
        let color = |name: &str| xml.attr(name).map_or(0, |v| crate::xml::as3_int(v) as u32);
        EffectProps {
            id: xml.text.trim().to_owned(),
            particle: xml.attr("particle").unwrap_or("").to_owned(),
            cooldown: xml.attr_number("cooldown", 0.0),
            color: color("color"),
            color2: color("color2"),
            rate: num("rate", 5.0),
            speed: num("speed", 0.0),
            speed_variance: num("speedVariance", 0.5),
            spread: num("spread", 0.0),
            life: num("life", 1.0),
            life_variance: num("lifeVariance", 0.0),
            size: int("size", 3),
            friction: num("friction", 0.0),
            rise: num("rise", 3.0),
            rise_variance: num("riseVariance", 0.0),
            rise_acc: num("riseAcc", 0.0),
            range_x: int("rangeX", 0),
            range_y: int("rangeY", 0),
            z_offset: num("zOffset", 0.0),
            min_radius: num("minRadius", 0.0),
            max_radius: num("maxRadius", 1.0),
            amount: int("amount", 1),
            bitmap: xml.attr("bitmapFile").filter(|f| !f.is_empty()).map(|f| (f.to_owned(), xml.attr_int("bitmapIndex", 0) as u32)),
        }
    }
}

/// `ParticleProperties`: a particle `XMLEffect`s give off (particles.xml).
#[derive(Debug, Clone)]
pub struct ParticleDesc {
    pub texture: crate::texture::TextureData,
    /// `TextureRedrawer.redraw` size (100: five times the sprite).
    pub size: i32,
    pub z: f64,
    /// Seconds.
    pub duration: f64,
    pub animations: Vec<crate::animation::AnimationData>,
}

impl ParticleDesc {
    pub fn from_xml(xml: &Element) -> Self {
        ParticleDesc {
            texture: crate::texture::TextureData::from_xml(xml),
            size: xml.int("Size", 100),
            z: xml.number("Z", 0.0),
            duration: xml.number("Duration", 0.0),
            animations: xml.children_named("Animation").map(crate::animation::AnimationData::from_xml).collect(),
        }
    }
}

/// OP items without the `<Admin/>` tag that can't be traded: the server's `Item.OpItems`
/// (source/Shared/resources/Item.cs). Keep the two lists the same.
const OP_ITEMS: [&str; 36] = [
    "Doomie", "Doomie Amulet", "Doomie Ninjaspell", "Doomie Robe", "Doomie Staff", "Doomie Star",
    "Doomie Stick", "Doomie Yolo", "Nuke", "God Rushing", "Doomed Gods", "Doomed Hero Badge",
    "Class Test", "Test Auraa Generator", "XP Booster Test",
    "Apple", "Gift", "Keys", "TKeys", "LH Lost Halls Debug Item", "Test TP", "Shatters TP",
    "Test Close", "Test Ring", "Snowy Spawner", "InvinceMe", "tots", "frpk", "frs", "tgs",
    "Prod Enemy Item", "Test Horn", "GH1 Key", "GH2 Key", "GH3 Key", "GH4 Key",
];

impl ObjectDesc {
    /// Whether the server refuses to let this item be traded, sold on the market or dropped for
    /// others: admin items and the OP ones around them. Everything else can, Soulbound or not.
    pub fn untradeable(&self) -> bool {
        self.xml.has("Admin") || OP_ITEMS.contains(&self.id.as_str())
    }

    /// Weapons: `<NumProjectiles>` (1), `<ArcGap>` in degrees (11.25), `<RateOfFire>` (1).
    pub fn num_projectiles(&self) -> i32 {
        self.xml.int("NumProjectiles", 1)
    }

    pub fn arc_gap_degrees(&self) -> f64 {
        self.xml.number("ArcGap", 11.25)
    }

    pub fn rate_of_fire(&self) -> f64 {
        self.xml.number("RateOfFire", 1.0)
    }

    /// `<ProtectFromSink>`: doesn't sink into water.
    pub fn protect_from_sink(&self) -> bool {
        self.xml.has("ProtectFromSink")
    }

    pub fn from_xml(xml: &Element) -> Self {
        let (mut min_size, mut max_size, mut size_step) = (100, 100, 5);
        if xml.has("Size") {
            min_size = xml.int("Size", 100);
            max_size = min_size;
            if max_size == -1 {
                (min_size, max_size) = (0, 0);
            }
        } else {
            min_size = xml.int("MinSize", min_size);
            max_size = xml.int("MaxSize", max_size);
            size_step = xml.int("SizeStep", size_step);
        }
        let draw_on_ground = xml.has("DrawOnGround");
        ObjectDesc {
            object_type: xml.attr_int("type", 0) as u16,
            id: xml.attr("id").unwrap_or("").to_owned(),
            display_id: display_name(xml.text_of("DisplayId"), xml.attr("id")),
            class: ObjectClass::parse(xml.text_of("Class").unwrap_or("")),
            texture: TextureData::from_xml(xml),
            top: xml.child("Top").map(TextureData::from_xml),
            model: xml.text_of("Model").map(str::to_owned),
            shadow_size: xml.int("ShadowSize", 100),
            player: xml.has("Player"),
            enemy: xml.has("Enemy"),
            draw_on_ground,
            ground_size: xml.number("GroundSize", 1.0).max(1.0),
            draw_under: draw_on_ground || xml.has("DrawUnder"),
            occupy_square: xml.has("OccupySquare"),
            full_occupy: xml.has("FullOccupy"),
            enemy_occupy_square: xml.has("EnemyOccupySquare"),
            is_static: xml.has("Static"),
            no_mini_map: xml.has("NoMiniMap"),
            z: xml.number("Z", 0.0),
            flying: xml.has("Flying"),
            color: xml.child("Color").map(|c| crate::xml::as3_int(&c.text) as u32),
            show_name: xml.has("ShowName"),
            dont_face_attacks: xml.has("DontFaceAttacks"),
            dont_face_movement: xml.has("DontFaceMovement"),
            min_size,
            max_size,
            size_step: size_step.max(1),
            angle_correction: xml.number("AngleCorrection", 0.0) * std::f64::consts::PI / 4.0,
            rotation: xml.number("Rotation", 0.0),
            shadow_color: xml.int("ShadowColor", 0) as u32,
            projectiles: xml
                .children_named("Projectile")
                .map(|p| {
                    let d = ProjectileDesc::from_xml(p);
                    (d.bullet_type, d)
                })
                .collect(),
            animations: xml.children_named("Animation").map(crate::animation::AnimationData::from_xml).collect(),
            effect: xml.child("Effect").map(EffectProps::from_xml),
            xml: xml.clone(),
        }
    }
}

/// The name shown to players. A `{lang.key}` DisplayId is a language-file key the
/// client doesn't load, so the object's `id` (its plain English name) stands in.
pub(crate) fn display_name(display_id: Option<&str>, id: Option<&str>) -> String {
    match display_id {
        Some(d) if !(d.starts_with('{') && d.ends_with('}')) => d.to_owned(),
        _ => id.or(display_id).unwrap_or("").to_owned(),
    }
}
