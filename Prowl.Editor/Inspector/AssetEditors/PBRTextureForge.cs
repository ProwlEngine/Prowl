// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using ImageMagick;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>
/// GPU-accelerated PBR texture generation: derive Height/Normal/Edge/AO/Metallic/Smoothness
/// maps from one or more source textures via embedded fragment shaders.
/// </summary>
/// <remarks>
/// Each public method runs a chain of fullscreen passes through pooled <see cref="RenderTexture"/>s
/// and returns a fresh <see cref="Texture2D"/> with the result. Kernels are compiled lazily
/// the first time they're used. Designed for the texture-asset inspector — no project-asset
/// integration; export is via <see cref="SavePng"/>.
/// </remarks>
public static class PBRTextureForge
{
    // ─── Universal vertex shader (matches Prowl fullscreen-quad attribute layout) ─────
    private const string VERTEX_SRC = @"
layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;
out vec2 vUV;
void main() {
    vUV = vertexTexCoord;
    gl_Position = vec4(vertexPosition, 1.0);
}
";

    // ─── Photoshop-style HSL helpers (used by Metallic/Smoothness color matching) ────
    private const string HSL_LIB = @"
vec3 RGBToHSL(vec3 c) {
    float fmin = min(min(c.r, c.g), c.b);
    float fmax = max(max(c.r, c.g), c.b);
    float delta = fmax - fmin;
    vec3 hsl = vec3(0.0);
    hsl.z = (fmax + fmin) * 0.5;
    if (delta != 0.0) {
        hsl.y = hsl.z < 0.5 ? delta / (fmax + fmin) : delta / (2.0 - fmax - fmin);
        float dr = (((fmax - c.r) / 6.0) + (delta / 2.0)) / delta;
        float dg = (((fmax - c.g) / 6.0) + (delta / 2.0)) / delta;
        float db = (((fmax - c.b) / 6.0) + (delta / 2.0)) / delta;
        if (c.r == fmax)      hsl.x = db - dg;
        else if (c.g == fmax) hsl.x = (1.0/3.0) + dr - db;
        else                  hsl.x = (2.0/3.0) + dg - dr;
        if (hsl.x < 0.0)      hsl.x += 1.0;
        else if (hsl.x > 1.0) hsl.x -= 1.0;
    }
    return hsl;
}
";

    // ─── Kernel: cosine-weighted directional blur ─────────────────────────────────────
    // Workhorse for all octave-pyramid generators (Height/Normal/Edge from anything).
    // Samples are weighted by raised cosine over [-N, +N], producing a tent-like blur.
    private const string FRAG_DIR_BLUR = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform vec2 _ImageSize;
uniform vec2 _BlurDirection;
uniform float _BlurSpread;
uniform int _BlurSamples;
uniform float _BlurContrast;
uniform int _Desaturate;
void main() {
    vec2 pixelSize = 1.0 / _ImageSize;
    int n = _BlurSamples;
    int total = n * 2;
    vec4 acc = vec4(0.0);
    for (int i = -n; i <= n; ++i) {
        float w = cos((float(i) / float(total)) * 6.28318530718) * 0.5 + 0.5;
        vec2 uv = vUV + pixelSize * _BlurDirection * float(i) * _BlurSpread;
        vec4 s = texture(_MainTex, uv);
        if (_Desaturate > 0) {
            float l = s.x * 0.3 + s.y * 0.5 + s.z * 0.2;
            s = vec4(l, l, l, s.w);
        }
        acc += vec4(s.xyz * w, w);
    }
    acc.xyz *= 1.0 / acc.w;
    acc.xyz = clamp((acc.xyz - 0.5) * _BlurContrast + 0.5, 0.0, 1.0);
    outColor = vec4(acc.xyz, 1.0);
}
";

    // ─── Kernel: desaturate (luminance) ──────────────────────────────────────────────
    private const string FRAG_DESATURATE = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
void main() {
    vec3 c = texture(_MainTex, vUV).xyz;
    float l = c.x * 0.3 + c.y * 0.5 + c.z * 0.2;
    outColor = vec4(l, l, l, 1.0);
}
";

    // ─── Kernel: simple texture copy / passthrough ───────────────────────────────────
    private const string FRAG_PASSTHROUGH = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
void main() { outColor = texture(_MainTex, vUV); }
";

    // ─── Kernel: combine 7-octave pyramid into final height ──────────────────────────
    // Subtracts per-octave from average, scales by per-octave contrast, weights and
    // sums, then applies final tone-map (contrast/bias/gain split-curve).
    private const string FRAG_COMBINE_HEIGHT = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _AvgTex;
uniform sampler2D _BlurTex0;
uniform sampler2D _BlurTex1;
uniform sampler2D _BlurTex2;
uniform sampler2D _BlurTex3;
uniform sampler2D _BlurTex4;
uniform sampler2D _BlurTex5;
uniform sampler2D _BlurTex6;
uniform float _Blur0Weight; uniform float _Blur0Contrast;
uniform float _Blur1Weight; uniform float _Blur1Contrast;
uniform float _Blur2Weight; uniform float _Blur2Contrast;
uniform float _Blur3Weight; uniform float _Blur3Contrast;
uniform float _Blur4Weight; uniform float _Blur4Contrast;
uniform float _Blur5Weight; uniform float _Blur5Contrast;
uniform float _Blur6Weight; uniform float _Blur6Contrast;
uniform float _FinalContrast;
uniform float _FinalBias;
uniform float _FinalGain;

vec4 octave(sampler2D tex, float avg, float contrast, float weight) {
    vec3 v = pow(max(texture(tex, vUV).xyz, vec3(0.0)), vec3(0.45));
    return vec4((v - avg) * contrast + 0.5, 1.0) * weight;
}
void main() {
    float avg = pow(max(texture(_AvgTex, vUV).x, 0.0), 0.45);
    vec4 h  = octave(_BlurTex0, avg, _Blur0Contrast, _Blur0Weight);
    h += octave(_BlurTex1, avg, _Blur1Contrast, _Blur1Weight);
    h += octave(_BlurTex2, avg, _Blur2Contrast, _Blur2Weight);
    h += octave(_BlurTex3, avg, _Blur3Contrast, _Blur3Weight);
    h += octave(_BlurTex4, avg, _Blur4Contrast, _Blur4Weight);
    h += octave(_BlurTex5, avg, _Blur5Contrast, _Blur5Weight);
    h += octave(_BlurTex6, avg, _Blur6Contrast, _Blur6Weight);
    h *= 1.0 / max(h.w, 1e-5);
    float v = clamp((h.x - 0.5) * _FinalContrast + 0.5 + _FinalBias, 0.0, 1.0);
    // Split-curve gain: stretches highlights and shadows separately around 0.5.
    if (v > 0.5) v = pow(clamp(v * 2.0 - 1.0, 0.0, 1.0), _FinalGain) * 0.5 + 0.5;
    else         v = 1.0 - (pow(clamp((1.0 - v) * 2.0 - 1.0, 0.0, 1.0), _FinalGain) * 0.5 + 0.5);
    outColor = vec4(v, v, v, 1.0);
}
";

    // ─── Kernel: compute normal from height (gradient + optional diffuse shape) ──────
    private const string FRAG_NORMAL_FROM_HEIGHT = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;       // height (R channel used)
uniform sampler2D _LightTex;      // optional: diffuse, for shape recognition
uniform sampler2D _LightBlurTex;  // optional: blurred diffuse
uniform vec2 _ImageSize;
uniform float _BlurContrast;      // slope steepness (default 20)
uniform float _ShapeRecognition;  // 0 = pure height-gradient normal
uniform float _LightRotation;
uniform float _ShapeBias;
uniform int _Desaturate;
void main() {
    vec2 pixelSize = 1.0 / _ImageSize;
    float h0  = texture(_MainTex, vUV).x;
    float hdx = texture(_MainTex, vec2(vUV.x + pixelSize.x, vUV.y)).x - h0;
    float hdy = texture(_MainTex, vec2(vUV.x, vUV.y + pixelSize.y)).x - h0;
    vec3 normalTex = normalize(cross(
        normalize(vec3(1.0, 0.0, hdx * _BlurContrast)),
        normalize(vec3(0.0, 1.0, hdy * _BlurContrast))));

    vec3 heightTex = texture(_LightTex, vUV).xyz;
    vec3 heightBlurTex = texture(_LightBlurTex, vUV).xyz;
    if (_Desaturate > 0) {
        float lum = heightTex.x * 0.3 + heightTex.y * 0.5 + heightTex.z * 0.2;
        heightTex = vec3(lum);
    }
    float HPHeight = (heightTex.x - heightBlurTex.x) + _ShapeBias;
    HPHeight = HPHeight * 2.0 - 1.0;

    vec3 lightDirection = vec3(sin(_LightRotation), cos(_LightRotation), 0.0);
    vec3 lightCross = cross(lightDirection, vec3(0.0, 0.0, 1.0));
    vec3 shape = (HPHeight * lightDirection) + (dot(normalTex, lightCross) * lightCross);
    shape.z = sqrt(1.0 - clamp(dot(shape.xy, shape.xy), 0.0, 1.0));
    shape = normalize(shape);

    normalTex = normalize(mix(normalTex, shape, _ShapeRecognition));
    outColor = vec4(normalTex * 0.5 + 0.5, 1.0);
}
";

