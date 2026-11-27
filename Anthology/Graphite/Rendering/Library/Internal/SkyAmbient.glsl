// Projects the captured sky onto order 2 spherical harmonics and packs them as the lighting library reads them, so
// ambient light can follow the sky. One workgroup reads a small mip of the cube, every texel weighted by its solid angle.
Compute
{
    layout(local_size_x = 64) in;
    #include "Graphite/Math.glsl"

    uniform samplerCube Graphite_Source;
    layout(std430) buffer Graphite_SkyAmbient { vec4 packed[7]; } result;
    layout(push_constant) uniform Push { float lod; uint size; } push;

    const float K0 = 0.2820947918;
    const float K1 = 0.4886025119;
    const float K2a = 1.0925484306;
    const float K2b = 0.3153915652;
    const float K2c = 0.5462742153;

    shared vec3 partial[64][9];
    shared float partialWeight[64];

    vec3 FaceDirection(uint face, vec2 st)
    {
        if (face == 0u) return vec3(1.0, -st.y, -st.x);
        if (face == 1u) return vec3(-1.0, -st.y, st.x);
        if (face == 2u) return vec3(st.x, 1.0, st.y);
        if (face == 3u) return vec3(st.x, -1.0, -st.y);
        if (face == 4u) return vec3(st.x, -st.y, 1.0);
        return vec3(-st.x, -st.y, -1.0);
    }

    void main()
    {
        uint thread = gl_LocalInvocationID.x;
        vec3 c[9];
        for (int i = 0; i < 9; i++)
            c[i] = vec3(0.0);
        float weight = 0.0;

        uint texels = 6u * push.size * push.size;
        for (uint t = thread; t < texels; t += 64u)
        {
            uint face = t / (push.size * push.size);
            uint inFace = t % (push.size * push.size);
            vec2 st = (vec2(inFace % push.size, inFace / push.size) + 0.5) / float(push.size) * 2.0 - 1.0;
            float solidAngle = 1.0 / pow(1.0 + dot(st, st), 1.5);
            vec3 n = normalize(FaceDirection(face, st));
            vec3 light = textureLod(Graphite_Source, n, push.lod).rgb * solidAngle;

            c[0] += light * K0;
            c[1] += light * (K1 * n.y);
            c[2] += light * (K1 * n.z);
            c[3] += light * (K1 * n.x);
            c[4] += light * (K2a * n.x * n.y);
            c[5] += light * (K2a * n.y * n.z);
            c[6] += light * (K2b * (3.0 * n.z * n.z - 1.0));
            c[7] += light * (K2a * n.x * n.z);
            c[8] += light * (K2c * (n.x * n.x - n.y * n.y));
            weight += solidAngle;
        }

        for (int i = 0; i < 9; i++)
            partial[thread][i] = c[i];
        partialWeight[thread] = weight;
        barrier();

        for (uint stride = 32u; stride > 0u; stride >>= 1)
        {
            if (thread < stride)
            {
                for (int i = 0; i < 9; i++)
                    partial[thread][i] += partial[thread + stride][i];
                partialWeight[thread] += partialWeight[thread + stride];
            }
            barrier();
        }

        if (thread != 0u)
            return;

        // The discrete weights cover the whole sphere, 4 pi steradians
        float scale = 4.0 * GRAPHITE_PI / partialWeight[0];
        vec3 r[9];
        for (int i = 0; i < 9; i++)
            r[i] = partial[0][i] * scale;

        // Cosine lobe convolution over pi per band, then the basis constants, folded for one dot per band
        vec3 c0 = r[0] * K0;
        vec3 c1 = r[1] * (2.0 / 3.0 * K1), c2 = r[2] * (2.0 / 3.0 * K1), c3 = r[3] * (2.0 / 3.0 * K1);
        vec3 c4 = r[4] * (0.25 * K2a), c5 = r[5] * (0.25 * K2a), c7 = r[7] * (0.25 * K2a);
        vec3 c6 = r[6] * (0.25 * K2b);
        vec3 c8 = r[8] * (0.25 * K2c);
        result.packed[0] = vec4(c3.x, c1.x, c2.x, c0.x - c6.x);
        result.packed[1] = vec4(c3.y, c1.y, c2.y, c0.y - c6.y);
        result.packed[2] = vec4(c3.z, c1.z, c2.z, c0.z - c6.z);
        result.packed[3] = vec4(c4.x, c5.x, c6.x * 3.0, c7.x);
        result.packed[4] = vec4(c4.y, c5.y, c6.y * 3.0, c7.y);
        result.packed[5] = vec4(c4.z, c5.z, c6.z * 3.0, c7.z);
        result.packed[6] = vec4(c8, 0.0);
    }
}
