// Graphite's standard unlit surface: a texture times a tint and the vertex colour, fogged.
// Also the "Unlit" surface model: a shader with Surface "Unlit" takes these passes and properties, its code in place of each "#pragma surface".
Shader "Graphite/Unlit"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTexUV("UV Set", Integer) = 0 [Enum(UV0, UV1)]
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
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
            #define GRAPHITE_STANDARD_UNLIT
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
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
            #define GRAPHITE_STANDARD_UNLIT
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
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
            #define GRAPHITE_STANDARD_UNLIT
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
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
            #pragma variant global GRAPHITE_DEBUG UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY
            #define GRAPHITE_STANDARD_UNLIT
            #include "Graphite/Standard/Standard.glsl"
        }
        Vertex
        {
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
