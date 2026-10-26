// Graphite's standard physically based surface: metallic and roughness, normal, occlusion, emission, parallax, translucency and anisotropy, each input compiled in only while used.
// Also the "Lit" surface model: a shader with Surface "Lit" takes these passes and properties, its code in place of each "#pragma surface".
// A Surface block's Vertex code goes in place of each "#pragma surface_vertex".
// A Surface block's Lighting code supplies Graphite_BRDF to the passes marked "#pragma surface_lighting".
Shader "Graphite/Lit"
{
    Properties
    {
        _MainTex("Albedo", Texture2D) = "white" {} [Header("Surface Inputs")]
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTexUV("Albedo UV Set", Integer) = 0 [Enum(UV0, UV1)]
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
        _Metallic("Metallic", Float) = 0.0 [Range(0, 1)]
        _Roughness("Roughness", Float) = 1.0 [Range(0, 1)]
        _SurfaceTex("Surface (G Roughness, B Metallic)", Texture2D) = "white" {} [Keyword(_SURFACEMAP)]
        _SurfaceTexUV("Surface UV Set", Integer) = 0 [Enum(UV0, UV1)] [ShowIf(_SURFACEMAP)]
        _NormalTex("Normal Map", Texture2D) = "normal" {} [Keyword(_NORMALMAP)] [Normal]
        _NormalScale("Normal Scale", Float) = 1.0 [ShowIf(_NORMALMAP)]
        _NormalTexUV("Normal UV Set", Integer) = 0 [Enum(UV0, UV1)] [ShowIf(_NORMALMAP)]
        _OcclusionTex("Occlusion (R)", Texture2D) = "white" {} [Keyword(_OCCLUSIONMAP)]
        _OcclusionStrength("Occlusion Strength", Float) = 1.0 [Range(0, 1)] [ShowIf(_OCCLUSIONMAP)]
        _OcclusionTexUV("Occlusion UV Set", Integer) = 0 [Enum(UV0, UV1)] [ShowIf(_OCCLUSIONMAP)]

        _Emission("Emission", Integer) = 0 [Toggle(_EMISSION)] [Header("Emission")]
        _EmissionTex("Emission Map", Texture2D) = "white" {} [ShowIf(_EMISSION)]
        _EmissiveColor("Emissive Color", Color) = (1.0, 1.0, 1.0, 1.0) [ShowIf(_EMISSION)]
        _EmissionIntensity("Emission Intensity", Float) = 1.0 [ShowIf(_EMISSION)]
        _EmissionTexUV("Emission UV Set", Integer) = 0 [Enum(UV0, UV1)] [ShowIf(_EMISSION)]

        _ParallaxMap("Height Map (G)", Texture2D) = "black" {} [Keyword(_PARALLAXMAP)] [Header("Detail")]
        _Parallax("Height Scale", Float) = 0.0 [Range(0, 0.2)] [ShowIf(_PARALLAXMAP)]
        _ParallaxSteps("Parallax Steps", Integer) = 16 [Range(1, 64)] [ShowIf(_PARALLAXMAP)]
        _Anisotropy("Anisotropy", Float) = 0.0 [Range(-1, 1)] [Toggle(_ANISOTROPIC)]
        _AnisoDirectionMap("Anisotropy Direction (RG)", Texture2D) = "normal" {} [ShowIf(_ANISOTROPIC)]

        _TranslucencyStrength("Translucency", Float) = 0.0 [Range(0, 1)] [Toggle(_TRANSLUCENCY)] [Header("Translucency")]
        _TranslucencyMap("Translucency (B), Occlusion (G)", Texture2D) = "white" {} [ShowIf(_TRANSLUCENCY)]
        _ScatteringPower("Scattering Power", Float) = 0.0 [ShowIf(_TRANSLUCENCY)]
        _ScatteringDistortion("Scattering Distortion", Float) = 0.5 [ShowIf(_TRANSLUCENCY)]
        _ScatteringScale("Scattering Scale", Float) = 1.0 [ShowIf(_TRANSLUCENCY)]
        _InstanceTint("Instance Tint", Integer) = 0 [Toggle(_INSTANCE_TINT)]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #pragma variant _NORMALMAP
            #pragma variant _SURFACEMAP
            #pragma variant _OCCLUSIONMAP
            #pragma variant _EMISSION
            #pragma variant _PARALLAXMAP
            #pragma variant _TRANSLUCENCY
            #pragma variant _ANISOTROPIC
            #pragma surface_lighting
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_DEPTH
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Debug"
        Tags { "LightMode" = "Debug" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_STANDARD_DEBUG
            #pragma variant _NORMALMAP
            #pragma variant _SURFACEMAP
            #pragma variant _OCCLUSIONMAP
            #pragma variant _EMISSION
            #pragma variant _PARALLAXMAP
            #pragma variant _TRANSLUCENCY
            #pragma variant _ANISOTROPIC
            #pragma variant global GRAPHITE_DEBUG UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY
            #pragma surface_lighting
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
            #pragma surface_vertex
            #include "Graphite/Standard/StandardVertex.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #pragma surface
            #include "Graphite/Standard/StandardFragment.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