    // ─── Kernel: combine 7-octave pyramid of normals ─────────────────────────────────
    // Ports fragCombineNormal (pass 4). Weighted sum of octaves, remap to [-1,1],
    // angularity-blend toward a flattened 'cone' normal, contrast stretch, Y-flip option.
    private const string FRAG_COMBINE_NORMAL = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _BlurTex0;
uniform sampler2D _BlurTex1;
uniform sampler2D _BlurTex2;
uniform sampler2D _BlurTex3;
uniform sampler2D _BlurTex4;
uniform sampler2D _BlurTex5;
uniform sampler2D _BlurTex6;
uniform float _Blur0Weight;
uniform float _Blur1Weight;
uniform float _Blur2Weight;
uniform float _Blur3Weight;
uniform float _Blur4Weight;
uniform float _Blur5Weight;
uniform float _Blur6Weight;
uniform float _FinalContrast;
uniform float _Angularity;
uniform float _AngularIntensity;
uniform int _FlipNormalY;
void main() {
    vec4 n = vec4(texture(_BlurTex0, vUV).xyz, 1.0) * _Blur0Weight;
    n += vec4(texture(_BlurTex1, vUV).xyz, 1.0) * _Blur1Weight;
    n += vec4(texture(_BlurTex2, vUV).xyz, 1.0) * _Blur2Weight;
    n += vec4(texture(_BlurTex3, vUV).xyz, 1.0) * _Blur3Weight;
    n += vec4(texture(_BlurTex4, vUV).xyz, 1.0) * _Blur4Weight;
    n += vec4(texture(_BlurTex5, vUV).xyz, 1.0) * _Blur5Weight;
    n += vec4(texture(_BlurTex6, vUV).xyz, 1.0) * _Blur6Weight;
    n *= 1.0 / max(n.w, 1e-5);
    vec3 nv = normalize(n.xyz * 2.0 - 1.0);

    vec3 angularDir = normalize(vec3(
        normalize(vec3(nv.xy, 0.001)).xy * _AngularIntensity,
        max(1.0 - _AngularIntensity, 0.001)));
    nv = mix(nv, angularDir, _Angularity);

    nv.xy = nv.xy * _FinalContrast;
    nv.z = pow(clamp(nv.z, 0.0, 1.0), _FinalContrast);
    nv = normalize(nv) * 0.5 + 0.5;

    if (_FlipNormalY == 0) nv.y = 1.0 - nv.y;
    outColor = vec4(nv, 1.0);
}
";

    // ─── Kernel: Sobel-like gradient on normal X/Y → diff product ────────────────────
    // Ports fragEdge (pass 5). Outputs a signed difference encoded as a single channel
    // centred at 0.5 — positive values are 'edges', negative are 'crevices'.
    private const string FRAG_EDGE_FROM_NORMAL = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform vec2 _ImageSize;
uniform float _BlurContrast;
uniform int _FlipNormalY;
void main() {
    vec2 ps = (1.0 / _ImageSize) * 0.5;
    vec4 mX  = texture(_MainTex, vec2(vUV.x + ps.x, vUV.y)) * 2.0 - 1.0;
    vec4 mX2 = texture(_MainTex, vec2(vUV.x - ps.x, vUV.y)) * 2.0 - 1.0;
    vec4 mY  = texture(_MainTex, vec2(vUV.x, vUV.y + ps.y)) * 2.0 - 1.0;
    vec4 mY2 = texture(_MainTex, vec2(vUV.x, vUV.y - ps.y)) * 2.0 - 1.0;
    float diffX = (mX.x  - mX2.x) * _BlurContrast;
    float diffY = (mY.y  - mY2.y) * _BlurContrast;
    if (_FlipNormalY == 0) diffY *= -1.0;
    float diff = (diffX + 0.5) * (diffY + 0.5) * 2.0;
    outColor = vec4(diff, diff, diff, 1.0);
}
";

    // ─── Kernel: combine 7-octave pyramid of edges into a tone-mapped cavity map ─────
    // Ports fragCombineEdge (pass 6). Weighted sum, then a split curve: values > 0.5
    // treated as edges (powered by Pinch, scaled by EdgeAmount); < 0.5 as crevices
    // (same treatment, negated). Final contrast + pillow (round/sharpen) + bias.
    private const string FRAG_COMBINE_EDGE = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform sampler2D _BlurTex1;
uniform sampler2D _BlurTex2;
uniform sampler2D _BlurTex3;
uniform sampler2D _BlurTex4;
uniform sampler2D _BlurTex5;
uniform sampler2D _BlurTex6;
uniform float _Blur0Weight;
uniform float _Blur1Weight;
uniform float _Blur2Weight;
uniform float _Blur3Weight;
uniform float _Blur4Weight;
uniform float _Blur5Weight;
uniform float _Blur6Weight;
uniform float _Pinch;
uniform float _Pillow;
uniform float _EdgeAmount;
uniform float _CreviceAmount;
uniform float _FinalContrast;
uniform float _FinalBias;
void main() {
    vec4 acc = vec4(texture(_MainTex, vUV).xyz, 1.0) * _Blur0Weight;
    acc += vec4(texture(_BlurTex1, vUV).xyz, 1.0) * _Blur1Weight;
    acc += vec4(texture(_BlurTex2, vUV).xyz, 1.0) * _Blur2Weight;
    acc += vec4(texture(_BlurTex3, vUV).xyz, 1.0) * _Blur3Weight;
    acc += vec4(texture(_BlurTex4, vUV).xyz, 1.0) * _Blur4Weight;
    acc += vec4(texture(_BlurTex5, vUV).xyz, 1.0) * _Blur5Weight;
    acc += vec4(texture(_BlurTex6, vUV).xyz, 1.0) * _Blur6Weight;
    acc *= 1.0 / max(acc.w, 1e-5);
    float v = acc.x;
    if (v > 0.5) {
        v = max(v * 2.0 - 1.0, 0.0);
        v = pow(v, _Pinch) * _EdgeAmount;
        v = v * 0.5 + 0.5;
    } else {
        v = max(-(v * 2.0 - 1.0), 0.0);
        v = pow(v, _Pinch) * _CreviceAmount;
        v = -v * 0.5 + 0.5;
    }
    v = (v - 0.5) * _FinalContrast + 0.5;
    v = pow(clamp(v, 0.0, 1.0), _Pillow);
    v = clamp(v + _FinalBias, 0.0, 1.0);
    outColor = vec4(v, v, v, 1.0);
}
";

    // ─── Kernel: AO accumulation pass (runs 100× with different _Progress) ───────────
    // Ports fragAO (pass 7). Each invocation samples along one direction angle (derived
    // from _Progress) and blends into the previous accumulator (_BlendTex) via _BlendAmount
    // = 1/iteration, yielding a running average. R = normal-only AO, G = depth-only AO.
    private const string FRAG_AO = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;     // normal map
uniform sampler2D _HeightTex;   // height (single channel)
uniform sampler2D _BlendTex;    // previous accumulator (R=normal, G=depth)
uniform vec2 _ImageSize;
uniform float _Spread;
uniform float _Depth;
uniform float _BlendAmount;
uniform float _Progress;
uniform int _FlipNormalY;

float rand(vec3 co) {
    return fract(sin(dot(co, vec3(12.9898, 78.233, 137.9462))) * 43758.5453);
}

void main() {
    vec2 pixelSize = 1.0 / _ImageSize;
    vec3 flipTex = _FlipNormalY == 0 ? vec3(1.0, -1.0, 1.0) : vec3(1.0);

    vec3 mainNormal = normalize(texture(_MainTex, vUV).xyz * 2.0 - 1.0) * flipTex;
    float mainHeight = texture(_HeightTex, vUV).x;

    vec2 dir = vec2(sin(_Progress * 6.28318530718), cos(_Progress * 6.28318530718));

    vec2 AO = vec2(0.0);
    float AOAccum = 0.0;

    const int AOSamples = 50;
    for (int i = 1; i <= AOSamples; i++) {
        float progress = float(i) / float(AOSamples);
        vec2 randomizer = vec2(
            rand(vec3(vUV, float(i))),
            rand(vec3(vUV.yx, float(i)))
        ) * progress * 0.1;
        vec2 uvOffset = dir * _Spread * progress + randomizer;
        vec2 trueDir = normalize(uvOffset);
        vec2 sampleUV = vUV + pixelSize * uvOffset;

        vec3 sampleN = normalize(texture(_MainTex, sampleUV).xyz * 2.0 - 1.0) * flipTex;
        float sampleHeight = texture(_HeightTex, sampleUV).x;

        float sampleImportance = sqrt(1.0 - progress);
        AO.x += dot(sampleN, vec3(trueDir, 0.0)) * sampleImportance;
        AOAccum += sampleImportance;

        vec3 samplePos = vec3(trueDir * _Spread * progress,
                              (sampleHeight - mainHeight) * _Depth);
        float sampleDist = clamp(length(samplePos) * 0.1, 0.0, 1.0);
        float depthAO = clamp(dot(vec3(0.0, 0.0, 1.0), normalize(samplePos)), 0.0, 1.0);
        AO.y = max(depthAO * sampleDist, AO.y);
    }

    AO.x *= 1.0 / max(AOAccum, 1e-5);
    float AOX1 = clamp(AO.x + 1.0, 0.0, 1.0);
    float AOX2 = clamp(AO.x + 0.5, 0.0, 1.0);
    AO.x = pow(AOX1, 5.0) * pow(AOX2, 0.2);
    AO.x = sqrt(AO.x);
    AO.y = 1.0 - AO.y;

    vec2 blendTex = texture(_BlendTex, vUV).xy;
    AO = mix(blendTex, AO, _BlendAmount);
    outColor = vec4(AO.x, AO.y, 0.0, 1.0);
}
";

