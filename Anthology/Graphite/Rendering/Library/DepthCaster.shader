// The shadow caster of materials without a ShadowCaster pass, depth alone, culled as the material's Forward pass is
Shader "Graphite/DepthCaster"
{
    Pass
    {
        Tags { "LightMode" = "ShadowCaster" }
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"
            void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
        }
        Fragment
        {
            void main() { }
        }
        ENDGLSL
    }
}
