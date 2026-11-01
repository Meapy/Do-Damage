// The world drawn through a filter, as the AIR client's Stage3D post effects do: grey while
// paused, nearly black while blind, blurred (swaying) while drunk. One triangle covers the screen.
// The look (Options, Graphics) goes on top: a glow round bright light (`fs_bright` picks it out
// at a quarter of the size, `fs_blur` spreads it, twice), and vivid colours with a vignette.
// With HDR on, `fs_hdr` turns the finished frame into scRGB for the display, the glow brighter
// than white.

struct Post {
    // 0 none, 1 grey, 2 blind, 3 blur.
    mode: f32,
    // The blur's radius, in pixels.
    radius: f32,
    texel: vec2<f32>,
    // `fs_blur`'s step, in texels of what it reads.
    dir: vec2<f32>,
    // How strong the glow is, and the vivid colours (0: off).
    glow: f32,
    vivid: f32,
    // HDR: SDR white in scRGB units (0: SDR), and how far past it the glow reaches.
    paper: f32,
    headroom: f32,
    _pad: vec2<f32>,
};

@group(0) @binding(0) var scene: texture_2d<f32>;
@group(0) @binding(1) var scene_sampler: sampler;
@group(0) @binding(2) var<uniform> post: Post;
@group(0) @binding(3) var bloom: texture_2d<f32>;
@group(0) @binding(4) var tight: texture_2d<f32>;

// The glow at a pixel: a tight halo that hugs small bright things (shots, sparks), over a wide
// soft one that big ones (lava, fire) cast round them.
fn glow_at(uv: vec2<f32>) -> vec3<f32> {
    let near = textureSampleLevel(tight, scene_sampler, uv, 0.0).rgb;
    let wide = textureSampleLevel(bloom, scene_sampler, uv, 0.0).rgb;
    return (near * 0.9 + wide * 0.7) * post.glow;
}

struct VertexOut {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vi: u32) -> VertexOut {
    let x = f32((vi << 1u) & 2u);
    let y = f32(vi & 2u);
    var out: VertexOut;
    out.position = vec4<f32>(x * 2.0 - 1.0, 1.0 - y * 2.0, 0.0, 1.0);
    out.uv = vec2<f32>(x, y);
    return out;
}

