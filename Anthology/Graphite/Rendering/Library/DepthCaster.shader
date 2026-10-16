Shader "Graphite/DepthCaster"
{
    Pass
    {
        Tags { "LightMode" = "ShadowCaster" }
        Cull Back
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

    Pass
    {
        Tags { "LightMode" = "ShadowCaster" }
        Cull Front
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

    Pass
    {
        Tags { "LightMode" = "ShadowCaster" }
        Cull Off
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
