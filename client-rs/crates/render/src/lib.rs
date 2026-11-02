//! The GPU renderer: textured quads from atlas pages, batched into as few draws as possible.
//!
//! It runs on wgpu, so the same code draws through DirectX 12, Vulkan, Metal, WebGPU or WebGL2.
//! A frame is `begin`, any number of `sprite` (quads) and `triangle` calls in back-to-front
//! order, then `present`. Draw order is kept across the two kinds.

mod atlas;

pub use atlas::AtlasRect;
use atlas::{Atlas, Page};

/// Atlas page size, or the GPU's limit if lower (WebGL2 only guarantees 2048). One sheet is
/// 2048×2048, which needs a bigger page once padded.
const PAGE_SIZE: u32 = 4096;

/// Atlas pages bound together: draws from any of them go out in one call. (WebGL2 allows 16
/// textures a shader stage.)
const GROUP: usize = 8;

#[repr(C)]
#[derive(Debug, Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Instance {
    axes: [f32; 4],
    origin: [f32; 2],
    uv: [f32; 4],
    mul: [f32; 4],
    add: [f32; 4],
    /// Which page of its group.
    page: u32,
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Globals {
    screen: [f32; 2],
    _pad: [f32; 2],
}

#[repr(C)]
#[derive(Debug, Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct TriVertex {
    pos: [f32; 2],
    /// Within `rect`: 0..1 across it, repeating if `wrap` is 1.
    uv: [f32; 2],
    /// The source rectangle on the atlas page (x, y, w, h), in page coordinates.
    rect: [f32; 4],
    mul: [f32; 4],
    wrap: f32,
    /// Which page of its group.
    page: u32,
}

#[derive(Clone, Copy, PartialEq)]
enum Kind {
    Quads,
    Triangles,
}

/// A run of consecutive quads (instances) or triangle vertices drawn from the same group of
/// pages.
struct Run {
    kind: Kind,
    group: u16,
    start: u32,
    end: u32,
}

/// An affine transform from the unit square to the screen: the quad's corner (0, 0) lands on
/// `origin`, (1, 0) on `origin + x_axis`, (0, 1) on `origin + y_axis`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Quad {
    pub origin: [f32; 2],
    pub x_axis: [f32; 2],
    pub y_axis: [f32; 2],
}

impl Quad {
    /// An upright rectangle.
    pub fn rect(x: f32, y: f32, w: f32, h: f32) -> Self {
        Self { origin: [x, y], x_axis: [w, 0.0], y_axis: [0.0, h] }
    }

    /// A `w`×`h` rectangle centred on (`cx`, `cy`), turned by `angle` radians.
    pub fn rotated(cx: f32, cy: f32, w: f32, h: f32, angle: f32) -> Self {
        let (s, c) = angle.sin_cos();
        let x_axis = [w * c, w * s];
        let y_axis = [-h * s, h * c];
        let origin = [cx - (x_axis[0] + y_axis[0]) / 2.0, cy - (x_axis[1] + y_axis[1]) / 2.0];
        Self { origin, x_axis, y_axis }
    }
}

/// Flash's ColorTransform: `colour * mul + add`, with `add` in 0..1 rather than 0..255.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Tint {
    pub mul: [f32; 4],
    pub add: [f32; 3],
}

impl Tint {
    pub const NONE: Tint = Tint { mul: [1.0; 4], add: [0.0; 3] };

    /// A solid colour, for rectangles drawn with `Renderer::white`.
    pub fn color(rgb: u32, alpha: f32) -> Self {
        let [r, g, b] = rgb_f32(rgb);
        Tint { mul: [r, g, b, alpha], add: [0.0; 3] }
    }
}

/// A colour filter applied to a sprite's texels before its tint (Flash's ColorMatrixFilters).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Filter {
    None,
    /// `MoreColorUtil.greyscaleFilterMatrix`: paused, stasis, petrified.
    Grey,
    /// `MoreColorUtil.redFilterMatrix`: cursed.
    Red,
}

/// A filter over the whole world (the AIR client's Stage3D post effects).
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum ScreenFilter {
    /// Paused: grey.
    Grey,
    /// Blind: nearly black.
    Blind,
    /// Drunk: blurred by this many pixels.
    Blur(f32),
}

/// How the world looks over what the art draws (Options, Graphics): 0 is off.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct Look {
    /// A glow round bright colours (bloom), and how strong.
    pub glow: f32,
    /// Livelier colours (vibrance, a little contrast) and darker edges.
    pub vivid: f32,
}

impl Look {
    fn on(&self) -> bool {
        self.glow > 0.0 || self.vivid > 0.0
    }
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct PostParams {
    mode: f32,
    radius: f32,
    texel: [f32; 2],
    dir: [f32; 2],
    glow: f32,
    vivid: f32,
    paper: f32,
    headroom: f32,
    _pad: [f32; 2],
}

/// HDR output: SDR white in scRGB units (1.0 is 80 nits) and how many times brighter than that
/// the glow may reach.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Hdr {
    pub paper: f32,
    pub headroom: f32,
}

