#version 460 core

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D currentColorTex;
layout(set = 0, binding = 1) uniform sampler2D historyColorTex;
layout(set = 0, binding = 2) uniform sampler2D motionVectorTex;

layout(set = 0, binding = 3) uniform TAAParams
{
    vec2 jitter;
    vec2 screenSize;
} taaParams;

const float VARIANCE_CLIP_GAMMA = 1.25;
const float HISTORY_WEIGHT_MAX = 0.9;
const float HISTORY_WEIGHT_MIN = 0.5;
const float VELOCITY_REJECT_PIXELS = 24.0;

vec3 RGBToYCoCg(vec3 c)
{
    float y = dot(c, vec3(0.25, 0.5, 0.25));
    float co = dot(c, vec3(0.5, 0.0, -0.5));
    float cg = dot(c, vec3(-0.25, 0.5, -0.25));
    return vec3(y, co, cg);
}

vec3 YCoCgToRGB(vec3 c)
{
    float y = c.x;
    float co = c.y;
    float cg = c.z;
    return vec3(y + co - cg, y + cg, y - co - cg);
}

// Clips historyColor toward center along the ray from center to historyColor, stopping at the box boundary.
vec3 ClipToAABB(vec3 boxMin, vec3 boxMax, vec3 center, vec3 historyColor)
{
    vec3 extents = 0.5 * (boxMax - boxMin) + 1e-6;
    vec3 rayDir = historyColor - center;
    vec3 rayDirUnit = rayDir / extents;
    vec3 absUnit = abs(rayDirUnit);
    float maxUnit = max(absUnit.x, max(absUnit.y, absUnit.z));

    if (maxUnit > 1.0)
    {
        return center + rayDir / maxUnit;
    }

    return historyColor;
}

void main()
{
    vec2 texelSize = 1.0 / taaParams.screenSize;

    vec2 motion = texture(motionVectorTex, uv).xy;
    vec2 historyUV = uv - motion;

    // Un-jitter the current frame sample position before building the neighborhood clamp box.
    vec2 sampleUV = uv - taaParams.jitter * texelSize;

    vec3 colorTL = texture(currentColorTex, sampleUV + vec2(-1.0, -1.0) * texelSize).rgb;
    vec3 colorTM = texture(currentColorTex, sampleUV + vec2( 0.0, -1.0) * texelSize).rgb;
    vec3 colorTR = texture(currentColorTex, sampleUV + vec2( 1.0, -1.0) * texelSize).rgb;
    vec3 colorML = texture(currentColorTex, sampleUV + vec2(-1.0,  0.0) * texelSize).rgb;
    vec3 colorMM = texture(currentColorTex, sampleUV).rgb;
    vec3 colorMR = texture(currentColorTex, sampleUV + vec2( 1.0,  0.0) * texelSize).rgb;
    vec3 colorBL = texture(currentColorTex, sampleUV + vec2(-1.0,  1.0) * texelSize).rgb;
    vec3 colorBM = texture(currentColorTex, sampleUV + vec2( 0.0,  1.0) * texelSize).rgb;
    vec3 colorBR = texture(currentColorTex, sampleUV + vec2( 1.0,  1.0) * texelSize).rgb;

    vec3 ycocgTL = RGBToYCoCg(colorTL);
    vec3 ycocgTM = RGBToYCoCg(colorTM);
    vec3 ycocgTR = RGBToYCoCg(colorTR);
    vec3 ycocgML = RGBToYCoCg(colorML);
    vec3 ycocgMM = RGBToYCoCg(colorMM);
    vec3 ycocgMR = RGBToYCoCg(colorMR);
    vec3 ycocgBL = RGBToYCoCg(colorBL);
    vec3 ycocgBM = RGBToYCoCg(colorBM);
    vec3 ycocgBR = RGBToYCoCg(colorBR);

    vec3 sum = ycocgTL + ycocgTM + ycocgTR + ycocgML + ycocgMM + ycocgMR + ycocgBL + ycocgBM + ycocgBR;
    vec3 sumSq = ycocgTL * ycocgTL + ycocgTM * ycocgTM + ycocgTR * ycocgTR +
                 ycocgML * ycocgML + ycocgMM * ycocgMM + ycocgMR * ycocgMR +
                 ycocgBL * ycocgBL + ycocgBM * ycocgBM + ycocgBR * ycocgBR;

    const float tapCount = 9.0;
    vec3 mean = sum / tapCount;
    vec3 variance = max(sumSq / tapCount - mean * mean, vec3(0.0));
    vec3 stdDev = sqrt(variance);

    vec3 boxMin = mean - stdDev * VARIANCE_CLIP_GAMMA;
    vec3 boxMax = mean + stdDev * VARIANCE_CLIP_GAMMA;

    vec3 currentColor = colorMM;

    bool historyOffscreen = any(lessThan(historyUV, vec2(0.0))) || any(greaterThan(historyUV, vec2(1.0)));

    vec3 finalColor;

    if (historyOffscreen)
    {
        finalColor = currentColor;
    }
    else
    {
        vec3 historyColorRaw = texture(historyColorTex, historyUV).rgb;
        vec3 historyYCoCg = RGBToYCoCg(historyColorRaw);

        vec3 clippedYCoCg = ClipToAABB(boxMin, boxMax, mean, historyYCoCg);
        vec3 clippedHistoryColor = YCoCgToRGB(clippedYCoCg);

        float velocityPixels = length(motion * taaParams.screenSize);
        float velocityConfidence = clamp(velocityPixels / VELOCITY_REJECT_PIXELS, 0.0, 1.0);
        float historyWeight = mix(HISTORY_WEIGHT_MAX, HISTORY_WEIGHT_MIN, velocityConfidence);

        finalColor = mix(currentColor, clippedHistoryColor, historyWeight);
    }

    outColor = vec4(finalColor, 1.0);
}
