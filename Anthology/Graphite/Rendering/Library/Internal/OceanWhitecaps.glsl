// The ocean's whitecaps in world space on the depth's slices, a foam simulation: each step the foam fades, then
// rises to the crests under it, max(foam * (1 - fade dt), crest), crests where the surface folds,
// 4 strength (coverage - J). J is the Jacobian of the waves at the texel's undisplaced point plus the ripples' own,
// their chop pulling the water toward their crests, ripple foam times the Laplacian of their height. A step reads the last state where the slices sat then, so
// the foam stays still in the world as the viewer moves and lingers behind the crests that made it. A step with no
// time only moves the state across, which lands it back in the first texture.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(r16f) uniform readonly image2DArray Ocean_WhitecapSource;
    layout(r16f) uniform writeonly image2DArray Ocean_WhitecapTarget;
    uniform sampler2DArray Ocean_Slopes;
    uniform sampler2DArray Ocean_Ripples;

    layout(push_constant) uniform Push
    {
        vec4 sizes;             // each cascade's tile size in metres
        vec4 depth;             // the depth viewer's xz modulo the largest depth texel, the first slice's width, resolution
        vec4 shore;             // shallow attenuation, depth slices, ripple slices, cascade resolution
        vec4 tile;              // the depth viewer's xz on the largest tile, and its xz modulo the largest texel when the source was stepped
        vec4 moved;             // how far the viewer moved since the source was stepped, in xz, then the ripples' chop
        vec4 rule;              // seconds stepped, coverage, strength, fade
    } push;

    #include "Graphite/Internal/OceanWater.glsl"

    float Source(ivec2 texel, int slice)
    {
        int resolution = int(push.depth.w);
        if (any(lessThan(texel, ivec2(0))) || any(greaterThanEqual(texel, ivec2(resolution))))
            return 0.0;
        return imageLoad(Ocean_WhitecapSource, ivec3(texel, slice)).r;
    }

    // The source between texels, bilinear
    float SourceAt(vec2 texel, int slice)
    {
        vec2 corner = floor(texel), f = texel - corner;
        ivec2 c = ivec2(corner);
        return mix(mix(Source(c, slice), Source(c + ivec2(1, 0), slice), f.x), mix(Source(c + ivec2(0, 1), slice), Source(c + ivec2(1, 1), slice), f.x), f.y);
    }

    // How the ripples curve at a position relative to the depth viewer, the Laplacian of their height, every ripple
    // slice holding it added together
    float RippleCurvature(vec2 position)
    {
        float curvature = 0.0;
        float step = 1.0 / push.depth.w;
        for (int slice = 0; slice < int(push.shore.z); slice++)
        {
            float width = push.depth.z * exp2(float(slice));
            float reach = max(abs(position.x), abs(position.y)) / (0.5 * width);
            float weight = 1.0 - smoothstep(0.8, 0.95, reach);
            if (weight <= 0.0)
                continue;
            float texel = width / push.depth.w;
            vec3 at = vec3((position + mod(push.depth.xy, texel)) / width + 0.5, float(slice));
            float around = textureLod(Ocean_Ripples, at + vec3(step, 0.0, 0.0), 0.0).x + textureLod(Ocean_Ripples, at - vec3(step, 0.0, 0.0), 0.0).x
                + textureLod(Ocean_Ripples, at + vec3(0.0, step, 0.0), 0.0).x + textureLod(Ocean_Ripples, at - vec3(0.0, step, 0.0), 0.0).x;
            curvature += (around - 4.0 * textureLod(Ocean_Ripples, at, 0.0).x) / (texel * texel) * weight;
        }
        return curvature;
    }

    void main()
    {
        int resolution = int(push.depth.w);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (id.x >= resolution || id.y >= resolution || id.z >= int(push.shore.y))
            return;
        float texel = push.depth.z * exp2(float(id.z)) / float(resolution);
        vec2 position = (vec2(id.xy) + 0.5 - 0.5 * float(resolution)) * texel - mod(push.depth.xy, texel);
        vec2 from = (position + push.moved.xy + mod(push.tile.zw, texel)) / texel + 0.5 * float(resolution) - 0.5;
        float foam = SourceAt(from, id.z);

        float dt = push.rule.x;
        if (dt > 0.0)
        {
            vec2 wave = push.tile.xy + position;
            vec4 slopes = OceanWater_Sample(Ocean_Slopes, wave, position, texel);
            float twist = OceanWater_Sample(Ocean_Displacement, wave, position, texel).w;
            float jacobian = (1.0 + slopes.z) * (1.0 + slopes.w) - twist * twist;
            jacobian += push.moved.z * RippleCurvature(position);
            float crest = clamp(4.0 * push.rule.z * (push.rule.y - jacobian), 0.0, 1.0);
            foam = max(foam * max(1.0 - push.rule.w * dt, 0.0), crest);
        }
        imageStore(Ocean_WhitecapTarget, id, vec4(clamp(foam, 0.0, 1.0)));
    }
}
