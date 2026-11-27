// Moves the ocean's starting amplitudes to the time and builds the spectra of eight real fields, two to a complex one
// so each transform makes two: displacement x with height, displacement z with dDx/dz, slope x with slope z, and
// dDx/dx with dDz/dz. The horizontal fields are scaled by the chop and lean toward the crests, so crests sharpen.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rgba32f) uniform readonly image2DArray Ocean_Initial;
    layout(rgba32f) uniform writeonly image2DArray Ocean_SpectrumA;
    layout(rgba32f) uniform writeonly image2DArray Ocean_SpectrumB;

    layout(push_constant) uniform Push
    {
        vec4 sizes;             // tile size of each cascade in metres
        vec4 grid;              // resolution, gravity, loop period, cascades
        vec4 time;              // seconds into the loop, chop
    } push;

    const float PI = 3.14159265359;

    vec2 Mul(vec2 a, vec2 b) { return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
    vec2 MulI(vec2 a) { return vec2(-a.y, a.x); }

    float Frequency(float k)
    {
        float w = sqrt(push.grid.y * k);
        float period = push.grid.z;
        return period > 0.0 ? 2.0 * PI * ceil(period * w / (2.0 * PI)) / period : w;
    }

    void main()
    {
        int size = int(push.grid.x);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (id.x >= size || id.y >= size || id.z >= int(push.grid.w))
            return;
        ivec2 m = ivec2(id.x < size / 2 ? id.x : id.x - size, id.y < size / 2 ? id.y : id.y - size);
        vec2 k = 2.0 * PI * vec2(m) / push.sizes[id.z];
        float kLength = max(length(k), 1e-6);

        vec4 initial = imageLoad(Ocean_Initial, id);
        float phase = Frequency(kLength) * push.time.x;
        vec2 turn = vec2(cos(phase), sin(phase));
        vec2 h = Mul(initial.xy, turn) + Mul(initial.zw, vec2(turn.x, -turn.y));

        float chop = push.time.y;
        vec2 dx = MulI(h) * (chop * k.x / kLength);
        vec2 dz = MulI(h) * (chop * k.y / kLength);
        vec2 dxdz = -h * (chop * k.x * k.y / kLength);
        vec2 dxdx = -h * (chop * k.x * k.x / kLength);
        vec2 dzdz = -h * (chop * k.y * k.y / kLength);
        vec2 slopeX = MulI(h) * k.x;
        vec2 slopeZ = MulI(h) * k.y;

        imageStore(Ocean_SpectrumA, id, vec4(dx + MulI(h), dz + MulI(dxdz)));
        imageStore(Ocean_SpectrumB, id, vec4(slopeX + MulI(slopeZ), dxdx + MulI(dzdz)));
    }
}
