Shader "Graphite/Sky/Procedural"
{
    Pass
    {
        Tags { "LightMode" = "Sky" }
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Sky.glsl"
            void main() { }
        }
        Fragment
        {
            #include "Graphite/Sky.glsl"
            #include "Graphite/Skies.glsl"
            layout(location = 0) out vec4 outColor;
            void main()
            {
                outColor = vec4(Graphite_ProceduralSky(Graphite_SkyDirection(), Graphite_SunDirection()), 1.0);
            }
        }
        ENDGLSL
    }
}
