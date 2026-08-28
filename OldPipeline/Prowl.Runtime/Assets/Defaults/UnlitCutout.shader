Shader "Default/Cutout/Unlit"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _MainTexUV ("Texture UV Set", Int) = 0
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)

    _AlphaCutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
}

Pass "Forward"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_UNLIT
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
    Cull Back
    ZWrite On
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_UNLIT
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
    Cull Back
    GLSLPROGRAM

        Shared
        {
            #define PROWL_ALPHA_CUTOUT
            #define PROWL_UNLIT
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
