#version 460 core

// Deferred voxel cone traced GI pass. Reads the G-buffer plus the already
// lit scene color, traces a handful of diffuse hemisphere cones and one
// specular reflection cone through the voxel radiance volume, and adds the
// indirect result on top of the direct lighting.

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uGBufferPosition;
layout(set = 0, binding = 1) uniform sampler2D uGBufferNormal;
layout(set = 0, binding = 2) uniform sampler2D uGBufferAlbedoRoughness;
layout(set = 0, binding = 3) uniform sampler2D uSceneColor;

// Representative direction of the anisotropic voxel mip chain. A full
// implementation binds up to 6 of these (one per axis direction) and
// blends coneTrace results by how much each cone direction faces each
// axis; here a single chain is sampled to keep the pass compact.
layout(set = 0, binding = 4) uniform sampler3D uVoxelRadiance;

layout(push_constant) uniform ConeTracePush
{
    vec3 volumeOrigin;
    float voxelSize;
    int gridResolution;
    float coneStartRadius;
    float maxDistance;
    vec3 cameraPos;
} pc;

// Marches a cone through the voxel volume, front-to-back compositing
// radiance and opacity until the cone saturates or runs out of distance.
// dirIndex identifies which of the (up to 6) directional mip chains this
// cone should sample; only one chain is wired up in this simplified pass.
vec4 coneTrace(vec3 startPos, vec3 direction, float apertureTan, float maxDist, int dirIndex, sampler3D voxelTex)
{
    vec3 accumRadiance = vec3(0.0);
    float accumAlpha = 0.0;
    float volumeWorldSize = pc.voxelSize * float(pc.gridResolution);

    float t = pc.voxelSize;
    while (t < maxDist && accumAlpha < 0.95)
    {
        float coneRadius = pc.coneStartRadius + t * apertureTan;
        float diameter = max(coneRadius * 2.0, pc.voxelSize);
        float mipLevel = log2(diameter / pc.voxelSize);

        vec3 samplePosWS = startPos + direction * t;
        vec3 uvw = (samplePosWS - pc.volumeOrigin) / volumeWorldSize;
        if (any(lessThan(uvw, vec3(0.0))) || any(greaterThan(uvw, vec3(1.0))))
            break;

        vec4 voxelSample = textureLod(voxelTex, uvw, mipLevel);
        accumRadiance += (1.0 - accumAlpha) * voxelSample.rgb * voxelSample.a;
        accumAlpha += (1.0 - accumAlpha) * voxelSample.a;

        t += diameter * 0.5;
    }

    return vec4(accumRadiance, accumAlpha);
}

void main()
{
    vec3 worldPos = texture(uGBufferPosition, vUV).rgb;
    vec3 normal = normalize(texture(uGBufferNormal, vUV).rgb);
    vec4 albedoRoughness = texture(uGBufferAlbedoRoughness, vUV);
    vec3 albedo = albedoRoughness.rgb;
    float roughness = albedoRoughness.a;

    vec3 viewDir = normalize(pc.cameraPos - worldPos);
    vec3 reflectDir = reflect(-viewDir, normal);

    vec3 up = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, normal));
    vec3 bitangent = cross(normal, tangent);

    // Fixed hemisphere directions (tangent space) for the diffuse cones:
    // one straight along the normal plus five arranged around it, roughly
    // 60 degree spread, matching the classic VCT diffuse GI setup.
    const vec3 coneDirsTS[6] = vec3[6](
        vec3(0.0, 0.0, 1.0),
        vec3(0.0, 0.618, 0.786),
        vec3(0.588, 0.191, 0.786),
        vec3(0.363, -0.500, 0.786),
        vec3(-0.363, -0.500, 0.786),
        vec3(-0.588, 0.191, 0.786)
    );
    const float coneWeights[6] = float[6](0.25, 0.15, 0.15, 0.15, 0.15, 0.15);
    const float diffuseApertureTan = 0.577; // tan(30 deg), ~60 degree full aperture

    vec3 startPos = worldPos + normal * pc.voxelSize * 2.0;

    vec3 diffuseGI = vec3(0.0);
    for (int i = 0; i < 6; ++i)
    {
        vec3 dirTS = coneDirsTS[i];
        vec3 dirWS = normalize(tangent * dirTS.x + bitangent * dirTS.y + normal * dirTS.z);
        vec4 result = coneTrace(startPos, dirWS, diffuseApertureTan, pc.maxDistance, 0, uVoxelRadiance);
        diffuseGI += result.rgb * coneWeights[i];
    }

    float specApertureTan = clamp(roughness * roughness, 0.01, 1.0);
    vec4 specResult = coneTrace(startPos, reflectDir, specApertureTan, pc.maxDistance, 0, uVoxelRadiance);
    vec3 specularGI = specResult.rgb;

    vec3 directColor = texture(uSceneColor, vUV).rgb;
    vec3 finalColor = directColor + diffuseGI * albedo + specularGI;

    outColor = vec4(finalColor, 1.0);
}
