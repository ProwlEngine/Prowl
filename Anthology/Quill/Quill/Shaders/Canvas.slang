// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// The single source of truth for the Quill canvas shader. Every backend's shader is generated from
// this file by Quill.ShaderGen on build; do not edit the generated copies in the samples.

module Canvas;

struct VertexInput
{
    float2 aPosition : POSITION0;
    float2 aTexCoord : TEXCOORD0;
    float4 aColor : COLOR0;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 fragTexCoord : TEXCOORD0;
    float4 fragColor : COLOR0;
    float2 fragPos : TEXCOORD1;
};

uniform float4x4 projection;

uniform float4x4 scissorMat;
uniform float2 scissorExt;

uniform float4x4 brushMat;
uniform int brushType;          // 0=none, 1=linear, 2=radial, 3=box
uniform float4 brushColor1;
uniform float4 brushColor2;
uniform float4 brushParams;
uniform float2 brushParams2;

uniform float4x4 brushTextureMat;
uniform float dpiScale;

// 1 / font atlas size, so the text distance-field screen range is correct at any zoom.
uniform float2 atlasTexelSize;

// Backdrop blur
uniform float2 viewportSize;      // framebuffer size in pixels, for screen->uv mapping
uniform float backdropBlurAmount; // > 0 when the current fill is frosted glass
uniform int backdropFlipY;        // 1 on bottom-left-origin backends, which store the scene flipped

uniform Sampler2D texture0;
uniform Sampler2D fontTexture;     // dedicated font-atlas unit, so text batches with shapes
uniform Sampler2D backdropTexture; // blurred copy of the framebuffer behind the shape

// Width of the signed-distance range in atlas texels. Must match Scribe's FontSystem.DistanceRange.
static const float sdfPxRange = 4.0;


[shader("vertex")]
VertexOutput Vertex(VertexInput input)
{
    VertexOutput output;
    output.fragTexCoord = input.aTexCoord;
    output.fragColor = input.aColor;
    output.fragPos = input.aPosition;
    output.position = mul(projection, float4(input.aPosition, 0.0, 1.0));
    return output;
}


float calculateBrushFactor(float2 fragPos)
{
    float2 logicalPos = fragPos / dpiScale;
    float2 transformedPoint = (mul(brushMat, float4(logicalPos, 0.0, 1.0))).xy;

    if (brushType == 1)
    {
        float2 startPoint = brushParams.xy;
        float2 endPoint = brushParams.zw;
        float2 line = endPoint - startPoint;
        float lineLength = length(line);
        if (lineLength < 0.001) return 0.0;
        return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
    }
    if (brushType == 2)
    {
        float2 center = brushParams.xy;
        return clamp(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
    }
    if (brushType == 3)
    {
        float2 center = brushParams.xy;
        float2 halfSize = brushParams.zw;
        float radius = brushParams2.x;
        float feather = brushParams2.y;
        if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;

        float2 q = abs(transformedPoint - center) - (halfSize - float2(radius));
        float dist = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
        return smoothstep(-feather * 0.5, feather * 0.5, dist);
    }
    return 0.0;
}


float scissorMask(float2 p)
{
    if (scissorExt.x < 0.0 || scissorExt.y < 0.0)
        return 1.0;

    float2 logicalP = p / dpiScale;
    float2 transformedPoint = (mul(scissorMat, float4(logicalP, 0.0, 1.0))).xy;
    float2 logicalExt = scissorExt / dpiScale;
    float2 distanceFromEdges = abs(transformedPoint) - logicalExt;
    float halfPixelLogical = 0.5 / dpiScale;
    float2 smoothEdges = float2(halfPixelLogical) - distanceFromEdges;

    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}


// Screen-space width (in pixels) that one distance-field unit spans at this fragment. Derived from
// texture-coordinate derivatives, so glyphs stay crisp at any canvas zoom or DPI automatically.
float sdfScreenPxRange(float2 uv)
{
    float2 unitRange = float2(sdfPxRange) * atlasTexelSize;
    float2 screenTexSize = float2(1.0) / fwidth(uv);
    return max(0.5 * dot(unitRange, screenTexSize), 1.0);
}


[shader("fragment")]
float4 Fragment(VertexOutput input) : SV_Target
{
    float mask = scissorMask(input.fragPos);

    float4 color = input.fragColor;

    if (brushType > 0)
    {
        float factor = calculateBrushFactor(input.fragPos);
        color = lerp(brushColor1, brushColor2, factor);
    }

    // Text mode: UV >= 2.0. The glyph atlas holds a single-channel signed distance field (replicated
    // across RGB); reconstruct sharp coverage from it.
    if (input.fragTexCoord.x >= 2.0)
    {
        float2 uv = input.fragTexCoord - float2(2.0);
        float sd = fontTexture.Sample(uv).r;
        float screenPxDistance = sdfScreenPxRange(uv) * (sd - 0.5);
        float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
        return color * coverage * mask;
    }

    // Edge anti-aliasing: coverage is baked into the geometry (fringe vertices) and carried in
    // fragTexCoord.x (1 = solid core, 0 = outer fringe edge).
    float edgeAlpha = clamp(input.fragTexCoord.x, 0.0, 1.0);

    float2 logicalPos = input.fragPos / dpiScale;
    float4 fill = color * texture0.Sample(mul(brushTextureMat, float4(logicalPos, 0.0, 1.0)).xy);

    // Backdrop blur: composite the fill over the blurred framebuffer behind the shape.
    if (backdropBlurAmount > 0.0)
    {
        float2 uv = input.fragPos / viewportSize;
        if (backdropFlipY == 1)
            uv.y = 1.0 - uv.y;
        float3 blurred = backdropTexture.Sample(uv).rgb;
        // fill is premultiplied; over-composite it onto the opaque blurred backdrop
        float3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;
        return float4(outRgb, 1.0) * edgeAlpha * mask;
    }

    return fill * edgeAlpha * mask;
}
