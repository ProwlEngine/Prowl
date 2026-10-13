Shader "Hidden/Gizmos"
{
    Properties
    {
        _MainTex("Icon", Texture2D) = "white" {}
        _Color("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Occluded("Occluded", Integer) = 0 [Toggle(GIZMO_OCCLUDED)] [HideInInspector]
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "4000" }
        Cull Off
        ZWrite Off
        ZTest GreaterEqual
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Shared
        {
            // The occluded draw of a gizmo, behind geometry, faded; a child material turns it on and tests depth Less
            #pragma variant GIZMO_OCCLUDED
        }
        Vertex
        {
            #include "Prowl/GizmoCore.glsl"
        }
        Fragment
        {
            #include "Prowl/GizmoCore.glsl"
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
