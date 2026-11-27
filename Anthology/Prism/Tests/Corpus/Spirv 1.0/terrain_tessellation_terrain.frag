#version 460 core

layout(location = 0) in vec3 fWorldPos;
layout(location = 1) in vec3 fNormal;
layout(location = 2) in vec2 fUV;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform sampler2D grassAlbedo;
layout(set = 1, binding = 1) uniform sampler2D grassNormal;
layout(set = 1, binding = 2) uniform sampler2D rockAlbedo;
layout(set = 1, binding = 3) uniform sampler2D rockNormal;
layout(set = 1, binding = 4) uniform sampler2D snowAlbedo;
layout(set = 1, binding = 5) uniform sampler2D snowNormal;

layout(set = 1, binding = 6) uniform LightingParams
{
    vec4 lightDirection;
    vec4 lightColor;
    vec4 cameraWorldPos;
    float triplanarSharpness;
    float texelScale;
    float snowHeightStart;
    float snowHeightEnd;
    float rockSlopeStart;
    float rockSlopeEnd;
    float specularPower;
    float padding2;
} lighting;

vec3 triplanarWeights(vec3 normal)
{
    vec3 blend = pow(abs(normal), vec3(lighting.triplanarSharpness));
    return blend / max(blend.x + blend.y + blend.z, 0.0001);
}

vec3 triplanarSample(sampler2D tex, vec3 worldPos, vec3 weights, float scale)
{
    vec3 colorX = texture(tex, worldPos.yz * scale).rgb;
    vec3 colorY = texture(tex, worldPos.xz * scale).rgb;
    vec3 colorZ = texture(tex, worldPos.xy * scale).rgb;
    return colorX * weights.x + colorY * weights.y + colorZ * weights.z;
}

void main()
{
    vec3 normal = normalize(fNormal);
    vec3 weights = triplanarWeights(normal);

    vec3 grassColor = triplanarSample(grassAlbedo, fWorldPos, weights, lighting.texelScale);
    vec3 rockColor = triplanarSample(rockAlbedo, fWorldPos, weights, lighting.texelScale);
    vec3 snowColor = triplanarSample(snowAlbedo, fWorldPos, weights, lighting.texelScale);

    vec3 grassNormalTS = triplanarSample(grassNormal, fWorldPos, weights, lighting.texelScale) * 2.0 - 1.0;
    vec3 rockNormalTS = triplanarSample(rockNormal, fWorldPos, weights, lighting.texelScale) * 2.0 - 1.0;
    vec3 snowNormalTS = triplanarSample(snowNormal, fWorldPos, weights, lighting.texelScale) * 2.0 - 1.0;

    float slope = 1.0 - clamp(dot(normal, vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
    float rockFactor = smoothstep(lighting.rockSlopeStart, lighting.rockSlopeEnd, slope);
    float snowFactor = smoothstep(lighting.snowHeightStart, lighting.snowHeightEnd, fWorldPos.y);
    snowFactor *= (1.0 - rockFactor);

    vec3 albedo = mix(grassColor, rockColor, rockFactor);
    albedo = mix(albedo, snowColor, snowFactor);

    vec3 blendedNormalTS = mix(grassNormalTS, rockNormalTS, rockFactor);
    blendedNormalTS = mix(blendedNormalTS, snowNormalTS, snowFactor);
    blendedNormalTS = normalize(blendedNormalTS);

    vec3 finalNormal = normalize(normal + blendedNormalTS * 0.5);

    vec3 lightDir = normalize(-lighting.lightDirection.xyz);
    vec3 viewDir = normalize(lighting.cameraWorldPos.xyz - fWorldPos);
    vec3 halfVec = normalize(lightDir + viewDir);

    float ndotl = max(dot(finalNormal, lightDir), 0.0);
    float ndoth = max(dot(finalNormal, halfVec), 0.0);
    float specular = pow(ndoth, lighting.specularPower) * step(0.0001, ndotl);

    vec3 ambient = albedo * 0.15;
    vec3 diffuse = albedo * lighting.lightColor.rgb * ndotl;
    vec3 specularColor = lighting.lightColor.rgb * specular * 0.3;

    outColor = vec4(ambient + diffuse + specularColor, 1.0);
}
