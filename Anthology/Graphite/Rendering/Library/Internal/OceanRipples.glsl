// The ocean's ripples, dynamic waves: the 2D wave equation, explicit and semi implicit, in the first depth
// slices around the viewer, on the same texels. Each texel keeps (height, vertical velocity, dh/dx, dh/dz), the
// slopes from the last step's heights. A step reads the last state where the slices sat then, a whole number of
// texels away, so the ripples stay still in the world as the viewer moves. Waves travel at the deep water speed of
// the shortest wave a slice holds, held under a Courant number of 0.7, damp over time, fade into a sponge at the
// slice's edge, die on land, decay in the shallows and drift with the current, read back along it each step. A force step pushes the water around a sphere moving through
// it, a band limited push, over the texels near it. Moving across the water it pushes a shallow trough under itself
// and a rise around it, so it sits a little low in the water with its wake standing up either side, balanced by a
// wide shallow dip so it adds no water. A sphere pushes only the finest slice holding all it pushes.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rgba16f) uniform readonly image2DArray Ocean_RippleSource;
    layout(rgba16f) uniform image2DArray Ocean_RippleTarget;
    layout(r32f) uniform readonly image2DArray Ocean_Depth;
    layout(r32f) uniform readonly image2DArray Ocean_Level;
    layout(rgba16f) uniform readonly image2DArray Ocean_Flow;

    layout(push_constant) uniform Push
    {
        vec4 slices;            // the first slice's width, resolution, mode (0 step, 1 force, 2 clear), a force's slice
        vec4 viewer;            // the viewer's xz modulo the largest texel now, and when the source was stepped
        vec4 step;              // seconds, gravity, damping, shallow attenuation
        vec4 sphere;            // a force's sphere centre relative to the viewer, radius
        vec4 motion;            // its velocity through the water, weight
        vec4 moved;             // how far the viewer moved since the source was stepped, in xz
    } push;

    const float PI = 3.14159265359;

    float Texel(int slice) { return push.slices.x * exp2(float(slice)) / push.slices.y; }

    vec2 Position(ivec2 texel, int slice, vec2 viewer)
    {
        float size = Texel(slice);
        return (vec2(texel) + 0.5 - 0.5 * push.slices.y) * size - mod(viewer, size);
    }

    vec4 Source(ivec2 texel, int slice)
    {
        int resolution = int(push.slices.y);
        if (any(lessThan(texel, ivec2(0))) || any(greaterThanEqual(texel, ivec2(resolution))))
            return vec4(0.0);
        return imageLoad(Ocean_RippleSource, ivec3(texel, slice));
    }

    // The source between texels, bilinear
    vec4 SourceAt(vec2 texel, int slice)
    {
        vec2 corner = floor(texel), f = texel - corner;
        ivec2 c = ivec2(corner);
        return mix(mix(Source(c, slice), Source(c + ivec2(1, 0), slice), f.x), mix(Source(c + ivec2(0, 1), slice), Source(c + ivec2(1, 1), slice), f.x), f.y);
    }

    // A band limited push, rising to a peak near ax = 1 and falling off fast past it
    float Push(float a, float x)
    {
        float ax = a * x;
        float ax2 = ax * ax;
        return ax / (1.0 + ax2 * ax2 * ax2);
    }

    void StepTexel(ivec3 id)
    {
        int slice = id.z;
        float size = Texel(slice);
        vec2 position = Position(id.xy, slice, push.viewer.xy);
        float dt = push.step.x;
        vec2 flow = imageLoad(Ocean_Flow, id).xy;
        vec2 from = (position + push.moved.xy - flow * dt + mod(push.viewer.zw, size)) / size + 0.5 * push.slices.y - 0.5;

        vec4 here = SourceAt(from, slice);
        float left = SourceAt(from - vec2(1.0, 0.0), slice).x, right = SourceAt(from + vec2(1.0, 0.0), slice).x;
        float down = SourceAt(from - vec2(0.0, 1.0), slice).x, up = SourceAt(from + vec2(0.0, 1.0), slice).x;

        float speed = sqrt(push.step.y * 2.0 * size / (2.0 * PI));
        if (dt > 0.0)
            speed = min(speed, 0.7 * size / dt);
        float velocity = here.y + dt * speed * speed / (size * size) * (left + right + down + up - 4.0 * here.x);
        velocity *= 1.0 - min(1.0, push.step.z * dt);
        float height = (here.x + dt * velocity) * (1.0 - min(1.0, push.step.z * dt));

        float depth = imageLoad(Ocean_Depth, id).r + imageLoad(Ocean_Level, id).r;
        float shallow = 1.0 - (1.0 - clamp(2.0 * depth / (2.0 * size), 0.0, 1.0)) * dt * 2.0;
        height *= mix(1.0, shallow, push.step.w);

        vec2 edge = min(vec2(id.xy) + 0.5, push.slices.y - vec2(id.xy) - 0.5) / push.slices.y;
        float sponge = mix(0.95, 1.0, clamp(min(edge.x, edge.y) / 0.1, 0.0, 1.0));
        if (depth <= 0.0)
            sponge = 0.0;

        vec2 slopes = vec2(right - left, up - down) / (2.0 * size);
        imageStore(Ocean_RippleTarget, id, vec4(height * sponge, velocity * sponge, slopes));
    }

    void ForceTexel(ivec3 id)
    {
        int slice = id.z;
        float size = Texel(slice);
        float shortest = 2.0 * size;
        vec2 position = Position(id.xy, slice, push.viewer.xy);
        vec2 toTexel = position - push.sphere.xz;
        float reach = length(toTexel);
        float signedDistance = reach - push.sphere.w;

        vec3 velocity = push.motion.xyz;
        float vertical = -velocity.y * Push(1.67 / shortest, signedDistance);
        float spread = push.sphere.w + size;
        float x = reach * reach / (spread * spread);
        float shape = (1.0 - x) * exp(-x) * (x < 1.0 ? 0.4 : 1.0) + 0.6 * exp(-1.0) / 4.0 * exp(-x / 4.0);
        float sideways = -0.3 * push.sphere.w / spread * length(velocity.xz) * shape;
        float accel = push.motion.w * (vertical * 0.2 / shortest + sideways * 0.4);

        vec4 state = imageLoad(Ocean_RippleTarget, id);
        state.y += accel * push.step.x;
        imageStore(Ocean_RippleTarget, id, state);
    }

    void main()
    {
        int resolution = int(push.slices.y);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (push.slices.z > 1.5)
        {
            if (id.x < resolution && id.y < resolution)
                imageStore(Ocean_RippleTarget, id, vec4(0.0));
            return;
        }
        if (push.slices.z > 0.5)
        {
            int slice = int(push.slices.w);
            float size = Texel(slice);
            vec2 corner = (push.sphere.xz + mod(push.viewer.xy, size)) / size + 0.5 * push.slices.y - 0.5 - float(gl_NumWorkGroups.x * 4u);
            id = ivec3(ivec2(floor(corner)) + id.xy, slice);
            if (any(lessThan(id.xy, ivec2(0))) || any(greaterThanEqual(id.xy, ivec2(resolution))))
                return;
            ForceTexel(id);
            return;
        }
        if (id.x >= resolution || id.y >= resolution)
            return;
        StepTexel(id);
    }
}
