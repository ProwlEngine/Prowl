// What a draw shows while its shader compiles or after it failed: flat grey, no material, no lighting.
Shader "Graphite/Fallback"
{
    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"
            void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
        }
        Fragment
        {
            #include "Graphite/Passes.glsl"
            layout(location = 0) out vec4 finalColor;
            void main()
            {
                Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), 1.0, 0.0);
                finalColor = vec4(0.5, 0.5, 0.5, 1.0);
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
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
        Name "ShadowCaster"
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
