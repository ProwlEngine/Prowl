// Shared implementation behind every Default/Standard* and Default/Unlit* shader.
//
// The shader set is split the way the legacy fixed-function sets were: one shader per
// combination of alpha mode and cull mode, each with its own render state, rather than one
// mega shader that branches at runtime. Every one of them is a thin file that picks its
// behaviour with defines and includes this.
//
//   Pass "..."
//   {
//       Shared   { #define PROWL_ALPHA_CUTOUT }
//       Vertex   { #define PROWL_VERTEX_STAGE
//                  #include "StandardCore"
//                  void main() { ProwlVertex(); } }
//       Fragment { #define PROWL_FRAGMENT_STAGE
//                  #include "StandardCore"
//                  void main() { ProwlFragment(); } }
//   }
//
// Surface defines (set in Shared so both stages agree):
//   PROWL_ALPHA_CUTOUT   alpha-test against _AlphaCutoff, opaque output
//   PROWL_ALPHA_BLEND    albedo alpha reaches the framebuffer, no alpha test
//   neither of those     fully opaque, the alpha channel is never read
//   PROWL_DOUBLE_SIDED   shade back faces with a flipped tangent frame
//   PROWL_UNLIT          skip lighting, GI and the whole PBR input set
//   PROWL_ANISOTROPIC    use the anisotropic BRDF and the _Anisotropy inputs
//
// Pass defines, exactly one (forward is assumed when none is given):
//   PROWL_PASS_FORWARD   the lit/unlit colour pass
//   PROWL_PASS_PREPASS   depth-normals + motion vectors + packed roughness/metallic
//   PROWL_PASS_SHADOW    shadow-map depth only
//
// Colour space: every texture that carries colour (_MainTex, _EmissionTex) is authored in
// sRGB and decoded here. Every factor (_MainColor, _EmissiveColor, vertex colour) is linear
// and is multiplied in AFTER the decode, which is what glTF's baseColorFactor / COLOR_0 /
// emissiveFactor require.

#ifndef PROWL_STANDARD_CORE
#define PROWL_STANDARD_CORE

#if !defined(PROWL_PASS_FORWARD) && !defined(PROWL_PASS_PREPASS) && !defined(PROWL_PASS_SHADOW)
    #define PROWL_PASS_FORWARD
#endif

// The alpha channel is only ever sampled where it can change the output.
#if defined(PROWL_ALPHA_CUTOUT) || defined(PROWL_ALPHA_BLEND)
    #define PROWL_READS_ALPHA
#endif

#ifdef PROWL_VERTEX_STAGE
    #define PROWL_VARYING out
#else
    #define PROWL_VARYING in
#endif

#include "ProwlCG"

#ifdef PROWL_VERTEX_STAGE
#include "VertexAttributes"
#endif

#if defined(PROWL_FRAGMENT_STAGE) && defined(PROWL_PASS_FORWARD)
#include "Lighting"
#endif

// ============================================================================
//  Varyings
// ============================================================================

uniform vec2 _Tiling;
uniform vec2 _Offset;

#ifdef PROWL_PASS_FORWARD
    PROWL_VARYING vec2 vUV;
    PROWL_VARYING vec3 vWorldPos;
    PROWL_VARYING vec4 vColor;
    #ifndef PROWL_UNLIT
        PROWL_VARYING vec3 vNormal;
        PROWL_VARYING vec3 vTangent;
        PROWL_VARYING vec3 vBitangent;
        PROWL_VARYING vec2 vLightmapUV;
    #endif
#endif

#ifdef PROWL_PASS_PREPASS
    PROWL_VARYING vec2 vUV;
    PROWL_VARYING vec3 vNormal;
    PROWL_VARYING vec3 vTangent;
    PROWL_VARYING vec3 vBitangent;
    PROWL_VARYING vec4 vCurrClipNJ;
    PROWL_VARYING vec4 vPrevClip;
#endif

#if defined(PROWL_PASS_SHADOW) && defined(PROWL_ALPHA_CUTOUT)
    PROWL_VARYING vec2 vUV;
#endif

// ============================================================================
//  Vertex
// ============================================================================

#ifdef PROWL_VERTEX_STAGE

