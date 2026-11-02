Shader "Graphite/Sky/Procedural"
{
    Properties
    {
        _SunSize("Sun Size", Float) = 1.0 [Range(0, 10)]
        _SunGlow("Sun Glow", Float) = 1.0 [Range(0, 5)]
        intensity("Intensity", Float) = 1.0 [Range(0, 8)]
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _GroundColor("Ground", Color) = (0.25, 0.25, 0.25, 1.0)
    }

    Surface "Sky"
    {
        GLSLPROGRAM
        #include "Graphite/Skies.glsl"
        uniform float _SunSize;
        uniform float _SunGlow;
        uniform float intensity;
        uniform vec4 _Tint;
        uniform vec4 _GroundColor;

        vec3 Sky(vec3 direction)
        {
            return Graphite_ProceduralSky(direction, Graphite_SunDirection(), Graphite_SunIlluminance(), _SunSize, _SunGlow, intensity, _Tint.rgb, _GroundColor.rgb);
        }
        ENDGLSL
    }
}