/// The texture the world is drawn into when a screen filter or the look is on, and the
/// quarter-size pair the glow is spread across. With HDR, `frame` takes the whole frame (world
/// and interface) before `fs_hdr` puts it on the screen.
struct Offscreen {
    width: u32,
    height: u32,
    view: wgpu::TextureView,
    bind_group: wgpu::BindGroup,
    /// The bright light picked out (the tight glow), spread across into `bloom_b`, then down
    /// into `bloom_c` (the wide glow).
    bloom_a: wgpu::TextureView,
    bloom_b: wgpu::TextureView,
    bloom_c: wgpu::TextureView,
    bright_group: wgpu::BindGroup,
    blur_h_group: wgpu::BindGroup,
    blur_v_group: wgpu::BindGroup,
    frame: wgpu::Texture,
    frame_view: wgpu::TextureView,
    hdr_group: wgpu::BindGroup,
}

pub fn rgb_f32(rgb: u32) -> [f32; 3] {
    [((rgb >> 16) & 255) as f32 / 255.0, ((rgb >> 8) & 255) as f32 / 255.0, (rgb & 255) as f32 / 255.0]
}

pub struct Renderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    surface: wgpu::Surface<'static>,
    config: wgpu::SurfaceConfiguration,
    pipeline: wgpu::RenderPipeline,
    tri_pipeline: wgpu::RenderPipeline,
    tri_buffer: wgpu::Buffer,
    tri_capacity: usize,
    tri_vertices: Vec<TriVertex>,
    page_layout: wgpu::BindGroupLayout,
    sampler: wgpu::Sampler,
    globals: wgpu::Buffer,
    globals_group: wgpu::BindGroup,
    instance_buffer: wgpu::Buffer,
    instance_capacity: usize,
    atlas: Atlas,
    /// One binding per GROUP pages, and how many pages they were made for.
    groups: Vec<wgpu::BindGroup>,
    groups_pages: usize,
    white: AtlasRect,
    instances: Vec<Instance>,
    runs: Vec<Run>,
    /// GPU draw calls of the last frame.
    pub last_draws: u32,
    can_capture: bool,
    capture_next: bool,
    captured: Option<image::RgbaImage>,
    post_pipeline: wgpu::RenderPipeline,
    post_layout: wgpu::BindGroupLayout,
    post_sampler: wgpu::Sampler,
    post_params: wgpu::Buffer,
    bright_pipeline: wgpu::RenderPipeline,
    blur_pipeline: wgpu::RenderPipeline,
    blur_h_params: wgpu::Buffer,
    blur_v_params: wgpu::Buffer,
    look: Look,
    /// What everything but the HDR pass draws in: the surface's format without HDR.
    sdr_format: wgpu::TextureFormat,
    hdr_pipeline: wgpu::RenderPipeline,
    /// The surface offers `Rgba16Float` (scRGB on DirectX 12 and Vulkan).
    hdr_supported: bool,
    hdr: Option<Hdr>,
    offscreen: Option<Offscreen>,
    /// Where the world's runs end, and the filter it's drawn through, this frame.
    split: Option<usize>,
    /// Where the world's runs that can glow end (the rest of the world, names and numbers, is
    /// drawn after the glow is picked out).
    glow_split: Option<usize>,
    screen_filter: Option<ScreenFilter>,
}