// Builds a usable tangent frame even for meshes that ship no tangents, so the fragment
// stage never has to reason about uninitialised varyings.
void ProwlBuildTangentFrame(vec3 worldNormal, out vec3 tangent, out vec3 bitangent)
{
#ifdef HAS_TANGENTS
    tangent = TransformDirection(GetMorphedTangent(vertexTangent.xyz));
    bitangent = cross(tangent, worldNormal) * vertexTangent.w;
    // Degenerate frame (tangent parallel to the normal): rebuild from an arbitrary axis.
    if (dot(bitangent, bitangent) < 0.000001)
    {
        tangent = abs(worldNormal.y) < 0.999
            ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0)))
            : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
        bitangent = cross(tangent, worldNormal) * vertexTangent.w;
    }
#else
    tangent = abs(worldNormal.y) < 0.999
        ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0)))
        : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(tangent, worldNormal);
#endif
}

void ProwlVertex()
{
    gl_Position = TransformClip(vertexPosition);

#ifdef PROWL_PASS_FORWARD
    vUV = vertexTexCoord0 * _Tiling + _Offset;
    vWorldPos = TransformPosition(vertexPosition);
    vColor = GetInstanceColor();
    #ifndef PROWL_UNLIT
        vLightmapUV = vertexTexCoord1; // raw UV2, the lightmap scale/offset is applied per fragment
        vNormal = TransformDirection(GetMorphedNormal(vertexNormal));
        ProwlBuildTangentFrame(vNormal, vTangent, vBitangent);
    #endif
#endif

#ifdef PROWL_PASS_PREPASS
    vUV = vertexTexCoord0 * _Tiling + _Offset;
    vNormal = TransformDirection(GetMorphedNormal(vertexNormal));
    ProwlBuildTangentFrame(vNormal, vTangent, vBitangent);

    // Jitter-free current and previous clip positions for motion vectors.
    vec4 worldPos = GetModelMatrix() * vec4(vertexPosition, 1.0);
    vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * worldPos;
    vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
    vPrevClip = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;
#endif

#if defined(PROWL_PASS_SHADOW) && defined(PROWL_ALPHA_CUTOUT)
    vUV = vertexTexCoord0 * _Tiling + _Offset;
#endif
}

#endif // PROWL_VERTEX_STAGE

// ============================================================================
//  Fragment
// ============================================================================

#ifdef PROWL_FRAGMENT_STAGE

#if defined(PROWL_PASS_FORWARD) || defined(PROWL_READS_ALPHA)
uniform sampler2D _MainTex;
uniform vec4 _MainColor;
#endif

#ifdef PROWL_ALPHA_CUTOUT
uniform float _AlphaCutoff;
#endif

#ifndef PROWL_UNLIT
    #if defined(PROWL_PASS_FORWARD) || defined(PROWL_PASS_PREPASS)
        uniform sampler2D _NormalTex;
        uniform float _NormalScale;
        uniform sampler2D _SurfaceTex;
        uniform float _Metallic;
        uniform float _Roughness;
    #endif
    #ifdef PROWL_PASS_FORWARD
        uniform sampler2D _OcclusionTex;
        uniform float _OcclusionStrength;
        uniform sampler2D _EmissionTex;
        uniform vec4 _EmissiveColor;
        uniform float _EmissionIntensity;

        uniform sampler2D _ParallaxMap;
        uniform float _Parallax;
        uniform int _ParallaxSteps;

        uniform sampler2D _TranslucencyMap;
        uniform float _TranslucencyStrength;
        uniform float _ScatteringPower;
        uniform float _ScatteringDistortion;
        uniform float _ScatteringScale;

        #ifdef PROWL_ANISOTROPIC
            uniform float _Anisotropy;
            uniform sampler2D _AnisoDirectionMap;
        #endif
    #endif
#endif

// --- Baked GI, selected per-object by _GIMode ---
//   0 = realtime ambient (CalculateAmbient), 1 = baked lightmap (RGBM), 2 = light-probe SH.
// _GIMode is set per-draw by the render pipeline; _Lightmap/_LightmapScaleOffset are per-object
// (lightmapped statics); the prowl_SH* uniforms (in Lighting) are per-object (probe-lit dynamics).
#if defined(PROWL_PASS_FORWARD) && !defined(PROWL_UNLIT)
uniform int _GIMode;
uniform sampler2D _Lightmap;
uniform vec4 _LightmapScaleOffset;
// Which UV set the lightmap was baked into: 1 = UV2 (dedicated), 0 = UV0 (primary, fallback for
// meshes without UV2). Matches LightmapBakeService's per-mesh fallback.
uniform int _LightmapUV;

