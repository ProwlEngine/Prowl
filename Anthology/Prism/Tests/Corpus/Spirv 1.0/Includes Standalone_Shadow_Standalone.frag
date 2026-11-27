#version 460 core

// =============================================================
// ShaderVariables.glsl (partial) - inlined for PROWL_MATRIX_I_VP,
// which WorldPosFromDepth needs to unproject clip space back to world space.
// =============================================================

layout(std140, set = 0, binding = 0) uniform GlobalUniforms { mat4 prowl_MatV; mat4 prowl_MatIV; mat4 prowl_MatP; mat4 prowl_MatVP; mat4 prowl_PrevViewProj; mat4 prowl_MatIP; mat4 prowl_MatIVP; mat4 prowl_MatVP_NonJittered; vec3 _WorldSpaceCameraPos; float _padding0; vec4 _ProjectionParams; vec4 _ScreenParams; vec2 _CameraJitter; vec2 _CameraPreviousJitter; vec4 _Time; vec4 _SinTime; vec4 _CosTime; vec4 prowl_DeltaTime; };

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
#define PROWL_MATRIX_I_P prowl_MatIP
#define PROWL_MATRIX_I_VP prowl_MatIVP
#define PROWL_MATRIX_VP_NONJITTERED prowl_MatVP_NonJittered

// =============================================================
// Shadow.glsl - inlined, standalone.
// Shadow sampling utilities for deferred lighting.
// Shared by DirectionalLight, SpotLight, and PointLight shaders.
// (Real source has no include guard; one is added here for consistency.)
// =============================================================

#ifndef SHADOW_GLSL
#define SHADOW_GLSL

// Precomputed Poisson disk offsets for rotated PCF sampling
const vec2 POISSON_DISK_8[8] = vec2[](
    vec2(-0.613392,  0.617481),
    vec2( 0.170019, -0.040254),
    vec2(-0.299417, -0.792901),
    vec2( 0.645680, -0.530998),
    vec2( 0.454148,  0.516511),
    vec2(-0.507431,  0.281182),
    vec2(-0.177186, -0.283153),
    vec2( 0.100558,  0.765839)
);

// Simple hash function for random rotation in PCF sampling
float InterleavedGradientNoise(vec2 position) {
    vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(position, magic.xy)));
}

// Reconstruct world position from depth buffer
vec3 WorldPosFromDepth(float depth, vec2 texCoord) {
    float z = depth * 2.0 - 1.0;
    vec4 clipSpacePosition = vec4(texCoord * 2.0 - 1.0, z, 1.0);
    vec4 worldSpacePosition = PROWL_MATRIX_I_VP * clipSpacePosition;
    worldSpacePosition /= worldSpacePosition.w;
    return worldSpacePosition.xyz;
}

// Sample shadow from atlas with PCF filtering
// Parameters:
//   shadowAtlas: The shadow atlas texture
//   atlasCoords: UV coordinates in the atlas (normalized 0-1)
//   shadowMin/Max: Atlas boundaries for clamping (prevents bleeding)
//   currentDepth: Fragment depth in light space
//   shadowQuality: 0 = hard shadows, 1 = soft shadows
//   shadowStrength: Multiplier for shadow intensity
//   filterRadius: Soft PCF kernel radius in atlas texels (callers scale this per cascade so the
//                 penumbra stays roughly constant in world space; ignored on the hard path)
float SampleShadowPCF(
    sampler2DShadow shadowAtlas,
    vec2 atlasCoords,
    vec2 shadowMin,
    vec2 shadowMax,
    float currentDepth,
    float shadowQuality,
    float shadowStrength,
    float filterRadius)
{
    // Hardware depth comparison (GL_COMPARE_REF_TO_TEXTURE + GL_LEQUAL): texture() returns the
    // filtered fraction that is LIT (currentDepth <= storedDepth). With LINEAR filtering on the
    // atlas each fetch is a hardware 2x2 PCF tap, so even the hard path is bilinearly filtered.
    float lit;

    // Check shadow quality: 0 = Hard, 1 = Soft
    if (shadowQuality < 0.5) {
        // Hard shadows - single hardware-compared (2x2) sample
        lit = texture(shadowAtlas, vec3(atlasCoords, currentDepth));
    } else {
        // Soft shadows - rotated Poisson disk, each tap a hardware 2x2 comparison
        vec2 texelSize = vec2(1.0) / vec2(textureSize(shadowAtlas, 0));
        float randomRotation = InterleavedGradientNoise(gl_FragCoord.xy) * 6.283185;
        float s = sin(randomRotation);
        float c = cos(randomRotation);
        mat2 rotationMatrix = mat2(c, -s, s, c);

        vec2 texelScale = texelSize * filterRadius;
        lit = 0.0;
        for(int i = 0; i < 8; i++) {
            vec2 offset = (rotationMatrix * POISSON_DISK_8[i]) * texelScale;
            vec2 sampleCoords = clamp(atlasCoords + offset, shadowMin, shadowMax);
            lit += texture(shadowAtlas, vec3(sampleCoords, currentDepth));
        }
        lit /= 8.0;
    }

    float shadow = 1.0 - lit;       // fraction occluded
    return shadow * shadowStrength;
}

