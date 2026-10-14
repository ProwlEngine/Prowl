#version 460 core

layout(set = 1, binding = 0) uniform sampler2D albedoMap;
layout(set = 1, binding = 1) uniform sampler2D normalMap;
layout(set = 1, binding = 2) uniform sampler2D metallicRoughnessMap;
layout(set = 1, binding = 3) uniform sampler2D aoMap;

layout(push_constant) uniform MaterialPushConstant
{
    float alphaCutoff;
    float normalIntensity;
} material;

layout(location = 0) in vec3 inWorldPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inUV;
layout(location = 4) in vec4 inCurrClipPos;
layout(location = 5) in vec4 inPrevClipPos;

layout(location = 0) out vec4 outAlbedo;
layout(location = 1) out vec2 outNormal;
layout(location = 2) out vec2 outMotion;
layout(location = 3) out vec4 outMaterial;

vec2 octEncode(vec3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    vec2 wrapped = (1.0 - abs(n.yx)) * vec2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    vec2 encoded = n.z >= 0.0 ? n.xy : wrapped;
    return encoded * 0.5 + 0.5;
}

void main()
{
    vec4 albedoSample = texture(albedoMap, inUV);
    if (albedoSample.a < material.alphaCutoff)
    {
        discard;
    }

    vec3 N = normalize(inNormal);
    vec3 T = normalize(inTangent.xyz - N * dot(inTangent.xyz, N));
    vec3 B = cross(N, T) * inTangent.w;
    mat3 TBN = mat3(T, B, N);

    vec3 tangentNormal = texture(normalMap, inUV).xyz * 2.0 - 1.0;
    tangentNormal.xy *= material.normalIntensity;
    vec3 worldNormal = normalize(TBN * normalize(tangentNormal));

    vec4 mr = texture(metallicRoughnessMap, inUV);
    float metallic = mr.b;
    float roughness = clamp(mr.g, 0.045, 1.0);
    float ao = texture(aoMap, inUV).r;

    vec2 currNdc = inCurrClipPos.xy / inCurrClipPos.w;
    vec2 prevNdc = inPrevClipPos.xy / inPrevClipPos.w;
    vec2 motion = (currNdc - prevNdc) * 0.5;

    outAlbedo = vec4(albedoSample.rgb, albedoSample.a);
    outNormal = octEncode(worldNormal);
    outMotion = motion;
    outMaterial = vec4(metallic, roughness, ao, 1.0);
}
