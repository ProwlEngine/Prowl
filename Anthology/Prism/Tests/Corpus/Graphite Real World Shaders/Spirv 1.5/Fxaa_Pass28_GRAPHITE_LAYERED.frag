#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Fxaa"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Fxaa"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Fxaa"
#line 1 "Graphite/Internal/Fxaa.glsl"



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _EdgeThresholdMin; float _EdgeThresholdMax; float _SubpixelQuality; };




float Luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

float LumaAt(ivec2 texel, ivec2 offset)
{
    return Luma(texelFetch(Graphite_Source, ivec3(clamp(texel + offset, ivec2(0), ivec2(_Resolution) - 1), gl_ViewIndex), 0).rgb);
}

float LumaSample(vec2 uv) { return Luma(Graphite_SampleSource(uv).rgb); }

vec3 Fxaa311(vec2 texCoord)
{
    float quality[12] = float[12] (1.0, 1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0);
    const int iterations = 12;
    vec2 inverseScreenSize = 1.0 / _Resolution;
    ivec2 texel = ivec2(texCoord * _Resolution);
    vec3 colorCenter = texelFetch(Graphite_Source, ivec3(texel, gl_ViewIndex), 0).rgb;
    float lumaCenter = Luma(colorCenter);
    float lumaDown = LumaAt(texel, ivec2(0, - 1));
    float lumaUp = LumaAt(texel, ivec2(0, 1));
    float lumaLeft = LumaAt(texel, ivec2(- 1, 0));
    float lumaRight = LumaAt(texel, ivec2(1, 0));
    float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
    float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));
    float lumaRange = lumaMax - lumaMin;
    if (lumaRange < max(_EdgeThresholdMin, lumaMax * _EdgeThresholdMax))
        return colorCenter;

    float lumaDownLeft = LumaAt(texel, ivec2(- 1, - 1));
    float lumaUpRight = LumaAt(texel, ivec2(1, 1));
    float lumaUpLeft = LumaAt(texel, ivec2(- 1, 1));
    float lumaDownRight = LumaAt(texel, ivec2(1, - 1));
    float lumaDownUp = lumaDown + lumaUp;
    float lumaLeftRight = lumaLeft + lumaRight;
    float lumaLeftCorners = lumaDownLeft + lumaUpLeft;
    float lumaDownCorners = lumaDownLeft + lumaDownRight;
    float lumaRightCorners = lumaDownRight + lumaUpRight;
    float lumaUpCorners = lumaUpRight + lumaUpLeft;

    float edgeHorizontal = abs(- 2.0 * lumaLeft + lumaLeftCorners) + abs(- 2.0 * lumaCenter + lumaDownUp) * 2.0 + abs(- 2.0 * lumaRight + lumaRightCorners);
    float edgeVertical = abs(- 2.0 * lumaUp + lumaUpCorners) + abs(- 2.0 * lumaCenter + lumaLeftRight) * 2.0 + abs(- 2.0 * lumaDown + lumaDownCorners);
    bool isHorizontal = edgeHorizontal >= edgeVertical;
    float luma1 = isHorizontal ? lumaDown : lumaLeft;
    float luma2 = isHorizontal ? lumaUp : lumaRight;
    float gradient1 = luma1 - lumaCenter;
    float gradient2 = luma2 - lumaCenter;
    bool is1Steepest = abs(gradient1) >= abs(gradient2);
    float gradientScaled = 0.25 * max(abs(gradient1), abs(gradient2));
    float stepLength = isHorizontal ? inverseScreenSize.y : inverseScreenSize.x;
    float lumaLocalAverage;
    if (is1Steepest)
    {
        stepLength = - stepLength;
        lumaLocalAverage = 0.5 * (luma1 + lumaCenter);
    }
    else
    {
        lumaLocalAverage = 0.5 * (luma2 + lumaCenter);
    }

    vec2 currentUv = texCoord;
    if (isHorizontal)
        currentUv.y += stepLength * 0.5;
    else
        currentUv.x += stepLength * 0.5;

    vec2 offset = isHorizontal ? vec2(inverseScreenSize.x, 0.0) : vec2(0.0, inverseScreenSize.y);
    vec2 uv1 = currentUv - offset;
    vec2 uv2 = currentUv + offset;
    float lumaEnd1 = LumaSample(uv1) - lumaLocalAverage;
    float lumaEnd2 = LumaSample(uv2) - lumaLocalAverage;
    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;
    bool reachedBoth = reached1 && reached2;
    if (! reached1)
        uv1 -= offset;
    if (! reached2)
        uv2 += offset;
    if (! reachedBoth)
    {
        for (int i = 2; i < iterations; i ++)
        {
            if (! reached1)
                lumaEnd1 = LumaSample(uv1) - lumaLocalAverage;
            if (! reached2)
                lumaEnd2 = LumaSample(uv2) - lumaLocalAverage;
            reached1 = abs(lumaEnd1) >= gradientScaled;
            reached2 = abs(lumaEnd2) >= gradientScaled;
            reachedBoth = reached1 && reached2;
            if (! reached1)
                uv1 -= offset * quality[i];
            if (! reached2)
                uv2 += offset * quality[i];
            if (reachedBoth)
                break;
        }
    }

    float distance1 = isHorizontal ? (texCoord.x - uv1.x) : (texCoord.y - uv1.y);
    float distance2 = isHorizontal ? (uv2.x - texCoord.x) : (uv2.y - texCoord.y);
    bool isDirection1 = distance1 < distance2;
    float distanceFinal = min(distance1, distance2);
    float pixelOffset = - distanceFinal / (distance1 + distance2) + 0.5;
    bool isLumaCenterSmaller = lumaCenter < lumaLocalAverage;
    bool correctVariation = ( (isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0) != isLumaCenterSmaller;
    float finalOffset = correctVariation ? pixelOffset : 0.0;

    float lumaAverage = (1.0 / 12.0) * (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
    float subPixelOffset1 = clamp(abs(lumaAverage - lumaCenter) / lumaRange, 0.0, 1.0);
    float subPixelOffset2 = (- 2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
    finalOffset = max(finalOffset, subPixelOffset2 * subPixelOffset2 * _SubpixelQuality);

    vec2 finalUv = texCoord;
    if (isHorizontal)
        finalUv.y += finalOffset * stepLength;
    else
        finalUv.x += finalOffset * stepLength;
    return Graphite_SampleSource(finalUv).rgb;
}

void main()
{
    outColor = vec4(Fxaa311(vUV), Graphite_SampleSource(vUV).a);
}
#line 10 "Graphite/Fxaa"

