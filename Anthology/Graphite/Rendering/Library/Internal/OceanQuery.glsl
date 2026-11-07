// The water at query points, as the ocean's surface draws it: the point under which the displaced water lands on the
// query's xz is found by four fixed point steps, then its height and slopes are read there. Waves
// shorter than a query's spatial length are filtered out by the cascades' mips, and waves shrink in shallow water as
// the surface's do. Each point gives the height above sea level before the planet's curve, the lake's level included, the normal's x and z over
// its y, and the water's depth.
Compute
{
    layout(local_size_x = 64) in;

    uniform sampler2DArray Ocean_Slopes;

    // each point: its position on the largest tile and relative to the depth's viewer, then its spatial length
    layout(std430) readonly buffer Ocean_Points { vec4 points[]; };
    layout(std430) writeonly buffer Ocean_Results { vec4 results[]; };

    layout(push_constant) uniform Push
    {
        vec4 sizes;             // each cascade's tile size in metres
        vec4 depth;             // the depth viewer's xz modulo the largest depth texel, the first slice's width, resolution
        vec4 shore;             // shallow attenuation, depth slices, point count, cascade resolution
    } push;

    #include "Graphite/Internal/OceanWater.glsl"

    void main()
    {
        uint index = gl_GlobalInvocationID.x;
        if (index >= uint(push.shore.z))
            return;
        vec4 wave = points[index * 2u];
        float span = points[index * 2u + 1u].x;
        vec2 bed = OceanWater_Floor(wave.zw);
        float depth = bed.x;

        vec2 source = OceanWater_Source(wave.xy, wave.zw, span);
        vec4 displacement = OceanWater_Sample(Ocean_Displacement, source, wave.zw, span);
        vec4 slopes = OceanWater_Sample(Ocean_Slopes, source, wave.zw, span);
        vec3 tangentX = vec3(1.0 + slopes.z, slopes.x, displacement.w);
        vec3 tangentZ = vec3(displacement.w, slopes.y, 1.0 + slopes.w);
        vec3 normal = cross(tangentZ, tangentX);
        normal.y = max(normal.y, 0.05);
        results[index] = vec4(displacement.y + bed.y, normal.x / normal.y, normal.z / normal.y, depth);
    }
}
