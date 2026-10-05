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

// Moves a receiver toward the light and out along its geometric normal before the shadow test.
// Both biases are in shadow map texels, texelWorld is the world size of one texel at the receiver,
// so the offset follows the map's resolution instead of its depth range.
vec3 ApplyShadowBias(vec3 worldPos, vec3 geomNormal, vec3 toLight, float texelWorld, float depthBias, float normalBias)
{
    float NdotL = clamp(dot(geomNormal, toLight), 0.0, 1.0);
    float sinTheta = sqrt(1.0 - NdotL * NdotL);
    return worldPos + (toLight * depthBias + geomNormal * (normalBias * sinTheta)) * texelWorld;
}

// Projects a world position into a shadow map's 0 to 1 coordinates
vec3 ProjectToShadowMap(mat4 shadowMatrix, vec3 worldPos)
{
    vec4 clip = shadowMatrix * vec4(worldPos, 1.0);
    return (clip.xyz / clip.w) * 0.5 + 0.5;
}

// Fraction of light blocked at projCoords inside an atlas tile, 0 to 1.
//   atlasParams: xy = tile position in texels, z = tile size in texels
//   quality: 0 = hard, 1 = soft
//   filterRadius: soft kernel radius in texels
float SampleShadowPCF(sampler2DShadow shadowAtlas, float atlasSize, vec3 projCoords, vec4 atlasParams,
                      float quality, float filterRadius)
{
    // Every tap stays half a texel inside the tile so the hardware 2x2 compare never reads a neighbour
    vec2 texelSize = vec2(1.0 / atlasSize);
    vec2 tileMin = atlasParams.xy / atlasSize + texelSize * 0.5;
    vec2 tileMax = (atlasParams.xy + atlasParams.z) / atlasSize - texelSize * 0.5;
    vec2 atlasCoords = clamp((atlasParams.xy + projCoords.xy * atlasParams.z) / atlasSize, tileMin, tileMax);

    // Hardware depth comparison returns the filtered fraction that is lit
    float lit;
    if (quality < 0.5) {
        lit = texture(shadowAtlas, vec3(atlasCoords, projCoords.z));
    } else {
        float randomRotation = InterleavedGradientNoise(gl_FragCoord.xy) * 6.283185;
        float s = sin(randomRotation);
        float c = cos(randomRotation);
        mat2 rotationMatrix = mat2(c, -s, s, c);

        vec2 texelScale = texelSize * filterRadius;
        lit = 0.0;
        for (int i = 0; i < 8; i++) {
            vec2 offset = (rotationMatrix * POISSON_DISK_8[i]) * texelScale;
            lit += texture(shadowAtlas, vec3(clamp(atlasCoords + offset, tileMin, tileMax), projCoords.z));
        }
        lit /= 8.0;
    }

    return 1.0 - lit;
}
