#version 460 core

layout(quads, fractional_odd_spacing, ccw) in;

layout(location = 0) in vec3 tcWorldPos[];
layout(location = 1) in vec2 tcUV[];

layout(location = 0) out vec3 teWorldPos;
layout(location = 1) out vec3 teNormal;
layout(location = 2) out vec2 teUV;

layout(set = 0, binding = 0) uniform sampler2D heightMap;

layout(set = 0, binding = 1) uniform TessellationParams
{
    mat4 viewProj;
    vec4 cameraWorldPos;
    vec2 screenSize;
    float edgeLengthTargetPixels;
    float padding0;
} tessParams;

layout(set = 0, binding = 2) uniform TerrainParams
{
    float heightScale;
    float uvTexelSize;
    float worldPatchSize;
    float padding1;
} terrainParams;

vec3 bilinearPos(vec3 p00, vec3 p10, vec3 p01, vec3 p11, vec2 uv)
{
    vec3 a = mix(p00, p10, uv.x);
    vec3 b = mix(p01, p11, uv.x);
    return mix(a, b, uv.y);
}

vec2 bilinearUV(vec2 p00, vec2 p10, vec2 p01, vec2 p11, vec2 uv)
{
    vec2 a = mix(p00, p10, uv.x);
    vec2 b = mix(p01, p11, uv.x);
    return mix(a, b, uv.y);
}

void main()
{
    vec2 uv = gl_TessCoord.xy;

    // control point winding: 0=(u0,v0) 1=(u1,v0) 2=(u1,v1) 3=(u0,v1)
    vec3 worldPos = bilinearPos(tcWorldPos[0], tcWorldPos[1], tcWorldPos[3], tcWorldPos[2], uv);
    vec2 baseUV = bilinearUV(tcUV[0], tcUV[1], tcUV[3], tcUV[2], uv);

    float height = texture(heightMap, baseUV).r;
    worldPos.y += height * terrainParams.heightScale;

    float texel = terrainParams.uvTexelSize;
    float heightX1 = texture(heightMap, baseUV + vec2(texel, 0.0)).r;
    float heightX0 = texture(heightMap, baseUV - vec2(texel, 0.0)).r;
    float heightZ1 = texture(heightMap, baseUV + vec2(0.0, texel)).r;
    float heightZ0 = texture(heightMap, baseUV - vec2(0.0, texel)).r;

    float worldStep = terrainParams.worldPatchSize * texel * 2.0;

    vec3 tangent = vec3(worldStep, (heightX1 - heightX0) * terrainParams.heightScale, 0.0);
    vec3 bitangent = vec3(0.0, (heightZ1 - heightZ0) * terrainParams.heightScale, worldStep);

    vec3 normal = normalize(cross(bitangent, tangent));

    teWorldPos = worldPos;
    teNormal = normal;
    teUV = baseUV;

    gl_Position = tessParams.viewProj * vec4(worldPos, 1.0);
}