    // ─── Kernel: final AO tone-map — blend normal-only vs depth-only, contrast/bias ──
    private const string FRAG_COMBINE_AO = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform float _AOBlend;
uniform float _FinalContrast;
uniform float _FinalBias;
void main() {
    vec2 ao = texture(_MainTex, vUV).xy;
    float v = mix(ao.x, ao.y, _AOBlend);
    v += _FinalBias;
    v = pow(clamp(v, 0.0, 1.0), _FinalContrast);
    v = clamp(v, 0.0, 1.0);
    outColor = vec4(v, v, v, 1.0);
}
";

    // ─── Kernel: clear an RT to (0,0,0,1) ────────────────────────────────────────────
    private const string FRAG_CLEAR = @"
in vec2 vUV;
out vec4 outColor;
void main() { outColor = vec4(0.0, 0.0, 0.0, 1.0); }
";

    // ─── Kernel: Metallic via HSL color match ────────────────────────────────────────
    // Ports Blit_Metallic. Matches diffuse hue/sat/lum against a user-picked "metal"
    // colour, modulates by a high-pass overlay term from the original diffuse.
    private const string FRAG_METALLIC = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;        // original diffuse
uniform sampler2D _BlurTex;        // pre-blurred diffuse (for HSL match stability)
uniform sampler2D _OverlayBlurTex; // wider-blurred diffuse (for high-pass overlay)
uniform vec4 _MetalColor;
uniform float _HueWeight;
uniform float _SatWeight;
uniform float _LumWeight;
uniform float _MaskLow;
uniform float _MaskHigh;
uniform float _BlurOverlay;
uniform float _FinalContrast;
uniform float _FinalBias;
" + HSL_LIB + @"
void main() {
    vec3 mainTex = texture(_MainTex, vUV).xyz;
    vec3 blurTex = texture(_BlurTex, vUV).xyz;
    vec3 overlayBlur = texture(_OverlayBlurTex, vUV).xyz;

    vec3 overlay = mainTex - overlayBlur;
    float overlayGrey = overlay.x * 0.3 + overlay.y * 0.5 + overlay.z * 0.2;

    vec3 bHSL = RGBToHSL(blurTex);
    vec3 mHSL = RGBToHSL(_MetalColor.xyz);

    // Circular hue distance (wraps around 1.0).
    float hueDif = 1.0 - min(
        min(abs(bHSL.x - mHSL.x), abs((bHSL.x + 1.0) - mHSL.x)),
        abs((bHSL.x - 1.0) - mHSL.x)) * 2.0;
    float satDif = 1.0 - abs(bHSL.y - mHSL.y);
    float lumDif = 1.0 - abs(bHSL.z - mHSL.z);

    float wSum = max(_HueWeight + _SatWeight + _LumWeight, 1e-5);
    float match = (hueDif * _HueWeight + satDif * _SatWeight + lumDif * _LumWeight) / wSum;
    match = smoothstep(_MaskLow, _MaskHigh, match);

    match = clamp((match - 0.5) * _FinalContrast + 0.5 + _FinalBias, 0.0, 1.0);
    match *= clamp(overlayGrey * _BlurOverlay + 1.0, 0.0, 10.0);
    match = clamp(match, 0.0, 1.0);
    outColor = vec4(match, match, match, 1.0);
}
";

    // ─── Kernel: Smoothness via HSL color match (up to 3 samples + metal pass-through)
    // Ports Blit_Smoothness. Starts from a base value, lerps toward each enabled sample's
    // smoothness value by that sample's match mask, finally replaces with _MetalSmoothness
    // where the metallic-mask texture is white.
    private const string FRAG_SMOOTHNESS = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform sampler2D _BlurTex;
uniform sampler2D _OverlayBlurTex;
uniform sampler2D _MetallicTex;
uniform vec4 _SampleColor1; uniform int _UseSample1; uniform float _HueWeight1; uniform float _SatWeight1; uniform float _LumWeight1; uniform float _MaskLow1; uniform float _MaskHigh1; uniform float _Sample1Smoothness;
uniform vec4 _SampleColor2; uniform int _UseSample2; uniform float _HueWeight2; uniform float _SatWeight2; uniform float _LumWeight2; uniform float _MaskLow2; uniform float _MaskHigh2; uniform float _Sample2Smoothness;
uniform vec4 _SampleColor3; uniform int _UseSample3; uniform float _HueWeight3; uniform float _SatWeight3; uniform float _LumWeight3; uniform float _MaskLow3; uniform float _MaskHigh3; uniform float _Sample3Smoothness;
uniform float _BaseSmoothness;
uniform float _MetalSmoothness;
uniform float _BlurOverlay;
uniform float _FinalContrast;
uniform float _FinalBias;
" + HSL_LIB + @"

float hslMatch(vec3 bHSL, vec3 sHSL, float hw, float sw, float lw, float mLow, float mHigh) {
    float hueDif = 1.0 - min(
        min(abs(bHSL.x - sHSL.x), abs((bHSL.x + 1.0) - sHSL.x)),
        abs((bHSL.x - 1.0) - sHSL.x)) * 2.0;
    float satDif = 1.0 - abs(bHSL.y - sHSL.y);
    float lumDif = 1.0 - abs(bHSL.z - sHSL.z);
    float wSum = max(hw + sw + lw, 1e-5);
    float m = (hueDif * hw + satDif * sw + lumDif * lw) / wSum;
    return smoothstep(mLow, mHigh, m);
}

void main() {
    vec3 mainTex = texture(_MainTex, vUV).xyz;
    vec3 blurTex = texture(_BlurTex, vUV).xyz;
    vec3 overlayBlur = texture(_OverlayBlurTex, vUV).xyz;

    vec3 overlay = mainTex - overlayBlur;
    float overlayGrey = overlay.x * 0.3 + overlay.y * 0.5 + overlay.z * 0.2;
    float metalMask = texture(_MetallicTex, vUV).x;

    vec3 bHSL = RGBToHSL(blurTex);

    float m1 = _UseSample1 != 0 ? hslMatch(bHSL, RGBToHSL(_SampleColor1.xyz),
        _HueWeight1, _SatWeight1, _LumWeight1, _MaskLow1, _MaskHigh1) : 0.0;
    float m2 = _UseSample2 != 0 ? hslMatch(bHSL, RGBToHSL(_SampleColor2.xyz),
        _HueWeight2, _SatWeight2, _LumWeight2, _MaskLow2, _MaskHigh2) : 0.0;
    float m3 = _UseSample3 != 0 ? hslMatch(bHSL, RGBToHSL(_SampleColor3.xyz),
        _HueWeight3, _SatWeight3, _LumWeight3, _MaskLow3, _MaskHigh3) : 0.0;

    float sm = _BaseSmoothness;
    sm = mix(sm, _Sample3Smoothness, m3);
    sm = mix(sm, _Sample2Smoothness, m2);
    sm = mix(sm, _Sample1Smoothness, m1);
    sm = mix(sm, _MetalSmoothness, metalMask);

    sm = clamp((sm - 0.5) * _FinalContrast + 0.5 + _FinalBias, 0.0, 1.0);
    sm *= clamp(overlayGrey * _BlurOverlay + 1.0, 0.0, 10.0);
    sm = clamp(sm, 0.0, 1.0);
    outColor = vec4(sm, sm, sm, 1.0);
}
";