fn filtered(uv: vec2<f32>) -> vec3<f32> {
    if (post.mode > 2.5) {
        // A 7 × 7 spread of samples over the radius.
        var sum = vec4<f32>(0.0);
        for (var i = -3; i <= 3; i++) {
            for (var j = -3; j <= 3; j++) {
                let d = vec2<f32>(f32(i), f32(j)) / 3.0 * post.radius * post.texel;
                sum += textureSampleLevel(scene, scene_sampler, uv + d, 0.0);
            }
        }
        return (sum / 49.0).rgb;
    }
    let c = textureSampleLevel(scene, scene_sampler, uv, 0.0).rgb;
    if (post.mode > 1.5) {
        // Map.BLIND_FILTER: each channel 0.05 × (r + g + b).
        return vec3<f32>(0.05 * (c.r + c.g + c.b));
    }
    if (post.mode > 0.5) {
        // MoreColorUtil.greyscaleFilterMatrix.
        return vec3<f32>(dot(c, vec3<f32>(0.3, 0.59, 0.11)));
    }
    return c;
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4<f32> {
    var c = filtered(in.uv);
    if (post.glow > 0.0) {
        // Screen blend: bright light spills onto what's round it without going past white.
        let b = glow_at(in.uv);
        c = 1.0 - (1.0 - c) * (1.0 - min(b, vec3<f32>(1.0)));
    }
    if (post.vivid > 0.0) {
        // Vibrance: muted colours gain a little saturation; greys and strong colours hardly any.
        let l = dot(c, vec3<f32>(0.299, 0.587, 0.114));
        let sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
        c = mix(vec3<f32>(l), c, 1.0 + 1.2 * post.vivid * sat * (1.0 - sat));
        // A light S curve for contrast.
        c = clamp(c, vec3<f32>(0.0), vec3<f32>(1.0));
        c = mix(c, c * c * (3.0 - 2.0 * c), 0.12 * post.vivid);
        // The far corners a touch darker.
        let d = (in.uv - 0.5) * vec2<f32>(1.0, post.texel.x / post.texel.y);
        c *= 1.0 - 0.18 * post.vivid * smoothstep(0.45, 0.85, length(d) * 1.2);
    }
    // With HDR the world leaves alpha 0, so `fs_hdr` knows where the interface covers it.
    return vec4<f32>(c, select(1.0, 0.0, post.paper > 0.0));
}

// What glows: light, bright in every channel it has, such as lava, fire, magic and shots. Its
// brightness is half its strongest channel and half its luminance, so a strong red or blue wall
// (one channel) stays as it is, and so does white sand or stone (no colour). Averaged over a
// 4 × 4 block with four filtered samples.
fn bright(uv: vec2<f32>) -> vec3<f32> {
    let c = textureSampleLevel(scene, scene_sampler, uv, 0.0).rgb;
    let hi = max(c.r, max(c.g, c.b));
    let key = 0.5 * hi + 0.5 * dot(c, vec3<f32>(0.299, 0.587, 0.114));
    let sat = (hi - min(c.r, min(c.g, c.b))) / max(hi, 0.001);
    return c * smoothstep(0.7, 0.92, key) * (0.3 + 0.7 * sat);
}

@fragment
fn fs_bright(in: VertexOut) -> @location(0) vec4<f32> {
    let t = post.texel;
    let a = bright(in.uv + vec2<f32>(-t.x, -t.y));
    let b = bright(in.uv + vec2<f32>(t.x, -t.y));
    let c = bright(in.uv + vec2<f32>(-t.x, t.y));
    let d = bright(in.uv + vec2<f32>(t.x, t.y));
    // Half the average, half the brightest: a spark a pixel or two wide keeps its light, where
    // averaging alone would thin it sixteen times; a wide area (lava) is the same either way.
    return vec4<f32>((a + b + c + d) / 8.0 + max(max(a, b), max(c, d)) * 0.5, 1.0);
}

fn tap(uv: vec2<f32>) -> vec3<f32> {
    return textureSampleLevel(scene, scene_sampler, uv, 0.0).rgb;
}

// Five filtered samples along the step, each pair two texels apart: a wide Gaussian. (Written
// out: loops over a constant array crashed AMD's Vulkan driver.)
@fragment
fn fs_blur(in: VertexOut) -> @location(0) vec4<f32> {
    let d1 = post.dir * 2.7692;
    let d2 = post.dir * 6.4616;
    let sum = tap(in.uv) * 0.2270 + (tap(in.uv + d1) + tap(in.uv - d1)) * 0.3162
        + (tap(in.uv + d2) + tap(in.uv - d2)) * 0.0703;
    return vec4<f32>(sum, 1.0);
}

fn to_linear(c: vec3<f32>) -> vec3<f32> {
    return select(pow((c + 0.055) / 1.055, vec3<f32>(2.4)), c / 12.92, c <= vec3<f32>(0.04045));
}

// The finished frame (world and interface, as SDR draws them) into scRGB at Windows' SDR white,
// and where the world shows, the glow again as light brighter than white.
@fragment
fn fs_hdr(in: VertexOut) -> @location(0) vec4<f32> {
    let f = textureSampleLevel(scene, scene_sampler, in.uv, 0.0);
    var c = to_linear(f.rgb) * post.paper;
    if (post.glow > 0.0) {
        let b = glow_at(in.uv);
        c += to_linear(min(b, vec3<f32>(1.0))) * post.paper * post.headroom * (1.0 - f.a);
    }
    return vec4<f32>(c, 1.0);
}