impl Renderer {
    /// Set up the GPU for a window. `target` is usually an `Arc<winit::window::Window>`.
    pub async fn new(
        target: impl Into<wgpu::SurfaceTarget<'static>>,
        width: u32,
        height: u32,
    ) -> Result<Self, String> {
        let instance = wgpu::util::new_instance_with_webgpu_detection(&wgpu::InstanceDescriptor::default()).await;
        let surface = instance.create_surface(target).map_err(|e| e.to_string())?;
        let adapter = instance
            .request_adapter(&wgpu::RequestAdapterOptions {
                power_preference: wgpu::PowerPreference::HighPerformance,
                compatible_surface: Some(&surface),
                force_fallback_adapter: false,
            })
            .await
            .ok_or("no graphics adapter")?;
        let info = adapter.get_info();
        log::info!("GPU: {} ({:?})", info.name, info.backend);
        let limits = wgpu::Limits::downlevel_webgl2_defaults().using_resolution(adapter.limits());
        let (device, queue) = adapter
            .request_device(
                &wgpu::DeviceDescriptor {
                    label: Some("bsk"),
                    required_features: wgpu::Features::empty(),
                    required_limits: limits,
                    memory_hints: wgpu::MemoryHints::Performance,
                },
                None,
            )
            .await
            .map_err(|e| e.to_string())?;

        let caps = surface.get_capabilities(&adapter);
        // Flash blends in gamma space; a non-sRGB target keeps the colours the same.
        let format = caps.formats.iter().copied().find(|f| !f.is_srgb()).unwrap_or(caps.formats[0]);
        let alpha_mode = caps.alpha_modes[0];
        let hdr_supported = caps.formats.contains(&wgpu::TextureFormat::Rgba16Float);
        log::info!("surface {format:?}, HDR (Rgba16Float) {}", if hdr_supported { "offered" } else { "not offered" });
        // Copying frames out (screenshots) needs COPY_SRC on the surface, where it's offered.
        let can_capture = caps.usages.contains(wgpu::TextureUsages::COPY_SRC)
            && matches!(format, wgpu::TextureFormat::Bgra8Unorm | wgpu::TextureFormat::Rgba8Unorm);
        let usage = if can_capture {
            wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC
        } else {
            wgpu::TextureUsages::RENDER_ATTACHMENT
        };
        let config = wgpu::SurfaceConfiguration {
            usage,
            format,
            width: width.max(1),
            height: height.max(1),
            present_mode: wgpu::PresentMode::AutoVsync,
            desired_maximum_frame_latency: 2,
            alpha_mode,
            view_formats: vec![],
        };
        surface.configure(&device, &config);

        let shader = device.create_shader_module(wgpu::include_wgsl!("sprite.wgsl"));
        let globals_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("globals"),
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::VERTEX,
                ty: wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
                count: None,
            }],
        });
        let page_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("page"),
            entries: &page_entries(),
        });
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("sprite"),
            bind_group_layouts: &[&globals_layout, &page_layout],
            push_constant_ranges: &[],
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("sprite"),
            layout: Some(&layout),
            vertex: wgpu::VertexState {
                module: &shader,
                entry_point: Some("vs_main"),
                compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout {
                    array_stride: std::mem::size_of::<Instance>() as u64,
                    step_mode: wgpu::VertexStepMode::Instance,
                    attributes: &wgpu::vertex_attr_array![
                        0 => Float32x4, 1 => Float32x2, 2 => Float32x4, 3 => Float32x4, 4 => Float32x4, 5 => Uint32
                    ],
                }],
            },
            fragment: Some(wgpu::FragmentState {
                module: &shader,
                entry_point: Some("fs_main"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: Some(wgpu::BlendState::PREMULTIPLIED_ALPHA_BLENDING),
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview: None,
            cache: None,
        });

        let tri_shader = device.create_shader_module(wgpu::include_wgsl!("triangle.wgsl"));
        let tri_pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("triangles"),
            layout: Some(&layout),
            vertex: wgpu::VertexState {
                module: &tri_shader,
                entry_point: Some("vs_main"),
                compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout {
                    array_stride: std::mem::size_of::<TriVertex>() as u64,
                    step_mode: wgpu::VertexStepMode::Vertex,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x2, 1 => Float32x2, 2 => Float32x4, 3 => Float32x4, 4 => Float32, 5 => Uint32],
                }],
            },
            fragment: Some(wgpu::FragmentState {
                module: &tri_shader,
                entry_point: Some("fs_main"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: Some(wgpu::BlendState::PREMULTIPLIED_ALPHA_BLENDING),
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview: None,
            cache: None,
        });
        let post_shader = device.create_shader_module(wgpu::include_wgsl!("post.wgsl"));
        let post_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("post"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 2,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 3,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 4,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
            ],
        });
        let post_pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("post"),
            bind_group_layouts: &[&post_layout],
            push_constant_ranges: &[],
        });
        let post_stage = |entry: &str, format: wgpu::TextureFormat| {
            device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
                label: Some(entry),
                layout: Some(&post_pipeline_layout),
                vertex: wgpu::VertexState {
                    module: &post_shader,
                    entry_point: Some("vs_main"),
                    compilation_options: Default::default(),
                    buffers: &[],
                },
                fragment: Some(wgpu::FragmentState {
                    module: &post_shader,
                    entry_point: Some(entry),
                    compilation_options: Default::default(),
                    targets: &[Some(wgpu::ColorTargetState { format, blend: None, write_mask: wgpu::ColorWrites::ALL })],
                }),
                primitive: wgpu::PrimitiveState::default(),
                depth_stencil: None,
                multisample: wgpu::MultisampleState::default(),
                multiview: None,
                cache: None,
            })
        };
        let post_pipeline = post_stage("fs_main", format);
        let bright_pipeline = post_stage("fs_bright", format);
        let blur_pipeline = post_stage("fs_blur", format);
        let hdr_pipeline = post_stage("fs_hdr", wgpu::TextureFormat::Rgba16Float);
        let post_sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("post"),
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let params_buffer = |label| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label),
                size: std::mem::size_of::<PostParams>() as u64,
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            })
        };
        let post_params = params_buffer("post");
        let blur_h_params = params_buffer("blur across");
        let blur_v_params = params_buffer("blur down");

        let tri_capacity = 3 * 1024;
        let tri_buffer = Self::make_buffer(&device, "triangles", tri_capacity * std::mem::size_of::<TriVertex>());

        let globals = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("globals"),
            size: std::mem::size_of::<Globals>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let globals_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("globals"),
            layout: &globals_layout,
            entries: &[wgpu::BindGroupEntry { binding: 0, resource: globals.as_entire_binding() }],
        });
        // Pixel art: nearest-neighbour, like Flash with smoothing off.
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("pixel"),
            mag_filter: wgpu::FilterMode::Nearest,
            min_filter: wgpu::FilterMode::Nearest,
            ..Default::default()
        });
        let instance_capacity = 4096;
        let instance_buffer = Self::make_instance_buffer(&device, instance_capacity);

        let page_size = PAGE_SIZE.min(device.limits().max_texture_dimension_2d);
        let mut renderer = Self {
            device,
            queue,
            surface,
            config,
            pipeline,
            tri_pipeline,
            tri_buffer,
            tri_capacity,
            tri_vertices: Vec::new(),
            page_layout,
            sampler,
            globals,
            globals_group,
            instance_buffer,
            instance_capacity,
            atlas: Atlas::new(page_size),
            groups: Vec::new(),
            groups_pages: 0,
            white: AtlasRect { page: 0, x: 0, y: 0, w: 0, h: 0 },
            instances: Vec::with_capacity(instance_capacity),
            runs: Vec::new(),
            last_draws: 0,
            can_capture,
            capture_next: false,
            captured: None,
            post_pipeline,
            post_layout,
            post_sampler,
            post_params,
            bright_pipeline,
            blur_pipeline,
            blur_h_params,
            blur_v_params,
            look: Look::default(),
            sdr_format: format,
            hdr_pipeline,
            hdr_supported,
            hdr: None,
            offscreen: None,
            split: None,
            glow_split: None,
            screen_filter: None,
        };
        let white = image::RgbaImage::from_pixel(4, 4, image::Rgba([255; 4]));
        renderer.white = renderer.add_image(&white).expect("4×4 fits").sub(1, 1, 2, 2);
        Ok(renderer)
    }

    fn make_instance_buffer(device: &wgpu::Device, capacity: usize) -> wgpu::Buffer {
        Self::make_buffer(device, "instances", capacity * std::mem::size_of::<Instance>())
    }

    fn make_buffer(device: &wgpu::Device, label: &str, bytes: usize) -> wgpu::Buffer {
        device.create_buffer(&wgpu::BufferDescriptor {
            label: Some(label),
            size: bytes as u64,
            usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        })
    }

    pub fn resize(&mut self, width: u32, height: u32) {
        if width == 0 || height == 0 {
            return;
        }
        self.config.width = width;
        self.config.height = height;
        self.surface.configure(&self.device, &self.config);
    }

    /// Wait for the display's refresh (vsync), or draw as fast as possible (where the system
    /// can; otherwise it stays in step with the display).
    pub fn set_vsync(&mut self, on: bool) {
        let mode = if on { wgpu::PresentMode::AutoVsync } else { wgpu::PresentMode::AutoNoVsync };
        if self.config.present_mode != mode {
            self.config.present_mode = mode;
            self.surface.configure(&self.device, &self.config);
        }
    }

    /// The glow and colours the world is drawn with from the next frame.
    pub fn set_look(&mut self, look: Look) {
        self.look = look;
    }

    /// Whether this display can show HDR (Windows' HDR on, on DirectX 12 or Vulkan).
    pub fn hdr_supported(&self) -> bool {
        self.hdr_supported
    }

    /// Draw in HDR (scRGB) or not, from the next frame. Returns whether it's on: never where
    /// the surface doesn't offer it.
    pub fn set_hdr(&mut self, hdr: Option<Hdr>) -> bool {
        let hdr = hdr.filter(|_| self.hdr_supported);
        let (format, usage) = match hdr {
            Some(_) => (wgpu::TextureFormat::Rgba16Float, wgpu::TextureUsages::RENDER_ATTACHMENT),
            None if self.can_capture => (self.sdr_format, wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC),
            None => (self.sdr_format, wgpu::TextureUsages::RENDER_ATTACHMENT),
        };
        self.hdr = hdr;
        if self.config.format != format {
            log::info!("HDR {}", if hdr.is_some() { "on" } else { "off" });
            self.config.format = format;
            self.config.usage = usage;
            self.surface.configure(&self.device, &self.config);
        }
        hdr.is_some()
    }

    pub fn size(&self) -> (u32, u32) {
        (self.config.width, self.config.height)
    }

    /// The largest image `add_image` takes.
    pub fn max_image_size(&self) -> u32 {
        self.atlas.size - 1
    }

    /// A 2×2 white block, for solid rectangles (tint it with `Tint::color`).
    pub fn white(&self) -> AtlasRect {
        self.white
    }

    /// Copy an image (straight alpha) into the atlas. `None` if it's bigger than `max_image_size`.
    pub fn add_image(&mut self, image: &image::RgbaImage) -> Option<AtlasRect> {
        let (w, h) = image.dimensions();
        let device = &self.device;
        let rect = self.atlas.allocate(w, h, |size| new_page(device, size))?;
        let mut pixels = image.as_raw().clone();
        for px in pixels.chunks_exact_mut(4) {
            let a = px[3] as u16;
            for c in &mut px[..3] {
                *c = ((*c as u16 * a + 127) / 255) as u8;
            }
        }
        self.queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &self.atlas.pages[rect.page as usize].texture,
                mip_level: 0,
                origin: wgpu::Origin3d { x: rect.x as u32, y: rect.y as u32, z: 0 },
                aspect: wgpu::TextureAspect::All,
            },
            &pixels,
            wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(4 * w), rows_per_image: Some(h) },
            wgpu::Extent3d { width: w, height: h, depth_or_array_layers: 1 },
        );
        Some(rect)
    }

    /// Quads and triangle vertices queued this frame.
    pub fn queued(&self) -> (usize, usize) {
        (self.instances.len(), self.tri_vertices.len())
    }

    pub fn atlas_pages(&self) -> usize {
        self.atlas.pages.len()
    }

    /// Start a frame.
    pub fn begin(&mut self) {
        self.instances.clear();
        self.tri_vertices.clear();
        self.runs.clear();
        self.split = None;
        self.glow_split = None;
        self.screen_filter = None;
    }

    /// Everything drawn so far this frame is the world: draw it through `filter` (if any). What
    /// comes after (the HUD) is drawn plainly.
    /// Everything drawn so far this frame is the world's light and things: the glow comes from
    /// these only. What's drawn after, until `world_done`, is still the world (its filter and
    /// colours) but never glows: names, damage numbers, health bars.
    pub fn glow_done(&mut self) {
        self.glow_split = Some(self.runs.len());
    }

    pub fn world_done(&mut self, filter: Option<ScreenFilter>) {
        self.split = Some(self.runs.len());
        self.screen_filter = filter;
    }

    fn extend_run(&mut self, kind: Kind, page: u16, start: u32, end: u32) {
        let group = page / GROUP as u16;
        let at_split = self.split == Some(self.runs.len()) || self.glow_split == Some(self.runs.len());
        match self.runs.last_mut() {
            Some(run) if !at_split && run.kind == kind && run.group == group && run.end == start => run.end = end,
            _ => self.runs.push(Run { kind, group, start, end }),
        }
    }

    /// The page bindings, made again when pages were added.
    fn update_groups(&mut self) {
        let pages = self.atlas.pages.len();
        if pages == self.groups_pages {
            return;
        }
        self.groups = (0..pages.div_ceil(GROUP))
            .map(|g| {
                let view = |i: usize| &self.atlas.pages[(g * GROUP + i).min(pages - 1)].view;
                let mut entries: Vec<wgpu::BindGroupEntry> = (0..GROUP)
                    .map(|i| wgpu::BindGroupEntry { binding: i as u32, resource: wgpu::BindingResource::TextureView(view(i)) })
                    .collect();
                entries.push(wgpu::BindGroupEntry { binding: GROUP as u32, resource: wgpu::BindingResource::Sampler(&self.sampler) });
                self.device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("atlas pages"), layout: &self.page_layout, entries: &entries })
            })
            .collect();
        self.groups_pages = pages;
    }

    /// A textured triangle: screen points, and texture coordinates within `src` (0..1).
    pub fn triangle(&mut self, src: AtlasRect, pts: [[f32; 2]; 3], uvs: [[f32; 2]; 3], tint: Tint) {
        self.push_triangle(src, pts, uvs, tint, false);
    }

    /// Like `triangle`, with the texture repeating outside 0..1 (Flash's `repeat` fills).
    pub fn triangle_wrapped(&mut self, src: AtlasRect, pts: [[f32; 2]; 3], uvs: [[f32; 2]; 3], tint: Tint) {
        self.push_triangle(src, pts, uvs, tint, true);
    }

    fn push_triangle(&mut self, src: AtlasRect, pts: [[f32; 2]; 3], uvs: [[f32; 2]; 3], tint: Tint, wrap: bool) {
        self.push_triangle_muls(src, pts, uvs, [tint.mul; 3], wrap);
    }

    /// A triangle whose colour (RGBA multiplier) differs at each corner, blended across it:
    /// gradients, drawn with `white()`.
    pub fn triangle_colors(&mut self, src: AtlasRect, pts: [[f32; 2]; 3], uvs: [[f32; 2]; 3], muls: [[f32; 4]; 3]) {
        self.push_triangle_muls(src, pts, uvs, muls, false);
    }

    fn push_triangle_muls(&mut self, src: AtlasRect, pts: [[f32; 2]; 3], uvs: [[f32; 2]; 3], muls: [[f32; 4]; 3], wrap: bool) {
        let inv = 1.0 / self.atlas.size as f32;
        let rect = [src.x as f32 * inv, src.y as f32 * inv, src.w as f32 * inv, src.h as f32 * inv];
        let start = self.tri_vertices.len() as u32;
        for ((p, uv), mul) in pts.iter().zip(uvs).zip(muls) {
            self.tri_vertices.push(TriVertex { pos: *p, uv, rect, mul, wrap: if wrap { 1.0 } else { 0.0 }, page: (src.page as usize % GROUP) as u32 });
        }
        self.extend_run(Kind::Triangles, src.page, start, start + 3);
    }

    /// Draw `src` (an atlas rectangle) onto `dst`.
    pub fn sprite(&mut self, src: AtlasRect, dst: Quad, tint: Tint) {
        self.sprite_part(src, [0.0, 0.0, 1.0, 1.0], dst, tint);
    }

    /// Draw part of `src` onto `dst`: `part` is (u0, v0, u1, v1) within the rectangle, 0..1.
    pub fn sprite_part(&mut self, src: AtlasRect, part: [f32; 4], dst: Quad, tint: Tint) {
        self.sprite_filtered(src, part, dst, tint, Filter::None);
    }

    /// `sprite_part` through a colour filter.
    pub fn sprite_filtered(&mut self, src: AtlasRect, part: [f32; 4], dst: Quad, tint: Tint, filter: Filter) {
        let inv = 1.0 / self.atlas.size as f32;
        let (x, y, w, h) = (src.x as f32, src.y as f32, src.w as f32, src.h as f32);
        let uv = [
            (x + part[0] * w) * inv,
            (y + part[1] * h) * inv,
            (x + part[2] * w) * inv,
            (y + part[3] * h) * inv,
        ];
        self.instances.push(Instance {
            axes: [dst.x_axis[0], dst.x_axis[1], dst.y_axis[0], dst.y_axis[1]],
            origin: dst.origin,
            uv,
            mul: tint.mul,
            add: [tint.add[0], tint.add[1], tint.add[2], match filter {
                Filter::None => 0.0,
                Filter::Grey => 1.0,
                Filter::Red => 2.0,
            }],
            page: (src.page as usize % GROUP) as u32,
        });
        let end = self.instances.len() as u32;
        self.extend_run(Kind::Quads, src.page, end - 1, end);
    }

    /// A solid rectangle.
    pub fn fill(&mut self, x: f32, y: f32, w: f32, h: f32, rgb: u32, alpha: f32) {
        self.sprite(self.white, Quad::rect(x, y, w, h), Tint::color(rgb, alpha));
    }

    /// Send the frame to the GPU and show it.
    pub fn present(&mut self, clear_rgb: u32) -> Result<(), wgpu::SurfaceError> {
        let frame = match self.surface.get_current_texture() {
            Ok(frame) => frame,
            Err(wgpu::SurfaceError::Lost | wgpu::SurfaceError::Outdated) => {
                self.surface.configure(&self.device, &self.config);
                self.surface.get_current_texture()?
            }
            Err(e) => return Err(e),
        };
        let view = frame.texture.create_view(&Default::default());

        let globals = Globals { screen: [self.config.width as f32, self.config.height as f32], _pad: [0.0; 2] };
        self.queue.write_buffer(&self.globals, 0, bytemuck::bytes_of(&globals));
        if self.instances.len() > self.instance_capacity {
            self.instance_capacity = self.instances.len().next_power_of_two();
            self.instance_buffer = Self::make_instance_buffer(&self.device, self.instance_capacity);
        }
        if !self.instances.is_empty() {
            self.queue.write_buffer(&self.instance_buffer, 0, bytemuck::cast_slice(&self.instances));
        }
        if self.tri_vertices.len() > self.tri_capacity {
            self.tri_capacity = self.tri_vertices.len().next_power_of_two();
            self.tri_buffer =
                Self::make_buffer(&self.device, "triangles", self.tri_capacity * std::mem::size_of::<TriVertex>());
        }
        if !self.tri_vertices.is_empty() {
            self.queue.write_buffer(&self.tri_buffer, 0, bytemuck::cast_slice(&self.tri_vertices));
        }

        self.update_groups();
        let mut encoder = self.device.create_command_encoder(&Default::default());
        let [r, g, b] = rgb_f32(clear_rgb);
        let clear = wgpu::Color { r: r as f64, g: g as f64, b: b as f64, a: 1.0 };
        let filtered = match (self.split, self.screen_filter) {
            (Some(split), filter) if filter.is_some() || self.look.on() => Some((split.min(self.runs.len()), filter)),
            _ => None,
        };
        if filtered.is_some() || self.hdr.is_some() {
            self.prepare_offscreen();
        }
        let (mode, radius) = match filtered.and_then(|f| f.1) {
            None => (0.0, 0.0),
            Some(ScreenFilter::Grey) => (1.0, 0.0),
            Some(ScreenFilter::Blind) => (2.0, 0.0),
            Some(ScreenFilter::Blur(r)) => (3.0, r),
        };
        let (w, h) = (self.config.width as f32, self.config.height as f32);
        let texel = [1.0 / w, 1.0 / h];
        let (paper, headroom) = self.hdr.map_or((0.0, 0.0), |h| (h.paper, h.headroom));
        // Without the world (the menus) there's nothing to glow.
        let glow = if self.split.is_some() { self.look.glow } else { 0.0 };
        let params = PostParams { mode, radius, texel, dir: [0.0; 2], glow, vivid: self.look.vivid, paper, headroom, _pad: [0.0; 2] };
        self.queue.write_buffer(&self.post_params, 0, bytemuck::bytes_of(&params));
        // With HDR the frame goes to `frame` first, then through `fs_hdr` to the screen.
        let target = match (&self.offscreen, self.hdr) {
            (Some(off), Some(_)) => &off.frame_view,
            _ => &view,
        };
        if let Some((split, _)) = filtered {
            let off = self.offscreen.as_ref().expect("prepared");
            let glowing = if self.look.glow > 0.0 { self.glow_split.map_or(split, |g| g.min(split)) } else { split };
            {
                let mut pass = begin_pass(&mut encoder, &off.view, wgpu::LoadOp::Clear(clear));
                self.draw_runs(&mut pass, &self.runs[..glowing]);
            }
            if self.look.glow > 0.0 {
                let (bw, bh) = (bloom_size(off.width) as f32, bloom_size(off.height) as f32);
                for (buffer, dir) in [(&self.blur_h_params, [1.0 / bw, 0.0]), (&self.blur_v_params, [0.0, 1.0 / bh])] {
                    let p = PostParams { dir, ..params };
                    self.queue.write_buffer(buffer, 0, bytemuck::bytes_of(&p));
                }
                let black = wgpu::LoadOp::Clear(wgpu::Color::BLACK);
                for (pipeline, group, target) in [
                    (&self.bright_pipeline, &off.bright_group, &off.bloom_a),
                    (&self.blur_pipeline, &off.blur_h_group, &off.bloom_b),
                    (&self.blur_pipeline, &off.blur_v_group, &off.bloom_c),
                ] {
                    let mut pass = begin_pass(&mut encoder, target, black);
                    pass.set_pipeline(pipeline);
                    pass.set_bind_group(0, group, &[]);
                    pass.draw(0..3, 0..1);
                }
            }
            if glowing < split {
                let mut pass = begin_pass(&mut encoder, &off.view, wgpu::LoadOp::Load);
                self.draw_runs(&mut pass, &self.runs[glowing..split]);
            }
            let mut pass = begin_pass(&mut encoder, target, wgpu::LoadOp::Clear(clear));
            pass.set_pipeline(&self.post_pipeline);
            pass.set_bind_group(0, &off.bind_group, &[]);
            pass.draw(0..3, 0..1);
            self.draw_runs(&mut pass, &self.runs[split..]);
        } else {
            let mut pass = begin_pass(&mut encoder, target, wgpu::LoadOp::Clear(clear));
            self.draw_runs(&mut pass, &self.runs);
        }
        let hdr_frame = match (&self.offscreen, self.hdr) {
            (Some(off), Some(_)) => {
                let mut pass = begin_pass(&mut encoder, &view, wgpu::LoadOp::Clear(wgpu::Color::BLACK));
                pass.set_pipeline(&self.hdr_pipeline);
                pass.set_bind_group(0, &off.hdr_group, &[]);
                pass.draw(0..3, 0..1);
                Some(&off.frame)
            }
            _ => None,
        };
        // Screenshots: the frame as SDR draws it (an HDR surface can't be read back as 8 bits).
        let readback = match hdr_frame {
            Some(texture) => self.capture_next.then(|| self.copy_out(&mut encoder, texture)),
            None => (self.capture_next && self.can_capture).then(|| self.copy_out(&mut encoder, &frame.texture)),
        };
        self.queue.submit([encoder.finish()]);
        if let Some((buffer, padded_row)) = readback {
            self.captured = self.read_back(&buffer, padded_row);
            self.capture_next = false;
        }
        frame.present();
        self.last_draws = self.runs.len() as u32;
        Ok(())
    }

    fn draw_runs(&self, pass: &mut wgpu::RenderPass, runs: &[Run]) {
        pass.set_bind_group(0, &self.globals_group, &[]);
        let mut bound: Option<Kind> = None;
        let mut bound_group: Option<u16> = None;
        for run in runs {
            if bound != Some(run.kind) {
                match run.kind {
                    Kind::Quads => {
                        pass.set_pipeline(&self.pipeline);
                        pass.set_vertex_buffer(0, self.instance_buffer.slice(..));
                    }
                    Kind::Triangles => {
                        pass.set_pipeline(&self.tri_pipeline);
                        pass.set_vertex_buffer(0, self.tri_buffer.slice(..));
                    }
                }
                bound = Some(run.kind);
            }
            if bound_group != Some(run.group) {
                pass.set_bind_group(1, &self.groups[run.group as usize], &[]);
                bound_group = Some(run.group);
            }
            match run.kind {
                Kind::Quads => pass.draw(0..6, run.start..run.end),
                Kind::Triangles => pass.draw(run.start..run.end, 0..1),
            }
        }
    }

    /// The texture the world is drawn into for a screen filter, the window's size.
    fn prepare_offscreen(&mut self) {
        let (width, height) = (self.config.width, self.config.height);
        if self.offscreen.as_ref().is_some_and(|o| o.width == width && o.height == height) {
            return;
        }
        let target = |label, width, height| {
            self.device
                .create_texture(&wgpu::TextureDescriptor {
                    label: Some(label),
                    size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
                    mip_level_count: 1,
                    sample_count: 1,
                    dimension: wgpu::TextureDimension::D2,
                    format: self.sdr_format,
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                        | wgpu::TextureUsages::TEXTURE_BINDING
                        | wgpu::TextureUsages::COPY_SRC,
                    view_formats: &[],
                })
        };
        let view = target("world", width, height).create_view(&Default::default());
        let bloom_a = target("glow", bloom_size(width), bloom_size(height)).create_view(&Default::default());
        let bloom_b = target("glow across", bloom_size(width), bloom_size(height)).create_view(&Default::default());
        let bloom_c = target("wide glow", bloom_size(width), bloom_size(height)).create_view(&Default::default());
        let frame = target("frame", width, height);
        let frame_view = frame.create_view(&Default::default());
        let group = |label, scene: &wgpu::TextureView, bloom: &wgpu::TextureView, tight: &wgpu::TextureView, params: &wgpu::Buffer| {
            self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some(label),
                layout: &self.post_layout,
                entries: &[
                    wgpu::BindGroupEntry { binding: 0, resource: wgpu::BindingResource::TextureView(scene) },
                    wgpu::BindGroupEntry { binding: 1, resource: wgpu::BindingResource::Sampler(&self.post_sampler) },
                    wgpu::BindGroupEntry { binding: 2, resource: params.as_entire_binding() },
                    wgpu::BindGroupEntry { binding: 3, resource: wgpu::BindingResource::TextureView(bloom) },
                    wgpu::BindGroupEntry { binding: 4, resource: wgpu::BindingResource::TextureView(tight) },
                ],
            })
        };
        let bind_group = group("world", &view, &bloom_c, &bloom_a, &self.post_params);
        let bright_group = group("bright", &view, &view, &view, &self.post_params);
        let blur_h_group = group("blur across", &bloom_a, &bloom_a, &bloom_a, &self.blur_h_params);
        let blur_v_group = group("blur down", &bloom_b, &bloom_b, &bloom_b, &self.blur_v_params);
        let hdr_group = group("hdr", &frame_view, &bloom_c, &bloom_a, &self.post_params);
        self.offscreen = Some(Offscreen {
            width,
            height,
            view,
            bind_group,
            bloom_a,
            bloom_b,
            bloom_c,
            bright_group,
            blur_h_group,
            blur_v_group,
            frame,
            frame_view,
            hdr_group,
        });
    }

    /// Ask for the next frame's pixels (desktop only); `take_capture` returns them after
    /// `present`. Returns false if this surface can't be read back.
    pub fn capture_next_frame(&mut self) -> bool {
        // With HDR the frame is read from `frame`, which always can be.
        self.capture_next = self.can_capture || self.hdr.is_some();
        self.capture_next
    }

    pub fn take_capture(&mut self) -> Option<image::RgbaImage> {
        self.captured.take()
    }

    fn copy_out(&self, encoder: &mut wgpu::CommandEncoder, texture: &wgpu::Texture) -> (wgpu::Buffer, u32) {
        let (w, h) = (self.config.width, self.config.height);
        let padded_row = (4 * w).div_ceil(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT) * wgpu::COPY_BYTES_PER_ROW_ALIGNMENT;
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("capture"),
            size: (padded_row * h) as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        encoder.copy_texture_to_buffer(
            texture.as_image_copy(),
            wgpu::TexelCopyBufferInfo {
                buffer: &buffer,
                layout: wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(padded_row), rows_per_image: Some(h) },
            },
            wgpu::Extent3d { width: w, height: h, depth_or_array_layers: 1 },
        );
        (buffer, padded_row)
    }

    fn read_back(&self, buffer: &wgpu::Buffer, padded_row: u32) -> Option<image::RgbaImage> {
        let slice = buffer.slice(..);
        slice.map_async(wgpu::MapMode::Read, |_| {});
        self.device.poll(wgpu::Maintain::Wait);
        let (w, h) = (self.config.width, self.config.height);
        let data = slice.get_mapped_range();
        let mut out = image::RgbaImage::new(w, h);
        let bgra = self.sdr_format == wgpu::TextureFormat::Bgra8Unorm;
        for y in 0..h {
            let row = &data[(y * padded_row) as usize..][..(4 * w) as usize];
            for x in 0..w {
                let p = &row[(4 * x) as usize..][..4];
                let rgba = if bgra { [p[2], p[1], p[0], 255] } else { [p[0], p[1], p[2], 255] };
                out.put_pixel(x, y, image::Rgba(rgba));
            }
        }
        Some(out)
    }
}