    // ─── Kernel: Height from Normal (directional spiral integration, iterative) ──────
    // Ports Blit_Height_From_Normal.fragHeight. Each invocation samples along one angle
    // (_Progress) and blends into the running accumulator (_BlendTex) with _BlendAmount.
    private const string FRAG_HEIGHT_FROM_NORMAL = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;     // normal map
uniform sampler2D _BlendTex;    // previous accumulator (single-channel)
uniform vec2 _ImageSize;
uniform float _Spread;
uniform int _Samples;
uniform float _SpreadBoost;
uniform float _BlendAmount;
uniform float _Progress;
uniform int _FlipNormalY;

float rand(vec3 co) {
    return fract(sin(dot(co, vec3(12.9898, 78.233, 137.9462))) * 43758.5453);
}

void main() {
    vec2 pixelSize = 1.0 / _ImageSize;
    vec3 flipTex = _FlipNormalY == 0 ? vec3(1.0, -1.0, 1.0) : vec3(1.0);

    vec2 direction = vec2(sin(_Progress * 6.28318530718), cos(_Progress * 6.28318530718));

    float totalWeight = 0.0;
    float AO = 0.0;
    for (int i = 1; i <= _Samples; i++) {
        float pr = float(i) / float(_Samples);
        vec2 randomizer = vec2(
            rand(vec3(vUV,    float(i))),
            rand(vec3(vUV.yx, float(i)))) * pr * 0.1;
        vec2 uvOffset = direction * _Spread * (pr * _SpreadBoost) + randomizer;
        vec2 trueDir = normalize(uvOffset);
        vec2 sampleUV = vUV + pixelSize * uvOffset;

        vec3 sampleN = texture(_MainTex, sampleUV).xyz * 2.0 - 1.0;
        sampleN *= flipTex;
        float sampleAO = dot(sampleN, vec3(trueDir, 0.0));

        float w = 1.0;
        totalWeight += w;
        AO += sampleAO * w;
    }

    AO *= 1.0 / max(totalWeight, 1e-5);
    AO *= (float(_Samples) * _SpreadBoost) / 50.0;
    AO = AO * 0.5 + 0.5;

    float prev = texture(_BlendTex, vUV).x;
    AO = mix(prev, AO, _BlendAmount);
    outColor = vec4(AO, AO, AO, 1.0);
}
";

    // ─── Kernel: simple contrast/bias tone-map for height-from-normal output ─────────
    private const string FRAG_COMBINE_HEIGHT_SIMPLE = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform float _FinalContrast;
uniform float _FinalBias;
void main() {
    float v = texture(_MainTex, vUV).x;
    v = (v - 0.5) * _FinalContrast + 0.5 + _FinalBias;
    v = clamp(v, 0.0, 1.0);
    outColor = vec4(v, v, v, 1.0);
}
";

    // ─── Kernel: Seamless tiling (overlap-mode cross-fade) ───────────────────────────
    // Ports Blit_Seamless_Texture_Maker frag (overlap variant). Samples the source at
    // four UV offsets (base, H-shift, V-shift, diagonal), uses a height reference to pick
    // which version wins in overlap regions via smoothstep, producing seamless tiling.
    private const string FRAG_SEAMLESS = @"
in vec2 vUV;
out vec4 outColor;
uniform sampler2D _MainTex;
uniform sampler2D _HeightTex;
uniform float _Falloff;
uniform float _OverlapX;
uniform float _OverlapY;
uniform int _IsHeight;
void main() {
    vec2 overlap = vec2(_OverlapX, _OverlapY);
    vec2 invOverlap = 1.0 - overlap;
    vec2 oneOverOverlap = 1.0 / max(overlap, vec2(1e-4));

    vec2 UV  = vUV;
    vec2 UV2 = UV - vec2(overlap.x, 0.0);
    vec2 UV3 = UV - vec2(0.0, overlap.y);
    vec2 UV4 = UV - overlap;

    vec2 UVMask = clamp((1.0 - fract(UV) - invOverlap) * oneOverOverlap, 0.0, 1.0);

    UV  = fract(UV)  * invOverlap;
    UV2 = fract(UV2); UV2.x += overlap.x; UV2 *= invOverlap;
    UV3 = fract(UV3); UV3.y += overlap.y; UV3 *= invOverlap;
    UV4 = fract(UV4) + overlap; UV4 *= invOverlap;

    float h1 = texture(_HeightTex, UV ).x;
    float h2 = texture(_HeightTex, UV2).x;
    float h3 = texture(_HeightTex, UV3).x;
    float h4 = texture(_HeightTex, UV4).x;
    vec4 t1 = texture(_MainTex, UV );
    vec4 t2 = texture(_MainTex, UV2);
    vec4 t3 = texture(_MainTex, UV3);
    vec4 t4 = texture(_MainTex, UV4);

    float falloff = clamp(_Falloff, 0.0, 1.0);
    float SSHigh =  0.01 + 0.5 * falloff;
    float SSLow  = -0.01 - 0.5 * falloff;

    // Horizontal blend of the two top-row variants.
    float bH = smoothstep(SSLow, SSHigh, (h2 + UVMask.x) - (h1 + (1.0 - UVMask.x)));
    vec4  tH = mix(t1, t2, bH);
    float hH = max(h1 + (1.0 - UVMask.x), h2 + UVMask.x) - 1.0
             + clamp(min(UVMask.x, 1.0 - UVMask.x), 0.0, 1.0);

    // Horizontal blend of the two bottom-row variants.
    float bV = smoothstep(SSLow, SSHigh, (h4 + UVMask.x) - (h3 + (1.0 - UVMask.x)));
    vec4  tV = mix(t3, t4, bV);
    float hV = max(h3 + (1.0 - UVMask.x), h4 + UVMask.x) - 1.0
             + clamp(min(UVMask.x, 1.0 - UVMask.x), 0.0, 1.0);

    // Vertical blend of the two mid results.
    float bF = smoothstep(SSLow, SSHigh, (hV + UVMask.y) - (hH + (1.0 - UVMask.y)));
    vec4  tF = mix(tH, tV, bF);
    float hF = max(hH + (1.0 - UVMask.y), hV + UVMask.y) - 1.0
             + clamp(min(UVMask.y, 1.0 - UVMask.y), 0.0, 1.0);

    if (_IsHeight != 0) outColor = vec4(hF, hF, hF, 1.0);
    else                outColor = vec4(tF.xyz, 1.0);
}
";

    // ─── Per-program cache (lazy-compile) ────────────────────────────────────────────
    private static readonly Dictionary<string, Material> _materialCache = new();

    private static Material GetMaterial(string key, string fragmentSource)
    {
        if (_materialCache.TryGetValue(key, out var existing) && existing != null) return existing;

        var pass = new ShaderPass(
            name: key,
            tags: null,
            tagSortOffsets: null,
            state: new RasterizerState
            {
                DepthTest = false,
                DepthWrite = false,
                DoBlend = false,
                CullFace = RasterizerState.PolyFace.None,
            },
            vertexSource: VERTEX_SRC,
            fragmentSource: fragmentSource,
            fallbackAsset: "");

        var shader = new Shader($"PBRForge/{key}", Array.Empty<ShaderProperty>(), new[] { pass });
        var mat = new Material(shader);
        _materialCache[key] = mat;
        return mat;
    }

    // ─── RT helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get a temporary RT sized to (w,h) with the given format. Pair with
    /// <see cref="ReleaseRT"/> when done. Wraps Prowl's RT pool so back-to-back
    /// generators reuse memory.
    /// </summary>
    private static RenderTexture GetRT(int w, int h, TextureImageFormat format = TextureImageFormat.Color4b)
        => RenderTexture.GetTemporaryRT(w, h, false, new[] { format });

    private static void ReleaseRT(RenderTexture rt) => RenderTexture.ReleaseTemporaryRT(rt);

    /// <summary>Read RT pixels back into a fresh CPU-side <see cref="Texture2D"/> (Color4b).
    /// Stored bottom-up, matching Prowl's source-texture convention (Texture2D.FromImage
    /// calls image.Flip() before upload, so GL row 0 ends up at the bottom of the original
    /// image). Display code must apply a Y-flip when drawing — see
    /// <see cref="PBRForgeUtils.DrawTexturePreview"/>.</summary>
    public static Texture2D ReadbackRGBA(RenderTexture rt)
    {
        int w = rt.Width, h = rt.Height;
        var tex = new Texture2D((uint)w, (uint)h, false, TextureImageFormat.Color4b);
        byte[] pixels = new byte[w * h * 4];

        // Bind the source RT for the read; we deliberately don't unbind here — the
        // surrounding OffscreenRenderScope (or caller) is responsible for restoring
        // the previous framebuffer.
        Graphics.BindFramebuffer(rt.frameBuffer);
        unsafe
        {
            fixed (byte* p = pixels)
                Graphics.GL.ReadPixels(0, 0, (uint)w, (uint)h,
                    Silk.NET.OpenGL.PixelFormat.Rgba,
                    Silk.NET.OpenGL.PixelType.UnsignedByte,
                    p);
        }

        tex.SetData<byte>(pixels);
        return tex;
    }

