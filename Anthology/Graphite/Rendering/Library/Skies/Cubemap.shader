Shader "Graphite/Sky/Cubemap"
{
    Properties
    {
        _Cubemap("Cubemap", TextureCubemap) = "gray" {}
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        intensity("Intensity", Float) = 1.0 [Range(0, 8)]
        _Rotation("Rotation", Float) = 0.0 [Range(0, 360)]
    }

    Surface "Sky"
    {
        GLSLPROGRAM
        #include "Graphite/Skies.glsl"
        uniform samplerCube _Cubemap;
        uniform vec4 _Tint;
        uniform float intensity;
        uniform float _Rotation;

        vec3 Sky(vec3 direction)
        {
            return texture(_Cubemap, Graphite_RotateSkyY(direction, -_Rotation)).rgb * _Tint.rgb * intensity;
        }
        ENDGLSL
    }
}
