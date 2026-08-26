Shader "Default/Cutout/Standard Double Sided"

Properties
{
    _MainTex ("Albedo", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)

    _NormalTex ("Normal Map", Texture2D) = "normal"
    _NormalScale ("Normal Scale", Float) = 1.0

    _SurfaceTex ("Surface (G Roughness, B Metallic)", Texture2D) = "surface"
    _Metallic ("Metallic", Range(0.0, 1.0)) = 1.0
    _Roughness ("Roughness", Range(0.0, 1.0)) = 1.0

    _OcclusionTex ("Occlusion (R)", Texture2D) = "white"
    _OcclusionStrength ("Occlusion Strength", Range(0.0, 1.0)) = 1.0

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissiveColor ("Emissive Color", Color) = (1.0, 1.0, 1.0, 1.0)
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

    _AlphaCutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

    _ParallaxMap ("Height Map (G)", Texture2D) = "black"
    _Parallax ("Height Scale", Float) = 0.0
    _ParallaxSteps ("POM Steps", Int) = 16

    _TranslucencyMap ("Translucency (B), Occlusion (G)", Texture2D) = "white"
    _TranslucencyStrength ("Translucency Strength", Float) = 0.0
    _ScatteringPower ("Scattering Power", Float) = 0.0
    _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
    _ScatteringScale ("Scattering Scale", Float) = 1.0
}

Pass "Forward"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Off
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_DOUBLE_SIDED
            #define PROWL_PASS_FORWARD
        }

        Vertex
        {
            #define PROWL_VERTEX_STAGE
            #include "StandardCore"

            void main() { ProwlVertex(); }
        }

        Fragment
        {
            #define PROWL_FRAGMENT_STAGE
            #include "StandardCore"

            void main() { ProwlFragment(); }
        }

    ENDGLSL
}

Pass "Prepass"
{
    Tags { "LightMode" = "Prepass" }
    Cull Off
    ZWrite On
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_DOUBLE_SIDED
            #define PROWL_PASS_PREPASS
        }

        Vertex
        {
            #define PROWL_VERTEX_STAGE
            #include "StandardCore"

            void main() { ProwlVertex(); }
        }

        Fragment
        {
            #define PROWL_FRAGMENT_STAGE
            #include "StandardCore"

            void main() { ProwlFragment(); }
        }

    ENDGLSL
}

Pass "ShadowCaster"
{
    Tags { "LightMode" = "ShadowCaster" }
    Cull Off
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_DOUBLE_SIDED
            #define PROWL_PASS_SHADOW
        }

        Vertex
        {
            #define PROWL_VERTEX_STAGE
            #include "StandardCore"

            void main() { ProwlVertex(); }
        }

        Fragment
        {
            #define PROWL_FRAGMENT_STAGE
            #include "StandardCore"

            void main() { ProwlFragment(); }
        }

    ENDGLSL
}