/// The glow's textures are a quarter of the window's size each way.
fn bloom_size(n: u32) -> u32 {
    n.div_ceil(4).max(1)
}

fn begin_pass<'a>(encoder: &'a mut wgpu::CommandEncoder, view: &'a wgpu::TextureView, load: wgpu::LoadOp<wgpu::Color>) -> wgpu::RenderPass<'a> {
    encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        label: Some("frame"),
        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
            view,
            resolve_target: None,
            ops: wgpu::Operations { load, store: wgpu::StoreOp::Store },
        })],
        depth_stencil_attachment: None,
        timestamp_writes: None,
        occlusion_query_set: None,
    })
}

/// The page binding's entries: GROUP textures, then the sampler.
fn page_entries() -> Vec<wgpu::BindGroupLayoutEntry> {
    let mut entries: Vec<wgpu::BindGroupLayoutEntry> = (0..GROUP as u32)
        .map(|binding| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Texture {
                sample_type: wgpu::TextureSampleType::Float { filterable: true },
                view_dimension: wgpu::TextureViewDimension::D2,
                multisampled: false,
            },
            count: None,
        })
        .collect();
    entries.push(wgpu::BindGroupLayoutEntry {
        binding: GROUP as u32,
        visibility: wgpu::ShaderStages::FRAGMENT,
        ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
        count: None,
    });
    entries
}

fn new_page(device: &wgpu::Device, size: u32) -> Page {
    let texture = device.create_texture(&wgpu::TextureDescriptor {
        label: Some("atlas page"),
        size: wgpu::Extent3d { width: size, height: size, depth_or_array_layers: 1 },
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format: wgpu::TextureFormat::Rgba8Unorm,
        usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
        view_formats: &[],
    });
    Page::new(texture)
}
