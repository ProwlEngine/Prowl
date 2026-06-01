// Shadow sampling utilities for deferred lighting
// Shared by DirectionalLight, SpotLight, and PointLight shaders

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
float SampleShadowPCF(
    sampler2D shadowAtlas,
    vec2 atlasCoords,
    vec2 shadowMin,
    vec2 shadowMax,
    float currentDepth,
    float shadowQuality,
    float shadowStrength)
{
    float shadow = 0.0;
    vec2 atlasSize = vec2(textureSize(shadowAtlas, 0));
    vec2 texelSize = vec2(1.0) / atlasSize;

    // Check shadow quality: 0 = Hard, 1 = Soft
    if (shadowQuality < 0.5) {
        // Hard shadows - single sample
        float closestDepth = texture(shadowAtlas, atlasCoords).r;
        shadow = currentDepth > closestDepth ? 1.0 : 0.0;
    } else {
        // Soft shadows - Poisson Disk PCF with random rotation
        float filterRadius = 1.5;
        float randomRotation = InterleavedGradientNoise(gl_FragCoord.xy) * 6.283185;
        float s = sin(randomRotation);
        float c = cos(randomRotation);
        mat2 rotationMatrix = mat2(c, -s, s, c);

        for(int i = 0; i < 8; i++) {
            vec2 offset = rotationMatrix * POISSON_DISK_8[i] * texelSize * filterRadius;
            vec2 sampleCoords = clamp(atlasCoords + offset, shadowMin, shadowMax);
            float pcfDepth = texture(shadowAtlas, sampleCoords).r;
            shadow += currentDepth > pcfDepth ? 1.0 : 0.0;
        }
        shadow /= 8.0;
    }

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
    float slopeScaleBias = baseBias * tan(acos(cosTheta));
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
