Shader "Graphite/Sky/Procedural"
{
    Properties
    {
        _SunSize("Sun Size", Float) = 1.0 [Range(0, 10)]
        _SunGlow("Sun Glow", Float) = 1.0 [Range(0, 5)]
        _Exposure("Exposure", Float) = 1.0 [Range(0, 8)]
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _GroundColor("Ground", Color) = (0.25, 0.25, 0.25, 1.0)
    }

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
            uniform float _SunSize;
            uniform float _SunGlow;
            uniform float _Exposure;
            uniform vec4 _Tint;
            uniform vec4 _GroundColor;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                vec3 color = Graphite_ProceduralSky(Graphite_SkyDirection(), Graphite_SunDirection(), _SunSize, _SunGlow, _Exposure, _Tint.rgb, _GroundColor.rgb);
                outColor = vec4(color, 1.0);
            }
        }
        ENDGLSL
    }
}