    /// <summary>
    /// Save a <see cref="Texture2D"/> to disk as a PNG. The texture is assumed to be in
    /// Prowl's bottom-up convention (matching <see cref="ReadbackRGBA"/> output and
    /// <see cref="Texture2D.FromImage"/>); this method flips Y on the way to PNG.
    /// </summary>
    public static void SavePng(Texture2D tex, string absolutePath)
    {
        uint w = tex.Width, h = tex.Height;
        byte[] pixels = new byte[w * h * 4];
        tex.GetData<byte>(pixels);

        // Flip rows: bottom-up → top-down for PNG.
        byte[] topDown = new byte[pixels.Length];
        int rowBytes = (int)w * 4;
        for (int y = 0; y < h; y++)
            Array.Copy(pixels, y * rowBytes, topDown, ((int)h - 1 - y) * rowBytes, rowBytes);

        // PixelReadSettings tells Magick "this is raw pixel data" rather than "this is an
        // .rgb file with a header" (which is what MagickReadSettings.Format = Rgba means
        // and triggered the ReadRGBImage end-of-file error).
        var settings = new PixelReadSettings(w, h, StorageType.Char, PixelMapping.RGBA);
        using var img = new MagickImage(topDown, settings);
        img.Format = MagickFormat.Png;
        img.Write(absolutePath);
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //                         Generator: HEIGHT FROM DIFFUSE
    // ────────────────────────────────────────────────────────────────────────────────

    public struct HeightFromDiffuseSettings
    {
        public float[] BlurWeights;     // 7 octaves
        public float[] BlurContrasts;   // 7 octaves
        public float FinalContrast;
        public float FinalBias;
        /// <summary>
        /// Power exponent applied to the split-curve final-tone-map. <b>1.0 = identity</b>
        /// (no remap). Values &lt; 1 lift highlights/shadows, values &gt; 1 compress them.
        /// </summary>
        public float FinalGain;
        public float SpreadBoost;       // multiplies the per-octave spread (size in pixels)

        public static HeightFromDiffuseSettings Default => new()
        {
            BlurWeights   = new[] { 0.15f, 0.19f, 0.30f, 0.50f, 0.70f, 0.90f, 1.00f },
            BlurContrasts = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f },
            FinalContrast = 1.5f,
            FinalBias = 0.0f,
            FinalGain = 1.0f,
            SpreadBoost = 1.0f,
        };
    }

    /// <summary>
    /// Generate a height map from a diffuse/albedo. Decomposes luminance into 7
    /// frequency octaves via an iterative cosine-weighted directional blur
    /// pyramid, weights/contrasts each octave, then composites and tone-maps.
    /// </summary>
    public static Texture2D GenerateHeightFromDiffuse(Texture2D diffuse, HeightFromDiffuseSettings s)
    {
        if (diffuse == null) throw new ArgumentNullException(nameof(diffuse));
        if (s.BlurWeights == null || s.BlurWeights.Length != 7) throw new ArgumentException("BlurWeights must be length 7");
        if (s.BlurContrasts == null || s.BlurContrasts.Length != 7) throw new ArgumentException("BlurContrasts must be length 7");

        // Snapshot the editor's framebuffer + viewport so our blits and readback don't
        // hijack whatever the inspector / scene view is rendering into this frame.
        using var _ = OffscreenRenderScope.Begin();

        int w = (int)diffuse.Width, h = (int)diffuse.Height;
        var avg = BuildAveragePyramid(diffuse, w, h, s.SpreadBoost, out var octaves);

        var combineMat = GetMaterial("CombineHeight", FRAG_COMBINE_HEIGHT);
        combineMat.SetTexture("_AvgTex", avg.MainTexture!);
        for (int i = 0; i < 7; i++)
        {
            combineMat.SetTexture($"_BlurTex{i}", octaves[i].MainTexture!);
            combineMat.SetFloat($"_Blur{i}Weight", s.BlurWeights[i]);
            combineMat.SetFloat($"_Blur{i}Contrast", s.BlurContrasts[i]);
        }
        combineMat.SetFloat("_FinalContrast", s.FinalContrast);
        combineMat.SetFloat("_FinalBias", s.FinalBias);
        combineMat.SetFloat("_FinalGain", s.FinalGain);

        var output = GetRT(w, h);
        RenderPipeline.Blit(diffuse, output, combineMat, 0);
        var tex = ReadbackRGBA(output);
        ReleaseRT(output);

        ReleaseRT(avg);
        for (int i = 0; i < 7; i++) ReleaseRT(octaves[i]);

        return tex;
    }

