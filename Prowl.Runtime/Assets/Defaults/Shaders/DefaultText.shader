Shader "UI/Text"
{
    Properties
    {
        _MainTex("SDF Atlas", Texture2D) = "white"
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector2) = (1.0, 1.0)
        _Offset("Offset", Vector2) = (0.0, 0.0)
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
        Cull Off
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Shared
        {
            #define PROWL_UI_MATERIAL
            #define PROWL_UI_TEXT
        }
        Vertex
        {
            #include "Prowl/UICore.glsl"
        }
        Fragment
        {
            #include "Prowl/UICore.glsl"
        }
        ENDGLSL
    }
}
