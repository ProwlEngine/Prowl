#version 460 core

layout(set = 0, binding = 0) uniform sampler2D gDepth;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D noiseTexture;

#define KERNEL_SIZE 32

layout(set = 1, binding = 0) uniform SSAOUBO
{
    vec4 samples[KERNEL_SIZE];
    mat4 projection;
    mat4 view;
    mat4 invProjection;
    vec2 noiseScale;
    float radius;
    float bias;
    float power;
} ssao;

layout(location = 0) in vec2 inUV;
layout(location = 0) out float outOcclusion;

vec3 octDecode(vec2 e)
{
    e = e * 2.0 - 1.0;
    vec3 n = vec3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
    float t = clamp(-n.z, 0.0, 1.0);
    n.x += n.x >= 0.0 ? -t : t;
    n.y += n.y >= 0.0 ? -t : t;
    return normalize(n);
}

vec3 reconstructViewPos(vec2 uv, float depth)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, depth, 1.0);
    vec4 viewPos = ssao.invProjection * clip;
    return viewPos.xyz / viewPos.w;
}

void main()
{
    float depth = texture(gDepth, inUV).r;
    vec3 viewPos = reconstructViewPos(inUV, depth);

    vec3 worldNormal = octDecode(texture(gNormal, inUV).rg);
    vec3 N = normalize(mat3(ssao.view) * worldNormal);

    vec3 randomVec = normalize(texture(noiseTexture, inUV * ssao.noiseScale).xyz * 2.0 - 1.0);
    vec3 tangent = normalize(randomVec - N * dot(randomVec, N));
    vec3 bitangent = cross(N, tangent);
    mat3 TBN = mat3(tangent, bitangent, N);

    float occlusion = 0.0;
    for (int i = 0; i < KERNEL_SIZE; i++)
    {
        vec3 samplePos = viewPos + (TBN * ssao.samples[i].xyz) * ssao.radius;

        vec4 offset = vec4(samplePos, 1.0);
        offset = ssao.projection * offset;
        offset.xyz /= offset.w;
        offset.xy = offset.xy * 0.5 + 0.5;

        float sampleDepth = texture(gDepth, offset.xy).r;
        vec3 sampledViewPos = reconstructViewPos(offset.xy, sampleDepth);

        float rangeCheck = smoothstep(0.0, 1.0, ssao.radius / max(abs(viewPos.z - sampledViewPos.z), 1e-4));
        occlusion += (sampledViewPos.z >= samplePos.z + ssao.bias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusion = 1.0 - (occlusion / float(KERNEL_SIZE));
    outOcclusion = pow(clamp(occlusion, 0.0, 1.0), ssao.power);
}
