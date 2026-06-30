module Shadow;

__exported import ShaderVariables;

// Precomputed Poisson disk offsets for rotated PCF sampling
public static const float2 POISSON_DISK_8[8] = {
    float2(-0.613392,  0.617481),
    float2( 0.170019, -0.040254),
    float2(-0.299417, -0.792901),
    float2( 0.645680, -0.530998),
    float2( 0.454148,  0.516511),
    float2(-0.507431,  0.281182),
    float2(-0.177186, -0.283153),
    float2( 0.100558,  0.765839)
};

// Simple hash function for random rotation in PCF sampling
public float InterleavedGradientNoise(float2 position) {
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(position, magic.xy)));
}

// Reconstruct world position from depth buffer
public float3 WorldPosFromDepth(float depth, float2 texCoord) {
    float z = depth * 2.0 - 1.0;
    float4 clipSpacePosition = float4(texCoord * 2.0 - 1.0, z, 1.0);
    float4 worldSpacePosition = mul(Frame.prowl_MatIVP, clipSpacePosition);
    worldSpacePosition /= worldSpacePosition.w;
    return worldSpacePosition.xyz;
}

// Sample shadow from atlas with PCF filtering. fragCoord is the pixel position (input.position.xy in
// the fragment stage) used to drive the rotated Poisson kernel - gl_FragCoord is unavailable here.
public float SampleShadowPCF(
    Sampler2DShadow shadowAtlas,
    float2 atlasCoords,
    float2 shadowMin,
    float2 shadowMax,
    float currentDepth,
    float shadowQuality,
    float shadowStrength,
    float2 fragCoord)
{
    float lit;

    if (shadowQuality < 0.5) {
        // Hard shadows - single hardware-compared (2x2) sample
        lit = shadowAtlas.SampleCmp(atlasCoords, currentDepth);
    } else {
        // Soft shadows - rotated Poisson disk, each tap a hardware 2x2 comparison
        uint atlasW, atlasH;
        shadowAtlas.GetDimensions(atlasW, atlasH);
        float2 texelSize = float2(1.0) / float2(atlasW, atlasH);
        float filterRadius = 1.5;
        float randomRotation = InterleavedGradientNoise(fragCoord) * 6.283185;
        float s = sin(randomRotation);
        float c = cos(randomRotation);
        float2x2 rotationMatrix = float2x2(c, s, -s, c);

        float2 texelScale = texelSize * filterRadius;
        lit = 0.0;
        for (int i = 0; i < 8; i++) {
            float2 offset = mul(rotationMatrix, POISSON_DISK_8[i]) * texelScale;
            float2 sampleCoords = clamp(atlasCoords + offset, shadowMin, shadowMax);
            lit += shadowAtlas.SampleCmp(sampleCoords, currentDepth);
        }
        lit /= 8.0;
    }

    float shadow = 1.0 - lit;
    return shadow * shadowStrength;
}

// Calculate slope-scale bias based on surface angle to light
public float CalculateSlopeBias(float3 worldNormal, float3 lightDirection, float baseBias) {
    float cosTheta = clamp(dot(normalize(worldNormal), normalize(lightDirection)), 0.0, 1.0);
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float slopeScaleBias = baseBias * (sinTheta / max(cosTheta, 1e-4));
    slopeScaleBias = clamp(slopeScaleBias, 0.0, baseBias * 2.0);
    return baseBias + slopeScaleBias;
}

// Convert shadow space coordinates to atlas UVs
public void GetAtlasCoordinates(
    float3 projCoords,
    float4 atlasParams,
    float atlasSize,
    out float2 atlasCoords,
    out float2 shadowMin,
    out float2 shadowMax)
{
    atlasCoords.x = atlasParams.x + (projCoords.x * atlasParams.z);
    atlasCoords.y = atlasParams.y + (projCoords.y * atlasParams.z);

    float2 texelSize = float2(1.0) / atlasSize;
    shadowMin = float2(atlasParams.x, atlasParams.y) / atlasSize + texelSize * 0.5;
    shadowMax = float2(atlasParams.x + atlasParams.z, atlasParams.y + atlasParams.z) / atlasSize - texelSize * 0.5;

    atlasCoords /= atlasSize;
}
