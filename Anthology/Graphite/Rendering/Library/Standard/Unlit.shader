// Graphite's standard unlit surface: a texture times a tint and the vertex colour, fogged.
Shader "Graphite/Unlit"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTexUV("UV Set", Integer) = 0 [Enum(UV0, UV1)]
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
        _Tiling("Tiling", Vector2) = (1.0, 1.0)
        _Offset("Offset", Vector2) = (0.0, 0.0)
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #define GRAPHITE_STANDARD_UNLIT
        }
        Vertex
        {
            #include "Graphite/Standard/Standard.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Standard/Standard.glsl"
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
            #define GRAPHITE_STANDARD_UNLIT
        }
        Vertex
        {
            #include "Graphite/Standard/Standard.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Standard/Standard.glsl"
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
            #define GRAPHITE_STANDARD_UNLIT
        }
        Vertex
        {
            #include "Graphite/Standard/Standard.glsl"
            void main() { Graphite_StandardVertex(); }
        }
        Fragment
        {
            #include "Graphite/Standard/Standard.glsl"
            void main() { Graphite_StandardFragment(); }
        }
        ENDGLSL
    }
}
