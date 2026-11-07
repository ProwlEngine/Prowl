// The inverse FFT of the ocean's two spectra, a row or a column per workgroup and every stage in shared memory, the
// Stockham radix 2 form so the order stays natural. Rows transform in place, columns then write the fields: the
// displacement (x, height, z, dDx/dz) and the slopes (dh/dx, dh/dz, dDx/dx, dDz/dz). Resolutions up to 256.
// Columns also keep each texel's foam, which fades by the fade rate and builds where the Jacobian of the sideways
// motion falls under the coverage, as waves fold.
Compute
{
    layout(local_size_x = 128) in;

    layout(rgba32f) uniform image2DArray Ocean_SpectrumA;
    layout(rgba32f) uniform image2DArray Ocean_SpectrumB;
    layout(rgba16f) uniform writeonly image2DArray Ocean_Displacement;
    layout(rgba16f) uniform writeonly image2DArray Ocean_Slopes;
    layout(r16f) uniform image2DArray Ocean_Foam;

    // columns 0 transforms rows in place, 1 transforms columns into the fields. reset 1 starts the foam clear
    layout(push_constant) uniform Push
    {
        uint size;
        uint columns;
        uint reset;
        float deltaTime;
        float coverage;
        float strength;
        float fade;
        float pad;
    } push;

    const float PI = 3.14159265359;

    shared vec4 a[2][256];
    shared vec4 b[2][256];

    vec4 Turn(vec4 v, vec2 w) { return vec4(v.x * w.x - v.y * w.y, v.x * w.y + v.y * w.x, v.z * w.x - v.w * w.y, v.z * w.y + v.w * w.x); }

    ivec3 Texel(uint n)
    {
        uint line = gl_WorkGroupID.y;
        return push.columns == 0u ? ivec3(n, line, gl_WorkGroupID.z) : ivec3(line, n, gl_WorkGroupID.z);
    }

    void main()
    {
        uint size = push.size;
        uint halfSize = size / 2u;
        uint t = gl_LocalInvocationID.x;

        for (uint n = t; n < size; n += 128u)
        {
            a[0][n] = imageLoad(Ocean_SpectrumA, Texel(n));
            b[0][n] = imageLoad(Ocean_SpectrumB, Texel(n));
        }
        barrier();

        uint source = 0u;
        for (uint span = 1u; span < size; span *= 2u)
        {
            if (t < halfSize)
            {
                float angle = PI * float(t % span) / float(span);
                vec2 w = vec2(cos(angle), sin(angle));
                vec4 a0 = a[source][t], a1 = Turn(a[source][t + halfSize], w);
                vec4 b0 = b[source][t], b1 = Turn(b[source][t + halfSize], w);
                uint to = (t / span) * span * 2u + t % span;
                a[1u - source][to] = a0 + a1;
                a[1u - source][to + span] = a0 - a1;
                b[1u - source][to] = b0 + b1;
                b[1u - source][to + span] = b0 - b1;
            }
            source = 1u - source;
            barrier();
        }

        for (uint n = t; n < size; n += 128u)
        {
            vec4 fa = a[source][n], fb = b[source][n];
            if (push.columns == 0u)
            {
                imageStore(Ocean_SpectrumA, Texel(n), fa);
                imageStore(Ocean_SpectrumB, Texel(n), fb);
            }
            else
            {
                imageStore(Ocean_Displacement, Texel(n), fa);
                imageStore(Ocean_Slopes, Texel(n), fb);
                float jacobian = (1.0 + fb.z) * (1.0 + fb.w) - fa.w * fa.w;
                float foam = push.reset != 0u ? 0.0 : imageLoad(Ocean_Foam, Texel(n)).r;
                foam = foam * max(1.0 - push.fade * push.deltaTime, 0.0) + 5.0 * push.deltaTime * push.strength * clamp(push.coverage - jacobian, 0.0, 1.0);
                imageStore(Ocean_Foam, Texel(n), vec4(clamp(foam, 0.0, 1.0)));
            }
        }
    }
}