// Calculate slope-scale bias based on surface angle to light
// Parameters:
//   worldNormal: Surface normal in world space
//   lightDirection: Direction to light (normalized)
//   baseBias: Base shadow bias value
// Returns: Combined bias value
float CalculateSlopeBias(vec3 worldNormal, vec3 lightDirection, float baseBias) {
    float cosTheta = clamp(dot(normalize(worldNormal), normalize(lightDirection)), 0.0, 1.0);
    // tan(acos(cosTheta)) == sin/cos, without the two transcendentals.
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float slopeScaleBias = baseBias * (sinTheta / max(cosTheta, 1e-4));
    slopeScaleBias = clamp(slopeScaleBias, 0.0, baseBias * 2.0);
    return baseBias + slopeScaleBias;
}

// Convert shadow space coordinates to atlas UVs
// Parameters:
//   projCoords: Projected coordinates from shadow matrix (already in 0-1 range)
//   atlasParams: xy = atlas position, z = atlas size
//   atlasSize: Total atlas texture size
// Returns: Normalized atlas coordinates and boundaries for clamping
void GetAtlasCoordinates(
    vec3 projCoords,
    vec4 atlasParams,
    float atlasSize,
    out vec2 atlasCoords,
    out vec2 shadowMin,
    out vec2 shadowMax)
{
    // Map projected coords to atlas region
    atlasCoords.x = atlasParams.x + (projCoords.x * atlasParams.z);
    atlasCoords.y = atlasParams.y + (projCoords.y * atlasParams.z);

    // Calculate shadow map boundaries to prevent bleeding
    vec2 texelSize = vec2(1.0) / atlasSize;
    shadowMin = vec2(atlasParams.x, atlasParams.y) / atlasSize + texelSize * 0.5;
    shadowMax = vec2(atlasParams.x + atlasParams.z, atlasParams.y + atlasParams.z) / atlasSize - texelSize * 0.5;

    // Normalize to 0-1 range
    atlasCoords /= atlasSize;
}

#endif

layout(set = 1, binding = 1) uniform sampler2DShadow testShadowMap;

layout(location = 0) out vec4 FragColor;

void main()
{
    mat4 lightSpaceMatrix = PROWL_MATRIX_VP;
    vec3 testWorldPos = vec3(0.0, 1.0, 0.0);
    vec3 testNormal = normalize(vec3(0.0, 1.0, 0.0));
    vec3 testLightDir = normalize(vec3(0.3, 0.8, 0.2));

    float bias = CalculateSlopeBias(testNormal, testLightDir, 0.005);

    vec4 clipPos = lightSpaceMatrix * vec4(testWorldPos, 1.0);
    vec3 projCoords = clipPos.xyz / clipPos.w * 0.5 + 0.5;

    float shadow = SampleShadowPCF(testShadowMap, projCoords.xy, vec2(0.0), vec2(1.0), projCoords.z - bias, 1.0, 1.0, 1.5);
    vec3 reconstructed = WorldPosFromDepth(projCoords.z, projCoords.xy);

    vec3 result = vec3(1.0 - shadow) * (vec3(1.0) - abs(reconstructed - testWorldPos) * 0.001);
    FragColor = vec4(result, 1.0);
}