vec3 DecodeRGBM(vec4 rgbm) { return rgbm.rgb * (rgbm.a * 8.0); }

vec3 CalculateGI(vec3 worldNormal, vec2 lightmapUV2, vec2 uv0)
{
    if (_GIMode == 1)
    {
        vec2 base = (_LightmapUV == 1) ? lightmapUV2 : uv0;
        vec2 lmUV = base * _LightmapScaleOffset.xy + _LightmapScaleOffset.zw;
        return DecodeRGBM(texture(_Lightmap, lmUV)); // baked irradiance (linear)
    }
    if (_GIMode == 2)
        return ShadeSH9(worldNormal);               // light-probe SH

    return CalculateAmbient(worldNormal) * _AmbientStrength;
}
#endif

// Reads the tangent frame, flipping it for back faces on the double-sided variants so the
// shading normal always faces the viewer and the normal map keeps its handedness.
#if (defined(PROWL_PASS_FORWARD) && !defined(PROWL_UNLIT)) || defined(PROWL_PASS_PREPASS)
void ProwlSurfaceFrame(out vec3 N, out vec3 T, out vec3 B)
{
    N = normalize(vNormal);
    T = normalize(vTangent);
    B = normalize(vBitangent);
#ifdef PROWL_DOUBLE_SIDED
    if (!gl_FrontFacing)
    {
        N = -N;
        B = -B;
    }
#endif
}
#endif

// ---------------------------------------------------------------------------
//  Forward
// ---------------------------------------------------------------------------

#ifdef PROWL_PASS_FORWARD

layout (location = 0) out vec4 fragColor;

void ProwlFragment()
{
    vec2 uv = vUV;

#ifndef PROWL_UNLIT
    vec3 N, T, B;
    ProwlSurfaceFrame(N, T, B);

    // --- Parallax Occlusion Mapping ---
    #ifdef HAS_TANGENTS
    if (_Parallax > 0.0 && _ParallaxSteps > 0)
    {
        mat3 TBN = mat3(T, B, N);
        vec3 viewDirTS = normalize(transpose(TBN) * (_WorldSpaceCameraPos.xyz - vWorldPos));
        uv = ParallaxOcclusionMapping(_ParallaxMap, uv, viewDirTS, _Parallax, _ParallaxSteps);
    }
    #endif
#endif

    // --- Albedo. sRGB texture decoded first, linear factors applied after. ---
    vec4 albedoTexel = texture(_MainTex, uv);
    vec3 baseColor = gammaToLinearSpace(albedoTexel.rgb) * _MainColor.rgb * vColor.rgb;

#ifdef PROWL_READS_ALPHA
    float alpha = albedoTexel.a * _MainColor.a * vColor.a;
#endif

#ifdef PROWL_ALPHA_CUTOUT
    if (alpha < _AlphaCutoff)
        discard;
#endif

#ifdef PROWL_UNLIT
    vec3 color = ApplyFog(baseColor, vWorldPos);
#else
    vec3 worldNormal = ApplyNormalMapScaled(_NormalTex, uv, N, T, B, _NormalScale);

    // --- Surface: G = Roughness, B = Metallic, each scaled by its factor (glTF semantics). ---
    // glTF is free to author a roughnessFactor of 0 and plenty of models do, so the floor matters
    // here and not just as a backstop inside the BRDF.
    vec4 surface = texture(_SurfaceTex, uv);
    float roughness = clamp(surface.g * _Roughness, PROWL_MIN_ROUGHNESS, 1.0);
    float metallic = clamp(surface.b * _Metallic, 0.0, 1.0);

    // --- Ambient occlusion. R channel, 1 = unoccluded, lerped by strength. ---
    float ao = mix(1.0, texture(_OcclusionTex, uv).r, _OcclusionStrength);

    // --- Translucency map: G = extra occlusion, B = thickness ---
    vec4 transOcc = texture(_TranslucencyMap, uv);
    ao *= transOcc.g;
    float translucency = transOcc.b * _TranslucencyStrength;

    // --- Emission. sRGB texture, linear factor, then the strength multiplier. ---
    vec3 emission = gammaToLinearSpace(texture(_EmissionTex, uv).rgb) * _EmissiveColor.rgb * _EmissionIntensity;

    vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - vWorldPos);

    #ifdef PROWL_ANISOTROPIC
        // RG encodes a direction in the tangent plane; (0.5, 0.5) means "use the mesh tangent".
        vec2 anisoDir = texture(_AnisoDirectionMap, uv).rg * 2.0 - 1.0;
        float anisoDirLen = length(anisoDir);
        vec3 anisoTangent = T;
        vec3 anisoBitangent = B;
        if (anisoDirLen > 0.01)
        {
            anisoDir /= anisoDirLen;
            anisoTangent = normalize(T * anisoDir.x + B * anisoDir.y);
            anisoBitangent = normalize(cross(worldNormal, anisoTangent));
        }
        vec3 lighting = CalculateForwardLightingAniso(vWorldPos, worldNormal, viewDir,
                                                      anisoTangent, anisoBitangent,
                                                      baseColor, metallic, roughness, _Anisotropy, ao);
    #else
        vec3 lighting = CalculateForwardLighting(vWorldPos, worldNormal, viewDir,
                                                 baseColor, metallic, roughness, ao,
                                                 translucency, _ScatteringPower,
                                                 _ScatteringDistortion, _ScatteringScale);
    #endif

    // --- Ambient / baked GI ---
    vec3 ambientLight = CalculateGI(worldNormal, vLightmapUV, uv) * ao;

    // Diffuse ambient (non-metals only, metals have no diffuse)
    vec3 diffuseColor = baseColor * (1.0 - metallic);
    vec3 ambientDiffuse = ambientLight * diffuseColor;

    // Specular ambient approximation (critical for metals which have no diffuse).
    // Without IBL/environment maps we approximate indirect specular using the ambient
    // light, Fresnel at the view angle, and a roughness-dependent falloff.
    vec3 F0 = mix(vec3(0.04), baseColor, metallic);
    float NdotV = max(dot(worldNormal, viewDir), 0.0);
    vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    vec3 ambientSpecular = ambientLight * F * mix(specOcclusion, 1.0, 0.25);

    vec3 color = ApplyFog(ambientDiffuse + ambientSpecular + lighting + emission, vWorldPos);
