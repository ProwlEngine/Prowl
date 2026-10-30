Shader "Graphite/Sky/Cubemap"
{
    Properties
    {
        _Cubemap("Cubemap", TextureCubemap) = "gray" {}
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Exposure("Exposure", Float) = 1.0 [Range(0, 8)]
        _Rotation("Rotation", Float) = 0.0 [Range(0, 360)]
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
            uniform samplerCube _Cubemap;
            uniform vec4 _Tint;
            uniform float _Exposure;
            uniform float _Rotation;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                vec3 color = texture(_Cubemap, Graphite_RotateSkyY(Graphite_SkyDirection(), -_Rotation)).rgb;
                outColor = vec4(Graphite_SaturateHalf(color * _Tint.rgb * (_Exposure * Graphite_Exposure())), 1.0);
            }
        }
        ENDGLSL
    }
}
