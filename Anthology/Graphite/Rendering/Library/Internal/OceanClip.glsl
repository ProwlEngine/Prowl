// Where the ocean's surface is cut away, on the depth's slices. Red is the clip, the surface
// cut where it is over a half, and green the water's height above sea level there. The height pass finds the water
// landing on each texel by a fixed point inversion and clears the clip. Each input then draws its signed distance at that
// height in its own space: spheres and boxes cut what they hold, a remove area cuts its rectangle and an include area
// brings the water back in its rectangle. Distances map to clip over a texel, so the cut falls on the shape's edge.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rg16f) uniform image2DArray Ocean_Clip;

    layout(push_constant) uniform Push
    {
        vec4 sizes;             // each cascade's tile size in metres
        vec4 depth;             // the depth viewer's xz modulo the largest depth texel, the first slice's width, resolution
        vec4 shore;             // shallow attenuation, depth slices, pass (0 input, 1 height, 2 clear), cascade resolution
        vec4 tile;              // the depth viewer's xz on the largest tile, the input's shape (0 sphere, 1 box, 2 include, 3 remove)
        vec4 centre;            // the input's centre relative to the depth viewer and the sea, its radius for a sphere
        vec4 axisX;             // the input's axes, each with its half size
        vec4 axisY;
        vec4 axisZ;
    } push;

    #include "Graphite/Internal/OceanWater.glsl"

    float Box(vec3 q)
    {
        return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
    }

    void main()
    {
        int resolution = int(push.depth.w);
        ivec3 id = ivec3(gl_GlobalInvocationID);
        if (id.x >= resolution || id.y >= resolution || id.z >= int(push.shore.y))
            return;
        float texel = push.depth.z * exp2(float(id.z)) / float(resolution);
        vec2 position = (vec2(id.xy) + 0.5 - 0.5 * float(resolution)) * texel - mod(push.depth.xy, texel);

        if (push.shore.z > 1.5)
        {
            imageStore(Ocean_Clip, id, vec4(0.0));
            return;
        }
        if (push.shore.z > 0.5)
        {
            vec2 source = OceanWater_Source(push.tile.xy + position, position, texel);
            float height = OceanWater_Sample(Ocean_Displacement, source, position, texel).y + OceanWater_Floor(position).y;
            imageStore(Ocean_Clip, id, vec4(0.0, height, 0.0, 0.0));
            return;
        }

        vec4 texelValue = imageLoad(Ocean_Clip, id);
        vec3 offset = vec3(position.x, texelValue.g, position.y) - push.centre.xyz;
        vec3 local = vec3(dot(offset, push.axisX.xyz), dot(offset, push.axisY.xyz), dot(offset, push.axisZ.xyz));
        vec3 halfSize = vec3(push.axisX.w, push.axisY.w, push.axisZ.w);
        int shape = int(push.tile.z + 0.5);
        float signedDistance;
        if (shape == 0)
            signedDistance = length(local) - push.centre.w;
        else if (shape == 1)
            signedDistance = Box(abs(local) - halfSize);
        else
            signedDistance = Box(vec3(abs(local.xz) - halfSize.xz, -1.0).xzy);
        float cover = clamp(0.5 - signedDistance / texel, 0.0, 1.0);
        texelValue.r = shape == 2 ? min(texelValue.r, 1.0 - cover) : max(texelValue.r, cover);
        imageStore(Ocean_Clip, id, texelValue);
    }
}
