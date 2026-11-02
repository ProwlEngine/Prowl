// Graphite's standard unlit surface: it emits base_color times its map and the vertex colour at emission_luminance nits,
// exposed like every other light, fogged.
// Also the "Unlit" surface model: a shader with Surface "Unlit" takes these passes and properties, its code in place of each "#pragma surface".
// A Surface block's Vertex code goes in place of each "#pragma surface_vertex".
Shader "Graphite/Unlit"
{
    Properties
    {
        base_color("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        base_color_map("Base Color Map", Texture2D) = "white" {} [Map]
        emission_luminance("Emission Luminance", Float) = 1.0
        alpha_cutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        GLSLPROGRAM
        Shared
        {
            #define GRAPHITE_STANDARD_FORWARD
            #include "Graphite/Standard/UnlitSurface.glsl"
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
            #include "Graphite/Standard/UnlitSurface.glsl"
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
            #define GRAPHITE_STANDARD_SHADOW
            #include "Graphite/Standard/UnlitSurface.glsl"
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
            #pragma variant global GRAPHITE_DEBUG UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY
            #include "Graphite/Standard/UnlitSurface.glsl"
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
