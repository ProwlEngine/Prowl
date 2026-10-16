#version 460 core

layout(location = 0) in vec2 fragTexCoord;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in vec2 fragPos;

layout(location = 3) out vec4 finalColor;

layout(set = 2, binding = 1) uniform sampler2D texture0;
layout(set = 2, binding = 2) uniform sampler2D fontTexture;     // dedicated font-atlas sampler, so text batches with shapes

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    mat4 scissorMat;
    vec2 scissorExt;

    mat4 brushMat;
    int brushType;
    vec4 brushColor1;
    vec4 brushColor2;
    vec4 brushParams;
    vec2 brushParams2;

    mat4 brushTextureMat;
    float dpiScale;

    // Backdrop blur
    vec2 viewportSize;         // framebuffer size in pixels
    float backdropBlurAmount;  // > 0 when this fill is frosted glass
    int backdropFlipY;         // 1 to flip the backdrop sample vertically
};

layout(set = 2, binding = 3) uniform sampler2D backdropTexture; // blurred copy of the scene behind the shape

// ============== Canvas functions ==============

float calculateBrushFactor() {
    vec2 logicalPos = fragPos / max(dpiScale, 0.001);
    vec2 transformedPoint = (brushMat * vec4(logicalPos, 0.0, 1.0)).xy;

    if (brushType == 1) {
        vec2 startPoint = brushParams.xy; vec2 endPoint = brushParams.zw;
        vec2 line = endPoint - startPoint; float lineLength = length(line);
        if (lineLength < 0.001) return 0.0;
        return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
    }
    if (brushType == 2) {
        vec2 center = brushParams.xy;
        return clamp(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
    }
    if (brushType == 3) {
        vec2 center = brushParams.xy; vec2 halfSize = brushParams.zw;
        float radius = brushParams2.x; float feather = brushParams2.y;
        if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
        vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
        float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
        return smoothstep(-feather * 0.5, feather * 0.5, dist);
    }
    return 0.0;
}

float scissorMask(vec2 p) {
    if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
    float dpi = max(dpiScale, 0.001);
    vec2 logicalP = p / dpi;
    vec2 transformedPoint = (scissorMat * vec4(logicalP, 0.0, 1.0)).xy;
    vec2 logicalExt = scissorExt / dpi;
    vec2 distanceFromEdges = abs(transformedPoint) - logicalExt;
    float halfPixelLogical = 0.5 / dpi;
    vec2 smoothEdges = vec2(halfPixelLogical) - distanceFromEdges;
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

// Single-channel SDF text: width of the distance range in atlas texels (matches Scribe's
// FontSystem.DistanceRange), and the screen-space span of one unit at this fragment.
const float sdfPxRange = 4.0;
float sdfScreenPxRange(vec2 uv) {
    vec2 unitRange = vec2(sdfPxRange) / vec2(textureSize(fontTexture, 0));
    vec2 screenTexSize = vec2(1.0) / fwidth(uv);
    return max(0.5 * dot(unitRange, screenTexSize), 1.0);
}

float sampleCoverage(vec2 uv) {
    float d = texture(fontTexture, uv).r;
    float sd = (d - 0.5) * sdfScreenPxRange(uv);
    return clamp(sd + 0.5, 0.0, 1.0);
}

void main()
{
    float mask = scissorMask(fragPos);
    vec4 color = fragColor;

    if (brushType > 0) {
        float factor = calculateBrushFactor();
        color = mix(brushColor1, brushColor2, factor);
    }

    // SDF text mode: UV.x >= 2.0. The atlas holds a single-channel signed distance field
    // (replicated across RGB); reconstruct sharp coverage from it.
    if (fragTexCoord.x >= 2.0) {
        vec2 uv = fragTexCoord - vec2(2.0);

        // Basic SDF Text
        finalColor = color * sampleCoverage(uv) * mask;
        return;

        // Sub-Pixel SDF Text - Needs a Dual Blend or something? It does sorta work but not well enough.
        //vec2 uvPerScreenX = dFdx(uv);
        //vec2 shift = uvPerScreenX / 3.0;
        //
        //vec3 coverage = vec3(sampleCoverage(uv - shift), sampleCoverage(uv), sampleCoverage(uv + shift));

        //float alpha = (coverage.x + coverage.y + coverage.z) / 3.0;
        //finalColor = vec4(color.rgb * coverage, alpha) * mask;
        //return;
    }

    // Edge anti-aliasing: coverage is baked into the geometry (fringe vertices) and carried
    // in fragTexCoord.x (1 = solid core, 0 = outer fringe edge).
    float edgeAlpha = clamp(fragTexCoord.x, 0.0, 1.0);

    float dpi = max(dpiScale, 0.001);
    vec2 logicalPos = fragPos / dpi;
    vec4 fill = color * texture(texture0, (brushTextureMat * vec4(logicalPos, 0.0, 1.0)).xy);

    // Backdrop blur: composite the fill over the blurred scene behind the shape.
    if (backdropBlurAmount > 0.0) {
        vec2 uv = fragPos / viewportSize;
        if (backdropFlipY == 1) uv.y = 1.0 - uv.y;
        vec3 blurred = texture(backdropTexture, uv).rgb;
        vec3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;  // fill is premultiplied
        finalColor = vec4(outRgb, 1.0) * edgeAlpha * mask;
        return;
    }

    finalColor = fill * edgeAlpha * mask;
}
