#version 460 core

// Fragment stage of the conservative voxelization pass.
// Reconstructs an integer voxel coordinate, evaluates direct lighting once
// per covered fragment, packs the result into a single uint and merges it
// into the voxel volume with an atomic operation.

layout(location = 0) in vec3 gWorldPos;
layout(location = 1) in vec3 gWorldNormal;
layout(location = 2) in vec2 gUV;
layout(location = 3) flat in int gAxis;

layout(set = 0, binding = 1) uniform sampler2D uAlbedoMap;

layout(set = 0, binding = 2) uniform LightData
{
    vec4 direction; // xyz: normalized direction the light travels, w unused
    vec4 color;     // rgb: light color/intensity, w unused
} uLight;

layout(r32ui, set = 0, binding = 3) uniform uimage3D uVoxelRadiance;

layout(push_constant) uniform VoxelizeFragPush
{
    vec3 volumeOrigin;
    float voxelSize;
    int gridResolution;
} pc;

void main()
{
    // gl_FragCoord.xy already lands on the two non-dominant axes because
    // the geometry shader swizzled them into clip xy; the dominant axis
    // is recovered from the interpolated world position instead.
    ivec2 texelXY = ivec2(gl_FragCoord.xy);
    vec3 localPos = (gWorldPos - pc.volumeOrigin) / pc.voxelSize;

    ivec3 voxelCoord;
    if (gAxis == 0)
        voxelCoord = ivec3(int(localPos.x), texelXY.x, texelXY.y);
    else if (gAxis == 1)
        voxelCoord = ivec3(texelXY.x, int(localPos.y), texelXY.y);
    else
        voxelCoord = ivec3(texelXY.x, texelXY.y, int(localPos.z));

    voxelCoord = clamp(voxelCoord, ivec3(0), ivec3(pc.gridResolution - 1));

    vec3 albedo = texture(uAlbedoMap, gUV).rgb;
    vec3 normal = normalize(gWorldNormal);
    float NdotL = max(dot(normal, -uLight.direction.xyz), 0.0);
    vec3 radiance = albedo * uLight.color.rgb * NdotL;

    // Pack radiance as 8-8-8-8 unorm and merge with imageAtomicMax: this is
    // the classic cheap "keep the brightest sample" trick used for
    // real-time voxelization instead of a true running average, which
    // would need a slower atomicCompSwap retry loop.
    uvec3 rgb8 = uvec3(clamp(radiance, vec3(0.0), vec3(1.0)) * 255.0 + 0.5);
    uint packed = rgb8.r | (rgb8.g << 8) | (rgb8.b << 16) | (255u << 24);

    imageAtomicMax(uVoxelRadiance, voxelCoord, packed);
}