#endif

#ifdef PROWL_ALPHA_BLEND
    fragColor = vec4(color, alpha);
#else
    fragColor = vec4(color, 1.0);
#endif
}

#endif // PROWL_PASS_FORWARD

// ---------------------------------------------------------------------------
//  Depth-normals prepass
// ---------------------------------------------------------------------------

#ifdef PROWL_PASS_PREPASS

layout (location = 0) out vec4 normalOut;
layout (location = 1) out vec4 motionRM;

void ProwlFragment()
{
#ifdef PROWL_ALPHA_CUTOUT
    if (texture(_MainTex, vUV).a * _MainColor.a < _AlphaCutoff)
        discard;
#endif

    vec3 N, T, B;
    ProwlSurfaceFrame(N, T, B);

    vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
    vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;

#ifdef PROWL_UNLIT
    // No PBR inputs, so SSR gets a perfectly rough dielectric and skips the surface.
    normalOut = EncodeViewNormal(N);
    motionRM = vec4(currNDC - prevNDC, 0.0, 0.0);
#else
    normalOut = EncodeViewNormal(ApplyNormalMapScaled(_NormalTex, vUV, N, T, B, _NormalScale));

    // Roughness/metallic must match the forward pass exactly, floor included, or SSR reflects off
    // a different surface than the one being shaded.
    vec4 surface = texture(_SurfaceTex, vUV);
    motionRM = vec4(currNDC - prevNDC,
                    clamp(surface.g * _Roughness, PROWL_MIN_ROUGHNESS, 1.0),
                    clamp(surface.b * _Metallic, 0.0, 1.0));
#endif
}

#endif // PROWL_PASS_PREPASS

// ---------------------------------------------------------------------------
//  Shadow caster
// ---------------------------------------------------------------------------

#ifdef PROWL_PASS_SHADOW

void ProwlFragment()
{
#ifdef PROWL_ALPHA_CUTOUT
    if (texture(_MainTex, vUV).a * _MainColor.a < _AlphaCutoff)
        discard;
#endif
    gl_FragDepth = gl_FragCoord.z;
}

#endif // PROWL_PASS_SHADOW

#endif // PROWL_FRAGMENT_STAGE

#endif // PROWL_STANDARD_CORE
