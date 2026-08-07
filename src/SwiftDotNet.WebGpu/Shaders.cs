namespace SwiftDotNet;

/// <summary>
/// The WGSL source for the single pipeline that draws the entire UI.
/// </summary>
/// <remarks>
/// <para>Every primitive the engine's <c>ICanvas</c> exposes is one instanced quad whose fragment shader
/// evaluates a signed distance field. That is what makes a from-scratch GPU backend tractable for this
/// DSL: rounded rectangles, capsules, circles, borders and shadows are all the same two distance
/// functions, and antialiasing falls out of the distance's screen-space derivative rather than needing
/// multisampling.</para>
///
/// <para>Branching is written with <c>select</c> rather than <c>if</c> around the shading maths because
/// the instance index is flat-interpolated: neighbouring fragments in a quad can belong to different
/// instances, so control flow is non-uniform and <c>fwidth</c> must be evaluated outside it.</para>
/// </remarks>
static class Shaders
{
    public const string Wgsl = """
    const KIND_FILL_RRECT   : i32 = 0;
    const KIND_STROKE_RRECT : i32 = 1;
    const KIND_FILL_ELLIPSE : i32 = 2;
    const KIND_STROKE_ELLIPSE: i32 = 3;
    const KIND_SHADOW       : i32 = 4;
    const KIND_GLYPH        : i32 = 5;
    const KIND_IMAGE        : i32 = 6;

    struct Instance {
        bounds   : vec4<f32>,   // local minX, minY, maxX, maxY
        shape    : vec4<f32>,   // corner radius, stroke width, shadow blur, kind
        color    : vec4<f32>,
        xform0   : vec4<f32>,   // a, b, c, d
        xform1   : vec4<f32>,   // tx, ty, first stop index, signed stop count (negative = radial)
        clip     : vec4<f32>,   // device-space minX, minY, maxX, maxY
        uv       : vec4<f32>,   // u0, v0, u1, v1
        gradient : vec4<f32>,   // linear: x0,y0,x1,y1   radial: cx,cy,radius,_
    };

    struct Stop {
        color : vec4<f32>,
        loc   : vec4<f32>,      // .x is the position; padded for storage alignment
    };

    struct Uniforms {
        viewport : vec4<f32>,   // width, height, _, _
    };

    @group(0) @binding(0) var<uniform>              u         : Uniforms;
    @group(0) @binding(1) var<storage, read>        instances : array<Instance>;
    @group(0) @binding(2) var<storage, read>        stops     : array<Stop>;

    @group(1) @binding(0) var atlasTex     : texture_2d<f32>;
    @group(1) @binding(1) var atlasSampler : sampler;

    @group(2) @binding(0) var imageTex     : texture_2d<f32>;
    @group(2) @binding(1) var imageSampler : sampler;

    struct VsOut {
        @builtin(position)             pos    : vec4<f32>,
        @location(0)                   local  : vec2<f32>,
        @location(1)                   screenPos : vec2<f32>,
        @location(2)                   uv     : vec2<f32>,
        @location(3) @interpolate(flat) idx   : u32,
    };

    @vertex
    fn vs(@builtin(vertex_index) vi : u32, @builtin(instance_index) ii : u32) -> VsOut {
        let inst = instances[ii];
        let kind = i32(inst.shape.w);

        var quad = array<vec2<f32>, 6>(
            vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 0.0), vec2<f32>(0.0, 1.0),
            vec2<f32>(1.0, 0.0), vec2<f32>(1.0, 1.0), vec2<f32>(0.0, 1.0));
        let t = quad[vi];

        // The quad must cover more than the shape: a shadow needs room for its falloff, a stroke sits
        // halfSize outside the path, and every edge needs a pixel of slack for the antialiasing ramp.
        var pad = 1.0;
        if (kind == KIND_SHADOW) {
            pad = inst.shape.z * 3.0 + 1.0;
        } else if (kind == KIND_STROKE_RRECT || kind == KIND_STROKE_ELLIPSE) {
            pad = inst.shape.y * 0.5 + 1.0;
        }

        let lo = inst.bounds.xy - vec2<f32>(pad);
        let hi = inst.bounds.zw + vec2<f32>(pad);
        let local = mix(lo, hi, t);

        let screenPos = vec2<f32>(
            inst.xform0.x * local.x + inst.xform0.z * local.y + inst.xform1.x,
            inst.xform0.y * local.x + inst.xform0.w * local.y + inst.xform1.y);

        var out : VsOut;
        out.pos = vec4<f32>(
            screenPos.x / u.viewport.x * 2.0 - 1.0,
            1.0 - screenPos.y / u.viewport.y * 2.0,
            0.0, 1.0);
        out.local  = local;
        out.screenPos = screenPos;
        out.uv     = mix(inst.uv.xy, inst.uv.zw, t);
        out.idx    = ii;
        return out;
    }

    // Exact distance to a rounded box centred at the origin.
    fn sdRoundRect(p : vec2<f32>, halfSize : vec2<f32>, r : f32) -> f32 {
        let q = abs(p) - halfSize + vec2<f32>(r);
        return min(max(q.x, q.y), 0.0) + length(max(q, vec2<f32>(0.0))) - r;
    }

    // Cheap ellipse approximation; the error is sub-pixel at UI scales and it avoids the iterative
    // exact solution, which is far more expensive per fragment than this whole shader.
    fn sdEllipse(p : vec2<f32>, halfSize : vec2<f32>) -> f32 {
        let h = max(halfSize, vec2<f32>(0.0001));
        let k0 = length(p / h);
        let k1 = length(p / (h * h));
        return select(k0 * (k0 - 1.0) / max(k1, 0.0001), -min(h.x, h.y), k1 == 0.0);
    }

    fn gradientColor(base : i32, count : i32, t : f32) -> vec4<f32> {
        if (count <= 1) {
            return stops[base].color;
        }

        var result = stops[base].color;                       // before the first stop
        for (var i = 0; i < count - 1; i = i + 1) {
            let s0 = stops[base + i];
            let s1 = stops[base + i + 1];
            let l0 = s0.loc.x;
            let l1 = s1.loc.x;
            let f  = clamp((t - l0) / max(l1 - l0, 0.0001), 0.0, 1.0);
            result = select(result, mix(s0.color, s1.color, f), t >= l0 && t <= l1);
        }

        let last = stops[base + count - 1];
        return select(result, last.color, t > last.loc.x);    // past the final stop
    }

    @fragment
    fn fs(frag : VsOut) -> @location(0) vec4<f32> {
        let inst = instances[frag.idx];

        // Clipping is per-instance rather than a scissor state so the whole frame stays one draw call.
        if (frag.screenPos.x < inst.clip.x || frag.screenPos.x > inst.clip.z ||
            frag.screenPos.y < inst.clip.y || frag.screenPos.y > inst.clip.w) {
            discard;
        }

        let kind   = i32(inst.shape.w);
        let center = (inst.bounds.xy + inst.bounds.zw) * 0.5;
        let halfSize   = (inst.bounds.zw - inst.bounds.xy) * 0.5;
        let p      = frag.local - center;

        // Both distance fields and the derivative are evaluated unconditionally: the instance index is
        // flat, so a branch on `kind` is non-uniform and fwidth would be undefined inside it.
        let dRect    = sdRoundRect(p, halfSize, inst.shape.x);
        let dEllipse = sdEllipse(p, halfSize);
        var d        = select(dRect, dEllipse, kind == KIND_FILL_ELLIPSE || kind == KIND_STROKE_ELLIPSE);
        d            = select(d, abs(d) - inst.shape.y * 0.5, kind == KIND_STROKE_RRECT || kind == KIND_STROKE_ELLIPSE);
        let aa       = max(fwidth(d), 0.0001);

        let atlas = textureSample(atlasTex, atlasSampler, frag.uv);
        let image = textureSample(imageTex, imageSampler, frag.uv);

        // Flat colour, or a gradient sampled in the shape's own local space so it rotates with it.
        let count = i32(abs(inst.xform1.w));
        let base  = i32(inst.xform1.z);
        let ab    = inst.gradient.zw - inst.gradient.xy;
        let tLin  = dot(frag.local - inst.gradient.xy, ab) / max(dot(ab, ab), 0.0001);
        let tRad  = length(frag.local - inst.gradient.xy) / max(inst.gradient.z, 0.0001);
        let tg    = clamp(select(tLin, tRad, inst.xform1.w < 0.0), 0.0, 1.0);
        let grad  = gradientColor(base, count, tg);

        var color = select(inst.color, vec4<f32>(grad.rgb, grad.a * inst.color.a), count > 0);
        color     = select(color, vec4<f32>(image.rgb, image.a * inst.color.a), kind == KIND_IMAGE);

        let shapeAlpha  = 1.0 - smoothstep(-aa, aa, d);
        let blur        = max(inst.shape.z, 0.5);
        let shadowAlpha = 1.0 - smoothstep(-blur, blur, dRect);

        var alpha = shapeAlpha;
        alpha = select(alpha, shadowAlpha, kind == KIND_SHADOW);
        alpha = select(alpha, atlas.r,     kind == KIND_GLYPH);
        alpha = select(alpha, 1.0,         kind == KIND_IMAGE);

        let a = color.a * alpha;
        if (a <= 0.0) {
            discard;
        }

        // Premultiplied: the pipeline blends with (One, OneMinusSrcAlpha).
        return vec4<f32>(color.rgb * a, a);
    }
    """;
}
