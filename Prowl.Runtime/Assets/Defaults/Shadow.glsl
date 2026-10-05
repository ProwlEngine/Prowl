// Shadow sampling utilities for deferred lighting
// Shared by DirectionalLight, SpotLight, and PointLight shaders

// Screen space noise, used to jitter ray marches
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
//   quality: 0 = hard (one hardware 2x2 compare), 1 = soft (5x5 tent filter)
float SampleShadowPCF(sampler2DShadow shadowAtlas, float atlasSize, vec3 projCoords, vec4 atlasParams, float quality)
{
    // Every tap stays half a texel inside the tile so the hardware 2x2 compare never reads a neighbour
    float invSize = 1.0 / atlasSize;
    vec2 tileMin = (atlasParams.xy + 0.5) * invSize;
    vec2 tileMax = (atlasParams.xy + atlasParams.z - 0.5) * invSize;
    vec2 texelCoords = atlasParams.xy + projCoords.xy * atlasParams.z;

    // Hardware depth comparison returns the bilinearly filtered fraction that is lit
    if (quality < 0.5)
        return 1.0 - texture(shadowAtlas, vec3(clamp(texelCoords * invSize, tileMin, tileMax), projCoords.z));

    // Three bilinear taps per axis reproduce a five texel tent exactly, so nine taps cover 5x5 with no noise.
    // Per axis weights are 4 - 3f, 7 and 1 + 3f (they sum to 12), placed so each tap's bilinear split
    // lands on the tent's texel weights.
    vec2 base = floor(texelCoords + 0.5);
    vec2 f = texelCoords + 0.5 - base;
    base -= 0.5;

    vec2 w0 = 4.0 - 3.0 * f;
    vec2 w2 = 1.0 + 3.0 * f;
    vec2 o0 = (3.0 - 2.0 * f) / w0 - 2.0;
    vec2 o1 = (3.0 + f) / 7.0;
    vec2 o2 = f / w2 + 2.0;

    vec3 wx = vec3(w0.x, 7.0, w2.x);
    vec3 wy = vec3(w0.y, 7.0, w2.y);
    vec3 ox = vec3(o0.x, o1.x, o2.x);
    vec3 oy = vec3(o0.y, o1.y, o2.y);

    float lit = 0.0;
    for (int j = 0; j < 3; j++) {
        for (int i = 0; i < 3; i++) {
            vec2 uv = clamp((base + vec2(ox[i], oy[j])) * invSize, tileMin, tileMax);
            lit += wx[i] * wy[j] * texture(shadowAtlas, vec3(uv, projCoords.z));
        }
    }

    return 1.0 - lit / 144.0;
}
