// The starting amplitudes of the ocean's wave cascades, h0 at each wave vector and the conjugate of h0 at its opposite,
// so the waves stay real as they move. Power comes per octave of wavelength, 0.0625 m to 2048 m, linear between octave
// centres, cut at long wavelengths by the Pierson Moskowitz wind term and spread around the wind by cos squared, which
// turbulence flattens. Each cascade keeps a band of wave numbers: the first from zero, each next one from six of its
// own wavelengths per tile, so no wave is in two cascades.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rgba32f) uniform writeonly image2DArray Ocean_Initial;

    layout(push_constant) uniform Push
    {
        vec4 power[4];          // linear power of the 14 octaves
        vec4 wind;              // direction xz, speed in m/s, turbulence
        vec4 sizes;             // tile size of each cascade in metres
        vec4 grid;              // resolution, gravity, loop period, cascades
    } push;

    const float PI = 3.14159265359;

    uint Hash(uint seed)
    {
        seed = (seed ^ 61u) ^ (seed >> 16u);
        seed *= 9u;
        seed = seed ^ (seed >> 4u);
        seed *= 0x27d4eb2du;
        return seed ^ (seed >> 15u);
    }

    float Random(inout uint state)
    {
        state ^= state << 13u;
        state ^= state >> 17u;
        state ^= state << 5u;
        return float(state) / 4294967296.0;
    }

    vec2 Gaussian(inout uint state)
    {
        float u1 = max(Random(state), 1e-6);
        float u2 = Random(state);
        float r = sqrt(-2.0 * log(u1));
        return r * vec2(cos(2.0 * PI * u2), sin(2.0 * PI * u2));
    }

    float Power(int octave) { return push.power[octave >> 2][octave & 3]; }

    float Frequency(float k)
    {
        float w = sqrt(push.grid.y * k);
        float period = push.grid.z;
        return period > 0.0 ? 2.0 * PI * ceil(period * w / (2.0 * PI)) / period : w;
    }

    float Spreading(float cosine)
    {
        const float even = 0.33661977236;
        float turbulence = push.wind.w;
        return cosine > 0.0 ? mix(2.0 / PI * cosine * cosine, even, turbulence) : even * turbulence;
    }

    // The band of wave numbers a cascade keeps
    vec2 Band(int cascade, int cascades)
    {
        float low = cascade == 0 ? 0.0 : 12.0 * PI / push.sizes[cascade];
        float high = cascade == cascades - 1 ? 1e30 : 12.0 * PI / push.sizes[cascade + 1];
        return vec2(low, high);
    }

    // h0 at a wave of integer coordinates m on a cascade's tile, the same for every texel that asks
    vec2 Amplitude(ivec2 m, int cascade, int cascades, int size)
    {
        if (m == ivec2(0) || abs(m.x) >= size / 2 || abs(m.y) >= size / 2)
            return vec2(0.0);
        float tile = push.sizes[cascade];
        vec2 k = 2.0 * PI * vec2(m) / tile;
        float kLength = length(k);
        vec2 band = Band(cascade, cascades);
        if (kLength < band.x || kLength >= band.y)
            return vec2(0.0);

        float w = Frequency(kLength);
        float dwdk = push.grid.y / (2.0 * w);
        float octave = clamp(log2(2.0 * PI / kLength) + 4.0, 0.0, 13.0);
        int below = int(floor(octave));
        float power = mix(Power(below), Power(min(below + 1, 13)), octave - float(below));
        float wm = 0.87 * push.grid.y / max(push.wind.z, 0.01);
        power *= exp(-1.291 * pow(wm / w, 4.0));
        power *= Spreading(dot(k / kLength, push.wind.xy));
        float dk = 2.0 * PI / tile;
        float spectrum = power * dk * dk * dwdk / kLength;

        ivec2 texel = (m + size) % size;
        uint state = Hash(uint(cascade * size * size + texel.y * size + texel.x));
        return Gaussian(state) * sqrt(spectrum * 0.5) * 1.5;
    }

    void main()
    {
        int size = int(push.grid.x);
        int cascades = int(push.grid.w);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (id.x >= size || id.y >= size || id.z >= cascades)
            return;
        ivec2 m = ivec2(id.x < size / 2 ? id.x : id.x - size, id.y < size / 2 ? id.y : id.y - size);
        vec2 positive = Amplitude(m, id.z, cascades, size);
        vec2 negative = Amplitude(-m, id.z, cascades, size);
        imageStore(Ocean_Initial, id, vec4(positive, negative.x, -negative.y));
    }
}
