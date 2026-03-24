// Common GLSL shader source for Canvas + Slug rendering, shared across OpenGL-based samples.
// Slug algorithm by Eric Lengyel, MIT OR Apache-2.0.

namespace Common
{
    /// <summary>
    /// Contains GLSL 330 vertex and fragment shader source for the Canvas rendering system
    /// </summary>
    public static class CanvasShaderSource
    {
        /// <summary>
        /// GLSL 330 vertex shader with Slug attribute passthrough.
        /// </summary>
        public const string VertexShader = @"#version 330
uniform mat4 projection;

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;
layout(location = 3) in vec4 aSlugBand;      // bandScaleX, bandScaleY, bandOffsetX, bandOffsetY
layout(location = 4) in vec2 aSlugGlyphInfo;  // packedBandLoc, bandCount

out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragPos;
out vec4 fragSlugBand;
flat out vec2 fragSlugGlyph;

void main()
{
    fragTexCoord = aTexCoord;
    fragColor = aColor;
    fragPos = aPosition;
    fragSlugBand = aSlugBand;
    fragSlugGlyph = aSlugGlyphInfo;
    gl_Position = projection * vec4(aPosition, 0.0, 1.0);
}";

        /// <summary>
        /// GLSL 330 fragment shader with unified Canvas + Slug rendering.
        /// Slug mode is triggered when fragSlugGlyph.y (bandCount) > 0.
        /// </summary>
        public const string FragmentShader = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragPos;
in vec4 fragSlugBand;
flat in vec2 fragSlugGlyph;

out vec4 finalColor;

uniform sampler2D texture0;
uniform mat4 scissorMat;
uniform vec2 scissorExt;

uniform mat4 brushMat;
uniform int brushType;       // 0=none, 1=linear, 2=radial, 3=box
uniform vec4 brushColor1;
uniform vec4 brushColor2;
uniform vec4 brushParams;
uniform vec2 brushParams2;

uniform mat4 brushTextureMat;
uniform float dpiScale;

// Slug textures
uniform sampler2D slugCurveTexture;
uniform sampler2D slugBandTexture;
uniform vec2 slugCurveTexSize;
uniform vec2 slugBandTexSize;

// ============== Slug functions ==============

vec2 SlugFetchBand(float absTexelIndex) {
    float tx = mod(absTexelIndex, slugBandTexSize.x);
    float ty = floor(absTexelIndex / slugBandTexSize.x);
    vec2 uv = (vec2(tx, ty) + 0.5) / slugBandTexSize;
    return texture(slugBandTexture, uv).rg;
}

vec4 SlugFetchCurve(vec2 curveLoc) {
    vec2 uv = (curveLoc + 0.5) / slugCurveTexSize;
    return texture(slugCurveTexture, uv);
}

vec2 SlugCalcRootEligibility(float y1, float y2, float y3) {
    float s0 = (y1 > 0.0) ? 1.0 : 0.0;
    float s1 = (y2 > 0.0) ? 1.0 : 0.0;
    float s2 = (y3 > 0.0) ? 1.0 : 0.0;
    float ns0 = 1.0 - s0, ns1 = 1.0 - s1, ns2 = 1.0 - s2;

    float root1 = clamp(
        s0*ns1*ns2 + ns0*s1*ns2 + s0*s1*ns2 + s0*ns1*s2, 0.0, 1.0);
    float root2 = clamp(
        ns0*s1*ns2 + ns0*ns1*s2 + s0*ns1*s2 + ns0*s1*s2, 0.0, 1.0);

    return vec2(root1, root2);
}

vec2 SlugSolveHorizPoly(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw * 2.0 + p3;
    vec2 b = p12.xy - p12.zw;
    float ra = 1.0 / a.y;
    float rb = 0.5 / b.y;
    float d = sqrt(max(b.y * b.y - a.y * p12.y, 0.0));
    float t1 = (b.y - d) * ra;
    float t2 = (b.y + d) * ra;
    if (abs(a.y) < 0.0001) { t1 = p12.y * rb; t2 = t1; }
    return vec2(
        (a.x * t1 - b.x * 2.0) * t1 + p12.x,
        (a.x * t2 - b.x * 2.0) * t2 + p12.x);
}

vec2 SlugSolveVertPoly(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw * 2.0 + p3;
    vec2 b = p12.xy - p12.zw;
    float ra = 1.0 / a.x;
    float rb = 0.5 / b.x;
    float d = sqrt(max(b.x * b.x - a.x * p12.x, 0.0));
    float t1 = (b.x - d) * ra;
    float t2 = (b.x + d) * ra;
    if (abs(a.x) < 0.0001) { t1 = p12.x * rb; t2 = t1; }
    return vec2(
        (a.y * t1 - b.y * 2.0) * t1 + p12.y,
        (a.y * t2 - b.y * 2.0) * t2 + p12.y);
}

float SlugRender(vec2 renderCoord, vec4 bandTransform, vec2 glyphTexInfo) {
    float glyTexPackedXY = glyphTexInfo.x;
    float glyphBandTexX  = mod(glyTexPackedXY, slugBandTexSize.x);
    float glyphBandTexY  = floor(glyTexPackedXY / slugBandTexSize.x);
    float bandCount      = glyphTexInfo.y;
    float bandMax        = bandCount - 1.0;

    vec2 emsPerPixel = fwidth(renderCoord);
    vec2 pixelsPerEm = 1.0 / max(emsPerPixel, vec2(0.0001, 0.0001));

    vec2 bandPos   = renderCoord * bandTransform.xy + bandTransform.zw;
    float bandIndexY = clamp(floor(bandPos.y), 0.0, bandMax);
    float bandIndexX = clamp(floor(bandPos.x), 0.0, bandMax);

    float glyphBaseTexel = glyphBandTexY * slugBandTexSize.x + glyphBandTexX;

    // Horizontal bands
    float xcov = 0.0, xwgt = 0.0;
    vec2 hBH = SlugFetchBand(glyphBaseTexel + bandIndexY);
    for (float ci = 0.0; ci < hBH.r; ci += 1.0) {
        vec2 cLoc = SlugFetchBand(hBH.g + ci);
        vec4 p12 = SlugFetchCurve(cLoc) - vec4(renderCoord, renderCoord);
        vec2 p3  = SlugFetchCurve(vec2(cLoc.x + 1.0, cLoc.y)).xy - renderCoord;
        if (max(max(p12.x, p12.z), p3.x) * pixelsPerEm.x < -0.5) break;
        vec2 elig = SlugCalcRootEligibility(p12.y, p12.w, p3.y);
        if (elig.x + elig.y > 0.0) {
            vec2 r = SlugSolveHorizPoly(p12, p3) * pixelsPerEm.x;
            if (elig.x > 0.5) { xcov += clamp(r.x+0.5, 0.0, 1.0); xwgt = max(xwgt, clamp(1.0-abs(r.x)*2.0, 0.0, 1.0)); }
            if (elig.y > 0.5) { xcov -= clamp(r.y+0.5, 0.0, 1.0); xwgt = max(xwgt, clamp(1.0-abs(r.y)*2.0, 0.0, 1.0)); }
        }
    }

    // Vertical bands
    float ycov = 0.0, ywgt = 0.0;
    vec2 vBH = SlugFetchBand(glyphBaseTexel + bandCount + bandIndexX);
    for (float vi = 0.0; vi < vBH.r; vi += 1.0) {
        vec2 cLoc = SlugFetchBand(vBH.g + vi);
        vec4 p12 = SlugFetchCurve(cLoc) - vec4(renderCoord, renderCoord);
        vec2 p3  = SlugFetchCurve(vec2(cLoc.x + 1.0, cLoc.y)).xy - renderCoord;
        if (max(max(p12.y, p12.w), p3.y) * pixelsPerEm.y < -0.5) break;
        vec2 elig = SlugCalcRootEligibility(p12.x, p12.z, p3.x);
        if (elig.x + elig.y > 0.0) {
            vec2 r = SlugSolveVertPoly(p12, p3) * pixelsPerEm.y;
            if (elig.x > 0.5) { ycov -= clamp(r.x+0.5, 0.0, 1.0); ywgt = max(ywgt, clamp(1.0-abs(r.x)*2.0, 0.0, 1.0)); }
            if (elig.y > 0.5) { ycov += clamp(r.y+0.5, 0.0, 1.0); ywgt = max(ywgt, clamp(1.0-abs(r.y)*2.0, 0.0, 1.0)); }
        }
    }

    float coverage = max(
        abs(xcov * xwgt + ycov * ywgt) / max(xwgt + ywgt, 0.0001),
        min(abs(xcov), abs(ycov)));
    return clamp(coverage, 0.0, 1.0);
}

// ============== Canvas functions ==============

float calculateBrushFactor() {
    if (brushType == 0) return 0.0;
    vec2 logicalPos = fragPos / dpiScale;
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
        return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
    }
    return 0.0;
}

float scissorMask(vec2 p) {
    if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
    vec2 logicalP = p / dpiScale;
    vec2 transformedPoint = (scissorMat * vec4(logicalP, 0.0, 1.0)).xy;
    vec2 logicalExt = scissorExt / dpiScale;
    vec2 distanceFromEdges = abs(transformedPoint) - logicalExt;
    float halfPixelLogical = 0.5 / dpiScale;
    vec2 smoothEdges = vec2(halfPixelLogical) - distanceFromEdges;
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

void main()
{
    // Slug mode: bandCount > 0 means this is a Slug glyph
    if (fragSlugGlyph.y > 0.0) {
        float coverage = SlugRender(fragTexCoord, fragSlugBand, fragSlugGlyph);
        finalColor = vec4(fragColor.rgb * coverage, coverage * fragColor.a);
        return;
    }

    float mask = scissorMask(fragPos);
    vec4 color = fragColor;

    if (brushType > 0) {
        float factor = calculateBrushFactor();
        color = mix(brushColor1, brushColor2, factor);
    }

    // Text mode: UV >= 2.0
    if (fragTexCoord.x >= 2.0) {
        finalColor = color * texture(texture0, fragTexCoord - vec2(2.0)) * mask;
        return;
    }

    // Edge anti-aliasing
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);

    vec2 logicalPos = fragPos / dpiScale;
    finalColor = color * texture(texture0, (brushTextureMat * vec4(logicalPos, 0.0, 1.0)).xy) * edgeAlpha * mask;
}";
    }
}
