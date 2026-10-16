Shader "Hidden/Invalid"
{
    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        Cull Off
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
                finalColor = vec4(1.0, 0.0, 1.0, 1.0);
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
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
