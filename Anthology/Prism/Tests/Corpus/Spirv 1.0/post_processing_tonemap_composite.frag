#version 460 core

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D bloomColor;
layout(set = 0, binding = 2) uniform sampler3D colorGradeLUT;

layout(push_constant) uniform TonemapParams
{
    float exposure;
    float bloomIntensity;
    float gradingStrength;
    float vignetteIntensity;
    float grainIntensity;
    float time;
} params;

vec3 acesFilmic(vec3 color)
{
    vec3 numerator = color * (2.51 * color + 0.03);
    vec3 denominator = color * (2.43 * color + 0.59) + 0.14;
    return clamp(numerator / denominator, 0.0, 1.0);
}

float hash(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main()
{
    vec3 color = texture(sceneColor, uv).rgb;
    vec3 bloom = texture(bloomColor, uv).rgb;

    color *= exp2(params.exposure);
    color += bloom * params.bloomIntensity;

    vec3 tonemapped = acesFilmic(color);

    vec3 graded = texture(colorGradeLUT, tonemapped).rgb;
    tonemapped = mix(tonemapped, graded, params.gradingStrength);

    vec2 centered = uv - vec2(0.5);
    float dist = length(centered);
    float falloff = smoothstep(0.3, 0.8, dist);
    float vignette = 1.0 - falloff * params.vignetteIntensity;
    tonemapped *= vignette;

    float noise = hash(uv * 512.0 + params.time);
    float grain = (noise - 0.5) * params.grainIntensity;
    tonemapped += grain;

    outColor = vec4(clamp(tonemapped, 0.0, 1.0), 1.0);
}