    /// <summary>
    /// Build the desaturated multi-octave pyramid that Height/Normal/Edge generators all
    /// share. Returns the average (heaviest blur) plus 7 progressively-narrower octaves
    /// in <paramref name="octaves"/> (caller must release).
    /// </summary>
    private static RenderTexture BuildAveragePyramid(Texture2D source, int w, int h, float spreadBoost, out RenderTexture[] octaves)
    {
        // Geometric spread progression: 1, 1+e, 1+2e, 1+4e, 1+8e, 1+16e, 1+32e (e = boost).
        // Each octave is built by an H-pass then V-pass through a cosine-weighted blur.
        float[] spreads = new float[7];
        spreads[0] = 1f;
        for (int i = 1; i < 7; i++) spreads[i] = 1f + (float)Math.Pow(2, i - 1) * spreadBoost;

        var blurMat = GetMaterial("DirBlur", FRAG_DIR_BLUR);
        blurMat.SetVector("_ImageSize", new Float2(w, h));
        blurMat.SetFloat("_BlurContrast", 1f);
        blurMat.SetInt("_Desaturate", 1);
        blurMat.SetInt("_BlurSamples", 16);

        octaves = new RenderTexture[7];
        for (int i = 0; i < 7; i++)
        {
            octaves[i] = GetRT(w, h);
            var horiz = GetRT(w, h);

            blurMat.SetVector("_BlurDirection", new Float2(1, 0));
            blurMat.SetFloat("_BlurSpread", spreads[i]);
            RenderPipeline.Blit(source, horiz, blurMat, 0);

            blurMat.SetVector("_BlurDirection", new Float2(0, 1));
            blurMat.SetFloat("_BlurSpread", spreads[i]);
            RenderPipeline.Blit(horiz, octaves[i], blurMat, 0);

            ReleaseRT(horiz);
        }

        // Average pass: very-wide blur across both axes for the per-pixel local mean.
        var avgH = GetRT(w, h);
        var avg = GetRT(w, h);
        blurMat.SetInt("_BlurSamples", 32);
        blurMat.SetFloat("_BlurSpread", 64f);
        blurMat.SetVector("_BlurDirection", new Float2(1, 0));
        RenderPipeline.Blit(source, avgH, blurMat, 0);
        blurMat.SetVector("_BlurDirection", new Float2(0, 1));
        RenderPipeline.Blit(avgH, avg, blurMat, 0);
        ReleaseRT(avgH);

        return avg;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  Shared pyramid helpers (used by Normal / Edge / Height generators)
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a 7-octave pyramid from a <see cref="RenderTexture"/> source by running the
    /// cosine-weighted directional blur.
    /// progression <c>1, 1+e, 1+2e, 1+4e, 1+8e, 1+16e, 1+32e</c> where <c>e = spreadBoost</c>.
    /// </summary>
    private static RenderTexture[] BuildOctavesFromRT(RenderTexture source, int w, int h, float spreadBoost, bool desaturate)
    {
        float[] spreads = new float[7];
        spreads[0] = 1f;
        for (int i = 1; i < 7; i++) spreads[i] = 1f + MathF.Pow(2, i - 1) * spreadBoost;

        var blurMat = GetMaterial("DirBlur", FRAG_DIR_BLUR);
        blurMat.SetVector("_ImageSize", new Float2(w, h));
        blurMat.SetFloat("_BlurContrast", 1f);
        blurMat.SetInt("_Desaturate", desaturate ? 1 : 0);
        blurMat.SetInt("_BlurSamples", 16);

        var octaves = new RenderTexture[7];
        for (int i = 0; i < 7; i++)
        {
            octaves[i] = GetRT(w, h);
            var horiz = GetRT(w, h);

            blurMat.SetVector("_BlurDirection", new Float2(1, 0));
            blurMat.SetFloat("_BlurSpread", spreads[i]);
            RenderPipeline.Blit(source, horiz, blurMat, 0);

            blurMat.SetVector("_BlurDirection", new Float2(0, 1));
            RenderPipeline.Blit(horiz, octaves[i], blurMat, 0);

            ReleaseRT(horiz);
        }
        return octaves;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: NORMAL FROM HEIGHT
    // ════════════════════════════════════════════════════════════════════════════════

    public struct NormalFromHeightSettings
    {
        /// <summary>Steepness of the height→normal gradient. default 20.</summary>
        public float SlopeContrast;
        /// <summary>7-octave blur weights. default: 0.3, 0.35, 0.5, 0.8, 1.0, 0.95, 0.8.</summary>
        public float[] BlurWeights;
        /// <summary>Final contrast applied to the normal tangent. Default 5.</summary>
        public float FinalContrast;
        /// <summary>[0,1] — tilts the normal toward a uniform cone shape (flatten).</summary>
        public float Angularity;
        /// <summary>[0,1] — how far to tilt when Angularity &gt; 0.</summary>
        public float AngularIntensity;
        /// <summary>[0,1] — 0 disables diffuse-based shape bias (pure gradient normal).</summary>
        public float ShapeRecognition;
        /// <summary>Radians — rotates the diffuse shape light direction.</summary>
        public float LightRotation;
        /// <summary>[0,1] — bias applied to high-pass of diffuse (0.5 = unbiased). Default 0.5.</summary>
        public float ShapeBias;
        /// <summary>Spread boost for the 7-octave pyramid (geometric progression).</summary>
        public float SpreadBoost;
        /// <summary>true = OpenGL normal map (+Y up); false = DirectX (flip Y).</summary>
        public bool OpenGLNormalY;

        public static NormalFromHeightSettings Default => new()
        {
            SlopeContrast = 20f,
            BlurWeights = new[] { 0.3f, 0.35f, 0.5f, 0.8f, 1.0f, 0.95f, 0.8f },
            FinalContrast = 5f,
            Angularity = 0f,
            AngularIntensity = 0.5f,
            ShapeRecognition = 0f,
            LightRotation = 0f,
            ShapeBias = 0.5f,
            SpreadBoost = 1f,
            OpenGLNormalY = true,
        };
    }

    /// <summary>
    /// Generate a tangent-space normal from a height map. If <paramref name="diffuse"/> is
    /// provided and settings have <c>ShapeRecognition &gt; 0</c>, the diffuse contributes
    /// shape hints via high-pass blending.
    /// </summary>
    public static Texture2D GenerateNormalFromHeight(Texture2D height, Texture2D? diffuse, NormalFromHeightSettings s)
    {
        if (height == null) throw new ArgumentNullException(nameof(height));
        if (s.BlurWeights == null || s.BlurWeights.Length != 7) throw new ArgumentException("BlurWeights must be length 7");

        using var _ = OffscreenRenderScope.Begin();

        int w = (int)height.Width, hh = (int)height.Height;

        // _LightTex + _LightBlurTex: either the provided diffuse (desaturated) or the height
        // itself as a fallback (_ShapeRecognition=0 means the contribution is zero anyway).
        Texture2D lightSource = diffuse ?? height;
        bool desaturate = diffuse != null;

        var lightBlurH = GetRT(w, hh);
        var lightBlur = GetRT(w, hh);
        var blurMat = GetMaterial("DirBlur", FRAG_DIR_BLUR);
        blurMat.SetVector("_ImageSize", new Float2(w, hh));
        blurMat.SetFloat("_BlurContrast", 1f);
        blurMat.SetInt("_Desaturate", desaturate ? 1 : 0);
        blurMat.SetInt("_BlurSamples", 16);
        blurMat.SetFloat("_BlurSpread", 32f);
        blurMat.SetVector("_BlurDirection", new Float2(1, 0));
        RenderPipeline.Blit(lightSource, lightBlurH, blurMat, 0);
        blurMat.SetVector("_BlurDirection", new Float2(0, 1));
        RenderPipeline.Blit(lightBlurH, lightBlur, blurMat, 0);
        ReleaseRT(lightBlurH);

        // Normal from height gradient (with optional shape-recognition blend).
        var normalMat = GetMaterial("NormalFromHeight", FRAG_NORMAL_FROM_HEIGHT);
        normalMat.SetTexture("_LightTex", lightSource);
        normalMat.SetTexture("_LightBlurTex", lightBlur.MainTexture!);
        normalMat.SetVector("_ImageSize", new Float2(w, hh));
        normalMat.SetFloat("_BlurContrast", s.SlopeContrast);
        normalMat.SetFloat("_ShapeRecognition", s.ShapeRecognition);
        normalMat.SetFloat("_LightRotation", s.LightRotation);
        normalMat.SetFloat("_ShapeBias", s.ShapeBias);
        normalMat.SetInt("_Desaturate", desaturate ? 1 : 0);

        var rawNormal = GetRT(w, hh);
        RenderPipeline.Blit(height, rawNormal, normalMat, 0);
        ReleaseRT(lightBlur);

        // 7-octave spatial smoothing of the normal, then the combine.
        var octaves = BuildOctavesFromRT(rawNormal, w, hh, s.SpreadBoost, desaturate: false);

        var combineMat = GetMaterial("CombineNormal", FRAG_COMBINE_NORMAL);
        for (int i = 0; i < 7; i++)
        {
            combineMat.SetTexture($"_BlurTex{i}", octaves[i].MainTexture!);
            combineMat.SetFloat($"_Blur{i}Weight", s.BlurWeights[i]);
        }
        combineMat.SetFloat("_FinalContrast", s.FinalContrast);
        combineMat.SetFloat("_Angularity", s.Angularity);
        combineMat.SetFloat("_AngularIntensity", s.AngularIntensity);
        combineMat.SetInt("_FlipNormalY", s.OpenGLNormalY ? 1 : 0);

        var output = GetRT(w, hh);
        RenderPipeline.Blit(rawNormal, output, combineMat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(rawNormal);
        for (int i = 0; i < 7; i++) ReleaseRT(octaves[i]);

        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: EDGE FROM NORMAL
    // ════════════════════════════════════════════════════════════════════════════════

    public struct EdgeFromNormalSettings
    {
        /// <summary>Scale applied to the raw Sobel diff before the pyramid. Default 1.</summary>
        public float SlopeContrast;
        /// <summary>7-octave blur weights.</summary>
        public float[] BlurWeights;
        /// <summary>[0,1] — multiplier for bright-edge output. Default 1.</summary>
        public float EdgeAmount;
        /// <summary>[0,1] — multiplier for dark-crevice output. Default 1.</summary>
        public float CreviceAmount;
        /// <summary>Power exponent for sharpening near 0.5. Default 1.</summary>
        public float Pinch;
        /// <summary>Post-contrast power; &gt;1 rounds the result, &lt;1 sharpens. Default 1.</summary>
        public float Pillow;
        public float FinalContrast;
        public float FinalBias;
        public float SpreadBoost;
        public bool OpenGLNormalY;

        public static EdgeFromNormalSettings Default => new()
        {
            SlopeContrast = 1f,
            BlurWeights = new[] { 1.0f, 0.5f, 0.3f, 0.5f, 0.7f, 0.7f, 0.3f },
            EdgeAmount = 1f,
            CreviceAmount = 1f,
            Pinch = 1f,
            Pillow = 1f,
            FinalContrast = 1f,
            FinalBias = 0f,
            SpreadBoost = 1f,
            OpenGLNormalY = true,
        };
    }

    /// <summary>
    /// Generate an edge/cavity map from a normal map. Output is grayscale: high values are
    /// convex edges, low values concave crevices.
    /// </summary>
    public static Texture2D GenerateEdgeFromNormal(Texture2D normal, EdgeFromNormalSettings s)
    {
        if (normal == null) throw new ArgumentNullException(nameof(normal));
        if (s.BlurWeights == null || s.BlurWeights.Length != 7) throw new ArgumentException("BlurWeights must be length 7");

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)normal.Width, h = (int)normal.Height;

        // Raw Sobel-style gradient on the normal → grayscale edge signal.
        var edgeMat = GetMaterial("EdgeFromNormal", FRAG_EDGE_FROM_NORMAL);
        edgeMat.SetVector("_ImageSize", new Float2(w, h));
        edgeMat.SetFloat("_BlurContrast", s.SlopeContrast);
        edgeMat.SetInt("_FlipNormalY", s.OpenGLNormalY ? 1 : 0);

        var rawEdge = GetRT(w, h);
        RenderPipeline.Blit(normal, rawEdge, edgeMat, 0);

        var octaves = BuildOctavesFromRT(rawEdge, w, h, s.SpreadBoost, desaturate: false);

        var combineMat = GetMaterial("CombineEdge", FRAG_COMBINE_EDGE);
        // Combine shader reads _MainTex as octave[0] and _BlurTex1..6 as the rest.
        combineMat.SetFloat("_Blur0Weight", s.BlurWeights[0]);
        for (int i = 1; i < 7; i++)
        {
            combineMat.SetTexture($"_BlurTex{i}", octaves[i].MainTexture!);
            combineMat.SetFloat($"_Blur{i}Weight", s.BlurWeights[i]);
        }
        combineMat.SetFloat("_Pinch", s.Pinch);
        combineMat.SetFloat("_Pillow", s.Pillow);
        combineMat.SetFloat("_EdgeAmount", s.EdgeAmount);
        combineMat.SetFloat("_CreviceAmount", s.CreviceAmount);
        combineMat.SetFloat("_FinalContrast", s.FinalContrast);
        combineMat.SetFloat("_FinalBias", s.FinalBias);

        var output = GetRT(w, h);
        // octaves[0] is passed as the source (bound to _MainTex inside Blit).
        RenderPipeline.Blit(octaves[0], output, combineMat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(rawEdge);
        for (int i = 0; i < 7; i++) ReleaseRT(octaves[i]);

        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: AO FROM NORMAL (+HEIGHT)
    // ════════════════════════════════════════════════════════════════════════════════

    public struct AOSettings
    {
        /// <summary>Sampling radius in pixels. default 50.</summary>
        public float Spread;
        /// <summary>Height scale for the depth-occlusion contribution. Default 100.</summary>
        public float Depth;
        /// <summary>[0,1] — mix between normal-only (0) and depth-only (1) AO. Default 1
        /// when height is provided, else auto-forced to 0.</summary>
        public float DepthBlend;
        /// <summary>Power exponent on the final AO. Default 1.</summary>
        public float FinalContrast;
        public float FinalBias;
        /// <summary>Number of accumulator iterations. Lower is faster.</summary>
        public int Iterations;
        public bool OpenGLNormalY;

        public static AOSettings Default => new()
        {
            Spread = 50f,
            Depth = 100f,
            DepthBlend = 1f,
            FinalContrast = 1f,
            FinalBias = 0f,
            Iterations = 99,
            OpenGLNormalY = true,
        };
    }

    /// <summary>
    /// Generate an ambient occlusion map.
    /// </summary>
    public static Texture2D GenerateAO(Texture2D normal, Texture2D? height, AOSettings s)
    {
        if (normal == null) throw new ArgumentNullException(nameof(normal));

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)normal.Width, h = (int)normal.Height;

        // Height fallback: if no height, use a grey-0.5 proxy so (sampleHeight - mainHeight)
        // is zero everywhere and the depth-AO term stays flat. Blend stays off too.
        Texture2D heightSrc = height ?? Texture2D.LoadDefault(DefaultTexture.Gray18);
        float blend = height != null ? s.DepthBlend : 0f;

        // Double-buffer the accumulator so each iteration reads from one and writes to the
        // other, then swap.
        var accumA = GetRT(w, h);
        var accumB = GetRT(w, h);

        // Clear accumA to (0,0,0,1).
        var clearMat = GetMaterial("Clear", FRAG_CLEAR);
        RenderPipeline.Blit(normal, accumA, clearMat, 0);

        var aoMat = GetMaterial("AO", FRAG_AO);
        aoMat.SetTexture("_HeightTex", heightSrc);
        aoMat.SetVector("_ImageSize", new Float2(w, h));
        aoMat.SetFloat("_Spread", s.Spread);
        aoMat.SetFloat("_Depth", s.Depth);
        aoMat.SetInt("_FlipNormalY", s.OpenGLNormalY ? 1 : 0);

        int iters = Math.Max(1, s.Iterations);
        var read = accumA;
        var write = accumB;
        for (int i = 1; i <= iters; i++)
        {
            aoMat.SetFloat("_BlendAmount", 1.0f / i);
            aoMat.SetFloat("_Progress", (float)i / iters);
            aoMat.SetTexture("_BlendTex", read.MainTexture!);
            RenderPipeline.Blit(normal, write, aoMat, 0);
            (read, write) = (write, read);
        }

        var combineMat = GetMaterial("CombineAO", FRAG_COMBINE_AO);
        combineMat.SetFloat("_AOBlend", blend);
        combineMat.SetFloat("_FinalContrast", s.FinalContrast);
        combineMat.SetFloat("_FinalBias", s.FinalBias);

        var output = GetRT(w, h);
        RenderPipeline.Blit(read, output, combineMat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(accumA);
        ReleaseRT(accumB);
        return tex;
    }

    /// <summary>
    /// Separable H+V cosine-weighted blur at the given spread, into a fresh temporary RT.
    /// The RT is owned by the caller (release when done). Used by Metallic/Smoothness to
    /// prep the pre-blur and overlay-blur inputs their kernels consume.
    /// </summary>
    private static RenderTexture BlurToRT(Texture2D source, int w, int h, float spread, int samples, bool desaturate)
    {
        var blurMat = GetMaterial("DirBlur", FRAG_DIR_BLUR);
        blurMat.SetVector("_ImageSize", new Float2(w, h));
        blurMat.SetFloat("_BlurContrast", 1f);
        blurMat.SetInt("_Desaturate", desaturate ? 1 : 0);
        blurMat.SetInt("_BlurSamples", samples);
        blurMat.SetFloat("_BlurSpread", MathF.Max(1e-3f, spread));

        var horiz = GetRT(w, h);
        var final = GetRT(w, h);
        blurMat.SetVector("_BlurDirection", new Float2(1, 0));
        RenderPipeline.Blit(source, horiz, blurMat, 0);
        blurMat.SetVector("_BlurDirection", new Float2(0, 1));
        RenderPipeline.Blit(horiz, final, blurMat, 0);
        ReleaseRT(horiz);
        return final;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: METALLIC FROM DIFFUSE
    // ════════════════════════════════════════════════════════════════════════════════

    public struct MetallicSettings
    {
        /// <summary>Reference colour to match.</summary>
        public Float4 MetalColor;
        public float HueWeight;   // default 1.0
        public float SatWeight;   // default 0.5
        public float LumWeight;   // default 0.2
        public float MaskLow;     // default 0
        public float MaskHigh;    // default 1
        public float BlurSize;    // pre-blur samples (0–100, default 0 = no blur)
        public float OverlayBlurSize; // wider blur for overlay (default 30)
        public float BlurOverlay; // overlay strength (default 1)
        public float FinalContrast;
        public float FinalBias;

        public static MetallicSettings Default => new()
        {
            MetalColor = new Float4(0.7f, 0.7f, 0.7f, 1f),
            HueWeight = 1f,
            SatWeight = 0.5f,
            LumWeight = 0.2f,
            MaskLow = 0f,
            MaskHigh = 1f,
            BlurSize = 0f,
            OverlayBlurSize = 30f,
            BlurOverlay = 1f,
            FinalContrast = 1f,
            FinalBias = 0f,
        };
    }

    public static Texture2D GenerateMetallic(Texture2D diffuse, MetallicSettings s)
    {
        if (diffuse == null) throw new ArgumentNullException(nameof(diffuse));

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)diffuse.Width, h = (int)diffuse.Height;

        // Pre-blur (stability for the HSL match). If BlurSize is 0 we still make a trivial
        // copy so the shader can bind a sampler — cheaper than branching.
        var preBlur = BlurToRT(diffuse, w, h, MathF.Max(1f, s.BlurSize), 16, desaturate: false);
        var overlayBlur = BlurToRT(diffuse, w, h, MathF.Max(1f, s.OverlayBlurSize), 32, desaturate: false);

        var mat = GetMaterial("Metallic", FRAG_METALLIC);
        mat.SetTexture("_BlurTex", preBlur.MainTexture!);
        mat.SetTexture("_OverlayBlurTex", overlayBlur.MainTexture!);
        mat.SetVector("_MetalColor", s.MetalColor);
        mat.SetFloat("_HueWeight", s.HueWeight);
        mat.SetFloat("_SatWeight", s.SatWeight);
        mat.SetFloat("_LumWeight", s.LumWeight);
        mat.SetFloat("_MaskLow", s.MaskLow);
        mat.SetFloat("_MaskHigh", s.MaskHigh);
        mat.SetFloat("_BlurOverlay", s.BlurOverlay);
        mat.SetFloat("_FinalContrast", s.FinalContrast);
        mat.SetFloat("_FinalBias", s.FinalBias);

        var output = GetRT(w, h);
        RenderPipeline.Blit(diffuse, output, mat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(preBlur);
        ReleaseRT(overlayBlur);
        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: SMOOTHNESS FROM DIFFUSE
    // ════════════════════════════════════════════════════════════════════════════════

    public struct SmoothnessSample
    {
        public bool Use;
        public Float4 Color;
        public float HueWeight;
        public float SatWeight;
        public float LumWeight;
        public float MaskLow;
        public float MaskHigh;
        public float Smoothness;

        public static SmoothnessSample Off => new() { Use = false };
    }

    public struct SmoothnessSettings
    {
        public float BaseSmoothness;     // default 0.1
        public float MetalSmoothness;    // default 0.7 (applied where metallic mask is white)
        public SmoothnessSample Sample1; // defaults: off
        public SmoothnessSample Sample2;
        public SmoothnessSample Sample3;
        public float BlurSize;
        public float OverlayBlurSize;
        public float BlurOverlay;        // default 3.0 (higher than Metallic)
        public float FinalContrast;
        public float FinalBias;

        public static SmoothnessSettings Default => new()
        {
            BaseSmoothness = 0.1f,
            MetalSmoothness = 0.7f,
            Sample1 = SmoothnessSample.Off,
            Sample2 = SmoothnessSample.Off,
            Sample3 = SmoothnessSample.Off,
            BlurSize = 0f,
            OverlayBlurSize = 30f,
            BlurOverlay = 3f,
            FinalContrast = 1f,
            FinalBias = 0f,
        };
    }

    /// <summary>
    /// Generate a smoothness map from diffuse. If <paramref name="metallic"/> is null,
    /// metal-tagged regions contribute nothing and the base + sample values drive the result.
    /// </summary>
    public static Texture2D GenerateSmoothness(Texture2D diffuse, Texture2D? metallic, SmoothnessSettings s)
    {
        if (diffuse == null) throw new ArgumentNullException(nameof(diffuse));

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)diffuse.Width, h = (int)diffuse.Height;

        var preBlur = BlurToRT(diffuse, w, h, MathF.Max(1f, s.BlurSize), 16, desaturate: false);
        var overlayBlur = BlurToRT(diffuse, w, h, MathF.Max(1f, s.OverlayBlurSize), 32, desaturate: false);

        var mat = GetMaterial("Smoothness", FRAG_SMOOTHNESS);
        mat.SetTexture("_BlurTex", preBlur.MainTexture!);
        mat.SetTexture("_OverlayBlurTex", overlayBlur.MainTexture!);
        mat.SetTexture("_MetallicTex", metallic ?? Texture2D.LoadDefault(DefaultTexture.White));

        void ApplySample(string suffix, SmoothnessSample samp)
        {
            mat.SetInt($"_UseSample{suffix}", samp.Use ? 1 : 0);
            mat.SetVector($"_SampleColor{suffix}", samp.Color);
            mat.SetFloat($"_HueWeight{suffix}", samp.HueWeight);
            mat.SetFloat($"_SatWeight{suffix}", samp.SatWeight);
            mat.SetFloat($"_LumWeight{suffix}", samp.LumWeight);
            mat.SetFloat($"_MaskLow{suffix}", samp.MaskLow);
            mat.SetFloat($"_MaskHigh{suffix}", samp.MaskHigh);
            mat.SetFloat($"_Sample{suffix}Smoothness", samp.Smoothness);
        }
        ApplySample("1", s.Sample1);
        ApplySample("2", s.Sample2);
        ApplySample("3", s.Sample3);

        // When no metallic map is provided, zero out its contribution so MetalSmoothness
        // doesn't override the base/sample values everywhere.
        mat.SetFloat("_BaseSmoothness", s.BaseSmoothness);
        mat.SetFloat("_MetalSmoothness", metallic != null ? s.MetalSmoothness : s.BaseSmoothness);
        mat.SetFloat("_BlurOverlay", s.BlurOverlay);
        mat.SetFloat("_FinalContrast", s.FinalContrast);
        mat.SetFloat("_FinalBias", s.FinalBias);

        var output = GetRT(w, h);
        RenderPipeline.Blit(diffuse, output, mat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(preBlur);
        ReleaseRT(overlayBlur);
        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: HEIGHT FROM NORMAL
    // ════════════════════════════════════════════════════════════════════════════════

    public struct HeightFromNormalSettings
    {
        public float Spread;         // pixels — default 50
        public float SpreadBoost;    // default 1
        public int SamplesPerIter;   // default 50
        public int Iterations;       // default 99
        public float FinalContrast;  // default 1
        public float FinalBias;
        public bool OpenGLNormalY;

        public static HeightFromNormalSettings Default => new()
        {
            Spread = 50f,
            SpreadBoost = 1f,
            SamplesPerIter = 50,
            Iterations = 99,
            FinalContrast = 1f,
            FinalBias = 0f,
            OpenGLNormalY = true,
        };
    }

    public static Texture2D GenerateHeightFromNormal(Texture2D normal, HeightFromNormalSettings s)
    {
        if (normal == null) throw new ArgumentNullException(nameof(normal));

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)normal.Width, h = (int)normal.Height;

        var accumA = GetRT(w, h);
        var accumB = GetRT(w, h);
        var clearMat = GetMaterial("Clear", FRAG_CLEAR);
        RenderPipeline.Blit(normal, accumA, clearMat, 0);

        var heightMat = GetMaterial("HeightFromNormal", FRAG_HEIGHT_FROM_NORMAL);
        heightMat.SetVector("_ImageSize", new Float2(w, h));
        heightMat.SetFloat("_Spread", s.Spread);
        heightMat.SetInt("_Samples", s.SamplesPerIter);
        heightMat.SetFloat("_SpreadBoost", s.SpreadBoost);
        heightMat.SetInt("_FlipNormalY", s.OpenGLNormalY ? 1 : 0);

        int iters = Math.Max(1, s.Iterations);
        var read = accumA;
        var write = accumB;
        for (int i = 1; i <= iters; i++)
        {
            heightMat.SetFloat("_BlendAmount", 1.0f / i);
            heightMat.SetFloat("_Progress", (float)i / iters);
            heightMat.SetTexture("_BlendTex", read.MainTexture!);
            RenderPipeline.Blit(normal, write, heightMat, 0);
            (read, write) = (write, read);
        }

        var combineMat = GetMaterial("CombineHeightSimple", FRAG_COMBINE_HEIGHT_SIMPLE);
        combineMat.SetFloat("_FinalContrast", s.FinalContrast);
        combineMat.SetFloat("_FinalBias", s.FinalBias);

        var output = GetRT(w, h);
        RenderPipeline.Blit(read, output, combineMat, 0);
        var tex = ReadbackRGBA(output);

        ReleaseRT(output);
        ReleaseRT(accumA);
        ReleaseRT(accumB);
        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //                         Generator: SEAMLESS TILING
    // ════════════════════════════════════════════════════════════════════════════════

    public struct SeamlessSettings
    {
        /// <summary>[0,1] — blend-zone hardness. Lower = tighter transitions.</summary>
        public float Falloff;
        /// <summary>[0,1] — horizontal overlap fraction at the seam.</summary>
        public float OverlapX;
        /// <summary>[0,1] — vertical overlap fraction at the seam.</summary>
        public float OverlapY;

        public static SeamlessSettings Default => new()
        {
            Falloff = 0.1f,
            OverlapX = 0.2f,
            OverlapY = 0.2f,
        };
    }

    /// <summary>
    /// Make a tiling-friendly version of <paramref name="source"/> using the height-driven
    /// overlap technique. Provide a <paramref name="heightReference"/> (usually the Height
    /// map) so the blend picks the "higher" detail at seams; if null, source is used
    /// as its own height reference which still gives decent results on luminance-varied maps.
    /// </summary>
    /// <param name="isHeightMap">true if <paramref name="source"/> itself IS a height map
    /// (we emit the packed height output instead of the RGB output).</param>
    public static Texture2D GenerateSeamless(Texture2D source, Texture2D? heightReference, SeamlessSettings s, bool isHeightMap = false)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        using var _ = OffscreenRenderScope.Begin();
        int w = (int)source.Width, h = (int)source.Height;

        var mat = GetMaterial("Seamless", FRAG_SEAMLESS);
        mat.SetTexture("_HeightTex", heightReference ?? source);
        mat.SetFloat("_Falloff", s.Falloff);
        mat.SetFloat("_OverlapX", s.OverlapX);
        mat.SetFloat("_OverlapY", s.OverlapY);
        mat.SetInt("_IsHeight", isHeightMap ? 1 : 0);

        var output = GetRT(w, h);
        RenderPipeline.Blit(source, output, mat, 0);
        var tex = ReadbackRGBA(output);
        ReleaseRT(output);
        return tex;
    }
}
